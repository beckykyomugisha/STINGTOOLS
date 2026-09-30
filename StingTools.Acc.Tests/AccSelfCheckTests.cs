// ACC_SelfCheck: the go-live check must PASS only what it checked, SKIP what it could not
// reach, and never write to ACC. Driven over a real loopback listener that plays every ACC
// service the check touches, so the assertions are about the real clients' behaviour.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Acc.Tests.TestHelpers;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccSelfCheckTests : IDisposable
    {
        private const string Project = "b.11111111-2222-3333-4444-555555555555";
        private const string Bare = "11111111-2222-3333-4444-555555555555";
        private const string Hub = "b.hub-kut";
        private static readonly string[] Folders =
        {
            "urn:adsk.wipprod:fs.folder:co.wip", "urn:adsk.wipprod:fs.folder:co.shared",
            "urn:adsk.wipprod:fs.folder:co.pub", "urn:adsk.wipprod:fs.folder:co.arch",
        };

        private readonly string _dir = Path.Combine(Path.GetTempPath(), "sting-selfcheck-" + Guid.NewGuid().ToString("N"));
        private readonly List<string> _requests = new List<string>();

        // Knobs a test turns to break one thing.
        private int _tokenStatus = 200;
        private string _projectsBody;
        private int _attributeCount = AccDocsAttributeSet.All.Count;
        private string _testStatus = "Success";

        public AccSelfCheckTests()
        {
            CredentialIsolation.Reset();
            Directory.CreateDirectory(_dir);
            AccHttp.DelayHook = _ => Task.CompletedTask;
            _projectsBody = new JObject
            {
                ["data"] = new JArray(new JObject { ["id"] = Project, ["attributes"] = new JObject { ["name"] = "KUT Temple" } }),
            }.ToString();
        }

        public void Dispose()
        {
            AccModelCoordSync.OverrideHostForTests(null);
            AccProjectDiscovery.OverrideHostForTests(null);
            AccDocsMetadata.OverrideHostForTests(null);
            AccIssueSync.OverrideHostForTests(null);
            AccHttp.DelayHook = t => Task.Delay(t);
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        // ── the fake ACC ─────────────────────────────────────────────────────

        private int _membersStatus = 200;

        private LoopbackServer Serve()
        {
            var server = new LoopbackServer((_, req) =>
            {
                string path = WebUtility.UrlDecode(req.Url.AbsolutePath);
                lock (_requests) _requests.Add(req.HttpMethod + " " + path);

                if (path.EndsWith("/authentication/v2/token"))
                    return _tokenStatus == 200
                        ? new CannedResponse(200, "{\"access_token\":\"fresh\",\"refresh_token\":\"rotated\",\"expires_in\":3600}")
                        : new CannedResponse(_tokenStatus, "{\"error\":\"invalid_grant\"}");

                if (path == "/project/v1/hubs")
                    return new CannedResponse(200, new JObject
                    {
                        ["data"] = new JArray(new JObject { ["id"] = Hub, ["attributes"] = new JObject { ["name"] = "Planscape", ["region"] = "US" } }),
                    }.ToString());
                if (path == $"/project/v1/hubs/{Hub}/projects")
                    return new CannedResponse(200, _projectsBody);

                if (path == $"/construction/issues/v1/projects/{Bare}/issue-types")
                    return new CannedResponse(200, new JObject
                    {
                        ["results"] = new JArray(
                            new JObject { ["id"] = "t-design", ["title"] = "Design", ["isActive"] = true,
                                ["subtypes"] = new JArray(new JObject { ["id"] = "s-design", ["title"] = "Design", ["isActive"] = true }) },
                            new JObject { ["id"] = "t-coord", ["title"] = "Coordination", ["isActive"] = true,
                                ["subtypes"] = new JArray(new JObject { ["id"] = "s-clash", ["title"] = "Clash", ["isActive"] = true }) }),
                    }.ToString());
                if (path == $"/construction/issues/v1/projects/{Bare}/issues")
                    return new CannedResponse(200, "{\"results\":[{\"id\":\"i1\",\"title\":\"x\"}],\"pagination\":{\"limit\":1,\"offset\":0,\"totalResults\":42}}");

                if (path == $"/construction/admin/v1/projects/{Bare}/users")
                    return _membersStatus == 200
                        ? new CannedResponse(200, "{\"pagination\":{\"totalResults\":1},\"results\":[{\"id\":\"f-1\",\"autodeskId\":\"ADSK1111\"," +
                                                  "\"email\":\"bim@planscape.build\",\"name\":\"BIM Lead\",\"status\":\"active\",\"roles\":[]}]}")
                        : new CannedResponse(_membersStatus, "{}");

                if (path == $"/bim360/modelset/v3/containers/{Bare}/modelsets")
                    return new CannedResponse(200, "{\"modelSets\":[{\"modelSetId\":\"ms-fed\",\"name\":\"KUT – Federated (SHARED)\"}]}");
                if (path == $"/bim360/clash/v3/containers/{Bare}/modelsets/ms-fed/tests")
                    return new CannedResponse(200, new JObject
                    {
                        ["tests"] = new JArray(new JObject { ["id"] = "test-1", ["status"] = _testStatus, ["completedAt"] = "2026-09-29T10:00:00Z" }),
                    }.ToString());

                if (path.StartsWith($"/data/v1/projects/{Project}/folders/"))
                {
                    string urn = path.Substring($"/data/v1/projects/{Project}/folders/".Length);
                    return Folders.Contains(urn)
                        ? new CannedResponse(200, new JObject { ["data"] = new JObject { ["id"] = urn, ["attributes"] = new JObject { ["displayName"] = urn.Split('.').Last() } } }.ToString())
                        : new CannedResponse(404, "{}");
                }

                if (path.StartsWith($"/bim360/docs/v1/projects/{Bare}/folders/") && path.EndsWith("/custom-attribute-definitions"))
                {
                    var defs = new JArray(AccDocsAttributeSet.All.Take(_attributeCount)
                        .Select((s, i) => new JObject { ["id"] = 100 + i, ["name"] = s.Name, ["type"] = s.Type }));
                    return new CannedResponse(200, new JObject
                    {
                        ["results"] = defs,
                        ["pagination"] = new JObject { ["totalResults"] = defs.Count },
                    }.ToString());
                }

                return new CannedResponse(599, "{\"unexpected\":\"" + path + "\"}");
            });
            AccModelCoordSync.OverrideHostForTests(server.BaseUrl);
            AccProjectDiscovery.OverrideHostForTests(server.BaseUrl);
            AccDocsMetadata.OverrideHostForTests(server.BaseUrl);
            return server;
        }

        private AccOperatingPolicy Settings(JObject o)
        {
            string path = Path.Combine(_dir, AccOperatingPolicy.FileName);
            File.WriteAllText(path, o.ToString());
            return AccOperatingPolicy.Load(path);
        }

        private static JObject GoodSettings() => new JObject
        {
            ["projectId"] = Project,
            ["hubId"] = Hub,
            ["coordModelSetId"] = "ms-fed",
            ["coordModelSetName"] = "KUT – Federated (SHARED)",
            ["cdeFolders"] = new JObject { ["WIP"] = Folders[0], ["SHARED"] = Folders[1], ["PUBLISHED"] = Folders[2], ["ARCHIVE"] = Folders[3] },
            ["docsAttributes"] = true,
            ["escalateAssignedTo"] = "bim@planscape.build",
        };

        private static AccCredentials MachineCreds() => new AccCredentials
        {
            ClientId = "test-client",
            ClientSecret = "test-secret",
            RefreshToken = "test-refresh",
            AccessToken = "stale",
            AccessTokenExpiry = DateTime.UtcNow.AddMinutes(30),
            RefreshTokenIssuedAt = DateTime.UtcNow.AddDays(-1),
        };

        private static async Task<List<AccCheckResult>> Run(AccCredentials creds, AccOperatingPolicy policy)
        {
            AccProjectScope.Apply(creds, policy);
            return await AccSelfCheck.RunAsync(creds, policy, DateTime.UtcNow);
        }

        private static AccCheckResult Row(List<AccCheckResult> r, string id) => r.Single(x => x.Id == id);

        private void AssertReadOnly()
        {
            var writes = _requests.Where(r => !r.StartsWith("GET ") && !r.EndsWith("/authentication/v2/token")).ToList();
            Assert.True(writes.Count == 0, "the self-check wrote to ACC: " + string.Join("; ", writes));
        }

        // ── tests ────────────────────────────────────────────────────────────

        [Fact]
        public async Task HappyPath_EveryCheckPasses_AndNothingIsWrittenToAcc()
        {
            using var server = Serve();
            var results = await Run(MachineCreds(), Settings(GoodSettings()));

            var notPass = results.Where(r => r.Status != AccCheckStatus.Pass).ToList();
            // The ONLY non-pass is the write-scope row: it cannot be proved without writing.
            Assert.Single(notPass);
            Assert.Equal("7.2", notPass[0].Id);
            Assert.Equal(AccCheckStatus.Skipped, notPass[0].Status);
            Assert.False(AccSelfCheck.AnyFail(results));

            Assert.Contains("Coordination / Clash", Row(results, "4.1").Detail);
            Assert.Contains("42", Row(results, "4.2").Detail);
            Assert.Contains("BIM Lead", Row(results, "4.3").Detail);
            Assert.Contains("test-1", Row(results, "5.3").Detail);
            Assert.Equal(4, results.Count(r => r.Id.StartsWith("6.1.")));
            Assert.Equal(4, results.Count(r => r.Id.StartsWith("6.2.")));
            AssertReadOnly();
            // No scope file or resources call: the clash check is the tests endpoint only.
            Assert.DoesNotContain(_requests, r => r.Contains("/resources"));
            Assert.StartsWith("READY", AccSelfCheck.Summary(results));
        }

        [Fact]
        public async Task Assignee_MembersUnreadable_AnEmailFails_AnIdWarns_AndNeitherBlames7_1()
        {
            _membersStatus = 403;
            using (Serve())
            {
                var results = await Run(MachineCreds(), Settings(GoodSettings()));
                var row = Row(results, "4.3");
                Assert.Equal(AccCheckStatus.Fail, row.Status);
                Assert.Contains("REFUSED", row.Detail);
                Assert.Contains("Admin", row.Detail);
                Assert.Equal(AccCheckStatus.Pass, Row(results, "7.1").Status);   // an expected admin-only 403 is not a scope fault
            }
            using (Serve())
            {
                var o = GoodSettings();
                o["escalateAssignedTo"] = "ADSK1111";
                o["escalateAssignedToType"] = "user";
                var results = await Run(MachineCreds(), Settings(o));
                Assert.Equal(AccCheckStatus.Warn, Row(results, "4.3").Status);
            }
        }

        [Fact]
        public async Task Assignee_NotConfigured_IsSkipped_NotPassed()
        {
            using var server = Serve();
            var o = GoodSettings();
            o.Remove("escalateAssignedTo");
            var results = await Run(MachineCreds(), Settings(o));
            Assert.Equal(AccCheckStatus.Skipped, Row(results, "4.3").Status);
        }

        [Fact]
        public async Task AuthRejected_FailsSignIn_AndSkipsEveryLaterStep_WithTheReason()
        {
            _tokenStatus = 400;
            using var server = Serve();
            var results = await Run(MachineCreds(), Settings(GoodSettings()));

            var signIn = Row(results, "2.1");
            Assert.Equal(AccCheckStatus.Fail, signIn.Status);
            Assert.Contains("refused", signIn.Detail);
            Assert.Contains("Sign in with Autodesk again", signIn.Remedy);

            foreach (var id in new[] { "2.2", "3.1", "3.2", "4.1", "4.2", "5.1", "5.2", "5.3", "6.1", "6.2", "7.1" })
            {
                var r = Row(results, id);
                Assert.Equal(AccCheckStatus.Skipped, r.Status);
                Assert.Contains("not checked", r.Detail);
            }
            Assert.Contains("sign-in failed (2.1)", Row(results, "4.1").Detail);
            Assert.DoesNotContain(results, r => r.Status == AccCheckStatus.Pass && r.Id.CompareTo("2") > 0);
            // Only the token endpoint was called — nothing ran on a dead sign-in.
            Assert.All(_requests, r => Assert.EndsWith("/authentication/v2/token", r));
            Assert.True(AccSelfCheck.AnyFail(results));
        }

        [Fact]
        public async Task Unreachable_SignIn_IsReportedAsNetwork_NotAsARejectedToken()
        {
            _tokenStatus = 500;
            using var server = Serve();
            var results = await Run(MachineCreds(), Settings(GoodSettings()));
            var signIn = Row(results, "2.1");
            Assert.Equal(AccCheckStatus.Fail, signIn.Status);
            Assert.Contains("could not reach Autodesk", signIn.Detail);
            Assert.Contains("network", signIn.Remedy);
        }

        [Fact]
        public async Task ConfiguredProjectNotVisible_Fails_NamingTheProjectsThatAreVisible()
        {
            _projectsBody = new JObject
            {
                ["data"] = new JArray(
                    new JObject { ["id"] = "b.99999999-0000-0000-0000-000000000000", ["attributes"] = new JObject { ["name"] = "Other Job" } },
                    new JObject { ["id"] = "b.88888888-0000-0000-0000-000000000000", ["attributes"] = new JObject { ["name"] = "Training Sandbox" } }),
            }.ToString();
            using var server = Serve();
            var results = await Run(MachineCreds(), Settings(GoodSettings()));

            var p = Row(results, "3.1");
            Assert.Equal(AccCheckStatus.Fail, p.Status);
            Assert.Contains(Project, p.Detail);
            Assert.Contains("Other Job", p.Detail);
            Assert.Contains("Training Sandbox", p.Detail);
            foreach (var id in new[] { "3.2", "4.1", "4.2", "5.1", "6.1" })
                Assert.Equal(AccCheckStatus.Skipped, Row(results, id).Status);
            Assert.DoesNotContain(_requests, r => r.Contains("/construction/issues/"));
        }

        [Fact]
        public async Task ProjectIdMatches_InEitherIdForm()
        {
            // Settings hold the bare GUID; Data Management returns "b.": the same project.
            var s = GoodSettings();
            s["projectId"] = Bare;
            using var server = Serve();
            var results = await Run(MachineCreds(), Settings(s));
            Assert.Equal(AccCheckStatus.Pass, Row(results, "3.1").Status);
        }

        [Fact]
        public async Task MalformedSettings_FailsNamingTheBadKey()
        {
            var s = GoodSettings();
            s["coordModelSet"] = "typo";           // unknown key: the whole file is discarded
            using var server = Serve();
            var creds = MachineCreds();
            creds.ProjectId = Project;             // an OLD machine-file id: reported, never used
            var results = await Run(creds, Settings(s));

            var r = Row(results, "1.2");
            Assert.Equal(AccCheckStatus.Fail, r.Status);
            Assert.Contains("coordModelSet", r.Detail);
            Assert.Contains("discarded", r.Detail);
            // The discarded file configured the project, so there is none now.
            Assert.Equal(AccCheckStatus.Fail, Row(results, "1.3").Status);
            var legacy = Row(results, "1.4");
            Assert.Equal(AccCheckStatus.Warn, legacy.Status);
            Assert.Contains("old machine ids present", legacy.Detail);
            Assert.Contains(Project, legacy.Detail);
            Assert.Contains("Save", legacy.Remedy);
            // Nothing project-specific ran on the legacy id.
            Assert.Equal(AccCheckStatus.Skipped, Row(results, "3.1").Status);
            Assert.DoesNotContain(_requests, q => q.Contains(Bare));
            Assert.True(AccSelfCheck.AnyFail(results));
        }

        [Fact]
        public async Task MissingCustomAttributes_Warn_NamingThem_AndCreateNothing()
        {
            _attributeCount = 2;
            using var server = Serve();
            var results = await Run(MachineCreds(), Settings(GoodSettings()));

            var attrs = results.Where(r => r.Id.StartsWith("6.2.")).ToList();
            Assert.Equal(4, attrs.Count);
            Assert.All(attrs, a =>
            {
                Assert.Equal(AccCheckStatus.Warn, a.Status);
                // Exactly the four the folder lacks, in order - the two it has are not named.
                Assert.Contains("missing: " + string.Join(", ", AccDocsAttributeSet.All.Skip(2).Select(x => x.Name)) + ".", a.Detail);
                Assert.Contains("NOT written", a.Detail);
            });
            Assert.False(AccSelfCheck.AnyFail(results));
            AssertReadOnly();
        }

        [Fact]
        public async Task ClashTestNotCompleted_Warns_ItIsNotAClean()
        {
            _testStatus = "Processing";
            using var server = Serve();
            var results = await Run(MachineCreds(), Settings(GoodSettings()));
            var t = Row(results, "5.3");
            Assert.Equal(AccCheckStatus.Warn, t.Status);
            Assert.Contains("none completed", t.Detail);
        }

        [Fact]
        public async Task UnreachableFolder_FailsThatFolderOnly()
        {
            var s = GoodSettings();
            s["cdeFolders"]["ARCHIVE"] = "urn:adsk.wipprod:fs.folder:co.gone";
            using var server = Serve();
            var results = await Run(MachineCreds(), Settings(s));
            Assert.Equal(AccCheckStatus.Fail, Row(results, "6.1.4").Status);
            Assert.Contains("co.gone", Row(results, "6.1.4").Detail);
            Assert.Equal(AccCheckStatus.Pass, Row(results, "6.1.1").Status);
            Assert.Equal(3, results.Count(r => r.Id.StartsWith("6.2.")));  // attributes only on reachable folders
        }

        [Fact]
        public async Task NoClientId_FailsConfiguration_AndSkipsSignIn()
        {
            using var server = Serve();
            var creds = MachineCreds();
            creds.ClientId = "";
            var results = await Run(creds, Settings(GoodSettings()));
            Assert.Equal(AccCheckStatus.Fail, Row(results, "1.1").Status);
            Assert.Equal(AccCheckStatus.Skipped, Row(results, "2.1").Status);
            Assert.Equal(AccCheckStatus.Skipped, Row(results, "5.1").Status);
            Assert.Empty(_requests);
        }

        [Fact]
        public void Format_NamesEveryStatus_AndSaysReadOnly()
        {
            var rows = new List<AccCheckResult>
            {
                new AccCheckResult { Id = "1.1", Title = "a", Status = AccCheckStatus.Pass, Detail = "ok" },
                new AccCheckResult { Id = "2.1", Title = "b", Status = AccCheckStatus.Fail, Detail = "bad", Remedy = "fix" },
            };
            string text = AccSelfCheck.Format(rows, "hdr");
            Assert.Contains("[PASS", text);
            Assert.Contains("[FAIL", text);
            Assert.Contains("→ fix", text);
            Assert.Contains("NOT READY", text);
            Assert.Contains("Read-only", text);
        }
    }
}
