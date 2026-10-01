// H-1: the sign-in lifetime the ACC card and ACC_SelfCheck report. Autodesk documents refresh
// tokens as valid for 14 days; the code said 15, so on day 14 the card read "lapses in 1 day"
// for a sign-in that had already lapsed, and the red (< 3 days) warning came a day late.

using System;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccSignInLifetimeTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

        private static AccCredentials Signed(DateTime issued) => new AccCredentials
        {
            ClientId = "c", RefreshToken = "r", RefreshTokenIssuedAt = issued,
        };

        [Fact]
        public void TheLifetimeIsTheDocumentedFourteenDays()
            => Assert.Equal(TimeSpan.FromDays(14), AccSignInLifetime.RefreshTokenLifetime);

        [Fact]
        public void OnDayFourteenTheSignInIsReportedExpired_NotOneDayLeft()
        {
            var c = Signed(T0);
            Assert.Equal(0, AccSignInLifetime.DaysRemaining(c, T0.AddDays(14))!.Value, 3);
            Assert.Contains("EXPIRED", AccSignInLifetime.Describe(c, T0.AddDays(14)));
        }

        [Fact]
        public void TheWarningStartsThreeDaysBeforeTheRealLapse()
        {
            var c = Signed(T0);
            Assert.DoesNotContain("lapses in", AccSignInLifetime.Describe(c, T0.AddDays(10.9)).Replace("renews automatically (lapses in", ""));
            Assert.Contains("sign in again to be safe", AccSignInLifetime.Describe(c, T0.AddDays(11.5)));
        }

        [Fact]
        public void UnknownIssueTimeAndNoTokenAreSaidAsSuch()
        {
            Assert.Null(AccSignInLifetime.DaysRemaining(Signed(default), T0));
            Assert.Contains("age unknown", AccSignInLifetime.Describe(Signed(default), T0));
            Assert.Equal("Not signed in to Autodesk.", AccSignInLifetime.Describe(new AccCredentials(), T0));
        }

        [Fact]
        public void KeepAliveRefreshesAfterThreeDaysAndNeedsAClientId()
        {
            Assert.False(AccSignInLifetime.ShouldKeepAlive(Signed(T0), T0.AddDays(2.9)));
            Assert.True(AccSignInLifetime.ShouldKeepAlive(Signed(T0), T0.AddDays(3)));
            Assert.False(AccSignInLifetime.ShouldKeepAlive(new AccCredentials { RefreshToken = "r", RefreshTokenIssuedAt = T0 }, T0.AddDays(5)));
        }
    }
}
