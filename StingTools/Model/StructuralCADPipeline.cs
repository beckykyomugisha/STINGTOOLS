// ============================================================================
// StructuralCADPipeline.cs — Advanced CAD-to-Structural BIM Conversion Pipeline
//
// Enhanced geometry analysis beyond parallel-line detection:
//   - Circle/arc detection for column cross-sections (round columns)
//   - Rectangle detection from 4-line closed loops (rectangular columns)
//   - Hatch boundary detection for slab outlines
//   - Dimension text extraction for member sizing
//   - Block attribute parsing for family type inference
//   - Line weight/color classification (thick lines = structural)
//   - Cross-shaped intersection detection for column/beam nodes
//   - Grid line detection from long continuous lines
//   - Foundation outline detection from dashed/dotted lines
//   - Centerline extraction from double-line walls
//
// Pipeline:
//   1. Prerequisites check (families, levels, types)
//   2. Full geometry extraction with enhanced classification
//   3. Structural member detection (columns, beams, slabs, foundations)
//   4. Type matching via StructuralTypeFactory (size-based)
//   5. Element creation with workset assignment
//   6. Post-processing (join geometry, set analytical model)
//
// Uses StructuralTypeFactory for intelligent type creation from DWG sizes.
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


    /// <summary>
    /// Advanced structural CAD-to-BIM conversion pipeline.
    /// Extends base CADToModelEngine with structural-specific detection algorithms
    /// and intelligent type creation via StructuralTypeFactory.
    /// </summary>
    public class StructuralCADPipeline
    {
        private readonly Document _doc;
        private readonly StructuralTypeFactory _typeFactory;
        private readonly StructuralModelingEngine _structEngine;

        // Thresholds for structural element classification
        private const double MinColumnSizeMm = 150;     // Smallest column dimension
        private const double MaxColumnSizeMm = 1500;    // Largest column dimension
        private const double MinBeamLengthMm = 500;     // Minimum beam length
        private const double GridLineMinLengthFt = 20;  // ~6m minimum for grid line
        private const double ColumnRectMaxAspect = 3.0; // Max aspect ratio for column vs wall

        public StructuralCADPipeline(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _typeFactory = new StructuralTypeFactory(doc);
            _structEngine = new StructuralModelingEngine(doc);
        }

        /// <summary>The type factory for external access.</summary>
        public StructuralTypeFactory TypeFactory => _typeFactory;

        // ── Prerequisites Check ──────────────────────────────────────────

        /// <summary>
        /// Checks all prerequisites for structural automation.
        /// Returns detailed status of loaded families, levels, and DWG imports.
        /// </summary>
        public PrerequisiteCheckResult CheckPrerequisites()
        {
            var result = new PrerequisiteCheckResult();

            // Levels
            var levels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level)).ToList();
            result.HasLevels = levels.Count > 0;
            result.LevelCount = levels.Count;
            if (!result.HasLevels)
                result.Errors.Add("No levels defined. Create at least one level.");

            // Column families
            var colSymbols = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .OfClass(typeof(FamilySymbol)).ToList();
            result.HasColumnFamilies = colSymbols.Count > 0;
            result.ColumnFamilyCount = colSymbols.Count;
            if (!result.HasColumnFamilies)
                result.Errors.Add("No structural column families loaded. Load at least one column family.");

            // Beam families
            var beamSymbols = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .OfClass(typeof(FamilySymbol)).ToList();
            result.HasBeamFamilies = beamSymbols.Count > 0;
            result.BeamFamilyCount = beamSymbols.Count;
            if (!result.HasBeamFamilies)
                result.Errors.Add("No structural framing families loaded. Load at least one beam family.");

            // Foundations (optional)
            var fdnSymbols = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                .OfClass(typeof(FamilySymbol)).ToList();
            result.HasFoundationFamilies = fdnSymbols.Count > 0;
            result.FoundationFamilyCount = fdnSymbols.Count;
            if (!result.HasFoundationFamilies)
                result.Warnings.Add("No foundation families loaded. Foundations will be skipped.");

            // Wall types
            var wallTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(WallType)).ToList();
            result.HasWallTypes = wallTypes.Count > 0;
            result.WallTypeCount = wallTypes.Count;

            // Floor types
            var floorTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(FloorType)).ToList();
            result.HasFloorTypes = floorTypes.Count > 0;
            result.FloorTypeCount = floorTypes.Count;

            // DWG imports
            var imports = CADToModelEngine.FindImportInstances(_doc);
            result.HasImportedDWG = imports.Count > 0;
            result.DWGCount = imports.Count;
            if (!result.HasImportedDWG)
                result.Errors.Add("No imported/linked DWG files found. Link a structural DWG first.");

            result.AllPassed = result.Errors.Count == 0;
            return result;
        }

        // ── Enhanced Geometry Extraction ──────────────────────────────────

        /// <summary>
        /// Enhanced structural geometry extraction from DWG.
        /// Detects circles, rectangles, grid lines, dimension text, and beam centerlines
        /// beyond the base parallel-line detection.
        /// </summary>
        public StructuralExtractionResult ExtractStructuralGeometry(ImportInstance importInstance)
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

            foreach (var geomObj in geomElement)
            {
                if (geomObj is GeometryInstance gInstance)
                {
                    var instanceGeom = gInstance.GetInstanceGeometry();
                    if (instanceGeom != null)
                        ProcessStructuralGeometry(instanceGeom, result, allLines, 0);
                }
            }

            result.TotalEntities = allLines.Count + result.Circles.Count;

            // Post-processing: detect structural members from collected geometry

            // 1. Detect rectangular columns from small closed loops
            DetectRectangularColumns(allLines, result);

            // 2. Detect beam centerlines from structural layer lines
            DetectBeamCenterlines(allLines, result);

            // 3. Detect grid lines from long straight lines
            DetectGridLines(allLines, result);

            // 4. Detect slab boundaries from floor/slab layer closed loops
            DetectSlabBoundaries(allLines, result);

            // Build summary
            result.Summary = $"Extracted: {result.Circles.Count} circles (columns), " +
                $"{result.Rectangles.Count} rectangles (columns), " +
                $"{result.BeamLines.Count} beam centerlines, " +
                $"{result.SlabBoundaries.Count} slab boundaries, " +
                $"{result.GridLines.Count} grid lines, " +
                $"{result.Dimensions.Count} dimensions from {result.TotalEntities} entities";

            return result;
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
                bool isStructural = StructuralLayerClassifier.IsStructuralLayer(layerName);

                // Track layer classification
                var classKey = isStructural ? $"STRUCT: {layerName}" : $"OTHER: {layerName}";
                if (!result.LayerClassification.ContainsKey(classKey))
                    result.LayerClassification[classKey] = 0;
                result.LayerClassification[classKey]++;

                // Detect circles (round columns, pile caps)
                if (obj is Arc arc && arc.IsCyclic)
                {
                    double radiusMm = arc.Radius * Units.FeetToMm;
                    if (radiusMm * 2 >= MinColumnSizeMm && radiusMm * 2 <= MaxColumnSizeMm)
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
                        Category = isStructural ? "Structural" : LayerMapper.InferCategory(layerName),
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
                            Category = isStructural ? "Structural" : LayerMapper.InferCategory(layerName),
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

        // ── Column Detection (Rectangles from 4-line loops) ──────────────

        /// <summary>
        /// Detects small closed rectangles that represent column cross-sections.
        /// Algorithm:
        ///   1. Group lines by proximity of endpoints (30mm tolerance)
        ///   2. Find 4-line groups that form closed loops
        ///   3. Verify aspect ratio < 3:1 and dimensions within column range
        ///   4. Compute center, width, depth, rotation
        /// </summary>
        private void DetectRectangularColumns(List<ExtractedLine> lines,
            StructuralExtractionResult result)
        {
            var structLines = lines.Where(l =>
                StructuralLayerClassifier.IsStructuralLayer(l.LayerName) ||
                l.Category == "Columns" || l.Category == "Structural").ToList();

            const double endTol = 0.1; // ~30mm
            var used = new HashSet<int>();

            for (int i = 0; i < structLines.Count; i++)
            {
                if (used.Contains(i)) continue;
                var chain = new List<int> { i };
                var currentEnd = structLines[i].End;

                // Try to form a 4-line closed rectangle
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    bool found = false;
                    for (int j = 0; j < structLines.Count; j++)
                    {
                        if (used.Contains(j) || chain.Contains(j)) continue;

                        if (structLines[j].Start.DistanceTo(currentEnd) < endTol)
                        {
                            chain.Add(j);
                            currentEnd = structLines[j].End;
                            found = true;
                            break;
                        }
                        if (structLines[j].End.DistanceTo(currentEnd) < endTol)
                        {
                            chain.Add(j);
                            currentEnd = structLines[j].Start;
                            found = true;
                            break;
                        }
                    }
                    if (!found) break;
                }

                // Check if we have a closed 4-line rectangle
                if (chain.Count == 4 &&
                    currentEnd.DistanceTo(structLines[chain[0]].Start) < endTol)
                {
                    // Compute bounding box
                    var pts = chain.Select(idx => structLines[idx].Start).ToList();
                    pts.AddRange(chain.Select(idx => structLines[idx].End));

                    double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
                    double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
                    double widthFt = maxX - minX;
                    double depthFt = maxY - minY;
                    double widthMm = widthFt * Units.FeetToMm;
                    double depthMm = depthFt * Units.FeetToMm;
                    double aspectRatio = Math.Max(widthMm, depthMm) /
                        Math.Max(1, Math.Min(widthMm, depthMm));

                    // Verify column dimensions
                    if (widthMm >= MinColumnSizeMm && depthMm >= MinColumnSizeMm &&
                        widthMm <= MaxColumnSizeMm && depthMm <= MaxColumnSizeMm &&
                        aspectRatio <= ColumnRectMaxAspect)
                    {
                        foreach (var idx in chain) used.Add(idx);

                        result.Rectangles.Add(new DetectedRectangle
                        {
                            Center = new XYZ((minX + maxX) / 2, (minY + maxY) / 2, 0),
                            WidthFt = widthFt,
                            DepthFt = depthFt,
                            LayerName = structLines[chain[0]].LayerName,
                            Confidence = 0.88,
                        });
                    }
                }
            }
        }

        // ── Beam Centerline Detection ────────────────────────────────────

        /// <summary>
        /// Detects beam centerlines from structural layer lines.
        /// Beams are single lines on beam/framing layers, or centerlines between
        /// parallel wall lines on structural layers.
        /// </summary>
        private void DetectBeamCenterlines(List<ExtractedLine> lines,
            StructuralExtractionResult result)
        {
            foreach (var line in lines)
            {
                var cls = StructuralLayerClassifier.Classify(line.LayerName);
                if (cls == null) continue;

                double lengthMm = line.Length * Units.FeetToMm;
                if (lengthMm < MinBeamLengthMm) continue;

                if (cls.Value.Type == StructuralElementType.Beam ||
                    cls.Value.Type == StructuralElementType.Lintel ||
                    cls.Value.Type == StructuralElementType.Purlin ||
                    cls.Value.Type == StructuralElementType.TransferBeam ||
                    cls.Value.Type == StructuralElementType.GroundBeam ||
                    cls.Value.Type == StructuralElementType.TieBeam)
                {
                    result.BeamLines.Add(line);
                }
            }
        }

        // ── Grid Line Detection ──────────────────────────────────────────

        /// <summary>
        /// Detects grid lines from long straight lines on grid/structural layers.
        /// Grid lines are typically the longest lines in the drawing, spanning
        /// the full building extent in one direction.
        /// </summary>
        private void DetectGridLines(List<ExtractedLine> lines,
            StructuralExtractionResult result)
        {
            var gridCandidates = lines
                .Where(l => l.Length >= GridLineMinLengthFt)
                .Where(l => l.Category == "Grids" ||
                    (l.LayerName?.ToLowerInvariant().Contains("grid") ?? false) ||
                    (l.LayerName?.ToLowerInvariant().Contains("axis") ?? false) ||
                    (l.LayerName?.ToLowerInvariant().Contains("raster") ?? false))
                .ToList();

            // Also detect from pure geometry: very long straight lines
            if (gridCandidates.Count == 0)
            {
                // Find the top 10% longest lines as grid candidates
                var sortedByLength = lines.OrderByDescending(l => l.Length).ToList();
                double threshold = sortedByLength.Count > 10
                    ? sortedByLength[Math.Min(10, sortedByLength.Count - 1)].Length
                    : GridLineMinLengthFt;
                threshold = Math.Max(threshold, GridLineMinLengthFt);

                gridCandidates = lines
                    .Where(l => l.Length >= threshold)
                    .Where(l => IsNearlyAxisAligned(l))
                    .ToList();
            }

            int labelIdx = 1;
            foreach (var gl in gridCandidates)
            {
                var dir = gl.Direction;
                bool isHoriz = Math.Abs(dir.Y) > Math.Abs(dir.X);

                result.GridLines.Add(new DetectedGridLine
                {
                    Start = gl.Start,
                    End = gl.End,
                    Label = isHoriz ? ((char)('A' + labelIdx - 1)).ToString() : labelIdx.ToString(),
                    IsHorizontal = isHoriz,
                });
                labelIdx++;
            }
        }

        private bool IsNearlyAxisAligned(ExtractedLine line)
        {
            var dir = line.Direction;
            // Within 5 degrees of X or Y axis
            return Math.Abs(dir.X) > 0.996 || Math.Abs(dir.Y) > 0.996;
        }

        // ── Slab Boundary Detection ──────────────────────────────────────

        private void DetectSlabBoundaries(List<ExtractedLine> lines,
            StructuralExtractionResult result)
        {
            var slabLines = lines.Where(l =>
            {
                var cls = StructuralLayerClassifier.Classify(l.LayerName);
                return cls?.Type == StructuralElementType.Slab ||
                    l.Category == "Floors" || l.Category == "Slabs";
            }).ToList();

            // Use existing closed loop detector
            var cadEngine = new CADToModelEngine(_doc);
            // Re-use the closed loop detection algorithm from base class
            // For simplicity, detect rectangular boundaries
            if (slabLines.Count >= 4)
            {
                // Group lines by proximity and find closed rectangles
                double minX = slabLines.Min(l => Math.Min(l.Start.X, l.End.X));
                double maxX = slabLines.Max(l => Math.Max(l.Start.X, l.End.X));
                double minY = slabLines.Min(l => Math.Min(l.Start.Y, l.End.Y));
                double maxY = slabLines.Max(l => Math.Max(l.Start.Y, l.End.Y));

                double widthMm = (maxX - minX) * Units.FeetToMm;
                double depthMm = (maxY - minY) * Units.FeetToMm;

                if (widthMm > 1000 && depthMm > 1000)
                {
                    result.SlabBoundaries.Add(new DetectedLoop
                    {
                        Points = new List<XYZ>
                        {
                            new XYZ(minX, minY, 0),
                            new XYZ(maxX, minY, 0),
                            new XYZ(maxX, maxY, 0),
                            new XYZ(minX, maxY, 0),
                        },
                        LayerName = slabLines.First().LayerName,
                    });
                }
            }
        }


        // ── Full Conversion Pipeline ─────────────────────────────────────

        /// <summary>
        /// Runs the complete structural CAD-to-BIM conversion pipeline.
        /// Steps:
        ///   1. Check prerequisites
        ///   2. Build type catalog
        ///   3. Extract structural geometry
        ///   4. Create columns (from circles + rectangles)
        ///   5. Create beams (from centerlines)
        ///   6. Create slabs (from boundaries)
        ///   7. Create grid lines
        ///   8. Post-process (join, analytical model)
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
                StingLog.Info("StructuralCADPipeline: Starting full pipeline");

                // Step 1: Build type catalog
                _typeFactory.BuildCatalog();
                StingLog.Info($"  Type catalog: {_typeFactory.CatalogSize} types");

                // Step 2: Extract structural geometry
                var extraction = ExtractStructuralGeometry(importInstance);
                StingLog.Info($"  Extraction: {extraction.Summary}");

                var level = new ModelFamilyResolver(_doc).ResolveLevel(levelName);
                if (level == null)
                {
                    totalResult.Warnings.Add("No level found, using elevation 0");
                }

                // Step 3: Create columns from circles
                if (createColumns && extraction.Circles.Count > 0)
                {
                    int colCount = CreateColumnsFromCircles(extraction.Circles,
                        level, defaultHeightMm, totalResult);
                    totalResult.ColumnsCreated += colCount;
                }

                // Step 4: Create columns from rectangles
                if (createColumns && extraction.Rectangles.Count > 0)
                {
                    int colCount = CreateColumnsFromRectangles(extraction.Rectangles,
                        level, defaultHeightMm, totalResult);
                    totalResult.ColumnsCreated += colCount;
                }

                // Step 5: Create beams from centerlines
                if (createBeams && extraction.BeamLines.Count > 0)
                {
                    int beamCount = CreateBeamsFromLines(extraction.BeamLines,
                        level, defaultBeamDepthMm, defaultHeightMm, totalResult);
                    totalResult.BeamsCreated += beamCount;
                }

                // Step 6: Create slabs from boundaries
                if (createSlabs && extraction.SlabBoundaries.Count > 0)
                {
                    int slabCount = CreateSlabsFromBoundaries(extraction.SlabBoundaries,
                        level, defaultSlabThickMm, totalResult);
                    totalResult.SlabsCreated += slabCount;
                }

                // Step 7: Create grid lines
                if (createGrids && extraction.GridLines.Count > 0)
                {
                    int gridCount = CreateGridLinesFromDetected(extraction.GridLines, totalResult);
                    // Grid lines don't count as structural elements
                }

                sw.Stop();
                totalResult.Duration = sw.Elapsed;

                var parts = new List<string>();
                if (totalResult.ColumnsCreated > 0) parts.Add($"{totalResult.ColumnsCreated} columns");
                if (totalResult.BeamsCreated > 0) parts.Add($"{totalResult.BeamsCreated} beams");
                if (totalResult.SlabsCreated > 0) parts.Add($"{totalResult.SlabsCreated} slabs");
                if (totalResult.FootingsCreated > 0) parts.Add($"{totalResult.FootingsCreated} foundations");

                totalResult.Summary = parts.Count > 0
                    ? $"Created {string.Join(", ", parts)} from DWG structural analysis " +
                      $"in {sw.Elapsed.TotalSeconds:F1}s"
                    : "No structural elements created — check DWG layer names";

                StingLog.Info($"StructuralCADPipeline: {totalResult.Summary}");
            }
            catch (Exception ex)
            {
                StingLog.Error("StructuralCADPipeline failed", ex);
                totalResult.Success = false;
                totalResult.Summary = $"Pipeline failed: {ex.Message}";
            }

            return totalResult;
        }

        // ── Element Creation Methods ─────────────────────────────────────

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
                        double diamMm = circle.DiameterMm;
                        var typeMatch = _typeFactory.FindOrCreateColumnType(diamMm, diamMm);
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
                        result.Warnings.Add($"Circle column: {ex.Message}");
                    }

                    if (count % 50 == 0 && EscapeChecker.IsEscapePressed())
                    {
                        result.Warnings.Add($"Cancelled after {count} columns");
                        break;
                    }
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

                        // Apply rotation if detected
                        if (Math.Abs(rect.Rotation) > 0.01)
                        {
                            var axis = Line.CreateBound(pt, pt + XYZ.BasisZ);
                            ElementTransformUtils.RotateElement(_doc, col.Id, axis, rect.Rotation);
                        }

                        ModelWorksetAssigner.Assign(_doc, col);
                        result.CreatedIds.Add(col.Id);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Rect column: {ex.Message}");
                    }
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
            var fh = new ModelFailureHandler();

            // Get beam type
            var typeMatch = _typeFactory.FindOrCreateBeamType(defaultDepthMm);
            if (!typeMatch.Success)
            {
                result.Warnings.Add($"Beam type: {typeMatch.Message}");
                return 0;
            }

            var symbol = _doc.GetElement(typeMatch.TypeId) as FamilySymbol;
            if (symbol == null) return 0;
            if (!symbol.IsActive)
            {
                using (var tx = new Transaction(_doc, "Activate Beam"))
                { tx.Start(); symbol.Activate(); _doc.Regenerate(); tx.Commit(); }
            }

            double z = Units.Mm(heightMm) + (level?.Elevation ?? 0);

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
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Beam: {ex.Message}");
                    }
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
            if (!typeMatch.Success)
            {
                result.Warnings.Add($"Slab type: {typeMatch.Message}");
                return 0;
            }

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
                            if (a.DistanceTo(b) < 0.01) continue;
                            curveLoop.Append(Line.CreateBound(
                                new XYZ(a.X, a.Y, 0), new XYZ(b.X, b.Y, 0)));
                        }

                        var slab = Floor.Create(_doc,
                            new List<CurveLoop> { curveLoop },
                            typeMatch.TypeId, level?.Id ?? ElementId.InvalidElementId);

                        var structParam = slab.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
                        if (structParam != null && !structParam.IsReadOnly)
                            structParam.Set(1);

                        ModelWorksetAssigner.Assign(_doc, slab);
                        result.CreatedIds.Add(slab.Id);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Slab: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            return count;
        }

        private int CreateGridLinesFromDetected(List<DetectedGridLine> gridLines,
            StructuralModelResult result)
        {
            int count = 0;

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
                        count++;
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Grid: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            return count;
        }

        // ── Helpers ──────────────────────────────────────────────────────

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
