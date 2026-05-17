// DeliverableServerSync.cs — Phase 177 (document manager review).
//
// Thin glue between the plugin's DeliverableLifecycle (template-engine v1.1)
// and the Planscape server's DocumentRecord state machine. Called fire-and-
// forget from inside Issue/ReIssue/Publish/Cancel/Supersede/Replace so the
// plugin's existing code path is unchanged when the server is unreachable.
//
// Resolution flow:
//   1. Plugin lifecycle mutates deliverables.json + writes local audit row.
//   2. This sync attempts to map the deliverable to the server's
//      DocumentRecord by FileName == DocNumber, creating it if absent.
//   3. CDE state + suitability + revision mirror across.
//   4. Failures log a warning and are dropped — the next 5-min SyncScheduler
//      tick (or the next coordinator action) retries automatically.
//
// ReconcileAsync (Phase 177-B): walks deliverables.json and pushes any row
// whose ServerSyncedAt is missing or older than its row UpdatedAt. Called
// on login + each SyncScheduler tick so an event that fired while offline
// still reaches the server even if the user stops touching the project.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.BIMManager;

namespace Planscape.Docs.Templates
{
    internal static class DeliverableServerSync
    {
        /// <summary>
        /// Fire-and-forget mirror of a lifecycle event. Returns immediately;
        /// the actual HTTP call runs on the thread pool.
        /// </summary>
        public static void FireAndForget(Document doc, dynamic deliverable, string action, string reason)
        {
            try
            {
                Guid projectId = ResolvePlanscapeProjectId(doc);
                if (projectId == Guid.Empty) return; // not connected — nothing to do

                var payload = new
                {
                    docNumber       = (string)deliverable.DocNumber ?? (string)deliverable.Code,
                    title           = SafeProp(deliverable, "Title") ?? SafeProp(deliverable, "Description"),
                    discipline      = SafeProp(deliverable, "Discipline") ?? SafeProp(deliverable, "DISC"),
                    originator      = SafeProp(deliverable, "Originator") ?? SafeProp(deliverable, "OrgCode"),
                    revision        = SafeProp(deliverable, "Revision"),
                    templateId      = SafeProp(deliverable, "TemplateId"),
                    newCdeStatus    = SafeProp(deliverable, "CDE"),
                    suitabilityCode = SafeProp(deliverable, "Suitability"),
                    action          = action,
                    reason          = reason
                };
                if (string.IsNullOrEmpty(payload.docNumber)) return;

                string docNumberCapture = payload.docNumber;
                Document docCapture = doc;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        bool ok = await PlanscapeServerClient.Instance
                            .SyncDeliverableFromPluginAsync(projectId, payload);
                        if (ok)
                        {
                            try { StampSyncedAt(docCapture, docNumberCapture, DateTime.UtcNow); }
                            catch (Exception ex) { StingLog.Warn($"StampSyncedAt failed: {ex.Message}"); }
                        }
                        else
                        {
                            StingLog.Warn($"DeliverableServerSync {action} for {docNumberCapture} failed: " +
                                          $"{PlanscapeServerClient.Instance.LastError}");
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"DeliverableServerSync exception: {ex.Message}"); }
                });
            }
            catch (Exception ex) { StingLog.Warn($"DeliverableServerSync.FireAndForget: {ex.Message}"); }
        }

        /// <summary>
        /// Push a single audit row to the server. Buffered so the next
        /// successful audit tick mops it up if the server is offline.
        /// </summary>
        public static void PushAudit(Document doc, string action, string entityType, string entityId, JObject details)
        {
            try
            {
                Guid projectId = ResolvePlanscapeProjectId(doc);
                if (projectId == Guid.Empty) return;

                var ev = new
                {
                    action,
                    entityType,
                    entityId,
                    detailsJson = details?.ToString(Newtonsoft.Json.Formatting.None),
                    timestamp   = DateTime.UtcNow
                };

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await PlanscapeServerClient.Instance.PushAuditEventsAsync(
                            projectId, new object[] { ev });
                    }
                    catch (Exception ex) { StingLog.Warn($"DeliverableServerSync.PushAudit: {ex.Message}"); }
                });
            }
            catch (Exception ex) { StingLog.Warn($"DeliverableServerSync.PushAudit: {ex.Message}"); }
        }

        /// <summary>
        /// Phase 177-B — pull deliverables.json off disk and push every row
        /// whose ServerSyncedAt is missing or earlier than its UpdatedAt
        /// (or its rev-history's most recent timestamp). Called on login
        /// success and on every periodic sync tick so a lifecycle event
        /// that fired while offline still reaches the server even if the
        /// user never touches the project again.
        ///
        /// Bounded to 50 rows per call so a never-synced project doesn't
        /// hammer the server in one go. The next tick mops up the next 50.
        /// </summary>
        public static async Task<int> ReconcileAsync(Document doc)
        {
            try
            {
                Guid projectId = ResolvePlanscapeProjectId(doc);
                if (projectId == Guid.Empty) return 0;

                string path = DeliverablesJsonPath(doc);
                if (!File.Exists(path)) return 0;

                JArray arr;
                try { arr = JArray.Parse(File.ReadAllText(path)); }
                catch (Exception ex)
                {
                    StingLog.Warn($"ReconcileAsync — deliverables.json parse failed: {ex.Message}");
                    return 0;
                }

                int pushed = 0;
                foreach (var token in arr)
                {
                    if (pushed >= 50) break;
                    if (token is not JObject row) continue;
                    if (!NeedsSync(row)) continue;

                    string docNumber = row.Value<string>("DocNumber") ?? row.Value<string>("Code");
                    if (string.IsNullOrEmpty(docNumber)) continue;

                    var payload = new
                    {
                        docNumber       = docNumber,
                        title           = row.Value<string>("Title")      ?? row.Value<string>("Description"),
                        discipline      = row.Value<string>("Discipline") ?? row.Value<string>("DISC"),
                        originator      = row.Value<string>("Originator") ?? row.Value<string>("OrgCode"),
                        revision        = row.Value<string>("Revision"),
                        templateId      = row.Value<string>("TemplateId"),
                        newCdeStatus    = row.Value<string>("CDE"),
                        suitabilityCode = row.Value<string>("Suitability"),
                        action          = "reconcile",
                        reason          = "offline_replay"
                    };

                    bool ok = await PlanscapeServerClient.Instance
                        .SyncDeliverableFromPluginAsync(projectId, payload);
                    if (!ok)
                    {
                        StingLog.Warn($"Reconcile {docNumber} failed: " +
                                      $"{PlanscapeServerClient.Instance.LastError}");
                        // Stop on first failure so we don't burn round-trips
                        // when the server is throwing 5xx — next tick retries.
                        break;
                    }

                    row["ServerSyncedAt"] = DateTime.UtcNow;
                    pushed++;
                }

                if (pushed > 0)
                {
                    string tmp = path + ".tmp";
                    File.WriteAllText(tmp, arr.ToString(Newtonsoft.Json.Formatting.Indented));
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(tmp, path);
                    StingLog.Info($"DeliverableServerSync.Reconcile pushed {pushed} row(s).");
                }
                return pushed;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ReconcileAsync: {ex.Message}");
                return 0;
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static bool NeedsSync(JObject row)
        {
            DateTime synced = row["ServerSyncedAt"]?.Type == JTokenType.Date
                ? row["ServerSyncedAt"].Value<DateTime>()
                : DateTime.MinValue;

            // The lifecycle bumps revision history; treat the latest history
            // entry's timestamp as the row's "updatedAt" if no explicit field.
            DateTime updated = row["UpdatedAt"]?.Type == JTokenType.Date
                ? row["UpdatedAt"].Value<DateTime>()
                : DateTime.MinValue;

            if (row["RevisionHistory"] is JArray history && history.Count > 0)
            {
                var last = history[history.Count - 1] as JObject;
                if (last?["Timestamp"] != null
                    && DateTime.TryParse(last["Timestamp"].Value<string>(), out var ts))
                    if (ts > updated) updated = ts;
            }

            // Never-synced rows always need a push if we have any signal of activity.
            if (synced == DateTime.MinValue) return updated > DateTime.MinValue;
            return updated > synced;
        }

        private static void StampSyncedAt(Document doc, string docNumber, DateTime ts)
        {
            string path = DeliverablesJsonPath(doc);
            if (!File.Exists(path)) return;
            JArray arr = JArray.Parse(File.ReadAllText(path));
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is JObject o
                    && string.Equals(o.Value<string>("DocNumber") ?? o.Value<string>("Code"),
                                     docNumber, StringComparison.Ordinal))
                {
                    o["ServerSyncedAt"] = ts;
                    string tmp = path + ".tmp";
                    File.WriteAllText(tmp, arr.ToString(Newtonsoft.Json.Formatting.Indented));
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(tmp, path);
                    return;
                }
            }
        }

        private static string DeliverablesJsonPath(Document doc)
        {
            try
            {
                string consolidated = StingTools.Core.ProjectFolderEngine.GetDataPath(doc);
                if (!string.IsNullOrEmpty(consolidated))
                    return Path.Combine(consolidated, "_BIM_COORD", "deliverables.json");
            }
            catch { /* ignored */ }
            try
            {
                string p = doc?.PathName;
                if (!string.IsNullOrEmpty(p))
                    return Path.Combine(Path.GetDirectoryName(p) ?? "", "_BIM_COORD", "deliverables.json");
            }
            catch { /* ignored */ }
            return Path.Combine(Path.GetTempPath(), "Planscape", "BIMCoord", "deliverables.json");
        }

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

        private static string SafeProp(dynamic obj, string name)
        {
            try
            {
                if (obj == null) return null;
                var t = obj.GetType();
                var p = t.GetProperty(name);
                return p?.GetValue(obj) as string;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }
    }
}
