using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Color = System.Windows.Media.Color;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Multi-page WPF wizard for bidirectional Excel data exchange.
    /// Consolidates Export, Import, and RoundTrip workflows into a single wizard.
    /// </summary>
    public static class ExcelExchangeWizard
    {
        public static ExcelExchangeSettings Show(Document doc, string defaultMode = "Export")
        {
            var wizard = new StingWizardDialog("Excel Data Exchange", 800, 580);
            wizard.AddPage(new ModePage(defaultMode));
            wizard.AddPage(new ScopeAndColumnsPage(doc));
            wizard.AddPage(new FileAndOptionsPage(doc));

            bool? result = wizard.ShowDialog();
            if (result != true || !wizard.IsCompleted) return null;

            return BuildSettings(wizard.Results);
        }

        private static ExcelExchangeSettings BuildSettings(Dictionary<string, object> r)
        {
            var s = new ExcelExchangeSettings();
            if (r.TryGetValue("Mode", out var m)) s.Mode = m as string ?? "Export";
            if (r.TryGetValue("Scope", out var sc)) s.Scope = sc as string ?? "AllTaggable";
            if (r.TryGetValue("FilePath", out var fp)) s.FilePath = fp as string ?? "";
            if (r.TryGetValue("IncludeTags", out var it)) s.IncludeTags = it is true;
            if (r.TryGetValue("IncludeIdentity", out var ii)) s.IncludeIdentity = ii is true;
            if (r.TryGetValue("IncludeSpatial", out var isp)) s.IncludeSpatial = isp is true;
            if (r.TryGetValue("IncludeMEP", out var imep)) s.IncludeMEP = imep is true;
            if (r.TryGetValue("IncludeDimensions", out var idim)) s.IncludeDimensions = idim is true;
            if (r.TryGetValue("IncludeCost", out var ic)) s.IncludeCost = ic is true;
            if (r.TryGetValue("IncludeClassification", out var icl)) s.IncludeClassification = icl is true;
            if (r.TryGetValue("ValidationStrict", out var vs)) s.ValidationStrict = vs is true;
            if (r.TryGetValue("AutoRebuildTags", out var art)) s.AutoRebuildTags = art is true;
            if (r.TryGetValue("CreateAuditTrail", out var cat)) s.CreateAuditTrail = cat is true;
            return s;
        }

        // ════════════════════════════════════════════════════════════
        //  Page 1: Exchange Mode
        // ════════════════════════════════════════════════════════════
        private class ModePage : WizardPage
        {
            private readonly string _defaultMode;
            private readonly Dictionary<string, RadioButton> _modeRadios = new();

            public ModePage(string defaultMode)
            {
                _defaultMode = defaultMode;
                Title = "Mode";
                Description = "Select the data exchange direction.";
            }

            public override void BuildContent()
            {
                var panel = new StackPanel { Margin = new Thickness(4) };
                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Exchange Mode"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "Choose the direction of data exchange between Revit and Excel."));

                var modes = new (string key, string label, string desc, string icon)[]
                {
                    ("Export", "Export to Excel", "Write Revit element data to a new Excel workbook for external review or editing", ">>>"),
                    ("Import", "Import from Excel", "Read modified data from an Excel file and update Revit element parameters", "<<<"),
                    ("RoundTrip", "Round-Trip (Export + Edit + Import)", "Export data, open in Excel for editing, then import changes back automatically", "<->"),
                    ("Template", "Export Empty Template", "Create a blank Excel template with headers and validation lists for manual data entry", "[T]")
                };

                foreach (var (key, label, desc, icon) in modes)
                {
                    var border = new Border
                    {
                        BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 230)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6),
                        Margin = new Thickness(0, 4, 0, 4),
                        Padding = new Thickness(14, 10, 14, 10),
                        Background = Brushes.White
                    };

                    var row = new DockPanel();
                    var iconBlock = new TextBlock
                    {
                        Text = icon, FontSize = 14, FontWeight = FontWeights.Bold,
                        Width = 36, TextAlignment = TextAlignment.Center,
                        Foreground = new SolidColorBrush(Color.FromRgb(88, 44, 131)),
                        VerticalAlignment = VerticalAlignment.Center,
                        FontFamily = new FontFamily("Consolas"),
                        Margin = new Thickness(0, 0, 10, 0)
                    };
                    DockPanel.SetDock(iconBlock, Dock.Left);
                    row.Children.Add(iconBlock);

                    var rb = new RadioButton
                    {
                        GroupName = "Mode",
                        IsChecked = key == _defaultMode,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    _modeRadios[key] = rb;
                    DockPanel.SetDock(rb, Dock.Left);
                    row.Children.Add(rb);

                    var textPanel = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
                    textPanel.Children.Add(new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 60)) });
                    textPanel.Children.Add(new TextBlock { Text = desc, FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 120)), TextWrapping = TextWrapping.Wrap });
                    row.Children.Add(textPanel);

                    border.Child = row;
                    border.MouseLeftButtonUp += (s, e) => { rb.IsChecked = true; };
                    panel.Children.Add(border);
                }

                Content = panel;
            }

            public override void CollectResults(Dictionary<string, object> results)
            {
                string mode = _modeRadios.FirstOrDefault(kv => kv.Value.IsChecked == true).Key ?? "Export";
                results["Mode"] = mode;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Page 2: Scope & Column Groups
        // ════════════════════════════════════════════════════════════
        private class ScopeAndColumnsPage : WizardPage
        {
            private readonly Document _doc;
            private ComboBox _scopeCombo;
            private CheckBox _tags, _identity, _spatial, _mep, _dimensions, _cost, _classification;

            public ScopeAndColumnsPage(Document doc)
            {
                _doc = doc;
                Title = "Data";
                Description = "Choose element scope and data columns.";
            }

            public override void BuildContent()
            {
                var panel = new StackPanel { Margin = new Thickness(4) };

                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Element Scope"));
                var scopes = new[] { "All taggable elements", "Selected elements only", "Active view elements" };
                var scopePanel = StingWizardDialog.MakeLabelledCombo("Scope:", scopes, 0, out _scopeCombo);
                panel.Children.Add(scopePanel);

                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Column Groups"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "Select which parameter groups to include as columns in the Excel file. " +
                    "Core columns (Element ID, Category, Family) are always included."));

                _tags = StingWizardDialog.MakeLabelledCheck("Tags — DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ, TAG1-TAG7", true);
                panel.Children.Add(_tags);
                _identity = StingWizardDialog.MakeLabelledCheck("Identity — Family name, Type name, Mark, Description, Manufacturer", true);
                panel.Children.Add(_identity);
                _spatial = StingWizardDialog.MakeLabelledCheck("Spatial — Room, Level, Grid Reference, LOC, ZONE", true);
                panel.Children.Add(_spatial);
                _mep = StingWizardDialog.MakeLabelledCheck("MEP — Flow, Pressure, Voltage, Circuit, System Name", false);
                panel.Children.Add(_mep);
                _dimensions = StingWizardDialog.MakeLabelledCheck("Dimensions — Width, Height, Depth, Area, Volume", false);
                panel.Children.Add(_dimensions);
                _cost = StingWizardDialog.MakeLabelledCheck("Cost — Unit Rate, Total Cost, Embodied Carbon", false);
                panel.Children.Add(_cost);
                _classification = StingWizardDialog.MakeLabelledCheck("Classification — Uniclass, OmniClass, Keynote, COBie Type", false);
                panel.Children.Add(_classification);

                // Select All/None
                var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                var selectAll = new Button { Content = "Select All", FontSize = 11, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
                selectAll.Click += (s, e) => { _tags.IsChecked = _identity.IsChecked = _spatial.IsChecked = _mep.IsChecked = _dimensions.IsChecked = _cost.IsChecked = _classification.IsChecked = true; };
                btnPanel.Children.Add(selectAll);
                var essential = new Button { Content = "Essential Only", FontSize = 11, Padding = new Thickness(12, 4, 12, 4) };
                essential.Click += (s, e) => { _tags.IsChecked = _identity.IsChecked = _spatial.IsChecked = true; _mep.IsChecked = _dimensions.IsChecked = _cost.IsChecked = _classification.IsChecked = false; };
                btnPanel.Children.Add(essential);
                panel.Children.Add(btnPanel);

                Content = panel;
            }

            public override void CollectResults(Dictionary<string, object> results)
            {
                string scope = _scopeCombo?.SelectedItem?.ToString() ?? "";
                results["Scope"] = scope.Contains("Selected") ? "Selected" : scope.Contains("Active") ? "ActiveView" : "AllTaggable";
                results["IncludeTags"] = _tags?.IsChecked == true;
                results["IncludeIdentity"] = _identity?.IsChecked == true;
                results["IncludeSpatial"] = _spatial?.IsChecked == true;
                results["IncludeMEP"] = _mep?.IsChecked == true;
                results["IncludeDimensions"] = _dimensions?.IsChecked == true;
                results["IncludeCost"] = _cost?.IsChecked == true;
                results["IncludeClassification"] = _classification?.IsChecked == true;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Page 3: File Path & Import Options
        // ════════════════════════════════════════════════════════════
        private class FileAndOptionsPage : WizardPage
        {
            private readonly Document _doc;
            private TextBox _filePath;
            private CheckBox _validationStrict;
            private CheckBox _autoRebuild;
            private CheckBox _auditTrail;

            public FileAndOptionsPage(Document doc)
            {
                _doc = doc;
                Title = "Options";
                Description = "Set file path and processing options.";
            }

            public override void BuildContent()
            {
                var panel = new StackPanel { Margin = new Thickness(4) };

                panel.Children.Add(StingWizardDialog.MakeSectionHeader("File Path"));

                string defaultPath = "";
                try
                {
                    string dir = Path.GetDirectoryName(_doc.PathName) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    defaultPath = Path.Combine(dir, $"STING_Excel_{DateTime.Now:yyyyMMdd}.xlsx");
                }
                catch (Exception ex) { StingLog.Warn($"Excel path: {ex.Message}"); }

                var filePanel = StingWizardDialog.MakeLabelledText("Excel File:", defaultPath, out _filePath);
                panel.Children.Add(filePanel);

                var browseBtn = new Button
                {
                    Content = "Browse...", FontSize = 11,
                    Padding = new Thickness(12, 4, 12, 4),
                    Margin = new Thickness(0, 4, 0, 8),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                browseBtn.Click += (s, e) =>
                {
                    string mode = Wizard?.Results?.ContainsKey("Mode") == true
                        ? Wizard.Results["Mode"] as string ?? "Export" : "Export";

                    if (mode == "Import" || mode == "RoundTrip")
                    {
                        var dlg = new Microsoft.Win32.OpenFileDialog
                        {
                            Filter = "Excel Files|*.xlsx|All Files|*.*",
                            Title = "Select Excel File"
                        };
                        if (dlg.ShowDialog() == true) _filePath.Text = dlg.FileName;
                    }
                    else
                    {
                        var dlg = new Microsoft.Win32.SaveFileDialog
                        {
                            Filter = "Excel Files|*.xlsx",
                            Title = "Save Excel File",
                            FileName = Path.GetFileName(_filePath.Text)
                        };
                        if (dlg.ShowDialog() == true) _filePath.Text = dlg.FileName;
                    }
                };
                panel.Children.Add(browseBtn);

                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Import Options"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "These options apply to Import and Round-Trip modes."));

                _validationStrict = StingWizardDialog.MakeLabelledCheck(
                    "Strict validation — reject rows with invalid DISC/SYS/FUNC/PROD codes", true);
                panel.Children.Add(_validationStrict);

                _autoRebuild = StingWizardDialog.MakeLabelledCheck(
                    "Auto-rebuild tags — re-derive TAG1 from imported token values", true);
                panel.Children.Add(_autoRebuild);

                _auditTrail = StingWizardDialog.MakeLabelledCheck(
                    "Create audit trail — log all changes to STING_EXCEL_AUDIT.csv", true);
                panel.Children.Add(_auditTrail);

                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Processing"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "Click 'Finish' to execute the exchange. A progress dialog will show status during processing."));

                Content = panel;
            }

            public override string Validate()
            {
                if (string.IsNullOrWhiteSpace(_filePath?.Text))
                    return "Please specify an Excel file path.";
                return null;
            }

            public override void CollectResults(Dictionary<string, object> results)
            {
                results["FilePath"] = _filePath?.Text ?? "";
                results["ValidationStrict"] = _validationStrict?.IsChecked == true;
                results["AutoRebuildTags"] = _autoRebuild?.IsChecked == true;
                results["CreateAuditTrail"] = _auditTrail?.IsChecked == true;
            }
        }
    }

    /// <summary>Settings from the Excel exchange wizard.</summary>
    public class ExcelExchangeSettings
    {
        public string Mode { get; set; } = "Export";
        public string Scope { get; set; } = "AllTaggable";
        public string FilePath { get; set; } = "";
        public bool IncludeTags { get; set; } = true;
        public bool IncludeIdentity { get; set; } = true;
        public bool IncludeSpatial { get; set; } = true;
        public bool IncludeMEP { get; set; }
        public bool IncludeDimensions { get; set; }
        public bool IncludeCost { get; set; }
        public bool IncludeClassification { get; set; }
        public bool ValidationStrict { get; set; } = true;
        public bool AutoRebuildTags { get; set; } = true;
        public bool CreateAuditTrail { get; set; } = true;
    }
}
