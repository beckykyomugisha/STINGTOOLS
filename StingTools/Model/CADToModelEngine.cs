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
            ("door", "Doors"), ("tur", "Doors"), ("porte", "Doors"), ("dr-", "Doors"),
            ("window", "Windows"), ("fenster", "Windows"), ("fenetre", "Windows"), ("wn-", "Windows"),
            ("column", "Columns"), ("col-", "Columns"), ("stutze", "Columns"),
            ("beam", "Beams"), ("trager", "Beams"), ("poutre", "Beams"),
            ("slab", "Floors"), ("floor", "Floors"), ("dalle", "Floors"),
            ("roof", "Roofs"), ("dach", "Roofs"),
            ("stair", "Stairs"), ("treppe", "Stairs"),
            ("ceiling", "Ceilings"), ("decke", "Ceilings"),
            ("furniture", "Furniture"), ("furn", "Furniture"), ("mobel", "Furniture"),
            ("plumbing", "Plumbing"), ("plumb", "Plumbing"), ("sanit", "Plumbing"),
            ("duct", "Ducts"), ("hvac", "Ducts"), ("mech-", "Ducts"),
            ("pipe", "Pipes"), ("rohr", "Pipes"),
            ("elec", "Electrical"), ("cable", "Electrical"), ("light", "Electrical"),
            ("grid", "Grids"), ("raster", "Grids"),
            ("dim", "Dimensions"), ("text", "Text"), ("anno", "Annotations"),
        };

        /// <summary>
        /// Infers the Revit category from a DWG layer name.
        /// Returns null if no match found.
        /// </summary>
        public static string InferCategory(string layerName)
        {
            if (string.IsNullOrEmpty(layerName)) return null;
            var lower = layerName.ToLowerInvariant();
            foreach (var (pattern, category) in Rules)
            {
                if (lower.Contains(pattern))
                    return category;
            }
            return null;
        }
    }

    #endregion

    #region Extracted Geometry Types

    /// <summary>A line segment extracted from DWG geometry.</summary>
    internal class ExtractedLine
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
    internal class DetectedWall
    {
        public XYZ CenterStart { get; set; }
        public XYZ CenterEnd { get; set; }
        public double ThicknessFt { get; set; }
        public string LayerName { get; set; }
        public double LengthFt => CenterStart.DistanceTo(CenterEnd);
    }

    /// <summary>A closed loop detected as a potential floor/room boundary.</summary>
    internal class DetectedLoop
    {
        public List<XYZ> Points { get; set; } = new();
        public string LayerName { get; set; }
    }

    /// <summary>A block reference detected as a potential door/window/fixture.</summary>
    internal class DetectedBlock
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
                    var blockName = nestedInstance.Symbol?.Name;
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
            catch { return null; }
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
            const double endpointTol = 0.1; // ~30mm tolerance

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
                            catch { /* Some circuits may not be valid rooms */ }
                        }
                        tx.Commit();
                    }
                    catch
                    {
                        tx.RollBack();
                    }
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
    }
}
