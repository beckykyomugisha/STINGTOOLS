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
        /// <summary>The element's material name. Matched BEFORE the type name —
        /// see SupplierUnitRule.MatchMaterialPatterns for why.</summary>
        public string MaterialName = "";

        /// <summary>Wastage this row's variant implies, or -1 for the rule's
        /// default. Blended across rows that merge — see the accumulator.</summary>
        public double WastePctOverride = -1;
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
        /// <summary>Filled during the pass, for the export note. See MaterialMatchTally.</summary>
        public MaterialMatchTally MaterialScan = new MaterialMatchTally();
        /// <summary>Description/type substrings that are never materials.</summary>
        public List<string> ExcludedDescriptionPatterns = new List<string>();
        /// <summary>Categories no description pattern may exclude — see StageLibrary.</summary>
        public List<string> ExclusionProtectedCategories = new List<string>();
        /// <summary>Intermediate measures and their purchasable constituents.</summary>
        public List<IntermediateMeasureRule> IntermediateMeasures = new List<IntermediateMeasureRule>();
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

            var protectedCats = new HashSet<string>(
                input.ExclusionProtectedCategories ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

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
                if (patterns.Count > 0
                    && !(!string.IsNullOrWhiteSpace(row.Category) && protectedCats.Contains(row.Category.Trim())))
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
                    ? input.Units.Resolve(row.ConstituentKind, row.Category, row.TypeName, row.MaterialName)
                    : new SupplierUnitResolution { Match = SupplierUnitMatch.None };
                var rule = res.Rule;

                // The COMMODITY's stage wins over the ELEMENT's. A rule matched by
                // category would otherwise inherit that category's stage — filing
                // wall paint under the frame, because "Walls" routes there.
                string stageId = !string.IsNullOrWhiteSpace(rule?.StageId)
                    ? rule.StageId
                    : stageIndex.Resolve(row.ConstituentKind, row.Category, row.LevelCode,
                                         ((row.TypeName ?? "") + " " + (row.Description ?? "")).Trim());

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
                        FallbackUnit = row.Unit ?? "",
                        SourceKind = row.ConstituentKind ?? ""
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
                // The material scan. Only rows with no constituent kind of
                // their own could ever be decided by material, so those are the
                // denominator — counting the rest would report a coverage the
                // feature was never asked for.
                // Only categories some rule could ever claim. A second project
                // reported "200 rows could be identified by material, 3
                // matched" and listed 56 unplaced materials led by
                // '911 CARRERA S - BODY COLOR' - a Porsche in the entourage.
                // A car's paint is never a building commodity, and counting it
                // made a working feature read as a 1.5% success rate while
                // burying the materials that DO need a pattern.
                if (string.IsNullOrWhiteSpace(row.ConstituentKind) && input.MaterialScan != null
                    && CategoryCouldConvert(input.Units, row.Category))
                {
                    var scan = input.MaterialScan;
                    scan.RowsInspected++;
                    bool hasMaterial = !string.IsNullOrWhiteSpace(row.MaterialName);
                    if (hasMaterial) scan.RowsWithMaterial++;
                    if (res.Match == SupplierUnitMatch.ByMaterial) scan.MatchedByMaterial++;
                    else if (hasMaterial && res.Rule == null)
                    {
                        scan.UnplacedMaterials.Add(row.MaterialName.Trim());
                        if (!string.IsNullOrWhiteSpace(row.Category))
                            scan.UnplacedCategories.Add(row.Category.Trim());
                    }
                }

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
                if (!string.IsNullOrWhiteSpace(row.Category)) a.Categories.Add(row.Category.Trim());
                if (!string.IsNullOrWhiteSpace(row.TypeName)) a.TypeNames.Add(row.TypeName.Trim());
                if (!string.IsNullOrWhiteSpace(row.TypeName) && row.Quantity > 0)
                {
                    string tn = row.TypeName.Trim();
                    a.SourceByType.TryGetValue(tn, out double prev);
                    a.SourceByType[tn] = prev + row.Quantity;
                }
                if (row.WastePctOverride >= 0 && row.Quantity > 0)
                {
                    a.WasteWeighted += row.WastePctOverride * row.Quantity;
                    a.WasteWeight += row.Quantity;
                }
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
                    double blendedWaste = a.WasteWeight > 0 ? a.WasteWeighted / a.WasteWeight : -1;
                    var conv = SupplierUnitConverter.Convert(a.Rule, a.SourceQuantity, blendedWaste);
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
                        Categories = a.Categories.ToList(),
                        // (the per-type source is published on the DOCUMENT, below)
                        TypeNames = a.TypeNames.Take(8).ToList(),
                        SourceKind = a.SourceKind,
                        ConversionBlocked = a.ConversionBlocked,
                        ConversionNote = a.ConversionNote
                    });
                }

                doc.Stages.Add(section);
            }

            // One entry per commodity, merged across stages — the breakdown is
            // about which TYPE produced a commodity, not which section it was
            // printed in.
            foreach (var kv in acc)
            {
                if (kv.Value.SourceByType.Count == 0) continue;
                if (!doc.SourceByType.TryGetValue(kv.Key.key, out var byType))
                    doc.SourceByType[kv.Key.key] =
                        byType = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in kv.Value.SourceByType)
                {
                    byType.TryGetValue(t.Key, out double prev);
                    byType[t.Key] = prev + t.Value;
                }
            }

            StageMapper.AssignLetters(doc.Stages);

            // AFTER assembly: a row is only an intermediate if the things bought
            // in its place actually reached the document, which cannot be known
            // while the rows are still being accumulated.
            IntermediateMeasureMarker.Apply(doc, input.IntermediateMeasures);
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

        /// <summary>
        /// True when ANY rule in the table names this category. Furniture,
        /// entourage and casework are named by none, so a material on them can
        /// never become a commodity and counting it only dilutes the scan.
        ///
        /// A rule with no categories at all (matched by kind or material alone)
        /// makes every category a candidate - which is correct, because such a
        /// rule really could claim anything.
        /// </summary>
        private static bool CategoryCouldConvert(SupplierUnitTable units, string category)
        {
            if (units?.Rules == null) return true;
            if (string.IsNullOrWhiteSpace(category)) return true;
            string c = category.Trim();
            foreach (var r in units.Rules)
            {
                if (r?.MatchCategories == null || r.MatchCategories.Count == 0)
                {
                    if (r?.MatchMaterialPatterns != null && r.MatchMaterialPatterns.Count > 0)
                        return true;   // material-only rule: any category could carry it
                    continue;
                }
                foreach (string mc in r.MatchCategories)
                    if (string.Equals(mc, c, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private sealed class Accum
        {
            public SupplierUnitRule Rule;
            public string Description = "";
            public string Spec = "";
            public string FallbackUnit = "";
            public string SourceKind = "";
            public double SourceQuantity;
            public bool ConversionBlocked;
            public string ConversionNote = "";
            public List<string> TraceRefs = new List<string>();
            public readonly SortedSet<string> Categories =
                new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            /// <summary>
            /// Quantity-weighted numerator for the blended waste, and the
            /// quantity that carried an override at all.
            ///
            /// Rows merge by commodity, so a wall in Flemish bond (8%) and one
            /// in stack bond (3%) become ONE order line and cannot both have
            /// their own allowance. Weighting by quantity is the only blend
            /// that keeps the total right: the bigger wall moves the figure
            /// more, which is what a QS would do by hand.
            ///
            /// Rows with NO override are excluded from both sides rather than
            /// counted at the rule's default — that would let one unstated row
            /// drag a stated blend back toward a number nobody chose.
            /// </summary>
            public double WasteWeighted, WasteWeight;

            /// <summary>
            /// Measured source per model type, for the by-type breakdown.
            ///
            /// The aggregator's own numerator, so a share computed from it
            /// cannot disagree with the order line it came from — the same
            /// reason the breakdown apportions rather than re-converts.
            /// </summary>
            public readonly Dictionary<string, double> SourceByType =
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            public readonly SortedSet<string> TypeNames =
                new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
