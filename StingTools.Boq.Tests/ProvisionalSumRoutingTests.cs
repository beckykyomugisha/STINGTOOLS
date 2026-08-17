using System.Collections.Generic;
using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — a provisional sum must route through the SAME stage-category
    /// table the model rows use, not by matching its category against section
    /// titles.
    ///
    /// The shipped library routes category "Electrical Equipment" to the stage
    /// titled "ELEMENT 06: ELECTRICAL INSTALLATION". A title match fails on that
    /// pair, so the old code minted a duplicate "ELECTRICAL EQUIPMENT" section at
    /// the end of the document — while the correct routing sat unused in the JSON.
    /// </summary>
    public class ProvisionalSumRoutingTests
    {
        private static List<StageDefinition> Defs() => new List<StageDefinition>
        {
            new StageDefinition { StageId = "substructure", Title = "ELEMENT 01: SUB-STRUCTURE", Order = 20,
                                  Categories = { "Structural Foundations" } },
            new StageDefinition { StageId = "electrical", Title = "ELEMENT 06: ELECTRICAL INSTALLATION", Order = 70,
                                  Categories = { "Electrical Equipment", "Lighting Fixtures" } },
            new StageDefinition { StageId = "external", Title = "ELEMENT 08: EXTERNAL WORKS", Order = 90,
                                  Categories = { "Site" } }
        };

        private static List<StageSection> Built(params string[] stageIds) =>
            stageIds.Select(id => new StageSection
            {
                StageId = id,
                Title = Defs().First(d => d.StageId == id).Title
            }).ToList();

        [Fact]
        public void A_Category_Routes_Through_The_Stage_Table_Not_The_Section_Title()
        {
            // "Electrical Equipment" appears nowhere in "ELEMENT 06: ELECTRICAL
            // INSTALLATION" — a title match would fail here.
            Assert.Equal("electrical",
                ManualRowPlacer.ResolveStageIdForCategory(Defs(), "Electrical Equipment"));
        }

        [Fact]
        public void An_Existing_Section_Is_Reused_Rather_Than_Duplicated()
        {
            var stages = Built("substructure", "electrical");

            var hit = ManualRowPlacer.ResolveSection(stages, Defs(), "Lighting Fixtures");

            Assert.NotNull(hit);
            Assert.Equal("electrical", hit.StageId);
            Assert.Equal(2, stages.Count);   // nothing minted
        }

        [Fact]
        public void A_Blank_Category_Still_Matches_Nothing()
        {
            var stages = Built("substructure", "electrical");
            Assert.Null(ManualRowPlacer.ResolveSection(stages, Defs(), ""));
            Assert.Null(ManualRowPlacer.ResolveSection(stages, Defs(), null));
        }

        [Fact]
        public void An_Unknown_Category_Resolves_Nowhere_So_The_Caller_Mints()
        {
            var stages = Built("substructure");
            Assert.Equal("", ManualRowPlacer.ResolveStageIdForCategory(Defs(), "Landscaping"));
            Assert.Null(ManualRowPlacer.ResolveSection(stages, Defs(), "Landscaping"));
        }

        [Fact]
        public void A_Known_Stage_With_No_Commodities_Is_Inserted_In_Definition_Order()
        {
            // Electrical had no modelled commodities, so the aggregator dropped it.
            // Its provisional sum must still land BEFORE External Works, not after.
            var stages = Built("substructure", "external");
            var section = new StageSection { StageId = "electrical", Title = "ELEMENT 06: ELECTRICAL INSTALLATION" };

            ManualRowPlacer.InsertByDefinitionOrder(stages, Defs(), section);

            Assert.Equal(new[] { "substructure", "electrical", "external" },
                         stages.Select(s => s.StageId).ToArray());
        }

        [Fact]
        public void A_Section_With_No_Definition_Is_Appended_At_The_End()
        {
            var stages = Built("substructure", "external");
            var section = new StageSection { StageId = "ps-landscaping", Title = "LANDSCAPING" };

            ManualRowPlacer.InsertByDefinitionOrder(stages, Defs(), section);

            Assert.Equal("ps-landscaping", stages.Last().StageId);
        }

        [Fact]
        public void Insertion_Keeps_Lettering_Sequential_After_Reassignment()
        {
            var stages = Built("substructure", "external");
            ManualRowPlacer.InsertByDefinitionOrder(stages, Defs(),
                new StageSection { StageId = "electrical", Title = "ELEMENT 06: ELECTRICAL INSTALLATION" });

            StageMapper.AssignLetters(stages);

            Assert.Equal(new[] { "A", "B", "C" }, stages.Select(s => s.Letter).ToArray());
            Assert.Equal("electrical", stages[1].StageId);
        }
    }
}
