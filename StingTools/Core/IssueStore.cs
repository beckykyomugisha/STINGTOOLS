// ══════════════════════════════════════════════════════════════════════════
//  IssueStore.cs — the ONE repository for BIM issues. ISO IM runner, Phase 2.
//
//  Every writer converges here. Before this, nineteen call sites each did their
//  own load / mint / serialize / save against issues.json, which is how the
//  register ended up with three identifier spellings, five status vocabularies,
//  duplicate NCR-0001 rows, and a second issues.json in the export folder that
//  nothing ever read.
//
//  WHAT THIS OWNS
//    * Path       — always CoordStores.Issues(doc). Never a hand-rolled Combine.
//    * Load       — reads, then migrates every row to the canonical schema
//                   (IssueSchema.MigrateAll), so the issue_id/id/IssueId fork
//                   drains as stores are touched.
//    * Mint       — IssueIdMinter, reserved in memory for the whole batch. The
//                   Count+1-inside-the-loop duplicate-ID defect cannot recur:
//                   there is no other minting path left.
//    * Status     — IssueStatusNormalizer on every read and write.
//    * Dedup      — (source, source_hash) while the prior issue is still open.
//    * Persist    — CoordStores.WriteArray (atomic). Refuses to write when the
//                   store exists but is unreadable, so a locked file on a share
//                   cannot truncate a live register.
//    * Audit      — appends to the tamper-evident _BIM_COORD chain
//                   (Planscape.Docs.Workflow.AuditLog).
//    * Server     — fire-and-forget create/update push, mirroring the
//                   DeliverableServerSync.FireAndForget pattern.
//
//  USAGE — batch (the normal case; one load, one save, one audit pass):
//
//      using var batch = IssueStore.Begin(doc);
//      foreach (var w in warnings)
//          batch.Create(new IssueSpec { Type = "NCR", Title = w.Title,
//                                       Source = IssueSource.Warning,
//                                       SourceHash = w.Key });
//      int written = batch.Commit();
//
//  USAGE — single issue:
//
//      JObject issue = IssueStore.Raise(doc, new IssueSpec { … });
//
//  THREADING — Commit() takes a per-path lock, so two commands committing at
//  once serialize rather than last-writer-wins. Server pushes are dispatched
//  AFTER the lock is released, with the store path resolved on the calling
//  (Revit) thread so nothing touches the Revit API from the thread pool.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using Planscape.Docs.Workflow;
using StingTools.BIMManager;

namespace StingTools.Core
{
    /// <summary>Single repository for the project issue register.</summary>
    public static class IssueStore
    {
        // Per-store-path locks. Issue writes are infrequent; a small dictionary is ample.
        private static readonly Dictionary<string, object> _locks =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _locksGuard = new();

        internal static object LockFor(string path)
        {
            lock (_locksGuard)
            {
                if (!_locks.TryGetValue(path ?? "", out var l)) _locks[path ?? ""] = l = new object();
                return l;
            }
        }

        // ── Paths + loading ───────────────────────────────────────────────

        /// <summary>The canonical issues.json path. The only issue path in the codebase.</summary>
        public static string PathFor(Document doc) => CoordStores.Issues(doc);

        /// <summary>
        /// Load the register, migrated to the canonical schema. Returns an empty array when
        /// the store is absent OR unreadable — use <see cref="TryLoad"/> when you intend to
        /// write the result back.
        /// </summary>
        public static JArray Load(Document doc)
        {
            TryLoad(doc, out JArray rows);
            return rows;
        }

        /// <summary>
        /// Load for read-modify-write. False means the store exists but could not be read;
        /// the caller MUST NOT save, or it will overwrite a live register with a partial one.
        /// </summary>
        public static bool TryLoad(Document doc, out JArray rows)
        {
            rows = new JArray();
            string path = PathFor(doc);
            if (string.IsNullOrEmpty(path)) return true;
            if (!CoordStores.TryRead(path, out rows)) { rows = new JArray(); return false; }
            IssueSchema.MigrateAll(rows);
            return true;
        }

        // ── Reads (the shared predicates) ─────────────────────────────────

        /// <summary>Issues still needing attention. One predicate for KPIs, gates and dashboards.</summary>
        public static int OpenCount(Document doc) => IssueSchema.OpenCount(Load(doc));

        /// <summary>Workflow gate `has_open_issues`.</summary>
        public static bool HasOpen(Document doc) => Load(doc).OfType<JObject>().Any(IssueSchema.IsOpen);

        /// <summary>All open issues.</summary>
        public static List<JObject> Open(Document doc) =>
            Load(doc).OfType<JObject>().Where(IssueSchema.IsOpen).ToList();

        /// <summary>Look up one issue by identifier, under any historical spelling.</summary>
        public static JObject Get(Document doc, string issueId) => IssueSchema.FindById(Load(doc), issueId);

        // ── Single-issue writes ───────────────────────────────────────────

        /// <summary>
        /// Raise one issue. Returns the created record, or the EXISTING open record when the
        /// spec's (Source, SourceHash) already has one — so a repeated QA scan updates
        /// nothing rather than growing the register every run. Null when the store is
        /// unreadable.
        /// </summary>
        public static JObject Raise(Document doc, IssueSpec spec)
        {
            using var batch = Begin(doc);
            if (!batch.Ok) return null;
            JObject row = batch.Create(spec);
            batch.Commit();
            return row;
        }

        /// <summary>
        /// Transition an issue's status. Routes through IssueStatusNormalizer, stamps
        /// status_history, audits `issue.status_changed`, and pushes the change server-side
        /// when the issue is mirrored. Returns false when the issue is absent or already in
        /// that status.
        /// </summary>
        public static bool SetStatus(Document doc, string issueId, string newStatus,
                                     string note = null, string response = null)
        {
            using var batch = Begin(doc);
            if (!batch.Ok) return false;
            if (!batch.SetStatus(issueId, newStatus, note, response)) return false;
            batch.Commit();
            return true;
        }

        // ── Batch ─────────────────────────────────────────────────────────

        /// <summary>Open a batch: one load, many creates/updates, one atomic save.</summary>
        public static IssueBatch Begin(Document doc) => new IssueBatch(doc);

        // ── Server merge (pull side) ──────────────────────────────────────

        /// <summary>
        /// Upsert rows pulled from the Planscape server into the canonical register.
        ///
        /// Dedup is three-way, in order: <c>server_id</c> (GUID), then <c>server_code</c>
        /// (the issueCode we recorded when WE pushed the issue up), then <c>issue_id</c>.
        /// The middle step is what closes the create-then-pull loop — an issue this plugin
        /// created and pushed gets its server GUID filled in on the next pull instead of
        /// re-appearing as a duplicate.
        ///
        /// Returns the number of rows merged.
        /// </summary>
        public static int MergeFromServer(Document doc, JArray serverRows)
        {
            if (serverRows == null || serverRows.Count == 0) return 0;

            string path = PathFor(doc);
            if (string.IsNullOrEmpty(path)) return 0;

            lock (LockFor(path))
            {
                if (!CoordStores.TryRead(path, out JArray local))
                {
                    StingLog.Warn("IssueStore.MergeFromServer refused — issues.json unreadable; not overwriting it.");
                    return 0;
                }
                IssueSchema.MigrateAll(local);

                var minter = new IssueIdMinter(local);
                int merged = 0;

                foreach (var srv in serverRows.OfType<JObject>())
                {
                    string sid = (string)srv["id"] ?? "";
                    string code = (string)srv["issueCode"] ?? "";
                    if (string.IsNullOrWhiteSpace(sid) && string.IsNullOrWhiteSpace(code)) continue;

                    JObject existing =
                        IssueSchema.FindByServerId(local, sid)
                        ?? local.OfType<JObject>().FirstOrDefault(r =>
                               !string.IsNullOrWhiteSpace(code) &&
                               string.Equals((string)r["server_code"], code, StringComparison.OrdinalIgnoreCase))
                        ?? IssueSchema.FindById(local, code);

                    JObject mapped = MapServerRow(srv, existing, minter);

                    if (existing != null)
                    {
                        // Preserve local-only enrichment the server does not own.
                        foreach (string keep in new[] { "comments", "linked_transmittals", "status_history",
                                                        "source_hash", "cost_impact_ugx" })
                            if (existing[keep] != null && mapped[keep] == null)
                                mapped[keep] = existing[keep].DeepClone();

                        int idx = local.IndexOf(existing);
                        if (idx >= 0) local[idx] = mapped; else local.Add(mapped);
                    }
                    else local.Add(mapped);

                    merged++;
                }

                if (merged > 0) CoordStores.WriteArray(path, local);
                return merged;
            }
        }

        /// <summary>Map one server DTO (camelCase) onto the canonical schema.</summary>
        private static JObject MapServerRow(JObject srv, JObject existing, IssueIdMinter minter)
        {
            string sid = (string)srv["id"] ?? "";
            string code = (string)srv["issueCode"] ?? "";
            string assignee = (string)srv["assignee"] ?? "";
            string created = (string)srv["createdAt"] ?? (string)srv["created_date"] ?? "";

            // Keep whatever identifier the register already knows this issue by, so local
            // cross-links (linked_transmittals, revision back-refs) keep resolving.
            string id = existing != null ? IssueSchema.IdOf(existing) : null;
            if (string.IsNullOrWhiteSpace(id)) id = string.IsNullOrWhiteSpace(code) ? minter.Next("SRV") : code;

            var elems = new JArray();
            string linkedRaw = (string)srv["linkedElementIds"] ?? "";
            if (!string.IsNullOrWhiteSpace(linkedRaw) && linkedRaw.TrimStart().StartsWith("["))
            {
                try { elems = JArray.Parse(linkedRaw); }
                catch (Exception ex) { StingLog.Warn($"IssueStore: linkedElementIds parse: {ex.Message}"); }
            }

            var row = new JObject
            {
                [IssueSchema.IdField]  = id,
                ["server_id"]          = sid,
                ["server_code"]        = code,
                ["type"]               = (string)srv["type"] ?? "",
                ["type_description"]   = (string)srv["type"] ?? "",
                ["priority"]           = (string)srv["priority"] ?? "MEDIUM",
                ["title"]              = (string)srv["title"] ?? "",
                ["description"]        = (string)srv["description"] ?? "",
                // The server speaks its own status vocabulary ("Open", "open", "New").
                // Normalising HERE is what makes server-pulled issues visible to the
                // has_open_issues gate and the BCC KPI counts.
                ["status"]             = IssueStatusNormalizer.Canonical((string)srv["status"] ?? "OPEN"),
                ["assigned_to"]        = assignee,
                ["discipline"]         = (string)srv["discipline"] ?? "",
                ["revision"]           = (string)srv["revision"] ?? "",
                ["raised_by"]          = (string)srv["createdBy"] ?? "",
                ["created_by"]         = (string)srv["createdBy"] ?? "",
                ["created_date"]       = created,
                ["modified_date"]      = (string)srv["updatedAt"] ?? created,
                ["date_due"]           = (string)srv["dueDate"] ?? "",
                ["date_closed"]        = (string)srv["resolvedAt"] ?? "",
                ["source"]             = IssueSchema.SourceName(IssueSource.Server),
                ["element_ids"]        = elems,
                ["model_id"]           = (string)srv["modelId"] ?? "",
                ["model_element_guid"] = (string)srv["modelElementGuid"] ?? "",
            };

            if (srv["latitude"] != null)  row["latitude"]  = srv["latitude"].DeepClone();
            if (srv["longitude"] != null) row["longitude"] = srv["longitude"].DeepClone();

            return row;
        }

        // ── Server push (create/update side) ──────────────────────────────

        /// <summary>
        /// Mirror a newly-created issue to the server. Fire-and-forget: returns immediately,
        /// the HTTP call runs on the thread pool, and a failure logs a warning and is dropped
        /// (the periodic sync tick retries). Modelled on DeliverableServerSync.FireAndForget.
        ///
        /// The store PATH is resolved on the calling thread and captured as a string, so the
        /// background continuation never touches the Revit API.
        /// </summary>
        internal static void PushCreateFireAndForget(Document doc, JObject row)
        {
            try
            {
                if (row == null) return;
                Guid projectId = ResolvePlanscapeProjectId(doc);
                if (projectId == Guid.Empty) return;   // not connected — nothing to do

                string issueId    = IssueSchema.IdOf(row);
                string type       = (string)row["type"] ?? "RFI";
                string title      = (string)row["title"] ?? "";
                string priority   = (string)row["priority"] ?? "MEDIUM";
                string assignee   = (string)row["assigned_to"] ?? "";
                string discipline = (string)row["discipline"] ?? "";
                if (string.IsNullOrWhiteSpace(title)) return;

                var elementIds = new List<long>();
                if (row["element_ids"] is JArray ea)
                    foreach (var t in ea)
                        if (long.TryParse(t?.ToString(), out long v)) elementIds.Add(v);

                string pathCapture = PathFor(doc);   // resolved on the Revit thread

                _ = Task.Run(async () =>
                {
                    try
                    {
                        string code = await PlanscapeServerClient.Instance.CreateIssueAsync(
                            projectId, type, title, priority, assignee, discipline, elementIds);

                        if (string.IsNullOrEmpty(code))
                        {
                            StingLog.Warn($"IssueStore: server create for {issueId} failed: " +
                                          $"{PlanscapeServerClient.Instance.LastError}");
                            return;
                        }
                        // Record the code so the next pull matches this row instead of
                        // duplicating it (see MergeFromServer's three-way dedup).
                        StampServerCode(pathCapture, issueId, code);
                    }
                    catch (Exception ex) { StingLog.Warn($"IssueStore server create: {ex.Message}"); }
                });
            }
            catch (Exception ex) { StingLog.Warn($"IssueStore.PushCreateFireAndForget: {ex.Message}"); }
        }

        /// <summary>Mirror a status change to the server. Only meaningful once we know the GUID.</summary>
        internal static void PushUpdateFireAndForget(Document doc, JObject row, string newStatus)
        {
            try
            {
                if (row == null) return;
                string serverId = IssueSchema.ServerIdOf(row);
                if (string.IsNullOrWhiteSpace(serverId)) return;         // local-only issue
                if (!Guid.TryParse(serverId, out Guid issueGuid)) return;

                Guid projectId = ResolvePlanscapeProjectId(doc);
                if (projectId == Guid.Empty) return;

                string issueId = IssueSchema.IdOf(row);
                var patch = new { status = IssueStatusNormalizer.Canonical(newStatus) };

                _ = Task.Run(async () =>
                {
                    try
                    {
                        bool ok = await PlanscapeServerClient.Instance
                            .UpdateIssueAsync(projectId, issueGuid, patch);
                        if (!ok)
                            StingLog.Warn($"IssueStore: server update for {issueId} failed: " +
                                          $"{PlanscapeServerClient.Instance.LastError}");
                    }
                    catch (Exception ex) { StingLog.Warn($"IssueStore server update: {ex.Message}"); }
                });
            }
            catch (Exception ex) { StingLog.Warn($"IssueStore.PushUpdateFireAndForget: {ex.Message}"); }
        }

        /// <summary>
        /// Push every locally-raised issue the server has never seen.
        ///
        /// The create path already fire-and-forgets a push, but that is dropped when the
        /// server is unreachable — so an issue raised offline would never reach it. This is
        /// the catch-up pass, called on connect and on each sync tick, mirroring
        /// DeliverableServerSync.ReconcileAsync.
        ///
        /// Absorbs the one capability that MobileIssueBridge had and nothing else did
        /// (pushing local-only rows upward). That class is retired: it had no callers, and
        /// it carried its own seventh schema variant — "MOB-" prefixed ids, `assignee`
        /// instead of `assigned_to`, un-normalised server statuses — so anything it wrote
        /// would have been a fresh fork of the register.
        ///
        /// Bounded per call so a never-synced project cannot hammer the server; the next
        /// tick mops up the next batch.
        /// </summary>
        public static async Task<int> ReconcileToServerAsync(Document doc, int max = 50)
        {
            try
            {
                Guid projectId = ResolvePlanscapeProjectId(doc);
                if (projectId == Guid.Empty) return 0;

                string path = PathFor(doc);
                if (string.IsNullOrEmpty(path)) return 0;
                if (!CoordStores.TryRead(path, out JArray rows)) return 0;
                IssueSchema.MigrateAll(rows);

                // Never-synced = no server GUID AND no server-assigned code.
                var pending = rows.OfType<JObject>()
                    .Where(r => string.IsNullOrWhiteSpace(IssueSchema.ServerIdOf(r)) &&
                                string.IsNullOrWhiteSpace((string)r["server_code"]) &&
                                !string.Equals(IssueSchema.SourceOf(r), "server", StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrWhiteSpace((string)r["title"]))
                    .Take(max).ToList();
                if (pending.Count == 0) return 0;

                int pushed = 0;
                foreach (var row in pending)
                {
                    var elementIds = new List<long>();
                    if (row["element_ids"] is JArray ea)
                        foreach (var t in ea)
                            if (long.TryParse(t?.ToString(), out long v)) elementIds.Add(v);

                    string code = await PlanscapeServerClient.Instance.CreateIssueAsync(
                        projectId,
                        (string)row["type"] ?? "RFI",
                        (string)row["title"] ?? "",
                        (string)row["priority"] ?? "MEDIUM",
                        (string)row["assigned_to"] ?? "",
                        (string)row["discipline"] ?? "",
                        elementIds);

                    if (string.IsNullOrEmpty(code))
                    {
                        // Stop on first failure rather than burning round-trips against a
                        // server that is throwing — the next tick retries.
                        StingLog.Warn($"IssueStore.Reconcile stopped: {PlanscapeServerClient.Instance.LastError}");
                        break;
                    }
                    row["server_code"] = code;
                    pushed++;
                }

                if (pushed > 0)
                {
                    lock (LockFor(path)) { CoordStores.WriteArray(path, rows); }
                    StingLog.Info($"IssueStore.ReconcileToServer pushed {pushed} issue(s).");
                }
                return pushed;
            }
            catch (Exception ex) { StingLog.Warn($"IssueStore.ReconcileToServerAsync: {ex.Message}"); return 0; }
        }

        /// <summary>Write the server-assigned issue code back onto a local row. File I/O only.</summary>
        private static void StampServerCode(string path, string issueId, string code)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrWhiteSpace(issueId)) return;
            try
            {
                lock (LockFor(path))
                {
                    if (!CoordStores.TryRead(path, out JArray rows)) return;
                    var row = IssueSchema.FindById(rows, issueId);
                    if (row == null) return;
                    if (string.Equals((string)row["server_code"], code, StringComparison.Ordinal)) return;
                    row["server_code"] = code;
                    CoordStores.WriteArray(path, rows);
                }
            }
            catch (Exception ex) { StingLog.Warn($"IssueStore.StampServerCode: {ex.Message}"); }
        }

        /// <summary>Planscape project GUID for this model, or Guid.Empty when not connected.</summary>
        internal static Guid ResolvePlanscapeProjectId(Document doc)
        {
            try
            {
                string bimDir = ProjectFolderEngine.GetMetaPath(doc, "STING_BIM_MANAGER");
                if (string.IsNullOrEmpty(bimDir)) return Guid.Empty;
                string cfg = Path.Combine(bimDir, "planscape_connection.json");
                return PlatformSyncCommand.LoadPlanscapeProjectId(cfg);
            }
            catch (Exception ex) { StingLog.Warn($"IssueStore.ResolvePlanscapeProjectId: {ex.Message}"); return Guid.Empty; }
        }

        // ── Audit ─────────────────────────────────────────────────────────

        /// <summary>
        /// Audit action for a creation, chosen by provenance so the tamper-evident chain
        /// distinguishes an escalated warning from a human-raised RFI. Every entry also
        /// carries <c>source</c> in its payload, so a query by provenance works uniformly
        /// regardless of which action name was used.
        /// </summary>
        internal static string CreateAction(IssueSource source) => source switch
        {
            IssueSource.Warning => "issue.escalated_from_warning",
            IssueSource.Clash   => "issue.from_clash",
            _                   => "issue.raised",
        };
    }

    /// <summary>
    /// One load / many writes / one atomic save against the issue register.
    ///
    /// Holds a single <see cref="IssueIdMinter"/> for its lifetime, which is why identifiers
    /// cannot collide within a batch — the defect that made a multi-group warning scan emit
    /// NCR-0001 repeatedly.
    /// </summary>
    public sealed class IssueBatch : IDisposable
    {
        private readonly Document _doc;
        private readonly string _path;
        private readonly JArray _rows;
        private readonly IssueIdMinter _minter;
        private readonly List<JObject> _created = new();
        private readonly List<(JObject Row, string From, string To)> _transitions = new();
        private readonly DateTime _now = DateTime.Now;
        private readonly string _user = Environment.UserName ?? "unknown";
        private bool _committed;

        /// <summary>False when the store exists but is unreadable — do not write.</summary>
        public bool Ok { get; }

        /// <summary>Records created in this batch (empty before Create is called).</summary>
        public IReadOnlyList<JObject> Created => _created;

        internal IssueBatch(Document doc)
        {
            _doc = doc;
            _path = IssueStore.PathFor(doc);
            Ok = IssueStore.TryLoad(doc, out _rows);
            if (!Ok)
                StingLog.Warn("IssueBatch: issues.json exists but is unreadable — batch will not save.");
            _minter = new IssueIdMinter(_rows);
        }

        /// <summary>The live register, migrated. Read-only use — mutate through the batch API.</summary>
        public JArray Rows => _rows;

        /// <summary>
        /// Create an issue. Returns the EXISTING open record when this spec's
        /// (Source, SourceHash) already has one, so re-running a scan does not grow the
        /// register. The returned row is appended to <see cref="Created"/> only when new.
        /// </summary>
        public JObject Create(IssueSpec spec)
        {
            if (!Ok || spec == null) return null;

            if (!string.IsNullOrWhiteSpace(spec.SourceHash))
            {
                var dup = IssueSchema.FindOpenByDedupKey(_rows, spec.Source, spec.SourceHash);
                if (dup != null) return dup;
            }
            if (!string.IsNullOrWhiteSpace(spec.ServerId))
            {
                var dup = IssueSchema.FindByServerId(_rows, spec.ServerId);
                if (dup != null) return dup;
            }

            string id = _minter.Next(spec.Type);
            JObject row = IssueSchema.Create(spec, id, _now, _user);
            _rows.Add(row);
            _created.Add(row);
            return row;
        }

        /// <summary>Transition an issue's status. Returns false when absent or unchanged.</summary>
        public bool SetStatus(string issueId, string newStatus, string note = null, string response = null)
        {
            if (!Ok) return false;
            var row = IssueSchema.FindById(_rows, issueId);
            if (row == null) return false;

            string from = IssueSchema.StatusOf(row);
            if (!IssueSchema.ApplyStatus(row, newStatus, _user, _now, note)) return false;
            if (response != null) row["response"] = response;

            _transitions.Add((row, from, IssueSchema.StatusOf(row)));
            return true;
        }

        /// <summary>
        /// Persist atomically, append the audit entries, then dispatch server pushes.
        /// Returns the number of rows created. Safe to call once; a second call is a no-op.
        /// </summary>
        public int Commit()
        {
            if (!Ok || _committed) return 0;
            _committed = true;

            if (_created.Count == 0 && _transitions.Count == 0) return 0;

            lock (IssueStore.LockFor(_path))
            {
                CoordStores.WriteArray(_path, _rows);
            }

            // Audit + server push happen after the store is durable, so a crash mid-push
            // leaves the register correct rather than the audit chain claiming a row exists.
            foreach (var row in _created)
            {
                string source = IssueSchema.SourceOf(row);
                SafeAudit(IssueStore.CreateAction(
                              Enum.TryParse(source, true, out IssueSource s) ? s : IssueSource.Manual),
                          IssueSchema.IdOf(row),
                          new JObject
                          {
                              ["issue_id"]    = IssueSchema.IdOf(row),
                              ["type"]        = (string)row["type"],
                              ["title"]       = (string)row["title"],
                              ["priority"]    = (string)row["priority"],
                              ["status"]      = IssueSchema.StatusOf(row),
                              ["discipline"]  = (string)row["discipline"],
                              ["source"]      = source,
                              ["source_hash"] = (string)row["source_hash"] ?? "",
                          });
                IssueStore.PushCreateFireAndForget(_doc, row);
            }

            foreach (var (row, from, to) in _transitions)
            {
                SafeAudit("issue.status_changed", IssueSchema.IdOf(row),
                          new JObject
                          {
                              ["issue_id"] = IssueSchema.IdOf(row),
                              ["from"]     = from,
                              ["to"]       = to,
                              ["source"]   = IssueSchema.SourceOf(row),
                          });
                IssueStore.PushUpdateFireAndForget(_doc, row, to);
            }

            if (_created.Count > 0)
                StingLog.Info($"IssueStore: created {_created.Count} issue(s); " +
                              $"{_transitions.Count} status change(s).");
            return _created.Count;
        }

        /// <summary>Audit failures must never take down the write that already succeeded.</summary>
        private void SafeAudit(string action, string id, JObject payload)
        {
            try { AuditLog.Append(_doc, action, id, payload); }
            catch (Exception ex) { StingLog.Warn($"IssueStore audit '{action}': {ex.Message}"); }
        }

        /// <summary>
        /// Commits if the caller has not already. A batch that created nothing is a no-op, and
        /// a second Commit is a no-op, so an explicit Commit() plus scope exit is safe.
        ///
        /// This commits on EXCEPTION paths too, deliberately: each created issue is an
        /// independently valid record, and losing a batch of legitimately-raised issues
        /// because an unrelated later step threw is worse than persisting them. There is no
        /// partial-record risk — a row is only added to the batch once fully built.
        /// </summary>
        public void Dispose()
        {
            if (!_committed) Commit();
        }
    }
}
