// KUT deep review INT-2 / INT-11 / INT-13 / INT-14 — escalation and clash-read integrity.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using StingTools.Acc.Tests.TestHelpers;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccEscalationIntegrityTests : IDisposable
    {
        private const string Container = "b.11111111-2222-3333-4444-555555555555";
        private const string ModelSet = "ms-kut-federated";
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "sting-ledger-" + Guid.NewGuid().ToString("N"));

        public AccEscalationIntegrityTests()
        {
            Directory.CreateDirectory(_dir);
            AccModelCoordSync.DelayHook = _ => Task.CompletedTask;
        }

        public void Dispose()
        {
            AccModelCoordSync.OverrideHostForTests(null);
            AccModelCoordSync.DelayHook = t => Task.Delay(t);
            try { Directory.Delete(_dir, true); } catch { }
        }

        // ── INT-14: the ledger ────────────────────────────────────────────────

        [Fact]
        public void AMissingLedgerIsEmpty()
        {
            var map = AccEscalationLedger.Load(Path.Combine(_dir, "none.json"), out string err);
            Assert.NotNull(map);
            Assert.Empty(map);
            Assert.Null(err);
        }

        [Fact]
        public void AnUnreadableLedgerIsAnErrorNotAnEmptyLedger()
        {
            // Old behaviour: parse failure -> empty map -> every clash escalated again.
            string p = Path.Combine(_dir, "pushed_clashes.json");
            File.WriteAllText(p, "{ \"11@A|22@B\": \"ISSUE-1\", ");   // truncated mid-write
            var map = AccEscalationLedger.Load(p, out string err);
            Assert.Null(map);
            Assert.Contains("duplicate ACC issue", err);
        }

        [Fact]
        public void TheLedgerRoundTripsAndOverwritesAtomically()
        {
            string p = Path.Combine(_dir, "pushed_clashes.json");
            Assert.True(AccEscalationLedger.Save(p, new System.Collections.Generic.Dictionary<string, string> { ["a"] = "1" }, out _));
            Assert.True(AccEscalationLedger.Save(p, new System.Collections.Generic.Dictionary<string, string> { ["a"] = "1", ["b"] = "2" }, out _));
            var map = AccEscalationLedger.Load(p, out string err);
            Assert.Null(err);
            Assert.Equal(2, map.Count);
            Assert.False(File.Exists(p + ".tmp"));
        }

        // ── INT-11: signatures ───────────────────────────────────────────────

        [Fact]
        public void ClashesWithNoInstanceRowDoNotShareOneSignature()
        {
            // Old: both were "0@|0@", so only the first was ever escalated.
            var a = new AccClashRecord { Id = "c1" };
            var b = new AccClashRecord { Id = "c2" };
            Assert.NotEqual(a.StableSignature(), b.StableSignature());
        }

        [Fact]
        public void ClashesWithObjectsKeepTheExistingSignature()
        {
            // An existing ledger must still match: the object-based key is unchanged and
            // order-invariant.
            var ab = new AccClashRecord { Id = "c1", LeftObjectId = 11, LeftDocument = "A.rvt", RightObjectId = 22, RightDocument = "B.rvt" };
            var ba = new AccClashRecord { Id = "c9", LeftObjectId = 22, LeftDocument = "B.rvt", RightObjectId = 11, RightDocument = "A.rvt" };
            Assert.Equal("11@A.rvt|22@B.rvt", ab.StableSignature());
            Assert.Equal(ab.StableSignature(), ba.StableSignature());
        }

        // ── INT-13 / INT-2: the clash read ───────────────────────────────────

        private static AccCredentials FreshCreds() => new AccCredentials
        {
            ClientId = "test-client", ClientSecret = "test-secret", RefreshToken = "test-refresh",
            ProjectId = Container, AccessToken = "test-access-token",
            AccessTokenExpiry = DateTime.UtcNow.AddHours(1), IssueTypeId = "test-issue-type",
        };

        [Fact]
        public async Task NoCompletedClashTestIsNotAResult()
        {
            // Old: fell back to tests.First() and could report "no clashes" as a success.
            using var server = LoopbackServer.Always(200, "{\"tests\":[{\"clashTestId\":\"T1\",\"status\":\"running\"}]}");
            AccModelCoordSync.OverrideHostForTests(server.BaseUrl);
            var result = await AccModelCoordSync.GetClashesAsync(FreshCreds(), Container, ModelSet);
            Assert.False(result.Succeeded);
            Assert.Contains("not ready", result.Detail);
        }

        [Fact]
        public async Task ACappedReadIsAFailureNotASmallerSuccess()
        {
            LoopbackServer server = null;
            server = new LoopbackServer((_, req) =>
            {
                string path = req.Url.AbsolutePath, b = server.BaseUrl;
                if (path.EndsWith("/tests"))
                    return new CannedResponse(200, "{\"tests\":[{\"clashTestId\":\"T1\",\"status\":\"completed\"}]}");
                if (path.EndsWith("/resources"))
                    return new CannedResponse(200, "{\"resources\":[{\"type\":\"clash\",\"url\":\"" + b + "/scope/clash\"}," +
                                                   "{\"type\":\"clash-instance\",\"url\":\"" + b + "/scope/inst\"}]}");
                if (path == "/scope/clash")
                    return new CannedResponse(200, "{\"clashes\":[{\"id\":\"c1\"},{\"id\":\"c2\"},{\"id\":\"c3\"}]}");
                if (path == "/scope/inst")
                    return new CannedResponse(200, "{\"instances\":[]}");
                return new CannedResponse(404, "{}");
            });
            using (server)
            {
                AccModelCoordSync.OverrideHostForTests(server.BaseUrl);
                var capped = await AccModelCoordSync.GetClashesAsync(FreshCreds(), Container, ModelSet, max: 2);
                Assert.False(capped.Succeeded);
                Assert.Contains("capped", capped.Detail);

                // The default reads everything — and the three instance-less clashes keep three keys.
                var all = await AccModelCoordSync.GetClashesAsync(FreshCreds(), Container, ModelSet);
                Assert.True(all.Succeeded);
                Assert.Equal(3, all.Value.Count);
                Assert.Equal(3, all.Value.Select(c => c.StableSignature()).Distinct().Count());
            }
        }
    }
}
