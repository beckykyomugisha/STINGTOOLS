using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — the first real export sold an OPENING 1,187 times.
    /// `M_GM_OpeningWall_Instance — Opening` is a void; there is nothing to buy.
    /// Alongside it came muntin patterns, window trims and a wardrobe.
    ///
    /// Category exclusion could not catch these: they are all `Generic Models`,
    /// which legitimately holds real building elements in other projects, so
    /// excluding the whole category would hide genuine work. The discriminator
    /// is the description, not the category.
    /// </summary>
    public class DescriptionExclusionTests
    {
        private static AggregatorInputs Inputs(string[] patterns, params ConstituentInput[] rows) =>
            new AggregatorInputs
            {
                Constituents = rows.ToList(),
                Units = new SupplierUnitTable(),
                StageDefs = { new StageDefinition { StageId = "superstructure", Title = "SUPERSTRUCTURE", Order = 10 } },
                DefaultStageId = "superstructure",
                ExcludedDescriptionPatterns = patterns.ToList(),
                Rates = new CommodityRateResolver(null, null)
            };

        private static ConstituentInput Row(string description, string typeName = "") =>
            new ConstituentInput
            {
                Category = "Generic Models", Description = description,
                TypeName = typeName, Unit = "each", Quantity = 1
            };

        [Fact]
        public void An_Opening_Is_Not_A_Material()
        {
            var doc = CommodityAggregator.Build(Inputs(
                new[] { "opening" },
                Row("M_GM_OpeningWall_Instance — Opening"),
                Row("Precast lintel 900mm")));

            var kept = doc.Stages.SelectMany(s => s.Commodities).Select(c => c.Description).ToList();
            Assert.DoesNotContain(kept, d => d.Contains("Opening"));
            Assert.Contains("Precast lintel 900mm", kept);
        }

        [Fact]
        public void Pattern_Exclusions_Are_Counted_Like_Category_Ones()
        {
            var doc = CommodityAggregator.Build(Inputs(
                new[] { "opening", "muntin" },
                Row("M_GM_OpeningWall_Instance — Opening"),
                Row("M_Muntin Pattern_2x2"),
                Row("Precast lintel")));

            Assert.Equal(2, doc.ExcludedRowCount);
        }

        [Fact]
        public void Matching_Is_Case_Insensitive_And_Substring()
        {
            var doc = CommodityAggregator.Build(Inputs(
                new[] { "OPENING" },
                Row("m_gm_openingwall_instance — opening")));

            Assert.Equal(1, doc.ExcludedRowCount);
        }

        [Fact]
        public void The_Type_Name_Is_Checked_As_Well_As_The_Description()
        {
            var doc = CommodityAggregator.Build(Inputs(
                new[] { "opening" },
                Row("Generic model", typeName: "Wall Opening 900x2100")));

            Assert.Equal(1, doc.ExcludedRowCount);
        }

        [Fact]
        public void A_Blank_Pattern_Never_Matches_Everything()
        {
            // "".IndexOf on a haystack returns 0 — the trap that has now bitten
            // this feature twice. A blank pattern must be inert, not a wildcard.
            var doc = CommodityAggregator.Build(Inputs(
                new[] { "", "   " },
                Row("Precast lintel"), Row("Blockwork")));

            Assert.Equal(0, doc.ExcludedRowCount);
            Assert.Equal(2, doc.Stages.SelectMany(s => s.Commodities).Count());
        }

        [Fact]
        public void An_Empty_Pattern_List_Changes_Nothing()
        {
            var doc = CommodityAggregator.Build(Inputs(
                new string[0], Row("M_GM_OpeningWall_Instance — Opening")));

            Assert.Equal(0, doc.ExcludedRowCount);
        }

        [Fact]
        public void The_Shipped_Library_Excludes_Openings()
        {
            var lib = Newtonsoft.Json.JsonConvert.DeserializeObject<StageLibrary>(
                System.IO.File.ReadAllText(System.IO.Path.Combine(
                    System.AppContext.BaseDirectory, "Data", "STING_MATERIAL_STAGES.json")));

            Assert.Contains(lib.ExcludedDescriptionPatterns,
                p => p.Equals("opening", System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void The_Shipped_Patterns_Do_Not_Catch_Real_Materials()
        {
            var lib = Newtonsoft.Json.JsonConvert.DeserializeObject<StageLibrary>(
                System.IO.File.ReadAllText(System.IO.Path.Combine(
                    System.AppContext.BaseDirectory, "Data", "STING_MATERIAL_STAGES.json")));

            // A shipped pattern that matched any of these would silently delete
            // real purchasable work from every bill.
            string[] mustSurvive =
            {
                "Cement (OPC 42.5N)", "Hollow blocks 8\"", "Bricks", "River and pit sand",
                "Steel reinforcement bars", "Sawn timber for formwork", "In-situ concrete",
                "Roofing sheets", "Exterior paint (weather-guard)", "Plaster (2 faces)",
                "900x2400mm wooden door", "1200x1500mm steel casement window"
            };

            foreach (string d in mustSurvive)
                foreach (string p in lib.ExcludedDescriptionPatterns)
                    Assert.False(d.IndexOf(p, System.StringComparison.OrdinalIgnoreCase) >= 0,
                        $"shipped pattern '{p}' would exclude the real material '{d}'");
        }
    }
}
