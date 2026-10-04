using System;
using System.IO;
using StingTools.Commands.Kpi;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT deep review ACC-10 / ACC-12. "No clash run" was counted as 0 open clashes, so a model
    /// never clash-tested earned the full 25 clash points; and "Sheet ISO 19650 compliance" was the
    /// share of sheets with SHT_TAG_1 filled, not the five ISO checks.
    /// </summary>
    public class OwnerKpiHealthTests
    {
        [Fact]
        public void NoClashRunEarnsNoClashPoints()
        {
            // compliance 50, warnings/stale perfect. Old formula: 0.40*50 + 0.25*100 + 0.20*100 + 0.15*100 = 80.
            var r = OwnerKpiHealth.Compute(1000, 50, clashRunFound: false, openClashes: 0, warnings: 0, stale: 0);
            Assert.NotNull(r.Score);
            Assert.True(r.Score < 80, $"no clash run must not score like a clean clash run: {r.Score}");
            Assert.Equal(Math.Round((0.40 * 50 + 0.20 * 100 + 0.15 * 100) / 0.75, 1), r.Score);
            Assert.Contains("NO STING clash run", r.Basis);
        }

        [Fact]
        public void WithARunTheOriginalWeightsApply()
        {
            var r = OwnerKpiHealth.Compute(1000, 50, clashRunFound: true, openClashes: 100, warnings: 0, stale: 0);
            Assert.Equal(Math.Round(0.40 * 50 + 0.25 * 50 + 0.20 * 100 + 0.15 * 100, 1), r.Score);
        }

        [Fact]
        public void NothingInScopeHasNoScore()
        {
            var r = OwnerKpiHealth.Compute(0, 0, clashRunFound: false, openClashes: 0, warnings: 0, stale: 0);
            Assert.Null(r.Score);
            Assert.StartsWith("n/a", r.Basis);
        }

        [Fact]
        public void TheDashboardUsesTheHelperAndTheHonestLabel()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            string s = File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "Commands", "Kpi", "OwnerKpiDashboardCommand.cs"));
            Assert.Contains("OwnerKpiHealth.Compute(", s);
            Assert.DoesNotContain("0.25 * clashClean", s);
            Assert.DoesNotContain("\"Sheet ISO 19650 compliance\"", s);
        }
    }
}
