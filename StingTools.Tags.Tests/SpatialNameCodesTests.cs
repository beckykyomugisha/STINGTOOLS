// ══════════════════════════════════════════════════════════════════════════
//  SpatialNameCodesTests.cs — LVL / ZONE / LOC codes read from names.
//
//  Tagging accuracy review, 2026-09-29. Every rule used String.Contains, so a
//  code matched inside a longer one ("Zone 12" → Z01, "BLD10" → BLD1, "21st
//  Floor" → L01), "Level -1" lost its sign and became Level 1, and an
//  unrecognised multi-word level name was passed through with the tag
//  separator in it ("Ring beam" → "RING-BEAM"), which the token sanitiser then
//  cut to "RING" and an Overwrite run refused as a nine-segment tag.
// ══════════════════════════════════════════════════════════════════════════
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class SpatialNameCodesTests
    {
        // ── Levels: behaviour that must not change ──────────────────────────

        [Theory]
        [InlineData("Level 1", "L01")]
        [InlineData("Level 2", "L02")]
        [InlineData("Level 10", "L10")]
        [InlineData("Level 00", "L00")]
        [InlineData("Ground Floor", "GF")]
        [InlineData("Ground", "GF")]
        [InlineData("Lower Ground", "LG")]
        [InlineData("Upper Ground Floor", "UG")]
        [InlineData("Basement", "B1")]
        [InlineData("Basement 2", "B2")]
        [InlineData("B3", "B3")]
        [InlineData("Sub-basement 1", "SB1")]
        [InlineData("Roof", "RF")]
        [InlineData("Penthouse", "PH")]
        [InlineData("Mezzanine", "MZ")]
        [InlineData("Plant Room Level", "PL")]
        [InlineData("First Floor", "L01")]
        [InlineData("Second Floor", "L02")]
        [InlineData("3rd Floor", "L03")]
        [InlineData("11th Floor", "L11")]
        [InlineData("L01", "L01")]
        [InlineData("L7", "L07")]
        [InlineData("Floor 3", "L03")]
        public void Recognised_level_names_keep_their_code(string name, string expected)
        {
            Assert.Equal(expected, SpatialNameCodes.LevelCodeFromName(name));
        }

        // ── Levels: the defects ─────────────────────────────────────────────

        [Theory]
        [InlineData("21st Floor", "L21")]
        [InlineData("22nd Floor", "L22")]
        [InlineData("23rd Floor", "L23")]
        [InlineData("31st Floor", "L31")]
        public void A_numeric_ordinal_is_read_as_a_whole_number(string name, string expected)
        {
            Assert.Equal(expected, SpatialNameCodes.LevelCodeFromName(name));
        }

        [Fact]
        public void A_compound_ordinal_word_is_not_read_as_its_tail()
        {
            Assert.NotEqual("L01", SpatialNameCodes.LevelCodeFromName("Twenty-First Floor"));
        }

        [Theory]
        [InlineData("Level -1", "B1")]
        [InlineData("Level -2", "B2")]
        [InlineData("Level −1", "B1")]
        public void A_negative_level_is_below_ground_not_level_one(string name, string expected)
        {
            Assert.Equal(expected, SpatialNameCodes.LevelCodeFromName(name));
            Assert.NotEqual(SpatialNameCodes.LevelCodeFromName("Level 1"),
                            SpatialNameCodes.LevelCodeFromName(name));
        }

        [Theory]
        [InlineData("Ring beam", "RINGBEAM")]
        [InlineData("Transfer Level", "TRANSFERLEVE")]
        [InlineData("Plant Deck East", "PLANTDECKEAS")]
        public void A_passthrough_level_code_never_contains_a_separator(string name, string expected)
        {
            string code = SpatialNameCodes.LevelCodeFromName(name, out bool passthrough);
            Assert.True(passthrough);
            Assert.Equal(expected, code);
            Assert.DoesNotContain("-", code);
            Assert.DoesNotContain(" ", code);
            Assert.True(code.Length <= SpatialNameCodes.MaxPassthroughLength);
        }

        [Fact]
        public void Two_multi_word_levels_no_longer_collapse_to_one_code()
        {
            // Both used to become "PLANT-DECK-…" and store as "PLANT".
            Assert.NotEqual(SpatialNameCodes.LevelCodeFromName("Plant Deck East"),
                            SpatialNameCodes.LevelCodeFromName("Plant Deck West"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("---")]
        public void A_level_name_with_nothing_usable_is_XX(string name)
        {
            Assert.Equal("XX", SpatialNameCodes.LevelCodeFromName(name));
        }

        // ── Zones ───────────────────────────────────────────────────────────

        [Theory]
        [InlineData("Z01", "Z01")]
        [InlineData("Z02-101", "Z02")]
        [InlineData("Zone 3", "Z03")]
        [InlineData("Zone B Office", "Z02")]
        [InlineData("Wing D", "Z04")]
        [InlineData("North Wing", "Z01")]
        [InlineData("South", "Z02")]
        public void Zone_codes_are_recognised(string text, string expected)
        {
            Assert.Equal(expected, SpatialNameCodes.ZoneFromText(text));
        }

        [Theory]
        [InlineData("Zone 12")]
        [InlineData("Z012")]
        [InlineData("Zone 10 Plant")]
        [InlineData("Wing Annex")]
        [InlineData("Northampton Suite")]
        [InlineData("Eastleigh")]
        public void A_zone_code_inside_a_longer_one_is_not_a_match(string text)
        {
            Assert.Null(SpatialNameCodes.ZoneFromText(text));
        }

        // ── LOC fallback aliases ────────────────────────────────────────────

        [Theory]
        [InlineData("BLD1", "BLD1")]
        [InlineData("BLD2 Mechanical", "BLD2")]
        [InlineData("M-BLD3-Services", "BLD3")]
        [InlineData("Building 1", "BLD1")]
        [InlineData("Block C", "BLD3")]
        [InlineData("EXT", "EXT")]
        [InlineData("EXT-External Works", "EXT")]
        [InlineData("External Works", "EXT")]
        public void Loc_aliases_are_recognised(string text, string expected)
        {
            Assert.Equal(expected, SpatialNameCodes.LocFromTextFallback(text));
        }

        [Theory]
        [InlineData("BLD10")]
        [InlineData("BLD12 Plant")]
        [InlineData("Building 12")]
        [InlineData("Block AB")]
        [InlineData("Next Stage")]
        [InlineData("Textile Store")]
        [InlineData("Extension")]
        public void A_loc_alias_inside_a_longer_code_is_not_a_match(string text)
        {
            Assert.Null(SpatialNameCodes.LocFromTextFallback(text));
        }
    }
}
