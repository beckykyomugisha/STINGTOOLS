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

    /// <summary>
    /// Tests for NormaliseKey - the reason nine families were reported "no
    /// Category declared" while their declarations sat unused in the config.
    /// The pairs below are the real file/declaration spellings from the shipped
    /// data, measured 2026-09-21.
    /// </summary>
    public class TagCategoryKeyNormalisationTests
    {
        [Theory]
        // A slash cannot go in a file name, so the file has a dash.
        [InlineData("STING - Brace - Truss Tag", "STING - Brace / Truss Tag")]
        [InlineData("STING - Tie-In Point Tag (Conduit - Electrical LV-ELV)",
                    "STING - Tie-In Point Tag (Conduit - Electrical LV/ELV)")]
        [InlineData("STING - Tie-In Point Tag (Gas - Medical - Industrial - Natural Gas)",
                    "STING - Tie-In Point Tag (Gas - Medical / Industrial / Natural Gas)")]
        // The file carries a second " Tag" the declaration does not.
        [InlineData("STING - Specialty Equipment Tag Asset Tag",
                    "STING - Specialty Equipment Tag Asset")]
        [InlineData("STING - Specialty Equipment Tag General Tag",
                    "STING - Specialty Equipment Tag General")]
        [InlineData("STING - Tie-In Point Tag (Duct - HVAC) Tag",
                    "STING - Tie-In Point Tag (Duct - HVAC)")]
        // Case and stray spacing must not matter either.
        [InlineData("STING - Duct Tag", "sting  -  DUCT   tag")]
        public void FileNameAndDeclarationAgreeOnOneKey(string fileName, string declared)
        {
            Assert.Equal(TagCategoryNameForms.NormaliseKey(declared),
                         TagCategoryNameForms.NormaliseKey(fileName));
        }

        [Fact]
        public void DifferentFamiliesKeepDifferentKeys()
        {
            // The whole risk of normalising: two families folding onto one key
            // would silently hand one of them the other's category.
            var names = new[]
            {
                "STING - Duct Tag", "STING - Duct Accessory Tag", "STING - Flex Duct Tag",
                "STING - Pipe Tag", "STING - Pipe Accessory Tag",
                "STING - Specialty Equipment Tag Asset Tag",
                "STING - Specialty Equipment Tag General Tag",
                "STING - Medical Equipment Tag", "STING - Mechanical Equipment Tag",
            };
            var keys = names.Select(TagCategoryNameForms.NormaliseKey).ToList();
            Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void OnlyOneTrailingTagIsDropped()
        {
            // "... Asset Tag" -> "... ASSET", not "... " - the second Tag is part
            // of the family's actual name.
            Assert.Equal("STING - SPECIALTY EQUIPMENT TAG ASSET",
                         TagCategoryNameForms.NormaliseKey("STING - Specialty Equipment Tag Asset Tag"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void EmptyNamesGiveAnEmptyKeyRatherThanThrowing(string name)
        {
            Assert.Equal("", TagCategoryNameForms.NormaliseKey(name));
        }
    }
}
