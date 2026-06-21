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
using System.Text.RegularExpressions;
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
        /// <summary>P2.1 — discipline inferred from the block's DWG layer (E/M/P/FP), or "".</summary>
        public string LayerDiscipline { get; set; } = "";
        /// <summary>P2.1 — discipline the matched rule's category implies.</summary>
        public string RuleDiscipline { get; set; } = "";
        /// <summary>P2.1 — true when the layer discipline is known and disagrees with the
        /// rule's discipline (a low-confidence, block-name-only match to confirm).</summary>
        public bool LayerMismatch { get; set; }
    }

    /// <summary>P2.1 — discipline corroboration: does the block's layer agree with the
    /// discipline its matched fixture category implies? Reduces false positives from the
    /// short 2-letter block-name tokens.</summary>
    public static class MepFixtureDiscipline
    {
        // Block-name fixture category → expected discipline.
        public static string OfCategory(string revitCategory)
        {
            switch (revitCategory)
            {
                case "Air Terminals":
                case "Mechanical Equipment":   return "M";
                case "Plumbing Fixtures":      return "P";
                case "Sprinklers":
                case "Fire Alarm Devices":     return "FP";
                case "Electrical Fixtures":
                case "Electrical Equipment":
                case "Lighting Fixtures":
                case "Data Devices":
                case "Communication Devices":
                case "Security Devices":
                case "Nurse Call Devices":     return "E";
                default: return "";
            }
        }

        // LayerMapper category (Electrical/Ducts/Pipes/Plumbing/Fire Protection) → discipline.
        public static string OfLayerCategory(string layerCategory)
        {
            switch (layerCategory)
            {
                case "Ducts":           return "M";
                case "Pipes":
                case "Plumbing":        return "P";
                case "Fire Protection": return "FP";
                case "Electrical":      return "E";
                default: return "";   // arch/struct/unknown → no opinion
            }
        }

        /// <summary>Compatible disciplines (exact, plus E↔FP since fire-alarm / safety
        /// devices commonly share electrical layers).</summary>
        public static bool Compatible(string layerDisc, string ruleDisc)
        {
            if (string.IsNullOrEmpty(layerDisc) || string.IsNullOrEmpty(ruleDisc)) return true;
            if (layerDisc == ruleDisc) return true;
            return (layerDisc == "E" && ruleDisc == "FP") || (layerDisc == "FP" && ruleDisc == "E");
        }
    }

    /// <summary>Read-only outcome of an MEP detection pass over a DWG import.</summary>
    public class MepDetectionResult
    {
        public List<MepDetectedFixture> Fixtures { get; } = new List<MepDetectedFixture>();
        /// <summary>V2 — ExtractedLines classified as straight runs (Duct/Pipe/Conduit/Tray).</summary>
        public List<MepRunCandidate> Runs { get; } = new List<MepRunCandidate>();
        /// <summary>V3 — riser blocks (UP/DN/RISER) → vertical run segments.</summary>
        public List<MepRiserCandidate> Risers { get; } = new List<MepRiserCandidate>();
        public int DrainageRunCount => Runs.Count(r => r.Drainage);
        /// <summary>P2.1 — fixtures whose block-name match disagrees with the layer discipline.</summary>
        public int LayerMismatchCount => Fixtures.Count(f => f.LayerMismatch);
        /// <summary>P6-1.1 — runs whose system will fall back silently (no service keyword).</summary>
        public int DuctServiceDefaulted => Runs.Count(r => r.Kind == MepRunKind.Duct && r.ServiceDefaulted);
        public int PipeServiceDefaulted => Runs.Count(r => r.Kind == MepRunKind.Pipe && r.ServiceDefaulted);
        /// <summary>P6-1.3 — pipe/conduit runs whose W×H layer size was coerced to a diameter.</summary>
        public int RectCoercedRunCount => Runs.Count(r => r.Size?.RectCoerced == true);
        /// <summary>P6-1.3 — fixtures placed from mirrored (negative-scale) blocks (rotation unreliable).</summary>
        public int MirroredFixtureCount => Fixtures.Count(f => f.Block?.Mirrored == true);
        /// <summary>Block names that matched no fixture rule, with occurrence counts.</summary>
        public Dictionary<string, int> UnmatchedBlockCounts { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> LayerCounts { get; set; } = new Dictionary<string, int>();
        public int TotalEntities { get; set; }
        public int TotalBlocks { get; set; }
        public int TotalLines { get; set; }

        /// <summary>Detected fixtures grouped by Revit category, count descending.</summary>
        public IEnumerable<IGrouping<string, MepDetectedFixture>> ByCategory()
            => Fixtures.GroupBy(f => f.Category).OrderByDescending(g => g.Count());

        /// <summary>Run candidates grouped by kind: count + total length (m), count descending.</summary>
        public IEnumerable<(MepRunKind Kind, int Count, double TotalM)> RunsByKind()
            => Runs.GroupBy(r => r.Kind)
                   .Select(g => (g.Key, g.Count(), g.Sum(r => StingTools.Model.Units.ToMm(r.LengthFt) / 1000.0)))
                   .OrderByDescending(t => t.Item2);

        /// <summary>P1.1 — runs grouped by parsed service classification (preview).</summary>
        public IEnumerable<(string Service, int Count)> RunsByService()
            => Runs.GroupBy(r => r.Classification.ToString())
                   .Select(g => (g.Key, g.Count()))
                   .OrderByDescending(t => t.Item2);
    }

    public class MepDetectionEngine
    {
        private static readonly Regex RiserRx = new Regex(@"riser|\b(up|dn|down)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
            result.TotalLines = extraction.Lines?.Count ?? 0;

            // V2 — classify extracted lines on MEP layers into straight-run candidates.
            // Length floor filters short fixture-internal lines; run-kind requires an
            // explicit run keyword (or Ducts/Pipes category) so power-layer symbol
            // lines are not misread as conduit.
            foreach (var line in extraction.Lines ?? new List<ExtractedLine>())
            {
                if (line == null) continue;
                if (StingTools.Model.Units.ToMm(line.Length) < MepRunClassifier.MinRunLengthMm) continue;
                var kind = MepRunClassifier.DetectKind(line.LayerName, line.Category);
                if (kind == null) continue;
                bool drainage = kind == MepRunKind.Pipe && MepRunClassifier.IsDrainage(line.LayerName);
                var cls = MepServiceClassifier.Classify(line.LayerName, kind.Value, out bool svcDefaulted);
                result.Runs.Add(new MepRunCandidate
                {
                    Line = line,
                    Kind = kind.Value,
                    Size = MepRunClassifier.ParseSize(line.LayerName, kind.Value),
                    Drainage = drainage,
                    SlopePercent = drainage ? MepRunClassifier.DefaultDrainageSlopePercent : 0,
                    Classification = cls,
                    ServiceDefaulted = svcDefaulted,
                });
            }

            var lib = MepFixtureMap.Get(_doc);
            foreach (var block in extraction.Blocks ?? new List<DetectedBlock>())
            {
                if (block == null || string.IsNullOrEmpty(block.BlockName)) continue;

                // V3 — riser blocks (UP/DN/RISER) become vertical runs, not fixtures.
                if (RiserRx.IsMatch(block.BlockName))
                {
                    var rk = MepRunClassifier.DetectKind(block.LayerName, block.InferredCategory) ?? MepRunKind.Pipe;
                    bool up = !Regex.IsMatch(block.BlockName, @"(?i)\b(dn|down)\b");
                    result.Risers.Add(new MepRiserCandidate
                    {
                        Point = block.InsertionPoint,
                        Kind = rk,
                        Size = MepRunClassifier.Default(rk),
                        Up = up,
                        BlockName = block.BlockName,
                        Classification = MepServiceClassifier.Classify(block.LayerName, rk),
                        DrainageStack = !up || MepRunClassifier.IsDrainage(block.LayerName),
                    });
                    continue;
                }

                var rule = lib?.Match(block.BlockName);
                if (rule == null)
                {
                    result.UnmatchedBlockCounts.TryGetValue(block.BlockName, out int n);
                    result.UnmatchedBlockCounts[block.BlockName] = n + 1;
                    continue;
                }
                // P2.1 — corroborate the block-name match against the block's DWG layer.
                string layerCat = LayerMapper.InferCategory(block.LayerName);
                string layerDisc = MepFixtureDiscipline.OfLayerCategory(layerCat);
                string ruleDisc = MepFixtureDiscipline.OfCategory(rule.Category);
                result.Fixtures.Add(new MepDetectedFixture
                {
                    Block = block,
                    Rule = rule,
                    MountingHeightMm = ResolveHeightMm(rule),
                    LayerDiscipline = layerDisc,
                    RuleDiscipline = ruleDisc,
                    LayerMismatch = !MepFixtureDiscipline.Compatible(layerDisc, ruleDisc),
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
