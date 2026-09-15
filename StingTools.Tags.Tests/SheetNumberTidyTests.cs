using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>Sheet numbers produced from a pattern with an empty token.
    ///
    /// "A--001" is the real case, from a live project on 2026-09-15: the pattern
    /// "A-{lvl}-{seq:D3}" with no level. The empty segment then travelled — into an
    /// export filename as "SAH--ZZ", and the sheet number is the Number field of the
    /// ISO identifier, so it was on its way onto an issued drawing's document code.</summary>
    public class SheetNumberTidyTests
    {
        [Theory]
        [InlineData("A--001", "A-001")]            // the case that started this
        [InlineData("A-RCP--001", "A-RCP-001")]
        [InlineData("M-CO--003", "M-CO-003")]
        [InlineData("PRJ---0001", "PRJ-0001")]     // two empty tokens in a row
        [InlineData("-A-001", "A-001")]            // leading token empty
        [InlineData("A-001-", "A-001")]            // trailing token empty
        [InlineData("--A--001--", "A-001")]
        public void Empty_tokens_do_not_leave_their_separators(string raw, string expected)
            => Assert.Equal(expected, SheetNumberTidy.Collapse(raw));

        [Theory]
        [InlineData("A-001")]
        [InlineData("A-L1-001")]
        [InlineData("PROJECTN-SAH-ZZ-01-DR-A-0001")]
        [InlineData("M-101")]
        [InlineData("A101")]
        public void A_well_formed_number_is_returned_unchanged(string raw)
        {
            Assert.Equal(raw, SheetNumberTidy.Collapse(raw));
            Assert.False(SheetNumberTidy.NeedsTidying(raw));
        }

        [Fact]
        public void The_damage_is_detectable_before_anything_is_renamed()
        {
            // So a command can report what it actually altered rather than renaming
            // every sheet and claiming a count.
            Assert.True(SheetNumberTidy.NeedsTidying("A--001"));
            Assert.False(SheetNumberTidy.NeedsTidying("A-001"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Nothing_in_nothing_out(string raw)
        {
            Assert.Equal(raw, SheetNumberTidy.Collapse(raw));
            Assert.False(SheetNumberTidy.NeedsTidying(raw));
        }

        [Fact]
        public void A_number_that_is_only_separators_collapses_to_empty_not_to_a_dash()
        {
            // "-" alone is not a sheet number. Returning "-" would be a number that
            // looks deliberate; empty is honestly nothing, and the caller can refuse.
            Assert.Equal("", SheetNumberTidy.Collapse("---"));
        }

        [Fact]
        public void Tidying_is_idempotent()
        {
            var once = SheetNumberTidy.Collapse("A--001");
            Assert.Equal(once, SheetNumberTidy.Collapse(once));
        }

        [Fact]
        public void A_different_separator_can_be_used_without_touching_hyphens()
        {
            // Some projects number with dots. The hyphen inside must survive.
            Assert.Equal("A.001", SheetNumberTidy.Collapse("A..001", '.'));
            Assert.Equal("A--001", SheetNumberTidy.Collapse("A--001", '.'));
        }
    }
}
