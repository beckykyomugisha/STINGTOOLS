using System;
using System.IO;
using StingTools.Core.Validation;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT deep review ACC-5. "LOD Check" printed <c>OverallPct</c>, which is 100.0 when nothing
    /// was verified, so an empty scope or a rung that asserts nothing read "Pass rate 100.0%".
    /// LOD_Verify already said NOT ASSESSED for the same run. One function now serves both.
    /// </summary>
    public class LodPassRateTextTests
    {
        [Fact]
        public void AnEmptyScopeIsNotAPassRate()
        {
            var t = new LodTally();
            Assert.Equal(100.0, t.OverallPct);              // the trap this guards against
            Assert.StartsWith("NO ELEMENTS IN SCOPE", t.PassRateText());
        }

        [Fact]
        public void ARungThatAssertsNothingIsNotAssessed()
        {
            var t = new LodTally();
            t.RecordNotAssessed("Walls");
            Assert.StartsWith("NOT ASSESSED", t.PassRateText());
        }

        [Fact]
        public void ARealRunPrintsItsPercentage()
        {
            var t = new LodTally { Total = 4, Passed = 3 };
            Assert.Equal("75.0%", t.PassRateText());
        }

        [Fact]
        public void LodCheckDoesNotPrintTheRawPercentage()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            string src = File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "Core", "LODValidationCommand.cs"));
            Assert.DoesNotContain("OverallPct", src);
            Assert.Contains("PassRateText()", src);
        }
    }
}
