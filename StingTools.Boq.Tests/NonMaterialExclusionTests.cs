using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED-8 — the aggregator's "no rule → keep the row in its measured
    /// unit" fallback exists so measured WORK cannot vanish. It also fired on
    /// every unmatched row, so a real export turned `Bed_Double_Nightstands`,
    /// `AD_Floathing TV shelf` and `2D_Chair_&amp;_Ottoman_Accent` into purchasable
    /// commodities: 60 rows of noise, 61 unpriced-commodity issues, grand total
    /// UGX 0.
    ///
    /// Three buckets, not two:
    ///   • converted        — matched a rule, in supplier units
    ///   • awaiting a rate  — a real material with no rule yet (doors, windows,
    ///                        fixtures are in the reference sample and belong)
    ///   • excluded         — not a material at all; dropped, COUNTED, reported
    /// </summary>
    public class NonMaterialExclusionTests
    {
        private static AggregatorInputs Inputs(string[] excluded, params ConstituentInput[] rows) =>
            new AggregatorInputs
            {
                Constituents = rows.ToList(),
                Units = new SupplierUnitTable(),
                StageDefs = { new StageDefinition { StageId = "superstructure", Title = "SUPERSTRUCTURE", Order = 10 } },
                DefaultStageId = "superstructure",
                ExcludedCategories = excluded.ToList(),
                Rates = new CommodityRateResolver(null, null)
            };

        private static ConstituentInput Row(string category, string description) =>
            new ConstituentInput { Category = category, Description = description, Unit = "each", Quantity = 1 };

        [Fact]
        public void An_Excluded_Category_Never_Becomes_A_Commodity()
        {
            var doc = CommodityAggregator.Build(Inputs(
                new[] { "Furniture" },
                Row("Furniture", "Bed_Double_Nightstands"),
                Row("Walls", "Blockwork")));

            var all = doc.Stages.SelectMany(s => s.Commodities).Select(c => c.Description).ToList();
            Assert.DoesNotContain("Bed_Double_Nightstands", all);
            Assert.Contains("Blockwork", all);
        }

        [Fact]
        public void Exclusions_Are_Counted_Not_Silently_Dropped()
        {
            // Silence is how measured work disappears unnoticed. The count is the
            // difference between "excluded on purpose" and "lost".
            var doc = CommodityAggregator.Build(Inputs(
                new[] { "Furniture", "Entourage" },
                Row("Furniture", "Chair"),
                Row("Furniture", "Desk"),
                Row("Entourage", "Person"),
                Row("Walls", "Blockwork")));

            Assert.Equal(3, doc.ExcludedRowCount);
            Assert.Equal(2, doc.ExcludedByCategory["Furniture"]);
            Assert.Equal(1, doc.ExcludedByCategory["Entourage"]);
        }

        [Fact]
        public void Exclusion_Is_Case_Insensitive()
        {
            var doc = CommodityAggregator.Build(Inputs(
                new[] { "furniture" },
                Row("Furniture", "Chair")));

            Assert.Equal(1, doc.ExcludedRowCount);
            Assert.Empty(doc.Stages.SelectMany(s => s.Commodities));
        }

        [Fact]
        public void A_Real_Material_Without_A_Rule_Is_Kept_Not_Excluded()
        {
            // Doors and windows have no supplier-unit rule yet — their rates are
            // per-type project data — but they ARE bought, and the reference
            // sample gives them their own section. They must survive.
            var doc = CommodityAggregator.Build(Inputs(
                new[] { "Furniture" },
                Row("Doors", "900x2400mm wooden door"),
                Row("Windows", "1200x1500mm steel casement")));

            var kept = doc.Stages.SelectMany(s => s.Commodities).Select(c => c.Description).ToList();
            Assert.Contains("900x2400mm wooden door", kept);
            Assert.Contains("1200x1500mm steel casement", kept);
            Assert.Equal(0, doc.ExcludedRowCount);
        }

        [Fact]
        public void An_Empty_Exclusion_List_Changes_Nothing()
        {
            var doc = CommodityAggregator.Build(Inputs(
                new string[0],
                Row("Furniture", "Chair")));

            Assert.Equal(0, doc.ExcludedRowCount);
            Assert.Single(doc.Stages.SelectMany(s => s.Commodities));
        }

        [Fact]
        public void The_Shipped_Library_Excludes_The_Categories_That_Broke_The_Real_Export()
        {
            var lib = Newtonsoft.Json.JsonConvert.DeserializeObject<StageLibrary>(
                System.IO.File.ReadAllText(System.IO.Path.Combine(
                    System.AppContext.BaseDirectory, "Data", "STING_MATERIAL_STAGES.json")));

            foreach (string c in new[] { "Furniture", "Furniture Systems", "Entourage", "Planting" })
                Assert.Contains(c, lib.ExcludedCategories, System.StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_Shipped_Library_Does_Not_Exclude_Anything_A_Site_Team_Buys()
        {
            var lib = Newtonsoft.Json.JsonConvert.DeserializeObject<StageLibrary>(
                System.IO.File.ReadAllText(System.IO.Path.Combine(
                    System.AppContext.BaseDirectory, "Data", "STING_MATERIAL_STAGES.json")));

            foreach (string c in new[] { "Doors", "Windows", "Walls", "Floors", "Roofs",
                                         "Plumbing Fixtures", "Electrical Fixtures", "Ceilings" })
                Assert.DoesNotContain(c, lib.ExcludedCategories, System.StringComparer.OrdinalIgnoreCase);
        }
    }
}
