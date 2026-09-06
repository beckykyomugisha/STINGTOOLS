using System.Collections.Generic;
using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — routing is by CONSTITUENT KIND, not by element: a wall sends
    /// blockwork to its storey stage and plaster to Finishes, which is why the
    /// PATMAC Finishes section carries its own 600 bags of cement.
    ///
    /// Letters are assigned, never authored, so PATMAC defects D1 and D2 cannot
    /// recur.
    /// </summary>
    public class StageMapperTests
    {
        private static List<StageDefinition> Defs() => new List<StageDefinition>
        {
            new StageDefinition { StageId = "tools",        Title = "TOOLS AND EQUIPMENT", Order = 10 },
            new StageDefinition { StageId = "substructure", Title = "SUB-STRUCTURE",       Order = 20,
                                  Categories = { "Structural Foundations" } },
            new StageDefinition { StageId = "superstructure", Title = "SUPERSTRUCTURE",    Order = 30,
                                  ConstituentKinds = { "blockwork", "mortar_cement", "mortar_sand", "concrete", "rebar" } },
            new StageDefinition { StageId = "finishes",     Title = "FINISHES",            Order = 40,
                                  ConstituentKinds = { "plaster", "plaster_cement", "plaster_sand" } }
        };

        [Fact]
        public void Plaster_Constituents_Route_To_Finishes_Not_To_The_Storey()
        {
            string stage = StageMapper.ResolveStageId(
                constituentKind: "plaster_cement", category: "Walls",
                levelCode: "L01", defs: Defs(), defaultStageId: "superstructure");

            Assert.Equal("finishes", stage);
        }

        [Fact]
        public void Blockwork_From_The_Same_Wall_Routes_To_The_Superstructure()
        {
            string stage = StageMapper.ResolveStageId(
                constituentKind: "blockwork", category: "Walls",
                levelCode: "L01", defs: Defs(), defaultStageId: "superstructure");

            Assert.Equal("superstructure", stage);
        }

        [Fact]
        public void Category_Routing_Applies_When_There_Is_No_Constituent_Kind()
        {
            string stage = StageMapper.ResolveStageId(
                constituentKind: null, category: "Structural Foundations",
                levelCode: "GF", defs: Defs(), defaultStageId: "superstructure");

            Assert.Equal("substructure", stage);
        }

        [Fact]
        public void An_Unmatched_Row_Goes_To_The_Named_Default_Never_Vanishes()
        {
            string stage = StageMapper.ResolveStageId(
                constituentKind: "something_new", category: "Casework",
                levelCode: "L02", defs: Defs(), defaultStageId: "superstructure");

            Assert.Equal("superstructure", stage);
        }

        [Fact]
        public void Letters_Are_Sequential_And_Unique()
        {
            var stages = new List<StageSection>
            {
                new StageSection { StageId = "tools" },
                new StageSection { StageId = "substructure" },
                new StageSection { StageId = "superstructure" },
                new StageSection { StageId = "finishes" }
            };

            StageMapper.AssignLetters(stages);

            Assert.Equal(new[] { "A", "B", "C", "D" }, stages.Select(s => s.Letter).ToArray());
            Assert.Equal(stages.Count, stages.Select(s => s.Letter).Distinct().Count());
        }

        [Fact]
        public void Lettering_Survives_More_Than_Twenty_Six_Stages()
        {
            var stages = Enumerable.Range(0, 28)
                .Select(i => new StageSection { StageId = $"s{i}" }).ToList();

            StageMapper.AssignLetters(stages);

            Assert.Equal("Z", stages[25].Letter);
            Assert.Equal("AA", stages[26].Letter);
            Assert.Equal("AB", stages[27].Letter);
            Assert.Equal(28, stages.Select(s => s.Letter).Distinct().Count());
        }

        [Fact]
        public void The_Summary_Is_Projected_From_The_Body_So_It_Cannot_Diverge()
        {
            var doc = new MaterialScheduleDocument();
            doc.Stages.Add(new StageSection { StageId = "a", Title = "ALPHA" });
            doc.Stages.Add(new StageSection { StageId = "b", Title = "BETA" });
            StageMapper.AssignLetters(doc.Stages);

            var summary = doc.Summary.ToList();

            Assert.Equal(doc.Stages.Count, summary.Count);
            for (int i = 0; i < summary.Count; i++)
            {
                Assert.Equal(doc.Stages[i].Letter, summary[i].Letter);
                Assert.Equal(doc.Stages[i].Title, summary[i].Title);
            }
        }

        [Fact]
        public void Shipped_Stage_Library_Parses_And_Its_Default_Stage_Exists()
        {
            string path = System.IO.Path.Combine(
                System.AppContext.BaseDirectory, "Data", "STING_MATERIAL_STAGES.json");
            Assert.True(System.IO.File.Exists(path), $"missing shipped file: {path}");

            var lib = Newtonsoft.Json.JsonConvert
                .DeserializeObject<StageLibrary>(System.IO.File.ReadAllText(path));

            Assert.NotNull(lib);
            Assert.NotEmpty(lib!.Stages);
            Assert.False(string.IsNullOrWhiteSpace(lib.DefaultStageId));
            Assert.Contains(lib.Stages, s => s.StageId == lib.DefaultStageId);

            // Order values must be unique — equal Order makes section sequence
            // depend on list order, which is exactly the PATMAC D2 failure.
            Assert.Equal(lib.Stages.Count, lib.Stages.Select(s => s.Order).Distinct().Count());

            // A constituent kind must not route to two stages.
            var allKinds = lib.Stages.SelectMany(s => s.ConstituentKinds ?? new List<string>()).ToList();
            Assert.Equal(allKinds.Count, allKinds.Distinct(System.StringComparer.OrdinalIgnoreCase).Count());
        }
    }
}
