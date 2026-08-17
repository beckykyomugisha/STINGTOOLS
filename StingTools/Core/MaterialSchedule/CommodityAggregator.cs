// ══════════════════════════════════════════════════════════════════════════
//  CommodityAggregator.cs — MAT-SCHED constituent rows → stage sections.
//
//  Quantities are SUMMED IN SOURCE UNITS BEFORE conversion. Converting per row
//  and then adding would round up once per element and inflate the order —
//  eleven cubic metres of sand is one truck trip, not two.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    /// <summary>One constituent row handed to the engine by the Revit adapter.</summary>
    public sealed class ConstituentInput
    {
        public string ConstituentKind = "";
        public string Category = "";
        public string TypeName = "";    // narrows a category match (MAT-SCHED trade units)
        public string Description = "";
        public string Unit = "";        // source unit as measured
        public double Quantity;
        public string LevelCode = "";
        public string TraceRef = "";    // BOQ line ref / element id, for the audit trail
    }

    public sealed class AggregatorInputs
    {
        public List<ConstituentInput> Constituents = new List<ConstituentInput>();
        public SupplierUnitTable Units = new SupplierUnitTable();
        public List<StageDefinition> StageDefs = new List<StageDefinition>();
        public string DefaultStageId = "";
        /// <summary>Categories that are not materials — see StageLibrary.ExcludedCategories.</summary>
        public List<string> ExcludedCategories = new List<string>();
        /// <summary>Description/type substrings that are never materials.</summary>
        public List<string> ExcludedDescriptionPatterns = new List<string>();
        public CommodityRateResolver Rates;
        public MaterialScheduleOptions Options = new MaterialScheduleOptions();
    }

    public static class CommodityAggregator
    {
        public static MaterialScheduleDocument Build(AggregatorInputs input)
        {
            var doc = new MaterialScheduleDocument();
            if (input == null) return doc;
            doc.Options = input.Options ?? new MaterialScheduleOptions();

            // (stageId, commodityKey) → accumulator in SOURCE units.
            var acc = new Dictionary<(string stage, string key), Accum>();

            // PERF: built ONCE. This used to sort the definition list and allocate
            // a List per row.
            var stageIndex = StageIndex.Build(input.StageDefs, input.DefaultStageId);

            var patterns = (input.ExcludedDescriptionPatterns ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();

            var excluded = new HashSet<string>(
                input.ExcludedCategories ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            foreach (var row in input.Constituents ?? new List<ConstituentInput>())
            {
                if (row == null) continue;

                // MAT-SCHED-8 — not a material. Counted, not silently dropped:
                // a real export turned beds and TV shelves into purchasable
                // commodities, 60 rows of noise and a UGX 0 grand total.
                if (!string.IsNullOrWhiteSpace(row.Category) && excluded.Contains(row.Category.Trim()))
                {
                    string c = row.Category.Trim();
                    doc.ExcludedByCategory.TryGetValue(c, out int n);
                    doc.ExcludedByCategory[c] = n + 1;
                    continue;
                }

                // Not a material despite a legitimate category — an opening, a
                // muntin pattern, a trim. Blank patterns are skipped: "".IndexOf
                // returns 0 and would exclude the entire model.
                if (patterns.Count > 0)
                {
                    string hay = (row.Description ?? "") + " " + (row.TypeName ?? "");
                    bool hit = false;
                    foreach (string pat in patterns)
                        if (hay.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0) { hit = true; break; }
                    if (hit)
                    {
                        string c2 = string.IsNullOrWhiteSpace(row.Category) ? "(uncategorised)" : row.Category.Trim();
                        doc.ExcludedByCategory.TryGetValue(c2, out int n2);
                        doc.ExcludedByCategory[c2] = n2 + 1;
                        continue;
                    }
                }

                // Constituent kind first, then category (+ optional type pattern).
                var res = input.Units != null
                    ? input.Units.Resolve(row.ConstituentKind, row.Category, row.TypeName)
                    : new SupplierUnitResolution { Match = SupplierUnitMatch.None };
                var rule = res.Rule;

                // The COMMODITY's stage wins over the ELEMENT's. A rule matched by
                // category would otherwise inherit that category's stage — filing
                // wall paint under the frame, because "Walls" routes there.
                string stageId = !string.IsNullOrWhiteSpace(rule?.StageId)
                    ? rule.StageId
                    : stageIndex.Resolve(row.ConstituentKind, row.Category, row.LevelCode);

                // No rule → the row still appears, keyed by its own description and
                // carrying its measured unit. Silently dropping it would lose real
                // measured work from the document.
                string commodityKey = rule?.CommodityKey
                    ?? (string.IsNullOrWhiteSpace(row.Description) ? (row.ConstituentKind ?? "") : row.Description);

                var k = (stageId, commodityKey);
                if (!acc.TryGetValue(k, out var a))
                {
                    a = new Accum
                    {
                        Rule = rule,
                        Description = rule?.Description ?? row.Description ?? commodityKey,
                        Spec = rule?.Spec ?? "",
                        FallbackUnit = row.Unit ?? ""
                    };
                    acc[k] = a;
                }

                // A rule whose sourceUnit does not match the row's measured unit
                // must NOT convert. Without this guard a m2 quantity flowed into
                // a piece-count commodity unchallenged: "Bricks · No. · 364.31"
                // in a real export was 364 SQUARE METRES of brickwork relabelled
                // as a brick count.
                if (rule != null && !UnitsAlign(row.Unit, rule.SourceUnit))
                {
                    a.ConversionBlocked = true;
                    if (string.IsNullOrEmpty(a.ConversionNote))
                        a.ConversionNote = $"measured in '{row.Unit}' but commodity "
                                         + $"'{rule.CommodityKey}' is bought per '{rule.SourceUnit}' "
                                         + "— converting would compare unlike units";
                    rule = null;   // keep the measured figure, do not convert
                    a.Rule = null;
                }

                // A category hit whose type did not match is NOT converted. Record
                // why, so the reconciler can name it and the QS can either fix the
                // rule or price the measured row by hand.
                if (res.Match == SupplierUnitMatch.CategoryTypeMismatch)
                {
                    a.ConversionBlocked = true;
                    if (string.IsNullOrEmpty(a.ConversionNote))
                        a.ConversionNote = $"category '{row.Category}' maps to commodity "
                                         + $"'{res.CandidateCommodityKey}', but type '{row.TypeName}' "
                                         + "matches none of its type patterns";
                }
                a.SourceQuantity += row.Quantity;
                if (!string.IsNullOrWhiteSpace(row.TraceRef)) a.TraceRefs.Add(row.TraceRef);
            }

            // Materialise stages in definition order, dropping empties.
            var orderedDefs = (input.StageDefs ?? new List<StageDefinition>())
                .OrderBy(d => d.Order).ToList();

            foreach (var def in orderedDefs)
            {
                var mine = acc.Where(kv => kv.Key.stage == def.StageId)
                              .OrderBy(kv => kv.Value.Description, StringComparer.OrdinalIgnoreCase)
                              .ToList();
                if (mine.Count == 0) continue;

                var section = new StageSection
                {
                    StageId = def.StageId,
                    Title = def.Title,
                    Preamble = def.Preamble
                };

                foreach (var kv in mine)
                {
                    var a = kv.Value;
                    var conv = SupplierUnitConverter.Convert(a.Rule, a.SourceQuantity);
                    var rate = input.Rates?.Resolve(kv.Key.key)
                               ?? new CommodityRate { RateUGX = 0, Source = "unpriced" };

                    section.Commodities.Add(new MaterialCommodity
                    {
                        CommodityKey = kv.Key.key,
                        Description = a.Description,
                        Spec = a.Spec,
                        SupplierUnit = string.IsNullOrWhiteSpace(conv.SupplierUnit)
                            ? a.FallbackUnit : conv.SupplierUnit,
                        NetQuantity = conv.NetQuantity,
                        WastagePct = conv.WastagePct,
                        OrderQuantity = conv.OrderQuantity,
                        RateUGX = rate.RateUGX,
                        RateSource = rate.Source,
                        TraceRefs = a.TraceRefs,
                        ConversionBlocked = a.ConversionBlocked,
                        ConversionNote = a.ConversionNote
                    });
                }

                doc.Stages.Add(section);
            }

            StageMapper.AssignLetters(doc.Stages);
            return doc;
        }

        /// <summary>
        /// True when a measured unit and a rule's source unit denote the same
        /// dimension. An empty sourceUnit means the rule declares no expectation
        /// and is trusted, so existing rules keep working.
        /// </summary>
        private static bool UnitsAlign(string measured, string ruleSource)
        {
            if (string.IsNullOrWhiteSpace(ruleSource)) return true;
            if (string.IsNullOrWhiteSpace(measured)) return true;   // nothing to contradict
            return string.Equals(StingTools.BOQ.BoqUnits.Normalise(measured),
                                 StingTools.BOQ.BoqUnits.Normalise(ruleSource),
                                 StringComparison.OrdinalIgnoreCase);
        }

        private sealed class Accum
        {
            public SupplierUnitRule Rule;
            public string Description = "";
            public string Spec = "";
            public string FallbackUnit = "";
            public double SourceQuantity;
            public bool ConversionBlocked;
            public string ConversionNote = "";
            public List<string> TraceRefs = new List<string>();
        }
    }
}
