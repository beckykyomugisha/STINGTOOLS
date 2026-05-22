using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// F1 — Surface materials carried by Revit links.
    ///
    /// The MAT > Browse grid is host-only by design (FilteredElementCollector
    /// without a link link walk). A linked architectural model can carry
    /// "BLE_Concrete_C40" with a different RGB or class than the host;
    /// no surface exposes this so the team finds out during IFC export.
    ///
    /// This index walks every RevitLinkInstance whose linked doc is
    /// loaded, indexes its Material elements, and emits
    /// <see cref="LinkedMaterialRow"/> rows the MAT panel can append to
    /// its Browse grid under a "(in link)" badge.
    ///
    /// Also computes reconciliation deltas: any link material whose name
    /// matches a host material but differs by RGB / class / cost / carbon
    /// surfaces as a <see cref="LinkedMaterialMismatch"/> so the team
    /// can choose to canonicalise.
    /// </summary>
    public class LinkedMaterialRow
    {
        public string LinkInstanceName { get; set; }
        public string LinkedDocTitle { get; set; }
        public string MaterialName { get; set; }
        public string MaterialClass { get; set; }
        public string ColorText { get; set; }
        public int LinkUsageCount { get; set; }
    }

    public class LinkedMaterialMismatch
    {
        public string MaterialName { get; set; }
        public string LinkedDocTitle { get; set; }
        public string Field { get; set; }     // "RGB" / "Class" / "Cost" / "Carbon"
        public string HostValue { get; set; }
        public string LinkValue { get; set; }
    }

    public class LinkedMaterialAuditResult
    {
        public List<LinkedMaterialRow> Rows { get; } = new List<LinkedMaterialRow>();
        public List<LinkedMaterialMismatch> Mismatches { get; } = new List<LinkedMaterialMismatch>();
        public int LinksScanned { get; set; }
        public int LinksUnloaded { get; set; }
        public int DistinctMaterials => Rows.Select(r => r.MaterialName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    }

    public static class LinkedMaterialIndex
    {
        public static LinkedMaterialAuditResult Run(Document hostDoc)
        {
            var result = new LinkedMaterialAuditResult();
            if (hostDoc == null) return result;

            // Build the host material name → snapshot map once so the
            // reconciliation pass is O(N + M) rather than O(N·M).
            var hostByName = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var m in new FilteredElementCollector(hostDoc).OfClass(typeof(Material)).Cast<Material>())
                    hostByName[m.Name ?? ""] = m;
            }
            catch (Exception ex) { StingLog.Warn($"LinkedMaterialIndex host scan: {ex.Message}"); }

            try
            {
                var linkInstances = new FilteredElementCollector(hostDoc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .ToList();

                foreach (var li in linkInstances)
                {
                    result.LinksScanned++;
                    Document linkedDoc = null;
                    try { linkedDoc = li.GetLinkDocument(); }
                    catch (Exception ex) { StingLog.WarnRateLimited("LinkedMat.GetDoc", $"GetLinkDocument: {ex.Message}"); }
                    if (linkedDoc == null) { result.LinksUnloaded++; continue; }

                    // Per-link usage counter — materials referenced by
                    // GetMaterialIds across non-type elements in the link.
                    var usage = ComputeLinkUsage(linkedDoc);

                    foreach (var mat in new FilteredElementCollector(linkedDoc).OfClass(typeof(Material)).Cast<Material>())
                    {
                        try
                        {
                            string colText = "";
                            try
                            {
                                var c = mat.Color;
                                if (c != null && c.IsValid)
                                    colText = $"{c.Red:000} {c.Green:000} {c.Blue:000}";
                            }
                            catch (Exception ex) { StingLog.WarnRateLimited("LinkedMat.Color", $"link mat color: {ex.Message}"); }

                            int use = 0;
                            if (mat.Id != null) usage.TryGetValue(mat.Id.Value, out use);

                            result.Rows.Add(new LinkedMaterialRow
                            {
                                LinkInstanceName = li.Name ?? "",
                                LinkedDocTitle = linkedDoc.Title ?? "",
                                MaterialName = mat.Name ?? "",
                                MaterialClass = mat.MaterialClass ?? "",
                                ColorText = colText,
                                LinkUsageCount = use,
                            });

                            // Reconciliation: compare against the host
                            // material with the same name (if any).
                            if (hostByName.TryGetValue(mat.Name ?? "", out var hostMat))
                                CompareAndReport(result, hostMat, mat, linkedDoc.Title ?? "");
                        }
                        catch (Exception ex) { StingLog.WarnRateLimited("LinkedMat.Walk", $"link mat walk: {ex.Message}"); }
                    }
                }
            }
            catch (Exception ex) { StingLog.Error("LinkedMaterialIndex.Run", ex); }
            return result;
        }

        private static Dictionary<long, int> ComputeLinkUsage(Document linkedDoc)
        {
            var map = new Dictionary<long, int>();
            try
            {
                foreach (var el in new FilteredElementCollector(linkedDoc).WhereElementIsNotElementType())
                {
                    try
                    {
                        var mats = el.GetMaterialIds(false);
                        if (mats != null)
                            foreach (var mid in mats)
                                if (mid != null && mid.Value > 0)
                                    map[mid.Value] = map.TryGetValue(mid.Value, out int v) ? v + 1 : 1;
                    }
                    catch (Exception ex) { StingLog.WarnRateLimited("LinkedMat.Usage", $"link usage walk: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ComputeLinkUsage: {ex.Message}"); }
            return map;
        }

        private static void CompareAndReport(LinkedMaterialAuditResult result, Material host, Material link, string linkedDocTitle)
        {
            try
            {
                // RGB
                try
                {
                    var hc = host.Color; var lc = link.Color;
                    string hRgb = (hc != null && hc.IsValid) ? $"{hc.Red:000} {hc.Green:000} {hc.Blue:000}" : "";
                    string lRgb = (lc != null && lc.IsValid) ? $"{lc.Red:000} {lc.Green:000} {lc.Blue:000}" : "";
                    if (!string.Equals(hRgb, lRgb, StringComparison.Ordinal))
                        result.Mismatches.Add(new LinkedMaterialMismatch
                        {
                            MaterialName = host.Name ?? "",
                            LinkedDocTitle = linkedDocTitle,
                            Field = "RGB",
                            HostValue = hRgb,
                            LinkValue = lRgb,
                        });
                }
                catch (Exception ex) { StingLog.Warn($"CompareAndReport RGB: {ex.Message}"); }

                // Class
                string hClass = host.MaterialClass ?? "";
                string lClass = link.MaterialClass ?? "";
                if (!string.Equals(hClass, lClass, StringComparison.OrdinalIgnoreCase))
                    result.Mismatches.Add(new LinkedMaterialMismatch
                    {
                        MaterialName = host.Name ?? "",
                        LinkedDocTitle = linkedDocTitle,
                        Field = "Class",
                        HostValue = hClass,
                        LinkValue = lClass,
                    });

                // Cost
                double hCost = ReadDouble(host, BuiltInParameter.ALL_MODEL_COST);
                double lCost = ReadDouble(link, BuiltInParameter.ALL_MODEL_COST);
                if (Math.Abs(hCost - lCost) > 0.01)
                    result.Mismatches.Add(new LinkedMaterialMismatch
                    {
                        MaterialName = host.Name ?? "",
                        LinkedDocTitle = linkedDocTitle,
                        Field = "Cost",
                        HostValue = hCost.ToString("F2"),
                        LinkValue = lCost.ToString("F2"),
                    });

                // Carbon
                double hCarbon = ReadSharedDouble(host, "STING_EMB_CARBON_NR");
                double lCarbon = ReadSharedDouble(link, "STING_EMB_CARBON_NR");
                if (Math.Abs(hCarbon - lCarbon) > 0.01)
                    result.Mismatches.Add(new LinkedMaterialMismatch
                    {
                        MaterialName = host.Name ?? "",
                        LinkedDocTitle = linkedDocTitle,
                        Field = "Carbon",
                        HostValue = hCarbon.ToString("F1"),
                        LinkValue = lCarbon.ToString("F1"),
                    });
            }
            catch (Exception ex) { StingLog.WarnRateLimited("LinkedMat.Cmp", $"CompareAndReport: {ex.Message}"); }
        }

        private static double ReadDouble(Material mat, BuiltInParameter bip)
        {
            try
            {
                var p = mat.get_Parameter(bip);
                if (p != null && p.StorageType == StorageType.Double) return p.AsDouble();
            }
            catch (Exception ex) { StingLog.WarnRateLimited("LinkedMat.ReadDbl", $"ReadDouble: {ex.Message}"); }
            return 0;
        }

        private static double ReadSharedDouble(Material mat, string name)
        {
            try
            {
                var p = mat.LookupParameter(name);
                if (p != null && p.StorageType == StorageType.Double) return p.AsDouble();
            }
            catch (Exception ex) { StingLog.WarnRateLimited("LinkedMat.ReadShared", $"ReadSharedDouble: {ex.Message}"); }
            return 0;
        }
    }
}
