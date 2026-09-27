using StingTools.Core;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>Which of the project's discipline folders an export goes to.
    ///
    /// The folders are ProjectSetup.DefaultBimDisciplines. Before the matcher, the
    /// Export Centre and the batch PDF exporters created a folder named after the raw
    /// code, so a CDE folder ended up holding "A" beside "A_Architectural".</summary>
    public class DisciplineFolderMatcherTests
    {
        private static readonly string[] Bim =
            { "A_Architectural", "E_Electrical", "M_Mechanical", "P_Plumbing", "S_Structural" };

        [Theory]
        [InlineData("A", "A_Architectural")]
        [InlineData("a", "A_Architectural")]
        [InlineData("M", "M_Mechanical")]
        [InlineData("E", "E_Electrical")]
        [InlineData("P", "P_Plumbing")]
        [InlineData("S", "S_Structural")]
        public void A_role_letter_finds_its_folder(string code, string expected)
            => Assert.Equal(expected, DisciplineFolderMatcher.Match(Bim, code));

        [Theory]
        [InlineData("MECH", "M_Mechanical")]
        [InlineData("HVAC", "M_Mechanical")]
        [InlineData("ELEC", "E_Electrical")]
        [InlineData("STR", "S_Structural")]
        [InlineData("ARCH", "A_Architectural")]
        [InlineData("MG", "M_Mechanical")]
        public void A_sting_discipline_code_folds_to_its_role_letter(string code, string expected)
            => Assert.Equal(expected, DisciplineFolderMatcher.Match(Bim, code));

        [Theory]
        [InlineData("FP")]      // Y (specialist) — no Y folder in the default set
        [InlineData("COORD")]   // Z (general)
        [InlineData("Other")]
        [InlineData("")]
        [InlineData(null)]
        public void No_folder_means_null_not_an_invented_one(string code)
            => Assert.Null(DisciplineFolderMatcher.Match(Bim, code));

        [Fact]
        public void A_project_folder_named_exactly_after_the_code_wins()
        {
            var folders = new[] { "FP", "A_Architectural" };
            Assert.Equal("FP", DisciplineFolderMatcher.Match(folders, "FP"));
        }

        [Fact]
        public void A_specialist_folder_is_used_when_the_project_has_one()
        {
            var folders = new[] { "A_Architectural", "Y_Specialist" };
            Assert.Equal("Y_Specialist", DisciplineFolderMatcher.Match(folders, "FP"));
        }

        [Fact]
        public void A_prefix_match_needs_the_underscore()
        {
            // "M" must not match "MEP_Services" by prefix alone.
            var folders = new[] { "MEP_Services", "M_Mechanical" };
            Assert.Equal("M_Mechanical", DisciplineFolderMatcher.Match(folders, "M"));
        }

        [Fact]
        public void No_folder_list_is_null()
            => Assert.Null(DisciplineFolderMatcher.Match(null, "A"));

        [Fact]
        public void The_role_segment_of_an_iso_sheet_number_finds_the_folder()
        {
            // Sheet number is the identifier: Project-Originator-Volume-Level-Type-Role-Number.
            var seg = Iso19650DocumentCode.Decompose("SAH-PLNS-ZZ-01-DR-S-0004");
            Assert.NotNull(seg);
            Assert.Equal("S_Structural", DisciplineFolderMatcher.Match(Bim, seg.Role));
        }
    }
}
