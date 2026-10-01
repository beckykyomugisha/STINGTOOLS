// ACC Reviews → STING proposals. What these guard:
//   * a failed or mis-shaped Reviews read is never "not reviewed" (the whole point of
//     AccFetchResult), and the request goes to the documented path with the bare project id
//     and the URL-encoded version URN;
//   * starting a review sends exactly {name, workflowId, fileVersions[{urn}]} and is NOT
//     retried on a gateway error (a retry could start a second review for real reviewers);
//   * a proposal is made only for a CLOSED review with a final outcome, and an approval never
//     gets a code nobody configured;
//   * dedupe by (version, review): a decided proposal is never resurrected;
//   * a file is attached to a STING record only on an exact or separator-bounded match;
//   * ACC transmittals land as read-only rows and never touch a STING-written row.
//
// NOT covered here (Revit-bound, verified by build only): the ACC_ReadReviews /
// ACC_ReviewProposals / ACC_ReadTransmittals / ACC_StartReview command shells and the
// DeliverableLifecycle.ApproveFromReview / RejectFromReview transitions they call.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Acc.Tests.TestHelpers;
using StingTools.BIMManager;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccReviewsClientTests : IDisposable
    {
        private const string ProjectGuid = "c0337487-5b66-422b-a284-c273b424af54";
        private const string Version = "urn:adsk.wipprod:fs.file:vf.AS3XD9MzQvu4MakMF-w7vQ?version=3";

        public AccReviewsClientTests()
        {
            AccHttp.DelayHook = _ => Task.CompletedTask;
            CredentialIsolation.Reset();
        }

        public void Dispose()
        {
            AccHttp.DelayHook = t => Task.Delay(t);
            AccIssueSync.OverrideHostForTests(null);
        }

        private static AccCredentials Creds() => new AccCredentials
        {
            ClientId = "c", ClientSecret = "s", RefreshToken = "r",
            ProjectId = "b." + ProjectGuid, HubId = "b.hub-1",
            AccessToken = "tok", AccessTokenExpiry = DateTime.UtcNow.AddHours(1),
        };

        private static LoopbackServer Serve(Func<int, HttpListenerRequest, CannedResponse> h)
        {
            var s = new LoopbackServer(h);
            AccIssueSync.OverrideHostForTests(s.BaseUrl);
            return s;
        }

        // D8: the paging guard returned success with pages still unread.
        [Fact]
        public async Task APagedList_ThatOutrunsTheGuard_IsIncomplete_NotOk()
        {
            using var s = Serve((i, r) =>
            {
                var results = new JArray();
                for (int k = 0; k < 50; k++) results.Add(new JObject { ["id"] = $"wf-{i}-{k}", ["name"] = "w", ["status"] = "ACTIVE" });
                return new CannedResponse(200, new JObject
                {
                    ["results"] = results,
                    ["pagination"] = new JObject { ["totalResults"] = 1000000 },
                }.ToString());
            });
            var res = await AccReviews.ListWorkflowsAsync(Creds(), ProjectGuid);
            Assert.False(res.Succeeded);
            Assert.Contains("INCOMPLETE", res.Detail);
        }

        [Fact]
        public async Task AFolderListing_ThatOutrunsTheGuard_IsIncomplete_NotOk()
        {
            using var s = Serve((i, r) => new CannedResponse(200, new JObject
            {
                ["data"] = new JArray(),
                ["links"] = new JObject { ["next"] = new JObject { ["href"] = r.Url.GetLeftPart(UriPartial.Path) + "?page=" + (i + 1) } },
            }.ToString()));
            var res = await AccReviews.ListFolderFilesAsync(Creds(), ProjectGuid, "urn:adsk.wipprod:fs.folder:co.x");
            Assert.False(res.Succeeded);
            Assert.Contains("INCOMPLETE", res.Detail);
        }

        private static string ReadBody(HttpListenerRequest r)
        {
            using var sr = new StreamReader(r.InputStream, Encoding.UTF8);
            return sr.ReadToEnd();
        }

        private static JObject Record(string value, string label, string reviewId, string status, int seq = 4) => new JObject
        {
            ["approvalStatus"] = new JObject { ["id"] = "opt-" + label, ["label"] = label, ["value"] = value },
            ["review"] = new JObject { ["id"] = reviewId, ["sequenceId"] = seq, ["status"] = status },
        };

        private static string Page(IEnumerable<JObject> results, int total) => new JObject
        {
            ["pagination"] = new JObject { ["limit"] = 50, ["offset"] = 0, ["totalResults"] = total },
            ["results"] = new JArray(results),
        }.ToString();

        [Fact]
        public async Task ApprovalStatuses_AreRead_FromTheDocumentedPath()
        {
            string path = null;
            using var s = Serve((i, r) =>
            {
                path = r.RawUrl;
                return new CannedResponse(200, Page(new[] { Record("APPROVED", "Approved with comments", "rev-1", "CLOSED") }, 1));
            });
            var res = await AccReviews.GetApprovalStatusesAsync(Creds(), Creds().ProjectId, Version);

            Assert.Equal(AccFetchStatus.Ok, res.Status);
            var rec = Assert.Single(res.Value);
            Assert.Equal("APPROVED", rec.Value);
            Assert.Equal("Approved with comments", rec.Label);
            Assert.Equal("rev-1", rec.ReviewId);
            Assert.Equal("4", rec.ReviewSequenceId);
            Assert.Equal("CLOSED", rec.ReviewStatus);
            // bare project GUID, URL-encoded version URN
            Assert.StartsWith($"/construction/reviews/v1/projects/{ProjectGuid}/versions/" + Uri.EscapeDataString(Version) + "/approval-statuses", path);
            Assert.DoesNotContain("/b.", path);
        }

        [Theory]
        [InlineData(403, AccFetchStatus.AuthFailed)]
        [InlineData(404, AccFetchStatus.NotFound)]
        [InlineData(500, AccFetchStatus.TransportFailed)]
        public async Task AFailedRead_IsNotNotReviewed(int status, AccFetchStatus expected)
        {
            using var s = Serve((i, r) => new CannedResponse(status, "{}"));
            var res = await AccReviews.GetApprovalStatusesAsync(Creds(), Creds().ProjectId, Version);
            Assert.False(res.Succeeded);
            Assert.Equal(expected, res.Status);
            Assert.Empty(res.Value);
        }

        [Fact]
        public async Task A200WithoutResults_IsASchemaChange_NotAnEmptyList()
        {
            using var s = Serve((i, r) => new CannedResponse(200, "{\"data\":[]}"));
            var res = await AccReviews.GetApprovalStatusesAsync(Creds(), Creds().ProjectId, Version);
            Assert.Equal(AccFetchStatus.TransportFailed, res.Status);
        }

        [Fact]
        public async Task AnEmptyResultsArray_IsEmptyOk()
        {
            using var s = Serve((i, r) => new CannedResponse(200, Page(Array.Empty<JObject>(), 0)));
            var res = await AccReviews.GetApprovalStatusesAsync(Creds(), Creds().ProjectId, Version);
            Assert.Equal(AccFetchStatus.EmptyOk, res.Status);
        }

        [Fact]
        public async Task Paging_FollowsOffset_UntilTheTotal()
        {
            using var s = Serve((i, r) =>
            {
                int offset = int.Parse(r.QueryString["offset"] ?? "0");
                int count = offset == 0 ? 50 : 1;
                return new CannedResponse(200, Page(Enumerable.Range(0, count).Select(n => Record("IN_REVIEW", "x", "r" + (offset + n), "OPEN")), 51));
            });
            var res = await AccReviews.GetApprovalStatusesAsync(Creds(), Creds().ProjectId, Version);
            Assert.Equal(51, res.Value.Count);
            Assert.Equal(2, s.RequestCount);
        }

        [Fact]
        public async Task StartReview_SendsExactlyTheThreeDocumentedFields()
        {
            string body = null, method = null;
            using var s = Serve((i, r) =>
            {
                body = ReadBody(r); method = r.HttpMethod;
                return new CannedResponse(201, "{\"id\":\"rev-9\",\"sequenceId\":12,\"status\":\"OPEN\"}");
            });
            var res = await AccReviews.StartReviewAsync(Creds(), Creds().ProjectId, "wf-1", "STING — X.pdf", new[] { Version, Version });

            Assert.True(res.Ok);
            Assert.Equal("rev-9", res.ReviewId);
            Assert.Equal("12", res.SequenceId);
            Assert.Equal("POST", method);
            var o = JObject.Parse(body);
            Assert.Equal(new[] { "fileVersions", "name", "workflowId" }, o.Properties().Select(p => p.Name).OrderBy(n => n));
            Assert.Equal("wf-1", (string)o["workflowId"]);
            Assert.Equal(Version, (string)Assert.Single((JArray)o["fileVersions"])["urn"]);
        }

        [Fact]
        public async Task StartReview_IsNotRetriedOnAGatewayError()
        {
            using var s = Serve((i, r) => new CannedResponse(502, "{}"));
            var res = await AccReviews.StartReviewAsync(Creds(), Creds().ProjectId, "wf-1", "n", new[] { Version });
            Assert.False(res.Ok);
            Assert.Equal(1, s.RequestCount);
        }

        [Fact]
        public async Task StartReview_RefusesAnItemUrn_WithoutARequest()
        {
            using var s = Serve((i, r) => new CannedResponse(201, "{}"));
            var res = await AccReviews.StartReviewAsync(Creds(), Creds().ProjectId, "wf-1", "n",
                new[] { "urn:adsk.wipprod:dm.lineage:AS3XD9MzQvu4MakMF-w7vQ" });
            Assert.False(res.Ok);
            Assert.Equal(0, s.RequestCount);
            Assert.Contains("VERSION", res.Detail);
        }

        [Fact]
        public async Task ReviewerComment_ComesFromSubmittedSteps()
        {
            using var s = Serve((i, r) => new CannedResponse(200, Page(new[]
            {
                new JObject { ["stepId"] = "1", ["stepName"] = "Initiator", ["status"] = "SUBMITTED", ["notes"] = "" },
                new JObject { ["stepId"] = "2", ["stepName"] = "Review", ["status"] = "SUBMITTED", ["notes"] = "Fire strategy missing", ["actionBy"] = new JObject { ["name"] = "A. Reviewer" } },
                new JObject { ["stepId"] = "3", ["stepName"] = "Other", ["status"] = "VOID", ["notes"] = "ignored" },
            }, 3)));
            var steps = await AccReviews.GetReviewProgressAsync(Creds(), Creds().ProjectId, "rev-1");
            Assert.True(steps.Succeeded);
            Assert.Equal("A. Reviewer: Fire strategy missing", AccReviews.ReviewerComment(steps.Value));
        }

        [Fact]
        public async Task FolderFiles_FollowLinksNext_AndKeepTheTipVersion()
        {
            LoopbackServer s = null;
            s = Serve((i, r) =>
            {
                var item = new JObject
                {
                    ["type"] = "items", ["id"] = "urn:item:" + i,
                    ["attributes"] = new JObject { ["displayName"] = $"KUT-PCE-ZZ-XX-DR-A-000{i}.pdf" },
                    ["relationships"] = new JObject { ["tip"] = new JObject { ["data"] = new JObject { ["id"] = $"urn:v:{i}?version=2" } } },
                };
                var o = new JObject { ["data"] = new JArray(item) };
                if (i == 0) o["links"] = new JObject { ["next"] = new JObject { ["href"] = s.BaseUrl + "/next-page" } };
                return new CannedResponse(200, o.ToString());
            });
            var res = await AccReviews.ListFolderFilesAsync(Creds(), Creds().ProjectId, "urn:adsk.wipprod:fs.folder:co.X");
            Assert.Equal(2, res.Value.Count);
            Assert.Equal("urn:v:1?version=2", res.Value[1].TipVersionUrn);
            Assert.Contains("/data/v1/projects/b." + ProjectGuid + "/folders/", s.Paths[0]);
        }

        [Fact]
        public void ItemUrnForVersion_FollowsTheLineageConvention()
        {
            Assert.Equal("urn:adsk.wipprod:dm.lineage:AS3XD9MzQvu4MakMF-w7vQ", AccReviews.ItemUrnForVersion(Version));
            Assert.Equal("", AccReviews.ItemUrnForVersion("urn:adsk.wipprod:dm.lineage:X"));
            Assert.Equal("", AccReviews.ItemUrnForVersion(""));
        }

        [Fact]
        public async Task CurrentVersion_ReadsTheItemTip_NotTheRememberedVersion()
        {
            // A.pdf was seen at version 3; a new revision (version 4) was uploaded since.
            using var s = Serve((i, r) => new CannedResponse(200, new JObject
            {
                ["data"] = new JObject
                {
                    ["type"] = "items", ["id"] = "urn:adsk.wipprod:dm.lineage:AS3XD9MzQvu4MakMF-w7vQ",
                    ["relationships"] = new JObject { ["tip"] = new JObject { ["data"] = new JObject
                        { ["type"] = "versions", ["id"] = "urn:adsk.wipprod:fs.file:vf.AS3XD9MzQvu4MakMF-w7vQ?version=4" } } },
                },
            }.ToString()));
            var res = await AccReviews.CurrentVersionAsync(Creds(), Creds().ProjectId, "", Version);
            Assert.True(res.Succeeded);
            Assert.Equal("urn:adsk.wipprod:fs.file:vf.AS3XD9MzQvu4MakMF-w7vQ?version=4", res.Value);
            Assert.Contains("/data/v1/projects/b." + ProjectGuid + "/items/", s.Paths[0]);
            Assert.Contains("dm.lineage", Uri.UnescapeDataString(s.Paths[0]));
        }

        [Theory]
        [InlineData(500, "{}")]
        [InlineData(200, "{\"data\":{\"id\":\"x\"}}")]
        [InlineData(200, "not json")]
        public async Task CurrentVersion_AFailedOrTiplessRead_IsAFailure_NeverTheRememberedVersion(int status, string body)
        {
            using var s = Serve((i, r) => new CannedResponse(status, body));
            var res = await AccReviews.CurrentVersionAsync(Creds(), Creds().ProjectId, "urn:adsk.wipprod:dm.lineage:X", Version);
            Assert.False(res.Succeeded);
            Assert.NotEqual(Version, res.Value);
        }

        [Fact]
        public async Task CurrentVersion_WithNoKnowableItem_Refuses()
        {
            var res = await AccReviews.CurrentVersionAsync(Creds(), Creds().ProjectId, "", "urn:odd");
            Assert.False(res.Succeeded);
        }

        [Fact]
        public async Task Transmittals_AFailedDocumentList_FailsTheWholeRead()
        {
            using var s = Serve((i, r) => r.Url.AbsolutePath.EndsWith("/documents")
                ? new CannedResponse(500, "{}")
                : new CannedResponse(200, Page(new[] { new JObject { ["id"] = "t1", ["sequenceId"] = 7, ["title"] = "T", ["status"] = "COMPLETED" } }, 1)));
            var res = await AccReviews.ListTransmittalsAsync(Creds(), Creds().ProjectId, withDocuments: true);
            Assert.False(res.Succeeded);
            Assert.Contains("/construction/transmittals/v1/projects/" + ProjectGuid + "/transmittals", s.Paths[0]);
        }

        [Fact]
        public async Task Transmittals_AreReadWithDocuments()
        {
            using var s = Serve((i, r) => r.Url.AbsolutePath.EndsWith("/documents")
                ? new CannedResponse(200, Page(new[] { new JObject { ["urn"] = Version, ["fileName"] = "A.pdf", ["revisionLabel"] = "C01", ["approveStatus"] = new JObject { ["label"] = "Approved", ["value"] = "APPROVED" } } }, 1))
                : new CannedResponse(200, Page(new[] { new JObject
                  {
                      ["id"] = "t1", ["sequenceId"] = 7, ["title"] = "Stage 4 issue", ["status"] = "COMPLETED",
                      ["createdAt"] = "2026-09-22T10:00:00Z", ["documentsCount"] = 1,
                      ["sentBy"] = new JObject { ["name"] = "Davis", ["companyName"] = "Planscape" },
                      ["recipients"] = new JArray(new JObject { ["name"] = "Symbion" }),
                  } }, 1)));
            var res = await AccReviews.ListTransmittalsAsync(Creds(), Creds().ProjectId, withDocuments: true);
            Assert.True(res.Succeeded);
            var t = Assert.Single(res.Value);
            Assert.Equal("7", t.SequenceId);
            Assert.Equal("Davis", t.SentBy);
            Assert.Equal(new[] { "Symbion" }, t.Recipients);
            Assert.Equal("Approved", Assert.Single(t.Documents).ApproveStatus);
        }
    }

    public class AccReviewProposalTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 1, 9, 0, 0);
        private const string V = "urn:v:1?version=3";

        private static AccApprovalRecord Rec(string value, string label = "Approved", string status = "CLOSED", string review = "rev-1")
            => new AccApprovalRecord { Value = value, Label = label, ReviewId = review, ReviewSequenceId = "4", ReviewStatus = status };

        private static Dictionary<string, string> Map(params (string k, string v)[] kv) =>
            kv.ToDictionary(x => x.k, x => x.v, StringComparer.OrdinalIgnoreCase);

        [Theory]
        [InlineData("APPROVED", "OPEN")]
        [InlineData("IN_REVIEW", "CLOSED")]
        [InlineData("APPROVED", "VOID")]
        [InlineData("", "CLOSED")]
        public void NothingIsProposed_UntilTheReviewIsClosedWithAFinalOutcome(string value, string status)
        {
            var p = AccReviewProposals.FromApproval(Rec(value, status: status), V, "a.pdf", null, "", Now, out string why);
            Assert.Null(p);
            Assert.False(string.IsNullOrEmpty(why));
        }

        [Fact]
        public void AnApproval_WithNoMap_ProposesNoCode()
        {
            var p = AccReviewProposals.FromApproval(Rec("APPROVED"), V, "a.pdf", null, "", Now, out _);
            Assert.Equal(AccProposalKind.Approve, p.Kind);
            Assert.Equal("", p.ProposedSuitability);
            Assert.Equal("", p.ProposedCdeState);
            Assert.Contains("code to be chosen", p.MappingReason);
            Assert.Equal(AccProposalState.Pending, p.State);
        }

        [Fact]
        public void TheLabelWins_ThenTheValue_ThenNothing()
        {
            var map = Map(("Approved with comments", "B1"), ("APPROVED", "A2"));
            var byLabel = AccReviewProposals.FromApproval(Rec("APPROVED", "approved WITH comments"), V, "a", map, "", Now, out _);
            Assert.Equal("B1", byLabel.ProposedSuitability);
            Assert.Equal("PUBLISHED", byLabel.ProposedCdeState);

            var byValue = AccReviewProposals.FromApproval(Rec("APPROVED", "Approved for construction"), V, "a", map, "", Now, out _);
            Assert.Equal("A2", byValue.ProposedSuitability);

            // The map is case-insensitive, so a key "Approved" also answers the APPROVED value;
            // only a map with neither the label nor the value leaves the code to a person.
            var none = AccReviewProposals.FromApproval(Rec("APPROVED", "Odd"), V, "a", Map(("Approved for construction", "A2")), "", Now, out _);
            Assert.Equal("", none.ProposedSuitability);
        }

        [Fact]
        public void ARejection_CarriesTheComment_AndNoCode()
        {
            var map = Map(("APPROVED", "A1"), ("REJECTED", "A1"));
            var p = AccReviewProposals.FromApproval(Rec("REJECTED", "Rejected"), V, "a.pdf", map, "Fire strategy missing", Now, out _);
            Assert.Equal(AccProposalKind.Reject, p.Kind);
            Assert.Equal("", p.ProposedSuitability);
            Assert.Equal("Fire strategy missing", p.Comment);
        }

        [Fact]
        public void Merge_DedupesByVersionAndReview_AndNeverResurrectsADecision()
        {
            var q = new AccReviewQueue();
            AccReviewProposal Make(string review) => AccReviewProposals.FromApproval(Rec("APPROVED", review: review), V, "a", null, "", Now, out _);

            var r1 = AccReviewProposals.Merge(q, new[] { Make("rev-1"), Make("rev-1"), Make("rev-2") }, Now);
            Assert.Equal(2, r1.Added);
            Assert.Equal(2, q.Proposals.Count);

            Assert.True(AccReviewProposals.Decide(q.Proposals[0], AccProposalState.Dismissed, "me", Now, "", "no"));
            var r2 = AccReviewProposals.Merge(q, new[] { Make("rev-1"), Make("rev-2") }, Now.AddDays(1));
            Assert.Equal(1, r2.AlreadyDecided);
            Assert.Equal(1, r2.Unchanged);
            Assert.Equal(0, r2.Added);
            Assert.Equal(AccProposalState.Dismissed, q.Proposals[0].State);
            Assert.Single(q.Pending);

            // A pending proposal whose answer changed is updated in place, not duplicated.
            var changed = Make("rev-2"); changed.ApprovalLabel = "Approved with comments";
            var r3 = AccReviewProposals.Merge(q, new[] { changed }, Now.AddDays(2));
            Assert.Equal(1, r3.Updated);
            Assert.Equal(2, q.Proposals.Count);
            Assert.Equal("Approved with comments", q.Proposals[1].ApprovalLabel);
        }

        [Fact]
        public void Decide_OnlyMovesAPendingProposal()
        {
            var p = new AccReviewProposal { Key = "k" };
            Assert.False(AccReviewProposals.Decide(p, AccProposalState.Pending, "me", Now, "", ""));
            Assert.True(AccReviewProposals.Decide(p, AccProposalState.Accepted, "me", Now, "A1", "ok"));
            Assert.False(AccReviewProposals.Decide(p, AccProposalState.Dismissed, "me", Now, "", ""));
            Assert.Equal(AccProposalState.Accepted, p.State);
            Assert.Equal("A1", p.AppliedSuitability);
        }

        [Theory]
        [InlineData("KUT-PCE-ZZ-XX-DR-A-0001.pdf", "KUT-PCE-ZZ-XX-DR-A-0001")]
        [InlineData("KUT-PCE-ZZ-XX-DR-A-0001-C01.pdf", "KUT-PCE-ZZ-XX-DR-A-0001")]
        [InlineData("kut-pce-zz-xx-dr-a-0001_S3.pdf", "KUT-PCE-ZZ-XX-DR-A-0001")]
        [InlineData("KUT-PCE-ZZ-XX-DR-A-00010.pdf", "")]                  // not "…-0001" + more digits
        [InlineData("KUT-PCE-ZZ-XX-DR-A-0001X.pdf", "")]
        [InlineData("", "")]
        public void AFile_MatchesADocument_OnlyOnASeparatorBoundedPrefix(string file, string expected)
        {
            // "…-000" is a real key too: it must not swallow "…-0001" or "…-00010".
            var keys = new[] { "KUT-PCE-ZZ-XX-DR-A-0001", "KUT-PCE-ZZ-XX-DR-A-000" };
            Assert.Equal(expected, AccReviewProposals.MatchDocumentKey(file, keys));
        }

        [Fact]
        public void TheLongestMatchingKey_Wins()
        {
            Assert.Equal("A-01-02", AccReviewProposals.MatchDocumentKey("A-01-02-P1.pdf", new[] { "A", "A-01", "A-01-02" }));
        }

        [Fact]
        public void TheTransmittal_IsFoundByAnyRecordedUrn()
        {
            var rows = JArray.Parse(@"[
              {""transmittal_id"":""TR-1"",""acc_version_urn"":""urn:v:1?version=1""},
              {""id"":""TR-2"",""acc_cover_version_urn"":""urn:v:cover?version=1""},
              {""transmittal_id"":""TR-3"",""acc_item_urn"":""urn:item:3""}]");
            Assert.Equal("TR-1", AccReviewProposals.FindTransmittal(rows, "urn:v:1?version=1", ""));
            Assert.Equal("TR-2", AccReviewProposals.FindTransmittal(rows, "urn:v:cover?version=1", ""));
            Assert.Equal("TR-3", AccReviewProposals.FindTransmittal(rows, "urn:v:other?version=9", "urn:item:3"));
            Assert.Equal("", AccReviewProposals.FindTransmittal(rows, "urn:v:none?version=1", "urn:item:none"));
        }

        // E3: v3's approval must not land on v1's transmittal.
        [Fact]
        public void TheTransmittal_ForThisVersion_WinsOverAnEarlierOneOnTheSameItem()
        {
            var rows = JArray.Parse(@"[
              {""transmittal_id"":""TR-v1"",""acc_item_urn"":""urn:item:9"",""acc_version_urn"":""urn:v:9?version=1""},
              {""transmittal_id"":""TR-v3"",""acc_item_urn"":""urn:item:9"",""acc_version_urn"":""urn:v:9?version=3""}]");
            Assert.Equal("TR-v3", AccReviewProposals.FindTransmittal(rows, "urn:v:9?version=3", "urn:item:9"));
            Assert.Equal("TR-v1", AccReviewProposals.FindTransmittal(rows, "urn:v:9?version=1", "urn:item:9"));
        }

        [Fact]
        public void SeveralTransmittalsOnTheItem_AndNoneForThisVersion_IsRefusedWithAReason()
        {
            var rows = JArray.Parse(@"[
              {""transmittal_id"":""TR-a"",""acc_item_urn"":""urn:item:9""},
              {""transmittal_id"":""TR-b"",""acc_item_urn"":""urn:item:9""}]");
            Assert.Equal("", AccReviewProposals.FindTransmittal(rows, "urn:v:9?version=4", "urn:item:9", out string why));
            Assert.Contains("TR-a", why);
            Assert.Contains("TR-b", why);
        }

        [Fact]
        public void TheOnlyTransmittalOnTheItem_RecordingAnotherVersion_IsRefused()
        {
            var rows = JArray.Parse(@"[{""transmittal_id"":""TR-v1"",""acc_item_urn"":""urn:item:9"",""acc_version_urn"":""urn:v:9?version=1""}]");
            Assert.Equal("", AccReviewProposals.FindTransmittal(rows, "urn:v:9?version=3", "urn:item:9", out string why));
            Assert.Contains("TR-v1", why);
        }

        [Fact]
        public void TheOnlyTransmittalOnTheItem_RecordingNoVersion_IsStillFound()
        {
            var rows = JArray.Parse(@"[{""transmittal_id"":""TR-1"",""acc_item_urn"":""urn:item:9""}]");
            Assert.Equal("TR-1", AccReviewProposals.FindTransmittal(rows, "urn:v:9?version=3", "urn:item:9", out string why));
            Assert.Null(why);
        }

        [Fact]
        public void AnUnreadableQueue_IsAnError_NotAnEmptyQueue()
        {
            string path = Path.Combine(Path.GetTempPath(), "sting-acc-queue-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{ not json");
            try
            {
                Assert.Null(AccReviewQueue.Load(path, out string err));
                Assert.False(string.IsNullOrEmpty(err));
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void TheQueue_RoundTrips()
        {
            string path = Path.Combine(Path.GetTempPath(), "sting-acc-queue-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var q = new AccReviewQueue();
                AccReviewProposals.Merge(q, new[] { AccReviewProposals.FromApproval(Rec("APPROVED"), V, "a.pdf", null, "", Now, out _) }, Now);
                q.RememberFiles(new[] { new AccKnownFile { FileName = "a.pdf", ItemUrn = "i1", VersionUrn = "urn:v:1?version=1" } });
                q.RememberFiles(new[] { new AccKnownFile { FileName = "a.pdf", ItemUrn = "i1", VersionUrn = "urn:v:1?version=2" } });
                q.Save(path);
                var back = AccReviewQueue.Load(path, out string err);
                Assert.Null(err);
                Assert.Single(back.Proposals);
                Assert.Equal(AccReviewProposal.KeyFor(V, "rev-1"), back.Proposals[0].Key);
                var f = Assert.Single(back.Files);
                Assert.Equal("urn:v:1?version=2", f.VersionUrn);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void AccTransmittals_AreReadOnlyRows_AndNeverTouchStingRows()
        {
            var rows = JArray.Parse(@"[{""transmittal_id"":""ACC-TR-7"",""status"":""SENT"",""title"":""STING's own""}]");
            var t = new AccTransmittal { Id = "t1", SequenceId = "7", Title = "Stage 4", Status = "COMPLETED", CreatedAt = "2026-09-22T10:00:00Z" };
            t.Recipients.Add("Symbion");

            var r1 = AccReviewProposals.MergeTransmittals(rows, new[] { t }, Now);
            Assert.Equal((1, 0, 0), r1);
            Assert.Equal("STING's own", (string)rows[0]["title"]);          // untouched
            var acc = (JObject)rows[1];
            Assert.Equal("acc", (string)acc["source"]);
            Assert.True((bool)acc["read_only"]);
            Assert.Equal("SENT", (string)acc["status"]);
            Assert.Equal("2026-09-22", (string)acc["date_issued"]);
            Assert.Equal("Symbion", TransmittalRecord.Recipient(acc));

            Assert.Equal((0, 0, 1), AccReviewProposals.MergeTransmittals(rows, new[] { t }, Now.AddHours(1)));
            t.Status = "FAILED";
            Assert.Equal((0, 1, 0), AccReviewProposals.MergeTransmittals(rows, new[] { t }, Now.AddHours(2)));
            Assert.Equal("ACC_FAILED", (string)rows[1]["status"]);
            Assert.Equal(2, rows.Count);
        }
    }

    public class TransmittalReviewDecisionTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 1, 9, 0, 0);

        [Fact]
        public void AnApproval_Acknowledges_AndRecordsTheCode()
        {
            var rows = JArray.Parse(@"[{""transmittal_id"":""TR-1"",""status"":""SENT"",""suitability"":""S3""}]");
            var row = TransmittalRecord.RecordReviewDecision(rows, "TR-1", true, "a2", Now, "me", "ACC review #4", out string why);
            Assert.Null(why);
            Assert.Equal("ACKNOWLEDGED", (string)row["status"]);
            Assert.Equal("A2", (string)row["suitability"]);
            var h = (JObject)((JArray)row["status_history"]).Last;
            Assert.Equal("SENT", (string)h["from"]);
            Assert.Equal("S3", (string)h["suitability_from"]);
            Assert.Equal("A2", (string)h["suitability_to"]);
        }

        [Fact]
        public void ARejection_Rejects_AndKeepsTheCode()
        {
            var rows = JArray.Parse(@"[{""transmittal_id"":""TR-1"",""status"":""SENT"",""suitability"":""S3""}]");
            var row = TransmittalRecord.RecordReviewDecision(rows, "TR-1", false, "A1", Now, "me", "rejected", out _);
            Assert.Equal("REJECTED", (string)row["status"]);
            Assert.Equal("S3", (string)row["suitability"]);
        }

        [Theory]
        [InlineData(@"[{""transmittal_id"":""TR-1"",""status"":""SENT"",""read_only"":true}]")]
        [InlineData(@"[{""transmittal_id"":""TR-1"",""status"":""VOID""}]")]
        [InlineData(@"[{""transmittal_id"":""TR-2"",""status"":""SENT""}]")]
        public void AReadOnlyVoidOrMissingRow_IsRefused(string json)
        {
            var rows = JArray.Parse(json);
            string before = rows.ToString();
            Assert.Null(TransmittalRecord.RecordReviewDecision(rows, "TR-1", true, "A1", Now, "me", "", out string why));
            Assert.False(string.IsNullOrEmpty(why));
            Assert.Equal(before, rows.ToString());
        }
    }

    public class AccReviewPolicyTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "sting-accreview-policy-" + Guid.NewGuid().ToString("N"));
        private string PathFor() => Path.Combine(_dir, AccOperatingPolicy.FileName);

        public AccReviewPolicyTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

        private AccOperatingPolicy Load(string json)
        {
            File.WriteAllText(PathFor(), json);
            return AccOperatingPolicy.Load(PathFor());
        }

        [Fact]
        public void AValidMapAndWorkflow_Load()
        {
            var p = Load(@"{""reviewApprovalMap"":{""Approved"":""a1"",""Approved with comments"":""B1""},
                            ""startAccReviewOnPublish"":{""workflowId"":""wf-1"",""unattended"":true}}");
            Assert.Equal(AccPolicySource.Loaded, p.Source);
            Assert.Equal("A1", p.ReviewApprovalMap["approved"]);
            Assert.Equal("wf-1", p.ReviewWorkflowId);
            Assert.True(p.ReviewStartUnattended);
        }

        [Fact]
        public void Absent_MeansNoMapAndNoReviewStart()
        {
            var p = Load("{}");
            Assert.Empty(p.ReviewApprovalMap);
            Assert.Equal("", p.ReviewWorkflowId);
            Assert.False(p.ReviewStartUnattended);
        }

        [Theory]
        [InlineData(@"{""reviewApprovalMap"":{""Approved"":""A9X""}}")]           // not a code
        [InlineData(@"{""reviewApprovalMap"":{""Approved"":""S0""}}")]            // WIP: not a grant
        [InlineData(@"{""reviewApprovalMap"":{""Approved"":""AR""}}")]            // ARCHIVE: not a grant
        [InlineData(@"{""reviewApprovalMap"":{""Approved"":1}}")]
        [InlineData(@"{""startAccReviewOnPublish"":""wf-1""}")]
        [InlineData(@"{""startAccReviewOnPublish"":{}}")]
        [InlineData(@"{""startAccReviewOnPublish"":{""workflowId"":""""}}")]
        [InlineData(@"{""startAccReviewOnPublish"":{""workflowId"":""wf"",""workflow"":""typo""}}")]
        public void AnythingElse_IsMalformed(string json)
        {
            var p = Load(json);
            Assert.Equal(AccPolicySource.Malformed, p.Source);
            Assert.Empty(p.ReviewApprovalMap);
            Assert.Equal("", p.ReviewWorkflowId);
        }
    }
}
