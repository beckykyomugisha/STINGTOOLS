// P11: the background keep-alive loaded the credentials, refreshed over the network, then wrote
// the whole object back - so a hub, project or client id saved from the ACC card during the
// refresh was overwritten with the values read before it. Only the tokens may change now.

using System;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccRefreshPersistTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

        private static AccCredentials Refreshed() => new AccCredentials
        {
            ClientId = "app-1", HubId = "b.hub-old", ProjectId = "proj-old", FolderUrn = "urn:old",
            AccessToken = "at-new", AccessTokenExpiry = T0.AddHours(1),
            RefreshToken = "rt-new", RefreshTokenIssuedAt = T0,
        };

        [Fact]
        public void A_refresh_changes_only_the_tokens_on_the_file_as_it_is_now()
        {
            var file = new AccCredentials
            {
                ClientId = "app-1", HubId = "b.hub-new", ProjectId = "proj-new", FolderUrn = "urn:new", Region = "EMEA",
                AccessToken = "at-old", RefreshToken = "rt-old", RefreshTokenIssuedAt = T0.AddDays(-4),
            };
            var merged = AccIssueSync.MergeRefreshedTokens(file, Refreshed(), out string refused);

            Assert.Null(refused);
            Assert.NotNull(merged);
            Assert.Equal("b.hub-new", merged.HubId);       // saved during the refresh: kept
            Assert.Equal("proj-new", merged.ProjectId);
            Assert.Equal("urn:new", merged.FolderUrn);
            Assert.Equal("EMEA", merged.Region);
            Assert.Equal("at-new", merged.AccessToken);    // the refresh: applied
            Assert.Equal("rt-new", merged.RefreshToken);
            Assert.Equal(T0, merged.RefreshTokenIssuedAt);
            Assert.Equal(T0.AddHours(1), merged.AccessTokenExpiry);
        }

        [Fact]
        public void A_file_that_now_names_another_app_is_not_overwritten_with_the_old_apps_tokens()
        {
            var file = new AccCredentials { ClientId = "app-2", RefreshToken = "" };
            var merged = AccIssueSync.MergeRefreshedTokens(file, Refreshed(), out string refused);
            Assert.Null(merged);
            Assert.Contains("different app", refused);
        }

        [Fact]
        public void No_sign_in_on_file_yet_writes_the_callers_object_as_before()
        {
            Assert.Null(AccIssueSync.MergeRefreshedTokens(null, Refreshed(), out string r1));
            Assert.Null(r1);
            Assert.Null(AccIssueSync.MergeRefreshedTokens(new AccCredentials(), Refreshed(), out string r2));
            Assert.Null(r2);
        }
    }
}
