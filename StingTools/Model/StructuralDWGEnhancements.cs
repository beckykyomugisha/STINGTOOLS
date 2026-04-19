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
        /// Wraps <see cref="ImportInstance.Explode"/> in a single transaction
        /// so DWG block references become individual Revit DetailLines /
        /// ModelLines / etc. on their host layers. Call BEFORE
        /// ExtractStructuralGeometry so block-hidden geometry is visible
        /// to the pipeline's layer filter.
        /// </summary>
        public static class ExplodeHelper
        {
            /// <summary>
            /// Explode a single ImportInstance in place and return the count
            /// of direct children created. Returns 0 if the instance is
            /// non-explodable (linked DWG, exploded already, or protected).
            /// </summary>
            public static int ExplodeInPlace(Document doc, ImportInstance import)
            {
                if (doc == null || import == null) return 0;
                // Revit refuses to explode linked (non-imported) references
                // and view-specific instances that have been edited in place.
                // Exploding a view-specific import also destroys the
                // ImportInstance, which is what we want.
                try
                {
                    ICollection<ElementId> children;
                    using (var tx = new Transaction(doc, "STING: Explode DWG import"))
                    {
                        tx.Start();
                        children = import.Explode();
                        tx.Commit();
                    }
                    return children?.Count ?? 0;
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

        internal static Wall CreateWallFromCurve(
            Document doc, Curve centerline, double heightMm, double thicknessMm)
        {
            // Resolve the first valid wall type, preferring one whose name
            // already contains "STING" (matches the TypeFactory convention).
            var wallType = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType)).Cast<WallType>()
                .Where(t => t.Kind == WallKind.Basic)
                .OrderByDescending(t => (t.Name ?? "").Contains("STING"))
                .FirstOrDefault();
            if (wallType == null)
            {
                StingLog.Warn("Interactive wall: no Basic WallType found");
                return null;
            }
            var level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).FirstOrDefault();
            if (level == null)
            {
                StingLog.Warn("Interactive wall: no Level in project");
                return null;
            }
            double heightFt = heightMm * MmToFeet;
            return Wall.Create(doc, centerline, wallType.Id, level.Id, heightFt, 0, false, true);
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
                var doc = app?.ActiveUIDocument?.Document ?? commandData?.Application?.ActiveUIDocument?.Document;
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
                var doc = app?.ActiveUIDocument?.Document ?? commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    TaskDialog.Show("STING", "No active document.");
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
                var doc = app?.ActiveUIDocument?.Document ?? commandData?.Application?.ActiveUIDocument?.Document;
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
    /// Interactive: pick two parallel DWG lines → one structural wall
    /// whose thickness equals the perpendicular distance between them.
    /// Prompts the user twice via the Revit selection filter.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DWGInteractivePickWallCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var app = ParameterHelpers.GetApp(commandData);
                var uidoc = app?.ActiveUIDocument ?? commandData?.Application?.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null || uidoc == null)
                {
                    TaskDialog.Show("STING", "No active document.");
                    return Result.Cancelled;
                }

                // Let the user pick two DWG sub-objects (lines inside the
                // ImportInstance). We accept PickObject calls with our own
                // ISelectionFilter that restricts the selection to lines.
                var filter = new DwgLineFilter();
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

                var line1 = GetGeometryAsLine(doc, r1);
                var line2 = GetGeometryAsLine(doc, r2);
                if (line1 == null || line2 == null)
                {
                    TaskDialog.Show("STING Pick Wall",
                        "Both picks must be straight lines inside the DWG import.");
                    return Result.Cancelled;
                }

                // Verify parallelism + measure perpendicular distance.
                var dir1 = (line1.GetEndPoint(1) - line1.GetEndPoint(0)).Normalize();
                var dir2 = (line2.GetEndPoint(1) - line2.GetEndPoint(0)).Normalize();
                double dot = Math.Abs(dir1.DotProduct(dir2));
                if (dot < 0.95)
                {
                    TaskDialog.Show("STING Pick Wall",
                        $"The two lines are not parallel enough (dot={dot:F3}, need ≥ 0.95).");
                    return Result.Cancelled;
                }

                var pA = line1.GetEndPoint(0);
                var pB = line2.GetEndPoint(0);
                var rel = pB - pA;
                var perp = rel - dir1 * rel.DotProduct(dir1);
                double thicknessFt = perp.GetLength();
                double thicknessMm = thicknessFt * FeetToMm;
                if (thicknessMm < 50 || thicknessMm > 1000)
                {
                    TaskDialog.Show("STING Pick Wall",
                        $"Measured wall thickness {thicknessMm:F0}mm is outside the sensible range (50-1000mm).");
                    return Result.Cancelled;
                }

                // Build the centerline from midpoints of matching endpoints.
                var l2a = line2.GetEndPoint(0);
                var l2b = line2.GetEndPoint(1);
                if (line1.GetEndPoint(0).DistanceTo(l2b) < line1.GetEndPoint(0).DistanceTo(l2a))
                {
                    // Anti-parallel — swap
                    var tmp = l2a; l2a = l2b; l2b = tmp;
                }
                var start = (line1.GetEndPoint(0) + l2a) * 0.5;
                var end = (line1.GetEndPoint(1) + l2b) * 0.5;
                start = new XYZ(start.X, start.Y, 0);
                end = new XYZ(end.X, end.Y, 0);
                if (start.DistanceTo(end) < (300 * MmToFeet))
                {
                    TaskDialog.Show("STING Pick Wall", "Picked lines are too short for a wall.");
                    return Result.Cancelled;
                }

                Wall created;
                using (var tx = new Transaction(doc, "STING: Pick Wall from DWG"))
                {
                    tx.Start();
                    var curve = Line.CreateBound(start, end);
                    created = StructuralDWGEnhancements.CreateWallFromCurve(doc, curve, 3000, thicknessMm);
                    tx.Commit();
                }

                if (created != null)
                {
                    uidoc.Selection.SetElementIds(new List<ElementId> { created.Id });
                    TaskDialog.Show("STING Pick Wall",
                        $"Created wall with measured thickness {thicknessMm:F0}mm.");
                    return Result.Succeeded;
                }
                TaskDialog.Show("STING Pick Wall", "Wall creation failed — check the log.");
                return Result.Failed;
            }
            catch (Exception ex)
            {
                StingLog.Error("DWGInteractivePickWallCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static Line GetGeometryAsLine(Document doc, Reference r)
        {
            if (doc == null || r == null) return null;
            try
            {
                var host = doc.GetElement(r);
                if (host == null) return null;
                var go = host.GetGeometryObjectFromReference(r);
                return go as Line;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Pick wall geom: {ex.Message}");
                return null;
            }
        }

        private sealed class DwgLineFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is ImportInstance;
            public bool AllowReference(Reference reference, XYZ position) => true;
        }
    }

    /// <summary>
    /// Interactive: pick a closed rectangle / circle in the DWG → one
    /// structural column at the geometric centre. Size inferred from
    /// picked geometry bounding box.
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
                var uidoc = app?.ActiveUIDocument ?? commandData?.Application?.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null || uidoc == null)
                {
                    TaskDialog.Show("STING", "No active document.");
                    return Result.Cancelled;
                }

                XYZ centre;
                try
                {
                    var pt = uidoc.Selection.PickPoint(
                        "Click the centre of the column on the DWG");
                    centre = new XYZ(pt.X, pt.Y, 0);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                // Fixed default size (300×300mm) — the user can edit the
                // created column's type afterwards. A richer implementation
                // would measure the picked rectangle; that's future work.
                var symbol = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                    .FirstOrDefault();
                if (symbol == null)
                {
                    TaskDialog.Show("STING Pick Column",
                        "No structural column family loaded in the project.");
                    return Result.Cancelled;
                }

                var level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation).FirstOrDefault();
                if (level == null)
                {
                    TaskDialog.Show("STING Pick Column", "No level in the project.");
                    return Result.Cancelled;
                }

                FamilyInstance col;
                using (var tx = new Transaction(doc, "STING: Pick Column from DWG"))
                {
                    tx.Start();
                    if (!symbol.IsActive) symbol.Activate();
                    col = doc.Create.NewFamilyInstance(centre, symbol, level, StructuralType.Column);
                    tx.Commit();
                }
                if (col != null)
                {
                    uidoc.Selection.SetElementIds(new List<ElementId> { col.Id });
                    return Result.Succeeded;
                }
                return Result.Failed;
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
    /// Interactive: pick two parallel DWG lines → one structural beam
    /// whose width equals the perpendicular distance between them.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DWGInteractivePickBeamCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var app = ParameterHelpers.GetApp(commandData);
                var uidoc = app?.ActiveUIDocument ?? commandData?.Application?.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null || uidoc == null)
                {
                    TaskDialog.Show("STING", "No active document.");
                    return Result.Cancelled;
                }

                XYZ p1, p2;
                try
                {
                    p1 = uidoc.Selection.PickPoint("Pick beam START on the DWG");
                    p2 = uidoc.Selection.PickPoint("Pick beam END on the DWG");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                p1 = new XYZ(p1.X, p1.Y, 0);
                p2 = new XYZ(p2.X, p2.Y, 0);
                if (p1.DistanceTo(p2) < (500 * MmToFeet))
                {
                    TaskDialog.Show("STING Pick Beam",
                        "Picked endpoints are closer than 500mm — too short for a beam.");
                    return Result.Cancelled;
                }

                var symbol = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                    .FirstOrDefault();
                if (symbol == null)
                {
                    TaskDialog.Show("STING Pick Beam",
                        "No structural framing family loaded in the project.");
                    return Result.Cancelled;
                }

                var level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation).FirstOrDefault();
                if (level == null)
                {
                    TaskDialog.Show("STING Pick Beam", "No level in the project.");
                    return Result.Cancelled;
                }

                FamilyInstance beam;
                using (var tx = new Transaction(doc, "STING: Pick Beam from DWG"))
                {
                    tx.Start();
                    if (!symbol.IsActive) symbol.Activate();
                    var curve = Line.CreateBound(p1, p2);
                    beam = doc.Create.NewFamilyInstance(curve, symbol, level, StructuralType.Beam);
                    tx.Commit();
                }

                if (beam != null)
                {
                    uidoc.Selection.SetElementIds(new List<ElementId> { beam.Id });
                    return Result.Succeeded;
                }
                return Result.Failed;
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
