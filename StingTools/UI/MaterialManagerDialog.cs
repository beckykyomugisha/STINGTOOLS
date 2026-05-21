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

namespace StingTools.UI
{
    /// <summary>
    /// STING Material Manager — proper WPF replacement for the legacy
    /// TaskDialog. Three tabs (Browse / Create / Export) on top of a
    /// shared materials collection. Result.Operation carries the tag of
    /// any follow-up command the caller should run; otherwise the dialog
    /// has already executed (browse / export are inline).
    /// </summary>
    public class MaterialManagerResult
    {
        public bool Confirmed { get; set; }
        public string Operation { get; set; } // "CreateBLEMaterials" | "CreateMEPMaterials" | ""
    }

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

            return new MaterialRow
            {
                Name = n,
                Class = ov?.Class ?? m.MaterialClass ?? "",
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
            };
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

    internal static class MaterialManagerDialog
    {
        public static MaterialManagerResult Show(UIDocument uidoc)
        {
            var result = new MaterialManagerResult();
            if (uidoc == null) return result;
            var doc = uidoc.Document;

            // ── Collect materials once ──
            var materials = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .OrderBy(m => m.Name)
                .ToList();

            var rows = new ObservableCollection<MaterialRow>(materials.Select(m =>
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
                catch (Exception ex) { StingLog.Warn($"MaterialMgr color '{n}': {ex.Message}"); }
                return new MaterialRow
                {
                    Name = n,
                    Class = m.MaterialClass ?? "",
                    Origin = origin,
                    ColorText = colTxt,
                    ColorSwatch = swatch,
                    Id = m.Id,
                };
            }));

            int stingCount = rows.Count(r => r.Origin != "Other");

            // ── Window shell ──
            var win = new Window
            {
                Title = "STING Material Manager",
                Width = 880,
                Height = 620,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResize,
            };
            try
            {
                var hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (hwnd != IntPtr.Zero) new System.Windows.Interop.WindowInteropHelper(win).Owner = hwnd;
            }
            catch (Exception ex) { StingLog.Warn($"MaterialMgr owner: {ex.Message}"); }

            var root = new DockPanel { LastChildFill = true };

            // Header strip
            var header = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F, 0x2A, 0x3A)),
                Padding = new Thickness(14, 10, 14, 10),
            };
            var headerSp = new StackPanel { Orientation = Orientation.Horizontal };
            headerSp.Children.Add(new TextBlock
            {
                Text = "Material Manager",
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            });
            headerSp.Children.Add(new TextBlock
            {
                Text = $"   {rows.Count} materials · {stingCount} STING/BLE/MEP",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB0, 0xC4, 0xDE)),
                FontSize = 12,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            header.Child = headerSp;
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // Footer
            var footer = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF0, 0xF3, 0xF8)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD0, 0xD7, 0xE0)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 8, 12, 8),
            };
            var footerSp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnSelect = MakeFooterBtn("Select in Project", "Set Revit selection to the material's host elements (if any).");
            var btnApply  = MakeFooterBtn("Apply to Selection", "Paint the chosen material onto every currently-selected element that has a Material parameter.");
            var btnClose  = MakeFooterBtn("Close", "");
            footerSp.Children.Add(btnSelect);
            footerSp.Children.Add(btnApply);
            footerSp.Children.Add(btnClose);
            footer.Child = footerSp;
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            // ── Body: TabControl ──
            var tabs = new TabControl { Margin = new Thickness(0) };

            // Tab 1 — Browse
            tabs.Items.Add(BuildBrowseTab(rows, out var listView, out var searchBox, out var originFilter));

            // Tab 2 — Create
            tabs.Items.Add(BuildCreateTab(result, win));

            // Tab 3 — Export
            tabs.Items.Add(BuildExportTab(doc, materials));

            root.Children.Add(tabs);
            win.Content = root;

            // ── Footer wiring ──
            btnClose.Click += (_, __) => { result.Confirmed = false; win.Close(); };
            btnSelect.Click += (_, __) =>
            {
                if (listView.SelectedItem is MaterialRow row && row.Id != null && row.Id != ElementId.InvalidElementId)
                {
                    try
                    {
                        var hosts = new FilteredElementCollector(doc)
                            .WhereElementIsNotElementType()
                            .Where(e => ElementHasMaterial(e, row.Id))
                            .Select(e => e.Id)
                            .ToList();
                        if (hosts.Count == 0)
                        {
                            TaskDialog.Show("Material Manager", $"No elements found using '{row.Name}'.");
                            return;
                        }
                        uidoc.Selection.SetElementIds(hosts);
                        TaskDialog.Show("Material Manager", $"Selected {hosts.Count} element(s) using '{row.Name}'.");
                    }
                    catch (Exception ex) { TaskDialog.Show("Material Manager", $"Select failed: {ex.Message}"); }
                }
                else TaskDialog.Show("Material Manager", "Pick a material in the Browse tab first.");
            };
            btnApply.Click += (_, __) =>
            {
                if (!(listView.SelectedItem is MaterialRow row) || row.Id == null || row.Id == ElementId.InvalidElementId)
                { TaskDialog.Show("Material Manager", "Pick a material in the Browse tab first."); return; }
                var sel = uidoc.Selection.GetElementIds();
                if (sel.Count == 0)
                { TaskDialog.Show("Material Manager", "Nothing selected in Revit."); return; }
                int written = 0;
                using (var t = new Transaction(doc, $"STING Apply Material '{row.Name}'"))
                {
                    t.Start();
                    foreach (var id in sel)
                    {
                        try
                        {
                            var el = doc.GetElement(id);
                            var p = el?.LookupParameter("Material") ?? el?.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.ElementId)
                            { p.Set(row.Id); written++; }
                        }
                        catch (Exception ex) { StingLog.Warn($"MaterialMgr apply {id}: {ex.Message}"); }
                    }
                    t.Commit();
                }
                TaskDialog.Show("Material Manager", $"Material '{row.Name}' applied to {written} of {sel.Count} selected element(s).");
            };

            // Search + filter
            var view = CollectionViewSource.GetDefaultView(rows);
            view.Filter = item =>
            {
                if (!(item is MaterialRow r)) return false;
                string q = searchBox.Text?.Trim() ?? "";
                string filt = (originFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
                bool originOk = filt == "All" || string.Equals(r.Origin, filt, StringComparison.OrdinalIgnoreCase);
                bool textOk = string.IsNullOrEmpty(q)
                              || (r.Name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                              || (r.Class ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                return originOk && textOk;
            };
            searchBox.TextChanged += (_, __) => view.Refresh();
            originFilter.SelectionChanged += (_, __) => view.Refresh();

            win.ShowDialog();
            return result;
        }

        private static TabItem BuildBrowseTab(
            ObservableCollection<MaterialRow> rows,
            out ListView listView,
            out TextBox searchBox,
            out ComboBox originFilter)
        {
            var sp = new DockPanel { LastChildFill = true, Margin = new Thickness(10) };

            // Toolbar
            var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchBox = new TextBox { Padding = new Thickness(6, 4, 6, 4) };
            Grid.SetColumn(searchBox, 0);
            toolbar.Children.Add(searchBox);
            originFilter = new ComboBox { Margin = new Thickness(8, 0, 0, 0), Width = 110 };
            originFilter.Items.Add(new ComboBoxItem { Content = "All", IsSelected = true });
            originFilter.Items.Add(new ComboBoxItem { Content = "STING" });
            originFilter.Items.Add(new ComboBoxItem { Content = "BLE" });
            originFilter.Items.Add(new ComboBoxItem { Content = "MEP" });
            originFilter.Items.Add(new ComboBoxItem { Content = "Other" });
            Grid.SetColumn(originFilter, 1);
            toolbar.Children.Add(originFilter);
            DockPanel.SetDock(toolbar, Dock.Top);
            sp.Children.Add(toolbar);

            // Hint row
            var hint = new TextBlock
            {
                Text = "Search by name or class. Filter by origin (STING / BLE / MEP / Other). "
                     + "Pick a row, then use the footer to Select in Project or Apply to Selection.",
                FontSize = 11,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x60, 0x70, 0x80)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            };
            DockPanel.SetDock(hint, Dock.Top);
            sp.Children.Add(hint);

            // ListView
            listView = new ListView { ItemsSource = rows, SelectionMode = SelectionMode.Single };
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn
            {
                Header = "Color",
                Width = 60,
                CellTemplate = MakeColorCellTemplate(),
            });
            gv.Columns.Add(new GridViewColumn { Header = "Name",   DisplayMemberBinding = new Binding("Name"),   Width = 320 });
            gv.Columns.Add(new GridViewColumn { Header = "Class",  DisplayMemberBinding = new Binding("Class"),  Width = 180 });
            gv.Columns.Add(new GridViewColumn { Header = "Origin", DisplayMemberBinding = new Binding("Origin"), Width = 90 });
            gv.Columns.Add(new GridViewColumn { Header = "RGB",    DisplayMemberBinding = new Binding("ColorText"), Width = 110 });
            listView.View = gv;
            sp.Children.Add(listView);

            return new TabItem { Header = "Browse", Content = sp };
        }

        private static TabItem BuildCreateTab(MaterialManagerResult result, Window win)
        {
            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(new TextBlock
            {
                Text = "Create materials from STING's CSV catalogues. These commands are idempotent — re-running will skip materials that already exist.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16),
                FontSize = 12,
            });

            var ble = MakeCreateCard("Building elements (BLE)",
                "815 materials covering walls, floors, ceilings, roofs, doors, windows, insulation, finishes.",
                "Create BLE Materials");
            ble.Click += (_, __) => { result.Confirmed = true; result.Operation = "CreateBLEMaterials"; win.Close(); };

            var mep = MakeCreateCard("MEP services",
                "464 materials covering ducts, pipes, conduits, cable trays, plumbing fixtures, insulation.",
                "Create MEP Materials");
            mep.Click += (_, __) => { result.Confirmed = true; result.Operation = "CreateMEPMaterials"; win.Close(); };

            sp.Children.Add(ble);
            sp.Children.Add(new Border { Height = 8 });
            sp.Children.Add(mep);
            return new TabItem { Header = "Create", Content = sp };
        }

        private static TabItem BuildExportTab(Document doc, List<Material> materials)
        {
            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(new TextBlock
            {
                Text = "Export all project materials to CSV (Name, Class, Color, Transparency, Smoothness, Shininess, "
                     + "Description, Manufacturer, Model, Cost, Keynote, Mark, URL, thermal + structural properties).",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16),
                FontSize = 12,
            });
            var btn = MakeFooterBtn("Export Material List…", "Write all material properties to CSV under the project output folder.");
            btn.HorizontalAlignment = HorizontalAlignment.Left;
            btn.Padding = new Thickness(20, 8, 20, 8);
            btn.Click += (_, __) =>
            {
                try
                {
                    string outDir = OutputLocationHelper.GetOutputDirectory(doc);
                    string filePath = System.IO.Path.Combine(outDir, "STING_MATERIALS_EXPORT.csv");
                    StingTools.Temp.MaterialPropertyHelper.ExportMaterialsCsv(doc, materials, filePath);
                    TaskDialog.Show("Material Manager", $"Exported {materials.Count} material(s) to:\n{filePath}");
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true })?.Dispose(); }
                    catch (Exception ex) { StingLog.Warn($"MaterialMgr open csv: {ex.Message}"); }
                }
                catch (Exception ex) { TaskDialog.Show("Material Manager", $"Export failed: {ex.Message}"); }
            };
            sp.Children.Add(btn);
            return new TabItem { Header = "Export", Content = sp };
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Button MakeFooterBtn(string text, string tooltip)
        {
            return new Button
            {
                Content = text,
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = string.IsNullOrEmpty(tooltip) ? null : tooltip,
                MinWidth = 110,
            };
        }

        private static Button MakeCreateCard(string title, string subtitle, string btnText)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
            sp.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x60, 0x70, 0x80)),
                Margin = new Thickness(0, 2, 0, 6),
                TextWrapping = TextWrapping.Wrap,
            });
            sp.Children.Add(new TextBlock { Text = btnText, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x18, 0x68, 0xBA)) });
            return new Button
            {
                Content = sp,
                Padding = new Thickness(14, 10, 14, 10),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF7, 0xFA, 0xFD)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xD7, 0xE0)),
                BorderThickness = new Thickness(1),
            };
        }

        private static DataTemplate MakeColorCellTemplate()
        {
            const string xaml =
                "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "             xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" +
                "  <Border Background='{Binding ColorSwatch}' Width='34' Height='16' " +
                "          BorderBrush='#888' BorderThickness='1' CornerRadius='2'/>" +
                "</DataTemplate>";
            using (var ms = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(xaml)))
                return (DataTemplate)System.Windows.Markup.XamlReader.Load(ms);
        }

        private static bool ElementHasMaterial(Element el, ElementId matId)
        {
            try
            {
                // Check Materials collection (compound elements like Walls/Floors)
                var mats = el.GetMaterialIds(false);
                if (mats != null)
                    foreach (var m in mats)
                        if (m == matId) return true;

                // Check direct "Material" parameter (MEP fittings, hosted components)
                var p = el.LookupParameter("Material") ?? el.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (p != null && p.StorageType == StorageType.ElementId && p.AsElementId() == matId) return true;
            }
            catch (Exception ex) { StingLog.Warn($"ElementHasMaterial {el?.Id}: {ex.Message}"); }
            return false;
        }
    }
}
