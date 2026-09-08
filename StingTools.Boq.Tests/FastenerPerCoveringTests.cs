using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// A tiled roof was being quoted screws it does not use.
    ///
    /// STING_CONSUMABLES applied a flat 11 fasteners per m² to every roof
    /// covering. MATERIAL_LOOKUP.csv has said otherwise since long before the
    /// material schedule existed — clay and concrete tile at ZERO, because
    /// tiles are nailed every other course rather than screwed, and four sheet
    /// profiles at four different densities.
    ///
    /// That is wrong by KIND, not by degree, and unlike the coverage figures
    /// (PhysicalConstantDriftTests) it needed no supplier to settle: the data
    /// was already in the repository, stating the opposite, unread.
    /// </summary>
    public class FastenerPerCoveringTests
    {
        private static SupplierUnitTable Shipped() =>
            JsonConvert.DeserializeObject<SupplierUnitTable>(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
                                              "Data", "STING_SUPPLIER_UNITS.json")));

        private static SupplierUnitRule Covering(string key, double density) =>
            new SupplierUnitRule
            {
                CommodityKey = key, SupplierUnit = "No.", SourceUnit = "m2",
                SourceUnitsPerSupplierUnit = 1, MatchCategories = { "Roofs" },
                MatchTypePatterns = { key }, FeedsDriver = "roof_covering_m2",
                FastenersPerM2 = density
            };

        private static ConstituentInput Row(string type, double m2, string unit = "m2") =>
            new ConstituentInput
            {
                ConstituentKind = "", Category = "Roofs", TypeName = type,
                Description = type, Unit = unit, Quantity = m2
            };

        // ── the count, per covering ─────────────────────────────────────────

        [Fact]
        public void A_Sheet_Roof_Counts_Its_Own_Density()
        {
            var d = ConsumableDrivers.From(new[] { Row("roof-sheet", 100) },
                new SupplierUnitTable { Rules = { Covering("roof-sheet", 8) } });

            Assert.Equal(800, d.RoofFastenerNr);
        }

        [Fact]
        public void A_Tiled_Roof_Counts_ZERO_Because_Tiles_Are_Nailed()
        {
            var d = ConsumableDrivers.From(new[] { Row("roof-tile-clay", 100) },
                new SupplierUnitTable { Rules = { Covering("roof-tile-clay", 0) } });

            Assert.Equal(0, d.RoofFastenerNr);
            // The AREA still counts — the covering is real and reported.
            Assert.Equal(100, d.RoofCoveringM2);
        }

        [Fact]
        public void A_MIXED_Roof_Is_The_Case_No_Flat_Ratio_Can_Express()
        {
            // 100 m² of sheet at 8 and 100 m² of tile at 0. A flat 11/m² over
            // the combined 200 m² gives 2,200 fasteners; the truth is 800.
            var table = new SupplierUnitTable
            { Rules = { Covering("roof-sheet", 8), Covering("roof-tile-clay", 0) } };

            var d = ConsumableDrivers.From(
                new[] { Row("roof-sheet", 100), Row("roof-tile-clay", 100) }, table);

            Assert.Equal(800, d.RoofFastenerNr);
            Assert.Equal(200, d.RoofCoveringM2);
        }

        [Fact]
        public void A_Covering_That_States_No_Density_Adds_NOTHING()
        {
            // Not zero-by-default: a roof nobody has measured fixings for is not
            // a roof with zero fixings, and borrowing another covering's density
            // would be a confident number for an unmeasured thing.
            var d = ConsumableDrivers.From(new[] { Row("roof-x", 100) },
                new SupplierUnitTable { Rules = { Covering("roof-x", -1) } });

            Assert.Equal(0, d.RoofFastenerNr);
            Assert.Equal(100, d.RoofCoveringM2);
        }

        [Fact]
        public void The_Unit_Guard_Applies_To_The_Count_Too()
        {
            // 29 ridge caps measured in "each" must not be multiplied by a
            // per-m² density.
            var d = ConsumableDrivers.From(new[] { Row("roof-sheet", 29, "each") },
                new SupplierUnitTable { Rules = { Covering("roof-sheet", 8) } });

            Assert.Equal(0, d.RoofFastenerNr);
            Assert.NotEmpty(d.UnitMismatches);
        }

        [Fact]
        public void An_Unattributed_Covering_Contributes_No_Fasteners()
        {
            // Product unknown, so its density is unknown too. Counting it would
            // pick a density for a covering nobody has identified.
            var d = ConsumableDrivers.From(new[] { Row("Generic - 225mm", 610) },
                new SupplierUnitTable { Rules = { Covering("roof-sheet", 8) } });

            Assert.Equal(0, d.RoofFastenerNr);
            Assert.Equal(610, d.RoofCoveringUnattributedM2);
        }

        // ── the shipped file matches MATERIAL_LOOKUP ────────────────────────

        [Theory]
        [InlineData("roof-sheet", 8.0)]              // ROOF_SHEET CORRUGATED
        [InlineData("roof-sheet-boxprofile", 6.0)]   // ROOF_SHEET BOX_PROFILE
        [InlineData("roof-tile-clay", 0.0)]          // ROOF_SHEET CLAY_TILE
        [InlineData("roof-tile-concrete", 0.0)]      // ROOF_SHEET CONCRETE_TILE
        public void The_Shipped_Densities_Agree_With_MATERIAL_LOOKUP(string key, double expected)
        {
            // The point of the whole exercise: two files stating the same
            // physical fact and finally agreeing. PhysicalConstantDriftTests
            // guards the coverage figures the same way.
            Assert.Equal(expected, Shipped().ResolveByCommodityKey(key).FastenersPerM2);
        }

        [Fact]
        public void Every_Nailed_Covering_Is_Zero_Not_Unstated()
        {
            // -1 (unstated) and 0 (nailed) mean different things and produce
            // different notes. Tiles and shingles are KNOWN to take no screws,
            // so they must say 0.
            var units = Shipped();
            foreach (string k in new[] { "roof-tile", "roof-tile-clay", "roof-tile-concrete",
                                         "roof-tile-stonecoated", "roof-shingle" })
                Assert.Equal(0.0, units.ResolveByCommodityKey(k).FastenersPerM2);
        }

        [Fact]
        public void The_Consumable_Reads_The_COUNT_Not_The_Area()
        {
            var lib = JsonConvert.DeserializeObject<ConsumablesLibrary>(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
                                              "Data", "STING_CONSUMABLES.json")));

            var rule = lib.Rules.Single(r =>
                string.Equals(r.ConstituentKind, "roof_fastener", StringComparison.OrdinalIgnoreCase));

            Assert.Equal("roof_fastener_nr", rule.Driver);
            // 1 by construction: the driver is already the number of fasteners,
            // so any other multiplier would re-ratio an already-counted figure.
            Assert.Equal(1.0, rule.PerDriver);
            Assert.Contains("nailed rather than screwed", rule.SourceNote);
        }
    }
}
