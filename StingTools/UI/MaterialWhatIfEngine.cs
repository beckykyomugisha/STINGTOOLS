using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// N+11 — What-if material swap engine.
    ///
    /// Answers "if I switch every element using BLE_Concrete_C40 to
    /// BLE_Concrete_C30, what does the cost + carbon delta look like —
    /// BEFORE I commit?". Tally / One Click LCA's headline feature.
    ///
    /// Pure preview is read-only over the BOQDocument; the optional
    /// Commit() path reuses the proven repoint-usages pattern from
    /// MaterialDuplicateFinder.Merge so a single Transaction either
    /// flips every match or none of them (Ctrl+Z reverts the whole
    /// batch).
    /// </summary>
    public class WhatIfPreviewRow
    {
        public long ElementId { get; set; }
        public string ElementName { get; set; }
        public string Category { get; set; }
        public double Quantity { get; set; }
        public string Unit { get; set; }
        public double OldCost { get; set; }
        public double NewCost { get; set; }
        public double OldCarbon { get; set; }
        public double NewCarbon { get; set; }

        public double CostDelta => NewCost - OldCost;
        public double CarbonDelta => NewCarbon - OldCarbon;
    }

    public class WhatIfPreview
    {
        public string FromMaterial { get; set; }
        public string ToMaterial { get; set; }
        public string CategoryFilter { get; set; }
        public List<WhatIfPreviewRow> Rows { get; } = new List<WhatIfPreviewRow>();

        public int ElementCount => Rows.Count;
        public double OldCostTotal => Rows.Sum(r => r.OldCost);
        public double NewCostTotal => Rows.Sum(r => r.NewCost);
        public double OldCarbonTotal => Rows.Sum(r => r.OldCarbon);
        public double NewCarbonTotal => Rows.Sum(r => r.NewCarbon);
        public double CostDeltaTotal => NewCostTotal - OldCostTotal;
        public double CarbonDeltaTotal => NewCarbonTotal - OldCarbonTotal;
    }

    public static class MaterialWhatIfEngine
    {
        /// <summary>
        /// Read-only — walks the document, finds every element using
        /// <paramref name="fromMaterial"/> (optionally filtered by
        /// category), computes cost + carbon as-is and as-if-swapped.
        /// </summary>
        public static WhatIfPreview Preview(Document doc, string fromMaterialName,
            string toMaterialName, string categoryFilter = null)
        {
            var preview = new WhatIfPreview
            {
                FromMaterial = fromMaterialName,
                ToMaterial = toMaterialName,
                CategoryFilter = categoryFilter,
            };
            if (doc == null || string.IsNullOrEmpty(fromMaterialName) || string.IsNullOrEmpty(toMaterialName)) return preview;

            try
            {
                var fromMat = FindMaterial(doc, fromMaterialName);
                var toMat = FindMaterial(doc, toMaterialName);
                if (fromMat == null || toMat == null) return preview;

                // Per-material cost / carbon factors (use the same chain the
                // BOQ uses, so the preview matches what BOQ would compute
                // after commit).
                double fromCost = MaterialLookupCsv.GetCost(fromMaterialName);
                double toCost   = MaterialLookupCsv.GetCost(toMaterialName);
                double fromCarbon = MaterialLookupCsv.GetCarbon(fromMaterialName);
                double toCarbon   = MaterialLookupCsv.GetCarbon(toMaterialName);
                // Element-param fallback
                fromCost = OverrideFromParam(fromMat, BuiltInParameter.ALL_MODEL_COST, fromCost);
                toCost   = OverrideFromParam(toMat,   BuiltInParameter.ALL_MODEL_COST, toCost);
                fromCarbon = OverrideFromSharedParam(fromMat, "STING_EMB_CARBON_NR", fromCarbon);
                toCarbon   = OverrideFromSharedParam(toMat,   "STING_EMB_CARBON_NR", toCarbon);

                foreach (var el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    if (!string.IsNullOrEmpty(categoryFilter) &&
                        !string.Equals(el.Category?.Name, categoryFilter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!ElementUsesMaterial(el, fromMat.Id)) continue;

                    double qty = EstimateQuantity(el, out string unit);
                    preview.Rows.Add(new WhatIfPreviewRow
                    {
                        ElementId   = el.Id?.Value ?? 0,
                        ElementName = el.Name ?? "",
                        Category    = el.Category?.Name ?? "",
                        Quantity    = qty,
                        Unit        = unit,
                        OldCost     = qty * fromCost,
                        NewCost     = qty * toCost,
                        OldCarbon   = qty * fromCarbon,
                        NewCarbon   = qty * toCarbon,
                    });
                }
            }
            catch (Exception ex) { StingLog.Error("MaterialWhatIfEngine.Preview", ex); }
            return preview;
        }

        /// <summary>
        /// Apply a previewed swap. Walks every element in the preview,
        /// repoints its Material parameter from the old to the new
        /// material. Single Transaction so Ctrl+Z reverts the batch.
        /// </summary>
        public static int Commit(Document doc, WhatIfPreview preview)
        {
            if (doc == null || preview == null || preview.Rows.Count == 0) return 0;
            var fromMat = FindMaterial(doc, preview.FromMaterial);
            var toMat   = FindMaterial(doc, preview.ToMaterial);
            if (fromMat == null || toMat == null) return 0;
            int written = 0;
            using (var t = new Transaction(doc, $"STING What-If: {preview.FromMaterial} → {preview.ToMaterial}"))
            {
                t.Start();
                foreach (var row in preview.Rows)
                {
                    try
                    {
                        var el = doc.GetElement(new ElementId(row.ElementId));
                        if (el == null) continue;
                        var p = el.LookupParameter("Material") ?? el.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                        if (p == null || p.IsReadOnly || p.StorageType != StorageType.ElementId) continue;
                        if (p.AsElementId() == fromMat.Id)
                        { p.Set(toMat.Id); written++; }
                    }
                    catch (Exception ex) { StingLog.Warn($"WhatIfEngine Commit {row.ElementId}: {ex.Message}"); }
                }
                t.Commit();
            }
            MaterialAuditLogger.Log(doc, "MAT_WhatIfSwap", preview.ToMaterial,
                new Dictionary<string, object>
                {
                    ["from"] = preview.FromMaterial,
                    ["to"]   = preview.ToMaterial,
                    ["elementsAffected"]   = written,
                    ["costDelta"]          = preview.CostDeltaTotal,
                    ["carbonDeltaKg"]      = preview.CarbonDeltaTotal,
                });
            return written;
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static Material FindMaterial(Document doc, string name)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static bool ElementUsesMaterial(Element el, ElementId matId)
        {
            try
            {
                var p = el.LookupParameter("Material") ?? el.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (p != null && p.StorageType == StorageType.ElementId && p.AsElementId() == matId)
                    return true;
                var mats = el.GetMaterialIds(false);
                if (mats != null)
                    foreach (var m in mats)
                        if (m == matId) return true;
            }
            catch (Exception ex) { StingLog.WarnRateLimited("WhatIf.Uses", $"ElementUsesMaterial {el?.Id}: {ex.Message}"); }
            return false;
        }

        private static double OverrideFromParam(Material mat, BuiltInParameter bip, double fallback)
        {
            try
            {
                var p = mat.get_Parameter(bip);
                if (p != null && p.StorageType == StorageType.Double && p.AsDouble() > 0)
                    return p.AsDouble();
            }
            catch (Exception ex) { StingLog.WarnRateLimited("WhatIf.Param", $"OverrideFromParam: {ex.Message}"); }
            return fallback;
        }

        private static double OverrideFromSharedParam(Material mat, string paramName, double fallback)
        {
            try
            {
                var p = mat.LookupParameter(paramName);
                if (p != null && p.StorageType == StorageType.Double && p.AsDouble() > 0)
                    return p.AsDouble();
            }
            catch (Exception ex) { StingLog.WarnRateLimited("WhatIf.Shared", $"OverrideFromSharedParam: {ex.Message}"); }
            return fallback;
        }

        private static double EstimateQuantity(Element el, out string unit)
        {
            // R-3 — UnitUtils-based conversion. Revit's internal unit is
            // not always ft³ — for templates with metric bias the
            // hardcoded factors are wrong. UnitUtils picks the right
            // input unit per document.
            try
            {
                var v = el.LookupParameter("Volume");
                if (v != null && v.HasValue && v.StorageType == StorageType.Double)
                { unit = "m³"; return UnitUtils.ConvertFromInternalUnits(v.AsDouble(), UnitTypeId.CubicMeters); }
                var a = el.LookupParameter("Area");
                if (a != null && a.HasValue && a.StorageType == StorageType.Double)
                { unit = "m²"; return UnitUtils.ConvertFromInternalUnits(a.AsDouble(), UnitTypeId.SquareMeters); }
                var l = el.LookupParameter("Length");
                if (l != null && l.HasValue && l.StorageType == StorageType.Double)
                { unit = "m"; return UnitUtils.ConvertFromInternalUnits(l.AsDouble(), UnitTypeId.Meters); }
            }
            catch (Exception ex) { StingLog.WarnRateLimited("WhatIf.Qty", $"EstimateQuantity: {ex.Message}"); }
            unit = "each";
            return 1;
        }
    }
}
