// ══════════════════════════════════════════════════════════════════════════
//  ConsumablesTally.cs — MATSCHED-T4 diagnostic and honesty banner.
//
//  Two jobs, and the second is the important one.
//
//  1. DENOMINATOR. Same contract as the four layer-scan tallies: how many
//     rules were considered, how many fired, and — by name — which drivers
//     measured zero. A consumables section that is simply absent is
//     indistinguishable from a model with no walls, no steel and no formwork.
//
//  2. BANNER. Every row this source produces is RATIO-DERIVED. Nothing in the
//     model states how much hoop iron a wall carries; the number exists only
//     because a table says so. A ratio presented as a measurement is worse
//     than no number at all, because nobody checks it — so the banner names
//     the rules that fired, quotes their ratios, and says in the wording
//     SiteToolsCalculator already uses that these are practice heuristics and
//     not a standard.
//
//  The banner is CONDITIONAL on something having fired. A banner qualifying
//  rows that were never emitted is noise, and noise is how real banners come
//  to be ignored.
//
//  Revit-free on purpose: the value of a diagnostic is entirely in whether its
//  message is right, so the message is the part that has to be testable.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    public sealed class ConsumablesTally
    {
        /// <summary>One rule that produced a row, kept for the banner.</summary>
        public sealed class FiredRule
        {
            public string Kind = "";
            public string Driver = "";
            public double PerDriver;
            public string Unit = "";
            public double DriverValue;
        }

        public int RulesConsidered;

        public readonly List<FiredRule> FiredRules = new List<FiredRule>();

        /// <summary>Drivers that measured zero, by name, with the kinds they
        /// would have produced. The commonest and least alarming outcome, and
        /// the one most easily mistaken for a bug.</summary>
        public readonly SortedDictionary<string, SortedSet<string>> DriversAbsent =
            new SortedDictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Rules that could never fire — an unknown driver, a missing
        /// ratio, a missing unit. Dead config, reported as such.</summary>
        public readonly SortedSet<string> UnusableRules =
            new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Roof covering measured but not attributable to a product, in m².
        ///
        /// This exists to stop one sentence lying. A zero driver was reported as
        /// "that is the intended behaviour — with no driver the quantity would be
        /// invented", which is right when the model has no roof and wrong when it
        /// has 856 m² of roof nobody could name. Same zero, opposite action:
        /// nothing to do, versus name the roof type or count the fixing pattern
        /// by hand.
        /// </summary>
        public double RoofCoveringUnattributedM2;

        /// <summary>
        /// Roof covering that was measured AND identified, in m2.
        ///
        /// Separates two zeros that read identically. On a second project the
        /// fastener driver was zero and the note said "with no driver the
        /// quantity would be invented" - while 437 m2 of shingle and clay tile
        /// sat priced above it. Both are NAILED, so zero screws is the right
        /// answer, not a missing one.
        ///
        /// Same failure as the unattributed case one level down: the number is
        /// correct and the sentence explaining it is not.
        /// </summary>
        public double RoofCoveringMeasuredM2;

        /// <summary>Driver rows skipped because their measured unit disagreed
        /// with the unit the driver is counted in. Carried through from
        /// ConsumableDrivers so one line reports the whole source.</summary>
        public readonly SortedSet<string> UnitMismatches =
            new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        private const int MaxNamesShown = 8;

        public void Reset()
        {
            RulesConsidered = 0;
            FiredRules.Clear();
            DriversAbsent.Clear();
            UnusableRules.Clear();
            UnitMismatches.Clear();
            RoofCoveringUnattributedM2 = 0;
            RoofCoveringMeasuredM2 = 0;
        }

        public void Consider(string kind) { RulesConsidered++; }

        public void Fired(string kind, string driver, double perDriver, string unit, double driverValue)
            => FiredRules.Add(new FiredRule
            {
                Kind = kind, Driver = driver, PerDriver = perDriver,
                Unit = unit, DriverValue = driverValue
            });

        public void RejectDriverAbsent(string kind, string driver)
        {
            string d = (driver ?? "").Trim();
            if (!DriversAbsent.TryGetValue(d, out var set))
                DriversAbsent[d] = set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(kind);
        }

        public void RejectUnknownDriver(string kind, string driver)
            => UnusableRules.Add($"{kind} (driver '{driver}' is not one of "
                               + string.Join(", ", ConsumablesCalculator.AllDrivers.OrderBy(x => x)) + ")");

        public void RejectNoRatio(string kind) => UnusableRules.Add($"{kind} (no ratio)");

        public void RejectNoUnit(string kind) => UnusableRules.Add($"{kind} (no unit)");

        /// <summary>
        /// The denominator line, or NULL when no rule was even looked at — no
        /// library means nothing to report, and an invented zero reads as a
        /// finding.
        /// </summary>
        public string Summary()
        {
            if (RulesConsidered <= 0) return null;

            string s = $"Consumables scan: {RulesConsidered} ratio rule(s) considered, "
                     + $"{FiredRules.Count} produced a quantity.";

            if (DriversAbsent.Count > 0)
            {
                var parts = DriversAbsent.Take(MaxNamesShown)
                    .Select(kv => $"{kv.Key} (would have given {string.Join(", ", kv.Value)})");
                s += " Nothing was emitted for these because their measured driver is zero: "
                   + string.Join("; ", parts)
                   + (DriversAbsent.Count > MaxNamesShown ? ", …" : "")
                   + ". That is the intended behaviour — with no driver the quantity would be "
                   + "invented, not estimated.";

                // ... unless the driver is zero because nothing could be
                // IDENTIFIED, which is a different fact with a different fix.
                // A covering that is nailed rather than screwed. The count is
                // zero because it SHOULD be, and saying "no driver" invites
                // somebody to go looking for a fault that is not there.
                if (RoofCoveringMeasuredM2 > 0
                    && DriversAbsent.ContainsKey("roof_fastener_nr"))
                {
                    s += $" The roof fastener count is zero because the {RoofCoveringMeasuredM2:N0} m² "
                       + "of covering measured here is NAILED, not screwed — tiles and shingles are "
                       + "fixed every other course. That is the right answer for this roof, not a "
                       + "missing measurement.";
                }

                if (RoofCoveringUnattributedM2 > 0
                    && DriversAbsent.ContainsKey("roof_covering_m2"))
                {
                    s += $" Read that carefully for the roof: {RoofCoveringUnattributedM2:N0} m² of "
                       + "roof covering WAS measured, but its type name matched no supplier pattern, so "
                       + "the product is unknown — and the fastener ratio is product-specific (11/m² is "
                       + "corrugated sheeting fixed at every second corrugation; a tiled roof of the same "
                       + "area takes clips at another rate). Applying it here would give a confident "
                       + "number for the wrong roof. Name the roof type so it matches a pattern in "
                       + "STING_SUPPLIER_UNITS.json, or count the specified fixing pattern by hand.";
                }
            }

            if (UnusableRules.Count > 0)
                s += " These rules can never fire and should be corrected or removed: "
                   + string.Join(", ", UnusableRules.Take(MaxNamesShown))
                   + (UnusableRules.Count > MaxNamesShown ? ", …" : "") + ".";

            if (UnitMismatches.Count > 0)
                s += " Driver rows skipped on a unit mismatch: "
                   + string.Join(", ", UnitMismatches.Take(MaxNamesShown))
                   + (UnitMismatches.Count > MaxNamesShown ? ", …" : "")
                   + " — a quantity measured in one dimension is never added to a total in another.";

            return s;
        }

        /// <summary>
        /// The honesty banner, or NULL when nothing was derived.
        ///
        /// It quotes the actual ratio and the actual driver value for every rule
        /// that fired, so the figure can be checked against the specified detail
        /// without opening the JSON — which is the difference between a
        /// disclaimer and a diagnostic.
        /// </summary>
        public string Banner()
        {
            if (FiredRules.Count == 0) return null;

            var parts = FiredRules
                .OrderBy(f => f.Kind, StringComparer.OrdinalIgnoreCase)
                .Select(f => $"{f.Kind} = {Fmt(f.DriverValue)} × {Fmt(f.PerDriver)} {f.Unit} per "
                           + $"{UnitOfDriver(f.Driver)} ({f.Driver})");

            return "Consumables (" + string.Join("; ", parts) + ") are DERIVED FROM RATIOS, not "
                 + "measured: nothing in the model states how much hoop iron a wall carries or how "
                 + "much wire ties a tonne of steel. These are PRACTICE HEURISTICS, not a standard "
                 + "— no measurement rule publishes them. Every ratio and its source note live in "
                 + "STING_CONSUMABLES.json, overridable per project at _BIM_COORD/consumables.json. "
                 + "Review before issue.";
        }

        private static string UnitOfDriver(string driver)
        {
            switch ((driver ?? "").Trim().ToLowerInvariant())
            {
                case "walled_area_m2":   return "m² of walling";
                case "rebar_kg":         return "kg of reinforcement";
                case "formwork_m2":      return "m² of formwork";
                case "roof_covering_m2": return "m² of roof covering";
                default:                 return driver ?? "";
            }
        }

        private static string Fmt(double v)
            => Math.Abs(v - Math.Round(v)) < 1e-9 ? Math.Round(v).ToString("0")
                                                  : v.ToString("0.####");
    }
}
