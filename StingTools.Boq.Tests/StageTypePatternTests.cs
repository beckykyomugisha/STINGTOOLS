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
    /// MAT-SCHED — an element's CATEGORY often says nothing about the work it
    /// belongs to. The second real export filed
    /// `2022_RoofCap_Eagle_HighProfileTiles` and `gate_16438` under
    /// SUPERSTRUCTURE, because that is where their categories point.
    ///
    /// A constituent kind must still win: a roof-shaped NAME must not drag real
    /// concrete out of the frame.
    /// </summary>
    public class StageTypePatternTests
    {
        private static List<StageDefinition> Defs() => new List<StageDefinition>
        {
            new StageDefinition { StageId = "superstructure", Order = 2,
                ConstituentKinds = { "concrete", "blockwork" }, Categories = { "Walls", "Generic Models" } },
            new StageDefinition { StageId = "roof", Order = 3,
                Categories = { "Roofs" }, TypePatterns = { "roofcap", "roofing" } },
            new StageDefinition { StageId = "external", Order = 8,
                TypePatterns = { "gate", "fence" } },
        };

        [Fact]
        public void A_Type_Pattern_Beats_The_Category()
        {
            var ix = StageIndex.Build(Defs(), "superstructure");

            Assert.Equal("roof", ix.Resolve("", "Generic Models", "", "2022_RoofCap_Eagle_HighProfileTiles"));
            Assert.Equal("external", ix.Resolve("", "Generic Models", "", "gate_16438 gate"));
        }

        [Fact]
        public void A_Constituent_Kind_Still_Beats_A_Type_Pattern()
        {
            // A concrete roof slab carries kind "concrete" and belongs to the
            // frame however its type is named.
            var ix = StageIndex.Build(Defs(), "superstructure");

            Assert.Equal("superstructure", ix.Resolve("concrete", "Roofs", "", "Roofing slab RC 225"));
        }

        [Fact]
        public void No_Type_Text_Behaves_Exactly_As_Before()
        {
            var ix = StageIndex.Build(Defs(), "superstructure");

            Assert.Equal(ix.Resolve("", "Roofs", ""), ix.Resolve("", "Roofs", "", null));
            Assert.Equal("superstructure", ix.Resolve("", "Generic Models", "", null));
        }

        [Fact]
        public void A_Blank_Pattern_Is_Inert_Not_A_Wildcard()
        {
            var defs = Defs();
            defs[2].TypePatterns.Add("   ");
            var ix = StageIndex.Build(defs, "superstructure");

            Assert.Equal("superstructure", ix.Resolve("", "Walls", "", "anything at all"));
        }

        // ── shipped data ──────────────────────────────────────────────────

        private static StageLibrary Stages() =>
            JsonConvert.DeserializeObject<StageLibrary>(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "STING_MATERIAL_STAGES.json")));

        [Fact]
        public void Shipped_Intermediates_Name_Kinds_That_Differ_From_Their_Children()
        {
            var lib = Stages();
            Assert.NotEmpty(lib.IntermediateMeasures);

            foreach (var r in lib.IntermediateMeasures)
            {
                Assert.False(string.IsNullOrWhiteSpace(r.Kind));
                Assert.NotEmpty(r.Children);
                Assert.DoesNotContain(r.Children, c =>
                    string.Equals(c, r.Kind, StringComparison.OrdinalIgnoreCase));
            }
        }

        [Fact]
        public void A_Protected_Category_Is_Never_Also_An_Excluded_One()
        {
            // Otherwise the protection is decorative: the category check runs
            // first and drops the row before any pattern is consulted.
            var lib = Stages();
            Assert.NotEmpty(lib.ExclusionProtectedCategories);

            foreach (string c in lib.ExclusionProtectedCategories)
                Assert.DoesNotContain(lib.ExcludedCategories,
                    x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void No_Stage_Routes_A_Category_That_Is_Excluded_Outright()
        {
            // ELEMENT 08 listed "Site" and "Parking", both of which are in
            // excludedCategories and are dropped before routing — so the stage
            // advertised coverage it could never deliver. Same class as the
            // unreachable supplier rules removed in MATSCHED-3b.
            var lib = Stages();
            var excluded = new HashSet<string>(lib.ExcludedCategories, StringComparer.OrdinalIgnoreCase);

            var dead = lib.Stages
                .SelectMany(s => (s.Categories ?? new List<string>()).Select(c => $"{s.StageId}:{c}"))
                .Where(pair => excluded.Contains(pair.Split(':')[1]))
                .ToList();

            Assert.True(dead.Count == 0, "stages route categories that are excluded outright: " + string.Join(", ", dead));
        }
    }
}
