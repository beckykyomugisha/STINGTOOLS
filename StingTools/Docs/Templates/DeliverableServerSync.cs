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

                _ = Task.Run(async () =>
                {
                    try
                    {
                        bool ok = await PlanscapeServerClient.Instance
                            .SyncDeliverableFromPluginAsync(projectId, payload);
                        if (!ok)
                            StingLog.Warn($"DeliverableServerSync {action} for {payload.docNumber} failed: " +
                                          $"{PlanscapeServerClient.Instance.LastError}");
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

        // ── Helpers ────────────────────────────────────────────────────────

        private static Guid ResolvePlanscapeProjectId(Document doc)
        {
            try
            {
                string bimDir = ProjectFolderEngine.GetMetaPath(doc, "STING_BIM_MANAGER");
                if (string.IsNullOrEmpty(bimDir)) return Guid.Empty;
                string cfgPath = Path.Combine(bimDir, "planscape_connection.json");
                return PlatformSyncCommand.LoadPlanscapeProjectId(cfgPath);
            }
            catch { return Guid.Empty; }
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
            catch { return null; }
        }
    }
}
