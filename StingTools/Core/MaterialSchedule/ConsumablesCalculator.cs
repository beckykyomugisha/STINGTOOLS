// ══════════════════════════════════════════════════════════════════════════
//  ConsumablesCalculator.cs — MATSCHED-T4 ratio-derived consumables.
//
//  THERE IS NO STANDARD FOR THIS, and that is the whole point of the file.
//
//  T1-T3 measured what the model STATES: a screed layer's thickness, a board
//  layer's area, a membrane layer's function. Nothing here is stated anywhere.
//  Hoop iron, binding wire, formwork nails and roofing fasteners are bought in
//  proportion to work the schedule has already measured, at ratios that come
//  from contractor practice — not from NRM2, not from the model, not from any
//  published measurement rule.
//
//  A ratio presented as a measurement is WORSE THAN NO NUMBER AT ALL, because
//  nobody checks it. So:
//
//    * every ratio lives in STING_CONSUMABLES.json with a per-row source note
//      saying where the figure came from. None is hardcoded here;
//    * every rule must name a KNOWN driver, or it does not fire;
//    * a driver that measures zero produces NOTHING — never a minimum, never a
//      fixed quantity, because either would be a fabricated number wearing the
//      costume of a calculation;
//    * ConsumablesTally reports which rules fired, which drivers were absent
//      and why, and the export carries a banner naming these as heuristics.
//
//  Document-free, so the arithmetic and the honesty are both unit-tested.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    /// <summary>
    /// The measured totals a consumable can be derived from, taken from the
    /// SAME constituent rows the bill is built out of — not a second parallel
    /// take-off, and not a round-trip back through order quantities, which
    /// would fold the supplier rule's wastage into the driver.
    /// </summary>
    public sealed class ConsumableDrivers
    {
        public double WalledAreaM2;
        public double RebarKg;
        public double FormworkM2;
        public double RoofCoveringM2;

        /// <summary>
        /// Fasteners, already COUNTED per covering rather than derived by one
        /// flat ratio over a mixed roof. A sheet roof and a tiled roof on the
        /// same building need different numbers and one of them is zero, which
        /// no single ratio over the combined area can express.
        /// </summary>
        public double RoofFastenerNr;

        /// <summary>
        /// Roof covering that WAS measured but could not be attributed to a
        /// product: the category resolved to a roof commodity and the type name
        /// matched no pattern, so the supplier table refused to convert it.
        ///
        /// Kept OUT of RoofCoveringM2 on purpose. The fastener ratio is
        /// product-specific — 11/m² is corrugated sheeting fixed at every second
        /// corrugation, and a tiled roof of the same area takes clips at a
        /// different rate — so multiplying an unidentified covering by it would
        /// produce a confident number for the wrong roof.
        ///
        /// It is tracked separately because zero-because-nothing-is-there and
        /// zero-because-nothing-could-be-identified call for opposite actions,
        /// and the export said the first when it meant the second.
        /// </summary>
        public double RoofCoveringUnattributedM2;

        /// <summary>Rows whose measured unit disagreed with the unit a driver is
        /// counted in, and were therefore NOT summed. The `formwork` kind is
        /// emitted as "item" for a permanent-formwork void slab, and adding that
        /// 1 to a square-metre total would be the "Bricks · No. · 364.31"
        /// defect all over again.</summary>
        public readonly SortedSet<string> UnitMismatches =
            new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The unit a driver is counted in. A quantity measured in
        /// another dimension is never added to it.</summary>
        internal static string UnitFor(string driver)
        {
            switch ((driver ?? "").Trim().ToLowerInvariant())
            {
                case "rebar_kg": return "kg";
                // Counted FROM an area, so the ROW that feeds it is in m2.
                case "roof_fastener_nr": return "m2";
                default:         return "m2";
            }
        }

        /// <summary>Add to a driver by name. An unknown name adds nothing —
        /// the rule declaring it is reported by Validate, not healed here.</summary>
        public void Add(string driver, double quantity)
        {
            switch ((driver ?? "").Trim().ToLowerInvariant())
            {
                case "walled_area_m2":   WalledAreaM2 += quantity; break;
                case "rebar_kg":         RebarKg += quantity; break;
                case "formwork_m2":      FormworkM2 += quantity; break;
                case "roof_covering_m2": RoofCoveringM2 += quantity; break;
                case "roof_fastener_nr": RoofFastenerNr += quantity; break;
            }
        }

        public double Value(string driver)
        {
            switch ((driver ?? "").Trim().ToLowerInvariant())
            {
                case "walled_area_m2":   return WalledAreaM2;
                case "rebar_kg":         return RebarKg;
                case "formwork_m2":      return FormworkM2;
                case "roof_covering_m2": return RoofCoveringM2;
                case "roof_fastener_nr": return RoofFastenerNr;
                default:                 return 0;
            }
        }

        /// <summary>
        /// Sum the drivers out of the constituent rows.
        ///
        /// <paramref name="units"/> is the supplier-unit table, needed for ONE
        /// driver: the roof covering carries no constituent kind (it is matched
        /// by category + type pattern), so the only honest way to identify it is
        /// to ask the same table the aggregator asks. Re-deriving that match
        /// here with a second copy of the rules is how two files come to
        /// disagree without anyone comparing them.
        /// </summary>
        public static ConsumableDrivers From(IEnumerable<ConstituentInput> rows, SupplierUnitTable units)
        {
            var d = new ConsumableDrivers();
            if (rows == null) return d;

            foreach (var r in rows)
            {
                if (r == null || r.Quantity <= 0) continue;
                string kind = (r.ConstituentKind ?? "").Trim();
                string unit = StingTools.BOQ.BoqUnits.Normalise(r.Unit);

                bool Is(string want) => string.Equals(unit, want, StringComparison.OrdinalIgnoreCase);

                if (kind.Equals("blockwork", StringComparison.OrdinalIgnoreCase)
                 || kind.Equals("brickwork", StringComparison.OrdinalIgnoreCase))
                {
                    if (Is("m2")) d.WalledAreaM2 += r.Quantity;
                    else d.UnitMismatches.Add($"{kind} in '{r.Unit}'");
                    continue;
                }

                // `rebar` only, NOT `mesh`. Mesh is measured in m² and is bought
                // as sheets; it needs no tying wire the way loose bars do, and
                // adding an area to a mass would be nonsense besides.
                if (kind.Equals("rebar", StringComparison.OrdinalIgnoreCase))
                {
                    if (Is("kg")) d.RebarKg += r.Quantity;
                    else d.UnitMismatches.Add($"{kind} in '{r.Unit}'");
                    continue;
                }

                if (kind.Equals("formwork", StringComparison.OrdinalIgnoreCase))
                {
                    if (Is("m2")) d.FormworkM2 += r.Quantity;
                    else d.UnitMismatches.Add($"{kind} in '{r.Unit}'");
                    continue;
                }

                // A covering has no kind of its own — the supplier table matches
                // it by category, material or type pattern, and the RULE says
                // which driver it feeds. Nothing here names a commodity.
                if (units != null && string.IsNullOrEmpty(kind))
                {
                    var res = units.Resolve(r.ConstituentKind, r.Category, r.TypeName, r.MaterialName);
                    if (res.Rule != null && !string.IsNullOrWhiteSpace(res.Rule.FeedsDriver))
                    {
                        if (Is(UnitFor(res.Rule.FeedsDriver)))
                        {
                            d.Add(res.Rule.FeedsDriver, r.Quantity);

                            // The fastener count, per covering, in the same
                            // pass. A covering that states no density adds
                            // NOTHING rather than borrowing another's — a roof
                            // nobody has measured fixings for is not a roof
                            // with zero fixings, and those are different facts.
                            if (res.Rule.FastenersPerM2 >= 0
                                && string.Equals(res.Rule.FeedsDriver, "roof_covering_m2",
                                                 StringComparison.OrdinalIgnoreCase))
                                d.RoofFastenerNr += r.Quantity * res.Rule.FastenersPerM2;
                        }
                        else d.UnitMismatches.Add($"{res.Rule.CommodityKey} in '{r.Unit}'");
                    }
                    else if (res.Match == SupplierUnitMatch.CategoryTypeMismatch)
                    {
                        // Measured, but the product is unknown, so the ratio for
                        // it is unknown too. Tracked apart from the real driver
                        // and never added to it.
                        var candidate = units.ResolveByCommodityKey(res.CandidateCommodityKey);
                        if (candidate != null
                            && string.Equals(candidate.FeedsDriver, "roof_covering_m2",
                                             StringComparison.OrdinalIgnoreCase)
                            && Is("m2"))
                            d.RoofCoveringUnattributedM2 += r.Quantity;
                    }
                }
            }
            return d;
        }
    }

    /// <summary>
    /// One consumable rule. Quantity = driver × PerDriver, and nothing else —
    /// no minimum, no fixed quantity, no floor. Every one of those would be a
    /// number produced when the model said nothing.
    /// </summary>
    public sealed class ConsumableRule
    {
        /// <summary>The constituent kind emitted, e.g. "hoop_iron".</summary>
        public string ConstituentKind = "";
        public string Description = "";
        /// <summary>walled_area_m2 / rebar_kg / formwork_m2 / roof_covering_m2</summary>
        public string Driver = "";
        public double PerDriver;
        /// <summary>Unit of the EMITTED quantity. Must match the supplier rule's
        /// sourceUnit or the aggregator refuses to convert.</summary>
        public string Unit = "";
        /// <summary>REQUIRED. Where the figure came from, in words. A ratio
        /// without a stated source is indistinguishable from a guess, and a
        /// shipped-data test fails on an empty one.</summary>
        public string SourceNote = "";
    }

    public sealed class ConsumablesLibrary
    {
        public string SchemaVersion = "1.0";
        public string Note = "";
        public List<ConsumableRule> Rules = new List<ConsumableRule>();
    }

    public static class ConsumablesCalculator
    {
        private static readonly HashSet<string> KnownDrivers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "walled_area_m2", "rebar_kg", "formwork_m2", "roof_covering_m2", "roof_fastener_nr" };

        public static IReadOnlyCollection<string> AllDrivers => KnownDrivers;

        public static bool IsKnownDriver(string driver)
            => !string.IsNullOrWhiteSpace(driver) && KnownDrivers.Contains(driver.Trim());

        /// <summary>
        /// Turn measured drivers into constituent rows.
        ///
        /// They are returned as ordinary <see cref="ConstituentInput"/>s and fed
        /// back through the aggregator, so a consumable is staged, unit-checked,
        /// converted and priced by exactly the same machinery as a measured
        /// commodity. The site-tools section bypasses all of that and appends
        /// finished rows; doing the same here would mean a second, untested
        /// path to the page.
        /// </summary>
        public static List<ConstituentInput> Quantify(ConsumableDrivers drivers,
                                                      IEnumerable<ConsumableRule> rules,
                                                      ConsumablesTally tally)
        {
            var outList = new List<ConstituentInput>();
            if (drivers == null || rules == null) return outList;

            foreach (var r in rules)
            {
                if (r == null || string.IsNullOrWhiteSpace(r.ConstituentKind)) continue;
                string kind = r.ConstituentKind.Trim();
                tally?.Consider(kind);

                if (!IsKnownDriver(r.Driver))
                {
                    // Dead config is worse than absent config: it advertises
                    // coverage the export does not have.
                    tally?.RejectUnknownDriver(kind, r.Driver);
                    continue;
                }
                if (r.PerDriver <= 0)
                {
                    tally?.RejectNoRatio(kind);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(r.Unit))
                {
                    tally?.RejectNoUnit(kind);
                    continue;
                }

                double d = drivers.Value(r.Driver);
                if (d <= 0)
                {
                    // THE hard requirement. No measured driver, no row — not a
                    // minimum, not a fixed quantity, not a zero-quantity row
                    // that reads as a measurement of nothing.
                    tally?.RejectDriverAbsent(kind, r.Driver);
                    continue;
                }

                outList.Add(new ConstituentInput
                {
                    ConstituentKind = kind,
                    Category = "",
                    TypeName = "",
                    Description = string.IsNullOrWhiteSpace(r.Description) ? kind : r.Description,
                    Unit = r.Unit.Trim(),
                    Quantity = d * r.PerDriver,
                    LevelCode = "",
                    TraceRef = $"derived:{r.Driver}"
                });
                tally?.Fired(kind, r.Driver, r.PerDriver, r.Unit.Trim(), d);
            }
            return outList;
        }
    }
}
