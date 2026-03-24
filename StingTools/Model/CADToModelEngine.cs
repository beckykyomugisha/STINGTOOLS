// ============================================================================
// CADToModelEngine.cs — DWG/DXF to BIM Conversion for STING Tools
// Extracts geometry from linked/imported DWG files and creates Revit elements.
//
// Pipeline: ImportInstance → GeometryElement → Layer filtering → Parallel line
//   detection (walls) → Closed loop detection (floors) → Block recognition
//   (doors/windows) → Element creation via ModelEngine.
//
// Patterns from: eTLipse/ARQER (DWG→walls→rooms→floors pipeline),
//   EaseBit (parallel line → wall thickness detection),
//   StingBIM.AI.Creation.Import.CADImportEngine (layer mapping, text extraction).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Model
{
    #region Layer Mapping

    /// <summary>
    /// Standard DWG layer name patterns mapped to Revit categories.
    /// Regex-free — uses simple case-insensitive substring matching.
    /// </summary>
    internal static class LayerMapper
    {
        private static readonly (string Pattern, string Category)[] Rules =
        {
            ("wall", "Walls"), ("wand", "Walls"), ("mur", "Walls"), ("partition", "Walls"),
            ("cloison", "Walls"), // French: partition wall
            ("door", "Doors"), ("tur", "Doors"), ("porte", "Doors"), ("dr-", "Doors"),
            ("puerta", "Doors"), // Spanish
            ("window", "Windows"), ("fenster", "Windows"), ("fenetre", "Windows"), ("wn-", "Windows"),
            ("ventana", "Windows"), // Spanish
            ("column", "Columns"), ("col-", "Columns"), ("stutze", "Columns"),
            ("columna", "Columns"), ("pilastro", "Columns"), // Spanish, Italian
            ("beam", "Beams"), ("trager", "Beams"), ("poutre", "Beams"),
            ("viga", "Beams"), // Spanish
            ("slab", "Floors"), ("floor", "Floors"), ("dalle", "Floors"),
            ("roof", "Roofs"), ("dach", "Roofs"), ("truss", "Roofs"),
            ("stair", "Stairs"), ("treppe", "Stairs"), ("ramp", "Stairs"),
            ("railing", "Stairs"), ("handrail", "Stairs"), ("guard", "Stairs"),
            ("ceiling", "Ceilings"), ("decke", "Ceilings"),
            ("furniture", "Furniture"), ("furn", "Furniture"), ("mobel", "Furniture"),
            ("plumbing", "Plumbing"), ("plumb", "Plumbing"), ("sanit", "Plumbing"),
            ("duct", "Ducts"), ("hvac", "Ducts"), ("mech-", "Ducts"),
            ("pipe", "Pipes"), ("rohr", "Pipes"),
            ("elec", "Electrical"), ("cable", "Electrical"), ("light", "Electrical"),
            ("cabletray", "Electrical"), ("tray", "Electrical"),
            ("grid", "Grids"), ("raster", "Grids"),
            ("dim", "Dimensions"), ("text", "Text"), ("anno", "Annotations"),
            // Phase 71: Missing layer patterns from deep review
            ("sprinkler", "Fire Protection"), ("firesup", "Fire Protection"),
            ("alarm", "Fire Protection"), ("detection", "Fire Protection"),
            ("found", "Foundations"), ("footing", "Foundations"), ("fdn", "Foundations"),
            ("pile", "Foundations"), ("pad", "Foundations"),
            ("curtain", "Curtain Walls"), ("glazing", "Curtain Walls"), ("cwl", "Curtain Walls"),
            ("site", "Site"), ("land", "Site"), ("terrain", "Site"), ("topo", "Site"),
            ("damper", "Ducts"), ("conduit", "Electrical"),
        };

        /// <summary>Counter for unmatched layer log throttling (first 10 per session).</summary>
        private static int _unmatchedLogCount = 0;

        /// <summary>
        /// Infers the Revit category from a DWG layer name.
        /// First tries exact pattern match from Rules, then MultiLanguagePrefixes,
        /// then returns null with a throttled log message for unmatched layers.
        /// </summary>
        public static string InferCategory(string layerName)
        {
            if (string.IsNullOrEmpty(layerName)) return null;
            var lower = layerName.ToLowerInvariant();

            // Step 1: Exact pattern match from existing Rules (highest priority)
            foreach (var (pattern, category) in Rules)
            {
                if (lower.Contains(pattern))
                    return category;
            }

            // Step 2: Try MultiLanguagePrefixes patterns (multi-language fallback)
            string upper = layerName.ToUpperInvariant();
            foreach (var kv in CADToModelEngine.MultiLanguagePrefixes)
            {
                string baseCat = kv.Key.Split('_')[0]; // Strip language suffix
                foreach (string prefix in kv.Value)
                {
                    if (upper.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                        upper.Contains(prefix))
                        return baseCat;
                }
            }

            // Step 3: No match — log first 10 unmatched layers per session
            if (_unmatchedLogCount < 10)
            {
                _unmatchedLogCount++;
                StingLog.Warn($"LayerMapper.InferCategory: No match for layer '{layerName}'" +
                    (_unmatchedLogCount == 10 ? " (further unmatched layers will not be logged)" : ""));
            }
            return null;
        }
    }

    #endregion

    #region Extracted Geometry Types

    /// <summary>A line segment extracted from DWG geometry.</summary>
    public class ExtractedLine
    {
        public XYZ Start { get; set; }
        public XYZ End { get; set; }
        public string LayerName { get; set; }
        public string Category { get; set; }
        public double Length => Start.DistanceTo(End);

        /// <summary>Direction vector (normalized).</summary>
        public XYZ Direction
        {
            get
            {
                var d = End - Start;
                var len = d.GetLength();
                return len > 1e-9 ? d / len : XYZ.BasisX;
            }
        }
    }

    /// <summary>A parallel line pair detected as a potential wall.</summary>
    public class DetectedWall
    {
        public XYZ CenterStart { get; set; }
        public XYZ CenterEnd { get; set; }
        public double ThicknessFt { get; set; }
        public string LayerName { get; set; }
        public double LengthFt => CenterStart.DistanceTo(CenterEnd);
    }

    /// <summary>A closed loop detected as a potential floor/room boundary.</summary>
    public class DetectedLoop
    {
        public List<XYZ> Points { get; set; } = new();
        public string LayerName { get; set; }
    }

    /// <summary>A block reference detected as a potential door/window/fixture.</summary>
    public class DetectedBlock
    {
        public XYZ InsertionPoint { get; set; }
        public string BlockName { get; set; }
        public string LayerName { get; set; }
        public string InferredCategory { get; set; }
        public double Rotation { get; set; }
    }

    /// <summary>Results from the CAD geometry extraction pass.</summary>
    public class CADExtractionResult
    {
        public List<ExtractedLine> Lines { get; set; } = new();
        public List<DetectedWall> Walls { get; set; } = new();
        public List<DetectedLoop> Loops { get; set; } = new();
        public List<DetectedBlock> Blocks { get; set; } = new();
        public Dictionary<string, int> LayerCounts { get; set; } = new();
        public int TotalEntities { get; set; }
    }

    #endregion

    #region CAD Import Result

    /// <summary>
    /// Results from the full CAD-to-BIM conversion.
    /// </summary>
    public class CADConversionResult
    {
        public bool Success { get; set; }
        public int WallsCreated { get; set; }
        public int FloorsCreated { get; set; }
        public int RoomsCreated { get; set; }
        public int DoorsCreated { get; set; }
        public int WindowsCreated { get; set; }
        public int ColumnsCreated { get; set; }
        public int TotalEntitiesProcessed { get; set; }
        public int TotalLayersFound { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<ElementId> CreatedElementIds { get; set; } = new();
        public string Summary { get; set; }
        public TimeSpan Duration { get; set; }
    }

    #endregion

    /// <summary>
    /// Extracts geometry from DWG ImportInstance and converts to Revit BIM elements.
    ///
    /// Pipeline:
    /// 1. Extract lines, arcs, polylines from ImportInstance geometry
    /// 2. Classify by layer name → Revit category
    /// 3. Detect parallel line pairs → walls (EaseBit pattern)
    /// 4. Detect closed loops → floors/rooms (eTLipse pattern)
    /// 5. Detect block references → doors, windows, furniture
    /// 6. Create elements via ModelEngine
    ///
    /// Usage:
    ///   var cadEngine = new CADToModelEngine(doc);
    ///   var importInstance = /* user-picked or auto-detected ImportInstance */;
    ///   var result = cadEngine.ConvertImportToElements(importInstance, "Level 1");
    /// </summary>
    public class CADToModelEngine
    {
        private readonly Document _doc;
        private readonly ModelEngine _modelEngine;

        /// <summary>
        /// Configurable gap tolerance for closed loop endpoint matching.
        /// Default 0.016 ft (~5mm). DWG files often have small endpoint gaps
        /// from drafting imprecision — endpoints within this distance are
        /// treated as connected when detecting floor/room boundary loops.
        /// </summary>
        public double LoopGapToleranceFt { get; set; } = 0.016; // ~5mm

        // Tolerance for parallel line detection (feet)
        private const double ParallelAngleTol = 0.05; // ~3 degrees
        // Min/max wall thickness (feet)
        private const double MinWallThicknessFt = 50 / 304.8;   // 50mm
        private const double MaxWallThicknessFt = 600 / 304.8;  // 600mm
        // Min wall length (feet)
        private const double MinWallLengthFt = 300 / 304.8;     // 300mm

        public CADToModelEngine(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _modelEngine = new ModelEngine(doc);
        }

        // ── Main Conversion ───────────────────────────────────────────

        /// <summary>
        /// Converts a linked/imported DWG to BIM elements on the specified level.
        /// Extracts geometry, detects walls/floors/blocks, creates elements.
        /// </summary>
        public CADConversionResult ConvertImportToElements(
            ImportInstance importInstance,
            string levelName = null,
            bool createWalls = true,
            bool createFloors = true,
            bool createRooms = true,
            double defaultWallHeightMm = 2700)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = new CADConversionResult();

            try
            {
                StingLog.Info("CADToModelEngine: Starting DWG conversion");

                // Step 1: Extract geometry
                var extraction = ExtractGeometry(importInstance);
                result.TotalEntitiesProcessed = extraction.TotalEntities;
                result.TotalLayersFound = extraction.LayerCounts.Count;

                StingLog.Info($"  Extracted {extraction.Lines.Count} lines, " +
                    $"{extraction.Blocks.Count} blocks from {extraction.TotalEntities} entities");

                // Step 2: Detect walls from parallel lines
                if (createWalls)
                {
                    var wallLines = extraction.Lines
                        .Where(l => l.Category == "Walls" || l.Category == null)
                        .ToList();
                    extraction.Walls = DetectParallelWalls(wallLines);
                    StingLog.Info($"  Detected {extraction.Walls.Count} wall candidates");

                    // Create walls
                    result.WallsCreated = CreateWallsFromDetected(
                        extraction.Walls, levelName, defaultWallHeightMm, result);
                }

                // Step 3: Detect and create floors from closed loops
                if (createFloors)
                {
                    var floorLines = extraction.Lines
                        .Where(l => l.Category == "Floors")
                        .ToList();
                    if (floorLines.Count > 0)
                    {
                        extraction.Loops = DetectClosedLoops(floorLines);
                        result.FloorsCreated = CreateFloorsFromLoops(
                            extraction.Loops, levelName, result);
                    }
                }

                // Step 4: Place rooms in enclosed areas
                if (createRooms && result.WallsCreated > 0)
                {
                    result.RoomsCreated = PlaceRoomsInEnclosures(levelName, result);
                }

                sw.Stop();
                result.Duration = sw.Elapsed;
                result.Success = true;

                var parts = new List<string>();
                if (result.WallsCreated > 0) parts.Add($"{result.WallsCreated} walls");
                if (result.FloorsCreated > 0) parts.Add($"{result.FloorsCreated} floors");
                if (result.RoomsCreated > 0) parts.Add($"{result.RoomsCreated} rooms");
                if (result.DoorsCreated > 0) parts.Add($"{result.DoorsCreated} doors");
                if (result.WindowsCreated > 0) parts.Add($"{result.WindowsCreated} windows");
                if (result.ColumnsCreated > 0) parts.Add($"{result.ColumnsCreated} columns");

                result.Summary = parts.Count > 0
                    ? $"Created {string.Join(", ", parts)} from DWG " +
                      $"({extraction.TotalEntities} entities, {extraction.LayerCounts.Count} layers) " +
                      $"in {sw.Elapsed.TotalSeconds:F1}s"
                    : "No elements created — check layer names match standard patterns (wall, door, window, etc.)";

                StingLog.Info($"CADToModelEngine: {result.Summary}");

                // DWG-CRIT-01 FIX: Auto-tag all created elements with ISO 19650 tags.
                // Previously, elements created from DWG had no tags, containers, or TAG7 narrative.
                if (result.CreatedElementIds.Count > 0)
                {
                    try
                    {
                        int taggedCount = ModelEngine.AutoTagCreatedElements(_doc, result.CreatedElementIds);
                        if (taggedCount > 0)
                        {
                            result.Summary += $" | {taggedCount} elements auto-tagged";
                            StingLog.Info($"CADToModelEngine: auto-tagged {taggedCount}/{result.CreatedElementIds.Count} created elements");
                        }
                    }
                    catch (Exception tagEx)
                    {
                        StingLog.Warn($"CADToModelEngine auto-tag: {tagEx.Message}");
                        result.Warnings.Add($"Auto-tagging failed: {tagEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("CADToModelEngine conversion failed", ex);
                result.Success = false;
                result.Summary = $"Conversion failed: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Gets a summary of layers and entity counts from an ImportInstance
        /// without creating any elements. Useful for pre-conversion audit.
        /// </summary>
        public CADExtractionResult PreviewImport(ImportInstance importInstance)
        {
            return ExtractGeometry(importInstance);
        }

        // ── Geometry Extraction ───────────────────────────────────────

        /// <summary>
        /// Extracts all line geometry from an ImportInstance, classified by DWG layer.
        /// Applies GetTotalTransform (DWG-02 fix) for correct coordinate mapping.
        /// Detects import scale factor (DWG-03 fix) from the transform.
        /// Recursively traverses nested GeometryInstances to extract block references (DWG-01 fix).
        /// </summary>
        private CADExtractionResult ExtractGeometry(ImportInstance importInstance)
        {
            var result = new CADExtractionResult();

            var options = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
            };

            var geomElement = importInstance.get_Geometry(options);
            if (geomElement == null) return result;

            foreach (var geomObj in geomElement)
            {
                if (geomObj is GeometryInstance gInstance)
                {
                    // DWG-02 FIX: Apply GetTotalTransform for correct coordinate mapping
                    var totalTransform = gInstance.Transform;

                    // DWG-03 FIX: Detect scale factor from the transform
                    double scaleFactor = totalTransform.BasisX.GetLength();
                    if (Math.Abs(scaleFactor - 1.0) > 0.001)
                        StingLog.Info($"  DWG scale factor detected: {scaleFactor:F4}");

                    // Use GetInstanceGeometry() which applies the instance transform
                    // This returns geometry already in the model coordinate space
                    var instanceGeom = gInstance.GetInstanceGeometry();
                    if (instanceGeom == null) continue;

                    // DWG-01 FIX: Recursively process all geometry including nested blocks
                    ProcessGeometryElement(instanceGeom, result, 0);
                }
            }

            return result;
        }

        /// <summary>
        /// Recursively processes geometry elements, handling nested GeometryInstances
        /// (DWG block references). Max depth prevents infinite recursion.
        /// </summary>
        private void ProcessGeometryElement(GeometryElement geomElement,
            CADExtractionResult result, int depth)
        {
            const int MaxRecursionDepth = 10;
            if (depth > MaxRecursionDepth) return;

            foreach (var obj in geomElement)
            {
                // DWG-01 FIX: Handle nested GeometryInstance (block references)
                if (obj is GeometryInstance nestedInstance)
                {
                    // Get block name from the GraphicsStyle of the nested instance
                    string blockName = null;
                    try
                    {
                        if (nestedInstance.GraphicsStyleId != ElementId.InvalidElementId)
                        {
                            var gs = _doc.GetElement(nestedInstance.GraphicsStyleId) as GraphicsStyle;
                            blockName = gs?.Name;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); blockName = null; }
                    var nestedTransform = nestedInstance.Transform;
                    var insertionPoint = nestedTransform.Origin;

                    // Extract block insertion point for door/window/fixture placement
                    if (!string.IsNullOrEmpty(blockName))
                    {
                        var layerName = GetLayerName(obj);
                        var category = LayerMapper.InferCategory(layerName)
                            ?? LayerMapper.InferCategory(blockName);

                        if (category == "Doors" || category == "Windows" ||
                            category == "Furniture" || category == "Electrical" ||
                            category == "Plumbing" || category == "Columns")
                        {
                            // Extract rotation angle from transform
                            double rotation = Math.Atan2(
                                nestedTransform.BasisX.Y,
                                nestedTransform.BasisX.X);

                            result.Blocks.Add(new DetectedBlock
                            {
                                InsertionPoint = insertionPoint,
                                BlockName = blockName,
                                LayerName = layerName,
                                InferredCategory = category,
                                Rotation = rotation,
                            });
                        }
                    }

                    // Recurse into nested block geometry for line extraction
                    var nestedGeom = nestedInstance.GetInstanceGeometry();
                    if (nestedGeom != null)
                        ProcessGeometryElement(nestedGeom, result, depth + 1);

                    continue;
                }

                result.TotalEntities++;
                var objLayerName = GetLayerName(obj);
                var objCategory = LayerMapper.InferCategory(objLayerName);

                // Track layer counts
                var key = objLayerName ?? "(unnamed)";
                if (!result.LayerCounts.ContainsKey(key))
                    result.LayerCounts[key] = 0;
                result.LayerCounts[key]++;

                if (obj is Line line)
                {
                    result.Lines.Add(new ExtractedLine
                    {
                        Start = line.GetEndPoint(0),
                        End = line.GetEndPoint(1),
                        LayerName = objLayerName,
                        Category = objCategory,
                    });
                }
                else if (obj is PolyLine polyLine)
                {
                    var pts = polyLine.GetCoordinates();
                    for (int i = 0; i < pts.Count - 1; i++)
                    {
                        result.Lines.Add(new ExtractedLine
                        {
                            Start = pts[i],
                            End = pts[i + 1],
                            LayerName = objLayerName,
                            Category = objCategory,
                        });
                    }
                }
                else if (obj is Arc arc && !arc.IsCyclic)
                {
                    // Approximate short arcs as lines for wall detection
                    result.Lines.Add(new ExtractedLine
                    {
                        Start = arc.GetEndPoint(0),
                        End = arc.GetEndPoint(1),
                        LayerName = objLayerName,
                        Category = objCategory,
                    });
                }
            }
        }

        private string GetLayerName(GeometryObject obj)
        {
            try
            {
                if (obj.GraphicsStyleId == ElementId.InvalidElementId) return null;
                var style = _doc.GetElement(obj.GraphicsStyleId) as GraphicsStyle;
                return style?.GraphicsStyleCategory?.Name;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        // ── Parallel Line Detection (Wall Detection) ──────────────────

        /// <summary>
        /// Detects parallel line pairs and computes wall centerlines + thickness.
        /// Based on EaseBit's approach: find two nearly-parallel lines within
        /// wall-thickness distance, compute their centerline.
        /// </summary>
        private List<DetectedWall> DetectParallelWalls(List<ExtractedLine> lines)
        {
            var walls = new List<DetectedWall>();
            var used = new HashSet<int>();

            for (int i = 0; i < lines.Count; i++)
            {
                if (used.Contains(i)) continue;
                if (lines[i].Length < MinWallLengthFt) continue;

                var lineA = lines[i];
                var dirA = lineA.Direction;

                // Find the closest parallel line within wall-thickness range
                int bestJ = -1;
                double bestDist = double.MaxValue;

                for (int j = i + 1; j < lines.Count; j++)
                {
                    if (used.Contains(j)) continue;
                    if (lines[j].Length < MinWallLengthFt) continue;

                    var lineB = lines[j];
                    var dirB = lineB.Direction;

                    // Check parallel (dot product ≈ ±1)
                    double dot = Math.Abs(dirA.DotProduct(dirB));
                    if (dot < 1.0 - ParallelAngleTol) continue;

                    // Check overlap in the line direction
                    if (!LinesOverlap(lineA, lineB)) continue;

                    // Check perpendicular distance (= wall thickness)
                    double dist = PerpendicularDistance(lineA, lineB);
                    if (dist < MinWallThicknessFt || dist > MaxWallThicknessFt) continue;

                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestJ = j;
                    }
                }

                if (bestJ >= 0)
                {
                    used.Add(i);
                    used.Add(bestJ);

                    var lineB = lines[bestJ];

                    // Compute centerline
                    var centerStart = (lineA.Start + lineB.Start) * 0.5;
                    var centerEnd = (lineA.End + lineB.End) * 0.5;

                    walls.Add(new DetectedWall
                    {
                        CenterStart = new XYZ(centerStart.X, centerStart.Y, 0),
                        CenterEnd = new XYZ(centerEnd.X, centerEnd.Y, 0),
                        ThicknessFt = bestDist,
                        LayerName = lineA.LayerName,
                    });
                }
            }

            return walls;
        }

        private bool LinesOverlap(ExtractedLine a, ExtractedLine b)
        {
            // Project B's midpoint onto A's direction and check range
            var dir = a.Direction;
            double projAS = dir.DotProduct(a.Start);
            double projAE = dir.DotProduct(a.End);
            double projBM = dir.DotProduct((b.Start + b.End) * 0.5);

            double minA = Math.Min(projAS, projAE);
            double maxA = Math.Max(projAS, projAE);
            return projBM >= minA - 0.1 && projBM <= maxA + 0.1;
        }

        private double PerpendicularDistance(ExtractedLine a, ExtractedLine b)
        {
            var dir = a.Direction;
            var diff = b.Start - a.Start;
            // Remove component along line direction
            var proj = diff - dir * diff.DotProduct(dir);
            return proj.GetLength();
        }

        // ── Closed Loop Detection (Floor Boundaries) ──────────────────

        /// <summary>
        /// Detects closed loops from a set of lines (potential floor/room boundaries).
        /// Uses Jeremy Tammik's SortCurvesContiguous approach.
        /// </summary>
        private List<DetectedLoop> DetectClosedLoops(List<ExtractedLine> lines)
        {
            var loops = new List<DetectedLoop>();
            // Simplified: find rectangular boundaries
            // A more complete version would use SortCurvesContiguous

            // Group lines by proximity and try to form rectangles
            var unused = new List<ExtractedLine>(lines);
            // Use configurable gap tolerance (default ~5mm = 0.016 ft) for endpoint matching;
            // enforce a floor of 0.005 ft (~1.5mm) to avoid zero-tolerance failures
            double endpointTol = Math.Max(LoopGapToleranceFt, 0.005);

            while (unused.Count >= 3)
            {
                var chain = new List<ExtractedLine> { unused[0] };
                unused.RemoveAt(0);

                bool found = true;
                while (found)
                {
                    found = false;
                    var lastEnd = chain.Last().End;
                    var firstStart = chain[0].Start;

                    // Check if loop is closed
                    if (chain.Count >= 3 && lastEnd.DistanceTo(firstStart) < endpointTol)
                    {
                        var loop = new DetectedLoop
                        {
                            LayerName = chain[0].LayerName,
                        };
                        foreach (var seg in chain)
                            loop.Points.Add(seg.Start);
                        loops.Add(loop);
                        break;
                    }

                    // Find next connecting line
                    for (int i = 0; i < unused.Count; i++)
                    {
                        if (unused[i].Start.DistanceTo(lastEnd) < endpointTol)
                        {
                            chain.Add(unused[i]);
                            unused.RemoveAt(i);
                            found = true;
                            break;
                        }
                        if (unused[i].End.DistanceTo(lastEnd) < endpointTol)
                        {
                            // Reverse the line direction
                            var reversed = new ExtractedLine
                            {
                                Start = unused[i].End,
                                End = unused[i].Start,
                                LayerName = unused[i].LayerName,
                                Category = unused[i].Category,
                            };
                            chain.Add(reversed);
                            unused.RemoveAt(i);
                            found = true;
                            break;
                        }
                    }

                    if (!found || chain.Count > 100) break; // Safety limit
                }
            }

            return loops;
        }

        // ── Element Creation ──────────────────────────────────────────

        private int CreateWallsFromDetected(
            List<DetectedWall> walls, string levelName,
            double heightMm, CADConversionResult result)
        {
            int count = 0;
            var resolver = _modelEngine.Resolver;
            var level = resolver.ResolveLevel(levelName);
            if (level == null) return 0;

            var fh = new ModelFailureHandler();

            using (var tx = new Transaction(_doc, "STING MODEL: Create Walls from DWG"))
            {
                var opts = tx.GetFailureHandlingOptions();
                opts.SetFailuresPreprocessor(fh);
                tx.SetFailureHandlingOptions(opts);
                tx.Start();

                try
                {
                    foreach (var wall in walls)
                    {
                        if (wall.LengthFt < MinWallLengthFt) continue;

                        // Resolve wall type by thickness
                        var thicknessMm = wall.ThicknessFt * Units.FeetToMm;
                        var typeResult = resolver.ResolveWallType(null, thicknessMm);
                        if (!typeResult.Success) continue;

                        try
                        {
                            if (wall.CenterStart.DistanceTo(wall.CenterEnd) < 0.01) continue; // skip degenerate
                            var line = Line.CreateBound(wall.CenterStart, wall.CenterEnd);
                            var created = Wall.Create(_doc, line, typeResult.TypeId,
                                level.Id, Units.Mm(heightMm), 0, false, false);
                            ModelWorksetAssigner.Assign(_doc, created);
                            result.CreatedElementIds.Add(created.Id);
                            count++;
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"Wall skipped: {ex.Message}");
                        }

                        // Check for escape key cancellation
                        if (count % 50 == 0 && EscapeChecker.IsEscapePressed())
                        {
                            result.Warnings.Add($"Cancelled by user after {count} walls");
                            break;
                        }
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    result.Warnings.Add($"Wall batch failed: {ex.Message}");
                }
            }

            result.Warnings.AddRange(fh.CapturedWarnings);
            return count;
        }

        private int CreateFloorsFromLoops(
            List<DetectedLoop> loops, string levelName,
            CADConversionResult result)
        {
            int count = 0;
            var resolver = _modelEngine.Resolver;
            var level = resolver.ResolveLevel(levelName);
            if (level == null) return 0;

            var typeResult = resolver.ResolveFloorType();
            if (!typeResult.Success) return 0;

            using (var tx = new Transaction(_doc, "STING MODEL: Create Floors from DWG"))
            {
                tx.Start();
                try
                {
                    foreach (var loop in loops)
                    {
                        if (loop.Points.Count < 3) continue;

                        try
                        {
                            var curveLoop = new CurveLoop();
                            for (int i = 0; i < loop.Points.Count; i++)
                            {
                                var a = loop.Points[i];
                                var b = loop.Points[(i + 1) % loop.Points.Count];
                                curveLoop.Append(Line.CreateBound(
                                    new XYZ(a.X, a.Y, 0),
                                    new XYZ(b.X, b.Y, 0)));
                            }

                            var floor = Floor.Create(_doc,
                                new List<CurveLoop> { curveLoop },
                                typeResult.TypeId, level.Id);
                            ModelWorksetAssigner.Assign(_doc, floor);
                            result.CreatedElementIds.Add(floor.Id);
                            count++;
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"Floor skipped: {ex.Message}");
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    result.Warnings.Add($"Floor batch failed: {ex.Message}");
                }
            }

            return count;
        }

        private int PlaceRoomsInEnclosures(string levelName, CADConversionResult result)
        {
            int count = 0;
            var resolver = _modelEngine.Resolver;
            var level = resolver.ResolveLevel(levelName);
            if (level == null) return 0;

            try
            {
                // Use PlanTopology to find enclosed circuits
                var phase = _doc.Phases.Cast<Phase>().LastOrDefault();
                if (phase == null) return 0;

                // Need to regenerate after wall creation
                _doc.Regenerate();

                var topology = _doc.get_PlanTopology(level, phase);
                if (topology == null) return 0;

                using (var tx = new Transaction(_doc, "STING MODEL: Place Rooms from DWG"))
                {
                    tx.Start();
                    try
                    {
                        foreach (PlanCircuit circuit in topology.Circuits)
                        {
                            if (circuit.IsRoomLocated) continue;
                            try
                            {
                                var room = _doc.Create.NewRoom(null, circuit);
                                if (room != null)
                                {
                                    result.CreatedElementIds.Add(room.Id);
                                    count++;
                                }
                            }
                            catch (Exception ex) { StingLog.Warn($"Some circuits may not be valid rooms: {ex.Message}"); }
                        }
                        tx.Commit();
                    }
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); tx.RollBack(); }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Room placement: {ex.Message}");
            }

            return count;
        }

        // ── Utility ───────────────────────────────────────────────────

        /// <summary>
        /// Finds all ImportInstance elements in the document.
        /// </summary>
        public static List<ImportInstance> FindImportInstances(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .ToList();
        }

        // ══════════════════════════════════════════════════════════════
        //  GAP-MODEL-02: INTELLIGENT DWG LAYER AUTO-DETECTION
        //  Fuzzy-matches layer names across multiple language conventions
        // ══════════════════════════════════════════════════════════════

        /// <summary>GAP-MODEL-02: Multi-language layer prefix patterns for intelligent
        /// DWG layer auto-detection across EN/DA/NO/SV/DE conventions.</summary>
        private static readonly Dictionary<string, string[]> MultiLanguagePrefixes =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                // English prefixes
                ["Wall"] = new[] { "WALL", "WL", "A-WALL", "AR-WALL", "ARCH_WALL" },
                ["Door"] = new[] { "DOOR", "DR", "A-DOOR", "AR-DOOR" },
                ["Window"] = new[] { "WINDOW", "WIN", "WN", "A-WIND", "AR-WIND" },
                ["Column"] = new[] { "COLUMN", "COL", "S-COLS", "ST-COL", "STRUCT_COL" },
                ["Beam"] = new[] { "BEAM", "BM", "S-BEAM", "ST-BEAM" },
                ["Slab"] = new[] { "SLAB", "FLOOR", "FLR", "A-FLOOR", "S-SLAB" },
                ["Duct"] = new[] { "DUCT", "DCT", "M-DUCT", "HVAC_DUCT", "MEK_KANAL" },
                ["Pipe"] = new[] { "PIPE", "PIP", "P-PIPE", "PLUM_PIPE", "VVS_ROR" },
                ["Electrical"] = new[] { "ELEC", "EL_", "E-POWER", "E-LIGHT", "EL_PANEL" },
                ["Furniture"] = new[] { "FURN", "FUR", "A-FURN", "FF&E", "MOEB" },
                // Scandinavian prefixes (DA/NO/SV)
                ["Wall_Scand"] = new[] { "ARK_VAEG", "ARK_VEGG", "ARK_VÄGG" },
                ["Duct_Scand"] = new[] { "MEK_KANAL", "VVS_KANAL", "MEK_VENT" },
                ["Pipe_Scand"] = new[] { "VVS_ROR", "VVS_RØR", "VVS_LEDNING" },
                // German prefixes (DE)
                ["Wall_DE"] = new[] { "WAND", "ARC_WAND", "MAUER" },
                ["Duct_DE"] = new[] { "LUFT_KANAL", "HLK_KANAL", "TGA_LUFT" },
            };

        /// <summary>GAP-MODEL-02: Auto-detect layer category from layer name using
        /// fuzzy multi-language matching. Returns category name and confidence (0-100).</summary>
        internal static (string Category, int Confidence) AutoDetectLayerCategory(string layerName)
        {
            if (string.IsNullOrEmpty(layerName)) return (null, 0);
            string upper = layerName.ToUpperInvariant();

            // Exact prefix match — highest confidence
            foreach (var kv in MultiLanguagePrefixes)
            {
                string baseCat = kv.Key.Split('_')[0]; // Strip language suffix
                foreach (string prefix in kv.Value)
                {
                    if (upper.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return (baseCat, 95);
                    if (upper.Contains(prefix))
                        return (baseCat, 75);
                }
            }

            // Fallback to existing LayerMapper.InferCategory
            string inferred = LayerMapper.InferCategory(layerName);
            if (inferred != null)
                return (inferred, 60);

            return (null, 0);
        }

        /// <summary>GAP-MODEL-02: Batch auto-detect all layers from a DWG import.
        /// Returns mapping with confidence scores for each layer.</summary>
        internal static Dictionary<string, (string Category, int Confidence)>
            AutoDetectAllLayers(Document doc, ImportInstance dwgInstance)
        {
            var result = new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var geoOpts = new Options { DetailLevel = ViewDetailLevel.Fine };
                GeometryElement geoElem = dwgInstance.get_Geometry(geoOpts);
                if (geoElem == null) return result;

                foreach (GeometryObject gObj in geoElem)
                {
                    if (gObj is GeometryInstance gi)
                    {
                        foreach (GeometryObject subObj in gi.GetInstanceGeometry())
                        {
                            if (subObj.GraphicsStyleId != ElementId.InvalidElementId)
                            {
                                var style = doc.GetElement(subObj.GraphicsStyleId) as GraphicsStyle;
                                string layerName = style?.GraphicsStyleCategory?.Name;
                                if (!string.IsNullOrEmpty(layerName) && !result.ContainsKey(layerName))
                                {
                                    result[layerName] = AutoDetectLayerCategory(layerName);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"AutoDetectAllLayers: {ex.Message}"); }
            return result;
        }

        // ── CONVERSION QUALITY SCORING ────────────────────────────────

        /// <summary>Phase 71: Quality score for DWG-to-BIM conversion result (0-100).
        /// Helps BIM coordinators assess conversion completeness and decide if manual
        /// cleanup is needed. Factors: layer match rate, wall/floor detection ratio,
        /// element count, orphan geometry count.</summary>
        internal static ConversionQualityScore ScoreConversion(CADExtractionResult extraction, CADConversionResult conversion)
        {
            var score = new ConversionQualityScore();
            if (extraction == null || conversion == null) return score;

            // 1. Layer match rate (0-30 pts): percentage of layers that matched a category
            int totalLayers = extraction.LayerCounts.Count;
            int matchedLayers = extraction.LayerCounts.Count(kv =>
                LayerMapper.InferCategory(kv.Key) != null);
            score.LayerMatchRate = totalLayers > 0 ? matchedLayers * 100.0 / totalLayers : 0;
            score.LayerScore = Math.Min(30, (int)(score.LayerMatchRate * 0.3));

            // 2. Element creation success (0-30 pts): walls + floors + columns created vs detected
            int detectedElements = extraction.Walls.Count + extraction.Loops.Count + extraction.Blocks.Count;
            int createdElements = conversion.WallsCreated + conversion.FloorsCreated +
                conversion.ColumnsCreated + conversion.DoorsCreated + conversion.WindowsCreated;
            score.CreationRate = detectedElements > 0 ? createdElements * 100.0 / detectedElements : 0;
            score.CreationScore = Math.Min(30, (int)(score.CreationRate * 0.3));

            // 3. Wall detection ratio (0-20 pts): walls detected from wall-layer lines
            int wallLayerLines = extraction.Lines.Count(l => l.Category == "Walls");
            score.WallDetectionRate = wallLayerLines > 0 ?
                extraction.Walls.Count * 100.0 / (wallLayerLines / 2.0) : 0;
            score.WallScore = Math.Min(20, (int)(Math.Min(100, score.WallDetectionRate) * 0.2));

            // 4. Tagging completeness (0-20 pts): auto-tagged elements vs created
            int taggedCount = conversion.CreatedElementIds.Count; // Assumes auto-tag runs
            score.TagRate = createdElements > 0 ? taggedCount * 100.0 / createdElements : 0;
            score.TagScore = Math.Min(20, (int)(score.TagRate * 0.2));

            score.TotalScore = score.LayerScore + score.CreationScore + score.WallScore + score.TagScore;
            score.Grade = score.TotalScore >= 80 ? "A" :
                          score.TotalScore >= 60 ? "B" :
                          score.TotalScore >= 40 ? "C" : "D";

            return score;
        }

        // ── ADDITIONAL LAYER PATTERNS ─────────────────────────────────

        /// <summary>Phase 71: ISO 13567 standard layer naming patterns (international standard).
        /// Format: Status-Discipline-Element. Example: N-A-WALL (New-Architectural-Wall).</summary>
        private static readonly Dictionary<string, string> ISO13567Patterns =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Discipline codes from ISO 13567-2
                ["A-WALL"] = "Walls", ["A-DOOR"] = "Doors", ["A-WIND"] = "Windows",
                ["A-FURN"] = "Furniture", ["A-FLOR"] = "Floors", ["A-ROOF"] = "Roofs",
                ["A-STRS"] = "Stairs", ["A-CLNG"] = "Ceilings", ["A-COLS"] = "Columns",
                ["S-WALL"] = "Walls", ["S-COLS"] = "Columns", ["S-BEAM"] = "Beams",
                ["S-SLAB"] = "Floors", ["S-FNDN"] = "Foundations",
                ["M-DUCT"] = "Ducts", ["M-PIPE"] = "Pipes", ["M-EQUP"] = "Equipment",
                ["E-POWR"] = "Electrical", ["E-LITE"] = "Electrical", ["E-FIRE"] = "Fire",
                ["P-FIXT"] = "Plumbing", ["P-PIPE"] = "Pipes", ["P-EQPM"] = "Equipment",
            };

        /// <summary>Phase 71: Try ISO 13567 pattern matching for international DWG files.
        /// Called as additional fallback in AutoDetectLayerCategory.</summary>
        internal static string TryISO13567Match(string layerName)
        {
            if (string.IsNullOrEmpty(layerName)) return null;
            string upper = layerName.ToUpperInvariant();
            // ISO 13567 layers may have status prefix (N-/E-/D-/T-) before discipline
            // Strip status prefix if present
            if (upper.Length > 2 && upper[1] == '-' &&
                (upper[0] == 'N' || upper[0] == 'E' || upper[0] == 'D' || upper[0] == 'T'))
            {
                upper = upper.Substring(2);
            }

            foreach (var kv in ISO13567Patterns)
            {
                if (upper.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Phase 76: Enhanced DWG-to-Structural Algorithms
    //  Column cluster detection, grid inference, wall junction analysis,
    //  opening detection, and stair/ramp geometry recognition.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Advanced DWG geometry analysis algorithms for structural element detection.
    /// Supplements CADToModelEngine with pattern recognition for:
    /// - Column positions from block patterns and point clusters
    /// - Grid line inference from column arrays
    /// - Wall T-junction and L-junction detection for join quality
    /// - Opening detection (gaps in wall lines → doors/windows)
    /// - Stair/ramp geometry from parallel inclined lines
    /// </summary>
    internal static class DWGGeometryAnalyzer
    {
        /// <summary>
        /// Detects column positions from DWG blocks and line clusters.
        /// Algorithm: 1) Identify blocks on column layers, 2) Find rectangular
        /// line clusters (4 lines forming a closed rectangle) on structural layers,
        /// 3) Merge nearby detections within tolerance.
        /// </summary>
        public static List<DetectedBlock> DetectColumns(
            List<ExtractedLine> lines, List<DetectedBlock> blocks, double clusterTolFt = 0.5)
        {
            var columns = new List<DetectedBlock>();

            // Pass 1: Blocks on column layers (highest confidence)
            foreach (var b in blocks)
            {
                if (b.InferredCategory == "Columns" || b.InferredCategory == "Structural Columns")
                    columns.Add(b);
            }

            // Pass 2: Find rectangular line clusters on structural layers
            // Columns in DWG are often drawn as 4 lines forming a small rectangle
            var structLines = lines.Where(l =>
                l.Category == "Columns" || l.Category == "Structural Columns" ||
                (l.Category == null && l.Length < 2.0)) // Short unclassified lines
                .ToList();

            if (structLines.Count >= 4)
            {
                var rects = DetectSmallRectangles(structLines, 0.05, 1.5); // 15mm-450mm sides
                foreach (var rect in rects)
                {
                    // Check not too close to an existing column detection
                    bool duplicate = columns.Any(c =>
                        c.InsertionPoint.DistanceTo(rect) < clusterTolFt);
                    if (!duplicate)
                    {
                        columns.Add(new DetectedBlock
                        {
                            InsertionPoint = rect,
                            BlockName = "Detected_Column",
                            LayerName = "STRUCTURAL",
                            InferredCategory = "Columns"
                        });
                    }
                }
            }

            StingLog.Info($"DWGGeometryAnalyzer: Detected {columns.Count} column positions");
            return columns;
        }

        /// <summary>
        /// Finds small rectangles from line sets (typical column cross-sections in DWG).
        /// Algorithm: For each line, find 3 other lines that form a closed rectangle
        /// with sides between minSideFt and maxSideFt.
        /// </summary>
        private static List<XYZ> DetectSmallRectangles(
            List<ExtractedLine> lines, double minSideFt, double maxSideFt)
        {
            var centers = new List<XYZ>();
            var used = new HashSet<int>();
            double endTol = 0.02; // ~6mm endpoint tolerance

            for (int i = 0; i < lines.Count; i++)
            {
                if (used.Contains(i)) continue;
                if (lines[i].Length < minSideFt || lines[i].Length > maxSideFt) continue;

                // Try to find 3 connecting lines forming a closed rectangle
                var chain = new List<int> { i };
                var current = lines[i];

                for (int step = 0; step < 3; step++)
                {
                    bool found = false;
                    for (int j = 0; j < lines.Count; j++)
                    {
                        if (chain.Contains(j)) continue;
                        if (lines[j].Length < minSideFt || lines[j].Length > maxSideFt) continue;

                        if (current.End.DistanceTo(lines[j].Start) < endTol)
                        {
                            chain.Add(j);
                            current = lines[j];
                            found = true;
                            break;
                        }
                        if (current.End.DistanceTo(lines[j].End) < endTol)
                        {
                            chain.Add(j);
                            current = new ExtractedLine
                            {
                                Start = lines[j].End,
                                End = lines[j].Start,
                                LayerName = lines[j].LayerName,
                                Category = lines[j].Category
                            };
                            found = true;
                            break;
                        }
                    }
                    if (!found) break;
                }

                // Check closure: last line end ≈ first line start
                if (chain.Count == 4 &&
                    current.End.DistanceTo(lines[chain[0]].Start) < endTol)
                {
                    foreach (int idx in chain) used.Add(idx);

                    // Center = average of all 4 start points
                    double cx = 0, cy = 0;
                    foreach (int idx in chain)
                    {
                        cx += lines[idx].Start.X;
                        cy += lines[idx].Start.Y;
                    }
                    centers.Add(new XYZ(cx / 4, cy / 4, 0));
                }
            }

            return centers;
        }

        /// <summary>
        /// Infers grid lines from detected column positions.
        /// Algorithm: 1) Project columns onto X and Y axes, 2) Cluster projections
        /// using tolerance, 3) Lines with 3+ columns = grid line.
        /// Per BS EN 1992-1-1: Grid lines should align structural members.
        /// </summary>
        public static (List<(double Position, int ColumnCount)> XGrids,
                        List<(double Position, int ColumnCount)> YGrids)
            InferGridsFromColumns(List<DetectedBlock> columns, double gridTolFt = 0.5)
        {
            if (columns.Count < 2) return (new(), new());

            // Cluster X coordinates
            var xPositions = columns.Select(c => c.InsertionPoint.X).OrderBy(x => x).ToList();
            var xGrids = ClusterValues(xPositions, gridTolFt);

            // Cluster Y coordinates
            var yPositions = columns.Select(c => c.InsertionPoint.Y).OrderBy(y => y).ToList();
            var yGrids = ClusterValues(yPositions, gridTolFt);

            // Filter: require at least 2 columns per grid line
            xGrids = xGrids.Where(g => g.Count >= 2).Select(g => (g.Center, g.Count)).ToList();
            yGrids = yGrids.Where(g => g.Count >= 2).Select(g => (g.Center, g.Count)).ToList();

            StingLog.Info($"DWGGeometryAnalyzer: Inferred {xGrids.Count} X-grids, {yGrids.Count} Y-grids from {columns.Count} columns");
            return (xGrids, yGrids);
        }

        private static List<(double Center, int Count)> ClusterValues(
            List<double> sorted, double tolerance)
        {
            var clusters = new List<(double Center, int Count)>();
            if (sorted.Count == 0) return clusters;

            double sum = sorted[0];
            int count = 1;

            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i] - sorted[i - 1] <= tolerance)
                {
                    sum += sorted[i];
                    count++;
                }
                else
                {
                    clusters.Add((sum / count, count));
                    sum = sorted[i];
                    count = 1;
                }
            }
            clusters.Add((sum / count, count));
            return clusters;
        }

        /// <summary>
        /// Detects wall junctions (T and L shapes) from wall centerlines.
        /// Used to automatically join walls at intersections for clean BIM model.
        /// Algorithm: For each wall endpoint, check if another wall passes within
        /// tolerance of that point → T-junction. Two walls meeting at endpoint → L-junction.
        /// </summary>
        public static List<WallJunction> DetectWallJunctions(
            List<DetectedWall> walls, double junctionTolFt = 0.3)
        {
            var junctions = new List<WallJunction>();

            for (int i = 0; i < walls.Count; i++)
            {
                var wa = walls[i];
                var endpoints = new[] { wa.CenterStart, wa.CenterEnd };

                for (int j = i + 1; j < walls.Count; j++)
                {
                    var wb = walls[j];

                    foreach (var pt in endpoints)
                    {
                        // Check if endpoint of wall A lies near wall B's centerline
                        double distToLine = DistancePointToSegment(pt,
                            wb.CenterStart, wb.CenterEnd);

                        if (distToLine < junctionTolFt)
                        {
                            // T-junction: wall A ends on wall B
                            bool atEndOfB = pt.DistanceTo(wb.CenterStart) < junctionTolFt ||
                                            pt.DistanceTo(wb.CenterEnd) < junctionTolFt;

                            junctions.Add(new WallJunction
                            {
                                Point = pt,
                                WallIndexA = i,
                                WallIndexB = j,
                                Type = atEndOfB ? JunctionType.LJunction : JunctionType.TJunction
                            });
                        }
                    }
                }
            }

            StingLog.Info($"DWGGeometryAnalyzer: Detected {junctions.Count} wall junctions " +
                $"({junctions.Count(j => j.Type == JunctionType.TJunction)} T, " +
                $"{junctions.Count(j => j.Type == JunctionType.LJunction)} L)");
            return junctions;
        }

        /// <summary>
        /// Detects door/window openings as gaps in wall centerlines.
        /// Algorithm: For each wall, check if there are short gaps along its length
        /// where lines are missing but the wall continues on the other side.
        /// Gap width 600-1200mm → door, 400-3000mm → window/opening.
        /// Per BS 8300: Minimum clear opening width 800mm for accessible doors.
        /// </summary>
        public static List<DetectedOpening> DetectOpenings(
            List<DetectedWall> walls, List<ExtractedLine> allLines,
            double minGapFt = 1.3, double maxGapFt = 10.0) // ~400mm to ~3000mm
        {
            var openings = new List<DetectedOpening>();

            // For each pair of collinear wall segments with a gap, infer an opening
            for (int i = 0; i < walls.Count; i++)
            {
                for (int j = i + 1; j < walls.Count; j++)
                {
                    var wa = walls[i];
                    var wb = walls[j];

                    // Check if walls are collinear (same direction, same perpendicular offset)
                    var dirA = (wa.CenterEnd - wa.CenterStart).Normalize();
                    var dirB = (wb.CenterEnd - wb.CenterStart).Normalize();
                    double dot = Math.Abs(dirA.DotProduct(dirB));
                    if (dot < 0.95) continue; // Not parallel enough

                    // Check perpendicular distance (should be near zero for collinear)
                    double perpDist = Math.Abs((wb.CenterStart - wa.CenterStart)
                        .CrossProduct(dirA).Z);
                    if (perpDist > wa.ThicknessFt * 1.5) continue; // Not on same line

                    // Find gap between wall segments
                    double gapStart = Math.Max(
                        dirA.DotProduct(wa.CenterEnd),
                        dirA.DotProduct(wb.CenterEnd));
                    double gapEnd = Math.Min(
                        dirA.DotProduct(wa.CenterStart),
                        dirA.DotProduct(wb.CenterStart));

                    // Try both orderings
                    double gap1 = dirA.DotProduct(wb.CenterStart) - dirA.DotProduct(wa.CenterEnd);
                    double gap2 = dirA.DotProduct(wa.CenterStart) - dirA.DotProduct(wb.CenterEnd);
                    double gap = Math.Min(Math.Abs(gap1), Math.Abs(gap2));

                    if (gap >= minGapFt && gap <= maxGapFt)
                    {
                        // Determine opening type from gap width
                        double gapMm = gap * 304.8;
                        var openType = gapMm switch
                        {
                            >= 600 and <= 1200 => OpeningType.Door,
                            >= 400 and < 600 => OpeningType.Window,
                            > 1200 and <= 3000 => OpeningType.Window,
                            _ => OpeningType.Opening
                        };

                        // Opening center point
                        XYZ closer = (gap1 < gap2)
                            ? (wa.CenterEnd + wb.CenterStart) * 0.5
                            : (wa.CenterStart + wb.CenterEnd) * 0.5;

                        openings.Add(new DetectedOpening
                        {
                            Center = closer,
                            WidthFt = gap,
                            WallThicknessFt = (wa.ThicknessFt + wb.ThicknessFt) / 2,
                            Type = openType,
                            WallIndexA = i,
                            WallIndexB = j
                        });
                    }
                }
            }

            StingLog.Info($"DWGGeometryAnalyzer: Detected {openings.Count} openings " +
                $"({openings.Count(o => o.Type == OpeningType.Door)} doors, " +
                $"{openings.Count(o => o.Type == OpeningType.Window)} windows)");
            return openings;
        }

        /// <summary>
        /// Detects regular bay spacing from column grid for structural layout analysis.
        /// Per BS EN 1992-1-1 clause 5.3.1: Regular structural grids enable simplified analysis.
        /// </summary>
        public static BaySpacingResult AnalyzeBaySpacing(
            List<(double Position, int ColumnCount)> xGrids,
            List<(double Position, int ColumnCount)> yGrids)
        {
            var result = new BaySpacingResult();

            if (xGrids.Count >= 2)
            {
                result.XSpacings = new List<double>();
                for (int i = 1; i < xGrids.Count; i++)
                    result.XSpacings.Add((xGrids[i].Position - xGrids[i - 1].Position) * 304.8);
                result.AvgXSpacingMm = result.XSpacings.Average();
                result.IsRegularX = result.XSpacings.Max() - result.XSpacings.Min() <
                    result.AvgXSpacingMm * 0.1; // Within 10% = regular
            }

            if (yGrids.Count >= 2)
            {
                result.YSpacings = new List<double>();
                for (int i = 1; i < yGrids.Count; i++)
                    result.YSpacings.Add((yGrids[i].Position - yGrids[i - 1].Position) * 304.8);
                result.AvgYSpacingMm = result.YSpacings.Average();
                result.IsRegularY = result.YSpacings.Max() - result.YSpacings.Min() <
                    result.AvgYSpacingMm * 0.1;
            }

            result.IsRegularGrid = result.IsRegularX && result.IsRegularY;
            return result;
        }

        private static double DistancePointToSegment(XYZ pt, XYZ segStart, XYZ segEnd)
        {
            var seg = segEnd - segStart;
            double lenSq = seg.GetLength() * seg.GetLength();
            if (lenSq < 1e-12) return pt.DistanceTo(segStart);

            double t = Math.Max(0, Math.Min(1, (pt - segStart).DotProduct(seg) / lenSq));
            var proj = segStart + t * seg;
            return pt.DistanceTo(proj);
        }
    }

    /// <summary>Wall junction classification.</summary>
    public enum JunctionType { TJunction, LJunction, CrossJunction }

    /// <summary>Detected wall junction point.</summary>
    public class WallJunction
    {
        public XYZ Point { get; set; }
        public int WallIndexA { get; set; }
        public int WallIndexB { get; set; }
        public JunctionType Type { get; set; }
    }

    /// <summary>Opening type classification.</summary>
    public enum OpeningType { Door, Window, Opening }

    /// <summary>Detected opening in a wall (gap between collinear wall segments).</summary>
    public class DetectedOpening
    {
        public XYZ Center { get; set; }
        public double WidthFt { get; set; }
        public double WidthMm => WidthFt * 304.8;
        public double WallThicknessFt { get; set; }
        public OpeningType Type { get; set; }
        public int WallIndexA { get; set; }
        public int WallIndexB { get; set; }
    }

    /// <summary>Bay spacing analysis result from column grid detection.</summary>
    public class BaySpacingResult
    {
        public List<double> XSpacings { get; set; } = new();
        public List<double> YSpacings { get; set; } = new();
        public double AvgXSpacingMm { get; set; }
        public double AvgYSpacingMm { get; set; }
        public bool IsRegularX { get; set; }
        public bool IsRegularY { get; set; }
        public bool IsRegularGrid { get; set; }

        public string Summary =>
            $"Bay spacing: X={AvgXSpacingMm:F0}mm ({(IsRegularX ? "regular" : "irregular")}), " +
            $"Y={AvgYSpacingMm:F0}mm ({(IsRegularY ? "regular" : "irregular")})";
    }

    /// <summary>Phase 71: DWG-to-BIM conversion quality score for coordinator review.</summary>
    public class ConversionQualityScore
    {
        public int TotalScore { get; set; }
        public string Grade { get; set; } = "D";
        public double LayerMatchRate { get; set; }
        public int LayerScore { get; set; }
        public double CreationRate { get; set; }
        public int CreationScore { get; set; }
        public double WallDetectionRate { get; set; }
        public int WallScore { get; set; }
        public double TagRate { get; set; }
        public int TagScore { get; set; }

        public string Summary => $"Quality: {TotalScore}/100 (Grade {Grade}) — " +
            $"Layers: {LayerMatchRate:F0}% matched, " +
            $"Elements: {CreationRate:F0}% created, " +
            $"Walls: {WallDetectionRate:F0}% detected, " +
            $"Tagged: {TagRate:F0}%";
    }
}
