// A-2 — the Revit-free halves of minSizeMm on tag rules, tag leaderStyle and
// the tag-depth layering (tokenProfile > annotation > style pack).

using System.Collections.Generic;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class AnnotationRuleResolutionTests
    {
        [Theory]
        [InlineData(10, null, true)]     // no minimum
        [InlineData(10, 0.0, true)]      // zero minimum = no minimum
        [InlineData(49.9, 50.0, false)]  // under
        [InlineData(50, 50.0, true)]     // at
        [InlineData(0, 50.0, true)]      // unmeasurable is kept, and counted by the caller
        public void MinSizeGate(double sizeMm, double? min, bool keeps)
            => Assert.Equal(keeps, AnnotationMinSize.Keeps(sizeMm, min));

        [Theory]
        [InlineData(null, TagLeaderMode.None)]
        [InlineData("NoLeader", TagLeaderMode.None)]
        [InlineData(" attached ", TagLeaderMode.Attached)]
        [InlineData("FREE", TagLeaderMode.Free)]
        [InlineData("Elbow", TagLeaderMode.Unrecognised)]
        public void LeaderStyleParse(string s, TagLeaderMode mode)
            => Assert.Equal(mode, TagLeader.Parse(s));

        [Fact]
        public void AnnotationDepthsComeFromTagDepthsAndTagRuleDepth_RuleWins()
        {
            var pack = new AnnotationRulePack
            {
                TagDepths = new Dictionary<string, int> { ["Doors"] = 3, ["Walls"] = 12 },
                Rules = new List<AutoAnnotationRule>
                {
                    new AutoAnnotationRule { RuleType = "AutoTag", Category = "Doors", Depth = 5 },
                    new AutoAnnotationRule { RuleType = "AutoTag", Category = "*", Depth = 9 },          // no single category
                    new AutoAnnotationRule { RuleType = "AutoTag", Category = "Windows", Depth = 4, Enabled = false },
                    new AutoAnnotationRule { RuleType = "AutoDim", Category = "Grids", Depth = 7 },      // not a tag rule
                },
            };
            var m = TagDepthLayering.FromAnnotation(pack);
            Assert.Equal(5, m["doors"]);     // rule beats map, case-insensitive
            Assert.Equal(10, m["Walls"]);    // clamped
            Assert.False(m.ContainsKey("*"));
            Assert.False(m.ContainsKey("Windows"));
            Assert.False(m.ContainsKey("Grids"));
        }

        [Fact]
        public void DrawingTypeLayersBeatThePack_ProfileBeatsAnnotation()
        {
            var profile    = new Dictionary<string, int> { ["Doors"] = 8 };
            var annotation = new Dictionary<string, int> { ["Doors"] = 5, ["Rooms"] = 4 };
            var pack       = new Dictionary<string, int> { ["Doors"] = 1, ["Rooms"] = 1, ["Walls"] = 2 };
            var m = TagDepthLayering.Merge(profile, annotation, pack);
            Assert.Equal(8, m["Doors"]);
            Assert.Equal(4, m["Rooms"]);
            Assert.Equal(2, m["Walls"]);
            Assert.Null(TagDepthLayering.Merge(null, null, new Dictionary<string, int>()));
        }

        [Fact]
        public void EmptyAnnotationContributesNothing()
            => Assert.Null(TagDepthLayering.FromAnnotation(new AnnotationRulePack()));

        [Fact]
        public void LegacyFlagsAreDetectedAndFolded()
        {
#pragma warning disable CS0618
            var pack = new AnnotationRulePack { AutoTagDoors = true };
#pragma warning restore CS0618
            Assert.True(pack.HasLegacyFlags());
            pack.MigrateFromLegacy();
            Assert.False(pack.HasLegacyFlags());
            Assert.Contains(pack.Rules, r => r.Category == "Doors" && r.RuleType == "AutoTag");
            Assert.False(new AnnotationRulePack().HasLegacyFlags());
        }
    }
}
