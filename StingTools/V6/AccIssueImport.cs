// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccIssueImport.cs — ACC Issues → STING issue register (BIM-BCF-SYNC-01, ACC half).
//
// WHY THIS EXISTS
// ---------------
// Coordinators raise issues in ACC. Every STING surface that counts issues — the BIM
// Coordination Center list, its KPIs, the workflow gate `has_open_issues` — reads the
// project register (IssueStore / CoordStores.Issues). Before this file, nothing put ACC
// issues there, so a project run from ACC showed a clean register while ACC held open
// issues. That is the failure mode this codebase keeps producing: a count that is
// smaller than the truth and says nothing about why.
//
// THE CONTRACT
// ------------
//  * Identity is the ACC issue id, stored on the row as `acc_issue_id`. One ACC issue →
//    at most one STING row, found under any status, so a closed row is updated rather
//    than duplicated.
//  * ACC owns five fields: title, description, status, assigned_to, date_due (ACC's dueDate;
//    a row imported before it was mapped has an implicit base of "" - see LegacyBase). Nothing
//    else on the row is touched after creation, so comments, linked transmittals and history that a
//    coordinator adds in STING survive every import.
//  * STING rows are NEVER deleted. An ACC issue that has disappeared from the container is
//    reported (MissingFromAcc) and left alone.
//  * CONFLICTS ARE REPORTED, NOT RESOLVED. Each row carries `acc_last_import` — the value
//    of each owned field as it was last imported. That is the common base of a three-way
//    compare, per field:
//        local == base, acc != base  → ACC changed it: update.
//        local != base, acc == base  → edited locally: keep the local value, no conflict,
//            REPORTED as LocalAhead ("changed in STING, not in ACC"). Nothing is written to
//            ACC by an import; ACC_PushIssueChanges (AccIssuePush.cs) sends it on request.
//        local != base, acc != base, local != acc → BOTH changed: CONFLICT. Local value kept,
//            base left where it was so the conflict is reported again next run until
//            somebody reconciles it. Recorded on the row as `acc_conflicts`.
//        local == acc                → agree: base advances.
//    A timestamp compare ("modified after the last import") was considered and rejected:
//    the importer's own status transitions stamp modified_date, and any unrelated local
//    edit (a comment) would make every owned field look contested.
//  * ROUND TRIP. STING itself creates ACC issues (clash escalation records signature →
//    ACC id in _BIM_COORD/acc/pushed_clashes.json). When one of those comes back, the
//    row is marked `acc_origin` + `acc_origin_key` and, if a STING row already carries
//    that key (`acc_origin_key` or `source_hash`), THAT row is linked to the ACC id rather
//    than a second one created.
//  * An INCREMENTAL pull (filter[updatedAt]) is merged with completePull: false - the rules
//    hold for every issue it returned, and MissingFromAcc is not computed at all.
//  * Otherwise the caller must only pass a COMPLETE pull. A partial read would make every unseen
//    issue look deleted — the defect AccFetchResult exists to prevent. The command checks
//    pull.Succeeded before calling this; this file cannot tell.
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
    /// <summary>
    /// The importer's own view of one ACC issue — decoupled from <see cref="AccIssue"/> so the
    /// transport can change shape (ACC Issues v1 camelCase) without the merge rules moving.
    /// </summary>
    public sealed class AccIssueImportRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        /// <summary>Raw ACC status (draft / open / pending / in_progress / in_review /
        /// completed / not_approved / in_dispute / closed).</summary>
        public string Status { get; set; } = string.Empty;
        public string IssueTypeId { get; set; } = string.Empty;
        /// <summary>ACC user / company / role id of the assignee. ACC returns an id, not a
        /// name; it is stored as given rather than guessed into a name.</summary>
        public string AssignedTo { get; set; } = string.Empty;
        /// <summary>user / company / role, as ACC reports it.</summary>
        public string AssignedToType { get; set; } = string.Empty;
        /// <summary>The assignee's display name, looked up in the project's member list
        /// (AccProjectMembers). Empty when it could not be looked up - never guessed.</summary>
        public string AssignedToName { get; set; } = string.Empty;
        public string LocationDescription { get; set; } = string.Empty;

        /// <summary>The human number ACC shows (#123). Bookkeeping, never a conflict.</summary>
        public string DisplayId { get; set; } = string.Empty;
        /// <summary>ACC's due date as yyyy-MM-dd, or empty. ACC-owned (see OwnedFields).</summary>
        public string DueDate { get; set; } = string.Empty;
        /// <summary>ACC's updatedAt, ISO 8601 UTC, or empty. Bookkeeping.</summary>
        public string UpdatedAt { get; set; } = string.Empty;
        /// <summary>Custom attribute definition id -> value text. Read to link an issue STING
        /// escalated back to its clash when the sidecar has lost it.</summary>
        public Dictionary<string, string> CustomAttributes { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>E6: ACC's createdAt, ISO 8601 UTC, or empty when ACC omitted it (ParseIssue
        /// no longer defaults it to the time of the pull).</summary>
        public string CreatedAt { get; set; } = string.Empty;
        /// <summary>E6: ACC's createdBy - a user id, as given.</summary>
        public string CreatedBy { get; set; } = string.Empty;
        /// <summary>E6: the creator's display name from the project member list; empty when it
        /// could not be looked up - never guessed.</summary>
        public string CreatedByName { get; set; } = string.Empty;

        public static AccIssueImportRecord From(AccIssue a)
        {
            if (a == null) return null;
            return new AccIssueImportRecord
            {
                Id = a.Id ?? string.Empty,
                Title = a.Title ?? string.Empty,
                Description = a.Description ?? string.Empty,
                Status = a.Status ?? string.Empty,
                IssueTypeId = a.IssueType ?? string.Empty,
                AssignedTo = a.AssignedToUserId ?? string.Empty,
                AssignedToType = a.AssignedToType ?? string.Empty,
                LocationDescription = a.LocationDescription ?? string.Empty,
                DisplayId = a.DisplayId ?? string.Empty,
                DueDate = a.DueDate.HasValue ? a.DueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty,
                UpdatedAt = a.UpdatedAt.HasValue
                    ? DateTime.SpecifyKind(a.UpdatedAt.Value, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)
                    : string.Empty,
                CreatedAt = a.CreatedAt.HasValue
                    ? DateTime.SpecifyKind(a.CreatedAt.Value, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)
                    : string.Empty,
                CreatedBy = a.CreatedBy ?? string.Empty,
                CustomAttributes = (a.CustomAttributes ?? new List<AccCustomAttributeValue>())
                    .Where(c => c != null && !string.IsNullOrEmpty(c.AttributeDefinitionId))
                    .GroupBy(c => c.AttributeDefinitionId, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First().ValueText, StringComparer.Ordinal),
            };
        }
    }

    /// <summary>An ACC issue STING itself created, recovered from a push sidecar.</summary>
    public sealed class AccOriginLink
    {
        /// <summary>What pushed it, e.g. "sting_clash_escalation".</summary>
        public string Origin { get; set; } = string.Empty;
        /// <summary>The pusher's own key — the clash signature for escalations.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Invert a sidecar (key → ACC id) into ACC id → link. First key wins on a
        /// duplicate ACC id so the result does not depend on dictionary order silently.</summary>
        public static void AddSidecar(IDictionary<string, AccOriginLink> into, string origin,
            IEnumerable<KeyValuePair<string, string>> keyToAccId)
        {
            if (into == null || keyToAccId == null) return;
            foreach (var kv in keyToAccId.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(kv.Value) || into.ContainsKey(kv.Value)) continue;
                into[kv.Value] = new AccOriginLink { Origin = origin, Key = kv.Key };
            }
        }

        /// <summary>Recover links from a custom attribute STING wrote on create (e.g. the clash
        /// signature). The sidecar wins: an ACC id it already names is not overwritten, so the
        /// two sources cannot disagree silently. Returns how many links were added.</summary>
        public static int AddFromAttribute(IDictionary<string, AccOriginLink> into, string origin,
            IEnumerable<AccIssueImportRecord> pulled, string attributeDefinitionId)
        {
            if (into == null || pulled == null || string.IsNullOrWhiteSpace(attributeDefinitionId)) return 0;
            int added = 0;
            foreach (var r in pulled)
            {
                string id = r?.Id?.Trim();
                if (string.IsNullOrEmpty(id) || into.ContainsKey(id)) continue;
                if (r.CustomAttributes == null || !r.CustomAttributes.TryGetValue(attributeDefinitionId, out string key)) continue;
                if (string.IsNullOrWhiteSpace(key)) continue;
                into[id] = new AccOriginLink { Origin = origin, Key = key.Trim() };
                added++;
            }
            return added;
        }
    }

    /// <summary>One field both sides changed since the last import.</summary>
    public sealed class AccImportConflict
    {
        public string IssueId { get; set; }
        public string AccIssueId { get; set; }
        public string Field { get; set; }
        /// <summary>Value at the last import; null when the row was never imported (a link).</summary>
        public string BaseValue { get; set; }
        public string LocalValue { get; set; }
        public string AccValue { get; set; }
    }

    /// <summary>One owned field changed in STING since the last import while ACC still holds
    /// the imported value: STING is AHEAD. The import keeps the local value and writes nothing
    /// to ACC; ACC_PushIssueChanges is the only thing that sends it.</summary>
    public sealed class AccLocalAhead
    {
        public string IssueId { get; set; }
        public string AccIssueId { get; set; }
        public string Field { get; set; }
        public string BaseValue { get; set; }
        public string LocalValue { get; set; }
        public string AccValue { get; set; }
    }

    /// <summary>What an import did. Every list is populated even when empty.</summary>
    public sealed class AccIssueImportResult
    {
        public int Pulled { get; set; }
        public List<JObject> Created { get; } = new List<JObject>();
        public List<JObject> Updated { get; } = new List<JObject>();
        /// <summary>Existing STING rows newly linked to an ACC id through the round-trip marker.</summary>
        public List<JObject> Linked { get; } = new List<JObject>();
        public int StatusChanges { get; set; }
        public int Unchanged { get; set; }
        public List<AccImportConflict> Conflicts { get; } = new List<AccImportConflict>();
        /// <summary>Fields changed in STING, not in ACC — kept locally, NOT sent to ACC.</summary>
        public List<AccLocalAhead> LocalAhead { get; } = new List<AccLocalAhead>();
        /// <summary>True when the pull was an updated-since window, so an issue it did not
        /// return is NOT evidence of deletion and <see cref="MissingFromAcc"/> was not computed.</summary>
        public bool Incremental { get; set; }
        /// <summary>STING issue ids whose ACC issue was not in the pull. Kept, never deleted.</summary>
        public List<string> MissingFromAcc { get; } = new List<string>();
        /// <summary>ACC records with no id (cannot be keyed) or repeated ids (pagination overlap).</summary>
        public int Skipped { get; set; }

        public bool AnyChange => Created.Count > 0 || Updated.Count > 0 || Linked.Count > 0;
    }

    /// <summary>
    /// Where the merge writes. The command backs this with an <see cref="IssueBatch"/> so new
    /// rows and status changes get the register's id minting, audit entries and server push;
    /// tests back it with a bare JArray.
    /// </summary>
    public interface IAccIssueWriter
    {
        string MintId(string type);
        /// <summary>Append a fully-built new row.</summary>
        void Add(JObject row);
        /// <summary>Transition status (canonical). Returns true when it changed.</summary>
        bool SetStatus(JObject row, string canonicalStatus, string note);
        /// <summary>A non-status field of <paramref name="row"/> was changed in place.</summary>
        void Touched(JObject row);
    }

    /// <summary>Pure writer over a JArray — the register without the Revit-bound store.</summary>
    public sealed class JArrayAccIssueWriter : IAccIssueWriter
    {
        private readonly JArray _rows;
        private readonly IssueIdMinter _minter;
        private readonly DateTime _now;
        private readonly string _user;

        public JArrayAccIssueWriter(JArray rows, DateTime now, string user)
        {
            _rows = rows ?? throw new ArgumentNullException(nameof(rows));
            _minter = new IssueIdMinter(_rows);
            _now = now;
            _user = user;
        }

        public string MintId(string type) => _minter.Next(type);
        public void Add(JObject row) => _rows.Add(row);
        public bool SetStatus(JObject row, string canonicalStatus, string note)
            => IssueSchema.ApplyStatus(row, canonicalStatus, _user, _now, note);
        public void Touched(JObject row) { }
    }

    public static class AccIssueImport
    {
        public const string AccIdField = "acc_issue_id";
        public const string BaseField = "acc_last_import";
        public const string ConflictsField = "acc_conflicts";
        /// <summary>ACC's assignee id / type / display name, kept beside the STING
        /// assigned_to field (which stays the ACC-owned, three-way-merged value).</summary>
        public const string AssignedIdField = "acc_assigned_to_id";
        public const string AssignedTypeField = "acc_assigned_to_type";
        public const string AssignedNameField = "acc_assigned_to_name";
        public const string OriginField = "acc_origin";
        public const string OriginKeyField = "acc_origin_key";
        public const string ClashEscalationOrigin = "sting_clash_escalation";
        public const string LifecycleGapOrigin = "sting_lifecycle_gap";

        public const string CommentsPushedField = "acc_comments_pushed";

        /// <summary>The fields ACC owns, as (row field, ACC value) pairs. Status is compared
        /// in canonical form so "in_review" → "pending" (both IN_PROGRESS) is not a change.
        /// date_due joined the set when ACC's dueDate was mapped (see LegacyBase).</summary>
        internal static readonly string[] OwnedFields = { "title", "description", "status", "assigned_to", "date_due" };

        /// <summary>The base a row imported BEFORE a field was owned implicitly had for it. The
        /// importer that predates date_due always wrote date_due = "", so that is its base; any
        /// other answer would report every ACC due date as a conflict on the first run after
        /// upgrading. Applies only to rows that already carried a base object.</summary>
        private static readonly Dictionary<string, string> LegacyBase =
            new Dictionary<string, string>(StringComparer.Ordinal) { ["date_due"] = "" };

        internal static string AccValue(AccIssueImportRecord a, string field) => field switch
        {
            "title" => a.Title ?? "",
            "description" => a.Description ?? "",
            "status" => IssueStatusNormalizer.CanonicalAcc(a.Status),
            "assigned_to" => a.AssignedTo ?? "",
            "date_due" => a.DueDate ?? "",
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "not an ACC-owned field"),
        };

        internal static string LocalValue(JObject row, string field)
            => field == "status" ? IssueSchema.StatusOf(row) : (row[field]?.ToString() ?? "");

        /// <summary>The recorded base for a field, honouring <see cref="LegacyBase"/>. Null
        /// when the row has never been imported for that field.</summary>
        internal static string BaseValue(JObject baseObj, string field, bool baseExisted)
        {
            if (baseObj?[field]?.Type == JTokenType.String) return (string)baseObj[field];
            if (baseExisted && baseObj != null && baseObj[field] == null && LegacyBase.TryGetValue(field, out string legacy))
                return legacy;
            return null;
        }

        /// <summary>
        /// Merge a COMPLETE ACC pull into the register rows. See the file header for the rules.
        /// </summary>
        /// <param name="rows">The live register (already migrated). Read to match; written only
        /// through <paramref name="writer"/> or by in-place field edits reported to it.</param>
        /// <param name="pulled">Every issue in the container. Must be a complete read.</param>
        /// <param name="origins">ACC id → the STING push that created it (may be null).</param>
        /// <param name="completePull">False for an updated-since (incremental) read: every rule
        /// above still holds for what WAS returned, but an issue that was not returned proves
        /// nothing, so MissingFromAcc is not computed.</param>
        public static AccIssueImportResult Merge(JArray rows, IEnumerable<AccIssueImportRecord> pulled,
            IReadOnlyDictionary<string, AccOriginLink> origins, IAccIssueWriter writer,
            DateTime now, string user, bool completePull = true)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            var result = new AccIssueImportResult { Incremental = !completePull };
            user = string.IsNullOrWhiteSpace(user) ? "unknown" : user;
            string stamp = now.ToString("o", CultureInfo.InvariantCulture);

            // Index the register by ACC id once. A row that somehow carries a duplicate ACC id
            // keeps the first; the second is never touched rather than both being rewritten.
            var byAccId = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (var r in rows.OfType<JObject>())
            {
                string aid = (string)r[AccIdField];
                if (!string.IsNullOrWhiteSpace(aid) && !byAccId.ContainsKey(aid.Trim())) byAccId[aid.Trim()] = r;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in pulled ?? Enumerable.Empty<AccIssueImportRecord>())
            {
                result.Pulled++;
                string id = a?.Id?.Trim();
                if (string.IsNullOrEmpty(id) || !seen.Add(id)) { result.Skipped++; continue; }

                AccOriginLink origin = null;
                origins?.TryGetValue(id, out origin);

                if (byAccId.TryGetValue(id, out JObject row))
                {
                    UpdateRow(row, a, origin, writer, result, user, stamp);
                    continue;
                }

                // Round trip: a STING row already describes the thing STING pushed.
                JObject linked = origin == null ? null : FindByOriginKey(rows, origin.Key);
                if (linked != null)
                {
                    linked[AccIdField] = id;
                    linked[OriginField] = origin.Origin;
                    linked[OriginKeyField] = origin.Key;
                    byAccId[id] = linked;
                    result.Linked.Add(linked);
                    // No base yet: every owned field that disagrees is a conflict, because
                    // nothing says which side is newer. Fields that agree get a base.
                    UpdateRow(linked, a, origin, writer, result, user, stamp, alreadyTouched: true);
                    continue;
                }

                JObject created = CreateRow(a, origin, writer, now, user, stamp);
                writer.Add(created);
                byAccId[id] = created;
                result.Created.Add(created);
            }

            // Report — never delete — rows whose ACC issue is gone. Only a complete pull can say so.
            foreach (var kv in byAccId)
                if (completePull && !seen.Contains(kv.Key))
                    result.MissingFromAcc.Add(IssueSchema.IdOf(kv.Value) ?? kv.Key);

            return result;
        }

        private static JObject FindByOriginKey(JArray rows, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            return rows.OfType<JObject>().FirstOrDefault(r =>
                string.IsNullOrWhiteSpace((string)r[AccIdField]) &&
                (string.Equals((string)r[OriginKeyField], key, StringComparison.Ordinal) ||
                 string.Equals((string)r["source_hash"], key, StringComparison.Ordinal)));
        }

        private static JObject CreateRow(AccIssueImportRecord a, AccOriginLink origin, IAccIssueWriter writer,
            DateTime now, string user, string stamp)
        {
            string type = origin?.Origin == ClashEscalationOrigin ? "CLASH" : "ACC";
            var spec = new IssueSpec
            {
                Type = type,
                Title = a.Title ?? "",
                Description = a.Description ?? "",
                AssignedTo = a.AssignedTo ?? "",
                Source = IssueSource.Acc,
                SourceHash = a.Id,
            };
            JObject row = IssueSchema.Create(spec, writer.MintId(type), now, user);

            // Create() writes OPEN and an SLA due date computed from priority. For an issue
            // that lives in ACC both would be invented: ACC's status is known, and ACC's due
            // date is ACC's (empty when ACC has none — never the invented SLA date).
            row["status"] = IssueStatusNormalizer.CanonicalAcc(a.Status);
            row["date_due"] = a.DueDate ?? "";
            if (!string.IsNullOrWhiteSpace(a.UpdatedAt)) row["acc_updated_at"] = a.UpdatedAt;
            if (!string.IsNullOrWhiteSpace(a.AssignedToType)) row["acc_assigned_to_type"] = a.AssignedToType;
            row[AccIdField] = a.Id;
            row["acc_status"] = a.Status ?? "";
            row["acc_issue_type_id"] = a.IssueTypeId ?? "";
            row["acc_location"] = a.LocationDescription ?? "";
            row[AssignedTypeField] = a.AssignedToType ?? "";
            row[AssignedIdField] = a.AssignedTo ?? "";
            if (!string.IsNullOrWhiteSpace(a.AssignedToName)) row[AssignedNameField] = a.AssignedToName;
            if (!string.IsNullOrWhiteSpace(a.DisplayId)) row["acc_display_id"] = a.DisplayId;
            if (origin != null)
            {
                row[OriginField] = origin.Origin;
                row[OriginKeyField] = origin.Key;
            }
            var baseObj = new JObject();
            foreach (string f in OwnedFields) baseObj[f] = AccValue(a, f);
            row[BaseField] = baseObj;
            row["acc_imported_at"] = stamp;
            ApplyAccCreation(row, a, backfill: true);
            // E6: ACC issues carry no priority. Create() wrote MEDIUM, and the SLA check then
            // ran a one-week clock on an issue whose urgency nobody stated. Blank, and marked.
            row["priority"] = "";
            row[PriorityDefaultedField] = true;
            return row;
        }

        /// <summary>E6: set when STING gave the row no priority because ACC carries none; the SLA
        /// check reports such a row as "no SLA" instead of assuming MEDIUM.</summary>
        public const string PriorityDefaultedField = "priority_defaulted";
        public const string CreatedAtField = "acc_created_at";
        public const string CreatedByField = "acc_created_by";

        /// <summary>E6: who raised the issue and when, from ACC - not "the importing user, now".
        /// <paramref name="backfill"/> rewrites the row's own created_date / date_raised /
        /// raised_by / created_by: always on create, and once for a row an earlier import created
        /// with the import time (no acc_created_at yet). A row STING raised itself keeps its own.
        /// A value ACC omitted is left blank - never filled with the time of the pull.</summary>
        private static bool ApplyAccCreation(JObject row, AccIssueImportRecord a, bool backfill)
        {
            bool touched = false;
            string created = (a.CreatedAt ?? "").Trim();
            string by = (a.CreatedBy ?? "").Trim();
            string byName = (a.CreatedByName ?? "").Trim();
            if (backfill)
            {
                DateTime utc = default;
                bool known = created.Length > 0 && DateTime.TryParse(created, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);
                string createdDate = known ? DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture) : "";
                string raised = known ? DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "";
                string raiser = byName.Length > 0 ? byName : by;
                touched |= SetIfDifferent(row, "created_date", createdDate);
                touched |= SetIfDifferent(row, "date_raised", raised);
                touched |= SetIfDifferent(row, "raised_by", raiser);
                touched |= SetIfDifferent(row, "created_by", raiser);
            }
            if (row[CreatedAtField] == null || created.Length > 0) touched |= SetIfDifferent(row, CreatedAtField, created);
            if (by.Length > 0) touched |= SetIfDifferent(row, CreatedByField, by);
            return touched;
        }

        private static void UpdateRow(JObject row, AccIssueImportRecord a, AccOriginLink origin,
            IAccIssueWriter writer, AccIssueImportResult result, string user, string stamp,
            bool alreadyTouched = false)
        {
            bool touched = alreadyTouched;
            bool statusChanged = false;
            var conflicts = new List<AccImportConflict>();

            JObject baseObj = row[BaseField] as JObject;
            bool baseExisted = baseObj != null;
            if (baseObj == null) { baseObj = new JObject(); row[BaseField] = baseObj; touched = true; }

            foreach (string f in OwnedFields)
            {
                string acc = AccValue(a, f);
                string local = LocalValue(row, f);
                string baseVal = BaseValue(baseObj, f, baseExisted);

                if (string.Equals(local, acc, StringComparison.Ordinal))
                {
                    if (!string.Equals(baseVal, acc, StringComparison.Ordinal)) { baseObj[f] = acc; touched = true; }
                    continue;
                }

                bool accChanged = baseVal == null || !string.Equals(acc, baseVal, StringComparison.Ordinal);
                bool localChanged = baseVal == null || !string.Equals(local, baseVal, StringComparison.Ordinal);

                if (accChanged && !localChanged)
                {
                    if (f == "status")
                    {
                        if (writer.SetStatus(row, acc, $"ACC import: {a.Status}")) statusChanged = true;
                    }
                    else
                    {
                        row[f] = acc;
                        touched = true;
                    }
                    baseObj[f] = acc;
                    touched = true;
                }
                else if (accChanged)
                {
                    conflicts.Add(new AccImportConflict
                    {
                        IssueId = IssueSchema.IdOf(row),
                        AccIssueId = a.Id,
                        Field = f,
                        BaseValue = baseVal,
                        LocalValue = local,
                        AccValue = acc,
                    });
                }
                else
                {
                    // Only the local side changed — the local edit stands, and is REPORTED:
                    // "changed in STING, not in ACC". Nothing is written to ACC here.
                    result.LocalAhead.Add(new AccLocalAhead
                    {
                        IssueId = IssueSchema.IdOf(row),
                        AccIssueId = a.Id,
                        Field = f,
                        BaseValue = baseVal,
                        LocalValue = local,
                        AccValue = acc,
                    });
                }
            }

            // ACC-only bookkeeping: always ACC's, never a conflict.
            touched |= SetIfDifferent(row, "acc_status", a.Status ?? "");
            touched |= SetIfDifferent(row, "acc_issue_type_id", a.IssueTypeId ?? "");
            touched |= SetIfDifferent(row, "acc_location", a.LocationDescription ?? "");
            touched |= SetIfChanged(row, AssignedTypeField, a.AssignedToType ?? "");
            // The name follows the id. A name that could not be looked up this run leaves the
            // last one only while the id is unchanged; a new id with no name clears it, so a
            // row never shows the previous assignee's name next to a new assignee's id.
            string prevAccAssignee = (string)row[AssignedIdField] ?? "";
            touched |= SetIfChanged(row, AssignedIdField, a.AssignedTo ?? "");
            if (!string.IsNullOrWhiteSpace(a.AssignedToName))
                touched |= SetIfDifferent(row, AssignedNameField, a.AssignedToName);
            else if (row[AssignedNameField] != null && !string.Equals(prevAccAssignee, a.AssignedTo ?? "", StringComparison.Ordinal))
            { row.Remove(AssignedNameField); touched = true; }
            if (!string.IsNullOrWhiteSpace(a.DisplayId)) touched |= SetIfDifferent(row, "acc_display_id", a.DisplayId);
            if (!string.IsNullOrWhiteSpace(a.UpdatedAt)) touched |= SetIfDifferent(row, "acc_updated_at", a.UpdatedAt);
            // E6: an ACC-sourced row an earlier import stamped with the import time and user is
            // corrected once (it has no acc_created_at yet); a STING-raised row keeps its own.
            bool importedRow = string.Equals((string)row["source"], IssueSchema.SourceName(IssueSource.Acc), StringComparison.Ordinal);
            touched |= ApplyAccCreation(row, a, backfill: importedRow && row[CreatedAtField] == null && !string.IsNullOrWhiteSpace(a.CreatedAt));
            if (origin != null && string.IsNullOrWhiteSpace((string)row[OriginField]))
            {
                row[OriginField] = origin.Origin;
                row[OriginKeyField] = origin.Key;
                touched = true;
            }

            // Persist the conflict set on the row, so the register shows it, not only this run's
            // report. Compared before writing so an unchanged conflict is not a change.
            JArray newConflicts = conflicts.Count == 0 ? null : new JArray(conflicts.Select(c => new JObject
            {
                ["field"] = c.Field,
                ["base"] = c.BaseValue,
                ["local"] = c.LocalValue,
                ["acc"] = c.AccValue,
            }));
            JToken oldConflicts = row[ConflictsField];
            if (newConflicts == null && oldConflicts != null) { row.Remove(ConflictsField); touched = true; }
            else if (newConflicts != null && !JToken.DeepEquals(oldConflicts, newConflicts))
            {
                row[ConflictsField] = newConflicts;
                touched = true;
            }
            result.Conflicts.AddRange(conflicts);

            if (touched || statusChanged)
            {
                row["acc_imported_at"] = stamp;
                if (touched)
                {
                    row["modified_by"] = user;
                    row["modified_date"] = stamp;
                }
                writer.Touched(row);
                if (!alreadyTouched) result.Updated.Add(row);
            }
            else result.Unchanged++;
            if (statusChanged) result.StatusChanges++;
        }

        /// <summary>As SetIfDifferent, but an empty value on a row that never had the field is
        /// not a change - rows imported before the field existed stay "unchanged".</summary>
        private static bool SetIfChanged(JObject row, string field, string value)
        {
            if (string.IsNullOrEmpty(value) && row[field] == null) return false;
            return SetIfDifferent(row, field, value);
        }

        private static bool SetIfDifferent(JObject row, string field, string value)
        {
            if (string.Equals(row[field]?.ToString() ?? "", value, StringComparison.Ordinal) && row[field] != null)
                return false;
            row[field] = value;
            return true;
        }
    }
}
