using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    /// <summary>
    /// N+4 — Material-aware title-block tokens.
    /// Resolved by <see cref="TitleBlockParamApplier.ResolveTemplate"/>
    /// when a template carries <c>${MAT_xxx}</c>. Falls through to
    /// ProjectInfo for unknown keys so the existing applier semantics
    /// remain intact.
    ///
    /// Supported tokens (more can be added without touching the applier):
    ///
    ///   ${MAT_PRIMARY_NAME}         Most-used material in the project.
    ///   ${MAT_PRIMARY_CLASS}        Class of the most-used material.
    ///   ${MAT_LIBRARY_COUNT}        Total material count.
    ///   ${MAT_LIBRARY_COST_TOTAL}   Σ of every material's Cost.
    ///   ${MAT_LIBRARY_CARBON_TOTAL} Σ of every material's STING_EMB_CARBON_NR.
    ///   ${MAT_LIBRARY_CARBON_T}     Same as above but in tonnes (kg ÷ 1000).
    ///   ${MAT_EPD_FRESH_PCT}        % of materials with fresh / non-missing EPDs.
    ///   ${MAT_UNUSED_COUNT}         Materials with zero modelled usage.
    ///
    /// Results are computed on demand — no cache. Title-block applies
    /// run once per sheet; the perf cost (one FilteredElementCollector
    /// per token expansion) is negligible vs Revit's stamp transaction.
    /// </summary>
    public static class MaterialTitleBlockTokens
    {
        public static string Resolve(Document doc, string tokenName)
        {
            if (doc == null || string.IsNullOrEmpty(tokenName)) return null;
            try
            {
                switch (tokenName)
                {
                    case "MAT_PRIMARY_NAME":         return PrimaryMaterial(doc)?.name ?? "";
                    case "MAT_PRIMARY_CLASS":        return PrimaryMaterial(doc)?.cls  ?? "";
                    case "MAT_LIBRARY_COUNT":        return CountMaterials(doc).ToString();
                    case "MAT_LIBRARY_COST_TOTAL":   return SumCost(doc).ToString("F0");
                    case "MAT_LIBRARY_CARBON_TOTAL": return SumCarbon(doc).ToString("F0");
                    case "MAT_LIBRARY_CARBON_T":     return (SumCarbon(doc) / 1000.0).ToString("F1");
                    case "MAT_EPD_FRESH_PCT":        return EpdFreshPct(doc).ToString("F0") + "%";
                    case "MAT_UNUSED_COUNT":         return UnusedCount(doc).ToString();
                    default: return null; // unknown MAT_* token — let applier fall through
                }
            }
            catch (Exception ex) { StingLog.Warn($"MaterialTitleBlockTokens.Resolve {tokenName}: {ex.Message}"); return ""; }
        }

        // ── Internals ──

        private struct PrimaryRow { public string name; public string cls; public int usage; }

        private static PrimaryRow? PrimaryMaterial(Document doc)
        {
            try
            {
                var usage = ComputeUsage(doc);
                if (usage == null || usage.Count == 0) return null;
                var top = usage.OrderByDescending(kv => kv.Value).First();
                if (doc.GetElement(new ElementId(top.Key)) is Material m)
                    return new PrimaryRow { name = m.Name ?? "", cls = m.MaterialClass ?? "", usage = top.Value };
            }
            catch (Exception ex) { StingLog.Warn($"PrimaryMaterial: {ex.Message}"); }
            return null;
        }

        private static int CountMaterials(Document doc)
            => new FilteredElementCollector(doc).OfClass(typeof(Material)).GetElementCount();

        private static double SumCost(Document doc)
        {
            double sum = 0;
            foreach (var m in new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>())
            {
                try
                {
                    var p = m.get_Parameter(BuiltInParameter.ALL_MODEL_COST);
                    if (p != null && p.StorageType == StorageType.Double) sum += p.AsDouble();
                }
                catch (Exception ex) { StingLog.Warn($"SumCost '{m.Name}': {ex.Message}"); }
            }
            return sum;
        }

        private static double SumCarbon(Document doc)
        {
            double sum = 0;
            foreach (var m in new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>())
            {
                try
                {
                    var p = m.LookupParameter("STING_EMB_CARBON_NR");
                    if (p != null && p.StorageType == StorageType.Double) sum += p.AsDouble();
                }
                catch (Exception ex) { StingLog.Warn($"SumCarbon '{m.Name}': {ex.Message}"); }
            }
            return sum;
        }

        private static double EpdFreshPct(Document doc)
        {
            int total = 0, fresh = 0;
            foreach (var m in new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>())
            {
                total++;
                try
                {
                    var ds = m.LookupParameter("STING_MAT_EPD_DATE_TXT");
                    if (ds == null || ds.StorageType != StorageType.String) continue;
                    string raw = (ds.AsString() ?? "").Trim();
                    if (string.IsNullOrEmpty(raw)) continue;
                    if (DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
                    {
                        var age = (DateTime.UtcNow - dt.ToUniversalTime()).TotalDays / 365.25;
                        if (age < 5) fresh++;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"EpdFreshPct '{m.Name}': {ex.Message}"); }
            }
            return total == 0 ? 0 : 100.0 * fresh / total;
        }

        private static int UnusedCount(Document doc)
        {
            var usage = ComputeUsage(doc);
            int total = CountMaterials(doc);
            return total - (usage?.Count ?? 0);
        }

        /// <summary>
        /// Lightweight usage counter — Material id → element count.
        /// One-pass O(N) over the doc, no extra dependency on the UI
        /// layer's MaterialRowBuilder.
        /// </summary>
        private static Dictionary<long, int> ComputeUsage(Document doc)
        {
            var map = new Dictionary<long, int>();
            if (doc == null) return map;
            try
            {
                foreach (var el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    try
                    {
                        var mats = el.GetMaterialIds(false);
                        if (mats != null)
                            foreach (var mid in mats)
                                if (mid != null && mid.Value > 0)
                                    map[mid.Value] = map.TryGetValue(mid.Value, out int v) ? v + 1 : 1;
                        Parameter p = el.LookupParameter("Material") ?? el.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                        if (p != null && p.StorageType == StorageType.ElementId)
                        {
                            var mid = p.AsElementId();
                            if (mid != null && mid.Value > 0)
                                map[mid.Value] = map.TryGetValue(mid.Value, out int v) ? v + 1 : 1;
                        }
                    }
                    catch (Exception ex) { StingLog.WarnRateLimited("MatTBT.usage", $"ComputeUsage {el?.Id}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ComputeUsage: {ex.Message}"); }
            return map;
        }
    }
}
