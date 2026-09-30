// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccReviews.cs — the ACC Reviews and Transmittals READ clients, plus the
// one Reviews write (start a review on file versions).
//
// WHY. On KUT, ACC is the ISO 19650 approval authority: a document is approved or rejected
// by an ACC Review, not by a STING button. Until this file STING never read that decision
// back, so a drawing ACC had approved still read "S3 / SHARED" in the deliverable register,
// and a rejected one read as if nothing had happened. This client reads the decision; the
// Revit-free AccReviewProposals turns it into a PROPOSAL a person accepts - it never changes
// STING state by itself.
//
// Revit-free; logs through StingLog (the tests link a shim). Every read returns an
// AccFetchResult, so a failed read can never be rendered as "no reviews".
//
// ── API FACTS ─────────────────────────────────────────────────────────────────────
// Sources: the APS reference pages (JS-rendered; their slugs are below), the APS blog posts
// announcing the Reviews API (GA, write API, approval tracking), and the generated client in
// github.com/adsk-duszykf/Adsk.Platform.Toolkit.dotNet (Autodesk.ACC/GeneratedCode/
// Construction/Reviews + /Transmittals), which is generated from Autodesk's OpenAPI spec.
// NOTE: github.com/autodesk-platform-services/aps-sdk-openapi does NOT (2026-10-01) publish a
// reviews or transmittals yaml - only issues, accountadmin, data management, model
// derivative, oss, webhooks, auth.
//
//   GET  /construction/reviews/v1/projects/{projectId}/versions/{versionId}/approval-statuses
//        (reviews-getversionapprovalstatuses-GET)                                   CONFIRMED path
//        200 {pagination{limit,offset,totalResults}, results[{approvalStatus{id,label,value},
//             review{id,sequenceId,status}}]}                                        CONFIRMED shape
//        approvalStatus.value: APPROVED | REJECTED | IN_REVIEW                       CONFIRMED enum
//        review.status:        OPEN | CLOSED | VOID | FAILED                        CONFIRMED enum
//   GET  /construction/reviews/v1/projects/{projectId}/reviews/{reviewId}/progress
//        (reviews-getreviewprogress-GET) results[{stepId,stepName,status,notes,actionBy,
//        claimedBy,endTime,candidates}]; step status CLAIMED|UNCLAIMED|SUBMITTED|VOID CONFIRMED
//   GET  /construction/reviews/v1/projects/{projectId}/workflows (reviews-workflows-GET)
//        results[{id,name,status(ACTIVE|INACTIVE),approvalStatusOptions[{id,label,value,builtIn}]}]
//   POST /construction/reviews/v1/projects/{projectId}/reviews (reviews-createreview-POST)
//        body {name, workflowId, fileVersions:[{urn}]}; generated client also lists optional
//        notes + workflowOptions. A third-party client verified against the live validator
//        (github.com/KenLP/acc-forma-mcp-server src/apis/reviews.ts, 2026-08-13) reports
//        exactly name/workflowId/fileVersions accepted and extra properties REJECTED - so only
//        those three are sent. Reviewers come from the workflow's steps; there is no list.
//   GET  /construction/transmittals/v1/projects/{projectId}/transmittals
//        (transmittals-listtransmittals-GET) ?limit&offset&sort ·
//        results[{id,sequenceId,title,message,status,createdAt,documentsCount,sentBy{name,email,
//        companyName},packedStatus,recipients,displayRecipients,externalMembers}]   CONFIRMED path
//   GET  …/transmittals/{transmittalId}/documents (transmittals-listtransmittaldocuments-GET)
//        results[{urn,name,fileName,title,version,revisionLabel,approveStatus{id,label,value}}]
//   The Transmittals API is READ-ONLY: there is no create endpoint.
//
// ── NOT CONFIRMED against a live tenant ───────────────────────────────────────────
//   * versionId in the approval-statuses path is sent as the URL-ENCODED version URN (the
//     same convention the Docs custom-attribute endpoints document). Not live-tested.
//   * projectId is sent in the bare-GUID form (AccIds.ForAcc), as for every construction/*
//     API; the third-party client reports both forms answer 200.
//   * Whether a version's approvalStatus is final while its review is still OPEN (a
//     multi-step workflow). This client proposes nothing until the review is CLOSED.
//   * The transmittal `status` enum in the generated client is garbled (it lists
//     SENDING, COMPLETED, FAILED alongside non-status words), so the raw value is kept
//     verbatim and only COMPLETED is read as "sent".
//   * Scopes: data:read for the reads, data:write for POST reviews (by analogy with every
//     other ACC API; the reference pages were not machine-readable).
//   * Secure Service Accounts are NOT supported by the Reviews API (GA blog post).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.V6
{
    /// <summary>One approval record of one file version, as ACC returned it.</summary>
    public sealed class AccApprovalRecord
    {
        /// <summary>APPROVED, REJECTED or IN_REVIEW (upper-cased as received).</summary>
        public string Value { get; set; } = string.Empty;
        /// <summary>The workflow's own label for the outcome ("Approved with comments" …).</summary>
        public string Label { get; set; } = string.Empty;
        public string ApprovalOptionId { get; set; } = string.Empty;
        public string ReviewId { get; set; } = string.Empty;
        public string ReviewSequenceId { get; set; } = string.Empty;
        /// <summary>OPEN, CLOSED, VOID or FAILED.</summary>
        public string ReviewStatus { get; set; } = string.Empty;
    }

    /// <summary>A step of a review's progress - where the reviewer's notes live.</summary>
    public sealed class AccReviewStep
    {
        public string StepId { get; set; } = string.Empty;
        public string StepName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string ActionBy { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
    }

    public sealed class AccReviewWorkflow
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>A file in an ACC folder: the item and its current (tip) version.</summary>
    public sealed class AccFolderFile
    {
        public string FileName { get; set; } = string.Empty;
        public string ItemUrn { get; set; } = string.Empty;
        public string TipVersionUrn { get; set; } = string.Empty;
        public string FolderUrn { get; set; } = string.Empty;
    }

    /// <summary>An ACC transmittal as the list endpoint returns it.</summary>
    public sealed class AccTransmittal
    {
        public string Id { get; set; } = string.Empty;
        public string SequenceId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        /// <summary>Kept verbatim - see the header on the status enum.</summary>
        public string Status { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        public int DocumentsCount { get; set; }
        public string SentBy { get; set; } = string.Empty;
        public string SentByCompany { get; set; } = string.Empty;
        public List<string> Recipients { get; } = new List<string>();
        /// <summary>Filled only when the documents were read too.</summary>
        public List<AccTransmittalDocument> Documents { get; } = new List<AccTransmittalDocument>();
    }

    public sealed class AccTransmittalDocument
    {
        public string Urn { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string RevisionLabel { get; set; } = string.Empty;
        public string ApproveStatus { get; set; } = string.Empty;
    }

    public sealed class AccStartReviewResult
    {
        public bool Ok { get; set; }
        public string ReviewId { get; set; } = string.Empty;
        public string SequenceId { get; set; } = string.Empty;
        public AccFetchStatus Status { get; set; } = AccFetchStatus.TransportFailed;
        public int HttpStatus { get; set; }
        public string Detail { get; set; } = string.Empty;
    }

    public static class AccReviews
    {
        private const int PageLimit = 50;
        /// <summary>Runaway guard, not a real limit: 100 pages of 50.</summary>
        private const int MaxPages = 100;

        private static string ReviewsBase(string projectId) =>
            AccIssueSync.Host + "/construction/reviews/v1/projects/" + Uri.EscapeDataString(AccIds.ForAcc(projectId));
        private static string TransmittalsBase(string projectId) =>
            AccIssueSync.Host + "/construction/transmittals/v1/projects/" + Uri.EscapeDataString(AccIds.ForAcc(projectId));

        private static Func<HttpRequestMessage> Get(string url, AccCredentials creds) => () =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            AccIds.ApplyRegion(req, creds?.Region);
            return req;
        };

        // ── approval statuses of one version ─────────────────────────────

        /// <summary>Every approval record of one file version, all pages. A 200 without a
        /// <c>results</c> array is TransportFailed, never "not reviewed".</summary>
        public static async Task<AccFetchResult<List<AccApprovalRecord>>> GetApprovalStatusesAsync(
            AccCredentials creds, string projectId, string versionUrn)
        {
            var list = new List<AccApprovalRecord>();
            if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(versionUrn))
                return AccFetchResult<List<AccApprovalRecord>>.Failure(AccFetchStatus.NotFound, list, 0,
                    "no ACC project id or version URN to read approval statuses for");

            string baseUrl = ReviewsBase(projectId) + "/versions/" + Uri.EscapeDataString(versionUrn) + "/approval-statuses";
            var page = await ReadPagesAsync(creds, baseUrl, "approval statuses of " + versionUrn, o =>
            {
                var a = o["approvalStatus"] as JObject;
                var r = o["review"] as JObject;
                list.Add(new AccApprovalRecord
                {
                    Value = Str(a?["value"]).ToUpperInvariant(),
                    Label = Str(a?["label"]),
                    ApprovalOptionId = Str(a?["id"]),
                    ReviewId = Str(r?["id"]),
                    ReviewSequenceId = Str(r?["sequenceId"]),
                    ReviewStatus = Str(r?["status"]).ToUpperInvariant(),
                });
            }).ConfigureAwait(false);
            if (!page.ok) return AccFetchResult<List<AccApprovalRecord>>.Failure(page.status, list, page.http, page.detail);
            return AccFetchResult<List<AccApprovalRecord>>.Success(list, list.Count == 0);
        }

        /// <summary>The steps of a review, with each reviewer's notes.</summary>
        public static async Task<AccFetchResult<List<AccReviewStep>>> GetReviewProgressAsync(
            AccCredentials creds, string projectId, string reviewId)
        {
            var list = new List<AccReviewStep>();
            if (string.IsNullOrWhiteSpace(reviewId))
                return AccFetchResult<List<AccReviewStep>>.Failure(AccFetchStatus.NotFound, list, 0, "no review id");
            string url = ReviewsBase(projectId) + "/reviews/" + Uri.EscapeDataString(reviewId) + "/progress";
            var page = await ReadPagesAsync(creds, url, "progress of review " + reviewId, o =>
            {
                list.Add(new AccReviewStep
                {
                    StepId = Str(o["stepId"]),
                    StepName = Str(o["stepName"]),
                    Status = Str(o["status"]).ToUpperInvariant(),
                    Notes = Str(o["notes"]),
                    ActionBy = Str(o["actionBy"]?["name"]),
                    EndTime = Str(o["endTime"]),
                });
            }).ConfigureAwait(false);
            if (!page.ok) return AccFetchResult<List<AccReviewStep>>.Failure(page.status, list, page.http, page.detail);
            return AccFetchResult<List<AccReviewStep>>.Success(list, list.Count == 0);
        }

        /// <summary>The project's approval workflow definitions.</summary>
        public static async Task<AccFetchResult<List<AccReviewWorkflow>>> ListWorkflowsAsync(
            AccCredentials creds, string projectId)
        {
            var list = new List<AccReviewWorkflow>();
            var page = await ReadPagesAsync(creds, ReviewsBase(projectId) + "/workflows", "approval workflows", o =>
                list.Add(new AccReviewWorkflow { Id = Str(o["id"]), Name = Str(o["name"]), Status = Str(o["status"]) }))
                .ConfigureAwait(false);
            if (!page.ok) return AccFetchResult<List<AccReviewWorkflow>>.Failure(page.status, list, page.http, page.detail);
            return AccFetchResult<List<AccReviewWorkflow>>.Success(list, list.Count == 0);
        }

        /// <summary>The reviewer comment for a finished review: the notes of the SUBMITTED
        /// steps, last step first, joined. Empty when nobody wrote any. Pure.</summary>
        public static string ReviewerComment(IEnumerable<AccReviewStep> steps)
        {
            var parts = (steps ?? Enumerable.Empty<AccReviewStep>())
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Notes) &&
                            string.Equals(s.Status, "SUBMITTED", StringComparison.OrdinalIgnoreCase))
                .Reverse()
                .Select(s => (string.IsNullOrWhiteSpace(s.ActionBy) ? s.StepName : s.ActionBy) is string who &&
                             !string.IsNullOrWhiteSpace(who) ? $"{who}: {s.Notes.Trim()}" : s.Notes.Trim())
                .ToList();
            return string.Join(" | ", parts);
        }

        // ── start a review (the one write) ───────────────────────────────

        /// <summary>Start an ACC review of <paramref name="versionUrns"/> on a workflow. NOT
        /// idempotent: a retry after a gateway error could start a second review assigned to
        /// real people, so transport failures are not retried (AccHttp rules).</summary>
        public static async Task<AccStartReviewResult> StartReviewAsync(
            AccCredentials creds, string projectId, string workflowId, string name, IEnumerable<string> versionUrns)
        {
            var urns = (versionUrns ?? Enumerable.Empty<string>()).Where(u => !string.IsNullOrWhiteSpace(u))
                                                                   .Distinct(StringComparer.Ordinal).ToList();
            if (string.IsNullOrWhiteSpace(workflowId))
                return new AccStartReviewResult { Status = AccFetchStatus.NotFound, Detail = "no approval workflow id configured" };
            if (urns.Count == 0)
                return new AccStartReviewResult { Status = AccFetchStatus.NotFound, Detail = "no file version to review" };
            if (urns.Any(u => u.IndexOf("?version=", StringComparison.OrdinalIgnoreCase) < 0))
                return new AccStartReviewResult
                {
                    Status = AccFetchStatus.NotFound,
                    Detail = "a review takes file VERSION URNs (…?version=N); an item URN was given",
                };

            var body = new JObject
            {
                ["name"] = string.IsNullOrWhiteSpace(name) ? "STING review" : name.Trim(),
                ["workflowId"] = workflowId.Trim(),
                ["fileVersions"] = new JArray(urns.Select(u => new JObject { ["urn"] = u })),
            };
            string payload = body.ToString(Newtonsoft.Json.Formatting.None);
            var resp = await AccHttp.SendAsync(() =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, ReviewsBase(projectId) + "/reviews")
                { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
                AccIds.ApplyRegion(req, creds?.Region);
                return req;
            }, creds, idempotent: false).ConfigureAwait(false);

            var result = new AccStartReviewResult { HttpStatus = resp.Status };
            if (!resp.IsSuccess)
            {
                result.Status = resp.Classify();
                result.Detail = "start review: " + resp.Describe() + Trim(resp.Body);
                return result;
            }
            try
            {
                var o = JObject.Parse(resp.Body);
                result.ReviewId = Str(o["id"]);
                result.SequenceId = Str(o["sequenceId"]);
            }
            catch (Exception ex)
            {
                // The review WAS created (2xx); only the echo is unreadable. Say so rather than
                // report a failure that invites a second click and a second review.
                result.Ok = true;
                result.Status = AccFetchStatus.Ok;
                result.Detail = "ACC accepted the review (HTTP " + resp.Status + ") but its reply could not be read: " + ex.Message;
                return result;
            }
            result.Ok = true;
            result.Status = AccFetchStatus.Ok;
            return result;
        }

        // ── Data Management: files in a folder ───────────────────────────

        /// <summary>The files directly in an ACC folder (not sub-folders), each with its tip
        /// version, following links.next. A 200 without a <c>data</c> array is a failure.</summary>
        public static async Task<AccFetchResult<List<AccFolderFile>>> ListFolderFilesAsync(
            AccCredentials creds, string projectId, string folderUrn)
        {
            var list = new List<AccFolderFile>();
            if (string.IsNullOrWhiteSpace(folderUrn))
                return AccFetchResult<List<AccFolderFile>>.Failure(AccFetchStatus.NotFound, list, 0, "no folder URN");
            string url = AccIssueSync.Host + "/data/v1/projects/" + Uri.EscapeDataString(AccIds.ForDataManagement(projectId)) +
                         "/folders/" + Uri.EscapeDataString(folderUrn) + "/contents?filter[type]=items&page[limit]=200";
            for (int page = 0; page < MaxPages && !string.IsNullOrEmpty(url); page++)
            {
                var resp = await AccHttp.SendAsync(Get(url, creds), creds, idempotent: true).ConfigureAwait(false);
                if (!resp.IsSuccess)
                    return AccFetchResult<List<AccFolderFile>>.Failure(resp.Classify(), list, resp.Status,
                        $"folder {folderUrn}: " + resp.Describe());
                JObject doc;
                try { doc = JObject.Parse(resp.Body); }
                catch (Exception ex)
                {
                    return AccFetchResult<List<AccFolderFile>>.Failure(AccFetchStatus.TransportFailed, list, resp.Status,
                        $"folder {folderUrn}: the contents reply was not JSON ({ex.Message})");
                }
                if (!(doc["data"] is JArray data))
                    return AccFetchResult<List<AccFolderFile>>.Failure(AccFetchStatus.TransportFailed, list, resp.Status,
                        $"folder {folderUrn}: the contents reply carried no data array");
                foreach (var it in data.OfType<JObject>())
                {
                    if (!string.Equals(Str(it["type"]), "items", StringComparison.Ordinal)) continue;
                    string tip = Str(it["relationships"]?["tip"]?["data"]?["id"]);
                    if (string.IsNullOrEmpty(tip)) continue;
                    list.Add(new AccFolderFile
                    {
                        FileName = Str(it["attributes"]?["displayName"]),
                        ItemUrn = Str(it["id"]),
                        TipVersionUrn = tip,
                        FolderUrn = folderUrn,
                    });
                }
                url = Str(doc["links"]?["next"]?["href"]);
            }
            return AccFetchResult<List<AccFolderFile>>.Success(list, list.Count == 0);
        }

        // ── Data Management: an item's CURRENT version ───────────────────

        /// <summary>The item (lineage) URN a version URN belongs to, by the Data Management
        /// convention <c>…:fs.file:vf.{lineage}?version=N</c> → <c>…:dm.lineage:{lineage}</c>.
        /// Empty when the URN does not have that shape. Pure.</summary>
        public static string ItemUrnForVersion(string versionUrn)
        {
            if (string.IsNullOrWhiteSpace(versionUrn)) return string.Empty;
            string v = versionUrn.Trim();
            int q = v.IndexOf("?version=", StringComparison.OrdinalIgnoreCase);
            if (q <= 0) return string.Empty;
            v = v.Substring(0, q);
            const string marker = ":fs.file:vf.";
            int m = v.IndexOf(marker, StringComparison.Ordinal);
            if (m <= 0 || m + marker.Length >= v.Length) return string.Empty;
            return v.Substring(0, m) + ":dm.lineage:" + v.Substring(m + marker.Length);
        }

        /// <summary>The CURRENT (tip) version URN of an ACC item, read live from Data
        /// Management (GET projects/{p}/items/{item} → data.relationships.tip.data.id).
        /// ACC is the one source of truth for "which version is current": a version STING
        /// remembered may already be superseded by a later upload. A 200 without a tip is a
        /// failure, never "no version".</summary>
        public static async Task<AccFetchResult<string>> GetItemTipVersionAsync(
            AccCredentials creds, string projectId, string itemUrn)
        {
            if (string.IsNullOrWhiteSpace(itemUrn))
                return AccFetchResult<string>.Failure(AccFetchStatus.NotFound, string.Empty, 0, "no item URN");
            string url = AccIssueSync.Host + "/data/v1/projects/" + Uri.EscapeDataString(AccIds.ForDataManagement(projectId)) +
                         "/items/" + Uri.EscapeDataString(itemUrn.Trim());
            var resp = await AccHttp.SendAsync(Get(url, creds), creds, idempotent: true).ConfigureAwait(false);
            if (!resp.IsSuccess)
                return AccFetchResult<string>.Failure(resp.Classify(), string.Empty, resp.Status, $"item {itemUrn}: " + resp.Describe());
            string tip;
            try { tip = Str(JObject.Parse(resp.Body)["data"]?["relationships"]?["tip"]?["data"]?["id"]); }
            catch (Exception ex)
            {
                return AccFetchResult<string>.Failure(AccFetchStatus.TransportFailed, string.Empty, resp.Status,
                    $"item {itemUrn}: the reply was not JSON ({ex.Message})");
            }
            if (string.IsNullOrEmpty(tip))
                return AccFetchResult<string>.Failure(AccFetchStatus.TransportFailed, string.Empty, resp.Status,
                    $"item {itemUrn}: the reply carried no tip version");
            return AccFetchResult<string>.Success(tip, false);
        }

        /// <summary>The version a review must be started on: the item's live tip. The item is
        /// the remembered <paramref name="itemUrn"/>, else derived from the remembered version.
        /// A failed read is returned as a failure - the caller must not fall back to the
        /// remembered version, which may be the previous revision.</summary>
        public static Task<AccFetchResult<string>> CurrentVersionAsync(
            AccCredentials creds, string projectId, string itemUrn, string rememberedVersionUrn)
        {
            string item = !string.IsNullOrWhiteSpace(itemUrn) ? itemUrn.Trim() : ItemUrnForVersion(rememberedVersionUrn);
            if (string.IsNullOrEmpty(item))
                return Task.FromResult(AccFetchResult<string>.Failure(AccFetchStatus.NotFound, string.Empty, 0,
                    "STING does not know the ACC item of '" + (rememberedVersionUrn ?? "") +
                    "', so it cannot confirm that version is still current"));
            return GetItemTipVersionAsync(creds, projectId, item);
        }

        // ── Transmittals (read-only API) ─────────────────────────────────

        /// <summary>Every ACC transmittal in the project, optionally with its documents.
        /// A failure on any document list fails the whole read: a transmittal recorded with a
        /// silently empty document list would read as "sent nothing".</summary>
        public static async Task<AccFetchResult<List<AccTransmittal>>> ListTransmittalsAsync(
            AccCredentials creds, string projectId, bool withDocuments)
        {
            var list = new List<AccTransmittal>();
            var page = await ReadPagesAsync(creds, TransmittalsBase(projectId) + "/transmittals", "ACC transmittals", o =>
            {
                var t = new AccTransmittal
                {
                    Id = Str(o["id"]),
                    SequenceId = Str(o["sequenceId"]),
                    Title = Str(o["title"]),
                    Message = Str(o["message"]),
                    Status = Str(o["status"]),
                    CreatedAt = Str(o["createdAt"]),
                    DocumentsCount = o["documentsCount"]?.Type == JTokenType.Integer ? (int)o["documentsCount"] : 0,
                    SentBy = Str(o["sentBy"]?["name"]),
                    SentByCompany = Str(o["sentBy"]?["companyName"]),
                };
                foreach (var key in new[] { "displayRecipients", "recipients", "externalMembers" })
                    if (o[key] is JArray arr)
                        foreach (var r in arr)
                        {
                            string n = r is JObject ro ? FirstNonEmpty(Str(ro["name"]), Str(ro["email"])) : Str(r);
                            if (!string.IsNullOrWhiteSpace(n) && !t.Recipients.Contains(n)) t.Recipients.Add(n);
                        }
                if (!string.IsNullOrEmpty(t.Id)) list.Add(t);
            }).ConfigureAwait(false);
            if (!page.ok) return AccFetchResult<List<AccTransmittal>>.Failure(page.status, list, page.http, page.detail);

            if (withDocuments)
                foreach (var t in list)
                {
                    string url = TransmittalsBase(projectId) + "/transmittals/" + Uri.EscapeDataString(t.Id) + "/documents";
                    var docs = await ReadPagesAsync(creds, url, "documents of transmittal " + t.Id, o =>
                        t.Documents.Add(new AccTransmittalDocument
                        {
                            Urn = Str(o["urn"]),
                            FileName = FirstNonEmpty(Str(o["fileName"]), Str(o["name"])),
                            Title = Str(o["title"]),
                            Version = Str(o["version"]),
                            RevisionLabel = Str(o["revisionLabel"]),
                            ApproveStatus = FirstNonEmpty(Str(o["approveStatus"]?["label"]), Str(o["approveStatus"]?["value"])),
                        })).ConfigureAwait(false);
                    if (!docs.ok) return AccFetchResult<List<AccTransmittal>>.Failure(docs.status, list, docs.http, docs.detail);
                }
            return AccFetchResult<List<AccTransmittal>>.Success(list, list.Count == 0);
        }

        // ── shared paging ────────────────────────────────────────────────

        /// <summary>limit/offset paging over a {results[], pagination{totalResults}} reply.
        /// Stops when a page is short, when totalResults is reached, or at the guard.</summary>
        private static async Task<(bool ok, AccFetchStatus status, int http, string detail)> ReadPagesAsync(
            AccCredentials creds, string baseUrl, string what, Action<JObject> each)
        {
            int offset = 0;
            for (int page = 0; page < MaxPages; page++)
            {
                string sep = baseUrl.Contains("?") ? "&" : "?";
                string url = baseUrl + sep + "limit=" + PageLimit.ToString(CultureInfo.InvariantCulture) +
                             "&offset=" + offset.ToString(CultureInfo.InvariantCulture);
                var resp = await AccHttp.SendAsync(Get(url, creds), creds, idempotent: true).ConfigureAwait(false);
                if (!resp.IsSuccess)
                    return (false, resp.Classify(), resp.Status, what + ": " + resp.Describe());

                JObject o;
                try { o = JObject.Parse(resp.Body); }
                catch (Exception ex) { return (false, AccFetchStatus.TransportFailed, resp.Status, what + ": reply was not JSON (" + ex.Message + ")"); }
                if (!(o["results"] is JArray results))
                    return (false, AccFetchStatus.TransportFailed, resp.Status,
                            what + ": the reply carried no results array - a changed API shape, not an empty list");

                foreach (var r in results.OfType<JObject>()) each(r);

                int total = o["pagination"]?["totalResults"]?.Type == JTokenType.Integer ? (int)o["pagination"]["totalResults"] : -1;
                offset += results.Count;
                if (results.Count == 0 || results.Count < PageLimit) break;
                if (total >= 0 && offset >= total) break;
            }
            return (true, AccFetchStatus.Ok, 200, string.Empty);
        }

        private static string Str(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Object || t.Type == JTokenType.Array) return string.Empty;
            if (t.Type == JTokenType.Date)
                return ((DateTime)t).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            return (t.ToString() ?? string.Empty).Trim();
        }

        private static string FirstNonEmpty(params string[] v) => v.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? string.Empty;

        private static string Trim(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return string.Empty;
            body = body.Trim();
            return " — " + (body.Length > 300 ? body.Substring(0, 300) + "…" : body);
        }
    }
}
