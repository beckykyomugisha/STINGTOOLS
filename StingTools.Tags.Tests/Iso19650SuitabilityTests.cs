using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>Suitability, and the two facts that follow from it.
    ///
    /// The drawing that prompted this printed STATUS "S2 / WIP", SUITABILITY
    /// "S4 - FOR APROVAL" and CDE REF "WIP" — four cells, two facts, and a
    /// contradiction, because each was typed independently. Derived, they cannot
    /// disagree.</summary>
    public class Iso19650SuitabilityTests
    {
        [Theory]
        [InlineData("S0", "WIP")]
        [InlineData("S1", "SHARED")]
        [InlineData("S2", "SHARED")]
        [InlineData("S4", "SHARED")]
        [InlineData("S7", "SHARED")]
        [InlineData("A1", "PUBLISHED")]
        [InlineData("A3", "PUBLISHED")]
        [InlineData("B1", "PUBLISHED")]
        [InlineData("CR", "PUBLISHED")]
        public void The_cde_container_follows_from_the_suitability(string code, string state)
            => Assert.Equal(state, Iso19650Suitability.CdeStateFor(code));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("S9")]      // not in the standard
        [InlineData("WIP")]     // a state, not a suitability
        [InlineData("NONSENSE")]
        public void An_unrecognised_code_files_the_document_nowhere(string code)
        {
            // Returning a plausible default here would put a drawing in PUBLISHED —
            // which is a contractual statement, not a cosmetic one.
            Assert.Null(Iso19650Suitability.CdeStateFor(code));
        }

        [Theory]
        [InlineData("S2", "SUITABLE FOR INFORMATION")]
        [InlineData("S4", "SUITABLE FOR STAGE APPROVAL")]
        [InlineData("A2", "AUTHORIZED FOR CONSTRUCTION")]
        [InlineData("CR", "AS CONSTRUCTED RECORD")]
        public void The_description_is_the_standards_own_wording(string code, string desc)
            => Assert.Equal(desc, Iso19650Suitability.DescriptionFor(code));

        [Fact]
        public void An_unknown_code_gets_no_invented_description()
            => Assert.Null(Iso19650Suitability.DescriptionFor("S9"));

        // ── pulling the code back out of a hand-filled cell ───────────────────

        [Theory]
        [InlineData("S4", "S4")]
        [InlineData("S4 - FOR APROVAL", "S4")]        // the real cell, typo and all
        [InlineData("S2 - Suitable for information", "S2")]
        [InlineData("s4/for approval", "S4")]
        [InlineData("  A2  ", "A2")]
        [InlineData("CR", "CR")]
        public void The_code_is_recoverable_from_a_cell_holding_code_and_description(
            string raw, string expected)
            => Assert.Equal(expected, Iso19650Suitability.ExtractCode(raw));

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("FOR APPROVAL")]      // description only, no code
        [InlineData("WIP")]
        public void A_cell_with_no_code_in_it_yields_no_code(string raw)
            => Assert.Equal("", Iso19650Suitability.ExtractCode(raw));

        [Fact]
        public void A_description_containing_a_stray_letter_digit_pair_is_not_mistaken_for_a_code()
        {
            // "S9" is not in the standard, so it must not be picked up as the code
            // just because it has the shape of one.
            Assert.Equal("", Iso19650Suitability.ExtractCode("S9 - something"));
        }

        [Fact]
        public void The_three_facts_agree_when_derived_from_one_code()
        {
            // This is the whole point: one input, no contradiction possible.
            const string typed = "S4 - FOR APROVAL";
            var code = Iso19650Suitability.ExtractCode(typed);

            Assert.Equal("S4", code);
            Assert.Equal("SUITABLE FOR STAGE APPROVAL", Iso19650Suitability.DescriptionFor(code));
            Assert.Equal("SHARED", Iso19650Suitability.CdeStateFor(code));
        }
    }
}
