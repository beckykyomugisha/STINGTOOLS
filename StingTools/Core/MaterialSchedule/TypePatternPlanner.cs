// ══════════════════════════════════════════════════════════════════════════
//  TypePatternPlanner.cs — what a proposed type mapping would actually do.
//
//  Mapping a type to a commodity is not a labelling change. It converts the
//  row from measured units into supplier units: 610.62 m2 of roof becomes
//  N sheets, at a different rate, in a different unit. That is the number
//  somebody buys against.
//
//  So no mapping is written without this. It answers three questions the
//  author cannot answer from the pattern alone:
//
//    * which rows would it claim — a pattern is a SUBSTRING test, so "225"
//      catches every 225-anything in the model, not just the roof;
//    * what does each row become — before and after, both units named;
//    * is the commodity even plausible — a tiled roof mapped to roof-sheet
//      converts cleanly and prices sheeting for a tile roof, which is a
//      confident wrong number and the exact failure this schedule keeps
//      being audited for.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    public sealed class PatternMatchRow
    {
        public string Description = "";
        public double BeforeQuantity;
        public string BeforeUnit = "";
        public double AfterQuantity;
        public string AfterUnit = "";

        /// <summary>True when this row already converts — the mapping is not needed for it.</summary>
        public bool AlreadyConverted;
    }

    public sealed class TypePatternPlan
    {
        public string CommodityKey = "";
        public string Pattern = "";
        public readonly List<PatternMatchRow> Matches = new List<PatternMatchRow>();

        /// <summary>Reasons this must not be written at all.</summary>
        public readonly List<string> Blockers = new List<string>();

        /// <summary>Reasons to look twice before writing it.</summary>
        public readonly List<string> Warnings = new List<string>();

        public bool CanApply => Blockers.Count == 0 && Matches.Count > 0;

        public string Summary()
        {
            if (Blockers.Count > 0)
                return "This mapping cannot be applied:\n  • " + string.Join("\n  • ", Blockers);

            if (Matches.Count == 0)
                return $"'{Pattern}' matches no commodity row in this schedule, so mapping it would "
                     + "change nothing. Check the spelling against the Description column — the "
                     + "pattern is matched against the model TYPE name.";

            var s = new System.Text.StringBuilder();
            s.AppendLine($"'{Pattern}' → {CommodityKey} would claim {Matches.Count} row(s):");
            foreach (var m in Matches.Take(12))
                s.AppendLine($"  • {m.Description}"
                           + $"\n      {m.BeforeQuantity:N2} {m.BeforeUnit}"
                           + $"  →  {m.AfterQuantity:N2} {m.AfterUnit}"
                           + (m.AlreadyConverted ? "   (already converts — unchanged by this)" : ""));
            if (Matches.Count > 12) s.AppendLine($"  … and {Matches.Count - 12} more.");

            if (Warnings.Count > 0)
                s.AppendLine().AppendLine("Look twice:\n  • " + string.Join("\n  • ", Warnings));

            return s.ToString().TrimEnd();
        }
    }

    public static class TypePatternPlanner
    {
        /// <summary>
        /// Work out what mapping <paramref name="pattern"/> to
        /// <paramref name="commodityKey"/> would do to the rows in this schedule.
        ///
        /// Never writes. The caller decides whether the result is acceptable,
        /// and it is shown before it is offered.
        /// </summary>
        public static TypePatternPlan Plan(SupplierUnitTable table,
                                           IEnumerable<MaterialCommodity> commodities,
                                           string commodityKey, string pattern)
        {
            var plan = new TypePatternPlan
            {
                CommodityKey = (commodityKey ?? "").Trim(),
                Pattern = (pattern ?? "").Trim()
            };

            if (plan.Pattern.Length == 0) { plan.Blockers.Add("the pattern is empty"); return plan; }
            if (plan.CommodityKey.Length == 0) { plan.Blockers.Add("no commodity was chosen"); return plan; }

            if (plan.Pattern.Length < SupplierUnitPatchFile.MinPatternLength)
                plan.Blockers.Add($"'{plan.Pattern}' is shorter than "
                    + $"{SupplierUnitPatchFile.MinPatternLength} characters. A pattern is matched as a "
                    + "SUBSTRING of every type name, so a fragment this small claims types nobody "
                    + "meant it to.");

            var rule = table?.ResolveByCommodityKey(plan.CommodityKey);
            if (rule == null)
            {
                plan.Blockers.Add($"'{plan.CommodityKey}' is not a commodity in the supplier-unit table");
                return plan;
            }

            foreach (var c in commodities ?? Enumerable.Empty<MaterialCommodity>())
            {
                if (c == null || c.IsMemorandum) continue;

                // The pattern is matched against the model TYPE names behind the
                // row, and against the description, which for an unconverted row
                // IS the type name.
                bool hit = Contains(c.Description, plan.Pattern)
                        || (c.TypeNames ?? new List<string>()).Any(t => Contains(t, plan.Pattern));
                if (!hit) continue;

                var conv = SupplierUnitConverter.Convert(rule, c.NetQuantity);
                plan.Matches.Add(new PatternMatchRow
                {
                    Description = c.Description,
                    BeforeQuantity = c.OrderQuantity,
                    BeforeUnit = c.SupplierUnit,
                    AfterQuantity = conv.OrderQuantity,
                    AfterUnit = conv.SupplierUnit,
                    AlreadyConverted = !c.ConversionBlocked
                                       && !string.Equals(c.SupplierUnit, c.CommodityKey,
                                                         StringComparison.OrdinalIgnoreCase)
                });
            }

            AddWarnings(plan, rule);
            return plan;
        }

        private static void AddWarnings(TypePatternPlan plan, SupplierUnitRule rule)
        {
            int already = plan.Matches.Count(m => m.AlreadyConverted);
            if (already > 0)
                plan.Warnings.Add($"{already} of the matched row(s) already convert to a commodity. "
                    + "This mapping would re-route them, changing their unit and quantity.");

            // The roof case, generalised: two commodities in the same family
            // convert equally cleanly and only one is right for the building.
            if (rule.CommodityKey.IndexOf("roof", StringComparison.OrdinalIgnoreCase) >= 0)
                plan.Warnings.Add("Sheet and tile roofs both convert from area and both look correct "
                    + "afterwards. Check which this roof actually is — the fastener and covering "
                    + "rates differ, and a wrong choice here prices confidently for the other one.");

            if (plan.Matches.Count > 8)
                plan.Warnings.Add($"{plan.Matches.Count} rows is a lot for one pattern. A longer, "
                    + "more specific pattern usually means you meant fewer.");
        }

        private static bool Contains(string haystack, string needle) =>
            !string.IsNullOrEmpty(haystack)
            && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Commodities a row could sensibly be mapped to: everything the table
        /// can convert an AREA or a COUNT into. Offered as a list rather than a
        /// free-text key so the commodity can never be misspelt.
        /// </summary>
        public static List<SupplierUnitRule> Candidates(SupplierUnitTable table) =>
            (table?.Rules ?? new List<SupplierUnitRule>())
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.CommodityKey))
                .OrderBy(r => r.CommodityKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
}
