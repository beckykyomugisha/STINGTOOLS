// ============================================================================
// MepDetectionEngine.cs — Phase: MEP-from-DWG V1.
//
// The MEP discipline pipeline that sits ABOVE the shared geometry-extraction
// core. It does NOT re-extract geometry: it calls the existing
// CADToModelEngine.PreviewImport (→ ExtractGeometry) and classifies the
// extracted DWG block references against the STING_DWG_FIXTURE_MAP. The result
// is a read-only plan — what would place as which fixture, and what would be
// skipped — consumed by Mep_CadPreview (audit) and MepFixtureBuilder (placement).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.Placement;
using StingTools.Model;   // CADToModelEngine, DetectedBlock, CADExtractionResult

namespace StingTools.Core.Cad.Mep
{
    /// <summary>One DWG block classified as an MEP fixture by the map.</summary>
    public class MepDetectedFixture
    {
        public DetectedBlock Block { get; set; }
        public MepFixtureRule Rule { get; set; }
        public double MountingHeightMm { get; set; }
        public string Category => Rule?.Category ?? "";
        public string BlockName => Block?.BlockName ?? "";
    }

    /// <summary>Read-only outcome of an MEP detection pass over a DWG import.</summary>
    public class MepDetectionResult
    {
        public List<MepDetectedFixture> Fixtures { get; } = new List<MepDetectedFixture>();
        /// <summary>Block names that matched no fixture rule, with occurrence counts.</summary>
        public Dictionary<string, int> UnmatchedBlockCounts { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> LayerCounts { get; set; } = new Dictionary<string, int>();
        public int TotalEntities { get; set; }
        public int TotalBlocks { get; set; }

        /// <summary>Detected fixtures grouped by Revit category, count descending.</summary>
        public IEnumerable<IGrouping<string, MepDetectedFixture>> ByCategory()
            => Fixtures.GroupBy(f => f.Category).OrderByDescending(g => g.Count());
    }

    public class MepDetectionEngine
    {
        private readonly Document _doc;
        public MepDetectionEngine(Document doc) => _doc = doc ?? throw new ArgumentNullException(nameof(doc));

        /// <summary>Run the read-only detection pass. Reuses the shared extraction core.</summary>
        public MepDetectionResult Detect(ImportInstance import)
        {
            var result = new MepDetectionResult();
            if (import == null) return result;

            CADExtractionResult extraction;
            try { extraction = new CADToModelEngine(_doc).PreviewImport(import); }
            catch (Exception ex) { StingLog.Error("MepDetectionEngine.PreviewImport", ex); return result; }
            if (extraction == null) return result;

            result.LayerCounts = extraction.LayerCounts ?? new Dictionary<string, int>();
            result.TotalEntities = extraction.TotalEntities;
            result.TotalBlocks = extraction.Blocks?.Count ?? 0;

            var lib = MepFixtureMap.Get(_doc);
            foreach (var block in extraction.Blocks ?? new List<DetectedBlock>())
            {
                if (block == null || string.IsNullOrEmpty(block.BlockName)) continue;
                var rule = lib?.Match(block.BlockName);
                if (rule == null)
                {
                    result.UnmatchedBlockCounts.TryGetValue(block.BlockName, out int n);
                    result.UnmatchedBlockCounts[block.BlockName] = n + 1;
                    continue;
                }
                result.Fixtures.Add(new MepDetectedFixture
                {
                    Block = block,
                    Rule = rule,
                    MountingHeightMm = ResolveHeightMm(rule),
                });
            }
            return result;
        }

        /// <summary>Mounting height (mm above the level): preferred height from
        /// STING_HEIGHT_STANDARDS.json when the rule names a source, else the
        /// rule's explicit MountingHeightMm.</summary>
        public static double ResolveHeightMm(MepFixtureRule rule)
        {
            if (rule == null) return 0;
            if (!string.IsNullOrEmpty(rule.MountingHeightSource))
            {
                var e = HeightStandardsTable.Get(rule.MountingHeightSource);
                if (e != null)
                {
                    if (e.PreferredMm > 0) return e.PreferredMm;
                    if (e.MinMm > 0 && e.MaxMm > 0) return (e.MinMm + e.MaxMm) / 2.0;
                    if (e.MinMm > 0) return e.MinMm;
                }
            }
            return rule.MountingHeightMm;
        }
    }
}
