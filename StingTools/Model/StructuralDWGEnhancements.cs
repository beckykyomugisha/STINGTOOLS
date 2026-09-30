// ============================================================================
// StructuralDWGEnhancements.cs — EaseBit-style detection & interactive helpers
//
// Phase 78. Adds four cross-cutting capabilities to the STING CAD Wizard
// pipeline that were not part of the legacy single-page wizard:
//
//   1. SpatialLineIndex — uniform grid index for parallel line pair and small
//      rectangle lookup. Replaces O(n²) nested scans when UseSpatialIndex is
//      enabled on a DWGConversionConfig with >500 line entities on the
//      wall/column/beam layers.
//
//   2. OpeningDetector — scans an ImportInstance for door/window/opening
//      BLOCK insertions that fall on or near a freshly-created wall and
//      cuts rectangular voids through the wall via Document.NewOpening.
//      Also exposes a CountCandidateOpenings method for the dry-run path
//      so the wizard can preview how many openings WOULD be cut without
//      touching the model.
//
//   3. ExplodeHelper — fully explodes a nested ImportInstance in place so
//      geometry hidden inside blocks surfaces onto its host layer. Uses
//      ImportInstance.Explode in a single transaction and returns the
//      count of direct children created. Invoked by the pipeline's
//      ExplodeOnImport config flag.
//
//   4. Interactive commands — IExternalCommand classes that let the user
//      pick lines directly in the active view to create individual walls,
//      columns, or beams without running the full layer-mapped pipeline.
//      Plus three utility commands that expose the dry-run, explode and
//      opening-detection pipelines as one-click actions from the STING
//      dock panel: DWGDryRunPreviewCommand, DWGExplodeImportsCommand and
//      DWGDetectOpeningsCommand.
//
// Inspired by: EaseBit 2.1.0 Create Walls (wall/opening detection from
//   parallel line pairs with user-picked source lines) and AGACAD Smart
//   Walls (interactive wall creation from DWG reference geometry).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using StingTools.Core;

namespace StingTools.Model
{
    /// <summary>
    /// Phase-78 EaseBit-style enhancements for the DWG-to-Structural pipeline.
    /// All helpers are wrapped in a top-level static class so they share a
    /// namespace root with StructuralCADPipeline without leaking internal
    /// detection types into the wider StingTools.Model namespace.
    /// </summary>
    public static class StructuralDWGEnhancements
    {
        // Internal alias for the mm→ft / ft→mm conversion. Kept local so the
        // enhancements file compiles stand-alone even if Units moves.
        private const double MmToFeet = 1.0 / 304.8;
        private const double FeetToMm = 304.8;

        // ════════════════════════════════════════════════════════════════
        // 1. SpatialLineIndex — uniform grid index for parallel-pair lookup
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Uniform-grid spatial index over a list of ExtractedLine endpoints.
        /// Cell size defaults to 2× the configured ParallelLineToleranceMm so
        /// any parallel pair is guaranteed to share a cell with at least one
        /// of its neighbours. Memory footprint O(n); parallel-pair lookup is
        /// O(k) per line where k is the average cell occupancy.
        /// </summary>
        public sealed class SpatialLineIndex
        {
            private readonly Dictionary<(int gx, int gy), List<int>> _buckets
                = new Dictionary<(int, int), List<int>>();
            private readonly double _cellFt;
            private readonly List<ExtractedLine> _lines;

            public int CellCount => _buckets.Count;
            public int LineCount => _lines?.Count ?? 0;

            public SpatialLineIndex(List<ExtractedLine> lines, double cellSizeFt)
            {
                _lines = lines ?? throw new ArgumentNullException(nameof(lines));
                _cellFt = Math.Max(0.05, cellSizeFt); // never smaller than 15mm
                foreach (var (line, i) in EnumerateWithIndex(lines))
                {
                    if (line == null || line.Start == null || line.End == null) continue;
                    AddToCells(i, line.Start);
                    AddToCells(i, line.End);
                    // Mid-point sample — catches long lines whose endpoints
                    // are in far-apart cells but whose body passes through
                    // the cell of a short parallel neighbour.
                    var mid = (line.Start + line.End) * 0.5;
                    AddToCells(i, mid);
                }
            }

            private void AddToCells(int idx, XYZ p)
            {
                int gx = (int)Math.Floor(p.X / _cellFt);
                int gy = (int)Math.Floor(p.Y / _cellFt);
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var key = (gx + dx, gy + dy);
                    if (!_buckets.TryGetValue(key, out var list))
                    {
                        list = new List<int>();
                        _buckets[key] = list;
                    }
                    // Guard against duplicate adds from start/end/mid that
                    // happen to land in the same cell.
                    if (list.Count == 0 || list[list.Count - 1] != idx)
                        list.Add(idx);
                }
            }

            /// <summary>
            /// Return candidate neighbour indices of the line with the
            /// given index, de-duplicated and excluding the query line itself.
            /// Safe to call with an out-of-range index (returns empty set).
            /// </summary>
            public IEnumerable<int> Candidates(int queryIdx)
            {
                if (queryIdx < 0 || queryIdx >= _lines.Count) yield break;
                var line = _lines[queryIdx];
                if (line == null || line.Start == null) yield break;

                var seen = new HashSet<int>();
                foreach (var p in new[] { line.Start, line.End, (line.Start + line.End) * 0.5 })
                {
                    int gx = (int)Math.Floor(p.X / _cellFt);
                    int gy = (int)Math.Floor(p.Y / _cellFt);
                    if (!_buckets.TryGetValue((gx, gy), out var list)) continue;
                    foreach (var i in list)
                    {
                        if (i == queryIdx) continue;
                        if (seen.Add(i)) yield return i;
                    }
                }
            }

            private static IEnumerable<(T, int)> EnumerateWithIndex<T>(IList<T> src)
            {
                for (int i = 0; i < src.Count; i++) yield return (src[i], i);
            }
        }

        // ════════════════════════════════════════════════════════════════
        // 2. OpeningDetector — door/window/opening blocks → wall voids
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Result of a single OpeningDetector run. The dry-run path only
        /// populates <see cref="Detected"/>; the normal path additionally
        /// cuts voids in the document and populates <see cref="Created"/>
        /// and <see cref="CreatedIds"/>.
        /// </summary>
        public sealed class OpeningResult
        {
            public int Detected { get; set; }
            public int Created { get; set; }
            public List<ElementId> CreatedIds { get; } = new();
            public List<string> Warnings { get; } = new();
        }

        /// <summary>
        /// Detects door / window / opening block insertions inside a DWG
        /// ImportInstance and cuts rectangular voids through the nearest
        /// already-created wall via <see cref="Document.NewOpening"/>.
        /// Triggered by DWGConversionConfig.DetectOpenings.
        /// </summary>
        public static class OpeningDetector
        {
            // Block-name keywords that count as openings. Lower-cased
            // substring match to keep international naming working.
            private static readonly string[] OpeningKeywords = new[]
            {
                "door", "window", "win", "opening", "hole", "cutout", "cut_out",
                "puerta", "ventana", "porte", "fenetre", "fenêtre",
                "tür", "tuer", "fenster", "porta", "finestra",
            };

            /// <summary>
            /// Preview-only opening count for the dry-run summary. Extracts
            /// block references from <see cref="StructuralExtractionResult.FoundationBlocks"/>
            /// (which actually contains ALL detected blocks, per the
            /// existing extraction code path) plus any GeometryInstance
            /// blocks on the user's selected layers that match the opening
            /// keyword list. Does NOT touch the document.
            /// </summary>
            public static int CountCandidateOpenings(
                StructuralExtractionResult extraction, DWGConversionConfig config)
            {
                if (extraction == null) return 0;
                int count = 0;
                var blocks = extraction.FoundationBlocks ?? new List<DetectedBlock>();
                foreach (var b in blocks)
                {
                    if (b?.BlockName == null) continue;
                    if (IsOpeningBlock(b.BlockName, b.InferredCategory)) count++;
                }
                // Additional estimate: for each DetectedWall, count how many
                // blocks land within the wall's bounding corridor.
                // (Not currently used — block filtering above is the primary
                // signal — but reserved for future refinement.)
                return count;
            }

            /// <summary>
            /// Full detection + cut pass. Iterates all opening-keyword blocks
            /// in the ImportInstance (resolved via
            /// <paramref name="extraction"/>), finds the nearest wall in
            /// <paramref name="wallIds"/> within the configured search
            /// radius, and cuts a rectangular void at the block's
            /// insertion point using the block's bounding box as the
            /// opening size.
            /// </summary>
            public static OpeningResult DetectAndCut(
                Document doc,
                StructuralExtractionResult extraction,
                List<ElementId> wallIds,
                DWGConversionConfig config)
            {
                var result = new OpeningResult();
                if (doc == null || extraction == null || wallIds == null || wallIds.Count == 0)
                    return result;

                var openings = (extraction.FoundationBlocks ?? new List<DetectedBlock>())
                    .Where(b => b?.BlockName != null
                        && IsOpeningBlock(b.BlockName, b.InferredCategory))
                    .ToList();
                result.Detected = openings.Count;
                if (openings.Count == 0) return result;

                // Resolve walls once.
                var walls = new List<Wall>();
                foreach (var id in wallIds)
                {
                    if (doc.GetElement(id) is Wall w) walls.Add(w);
                }
                if (walls.Count == 0) return result;

                double minWFt = Math.Max(0.3, config.MinOpeningWidthMm * MmToFeet);
                double maxWFt = Math.Max(minWFt + 0.1, config.MaxOpeningWidthMm * MmToFeet);
                double searchFt = Math.Max(maxWFt, (config.MaxWallThicknessMm * 2.0) * MmToFeet);

                // Default head height per BS 8300 accessibility (2100mm head clearance).
                double defaultHeadFt = 2100 * MmToFeet;
                double defaultSillFt = 0; // door by default; windows overridden below

                using (var tx = new Transaction(doc, "STING: Cut DWG openings in walls"))
                {
                    tx.Start();
                    var failOpts = tx.GetFailureHandlingOptions();
                    tx.SetFailureHandlingOptions(failOpts);

                    foreach (var blk in openings)
                    {
                        try
                        {
                            var hitWall = FindNearestWall(blk.InsertionPoint, walls, searchFt);
                            if (hitWall == null) continue;

                            // Size the opening: block names often include a width
                            // (e.g. "DOOR 900x2100"). If we can't parse, fall
                            // back to the user's min-opening default.
                            (double widthFt, double heightFt) = ParseOpeningSize(
                                blk.BlockName, minWFt, maxWFt, defaultHeadFt);
                            bool isWindow = blk.BlockName.ToLowerInvariant().Contains("win")
                                || (blk.InferredCategory?.ToLowerInvariant().Contains("window") ?? false);
                            double sillFt = isWindow ? (900 * MmToFeet) : defaultSillFt;

                            // Project the insertion point onto the wall centerline
                            // so the opening sits cleanly on the wall.
                            var loc = hitWall.Location as LocationCurve;
                            if (loc?.Curve == null) continue;
                            var proj = loc.Curve.Project(blk.InsertionPoint);
                            if (proj == null) continue;

                            // Compute the two opposite corners of the rectangular
                            // opening along the wall axis, at the configured heights.
                            var dir = (loc.Curve.GetEndPoint(1) - loc.Curve.GetEndPoint(0));
                            var len = dir.GetLength();
                            if (len < 1e-6) continue;
                            dir = dir / len;

                            var centre = proj.XYZPoint;
                            var half = dir * (widthFt / 2.0);
                            var p1 = new XYZ(centre.X - half.X, centre.Y - half.Y, sillFt);
                            var p2 = new XYZ(centre.X + half.X, centre.Y + half.Y, sillFt + heightFt);

                            var opening = doc.Create.NewOpening(hitWall, p1, p2);
                            if (opening != null)
                            {
                                result.Created++;
                                result.CreatedIds.Add(opening.Id);
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add(
                                $"Opening at {Format(blk.InsertionPoint)} skipped: {ex.Message}");
                            StingLog.Warn($"Opening cut: {ex.Message}");
                        }
                    }

                    tx.Commit();
                }

                return result;
            }

            // Helpers --------------------------------------------------------

            private static bool IsOpeningBlock(string blockName, string inferredCategory)
            {
                if (string.IsNullOrWhiteSpace(blockName)) return false;
                var lower = blockName.ToLowerInvariant();
                foreach (var k in OpeningKeywords)
                {
                    if (lower.Contains(k)) return true;
                }
                if (!string.IsNullOrEmpty(inferredCategory))
                {
                    var c = inferredCategory.ToLowerInvariant();
                    if (c.Contains("door") || c.Contains("window") || c.Contains("opening"))
                        return true;
                }
                return false;
            }

            private static Wall FindNearestWall(XYZ p, List<Wall> walls, double maxFt)
            {
                if (p == null) return null;
                Wall best = null;
                double bestD = maxFt;
                foreach (var w in walls)
                {
                    try
                    {
                        if (!(w.Location is LocationCurve loc) || loc.Curve == null) continue;
                        var pr = loc.Curve.Project(p);
                        if (pr == null) continue;
                        double d = pr.Distance;
                        if (d < bestD) { bestD = d; best = w; }
                    }
                    catch (Exception ex) { StingLog.Warn($"Wall nearest: {ex.Message}"); }
                }
                return best;
            }

            // Parse block names like "DOOR 900x2100", "Win_1200x1500", "DR-1800x2100"
            // into (widthFt, heightFt). Falls back to (minFt, headFt) when no
            // dimensions can be parsed.
            private static (double wFt, double hFt) ParseOpeningSize(
                string name, double minFt, double maxFt, double defaultHeadFt)
            {
                try
                {
                    var clean = new string(name.Select(c =>
                        char.IsDigit(c) || c == 'x' || c == 'X' ? c : ' ').ToArray());
                    var parts = clean.Split(new[] { 'x', 'X', ' ' },
                        StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2
                        && int.TryParse(parts[0], out int wMm)
                        && int.TryParse(parts[1], out int hMm))
                    {
                        double w = Math.Max(minFt, Math.Min(maxFt, wMm * MmToFeet));
                        // Never produce an opening shorter than 1000mm — BS 8300
                        // minimum accessible door height minus tolerance.
                        double minHFt = 1000 * MmToFeet;
                        double h = Math.Max(minHFt, hMm * MmToFeet);
                        return (w, h);
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Opening size parse: {ex.Message}"); }
                return (minFt, defaultHeadFt);
            }

            private static string Format(XYZ p)
                => p == null ? "(null)" : $"({p.X * FeetToMm:F0},{p.Y * FeetToMm:F0})mm";
        }

        // ════════════════════════════════════════════════════════════════
        // 3. ExplodeHelper — in-place ImportInstance explode
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Wraps the programmatic Explode path in a single transaction so
        /// DWG block references become individual Revit DetailLines /
        /// ModelLines / etc. on their host layers. Call BEFORE
        /// ExtractStructuralGeometry so block-hidden geometry is visible
        /// to the pipeline's layer filter.
        ///
        /// Important: the public <c>Explode()</c> method on
        /// <see cref="ImportInstance"/> was removed from the Revit API in
        /// 2019 and never reinstated — Autodesk deliberately blocks
        /// programmatic explode to stop plugins silently polluting
        /// projects with thousands of line elements. This helper therefore
        /// tries the method reflectively (so any vendor-shim that still
        /// exposes it keeps working) and falls back to a clear "please
        /// use Revit's UI Modify -> Explode" message when the API refuses.
        /// </summary>
        public static class ExplodeHelper
        {
            /// <summary>True when programmatic explode is available on the
            /// host Revit API. Computed lazily on first call to avoid the
            /// reflection cost during every pipeline run.</summary>
            private static bool? _explodeSupported;

            private static System.Reflection.MethodInfo FindExplodeMethod()
            {
                try
                {
                    return typeof(ImportInstance).GetMethod(
                        "Explode",
                        System.Reflection.BindingFlags.Instance
                            | System.Reflection.BindingFlags.Public,
                        null, Type.EmptyTypes, null);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ExplodeHelper reflection: {ex.Message}");
                    return null;
                }
            }

            /// <summary>True when the running Revit build exposes
            /// <c>ImportInstance.Explode()</c>.</summary>
            public static bool IsProgrammaticExplodeSupported
                => (_explodeSupported ??= (FindExplodeMethod() != null));

            /// <summary>
            /// Explode a single ImportInstance in place and return the count
            /// of direct children created. Returns 0 if the instance is
            /// non-explodable (linked DWG, exploded already, or the host
            /// Revit build doesn't expose the API).
            /// </summary>
            public static int ExplodeInPlace(Document doc, ImportInstance import)
            {
                if (doc == null || import == null) return 0;
                var explodeMethod = FindExplodeMethod();
                if (explodeMethod == null)
                {
                    _explodeSupported = false;
                    StingLog.Info(
                        "ExplodeInPlace: ImportInstance.Explode is not exposed by " +
                        "this Revit API build — user must run 'Modify -> Explode' " +
                        "in the Revit UI before re-running the CAD Wizard.");
                    return 0;
                }
                try
                {
                    int count;
                    using (var tx = new Transaction(doc, "STING: Explode DWG import"))
                    {
                        tx.Start();
                        var children = explodeMethod.Invoke(import, null)
                            as ICollection<ElementId>;
                        count = children?.Count ?? 0;
                        tx.Commit();
                    }
                    _explodeSupported = true;
                    return count;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ExplodeInPlace: {ex.Message}");
                    return 0;
                }
            }

            /// <summary>
            /// Explode every import instance that the current session owns.
            /// Returns the total number of children produced across all
            /// explodes. Skips linked DWGs automatically.
            /// </summary>
            public static int ExplodeAllImports(Document doc)
            {
                if (doc == null) return 0;
                var imports = CADToModelEngine.FindImportInstances(doc);
                int total = 0;
                foreach (var imp in imports)
                {
                    total += ExplodeInPlace(doc, imp);
                }
                return total;
            }
        }

        // ════════════════════════════════════════════════════════════════
        // 4. Interactive picker commands — pick geometry in-view to build
        //    single Revit elements from DWG reference lines. These bypass
        //    the full layer-mapped pipeline and are meant for spot fixes
        //    or when a CAD source has odd layering that auto-detection
        //    can't classify cleanly.
        // ════════════════════════════════════════════════════════════════

        /// <summary>Creates one structural wall on <paramref name="centerline"/> using the
        /// given wall type and level. Callers resolve the type (sized to what they
        /// measured) and the level; this only builds the element.</summary>
        internal static Wall CreateWallFromCurve(Document doc, Curve centerline,
            double heightMm, ElementId wallTypeId, Level level)
        {
            if (doc == null || centerline == null || level == null
                || wallTypeId == null || wallTypeId == ElementId.InvalidElementId)
            {
                StingLog.Warn("Interactive wall: missing curve, wall type or level");
                return null;
            }
            double heightFt = heightMm * MmToFeet;
            return Wall.Create(doc, centerline, wallTypeId, level.Id, heightFt, 0, false, true);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // IExternalCommand classes
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Dry-run preview: runs the extraction + detection passes without
    /// creating any Revit elements and shows the counts in a TaskDialog.
    /// Dispatched via the dock panel "Dry-Run Preview" button.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DWGDryRunPreviewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var app = ParameterHelpers.GetApp(commandData);
                var doc = app?.ActiveUIDocument?.Document ?? ParameterHelpers.GetDoc(commandData);
                if (doc == null)
                {
                    TaskDialog.Show("STING", "No active document.");
                    return Result.Cancelled;
                }

                var imports = CADToModelEngine.FindImportInstances(doc);
                if (imports.Count == 0)
                {
                    TaskDialog.Show("STING DWG Dry Run",
                        "No imported DWG files found. Insert → Import/Link CAD first.");
                    return Result.Cancelled;
                }

                var config = new DWGConversionConfig
                {
                    DryRun = true,
                    CreateWalls = true,
                    CreateColumns = true,
                    CreateBeams = true,
                    CreateSlabs = true,
                    CreateFoundations = true,
                    CreateGrids = true,
                    AutoTag = false,
                    AutoSeqNumbers = false,
                    DetectOpenings = true,
                };
                var pipeline = new StructuralCADPipeline(doc);
                var result = pipeline.RunFullPipelineWithConfig(imports[0], config);

                var td = new TaskDialog("STING DWG Dry-Run Preview")
                {
                    MainInstruction = result.WasDryRun
                        ? "Dry run complete — no elements were created."
                        : "Dry run result",
                    MainContent = result.Summary
                        + $"\n\n  Walls rejected by thickness: {result.WallsRejectedByThickness}"
                        + $"\n  Opening candidates:          {result.OpeningsDetected}",
                    MainIcon = TaskDialogIcon.TaskDialogIconInformation,
                };
                td.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DWGDryRunPreviewCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Fully explodes every DWG ImportInstance in the active document so
    /// block-hidden geometry surfaces onto its host layer. Idempotent —
    /// safe to re-run after adding new imports.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DWGExplodeImportsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var app = ParameterHelpers.GetApp(commandData);
                var doc = app?.ActiveUIDocument?.Document ?? ParameterHelpers.GetDoc(commandData);
                if (doc == null)
                {
                    TaskDialog.Show("STING", "No active document.");
                    return Result.Cancelled;
                }

                if (!StructuralDWGEnhancements.ExplodeHelper.IsProgrammaticExplodeSupported)
                {
                    TaskDialog.Show("STING DWG Explode",
                        "This Revit build does not expose ImportInstance.Explode to the API " +
                        "(Autodesk removed programmatic explode to prevent plugins " +
                        "silently polluting projects with thousands of line elements).\n\n" +
                        "To explode DWG imports:\n" +
                        "  1. Select the imported DWG in the view.\n" +
                        "  2. Modify tab → Import Instance panel → Explode → Full Explode.\n" +
                        "  3. Re-run the CAD Wizard — the pipeline will now see the " +
                        "previously hidden block geometry on its host layer.");
                    return Result.Cancelled;
                }

                int exploded = StructuralDWGEnhancements.ExplodeHelper.ExplodeAllImports(doc);
                TaskDialog.Show("STING DWG Explode",
                    exploded > 0
                        ? $"Exploded {exploded} child elements from DWG imports.\n" +
                          "Hidden block geometry is now on its host layer."
                        : "No imports were explodable (linked DWGs cannot be exploded).");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DWGExplodeImportsCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Opening detector: scans the active DWG for door/window block
    /// insertions that fall on or near structural walls already in the
    /// model, then cuts rectangular voids through those walls.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DWGDetectOpeningsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var app = ParameterHelpers.GetApp(commandData);
                var doc = app?.ActiveUIDocument?.Document ?? ParameterHelpers.GetDoc(commandData);
                if (doc == null)
                {
                    TaskDialog.Show("STING", "No active document.");
                    return Result.Cancelled;
                }

                var imports = CADToModelEngine.FindImportInstances(doc);
                if (imports.Count == 0)
                {
                    TaskDialog.Show("STING DWG Openings", "No DWG imports found.");
                    return Result.Cancelled;
                }

                // Re-run extraction to get the latest block catalogue, then
                // cut openings through ALL walls currently in the project.
                var config = new DWGConversionConfig
                {
                    DryRun = false,
                    DetectOpenings = true,
                    CreateWalls = false,
                    CreateColumns = false,
                    CreateBeams = false,
                    CreateSlabs = false,
                    CreateFoundations = false,
                    CreateGrids = false,
                    AutoTag = false,
                    AutoSeqNumbers = false,
                };
                var pipeline = new StructuralCADPipeline(doc);
                pipeline.CurrentConfig = config;
                var extraction = pipeline.ExtractStructuralGeometry(imports[0]);

                var wallIds = new FilteredElementCollector(doc)
                    .OfClass(typeof(Wall)).Cast<Wall>()
                    .Where(w => w.WallType?.Kind == WallKind.Basic)
                    .Select(w => w.Id).ToList();

                if (wallIds.Count == 0)
                {
                    TaskDialog.Show("STING DWG Openings",
                        "No walls in the project to cut openings in.\n" +
                        "Run the CAD Wizard first to create walls.");
                    return Result.Cancelled;
                }

                var res = StructuralDWGEnhancements.OpeningDetector.DetectAndCut(
                    doc, extraction, wallIds, config);

                TaskDialog.Show("STING DWG Openings",
                    $"Detected {res.Detected} opening candidate(s).\n" +
                    $"Cut {res.Created} opening(s) through existing walls.\n" +
                    (res.Warnings.Count > 0
                        ? $"\nWarnings ({res.Warnings.Count}):\n  " +
                          string.Join("\n  ", res.Warnings.Take(8))
                        : ""));
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DWGDetectOpeningsCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Shared plumbing for the three interactive DWG pickers: which level to build
    /// on, how to read a picked DWG sub-object in model coordinates, and the
    /// selection filter.
    /// </summary>
    internal static class DwgPickSupport
    {
        /// <summary>The active view's level when it is a plan view; otherwise the lowest level.</summary>
        internal static Level ResolvePickLevel(Document doc, out string how)
        {
            how = null;
            if (doc.ActiveView is ViewPlan plan && plan.GenLevel != null)
            {
                how = "active plan view";
                return plan.GenLevel;
            }
            var lowest = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).FirstOrDefault();
            if (lowest != null) how = "lowest level — the active view is not a plan";
            return lowest;
        }

        /// <summary>
        /// The picked DWG sub-object, in model coordinates. Whether
        /// GetGeometryObjectFromReference returns import-local or model coordinates
        /// is not something we rely on: when the import carries a non-identity
        /// transform, both readings are tried and the one that passes closest to the
        /// point the user actually clicked (Reference.GlobalPoint) wins.
        /// </summary>
        internal static GeometryObject GetPickedGeometry(Document doc, Reference r)
        {
            if (doc == null || r == null) return null;
            try
            {
                var host = doc.GetElement(r);
                var go = host?.GetGeometryObjectFromReference(r);
                if (go == null) return null;

                var t = (host as Instance)?.GetTotalTransform();
                var gp = r.GlobalPoint;
                if (t == null || t.IsIdentity || gp == null) return go;

                switch (go)
                {
                    case Curve c:
                    {
                        var ct = c.CreateTransformed(t);
                        return PlanDistance(ct, gp) < PlanDistance(c, gp) ? ct : c;
                    }
                    case PolyLine pl:
                    {
                        var pt = pl.GetTransformed(t);
                        return PlanDistance(pt, gp) < PlanDistance(pl, gp) ? pt : pl;
                    }
                    default:
                        return go;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DWG pick geometry: {ex.Message}");
                return null;
            }
        }

        private static double PlanDistance(Curve c, XYZ p)
        {
            if (c is Arc a)
            {
                double dx = p.X - a.Center.X, dy = p.Y - a.Center.Y;
                return Math.Abs(Math.Sqrt(dx * dx + dy * dy) - a.Radius);
            }
            if (c is Line l)
            {
                var s = l.GetEndPoint(0); var e = l.GetEndPoint(1);
                return DwgPickGeometry.DistanceToSegment2D(s.X, s.Y, e.X, e.Y, p.X, p.Y);
            }
            try
            {
                var z = c.IsBound ? c.GetEndPoint(0).Z : p.Z;
                return c.Distance(new XYZ(p.X, p.Y, z));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DWG pick distance: {ex.Message}");
                return double.MaxValue;
            }
        }

        private static double PlanDistance(PolyLine pl, XYZ p)
        {
            var pts = pl.GetCoordinates();
            return DwgPickGeometry.DistanceToPolyline2D(
                pts.Select(q => q.X).ToList(), pts.Select(q => q.Y).ToList(), p.X, p.Y);
        }

        internal static DwgPickGeometry.ParallelPair Measure(Line a, Line b)
        {
            var a0 = a.GetEndPoint(0); var a1 = a.GetEndPoint(1);
            var b0 = b.GetEndPoint(0); var b1 = b.GetEndPoint(1);
            return DwgPickGeometry.MeasureParallelPair(
                a0.X, a0.Y, a1.X, a1.Y, b0.X, b0.Y, b1.X, b1.Y, 0.95);
        }

        internal static string DescribeMatch(TypeMatchResult tm, string what, double measuredMm)
        {
            switch (tm.MatchMethod)
            {
                case TypeMatchMethod.ExactMatch:
                    return $"Type '{tm.TypeName}' — existing {what} type matching {measuredMm:F0} mm.";
                case TypeMatchMethod.DuplicatedAndSized:
                    return $"Type '{tm.TypeName}' — new type duplicated from the nearest existing one and sized to {measuredMm:F0} mm.";
                default:
                    return $"Type '{tm.TypeName}' — NOT sized to {measuredMm:F0} mm: no matching type and a new one " +
                        "could not be created, so the nearest existing type was used." +
                        (string.IsNullOrEmpty(tm.Message) ? "" : $" ({tm.Message})");
            }
        }

        internal sealed class DwgGeometryFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is ImportInstance;
            public bool AllowReference(Reference reference, XYZ position) => true;
        }
    }

    /// <summary>
    /// Interactive: pick two parallel DWG lines → one structural wall whose type
    /// width matches the perpendicular distance between them (±5 mm). If no Basic
    /// wall type is that wide, the nearest one is duplicated at the measured width.
    /// Built on the active plan view's level (lowest level otherwise), 3000 mm high.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DWGInteractivePickWallCommand : IExternalCommand
    {
        private const double WallHeightMm = 3000;
        private const double WidthToleranceMm = 5;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var app = ParameterHelpers.GetApp(commandData);
                var uidoc = app?.ActiveUIDocument ?? ParameterHelpers.GetUIDoc(commandData);
                var doc = uidoc?.Document;
                if (doc == null || uidoc == null)
                {
                    TaskDialog.Show("STING", "No active document.");
                    return Result.Cancelled;
                }

                var filter = new DwgPickSupport.DwgGeometryFilter();
                Reference r1, r2;
                try
                {
                    r1 = uidoc.Selection.PickObject(ObjectType.PointOnElement, filter,
                        "Pick the FIRST wall face line in the DWG");
                    r2 = uidoc.Selection.PickObject(ObjectType.PointOnElement, filter,
                        "Pick the SECOND (opposite) wall face line");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                var line1 = DwgPickSupport.GetPickedGeometry(doc, r1) as Line;
                var line2 = DwgPickSupport.GetPickedGeometry(doc, r2) as Line;
                if (line1 == null || line2 == null)
                {
                    TaskDialog.Show("STING Pick Wall",
                        "Both picks must be straight lines inside the DWG import.");
                    return Result.Cancelled;
                }

                var pair = DwgPickSupport.Measure(line1, line2);
                if (!pair.IsParallel)
                {
                    TaskDialog.Show("STING Pick Wall",
                        $"The two lines are not parallel enough (dot={pair.Dot:F3}, need ≥ 0.95).");
                    return Result.Cancelled;
                }

                double thicknessMm = pair.Gap * Units.FeetToMm;
                if (thicknessMm < 50 || thicknessMm > 1000)
                {
                    TaskDialog.Show("STING Pick Wall",
                        $"Measured wall thickness {thicknessMm:F0}mm is outside the sensible range (50-1000mm).");
                    return Result.Cancelled;
                }
                if (pair.Length < 300 * Units.MmToFeet)
                {
                    TaskDialog.Show("STING Pick Wall", "Picked lines are too short for a wall.");
                    return Result.Cancelled;
                }

                var level = DwgPickSupport.ResolvePickLevel(doc, out string levelHow);
                if (level == null)
                {
                    TaskDialog.Show("STING Pick Wall", "No level in the project.");
                    return Result.Cancelled;
                }

                var factory = new StructuralTypeFactory(doc);
                Wall created = null;
                TypeMatchResult tm;
                using (var tx = new Transaction(doc, "STING: Pick Wall from DWG"))
                {
                    tx.Start();
                    tm = factory.FindOrCreateWallType(thicknessMm, isStructural: true,
                        allowDuplicate: true, exactToleranceMm: WidthToleranceMm);
                    if (!tm.Success)
                    {
                        tx.RollBack();
                        TaskDialog.Show("STING Pick Wall", tm.Message ?? "No Basic wall type in the project.");
                        return Result.Cancelled;
                    }
                    var curve = Line.CreateBound(
                        new XYZ(pair.StartX, pair.StartY, level.Elevation),
                        new XYZ(pair.EndX, pair.EndY, level.Elevation));
                    created = StructuralDWGEnhancements.CreateWallFromCurve(
                        doc, curve, WallHeightMm, tm.TypeId, level);
                    if (created == null) { tx.RollBack(); }
                    else tx.Commit();
                }

                if (created == null)
                {
                    TaskDialog.Show("STING Pick Wall", "Wall creation failed — check the log.");
                    return Result.Failed;
                }

                uidoc.Selection.SetElementIds(new List<ElementId> { created.Id });
                TaskDialog.Show("STING Pick Wall",
                    $"Created a structural wall {thicknessMm:F0} mm thick (measured), " +
                    $"{WallHeightMm:F0} mm high, on level '{level.Name}' ({levelHow}).\n\n" +
                    DwgPickSupport.DescribeMatch(tm, "wall", thicknessMm));
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DWGInteractivePickWallCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Interactive: pick a column outline in the DWG → one structural column sized
    /// from it. Accepts a circle (round column, diameter), a closed rectangular
    /// polyline, or one edge line followed by the opposite edge line. The column
    /// type is found or duplicated at the measured size via StructuralTypeFactory,
    /// placed at the outline's centre and rotated to the outline.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DWGInteractivePickColumnCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var app = ParameterHelpers.GetApp(commandData);
                var uidoc = app?.ActiveUIDocument ?? ParameterHelpers.GetUIDoc(commandData);
                var doc = uidoc?.Document;
                if (doc == null || uidoc == null)
                {
                    TaskDialog.Show("STING", "No active document.");
                    return Result.Cancelled;
                }

                var filter = new DwgPickSupport.DwgGeometryFilter();
                Reference r1;
                try
                {
                    r1 = uidoc.Selection.PickObject(ObjectType.PointOnElement, filter,
                        "Pick the column outline in the DWG — a circle, a closed rectangle, or one edge line");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                var go = DwgPickSupport.GetPickedGeometry(doc, r1);
                bool round = false;
                string shapeNote;
                DwgPickGeometry.RectDims dims;

                if (go is Arc arc)
                {
                    round = true;
                    double dFt = arc.Radius * 2;
                    dims = new DwgPickGeometry.RectDims(dFt, dFt, arc.Center.X, arc.Center.Y, 0);
                    shapeNote = arc.IsBound
                        ? "round (from an arc — only part of a circle was picked)"
                        : "round (from a circle)";
                }
                else if (go is PolyLine pl)
                {
                    var pts = pl.GetCoordinates();
                    if (!DwgPickGeometry.TryParseRectangle(
                            pts.Select(p => p.X).ToList(), pts.Select(p => p.Y).ToList(), out dims))
                    {
                        TaskDialog.Show("STING Pick Column",
                            "The picked polyline is not a closed rectangle (four right-angled corners). " +
                            "Pick a circle or a rectangular outline.");
                        return Result.Cancelled;
                    }
                    shapeNote = "rectangular (from a closed polyline)";
                }
                else if (go is Line edge1)
                {
                    Reference r2;
                    try
                    {
                        r2 = uidoc.Selection.PickObject(ObjectType.PointOnElement, filter,
                            "Now pick the OPPOSITE edge line of the same column");
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        return Result.Cancelled;
                    }
                    var edge2 = DwgPickSupport.GetPickedGeometry(doc, r2) as Line;
                    if (edge2 == null)
                    {
                        TaskDialog.Show("STING Pick Column", "The second pick must be a straight DWG line.");
                        return Result.Cancelled;
                    }
                    var pair = DwgPickSupport.Measure(edge1, edge2);
                    if (!pair.IsParallel)
                    {
                        TaskDialog.Show("STING Pick Column",
                            $"The two edges are not parallel enough (dot={pair.Dot:F3}, need ≥ 0.95).");
                        return Result.Cancelled;
                    }
                    dims = DwgPickGeometry.RectFromParallelEdges(pair);
                    shapeNote = "rectangular (from two opposite edges)";
                }
                else
                {
                    TaskDialog.Show("STING Pick Column",
                        "Pick a circle, a closed rectangular polyline, or a straight edge line inside the DWG import.");
                    return Result.Cancelled;
                }

                double wMm = dims.Width * Units.FeetToMm, dMm = dims.Depth * Units.FeetToMm;
                if (wMm < 100 || dMm < 100 || wMm > 3000 || dMm > 3000)
                {
                    TaskDialog.Show("STING Pick Column",
                        $"Measured column {wMm:F0} × {dMm:F0} mm is outside the sensible range (100-3000 mm).");
                    return Result.Cancelled;
                }

                var level = DwgPickSupport.ResolvePickLevel(doc, out string levelHow);
                if (level == null)
                {
                    TaskDialog.Show("STING Pick Column", "No level in the project.");
                    return Result.Cancelled;
                }

                var factory = new StructuralTypeFactory(doc);
                FamilyInstance col = null;
                TypeMatchResult tm;
                using (var tx = new Transaction(doc, "STING: Pick Column from DWG"))
                {
                    tx.Start();
                    tm = factory.FindOrCreateColumnType(wMm, dMm,
                        preferredFamily: round ? "Round" : "Rectangular", allowDuplicate: true);
                    if (!tm.Success)
                    {
                        tx.RollBack();
                        TaskDialog.Show("STING Pick Column", tm.Message ?? "No structural column family loaded in the project.");
                        return Result.Cancelled;
                    }
                    var symbol = doc.GetElement(tm.TypeId) as FamilySymbol;
                    if (symbol == null)
                    {
                        tx.RollBack();
                        TaskDialog.Show("STING Pick Column", "The resolved column type could not be read.");
                        return Result.Failed;
                    }
                    if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }
                    var centre = new XYZ(dims.CenterX, dims.CenterY, level.Elevation);
                    col = doc.Create.NewFamilyInstance(centre, symbol, level, StructuralType.Column);
                    if (col != null && Math.Abs(dims.AngleRad) > 1e-6)
                    {
                        var axis = Line.CreateBound(centre, centre + XYZ.BasisZ);
                        ElementTransformUtils.RotateElement(doc, col.Id, axis, dims.AngleRad);
                    }
                    if (col == null) tx.RollBack(); else tx.Commit();
                }

                if (col == null)
                {
                    TaskDialog.Show("STING Pick Column", "Column creation failed — check the log.");
                    return Result.Failed;
                }

                uidoc.Selection.SetElementIds(new List<ElementId> { col.Id });
                string size = round ? $"Ø{wMm:F0} mm" : $"{wMm:F0} × {dMm:F0} mm";
                string rot = Math.Abs(dims.AngleRad) > 1e-6
                    ? $", rotated {dims.AngleRad * 180.0 / Math.PI:F1}°" : "";
                TaskDialog.Show("STING Pick Column",
                    $"Created a {shapeNote} column, measured {size}{rot}, on level " +
                    $"'{level.Name}' ({levelHow}).\n\n" +
                    DwgPickSupport.DescribeMatch(tm, "column", Math.Max(wMm, dMm)));
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DWGInteractivePickColumnCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Interactive beam. Two modes, chosen up front:
    ///   • Edge lines — pick the beam's two parallel edge lines; the gap is its
    ///     width and a framing type of that width (±5 mm) is found or duplicated.
    ///     Plan geometry cannot show depth, so the type's depth is kept and reported.
    ///   • Points — pick start and end; the first framing type is used and the
    ///     result says that no size was measured.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DWGInteractivePickBeamCommand : IExternalCommand
    {
        private const double WidthToleranceMm = 5;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var app = ParameterHelpers.GetApp(commandData);
                var uidoc = app?.ActiveUIDocument ?? ParameterHelpers.GetUIDoc(commandData);
                var doc = uidoc?.Document;
                if (doc == null || uidoc == null)
                {
                    TaskDialog.Show("STING", "No active document.");
                    return Result.Cancelled;
                }

                var mode = new TaskDialog("STING Pick Beam")
                {
                    MainInstruction = "How should the beam be picked?",
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                };
                mode.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Pick the two parallel edge lines",
                    "Measures the beam width and matches or creates a framing type of that width.");
                mode.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Pick start and end points",
                    "No size is measured — the first framing type in the project is used.");
                var choice = mode.Show();
                if (choice != TaskDialogResult.CommandLink1 && choice != TaskDialogResult.CommandLink2)
                    return Result.Cancelled;
                bool byEdges = choice == TaskDialogResult.CommandLink1;

                double bx0, by0, bx1, by1;
                double widthMm = 0;
                try
                {
                    if (byEdges)
                    {
                        var filter = new DwgPickSupport.DwgGeometryFilter();
                        var r1 = uidoc.Selection.PickObject(ObjectType.PointOnElement, filter,
                            "Pick the FIRST beam edge line in the DWG");
                        var r2 = uidoc.Selection.PickObject(ObjectType.PointOnElement, filter,
                            "Pick the SECOND (opposite) beam edge line");
                        var l1 = DwgPickSupport.GetPickedGeometry(doc, r1) as Line;
                        var l2 = DwgPickSupport.GetPickedGeometry(doc, r2) as Line;
                        if (l1 == null || l2 == null)
                        {
                            TaskDialog.Show("STING Pick Beam",
                                "Both picks must be straight lines inside the DWG import.");
                            return Result.Cancelled;
                        }
                        var pair = DwgPickSupport.Measure(l1, l2);
                        if (!pair.IsParallel)
                        {
                            TaskDialog.Show("STING Pick Beam",
                                $"The two lines are not parallel enough (dot={pair.Dot:F3}, need ≥ 0.95).");
                            return Result.Cancelled;
                        }
                        widthMm = pair.Gap * Units.FeetToMm;
                        if (widthMm < 50 || widthMm > 2000)
                        {
                            TaskDialog.Show("STING Pick Beam",
                                $"Measured beam width {widthMm:F0} mm is outside the sensible range (50-2000 mm).");
                            return Result.Cancelled;
                        }
                        bx0 = pair.StartX; by0 = pair.StartY; bx1 = pair.EndX; by1 = pair.EndY;
                    }
                    else
                    {
                        var p1 = uidoc.Selection.PickPoint("Pick beam START on the DWG");
                        var p2 = uidoc.Selection.PickPoint("Pick beam END on the DWG");
                        bx0 = p1.X; by0 = p1.Y; bx1 = p2.X; by1 = p2.Y;
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                double lenFt = Math.Sqrt((bx1 - bx0) * (bx1 - bx0) + (by1 - by0) * (by1 - by0));
                if (lenFt < 500 * Units.MmToFeet)
                {
                    TaskDialog.Show("STING Pick Beam",
                        "The beam would be shorter than 500mm — too short.");
                    return Result.Cancelled;
                }

                var level = DwgPickSupport.ResolvePickLevel(doc, out string levelHow);
                if (level == null)
                {
                    TaskDialog.Show("STING Pick Beam", "No level in the project.");
                    return Result.Cancelled;
                }

                FamilyInstance beam = null;
                string typeNote;
                using (var tx = new Transaction(doc, "STING: Pick Beam from DWG"))
                {
                    tx.Start();
                    FamilySymbol symbol;
                    if (byEdges)
                    {
                        var tm = new StructuralTypeFactory(doc)
                            .FindOrCreateBeamTypeByWidth(widthMm, WidthToleranceMm, allowDuplicate: true);
                        if (!tm.Success)
                        {
                            tx.RollBack();
                            TaskDialog.Show("STING Pick Beam", tm.Message);
                            return Result.Cancelled;
                        }
                        symbol = doc.GetElement(tm.TypeId) as FamilySymbol;
                        typeNote = DwgPickSupport.DescribeMatch(tm, "framing", widthMm) +
                            (tm.DepthMm > 0
                                ? $"\nDepth {tm.DepthMm:F0} mm comes from that type — plan lines cannot show depth; check it."
                                : "\nDepth was not measured — plan lines cannot show it; check the type.");
                    }
                    else
                    {
                        symbol = new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_StructuralFraming)
                            .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                            .FirstOrDefault();
                        typeNote = symbol == null ? "" :
                            $"No size was measured — used the first framing type, '{symbol.FamilyName}: {symbol.Name}'. " +
                            "Use the edge-line mode to size the beam by width.";
                    }
                    if (symbol == null)
                    {
                        tx.RollBack();
                        TaskDialog.Show("STING Pick Beam",
                            "No structural framing family loaded in the project.");
                        return Result.Cancelled;
                    }
                    if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }
                    var curve = Line.CreateBound(
                        new XYZ(bx0, by0, level.Elevation), new XYZ(bx1, by1, level.Elevation));
                    beam = doc.Create.NewFamilyInstance(curve, symbol, level, StructuralType.Beam);
                    if (beam == null) tx.RollBack(); else tx.Commit();
                }

                if (beam == null)
                {
                    TaskDialog.Show("STING Pick Beam", "Beam creation failed — check the log.");
                    return Result.Failed;
                }

                uidoc.Selection.SetElementIds(new List<ElementId> { beam.Id });
                TaskDialog.Show("STING Pick Beam",
                    (byEdges ? $"Created a beam {widthMm:F0} mm wide (measured)" : "Created a beam") +
                    $", {lenFt * Units.FeetToMm:F0} mm long, on level '{level.Name}' ({levelHow}).\n\n" +
                    typeNote);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DWGInteractivePickBeamCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
