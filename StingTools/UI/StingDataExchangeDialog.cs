using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Result from StingDataExchangeDialog containing export/import configuration.
    /// </summary>
    public class DataExchangeResult
    {
        /// <summary>Direction: "Export" or "Import".</summary>
        public string Direction { get; set; } = "Export";
        /// <summary>Selected categories to exchange.</summary>
        public List<string> Categories { get; set; } = new();
        /// <summary>Selected parameters to exchange.</summary>
        public List<string> Parameters { get; set; } = new();
        /// <summary>Format: "CSV", "Excel", "JSON".</summary>
        public string Format { get; set; } = "CSV";
        /// <summary>Full file path.</summary>
        public string FilePath { get; set; }
        /// <summary>Scope: "ActiveView", "Selection", "Project".</summary>
        public string Scope { get; set; } = "Project";
        /// <summary>Include element ID column.</summary>
        public bool IncludeElementId { get; set; } = true;
        /// <summary>Include family/type columns.</summary>
        public bool IncludeFamilyType { get; set; } = true;
        /// <summary>Whether user cancelled.</summary>
        public bool Cancelled { get; set; } = true;
    }

    /// <summary>
    /// Unified data exchange dialog for exporting/importing element data.
    /// Provides category selection, parameter column mapping, format choice
    /// and scope selection in a single window.
    /// </summary>
    internal static class StingDataExchangeDialog
    {
        // ── Theme colours ──
        private static readonly System.Windows.Media.Color BgLight = System.Windows.Media.Color.FromRgb(0xF5, 0xF5, 0xF5);
        private static readonly System.Windows.Media.Color BgWhite = System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF);
        private static readonly System.Windows.Media.Color BgHeader = System.Windows.Media.Color.FromRgb(0x00, 0x69, 0x5C);
        private static readonly System.Windows.Media.Color AccentTeal = System.Windows.Media.Color.FromRgb(0x00, 0x96, 0x88);
        private static readonly System.Windows.Media.Color FgDark = System.Windows.Media.Color.FromRgb(0x22, 0x22, 0x22);
        private static readonly System.Windows.Media.Color FgSubtle = System.Windows.Media.Color.FromRgb(0x77, 0x77, 0x77);
        private static readonly System.Windows.Media.Color BorderLight = System.Windows.Media.Color.FromRgb(0xD0, 0xD0, 0xD0);

        private static readonly SolidColorBrush BrBgLight = new(BgLight);
        private static readonly SolidColorBrush BrBgWhite = new(BgWhite);
        private static readonly SolidColorBrush BrBgHeader = new(BgHeader);
        private static readonly SolidColorBrush BrAccent = new(AccentTeal);
        private static readonly SolidColorBrush BrFgDark = new(FgDark);
        private static readonly SolidColorBrush BrFgSubtle = new(FgSubtle);
        private static readonly SolidColorBrush BrBorder = new(BorderLight);

        /// <summary>
        /// Show the data exchange dialog. Returns null if cancelled.
        /// </summary>
        public static DataExchangeResult Show(Document doc)
        {
            var result = new DataExchangeResult();

            var win = new Window
            {
                Title = "STING Data Exchange",
                Width = 700,
                Height = 550,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResize,
                Background = BrBgLight
            };

            try
            {
                var hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (hwnd != IntPtr.Zero)
                    new System.Windows.Interop.WindowInteropHelper(win).Owner = hwnd;
            }
            catch (Exception ex) { StingLog.Warn($"DataExchangeDialog owner set failed: {ex.Message}"); }

            var root = new DockPanel { LastChildFill = true };

            // ── Header ──
            var header = new Border
            {
                Background = BrBgHeader,
                Padding = new Thickness(16, 10, 16, 10)
            };
            header.Child = new TextBlock
            {
                Text = "DATA EXCHANGE — Export / Import",
                FontSize = 15, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // ── Footer ──
            var footer = new Border
            {
                Background = BrBgWhite,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10)
            };
            var footerPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            var btnCancel = new Button
            {
                Content = "Cancel", Width = 80, Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0)),
                Foreground = BrFgDark, BorderThickness = new Thickness(0)
            };
            btnCancel.Click += (s, e) => { result.Cancelled = true; win.DialogResult = false; win.Close(); };
            footerPanel.Children.Add(btnCancel);

            var btnOk = new Button
            {
                Content = "Execute", Width = 100, Height = 30,
                Background = BrAccent, Foreground = Brushes.White,
                FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0)
            };
            btnOk.Click += (s, e) => { result.Cancelled = false; win.DialogResult = true; win.Close(); };
            footerPanel.Children.Add(btnOk);

            footer.Child = footerPanel;
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            // ── Main content ──
            var mainGrid = new System.Windows.Controls.Grid { Margin = new Thickness(12) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ── Direction + Format row ──
            var topRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

            topRow.Children.Add(new TextBlock { Text = "Direction:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Foreground = BrFgDark });
            var cbDirection = new System.Windows.Controls.ComboBox { Width = 100, Margin = new Thickness(0, 0, 16, 0) };
            cbDirection.Items.Add("Export");
            cbDirection.Items.Add("Import");
            cbDirection.SelectedIndex = 0;
            cbDirection.SelectionChanged += (s, e) => { result.Direction = cbDirection.SelectedItem?.ToString() ?? "Export"; };
            topRow.Children.Add(cbDirection);

            topRow.Children.Add(new TextBlock { Text = "Format:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Foreground = BrFgDark });
            var cbFormat = new System.Windows.Controls.ComboBox { Width = 100, Margin = new Thickness(0, 0, 16, 0) };
            cbFormat.Items.Add("CSV");
            cbFormat.Items.Add("Excel");
            cbFormat.Items.Add("JSON");
            cbFormat.SelectedIndex = 0;
            cbFormat.SelectionChanged += (s, e) => { result.Format = cbFormat.SelectedItem?.ToString() ?? "CSV"; };
            topRow.Children.Add(cbFormat);

            topRow.Children.Add(new TextBlock { Text = "Scope:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Foreground = BrFgDark });
            var cbScope = new System.Windows.Controls.ComboBox { Width = 120 };
            cbScope.Items.Add("Project");
            cbScope.Items.Add("Active View");
            cbScope.Items.Add("Selection");
            cbScope.SelectedIndex = 0;
            cbScope.SelectionChanged += (s, e) =>
            {
                string sel = cbScope.SelectedItem?.ToString() ?? "Project";
                result.Scope = sel.Replace(" ", "");
            };
            topRow.Children.Add(cbScope);

            System.Windows.Controls.Grid.SetRow(topRow, 0);
            mainGrid.Children.Add(topRow);

            // ── Category + Parameter lists ──
            var listsGrid = new System.Windows.Controls.Grid();
            listsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            listsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            listsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Categories
            var catPanel = new StackPanel();
            catPanel.Children.Add(new TextBlock { Text = "Categories", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4), Foreground = BrFgDark });
            var catList = new System.Windows.Controls.ListBox { SelectionMode = SelectionMode.Multiple, Height = 300 };

            var categories = new[] { "Air Terminals", "Cable Trays", "Conduits", "Doors", "Duct Accessories",
                "Duct Fittings", "Ducts", "Electrical Equipment", "Electrical Fixtures", "Floors",
                "Furniture", "Generic Models", "Lighting Devices", "Lighting Fixtures", "Mechanical Equipment",
                "Pipe Accessories", "Pipe Fittings", "Pipes", "Plumbing Fixtures", "Roofs",
                "Rooms", "Sprinklers", "Walls", "Windows" };
            foreach (string cat in categories)
            {
                var cb = new System.Windows.Controls.CheckBox { Content = cat, IsChecked = true, Margin = new Thickness(2) };
                cb.Checked += (s, e) => { if (!result.Categories.Contains(cat)) result.Categories.Add(cat); };
                cb.Unchecked += (s, e) => { result.Categories.Remove(cat); };
                result.Categories.Add(cat);
                catList.Items.Add(cb);
            }
            catPanel.Children.Add(catList);
            System.Windows.Controls.Grid.SetColumn(catPanel, 0);
            listsGrid.Children.Add(catPanel);

            // Parameters
            var paramPanel = new StackPanel();
            paramPanel.Children.Add(new TextBlock { Text = "Parameters", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4), Foreground = BrFgDark });
            var paramList = new System.Windows.Controls.ListBox { SelectionMode = SelectionMode.Multiple, Height = 300 };

            var defaultParams = new[] { "ASS_TAG_1", "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT",
                "ASS_LVL_COD_TXT", "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT", "ASS_PRODCT_COD_TXT",
                "ASS_SEQ_NUM_TXT", "ASS_TAG_7_TXT", "ASS_STATUS_TXT", "ASS_REVISION_TXT" };
            var optionalParams = new[] { "Width", "Height", "Area", "Volume", "Length", "Flow", "Voltage",
                "Mark", "Comments", "Level", "Room Name", "Room Number" };

            foreach (string p in defaultParams)
            {
                var cb = new System.Windows.Controls.CheckBox { Content = p, IsChecked = true, Margin = new Thickness(2) };
                cb.Checked += (s, e) => { if (!result.Parameters.Contains(p)) result.Parameters.Add(p); };
                cb.Unchecked += (s, e) => { result.Parameters.Remove(p); };
                result.Parameters.Add(p);
                paramList.Items.Add(cb);
            }
            foreach (string p in optionalParams)
            {
                var cb = new System.Windows.Controls.CheckBox { Content = p, IsChecked = false, Margin = new Thickness(2) };
                cb.Checked += (s, e) => { if (!result.Parameters.Contains(p)) result.Parameters.Add(p); };
                cb.Unchecked += (s, e) => { result.Parameters.Remove(p); };
                paramList.Items.Add(cb);
            }
            paramPanel.Children.Add(paramList);
            System.Windows.Controls.Grid.SetColumn(paramPanel, 2);
            listsGrid.Children.Add(paramPanel);

            System.Windows.Controls.Grid.SetRow(listsGrid, 1);
            mainGrid.Children.Add(listsGrid);

            // ── Options row ──
            var optRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var cbIncId = new System.Windows.Controls.CheckBox { Content = "Include Element ID", IsChecked = true, Margin = new Thickness(0, 0, 16, 0) };
            cbIncId.Checked += (s, e) => result.IncludeElementId = true;
            cbIncId.Unchecked += (s, e) => result.IncludeElementId = false;
            optRow.Children.Add(cbIncId);

            var cbIncFam = new System.Windows.Controls.CheckBox { Content = "Include Family/Type", IsChecked = true, Margin = new Thickness(0, 0, 16, 0) };
            cbIncFam.Checked += (s, e) => result.IncludeFamilyType = true;
            cbIncFam.Unchecked += (s, e) => result.IncludeFamilyType = false;
            optRow.Children.Add(cbIncFam);

            var btnBrowse = new Button { Content = "Browse...", Width = 80, Height = 24, Margin = new Thickness(0, 0, 8, 0) };
            var tbPath = new TextBlock { Text = "(default: project folder)", Foreground = BrFgSubtle, VerticalAlignment = VerticalAlignment.Center };
            btnBrowse.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|Excel files (*.xlsx)|*.xlsx|JSON files (*.json)|*.json|All files (*.*)|*.*",
                    FileName = $"STING_Export_{DateTime.Now:yyyyMMdd}"
                };
                if (dlg.ShowDialog() == true)
                {
                    result.FilePath = dlg.FileName;
                    tbPath.Text = Path.GetFileName(dlg.FileName);
                }
            };
            optRow.Children.Add(btnBrowse);
            optRow.Children.Add(tbPath);

            System.Windows.Controls.Grid.SetRow(optRow, 2);
            mainGrid.Children.Add(optRow);

            root.Children.Add(mainGrid);
            win.Content = root;

            bool? dialogResult = win.ShowDialog();
            if (dialogResult == true)
                return result;

            return null;
        }
    }

    /// <summary>
    /// Engine to execute data exchange operations from StingDataExchangeDialog results.
    /// </summary>
    internal static class DataExchangeEngine
    {
        /// <summary>
        /// Execute the data exchange operation configured in the result.
        /// </summary>
        public static void Execute(Document doc, UIDocument uidoc, DataExchangeResult config)
        {
            if (config == null || config.Cancelled) return;

            try
            {
                if (config.Direction == "Export")
                    ExecuteExport(doc, uidoc, config);
                else
                    ExecuteImport(doc, config);
            }
            catch (Exception ex)
            {
                StingLog.Error("DataExchangeEngine.Execute failed", ex);
                TaskDialog.Show("STING Data Exchange", $"Operation failed:\n{ex.Message}");
            }
        }

        private static void ExecuteExport(Document doc, UIDocument uidoc, DataExchangeResult config)
        {
            // Determine output path
            string outputPath = config.FilePath;
            if (string.IsNullOrEmpty(outputPath))
            {
                string ext = config.Format == "Excel" ? ".xlsx" : config.Format == "JSON" ? ".json" : ".csv";
                outputPath = OutputLocationHelper.GetTimestampedPath(doc, "STING_Export", ext);
            }

            // Collect elements
            var categoryNames = new HashSet<string>(config.Categories, StringComparer.OrdinalIgnoreCase);
            FilteredElementCollector collector;

            if (config.Scope == "ActiveView" && doc.ActiveView != null)
                collector = new FilteredElementCollector(doc, doc.ActiveView.Id);
            else if (config.Scope == "Selection" && uidoc != null)
            {
                var selIds = uidoc.Selection.GetElementIds();
                if (selIds.Count == 0)
                {
                    TaskDialog.Show("STING Data Exchange", "No elements selected.");
                    return;
                }
                collector = new FilteredElementCollector(doc, selIds);
            }
            else
                collector = new FilteredElementCollector(doc);

            var elements = collector
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && categoryNames.Contains(e.Category.Name))
                .ToList();

            if (elements.Count == 0)
            {
                TaskDialog.Show("STING Data Exchange", "No elements found matching selected categories.");
                return;
            }

            // Build CSV
            var lines = new List<string>();
            var headerCols = new List<string>();
            if (config.IncludeElementId) headerCols.Add("ElementId");
            headerCols.Add("Category");
            if (config.IncludeFamilyType) { headerCols.Add("Family"); headerCols.Add("Type"); }
            headerCols.AddRange(config.Parameters);
            lines.Add(string.Join(",", headerCols.Select(QuoteCsv)));

            foreach (var el in elements)
            {
                var row = new List<string>();
                if (config.IncludeElementId) row.Add(el.Id.ToString());
                row.Add(el.Category?.Name ?? "");
                if (config.IncludeFamilyType)
                {
                    row.Add(ParameterHelpers.GetFamilyName(el));
                    row.Add(ParameterHelpers.GetFamilySymbolName(el));
                }
                foreach (string paramName in config.Parameters)
                    row.Add(ParameterHelpers.GetString(el, paramName));

                lines.Add(string.Join(",", row.Select(QuoteCsv)));
            }

            string dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllLines(outputPath, lines);
            StingLog.Info($"DataExchange export: {elements.Count} elements, {config.Parameters.Count} params → {outputPath}");

            TaskDialog td = new TaskDialog("STING Data Exchange");
            td.MainInstruction = "Export Complete";
            td.MainContent = $"Exported {elements.Count} elements with {config.Parameters.Count} parameters to:\n{outputPath}";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Open file location");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Close");
            var tdResult = td.Show();
            if (tdResult == TaskDialogResult.CommandLink1)
            {
                try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{outputPath}\"")?.Dispose(); }
                catch (Exception ex) { StingLog.Warn($"Failed to open explorer: {ex.Message}"); }
            }
        }

        private static void ExecuteImport(Document doc, DataExchangeResult config)
        {
            string inputPath = config.FilePath;
            if (string.IsNullOrEmpty(inputPath))
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|Excel files (*.xlsx)|*.xlsx|JSON files (*.json)|*.json|All files (*.*)|*.*"
                };
                if (dlg.ShowDialog() != true) return;
                inputPath = dlg.FileName;
            }

            if (!File.Exists(inputPath))
            {
                TaskDialog.Show("STING Data Exchange", $"File not found: {inputPath}");
                return;
            }

            // Parse CSV
            var lines = File.ReadAllLines(inputPath);
            if (lines.Length < 2)
            {
                TaskDialog.Show("STING Data Exchange", "File is empty or has no data rows.");
                return;
            }

            var headers = StingToolsApp.ParseCsvLine(lines[0]);
            int idCol = -1;
            for (int i = 0; i < headers.Length; i++)
            {
                if (headers[i].Trim().Equals("ElementId", StringComparison.OrdinalIgnoreCase))
                { idCol = i; break; }
            }

            if (idCol < 0)
            {
                TaskDialog.Show("STING Data Exchange", "CSV must contain an 'ElementId' column for import.");
                return;
            }

            int updated = 0;
            int skipped = 0;

            using (var tx = new Transaction(doc, "STING Data Import"))
            {
                tx.Start();
                for (int row = 1; row < lines.Length; row++)
                {
                    try
                    {
                        var cols = StingToolsApp.ParseCsvLine(lines[row]);
                        if (cols.Length <= idCol) continue;

                        if (!long.TryParse(cols[idCol].Trim(), out long eid)) { skipped++; continue; }
                        var el = doc.GetElement(new ElementId(eid));
                        if (el == null) { skipped++; continue; }

                        bool anySet = false;
                        for (int c = 0; c < Math.Min(cols.Length, headers.Length); c++)
                        {
                            if (c == idCol) continue;
                            string hdr = headers[c].Trim();
                            if (hdr == "Category" || hdr == "Family" || hdr == "Type") continue;

                            string val = cols[c].Trim();
                            if (!string.IsNullOrEmpty(val))
                            {
                                ParameterHelpers.SetString(el, hdr, val, overwrite: true);
                                anySet = true;
                            }
                        }
                        if (anySet) updated++;
                        else skipped++;
                    }
                    catch (Exception ex) { StingLog.Warn($"DataImport row {row}: {ex.Message}"); skipped++; }
                }
                tx.Commit();
            }

            ComplianceScan.InvalidateCache();
            StingLog.Info($"DataExchange import: {updated} updated, {skipped} skipped from {inputPath}");
            TaskDialog.Show("STING Data Exchange",
                $"Import complete.\n\nUpdated: {updated} elements\nSkipped: {skipped}");
        }

        private static string QuoteCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }
    }
}
