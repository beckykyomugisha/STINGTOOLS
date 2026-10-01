// AUT-2: issue types and root causes were read as ONE page. A type past the first 100 was
// reported "not found" (and the remedy offered was to rename something that already
// existed); a root cause past the first page could never be matched. These run over a real
// loopback listener. They use ResolveNamedIssueTypeAsync and GetRootCausesAsync, which never
// write the machine credentials file (ResolveIssueTypeAsync does, so it is not driven here).

using System;
using System.Linq;
using System.Threading.Tasks;
using StingTools.Acc.Tests.TestHelpers;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccIssueListPagingTests : IDisposable
    {
        public AccIssueListPagingTests() => AccIssueSync.DelayHook = _ => Task.CompletedTask;

        public void Dispose()
        {
            AccIssueSync.OverrideHostForTests(null);
            AccIssueSync.DelayHook = t => Task.Delay(t);
        }

        private static AccCredentials FreshCreds() => new AccCredentials
        {
            ClientId = "test-client",
            RefreshToken = "test-refresh",
            ProjectId = "b.11111111-2222-3333-4444-555555555555",
            AccessToken = "test-access-token",
            AccessTokenExpiry = DateTime.UtcNow.AddHours(1),
        };

        private static string TypesPage(int from, int count, int total, string extraTitle = null)
        {
            var items = Enumerable.Range(from, count).Select(i =>
                $"{{\"id\":\"t{i}\",\"title\":\"Type {i}\",\"isActive\":true,\"subtypes\":[{{\"id\":\"s{i}\",\"title\":\"Sub {i}\",\"isActive\":true}}]}}").ToList();
            if (extraTitle != null)
                items.Add($"{{\"id\":\"tx\",\"title\":\"{extraTitle}\",\"isActive\":true,\"subtypes\":[{{\"id\":\"sx\",\"title\":\"{extraTitle}\",\"isActive\":true}}]}}");
            return "{\"pagination\":{\"limit\":100,\"offset\":" + from + ",\"totalResults\":" + total + "},\"results\":[" + string.Join(",", items) + "]}";
        }

        [Fact]
        public async Task ATypeOnPageTwo_IsFound()
        {
            using var server = new LoopbackServer((i, req) => i == 0
                ? new CannedResponse(200, TypesPage(0, 100, 101))
                : new CannedResponse(200, TypesPage(100, 0, 101, extraTitle: "Lifecycle Gap")));
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var r = await AccIssueSync.ResolveNamedIssueTypeAsync(FreshCreds(), "", "", "Lifecycle Gap", "hint");

            Assert.True(r.Succeeded, r.Detail);
            Assert.Equal("sx", r.Value);
            Assert.Equal(2, server.RequestCount);
            Assert.Contains(server.Paths, p => p.Contains("offset=100"));
        }

        [Fact]
        public async Task APageFailure_IsTheFailure_NotNotFound()
        {
            using var server = new LoopbackServer((i, req) => i == 0
                ? new CannedResponse(200, TypesPage(0, 100, 150))
                : new CannedResponse(500, "{\"detail\":\"boom\"}"));
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var r = await AccIssueSync.ResolveNamedIssueTypeAsync(FreshCreds(), "", "", "Lifecycle Gap", "hint");

            Assert.False(r.Succeeded);
            Assert.NotEqual(AccFetchStatus.NotFound, r.Status);
            Assert.Contains("page 2", r.Detail);
        }

        [Fact]
        public async Task AListThatNeverEnds_IsIncomplete()
        {
            using var server = new LoopbackServer((i, req) => new CannedResponse(200, TypesPage(i * 100, 100, 1_000_000)));
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var r = await AccIssueSync.ReadAllResultsAsync(FreshCreds(), server.BaseUrl + "/x?include=subtypes", 100, "ACC issue types");

            Assert.False(r.Succeeded);
            Assert.True(r.Truncated);
            Assert.Contains("INCOMPLETE", r.Detail);
            Assert.Equal(AccIssueSync.ResultsMaxPages, server.RequestCount);
        }

        [Fact]
        public async Task RootCauses_AreReadAcrossPages()
        {
            string Cat(int i) => $"{{\"id\":\"c{i}\",\"title\":\"Cat {i}\",\"rootCauses\":[{{\"id\":\"rc{i}\",\"title\":\"Cause {i}\"}}]}}";
            using var server = new LoopbackServer((i, req) => i == 0
                ? new CannedResponse(200, "{\"pagination\":{\"totalResults\":201},\"results\":[" +
                                          string.Join(",", Enumerable.Range(0, 200).Select(Cat)) + "]}")
                : new CannedResponse(200, "{\"pagination\":{\"totalResults\":201},\"results\":[" + Cat(200) + "]}"));
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var r = await AccIssueSync.GetRootCausesAsync(FreshCreds());

            Assert.True(r.Succeeded, r.Detail);
            Assert.Equal(201, r.Value.Count);
            Assert.Contains(r.Value, c => c.Id == "rc200");
        }

        [Fact]
        public async Task OneShortPage_WithNoPaginationBlock_IsTheWholeList()
        {
            using var server = new LoopbackServer((i, req) => new CannedResponse(200,
                "{\"results\":[{\"id\":\"t1\",\"title\":\"Coordination\",\"subtypes\":[{\"id\":\"s1\",\"title\":\"Coordination\"}]}]}"));
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var r = await AccIssueSync.ResolveNamedIssueTypeAsync(FreshCreds(), "", "", "Coordination", "hint");

            Assert.True(r.Succeeded, r.Detail);
            Assert.Equal(1, server.RequestCount);
        }
    }
}
