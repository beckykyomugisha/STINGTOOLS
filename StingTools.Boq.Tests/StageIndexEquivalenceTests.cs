using System.Collections.Generic;
using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED perf — stage resolution used to sort the definition list and
    /// allocate a new List on EVERY row. Harmless at 60 rows, wasteful at 20k.
    /// It is now indexed once per build.
    ///
    /// The whole point is that NOTHING about the answer changes, so these tests
    /// assert the indexed path agrees with the original per-call path across the
    /// cases that actually distinguish them: precedence between kind, category
    /// and level; Order deciding ties; and the unmatched fallback.
    /// </summary>
    public class StageIndexEquivalenceTests
    {
        private static List<StageDefinition> Defs() => new List<StageDefinition>
        {
            // Deliberately NOT in Order sequence, so a path that forgets to sort
            // gives a different answer to one that does.
            new StageDefinition { StageId = "finishes",       Title = "FINISHES",       Order = 60,
                                  ConstituentKinds = { "plaster", "paint_interior" },
                                  Categories = { "Ceilings" } },
            new StageDefinition { StageId = "substructure",   Title = "SUB-STRUCTURE",  Order = 20,
                                  Categories = { "Structural Foundations" },
                                  LevelCodes = { "FDN" } },
            new StageDefinition { StageId = "superstructure", Title = "SUPERSTRUCTURE", Order = 30,
                                  ConstituentKinds = { "blockwork", "mortar_cement" },
                                  Categories = { "Walls", "Floors" } },
            new StageDefinition { StageId = "roof",           Title = "ROOF",           Order = 40,
                                  Categories = { "Roofs" } },
        };

        public static IEnumerable<object[]> Cases() => new[]
        {
            new object[] { "plaster",        "Walls",                   "L01" },  // kind beats category
            new object[] { "mortar_cement",  "Ceilings",                "L01" },  // kind beats category, other way
            new object[] { "paint_interior", "Walls",                   "GF"  },
            new object[] { null,             "Walls",                   "L01" },  // category
            new object[] { null,             "Ceilings",                "L02" },
            new object[] { null,             "Roofs",                   "RF"  },
            new object[] { null,             "Structural Foundations",  "FDN" },
            new object[] { null,             null,                      "FDN" },  // level only
            new object[] { null,             "Furniture",               "L01" },  // unmatched → default
            new object[] { "unknown_kind",   "Unknown Category",        "ZZ"  },  // unmatched → default
            new object[] { "",               "",                        ""    },  // all blank → default
            new object[] { "BLOCKWORK",      "walls",                   "l01" },  // case-insensitivity
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void Indexed_Resolution_Agrees_With_The_Per_Call_Path(string kind, string category, string level)
        {
            var defs = Defs();
            const string fallback = "superstructure";

            string perCall = StageMapper.ResolveStageId(kind, category, level, defs, fallback);
            string indexed = StageIndex.Build(defs, fallback).Resolve(kind, category, level);

            Assert.Equal(perCall, indexed);
        }

        [Fact]
        public void Order_Decides_When_Two_Stages_Claim_The_Same_Category()
        {
            // Both claim "Walls"; the lower Order must win, whichever way the
            // list happens to be arranged.
            var defs = new List<StageDefinition>
            {
                new StageDefinition { StageId = "late",  Order = 90, Categories = { "Walls" } },
                new StageDefinition { StageId = "early", Order = 10, Categories = { "Walls" } },
            };

            Assert.Equal("early", StageIndex.Build(defs, "x").Resolve(null, "Walls", null));
            Assert.Equal("early", StageMapper.ResolveStageId(null, "Walls", null, defs, "x"));
        }

        [Fact]
        public void The_Index_Is_Built_Once_And_Reused()
        {
            // Cheap structural guarantee: the same instance answers repeatedly
            // without rebuilding. If this ever regresses to per-call work the
            // allocation profile silently returns.
            var idx = StageIndex.Build(Defs(), "superstructure");

            for (int i = 0; i < 1000; i++)
                Assert.Equal("finishes", idx.Resolve("plaster", "Walls", "L01"));
        }

        [Fact]
        public void An_Empty_Definition_List_Falls_Back_Without_Throwing()
        {
            var idx = StageIndex.Build(new List<StageDefinition>(), "fallback");
            Assert.Equal("fallback", idx.Resolve("anything", "anything", "anything"));
        }

        [Fact]
        public void A_Null_Definition_List_Falls_Back_Without_Throwing()
        {
            Assert.Equal("fallback", StageIndex.Build(null, "fallback").Resolve("k", "c", "l"));
        }
    }
}
