using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-83: rooms, spaces and areas are tagged with a SpatialElementTag
    /// (NewRoomTag / NewSpaceTag / NewAreaTag). The runner used to hand them to
    /// IndependentTag.Create, which threw once per room — every shipped room /
    /// space / area rule placed nothing. These pin the routing decision and the
    /// "already tagged" key the runner shares between tag kinds.
    /// </summary>
    public class SpatialTagRoutingTests
    {
        [Theory]
        [InlineData("OST_Rooms", SpatialTagKind.Room)]
        [InlineData("OST_MEPSpaces", SpatialTagKind.Space)]
        [InlineData("OST_Areas", SpatialTagKind.Area)]
        [InlineData("ost_rooms", SpatialTagKind.Room)]
        [InlineData("OST_Doors", SpatialTagKind.None)]
        [InlineData("OST_RoomTags", SpatialTagKind.None)]
        [InlineData(null, SpatialTagKind.None)]
        public void Spatial_hosts_route_to_the_spatial_tag_path(string bic, SpatialTagKind expected)
            => Assert.Equal(expected, SpatialTagRouting.KindOf(bic));

        [Theory]
        [InlineData(SpatialTagKind.Room, "FloorPlan", true)]
        [InlineData(SpatialTagKind.Room, "CeilingPlan", true)]
        [InlineData(SpatialTagKind.Room, "Section", true)]
        [InlineData(SpatialTagKind.Room, "Elevation", false)]
        [InlineData(SpatialTagKind.Room, "ThreeD", false)]
        [InlineData(SpatialTagKind.Space, "FloorPlan", true)]
        [InlineData(SpatialTagKind.Space, "Section", false)]
        [InlineData(SpatialTagKind.Space, "AreaPlan", false)]
        [InlineData(SpatialTagKind.Area, "AreaPlan", true)]
        [InlineData(SpatialTagKind.Area, "FloorPlan", false)]
        public void Each_spatial_tag_goes_only_where_Revit_accepts_it(SpatialTagKind kind, string viewType, bool ok)
        {
            var refusal = SpatialTagRouting.ViewRefusal(kind, viewType);
            Assert.Equal(ok, refusal == null);
            if (!ok) Assert.Contains(viewType, refusal);
        }

        [Fact]
        public void Only_rooms_can_be_tagged_through_a_link()
        {
            Assert.True(SpatialTagRouting.LinkedTaggable(SpatialTagKind.Room));
            Assert.False(SpatialTagRouting.LinkedTaggable(SpatialTagKind.Space));
            Assert.False(SpatialTagRouting.LinkedTaggable(SpatialTagKind.Area));
        }

        [Fact]
        public void A_host_element_and_a_linked_element_with_the_same_id_are_different_hosts()
        {
            Assert.NotEqual(TaggedHostKey.Local(42), TaggedHostKey.Linked(7, 42));
            Assert.NotEqual(TaggedHostKey.Linked(7, 42), TaggedHostKey.Linked(8, 42));
            Assert.Equal(TaggedHostKey.Local(42), TaggedHostKey.From(42, -1, -1));
            Assert.Equal(TaggedHostKey.Linked(7, 42), TaggedHostKey.From(-1, 7, 42));
        }

        /// <summary>
        /// Every shipped tag rule on rooms, spaces or areas must reach the spatial
        /// path. Resolved the way the runner does it: the rule kind's forced
        /// category, else the row's own, as a BuiltInCategory name. Asserts a
        /// non-trivial count so a data change that empties the set fails loudly
        /// rather than passing vacuously.
        /// </summary>
        [Fact]
        public void Every_shipped_room_space_area_tag_rule_reaches_the_spatial_path()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_DRAWING_TYPES.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            var types = JObject.Parse(File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "Data", "STING_DRAWING_TYPES.json")));

            int spatial = 0;
            var misrouted = new List<string>();
            foreach (var t in types["drawingTypes"])
            {
                var rules = t["annotation"]?["rules"];
                if (rules == null) continue;
                foreach (var r in rules)
                {
                    var rt = r["ruleType"]?.ToString();
                    if (!AnnotationRuleKinds.IsTagKind(rt)) continue;
                    if (AnnotationRuleKinds.IsMaterialCalloutKind(rt)) continue;
                    var cat = AnnotationRuleKinds.EffectiveCategory(rt, r["category"]?.ToString());
                    var bic = cat != null && cat.StartsWith("OST_", StringComparison.OrdinalIgnoreCase)
                        ? cat : RevitCategoryTree.FindByDisplayName(cat)?.Bic ?? cat;
                    bool isSpatialCategory = bic == "OST_Rooms" || bic == "OST_MEPSpaces" || bic == "OST_Areas";
                    if (!isSpatialCategory) continue;
                    spatial++;
                    if (SpatialTagRouting.KindOf(bic) == SpatialTagKind.None)
                        misrouted.Add($"{t["id"]}: {rt} / {cat}");
                }
            }
            Assert.True(spatial >= 30, $"expected the shipped room/space/area tag rules (~40), found {spatial}");
            Assert.Empty(misrouted);
        }
    }
}
