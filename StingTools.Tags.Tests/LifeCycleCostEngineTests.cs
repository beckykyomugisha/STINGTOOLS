using System.Collections.Generic;
using StingTools.Core.Hvac;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Covers the life-cycle cost NPV + escalation + replacement-cycle math
    /// against hand-calculated 3-year toy cases.
    /// </summary>
    public class LifeCycleCostEngineTests
    {
        private static LccOption Opt(string name, double cap, double energy = 0, double maintFlat = 0,
            List<LccReplacement> repl = null)
            => new LccOption
            {
                Name = name, CapitalCost = cap, AnnualEnergyCost = energy,
                AnnualMaintCostFlat = maintFlat, Replacements = repl ?? new List<LccReplacement>()
            };

        // ── Toy case 1: capital 1000 + energy 100/yr, esc 0, disc 10%, 3 yrs ──
        // Nominal:  1000 + 3×100 = 1300
        // NPV:      1000 + 100/1.1 + 100/1.21 + 100/1.331
        //                 = 1000 + 90.9091 + 82.6446 + 75.1315 = 1248.685
        [Fact]
        public void Npv_no_escalation_matches_hand_calc()
        {
            var inputs = new LccInputs
            {
                HorizonYears = 3, EscalationPct = 0, DiscountPct = 10,
                Options = new List<LccOption> { Opt("X", 1000, energy: 100) }
            };
            var r = LifeCycleCostEngine.Compute(inputs);
            var o = r.Options[0];
            Assert.Equal(1300.0, o.TotalNominal, 3);
            Assert.Equal(1248.685, o.TotalNpv, 3);
            // year rows: 0..3 inclusive
            Assert.Equal(4, o.Years.Count);
            Assert.Equal(1.0, o.Years[0].DiscountFactor, 6);
        }

        // ── Toy case 2: add 5% escalation ──
        // Nominal: 1000 + 100 + 105 + 110.25 = 1315.25
        // NPV:     1000 + 100/1.1 + 105/1.21 + 110.25/1.331
        //                = 1000 + 90.9091 + 86.7769 + 82.8324 = 1260.518
        [Fact]
        public void Npv_with_escalation_matches_hand_calc()
        {
            var inputs = new LccInputs
            {
                HorizonYears = 3, EscalationPct = 5, DiscountPct = 10,
                Options = new List<LccOption> { Opt("X", 1000, energy: 100) }
            };
            var o = LifeCycleCostEngine.Compute(inputs).Options[0];
            Assert.Equal(1315.25, o.TotalNominal, 2);
            Assert.Equal(1260.518, o.TotalNpv, 3);
        }

        // ── Toy case 3: replacement at 50% capital every 2 years, no esc/disc ──
        // Year 2 replacement = 0.5 × 1000 = 500 → nominal total 1500
        [Fact]
        public void Replacement_cycle_adds_cost_on_interval()
        {
            var repl = new List<LccReplacement> { new LccReplacement { Component = "compressor", IntervalYears = 2, PctOfCapital = 0.5 } };
            var inputs = new LccInputs
            {
                HorizonYears = 3, EscalationPct = 0, DiscountPct = 0,
                Options = new List<LccOption> { Opt("X", 1000, repl: repl) }
            };
            var o = LifeCycleCostEngine.Compute(inputs).Options[0];
            Assert.Equal(1500.0, o.TotalNominal, 3);
            Assert.Equal(1500.0, o.TotalNpv, 3);   // disc 0 → npv == nominal
            Assert.Equal(500.0, o.Years[2].ReplacementNominal, 3);
            Assert.Equal(0.0, o.Years[1].ReplacementNominal, 3);
            Assert.Equal(0.0, o.Years[3].ReplacementNominal, 3);
        }

        // ── Per-m² maintenance resolves through area ──
        [Fact]
        public void Per_m2_maintenance_uses_area()
        {
            var opt = new LccOption { Name = "M", CapitalCost = 0, AnnualMaintCostPerM2 = 2.0, AreaM2 = 100 };
            Assert.Equal(200.0, opt.AnnualMaintenance, 3);
            var inputs = new LccInputs { HorizonYears = 1, EscalationPct = 0, DiscountPct = 0, Options = new List<LccOption> { opt } };
            var o = LifeCycleCostEngine.Compute(inputs).Options[0];
            Assert.Equal(200.0, o.TotalNominal, 3);
        }

        // ── Crossover: high-capital/low-energy overtakes low-capital/high-energy ──
        [Fact]
        public void Crossover_year_detected_for_two_options()
        {
            // A: cheap capital, dear to run.  B: dear capital, cheap to run.
            var a = Opt("A", 1000, energy: 300);
            var b = Opt("B", 2000, energy: 100);
            var inputs = new LccInputs
            {
                HorizonYears = 10, EscalationPct = 0, DiscountPct = 0,
                Options = new List<LccOption> { a, b }
            };
            var r = LifeCycleCostEngine.Compute(inputs);
            // diff A-B at y0 = -1000; A grows 200/yr faster → crosses when 1000 < 200*y → y=6
            Assert.Equal(6, r.CrossoverYearNominal);
        }

        [Fact]
        public void Empty_options_is_safe()
        {
            var r = LifeCycleCostEngine.Compute(new LccInputs { HorizonYears = 40 });
            Assert.Empty(r.Options);
            Assert.Equal(-1, r.CrossoverYearNominal);
        }
    }
}
