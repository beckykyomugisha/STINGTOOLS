using System;
using System.IO;
using StingTools.Core.Validation;
using Xunit;
using O = StingTools.Core.Validation.OwnerStandardsRag.RuleOutcome;

namespace StingTools.Tags.Tests
{
    /// <summary>KUT deep review ISO-6 / ACC-13: an owner rule that examined nothing is not a pass.</summary>
    public class OwnerStandardsRagTests
    {
        [Fact]
        public void NothingTaggedIsNotGreen()
        {
            // discipline-code-valid skips blank values, so with nothing tagged it checks 0.
            // Old verdict: no violations -> GREEN.
            var v = OwnerStandardsRag.Verdict(new[] { new O("WARN", false, 0, 0), new O("BLOCK", false, 0, 0) });
            Assert.Equal(OwnerStandardsRag.NotAssessed, v);
        }

        [Fact]
        public void OneUnassessedRuleMakesTheResultIncomplete()
        {
            var v = OwnerStandardsRag.Verdict(new[] { new O("BLOCK", false, 40, 0), new O("WARN", true, 0, 0) });
            Assert.Equal(OwnerStandardsRag.Incomplete, v);
        }

        [Fact]
        public void FailuresStillDecideRedAndAmber()
        {
            Assert.Equal(OwnerStandardsRag.Red, OwnerStandardsRag.Verdict(new[] { new O("BLOCK", false, 5, 1), new O("WARN", false, 0, 0) }));
            Assert.Equal(OwnerStandardsRag.Amber, OwnerStandardsRag.Verdict(new[] { new O("BLOCK", false, 5, 0), new O("WARN", false, 3, 2) }));
        }

        [Fact]
        public void GreenNeedsEveryGatingRuleAssessed()
        {
            Assert.Equal(OwnerStandardsRag.Green, OwnerStandardsRag.Verdict(new[]
            {
                new O("BLOCK", false, 5, 0), new O("WARN", false, 9, 0),
                new O("INFO", false, 0, 0),   // INFO never decides the colour
            }));
        }

        [Fact]
        public void TheStateOfAnEmptyRuleSaysSo()
        {
            Assert.Equal("NOT ASSESSED (0 checked)", OwnerStandardsRag.State(new O("WARN", false, 0, 0)));
            Assert.Equal("ok", OwnerStandardsRag.State(new O("WARN", false, 7, 0)));
        }

        [Fact]
        public void TheAuditUsesThisVerdict()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            string src = File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "Commands", "Validation", "OwnerStandardsAuditCommand.cs"));
            Assert.Contains("OwnerStandardsRag.Verdict(", src);
            Assert.DoesNotContain("warnFail > 0 ? \"AMBER\" : \"GREEN\"", src);
        }
    }
}
