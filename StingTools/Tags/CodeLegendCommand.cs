using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using WpfColor = System.Windows.Media.Color;

namespace StingTools.Tags
{
    // ══════════════════════════════════════════════════════════════════════
    //  CodeLegendCommand — display CODE_LEGEND.json as a searchable dialog
    //  Phase 76 Item 11   |   Phase 99 rewrite: resizable DataGrid + copy
    //
    //  Shows every ISO 19650 code, discipline code, STING parameter, CDE
    //  state, RIBA stage, MEP system etc. in a filterable, resizable WPF
    //  window. Data loaded from CODE_LEGEND.json.
    //
    //  Phase 99 improvements:
    //    - Replaced fixed-width 100 / 200 / * Grid columns (which clipped
    //      every "Description" cell) with a DataGrid per section. Column
    //      widths are user-drag-resizable and text wraps in the cells.
    //    - Right-click context menu: Copy Code, Copy Row, Copy All Visible,
    //      Copy Section CSV.
    //    - Ctrl+C while a row is focused copies the row.
    //    - Sections collapsible via an Expander header.
    // ══════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CodeLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            string dataPath = StingToolsApp.DataPath;
            string legendPath = Path.Combine(dataPath, "CODE_LEGEND.json");

            JObject legend = null;
            if (File.Exists(legendPath))
            {
                try { legend = JObject.Parse(File.ReadAllText(legendPath)); }
                catch (Exception ex) { StingLog.Warn($"CodeLegend: failed to parse {legendPath}: {ex.Message}"); }
            }

            // Phase 98/99: modeless Show + BCC-aware owner so the legend stacks
            // above BCC / Revit and doesn't drop behind on focus.
            var win = new CodeLegendWindow(legend);
            StingTools.UI.StingWindowHelper.ShowOwned(win);
            return Result.Succeeded;
        }
    }

    // ── Data model for DataGrid binding ─────────────────────────────────

    internal class LegendRow
    {
        public string Code        { get; set; }
        public string Label       { get; set; }
        public string Description { get; set; }
        public string Section     { get; set; }
    }

    // ── WPF Window ──────────────────────────────────────────────────────

    internal class CodeLegendWindow : Window
    {
        private readonly JObject _legend;
        private StackPanel _contentPanel;
        private System.Windows.Controls.TextBox _searchBox;
        private TextBlock _statusBlock;

        private static readonly SolidColorBrush NavyBrush    = new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x3A, 0x5F));
        private static readonly SolidColorBrush AmberBrush   = new SolidColorBrush(WpfColor.FromRgb(0xE8, 0xA0, 0x20));
        private static readonly SolidColorBrush CardBrush    = new SolidColorBrush(WpfColor.FromRgb(0xFA, 0xFA, 0xFE));
        private static readonly SolidColorBrush BorderBrush_ = new SolidColorBrush(WpfColor.FromRgb(0xDD, 0xE3, 0xEE));
        private static readonly SolidColorBrush AltRowBrush  = new SolidColorBrush(WpfColor.FromRgb(0xF5, 0xF8, 0xFF));

        public CodeLegendWindow(JObject legend)
        {
            _legend = legend;
            Title   = "STING Code Legend";
            Width   = 960;
            Height  = 760;
            MinWidth  = 600;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Build();
        }

        private void Build()
        {
            var root = new DockPanel { Background = Brushes.White };

            // ── Header ──
            var header = new Border { Background = NavyBrush, Padding = new Thickness(20, 14, 20, 14) };
            DockPanel.SetDock(header, Dock.Top);
            var hStack = new StackPanel();
            hStack.Children.Add(new TextBlock { Text = "CODE LEGEND", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
            hStack.Children.Add(new TextBlock
            {
                Text = "ISO 19650 · CDE States · Discipline Codes · STING Parameters · RIBA Stages · MEP Systems · Revisions · Issue Types",
                FontSize = 10, Foreground = new SolidColorBrush(Colors.LightSteelBlue), Margin = new Thickness(0, 2, 0, 0)
            });
            header.Child = hStack;
            root.Children.Add(header);

            // ── Toolbar: search + copy actions ──
            var toolbar = new Border
            {
                Background = new SolidColorBrush(WpfColor.FromRgb(0xF0, 0xF4, 0xFF)),
                Padding = new Thickness(16, 8, 16, 8),
                BorderBrush = BorderBrush_, BorderThickness = new Thickness(0, 0, 0, 1)
            };
            DockPanel.SetDock(toolbar, Dock.Top);
            var toolRow = new StackPanel { Orientation = Orientation.Horizontal };
            toolRow.Children.Add(new TextBlock { Text = "Search:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), FontSize = 12 });
            _searchBox = new System.Windows.Controls.TextBox { Width = 320, Height = 28, FontSize = 12, Padding = new Thickness(4, 2, 4, 2), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Filter by code, label, description, or section name" };
            _searchBox.TextChanged += (s, e) => RefreshContent(_searchBox.Text);
            toolRow.Children.Add(_searchBox);

            var clearBtn = MakeToolBtn("Clear", "Clear the search filter");
            clearBtn.Click += (s, e) => { _searchBox.Text = ""; _searchBox.Focus(); };
            toolRow.Children.Add(clearBtn);

            var copyAllBtn = MakeToolBtn("Copy All Visible", "Copy every visible row as tab-separated text");
            copyAllBtn.Click += (s, e) => CopyAllVisible();
            toolRow.Children.Add(copyAllBtn);

            var exportBtn = MakeToolBtn("Export CSV", "Save the filtered legend as a CSV file");
            exportBtn.Click += (s, e) => ExportCsv();
            toolRow.Children.Add(exportBtn);

            var collapseBtn = MakeToolBtn("Collapse All", "Collapse every section");
            collapseBtn.Click += (s, e) => ToggleAllExpanders(expand: false);
            toolRow.Children.Add(collapseBtn);

            var expandBtn = MakeToolBtn("Expand All", "Expand every section");
            expandBtn.Click += (s, e) => ToggleAllExpanders(expand: true);
            toolRow.Children.Add(expandBtn);

            toolbar.Child = toolRow;
            root.Children.Add(toolbar);

            // ── Status bar ──
            var statusBorder = new Border { Background = new SolidColorBrush(WpfColor.FromRgb(0xEC, 0xEC, 0xF3)), Padding = new Thickness(12, 4, 12, 4), BorderBrush = BorderBrush_, BorderThickness = new Thickness(0, 0, 0, 1) };
            DockPanel.SetDock(statusBorder, Dock.Bottom);
            _statusBlock = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Colors.DimGray) };
            statusBorder.Child = _statusBlock;
            root.Children.Add(statusBorder);

            // ── Scrollable content ──
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };
            _contentPanel = new StackPanel();
            scroll.Content = _contentPanel;
            root.Children.Add(scroll);

            Content = root;
            RefreshContent("");
        }

        private Button MakeToolBtn(string text, string tooltip)
        {
            return new Button
            {
                Content = text,
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(6, 0, 0, 0),
                FontSize = 11,
                Cursor = Cursors.Hand,
                ToolTip = tooltip
            };
        }

        private void ToggleAllExpanders(bool expand)
        {
            foreach (var child in _contentPanel.Children)
            {
                if (child is Expander ex) ex.IsExpanded = expand;
            }
        }

        // ── CONTENT REFRESH — builds per-section DataGrids ─────────────

        private void RefreshContent(string filter)
        {
            _contentPanel.Children.Clear();

            if (_legend == null)
            {
                _contentPanel.Children.Add(new TextBlock
                {
                    Text = "CODE_LEGEND.json not found.\nPlace the file in the STING data directory.",
                    FontSize = 12, Foreground = Brushes.Gray, Margin = new Thickness(0, 16, 0, 0)
                });
                _statusBlock.Text = "Legend file missing.";
                return;
            }

            var sections = _legend["sections"] as JArray;
            if (sections == null) { _statusBlock.Text = "Legend has no sections."; return; }

            bool hasFilter = !string.IsNullOrWhiteSpace(filter);
            string q = filter?.ToLowerInvariant() ?? "";

            int totalRows = 0;
            int visibleRows = 0;

            foreach (var sec in sections)
            {
                string sectionName = sec["section"]?.ToString() ?? "";
                var entries = sec["entries"] as JArray;
                if (entries == null) continue;

                var matchRows = new List<LegendRow>();
                foreach (var entry in entries)
                {
                    string code  = entry["code"]?.ToString() ?? "";
                    string label = entry["label"]?.ToString() ?? "";
                    string desc  = entry["description"]?.ToString() ?? "";
                    totalRows++;

                    if (!hasFilter ||
                        code.ToLowerInvariant().Contains(q) ||
                        label.ToLowerInvariant().Contains(q) ||
                        desc.ToLowerInvariant().Contains(q) ||
                        sectionName.ToLowerInvariant().Contains(q))
                    {
                        matchRows.Add(new LegendRow { Code = code, Label = label, Description = desc, Section = sectionName });
                        visibleRows++;
                    }
                }

                if (matchRows.Count == 0) continue;

                // Section expander hosts the DataGrid
                var expander = new Expander
                {
                    IsExpanded = true,
                    Margin = new Thickness(0, 6, 0, 0),
                    BorderBrush = BorderBrush_,
                    BorderThickness = new Thickness(1),
                    Background = CardBrush
                };
                var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
                headerPanel.Children.Add(new TextBlock
                {
                    Text = sectionName.ToUpper(),
                    FontSize = 12, FontWeight = FontWeights.Bold, Foreground = NavyBrush,
                    VerticalAlignment = VerticalAlignment.Center
                });
                headerPanel.Children.Add(new TextBlock
                {
                    Text = $"   ({matchRows.Count} " + (matchRows.Count == 1 ? "entry" : "entries") + ")",
                    FontSize = 10, Foreground = new SolidColorBrush(Colors.DimGray),
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0)
                });
                expander.Header = headerPanel;

                var dg = BuildSectionGrid(matchRows);
                expander.Content = dg;
                _contentPanel.Children.Add(expander);
            }

            if (_contentPanel.Children.Count == 0)
            {
                _contentPanel.Children.Add(new TextBlock
                {
                    Text = $"No codes matching \"{filter}\".",
                    FontSize = 12, Foreground = Brushes.Gray, Margin = new Thickness(0, 16, 0, 0)
                });
            }

            _statusBlock.Text = hasFilter
                ? $"{visibleRows} of {totalRows} entries match \"{filter}\"   |   right-click any row to copy"
                : $"{totalRows} entries across {sections.Count} sections   |   right-click any row to copy, drag column borders to resize";
        }

        // ── Per-section DataGrid — resizable, wraps, copy-enabled ──────

        private DataGrid BuildSectionGrid(List<LegendRow> rows)
        {
            var dg = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                CanUserResizeColumns = true,
                CanUserResizeRows = false,
                CanUserSortColumns = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                AlternatingRowBackground = AltRowBrush,
                RowHeaderWidth = 0,
                FontSize = 11,
                BorderBrush = BorderBrush_,
                BorderThickness = new Thickness(0),
                HorizontalGridLinesBrush = BorderBrush_,
                Background = Brushes.White
            };

            // Build a cell template that wraps text — so long descriptions expand
            // the row height instead of being clipped like the old fixed-Grid layout.
            DataGridTemplateColumn MakeWrapColumn(string header, string path, double minW, double preferredW, SolidColorBrush fg, FontWeight fw)
            {
                var col = new DataGridTemplateColumn
                {
                    Header = header,
                    MinWidth = minW,
                    Width = new DataGridLength(preferredW, DataGridLengthUnitType.Pixel),
                    CanUserSort = true,
                    SortMemberPath = path
                };
                var tpl = new DataTemplate();
                var tb = new FrameworkElementFactory(typeof(TextBlock));
                tb.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(path));
                tb.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
                tb.SetValue(TextBlock.MarginProperty, new Thickness(8, 4, 8, 4));
                tb.SetValue(TextBlock.ForegroundProperty, fg);
                tb.SetValue(TextBlock.FontWeightProperty, fw);
                tpl.VisualTree = tb;
                col.CellTemplate = tpl;
                return col;
            }

            dg.Columns.Add(MakeWrapColumn("Code",        "Code",        70,  110, AmberBrush, FontWeights.Bold));
            dg.Columns.Add(MakeWrapColumn("Label",       "Label",       140, 240, new SolidColorBrush(WpfColor.FromRgb(0x1A, 0x23, 0x7E)), FontWeights.SemiBold));
            dg.Columns.Add(MakeWrapColumn("Description", "Description", 200, 500, new SolidColorBrush(WpfColor.FromRgb(0x42, 0x42, 0x42)), FontWeights.Normal));

            dg.ItemsSource = rows;

            // Context menu: Copy Code, Copy Row, Copy Section, Copy All Visible.
            // Phase 99 fix: both ContextMenu and MenuItem exist in WPF
            // (System.Windows.Controls) AND in Autodesk.Revit.UI, and both
            // namespaces are imported at the top of this file. Fully qualify
            // the WPF types to avoid CS0104 ambiguity.
            var ctx = new System.Windows.Controls.ContextMenu();
            var miCode = new System.Windows.Controls.MenuItem { Header = "Copy _Code (Ctrl+C)" };
            miCode.Click += (s, e) => CopyField(dg, r => r.Code);
            var miRow = new System.Windows.Controls.MenuItem { Header = "Copy Row (_tab-separated)" };
            miRow.Click += (s, e) => CopyField(dg, r => $"{r.Code}\t{r.Label}\t{r.Description}");
            var miLabel = new System.Windows.Controls.MenuItem { Header = "Copy _Label" };
            miLabel.Click += (s, e) => CopyField(dg, r => r.Label);
            var miDesc = new System.Windows.Controls.MenuItem { Header = "Copy _Description" };
            miDesc.Click += (s, e) => CopyField(dg, r => r.Description);
            var miSection = new System.Windows.Controls.MenuItem { Header = "Copy _Section (CSV)" };
            miSection.Click += (s, e) => CopySectionCsv(rows);
            var miAll = new System.Windows.Controls.MenuItem { Header = "Copy _All Visible (CSV)" };
            miAll.Click += (s, e) => CopyAllVisible();
            ctx.Items.Add(miCode);
            ctx.Items.Add(miLabel);
            ctx.Items.Add(miDesc);
            ctx.Items.Add(miRow);
            ctx.Items.Add(new Separator());
            ctx.Items.Add(miSection);
            ctx.Items.Add(miAll);
            dg.ContextMenu = ctx;

            // Ctrl+C copies the selected rows (row-tab form)
            dg.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    CopyField(dg, r => $"{r.Code}\t{r.Label}\t{r.Description}");
                    e.Handled = true;
                }
            };

            return dg;
        }

        // ── COPY HELPERS ───────────────────────────────────────────────

        private void CopyField(DataGrid dg, Func<LegendRow, string> selector)
        {
            try
            {
                var rows = dg.SelectedItems.OfType<LegendRow>().ToList();
                if (rows.Count == 0) return;
                var sb = new StringBuilder();
                foreach (var r in rows) sb.AppendLine(selector(r));
                Clipboard.SetText(sb.ToString().TrimEnd());
                _statusBlock.Text = $"Copied {rows.Count} row(s) to clipboard.";
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CodeLegend.CopyField: {ex.Message}");
            }
        }

        private void CopySectionCsv(List<LegendRow> rows)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Code,Label,Description,Section");
                foreach (var r in rows)
                    sb.AppendLine($"{CsvEscape(r.Code)},{CsvEscape(r.Label)},{CsvEscape(r.Description)},{CsvEscape(r.Section)}");
                Clipboard.SetText(sb.ToString().TrimEnd());
                _statusBlock.Text = $"Copied {rows.Count} entries as CSV to clipboard.";
            }
            catch (Exception ex) { StingLog.Warn($"CodeLegend.CopySectionCsv: {ex.Message}"); }
        }

        private void CopyAllVisible()
        {
            try
            {
                var allGrids = _contentPanel.Children.OfType<Expander>()
                    .Select(ex => ex.Content as DataGrid)
                    .Where(g => g != null)
                    .ToList();
                var sb = new StringBuilder();
                sb.AppendLine("Code,Label,Description,Section");
                int count = 0;
                foreach (var g in allGrids)
                {
                    foreach (var r in g.ItemsSource.OfType<LegendRow>())
                    {
                        sb.AppendLine($"{CsvEscape(r.Code)},{CsvEscape(r.Label)},{CsvEscape(r.Description)},{CsvEscape(r.Section)}");
                        count++;
                    }
                }
                Clipboard.SetText(sb.ToString().TrimEnd());
                _statusBlock.Text = $"Copied {count} visible entries as CSV to clipboard.";
            }
            catch (Exception ex) { StingLog.Warn($"CodeLegend.CopyAllVisible: {ex.Message}"); }
        }

        private void ExportCsv()
        {
            try
            {
                var allGrids = _contentPanel.Children.OfType<Expander>()
                    .Select(ex => ex.Content as DataGrid)
                    .Where(g => g != null)
                    .ToList();
                if (allGrids.Count == 0) return;

                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV Files|*.csv",
                    FileName = $"STING_CodeLegend_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };
                if (sfd.ShowDialog() != true) return;

                var sb = new StringBuilder();
                sb.AppendLine("Code,Label,Description,Section");
                int count = 0;
                foreach (var g in allGrids)
                {
                    foreach (var r in g.ItemsSource.OfType<LegendRow>())
                    {
                        sb.AppendLine($"{CsvEscape(r.Code)},{CsvEscape(r.Label)},{CsvEscape(r.Description)},{CsvEscape(r.Section)}");
                        count++;
                    }
                }
                File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                _statusBlock.Text = $"Exported {count} entries to {Path.GetFileName(sfd.FileName)}.";
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CodeLegend.ExportCsv: {ex.Message}");
                MessageBox.Show(this, $"Export failed: {ex.Message}", "STING", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string CsvEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
