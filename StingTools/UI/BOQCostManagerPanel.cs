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
using System.Threading.Tasks;
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
        // Slice 1.5 — Materials tab expand state, mirrors _openSections so the
        // Expand-all / Collapse-all toolbar buttons (and per-section chevrons)
        // drive the Materials tab too and survive a rebuild.
        private readonly HashSet<string> _openMaterialSections = new HashSet<string>();
        private string _activeSnapshotLabel = "";
        private readonly Dictionary<string, double> _snapshotDeltas = new Dictionary<string, double>();

        private static string SectionKey(BOQSection sec)
            => $"{sec?.Discipline}|{sec?.NRM2Section}|{sec?.Name}";

        // Phase 108c: toggle hides the NRM2 description RowDetails strip across
        // every section grid — useful when the QS wants a compact price-only view.
        private bool _showNrm2 = true;
        private ToggleButton _nrm2Toggle;

        // P2.2 — active grouping strategy. Changing it triggers a full rebuild
        // (re-aggregation within the new dimension), so the dropdown handler
        // calls RefreshAsync rather than RebuildSectionsView.
        private BoqGroupingMode _groupingMode = BoqGroupingMode.WorkSection;

        // P2.1 / P2.3 — toggleable grid columns the user (or a print profile)
        // has hidden. Empty = show every column. Keyed by canonical column key
        // (Level / Location / Source / Confidence / Carbon / Note).
        private readonly HashSet<string> _hiddenColumns =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _activeProfileId = "";

        // P1.3 — per-project UI state persistence. Grouping mode, display currency,
        // hidden-column set and the expand/collapse sets reset every session unless
        // we persist them to <project>/_BIM_COORD/boq_ui_state.json. Best-effort:
        // a read/write failure logs a warning and never blocks the UI. The flag
        // gates SaveUiState so a write can't fire mid-load (during Build) and
        // clobber the file we're still reading.
        private bool _uiStateLoaded;
        // Set when a persisted OpenSections set was loaded so the first RefreshAsync
        // doesn't auto-expand-all over a deliberately-empty (collapse-all) state.
        private bool _suppressAutoOpenOnce;

        private sealed class BoqUiState
        {
            public string GroupingMode { get; set; }
            public string DisplayCurrency { get; set; }
            public List<string> HiddenColumns { get; set; }
            public List<string> OpenSections { get; set; }
            public List<string> OpenMaterialSections { get; set; }
        }

        private string UiStatePath()
        {
            try
            {
                string parent = System.IO.Path.GetDirectoryName(Doc?.PathName ?? "");
                if (string.IsNullOrEmpty(parent)) return null;   // unsaved doc — no persistence
                return System.IO.Path.Combine(parent, "_BIM_COORD", "boq_ui_state.json");
            }
            catch { return null; }
        }

        /// <summary>Load persisted UI state into the fields before Build() so the
        /// combos/toggles/columns open in the user's last configuration. Best-effort.</summary>
        private void LoadUiState()
        {
            try
            {
                string path = UiStatePath();
                if (path != null && System.IO.File.Exists(path))
                {
                    var st = Newtonsoft.Json.JsonConvert.DeserializeObject<BoqUiState>(
                        System.IO.File.ReadAllText(path));
                    if (st != null)
                    {
                        if (!string.IsNullOrWhiteSpace(st.GroupingMode)
                            && Enum.TryParse<BoqGroupingMode>(st.GroupingMode, out var gm))
                            _groupingMode = gm;
                        if (string.Equals(st.DisplayCurrency, "USD", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(st.DisplayCurrency, "UGX", StringComparison.OrdinalIgnoreCase))
                            _displayCurrency = st.DisplayCurrency.ToUpperInvariant();
                        if (st.HiddenColumns != null)
                        {
                            _hiddenColumns.Clear();
                            foreach (var c in st.HiddenColumns)
                                if (!string.IsNullOrWhiteSpace(c)) _hiddenColumns.Add(c.Trim());
                        }
                        if (st.OpenSections != null)
                        {
                            _openSections.Clear();
                            foreach (var s in st.OpenSections)
                                if (!string.IsNullOrWhiteSpace(s)) _openSections.Add(s);
                            _suppressAutoOpenOnce = true;   // honour a persisted collapse-all
                        }
                        if (st.OpenMaterialSections != null)
                        {
                            _openMaterialSections.Clear();
                            foreach (var s in st.OpenMaterialSections)
                                if (!string.IsNullOrWhiteSpace(s)) _openMaterialSections.Add(s);
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"BOQ LoadUiState: {ex.Message}"); }
            _uiStateLoaded = true;
        }

        /// <summary>Persist the current UI state. Best-effort, fired on each change.</summary>
        private void SaveUiState()
        {
            if (!_uiStateLoaded) return;   // don't write a partial state during Build
            try
            {
                string path = UiStatePath();
                if (path == null) return;
                var st = new BoqUiState
                {
                    GroupingMode = _groupingMode.ToString(),
                    DisplayCurrency = _displayCurrency,
                    HiddenColumns = _hiddenColumns.ToList(),
                    OpenSections = _openSections.ToList(),
                    OpenMaterialSections = _openMaterialSections.ToList()
                };
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllText(path,
                    Newtonsoft.Json.JsonConvert.SerializeObject(st, Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"BOQ SaveUiState: {ex.Message}"); }
        }

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
        // Slice 1.5 — Actions tab master-detail: buttons on the left, a single
        // inline report pane on the right that renders the last-clicked action's
        // result (no popup). Reuses the BOQInlineResults sink via ShowInlineResult.
        private TabItem _actionsTab;
        private Border _actionReportHost;        // right-pane content holder
        private TextBlock _actionReportTitle;    // right-pane header (= action label)
        private UIElement _actionReportEmpty;    // "select an action" placeholder
        private Button _selectedActionBtn;       // for highlight reset
        private Button _linksBtn;                 // header "⛓ Links (N)" badge
        private bool _inlineResultPosted;        // did the current action render inline?
        // Invoked by StingCommandHandler.Execute's finally when a dispatched action
        // completes, so the Actions pane can resolve its "Running…" placeholder even
        // when the command reported via its own TaskDialog (not StingResultPanel).
        internal static Action PendingActionResolve;
        private ToggleButton _ugxToggle, _usdToggle;

        // Slice 1 (5D workspace) — inline result region. Panel-driven actions set
        // the InlineHost=1 ExtraParam so commands route their result here via
        // BOQInlineResults.Post() instead of StingResultPanel.Show(). This is the
        // "zero external reporting popups" convention the rest of the workspace
        // (Schedule / cash-flow / sweep) follows.
        private Border _inlineResultRegion;   // collapsed by default
        private Border _inlineResultHost;     // receives BuildInlineContent output
        private TextBlock _inlineResultTitle;
        private Action<StingResultPanel.Builder> _inlineSink;  // stable delegate for sink (de)registration

        // P2.1 — Schedule / 4D + EVM tab. Phase rows are the tab's own source of
        // truth, persisted to <project>/_BIM_COORD/boq_schedule.json (same
        // convention as boq_ui_state.json). EVM is computed via the reused
        // EvmCalculator engine; the S-curve is drawn on a plain Canvas (no chart lib).
        private TabItem _scheduleTab;
        private System.Collections.ObjectModel.ObservableCollection<BoqSchedulePhase> _schedulePhases
            = new System.Collections.ObjectModel.ObservableCollection<BoqSchedulePhase>();
        private DataGrid _scheduleGrid;
        private Canvas _sCurveCanvas;
        private StackPanel _evmStrip;
        private System.Windows.Controls.TextBox _evmActualsBox;
        private System.Windows.Controls.TextBox _evmAsOfBox;
        private double _scheduleActualToDate;
        private bool _scheduleLoaded;

        public BOQCostManagerPanel(Document doc)
        {
            Doc = doc;
            LoadUiState();   // P1.3 — restore grouping / currency / columns / expand sets before Build
            Build();
            // Register the inline-result sink so panel-driven commands render their
            // results in-panel. Cleared on Unloaded so a closed panel can't capture
            // a later ribbon/workflow invocation (those fall back to .Show()).
            // Hold the delegate in a field — a method-group re-conversion would be a
            // fresh instance, so ReferenceEquals must compare the stored delegate.
            _inlineSink = PostInlineResult;
            BOQInlineResults.Sink = _inlineSink;
            this.Unloaded += (s, e) =>
            {
                if (ReferenceEquals(BOQInlineResults.Sink, _inlineSink))
                    BOQInlineResults.Sink = null;
            };
            RefreshAsync();
        }

        // ── Theming ────────────────────────────────────────────────────────

        // Theme-routed palette: tracks ThemeManager so the panel follows
        // the active theme (Corporate by default — navy header, orange accent).
        private static Brush NavyBrush   => ThemeManager.GetBrush("HeaderBg");
        private static Brush OrangeBrush => ThemeManager.GetBrush("AccentBrush");
        private static Brush GreenBrush  => ThemeManager.GetBrush("SuccessColor");
        private static Brush AmberBrush  => ThemeManager.GetBrush("WarningColor");
        private static Brush RedBrush    => ThemeManager.GetBrush("ErrorColor");
        private static Brush PanelBg     => ThemeManager.GetBrush("PrimaryBg");
        private static Brush HeaderFg    => ThemeManager.GetBrush("HeaderFg");
        private static Brush BorderColor => ThemeManager.GetBrush("BorderColor");

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

            // Phase 184r — load the shared button-style ResourceDictionary
            // so the Actions tab (Phase 184n) can pick up the same
            // GreenBtn / OrangeBtn / BlueBtn / ActionBtn styles the dock
            // panel uses, including theme-aware DynamicResource binding
            // for the neutral palette. Wrapped in try/catch because a
            // missing XAML asset shouldn't break the BOQ panel — the
            // button factory falls back to inline colours when
            // FindResource returns null.
            try
            {
                var dict = new ResourceDictionary
                {
                    Source = new Uri("/StingTools;component/UI/StingButtonStyles.xaml",
                                     UriKind.Relative)
                };
                this.Resources.MergedDictionaries.Add(dict);
            }
            catch (Exception ex) { StingLog.Warn($"Load StingButtonStyles.xaml: {ex.Message}"); }

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

            // Inline result region — sits directly above the footer, collapsed
            // until a panel-driven command posts a result (no popup).
            var resultRegion = BuildInlineResultRegion();
            root.Children.Add(resultRegion);
            DockPanel.SetDock(resultRegion, Dock.Bottom);

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
            // Phase 184n — Actions tab consolidates every cost-management
            // command from P0 → P8 inline. Replaces the fragmented dock-
            // panel sub-sections so users have one place to find every
            // cost workflow.
            _actionsTab = new TabItem { Header = "Actions", Content = BuildActionsTab() };
            _mainTabs.Items.Add(_actionsTab);

            // P2.1 — Schedule (4D programme + cash-flow S-curve + EVM), inline.
            _scheduleTab = new TabItem { Header = "Schedule", Content = BuildScheduleTab() };
            _mainTabs.Items.Add(_scheduleTab);

            // Phase 108j — wrap the main TabControl in a Grid host so
            // ShowTenderSetupInline can stack the tender-setup UI on top,
            // swapping Visibility rather than re-parenting. The footer's
            // ＋/Reconcile/Import/Export/★ buttons stay visible in either
            // mode so the user can cancel back to the BOQ without losing
            // their place in the tabs.
            _contentHost = new Grid();
            _contentHost.Children.Add(_mainTabs);
            root.Children.Add(_contentHost);

            Content = root;
        }

        private Grid _contentHost;
        private FrameworkElement _tenderSetupView;

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
            // P1.3 — reflect the persisted currency (handlers attach AFTER the
            // initializer, so setting IsChecked here doesn't fire RefreshDisplay).
            bool usd = _displayCurrency == "USD";
            _ugxToggle = new ToggleButton { Content = "UGX", IsChecked = !usd, Width = 52, Height = 26, Margin = new Thickness(0, 0, 4, 0) };
            _usdToggle = new ToggleButton { Content = "USD", IsChecked = usd, Width = 52, Height = 26 };
            _ugxToggle.Checked += (s, e) => { _usdToggle.IsChecked = false; _displayCurrency = "UGX"; SaveUiState(); RefreshDisplay(); };
            _usdToggle.Checked += (s, e) => { _ugxToggle.IsChecked = false; _displayCurrency = "USD"; SaveUiState(); RefreshDisplay(); };
            ccyPanel.Children.Add(_ugxToggle);
            ccyPanel.Children.Add(_usdToggle);
            Grid.SetColumn(ccyPanel, 1);
            grid.Children.Add(ccyPanel);

            // Header actions
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center };
            // Per-link takeoff chooser. Persisted per project — flexible (pick
            // which links) + sustainable (survives reopen). Linked rows are
            // read-only (not cost-stamped / selectable in host).
            _linksBtn = BuildHeaderBtn("⛓ Links", () => ChooseLinkedModels());
            actions.Children.Add(_linksBtn);

            // Explicit Refresh forces a full recompute incl. reloaded links — the
            // per-link takeoff cache (P1.2) is only auto-invalidated on document
            // close, so a link Reloaded mid-session would otherwise stay stale.
            // Passive refreshes (filter / rate edit) keep using the cache for speed.
            actions.Children.Add(BuildHeaderBtn("↻ Refresh", () =>
            {
                try { StingTools.BOQ.BOQCostManager.InvalidateLinkCache(); } catch { }
                DispatchAction("BOQRefresh");
            }));
            actions.Children.Add(BuildHeaderBtn("Set Budget", () => ShowBudgetDialog()));
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            return grid;
        }

        /// <summary>
        /// Per-link takeoff chooser: lists the loaded Revit links, pre-ticks the
        /// persisted selection, and on OK saves it (per project) + refreshes.
        /// Reuses the multi-select StingListPicker (checkbox list). Reading links
        /// is read-only — safe from the dockable-pane UI thread.
        /// </summary>
        private void ChooseLinkedModels()
        {
            try
            {
                var doc = Doc;
                if (doc == null) return;

                var loaded = new List<string>();
                try
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var links = new FilteredElementCollector(doc)
                        .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>();
                    foreach (var rli in links)
                    {
                        Document ld = null; try { ld = rli.GetLinkDocument(); } catch { }
                        if (ld == null) continue;                 // unloaded
                        string t; try { t = ld.Title; } catch { continue; }
                        if (!string.IsNullOrWhiteSpace(t) && seen.Add(t)) loaded.Add(t);
                    }
                }
                catch (Exception ex) { StingLog.Warn($"ChooseLinkedModels enum: {ex.Message}"); }

                if (loaded.Count == 0)
                {
                    TaskDialog.Show("STING — Linked models",
                        "No loaded Revit links found in this model.\n\n" +
                        "Link a model and load it (Manage → Links), then choose it here.");
                    return;
                }

                var current = StingTools.BOQ.BOQCostManager.GetIncludedLinkTitles(doc);
                // P2.3 — instance count per link (mirrored / repeated placements).
                var counts = StingTools.BOQ.BOQCostManager.CountLinkInstancesByTitle(doc);
                var items = loaded
                    .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                    .Select(t =>
                    {
                        counts.TryGetValue(t, out int n);
                        string detail = (n > 1 ? $"{n} instances" : "1 instance");
                        if (current.Contains(t)) detail += " · included";
                        return new StingTools.Select.StingListPicker.ListItem
                        {
                            Label = t,
                            Detail = detail,
                            IsSelected = current.Contains(t)
                        };
                    })
                    .ToList();

                var picked = StingTools.Select.StingListPicker.Show(
                    "STING — Linked models in takeoff",
                    "Tick the links whose quantities to include. Linked rows are read-only — " +
                    "not cost-stamped or selectable in the host (tagged \"[Linked: <model>]\").",
                    items, allowMultiSelect: true);
                if (picked == null) return;   // cancelled — leave selection unchanged

                var pickedTitles = picked.Select(p => p.Label).ToList();
                StingTools.BOQ.BOQCostManager.SetIncludedLinkTitles(doc, pickedTitles);

                // P2.3 — for included links placed more than once, offer a per-link
                // ×N opt-in (default off — a shared reference model placed once is the
                // common case; a mirrored wing placed twice is the exception).
                var multiCandidates = pickedTitles
                    .Where(t => counts.TryGetValue(t, out int n) && n > 1)
                    .OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
                if (multiCandidates.Count > 0)
                {
                    var mult = StingTools.BOQ.BOQCostManager.GetLinkMultiplyMap(doc);
                    var multItems = multiCandidates.Select(t =>
                    {
                        counts.TryGetValue(t, out int n);
                        bool on = mult.TryGetValue(t, out bool v) && v;
                        return new StingTools.Select.StingListPicker.ListItem
                        { Label = t, Detail = $"placed {n}× — take off ×{n}", IsSelected = on };
                    }).ToList();
                    var multPicked = StingTools.Select.StingListPicker.Show(
                        "STING — Multiply repeated links",
                        "These included links are placed more than once. Tick a link to multiply its " +
                        "quantities (and cost / carbon) by its instance count — for genuinely repeated " +
                        "geometry like mirrored wings. Leave unticked for shared reference models.",
                        multItems, allowMultiSelect: true);
                    if (multPicked != null)
                    {
                        var newMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                        var onSet = new HashSet<string>(multPicked.Select(p => p.Label), StringComparer.OrdinalIgnoreCase);
                        foreach (var t in multiCandidates) newMap[t] = onSet.Contains(t);
                        StingTools.BOQ.BOQCostManager.SetLinkMultiplyMap(doc, newMap);
                    }
                }

                // No link-cache invalidation needed: the multiplier is applied
                // post-cache (on the cloned raw rows) in CollectLinkedItems, so a
                // selection / ×N change takes effect on the next refresh without
                // re-walking the linked Revit DBs.
                UpdateLinksBadge();
                DispatchAction("BOQRefresh");
            }
            catch (Exception ex) { StingLog.Error("BOQ ChooseLinkedModels", ex); }
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
                // Slice 1.5 — also expand every Materials category so the button
                // works on whichever tab is active (mirrors the BOQ set pattern).
                _openMaterialSections.Clear();
                foreach (var k in MaterialCategoryKeys()) _openMaterialSections.Add(k);
                SaveUiState();
                RebuildSectionsView();
                RebuildMaterialsTab();
            }));
            sp.Children.Add(MakeToolbarButton("⊟ Collapse all", () =>
            {
                _openSections.Clear();
                _openMaterialSections.Clear();
                SaveUiState();
                RebuildSectionsView();
                RebuildMaterialsTab();
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

            // P2.2 — grouping mode selector. Reorganises the bill (work section
            // / level / zone / location). Full rebuild so re-aggregation runs.
            sp.Children.Add(new TextBlock { Text = "Group:", VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 0, 4, 0) });
            var groupCombo = new System.Windows.Controls.ComboBox
            {
                Width = 150, Height = 24, FontSize = 11, VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "How the bill is grouped into sections"
            };
            foreach (var opt in new[]
            {
                ("Work section (NRM2)", BoqGroupingMode.WorkSection),
                ("Level",               BoqGroupingMode.Level),
                ("Level → work section",BoqGroupingMode.LevelThenWorkSection),
                ("Zone",                BoqGroupingMode.Zone),
                ("Location",            BoqGroupingMode.Location),
                ("Source model",        BoqGroupingMode.SourceModel),
            })
                groupCombo.Items.Add(new ComboBoxItem { Content = opt.Item1, Tag = opt.Item2 });
            // P1.3 — preselect the persisted grouping (before the handler attaches, so
            // it doesn't fire a redundant RefreshAsync during Build).
            var preSel = groupCombo.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(ci => ci.Tag is BoqGroupingMode gm && gm == _groupingMode);
            groupCombo.SelectedItem = preSel ?? groupCombo.Items[0];
            groupCombo.SelectionChanged += (s, e) =>
            {
                if (groupCombo.SelectedItem is ComboBoxItem ci && ci.Tag is BoqGroupingMode m && m != _groupingMode)
                {
                    _groupingMode = m;
                    _openSections.Clear();   // section keys change — let RefreshAsync re-open all
                    SaveUiState();
                    RefreshAsync();          // re-aggregate + re-group from the model
                }
            };
            sp.Children.Add(groupCombo);

            // P2.1 — column visibility menu (Level / Location / Source / Conf /
            // CO₂ / Note). Lightweight checkable dropdown; rebuilds the grids.
            var colsBtn = new Button
            {
                Content = "▦ Columns", FontSize = 10, Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(8, 0, 0, 0), Background = Brushes.Transparent,
                BorderThickness = new Thickness(0.5), BorderBrush = new SolidColorBrush(Color.FromRgb(180, 190, 200)),
                Cursor = Cursors.Hand
            };
            colsBtn.Click += (s, e) => ShowColumnsMenu(colsBtn);
            sp.Children.Add(colsBtn);

            // P2.3 — print / export column profile selector. Sets which
            // toggleable columns are visible in one click.
            sp.Children.Add(new TextBlock { Text = "Profile:", VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 4, 0) });
            var profileCombo = new System.Windows.Controls.ComboBox
            {
                Width = 160, Height = 24, FontSize = 11, VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Named visible-column set (corporate + project profiles). " +
                          "Export a tender bill via Professional Export; the full round-trip via BOQ Export."
            };
            try
            {
                var reg = BoqPrintProfileRegistry.Get(Doc);
                profileCombo.Items.Add(new ComboBoxItem { Content = "— all columns —", Tag = "" });
                foreach (var p in reg.Profiles)
                    profileCombo.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
            }
            catch (Exception ex) { StingLog.Warn($"BOQ profile combo: {ex.Message}"); }
            profileCombo.SelectedIndex = 0;
            profileCombo.SelectionChanged += (s, e) =>
            {
                if (profileCombo.SelectedItem is ComboBoxItem ci) ApplyPrintProfile(ci.Tag as string);
            };
            sp.Children.Add(profileCombo);

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

        // P2.1 — checkable column-visibility dropdown. Toggling a column flips
        // its key in _hiddenColumns and rebuilds every section grid.
        private void ShowColumnsMenu(UIElement anchor)
        {
            var menu = new ContextMenu();
            foreach (var col in BoqPrintProfileRegistry.ToggleableColumns)
            {
                string key = col;
                var mi = new MenuItem
                {
                    Header = ColumnDisplayName(key),
                    IsCheckable = true,
                    IsChecked = !_hiddenColumns.Contains(key),
                    StaysOpenOnClick = true
                };
                mi.Click += (s, e) =>
                {
                    if (mi.IsChecked) _hiddenColumns.Remove(key); else _hiddenColumns.Add(key);
                    SaveUiState();
                    RebuildSectionsView();
                };
                menu.Items.Add(mi);
            }
            menu.PlacementTarget = anchor;
            menu.IsOpen = true;
        }

        private static string ColumnDisplayName(string key)
        {
            switch (key)
            {
                case "Carbon":     return "CO₂";
                case "Source":     return "Source";
                case "Confidence": return "Confidence";
                default:           return key;
            }
        }

        // P2.3 — apply a named print profile: its visible-column set becomes the
        // grid's column visibility. Empty id = show every column.
        private void ApplyPrintProfile(string profileId)
        {
            _activeProfileId = profileId ?? "";
            _hiddenColumns.Clear();
            if (!string.IsNullOrEmpty(_activeProfileId))
            {
                try
                {
                    var profile = BoqPrintProfileRegistry.Get(Doc).GetById(_activeProfileId);
                    foreach (var hidden in BoqPrintProfileRegistry.HiddenColumnsFor(profile))
                        _hiddenColumns.Add(hidden);
                }
                catch (Exception ex) { StingLog.Warn($"ApplyPrintProfile({profileId}): {ex.Message}"); }
            }
            SaveUiState();   // P1.3 — a profile sets the visible-column set; persist it
            RebuildSectionsView();
        }

        // ══════════════════════════════════════════════════════════════════
        //  Phase 184n — Actions tab: every P0 → P8 cost command inline.
        //  Grouped by phase / workflow so a QS finds the right action
        //  without remembering the global command catalogue.
        // ══════════════════════════════════════════════════════════════════

        private UIElement BuildActionsTab()
        {
            var sp = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            sp.Children.Add(new TextBlock
            {
                Text = "Cost Management Actions",
                FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = NavyBrush, Margin = new Thickness(0, 0, 0, 4)
            });
            sp.Children.Add(new TextBlock
            {
                Text = "Every cost workflow (P0 → P8) in one place. " +
                       "Hover for a tooltip describing each action.",
                FontSize = 11, Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 14)
            });

            sp.Children.Add(BuildActionGroup("QS ROUND-TRIP (P3)",
                "Hand the bill to a Quantity Surveyor in Excel and bring priced rates back. " +
                "Rows carry a stable hidden key so rates land on the right rows after a rebuild.",
                new[]
                {
                    ("★ Export QS Bill", "BOQQsExport",
                     "Export the bill in NRM2 trade order (Ref/Description/Unit/Qty/Rate/Amount) with section " +
                     "collections + grand summary. Choose priced or unpriced.", true),
                    ("Import QS Bill", "BOQQsImport",
                     "Re-import the QS's priced workbook. Shows a diff (rate changes / new rows / model-qty drift) " +
                     "before applying. Model quantities preserved; QS rates land as RateSource=QS.", false),
                    ("Rate Gap Report", "BOQ_RateGapReport",
                     "List every modelled item that still needs a price (no rate / low confidence / defaulted), the " +
                     "value at risk, and a CSV worklist to hand the QS. Read-only.", false),
                    ("Add Manual / PS / Daywork", "BOQAddManualRow",
                     "Author a row the model can't carry (manual measured / provisional sum / dayworks / PC sum). " +
                     "Survives a model rebuild.", false),
                }));

            sp.Children.Add(BuildActionGroup("AUTOMATION (P2)",
                "Run workflows, validate the model and toggle the stale-cost detector.",
                new[]
                {
                    ("★ Run Cost Workflow", "Cost_RunWorkflow",
                     "Pick and run a WORKFLOW_BOQ_*.json preset (Full Refresh / Quick Valuation / Tender Pack)", true),
                    ("Validate Cost",        "Cost_ValidateAll",
                     "Run the 5-validator chain (missing material / untyped category / unpriced PROD / zero qty / stale)", false),
                    ("Clear Stale Flags",    "Cost_ClearStale",
                     "Reset ASS_CST_STALE_BOOL on every element after a successful BOQ build", false),
                    ("Toggle Stale Marker",  "Cost_ToggleStaleMarker",
                     "Enable/disable the IUpdater that flags BOQ rows as stale on geometry change", false),
                    ("Reload Rules",         "Cost_ReloadRules",
                     "Invalidate rate provider / take-off rule / ICMS3 phase / CostStamp / default-rates caches", false),
                    ("Migrate UGX → Neutral","Cost_MigrateCurrencyParams",
                     "One-shot: copy legacy CST_UNIT_RATE_UGX into currency-neutral params + FX stamp", false),
                    ("Migrate ES v1 → v2",   "Cost_MigrateESEntities",
                     "Bulk-migrate v1 Extensible Storage cost overrides to v2 (waste / OH / profit / lock)", false),
                }));

            sp.Children.Add(BuildActionGroup("COST PLAN — NRM1 (P4)",
                "Elemental cost planning for RIBA 1-3 — £/m² GIFA benchmarks per building type.",
                new[]
                {
                    ("★ New Cost Plan",  "CostPlan_Create",
                     "Mint a PERT 3-point cost plan from a building-type benchmark set", true),
                    ("Compare vs BOQ",   "CostPlan_Compare",
                     "Variance report — NRM1 cost plan vs live BOQ totals, RAG-coded per element", false),
                    ("Export Cost Plan", "CostPlan_Export",
                     "Export the active cost plan to xlsx (full NRM1 breakdown + totals)", false),
                }));

            sp.Children.Add(BuildActionGroup("PAYMENT CERTS (P5.1)",
                "Monthly interim certificates per JCT 2024 / NEC4 / FIDIC 2017.",
                new[]
                {
                    ("Set % Complete",   "PaymentCert_SetProgress",
                     "Stamp % complete on a BOQ section's elements (ASS_PMT_PCT_COMPLETE_NR) — the input for Issue Cert + EVM.", false),
                    ("★ Issue Cert",     "PaymentCert_Issue",
                     "Build a draft interim cert from current BOQ + weighted % complete. Retention auto-halves.", true),
                    ("Cert Document",    "PaymentCert_ExportDoc",
                     "Render a numbered interim certificate as a formatted XLSX (SOV + retention/MOS/previous/net/VAT/payable + signatures).", false),
                    ("Approve Cert",     "PaymentCert_Approve",
                     "Advance the cert state machine — Draft → Issued or Issued → Agreed", false),
                    ("Cert Register",    "PaymentCert_Register",
                     "Export CSV register of every cert (gross / retention / payable / signers / cumulative)", false),
                }));

            sp.Children.Add(BuildActionGroup("VARIATIONS + STAR RATES (P5.2)",
                "Change-order tracking with auto-mint from snapshot diffs and first-principles star rates.",
                new[]
                {
                    ("★ Variation from Diff","Variation_FromDiff",
                     "Mint a draft VO from a BOQSnapshotDiff. Numbered VO-AI / VO-CE / VO-EI / VO-CC.", true),
                    ("Star Rate Build-Up",   "Variation_BuildStarRate",
                     "Author a star rate from first principles — labour + plant + materials + OH + profit", false),
                    ("VO Register",          "Variation_ExportRegister",
                     "Export all variations to CSV (number / status / value / signers)", false),
                    ("Reclassify Legacy",    "Variation_ReclassifyLegacy",
                     "Walk legacy variations still on default Other / Employer and set their reason + liability via multi-select picker (Phase 184p)", false),
                }));

            sp.Children.Add(BuildActionGroup("EARNED VALUE MGMT (P5.3)",
                "PMI EVM metrics — BCWS / BCWP / ACWP, CPI, SPI, EAC, ETC, VAC, TCPI.",
                new[]
                {
                    ("★ Calculate EVM", "Evm_Calculate",
                     "Compute every PMI metric with Green/Amber/Red gates at CPI 0.95 / 1.00", true),
                    ("Import Actuals",  "Evm_ImportActuals",
                     "Sum the latest actuals CSV under _bim_manager/actuals/", false),
                    ("Export S-Curve",  "Evm_ExportReport",
                     "CSV of every EVM period — drives an S-curve in your favourite chart tool", false),
                }));

            sp.Children.Add(BuildActionGroup("COST REPORT (P4.4)",
                "Anticipated final cost — where the project is heading vs budget.",
                new[]
                {
                    ("★ Anticipated Final Cost", "Cost_AnticipatedFinalCost",
                     "Modelled works + manual/PS allowances + agreed variations + pending variations → AFC vs budget. On screen + XLSX.", true),
                    ("Reconcile Provisionals", "ReconcileProvisionals",
                     "Match provisional sums to modelled elements and reconcile against outturn (final account).", false),
                }));

            sp.Children.Add(BuildActionGroup("MEASUREMENT STANDARD (P6)",
                "Switch the active standard. Affects how rows are classified and described.",
                new[]
                {
                    ("Set Standard",     "Cost_SetMeasurementStandard",
                     "Pick the active standard — NRM2 / CESMM4 / POMI / ICMS3 / MMHW", false),
                    ("Standard Preview", "Cost_StandardInspect",
                     "Diagnostic preview — how each standard classifies common categories", false),
                }));

            sp.Children.Add(BuildActionGroup("IFC + ICMS3 (P8)",
                "External tool round-trip and cost-plus-carbon ledger.",
                new[]
                {
                    ("Stamp IFC Qto",    "Cost_StampIfcQuantities",
                     "Populate IFC4 Qto_*BaseQuantities + Pset_StingCost so Cost-X / CostOS / Candy / Bluebeam Revu can read cost direct from IFC", false),
                    ("★ ICMS3 Report",   "Cost_ExportIcms3Report",
                     "Export ICMS3 cost + carbon ledger — £ + kgCO₂e + £/kgCO₂e per ICMS group", true),
                }));

            sp.Children.Add(BuildActionGroup("QS SIGN-OFF (G9)",
                "Record a Quantity Surveyor's verification. Until signed, every export is marked DRAFT.",
                new[]
                {
                    ("Record QS Sign-off", "BOQ_SignOff",
                     "Record the QS name + role against the current snapshot. Clears the DRAFT mark on exports of that signed snapshot.", false),
                }));

            var leftRail = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = sp,
                Padding = new Thickness(0)
            };

            // Slice 1.5 — master-detail: action buttons on the left, one inline
            // report pane on the right rendering the last-clicked action's result
            // (no popup). A draggable splitter lets the user size the panes.
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star), MinWidth = 360 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 280 });

            Grid.SetColumn(leftRail, 0);
            grid.Children.Add(leftRail);

            var splitter = new GridSplitter
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = BorderColor,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext
            };
            Grid.SetColumn(splitter, 1);
            grid.Children.Add(splitter);

            grid.Children.Add(BuildActionReportPane());   // sets _actionReportHost/Title; column 2
            return grid;
        }

        /// <summary>Slice 1.5 — the Actions tab's right-hand inline report pane.
        /// Header (= action label) + scrollable content host seeded with an
        /// empty-state hint. Populated by RunActionInline / ShowInlineResult.</summary>
        private UIElement BuildActionReportPane()
        {
            var root = new DockPanel { LastChildFill = true, Margin = new Thickness(8, 0, 0, 0) };

            var header = new Border
            {
                Background = NavyBrush,
                Padding = new Thickness(12, 8, 12, 8),
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = BorderColor
            };
            _actionReportTitle = new TextBlock
            {
                Text = "Action result", Foreground = Brushes.White,
                FontSize = 12, FontWeight = FontWeights.Bold
            };
            header.Child = _actionReportTitle;
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            _actionReportEmpty = new TextBlock
            {
                Text = "Select an action on the left — its result appears here, inline (no popup).",
                Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(14), VerticalAlignment = VerticalAlignment.Top
            };
            _actionReportHost = new Border
            {
                Background = Brushes.White, Padding = new Thickness(0),
                Child = _actionReportEmpty
            };
            root.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _actionReportHost
            });

            Grid.SetColumn(root, 2);
            return root;
        }

        /// <summary>
        /// One titled action-group. Title row + caption + WrapPanel of
        /// action buttons. Star-marked headlines get the headline style
        /// (filled GreenBtn-equivalent + bold) to lead the eye.
        /// </summary>
        private UIElement BuildActionGroup(string title, string caption,
            (string label, string tag, string tooltip, bool headline)[] actions)
        {
            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = BorderColor,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(14, 10, 14, 12),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = title, FontSize = 12, FontWeight = FontWeights.Bold,
                Foreground = NavyBrush, Margin = new Thickness(0, 0, 0, 2)
            });
            sp.Children.Add(new TextBlock
            {
                Text = caption, FontSize = 10, Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 10)
            });
            var wp = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var (label, tag, tooltip, headline) in actions)
                wp.Children.Add(BuildActionButton(label, tag, tooltip, headline));
            sp.Children.Add(wp);
            card.Child = sp;
            return card;
        }

        /// <summary>
        /// A single inline action button that fires the same dispatch
        /// path as the dock-panel buttons. Phase 184r — prefers the
        /// shared StingButtonStyles.xaml resources (GreenBtn for
        /// headline, ActionBtn for neutral) so theme switching flows
        /// through. Falls back to inline colour literals when the
        /// dictionary failed to load.
        /// </summary>
        private Button BuildActionButton(string label, string tag, string tooltip, bool headline)
        {
            var btn = new Button
            {
                Content = label,
                Tag = tag,
                ToolTip = tooltip,
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand
            };

            // Try the shared resource dictionary first.
            Style style = null;
            try { style = TryFindResource(headline ? "GreenBtn" : "ActionBtn") as Style; }
            catch (Exception ex) { StingLog.Warn($"BuildActionButton FindResource: {ex.Message}"); }

            if (style != null)
            {
                btn.Style = style;
                // Style sets neutral FontSize 10 + Height 26; bump the
                // Actions tab to slightly larger so the click target is
                // comfortable.
                btn.FontSize = 11;
                btn.MinHeight = 30;
                btn.FontWeight = headline ? FontWeights.Bold : FontWeights.SemiBold;
            }
            else
            {
                // Fallback — inline literals (same colours as before).
                btn.FontSize = 11;
                btn.FontWeight = headline ? FontWeights.Bold : FontWeights.SemiBold;
                btn.Background = headline
                    ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))      // GreenBtn
                    : new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xF8));     // neutral
                btn.Foreground = headline ? Brushes.White : NavyBrush;
                btn.BorderBrush = headline
                    ? new SolidColorBrush(Color.FromRgb(0x38, 0x8E, 0x3C))
                    : BorderColor;
                btn.BorderThickness = new Thickness(1);
            }

            btn.Click += (s, e) => RunActionInline(btn, label, tag);
            return btn;
        }

        /// <summary>
        /// Slice 1.5 — run an Actions-tab command and route its result to the
        /// right-hand report pane instead of a popup. Highlights the clicked
        /// button, shows a running placeholder, and sets InlineHost=1 so commands
        /// that support the inline gate post here (BOQInlineResults →
        /// ShowInlineResult → the Actions pane). Commands not yet converted still
        /// pop their own dialog (that's the Slice 3 sweep); the pane still
        /// responds per-button so the surface is never dead.
        /// </summary>
        private void RunActionInline(Button btn, string label, string tag)
        {
            try
            {
                HighlightActionButton(btn);
                if (_actionReportTitle != null) _actionReportTitle.Text = label;

                // P2.2 — input-heavy actions render a single inline editable form
                // first; on Run the form sets the command's ExtraParams and then
                // dispatches. Actions without a form dispatch directly (the
                // command's own inline pickers / result still render in the pane).
                if (TryShowInlineFormFor(label, tag)) return;

                DispatchInline(label, tag);
            }
            catch (Exception ex) { StingLog.Error("BOQ RunActionInline", ex); }
        }

        /// <summary>
        /// Set up the inline pickers/result sinks and dispatch a command into the
        /// Actions report pane (no popup). Shared by the direct-dispatch path and
        /// the inline-form Run path. Caller has already set the report title.
        /// </summary>
        private void DispatchInline(string label, string tag)
        {
            try
            {
                // P0.2 — defensively clear any stale inline registration left over by
                // a prior aborted run (command that threw before the dispatcher's
                // finally cleared the statics) so we never inherit a foreign sink.
                StingTools.Select.StingListPicker.InlineHost = null;
                StingTools.Select.StingListPicker.InlineHostDoc = null;
                StingTools.Select.StingListPicker.InlineTitleSink = null;
                StingResultPanel.InlineSink = null;

                if (_actionReportHost != null)
                    _actionReportHost.Child = new TextBlock
                    {
                        Text = $"Running “{label}” …\nThe result appears here when the action completes.",
                        Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(14)
                    };
                // Slice 1.5 — route this command's input pickers AND result panels
                // into the Actions report pane instead of popups. The dispatcher
                // clears these in StingCommandHandler.Execute's finally, once the
                // (synchronous) command has run.
                _inlineResultPosted = false;   // set true by the sink if a result lands inline
                StingTools.Select.StingListPicker.InlineHost = _actionReportHost;
                StingTools.Select.StingListPicker.InlineHostDoc = Doc;   // P0.1 — txn-state guard target
                StingTools.Select.StingListPicker.InlineTitleSink = t => { if (_actionReportTitle != null) _actionReportTitle.Text = t; };
                StingResultPanel.InlineSink = b => { _inlineResultPosted = true; ShowInlineResult(b); };

                // Resolve the "Running…" placeholder when the command finishes. Some
                // actions still report via their own TaskDialog (Slice-3 conversion
                // pending) — without this the pane would sit on "Running…" forever
                // and look broken. The dispatcher invokes + clears this in its finally.
                string lbl = label;
                PendingActionResolve = () => ResolveActionPane(lbl);

                StingCommandHandler.SetExtraParam("InlineHost", "1");
                DispatchAction(tag);
            }
            catch (Exception ex) { StingLog.Error("BOQ DispatchInline", ex); }
        }

        // ── P2.2 — reusable inline editable form host ───────────────────────

        private enum BoqFormKind { Text, Number, Combo, Check }

        private class BoqFormField
        {
            public string Key;
            public string Label;
            public BoqFormKind Kind = BoqFormKind.Text;
            public string Default = "";
            public List<(string display, string value)> Options;  // Combo only
            public System.Windows.Controls.Control Control;   // runtime — set by ShowInlineForm
        }

        /// <summary>
        /// P2.2 — render a titled editable form (labelled rows of TextBox /
        /// numeric / ComboBox / CheckBox) + a Run button into the Actions report
        /// pane. On Run the field values are passed to <paramref name="applyExtraParams"/>
        /// (which sets the command's ExtraParams), then the command is dispatched
        /// inline. No popup. Switches to the Actions tab so the form is visible
        /// even when invoked from another surface (e.g. the budget strip).
        /// </summary>
        private void ShowInlineForm(string label, string tag, List<BoqFormField> fields,
            Action<Func<string, string>> applyExtraParams)
        {
            if (_actionReportHost == null) return;
            try
            {
                if (_mainTabs != null && _actionsTab != null) _mainTabs.SelectedItem = _actionsTab;
                if (_actionReportTitle != null) _actionReportTitle.Text = label;

                var sp = new StackPanel { Margin = new Thickness(14) };
                sp.Children.Add(new TextBlock
                {
                    Text = label, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = NavyBrush,
                    Margin = new Thickness(0, 0, 0, 10)
                });

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                for (int i = 0; i < fields.Count; i++)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var f = fields[i];
                    var lbl = new TextBlock { Text = f.Label, FontSize = 11, Foreground = NavyBrush,
                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
                    Grid.SetRow(lbl, i); Grid.SetColumn(lbl, 0); grid.Children.Add(lbl);

                    System.Windows.Controls.Control ctl;
                    switch (f.Kind)
                    {
                        case BoqFormKind.Check:
                            ctl = new CheckBox { IsChecked = f.Default == "1" || f.Default == "true",
                                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
                            break;
                        case BoqFormKind.Combo:
                            var cb = new System.Windows.Controls.ComboBox { Height = 26, FontSize = 11, Margin = new Thickness(0, 4, 0, 4) };
                            foreach (var (display, value) in f.Options ?? new List<(string, string)>())
                                cb.Items.Add(new ComboBoxItem { Content = display, Tag = value });
                            cb.SelectedIndex = 0;
                            ctl = cb;
                            break;
                        default:
                            ctl = new TextBox { Text = f.Default ?? "", Height = 26, FontSize = 11,
                                Margin = new Thickness(0, 4, 0, 4) };
                            break;
                    }
                    f.Control = ctl;
                    Grid.SetRow(ctl, i); Grid.SetColumn(ctl, 1); grid.Children.Add(ctl);
                }
                sp.Children.Add(grid);

                var runBtn = new Button
                {
                    Content = "Run", FontSize = 12, FontWeight = FontWeights.Bold, Height = 30, MinWidth = 100,
                    Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
                    Background = GreenBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                runBtn.Click += (s, e) =>
                {
                    try
                    {
                        Func<string, string> get = key =>
                        {
                            var f = fields.FirstOrDefault(x => x.Key == key);
                            if (f?.Control == null) return "";
                            switch (f.Kind)
                            {
                                case BoqFormKind.Check: return (f.Control as CheckBox)?.IsChecked == true ? "1" : "0";
                                case BoqFormKind.Combo: return ((f.Control as System.Windows.Controls.ComboBox)?.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                                default: return (f.Control as TextBox)?.Text ?? "";
                            }
                        };
                        applyExtraParams(get);
                        DispatchInline(label, tag);
                    }
                    catch (Exception ex) { StingLog.Error($"BOQ inline form Run '{label}'", ex); }
                };
                sp.Children.Add(runBtn);

                _actionReportHost.Child = sp;
            }
            catch (Exception ex) { StingLog.Error($"BOQ ShowInlineForm '{label}'", ex); }
        }

        /// <summary>
        /// P2.2 — render an inline form for the input-heavy actions that were
        /// converted from picker chains / dialogs. Returns true when a form was
        /// shown (caller must not also dispatch); false to dispatch directly.
        /// </summary>
        private bool TryShowInlineFormFor(string label, string tag)
        {
            switch (tag)
            {
                case "PaymentCert_SetProgress":
                {
                    var secOpts = new List<(string, string)> { ("ALL sections", "ALL") };
                    if (_boq != null)
                        foreach (var s in _boq.Sections)
                            secOpts.Add((string.IsNullOrEmpty(s.NRM2Section) ? s.Name : $"§{s.NRM2Section}  {s.Name}",
                                         string.IsNullOrEmpty(s.NRM2Section) ? s.Name : s.NRM2Section));
                    ShowInlineForm(label, tag, new List<BoqFormField>
                    {
                        new BoqFormField { Key = "PmtSection", Label = "Section", Kind = BoqFormKind.Combo, Options = secOpts },
                        new BoqFormField { Key = "PmtPercent", Label = "% complete", Kind = BoqFormKind.Number, Default = "0" },
                    }, get =>
                    {
                        StingCommandHandler.SetExtraParam("PmtSection", get("PmtSection"));
                        StingCommandHandler.SetExtraParam("PmtPercent", get("PmtPercent"));
                    });
                    return true;
                }
                case "BOQ_SignOff":
                {
                    ShowInlineForm(label, tag, new List<BoqFormField>
                    {
                        new BoqFormField { Key = "SignOffBy", Label = "QS name", Kind = BoqFormKind.Text },
                        new BoqFormField { Key = "SignOffRole", Label = "Role", Kind = BoqFormKind.Text, Default = "Quantity Surveyor" },
                        new BoqFormField { Key = "SignOffScope", Label = "Scope (optional)", Kind = BoqFormKind.Text },
                    }, get =>
                    {
                        StingCommandHandler.SetExtraParam("SignOffBy", get("SignOffBy"));
                        StingCommandHandler.SetExtraParam("SignOffRole", get("SignOffRole"));
                        StingCommandHandler.SetExtraParam("SignOffScope", get("SignOffScope"));
                    });
                    return true;
                }
                case "BOQSetBudget":
                {
                    string current = _boq?.ProjectBudgetUGX.ToString("F0", CultureInfo.InvariantCulture) ?? "0";
                    ShowInlineForm(label, tag, new List<BoqFormField>
                    {
                        new BoqFormField { Key = "ProjectBudgetUgx", Label = "Project budget (UGX)", Kind = BoqFormKind.Number, Default = current },
                    }, get =>
                    {
                        string raw = get("ProjectBudgetUgx");
                        if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double bud))
                            StingCommandHandler.SetExtraParam("ProjectBudgetUgx", bud.ToString("F0", CultureInfo.InvariantCulture));
                    });
                    return true;
                }
            }
            return false;
        }

        private void HighlightActionButton(Button btn)
        {
            try
            {
                if (_selectedActionBtn != null && !ReferenceEquals(_selectedActionBtn, btn))
                {
                    _selectedActionBtn.BorderBrush = BorderColor;
                    _selectedActionBtn.BorderThickness = new Thickness(1);
                }
                _selectedActionBtn = btn;
                btn.BorderBrush = NavyBrush;
                btn.BorderThickness = new Thickness(2);
            }
            catch { /* highlight is cosmetic — never break a dispatch */ }
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

            // Slice 1 — QTO IFC export (estimator feed). Routes its result inline
            // via the InlineHost convention rather than a StingResultPanel window.
            var qtoBtn = BuildActionBtn("⛁ QTO IFC", NavyBrush);
            qtoBtn.ToolTip = "Export an IFC4 file with base quantities + Pset_StingCost "
                + "(NRM2 / unit rate) so an external estimator (CostX / iTWO / Candy) can "
                + "ingest measured quantities. Result shows inline below — no popup.";
            qtoBtn.Click += (s, e) => ExportQtoIfcInline();
            right.Children.Add(qtoBtn);

            var tenderBtn = BuildActionBtn("★ Tender BOQ", OrangeBrush);
            tenderBtn.ToolTip = "Export a tender-grade NRM2 Bill of Quantities with cover, document control, "
                + "preliminaries, preambles, bills, collections and grand summary — QS presentation format.";
            right.Children.Add(tenderBtn);

            foreach (UIElement ch in right.Children)
            {
                if (ch is Button b)
                {
                    string caption = b.Content as string ?? "";
                    if (caption.StartsWith("＋")) b.Click += (s, e) => AddManualRow();
                    else if (caption.StartsWith("Reconcile")) b.Click += (s, e) => DispatchAction("ReconcileProvisionals");
                    else if (caption.StartsWith("Import")) b.Click += (s, e) => DispatchAction("BOQImport");
                    else if (caption.StartsWith("Export")) b.Click += (s, e) => DispatchAction("BOQExport");
                    else if (caption.StartsWith("★")) b.Click += (s, e) => ShowTenderSetupInline();
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
                _boq = BOQCostManager.BuildBOQDocument(Doc, null, _groupingMode);
                _health = BOQCostManager.ComputeBOQHealth(_boq);
                LoadSnapshotDropdown();
                // On first load open every section; subsequent refreshes preserve
                // state. P1.3 — suppress the auto-open-all once, so a persisted
                // "collapse all" (empty OpenSections) isn't immediately re-expanded.
                if (_openSections.Count == 0 && _boq != null && !_suppressAutoOpenOnce)
                    foreach (var s in _boq.Sections) _openSections.Add(SectionKey(s));
                _suppressAutoOpenOnce = false;
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
            UpdateLinksBadge();
            if (_boq == null) return;
            RebuildSectionsView();
            RebuildMaterialsTab();
            RefreshScheduleTab();   // P2.1 — cheap recompute (planned cost share + EVM + S-curve)
        }

        /// <summary>Show the count of included links on the header button —
        /// "⛓ Links" when none, "⛓ Links (N)" when N are in the takeoff.</summary>
        private void UpdateLinksBadge()
        {
            if (_linksBtn == null) return;
            int n = 0;
            try { if (Doc != null) n = StingTools.BOQ.BOQCostManager.GetIncludedLinkTitles(Doc).Count; } catch { }
            _linksBtn.Content = n > 0 ? $"⛓ Links ({n})" : "⛓ Links";
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
                    // Spatial bills (blank NRM2 §) get a location glyph instead.
                    Text = string.IsNullOrWhiteSpace(sec.NRM2Section) ? "▣" : $"§{sec.NRM2Section}",
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
            expander.Expanded  += (s, e) => { _openSections.Add(capturedKey); SaveUiState(); };
            expander.Collapsed += (s, e) => { _openSections.Remove(capturedKey); SaveUiState(); };
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

            // P2.1 — toggleable columns. Hidden ones (via the Columns menu or a
            // print profile) are simply not added; grids rebuild on toggle.
            void AddIfVisible(string key, DataGridColumn col)
            {
                if (!_hiddenColumns.Contains(key)) grid.Columns.Add(col);
            }

            AddIfVisible("Level", new DataGridTextColumn
            {
                Header = "Level", Binding = new Binding(nameof(BOQItemViewModel.LevelDisplay)),
                Width = new DataGridLength(95), IsReadOnly = true
            });
            AddIfVisible("Location", new DataGridTextColumn
            {
                Header = "Location", Binding = new Binding(nameof(BOQItemViewModel.LocationDisplay)),
                Width = new DataGridLength(110), IsReadOnly = true
            });
            AddIfVisible("Source", new DataGridTextColumn
            {
                Header = "Src", Binding = new Binding(nameof(BOQItemViewModel.SourceLabel)),
                Width = new DataGridLength(48), IsReadOnly = true
            });
            // P1.1 — host vs linked-model provenance ("Host" / link Title).
            AddIfVisible("Model", new DataGridTextColumn
            {
                Header = "Model", Binding = new Binding(nameof(BOQItemViewModel.ModelDisplay)),
                Width = new DataGridLength(120), IsReadOnly = true
            });
            AddIfVisible("Confidence", BuildConfidenceColumn());
            AddIfVisible("Carbon", new DataGridTextColumn
            {
                Header = "CO₂ kg", Binding = new Binding(nameof(BOQItemViewModel.CarbonDisplay)),
                Width = new DataGridLength(75), IsReadOnly = true
            });
            AddIfVisible("Note", BuildEditableColumn("Note", nameof(BOQItemViewModel.Note), 200, isNumber: false));

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
            Add("Edit note",            () => BeginEditCellByHeader(vm, "Note"));
            ctx.Items.Add(new Separator());
            Add("Edit description…", () => ShowNRM2EditDialog(vm));
            ctx.Items.Add(new Separator());
            Add("Mark as modeled",           () => ChangeSource(vm, BOQRowSource.Model));
            Add("Mark as manual / unmodeled", () => ChangeSource(vm, BOQRowSource.Manual));
            Add("Mark as provisional sum",   () => ChangeSource(vm, BOQRowSource.ProvisionalSum));
            Add("Mark as dayworks",          () => ChangeSource(vm, BOQRowSource.Dayworks));
            Add("Mark as PC sum",            () => ChangeSource(vm, BOQRowSource.PCSum));
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
            // P2 — aggregation drill-down: select every constituent element.
            if (vm.IsAggregated && vm.ConstituentElementIds.Count > 0)
                Add($"Select all {vm.SimilarCount} similar in Revit", () =>
                {
                    StingCommandHandler.SetExtraParam("SelectElementId",
                        string.Join(",", vm.ConstituentElementIds));
                    DispatchAction("SelectInRevit");
                });
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

        // P2.1 — column indices shift when toggleable columns are hidden, so
        // edit-by-header rather than a fixed index for the movable Note column.
        private void BeginEditCellByHeader(BOQItemViewModel vm, string header)
        {
            foreach (var grid in FindAllDataGrids(_sectionsPanel))
            {
                if (!grid.Items.Contains(vm)) continue;
                var col = grid.Columns.FirstOrDefault(c => (c.Header as string) == header);
                if (col == null) return;   // column currently hidden
                grid.SelectedItem = vm;
                grid.CurrentColumn = col;
                grid.BeginEdit();
                return;
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

        /// <summary>Distinct Materials-tab category keys — must match the
        /// GroupBy in BuildMaterialsContent so Expand-all seeds the right set.</summary>
        private IEnumerable<string> MaterialCategoryKeys()
        {
            if (_boq == null) return System.Linq.Enumerable.Empty<string>();
            return _boq.AllItems
                .Where(i => i.Source == BOQRowSource.Model && i.Quantity > 0)
                .Select(i => i.Category ?? "(uncategorised)")
                .Distinct();
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
                    // Slice 1.5 — seed from the persisted set so Expand-all /
                    // Collapse-all and manual toggles survive a rebuild.
                    IsExpanded = _openMaterialSections.Contains(grp.Key),
                    Margin = new Thickness(0, 0, 0, 4),
                    BorderThickness = new Thickness(1),
                    BorderBrush = BorderColor,
                    Background = Brushes.White
                };
                string matKey = grp.Key;   // capture — grp is reused by the loop
                expander.Expanded  += (s, e) => { _openMaterialSections.Add(matKey); SaveUiState(); };
                expander.Collapsed += (s, e) => { _openMaterialSections.Remove(matKey); SaveUiState(); };

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
            // P2.2 — inline editable form in the Actions pane (no popup). The
            // BOQSetBudget command already reads the ProjectBudgetUgx ExtraParam.
            TryShowInlineFormFor("Set project budget", "BOQSetBudget");
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
            string type = PromptString("Row type (Manual / PS / Dayworks / PC Sum):", "Manual");

            if (!double.TryParse(qtyStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double qty)) qty = 1;
            if (!double.TryParse(rateStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double rate)) rate = 0;

            StingCommandHandler.SetExtraParam("ManualRowName", name);
            StingCommandHandler.SetExtraParam("ManualRowQty", qty.ToString("F3", CultureInfo.InvariantCulture));
            StingCommandHandler.SetExtraParam("ManualRowUnit", unit ?? "each");
            StingCommandHandler.SetExtraParam("ManualRowRate", rate.ToString("F0", CultureInfo.InvariantCulture));
            StingCommandHandler.SetExtraParam("ManualRowSection", section ?? "22");
            StingCommandHandler.SetExtraParam("ManualRowDisc", disc ?? "A");
            StingCommandHandler.SetExtraParam("ManualRowSource", type ?? "Manual");
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

        // ══════════════════════════════════════════════════════════════════
        //  Slice 1 — inline result region (the no-popup convention)
        //  A panel-driven command sets InlineHost=1, runs on the Revit thread,
        //  builds a StingResultPanel.Builder, and calls BOQInlineResults.Post().
        //  That invokes PostInlineResult below, which marshals to the UI thread
        //  and renders the SAME section/metric/table tree via
        //  StingResultPanel.BuildInlineContent — no window appears.
        // ══════════════════════════════════════════════════════════════════

        private Border BuildInlineResultRegion()
        {
            _inlineResultRegion = new Border
            {
                Background = Brushes.White,
                BorderBrush = BorderColor,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 8, 12, 10),
                Visibility = Visibility.Collapsed
            };

            var dock = new DockPanel { LastChildFill = true };

            // Header row: title + close ✕
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _inlineResultTitle = new TextBlock
            {
                Text = "Result", FontSize = 12, FontWeight = FontWeights.Bold,
                Foreground = NavyBrush, VerticalAlignment = VerticalAlignment.Center
            };
            headerGrid.Children.Add(_inlineResultTitle);
            var closeBtn = new Button
            {
                Content = "✕", Width = 24, Height = 22, Padding = new Thickness(0),
                Background = Brushes.Transparent, Foreground = Brushes.Gray,
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand, FontSize = 12,
                ToolTip = "Dismiss result"
            };
            closeBtn.Click += (s, e) =>
            {
                if (_inlineResultRegion != null) _inlineResultRegion.Visibility = Visibility.Collapsed;
                if (_inlineResultHost != null) _inlineResultHost.Child = null;
            };
            Grid.SetColumn(closeBtn, 1);
            headerGrid.Children.Add(closeBtn);
            DockPanel.SetDock(headerGrid, Dock.Top);
            dock.Children.Add(headerGrid);

            _inlineResultHost = new Border { Margin = new Thickness(0, 6, 0, 0) };
            dock.Children.Add(_inlineResultHost);

            _inlineResultRegion.Child = dock;
            return _inlineResultRegion;
        }

        /// <summary>
        /// Sink target for BOQInlineResults.Post — called on the Revit API thread
        /// by panel-driven commands. Marshals to the UI thread and renders the
        /// result inline. Safe if the region hasn't been built yet (no-op).
        /// </summary>
        private void PostInlineResult(StingResultPanel.Builder b)
        {
            if (b == null) return;
            try
            {
                Dispatcher.BeginInvoke(new Action(() => ShowInlineResult(b)));
            }
            catch (Exception ex) { StingLog.Error("BOQ PostInlineResult", ex); }
        }

        private void ShowInlineResult(StingResultPanel.Builder b)
        {
            // Slice 1.5 — when the user is on the Actions tab, render into its
            // master-detail right pane instead of the global footer region.
            if (_actionReportHost != null && _mainTabs != null
                && ReferenceEquals(_mainTabs.SelectedItem, _actionsTab))
            {
                try
                {
                    if (_actionReportTitle != null && !string.IsNullOrEmpty(b.Title))
                        _actionReportTitle.Text = b.Title;
                    _actionReportHost.Child = StingResultPanel.BuildInlineContent(b);
                }
                catch (Exception ex)
                {
                    StingLog.Error("BOQ ShowInlineResult (actions)", ex);
                    _actionReportHost.Child = new TextBlock
                    {
                        Text = b.Subtitle ?? "Action completed.", TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(14), Foreground = NavyBrush
                    };
                }
                return;
            }

            if (_inlineResultRegion == null || _inlineResultHost == null) return;
            try
            {
                if (_inlineResultTitle != null)
                    _inlineResultTitle.Text = string.IsNullOrEmpty(b.Title) ? "Result" : b.Title;
                _inlineResultHost.Child = StingResultPanel.BuildInlineContent(b);
                _inlineResultRegion.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                StingLog.Error("BOQ ShowInlineResult", ex);
                // Last-ditch fallback so the user still sees something.
                if (_inlineResultHost != null)
                    _inlineResultHost.Child = new TextBlock
                    {
                        Text = b.Subtitle ?? "Action completed.", TextWrapping = TextWrapping.Wrap,
                        Foreground = NavyBrush
                    };
                if (_inlineResultRegion != null) _inlineResultRegion.Visibility = Visibility.Visible;
            }
        }

        /// <summary>Called when a dispatched Actions command finishes. If it
        /// already rendered a result inline (StingResultPanel-based), do nothing.
        /// Otherwise the action reported via its own TaskDialog (Slice-3 conversion
        /// pending), so replace the "Running…" placeholder with a completion note
        /// rather than leaving the pane stuck.</summary>
        private void ResolveActionPane(string label)
        {
            try
            {
                if (_inlineResultPosted) return;                 // result already shown inline
                if (_actionReportHost == null) return;
                if (_actionReportTitle != null) _actionReportTitle.Text = label;
                var sp = new StackPanel { Margin = new Thickness(14) };
                sp.Children.Add(new TextBlock
                {
                    Text = $"✓  “{label}” completed.",
                    FontWeight = FontWeights.SemiBold, Foreground = NavyBrush,
                    FontSize = 12, TextWrapping = TextWrapping.Wrap
                });
                sp.Children.Add(new TextBlock
                {
                    Text = "This action reported in its own dialog. Inline reporting for every " +
                           "action is being rolled out — those already converted render here directly.",
                    Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 0)
                });
                _actionReportHost.Child = sp;
            }
            catch (Exception ex) { StingLog.Warn($"ResolveActionPane: {ex.Message}"); }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Phase 108j — inline tender-setup view
        //  Swaps the BOQ/Materials TabControl for the 4-tab tender setup
        //  built via BOQTenderDialog.CreateInlineTabs. Avoids a modal
        //  window so the user keeps their place in the coordination
        //  center / BCC layout. On Export the config is persisted and a
        //  SkipDialog ExtraParam flag makes the export command bypass
        //  its own modal and read straight from project_config.json.
        // ══════════════════════════════════════════════════════════════════

        private BOQTenderDialog _tenderStaging;

        public void ShowTenderSetupInline()
        {
            if (_contentHost == null || Doc == null) return;
            try
            {
                if (_tenderSetupView != null)
                {
                    _tenderSetupView.Visibility = Visibility.Visible;
                    if (_mainTabs != null) _mainTabs.Visibility = Visibility.Collapsed;
                    return;
                }
                _tenderSetupView = BuildTenderSetupView();
                _contentHost.Children.Add(_tenderSetupView);
                if (_mainTabs != null) _mainTabs.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex) { StingLog.Error("ShowTenderSetupInline", ex); }
        }

        public void HideTenderSetupInline()
        {
            if (_mainTabs != null) _mainTabs.Visibility = Visibility.Visible;
            if (_tenderSetupView != null) _tenderSetupView.Visibility = Visibility.Collapsed;
        }

        private FrameworkElement BuildTenderSetupView()
        {
            // Construct the dialog without showing it; we only use its tabs.
            var config = BOQTenderDialog.LoadFromConfig(Doc);
            _tenderStaging = new BOQTenderDialog(config, Doc);
            var tabs = _tenderStaging.CreateInlineTabs();

            // Outer inline shell: header band + tabs + action bar
            var root = new DockPanel { LastChildFill = true, Margin = new Thickness(0), Background = PanelBg };

            // Header (inline replacement for the dialog's window title)
            var header = new Border { Background = NavyBrush, Padding = new Thickness(14, 10, 14, 10) };
            var hGrid = new Grid();
            hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titleStack = new StackPanel();
            titleStack.Children.Add(new TextBlock
            {
                Text = "TENDER BOQ — PRE-EXPORT SETUP", FontSize = 14, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "Fill the fields below, click Export ★ to generate the NRM2 BOQ, or Cancel to return to the Bill view.",
                FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xC0, 0xE0)),
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(titleStack, 0);
            hGrid.Children.Add(titleStack);
            var backBtn = new Button
            {
                Content = "✕  Back to BOQ", Height = 26, Padding = new Thickness(10, 2, 10, 2),
                Background = Brushes.Transparent, Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0xC0, 0xE0)),
                BorderThickness = new Thickness(1), Cursor = Cursors.Hand, FontSize = 10
            };
            backBtn.Click += (s, e) => HideTenderSetupInline();
            Grid.SetColumn(backBtn, 1);
            hGrid.Children.Add(backBtn);
            header.Child = hGrid;
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // Footer — Cancel + Export ★
            var actionBar = new Border
            {
                Background = Brushes.White, BorderBrush = BorderColor,
                BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(14, 8, 14, 8)
            };
            var aGrid = new Grid();
            aGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            aGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            aGrid.Children.Add(new TextBlock
            {
                Text = "Changes save to project_config.json under BOQ_TENDER_* keys on Export.",
                Foreground = Brushes.Gray, FontSize = 10, FontStyle = FontStyles.Italic,
                VerticalAlignment = VerticalAlignment.Center
            });
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(btnRow, 1);
            var cancelBtn = new Button { Content = "Cancel", Width = 90, Height = 30, Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand };
            cancelBtn.Click += (s, e) => HideTenderSetupInline();
            var exportBtn = new Button
            {
                Content = "Export ★", Width = 130, Height = 30, FontWeight = FontWeights.Bold,
                Background = OrangeBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            exportBtn.Click += (s, e) =>
            {
                try
                {
                    // _tenderStaging.Config is the same instance every tab
                    // field handler wrote to, so the dialog's existing
                    // persistence logic works unchanged.
                    BOQTenderDialog.PersistToConfig(_tenderStaging.Config);
                    StingCommandHandler.SetExtraParam("BOQTender_SkipDialog", "true");
                    HideTenderSetupInline();
                    DispatchAction("BOQExportProfessional", refreshAfter: false);
                }
                catch (Exception ex) { StingLog.Error("BOQ inline Export", ex); }
            };
            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(exportBtn);
            aGrid.Children.Add(btnRow);
            actionBar.Child = aGrid;
            DockPanel.SetDock(actionBar, Dock.Bottom);
            root.Children.Add(actionBar);

            root.Children.Add(tabs);
            return root;
        }

        /// <summary>
        /// Slice 1 — QTO IFC export driven inline. Sets InlineHost=1 so
        /// BOQExportIfcQtoCommand posts its result to the inline region instead
        /// of opening a StingResultPanel window. refreshAfter:false — the export
        /// doesn't mutate the BOQ view (it stamps + writes a file, then rolls the
        /// export transaction back), so a 300ms rebuild would only churn the grid.
        /// </summary>
        private void ExportQtoIfcInline()
        {
            try
            {
                StingCommandHandler.SetExtraParam("InlineHost", "1");
                DispatchAction("BOQExportIfcQto", refreshAfter: false);
            }
            catch (Exception ex) { StingLog.Error("BOQ ExportQtoIfcInline", ex); }
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

        // ══════════════════════════════════════════════════════════════════
        //  P2.1 — Schedule tab (4D programme + cash-flow S-curve + EVM)
        //  All inline, no popups. Phase rows persist to boq_schedule.json;
        //  EVM is computed via the reused EvmCalculator engine; the S-curve
        //  is drawn on a plain Canvas. Reads run on the dockable pane's
        //  thread (Revit main thread) so direct model reads are safe; writes
        //  go through DispatchAction.
        // ══════════════════════════════════════════════════════════════════

        private string SchedulePath()
        {
            try
            {
                string parent = System.IO.Path.GetDirectoryName(Doc?.PathName ?? "");
                if (string.IsNullOrEmpty(parent)) return null;
                return System.IO.Path.Combine(parent, "_BIM_COORD", "boq_schedule.json");
            }
            catch { return null; }
        }

        private UIElement BuildScheduleTab()
        {
            var root = new DockPanel { LastChildFill = true, Margin = new Thickness(0) };

            // Toolbar
            var bar = new Border { Background = NavyBrush, Padding = new Thickness(12, 8, 12, 8) };
            var barSp = new StackPanel { Orientation = Orientation.Horizontal };
            barSp.Children.Add(new TextBlock
            {
                Text = "4D Programme + Earned Value", Foreground = Brushes.White,
                FontSize = 13, FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0)
            });
            barSp.Children.Add(new TextBlock { Text = "As-of", Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Margin = new Thickness(0, 0, 4, 0) });
            _evmAsOfBox = new TextBox { Width = 100, Height = 24, FontSize = 11,
                Text = DateTime.Today.ToString("yyyy-MM-dd"), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "EVM cut-off date (yyyy-MM-dd)" };
            barSp.Children.Add(_evmAsOfBox);
            barSp.Children.Add(new TextBlock { Text = "Actual cost (ACWP)", Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Margin = new Thickness(10, 0, 4, 0) });
            _evmActualsBox = new TextBox { Width = 120, Height = 24, FontSize = 11, Text = "0",
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Actual cost incurred to the as-of date (editable; or use Import actuals)" };
            barSp.Children.Add(_evmActualsBox);

            barSp.Children.Add(BuildScheduleBtn("↻ Recalculate", () => { SaveSchedule(); RecalcSchedule(); }));
            barSp.Children.Add(BuildScheduleBtn("＋ Phase", AddSchedulePhaseRow));
            barSp.Children.Add(BuildScheduleBtn("⟳ Seed from Revit phases", SeedScheduleFromRevitPhases));
            barSp.Children.Add(BuildScheduleBtn("⤓ Import actuals", ImportScheduleActuals));
            barSp.Children.Add(BuildScheduleBtn("⛏ Stamp 4D dates on model", () => DispatchAction("AssignPhaseDates")));
            barSp.Children.Add(BuildScheduleBtn("↗ Export schedule/EVM", ExportScheduleEvmInline));
            bar.Child = barSp;
            DockPanel.SetDock(bar, Dock.Top);
            root.Children.Add(bar);

            // EVM strip
            _evmStrip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 10, 12, 6) };
            DockPanel.SetDock(_evmStrip, Dock.Top);
            root.Children.Add(_evmStrip);

            // S-curve
            var curveCard = new Border
            {
                Background = Brushes.White, BorderBrush = BorderColor, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), Margin = new Thickness(12, 0, 12, 8), Height = 220
            };
            _sCurveCanvas = new Canvas { Background = Brushes.White, ClipToBounds = true };
            _sCurveCanvas.SizeChanged += (s, e) => DrawSCurve();
            curveCard.Child = _sCurveCanvas;
            DockPanel.SetDock(curveCard, Dock.Top);
            root.Children.Add(curveCard);

            // Phase grid (fills remaining space)
            _scheduleGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                Margin = new Thickness(12, 0, 12, 12),
                ItemsSource = _schedulePhases,
                FontSize = 11
            };
            _scheduleGrid.Columns.Add(new DataGridTextColumn { Header = "Phase", Width = new DataGridLength(2, DataGridLengthUnitType.Star),
                Binding = new Binding("Name") { Mode = BindingMode.TwoWay } });
            _scheduleGrid.Columns.Add(new DataGridTextColumn { Header = "Start", Width = 110,
                Binding = new Binding("StartStr") { Mode = BindingMode.TwoWay } });
            _scheduleGrid.Columns.Add(new DataGridTextColumn { Header = "End", Width = 110,
                Binding = new Binding("EndStr") { Mode = BindingMode.TwoWay } });
            _scheduleGrid.Columns.Add(new DataGridTextColumn { Header = "% Complete", Width = 90,
                Binding = new Binding("PercentComplete") { Mode = BindingMode.TwoWay, StringFormat = "0.#" } });
            var planCol = new DataGridTextColumn { Header = "Planned (UGX)", Width = 130, IsReadOnly = true,
                Binding = new Binding("PlannedCost") { StringFormat = "N0" } };
            _scheduleGrid.Columns.Add(planCol);
            _scheduleGrid.CellEditEnding += (s, e) =>
            {
                // Commit the edit to the source first, then persist + recompute.
                Dispatcher.BeginInvoke(new Action(() => { SaveSchedule(); RecalcSchedule(); }),
                    System.Windows.Threading.DispatcherPriority.Background);
            };
            var gridScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _scheduleGrid
            };
            root.Children.Add(gridScroll);

            return root;
        }

        private Button BuildScheduleBtn(string label, Action onClick)
        {
            var b = new Button
            {
                Content = label, FontSize = 11, Height = 24, Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand,
                Background = Brushes.White, Foreground = NavyBrush, BorderBrush = BorderColor,
                BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center
            };
            b.Click += (s, e) => { try { onClick(); } catch (Exception ex) { StingLog.Error($"BOQ schedule btn '{label}'", ex); } };
            return b;
        }

        /// <summary>Load phases from boq_schedule.json; seed from Revit phases on first use.</summary>
        private void EnsureScheduleLoaded()
        {
            if (_scheduleLoaded) return;
            _scheduleLoaded = true;
            try
            {
                string path = SchedulePath();
                if (path != null && System.IO.File.Exists(path))
                {
                    var st = Newtonsoft.Json.JsonConvert.DeserializeObject<BoqScheduleState>(
                        System.IO.File.ReadAllText(path));
                    if (st != null)
                    {
                        _schedulePhases.Clear();
                        foreach (var p in st.Phases ?? new List<BoqSchedulePhase>())
                            if (p != null) _schedulePhases.Add(p);
                        _scheduleActualToDate = st.ActualCostToDate;
                        if (_evmActualsBox != null) _evmActualsBox.Text = st.ActualCostToDate.ToString("F0", CultureInfo.InvariantCulture);
                        if (_evmAsOfBox != null && !string.IsNullOrWhiteSpace(st.AsOf)) _evmAsOfBox.Text = st.AsOf;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"BOQ EnsureScheduleLoaded: {ex.Message}"); }

            if (_schedulePhases.Count == 0) SeedScheduleFromRevitPhases();
        }

        private void SeedScheduleFromRevitPhases()
        {
            try
            {
                _schedulePhases.Clear();
                var phases = new FilteredElementCollector(Doc).OfClass(typeof(Phase)).Cast<Phase>().ToList();
                var start = DateTime.Today;
                if (phases.Count > 0)
                {
                    int i = 0;
                    foreach (var ph in phases)
                    {
                        _schedulePhases.Add(new BoqSchedulePhase
                        {
                            Name = ph.Name ?? $"Phase {i + 1}",
                            Start = start.AddMonths(i),
                            End = start.AddMonths(i + 1),
                            PercentComplete = 0
                        });
                        i++;
                    }
                }
                else
                {
                    // No Revit phases — a single construction phase spanning a year.
                    _schedulePhases.Add(new BoqSchedulePhase
                    { Name = "Construction", Start = start, End = start.AddMonths(12), PercentComplete = 0 });
                }
                SaveSchedule();
                RecalcSchedule();
            }
            catch (Exception ex) { StingLog.Error("BOQ SeedScheduleFromRevitPhases", ex); }
        }

        private void AddSchedulePhaseRow()
        {
            var last = _schedulePhases.LastOrDefault();
            var start = last?.End ?? DateTime.Today;
            _schedulePhases.Add(new BoqSchedulePhase
            { Name = "New phase", Start = start, End = start.AddMonths(1), PercentComplete = 0 });
            SaveSchedule();
            RecalcSchedule();
        }

        private void SaveSchedule()
        {
            if (!_scheduleLoaded) return;
            try
            {
                string path = SchedulePath();
                if (path == null) return;
                double acwp = ParseActualsBox();
                string asOf = _evmAsOfBox?.Text?.Trim();
                var st = new BoqScheduleState
                {
                    Phases = _schedulePhases.ToList(),
                    ActualCostToDate = acwp,
                    AsOf = asOf
                };
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllText(path,
                    Newtonsoft.Json.JsonConvert.SerializeObject(st, Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"BOQ SaveSchedule: {ex.Message}"); }
        }

        private double ParseActualsBox()
        {
            if (_evmActualsBox != null
                && double.TryParse(_evmActualsBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                return v;
            return _scheduleActualToDate;
        }

        private DateTime ParseAsOf()
        {
            if (_evmAsOfBox != null
                && DateTime.TryParse(_evmAsOfBox.Text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d;
            return DateTime.Today;
        }

        /// <summary>Cheap recompute called on every RefreshDisplay + explicit Recalculate.</summary>
        private void RefreshScheduleTab()
        {
            if (_scheduleGrid == null) return;   // tab not built yet
            EnsureScheduleLoaded();
            RecalcSchedule();
        }

        /// <summary>
        /// Allocate the BOQ total across phases by duration, compute the EVM
        /// period via the reused EvmCalculator, refresh the metric strip and
        /// redraw the S-curve. No model writes.
        /// </summary>
        private void RecalcSchedule()
        {
            try
            {
                _scheduleActualToDate = ParseActualsBox();
                double bac = (_boq != null && _boq.ProjectBudgetUGX > 0) ? _boq.ProjectBudgetUGX
                           : (_boq?.GrandTotalUGX ?? 0);

                // Planned cost per phase = BAC × duration share.
                double totalDays = _schedulePhases.Sum(p => Math.Max(1, (p.End - p.Start).TotalDays));
                foreach (var p in _schedulePhases)
                {
                    double days = Math.Max(1, (p.End - p.Start).TotalDays);
                    p.PlannedCost = totalDays > 0 ? Math.Round(bac * days / totalDays, 0) : 0;
                }

                var asOf = ParseAsOf();
                double bcws = 0, bcwp = 0;
                foreach (var p in _schedulePhases)
                {
                    double span = Math.Max(1, (p.End - p.Start).TotalDays);
                    double frac = Math.Max(0, Math.Min(1, (asOf - p.Start).TotalDays / span));
                    bcws += p.PlannedCost * frac;
                    bcwp += p.PlannedCost * (p.PercentComplete / 100.0);
                }
                double acwp = _scheduleActualToDate;

                var period = StingTools.Core.Evm.EvmCalculator.Compute(bac, bcws, bcwp, acwp, asOf);
                RenderEvmStrip(period);
                DrawSCurve();
            }
            catch (Exception ex) { StingLog.Error("BOQ RecalcSchedule", ex); }
        }

        private void RenderEvmStrip(StingTools.Core.Evm.EvmPeriod p)
        {
            if (_evmStrip == null) return;
            _evmStrip.Children.Clear();
            if (p == null) return;
            string cur = "UGX";
            _evmStrip.Children.Add(EvmMetric("PV (BCWS)", $"{cur} {p.Bcws:N0}", null));
            _evmStrip.Children.Add(EvmMetric("EV (BCWP)", $"{cur} {p.Bcwp:N0}", null));
            _evmStrip.Children.Add(EvmMetric("AC (ACWP)", $"{cur} {p.Acwp:N0}", null));
            _evmStrip.Children.Add(EvmMetric("SPI", p.Spi.ToString("0.000"), RagFor(p.Spi)));
            _evmStrip.Children.Add(EvmMetric("CPI", p.Cpi.ToString("0.000"), RagFor(p.Cpi)));
            _evmStrip.Children.Add(EvmMetric("EAC", $"{cur} {p.Eac:N0}", null));
            _evmStrip.Children.Add(EvmMetric("ETC", $"{cur} {p.Etc:N0}", null));
            _evmStrip.Children.Add(EvmMetric("VAC", $"{cur} {p.Vac:N0}", p.Vac < 0 ? RedBrush : GreenBrush));
        }

        private Brush RagFor(double index)
        {
            if (index <= 0) return Brushes.Gray;
            if (index < 0.90) return RedBrush;
            if (index < 0.95) return AmberBrush;
            return GreenBrush;
        }

        private UIElement EvmMetric(string label, string value, Brush valueBrush)
        {
            var card = new Border
            {
                Background = Brushes.White, BorderBrush = BorderColor, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3), Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 8, 0), MinWidth = 92
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label, FontSize = 9.5, Foreground = Brushes.Gray });
            sp.Children.Add(new TextBlock { Text = value, FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = valueBrush ?? NavyBrush });
            card.Child = sp;
            return card;
        }

        /// <summary>
        /// Draw a lightweight cash-flow S-curve on the Canvas: the planned
        /// cumulative cost over the programme (cost-loaded baseline) plus the
        /// earned (BCWP) and actual (ACWP) cumulative-to-date ramps, with an
        /// as-of guide. No external chart library.
        /// </summary>
        private void DrawSCurve()
        {
            var c = _sCurveCanvas;
            if (c == null) return;
            c.Children.Clear();
            double w = c.ActualWidth, h = c.ActualHeight;
            if (w < 40 || h < 40 || _schedulePhases.Count == 0) return;

            var phases = _schedulePhases.Where(p => p.End > p.Start).OrderBy(p => p.Start).ToList();
            if (phases.Count == 0) return;

            DateTime t0 = phases.Min(p => p.Start);
            DateTime t1 = phases.Max(p => p.End);
            double totalDays = Math.Max(1, (t1 - t0).TotalDays);
            DateTime asOf = ParseAsOf();

            double bacTotal = phases.Sum(p => p.PlannedCost);
            double bcwp = phases.Sum(p => p.PlannedCost * (p.PercentComplete / 100.0));
            double acwp = _scheduleActualToDate;
            double maxVal = Math.Max(bacTotal, Math.Max(bcwp, acwp));
            if (maxVal <= 0) maxVal = 1;

            const double padL = 8, padR = 8, padT = 10, padB = 18;
            double plotW = w - padL - padR, plotH = h - padT - padB;

            Func<DateTime, double> X = t => padL + plotW * Math.Max(0, Math.Min(1, (t - t0).TotalDays / totalDays));
            Func<double, double> Y = v => padT + plotH * (1 - Math.Max(0, Math.Min(1, v / maxVal)));

            // Baseline axes.
            c.Children.Add(MakeLine(padL, padT + plotH, padL + plotW, padT + plotH, BorderColor, 1));

            // Planned cumulative — sampled across the programme.
            var planned = new PointCollection();
            int samples = 48;
            for (int i = 0; i <= samples; i++)
            {
                DateTime t = t0.AddDays(totalDays * i / samples);
                double cum = 0;
                foreach (var p in phases)
                {
                    double span = Math.Max(1, (p.End - p.Start).TotalDays);
                    double frac = Math.Max(0, Math.Min(1, (t - p.Start).TotalDays / span));
                    cum += p.PlannedCost * frac;
                }
                planned.Add(new System.Windows.Point(X(t), Y(cum)));
            }
            c.Children.Add(new Polyline { Points = planned, Stroke = NavyBrush, StrokeThickness = 2 });

            // Earned + actual cumulative-to-date ramps (single scalar each at as-of).
            double asOfX = X(asOf);
            c.Children.Add(MakeLine(padL, Y(0), asOfX, Y(bcwp), GreenBrush, 2));
            c.Children.Add(MakeLine(padL, Y(0), asOfX, Y(acwp), AmberBrush, 2));

            // As-of vertical guide.
            var guide = MakeLine(asOfX, padT, asOfX, padT + plotH, Brushes.Gray, 1);
            guide.StrokeDashArray = new DoubleCollection { 3, 3 };
            c.Children.Add(guide);

            // Legend.
            AddLegend(c, padL + 4, padT, "Planned", NavyBrush, "Earned (EV)", GreenBrush, "Actual (AC)", AmberBrush);
        }

        private System.Windows.Shapes.Line MakeLine(double x1, double y1, double x2, double y2, Brush stroke, double thick)
        {
            return new System.Windows.Shapes.Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = thick };
        }

        private void AddLegend(Canvas c, double x, double y, params object[] pairs)
        {
            double cx = x;
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                string label = pairs[i] as string;
                var brush = pairs[i + 1] as Brush;
                var sw = new System.Windows.Shapes.Rectangle { Width = 10, Height = 10, Fill = brush };
                Canvas.SetLeft(sw, cx); Canvas.SetTop(sw, y); c.Children.Add(sw);
                var tb = new TextBlock { Text = label, FontSize = 9.5, Foreground = Brushes.Gray };
                Canvas.SetLeft(tb, cx + 13); Canvas.SetTop(tb, y - 2); c.Children.Add(tb);
                cx += 13 + Math.Max(48, (label?.Length ?? 6) * 6) + 12;
            }
        }

        private void ImportScheduleActuals()
        {
            try
            {
                string dir = System.IO.Path.Combine(
                    StingTools.BIMManager.BIMManagerEngine.GetBIMManagerDir(Doc), "actuals");
                double total = StingTools.Core.Evm.EvmCalculator.ImportAllActualsToDate(
                    dir, ParseAsOf(), out int filesRead, out int dupsSkipped);
                if (filesRead == 0)
                {
                    FlashHint(null, $"No actuals_*.csv found under {dir} — enter ACWP manually in the box.");
                    return;
                }
                _scheduleActualToDate = total;
                if (_evmActualsBox != null) _evmActualsBox.Text = total.ToString("F0", CultureInfo.InvariantCulture);
                SaveSchedule();
                RecalcSchedule();
                FlashHint(null, $"Imported {filesRead} actuals file(s) ({dupsSkipped} dup skipped) — ACWP {total:N0}.");
            }
            catch (Exception ex) { StingLog.Error("BOQ ImportScheduleActuals", ex); }
        }

        private void ExportScheduleEvmInline()
        {
            try
            {
                SaveSchedule();
                RecalcSchedule();
                var asOf = ParseAsOf();
                double bac = (_boq != null && _boq.ProjectBudgetUGX > 0) ? _boq.ProjectBudgetUGX : (_boq?.GrandTotalUGX ?? 0);
                double bcws = 0, bcwp = 0;
                foreach (var p in _schedulePhases)
                {
                    double span = Math.Max(1, (p.End - p.Start).TotalDays);
                    double frac = Math.Max(0, Math.Min(1, (asOf - p.Start).TotalDays / span));
                    bcws += p.PlannedCost * frac; bcwp += p.PlannedCost * (p.PercentComplete / 100.0);
                }
                var period = StingTools.Core.Evm.EvmCalculator.Compute(bac, bcws, bcwp, _scheduleActualToDate, asOf);

                // CSV next to the schedule json.
                string csvPath = null;
                try
                {
                    string parent = System.IO.Path.GetDirectoryName(SchedulePath() ?? "");
                    if (!string.IsNullOrEmpty(parent))
                    {
                        System.IO.Directory.CreateDirectory(parent);
                        csvPath = System.IO.Path.Combine(parent, $"schedule_evm_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine("Phase,Start,End,PercentComplete,PlannedUGX");
                        foreach (var p in _schedulePhases)
                            sb.AppendLine($"\"{p.Name}\",{p.StartStr},{p.EndStr},{p.PercentComplete:0.#},{p.PlannedCost:0}");
                        sb.AppendLine();
                        sb.AppendLine("Metric,Value");
                        sb.AppendLine($"BAC,{bac:0}");
                        sb.AppendLine($"PV (BCWS),{period.Bcws:0}");
                        sb.AppendLine($"EV (BCWP),{period.Bcwp:0}");
                        sb.AppendLine($"AC (ACWP),{period.Acwp:0}");
                        sb.AppendLine($"SPI,{period.Spi:0.000}");
                        sb.AppendLine($"CPI,{period.Cpi:0.000}");
                        sb.AppendLine($"EAC,{period.Eac:0}");
                        sb.AppendLine($"ETC,{period.Etc:0}");
                        sb.AppendLine($"VAC,{period.Vac:0}");
                        System.IO.File.WriteAllText(csvPath, sb.ToString());
                    }
                }
                catch (Exception ex) { StingLog.Warn($"BOQ schedule CSV write: {ex.Message}"); csvPath = null; }

                var b = StingResultPanel.Create("Schedule + EVM");
                b.SetSubtitle($"As-of {asOf:yyyy-MM-dd} · {_schedulePhases.Count} phase(s) · BAC UGX {bac:N0}");
                b.AddSection("EARNED VALUE")
                    .Metric("PV (BCWS)", $"UGX {period.Bcws:N0}")
                    .Metric("EV (BCWP)", $"UGX {period.Bcwp:N0}")
                    .Metric("AC (ACWP)", $"UGX {period.Acwp:N0}")
                    .Metric("SPI", period.Spi.ToString("0.000"), period.ScheduleHealth)
                    .Metric("CPI", period.Cpi.ToString("0.000"), period.CostHealth)
                    .Metric("EAC", $"UGX {period.Eac:N0}")
                    .Metric("ETC", $"UGX {period.Etc:N0}")
                    .Metric("VAC", $"UGX {period.Vac:N0}");
                b.AddSection("PROGRAMME");
                foreach (var p in _schedulePhases)
                    b.Metric(p.Name, $"{p.PercentComplete:0.#}%", $"{p.StartStr} → {p.EndStr} · UGX {p.PlannedCost:N0}");
                if (!string.IsNullOrEmpty(csvPath)) b.SetCsvPath(csvPath);
                ShowInlineResult(b);
            }
            catch (Exception ex) { StingLog.Error("BOQ ExportScheduleEvmInline", ex); }
        }
    }

    /// <summary>P2.1 — a single programme phase row (editable in the Schedule grid).</summary>
    internal class BoqSchedulePhase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void N(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        private string _name = "";
        private DateTime _start = DateTime.Today;
        private DateTime _end = DateTime.Today.AddMonths(1);
        private double _pct;

        public string Name { get => _name; set { _name = value ?? ""; N(nameof(Name)); } }
        public DateTime Start { get => _start; set { _start = value; N(nameof(Start)); N(nameof(StartStr)); } }
        public DateTime End { get => _end; set { _end = value; N(nameof(End)); N(nameof(EndStr)); } }
        public double PercentComplete { get => _pct; set { _pct = Math.Max(0, Math.Min(100, value)); N(nameof(PercentComplete)); } }

        [Newtonsoft.Json.JsonIgnore]
        public string StartStr
        {
            get => _start.ToString("yyyy-MM-dd");
            set { if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) Start = d; }
        }
        [Newtonsoft.Json.JsonIgnore]
        public string EndStr
        {
            get => _end.ToString("yyyy-MM-dd");
            set { if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) End = d; }
        }
        /// <summary>Computed in RecalcSchedule (BAC × duration share); not persisted as authoritative.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public double PlannedCost { get; set; }
    }

    /// <summary>P2.1 — persisted schedule state in &lt;project&gt;/_BIM_COORD/boq_schedule.json.</summary>
    internal class BoqScheduleState
    {
        public List<BoqSchedulePhase> Phases { get; set; } = new List<BoqSchedulePhase>();
        public double ActualCostToDate { get; set; }
        public string AsOf { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  BOQInlineResults — static mailbox bridging Revit-thread commands to the
    //  panel's inline result region. A command that was invoked with the
    //  InlineHost=1 ExtraParam builds a StingResultPanel.Builder and calls
    //  Post(builder) instead of builder.Show(). When a BOQCostManagerPanel is
    //  alive it has registered Sink; otherwise Post returns false and the caller
    //  falls back to the popup (ribbon / workflow callers keep working).
    // ══════════════════════════════════════════════════════════════════════
    internal static class BOQInlineResults
    {
        // Set by BOQCostManagerPanel on construction, cleared on Unloaded.
        public static Action<StingResultPanel.Builder> Sink;

        public static bool Active => Sink != null;

        /// <summary>
        /// Hand a built result to the live panel. Returns true if a sink consumed
        /// it (caller must NOT also .Show()); false if no panel is hosting.
        /// </summary>
        public static bool Post(StingResultPanel.Builder b)
        {
            var s = Sink;
            if (s == null || b == null) return false;
            try { s(b); return true; }
            catch (Exception ex) { StingLog.Error("BOQInlineResults.Post", ex); return false; }
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

        // P1.2 / P2 — aggregation drill-down support.
        public int SimilarCount => _item.SimilarCount;
        public IReadOnlyList<long> ConstituentElementIds
            => _item.ConstituentElementIds ?? new List<long>();
        public bool IsAggregated => _item.SimilarCount > 1;

        // P2.1 — spatial columns.
        public string LevelDisplay => _item.Level ?? "";
        public string LocationDisplay => _item.Location ?? "";

        // P1.1 — provenance as a first-class column. SourceModel is "" / null for
        // host elements and the linked model's Title for linked rows; surface it as
        // "Host" so a QS can sort/filter host-vs-link without parsing the Note text.
        public string ModelDisplay
            => string.IsNullOrWhiteSpace(_item.SourceModel) ? "Host" : _item.SourceModel;

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
                    case BOQRowSource.Dayworks:       return "Daywk";
                    case BOQRowSource.PCSum:          return "PC";
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
                    case BOQRowSource.Dayworks:       c = Color.FromRgb(230, 245, 252); break;
                    case BOQRowSource.PCSum:          c = Color.FromRgb(245, 240, 230); break;
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
                    case BOQRowSource.Dayworks:       c = Color.FromRgb(40, 150, 200);  break;
                    case BOQRowSource.PCSum:          c = Color.FromRgb(170, 130, 60);  break;
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
