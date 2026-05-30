using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

// Autodesk.Revit.UI ships TextBox + ComboBox types that collide with the
// System.Windows.Controls equivalents used by this WPF dialog. Alias the
// WPF types so the call sites below stay readable.
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
// Autodesk.Revit.DB also ships Grid (datum line) + Binding (param binding)
// that collide with System.Windows.Controls.Grid + System.Windows.Data.Binding.
using Grid = System.Windows.Controls.Grid;
using Binding = System.Windows.Data.Binding;

namespace StingTools.UI
{
    // ----- MaterialRow + EpdFreshness + MaterialRowBuilder extracted from former
    // MaterialManagerDialog.cs (Phase D Material consolidation regression fix).
    // The dialog Window class itself is correctly deleted; these POCOs and the
    // builder are consumed by Material Hub (MaterialHubPanel.* + several support
    // files + StingDockPanel.xaml.cs).

    public class MaterialRow : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Class { get; set; }
        public string Origin { get; set; } // STING / BLE / MEP / Other
        public string ColorText { get; set; }
        public Brush ColorSwatch { get; set; }
        public ElementId Id { get; set; }

        // Phase B fields — populated by MaterialRowBuilder.
        public int UsageCount { get; set; }
        public double Cost { get; set; }
        public double CarbonKgCo2e { get; set; }
        public string CostText
        {
            get
            {
                var loc = ActiveLocale;
                return loc != null ? loc.FormatCost(Cost) : (Cost > 0 ? Cost.ToString("F0") : "");
            }
        }
        public string CarbonText
        {
            get
            {
                var loc = ActiveLocale;
                return loc != null ? loc.FormatCarbon(CarbonKgCo2e) : (CarbonKgCo2e > 0 ? CarbonKgCo2e.ToString("F0") : "");
            }
        }

        /// <summary>Active locale shared by every row — set by MaterialRowBuilder.Build.</summary>
        public static MaterialLocale ActiveLocale { get; set; }

        // Asset-share counts (filled by MaterialRowBuilder + AssetShareCounter).
        public int AppearanceSharedBy { get; set; }
        public int PhysicalSharedBy { get; set; }
        public int ThermalSharedBy { get; set; }

        // EPD provenance + freshness — A8.
        public string EpdSource { get; set; }
        public string EpdDate { get; set; }
        public EpdFreshness EpdFreshness { get; set; } = EpdFreshness.Unknown;

        // A3 — Uniclass 2015 Pr_ binding (resolved from Class via
        // MaterialUniclassMapper). Empty when no rule matches.
        public string UniclassCode { get; set; } = "";
        public string UniclassTitle { get; set; } = "";

        // A5/G29 — NRM2 cost dimensionality. Cost stays the "total" /
        // legacy single-number for back-compat; Supply + Install + VAT
        // optional split for tender-grade procurement output.
        public double SupplyCost { get; set; }
        public double InstallCost { get; set; }
        public double VatPct { get; set; } = 20; // UK default; project override via Region

        /// <summary>True when the supply/install split is meaningful — at
        /// least one of the split fields is populated. Drives whether the
        /// RFQ generator emits per-line split rates.</summary>
        public bool HasCostSplit => SupplyCost > 0 || InstallCost > 0;

        // Priority 9 — Lifecycle state pill data (Draft / Reviewed / Approved / Frozen).
        public string LifecycleText => MaterialLifecycle.Read(this);
        public Brush LifecycleBrush
        {
            get
            {
                switch (LifecycleText)
                {
                    case "Reviewed": return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xA0, 0x10));
                    case "Approved": return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2C, 0xA0, 0x2C));
                    case "Frozen":   return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F, 0x4A, 0x90));
                    default:         return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x90, 0x90, 0x90));
                }
            }
        }

        // Power-user "pin / star" bookmark flag — persisted via session memory.
        public bool IsBookmarked { get; set; }

        // Suppress CS0067 — the event is required by the INotifyPropertyChanged
        // contract but the grid binds one-way to immutable rows so we never
        // raise it. Future bind-back work will exercise this surface.
#pragma warning disable CS0067
        public string EpdFreshnessText => EpdFreshness switch
        {
            EpdFreshness.Fresh   => "✓ Fresh",
            EpdFreshness.Stale   => "△ Stale",
            EpdFreshness.Expired => "✗ Expired",
            EpdFreshness.Missing => "— Missing",
            _ => "—",
        };
        public Brush EpdFreshnessBrush => EpdFreshness switch
        {
            EpdFreshness.Fresh   => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2C, 0xA0, 0x2C)),
            EpdFreshness.Stale   => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xA0, 0x10)),
            EpdFreshness.Expired => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x30, 0x30)),
            _                    => Brushes.Gray,
        };

        public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore CS0067
    }

    public enum EpdFreshness { Unknown, Missing, Fresh, Stale, Expired }

    /// <summary>
    /// Constructs <see cref="MaterialRow"/> values from a Revit document.
    /// Usage counts are O(N) over modelled elements — call once on Refresh
    /// and reuse the collection thereafter.
    /// </summary>
    public static class MaterialRowBuilder
    {
        public static System.Collections.ObjectModel.ObservableCollection<MaterialRow> Build(Document doc)
        {
            var rows = new System.Collections.ObjectModel.ObservableCollection<MaterialRow>();
            if (doc == null) return rows;
            // Pin the active locale before building rows so currency / carbon
            // formatters get the right symbols + thousands separators.
            MaterialRow.ActiveLocale = MaterialLocaleManager.Resolve(doc);
            var materials = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .OrderBy(m => m.Name)
                .ToList();
            var usage = ComputeUsageCounts(doc);
            var shares = AssetShareCounter.Compute(materials);
            foreach (var m in materials)
                rows.Add(BuildOne(doc, m, usage, shares));
            return rows;
        }

        public static MaterialRow BuildOne(Document doc, Material m,
            IDictionary<long, int> usageMap = null,
            IDictionary<long, AssetShareCount> assetShareMap = null)
        {
            string n = m.Name ?? "(unnamed)";
            string origin =
                n.StartsWith("STING", StringComparison.OrdinalIgnoreCase) ? "STING" :
                n.StartsWith("BLE_",  StringComparison.OrdinalIgnoreCase) ? "BLE" :
                n.StartsWith("MEP_",  StringComparison.OrdinalIgnoreCase) ? "MEP" : "Other";
            string colTxt = ""; Brush swatch = Brushes.Transparent;
            try
            {
                var c = m.Color;
                if (c != null && c.IsValid)
                {
                    colTxt = $"{c.Red:000} {c.Green:000} {c.Blue:000}";
                    swatch = new SolidColorBrush(System.Windows.Media.Color.FromRgb(c.Red, c.Green, c.Blue));
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildOne color '{n}': {ex.Message}"); }

            // ── Cost + carbon resolution chain ──
            //   1) Project override JSON  → wins if present
            //   2) Element Material params (ALL_MODEL_COST / STING_EMB_CARBON_NR)
            //   3) Corporate MATERIAL_LOOKUP.csv lookup by name
            double cost = 0, carbon = 0;
            var ov = MaterialOverrideRegistry.ResolveOverride(doc, n);
            if (ov?.Cost.HasValue == true) cost = ov.Cost.Value;
            else
            {
                try
                {
                    var cp = m.get_Parameter(BuiltInParameter.ALL_MODEL_COST);
                    if (cp != null && cp.StorageType == StorageType.Double) cost = cp.AsDouble();
                }
                catch (Exception ex) { StingLog.Warn($"BuildOne cost '{n}': {ex.Message}"); }
                if (cost <= 0) cost = MaterialLookupCsv.GetCost(n);
            }
            if (ov?.CarbonKgCo2e.HasValue == true) carbon = ov.CarbonKgCo2e.Value;
            else
            {
                try
                {
                    var lp = m.LookupParameter("STING_EMB_CARBON_NR");
                    if (lp != null && lp.StorageType == StorageType.Double) carbon = lp.AsDouble();
                }
                catch (Exception ex) { StingLog.Warn($"BuildOne carbon '{n}': {ex.Message}"); }
                if (carbon <= 0) carbon = MaterialLookupCsv.GetCarbon(n);
            }

            int use = 0;
            if (usageMap != null && m.Id != null) usageMap.TryGetValue(m.Id.Value, out use);

            // Asset-share counts (filled per-pass in MaterialRowBuilder.Build).
            int appShared = 0, phyShared = 0, thrShared = 0;
            if (assetShareMap != null && m.Id != null &&
                assetShareMap.TryGetValue(m.Id.Value, out var sh))
            { appShared = sh.Appearance; phyShared = sh.Physical; thrShared = sh.Thermal; }

            // EPD source / date / freshness — A8.
            // Resolution: project override → STING_MAT_EPD_* params on the material.
            string epdSrc = ov?.EpdSource ?? ReadStringParam(m, "STING_MAT_EPD_SRC_TXT");
            string epdDate = ov?.EpdDate ?? ReadStringParam(m, "STING_MAT_EPD_DATE_TXT");
            EpdFreshness fresh = ComputeFreshness(epdDate, carbon);

            string classText = ov?.Class ?? m.MaterialClass ?? "";
            var uniclass = MaterialUniclassMapper.Resolve(classText);

            // A5/G29 — Cost split shared params (NUMBER storage).
            double supply = ReadDoubleParam(m, "MAT_COST_SUPPLY_NR");
            double install = ReadDoubleParam(m, "MAT_COST_INSTALL_NR");
            double vatPct = ReadDoubleParam(m, "MAT_VAT_PCT_NR");
            if (vatPct <= 0) vatPct = 20.0;

            return new MaterialRow
            {
                Name = n,
                Class = classText,
                Origin = origin,
                ColorText = colTxt,
                ColorSwatch = swatch,
                Id = m.Id,
                UsageCount = use,
                Cost = cost,
                CarbonKgCo2e = carbon,
                AppearanceSharedBy = appShared,
                PhysicalSharedBy = phyShared,
                ThermalSharedBy = thrShared,
                EpdSource = epdSrc ?? "",
                EpdDate = epdDate ?? "",
                EpdFreshness = fresh,
                UniclassCode = uniclass?.Code ?? "",
                UniclassTitle = uniclass?.Title ?? "",
                SupplyCost = supply,
                InstallCost = install,
                VatPct = vatPct,
            };
        }

        private static double ReadDoubleParam(Material m, string paramName)
        {
            try
            {
                var p = m?.LookupParameter(paramName);
                if (p != null && p.HasValue && p.StorageType == StorageType.Double) return p.AsDouble();
            }
            catch (Exception ex) { StingLog.WarnRateLimited("RowBuilder.ReadDouble", $"ReadDoubleParam '{paramName}': {ex.Message}"); }
            return 0;
        }

        private static string ReadStringParam(Material m, string paramName)
        {
            try
            {
                var p = m?.LookupParameter(paramName);
                if (p != null && p.HasValue && p.StorageType == StorageType.String) return p.AsString();
            }
            catch (Exception ex) { StingLog.Warn($"ReadStringParam '{paramName}': {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// EPDs are typically valid for 5 years (EN 15804). Less than 4 years
        /// → Fresh. 4–5 → Stale. Over 5 → Expired. No date but carbon present
        /// → Stale (we have data but no provenance). No date and no carbon →
        /// Missing.
        /// </summary>
        private static EpdFreshness ComputeFreshness(string epdDateStr, double carbonKg)
        {
            if (string.IsNullOrWhiteSpace(epdDateStr))
                return carbonKg > 0 ? EpdFreshness.Stale : EpdFreshness.Missing;
            if (!DateTime.TryParse(epdDateStr, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
                return EpdFreshness.Unknown;
            var ageYears = (DateTime.UtcNow - dt.ToUniversalTime()).TotalDays / 365.25;
            if (ageYears < 4)  return EpdFreshness.Fresh;
            if (ageYears < 5)  return EpdFreshness.Stale;
            return EpdFreshness.Expired;
        }

        /// <summary>
        /// One-pass usage count over every modelled element. Counts:
        ///   • compound element layers (GetMaterialIds)
        ///   • direct Material parameter / MATERIAL_ID_PARAM
        /// Returns a dictionary keyed by Material ElementId.Value.
        /// </summary>
        public static IDictionary<long, int> ComputeUsageCounts(Document doc)
        {
            var map = new Dictionary<long, int>();
            if (doc == null) return map;
            try
            {
                var elements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElements();
                foreach (var el in elements)
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
                    catch (Exception ex) { StingLog.Warn($"ComputeUsageCounts {el?.Id}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ComputeUsageCounts: {ex.Message}"); }
            return map;
        }
    }

}
