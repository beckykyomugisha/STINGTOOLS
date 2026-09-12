// AccIssueSync.PullIssuesAsync, driven over a real loopback listener.
//
// The defect: three exits — auth failure, a mid-pagination HTTP error, and success — all
// returned a bare List<AccIssue> the caller could not tell apart.
// AccSyncIssueStatusCommand reconciled the escalation sidecar against that list and
// reported every tracked clash whose issue was absent as NOT_FOUND / keep. So an expired
// token made every escalated clash look deleted from ACC, and a page-2 failure produced a
// PARTIAL reconciliation presented as a complete one — after which the sidecar was
// written, permanently un-tracking whatever happened to be on page 1.
//
// Every assertion below asserts the STATUS, not just emptiness. Emptiness is the bug:
// an empty list is what a successful read of an empty container also returns.

using System;
using System.Linq;
using System.Threading.Tasks;
using StingTools.Acc.Tests.TestHelpers;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccIssuePullLoopbackTests : IDisposable
    {
        private const string Container = "b.11111111-2222-3333-4444-555555555555";

        public AccIssuePullLoopbackTests() => AccIssueSync.DelayHook = _ => Task.CompletedTask;

        public void Dispose()
        {
            AccIssueSync.OverrideHostForTests(null);
            AccIssueSync.DelayHook = t => Task.Delay(t);
        }

        /// <summary>A fresh token, so EnsureAuthAsync short-circuits and no test reaches the
        /// real Autodesk token endpoint or writes %APPDATA%\Planscape\acc_credentials.json.</summary>
        private static AccCredentials FreshCreds() => new AccCredentials
        {
            ClientId = "test-client",
            ClientSecret = "test-secret",
            RefreshToken = "test-refresh",
            ProjectId = Container,
            AccessToken = "test-access-token",
            AccessTokenExpiry = DateTime.UtcNow.AddHours(1),
            IssueTypeId = "issue-type-clash",
        };

        /// <summary>Credentials with no usable token, so EnsureAuthAsync must attempt a
        /// refresh — which the listener rejects.</summary>
        private static AccCredentials StaleCreds() => new AccCredentials
        {
            ClientId = "test-client",
            ClientSecret = "test-secret",
            RefreshToken = "expired-refresh",
            ProjectId = Container,
            AccessToken = "",
            AccessTokenExpiry = DateTime.UtcNow.AddHours(-1),
        };

        private static string Page(int count, string idPrefix) =>
            "{\"results\":[" + string.Join(",", Enumerable.Range(0, count)
                .Select(i => $"{{\"id\":\"{idPrefix}{i}\",\"title\":\"Clash {idPrefix}{i}\",\"status\":\"open\"}}"))
            + "]}";

        // ── The auth case: "every escalated clash looks deleted" ────────────────

        [Fact]
        public async Task AuthFailure_IsAuthFailed_NotAnEmptySuccess()
        {
            // The token endpoint rejects the refresh, so no issue is read at all.
            using var server = LoopbackServer.Always(401, "{\"error\":\"invalid_grant\"}");
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var result = await AccIssueSync.PullIssuesAsync(StaleCreds());

            Assert.Equal(AccFetchStatus.AuthFailed, result.Status);
            Assert.False(result.Succeeded);
            Assert.NotEqual(AccFetchStatus.EmptyOk, result.Status);   // this is the bug shape
            Assert.Empty(result.Value);
            Assert.False(string.IsNullOrWhiteSpace(result.Detail));
            // And the command must refuse rather than reconcile against nothing.
            Assert.Equal(AccCommandVerdict.Failed, AccCommandOutcome.Verdict(result.Status));
        }

        [Fact]
        public async Task Http401OnTheIssueList_IsAuthFailed()
        {
            using var server = LoopbackServer.Always(401, "{\"developerMessage\":\"token invalid\"}");
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var result = await AccIssueSync.PullIssuesAsync(FreshCreds(), pageSize: 2);

            Assert.Equal(AccFetchStatus.AuthFailed, result.Status);
            Assert.False(result.Succeeded);
            Assert.Empty(result.Value);
        }

        // ── The partial case: a subset presented as the whole ──────────────────

        [Fact]
        public async Task FullPageThenServerError_IsAFailure_AndTheDetailNamesThePageCount()
        {
            using var server = new LoopbackServer((index, _) => index == 0
                ? new CannedResponse(200, Page(2, "p1-"))     // a FULL page -> pagination continues
                : new CannedResponse(500, "{\"error\":\"boom\"}"));
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var result = await AccIssueSync.PullIssuesAsync(FreshCreds(), pageSize: 2);

            Assert.False(result.Succeeded);
            Assert.Equal(AccFetchStatus.TransportFailed, result.Status);
            Assert.NotEqual(AccFetchStatus.Ok, result.Status);
            Assert.NotEqual(AccFetchStatus.EmptyOk, result.Status);

            // Attribution, not just the verdict: the detail must say how far it got, so a
            // partial set cannot be mistaken for the whole container.
            Assert.Contains("page 2", result.Detail, StringComparison.Ordinal);
            Assert.Contains("1 page(s) succeeded", result.Detail, StringComparison.Ordinal);
            Assert.Contains("INCOMPLETE", result.Detail, StringComparison.Ordinal);

            // The rows read so far are carried for diagnostics — and they are a SUBSET.
            Assert.Equal(2, result.Value.Count);
            Assert.Equal(2, server.RequestCount);

            // The consumer must refuse: this is what stops the sidecar being written.
            Assert.Equal(AccCommandVerdict.Failed, AccCommandOutcome.Verdict(result.Status));
            string msg = AccCommandOutcome.FailureMessage("the ACC issue list", result.Status,
                result.HttpStatus, result.Detail, Container);
            Assert.DoesNotContain("not found", msg, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task FullPageThenTheConnectionBreaks_IsAFailure_NotAPartialSuccess()
        {
            // Page 1 succeeds, then page 2's connection is dropped without a response.
            //
            // What this asserts, and what it deliberately does NOT. The load-bearing claim
            // is that a half-read list is never handed back as a success: the caller is
            // reconciling issue status, so a silently short list reads as "these issues no
            // longer exist in ACC". That holds on every platform.
            //
            // It does NOT assert HttpStatus. How far a broken exchange gets before it dies
            // is environment-specific, and pinning it made this test lie: on Windows
            // nothing reached the client (status 0), while on Linux the status line arrived
            // and only the body was lost (status 200, attributed to the payload). Two
            // earlier attempts to force one answer -- disposing the listener inside its own
            // handler, then HttpListenerResponse.Abort() -- both passed locally and failed
            // in CI, because the difference is in the platform's listener, not in us.
            // TransportDown_CarriesNoHttpStatus below covers the attribution claim with a
            // scenario that IS deterministic: a port where nothing is listening at all.
            using var server = new LoopbackServer((index, _) =>
                index == 0 ? new CannedResponse(200, Page(2, "p1-")) : CannedResponse.Abort);
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var result = await AccIssueSync.PullIssuesAsync(FreshCreds(), pageSize: 2);

            Assert.False(result.Succeeded);
            Assert.Equal(AccFetchStatus.TransportFailed, result.Status);
            Assert.NotEmpty(result.Detail);
            // Page 1's two issues were read and are NOT presented as the whole list.
            Assert.True(result.Value.Count <= 2,
                $"a partial list of {result.Value.Count} was returned as though complete");
        }

        [Fact]
        public async Task TransportDown_CarriesNoHttpStatus()
        {
            // Nothing is listening on port 1, on any platform, so the request cannot start.
            // HttpStatus 0 then means exactly one thing -- no response ever arrived -- which
            // is the attribution a downstream payload check could not have inferred. That is
            // the lesson #927 paid for, asserted where it can actually be guaranteed.
            AccIssueSync.OverrideHostForTests("http://127.0.0.1:1");

            var result = await AccIssueSync.PullIssuesAsync(FreshCreds(), pageSize: 2);

            Assert.False(result.Succeeded);
            Assert.Equal(AccFetchStatus.TransportFailed, result.Status);
            Assert.Equal(0, result.HttpStatus);
            Assert.Empty(result.Value);
        }

        [Fact]
        public async Task Http200_WithNoResultsArray_IsTransportFailed_NotAnEmptyContainer()
        {
            // What a changed construction/issues/v1 payload looks like.
            using var server = LoopbackServer.Always(200, "{\"data\":{\"items\":[]}}");
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var result = await AccIssueSync.PullIssuesAsync(FreshCreds(), pageSize: 2);

            Assert.Equal(AccFetchStatus.TransportFailed, result.Status);
            Assert.NotEqual(AccFetchStatus.EmptyOk, result.Status);
            Assert.False(result.Succeeded);
        }

        [Fact]
        public async Task PageCapReachedWithoutAShortPage_IsAFailure()
        {
            // Every page full means there is more than we read. Returning that as Ok would
            // be a truncated set presented as complete.
            using var server = LoopbackServer.Always(200, Page(2, "x-"));
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var result = await AccIssueSync.PullIssuesAsync(FreshCreds(), pageSize: 2, maxPages: 3);

            Assert.False(result.Succeeded);
            Assert.Contains("INCOMPLETE", result.Detail, StringComparison.Ordinal);
            Assert.Equal(6, result.Value.Count);
            Assert.Equal(3, server.RequestCount);
        }

        // ── The success cases, so "everything fails" would not pass either ─────

        [Fact]
        public async Task TwoFullPagesThenAShortPage_IsOk_WithTheSummedCount()
        {
            using var server = new LoopbackServer((index, _) => index switch
            {
                0 => new CannedResponse(200, Page(2, "a-")),
                1 => new CannedResponse(200, Page(2, "b-")),
                _ => new CannedResponse(200, Page(1, "c-")),      // short page ends pagination
            });
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var result = await AccIssueSync.PullIssuesAsync(FreshCreds(), pageSize: 2);

            Assert.Equal(AccFetchStatus.Ok, result.Status);
            Assert.True(result.Succeeded);
            Assert.Equal(5, result.Value.Count);                  // assert on non-empty data
            Assert.Equal(3, server.RequestCount);
            Assert.Equal("a-0", result.Value[0].Id);
            Assert.Equal("c-0", result.Value[4].Id);
            Assert.Equal(AccCommandVerdict.Proceed, AccCommandOutcome.Verdict(result.Status));
        }

        [Fact]
        public async Task AContainerWithNoIssues_IsEmptyOk_AndStillSucceeds()
        {
            // The legitimate empty case. Over-correcting this into an error would make a
            // genuinely clean container unreportable, which is worse than the original bug.
            using var server = LoopbackServer.Always(200, "{\"results\":[]}");
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var result = await AccIssueSync.PullIssuesAsync(FreshCreds(), pageSize: 2);

            Assert.Equal(AccFetchStatus.EmptyOk, result.Status);
            Assert.True(result.Succeeded);
            Assert.Empty(result.Value);
            Assert.Equal(AccCommandVerdict.SucceededEmpty, AccCommandOutcome.Verdict(result.Status));
        }

        [Fact]
        public async Task RateLimitedOnEveryAttempt_IsAFailure_NotAShortRead()
        {
            using var server = LoopbackServer.Always(429, "{\"detail\":\"rate limited\"}");
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var result = await AccIssueSync.PullIssuesAsync(FreshCreds(), pageSize: 2);

            Assert.False(result.Succeeded);
            Assert.Equal(4, server.RequestCount);                 // the per-page retry ran
            Assert.Contains("rate-limited", result.Detail, StringComparison.OrdinalIgnoreCase);
        }
    }
}
