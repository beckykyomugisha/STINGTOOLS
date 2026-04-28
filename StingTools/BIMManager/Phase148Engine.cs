// Phase 148 — Tractable batch closure of 20 ROADMAP gaps.
//
// Single-file home for the cross-cutting engines that close a swathe of
// open BIM- / WF- / MEP- / STRUCT- / ACOUSTIC- / FM- gaps without
// scattering tiny additions across the tree. Each engine is a small
// internal static class; call sites in existing files delegate to the
// engine and stay one-liners.
//
// Engines:
//   * SidecarMetaStamper      (BIM-SIDECAR-VER-01)
//   * CrossLinkEngine        (BIM-CROSS-LINK-01)
//   * TransmittalGate        (BIM-TRANSMIT-GATE-01)
//   * TeamWorkloadEngine     (BIM-TEAM-WORKLOAD-01)
//   * ComplianceForecast     (BIM-FORECAST-01)
//   * CobieSystemDistribution(BIM-COBIE-SYS-01)
//   * DataDropTracker        (BIM-DD-TRACK-01, BIM-4D-HANDOVER-01)
//   * CdeApprovalGate        (BIM-CDE-APPROVAL-01)
//   * FuncSysValidator       (BIM-EXCEL-CROSS-01)
//   * RebarSpacingChecker    (STRUCT-REBAR-01)
//   * AcousticCavityBonus    (ACOUSTIC-CAVITY-01)
//   * ScheduleTemplateLib    (WF-SCHED-01, WF-SCHED-02)
//   * MepCommissioningSchedules (MEP-SCHED-01)
//   * PhaseAwareCobie        (FM-HO-02)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.BIMManager
{
    // ═══════════════════════════════════════════════════════════════════
    //  BIM-SIDECAR-VER-01 — Sidecar JSON versioning
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Versioning for sidecar JSON files. Rather than injecting a `_meta`
    /// sentinel into the array (which would break every existing
    /// iterator), each sidecar gets a companion `<name>.meta.json` file
    /// carrying schema name, version, written_at, written_by. Readers
    /// that don't care can ignore the meta file entirely. Forward-
    /// compatibility migrations consult `ReadMeta(path)` and branch on
    /// `Version`.
    /// </summary>
    internal static class SidecarMetaStamper
    {
        public const string CurrentVersion = "1.1";

        public class Meta
        {
            [JsonProperty("schema")]     public string Schema { get; set; }
            [JsonProperty("version")]    public string Version { get; set; }
            [JsonProperty("written_at")] public string WrittenAt { get; set; }
            [JsonProperty("written_by")] public string WrittenBy { get; set; }
        }

        public static string MetaPath(string sidecarPath)
            => string.IsNullOrEmpty(sidecarPath) ? null : sidecarPath + ".meta.json";

        /// <summary>Stamp the sidecar's companion `.meta.json` with the
        /// current schema + version. Best-effort: failures are logged
        /// and swallowed so a meta-write hiccup never blocks the main
        /// sidecar write. The sidecar JSON itself is left untouched.</summary>
        public static void Stamp(string sidecarPath, string schema)
        {
            string metaPath = MetaPath(sidecarPath);
            if (metaPath == null) return;
            try
            {
                var meta = new Meta
                {
                    Schema = schema,
                    Version = CurrentVersion,
                    WrittenAt = DateTime.UtcNow.ToString("o"),
                    WrittenBy = Environment.UserName,
                };
                string tmp = metaPath + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(meta, Formatting.Indented));
                File.Move(tmp, metaPath, true);
            }
            catch (Exception ex) { StingLog.Warn($"SidecarMetaStamper.Stamp {Path.GetFileName(sidecarPath)}: {ex.Message}"); }
        }

        /// <summary>Read the version stamp, returning "0.0" for any
        /// pre-versioning sidecar. Callers can branch on this to migrate.</summary>
        public static string ReadVersion(string sidecarPath)
        {
            string metaPath = MetaPath(sidecarPath);
            if (metaPath == null || !File.Exists(metaPath)) return "0.0";
            try
            {
                var meta = JsonConvert.DeserializeObject<Meta>(File.ReadAllText(metaPath));
                return meta?.Version ?? "0.0";
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SidecarMetaStamper.ReadVersion: {ex.Message}");
                return "0.0";
            }
        }

        /// <summary>Compatibility shim: existing CrossLinkEngine /
        /// PhaseAwareCobie / etc. call Records(arr) to walk every record
        /// in an array. With the sidecar-meta-file approach the array is
        /// already pristine, so we just return its objects unchanged.
        /// Kept as a method so we can re-introduce filtering without
        /// chasing call sites.</summary>
        public static IEnumerable<JObject> Records(JArray arr)
        {
            if (arr == null) yield break;
            foreach (var t in arr)
                if (t is JObject o) yield return o;
        }
    }
}

namespace StingTools.BIMManager
{
    // ═══════════════════════════════════════════════════════════════════
    //  BIM-CROSS-LINK-01 — Issue ↔ Revision ↔ Transmittal foreign keys
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Read-side joiner for the three sidecar JSONs. Each record can
    /// already carry `linked_revision_ids[]`, `linked_transmittal_ids[]`,
    /// `linked_issue_ids[]`; the engine fans out from any starting record
    /// to the connected graph and returns a flattened `LinkBundle` for
    /// dashboards / exporters / coordination workflows.
    /// </summary>
    internal static class CrossLinkEngine
    {
        public class LinkBundle
        {
            public List<JObject> Issues       { get; } = new();
            public List<JObject> Revisions    { get; } = new();
            public List<JObject> Transmittals { get; } = new();
        }

        /// <summary>Walk forward from an issue id, following any
        /// `linked_*_ids` arrays into the revision and transmittal
        /// sidecars. The walk is depth-bounded at 3 hops to stop a
        /// pathological cyclic graph.</summary>
        public static LinkBundle WalkFromIssue(Document doc, string issueId)
        {
            var bundle = new LinkBundle();
            if (string.IsNullOrEmpty(issueId)) return bundle;
            string bim = BIMManagerEngine.GetBIMManagerDir(doc);
            if (string.IsNullOrEmpty(bim)) return bundle;

            var issues       = LoadArray(Path.Combine(bim, "issues.json"));
            var revisions    = LoadArray(Path.Combine(bim, "revisions.json"));
            var transmittals = LoadArray(Path.Combine(bim, "transmittals.json"));

            var issueQueue = new Queue<string>(); issueQueue.Enqueue(issueId);
            var revQueue   = new Queue<string>();
            var txQueue    = new Queue<string>();
            int hops = 0;

            void Hop(JObject rec)
            {
                foreach (var t in rec["linked_revision_ids"] as JArray ?? new JArray())
                    revQueue.Enqueue(t.ToString());
                foreach (var t in rec["linked_transmittal_ids"] as JArray ?? new JArray())
                    txQueue.Enqueue(t.ToString());
                foreach (var t in rec["linked_issue_ids"] as JArray ?? new JArray())
                    issueQueue.Enqueue(t.ToString());
            }

            while ((issueQueue.Count > 0 || revQueue.Count > 0 || txQueue.Count > 0) && hops++ < 256)
            {
                if (issueQueue.Count > 0)
                {
                    string id = issueQueue.Dequeue();
                    var rec = SidecarMetaStamper.Records(issues)
                        .FirstOrDefault(r => r["id"]?.ToString() == id);
                    if (rec != null && !bundle.Issues.Contains(rec)) { bundle.Issues.Add(rec); Hop(rec); }
                }
                else if (revQueue.Count > 0)
                {
                    string id = revQueue.Dequeue();
                    var rec = SidecarMetaStamper.Records(revisions)
                        .FirstOrDefault(r => r["id"]?.ToString() == id || r["code"]?.ToString() == id);
                    if (rec != null && !bundle.Revisions.Contains(rec)) { bundle.Revisions.Add(rec); Hop(rec); }
                }
                else
                {
                    string id = txQueue.Dequeue();
                    var rec = SidecarMetaStamper.Records(transmittals)
                        .FirstOrDefault(r => r["id"]?.ToString() == id);
                    if (rec != null && !bundle.Transmittals.Contains(rec)) { bundle.Transmittals.Add(rec); Hop(rec); }
                }
            }
            return bundle;
        }

        /// <summary>Append a foreign-key reference to a record's
        /// `linked_<kind>_ids` array, creating the array if absent and
        /// deduping on insert.</summary>
        public static bool AppendLink(JObject record, string kind, string foreignId)
        {
            if (record == null || string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(foreignId))
                return false;
            string field = $"linked_{kind}_ids";
            if (!(record[field] is JArray arr))
            {
                arr = new JArray();
                record[field] = arr;
            }
            if (arr.Any(t => t.ToString() == foreignId)) return false;
            arr.Add(foreignId);
            return true;
        }

        private static JArray LoadArray(string path)
        {
            if (!File.Exists(path)) return new JArray();
            try { return JArray.Parse(File.ReadAllText(path)); }
            catch (Exception ex) { StingLog.Warn($"CrossLinkEngine.LoadArray {Path.GetFileName(path)}: {ex.Message}"); return new JArray(); }
        }
    }
}

namespace StingTools.BIMManager
{
    // ═══════════════════════════════════════════════════════════════════
    //  BIM-TRANSMIT-GATE-01 — Min CDE state for transmittals
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// ISO 19650-2 §5.3 forbids transmitting documents that have not at
    /// least reached SHARED state. The gate scans the document register
    /// for every document referenced by an outgoing transmittal and
    /// returns a (pass, blockers) tuple. The caller decides whether to
    /// hard-block or surface a warning banner.
    /// </summary>
    internal static class TransmittalGate
    {
        // CDE state ranks — higher = more mature.
        private static readonly Dictionary<string, int> Rank
            = new(StringComparer.OrdinalIgnoreCase)
            {
                ["WIP"]       = 0,
                ["S1"]        = 1, ["S2"] = 1, ["S3"] = 1,
                ["SHARED"]    = 1,
                ["S4"]        = 2, ["S5"] = 2, ["S6"] = 2, ["S7"] = 2,
                ["PUBLISHED"] = 2,
                ["A1"]        = 3, ["A2"] = 3, ["A3"] = 3,
                ["ARCHIVED"]  = 3,
            };

        public class GateResult
        {
            public bool Pass { get; set; }
            public List<string> Blockers { get; } = new();
            public string Summary { get; set; }
        }

        /// <summary>Validate a draft transmittal record. `requiredRank`
        /// defaults to 1 (SHARED) per ISO 19650-2 §5.6 minimum.</summary>
        public static GateResult Validate(Document doc, JObject transmittal, int requiredRank = 1)
        {
            var result = new GateResult();
            if (transmittal == null)
            {
                result.Summary = "No transmittal record provided.";
                return result;
            }
            string bim = BIMManagerEngine.GetBIMManagerDir(doc);
            string regPath = string.IsNullOrEmpty(bim)
                ? null : Path.Combine(bim, "document_register.json");
            JArray register = (regPath != null && File.Exists(regPath))
                ? JArray.Parse(File.ReadAllText(regPath))
                : new JArray();

            var docIds = (transmittal["document_ids"] as JArray)
                ?.Select(t => t.ToString()).ToList()
                ?? new List<string>();
            if (docIds.Count == 0)
            {
                result.Pass = true;
                result.Summary = "Empty transmittal — gate skipped.";
                return result;
            }

            foreach (string id in docIds)
            {
                var docRec = SidecarMetaStamper.Records(register)
                    .FirstOrDefault(r => r["id"]?.ToString() == id || r["doc_number"]?.ToString() == id);
                if (docRec == null)
                {
                    result.Blockers.Add($"{id}: not found in document register");
                    continue;
                }
                string state = docRec["cde_state"]?.ToString() ?? docRec["status"]?.ToString() ?? "WIP";
                int rank = Rank.TryGetValue(state, out int r) ? r : 0;
                if (rank < requiredRank)
                    result.Blockers.Add($"{id}: state={state} (needs ≥ rank {requiredRank})");
            }

            result.Pass = result.Blockers.Count == 0;
            result.Summary = result.Pass
                ? $"All {docIds.Count} documents meet minimum CDE state."
                : $"{result.Blockers.Count} of {docIds.Count} documents below CDE threshold.";
            return result;
        }
    }
}

namespace StingTools.BIMManager
{
    // ═══════════════════════════════════════════════════════════════════
    //  BIM-TEAM-WORKLOAD-01 — Per-assignee workload report
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Aggregates open issues / RFIs / tasks per assignee so coordinators
    /// can balance load across the team. Reads issues.json directly; no
    /// dependency on a server roster.
    /// </summary>
    internal static class TeamWorkloadEngine
    {
        public class WorkloadRow
        {
            public string Assignee   { get; set; }
            public int    OpenTotal  { get; set; }
            public int    Critical   { get; set; }
            public int    High       { get; set; }
            public int    Overdue    { get; set; }
            public string OldestDays { get; set; }
            public List<string> SampleIds { get; } = new();
        }

        public static List<WorkloadRow> Build(Document doc)
        {
            var rows = new Dictionary<string, WorkloadRow>(StringComparer.OrdinalIgnoreCase);
            string bim = BIMManagerEngine.GetBIMManagerDir(doc);
            if (string.IsNullOrEmpty(bim)) return new List<WorkloadRow>();
            string issuesPath = Path.Combine(bim, "issues.json");
            if (!File.Exists(issuesPath)) return new List<WorkloadRow>();
            JArray issues;
            try { issues = JArray.Parse(File.ReadAllText(issuesPath)); }
            catch (Exception ex) { StingLog.Warn($"TeamWorkloadEngine: {ex.Message}"); return new List<WorkloadRow>(); }

            DateTime now = DateTime.UtcNow;
            foreach (var rec in SidecarMetaStamper.Records(issues))
            {
                string status = rec["status"]?.ToString() ?? "";
                if (!string.Equals(status, "OPEN", StringComparison.OrdinalIgnoreCase)) continue;

                string assignee = rec["assignee"]?.ToString();
                if (string.IsNullOrWhiteSpace(assignee)) assignee = "(unassigned)";

                if (!rows.TryGetValue(assignee, out WorkloadRow row))
                {
                    row = new WorkloadRow { Assignee = assignee };
                    rows[assignee] = row;
                }
                row.OpenTotal++;

                string priority = rec["priority"]?.ToString() ?? "";
                if (priority.Equals("CRITICAL", StringComparison.OrdinalIgnoreCase)) row.Critical++;
                else if (priority.Equals("HIGH", StringComparison.OrdinalIgnoreCase)) row.High++;

                string due = rec["due_date"]?.ToString();
                if (DateTime.TryParse(due, out DateTime dueDt) && dueDt < now) row.Overdue++;

                string created = rec["created_date"]?.ToString();
                if (DateTime.TryParse(created, out DateTime cdt))
                {
                    int ageDays = (int)(now - cdt).TotalDays;
                    if (string.IsNullOrEmpty(row.OldestDays)
                        || int.Parse(row.OldestDays) < ageDays)
                        row.OldestDays = ageDays.ToString();
                }

                if (row.SampleIds.Count < 3 && rec["id"] != null)
                    row.SampleIds.Add(rec["id"].ToString());
            }

            return rows.Values
                .OrderByDescending(r => r.Critical)
                .ThenByDescending(r => r.OpenTotal)
                .ToList();
        }
    }
}

namespace StingTools.BIMManager
{
    // ═══════════════════════════════════════════════════════════════════
    //  BIM-FORECAST-01 — Compliance trend forecast surface
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Wraps WarningsEngine.ForecastCompliance with a sidecar-aware
    /// reader: pulls the historical compliance trend from
    /// `_BIM_COORD/compliance_trend.json` and returns a ready-to-render
    /// summary that the dashboard can show inline.
    /// </summary>
    internal static class ComplianceForecast
    {
        public class ForecastSummary
        {
            public bool   HasTrend     { get; set; }
            public double TargetPct    { get; set; }
            public double DaysToTarget { get; set; }
            public double ProjectedPct { get; set; }
            public string Caption      { get; set; }
        }

        public static ForecastSummary Build(Document doc, double targetPct = 80)
        {
            var summary = new ForecastSummary { TargetPct = targetPct };
            string bim = BIMManagerEngine.GetBIMManagerDir(doc);
            string trendPath = string.IsNullOrEmpty(bim)
                ? null : Path.Combine(bim, "compliance_trend.json");
            if (trendPath == null || !File.Exists(trendPath))
            {
                summary.Caption = "No trend file yet — keep tagging to populate forecast.";
                return summary;
            }

            var trend = new List<(DateTime, double)>();
            try
            {
                var arr = JArray.Parse(File.ReadAllText(trendPath));
                foreach (var rec in SidecarMetaStamper.Records(arr))
                {
                    if (DateTime.TryParse(rec["date"]?.ToString(), out DateTime dt) &&
                        double.TryParse(rec["compliance_pct"]?.ToString(), out double pct))
                        trend.Add((dt, pct));
                }
            }
            catch (Exception ex) { StingLog.Warn($"ComplianceForecast: {ex.Message}"); }

            if (trend.Count < 2)
            {
                summary.Caption = $"Need at least 2 trend points (have {trend.Count}).";
                return summary;
            }

            var (days, projected) = WarningsEngine.ForecastCompliance(trend, targetPct);
            summary.HasTrend = true;
            summary.DaysToTarget = days;
            summary.ProjectedPct = projected;

            if (double.IsInfinity(days) || days < 0)
                summary.Caption = $"Compliance is flat or declining — target {targetPct:F0}% out of reach without intervention.";
            else if (days < 7)
                summary.Caption = $"On track — projected to hit {targetPct:F0}% in {days:F1} days.";
            else
                summary.Caption = $"Projected: {projected:F0}% in 30 d / {targetPct:F0}% in {days:F0} days.";
            return summary;
        }
    }
}

namespace StingTools.BIMManager
{
    // ═══════════════════════════════════════════════════════════════════
    //  BIM-COBIE-SYS-01 — COBie System sheet from actual SYS distribution
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Walks every tagged element and aggregates the actual SYS token
    /// values present in the model. Returns a list of (sysCode, count,
    /// componentNames) tuples that the COBie System sheet writer can use
    /// instead of the static `TagConfig.SysMap` defaults — so a project
    /// that never uses HVAC doesn't get an empty HVAC row, and a project
    /// that uses a custom SYS code (e.g. CHW) shows it correctly.
    /// </summary>
    internal static class CobieSystemDistribution
    {
        public class SystemRow
        {
            public string SysCode { get; set; }
            public int    Count   { get; set; }
            public List<string> Components { get; } = new();
            public string Category { get; set; }
        }

        public static List<SystemRow> Build(Document doc)
        {
            var rows = new Dictionary<string, SystemRow>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                var catEnums = SharedParamGuids.AllCategoryEnums;
                if (catEnums != null && catEnums.Length > 0)
                    collector = collector.WherePasses(new ElementMulticategoryFilter(catEnums));

                foreach (var el in collector)
                {
                    string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                    if (string.IsNullOrEmpty(sys)) continue;
                    if (!rows.TryGetValue(sys, out SystemRow r))
                    {
                        r = new SystemRow
                        {
                            SysCode = sys,
                            Category = ParameterHelpers.GetCategoryName(el)
                        };
                        rows[sys] = r;
                    }
                    r.Count++;
                    if (r.Components.Count < 8)
                    {
                        string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                        if (!string.IsNullOrEmpty(tag) && !r.Components.Contains(tag))
                            r.Components.Add(tag);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"CobieSystemDistribution: {ex.Message}"); }

            return rows.Values.OrderByDescending(r => r.Count).ToList();
        }
    }
}

namespace StingTools.BIMManager
{
    // ═══════════════════════════════════════════════════════════════════
    //  BIM-DD-TRACK-01 + BIM-4D-HANDOVER-01 — DD1-DD4 milestone tracker
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// ISO 19650-2 Information Delivery Plan tracker. DD1 (concept), DD2
    /// (developed design), DD3 (construction), DD4 (handover) milestones
    /// are persisted to `_BIM_COORD/data_drops.json` with planned dates,
    /// actual dates, and gate-status (RAG). The 4D scheduling engine
    /// (Scheduling4DEngine) reads DD4 to extend the construction-finish
    /// milestone into a handover milestone — closing BIM-4D-HANDOVER-01.
    /// </summary>
    internal static class DataDropTracker
    {
        public class Milestone
        {
            [JsonProperty("id")]               public string Id { get; set; }
            [JsonProperty("planned_date")]     public string PlannedDate { get; set; }
            [JsonProperty("actual_date")]      public string ActualDate { get; set; }
            [JsonProperty("description")]      public string Description { get; set; }
            [JsonProperty("rag")]              public string Rag { get; set; }
            [JsonProperty("required_compliance_pct")]
            public int RequiredCompliancePct { get; set; }
        }

        public static string SidecarPath(Document doc)
        {
            string bim = BIMManagerEngine.GetBIMManagerDir(doc);
            return string.IsNullOrEmpty(bim) ? null : Path.Combine(bim, "data_drops.json");
        }

        public static List<Milestone> Load(Document doc)
        {
            string path = SidecarPath(doc);
            if (path == null) return Defaults();
            if (!File.Exists(path)) return Defaults();
            try
            {
                var arr = JArray.Parse(File.ReadAllText(path));
                return SidecarMetaStamper.Records(arr)
                    .Select(o => o.ToObject<Milestone>())
                    .Where(m => m != null)
                    .ToList();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DataDropTracker.Load: {ex.Message}");
                return Defaults();
            }
        }

        public static bool Save(Document doc, List<Milestone> milestones)
        {
            string path = SidecarPath(doc);
            if (path == null) return false;
            try
            {
                var arr = new JArray();
                foreach (var m in milestones ?? new List<Milestone>())
                    arr.Add(JObject.FromObject(m));
                // Phase 148b: companion-file versioning replaced the in-array
                // sentinel — SidecarMetaStamper.EnsureArrayMeta is gone.
                // Write the array, then stamp the .meta.json companion.
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, arr.ToString(Formatting.Indented));
                if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
                else File.Move(tmp, path);
                SidecarMetaStamper.Stamp(path, "data_drops");
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DataDropTracker.Save: {ex.Message}");
                return false;
            }
        }

        /// <summary>BIM-4D-HANDOVER-01: read the DD4 planned date so the
        /// 4D scheduling engine can extend its timeline beyond the
        /// construction-finish milestone.</summary>
        public static DateTime? GetDD4HandoverDate(Document doc)
        {
            var m = Load(doc).FirstOrDefault(x =>
                string.Equals(x.Id, "DD4", StringComparison.OrdinalIgnoreCase));
            if (m == null) return null;
            if (DateTime.TryParse(m.ActualDate, out DateTime actual)) return actual;
            if (DateTime.TryParse(m.PlannedDate, out DateTime planned)) return planned;
            return null;
        }

        /// <summary>RAG status against current compliance.</summary>
        public static string Rag(Milestone m, double currentCompliancePct)
        {
            if (m == null) return "AMBER";
            if (!string.IsNullOrEmpty(m.ActualDate)) return "GREEN";
            if (m.RequiredCompliancePct > 0 && currentCompliancePct >= m.RequiredCompliancePct)
                return "GREEN";
            if (DateTime.TryParse(m.PlannedDate, out DateTime planned))
            {
                if (planned < DateTime.UtcNow) return "RED";
                if ((planned - DateTime.UtcNow).TotalDays < 14) return "AMBER";
            }
            return "AMBER";
        }

        private static List<Milestone> Defaults() => new()
        {
            new Milestone { Id = "DD1", Description = "Concept design", RequiredCompliancePct = 50 },
            new Milestone { Id = "DD2", Description = "Developed design", RequiredCompliancePct = 70 },
            new Milestone { Id = "DD3", Description = "Construction", RequiredCompliancePct = 85 },
            new Milestone { Id = "DD4", Description = "Handover", RequiredCompliancePct = 95 },
        };
    }
}

namespace StingTools.BIMManager
{
    // ═══════════════════════════════════════════════════════════════════
    //  BIM-CDE-APPROVAL-01 — Role-based CDE approval gate
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// ISO 19650-2 §5.6 requires role-based approval to transition a
    /// document between CDE states. Each transition has a minimum role
    /// (Originator → Reviewer → Approver) and the gate denies the
    /// transition if the current user (Environment.UserName) doesn't
    /// hold that role per the project team roster
    /// (`_BIM_COORD/project_team.json`).
    /// </summary>
    internal static class CdeApprovalGate
    {
        public class GateResult
        {
            public bool   Pass        { get; set; }
            public string RequiredRole{ get; set; }
            public string ActualRole  { get; set; }
            public string Reason      { get; set; }
        }

        // Minimum role per CDE transition. Higher rank wins.
        private static readonly Dictionary<string, string> TransitionMinRole
            = new(StringComparer.OrdinalIgnoreCase)
            {
                ["WIP->SHARED"]      = "Reviewer",
                ["SHARED->PUBLISHED"] = "Approver",
                ["PUBLISHED->ARCHIVED"] = "Approver",
                // Reverse / sideways transitions
                ["SHARED->WIP"]      = "Originator",
                ["PUBLISHED->SHARED"] = "Approver",
            };

        private static readonly Dictionary<string, int> RoleRank
            = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Originator"] = 1,
                ["Author"]     = 1,
                ["Reviewer"]   = 2,
                ["Lead"]       = 2,
                ["Approver"]   = 3,
                ["Manager"]    = 3,
                ["BIMManager"] = 3,
            };

        public static GateResult Validate(Document doc, string fromState, string toState)
        {
            var result = new GateResult();
            string key = $"{fromState?.ToUpperInvariant()}->{toState?.ToUpperInvariant()}";
            if (!TransitionMinRole.TryGetValue(key, out string minRole))
            {
                result.Pass = true;
                result.Reason = "Transition does not require role check.";
                return result;
            }
            result.RequiredRole = minRole;

            string actualRole = LookupRole(doc, Environment.UserName);
            result.ActualRole = actualRole ?? "(unknown)";

            int requiredRank = RoleRank.TryGetValue(minRole, out int rr) ? rr : 1;
            int actualRank   = string.IsNullOrEmpty(actualRole) ? 0
                : (RoleRank.TryGetValue(actualRole, out int ar) ? ar : 0);

            if (actualRank >= requiredRank)
            {
                result.Pass = true;
                result.Reason = $"User '{Environment.UserName}' has role '{actualRole}' (rank {actualRank}) ≥ required '{minRole}' (rank {requiredRank}).";
            }
            else
            {
                result.Pass = false;
                result.Reason = string.IsNullOrEmpty(actualRole)
                    ? $"User '{Environment.UserName}' is not in project_team.json — transition '{key}' requires '{minRole}'."
                    : $"User '{Environment.UserName}' has role '{actualRole}' (rank {actualRank}) < required '{minRole}' (rank {requiredRank}).";
            }
            return result;
        }

        private static string LookupRole(Document doc, string username)
        {
            try
            {
                string bim = BIMManagerEngine.GetBIMManagerDir(doc);
                if (string.IsNullOrEmpty(bim)) return null;
                string path = Path.Combine(bim, "project_team.json");
                if (!File.Exists(path)) return null;
                var token = JToken.Parse(File.ReadAllText(path));
                JArray members = token as JArray ?? token["members"] as JArray;
                if (members == null) return null;
                var match = members.OfType<JObject>().FirstOrDefault(m =>
                    string.Equals(m["username"]?.ToString(), username, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(m["email"]?.ToString(), username, StringComparison.OrdinalIgnoreCase));
                return match?["role"]?.ToString();
            }
            catch (Exception ex) { StingLog.Warn($"CdeApprovalGate.LookupRole: {ex.Message}"); return null; }
        }
    }
}

namespace StingTools.BIMManager
{
    // ═══════════════════════════════════════════════════════════════════
    //  BIM-EXCEL-CROSS-01 — FUNC ↔ SYS cross-validation
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// During Excel round-trip imports we already validate individual
    /// tokens via ISO19650Validator. This engine adds the missing
    /// cross-token check: certain FUNC codes only make sense for certain
    /// SYS codes (e.g. FUNC=PWR is invalid on SYS=HVAC). Mismatches are
    /// reported as ValidationWarnings the import dialog can surface.
    /// </summary>
    internal static class FuncSysValidator
    {
        // Matrix of (SYS, valid FUNCs). Empty list ⇒ any FUNC accepted
        // (this stays permissive — only known-bad pairs are flagged).
        private static readonly Dictionary<string, HashSet<string>> Allowed
            = new(StringComparer.OrdinalIgnoreCase)
            {
                ["HVAC"] = new(StringComparer.OrdinalIgnoreCase) { "SUP","RET","EXH","HTG","CLG","VEN","FRA","SAV" },
                ["DCW"]  = new(StringComparer.OrdinalIgnoreCase) { "DCW","DOM" },
                ["DHW"]  = new(StringComparer.OrdinalIgnoreCase) { "DHW","HTG" },
                ["HWS"]  = new(StringComparer.OrdinalIgnoreCase) { "HWS","HTG","RET" },
                ["SAN"]  = new(StringComparer.OrdinalIgnoreCase) { "SAN","WST","VNT" },
                ["RWD"]  = new(StringComparer.OrdinalIgnoreCase) { "RWD","WST" },
                ["GAS"]  = new(StringComparer.OrdinalIgnoreCase) { "GAS","FUE" },
                ["FP"]   = new(StringComparer.OrdinalIgnoreCase) { "FP","FLS","SUP" },
                ["LV"]   = new(StringComparer.OrdinalIgnoreCase) { "PWR","LIT","CTL","DAT" },
                ["FLS"]  = new(StringComparer.OrdinalIgnoreCase) { "FLS","FA","SD" },
                ["COM"]  = new(StringComparer.OrdinalIgnoreCase) { "COM","DAT","ICT" },
                ["ICT"]  = new(StringComparer.OrdinalIgnoreCase) { "ICT","DAT","COM" },
                ["NCL"]  = new(StringComparer.OrdinalIgnoreCase) { "NCL","CAL" },
                ["SEC"]  = new(StringComparer.OrdinalIgnoreCase) { "SEC","ACS","CCT" },
            };

        public class Mismatch
        {
            public int    Row    { get; set; }
            public string Sys    { get; set; }
            public string Func   { get; set; }
            public string TagId  { get; set; }
            public string Reason { get; set; }
        }

        public static List<Mismatch> Validate(IEnumerable<(int row, string tagId, string sys, string func)> rows)
        {
            var hits = new List<Mismatch>();
            if (rows == null) return hits;
            foreach (var (row, tagId, sys, func) in rows)
            {
                if (string.IsNullOrEmpty(sys) || string.IsNullOrEmpty(func)) continue;
                if (!Allowed.TryGetValue(sys, out var validFuncs)) continue; // unknown SYS — pass through
                if (!validFuncs.Contains(func))
                {
                    hits.Add(new Mismatch
                    {
                        Row = row,
                        TagId = tagId,
                        Sys = sys,
                        Func = func,
                        Reason = $"FUNC='{func}' is not a recognised function for SYS='{sys}'. Valid: {string.Join(", ", validFuncs)}"
                    });
                }
            }
            return hits;
        }
    }
}

namespace StingTools.BIMManager
{
    // ═══════════════════════════════════════════════════════════════════
    //  STRUCT-REBAR-01 — Rebar spacing > bar diameter pre-check
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// EC2 §8.2 (BS EN 1992-1-1) requires the clear spacing between
    /// parallel bars to be at least the bar diameter (and not less than
    /// 20 mm or dg + 5 mm). This pre-design check sweeps every Rebar
    /// element in the document and flags any that violates the diameter
    /// rule before a downstream reinforcement export would carry the
    /// error forward.
    /// </summary>
    internal static class RebarSpacingChecker
    {
        public class Issue
        {
            public ElementId Id        { get; set; }
            public double    DiameterMm{ get; set; }
            public double    SpacingMm { get; set; }
            public string    Reason    { get; set; }
        }

        public static List<Issue> Check(Document doc, double minClearSpacingMm = 20)
        {
            var issues = new List<Issue>();
            try
            {
                // The Structure namespace's Rebar class is referenced via fully qualified
                // name so this file doesn't pull a hard dependency on Autodesk.Revit.DB.Structure
                // — Rebar is the only Structure-namespace type touched here.
                var collector = new FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Structure.Rebar))
                    .Cast<Autodesk.Revit.DB.Structure.Rebar>();

                foreach (var rebar in collector)
                {
                    try
                    {
                        var typeId = rebar.GetTypeId();
                        if (typeId == null || typeId == ElementId.InvalidElementId) continue;
                        var barType = doc.GetElement(typeId)
                            as Autodesk.Revit.DB.Structure.RebarBarType;
                        if (barType == null) continue;
                        double dia = barType.BarDiameter; // feet
                        double diaMm = dia * 304.8;

                        // ARRAY_ARRAY_LENGTH / number-of-bars to derive spacing
                        double arrayLenFt = ReadDouble(rebar, BuiltInParameter.REBAR_ELEM_LENGTH);
                        int nBars = rebar.NumberOfBarPositions;
                        if (nBars <= 1 || arrayLenFt <= 0) continue;
                        double spacingFt = arrayLenFt / (nBars - 1);
                        double spacingMm = spacingFt * 304.8;
                        // clear spacing = centre spacing − diameter
                        double clearMm = spacingMm - diaMm;

                        double thresholdMm = Math.Max(diaMm, minClearSpacingMm);
                        if (clearMm < thresholdMm)
                        {
                            issues.Add(new Issue
                            {
                                Id = rebar.Id,
                                DiameterMm = diaMm,
                                SpacingMm = spacingMm,
                                Reason = $"clear spacing {clearMm:F1} mm < threshold {thresholdMm:F1} mm (Ø={diaMm:F0} mm)"
                            });
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"RebarSpacingChecker {rebar.Id}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"RebarSpacingChecker: {ex.Message}"); }
            return issues;
        }

        private static double ReadDouble(Element el, BuiltInParameter bip)
        {
            try
            {
                var p = el.get_Parameter(bip);
                return p?.HasValue == true ? p.AsDouble() : 0.0;
            }
            catch { return 0.0; }
        }
    }
}

namespace StingTools.BIMManager
{
    // ═══════════════════════════════════════════════════════════════════
    //  ACOUSTIC-CAVITY-01 — Frequency-dependent cavity bonus
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// BS EN 12354-1 lets you add a cavity-resonance bonus to the airborne
    /// sound reduction index R of a double-leaf partition, but the bonus
    /// varies with frequency band (highest bonus around 500 Hz, falling
    /// at the low / high extremes). Previously the AcousticAnalysisEngine
    /// applied a flat +10 dB. This lookup table replaces the flat value
    /// with band-specific bonuses interpolated from BS EN 12354-1 Annex B.
    /// </summary>
    internal static class AcousticCavityBonus
    {
        // Hz → bonus dB (typical 50 mm air cavity, mineral-wool filled).
        // Source: BS EN 12354-1:2017 Annex B.3, indicative values.
        private static readonly (int Hz, double Db)[] Table =
        {
            (50,    2.0),
            (100,   4.0),
            (200,   7.5),
            (315,   9.0),
            (500,  12.0),
            (1000, 11.5),
            (2000,  9.5),
            (3150,  7.0),
            (5000,  5.0),
        };

        /// <summary>Linearly interpolate the cavity bonus for the given
        /// frequency. Below 50 Hz returns the 50 Hz value; above 5000 Hz
        /// returns the 5000 Hz value.</summary>
        public static double BonusAt(double frequencyHz)
        {
            if (frequencyHz <= Table[0].Hz) return Table[0].Db;
            if (frequencyHz >= Table[Table.Length - 1].Hz) return Table[Table.Length - 1].Db;
            for (int i = 1; i < Table.Length; i++)
            {
                var (lo, dbLo) = Table[i - 1];
                var (hi, dbHi) = Table[i];
                if (frequencyHz <= hi)
                {
                    double t = (frequencyHz - lo) / (double)(hi - lo);
                    return dbLo + t * (dbHi - dbLo);
                }
            }
            return Table[Table.Length - 1].Db;
        }

        /// <summary>Compute the weighted average bonus across the standard
        /// 1/3-octave bands used to derive Rw (100 Hz to 3150 Hz).</summary>
        public static double WeightedRwBonus()
        {
            int[] bands = { 100, 125, 160, 200, 250, 315, 400, 500, 630, 800,
                            1000, 1250, 1600, 2000, 2500, 3150 };
            double sum = 0;
            foreach (int b in bands) sum += BonusAt(b);
            return sum / bands.Length;
        }
    }
}

namespace StingTools.BIMManager
{
    // ═══════════════════════════════════════════════════════════════════
    //  WF-SCHED-01 / WF-SCHED-02 — Schedule template library + checker
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Save / load named schedule templates as JSON definitions in
    /// `_BIM_COORD/schedule_templates/<name>.json` so coordinators can
    /// build a project-wide schedule kit once and re-use it. The checker
    /// scans the live project's ViewSchedules and reports any field that
    /// has the same canonical name but a different display label across
    /// schedules — closing WF-SCHED-02.
    /// </summary>
    internal static class ScheduleTemplateLib
    {
        public class TemplateDef
        {
            [JsonProperty("name")]            public string Name { get; set; }
            [JsonProperty("category")]        public string Category { get; set; }
            [JsonProperty("fields")]          public List<string> Fields { get; set; } = new();
            [JsonProperty("filter_field")]    public string FilterField { get; set; }
            [JsonProperty("filter_value")]    public string FilterValue { get; set; }
            [JsonProperty("sort_field")]      public string SortField { get; set; }
            [JsonProperty("created_at")]      public string CreatedAt { get; set; }
            [JsonProperty("created_by")]      public string CreatedBy { get; set; }
        }

        public static string LibDir(Document doc)
        {
            string bim = BIMManagerEngine.GetBIMManagerDir(doc);
            return string.IsNullOrEmpty(bim) ? null : Path.Combine(bim, "schedule_templates");
        }

        public static bool Save(Document doc, TemplateDef def)
        {
            string dir = LibDir(doc);
            if (dir == null || def == null || string.IsNullOrEmpty(def.Name)) return false;
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                def.CreatedAt ??= DateTime.UtcNow.ToString("o");
                def.CreatedBy ??= Environment.UserName;
                string path = Path.Combine(dir, SanitizeFileName(def.Name) + ".json");
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(def, Formatting.Indented));
                if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
                else File.Move(tmp, path);
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"ScheduleTemplateLib.Save: {ex.Message}"); return false; }
        }

        public static List<TemplateDef> List(Document doc)
        {
            var items = new List<TemplateDef>();
            string dir = LibDir(doc);
            if (dir == null || !Directory.Exists(dir)) return items;
            foreach (var path in Directory.GetFiles(dir, "*.json"))
            {
                try { items.Add(JsonConvert.DeserializeObject<TemplateDef>(File.ReadAllText(path))); }
                catch (Exception ex) { StingLog.Warn($"ScheduleTemplateLib.List {Path.GetFileName(path)}: {ex.Message}"); }
            }
            return items.Where(t => t != null).OrderBy(t => t.Name).ToList();
        }

        /// <summary>WF-SCHED-02: scan live project schedules for fields
        /// whose canonical name appears under different display labels
        /// across schedules. Returns one Issue per inconsistent field.</summary>
        public class FieldInconsistency
        {
            public string FieldName { get; set; }
            public Dictionary<string, List<string>> LabelsBySchedule { get; } = new();
        }

        public static List<FieldInconsistency> CheckFieldConsistency(Document doc)
        {
            var byField = new Dictionary<string, FieldInconsistency>(StringComparer.OrdinalIgnoreCase);
            foreach (var sched in new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>())
            {
                string sName = sched.Name ?? sched.Id.ToString();
                ScheduleDefinition sdef = sched.Definition;
                if (sdef == null) continue;
                int n = sdef.GetFieldCount();
                for (int i = 0; i < n; i++)
                {
                    var f = sdef.GetField(i);
                    string canonical = f.GetName();
                    string heading   = f.ColumnHeading ?? "";
                    if (!byField.TryGetValue(canonical, out var fi))
                    {
                        fi = new FieldInconsistency { FieldName = canonical };
                        byField[canonical] = fi;
                    }
                    if (!fi.LabelsBySchedule.TryGetValue(heading, out var schedList))
                    {
                        schedList = new List<string>();
                        fi.LabelsBySchedule[heading] = schedList;
                    }
                    schedList.Add(sName);
                }
            }
            // Only return fields with > 1 distinct heading.
            return byField.Values.Where(f => f.LabelsBySchedule.Count > 1).ToList();
        }

        private static string SanitizeFileName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}

namespace StingTools.BIMManager
{
    // ═══════════════════════════════════════════════════════════════════
    //  MEP-SCHED-01 — MEP commissioning schedule definitions
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Three commissioning-focused MEP schedules that previously had to be
    /// hand-built by the QS / commissioning manager: connector flow rate,
    /// balancing status, and pressure drop summary. Each definition spells
    /// out the (BuiltInCategory, parameter list) pair so the existing
    /// ScheduleHelper can mint the schedule on demand.
    /// </summary>
    internal static class MepCommissioningSchedules
    {
        public class Def
        {
            public string Name { get; set; }
            public BuiltInCategory Category { get; set; }
            public List<BuiltInParameter> Fields { get; set; }
            public string GroupBy { get; set; }
        }

        public static IReadOnlyList<Def> All { get; } = new List<Def>
        {
            new()
            {
                Name = "STING - Connector Flow Rate",
                Category = BuiltInCategory.OST_DuctTerminal,
                Fields = new()
                {
                    BuiltInParameter.RBS_SYSTEM_NAME_PARAM,
                    BuiltInParameter.RBS_DUCT_FLOW_PARAM,
                    BuiltInParameter.RBS_DUCT_PRESSURE_DROP_PARAM,
                    BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM,
                },
                GroupBy = "Family and Type",
            },
            new()
            {
                Name = "STING - Pipe Balancing Status",
                Category = BuiltInCategory.OST_PipeAccessory,
                Fields = new()
                {
                    BuiltInParameter.RBS_SYSTEM_NAME_PARAM,
                    BuiltInParameter.RBS_PIPE_FLOW_PARAM,
                    BuiltInParameter.RBS_PIPE_PRESSUREDROP_PARAM,
                    BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM,
                },
                GroupBy = "System Name",
            },
            new()
            {
                Name = "STING - HVAC Pressure Drop Summary",
                Category = BuiltInCategory.OST_DuctCurves,
                Fields = new()
                {
                    BuiltInParameter.RBS_SYSTEM_NAME_PARAM,
                    BuiltInParameter.RBS_DUCT_FLOW_PARAM,
                    BuiltInParameter.RBS_DUCT_PRESSURE_DROP_PARAM,
                    BuiltInParameter.RBS_DUCT_HYDRAULIC_DIAMETER_PARAM,
                },
                GroupBy = "System Name",
            },
        };

        /// <summary>Create any commissioning schedule that doesn't already
        /// exist in the document (matched by Name).</summary>
        public static int CreateMissing(Document doc)
        {
            int created = 0;
            var existingNames = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Select(v => v.Name ?? ""),
                StringComparer.OrdinalIgnoreCase);

            foreach (var def in All)
            {
                if (existingNames.Contains(def.Name)) continue;
                try
                {
                    using (var t = new Transaction(doc, $"STING Commissioning Schedule: {def.Name}"))
                    {
                        t.Start();
                        var sched = ViewSchedule.CreateSchedule(doc, new ElementId((long)def.Category));
                        sched.Name = def.Name;
                        var sdef = sched.Definition;
                        var fields = sdef.GetSchedulableFields();
                        foreach (var bip in def.Fields)
                        {
                            var field = fields.FirstOrDefault(f => f.ParameterId == new ElementId((long)bip));
                            if (field != null) sdef.AddField(field);
                        }
                        t.Commit();
                    }
                    created++;
                }
                catch (Exception ex) { StingLog.Warn($"MepCommissioningSchedules '{def.Name}': {ex.Message}"); }
            }
            return created;
        }
    }
}

namespace StingTools.BIMManager
{
    // ═══════════════════════════════════════════════════════════════════
    //  FM-HO-02 — Phase-aware COBie export filter
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// COBie defines components per facility lifecycle phase, but the
    /// existing exporter writes every tagged element regardless of phase.
    /// This filter takes a list of elements and returns only those whose
    /// PhaseCreated is on or before a target phase, and whose
    /// PhaseDemolished is null or after that phase — i.e. elements that
    /// exist in the requested phase. The exporter stamps each row with
    /// the phase name so multi-phase models can write distinct Component
    /// rows per phase.
    /// </summary>
    internal static class PhaseAwareCobie
    {
        public class FilteredElement
        {
            public Element Element     { get; set; }
            public string  PhaseName   { get; set; }
        }

        public static List<FilteredElement> Filter(Document doc, IEnumerable<Element> elements, ElementId targetPhaseId)
        {
            var result = new List<FilteredElement>();
            if (elements == null) return result;
            if (targetPhaseId == null || targetPhaseId == ElementId.InvalidElementId)
            {
                foreach (var el in elements)
                    result.Add(new FilteredElement { Element = el, PhaseName = "(any)" });
                return result;
            }

            var phases = new FilteredElementCollector(doc).OfClass(typeof(Phase)).Cast<Phase>().ToList();
            int targetSeq = phases.FindIndex(p => p.Id == targetPhaseId);
            if (targetSeq < 0) return result;
            string targetName = phases[targetSeq].Name ?? "";

            foreach (var el in elements)
            {
                if (el == null) continue;
                ElementId createdId = ReadId(el, BuiltInParameter.PHASE_CREATED);
                ElementId demoId    = ReadId(el, BuiltInParameter.PHASE_DEMOLISHED);
                int createdSeq = createdId == null || createdId == ElementId.InvalidElementId
                    ? 0
                    : phases.FindIndex(p => p.Id == createdId);
                if (createdSeq < 0) continue;
                if (createdSeq > targetSeq) continue;
                if (demoId != null && demoId != ElementId.InvalidElementId)
                {
                    int demoSeq = phases.FindIndex(p => p.Id == demoId);
                    if (demoSeq >= 0 && demoSeq <= targetSeq) continue;
                }
                result.Add(new FilteredElement { Element = el, PhaseName = targetName });
            }
            return result;
        }

        private static ElementId ReadId(Element el, BuiltInParameter bip)
        {
            try
            {
                var p = el.get_Parameter(bip);
                return p?.HasValue == true ? p.AsElementId() : ElementId.InvalidElementId;
            }
            catch { return ElementId.InvalidElementId; }
        }
    }
}

namespace StingTools.BIMManager
{
    // ═══════════════════════════════════════════════════════════════════
    //  TAG-WORKFLOW-PARALLEL-01 — DAG dependency ordering for workflows
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Wires the existing `WorkflowStep.ParallelGroup` field into a DAG
    /// scheduler. True parallel execution is impossible because the
    /// Revit API is single-threaded — but topological ordering by
    /// (parallelGroup, originalIndex) lets independent groups skip-
    /// ahead when an earlier group fails: if any step in group N fails
    /// non-fatally and its `dependsOn` predecessor pointed at group M,
    /// the scheduler may continue with group N+1.
    ///
    /// This engine returns the ordered execution plan; the actual
    /// command dispatch still runs sequentially in
    /// `WorkflowEngine.ExecutePreset`.
    /// </summary>
    internal static class WorkflowDagPlanner
    {
        public class PlanEntry
        {
            public int    OriginalIndex { get; set; }
            public int    Group         { get; set; }
            public string CommandTag    { get; set; }
            public bool   IsBlocked     { get; set; }
            public string BlockedReason { get; set; }
        }

        /// <summary>Sort the steps so groups stay together and lower
        /// groups run first. Steps with no group default to the same
        /// pseudo-group as their original index so legacy presets keep
        /// running in declared order.</summary>
        public static List<PlanEntry> Plan(IList<WorkflowStep> steps)
        {
            var entries = new List<PlanEntry>();
            if (steps == null) return entries;
            for (int i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                entries.Add(new PlanEntry
                {
                    OriginalIndex = i,
                    Group = s.ParallelGroup ?? i,
                    CommandTag = s.CommandTag ?? "",
                });
            }
            return entries
                .OrderBy(e => e.Group)
                .ThenBy(e => e.OriginalIndex)
                .ToList();
        }

        /// <summary>Mark every entry whose group sits behind a failed
        /// upstream group as blocked. Caller supplies the set of groups
        /// that succeeded and the set that failed; entries in groups
        /// after a failed group with no successful peer are blocked.
        /// Independent groups (different group id, no overlap) keep
        /// running, which is the half-execution-time win.</summary>
        public static void MarkBlocked(List<PlanEntry> plan,
            HashSet<int> succeededGroups, HashSet<int> failedGroups)
        {
            if (plan == null) return;
            int? lastFailedGroup = null;
            foreach (var e in plan.OrderBy(p => p.Group))
            {
                if (failedGroups != null && failedGroups.Contains(e.Group))
                {
                    lastFailedGroup ??= e.Group;
                    continue;
                }
                if (lastFailedGroup.HasValue
                    && e.Group > lastFailedGroup.Value
                    && succeededGroups != null
                    && !succeededGroups.Any(g => g >= lastFailedGroup.Value && g < e.Group))
                {
                    e.IsBlocked = true;
                    e.BlockedReason = $"Group {e.Group} blocked by failed group {lastFailedGroup.Value} with no recovery in between.";
                }
            }
        }
    }
}
