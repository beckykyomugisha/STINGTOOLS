// ══════════════════════════════════════════════════════════════════════════
//  BOQCostManagerPanel.cs — Phase 5 of the BOQ & Cost Manager.
//  WPF UserControl hosted inside the BIM Coordination Center 4D/5D tab.
//  No XAML file — layout built in C# following the StingResultPanel pattern.
//  Revit API access is routed via the StingBOQActionHandler ExternalEvent so
//  button clicks on the WPF thread never touch the Revit DB directly.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.BOQ;
using StingTools.Core;
// Disambiguate WPF vs Revit short names (per-file aliases — narrower than fully-qualified paths)
using Color = System.Windows.Media.Color;
using Grid = System.Windows.Controls.Grid;
using Binding = System.Windows.Data.Binding;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace StingTools.UI
{
    /// <summary>
    /// Budget / snapshot / BOQ editor surface. Host responsibility: set Doc
    /// once and call RefreshAsync() whenever a command that could mutate
    /// costs completes (dispatched actions already refresh automatically).
    /// </summary>
    internal class BOQCostManagerPanel : UserControl
    {
        public Document Doc { get; set; }

        // View-model state
        private BOQDocument _boq;
        private BOQHealthScore _health;
        private string _displayCurrency = "UGX";   // "UGX" | "USD" — toggled in header
        private string _searchText = "";
        private readonly HashSet<string> _activeDisciplines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // WPF controls we need to mutate
        private TextBlock _projectName, _budgetValue, _modeledValue, _provisionalValue,
                         _varianceValue, _coverageValue, _grandTotalValue, _healthValue,
                         _matchHint, _snapshotDiff, _paragraphCoverage;
        private ProgressBar _budgetBar;
        private System.Windows.Controls.ComboBox _snapshotPicker;
        private System.Windows.Controls.TextBox _searchBox;
        private StackPanel _sectionsPanel;
        private ToggleButton _ugxToggle, _usdToggle;

        public BOQCostManagerPanel(Document doc)
        {
            Doc = doc;
            Build();
            RefreshAsync();
        }

        // ── Theming ────────────────────────────────────────────────────────

        private static readonly Brush NavyBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x23, 0x7E));
        private static readonly Brush OrangeBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0x91, 0x2D));
        private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
        private static readonly Brush AmberBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0x8A, 0x00));
        private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
        private static readonly Brush PanelBg = new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xFA));
        private static readonly Brush HeaderFg = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly Brush BorderColor = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB));
        private static readonly Brush ModelRowBg = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly Brush ManualRowBg = new SolidColorBrush(Color.FromRgb(0xFF, 0xFB, 0xE6));
        private static readonly Brush PSRowBg = new SolidColorBrush(Color.FromRgb(0xF3, 0xF0, 0xFC));

        static BOQCostManagerPanel()
        {
            NavyBrush.Freeze(); OrangeBrush.Freeze(); GreenBrush.Freeze();
            AmberBrush.Freeze(); RedBrush.Freeze(); PanelBg.Freeze();
            HeaderFg.Freeze(); BorderColor.Freeze();
            ModelRowBg.Freeze(); ManualRowBg.Freeze(); PSRowBg.Freeze();
        }

        // ══════════════════════════════════════════════════════════════════
        //  Layout construction
        //  DockPanel root with 4 top-docked rows (header strip, budget strip,
        //  snapshot row, toolbar) and the sections ScrollViewer filling the
        //  remaining space. Footer docks at the bottom for global actions.
        // ══════════════════════════════════════════════════════════════════

        private void Build()
        {
            Background = PanelBg;
            var root = new DockPanel { LastChildFill = true };

            root.Children.Add(BuildHeaderStrip());
            DockPanel.SetDock(root.Children[0], Dock.Top);
            root.Children.Add(BuildBudgetStrip());
            DockPanel.SetDock(root.Children[1], Dock.Top);
            root.Children.Add(BuildSnapshotRow());
            DockPanel.SetDock(root.Children[2], Dock.Top);
            root.Children.Add(BuildToolbar());
            DockPanel.SetDock(root.Children[3], Dock.Top);
            root.Children.Add(BuildFooter());
            DockPanel.SetDock(root.Children[4], Dock.Bottom);

            // Main content — BOQ sections
            _sectionsPanel = new StackPanel { Margin = new Thickness(12) };
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _sectionsPanel
            };
            root.Children.Add(scroll);

            Content = root;
        }

        private UIElement BuildHeaderStrip()
        {
            var grid = new Grid { Background = NavyBrush };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Margin = new Thickness(14, 10, 0, 10) };
            _projectName = new TextBlock
            {
                Text = "BOQ & Cost Manager", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = HeaderFg
            };
            var subtitle = new TextBlock
            {
                Text = "ISO 19650-3:2020 compliant bill of quantities, NRM2 descriptions, cost snapshots",
                FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xC0, 0xE0))
            };
            left.Children.Add(_projectName);
            left.Children.Add(subtitle);
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            // Currency toggle
            var ccyPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center };
            _ugxToggle = new ToggleButton { Content = "UGX", IsChecked = true, Width = 52, Height = 26, Margin = new Thickness(0, 0, 4, 0) };
            _usdToggle = new ToggleButton { Content = "USD", IsChecked = false, Width = 52, Height = 26 };
            _ugxToggle.Checked += (s, e) => { _usdToggle.IsChecked = false; _displayCurrency = "UGX"; RefreshDisplay(); };
            _usdToggle.Checked += (s, e) => { _ugxToggle.IsChecked = false; _displayCurrency = "USD"; RefreshDisplay(); };
            ccyPanel.Children.Add(_ugxToggle);
            ccyPanel.Children.Add(_usdToggle);
            Grid.SetColumn(ccyPanel, 1);
            grid.Children.Add(ccyPanel);

            // Header actions
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(BuildHeaderBtn("↻ Refresh", () => DispatchAction("BOQRefresh")));
            actions.Children.Add(BuildHeaderBtn("Set Budget", () => ShowBudgetDialog()));
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            return grid;
        }

        private Button BuildHeaderBtn(string text, Action click)
        {
            var b = new Button
            {
                Content = text, Height = 26, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(4, 0, 0, 0),
                Background = OrangeBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0),
                FontSize = 11, Cursor = Cursors.Hand
            };
            b.Click += (s, e) => click();
            return b;
        }

        private UIElement BuildBudgetStrip()
        {
            var border = new Border { Background = Brushes.White, BorderBrush = BorderColor, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 10, 12, 10) };
            var sp = new StackPanel();

            var cards = new UniformGrid { Columns = 6 };
            _budgetValue = MakeMetric(cards, "Project budget", "UGX 0", NavyBrush);
            _modeledValue = MakeMetric(cards, "Modeled cost", "UGX 0", GreenBrush);
            _provisionalValue = MakeMetric(cards, "Provisional / manual", "UGX 0", AmberBrush);
            _varianceValue = MakeMetric(cards, "Variance", "UGX 0", NavyBrush);
            _coverageValue = MakeMetric(cards, "Coverage", "0%", NavyBrush);
            _healthValue = MakeMetric(cards, "BOQ Health", "—", GreenBrush);
            sp.Children.Add(cards);

            _budgetBar = new ProgressBar
            {
                Height = 6, Maximum = 100, Value = 0, Margin = new Thickness(0, 8, 0, 4),
                Foreground = GreenBrush
            };
            sp.Children.Add(_budgetBar);
            _paragraphCoverage = new TextBlock
            {
                Text = "", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 2, 0, 0)
            };
            sp.Children.Add(_paragraphCoverage);
            border.Child = sp;
            return border;
        }

        private TextBlock MakeMetric(System.Windows.Controls.Panel parent, string label, string value, Brush color)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            stack.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), FontSize = 9,
                Foreground = Brushes.Gray, FontWeight = FontWeights.SemiBold });
            var tb = new TextBlock
            {
                Text = value, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = color,
                Margin = new Thickness(0, 3, 0, 0)
            };
            stack.Children.Add(tb);
            parent.Children.Add(stack);
            return tb;
        }

        private UIElement BuildSnapshotRow()
        {
            var border = new Border { Background = Brushes.White, BorderBrush = BorderColor, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 8, 12, 8) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(new TextBlock { Text = "Snapshot:", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });

            _snapshotPicker = new System.Windows.Controls.ComboBox { Width = 340, Margin = new Thickness(8, 0, 8, 0) };
            Grid.SetColumn(_snapshotPicker, 1);
            grid.Children.Add(_snapshotPicker);

            _snapshotDiff = new TextBlock { Text = "", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            Grid.SetColumn(_snapshotDiff, 2);
            grid.Children.Add(_snapshotDiff);

            var saveBtn = BuildActionBtn("Save snapshot ▾", OrangeBrush);
            saveBtn.Click += (s, e) => ShowSaveSnapshotMenu(saveBtn);
            Grid.SetColumn(saveBtn, 3);
            grid.Children.Add(saveBtn);

            var compareBtn = BuildActionBtn("Compare", NavyBrush);
            compareBtn.Click += (s, e) => DispatchAction("BOQSnapshotCompare");
            Grid.SetColumn(compareBtn, 4);
            grid.Children.Add(compareBtn);

            border.Child = grid;
            return border;
        }

        private Button BuildActionBtn(string text, Brush bg)
        {
            return new Button
            {
                Content = text, Height = 28, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(4, 0, 0, 0),
                Background = bg, Foreground = Brushes.White, BorderThickness = new Thickness(0),
                FontSize = 11, Cursor = Cursors.Hand
            };
        }

        private UIElement BuildToolbar()
        {
            var border = new Border { Background = Brushes.White, BorderBrush = BorderColor, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 6, 12, 6) };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };

            _searchBox = new System.Windows.Controls.TextBox { Width = 260, Height = 24, FontSize = 11,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0) };
            _searchBox.TextChanged += (s, e) => { _searchText = _searchBox.Text ?? ""; RebuildSectionsView(); };
            sp.Children.Add(new TextBlock { Text = "Filter:", VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 6, 0) });
            sp.Children.Add(_searchBox);

            string[] discs = { "ALL", "A", "S", "M", "E", "P", "FP", "PS" };
            foreach (var d in discs)
            {
                var tb = new ToggleButton
                {
                    Content = d, Height = 24, MinWidth = 32, FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(2, 0, 0, 0), Cursor = Cursors.Hand,
                    IsChecked = d == "ALL"
                };
                string dd = d;
                tb.Click += (s, e) =>
                {
                    bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
                    if (dd == "ALL") { _activeDisciplines.Clear(); }
                    else
                    {
                        if (!ctrl) _activeDisciplines.Clear();
                        if (tb.IsChecked == true) _activeDisciplines.Add(dd);
                        else _activeDisciplines.Remove(dd);
                    }
                    RebuildSectionsView();
                };
                sp.Children.Add(tb);
            }

            _matchHint = new TextBlock { Text = "", FontSize = 10, Foreground = Brushes.Gray,
                Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(_matchHint);

            border.Child = sp;
            return border;
        }

        private UIElement BuildFooter()
        {
            var border = new Border { Background = Brushes.White, BorderBrush = BorderColor, BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 8, 12, 8) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel();
            left.Children.Add(new TextBlock { Text = "GRAND TOTAL (incl. preliminaries / contingency / overhead)",
                FontSize = 10, Foreground = Brushes.Gray, FontWeight = FontWeights.SemiBold });
            _grandTotalValue = new TextBlock { Text = "UGX 0", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = NavyBrush };
            left.Children.Add(_grandTotalValue);
            grid.Children.Add(left);

            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(BuildActionBtn("＋ Manual row", NavyBrush)); // placeholder — dialog wired below
            right.Children.Add(BuildActionBtn("Reconcile PS", AmberBrush));
            right.Children.Add(BuildActionBtn("Import Excel", NavyBrush));
            right.Children.Add(BuildActionBtn("Export ↗", GreenBrush));

            // Wire buttons after they are in the tree so event handlers can use visual parent
            foreach (UIElement ch in right.Children)
            {
                if (ch is Button b)
                {
                    string caption = b.Content as string ?? "";
                    if (caption.StartsWith("＋")) b.Click += (s, e) => AddManualRow();
                    else if (caption.StartsWith("Reconcile")) b.Click += (s, e) => DispatchAction("ReconcileProvisionals");
                    else if (caption.StartsWith("Import")) b.Click += (s, e) => DispatchAction("BOQImport");
                    else if (caption.StartsWith("Export")) b.Click += (s, e) => DispatchAction("BOQExport");
                }
            }
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            border.Child = grid;
            return border;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Data refresh + section rendering
        //  RefreshAsync rebuilds the BOQDocument via BOQCostManager, then
        //  hands off to RefreshDisplay to update all WPF surfaces. Host code
        //  dispatches command tags via the ExternalEvent handler below; when
        //  the handler completes it calls RefreshAsync() on the UI thread.
        // ══════════════════════════════════════════════════════════════════

        public void RefreshAsync()
        {
            if (Doc == null) return;
            try
            {
                _boq = BOQCostManager.BuildBOQDocument(Doc);
                _health = BOQCostManager.ComputeBOQHealth(_boq);
                LoadSnapshotDropdown();
                RefreshDisplay();
            }
            catch (Exception ex)
            {
                StingLog.Error("BOQCostManagerPanel.RefreshAsync", ex);
                _paragraphCoverage.Text = $"Refresh failed — see log. {ex.Message}";
            }
        }

        private void RefreshDisplay()
        {
            if (_boq == null) return;
            _projectName.Text = $"BOQ & Cost Manager — {_boq.ProjectName}";

            // Currency-aware formatting
            Func<double, string> fmt = v => _displayCurrency == "USD"
                ? "$ " + v.ToString("N2", CultureInfo.InvariantCulture)
                : "UGX " + v.ToString("N0", CultureInfo.InvariantCulture);
            double modelUgx = _boq.ModeledTotalUGX;
            double provUgx = _boq.ProvTotalUGX;
            double grandUgx = _boq.GrandTotalUGX;
            double budgetUgx = _boq.ProjectBudgetUGX;
            double varianceUgx = _boq.BudgetVarianceUGX;
            double toDisplay(double u) => _displayCurrency == "USD" && _boq.ExchangeRateUgxPerUsd > 0
                ? u / _boq.ExchangeRateUgxPerUsd : u;

            _budgetValue.Text = fmt(toDisplay(budgetUgx));
            _modeledValue.Text = fmt(toDisplay(modelUgx));
            _provisionalValue.Text = fmt(toDisplay(provUgx));
            _varianceValue.Text = fmt(toDisplay(varianceUgx));
            _varianceValue.Foreground = varianceUgx >= 0 ? GreenBrush : RedBrush;
            _coverageValue.Text = $"{_boq.BudgetCoveragePct:F1}%";
            _coverageValue.Foreground = _boq.BudgetCoveragePct >= 80 && _boq.BudgetCoveragePct <= 110 ? GreenBrush
                : _boq.BudgetCoveragePct >= 60 && _boq.BudgetCoveragePct <= 130 ? AmberBrush : RedBrush;
            _grandTotalValue.Text = fmt(toDisplay(grandUgx));

            _budgetBar.Value = budgetUgx > 0 ? Math.Min(100, _boq.SubtotalUGX / budgetUgx * 100.0) : 0;
            _budgetBar.Foreground = _budgetBar.Value <= 100 ? GreenBrush : RedBrush;

            _healthValue.Text = _health != null ? $"{_health.OverallScore:F0} / 100" : "—";
            _healthValue.Foreground = _health == null ? Brushes.Gray
                : _health.OverallScore >= 85 ? GreenBrush
                : _health.OverallScore >= 50 ? AmberBrush : RedBrush;

            _paragraphCoverage.Text = $"Paragraph coverage {_boq.ParagraphCoveragePct:F0}% ({_boq.ResolvedParagraphCount}/{_boq.AllItems.Count}) "
                + $"| Avg rate confidence {_boq.AverageRateConfidence:F0} "
                + $"| Embodied carbon {_boq.TotalCarbonKg / 1000.0:F2} tCO₂e";

            RebuildSectionsView();
        }

        private void RebuildSectionsView()
        {
            if (_sectionsPanel == null || _boq == null) return;
            _sectionsPanel.Children.Clear();
            int totalSections = _boq.Sections.Count;
            int visibleSections = 0, visibleItems = 0;
            foreach (var sec in _boq.Sections)
            {
                if (_activeDisciplines.Count > 0 && !_activeDisciplines.Contains(sec.Discipline ?? "")) continue;
                var filtered = sec.Items.Where(MatchesFilter).ToList();
                if (filtered.Count == 0) continue;
                _sectionsPanel.Children.Add(BuildSectionCard(sec, filtered));
                visibleSections++;
                visibleItems += filtered.Count;
            }
            _matchHint.Text = string.IsNullOrEmpty(_searchText) && _activeDisciplines.Count == 0
                ? $"{_boq.Sections.Count} sections, {_boq.AllItems.Count} items"
                : $"Showing {visibleSections} of {totalSections} sections, {visibleItems} items matching filter";
        }

        private bool MatchesFilter(BOQLineItem it)
        {
            if (string.IsNullOrEmpty(_searchText)) return true;
            string q = _searchText.ToLowerInvariant();
            return (it.ItemName ?? "").ToLowerInvariant().Contains(q)
                || (it.FamilyName ?? "").ToLowerInvariant().Contains(q)
                || (it.Unit ?? "").ToLowerInvariant().Contains(q)
                || (it.Note ?? "").ToLowerInvariant().Contains(q)
                || (it.Category ?? "").ToLowerInvariant().Contains(q)
                || (it.Level ?? "").ToLowerInvariant().Contains(q)
                || (it.Location ?? "").ToLowerInvariant().Contains(q)
                || (it.NRM2Section ?? "").ToLowerInvariant().Contains(q)
                || (it.BOQLineRef ?? "").ToLowerInvariant().Contains(q)
                || it.RateUGX.ToString("F0", CultureInfo.InvariantCulture).Contains(q)
                || it.TotalUGX.ToString("F0", CultureInfo.InvariantCulture).Contains(q);
        }

        private Expander BuildSectionCard(BOQSection sec, List<BOQLineItem> items)
        {
            double totalShown = items.Sum(i => i.TotalUGX);
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new Border
            {
                Background = NavyBrush, CornerRadius = new CornerRadius(2), Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 0, 8, 0),
                Child = new TextBlock { Text = $"§{sec.NRM2Section}", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold }
            });
            header.Children.Add(new TextBlock { Text = sec.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            header.Children.Add(new TextBlock { Text = $"  · {sec.Discipline}", FontSize = 11, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
            header.Children.Add(new TextBlock { Text = $"  · {items.Count} items", FontSize = 11, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
            header.Children.Add(new TextBlock { Text = $"  · UGX {totalShown:N0}", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = OrangeBrush,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });

            var expander = new Expander
            {
                Header = header, IsExpanded = true,
                Margin = new Thickness(0, 0, 0, 8),
                Background = Brushes.White,
                BorderBrush = BorderColor, BorderThickness = new Thickness(1)
            };
            expander.Content = BuildSectionGrid(items);
            return expander;
        }

        private DataGrid BuildSectionGrid(List<BOQLineItem> items)
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
                CanUserReorderColumns = false, CanUserSortColumns = true, HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, FontSize = 11,
                HorizontalGridLinesBrush = BorderColor, RowHeaderWidth = 0,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFB)),
                ItemsSource = items.Select(i => new BOQItemViewModel(i, _displayCurrency, _boq.ExchangeRateUgxPerUsd)).ToList(),
                MinHeight = 80
            };
            grid.Columns.Add(MakeTextCol("Ref", nameof(BOQItemViewModel.LineRef), 70));
            grid.Columns.Add(MakeTextCol("Item", nameof(BOQItemViewModel.ItemName), 220));
            grid.Columns.Add(MakeTextCol("Qty", nameof(BOQItemViewModel.QuantityDisplay), 70));
            grid.Columns.Add(MakeTextCol("Unit", nameof(BOQItemViewModel.Unit), 55));
            grid.Columns.Add(MakeTextCol("Rate", nameof(BOQItemViewModel.RateDisplay), 100));
            grid.Columns.Add(MakeTextCol("Total", nameof(BOQItemViewModel.TotalDisplay), 110));
            grid.Columns.Add(MakeTextCol("Source", nameof(BOQItemViewModel.SourceLabel), 80));
            grid.Columns.Add(MakeTextCol("Conf.", nameof(BOQItemViewModel.RateConfidenceDisplay), 55));
            grid.Columns.Add(MakeTextCol("Carbon", nameof(BOQItemViewModel.CarbonDisplay), 85));
            grid.Columns.Add(MakeTextCol("Note", nameof(BOQItemViewModel.Note), 220));
            grid.MouseDoubleClick += (s, e) =>
            {
                if (grid.SelectedItem is BOQItemViewModel vm && vm.RevitElementId > 0)
                {
                    StingCommandHandler.SetExtraParam("SelectElementId", vm.RevitElementId.ToString());
                    DispatchAction("SelectInRevit");
                }
            };
            return grid;
        }

        private DataGridTextColumn MakeTextCol(string header, string bindingPath, double width)
        {
            var col = new DataGridTextColumn
            {
                Header = header, Binding = new Binding(bindingPath), Width = new DataGridLength(width),
                IsReadOnly = true
            };
            return col;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Snapshots — dropdown population + save menu
        // ══════════════════════════════════════════════════════════════════

        private List<BOQSnapshotMeta> _snapshotMetas = new List<BOQSnapshotMeta>();

        private void LoadSnapshotDropdown()
        {
            if (_snapshotPicker == null) return;
            _snapshotMetas = BOQCostManager.ListSnapshots(Doc);
            _snapshotPicker.Items.Clear();
            _snapshotPicker.Items.Add("— Live (unsaved) —");
            foreach (var m in _snapshotMetas) _snapshotPicker.Items.Add(m.DisplayText);
            _snapshotPicker.SelectedIndex = 0;
            _snapshotPicker.SelectionChanged -= OnSnapshotPicked;
            _snapshotPicker.SelectionChanged += OnSnapshotPicked;
        }

        private void OnSnapshotPicked(object sender, SelectionChangedEventArgs e)
        {
            if (_snapshotPicker.SelectedIndex <= 0)
            {
                _snapshotDiff.Text = "";
                return;
            }
            var meta = _snapshotMetas[_snapshotPicker.SelectedIndex - 1];
            double delta = _boq.GrandTotalUGX - meta.GrandTotalUGX;
            string sign = delta >= 0 ? "+" : "";
            _snapshotDiff.Text = $"{sign}UGX {delta:N0} vs {meta.Label}";
            _snapshotDiff.Foreground = delta >= 0 ? GreenBrush : RedBrush;
        }

        private void ShowSaveSnapshotMenu(Button anchor)
        {
            var menu = new ContextMenu();
            foreach (var type in new[] { "DD", "Stage", "Weekly", "Handover", "Manual" })
            {
                string t = type;
                var mi = new MenuItem { Header = $"Save as {t} snapshot…" };
                mi.Click += (s, e) =>
                {
                    string label = PromptString($"Snapshot label for {t}:", $"{t} — {DateTime.Now:yyyy-MM-dd}");
                    if (string.IsNullOrWhiteSpace(label)) return;
                    StingCommandHandler.SetExtraParam("SnapshotLabel", label);
                    StingCommandHandler.SetExtraParam("SnapshotType", t);
                    DispatchAction("BOQSnapshotSave");
                };
                menu.Items.Add(mi);
            }
            menu.PlacementTarget = anchor;
            menu.IsOpen = true;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Modal dialogs (budget, add-row, string prompt)
        // ══════════════════════════════════════════════════════════════════

        private void ShowBudgetDialog()
        {
            string current = _boq?.ProjectBudgetUGX.ToString("F0", CultureInfo.InvariantCulture) ?? "0";
            string input = PromptString("Project budget (UGX):", current);
            if (string.IsNullOrWhiteSpace(input)) return;
            if (!double.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out double budget))
            {
                MessageBox.Show("Enter a numeric budget in UGX.", "BOQ"); return;
            }
            StingCommandHandler.SetExtraParam("ProjectBudgetUgx", budget.ToString("F0", CultureInfo.InvariantCulture));
            DispatchAction("BOQSetBudget");
        }

        private void AddManualRow()
        {
            string name = PromptString("Item name:", "New manual item");
            if (string.IsNullOrWhiteSpace(name)) return;
            string qtyStr = PromptString("Quantity:", "1");
            string unit = PromptString("Unit (m², m³, m, each, item, kg):", "each");
            string rateStr = PromptString("Unit rate (UGX):", "0");
            string section = PromptString("NRM2 section number:", "22");
            string disc = PromptString("Discipline code (A/S/M/E/P/FP/PS):", "A");

            if (!double.TryParse(qtyStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double qty)) qty = 1;
            if (!double.TryParse(rateStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double rate)) rate = 0;

            StingCommandHandler.SetExtraParam("ManualRowName", name);
            StingCommandHandler.SetExtraParam("ManualRowQty", qty.ToString("F3", CultureInfo.InvariantCulture));
            StingCommandHandler.SetExtraParam("ManualRowUnit", unit ?? "each");
            StingCommandHandler.SetExtraParam("ManualRowRate", rate.ToString("F0", CultureInfo.InvariantCulture));
            StingCommandHandler.SetExtraParam("ManualRowSection", section ?? "22");
            StingCommandHandler.SetExtraParam("ManualRowDisc", disc ?? "A");
            DispatchAction("BOQAddManualRow");
        }

        private static string PromptString(string prompt, string defaultValue)
        {
            // Minimal WPF InputDialog substitute — deliberately compact so the
            // panel stays a single file.
            var w = new Window
            {
                Title = "STING — BOQ", Width = 420, Height = 170, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize, Background = Brushes.White
            };
            var sp = new StackPanel { Margin = new Thickness(14) };
            sp.Children.Add(new TextBlock { Text = prompt, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) });
            var tb = new System.Windows.Controls.TextBox { Text = defaultValue ?? "", Height = 26, FontSize = 12 };
            sp.Children.Add(tb);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "OK", Width = 80, Height = 26, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
            var cancel = new Button { Content = "Cancel", Width = 80, Height = 26, IsCancel = true };
            row.Children.Add(ok); row.Children.Add(cancel);
            sp.Children.Add(row);
            w.Content = sp;
            string result = null;
            ok.Click += (s, e) => { result = tb.Text ?? ""; w.Close(); };
            cancel.Click += (s, e) => { result = null; w.Close(); };
            tb.Focus(); tb.SelectAll();
            w.ShowDialog();
            return result;
        }

        // ══════════════════════════════════════════════════════════════════
        //  ExternalEvent dispatch — every Revit API call routes through here
        // ══════════════════════════════════════════════════════════════════

        private void DispatchAction(string tag)
        {
            try
            {
                StingDockPanel.DispatchCommand(tag);
                // Schedule a UI refresh after the Revit event chain settles.
                // 300ms empirically enough for most commands; the refresh is
                // idempotent so spurious calls are harmless.
                System.Threading.Tasks.Task.Delay(300)
                    .ContinueWith(_ => Dispatcher.BeginInvoke(new Action(RefreshAsync)));
            }
            catch (Exception ex) { StingLog.Error($"BOQ DispatchAction({tag})", ex); }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  BOQItemViewModel — read-only view over BOQLineItem with currency-aware
    //  display strings. Editing happens via add-manual-row flow or the
    //  Excel roundtrip — not inline — so we do not raise PropertyChanged.
    // ══════════════════════════════════════════════════════════════════════

    internal class BOQItemViewModel
    {
        private readonly BOQLineItem _item;
        private readonly string _currency;
        private readonly double _ugxPerUsd;

        public BOQItemViewModel(BOQLineItem item, string currency, double ugxPerUsd)
        {
            _item = item;
            _currency = currency ?? "UGX";
            _ugxPerUsd = ugxPerUsd > 0 ? ugxPerUsd : 3700;
        }

        public string LineRef => _item.BOQLineRef ?? "";
        public string ItemName => string.IsNullOrEmpty(_item.FamilyName) ? _item.ItemName
            : $"{_item.ItemName}  ({_item.FamilyName})";
        public string QuantityDisplay => _item.Quantity.ToString("N3", CultureInfo.InvariantCulture);
        public string Unit => _item.Unit ?? "";
        public string RateDisplay => _currency == "USD"
            ? $"$ {_item.RateUSD:N2}"
            : $"UGX {_item.RateUGX:N0}";
        public string TotalDisplay => _currency == "USD"
            ? $"$ {_item.TotalUSD:N2}"
            : $"UGX {_item.TotalUGX:N0}";
        public string SourceLabel => _item.Source switch
        {
            BOQRowSource.Model => "Model",
            BOQRowSource.Manual => "Manual",
            BOQRowSource.ProvisionalSum => "PS",
            _ => ""
        };
        public string RateConfidenceDisplay => _item.RateConfidence.ToString(CultureInfo.InvariantCulture);
        public string CarbonDisplay => _item.EmbodiedCarbonKg > 0
            ? _item.EmbodiedCarbonKg.ToString("N0", CultureInfo.InvariantCulture) + " kg"
            : "";
        public string Note => _item.Note ?? "";
        public long RevitElementId => _item.RevitElementId;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ToggleButton alias — keep the file self-contained without extra using.
    // ══════════════════════════════════════════════════════════════════════

    internal class ToggleButton : System.Windows.Controls.Primitives.ToggleButton { }
}
