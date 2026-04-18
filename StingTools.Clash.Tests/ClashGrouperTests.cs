using System.Collections.Generic;
using StingTools.Core.Clash;
using Xunit;

namespace StingTools.Clash.Tests
{
    public class ClashGrouperTests
    {
        [Fact]
        public void Empty_Input_Yields_Empty_Output()
        {
            var groups = ClashGrouper.Group(new List<ClashRecord>());
            Assert.Empty(groups);
        }

        [Fact]
        public void ElementPattern_Groups_When_Same_Element_Clashes_Multiple_Beams()
        {
            // One duct (ElementA id = 1000) clashing with 5 different beams.
            var clashes = new List<ClashRecord>();
            for (int i = 0; i < 5; i++)
            {
                clashes.Add(new ClashRecord
                {
                    Identity = $"id-{i}",
                    MatrixPairId = "DUCT:STR_BEAM",
                    VolumeMm3 = 1000f,
                    ElementA = new ClashElementRecord { ElementId = 1000, Category = "Ducts" },
                    ElementB = new ClashElementRecord { ElementId = 200 + i, Category = "Structural Framing" },
                    Centroid = new[] { (float)i, 0f, 0f },
                    AabbMin = new[] { 0f, 0f, 0f },
                    AabbMax = new[] { 1f, 1f, 1f },
                });
            }

            var groups = ClashGrouper.Group(clashes);
            Assert.Single(groups);
            Assert.Equal("element", groups[0].Kind);
            Assert.Equal(5, groups[0].Size);
            Assert.All(clashes, c => Assert.Equal(groups[0].Id, c.GroupId));
        }

        [Fact]
        public void Below_Three_Members_Falls_Through_To_Spatial()
        {
            var clashes = new List<ClashRecord>
            {
                new ClashRecord {
                    Identity = "id-1", MatrixPairId = "PAIR",
                    ElementA = new ClashElementRecord { ElementId = 1, Category = "A" },
                    ElementB = new ClashElementRecord { ElementId = 10, Category = "B" },
                    Centroid = new[] { 0f, 0f, 0f },
                    AabbMin = new[] { 0f, 0f, 0f }, AabbMax = new[] { 1f, 1f, 1f },
                },
                new ClashRecord {
                    Identity = "id-2", MatrixPairId = "PAIR",
                    ElementA = new ClashElementRecord { ElementId = 1, Category = "A" },
                    ElementB = new ClashElementRecord { ElementId = 11, Category = "B" },
                    Centroid = new[] { 0.5f, 0f, 0f },
                    AabbMin = new[] { 0f, 0f, 0f }, AabbMax = new[] { 1f, 1f, 1f },
                },
            };

            var groups = ClashGrouper.Group(clashes);
            // 2 members is below the element-pattern threshold (3), so spatial fallback.
            Assert.Single(groups);
            Assert.Equal("spatial", groups[0].Kind);
            Assert.Equal(2, groups[0].Size);
        }

        [Fact]
        public void H10_Tied_Bucket_Sizes_Produce_Deterministic_GroupIds()
        {
            // Two ElementA-anchor buckets of size 3 — ties on count. Pre-G9,
            // OrderByDescending(count) alone was unstable on ties and could
            // swap the winner between runs depending on Dictionary enumeration
            // order. Post-G9, secondary sort on (side, pair-id, element-id)
            // produces identical output on two identical inputs.
            List<ClashRecord> BuildInput()
            {
                var list = new List<ClashRecord>();
                for (int i = 0; i < 3; i++)
                {
                    list.Add(new ClashRecord
                    {
                        Identity = $"bucketA-{i}",
                        MatrixPairId = "PAIR_X",
                        ElementA = new ClashElementRecord { ElementId = 100, Category = "X" },
                        ElementB = new ClashElementRecord { ElementId = 500 + i, Category = "Y" },
                        Centroid = new[] { (float)i, 0f, 0f },
                        AabbMin = new[] { 0f, 0f, 0f }, AabbMax = new[] { 1f, 1f, 1f },
                    });
                }
                for (int i = 0; i < 3; i++)
                {
                    list.Add(new ClashRecord
                    {
                        Identity = $"bucketB-{i}",
                        MatrixPairId = "PAIR_Y",
                        ElementA = new ClashElementRecord { ElementId = 200, Category = "X" },
                        ElementB = new ClashElementRecord { ElementId = 600 + i, Category = "Y" },
                        Centroid = new[] { (float)i, 10f, 0f },
                        AabbMin = new[] { 0f, 0f, 0f }, AabbMax = new[] { 1f, 1f, 1f },
                    });
                }
                return list;
            }

            var run1 = ClashGrouper.Group(BuildInput());
            var run2 = ClashGrouper.Group(BuildInput());

            Assert.Equal(run1.Count, run2.Count);
            // Anchor/size ordering must be bit-identical across runs.
            for (int i = 0; i < run1.Count; i++)
            {
                Assert.Equal(run1[i].Id, run2[i].Id);
                Assert.Equal(run1[i].Anchor, run2[i].Anchor);
                Assert.Equal(run1[i].Size, run2[i].Size);
                Assert.Equal(run1[i].Kind, run2[i].Kind);
            }
        }

        [Fact]
        public void Repetition_Grouping_Folds_ZStack()
        {
            // Five clashes at Z = 0, 3, 6, 9, 12 — equally spaced 3 ft apart —
            // same matrix pair + similar volume. Element-pattern won't trigger
            // because each clash has different A and B ids.
            var clashes = new List<ClashRecord>();
            for (int i = 0; i < 5; i++)
            {
                clashes.Add(new ClashRecord
                {
                    Identity = $"rep-{i}",
                    MatrixPairId = "PIPE:FLOOR",
                    VolumeMm3 = 1000f,
                    ElementA = new ClashElementRecord { ElementId = 100 + i, Category = "Pipes" },
                    ElementB = new ClashElementRecord { ElementId = 200 + i, Category = "Floors" },
                    Centroid = new[] { 0f, 0f, (float)(i * 3) },
                    AabbMin = new[] { 0f, 0f, 0f }, AabbMax = new[] { 1f, 1f, 1f },
                });
            }
            var groups = ClashGrouper.Group(clashes);
            Assert.Single(groups);
            Assert.Equal("pattern", groups[0].Kind);
            Assert.Equal(5, groups[0].Size);
        }
    }
}
