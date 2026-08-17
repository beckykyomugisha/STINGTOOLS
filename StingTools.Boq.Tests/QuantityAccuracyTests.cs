using System.Linq;
using StingTools.BOQ.Takeoff;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — three quantity defects found by cross-checking a real export
    /// against the ratios that produced it.
    ///
    ///  1. "Bricks · No. · 364.31" was 364 SQUARE METRES of brickwork relabelled
    ///     as a brick count. Nothing checked that a rule's sourceUnit matched the
    ///     measured unit, so an area flowed into a piece-count commodity.
    ///  2. blockwork (m²) and units (nr) both mapped to commodity "block", so the
    ///     aggregator ADDED an area to a piece count. The export's block figure
    ///     implied 2,024 m² of wall while its mortar implied 994 m² — both
    ///     derive from the same area, so they could not honestly differ.
    ///  3. Wastage was applied in the engine AND in the supplier-unit rule, so
    ///     blocks carried ~10% and plaster cement ~23% instead of the stated
    ///     5% and 2.5%.
    /// </summary>
    public class QuantityAccuracyTests
    {
        private static MasonryWallInput Wall(bool brick = false) => new MasonryWallInput
        {
            FaceAreaM2 = 100.0,
            IsBrick = brick,
            UnitsPerM2 = brick ? 60 : 12.5,
            UnitWastePct = 5,          // retired — must have no effect
            PlasterFaces = 2,
            PlasterThicknessM = 0.013,
            PlasterWastePct = 20,      // retired — must have no effect
            MortarRatioM3PerM2 = 0.011,
            MortarCementBagsPerM3 = 9,
            MortarSandRatio = 1.25,
            PlasterCementBagsPerM3 = 9,
            PlasterSandRatio = 1.25
        };

        // ── 3. wastage lives in exactly one place ───────────────────────────

        [Fact]
        public void Unit_Counts_Are_Net_Of_Waste()
        {
            // 100 m² × 12.5 = 1,250 blocks. NOT 1,312.5 — the rule adds its 5%.
            var lines = CompoundTakeoff.MasonryWall(Wall());
            Assert.Equal(1250.0, lines.Single(l => l.Kind == "block_units").Quantity, 4);
        }

        [Fact]
        public void Plaster_Derived_Cement_Is_Net_Of_Waste()
        {
            // 200 m² plastered × 0.013 = 2.6 m³ × 9 = 23.4 bags. NOT 28.08.
            var lines = CompoundTakeoff.MasonryWall(Wall());
            Assert.Equal(23.4, lines.Single(l => l.Kind == "plaster_cement").Quantity, 3);
        }

        [Fact]
        public void The_Retired_Waste_Inputs_Have_No_Effect_At_All()
        {
            var withWaste = Wall();
            var without = Wall();
            without.UnitWastePct = 0;
            without.PlasterWastePct = 0;

            var a = CompoundTakeoff.MasonryWall(withWaste);
            var b = CompoundTakeoff.MasonryWall(without);

            foreach (var kind in new[] { "block_units", "plaster_cement", "plaster_sand", "mortar" })
                Assert.Equal(b.Single(l => l.Kind == kind).Quantity,
                             a.Single(l => l.Kind == kind).Quantity, 6);
        }

        // ── 2. bricks are not blocks ────────────────────────────────────────

        [Fact]
        public void A_Brick_Wall_Emits_Brick_Units_Not_Block_Units()
        {
            var lines = CompoundTakeoff.MasonryWall(Wall(brick: true));

            Assert.Contains(lines, l => l.Kind == "brick_units");
            Assert.DoesNotContain(lines, l => l.Kind == "block_units");
        }

        [Fact]
        public void The_Area_Measure_And_The_Piece_Count_Are_Different_Kinds()
        {
            // blockwork is the m² a QS prices; block_units is what a site buys.
            // Merging them added an area to a piece count.
            var lines = CompoundTakeoff.MasonryWall(Wall());

            Assert.Equal("m2", lines.Single(l => l.Kind == "blockwork").Unit);
            Assert.Equal("nr", lines.Single(l => l.Kind == "block_units").Unit);
        }

        [Fact]
        public void The_Shipped_Table_No_Longer_Maps_An_Area_Kind_To_A_Piece_Commodity()
        {
            var table = Newtonsoft.Json.JsonConvert.DeserializeObject<SupplierUnitTable>(
                System.IO.File.ReadAllText(System.IO.Path.Combine(
                    System.AppContext.BaseDirectory, "Data", "STING_SUPPLIER_UNITS.json")));

            foreach (string areaKind in new[] { "blockwork", "brickwork" })
                Assert.DoesNotContain(table.Rules,
                    r => r.MatchKinds.Contains(areaKind, System.StringComparer.OrdinalIgnoreCase));

            Assert.Contains(table.Rules, r => r.CommodityKey == "block"
                && r.MatchKinds.Contains("block_units"));
            Assert.Contains(table.Rules, r => r.CommodityKey == "brick"
                && r.MatchKinds.Contains("brick_units"));
        }

        // ── 1. unlike units cannot convert ──────────────────────────────────

        private static AggregatorInputs Inputs(SupplierUnitTable t, params ConstituentInput[] rows) =>
            new AggregatorInputs
            {
                Constituents = rows.ToList(),
                Units = t,
                StageDefs = { new StageDefinition { StageId = "s", Title = "S", Order = 10 } },
                DefaultStageId = "s",
                Rates = new CommodityRateResolver(
                    new[] { new CommodityRate { CommodityKey = "brick", RateUGX = 400 } }, null)
            };

        private static SupplierUnitTable BrickTable()
        {
            var t = new SupplierUnitTable();
            t.Rules.Add(new SupplierUnitRule
            {
                CommodityKey = "brick", Description = "Bricks", SupplierUnit = "No.",
                SourceUnit = "nr", SourceUnitsPerSupplierUnit = 1.0,
                MatchKinds = { "brick_units", "brickwork" }   // deliberately wrong
            });
            return t;
        }

        [Fact]
        public void A_Square_Metre_Quantity_Cannot_Become_A_Piece_Count()
        {
            // The exact defect: 364 m² of brickwork must not print as 364 bricks.
            var doc = CommodityAggregator.Build(Inputs(BrickTable(),
                new ConstituentInput { ConstituentKind = "brickwork", Description = "Brickwork wall",
                                       Unit = "m2", Quantity = 364.31 }));

            var row = doc.Stages.Single().Commodities.Single();
            Assert.True(row.ConversionBlocked);
            Assert.Equal("m2", row.SupplierUnit);          // kept its measured unit
            Assert.Contains("unlike units", row.ConversionNote);
        }

        [Fact]
        public void A_Matching_Unit_Still_Converts_Normally()
        {
            var doc = CommodityAggregator.Build(Inputs(BrickTable(),
                new ConstituentInput { ConstituentKind = "brick_units", Description = "Bricks",
                                       Unit = "nr", Quantity = 6000 }));

            var row = doc.Stages.Single().Commodities.Single();
            Assert.False(row.ConversionBlocked);
            Assert.Equal("No.", row.SupplierUnit);
            Assert.Equal(6000, row.OrderQuantity);
        }

        [Fact]
        public void A_Rule_Declaring_No_Source_Unit_Is_Trusted()
        {
            // Existing rules that never declared a sourceUnit must keep working
            // rather than being blocked wholesale.
            var t = new SupplierUnitTable();
            t.Rules.Add(new SupplierUnitRule
            {
                CommodityKey = "brick", SupplierUnit = "No.", SourceUnit = "",
                SourceUnitsPerSupplierUnit = 1.0, MatchKinds = { "brick_units" }
            });

            var doc = CommodityAggregator.Build(Inputs(t,
                new ConstituentInput { ConstituentKind = "brick_units", Unit = "nr", Quantity = 10 }));

            Assert.False(doc.Stages.Single().Commodities.Single().ConversionBlocked);
        }

        [Fact]
        public void The_Reconciler_Reports_An_Unlike_Unit_Conversion()
        {
            var doc = CommodityAggregator.Build(Inputs(BrickTable(),
                new ConstituentInput { ConstituentKind = "brickwork", Unit = "m2", Quantity = 364.31 }));

            Assert.Contains(Reconciler.Check(doc).Issues, i => i.Code == "R5");
        }
    }
}
