using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using StingTools.Core;
using Color = System.Windows.Media.Color;
using Grid = System.Windows.Controls.Grid;

namespace StingTools.UI
{
    /// <summary>
    /// Multi-page WPF wizard for COBie V2.4 export with configurable columns,
    /// worksheet selection, streaming export, and progress feedback.
    /// </summary>
    public static class COBieExportWizard
    {
        /// <summary>Launch the COBie export wizard and return collected settings, or null if cancelled.</summary>
        public static COBieExportSettings Show(Document doc)
        {
            var wizard = new StingWizardDialog("COBie V2.4 Export Wizard", 860, 620);
            wizard.AddPage(new PresetPage(doc));
            wizard.AddPage(new WorksheetPage());
            wizard.AddPage(new ColumnConfigPage());
            wizard.AddPage(new OutputPage(doc));

            bool? result = wizard.ShowDialog();
            if (result != true || !wizard.IsCompleted) return null;

            return BuildSettings(wizard.Results);
        }

        private static COBieExportSettings BuildSettings(Dictionary<string, object> results)
        {
            var s = new COBieExportSettings();
            if (results.TryGetValue("PresetKey", out var pk)) s.PresetKey = pk as string;
            if (results.TryGetValue("AssetTypes", out var at)) s.AssetTypes = at as string[];
            if (results.TryGetValue("StatusFilter", out var sf)) s.StatusFilter = sf as string;
            if (results.TryGetValue("SelectedWorksheets", out var sw)) s.SelectedWorksheets = sw as HashSet<string>;
            if (results.TryGetValue("ExcludedColumns", out var ec)) s.ExcludedColumns = ec as Dictionary<string, HashSet<string>>;
            if (results.TryGetValue("OutputDir", out var od)) s.OutputDir = od as string;
            if (results.TryGetValue("ExportXLSX", out var ex)) s.ExportXLSX = ex is true;
            if (results.TryGetValue("ExportCSV", out var csv)) s.ExportCSV = csv is true;
            if (results.TryGetValue("StreamingExport", out var se)) s.StreamingExport = se is true;
            if (results.TryGetValue("IncludeResourceEnhanced", out var ire)) s.IncludeResourceEnhanced = ire is true;
            if (results.TryGetValue("IncludeImpactEnhanced", out var iie)) s.IncludeImpactEnhanced = iie is true;
            return s;
        }

        // ════════════════════════════════════════════════════════════
        //  Page 1: Project Preset & Asset Configuration
        // ════════════════════════════════════════════════════════════
        private class PresetPage : WizardPage
        {
            private readonly Document _doc;
            private ComboBox _presetCombo;
            private ComboBox _statusCombo;
            private readonly List<CheckBox> _assetChecks = new();

            public PresetPage(Document doc)
            {
                _doc = doc;
                Title = "Preset";
                Description = "Select COBie project type and asset configuration.";
            }

            public override void BuildContent()
            {
                var panel = new StackPanel { Margin = new Thickness(4) };
                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Project Type Preset"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "Choose a COBie project type preset to pre-configure worksheet requirements and focus categories. " +
                    "Select 'Full Export' for all worksheets without filtering."));

                var presetItems = new List<string> { "FULL — Full Export (all worksheets)" };
                presetItems.Add("COMMERCIAL_OFFICE — Commercial Office");
                presetItems.Add("HEALTHCARE_NHS — Healthcare NHS");
                presetItems.Add("HEALTHCARE_PRIVATE — Healthcare Private");
                presetItems.Add("EDUCATION_SCHOOL — Education School");
                presetItems.Add("EDUCATION_UNI — Education University");
                presetItems.Add("RESIDENTIAL — Residential Standard");
                presetItems.Add("RESIDENTIAL_HIGHRISE — Residential High-Rise");
                presetItems.Add("RETAIL — Retail");
                presetItems.Add("HOTEL — Hotel/Hospitality");
                presetItems.Add("DATA_CENTRE — Data Centre");
                presetItems.Add("INDUSTRIAL — Industrial/Warehouse");
                presetItems.Add("TRANSPORT_STATION — Transport Station");
                presetItems.Add("TRANSPORT_AIRPORT — Transport Airport");
                presetItems.Add("DEFENCE — Defence MOD");
                presetItems.Add("HERITAGE — Heritage/Listed Building");
                presetItems.Add("MIXED_USE — Mixed-Use Development");
                presetItems.Add("LABORATORY — Laboratory/Research");
                presetItems.Add("SPORTS — Sports & Leisure");
                presetItems.Add("CULTURAL — Cultural (Museum/Gallery)");
                presetItems.Add("MODULAR — Modular/Off-Site");
                presetItems.Add("INFRASTRUCTURE_CIVIL — Infrastructure Civil");
                presetItems.Add("INFRASTRUCTURE_WATER — Infrastructure Water");
                presetItems.Add("FITOUT — Fit-Out Interior");

                var presetPanel = StingWizardDialog.MakeLabelledCombo("COBie Preset:",
                    presetItems.ToArray(), 0, out _presetCombo);
                panel.Children.Add(presetPanel);

                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Asset Type Filter"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "Select which asset types to include in the export. Unchecked types will be excluded from Component and Type worksheets."));

                var assetTypes = new[] { "Fixed", "Moveable", "Operated", "Monitored", "Safety" };
                foreach (var at in assetTypes)
                {
                    var cb = StingWizardDialog.MakeLabelledCheck(at, at == "Fixed" || at == "Moveable");
                    _assetChecks.Add(cb);
                    panel.Children.Add(cb);
                }

                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Element Status Filter"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "Optionally filter elements by their STATUS token value. 'All' includes every tagged element."));

                var statusItems = new[] { "All (no filter)", "NEW", "EXISTING", "DEMOLISHED", "TEMPORARY" };
                var statusPanel = StingWizardDialog.MakeLabelledCombo("Status:", statusItems, 0, out _statusCombo);
                panel.Children.Add(statusPanel);

                // Project info summary
                var pi = _doc.ProjectInformation;
                if (pi != null)
                {
                    panel.Children.Add(StingWizardDialog.MakeSectionHeader("Project Information"));
                    var infoText = new TextBlock
                    {
                        Text = $"Project: {pi.Name}\nAddress: {pi.Address}\nClient: {pi.ClientName}",
                        FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 100)),
                        Margin = new Thickness(0, 0, 0, 8)
                    };
                    panel.Children.Add(infoText);
                }

                Content = panel;
            }

            public override void CollectResults(Dictionary<string, object> results)
            {
                string presetText = _presetCombo.SelectedItem?.ToString() ?? "";
                string presetKey = presetText.StartsWith("FULL") ? null : presetText.Split(new[] { ' ' }, 2)[0].Trim();
                results["PresetKey"] = presetKey;

                var selectedAssets = _assetChecks.Where(c => c.IsChecked == true)
                    .Select(c => c.Content.ToString()).ToArray();
                results["AssetTypes"] = selectedAssets;

                string status = _statusCombo.SelectedItem?.ToString() ?? "";
                results["StatusFilter"] = status.StartsWith("All") ? null : status;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Page 2: Worksheet Selection
        // ════════════════════════════════════════════════════════════
        private class WorksheetPage : WizardPage
        {
            private readonly Dictionary<string, CheckBox> _wsChecks = new();
            private CheckBox _resourceEnhanced;
            private CheckBox _impactEnhanced;

            public WorksheetPage()
            {
                Title = "Worksheets";
                Description = "Select which COBie worksheets to include in the export.";
            }

            public override void BuildContent()
            {
                var panel = new StackPanel { Margin = new Thickness(4) };
                panel.Children.Add(StingWizardDialog.MakeSectionHeader("COBie V2.4 Worksheets"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "Check the worksheets to include. Core worksheets (Instruction, Contact, Facility) are always included."));

                var worksheets = new[]
                {
                    ("Instruction", "Generation metadata and standard references", true),
                    ("Contact", "Project team contact information", true),
                    ("Facility", "Building/site identification and units", true),
                    ("Floor", "Level definitions with elevations", true),
                    ("Space", "Room/space data with areas", true),
                    ("Zone", "Grouped spaces by zone type", true),
                    ("Type", "Equipment/asset type definitions", true),
                    ("Component", "Individual asset instances with tags", true),
                    ("System", "Building system groupings", true),
                    ("Assembly", "Wall/floor compound compositions", true),
                    ("Connection", "MEP connector relationships", true),
                    ("Spare", "Spare parts per equipment type", true),
                    ("Resource", "Labour/tool resources for maintenance", true),
                    ("Job", "Planned preventive maintenance tasks", true),
                    ("Impact", "Environmental/sustainability impact data", true),
                    ("Document", "Project document register", true),
                    ("Attribute", "Extended parameter attributes (40+ per element)", true),
                    ("Coordinate", "Element spatial coordinates and rotation", true),
                    ("Issue", "Project issues and RFIs", true),
                    ("PickLists", "Controlled vocabulary pick lists", true)
                };

                // 2-column layout
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                int row = 0;
                foreach (var (name, desc, defaultOn) in worksheets)
                {
                    if (row >= grid.RowDefinitions.Count)
                        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    int col = _wsChecks.Count % 2;
                    if (col == 0 && _wsChecks.Count > 0)
                        row++;
                    if (row >= grid.RowDefinitions.Count)
                        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    var cb = new CheckBox
                    {
                        Content = name,
                        ToolTip = desc,
                        IsChecked = defaultOn,
                        FontSize = 11,
                        Margin = new Thickness(2, 3, 2, 3),
                        Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 80))
                    };
                    // Core sheets always checked
                    if (name == "Instruction" || name == "Contact" || name == "Facility")
                        cb.IsEnabled = false;

                    _wsChecks[name] = cb;
                    Grid.SetRow(cb, row);
                    Grid.SetColumn(cb, col);
                    grid.Children.Add(cb);
                }
                panel.Children.Add(grid);

                // Select All / Deselect All
                var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
                var selectAll = new Button { Content = "Select All", FontSize = 11, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
                selectAll.Click += (s, e) => { foreach (var cb in _wsChecks.Values) if (cb.IsEnabled) cb.IsChecked = true; };
                btnPanel.Children.Add(selectAll);
                var deselectAll = new Button { Content = "Deselect Optional", FontSize = 11, Padding = new Thickness(12, 4, 12, 4) };
                deselectAll.Click += (s, e) => { foreach (var cb in _wsChecks.Values) if (cb.IsEnabled) cb.IsChecked = false; };
                btnPanel.Children.Add(deselectAll);
                panel.Children.Add(btnPanel);

                // Enhanced Resource/Impact options
                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Enhanced Worksheets"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "Enable enhanced data collection for Resource and Impact worksheets with additional detail."));

                _resourceEnhanced = StingWizardDialog.MakeLabelledCheck(
                    "Enhanced Resources — include skill requirements, hourly rates, certifications", true);
                panel.Children.Add(_resourceEnhanced);

                _impactEnhanced = StingWizardDialog.MakeLabelledCheck(
                    "Enhanced Impact — include lifecycle cost analysis, carbon per m², energy benchmarks", true);
                panel.Children.Add(_impactEnhanced);

                Content = panel;
            }

            public override void CollectResults(Dictionary<string, object> results)
            {
                var selected = new HashSet<string>();
                foreach (var kvp in _wsChecks)
                    if (kvp.Value.IsChecked == true) selected.Add(kvp.Key);
                results["SelectedWorksheets"] = selected;
                results["IncludeResourceEnhanced"] = _resourceEnhanced?.IsChecked == true;
                results["IncludeImpactEnhanced"] = _impactEnhanced?.IsChecked == true;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Page 3: Column Configuration
        // ════════════════════════════════════════════════════════════
        private class ColumnConfigPage : WizardPage
        {
            private ComboBox _worksheetSelector;
            private StackPanel _columnList;
            private readonly Dictionary<string, HashSet<string>> _excludedColumns = new();
            private readonly Dictionary<string, List<CheckBox>> _columnCheckboxes = new();

            // Core COBie columns per worksheet
            private static readonly Dictionary<string, string[]> WorksheetColumns = new()
            {
                ["Component"] = new[] { "Name", "CreatedBy", "CreatedOn", "TypeName", "Space",
                    "Description", "ExternalSystem", "ExternalObject", "ExternalIdentifier",
                    "SerialNumber", "InstallationDate", "WarrantyStartDate", "TagNumber",
                    "BarCode", "AssetIdentifier", "Category", "Discipline", "Location",
                    "Zone", "Level", "System", "Function", "ProductCode", "SequenceNumber", "Status" },
                ["Type"] = new[] { "Name", "CreatedBy", "CreatedOn", "Category", "Description",
                    "AssetType", "Manufacturer", "ModelNumber", "WarrantyGuarantorParts",
                    "WarrantyDurationParts", "WarrantyGuarantorLabor", "WarrantyDurationLabor",
                    "ReplacementCost", "ExpectedLife", "NominalLength", "NominalWidth", "NominalHeight",
                    "Shape", "Size", "Color", "Finish", "Grade", "Material", "SustainabilityPerformance" },
                ["Attribute"] = new[] { "Name", "CreatedBy", "CreatedOn", "Category", "SheetName",
                    "RowName", "Value", "Unit", "Description", "AllowedValues" },
                ["Impact"] = new[] { "Name", "CreatedBy", "CreatedOn", "ImpactType", "ImpactStage",
                    "SheetName", "RowName", "Value", "Unit", "Description" }
            };

            public ColumnConfigPage()
            {
                Title = "Columns";
                Description = "Configure which columns to include per worksheet.";
            }

            public override void BuildContent()
            {
                var panel = new StackPanel { Margin = new Thickness(4) };
                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Column Configuration"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "Select a worksheet to configure its columns. Uncheck columns to exclude them from export. " +
                    "Required columns (Name, CreatedBy, CreatedOn) cannot be excluded."));

                var wsItems = WorksheetColumns.Keys.ToArray();
                var wsPanel = StingWizardDialog.MakeLabelledCombo("Worksheet:", wsItems, 0, out _worksheetSelector);
                _worksheetSelector.SelectionChanged += (s, e) => RefreshColumns();
                panel.Children.Add(wsPanel);

                _columnList = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                panel.Children.Add(_columnList);

                Content = panel;
            }

            public override void OnNavigatedTo()
            {
                RefreshColumns();
            }

            private void RefreshColumns()
            {
                _columnList.Children.Clear();
                string ws = _worksheetSelector?.SelectedItem?.ToString();
                if (ws == null || !WorksheetColumns.ContainsKey(ws)) return;

                if (!_columnCheckboxes.ContainsKey(ws))
                {
                    _columnCheckboxes[ws] = new List<CheckBox>();
                    foreach (string col in WorksheetColumns[ws])
                    {
                        bool required = col == "Name" || col == "CreatedBy" || col == "CreatedOn";
                        bool excluded = _excludedColumns.TryGetValue(ws, out var excSet) && excSet.Contains(col);
                        var cb = new CheckBox
                        {
                            Content = col,
                            IsChecked = !excluded,
                            IsEnabled = !required,
                            FontSize = 11,
                            Margin = new Thickness(2, 2, 2, 2),
                            Foreground = new SolidColorBrush(required
                                ? Color.FromRgb(88, 44, 131)
                                : Color.FromRgb(60, 60, 80))
                        };
                        if (required) cb.FontWeight = FontWeights.SemiBold;
                        cb.Tag = col;
                        _columnCheckboxes[ws].Add(cb);
                    }
                }

                // 3-column layout for columns
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition());

                int idx = 0;
                foreach (var cb in _columnCheckboxes[ws])
                {
                    int r = idx / 3;
                    int c = idx % 3;
                    while (r >= grid.RowDefinitions.Count)
                        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Grid.SetRow(cb, r);
                    Grid.SetColumn(cb, c);
                    grid.Children.Add(cb);
                    idx++;
                }
                _columnList.Children.Add(grid);
            }

            public override void CollectResults(Dictionary<string, object> results)
            {
                var excluded = new Dictionary<string, HashSet<string>>();
                foreach (var kvp in _columnCheckboxes)
                {
                    var excl = new HashSet<string>();
                    foreach (var cb in kvp.Value)
                        if (cb.IsChecked != true && cb.IsEnabled)
                            excl.Add(cb.Tag.ToString());
                    if (excl.Count > 0) excluded[kvp.Key] = excl;
                }
                results["ExcludedColumns"] = excluded;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Page 4: Output Options
        // ════════════════════════════════════════════════════════════
        private class OutputPage : WizardPage
        {
            private readonly Document _doc;
            private TextBox _outputDir;
            private CheckBox _xlsxCheck;
            private CheckBox _csvCheck;
            private CheckBox _streamingCheck;

            public OutputPage(Document doc)
            {
                _doc = doc;
                Title = "Output";
                Description = "Configure export format and output location.";
            }

            public override void BuildContent()
            {
                var panel = new StackPanel { Margin = new Thickness(4) };

                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Export Format"));
                _xlsxCheck = StingWizardDialog.MakeLabelledCheck("Excel Workbook (.xlsx) — single file with all worksheets", true);
                panel.Children.Add(_xlsxCheck);
                _csvCheck = StingWizardDialog.MakeLabelledCheck("CSV Files — one file per worksheet", true);
                panel.Children.Add(_csvCheck);

                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Performance"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "Streaming export writes data progressively, reducing memory usage for large models (10,000+ elements)."));
                _streamingCheck = StingWizardDialog.MakeLabelledCheck("Enable streaming export with progress dialog", true);
                panel.Children.Add(_streamingCheck);

                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Output Location"));
                string defaultDir = "";
                try
                {
                    defaultDir = Path.Combine(
                        Path.GetDirectoryName(_doc.PathName) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "STING_BIM_MANAGER",
                        $"COBie_V24_{DateTime.Now:yyyyMMdd}");
                }
                catch (Exception ex) { StingLog.Warn($"COBie output path: {ex.Message}"); }

                var dirPanel = StingWizardDialog.MakeLabelledText("Output Directory:", defaultDir, out _outputDir);
                panel.Children.Add(dirPanel);

                var browseBtn = new Button
                {
                    Content = "Browse...", FontSize = 11,
                    Padding = new Thickness(12, 4, 12, 4),
                    Margin = new Thickness(0, 4, 0, 8),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                browseBtn.Click += (s, e) =>
                {
                    // Use SaveFileDialog as a folder picker workaround (select any file in target folder)
                    var dlg = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Select output folder (save placeholder file)",
                        FileName = "COBie_Output",
                        Filter = "Folder|*.folder",
                        CheckPathExists = true
                    };
                    if (!string.IsNullOrEmpty(_outputDir.Text))
                    {
                        try { dlg.InitialDirectory = _outputDir.Text; }
                        catch (Exception ex2) { StingLog.Warn($"COBie browse dir: {ex2.Message}"); }
                    }
                    if (dlg.ShowDialog() == true)
                        _outputDir.Text = System.IO.Path.GetDirectoryName(dlg.FileName) ?? _outputDir.Text;
                };
                panel.Children.Add(browseBtn);

                // Summary
                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Export Summary"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "Click 'Finish' to begin the COBie export. A progress dialog will show export status for each worksheet."));

                Content = panel;
            }

            public override string Validate()
            {
                if (_xlsxCheck?.IsChecked != true && _csvCheck?.IsChecked != true)
                    return "Please select at least one export format (XLSX or CSV).";
                if (string.IsNullOrWhiteSpace(_outputDir?.Text))
                    return "Please specify an output directory.";
                return null;
            }

            public override void CollectResults(Dictionary<string, object> results)
            {
                results["OutputDir"] = _outputDir?.Text ?? "";
                results["ExportXLSX"] = _xlsxCheck?.IsChecked == true;
                results["ExportCSV"] = _csvCheck?.IsChecked == true;
                results["StreamingExport"] = _streamingCheck?.IsChecked == true;
            }
        }
    }

    /// <summary>Settings collected from the COBie export wizard.</summary>
    public class COBieExportSettings
    {
        public string PresetKey { get; set; }
        public string[] AssetTypes { get; set; } = { "Fixed", "Moveable" };
        public string StatusFilter { get; set; }
        public HashSet<string> SelectedWorksheets { get; set; } = new();
        public Dictionary<string, HashSet<string>> ExcludedColumns { get; set; } = new();
        public string OutputDir { get; set; } = "";
        public bool ExportXLSX { get; set; } = true;
        public bool ExportCSV { get; set; } = true;
        public bool StreamingExport { get; set; } = true;
        public bool IncludeResourceEnhanced { get; set; } = true;
        public bool IncludeImpactEnhanced { get; set; } = true;
    }
}
