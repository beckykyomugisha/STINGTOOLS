// Tests for the 2026-09-30 ACC hardening pass: the shared transport's retry rules, the
// credential store (DPAPI, cross-process adoption), id forms per API family, the Issues v1
// shape, issue-type choice by name, discipline from model names, the new project settings,
// clash truncation / not-ready, the batched multi-part upload, CDE-folder refusal, PKCE and
// paged discovery. Each fact asserted here was a defect or a guess before the pass.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Acc.Tests.TestHelpers;
using StingTools.Core;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    internal static class H
    {
        public const string Project = "b.11111111-2222-3333-4444-555555555555";
        public const string Bare = "11111111-2222-3333-4444-555555555555";

        public static AccCredentials Creds() => new AccCredentials
        {
            ClientId = "test-client",
            ClientSecret = "test-secret",
            RefreshToken = "test-refresh",
            ProjectId = Project,
            AccessToken = "test-access-token",
            AccessTokenExpiry = DateTime.UtcNow.AddHours(1),
            IssueTypeId = "type-1",
            IssueSubtypeId = "sub-1",
            HubId = "b.hub",
        };

        public static CannedResponse With(this CannedResponse r, string header, string value)
        {
            r.Headers[header] = value;
            return r;
        }
    }

    public class AccHttpRetryRuleTests : IDisposable
    {
        private readonly List<TimeSpan> _waits = new List<TimeSpan>();

        public AccHttpRetryRuleTests()
        {
            AccHttp.DelayHook = t => { _waits.Add(t); return Task.CompletedTask; };
            CredentialIsolation.Reset();
        }

        public void Dispose()
        {
            AccHttp.DelayHook = t => Task.Delay(t);
            AccIssueSync.OverrideHostForTests(null);
        }

        [Fact]
        public async Task RetryAfter_IsHonoured_NotTheBackoff()
        {
            using var server = new LoopbackServer((i, _) => i == 0
                ? new CannedResponse(429, "{}").With("Retry-After", "7")
                : new CannedResponse(200, "{}"));
            var r = await AccHttp.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, server.BaseUrl + "/x"), null, idempotent: true);
            Assert.Equal(200, r.Status);
            Assert.Equal(2, server.RequestCount);
            Assert.Equal(TimeSpan.FromSeconds(7), _waits.Single());
        }

        [Fact]
        public async Task RetryAfter_LongerThanTheCap_IsNotWaited_AndIsReported()
        {
            using var s2 = new LoopbackServer((_, __) => new CannedResponse(429, "{}").With("Retry-After", "3600"));
            var r = await AccHttp.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, s2.BaseUrl + "/x"), null, idempotent: true);
            Assert.Equal(429, r.Status);
            Assert.Equal(1, s2.RequestCount);
            Assert.Empty(_waits);
        }

        [Fact]
        public async Task Post_BareGatewayError_IsNotRetried_ItMayHaveCreatedTheIssue()
        {
            using var server = LoopbackServer.Always(502, "{}");
            var r = await AccHttp.SendAsync(() => new HttpRequestMessage(HttpMethod.Post, server.BaseUrl + "/x"), null, idempotent: false);
            Assert.Equal(502, r.Status);
            Assert.Equal(1, server.RequestCount);
        }

        [Fact]
        public async Task Get_GatewayError_IsRetried()
        {
            using var server = new LoopbackServer((i, _) => i < 2 ? new CannedResponse(503, "{}") : new CannedResponse(200, "{}"));
            var r = await AccHttp.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, server.BaseUrl + "/x"), null, idempotent: true);
            Assert.Equal(200, r.Status);
            Assert.Equal(3, server.RequestCount);
        }

        [Fact]
        public async Task Post_503WithRetryAfter_IsRetried_TheServerSaidNotProcessed()
        {
            using var server = new LoopbackServer((i, _) => i == 0
                ? new CannedResponse(503, "{}").With("Retry-After", "1")
                : new CannedResponse(201, "{}"));
            var r = await AccHttp.SendAsync(() => new HttpRequestMessage(HttpMethod.Post, server.BaseUrl + "/x"), null, idempotent: false);
            Assert.Equal(201, r.Status);
            Assert.Equal(2, server.RequestCount);
        }

        [Fact]
        public async Task A401_RefreshesOnce_ThenRetries()
        {
            int apiCalls = 0, tokenCalls = 0;
            using var server = new LoopbackServer((i, req) =>
            {
                if (req.Url.AbsolutePath.Contains("/authentication/v2/token"))
                {
                    tokenCalls++;
                    return new CannedResponse(200, "{\"access_token\":\"new-access\",\"refresh_token\":\"new-refresh\",\"expires_in\":3600}");
                }
                apiCalls++;
                return req.Headers["Authorization"] == "Bearer new-access"
                    ? new CannedResponse(200, "{}")
                    : new CannedResponse(401, "{}");
            });
            AccIssueSync.OverrideHostForTests(server.BaseUrl);
            var creds = H.Creds();
            var r = await AccHttp.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, server.BaseUrl + "/api"), creds, idempotent: true);
            Assert.Equal(200, r.Status);
            Assert.Equal(1, tokenCalls);
            Assert.Equal(2, apiCalls);
            Assert.Equal("new-refresh", creds.RefreshToken);
            Assert.NotEqual(default, creds.RefreshTokenIssuedAt);
        }

        [Fact]
        public async Task RejectedRefresh_IsAuthFailed_UnreachableSignInIsTransport()
        {
            using var rejecting = LoopbackServer.Always(400, "{\"error\":\"invalid_grant\"}");
            AccIssueSync.OverrideHostForTests(rejecting.BaseUrl);
            var stale = H.Creds();
            stale.AccessTokenExpiry = DateTime.UtcNow.AddHours(-1);
            var a = await AccIssueSync.EnsureAuthDetailedAsync(stale);
            Assert.False(a.Ok);
            Assert.Equal(AccFetchStatus.AuthFailed, a.Status);
            Assert.Contains("sign in again", a.Detail, StringComparison.OrdinalIgnoreCase);

            // Nothing listening: the sign-in service could not be reached. That is NOT a
            // rejected refresh token, and must not tell the user to sign in again.
            AccIssueSync.OverrideHostForTests("http://localhost:1");
            var stale2 = H.Creds();
            stale2.AccessTokenExpiry = DateTime.UtcNow.AddHours(-1);
            AccHttp.DelayHook = _ => Task.CompletedTask;
            var b = await AccIssueSync.EnsureAuthDetailedAsync(stale2);
            Assert.False(b.Ok);
            Assert.Equal(AccFetchStatus.TransportFailed, b.Status);
        }

        [Fact]
        public async Task PublicClient_RefreshSendsClientIdInBody_AndNoBasicHeader()
        {
            string auth = "?", body = "";
            using var server = new LoopbackServer((i, req) =>
            {
                auth = req.Headers["Authorization"];
                using (var sr = new StreamReader(req.InputStream)) body = sr.ReadToEnd();
                return new CannedResponse(200, "{\"access_token\":\"a\",\"refresh_token\":\"r2\",\"expires_in\":3600}");
            });
            AccIssueSync.OverrideHostForTests(server.BaseUrl);
            var c = H.Creds();
            c.ClientSecret = "";
            c.AccessTokenExpiry = DateTime.UtcNow.AddHours(-1);
            var o = await AccIssueSync.EnsureAuthDetailedAsync(c);
            Assert.True(o.Ok, o.Detail);
            Assert.Null(auth);
            Assert.Contains("client_id=test-client", body);
        }
    }

    public class AccCredentialStoreTests : IDisposable
    {
        public AccCredentialStoreTests() => CredentialIsolation.Reset();

        public void Dispose()
        {
            try { File.Delete(AccCredentialStore.CredentialsPath); } catch (Exception) { }
            AccIssueSync.OverrideHostForTests(null);
        }

        [Fact]
        public void Secrets_AreNotWrittenInPlaintext_OnWindows_AndReadBack()
        {
            var c = H.Creds();
            Assert.True(AccIssueSync.SaveCredentials(c, out string err), err);
            string raw = File.ReadAllText(AccCredentialStore.CredentialsPath);
            if (OperatingSystem.IsWindows())
            {
                Assert.DoesNotContain("test-secret", raw);
                Assert.DoesNotContain("test-refresh", raw);
                Assert.Contains("dpapi:", raw);
            }
            var back = AccCredentialStore.Load(out string warn);
            Assert.Equal("", warn);
            Assert.Equal("test-secret", back.ClientSecret);
            Assert.Equal("test-refresh", back.RefreshToken);
        }

        [Fact]
        public void LegacyPlaintextFile_StillReads()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AccCredentialStore.CredentialsPath));
            File.WriteAllText(AccCredentialStore.CredentialsPath,
                "{\"ClientId\":\"c\",\"ClientSecret\":\"plain-secret\",\"RefreshToken\":\"plain-refresh\"}");
            var c = AccCredentialStore.Load(out string warn);
            Assert.Equal("plain-secret", c.ClientSecret);
            Assert.Equal("plain-refresh", c.RefreshToken);
        }

        [Fact]
        public void UndecryptableSecret_IsBlankedAndReported_NeverGarbage()
        {
            if (!OperatingSystem.IsWindows()) return;
            Directory.CreateDirectory(Path.GetDirectoryName(AccCredentialStore.CredentialsPath));
            File.WriteAllText(AccCredentialStore.CredentialsPath,
                "{\"ClientId\":\"c\",\"RefreshToken\":\"dpapi:AAAAAAAAAAAAAAAA\"}");
            var c = AccCredentialStore.Load(out string warn);
            Assert.Equal("", c.RefreshToken);
            Assert.Contains("sign in again", warn);
        }

        [Fact]
        public async Task ATokenAnotherSessionRotated_IsAdopted_NotRefreshedAgain()
        {
            // Another Revit process already refreshed: the file holds a newer refresh token and
            // a fresh access token. This session's stale copy must adopt them, not spend its
            // (now dead) refresh token.
            var other = H.Creds();
            other.RefreshToken = "rotated-by-other-session";
            other.AccessToken = "fresh-from-other-session";
            other.AccessTokenExpiry = DateTime.UtcNow.AddMinutes(50);
            Assert.True(AccIssueSync.SaveCredentials(other, out _));

            using var server = LoopbackServer.Always(400, "{\"error\":\"invalid_grant\"}");
            AccIssueSync.OverrideHostForTests(server.BaseUrl);
            var mine = H.Creds();
            mine.AccessTokenExpiry = DateTime.UtcNow.AddHours(-1);

            var o = await AccIssueSync.EnsureAuthDetailedAsync(mine);

            Assert.True(o.Ok, o.Detail);
            Assert.Equal(0, server.RequestCount);
            Assert.Equal("fresh-from-other-session", mine.AccessToken);
            Assert.Equal("rotated-by-other-session", mine.RefreshToken);
        }

        // D1: the BIM Coordination Center wrote the refresh token it loaded in the morning over
        // the one APS had rotated since, and other sessions then adopted the dead token.
        [Fact]
        public void AnOlderRefreshToken_NeverOverwritesANewerOneOnDisk()
        {
            var live = H.Creds();
            live.RefreshToken = "rotated-at-noon";
            live.RefreshTokenIssuedAt = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
            Assert.True(AccIssueSync.SaveCredentials(live, out _));

            var stale = H.Creds();
            stale.RefreshToken = "loaded-this-morning";
            stale.RefreshTokenIssuedAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
            Assert.True(AccIssueSync.SaveCredentials(stale, out _));

            Assert.Equal("rotated-at-noon", AccCredentialStore.Load(out _).RefreshToken);
            Assert.Equal("rotated-at-noon", stale.RefreshToken);   // the caller goes on with the live one
        }

        [Fact]
        public void ANewerTokenStillReplacesAnOlderOne()
        {
            var old = H.Creds();
            old.RefreshToken = "old"; old.RefreshTokenIssuedAt = DateTime.UtcNow.AddHours(-3);
            Assert.True(AccIssueSync.SaveCredentials(old, out _));
            var signedIn = H.Creds();
            signedIn.RefreshToken = "fresh-sign-in"; signedIn.RefreshTokenIssuedAt = DateTime.UtcNow;
            Assert.True(AccIssueSync.SaveCredentials(signedIn, out _));
            Assert.Equal("fresh-sign-in", AccCredentialStore.Load(out _).RefreshToken);
        }

        [Fact]
        public async Task ASessionHoldingTheNewerToken_DoesNotAdoptAnOlderFile()
        {
            var older = H.Creds();
            older.RefreshToken = "older-on-disk";
            older.RefreshTokenIssuedAt = DateTime.UtcNow.AddHours(-5);
            older.AccessToken = "older-access";
            older.AccessTokenExpiry = DateTime.UtcNow.AddMinutes(50);
            Assert.True(AccIssueSync.SaveCredentials(older, out _));

            using var server = LoopbackServer.Always(200,
                "{\"access_token\":\"refreshed-access\",\"refresh_token\":\"rotated-from-mine\",\"expires_in\":3600}");
            AccIssueSync.OverrideHostForTests(server.BaseUrl);
            var mine = H.Creds();
            mine.RefreshToken = "mine-newer";
            mine.RefreshTokenIssuedAt = DateTime.UtcNow.AddHours(-1);
            mine.AccessTokenExpiry = DateTime.UtcNow.AddHours(-1);

            var o = await AccIssueSync.EnsureAuthDetailedAsync(mine);

            Assert.True(o.Ok, o.Detail);
            Assert.Equal(1, server.RequestCount);                 // refreshed with its own token
            Assert.Equal("refreshed-access", mine.AccessToken);   // not the file's older one
            Assert.Equal("rotated-from-mine", AccCredentialStore.Load(out _).RefreshToken);
        }

        // D4: the loser of a refresh race got "sign in again" although the winner had saved a
        // valid rotated token by then.
        [Fact]
        public async Task LosingARefreshRace_AdoptsTheWinnersToken_NotSignInAgain()
        {
            var mine = H.Creds();
            mine.RefreshToken = "shared-before-race";
            mine.RefreshTokenIssuedAt = DateTime.UtcNow.AddHours(-2);
            mine.AccessTokenExpiry = DateTime.UtcNow.AddHours(-1);
            Assert.True(AccIssueSync.SaveCredentials(mine, out _));

            using var server = new LoopbackServer((n, req) =>
            {
                // The other session finished first: it saved the rotated token, then Autodesk
                // refuses ours (the same refresh token, already spent).
                var winner = H.Creds();
                winner.RefreshToken = "rotated-by-winner";
                winner.RefreshTokenIssuedAt = DateTime.UtcNow;
                winner.AccessToken = "winner-access";
                winner.AccessTokenExpiry = DateTime.UtcNow.AddMinutes(55);
                AccIssueSync.SaveCredentials(winner, out _);
                return new CannedResponse(400, "{\"error\":\"invalid_grant\"}");
            });
            AccIssueSync.OverrideHostForTests(server.BaseUrl);

            var o = await AccIssueSync.EnsureAuthDetailedAsync(mine);

            Assert.True(o.Ok, o.Detail);
            Assert.Equal("winner-access", mine.AccessToken);
            Assert.Equal("rotated-by-winner", mine.RefreshToken);
        }

        [Fact]
        public async Task ARefusedGrant_WithNobodyHoldingAValidToken_IsStillSignInAgain()
        {
            using var server = LoopbackServer.Always(400, "{\"error\":\"invalid_grant\"}");
            AccIssueSync.OverrideHostForTests(server.BaseUrl);
            var mine = H.Creds();
            mine.AccessTokenExpiry = DateTime.UtcNow.AddHours(-1);
            var o = await AccIssueSync.EnsureAuthDetailedAsync(mine);
            Assert.False(o.Ok);
            Assert.Equal(AccFetchStatus.AuthFailed, o.Status);
        }

        [Theory]
        [InlineData("b", 12, "a", 8, true)]     // file newer, different
        [InlineData("b", 8, "a", 12, false)]    // file older
        [InlineData("a", 12, "a", 8, false)]    // same token
        [InlineData("b", 0, "a", 8, false)]     // file has no issue time: never overrides
        [InlineData("b", 12, "a", 0, true)]     // mine has none: a dated file token wins
        public void FileTokenIsNewer(string fileTok, int fileHour, string mineTok, int mineHour, bool expected)
        {
            DateTime At(int h) => h == 0 ? default : new DateTime(2026, 10, 1, h, 0, 0, DateTimeKind.Utc);
            var file = H.Creds(); file.RefreshToken = fileTok; file.RefreshTokenIssuedAt = At(fileHour);
            var mine = H.Creds(); mine.RefreshToken = mineTok; mine.RefreshTokenIssuedAt = At(mineHour);
            Assert.Equal(expected, AccIssueSync.FileTokenIsNewer(file, mine));
        }
    }

    public class AccIdsAndIssueShapeTests : IDisposable
    {
        public void Dispose() => AccIssueSync.OverrideHostForTests(null);

        [Theory]
        [InlineData("b.abc", "abc", "b.abc")]
        [InlineData("abc", "abc", "b.abc")]
        [InlineData(" B.abc ", "abc", "B.abc")]
        [InlineData("", "", "")]
        public void EachApiFamilyGetsItsIdForm(string input, string acc, string dm)
        {
            Assert.Equal(acc, AccIds.ForAcc(input));
            Assert.Equal(dm, AccIds.ForDataManagement(input));
        }

        [Fact]
        public async Task PushIssue_UsesTheV1ProjectsPath_WithTheBareId_AndCamelCaseBody()
        {
            string path = null, body = null, region = null;
            using var server = new LoopbackServer((i, req) =>
            {
                path = req.Url.AbsolutePath;
                region = req.Headers["x-ads-region"];
                using (var sr = new StreamReader(req.InputStream)) body = sr.ReadToEnd();
                return new CannedResponse(201, "{\"id\":\"new\"}");
            });
            AccIssueSync.OverrideHostForTests(server.BaseUrl);
            var c = H.Creds();
            c.Region = "EMEA";
            var r = await AccIssueSync.PushIssueDetailedAsync(c, new AccIssue
            {
                Title = new string('T', 150), Description = "d", LocationDescription = "L",
                AssignedToUserId = "u1", AssignedToType = "user", DueDate = new DateTime(2026, 10, 14),
            });
            Assert.True(r.Ok, r.Detail);
            Assert.Equal($"/construction/issues/v1/projects/{H.Bare}/issues", path);
            Assert.Equal("EMEA", region);
            var j = JObject.Parse(body);
            Assert.Equal("sub-1", (string)j["issueSubtypeId"]);
            Assert.Null(j["issue_type_id"]);
            Assert.Equal("open", (string)j["status"]);
            Assert.Equal(100, ((string)j["title"]).Length);            // documented limit
            Assert.Equal("2026-10-14", (string)j["dueDate"]);
            Assert.Equal("user", (string)j["assignedToType"]);
        }

        [Fact]
        public async Task PushIssue_Rejected_ReportsWhy_NotJustNull()
        {
            using var server = LoopbackServer.Always(400, "{\"detail\":\"issueSubtypeId is invalid\"}");
            AccIssueSync.OverrideHostForTests(server.BaseUrl);
            var r = await AccIssueSync.PushIssueDetailedAsync(H.Creds(), new AccIssue { Title = "t" });
            Assert.False(r.Ok);
            Assert.Equal(400, r.HttpStatus);
            Assert.Contains("issueSubtypeId is invalid", r.Detail);
        }

        // E11: the due date is sent in the invariant culture. Under th-TH the current culture's
        // calendar is Thai Buddhist, so "yyyy" wrote 2569 for 2026 and ACC got a date 543 years out.
        [Theory]
        [InlineData("th-TH")]
        [InlineData("ar-SA")]
        [InlineData("en-GB")]
        public void BuildIssueBody_DueDate_IsInvariant_WhateverTheCulture(string culture)
        {
            var before = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(culture);
            try
            {
                var body = AccIssueSync.BuildIssueBody(new AccIssue { Title = "t", DueDate = new DateTime(2026, 10, 15) }, "subtype-1");
                Assert.Equal("2026-10-15", (string)body["dueDate"]);
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = before; }
        }

        [Fact]
        public void ParseIssue_ReadsV1FieldNames()
        {
            var i = AccIssueSync.ParseIssue(JObject.Parse(
                "{\"id\":\"x\",\"displayId\":42,\"title\":\"t\",\"status\":\"in_review\",\"issueSubtypeId\":\"s\"," +
                "\"assignedTo\":\"u\",\"assignedToType\":\"user\",\"locationDetails\":\"L2\",\"dueDate\":\"2026-10-01\"}"));
            Assert.Equal("42", i.DisplayId);
            Assert.Equal("s", i.IssueType);
            Assert.Equal("u", i.AssignedToUserId);
            Assert.Equal("L2", i.LocationDescription);
            Assert.Equal(new DateTime(2026, 10, 1), i.DueDate?.Date);
        }

        [Theory]
        [InlineData("closed", true)]
        [InlineData("completed", false)]   // responded, awaiting acceptance: still open work
        [InlineData("in_review", false)]
        [InlineData("open", false)]
        [InlineData("", false)]
        public void ClosedStatus_UsesTheAccVocabulary(string status, bool closed)
            => Assert.Equal(closed, AccIssueSync.IsClosedStatus(status));
    }

    public class IssueTypeChooserTests
    {
        private static List<JToken> Types(string json) => JArray.Parse(json).ToList();

        [Fact]
        public void PrefersClashByName_NotTheFirstOffered()
        {
            var c = IssueTypeChooser.Choose(Types(
                "[{\"id\":\"d\",\"title\":\"Design\",\"subtypes\":[{\"id\":\"d1\",\"title\":\"General\"}]}," +
                " {\"id\":\"c\",\"title\":\"Coordination\",\"subtypes\":[{\"id\":\"c1\",\"title\":\"RFI\"},{\"id\":\"c2\",\"title\":\"Clash\"}]}]"), "");
            Assert.True(c.Ok, c.Reason);
            Assert.Equal("c", c.TypeId);
            Assert.Equal("c2", c.SubtypeId);
        }

        [Fact]
        public void NoClashOrCoordinationType_ChoosesNothing_AndNamesTheOptions()
        {
            var c = IssueTypeChooser.Choose(Types(
                "[{\"id\":\"d\",\"title\":\"Design\",\"subtypes\":[{\"id\":\"d1\",\"title\":\"General\"}]}]"), "");
            Assert.False(c.Ok);
            Assert.Contains("'Design'", c.Reason);
        }

        [Fact]
        public void ATypesOnlySubtype_IsNotAGuess()
        {
            var c = IssueTypeChooser.Choose(Types(
                "[{\"id\":\"c\",\"title\":\"Coordination\",\"subtypes\":[{\"id\":\"c1\",\"title\":\"General\"}]}]"), "");
            Assert.True(c.Ok);
            Assert.Equal("c1", c.SubtypeId);
        }

        [Fact]
        public void AmbiguousSubtypes_AreRefused()
        {
            var c = IssueTypeChooser.Choose(Types(
                "[{\"id\":\"c\",\"title\":\"Coordination\",\"subtypes\":[{\"id\":\"a\",\"title\":\"A\"},{\"id\":\"b\",\"title\":\"B\"}]}]"), "");
            Assert.False(c.Ok);
        }
    }

    public class AccDisciplineResolverTests
    {
        [Theory]
        [InlineData("KUT-ELEC-DISTRIBUTION.rvt", "E")]      // was structural: diSTRibution
        [InlineData("CONSTRUCTION-SEQUENCE.nwc", "")]        // was structural
        [InlineData("KUT-PLN-ZZ-XX-M3-S-0001.rvt", "S")]     // ISO 19650 role field
        [InlineData("KUT-PLN-ZZ-XX-M3-M-0001.rvt", "M")]
        [InlineData("KUT-PLN-ZZ-XX-M3-D-0001.rvt", "P")]     // drainage → public health
        [InlineData("Temple_Structural_Model.rvt", "S")]
        [InlineData("Sprinkler layout.rvt", "FP")]
        [InlineData("", "")]
        public void DisciplineComesFromWholeTokens(string name, string expected)
            => Assert.Equal(expected, AccDisciplineResolver.Discipline(name));

        [Fact]
        public void TheProjectMapWins()
        {
            var map = new Dictionary<string, string> { ["PH"] = "E" };   // a project's own habit
            Assert.Equal("E", AccDisciplineResolver.Discipline("KUT_PH_Model.rvt", map));
            Assert.Equal("OST_ElectricalEquipment", AccDisciplineResolver.Ost("KUT_PH_Model.rvt", map));
        }
    }

    public class AccSettingsNewKeysTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "acc-settings-" + Guid.NewGuid().ToString("N") + ".json");
        public void Dispose() { try { File.Delete(_path); } catch (Exception) { } }

        private AccOperatingPolicy Load(string json) { File.WriteAllText(_path, json); return AccOperatingPolicy.Load(_path); }

        [Fact]
        public void AMalformedFile_ThatSaysUnattended_StaysNonInteractive_ButDiscardsEverythingElse()
        {
            // A typo must not turn a scheduled run back into one that waits on dialogs.
            var p = Load("{\"unattended\":true,\"coordModelSetId\":\"ms-1\",\"typoKey\":1}");
            Assert.Equal(AccPolicySource.Malformed, p.Source);
            Assert.False(p.MayPrompt);
            Assert.Equal("", p.CoordModelSetId);                     // still discarded
            var q = Load("{\"coordModelSetId\":\"ms-1\",\"typoKey\":1}");
            Assert.True(q.MayPrompt);                                  // no unattended claim: prompt as before
        }

        [Fact]
        public void UploadUnattended_IsOffUnlessSet_AndMustBeABoolean()
        {
            Assert.False(Load("{\"unattended\":true}").UploadUnattended);
            Assert.True(Load("{\"unattended\":true,\"uploadUnattended\":true}").UploadUnattended);
            Assert.Equal(AccPolicySource.Malformed, Load("{\"uploadUnattended\":\"yes\"}").Source);
        }

        [Fact]
        public void UploadAllowReissue_IsOffUnlessSet_AndMustBeABoolean()
        {
            Assert.False(Load("{\"unattended\":true}").UploadAllowReissue);
            Assert.True(Load("{\"uploadAllowReissue\":true}").UploadAllowReissue);
            Assert.Equal(AccPolicySource.Malformed, Load("{\"uploadAllowReissue\":1}").Source);
        }

        [Fact]
        public void AllNewKeysParse()
        {
            var p = Load("{\"hubId\":\"b.h\",\"folderUrn\":\"urn:f\",\"distToMm\":1,\"issueTypeId\":\"t\",\"issueSubtypeId\":\"s\"," +
                         "\"region\":\"emea\",\"cdeFolders\":{\"WIP\":\"urn:w\",\"PUBLISHED\":\"urn:p\"},\"disciplineMap\":{\"PH\":\"P\"}," +
                         "\"docsAttributes\":true,\"docsAttributesCreateMissing\":false,\"escalateDueDays\":7," +
                         "\"escalateAssignedTo\":\"u\",\"escalateAssignedToType\":\"User\",\"escalateExcludeStatuses\":[\"Closed\"]}");
            Assert.Equal(AccPolicySource.Loaded, p.Source);
            Assert.Equal("EMEA", p.Region);
            Assert.Equal(1.0, p.DistToMm);
            Assert.Equal("urn:w", p.CdeFolders["wip"]);
            Assert.Equal("P", p.DisciplineMap["PH"]);
            Assert.True(p.DocsAttributes);
            Assert.Equal(7, p.EscalateDueDays);
            Assert.Equal("user", p.EscalateAssignedToType);
            Assert.Equal(new[] { "closed" }, p.EscalateExcludeStatuses);
        }

        [Theory]
        [InlineData("{\"region\":\"MARS\"}")]
        [InlineData("{\"cdeFolders\":{\"DRAFT\":\"urn:x\"}}")]
        [InlineData("{\"escalateAssignedTo\":\"u\"}")]                  // half an assignee
        [InlineData("{\"distToMm\":0}")]
        [InlineData("{\"cdeFolders\":{\"WIP\":\"\"}}")]
        public void BadValues_MakeTheWholeFileMalformed(string json)
            => Assert.Equal(AccPolicySource.Malformed, Load(json).Source);

        [Fact]
        public void ProjectValues_OverlayTheMachineFile_AndAreNotSavedIntoIt()
        {
            var p = Load("{\"projectId\":\"b.proj\",\"hubId\":\"b.hub2\",\"folderUrn\":\"urn:proj\",\"distToMm\":1,\"region\":\"EMEA\"}");
            var c = new AccCredentials { ProjectId = "b.other", FolderUrn = "urn:machine", HubId = "b.hub1", DistToMm = 1000 };
            AccProjectScope.Apply(c, p);
            Assert.Equal("urn:proj", c.FolderUrn);
            Assert.Equal("b.hub2", c.HubId);
            Assert.Equal(1, c.DistToMm);
            Assert.Equal("EMEA", c.Region);
            var file = AccIssueSync.ToMachineFile(c);
            Assert.Equal("urn:machine", (string)file["FolderUrn"]);
            Assert.Equal("b.hub1", (string)file["HubId"]);
            Assert.Equal(1000.0, (double)file["DistToMm"]);
            Assert.Equal("b.other", (string)file["ProjectId"]);
        }

        [Fact]
        public void ADifferentProject_DoesNotInheritTheMachineFolder()
        {
            var p = Load("{\"projectId\":\"b.proj\"}");
            var c = new AccCredentials { ProjectId = "b.other", FolderUrn = "urn:other-projects-folder" };
            AccProjectScope.Apply(c, p);
            Assert.Equal("", c.FolderUrn);
        }
    }

    public class AccClashPullShapeTests : IDisposable
    {
        public void Dispose() => AccModelCoordSync.OverrideHostForTests(null);

        private static byte[] Json(string s) => System.Text.Encoding.UTF8.GetBytes(s);

        private static LoopbackServer Server(string testsJson, int clashCount)
        {
            LoopbackServer server = null;
            server = new LoopbackServer((i, req) =>
            {
                string p = req.Url.AbsolutePath;
                if (p.EndsWith("/tests")) return new CannedResponse(200, testsJson);
                if (p.EndsWith("/resources"))
                    return new CannedResponse(200, "{\"resources\":[" +
                        $"{{\"type\":\"scope-version-clash\",\"url\":\"{server.BaseUrl}/files/clash\"}}," +
                        $"{{\"type\":\"scope-version-clash-instance\",\"url\":\"{server.BaseUrl}/files/instance\"}}," +
                        $"{{\"type\":\"scope-version-document\",\"url\":\"{server.BaseUrl}/files/document\"}}]}}");
                if (p.EndsWith("/files/clash"))
                    return new CannedResponse(200, "{\"clashes\":[" + string.Join(",", Enumerable.Range(1, clashCount).Select(n => $"{{\"id\":\"{n}\",\"dist\":-0.05,\"status\":\"active\"}}")) + "]}");
                if (p.EndsWith("/files/instance"))
                    return new CannedResponse(200, "{\"instances\":[" + string.Join(",", Enumerable.Range(1, clashCount).Select(n => $"{{\"cid\":\"{n}\",\"ldid\":\"1\",\"rdid\":\"2\",\"lvid\":{n},\"rvid\":{n + 1000}}}")) + "]}");
                if (p.EndsWith("/files/document"))
                    return new CannedResponse(200, "{\"documents\":[{\"id\":\"1\",\"name\":\"KUT-STR.rvt\"},{\"id\":\"2\",\"name\":\"KUT-MECH.rvt\"}]}");
                return new CannedResponse(404, "{}");
            });
            AccModelCoordSync.OverrideHostForTests(server.BaseUrl);
            return server;
        }

        [Fact]
        public async Task NoCompletedTest_IsNotReady_NotClean_AndReadsNothing()
        {
            using var server = Server("{\"tests\":[{\"id\":\"t1\",\"status\":\"Processing\"}]}", 3);
            var r = await AccModelCoordSync.GetClashesAsync(H.Creds(), H.Project, "ms");
            Assert.True(r.NotReady);
            Assert.Empty(r.Value);
            Assert.Contains("Processing", r.Detail);
            Assert.DoesNotContain(server.Paths, p => p.Contains("/resources"));
        }

        [Fact]
        public async Task SuccessStatus_CountsAsCompleted_AndEveryClashIsRead()
        {
            using var server = Server("{\"tests\":[{\"id\":\"t1\",\"status\":\"Success\",\"completedAt\":\"2026-09-30\"}]}", 1500);
            var r = await AccModelCoordSync.GetClashesAsync(H.Creds(), H.Project, "ms");
            Assert.True(r.Succeeded, r.Detail);
            Assert.Equal(1500, r.Value.Count);          // no silent 1,000 cap
            Assert.False(r.Truncated);
            Assert.True(r.Value.All(c => c.DocumentsNamed));
            Assert.Contains(server.Paths, p => p.Contains($"/containers/{H.Bare}/"));   // bare id for MC
        }

        [Fact]
        public async Task ACallerCap_IsReportedAsTruncated()
        {
            using var server = Server("{\"tests\":[{\"id\":\"t1\",\"status\":\"Success\"}]}", 30);
            var r = await AccModelCoordSync.GetClashesAsync(H.Creds(), H.Project, "ms", max: 10);
            Assert.Equal(10, r.Value.Count);
            Assert.True(r.Truncated);
            Assert.Equal(30, r.TotalAvailable);
        }
    }

    public class AccUploadHardeningTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "sting-up2-" + Guid.NewGuid().ToString("N"));
        private readonly long _savedPart = AccModelUpload.PartSizeBytes;

        public AccUploadHardeningTests()
        {
            Directory.CreateDirectory(_dir);
            AccHttp.DelayHook = _ => Task.CompletedTask;
        }

        public void Dispose()
        {
            AccModelUpload.PartSizeBytes = _savedPart;
            AccModelUpload.OverrideHostForTests(null);
            AccHttp.DelayHook = t => Task.Delay(t);
            try { Directory.Delete(_dir, true); } catch (Exception) { }
        }

        private string File(int bytes)
        {
            string f = Path.Combine(_dir, "KUT-PLN-ZZ-XX-M3-A-0001.rvt");
            System.IO.File.WriteAllBytes(f, new byte[bytes]);
            return f;
        }

        [Fact]
        public async Task ManyParts_AreSignedInBatchesOf25_AndAnExpiredUrlIsRenewed()
        {
            AccModelUpload.PartSizeBytes = 10;           // 300 bytes → 30 parts → 2 sign requests
            var signs = new List<string>();
            bool expiredOnce = false;
            LoopbackServer server = null;
            server = new LoopbackServer((i, req) =>
            {
                string p = req.Url.PathAndQuery;
                if (p.Contains("/storage")) return new CannedResponse(201, "{\"data\":{\"id\":\"urn:adsk.objects:os.object:bucket/obj\"}}");
                if (req.HttpMethod == "GET" && p.Contains("signeds3upload"))
                {
                    signs.Add(p);
                    var q = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                    int first = int.Parse(q["firstPart"]), n = int.Parse(q["parts"]);
                    var urls = Enumerable.Range(first, n).Select(k => $"\"{server.BaseUrl}/s3/{k}?sig={signs.Count}\"");
                    return new CannedResponse(200, "{\"uploadKey\":\"UK\",\"urls\":[" + string.Join(",", urls) + "]}");
                }
                if (req.HttpMethod == "PUT")
                {
                    if (p.StartsWith("/s3/7?sig=1") && !expiredOnce) { expiredOnce = true; return new CannedResponse(403, ""); }
                    return new CannedResponse(200, "");
                }
                if (req.HttpMethod == "POST" && p.Contains("signeds3upload")) return new CannedResponse(200, "{}");
                if (p.Contains("/items"))
                    return new CannedResponse(201, "{\"data\":{\"id\":\"urn:item\"},\"included\":[{\"type\":\"versions\",\"id\":\"urn:ver?version=1\"}]}");
                return new CannedResponse(404, "{}");
            });
            AccModelUpload.OverrideHostForTests(server.BaseUrl);
            var c = H.Creds();
            c.FolderUrn = "urn:adsk.wipprod:fs.folder:co.X";

            var r = await AccModelUpload.UploadAsync(c, File(300));

            Assert.True(r.Ok, r.Message);
            Assert.Equal("urn:ver?version=1", r.VersionUrn);
            Assert.Contains(signs, s => s.Contains("firstPart=1") && s.Contains("parts=25"));
            Assert.Contains(signs, s => s.Contains("firstPart=26") && s.Contains("parts=5") && s.Contains("uploadKey=UK"));
            Assert.Contains(signs, s => s.Contains("firstPart=7"));          // the renewal
            Assert.True(expiredOnce);
        }

        [Fact]
        public async Task NoProjectFilesFolder_UploadsNothing_AndNamesTheFolders()
        {
            using var server = new LoopbackServer((i, req) =>
                req.Url.AbsolutePath.EndsWith("/topFolders")
                    ? new CannedResponse(200, "{\"data\":[{\"id\":\"urn:plans\",\"attributes\":{\"name\":\"Plans\"}}]}")
                    : new CannedResponse(500, "{}"));
            AccModelUpload.OverrideHostForTests(server.BaseUrl);
            var r = await AccModelUpload.UploadAsync(H.Creds(), File(10));
            Assert.False(r.Ok);
            Assert.Contains("'Plans'", r.Message);
            Assert.DoesNotContain(server.Paths, p => p.Contains("/storage"));
        }

        [Fact]
        public async Task CdeFoldersConfigured_AStateWithNoFolder_IsRefused_NotSentElsewhere()
        {
            using var server = LoopbackServer.Always(500, "{}");
            AccModelUpload.OverrideHostForTests(server.BaseUrl);
            var c = H.Creds();
            c.FolderUrn = "urn:adsk.wipprod:fs.folder:co.DEFAULT";
            var r = await AccModelUpload.UploadAsync(c, File(10), new AccUploadOptions
            {
                Suitability = "A1",   // PUBLISHED
                CdeFolders = new Dictionary<string, string> { ["WIP"] = "urn:adsk.wipprod:fs.folder:co.W" },
            });
            Assert.False(r.Ok);
            Assert.Contains("PUBLISHED", r.Message);
            Assert.Equal(0, server.RequestCount);
        }
    }

    public class AccUploadResumeTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "sting-resume-" + Guid.NewGuid().ToString("N"));
        private readonly long _savedPart = AccModelUpload.PartSizeBytes;

        public AccUploadResumeTests()
        {
            Directory.CreateDirectory(_dir);
            AccHttp.DelayHook = _ => Task.CompletedTask;
            AccModelUpload.PartSizeBytes = 10;          // 100 bytes → 10 parts
        }

        public void Dispose()
        {
            AccModelUpload.PartSizeBytes = _savedPart;
            AccModelUpload.OverrideHostForTests(null);
            AccHttp.DelayHook = t => Task.Delay(t);
            try { Directory.Delete(_dir, true); } catch (Exception) { }
        }

        private sealed class FakeAcc
        {
            public int StoragePosts, Finalises;
            public List<int> PartsPut = new List<int>();
            public Func<int, int> PartStatus = _ => 200;      // part number → status
            public Func<string, int> SignStatus = _ => 200;   // query → status
            public LoopbackServer Server;
            public FakeAcc()
            {
                Server = new LoopbackServer((i, req) =>
                {
                    string p = req.Url.PathAndQuery;
                    if (p.Contains("/storage"))
                    {
                        StoragePosts++;
                        return new CannedResponse(201, "{\"data\":{\"id\":\"urn:adsk.objects:os.object:bucket/obj" + StoragePosts + "\"}}");
                    }
                    if (req.HttpMethod == "GET" && p.Contains("signeds3upload"))
                    {
                        int st = SignStatus(req.Url.Query);
                        if (st != 200) return new CannedResponse(st, "{\"reason\":\"upload key expired\"}");
                        var q = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                        int first = int.Parse(q["firstPart"]), n = int.Parse(q["parts"]);
                        var urls = Enumerable.Range(first, n).Select(k => $"\"{Server.BaseUrl}/s3/{k}\"");
                        return new CannedResponse(200, "{\"uploadKey\":\"UK\",\"urls\":[" + string.Join(",", urls) + "]}");
                    }
                    if (req.HttpMethod == "PUT")
                    {
                        int part = int.Parse(req.Url.AbsolutePath.Split('/').Last());
                        int st = PartStatus(part);
                        if (st == 200) PartsPut.Add(part);
                        return new CannedResponse(st, "");
                    }
                    if (req.HttpMethod == "POST" && p.Contains("signeds3upload")) { Finalises++; return new CannedResponse(200, "{}"); }
                    if (p.Contains("/items"))
                        return new CannedResponse(201, "{\"data\":{\"id\":\"urn:item\"},\"included\":[{\"type\":\"versions\",\"id\":\"urn:ver\"}]}");
                    return new CannedResponse(404, "{}");
                });
                AccModelUpload.OverrideHostForTests(Server.BaseUrl);
            }
        }

        private static AccCredentials Creds()
        {
            var c = H.Creds();
            c.FolderUrn = "urn:adsk.wipprod:fs.folder:co.X";
            return c;
        }

        private string NewFile(int bytes)
        {
            string f = Path.Combine(_dir, "model.rvt");
            File.WriteAllBytes(f, new byte[bytes]);
            return f;
        }

        [Fact]
        public async Task AnInterruptedUpload_ResumesAfterTheLastConfirmedPart_WithoutANewStorageObject()
        {
            var acc = new FakeAcc();
            using var server = acc.Server;
            bool broken = true;
            acc.PartStatus = part => part == 7 && broken ? 500 : 200;
            string file = NewFile(100);

            var first = await AccModelUpload.UploadAsync(Creds(), file);
            Assert.False(first.Ok);
            Assert.Contains("resume from part 7", first.Message);
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, acc.PartsPut.ToArray());

            broken = false;
            var second = await AccModelUpload.UploadAsync(Creds(), file);

            Assert.True(second.Ok, second.Message);
            Assert.Contains("resumed", second.Message);
            Assert.Equal(1, acc.StoragePosts);                                        // same storage object
            Assert.Equal(Enumerable.Range(1, 10).ToArray(), acc.PartsPut.ToArray()); // 1..6 not sent again
            Assert.Equal(1, acc.Finalises);

            // Finished: the next upload of the same file is a NEW upload (a new version), not a resume.
            var third = await AccModelUpload.UploadAsync(Creds(), file);
            Assert.True(third.Ok, third.Message);
            Assert.Equal(2, acc.StoragePosts);
        }

        [Fact]
        public async Task AnExpiredSession_StartsAgainFromTheFirstByte()
        {
            var acc = new FakeAcc();
            using var server = acc.Server;
            bool broken = true;
            acc.PartStatus = part => part == 4 && broken ? 500 : 200;
            string file = NewFile(100);
            Assert.False((await AccModelUpload.UploadAsync(Creds(), file)).Ok);

            broken = false;
            acc.SignStatus = q => q.Contains("uploadKey=UK") && acc.StoragePosts == 1 ? 400 : 200;   // Autodesk forgot the session
            acc.PartsPut.Clear();
            var again = await AccModelUpload.UploadAsync(Creds(), file);

            Assert.True(again.Ok, again.Message);
            Assert.Equal(2, acc.StoragePosts);                                        // a fresh storage object
            Assert.Equal(Enumerable.Range(1, 10).ToArray(), acc.PartsPut.ToArray());
        }

        [Fact]
        public async Task AChangedFile_IsNeverResumedIntoTheOldObject()
        {
            var acc = new FakeAcc();
            using var server = acc.Server;
            bool broken = true;
            acc.PartStatus = part => part == 3 && broken ? 500 : 200;
            string file = NewFile(100);
            Assert.False((await AccModelUpload.UploadAsync(Creds(), file)).Ok);

            broken = false;
            File.WriteAllBytes(file, new byte[95]);                                   // edited since
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));
            acc.PartsPut.Clear();
            var r = await AccModelUpload.UploadAsync(Creds(), file);

            Assert.True(r.Ok, r.Message);
            Assert.Equal(2, acc.StoragePosts);
            Assert.Equal(Enumerable.Range(1, 10).ToArray(), acc.PartsPut.ToArray());
        }
    }

    public class AccPkceAndDiscoveryTests : IDisposable
    {
        public void Dispose() => AccProjectDiscovery.OverrideHostForTests(null);

        [Fact]
        public void CodeChallenge_MatchesRfc7636AppendixB()
            => Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
                AccOAuthFlow.CodeChallengeS256("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

        [Fact]
        public void AuthorizeUrl_CarriesS256Challenge()
        {
            string url = AccOAuthFlow.BuildAuthorizeUrl("cid", "http://localhost:8910/callback", "data:read", "st", "CH");
            Assert.Contains("code_challenge=CH", url);
            Assert.Contains("code_challenge_method=S256", url);
            Assert.True(AccOAuthFlow.NewCodeVerifier().Length >= 43);
        }

        [Fact]
        public async Task Projects_FollowLinksNext_AndCarryTheHubRegion()
        {
            LoopbackServer server = null;
            server = new LoopbackServer((i, req) =>
            {
                string p = req.Url.PathAndQuery;
                if (p.StartsWith("/project/v1/hubs?"))
                    return new CannedResponse(200, "{\"data\":[{\"id\":\"b.h\",\"attributes\":{\"name\":\"Hub\",\"region\":\"EMEA\"}}]}");
                if (p.Contains("page[number]=1") || p.Contains("page%5Bnumber%5D=1"))
                    return new CannedResponse(200, "{\"data\":[{\"id\":\"b.p2\",\"attributes\":{\"name\":\"Two\"}}]}");
                return new CannedResponse(200, "{\"data\":[{\"id\":\"b.p1\",\"attributes\":{\"name\":\"One\"}}]," +
                    $"\"links\":{{\"next\":{{\"href\":\"{server.BaseUrl}/project/v1/hubs/b.h/projects?page[number]=1&page[limit]=200\"}}}}}}");
            });
            AccProjectDiscovery.OverrideHostForTests(server.BaseUrl);
            var r = await AccProjectDiscovery.ListAllProjectsAsync(H.Creds());
            Assert.True(r.Succeeded, r.Detail);
            Assert.Equal(new[] { "b.p1", "b.p2" }, r.Value.Select(p => p.Id).ToArray());
            Assert.All(r.Value, p => Assert.Equal("EMEA", p.Region));
        }
    }

    // C3: a create retried after a timeout must resolve to the same server issue.
    public class IssueServerCreateKeyTests
    {
        private static readonly Guid Project = Guid.Parse("11111111-2222-3333-4444-555555555555");

        [Fact]
        public void TheKey_IsStablePerProjectAndIssue_AndCaseInsensitiveOnTheId()
        {
            string k = StingTools.Core.IssueSchema.ServerCreateKey(Project, "iss-0042");
            Assert.Equal(k, StingTools.Core.IssueSchema.ServerCreateKey(Project, " ISS-0042 "));
            Assert.NotEqual(k, StingTools.Core.IssueSchema.ServerCreateKey(Project, "ISS-0043"));
            Assert.NotEqual(k, StingTools.Core.IssueSchema.ServerCreateKey(Guid.NewGuid(), "ISS-0042"));
            Assert.StartsWith("sting-issue-create:", k);
        }

        [Fact]
        public void NoStableIdentity_NoKey()
        {
            Assert.Null(StingTools.Core.IssueSchema.ServerCreateKey(Guid.Empty, "ISS-1"));
            Assert.Null(StingTools.Core.IssueSchema.ServerCreateKey(Project, "  "));
        }
    }

    public class AccOwnedIssueTests
    {
        [Theory]
        [InlineData("{\"acc_issue_id\":\"x\"}", true)]
        [InlineData("{\"acc_origin\":\"clash\"}", true)]
        [InlineData("{\"source\":\"ACC\"}", true)]
        [InlineData("{\"source\":\"manual\"}", false)]
        [InlineData("{\"source\":\"clash\",\"acc_issue_id\":\"\"}", false)]
        public void AnIssueAccOwns_IsNeverPushedToTheServerAsNew(string json, bool accOwned)
            => Assert.Equal(accOwned, StingTools.Core.IssueSchema.IsAccOwned(JObject.Parse(json)));
    }

    public class WorkflowFailOnErrorTests
    {
        [Fact]
        public void AnOptionalStep_ToleratesFailure_UnlessFailOnError()
        {
            Assert.True(new WorkflowStep { Optional = true }.ToleratesFailure);
            Assert.False(new WorkflowStep { Optional = true, FailOnError = true }.ToleratesFailure);
            Assert.False(new WorkflowStep { Optional = false }.ToleratesFailure);
        }

        [Fact]
        public void AnUpstreamFailure_BlocksRequiredAndFailOnErrorSteps_NotTolerantOnes()
        {
            var failed = new List<int> { 1 };
            var none = new List<int>();
            Assert.True(WorkflowStepGate.IsBlocked(new WorkflowStep(), 3, failed, none, out int g));
            Assert.Equal(1, g);
            Assert.True(WorkflowStepGate.IsBlocked(new WorkflowStep { Optional = true, FailOnError = true }, 3, failed, none, out _));
            Assert.False(WorkflowStepGate.IsBlocked(new WorkflowStep { Optional = true }, 3, failed, none, out _));
            Assert.False(WorkflowStepGate.IsBlocked(new WorkflowStep { Optional = true, FailOnError = true, RunAfterFailure = true }, 3, failed, none, out _));
            Assert.True(WorkflowStepGate.IsBlocked(new WorkflowStep { Optional = true, RunAfterFailure = false }, 3, failed, none, out _));
            // A group that succeeded after the failure clears the block; nothing failed = nothing blocked.
            Assert.False(WorkflowStepGate.IsBlocked(new WorkflowStep(), 3, failed, new List<int> { 2 }, out _));
            Assert.False(WorkflowStepGate.IsBlocked(new WorkflowStep(), 3, none, none, out _));
            Assert.False(WorkflowStepGate.IsBlocked(new WorkflowStep(), 1, failed, none, out _));
        }

        [Fact]
        public void TheKutFortnightlyIssue_NeverPublishesToAcc_AfterItsRevisionGateFailed()
        {
            string path = null;
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                string cand = Path.Combine(d.FullName, "StingTools", "Data", "WORKFLOW_KUT_FortnightlyIssue.json");
                if (System.IO.File.Exists(cand)) { path = cand; break; }
            }
            Assert.NotNull(path);
            var preset = Newtonsoft.Json.JsonConvert.DeserializeObject<WorkflowPreset>(System.IO.File.ReadAllText(path));
            // Step 1 (group 1) failed; steps 2-5 were blocked, so nothing has succeeded since.
            var failed = new List<int> { 1 };
            var none = new List<int>();
            for (int i = 1; i < preset.Steps.Count; i++)
            {
                var step = preset.Steps[i];
                bool blocked = WorkflowStepGate.IsBlocked(step, i + 1, failed, none, out _);
                if (step.CommandTag.StartsWith("ACC", StringComparison.OrdinalIgnoreCase))
                    Assert.True(blocked, step.CommandTag + " must not run after the revision gate failed");
            }
        }

        [Fact]
        public void TheKutCycle_CountsAFailedAccPull_AsAFailure()
        {
            string dir = AppContext.BaseDirectory;
            string path = null;
            for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
            {
                string cand = Path.Combine(d.FullName, "StingTools", "Data", "WORKFLOW_KUT_CoordinationCycle.json");
                if (System.IO.File.Exists(cand)) { path = cand; break; }
            }
            Assert.NotNull(path);
            var preset = Newtonsoft.Json.JsonConvert.DeserializeObject<WorkflowPreset>(System.IO.File.ReadAllText(path));
            foreach (var tag in new[] { "ACC_PullClashes", "ACC_SyncIssueStatus" })
            {
                var step = preset.Steps.Single(s => s.CommandTag == tag);
                Assert.True(step.Optional);
                Assert.False(step.ToleratesFailure);
            }
        }
    }
}
