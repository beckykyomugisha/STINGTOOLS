// The Project Setup Wizard's "Rename scope boxes": only names STING reads.

using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class ScopeBoxRenamePatternTests
    {
        private static List<ScopeBoxRenameRow> Rows(params string[] names)
            => names.Select(n => new ScopeBoxRenameRow { CurrentName = n }).ToList();

        [Fact]
        public void The_default_pattern_gives_zone_boxes_the_tagger_reads()
        {
            var r = ScopeBoxRenamePattern.Apply(null, Rows("Scope Box 1", "Scope Box 2"),
                new[] { "BLD1" }, new[] { "Z01", "Z02" });
            Assert.Equal(new[] { "STING-ZONE::Z01", "STING-ZONE::Z02" }, r.Select(x => x.NewName).ToArray());
            Assert.All(r, x => Assert.True(ScopeBoxNames.TryParseZone(x.NewName, out _, out _)));
        }

        [Fact]
        public void The_old_default_pattern_is_refused_because_nothing_reads_it()
        {
            var r = ScopeBoxRenamePattern.Apply("{BLD}-{ZONE}-{INDEX}", Rows("Scope Box 1"), new[] { "BLD1" }, new[] { "Z01" });
            Assert.Null(r[0].NewName);
            Assert.Contains("not a name STING reads", r[0].Problem);
        }

        [Fact]
        public void A_loc_pattern_works_and_a_repeated_name_is_refused_on_the_later_box()
        {
            var r = ScopeBoxRenamePattern.Apply("STING-LOC::{LOC}", Rows("a", "b", "c"),
                new[] { "BLD1", "BLD2" }, null);
            Assert.Equal("STING-LOC::BLD1", r[0].NewName);
            Assert.Equal("STING-LOC::BLD2", r[1].NewName);
            Assert.Null(r[2].NewName);                  // cycles back to BLD1
            Assert.Contains("already another box's name", r[2].Problem);
        }

        [Fact]
        public void A_name_another_box_already_has_is_refused()
        {
            var r = ScopeBoxRenamePattern.Apply(null, Rows("x"), null, new[] { "Z01" }, new[] { "STING-ZONE::Z01" });
            Assert.Null(r[0].NewName);
        }

        [Fact]
        public void A_rotated_box_is_refused_and_a_quarter_turn_is_not_rotated()
        {
            Assert.True(ScopeBoxRenamePattern.IsRotated(12.5));
            Assert.False(ScopeBoxRenamePattern.IsRotated(0));
            Assert.False(ScopeBoxRenamePattern.IsRotated(90));
            Assert.False(ScopeBoxRenamePattern.IsRotated(-180));
            var rows = new List<ScopeBoxRenameRow> { new ScopeBoxRenameRow { CurrentName = "t", Rotated = true } };
            var r = ScopeBoxRenamePattern.Apply(null, rows, null, new[] { "Z01" });
            Assert.Null(r[0].NewName);
            Assert.Contains("rotated", r[0].Problem);
        }

        [Theory]
        [InlineData("STING-ZONE::Z01", true)]
        [InlineData("STING-LOC::BLD2", true)]
        [InlineData("STING-ZONE::Z 01", false)]        // a space would land in the tag
        [InlineData("STING-LOC::", false)]
        [InlineData("STING-AREA::A01", false)]         // only the planner's plan makes these produce
        [InlineData("STING-SEED::30x20", false)]
        [InlineData("STING::mep-plan-A1-1to100::L01", false)]
        [InlineData("BLD1-Z01-01", false)]
        [InlineData("", false)]
        public void Check_accepts_only_loc_and_zone_names(string name, bool ok)
        {
            Assert.Equal(ok, ScopeBoxRenamePattern.Check(name, rotated: false) == null);
        }
    }
}
