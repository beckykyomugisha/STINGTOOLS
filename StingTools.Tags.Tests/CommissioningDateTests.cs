using System;
using System.IO;
using StingTools.Core.Validation;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT deep review MEP-5. QR commissioning wrote COMM_DATE_TXT as yyyy-MM-ddTHH:mm:ssZ on
    /// every state change, so the date rule rejected it (every Tier A item commissioned through
    /// STING's own workflow failed LOD 500) and it recorded the last change, not commissioning.
    /// </summary>
    public class CommissioningDateTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 4, 14, 30, 0, DateTimeKind.Utc);

        [Fact]
        public void CommissioningWritesAConformingDate()
        {
            string v = CommissioningDate.ValueFor("COMMISSIONED", "", Now);
            Assert.Equal("2026-10-04", v);
            Assert.True(DateFormatRule.IsConforming(v));
        }

        [Theory]
        [InlineData("RECEIVED")]
        [InlineData("INSTALLED")]
        [InlineData("TESTED")]
        [InlineData("HANDOVER")]
        public void OtherStatesLeaveTheDateAlone(string state)
        {
            Assert.Null(CommissioningDate.ValueFor(state, "", Now));
            Assert.Null(CommissioningDate.ValueFor(state, "2026-09-01", Now));
        }

        [Fact]
        public void HandoverKeepsTheCommissioningDate_NotTheHandoverDay()
        {
            Assert.Null(CommissioningDate.ValueFor("HANDOVER", "2026-09-01", Now));
        }

        [Fact]
        public void AnOldTimestampIsCutToItsOwnDate()
        {
            Assert.Equal("2026-09-01", CommissioningDate.ValueFor("HANDOVER", "2026-09-01T08:15:00Z", Now));
        }

        [Fact]
        public void TheQrWorkflowNoLongerStampsATimestamp()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            string src = File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "V6", "QRCommissioningWorkflow.cs"));
            Assert.DoesNotContain("SetString(el, ParamRegistry.COMM_DATE_TXT, now", src);
            Assert.Contains("CommissioningDate.ValueFor(", src);
        }
    }
}
