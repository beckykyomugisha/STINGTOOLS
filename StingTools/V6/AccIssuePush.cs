// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccIssuePush.cs — STING issue register → ACC Issues (the other half of
// ACC_ImportIssues). The Revit-free decisions behind ACC_PushIssueChanges.
//
// WHY THIS EXISTS
// ---------------
// ACC_ImportIssues keeps a per-row base (acc_last_import) for the fields ACC owns. When a
// coordinator changes one of those fields in STING and ACC has not moved, the import keeps
// the local value and reports it as LocalAhead - "changed in STING, not in ACC". Without a
// push, that change lives only in STING: the assignee in ACC never hears the issue was
// closed, and the next person to look at ACC acts on a stale status.
//
// THE CONTRACT
// ------------
//  * Candidates come from the REGISTER, not from a previous import's report: any row that
//    carries an ACC id and a base, where an owned field now differs from its base.
//  * Every candidate is re-read from ACC immediately before it is written. If ACC changed a
//    field since the base, that field is a CONFLICT - reported, never overwritten. The
//    person reconciles it; this code does not pick a winner.
//  * Only status and assignee are pushed. Title, description and due date changes are
//    reported as "not pushed": they are ACC's wording and ACC's programme.
//  * A status is pushed only when it ROUND-TRIPS: CanonicalAcc(ToAccStatus(s)) == s. A
//    STING status ACC cannot represent (ACCEPTED, RESOLVED, VOID) would come back on the
//    next import as a different value and re-trigger the push forever.
//  * ACC's own permission answer (permittedStatuses / permittedAttributes on the issue it
//    returned) is checked before writing. When ACC did not say, the write is attempted and
//    ACC's 403 is the answer.
//  * The base advances ONLY for fields ACC confirmed. A failed or ambiguous write leaves the
//    row exactly as it was, so the next run offers the same change again.
//
// Revit-free and log-free: linked into StingTools.Acc.Tests.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.V6
{
    /// <summary>One owned field that differs from its recorded base.</summary>
    public sealed class AccPushChange
    {
        public string Field { get; set; }
        public string BaseValue { get; set; }
        public string LocalValue { get; set; }
    }

    /// <summary>A register row with at least one field changed in STING since its base.</summary>
    public sealed class AccPushCandidate
    {
        public JObject Row { get; set; }
        public string IssueId { get; set; }
        public string AccIssueId { get; set; }
        public List<AccPushChange> Changes { get; } = new List<AccPushChange>();
    }

    /// <summary>What to send for one candidate, and why anything is not being sent.</summary>
    public sealed class AccPushPlanItem
    {
        public AccPushCandidate Candidate { get; set; }
        /// <summary>The PATCH body. Empty = nothing to write.</summary>
        public JObject Patch { get; } = new JObject();
        /// <summary>Row fields the PATCH carries.</summary>
        public List<string> PushFields { get; } = new List<string>();
        /// <summary>Fields ACC already holds the local value for: the base just catches up.</summary>
        public List<string> AgreeFields { get; } = new List<string>();
        /// <summary>The ACC status the PATCH sets, or null.</summary>
        public string AccStatus { get; set; }
        public List<AccImportConflict> Conflicts { get; } = new List<AccImportConflict>();
        /// <summary>Human-readable reasons a change is not being sent.</summary>
        public List<string> NotPushed { get; } = new List<string>();
        /// <summary>The comment posted after a successful PATCH, or null.</summary>
        public string CommentText { get; set; }
        public bool HasWrite => Patch.HasValues;
    }

    public static class AccIssuePush
    {
        /// <summary>Fields a push may write. Everything else in OwnedFields is report-only.</summary>
        public static readonly IReadOnlyCollection<string> PushableFields = new[] { "status", "assigned_to" };

        /// <summary>Canonical STING status → the ACC Issues v1 status that maps BACK to it.
        /// Null when no ACC status round-trips (see the file header).</summary>
        public static string ToAccStatus(string canonical)
        {
            string s = IssueStatusNormalizer.Canonical(canonical);
            string acc = s switch
            {
                "OPEN" => "open",
                "IN_PROGRESS" => "in_progress",
                "RESPONDED" => "completed",
                "CLOSED" => "closed",
                _ => null,
            };
            // Belt and braces: the map is only as good as the normaliser it has to agree with.
            return acc != null && IssueStatusNormalizer.CanonicalAcc(acc) == s ? acc : null;
        }

        /// <summary>Every row with an ACC id and a base where an owned field differs from it.
        /// A field with no recorded base is not a candidate: nothing says which side moved.</summary>
        public static List<AccPushCandidate> FindCandidates(JArray rows)
        {
            var list = new List<AccPushCandidate>();
            foreach (var row in (rows ?? new JArray()).OfType<JObject>())
            {
                string accId = ((string)row[AccIssueImport.AccIdField])?.Trim();
                if (string.IsNullOrEmpty(accId)) continue;
                if (!(row[AccIssueImport.BaseField] is JObject baseObj)) continue;

                var c = new AccPushCandidate { Row = row, IssueId = IssueSchema.IdOf(row), AccIssueId = accId };
                foreach (string f in AccIssueImport.OwnedFields)
                {
                    string b = AccIssueImport.BaseValue(baseObj, f, baseExisted: true);
                    if (b == null) continue;
                    string local = AccIssueImport.LocalValue(row, f);
                    if (!string.Equals(local, b, StringComparison.Ordinal))
                        c.Changes.Add(new AccPushChange { Field = f, BaseValue = b, LocalValue = local });
                }
                if (c.Changes.Count > 0) list.Add(c);
            }
            return list;
        }

        /// <summary>Decide what to send for one candidate, given the issue ACC returned just now.</summary>
        public static AccPushPlanItem Plan(AccPushCandidate c, AccIssue current)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            var item = new AccPushPlanItem { Candidate = c };
            if (current == null)
            {
                item.NotPushed.Add("ACC did not return the issue");
                return item;
            }
            var rec = AccIssueImportRecord.From(current);

            foreach (var ch in c.Changes)
            {
                string accNow = AccIssueImport.AccValue(rec, ch.Field);
                if (string.Equals(accNow, ch.LocalValue, StringComparison.Ordinal))
                {
                    item.AgreeFields.Add(ch.Field);          // someone already made ACC agree
                    continue;
                }
                if (!string.Equals(accNow, ch.BaseValue, StringComparison.Ordinal))
                {
                    item.Conflicts.Add(new AccImportConflict
                    {
                        IssueId = c.IssueId, AccIssueId = c.AccIssueId, Field = ch.Field,
                        BaseValue = ch.BaseValue, LocalValue = ch.LocalValue, AccValue = accNow,
                    });
                    continue;
                }

                switch (ch.Field)
                {
                    case "status":
                    {
                        string acc = ToAccStatus(ch.LocalValue);
                        if (acc == null)
                        {
                            item.NotPushed.Add($"status {ch.LocalValue}: no ACC status maps back to it " +
                                               "(ACC has open / in_progress / completed / closed) — set it in ACC");
                            break;
                        }
                        if (current.PermittedStatuses != null &&
                            !current.PermittedStatuses.Contains(acc, StringComparer.OrdinalIgnoreCase))
                        {
                            item.NotPushed.Add($"status → {acc}: ACC does not permit this user to set it " +
                                               $"(permitted: {(current.PermittedStatuses.Count == 0 ? "none" : string.Join(", ", current.PermittedStatuses))})");
                            break;
                        }
                        item.Patch["status"] = acc;
                        item.AccStatus = acc;
                        item.PushFields.Add("status");
                        break;
                    }
                    case "assigned_to":
                    {
                        if (current.PermittedAttributes != null &&
                            !current.PermittedAttributes.Contains("assignedTo", StringComparer.OrdinalIgnoreCase))
                        {
                            item.NotPushed.Add("assignee: ACC does not permit this user to change it");
                            break;
                        }
                        if (string.IsNullOrWhiteSpace(ch.LocalValue))
                        {
                            item.Patch["assignedTo"] = JValue.CreateNull();
                            item.Patch["assignedToType"] = JValue.CreateNull();
                            item.PushFields.Add("assigned_to");
                            break;
                        }
                        string type = FirstNonEmpty(current.AssignedToType, (string)c.Row["acc_assigned_to_type"]);
                        if (string.IsNullOrEmpty(type))
                        {
                            item.NotPushed.Add($"assignee '{ch.LocalValue}': ACC needs to know whether it is a user, " +
                                               "company or role, and neither the issue nor the register says — assign it in ACC");
                            break;
                        }
                        item.Patch["assignedTo"] = ch.LocalValue.Trim();
                        item.Patch["assignedToType"] = type;
                        item.PushFields.Add("assigned_to");
                        break;
                    }
                    default:
                        item.NotPushed.Add($"{ch.Field}: changed in STING but not pushed — only status and " +
                                           "assignee are sent; change it in ACC");
                        break;
                }
            }

            if (item.HasWrite) item.CommentText = CommentText(c.Row, item.PushFields.Contains("status"));
            return item;
        }

        /// <summary>The STING note/comment text worth carrying to ACC: the note on the latest
        /// status change (when status is pushed and the note is not the importer's own), and
        /// every register comment not yet sent. Null when there is nothing to say.</summary>
        public static string CommentText(JObject row, bool statusPushed)
        {
            if (row == null) return null;
            var parts = new List<string>();
            if (statusPushed && row["status_history"] is JArray hist && hist.Count > 0)
            {
                var last = hist.Last as JObject;
                string note = ((string)last?["note"] ?? "").Trim();
                string to = (string)last?["to"] ?? "";
                if (note.Length > 0 && string.Equals(to, IssueSchema.StatusOf(row), StringComparison.Ordinal) &&
                    !note.StartsWith("ACC import", StringComparison.OrdinalIgnoreCase))
                    parts.Add($"Status {to}: {note}");
            }
            if (row["comments"] is JArray comments)
            {
                int sent = (int?)row[AccIssueImport.CommentsPushedField] ?? 0;
                for (int i = Math.Max(0, sent); i < comments.Count; i++)
                {
                    string t = CommentBody(comments[i]);
                    if (!string.IsNullOrWhiteSpace(t)) parts.Add(t.Trim());
                }
            }
            if (parts.Count == 0) return null;
            return $"From STING ({IssueSchema.IdOf(row)}):\n" + string.Join("\n", parts);
        }

        private static string CommentBody(JToken t)
        {
            if (t == null) return null;
            if (t.Type == JTokenType.String) return (string)t;
            if (t is JObject o)
            {
                string body = (string)(o["text"] ?? o["comment"] ?? o["body"] ?? o["message"]);
                string by = (string)(o["author"] ?? o["by"] ?? o["user"]);
                return string.IsNullOrWhiteSpace(by) ? body : $"{by}: {body}";
            }
            return t.ToString();
        }

        /// <summary>Record what ACC confirmed. Call ONLY after a successful PATCH: the base of
        /// every pushed or already-agreeing field moves to the local value, so the next import
        /// sees agreement rather than a local change.</summary>
        public static void ApplyPushed(AccPushPlanItem item, DateTime now)
        {
            var row = item?.Candidate?.Row;
            if (row == null) return;
            if (!(row[AccIssueImport.BaseField] is JObject baseObj)) { baseObj = new JObject(); row[AccIssueImport.BaseField] = baseObj; }
            foreach (string f in item.PushFields.Concat(item.AgreeFields).Distinct())
                baseObj[f] = AccIssueImport.LocalValue(row, f);
            if (item.AccStatus != null) row["acc_status"] = item.AccStatus;
            row["acc_pushed_at"] = now.ToString("o", CultureInfo.InvariantCulture);
        }

        /// <summary>Base catch-up only (no write happened): fields ACC already agrees with.</summary>
        public static void ApplyAgreement(AccPushPlanItem item)
        {
            var row = item?.Candidate?.Row;
            if (row == null || item.AgreeFields.Count == 0) return;
            if (!(row[AccIssueImport.BaseField] is JObject baseObj)) return;
            foreach (string f in item.AgreeFields) baseObj[f] = AccIssueImport.LocalValue(row, f);
        }

        /// <summary>Record that every current register comment has been sent to ACC.</summary>
        public static void MarkCommentsPushed(JObject row)
        {
            if (row == null) return;
            row[AccIssueImport.CommentsPushedField] = (row["comments"] as JArray)?.Count ?? 0;
        }

        private static string FirstNonEmpty(params string[] v)
            => v.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;
    }
}
