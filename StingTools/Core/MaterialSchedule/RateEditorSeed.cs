// ══════════════════════════════════════════════════════════════════════════
//  RateEditorSeed.cs — what the rate editor offers, and what it writes back.
//
//  WHY THIS EXISTS. The export tells you to "add a row keyed 'X' to
//  commodity_rates.csv". Of the 25 keys the first real export asked for, 21
//  carry a non-ASCII em dash, because an unmatched row's key IS its display
//  name. CommodityRateResolver.Resolve is an exact OrdinalIgnoreCase
//  dictionary hit — no canonicalisation, no fuzzy match — so a hyphen typed
//  where an em dash belongs misses, the key returns to _unpriced, and you get
//  back the identical flag you were trying to clear. No error, no warning.
//
//  So the editor is seeded with the keys the resolver already produced, and
//  the user types only a NUMBER. Nobody types a key.
//
//  Revit-free on purpose: every decision about what may be priced, what wins
//  and what a blank cell means is testable without a Revit session, which is
//  where every bug this feature has had actually lived.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    /// <summary>One editable row. The key is never typed — only NewRateUGX is.</summary>
    public sealed class RateEditRow
    {
        public string CommodityKey = "";
        public string Description = "";
        public string SupplierUnit = "";

        /// <summary>What the schedule will buy, so the biggest levers sort first.</summary>
        public double OrderQuantity;

        /// <summary>The rate in force now; 0 when unpriced.</summary>
        public double CurrentRateUGX;

        /// <summary>"baseline" / "project" / "unpriced" — where CurrentRateUGX came from.</summary>
        public string CurrentSource = "";

        /// <summary>
        /// What the user typed. NULL means "left alone", which is NOT the same
        /// as 0. A blank cell must never delete a rate already in force — that
        /// would turn an untouched row into a silent price removal.
        /// </summary>
        public double? NewRateUGX;

        public bool IsUnpriced => CurrentRateUGX <= 0;

        /// <summary>What pricing this row is worth at the rate in force.</summary>
        public double CurrentAmountUGX => Math.Round(OrderQuantity * CurrentRateUGX, 0);
    }

    public sealed class RateSeedResult
    {
        public readonly List<RateEditRow> Rows = new List<RateEditRow>();

        /// <summary>Rows withheld because pricing them would double-count.</summary>
        public int MemorandaExcluded;

        public int UnpricedCount => Rows.Count(r => r.IsUnpriced);

        public string Summary()
        {
            if (Rows.Count == 0)
                return "Nothing to price: the schedule produced no commodity rows.";

            string s = $"{Rows.Count} commodity row(s), {UnpricedCount} of them unpriced.";
            if (MemorandaExcluded > 0)
                s += $" {MemorandaExcluded} memorandum row(s) are not offered — each records an "
                   + "intermediate measure whose purchasable parts are listed separately, and "
                   + "pricing both pays for the same work twice.";
            return s;
        }
    }

    public static class RateEditorSeed
    {
        /// <summary>
        /// Build the editable rows from a built schedule.
        ///
        /// Everything is offered, not only the unpriced. A baseline rate is an
        /// indicative Kampala figure that the shipped file itself says must be
        /// re-priced before tender, so replacing one with a supplier's actual
        /// quote is the commonest real edit. Unpriced rows sort first because
        /// they are the ones totalling zero.
        /// </summary>
        public static RateSeedResult Build(MaterialScheduleDocument doc)
        {
            var result = new RateSeedResult();
            if (doc?.Stages == null) return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var c in doc.Stages.Where(s => s?.Commodities != null)
                                        .SelectMany(s => s.Commodities))
            {
                if (c == null || string.IsNullOrWhiteSpace(c.CommodityKey)) continue;

                // A memorandum's AmountUGX is hard-zero by design. Offering it a
                // rate cell would invite the user to defeat that, and the cell
                // would appear to do nothing, which reads as a bug.
                if (c.IsMemorandum) { result.MemorandaExcluded++; continue; }

                // One row per key: the same commodity appears in several stages
                // and they all share one rate.
                if (!seen.Add(c.CommodityKey)) continue;

                result.Rows.Add(new RateEditRow
                {
                    CommodityKey   = c.CommodityKey,
                    Description    = c.Description ?? "",
                    SupplierUnit   = c.SupplierUnit ?? "",
                    OrderQuantity  = c.OrderQuantity,
                    CurrentRateUGX = c.RateUGX,
                    CurrentSource  = c.RateSource ?? ""
                });
            }

            // Unpriced first, then largest quantity — the order somebody pricing
            // a job actually works in.
            result.Rows.Sort((a, b) =>
            {
                if (a.IsUnpriced != b.IsUnpriced) return a.IsUnpriced ? -1 : 1;
                int q = b.OrderQuantity.CompareTo(a.OrderQuantity);
                return q != 0 ? q
                    : string.Compare(a.Description, b.Description, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        /// <summary>
        /// Merge edits onto the rows already in the project file.
        ///
        /// Three rules, each of them a way this could quietly lose a price:
        ///   * a NULL NewRateUGX leaves the existing project row untouched;
        ///   * a NewRateUGX of 0 or less is NOT written — the resolver ignores a
        ///     zero project row anyway, so writing one produces a file that
        ///     looks like a decision and behaves like an absence;
        ///   * a project row for a key today's model no longer produces is KEPT.
        ///     Models change between exports, and dropping rates for what is not
        ///     in this one would silently discard pricing work.
        /// </summary>
        public static List<CommodityRate> Merge(IEnumerable<CommodityRate> existingProjectRows,
                                                IEnumerable<RateEditRow> edited,
                                                out int added, out int changed, out int untouched)
        {
            added = 0; changed = 0; untouched = 0;

            var byKey = new Dictionary<string, CommodityRate>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in existingProjectRows ?? Enumerable.Empty<CommodityRate>())
            {
                if (r == null || string.IsNullOrWhiteSpace(r.CommodityKey)) continue;
                byKey[r.CommodityKey] = new CommodityRate
                {
                    CommodityKey = r.CommodityKey,
                    SupplierUnit = r.SupplierUnit,
                    RateUGX      = r.RateUGX,
                    Description  = r.Description,
                    Source       = "project"
                };
            }

            foreach (var e in edited ?? Enumerable.Empty<RateEditRow>())
            {
                if (e == null || string.IsNullOrWhiteSpace(e.CommodityKey)) continue;
                if (!e.NewRateUGX.HasValue) { untouched++; continue; }

                double v = e.NewRateUGX.Value;
                if (v <= 0) { untouched++; continue; }

                if (byKey.TryGetValue(e.CommodityKey, out var existing))
                {
                    if (Math.Abs(existing.RateUGX - v) < 0.005) { untouched++; continue; }
                    existing.RateUGX = v;
                    if (!string.IsNullOrWhiteSpace(e.SupplierUnit)) existing.SupplierUnit = e.SupplierUnit;
                    if (string.IsNullOrWhiteSpace(existing.Description))
                        existing.Description = e.Description ?? "";
                    changed++;
                }
                else
                {
                    byKey[e.CommodityKey] = new CommodityRate
                    {
                        CommodityKey = e.CommodityKey,
                        SupplierUnit = e.SupplierUnit ?? "",
                        RateUGX      = v,
                        Description  = e.Description ?? "",
                        Source       = "project"
                    };
                    added++;
                }
            }

            return byKey.Values
                .OrderBy(r => r.CommodityKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Problems worth blocking or warning on before a save.
        ///
        /// The supplier-unit check earns its place: CommodityAggregator prefers
        /// the converter's unit and IGNORES the one in the CSV, so a row that
        /// says "Bags" against a commodity sold in trips is accepted, priced,
        /// and wrong with nothing anywhere to say so.
        /// </summary>
        public static List<string> Validate(IEnumerable<RateEditRow> edited)
        {
            var problems = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var e in edited ?? Enumerable.Empty<RateEditRow>())
            {
                if (e == null) continue;
                if (string.IsNullOrWhiteSpace(e.CommodityKey)) { problems.Add("a row has no key"); continue; }
                if (!seen.Add(e.CommodityKey))
                    problems.Add($"'{e.CommodityKey}' appears more than once — the last one would win silently");
                if (e.NewRateUGX.HasValue && e.NewRateUGX.Value < 0)
                    problems.Add($"'{e.CommodityKey}' has a negative rate");
            }
            return problems;
        }
    }
}
