// ══════════════════════════════════════════════════════════════════════════
//  BOQCostManagerPanel.cs — Phase 5 of the BOQ & Cost Manager.
//  WPF UserControl hosted inside the BIM Coordination Center 4D/5D tab.
//  No XAML file — layout built in C# following the StingResultPanel pattern.
//
//  Phase 108b (enhancement patch): inline editing, NRM2 row-details panel,
//  discipline-coloured section headers, per-row source colouring, rate
//  confidence colouring, Expand/Collapse all, Materials tab, Carbon card,
//  right-click context menu, per-row snapshot deltas.
//  Revit API access is routed via the StingBOQActionHandler ExternalEvent so
//  button clicks on the WPF thread never touch the Revit DB directly.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
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
using TextBox = System.Windows.Controls.TextBox;   // collides with Autodesk.Revit.UI.TextBox
using Ellipse = System.Windows.Shapes.Ellipse;     // collides with Autodesk.Revit.DB.Ellipse
using Visibility = System.Windows.Visibility;      // collides with Autodesk.Revit.DB.Visibility

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

        // Phase 108b: section state preserved across refreshes + per-section snapshot deltas.
        // Keyed by SectionKey(sec) rather than sec.Id — BOQSection.Id is a fresh
        // Guid.NewGuid on every BuildBOQDocument, so tracking by Id loses the
        // open/closed set after every rate edit. Composite (Discipline|NRM2|Name)
        // is stable across rebuilds.
        private readonly HashSet<string> _openSections = new HashSet<string>();
        private string _activeSnapshotLabel = "";
        private readonly Dictionary<string, double> _snapshotDeltas = new Dictionary<string, double>();

        private static string SectionKey(BOQSection sec)
            => $"{sec?.Discipline}|{sec?.NRM2Section}|{sec?.Name}";

        // Phase 108c: toggle hides the NRM2 description RowDetails strip across
        // every section grid — useful when the QS wants a compact price-only view.
        private bool _showNrm2 = true;
        private ToggleButton _nrm2Toggle;

        // Phase 108d: transient status-bar message support. FlashHint() writes
        // a short amber line into _paragraphCoverage for 4s, then restores the
        // coverage summary. Used when we cancel an edit on a model-derived cell.
        private string _defaultCoverageText = "";
        private int _flashSeq;

        // WPF controls we need to mutate
        private TextBlock _projectName, _budgetValue, _modeledValue, _provisionalValue,
                         _varianceValue, _coverageValue, _grandTotalValue, _healthValue,
                         _carbonValue, _matchHint, _snapshotDiff, _paragraphCoverage;
        private ProgressBar _budgetBar;
        private System.Windows.Controls.ComboBox _snapshotPicker;
        private System.Windows.Controls.TextBox _searchBox;
        private StackPanel _sectionsPanel;
        private TabControl _mainTabs;
        private TabItem _materialsTab;
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

        static BOQCostManagerPanel()
        {
            NavyBrush.Freeze(); OrangeBrush.Freeze(); GreenBrush.Freeze();
            AmberBrush.Freeze(); RedBrush.Freeze(); PanelBg.Freeze();
            HeaderFg.Freeze(); BorderColor.Freeze();
        }

        // ── Discipline palette for section headers ─────────────────────────
        // Background fills: muted pastel so text stays legible on white grids
        internal static SolidColorBrush SectionHeaderBrush(string disc)
        {
            Color c;
            switch (disc)
            {
                case "A":  c = Color.FromRgb(214, 228, 240); break;
                case "S":  c = Color.FromRgb(235, 228, 250); break;
                case "M":  c = Color.FromRgb(255, 243, 224); break;
                case "E":  c = Color.FromRgb(252, 235, 235); break;
                case "P":  c = Color.FromRgb(225, 245, 238); break;
                case "FP": c = Color.FromRgb(252, 232, 235); break;
                case "PS": c = Color.FromRgb(237, 231, 246); break;
                default:   c = Color.FromRgb(240, 240, 240); break;
            }
            var b = new SolidColorBrush(c); b.Freeze(); return b;
        }

        // Badge foreground pill colour (saturated complement of the header fill)
        internal static Color DiscBadgeColour(string disc)
        {
            switch (disc)
            {
                case "A":  return Color.FromRgb(14, 107, 168);
                case "S":  return Color.FromRgb(83, 74, 183);
                case "M":  return Color.FromRgb(186, 117, 23);
                case "E":  return Color.FromRgb(163, 45, 45);
                case "P":  return Color.FromRgb(15, 110, 86);
                case "FP": return Color.FromRgb(163, 45, 45);
                case "PS": return Color.FromRgb(83, 74, 183);
                default:   return Color.FromRgb(80, 80, 80);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Layout construction
        //  DockPanel root with 4 top-docked rows (header strip, budget strip,
        //  snapshot row, toolbar) and the sections TabControl filling the
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

            // Main content — TabControl with Bill of Quantities + Materials tabs
            _sectionsPanel = new StackPanel { Margin = new Thickness(0) };
            var boqScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _sectionsPanel,
                Padding = new Thickness(12)
            };
            _mainTabs = new TabControl { Margin = new Thickness(0), BorderThickness = new Thickness(0), Background = PanelBg };
            _mainTabs.Items.Add(new TabItem { Header = "Bill of Quantities", Content = boqScroll });
            _materialsTab = new TabItem { Header = "Materials",
                Content = new TextBlock { Text = "Loading…", Margin = new Thickness(14), Foreground = Brushes.Gray } };
            _mainTabs.Items.Add(_materialsTab);
            root.Children.Add(_mainTabs);

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
                Text = "ISO 19650-3:2020 compliant bill of quantities, item descriptions, cost snapshots",
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

            // Phase 108b: 7 cards now — carbon added after health
            var cards = new UniformGrid { Columns = 7 };
            _budgetValue      = MakeMetric(cards, "Project budget",       "UGX 0", NavyBrush);
            _modeledValue     = MakeMetric(cards, "Modeled cost",         "UGX 0", GreenBrush);
            _provisionalValue = MakeMetric(cards, "Provisional / manual", "UGX 0", AmberBrush);
            _varianceValue    = MakeMetric(cards, "Variance",             "UGX 0", NavyBrush);
            _coverageValue    = MakeMetric(cards, "Coverage",             "0%",    NavyBrush);
            _healthValue      = MakeMetric(cards, "BOQ Health",           "—",     GreenBrush);
            _carbonValue      = MakeMetric(cards, "Embodied carbon",      "0 kgCO₂e", GreenBrush);
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

            // Expand / collapse all (Part 5A)
            sp.Children.Add(MakeToolbarButton("⊞ Expand all", () =>
            {
                _openSections.Clear();
                if (_boq != null) foreach (var s2 in _boq.Sections) _openSections.Add(SectionKey(s2));
                RebuildSectionsView();
            }));
            sp.Children.Add(MakeToolbarButton("⊟ Collapse all", () =>
            {
                _openSections.Clear();
                RebuildSectionsView();
            }));

            // Phase 108c: toggle hides/shows the NRM2 description strip on
            // every row. Pressed = description visible (default); unpressed
            // = compact price-only view. Uses the same ToggleButton look as
            // the discipline pills so the affordance is familiar.
            _nrm2Toggle = new ToggleButton
            {
                Content = "¶ Description",
                ToolTip = "Show / hide item description under each BOQ row",
                Height = 24, MinWidth = 64, FontSize = 11, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand,
                IsChecked = _showNrm2
            };
            _nrm2Toggle.Checked   += (s, e) => { _showNrm2 = true;  RebuildSectionsView(); };
            _nrm2Toggle.Unchecked += (s, e) => { _showNrm2 = false; RebuildSectionsView(); };
            sp.Children.Add(_nrm2Toggle);

            _matchHint = new TextBlock { Text = "", FontSize = 10, Foreground = Brushes.Gray,
                Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(_matchHint);

            border.Child = sp;
            return border;
        }

        private Button MakeToolbarButton(string text, Action onClick)
        {
            var btn = new Button
            {
                Content = text, FontSize = 10, Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(8, 0, 0, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0.5),
                BorderBrush = new SolidColorBrush(Color.FromRgb(180, 190, 200)),
                Cursor = Cursors.Hand
            };
            btn.Click += (s, e) => onClick();
            return btn;
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
            right.Children.Add(BuildActionBtn("＋ Manual row", NavyBrush));
            right.Children.Add(BuildActionBtn("Reconcile PS", AmberBrush));
            right.Children.Add(BuildActionBtn("Import Excel", NavyBrush));
            right.Children.Add(BuildActionBtn("Export ↗", GreenBrush));

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
                // On first load open every section; subsequent refreshes preserve state
                if (_openSections.Count == 0 && _boq != null)
                    foreach (var s in _boq.Sections) _openSections.Add(SectionKey(s));
                RefreshDisplay();
            }
            catch (Exception ex)
            {
                StingLog.Error("BOQCostManagerPanel.RefreshAsync", ex);
                if (_paragraphCoverage != null)
                    _paragraphCoverage.Text = $"Refresh failed — see log. {ex.Message}";
            }
        }

        /// <summary>
        /// Full UI refresh — updates metrics AND rebuilds every section grid
        /// + the Materials tab. Use this on initial load, the Refresh button,
        /// currency toggle, and snapshot comparison.
        ///
        /// Do NOT call after an inline cell edit: rebuilding the grids tears
        /// down the user's freshly-bound VM and races the asynchronous
        /// parameter write in BOQWriteItemParamsCommand, causing the edit to
        /// "revert" for a frame. Call RefreshMetrics() instead.
        /// </summary>
        private void RefreshDisplay()
        {
            RefreshMetrics();
            if (_boq == null) return;
            RebuildSectionsView();
            RebuildMaterialsTab();
        }

        /// <summary>
        /// Metrics-only refresh — budget strip, totals, coverage strip and
        /// project-name header. Does NOT touch the section grids, so inline
        /// edits made in VMs survive. BOQLineItem totals are derived
        /// properties so recomputing here reads the just-edited state.
        /// </summary>
        private void RefreshMetrics()
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

            double carbonKg = _boq.TotalCarbonKg;
            _carbonValue.Text = carbonKg >= 1000
                ? $"{carbonKg / 1000:F1} tCO₂e"
                : $"{carbonKg:N0} kgCO₂e";
            _carbonValue.Foreground = carbonKg < 300000 ? GreenBrush
                : carbonKg < 800000 ? AmberBrush : RedBrush;

            _defaultCoverageText = $"Description coverage {_boq.ParagraphCoveragePct:F0}% ({_boq.ResolvedParagraphCount}/{_boq.AllItems.Count}) "
                + $"| Avg rate confidence {_boq.AverageRateConfidence:F0} "
                + $"| Embodied carbon {_boq.TotalCarbonKg / 1000.0:F2} tCO₂e";
            _paragraphCoverage.Text = _defaultCoverageText;
            _paragraphCoverage.Foreground = Brushes.Gray;
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
                var vms = filtered.Select(i => new BOQItemViewModel(i, _displayCurrency, _boq.ExchangeRateUgxPerUsd)).ToList();
                _sectionsPanel.Children.Add(BuildSectionCard(sec, vms));
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

        // ══════════════════════════════════════════════════════════════════
        //  BuildSectionCard — discipline-coloured header, NRM2 badge,
        //  per-section snapshot delta pill, item count, total in active currency
        // ══════════════════════════════════════════════════════════════════

        private Expander BuildSectionCard(BOQSection sec, List<BOQItemViewModel> vms)
        {
            string secKey = SectionKey(sec);
            bool isExpanded = _openSections.Contains(secKey);
            double totalShownUgx = vms.Sum(v => v.Underlying.TotalUGX);
            string displayTotal = _displayCurrency == "USD"
                ? $"$ {vms.Sum(v => v.Underlying.TotalUSD):N2}"
                : $"UGX {totalShownUgx:N0}";

            var headerGrid = new Grid { Background = SectionHeaderBrush(sec.Discipline) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var badge = new Border
            {
                Background = new SolidColorBrush(DiscBadgeColour(sec.Discipline)),
                CornerRadius = new CornerRadius(3), Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(6, 2, 8, 2),
                Child = new TextBlock
                {
                    Text = $"§{sec.NRM2Section}",
                    Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold
                }
            };
            Grid.SetColumn(badge, 0); headerGrid.Children.Add(badge);

            var discPillColour = DiscBadgeColour(sec.Discipline);
            discPillColour.A = 170;
            var discBadge = new Border
            {
                Background = new SolidColorBrush(discPillColour),
                CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 2, 8, 2),
                Child = new TextBlock { Text = sec.Discipline, Foreground = Brushes.White, FontSize = 10 }
            };
            Grid.SetColumn(discBadge, 1); headerGrid.Children.Add(discBadge);

            var namePanel = new StackPanel { Orientation = Orientation.Horizontal };
            namePanel.Children.Add(new TextBlock
            {
                Text = sec.Name, FontSize = 13, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            namePanel.Children.Add(new TextBlock
            {
                Text = $"  ·  {vms.Count} items",
                FontSize = 11, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(namePanel, 2); headerGrid.Children.Add(namePanel);

            // Snapshot delta pill — only when an active snapshot is selected
            if (!string.IsNullOrEmpty(_activeSnapshotLabel) && _snapshotDeltas.TryGetValue(secKey, out double delta))
            {
                string deltaStr = (delta >= 0 ? "+" : "") + $"UGX {delta:N0}";
                var deltaPill = new Border
                {
                    Background = delta >= 0
                        ? new SolidColorBrush(Color.FromRgb(200, 240, 215))
                        : new SolidColorBrush(Color.FromRgb(252, 210, 210)),
                    CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(0, 2, 10, 2),
                    Child = new TextBlock
                    {
                        Text = deltaStr, FontSize = 10,
                        Foreground = delta >= 0
                            ? new SolidColorBrush(Color.FromRgb(20, 100, 60))
                            : new SolidColorBrush(Color.FromRgb(140, 30, 30))
                    }
                };
                Grid.SetColumn(deltaPill, 3); headerGrid.Children.Add(deltaPill);
            }

            var totalTb = new TextBlock
            {
                Text = displayTotal, FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(26, 58, 92)),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(totalTb, 4); headerGrid.Children.Add(totalTb);

            var itemGrid = BuildItemGrid();
            itemGrid.ItemsSource = vms;
            itemGrid.Margin = new Thickness(0);

            var expander = new Expander
            {
                Header = headerGrid,
                Content = itemGrid,
                IsExpanded = isExpanded,
                Margin = new Thickness(0, 0, 0, 6),
                BorderThickness = new Thickness(1),
                BorderBrush = BorderColor,
                Background = Brushes.White,
                Padding = new Thickness(0)
            };
            // Capture the stable key — sec will be a new instance on next rebuild
            string capturedKey = secKey;
            expander.Expanded  += (s, e) => _openSections.Add(capturedKey);
            expander.Collapsed += (s, e) => _openSections.Remove(capturedKey);
            return expander;
        }

        // ══════════════════════════════════════════════════════════════════
        //  BuildItemGrid — editable DataGrid with RowDetailsTemplate for NRM2.
        //  Double-click a cell to edit; right-click for context menu.
        //  Row background and confidence cell background bound to the VM.
        // ══════════════════════════════════════════════════════════════════

        private DataGrid BuildItemGrid()
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = false,
                CanUserSortColumns = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = BorderColor,
                RowHeaderWidth = 0,
                FontSize = 11,
                RowHeight = 24,
                Background = Brushes.Transparent,
                RowBackground = Brushes.Transparent,
                AlternatingRowBackground = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                MinHeight = 80
            };

            // Row style — applies RowBackground from VM (manual=cream / PS=lavender / model=white)
            var rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(DataGridRow.BackgroundProperty,
                new Binding(nameof(BOQItemViewModel.RowBackground))));
            grid.RowStyle = rowStyle;

            grid.Columns.Add(BuildSourceDotColumn());
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Ref", Binding = new Binding(nameof(BOQItemViewModel.LineRef)),
                Width = new DataGridLength(70), IsReadOnly = true
            });
            grid.Columns.Add(BuildEditableColumn("Item / description", nameof(BOQItemViewModel.ItemName), 220, isNumber: false));
            grid.Columns.Add(BuildEditableColumn("Qty",  nameof(BOQItemViewModel.QuantityDisplay), 70, isNumber: true));
            grid.Columns.Add(BuildEditableColumn("Unit", nameof(BOQItemViewModel.Unit),            55, isNumber: false));
            grid.Columns.Add(BuildEditableColumn("Rate", nameof(BOQItemViewModel.RateDisplay),    105, isNumber: true));
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Total", Binding = new Binding(nameof(BOQItemViewModel.TotalDisplay)),
                Width = new DataGridLength(110), IsReadOnly = true
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Src", Binding = new Binding(nameof(BOQItemViewModel.SourceLabel)),
                Width = new DataGridLength(48), IsReadOnly = true
            });
            grid.Columns.Add(BuildConfidenceColumn());
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "CO₂ kg", Binding = new Binding(nameof(BOQItemViewModel.CarbonDisplay)),
                Width = new DataGridLength(75), IsReadOnly = true
            });
            grid.Columns.Add(BuildEditableColumn("Note", nameof(BOQItemViewModel.Note), 200, isNumber: false));

            // NRM2 detail panel — shown under every row by default so the BOQ
            // description (the whole point of a BOQ) is always visible. The
            // "Hide NRM2" toolbar toggle flips this to Collapsed for a compact
            // price-only view.
            grid.RowDetailsTemplate = BuildNrm2DetailTemplate();
            grid.RowDetailsVisibilityMode = _showNrm2
                ? DataGridRowDetailsVisibilityMode.Visible
                : DataGridRowDetailsVisibilityMode.Collapsed;

            // Double-click: enter edit mode (single click selects + shows details)
            grid.MouseDoubleClick += (s, e) =>
            {
                if (!(e.OriginalSource is DependencyObject src)) return;
                var cell = FindVisualParent<DataGridCell>(src);
                if (cell != null && !cell.IsEditing)
                {
                    cell.IsEditing = true;
                    e.Handled = true;
                }
            };

            // Phase 108d: lock model-derived fields. For rows with Source=Model
            // the Item Name and Qty columns are derived from the Revit element
            // (FamilySymbol name and geometry-based quantity); editing them in
            // the panel would look successful but revert on the next BuildBOQ
            // because the rebuild re-reads the element. Cancel BeginningEdit
            // for those columns and surface a short status message instead.
            grid.BeginningEdit += (s, e) =>
            {
                if (e.Row?.Item is BOQItemViewModel vm && vm.IsModelSource && e.Column != null)
                {
                    string hdr = e.Column.Header as string ?? "";
                    if (hdr.StartsWith("Item") || hdr == "Qty")
                    {
                        e.Cancel = true;
                        FlashHint(vm, hdr.StartsWith("Item")
                            ? "Item name is derived from the Revit family — edit the element type in Revit."
                            : "Quantity is derived from the element geometry — it can't be overridden inline.");
                    }
                }
            };

            // Phase 108d: forward mouse-wheel events to the outer ScrollViewer.
            // A DataGrid's internal ScrollViewer swallows the wheel event by
            // default, which stops the whole page from scrolling when the
            // cursor is over a grid. Re-raise the event on the DataGrid's
            // parent so the enclosing ScrollViewer picks it up.
            grid.PreviewMouseWheel += (s, e) =>
            {
                if (e.Handled) return;
                e.Handled = true;
                var forward = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = grid
                };
                (grid.Parent as UIElement)?.RaiseEvent(forward);
            };

            // Persist edits on cell commit
            grid.CellEditEnding += (s, e) =>
            {
                if (e.EditAction == DataGridEditAction.Commit
                    && grid.SelectedItem is BOQItemViewModel vm)
                {
                    Dispatcher.BeginInvoke(new Action(() => OnItemEdited(vm)),
                        System.Windows.Threading.DispatcherPriority.Background);
                }
            };

            // Context menu
            grid.ContextMenuOpening += (s, e) =>
            {
                if (grid.SelectedItem is BOQItemViewModel vm)
                    grid.ContextMenu = BuildRowContextMenu(vm);
                else
                    e.Handled = true;
            };

            return grid;
        }

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var current = System.Windows.Media.VisualTreeHelper.GetParent(child);
            while (current != null)
            {
                if (current is T typed) return typed;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        // ── Column factories ────────────────────────────────────────────────

        private DataGridTemplateColumn BuildEditableColumn(string header, string bindingPath, double width, bool isNumber)
        {
            var displayTpl = new DataTemplate();
            var displayFactory = new FrameworkElementFactory(typeof(TextBlock));
            displayFactory.SetBinding(TextBlock.TextProperty, new Binding(bindingPath));
            displayFactory.SetValue(TextBlock.MarginProperty, new Thickness(4, 0, 4, 0));
            displayFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            if (isNumber)
                displayFactory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
            displayTpl.VisualTree = displayFactory;

            var editTpl = new DataTemplate();
            var editFactory = new FrameworkElementFactory(typeof(TextBox));
            editFactory.SetBinding(TextBox.TextProperty, new Binding(bindingPath)
                { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus, Mode = BindingMode.TwoWay });
            editFactory.SetValue(TextBox.BorderThicknessProperty, new Thickness(0));
            editFactory.SetValue(TextBox.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(235, 245, 255)));
            editFactory.SetValue(TextBox.FontSizeProperty, 11.0);
            editFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
            editFactory.AddHandler(UIElement.GotFocusEvent,
                new RoutedEventHandler((s, e) => { if (s is TextBox tb) tb.SelectAll(); }));
            editTpl.VisualTree = editFactory;

            return new DataGridTemplateColumn
            {
                Header = header, Width = new DataGridLength(width),
                CellTemplate = displayTpl, CellEditingTemplate = editTpl,
                IsReadOnly = false
            };
        }

        private DataGridTemplateColumn BuildConfidenceColumn()
        {
            var tpl = new DataTemplate();
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty,
                new Binding(nameof(BOQItemViewModel.ConfidenceBrush)));
            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetBinding(TextBlock.TextProperty,
                new Binding(nameof(BOQItemViewModel.RateConfidenceDisplay)));
            tb.SetValue(TextBlock.MarginProperty, new Thickness(4, 0, 4, 0));
            tb.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            tb.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(tb);
            tpl.VisualTree = border;
            return new DataGridTemplateColumn
            {
                Header = "Conf", Width = new DataGridLength(42),
                CellTemplate = tpl, IsReadOnly = true
            };
        }

        private DataGridTemplateColumn BuildSourceDotColumn()
        {
            var tpl = new DataTemplate();
            var ell = new FrameworkElementFactory(typeof(Ellipse));
            ell.SetValue(Ellipse.WidthProperty, 8.0);
            ell.SetValue(Ellipse.HeightProperty, 8.0);
            ell.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            ell.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            ell.SetBinding(Ellipse.FillProperty, new Binding(nameof(BOQItemViewModel.SourceDotBrush)));
            tpl.VisualTree = ell;
            return new DataGridTemplateColumn
            {
                Header = "", Width = new DataGridLength(14),
                CellTemplate = tpl, IsReadOnly = true
            };
        }

        // ── NRM2 paragraph detail template (RowDetailsTemplate) ─────────────

        private DataTemplate BuildNrm2DetailTemplate()
        {
            var detailTpl = new DataTemplate();

            // Outer soft-blue panel
            var outerBorder = new FrameworkElementFactory(typeof(Border));
            outerBorder.SetValue(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(248, 250, 253)));
            outerBorder.SetValue(Border.BorderBrushProperty,
                new SolidColorBrush(Color.FromRgb(210, 220, 235)));
            outerBorder.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
            outerBorder.SetValue(Border.PaddingProperty, new Thickness(10, 6, 10, 6));

            var detailSp = new FrameworkElementFactory(typeof(StackPanel));

            // "NRM2 DESCRIPTION" caption
            var nrm2Label = new FrameworkElementFactory(typeof(TextBlock));
            nrm2Label.SetValue(TextBlock.TextProperty, "DESCRIPTION");
            nrm2Label.SetValue(TextBlock.FontSizeProperty, 9.0);
            nrm2Label.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            nrm2Label.SetValue(TextBlock.ForegroundProperty,
                new SolidColorBrush(Color.FromRgb(110, 130, 160)));
            nrm2Label.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 0, 3));
            detailSp.AppendChild(nrm2Label);

            // Grid so the watermark TextBlock can overlay the TextBox
            var overlayGrid = new FrameworkElementFactory(typeof(Grid));

            var nrm2Box = new FrameworkElementFactory(typeof(TextBox));
            nrm2Box.SetBinding(TextBox.TextProperty, new Binding(nameof(BOQItemViewModel.NRM2Paragraph))
                { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus, Mode = BindingMode.TwoWay });
            nrm2Box.SetValue(TextBox.TextWrappingProperty, TextWrapping.Wrap);
            nrm2Box.SetValue(TextBox.AcceptsReturnProperty, true);
            nrm2Box.SetValue(TextBox.FontSizeProperty, 11.5);
            nrm2Box.SetValue(TextBox.ForegroundProperty,
                new SolidColorBrush(Color.FromRgb(40, 50, 80)));
            nrm2Box.SetValue(TextBox.BackgroundProperty, Brushes.Transparent);
            nrm2Box.SetValue(TextBox.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
            nrm2Box.SetValue(TextBox.BorderBrushProperty,
                new SolidColorBrush(Color.FromRgb(220, 230, 245)));
            nrm2Box.SetValue(TextBox.PaddingProperty, new Thickness(2, 2, 2, 2));
            overlayGrid.AppendChild(nrm2Box);

            // Watermark — shows when paragraph is empty; click the TextBox
            // (which sits under the watermark) to start typing.
            var hint = new FrameworkElementFactory(typeof(TextBlock));
            hint.SetValue(TextBlock.TextProperty,
                "Click to enter description for this BOQ item…");
            hint.SetValue(TextBlock.FontSizeProperty, 11.0);
            hint.SetValue(TextBlock.FontStyleProperty, FontStyles.Italic);
            hint.SetValue(TextBlock.ForegroundProperty,
                new SolidColorBrush(Color.FromRgb(170, 180, 200)));
            hint.SetValue(TextBlock.IsHitTestVisibleProperty, false);
            hint.SetValue(TextBlock.MarginProperty, new Thickness(3, 2, 0, 0));
            hint.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Top);
            hint.SetBinding(TextBlock.VisibilityProperty,
                new Binding(nameof(BOQItemViewModel.NRM2EmptyHintVisibility)));
            overlayGrid.AppendChild(hint);

            detailSp.AppendChild(overlayGrid);

            // Footnote
            var editNote = new FrameworkElementFactory(typeof(TextBlock));
            editNote.SetValue(TextBlock.TextProperty,
                "Edit inline · click away or press Tab to save · Export and Save Snapshot persist the paragraph");
            editNote.SetValue(TextBlock.FontSizeProperty, 9.0);
            editNote.SetValue(TextBlock.ForegroundProperty,
                new SolidColorBrush(Color.FromRgb(160, 170, 180)));
            editNote.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 0, 0));
            detailSp.AppendChild(editNote);

            outerBorder.AppendChild(detailSp);
            detailTpl.VisualTree = outerBorder;
            return detailTpl;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Right-click context menu + edit helpers
        // ══════════════════════════════════════════════════════════════════

        private ContextMenu BuildRowContextMenu(BOQItemViewModel vm)
        {
            var ctx = new ContextMenu();
            void Add(string header, Action onClick, bool enabled = true)
            {
                var mi = new MenuItem { Header = header, IsEnabled = enabled };
                mi.Click += (s, e) => onClick();
                ctx.Items.Add(mi);
            }

            Add("Edit item name",       () => BeginEditCell(vm, 2));
            Add("Edit quantity",        () => BeginEditCell(vm, 3));
            Add("Edit unit",            () => BeginEditCell(vm, 4));
            Add("Edit rate",            () => BeginEditCell(vm, 5));
            Add("Edit note",            () => BeginEditCell(vm, 10));
            ctx.Items.Add(new Separator());
            Add("Edit description…", () => ShowNRM2EditDialog(vm));
            ctx.Items.Add(new Separator());
            Add("Mark as modeled",           () => ChangeSource(vm, BOQRowSource.Model));
            Add("Mark as manual / unmodeled", () => ChangeSource(vm, BOQRowSource.Manual));
            Add("Mark as provisional sum",   () => ChangeSource(vm, BOQRowSource.ProvisionalSum));
            ctx.Items.Add(new Separator());
            Add("Duplicate row",        () => DuplicateRow(vm));
            Add("Delete row",           () => DeleteRow(vm),
                enabled: vm.Underlying.Source != BOQRowSource.Model);
            ctx.Items.Add(new Separator());
            Add("Select in Revit",      () =>
            {
                StingCommandHandler.SetExtraParam("SelectElementId", vm.RevitElementId.ToString());
                DispatchAction("SelectInRevit");
            }, enabled: vm.RevitElementId > 0);
            return ctx;
        }

        private void BeginEditCell(BOQItemViewModel vm, int colIndex)
        {
            foreach (var grid in FindAllDataGrids(_sectionsPanel))
            {
                if (grid.Items.Contains(vm))
                {
                    grid.SelectedItem = vm;
                    if (colIndex < grid.Columns.Count)
                    {
                        grid.CurrentColumn = grid.Columns[colIndex];
                        grid.BeginEdit();
                    }
                    return;
                }
            }
        }

        private IEnumerable<DataGrid> FindAllDataGrids(DependencyObject parent)
        {
            if (parent == null) yield break;
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is DataGrid dg) yield return dg;
                foreach (var nested in FindAllDataGrids(child)) yield return nested;
            }
        }

        private void ShowNRM2EditDialog(BOQItemViewModel vm)
        {
            var w = new Window
            {
                Title = $"Description — {vm.ItemName}",
                Width = 680, Height = 280,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                Background = Brushes.White,
                Owner = Window.GetWindow(this)
            };
            var sp = new StackPanel { Margin = new Thickness(14) };
            sp.Children.Add(new TextBlock
            {
                Text = "Edit the description for this BOQ item.",
                FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 8)
            });
            var tb = new System.Windows.Controls.TextBox
            {
                Text = vm.NRM2Paragraph, TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true, MinHeight = 80, MaxHeight = 140,
                FontSize = 12, Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 10)
            };
            sp.Children.Add(tb);
            sp.Children.Add(new TextBlock
            {
                Text = "Describe what is being supplied and installed. Do not include costs or quantities.",
                FontSize = 10, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 8)
            });
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Save", Width = 80, Height = 26, Margin = new Thickness(0, 0, 8, 0),
                Background = NavyBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0), IsDefault = true };
            var cancel = new Button { Content = "Cancel", Width = 80, Height = 26, IsCancel = true };
            btnRow.Children.Add(ok); btnRow.Children.Add(cancel);
            sp.Children.Add(btnRow);
            w.Content = sp;
            ok.Click += (s, e) =>
            {
                string val = tb.Text?.Trim() ?? "";
                if (!string.IsNullOrEmpty(val))
                {
                    vm.Underlying.ResolvedNRM2Paragraph = val;
                    OnItemEdited(vm);
                }
                w.Close();
            };
            tb.Focus(); tb.SelectAll();
            w.ShowDialog();
        }

        // ── Source change / duplicate / delete / persistence ────────────────
        // Structural ops call RefreshDisplay() (metrics + section rebuild +
        // materials rebuild) because adding, removing or promoting a row
        // changes both the displayed rows AND the modeled / provisional /
        // grand totals / carbon card. Using RebuildSectionsView alone (as
        // before) left the budget strip stale — e.g. the Phase-108e user
        // report "duplicated a row but figures never updated, totals never
        // picked the copies' figures".

        private void ChangeSource(BOQItemViewModel vm, BOQRowSource src)
        {
            vm.Underlying.Source = src;
            PersistManualRows();
            RefreshDisplay();
        }

        private void DuplicateRow(BOQItemViewModel vm)
        {
            var clone = vm.Underlying.Clone();
            clone.Id = Guid.NewGuid().ToString("N");
            clone.ItemName = (clone.ItemName ?? "") + " (copy)";
            clone.BOQLineRef = "";
            clone.RevitElementId = -1;
            // Cloned rows become manual so they can be edited/deleted freely
            if (clone.Source == BOQRowSource.Model) clone.Source = BOQRowSource.Manual;
            var sec = _boq?.Sections.FirstOrDefault(s => s.Items.Contains(vm.Underlying));
            if (sec != null)
            {
                int idx = sec.Items.IndexOf(vm.Underlying);
                sec.Items.Insert(idx + 1, clone);
                PersistManualRows();
                RefreshDisplay();  // totals + carbon must recompute
            }
        }

        private void DeleteRow(BOQItemViewModel vm)
        {
            if (vm.Underlying.Source == BOQRowSource.Model) return;
            var sec = _boq?.Sections.FirstOrDefault(s => s.Items.Contains(vm.Underlying));
            if (sec != null)
            {
                sec.Items.Remove(vm.Underlying);
                PersistManualRows();
                RefreshDisplay();  // removed row's cost must leave the grand total
            }
        }

        /// <summary>
        /// Called by CellEditEnding when the user commits a cell edit.
        /// Persists the edit and updates the budget strip — but does NOT
        /// rebuild the section grids. Rebuilding would tear down the user's
        /// just-bound VM and race the async CST_UNIT_RATE_UGX write in
        /// BOQWriteItemParamsCommand, causing the displayed value to flash
        /// to the new number and then revert to the original. The in-memory
        /// BOQLineItem already has the new rate (VM setter mutated it), so
        /// recomputing metrics gives the user the correct picture. The next
        /// full RefreshAsync (Refresh button, currency toggle, doc re-open)
        /// will re-read from Revit with CST_RATE_SOURCE=Override honoured by
        /// the new Pass-0 branch in ResolveRate.
        /// </summary>
        private void OnItemEdited(BOQItemViewModel vm)
        {
            if (_boq == null) return;
            try
            {
                if (vm.Underlying.Source != BOQRowSource.Model)
                {
                    PersistManualRows();
                }
                else
                {
                    vm.Underlying.RateSource = "Override";

                    // (1) Phase 108f — durable sidecar write on the WPF thread.
                    // This is the authoritative source of truth for model-row
                    // overrides; BuildBOQDocument re-applies it via
                    // ApplyModelOverrides on every rebuild, so the edit
                    // survives Refresh, doc re-open and Revit restart even
                    // when the ExternalEvent below loses its slot to a
                    // subsequent SetCommand call.
                    try
                    {
                        BOQCostManager.UpsertModelOverride(Doc, new BOQModelOverride
                        {
                            UniqueId      = vm.Underlying.UniqueId,
                            ElementId     = vm.RevitElementId,
                            RateUGX       = vm.Underlying.RateUGX,
                            RateUSD       = vm.Underlying.RateUSD,
                            NRM2Paragraph = vm.Underlying.ResolvedNRM2Paragraph ?? "",
                            Note          = vm.Underlying.Note ?? "",
                            ModifiedBy    = Environment.UserName ?? ""
                        });
                    }
                    catch (Exception ex) { StingLog.Error("BOQ UpsertModelOverride", ex); }

                    // (2) Best-effort background sync to Revit element params
                    // (CST_UNIT_RATE_UGX, CST_RATE_SOURCE, ASS_NRM2_PARA_TXT)
                    // so export / IFC / COBie see the current values. May be
                    // superseded by a subsequent dispatch before Execute runs
                    // — that's fine, the sidecar already holds the edit.
                    StingCommandHandler.SetExtraParam("BOQEditElementId", vm.RevitElementId.ToString());
                    StingCommandHandler.SetExtraParam("BOQEditRateUGX",   vm.Underlying.RateUGX.ToString(CultureInfo.InvariantCulture));
                    StingCommandHandler.SetExtraParam("BOQEditRateUSD",   vm.Underlying.RateUSD.ToString(CultureInfo.InvariantCulture));
                    StingCommandHandler.SetExtraParam("BOQEditNRM2Para",  vm.Underlying.ResolvedNRM2Paragraph ?? "");
                    StingCommandHandler.SetExtraParam("BOQEditNote",      vm.Underlying.Note ?? "");
                    DispatchAction("BOQWriteItemParams", refreshAfter: false);
                }
                // Metrics only — keeps the user's edit visible and updates
                // the totals / budget bar / carbon card in place.
                RefreshMetrics();
            }
            catch (Exception ex) { StingLog.Error("BOQ OnItemEdited", ex); }
        }

        private void PersistManualRows()
        {
            if (Doc == null || _boq == null) return;
            try
            {
                var manuals = _boq.AllItems.Where(i => i.Source != BOQRowSource.Model).ToList();
                var store = BOQCostManager.LoadManualStore(Doc);
                BOQCostManager.SaveManualRows(Doc, manuals, store?.ProjectBudgetUGX ?? _boq.ProjectBudgetUGX);
            }
            catch (Exception ex) { StingLog.Error("BOQ PersistManualRows", ex); }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Materials tab — groups modeled BOQ items by category, computes
        //  per-category totals and aggregate embodied carbon.
        // ══════════════════════════════════════════════════════════════════

        private void RebuildMaterialsTab()
        {
            if (_materialsTab == null) return;
            _materialsTab.Content = BuildMaterialsContent();
        }

        private FrameworkElement BuildMaterialsContent()
        {
            if (_boq == null)
                return new TextBlock { Text = "No data", Margin = new Thickness(14), Foreground = Brushes.Gray };

            var sp = new StackPanel { Margin = new Thickness(12) };
            var grouped = _boq.AllItems
                .Where(i => i.Source == BOQRowSource.Model && i.Quantity > 0)
                .GroupBy(i => i.Category ?? "(uncategorised)")
                .OrderBy(g => g.Key)
                .ToList();

            if (grouped.Count == 0)
            {
                sp.Children.Add(new TextBlock
                {
                    Text = "No modeled materials yet — add rows via the BOQ tab first.",
                    Foreground = Brushes.Gray, Margin = new Thickness(6)
                });
                return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            }

            foreach (var grp in grouped)
            {
                double totalUGX = grp.Sum(i => i.TotalUGX);
                double totalCarbon = grp.Sum(i => i.EmbodiedCarbonKg);
                string disc = grp.First().Discipline ?? "";

                var expander = new Expander
                {
                    Header = BuildMaterialSectionHeader(grp.Key, disc, grp.Count(), totalUGX, totalCarbon),
                    IsExpanded = false,
                    Margin = new Thickness(0, 0, 0, 4),
                    BorderThickness = new Thickness(1),
                    BorderBrush = BorderColor,
                    Background = Brushes.White
                };

                var grid = new DataGrid
                {
                    AutoGenerateColumns = false, CanUserAddRows = false,
                    IsReadOnly = true, FontSize = 11, RowHeight = 22,
                    Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                    HorizontalGridLinesBrush = BorderColor,
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                    RowHeaderWidth = 0
                };
                grid.Columns.Add(new DataGridTextColumn { Header = "Ref",    Binding = new Binding(nameof(BOQItemViewModel.LineRef)),          Width = new DataGridLength(70)  });
                grid.Columns.Add(new DataGridTextColumn { Header = "Item",   Binding = new Binding(nameof(BOQItemViewModel.ItemName)),         Width = new DataGridLength(220) });
                grid.Columns.Add(new DataGridTextColumn { Header = "Qty",    Binding = new Binding(nameof(BOQItemViewModel.QuantityDisplay)),  Width = new DataGridLength(70)  });
                grid.Columns.Add(new DataGridTextColumn { Header = "Unit",   Binding = new Binding(nameof(BOQItemViewModel.Unit)),             Width = new DataGridLength(55)  });
                grid.Columns.Add(new DataGridTextColumn { Header = "Rate",   Binding = new Binding(nameof(BOQItemViewModel.RateDisplay)),      Width = new DataGridLength(105) });
                grid.Columns.Add(new DataGridTextColumn { Header = "Total",  Binding = new Binding(nameof(BOQItemViewModel.TotalDisplay)),     Width = new DataGridLength(110) });
                grid.Columns.Add(new DataGridTextColumn { Header = "CO₂ kg", Binding = new Binding(nameof(BOQItemViewModel.CarbonDisplay)),    Width = new DataGridLength(85)  });
                grid.ItemsSource = grp.Select(i => new BOQItemViewModel(i, _displayCurrency, _boq.ExchangeRateUgxPerUsd)).ToList();
                expander.Content = grid;
                sp.Children.Add(expander);
            }

            double grandMat = _boq.AllItems.Where(i => i.Source == BOQRowSource.Model).Sum(i => i.TotalUGX);
            double grandCarbon = _boq.TotalCarbonKg;
            var footer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(240, 244, 248)),
                Padding = new Thickness(14, 8, 14, 8),
                BorderThickness = new Thickness(0, 1, 0, 0),
                BorderBrush = BorderColor, Margin = new Thickness(0, 8, 0, 0)
            };
            footer.Child = new TextBlock
            {
                Text = $"Modeled material total (excl. markups): UGX {grandMat:N0}   ·   Embodied carbon: {grandCarbon:N0} kgCO₂e",
                FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(26, 58, 92))
            };
            sp.Children.Add(footer);

            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private FrameworkElement BuildMaterialSectionHeader(string cat, string disc, int count, double totalUGX, double carbonKg)
        {
            var g = new Grid { Background = SectionHeaderBrush(disc) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var badge = new Border
            {
                Background = new SolidColorBrush(DiscBadgeColour(disc)),
                CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(6, 2, 8, 2),
                Child = new TextBlock { Text = string.IsNullOrEmpty(disc) ? "—" : disc,
                    Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold }
            };
            Grid.SetColumn(badge, 0); g.Children.Add(badge);

            var name = new TextBlock
            {
                Text = $"{cat}  ·  {count} items", FontSize = 12, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(name, 1); g.Children.Add(name);

            if (carbonKg > 0)
            {
                var carbonPill = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(225, 245, 238)),
                    CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(0, 2, 10, 2),
                    Child = new TextBlock
                    {
                        Text = $"{carbonKg:N0} kgCO₂e", FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(15, 110, 86))
                    }
                };
                Grid.SetColumn(carbonPill, 2); g.Children.Add(carbonPill);
            }

            var total = new TextBlock
            {
                Text = $"UGX {totalUGX:N0}", FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(26, 58, 92)),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 8, 2)
            };
            Grid.SetColumn(total, 3); g.Children.Add(total);
            return g;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Snapshots — dropdown + per-section delta + save menu
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
            _snapshotDeltas.Clear();
            _activeSnapshotLabel = "";

            if (_snapshotPicker.SelectedIndex <= 0 || _boq == null)
            {
                _snapshotDiff.Text = "";
                RebuildSectionsView();
                return;
            }

            var meta = _snapshotMetas[_snapshotPicker.SelectedIndex - 1];
            _activeSnapshotLabel = meta.Label;

            double delta = _boq.GrandTotalUGX - meta.GrandTotalUGX;
            string sign = delta >= 0 ? "+" : "";
            _snapshotDiff.Text = $"{sign}UGX {delta:N0} vs {meta.Label}";
            _snapshotDiff.Foreground = delta >= 0 ? GreenBrush : RedBrush;

            // Per-section deltas: compare the selected snapshot's section totals
            // against the current live BOQ. Matches by (NRM2Section, Discipline)
            // so section Ids can differ between runs.
            try
            {
                var snapDoc = BOQCostManager.LoadSnapshot(meta.Path);
                if (snapDoc != null)
                {
                    foreach (var liveSec in _boq.Sections)
                    {
                        var snapSec = snapDoc.Sections.FirstOrDefault(s =>
                            s.NRM2Section == liveSec.NRM2Section && s.Discipline == liveSec.Discipline);
                        if (snapSec != null)
                            _snapshotDeltas[SectionKey(liveSec)] = liveSec.TotalUGX - snapSec.TotalUGX;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Snapshot delta: {ex.Message}"); }

            RebuildSectionsView();
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
            string section = PromptString("Section number:", "22");
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

        /// <summary>
        /// Flash an amber hint message in the coverage strip for 4s, then
        /// restore the default coverage summary. Safe to call repeatedly —
        /// _flashSeq ensures only the latest restore actually fires.
        /// </summary>
        private void FlashHint(BOQItemViewModel vm, string message)
        {
            if (_paragraphCoverage == null) return;
            int mine = System.Threading.Interlocked.Increment(ref _flashSeq);
            _paragraphCoverage.Text = message;
            _paragraphCoverage.Foreground = AmberBrush;
            System.Threading.Tasks.Task.Delay(4000).ContinueWith(_ =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (mine != _flashSeq || _paragraphCoverage == null) return;
                    _paragraphCoverage.Text = _defaultCoverageText ?? "";
                    _paragraphCoverage.Foreground = Brushes.Gray;
                }));
            });
        }

        /// <summary>
        /// Fire-and-forget dispatch of a command tag to the shared ExternalEvent.
        /// When refreshAfter=true (default) schedules a 300ms RefreshAsync so
        /// the panel picks up side effects of the command (spatial changes,
        /// new tags, etc.). Pass refreshAfter=false from edit commit paths —
        /// the edit is already visible in the mutated VM and the 300ms rebuild
        /// races the async parameter write, causing the edit to flash-and-revert.
        /// </summary>
        private void DispatchAction(string tag, bool refreshAfter = true)
        {
            try
            {
                StingDockPanel.DispatchCommand(tag);
                if (refreshAfter)
                {
                    System.Threading.Tasks.Task.Delay(300)
                        .ContinueWith(_ => Dispatcher.BeginInvoke(new Action(RefreshAsync)));
                }
            }
            catch (Exception ex) { StingLog.Error($"BOQ DispatchAction({tag})", ex); }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  BOQItemViewModel — mutable view over BOQLineItem. Edits flow straight
    //  into the underlying model; the panel persists via SaveManualRows for
    //  manual/PS sources and via the BOQWriteItemParams ExternalEvent for
    //  modeled sources.
    // ══════════════════════════════════════════════════════════════════════

    internal class BOQItemViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void N(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        private readonly BOQLineItem _item;
        private string _currency;
        private double _ugxPerUsd;

        public BOQItemViewModel(BOQLineItem item, string currency, double ugxPerUsd)
        {
            _item = item;
            _currency = currency ?? "UGX";
            _ugxPerUsd = ugxPerUsd > 0 ? ugxPerUsd : 3700;
        }

        public BOQLineItem Underlying => _item;
        public long RevitElementId => _item.RevitElementId;
        public bool IsModelSource => _item.Source == BOQRowSource.Model;

        public string LineRef => _item.BOQLineRef ?? "";

        public string ItemName
        {
            get => _item.ItemName ?? "";
            set { if (_item.ItemName != value) { _item.ItemName = value; N(nameof(ItemName)); } }
        }

        public string QuantityDisplay
        {
            get => _item.Quantity.ToString("N3", CultureInfo.InvariantCulture);
            set
            {
                string clean = (value ?? "").Replace(",", "").Trim();
                if (double.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out double d) && d >= 0)
                {
                    _item.Quantity = d;
                    N(nameof(QuantityDisplay)); N(nameof(TotalDisplay));
                }
            }
        }

        public string Unit
        {
            get => _item.Unit ?? "";
            set { var v = string.IsNullOrWhiteSpace(value) ? "each" : value.Trim();
                  if (_item.Unit != v) { _item.Unit = v; N(nameof(Unit)); } }
        }

        public string RateDisplay
        {
            get => _currency == "USD" ? $"$ {_item.RateUSD:N2}" : $"UGX {_item.RateUGX:N0}";
            set
            {
                string clean = (value ?? "").Replace("UGX", "").Replace("$", "").Replace(",", "").Trim();
                if (double.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out double d) && d >= 0)
                {
                    if (_currency == "USD")
                    {
                        _item.RateUSD = d;
                        _item.RateUGX = Math.Round(d * _ugxPerUsd, 0);
                    }
                    else
                    {
                        _item.RateUGX = d;
                        _item.RateUSD = _ugxPerUsd > 0 ? Math.Round(d / _ugxPerUsd, 2) : 0;
                    }
                    _item.RateSource = "Override";
                    N(nameof(RateDisplay)); N(nameof(TotalDisplay));
                }
            }
        }

        public string TotalDisplay => _currency == "USD"
            ? $"$ {_item.TotalUSD:N2}" : $"UGX {_item.TotalUGX:N0}";

        public string Note
        {
            get => _item.Note ?? "";
            set { if (_item.Note != value) { _item.Note = value ?? ""; N(nameof(Note)); } }
        }

        /// <summary>
        /// NRM2 BOQ description paragraph. Always editable — when the backend
        /// couldn't resolve a template the getter returns empty (so the
        /// TextBox is clean for typing). The detail panel under each row
        /// shows a watermark hint label when this is empty.
        /// </summary>
        public string NRM2Paragraph
        {
            get => _item.ResolvedNRM2Paragraph ?? "";
            set
            {
                string v = (value ?? "").Trim();
                if (_item.ResolvedNRM2Paragraph != v)
                {
                    _item.ResolvedNRM2Paragraph = v;
                    N(nameof(NRM2Paragraph));
                    N(nameof(HasNRM2Paragraph));
                    N(nameof(NRM2EmptyHintVisibility));
                }
            }
        }

        public bool HasNRM2Paragraph => !string.IsNullOrEmpty(_item.ResolvedNRM2Paragraph);

        /// <summary>
        /// Visibility of the "Click to enter NRM2 description…" watermark
        /// shown over an empty paragraph TextBox.
        /// </summary>
        public Visibility NRM2EmptyHintVisibility
            => HasNRM2Paragraph ? Visibility.Collapsed : Visibility.Visible;

        public string SourceLabel
        {
            get
            {
                switch (_item.Source)
                {
                    case BOQRowSource.ProvisionalSum: return "PS";
                    case BOQRowSource.Manual:         return "Manual";
                    default:                          return "Model";
                }
            }
        }

        public string RateConfidenceDisplay => _item.RateConfidence.ToString(CultureInfo.InvariantCulture);

        public string CarbonDisplay => _item.EmbodiedCarbonKg > 0
            ? _item.EmbodiedCarbonKg.ToString("N0", CultureInfo.InvariantCulture) + " kg"
            : "";

        // ── Binding brushes ─────────────────────────────────────────────────
        // Row background: manual = cream, PS = lavender, model = transparent
        public SolidColorBrush RowBackground
        {
            get
            {
                Color c;
                switch (_item.Source)
                {
                    case BOQRowSource.ProvisionalSum: c = Color.FromRgb(237, 231, 246); break;
                    case BOQRowSource.Manual:         c = Color.FromRgb(255, 251, 230); break;
                    default:                          c = Color.FromArgb(0, 255, 255, 255); break;
                }
                var b = new SolidColorBrush(c); b.Freeze(); return b;
            }
        }

        public SolidColorBrush SourceDotBrush
        {
            get
            {
                Color c;
                switch (_item.Source)
                {
                    case BOQRowSource.ProvisionalSum: c = Color.FromRgb(138, 120, 190); break;
                    case BOQRowSource.Manual:         c = Color.FromRgb(230, 160, 40);  break;
                    default:                          c = Color.FromRgb(60, 130, 90);   break;
                }
                var b = new SolidColorBrush(c); b.Freeze(); return b;
            }
        }

        // Confidence cell background: ≥75=green, 40-74=amber, <40=red
        public SolidColorBrush ConfidenceBrush
        {
            get
            {
                Color c;
                if (_item.RateConfidence >= 75)      c = Color.FromRgb(200, 240, 220);
                else if (_item.RateConfidence >= 40) c = Color.FromRgb(255, 243, 205);
                else                                 c = Color.FromRgb(252, 220, 220);
                var b = new SolidColorBrush(c); b.Freeze(); return b;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ToggleButton alias — keep the file self-contained without extra using.
    // ══════════════════════════════════════════════════════════════════════

    internal class ToggleButton : System.Windows.Controls.Primitives.ToggleButton { }
}
