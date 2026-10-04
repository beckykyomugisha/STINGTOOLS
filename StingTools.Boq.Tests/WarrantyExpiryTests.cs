using System;
using System.IO;
using StingTools.Core;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// KUT deep review MEP-3. The Warranty Tracker wrote today + a per-category default (5 / 3 /
    /// 2 / 5 / 10 years) as MNT_WARRANTY_EXPIRY_TXT on every empty element, and that invented
    /// date then satisfied the KUT LOD-500 expiry requirement. An expiry is now only ever
    /// computed from a start and a duration the element records.
    /// </summary>
    public class WarrantyExpiryTests
    {
        private static readonly DateTime Today = new DateTime(2026, 10, 4);

        [Fact]
        public void NothingRecordedMeansMissing_NotADefault()
        {
            var p = WarrantyExpiry.Plan("", "", "", "", Today);
            Assert.Equal(WarrantyState.MissingStart, p.State);
            Assert.False(p.ShouldWrite);
            Assert.Equal("", p.Expiry);
            Assert.Null(p.Expired);
        }

        [Fact]
        public void StartWithoutDurationIsMissingDuration()
        {
            var p = WarrantyExpiry.Plan("", "2026-03-01", "", "", Today);
            Assert.Equal(WarrantyState.MissingDuration, p.State);
            Assert.False(p.ShouldWrite);
        }

        [Fact]
        public void ComputedFromInstallationPlusDuration()
        {
            var p = WarrantyExpiry.Plan("", "2026-03-01", "2", "", Today);
            Assert.Equal(WarrantyState.Computed, p.State);
            Assert.True(p.ShouldWrite);
            Assert.Equal("2028-03-01", p.Expiry);
            Assert.False(p.Expired);
        }

        [Theory]
        [InlineData("18", "months", "2027-09-01")]
        [InlineData("18 months", "", "2027-09-01")]
        [InlineData("1.5", "years", "2027-09-01")]
        public void MonthsAndYears(string amount, string unit, string expected) =>
            Assert.Equal(expected, WarrantyExpiry.Plan("", "2026-03-01", amount, unit, Today).Expiry);

        [Fact]
        public void ARecordedExpiryIsKeptAndJudged()
        {
            var p = WarrantyExpiry.Plan("2025-01-01", "2026-03-01", "5", "", Today);
            Assert.Equal(WarrantyState.Recorded, p.State);
            Assert.False(p.ShouldWrite);
            Assert.True(p.Expired);
        }

        [Theory]
        [InlineData("01/03/2026", "2")]   // not ISO — never guessed as day/month or month/day
        [InlineData("2026-03-01", "five")]
        [InlineData("2026-03-01", "-1")]
        public void UnreadableIsNeverWritten(string start, string dur)
        {
            var p = WarrantyExpiry.Plan("", start, dur, "", Today);
            Assert.Equal(WarrantyState.Unreadable, p.State);
            Assert.False(p.ShouldWrite);
        }

        [Fact]
        public void TheTrackerNoLongerCarriesDefaultPeriods()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            string src = File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "Temp", "IoTMaintenanceCommands.cs"));
            int a = src.IndexOf("class WarrantyTrackerCommand", StringComparison.Ordinal);
            Assert.True(a > 0);
            int b = src.IndexOf("class HandoverPackageCommand", a, StringComparison.Ordinal);
            string body = src.Substring(a, b - a);
            Assert.DoesNotContain("AddYears(", body);
            Assert.DoesNotContain("WarrantyPeriods", body);
            Assert.Contains("WarrantyExpiry.Plan(", body);
        }
    }
}
