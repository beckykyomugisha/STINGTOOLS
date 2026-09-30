// R2: ACC_UploadLastBundle inside a workflow run uploads only the bundle that run built.
using System;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccBundleRecordRunTests
    {
        private static readonly DateTime RunStart = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

        private static AccBundleRecord Built(DateTime utc) =>
            new AccBundleRecord { Path = @"C:\x\ACC_PUBLISH_KUT.zip", CreatedUtc = utc, Suitability = "S2", DeliverableCount = 3 };

        [Fact]
        public void ABundleBuiltBeforeTheRun_IsRefused_WithTheReason()
        {
            Assert.False(Built(RunStart.AddDays(-14)).IsFromThisRun(RunStart, out string why));
            Assert.Contains("before this workflow run", why);
            Assert.Contains("ACC_PUBLISH_KUT.zip", why);
        }

        [Fact]
        public void ABundleBuiltDuringTheRun_Goes()
        {
            Assert.True(Built(RunStart.AddMinutes(3)).IsFromThisRun(RunStart, out string why));
            Assert.Equal("", why);
            Assert.True(Built(RunStart).IsFromThisRun(RunStart, out _));
        }

        [Fact]
        public void OutsideARun_AnyBundleGoes()
            => Assert.True(Built(RunStart.AddYears(-1)).IsFromThisRun(null, out _));
    }
}
