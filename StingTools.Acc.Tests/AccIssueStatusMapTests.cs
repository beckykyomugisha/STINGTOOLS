// INT-12 — the STING <-> ACC Issues v1 status mapping.
//
// The defects: AccIssueSync.IsClosedStatus substring-matched "closed" / "resolved" /
// "not_an_issue" / "void" (three are not Issues v1 values) and treated every other string as
// open; a pulled issue with no status was recorded as "open"; and PushIssueAsync posted the
// STING status verbatim, so IN_PROGRESS or RESOLVED went to ACC as values outside its enum.
//
// Vocabulary: components.schemas.status in
// https://github.com/autodesk-platform-services/aps-sdk-openapi/blob/main/construction/issues/Issues.yaml

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Planscape.Shared.Helpers;
using StingTools.Acc.Tests.TestHelpers;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccIssueStatusMapTests : IDisposable
    {
        public AccIssueStatusMapTests() => AccIssueSync.DelayHook = _ => Task.CompletedTask;

        public void Dispose()
        {
            AccIssueSync.OverrideHostForTests(null);
            AccIssueSync.DelayHook = t => Task.Delay(t);
        }

        private static readonly string[] IssuesV1 =
            { "draft", "open", "pending", "in_progress", "in_review", "completed", "not_approved", "in_dispute", "closed" };

        private static AccCredentials FreshCreds() => new AccCredentials
        {
            ClientId = "test-client",
            ClientSecret = "test-secret",
            RefreshToken = "test-refresh",
            ProjectId = "b.11111111-2222-3333-4444-555555555555",
            AccessToken = "test-access-token",
            AccessTokenExpiry = DateTime.UtcNow.AddHours(1),
            IssueTypeId = "issue-type-clash",
        };

        // ── the vocabulary ──────────────────────────────────────────────────

        [Fact]
        public void TheMapCarriesExactlyTheIssuesV1Enum()
        {
            Assert.Equal(IssuesV1, AccIssueStatusMap.AccStatuses.ToArray());
        }

        [Fact]
        public void EveryIssuesV1StatusIsMappedOnPurpose()
        {
            foreach (var s in IssuesV1)
                Assert.NotEqual(AccIssueState.Unknown, AccIssueStatusMap.FromAcc(s));
            Assert.Equal(AccIssueState.Responded, AccIssueStatusMap.FromAcc("completed"));
            Assert.Equal(AccIssueState.Open, AccIssueStatusMap.FromAcc("not_approved"));
            Assert.Equal(AccIssueState.InProgress, AccIssueStatusMap.FromAcc("IN_REVIEW"));
        }

        [Fact]
        public void OnlyClosedIsClosed()
        {
            foreach (var s in IssuesV1)
                Assert.Equal(s == "closed", AccIssueSync.IsClosedStatus(s));
        }

        [Theory]
        [InlineData("void")]
        [InlineData("resolved")]
        [InlineData("not_an_issue")]
        [InlineData("answered")]
        [InlineData("work_completed")]
        [InlineData("ready_to_inspect")]
        [InlineData("closed_pending")]
        [InlineData("")]
        [InlineData(null)]
        public void AStatusOutsideIssuesV1IsUnknown_NeitherClosedNorOpen(string status)
        {
            Assert.False(AccIssueSync.IsClosedStatus(status));
            Assert.Equal(AccIssueState.Unknown, AccIssueStatusMap.FromAcc(status));
            Assert.Equal(AccReconcileAction.KeepUnknown, AccIssueStatusMap.ReconcileAction(status));
        }

        [Fact]
        public void ReconcileUntracksOnlyClosed_AndKeepsTheRest()
        {
            foreach (var s in IssuesV1)
                Assert.Equal(s == "closed" ? AccReconcileAction.Untrack : AccReconcileAction.Keep,
                             AccIssueStatusMap.ReconcileAction(s));
        }

        // ── the push side ───────────────────────────────────────────────────

        [Theory]
        [InlineData("OPEN")]
        [InlineData("open")]
        [InlineData("IN_PROGRESS")]
        [InlineData("RESPONDED")]
        public async Task AnOpenStingIssueIsCreatedOpen(string stingStatus)
        {
            string body = null;
            using var server = new LoopbackServer((_, req) =>
            {
                using (var sr = new StreamReader(req.InputStream, Encoding.UTF8)) body = sr.ReadToEnd();
                return new CannedResponse(201, "{\"id\":\"acc-1\"}");
            });
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var id = await AccIssueSync.PushIssueAsync(FreshCreds(), new AccIssue { Title = "t", Status = stingStatus });

            Assert.Equal("acc-1", id);
            string sent = (string)JObject.Parse(body)["status"];
            Assert.Equal("open", sent);
            Assert.Contains(sent, IssuesV1);
        }

        [Theory]
        [InlineData("RESOLVED")]
        [InlineData("CLOSED")]
        [InlineData("VOID")]
        [InlineData("ACCEPTED")]
        [InlineData("something-new")]
        public async Task AnIssueThatIsNotOpenIsNotPushed(string stingStatus)
        {
            using var server = LoopbackServer.Always(201, "{\"id\":\"should-not-exist\"}");
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var id = await AccIssueSync.PushIssueAsync(FreshCreds(), new AccIssue { Title = "t", Status = stingStatus });

            Assert.Null(id);
            Assert.Equal(0, server.RequestCount);
        }

        [Theory]
        [InlineData("OPEN")]
        [InlineData("IN_PROGRESS")]
        [InlineData("RESOLVED")]
        [InlineData("CLOSED")]
        [InlineData("VOID")]
        [InlineData("RESPONDED")]
        [InlineData("ACCEPTED")]
        [InlineData("")]
        public void ACreateStatusIsAlwaysAnIssuesV1ValueOrNothing(string stingStatus)
        {
            string s = AccIssueStatusMap.ToAccCreateStatus(stingStatus);
            Assert.True(s == null || IssuesV1.Contains(s), $"'{stingStatus}' -> '{s}' is not an Issues v1 status");
        }

        // ── the pull side ───────────────────────────────────────────────────

        [Fact]
        public async Task APulledIssueWithNoStatusIsNotRecordedAsOpen()
        {
            using var server = LoopbackServer.Always(200,
                "{\"results\":[{\"id\":\"i-1\",\"title\":\"no status\"},{\"id\":\"i-2\",\"title\":\"x\",\"status\":\"closed\"}]}");
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var result = await AccIssueSync.PullIssuesAsync(FreshCreds(), pageSize: 10);

            Assert.True(result.Succeeded);
            var noStatus = result.Value.Single(i => i.Id == "i-1");
            Assert.NotEqual("open", noStatus.Status);
            Assert.Equal(AccIssueState.Unknown, AccIssueStatusMap.FromAcc(noStatus.Status));
            Assert.True(AccIssueSync.IsClosedStatus(result.Value.Single(i => i.Id == "i-2").Status));
        }
    }
}
