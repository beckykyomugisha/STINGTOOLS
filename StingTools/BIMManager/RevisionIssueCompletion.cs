// StingTools — what an issue changes outside the model.
//
// "Issue Sheets for Revision" marked the Revit revision issued and refreshed the title
// blocks, and nothing else knew. The deliverable rows linked to those sheets kept their
// old revision and no issue date; the document register had no row until someone
// exported; the MIDP drift report had no actual date to compare its plan against; and
// every local issue whose target_revision matched was silently set CLOSED — a design
// team closing its own RFIs because a drawing went out.
//
// This is the one completion hook. It is Revit-free (the command gathers the facts and
// saves the files) so each rule is unit-tested:
//   * linked deliverables take the revision from the Revit issue (DeliverableRevisionRule),
//     the issue's suitability, the CDE state that suitability files in, the issue date,
//     and a RevisionHistory entry;
//   * each issued sheet gets its register row created or updated, keyed exactly as the
//     Export Centre keys a sheet's PDF (ISO identifier, else sheet number), so the later
//     export updates the same row;
//   * matching open issues are PROPOSED for resolution (RESPONDED + a note) — never closed.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Planscape.Docs.Templates;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.BIMManager
{
    /// <summary>One sheet in an issue.</summary>
    public sealed class IssuedSheet
    {
        public string SheetNumber { get; set; } = "";
        public string SheetName { get; set; } = "";
        /// <summary>ISO identifier when the sheet has one, else the sheet number — the
        /// register key the Export Centre uses for the sheet's PDF.</summary>
        public string DocNumber { get; set; } = "";
        /// <summary>The revision as THIS sheet prints it (per-sheet numbering).</summary>
        public string Revision { get; set; } = "";
    }

    /// <summary>The facts of one completed issue.</summary>
    public sealed class RevisionIssueEvent
    {
        /// <summary>The project-level revision code, for issue matching and messages.</summary>
        public string RevisionCode { get; set; } = "";
        public string Suitability { get; set; } = "";
        /// <summary>yyyy-MM-dd.</summary>
        public string IssuedDate { get; set; } = "";
        public string User { get; set; } = "";
        public List<IssuedSheet> Sheets { get; set; } = new List<IssuedSheet>();
    }

    public sealed class IssueCompletionReport
    {
        public List<string> DeliverablesUpdated { get; } = new List<string>();
        public int RegisterAdded { get; set; }
        public int RegisterUpdated { get; set; }
        public List<string> IssuesProposed { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class RevisionIssueCompletion
    {
        // ── deliverables.json ────────────────────────────────────────────

        /// <summary>Update every deliverable row linked (SheetNumbers) to an issued sheet.
        /// Returns the keys of the rows changed.</summary>
        public static List<string> ApplyToDeliverables(JArray deliverables, RevisionIssueEvent ev,
            DateTime utcNow, IssueCompletionReport report = null)
        {
            var changed = new List<string>();
            if (deliverables == null || ev?.Sheets == null || ev.Sheets.Count == 0) return changed;

            var bySheet = ev.Sheets
                .Where(s => !string.IsNullOrWhiteSpace(s.SheetNumber))
                .GroupBy(s => s.SheetNumber.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Revision ?? "", StringComparer.OrdinalIgnoreCase);

            string suit = NormaliseSuitability(ev.Suitability);
            string cde = suit.Length == 0 ? null : Iso19650Suitability.CdeStateFor(suit);

            foreach (var row in deliverables.OfType<JObject>())
            {
                var linked = ReadStringList(row["SheetNumbers"]);
                if (linked.Count == 0 || !linked.Any(bySheet.ContainsKey)) continue;

                var decision = DeliverableRevisionRule.Derive((string)row["Revision"], linked, bySheet);
                if (!decision.FromSheets) continue;   // linked but none of its sheets issued a number

                string key = DocumentIdentity.FirstNonBlank(row, DocumentIdentity.DeliverableKeys) ?? "";
                row["Revision"] = decision.Revision;
                row["RevisionSource"] = decision.Source;
                if (suit.Length > 0) row["Suitability"] = suit;
                if (!string.IsNullOrEmpty(cde)) row["CDE"] = cde;
                row["IssuedDate"] = ev.IssuedDate ?? "";
                row["IssuedBy"] = ev.User ?? "";

                if (!(row["RevisionHistory"] is JArray hist))
                {
                    hist = new JArray();
                    row["RevisionHistory"] = hist;
                }
                hist.Add(new JObject
                {
                    ["Revision"] = decision.Revision,
                    ["Suitability"] = suit.Length > 0 ? suit : (string)row["Suitability"],
                    ["Timestamp"] = utcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    ["User"] = ev.User ?? "",
                    ["Reason"] = $"Issued with revision {ev.RevisionCode} on {ev.IssuedDate}" +
                                 (decision.SheetsDisagree ? " — " + decision.Note : ""),
                    ["TemplateId"] = null,
                });
                if (decision.SheetsDisagree) report?.Warnings.Add($"Deliverable {key}: {decision.Note}");
                changed.Add(key);
            }
            report?.DeliverablesUpdated.AddRange(changed);
            return changed;
        }

        // ── document_register.json ───────────────────────────────────────

        /// <summary>Create or update one register row per issued sheet, keyed by doc_number
        /// (the Export Centre's key for a sheet PDF). No file yet: the export fills file_*.</summary>
        public static void ApplyToRegister(JArray register, RevisionIssueEvent ev, DateTime now,
            IssueCompletionReport report)
        {
            if (register == null || ev?.Sheets == null) return;
            string suit = NormaliseSuitability(ev.Suitability);
            string cde = suit.Length == 0 ? null : Iso19650Suitability.CdeStateFor(suit);
            string stamp = now.ToString("yyyy-MM-dd HH:mm");

            foreach (var s in ev.Sheets)
            {
                string docNumber = string.IsNullOrWhiteSpace(s.DocNumber) ? s.SheetNumber : s.DocNumber;
                if (string.IsNullOrWhiteSpace(docNumber)) continue;
                docNumber = docNumber.Trim();

                var row = register.OfType<JObject>().FirstOrDefault(r =>
                    string.Equals(r["doc_number"]?.ToString(), docNumber, StringComparison.OrdinalIgnoreCase));
                if (row == null)
                {
                    string id = ExportRegisterUpsert.NextId(register, "DOC", "document_id");
                    row = new JObject
                    {
                        ["doc_id"] = id,
                        ["document_id"] = id,
                        ["doc_number"] = docNumber,
                        ["title"] = $"{s.SheetNumber} - {s.SheetName}",
                        ["description"] = $"{s.SheetNumber} - {s.SheetName}",
                        ["document_type"] = "DR",
                        ["originator"] = ev.User ?? "",
                        ["date_created"] = stamp,
                        ["file_name"] = "",
                        ["file_path"] = "",
                        ["source"] = "STING Revision Issue",
                    };
                    register.Add(row);
                    if (report != null) report.RegisterAdded++;
                }
                else if (report != null) report.RegisterUpdated++;

                if (!string.IsNullOrWhiteSpace(s.Revision)) row["revision"] = s.Revision.Trim();
                if (suit.Length > 0) row["suitability"] = suit;
                if (!string.IsNullOrEmpty(cde)) { row["status"] = cde; row["cde_status"] = cde; }
                row["date_issued"] = ev.IssuedDate ?? "";
                row["date_modified"] = stamp;
            }
        }

        // ── issues.json ──────────────────────────────────────────────────

        /// <summary>
        /// Propose resolution of open issues targeting the issued revision. The issue moves
        /// to RESPONDED with a note; it is never CLOSED here — closing is the raiser's
        /// decision (IssueStatusNormalizer: Responded = answered, awaiting acceptance).
        /// Issues already responded to, or terminal, are left alone.
        /// </summary>
        public static List<string> ProposeIssueResolutions(JArray issues, RevisionIssueEvent ev,
            DateTime now, IssueCompletionReport report = null)
        {
            var proposed = new List<string>();
            if (issues == null || ev == null || string.IsNullOrWhiteSpace(ev.RevisionCode)) return proposed;
            string rev = ev.RevisionCode.Trim();
            foreach (var issue in issues.OfType<JObject>())
            {
                var kind = IssueStatusNormalizer.Normalize(issue["status"]?.ToString());
                if (IssueStatusNormalizer.IsTerminal(kind) || kind == IssueStatusKind.Responded
                    || kind == IssueStatusKind.Resolved) continue;
                string target = FirstNonBlank(issue["target_revision"]?.ToString(), issue["revision"]?.ToString());
                if (!string.Equals(target, rev, StringComparison.OrdinalIgnoreCase)) continue;

                string note = $"Revision {rev} issued {ev.IssuedDate} to {ev.Sheets?.Count ?? 0} sheet(s) " +
                              $"by {ev.User}. Proposed as resolved — please review and close if accepted.";
                issue["status"] = IssueStatusNormalizer.Canonical(IssueStatusKind.Responded);
                issue["proposed_resolution"] = note;
                issue["proposed_in_revision"] = rev;
                issue["date_responded"] = now.ToString("yyyy-MM-dd HH:mm");
                if (!(issue["comments"] is JArray comments))
                {
                    comments = new JArray();
                    issue["comments"] = comments;
                }
                comments.Add(new JObject
                {
                    ["author"] = ev.User ?? "",
                    ["date"] = now.ToString("yyyy-MM-dd HH:mm"),
                    ["text"] = note,
                    ["source"] = "STING Revision Issue",
                });
                string id = FirstNonBlank(issue["issue_id"]?.ToString(), issue["id"]?.ToString()) ?? "(no id)";
                proposed.Add(id);
            }
            report?.IssuesProposed.AddRange(proposed);
            return proposed;
        }

        // ── helpers ──────────────────────────────────────────────────────

        internal static string NormaliseSuitability(string raw)
        {
            string c = Iso19650Suitability.ExtractCode(raw ?? "");
            return Iso19650Suitability.CdeStateFor(c) == null ? "" : c;
        }

        private static List<string> ReadStringList(JToken t)
        {
            var list = new List<string>();
            if (t is JArray a)
                list.AddRange(a.Select(x => x?.ToString()?.Trim()).Where(x => !string.IsNullOrEmpty(x)));
            else if (t != null && t.Type == JTokenType.String)
                list.AddRange(t.ToString().Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length > 0));
            return list;
        }

        private static string FirstNonBlank(params string[] v) => v.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
    }
}
