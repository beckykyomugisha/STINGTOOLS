using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using Planscape.Docs.Workflow;
using StingTools.BIMManager;

namespace StingTools.Core
{
    /// <summary>
    /// Phase 3 (ISO IM runner) — the one place a warning scan becomes durable:
    /// a local trend snapshot, a tamper-evident audit entry, and a fire-and-forget
    /// server push.
    ///
    /// Called from exactly two choke points so it cannot double-fire:
    ///   • <c>WarningsEngine.ScanWarnings</c>, on the real-scan exit only — never on
    ///     a 30s cache hit, and never on the <c>GetWarnings()</c> exception path
    ///     (a failed scan is not a result of zero).
    ///   • <c>WarningsEngine.SaveExtendedBaseline</c>, after the baseline lands.
    ///
    /// Nothing here throws: warnings telemetry must never take down the scan that
    /// produced it, nor raise a dialog. Failures go to StingLog and stop there.
    /// </summary>
    internal static class WarningSnapshotRecorder
    {
        internal const string AuditScanCompleted = "warning.scan_completed";
        internal const string AuditBaselineSaved = "warning.baseline_saved";

        // NOTE ON AUDIT SCOPE — no `warning.escalated` event is emitted here.
        // Phase 2's IssueStore already appends `issue.escalated_from_warning`
        // (IssueStore.CreateAction) for every warning→issue escalation. Adding a
        // second event would double-log the same fact onto the chain.

        /// <summary>Record a completed real scan: snapshot → audit → push.</summary>
        internal static void RecordScan(Document doc, WarningReport report)
        {
            if (doc == null || report == null) return;
            try
            {
                var snap = BuildSnapshot(report, WarningSnapshotFormat.KindScan);

                WarningSnapshotStore.Append(doc, snap);

                SafeAudit(doc, AuditScanCompleted, new JObject
                {
                    ["total"]          = snap.Total,
                    ["auto_fixable"]   = snap.AutoFixable,
                    ["manual_review"]  = snap.ManualReview,
                    ["health_score"]   = snap.HealthScore,
                    ["baseline_total"] = snap.BaselineTotal.HasValue
                                            ? (JToken)snap.BaselineTotal.Value : JValue.CreateNull(),
                    ["by_severity"]    = JObject.FromObject(snap.BySeverity),
                });

                PushReportFireAndForget(doc, snap);
            }
            catch (Exception ex) { StingLog.Warn($"WarningSnapshotRecorder.RecordScan: {ex.Message}"); }
        }

        /// <summary>
        /// Record a baseline save: snapshot → audit → server baseline push.
        ///
        /// SCALE RECONCILIATION — deliberate. <c>warnings_baseline.json</c> stores a
        /// RAW <c>doc.GetWarnings().Count</c>: it includes suppressed warnings and
        /// excludes the synthetic stale-element / BOQ-gap warnings that
        /// <c>ScanWarnings</c> adds. The two numbers have never been on the same
        /// scale. The snapshot and the server push therefore both use the SCAN
        /// scale, so a baseline row is directly comparable to the scan rows either
        /// side of it on a trend line; <paramref name="rawBaselineCount"/> is carried
        /// through to the audit entry so the raw figure is not lost.
        /// </summary>
        internal static void RecordBaseline(Document doc, int rawBaselineCount)
        {
            if (doc == null) return;
            try
            {
                // Cached report if one is fresh, otherwise a real scan. Either way this
                // gives the severity breakdown the raw baseline file does not carry.
                var report = WarningsEngine.ScanWarnings(doc);
                var snap = BuildSnapshot(report, WarningSnapshotFormat.KindBaseline);

                WarningSnapshotStore.Append(doc, snap);

                SafeAudit(doc, AuditBaselineSaved, new JObject
                {
                    ["total"]              = snap.Total,
                    ["raw_baseline_count"] = rawBaselineCount,
                    ["health_score"]       = snap.HealthScore,
                    ["by_severity"]        = JObject.FromObject(snap.BySeverity),
                });

                PushBaselineFireAndForget(doc, snap);
            }
            catch (Exception ex) { StingLog.Warn($"WarningSnapshotRecorder.RecordBaseline: {ex.Message}"); }
        }

        // ── Snapshot construction ──────────────────────────────────────────

        private static WarningSnapshotFormat.WarningSnapshot BuildSnapshot(
            WarningReport report, string kind)
        {
            var snap = new WarningSnapshotFormat.WarningSnapshot
            {
                TsUtc        = DateTime.UtcNow,
                Kind         = kind,
                User         = Environment.UserName ?? "",
                Total        = report?.Total ?? 0,
                AutoFixable  = report?.AutoFixable ?? 0,
                ManualReview = report?.ManualReview ?? 0,
                HealthScore  = WarningsEngine.CalculateWarningHealthScore(report),
                BaselineTotal = report?.BaselineTotal,
            };

            // Enum keys → strings: the codec is Revit-free and must stay that way.
            if (report?.BySeverity != null)
                foreach (var kv in report.BySeverity) snap.BySeverity[kv.Key.ToString()] = kv.Value;
            if (report?.ByCategory != null)
                foreach (var kv in report.ByCategory) snap.ByCategory[kv.Key.ToString()] = kv.Value;

            return snap;
        }

        // ── Audit ──────────────────────────────────────────────────────────

        /// <summary>Audit failures must never take down the scan that already succeeded.</summary>
        private static void SafeAudit(Document doc, string action, JObject payload)
        {
            try
            {
                string docId = doc?.Title ?? "";
                AuditLog.Append(doc, action, docId, payload);
            }
            catch (Exception ex) { StingLog.Warn($"WarningSnapshotRecorder audit '{action}': {ex.Message}"); }
        }

        // ── Server push (fire-and-forget) ──────────────────────────────────

        /// <summary>
        /// POST /api/projects/{id}/warnings/report. Follows the
        /// DeliverableServerSync.FireAndForget idiom: no-op when unconnected,
        /// never blocks, never dialogs, one StingLog line on failure.
        /// </summary>
        private static void PushReportFireAndForget(Document doc, WarningSnapshotFormat.WarningSnapshot snap)
        {
            try
            {
                Guid projectId = ResolvePlanscapeProjectId(doc);
                if (projectId == Guid.Empty) return;   // not connected — nothing to do

                var payload = WarningSnapshotFormat.BuildReportPayload(snap);
                if (payload == null) return;

                int totalCapture = snap.Total;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        bool ok = await PlanscapeServerClient.Instance.PushWarningsAsync(projectId, payload);
                        if (!ok)
                            StingLog.Warn($"Warnings push ({totalCapture} warnings) failed: " +
                                          $"{PlanscapeServerClient.Instance.LastError}");
                    }
                    catch (Exception ex) { StingLog.Warn($"Warnings push exception: {ex.Message}"); }
                });
            }
            catch (Exception ex) { StingLog.Warn($"PushReportFireAndForget: {ex.Message}"); }
        }

        /// <summary>POST /api/projects/{id}/warnings/baseline (writes a ComplianceSnapshot).</summary>
        private static void PushBaselineFireAndForget(Document doc, WarningSnapshotFormat.WarningSnapshot snap)
        {
            try
            {
                Guid projectId = ResolvePlanscapeProjectId(doc);
                if (projectId == Guid.Empty) return;

                // The server's baseline row is a ComplianceSnapshot, so it wants the
                // compliance figures alongside the warning ones. Cached (30s TTL).
                int totalElements = 0;
                double compliancePct = 0;
                try
                {
                    var compliance = ComplianceScan.GetCached() ?? ComplianceScan.Scan(doc);
                    if (compliance != null)
                    {
                        totalElements = compliance.TotalElements;
                        compliancePct = compliance.CompliancePercent;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Baseline push compliance read: {ex.Message}"); }

                var payload = WarningSnapshotFormat.BuildBaselinePayload(snap, totalElements, compliancePct);
                if (payload == null) return;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        bool ok = await PlanscapeServerClient.Instance.PushWarningBaselineAsync(projectId, payload);
                        if (!ok)
                            StingLog.Warn($"Warning baseline push failed: " +
                                          $"{PlanscapeServerClient.Instance.LastError}");
                    }
                    catch (Exception ex) { StingLog.Warn($"Warning baseline push exception: {ex.Message}"); }
                });
            }
            catch (Exception ex) { StingLog.Warn($"PushBaselineFireAndForget: {ex.Message}"); }
        }

        /// <summary>
        /// Guid.Empty means "no Planscape connection configured" — the caller then
        /// does nothing at all. Same resolution DeliverableServerSync and IssueStore use.
        /// </summary>
        private static Guid ResolvePlanscapeProjectId(Document doc)
        {
            try
            {
                string bimDir = ProjectFolderEngine.GetMetaPath(doc, "STING_BIM_MANAGER");
                if (string.IsNullOrEmpty(bimDir)) return Guid.Empty;
                string cfgPath = Path.Combine(bimDir, "planscape_connection.json");
                return PlatformSyncCommand.LoadPlanscapeProjectId(cfgPath);
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return Guid.Empty; }
        }
    }
}
