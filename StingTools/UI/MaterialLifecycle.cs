using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Priority 9 — Material lifecycle states: Draft → Reviewed → Approved → Frozen.
    /// Stored on the Material element via STING_MAT_LIFECYCLE_TXT shared parameter.
    /// Frozen materials are read-only until the lifecycle is reset by an admin.
    ///
    /// Empty / unbound parameter ⇒ "Draft" (default).
    /// </summary>
    public static class MaterialLifecycle
    {
        public static readonly string[] States = new[] { "Draft", "Reviewed", "Approved", "Frozen" };
        public const string ParamName = "STING_MAT_LIFECYCLE_TXT";

        public static string Read(MaterialRow row)
        {
            try
            {
                var doc = StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document;
                if (doc == null || row?.Id == null) return "Draft";
                var mat = doc.GetElement(row.Id) as Material;
                if (mat == null) return "Draft";
                var p = mat.LookupParameter(ParamName);
                if (p == null || !p.HasValue || p.StorageType != StorageType.String) return "Draft";
                var s = (p.AsString() ?? "").Trim();
                return string.IsNullOrEmpty(s) ? "Draft" : s;
            }
            catch (Exception ex) { StingLog.WarnRateLimited("Lifecycle.Read", $"Read: {ex.Message}"); return "Draft"; }
        }

        public static bool Set(MaterialRow row, string state)
        {
            try
            {
                var doc = StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document;
                if (doc == null || row?.Id == null) return false;
                var mat = doc.GetElement(row.Id) as Material;
                if (mat == null) return false;
                using (var t = new Transaction(doc, $"STING Lifecycle '{state}' on '{mat.Name}'"))
                {
                    t.Start();
                    var p = mat.LookupParameter(ParamName);
                    if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) { t.RollBack(); return false; }
                    p.Set(state ?? "Draft");
                    t.Commit();
                }
                MaterialAuditLogger.Log(doc, "MAT_LifecycleChange", mat.Name,
                    new Dictionary<string, object> { ["state"] = state });
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"Lifecycle.Set: {ex.Message}"); return false; }
        }

        public static bool IsFrozen(MaterialRow row) =>
            string.Equals(Read(row), "Frozen", StringComparison.OrdinalIgnoreCase);
    }
}
