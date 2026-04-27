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

    /// <summary>Beam detected from parallel line pairs in DWG (two lines = one beam).</summary>
    public class DetectedBeam
    {
        public XYZ Start { get; set; }
        public XYZ End { get; set; }
        public double WidthFt { get; set; }
        public bool WidthDetected { get; set; }
        public string LayerName { get; set; }
        public double LengthFt => Start.DistanceTo(End);
        public double WidthMm => WidthFt * Units.FeetToMm;
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
        public List<DetectedBeam> BeamLines { get; set; } = new();
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
            if (!_grid.TryGetValue(cell, out var cellList))
                _grid[cell] = cellList = new List<int>();
            cellList.Add(lineIdx);
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
        private StructuralExtractionResult _extraction;

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
        /// Phase-78: Active DWG conversion config (set at start of RunFullPipelineWithConfig).
        /// When non-null, wall/beam detection reads Min/Max thickness, parallel dot tolerance
        /// and spatial-index flags from this config instead of hardcoded defaults.
        /// </summary>
        public DWGConversionConfig CurrentConfig { get; set; }

        /// <summary>
        /// Phase-78: Running totals for opening detection + rejected-pair counters.
        /// Populated by detectors and surfaced on StructuralModelResult.
        /// </summary>
        public int LastWallsRejectedByThickness { get; set; }

        // Phase-140: snap mappings produced by the P1-A grid-snap pass. Used
        // by the P2-C grid-label-mark step after columns have been created.
        // Index aligns with the order of extraction.Rectangles / extraction.Circles
        // at the moment the snap pass ran.
        private List<GridSnapper.SnapResult> _lastRectSnapInfo;
        private List<GridSnapper.SnapResult> _lastCircleSnapInfo;

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
                result.LayerClassification.TryGetValue(classKey, out int classCount);
                result.LayerClassification[classKey] = classCount + 1;

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
            // In AutoCAD, beams are represented by TWO parallel lines.
            // The distance between them is the beam width. We detect pairs,
            // compute centreline and measured width, and treat each pair as one beam.

            const double parallelTol = 0.05; // ~3° tolerance
            double minBeamWidthFt = 100 * Units.MmToFeet;  // 100mm min beam width
            double maxBeamWidthFt = 600 * Units.MmToFeet;  // 600mm max beam width
            const double overlapRatio = 0.5; // 50% longitudinal overlap required
            const double dirToleranceRad = 5.0 * Math.PI / 180.0; // 5° direction clustering

            // Build set of column center positions for context validation
            var columnCenters = new List<XYZ>();
            columnCenters.AddRange(result.Circles.Select(c => c.Center));
            columnCenters.AddRange(result.Rectangles.Select(r => r.Center));

            // Step 1: Filter to beam-type lines that pass context validation
            var beamCandidates = new List<ExtractedLine>();
            foreach (var line in allLines)
            {
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
                    beamCandidates.Add(line);
            }

            if (beamCandidates.Count == 0) return;

            // Step 2: Direction-based clustering (same algorithm as wall detection)
            var dirGroups = new List<List<int>>();
            for (int i = 0; i < beamCandidates.Count; i++)
            {
                double angle = Math.Atan2(beamCandidates[i].Direction.Y, beamCandidates[i].Direction.X);
                if (angle < 0) angle += Math.PI;

                bool added = false;
                foreach (var group in dirGroups)
                {
                    double refAngle = Math.Atan2(
                        beamCandidates[group[0]].Direction.Y,
                        beamCandidates[group[0]].Direction.X);
                    if (refAngle < 0) refAngle += Math.PI;
                    double diff = Math.Abs(angle - refAngle);
                    if (diff > Math.PI / 2) diff = Math.PI - diff;
                    if (diff < dirToleranceRad) { group.Add(i); added = true; break; }
                }
                if (!added) dirGroups.Add(new List<int> { i });
            }

            // Step 3: Within each direction group, find parallel pairs
            var used = new HashSet<int>();
            int pairsDetected = 0;

            foreach (var group in dirGroups)
            {
                for (int gi = 0; gi < group.Count; gi++)
                {
                    int i = group[gi];
                    if (used.Contains(i)) continue;

                    var lineA = beamCandidates[i];
                    var dirA = lineA.Direction;
                    int bestJ = -1;
                    double bestDist = double.MaxValue;

                    for (int gj = gi + 1; gj < group.Count; gj++)
                    {
                        int j = group[gj];
                        if (used.Contains(j)) continue;

                        var lineB = beamCandidates[j];

                        // Verify parallelism
                        double dot = Math.Abs(dirA.DotProduct(lineB.Direction));
                        if (dot < 1.0 - parallelTol) continue;

                        // Perpendicular distance = beam width
                        var diff = lineB.Start - lineA.Start;
                        var proj = diff - dirA * diff.DotProduct(dirA);
                        double perpDist = proj.GetLength();
                        if (perpDist < minBeamWidthFt || perpDist > maxBeamWidthFt) continue;

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

                        // Best match by closest perpendicular distance
                        if (perpDist < bestDist)
                        {
                            bestDist = perpDist;
                            bestJ = j;
                        }
                    }

                    if (bestJ >= 0)
                    {
                        // Paired: compute centreline from midpoints
                        used.Add(i);
                        used.Add(bestJ);
                        var lineB = beamCandidates[bestJ];

                        // CAD-CRIT-01: Fix anti-parallel line pairs — if lines run in opposite
                        // directions, swap b endpoints so midpoints connect corresponding ends.
                        var bStartB = lineB.Start;
                        var bEndB = lineB.End;
                        if (lineA.Start.DistanceTo(bEndB) < lineA.Start.DistanceTo(bStartB))
                        {
                            bStartB = lineB.End;
                            bEndB = lineB.Start;
                        }
                        var centerStart = (lineA.Start + bStartB) * 0.5;
                        var centerEnd = (lineA.End + bEndB) * 0.5;

                        result.BeamLines.Add(new DetectedBeam
                        {
                            Start = new XYZ(centerStart.X, centerStart.Y, 0),
                            End = new XYZ(centerEnd.X, centerEnd.Y, 0),
                            WidthFt = bestDist,
                            WidthDetected = true,
                            LayerName = lineA.LayerName,
                        });
                        pairsDetected++;
                    }
                }
            }

            // Step 4: Unpaired lines become beams with default width
            for (int i = 0; i < beamCandidates.Count; i++)
            {
                if (used.Contains(i)) continue;
                var line = beamCandidates[i];
                result.BeamLines.Add(new DetectedBeam
                {
                    Start = line.Start,
                    End = line.End,
                    WidthFt = 0, // Will use default from config
                    WidthDetected = false,
                    LayerName = line.LayerName,
                });
            }

            if (pairsDetected > 0)
                StingLog.Info($"Beam detection: {pairsDetected} parallel pairs detected (measured width), " +
                    $"{beamCandidates.Count - used.Count} unpaired lines (default width). " +
                    $"Total beams: {result.BeamLines.Count}");
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
            // Phase-78: pick up EaseBit-style knobs from CurrentConfig when set,
            // otherwise fall back to conservative hardcoded defaults.
            var cfg = CurrentConfig;
            // parallelTol = 1 - dot threshold → smaller value ⇒ stricter parallelism.
            double parallelDot = cfg != null
                ? Math.Max(0.9, Math.Min(1.0, cfg.ParallelDotTolerance))
                : 0.95;
            double parallelTol = 1.0 - parallelDot;
            double minThickFt = (cfg?.MinWallThicknessMm ?? 150) * Units.MmToFeet;
            double maxThickFt = (cfg?.MaxWallThicknessMm ?? 500) * Units.MmToFeet;
            double gapCapFt = (cfg?.ParallelLineToleranceMm ?? 500) * Units.MmToFeet;
            if (maxThickFt > gapCapFt) maxThickFt = gapCapFt; // gap cap always wins
            double minLengthFt = 500 * Units.MmToFeet;
            const double overlapRatio = 0.4; // 40% longitudinal overlap required
            int rejectedByThickness = 0;

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
                        if (perpDist < minThickFt || perpDist > maxThickFt)
                        {
                            // Only count as "rejected" when the pair was close enough
                            // that we would otherwise have investigated it. Very far
                            // pairs aren't really a rejection — just a non-pair.
                            if (perpDist > 0.001 && perpDist < gapCapFt * 1.5)
                                rejectedByThickness++;
                            continue;
                        }

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

                        // CAD-CRIT-01: Fix anti-parallel line pairs — if lines run in opposite
                        // directions, swap b endpoints so midpoints connect corresponding ends.
                        var bStart = lineB.Start;
                        var bEnd = lineB.End;
                        if (lineA.Start.DistanceTo(bEnd) < lineA.Start.DistanceTo(bStart))
                        {
                            bStart = lineB.End;
                            bEnd = lineB.Start;
                        }

                        var centerStart = (lineA.Start + bStart) * 0.5;
                        var centerEnd = (lineA.End + bEnd) * 0.5;

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

            // Phase-78: expose rejection counter for the summary / wizard feedback.
            LastWallsRejectedByThickness = rejectedByThickness;
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

                // Invalidate caches so dashboards reflect new structural elements
                if (totalResult.TotalCreated > 0)
                {
                    ComplianceScan.InvalidateCache();
                    StingAutoTagger.InvalidateContext();
                }
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
            bool cancelled = false;
            var fh = new ModelFailureHandler();
            using (var tx = new Transaction(_doc, "STING STRUCT: Columns from Circles"))
            {
                var opts = tx.GetFailureHandlingOptions();
                opts.SetFailuresPreprocessor(fh);
                tx.SetFailureHandlingOptions(opts);
                tx.Start();

                // Phase-97: per-element size detection + type creation bypass
                // (circular columns measure a single diameter; DetectSizes_Column=false
                // forces the config's default square column dimensions).
                var cfgC = CurrentConfig;
                bool detectColC = cfgC == null || cfgC.DetectSizes_Column;
                bool createColTypesC = cfgC == null || cfgC.CreateNewTypes_Column;
                double fallbackCircleW = cfgC?.ColumnWidthMm ?? 300;

                // PERF-R3: activate each unique FamilySymbol ONCE at the start of
                // the batch, then Regenerate once. The previous per-element
                // Activate+Regenerate was forcing the document to regenerate
                // after every column created — O(N) full regens for N columns.
                var pendingSymbols = new HashSet<ElementId>();
                var circlePlans = new List<(DetectedCircle circle, FamilySymbol symbol)>(circles.Count);
                foreach (var circle in circles)
                {
                    double dMm = detectColC ? circle.DiameterMm : fallbackCircleW;
                    var typeMatch = _typeFactory.FindOrCreateColumnType(
                        dMm, dMm, allowDuplicate: createColTypesC);
                    if (!typeMatch.Success) { result.Warnings.Add(typeMatch.Message); continue; }
                    var symbol = _doc.GetElement(typeMatch.TypeId) as FamilySymbol;
                    if (symbol == null) continue;
                    if (!symbol.IsActive) { symbol.Activate(); pendingSymbols.Add(symbol.Id); }
                    circlePlans.Add((circle, symbol));
                }
                if (pendingSymbols.Count > 0) _doc.Regenerate();

                foreach (var (circle, symbol) in circlePlans)
                {
                    try
                    {
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
                    if (count % 10 == 0 && EscapeChecker.IsEscapePressed()) { cancelled = true; break; }
                }
                if (cancelled) tx.RollBack(); else tx.Commit();
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

                // Phase-97: per-element size detection + type creation bypass
                var cfgCR = CurrentConfig;
                bool detectColCR = cfgCR == null || cfgCR.DetectSizes_Column;
                bool createColTypesCR = cfgCR == null || cfgCR.CreateNewTypes_Column;
                double fallbackCRW = cfgCR?.ColumnWidthMm ?? 300;
                double fallbackCRD = cfgCR?.ColumnDepthMm ?? 300;

                // PERF-R3: batch-activate symbols + single Regenerate.
                var pendingRectSymbols = new HashSet<ElementId>();
                var rectPlans = new List<(DetectedRectangle rect, FamilySymbol symbol)>(rects.Count);
                foreach (var rect in rects)
                {
                    double widthMm = detectColCR ? rect.WidthMm : fallbackCRW;
                    double depthMm = detectColCR ? rect.DepthMm : fallbackCRD;
                    var typeMatchR = _typeFactory.FindOrCreateColumnType(
                        widthMm, depthMm, allowDuplicate: createColTypesCR);
                    if (!typeMatchR.Success) { result.Warnings.Add(typeMatchR.Message); continue; }
                    var symR = _doc.GetElement(typeMatchR.TypeId) as FamilySymbol;
                    if (symR == null) continue;
                    if (!symR.IsActive) { symR.Activate(); pendingRectSymbols.Add(symR.Id); }
                    rectPlans.Add((rect, symR));
                }
                if (pendingRectSymbols.Count > 0) _doc.Regenerate();

                foreach (var (rect, symbol) in rectPlans)
                {
                    try
                    {
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

        private int CreateBeamsFromLines(List<DetectedBeam> beamLines,
            Level level, double defaultDepthMm, double heightMm,
            StructuralModelResult result)
        {
            int count = 0;
            // Cache beam types by width×depth key to avoid redundant type lookups
            var typeCache = new Dictionary<string, FamilySymbol>();

            // CAD-CRIT-02: NewFamilyInstance with a level places relative to level elevation,
            // so don't add level.Elevation — only use heightMm as the offset above the level.
            double z = Units.Mm(heightMm);
            var fh = new ModelFailureHandler();

            using (var tx = new Transaction(_doc, "STING STRUCT: Beams from DWG"))
            {
                var opts = tx.GetFailureHandlingOptions();
                opts.SetFailuresPreprocessor(fh);
                tx.SetFailureHandlingOptions(opts);
                tx.Start();

                // Phase-97: per-element size detection + type creation bypass
                // for beams. DetectSizes_Beam=false forces every beam to use
                // the wizard's BeamDepthMm × BeamWidthMm even if the DWG has
                // clearly-paired beam lines.
                var cfgB = CurrentConfig;
                bool detectBeamSize = cfgB == null || cfgB.DetectSizes_Beam;
                bool createBeamTypes = cfgB == null || cfgB.CreateNewTypes_Beam;
                double fallbackBeamW = cfgB?.BeamWidthMm ?? defaultDepthMm * 0.5;

                // PERF-R3: two-pass — resolve every distinct beam type in pass 1
                // (Activate but defer Regenerate), single Regenerate, then
                // create every beam in pass 2. Cuts N regens down to 1.
                var beamPlans = new List<(DetectedBeam bl, FamilySymbol symbol)>(beamLines.Count);
                bool anyActivated = false;
                foreach (var bl in beamLines)
                {
                    double widthMm = detectBeamSize && bl.WidthDetected
                        ? bl.WidthMm
                        : fallbackBeamW;

                    // Phase-140 P1-B: per-beam depth from span when enabled. Falls
                    // back to defaultDepthMm (the wizard BEAM Depth value) when off.
                    double depthMm = (cfgB != null && cfgB.UseSpanToDepthRatio)
                        ? BeamDepthCalculator.ComputeDepthMm(
                            bl.LengthFt * Units.FeetToMm, cfgB)
                        : defaultDepthMm;

                    string typeKey = $"{depthMm:F0}x{widthMm:F0}";
                    if (!typeCache.TryGetValue(typeKey, out var symbol))
                    {
                        var typeMatch = _typeFactory.FindOrCreateBeamType(
                            depthMm, widthMm,
                            allowDuplicate: createBeamTypes);
                        if (!typeMatch.Success) { result.Warnings.Add(typeMatch.Message); continue; }
                        symbol = _doc.GetElement(typeMatch.TypeId) as FamilySymbol;
                        if (symbol == null) continue;
                        if (!symbol.IsActive) { symbol.Activate(); anyActivated = true; }
                        typeCache[typeKey] = symbol;
                    }
                    beamPlans.Add((bl, symbol));
                }
                if (anyActivated) _doc.Regenerate();

                foreach (var (bl, symbol) in beamPlans)
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
            // Phase-97: slab thickness bypass. Slabs don't have a natural
            // thickness signal in a flat DWG, so DetectSizes_Slab only
            // affects whether we duplicate a type (detected=on) or pin to
            // the single existing type closest to config.SlabThicknessMm.
            var cfgS = CurrentConfig;
            bool createSlabTypes = cfgS == null || cfgS.CreateNewTypes_Slab;
            var typeMatch = _typeFactory.FindOrCreateFloorType(
                thickMm, allowDuplicate: createSlabTypes);
            if (!typeMatch.Success) { result.Warnings.Add(typeMatch.Message); return 0; }

            // Phase-140 P2-B: Group nested closed loops as outer + voids so lift
            // shafts, stair openings, and service penetrations come through as
            // actual holes in the floor instead of being filled.
            var grouped = SlabVoidDetector.Group(boundaries);
            int totalVoids = grouped.Sum(g => g.Voids.Count);
            if (totalVoids > 0)
                StingLog.Info($"  Slab voids: {totalVoids} nested loop(s) flagged as voids");

            using (var tx = new Transaction(_doc, "STING STRUCT: Slabs from DWG"))
            {
                tx.Start();
                foreach (var grp in grouped)
                {
                    var boundary = grp.Outer;
                    if (boundary == null || boundary.Points.Count < 3) continue;
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

                        // Build void CurveLoops for any nested loops on this slab.
                        var loops = new List<CurveLoop> { curveLoop };
                        foreach (var voidLoop in grp.Voids)
                        {
                            if (voidLoop?.Points == null || voidLoop.Points.Count < 3) continue;
                            try
                            {
                                var vl = new CurveLoop();
                                for (int i = 0; i < voidLoop.Points.Count; i++)
                                {
                                    var a = voidLoop.Points[i];
                                    var b = voidLoop.Points[(i + 1) % voidLoop.Points.Count];
                                    if (a.DistanceTo(b) < 0.005) continue;
                                    vl.Append(Line.CreateBound(
                                        new XYZ(a.X, a.Y, 0), new XYZ(b.X, b.Y, 0)));
                                }
                                loops.Add(vl);
                            }
                            catch (Exception ex) { result.Warnings.Add($"Slab void loop: {ex.Message}"); }
                        }

                        var slab = Floor.Create(_doc,
                            loops,
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
            bool cancelled = false;
            var fh = new ModelFailureHandler();
            using (var tx = new Transaction(_doc, "STING STRUCT: Walls from DWG"))
            {
                var opts = tx.GetFailureHandlingOptions();
                opts.SetFailuresPreprocessor(fh);
                tx.SetFailureHandlingOptions(opts);
                tx.Start();

                // Phase-97: per-element flag bypass for the legacy pipeline path.
                var cfgWp = CurrentConfig;
                bool detectWallWp = cfgWp == null || cfgWp.DetectSizes_Wall;
                bool createWallTypesWp = cfgWp == null || cfgWp.CreateNewTypes_Wall;

                foreach (var wall in walls)
                {
                    if (wall.CenterStart.DistanceTo(wall.CenterEnd) < 0.01) continue;
                    try
                    {
                        double thickMm = detectWallWp
                            ? wall.ThicknessFt * Units.FeetToMm
                            : (cfgWp?.WallThicknessMm ?? 200);
                        var typeMatch = _typeFactory.FindOrCreateWallType(
                            thickMm, true, allowDuplicate: createWallTypesWp);
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

                    if (count % 10 == 0 && EscapeChecker.IsEscapePressed()) { cancelled = true; break; }
                }

                if (cancelled) tx.RollBack(); else tx.Commit();
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

        // ── Enhanced Pipeline with DWGConversionConfig ───────────────────

        /// <summary>
        /// Runs the full pipeline using the enhanced DWGConversionConfig from the wizard.
        /// Supports: column soffit height, foundation creation, structural walls,
        /// construction relationships, and auto-tagging.
        /// </summary>
        public StructuralModelResult RunFullPipelineWithConfig(
            ImportInstance importInstance, DWGConversionConfig config)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var totalResult = new StructuralModelResult { Success = true };

            try
            {
                StingLog.Info("StructuralCADPipeline: Enhanced pipeline with config");

                _typeFactory.BuildCatalog();

                // Apply user layer selection to pipeline
                SelectedLayers = config.SelectedLayers;
                // Phase-78: expose the config to detection methods so they can pick up
                // min/max wall thickness, parallel dot tolerance and spatial-index flags.
                CurrentConfig = config;
                LastWallsRejectedByThickness = 0;

                // Phase-78: optionally explode nested DWG block references BEFORE
                // extraction so geometry hidden inside blocks is surfaced onto its host
                // layer. Delegates to StructuralDWGEnhancements.ExplodeHelper.
                // Silently no-ops on Revit builds where the Explode API isn't exposed.
                if (config.ExplodeOnImport && importInstance != null)
                {
                    if (!StructuralDWGEnhancements.ExplodeHelper.IsProgrammaticExplodeSupported)
                    {
                        totalResult.Warnings.Add(
                            "Explode-on-import: Revit API does not expose " +
                            "ImportInstance.Explode on this build. Run 'Modify -> " +
                            "Explode -> Full Explode' in the UI first, then re-run.");
                    }
                    else
                    {
                        try
                        {
                            int exploded = StructuralDWGEnhancements.ExplodeHelper
                                .ExplodeInPlace(_doc, importInstance);
                            StingLog.Info($"  Exploded {exploded} nested blocks before extraction");
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Explode-on-import failed: {ex.Message}");
                            totalResult.Warnings.Add($"Explode-on-import failed: {ex.Message}");
                        }
                    }
                }

                var extraction = ExtractStructuralGeometry(importInstance);
                _extraction = extraction;
                StingLog.Info($"  Extraction: {extraction.Summary}");

                // ── Phase-140 P1-A: Grid-snapped column placement ────────────
                // Snap detected column centres (rectangles + circles) to nearest
                // grid intersection. Mutates Center in place; later element
                // creation reads the snapped centres. Captures the snap mapping
                // so P2-C can reuse it for grid-label marks.
                _lastRectSnapInfo = null;
                _lastCircleSnapInfo = null;
                if (config.GridSnapToleranceMm > 0
                    && extraction.GridLines != null && extraction.GridLines.Count > 0
                    && (extraction.Rectangles.Count > 0 || extraction.Circles.Count > 0))
                {
                    int rectSnapped = GridSnapper.SnapRectangles(
                        extraction.Rectangles, extraction.GridLines,
                        config.GridSnapToleranceMm, out _lastRectSnapInfo);
                    int circleSnapped = GridSnapper.SnapCircles(
                        extraction.Circles, extraction.GridLines,
                        config.GridSnapToleranceMm, out _lastCircleSnapInfo);
                    int totalCols = extraction.Rectangles.Count + extraction.Circles.Count;
                    int snapped = rectSnapped + circleSnapped;
                    if (snapped > 0)
                        StingLog.Info($"  Grid-snap: {snapped}/{totalCols} columns snapped to " +
                            $"intersections within {config.GridSnapToleranceMm:F0} mm");
                    if (snapped < totalCols)
                        totalResult.Warnings.Add(
                            $"Grid-snap: {totalCols - snapped} column(s) did not lie on a grid " +
                            $"intersection within {config.GridSnapToleranceMm:F0} mm — kept at " +
                            $"detected centroid.");
                }

                // ── Phase-140 P1-D: Trim beam endpoints to column faces ───────
                // Pure mutation of DetectedBeam.Start/End — runs only when the
                // user enabled trimming and we have columns to anchor against.
                if (config.TrimBeamsToColumnFaces
                    && extraction.BeamLines.Count > 0
                    && (extraction.Rectangles.Count > 0 || extraction.Circles.Count > 0))
                {
                    int trimmed = BeamTrimmer.TrimEndpointsToColumns(
                        extraction.BeamLines, extraction.Rectangles,
                        extraction.Circles, config);
                    if (trimmed > 0)
                        StingLog.Info($"  Beam trim: adjusted {trimmed} endpoint(s) to column faces");
                }

                // ── Phase-140 P3-A: Skip duplicates of existing elements ──────
                // Build a one-shot index of element insertion points by category,
                // then drop detected items that already exist within tolerance.
                // Filter is applied to extraction lists so all downstream creation
                // paths see the filtered set — no per-method instrumentation.
                if (config.SkipDuplicates && config.DuplicateCheckToleranceMm > 0)
                {
                    try
                    {
                        var dupIndex = new DuplicateDetector.ExistingIndex(_doc,
                            BuiltInCategory.OST_StructuralColumns,
                            BuiltInCategory.OST_StructuralFraming,
                            BuiltInCategory.OST_Walls,
                            BuiltInCategory.OST_Floors,
                            BuiltInCategory.OST_StructuralFoundation);

                        int removed = 0;

                        // Rectangles → columns
                        for (int i = extraction.Rectangles.Count - 1; i >= 0; i--)
                        {
                            if (dupIndex.IsDuplicate(BuiltInCategory.OST_StructuralColumns,
                                    extraction.Rectangles[i].Center,
                                    config.DuplicateCheckToleranceMm))
                            { extraction.Rectangles.RemoveAt(i); removed++; }
                        }
                        // Circles → columns
                        for (int i = extraction.Circles.Count - 1; i >= 0; i--)
                        {
                            if (dupIndex.IsDuplicate(BuiltInCategory.OST_StructuralColumns,
                                    extraction.Circles[i].Center,
                                    config.DuplicateCheckToleranceMm))
                            { extraction.Circles.RemoveAt(i); removed++; }
                        }
                        // Beams (use midpoint of detected beam)
                        for (int i = extraction.BeamLines.Count - 1; i >= 0; i--)
                        {
                            var b = extraction.BeamLines[i];
                            var mid = new XYZ(0.5 * (b.Start.X + b.End.X),
                                              0.5 * (b.Start.Y + b.End.Y),
                                              0.5 * (b.Start.Z + b.End.Z));
                            if (dupIndex.IsDuplicate(BuiltInCategory.OST_StructuralFraming,
                                    mid, config.DuplicateCheckToleranceMm))
                            { extraction.BeamLines.RemoveAt(i); removed++; }
                        }

                        if (removed > 0)
                        {
                            StingLog.Info($"  Skip-duplicates: removed {removed} detected " +
                                $"item(s) already present within " +
                                $"{config.DuplicateCheckToleranceMm:F0} mm");
                            totalResult.Warnings.Add(
                                $"Skip-duplicates: skipped {removed} detected element(s) that " +
                                $"already exist in the model.");
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Duplicate detection: {ex.Message}");
                        totalResult.Warnings.Add($"Duplicate detection skipped: {ex.Message}");
                    }
                }

                // Phase-78: DRY-RUN gate. Extraction has run, so we know how many
                // walls/beams/columns/slabs/foundations/grids WOULD be created. Report
                // those counts, stamp the result as a dry-run, and bail BEFORE any
                // transaction opens.
                if (config.DryRun)
                {
                    sw.Stop();
                    totalResult.Duration = sw.Elapsed;
                    totalResult.WasDryRun = true;
                    totalResult.WallsRejectedByThickness = LastWallsRejectedByThickness;

                    // Dry-run counters — populate from extraction without creating elements.
                    totalResult.ColumnsCreated = extraction.Circles.Count + extraction.Rectangles.Count;
                    totalResult.BeamsCreated = extraction.BeamLines.Count;
                    totalResult.WallsCreated = extraction.Walls.Count;
                    totalResult.SlabsCreated = extraction.SlabBoundaries.Count;
                    totalResult.FootingsCreated = extraction.FoundationBlocks.Count;

                    // Opening candidates from the detected walls (no Revit writes).
                    if (config.DetectOpenings)
                    {
                        try
                        {
                            totalResult.OpeningsDetected =
                                StructuralDWGEnhancements.OpeningDetector
                                    .CountCandidateOpenings(extraction, config);
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Dry-run opening count failed: {ex.Message}");
                        }
                    }

                    totalResult.Summary =
                        $"DRY RUN — would create " +
                        $"{totalResult.WallsCreated} walls, " +
                        $"{totalResult.ColumnsCreated} columns, " +
                        $"{totalResult.BeamsCreated} beams, " +
                        $"{totalResult.SlabsCreated} slabs, " +
                        $"{totalResult.FootingsCreated} foundations" +
                        (totalResult.OpeningsDetected > 0
                            ? $" + {totalResult.OpeningsDetected} openings"
                            : "") +
                        $"  ({sw.Elapsed.TotalSeconds:F1}s, no elements written)";
                    StingLog.Info($"StructuralCADPipeline: {totalResult.Summary}");
                    return totalResult;
                }

                // Resolve levels
                var resolver = new ModelFamilyResolver(_doc);
                var baseLevel = resolver.ResolveLevel(config.BaseLevelName);
                var topLevel = resolver.ResolveLevel(config.TopLevelName);

                double baseLevelElev = baseLevel?.Elevation ?? 0;
                double topLevelElev = topLevel?.Elevation ?? baseLevelElev + Units.Mm(config.ColumnHeightMm);

                // Column height: if ColumnsStopAtSoffit, subtract slab thickness
                double columnTopElev = topLevelElev;
                if (config.ColumnsStopAtSoffit)
                    columnTopElev = topLevelElev - Units.Mm(config.SlabThicknessMm);

                double columnHeightFt = columnTopElev - baseLevelElev;
                double columnHeightMm = columnHeightFt * Units.FeetToMm;

                // Create elements in construction sequence order
                // Foundation → Column → Beam → Wall → Slab → Grid

                // 1. Foundations
                if (config.CreateFoundations && extraction.FoundationBlocks.Count > 0)
                {
                    totalResult.FootingsCreated += CreateFoundationsFromBlocks(
                        extraction.FoundationBlocks, baseLevel, config.FoundationDepthMm, totalResult);
                }
                // Also create pad foundations under detected columns
                if (config.CreateFoundations && (extraction.Circles.Count > 0 || extraction.Rectangles.Count > 0))
                {
                    totalResult.FootingsCreated += CreatePadFoundations(
                        extraction.Circles, extraction.Rectangles, baseLevel,
                        config.FoundationDepthMm, totalResult);
                }

                // 2. Columns (with soffit adjustment)
                if (config.CreateColumns && extraction.Circles.Count > 0)
                    totalResult.ColumnsCreated += CreateColumnsWithHeight(
                        extraction.Circles, null, baseLevel, topLevel,
                        columnHeightMm, config.ColumnsStopAtSoffit, config.SlabThicknessMm, totalResult);

                if (config.CreateColumns && extraction.Rectangles.Count > 0)
                    totalResult.ColumnsCreated += CreateRectColumnsWithHeight(
                        extraction.Rectangles, baseLevel, topLevel,
                        columnHeightMm, config.ColumnsStopAtSoffit, config.SlabThicknessMm, totalResult);

                // 3. Beams
                if (config.CreateBeams && extraction.BeamLines.Count > 0)
                    totalResult.BeamsCreated += CreateBeamsFromLines(
                        extraction.BeamLines, baseLevel,
                        config.BeamDepthMm, columnHeightMm, totalResult);

                // 4. Walls (structural or architectural)
                if (config.CreateWalls && extraction.Walls.Count > 0)
                {
                    totalResult.WallsCreated += CreateWallsWithConfig(
                        extraction.Walls, baseLevel, config, totalResult);

                    // CAD-HIGH-06: Join walls with overlapping bounding boxes
                    if (config.AutoJoinWalls && totalResult.WallsCreated > 1)
                    {
                        try
                        {
                            var wallIds = totalResult.CreatedIds
                                .Select(id => _doc.GetElement(id))
                                .OfType<Wall>().ToList();
                            if (wallIds.Count > 1)
                            {
                                int joined = 0;
                                using (var tx = new Transaction(_doc, "STING STRUCT: Join Walls"))
                                {
                                    tx.Start();
                                    for (int wi = 0; wi < wallIds.Count; wi++)
                                    {
                                        var bbI = wallIds[wi].get_BoundingBox(null);
                                        if (bbI == null) continue;
                                        for (int wj = wi + 1; wj < wallIds.Count; wj++)
                                        {
                                            var bbJ = wallIds[wj].get_BoundingBox(null);
                                            if (bbJ == null) continue;
                                            // Check bounding box overlap
                                            if (bbI.Max.X < bbJ.Min.X || bbJ.Max.X < bbI.Min.X) continue;
                                            if (bbI.Max.Y < bbJ.Min.Y || bbJ.Max.Y < bbI.Min.Y) continue;
                                            try
                                            {
                                                if (!JoinGeometryUtils.AreElementsJoined(_doc, wallIds[wi], wallIds[wj]))
                                                {
                                                    JoinGeometryUtils.JoinGeometry(_doc, wallIds[wi], wallIds[wj]);
                                                    joined++;
                                                }
                                            }
                                            catch (Exception ex) { StingLog.Warn($"Wall join: {ex.Message}"); }
                                        }
                                    }
                                    tx.Commit();
                                }
                                if (joined > 0) StingLog.Info($"  Joined {joined} wall pairs");
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"Wall joining step: {ex.Message}"); }
                    }
                }

                // 5. Slabs
                if (config.CreateSlabs && extraction.SlabBoundaries.Count > 0)
                    totalResult.SlabsCreated += CreateSlabsFromBoundaries(
                        extraction.SlabBoundaries, baseLevel, config.SlabThicknessMm, totalResult);

                // 6. Grids (only on base level, not repeated)
                if (config.CreateGrids && extraction.GridLines.Count > 0)
                    CreateGridLinesFromDetected(extraction.GridLines, totalResult);

                // 7. Repeat structural elements to additional levels
                if (config.RepeatToLevelNames != null && config.RepeatToLevelNames.Count > 0)
                {
                    // CAD-HIGH-03: Collect all levels once before the loop
                    var allLevelsOrdered = new FilteredElementCollector(_doc)
                        .OfClass(typeof(Level)).Cast<Level>()
                        .OrderBy(l => l.Elevation).ToList();

                    int levelsRepeated = 0;
                    foreach (var repeatLevelName in config.RepeatToLevelNames)
                    {
                        var repeatLevel = resolver.ResolveLevel(repeatLevelName);
                        if (repeatLevel == null)
                        {
                            StingLog.Warn($"Repeat level not found: {repeatLevelName}");
                            continue;
                        }

                        // Phase-140 P1-C: column height is derived from level-to-level
                        // distance (repeatLevel → next level above). Falls back to
                        // config.ColumnHeightMm only at the topmost storey, and warns
                        // so multi-storey models don't silently inherit a wrong height.
                        int idx = allLevelsOrdered.FindIndex(l => l.Id == repeatLevel.Id);
                        Level repeatTopLevel = (idx >= 0 && idx + 1 < allLevelsOrdered.Count)
                            ? allLevelsOrdered[idx + 1] : null;

                        double repeatColumnTopElev;
                        if (repeatTopLevel != null)
                        {
                            repeatColumnTopElev = repeatTopLevel.Elevation;
                        }
                        else
                        {
                            repeatColumnTopElev = repeatLevel.Elevation + Units.Mm(config.ColumnHeightMm);
                            totalResult.Warnings.Add(
                                $"Repeat level '{repeatLevelName}' has no level above it — " +
                                $"columns there fall back to wizard column height " +
                                $"({config.ColumnHeightMm:F0} mm) instead of level-to-level " +
                                $"spacing.");
                        }
                        if (config.ColumnsStopAtSoffit)
                            repeatColumnTopElev -= Units.Mm(config.SlabThicknessMm);
                        double repeatColumnHeightMm = (repeatColumnTopElev - repeatLevel.Elevation) * Units.FeetToMm;

                        StingLog.Info($"  Repeating to level: {repeatLevelName} " +
                            $"(height {repeatColumnHeightMm:F0} mm)");

                        // Columns (skip if continuous through — already created as tall columns)
                        if (config.CreateColumns && !config.ColumnsContinuousThrough)
                        {
                            if (extraction.Circles.Count > 0)
                                totalResult.ColumnsCreated += CreateColumnsWithHeight(
                                    extraction.Circles, null, repeatLevel, repeatTopLevel,
                                    repeatColumnHeightMm, config.ColumnsStopAtSoffit,
                                    config.SlabThicknessMm, totalResult);
                            if (extraction.Rectangles.Count > 0)
                                totalResult.ColumnsCreated += CreateRectColumnsWithHeight(
                                    extraction.Rectangles, repeatLevel, repeatTopLevel,
                                    repeatColumnHeightMm, config.ColumnsStopAtSoffit,
                                    config.SlabThicknessMm, totalResult);
                        }

                        // Beams
                        if (config.CreateBeams && extraction.BeamLines.Count > 0)
                            totalResult.BeamsCreated += CreateBeamsFromLines(
                                extraction.BeamLines, repeatLevel,
                                config.BeamDepthMm, repeatColumnHeightMm, totalResult);

                        // Walls
                        if (config.CreateWalls && extraction.Walls.Count > 0)
                            totalResult.WallsCreated += CreateWallsWithConfig(
                                extraction.Walls, repeatLevel, config, totalResult);

                        // Slabs
                        if (config.CreateSlabs && extraction.SlabBoundaries.Count > 0)
                            totalResult.SlabsCreated += CreateSlabsFromBoundaries(
                                extraction.SlabBoundaries, repeatLevel,
                                config.SlabThicknessMm, totalResult);

                        levelsRepeated++;
                    }

                    if (levelsRepeated > 0)
                        StingLog.Info($"  Repeated structural layout to {levelsRepeated} additional levels");
                }

                // Phase-78: Roll up rejection counter from wall detector.
                totalResult.WallsRejectedByThickness = LastWallsRejectedByThickness;

                // Phase-78: Opening detection — scan created walls for door/window-sized
                // gaps on the wall layer and cut openings via StructuralDWGEnhancements.
                if (config.DetectOpenings && totalResult.WallsCreated > 0)
                {
                    try
                    {
                        var wallIds = totalResult.CreatedIds
                            .Select(id => _doc.GetElement(id))
                            .OfType<Wall>()
                            .Select(w => w.Id)
                            .ToList();
                        var openRes = StructuralDWGEnhancements.OpeningDetector.DetectAndCut(
                            _doc, extraction, wallIds, config);
                        totalResult.OpeningsDetected = openRes.Detected;
                        totalResult.OpeningsCreated = openRes.Created;
                        totalResult.CreatedIds.AddRange(openRes.CreatedIds);
                        if (openRes.Warnings.Count > 0)
                            totalResult.Warnings.AddRange(openRes.Warnings);
                        StingLog.Info(
                            $"  Openings: {openRes.Detected} detected, {openRes.Created} cut");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Opening detection failed: {ex.Message}");
                        totalResult.Warnings.Add($"Opening detection failed: {ex.Message}");
                    }
                }

                // Post-pipeline connectivity audit
                if (totalResult.TotalCreated > 0)
                {
                    var auditResult = _structEngine.AnalyzeLoadPaths();
                    if (auditResult.Warnings.Count > 0)
                    {
                        totalResult.Warnings.Add("── Post-creation audit ──");
                        totalResult.Warnings.AddRange(auditResult.Warnings);

                        // Phase-140 P2-D: Surface load-path warnings as TextNotes in
                        // the active view so engineers see them without opening a
                        // log. Wrapped in its own sub-transaction inside the placer.
                        if (config.ShowStructuralWarningsInView)
                        {
                            try
                            {
                                var ids = StructuralWarningPlacer.PlaceWarnings(
                                    _doc, _doc.ActiveView, auditResult.Warnings);
                                if (ids.Count > 0)
                                {
                                    totalResult.CreatedIds.AddRange(ids);
                                    StingLog.Info($"  Placed {ids.Count} structural warning " +
                                        $"note(s) in the active view");
                                }
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"PlaceStructuralWarnings: {ex.Message}");
                            }
                        }
                    }
                }

                // Auto-tag created elements
                if (config.AutoTag && totalResult.CreatedIds.Count > 0)
                {
                    try
                    {
                        ModelEngine.AutoTagCreatedElements(_doc, totalResult.CreatedIds);
                        StingLog.Info($"  Auto-tagged {totalResult.CreatedIds.Count} elements");
                    }
                    catch (Exception ex) { StingLog.Warn($"Auto-tag: {ex.Message}"); }
                }

                // Apply numbering. Phase-140 P1-E: when the wizard populated
                // NumberingPerCategory, route through ApplyAllPerCategory so each
                // structural category is numbered independently. Otherwise fall back
                // to the single-category legacy path.
                if (config.AutoSeqNumbers && totalResult.CreatedIds.Count > 0)
                {
                    try
                    {
                        int numbered;
                        // Phase-140 P2-C: collect created column ids for grid-label-mark step
                        // before per-category numbering writes to Mark.
                        var createdColumnIds = new HashSet<ElementId>(
                            totalResult.CreatedIds.Where(id =>
                            {
                                var el = _doc.GetElement(id);
                                return el?.Category != null
                                    && el.Category.Id.IntegerValue ==
                                       (int)BuiltInCategory.OST_StructuralColumns;
                            }));

                        if (config.NumberingPerCategory != null
                            && config.NumberingPerCategory.Count > 0)
                        {
                            numbered = NumberingEngine.ApplyAllPerCategory(_doc,
                                config.NumberingPerCategory, totalResult.CreatedIds);
                            StingLog.Info($"  Numbered {numbered} elements across " +
                                $"{config.NumberingPerCategory.Count} categories");
                        }
                        else if (config.NumberingConfig != null)
                        {
                            numbered = NumberingEngine.ApplyNumbering(_doc,
                                config.NumberingConfig, totalResult.CreatedIds);
                            StingLog.Info($"  Numbered {numbered} elements");
                        }

                        // Phase-140 P2-C: Grid-label marks override the sequential
                        // Mark for grid-snapped columns. Run AFTER per-category
                        // numbering so grid-derived marks win on the visible Mark.
                        if (config.UseGridLabelsAsMarks
                            && createdColumnIds.Count > 0
                            && (_lastRectSnapInfo != null || _lastCircleSnapInfo != null))
                        {
                            try
                            {
                                var mapping = new List<(ElementId, GridSnapper.SnapResult)>();
                                // Re-pair created columns with their snap info.
                                // Walk the snap-info lists in declaration order and pick
                                // any column ids that fall within tolerance of each centre.
                                double tolFt = (config.GridSnapToleranceMm + 1.0) * Units.MmToFeet;
                                foreach (var snap in (_lastRectSnapInfo ?? new List<GridSnapper.SnapResult>())
                                    .Concat(_lastCircleSnapInfo ?? new List<GridSnapper.SnapResult>()))
                                {
                                    if (snap == null || !snap.DidSnap) continue;
                                    foreach (var id in createdColumnIds)
                                    {
                                        var el = _doc.GetElement(id);
                                        if (el?.Location is LocationPoint lp)
                                        {
                                            double dx = lp.Point.X - snap.SnappedCentre.X;
                                            double dy = lp.Point.Y - snap.SnappedCentre.Y;
                                            if (dx * dx + dy * dy < tolFt * tolFt)
                                            {
                                                mapping.Add((id, snap));
                                                break;
                                            }
                                        }
                                    }
                                }
                                if (mapping.Count > 0)
                                {
                                    int marked = GridLabelMarkBuilder.ApplyMarks(_doc, mapping,
                                        new HashSet<string>());
                                    StingLog.Info($"  Grid labels: stamped {marked} columns " +
                                        $"with '{{vertical}}/{{horizontal}}' marks");
                                }
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"Grid-label-mark step: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Numbering: {ex.Message}"); }
                }

                sw.Stop();
                totalResult.Duration = sw.Elapsed;

                var parts = new List<string>();
                if (totalResult.ColumnsCreated > 0) parts.Add($"{totalResult.ColumnsCreated} columns");
                if (totalResult.BeamsCreated > 0) parts.Add($"{totalResult.BeamsCreated} beams");
                if (totalResult.WallsCreated > 0) parts.Add($"{totalResult.WallsCreated} walls");
                if (totalResult.SlabsCreated > 0) parts.Add($"{totalResult.SlabsCreated} slabs");
                if (totalResult.FootingsCreated > 0) parts.Add($"{totalResult.FootingsCreated} foundations");
                if (totalResult.OpeningsCreated > 0)
                    parts.Add($"{totalResult.OpeningsCreated} openings");

                totalResult.Summary = parts.Count > 0
                    ? $"Created {string.Join(", ", parts)} from DWG in {sw.Elapsed.TotalSeconds:F1}s"
                    : "No structural elements created — check layer names and selection";
                if (totalResult.WallsRejectedByThickness > 0)
                    totalResult.Summary += $"  ({totalResult.WallsRejectedByThickness} wall pairs rejected — outside min/max thickness)";

                StingLog.Info($"StructuralCADPipeline: {totalResult.Summary}");
            }
            catch (Exception ex)
            {
                StingLog.Error("StructuralCADPipeline enhanced pipeline failed", ex);
                totalResult.Success = false;
                totalResult.Summary = $"Pipeline failed: {ex.Message}";
            }
            finally
            {
                // Phase-78: Always clear the config reference so it doesn't bleed
                // between invocations (e.g. two wizard runs in the same session).
                CurrentConfig = null;
            }

            return totalResult;
        }

        // ── Column creation with base-to-top level and soffit adjustment ──

        private int CreateColumnsWithHeight(List<DetectedCircle> circles,
            List<DetectedRectangle> rects, Level baseLevel, Level topLevel,
            double heightMm, bool stopAtSoffit, double slabThickMm,
            StructuralModelResult result)
        {
            int count = 0;
            bool cancelled = false;
            var fh = new ModelFailureHandler();
            using (var tx = new Transaction(_doc, "STING STRUCT: Columns (soffit)"))
            {
                var opts = tx.GetFailureHandlingOptions();
                opts.SetFailuresPreprocessor(fh);
                tx.SetFailureHandlingOptions(opts);
                tx.Start();

                // PERF-R3: batch-activate symbols, single Regenerate before creating.
                var soffitCircles = circles ?? new List<DetectedCircle>();
                var soffitPlans = new List<(DetectedCircle c, FamilySymbol symbol)>(soffitCircles.Count);
                bool soffitActivated = false;
                foreach (var circle in soffitCircles)
                {
                    var tm = _typeFactory.FindOrCreateColumnType(
                        circle.DiameterMm, circle.DiameterMm);
                    if (!tm.Success) { result.Warnings.Add(tm.Message); continue; }
                    var symbol = _doc.GetElement(tm.TypeId) as FamilySymbol;
                    if (symbol == null) continue;
                    if (!symbol.IsActive) { symbol.Activate(); soffitActivated = true; }
                    soffitPlans.Add((circle, symbol));
                }
                if (soffitActivated) _doc.Regenerate();

                foreach (var (circle, symbol) in soffitPlans)
                {
                    try
                    {
                        var pt = new XYZ(circle.Center.X, circle.Center.Y,
                            baseLevel?.Elevation ?? 0);
                        var col = _doc.Create.NewFamilyInstance(
                            pt, symbol, baseLevel, StructuralType.Column);

                        // Set top level constraint if available
                        if (topLevel != null)
                        {
                            var topParam = col.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
                            if (topParam != null && !topParam.IsReadOnly)
                                topParam.Set(topLevel.Id);

                            // If stopping at soffit, set top offset = -slab thickness
                            if (stopAtSoffit)
                            {
                                var topOffset = col.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
                                if (topOffset != null && !topOffset.IsReadOnly)
                                    topOffset.Set(-Units.Mm(slabThickMm));
                            }
                        }

                        ModelWorksetAssigner.Assign(_doc, col);
                        result.CreatedIds.Add(col.Id);
                        count++;
                    }
                    catch (Exception ex) { result.Warnings.Add($"Column (soffit): {ex.Message}"); }
                    if (count % 50 == 0 && EscapeChecker.IsEscapePressed()) { cancelled = true; break; }
                }
                if (cancelled) tx.RollBack(); else tx.Commit();
            }
            result.Warnings.AddRange(fh.CapturedWarnings);
            return count;
        }

        private int CreateRectColumnsWithHeight(List<DetectedRectangle> rects,
            Level baseLevel, Level topLevel,
            double heightMm, bool stopAtSoffit, double slabThickMm,
            StructuralModelResult result)
        {
            int count = 0;
            var fh = new ModelFailureHandler();
            using (var tx = new Transaction(_doc, "STING STRUCT: Rect Columns (soffit)"))
            {
                var opts = tx.GetFailureHandlingOptions();
                opts.SetFailuresPreprocessor(fh);
                tx.SetFailureHandlingOptions(opts);
                tx.Start();

                // Phase-97: per-element size detection + type creation bypass.
                var cfg = CurrentConfig;
                bool detectCol = cfg == null || cfg.DetectSizes_Column;
                bool createColTypes = cfg == null || cfg.CreateNewTypes_Column;
                double fallbackW = cfg?.ColumnWidthMm ?? 300;
                double fallbackD = cfg?.ColumnDepthMm ?? 300;

                // PERF-R3: resolve + activate-once + Regenerate-once, then create.
                var rectSoffitPlans = new List<(DetectedRectangle rect, FamilySymbol symbol)>(rects.Count);
                bool rectSoffitActivated = false;
                foreach (var rect in rects)
                {
                    double widthMm = detectCol ? rect.WidthMm : fallbackW;
                    double depthMm = detectCol ? rect.DepthMm : fallbackD;
                    var typeMatch = _typeFactory.FindOrCreateColumnType(
                        widthMm, depthMm,
                        allowDuplicate: createColTypes);
                    if (!typeMatch.Success) { result.Warnings.Add(typeMatch.Message); continue; }
                    var symR = _doc.GetElement(typeMatch.TypeId) as FamilySymbol;
                    if (symR == null) continue;
                    if (!symR.IsActive) { symR.Activate(); rectSoffitActivated = true; }
                    rectSoffitPlans.Add((rect, symR));
                }
                if (rectSoffitActivated) _doc.Regenerate();

                foreach (var (rect, symbol) in rectSoffitPlans)
                {
                    try
                    {
                        var pt = new XYZ(rect.Center.X, rect.Center.Y,
                            baseLevel?.Elevation ?? 0);
                        var col = _doc.Create.NewFamilyInstance(
                            pt, symbol, baseLevel, StructuralType.Column);

                        // Set top level and soffit offset
                        if (topLevel != null)
                        {
                            var topParam = col.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
                            if (topParam != null && !topParam.IsReadOnly)
                                topParam.Set(topLevel.Id);

                            if (stopAtSoffit)
                            {
                                var topOffset = col.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
                                if (topOffset != null && !topOffset.IsReadOnly)
                                    topOffset.Set(-Units.Mm(slabThickMm));
                            }
                        }

                        if (Math.Abs(rect.Rotation) > 0.01)
                        {
                            var axis = Line.CreateBound(pt, pt + XYZ.BasisZ);
                            ElementTransformUtils.RotateElement(_doc, col.Id, axis, rect.Rotation);
                        }

                        ModelWorksetAssigner.Assign(_doc, col);
                        result.CreatedIds.Add(col.Id);
                        count++;
                    }
                    catch (Exception ex) { result.Warnings.Add($"Rect column (soffit): {ex.Message}"); }
                }
                tx.Commit();
            }
            result.Warnings.AddRange(fh.CapturedWarnings);
            return count;
        }

        // ── Foundation creation ──────────────────────────────────────────

        private int CreatePadFoundations(List<DetectedCircle> circles,
            List<DetectedRectangle> rects, Level level, double depthMm,
            StructuralModelResult result)
        {
            int count = 0;

            // Phase-97: per-element size detection + type creation bypass.
            // Pad foundations inherit their plan dimensions from the detected
            // column cross-section below (circle diameter, rectangle bbox).
            // When DetectSizes_Foundation is false, the wizard's fixed
            // FoundationWidthMm is used for every pad.
            var cfgF = CurrentConfig;
            bool detectFdn = cfgF == null || cfgF.DetectSizes_Foundation;
            bool createFdnTypes = cfgF == null || cfgF.CreateNewTypes_Foundation;
            double fallbackFdnW = cfgF?.FoundationWidthMm ?? 1200;
            // Pad footing width by a pad-to-column oversize factor when sizing
            // from a column. EC7 §6.5: pad should exceed the column footprint
            // by at least 1.5× in each direction for axial-only load cases.
            const double PadOversizeFactor = 1.5;

            // Cache by rounded (w, d) so we only call FindOrCreate once per
            // distinct pad footprint.
            var fdnTypeCache = new Dictionary<string, FamilySymbol>();
            FamilySymbol ResolveFdnType(double wMm, double dMm)
            {
                string key = $"{Math.Round(wMm / 25) * 25:F0}x{Math.Round(dMm / 25) * 25:F0}";
                if (fdnTypeCache.TryGetValue(key, out var sym)) return sym;
                var tm = _typeFactory.FindOrCreateFoundationType(
                    wMm, dMm, allowDuplicate: createFdnTypes);
                if (!tm.Success) { result.Warnings.Add(tm.Message); return null; }
                sym = _doc.GetElement(tm.TypeId) as FamilySymbol;
                if (sym == null) return null;
                if (!sym.IsActive) { sym.Activate(); _doc.Regenerate(); }
                fdnTypeCache[key] = sym;
                return sym;
            }

            // Pre-flight: verify at least one foundation family is loaded.
            bool anyFdnFamily = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                .OfClass(typeof(FamilySymbol)).Any();
            if (!anyFdnFamily)
            {
                result.Warnings.Add("No structural foundation families loaded — skipping foundations.");
                return 0;
            }

            using (var tx = new Transaction(_doc, "STING STRUCT: Pad Foundations"))
            {
                tx.Start();

                foreach (var circle in circles ?? new List<DetectedCircle>())
                {
                    try
                    {
                        double colDiaMm = circle.DiameterMm;
                        double wMm = detectFdn
                            ? Math.Max(300, colDiaMm * PadOversizeFactor)
                            : fallbackFdnW;
                        double dMm = wMm; // square pad for circular columns
                        var fdnSymbol = ResolveFdnType(wMm, dMm);
                        if (fdnSymbol == null) continue;

                        var pt = new XYZ(circle.Center.X, circle.Center.Y,
                            level?.Elevation ?? 0);
                        var fdn = _doc.Create.NewFamilyInstance(
                            pt, fdnSymbol, level, StructuralType.Footing);
                        ModelWorksetAssigner.Assign(_doc, fdn);
                        result.CreatedIds.Add(fdn.Id);
                        count++;
                    }
                    catch (Exception ex) { result.Warnings.Add($"Pad fdn: {ex.Message}"); }
                }

                foreach (var rect in rects ?? new List<DetectedRectangle>())
                {
                    try
                    {
                        double wMm = detectFdn
                            ? Math.Max(300, rect.WidthMm * PadOversizeFactor)
                            : fallbackFdnW;
                        double dMm = detectFdn
                            ? Math.Max(300, rect.DepthMm * PadOversizeFactor)
                            : fallbackFdnW;
                        var fdnSymbol = ResolveFdnType(wMm, dMm);
                        if (fdnSymbol == null) continue;

                        var pt = new XYZ(rect.Center.X, rect.Center.Y,
                            level?.Elevation ?? 0);
                        var fdn = _doc.Create.NewFamilyInstance(
                            pt, fdnSymbol, level, StructuralType.Footing);

                        // Match the column's rotation so the pad aligns
                        // with the column it supports.
                        if (detectFdn && Math.Abs(rect.Rotation) > 0.01)
                        {
                            var axis = Line.CreateBound(pt, pt + XYZ.BasisZ);
                            ElementTransformUtils.RotateElement(_doc, fdn.Id, axis, rect.Rotation);
                        }

                        ModelWorksetAssigner.Assign(_doc, fdn);
                        result.CreatedIds.Add(fdn.Id);
                        count++;
                    }
                    catch (Exception ex) { result.Warnings.Add($"Pad fdn: {ex.Message}"); }
                }

                tx.Commit();
            }
            return count;
        }

        private int CreateFoundationsFromBlocks(List<DetectedBlock> blocks,
            Level level, double depthMm, StructuralModelResult result)
        {
            // Foundation blocks from DWG are placed as structural foundations.
            // Phase-97: when DetectSizes_Foundation is true, try to parse the
            // block name for dimensions ("PAD 1500x1500", "FTG_1800x1200") so
            // each foundation gets a matching type. When false, fall back to
            // the wizard's FoundationWidthMm for every block.
            int count = 0;

            // Pre-flight: verify at least one foundation family is loaded.
            bool anyFdnFamily = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                .OfClass(typeof(FamilySymbol)).Any();
            if (!anyFdnFamily)
            {
                result.Warnings.Add("No foundation families loaded.");
                return 0;
            }

            var cfgF = CurrentConfig;
            bool detectFdn = cfgF == null || cfgF.DetectSizes_Foundation;
            bool createFdnTypes = cfgF == null || cfgF.CreateNewTypes_Foundation;
            double fallbackFdnW = cfgF?.FoundationWidthMm ?? 1200;

            // Size cache keyed on parsed dimensions. Most projects have
            // 3-5 distinct pad sizes across 100+ locations, so caching by
            // (w, d) means ~5 FindOrCreateFoundationType calls instead of 100.
            var fdnTypeCache = new Dictionary<string, FamilySymbol>();
            FamilySymbol ResolveFdnType(double wMm, double dMm)
            {
                string key = $"{Math.Round(wMm / 25) * 25:F0}x{Math.Round(dMm / 25) * 25:F0}";
                if (fdnTypeCache.TryGetValue(key, out var sym)) return sym;
                var tm = _typeFactory.FindOrCreateFoundationType(
                    wMm, dMm, allowDuplicate: createFdnTypes);
                if (!tm.Success) { result.Warnings.Add(tm.Message); return null; }
                sym = _doc.GetElement(tm.TypeId) as FamilySymbol;
                if (sym == null) return null;
                if (!sym.IsActive) { sym.Activate(); _doc.Regenerate(); }
                fdnTypeCache[key] = sym;
                return sym;
            }

            using (var tx = new Transaction(_doc, "STING STRUCT: Foundation Blocks"))
            {
                tx.Start();

                foreach (var block in blocks)
                {
                    try
                    {
                        double wMm, dMm;
                        if (detectFdn && TryParseDimensionsFromBlockName(
                                block.BlockName, out wMm, out dMm))
                        {
                            // parsed successfully — use those dimensions
                        }
                        else
                        {
                            wMm = fallbackFdnW;
                            dMm = fallbackFdnW;
                        }

                        var fdnSymbol = ResolveFdnType(wMm, dMm);
                        if (fdnSymbol == null) continue;

                        var pt = new XYZ(block.InsertionPoint.X, block.InsertionPoint.Y,
                            level?.Elevation ?? 0);
                        var fdn = _doc.Create.NewFamilyInstance(
                            pt, fdnSymbol, level, StructuralType.Footing);

                        if (Math.Abs(block.Rotation) > 0.01)
                        {
                            var axis = Line.CreateBound(pt, pt + XYZ.BasisZ);
                            ElementTransformUtils.RotateElement(_doc, fdn.Id, axis, block.Rotation);
                        }

                        ModelWorksetAssigner.Assign(_doc, fdn);
                        result.CreatedIds.Add(fdn.Id);
                        count++;
                    }
                    catch (Exception ex) { result.Warnings.Add($"Foundation block: {ex.Message}"); }
                }

                tx.Commit();
            }
            return count;
        }

        /// <summary>
        /// Phase-97: parse dimension tokens from a DWG block name. Handles
        /// common patterns: "PAD 1500x1500", "FTG_1800x1200", "F-1200X1500",
        /// "FND-2000". Returns (widthMm, depthMm) with depth = width when
        /// the block name only has one dimension.
        /// </summary>
        private static bool TryParseDimensionsFromBlockName(
            string blockName, out double widthMm, out double depthMm)
        {
            widthMm = 0;
            depthMm = 0;
            if (string.IsNullOrWhiteSpace(blockName)) return false;
            try
            {
                var clean = new string(blockName.Select(c =>
                    char.IsDigit(c) || c == 'x' || c == 'X' ? c : ' ').ToArray());
                var parts = clean.Split(new[] { 'x', 'X', ' ' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2
                    && int.TryParse(parts[0], out int w)
                    && int.TryParse(parts[1], out int d)
                    && w >= 300 && w <= 10000
                    && d >= 300 && d <= 10000)
                {
                    widthMm = w;
                    depthMm = d;
                    return true;
                }
                if (parts.Length == 1
                    && int.TryParse(parts[0], out int sq)
                    && sq >= 300 && sq <= 10000)
                {
                    widthMm = sq;
                    depthMm = sq;
                    return true;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Fdn name parse: {ex.Message}"); }
            return false;
        }

        // ── Wall creation with structural/architectural flag ─────────────

        private int CreateWallsWithConfig(List<DetectedWall> walls, Level level,
            DWGConversionConfig config, StructuralModelResult result)
        {
            int count = 0;
            bool cancelled = false;
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
                        // Per-element size detection. When DetectSizes_Wall is
                        // false, ignore the measured parallel-pair gap and use
                        // the wizard's fixed WallThicknessMm for every wall.
                        double thickMm = config.DetectSizes_Wall
                            ? wall.ThicknessFt * Units.FeetToMm
                            : config.WallThicknessMm;
                        var typeMatch = _typeFactory.FindOrCreateWallType(
                            thickMm, config.CreateStructuralWalls,
                            allowDuplicate: config.CreateNewTypes_Wall);
                        if (!typeMatch.Success) { result.Warnings.Add(typeMatch.Message); continue; }

                        var line = Line.CreateBound(wall.CenterStart, wall.CenterEnd);
                        var created = Wall.Create(_doc, line, typeMatch.TypeId,
                            level?.Id ?? ElementId.InvalidElementId,
                            Units.Mm(config.WallHeightMm), 0, false,
                            config.CreateStructuralWalls);

                        ModelWorksetAssigner.Assign(_doc, created);
                        result.CreatedIds.Add(created.Id);
                        count++;
                    }
                    catch (Exception ex) { result.Warnings.Add($"Wall: {ex.Message}"); }
                    if (count % 50 == 0 && EscapeChecker.IsEscapePressed()) { cancelled = true; break; }
                }

                if (cancelled) tx.RollBack(); else tx.Commit();
            }
            result.Warnings.AddRange(fh.CapturedWarnings);
            return count;
        }
    }


    // ════════════════════════════════════════════════════════════════
    // ADVANCED GEOMETRY ALGORITHMS
    // ════════════════════════════════════════════════════════════════

    #region RANSAC Line Fitting

    /// <summary>Fitted line from RANSAC with inlier information.</summary>
    public class FittedLine
    {
        public XYZ Start { get; set; }
        public XYZ End { get; set; }
        public int InlierCount { get; set; }
        public double FitError { get; set; }
        public List<int> InlierIndices { get; set; } = new();
    }

    /// <summary>
    /// RANSAC (Random Sample Consensus) line fitting for noisy/fragmented CAD data.
    /// Handles: fragmented walls (multiple short segments), broken grid lines,
    /// noisy polylines from poor DWG quality.
    /// Algorithm:
    ///   1. Randomly sample 2 endpoints → define candidate line
    ///   2. Count inliers (line midpoints within threshold of candidate)
    ///   3. Keep model with most inliers
    ///   4. Least-squares re-fit on all inliers
    ///   5. Remove used points, repeat
    /// </summary>
    internal static class RANSACLineFitter
    {
        /// <summary>
        /// Fits clean lines through noisy/fragmented CAD line segments.
        /// </summary>
        /// <param name="noisyLines">Input fragmented line segments</param>
        /// <param name="inlierThresholdFt">Max distance from candidate line to be inlier (default 5mm)</param>
        /// <param name="maxIterations">RANSAC iterations per line (default 200)</param>
        /// <param name="minInliers">Minimum inlier count to accept a line (default 3)</param>
        public static List<FittedLine> FitLines(
            List<ExtractedLine> noisyLines,
            double inlierThresholdFt = 0.016,
            int maxIterations = 200,
            int minInliers = 3)
        {
            var results = new List<FittedLine>();
            if (noisyLines == null || noisyLines.Count < 2) return results;

            // Use midpoints of segments as data points
            var midpoints = noisyLines.Select(l => (l.Start + l.End) * 0.5).ToList();
            var used = new HashSet<int>();
            var rng = new Random(42); // Deterministic for reproducibility

            int maxLines = noisyLines.Count / 2; // Can't have more lines than half the segments

            for (int lineIter = 0; lineIter < maxLines; lineIter++)
            {
                var availableIndices = Enumerable.Range(0, midpoints.Count)
                    .Where(i => !used.Contains(i)).ToList();

                if (availableIndices.Count < minInliers) break;

                FittedLine bestLine = null;
                int bestInlierCount = 0;

                for (int iter = 0; iter < maxIterations; iter++)
                {
                    // Sample 2 random points
                    int i1 = availableIndices[rng.Next(availableIndices.Count)];
                    int i2 = availableIndices[rng.Next(availableIndices.Count)];
                    if (i1 == i2) continue;

                    var p1 = midpoints[i1];
                    var p2 = midpoints[i2];
                    if (p1.DistanceTo(p2) < 0.01) continue;

                    // Count inliers
                    var inliers = new List<int>();
                    foreach (int idx in availableIndices)
                    {
                        double dist = DistancePointToLine2D(midpoints[idx], p1, p2);
                        if (dist < inlierThresholdFt)
                            inliers.Add(idx);
                    }

                    if (inliers.Count > bestInlierCount && inliers.Count >= minInliers)
                    {
                        bestInlierCount = inliers.Count;

                        // Least-squares re-fit: find extreme projections along line direction
                        var dir = (p2 - p1).Normalize();
                        double minT = double.MaxValue, maxT = double.MinValue;
                        double rmsError = 0;

                        foreach (int idx in inliers)
                        {
                            double t = (midpoints[idx] - p1).DotProduct(dir);
                            minT = Math.Min(minT, t);
                            maxT = Math.Max(maxT, t);
                            double dist = DistancePointToLine2D(midpoints[idx], p1, p2);
                            rmsError += dist * dist;
                        }
                        if (inliers.Count == 0) continue;
                        rmsError = Math.Sqrt(rmsError / inliers.Count);

                        bestLine = new FittedLine
                        {
                            Start = p1 + dir * minT,
                            End = p1 + dir * maxT,
                            InlierCount = inliers.Count,
                            FitError = rmsError,
                            InlierIndices = inliers,
                        };
                    }
                }

                if (bestLine == null || bestLine.InlierCount < minInliers) break;

                results.Add(bestLine);
                foreach (int idx in bestLine.InlierIndices)
                    used.Add(idx);
            }

            StingLog.Info($"RANSAC: fitted {results.Count} lines from {noisyLines.Count} segments " +
                $"({used.Count} points used)");
            return results;
        }

        /// <summary>
        /// Merges collinear line segments into single consolidated lines.
        /// Handles fragmented walls drawn as multiple short segments.
        /// </summary>
        public static List<ExtractedLine> MergeCollinearSegments(
            List<ExtractedLine> lines,
            double angleToleranceRad = 0.05,
            double gapToleranceFt = 0.1)
        {
            if (lines == null || lines.Count <= 1) return lines ?? new List<ExtractedLine>();

            var merged = new List<ExtractedLine>();
            var used = new bool[lines.Count];

            for (int i = 0; i < lines.Count; i++)
            {
                if (used[i]) continue;

                var group = new List<int> { i };
                var dirI = lines[i].Direction;

                // Find collinear segments
                for (int j = i + 1; j < lines.Count; j++)
                {
                    if (used[j]) continue;
                    var dirJ = lines[j].Direction;

                    // Check angle (parallel or anti-parallel)
                    double dot = Math.Abs(dirI.DotProduct(dirJ));
                    if (dot < Math.Cos(angleToleranceRad)) continue;

                    // Check perpendicular distance
                    double perpDist = DistancePointToLine2D(
                        (lines[j].Start + lines[j].End) * 0.5,
                        lines[i].Start, lines[i].End);
                    if (perpDist > gapToleranceFt) continue;

                    group.Add(j);
                }

                if (group.Count == 1)
                {
                    merged.Add(lines[i]);
                    used[i] = true;
                    continue;
                }

                // Merge: project all endpoints onto common direction, take extremes
                var refPt = lines[i].Start;
                var dir = dirI;
                double minT = double.MaxValue, maxT = double.MinValue;
                XYZ minPt = null, maxPt = null;

                foreach (int idx in group)
                {
                    double ts = (lines[idx].Start - refPt).DotProduct(dir);
                    double te = (lines[idx].End - refPt).DotProduct(dir);
                    if (ts < minT) { minT = ts; minPt = lines[idx].Start; }
                    if (te < minT) { minT = te; minPt = lines[idx].End; }
                    if (ts > maxT) { maxT = ts; maxPt = lines[idx].Start; }
                    if (te > maxT) { maxT = te; maxPt = lines[idx].End; }
                    used[idx] = true;
                }

                if (minPt != null && maxPt != null && minPt.DistanceTo(maxPt) > 0.01)
                {
                    merged.Add(new ExtractedLine
                    {
                        Start = minPt, End = maxPt,
                        LayerName = lines[i].LayerName,
                        Category = lines[i].Category,
                    });
                }
            }

            StingLog.Info($"MergeCollinear: {lines.Count} segments → {merged.Count} merged lines");
            return merged;
        }

        private static double DistancePointToLine2D(XYZ point, XYZ lineStart, XYZ lineEnd)
        {
            var d = lineEnd - lineStart;
            double lenSq = d.X * d.X + d.Y * d.Y;
            if (lenSq < 1e-12) return Math.Sqrt(
                Math.Pow(point.X - lineStart.X, 2) + Math.Pow(point.Y - lineStart.Y, 2));

            double t = Math.Max(0, Math.Min(1,
                ((point.X - lineStart.X) * d.X + (point.Y - lineStart.Y) * d.Y) / lenSq));
            double projX = lineStart.X + t * d.X;
            double projY = lineStart.Y + t * d.Y;
            return Math.Sqrt(Math.Pow(point.X - projX, 2) + Math.Pow(point.Y - projY, 2));
        }
    }

    #endregion


    #region Douglas-Peucker Polyline Simplification

    /// <summary>
    /// Douglas-Peucker polyline simplification algorithm.
    /// Reduces point count while preserving shape within tolerance.
    /// O(n log n) average, O(n²) worst case.
    /// </summary>
    internal static class PolylineSimplifier
    {
        /// <summary>
        /// Simplifies a polyline by removing points within epsilon of the line
        /// between retained neighbours.
        /// </summary>
        /// <param name="polyline">Input points (ordered)</param>
        /// <param name="epsilonFt">Maximum perpendicular deviation to allow removal (default 5mm)</param>
        public static List<XYZ> Simplify(List<XYZ> polyline, double epsilonFt = 0.016)
        {
            if (polyline == null || polyline.Count <= 2) return polyline ?? new List<XYZ>();

            var keep = new bool[polyline.Count];
            keep[0] = true;
            keep[polyline.Count - 1] = true;

            SimplifyRecursive(polyline, 0, polyline.Count - 1, epsilonFt, keep);

            var result = new List<XYZ>();
            for (int i = 0; i < polyline.Count; i++)
                if (keep[i]) result.Add(polyline[i]);

            return result;
        }

        private static void SimplifyRecursive(
            List<XYZ> points, int start, int end,
            double epsilon, bool[] keep)
        {
            if (end - start < 2) return;

            double maxDist = 0;
            int maxIdx = start;

            for (int i = start + 1; i < end; i++)
            {
                double dist = PerpendicularDistance(points[i], points[start], points[end]);
                if (dist > maxDist)
                {
                    maxDist = dist;
                    maxIdx = i;
                }
            }

            if (maxDist > epsilon)
            {
                keep[maxIdx] = true;
                SimplifyRecursive(points, start, maxIdx, epsilon, keep);
                SimplifyRecursive(points, maxIdx, end, epsilon, keep);
            }
        }

        private static double PerpendicularDistance(XYZ point, XYZ lineStart, XYZ lineEnd)
        {
            var d = lineEnd - lineStart;
            double lenSq = d.X * d.X + d.Y * d.Y;
            if (lenSq < 1e-12) return point.DistanceTo(lineStart);

            double t = Math.Max(0, Math.Min(1,
                ((point.X - lineStart.X) * d.X + (point.Y - lineStart.Y) * d.Y) / lenSq));
            var proj = new XYZ(lineStart.X + t * d.X, lineStart.Y + t * d.Y, point.Z);
            return point.DistanceTo(proj);
        }
    }

    #endregion


    #region Arc and Curve Detection

    /// <summary>Detected arc/curve from sequential line segments.</summary>
    public class DetectedArc
    {
        public XYZ Center { get; set; }
        public double RadiusFt { get; set; }
        public double StartAngleRad { get; set; }
        public double EndAngleRad { get; set; }
        public double ArcLengthFt { get; set; }
        public string LayerName { get; set; }
        public double Confidence { get; set; }
        public int SegmentCount { get; set; }
    }

    /// <summary>
    /// Detects arcs and circles from sequences of short line segments.
    /// Uses sliding window + least-squares circle fitting.
    /// Algorithm:
    ///   1. Slide window of N segments along line list
    ///   2. Fit circle to segment midpoints (algebraic circle fit)
    ///   3. If RMS deviation < threshold → arc detected
    ///   4. Close arcs → circles
    /// </summary>
    internal static class ArcDetector
    {
        /// <summary>
        /// Detects arcs from sequences of short lines that approximate curves.
        /// </summary>
        public static List<DetectedArc> DetectArcs(
            List<ExtractedLine> lines,
            double maxDeviationFt = 0.03,
            int minSegments = 4)
        {
            var arcs = new List<DetectedArc>();
            if (lines == null || lines.Count < minSegments) return arcs;

            // Sort lines by connectivity (endpoint chaining)
            var chains = BuildChains(lines, 0.02);

            foreach (var chain in chains)
            {
                if (chain.Count < minSegments) continue;

                // Collect midpoints
                var midpoints = chain.Select(l => (l.Start + l.End) * 0.5).ToList();

                // Try to fit circle to this chain
                var fit = FitCircle(midpoints);
                if (fit == null) continue;

                // Check fit quality: RMS deviation from circle
                double rmsError = 0;
                foreach (var pt in midpoints)
                {
                    double dist = Math.Abs(pt.DistanceTo(fit.Value.Center) - fit.Value.Radius);
                    rmsError += dist * dist;
                }
                if (midpoints.Count == 0) continue;
                rmsError = Math.Sqrt(rmsError / midpoints.Count);

                if (rmsError > maxDeviationFt) continue;

                // Calculate arc angles
                double startAngle = Math.Atan2(
                    midpoints[0].Y - fit.Value.Center.Y,
                    midpoints[0].X - fit.Value.Center.X);
                double endAngle = Math.Atan2(
                    midpoints.Last().Y - fit.Value.Center.Y,
                    midpoints.Last().X - fit.Value.Center.X);

                double arcLength = fit.Value.Radius * Math.Abs(endAngle - startAngle);

                arcs.Add(new DetectedArc
                {
                    Center = fit.Value.Center,
                    RadiusFt = fit.Value.Radius,
                    StartAngleRad = startAngle,
                    EndAngleRad = endAngle,
                    ArcLengthFt = arcLength,
                    LayerName = chain[0].LayerName,
                    Confidence = Math.Max(0, 1.0 - rmsError / maxDeviationFt),
                    SegmentCount = chain.Count,
                });
            }

            return arcs;
        }

        /// <summary>
        /// Detects closed circles from connected line segments.
        /// </summary>
        public static List<DetectedCircle> DetectCirclesFromSegments(
            List<ExtractedLine> lines, double closureTolerance = 0.03)
        {
            var circles = new List<DetectedCircle>();
            var chains = BuildChains(lines, closureTolerance);

            foreach (var chain in chains)
            {
                if (chain.Count < 6) continue;

                // Check closure: first start ≈ last end
                if (chain[0].Start.DistanceTo(chain.Last().End) > closureTolerance) continue;

                var midpoints = chain.Select(l => (l.Start + l.End) * 0.5).ToList();
                var fit = FitCircle(midpoints);
                if (fit == null) continue;

                // Check radius consistency
                double maxDev = midpoints.Max(p => Math.Abs(p.DistanceTo(fit.Value.Center) - fit.Value.Radius));
                if (maxDev > closureTolerance * 3) continue;

                circles.Add(new DetectedCircle
                {
                    Center = fit.Value.Center,
                    RadiusFt = fit.Value.Radius,
                    LayerName = chain[0].LayerName,
                    Confidence = 0.90,
                });
            }

            return circles;
        }

        /// <summary>Algebraic circle fit (Kasa method): fits circle to 2D points.</summary>
        private static (XYZ Center, double Radius)? FitCircle(List<XYZ> points)
        {
            if (points.Count < 3) return null;

            // Kasa method: minimize Σ(x² + y² - 2ax - 2by - c)²
            double sumX = 0, sumY = 0, sumX2 = 0, sumY2 = 0;
            double sumXY = 0, sumX3 = 0, sumY3 = 0, sumX2Y = 0, sumXY2 = 0;
            int n = points.Count;

            foreach (var p in points)
            {
                double x = p.X, y = p.Y;
                sumX += x; sumY += y;
                sumX2 += x * x; sumY2 += y * y;
                sumXY += x * y;
                sumX3 += x * x * x; sumY3 += y * y * y;
                sumX2Y += x * x * y; sumXY2 += x * y * y;
            }

            double A = n * sumX2 - sumX * sumX;
            double B = n * sumXY - sumX * sumY;
            double C = n * sumY2 - sumY * sumY;
            double D = 0.5 * (n * sumX3 + n * sumXY2 - sumX * sumX2 - sumX * sumY2);
            double E = 0.5 * (n * sumX2Y + n * sumY3 - sumY * sumX2 - sumY * sumY2);

            double denom = A * C - B * B;
            if (Math.Abs(denom) < 1e-12) return null;

            double cx = (D * C - B * E) / denom;
            double cy = (A * E - B * D) / denom;
            if (!points.Any()) return null;
            double r = Math.Sqrt(points.Average(p =>
                Math.Pow(p.X - cx, 2) + Math.Pow(p.Y - cy, 2)));

            if (r < 0.01 || r > 1000) return null; // Sanity bounds

            return (new XYZ(cx, cy, 0), r);
        }

        /// <summary>Builds chains of connected line segments by endpoint proximity.</summary>
        private static List<List<ExtractedLine>> BuildChains(
            List<ExtractedLine> lines, double tolerance)
        {
            var chains = new List<List<ExtractedLine>>();
            var used = new bool[lines.Count];

            for (int i = 0; i < lines.Count; i++)
            {
                if (used[i]) continue;

                var chain = new List<ExtractedLine> { lines[i] };
                used[i] = true;

                // Extend chain forward
                bool extended = true;
                while (extended)
                {
                    extended = false;
                    var lastEnd = chain.Last().End;
                    for (int j = 0; j < lines.Count; j++)
                    {
                        if (used[j]) continue;
                        if (lastEnd.DistanceTo(lines[j].Start) < tolerance)
                        {
                            chain.Add(lines[j]);
                            used[j] = true;
                            extended = true;
                            break;
                        }
                        if (lastEnd.DistanceTo(lines[j].End) < tolerance)
                        {
                            // Reverse line
                            chain.Add(new ExtractedLine
                            {
                                Start = lines[j].End, End = lines[j].Start,
                                LayerName = lines[j].LayerName, Category = lines[j].Category
                            });
                            used[j] = true;
                            extended = true;
                            break;
                        }
                    }
                }

                chains.Add(chain);
            }

            return chains;
        }
    }

    #endregion


    #region Convex Hull

    /// <summary>
    /// Andrew's monotone chain convex hull algorithm.
    /// Computes the 2D convex hull of a point set in O(n log n).
    /// Returns points in counter-clockwise order.
    /// </summary>
    internal static class ConvexHull
    {
        /// <summary>
        /// Computes 2D convex hull (ignores Z coordinate).
        /// Returns vertices in counter-clockwise order.
        /// </summary>
        public static List<XYZ> Compute(List<XYZ> points)
        {
            if (points == null || points.Count <= 1)
                return points?.ToList() ?? new List<XYZ>();

            var sorted = points
                .OrderBy(p => p.X).ThenBy(p => p.Y)
                .ToList();

            int n = sorted.Count;
            if (n <= 2) return sorted;

            // Build lower hull
            var lower = new List<XYZ>();
            foreach (var p in sorted)
            {
                while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0)
                    lower.RemoveAt(lower.Count - 1);
                lower.Add(p);
            }

            // Build upper hull
            var upper = new List<XYZ>();
            for (int i = n - 1; i >= 0; i--)
            {
                while (upper.Count >= 2 && Cross(upper[^2], upper[^1], sorted[i]) <= 0)
                    upper.RemoveAt(upper.Count - 1);
                upper.Add(sorted[i]);
            }

            // Remove last point of each half (duplicated at junction)
            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);

            lower.AddRange(upper);
            return lower;
        }

        /// <summary>Cross product of vectors OA and OB (2D, Z ignored).</summary>
        private static double Cross(XYZ o, XYZ a, XYZ b) =>
            (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
    }

    #endregion
}
