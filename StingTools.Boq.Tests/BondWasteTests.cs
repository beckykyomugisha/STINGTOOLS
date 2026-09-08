using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.BOQ.Takeoff;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MATERIAL_LOOKUP.csv has carried per-bond cutting waste all along —
    /// stack 3%, stretcher and header 5, garden wall 6, English 7, Flemish 8.
    /// CompoundTakeoffBuilder resolved it per wall into
    /// MasonryWallInput.UnitWastePct, and NOTHING read it. Every wall got the
    /// supplier rule's flat 5, so a Flemish bond was under-ordered by three
    /// points and a stack bond over-ordered by two.
    ///
    /// The fix keeps wastage in exactly ONE place — the supplier rule still
    /// applies it, and this only hands the rule a better number than its flat
    /// default. Applying it in the take-off as well is the double-waste bug
    /// that was removed earlier, and nothing here multiplies by it.
    /// </summary>
    public class BondWasteTests
    {
        private static SupplierUnitRule BrickRule() => new SupplierUnitRule
        {
            CommodityKey = "brick", SupplierUnit = "No.", SourceUnit = "nr",
            SourceUnitsPerSupplierUnit = 1, RoundUpToWhole = true,
            DefaultWastagePct = 5, MatchKinds = { "brick_units" }
        };

        private static AggregatorInputs Inputs(params ConstituentInput[] rows) => new AggregatorInputs
        {
            Constituents = rows.ToList(),
            Units = new SupplierUnitTable { Rules = { BrickRule() } },
            StageDefs = new List<StageDefinition>
            { new StageDefinition { StageId = "superstructure", Title = "SUPER", Order = 10 } },
            DefaultStageId = "superstructure",
            Rates = new CommodityRateResolver(
                new List<CommodityRate> { new CommodityRate { CommodityKey = "brick", RateUGX = 400 } }, null)
        };

        private static ConstituentInput Bricks(double nr, double waste) => new ConstituentInput
        {
            ConstituentKind = "brick_units", Category = "Walls", Unit = "nr",
            Quantity = nr, Description = "Bricks", WastePctOverride = waste
        };

        private static MaterialCommodity Only(MaterialScheduleDocument d) =>
            d.Stages.SelectMany(s => s.Commodities).Single(c => c.CommodityKey == "brick");

        // ── the override reaches the conversion ─────────────────────────────

        [Fact]
        public void A_Flemish_Bond_Orders_Its_OWN_Eight_Percent_Not_The_Rules_Five()
        {
            var doc = CommodityAggregator.Build(Inputs(Bricks(1000, 8)));

            Assert.Equal(8, Only(doc).WastagePct);
            Assert.Equal(1080, Only(doc).OrderQuantity);
        }

        [Fact]
        public void A_Stack_Bond_Orders_Three_Percent_Not_Five()
        {
            // The other direction: the flat default OVER-ordered this one.
            var doc = CommodityAggregator.Build(Inputs(Bricks(1000, 3)));

            Assert.Equal(1030, Only(doc).OrderQuantity);
        }

        [Fact]
        public void A_Row_With_No_Override_Still_Gets_The_Rules_Default()
        {
            // -1 means "the rule's default is fine", NOT "no waste". Conflating
            // them would silently drop the allowance on every row with no
            // variant to speak of, which is most rows.
            var doc = CommodityAggregator.Build(Inputs(Bricks(1000, -1)));

            Assert.Equal(5, Only(doc).WastagePct);
            Assert.Equal(1050, Only(doc).OrderQuantity);
        }

        [Fact]
        public void A_Zero_Override_Really_Means_Zero()
        {
            // Distinct from -1. A variant that genuinely wastes nothing must be
            // able to say so.
            var doc = CommodityAggregator.Build(Inputs(Bricks(1000, 0)));

            Assert.Equal(0, Only(doc).WastagePct);
            Assert.Equal(1000, Only(doc).OrderQuantity);
        }

        // ── the blend, because rows merge ───────────────────────────────────

        [Fact]
        public void Two_Bonds_In_One_Commodity_Blend_By_QUANTITY()
        {
            // 1,000 bricks at 8% and 3,000 at 4% is not 6% — it is
            // (8×1000 + 4×3000) / 4000 = 5%. The bigger wall moves the figure
            // more, which is what a QS would do by hand.
            var doc = CommodityAggregator.Build(Inputs(Bricks(1000, 8), Bricks(3000, 4)));

            Assert.Equal(5, Only(doc).WastagePct, 3);
        }

        [Fact]
        public void A_Simple_Mean_Would_Be_Wrong_And_This_Proves_It()
        {
            // Same two bonds, sizes swapped. A simple mean gives 6% either way;
            // weighting gives 7% here and 5% above, and only one of those pairs
            // tracks the actual brick counts.
            var doc = CommodityAggregator.Build(Inputs(Bricks(3000, 8), Bricks(1000, 4)));

            Assert.Equal(7, Only(doc).WastagePct, 3);
        }

        [Fact]
        public void A_Row_Without_An_Override_Does_Not_Drag_The_Blend()
        {
            // Excluded from BOTH sides. Counting it at the rule's default would
            // let one unstated row pull a stated blend toward a number nobody
            // chose for it.
            var doc = CommodityAggregator.Build(Inputs(Bricks(1000, 8), Bricks(9000, -1)));

            Assert.Equal(8, Only(doc).WastagePct, 3);
        }

        [Fact]
        public void A_Zero_Quantity_Row_Cannot_Skew_The_Blend()
        {
            var doc = CommodityAggregator.Build(Inputs(Bricks(1000, 8), Bricks(0, 40)));

            Assert.Equal(8, Only(doc).WastagePct, 3);
        }

        // ── the converter, directly ─────────────────────────────────────────

        [Fact]
        public void The_Converter_Treats_Negative_As_Absent_And_Zero_As_Zero()
        {
            var rule = BrickRule();

            Assert.Equal(5, SupplierUnitConverter.Convert(rule, 1000, -1).WastagePct);
            Assert.Equal(0, SupplierUnitConverter.Convert(rule, 1000, 0).WastagePct);
            Assert.Equal(8, SupplierUnitConverter.Convert(rule, 1000, 8).WastagePct);
        }

        [Fact]
        public void The_Old_Two_Argument_Call_Still_Means_Use_The_Default()
        {
            Assert.Equal(5, SupplierUnitConverter.Convert(BrickRule(), 1000).WastagePct);
        }

        // ── the WIRING, not just the shape ──────────────────────────────────

        [Fact]
        public void MasonryWall_Puts_The_Bond_Waste_ON_The_Unit_Line()
        {
            // Every test above builds a ConstituentInput by hand, which proves
            // the blend and the converter and NOTHING about whether the
            // take-off ever sets the override. A mutation deleting that
            // assignment moved no test until this one existed — the same gap
            // found in the aggregator's category collection.
            var lines = CompoundTakeoff.MasonryWall(new MasonryWallInput
            {
                FaceAreaM2 = 10, IsBrick = true, UnitsPerM2 = 60, UnitWastePct = 8
            });

            var units = lines.Single(l => l.Kind == "brick_units");
            Assert.Equal(8, units.WastePctOverride);
        }

        [Fact]
        public void A_Wall_With_No_Stated_Bond_Waste_Leaves_The_Override_Absent()
        {
            // 0 from the lookup would mean "this bond wastes nothing", which is
            // not what an unset input means. It must read as -1, so the rule's
            // default applies rather than zero.
            var lines = CompoundTakeoff.MasonryWall(new MasonryWallInput
            {
                FaceAreaM2 = 10, IsBrick = false, UnitsPerM2 = 12.5, UnitWastePct = 0
            });

            Assert.Equal(-1, lines.Single(l => l.Kind == "block_units").WastePctOverride);
        }

        [Fact]
        public void Other_Lines_Do_Not_Inherit_The_Unit_Waste()
        {
            // Cutting waste on bricks says nothing about mortar or plaster, and
            // handing it to them would replace the cement rule's allowance with
            // a number about a different material entirely.
            var lines = CompoundTakeoff.MasonryWall(new MasonryWallInput
            {
                FaceAreaM2 = 10, IsBrick = true, UnitsPerM2 = 60, UnitWastePct = 8,
                MortarRatioM3PerM2 = 0.02, MortarCementBagsPerM3 = 6, MortarSandRatio = 1
            });

            foreach (var l in lines.Where(l => l.Kind != "brick_units"))
                Assert.Equal(-1, l.WastePctOverride);
        }

        // ── the shipped lookup still states the bonds ───────────────────────

        [Fact]
        public void MATERIAL_LOOKUP_Still_Varies_Waste_By_Bond()
        {
            // If it stopped, the whole override path would be carrying a
            // constant and should be deleted rather than left looking useful.
            var waste = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in File.ReadAllLines(
                         Path.Combine(AppContext.BaseDirectory, "Data", "MATERIAL_LOOKUP.csv")))
            {
                var p = (raw ?? "").Trim().Split(',');
                if (p.Length < 4 || !p[0].Trim().Equals("BRICK_BOND", StringComparison.OrdinalIgnoreCase)) continue;
                if (!p[2].Trim().Equals("WASTE_PCT", StringComparison.OrdinalIgnoreCase)) continue;
                if (double.TryParse(p[3].Trim(), out double v)) waste[p[1].Trim()] = v;
            }

            Assert.True(waste.Count >= 5, "the lookup should describe several bonds");
            Assert.True(waste.Values.Distinct().Count() > 1,
                        "if every bond wasted the same, the flat default would be right");
            Assert.Equal(8, waste["FLEMISH"]);
            Assert.Equal(3, waste["STACK"]);
        }
    }
}
