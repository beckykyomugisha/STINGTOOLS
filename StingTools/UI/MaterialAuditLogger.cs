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
            }
            catch (Exception ex) { StingLog.Warn($"MaterialAuditLogger.Log {action} '{materialName}': {ex.Message}"); }
        }
    }
}
