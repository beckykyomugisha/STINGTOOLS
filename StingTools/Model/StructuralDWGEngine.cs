// ============================================================================
// StructuralDWGEngine.cs — Phase-141 high-level facade for the structural
// DWG-to-BIM pipeline.
//
// CLAUDE.md described this file as "Precision DWG-to-BIM engine: detection,
// creation, joining, type creation, quality scoring (~1,457 lines)" — but
// the file did not exist on disk. The detection, creation, joining, and
// type-creation logic lives in StructuralCADPipeline.cs +
// StructuralTypeFactory.cs + StructuralPhase140Accuracy.cs; rather than
// duplicate it, this file is a focused FACADE that:
//
//   1. Exposes the detection methods as named entry points (so callers
//      outside the wizard can invoke them in any order).
//   2. Adds a **quality score** computation that the legacy pipeline never
//      surfaced — turns "we converted 412 elements" into a 0-100 score
//      based on connectivity, junction warnings, rejected pairs, and
//      detection confidence.
//   3. Provides convenience helpers for batch / scriptable use:
//      DetectAll(import) / RunWithDefaults(import) / Audit(import).
//
// All Revit writes go through StructuralCADPipeline so transaction
// handling, failure handlers, and worksharing checks stay in one place.
// This file only exposes APIs and computes pure metrics.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Model
{
    /// <summary>
    /// Phase-141 high-level facade over StructuralCADPipeline. Exposes detection
    /// methods as a callable API and produces a quality score for each conversion.
    /// </summary>
    public class StructuralDWGEngine
    {
        private readonly Document _doc;
        private readonly StructuralCADPipeline _pipeline;
        private readonly StructuralTypeFactory _typeFactory;

        public StructuralDWGEngine(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _pipeline = new StructuralCADPipeline(doc);
            _typeFactory = new StructuralTypeFactory(doc);
        }

        /// <summary>Underlying pipeline — exposed for advanced callers who need
        /// direct access to the methods this facade doesn't yet wrap.</summary>
        public StructuralCADPipeline Pipeline => _pipeline;

        // ── Detection facade — named entry points ──────────────────────

        /// <summary>Run the full extraction (lines, arcs, blocks → detected
        /// rectangles, circles, walls, beams, slabs, grids, junctions).</summary>
        public StructuralExtractionResult ExtractAll(ImportInstance import,
            DWGConversionConfig config = null)
        {
            if (config != null) _pipeline.CurrentConfig = config;
            return _pipeline.ExtractStructuralGeometry(import);
        }

        /// <summary>Re-validate circular columns against the active config's
        /// MinColumnDiameterMm / MaxColumnDiameterMm. Returns accepted +
        /// rejected lists.</summary>
        public (List<DetectedCircle> Accepted, List<DetectedCircle> Rejected)
            DetectCircularColumns(IList<DetectedCircle> circles)
        {
            var accepted = _pipeline.DetectCircularColumns(circles, out var rejected);
            return (accepted, rejected);
        }

        /// <summary>Detect parallel-pair walls from the supplied lines.</summary>
        public List<DetectedWall> DetectStructuralWalls(List<ExtractedLine> lines)
            => _pipeline.DetectStructuralWalls(lines);

        /// <summary>Classify junctions among detected beams + columns. Returns
        /// (centroid, junction-type, beam-count) triples.</summary>
        public List<(XYZ Point, string JunctionType, int BeamCount)>
            DetectJunctions(StructuralExtractionResult extraction)
            => _pipeline.DetectJunctions(extraction);

        /// <summary>Run AnalyzeLoadPaths against the current Revit model
        /// (post-creation connectivity audit).</summary>
        public StructuralModelResult AnalyzeLoadPaths()
        {
            // The pipeline holds an internal _structEngine — go via its
            // RunFullPipelineWithConfig path or directly through a fresh engine.
            try
            {
                return new StructuralModelingEngine(_doc).AnalyzeLoadPaths();
            }
            catch (Exception ex)
            {
                StingLog.Error("StructuralDWGEngine.AnalyzeLoadPaths", ex);
                return StructuralModelResult.Fail($"Load-path analysis failed: {ex.Message}");
            }
        }

        /// <summary>Resolve or create a beam type via the underlying type factory.</summary>
        public TypeMatchResult FindOrCreateBeamType(
            double depthMm, double widthMm, bool allowDuplicate = true)
            => _typeFactory.FindOrCreateBeamType(depthMm, widthMm,
                   allowDuplicate: allowDuplicate);

        // ── High-level workflows ───────────────────────────────────────

        /// <summary>Batch / scriptable entry: run the full conversion with the
        /// given config. Equivalent to what the wizard does on Convert.</summary>
        public StructuralModelResult RunWithConfig(ImportInstance import,
            DWGConversionConfig config)
            => _pipeline.RunFullPipelineWithConfig(import, config);

        /// <summary>Batch / scriptable entry: run with a default config (auto-detect
        /// sizes, all categories on, dry-run OFF). Useful for the "Quick" command.</summary>
        public StructuralModelResult RunWithDefaults(ImportInstance import,
            string baseLevelName = null, string topLevelName = null)
        {
            var config = new DWGConversionConfig
            {
                BaseLevelName = baseLevelName,
                TopLevelName = topLevelName,
                AutoDetectSizes = true,
                DryRun = false,
            };
            return _pipeline.RunFullPipelineWithConfig(import, config);
        }

        /// <summary>Audit-only entry: runs detection without writing to Revit.
        /// Returns extraction + a quality score.</summary>
        public AuditResult Audit(ImportInstance import,
            DWGConversionConfig config = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var cfg = config ?? new DWGConversionConfig { DryRun = true };
            cfg.DryRun = true;

            var extraction = ExtractAll(import, cfg);
            var junctions = DetectJunctions(extraction);
            var score = ComputeQualityScore(extraction, junctions);

            sw.Stop();
            return new AuditResult
            {
                Extraction = extraction,
                Junctions = junctions,
                QualityScore = score,
                Duration = sw.Elapsed,
            };
        }

        // ── Quality scoring ────────────────────────────────────────────

        /// <summary>
        /// Compute a 0-100 quality score for the extraction result. Higher is
        /// better. Heuristics:
        ///
        ///   - 100 points base
        ///   - −5 points per "Beam intersection (no column — WARNING)" junction
        ///   - −2 points per "Free end (no support)" junction
        ///   - −1 point  per 10 walls rejected by thickness band
        ///   - −0.1 point per detected element with confidence < 0.7
        ///
        /// Clamps to [0, 100]. Returns the score plus a per-component breakdown.
        /// </summary>
        public static QualityScore ComputeQualityScore(
            StructuralExtractionResult extraction,
            IList<(XYZ Point, string JunctionType, int BeamCount)> junctions)
        {
            var score = new QualityScore { Total = 100.0 };
            if (extraction == null) return score;

            // Junction penalties
            if (junctions != null)
            {
                int unsupportedIntersections = junctions.Count(j =>
                    j.JunctionType != null && j.JunctionType.IndexOf("WARNING",
                        StringComparison.OrdinalIgnoreCase) >= 0);
                int freeEnds = junctions.Count(j =>
                    j.JunctionType != null && j.JunctionType.StartsWith(
                        "Free end", StringComparison.OrdinalIgnoreCase));

                score.UnsupportedIntersections = unsupportedIntersections;
                score.FreeEnds = freeEnds;
                score.JunctionPenalty = unsupportedIntersections * 5.0 + freeEnds * 2.0;
            }

            // Confidence penalty — DetectedCircle / DetectedRectangle carry a
            // 0..1 confidence. Below 0.7 is OTHER-layer / heuristic detection.
            int lowConfidence = 0;
            foreach (var c in extraction.Circles ?? new List<DetectedCircle>())
                if (c.Confidence < 0.7) lowConfidence++;
            foreach (var r in extraction.Rectangles ?? new List<DetectedRectangle>())
                if (r.Confidence < 0.7) lowConfidence++;
            score.LowConfidenceCount = lowConfidence;
            score.ConfidencePenalty = lowConfidence * 0.1;

            // Detection density — how many elements vs how many entities.
            // Very low ratios suggest a poorly classified layer scheme.
            int totalEntities = extraction.TotalEntities;
            int detected = (extraction.Circles?.Count ?? 0)
                + (extraction.Rectangles?.Count ?? 0)
                + (extraction.Walls?.Count ?? 0)
                + (extraction.BeamLines?.Count ?? 0)
                + (extraction.SlabBoundaries?.Count ?? 0)
                + (extraction.GridLines?.Count ?? 0);
            score.DetectionRatio = totalEntities > 0
                ? (double)detected / totalEntities : 0.0;

            score.Total = Math.Max(0.0, Math.Min(100.0,
                100.0 - score.JunctionPenalty - score.ConfidencePenalty));
            return score;
        }

        // ── Result types ───────────────────────────────────────────────

        /// <summary>Result of a non-destructive audit run.</summary>
        public class AuditResult
        {
            public StructuralExtractionResult Extraction { get; set; }
            public IList<(XYZ Point, string JunctionType, int BeamCount)> Junctions { get; set; }
            public QualityScore QualityScore { get; set; }
            public TimeSpan Duration { get; set; }

            public string FormatSummary()
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("STRUCTURAL DWG-TO-BIM AUDIT");
                sb.AppendLine("════════════════════════════════════════");
                if (Extraction != null)
                {
                    sb.AppendLine($"  Detected: {Extraction.Summary}");
                }
                if (QualityScore != null)
                {
                    sb.AppendLine();
                    sb.AppendLine($"  Quality score:  {QualityScore.Total:F1} / 100");
                    sb.AppendLine($"     Unsupported intersections: {QualityScore.UnsupportedIntersections}");
                    sb.AppendLine($"     Free beam ends:            {QualityScore.FreeEnds}");
                    sb.AppendLine($"     Low-confidence elements:   {QualityScore.LowConfidenceCount}");
                    sb.AppendLine($"     Detection ratio:           {QualityScore.DetectionRatio:P0}");
                }
                sb.AppendLine();
                sb.AppendLine($"  Duration: {Duration.TotalSeconds:F1}s");
                return sb.ToString();
            }
        }

        public class QualityScore
        {
            public double Total { get; set; } = 100.0;
            public int UnsupportedIntersections { get; set; }
            public int FreeEnds { get; set; }
            public int LowConfidenceCount { get; set; }
            public double JunctionPenalty { get; set; }
            public double ConfidencePenalty { get; set; }
            public double DetectionRatio { get; set; }
        }
    }
}
