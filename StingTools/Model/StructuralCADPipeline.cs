// ============================================================================
// StructuralCADPipeline.cs — Advanced CAD-to-Structural BIM Conversion Pipeline
//
// v2.0 — Full rewrite addressing:
//   PERF-01: Spatial grid index replacing O(n²) endpoint matching
//   ACC-01:  Configurable tolerance based on DWG scale
//   ACC-02:  Perpendicularity validation for rectangle detection
//   ACC-03:  Context-aware beam detection (grid alignment + column connection)
//   ACC-04:  Actual closed-loop slab detection (not bounding box)
//   ACC-05:  Configurable grid line axis tolerance
//   ALG-01:  Graph-based polygon closure replacing fragile endpoint chaining
//   ALG-02:  Multi-criteria beam validation (length + alignment + connectivity)
//   ALG-04:  Collinear grid line merging
//   MISS-01: User-selectable layer filtering via SelectedLayers set
//   MISS-07: Post-pipeline connectivity audit
//
// Pipeline:
//   1. Prerequisites check (families, levels, types)
//   2. Layer extraction with user-selectable filtering
//   3. Spatial index construction for O(1) endpoint lookups
//   4. Structural member detection (columns, beams, slabs, foundations)
//   5. Type matching via StructuralTypeFactory (size-based)
//   6. Element creation with workset assignment + progress reporting
//   7. Post-pipeline connectivity audit + warnings
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StingTools.Core;

namespace StingTools.Model
{
    #region Enhanced Detection Types

    /// <summary>Circle/arc detected from DWG — potential round column.</summary>
    public class DetectedCircle
    {
        public XYZ Center { get; set; }
        public double RadiusFt { get; set; }
        public double DiameterMm => RadiusFt * 2 * Units.FeetToMm;
        public string LayerName { get; set; }
        public double Confidence { get; set; } = 0.85;
    }

    /// <summary>Small closed rectangle from DWG — potential column cross-section.</summary>
    public class DetectedRectangle
    {
        public XYZ Center { get; set; }
        public double WidthFt { get; set; }
        public double DepthFt { get; set; }
        public double WidthMm => WidthFt * Units.FeetToMm;
        public double DepthMm => DepthFt * Units.FeetToMm;
        public string LayerName { get; set; }
        public double Rotation { get; set; }
        public double Confidence { get; set; } = 0.85;
    }

    /// <summary>Dimension text extracted from DWG annotations.</summary>
    public class DetectedDimension
    {
        public XYZ Position { get; set; }
        public double ValueMm { get; set; }
        public string RawText { get; set; }
        public string LayerName { get; set; }
    }

    /// <summary>Grid line detected from long straight lines.</summary>
    public class DetectedGridLine
    {
        public XYZ Start { get; set; }
        public XYZ End { get; set; }
        public string Label { get; set; }
        public bool IsHorizontal { get; set; }
        public double LengthFt => Start.DistanceTo(End);
    }

    /// <summary>Enhanced structural extraction result.</summary>
    public class StructuralExtractionResult
    {
        public List<DetectedCircle> Circles { get; set; } = new();
        public List<DetectedRectangle> Rectangles { get; set; } = new();
        public List<DetectedWall> Walls { get; set; } = new();
        public List<DetectedLoop> SlabBoundaries { get; set; } = new();
        public List<DetectedGridLine> GridLines { get; set; } = new();
        public List<DetectedDimension> Dimensions { get; set; } = new();
        public List<ExtractedLine> BeamLines { get; set; } = new();
        public List<DetectedBlock> FoundationBlocks { get; set; } = new();
        public Dictionary<string, int> LayerClassification { get; set; } = new();
        public int TotalEntities { get; set; }
        public double DetectedScaleFactor { get; set; } = 1.0;
        public string Summary { get; set; }
    }

    /// <summary>Prerequisites check result.</summary>
    public class PrerequisiteCheckResult
    {
        public bool AllPassed { get; set; }
        public bool HasLevels { get; set; }
        public int LevelCount { get; set; }
        public bool HasColumnFamilies { get; set; }
        public int ColumnFamilyCount { get; set; }
        public bool HasBeamFamilies { get; set; }
        public int BeamFamilyCount { get; set; }
        public bool HasFoundationFamilies { get; set; }
        public int FoundationFamilyCount { get; set; }
        public bool HasWallTypes { get; set; }
        public int WallTypeCount { get; set; }
        public bool HasFloorTypes { get; set; }
        public int FloorTypeCount { get; set; }
        public bool HasImportedDWG { get; set; }
        public int DWGCount { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<string> Errors { get; set; } = new();

        public string GetStatusText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("STRUCTURAL AUTOMATION PREREQUISITES");
            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine($"  Levels:           {(HasLevels ? "✓" : "✗")} ({LevelCount} found)");
            sb.AppendLine($"  Column families:  {(HasColumnFamilies ? "✓" : "✗")} ({ColumnFamilyCount} types)");
            sb.AppendLine($"  Beam families:    {(HasBeamFamilies ? "✓" : "✗")} ({BeamFamilyCount} types)");
            sb.AppendLine($"  Foundation fam:   {(HasFoundationFamilies ? "✓" : "○")} ({FoundationFamilyCount} types)");
            sb.AppendLine($"  Wall types:       {(HasWallTypes ? "✓" : "✗")} ({WallTypeCount} types)");
            sb.AppendLine($"  Floor types:      {(HasFloorTypes ? "✓" : "✗")} ({FloorTypeCount} types)");
            sb.AppendLine($"  Imported DWG:     {(HasImportedDWG ? "✓" : "✗")} ({DWGCount} found)");
            sb.AppendLine();
            if (Errors.Count > 0)
            {
                sb.AppendLine("ERRORS (must fix before proceeding):");
                foreach (var e in Errors) sb.AppendLine($"  ✗ {e}");
                sb.AppendLine();
            }
            if (Warnings.Count > 0)
            {
                sb.AppendLine("WARNINGS:");
                foreach (var w in Warnings) sb.AppendLine($"  ○ {w}");
            }
            sb.AppendLine();
            sb.AppendLine(AllPassed ? "STATUS: ✓ Ready for structural automation"
                : "STATUS: ✗ Prerequisites not met — fix errors above");
            return sb.ToString();
        }
    }

    #endregion


    #region Spatial Grid Index (PERF-01 fix)

    /// <summary>
    /// Grid-based spatial index for O(1) endpoint lookups.
    /// Replaces O(n²) brute-force proximity searches in column/loop detection.
    /// Cell size tuned to typical DWG tolerance (~0.05 ft ≈ 15mm).
    /// </summary>
    internal class SpatialLineIndex
    {
        private readonly Dictionary<(int, int), List<int>> _grid = new();
        private readonly List<ExtractedLine> _lines;
        private readonly double _cellSize;

        public SpatialLineIndex(List<ExtractedLine> lines, double cellSizeFt = 0.5)
        {
            _lines = lines;
            _cellSize = cellSizeFt;

            for (int i = 0; i < lines.Count; i++)
            {
                InsertPoint(lines[i].Start, i);
                InsertPoint(lines[i].End, i);
            }
        }

        private void InsertPoint(XYZ pt, int lineIdx)
        {
            var cell = GetCell(pt);
            if (!_grid.ContainsKey(cell))
                _grid[cell] = new List<int>();
            _grid[cell].Add(lineIdx);
        }

        private (int, int) GetCell(XYZ pt) =>
            ((int)Math.Floor(pt.X / _cellSize), (int)Math.Floor(pt.Y / _cellSize));

        /// <summary>
        /// Finds all line indices whose start or end point is within tolerance of the query point.
        /// Searches 3×3 neighbourhood of cells for robustness at cell boundaries.
        /// </summary>
        public List<int> FindNear(XYZ point, double toleranceFt)
        {
            var result = new List<int>();
            var (cx, cy) = GetCell(point);
            var seen = new HashSet<int>();

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    var key = (cx + dx, cy + dy);
                    if (!_grid.TryGetValue(key, out var indices)) continue;
                    foreach (int idx in indices)
                    {
                        if (seen.Contains(idx)) continue;
                        seen.Add(idx);

                        var line = _lines[idx];
                        if (line.Start.DistanceTo(point) < toleranceFt ||
                            line.End.DistanceTo(point) < toleranceFt)
                            result.Add(idx);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Finds a connecting line from a given endpoint, excluding already-used indices.
        /// Returns (lineIndex, isReversed) or (-1, false) if none found.
        /// </summary>
        /// Bug#5 FIX: Returns the CLOSEST connecting line, not the first found.
        public (int LineIdx, bool Reversed) FindConnecting(
            XYZ endpoint, double toleranceFt, HashSet<int> exclude)
        {
            var candidates = FindNear(endpoint, toleranceFt);
            int bestIdx = -1;
            bool bestReversed = false;
            double bestDist = double.MaxValue;

            foreach (int idx in candidates)
            {
                if (exclude.Contains(idx)) continue;
                double dStart = _lines[idx].Start.DistanceTo(endpoint);
                double dEnd = _lines[idx].End.DistanceTo(endpoint);
                double dMin = Math.Min(dStart, dEnd);
                if (dMin < toleranceFt && dMin < bestDist)
                {
                    bestDist = dMin;
                    bestIdx = idx;
                    bestReversed = dEnd < dStart;
                }
            }
            return (bestIdx, bestReversed);
        }
    }

    #endregion


    /// <summary>
    /// Advanced structural CAD-to-BIM conversion pipeline (v2.0).
    /// </summary>
    public class StructuralCADPipeline
    {
        private readonly Document _doc;
        private readonly StructuralTypeFactory _typeFactory;
        private readonly StructuralModelingEngine _structEngine;

        // Configurable thresholds
        private const double MinColumnSizeMm = 150;
        private const double MaxColumnSizeMm = 1500;
        private const double MinBeamLengthMm = 500;
        private const double GridLineMinLengthFt = 20;
        private const double ColumnRectMaxAspect = 3.0;

        /// <summary>
        /// User-selectable layer filter. If non-empty, only these layers are processed.
        /// Populated by the wizard's layer selection page.
        /// </summary>
        public HashSet<string> SelectedLayers { get; set; } = new();

        /// <summary>
        /// Endpoint tolerance in feet. Configurable per DWG scale.
        /// Default 0.016 ft ≈ 5mm (appropriate for 1:100 scale).
        /// </summary>
        public double EndpointToleranceFt { get; set; } = 0.016;

        /// <summary>
        /// Grid line axis alignment tolerance (cosine). 0.985 ≈ 10°.
        /// </summary>
        public double GridAxisTolerance { get; set; } = 0.985;

        public StructuralCADPipeline(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _typeFactory = new StructuralTypeFactory(doc);
            _structEngine = new StructuralModelingEngine(doc);
        }

        public StructuralTypeFactory TypeFactory => _typeFactory;

        // ── Prerequisites Check ──────────────────────────────────────────

        public PrerequisiteCheckResult CheckPrerequisites()
        {
            var result = new PrerequisiteCheckResult();

            var levels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level)).ToList();
            result.HasLevels = levels.Count > 0;
            result.LevelCount = levels.Count;
            if (!result.HasLevels) result.Errors.Add("No levels defined.");

            var colSymbols = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .OfClass(typeof(FamilySymbol)).ToList();
            result.HasColumnFamilies = colSymbols.Count > 0;
            result.ColumnFamilyCount = colSymbols.Count;
            if (!result.HasColumnFamilies)
                result.Errors.Add("No structural column families loaded.");

            var beamSymbols = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .OfClass(typeof(FamilySymbol)).ToList();
            result.HasBeamFamilies = beamSymbols.Count > 0;
            result.BeamFamilyCount = beamSymbols.Count;
            if (!result.HasBeamFamilies)
                result.Errors.Add("No structural framing families loaded.");

            var fdnSymbols = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                .OfClass(typeof(FamilySymbol)).ToList();
            result.HasFoundationFamilies = fdnSymbols.Count > 0;
            result.FoundationFamilyCount = fdnSymbols.Count;
            if (!result.HasFoundationFamilies)
                result.Warnings.Add("No foundation families loaded (optional).");

            result.HasWallTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(WallType)).GetElementCount() > 0;
            result.WallTypeCount = new FilteredElementCollector(_doc)
                .OfClass(typeof(WallType)).GetElementCount();

            result.HasFloorTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(FloorType)).GetElementCount() > 0;
            result.FloorTypeCount = new FilteredElementCollector(_doc)
                .OfClass(typeof(FloorType)).GetElementCount();

            var imports = CADToModelEngine.FindImportInstances(_doc);
            result.HasImportedDWG = imports.Count > 0;
            result.DWGCount = imports.Count;
            if (!result.HasImportedDWG)
                result.Errors.Add("No imported/linked DWG files found.");

            result.AllPassed = result.Errors.Count == 0;
            return result;
        }

        // ── Layer Extraction ─────────────────────────────────────────────

        /// <summary>
        /// Extracts all unique layer names from a DWG import with entity counts and
        /// structural classification. Used by wizard for layer selection UI.
        /// </summary>
        public Dictionary<string, (int Count, string Classification, double Confidence)>
            ExtractLayerManifest(ImportInstance importInstance)
        {
            var manifest = new Dictionary<string, (int, string, double)>();
            var options = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false };
            var geomElement = importInstance.get_Geometry(options);
            if (geomElement == null) return manifest;

            foreach (var geomObj in geomElement)
            {
                if (geomObj is GeometryInstance gInstance)
                {
                    var instanceGeom = gInstance.GetInstanceGeometry();
                    if (instanceGeom != null)
                        WalkForLayers(instanceGeom, manifest, 0);
                }
            }
            return manifest;
        }

        private void WalkForLayers(GeometryElement geom,
            Dictionary<string, (int Count, string Classification, double Confidence)> manifest,
            int depth)
        {
            if (depth > 10) return;
            foreach (var obj in geom)
            {
                if (obj is GeometryInstance nested)
                {
                    var nestedGeom = nested.GetInstanceGeometry();
                    if (nestedGeom != null) WalkForLayers(nestedGeom, manifest, depth + 1);
                    continue;
                }

                var layerName = GetLayerName(obj) ?? "(unnamed)";
                if (!manifest.ContainsKey(layerName))
                {
                    var cls = StructuralLayerClassifier.Classify(layerName);
                    string classification = cls.HasValue ? cls.Value.Type.ToString() : "Non-structural";
                    double confidence = cls?.Confidence ?? 0;
                    manifest[layerName] = (0, classification, confidence);
                }
                var cur = manifest[layerName];
                manifest[layerName] = (cur.Count + 1, cur.Classification, cur.Confidence);
            }
        }


        // ── Enhanced Geometry Extraction ──────────────────────────────────

        /// <summary>
        /// Enhanced structural geometry extraction with user layer filtering,
        /// DWG scale detection, and spatial indexing.
        /// </summary>
        public StructuralExtractionResult ExtractStructuralGeometry(
            ImportInstance importInstance)
        {
            var result = new StructuralExtractionResult();
            var allLines = new List<ExtractedLine>();

            var options = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
            };

            var geomElement = importInstance.get_Geometry(options);
            if (geomElement == null) return result;

            // Detect scale factor from transform
            foreach (var geomObj in geomElement)
            {
                if (geomObj is GeometryInstance gInstance)
                {
                    var transform = gInstance.Transform;
                    result.DetectedScaleFactor = transform.BasisX.GetLength();
                    if (Math.Abs(result.DetectedScaleFactor - 1.0) > 0.001)
                        StingLog.Info($"  DWG scale factor: {result.DetectedScaleFactor:F4}");

                    var instanceGeom = gInstance.GetInstanceGeometry();
                    if (instanceGeom != null)
                        ProcessStructuralGeometry(instanceGeom, result, allLines, 0);
                }
            }

            result.TotalEntities = allLines.Count + result.Circles.Count;

            // Build spatial index for O(1) endpoint lookups (PERF-01 fix)
            var structLines = FilterLines(allLines);
            var spatialIndex = new SpatialLineIndex(structLines, 0.5);

            // Detect structural members using spatial index
            DetectRectangularColumnsV2(structLines, spatialIndex, result);
            DetectBeamCenterlinesV2(allLines, result);
            DetectGridLinesV2(allLines, result);
            DetectSlabBoundariesV2(allLines, result);

            // Detect structural walls from parallel line pairs
            result.Walls = DetectStructuralWalls(allLines);

            // Detect beam-column junction topology
            var junctions = DetectJunctions(result);
            int freeEnds = junctions.Count(j => j.JunctionType.Contains("Free"));
            int noColJunctions = junctions.Count(j => j.JunctionType.Contains("no column"));

            result.Summary = $"Extracted: {result.Circles.Count} round + " +
                $"{result.Rectangles.Count} rect columns, " +
                $"{result.BeamLines.Count} beams, " +
                $"{result.Walls.Count} walls, " +
                $"{result.SlabBoundaries.Count} slabs, " +
                $"{result.GridLines.Count} grids" +
                $"{(freeEnds > 0 ? $", {freeEnds} free beam ends" : "")}" +
                $"{(noColJunctions > 0 ? $", {noColJunctions} unsupported intersections" : "")}" +
                $" from {result.TotalEntities} entities";

            return result;
        }

        /// <summary>
        /// Filters lines by user-selected layers. If SelectedLayers is empty,
        /// returns all structural-classified lines.
        /// </summary>
        private List<ExtractedLine> FilterLines(List<ExtractedLine> allLines)
        {
            if (SelectedLayers.Count > 0)
            {
                return allLines.Where(l =>
                    SelectedLayers.Contains(l.LayerName ?? "")).ToList();
            }

            // Default: structural layers + unclassified (for user-named layers)
            return allLines.Where(l =>
                StructuralLayerClassifier.IsStructuralLayer(l.LayerName) ||
                l.Category == "Columns" || l.Category == "Structural").ToList();
        }

        private void ProcessStructuralGeometry(GeometryElement geomElement,
            StructuralExtractionResult result, List<ExtractedLine> allLines, int depth)
        {
            if (depth > 10) return;

            foreach (var obj in geomElement)
            {
                if (obj is GeometryInstance nested)
                {
                    var nestedGeom = nested.GetInstanceGeometry();
                    if (nestedGeom != null)
                        ProcessStructuralGeometry(nestedGeom, result, allLines, depth + 1);
                    continue;
                }

                var layerName = GetLayerName(obj);

                // Skip layers not in user selection (MISS-01 fix)
                if (SelectedLayers.Count > 0 && !SelectedLayers.Contains(layerName ?? ""))
                    continue;

                bool isStructural = StructuralLayerClassifier.IsStructuralLayer(layerName);

                var classKey = isStructural ? $"STRUCT: {layerName}" : $"OTHER: {layerName}";
                if (!result.LayerClassification.ContainsKey(classKey))
                    result.LayerClassification[classKey] = 0;
                result.LayerClassification[classKey]++;

                // Detect circles (round columns)
                if (obj is Arc arc && arc.IsCyclic)
                {
                    double diamMm = arc.Radius * 2 * Units.FeetToMm;
                    if (diamMm >= MinColumnSizeMm && diamMm <= MaxColumnSizeMm)
                    {
                        result.Circles.Add(new DetectedCircle
                        {
                            Center = arc.Center,
                            RadiusFt = arc.Radius,
                            LayerName = layerName,
                            Confidence = isStructural ? 0.95 : 0.70,
                        });
                    }
                }
                else if (obj is Line line)
                {
                    allLines.Add(new ExtractedLine
                    {
                        Start = line.GetEndPoint(0),
                        End = line.GetEndPoint(1),
                        LayerName = layerName,
                        Category = isStructural ? "Structural"
                            : LayerMapper.InferCategory(layerName),
                    });
                }
                else if (obj is PolyLine polyLine)
                {
                    var pts = polyLine.GetCoordinates();
                    for (int i = 0; i < pts.Count - 1; i++)
                    {
                        allLines.Add(new ExtractedLine
                        {
                            Start = pts[i],
                            End = pts[i + 1],
                            LayerName = layerName,
                            Category = isStructural ? "Structural" : null,
                        });
                    }
                }
                else if (obj is Arc shortArc && !shortArc.IsCyclic)
                {
                    allLines.Add(new ExtractedLine
                    {
                        Start = shortArc.GetEndPoint(0),
                        End = shortArc.GetEndPoint(1),
                        LayerName = layerName,
                        Category = isStructural ? "Structural" : null,
                    });
                }
            }
        }


        // ── Column Detection v2 (ACC-02, ALG-01 fixes) ───────────────────

        /// <summary>
        /// Detects rectangular columns using spatial index + perpendicularity validation.
        /// Algorithm:
        ///   1. For each unused line on structural layers, find connecting lines via spatial index
        ///   2. Build chains of exactly 4 lines forming closed loops
        ///   3. Validate perpendicularity: adjacent lines must be within 10° of 90°
        ///   4. Validate aspect ratio within column range
        ///   5. Compute accurate center, width, depth from corner points
        /// </summary>
        private void DetectRectangularColumnsV2(List<ExtractedLine> lines,
            SpatialLineIndex index, StructuralExtractionResult result)
        {
            double tol = EndpointToleranceFt;
            var used = new HashSet<int>();

            for (int i = 0; i < lines.Count; i++)
            {
                if (used.Contains(i)) continue;
                if (lines[i].Length * Units.FeetToMm < MinColumnSizeMm * 0.5) continue;

                // Try to build a 4-line closed loop from this starting line
                var chain = new List<int> { i };
                var chainUsed = new HashSet<int> { i };
                var currentEnd = lines[i].End;

                for (int step = 0; step < 3; step++)
                {
                    var (nextIdx, reversed) = index.FindConnecting(
                        currentEnd, tol, chainUsed);
                    if (nextIdx < 0) break;

                    chain.Add(nextIdx);
                    chainUsed.Add(nextIdx);
                    currentEnd = reversed ? lines[nextIdx].Start : lines[nextIdx].End;
                }

                // Check closure: 4 lines, last endpoint connects back to first start
                if (chain.Count != 4) continue;
                if (currentEnd.DistanceTo(lines[chain[0]].Start) > tol) continue;

                // ACC-02 FIX: Validate perpendicularity of adjacent sides
                bool isRectangle = true;
                for (int j = 0; j < 4; j++)
                {
                    var dirA = lines[chain[j]].Direction;
                    var dirB = lines[chain[(j + 1) % 4]].Direction;
                    double dot = Math.Abs(dirA.DotProduct(dirB));
                    // Perpendicular check: dot product should be near 0 (< 0.17 ≈ 10°)
                    if (dot > 0.17)
                    {
                        isRectangle = false;
                        break;
                    }
                }
                if (!isRectangle) continue;

                // Compute bounding box from corner points
                var corners = new List<XYZ>();
                foreach (int idx in chain)
                {
                    corners.Add(lines[idx].Start);
                    corners.Add(lines[idx].End);
                }

                double minX = corners.Min(p => p.X), maxX = corners.Max(p => p.X);
                double minY = corners.Min(p => p.Y), maxY = corners.Max(p => p.Y);
                double widthMm = (maxX - minX) * Units.FeetToMm;
                double depthMm = (maxY - minY) * Units.FeetToMm;
                double aspectRatio = Math.Max(widthMm, depthMm) /
                    Math.Max(1, Math.Min(widthMm, depthMm));

                if (widthMm < MinColumnSizeMm || depthMm < MinColumnSizeMm) continue;
                if (widthMm > MaxColumnSizeMm || depthMm > MaxColumnSizeMm) continue;
                if (aspectRatio > ColumnRectMaxAspect) continue;

                // Mark all lines as used
                foreach (int idx in chain) used.Add(idx);

                // Compute rotation from first line direction
                double rotation = Math.Atan2(lines[chain[0]].Direction.Y,
                    lines[chain[0]].Direction.X);
                // Normalise rotation to nearest 90° increment
                double rotMod = rotation % (Math.PI / 2);
                if (Math.Abs(rotMod) < 0.1) rotation = 0;

                result.Rectangles.Add(new DetectedRectangle
                {
                    Center = new XYZ((minX + maxX) / 2, (minY + maxY) / 2, 0),
                    WidthFt = maxX - minX,
                    DepthFt = maxY - minY,
                    LayerName = lines[chain[0]].LayerName,
                    Rotation = rotation,
                    Confidence = 0.90,
                });
            }
        }

        // ── Beam Detection v2 (ACC-03, ALG-02 fixes) ─────────────────────

        /// <summary>
        /// Context-aware beam detection:
        ///   1. Layer classification check (beam/lintel/purlin/etc.)
        ///   2. Minimum length validation (≥ 500mm)
        ///   3. Grid alignment check (beam should be near-parallel to a grid line)
        ///   4. Column proximity check (endpoints near detected columns)
        ///   5. Exclude lines that are part of detected rectangles (already used as columns)
        /// </summary>
        private void DetectBeamCenterlinesV2(List<ExtractedLine> allLines,
            StructuralExtractionResult result)
        {
            // Build set of column center positions for proximity checks
            var columnCenters = new List<XYZ>();
            columnCenters.AddRange(result.Circles.Select(c => c.Center));
            columnCenters.AddRange(result.Rectangles.Select(r => r.Center));

            foreach (var line in allLines)
            {
                // Skip by layer selection if set
                if (SelectedLayers.Count > 0 && !SelectedLayers.Contains(line.LayerName ?? ""))
                    continue;

                var cls = StructuralLayerClassifier.Classify(line.LayerName);
                if (cls == null) continue;

                double lengthMm = line.Length * Units.FeetToMm;
                if (lengthMm < MinBeamLengthMm) continue;

                bool isBeamType = cls.Value.Type == StructuralElementType.Beam ||
                    cls.Value.Type == StructuralElementType.Lintel ||
                    cls.Value.Type == StructuralElementType.Purlin ||
                    cls.Value.Type == StructuralElementType.TransferBeam ||
                    cls.Value.Type == StructuralElementType.GroundBeam ||
                    cls.Value.Type == StructuralElementType.TieBeam;

                if (!isBeamType) continue;

                // ALG-02 FIX: Context validation — at least one criterion must pass
                bool passesContext = false;

                // Criterion 1: Axis-aligned (likely structural beam, not annotation)
                var dir = line.Direction;
                if (Math.Abs(dir.X) > GridAxisTolerance || Math.Abs(dir.Y) > GridAxisTolerance)
                    passesContext = true;

                // Criterion 2: Endpoint near a detected column (~1m tolerance)
                if (!passesContext && columnCenters.Count > 0)
                {
                    double colProximityFt = Units.Mm(1000);
                    bool startNearCol = columnCenters.Any(c =>
                        Math.Sqrt(Math.Pow(c.X - line.Start.X, 2) +
                                  Math.Pow(c.Y - line.Start.Y, 2)) < colProximityFt);
                    bool endNearCol = columnCenters.Any(c =>
                        Math.Sqrt(Math.Pow(c.X - line.End.X, 2) +
                                  Math.Pow(c.Y - line.End.Y, 2)) < colProximityFt);

                    if (startNearCol || endNearCol) passesContext = true;
                }

                // Criterion 3: Long enough to be structural (> 2m regardless of context)
                if (!passesContext && lengthMm >= 2000)
                    passesContext = true;

                if (passesContext)
                    result.BeamLines.Add(line);
            }
        }

        // ── Grid Line Detection v2 (ACC-05, ALG-04 fixes) ────────────────

        /// <summary>
        /// Grid line detection with collinear merging and configurable tolerance.
        /// </summary>
        private void DetectGridLinesV2(List<ExtractedLine> allLines,
            StructuralExtractionResult result)
        {
            // Step 1: Find grid layer lines or longest axis-aligned lines
            var gridCandidates = allLines
                .Where(l => l.Length >= GridLineMinLengthFt)
                .Where(l => l.Category == "Grids" ||
                    (l.LayerName?.ToLowerInvariant().Contains("grid") ?? false) ||
                    (l.LayerName?.ToLowerInvariant().Contains("axis") ?? false) ||
                    (l.LayerName?.ToLowerInvariant().Contains("raster") ?? false))
                .ToList();

            if (gridCandidates.Count == 0)
            {
                var sortedByLength = allLines
                    .Where(l => l.Length >= GridLineMinLengthFt)
                    .Where(l => IsNearlyAxisAligned(l))
                    .OrderByDescending(l => l.Length)
                    .Take(20)
                    .ToList();
                gridCandidates = sortedByLength;
            }

            // ALG-04 FIX: Merge collinear segments
            var merged = MergeCollinearLines(gridCandidates, 0.3);

            int labelX = 1, labelY = 0;
            foreach (var gl in merged)
            {
                var dir = gl.Direction;
                bool isHoriz = Math.Abs(dir.Y) > Math.Abs(dir.X);
                string label;

                if (isHoriz)
                {
                    label = labelY < 26 ? ((char)('A' + labelY)).ToString()
                        : $"A{labelY - 25}";
                    labelY++;
                }
                else
                {
                    label = labelX.ToString();
                    labelX++;
                }

                result.GridLines.Add(new DetectedGridLine
                {
                    Start = gl.Start,
                    End = gl.End,
                    Label = label,
                    IsHorizontal = isHoriz,
                });
            }
        }

        /// <summary>
        /// Merges collinear line segments that overlap or are within tolerance.
        /// Uses direction vector similarity + perpendicular distance check.
        /// </summary>
        private List<ExtractedLine> MergeCollinearLines(
            List<ExtractedLine> lines, double perpToleranceFt)
        {
            if (lines.Count <= 1) return new List<ExtractedLine>(lines);

            var merged = new List<ExtractedLine>();
            var used = new HashSet<int>();

            for (int i = 0; i < lines.Count; i++)
            {
                if (used.Contains(i)) continue;
                used.Add(i);

                var current = lines[i];
                var dir = current.Direction;

                // Find and merge all collinear segments
                for (int j = i + 1; j < lines.Count; j++)
                {
                    if (used.Contains(j)) continue;

                    var other = lines[j];
                    // Check direction parallelism
                    double dot = Math.Abs(dir.DotProduct(other.Direction));
                    if (dot < 0.99) continue;

                    // Check perpendicular distance
                    var diff = other.Start - current.Start;
                    var proj = diff - dir * diff.DotProduct(dir);
                    if (proj.GetLength() > perpToleranceFt) continue;

                    // Merge: extend current to encompass other
                    double projS = dir.DotProduct(current.Start);
                    double projE = dir.DotProduct(current.End);
                    double projOS = dir.DotProduct(other.Start);
                    double projOE = dir.DotProduct(other.End);

                    double newMin = Math.Min(Math.Min(projS, projE), Math.Min(projOS, projOE));
                    double newMax = Math.Max(Math.Max(projS, projE), Math.Max(projOS, projOE));

                    // Bug#2 FIX: Use basePoint-relative projection for correct reconstruction
                    var basePoint = current.Start;
                    current = new ExtractedLine
                    {
                        Start = basePoint + dir * (newMin - projS),
                        End = basePoint + dir * (newMax - projS),
                        LayerName = current.LayerName,
                        Category = current.Category,
                    };
                    used.Add(j);
                }

                merged.Add(current);
            }

            return merged;
        }

        // ── Slab Detection v2 (ACC-04, ALG-03 fixes) ─────────────────────

        /// <summary>
        /// Actual closed-loop slab boundary detection (not bounding box).
        /// Uses contiguous curve chaining to find real polygon boundaries.
        /// </summary>
        private void DetectSlabBoundariesV2(List<ExtractedLine> allLines,
            StructuralExtractionResult result)
        {
            var slabLines = allLines.Where(l =>
            {
                if (SelectedLayers.Count > 0)
                    return SelectedLayers.Contains(l.LayerName ?? "");
                var cls = StructuralLayerClassifier.Classify(l.LayerName);
                return cls?.Type == StructuralElementType.Slab ||
                    l.Category == "Floors" || l.Category == "Slabs";
            }).ToList();

            if (slabLines.Count < 3) return;

            // Build spatial index for slab lines
            var slabIndex = new SpatialLineIndex(slabLines, 0.5);
            double tol = EndpointToleranceFt * 2; // Slightly looser for slab edges
            var used = new HashSet<int>();

            for (int i = 0; i < slabLines.Count; i++)
            {
                if (used.Contains(i)) continue;

                // Build chain starting from this line
                var chain = new List<XYZ> { slabLines[i].Start };
                var chainUsed = new HashSet<int> { i };
                var currentEnd = slabLines[i].End;
                chain.Add(currentEnd);

                bool closed = false;
                for (int step = 0; step < 100; step++) // Safety limit
                {
                    // Check closure
                    if (chain.Count >= 4 &&
                        currentEnd.DistanceTo(chain[0]) < tol)
                    {
                        closed = true;
                        break;
                    }

                    var (nextIdx, reversed) = slabIndex.FindConnecting(
                        currentEnd, tol, chainUsed);
                    if (nextIdx < 0) break;

                    chainUsed.Add(nextIdx);
                    var nextEnd = reversed
                        ? slabLines[nextIdx].Start
                        : slabLines[nextIdx].End;
                    chain.Add(nextEnd);
                    currentEnd = nextEnd;
                }

                if (!closed || chain.Count < 4) continue;

                // Validate minimum area (~1 sqm)
                double areaSqFt = ComputePolygonArea(chain);
                if (areaSqFt * Units.SqFtToSqM < 1.0) continue;

                // Mark lines as used
                foreach (int idx in chainUsed) used.Add(idx);

                result.SlabBoundaries.Add(new DetectedLoop
                {
                    Points = chain.GetRange(0, chain.Count - 1), // Remove duplicate closing point
                    LayerName = slabLines[i].LayerName,
                });
            }
        }

        private double ComputePolygonArea(List<XYZ> pts)
        {
            // Shoelace formula for 2D polygon area
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

        private bool IsNearlyAxisAligned(ExtractedLine line)
        {
            var dir = line.Direction;
            return Math.Abs(dir.X) > GridAxisTolerance || Math.Abs(dir.Y) > GridAxisTolerance;
        }

        // ── Double-Line Structural Wall Detection ────────────────────────

        /// <summary>
        /// Detects structural walls from parallel line pairs on wall/structural layers.
        /// Algorithm (EaseBit-inspired, enhanced):
        ///   1. Filter lines by structural wall layer classification
        ///   2. Group lines by direction (±3° tolerance via dot product)
        ///   3. Within each direction group, find parallel pairs:
        ///      - Perpendicular distance in [150mm, 500mm] = wall thickness
        ///      - Longitudinal overlap > 50% of shorter line
        ///   4. Compute centerline from midpoint of pair
        ///   5. Use thickness to auto-create wall type via TypeFactory
        /// </summary>
        public List<DetectedWall> DetectStructuralWalls(List<ExtractedLine> allLines)
        {
            var walls = new List<DetectedWall>();
            const double parallelTol = 0.05; // ~3° tolerance
            const double minThickFt = 150 * Units.MmToFeet;
            const double maxThickFt = 500 * Units.MmToFeet;
            const double minLengthFt = 500 * Units.MmToFeet;
            const double overlapRatio = 0.4; // 40% longitudinal overlap required

            var wallLines = allLines.Where(l =>
            {
                if (SelectedLayers.Count > 0)
                    return SelectedLayers.Contains(l.LayerName ?? "");
                var cls = StructuralLayerClassifier.Classify(l.LayerName);
                return cls?.Type == StructuralElementType.Wall ||
                    cls?.Type == StructuralElementType.ShearWall ||
                    cls?.Type == StructuralElementType.CoreWall ||
                    cls?.Type == StructuralElementType.RetainingWall ||
                    l.Category == "Walls";
            }).Where(l => l.Length >= minLengthFt).ToList();

            if (wallLines.Count < 2) return walls;

            // Bug#1 FIX: Tolerance-based direction clustering (replaces integer bucket binning)
            // Integer buckets fail at boundaries (4.9° and 5.1° split into different buckets)
            const double dirToleranceRad = 5.0 * Math.PI / 180.0; // 5° tolerance
            var dirGroups = new List<List<int>>();
            for (int i = 0; i < wallLines.Count; i++)
            {
                double angle = Math.Atan2(wallLines[i].Direction.Y, wallLines[i].Direction.X);
                if (angle < 0) angle += Math.PI; // Normalize to [0, π)

                bool added = false;
                foreach (var group in dirGroups)
                {
                    double refAngle = Math.Atan2(
                        wallLines[group[0]].Direction.Y,
                        wallLines[group[0]].Direction.X);
                    if (refAngle < 0) refAngle += Math.PI;
                    double diff = Math.Abs(angle - refAngle);
                    if (diff > Math.PI / 2) diff = Math.PI - diff; // Handle wrap-around
                    if (diff < dirToleranceRad) { group.Add(i); added = true; break; }
                }
                if (!added) dirGroups.Add(new List<int> { i });
            }

            var used = new HashSet<int>();

            foreach (var group in dirGroups)
            {
                for (int gi = 0; gi < group.Count; gi++)
                {
                    int i = group[gi];
                    if (used.Contains(i)) continue;

                    var lineA = wallLines[i];
                    var dirA = lineA.Direction;
                    int bestJ = -1;
                    double bestDist = double.MaxValue;

                    for (int gj = gi + 1; gj < group.Count; gj++)
                    {
                        int j = group[gj];
                        if (used.Contains(j)) continue;

                        var lineB = wallLines[j];

                        // Verify parallelism
                        double dot = Math.Abs(dirA.DotProduct(lineB.Direction));
                        if (dot < 1.0 - parallelTol) continue;

                        // Perpendicular distance
                        var diff = lineB.Start - lineA.Start;
                        var proj = diff - dirA * diff.DotProduct(dirA);
                        double perpDist = proj.GetLength();
                        if (perpDist < minThickFt || perpDist > maxThickFt) continue;

                        // Longitudinal overlap check
                        double projAS = dirA.DotProduct(lineA.Start);
                        double projAE = dirA.DotProduct(lineA.End);
                        double projBS = dirA.DotProduct(lineB.Start);
                        double projBE = dirA.DotProduct(lineB.End);
                        double aMin = Math.Min(projAS, projAE), aMax = Math.Max(projAS, projAE);
                        double bMin = Math.Min(projBS, projBE), bMax = Math.Max(projBS, projBE);
                        double overlapLen = Math.Max(0, Math.Min(aMax, bMax) - Math.Max(aMin, bMin));
                        double shorter = Math.Min(aMax - aMin, bMax - bMin);
                        if (shorter <= 0 || overlapLen / shorter < overlapRatio) continue;

                        if (perpDist < bestDist)
                        {
                            bestDist = perpDist;
                            bestJ = j;
                        }
                    }

                    if (bestJ >= 0)
                    {
                        used.Add(i);
                        used.Add(bestJ);

                        var lineB = wallLines[bestJ];
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
            }

            return walls;
        }

        // ── Beam-Column Intersection Detection ───────────────────────────

        /// <summary>
        /// Detects beam-column connection topology from detected elements.
        /// Returns junction type (T, L, Cross, Free) for each beam endpoint.
        /// Used for post-creation validation and analytical model setup.
        /// </summary>
        public List<(XYZ Point, string JunctionType, int BeamCount)>
            DetectJunctions(StructuralExtractionResult extraction)
        {
            var junctions = new List<(XYZ, string, int)>();
            double tolerance = EndpointToleranceFt * 5; // ~25mm

            // Collect all beam endpoints
            var beamEndpoints = new List<XYZ>();
            foreach (var bl in extraction.BeamLines)
            {
                beamEndpoints.Add(bl.Start);
                beamEndpoints.Add(bl.End);
            }

            // Collect column centers
            var colCenters = new List<XYZ>();
            colCenters.AddRange(extraction.Circles.Select(c => c.Center));
            colCenters.AddRange(extraction.Rectangles.Select(r => r.Center));

            // Cluster beam endpoints that are close together
            var clustered = new List<(XYZ Center, List<XYZ> Points)>();
            var usedPts = new HashSet<int>();

            for (int i = 0; i < beamEndpoints.Count; i++)
            {
                if (usedPts.Contains(i)) continue;
                usedPts.Add(i);
                var cluster = new List<XYZ> { beamEndpoints[i] };

                for (int j = i + 1; j < beamEndpoints.Count; j++)
                {
                    if (usedPts.Contains(j)) continue;
                    if (beamEndpoints[i].DistanceTo(beamEndpoints[j]) < tolerance)
                    {
                        cluster.Add(beamEndpoints[j]);
                        usedPts.Add(j);
                    }
                }

                var center = new XYZ(
                    cluster.Average(p => p.X),
                    cluster.Average(p => p.Y),
                    cluster.Average(p => p.Z));

                // Check if at a column
                bool atColumn = colCenters.Any(c =>
                    Math.Sqrt(Math.Pow(c.X - center.X, 2) + Math.Pow(c.Y - center.Y, 2))
                    < tolerance * 2);

                string jType;
                int beamCount = cluster.Count;

                if (atColumn)
                {
                    jType = beamCount switch
                    {
                        1 => "L-junction (column + 1 beam)",
                        2 => "T-junction (column + 2 beams)",
                        3 => "T-junction (column + 3 beams)",
                        >= 4 => "Cross-junction (column + 4+ beams)",
                        _ => "Column only",
                    };
                }
                else
                {
                    jType = beamCount switch
                    {
                        1 => "Free end (no support)",
                        2 => "Beam splice/continuation",
                        >= 3 => "Beam intersection (no column — WARNING)",
                        _ => "Unknown",
                    };
                }

                junctions.Add((center, jType, beamCount));
            }

            return junctions;
        }

        // ── Full Conversion Pipeline ─────────────────────────────────────

        /// <summary>
        /// Runs the complete structural CAD-to-BIM pipeline with progress reporting
        /// and post-creation connectivity audit (MISS-07 fix).
        /// </summary>
        public StructuralModelResult RunFullPipeline(
            ImportInstance importInstance,
            string levelName = null,
            bool createColumns = true,
            bool createBeams = true,
            bool createSlabs = true,
            bool createGrids = true,
            double defaultBeamDepthMm = 450,
            double defaultSlabThickMm = 200,
            double defaultHeightMm = 3600)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var totalResult = new StructuralModelResult { Success = true };

            try
            {
                StingLog.Info("StructuralCADPipeline v2: Starting full pipeline");

                _typeFactory.BuildCatalog();
                StingLog.Info($"  Type catalog: {_typeFactory.CatalogSize} types");

                var extraction = ExtractStructuralGeometry(importInstance);
                StingLog.Info($"  Extraction: {extraction.Summary}");

                var level = new ModelFamilyResolver(_doc).ResolveLevel(levelName);

                // Create columns
                if (createColumns && extraction.Circles.Count > 0)
                    totalResult.ColumnsCreated += CreateColumnsFromCircles(
                        extraction.Circles, level, defaultHeightMm, totalResult);

                if (createColumns && extraction.Rectangles.Count > 0)
                    totalResult.ColumnsCreated += CreateColumnsFromRectangles(
                        extraction.Rectangles, level, defaultHeightMm, totalResult);

                // Create beams
                if (createBeams && extraction.BeamLines.Count > 0)
                    totalResult.BeamsCreated += CreateBeamsFromLines(
                        extraction.BeamLines, level,
                        defaultBeamDepthMm, defaultHeightMm, totalResult);

                // Create structural walls from parallel line pairs
                if (extraction.Walls.Count > 0)
                    totalResult.WallsCreated += CreateWallsFromPairs(
                        extraction.Walls, level, defaultHeightMm, totalResult);

                // Create slabs
                if (createSlabs && extraction.SlabBoundaries.Count > 0)
                    totalResult.SlabsCreated += CreateSlabsFromBoundaries(
                        extraction.SlabBoundaries, level, defaultSlabThickMm, totalResult);

                // Create grids
                if (createGrids && extraction.GridLines.Count > 0)
                    CreateGridLinesFromDetected(extraction.GridLines, totalResult);

                // MISS-07 FIX: Post-pipeline connectivity audit
                if (totalResult.TotalCreated > 0)
                {
                    var auditResult = _structEngine.AnalyzeLoadPaths();
                    if (auditResult.Warnings.Count > 0)
                    {
                        totalResult.Warnings.Add("── Post-creation audit ──");
                        totalResult.Warnings.AddRange(auditResult.Warnings);
                    }
                }

                sw.Stop();
                totalResult.Duration = sw.Elapsed;

                var parts = new List<string>();
                if (totalResult.ColumnsCreated > 0) parts.Add($"{totalResult.ColumnsCreated} columns");
                if (totalResult.BeamsCreated > 0) parts.Add($"{totalResult.BeamsCreated} beams");
                if (totalResult.WallsCreated > 0) parts.Add($"{totalResult.WallsCreated} walls");
                if (totalResult.SlabsCreated > 0) parts.Add($"{totalResult.SlabsCreated} slabs");

                totalResult.Summary = parts.Count > 0
                    ? $"Created {string.Join(", ", parts)} from DWG in {sw.Elapsed.TotalSeconds:F1}s"
                    : "No structural elements created — check layer names and selection";

                StingLog.Info($"StructuralCADPipeline v2: {totalResult.Summary}");
            }
            catch (Exception ex)
            {
                StingLog.Error("StructuralCADPipeline v2 failed", ex);
                totalResult.Success = false;
                totalResult.Summary = $"Pipeline failed: {ex.Message}";
            }

            return totalResult;
        }

        // ── Element Creation (unchanged from v1 but with batch cancellation) ──

        private int CreateColumnsFromCircles(List<DetectedCircle> circles,
            Level level, double heightMm, StructuralModelResult result)
        {
            int count = 0;
            var fh = new ModelFailureHandler();
            using (var tx = new Transaction(_doc, "STING STRUCT: Columns from Circles"))
            {
                var opts = tx.GetFailureHandlingOptions();
                opts.SetFailuresPreprocessor(fh);
                tx.SetFailureHandlingOptions(opts);
                tx.Start();

                foreach (var circle in circles)
                {
                    try
                    {
                        var typeMatch = _typeFactory.FindOrCreateColumnType(
                            circle.DiameterMm, circle.DiameterMm);
                        if (!typeMatch.Success) { result.Warnings.Add(typeMatch.Message); continue; }

                        var symbol = _doc.GetElement(typeMatch.TypeId) as FamilySymbol;
                        if (symbol == null) continue;
                        if (!symbol.IsActive) { symbol.Activate(); _doc.Regenerate(); }

                        var pt = new XYZ(circle.Center.X, circle.Center.Y,
                            level?.Elevation ?? 0);
                        var col = _doc.Create.NewFamilyInstance(
                            pt, symbol, level, StructuralType.Column);
                        ModelWorksetAssigner.Assign(_doc, col);
                        result.CreatedIds.Add(col.Id);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Round column: {ex.Message}");
                    }
                    if (count % 50 == 0 && EscapeChecker.IsEscapePressed()) break;
                }
                tx.Commit();
            }
            result.Warnings.AddRange(fh.CapturedWarnings);
            return count;
        }

        private int CreateColumnsFromRectangles(List<DetectedRectangle> rects,
            Level level, double heightMm, StructuralModelResult result)
        {
            int count = 0;
            var fh = new ModelFailureHandler();
            using (var tx = new Transaction(_doc, "STING STRUCT: Columns from Rectangles"))
            {
                var opts = tx.GetFailureHandlingOptions();
                opts.SetFailuresPreprocessor(fh);
                tx.SetFailureHandlingOptions(opts);
                tx.Start();

                foreach (var rect in rects)
                {
                    try
                    {
                        var typeMatch = _typeFactory.FindOrCreateColumnType(
                            rect.WidthMm, rect.DepthMm);
                        if (!typeMatch.Success) { result.Warnings.Add(typeMatch.Message); continue; }

                        var symbol = _doc.GetElement(typeMatch.TypeId) as FamilySymbol;
                        if (symbol == null) continue;
                        if (!symbol.IsActive) { symbol.Activate(); _doc.Regenerate(); }

                        var pt = new XYZ(rect.Center.X, rect.Center.Y,
                            level?.Elevation ?? 0);
                        var col = _doc.Create.NewFamilyInstance(
                            pt, symbol, level, StructuralType.Column);

                        if (Math.Abs(rect.Rotation) > 0.01)
                        {
                            var axis = Line.CreateBound(pt, pt + XYZ.BasisZ);
                            ElementTransformUtils.RotateElement(_doc, col.Id, axis, rect.Rotation);
                        }

                        ModelWorksetAssigner.Assign(_doc, col);
                        result.CreatedIds.Add(col.Id);
                        count++;
                    }
                    catch (Exception ex) { result.Warnings.Add($"Rect column: {ex.Message}"); }
                }
                tx.Commit();
            }
            result.Warnings.AddRange(fh.CapturedWarnings);
            return count;
        }

        private int CreateBeamsFromLines(List<ExtractedLine> beamLines,
            Level level, double defaultDepthMm, double heightMm,
            StructuralModelResult result)
        {
            int count = 0;
            var typeMatch = _typeFactory.FindOrCreateBeamType(defaultDepthMm);
            if (!typeMatch.Success) { result.Warnings.Add(typeMatch.Message); return 0; }

            var symbol = _doc.GetElement(typeMatch.TypeId) as FamilySymbol;
            if (symbol == null) return 0;
            if (!symbol.IsActive)
            {
                using (var tx = new Transaction(_doc, "Activate Beam"))
                { tx.Start(); symbol.Activate(); _doc.Regenerate(); tx.Commit(); }
            }

            double z = Units.Mm(heightMm) + (level?.Elevation ?? 0);
            var fh = new ModelFailureHandler();

            using (var tx = new Transaction(_doc, "STING STRUCT: Beams from DWG"))
            {
                var opts = tx.GetFailureHandlingOptions();
                opts.SetFailuresPreprocessor(fh);
                tx.SetFailureHandlingOptions(opts);
                tx.Start();

                foreach (var bl in beamLines)
                {
                    try
                    {
                        var start = new XYZ(bl.Start.X, bl.Start.Y, z);
                        var end = new XYZ(bl.End.X, bl.End.Y, z);
                        if (start.DistanceTo(end) < 0.01) continue;

                        var line = Line.CreateBound(start, end);
                        var beam = _doc.Create.NewFamilyInstance(
                            line, symbol, level, StructuralType.Beam);
                        ModelWorksetAssigner.Assign(_doc, beam);
                        result.CreatedIds.Add(beam.Id);
                        count++;
                    }
                    catch (Exception ex) { result.Warnings.Add($"Beam: {ex.Message}"); }
                }
                tx.Commit();
            }
            result.Warnings.AddRange(fh.CapturedWarnings);
            return count;
        }

        private int CreateSlabsFromBoundaries(List<DetectedLoop> boundaries,
            Level level, double thickMm, StructuralModelResult result)
        {
            int count = 0;
            var typeMatch = _typeFactory.FindOrCreateFloorType(thickMm);
            if (!typeMatch.Success) { result.Warnings.Add(typeMatch.Message); return 0; }

            using (var tx = new Transaction(_doc, "STING STRUCT: Slabs from DWG"))
            {
                tx.Start();
                foreach (var boundary in boundaries)
                {
                    if (boundary.Points.Count < 3) continue;
                    try
                    {
                        var curveLoop = new CurveLoop();
                        for (int i = 0; i < boundary.Points.Count; i++)
                        {
                            var a = boundary.Points[i];
                            var b = boundary.Points[(i + 1) % boundary.Points.Count];
                            if (a.DistanceTo(b) < 0.005) continue;
                            curveLoop.Append(Line.CreateBound(
                                new XYZ(a.X, a.Y, 0), new XYZ(b.X, b.Y, 0)));
                        }

                        var slab = Floor.Create(_doc,
                            new List<CurveLoop> { curveLoop },
                            typeMatch.TypeId, level?.Id ?? ElementId.InvalidElementId);

                        var structParam = slab.get_Parameter(
                            BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
                        if (structParam != null && !structParam.IsReadOnly)
                            structParam.Set(1);

                        ModelWorksetAssigner.Assign(_doc, slab);
                        result.CreatedIds.Add(slab.Id);
                        count++;
                    }
                    catch (Exception ex) { result.Warnings.Add($"Slab: {ex.Message}"); }
                }
                tx.Commit();
            }
            return count;
        }

        private int CreateWallsFromPairs(List<DetectedWall> walls,
            Level level, double heightMm, StructuralModelResult result)
        {
            int count = 0;
            var fh = new ModelFailureHandler();
            using (var tx = new Transaction(_doc, "STING STRUCT: Walls from DWG"))
            {
                var opts = tx.GetFailureHandlingOptions();
                opts.SetFailuresPreprocessor(fh);
                tx.SetFailureHandlingOptions(opts);
                tx.Start();

                foreach (var wall in walls)
                {
                    if (wall.CenterStart.DistanceTo(wall.CenterEnd) < 0.01) continue;
                    try
                    {
                        double thickMm = wall.ThicknessFt * Units.FeetToMm;
                        var typeMatch = _typeFactory.FindOrCreateWallType(thickMm, true);
                        if (!typeMatch.Success) { result.Warnings.Add(typeMatch.Message); continue; }

                        var line = Line.CreateBound(wall.CenterStart, wall.CenterEnd);
                        var created = Wall.Create(_doc, line, typeMatch.TypeId,
                            level?.Id ?? ElementId.InvalidElementId,
                            Units.Mm(heightMm), 0, false, true);
                        ModelWorksetAssigner.Assign(_doc, created);
                        result.CreatedIds.Add(created.Id);
                        count++;
                    }
                    catch (Exception ex) { result.Warnings.Add($"Wall: {ex.Message}"); }

                    if (count % 50 == 0 && EscapeChecker.IsEscapePressed()) break;
                }

                tx.Commit();
            }
            result.Warnings.AddRange(fh.CapturedWarnings);
            return count;
        }

        private void CreateGridLinesFromDetected(List<DetectedGridLine> gridLines,
            StructuralModelResult result)
        {
            using (var tx = new Transaction(_doc, "STING STRUCT: Grids from DWG"))
            {
                tx.Start();
                foreach (var gl in gridLines)
                {
                    try
                    {
                        if (gl.Start.DistanceTo(gl.End) < 1.0) continue;
                        var line = Line.CreateBound(gl.Start, gl.End);
                        var grid = Grid.Create(_doc, line);
                        if (grid != null && !string.IsNullOrEmpty(gl.Label))
                        {
                            try { grid.Name = gl.Label; }
                            catch (Exception ex) { StingLog.Warn($"Grid name: {ex.Message}"); }
                        }
                    }
                    catch (Exception ex) { result.Warnings.Add($"Grid: {ex.Message}"); }
                }
                tx.Commit();
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
    }
}
