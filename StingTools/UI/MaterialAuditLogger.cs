using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Thin façade over <see cref="Planscape.Docs.Workflow.AuditLog"/> for
    /// material-change events. Every entry is hash-chained JSONL under
    /// <c>_BIM_COORD/audit_log_yyyy_MM.jsonl</c> so a material spec change
    /// has the same tamper-evidence guarantees as the rest of STING's
    /// audit surface.
    ///
    /// Action keys used by the Material Manager:
    ///   MAT_AutoFill     — IUpdater filled cost + carbon on new material
    ///   MAT_AutoApply    — IUpdater applied material to a new element
    ///   MAT_Apply        — User applied material to selection (MAT tab)
    ///   MAT_Edit         — User changed identity fields (TODO: hook)
    ///   MAT_Merge        — User merged duplicates
    ///   MAT_Delete       — User deleted a material
    ///   MAT_Import       — CSV import committed N changes
    ///   MAT_OverrideEdit — User edited materials.json
    /// </summary>
    public static class MaterialAuditLogger
    {
        public static void Log(Document doc, string action, string materialName,
            IDictionary<string, object> payload)
        {
            if (doc == null || string.IsNullOrEmpty(action)) return;
            try
            {
                var jo = new JObject { ["material"] = materialName ?? "" };
                if (payload != null)
                    foreach (var kv in payload)
                        jo[kv.Key] = JToken.FromObject(kv.Value ?? "");
                // P-5 — Best-effort retry on file-lock contention. Audit log
                // writes during batch ops can collide; one short retry covers
                // the typical 1-tick window.
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try { Planscape.Docs.Workflow.AuditLog.Append(doc, action, materialName ?? "(unknown)", jo); break; }
                    catch (System.IO.IOException) when (attempt < 2) { System.Threading.Thread.Sleep(10); }
                }

                // C7 — Mirror material events into the BIM Coordination
                // Center's coord log so the project timeline shows every
                // MAT_* event alongside revisions / issues / clashes.
                try { MaterialCoordLogBridge.Append(doc, action, materialName, payload); }
                catch (Exception ex) { StingLog.WarnRateLimited("AuditLog.Coord", $"Coord log mirror: {ex.Message}"); }

                // Priority 7 — Also push to the in-process activity feed
                // so the Material Hub status bar can surface it live.
                try
                {
                    string desc = payload != null && payload.Count > 0
                        ? string.Join(" · ", System.Linq.Enumerable.Select(payload, kv => $"{kv.Key}={kv.Value}"))
                        : "";
                    MaterialActivityFeed.Add(action, materialName, desc);
                }
                catch (Exception ex) { StingLog.WarnRateLimited("AuditLog.Feed", $"Activity feed: {ex.Message}"); }
            }
            catch (Exception ex) { StingLog.Warn($"MaterialAuditLogger.Log {action} '{materialName}': {ex.Message}"); }
        }
    }

    /// <summary>
    /// C7 — Bridge material-audit events into the BCC coord log so material edits
    /// show up on the project timeline alongside other coordination events.
    /// Best-effort — never throws, never blocks the audit writer.
    ///
    /// This wrote JSONL into a file named <c>coord_log.json</c> while the canonical
    /// log was <c>coord_log.jsonl</c>, and used its own field names (ts / source /
    /// material) that the timeline's entry type does not bind. So these events either
    /// broke the reader outright or deserialised blank. Both halves are fixed here:
    /// the path comes from <see cref="Core.CoordLog"/>, and the payload is emitted in
    /// the same shape as every other coordination event.
    /// </summary>
    public static class MaterialCoordLogBridge
    {
        public static void Append(Document doc, string action, string materialName,
            IDictionary<string, object> payload)
        {
            try
            {
                // Detail carries the material plus any extra payload, since the shared
                // entry shape has no material-specific field.
                string detail = materialName ?? "";
                if (payload != null && payload.Count > 0)
                {
                    var bits = new List<string>();
                    foreach (var kv in payload) bits.Add($"{kv.Key}={kv.Value}");
                    detail = string.IsNullOrEmpty(detail)
                        ? string.Join(", ", bits)
                        : detail + " (" + string.Join(", ", bits) + ")";
                }

                var entry = new JObject
                {
                    // Same format as the other writer — the timeline sorts on this
                    // string, so a divergent format would mis-order the log.
                    ["Timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["User"]      = Environment.UserName ?? "unknown",
                    ["Action"]    = action ?? "",
                    ["Category"]  = "Material",
                    ["Detail"]    = detail,
                    ["Impact"]    = "LOW",
                };

                Core.CoordLog.Append(doc, entry);
            }
            catch (Exception ex) { StingLog.WarnRateLimited("CoordLog.Append", $"Coord log append: {ex.Message}"); }
        }
    }
}
