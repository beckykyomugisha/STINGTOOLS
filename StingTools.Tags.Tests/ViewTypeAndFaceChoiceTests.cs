using System.Collections.Generic;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Views were created with the FIRST view type of their family, whatever the
    /// drawing type wanted; material tags were handed a whole element, which a
    /// material tag cannot tag. These pin the two choices that replace them.
    /// </summary>
    public class ViewTypeAndFaceChoiceTests
    {
        private static readonly List<(string, string)> Types = new List<(string, string)>
        {
            ("Floor Plan", "FloorPlan"),
            ("Building Section", "Section"),
            ("STING - Section", "Section"),
            ("Wall Section", "Section"),
            ("Exterior Elevation", "Elevation"),
        };

        [Fact]
        public void The_named_view_type_wins_over_the_first_of_its_family()
        {
            int i = ViewFamilyTypeChoice.Pick(Types, "Section", "STING - Section", out var why);
            Assert.Equal(2, i);
            Assert.Null(why);
        }

        [Fact]
        public void No_name_keeps_the_old_first_of_family_choice()
        {
            Assert.Equal(1, ViewFamilyTypeChoice.Pick(Types, "Section", null, out var why));
            Assert.Null(why);
        }

        [Fact]
        public void A_missing_named_type_falls_back_and_says_how_to_fix_it()
        {
            int i = ViewFamilyTypeChoice.Pick(Types, "Elevation", "STING - Elevation", out var why);
            Assert.Equal(4, i);
            Assert.Contains("DrawingTypes_EnsureViewTypes", why);
        }

        [Fact]
        public void A_name_for_another_kind_of_view_is_not_a_warning()
        {
            // A section drawing type producing a key plan: the plan takes the default quietly.
            int i = ViewFamilyTypeChoice.Pick(Types, "FloorPlan", "STING - Section", out var why);
            Assert.Equal(0, i);
            Assert.Null(why);
        }

        [Fact]
        public void No_type_of_the_family_at_all_is_minus_one()
        {
            Assert.Equal(-1, ViewFamilyTypeChoice.Pick(Types, "Detail", "STING - Callout", out var why));
            Assert.NotNull(why);
        }

        [Theory]
        [InlineData("Section", "Section")]
        [InlineData("elevation", "Elevation")]
        [InlineData("Detail", "Detail")]
        [InlineData("RCP", "CeilingPlan")]
        [InlineData("Clarification", null)]
        [InlineData("Coordination", null)]
        public void Purpose_maps_to_the_view_family_it_produces(string purpose, string family)
            => Assert.Equal(family, ViewFamilyTypeChoice.FamilyForPurpose(purpose));

        [Fact]
        public void The_face_towards_the_viewer_is_tagged_not_the_bigger_one_facing_away()
            => Assert.Equal(1, FaceChoice.Best(new[] { -1.0, 1.0 }, new[] { 20.0, 12.0 }));

        [Fact]
        public void Among_faces_towards_the_viewer_the_larger_wins()
            => Assert.Equal(0, FaceChoice.Best(new[] { 1.0, 1.0 }, new[] { 12.0, 0.3 }));

        [Fact]
        public void Edge_on_faces_still_give_a_tag_in_plan()
            => Assert.Equal(1, FaceChoice.Best(new[] { 0.0, 0.0 }, new[] { 3.0, 9.0 }));

        [Fact]
        public void No_usable_face_is_minus_one()
        {
            Assert.Equal(-1, FaceChoice.Best(new double[0], new double[0]));
            Assert.Equal(-1, FaceChoice.Best(new[] { 1.0 }, new[] { 0.0 }));
        }
    }
}
