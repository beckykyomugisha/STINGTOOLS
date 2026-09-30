// ACC-HARD-5: an escalated clash issue carries what its assignee needs to FIND the objects,
// because ACC's Issues API cannot pin one. Driven over a real loopback listener against the
// real clients (AccModelDerivative, AccDocsWebView, AccIssueLocator, AccIssueAttachment),
// plus the Revit-free half of the planscape://revit/select protocol.
//
// NOT covered here (Revit-bound, verified by build only): PlanscapeRevitSelect - the Idling
// handler that resolves the UniqueIds with Document.GetElement, selects and zooms.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Acc.Tests.TestHelpers;
using StingTools.Core;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccIssueLocateTests : IDisposable
    {
        private const string Container = "11111111-2222-3333-4444-555555555555";
        private const string ModelSet = "ms-kut";
        private const string UrnArch = "urn:adsk.wipprod:fs.file:vf.ARCH?version=3";
        private const string UrnMep = "urn:adsk.wipprod:fs.file:vf.MEP?version=5";
        private const string BubbleArch = "urn:adsk.wipprod:fs.file:vf.ARCHBUBBLE?version=3";
        private const string UidA = "d85573c2-f8d5-46ae-966a-ac82fa18f500-00066187";
        private const string UidB = "e3e052f9-0156-11d5-9301-0000863f27ad-00000153";
        private const string ViewerArch = "https://acc.autodesk.com/docs/files/projects/p1?folderUrn=f&entityId=ARCH";

        public AccIssueLocateTests()
        {
            AccModelCoordSync.DelayHook = _ => Task.CompletedTask;
            AccModelDerivative.ClearCache();
            AccDocsWebView.ClearCache();
            AccIssueAttachment.ClearCache();
        }

        public void Dispose()
        {
            AccModelCoordSync.OverrideHostForTests(null);
            AccModelCoordSync.DelayHook = t => Task.Delay(t);
            AccModelDerivative.ClearCache();
            AccDocsWebView.ClearCache();
            AccIssueAttachment.ClearCache();
        }

        private static AccCredentials Creds() => new AccCredentials
        {
            ClientId = "c", ClientSecret = "s", RefreshToken = "r",
            ProjectId = "b." + Container, HubId = "b.hub-1",
            AccessToken = "tok", AccessTokenExpiry = DateTime.UtcNow.AddHours(1),
            IssueSubtypeId = "sub-1",
        };

        private static AccClashRecord Clash() => new AccClashRecord
        {
            Id = "c1", LeftDocument = "KUT_ARCH.rvt", RightDocument = "KUT_MEP.rvt",
            LeftObjectId = 17299, RightObjectId = 3403, DocumentsNamed = true,
            LeftDocumentUrn = UrnArch, RightDocumentUrn = UrnMep, ModelSetVersion = 7,
        };

        private static string ReadBody(HttpListenerRequest req)
        {
            using var sr = new StreamReader(req.InputStream, Encoding.UTF8);
            return sr.ReadToEnd();
        }

        /// <summary>A model-coordination + MD + DM fake. <paramref name="mepMd"/> is the status
        /// Model Derivative answers for the MEP model (200 = resolves nothing, 404 = missing).</summary>
        private static LoopbackServer Fake(int mepMd, List<string> mdBodies, string archExternalId = UidA)
        {
            return new LoopbackServer((_, req) =>
            {
                string path = req.Url.AbsolutePath;
                if (path.EndsWith("/modelsets/" + ModelSet + "/versions/7"))
                    return new CannedResponse(200, new JObject
                    {
                        ["documentVersions"] = new JArray(
                            new JObject { ["versionUrn"] = UrnArch, ["bubbleUrn"] = BubbleArch, ["viewableGuid"] = "view-arch", ["displayName"] = "KUT_ARCH.rvt" },
                            new JObject { ["versionUrn"] = UrnMep, ["bubbleUrn"] = UrnMep, ["viewableGuid"] = "view-mep", ["displayName"] = "KUT_MEP.rvt" })
                    }.ToString());

                string archB64 = AccModelDerivative.EncodeUrn(BubbleArch);
                string mepB64 = AccModelDerivative.EncodeUrn(UrnMep);
                if (path.Contains("/designdata/" + mepB64 + "/"))
                    return mepMd == 200
                        ? new CannedResponse(200, path.EndsWith("/metadata")
                            ? "{\"data\":{\"metadata\":[{\"role\":\"3d\",\"guid\":\"view-mep\"}]}}"
                            : "{\"data\":{\"collection\":[]}}")
                        : new CannedResponse(mepMd, "{\"diagnostic\":\"no\"}");
                if (path == "/modelderivative/v2/designdata/" + archB64 + "/metadata")
                    return new CannedResponse(200,
                        "{\"data\":{\"metadata\":[{\"role\":\"3d\",\"guid\":\"other\",\"isMasterView\":true},{\"role\":\"3d\",\"guid\":\"view-arch\"},{\"role\":\"2d\",\"guid\":\"sheet\"}]}}");
                if (path == "/modelderivative/v2/designdata/" + archB64 + "/metadata/view-arch/properties:query")
                {
                    mdBodies?.Add(ReadBody(req));
                    return new CannedResponse(200, "{\"data\":{\"collection\":[{\"objectid\":17299,\"name\":\"Wall\",\"externalId\":\"" + archExternalId + "\"}," +
                                                   "{\"objectid\":999,\"externalId\":\"" + UidB + "\"}]}}");
                }
                if (path.Contains("/data/v1/projects/") && path.Contains("/versions/"))
                    return req.Url.OriginalString.Contains("ARCH")
                        ? new CannedResponse(200, "{\"data\":{\"links\":{\"webView\":{\"href\":\"" + ViewerArch + "\"}}}}")
                        : new CannedResponse(404, "{}");
                return new CannedResponse(404, "{\"unexpected\":\"" + path + "\"}");
            });
        }

        // ── dbId → externalId ────────────────────────────────────────────────

        [Fact]
        public async Task Locate_ResolvesOneSide_AndNamesWhyNotTheOther()
        {
            var bodies = new List<string>();
            using var server = Fake(mepMd: 200, bodies);
            AccModelCoordSync.OverrideHostForTests(server.BaseUrl);

            var summary = new AccLocateSummary();
            var map = await AccIssueLocator.LocateAsync(Creds(), Container, ModelSet, new[] { Clash() }, true, true, summary);

            var loc = map["c1"];
            Assert.Equal(UidA, loc.Left.UniqueId);                         // resolved through the BUBBLE urn + the model set's view guid
            Assert.False(loc.Right.Resolved);
            Assert.Contains("not in the model's property index", loc.Right.Reason);
            Assert.Equal(2, summary.Sides);
            Assert.Equal(1, summary.Resolved);
            Assert.Equal(ViewerArch, loc.Left.ViewerUrl);                  // the API's own webView.href
            Assert.Equal("", loc.Right.ViewerUrl);                         // 404 → no link, not a guessed one

            // The query asked for exactly the clash's object and only the two fields needed.
            var q = JObject.Parse(Assert.Single(bodies));
            Assert.Equal(new JArray("objectid", 17299).ToString(), q["query"]["$in"].ToString());
            Assert.Equal(new JArray("objectid", "externalId").ToString(), q["fields"].ToString());
            // An object the server returned but we did not ask for (999) is not smuggled in.
        }

        [Fact]
        public async Task Locate_ModelDerivative404_GivesNoLink_AndTheIssueIsStillPushed()
        {
            string pushedBody = null;
            using var md = Fake(mepMd: 404, null);
            AccModelCoordSync.OverrideHostForTests(md.BaseUrl);
            var summary = new AccLocateSummary();
            var map = await AccIssueLocator.LocateAsync(Creds(), Container, ModelSet, new[] { Clash() }, true, false, summary);
            var loc = map["c1"];

            Assert.False(loc.Right.Resolved);
            Assert.Contains("Model Derivative", loc.Right.Reason);
            Assert.Contains(summary.Reasons, r => r.Contains("Model Derivative"));

            // The issue body built from that location: the Arch link is there, the MEP side
            // says why it has none, and no link names the MEP object.
            var lines = AccIssueLinks.BuildLinkLines(loc, deepLinks: true, viewerLinks: false);
            string desc = AccIssueLinks.ComposeDescription("STING clash triage — score 0.90.", lines);
            Assert.Contains("uid=" + UidA, desc);
            Assert.DoesNotContain("3403&", desc);
            Assert.Contains("No Revit link for KUT_MEP object 3403", desc);

            // …and the push itself is unaffected by the locator failing.
            using var issues = new LoopbackServer((_, req) =>
            {
                if (req.HttpMethod == "POST" && req.Url.AbsolutePath.EndsWith("/issues"))
                {
                    pushedBody = ReadBody(req);
                    return new CannedResponse(201, "{\"id\":\"issue-1\"}");
                }
                return new CannedResponse(404, "{}");
            });
            AccModelCoordSync.OverrideHostForTests(issues.BaseUrl);
            var r = await AccIssueSync.PushIssueDetailedAsync(Creds(), new AccIssue { Title = "Clash", Description = desc });
            Assert.True(r.Ok);
            Assert.Equal("issue-1", r.Id);
            Assert.Contains("planscape://revit/select", (string)JObject.Parse(pushedBody)["description"]);
        }

        [Fact]
        public async Task Locate_ExternalIdThatIsNotARevitUniqueId_GivesNoLink()
        {
            using var server = Fake(mepMd: 200, null, archExternalId: "ifc-3vB2YO$MX4xv5uCqZZG05x");
            AccModelCoordSync.OverrideHostForTests(server.BaseUrl);
            var map = await AccIssueLocator.LocateAsync(Creds(), Container, ModelSet, new[] { Clash() }, true, false, new AccLocateSummary());
            Assert.False(map["c1"].Left.Resolved);
            Assert.Contains("not a Revit element", map["c1"].Left.Reason);
            Assert.DoesNotContain(AccIssueLinks.BuildLinkLines(map["c1"], true, false), l => l.Contains("planscape://"));
        }

        [Fact]
        public async Task ModelDerivative_202StillProcessing_IsAFailure_NotAnEmptyMatch()
        {
            using var server = LoopbackServer.Always(202, "{\"result\":\"processing\"}");
            AccModelCoordSync.OverrideHostForTests(server.BaseUrl);
            var r = await AccModelDerivative.ResolveExternalIdsAsync(Creds(), UrnArch, "", new long[] { 1 });
            Assert.False(r.Succeeded);
            Assert.Contains("202", r.Detail);
        }

        [Fact]
        public async Task ModelDerivative_IsCachedPerDocumentVersion()
        {
            using var server = Fake(mepMd: 200, null);
            AccModelCoordSync.OverrideHostForTests(server.BaseUrl);
            var first = await AccModelDerivative.ResolveExternalIdsAsync(Creds(), BubbleArch, "view-arch", new long[] { 17299 });
            int after = server.RequestCount;
            var second = await AccModelDerivative.ResolveExternalIdsAsync(Creds(), BubbleArch, "view-arch", new long[] { 17299 });
            Assert.Equal(UidA, first.Value[17299]);
            Assert.Equal(UidA, second.Value[17299]);
            Assert.Equal(after, server.RequestCount);                     // no second round trip
        }

        [Fact]
        public void EncodeUrn_IsUrlSafeUnpaddedBase64()
        {
            string e = AccModelDerivative.EncodeUrn("urn:adsk.wipprod:fs.file:vf.b909RzMKR4mhc3O7UBY_8g?version=2");
            Assert.DoesNotContain("=", e);
            Assert.DoesNotContain("+", e);
            Assert.DoesNotContain("/", e);
            string padded = e.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            Assert.Equal("urn:adsk.wipprod:fs.file:vf.b909RzMKR4mhc3O7UBY_8g?version=2", Encoding.UTF8.GetString(Convert.FromBase64String(padded)));
        }

        // ── Link format + length cap ─────────────────────────────────────────

        [Fact]
        public void SelectLink_HasTheDocumentedShape_AndRoundTrips()
        {
            string link = PlanscapeProtocol.BuildRevitSelectLink("KUT ARCH.rvt", new[] { UidA, UidB, UidA }, out int n);
            Assert.Equal($"planscape://revit/select?doc=KUT%20ARCH.rvt&uid={UidA},{UidB}", link);
            Assert.Equal(2, n);

            var parsed = PlanscapeProtocol.Parse(link);
            Assert.True(PlanscapeProtocol.TryParseRevitSelect(parsed, out string doc, out var uids, out int omitted));
            Assert.Equal("KUT ARCH.rvt", doc);
            Assert.Equal(new[] { UidA, UidB }, uids);
            Assert.Equal(0, omitted);
        }

        [Fact]
        public void SelectLink_TooManyIds_KeepsWholeIds_AndSaysHowManyWereLeftOut()
        {
            var ids = Enumerable.Range(1, 20).Select(i => $"d85573c2-f8d5-46ae-966a-ac82fa18f500-{i:x8}").ToList();
            string link = PlanscapeProtocol.BuildRevitSelectLink("M.rvt", ids, out int n, maxLength: 400);
            Assert.True(link.Length <= 400);
            Assert.InRange(n, 1, 19);
            Assert.EndsWith("&more=" + (20 - n), link);
            Assert.True(PlanscapeProtocol.TryParseRevitSelect(PlanscapeProtocol.Parse(link), out _, out var uids, out int omitted));
            Assert.Equal(ids.Take(n), uids);                               // every id kept is whole
            Assert.Equal(20 - n, omitted);
        }

        [Fact]
        public void SelectLink_NothingToSelect_OrNothingFits_IsNull()
        {
            Assert.Null(PlanscapeProtocol.BuildRevitSelectLink("M", new string[0], out _));
            Assert.Null(PlanscapeProtocol.BuildRevitSelectLink("M", new[] { UidA }, out int n, maxLength: 30));
            Assert.Equal(0, n);
        }

        [Fact]
        public void Description_StaysInsideAccsLimit_AndNeverCutsALink()
        {
            string longBase = new string('x', 2500);
            string l1 = "Select in Revit (A): planscape://revit/select?doc=A.rvt&uid=" + UidA;
            string l2 = "Open A in ACC: " + ViewerArch + new string('q', 300);
            string huge = "Open B in ACC: https://acc.autodesk.com/" + new string('z', 900);
            string d = AccIssueLinks.ComposeDescription(longBase, new[] { l1, huge, l2 });
            Assert.True(d.Length <= AccIssueLinks.DescriptionLimit, $"length {d.Length}");
            Assert.Contains(l1, d);                                        // whole
            Assert.Contains(l2, d);                                        // whole
            Assert.DoesNotContain("zzzz", d);                              // dropped whole, not truncated
            Assert.StartsWith("xxx", d);                                   // the base text shrank instead
        }

        [Fact]
        public void Parse_OldPathLinks_StillParse_WithNoQuery()
        {
            var i = PlanscapeProtocol.Parse("planscape://issue/abc-123");
            Assert.Equal("issue", i.Kind);
            Assert.Equal("abc-123", i.Target);
            Assert.Empty(i.Query);
            var d = PlanscapeProtocol.Parse("planscape://dashboard/Kampala Uganda Temple/20260101-0900");
            Assert.Equal("Kampala Uganda Temple", d.Target);
            Assert.False(PlanscapeProtocol.TryParseRevitSelect(i, out _, out _, out _));
            Assert.False(PlanscapeProtocol.TryParseRevitSelect(PlanscapeProtocol.Parse("planscape://revit/select?doc=A"), out _, out _, out _));
        }

        [Theory]
        [InlineData("KUT_ARCH.rvt", "KUT_ARCH", true)]
        [InlineData("KUT_ARCH.rvt", "KUT_ARCH_jdoe", true)]          // workshared local copy
        [InlineData("KUT_ARCH.rvt", "KUT_ARCHIVE", false)]
        [InlineData("KUT_ARCH.rvt", "KUT_MEP", false)]
        [InlineData("", "KUT_MEP", false)]
        public void ModelNameMatches_KnowsLocalCopies(string link, string title, bool expected)
            => Assert.Equal(expected, PlanscapeProtocol.ModelNameMatches(link, title));

        // ── IFC GUID from a Revit UniqueId (for the BCF components) ─────────

        [Fact]
        public void IfcGuid_ExportIdIsEpisodeXorElementId_ThenCompressed()
        {
            // 0x7b73f3da ^ 0x0001fd0b = 0x7b720ed1
            Assert.Equal("60f91daf-3dd7-4283-a86d-24137b720ed1",
                RevitIfcGuid.ExportGuidFromUniqueId("60f91daf-3dd7-4283-a86d-24137b73f3da-0001fd0b"));
            Assert.Equal("0000000000000000000000", RevitIfcGuid.Compress("00000000-0000-0000-0000-000000000000"));
            Assert.Equal("3$$$$$$$$$$$$$$$$$$$$$", RevitIfcGuid.Compress("ffffffff-ffff-ffff-ffff-ffffffffffff"));
            Assert.Null(RevitIfcGuid.FromUniqueId("not-a-uid"));
            Assert.Equal(22, RevitIfcGuid.FromUniqueId(UidA).Length);
        }

        // ── BCF attachment ───────────────────────────────────────────────────

        [Fact]
        public void Bcf_HasTheTwoElementsAsComponents_AReferencedViewpoint_AndNoInventedCamera()
        {
            byte[] bytes = AccClashBcf.Build("sig-1", "Clash A ↔ B", "desc", "planscape://revit/select?uid=" + UidA,
                new[] { new AccClashBcf.Element { UniqueId = UidA, DocumentName = "A.rvt" },
                        new AccClashBcf.Element { UniqueId = UidB, DocumentName = "B.rvt" } });
            using var zip = new ZipArchive(new MemoryStream(bytes));
            string topic = AccClashBcf.StableGuid("sig-1");
            Assert.NotNull(zip.GetEntry("bcf.version"));
            var markup = XDocument.Load(zip.GetEntry($"{topic}/markup.bcf").Open());
            var vp = XDocument.Load(zip.GetEntry($"{topic}/viewpoint.bcfv").Open());

            var vpRef = markup.Root.Element("Viewpoints");
            Assert.NotNull(vpRef);                                         // markup points at its viewpoint
            Assert.Equal(vp.Root.Attribute("Guid").Value, vpRef.Attribute("Guid").Value);
            Assert.Equal("viewpoint.bcfv", vpRef.Element("Viewpoint").Value);
            Assert.Equal("Clash", markup.Root.Element("Topic").Attribute("TopicType").Value);

            var comps = vp.Descendants("Component").ToList();
            Assert.Equal(2, comps.Count);
            Assert.Equal(UidA, comps[0].Element("AuthoringToolId").Value);
            Assert.Equal(RevitIfcGuid.FromUniqueId(UidA), comps[0].Attribute("IfcGuid").Value);
            Assert.Empty(vp.Descendants("PerspectiveCamera"));
            Assert.Empty(vp.Descendants("OrthogonalCamera"));
            Assert.Equal(topic, AccClashBcf.StableGuid("sig-1"));          // same clash, same topic
        }

        [Fact]
        public void Bcf_NoResolvedElement_IsNotBuilt()
            => Assert.Null(AccClashBcf.Build("s", "t", "d", "", new[] { new AccClashBcf.Element { UniqueId = "" } }));

        [Fact]
        public async Task Attachment_FollowsTheDocumentedSixSteps_InOrder()
        {
            string attachBody = null, putBody = null;
            LoopbackServer server = null;
            server = new LoopbackServer((_, req) =>
            {
                string p = req.Url.AbsolutePath;
                if (req.HttpMethod == "GET" && p.EndsWith("/topFolders"))
                    return new CannedResponse(200, "{\"data\":[{\"id\":\"urn:adsk.wipprod:fs.folder:co.PF\",\"attributes\":{\"name\":\"Project Files\"}," +
                                                   "\"relationships\":{\"parent\":{\"data\":{\"type\":\"folders\",\"id\":\"urn:adsk.wip:fs.folder:co.ROOT\"}}}}]}");
                if (req.HttpMethod == "POST" && p.EndsWith("/storage"))
                {
                    var b = JObject.Parse(ReadBody(req));
                    Assert.Equal("urn:adsk.wip:fs.folder:co.ROOT", (string)b["data"]["relationships"]["target"]["data"]["id"]);
                    return new CannedResponse(201, "{\"data\":{\"type\":\"objects\",\"id\":\"urn:adsk.objects:os.object:wip.dm.prod/a9d3-obj.bcfzip\"}}");
                }
                if (req.HttpMethod == "GET" && p.EndsWith("/signeds3upload"))
                    return new CannedResponse(200, "{\"uploadKey\":\"UK\",\"urls\":[\"" + server.BaseUrl + "/s3put\"]}");
                if (req.HttpMethod == "PUT" && p == "/s3put")
                {
                    Assert.Null(req.Headers["Authorization"]);            // signed URL: no bearer
                    putBody = ReadBody(req);
                    return new CannedResponse(200, "");
                }
                if (req.HttpMethod == "POST" && p.EndsWith("/signeds3upload"))
                    return new CannedResponse(200, "{\"objectId\":\"urn:adsk.objects:os.object:wip.dm.prod/a9d3-obj.bcfzip\"}");
                if (req.HttpMethod == "POST" && p.EndsWith("/attachments"))
                {
                    attachBody = ReadBody(req);
                    return new CannedResponse(200, "{\"attachments\":[]}");
                }
                return new CannedResponse(404, "{}");
            });
            using (server)
            {
                AccModelCoordSync.OverrideHostForTests(server.BaseUrl);
                var r = await AccIssueAttachment.AttachAsync(Creds(), "issue-9", Encoding.UTF8.GetBytes("BCF"), "STING clash.bcfzip", "bcfzip");

                Assert.True(r.Ok, r.Detail);
                var steps = server.Paths.Select(x => x.Split('?')[0]).ToList();
                Assert.Equal(6, steps.Count);
                Assert.EndsWith("/topFolders", steps[0]);
                Assert.EndsWith($"/data/v1/projects/b.{Container}/storage", steps[1]);
                Assert.EndsWith("/oss/v2/buckets/wip.dm.prod/objects/a9d3-obj.bcfzip/signeds3upload", steps[2]);
                Assert.Equal("/s3put", steps[3]);
                Assert.EndsWith("/signeds3upload", steps[4]);
                Assert.EndsWith($"/construction/issues/v1/projects/{Container}/attachments", steps[5]);
                Assert.Equal("BCF", putBody);

                var a = (JObject)JObject.Parse(attachBody)["attachments"][0];
                Assert.Equal("issue-9", (string)JObject.Parse(attachBody)["domainEntityId"]);
                Assert.Equal("a9d3-obj", (string)a["attachmentId"]);          // object key without extension
                Assert.Equal("a9d3-obj.bcfzip", (string)a["fileName"]);       // the object key itself
                Assert.Equal("issue-attachment", (string)a["attachmentType"]);
                Assert.Equal("urn:adsk.objects:os.object:wip.dm.prod/a9d3-obj.bcfzip", (string)a["storageUrn"]);
                Assert.Equal("STING clash.bcfzip", (string)a["displayName"]);
            }
        }

        [Fact]
        public async Task Attachment_NoHub_FailsBeforeAnyRequest_AndSaysWhy()
        {
            using var server = LoopbackServer.Always(200, "{}");
            AccModelCoordSync.OverrideHostForTests(server.BaseUrl);
            var c = Creds(); c.HubId = "";
            var r = await AccIssueAttachment.AttachAsync(c, "issue-9", new byte[] { 1 }, "x.bcfzip", "bcfzip");
            Assert.False(r.Ok);
            Assert.Equal("topFolders", r.Step);
            Assert.Contains("hub", r.Detail);
            Assert.Equal(0, server.RequestCount);
        }

        [Fact]
        public async Task Attachment_RefusedByAcc_IsReportedWithTheStep()
        {
            using var server = new LoopbackServer((_, req) =>
                req.Url.AbsolutePath.EndsWith("/topFolders")
                    ? new CannedResponse(200, "{\"data\":[{\"attributes\":{\"name\":\"Project Files\"},\"relationships\":{\"parent\":{\"data\":{\"id\":\"ROOT\"}}}}]}")
                    : new CannedResponse(403, "{\"detail\":\"forbidden\"}"));
            AccModelCoordSync.OverrideHostForTests(server.BaseUrl);
            var r = await AccIssueAttachment.AttachAsync(Creds(), "issue-9", new byte[] { 1 }, "x.bcfzip", "bcfzip");
            Assert.False(r.Ok);
            Assert.Equal("storage", r.Step);
            Assert.Equal(AccFetchStatus.AuthFailed, r.Status);
        }

        // ── Clash read carries what the locator needs ────────────────────────

        [Fact]
        public async Task GetClashes_CarriesDocumentUrns_AndTheModelSetVersion()
        {
            LoopbackServer server = null;
            server = new LoopbackServer((_, req) =>
            {
                string path = req.Url.AbsolutePath, b = server.BaseUrl;
                if (path.EndsWith("/tests"))
                    return new CannedResponse(200, "{\"tests\":[" +
                        "{\"id\":\"OLD\",\"status\":\"Success\",\"completedOn\":\"2026-08-01T00:00:00Z\",\"modelSetVersion\":6}," +
                        "{\"id\":\"NEW\",\"status\":\"Success\",\"completedOn\":\"2026-09-01T00:00:00Z\",\"modelSetVersion\":7}]}");
                if (path.EndsWith("/tests/NEW/resources"))
                    return new CannedResponse(200, "{\"resources\":[{\"type\":\"scope-version-clash.2.0.0\",\"url\":\"" + b + "/s/clash\"}," +
                        "{\"type\":\"scope-version-clash-instance.2.0.0\",\"url\":\"" + b + "/s/inst\"}," +
                        "{\"type\":\"scope-version-document.2.0.0\",\"url\":\"" + b + "/s/doc\"}]}");
                if (path == "/s/clash") return new CannedResponse(200, "{\"clashes\":[{\"id\":\"c1\",\"dist\":-0.02,\"status\":\"active\"}]}");
                if (path == "/s/inst") return new CannedResponse(200, "{\"instances\":[{\"cid\":\"c1\",\"ldid\":2,\"rdid\":3,\"lvid\":17299,\"rvid\":3403}]}");
                if (path == "/s/doc") return new CannedResponse(200, "{\"documents\":[{\"id\":2,\"name\":\"KUT_ARCH.rvt\",\"urn\":\"" + UrnArch + "\"}," +
                                                                     "{\"id\":3,\"name\":\"KUT_MEP.rvt\",\"urn\":\"" + UrnMep + "\"}]}");
                return new CannedResponse(404, "{}");
            });
            using (server)
            {
                AccModelCoordSync.OverrideHostForTests(server.BaseUrl);
                var r = await AccModelCoordSync.GetClashesAsync(Creds(), Container, ModelSet);
                Assert.True(r.Succeeded, r.Detail);
                var c = Assert.Single(r.Value);
                Assert.Equal(UrnArch, c.LeftDocumentUrn);
                Assert.Equal(UrnMep, c.RightDocumentUrn);
                Assert.Equal(7, c.ModelSetVersion);                        // the documented completedOn picked NEW
            }
        }

        // ── Settings ─────────────────────────────────────────────────────────

        [Fact]
        public void Policy_LocateTogglesDefaultOn_ParseStrictly()
        {
            string dir = Path.Combine(Path.GetTempPath(), "sting-locate-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string f = Path.Combine(dir, "acc_settings.json");
                File.WriteAllText(f, "{}");
                var p = AccOperatingPolicy.Load(f);
                Assert.True(p.IssueDeepLinks && p.IssueViewerLinks && p.IssueBcfAttachment);

                File.WriteAllText(f, "{\"issueDeepLinks\":false,\"issueViewerLinks\":true,\"issueBcfAttachment\":false}");
                p = AccOperatingPolicy.Load(f);
                Assert.Equal(AccPolicySource.Loaded, p.Source);
                Assert.False(p.IssueDeepLinks);
                Assert.True(p.IssueViewerLinks);
                Assert.False(p.IssueBcfAttachment);

                File.WriteAllText(f, "{\"issueBcfAttachment\":\"yes\"}");
                p = AccOperatingPolicy.Load(f);
                Assert.Equal(AccPolicySource.Malformed, p.Source);
                Assert.Contains("issueBcfAttachment", p.LoadError);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
