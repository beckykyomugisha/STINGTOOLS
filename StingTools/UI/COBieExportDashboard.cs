using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Result class for COBie Export Dashboard dialog.
    /// </summary>
    internal sealed class COBieExportDashboardResult
    {
        public bool Confirmed { get; set; }
        public string Operation { get; set; } = "";
        public Dictionary<string, string> Options { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Unified WPF dashboard for COBie V2.4 export configuration.
    /// Consolidates preset selection, worksheet configuration, column mapping, and output settings
    /// into a single tabbed dialog with operation cards.
    /// </summary>
    internal static class COBieExportDashboard
    {
        // ── Theme-routed palette ─────────────────────────────────────────
        // All colours come from ThemeManager so the dashboard tracks the
        // active theme (Corporate by default — navy header, orange accent).
        private static SolidColorBrush FZ(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        private static SolidColorBrush BgBrush     => ThemeManager.GetBrush("AltRowBg");
        private static SolidColorBrush HeaderBg    => ThemeManager.GetBrush("HeaderBg");
        private static SolidColorBrush AccentBrush => ThemeManager.GetBrush("AccentBrush");
        private static SolidColorBrush PanelBg     => ThemeManager.GetBrush("CardBg");
        private static SolidColorBrush FgDark      => ThemeManager.GetBrush("PanelFg");
        private static SolidColorBrush FgWhite     => ThemeManager.GetBrush("HeaderFg");
        private static SolidColorBrush FgMuted     => ThemeManager.GetBrush("SubtleFg");
        private static SolidColorBrush BorderLight => ThemeManager.GetBrush("BorderColor");
        private static SolidColorBrush GreenAccent => ThemeManager.GetBrush("SuccessColor");
        private static SolidColorBrush BlueAccent  => ThemeManager.GetBrush("InfoBlue");
        private static SolidColorBrush RedAccent   => ThemeManager.GetBrush("ErrorColor");

        // ── Preset list ─────────────────────────────────────────────────
        private static readonly string[] PresetKeys = new[]
        {
            "FULL", "COMMERCIAL_OFFICE", "HEALTHCARE_NHS", "HEALTHCARE_PRIVATE",
            "EDUCATION_SCHOOL", "EDUCATION_UNI", "RESIDENTIAL", "RESIDENTIAL_HIGHRISE",
            "RETAIL", "HOTEL", "DATA_CENTRE", "INDUSTRIAL",
            "TRANSPORT_STATION", "TRANSPORT_AIRPORT", "DEFENCE", "HERITAGE",
            "MIXED_USE", "LABORATORY", "SPORTS", "CULTURAL",
            "MODULAR", "INFRASTRUCTURE_CIVIL", "INFRASTRUCTURE_WATER", "FITOUT"
        };

        // ── Worksheet definitions ───────────────────────────────────────
        private static readonly string[] RequiredWorksheets = { "Instruction", "Contact", "Facility" };
        private static readonly string[] OptionalWorksheets =
        {
            "Floor", "Space", "Zone", "Type", "Component", "System",
            "Assembly", "Connection", "Spare", "Resource", "Job",
            "Impact", "Document", "Attribute", "Coordinate", "Issue", "PickLists"
        };

        // ── Column definitions per worksheet ────────────────────────────
        private static readonly string[] RequiredColumns = { "Name", "CreatedBy", "CreatedOn" };

        private static readonly Dictionary<string, string[]> WorksheetColumns = new Dictionary<string, string[]>
        {
            ["Component"] = new[]
            {
                "Name", "CreatedBy", "CreatedOn", "TypeName", "Space", "Description",
                "ExternalSystem", "ExternalObject", "ExternalIdentifier",
                "SerialNumber", "InstallationDate", "WarrantyStartDate",
                "TagNumber", "BarCode", "AssetIdentifier",
                "Category", "Discipline", "Location", "Zone", "Level",
                "System", "Function", "ProductCode", "SequenceNumber", "Status"
            },
            ["Type"] = new[]
            {
                "Name", "CreatedBy", "CreatedOn", "Category", "Description", "AssetType",
                "Manufacturer", "ModelNumber",
                "WarrantyGuarantorParts", "WarrantyDurationParts", "WarrantyGuarantorLabor",
                "WarrantyDurationLabor", "ReplacementCost", "ExpectedLife",
                "NominalLength", "NominalWidth", "NominalHeight",
                "Shape", "Size", "Color", "Finish", "Grade", "Material",
                "Constituents", "Features",
                "AccessibilityPerformance", "CodePerformance", "SustainabilityPerformance"
            },
            ["Attribute"] = new[]
            {
                "Name", "CreatedBy", "CreatedOn", "Category", "SheetName", "RowName",
                "Value", "Unit", "ExtSystem", "ExtObject", "ExtIdentifier", "Description",
                "AllowedValues"
            },
            ["Impact"] = new[]
            {
                "Name", "CreatedBy", "CreatedOn", "ImpactType", "ImpactStage",
                "SheetName", "RowName", "Value", "Unit", "LeadInTime", "Duration",
                "LeadOutTime", "ImpactUnit", "Description"
            }
        };

        // ── State ───────────────────────────────────────────────────────
        private static COBieExportDashboardResult _result;
        private static Window _window;

        // Tab 1 controls
        private static ComboBox _presetCombo;
        private static CheckBox _cbFixed, _cbMoveable, _cbOperated, _cbMonitored, _cbSafety;
        private static ComboBox _statusCombo;

        // Tab 2 controls
        private static readonly Dictionary<string, CheckBox> _worksheetChecks = new Dictionary<string, CheckBox>();
        private static CheckBox _cbResourceEnhanced, _cbImpactEnhanced;

        // Tab 3 controls
        private static ComboBox _columnSheetCombo;
        private static StackPanel _columnGrid;
        private static readonly Dictionary<string, Dictionary<string, CheckBox>> _columnChecks =
            new Dictionary<string, Dictionary<string, CheckBox>>();

        // Tab 4 controls
        private static CheckBox _cbExportXlsx, _cbExportCsv, _cbStreaming;
        private static TextBox _outputPath;

        // ── Public API ──────────────────────────────────────────────────

        /// <summary>
        /// Shows the COBie Export Dashboard dialog.
        /// Returns a COBieExportDashboardResult or null if cancelled.
        /// </summary>
        public static COBieExportDashboardResult Show()
        {
            _result = null;
            _worksheetChecks.Clear();
            _columnChecks.Clear();

            _window = new Window
            {
                Title = "COBie V2.4 Export Dashboard",
                Width = 780,
                Height = 680,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResize,
                Background = BgBrush,
                MinWidth = 640,
                MinHeight = 520
            };

            try
            {
                var handle = Process.GetCurrentProcess().MainWindowHandle;
                if (handle != IntPtr.Zero)
                    new WindowInteropHelper(_window).Owner = handle;
            }
            catch (Exception ex) { StingLog.Warn($"COBie dashboard window owner: {ex.Message}"); }

            var root = new DockPanel { LastChildFill = true };
            _window.Content = root;

            // ── Header ──────────────────────────────────────────────────
            root.Children.Add(BuildHeader());
            DockPanel.SetDock((UIElement)root.Children[root.Children.Count - 1], Dock.Top);

            // ── Footer ──────────────────────────────────────────────────
            var footer = BuildFooter();
            root.Children.Add(footer);
            DockPanel.SetDock(footer, Dock.Bottom);

            // ── TabControl ──────────────────────────────────────────────
            var tabs = new TabControl
            {
                Margin = new Thickness(12, 8, 12, 0),
                Background = PanelBg,
                BorderBrush = BorderLight,
                BorderThickness = new Thickness(1)
            };

            tabs.Items.Add(BuildPresetTab());
            tabs.Items.Add(BuildWorksheetsTab());
            tabs.Items.Add(BuildColumnsTab());
            tabs.Items.Add(BuildOutputTab());

            root.Children.Add(tabs);

            _window.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) _window.DialogResult = false;
            };

            var dialogResult = _window.ShowDialog();
            if (dialogResult != true) return null;
            return _result;
        }

        // ── Header ──────────────────────────────────────────────────────

        private static Border BuildHeader()
        {
            var headerPanel = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };

            headerPanel.Children.Add(new TextBlock
            {
                Text = "COBie V2.4 Export Dashboard",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = FgWhite
            });

            headerPanel.Children.Add(new TextBlock
            {
                Text = "Configure project type, worksheets, columns, and output settings for ISO 19650 COBie export",
                FontSize = 11,
                Foreground = FZ(Color.FromRgb(0xBB, 0xBB, 0xDD)),
                Margin = new Thickness(0, 4, 0, 0)
            });

            return new Border
            {
                Background = HeaderBg,
                Child = headerPanel
            };
        }

        // ── Footer ──────────────────────────────────────────────────────

        private static Border BuildFooter()
        {
            var footerGrid = new Grid { Margin = new Thickness(12, 8, 12, 12) };
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var hint = new TextBlock
            {
                Text = "Select an operation card to execute, or click Close to cancel.",
                Foreground = FgMuted,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(hint, 0);
            footerGrid.Children.Add(hint);

            var closeBtn = new Button
            {
                Content = "Close",
                Width = 90,
                Height = 32,
                FontSize = 12,
                Background = FZ(Color.FromRgb(0x99, 0x99, 0x99)),
                Foreground = FgWhite,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            closeBtn.Click += (s, e) => { _window.DialogResult = false; };
            Grid.SetColumn(closeBtn, 1);
            footerGrid.Children.Add(closeBtn);

            return new Border
            {
                BorderBrush = BorderLight,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = footerGrid
            };
        }

        // ── Tab 1: PRESET & FILTERS ─────────────────────────────────────

        private static TabItem BuildPresetTab()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(16, 12, 16, 12)
            };

            var stack = new StackPanel();
            scroll.Content = stack;

            // Section: Project Type Preset
            stack.Children.Add(MakeSectionHeader("Project Type Preset"));

            _presetCombo = new ComboBox
            {
                Margin = new Thickness(0, 6, 0, 12),
                Height = 30,
                FontSize = 12
            };
            foreach (var key in PresetKeys)
            {
                _presetCombo.Items.Add(FormatPresetName(key));
            }
            _presetCombo.SelectedIndex = 0;
            stack.Children.Add(_presetCombo);

            // Section: Asset Type Filter
            stack.Children.Add(MakeSectionHeader("Asset Type Filter"));

            var assetPanel = new WrapPanel { Margin = new Thickness(0, 6, 0, 12) };
            _cbFixed = MakeCheckBox("Fixed", true);
            _cbMoveable = MakeCheckBox("Moveable", true);
            _cbOperated = MakeCheckBox("Operated", false);
            _cbMonitored = MakeCheckBox("Monitored", false);
            _cbSafety = MakeCheckBox("Safety", false);
            assetPanel.Children.Add(_cbFixed);
            assetPanel.Children.Add(_cbMoveable);
            assetPanel.Children.Add(_cbOperated);
            assetPanel.Children.Add(_cbMonitored);
            assetPanel.Children.Add(_cbSafety);
            stack.Children.Add(assetPanel);

            // Section: Status Filter
            stack.Children.Add(MakeSectionHeader("Status Filter"));

            _statusCombo = new ComboBox
            {
                Margin = new Thickness(0, 6, 0, 16),
                Height = 30,
                FontSize = 12
            };
            _statusCombo.Items.Add("All");
            _statusCombo.Items.Add("NEW");
            _statusCombo.Items.Add("EXISTING");
            _statusCombo.Items.Add("DEMOLISHED");
            _statusCombo.Items.Add("TEMPORARY");
            _statusCombo.SelectedIndex = 0;
            stack.Children.Add(_statusCombo);

            // Operation cards
            stack.Children.Add(MakeSectionHeader("Export Operations"));

            stack.Children.Add(MakeOperationCard(
                "COBie Export",
                "Run full COBie V2.4 export with selected preset, worksheets, and columns",
                AccentBrush,
                "COBieExport",
                collectAllOptions: true));

            stack.Children.Add(MakeOperationCard(
                "Streaming COBie Export",
                "Progressive export for large models (50K+ elements) with batched processing",
                BlueAccent,
                "StreamingCOBieExport",
                collectAllOptions: true));

            stack.Children.Add(MakeOperationCard(
                "COBie Extended Import",
                "Import Type, System, and Job sheets from an existing COBie spreadsheet",
                GreenAccent,
                "COBieExtendedImport",
                collectAllOptions: false));

            return new TabItem
            {
                Header = "PRESET & FILTERS",
                Content = scroll
            };
        }

        // ── Tab 2: WORKSHEETS ───────────────────────────────────────────

        private static TabItem BuildWorksheetsTab()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(16, 12, 16, 12)
            };

            var stack = new StackPanel();
            scroll.Content = stack;

            // Section: COBie V2.4 Worksheets
            stack.Children.Add(MakeSectionHeader("COBie V2.4 Worksheets"));

            var wsGrid = new Grid { Margin = new Thickness(0, 6, 0, 4) };
            wsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            wsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int row = 0;

            // Required worksheets (always on, disabled)
            foreach (var ws in RequiredWorksheets)
            {
                var cb = MakeCheckBox(ws, true);
                cb.IsEnabled = false;
                cb.ToolTip = "Required worksheet - cannot be deselected";
                _worksheetChecks[ws] = cb;

                wsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(cb, row);
                Grid.SetColumn(cb, row < (RequiredWorksheets.Length + 1) / 2 ? 0 : 1);
                wsGrid.Children.Add(cb);
                row++;
            }

            // Optional worksheets in 2 columns
            int optIdx = 0;
            foreach (var ws in OptionalWorksheets)
            {
                var cb = MakeCheckBox(ws, true);
                _worksheetChecks[ws] = cb;

                int gridRow = RequiredWorksheets.Length + optIdx / 2;
                int gridCol = optIdx % 2;

                while (wsGrid.RowDefinitions.Count <= gridRow)
                    wsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                Grid.SetRow(cb, gridRow);
                Grid.SetColumn(cb, gridCol);
                wsGrid.Children.Add(cb);
                optIdx++;
            }

            stack.Children.Add(wsGrid);

            // Select All / Deselect Optional buttons
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 16)
            };

            var selectAllBtn = new Button
            {
                Content = "Select All",
                Width = 110,
                Height = 28,
                FontSize = 11,
                Margin = new Thickness(0, 0, 8, 0),
                Background = BlueAccent,
                Foreground = FgWhite,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            selectAllBtn.Click += (s, e) =>
            {
                foreach (var kvp in _worksheetChecks)
                    if (kvp.Value.IsEnabled) kvp.Value.IsChecked = true;
            };
            btnPanel.Children.Add(selectAllBtn);

            var deselectBtn = new Button
            {
                Content = "Deselect Optional",
                Width = 130,
                Height = 28,
                FontSize = 11,
                Background = FZ(Color.FromRgb(0x99, 0x99, 0x99)),
                Foreground = FgWhite,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            deselectBtn.Click += (s, e) =>
            {
                foreach (var kvp in _worksheetChecks)
                    if (kvp.Value.IsEnabled) kvp.Value.IsChecked = false;
            };
            btnPanel.Children.Add(deselectBtn);

            stack.Children.Add(btnPanel);

            // Section: Enhanced Worksheets
            stack.Children.Add(MakeSectionHeader("Enhanced Worksheets"));

            _cbResourceEnhanced = MakeCheckBox("Enhanced Resources (skill requirements, hourly rates, certifications)", false);
            _cbResourceEnhanced.Margin = new Thickness(0, 6, 0, 4);
            stack.Children.Add(_cbResourceEnhanced);

            _cbImpactEnhanced = MakeCheckBox("Enhanced Impact (lifecycle cost, carbon per m\u00B2, energy benchmarks)", false);
            _cbImpactEnhanced.Margin = new Thickness(0, 0, 0, 12);
            stack.Children.Add(_cbImpactEnhanced);

            return new TabItem
            {
                Header = "WORKSHEETS",
                Content = scroll
            };
        }

        // ── Tab 3: COLUMNS ──────────────────────────────────────────────

        private static TabItem BuildColumnsTab()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(16, 12, 16, 12)
            };

            var stack = new StackPanel();
            scroll.Content = stack;

            stack.Children.Add(MakeSectionHeader("Column Configuration Per Worksheet"));

            // Worksheet selector
            var selectorPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 6, 0, 12)
            };

            selectorPanel.Children.Add(new TextBlock
            {
                Text = "Worksheet:",
                FontSize = 12,
                Foreground = FgDark,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            _columnSheetCombo = new ComboBox
            {
                Width = 200,
                Height = 28,
                FontSize = 12
            };
            foreach (var key in WorksheetColumns.Keys)
                _columnSheetCombo.Items.Add(key);
            _columnSheetCombo.SelectionChanged += OnColumnSheetChanged;
            selectorPanel.Children.Add(_columnSheetCombo);

            stack.Children.Add(selectorPanel);

            // Column checkbox area
            _columnGrid = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            stack.Children.Add(_columnGrid);

            // Pre-build column checkboxes for each worksheet
            foreach (var kvp in WorksheetColumns)
            {
                var checks = new Dictionary<string, CheckBox>();
                foreach (var col in kvp.Value)
                {
                    var cb = MakeCheckBox(col, true);
                    if (RequiredColumns.Contains(col))
                    {
                        cb.IsEnabled = false;
                        cb.ToolTip = "Required column - cannot be deselected";
                    }
                    checks[col] = cb;
                }
                _columnChecks[kvp.Key] = checks;
            }

            // Select first worksheet
            if (_columnSheetCombo.Items.Count > 0)
                _columnSheetCombo.SelectedIndex = 0;

            return new TabItem
            {
                Header = "COLUMNS",
                Content = scroll
            };
        }

        private static void OnColumnSheetChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_columnGrid == null || _columnSheetCombo == null) return;

            // Remove checkboxes from their current parent Grid before clearing
            // (WPF throws if element already has a logical parent)
            foreach (var child in _columnGrid.Children)
            {
                if (child is Grid oldGrid)
                    oldGrid.Children.Clear();
            }
            _columnGrid.Children.Clear();

            var selected = _columnSheetCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(selected) || !_columnChecks.ContainsKey(selected)) return;

            var checks = _columnChecks[selected];

            var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int idx = 0;
            foreach (var kvp in checks)
            {
                int gridRow = idx / 2;
                int gridCol = idx % 2;

                while (grid.RowDefinitions.Count <= gridRow)
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                Grid.SetRow(kvp.Value, gridRow);
                Grid.SetColumn(kvp.Value, gridCol);
                grid.Children.Add(kvp.Value);
                idx++;
            }

            _columnGrid.Children.Add(grid);

            // Select All / Deselect buttons for columns
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0)
            };

            var selAll = new Button
            {
                Content = "Select All Columns",
                Width = 130,
                Height = 26,
                FontSize = 11,
                Margin = new Thickness(0, 0, 8, 0),
                Background = BlueAccent,
                Foreground = FgWhite,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            selAll.Click += (s, ev) =>
            {
                foreach (var c in checks.Values)
                    if (c.IsEnabled) c.IsChecked = true;
            };
            btnRow.Children.Add(selAll);

            var desel = new Button
            {
                Content = "Deselect Optional",
                Width = 130,
                Height = 26,
                FontSize = 11,
                Background = FZ(Color.FromRgb(0x99, 0x99, 0x99)),
                Foreground = FgWhite,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            desel.Click += (s, ev) =>
            {
                foreach (var c in checks.Values)
                    if (c.IsEnabled) c.IsChecked = false;
            };
            btnRow.Children.Add(desel);

            _columnGrid.Children.Add(btnRow);
        }

        // ── Tab 4: OUTPUT ───────────────────────────────────────────────

        private static TabItem BuildOutputTab()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(16, 12, 16, 12)
            };

            var stack = new StackPanel();
            scroll.Content = stack;

            // Section: Export Format
            stack.Children.Add(MakeSectionHeader("Export Format"));

            _cbExportXlsx = MakeCheckBox("Excel (.xlsx)", true);
            _cbExportXlsx.Margin = new Thickness(0, 6, 0, 4);
            stack.Children.Add(_cbExportXlsx);

            _cbExportCsv = MakeCheckBox("CSV files (one per worksheet)", false);
            _cbExportCsv.Margin = new Thickness(0, 0, 0, 12);
            stack.Children.Add(_cbExportCsv);

            // Section: Performance
            stack.Children.Add(MakeSectionHeader("Performance"));

            _cbStreaming = MakeCheckBox("Enable streaming export (recommended for 50K+ elements)", false);
            _cbStreaming.Margin = new Thickness(0, 6, 0, 12);
            stack.Children.Add(_cbStreaming);

            // Section: Output Location
            stack.Children.Add(MakeSectionHeader("Output Location"));

            var pathPanel = new DockPanel { Margin = new Thickness(0, 6, 0, 16) };

            var browseBtn = new Button
            {
                Content = "Browse...",
                Width = 80,
                Height = 28,
                FontSize = 11,
                Background = BlueAccent,
                Foreground = FgWhite,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            DockPanel.SetDock(browseBtn, Dock.Right);
            pathPanel.Children.Add(browseBtn);

            _outputPath = new TextBox
            {
                Height = 28,
                FontSize = 12,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            pathPanel.Children.Add(_outputPath);

            browseBtn.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "COBie Export Location",
                    Filter = "Excel Workbook (*.xlsx)|*.xlsx|All Files (*.*)|*.*",
                    FileName = "COBie_Export.xlsx",
                    InitialDirectory = _outputPath.Text
                };
                if (dlg.ShowDialog() == true)
                    _outputPath.Text = dlg.FileName;
            };

            stack.Children.Add(pathPanel);

            // Operation cards
            stack.Children.Add(MakeSectionHeader("Additional Operations"));

            stack.Children.Add(MakeOperationCard(
                "COBie Data Summary",
                "Browse COBie type map, picklists, job templates, and spare parts reference data",
                BlueAccent,
                "COBieDataSummary",
                collectAllOptions: false));

            stack.Children.Add(MakeOperationCard(
                "COBie Handover Export",
                "Generate full FM handover package with COBie, maintenance schedules, and O&M manual",
                GreenAccent,
                "COBieHandoverExport",
                collectAllOptions: false));

            return new TabItem
            {
                Header = "OUTPUT",
                Content = scroll
            };
        }

        // ── Helper: Collect all options from all tabs ────────────────────

        private static Dictionary<string, string> CollectAllOptions()
        {
            var opts = new Dictionary<string, string>();

            // Tab 1: Preset & Filters
            opts["PresetKey"] = _presetCombo.SelectedIndex >= 0 && _presetCombo.SelectedIndex < PresetKeys.Length
                ? PresetKeys[_presetCombo.SelectedIndex]
                : "FULL";

            var assetTypes = new List<string>();
            if (_cbFixed.IsChecked == true) assetTypes.Add("Fixed");
            if (_cbMoveable.IsChecked == true) assetTypes.Add("Moveable");
            if (_cbOperated.IsChecked == true) assetTypes.Add("Operated");
            if (_cbMonitored.IsChecked == true) assetTypes.Add("Monitored");
            if (_cbSafety.IsChecked == true) assetTypes.Add("Safety");
            opts["AssetTypes"] = string.Join(",", assetTypes);

            opts["StatusFilter"] = _statusCombo.SelectedItem?.ToString() ?? "All";

            // Tab 2: Worksheets
            var selectedWs = new List<string>();
            foreach (var kvp in _worksheetChecks)
            {
                if (kvp.Value.IsChecked == true)
                    selectedWs.Add(kvp.Key);
            }
            opts["SelectedWorksheets"] = string.Join(",", selectedWs);

            opts["IncludeResourceEnhanced"] = (_cbResourceEnhanced.IsChecked == true).ToString();
            opts["IncludeImpactEnhanced"] = (_cbImpactEnhanced.IsChecked == true).ToString();

            // Tab 3: Columns — collect excluded columns per worksheet
            var excluded = new List<string>();
            foreach (var wsKvp in _columnChecks)
            {
                var unchecked_ = new List<string>();
                foreach (var colKvp in wsKvp.Value)
                {
                    if (colKvp.Value.IsChecked != true && colKvp.Value.IsEnabled)
                        unchecked_.Add(colKvp.Key);
                }
                if (unchecked_.Count > 0)
                    excluded.Add(wsKvp.Key + ":" + string.Join(",", unchecked_));
            }
            opts["ExcludedColumns"] = string.Join(";", excluded);

            // Tab 4: Output
            opts["OutputDir"] = _outputPath?.Text ?? "";
            opts["ExportXLSX"] = (_cbExportXlsx.IsChecked == true).ToString();
            opts["ExportCSV"] = (_cbExportCsv.IsChecked == true).ToString();
            opts["StreamingExport"] = (_cbStreaming.IsChecked == true).ToString();

            return opts;
        }

        // ── Helper: Section header ──────────────────────────────────────

        private static TextBlock MakeSectionHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = HeaderBg,
                Margin = new Thickness(0, 8, 0, 2)
            };
        }

        // ── Helper: CheckBox ────────────────────────────────────────────

        private static CheckBox MakeCheckBox(string label, bool isChecked)
        {
            return new CheckBox
            {
                Content = label,
                IsChecked = isChecked,
                FontSize = 12,
                Foreground = FgDark,
                Margin = new Thickness(0, 3, 16, 3),
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        // ── Helper: Operation card ──────────────────────────────────────

        private static Border MakeOperationCard(
            string title,
            string description,
            SolidColorBrush accentColor,
            string dispatchTag,
            bool collectAllOptions)
        {
            var innerGrid = new Grid { Margin = new Thickness(12, 10, 12, 10) };
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            textStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = FgDark
            });

            textStack.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 11,
                Foreground = FgMuted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 12, 0)
            });

            Grid.SetColumn(textStack, 0);
            innerGrid.Children.Add(textStack);

            var runBtn = new Button
            {
                Content = "Run",
                Width = 70,
                Height = 30,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Background = accentColor,
                Foreground = FgWhite,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };

            runBtn.Click += (s, e) =>
            {
                _result = new COBieExportDashboardResult
                {
                    Confirmed = true,
                    Operation = dispatchTag,
                    Options = collectAllOptions ? CollectAllOptions() : new Dictionary<string, string>()
                };
                _window.DialogResult = true;
            };

            Grid.SetColumn(runBtn, 1);
            innerGrid.Children.Add(runBtn);

            var card = new Border
            {
                Background = PanelBg,
                BorderBrush = BorderLight,
                BorderThickness = new Thickness(4, 1, 1, 1),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 4, 0, 4),
                Child = innerGrid,
                Cursor = Cursors.Hand
            };

            // Set the left accent border color
            card.BorderBrush = accentColor;
            card.BorderThickness = new Thickness(4, 1, 1, 1);

            // Use a composite border to get the accent on the left only
            var outerBorder = new Border
            {
                BorderBrush = BorderLight,
                BorderThickness = new Thickness(0, 1, 1, 1),
                CornerRadius = new CornerRadius(0, 3, 3, 0),
                Background = PanelBg,
                Margin = new Thickness(0, 4, 0, 4)
            };

            var accentBar = new Border
            {
                Width = 4,
                Background = accentColor,
                CornerRadius = new CornerRadius(3, 0, 0, 3)
            };

            var cardDock = new DockPanel();
            DockPanel.SetDock(accentBar, Dock.Left);
            cardDock.Children.Add(accentBar);
            cardDock.Children.Add(innerGrid);

            outerBorder.Child = cardDock;

            return outerBorder;
        }

        // ── Helper: Format preset name ──────────────────────────────────

        private static string FormatPresetName(string key)
        {
            // "COMMERCIAL_OFFICE" → "Commercial Office"
            var parts = key.Split('_');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                    parts[i] = parts[i][0] + parts[i].Substring(1).ToLowerInvariant();
            }
            return string.Join(" ", parts);
        }
    }
}
