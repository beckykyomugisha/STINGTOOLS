// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccReviewProposals.cs — ACC review decisions as PROPOSALS, never as edits.
//
// THE RULE. ACC is the approval authority (KUT, ISO 19650). What ACC decided comes back
// into STING as a proposal in a per-project queue; a PERSON accepts or dismisses it, and
// only an accepted proposal changes STING state - through the existing lifecycle code
// (DeliverableLifecycle, TransmittalRecord, the register's suitability history). Nothing
// here writes a deliverable, a transmittal or a register row.
//
// WHY NOT GUESS A CODE. "Approved" in an ACC workflow does not say A1 or A2 or B1: that is
// the project's decision. The code is taken from the project's reviewApprovalMap (label ->
// code, then value -> code); with no entry the proposal says "approved - code to be chosen"
// and the person picks it when accepting. A mapped code that is not an ISO 19650 suitability
// cannot reach here - AccOperatingPolicy refuses the settings file.
//
// DEDUPE. One proposal per (version URN, review id). A re-read never resurrects a decided
// proposal, never duplicates a pending one, and updates a pending one only when ACC's answer
// for that pair changed (it should not, once CLOSED - but if it does, the person must see
// the current answer, not the first).
//
// Revit-free and log-free (StingTools.Acc.Tests links it).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;

namespace StingTools.V6
{
    public static class AccProposalKind
    {
        public const string Approve = "APPROVE";
        public const string Reject = "REJECT";
    }

    public static class AccProposalState
    {
        public const string Pending = "PENDING";
        public const string Accepted = "ACCEPTED";
        public const string Dismissed = "DISMISSED";
    }

    /// <summary>One ACC decision, waiting for (or recording) a person's answer.</summary>
    public sealed class AccReviewProposal
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        [JsonProperty("kind")] public string Kind { get; set; } = string.Empty;
        [JsonProperty("state")] public string State { get; set; } = AccProposalState.Pending;

        [JsonProperty("versionUrn")] public string VersionUrn { get; set; } = string.Empty;
        [JsonProperty("itemUrn")] public string ItemUrn { get; set; } = string.Empty;
        [JsonProperty("fileName")] public string FileName { get; set; } = string.Empty;
        [JsonProperty("reviewId")] public string ReviewId { get; set; } = string.Empty;
        [JsonProperty("reviewSequenceId")] public string ReviewSequenceId { get; set; } = string.Empty;
        [JsonProperty("reviewStatus")] public string ReviewStatus { get; set; } = string.Empty;
        [JsonProperty("approvalValue")] public string ApprovalValue { get; set; } = string.Empty;
        [JsonProperty("approvalLabel")] public string ApprovalLabel { get; set; } = string.Empty;
        [JsonProperty("comment")] public string Comment { get; set; } = string.Empty;

        /// <summary>The ISO 19650 code to apply, or empty = "a person chooses it on accept".</summary>
        [JsonProperty("proposedSuitability")] public string ProposedSuitability { get; set; } = string.Empty;
        /// <summary>The CDE state that code files into (empty when no code).</summary>
        [JsonProperty("proposedCdeState")] public string ProposedCdeState { get; set; } = string.Empty;
        /// <summary>Why this code (or why no code) - shown to the person deciding.</summary>
        [JsonProperty("mappingReason")] public string MappingReason { get; set; } = string.Empty;

        /// <summary>The STING records this decision would change, found when it was read.</summary>
        [JsonProperty("transmittalId")] public string TransmittalId { get; set; } = string.Empty;
        [JsonProperty("deliverableKey")] public string DeliverableKey { get; set; } = string.Empty;
        [JsonProperty("registerDocId")] public string RegisterDocId { get; set; } = string.Empty;

        [JsonProperty("firstSeen")] public string FirstSeen { get; set; } = string.Empty;
        [JsonProperty("lastSeen")] public string LastSeen { get; set; } = string.Empty;
        [JsonProperty("decidedBy")] public string DecidedBy { get; set; } = string.Empty;
        [JsonProperty("decidedAt")] public string DecidedAt { get; set; } = string.Empty;
        /// <summary>The code actually applied (may differ from the proposal: a person chose it).</summary>
        [JsonProperty("appliedSuitability")] public string AppliedSuitability { get; set; } = string.Empty;
        [JsonProperty("decisionNote")] public string DecisionNote { get; set; } = string.Empty;

        [JsonIgnore] public bool IsPending => string.Equals(State, AccProposalState.Pending, StringComparison.Ordinal);
        [JsonIgnore] public bool HasTarget =>
            !string.IsNullOrEmpty(TransmittalId) || !string.IsNullOrEmpty(DeliverableKey) || !string.IsNullOrEmpty(RegisterDocId);

        public static string KeyFor(string versionUrn, string reviewId) => (versionUrn ?? "") + "|" + (reviewId ?? "");

        /// <summary>One line for a picker.</summary>
        public string Describe()
        {
            string what = Kind == AccProposalKind.Reject
                ? "REJECTED"
                : "APPROVED → " + (string.IsNullOrEmpty(ProposedSuitability) ? "code to be chosen" : $"{ProposedSuitability} ({ProposedCdeState})");
            string review = string.IsNullOrEmpty(ReviewSequenceId) ? "" : $"  · review #{ReviewSequenceId}";
            return $"{what}  ·  {(string.IsNullOrEmpty(FileName) ? VersionUrn : FileName)}{review}";
        }
    }

    /// <summary>A file STING knows is in ACC, with the version last seen. Used to find the
    /// version a published deliverable should be reviewed on.</summary>
    public sealed class AccKnownFile
    {
        [JsonProperty("fileName")] public string FileName { get; set; } = string.Empty;
        [JsonProperty("itemUrn")] public string ItemUrn { get; set; } = string.Empty;
        [JsonProperty("versionUrn")] public string VersionUrn { get; set; } = string.Empty;
        [JsonProperty("source")] public string Source { get; set; } = string.Empty;
        [JsonProperty("seenAt")] public string SeenAt { get; set; } = string.Empty;
    }

    /// <summary>A review STING started, so the same version is not sent twice.</summary>
    public sealed class AccStartedReview
    {
        [JsonProperty("versionUrn")] public string VersionUrn { get; set; } = string.Empty;
        [JsonProperty("reviewId")] public string ReviewId { get; set; } = string.Empty;
        [JsonProperty("workflowId")] public string WorkflowId { get; set; } = string.Empty;
        [JsonProperty("deliverableKey")] public string DeliverableKey { get; set; } = string.Empty;
        [JsonProperty("startedBy")] public string StartedBy { get; set; } = string.Empty;
        [JsonProperty("startedAt")] public string StartedAt { get; set; } = string.Empty;
    }

    /// <summary>The per-project queue file (acc_review_proposals.json).</summary>
    public sealed class AccReviewQueue
    {
        public const string FileName = "acc_review_proposals.json";
        public const int SchemaVersion = 1;

        [JsonProperty("schema")] public int Schema { get; set; } = SchemaVersion;
        [JsonProperty("_comment")] public string Comment { get; set; } =
            "ACC review decisions awaiting a person. Nothing here has changed STING state; " +
            "ACC_ReviewProposals applies an accepted one through the deliverable / transmittal / register lifecycle.";
        [JsonProperty("proposals")] public List<AccReviewProposal> Proposals { get; set; } = new List<AccReviewProposal>();
        [JsonProperty("files")] public List<AccKnownFile> Files { get; set; } = new List<AccKnownFile>();
        [JsonProperty("startedReviews")] public List<AccStartedReview> StartedReviews { get; set; } = new List<AccStartedReview>();

        /// <summary>Load, or an empty queue when the file is absent. A file that exists but will
        /// not parse is an ERROR (<paramref name="error"/> set, null returned): saving an empty
        /// queue over it would erase every recorded decision.</summary>
        public static AccReviewQueue Load(string path, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new AccReviewQueue();
            try
            {
                string text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) return new AccReviewQueue();
                var q = JsonConvert.DeserializeObject<AccReviewQueue>(text);
                if (q == null) { error = "the proposal queue is empty JSON"; return null; }
                q.Proposals ??= new List<AccReviewProposal>();
                q.Files ??= new List<AccKnownFile>();
                q.StartedReviews ??= new List<AccStartedReview>();
                return q;
            }
            catch (Exception ex)
            {
                error = $"{path} could not be read ({ex.Message}); it was left untouched";
                return null;
            }
        }

        /// <summary>Atomic save (temp file + replace).</summary>
        public void Save(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(this, Formatting.Indented));
            File.Move(tmp, path, true);
        }

        public IEnumerable<AccReviewProposal> Pending => Proposals.Where(p => p != null && p.IsPending);

        /// <summary>Record the files seen in ACC, newest version wins per item.</summary>
        public void RememberFiles(IEnumerable<AccKnownFile> seen)
        {
            foreach (var f in seen ?? Enumerable.Empty<AccKnownFile>())
            {
                if (f == null || string.IsNullOrEmpty(f.VersionUrn)) continue;
                var hit = Files.FirstOrDefault(x =>
                    (!string.IsNullOrEmpty(f.ItemUrn) && string.Equals(x.ItemUrn, f.ItemUrn, StringComparison.Ordinal)) ||
                    string.Equals(x.VersionUrn, f.VersionUrn, StringComparison.Ordinal));
                if (hit == null) { Files.Add(f); continue; }
                hit.VersionUrn = f.VersionUrn;
                if (!string.IsNullOrEmpty(f.FileName)) hit.FileName = f.FileName;
                if (!string.IsNullOrEmpty(f.ItemUrn)) hit.ItemUrn = f.ItemUrn;
                if (!string.IsNullOrEmpty(f.Source)) hit.Source = f.Source;
                hit.SeenAt = f.SeenAt;
            }
        }
    }

    /// <summary>What a merge did - every incoming proposal lands in exactly one bucket.</summary>
    public sealed class AccProposalMergeResult
    {
        public int Added, Updated, Unchanged, AlreadyDecided;
    }

    public static class AccReviewProposals
    {
        /// <summary>
        /// Turn one approval record into a proposal, or null when there is nothing to decide
        /// yet: the review is not CLOSED, or the outcome is IN_REVIEW / unknown.
        /// <paramref name="whyNone"/> says which, for the report.
        /// </summary>
        public static AccReviewProposal FromApproval(AccApprovalRecord rec, string versionUrn, string fileName,
            IReadOnlyDictionary<string, string> approvalMap, string comment, DateTime now, out string whyNone)
        {
            whyNone = null;
            if (rec == null) { whyNone = "no record"; return null; }
            string value = (rec.Value ?? "").Trim().ToUpperInvariant();
            string rs = (rec.ReviewStatus ?? "").Trim().ToUpperInvariant();
            if (rs != "CLOSED")
            {
                whyNone = rs == "OPEN" || rs.Length == 0 ? "review still open" : $"review {rs.ToLowerInvariant()}";
                return null;
            }
            if (value != "APPROVED" && value != "REJECTED")
            {
                whyNone = value.Length == 0 ? "no approval outcome" : $"outcome {value}";
                return null;
            }

            string stamp = now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            var p = new AccReviewProposal
            {
                Key = AccReviewProposal.KeyFor(versionUrn, rec.ReviewId),
                Kind = value == "APPROVED" ? AccProposalKind.Approve : AccProposalKind.Reject,
                VersionUrn = versionUrn ?? "",
                FileName = fileName ?? "",
                ReviewId = rec.ReviewId ?? "",
                ReviewSequenceId = rec.ReviewSequenceId ?? "",
                ReviewStatus = rs,
                ApprovalValue = value,
                ApprovalLabel = rec.Label ?? "",
                Comment = comment ?? "",
                FirstSeen = stamp,
                LastSeen = stamp,
            };

            if (p.Kind == AccProposalKind.Reject)
            {
                p.MappingReason = "ACC rejected this version; accepting records the rejection with the reviewer comment " +
                                  "and changes no suitability code";
                return p;
            }

            string code = MapCode(rec.Label, value, approvalMap, out string reason);
            p.ProposedSuitability = code;
            p.ProposedCdeState = code.Length > 0 ? Iso19650Suitability.CdeStateFor(code) ?? "" : "";
            p.MappingReason = reason;
            return p;
        }

        /// <summary>The project's code for an approval: label first (a workflow can have
        /// "Approved" and "Approved with comments"), then the value (APPROVED). Empty when
        /// neither is mapped - never a default code.</summary>
        public static string MapCode(string label, string value, IReadOnlyDictionary<string, string> map, out string reason)
        {
            if (map != null && map.Count > 0)
            {
                string l = (label ?? "").Trim();
                if (l.Length > 0 && map.TryGetValue(l, out var byLabel) && !string.IsNullOrWhiteSpace(byLabel))
                {
                    reason = $"reviewApprovalMap maps the ACC label '{l}' to {byLabel.Trim().ToUpperInvariant()}";
                    return byLabel.Trim().ToUpperInvariant();
                }
                string v = (value ?? "").Trim();
                if (v.Length > 0 && map.TryGetValue(v, out var byValue) && !string.IsNullOrWhiteSpace(byValue))
                {
                    reason = $"reviewApprovalMap maps the ACC outcome {v.ToUpperInvariant()} to {byValue.Trim().ToUpperInvariant()}" +
                             (l.Length > 0 ? $" (label '{l}' has no entry of its own)" : "");
                    return byValue.Trim().ToUpperInvariant();
                }
                reason = $"approved — code to be chosen: reviewApprovalMap has no entry for label '{l}' or outcome {v.ToUpperInvariant()}";
                return string.Empty;
            }
            reason = "approved — code to be chosen: this project has no reviewApprovalMap in its ACC settings, " +
                     "so no suitability code is assumed";
            return string.Empty;
        }

        /// <summary>Merge freshly-read proposals into the queue. See the header on DEDUPE.</summary>
        public static AccProposalMergeResult Merge(AccReviewQueue queue, IEnumerable<AccReviewProposal> incoming, DateTime now)
        {
            var result = new AccProposalMergeResult();
            if (queue == null) return result;
            string stamp = now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in incoming ?? Enumerable.Empty<AccReviewProposal>())
            {
                if (p == null || string.IsNullOrEmpty(p.Key) || !seen.Add(p.Key)) continue;
                var existing = queue.Proposals.FirstOrDefault(x => string.Equals(x?.Key, p.Key, StringComparison.Ordinal));
                if (existing == null)
                {
                    p.State = AccProposalState.Pending;
                    queue.Proposals.Add(p);
                    result.Added++;
                    continue;
                }
                existing.LastSeen = stamp;
                if (!existing.IsPending) { result.AlreadyDecided++; continue; }

                bool changed =
                    !string.Equals(existing.Kind, p.Kind, StringComparison.Ordinal) ||
                    !string.Equals(existing.ApprovalLabel, p.ApprovalLabel, StringComparison.Ordinal) ||
                    !string.Equals(existing.ProposedSuitability, p.ProposedSuitability, StringComparison.Ordinal) ||
                    !string.Equals(existing.Comment, p.Comment, StringComparison.Ordinal) ||
                    !string.Equals(existing.TransmittalId, p.TransmittalId, StringComparison.Ordinal) ||
                    !string.Equals(existing.DeliverableKey, p.DeliverableKey, StringComparison.Ordinal) ||
                    !string.Equals(existing.RegisterDocId, p.RegisterDocId, StringComparison.Ordinal);
                if (!changed) { result.Unchanged++; continue; }

                existing.Kind = p.Kind;
                existing.ApprovalValue = p.ApprovalValue;
                existing.ApprovalLabel = p.ApprovalLabel;
                existing.ProposedSuitability = p.ProposedSuitability;
                existing.ProposedCdeState = p.ProposedCdeState;
                existing.MappingReason = p.MappingReason;
                existing.Comment = p.Comment;
                existing.TransmittalId = p.TransmittalId;
                existing.DeliverableKey = p.DeliverableKey;
                existing.RegisterDocId = p.RegisterDocId;
                if (!string.IsNullOrEmpty(p.FileName)) existing.FileName = p.FileName;
                result.Updated++;
            }
            return result;
        }

        /// <summary>Record a person's decision. Only a pending proposal can be decided.</summary>
        public static bool Decide(AccReviewProposal p, string state, string user, DateTime now, string appliedCode, string note)
        {
            if (p == null || !p.IsPending) return false;
            if (state != AccProposalState.Accepted && state != AccProposalState.Dismissed) return false;
            p.State = state;
            p.DecidedBy = user ?? "";
            p.DecidedAt = now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            p.AppliedSuitability = appliedCode ?? "";
            p.DecisionNote = note ?? "";
            return true;
        }

        // ── Which STING records a decision is about ──────────────────────

        /// <summary>The transmittal row that recorded this version's upload (acc_version_urn,
        /// acc_cover_version_urn, or the same item). Empty when none.</summary>
        public static string FindTransmittal(JArray transmittalRows, string versionUrn, string itemUrn)
        {
            if (transmittalRows == null) return string.Empty;
            foreach (var row in transmittalRows.OfType<JObject>())
            {
                bool hit =
                    (!string.IsNullOrEmpty(versionUrn) &&
                     (Eq(row["acc_version_urn"], versionUrn) || Eq(row["acc_cover_version_urn"], versionUrn))) ||
                    (!string.IsNullOrEmpty(itemUrn) && Eq(row["acc_item_urn"], itemUrn));
                if (!hit) continue;
                string id = S(row["transmittal_id"]);
                return id.Length > 0 ? id : S(row["id"]);
            }
            return string.Empty;
        }

        /// <summary>The document key a file name belongs to, from <paramref name="keys"/>:
        /// the file's stem exactly, else the LONGEST key the stem starts with followed by a
        /// separator (so "…-0001" never matches "…-00010-P01"). Empty when nothing matches -
        /// a file is never attached to the nearest-looking document.</summary>
        public static string MatchDocumentKey(string fileName, IEnumerable<string> keys)
        {
            string stem = Path.GetFileNameWithoutExtension((fileName ?? "").Trim());
            if (stem.Length == 0) return string.Empty;
            string best = string.Empty;
            foreach (var raw in keys ?? Enumerable.Empty<string>())
            {
                string k = (raw ?? "").Trim();
                if (k.Length == 0) continue;
                if (string.Equals(stem, k, StringComparison.OrdinalIgnoreCase)) return k;
                if (stem.Length > k.Length && stem.StartsWith(k, StringComparison.OrdinalIgnoreCase) &&
                    "-_ .".IndexOf(stem[k.Length]) >= 0 && k.Length > best.Length)
                    best = k;
            }
            return best;
        }

        /// <summary>Keys of deliverables.json rows (DocNumber, then Code).</summary>
        public static List<string> DeliverableKeys(JArray rows) =>
            (rows ?? new JArray()).OfType<JObject>()
                .Select(o => FirstNonEmpty(S(o["DocNumber"]), S(o["Code"])))
                .Where(k => k.Length > 0).ToList();

        /// <summary>doc_id of document-register rows - the key the register's own suitability
        /// writer (BIMManagerEngine.UpdateDocumentSuitability) matches on, so a match here is a
        /// row that writer will find. Every register writer emits doc_id.</summary>
        public static List<string> RegisterKeys(JArray rows) =>
            (rows ?? new JArray()).OfType<JObject>()
                .Select(o => S(o["doc_id"]))
                .Where(k => k.Length > 0).ToList();

        // ── ACC transmittals into transmittals.json (read-only rows) ─────

        public const string AccSource = "acc";

        /// <summary>What an ACC transmittal status means in STING's vocabulary. Only COMPLETED
        /// is "sent"; everything else is kept as ACC wrote it (TransmittalStatus.Normalise
        /// shows an unknown status as written, never as DRAFT).</summary>
        public static string StingStatusFor(string accStatus)
        {
            string s = (accStatus ?? "").Trim().ToUpperInvariant();
            if (s == "COMPLETED") return "SENT";
            return s.Length == 0 ? "ACC_UNKNOWN" : "ACC_" + s;
        }

        /// <summary>
        /// Upsert ACC transmittals as READ-ONLY rows (source "acc", keyed acc_transmittal_id).
        /// A row STING wrote is never touched, even if it happens to share an id. Returns
        /// (added, updated, unchanged).
        /// </summary>
        public static (int added, int updated, int unchanged) MergeTransmittals(
            JArray rows, IEnumerable<AccTransmittal> accTransmittals, DateTime now)
        {
            int added = 0, updated = 0, unchanged = 0;
            if (rows == null) return (0, 0, 0);
            foreach (var t in accTransmittals ?? Enumerable.Empty<AccTransmittal>())
            {
                if (t == null || string.IsNullOrEmpty(t.Id)) continue;
                var fresh = BuildRow(t, now);
                var existing = rows.OfType<JObject>().FirstOrDefault(r =>
                    string.Equals(S(r["source"]), AccSource, StringComparison.Ordinal) &&
                    string.Equals(S(r["acc_transmittal_id"]), t.Id, StringComparison.Ordinal));
                if (existing == null) { rows.Add(fresh); added++; continue; }

                // Compare the ACC-owned content only; imported_at always differs.
                var a = (JObject)existing.DeepClone(); a.Remove("imported_at"); a.Remove("last_read_at");
                var b = (JObject)fresh.DeepClone(); b.Remove("imported_at"); b.Remove("last_read_at");
                existing["last_read_at"] = fresh["last_read_at"];
                if (JToken.DeepEquals(a, b)) { unchanged++; continue; }
                string importedAt = S(existing["imported_at"]);
                foreach (var prop in fresh.Properties()) existing[prop.Name] = prop.Value.DeepClone();
                if (importedAt.Length > 0) existing["imported_at"] = importedAt;
                updated++;
            }
            return (added, updated, unchanged);
        }

        private static JObject BuildRow(AccTransmittal t, DateTime now)
        {
            string stamp = now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            string date = t.CreatedAt.Length >= 10 ? t.CreatedAt.Substring(0, 10) : t.CreatedAt;
            var docs = new JArray(t.Documents.Select(d => new JObject
            {
                ["file_name"] = d.FileName,
                ["title"] = d.Title,
                ["version"] = d.Version,
                ["revision"] = d.RevisionLabel,
                ["acc_version_urn"] = d.Urn,
                ["acc_approve_status"] = d.ApproveStatus,
            }));
            return new JObject
            {
                ["transmittal_id"] = "ACC-TR-" + (t.SequenceId.Length > 0 ? t.SequenceId : t.Id),
                ["source"] = AccSource,
                ["read_only"] = true,
                ["acc_transmittal_id"] = t.Id,
                ["acc_sequence_id"] = t.SequenceId,
                ["title"] = t.Title,
                ["message"] = t.Message,
                ["status"] = StingStatusFor(t.Status),
                ["acc_status"] = t.Status,
                ["date_issued"] = StingStatusFor(t.Status) == "SENT" ? date : "",
                ["date_prepared"] = date,
                ["issued_by"] = t.SentBy,
                ["from_organization"] = t.SentByCompany,
                ["recipient"] = string.Join("; ", t.Recipients),
                ["documents"] = docs,
                ["documents_count"] = t.DocumentsCount,
                ["imported_at"] = stamp,
                ["last_read_at"] = stamp,
            };
        }

        private static bool Eq(JToken t, string v) => string.Equals(S(t), v, StringComparison.Ordinal);
        private static string S(JToken t) =>
            t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Array || t.Type == JTokenType.Object
                ? string.Empty : (t.ToString() ?? string.Empty).Trim();
        private static string FirstNonEmpty(params string[] v) => v.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? string.Empty;
    }
}
