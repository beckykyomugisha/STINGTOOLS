// The 429 retry in AccIssueSync.PushIssueAsync.
//
// The defect: the HttpRequestMessage was built once, outside the retry loop, and
// re-sent. An HttpRequestMessage is single-use, so on .NET 8 the second send threw
// InvalidOperationException("The request message was already sent"). The call site
// caught it, so a rate-limited issue was DROPPED while the log said
// "ACC 429 - retrying in 1s". Bulk-escalating clashes to ACC Issues is exactly the
// workload that provokes 429s.
//
// These tests count requests SERVER-SIDE. "It didn't throw" would have passed against
// the broken code, because the throw was swallowed one frame up; only the count proves
// the retry actually ran.

using System;
using System.Threading.Tasks;
using StingTools.Acc.Tests.TestHelpers;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccIssueSyncRetryTests : IDisposable
    {
        public AccIssueSyncRetryTests()
        {
            // Neuter the back-off so the suite does not sleep 1+2+4 seconds proving the
            // retry ran. Production timings are untouched.
            AccIssueSync.DelayHook = _ => Task.CompletedTask;
        }

        public void Dispose()
        {
            AccIssueSync.OverrideHostForTests(null);
            AccIssueSync.DelayHook = t => Task.Delay(t);
        }

        /// <summary>Fresh token and a cached issue type, so PushIssueAsync makes exactly one
        /// kind of request (the POST under test) and never touches the real Autodesk token
        /// endpoint or the user's %APPDATA% credential file.</summary>
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

        private static AccIssue SampleIssue() => new AccIssue
        {
            Title = "Clash [structural-vs-services] (STING score 0.91)",
            Description = "Triaged from ACC Model Coordination.",
            Status = "open",
            LocationDescription = "KUT Federated",
        };

        [Fact]
        public async Task Retry_PersistentRateLimit_MakesFourRequests()
        {
            using var server = LoopbackServer.Always(429, "{\"detail\":\"rate limited\"}");
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            // If the request were still built outside the loop this would throw
            // InvalidOperationException out of PushIssueAsync and fail the test.
            var id = await AccIssueSync.PushIssueAsync(FreshCreds(), SampleIssue());

            Assert.Null(id);                              // four 429s -> no issue created
            Assert.Equal(4, server.RequestCount);         // the retry actually ran, 4 times
        }

        [Fact]
        public async Task Retry_RateLimitedTwiceThenCreated_ReturnsTheIssueId()
        {
            using var server = new LoopbackServer((index, _) => index < 2
                ? new CannedResponse(429, "{\"detail\":\"rate limited\"}")
                : new CannedResponse(201, "{\"id\":\"abc\"}"));
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var id = await AccIssueSync.PushIssueAsync(FreshCreds(), SampleIssue());

            Assert.Equal("abc", id);
            Assert.Equal(3, server.RequestCount);
        }

        [Fact]
        public async Task Retry_FirstAttemptSucceeds_MakesExactlyOneRequest()
        {
            // The retry must not fire when it is not needed.
            using var server = LoopbackServer.Always(201, "{\"id\":\"first-try\"}");
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var id = await AccIssueSync.PushIssueAsync(FreshCreds(), SampleIssue());

            Assert.Equal("first-try", id);
            Assert.Equal(1, server.RequestCount);
        }

        [Fact]
        public async Task Retry_NonRetryableError_DoesNotRetry()
        {
            // A 400 is the caller's fault; retrying it four times just delays the report.
            using var server = LoopbackServer.Always(400, "{\"detail\":\"bad request\"}");
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var id = await AccIssueSync.PushIssueAsync(FreshCreds(), SampleIssue());

            Assert.Null(id);
            Assert.Equal(1, server.RequestCount);
        }

        [Fact]
        public async Task Retry_EachAttemptCarriesTheFullBody()
        {
            // A retry that re-sent a consumed StringContent would post an empty body.
            string firstBody = null, lastBody = null;
            int seen = 0;
            using var server = new LoopbackServer((index, req) =>
            {
                using (var sr = new System.IO.StreamReader(req.InputStream, System.Text.Encoding.UTF8))
                {
                    string body = sr.ReadToEnd();
                    if (index == 0) firstBody = body;
                    lastBody = body;
                }
                seen++;
                return index < 2
                    ? new CannedResponse(429, "{\"detail\":\"rate limited\"}")
                    : new CannedResponse(201, "{\"id\":\"body-ok\"}");
            });
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var id = await AccIssueSync.PushIssueAsync(FreshCreds(), SampleIssue());

            Assert.Equal("body-ok", id);
            Assert.Equal(3, seen);
            Assert.Contains("STING score 0.91", firstBody);
            Assert.Contains("STING score 0.91", lastBody);
            Assert.Contains("issue-type-clash", lastBody);
        }
    }
}
