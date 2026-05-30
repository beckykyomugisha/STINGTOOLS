using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.Core;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;

namespace StingTools.UI
{
    /// <summary>
    /// STING Material Hub — modeless three-pane dashboard. Code-behind
    /// is intentionally thin; heavy lifting lives in MatActions /
    /// MaterialRowBuilder / individual gate engines. Hub adds:
    ///   • KPI strip + action bar (built from typed models)
    ///   • Navigation tree (faceted browse)
    ///   • Three-pane layout with GridSplitters
    ///   • Inspector card stack rebuilt on selection change
    ///   • Toast overlay + activity feed wiring (priorities 6 + 7)
    ///   • Right-click context + keyboard shortcuts (priority 8)
    ///   • Cell-edit binding via explicit TextBox in template column
    ///     (priority 3 — fixes the cost-revert bug)
    /// </summary>
    public partial class MaterialHubPanel : Page
    {
        public static MaterialHubPanel LastInstance { get; private set; }

        private ObservableCollection<MaterialRow> _rows;
        private List<FilterChip> _activeChips = new List<FilterChip>();

        public MaterialHubPanel()
        {
            InitializeComponent();
            // Phase C — register with ThemeManager so ThemeManager.CycleTheme()
            // repaints the merged StingButtonStyles.xaml DynamicResource keys
            // (ButtonBg / ButtonFg / BorderColor) on this panel. Identity
            // chrome (HubHeaderBg etc.) is local + intentionally NOT theme-cycled.
            try { ThemeManager.RegisterTarget(this); ThemeManager.InitialiseResources(); }
            catch { /* theme is non-fatal */ }
            LastInstance = this;
            this.Loaded += (_, __) => { InitialPopulate(); ApplyResponsiveLayout(this.ActualWidth); };
            // The three-pane layout (nav | grid | inspector) needs a wide
            // workspace. When docked narrow (the default left-dock width),
            // collapse the nav + inspector side-panes so the materials grid
            // stays visible instead of being pushed off-screen.
            this.SizeChanged += (_, e) => ApplyResponsiveLayout(e.NewSize.Width);
            // Priority 7 — Subscribe to the activity feed so newly-pushed
            // entries surface in the status bar immediately. Unsubscribe on
            // Unloaded so a docked-then-hidden panel doesn't accumulate
            // duplicate handlers across multiple show/hide cycles.
            MaterialActivityFeed.OnAdded += RenderActivityFeed;
            this.Unloaded += (_, __) =>
            {
                try { MaterialActivityFeed.OnAdded -= RenderActivityFeed; }
                catch (Exception ex) { StingLog.WarnRateLimited("HubUnload", $"Unsubscribe ActivityFeed: {ex.Message}"); }
            };
        }

        private bool _narrowLayout;
        private bool _responsiveInit;
        /// <summary>
        /// Switch the centre area between the wide three-pane layout
        /// (nav | grid | inspector) and a narrow grid-only layout. Below 720px
        /// the nav + inspector columns collapse so the materials grid — the
        /// interactive heart of the hub — is never pushed off the visible dock.
        /// </summary>
        private void ApplyResponsiveLayout(double width)
        {
            try
            {
                if (colNav == null || colInspector == null || colGrid == null) return;
                bool narrow = width > 0 && width < 720;
                if (_responsiveInit && narrow == _narrowLayout) return; // no churn
                _responsiveInit = true;
                _narrowLayout = narrow;

                if (narrow)
                {
                    colNav.MinWidth = 0;       colNav.Width = new System.Windows.GridLength(0);
                    colInspector.MinWidth = 0; colInspector.Width = new System.Windows.GridLength(0);
                    if (colSplit1 != null) colSplit1.Width = new System.Windows.GridLength(0);
                    if (colSplit2 != null) colSplit2.Width = new System.Windows.GridLength(0);
                }
                else
                {
                    colNav.MinWidth = 160;       colNav.Width = new System.Windows.GridLength(220);
                    colInspector.MinWidth = 240; colInspector.Width = new System.Windows.GridLength(340);
                    if (colSplit1 != null) colSplit1.Width = new System.Windows.GridLength(3);
                    if (colSplit2 != null) colSplit2.Width = new System.Windows.GridLength(3);
                }
            }
            catch (Exception ex) { StingLog.WarnRateLimited("HubLayout", ex.Message); }
        }

        private void RenderActivityFeed()
        {
            try
            {
                if (activityFeed == null || !Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(new Action(RenderActivityFeed));
                    return;
                }
                activityFeed.Children.Clear();
                foreach (var e in MaterialActivityFeed.Snapshot())
                {
                    var pill = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0x32, 0x4A, 0x66)),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(6, 1, 6, 1),
                        Margin = new Thickness(2, 0, 0, 0),
                        Cursor = Cursors.Hand,
                        ToolTip = $"{e.At:HH:mm:ss} · {e.Kind} · {e.Material} · {e.Description}",
                    };
                    pill.Child = new TextBlock { Text = $"{e.Kind}·{e.Material}", FontSize = 9, Foreground = Brushes.White };
                    string captured = e.Material;
                    pill.MouseLeftButtonDown += (_, __) => JumpToMaterialByName(captured);
                    activityFeed.Children.Add(pill);
                    if (activityFeed.Children.Count >= 5) break;
                }
            }
            catch (Exception ex) { StingLog.Warn($"RenderActivityFeed: {ex.Message}"); }
        }

        private void JumpToMaterialByName(string name)
        {
            if (_rows == null || string.IsNullOrEmpty(name)) return;
            foreach (var r in _rows)
            {
                if (string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    dgHubMaterials.SelectedItem = r;
                    dgHubMaterials.ScrollIntoView(r);
                    return;
                }
            }
        }

        // ── Public toggle entry (used by ribbon command) ───────────────────
        public void Surface()
        {
            // Visibility is owned by Revit's DockablePane; this hook lets
            // the launcher refresh on every show.
            if (!_initialised) InitialPopulate();
        }

        // ── First-time population ──────────────────────────────────────────
        private bool _initialised;
        private void InitialPopulate()
        {
            if (_initialised) return;
            // Each builder is isolated so a failure in one (e.g. a missing
            // resource in the KPI strip) cannot abort the rest — that
            // previously left the navigator, inspector and grid all empty.
            Step("BuildKpiStrip", BuildKpiStrip);
            Step("BuildActionBar", BuildActionBar);
            Step("BuildNavTreeSkeleton", BuildNavTreeSkeleton);
            Step("BuildInspectorPlaceholder", BuildInspectorPlaceholder);
            _initialised = true;
            Step("Refresh", Refresh);
        }

        private static void Step(string name, Action a)
        {
            try { a(); }
            catch (Exception ex) { StingLog.Warn($"MaterialHubPanel.{name}: {ex.Message}"); }
        }

        // ── Refresh — rebuilds rows + dependent UI ─────────────────────────
        public void Refresh()
        {
            try
            {
                var doc = StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    SetStatus("No document open.");
                    return;
                }
                _rows = MaterialRowBuilder.Build(doc);
                dgHubMaterials.ItemsSource = _rows;
                var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_rows);
                view.Filter = RowFilter;
                RefreshKpiValues();
                RefreshNavTreeCounts();
                SetStatus($"Loaded {_rows.Count} materials.");
            }
            catch (Exception ex)
            {
                StingLog.Error("MaterialHubPanel.Refresh", ex);
                SetStatus($"Refresh failed: {ex.Message}");
            }
        }

        // ── Status footer / toast helpers ──────────────────────────────────
        private void SetStatus(string msg)
        {
            if (txtHubStatus != null) txtHubStatus.Text = msg;
        }

        /// <summary>Inline toast (replaces TaskDialog confirmations).
        /// Slides in over the grid, auto-dismisses after 3 s.</summary>
        public void Toast(string message, string severity = "info")
        {
            try
            {
                Color bg = severity switch
                {
                    "error" => Color.FromRgb(0xC0, 0x30, 0x30),
                    "warn"  => Color.FromRgb(0xE0, 0xA0, 0x10),
                    "ok"    => Color.FromRgb(0x2C, 0xA0, 0x2C),
                    _       => Color.FromRgb(0x3D, 0x6F, 0xBA),
                };
                var toast = new Border
                {
                    Background = new SolidColorBrush(bg),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 4, 0, 0),
                    Child = new TextBlock { Text = message, Foreground = Brushes.White, FontSize = 11 }
                };
                activityFeed.Children.Insert(0, toast);
                if (activityFeed.Children.Count > 4)
                    activityFeed.Children.RemoveAt(activityFeed.Children.Count - 1);
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                timer.Tick += (_, __) =>
                {
                    timer.Stop();
                    if (activityFeed.Children.Contains(toast)) activityFeed.Children.Remove(toast);
                };
                timer.Start();
            }
            catch (Exception ex) { StingLog.Warn($"Toast: {ex.Message}"); }
        }

        // ── Filter row ─────────────────────────────────────────────────────
        private bool RowFilter(object item)
        {
            if (!(item is MaterialRow r)) return false;
            string q = (txtGlobalSearch?.Text ?? "").Trim();
            if (!string.IsNullOrEmpty(q))
            {
                bool match = (r.Name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                          || (r.Class ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                          || (r.UniclassCode ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!match) return false;
            }
            foreach (var chip in _activeChips)
            {
                if (!chip.Predicate(r)) return false;
            }
            return true;
        }

        // ── XAML event handlers ────────────────────────────────────────────
        private void GlobalSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_rows != null)
                System.Windows.Data.CollectionViewSource.GetDefaultView(_rows).Refresh();
        }

        private void HubRegion_Changed(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                string s = (cmbHubRegion?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "UK";
                if (!Enum.TryParse<MaterialRegion>(s, true, out var region)) return;
                var doc = StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document;
                if (doc == null) return;
                MaterialLocaleManager.WriteRegionToProject(doc, region);
                MaterialRow.ActiveLocale = MaterialLocaleManager.BuildLocale(region);
                dgHubMaterials?.Items?.Refresh();
                RefreshKpiValues();
                Toast($"Region set to {region}.", "ok");
            }
            catch (Exception ex) { Toast($"Region change failed: {ex.Message}", "error"); }
        }

        private void HubBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string tag)) return;
            HubDispatch(tag);
        }

        private void HubGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RebuildInspector(dgHubMaterials.SelectedItem as MaterialRow);
        }

        private void HubGrid_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            // priority 8 — Ctrl+click in cell → edit; double-click on read-only → Where-Used.
            try
            {
                if (e?.OriginalSource is DependencyObject src)
                {
                    var cell = FindAncestor<DataGridCell>(src);
                    if (cell != null && !cell.IsReadOnly) return;
                }
            }
            catch (Exception ex) { StingLog.Warn($"HubGrid_DoubleClick: {ex.Message}"); }
            HubDispatch("MAT_WhereUsed");
        }

        // ── Cost / carbon cell-edit handlers (template column path) ────────
        private void CostEdit_LostFocus(object sender, RoutedEventArgs e)   => CommitCellEdit(sender, "Cost");
        private void CarbonEdit_LostFocus(object sender, RoutedEventArgs e) => CommitCellEdit(sender, "kgCO₂e");
        private void CostEdit_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)   { CommitCellEdit(sender, "Cost"); e.Handled = true; }
            else if (e.Key == Key.Escape) RevertCellEdit(sender);
        }
        private void CarbonEdit_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)   { CommitCellEdit(sender, "kgCO₂e"); e.Handled = true; }
            else if (e.Key == Key.Escape) RevertCellEdit(sender);
        }

        private void CommitCellEdit(object sender, string columnHeader)
        {
            try
            {
                if (!(sender is TextBox tb)) return;
                var row = (tb.DataContext as MaterialRow);
                if (row == null || row.Id == null || row.Id.Value <= 0) return;
                var doc = StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document;
                if (doc == null) return;
                var mat = doc.GetElement(row.Id) as Autodesk.Revit.DB.Material;
                if (mat == null) return;
                string raw = tb.Text ?? "";
                MatCellCommitter.Commit(doc, mat, row, columnHeader, raw);
                // Flash the cell border green to confirm.
                FlashCell(tb, true);
                // Re-read the material into the row and refresh the visible cell.
                MaterialNameCache.Invalidate(doc);
                MaterialUsageIndex.Invalidate(doc);
                ReloadSingleRow(row.Id);
                Toast($"Updated {columnHeader} on '{row.Name}'.", "ok");
            }
            catch (Exception ex)
            {
                if (sender is TextBox tb) FlashCell(tb, false);
                Toast($"{columnHeader} edit failed: {ex.Message}", "error");
            }
        }

        private void RevertCellEdit(object sender)
        {
            if (sender is TextBox tb) FlashCell(tb, false);
        }

        private static void FlashCell(TextBox tb, bool ok)
        {
            try
            {
                var brush = new SolidColorBrush(ok
                    ? Color.FromRgb(0x2C, 0xA0, 0x2C)
                    : Color.FromRgb(0xC0, 0x30, 0x30));
                tb.BorderBrush = brush;
                tb.BorderThickness = new Thickness(2);
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                t.Tick += (_, __) =>
                {
                    t.Stop();
                    tb.BorderBrush = Brushes.LightGray;
                    tb.BorderThickness = new Thickness(1);
                };
                t.Start();
            }
            catch { }
        }

        private void ReloadSingleRow(Autodesk.Revit.DB.ElementId id)
        {
            if (_rows == null || id == null) return;
            var doc = StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document;
            if (doc == null) return;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Id?.Value == id.Value)
                {
                    if (doc.GetElement(id) is Autodesk.Revit.DB.Material m)
                        _rows[i] = MaterialRowBuilder.BuildOne(doc, m);
                    break;
                }
            }
            RefreshKpiValues();
        }

        // ── Right-click context menu + keyboard shortcuts ──────────────────
        private void HubGrid_ContextOpening(object sender, ContextMenuEventArgs e)
        {
            var menu = new ContextMenu();
            void Add(string label, string tag)
            {
                var mi = new MenuItem { Header = label };
                mi.Click += (_, __) => HubDispatch(tag);
                menu.Items.Add(mi);
            }
            Add("Apply to Selection",  "MAT_Apply");
            Add("Where Used",          "MAT_WhereUsed");
            Add("Eyedropper from face","MAT_Eyedropper");
            menu.Items.Add(new Separator());
            Add("Edit Identity",       "MAT_EditIdentity");
            Add("Detach asset",        "MAT_DetachAsset");
            Add("Repoint asset to…",   "MAT_RepointAsset");
            menu.Items.Add(new Separator());
            Add("Add to pack…",        "HUB_AddToPack");
            Add("Bookmark",            "HUB_Bookmark");
            Add("Compare with selected","HUB_Compare");
            dgHubMaterials.ContextMenu = menu;
        }

        private void HubGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5)             { Refresh(); e.Handled = true; }
            else if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control) { dgHubMaterials.BeginEdit(); e.Handled = true; }
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { txtGlobalSearch.Focus(); e.Handled = true; }
        }

        // ── Inspector / Nav placeholders — body in MaterialHubPanel.Builders.cs ─
        private static T FindAncestor<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null)
            {
                if (d is T hit) return hit;
                d = VisualTreeHelper.GetParent(d);
            }
            return null;
        }

        // ── Nav tree click → activate filter chip ──────────────────────────
        private void NavTree_Selected(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem tvi && tvi.Tag is FilterChip chip)
            {
                _activeChips.Clear();
                _activeChips.Add(chip);
                RebuildFilterChipBar();
                if (_rows != null) System.Windows.Data.CollectionViewSource.GetDefaultView(_rows).Refresh();
            }
        }

        private void RebuildFilterChipBar()
        {
            filterChipsBar.Items.Clear();
            foreach (var c in _activeChips)
            {
                var b = new Border
                {
                    Background = Brushes.LightSteelBlue,
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6, 2, 4, 2),
                    Margin = new Thickness(2)
                };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock { Text = c.Label, FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
                var x = new Button { Content = "×", Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(4, 0, 0, 0), FontSize = 10, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
                x.Click += (_, __) => { _activeChips.Remove(c); RebuildFilterChipBar(); System.Windows.Data.CollectionViewSource.GetDefaultView(_rows).Refresh(); };
                sp.Children.Add(x);
                b.Child = sp;
                filterChipsBar.Items.Add(b);
            }
        }

        /// <summary>Helper container — a nav-tree node's filter predicate.</summary>
        public class FilterChip
        {
            public string Label { get; set; }
            public Predicate<MaterialRow> Predicate { get; set; }
        }
    }
}
