using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — Roof and Finishes exported in MEASURED units (m²) because
    /// supplier-unit rules matched only on CompoundTakeoff's constituent kind,
    /// and those rows carry none. Unlike cement, they need no decomposition —
    /// roofing sheets, paint and tiles convert straight off the measured area.
    ///
    /// So rules also match on Revit CATEGORY. Category alone is too coarse: a
    /// concrete flat roof and a corrugated-sheet roof are both "Roofs" and buy
    /// completely differently, so a rule may additionally require a type-name
    /// pattern. A row whose category matches but whose type does NOT stays in
    /// measured units and is flagged — never silently converted, never silently
    /// dropped.
    /// </summary>
    public class TradeUnitMatchingTests
    {
        private static SupplierUnitRule SheetRoof() => new SupplierUnitRule
        {
            CommodityKey = "roof-sheet",
            Description = "Roofing sheets",
            SupplierUnit = "No.",
            SourceUnit = "m2",
            SourceUnitsPerSupplierUnit = 2.4,   // m² covered per sheet
            RoundUpToWhole = true,
            DefaultWastagePct = 10,
            MatchCategories = { "Roofs" },
            MatchTypePatterns = { "sheet", "IT4", "corrugated" }
        };

        private static SupplierUnitRule CementByKind() => new SupplierUnitRule
        {
            CommodityKey = "cement",
            Description = "Cement",
            SupplierUnit = "Bags",
            SourceUnit = "bag",
            SourceUnitsPerSupplierUnit = 1.0,
            MatchKinds = { "mortar_cement" }
        };

        private static SupplierUnitRule CeilingPaint() => new SupplierUnitRule
        {
            CommodityKey = "paint-ceiling",
            Description = "Ceiling matt paint",
            SupplierUnit = "Bkts",
            SourceUnit = "m2",
            SourceUnitsPerSupplierUnit = 80.0,  // m² per bucket
            RoundUpToWhole = true,
            MatchCategories = { "Ceilings" }
            // no type patterns — every ceiling is painted
        };

        private static SupplierUnitTable Table()
        {
            var t = new SupplierUnitTable();
            t.Rules.Add(CementByKind());
            t.Rules.Add(SheetRoof());
            t.Rules.Add(CeilingPaint());
            return t;
        }

        // ── resolution ──────────────────────────────────────────────────────

        [Fact]
        public void A_Constituent_Kind_Still_Wins_Regression_Guard()
        {
            var r = Table().Resolve("mortar_cement", "Walls", "Blockwork 200mm");

            Assert.Equal(SupplierUnitMatch.ByKind, r.Match);
            Assert.Equal("cement", r.Rule.CommodityKey);
        }

        [Fact]
        public void Category_Plus_Type_Pattern_Converts()
        {
            var r = Table().Resolve(null, "Roofs", "Corrugated sheet IT4 gauge 28");

            Assert.Equal(SupplierUnitMatch.ByCategory, r.Match);
            Assert.Equal("roof-sheet", r.Rule.CommodityKey);
        }

        [Fact]
        public void Type_Pattern_Matching_Is_Case_Insensitive()
        {
            var r = Table().Resolve(null, "ROOFS", "SHEET ROOF");
            Assert.Equal(SupplierUnitMatch.ByCategory, r.Match);
        }

        [Fact]
        public void Category_Match_With_A_Wrong_Type_Is_Blocked_Not_Converted()
        {
            // A 200mm RC flat roof is category Roofs but is NOT bought in sheets.
            var r = Table().Resolve(null, "Roofs", "RC flat slab 200mm");

            Assert.Equal(SupplierUnitMatch.CategoryTypeMismatch, r.Match);
            Assert.Null(r.Rule);                                  // must not convert
            Assert.Equal("roof-sheet", r.CandidateCommodityKey);  // but says what it nearly was
        }

        [Fact]
        public void A_Rule_With_No_Type_Patterns_Matches_The_Whole_Category()
        {
            var r = Table().Resolve(null, "Ceilings", "Suspended grid 600x600");

            Assert.Equal(SupplierUnitMatch.ByCategory, r.Match);
            Assert.Equal("paint-ceiling", r.Rule.CommodityKey);
        }

        [Fact]
        public void An_Unknown_Category_Simply_Does_Not_Match()
        {
            var r = Table().Resolve(null, "Furniture", "Desk");

            Assert.Equal(SupplierUnitMatch.None, r.Match);
            Assert.Null(r.Rule);
        }

        [Fact]
        public void A_Blank_Type_Name_Cannot_Satisfy_A_Type_Pattern()
        {
            // Empty must not behave like a wildcard — the IndexOf("") trap again.
            var r = Table().Resolve(null, "Roofs", "");

            Assert.Equal(SupplierUnitMatch.CategoryTypeMismatch, r.Match);
            Assert.Null(r.Rule);
        }

        // ── conversion through the aggregator ───────────────────────────────

        private static AggregatorInputs Inputs(params ConstituentInput[] rows) => new AggregatorInputs
        {
            Constituents = rows.ToList(),
            Units = Table(),
            StageDefs = { new StageDefinition { StageId = "roof", Title = "ROOF", Order = 10,
                                                Categories = { "Roofs" } },
                          new StageDefinition { StageId = "finishes", Title = "FINISHES", Order = 20,
                                                Categories = { "Ceilings" } } },
            DefaultStageId = "roof",
            Rates = new CommodityRateResolver(new[]
            {
                new CommodityRate { CommodityKey = "roof-sheet",    RateUGX = 35000 },
                new CommodityRate { CommodityKey = "paint-ceiling", RateUGX = 280000 }
            }, null)
        };

        [Fact]
        public void A_Sheet_Roof_Exports_In_Number_Of_Sheets_Not_Square_Metres()
        {
            // 100 m² ÷ 2.4 = 41.67 net, +10% wastage = 45.83 → 46 sheets
            var doc = CommodityAggregator.Build(Inputs(
                new ConstituentInput { Category = "Roofs", TypeName = "Corrugated sheet IT4",
                                       Description = "Roof", Unit = "m2", Quantity = 100 }));

            var sheet = doc.Stages.Single(s => s.StageId == "roof").Commodities.Single();
            Assert.Equal("No.", sheet.SupplierUnit);
            Assert.Equal(46, sheet.OrderQuantity);
            Assert.Equal(46 * 35000, sheet.AmountUGX);
            Assert.False(sheet.ConversionBlocked);
        }

        [Fact]
        public void A_Concrete_Roof_Stays_In_Square_Metres_And_Is_Flagged()
        {
            var doc = CommodityAggregator.Build(Inputs(
                new ConstituentInput { Category = "Roofs", TypeName = "RC flat slab 200mm",
                                       Description = "Roof", Unit = "m2", Quantity = 100 }));

            var row = doc.Stages.Single(s => s.StageId == "roof").Commodities.Single();
            Assert.Equal("m2", row.SupplierUnit);
            Assert.Equal(100, row.OrderQuantity);
            Assert.True(row.ConversionBlocked);
            Assert.Contains("roof-sheet", row.ConversionNote);
        }

        [Fact]
        public void The_Reconciler_Reports_A_Blocked_Conversion()
        {
            var doc = CommodityAggregator.Build(Inputs(
                new ConstituentInput { Category = "Roofs", TypeName = "RC flat slab 200mm",
                                       Description = "Roof", Unit = "m2", Quantity = 100 }));

            var rec = Reconciler.Check(doc);

            var issue = Assert.Single(rec.Issues, i => i.Code == "R5");
            Assert.Contains("measured units", issue.Message);
        }

        [Fact]
        public void A_Converted_Row_Raises_No_Blocked_Conversion_Issue()
        {
            var doc = CommodityAggregator.Build(Inputs(
                new ConstituentInput { Category = "Ceilings", TypeName = "Suspended grid",
                                       Description = "Ceiling", Unit = "m2", Quantity = 160 }));

            var rec = Reconciler.Check(doc);

            Assert.DoesNotContain(rec.Issues, i => i.Code == "R5");
            Assert.Equal(2, doc.Stages.Single().Commodities.Single().OrderQuantity);  // 160/80
        }

        // ── shipped data ────────────────────────────────────────────────────

        [Fact]
        public void The_Shipped_Table_Now_Covers_Roof_And_Finishes_Commodities()
        {
            string unitsPath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory, "Data", "STING_SUPPLIER_UNITS.json");
            string ratesPath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory, "Data", "STING_COMMODITY_RATES.csv");

            var table = Newtonsoft.Json.JsonConvert
                .DeserializeObject<SupplierUnitTable>(System.IO.File.ReadAllText(unitsPath));
            var rates = CommodityRateResolver.ParseCsv(System.IO.File.ReadAllLines(ratesPath), out _);
            var resolver = new CommodityRateResolver(rates, null);

            foreach (string key in new[] { "roof-sheet", "paint-wall", "paint-ceiling", "floor-tile" })
            {
                var rule = table!.ResolveByCommodityKey(key);
                Assert.True(rule != null, $"no supplier-unit rule for '{key}'");
                Assert.True(rule!.MatchCategories.Count > 0 || rule.MatchKinds.Count > 0,
                    $"'{key}' matches nothing — it can never be reached");
                Assert.True(resolver.Resolve(key).RateUGX > 0, $"'{key}' has no baseline rate");
            }
        }
    }
}
