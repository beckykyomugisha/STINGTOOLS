// Tests for ACC hub/project discovery, driven over a real local HttpListener.
//
// What these are for. Discovery exists so a coordinator can read back the ACC project
// they already have access to, instead of asking an administrator to transcribe a GUID.
// That makes it a CONFIGURATION-TIME convenience — and configuration-time code that
// guesses is worse than configuration-time code that is absent, because the guess is
// then baked into every later run.
//
// So the load-bearing assertions here are the ones about NOT inventing:
//   * a failed listing is never an empty listing (the house defect, third pass running);
//   * an id is reported exactly as APS returned it, never re-prefixed or trimmed to fit
//     what a container id is assumed to look like.

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using StingTools.Acc.Tests.TestHelpers;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccProjectDiscoveryTests : IDisposable
    {
        public void Dispose() => AccProjectDiscovery.OverrideHostForTests(null);

        /// <summary>Token already fresh, so EnsureAuthAsync short-circuits and no test
        /// reaches the real Autodesk endpoints or the user's credentials file.</summary>
        private static AccCredentials FreshCreds() => new AccCredentials
        {
            ClientId = "test-client",
            ClientSecret = "test-secret",
            RefreshToken = "test-refresh",
            AccessToken = "test-access-token",
            AccessTokenExpiry = DateTime.UtcNow.AddHours(1),
        };

        private static LoopbackServer Start(int status, string body)
        {
            var server = LoopbackServer.Always(status, body);
            AccProjectDiscovery.OverrideHostForTests(server.BaseUrl);
            return server;
        }

        private const string TwoHubs = @"{""data"":[
            {""type"":""hubs"",""id"":""b.hub-one"",""attributes"":{""name"":""Planscape"",""region"":""US""}},
            {""type"":""hubs"",""id"":""b.hub-two"",""attributes"":{""name"":""Owner Account"",""region"":""EMEA""}}]}";

        private const string TwoProjects = @"{""data"":[
            {""type"":""projects"",""id"":""b.proj-kut"",""attributes"":{""name"":""Kampala Uganda Temple""}},
            {""type"":""projects"",""id"":""b.proj-other"",""attributes"":{""name"":""Another Job""}}]}";

        // ── The load-bearing case: a failure is never an empty listing ──────────

        [Theory]
        [InlineData(401, AccFetchStatus.AuthFailed)]
        [InlineData(403, AccFetchStatus.AuthFailed)]
        [InlineData(404, AccFetchStatus.NotFound)]
        [InlineData(500, AccFetchStatus.TransportFailed)]
        public async Task AFailedHubListing_IsNotAnEmptyOne(int status, AccFetchStatus expected)
        {
            using var _ = Start(status, "{}");
            var r = await AccProjectDiscovery.ListHubsAsync(FreshCreds());

            Assert.False(r.Succeeded);
            Assert.Equal(expected, r.Status);
            Assert.NotEqual(AccFetchStatus.EmptyOk, r.Status);
            Assert.Empty(r.Value);                       // empty data, but NOT a success
            Assert.False(string.IsNullOrWhiteSpace(r.Detail));
        }

        [Fact]
        public async Task AHubBodyThatIsNotAHubList_IsATransportFailure_NotAnEmptyAccount()
        {
            // 200 with a shape we do not read. This is what a changed API, a proxy login
            // page or a JSON error envelope looks like, and it must not read as
            // "this user belongs to no hubs".
            using var _ = Start(200, @"{""error"":""unauthorized"",""detail"":""no""}");
            var r = await AccProjectDiscovery.ListHubsAsync(FreshCreds());

            Assert.False(r.Succeeded);
            Assert.Equal(AccFetchStatus.TransportFailed, r.Status);
        }

        [Fact]
        public async Task ATransportDown_IsAttributedToTheTransport()
        {
            // Nothing listening. HttpStatus 0 proves the failure was attributed to the
            // request never completing, rather than inferred from a payload check
            // downstream — the alternative-path escape that hid a bug two passes ago.
            AccProjectDiscovery.OverrideHostForTests("http://127.0.0.1:1");
            var r = await AccProjectDiscovery.ListHubsAsync(FreshCreds());

            Assert.False(r.Succeeded);
            Assert.Equal(AccFetchStatus.TransportFailed, r.Status);
            Assert.Equal(0, r.HttpStatus);
        }

        [Fact]
        public async Task AnAccountWithNoHubs_IsEmptyOk_NotAFailure()
        {
            // The legitimate empty case must survive. Over-correcting emptiness into an
            // error would be worse than the defect: a real new account has no hubs yet.
            using var _ = Start(200, @"{""data"":[]}");
            var r = await AccProjectDiscovery.ListHubsAsync(FreshCreds());

            Assert.True(r.Succeeded);
            Assert.Equal(AccFetchStatus.EmptyOk, r.Status);
            Assert.Empty(r.Value);
        }

        // ── Parsing, and the ids we must not touch ──────────────────────────────

        [Fact]
        public async Task HubsAreParsed_WithIdAndNameAndRegion()
        {
            using var _ = Start(200, TwoHubs);
            var r = await AccProjectDiscovery.ListHubsAsync(FreshCreds());

            Assert.True(r.Succeeded);
            Assert.Equal(AccFetchStatus.Ok, r.Status);
            Assert.Equal(2, r.Value.Count);              // non-empty: the count is the point
            Assert.Equal("b.hub-one", r.Value[0].Id);
            Assert.Equal("Planscape", r.Value[0].Name);
            Assert.Equal("US", r.Value[0].Region);
            Assert.Equal("Owner Account", r.Value[1].Name);
        }

        [Fact]
        public async Task ProjectsAreParsed_AndCarryTheirHub()
        {
            using var _ = Start(200, TwoProjects);
            var r = await AccProjectDiscovery.ListProjectsAsync(FreshCreds(), "b.hub-one");

            Assert.True(r.Succeeded);
            Assert.Equal(2, r.Value.Count);
            var kut = r.Value.Single(p => p.Name == "Kampala Uganda Temple");
            Assert.Equal("b.proj-kut", kut.Id);
            Assert.Equal("b.hub-one", kut.HubId);        // a flat list stays unambiguous
        }

        [Fact]
        public async Task TheProjectIdIsReportedExactlyAsAutodeskReturnedIt()
        {
            // THE assertion this feature turns on. Discovery is allowed to read an id
            // back; it is not allowed to decide what shape a container id "should" be.
            // Stripping or adding the "b." prefix to make it look like the runbook's
            // example would produce a container id nobody chose — and a wrong container
            // reads as a clean federation, which is the failure #927 exists to prevent.
            const string odd = "NOT.b-prefixed_AT-all";
            using var _ = Start(200,
                @"{""data"":[{""type"":""projects"",""id"":""" + odd + @""",""attributes"":{""name"":""Odd""}}]}");

            var r = await AccProjectDiscovery.ListProjectsAsync(FreshCreds(), "b.hub-one");

            Assert.True(r.Succeeded);
            Assert.Equal(odd, r.Value.Single().Id);
        }

        [Fact]
        public async Task AProjectWithNoId_IsSkipped_NotInventedFrom()
        {
            // An entry we cannot identify is dropped rather than given a placeholder id.
            // A row in the picker that resolves to nothing is worse than a shorter list.
            using var _ = Start(200,
                @"{""data"":[{""type"":""projects"",""attributes"":{""name"":""No Id Here""}},
                             {""type"":""projects"",""id"":""b.ok"",""attributes"":{""name"":""Fine""}}]}");

            var r = await AccProjectDiscovery.ListProjectsAsync(FreshCreds(), "b.hub-one");

            Assert.True(r.Succeeded);
            Assert.Equal("b.ok", r.Value.Single().Id);
        }

        // ── Flattening across hubs ──────────────────────────────────────────────

        [Fact]
        public async Task ListAll_WalksEveryHub_AndNamesTheHubOnEachProject()
        {
            using var server = new LoopbackServer((n, req) =>
                req.Url.AbsolutePath.EndsWith("/hubs", StringComparison.OrdinalIgnoreCase)
                    ? new CannedResponse(200, TwoHubs)
                    : new CannedResponse(200, TwoProjects));
            AccProjectDiscovery.OverrideHostForTests(server.BaseUrl);

            var r = await AccProjectDiscovery.ListAllProjectsAsync(FreshCreds());

            Assert.True(r.Succeeded);
            Assert.Equal(4, r.Value.Count);                          // 2 hubs x 2 projects
            Assert.Contains(r.Value, p => p.HubName == "Planscape");
            Assert.Contains(r.Value, p => p.HubName == "Owner Account");
        }

        [Fact]
        public async Task ListAll_WhenTheHubListingFails_FailsRatherThanReturningNoProjects()
        {
            using var _ = Start(403, "{}");
            var r = await AccProjectDiscovery.ListAllProjectsAsync(FreshCreds());

            Assert.False(r.Succeeded);
            Assert.Equal(AccFetchStatus.AuthFailed, r.Status);
        }

        [Fact]
        public async Task ListAll_WhenOneHubFails_SaysSo_RatherThanSilentlyReturningTheRest()
        {
            // A partial answer presented as a complete one is the AccIssueSync pagination
            // defect in another costume. The user is choosing a project from this list; a
            // silently short list means "your project is not in ACC", which is a lie.
            using var server = new LoopbackServer((n, req) =>
            {
                if (req.Url.AbsolutePath.EndsWith("/hubs", StringComparison.OrdinalIgnoreCase))
                    return new CannedResponse(200, TwoHubs);
                return req.Url.AbsolutePath.Contains("hub-two")
                    ? new CannedResponse(500, "{}")
                    : new CannedResponse(200, TwoProjects);
            });
            AccProjectDiscovery.OverrideHostForTests(server.BaseUrl);

            var r = await AccProjectDiscovery.ListAllProjectsAsync(FreshCreds());

            Assert.False(r.Succeeded);
            Assert.Contains("hub", r.Detail, StringComparison.OrdinalIgnoreCase);
        }

        // ── The seam that made this necessary ───────────────────────────────────

        [Fact]
        public async Task DiscoveryUsesTheDocumentedApsPath()
        {
            // If the path is wrong the call 404s against a real tenant, and a
            // configuration-time feature that fails only in production is worse than none.
            using var server = LoopbackServer.Always(200, TwoHubs);
            AccProjectDiscovery.OverrideHostForTests(server.BaseUrl);

            await AccProjectDiscovery.ListHubsAsync(FreshCreds());

            Assert.NotEmpty(server.Paths);
            Assert.Contains(server.Paths, p => p.Contains("/project/v1/hubs"));
        }
    }
}
