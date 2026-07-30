// ══════════════════════════════════════════════════════════════════════════
//  IssueSchema.cs — the canonical BIM-issue record. ISO IM runner, Phase 2
//  (ROADMAP IM-4 / IM-5).
//
//  WHY THIS EXISTS
//  ---------------
//  Before this file, nineteen writers created issue rows in seven mutually
//  incompatible shapes. Three identifier spellings coexisted in one store:
//
//      "issue_id"  BIMManagerEngine.CreateIssue, BCF import, RaiseIssue, …
//      "id"        every warnings/gap auto-escalation path
//      "IssueId"   LpsAutoIssueRaiser (whole record PascalCase)
//
//  Readers picked one. `BIMManagerCommands` looked up "issue_id" only, so an
//  auto-escalated warning was invisible to the register that is supposed to
//  manage it; the CSV export looked up "id" only, so a manually-raised RFI
//  exported with a blank identifier. Neither failed loudly — both just showed
//  a shorter list than the truth.
//
//  Three writers also minted identifiers as `existingIssues.Count + 1` INSIDE
//  their creation loop, so the count never advanced until the batch was saved
//  and a multi-group scan emitted NCR-0001 several times over.
//
//  THE CONTRACT
//  ------------
//    * ONE canonical identifier field: "issue_id".
//    * READS accept all three spellings (IdOf) so pre-existing stores keep
//      working untouched. WRITES emit "issue_id" and nothing else.
//    * Migrate()/MigrateAll() upgrade a legacy row in place when a store is
//      loaded, so the fork drains as stores are touched rather than needing a
//      migration command.
//    * Status is always persisted through IssueStatusNormalizer.Canonical, so
//      the has_open_issues gate, the BCC KPIs and every dashboard count share
//      one predicate.
//    * IDs are minted by IssueIdMinter, which reserves in memory across a
//      whole batch — the fix for the Count+1 defect.
//
//  Pure: no Autodesk.Revit.*, no file I/O. Unit-tested in
//  StingTools.Tags.Tests/IssueSchemaTests.cs. The Revit-facing repository that
//  owns paths, persistence, audit and server push is Core/IssueStore.cs.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    /// <summary>
    /// Where an issue came from. Persisted as the lowercase <c>source</c> field so a
    /// coordinator can tell an auto-escalated warning from a human-raised RFI.
    /// </summary>
    public enum IssueSource
    {
        /// <summary>Raised by a person through a command or dialog.</summary>
        Manual,
        /// <summary>Escalated from a model warning / QA scan.</summary>
        Warning,
        /// <summary>Raised from a clash-detection result.</summary>
        Clash,
        /// <summary>Pulled from Autodesk Construction Cloud.</summary>
        Acc,
        /// <summary>Raised by the lightning-protection compliance engine.</summary>
        Lps,
        /// <summary>Pulled from the Planscape server (incl. mobile capture).</summary>
        Server,
        /// <summary>Imported from a BCF 2.1 exchange file.</summary>
        Bcf,
        /// <summary>Raised by an ISO 19650 compliance / handover-gap scan.</summary>
        Compliance,
    }

    /// <summary>Everything a caller must supply to raise one issue.</summary>
    public sealed class IssueSpec
    {
        /// <summary>Identifier prefix and register bucket — RFI / TQ / NCR / CLASH / SI…</summary>
        public string Type { get; set; } = "RFI";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Priority { get; set; } = "MEDIUM";
        public string AssignedTo { get; set; } = "";
        public string Discipline { get; set; } = "";
        public string ViewName { get; set; } = "";
        public string Revision { get; set; } = "";

        /// <summary>Provenance. Persisted as <c>source</c>.</summary>
        public IssueSource Source { get; set; } = IssueSource.Manual;

        /// <summary>
        /// Stable identity of the THING that caused this issue — a warning-group key, a
        /// clash id, a BCF GUID. Two calls with the same (Source, SourceHash) describe the
        /// same problem, so <see cref="IssueStore"/> will not raise a second issue while
        /// the first is still open. Leave null to disable dedup for this issue.
        /// </summary>
        public string SourceHash { get; set; }

        /// <summary>Revit element ids as raw longs (kept Revit-free on purpose).</summary>
        public List<long> ElementIds { get; set; } = new List<long>();

        /// <summary>Server GUID when this issue originated server-side; used to dedupe pulls.</summary>
        public string ServerId { get; set; }

        /// <summary>Extra fields merged onto the record verbatim (clash_id, bcf_guid, …).</summary>
        public JObject Extra { get; set; }
    }

    /// <summary>
    /// Mints issue identifiers monotonically across a whole batch.
    ///
    /// The defect this replaces (ROADMAP IM-5): three writers computed
    /// <c>existingIssues.Count + 1</c> inside their per-warning loop. The array is not
    /// appended to until the batch is saved, so every issue in one scan got the SAME
    /// number — and after any deletion the count collides with live rows anyway.
    ///
    /// This scans the store ONCE for the high-water mark per prefix, then hands out
    /// strictly increasing ids from memory, skipping any that already exist under any
    /// identifier spelling.
    /// </summary>
    public sealed class IssueIdMinter
    {
        private readonly Dictionary<string, int> _high = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _taken = new(StringComparer.OrdinalIgnoreCase);

        public IssueIdMinter(JArray rows)
        {
            foreach (var row in (rows ?? new JArray()).OfType<JObject>())
            {
                string id = IssueSchema.IdOf(row);
                if (string.IsNullOrWhiteSpace(id)) continue;
                _taken.Add(id);

                // "NCR-0042" → prefix "NCR", ordinal 42. Anything else contributes only
                // to the taken-set, so a free-form id can never be handed out twice.
                int dash = id.LastIndexOf('-');
                if (dash <= 0 || dash == id.Length - 1) continue;
                string prefix = id.Substring(0, dash);
                if (!int.TryParse(id.Substring(dash + 1), NumberStyles.Integer,
                                  CultureInfo.InvariantCulture, out int n)) continue;
                if (!_high.TryGetValue(prefix, out int cur) || n > cur) _high[prefix] = n;
            }
        }

        /// <summary>Next free id for a type, e.g. "NCR-0007". Never returns the same value twice.</summary>
        public string Next(string type)
        {
            string prefix = string.IsNullOrWhiteSpace(type) ? "ISS" : type.Trim();
            _high.TryGetValue(prefix, out int n);
            string candidate;
            do { candidate = $"{prefix}-{++n:D4}"; } while (_taken.Contains(candidate));
            _high[prefix] = n;
            _taken.Add(candidate);
            return candidate;
        }
    }

    /// <summary>Canonical issue-record schema: identity, migration, creation, queries.</summary>
    public static class IssueSchema
    {
        /// <summary>The one identifier field new writes emit.</summary>
        public const string IdField = "issue_id";

        // Identifier spellings tolerated on READ, in precedence order. "id" is second
        // because a server-mapped row carries both and its "issue_id" holds the
        // human-readable issue code, which is the one a coordinator recognises.
        private static readonly string[] IdFields = { "issue_id", "id", "IssueId", "Id" };

        // ── Identity ──────────────────────────────────────────────────────

        /// <summary>The row's identifier under any of the three historical spellings.</summary>
        public static string IdOf(JObject row)
        {
            if (row == null) return null;
            foreach (string k in IdFields)
            {
                string v = row[k]?.Type == JTokenType.String ? (string)row[k] : row[k]?.ToString();
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
            return null;
        }

        /// <summary>Canonical status of a row (accepts the PascalCase LPS spelling too).</summary>
        public static string StatusOf(JObject row)
        {
            if (row == null) return IssueStatusNormalizer.Canonical(IssueStatusKind.Unknown);
            string raw = (string)(row["status"] ?? row["Status"]) ?? "";
            return IssueStatusNormalizer.Canonical(raw);
        }

        /// <summary>
        /// True when the issue still needs attention. THE single predicate — the BCC KPIs,
        /// the workflow gate `has_open_issues` and every dashboard count route through here
        /// rather than scanning serialized JSON for the literal "OPEN".
        /// </summary>
        public static bool IsOpen(JObject row) =>
            IssueStatusNormalizer.IsOpen((string)(row?["status"] ?? row?["Status"]) ?? "");

        /// <summary>Server GUID if this row is mirrored server-side.</summary>
        public static string ServerIdOf(JObject row)
        {
            string v = (string)(row?["server_id"]);
            return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        }

        /// <summary>Provenance token, lowercase; "manual" when a legacy row carries none.</summary>
        public static string SourceOf(JObject row)
        {
            string v = (string)(row?["source"]);
            return string.IsNullOrWhiteSpace(v) ? "manual" : v.Trim().ToLowerInvariant();
        }

        public static string SourceName(IssueSource s) => s.ToString().ToLowerInvariant();

        // ── Migration ─────────────────────────────────────────────────────

        // Legacy field → canonical field. Applied only when the canonical slot is empty,
        // so a row that already carries both keeps the canonical value.
        private static readonly (string From, string To)[] FieldAliases =
        {
            // PascalCase LPS shape (LpsAutoIssueRaiser).
            ("IssueId",         "issue_id"),
            ("Title",           "title"),
            ("Status",          "status"),
            ("Priority",        "priority"),
            ("Type",            "type"),
            ("Description",     "description"),
            ("DateRaised",      "date_raised"),
            ("DateDue",         "date_due"),
            ("DateClosed",      "date_closed"),
            ("RaisedBy",        "raised_by"),
            ("ClosedBy",        "closed_by"),
            ("Origin",          "origin"),
            ("RelatedCheck",    "related_check"),
            ("RelatedStandard", "related_standard"),
            ("ElementIds",      "element_ids"),
            // DocumentManagementDialog shape.
            ("date",            "date_raised"),
            ("closed_date",     "date_closed"),
            ("linked_elements", "element_ids"),
            // Warnings-escalation shape.
            ("assignee",        "assigned_to"),
            ("affected_elements", "element_ids"),
        };

        /// <summary>
        /// Upgrade one legacy row to the canonical schema, in place. Idempotent.
        /// Returns true when anything changed.
        ///
        /// Deliberately conservative: an alias is applied only when its canonical slot is
        /// absent or blank, and the alias key is removed only once its value has landed.
        /// Nothing is ever dropped.
        /// </summary>
        public static bool Migrate(JObject row)
        {
            if (row == null) return false;
            bool changed = false;

            // 1. Identifier — hoist whichever spelling is present onto issue_id.
            string id = IdOf(row);
            if (!string.IsNullOrWhiteSpace(id) && (string)row[IdField] != id)
            {
                row[IdField] = id;
                changed = true;
            }

            // 2. Field aliases.
            foreach (var (from, to) in FieldAliases)
            {
                if (from == to) continue;
                JToken src = row[from];
                if (src == null || src.Type == JTokenType.Null) continue;

                JToken dst = row[to];
                bool dstEmpty = dst == null || dst.Type == JTokenType.Null ||
                                (dst.Type == JTokenType.String && string.IsNullOrWhiteSpace((string)dst));
                if (dstEmpty) { row[to] = src.DeepClone(); changed = true; }
                row.Remove(from);
                changed = true;
            }

            // 3. Drop the duplicate "id" mirror once issue_id carries the value. Leaving it
            //    is what let the two halves of the register drift apart in the first place.
            if (row["id"] != null && !string.IsNullOrWhiteSpace((string)row[IdField]))
            {
                if (string.Equals((string)row["id"], (string)row[IdField], StringComparison.Ordinal))
                {
                    row.Remove("id");
                    changed = true;
                }
            }

            // 4. element_ids as a comma-joined STRING (WarningsEngine built its JSON by
            //    string concatenation) → a real array, so element lookups stop silently
            //    matching nothing.
            if (row["element_ids"]?.Type == JTokenType.String)
            {
                string raw = (string)row["element_ids"] ?? "";
                row["element_ids"] = new JArray(
                    raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim()).Where(s => s.Length > 0).Cast<object>().ToArray());
                changed = true;
            }

            // 5. Status.
            //
            // Migration DELIBERATELY does not rewrite a status it does not recognise.
            // The register carries states this normalizer has no kind for — "RESPONDED"
            // and "ACCEPTED" are both written by UpdateIssueCommand and filtered on
            // exactly elsewhere — and canonicalising them here would collapse them to
            // "UNKNOWN", destroying a distinction the workflow depends on.
            //
            // Canonical spelling is enforced where it is safe to do so instead:
            //   * on CREATE   (IssueSchema.Create always writes "OPEN"),
            //   * on TRANSITION (ApplyStatus writes the canonical form), and
            //   * on READ     (StatusOf / IsOpen normalise on the fly, so the gate and the
            //                  KPI counts agree regardless of what is on disk).
            //
            // An absent status is the one case worth filling in: the row predates the
            // field, and OPEN is what IsOpen already infers for it.
            string rawStatus = (string)row["status"] ?? "";
            if (string.IsNullOrWhiteSpace(rawStatus))
            {
                row["status"] = "OPEN";
                changed = true;
            }

            // 6. Provenance — legacy rows carry none; auto_created marks the escalation paths.
            if (row["source"] == null || string.IsNullOrWhiteSpace((string)row["source"]))
            {
                bool auto = row["auto_created"]?.Type == JTokenType.Boolean && (bool)row["auto_created"];
                bool fromWarning = row["source_warning"] != null || row["warning_category"] != null;
                row["source"] = (auto || fromWarning) ? SourceName(IssueSource.Warning)
                                                      : SourceName(IssueSource.Manual);
                changed = true;
            }

            return changed;
        }

        /// <summary>Migrate every row of a store. Returns how many changed.</summary>
        public static int MigrateAll(JArray rows)
        {
            int n = 0;
            foreach (var row in (rows ?? new JArray()).OfType<JObject>())
                if (Migrate(row)) n++;
            return n;
        }

        // ── Creation ──────────────────────────────────────────────────────

        /// <summary>Human-readable descriptions for the register's issue types.</summary>
        private static readonly Dictionary<string, string> TypeDescriptions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["RFI"]   = "Request for Information",
                ["TQ"]    = "Technical Query",
                ["NCR"]   = "Non-Conformance Report",
                ["CLASH"] = "Clash / Coordination",
                ["SI"]    = "Site Instruction",
                ["BCF"]   = "BCF Imported Issue",
                ["LPS"]   = "Lightning Protection",
            };

        /// <summary>SLA due-date offset in days, by priority.</summary>
        private static int DueDays(string priority) =>
            (priority ?? "").Trim().ToUpperInvariant() switch
            {
                "CRITICAL" => 1,
                "HIGH"     => 3,
                "MEDIUM"   => 7,
                _          => 14,
            };

        /// <summary>
        /// Build one canonical record. <paramref name="id"/> comes from
        /// <see cref="IssueIdMinter"/>; <paramref name="now"/> is injected so the whole
        /// batch shares a timestamp and so tests are deterministic.
        /// </summary>
        public static JObject Create(IssueSpec spec, string id, DateTime now, string user)
        {
            spec ??= new IssueSpec();
            string type = string.IsNullOrWhiteSpace(spec.Type) ? "RFI" : spec.Type.Trim();
            string priority = string.IsNullOrWhiteSpace(spec.Priority) ? "MEDIUM" : spec.Priority.Trim().ToUpperInvariant();
            user = string.IsNullOrWhiteSpace(user) ? "unknown" : user;

            var row = new JObject
            {
                [IdField]              = id,
                ["type"]               = type,
                ["type_description"]   = TypeDescriptions.TryGetValue(type, out string td) ? td : type,
                ["priority"]           = priority,
                ["title"]              = spec.Title ?? "",
                ["description"]        = spec.Description ?? "",
                ["status"]             = IssueStatusNormalizer.Canonical(IssueStatusKind.Open),
                ["assigned_to"]        = spec.AssignedTo ?? "",
                ["discipline"]         = spec.Discipline ?? "",
                ["raised_by"]          = user,
                ["created_by"]         = user,
                ["created_date"]       = now.ToString("o", CultureInfo.InvariantCulture),
                ["modified_by"]        = user,
                ["modified_date"]      = now.ToString("o", CultureInfo.InvariantCulture),
                ["date_raised"]        = now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                ["date_due"]           = now.AddDays(DueDays(priority)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["date_closed"]        = "",
                ["response"]           = "",
                ["element_ids"]        = new JArray(spec.ElementIds?.Select(v => v.ToString(CultureInfo.InvariantCulture)).Cast<object>().ToArray()
                                                    ?? Array.Empty<object>()),
                ["view_name"]          = spec.ViewName ?? "",
                ["revision"]           = spec.Revision ?? "",
                ["resolved_in_revision"] = "",
                ["linked_transmittals"] = new JArray(),
                ["comments"]           = new JArray(),
                ["source"]             = SourceName(spec.Source),
            };

            if (!string.IsNullOrWhiteSpace(spec.SourceHash)) row["source_hash"] = spec.SourceHash;
            if (!string.IsNullOrWhiteSpace(spec.ServerId))   row["server_id"]   = spec.ServerId;

            if (spec.Extra != null)
                foreach (var p in spec.Extra.Properties())
                    row[p.Name] = p.Value?.DeepClone();

            return row;
        }

        // ── Queries ───────────────────────────────────────────────────────

        /// <summary>Find a row by identifier under any spelling.</summary>
        public static JObject FindById(JArray rows, string id)
        {
            if (rows == null || string.IsNullOrWhiteSpace(id)) return null;
            return rows.OfType<JObject>()
                       .FirstOrDefault(r => string.Equals(IdOf(r), id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Find a row already mirrored from the given server GUID.</summary>
        public static JObject FindByServerId(JArray rows, string serverId)
        {
            if (rows == null || string.IsNullOrWhiteSpace(serverId)) return null;
            return rows.OfType<JObject>()
                       .FirstOrDefault(r => string.Equals(ServerIdOf(r), serverId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Find a still-open issue already raised for the same (source, source_hash).
        /// This is what stops a QA scan re-raising the same warning every run.
        /// </summary>
        public static JObject FindOpenByDedupKey(JArray rows, IssueSource source, string sourceHash)
        {
            if (rows == null || string.IsNullOrWhiteSpace(sourceHash)) return null;
            string src = SourceName(source);
            return rows.OfType<JObject>().FirstOrDefault(r =>
                string.Equals((string)r["source_hash"], sourceHash, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(SourceOf(r), src, StringComparison.OrdinalIgnoreCase) &&
                IsOpen(r));
        }

        /// <summary>Count of issues still needing attention.</summary>
        public static int OpenCount(JArray rows) =>
            (rows ?? new JArray()).OfType<JObject>().Count(IsOpen);

        /// <summary>Apply a status transition to a row, stamping history. Returns true when changed.</summary>
        public static bool ApplyStatus(JObject row, string newStatus, string user, DateTime now, string note)
        {
            if (row == null) return false;
            string from = StatusOf(row);
            string to = IssueStatusNormalizer.Canonical(newStatus);
            if (string.Equals(from, to, StringComparison.Ordinal)) return false;

            row["status"] = to;
            row["modified_by"] = user ?? "unknown";
            row["modified_date"] = now.ToString("o", CultureInfo.InvariantCulture);

            var kind = IssueStatusNormalizer.Normalize(to);
            if (kind == IssueStatusKind.Closed || kind == IssueStatusKind.Void)
                row["date_closed"] = now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

            if (row["status_history"] is not JArray hist) { hist = new JArray(); row["status_history"] = hist; }
            hist.Add(new JObject
            {
                ["from"] = from,
                ["to"] = to,
                ["by"] = user ?? "unknown",
                ["at"] = now.ToString("o", CultureInfo.InvariantCulture),
                ["note"] = note ?? "",
            });
            return true;
        }
    }
}
