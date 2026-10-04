using System.Collections.Generic;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DT-R11 (Drawing Self-Test g, Revit 2025): on a model whose Project Information
    /// had no project code or originator, the Profile and ISO policies built
    /// "{project}-{originator}-01-00-DR-A-0001" — the two parameter-backed tokens were
    /// absent from the token dictionary, so no substitution touched them and the
    /// braces reached the number. A known token is never left in braces: absent, it is
    /// the visible "XX" every other empty drawing-type segment already prints.
    /// </summary>
    public class SheetNumberKnownTokenTests
    {
        private const string IsoPattern = "{project}-{originator}-{vol}-{lvl}-{type}-{role}-{seq:D4}";

        private static Dictionary<string, string> ExtrasWithoutProjectOrOriginator() =>
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "vol", "01" }, { "type", "DR" }, { "role", "A" }, { "suit", "" }, { "rev", "" },
            };

        [Fact]
        public void Self_test_case_absent_project_and_originator_print_XX()
            => Assert.Equal("XX-XX-01-00-DR-A-0001",
                SheetNumberEngine.ApplyTokenPattern(IsoPattern, "A", "00", "", "", "", "Plan", 1,
                    ExtrasWithoutProjectOrOriginator()));

        [Theory]
        [InlineData("{proj}-{seq:D3}", "XX-001")]
        [InlineData("{orig}-{seq:D3}", "XX-001")]
        [InlineData("{vol}{type}{role}{suit}{rev}-{seq:D3}", "XXXXXXXXXX-001")]
        public void Every_known_spelling_resolves_even_with_no_extras(string pattern, string expected)
            => Assert.Equal(expected,
                SheetNumberEngine.ApplyTokenPattern(pattern, "A", "L01", "", "", "", "Plan", 1, null));

        [Fact]
        public void A_supplied_value_still_wins_through_any_alias()
            => Assert.Equal("KUT-PLN-001",
                SheetNumberEngine.ApplyTokenPattern("{proj}-{originator}-{seq:D3}", "A", "L01", "", "", "", "Plan", 1,
                    new Dictionary<string, string> { { "project", "KUT" }, { "orig", "PLN" } }));

        [Fact]
        public void A_supplied_empty_value_is_the_callers_and_is_kept()
            => Assert.Equal("A--001",
                SheetNumberEngine.ApplyTokenPattern("A-{suit}-{seq:D3}", "A", "L01", "", "", "", "Plan", 1,
                    new Dictionary<string, string> { { "suit", "" } }));

        [Fact]
        public void An_unknown_token_is_left_for_the_audit_to_report()
            => Assert.Equal("A-{bogus}-001",
                SheetNumberEngine.ApplyTokenPattern("A-{bogus}-{seq:D3}", "A", "L01", "", "", "", "Plan", 1, null));

        [Fact]
        public void The_sheet_name_resolves_the_same_way()
            => Assert.Equal("XX Level 2",
                SheetNumberEngine.ApplyNamePattern("{project} {lvl}", "A", "Level 2", "", "", "", "Plan", 1, null));
    }
}
