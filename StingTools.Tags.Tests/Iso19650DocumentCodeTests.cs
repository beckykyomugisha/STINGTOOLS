using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>The ISO 19650 document identifier.
    ///
    /// Every case here is drawn from what a live job actually produced on
    /// 2026-09-15 — the identifier that reached an issued PDF was
    /// PROJECTN-ORGANI-L01-LG-COORD-A-L1-001-1, with no volume, an internal level
    /// code, a role that is not a role, a number that was the whole sheet number,
    /// and the revision welded on. These tests are that drawing, in reverse.</summary>
    public class Iso19650DocumentCodeTests
    {
        [Fact]
        public void The_identifier_has_seven_fields_in_iso_order()
        {
            var code = Iso19650DocumentCode.Assemble(
                "PROJECTN", "SAH", "ZZ", "L01", "DR", "COORD", "A-L1-001");

            Assert.Equal("PROJECTN-SAH-ZZ-01-DR-Z-0001", code);
            Assert.Equal(7, code.Split('-').Length);
        }

        [Fact]
        public void The_revision_is_not_part_of_the_identifier()
        {
            // A document's identity does not change when it is revised. The old form
            // appended it, so every revision minted a "new document".
            var p01 = Iso19650DocumentCode.Assemble("PRJ", "SAH", "ZZ", "01", "DR", "A", "0001");
            var p02 = Iso19650DocumentCode.Assemble("PRJ", "SAH", "ZZ", "01", "DR", "A", "0001");

            Assert.Equal(p01, p02);
            Assert.DoesNotContain("P01", p01);
        }

        // ── the number segment ────────────────────────────────────────────────

        [Theory]
        [InlineData("A-L1-001", "0001")]   // the real case: hyphens and all
        [InlineData("7", "0007")]
        [InlineData("0042", "0042")]
        [InlineData("A-101", "0101")]
        [InlineData("12345", "12345")]     // already wider than four: keep it
        [InlineData("", "0000")]
        [InlineData("SHEET", "0000")]      // no digits at all
        public void The_number_is_four_digits_taken_from_the_trailing_run(string raw, string expected)
            => Assert.Equal(expected, Iso19650DocumentCode.NormaliseNumber(raw));

        [Fact]
        public void A_number_of_all_zeroes_does_not_become_empty()
        {
            // TrimStart('0') on "000" leaves nothing; the field must stay four digits.
            Assert.Equal("0000", Iso19650DocumentCode.NormaliseNumber("000"));
        }

        // ── the level segment ─────────────────────────────────────────────────

        [Theory]
        [InlineData("L01", "01")]          // STING's internal form, which reached a PDF
        [InlineData("L1", "01")]
        [InlineData("L02", "02")]
        [InlineData("GF", "00")]
        [InlineData("B1", "B1")]
        [InlineData("B2", "B2")]
        [InlineData("ROOF", "RF")]
        [InlineData("03", "03")]
        [InlineData("", "ZZ")]
        public void The_level_uses_iso_codes_not_stings_internal_ones(string raw, string expected)
            => Assert.Equal(expected, Iso19650DocumentCode.NormaliseLevel(raw));

        // ── the role segment ──────────────────────────────────────────────────

        [Theory]
        [InlineData("COORD", "Z")]         // the real case: not a role at all
        [InlineData("GEN", "Z")]
        [InlineData("ARCH", "A")]
        [InlineData("A", "A")]
        [InlineData("STRUCT", "S")]
        [InlineData("MECH", "M")]
        [InlineData("H", "M")]             // HVAC folds to mechanical
        [InlineData("ELEC", "E")]
        [InlineData("PLM", "P")]
        [InlineData("MG", "M")]            // medical gas
        // Radiation protection is a SPECIALIST designer (Y), not the electrical
        // engineer (E). This row asserted E because that is what the code did; the
        // test agreed with the defect rather than with ISO 19650, which is the one
        // way a test can make a wrong answer harder to find. Same for fire
        // protection, which folded to S (Structural).
        [InlineData("RP", "Y")]            // radiation protection -> specialist
        [InlineData("FP", "Y")]            // fire protection -> specialist
        [InlineData("", "Z")]
        [InlineData("NONSENSE", "Z")]
        public void The_role_is_one_letter_from_the_iso_alphabet(string raw, string expected)
        {
            var role = Iso19650DocumentCode.NormaliseRole(raw);
            Assert.Equal(expected, role);
            Assert.Single(role);
        }

        // ── the volume segment, which did not exist at all ────────────────────

        [Fact]
        public void A_missing_volume_becomes_ZZ_never_an_empty_segment()
        {
            // The old form had no volume field, and the export pattern emitted
            // "SAH--ZZ" — a hyphen with nothing between it and the next.
            Assert.Equal("ZZ", Iso19650DocumentCode.NormaliseVolume(null));
            Assert.Equal("ZZ", Iso19650DocumentCode.NormaliseVolume("   "));
            Assert.DoesNotContain("--",
                Iso19650DocumentCode.Assemble("PRJ", "SAH", "", "", "", "", ""));
        }

        // ── fields can never carry a separator ────────────────────────────────

        [Fact]
        public void A_separator_inside_a_field_is_stripped_not_passed_through()
        {
            // This is what made the old identifier unparseable: the Number segment
            // carried the sheet number's own hyphens, so the field count varied.
            var code = Iso19650DocumentCode.Assemble(
                "PRJ-X", "S A H", "Z Z", "L-01", "D R", "CO-ORD", "A-L1-001");

            Assert.Equal(7, code.Split('-').Length);
        }

        // ── the guard that stops the code eating its own output ───────────────

        [Fact]
        public void An_assembled_identifier_is_recognisable_as_one()
        {
            var code = Iso19650DocumentCode.Assemble("PRJ", "SAH", "ZZ", "01", "DR", "A", "0001");

            Assert.True(Iso19650DocumentCode.LooksAssembled(code));
            // The short sheet numbers it must NOT mistake for one:
            Assert.False(Iso19650DocumentCode.LooksAssembled("A-L1-001"));
            Assert.False(Iso19650DocumentCode.LooksAssembled("A--001"));
            Assert.False(Iso19650DocumentCode.LooksAssembled("A-101"));
            Assert.False(Iso19650DocumentCode.LooksAssembled(""));
            Assert.False(Iso19650DocumentCode.LooksAssembled(null));
            // And the OLD malformed form, which is not seven fields:
            Assert.False(Iso19650DocumentCode.LooksAssembled("PROJECTN-ORGANI-L01-LG-COORD-A-L1-001-1"));
        }

        [Fact]
        public void Assembling_twice_from_its_own_output_does_not_nest()
        {
            // Feed the identifier back in as the NUMBER, which is exactly what
            // Sheet_NumberFromIso made happen. The trailing-digit rule means it
            // collapses back to the same number instead of growing.
            var once = Iso19650DocumentCode.Assemble("PRJ", "SAH", "ZZ", "01", "DR", "A", "A-L1-001");
            var twice = Iso19650DocumentCode.Assemble("PRJ", "SAH", "ZZ", "01", "DR", "A", once);

            Assert.Equal(once, twice);
        }
    }
}
