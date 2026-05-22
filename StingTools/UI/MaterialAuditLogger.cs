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
                Planscape.Docs.Workflow.AuditLog.Append(doc, action, materialName ?? "(unknown)", jo);

                // C7 — Mirror material events into the BIM Coordination
                // Center's coord log so the project timeline shows every
                // MAT_* event alongside revisions / issues / clashes.
                try { MaterialCoordLogBridge.Append(doc, action, materialName, payload); }
                catch (Exception ex) { StingLog.WarnRateLimited("AuditLog.Coord", $"Coord log mirror: {ex.Message}"); }
            }
            catch (Exception ex) { StingLog.Warn($"MaterialAuditLogger.Log {action} '{materialName}': {ex.Message}"); }
        }
    }

    /// <summary>
    /// C7 — Bridge material-audit events into the BCC coord log so
    /// material edits show up on the project timeline alongside other
    /// coordination events. JSONL file at
    /// <c>&lt;project&gt;/_BIM_COORD/coord_log.json</c>.
    /// Best-effort — never throws, never blocks the audit writer.
    /// </summary>
    public static class MaterialCoordLogBridge
    {
        public static void Append(Document doc, string action, string materialName,
            IDictionary<string, object> payload)
        {
            try
            {
                string path = Core.ProjectFolderEngine.GetDataPath(doc, "coord_log.json");
                if (string.IsNullOrEmpty(path)) return;
                var dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                var entry = new JObject
                {
                    ["ts"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    ["user"] = Environment.UserName ?? "",
                    ["source"] = "MaterialManager",
                    ["action"] = action,
                    ["material"] = materialName ?? "",
                };
                if (payload != null)
                    foreach (var kv in payload)
                        entry[kv.Key] = JToken.FromObject(kv.Value ?? "");
                System.IO.File.AppendAllText(path, entry.ToString(Newtonsoft.Json.Formatting.None) + "\n");
            }
            catch (Exception ex) { StingLog.WarnRateLimited("CoordLog.Append", $"Coord log append: {ex.Message}"); }
        }
    }
}
