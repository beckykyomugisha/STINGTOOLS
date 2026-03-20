using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using StingTools.Core;
using Grid = System.Windows.Controls.Grid;
using Visibility = System.Windows.Visibility;

namespace StingTools.UI
{
    // ════════════════════════════════════════════════════════════════════
    //  StingExportDialog — BIMLink-style unified export dialog
    //
    //  A single WPF dialog that replaces ad-hoc export workflows with a
    //  comprehensive export configurator. Inspired by BIMLink, DiRootsOne,
    //  and Ideate BIMLink UI patterns:
    //
    //  ┌─────────────────────────────────────────────────────────────┐
    //  │  LEFT: Categories      │  CENTER: Parameters  │  RIGHT:    │
    //  │  ☑ Air Terminals       │  ☑ ASS_TAG_1         │  Filters   │
    //  │  ☑ Doors               │  ☑ ASS_DISCIPLINE    │  Family:   │
    //  │  ☑ Duct Accessories    │  ☑ ASS_LOC_TXT       │  [All]     │
    //  │  ☐ Electrical Equip    │  ☑ Width             │  Type:     │
    //  │  ...                   │  ☐ Cost              │  [All]     │
    //  │                        │  ...                 │            │
    //  ├────────────────────────┴──────────────────────┴────────────┤
    //  │  Format: ○ CSV  ○ Excel  ○ JSON    Location: [Browse...]  │
    //  │                          [Export]  [Cancel]                │
    //  └───────────────────────────────────────────────────────────┘
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Result from StingExportDialog.Show().</summary>
    public class ExportDialogResult
    {
        /// <summary>Selected Revit categories to export.</summary>
        public List<string> SelectedCategories { get; set; } = new();
        /// <summary>Selected parameter names to export as columns.</summary>
        public List<string> SelectedParameters { get; set; } = new();
        /// <summary>Family name filter (null/empty = all families).</summary>
        public string FamilyFilter { get; set; }
        /// <summary>Type name filter (null/empty = all types).</summary>
        public string TypeFilter { get; set; }
        /// <summary>Export file format: "CSV", "Excel", or "JSON".</summary>
        public string Format { get; set; } = "CSV";
        /// <summary>Full path to the output file.</summary>
        public string OutputPath { get; set; }
        /// <summary>Scope: "ActiveView", "Selection", or "Project".</summary>
        public string Scope { get; set; } = "Project";
        /// <summary>Whether user cancelled.</summary>
        public bool Cancelled { get; set; } = true;
        /// <summary>Include element ID column.</summary>
        public bool IncludeElementId { get; set; } = true;
        /// <summary>Include category column.</summary>
        public bool IncludeCategory { get; set; } = true;
        /// <summary>Include family and type columns.</summary>
        public bool IncludeFamilyType { get; set; } = true;
    }

    /// <summary>
    /// BIMLink-style unified export dialog. Provides category, parameter,
    /// family/type filtering and format/location selection in a single window.
    /// </summary>
    public static class StingExportDialog
    {
        // ── Brand colours (match STING dark theme) ──
        private static readonly System.Windows.Media.Color BgDark = System.Windows.Media.Color.FromRgb(45, 45, 48);
        private static readonly System.Windows.Media.Color BgMedium = System.Windows.Media.Color.FromRgb(55, 55, 60);
        private static readonly System.Windows.Media.Color BgLight = System.Windows.Media.Color.FromRgb(62, 62, 66);
        private static readonly System.Windows.Media.Color AccentOrange = System.Windows.Media.Color.FromRgb(232, 145, 45);
        private static readonly System.Windows.Media.Color TextWhite = System.Windows.Media.Color.FromRgb(241, 241, 241);
        private static readonly System.Windows.Media.Color TextGrey = System.Windows.Media.Color.FromRgb(170, 170, 170);
        private static readonly System.Windows.Media.Color BorderDark = System.Windows.Media.Color.FromRgb(70, 70, 74);

        /// <summary>
        /// Show the export dialog. Returns null if cancelled.
        /// </summary>
        public static ExportDialogResult Show(Document doc, string title = "STING Data Export",
            ICollection<ElementId> preSelection = null)
        {
            var result = new ExportDialogResult();

            // ── Gather model data ──
            var categories = GetModelCategories(doc);
            var allParams = GetAvailableParameters(doc, categories);
            var families = GetFamilies(doc, categories);

            // ── Build window ──
            var win = new Window
            {
                Title = title,
                Width = 960, Height = 640,
                MinWidth = 800, MinHeight = 520,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(BgDark),
                FontFamily = new FontFamily("Segoe UI"),
                ResizeMode = ResizeMode.CanResizeWithGrip
            };

            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(win);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"ExportDialog owner: {ex.Message}"); }

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Scope bar
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Main
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Format/Location
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Footer
            win.Content = root;

            // ═══════════════════ HEADER ═══════════════════
            var header = new Border
            {
                Background = new SolidColorBrush(AccentOrange),
                Padding = new Thickness(16, 10, 16, 10)
            };
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
            headerPanel.Children.Add(new TextBlock
            {
                Text = "STING",
                FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, Margin = new Thickness(0, 0, 8, 0)
            });
            headerPanel.Children.Add(new TextBlock
            {
                Text = "Data Export",
                FontSize = 16, FontWeight = FontWeights.Light,
                Foreground = Brushes.White
            });
            header.Child = headerPanel;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ═══════════════════ SCOPE BAR ═══════════════════
            var scopeBar = new Border
            {
                Background = new SolidColorBrush(BgMedium),
                Padding = new Thickness(12, 6, 12, 6)
            };
            var scopePanel = new StackPanel { Orientation = Orientation.Horizontal };
            scopePanel.Children.Add(MakeLabel("Scope:", true));
            var rbProject = MakeRadio("Entire Project", "scope", true);
            var rbView = MakeRadio("Active View", "scope");
            var rbSelection = MakeRadio("Selection", "scope");
            if (preSelection != null && preSelection.Count > 0)
                rbSelection.IsChecked = true;
            scopePanel.Children.Add(rbProject);
            scopePanel.Children.Add(rbView);
            scopePanel.Children.Add(rbSelection);

            // Count label
            var countLabel = new TextBlock
            {
                Foreground = new SolidColorBrush(TextGrey),
                FontSize = 11, Margin = new Thickness(20, 4, 0, 0)
            };
            scopePanel.Children.Add(countLabel);
            scopeBar.Child = scopePanel;
            Grid.SetRow(scopeBar, 1);
            root.Children.Add(scopeBar);

            // ═══════════════════ MAIN 3-COLUMN AREA ═══════════════════
            var mainGrid = new Grid { Margin = new Thickness(8) };
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) }); // splitter
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) }); // splitter
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            Grid.SetRow(mainGrid, 2);
            root.Children.Add(mainGrid);

            // ── LEFT: Categories ──
            var catPanel = MakeGroupPanel("Categories");
            var catSearch = MakeSearchBox("Search categories...");
            (catPanel.Child as StackPanel).Children.Add(catSearch);

            var catSelectAll = new System.Windows.Controls.CheckBox
            {
                Content = "Select All",
                IsChecked = true,
                Foreground = new SolidColorBrush(AccentOrange),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(4, 4, 0, 4)
            };
            (catPanel.Child as StackPanel).Children.Add(catSelectAll);

            var catList = new ListBox
            {
                Background = new SolidColorBrush(BgLight),
                Foreground = new SolidColorBrush(TextWhite),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 4, 0, 0)
            };
            var catChecks = new Dictionary<string, System.Windows.Controls.CheckBox>();
            foreach (var cat in categories.OrderBy(c => c))
            {
                var cb = new System.Windows.Controls.CheckBox
                {
                    Content = cat,
                    IsChecked = true,
                    Foreground = new SolidColorBrush(TextWhite),
                    FontSize = 12,
                    Margin = new Thickness(2)
                };
                catChecks[cat] = cb;
                catList.Items.Add(cb);
            }
            (catPanel.Child as StackPanel).Children.Add(catList);
            Grid.SetColumn(catPanel, 0);
            mainGrid.Children.Add(catPanel);

            // Category search filter
            catSearch.TextChanged += (s, e) =>
            {
                string filter = catSearch.Text?.Trim().ToLowerInvariant() ?? "";
                foreach (var item in catList.Items.OfType<System.Windows.Controls.CheckBox>())
                    item.Visibility = filter.Length == 0 || (item.Content?.ToString().ToLowerInvariant().Contains(filter) == true)
                        ? Visibility.Visible : Visibility.Collapsed;
            };

            // Select all toggle
            catSelectAll.Checked += (s, e) => { foreach (var cb in catChecks.Values) cb.IsChecked = true; };
            catSelectAll.Unchecked += (s, e) => { foreach (var cb in catChecks.Values) cb.IsChecked = false; };

            // Splitter 1
            var splitter1 = new GridSplitter
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(BorderDark)
            };
            Grid.SetColumn(splitter1, 1);
            mainGrid.Children.Add(splitter1);

            // ── CENTER: Parameters ──
            var paramPanel = MakeGroupPanel("Parameters (Columns)");
            var paramSearch = MakeSearchBox("Search parameters...");
            (paramPanel.Child as StackPanel).Children.Add(paramSearch);

            // Quick-select buttons
            var quickPanel = new WrapPanel { Margin = new Thickness(2, 2, 2, 4) };
            var btnAllParams = MakeSmallButton("All");
            var btnNoneParams = MakeSmallButton("None");
            var btnTags = MakeSmallButton("Tags");
            var btnIdentity = MakeSmallButton("Identity");
            var btnSpatial = MakeSmallButton("Spatial");
            var btnMEP = MakeSmallButton("MEP");
            quickPanel.Children.Add(btnAllParams);
            quickPanel.Children.Add(btnNoneParams);
            quickPanel.Children.Add(btnTags);
            quickPanel.Children.Add(btnIdentity);
            quickPanel.Children.Add(btnSpatial);
            quickPanel.Children.Add(btnMEP);
            (paramPanel.Child as StackPanel).Children.Add(quickPanel);

            var paramList = new ListBox
            {
                Background = new SolidColorBrush(BgLight),
                Foreground = new SolidColorBrush(TextWhite),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 4, 0, 0)
            };
            var paramChecks = new Dictionary<string, System.Windows.Controls.CheckBox>();

            // Categorize parameters for grouping
            var tagParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC,
                ParamRegistry.PROD, ParamRegistry.SEQ, ParamRegistry.TAG1,
                ParamRegistry.STATUS, ParamRegistry.REV
            };
            var spatialParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ASS_GRID_REF_TXT", "ASS_ROOM_NAME_TXT", "ASS_ROOM_NUMBER_TXT",
                "ASS_DEPARTMENT_TXT", "ASS_LEVEL_NAME_TXT"
            };

            // Add parameters grouped: Tags first, then STING, then native
            var stingParams = allParams.Where(p => p.StartsWith("ASS_") || p.StartsWith("BLE_")
                || p.StartsWith("HVC_") || p.StartsWith("ELC_") || p.StartsWith("PLM_")
                || p.StartsWith("MNT_") || p.StartsWith("TAG_") || p.StartsWith("STING_")
                || p.StartsWith("STR_") || p.StartsWith("FLS_") || p.StartsWith("COM_")
                || p.StartsWith("MEP_") || p.StartsWith("RGL_")).OrderBy(p => p).ToList();
            var nativeParams = allParams.Except(stingParams).OrderBy(p => p).ToList();

            void AddParamGroup(string header, IEnumerable<string> @params, bool defaultChecked)
            {
                var hdr = new TextBlock
                {
                    Text = $"── {header} ──",
                    Foreground = new SolidColorBrush(AccentOrange),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11,
                    Margin = new Thickness(4, 6, 0, 2)
                };
                paramList.Items.Add(hdr);
                foreach (var p in @params)
                {
                    var cb = new System.Windows.Controls.CheckBox
                    {
                        Content = p,
                        IsChecked = defaultChecked,
                        Foreground = new SolidColorBrush(TextWhite),
                        FontSize = 12,
                        Margin = new Thickness(2),
                        Tag = p
                    };
                    paramChecks[p] = cb;
                    paramList.Items.Add(cb);
                }
            }

            AddParamGroup("STING Tag Tokens", stingParams.Where(p => tagParams.Contains(p)), true);
            AddParamGroup("STING Parameters", stingParams.Where(p => !tagParams.Contains(p)), false);
            AddParamGroup("Revit Parameters", nativeParams, false);

            (paramPanel.Child as StackPanel).Children.Add(paramList);
            Grid.SetColumn(paramPanel, 2);
            mainGrid.Children.Add(paramPanel);

            // Parameter search filter
            paramSearch.TextChanged += (s, e) =>
            {
                string filter = paramSearch.Text?.Trim().ToLowerInvariant() ?? "";
                foreach (var item in paramList.Items)
                {
                    if (item is System.Windows.Controls.CheckBox cb)
                        cb.Visibility = filter.Length == 0 || (cb.Content?.ToString().ToLowerInvariant().Contains(filter) == true)
                            ? Visibility.Visible : Visibility.Collapsed;
                    else if (item is TextBlock tb)
                        tb.Visibility = filter.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                }
            };

            // Quick-select handlers
            btnAllParams.Click += (s, e) => { foreach (var cb in paramChecks.Values) cb.IsChecked = true; };
            btnNoneParams.Click += (s, e) => { foreach (var cb in paramChecks.Values) cb.IsChecked = false; };
            btnTags.Click += (s, e) =>
            {
                foreach (var cb in paramChecks.Values) cb.IsChecked = false;
                foreach (var p in tagParams) if (paramChecks.ContainsKey(p)) paramChecks[p].IsChecked = true;
            };
            btnIdentity.Click += (s, e) =>
            {
                foreach (var cb in paramChecks.Values) cb.IsChecked = false;
                foreach (var p in tagParams) if (paramChecks.ContainsKey(p)) paramChecks[p].IsChecked = true;
                foreach (var p in allParams.Where(n => n.Contains("NAME") || n.Contains("TYPE") || n.Contains("FAMILY")))
                    if (paramChecks.ContainsKey(p)) paramChecks[p].IsChecked = true;
            };
            btnSpatial.Click += (s, e) =>
            {
                foreach (var cb in paramChecks.Values) cb.IsChecked = false;
                foreach (var p in spatialParams) if (paramChecks.ContainsKey(p)) paramChecks[p].IsChecked = true;
                foreach (var p in tagParams) if (paramChecks.ContainsKey(p)) paramChecks[p].IsChecked = true;
            };
            btnMEP.Click += (s, e) =>
            {
                foreach (var cb in paramChecks.Values) cb.IsChecked = false;
                foreach (var p in tagParams) if (paramChecks.ContainsKey(p)) paramChecks[p].IsChecked = true;
                foreach (var p in allParams.Where(n => n.StartsWith("MEP_") || n.StartsWith("HVC_")
                    || n.StartsWith("ELC_") || n.StartsWith("PLM_") || n.Contains("FLOW")
                    || n.Contains("VOLTAGE") || n.Contains("POWER") || n.Contains("PRESSURE")))
                    if (paramChecks.ContainsKey(p)) paramChecks[p].IsChecked = true;
            };

            // Splitter 2
            var splitter2 = new GridSplitter
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(BorderDark)
            };
            Grid.SetColumn(splitter2, 3);
            mainGrid.Children.Add(splitter2);

            // ── RIGHT: Filters (Family/Type) ──
            var filterPanel = MakeGroupPanel("Filters");
            var filterStack = filterPanel.Child as StackPanel;

            filterStack.Children.Add(MakeLabel("Family:", true));
            var cmbFamily = new System.Windows.Controls.ComboBox
            {
                IsEditable = true,
                Background = new SolidColorBrush(BgLight),
                Foreground = new SolidColorBrush(TextWhite),
                FontSize = 12,
                Margin = new Thickness(4, 2, 4, 8)
            };
            cmbFamily.Items.Add("(All Families)");
            foreach (var fam in families.Keys.OrderBy(f => f))
                cmbFamily.Items.Add(fam);
            cmbFamily.SelectedIndex = 0;
            filterStack.Children.Add(cmbFamily);

            filterStack.Children.Add(MakeLabel("Type:", true));
            var cmbType = new System.Windows.Controls.ComboBox
            {
                IsEditable = true,
                Background = new SolidColorBrush(BgLight),
                Foreground = new SolidColorBrush(TextWhite),
                FontSize = 12,
                Margin = new Thickness(4, 2, 4, 8)
            };
            cmbType.Items.Add("(All Types)");
            cmbType.SelectedIndex = 0;
            filterStack.Children.Add(cmbType);

            // Update types when family changes
            cmbFamily.SelectionChanged += (s, e) =>
            {
                cmbType.Items.Clear();
                cmbType.Items.Add("(All Types)");
                string selectedFamily = cmbFamily.SelectedItem?.ToString();
                if (!string.IsNullOrEmpty(selectedFamily) && selectedFamily != "(All Families)"
                    && families.TryGetValue(selectedFamily, out var types))
                {
                    foreach (var t in types.OrderBy(x => x))
                        cmbType.Items.Add(t);
                }
                cmbType.SelectedIndex = 0;
            };

            // Options section
            filterStack.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8), Background = new SolidColorBrush(BorderDark) });
            filterStack.Children.Add(MakeLabel("Options:", true));

            var chkElementId = new System.Windows.Controls.CheckBox
            {
                Content = "Include Element ID",
                IsChecked = true,
                Foreground = new SolidColorBrush(TextWhite),
                FontSize = 12,
                Margin = new Thickness(4, 4, 0, 2)
            };
            filterStack.Children.Add(chkElementId);

            var chkCategory = new System.Windows.Controls.CheckBox
            {
                Content = "Include Category",
                IsChecked = true,
                Foreground = new SolidColorBrush(TextWhite),
                FontSize = 12,
                Margin = new Thickness(4, 2, 0, 2)
            };
            filterStack.Children.Add(chkCategory);

            var chkFamilyType = new System.Windows.Controls.CheckBox
            {
                Content = "Include Family & Type",
                IsChecked = true,
                Foreground = new SolidColorBrush(TextWhite),
                FontSize = 12,
                Margin = new Thickness(4, 2, 0, 2)
            };
            filterStack.Children.Add(chkFamilyType);

            Grid.SetColumn(filterPanel, 4);
            mainGrid.Children.Add(filterPanel);

            // ═══════════════════ FORMAT / LOCATION BAR ═══════════════════
            var formatBar = new Border
            {
                Background = new SolidColorBrush(BgMedium),
                Padding = new Thickness(12, 8, 12, 8),
                BorderBrush = new SolidColorBrush(BorderDark),
                BorderThickness = new Thickness(0, 1, 0, 0)
            };
            var formatGrid = new Grid();
            formatGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            formatGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var formatPanel = new StackPanel { Orientation = Orientation.Horizontal };
            formatPanel.Children.Add(MakeLabel("Format:", true));
            var rbCSV = MakeRadio("CSV", "format", true);
            var rbExcel = MakeRadio("Excel (.xlsx)", "format");
            var rbJSON = MakeRadio("JSON", "format");
            formatPanel.Children.Add(rbCSV);
            formatPanel.Children.Add(rbExcel);
            formatPanel.Children.Add(rbJSON);
            Grid.SetColumn(formatPanel, 0);
            formatGrid.Children.Add(formatPanel);

            var locationPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            locationPanel.Children.Add(MakeLabel("Location:", true));
            var txtPath = new System.Windows.Controls.TextBox
            {
                Width = 300,
                Background = new SolidColorBrush(BgLight),
                Foreground = new SolidColorBrush(TextWhite),
                BorderBrush = new SolidColorBrush(BorderDark),
                FontSize = 12,
                Padding = new Thickness(4, 3, 4, 3),
                Margin = new Thickness(4, 0, 4, 0),
                Text = OutputLocationHelper.GetTimestampedPath(doc, "STING_Export", ".csv")
            };
            locationPanel.Children.Add(txtPath);
            var btnBrowse = MakeSmallButton("Browse...");
            btnBrowse.Click += (s, e) =>
            {
                string ext = rbExcel.IsChecked == true ? ".xlsx" : rbJSON.IsChecked == true ? ".json" : ".csv";
                string filter = rbExcel.IsChecked == true ? "Excel Files|*.xlsx" : rbJSON.IsChecked == true ? "JSON Files|*.json" : "CSV Files|*.csv";
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export Location",
                    FileName = Path.GetFileNameWithoutExtension(txtPath.Text) + ext,
                    Filter = filter + "|All Files|*.*",
                    InitialDirectory = Path.GetDirectoryName(txtPath.Text) ?? OutputLocationHelper.GetOutputDirectory(doc)
                };
                if (dlg.ShowDialog() == true) txtPath.Text = dlg.FileName;
            };
            locationPanel.Children.Add(btnBrowse);
            Grid.SetColumn(locationPanel, 1);
            formatGrid.Children.Add(locationPanel);

            // Update file extension when format changes
            void UpdateExtension()
            {
                try
                {
                    string ext = rbExcel.IsChecked == true ? ".xlsx" : rbJSON.IsChecked == true ? ".json" : ".csv";
                    string current = txtPath.Text;
                    if (!string.IsNullOrEmpty(current))
                        txtPath.Text = Path.ChangeExtension(current, ext);
                }
                catch { /* path parse failure is non-fatal */ }
            }
            rbCSV.Checked += (s, e) => UpdateExtension();
            rbExcel.Checked += (s, e) => UpdateExtension();
            rbJSON.Checked += (s, e) => UpdateExtension();

            formatBar.Child = formatGrid;
            Grid.SetRow(formatBar, 3);
            root.Children.Add(formatBar);

            // ═══════════════════ FOOTER ═══════════════════
            var footer = new Border
            {
                Background = new SolidColorBrush(BgMedium),
                Padding = new Thickness(12, 8, 12, 8)
            };
            var footerPanel = new DockPanel();

            // Left: status
            var statusText = new TextBlock
            {
                Foreground = new SolidColorBrush(TextGrey),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Text = $"{categories.Count} categories, {allParams.Count} parameters available"
            };
            DockPanel.SetDock(statusText, Dock.Left);
            footerPanel.Children.Add(statusText);

            // Right: buttons
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnCancel = new Button
            {
                Content = "Cancel",
                Width = 80, Height = 30,
                Margin = new Thickness(4, 0, 0, 0),
                FontSize = 12
            };
            btnCancel.Click += (s, e) => win.DialogResult = false;

            var btnExport = new Button
            {
                Content = "Export",
                Width = 100, Height = 30,
                Margin = new Thickness(4, 0, 0, 0),
                FontSize = 12, FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(AccentOrange),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(AccentOrange)
            };
            btnExport.Click += (s, e) =>
            {
                // Validate
                var selCats = catChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
                var selParams = paramChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();

                if (selCats.Count == 0)
                {
                    MessageBox.Show("Select at least one category.", "STING Export", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (selParams.Count == 0)
                {
                    MessageBox.Show("Select at least one parameter.", "STING Export", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (string.IsNullOrWhiteSpace(txtPath.Text))
                {
                    MessageBox.Show("Choose an export location.", "STING Export", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                result.SelectedCategories = selCats;
                result.SelectedParameters = selParams;
                result.Format = rbExcel.IsChecked == true ? "Excel" : rbJSON.IsChecked == true ? "JSON" : "CSV";
                result.OutputPath = txtPath.Text;
                result.Scope = rbView.IsChecked == true ? "ActiveView" : rbSelection.IsChecked == true ? "Selection" : "Project";
                result.IncludeElementId = chkElementId.IsChecked == true;
                result.IncludeCategory = chkCategory.IsChecked == true;
                result.IncludeFamilyType = chkFamilyType.IsChecked == true;
                result.Cancelled = false;

                string famSel = cmbFamily.SelectedItem?.ToString() ?? cmbFamily.Text;
                if (famSel != "(All Families)" && !string.IsNullOrWhiteSpace(famSel))
                    result.FamilyFilter = famSel;

                string typeSel = cmbType.SelectedItem?.ToString() ?? cmbType.Text;
                if (typeSel != "(All Types)" && !string.IsNullOrWhiteSpace(typeSel))
                    result.TypeFilter = typeSel;

                win.DialogResult = true;
            };

            DockPanel.SetDock(buttonPanel, Dock.Right);
            buttonPanel.Children.Add(btnCancel);
            buttonPanel.Children.Add(btnExport);
            footerPanel.Children.Add(buttonPanel);

            footer.Child = footerPanel;
            Grid.SetRow(footer, 4);
            root.Children.Add(footer);

            // ── Show ──
            bool? dialogResult = win.ShowDialog();
            if (dialogResult != true || result.Cancelled) return null;
            return result;
        }

        // ═══════════════════ DATA HELPERS ═══════════════════

        private static HashSet<string> GetModelCategories(Document doc)
        {
            var cats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var known = TagConfig.DiscMap.Keys;
                foreach (var k in known) cats.Add(k);

                // Also collect categories actually present in the model
                var elements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElements();
                foreach (var el in elements)
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (!string.IsNullOrEmpty(cat)) cats.Add(cat);
                }
            }
            catch (Exception ex) { StingLog.Warn($"ExportDialog GetCategories: {ex.Message}"); }
            return cats;
        }

        private static List<string> GetAvailableParameters(Document doc, IEnumerable<string> categories)
        {
            var paramNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // Add all known STING parameters
                foreach (var kv in ParamRegistry.AllParamGuids)
                    paramNames.Add(kv.Key);

                // Sample first 50 elements for their native parameters
                var sample = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Take(50);
                foreach (var el in sample)
                {
                    foreach (Parameter p in el.Parameters)
                    {
                        if (p.Definition != null && !string.IsNullOrEmpty(p.Definition.Name))
                            paramNames.Add(p.Definition.Name);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ExportDialog GetParameters: {ex.Message}"); }
            return paramNames.ToList();
        }

        private static Dictionary<string, List<string>> GetFamilies(Document doc, IEnumerable<string> categories)
        {
            var families = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var types = new FilteredElementCollector(doc)
                    .WhereElementIsElementType()
                    .ToElements()
                    .OfType<FamilySymbol>()
                    .Take(500); // Limit for performance
                foreach (var fs in types)
                {
                    string famName = fs.FamilyName ?? "(Unknown)";
                    string typeName = fs.Name ?? "(Unnamed)";
                    if (!families.TryGetValue(famName, out var list))
                    {
                        list = new List<string>();
                        families[famName] = list;
                    }
                    if (!list.Contains(typeName))
                        list.Add(typeName);
                }
            }
            catch (Exception ex) { StingLog.Warn($"ExportDialog GetFamilies: {ex.Message}"); }
            return families;
        }

        // ═══════════════════ UI HELPERS ═══════════════════

        private static Border MakeGroupPanel(string title)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(BgMedium),
                BorderBrush = new SolidColorBrush(BorderDark),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(2),
                Padding = new Thickness(6)
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentOrange),
                Margin = new Thickness(0, 0, 0, 4)
            });
            border.Child = stack;
            return border;
        }

        private static System.Windows.Controls.TextBox MakeSearchBox(string placeholder)
        {
            var tb = new System.Windows.Controls.TextBox
            {
                Background = new SolidColorBrush(BgLight),
                Foreground = new SolidColorBrush(TextWhite),
                BorderBrush = new SolidColorBrush(BorderDark),
                FontSize = 12,
                Padding = new Thickness(4, 3, 4, 3),
                Margin = new Thickness(0, 0, 0, 4),
                Tag = placeholder
            };
            // Placeholder via GotFocus/LostFocus
            tb.Text = placeholder;
            tb.Foreground = new SolidColorBrush(TextGrey);
            tb.GotFocus += (s, e) =>
            {
                if (tb.Text == (string)tb.Tag)
                {
                    tb.Text = "";
                    tb.Foreground = new SolidColorBrush(TextWhite);
                }
            };
            tb.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(tb.Text))
                {
                    tb.Text = (string)tb.Tag;
                    tb.Foreground = new SolidColorBrush(TextGrey);
                }
            };
            return tb;
        }

        private static TextBlock MakeLabel(string text, bool bold = false)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(TextWhite),
                FontSize = 12,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Margin = new Thickness(4, 3, 4, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static RadioButton MakeRadio(string text, string group, bool isChecked = false)
        {
            return new RadioButton
            {
                Content = text,
                GroupName = group,
                IsChecked = isChecked,
                Foreground = new SolidColorBrush(TextWhite),
                FontSize = 12,
                Margin = new Thickness(8, 3, 4, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static Button MakeSmallButton(string text)
        {
            return new Button
            {
                Content = text,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(2),
                FontSize = 11,
                Background = new SolidColorBrush(BgLight),
                Foreground = new SolidColorBrush(TextWhite),
                BorderBrush = new SolidColorBrush(BorderDark)
            };
        }
    }

    /// <summary>
    /// Engine that executes the data export based on dialog settings.
    /// Supports CSV, Excel (via ClosedXML), and JSON output formats.
    /// </summary>
    internal static class DataExportEngine
    {
        public static void Execute(Document doc, Autodesk.Revit.UI.UIDocument uidoc, ExportDialogResult settings)
        {
            if (settings == null || settings.Cancelled) return;

            var catSet = new HashSet<string>(settings.SelectedCategories, StringComparer.OrdinalIgnoreCase);
            var paramNames = settings.SelectedParameters;

            // ── Collect elements ──
            IEnumerable<Element> elements;
            if (settings.Scope == "Selection" && uidoc != null)
            {
                elements = uidoc.Selection.GetElementIds()
                    .Select(id => doc.GetElement(id))
                    .Where(el => el != null);
            }
            else if (settings.Scope == "ActiveView" && doc.ActiveView != null)
            {
                elements = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .ToElements();
            }
            else
            {
                elements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElements();
            }

            // ── Filter by category ──
            var filtered = elements.Where(el =>
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                return !string.IsNullOrEmpty(cat) && catSet.Contains(cat);
            });

            // ── Filter by family/type ──
            if (!string.IsNullOrEmpty(settings.FamilyFilter))
            {
                string famFilter = settings.FamilyFilter;
                filtered = filtered.Where(el =>
                {
                    string fam = ParameterHelpers.GetFamilyName(el);
                    return string.Equals(fam, famFilter, StringComparison.OrdinalIgnoreCase);
                });
            }
            if (!string.IsNullOrEmpty(settings.TypeFilter))
            {
                string typeFilter = settings.TypeFilter;
                filtered = filtered.Where(el =>
                {
                    string typeName = ParameterHelpers.GetFamilySymbolName(el);
                    return string.Equals(typeName, typeFilter, StringComparison.OrdinalIgnoreCase);
                });
            }

            var elemList = filtered.ToList();
            StingLog.Info($"DataExport: {elemList.Count} elements, {paramNames.Count} parameters, format={settings.Format}");

            // ── Build header ──
            var headers = new List<string>();
            if (settings.IncludeElementId) headers.Add("ElementId");
            if (settings.IncludeCategory) headers.Add("Category");
            if (settings.IncludeFamilyType) { headers.Add("Family"); headers.Add("Type"); }
            headers.AddRange(paramNames);

            // ── Build rows ──
            var rows = new List<string[]>();
            foreach (var el in elemList)
            {
                var row = new List<string>();
                if (settings.IncludeElementId) row.Add(el.Id.ToString());
                if (settings.IncludeCategory) row.Add(ParameterHelpers.GetCategoryName(el));
                if (settings.IncludeFamilyType)
                {
                    row.Add(ParameterHelpers.GetFamilyName(el));
                    row.Add(ParameterHelpers.GetFamilySymbolName(el));
                }
                foreach (var pName in paramNames)
                {
                    string val = ReadParamValue(el, pName);
                    row.Add(val);
                }
                rows.Add(row.ToArray());
            }

            // ── Write output ──
            string dir = Path.GetDirectoryName(settings.OutputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            switch (settings.Format.ToUpperInvariant())
            {
                case "CSV":
                    WriteCsv(settings.OutputPath, headers, rows);
                    break;
                case "EXCEL":
                    WriteExcel(settings.OutputPath, headers, rows);
                    break;
                case "JSON":
                    WriteJson(settings.OutputPath, headers, rows);
                    break;
                default:
                    WriteCsv(settings.OutputPath, headers, rows);
                    break;
            }
        }

        private static string ReadParamValue(Element el, string paramName)
        {
            try
            {
                // Try STING shared parameter first
                string val = ParameterHelpers.GetString(el, paramName);
                if (!string.IsNullOrEmpty(val)) return val;

                // Try native parameter by name
                Parameter p = el.LookupParameter(paramName);
                if (p == null) return "";
                switch (p.StorageType)
                {
                    case StorageType.String: return p.AsString() ?? "";
                    case StorageType.Integer: return p.AsInteger().ToString();
                    case StorageType.Double: return p.AsDouble().ToString("F4");
                    case StorageType.ElementId:
                        var refEl = el.Document.GetElement(p.AsElementId());
                        return refEl?.Name ?? p.AsElementId().ToString();
                    default: return "";
                }
            }
            catch { return ""; }
        }

        private static void WriteCsv(string path, List<string> headers, List<string[]> rows)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
            foreach (var row in rows)
                sb.AppendLine(string.Join(",", row.Select(EscapeCsv)));
            File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
            StingLog.Info($"DataExport: CSV written to {path} ({rows.Count} rows)");
        }

        private static string EscapeCsv(string val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            if (val.Contains(",") || val.Contains("\"") || val.Contains("\n"))
                return $"\"{val.Replace("\"", "\"\"")}\"";
            return val;
        }

        private static void WriteExcel(string path, List<string> headers, List<string[]> rows)
        {
            try
            {
                using var wb = new ClosedXML.Excel.XLWorkbook();
                var ws = wb.Worksheets.Add("STING Export");
                for (int c = 0; c < headers.Count; c++)
                {
                    ws.Cell(1, c + 1).Value = headers[c];
                    ws.Cell(1, c + 1).Style.Font.Bold = true;
                    ws.Cell(1, c + 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromArgb(232, 145, 45);
                    ws.Cell(1, c + 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                }
                for (int r = 0; r < rows.Count; r++)
                {
                    for (int c = 0; c < rows[r].Length && c < headers.Count; c++)
                        ws.Cell(r + 2, c + 1).Value = rows[r][c];
                }
                ws.Columns().AdjustToContents(1, 50);
                ws.SheetView.FreezeRows(1);
                wb.SaveAs(path);
                StingLog.Info($"DataExport: Excel written to {path} ({rows.Count} rows)");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Excel export failed, falling back to CSV: {ex.Message}");
                WriteCsv(Path.ChangeExtension(path, ".csv"), headers, rows);
            }
        }

        private static void WriteJson(string path, List<string> headers, List<string[]> rows)
        {
            var jsonRows = new List<Dictionary<string, string>>();
            foreach (var row in rows)
            {
                var dict = new Dictionary<string, string>();
                for (int i = 0; i < headers.Count && i < row.Length; i++)
                    dict[headers[i]] = row[i];
                jsonRows.Add(dict);
            }
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(jsonRows, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(path, json, System.Text.Encoding.UTF8);
            StingLog.Info($"DataExport: JSON written to {path} ({rows.Count} rows)");
        }
    }
}
