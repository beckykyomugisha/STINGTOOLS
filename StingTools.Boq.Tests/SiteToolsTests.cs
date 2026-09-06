using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED-9 — site tools.
    ///
    /// HONESTY FIRST: there is no published standard for this. NRM2 puts tools
    /// and plant in PRELIMINARIES, priced as a lump or a percentage — there is no
    /// "wheelbarrows per mason" rule anywhere in the measurement standards. What
    /// follows is East African contractor practice, expressed as an editable
    /// table and calibrated against the PATMAC reference sample. It must never be
    /// presented as a code-derived figure.
    ///
    /// The chain is work → trade-days → gang → tools. Every step is measured or
    /// declared; nothing is a magic constant in code.
    /// </summary>
    public class SiteToolsTests
    {
        private static SiteToolsInput Patmac() => new SiteToolsInput
        {
            // Roughly the PATMAC mall: ~2,000 m² of walling, a modest frame,
            // built over a 6-month programme.
            BlockworkM2 = 2000,
            RebarKg = 12000,
            FormworkM2 = 900,
            ConcreteM3 = 160,
            DurationDays = 180,
            Storeys = 3
        };

        // ── gangs ───────────────────────────────────────────────────────────

        [Fact]
        public void Gang_Sizes_Come_From_Work_Divided_By_Productivity_And_Time()
        {
            // 2,000 m² ÷ 8 m² per mason-day = 250 mason-days.
            // Walling runs over 40% of a 180-day programme = 72 days.
            // 250 ÷ 72 = 3.47 → 4 masons.
            var g = SiteToolsCalculator.DeriveGangs(Patmac(), TradeRates.Default());

            Assert.Equal(4, g.Masons);
            Assert.Equal(6, g.Helpers);       // 4 × 1.5
        }

        [Fact]
        public void A_Longer_Programme_Needs_A_Smaller_Gang()
        {
            var slow = Patmac();
            slow.DurationDays = 360;

            var fast = SiteToolsCalculator.DeriveGangs(Patmac(), TradeRates.Default());
            var slower = SiteToolsCalculator.DeriveGangs(slow, TradeRates.Default());

            Assert.True(slower.Masons < fast.Masons,
                $"360 days should need fewer masons than 180: {slower.Masons} vs {fast.Masons}");
        }

        [Fact]
        public void A_Project_With_No_Programme_Cannot_Guess_A_Gang()
        {
            // Duration is the denominator. Without it the whole model is
            // meaningless, so it reports nothing rather than inventing a crew.
            var noDuration = Patmac();
            noDuration.DurationDays = 0;

            var g = SiteToolsCalculator.DeriveGangs(noDuration, TradeRates.Default());

            Assert.False(g.IsUsable);
            Assert.Equal(0, g.Masons);
        }

        [Fact]
        public void No_Measured_Work_Means_No_Gang_For_That_Trade()
        {
            var noSteel = Patmac();
            noSteel.RebarKg = 0;

            Assert.Equal(0, SiteToolsCalculator.DeriveGangs(noSteel, TradeRates.Default()).BarBenders);
        }

        // ── tools ───────────────────────────────────────────────────────────

        private static ToolRule Rule(string key, string driver, double per, double fixedQty = 0, double min = 0)
            => new ToolRule
            {
                ToolKey = key, Description = key, SupplierUnit = "No.",
                Driver = driver, PerDriver = per, FixedQuantity = fixedQty, Minimum = min
            };

        [Fact]
        public void A_Per_Helper_Tool_Scales_With_The_Helper_Gang()
        {
            var g = new GangSizes { Helpers = 6, IsUsable = true };
            var tools = SiteToolsCalculator.Quantify(g, new[] { Rule("spade", "helpers", 1.0) }, storeys: 1);

            Assert.Equal(6, tools.Single().Quantity);
        }

        [Fact]
        public void A_Shared_Tool_Rounds_Up_So_Nobody_Waits()
        {
            // One barrow per three helpers: 6 helpers → 2. A part barrow is not
            // a thing, and rounding down leaves a labourer idle.
            var g = new GangSizes { Helpers = 7, IsUsable = true };
            var tools = SiteToolsCalculator.Quantify(g, new[] { Rule("wheelbarrow", "helpers", 1.0 / 3.0) }, storeys: 1);

            Assert.Equal(3, tools.Single().Quantity);   // ceil(7/3)
        }

        [Fact]
        public void A_Fixed_Site_Tool_Does_Not_Scale()
        {
            var g = new GangSizes { Helpers = 40, IsUsable = true };
            var tools = SiteToolsCalculator.Quantify(g, new[] { Rule("sledge", "site", 0, fixedQty: 1) }, storeys: 1);

            Assert.Equal(1, tools.Single().Quantity);
        }

        [Fact]
        public void A_Storey_Driven_Tool_Follows_The_Building()
        {
            // One water tank, plus one per storey above the second.
            var g = new GangSizes { Helpers = 6, IsUsable = true };
            var rule = Rule("water-tank", "storeys", 1.0, fixedQty: 1);

            // 1 storey → just the base tank. 3 storeys → base + 1 for the storey
            // above the second. My first version of this test asserted 2 and 4,
            // which contradicted the sentence above it.
            Assert.Equal(1, SiteToolsCalculator.Quantify(g, new[] { rule }, storeys: 1).Single().Quantity);
            Assert.Equal(2, SiteToolsCalculator.Quantify(g, new[] { rule }, storeys: 3).Single().Quantity);
        }

        [Fact]
        public void A_Minimum_Applies_Even_To_A_Tiny_Gang()
        {
            var g = new GangSizes { Helpers = 1, IsUsable = true };
            var tools = SiteToolsCalculator.Quantify(g, new[] { Rule("spade", "helpers", 1.0, min: 4) }, storeys: 1);

            Assert.Equal(4, tools.Single().Quantity);
        }

        [Fact]
        public void A_Tool_Whose_Driver_Is_Absent_Is_Not_Ordered()
        {
            // No steel on site means no bar-bending gang, so no hacksaws.
            var g = new GangSizes { Helpers = 6, BarBenders = 0, IsUsable = true };
            var tools = SiteToolsCalculator.Quantify(g, new[] { Rule("hacksaw", "barbenders", 0.5) }, storeys: 1);

            Assert.Empty(tools);
        }

        [Fact]
        public void An_Unusable_Gang_Produces_No_Tools_At_All()
        {
            var tools = SiteToolsCalculator.Quantify(new GangSizes { IsUsable = false },
                new[] { Rule("spade", "helpers", 1.0, fixedQty: 2) }, storeys: 3);

            Assert.Empty(tools);
        }

        // ── calibration against the reference sample ────────────────────────

        [Fact]
        public void The_Shipped_Rules_Reproduce_The_Reference_Sample_Within_Reason()
        {
            // The PATMAC schedule listed 4 wheelbarrows, 8 spades, 10 jerrycans
            // and 3 water tanks. These are practice heuristics, not a standard,
            // so the test allows a sensible band rather than pretending to an
            // exactness nobody can justify — but a model that produced 1 barrow
            // or 40 spades would be wrong and this catches it.
            var lib = Newtonsoft.Json.JsonConvert.DeserializeObject<SiteToolsLibrary>(
                System.IO.File.ReadAllText(System.IO.Path.Combine(
                    System.AppContext.BaseDirectory, "Data", "STING_SITE_TOOLS.json")));

            var gangs = SiteToolsCalculator.DeriveGangs(Patmac(), lib.TradeRates);
            var tools = SiteToolsCalculator.Quantify(gangs, lib.Rules, Patmac().Storeys);

            double Q(string key) => tools.FirstOrDefault(t => t.ToolKey == key)?.Quantity ?? 0;

            Assert.InRange(Q("wheelbarrow"), 2, 6);
            Assert.InRange(Q("spade"), 5, 12);
            Assert.InRange(Q("jerrycan"), 5, 14);
            Assert.InRange(Q("water-tank"), 2, 5);
            Assert.InRange(Q("sledge-hammer"), 1, 2);
        }

        [Fact]
        public void Every_Shipped_Rule_Names_A_Driver_The_Calculator_Understands()
        {
            var lib = Newtonsoft.Json.JsonConvert.DeserializeObject<SiteToolsLibrary>(
                System.IO.File.ReadAllText(System.IO.Path.Combine(
                    System.AppContext.BaseDirectory, "Data", "STING_SITE_TOOLS.json")));

            foreach (var r in lib.Rules)
                Assert.True(SiteToolsCalculator.IsKnownDriver(r.Driver),
                    $"tool '{r.ToolKey}' names unknown driver '{r.Driver}' — it would silently never be ordered");
        }
    }
}
