using System;
using System.IO;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-80. The Setup Wizard looked for views stamped with the raw tag
    /// "exterior::face::&lt;Face&gt;", but since DTW-27 the producer adopts those and
    /// re-stamps them "::::Exterior-&lt;Face&gt;", so a wizard re-run after DOCS →
    /// Exterior Elevations (or after its own views were adopted) made a second set.
    /// Both now go through one routine and these tags.
    /// </summary>
    public class ExteriorElevationTagsTests
    {
        [Fact]
        public void Per_face_tag_and_stamp_are_the_producer_ones()
        {
            Assert.Equal("Exterior-North", ExteriorElevationTags.PerFace("North"));
            Assert.Equal("::::Exterior-North", ExteriorElevationTags.Stamp(ExteriorElevationTags.PerFace("North")));
            Assert.Equal("::::Exterior", ExteriorElevationTags.Stamp(ExteriorElevationTags.Combined));
        }

        [Fact]
        public void Legacy_raw_tag_is_still_recognised()
        {
            Assert.Equal("exterior::face::West", ExteriorElevationTags.Legacy("West"));
            Assert.True(ExteriorElevationTags.IsLegacy("exterior::face::West"));
            Assert.True(ExteriorElevationTags.IsLegacyFor("Exterior::Face::west", "West"));
            Assert.False(ExteriorElevationTags.IsLegacyFor("exterior::face::East", "West"));
            Assert.False(ExteriorElevationTags.IsLegacy("::::Exterior-West"));
        }

        [Fact]
        public void Per_face_stamp_is_told_from_the_combined_one()
        {
            Assert.True(ExteriorElevationTags.IsPerFaceStamp("::::Exterior-East", "East"));
            Assert.False(ExteriorElevationTags.IsPerFaceStamp("::::Exterior-East", "West"));
            Assert.False(ExteriorElevationTags.IsPerFaceStamp("::::Exterior", "East"));
        }

        [Fact]
        public void Combined_view_is_matched_to_its_face_by_name_only()
        {
            Assert.True(ExteriorElevationTags.IsCombinedStampFor("::::Exterior", "Arch Elevations A1 - Exterior - South", "South"));
            Assert.False(ExteriorElevationTags.IsCombinedStampFor("::::Exterior", "Arch Elevations A1 - Exterior - South", "North"));
            Assert.False(ExteriorElevationTags.IsCombinedStampFor("::::Exterior", "Renamed by hand", "South"));
            Assert.False(ExteriorElevationTags.IsCombinedStampFor("::::Exterior-South", "x - Exterior - South", "South"));
        }

        private static string Src(params string[] parts)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.True(dir != null, "repo root");
            var p = dir.FullName;
            foreach (var s in parts) p = Path.Combine(p, s);
            return File.ReadAllText(p);
        }

        [Fact]
        public void Wizard_produces_exterior_elevations_through_the_shared_routine()
        {
            var wizard = Src("StingTools", "Temp", "ProjectSetupCommand.cs");
            Assert.Contains("ProduceExteriorElevationsCommand.Produce(", wizard);
            // The wizard no longer stamps its own raw tag.
            Assert.DoesNotContain("$\"exterior::face::{", wizard);
        }

        [Fact]
        public void Docs_command_uses_the_shared_tags()
        {
            var docs = Src("StingTools", "Commands", "Drawing", "BatchProduceCommands.cs");
            Assert.Contains("ExteriorElevationTags.PerFace(", docs);
            Assert.Contains("ExteriorElevationTags.IsLegacyFor(", docs);
        }
    }
}
