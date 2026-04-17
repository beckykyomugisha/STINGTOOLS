using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using WpfColor = System.Windows.Media.Color;
using WpfGrid  = System.Windows.Controls.Grid;

namespace StingTools.Tags
{
    // ══════════════════════════════════════════════════════════════════════
    //  CodeLegendCommand — display CODE_LEGEND.json as a searchable dialog
    //  Phase 76 Item 11
    //
    //  Shows all ISO 19650 codes, discipline codes, STING params, CDE states
    //  etc. in a filterable WPF window. Data loaded from CODE_LEGEND.json.
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

            // Phase 98: modeless Show + owner set so it stacks above BCC (when BCC
            // is open) or above Revit's main window otherwise, and doesn't drop
            // behind on focus. ShowDialog would have blocked the BCC message loop.
            var win = new CodeLegendWindow(legend);
            StingTools.UI.StingWindowHelper.ShowOwned(win);
            return Result.Succeeded;
        }
    }

    // ── WPF Window ──────────────────────────────────────────────────────

    internal class CodeLegendWindow : Window
    {
        private readonly JObject _legend;
        private StackPanel _contentPanel;
        private System.Windows.Controls.TextBox _searchBox;

        private static readonly SolidColorBrush NavyBrush   = new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x3A, 0x5F));
        private static readonly SolidColorBrush AmberBrush  = new SolidColorBrush(WpfColor.FromRgb(0xE8, 0xA0, 0x20));
        private static readonly SolidColorBrush CardBrush   = new SolidColorBrush(WpfColor.FromRgb(0xFA, 0xFA, 0xFE));
        private static readonly SolidColorBrush BorderBrush_ = new SolidColorBrush(WpfColor.FromRgb(0xDD, 0xE3, 0xEE));

        public CodeLegendWindow(JObject legend)
        {
            _legend = legend;
            Title   = "STING Code Legend";
            Width   = 760;
            Height  = 680;
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
            hStack.Children.Add(new TextBlock { Text = "ISO 19650 · CDE States · Discipline Codes · STING Parameters · RIBA Stages · MEP Systems",
                FontSize = 10, Foreground = new SolidColorBrush(Colors.LightSteelBlue), Margin = new Thickness(0, 2, 0, 0) });
            header.Child = hStack;
            root.Children.Add(header);

            // ── Search bar ──
            var searchBorder = new Border { Background = new SolidColorBrush(WpfColor.FromRgb(0xF0, 0xF4, 0xFF)), Padding = new Thickness(16, 8, 16, 8), BorderBrush = BorderBrush_, BorderThickness = new Thickness(0, 0, 0, 1) };
            DockPanel.SetDock(searchBorder, Dock.Top);
            var searchRow = new StackPanel { Orientation = Orientation.Horizontal };
            searchRow.Children.Add(new TextBlock { Text = "Search:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), FontSize = 12 });
            _searchBox = new System.Windows.Controls.TextBox { Width = 320, Height = 28, FontSize = 12, Padding = new Thickness(4, 2, 4, 2), VerticalContentAlignment = VerticalAlignment.Center };
            _searchBox.TextChanged += (s, e) => RefreshContent(_searchBox.Text);
            searchRow.Children.Add(_searchBox);
            var clearBtn = new Button { Content = "Clear", Height = 28, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(6, 0, 0, 0), FontSize = 11, Cursor = System.Windows.Input.Cursors.Hand };
            clearBtn.Click += (s, e) => { _searchBox.Text = ""; _searchBox.Focus(); };
            searchRow.Children.Add(clearBtn);
            searchBorder.Child = searchRow;
            root.Children.Add(searchBorder);

            // ── Scrollable content ──
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };
            _contentPanel = new StackPanel();
            scroll.Content = _contentPanel;
            root.Children.Add(scroll);

            Content = root;
            RefreshContent("");
        }

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
                return;
            }

            var sections = _legend["sections"] as JArray;
            if (sections == null) return;

            bool hasFilter = !string.IsNullOrWhiteSpace(filter);
            string q = filter?.ToLowerInvariant() ?? "";

            foreach (var sec in sections)
            {
                string sectionName = sec["section"]?.ToString() ?? "";
                var entries = sec["entries"] as JArray;
                if (entries == null) continue;

                // Collect matching entries
                var matchEntries = new List<JToken>();
                foreach (var entry in entries)
                {
                    if (!hasFilter)
                    {
                        matchEntries.Add(entry);
                    }
                    else
                    {
                        string code  = entry["code"]?.ToString() ?? "";
                        string label = entry["label"]?.ToString() ?? "";
                        string desc  = entry["description"]?.ToString() ?? "";
                        if (code.ToLowerInvariant().Contains(q) ||
                            label.ToLowerInvariant().Contains(q) ||
                            desc.ToLowerInvariant().Contains(q) ||
                            sectionName.ToLowerInvariant().Contains(q))
                        {
                            matchEntries.Add(entry);
                        }
                    }
                }

                if (matchEntries.Count == 0) continue;

                // Section header
                var secHeader = new Border
                {
                    Background = NavyBrush,
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 8, 0, 0),
                    CornerRadius = new CornerRadius(4)
                };
                secHeader.Child = new TextBlock
                {
                    Text = sectionName.ToUpper(),
                    FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.White
                };
                _contentPanel.Children.Add(secHeader);

                // Entry rows
                bool alt = false;
                foreach (var entry in matchEntries)
                {
                    string code  = entry["code"]?.ToString() ?? "";
                    string label = entry["label"]?.ToString() ?? "";
                    string desc  = entry["description"]?.ToString() ?? "";

                    var row = new Border
                    {
                        Background = alt ? new SolidColorBrush(WpfColor.FromRgb(0xF5, 0xF8, 0xFF)) : Brushes.White,
                        BorderBrush = BorderBrush_,
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        Padding = new Thickness(10, 6, 10, 6)
                    };
                    var grid = new WpfGrid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var codeBlock = new TextBlock
                    {
                        Text = code, FontSize = 12, FontWeight = FontWeights.Bold,
                        Foreground = AmberBrush, VerticalAlignment = VerticalAlignment.Center
                    };
                    var labelBlock = new TextBlock
                    {
                        Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var descBlock = new TextBlock
                    {
                        Text = desc, FontSize = 11, Foreground = new SolidColorBrush(Colors.DimGray),
                        TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
                    };

                    WpfGrid.SetColumn(labelBlock, 1);
                    WpfGrid.SetColumn(descBlock, 2);
                    grid.Children.Add(codeBlock);
                    grid.Children.Add(labelBlock);
                    grid.Children.Add(descBlock);
                    row.Child = grid;
                    _contentPanel.Children.Add(row);
                    alt = !alt;
                }
            }

            if (_contentPanel.Children.Count == 0)
            {
                _contentPanel.Children.Add(new TextBlock
                {
                    Text = $"No codes matching \"{filter}\".",
                    FontSize = 12, Foreground = Brushes.Gray, Margin = new Thickness(0, 16, 0, 0)
                });
            }
        }
    }
}
