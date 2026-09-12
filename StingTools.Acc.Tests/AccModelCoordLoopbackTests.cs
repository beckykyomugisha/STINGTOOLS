// End-to-end tests for the ACC Model Coordination read client, driven over a real
// local HttpListener.
//
// These are the tests that actually close the defect. The pure-mapping suite proves the
// classifier is right; only these prove the CLIENT consults it — the old client mapped
// every failure to an empty list without any classifier being involved, so a pure
// mapping test would have passed against the broken code.
//
// The single most important assertion in this file is that a 404 does NOT produce
// EmptyOk. On the KUT fortnightly coordination cycle, that difference is a gate that
// checked the federation versus one that reported it clean without looking.

using System;
using System.Threading.Tasks;
using StingTools.Acc.Tests.TestHelpers;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccModelCoordLoopbackTests : IDisposable
    {
        private const string Container = "b.11111111-2222-3333-4444-555555555555";
        private const string ModelSet  = "ms-kut-federated";

        public AccModelCoordLoopbackTests()
        {
            // No real network: the back-off is neutered and the host is redirected per test.
            AccModelCoordSync.DelayHook = _ => Task.CompletedTask;
        }

        public void Dispose()
        {
            AccModelCoordSync.OverrideHostForTests(null);
            AccModelCoordSync.DelayHook = t => Task.Delay(t);
        }

        /// <summary>Credentials whose token is already fresh, so EnsureAuthAsync short-circuits
        /// and no test ever reaches the real Autodesk token endpoint or writes the user's
        /// %APPDATA%\Planscape\acc_credentials.json.</summary>
        private static AccCredentials FreshCreds() => new AccCredentials
        {
            ClientId = "test-client",
            ClientSecret = "test-secret",
            RefreshToken = "test-refresh",
            ProjectId = Container,
            AccessToken = "test-access-token",
            AccessTokenExpiry = DateTime.UtcNow.AddHours(1),
            IssueTypeId = "test-issue-type",
        };

        private static LoopbackServer Start(int status, string body)
        {
            var server = LoopbackServer.Always(status, body);
            AccModelCoordSync.OverrideHostForTests(server.BaseUrl);
            return server;
        }

        // ── The load-bearing case: a failure is not an empty result ─────────────

        [Fact]
        public async Task ListModelSets_Http404_IsNotFound_NotEmptyOk()
        {
            using var server = Start(404, "{\"detail\":\"not found\"}");

            var result = await AccModelCoordSync.ListModelSetsAsync(FreshCreds(), Container);

            Assert.Equal(AccFetchStatus.NotFound, result.Status);
            Assert.NotEqual(AccFetchStatus.EmptyOk, result.Status);
            Assert.False(result.Succeeded);
            Assert.Equal(404, result.HttpStatus);
            Assert.False(string.IsNullOrWhiteSpace(result.Detail));
            Assert.True(server.RequestCount >= 1, "the client must actually have made the request");
        }

        [Fact]
        public async Task GetClashes_Http404_IsNotFound_NotEmptyOk()
        {
            using var server = Start(404, "{\"detail\":\"not found\"}");

            var result = await AccModelCoordSync.GetClashesAsync(FreshCreds(), Container, ModelSet);

            Assert.Equal(AccFetchStatus.NotFound, result.Status);
            Assert.NotEqual(AccFetchStatus.EmptyOk, result.Status);
            Assert.False(result.Succeeded);
            Assert.Empty(result.Value);
            Assert.True(server.RequestCount >= 1);
        }

        [Fact]
        public async Task GetClashes_TransportDown_IsTransportFailed_NotEmptyOk()
        {
            // Nothing is listening on this port: SendAsync throws HttpRequestException.
            // The old shape let that propagate to a catch that produced an empty list.
            using (var probe = LoopbackServer.Always(200, "{}")) { AccModelCoordSync.OverrideHostForTests(probe.BaseUrl); }
            // probe disposed -> port closed, host override still pointing at it

            var result = await AccModelCoordSync.GetClashesAsync(FreshCreds(), Container, ModelSet);

            Assert.Equal(AccFetchStatus.TransportFailed, result.Status);
            Assert.NotEqual(AccFetchStatus.EmptyOk, result.Status);
            Assert.False(result.Succeeded);
            // HttpStatus 0 means NO response was received at all. Asserting the status
            // alone was an alternative-path escape: a swallowed HttpRequestException that
            // returned a bogus 200/EmptyOk still ended up TransportFailed downstream when
            // the empty payload failed the shape check. Deliberate sabotage found that,
            // and this line is what closes it — the failure must be attributed to the
            // transport, not to a payload that never arrived.
            Assert.Equal(0, result.HttpStatus);
            Assert.Contains("did not complete", result.Detail);
        }

        [Fact]
        public async Task ListModelSets_TransportDown_IsTransportFailed_NotEmptyOk()
        {
            using (var probe = LoopbackServer.Always(200, "{}")) { AccModelCoordSync.OverrideHostForTests(probe.BaseUrl); }

            var result = await AccModelCoordSync.ListModelSetsAsync(FreshCreds(), Container);

            Assert.Equal(AccFetchStatus.TransportFailed, result.Status);
            Assert.False(result.Succeeded);
            Assert.Equal(0, result.HttpStatus);
            Assert.Contains("did not complete", result.Detail);
        }

        [Fact]
        public async Task GetClashes_Http200_WithUnknownPayloadShape_IsTransportFailed_NotEmptyOk()
        {
            // What a changed APS sub-path looks like: 200, but not the payload we expect.
            using var server = Start(200, "{\"data\":{\"items\":[]}}");

            var result = await AccModelCoordSync.GetClashesAsync(FreshCreds(), Container, ModelSet);

            Assert.Equal(AccFetchStatus.TransportFailed, result.Status);
            Assert.NotEqual(AccFetchStatus.EmptyOk, result.Status);
            Assert.False(result.Succeeded);
        }

        // ── The other error mappings, end to end ────────────────────────────────

        [Fact]
        public async Task ListModelSets_Http401_IsAuthFailed()
        {
            using var server = Start(401, "{\"developerMessage\":\"token invalid\"}");
            var result = await AccModelCoordSync.ListModelSetsAsync(FreshCreds(), Container);
            Assert.Equal(AccFetchStatus.AuthFailed, result.Status);
            Assert.False(result.Succeeded);
        }

        [Fact]
        public async Task ListModelSets_Http500_IsTransportFailed()
        {
            using var server = Start(500, "{\"error\":\"boom\"}");
            var result = await AccModelCoordSync.ListModelSetsAsync(FreshCreds(), Container);
            Assert.Equal(AccFetchStatus.TransportFailed, result.Status);
            Assert.False(result.Succeeded);
        }

        [Fact]
        public async Task ListModelSets_MissingContainerId_IsNotFound_NotEmptyOk()
        {
            var result = await AccModelCoordSync.ListModelSetsAsync(FreshCreds(), "");
            Assert.Equal(AccFetchStatus.NotFound, result.Status);
            Assert.False(result.Succeeded);
        }

        // ── The success cases, so "everything fails" would not pass either ──────

        [Fact]
        public async Task ListModelSets_Http200_EmptyArray_IsEmptyOk()
        {
            using var server = Start(200, "{\"modelSets\":[]}");

            var result = await AccModelCoordSync.ListModelSetsAsync(FreshCreds(), Container);

            Assert.Equal(AccFetchStatus.EmptyOk, result.Status);
            Assert.True(result.Succeeded);
            Assert.Empty(result.Value);
        }

        [Fact]
        public async Task ListModelSets_Http200_OneSet_IsOk_AndCarriesTheData()
        {
            using var server = Start(200,
                "{\"modelSets\":[{\"modelSetId\":\"ms-kut-federated\",\"name\":\"KUT Federated\"}]}");

            var result = await AccModelCoordSync.ListModelSetsAsync(FreshCreds(), Container);

            Assert.Equal(AccFetchStatus.Ok, result.Status);
            Assert.True(result.Succeeded);
            Assert.Single(result.Value);                       // assert on non-empty data
            Assert.Equal("ms-kut-federated", result.Value[0].Id);
            Assert.Equal("KUT Federated", result.Value[0].Name);
        }

        [Fact]
        public async Task GetClashes_Http200_NoTestsRunYet_IsEmptyOk()
        {
            using var server = Start(200, "{\"tests\":[]}");

            var result = await AccModelCoordSync.GetClashesAsync(FreshCreds(), Container, ModelSet);

            Assert.Equal(AccFetchStatus.EmptyOk, result.Status);
            Assert.True(result.Succeeded);
            Assert.Empty(result.Value);
        }

        [Fact]
        public async Task GetClashes_FullChain_ReturnsClashes()
        {
            // tests -> resources -> three scope files -> join. Proves Ok is reachable, so
            // "return TransportFailed for everything" would not pass this suite either.
            LoopbackServer server = null;
            server = new LoopbackServer((_, req) =>
            {
                string path = req.Url.AbsolutePath;
                string b = server.BaseUrl;
                if (path.EndsWith("/tests"))
                    return new CannedResponse(200,
                        "{\"tests\":[{\"clashTestId\":\"T1\",\"status\":\"completed\",\"completedAt\":\"2026-09-01T00:00:00Z\"}]}");
                if (path.EndsWith("/resources"))
                    return new CannedResponse(200,
                        "{\"resources\":[" +
                        "{\"type\":\"clash\",\"url\":\"" + b + "/scope/clash\"}," +
                        "{\"type\":\"clash-instance\",\"url\":\"" + b + "/scope/inst\"}," +
                        "{\"type\":\"document\",\"url\":\"" + b + "/scope/doc\"}]}");
                if (path == "/scope/clash")
                    return new CannedResponse(200,
                        "{\"clashes\":[{\"id\":\"c1\",\"dist\":-0.05,\"status\":\"active\"}," +
                        "{\"id\":\"c2\",\"dist\":-0.12,\"status\":\"active\"}]}");
                if (path == "/scope/inst")
                    return new CannedResponse(200,
                        "{\"instances\":[{\"cid\":\"c1\",\"ldid\":\"d1\",\"rdid\":\"d2\",\"lvid\":11,\"rvid\":22}," +
                        "{\"cid\":\"c2\",\"ldid\":\"d1\",\"rdid\":\"d2\",\"lvid\":33,\"rvid\":44}]}");
                if (path == "/scope/doc")
                    return new CannedResponse(200,
                        "{\"documents\":[{\"id\":\"d1\",\"name\":\"KUT-STR-Model.rvt\"}," +
                        "{\"id\":\"d2\",\"name\":\"KUT-MEP-DUCT.rvt\"}]}");
                return new CannedResponse(404, "{}");
            });
            using (server)
            {
                AccModelCoordSync.OverrideHostForTests(server.BaseUrl);

                var result = await AccModelCoordSync.GetClashesAsync(FreshCreds(), Container, ModelSet);

                Assert.Equal(AccFetchStatus.Ok, result.Status);
                Assert.True(result.Succeeded);
                Assert.Equal(2, result.Value.Count);                 // assert on non-empty data
                Assert.Equal("c1", result.Value[0].Id);
                Assert.Equal("KUT-STR-Model.rvt", result.Value[0].LeftDocument);
                Assert.Equal("KUT-MEP-DUCT.rvt", result.Value[0].RightDocument);
                Assert.Equal(11, result.Value[0].LeftObjectId);
                Assert.Equal(50.0, result.Value[0].PenetrationMm, 3);  // 0.05 m * 1000
            }
        }

        [Fact]
        public async Task GetClashes_ScopeFileGone_IsTransportFailed_NotEmptyOk()
        {
            // The tests and resources hops succeed; the scope download 404s. Before the
            // split this produced an empty list, i.e. "clash-clean".
            LoopbackServer server = null;
            server = new LoopbackServer((_, req) =>
            {
                string path = req.Url.AbsolutePath;
                string b = server.BaseUrl;
                if (path.EndsWith("/tests"))
                    return new CannedResponse(200, "{\"tests\":[{\"clashTestId\":\"T1\",\"status\":\"completed\"}]}");
                if (path.EndsWith("/resources"))
                    return new CannedResponse(200,
                        "{\"resources\":[{\"type\":\"clash\",\"url\":\"" + b + "/scope/clash\"}," +
                        "{\"type\":\"clash-instance\",\"url\":\"" + b + "/scope/inst\"}]}");
                return new CannedResponse(404, "{\"detail\":\"expired\"}");
            });
            using (server)
            {
                AccModelCoordSync.OverrideHostForTests(server.BaseUrl);

                var result = await AccModelCoordSync.GetClashesAsync(FreshCreds(), Container, ModelSet);

                Assert.False(result.Succeeded);
                Assert.NotEqual(AccFetchStatus.EmptyOk, result.Status);
                Assert.Empty(result.Value);
            }
        }
    }
}
