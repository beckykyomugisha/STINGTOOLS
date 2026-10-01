// ACC account data: the project record (-> Project Information), the member list (->
// assignee resolution + names), and the locations tree (-> STING LOC/ZONE/LVL check).
//
// The load-bearing assertions, as everywhere in this project:
//   * a failed read is never an empty result, and a 403 from the Admin API says "needs
//     Account/Project Admin" rather than "no members" / "no details";
//   * a partial (paginated) read that breaks is a failure, not a short list;
//   * an assignee that cannot be resolved to exactly ONE member/company/role is REFUSED -
//     never the first match, never a guess.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Acc.Tests.TestHelpers;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccAccountDataTests : IDisposable
    {
        public AccAccountDataTests() => AccProjectMembers.ClearCache();

        public void Dispose()
        {
            AccIssueSync.OverrideHostForTests(null);
            AccProjectMembers.ClearCache();
        }

        private static AccCredentials Creds(string hub = "b.hub-1") => new AccCredentials
        {
            ClientId = "c", ClientSecret = "s", RefreshToken = "r", AccessToken = "a",
            AccessTokenExpiry = DateTime.UtcNow.AddHours(1),
            ProjectId = "b.proj-1", HubId = hub,
        };

        private static LoopbackServer Serve(Func<int, System.Net.HttpListenerRequest, CannedResponse> h)
        {
            var s = new LoopbackServer(h);
            AccIssueSync.OverrideHostForTests(s.BaseUrl);
            return s;
        }

        private const string AdminProject = @"{""id"":""proj-1"",""name"":""Kampala Uganda Temple"",""jobNumber"":""KUT"",
            ""addressLine1"":""Plot 1"",""city"":""Kampala"",""country"":""UG"",""startDate"":""2026-01-05"",""endDate"":null,
            ""timezone"":""Africa/Nairobi"",""type"":""Religious Building"",""status"":""active"",
            ""projectValue"":{""value"":1500000,""currency"":""UGX""},""currentPhase"":""Construction""}";

        // ── Project details ──────────────────────────────────────────────────

        [Fact]
        public async Task ProjectDetails_AdminRecordIsParsed_WithBareProjectId()
        {
            using var s = Serve((_, __) => new CannedResponse(200, AdminProject));
            var r = await AccProjectDetailsClient.GetAsync(Creds());

            Assert.True(r.Succeeded);
            Assert.Equal("admin", r.Value.Source);
            Assert.False(r.Value.Partial);
            Assert.Equal("Kampala Uganda Temple", r.Value.Name);
            Assert.Equal("KUT", r.Value.JobNumber);
            Assert.Equal("Plot 1, Kampala, UG", r.Value.AddressBlock);
            Assert.Equal("UGX", r.Value.Currency);
            Assert.Equal("", r.Value.EndDate);                        // null in ACC is absent, not "null"
            Assert.Equal("/construction/admin/v1/projects/proj-1", s.Paths.Single());   // "b." stripped
        }

        [Fact]
        public async Task ProjectDetails_403_FallsBackToTheName_AndSaysWhy()
        {
            using var s = Serve((_, req) => req.Url.AbsolutePath.StartsWith("/construction/admin")
                ? new CannedResponse(403, "{}")
                : new CannedResponse(200, @"{""data"":{""id"":""b.proj-1"",""attributes"":{""name"":""KUT (DM)""}}}"));
            var r = await AccProjectDetailsClient.GetAsync(Creds());

            Assert.True(r.Succeeded);
            Assert.True(r.Value.Partial);
            Assert.Equal("KUT (DM)", r.Value.Name);
            Assert.Equal("", r.Value.JobNumber);                      // not read, NOT invented
            Assert.Contains("Admin", r.Value.Limitation);
            Assert.Contains("/project/v1/hubs/b.hub-1/projects/b.proj-1", s.Paths.Last());
        }

        [Fact]
        public async Task ProjectDetails_403_WithNoHub_IsAnAuthFailureNamingAdmin_NotEmptyDetails()
        {
            using var s = Serve((_, __) => new CannedResponse(403, "{}"));
            var r = await AccProjectDetailsClient.GetAsync(Creds(hub: ""));

            Assert.False(r.Succeeded);
            Assert.Equal(AccFetchStatus.AuthFailed, r.Status);
            Assert.Contains("Admin", r.Detail);
        }

        [Theory]
        [InlineData(404, AccFetchStatus.NotFound)]
        [InlineData(500, AccFetchStatus.TransportFailed)]
        public async Task ProjectDetails_OtherFailures_AreFailures(int status, AccFetchStatus expected)
        {
            using var s = Serve((_, __) => new CannedResponse(status, "{}"));
            var r = await AccProjectDetailsClient.GetAsync(Creds());
            Assert.False(r.Succeeded);
            Assert.Equal(expected, r.Status);
        }

        [Fact]
        public async Task ProjectDetails_A200WithNoIdOrName_IsASchemaFailure()
        {
            using var s = Serve((_, __) => new CannedResponse(200, @"{""error"":""x""}"));
            var r = await AccProjectDetailsClient.GetAsync(Creds());
            Assert.Equal(AccFetchStatus.TransportFailed, r.Status);
        }

        // ── Project Information diff (Revit-free) ────────────────────────────

        [Fact]
        public void Diff_MarksDiffering_Same_AccEmpty_AndMissingParameters()
        {
            var d = AccProjectDetailsClient.ParseAdmin(JObject.Parse(AdminProject));
            var model = new Dictionary<string, string>
            {
                ["PROJECT_NAME"] = "kampala  uganda temple",   // same, modulo case/space
                ["PROJECT_NUMBER"] = "0001",                   // differs
                ["PROJECT_ADDRESS"] = "Plot 1\r\nKampala\r\nUG",  // same, modulo line breaks
                ["PROJECT_STATUS"] = "",                       // differs (empty in Revit)
                ["PRJ_ORG_PROJECT_CODE_TXT"] = "KUT",          // same
                // currency parameter not bound in this model -> null
            };
            var rows = AccProjectInfoDiff.Build(d, (k, t) => model.TryGetValue(t, out var v) ? v : null,
                "PRJ_ORG_PROJECT_CODE_TXT", "PRJ_ORG_CURRENCY_TXT");
            AccInfoRowState St(string key) => rows.Single(r => r.Key == key).State;

            Assert.Equal(AccInfoRowState.Same, St("name"));
            Assert.Equal(AccInfoRowState.Differs, St("jobNumber"));
            Assert.Equal(AccInfoRowState.Same, St("address"));
            Assert.Equal(AccInfoRowState.Differs, St("status"));
            Assert.Equal(AccInfoRowState.Same, St("projectCode"));
            Assert.Equal(AccInfoRowState.NoTarget, St("currency"));   // not bound: never offered
            Assert.Equal(AccInfoRowState.NoTarget, St("startDate"));  // info only
            Assert.Equal(new[] { "jobNumber", "status" }, rows.Where(r => r.Writable).Select(r => r.Key).ToArray());
        }

        // P10: the ACC job number was offered as the ISO project code whatever it held.
        [Theory]
        [InlineData("KUT 2026/001")]
        [InlineData("PRJ-01")]
        [InlineData("K")]
        [InlineData("TOOLONGCODE")]
        public void Diff_AJobNumberThatIsNotAProjectCode_IsShownButNeverOfferedAsTheCode(string job)
        {
            var d = new AccProjectDetails { Name = "X", JobNumber = job };
            var rows = AccProjectInfoDiff.Build(d, (k, t) => "OLD", "P", "C");
            Assert.Equal(AccInfoRowState.NotACode, rows.Single(r => r.Key == "projectCode").State);
            Assert.False(rows.Single(r => r.Key == "projectCode").Writable);
            Assert.True(rows.Single(r => r.Key == "jobNumber").Writable);   // Revit's free-text number may take it
        }

        [Theory]
        [InlineData("KUT")]
        [InlineData("kut2026")]
        [InlineData("AB")]
        public void Diff_AJobNumberThatIsAProjectCode_IsOffered(string job)
        {
            var d = new AccProjectDetails { Name = "X", JobNumber = job };
            var rows = AccProjectInfoDiff.Build(d, (k, t) => "OLD", "P", "C");
            Assert.True(rows.Single(r => r.Key == "projectCode").Writable);
        }

        [Fact]
        public void Diff_AnAccValueThatIsEmpty_IsNeverOfferedAsAWrite()
        {
            var d = new AccProjectDetails { Name = "X" };             // e.g. the Data Management fallback
            var rows = AccProjectInfoDiff.Build(d, (k, t) => "something", "P", "C");
            Assert.Equal(AccInfoRowState.AccEmpty, rows.Single(r => r.Key == "jobNumber").State);
            Assert.DoesNotContain(rows, r => r.Writable && r.AccValue.Length == 0);
        }

        // ── Members: reading ─────────────────────────────────────────────────

        private static string UsersPage(IEnumerable<(string id, string adsk, string email, string name, string coId, string co, string roleId, string role, string status)> users, int total, int offset)
            => new JObject
            {
                ["pagination"] = new JObject { ["limit"] = 200, ["offset"] = offset, ["totalResults"] = total },
                ["results"] = new JArray(users.Select(u => new JObject
                {
                    ["id"] = u.id, ["autodeskId"] = u.adsk, ["email"] = u.email, ["name"] = u.name,
                    ["companyId"] = u.coId, ["companyName"] = u.co, ["status"] = u.status,
                    ["roles"] = new JArray(new JObject { ["id"] = u.roleId, ["name"] = u.role }),
                })),
            }.ToString();

        private static readonly (string, string, string, string, string, string, string, string, string)[] Team =
        {
            ("f-1", "ADSK1111", "davis@planscape.build", "Mayanja Davis", "co-1", "Planscape Consulting", "r-1", "BIM Manager", "active"),
            ("f-2", "ADSK2222", "eng@symbion.co.ug", "Sentongo", "co-2", "Symbion Consulting", "r-2", "MEP Engineer", "active"),
            ("f-3", "ADSK3333", "eng2@symbion.co.ug", "Sentongo", "co-2", "Symbion Consulting", "r-2", "MEP Engineer", "active"),
            ("f-4", "", "new@x.com", "Pending Person", "co-2", "Symbion Consulting", "r-2", "MEP Engineer", "pending"),
        };

        [Fact]
        public async Task Members_AllPagesAreRead_AndCompaniesAndRolesAreDerived()
        {
            using var s = Serve((i, req) =>
            {
                bool second = req.Url.Query.Contains("offset=2");
                return new CannedResponse(200, UsersPage(second ? Team.Skip(2) : Team.Take(2), 4, second ? 2 : 0));
            });
            var r = await AccProjectMembers.GetDirectoryAsync(Creds());

            Assert.True(r.Succeeded);
            Assert.Equal(4, r.Value.Users.Count);
            Assert.Equal(2, s.RequestCount);
            Assert.Equal(2, r.Value.Companies.Count);
            Assert.Equal(2, r.Value.Roles.Count);
            Assert.Contains("/construction/admin/v1/projects/proj-1/users", s.Paths[0]);
        }

        [Fact]
        public async Task Members_APageThatFails_FailsTheWholeRead()
        {
            using var s = Serve((i, req) => req.Url.Query.Contains("offset=2")
                ? new CannedResponse(500, "{}")
                : new CannedResponse(200, UsersPage(Team.Take(2), 4, 0)));
            var r = await AccProjectMembers.GetDirectoryAsync(Creds());
            Assert.False(r.Succeeded);
            Assert.Contains("INCOMPLETE", r.Detail);
        }

        [Fact]
        public async Task Members_403_IsReportedAsNeedingAdmin_NotAsNoMembers()
        {
            using var s = Serve((_, __) => new CannedResponse(403, "{}"));
            var r = await AccProjectMembers.GetDirectoryAsync(Creds());
            Assert.False(r.Succeeded);
            Assert.Equal(AccFetchStatus.AuthFailed, r.Status);
            Assert.Equal(403, r.HttpStatus);
            Assert.Contains("Admin", r.Detail);
            Assert.Empty(r.Value.Users);
        }

        [Fact]
        public async Task Members_ASuccessfulReadIsCached_AFailureIsNot()
        {
            int n = 0;
            using var s = Serve((_, __) => ++n == 1 ? new CannedResponse(500, "{}") : new CannedResponse(200, UsersPage(Team, 4, 0)));
            Assert.False((await AccProjectMembers.GetDirectoryAsync(Creds())).Succeeded);
            Assert.True((await AccProjectMembers.GetDirectoryAsync(Creds())).Succeeded);
            Assert.True((await AccProjectMembers.GetDirectoryAsync(Creds())).Succeeded);
            Assert.Equal(2, s.RequestCount);                      // third call served from the cache
        }

        // ── Members: resolution ──────────────────────────────────────────────

        private static AccProjectDirectory Dir()
            => AccProjectDirectory.From(Team.Select(u => new AccProjectUser
            {
                Id = u.Item1, AutodeskId = u.Item2, Email = u.Item3, Name = u.Item4, CompanyId = u.Item5,
                CompanyName = u.Item6, Status = u.Item9,
                Roles = new List<AccNamedId> { new AccNamedId { Id = u.Item7, Name = u.Item8 } },
            }).ToList());

        [Fact]
        public void Resolve_Email_GivesTheAutodeskId_NotTheFormaId()
        {
            var r = AccProjectMembers.Resolve("DAVIS@planscape.build", "", Dir());
            Assert.True(r.Ok);
            Assert.Equal("ADSK1111", r.Id);                       // Issues' assignedTo wants the Autodesk ID
            Assert.Equal("user", r.Type);
            Assert.Equal("Mayanja Davis", r.DisplayName);
        }

        [Fact]
        public void Resolve_RoleAndCompanyNames()
        {
            var role = AccProjectMembers.Resolve("bim manager", "role", Dir());
            Assert.True(role.Ok); Assert.Equal("r-1", role.Id);
            var co = AccProjectMembers.Resolve("Symbion Consulting", "company", Dir());
            Assert.True(co.Ok); Assert.Equal("co-2", co.Id);
        }

        [Fact]
        public void Resolve_AnAmbiguousName_IsRefused_NeverTheFirstMatch()
        {
            var r = AccProjectMembers.Resolve("Sentongo", "user", Dir());
            Assert.False(r.Ok);
            Assert.Contains("ambiguous", r.Reason);
            Assert.Equal("", r.Id);
        }

        [Fact]
        public void Resolve_UnknownOrUnassignable_IsRefused()
        {
            Assert.False(AccProjectMembers.Resolve("nobody@x.com", "user", Dir()).Ok);
            Assert.False(AccProjectMembers.Resolve("Quantity Surveyor", "role", Dir()).Ok);
            var pending = AccProjectMembers.Resolve("new@x.com", "user", Dir());
            Assert.False(pending.Ok);
            Assert.Contains("Autodesk ID", pending.Reason);
            Assert.False(AccProjectMembers.Resolve("a@b.com", "role", Dir()).Ok);       // an email is a user
        }

        [Fact]
        public void Resolve_AFormaIdIsTranslated_AnUnknownIdIsRefused()
        {
            Assert.Equal("ADSK2222", AccProjectMembers.Resolve("f-2", "user", Dir()).Id);
            Assert.False(AccProjectMembers.Resolve("ZZZZ9999", "user", Dir()).Ok);
        }

        [Fact]
        public void Resolve_WithNoMemberList_AnIdIsSentUnverified_ButANameOrEmailIsRefused()
        {
            var id = AccProjectMembers.Resolve("ADSK1111", "user", null, "HTTP 403");
            Assert.True(id.Ok);
            Assert.True(id.Unverified);
            Assert.Equal("ADSK1111", id.Id);

            var email = AccProjectMembers.Resolve("davis@planscape.build", "user", null, "HTTP 403");
            Assert.False(email.Ok);
            Assert.Contains("HTTP 403", email.Reason);
            Assert.False(AccProjectMembers.Resolve("BIM Manager", "role", null, "HTTP 403").Ok);
        }

        [Fact]
        public void Directory_NameFor_ByType()
        {
            Assert.Equal("Mayanja Davis", Dir().NameFor("ADSK1111", "user"));
            Assert.Equal("MEP Engineer", Dir().NameFor("r-2", "role"));
            Assert.Equal("", Dir().NameFor("unknown", "user"));
        }

        [Fact]
        public async Task ResolveAsync_ReadsTheMembersOverHttp()
        {
            using var s = Serve((_, __) => new CannedResponse(200, UsersPage(Team, 4, 0)));
            var r = await AccProjectMembers.ResolveAsync(Creds(), "eng@symbion.co.ug", "user");
            Assert.True(r.Ok);
            Assert.Equal("ADSK2222", r.Id);
        }

        // ── Policy: an email may omit the type; nothing else may ─────────────

        [Theory]
        [InlineData("{\"escalateAssignedTo\":\"davis@planscape.build\"}", true, "user")]
        [InlineData("{\"escalateAssignedTo\":\"BIM Manager\",\"escalateAssignedToType\":\"role\"}", true, "role")]
        [InlineData("{\"escalateAssignedTo\":\"BIM Manager\"}", false, "")]
        [InlineData("{\"escalateAssignedTo\":\"davis@planscape.build\",\"escalateAssignedToType\":\"company\"}", false, "")]
        public void Policy_AssigneeForms(string json, bool ok, string type)
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "acc-assignee-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                System.IO.File.WriteAllText(path, json);
                var p = AccOperatingPolicy.Load(path);
                Assert.Equal(ok ? AccPolicySource.Loaded : AccPolicySource.Malformed, p.Source);
                if (ok) Assert.Equal(type, p.EscalateAssignedToType);
            }
            finally { try { System.IO.File.Delete(path); } catch (Exception) { } }
        }

        // ── Import: names travel with ids ────────────────────────────────────

        [Fact]
        public void Import_StoresAssigneeIdTypeAndName_AndClearsAStaleName()
        {
            var rows = new JArray();
            var writer = new JArrayAccIssueWriter(rows, DateTime.Now, "u");
            var rec = new AccIssueImportRecord { Id = "acc-1", Title = "T", Status = "open", AssignedTo = "ADSK1111", AssignedToType = "user", AssignedToName = "Mayanja Davis" };
            AccIssueImport.Merge(rows, new[] { rec }, null, writer, DateTime.Now, "u");
            var row = (JObject)rows.Single();
            Assert.Equal("ADSK1111", (string)row["assigned_to"]);                     // the merged field keeps the id
            Assert.Equal("ADSK1111", (string)row[AccIssueImport.AssignedIdField]);
            Assert.Equal("user", (string)row[AccIssueImport.AssignedTypeField]);
            Assert.Equal("Mayanja Davis", (string)row[AccIssueImport.AssignedNameField]);

            // Reassigned in ACC; this run could not look names up. The old name must not sit
            // beside the new id.
            var moved = new AccIssueImportRecord { Id = "acc-1", Title = "T", Status = "open", AssignedTo = "ADSK2222", AssignedToType = "user" };
            AccIssueImport.Merge(rows, new[] { moved }, null, writer, DateTime.Now, "u");
            Assert.Equal("ADSK2222", (string)row[AccIssueImport.AssignedIdField]);
            Assert.Null(row[AccIssueImport.AssignedNameField]);
        }

        // ── Locations ────────────────────────────────────────────────────────

        private const string Tree = @"{""pagination"":{""limit"":10000,""offset"":0,""totalResults"":6},""results"":[
            {""id"":""root"",""parentId"":null,""type"":""Root"",""name"":""Project"",""barcode"":null,""order"":0},
            {""id"":""b1"",""parentId"":""root"",""type"":""Area"",""name"":""BLD1"",""barcode"":null,""order"":0},
            {""id"":""l2"",""parentId"":""b1"",""type"":""Area"",""name"":""Second Floor"",""barcode"":""L02"",""order"":0},
            {""id"":""gf"",""parentId"":""b1"",""type"":""Area"",""name"":""Level 1"",""barcode"":null,""order"":1},
            {""id"":""z1"",""parentId"":""gf"",""type"":""Area"",""name"":""Zone Z01 East"",""barcode"":null,""order"":0},
            {""id"":""x"",""parentId"":""b1"",""type"":""Area"",""name"":""Plant Yard"",""barcode"":null,""order"":2}]}";

        [Fact]
        public async Task Locations_TreeIsReadWithPaths()
        {
            using var s = Serve((_, __) => new CannedResponse(200, Tree));
            var r = await AccLocationsClient.GetTreeAsync(Creds());
            Assert.True(r.Succeeded);
            Assert.Equal(6, r.Value.Count);
            Assert.Equal("Project > BLD1 > Level 1 > Zone Z01 East", r.Value.Single(n => n.Id == "z1").Path);
            Assert.Contains("/construction/locations/v2/projects/proj-1/trees/default/nodes", s.Paths[0]);
        }

        [Fact]
        public async Task Locations_Paginated_ReadsEveryPage()
        {
            var nodes = JObject.Parse(Tree)["results"].ToList();
            using var s = Serve((_, req) =>
            {
                int off = req.Url.Query.Contains("offset=3") ? 3 : 0;
                return new CannedResponse(200, new JObject
                {
                    ["pagination"] = new JObject { ["totalResults"] = 6, ["offset"] = off, ["limit"] = 3 },
                    ["results"] = new JArray(nodes.Skip(off).Take(3)),
                }.ToString());
            });
            var r = await AccLocationsClient.GetTreeAsync(Creds(), pageSize: 3);
            Assert.True(r.Succeeded);
            Assert.Equal(6, r.Value.Count);
            Assert.Equal(2, s.RequestCount);
        }

        [Fact]
        public async Task Locations_OnlyARoot_IsEmptyOk_AndA403IsAFailure()
        {
            using (var s = Serve((_, __) => new CannedResponse(200,
                @"{""pagination"":{""totalResults"":1},""results"":[{""id"":""root"",""parentId"":null,""type"":""Root"",""name"":""Project""}]}")))
            {
                var r = await AccLocationsClient.GetTreeAsync(Creds());
                Assert.Equal(AccFetchStatus.EmptyOk, r.Status);
            }
            using (var s = Serve((_, __) => new CannedResponse(403, "{}")))
            {
                var r = await AccLocationsClient.GetTreeAsync(Creds());
                Assert.False(r.Succeeded);
                Assert.Equal(AccFetchStatus.AuthFailed, r.Status);
                Assert.Contains("403", r.Detail);
            }
        }

        [Fact]
        public void LocationCheck_FindsMatchesGapsAndNameMismatches()
        {
            var nodes = JObject.Parse(Tree)["results"].Select(t => new AccLocationNode
            {
                Id = (string)t["id"], ParentId = (string)t["parentId"] ?? "", Type = (string)t["type"],
                Name = (string)t["name"], Barcode = (string)t["barcode"] ?? "",
            }).ToList();
            AccLocationsClient.ComputePaths(nodes);
            var codes = new List<StingSpatialCode>
            {
                new StingSpatialCode { Kind = "LOC", Code = "BLD1" },
                new StingSpatialCode { Kind = "LOC", Code = "BLD2" },
                new StingSpatialCode { Kind = "ZONE", Code = "Z01" },
                new StingSpatialCode { Kind = "ZONE", Code = "XX" },                       // placeholder: ignored
                new StingSpatialCode { Kind = "LVL", Code = "L01", SourceName = "Level 1" },
                new StingSpatialCode { Kind = "LVL", Code = "L02", SourceName = "Level 2" },
                new StingSpatialCode { Kind = "LVL", Code = "L03", SourceName = "Level 3" },
            };
            // Stand-in for SpatialNameCodes.LevelCodeFromName (the command passes the real one).
            string Lvl(string n) => n.StartsWith("Level ") ? "L" + n.Substring(6).PadLeft(2, '0') : "XX";

            var r = AccLocationCheck.Compare(nodes, codes, Lvl);

            Assert.Equal(5, r.NodesChecked);                                             // root excluded
            Assert.Equal(new[] { "Plant Yard" }, r.NodesWithoutCode.Select(n => n.Name).ToArray());
            Assert.Equal(new[] { "LOC BLD2", "LVL L03" }, r.CodesWithoutNode.Select(c => c.Kind + " " + c.Code).OrderBy(x => x).ToArray());
            Assert.Contains(r.Matches, m => m.Node.Id == "z1" && m.Code.Code == "Z01" && m.How == "token");
            Assert.Contains(r.Matches, m => m.Node.Id == "gf" && m.Code.Code == "L01" && m.How == "level-name");
            // "Second Floor" carries L02 only as a barcode, and is not what the model calls it.
            var mm = Assert.Single(r.NameMismatches);
            Assert.Equal("l2", mm.Node.Id);
            Assert.Contains("Level 2", mm.Detail);
        }

        [Fact]
        public void LocationCheck_TokenMatchIsWholeTokenOnly()
        {
            Assert.True(AccLocationCheck.HasToken("Zone Z01 East", "Z01"));
            Assert.False(AccLocationCheck.HasToken("Zone Z012", "Z01"));
            Assert.False(AccLocationCheck.HasToken("BLD10", "BLD1"));
        }
    }
}
