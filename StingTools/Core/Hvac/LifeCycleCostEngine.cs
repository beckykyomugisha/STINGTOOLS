using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Hvac
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 192 (E2) — 40-year HVAC life-cycle cost engine (pure logic).
    //
    // Revit-free so it can be unit-tested. Convention (documented + tested):
    //   • Capital cost is incurred at year 0 (discount factor 1).
    //   • A recurring or replacement cost "in year y" (y = 1..N) is escalated by
    //     (1+esc)^(y-1) — year 1 is the first operating year at base price — and
    //     discounted by 1/(1+disc)^y.
    //   • A replacement item recurs whenever y % intervalYears == 0 (y ≤ N).
    // Both nominal and NPV (discounted) columns are produced.
    // ─────────────────────────────────────────────────────────────────────────

    public class LccReplacement
    {
        public string Component { get; set; } = "";
        public int IntervalYears { get; set; }
        public double PctOfCapital { get; set; }   // 0..1
    }

    public class LccOption
    {
        public string Name { get; set; } = "";
        public double CapitalCost { get; set; }
        public double AnnualEnergyCost { get; set; }
        public double AnnualMaintCostPerM2 { get; set; }
        public double AreaM2 { get; set; }
        /// <summary>Flat annual maintenance — used when AnnualMaintCostPerM2/AreaM2 are 0.</summary>
        public double AnnualMaintCostFlat { get; set; }
        public List<LccReplacement> Replacements { get; set; } = new List<LccReplacement>();

        public double AnnualMaintenance =>
            (AnnualMaintCostPerM2 > 0 && AreaM2 > 0) ? AnnualMaintCostPerM2 * AreaM2 : AnnualMaintCostFlat;
    }

    public class LccInputs
    {
        public int HorizonYears { get; set; } = 40;
        public double EscalationPct { get; set; }   // e.g. 3.0 for 3%
        public double DiscountPct { get; set; }     // e.g. 5.0 for 5%
        public List<LccOption> Options { get; set; } = new List<LccOption>();
    }

    public class LccYearRow
    {
        public int Year;
        public double EnergyNominal;
        public double MaintNominal;
        public double ReplacementNominal;
        public double CapitalNominal;
        public double YearNominal => EnergyNominal + MaintNominal + ReplacementNominal + CapitalNominal;
        public double DiscountFactor;
        public double YearNpv;
        public double CumulativeNominal;
        public double CumulativeNpv;
    }

    public class LccOptionResult
    {
        public string Name = "";
        public List<LccYearRow> Years = new List<LccYearRow>();
        public double TotalNominal => Years.Count > 0 ? Years[Years.Count - 1].CumulativeNominal : 0;
        public double TotalNpv => Years.Count > 0 ? Years[Years.Count - 1].CumulativeNpv : 0;
    }

    public class LccResult
    {
        public LccInputs Inputs;
        public List<LccOptionResult> Options = new List<LccOptionResult>();
        /// <summary>First year the cumulative NPV ordering of the first two options flips
        /// (the lower-capital option overtaking the other), or -1 / 0 if none.</summary>
        public int CrossoverYearNpv;
        public int CrossoverYearNominal;
    }

    public static class LifeCycleCostEngine
    {
        public static LccResult Compute(LccInputs inputs)
        {
            var result = new LccResult { Inputs = inputs };
            double esc = inputs.EscalationPct / 100.0;
            double disc = inputs.DiscountPct / 100.0;
            int n = Math.Max(0, inputs.HorizonYears);

            foreach (var opt in inputs.Options ?? new List<LccOption>())
            {
                var or = new LccOptionResult { Name = opt.Name };
                double cumNom = opt.CapitalCost;   // capital at year 0
                double cumNpv = opt.CapitalCost;   // df = 1 at year 0
                double annualRecurring = opt.AnnualEnergyCost + opt.AnnualMaintenance;

                // Year 0 capital row
                or.Years.Add(new LccYearRow
                {
                    Year = 0,
                    CapitalNominal = opt.CapitalCost,
                    DiscountFactor = 1.0,
                    YearNpv = opt.CapitalCost,
                    CumulativeNominal = cumNom,
                    CumulativeNpv = cumNpv,
                });

                for (int y = 1; y <= n; y++)
                {
                    double escFactor = Math.Pow(1 + esc, y - 1);
                    double energy = opt.AnnualEnergyCost * escFactor;
                    double maint = opt.AnnualMaintenance * escFactor;
                    double replacement = 0;
                    foreach (var r in opt.Replacements ?? new List<LccReplacement>())
                        if (r.IntervalYears > 0 && y % r.IntervalYears == 0)
                            replacement += r.PctOfCapital * opt.CapitalCost * escFactor;

                    double df = 1.0 / Math.Pow(1 + disc, y);
                    var row = new LccYearRow
                    {
                        Year = y,
                        EnergyNominal = energy,
                        MaintNominal = maint,
                        ReplacementNominal = replacement,
                        DiscountFactor = df,
                    };
                    row.YearNpv = row.YearNominal * df;
                    cumNom += row.YearNominal;
                    cumNpv += row.YearNpv;
                    row.CumulativeNominal = cumNom;
                    row.CumulativeNpv = cumNpv;
                    or.Years.Add(row);
                }
                result.Options.Add(or);
            }

            result.CrossoverYearNpv = Crossover(result, npv: true);
            result.CrossoverYearNominal = Crossover(result, npv: false);
            return result;
        }

        /// <summary>First year where the sign of (optionA − optionB) cumulative cost flips. -1 if never.</summary>
        private static int Crossover(LccResult result, bool npv)
        {
            if (result.Options.Count < 2) return -1;
            var a = result.Options[0];
            var b = result.Options[1];
            int count = Math.Min(a.Years.Count, b.Years.Count);
            if (count == 0) return -1;
            double Diff(LccYearRow ra, LccYearRow rb) => npv ? ra.CumulativeNpv - rb.CumulativeNpv : ra.CumulativeNominal - rb.CumulativeNominal;
            double initial = Diff(a.Years[0], b.Years[0]);
            int initialSign = Math.Sign(initial);
            for (int i = 1; i < count; i++)
            {
                int s = Math.Sign(Diff(a.Years[i], b.Years[i]));
                if (s != 0 && initialSign != 0 && s != initialSign) return a.Years[i].Year;
                if (initialSign == 0 && s != 0) { initialSign = s; }
            }
            return -1;
        }
    }
}
