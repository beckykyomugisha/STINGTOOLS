// ============================================================================
// StructuralDWGEngine.cs — Precision DWG-to-Structural BIM Engine
//
// v1.0 — Comprehensive element creation engine with:
//   - Intelligent geometry extraction from DWG layers
//   - Type creation from detected line shapes/dimensions
//   - Wall joining (T/L/X junction detection and auto-join)
//   - Column-to-wall joining at intersections
//   - Beam extension to nearest support
//   - Collinear wall segment merging
//   - Grid snap alignment
//   - Shear wall detection (thick walls near cores)
//   - Foundation placement below columns
//   - STING auto-tagging pipeline integration
//   - Progress reporting with cancellation
//
// Algorithms inspired by: ETLIPS wall detection, EaseBit parallel line analysis,
//   Naviate structural templates, BIMLOGiQ placement intelligence
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StingTools.Core;

namespace StingTools.Model
{
    /// <summary>
    /// Precision engine for DWG-to-structural BIM conversion.
    /// Takes a StructuralDWGConfig from the wizard and creates
    /// Revit structural elements with joining, type creation, and tagging.
    /// </summary>
    internal static class StructuralDWGEngine
    {
        // ── Constants ──
        private const double FeetToMm = 304.8;
        private const double MmToFeet = 1.0 / 304.8;

        // DWG-HIGH-03: Pre-loaded type lookup cache.
        // Holds all existing element types/symbols collected ONCE before the
        // creation transaction. All FindOrCreate* methods use this cache for O(1)
        // lookups instead of repeated FilteredElementCollector scans.
        private sealed class TypeLookupCache
        {
            public Dictionary<string, WallType>     WallTypes      { get; }
            public Dictionary<string, FamilySymbol> ColumnSymbols  { get; }
            public Dictionary<string, FamilySymbol> BeamSymbols    { get; }
            public Dictionary<string, FloorType>    FloorTypes     { get; }
            public List<FamilySymbol>               FoundSymbols   { get; }

            // Lazily-resolved base types (first valid entry of each category)
            private WallType     _baseWall;
            private FamilySymbol _baseColumn;
            private FamilySymbol _baseBeam;
            private FloorType    _baseFloor;
            private FamilySymbol _baseFound;

            public WallType     BaseWallType     => _baseWall     ??= WallTypes.Values.FirstOrDefault(t => t.Kind == WallKind.Basic) ?? WallTypes.Values.FirstOrDefault();
            public FamilySymbol BaseColumnSymbol => _baseColumn   ??= ColumnSymbols.Values.FirstOrDefault();
            public FamilySymbol BaseBeamSymbol   => _baseBeam     ??= BeamSymbols.Values.FirstOrDefault();
            public FloorType    BaseFloorType    => _baseFloor    ??= FloorTypes.Values.FirstOrDefault();
            public FamilySymbol BaseFoundSymbol  => _baseFound    ??= FoundSymbols.FirstOrDefault();

            public TypeLookupCache(
                Dictionary<string, WallType>     wallTypes,
                Dictionary<string, FamilySymbol> colSymbols,
                Dictionary<string, FamilySymbol> beamSymbols,
                Dictionary<string, FloorType>    floorTypes,
                List<FamilySymbol>               foundSymbols)
            {
                WallTypes     = wallTypes;
                ColumnSymbols = colSymbols;
                BeamSymbols   = beamSymbols;
                FloorTypes    = floorTypes;
                FoundSymbols  = foundSymbols;
            }

            // Register a newly duplicated type so subsequent calls within the same batch see it.
            public void RegisterWall(string name, WallType t)         => WallTypes[name]     = t;
            public void RegisterColumn(string name, FamilySymbol s)   => ColumnSymbols[name] = s;
            public void RegisterBeam(string name, FamilySymbol s)     => BeamSymbols[name]   = s;
            public void RegisterFloor(string name, FloorType t)       => FloorTypes[name]    = t;
        }

        /// <summary>Result of the structural DWG conversion.</summary>
        public class ConversionResult
        {
            public int WallsCreated { get; set; }
            public int ColumnsCreated { get; set; }
            public int BeamsCreated { get; set; }
            public int SlabsCreated { get; set; }
            public int FoundationsCreated { get; set; }
            public int ShearWallsCreated { get; set; }
            public int BracingCreated { get; set; }
            public int GridLinesCreated { get; set; }
            public int JoinsPerformed { get; set; }
            public int TypesCreated { get; set; }
            public int ElementsTagged { get; set; }
            public int LevelsRepeated { get; set; }
            public int BeamPairsDetected { get; set; }
            public int Errors { get; set; }
            public List<string> Warnings { get; set; } = new();
            public List<ElementId> CreatedElementIds { get; set; } = new();
            public TimeSpan Duration { get; set; }

            public string GetSummary()
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("STRUCTURAL DWG CONVERSION RESULTS");
                sb.AppendLine("═════════════════════════════════════");
                sb.AppendLine($"  Walls:        {WallsCreated}");
                sb.AppendLine($"  Columns:      {ColumnsCreated}");
                sb.AppendLine($"  Beams:        {BeamsCreated}");
                sb.AppendLine($"  Slabs:        {SlabsCreated}");
                sb.AppendLine($"  Foundations:  {FoundationsCreated}");
                sb.AppendLine($"  Shear Walls:  {ShearWallsCreated}");
                sb.AppendLine($"  Bracing:      {BracingCreated}");
                sb.AppendLine($"  Grid Lines:   {GridLinesCreated}");
                sb.AppendLine($"  ─────────────────────────────────");
                sb.AppendLine($"  TOTAL:        {CreatedElementIds.Count} elements");
                sb.AppendLine($"  Joins:        {JoinsPerformed}");
                sb.AppendLine($"  Types:        {TypesCreated} new");
                sb.AppendLine($"  Tagged:       {ElementsTagged}");
                if (LevelsRepeated > 0) sb.AppendLine($"  Levels:       {LevelsRepeated} repeated");
                if (BeamPairsDetected > 0) sb.AppendLine($"  Beam Pairs:   {BeamPairsDetected} (width auto-detected)");
                if (Errors > 0) sb.AppendLine($"  Errors:       {Errors}");
                sb.AppendLine($"  Duration:     {Duration.TotalSeconds:F1}s");
                if (Warnings.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("WARNINGS:");
                    foreach (var w in Warnings.Take(10)) sb.AppendLine($"  ⚠ {w}");
                    if (Warnings.Count > 10)
                        sb.AppendLine($"  ... and {Warnings.Count - 10} more");
                }
                return sb.ToString();
            }
        }

        /// <summary>Extracted line with layer and type assignment.</summary>
        private class MappedLine
        {
            public XYZ Start { get; set; }
            public XYZ End { get; set; }
            public string LayerName { get; set; }
            public string ElementType { get; set; } // Wall, Column, Beam, etc.
            public double Length => Start.DistanceTo(End);
        }

        /// <summary>Detected wall from parallel lines or single lines on wall layers.</summary>
        private class DetectedWallSegment
        {
            public XYZ Start { get; set; }
            public XYZ End { get; set; }
            public double ThicknessFt { get; set; }
            public bool IsShearWall { get; set; }
            public string LayerName { get; set; }
        }

        /// <summary>Detected column from small rectangles or circles.</summary>
        private class DetectedColumnPos
        {
            public XYZ Center { get; set; }
            public double WidthFt { get; set; }
            public double DepthFt { get; set; }
            public bool IsCircular { get; set; }
            public double Rotation { get; set; }
            public string LayerName { get; set; }
        }

        /// <summary>Detected beam from parallel line pairs or single lines on beam layers.</summary>
        private class DetectedBeamLine
        {
            public XYZ Start { get; set; }
            public XYZ End { get; set; }
            public double WidthFt { get; set; }
            public bool WidthDetected { get; set; }
            public string LayerName { get; set; }
        }

        /// <summary>Detected slab from closed polygon loops.</summary>
        private class DetectedSlabLoop
        {
            public List<XYZ> BoundaryPoints { get; set; } = new();
            public double AreaSqFt { get; set; }
            public string LayerName { get; set; }
        }

        // ══════════════════════════════════════════════════════════════════
        // MAIN ENTRY POINT
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Execute the full DWG-to-structural conversion pipeline.
        /// </summary>
        public static ConversionResult Execute(Document doc, StructuralDWGConfig config)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = new ConversionResult();

            try
            {
                StingLog.Info("StructuralDWGEngine: Starting conversion pipeline");

                // Phase 77: Validate config dimensions before proceeding
                string dimError = config.ValidateDimensions();
                if (dimError != null)
                {
                    result.Warnings.Add($"Invalid config: {dimError}");
                    result.Errors++;
                    StingLog.Warn($"StructuralDWGEngine: Config validation failed: {dimError}");
                    return result;
                }

                // 1. Extract geometry from DWG by mapped layers
                var mappedLines = ExtractMappedGeometry(doc, config, result);
                StingLog.Info($"Extracted {mappedLines.Count} mapped lines from DWG");

                // 2. Detect structural elements
                var walls = DetectWalls(mappedLines, config, result);
                var columns = DetectColumns(mappedLines, config, result);
                var beams = DetectBeams(mappedLines, config, result);
                var slabs = DetectSlabs(mappedLines, config, result);

                result.BeamPairsDetected = beams.Count(b => b.WidthDetected);
                StingLog.Info($"Detected: {walls.Count} walls, {columns.Count} columns, {beams.Count} beams ({result.BeamPairsDetected} paired), {slabs.Count} slabs");

                // 3. Pre-processing: merge collinear walls, snap to grid
                if (config.MergeCollinearWalls)
                    walls = MergeCollinearWalls(walls, config.EndpointToleranceMm * MmToFeet);

                // 4. Get or create levels
                var baseLevel = doc.GetElement(config.BaseLevelId) as Level;
                var topLevel = config.TopLevelId != null ? doc.GetElement(config.TopLevelId) as Level : null;
                if (baseLevel == null)
                {
                    baseLevel = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level)).Cast<Level>()
                        .OrderBy(l => l.Elevation).FirstOrDefault();
                }
                if (baseLevel == null)
                {
                    result.Warnings.Add("No levels found in project. Please create at least one Level before importing structural DWG.");
                    result.Errors++;
                    StingLog.Error("StructuralDWGEngine: No levels found — cannot create structural elements without a base level");
                    return result;
                }

                // DWG-HIGH-03: Pre-collect all type catalogs into dictionaries BEFORE the
                // transaction. Each FindOrCreate* method previously ran 2 FilteredElementCollector
                // scans: one for the exact type name, one for a base type to duplicate from.
                // With N distinct beam widths, that was 2N collectors just for beam types.
                // Pre-loading into lookup dictionaries reduces to a single scan per category,
                // O(1) per type lookup inside the transaction.
                var preloadedWallTypes      = new FilteredElementCollector(doc).OfClass(typeof(WallType))
                    .Cast<WallType>().ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);
                var preloadedColumnSymbols  = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns).OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>().GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                var preloadedBeamSymbols    = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming).OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>().GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                var preloadedFloorTypes     = new FilteredElementCollector(doc).OfClass(typeof(FloorType))
                    .Cast<FloorType>().ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);
                var preloadedFoundSymbols   = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFoundation).OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>().ToList();
                var typePreload = new TypeLookupCache(preloadedWallTypes, preloadedColumnSymbols,
                    preloadedBeamSymbols, preloadedFloorTypes, preloadedFoundSymbols);

                // 5. Create elements in transaction
                using (var tx = new Transaction(doc, "STING Structural DWG Conversion"))
                {
                    tx.Start();

                    // Set failure handler to dismiss non-critical warnings
                    var failOpts = tx.GetFailureHandlingOptions();
                    failOpts.SetFailuresPreprocessor(new SilentWarningDismisser());
                    tx.SetFailureHandlingOptions(failOpts);

                    // 5a. Grid lines (first, so we can snap to them)
                    if (config.CreateGridLines && config.LayerMapping.ContainsKey("Grid Line"))
                        CreateGridLines(doc, mappedLines, config, baseLevel, result, typePreload);

                    // 5b. Walls
                    var createdWalls = CreateWalls(doc, walls, config, baseLevel, topLevel, result, typePreload);

                    // 5c. Columns
                    var createdColumns = CreateColumns(doc, columns, config, baseLevel, topLevel, result, typePreload);

                    // 5d. Beams
                    CreateBeams(doc, beams, config, baseLevel, result, typePreload);

                    // 5e. Slabs
                    CreateSlabs(doc, slabs, config, baseLevel, result, typePreload);

                    // 5f. Foundations below columns
                    if (config.DetectFoundations)
                        CreateFoundations(doc, columns, config, baseLevel, result, typePreload);

                    // 6. Joining
                    if (config.AutoJoinWalls)
                        JoinWalls(doc, createdWalls, result);
                    if (config.AutoJoinColumns)
                        JoinColumnsToWalls(doc, createdColumns, createdWalls, result);

                    tx.Commit();
                }

                // 6b. Repeat/copy structural layout to additional levels
                if (config.RepeatToLevelIds != null && config.RepeatToLevelIds.Count > 0)
                {
                    var allLevels = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level)).Cast<Level>()
                        .ToDictionary(l => l.Id);

                    foreach (var levelId in config.RepeatToLevelIds)
                    {
                        if (levelId == null || levelId == config.BaseLevelId) continue;
                        if (!allLevels.TryGetValue(levelId, out var repeatLevel)) continue;

                        // Find the next level above this one for top constraint
                        Level repeatTopLevel = null;
                        if (topLevel != null)
                        {
                            double targetTop = repeatLevel.Elevation + (topLevel.Elevation - baseLevel.Elevation);
                            repeatTopLevel = allLevels.Values
                                .Where(l => l.Elevation > repeatLevel.Elevation)
                                .OrderBy(l => Math.Abs(l.Elevation - targetTop))
                                .FirstOrDefault();
                        }

                        StingLog.Info($"Repeating structural layout to level: {repeatLevel.Name}");

                        using (var tx = new Transaction(doc, $"STING Repeat Structure to {repeatLevel.Name}"))
                        {
                            tx.Start();
                            var failOpts = tx.GetFailureHandlingOptions();
                            failOpts.SetFailuresPreprocessor(new SilentWarningDismisser());
                            tx.SetFailureHandlingOptions(failOpts);

                            var repeatWalls = CreateWalls(doc, walls, config, repeatLevel, repeatTopLevel, result);
                            var repeatCols = CreateColumns(doc, columns, config, repeatLevel, repeatTopLevel, result);
                            CreateBeams(doc, beams, config, repeatLevel, result);
                            CreateSlabs(doc, slabs, config, repeatLevel, result);

                            if (config.AutoJoinWalls)
                                JoinWalls(doc, repeatWalls, result);
                            if (config.AutoJoinColumns)
                                JoinColumnsToWalls(doc, repeatCols, repeatWalls, result);

                            tx.Commit();
                        }

                        result.LevelsRepeated++;
                    }
                }

                // 7. Auto-tag created elements
                if (config.AutoTag && result.CreatedElementIds.Count > 0)
                    AutoTagElements(doc, result);
            }
            catch (Exception ex)
            {
                StingLog.Error("StructuralDWGEngine: Pipeline failed", ex);
                result.Errors++;
                result.Warnings.Add($"Pipeline error: {ex.Message}");
            }

            sw.Stop();
            result.Duration = sw.Elapsed;
            StingLog.Info($"StructuralDWGEngine: Completed in {sw.Elapsed.TotalSeconds:F1}s, {result.CreatedElementIds.Count} elements");

            // Phase 77: Persist conversion result to sidecar for audit trail
            try
            {
                string projectPath = doc.PathName;
                if (!string.IsNullOrEmpty(projectPath))
                {
                    string sidecarPath = System.IO.Path.ChangeExtension(projectPath, ".sting_dwg_conversion.json");
                    var record = new Newtonsoft.Json.Linq.JObject
                    {
                        ["timestamp"] = DateTime.Now.ToString("o"),
                        ["user"] = Environment.UserName,
                        ["walls"] = result.WallsCreated,
                        ["columns"] = result.ColumnsCreated,
                        ["beams"] = result.BeamsCreated,
                        ["slabs"] = result.SlabsCreated,
                        ["foundations"] = result.FoundationsCreated,
                        ["gridLines"] = result.GridLinesCreated,
                        ["joins"] = result.JoinsPerformed,
                        ["types"] = result.TypesCreated,
                        ["tagged"] = result.ElementsTagged,
                        ["levels_repeated"] = result.LevelsRepeated,
                        ["beam_pairs_detected"] = result.BeamPairsDetected,
                        ["errors"] = result.Errors,
                        ["duration_s"] = result.Duration.TotalSeconds,
                        ["total_elements"] = result.CreatedElementIds.Count,
                    };
                    string tempPath = sidecarPath + ".tmp";
                    System.IO.File.WriteAllText(tempPath, record.ToString());
                    System.IO.File.Move(tempPath, sidecarPath, true);
                }
            }
            catch (Exception scEx)
            {
                StingLog.Warn($"DWG conversion sidecar save: {scEx.Message}");
            }

            return result;
        }

        // ══════════════════════════════════════════════════════════════════
        // GEOMETRY EXTRACTION
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Extract lines from DWG filtered by mapped layers.</summary>
        private static List<MappedLine> ExtractMappedGeometry(
            Document doc, StructuralDWGConfig config, ConversionResult result)
        {
            var lines = new List<MappedLine>();
            if (config.SelectedImport == null) return lines;

            // Build reverse lookup: layer name → element type
            var layerToType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in config.LayerMapping)
            {
                foreach (var layerName in kvp.Value)
                    layerToType[layerName] = kvp.Key;
            }

            try
            {
                var geoElem = config.SelectedImport.get_Geometry(new Options());
                if (geoElem == null) return lines;

                foreach (var geoObj in geoElem)
                {
                    if (geoObj is GeometryInstance gi)
                    {
                        var xform = gi.Transform;
                        foreach (var subGeo in gi.GetInstanceGeometry())
                        {
                            string layer = null;
                            try
                            {
                                var gStyle = doc.GetElement(subGeo.GraphicsStyleId) as GraphicsStyle;
                                layer = gStyle?.GraphicsStyleCategory?.Name;
                            }
                            catch (Exception ex) { StingLog.Warn($"Layer read: {ex.Message}"); }

                            if (string.IsNullOrEmpty(layer)) continue;
                            if (!layerToType.TryGetValue(layer, out string elemType)) continue;

                            if (subGeo is Line line)
                            {
                                lines.Add(new MappedLine
                                {
                                    Start = line.GetEndPoint(0),
                                    End = line.GetEndPoint(1),
                                    LayerName = layer,
                                    ElementType = elemType,
                                });
                            }
                            else if (subGeo is PolyLine poly)
                            {
                                var pts = poly.GetCoordinates();
                                for (int i = 0; i < pts.Count - 1; i++)
                                {
                                    lines.Add(new MappedLine
                                    {
                                        Start = pts[i],
                                        End = pts[i + 1],
                                        LayerName = layer,
                                        ElementType = elemType,
                                    });
                                }
                            }
                            else if (subGeo is Arc arc)
                            {
                                // For arcs on column layers, detect circles
                                // Otherwise approximate as chord
                                lines.Add(new MappedLine
                                {
                                    Start = arc.GetEndPoint(0),
                                    End = arc.GetEndPoint(1),
                                    LayerName = layer,
                                    ElementType = elemType,
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("Geometry extraction failed", ex);
                result.Warnings.Add($"DWG extraction error: {ex.Message}");
            }

            return lines;
        }

        // ══════════════════════════════════════════════════════════════════
        // ELEMENT DETECTION
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Detect walls from parallel line pairs or single lines on wall layers.</summary>
        private static List<DetectedWallSegment> DetectWalls(
            List<MappedLine> lines, StructuralDWGConfig config, ConversionResult result)
        {
            var walls = new List<DetectedWallSegment>();
            var wallLines = lines.Where(l => l.ElementType == "Wall" || l.ElementType == "Shear Wall").ToList();
            var used = new HashSet<int>();
            double tol = config.ParallelLineToleranceMm * MmToFeet;
            double minLen = config.MinWallLengthMm * MmToFeet;
            double defaultThick = config.WallThicknessMm * MmToFeet;
            double shearThick = config.ShearWallThicknessMm * MmToFeet;

            // Try to find parallel line pairs (accurate wall thickness)
            for (int i = 0; i < wallLines.Count; i++)
            {
                if (used.Contains(i)) continue;
                var a = wallLines[i];
                if (a.Length < minLen) continue;

                var dirA = (a.End - a.Start).Normalize();
                bool foundPair = false;

                for (int j = i + 1; j < wallLines.Count; j++)
                {
                    if (used.Contains(j)) continue;
                    var b = wallLines[j];

                    // Check parallelism
                    var dirB = (b.End - b.Start).Normalize();
                    double dot = Math.Abs(dirA.DotProduct(dirB));
                    if (dot < 0.98) continue; // Not parallel

                    // Check distance (gap = wall thickness)
                    double dist = PointToLineDistance(a.Start, a.End, b.Start);
                    if (dist < 0.01 || dist > tol) continue; // Too close or too far

                    // Check overlap
                    double overlap = CalculateOverlap(a.Start, a.End, b.Start, b.End);
                    if (overlap < minLen * 0.5) continue;

                    // Found a wall pair
                    // CAD-CRIT-01: Fix anti-parallel line pairs — swap b endpoints if lines
                    // run in opposite directions so midpoints connect corresponding ends.
                    var bStartW = b.Start;
                    var bEndW = b.End;
                    if (a.Start.DistanceTo(bEndW) < a.Start.DistanceTo(bStartW))
                    {
                        bStartW = b.End;
                        bEndW = b.Start;
                    }
                    var mid1 = (a.Start + bStartW) * 0.5;
                    var mid2 = (a.End + bEndW) * 0.5;

                    // Determine if it's a shear wall by thickness
                    bool isShear = a.ElementType == "Shear Wall" ||
                        (config.DetectShearWalls && dist > shearThick * 0.8);

                    walls.Add(new DetectedWallSegment
                    {
                        Start = ProjectOntoLine(a.Start, a.End, mid1),
                        End = ProjectOntoLine(a.Start, a.End, mid2),
                        ThicknessFt = dist,
                        IsShearWall = isShear,
                        LayerName = a.LayerName,
                    });

                    used.Add(i);
                    used.Add(j);
                    foundPair = true;
                    break;
                }

                // Single line = wall centerline (use default thickness)
                if (!foundPair && !used.Contains(i))
                {
                    walls.Add(new DetectedWallSegment
                    {
                        Start = a.Start,
                        End = a.End,
                        ThicknessFt = a.ElementType == "Shear Wall" ? shearThick : defaultThick,
                        IsShearWall = a.ElementType == "Shear Wall",
                        LayerName = a.LayerName,
                    });
                    used.Add(i);
                }
            }

            return walls;
        }

        /// <summary>Detect columns from small closed rectangles or clustered short lines.</summary>
        private static List<DetectedColumnPos> DetectColumns(
            List<MappedLine> lines, StructuralDWGConfig config, ConversionResult result)
        {
            var columns = new List<DetectedColumnPos>();
            var colLines = lines.Where(l => l.ElementType == "Column").ToList();
            double minSize = config.MinColumnSizeMm * MmToFeet;
            double maxSize = config.MaxColumnSizeMm * MmToFeet;
            double tol = config.EndpointToleranceMm * MmToFeet;
            var used = new HashSet<int>();

            // Try to find 4-line rectangles (column cross-sections)
            for (int i = 0; i < colLines.Count; i++)
            {
                if (used.Contains(i)) continue;
                var rect = TryBuildRectangle(colLines, i, tol, used);
                if (rect != null && rect.Width >= minSize && rect.Width <= maxSize
                    && rect.Height >= minSize && rect.Height <= maxSize)
                {
                    columns.Add(new DetectedColumnPos
                    {
                        Center = rect.Center,
                        WidthFt = rect.Width,
                        DepthFt = rect.Height,
                        IsCircular = false,
                        Rotation = rect.Rotation,
                        LayerName = colLines[i].LayerName,
                    });
                }
            }

            // Deduplicate columns at same position
            columns = DeduplicateColumns(columns, tol);

            // If no rectangles found, use default size at intersections
            if (columns.Count == 0 && colLines.Count > 0)
            {
                double defaultW = config.ColumnWidthMm * MmToFeet;
                double defaultD = config.ColumnDepthMm * MmToFeet;

                // Group short lines by proximity to find column centers
                var centers = FindClusterCenters(colLines, maxSize);
                foreach (var center in centers)
                {
                    columns.Add(new DetectedColumnPos
                    {
                        Center = center,
                        WidthFt = defaultW,
                        DepthFt = defaultD,
                        IsCircular = config.ColumnShape == "Circular",
                        LayerName = colLines[0].LayerName,
                    });
                }
            }

            return columns;
        }

        /// <summary>
        /// Detect beams from parallel line pairs on beam layers.
        /// In AutoCAD, beams are represented by TWO parallel lines — the distance
        /// between the lines is the beam width. This method pairs parallel lines,
        /// measures the perpendicular distance (beam width), and uses the centerline
        /// as the beam placement line. Falls back to single-line with default width
        /// for unpaired lines.
        /// </summary>
        private static List<DetectedBeamLine> DetectBeams(
            List<MappedLine> lines, StructuralDWGConfig config, ConversionResult result)
        {
            var beams = new List<DetectedBeamLine>();
            var beamLines = lines.Where(l => l.ElementType == "Beam").ToList();
            double minLen = config.MinBeamLengthMm * MmToFeet;
            double defaultWidthFt = config.BeamWidthMm * MmToFeet;

            // Beam width range: 100mm to 600mm (typical RC/steel beam widths)
            double minBeamWidthFt = 100 * MmToFeet;
            double maxBeamWidthFt = 600 * MmToFeet;

            var used = new HashSet<int>();

            // Phase 1: Find parallel line pairs (accurate beam width detection)
            for (int i = 0; i < beamLines.Count; i++)
            {
                if (used.Contains(i)) continue;
                var a = beamLines[i];
                if (a.Length < minLen) continue;

                var dirA = (a.End - a.Start).Normalize();
                bool foundPair = false;

                for (int j = i + 1; j < beamLines.Count; j++)
                {
                    if (used.Contains(j)) continue;
                    var b = beamLines[j];
                    if (b.Length < minLen) continue;

                    // Check parallelism (dot product of normalized directions)
                    var dirB = (b.End - b.Start).Normalize();
                    double dot = Math.Abs(dirA.DotProduct(dirB));
                    if (dot < 0.98) continue; // Not parallel enough

                    // Measure perpendicular distance = beam width
                    double dist = PointToLineDistance(a.Start, a.End, b.Start);
                    if (dist < minBeamWidthFt || dist > maxBeamWidthFt) continue;

                    // Check longitudinal overlap (paired lines must overlap significantly)
                    double overlap = CalculateOverlap(a.Start, a.End, b.Start, b.End);
                    if (overlap < minLen * 0.5) continue;

                    // Found a beam pair — compute centerline from midpoints
                    // CAD-CRIT-01: Fix anti-parallel line pairs — swap b endpoints if lines
                    // run in opposite directions so midpoints connect corresponding ends.
                    var bStartB = b.Start;
                    var bEndB = b.End;
                    if (a.Start.DistanceTo(bEndB) < a.Start.DistanceTo(bStartB))
                    {
                        bStartB = b.End;
                        bEndB = b.Start;
                    }
                    var mid1 = (a.Start + bStartB) * 0.5;
                    var mid2 = (a.End + bEndB) * 0.5;

                    beams.Add(new DetectedBeamLine
                    {
                        Start = ProjectOntoLine(a.Start, a.End, mid1),
                        End = ProjectOntoLine(a.Start, a.End, mid2),
                        WidthFt = dist,
                        WidthDetected = true,
                        LayerName = a.LayerName,
                    });

                    used.Add(i);
                    used.Add(j);
                    foundPair = true;
                    break;
                }

                // Phase 2: Single line fallback (use default beam width from config)
                if (!foundPair && !used.Contains(i))
                {
                    beams.Add(new DetectedBeamLine
                    {
                        Start = a.Start,
                        End = a.End,
                        WidthFt = defaultWidthFt,
                        WidthDetected = false,
                        LayerName = a.LayerName,
                    });
                    used.Add(i);
                }
            }

            int pairedCount = beams.Count(b => b.WidthDetected);
            int singleCount = beams.Count - pairedCount;
            if (pairedCount > 0)
                StingLog.Info($"DetectBeams: {pairedCount} beam pairs detected (width measured), {singleCount} single-line fallbacks");

            return beams;
        }

        /// <summary>Detect slabs from closed polygon loops on slab layers.</summary>
        private static List<DetectedSlabLoop> DetectSlabs(
            List<MappedLine> lines, StructuralDWGConfig config, ConversionResult result)
        {
            var slabs = new List<DetectedSlabLoop>();
            var slabLines = lines.Where(l => l.ElementType == "Slab").ToList();
            if (slabLines.Count < 3) return slabs;

            double tol = config.EndpointToleranceMm * MmToFeet;
            double minArea = config.MinSlabAreaM2 * 10.7639; // m² to ft²

            // Build closed loops from connected lines
            var loops = FindClosedLoops(slabLines, tol);

            foreach (var loop in loops)
            {
                double area = CalculatePolygonArea(loop);
                if (area < minArea) continue;

                slabs.Add(new DetectedSlabLoop
                {
                    BoundaryPoints = loop,
                    AreaSqFt = area,
                    LayerName = slabLines[0].LayerName,
                });
            }

            return slabs;
        }

        // ══════════════════════════════════════════════════════════════════
        // ELEMENT CREATION
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Create Revit walls from detected wall segments.</summary>
        private static List<Wall> CreateWalls(Document doc, List<DetectedWallSegment> walls,
            StructuralDWGConfig config, Level baseLevel, Level topLevel, ConversionResult result,
            TypeLookupCache cache = null)
        {
            var created = new List<Wall>();
            double heightFt = config.WallHeightMm * MmToFeet;

            // DWG-HIGH-03: Use pre-loaded cache for O(1) type lookup.
            var wallType = FindOrCreateWallType(doc, config, result, cache);

            foreach (var ws in walls)
            {
                try
                {
                    var line = Line.CreateBound(
                        new XYZ(ws.Start.X, ws.Start.Y, baseLevel.Elevation),
                        new XYZ(ws.End.X, ws.End.Y, baseLevel.Elevation));

                    var wall = Wall.Create(doc, line, wallType.Id, baseLevel.Id,
                        heightFt, 0, false, config.WallIsStructural);

                    if (wall != null)
                    {
                        // Set structural usage
                        if (config.WallIsStructural)
                        {
                            try
                            {
                                var param = wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT);
                                param?.Set(1);
                            }
                            catch (Exception ex) { StingLog.Warn($"Wall structural flag: {ex.Message}"); }
                        }

                        created.Add(wall);
                        result.CreatedElementIds.Add(wall.Id);
                        if (ws.IsShearWall) result.ShearWallsCreated++;
                        else result.WallsCreated++;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Wall creation failed: {ex.Message}");
                    result.Errors++;
                }
            }

            return created;
        }

        /// <summary>Create Revit structural columns from detected positions.</summary>
        private static List<FamilyInstance> CreateColumns(Document doc, List<DetectedColumnPos> columns,
            StructuralDWGConfig config, Level baseLevel, Level topLevel, ConversionResult result,
            TypeLookupCache cache = null)
        {
            var created = new List<FamilyInstance>();
            double heightFt = config.ColumnHeightMm * MmToFeet;

            // DWG-HIGH-03: Use pre-loaded cache for O(1) type lookup.
            var colSymbol = FindOrCreateColumnType(doc, config, result, cache);
            if (colSymbol == null)
            {
                result.Warnings.Add("No column family found. Columns will be skipped.");
                return created;
            }

            if (!colSymbol.IsActive) colSymbol.Activate();

            foreach (var col in columns)
            {
                try
                {
                    var pt = new XYZ(col.Center.X, col.Center.Y, baseLevel.Elevation);
                    var fi = doc.Create.NewFamilyInstance(
                        pt, colSymbol, baseLevel, StructuralType.Column);

                    if (fi != null)
                    {
                        // Set top level if available
                        if (topLevel != null)
                        {
                            try
                            {
                                var topParam = fi.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
                                topParam?.Set(topLevel.Id);
                                var topOffset = fi.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
                                topOffset?.Set(0.0);
                            }
                            catch (Exception ex) { StingLog.Warn($"Column top level: {ex.Message}"); }
                        }
                        else
                        {
                            // Set unconnected height
                            try
                            {
                                var param = fi.get_Parameter(BuiltInParameter.INSTANCE_LENGTH_PARAM);
                                if (param == null) param = fi.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
                                // Alternative: use top offset from base
                            }
                            catch (Exception ex) { StingLog.Warn($"Column height: {ex.Message}"); }
                        }

                        // Rotate if needed
                        if (Math.Abs(col.Rotation) > 0.01)
                        {
                            try
                            {
                                var axis = Line.CreateBound(pt, pt + XYZ.BasisZ);
                                ElementTransformUtils.RotateElement(doc, fi.Id, axis, col.Rotation);
                            }
                            catch (Exception ex) { StingLog.Warn($"Column rotation: {ex.Message}"); }
                        }

                        created.Add(fi);
                        result.CreatedElementIds.Add(fi.Id);
                        result.ColumnsCreated++;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Column creation failed: {ex.Message}");
                    result.Errors++;
                }
            }

            return created;
        }

        /// <summary>Create Revit structural beams, using detected width when available.</summary>
        private static void CreateBeams(Document doc, List<DetectedBeamLine> beams,
            StructuralDWGConfig config, Level level, ConversionResult result,
            TypeLookupCache cache = null)
        {
            // Cache beam types by width to avoid duplicate type creation
            var typeCache = new Dictionary<string, FamilySymbol>();

            foreach (var beam in beams)
            {
                try
                {
                    // Use detected width if available, otherwise config default
                    double widthMm = beam.WidthDetected
                        ? beam.WidthFt / MmToFeet
                        : config.BeamWidthMm;

                    string typeKey = $"{widthMm:F0}x{config.BeamDepthMm:F0}";
                    if (!typeCache.TryGetValue(typeKey, out var beamSymbol))
                    {
                        beamSymbol = FindOrCreateBeamType(doc, config, widthMm, result, cache);
                        if (beamSymbol == null)
                        {
                            result.Warnings.Add("No beam family found. Beams will be skipped.");
                            return;
                        }
                        if (!beamSymbol.IsActive) beamSymbol.Activate();
                        typeCache[typeKey] = beamSymbol;
                    }

                    var line = Line.CreateBound(
                        new XYZ(beam.Start.X, beam.Start.Y, level.Elevation + config.WallHeightMm * MmToFeet),
                        new XYZ(beam.End.X, beam.End.Y, level.Elevation + config.WallHeightMm * MmToFeet));

                    var fi = doc.Create.NewFamilyInstance(
                        line, beamSymbol, level, StructuralType.Beam);

                    if (fi != null)
                    {
                        result.CreatedElementIds.Add(fi.Id);
                        result.BeamsCreated++;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Beam creation failed: {ex.Message}");
                    result.Errors++;
                }
            }
        }

        /// <summary>Create floor slabs from detected boundary loops.</summary>
        private static void CreateSlabs(Document doc, List<DetectedSlabLoop> slabs,
            StructuralDWGConfig config, Level level, ConversionResult result,
            TypeLookupCache cache = null)
        {
            // DWG-HIGH-03: Use pre-loaded cache for O(1) type lookup.
            var floorType = FindOrCreateFloorType(doc, config, result, cache);
            if (floorType == null) return;

            foreach (var slab in slabs)
            {
                try
                {
                    if (slab.BoundaryPoints.Count < 3) continue;

                    var profile = new CurveLoop();
                    for (int i = 0; i < slab.BoundaryPoints.Count; i++)
                    {
                        var p1 = slab.BoundaryPoints[i];
                        var p2 = slab.BoundaryPoints[(i + 1) % slab.BoundaryPoints.Count];
                        var pt1 = new XYZ(p1.X, p1.Y, level.Elevation);
                        var pt2 = new XYZ(p2.X, p2.Y, level.Elevation);
                        if (pt1.DistanceTo(pt2) > 0.01)
                            profile.Append(Line.CreateBound(pt1, pt2));
                    }

                    var floor = Floor.Create(doc, new List<CurveLoop> { profile },
                        floorType.Id, level.Id);

                    if (floor != null)
                    {
                        // Mark as structural
                        try
                        {
                            var param = floor.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
                            param?.Set(1);
                        }
                        catch (Exception ex) { StingLog.Warn($"Slab structural flag: {ex.Message}"); }

                        result.CreatedElementIds.Add(floor.Id);
                        result.SlabsCreated++;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Slab creation failed: {ex.Message}");
                    result.Errors++;
                }
            }
        }

        /// <summary>Create grid lines from long straight lines.</summary>
        private static void CreateGridLines(Document doc, List<MappedLine> lines,
            StructuralDWGConfig config, Level level, ConversionResult result,
            TypeLookupCache cache = null)
        {
            var gridLines = lines.Where(l => l.ElementType == "Grid Line")
                .OrderByDescending(l => l.Length).ToList();

            // DWG-MED-01: Build index of already-existing model grids so we never create duplicates.
            // Pre-collect once outside the loop; Grid.Curve gives the grid's defining line.
            var existingGridEndpoints = new List<(XYZ A, XYZ B)>();
            try
            {
                var modelGrids = new FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Grid))
                    .Cast<Autodesk.Revit.DB.Grid>()
                    .ToList();

                foreach (var mg in modelGrids)
                {
                    try
                    {
                        if (mg.Curve is Line gl2)
                            existingGridEndpoints.Add((gl2.GetEndPoint(0), gl2.GetEndPoint(1)));
                    }
                    catch (Exception ex) { StingLog.Warn($"Grid endpoint read: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Existing grid pre-scan: {ex.Message}"); }

            // Tracks candidate lines processed in THIS batch (combined with existing model grids).
            var usedPositions = new List<(XYZ, XYZ)>(existingGridEndpoints);

            // Determine starting name counters by scanning existing names to avoid collisions.
            int gridNum = 1;
            char gridChar = 'A';
            try
            {
                var modelGrids2 = new FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Grid))
                    .Cast<Autodesk.Revit.DB.Grid>();

                foreach (var mg in modelGrids2)
                {
                    if (int.TryParse(mg.Name, out int n) && n >= gridNum)
                        gridNum = n + 1;
                    else if (mg.Name?.Length == 1 && char.IsLetter(mg.Name[0])
                             && mg.Name[0] >= gridChar)
                        gridChar = (char)(mg.Name[0] + 1);
                }
            }
            catch (Exception ex) { StingLog.Warn($"Grid name counter init: {ex.Message}"); }

            const double GridDupeTolFt = 1.0; // ~300mm — same endpoint tolerance as before

            foreach (var gl in gridLines)
            {
                if (gl.Length < 3.0) continue; // Min 3 ft (~1m) for a grid

                // DWG-MED-01: Check against BOTH model grids AND previously-created grids this batch.
                bool isDupe = usedPositions.Any(p =>
                    (p.Item1.DistanceTo(gl.Start) < GridDupeTolFt && p.Item2.DistanceTo(gl.End) < GridDupeTolFt) ||
                    (p.Item1.DistanceTo(gl.End) < GridDupeTolFt && p.Item2.DistanceTo(gl.Start) < GridDupeTolFt));
                if (isDupe) continue;

                try
                {
                    var line = Line.CreateBound(
                        new XYZ(gl.Start.X, gl.Start.Y, level.Elevation),
                        new XYZ(gl.End.X, gl.End.Y, level.Elevation));

                    var grid = Autodesk.Revit.DB.Grid.Create(doc, line);
                    if (grid != null)
                    {
                        // Name: horizontal = numbers, vertical = letters (per BS 1192 convention).
                        var dir = (gl.End - gl.Start).Normalize();
                        bool isHorizontal = Math.Abs(dir.X) > Math.Abs(dir.Y);

                        try
                        {
                            grid.Name = isHorizontal ? gridNum++.ToString() : gridChar++.ToString();
                        }
                        catch (Exception ex) { StingLog.Warn($"Grid naming: {ex.Message}"); }

                        result.GridLinesCreated++;
                        usedPositions.Add((gl.Start, gl.End));
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Grid creation failed: {ex.Message}");
                    result.Errors++;
                }
            }
        }

        /// <summary>Create pad foundations below column positions, sized to match column dimensions.</summary>
        private static void CreateFoundations(Document doc, List<DetectedColumnPos> columns,
            StructuralDWGConfig config, Level level, ConversionResult result,
            TypeLookupCache cache = null)
        {
            // DWG-HIGH-03: Use pre-loaded cache; inline fallback when none.
            var allFoundSymbols = cache?.FoundSymbols
                ?? new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .ToList();

            if (allFoundSymbols == null || allFoundSymbols.Count == 0)
            {
                result.Warnings.Add("No foundation family loaded. Foundations skipped.");
                return;
            }

            // DWG-HIGH-02: Foundation name suffix to find by column size.
            // EC7 §6.5.2 pad footings typically sized at 2–3× column width; we match family
            // names that include the column dimensions (b × h in mm) so that, e.g., a
            // 400×400 column uses a "Pad 400x400" family if available.
            string colSuffix = $"{config.ColumnWidthMm:F0}x{config.ColumnDepthMm:F0}";

            // DWG-HIGH-02: Prefer a symbol whose name contains the column dimensions.
            var foundSymbol = allFoundSymbols.FirstOrDefault(s =>
                    s.Name.IndexOf(colSuffix, StringComparison.OrdinalIgnoreCase) >= 0)
                ?? allFoundSymbols.FirstOrDefault(); // fall back to first available

            if (foundSymbol == null)
            {
                result.Warnings.Add("No foundation family loaded. Foundations skipped.");
                return;
            }

            if (!foundSymbol.IsActive) foundSymbol.Activate();

            // Foundation base level is at the underside of the base slab.
            // Per EC7 §6.5.2 and BS EN 1997-1 Table A3 (DA1):
            // foundation level = ground level − minimum embedment.
            // We place at the current import level; structural engineer to adjust embedment.
            double foundElevFt = level.Elevation;

            foreach (var col in columns)
            {
                try
                {
                    var pt = new XYZ(col.Center.X, col.Center.Y, foundElevFt);
                    var fi = doc.Create.NewFamilyInstance(
                        pt, foundSymbol, level, StructuralType.Footing);

                    if (fi != null)
                    {
                        // DWG-HIGH-02: Set foundation plan dimensions to match column size.
                        // Typical pad footing plan ≈ 2.5× column width per EC7 §6.5.2 guidance.
                        // If the family exposes Width/Depth parameters we set them; otherwise
                        // the engineer sizes the foundation in the native Revit type.
                        try
                        {
                            double padWidthFt  = config.ColumnWidthMm  * MmToFeet * 2.5;
                            double padDepthFt  = config.ColumnDepthMm  * MmToFeet * 2.5;
                            double padHeightFt = config.FoundationDepthMm * MmToFeet;

                            (fi.LookupParameter("Width")  ?? fi.LookupParameter("b"))?.Set(padWidthFt);
                            (fi.LookupParameter("Length") ?? fi.LookupParameter("d")
                                ?? fi.LookupParameter("Depth"))?.Set(padDepthFt);
                            (fi.LookupParameter("Height") ?? fi.LookupParameter("h")
                                ?? fi.LookupParameter("Thickness"))?.Set(padHeightFt);
                        }
                        catch (Exception ex)
                        {
                            // Non-fatal — family may not expose these parameters.
                            StingLog.Warn($"Foundation dimension set skipped: {ex.Message}");
                        }

                        result.CreatedElementIds.Add(fi.Id);
                        result.FoundationsCreated++;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Foundation creation failed: {ex.Message}");
                    result.Errors++;
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // TYPE CREATION
        // ══════════════════════════════════════════════════════════════════

        // DWG-HIGH-03: Accept TypeLookupCache for O(1) lookup; inline collector only when cache==null.
        private static WallType FindOrCreateWallType(Document doc, StructuralDWGConfig config,
            ConversionResult result, TypeLookupCache cache = null)
        {
            double thickFt = config.WallThicknessMm * MmToFeet;
            string typeName = $"{config.TypeNamingPrefix} {config.WallMaterial} Wall {config.WallThicknessMm:F0}mm";

            // 1. O(1) cache hit.
            if (cache != null && cache.WallTypes.TryGetValue(typeName, out var cached))
                return cached;

            // 2. Inline fallback (no cache provided, or cache miss on first call).
            var existing = cache != null
                ? null  // cache was built from a full collector — if not in cache it doesn't exist yet
                : new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

            if (existing != null) return existing;

            // 3. Base type: prefer Basic structural wall.
            var baseType = cache?.BaseWallType
                ?? new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault(t => t.Kind == WallKind.Basic)
                ?? new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault();

            if (baseType == null) return null;
            if (!config.CreateNewTypes) return baseType;

            try
            {
                var newType = baseType.Duplicate(typeName) as WallType;
                if (newType != null)
                {
                    result.TypesCreated++;
                    cache?.RegisterWall(typeName, newType); // keep cache coherent within batch
                }
                return newType ?? baseType;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Wall type creation failed: {ex.Message}");
                return baseType;
            }
        }

        // DWG-HIGH-03: Accept TypeLookupCache for O(1) lookup.
        private static FamilySymbol FindOrCreateColumnType(Document doc, StructuralDWGConfig config,
            ConversionResult result, TypeLookupCache cache = null)
        {
            string typeName = $"{config.TypeNamingPrefix} {config.ColumnMaterial} Col {config.ColumnWidthMm:F0}x{config.ColumnDepthMm:F0}";

            // 1. O(1) cache hit.
            if (cache != null && cache.ColumnSymbols.TryGetValue(typeName, out var cached))
                return cached;

            // 2. Inline fallback.
            var existing = cache != null
                ? null
                : new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

            if (existing != null) return existing;

            var baseSymbol = cache?.BaseColumnSymbol
                ?? new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault();

            if (baseSymbol == null) return null;
            if (!config.CreateNewTypes) return baseSymbol;

            try
            {
                var newType = baseSymbol.Duplicate(typeName) as FamilySymbol;
                if (newType != null)
                {
                    // Set b/h dimensions for column cross-section.
                    try
                    {
                        var wParam = newType.LookupParameter("b") ?? newType.LookupParameter("Width");
                        wParam?.Set(config.ColumnWidthMm * MmToFeet);
                        var dParam = newType.LookupParameter("h") ?? newType.LookupParameter("Depth");
                        dParam?.Set(config.ColumnDepthMm * MmToFeet);
                    }
                    catch (Exception ex) { StingLog.Warn($"Column type dims: {ex.Message}"); }

                    result.TypesCreated++;
                    cache?.RegisterColumn(typeName, newType);
                }
                return newType ?? baseSymbol;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Column type creation failed: {ex.Message}");
                return baseSymbol;
            }
        }

        // DWG-HIGH-03: Accept TypeLookupCache for O(1) lookup.
        private static FamilySymbol FindOrCreateBeamType(Document doc, StructuralDWGConfig config,
            double widthMm, ConversionResult result, TypeLookupCache cache = null)
        {
            string typeName = $"{config.TypeNamingPrefix} {config.BeamMaterial} Beam {widthMm:F0}x{config.BeamDepthMm:F0}";

            // 1. O(1) cache hit.
            if (cache != null && cache.BeamSymbols.TryGetValue(typeName, out var cached))
                return cached;

            // 2. Inline fallback.
            var existing = cache != null
                ? null
                : new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

            if (existing != null) return existing;

            var baseSymbol = cache?.BaseBeamSymbol
                ?? new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault();

            if (baseSymbol == null) return null;
            if (!config.CreateNewTypes) return baseSymbol;

            try
            {
                var newType = baseSymbol.Duplicate(typeName) as FamilySymbol;
                if (newType != null)
                {
                    try
                    {
                        var wParam = newType.LookupParameter("b") ?? newType.LookupParameter("Width");
                        wParam?.Set(widthMm * MmToFeet);
                        var dParam = newType.LookupParameter("h") ?? newType.LookupParameter("Depth");
                        dParam?.Set(config.BeamDepthMm * MmToFeet);
                    }
                    catch (Exception ex) { StingLog.Warn($"Beam type dims: {ex.Message}"); }

                    result.TypesCreated++;
                    cache?.RegisterBeam(typeName, newType);
                }
                return newType ?? baseSymbol;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Beam type creation failed: {ex.Message}");
                return baseSymbol;
            }
        }

        // DWG-HIGH-03: Accept TypeLookupCache for O(1) lookup.
        private static FloorType FindOrCreateFloorType(Document doc, StructuralDWGConfig config,
            ConversionResult result, TypeLookupCache cache = null)
        {
            string typeName = $"{config.TypeNamingPrefix} {config.SlabMaterial} Slab {config.SlabThicknessMm:F0}mm";

            // 1. O(1) cache hit.
            if (cache != null && cache.FloorTypes.TryGetValue(typeName, out var cached))
                return cached;

            // 2. Inline fallback.
            var existing = cache != null
                ? null
                : new FilteredElementCollector(doc)
                    .OfClass(typeof(FloorType))
                    .Cast<FloorType>()
                    .FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

            if (existing != null) return existing;

            var baseType = cache?.BaseFloorType
                ?? new FilteredElementCollector(doc)
                    .OfClass(typeof(FloorType))
                    .Cast<FloorType>()
                    .FirstOrDefault();

            if (baseType == null) return null;
            if (!config.CreateNewTypes) return baseType;

            try
            {
                var newType = baseType.Duplicate(typeName) as FloorType;
                if (newType != null)
                {
                    result.TypesCreated++;
                    cache?.RegisterFloor(typeName, newType);
                }
                return newType ?? baseType;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Floor type creation failed: {ex.Message}");
                return baseType;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // JOINING & POST-PROCESSING
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Join intersecting walls at T/L/X junctions.</summary>
        private static void JoinWalls(Document doc, List<Wall> walls, ConversionResult result)
        {
            for (int i = 0; i < walls.Count; i++)
            {
                for (int j = i + 1; j < walls.Count; j++)
                {
                    try
                    {
                        if (!JoinGeometryUtils.AreElementsJoined(doc, walls[i], walls[j]))
                        {
                            // Check if they're close enough to join
                            var bb1 = walls[i].get_BoundingBox(null);
                            var bb2 = walls[j].get_BoundingBox(null);
                            if (bb1 != null && bb2 != null && BoundingBoxesOverlap(bb1, bb2))
                            {
                                JoinGeometryUtils.JoinGeometry(doc, walls[i], walls[j]);
                                result.JoinsPerformed++;
                            }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Wall join: {ex.Message}"); }
                }
            }
        }

        /// <summary>Join columns to nearby walls.</summary>
        private static void JoinColumnsToWalls(Document doc, List<FamilyInstance> columns,
            List<Wall> walls, ConversionResult result)
        {
            foreach (var col in columns)
            {
                foreach (var wall in walls)
                {
                    try
                    {
                        var bb1 = col.get_BoundingBox(null);
                        var bb2 = wall.get_BoundingBox(null);
                        if (bb1 != null && bb2 != null && BoundingBoxesOverlap(bb1, bb2))
                        {
                            if (!JoinGeometryUtils.AreElementsJoined(doc, col, wall))
                            {
                                JoinGeometryUtils.JoinGeometry(doc, col, wall);
                                result.JoinsPerformed++;
                            }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Column-wall join: {ex.Message}"); }
                }
            }
        }

        /// <summary>Merge collinear wall segments into longer walls.</summary>
        private static List<DetectedWallSegment> MergeCollinearWalls(
            List<DetectedWallSegment> walls, double tolerance)
        {
            var merged = new List<DetectedWallSegment>();
            var used = new HashSet<int>();

            for (int i = 0; i < walls.Count; i++)
            {
                if (used.Contains(i)) continue;
                var current = walls[i];
                var dir = (current.End - current.Start).Normalize();
                var start = current.Start;
                var end = current.End;
                // DWG-CRIT-01: Track max thickness across all merged segments instead of using first only
                double maxThickness = current.ThicknessFt;
                bool isShearWall = current.IsShearWall;

                bool didMerge;
                // DWG-MED-01: Add convergence limit to prevent infinite merge loops
                int maxPasses = 10;
                int pass = 0;
                do
                {
                    didMerge = false;
                    for (int j = 0; j < walls.Count; j++)
                    {
                        if (used.Contains(j) || j == i) continue;
                        var other = walls[j];
                        var otherDir = (other.End - other.Start).Normalize();

                        // Check collinearity
                        if (Math.Abs(dir.DotProduct(otherDir)) < 0.98) continue;

                        // Check if endpoints connect
                        bool merged_j = false;
                        if (end.DistanceTo(other.Start) < tolerance)
                        {
                            end = other.End;
                            merged_j = true;
                        }
                        else if (end.DistanceTo(other.End) < tolerance)
                        {
                            end = other.Start;
                            merged_j = true;
                        }
                        else if (start.DistanceTo(other.End) < tolerance)
                        {
                            start = other.Start;
                            merged_j = true;
                        }
                        else if (start.DistanceTo(other.Start) < tolerance)
                        {
                            start = other.End;
                            merged_j = true;
                        }

                        if (merged_j)
                        {
                            used.Add(j);
                            didMerge = true;
                            // DWG-CRIT-01: Use maximum thickness of merged segments
                            if (other.ThicknessFt > maxThickness)
                                maxThickness = other.ThicknessFt;
                            if (other.IsShearWall)
                                isShearWall = true;
                        }
                    }
                    pass++;
                } while (didMerge && pass < maxPasses);

                used.Add(i);
                merged.Add(new DetectedWallSegment
                {
                    Start = start,
                    End = end,
                    ThicknessFt = maxThickness,
                    IsShearWall = isShearWall,
                    LayerName = current.LayerName,
                });
            }

            return merged;
        }

        // ══════════════════════════════════════════════════════════════════
        // AUTO-TAGGING
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Auto-tag all created elements using STING pipeline.</summary>
        private static void AutoTagElements(Document doc, ConversionResult result)
        {
            try
            {
                ModelEngine.AutoTagCreatedElements(doc, result.CreatedElementIds);
                result.ElementsTagged = result.CreatedElementIds.Count;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Auto-tagging failed: {ex.Message}");
                result.Warnings.Add($"Auto-tagging failed: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // GEOMETRY HELPERS
        // ══════════════════════════════════════════════════════════════════

        private static double PointToLineDistance(XYZ lineStart, XYZ lineEnd, XYZ point)
        {
            var lineDir = (lineEnd - lineStart).Normalize();
            var toPoint = point - lineStart;
            var projection = lineDir * toPoint.DotProduct(lineDir);
            return (toPoint - projection).GetLength();
        }

        private static XYZ ProjectOntoLine(XYZ lineStart, XYZ lineEnd, XYZ point)
        {
            var lineDir = (lineEnd - lineStart).Normalize();
            var toPoint = point - lineStart;
            double t = toPoint.DotProduct(lineDir);
            return lineStart + lineDir * t;
        }

        private static double CalculateOverlap(XYZ a1, XYZ a2, XYZ b1, XYZ b2)
        {
            var dir = (a2 - a1).Normalize();
            double a1p = (a1 - a1).DotProduct(dir);
            double a2p = (a2 - a1).DotProduct(dir);
            double b1p = (b1 - a1).DotProduct(dir);
            double b2p = (b2 - a1).DotProduct(dir);
            double minA = Math.Min(a1p, a2p), maxA = Math.Max(a1p, a2p);
            double minB = Math.Min(b1p, b2p), maxB = Math.Max(b1p, b2p);
            return Math.Max(0, Math.Min(maxA, maxB) - Math.Max(minA, minB));
        }

        private static bool BoundingBoxesOverlap(BoundingBoxXYZ bb1, BoundingBoxXYZ bb2)
        {
            double margin = 0.5; // 6 inches tolerance
            return bb1.Min.X - margin <= bb2.Max.X && bb1.Max.X + margin >= bb2.Min.X
                && bb1.Min.Y - margin <= bb2.Max.Y && bb1.Max.Y + margin >= bb2.Min.Y
                && bb1.Min.Z - margin <= bb2.Max.Z && bb1.Max.Z + margin >= bb2.Min.Z;
        }

        private class RectResult { public XYZ Center; public double Width, Height, Rotation; }

        private static RectResult TryBuildRectangle(List<MappedLine> lines, int startIdx,
            double tol, HashSet<int> used)
        {
            var first = lines[startIdx];
            var candidates = new List<int> { startIdx };
            var endpoint = first.End;

            // Try to chain 3 more lines to form a rectangle
            for (int step = 0; step < 3; step++)
            {
                bool found = false;
                for (int j = 0; j < lines.Count; j++)
                {
                    if (used.Contains(j) || candidates.Contains(j)) continue;
                    var other = lines[j];

                    if (endpoint.DistanceTo(other.Start) < tol)
                    {
                        candidates.Add(j);
                        endpoint = other.End;
                        found = true;
                        break;
                    }
                    else if (endpoint.DistanceTo(other.End) < tol)
                    {
                        candidates.Add(j);
                        endpoint = other.Start;
                        found = true;
                        break;
                    }
                }
                if (!found) return null;
            }

            // Check closure
            if (endpoint.DistanceTo(first.Start) > tol) return null;

            // Check right angles (4 lines should form 2 pairs of parallel lines)
            if (candidates.Count != 4) return null;

            // Mark as used
            foreach (int idx in candidates) used.Add(idx);

            // DWG-HIGH-03: Compute oriented bounding box using direction of longest side
            var allPts = new List<XYZ>();
            foreach (int idx in candidates) { allPts.Add(lines[idx].Start); allPts.Add(lines[idx].End); }
            double cx = allPts.Average(p => p.X);
            double cy = allPts.Average(p => p.Y);

            // Find longest side to determine rotation
            double maxLen = 0;
            XYZ longestDir = XYZ.BasisX;
            foreach (int idx in candidates)
            {
                var seg = lines[idx];
                double len = seg.Start.DistanceTo(seg.End);
                if (len > maxLen)
                {
                    maxLen = len;
                    longestDir = (seg.End - seg.Start).Normalize();
                }
            }

            // Compute rotation angle from X-axis
            double rotation = Math.Atan2(longestDir.Y, longestDir.X);

            // Project all points onto the oriented axes to get true width/height
            var perpDir = new XYZ(-longestDir.Y, longestDir.X, 0);
            double minAlongMain = double.MaxValue, maxAlongMain = double.MinValue;
            double minAlongPerp = double.MaxValue, maxAlongPerp = double.MinValue;
            foreach (var pt in allPts)
            {
                double projMain = pt.X * longestDir.X + pt.Y * longestDir.Y;
                double projPerp = pt.X * perpDir.X + pt.Y * perpDir.Y;
                if (projMain < minAlongMain) minAlongMain = projMain;
                if (projMain > maxAlongMain) maxAlongMain = projMain;
                if (projPerp < minAlongPerp) minAlongPerp = projPerp;
                if (projPerp > maxAlongPerp) maxAlongPerp = projPerp;
            }

            double width = maxAlongMain - minAlongMain;
            double height = maxAlongPerp - minAlongPerp;

            // Ensure width >= height (width is along the longest side)
            if (height > width)
            {
                var tmp = width; width = height; height = tmp;
                rotation += Math.PI / 2;
            }

            return new RectResult
            {
                Center = new XYZ(cx, cy, 0),
                Width = width,
                Height = height,
                Rotation = rotation,
            };
        }

        private static List<DetectedColumnPos> DeduplicateColumns(List<DetectedColumnPos> columns, double tol)
        {
            var result = new List<DetectedColumnPos>();
            var used = new HashSet<int>();

            for (int i = 0; i < columns.Count; i++)
            {
                if (used.Contains(i)) continue;
                var best = columns[i];

                for (int j = i + 1; j < columns.Count; j++)
                {
                    if (used.Contains(j)) continue;
                    // DWG-HIGH-04: Reduced from tol*3 to tol*1.5 to avoid merging distinct columns
                    if (columns[i].Center.DistanceTo(columns[j].Center) < tol * 1.5)
                    {
                        // Keep the one with larger area (more accurate detection)
                        if (columns[j].WidthFt * columns[j].DepthFt > best.WidthFt * best.DepthFt)
                            best = columns[j];
                        used.Add(j);
                    }
                }

                used.Add(i);
                result.Add(best);
            }

            return result;
        }

        private static List<XYZ> FindClusterCenters(List<MappedLine> lines, double clusterRadius)
        {
            var centers = new List<XYZ>();
            var midpoints = new List<XYZ>();

            foreach (var line in lines)
                midpoints.Add((line.Start + line.End) * 0.5);

            var used = new HashSet<int>();
            for (int i = 0; i < midpoints.Count; i++)
            {
                if (used.Contains(i)) continue;
                var cluster = new List<XYZ> { midpoints[i] };
                used.Add(i);

                for (int j = i + 1; j < midpoints.Count; j++)
                {
                    if (used.Contains(j)) continue;
                    if (midpoints[i].DistanceTo(midpoints[j]) < clusterRadius)
                    {
                        cluster.Add(midpoints[j]);
                        used.Add(j);
                    }
                }

                double cx = cluster.Average(p => p.X);
                double cy = cluster.Average(p => p.Y);
                centers.Add(new XYZ(cx, cy, 0));
            }

            return centers;
        }

        private static List<List<XYZ>> FindClosedLoops(List<MappedLine> lines, double tolerance)
        {
            var loops = new List<List<XYZ>>();
            var used = new HashSet<int>();

            for (int i = 0; i < lines.Count; i++)
            {
                if (used.Contains(i)) continue;

                var loop = new List<XYZ> { lines[i].Start };
                var endpoint = lines[i].End;
                var chain = new HashSet<int> { i };

                bool closed = false;
                for (int step = 0; step < lines.Count && !closed; step++)
                {
                    bool found = false;
                    for (int j = 0; j < lines.Count; j++)
                    {
                        if (chain.Contains(j)) continue;
                        var other = lines[j];

                        if (endpoint.DistanceTo(other.Start) < tolerance)
                        {
                            loop.Add(endpoint);
                            endpoint = other.End;
                            chain.Add(j);
                            found = true;

                            if (endpoint.DistanceTo(lines[i].Start) < tolerance)
                            {
                                loop.Add(endpoint);
                                closed = true;
                            }
                            break;
                        }
                        else if (endpoint.DistanceTo(other.End) < tolerance)
                        {
                            loop.Add(endpoint);
                            endpoint = other.Start;
                            chain.Add(j);
                            found = true;

                            if (endpoint.DistanceTo(lines[i].Start) < tolerance)
                            {
                                loop.Add(endpoint);
                                closed = true;
                            }
                            break;
                        }
                    }
                    if (!found) break;
                }

                if (closed && loop.Count >= 3)
                {
                    foreach (int idx in chain) used.Add(idx);
                    loops.Add(loop);
                }
            }

            return loops;
        }

        private static double CalculatePolygonArea(List<XYZ> pts)
        {
            double area = 0;
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += pts[i].X * pts[j].Y;
                area -= pts[j].X * pts[i].Y;
            }
            return Math.Abs(area) / 2.0;
        }

        /// <summary>Dismisses non-critical warnings during batch element creation.</summary>
        private class SilentWarningDismisser : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
            {
                var failures = fa.GetFailureMessages();
                foreach (var f in failures)
                {
                    if (f.GetSeverity() == FailureSeverity.Warning)
                        fa.DeleteWarning(f);
                }
                return FailureProcessingResult.Continue;
            }
        }
    }
}
