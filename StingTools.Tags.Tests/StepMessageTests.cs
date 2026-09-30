// A command's result dialog as one line for the workflow report.

using System.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class StepMessageTests
    {
        [Fact]
        public void A_dialog_body_becomes_one_line_without_rules_or_bullets()
        {
            var body = "Match-line sweep complete.\n\n════════\nPairs created           : 4\n  · first warning\n\n";
            Assert.Equal("Match Lines: Match-line sweep complete.; Pairs created : 4; first warning",
                StepMessage.Summarise("Match Lines", body));
        }

        [Fact]
        public void Long_bodies_are_cut_and_say_so()
        {
            var body = string.Join("\n", Enumerable.Range(1, 20).Select(i => "line " + i));
            var s = StepMessage.Summarise("T", body, maxLines: 3);
            Assert.Equal("T: line 1; line 2; line 3 …", s);
            var capped = StepMessage.Summarise("T", new string('x', 1000), maxChars: 50);
            Assert.Equal(50, capped.Length);
            Assert.EndsWith("…", capped);
        }

        [Fact]
        public void No_title_or_no_body_still_reads()
        {
            Assert.Equal("only body", StepMessage.Summarise(null, "only body"));
            Assert.Equal("Title", StepMessage.Summarise("Title", "\n═══\n"));
        }
    }
}
