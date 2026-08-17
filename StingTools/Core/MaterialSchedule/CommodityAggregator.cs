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

            foreach (var row in input.Constituents ?? new List<ConstituentInput>())
            {
                if (row == null) continue;

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
                    : StageMapper.ResolveStageId(
                        row.ConstituentKind, row.Category, row.LevelCode,
                        input.StageDefs, input.DefaultStageId);

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
