// ══════════════════════════════════════════════════════════════════════════
//  SiteToolsCalculator.cs — MAT-SCHED-9 site tools and small plant.
//
//  THERE IS NO STANDARD FOR THIS. NRM2 puts tools and plant in PRELIMINARIES,
//  priced as a lump sum or a percentage of the contract; no measurement
//  standard publishes a "wheelbarrows per mason" ratio. What follows is East
//  African contractor practice, expressed as an editable table and calibrated
//  against the PATMAC reference schedule.
//
//  It must never be presented to a client as a code-derived quantity. It is a
//  defensible starting point that a site manager corrects, which is still far
//  more useful to them than a percentage.
//
//  The chain is:  measured work → trade-days → gang size → tools.
//  Every step is measured or declared. Nothing is a constant buried in code.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    /// <summary>Measured work and programme facts the gang model needs.</summary>
    public sealed class SiteToolsInput
    {
        public double BlockworkM2;
        public double BrickworkM2;
        public double RebarKg;
        public double FormworkM2;
        public double ConcreteM3;
        /// <summary>Programme length. The denominator of the whole model — with
        /// no duration there is no gang size and nothing is produced.</summary>
        public int DurationDays;
        public int Storeys = 1;
    }

    /// <summary>
    /// Output rates per trade, and the fraction of the programme each trade
    /// actually occupies. Walling does not run for the whole job, so dividing
    /// its work by the full duration would understate the gang.
    /// </summary>
    public sealed class TradeRates
    {
        public double BlockworkM2PerMasonDay = 8.0;
        public double RebarKgPerBenderDay = 120.0;
        public double FormworkM2PerCarpenterDay = 10.0;
        public double ConcreteM3PerMixerDay = 8.0;
        public double HelpersPerMason = 1.5;

        public double MasonryPhaseFraction = 0.40;
        public double SteelPhaseFraction = 0.35;
        public double FormworkPhaseFraction = 0.35;
        public double ConcretePhaseFraction = 0.30;

        public static TradeRates Default() => new TradeRates();
    }

    public sealed class GangSizes
    {
        public int Masons;
        public int Helpers;
        public int BarBenders;
        public int Carpenters;
        public int Mixers;
        /// <summary>False when there is no programme to divide by.</summary>
        public bool IsUsable;

        public int Of(string driver)
        {
            switch ((driver ?? "").Trim().ToLowerInvariant())
            {
                case "masons":     return Masons;
                case "helpers":    return Helpers;
                case "barbenders": return BarBenders;
                case "carpenters": return Carpenters;
                case "mixers":     return Mixers;
                default:           return 0;
            }
        }
    }

    /// <summary>
    /// One tool rule. Quantity = max(Minimum, FixedQuantity + ceil(driver × PerDriver)).
    /// A rule whose driver resolves to zero produces nothing — no steel on site
    /// means no hacksaws.
    /// </summary>
    public sealed class ToolRule
    {
        public string ToolKey = "";
        public string Description = "";
        public string SupplierUnit = "No.";
        /// <summary>masons / helpers / barbenders / carpenters / mixers / storeys / site</summary>
        public string Driver = "site";
        public double PerDriver;
        public double FixedQuantity;
        public double Minimum;
    }

    public sealed class ToolQuantity
    {
        public string ToolKey = "";
        public string Description = "";
        public string SupplierUnit = "No.";
        public double Quantity;
    }

    public sealed class SiteToolsLibrary
    {
        public string SchemaVersion = "1.0";
        public TradeRates TradeRates = new TradeRates();
        public List<ToolRule> Rules = new List<ToolRule>();
    }

    public static class SiteToolsCalculator
    {
        private static readonly HashSet<string> KnownDrivers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "masons", "helpers", "barbenders", "carpenters", "mixers", "storeys", "site" };

        public static bool IsKnownDriver(string driver)
            => !string.IsNullOrWhiteSpace(driver) && KnownDrivers.Contains(driver.Trim());

        /// <summary>
        /// Gang sizes from work ÷ (output rate × phase duration). Rounded UP: a
        /// crew of 3.47 masons is 4 people, and rounding down leaves work unbuilt.
        /// </summary>
        public static GangSizes DeriveGangs(SiteToolsInput input, TradeRates rates)
        {
            var g = new GangSizes();
            if (input == null || rates == null || input.DurationDays <= 0) return g;
            g.IsUsable = true;

            int Crew(double work, double perDay, double phaseFraction)
            {
                if (work <= 0 || perDay <= 0) return 0;
                double phaseDays = input.DurationDays * Math.Max(0.01, phaseFraction);
                double tradeDays = work / perDay;
                return (int)Math.Ceiling(tradeDays / phaseDays - 1e-9);
            }

            g.Masons = Crew(input.BlockworkM2 + input.BrickworkM2,
                            rates.BlockworkM2PerMasonDay, rates.MasonryPhaseFraction);
            g.BarBenders = Crew(input.RebarKg, rates.RebarKgPerBenderDay, rates.SteelPhaseFraction);
            g.Carpenters = Crew(input.FormworkM2, rates.FormworkM2PerCarpenterDay, rates.FormworkPhaseFraction);
            g.Mixers = Crew(input.ConcreteM3, rates.ConcreteM3PerMixerDay, rates.ConcretePhaseFraction);

            // Helpers serve the whole site, so they follow the trades they carry
            // for — masons above all.
            g.Helpers = (int)Math.Ceiling(g.Masons * Math.Max(0, rates.HelpersPerMason) - 1e-9);
            return g;
        }

        /// <summary>
        /// Apply the tool rules to a gang. An unusable gang produces NOTHING —
        /// inventing a tool list without a programme would be a fabricated
        /// number wearing the costume of a calculation.
        /// </summary>
        public static List<ToolQuantity> Quantify(GangSizes gangs, IEnumerable<ToolRule> rules, int storeys)
        {
            var outList = new List<ToolQuantity>();
            if (gangs == null || !gangs.IsUsable || rules == null) return outList;

            foreach (var r in rules)
            {
                if (r == null || string.IsNullOrWhiteSpace(r.ToolKey)) continue;
                string driver = (r.Driver ?? "site").Trim().ToLowerInvariant();
                if (!KnownDrivers.Contains(driver)) continue;

                double driverValue;
                if (driver == "site") driverValue = 0;
                else if (driver == "storeys") driverValue = Math.Max(0, storeys - 2);   // above the 2nd
                else
                {
                    driverValue = gangs.Of(driver);
                    // A trade that is not on this project orders no tools for it.
                    if (driverValue <= 0) continue;
                }

                double qty = r.FixedQuantity + Math.Ceiling(driverValue * r.PerDriver - 1e-9);
                qty = Math.Max(qty, r.Minimum);
                if (qty <= 0) continue;

                outList.Add(new ToolQuantity
                {
                    ToolKey = r.ToolKey,
                    Description = string.IsNullOrWhiteSpace(r.Description) ? r.ToolKey : r.Description,
                    SupplierUnit = string.IsNullOrWhiteSpace(r.SupplierUnit) ? "No." : r.SupplierUnit,
                    Quantity = qty
                });
            }
            return outList;
        }
    }
}
