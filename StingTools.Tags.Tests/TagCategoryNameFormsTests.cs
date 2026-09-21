// Tests for TagCategoryNameForms - the singularisation that decides which tag
// category name a declared host category is looked up under.
//
// The four RealFamiliesThatFailed cases are not invented: they are the exact
// declarations that came back UNRESOLVED from the 206-family audit on
// 2026-09-21, against categories that genuinely exist in Revit.

using System;
using System.Linq;
using StingTools.Tags;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class TagCategoryNameFormsTests
    {
        [Theory]
        // The regression: -ies and -es plurals, all four measured live.
        [InlineData("Duct Accessories", "Duct Accessory Tags")]
        [InlineData("Pipe Accessories", "Pipe Accessory Tags")]
        [InlineData("Assemblies", "Assembly Tags")]
        [InlineData("Structural Trusses", "Structural Truss Tags")]
        // The ordinary -s plural that already worked, which must keep working.
        [InlineData("Doors", "Door Tags")]
        [InlineData("Windows", "Window Tags")]
        [InlineData("Walls", "Wall Tags")]
        [InlineData("Air Terminals", "Air Terminal Tags")]
        [InlineData("Spaces", "Space Tags")]
        // Singular hosts: " Tags" appended, nothing chopped.
        [InlineData("Furniture", "Furniture Tags")]
        [InlineData("Medical Equipment", "Medical Equipment Tags")]
        public void OffersTheRealTagCategoryName(string host, string expected)
        {
            Assert.Contains(expected, TagCategoryNameForms.Candidates(host));
        }

        [Fact]
        public void AlreadyATagCategoryIsOfferedFirst()
        {
            var c = TagCategoryNameForms.Candidates("Door Tags");
            Assert.Equal("Door Tags", c.First());
        }

        [Fact]
        public void DoesNotChopADoubleEss()
        {
            // "Mass" is singular already; "Mas Tags" would be nonsense.
            Assert.DoesNotContain("Mas Tags", TagCategoryNameForms.Candidates("Mass"));
            Assert.Contains("Mass Tags", TagCategoryNameForms.Candidates("Mass"));
        }

        [Fact]
        public void CandidatesAreDistinct()
        {
            foreach (var host in new[] { "Doors", "Assemblies", "Door Tags", "Furniture", "Mass" })
            {
                var c = TagCategoryNameForms.Candidates(host);
                Assert.Equal(c.Count, c.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void EmptyHostOffersNothingRatherThanThrowing(string host)
        {
            Assert.Empty(TagCategoryNameForms.Candidates(host));
        }

        [Fact]
        public void WhitespaceIsTrimmedBeforeBuildingNames()
        {
            Assert.Contains("Door Tags", TagCategoryNameForms.Candidates("  Doors  "));
        }
    }
}
