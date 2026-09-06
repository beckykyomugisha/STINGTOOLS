using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// Matching on the MATERIAL, because type names lie and materials mostly do
    /// not.
    ///
    /// A real roof in a delivered model: type "Generic - 225mm", total thickness
    /// 25 mm, one Structure layer of "Asphalt Shingle". The name was wrong by a
    /// factor of ten and the material was exactly right — and every layer reader
    /// filters on Finish1/Finish2/Substrate/Membrane, so a covering on a
    /// STRUCTURE layer was invisible to all of them and the row fell back to
    /// being named after the type.
    /// </summary>
    public class MaterialMatchTests
    {
        private static SupplierUnitTable Table() => new SupplierUnitTable
        {
            Rules =
            {
                new SupplierUnitRule
                {
                    CommodityKey = "roof-shingle", SupplierUnit = "Bundles", SourceUnit = "m2",
                    SourceUnitsPerSupplierUnit = 3.1, RoundUpToWhole = true, DefaultWastagePct = 10,
                    MatchCategories = { "Roofs" },
                    MatchMaterialPatterns = { "asphalt shingle", "shingle" }
                },
                new SupplierUnitRule
                {
                    CommodityKey = "roof-sheet", SupplierUnit = "No.", SourceUnit = "m2",
                    SourceUnitsPerSupplierUnit = 2.4, RoundUpToWhole = true, DefaultWastagePct = 10,
                    MatchCategories = { "Roofs" },
                    MatchTypePatterns = { "IT4", "corrugat" },
                    MatchMaterialPatterns = { "corrugat" }
                }
            }
        };

        [Fact]
        public void A_Correctly_Materialled_Roof_Converts_Despite_A_Nonsense_Type_Name()
        {
            // The whole point. "Generic - 225mm" matches no pattern anywhere.
            var res = Table().Resolve("", "Roofs", "Generic - 225mm", "Asphalt Shingle");

            Assert.Equal("roof-shingle", res.Rule?.CommodityKey);
        }

        [Fact]
        public void Without_The_Material_That_Same_Roof_Still_Cannot_Convert()
        {
            // Proves the material is what did it, not something else.
            var res = Table().Resolve("", "Roofs", "Generic - 225mm", null);

            Assert.Null(res.Rule);
            Assert.Equal(SupplierUnitMatch.CategoryTypeMismatch, res.Match);
        }

        [Fact]
        public void A_Material_Match_Does_Not_Also_Require_A_Type_Match()
        {
            // Requiring both would leave a correctly-materialled roof with a bad
            // name exactly as stuck as before.
            var res = Table().Resolve("", "Roofs", "nothing matches this", "asphalt shingle roofing");

            Assert.Equal("roof-shingle", res.Rule?.CommodityKey);
        }

        [Fact]
        public void The_Constituent_Kind_Still_Wins_Over_The_Material()
        {
            // A kind is emitted by our own take-off; a material is chosen by
            // whoever built the model. Decreasing reliability, in that order.
            var t = Table();
            t.Rules.Add(new SupplierUnitRule
            {
                CommodityKey = "by-kind", SupplierUnit = "Bags", SourceUnit = "bag",
                SourceUnitsPerSupplierUnit = 1, MatchKinds = { "mortar_cement" }
            });

            var res = t.Resolve("mortar_cement", "Roofs", "x", "Asphalt Shingle");

            Assert.Equal("by-kind", res.Rule?.CommodityKey);
        }

        [Fact]
        public void A_Material_Rule_Bound_To_A_Category_Does_Not_Escape_It()
        {
            // "Asphalt Shingle" on a WALL is not a roof covering.
            var res = Table().Resolve("", "Walls", "x", "Asphalt Shingle");

            Assert.Null(res.Rule);
        }

        [Fact]
        public void A_Material_Rule_With_No_Category_Applies_Anywhere()
        {
            var t = new SupplierUnitTable
            {
                Rules =
                {
                    new SupplierUnitRule
                    {
                        CommodityKey = "anywhere", SupplierUnit = "No.", SourceUnit = "m2",
                        SourceUnitsPerSupplierUnit = 1,
                        MatchMaterialPatterns = { "special-product" }
                    }
                }
            };

            Assert.Equal("anywhere", t.Resolve("", "Anything", "x", "Special-Product 900").Rule?.CommodityKey);
        }

        [Fact]
        public void A_Blank_Material_Changes_Nothing()
        {
            var res = Table().Resolve("", "Roofs", "IT4 sheeting", "");

            Assert.Equal("roof-sheet", res.Rule?.CommodityKey);   // still matched on type
        }

        // ── the arithmetic, stated ──────────────────────────────────────────

        [Theory]
        // 856.28 m² of roof, by covering. Net = area ÷ cover, order = net × (1+waste), rounded up.
        [InlineData("roof-shingle", 3.1,  10.0,  304)]   // 276.2 bundles + 10%
        [InlineData("roof-sheet",   2.4,  10.0,  393)]   // 356.8 sheets  + 10%
        public void The_Conversion_Is_Area_Over_Cover_Plus_Waste(string key, double cover,
                                                                double waste, int expected)
        {
            var rule = new SupplierUnitRule
            {
                CommodityKey = key, SupplierUnit = "No.", SourceUnit = "m2",
                SourceUnitsPerSupplierUnit = cover, RoundUpToWhole = true, DefaultWastagePct = waste
            };

            var r = SupplierUnitConverter.Convert(rule, 856.28);

            Assert.Equal(expected, r.OrderQuantity);
        }

        // ── the shipped file ────────────────────────────────────────────────

        private static SupplierUnitTable Shipped() =>
            JsonConvert.DeserializeObject<SupplierUnitTable>(
                File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory,
                                              "Data", "STING_SUPPLIER_UNITS.json")));

        [Theory]
        [InlineData("Asphalt Shingle",            "roof-shingle")]
        [InlineData("Harvey Tile - Charcoal",     "roof-tile-stonecoated")]
        [InlineData("Decra Roman",                "roof-tile-stonecoated")]
        [InlineData("Clay Tile - Marseille",      "roof-tile-clay")]
        [InlineData("Concrete Tile",              "roof-tile-concrete")]
        [InlineData("Box Profile Steel G28",      "roof-sheet-boxprofile")]
        [InlineData("Corrugated Iron Sheet",      "roof-sheet")]
        public void The_Shipped_Table_Recognises_East_African_Roofing_By_Material(
            string material, string expectedKey)
        {
            // Every one of these is a material a delivered model actually
            // carries. The type name is deliberately absent from the call: the
            // material alone has to be enough.
            var res = Shipped().Resolve("", "Roofs", "Generic - 225mm", material);

            Assert.Equal(expectedKey, res.Rule?.CommodityKey);
        }

        [Fact]
        public void Every_New_Roofing_Rule_Says_Where_Its_Cover_Came_From()
        {
            // A conversion factor multiplies the whole roof. One that cannot say
            // where it came from is a guess wearing a standard's clothes.
            var roofing = Shipped().Rules
                .Where(r => r.CommodityKey.StartsWith("roof-") && r.MatchMaterialPatterns.Count > 0)
                .ToList();

            Assert.NotEmpty(roofing);
            foreach (var r in roofing)
                Assert.False(string.IsNullOrWhiteSpace(r.SourceNote),
                             $"{r.CommodityKey} has no sourceNote");
        }

        [Fact]
        public void Every_New_Roofing_Rule_Tells_The_Reader_To_Confirm_The_Cover()
        {
            var roofing = Shipped().Rules
                .Where(r => r.CommodityKey.StartsWith("roof-") && r.MatchMaterialPatterns.Count > 0
                            && r.SourceUnitsPerSupplierUnit != 1.0);

            foreach (var r in roofing)
                Assert.True(r.SourceNote.IndexOf("confirm", System.StringComparison.OrdinalIgnoreCase) >= 0,
                            $"{r.CommodityKey} does not tell the reader to confirm its cover");
        }
    }
}
