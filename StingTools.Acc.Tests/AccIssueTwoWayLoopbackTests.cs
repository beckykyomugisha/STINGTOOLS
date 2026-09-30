// The real two-way clients over a loopback listener: PATCH / comment / GET one issue, the
// incremental filter on the list read, and custom-attribute resolution. Requests are counted
// SERVER-side, which is what proves a non-idempotent write was not retried.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Acc.Tests.TestHelpers;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccIssueTwoWayLoopbackTests : IDisposable
    {
        public AccIssueTwoWayLoopbackTests() => AccIssueSync.DelayHook = _ => Task.CompletedTask;

        public void Dispose()
        {
            AccIssueSync.OverrideHostForTests(null);
            AccIssueSync.DelayHook = t => Task.Delay(t);
        }

        private static AccCredentials Creds() => new AccCredentials
        {
            ClientId = "test-client", ClientSecret = "test-secret", RefreshToken = "test-refresh",
            ProjectId = "b.11111111-2222-3333-4444-555555555555",
            AccessToken = "test-access-token", AccessTokenExpiry = DateTime.UtcNow.AddHours(1),
        };

        private static string Body(HttpListenerRequest r)
        {
            using var sr = new StreamReader(r.InputStream, Encoding.UTF8);
            return sr.ReadToEnd();
        }

        [Fact]
        public async Task Patch_200_SendsPatchToTheIssue_WithBody()
        {
            string method = null, path = null, body = null;
            using var s = new LoopbackServer((i, r) =>
            {
                method = r.HttpMethod; path = r.Url.AbsolutePath; body = Body(r);
                return new CannedResponse(200, "{\"id\":\"a-1\",\"status\":\"closed\"}");
            });
            AccIssueSync.OverrideHostForTests(s.BaseUrl);

            var w = await AccIssueSync.PatchIssueAsync(Creds(), "a-1", new JObject { ["status"] = "closed" });

            Assert.True(w.Ok);
            Assert.Equal("PATCH", method);
            Assert.Equal("/construction/issues/v1/projects/11111111-2222-3333-4444-555555555555/issues/a-1", path);
            Assert.Equal("closed", (string)JObject.Parse(body)["status"]);
            Assert.Equal("closed", (string)w.Response["status"]);
        }

        [Theory]
        [InlineData(403, AccFetchStatus.AuthFailed, "may not make that change")]
        [InlineData(409, AccFetchStatus.TransportFailed, "conflict")]
        public async Task Patch_Refused_ReportedWithReason_NotRetried(int status, AccFetchStatus kind, string phrase)
        {
            using var s = LoopbackServer.Always(status, "{\"detail\":\"no\"}");
            AccIssueSync.OverrideHostForTests(s.BaseUrl);

            var w = await AccIssueSync.PatchIssueAsync(Creds(), "a-1", new JObject { ["status"] = "closed" });

            Assert.False(w.Ok);
            Assert.False(w.Ambiguous);
            Assert.Equal(kind, w.Status);
            Assert.Contains(phrase, w.Detail);
            Assert.Equal(1, s.RequestCount);
        }

        [Fact]
        public async Task Patch_429_IsRetried_ThenSucceeds()
        {
            using var s = new LoopbackServer((i, _) => i < 2
                ? new CannedResponse(429, "{}")
                : new CannedResponse(200, "{\"id\":\"a-1\"}"));
            AccIssueSync.OverrideHostForTests(s.BaseUrl);

            var w = await AccIssueSync.PatchIssueAsync(Creds(), "a-1", new JObject { ["status"] = "closed" });

            Assert.True(w.Ok);
            Assert.Equal(3, s.RequestCount);
        }

        [Fact]
        public async Task Patch_Bare502_NotRetried_ReportedAmbiguous()
        {
            using var s = LoopbackServer.Always(502, "bad gateway");
            AccIssueSync.OverrideHostForTests(s.BaseUrl);

            var w = await AccIssueSync.PatchIssueAsync(Creds(), "a-1", new JObject { ["status"] = "closed" });

            Assert.False(w.Ok);
            Assert.True(w.Ambiguous);
            Assert.Contains("MAY have been applied", w.Detail);
            Assert.Equal(1, s.RequestCount);
        }

        [Fact]
        public async Task Comment_201_PostsBodyToCommentsPath()
        {
            string method = null, path = null, body = null;
            using var s = new LoopbackServer((i, r) =>
            {
                method = r.HttpMethod; path = r.Url.AbsolutePath; body = Body(r);
                return new CannedResponse(201, "{\"id\":\"c-1\"}");
            });
            AccIssueSync.OverrideHostForTests(s.BaseUrl);

            var w = await AccIssueSync.AddCommentAsync(Creds(), "a-1", "From STING (ACC-0001):\nfixed");

            Assert.True(w.Ok);
            Assert.Equal("POST", method);
            Assert.EndsWith("/issues/a-1/comments", path);
            Assert.Equal("From STING (ACC-0001):\nfixed", (string)JObject.Parse(body)["body"]);
        }

        [Fact]
        public async Task Comment_Persistent429_FailsAfterBoundedAttempts()
        {
            using var s = LoopbackServer.Always(429, "{}");
            AccIssueSync.OverrideHostForTests(s.BaseUrl);
            var w = await AccIssueSync.AddCommentAsync(Creds(), "a-1", "x");
            Assert.False(w.Ok);
            Assert.Equal(AccHttp.DefaultMaxAttempts, s.RequestCount);
            Assert.Contains("rate-limited", w.Detail);
        }

        [Fact]
        public async Task GetIssue_ParsesPermissionsAndAttributes()
        {
            using var s = LoopbackServer.Always(200,
                "{\"id\":\"a-1\",\"displayId\":7,\"status\":\"open\",\"assignedTo\":\"u1\",\"assignedToType\":\"user\"," +
                "\"permittedStatuses\":[\"open\",\"closed\"],\"permittedAttributes\":[\"title\",\"assignedTo\"]," +
                "\"customAttributes\":[{\"attributeDefinitionId\":\"d1\",\"value\":\"sig\"}],\"rootCauseId\":\"rc1\"}");
            AccIssueSync.OverrideHostForTests(s.BaseUrl);

            var r = await AccIssueSync.GetIssueAsync(Creds(), "a-1");

            Assert.True(r.Succeeded);
            Assert.Equal(new[] { "open", "closed" }, r.Value.PermittedStatuses);
            Assert.Contains("assignedTo", r.Value.PermittedAttributes);
            Assert.Equal("sig", r.Value.CustomAttributes.Single().ValueText);
            Assert.Equal("rc1", r.Value.RootCauseId);
            Assert.Equal("7", r.Value.DisplayId);
        }

        [Fact]
        public async Task GetIssue_WithoutPermissionFields_LeavesThemNull_NotEmpty()
        {
            using var s = LoopbackServer.Always(200, "{\"id\":\"a-1\",\"status\":\"open\"}");
            AccIssueSync.OverrideHostForTests(s.BaseUrl);
            var r = await AccIssueSync.GetIssueAsync(Creds(), "a-1");
            Assert.True(r.Succeeded);
            Assert.Null(r.Value.PermittedStatuses);
            Assert.Null(r.Value.PermittedAttributes);
        }

        [Fact]
        public async Task GetIssue_404_IsNotFound_NotAnEmptyIssue()
        {
            using var s = LoopbackServer.Always(404, "{}");
            AccIssueSync.OverrideHostForTests(s.BaseUrl);
            var r = await AccIssueSync.GetIssueAsync(Creds(), "a-1");
            Assert.False(r.Succeeded);
            Assert.Equal(AccFetchStatus.NotFound, r.Status);
            Assert.Null(r.Value);
        }

        [Fact]
        public async Task Pull_Incremental_SendsUpdatedAtFilter_FullDoesNot()
        {
            var queries = new List<string>();
            using var s = new LoopbackServer((i, r) =>
            {
                queries.Add(Uri.UnescapeDataString(r.Url.Query));
                return new CannedResponse(200, "{\"results\":[{\"id\":\"a-1\",\"status\":\"open\"}],\"pagination\":{\"totalResults\":1}}");
            });
            AccIssueSync.OverrideHostForTests(s.BaseUrl);

            var since = new DateTime(2026, 9, 1, 6, 30, 0, DateTimeKind.Utc);
            var inc = await AccIssueSync.PullIssuesAsync(Creds(), updatedSince: since);
            var full = await AccIssueSync.PullIssuesAsync(Creds());

            Assert.True(inc.Succeeded);
            Assert.True(full.Succeeded);
            Assert.Contains("filter[updatedAt]=2026-09-01T06:30:00.000Z..", queries[0]);
            Assert.DoesNotContain("updatedAt", queries[1]);
        }

        [Fact]
        public async Task Fields_ResolveAsync_ReadsDefinitionsAndRootCauses()
        {
            using var s = new LoopbackServer((i, r) => r.Url.AbsolutePath.EndsWith("/issue-attribute-definitions")
                ? new CannedResponse(200, "{\"results\":[{\"id\":\"d1\",\"title\":\"STING Signature\",\"dataType\":\"text\"}],\"pagination\":{\"totalResults\":1}}")
                : r.Url.AbsolutePath.EndsWith("/issue-root-cause-categories")
                    ? new CannedResponse(200, "{\"results\":[{\"title\":\"Design\",\"rootCauses\":[{\"id\":\"rc1\",\"title\":\"Coordination\"}]}]}")
                    : new CannedResponse(404, "{}"));
            AccIssueSync.OverrideHostForTests(s.BaseUrl);

            var r = await AccIssueFields.ResolveAsync(Creds(),
                new Dictionary<string, string> { ["clashSignature"] = "STING Signature", ["clashId"] = "Nope" }, "Coordination");

            Assert.Equal("d1", r.DefinitionIdFor("clashSignature"));
            Assert.Equal("rc1", r.RootCauseId);
            Assert.Contains(r.Problems, p => p.Contains("'Nope'"));
        }

        [Fact]
        public async Task Fields_ResolveAsync_ReadFailure_IsAProblem_NotAnException()
        {
            using var s = LoopbackServer.Always(403, "{}");
            AccIssueSync.OverrideHostForTests(s.BaseUrl);
            var r = await AccIssueFields.ResolveAsync(Creds(),
                new Dictionary<string, string> { ["clashSignature"] = "STING Signature" }, null);
            Assert.False(r.Any);
            Assert.Contains(r.Problems, p => p.StartsWith("custom attributes:"));
        }
    }
}
