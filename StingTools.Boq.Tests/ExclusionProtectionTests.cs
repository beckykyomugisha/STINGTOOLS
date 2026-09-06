using System.Collections.Generic;
using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — the second real export dropped ONE Windows row as
    /// not-a-material while keeping twelve others. A description pattern written
    /// for Generic Models voids ("opening") matched a real window type.
    ///
    /// A door or a window is always bought, whatever it is called, so the
    /// category outranks the pattern.
    /// </summary>
    public class ExclusionProtectionTests
    {
        private static AggregatorInputs Inputs(params ConstituentInput[] rows)
        {
            var a = new AggregatorInputs
            {
                Constituents = rows.ToList(),
                DefaultStageId = "superstructure",
                StageDefs = new List<StageDefinition>
                {
                    new StageDefinition { StageId = "superstructure", Title = "SUPERSTRUCTURE", Order = 2,
                        Categories = { "Walls", "Generic Models" } },
                    new StageDefinition { StageId = "doors-windows", Title = "DOORS AND WINDOWS", Order = 4,
                        Categories = { "Doors", "Windows" } },
                },
                ExcludedDescriptionPatterns = new List<string> { "opening" },
                ExclusionProtectedCategories = new List<string> { "Doors", "Windows" },
            };
            return a;
        }

        [Fact]
        public void A_Protected_Category_Survives_A_Matching_Pattern()
        {
            var doc = CommodityAggregator.Build(Inputs(new ConstituentInput
            {
                Category = "Windows", TypeName = "M_Window-Awning OpeningLight 600x750",
                Description = "Awning window", Unit = "nr", Quantity = 4
            }));

            Assert.Equal(0, doc.ExcludedRowCount);
            Assert.Contains(doc.Stages.SelectMany(s => s.Commodities), c => c.Description == "Awning window");
        }

        [Fact]
        public void An_Unprotected_Category_Is_Still_Excluded_By_The_Same_Pattern()
        {
            // The pattern exists because a Generic Models OPENING was sold 1,187
            // times. Protecting doors and windows must not disarm it.
            var doc = CommodityAggregator.Build(Inputs(new ConstituentInput
            {
                Category = "Generic Models", TypeName = "M_GM_OpeningWall_Instance",
                Description = "Opening", Unit = "nr", Quantity = 1187
            }));

            Assert.Equal(1, doc.ExcludedRowCount);
            Assert.Empty(doc.Stages.SelectMany(s => s.Commodities));
        }

        [Fact]
        public void The_Aggregator_Marks_Intermediates_End_To_End()
        {
            var input = Inputs(
                new ConstituentInput { ConstituentKind = "blockwork", Category = "Walls",
                    Description = "Blockwork wall", Unit = "m2", Quantity = 174.6 },
                new ConstituentInput { ConstituentKind = "block_units", Category = "Walls",
                    Description = "Blocks", Unit = "nr", Quantity = 2182.53 });
            input.Units = new SupplierUnitTable
            {
                Rules =
                {
                    new SupplierUnitRule { CommodityKey = "block", Description = "Hollow blocks 8\"",
                        SupplierUnit = "No.", SourceUnit = "nr", SourceUnitsPerSupplierUnit = 1,
                        RoundUpToWhole = true, DefaultWastagePct = 5, MatchKinds = { "block_units" } }
                }
            };
            input.IntermediateMeasures = new List<IntermediateMeasureRule>
            {
                new IntermediateMeasureRule { Kind = "blockwork", Children = { "block_units" } }
            };

            var doc = CommodityAggregator.Build(input);
            var all = doc.Stages.SelectMany(s => s.Commodities).ToList();

            Assert.True(all.Single(c => c.Description == "Blockwork wall").IsMemorandum);
            Assert.False(all.Single(c => c.CommodityKey == "block").IsMemorandum);
        }
    }
}
