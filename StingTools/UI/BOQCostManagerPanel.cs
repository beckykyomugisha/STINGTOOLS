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
using StingTools.Core.Schedule;   // Phase 1b — unified ScheduleModel / ScheduleStore
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
            // G4 — the L/P/M rate-split columns are off by default (only relevant
            // when a rate source provides a split). Persisted ui-state overrides this.
            new HashSet<string>(new[] { "Labour", "Plant", "Material", "CO2 Quality",
                // Phase 2A — NRM2 measurement audit columns (off by default).
                "Gross", "Deduct", "Waste", "Net",
                // Phase 2E — WBS / CBS columns (off by default).
                "WBS", "CBS" }, StringComparer.OrdinalIgnoreCase);
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
            public bool WizardCompleted { get; set; }   // G7 — first-run Cost Setup wizard
        }

        // G7 — Cost Setup wizard state.
        private bool _wizardCompleted;       // persisted in boq_ui_state.json
        private bool _wizardOffered;         // session flag — offer at most once per open
        private int _wizardPage;
        private string _wizardBudget = "";
        private string _wizardCurrency = "UGX";
        private string _wizardPricing = "Auto";   // Auto | RoundTrip | Manual

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
                        _wizardCompleted = st.WizardCompleted;   // G7
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
                    OpenMaterialSections = _openMaterialSections.ToList(),
                    WizardCompleted = _wizardCompleted   // G7
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
        // Phase 2C — passive drift banner on the dashboard strip.
        private Border _driftBanner;
        private TextBlock _driftBannerText;
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

        // ── P0.1 — dispatch busy-guard ───────────────────────────────────────
        // The confirmed "lifeless panel" deadlock: a second action clicked while
        // the first is still in flight calls ExternalEvent.Raise() again, which
        // returns Pending; SetCommand overwrites the live tag and the event never
        // idles, so every later button (including the footer QTO IFC) goes dead.
        // This guard claims a single in-flight dispatch slot at the DispatchAction
        // chokepoint: while a command runs, further action clicks are ignored and
        // every dispatch-capable button is greyed. The slot is released from
        // StingCommandHandler.Execute's finally (the universal command-completion
        // hook) via DispatchGuardReset, so it covers footer/schedule dispatches
        // that don't register a PendingActionResolve.
        internal static volatile bool CommandRunning;
        // Re-enable hook the static completion path fires on the live instance to
        // un-grey buttons. Set in the ctor, cleared on Unloaded.
        internal static Action DispatchGuardReset;
        // Every dispatch-capable button, greyed (IsEnabled=false) while busy.
        private readonly List<Button> _dispatchButtons = new List<Button>();
        // Held delegate for DispatchGuardReset (de)registration — a method-group
        // re-conversion would be a fresh instance, so ReferenceEquals needs this.
        private Action _dispatchGuardReset;

        // Slice 1 (5D workspace) — inline result region. Panel-driven actions set
        // the InlineHost=1 ExtraParam so commands route their result here via
        // BOQInlineResults.Post() instead of StingResultPanel.Show(). This is the
        // "zero external reporting popups" convention the rest of the workspace
        // (Schedule / cash-flow / sweep) follows.
        private Border _inlineResultRegion;   // collapsed by default
        private Border _inlineResultHost;     // receives BuildInlineContent output
        private TextBlock _inlineResultTitle;
        private Action<StingResultPanel.Builder> _inlineSink;  // stable delegate for sink (de)registration

        // P2.1 / Phase 1b — Schedule / 4D + EVM tab. A thin view onto the single
        // unified schedule (Core.Schedule.ScheduleStore, <project>/_BIM_COORD/
        // schedule.json) — the same model BCC's 4D/5D tab reads. EVM is computed via
        // the reused EvmCalculator engine; the S-curve is drawn on a plain Canvas.
        private TabItem _scheduleTab;
        // Phase 1b — the Schedule tab is now a thin view onto the unified
        // ScheduleModel (ScheduleStore, _BIM_COORD/schedule.json). The grids bind
        // directly to the model types (same property names as the retired Boq*
        // view-models), so editing a row mutates the model task in place and
        // preserves its WBS / predecessors / element links on save.
        private System.Collections.ObjectModel.ObservableCollection<ScheduleTask> _schedulePhases
            = new System.Collections.ObjectModel.ObservableCollection<ScheduleTask>();
        private System.Collections.ObjectModel.ObservableCollection<SchedulePeriod> _schedulePeriods
            = new System.Collections.ObjectModel.ObservableCollection<SchedulePeriod>();
        private System.Collections.ObjectModel.ObservableCollection<ScheduleMilestone> _milestones
            = new System.Collections.ObjectModel.ObservableCollection<ScheduleMilestone>();
        // Backing model — holds provenance + the same task instances the grids show.
        private ScheduleModel _scheduleModel;
        private DataGrid _scheduleGrid;
        private DataGrid _periodsGrid;
        private DataGrid _milestonesGrid;
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
            // P0.1 — register this live instance's guard-release so the static
            // command-completion hook (StingCommandHandler.Execute finally) can
            // un-grey the buttons + free the dispatch slot when a command finishes.
            _dispatchGuardReset = EndDispatchGuard;
            DispatchGuardReset = _dispatchGuardReset;
            this.Unloaded += (s, e) =>
            {
                if (ReferenceEquals(BOQInlineResults.Sink, _inlineSink))
                    BOQInlineResults.Sink = null;
                // Drop the guard hook + clear the busy flag so a closed panel can't
                // strand the static flag (which would block a future re-opened panel).
                if (ReferenceEquals(DispatchGuardReset, _dispatchGuardReset))
                    DispatchGuardReset = null;
                CommandRunning = false;
            };
            // Phase 2D — enable the incremental take-off dirty marker while the
            // Cost Manager is in use. Kept enabled across panel hide/show so the
            // dirty set stays continuous (so the cache stays trustworthy); the
            // first build is always full (state defaults to force-full).
            try { StingTools.BOQ.StingCostDirtyMarker.SetEnabled(true); }
            catch (Exception ex) { StingLog.Warn($"BOQ enable dirty marker: {ex.Message}"); }
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
            _linksBtn = BuildHeaderBtn("⛓ Links", () => ChooseLinkedModels(),
                "Include linked Revit models in this bill of quantities.\n" +
                "Tick which loaded links to take off — their quantities, cost and carbon are added\n" +
                "(read-only, tagged \"[Linked: …]\"; view them via Group → Source model).\n" +
                "The count in the button shows how many links are included. Needs at least one link\n" +
                "loaded first (Manage → Links in Revit).");
            actions.Children.Add(_linksBtn);

            // Explicit Refresh forces a full recompute incl. reloaded links — the
            // per-link takeoff cache (P1.2) is only auto-invalidated on document
            // close, so a link Reloaded mid-session would otherwise stay stale.
            // Passive refreshes (filter / rate edit) keep using the cache for speed.
            actions.Children.Add(BuildHeaderBtn("↻ Refresh", () =>
            {
                try { StingTools.BOQ.BOQCostManager.InvalidateLinkCache(); } catch { }
                DispatchAction("BOQRefresh");
            }, "Refresh the bill. Phase 2D — re-takes-off only the elements you changed since the\n" +
               "last build (incremental), reusing cached rows for the rest. Identical result to a full\n" +
               "rebuild, just faster on large models. Links are re-walked. Use 'Refresh (full)' to force\n" +
               "a complete re-walk."));
            // Phase 2D — explicit full rebuild: clears the incremental host cache so
            // every element is re-taken-off (the always-correct fallback).
            actions.Children.Add(BuildHeaderBtn("↻ Refresh (full)", () =>
            {
                try { StingTools.BOQ.BOQCostManager.ForceHostFull(Doc); } catch { }
                try { StingTools.BOQ.BOQCostManager.InvalidateLinkCache(); } catch { }
                DispatchAction("BOQRefresh");
            }, "Force a complete rebuild — re-takes-off every element (host + links) from scratch.\n" +
               "Use this if you ever suspect the incremental refresh is out of step (it shouldn't be)."));
            actions.Children.Add(BuildHeaderBtn("Set Budget", () => ShowBudgetDialog()));
            actions.Children.Add(BuildHeaderBtn("✦ Cost Setup", () => ShowCostSetupWizard()));   // G7
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

        private Button BuildHeaderBtn(string text, Action click, string tooltip = null)
        {
            var b = new Button
            {
                Content = text, Height = 26, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(4, 0, 0, 0),
                Background = OrangeBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0),
                FontSize = 11, Cursor = Cursors.Hand
            };
            if (!string.IsNullOrEmpty(tooltip)) b.ToolTip = tooltip;
            b.Click += (s, e) => click();
            return Guarded(b);
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

            // Phase 2C — passive drift banner. Collapsed until the live bill
            // drifts from the last saved snapshot; clicking it runs Drift check.
            _driftBannerText = new TextBlock
            {
                Text = "", FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(140, 90, 0)), TextWrapping = TextWrapping.Wrap
            };
            _driftBanner = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(255, 248, 225)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(230, 190, 120)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 6, 0, 0),
                Cursor = Cursors.Hand, Visibility = Visibility.Collapsed,
                Child = _driftBannerText, ToolTip = "Click to run Drift check"
            };
            _driftBanner.MouseLeftButtonUp += (s, e) =>
            {
                try
                {
                    if (_mainTabs != null && _actionsTab != null) _mainTabs.SelectedItem = _actionsTab;
                    ShowDriftCheck("Drift check");
                }
                catch (Exception ex) { StingLog.Error("BOQ drift banner click", ex); }
            };
            sp.Children.Add(_driftBanner);

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
            return Guarded(new Button
            {
                Content = text, Height = 28, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(4, 0, 0, 0),
                Background = bg, Foreground = Brushes.White, BorderThickness = new Thickness(0),
                FontSize = 11, Cursor = Cursors.Hand
            });
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
                ("WBS",                 BoqGroupingMode.Wbs),
                ("CBS",                 BoqGroupingMode.Cbs),
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
                case "Deduct":     return "Deduct (NRM2)";
                case "Net":        return "Net (measured)";
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
                Text = "Every cost workflow in one place. " +
                       "Hover for a tooltip describing each action.",
                FontSize = 11, Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 14)
            });

            sp.Children.Add(BuildActionGroup("QS Round-Trip (Excel)",
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

            sp.Children.Add(BuildActionGroup("Automation",
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

            sp.Children.Add(BuildActionGroup("Live Rate Feeds",
                "Configure live rate feeds (BCIS / Planscape) and pull candidate rates onto the bill.",
                new[]
                {
                    ("Rate feeds", "BOQ_RateFeedsConfig",
                     "Configure the BCIS feed (base URL · API key · TTL) and the Planscape feed on/off. Saved to the project file only — the API key is never committed.", false),
                    ("★ Fetch live rates", "BOQ_FetchLiveRates",
                     "Pull candidate rates for the current bill from every configured feed, side-by-side with the current rate + confidence. Accept the best live rate per line or in bulk. Manual overrides are protected.", true),
                }));

            sp.Children.Add(BuildActionGroup("Change Detection",
                "Detect when the bill has moved from the last saved snapshot and re-price what changed.",
                new[]
                {
                    ("★ Drift check", "BOQ_DriftCheck",
                     "Compare the live bill against the last saved snapshot — what changed (qty moved / new / removed / rate revised), element-linked. Then re-price the changed lines via the rate chain (incl. live feeds). Overrides protected.", true),
                }));

            sp.Children.Add(BuildActionGroup("Cost Breakdown & ERP Export",
                "File the bill by a client breakdown structure and export it import-ready for an ERP.",
                new[]
                {
                    ("WBS / CBS map", "BOQ_WbsMap",
                     "Edit rules that assign WBS / CBS codes from element attributes (category / discipline / NRM2 § / level / zone). Saved to the project; lines with no rule inherit the linked 4D task's WBS. Group the bill by WBS / CBS via the Group dropdown.", false),
                    ("★ Export to ERP", "BOQ_ExportErp",
                     "Write a flat, import-ready CSV (WBS · CBS · cost code · qty · rate · total · level · location · source · IfcGuid) — the union most ERP / accounting importers accept — plus an optional Primavera P6 activity-cost XML. Opens inline.", true),
                }));

            sp.Children.Add(BuildActionGroup("Cost Plan (NRM1)",
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

            sp.Children.Add(BuildActionGroup("Payment Certificates",
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

            sp.Children.Add(BuildActionGroup("Variations & Star Rates",
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
                     "Walk legacy variations still on default Other / Employer and set their reason + liability via multi-select picker", false),
                }));

            sp.Children.Add(BuildActionGroup("Earned Value Management",
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

            sp.Children.Add(BuildActionGroup("Cost Report",
                "Anticipated final cost — where the project is heading vs budget.",
                new[]
                {
                    ("★ Anticipated Final Cost", "Cost_AnticipatedFinalCost",
                     "Modelled works + manual/PS allowances + agreed variations + pending variations → AFC vs budget. On screen + XLSX.", true),
                    ("Reconcile Provisionals", "ReconcileProvisionals",
                     "Record the final-account actual against each provisional sum (estimate → actual trail). The movement feeds the Anticipated Final Cost and persists across reopen.", false),
                    ("Preliminaries schedule", "BOQ_Prelims",
                     "Keep the flat prelims % or switch to an itemised built-up preliminaries schedule (site set-up, staff, welfare, insurances…). The active basis rolls into the grand total and the XLSX export.", false),
                    ("Labour rollup", "BOQ_LabourRollup",
                     "Σ labour / plant / material rate-split content by NRM2 section, plus labour hours by trade (crew). Read-only.", false),
                    ("Carbon Gap Report", "BOQ_CarbonGapReport",
                     "List materials whose embodied-carbon factor is missing or only database-grade (not an EPD), the carbon at stake, and a CSV worklist to drive EPD sourcing. Read-only.", false),
                }));

            sp.Children.Add(BuildActionGroup("Measurement Standard",
                "Switch the active standard. Affects how rows are classified and described.",
                new[]
                {
                    ("Set Standard",     "Cost_SetMeasurementStandard",
                     "Pick the active standard — NRM2 / CESMM4 / POMI / ICMS3 / MMHW", false),
                    ("Standard Preview", "Cost_StandardInspect",
                     "Diagnostic preview — how each standard classifies common categories", false),
                    ("Measurement audit", "BOQ_MeasurementAudit",
                     "Per-line gross → net derivation: gross geometry, openings/voids deducted (NRM2/CESMM4 rules), wastage step, net measured quantity. Read-only.", false),
                }));

            sp.Children.Add(BuildActionGroup("IFC & ICMS3 Export",
                "External tool round-trip and cost-plus-carbon ledger.",
                new[]
                {
                    ("Stamp IFC Qto",    "Cost_StampIfcQuantities",
                     "Populate IFC4 Qto_*BaseQuantities + Pset_StingCost so Cost-X / CostOS / Candy / Bluebeam Revu can read cost direct from IFC", false),
                    ("★ ICMS3 Report",   "Cost_ExportIcms3Report",
                     "Export ICMS3 cost + carbon ledger — £ + kgCO₂e + £/kgCO₂e per ICMS group", true),
                }));

            sp.Children.Add(BuildActionGroup("QS Sign-Off",
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
            return Guarded(btn);
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

                // G2 — the reconcile action renders a dynamic per-PS editable
                // trail form (not the fixed-field ShowInlineForm), so intercept it
                // before the generic form/dispatch path.
                if (tag == "ReconcileProvisionals") { ShowProvisionalReconcileForm(label); return; }
                if (tag == "BOQ_Prelims") { ShowPrelimsForm(label); return; }
                // Phase 2A — panel-local read-only report over the current bill.
                if (tag == "BOQ_MeasurementAudit") { ShowMeasurementAudit(label); return; }
                // Phase 2B — live rate feeds: inline config form + fetch/accept.
                if (tag == "BOQ_RateFeedsConfig") { ShowRateFeedsForm(label); return; }
                if (tag == "BOQ_FetchLiveRates") { ShowFetchLiveRates(label); return; }
                // Phase 2C — drift check (live bill vs last snapshot) + re-price.
                if (tag == "BOQ_DriftCheck") { ShowDriftCheck(label); return; }
                // Phase 2E — WBS/CBS map editor + ERP export.
                if (tag == "BOQ_WbsMap") { ShowWbsMapForm(label); return; }
                if (tag == "BOQ_ExportErp") { ShowErpExport(label); return; }
                // Star rate is a dynamic multi-line build-up — its own inline editor.
                if (tag == "Variation_BuildStarRate") { ShowStarRateForm(label); return; }
                // Reclassify legacy = a per-VO reason/liability grid — its own editor.
                if (tag == "Variation_ReclassifyLegacy") { ShowReclassifyForm(label); return; }

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
                // P0.2 — defensively clear any stale result sink left over by a prior
                // aborted run (command that threw before the dispatcher's finally
                // cleared it) so we never inherit a foreign sink. Picker routing is
                // gone — input pickers are modal now (no nested pump).
                StingResultPanel.InlineSink = null;

                if (_actionReportHost != null)
                    _actionReportHost.Child = new TextBlock
                    {
                        Text = $"Running “{label}” …\nThe result appears here when the action completes.",
                        Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(14)
                    };
                // Route this command's result panels into the Actions report pane
                // instead of popups (StingResultPanel.InlineSink sets a Border child —
                // no message pump, so it's safe). Input pickers are modal. The
                // dispatcher clears the sink in StingCommandHandler.Execute's finally,
                // once the (synchronous) command has run.
                _inlineResultPosted = false;   // set true by the sink if a result lands inline
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

        // ── Phase 2A — Measurement audit (panel-local read-only report) ─────

        /// <summary>
        /// Render the gross → net measured derivation for every model line in the
        /// current bill into the Actions report pane (no popup). Lists each line's
        /// gross geometry, the openings/voids deducted under the active standard,
        /// the wastage step, and the net measured quantity used for cost.
        /// </summary>
        private void ShowMeasurementAudit(string label)
        {
            try
            {
                var b = StingResultPanel.Create("Measurement audit");

                if (_boq == null || _boq.AllItems == null || _boq.AllItems.Count == 0)
                {
                    b.SetSubtitle("No bill built yet — press Refresh first, then re-open Measurement audit.");
                    ShowInlineResult(b);
                    return;
                }

                var std = StingTools.BOQ.MeasurementStandard.MeasurementStandardRegistry
                    .Get(_boq.MeasurementStandardId);

                var measured = _boq.AllItems
                    .Where(i => i.Source == BOQRowSource.Model && i.GrossQuantity > 0)
                    .OrderByDescending(i => i.DeductionQuantity)
                    .ThenByDescending(i => i.WastageQuantity)
                    .ToList();

                int withDeductions = measured.Count(i => i.DeductionQuantity > 0.0005);
                int withWaste = measured.Count(i => i.WastageQuantity > 0.0005);

                b.SetSubtitle($"Active standard: {std.DisplayName} ({std.Id}) — gross → net measured derivation.");
                b.AddSection("SUMMARY")
                 .Metric("Measured model lines", measured.Count.ToString(CultureInfo.InvariantCulture))
                 .Metric("Lines net of openings/voids", withDeductions.ToString(CultureInfo.InvariantCulture),
                         "deductions applied")
                 .Metric("Lines with a wastage allowance", withWaste.ToString(CultureInfo.InvariantCulture));

                var rows = new List<string[]>();
                foreach (var i in measured
                             .Where(x => x.DeductionQuantity > 0.0005 || x.WastageQuantity > 0.0005)
                             .Take(200))
                {
                    rows.Add(new[]
                    {
                        i.BOQLineRef ?? "",
                        AuditTrunc(i.ItemName, 34),
                        i.Unit ?? "",
                        i.GrossQuantity.ToString("N2", CultureInfo.InvariantCulture),
                        i.DeductionQuantity > 0.0005 ? i.DeductionQuantity.ToString("N2", CultureInfo.InvariantCulture) : "",
                        i.WastageQuantity > 0.0005 ? i.WastageQuantity.ToString("N2", CultureInfo.InvariantCulture) : "",
                        i.Quantity.ToString("N2", CultureInfo.InvariantCulture)
                    });
                }

                if (rows.Count > 0)
                {
                    b.AddSection($"GROSS → NET  ({rows.Count} line(s) with a deduction or wastage)")
                     .Table(new[] { "Ref", "Item", "Unit", "Gross", "Deduct", "Waste", "Net" }, rows);
                }
                else
                {
                    b.AddSection("GROSS → NET")
                     .Text("No openings/voids or wastage applied on this bill — every measured line is gross = net. " +
                           "Walls net of openings appear here once a wall hosts a door/window over the de-minimis.");
                }

                ShowInlineResult(b);
            }
            catch (Exception ex) { StingLog.Error("BOQ ShowMeasurementAudit", ex); }
        }

        private static string AuditTrunc(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        // ── Phase 2B — live rate feeds (inline config + fetch/accept) ───────

        /// <summary>Inline BCIS / Planscape feed config form. Persists to
        /// _BIM_COORD/rate_feeds.json (API key in the project file only).</summary>
        private void ShowRateFeedsForm(string label)
        {
            if (_actionReportHost == null || Doc == null) return;
            try
            {
                if (_mainTabs != null && _actionsTab != null) _mainTabs.SelectedItem = _actionsTab;
                if (_actionReportTitle != null) _actionReportTitle.Text = label;

                var cfg = StingTools.BOQ.Rates.RateFeedsStore.Load(Doc);

                var sp = new StackPanel { Margin = new Thickness(14) };
                sp.Children.Add(new TextBlock
                {
                    Text = "Live rate feeds", FontSize = 13, FontWeight = FontWeights.Bold,
                    Foreground = NavyBrush, Margin = new Thickness(0, 0, 0, 6)
                });
                sp.Children.Add(new TextBlock
                {
                    Text = "Configure external price books. Settings save to the project file only "
                         + "(_BIM_COORD/rate_feeds.json) — the BCIS API key is never committed to the repo. "
                         + "Use 'Fetch live rates' to pull candidates onto the bill.",
                    FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 12)
                });

                TextBox Row(string lbl, string val, double w = 320)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                    row.Children.Add(new TextBlock { Text = lbl, Width = 120, FontSize = 11,
                        Foreground = NavyBrush, VerticalAlignment = VerticalAlignment.Center });
                    var tb = new TextBox { Text = val ?? "", Width = w, Height = 24, FontSize = 11 };
                    row.Children.Add(tb);
                    sp.Children.Add(row);
                    return tb;
                }

                var bcisCb = new CheckBox { Content = "Enable BCIS feed", IsChecked = cfg.BcisEnabled,
                    FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = NavyBrush, Margin = new Thickness(0, 0, 0, 8) };
                sp.Children.Add(bcisCb);
                var urlTb = Row("Base URL", cfg.BcisBaseUrl);
                var keyTb = Row("API key", cfg.BcisApiKey);
                var ttlTb = Row("Cache TTL (min)", cfg.BcisTtlMinutes.ToString(CultureInfo.InvariantCulture), 120);

                var planCb = new CheckBox { Content = "Enable Planscape feed (uses the current Planscape login)",
                    IsChecked = cfg.PlanscapeEnabled, FontSize = 12, FontWeight = FontWeights.SemiBold,
                    Foreground = NavyBrush, Margin = new Thickness(0, 10, 0, 10) };
                sp.Children.Add(planCb);

                var saveBtn = new Button { Content = "Save feed settings", FontSize = 12, FontWeight = FontWeights.Bold,
                    Height = 28, MinWidth = 140, Background = GreenBrush, Foreground = Brushes.White,
                    BorderThickness = new Thickness(0), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
                saveBtn.Click += (s, e) =>
                {
                    try
                    {
                        cfg.BcisEnabled = bcisCb.IsChecked == true;
                        cfg.BcisBaseUrl = urlTb.Text?.Trim() ?? "";
                        cfg.BcisApiKey = keyTb.Text ?? "";
                        if (int.TryParse(ttlTb.Text?.Trim(), out int ttl) && ttl > 0) cfg.BcisTtlMinutes = ttl;
                        cfg.PlanscapeEnabled = planCb.IsChecked == true;
                        bool ok = StingTools.BOQ.Rates.RateFeedsStore.Save(Doc, cfg);
                        StingTools.BOQ.Rates.RateProviderRegistry.Invalidate();

                        var b = StingResultPanel.Create("Rate feeds");
                        b.SetSubtitle(ok ? "Saved to _BIM_COORD/rate_feeds.json." : "Could not save (unsaved project?).");
                        b.AddSection("ACTIVE FEEDS")
                         .PassFail("BCIS", cfg.BcisEnabled, cfg.BcisEnabled ? cfg.BcisBaseUrl : "disabled")
                         .PassFail("Planscape", cfg.PlanscapeEnabled, cfg.PlanscapeEnabled ? "uses current login" : "disabled")
                         .Text("Now run 'Fetch live rates' to pull candidate rates onto the bill.");
                        ShowInlineResult(b);
                    }
                    catch (Exception ex) { StingLog.Error("BOQ RateFeeds save", ex); }
                };
                sp.Children.Add(saveBtn);

                _actionReportHost.Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = sp
                };
            }
            catch (Exception ex) { StingLog.Error("BOQ ShowRateFeedsForm", ex); }
        }

        private sealed class LiveCandidate
        {
            public string Source;
            public double RateUgx;
            public int Confidence;
            public DateTime AsOf;
            public string Provenance;
        }

        private sealed class LiveRateRow
        {
            public BOQLineItem Line;
            public string Ref, Item, Unit, Category, Discipline, Location;
            public double CurrentRateUgx;
            public string CurrentSource;
            public int CurrentConfidence;
            public bool IsOverride;
            public List<LiveCandidate> Candidates = new List<LiveCandidate>();
            public LiveCandidate Best =>
                Candidates.Count == 0 ? null : Candidates.OrderByDescending(c => c.Confidence).ThenByDescending(c => c.RateUgx).First();
        }

        /// <summary>Fetch candidate rates from the configured feeds (off the UI
        /// thread — feeds are Revit-free) and render a per-line accept surface.</summary>
        private void ShowFetchLiveRates(string label)
        {
            if (_actionReportHost == null) return;
            try
            {
                if (_mainTabs != null && _actionsTab != null) _mainTabs.SelectedItem = _actionsTab;
                if (_actionReportTitle != null) _actionReportTitle.Text = label;

                if (_boq == null || _boq.AllItems == null || _boq.AllItems.Count == 0)
                {
                    var b0 = StingResultPanel.Create("Fetch live rates")
                        .SetSubtitle("No bill built yet — press Refresh first.");
                    ShowInlineResult(b0); return;
                }

                var feeds = StingTools.BOQ.Rates.RateProviderRegistry.GetLiveFeedProviders(Doc);
                if (feeds.Count == 0)
                {
                    var b1 = StingResultPanel.Create("Fetch live rates");
                    b1.SetSubtitle("No live feed is enabled.");
                    b1.AddSection("SETUP").Text("Enable BCIS or Planscape first via the 'Rate feeds' action, then re-run.");
                    b1.Action("Open Rate feeds", "Configure a live feed", _ => ShowRateFeedsForm("Rate feeds"));
                    ShowInlineResult(b1); return;
                }

                // Snapshot model rows on the WPF thread (no Revit reads — the line
                // items already carry every field the feeds need).
                double ugxPerUsd = _boq.ExchangeRateUgxPerUsd > 0 ? _boq.ExchangeRateUgxPerUsd : 3700.0;
                double ugxPerGbp = TagConfig.GetConfigDouble("UGX_PER_GBP", 4700.0);
                var rows = _boq.AllItems
                    .Where(i => i.Source == BOQRowSource.Model && i.Quantity > 0)
                    .GroupBy(i => (i.Category ?? "") + "|" + (i.Unit ?? ""))
                    .Select(g => g.First())   // one representative request per category|unit
                    .Select(i => new LiveRateRow
                    {
                        Line = i, Ref = i.BOQLineRef ?? "", Item = i.ItemName ?? "", Unit = i.Unit ?? "",
                        Category = i.Category ?? "", Discipline = i.Discipline ?? "", Location = i.Location ?? "",
                        CurrentRateUgx = i.RateUGX, CurrentSource = i.RateSource ?? "", CurrentConfidence = i.RateConfidence,
                        IsOverride = string.Equals(i.RateSource, "Override", StringComparison.OrdinalIgnoreCase)
                    })
                    .ToList();

                _actionReportHost.Child = new TextBlock
                {
                    Text = $"Fetching live rates for {rows.Count} distinct category/unit group(s) from "
                         + $"{feeds.Count} feed(s)…", Foreground = Brushes.Gray, FontSize = 11,
                    Margin = new Thickness(14), TextWrapping = TextWrapping.Wrap
                };

                System.Threading.Tasks.Task.Run(() =>
                {
                    foreach (var r in rows)
                    {
                        var req = new StingTools.BOQ.Rates.RateRequest
                        {
                            CategoryName = r.Category, Discipline = r.Discipline, Unit = r.Unit,
                            LocationCode = r.Location, CurrencyCode = "UGX", AsOf = DateTime.UtcNow
                        };
                        foreach (var p in feeds)
                        {
                            try
                            {
                                var lk = p.Resolve(req);
                                if (lk == null || lk.UnitRate <= 0) continue;
                                r.Candidates.Add(new LiveCandidate
                                {
                                    Source = MapFeedLabel(p.Id),
                                    RateUgx = ToUgx(lk.UnitRate, lk.CurrencyCode, ugxPerUsd, ugxPerGbp),
                                    Confidence = lk.Confidence,
                                    AsOf = lk.FetchedUtc,
                                    Provenance = lk.Provenance ?? ""
                                });
                            }
                            catch (Exception ex) { StingLog.WarnRateLimited("LiveFetch", $"feed {p.Id}: {ex.Message}"); }
                        }
                    }
                    foreach (var p in feeds) (p as IDisposable)?.Dispose();

                    Dispatcher.BeginInvoke(new Action(() => RenderLiveRates(label, rows, feeds.Count)));
                });
            }
            catch (Exception ex) { StingLog.Error("BOQ ShowFetchLiveRates", ex); }
        }

        private static string MapFeedLabel(string providerId)
        {
            switch (providerId)
            {
                case "bcis-http": return "BCIS";
                case "planscape": return "Planscape";
                default: return providerId ?? "Feed";
            }
        }

        private static double ToUgx(double rate, string ccy, double ugxPerUsd, double ugxPerGbp)
        {
            switch ((ccy ?? "UGX").ToUpperInvariant())
            {
                case "USD": return rate * ugxPerUsd;
                case "GBP": return rate * ugxPerGbp;
                default:    return rate; // UGX or unknown — treat as base
            }
        }

        private void RenderLiveRates(string label, List<LiveRateRow> rows, int feedCount)
        {
            try
            {
                if (_actionReportHost == null) return;
                if (_actionReportTitle != null) _actionReportTitle.Text = label;

                var withCandidates = rows.Where(r => r.Candidates.Count > 0).ToList();
                var sp = new StackPanel { Margin = new Thickness(14) };
                sp.Children.Add(new TextBlock
                {
                    Text = "Live rate candidates", FontSize = 13, FontWeight = FontWeights.Bold,
                    Foreground = NavyBrush, Margin = new Thickness(0, 0, 0, 4)
                });
                sp.Children.Add(new TextBlock
                {
                    Text = withCandidates.Count == 0
                        ? $"No feed returned a rate ({feedCount} feed(s) reachable but no match, or offline). "
                          + "Existing rates are unchanged."
                        : $"{withCandidates.Count} category group(s) have a live candidate. "
                          + "Best = highest confidence. Manual overrides are protected (no auto-apply). "
                          + "UGX. ⚠ = confidence < 60.",
                    FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                });

                if (withCandidates.Count > 0)
                {
                    var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });

                    void H(string t, int c)
                    {
                        var x = new TextBlock { Text = t, FontSize = 10, FontWeight = FontWeights.Bold,
                            Foreground = NavyBrush, Margin = new Thickness(0, 0, 6, 6) };
                        Grid.SetRow(x, 0); Grid.SetColumn(x, c); grid.Children.Add(x);
                    }
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    H("Category", 0); H("Unit", 1); H("Current (source)", 2); H("Best live candidate", 3); H("", 4);

                    var amber = new SolidColorBrush(Color.FromRgb(180, 120, 0));
                    int rr = 1;
                    foreach (var r in withCandidates)
                    {
                        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        var best = r.Best;

                        var catTb = new TextBlock { Text = AuditTrunc(r.Category, 28), FontSize = 11,
                            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 6, 2) };
                        var unitTb = new TextBlock { Text = r.Unit, FontSize = 11,
                            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 6, 2) };
                        var curTb = new TextBlock
                        {
                            Text = $"UGX {r.CurrentRateUgx:N0}  ({r.CurrentSource}/{r.CurrentConfidence})",
                            FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 6, 2)
                        };
                        bool low = best.Confidence < 60;
                        var candTb = new TextBlock
                        {
                            Text = $"{(low ? "⚠ " : "")}UGX {best.RateUgx:N0}  ({best.Source}/{best.Confidence}, {best.AsOf:dd MMM})",
                            FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 6, 2),
                            Foreground = low ? amber : (Brush)new SolidColorBrush(Color.FromRgb(40, 50, 80)),
                            TextWrapping = TextWrapping.Wrap
                        };

                        Grid.SetRow(catTb, rr);  Grid.SetColumn(catTb, 0);  grid.Children.Add(catTb);
                        Grid.SetRow(unitTb, rr); Grid.SetColumn(unitTb, 1); grid.Children.Add(unitTb);
                        Grid.SetRow(curTb, rr);  Grid.SetColumn(curTb, 2);  grid.Children.Add(curTb);
                        Grid.SetRow(candTb, rr); Grid.SetColumn(candTb, 3); grid.Children.Add(candTb);

                        if (r.IsOverride)
                        {
                            var prot = new TextBlock { Text = "protected", FontSize = 10, FontStyle = FontStyles.Italic,
                                Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
                            Grid.SetRow(prot, rr); Grid.SetColumn(prot, 4); grid.Children.Add(prot);
                        }
                        else
                        {
                            var acc = new Button { Content = "Accept", FontSize = 11, Height = 22, Cursor = Cursors.Hand,
                                Margin = new Thickness(0, 2, 0, 2) };
                            var capRow = r;
                            acc.Click += (s, e) => { AcceptLiveRate(capRow, capRow.Best); acc.IsEnabled = false; acc.Content = "✓"; };
                            Grid.SetRow(acc, rr); Grid.SetColumn(acc, 4); grid.Children.Add(acc);
                        }
                        rr++;
                    }
                    sp.Children.Add(grid);

                    int eligible = withCandidates.Count(r => !r.IsOverride);
                    var bulkBtn = new Button
                    {
                        Content = $"Accept best live rate on {eligible} line group(s)", FontSize = 12, FontWeight = FontWeights.Bold,
                        Height = 28, MinWidth = 220, Background = GreenBrush, Foreground = Brushes.White,
                        BorderThickness = new Thickness(0), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left,
                        IsEnabled = eligible > 0
                    };
                    bulkBtn.Click += (s, e) =>
                    {
                        int applied = 0;
                        foreach (var r in withCandidates.Where(x => !x.IsOverride && x.Best != null))
                        { AcceptLiveRate(r, r.Best); applied++; }
                        bulkBtn.Content = $"✓ Applied to {applied} group(s)";
                        bulkBtn.IsEnabled = false;
                    };
                    sp.Children.Add(bulkBtn);

                    int protectedCount = withCandidates.Count(r => r.IsOverride);
                    if (protectedCount > 0)
                        sp.Children.Add(new TextBlock
                        {
                            Text = $"{protectedCount} line group(s) carry a manual Override and were left untouched — "
                                 + "edit the rate inline if you want to replace it.",
                            FontSize = 10, FontStyle = FontStyles.Italic, Foreground = Brushes.Gray,
                            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0)
                        });
                }

                _actionReportHost.Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = sp
                };
            }
            catch (Exception ex) { StingLog.Error("BOQ RenderLiveRates", ex); }
        }

        /// <summary>Apply a live candidate to every model line in the same
        /// category/unit group (the snapshot row is one representative). Persists
        /// via the model-override sidecar (survives rebuild) and re-totals.</summary>
        private void AcceptLiveRate(LiveRateRow row, LiveCandidate cand)
        {
            if (row?.Line == null || cand == null || _boq == null) return;
            try
            {
                double ugxPerUsd = _boq.ExchangeRateUgxPerUsd > 0 ? _boq.ExchangeRateUgxPerUsd : 3700.0;
                double rateUgx = Math.Round(cand.RateUgx, 0);
                double rateUsd = ugxPerUsd > 0 ? Math.Round(rateUgx / ugxPerUsd, 2) : 0;

                // Apply to every model line sharing this category/unit (skip
                // manual overrides — never silently clobbered).
                var targets = _boq.AllItems.Where(i =>
                    i.Source == BOQRowSource.Model &&
                    string.Equals(i.Category ?? "", row.Category, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(i.Unit ?? "", row.Unit, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(i.RateSource, "Override", StringComparison.OrdinalIgnoreCase)).ToList();

                foreach (var line in targets)
                {
                    line.RateUGX = rateUgx;
                    line.RateUSD = rateUsd;
                    line.RateSource = cand.Source;          // "BCIS" / "Planscape"
                    line.RateConfidence = cand.Confidence;
                    line.LastCosted = DateTime.UtcNow;

                    // Persist so a full rebuild re-applies it (RateSource carried).
                    try
                    {
                        BOQCostManager.UpsertModelOverride(Doc, new BOQModelOverride
                        {
                            UniqueId = line.UniqueId,
                            ElementId = line.RevitElementId,
                            RateUGX = rateUgx,
                            RateUSD = rateUsd,
                            NRM2Paragraph = line.ResolvedNRM2Paragraph ?? "",
                            Note = line.Note ?? "",
                            RateSource = cand.Source,
                            ModifiedBy = Environment.UserName ?? ""
                        });
                    }
                    catch (Exception ex) { StingLog.WarnRateLimited("AcceptLive.Persist", $"persist {line.RevitElementId}: {ex.Message}"); }
                }

                RefreshDisplay();   // re-render + re-total from _boq (no Revit rebuild)
            }
            catch (Exception ex) { StingLog.Error("BOQ AcceptLiveRate", ex); }
        }

        // ── Phase 2C — drift check (live bill vs last snapshot) + re-price ──

        /// <summary>Compare the live bill to the last saved snapshot and render
        /// the element-linked changes + a re-price action, inline.</summary>
        private void ShowDriftCheck(string label)
        {
            if (_actionReportHost == null || Doc == null) return;
            try
            {
                if (_actionReportTitle != null) _actionReportTitle.Text = label;

                var b = StingResultPanel.Create("Drift check");
                if (_boq == null || _boq.AllItems == null || _boq.AllItems.Count == 0)
                {
                    b.SetSubtitle("No bill built yet — press Refresh first."); ShowInlineResult(b); return;
                }

                var snaps = BOQCostManager.ListSnapshots(Doc);
                if (snaps.Count == 0)
                {
                    b.SetSubtitle("No saved snapshot to compare against.");
                    b.AddSection("BASELINE").Text("Save a snapshot first (Save Snapshot) to set the baseline this check drifts from.");
                    ShowInlineResult(b); return;
                }

                var last = snaps[0];
                var snap = BOQCostManager.LoadSnapshot(last.Path);
                string liveCk = StingTools.BOQ.Sync.BoqSnapshotHasher.ComputeChecksum(_boq);
                string snapCk = !string.IsNullOrEmpty(last.Checksum)
                    ? last.Checksum
                    : (snap != null ? StingTools.BOQ.Sync.BoqSnapshotHasher.ComputeChecksum(snap) : "");
                bool checksumChanged = !string.Equals(liveCk, snapCk, StringComparison.Ordinal);

                var diff = BOQCostManager.CompareLiveToSnapshot(last.Path, _boq);

                // Per-line classification (for element-linked findings + reprice ids).
                var snapIndex = new Dictionary<string, BOQLineItem>(StringComparer.OrdinalIgnoreCase);
                if (snap != null)
                    foreach (var s in snap.AllItems) snapIndex[BOQCostManager.LineKey(s)] = s;

                var newLines = new List<BOQLineItem>();
                var qtyLines = new List<BOQLineItem>();
                var rateLines = new List<BOQLineItem>();
                foreach (var li in _boq.AllItems.Where(i => i.Source == BOQRowSource.Model))
                {
                    string k = BOQCostManager.LineKey(li);
                    if (!snapIndex.TryGetValue(k, out var s)) { newLines.Add(li); continue; }
                    bool qtyCh = s.Quantity > 0 && Math.Abs(li.Quantity - s.Quantity) / s.Quantity > 0.001;
                    bool rateCh = s.RateUGX > 0 && Math.Abs(li.RateUGX - s.RateUGX) / s.RateUGX > 0.01;
                    if (qtyCh) qtyLines.Add(li);
                    else if (rateCh) rateLines.Add(li);
                }
                var liveKeys = new HashSet<string>(_boq.AllItems.Select(BOQCostManager.LineKey), StringComparer.OrdinalIgnoreCase);
                var removed = snap?.AllItems
                    .Where(i => i.Source == BOQRowSource.Model && !liveKeys.Contains(BOQCostManager.LineKey(i)))
                    .ToList() ?? new List<BOQLineItem>();

                int changed = newLines.Count + qtyLines.Count + rateLines.Count + removed.Count;
                b.SetSubtitle(checksumChanged
                    ? $"Bill drifted from snapshot '{last.Label}' ({last.Date:dd MMM yyyy})."
                    : $"No drift — bill matches snapshot '{last.Label}'.");
                b.AddSection("SUMMARY")
                 .Metric("Checksum", checksumChanged ? "CHANGED" : "match")
                 .Metric("Net Δ cost", $"UGX {diff.NetChange:N0}", $"{diff.NetChangePct:+0.0;-0.0;0.0}%")
                 .Metric("Net Δ carbon", $"{diff.NetCarbonChange:N0} kg")
                 .Metric("Changed lines", changed.ToString(CultureInfo.InvariantCulture));

                void AddGroup(string title, List<BOQLineItem> ls, bool linked)
                {
                    if (ls.Count == 0) return;
                    b.AddSection(title);
                    foreach (var li in ls.Take(50))
                    {
                        string txt = $"{(string.IsNullOrEmpty(li.BOQLineRef) ? "" : li.BOQLineRef + "  ")}"
                                   + $"{AuditTrunc(li.ItemName, 38)} — {li.Quantity:N2} {li.Unit} @ UGX {li.RateUGX:N0}";
                        if (linked && li.RevitElementId > 0) b.Finding(txt, li.RevitElementId);
                        else b.Text(txt);
                    }
                    if (ls.Count > 50) b.Text($"… and {ls.Count - 50} more.");
                }
                AddGroup($"QTY MOVED ({qtyLines.Count})", qtyLines, true);
                AddGroup($"NEW ({newLines.Count})", newLines, true);
                AddGroup($"RATE REVISED ({rateLines.Count})", rateLines, true);
                AddGroup($"REMOVED ({removed.Count})", removed, false);

                if (changed == 0)
                    b.AddSection("RESULT").Text("Nothing changed since the last snapshot.");

                // Re-price action — qty-moved + new, non-override only.
                var repriceIds = qtyLines.Concat(newLines)
                    .Where(li => !string.Equals(li.RateSource, "Override", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(li => (li.ConstituentElementIds != null && li.ConstituentElementIds.Count > 0)
                        ? li.ConstituentElementIds
                        : (li.RevitElementId > 0 ? new List<long> { li.RevitElementId } : new List<long>()))
                    .Where(id => id > 0).Distinct().ToList();
                if (repriceIds.Count > 0)
                {
                    b.Action($"Re-price {repriceIds.Count} changed element(s)",
                        "Re-run the rate chain (incl. live feeds) on qty-moved + new lines; overrides protected; re-totals after.",
                        _ =>
                        {
                            try
                            {
                                StingCommandHandler.SetExtraParam("RepriceElementIds", string.Join(",", repriceIds));
                                DispatchInline("Re-price changed lines", "Cost_RepriceDrift");
                            }
                            catch (Exception ex) { StingLog.Error("BOQ Drift reprice dispatch", ex); }
                        });
                }

                ShowInlineResult(b);
            }
            catch (Exception ex) { StingLog.Error("BOQ ShowDriftCheck", ex); }
        }

        // ── Phase 2E — WBS/CBS map editor (inline) ──────────────────────────

        private void ShowWbsMapForm(string label)
        {
            if (_actionReportHost == null || Doc == null) return;
            try
            {
                if (_mainTabs != null && _actionsTab != null) _mainTabs.SelectedItem = _actionsTab;
                if (_actionReportTitle != null) _actionReportTitle.Text = label;

                var map = StingTools.BOQ.BoqWbsMapStore.Load(Doc);
                var working = map.Rules ?? new List<StingTools.BOQ.BoqWbsRule>();

                var editors = new List<(StingTools.BOQ.BoqWbsRule rule, TextBox cat, TextBox disc,
                    TextBox nrm2, TextBox lvl, TextBox zone, TextBox wbs, TextBox cbs)>();

                void Commit()
                {
                    foreach (var ed in editors)
                    {
                        ed.rule.MatchCategory = Norm(ed.cat.Text);
                        ed.rule.MatchDiscipline = Norm(ed.disc.Text);
                        ed.rule.MatchNrm2Section = Norm(ed.nrm2.Text);
                        ed.rule.MatchLevel = Norm(ed.lvl.Text);
                        ed.rule.MatchZone = Norm(ed.zone.Text);
                        ed.rule.Wbs = ed.wbs.Text?.Trim() ?? "";
                        ed.rule.Cbs = ed.cbs.Text?.Trim() ?? "";
                    }
                }

                Action render = null;
                render = () =>
                {
                    editors.Clear();
                    var sp = new StackPanel { Margin = new Thickness(14) };
                    sp.Children.Add(new TextBlock
                    {
                        Text = "WBS / CBS map", FontSize = 13, FontWeight = FontWeights.Bold,
                        Foreground = NavyBrush, Margin = new Thickness(0, 0, 0, 6)
                    });
                    sp.Children.Add(new TextBlock
                    {
                        Text = "First matching rule wins. Match fields are contains / case-insensitive; "
                             + "‘*’ or blank matches anything. Lines with no rule inherit the linked 4D "
                             + "task’s WBS. Group the bill by WBS / CBS via the Group dropdown.",
                        FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 10)
                    });

                    var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                    foreach (var w in new[] { 1.1, 0.7, 0.7, 0.8, 0.7, 0.9, 0.9, 0.32 })
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });

                    void H(string t, int c)
                    {
                        var x = new TextBlock { Text = t, FontSize = 10, FontWeight = FontWeights.Bold,
                            Foreground = NavyBrush, Margin = new Thickness(0, 0, 4, 6) };
                        Grid.SetRow(x, 0); Grid.SetColumn(x, c); grid.Children.Add(x);
                    }
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    H("Category", 0); H("Disc", 1); H("NRM2", 2); H("Level", 3); H("Zone", 4); H("WBS", 5); H("CBS", 6); H("", 7);

                    int r = 1;
                    foreach (var rule in working)
                    {
                        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        TextBox T(string val, int col)
                        {
                            var tb = new TextBox { Text = val ?? "", Height = 24, FontSize = 11, Margin = new Thickness(0, 2, 4, 2) };
                            Grid.SetRow(tb, r); Grid.SetColumn(tb, col); grid.Children.Add(tb);
                            return tb;
                        }
                        var cat = T(rule.MatchCategory, 0);
                        var disc = T(rule.MatchDiscipline, 1);
                        var nrm2 = T(rule.MatchNrm2Section, 2);
                        var lvl = T(rule.MatchLevel, 3);
                        var zone = T(rule.MatchZone, 4);
                        var wbs = T(rule.Wbs, 5);
                        var cbs = T(rule.Cbs, 6);
                        var del = new Button { Content = "×", Width = 22, Height = 22, FontSize = 12, Cursor = Cursors.Hand };
                        var capRule = rule;
                        del.Click += (s, e) => { Commit(); working.Remove(capRule); render(); };
                        Grid.SetRow(del, r); Grid.SetColumn(del, 7); grid.Children.Add(del);

                        editors.Add((rule, cat, disc, nrm2, lvl, zone, wbs, cbs));
                        r++;
                    }
                    sp.Children.Add(grid);

                    var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
                    var addBtn = new Button { Content = "+ Add rule", FontSize = 12, Height = 28, MinWidth = 90,
                        Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand };
                    addBtn.Click += (s, e) => { Commit(); working.Add(new StingTools.BOQ.BoqWbsRule()); render(); };
                    var saveBtn = new Button { Content = "Save map + Refresh", FontSize = 12, FontWeight = FontWeights.Bold,
                        Height = 28, MinWidth = 150, Background = GreenBrush, Foreground = Brushes.White,
                        BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
                    saveBtn.Click += (s, e) =>
                    {
                        try
                        {
                            Commit();
                            map.Rules = working;
                            StingTools.BOQ.BoqWbsMapStore.Save(Doc, map);
                            RefreshAsync();   // re-apply WBS/CBS to the bill
                            render();
                        }
                        catch (Exception ex) { StingLog.Error("BOQ WbsMap save", ex); }
                    };
                    btnRow.Children.Add(addBtn);
                    btnRow.Children.Add(saveBtn);
                    sp.Children.Add(btnRow);

                    _actionReportHost.Child = new ScrollViewer
                    {
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        Content = sp
                    };
                };
                render();
            }
            catch (Exception ex) { StingLog.Error("BOQ ShowWbsMapForm", ex); }
        }

        private static string Norm(string s)
        {
            s = (s ?? "").Trim();
            return string.IsNullOrEmpty(s) ? "*" : s;
        }

        // ── Phase 2E — ERP export (inline) ──────────────────────────────────

        private void ShowErpExport(string label)
        {
            if (_actionReportHost == null || Doc == null) return;
            try
            {
                if (_mainTabs != null && _actionsTab != null) _mainTabs.SelectedItem = _actionsTab;
                if (_actionReportTitle != null) _actionReportTitle.Text = label;

                if (_boq == null || _boq.AllItems == null || _boq.AllItems.Count == 0)
                {
                    var b0 = StingResultPanel.Create("Export to ERP").SetSubtitle("No bill built yet — press Refresh first.");
                    ShowInlineResult(b0); return;
                }

                var sp = new StackPanel { Margin = new Thickness(14) };
                sp.Children.Add(new TextBlock
                {
                    Text = "Export to ERP", FontSize = 13, FontWeight = FontWeights.Bold,
                    Foreground = NavyBrush, Margin = new Thickness(0, 0, 0, 6)
                });
                sp.Children.Add(new TextBlock
                {
                    Text = $"Writes a flat, import-ready CSV ({_boq.AllItems.Count} rows) with WBS · CBS · cost code · "
                         + "qty · rate · total · level · location · source · IfcGuid. Optionally also a Primavera "
                         + "P6 activity-cost XML grouped by WBS. The 8-sheet XLSX + IFC Qto exports are unchanged.",
                    FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                });

                var p6Cb = new CheckBox { Content = "Also write Primavera P6 activity-cost XML", IsChecked = false,
                    FontSize = 12, Foreground = NavyBrush, Margin = new Thickness(0, 0, 0, 10) };
                sp.Children.Add(p6Cb);

                var runBtn = new Button { Content = "Export", FontSize = 12, FontWeight = FontWeights.Bold,
                    Height = 28, MinWidth = 120, Background = GreenBrush, Foreground = Brushes.White,
                    BorderThickness = new Thickness(0), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
                runBtn.Click += (s, e) =>
                {
                    try
                    {
                        string dir = ResolveExportDir();
                        System.IO.Directory.CreateDirectory(dir);
                        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                        string csvPath = System.IO.Path.Combine(dir, $"boq_erp_{stamp}.csv");
                        StingTools.BOQ.BoqErpExporter.ExportCsv(_boq, csvPath);

                        string p6Path = null;
                        if (p6Cb.IsChecked == true)
                        {
                            p6Path = System.IO.Path.Combine(dir, $"boq_p6_{stamp}.xml");
                            StingTools.BOQ.BoqErpExporter.ExportP6Xml(_boq, p6Path);
                        }

                        int withWbs = _boq.AllItems.Count(i => !string.IsNullOrEmpty(i.WbsCode));
                        var b = StingResultPanel.Create("Export to ERP");
                        b.SetSubtitle("ERP export written.");
                        b.AddSection("RESULT")
                         .Metric("Rows", _boq.AllItems.Count.ToString(CultureInfo.InvariantCulture))
                         .Metric("Lines with WBS", withWbs.ToString(CultureInfo.InvariantCulture))
                         .Metric("Grand total", $"UGX {_boq.GrandTotalUGX:N0}");
                        if (p6Path != null)
                            b.AddSection("PRIMAVERA P6").Text($"P6 activity-cost XML: {p6Path}");
                        b.SetCsvPath(csvPath);   // renders an inline "Open file" button
                        ShowInlineResult(b);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error("BOQ ERP export", ex);
                        var b = StingResultPanel.Create("Export to ERP").SetSubtitle($"Export failed: {ex.Message}");
                        ShowInlineResult(b);
                    }
                };
                sp.Children.Add(runBtn);

                _actionReportHost.Child = sp;
            }
            catch (Exception ex) { StingLog.Error("BOQ ShowErpExport", ex); }
        }

        /// <summary>Export folder — &lt;project&gt;/_BIM_COORD/exports (falls back to temp).</summary>
        private string ResolveExportDir()
        {
            try
            {
                string parent = System.IO.Path.GetDirectoryName(Doc?.PathName ?? "");
                if (!string.IsNullOrEmpty(parent))
                    return System.IO.Path.Combine(parent, "_BIM_COORD", "exports");
            }
            catch (Exception ex) { StingLog.Warn($"ResolveExportDir: {ex.Message}"); }
            return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "STING_BOQ_exports");
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
                case "BOQQsExport":
                {
                    // P0.3 — priced/unpriced was a TaskDialog; render it inline.
                    ShowInlineForm(label, tag, new List<BoqFormField>
                    {
                        new BoqFormField { Key = "QsExportPriced", Label = "Pricing", Kind = BoqFormKind.Combo,
                            Options = new List<(string, string)>
                            {
                                ("Priced — include current rates", "1"),
                                ("Unpriced — blank for the QS to price", "0"),
                            } },
                    }, get => StingCommandHandler.SetExtraParam("QsExportPriced", get("QsExportPriced")));
                    return true;
                }
                case "PaymentCert_Issue":
                {
                    // P0.3 — contract-form picker rendered inline (no popup). Values
                    // are the ContractForm enum names PaymentCertIssueCommand parses.
                    ShowInlineForm(label, tag, new List<BoqFormField>
                    {
                        new BoqFormField { Key = "CertContractForm", Label = "Contract form", Kind = BoqFormKind.Combo,
                            Options = new List<(string, string)>
                            {
                                ("NEC4 ECC", "NEC4"),
                                ("JCT 2024 Standard Building Contract", "JCT2024"),
                                ("FIDIC Red Book 2017", "FIDIC2017Red"),
                            } },
                    }, get => StingCommandHandler.SetExtraParam("CertContractForm", get("CertContractForm")));
                    return true;
                }
                case "Cost_SetMeasurementStandard":
                {
                    // P0.3 — measurement-standard picker rendered inline. Options come
                    // from the same registry the command validates against.
                    var stdOpts = new List<(string, string)>();
                    try
                    {
                        foreach (var s in StingTools.BOQ.MeasurementStandard.MeasurementStandardRegistry.All())
                            stdOpts.Add(($"{s.DisplayName}  ({s.Version})", s.Id));
                    }
                    catch (Exception ex) { StingLog.Warn($"BOQ standard options: {ex.Message}"); }
                    if (stdOpts.Count == 0) stdOpts.Add(("NRM2", "nrm2"));
                    ShowInlineForm(label, tag, new List<BoqFormField>
                    {
                        new BoqFormField { Key = "MeasStandardId", Label = "Standard", Kind = BoqFormKind.Combo, Options = stdOpts },
                    }, get => StingCommandHandler.SetExtraParam("MeasStandardId", get("MeasStandardId")));
                    return true;
                }
                case "Cost_RunWorkflow":
                {
                    // P0.3 — preset picker rendered inline. Options come from the same
                    // discovery the command uses (CostRunWorkflowCommand.DiscoverBoqPresets).
                    var wfOpts = new List<(string, string)>();
                    try
                    {
                        foreach (var p in StingTools.Commands.Cost.CostRunWorkflowCommand.DiscoverBoqPresets())
                            wfOpts.Add((p.Name ?? System.IO.Path.GetFileNameWithoutExtension(p.Path), p.Path));
                    }
                    catch (Exception ex) { StingLog.Warn($"BOQ cost-workflow list: {ex.Message}"); }
                    // No presets — let the command surface its own NO PRESETS panel.
                    if (wfOpts.Count == 0) return false;
                    ShowInlineForm(label, tag, new List<BoqFormField>
                    {
                        new BoqFormField { Key = "CostWorkflowPath", Label = "Workflow preset", Kind = BoqFormKind.Combo, Options = wfOpts },
                    }, get => StingCommandHandler.SetExtraParam("CostWorkflowPath", get("CostWorkflowPath")));
                    return true;
                }
                case "CostPlan_Create":
                {
                    // P0.3 — building-type picker + GIFA TaskDialog rendered inline.
                    var btOpts = new List<(string, string)>();
                    try
                    {
                        foreach (var b in StingTools.Core.CostPlan.CostPlanRegistry.Get(Doc).BuildingTypes)
                            btOpts.Add((b, b));
                    }
                    catch (Exception ex) { StingLog.Warn($"BOQ cost-plan building types: {ex.Message}"); }
                    // No benchmarks loaded — let the command surface its own NO BENCHMARKS panel.
                    if (btOpts.Count == 0) return false;
                    string gifaDefault = SuggestGifaM2().ToString("F0", CultureInfo.InvariantCulture);
                    ShowInlineForm(label, tag, new List<BoqFormField>
                    {
                        new BoqFormField { Key = "CostPlanBuildingType", Label = "Building type", Kind = BoqFormKind.Combo, Options = btOpts },
                        new BoqFormField { Key = "CostPlanGifa", Label = "GIFA (m²)", Kind = BoqFormKind.Number, Default = gifaDefault },
                    }, get =>
                    {
                        StingCommandHandler.SetExtraParam("CostPlanBuildingType", get("CostPlanBuildingType"));
                        StingCommandHandler.SetExtraParam("CostPlanGifa", get("CostPlanGifa"));
                    });
                    return true;
                }
                case "CostPlan_Compare":
                case "CostPlan_Export":
                {
                    // P0.3 — saved-plan picker rendered inline (combo of plan files).
                    var planOpts = new List<(string, string)>();
                    try
                    {
                        foreach (var p in StingTools.Core.CostPlan.CostPlanEngine.ListPlans(Doc))
                            planOpts.Add((System.IO.Path.GetFileNameWithoutExtension(p), p));
                    }
                    catch (Exception ex) { StingLog.Warn($"BOQ cost-plan list: {ex.Message}"); }
                    // No saved plans — let the command surface its own NO PLANS panel.
                    if (planOpts.Count == 0) return false;
                    ShowInlineForm(label, tag, new List<BoqFormField>
                    {
                        new BoqFormField { Key = "CostPlanPath", Label = "Cost plan", Kind = BoqFormKind.Combo, Options = planOpts },
                    }, get => StingCommandHandler.SetExtraParam("CostPlanPath", get("CostPlanPath")));
                    return true;
                }
                case "Variation_FromDiff":
                {
                    // P0.3 — the 6-step Variation picker chain rendered as one inline
                    // form (two snapshot combos + contract/kind/reason/liability/EOT
                    // combos + a free-text rationale). Values are enum names the
                    // command parses; an empty liability means auto-suggest.
                    var snaps = BOQCostManager.ListSnapshots(Doc);
                    // < 2 snapshots — let the command surface its own NEED SNAPSHOTS panel.
                    if (snaps.Count < 2) return false;
                    var snapOpts = snaps
                        .Select(s => ($"{s.Type} · {s.Label} · {s.Date:yyyy-MM-dd HH:mm} · UGX {s.GrandTotalUGX:N0}", s.Path))
                        .ToList();
                    ShowInlineForm(label, tag, new List<BoqFormField>
                    {
                        new BoqFormField { Key = "VarSnapA", Label = "Baseline (A)", Kind = BoqFormKind.Combo, Options = snapOpts },
                        new BoqFormField { Key = "VarSnapB", Label = "Revised (B)", Kind = BoqFormKind.Combo, Options = snapOpts },
                        new BoqFormField { Key = "VarContractForm", Label = "Contract form", Kind = BoqFormKind.Combo,
                            Options = new List<(string, string)>
                            {
                                ("JCT 2024", "JCT2024"), ("JCT 2016", "JCT2016"), ("NEC4 ECC", "NEC4"),
                                ("FIDIC 2017 Red", "FIDIC2017Red"), ("FIDIC 2017 Yellow", "FIDIC2017Yellow"),
                                ("FIDIC 2017 Silver", "FIDIC2017Silver"), ("GC/Works", "GCWorks"), ("Bespoke", "Bespoke"),
                            } },
                        new BoqFormField { Key = "VarKind", Label = "Kind", Kind = BoqFormKind.Combo,
                            Options = new List<(string, string)>
                            {
                                ("Architect's / engineer's instruction", "Instruction"),
                                ("NEC4 compensation event", "CompensationEvent"),
                                ("FIDIC engineer instruction", "EngineerInstruction"),
                                ("Contractor claim", "ContractorClaim"),
                            } },
                        new BoqFormField { Key = "VarReason", Label = "Reason", Kind = BoqFormKind.Combo,
                            Options = new List<(string, string)>
                            {
                                ("Design change", "DesignChange"), ("Client request", "ClientRequest"),
                                ("Site condition", "SiteCondition"), ("Statutory change", "StatutoryChange"),
                                ("Error / omission", "ErrorOmission"), ("Contractor proposal", "ContractorProposal"),
                                ("Scope addition", "ScopeAddition"), ("Scope omission", "ScopeOmission"),
                                ("Specification", "Specification"), ("Quality", "Quality"),
                                ("Programme change", "ProgrammeChange"), ("Other", "Other"),
                            } },
                        new BoqFormField { Key = "VarLiability", Label = "Liability", Kind = BoqFormKind.Combo,
                            Options = new List<(string, string)>
                            {
                                ("(auto-suggest from reason)", ""), ("Employer / client", "Employer"),
                                ("Contractor", "Contractor"), ("Designer", "Designer"),
                                ("Shared", "Shared"), ("Force majeure", "ForceMajeure"),
                            } },
                        new BoqFormField { Key = "VarEot", Label = "EOT (days)", Kind = BoqFormKind.Combo,
                            Options = new List<(string, string)>
                            {
                                ("0 days", "0"), ("1 day", "1"), ("3 days", "3"), ("5 days", "5"), ("7 days", "7"),
                                ("14 days", "14"), ("21 days", "21"), ("30 days", "30"), ("60 days", "60"), ("90 days", "90"),
                            } },
                        new BoqFormField { Key = "VarReasonDetail", Label = "Rationale (optional)", Kind = BoqFormKind.Text },
                    }, get =>
                    {
                        StingCommandHandler.SetExtraParam("VarSnapA", get("VarSnapA"));
                        StingCommandHandler.SetExtraParam("VarSnapB", get("VarSnapB"));
                        StingCommandHandler.SetExtraParam("VarContractForm", get("VarContractForm"));
                        StingCommandHandler.SetExtraParam("VarKind", get("VarKind"));
                        StingCommandHandler.SetExtraParam("VarReason", get("VarReason"));
                        StingCommandHandler.SetExtraParam("VarLiability", get("VarLiability"));
                        StingCommandHandler.SetExtraParam("VarEot", get("VarEot"));
                        StingCommandHandler.SetExtraParam("VarReasonDetail", get("VarReasonDetail"));
                    });
                    return true;
                }
                case "PaymentCert_Approve":
                case "PaymentCert_ExportDoc":
                {
                    // P0.3 — cert picker rendered inline (combo of cert files). Approve
                    // offers only advanceable (Draft/Issued) certs; Cert Document offers
                    // all. Value = cert file path the command loads.
                    bool approveOnly = tag == "PaymentCert_Approve";
                    var certOpts = new List<(string, string)>();
                    try
                    {
                        foreach (var p in StingTools.Core.PaymentCert.PaymentCertEngine.ListCerts(Doc))
                        {
                            var c = StingTools.Core.PaymentCert.PaymentCertEngine.Load(p);
                            if (c == null) continue;
                            if (approveOnly
                                && c.Status != StingTools.Core.PaymentCert.PaymentCertStatus.Draft
                                && c.Status != StingTools.Core.PaymentCert.PaymentCertStatus.Issued) continue;
                            certOpts.Add(($"Cert #{c.CertNumber} ({c.Status}) — {c.Currency} {c.TotalPayable:N0} — {c.ValuationDate:yyyy-MM-dd}", p));
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"BOQ cert list: {ex.Message}"); }
                    // None available — let the command surface its own NO CERTS / NOTHING TO APPROVE panel.
                    if (certOpts.Count == 0) return false;
                    ShowInlineForm(label, tag, new List<BoqFormField>
                    {
                        new BoqFormField { Key = "CertPath", Label = "Certificate", Kind = BoqFormKind.Combo, Options = certOpts },
                    }, get => StingCommandHandler.SetExtraParam("CertPath", get("CertPath")));
                    return true;
                }
                case "Evm_Calculate":
                {
                    // P0.3 — planned %% (BCWS) band rendered inline. "earned" ⇒ the
                    // command uses BCWP (SV = 0); a number sets planned completion.
                    var evmOpts = new List<(string, string)> { ("Use earned % (SV = 0)", "earned") };
                    foreach (var p in new[] { 0, 10, 20, 25, 30, 40, 50, 60, 70, 75, 80, 90, 100 })
                        evmOpts.Add(($"{p}% planned", p.ToString()));
                    ShowInlineForm(label, tag, new List<BoqFormField>
                    {
                        new BoqFormField { Key = "EvmPlannedPct", Label = "Planned % (BCWS)", Kind = BoqFormKind.Combo, Options = evmOpts },
                    }, get => StingCommandHandler.SetExtraParam("EvmPlannedPct", get("EvmPlannedPct")));
                    return true;
                }
            }
            return false;
        }

        /// <summary>P0.3 — sum of model Room.Area (ft² → m²) as a GIFA default for the
        /// Cost Plan inline form. Reads run on the dockable pane's Revit-main thread.
        /// Returns 0 when there are no rooms (the form shows a blank, editable field).</summary>
        private double SuggestGifaM2()
        {
            double m2 = 0;
            try
            {
                if (Doc == null) return 0;
                var col = new FilteredElementCollector(Doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType();
                foreach (Element el in col)
                    if (el is Autodesk.Revit.DB.Architecture.Room room && room.Area > 0)
                        m2 += room.Area * 0.092903;   // ft² → m²
            }
            catch (Exception ex) { StingLog.Warn($"BOQ SuggestGifaM2: {ex.Message}"); }
            return Math.Round(m2);
        }

        // ── Star-rate first-principles build-up (dynamic inline form) ─────────
        //  Labour / plant / materials line items + overhead% / profit% → a
        //  StarRate serialized into the StarRateJson ExtraParam; the command
        //  deserializes + saves (no modal builder dialog). Blank rows ignored.
        private void ShowStarRateForm(string label)
        {
            if (_actionReportHost == null || Doc == null) return;
            try
            {
                if (_mainTabs != null && _actionsTab != null) _mainTabs.SelectedItem = _actionsTab;
                if (_actionReportTitle != null) _actionReportTitle.Text = label;

                string cc = _boq?.Currency ?? "UGX";

                var sp = new StackPanel { Margin = new Thickness(14) };
                sp.Children.Add(new TextBlock { Text = "Star rate — first-principles build-up",
                    FontSize = 13, FontWeight = FontWeights.Bold, Foreground = NavyBrush, Margin = new Thickness(0, 0, 0, 6) });
                sp.Children.Add(new TextBlock
                {
                    Text = "Build a rate from labour + plant + materials, then overhead and profit. "
                         + "Blank rows are ignored; click + add row for more lines.",
                    FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
                });

                var descTb = new TextBox { Height = 24, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) };
                sp.Children.Add(LabeledRow("Description", descTb));
                var unitTb = new TextBox { Text = "each", Height = 24, FontSize = 11, Margin = new Thickness(0, 0, 0, 10) };
                sp.Children.Add(LabeledRow("Unit", unitTb));

                var labour = new List<(TextBox res, TextBox num, TextBox rate)>();
                var plant = new List<(TextBox res, TextBox num, TextBox rate)>();
                var materials = new List<(TextBox res, TextBox num, TextBox rate)>();

                sp.Children.Add(BuildStarRateSection("LABOUR", $"Resource · hours · {cc} per hr", labour));
                sp.Children.Add(BuildStarRateSection("PLANT", $"Resource · hours · {cc} per hr", plant));
                sp.Children.Add(BuildStarRateSection("MATERIALS", $"Resource · qty · {cc} per unit", materials));

                var ohTb = new TextBox { Text = "8", Height = 24, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) };
                sp.Children.Add(LabeledRow("Overhead %", ohTb));
                var profitTb = new TextBox { Text = "5", Height = 24, FontSize = 11, Margin = new Thickness(0, 0, 0, 10) };
                sp.Children.Add(LabeledRow("Profit %", profitTb));

                var runBtn = new Button
                {
                    Content = "Run", FontSize = 12, FontWeight = FontWeights.Bold, Height = 30, MinWidth = 100,
                    HorizontalAlignment = HorizontalAlignment.Left, Background = GreenBrush, Foreground = Brushes.White,
                    BorderThickness = new Thickness(0), Cursor = Cursors.Hand
                };
                runBtn.Click += (s, e) =>
                {
                    try
                    {
                        double ParseD(string t, double def = 0)
                            => double.TryParse(t?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : def;

                        var rate = new StingTools.Core.Variation.StarRate
                        {
                            Description = descTb.Text?.Trim() ?? "",
                            Unit = string.IsNullOrWhiteSpace(unitTb.Text) ? "each" : unitTb.Text.Trim(),
                            Currency = cc,
                            OverheadPercent = ParseD(ohTb.Text, 8),
                            ProfitPercent = ParseD(profitTb.Text, 5),
                        };
                        void Collect(List<(TextBox res, TextBox num, TextBox rate)> rows,
                            List<StingTools.Core.Variation.StarRateLine> dest, string unit, bool isMaterials)
                        {
                            foreach (var row in rows)
                            {
                                string res = row.res.Text?.Trim() ?? "";
                                double num = ParseD(row.num.Text);
                                double r = ParseD(row.rate.Text);
                                if (res.Length == 0 && num == 0 && r == 0) continue;
                                var line = new StingTools.Core.Variation.StarRateLine { Resource = res, UnitRate = r, Unit = unit };
                                if (isMaterials) line.Quantity = num; else line.Hours = num;
                                dest.Add(line);
                            }
                        }
                        Collect(labour, rate.LabourLines, "hr", false);
                        Collect(plant, rate.PlantLines, "hr", false);
                        Collect(materials, rate.MaterialsLines, "unit", true);

                        if (rate.LabourLines.Count + rate.PlantLines.Count + rate.MaterialsLines.Count == 0)
                        {
                            _actionReportHost.Child = new TextBlock
                            {
                                Text = "Add at least one labour / plant / material line.",
                                Foreground = AmberBrush, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(14)
                            };
                            return;
                        }
                        string json = Newtonsoft.Json.JsonConvert.SerializeObject(rate);
                        StingCommandHandler.SetExtraParam("StarRateJson", json);
                        DispatchInline(label, "Variation_BuildStarRate");
                    }
                    catch (Exception ex) { StingLog.Error("BOQ ShowStarRateForm Run", ex); }
                };
                sp.Children.Add(runBtn);

                _actionReportHost.Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = sp
                };
            }
            catch (Exception ex) { StingLog.Error("BOQ ShowStarRateForm", ex); }
        }

        /// <summary>P0.3 — 120px-label + control row used by the star-rate form.</summary>
        private UIElement LabeledRow(string label, FrameworkElement control)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var t = new TextBlock { Text = label, FontSize = 11, Foreground = NavyBrush, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(t, 0); g.Children.Add(t);
            Grid.SetColumn(control, 1); g.Children.Add(control);
            return g;
        }

        /// <summary>P0.3 — one labour/plant/materials section: a header + dynamically
        /// growable rows (Resource · number · rate) + an "+ add row" button. Editors
        /// are appended to <paramref name="editors"/> so the Run handler can read them.</summary>
        private UIElement BuildStarRateSection(string title, string hint,
            List<(TextBox res, TextBox num, TextBox rate)> editors)
        {
            var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            outer.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = NavyBrush });
            outer.Children.Add(new TextBlock { Text = hint, FontSize = 9, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 4) });
            var rowsPanel = new StackPanel();
            outer.Children.Add(rowsPanel);

            void AddRow()
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 3) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                var res = new TextBox { Height = 24, FontSize = 11, Margin = new Thickness(0, 0, 4, 0) };
                var num = new TextBox { Height = 24, FontSize = 11, Margin = new Thickness(0, 0, 4, 0) };
                var rate = new TextBox { Height = 24, FontSize = 11 };
                Grid.SetColumn(res, 0); Grid.SetColumn(num, 1); Grid.SetColumn(rate, 2);
                g.Children.Add(res); g.Children.Add(num); g.Children.Add(rate);
                rowsPanel.Children.Add(g);
                editors.Add((res, num, rate));
            }
            AddRow(); AddRow();   // start with two rows

            var addBtn = new Button
            {
                Content = "+ add row", FontSize = 10, Height = 22, MinWidth = 70,
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0),
                Background = Brushes.White, Foreground = NavyBrush, BorderBrush = BorderColor,
                BorderThickness = new Thickness(1), Cursor = Cursors.Hand
            };
            addBtn.Click += (s, e) => AddRow();
            outer.Children.Add(addBtn);
            return outer;
        }

        /// <summary>P0.3 — a combo bound to (display, value) option pairs (value on Tag).</summary>
        private System.Windows.Controls.ComboBox MakeCombo(List<(string display, string value)> opts)
        {
            var cb = new System.Windows.Controls.ComboBox { Height = 24, FontSize = 11, Margin = new Thickness(0, 0, 6, 0) };
            foreach (var (display, value) in opts)
                cb.Items.Add(new ComboBoxItem { Content = display, Tag = value });
            if (cb.Items.Count > 0) cb.SelectedIndex = 0;
            return cb;
        }

        // ── Reclassify legacy variations (per-VO reason/liability grid) ───────
        //  Each legacy VO (Reason=Other & Liability=Employer) gets a reason combo
        //  ("— skip —" first) + a liability combo ("(auto-suggest)" first). On Run
        //  the assignments serialize into the ReclassifyJson ExtraParam
        //  ({VO number → "Reason|Liability"}); the command applies + saves. No popup.
        private void ShowReclassifyForm(string label)
        {
            if (_actionReportHost == null || Doc == null) return;
            try
            {
                if (_mainTabs != null && _actionsTab != null) _mainTabs.SelectedItem = _actionsTab;
                if (_actionReportTitle != null) _actionReportTitle.Text = label;

                var legacy = new List<(string number, string title)>();
                try
                {
                    foreach (var p in StingTools.Core.Variation.VariationEngine.ListVariations(Doc))
                    {
                        var v = StingTools.Core.Variation.VariationEngine.Load(p);
                        if (v == null) continue;
                        if (v.Reason == StingTools.Core.Variation.VariationReason.Other
                            && v.Liability == StingTools.Core.Variation.VariationLiability.Employer)
                            legacy.Add((v.Number, v.Title));
                    }
                }
                catch (Exception ex) { StingLog.Warn($"BOQ reclassify list: {ex.Message}"); }

                var sp = new StackPanel { Margin = new Thickness(14) };
                sp.Children.Add(new TextBlock { Text = "Reclassify legacy variations",
                    FontSize = 13, FontWeight = FontWeights.Bold, Foreground = NavyBrush, Margin = new Thickness(0, 0, 0, 6) });
                sp.Children.Add(new TextBlock
                {
                    Text = "Variations still on the default Other / Employer. Set a reason (and optionally a "
                         + "liability — blank auto-suggests) per row, then Run. Rows left on “— skip —” are untouched.",
                    FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
                });

                if (legacy.Count == 0)
                {
                    sp.Children.Add(new TextBlock
                    {
                        Text = "Every variation has a non-default reason / liability assigned.",
                        FontSize = 11, Foreground = AmberBrush, TextWrapping = TextWrapping.Wrap
                    });
                    _actionReportHost.Child = sp;
                    return;
                }

                var reasonOpts = new List<(string, string)>
                {
                    ("— skip —", ""), ("Design change", "DesignChange"), ("Client request", "ClientRequest"),
                    ("Site condition", "SiteCondition"), ("Statutory change", "StatutoryChange"),
                    ("Error / omission", "ErrorOmission"), ("Contractor proposal", "ContractorProposal"),
                    ("Scope addition", "ScopeAddition"), ("Scope omission", "ScopeOmission"),
                    ("Specification", "Specification"), ("Quality", "Quality"),
                    ("Programme change", "ProgrammeChange"), ("Other", "Other"),
                };
                var liabOpts = new List<(string, string)>
                {
                    ("(auto-suggest)", ""), ("Employer / client", "Employer"), ("Contractor", "Contractor"),
                    ("Designer", "Designer"), ("Shared", "Shared"), ("Force majeure", "ForceMajeure"),
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                void Header(string t, int col)
                {
                    var tb = new TextBlock { Text = t, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = NavyBrush, Margin = new Thickness(0, 0, 6, 6) };
                    Grid.SetRow(tb, 0); Grid.SetColumn(tb, col); grid.Children.Add(tb);
                }
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Header("Variation", 0); Header("Reason", 1); Header("Liability", 2);

                var editors = new List<(string number, System.Windows.Controls.ComboBox reason, System.Windows.Controls.ComboBox liab)>();
                int r = 1;
                foreach (var vo in legacy)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var lbl = new TextBlock { Text = $"{vo.number}  {vo.title}", FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(0, 2, 6, 2) };
                    Grid.SetRow(lbl, r); Grid.SetColumn(lbl, 0); grid.Children.Add(lbl);
                    var rc = MakeCombo(reasonOpts);
                    Grid.SetRow(rc, r); Grid.SetColumn(rc, 1); grid.Children.Add(rc);
                    var lc = MakeCombo(liabOpts);
                    Grid.SetRow(lc, r); Grid.SetColumn(lc, 2); grid.Children.Add(lc);
                    editors.Add((vo.number, rc, lc));
                    r++;
                }
                sp.Children.Add(grid);

                var runBtn = new Button
                {
                    Content = "Run", FontSize = 12, FontWeight = FontWeights.Bold, Height = 30, MinWidth = 100,
                    Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
                    Background = GreenBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand
                };
                runBtn.Click += (s, e) =>
                {
                    try
                    {
                        string Sel(System.Windows.Controls.ComboBox cb) => (cb.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                        var map = new Dictionary<string, string>();
                        foreach (var ed in editors)
                        {
                            string reason = Sel(ed.reason);
                            if (string.IsNullOrEmpty(reason)) continue;   // skipped row
                            map[ed.number] = reason + "|" + Sel(ed.liab);
                        }
                        if (map.Count == 0)
                        {
                            _actionReportHost.Child = new TextBlock
                            {
                                Text = "Set a reason on at least one variation (or there's nothing to apply).",
                                Foreground = AmberBrush, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14)
                            };
                            return;
                        }
                        StingCommandHandler.SetExtraParam("ReclassifyJson", Newtonsoft.Json.JsonConvert.SerializeObject(map));
                        DispatchInline(label, "Variation_ReclassifyLegacy");
                    }
                    catch (Exception ex) { StingLog.Error("BOQ ShowReclassifyForm Run", ex); }
                };
                sp.Children.Add(runBtn);

                _actionReportHost.Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = sp
                };
            }
            catch (Exception ex) { StingLog.Error("BOQ ShowReclassifyForm", ex); }
        }

        // ── G2 — provisional-sum reconciliation trail (dynamic inline form) ──
        //  Lists every PS row with its FROZEN original allowance + an editable
        //  "actual" + note. On Save it appends a dated adjustment to the trail
        //  (boq_provisionals.json), so estimate → actual is recorded, not
        //  overwritten. Σ(actual − original) is the provisional-sum movement,
        //  shown here, in the coverage strip, and folded into Anticipated Final
        //  Cost. No popup; persists like SaveUiState (UI = Revit main thread).
        private void ShowProvisionalReconcileForm(string label)
        {
            if (_actionReportHost == null || Doc == null) return;
            try
            {
                if (_mainTabs != null && _actionsTab != null) _mainTabs.SelectedItem = _actionsTab;
                if (_actionReportTitle != null) _actionReportTitle.Text = label;

                var psRows = _boq?.AllItems?
                    .Where(i => i.Source == BOQRowSource.ProvisionalSum)
                    .ToList() ?? new List<BOQLineItem>();

                var sp = new StackPanel { Margin = new Thickness(14) };
                sp.Children.Add(new TextBlock
                {
                    Text = "Provisional-sum reconciliation", FontSize = 13, FontWeight = FontWeights.Bold,
                    Foreground = NavyBrush, Margin = new Thickness(0, 0, 0, 6)
                });
                sp.Children.Add(new TextBlock
                {
                    Text = "Record the final-account actual against each provisional sum. The original "
                         + "allowance is frozen on first sight; the movement (actual − original) is added "
                         + "to the Anticipated Final Cost.",
                    FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                });

                if (psRows.Count == 0)
                {
                    sp.Children.Add(new TextBlock
                    {
                        Text = "No provisional sums in this bill yet. Add one via Actions → Add Manual / PS / "
                             + "Daywork and mark it a Provisional Sum.",
                        FontSize = 11, Foreground = AmberBrush, TextWrapping = TextWrapping.Wrap
                    });
                    _actionReportHost.Child = sp;
                    return;
                }

                var store = BoqProvisionalTrail.Load(Doc);

                var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });

                void HeaderCell(string text, int col)
                {
                    var t = new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeights.Bold,
                        Foreground = NavyBrush, Margin = new Thickness(0, 0, 8, 6) };
                    Grid.SetRow(t, 0); Grid.SetColumn(t, col); grid.Children.Add(t);
                }
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                HeaderCell("Provisional sum", 0);
                HeaderCell("Original (UGX)", 1);
                HeaderCell("Actual (UGX)", 2);
                HeaderCell("Note", 3);

                var editors = new List<(string id, string desc, double original, TextBox actual, TextBox note)>();
                int r = 1;
                foreach (var ps in psRows)
                {
                    var rec = store.Records.FirstOrDefault(x => x.Id == ps.Id);
                    double original = rec?.OriginalSum ?? ps.TotalUGX;   // freeze on first sight
                    string actualStr = rec?.ReconciledActual?.ToString("F0", CultureInfo.InvariantCulture) ?? "";
                    string noteStr = rec?.Adjustments != null && rec.Adjustments.Count > 0
                        ? rec.Adjustments[rec.Adjustments.Count - 1].Note : "";

                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var descTb = new TextBlock { Text = ps.ItemName ?? ps.Category ?? "(PS)", FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(0, 3, 8, 3) };
                    Grid.SetRow(descTb, r); Grid.SetColumn(descTb, 0); grid.Children.Add(descTb);

                    var origTb = new TextBlock { Text = original.ToString("N0", CultureInfo.InvariantCulture),
                        FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3) };
                    Grid.SetRow(origTb, r); Grid.SetColumn(origTb, 1); grid.Children.Add(origTb);

                    var actualTb = new TextBox { Text = actualStr, Height = 24, FontSize = 11, Margin = new Thickness(0, 2, 8, 2) };
                    Grid.SetRow(actualTb, r); Grid.SetColumn(actualTb, 2); grid.Children.Add(actualTb);

                    var noteTb = new TextBox { Text = noteStr, Height = 24, FontSize = 11, Margin = new Thickness(0, 2, 0, 2) };
                    Grid.SetRow(noteTb, r); Grid.SetColumn(noteTb, 3); grid.Children.Add(noteTb);

                    editors.Add((ps.Id, ps.ItemName ?? ps.Category ?? "(PS)", original, actualTb, noteTb));
                    r++;
                }
                sp.Children.Add(grid);

                int reconciled0 = store.Records.Count(x => x.ReconciledActual.HasValue);
                double mv0 = BoqProvisionalTrail.MovementUGX(store);
                var movementText = new TextBlock
                {
                    Text = $"Provisional-sum movement: UGX {mv0:+#,##0;-#,##0;0}   ·   {reconciled0}/{psRows.Count} reconciled",
                    FontSize = 12, FontWeight = FontWeights.SemiBold,
                    Foreground = mv0 > 0 ? RedBrush : mv0 < 0 ? GreenBrush : NavyBrush,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                sp.Children.Add(movementText);

                var saveBtn = new Button
                {
                    Content = "Save reconciliation", FontSize = 12, FontWeight = FontWeights.Bold, Height = 30, MinWidth = 150,
                    HorizontalAlignment = HorizontalAlignment.Left, Background = GreenBrush, Foreground = Brushes.White,
                    BorderThickness = new Thickness(0), Cursor = Cursors.Hand
                };
                saveBtn.Click += (s, e) =>
                {
                    try
                    {
                        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                        foreach (var ed in editors)
                        {
                            var rec = store.Records.FirstOrDefault(x => x.Id == ed.id);
                            if (rec == null)
                            {
                                rec = new BoqProvisionalRecord { Id = ed.id, Description = ed.desc, OriginalSum = ed.original };
                                store.Records.Add(rec);
                            }
                            else { rec.Description = ed.desc; }  // OriginalSum stays frozen

                            string raw = ed.actual.Text?.Trim() ?? "";
                            double? newActual = null;
                            if (!string.IsNullOrEmpty(raw) &&
                                double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double a))
                                newActual = Math.Round(a, 0);
                            string note = ed.note.Text?.Trim() ?? "";
                            double? prevActual = rec.ReconciledActual;

                            if (newActual.HasValue)
                            {
                                if (!prevActual.HasValue || Math.Abs(newActual.Value - prevActual.Value) > 0.5)
                                {
                                    // Actual changed → append a dated adjustment (estimate→actual trail).
                                    double priorBasis = prevActual ?? rec.OriginalSum;
                                    rec.Adjustments.Add(new BoqProvisionalAdjustment
                                    {
                                        Date = today, Amount = Math.Round(newActual.Value - priorBasis, 0), Note = note
                                    });
                                    rec.ReconciledActual = newActual;
                                }
                                else if (note.Length > 0 && rec.Adjustments.Count > 0)
                                {
                                    // Same actual, note edited → annotate the latest adjustment in place.
                                    rec.Adjustments[rec.Adjustments.Count - 1].Note = note;
                                }
                                else if (note.Length > 0)
                                {
                                    rec.Adjustments.Add(new BoqProvisionalAdjustment { Date = today, Amount = 0, Note = note });
                                    rec.ReconciledActual = newActual;
                                }
                            }
                            rec.Status = rec.ReconciledActual.HasValue ? "Closed"
                                       : rec.Adjustments.Count > 0 ? "PartlyReconciled" : "Open";
                        }
                        BoqProvisionalTrail.Save(Doc, store);
                        RefreshMetrics();          // coverage strip picks up the new movement
                        ShowProvisionalReconcileForm(label);  // re-render with persisted state + movement
                    }
                    catch (Exception ex) { StingLog.Error("BOQ ProvisionalReconcile save", ex); }
                };
                sp.Children.Add(saveBtn);

                _actionReportHost.Child = sp;
            }
            catch (Exception ex) { StingLog.Error("BOQ ShowProvisionalReconcileForm", ex); }
        }

        // ── G3 — preliminaries schedule (toggle + inline editable grid) ──────
        //  Keep the flat prelims % (default) or switch to an itemised built-up
        //  schedule. Each line is a fixed value or a % of the works subtotal.
        //  Save persists to _BIM_COORD/boq_prelims.json and triggers a rebuild
        //  so the grand total + export pick up the active basis. No popup.
        private void ShowPrelimsForm(string label)
        {
            if (_actionReportHost == null || Doc == null) return;
            try
            {
                if (_mainTabs != null && _actionsTab != null) _mainTabs.SelectedItem = _actionsTab;
                if (_actionReportTitle != null) _actionReportTitle.Text = label;

                var schedule = BoqPrelimsStore.Load(Doc);
                var working = (schedule.Lines ?? new List<BoqPrelimLine>()).Select(l => l.Clone()).ToList();
                bool enabled = schedule.Enabled;

                var editors = new List<(BoqPrelimLine line, TextBox name, TextBox cat,
                    System.Windows.Controls.ComboBox basis, TextBox val)>();
                CheckBox enableCb = null;

                double Subtotal() => _boq?.SubtotalUGX ?? 0;

                void Commit()
                {
                    foreach (var ed in editors)
                    {
                        ed.line.Name = ed.name.Text?.Trim() ?? "";
                        ed.line.Category = ed.cat.Text?.Trim() ?? "";
                        ed.line.Basis = ((ed.basis.SelectedItem as ComboBoxItem)?.Tag as string) ?? "value";
                        if (double.TryParse(ed.val.Text?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                            ed.line.Value = v;
                    }
                    if (enableCb != null) enabled = enableCb.IsChecked == true;
                }

                Action render = null;
                render = () =>
                {
                    editors.Clear();
                    var sp = new StackPanel { Margin = new Thickness(14) };
                    sp.Children.Add(new TextBlock
                    {
                        Text = "Preliminaries schedule", FontSize = 13, FontWeight = FontWeights.Bold,
                        Foreground = NavyBrush, Margin = new Thickness(0, 0, 0, 6)
                    });
                    sp.Children.Add(new TextBlock
                    {
                        Text = "Keep the flat prelims % or switch to an itemised built-up schedule. Each line is a "
                             + "fixed value or a % of the works subtotal. The active basis rolls into the grand total "
                             + "and the XLSX export.",
                        FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 10)
                    });

                    enableCb = new CheckBox
                    {
                        Content = "Use itemised preliminaries (instead of the flat %)", IsChecked = enabled,
                        FontSize = 12, Foreground = NavyBrush, Margin = new Thickness(0, 0, 0, 10)
                    };
                    sp.Children.Add(enableCb);

                    var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

                    void H(string t, int c)
                    {
                        var x = new TextBlock { Text = t, FontSize = 10, FontWeight = FontWeights.Bold,
                            Foreground = NavyBrush, Margin = new Thickness(0, 0, 6, 6) };
                        Grid.SetRow(x, 0); Grid.SetColumn(x, c); grid.Children.Add(x);
                    }
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    H("Item", 0); H("Category", 1); H("Basis", 2); H("Value / %", 3); H("Amount UGX", 4); H("", 5);

                    double subtotal = Subtotal();
                    int r = 1;
                    foreach (var line in working)
                    {
                        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        var nameTb = new TextBox { Text = line.Name, Height = 24, FontSize = 11, Margin = new Thickness(0, 2, 6, 2) };
                        var catTb = new TextBox { Text = line.Category, Height = 24, FontSize = 11, Margin = new Thickness(0, 2, 6, 2) };
                        var basisCb = new System.Windows.Controls.ComboBox { Height = 24, FontSize = 11, Margin = new Thickness(0, 2, 6, 2) };
                        basisCb.Items.Add(new ComboBoxItem { Content = "Value", Tag = "value" });
                        basisCb.Items.Add(new ComboBoxItem { Content = "% works", Tag = "percent" });
                        basisCb.SelectedIndex = string.Equals(line.Basis, "percent", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                        var valTb = new TextBox { Text = line.Value.ToString("0.######", CultureInfo.InvariantCulture),
                            Height = 24, FontSize = 11, Margin = new Thickness(0, 2, 6, 2) };
                        var amtTb = new TextBlock { Text = line.AmountFor(subtotal).ToString("N0", CultureInfo.InvariantCulture),
                            FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 6, 2) };
                        var delBtn = new Button { Content = "×", Width = 22, Height = 22, FontSize = 12,
                            Cursor = Cursors.Hand, ToolTip = "Remove line" };
                        var capLine = line;
                        delBtn.Click += (s, e) => { Commit(); working.Remove(capLine); render(); };

                        Grid.SetRow(nameTb, r); Grid.SetColumn(nameTb, 0); grid.Children.Add(nameTb);
                        Grid.SetRow(catTb, r); Grid.SetColumn(catTb, 1); grid.Children.Add(catTb);
                        Grid.SetRow(basisCb, r); Grid.SetColumn(basisCb, 2); grid.Children.Add(basisCb);
                        Grid.SetRow(valTb, r); Grid.SetColumn(valTb, 3); grid.Children.Add(valTb);
                        Grid.SetRow(amtTb, r); Grid.SetColumn(amtTb, 4); grid.Children.Add(amtTb);
                        Grid.SetRow(delBtn, r); Grid.SetColumn(delBtn, 5); grid.Children.Add(delBtn);

                        editors.Add((line, nameTb, catTb, basisCb, valTb));
                        r++;
                    }
                    sp.Children.Add(grid);

                    double itemTotal = working.Sum(l => l.AmountFor(subtotal));
                    double flatTotal = subtotal * (_boq?.PrelimPct ?? 0) / 100.0;
                    sp.Children.Add(new TextBlock
                    {
                        Text = $"Itemised prelims total: UGX {itemTotal:N0}   ·   flat % would be: UGX {flatTotal:N0}   "
                             + $"(active: {(enabled ? "itemised" : "flat %")})",
                        FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = NavyBrush,
                        Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap
                    });

                    var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
                    var addBtn = new Button { Content = "+ Add line", FontSize = 12, Height = 28, MinWidth = 90,
                        Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand };
                    addBtn.Click += (s, e) => { Commit(); working.Add(new BoqPrelimLine { Basis = "value" }); render(); };
                    var saveBtn = new Button { Content = "Save schedule", FontSize = 12, FontWeight = FontWeights.Bold,
                        Height = 28, MinWidth = 120, Background = GreenBrush, Foreground = Brushes.White,
                        BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
                    saveBtn.Click += (s, e) =>
                    {
                        try
                        {
                            Commit();
                            schedule.Enabled = enabled;
                            schedule.Lines = working;
                            BoqPrelimsStore.Save(Doc, schedule);
                            RefreshAsync();   // rebuild so grand total + metrics pick up the active basis
                            render();         // re-render with the rebuilt subtotal
                        }
                        catch (Exception ex) { StingLog.Error("BOQ Prelims save", ex); }
                    };
                    btnRow.Children.Add(addBtn);
                    btnRow.Children.Add(saveBtn);
                    sp.Children.Add(btnRow);

                    _actionReportHost.Child = sp;
                };
                render();
            }
            catch (Exception ex) { StingLog.Error("BOQ ShowPrelimsForm", ex); }
        }

        // ══════════════════════════════════════════════════════════════════
        //  G7 — Cost Setup wizard (inline, multi-page; orchestrates existing
        //  commands — never duplicates them). Rendered into the Actions pane
        //  per the "inline, no popups" convention; "wizard completed" persists
        //  to boq_ui_state.json so it doesn't nag.
        // ══════════════════════════════════════════════════════════════════
        private const int WizardLastPage = 5;

        private void MaybeOfferWizard()
        {
            try
            {
                if (_wizardCompleted || _wizardOffered || _boq == null) return;
                if (_boq.ProjectBudgetUGX > 0) return;   // only a fresh, unpriced project
                _wizardOffered = true;
                ShowCostSetupWizard();
            }
            catch (Exception ex) { StingLog.Warn($"BOQ MaybeOfferWizard: {ex.Message}"); }
        }

        public void ShowCostSetupWizard()
        {
            _wizardPage = 0;
            _wizardCurrency = _displayCurrency;
            _wizardBudget = _boq != null && _boq.ProjectBudgetUGX > 0
                ? _boq.ProjectBudgetUGX.ToString("F0", CultureInfo.InvariantCulture) : "";
            RenderWizardPage();
        }

        private void RenderWizardPage()
        {
            if (_actionReportHost == null) return;
            try
            {
                if (_mainTabs != null && _actionsTab != null) _mainTabs.SelectedItem = _actionsTab;
                if (_actionReportTitle != null) _actionReportTitle.Text = "Cost Setup wizard";

                var sp = new StackPanel { Margin = new Thickness(14) };
                sp.Children.Add(new TextBlock
                {
                    Text = $"Cost Setup  —  step {_wizardPage + 1} of {WizardLastPage + 1}",
                    FontSize = 13, FontWeight = FontWeights.Bold, Foreground = NavyBrush,
                    Margin = new Thickness(0, 0, 0, 10)
                });

                TextBox budgetTb = null;
                System.Windows.Controls.ComboBox ccyCb = null;
                var pricingRadios = new List<(string id, RadioButton rb)>();

                switch (_wizardPage)
                {
                    case 0:
                        sp.Children.Add(WizardText(
                            "This sets your project up to produce a costed Bill of Quantities. The BOQ reads "
                            + "quantities straight from the model; you add prices (auto rates, a QS round-trip, or "
                            + "by hand), markups and a budget, then save a baseline snapshot.\n\n"
                            + "It takes about a minute, nothing here is irreversible, and you can re-run it any time "
                            + "from the ✦ Cost Setup button.\n\n"
                            + "Full walkthrough: BOQ_QS_LAYMANS_GUIDE.md (in the project docs)."));
                        break;
                    case 1:
                        sp.Children.Add(WizardText("Model readiness — a quick scan so you know what the take-off has to work with:"));
                        sp.Children.Add(BuildReadinessReport());
                        break;
                    case 2:
                        sp.Children.Add(WizardText("Set the project budget and the display currency."));
                        var g = WizardTwoCol();
                        budgetTb = new TextBox { Text = _wizardBudget, Height = 26, FontSize = 12 };
                        ccyCb = new System.Windows.Controls.ComboBox { Height = 26, FontSize = 12 };
                        ccyCb.Items.Add(new ComboBoxItem { Content = "UGX", Tag = "UGX" });
                        ccyCb.Items.Add(new ComboBoxItem { Content = "USD", Tag = "USD" });
                        ccyCb.SelectedIndex = string.Equals(_wizardCurrency, "USD", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                        WizardRow(g, 0, "Project budget (UGX)", budgetTb);
                        WizardRow(g, 1, "Display currency", ccyCb);
                        sp.Children.Add(g);
                        break;
                    case 3:
                        sp.Children.Add(WizardText("How will this bill be priced?"));
                        pricingRadios.Add(("Auto", WizardRadio(sp, "Auto rates",
                            "STING's rate library + any project rate card. Fastest — review the Rate Gap Report after.", _wizardPricing == "Auto")));
                        pricingRadios.Add(("RoundTrip", WizardRadio(sp, "QS round-trip",
                            "Export the bill to Excel, a QS prices it, re-import. Most defensible.", _wizardPricing == "RoundTrip")));
                        pricingRadios.Add(("Manual", WizardRadio(sp, "Manual",
                            "Type rates into the Rate column yourself.", _wizardPricing == "Manual")));
                        var exportBtn = WizardActionBtn("⤓ Export QS Bill now", () => DispatchAction("BOQQsExport"));
                        exportBtn.Margin = new Thickness(0, 8, 0, 0);
                        sp.Children.Add(exportBtn);
                        break;
                    case 4:
                        sp.Children.Add(WizardText(
                            $"Markups. Default flat percentages: Preliminaries {_boq?.PrelimPct:0.#}%, "
                            + $"Contingency {_boq?.ContingencyPct:0.#}%, Overhead & profit {_boq?.OverheadPct:0.#}%.\n\n"
                            + "Keep the flat % for a quick estimate, or build an itemised preliminaries schedule."));
                        sp.Children.Add(WizardActionBtn("Open preliminaries schedule…",
                            () => ShowPrelimsForm("Preliminaries schedule")));
                        break;
                    case 5:
                        string budgetTxt = _boq != null && _boq.ProjectBudgetUGX > 0
                            ? "UGX " + _boq.ProjectBudgetUGX.ToString("N0") : "— not set —";
                        sp.Children.Add(WizardText(
                            "Ready to finish. This refreshes the bill and saves a baseline snapshot.\n\n"
                            + $"Budget: {budgetTxt}\n"
                            + $"Pricing path: {_wizardPricing}\n"
                            + $"Items in the bill: {_boq?.AllItems.Count ?? 0}"));
                        break;
                }

                var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
                if (_wizardPage > 0)
                    nav.Children.Add(WizardNavBtn("‹ Back", false, () =>
                    { CaptureWizardPage(budgetTb, ccyCb, pricingRadios); _wizardPage--; RenderWizardPage(); }));
                if (_wizardPage < WizardLastPage)
                    nav.Children.Add(WizardNavBtn("Next ›", true, () =>
                    { ApplyWizardPage(budgetTb, ccyCb, pricingRadios); _wizardPage++; RenderWizardPage(); }));
                else
                    nav.Children.Add(WizardNavBtn("✓ Finish", true, FinishWizard));
                nav.Children.Add(WizardNavBtn("Skip", false, () =>
                {
                    _actionReportHost.Child = new TextBlock
                    {
                        Text = "Cost Setup dismissed. Re-open it any time via the ✦ Cost Setup button.",
                        Margin = new Thickness(14), Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap
                    };
                }));
                sp.Children.Add(nav);

                _actionReportHost.Child = sp;
            }
            catch (Exception ex) { StingLog.Error("BOQ RenderWizardPage", ex); }
        }

        // Capture page values into fields WITHOUT side effects (used by Back).
        private void CaptureWizardPage(TextBox budgetTb, System.Windows.Controls.ComboBox ccyCb,
            List<(string id, RadioButton rb)> pricing)
        {
            if (budgetTb != null) _wizardBudget = budgetTb.Text?.Trim() ?? _wizardBudget;
            if (ccyCb != null) _wizardCurrency = (ccyCb.SelectedItem as ComboBoxItem)?.Tag as string ?? _wizardCurrency;
            if (pricing != null && pricing.Count > 0)
            {
                var sel = pricing.FirstOrDefault(p => p.rb.IsChecked == true);
                if (sel.id != null) _wizardPricing = sel.id;
            }
        }

        // Capture + apply this page's side effects (used by Next).
        private void ApplyWizardPage(TextBox budgetTb, System.Windows.Controls.ComboBox ccyCb,
            List<(string id, RadioButton rb)> pricing)
        {
            CaptureWizardPage(budgetTb, ccyCb, pricing);
            if (_wizardPage == 2)
            {
                // Currency — set the display toggle + persist.
                if (!string.Equals(_displayCurrency, _wizardCurrency, StringComparison.OrdinalIgnoreCase))
                {
                    _displayCurrency = _wizardCurrency;
                    if (_ugxToggle != null) _ugxToggle.IsChecked = _displayCurrency == "UGX";
                    if (_usdToggle != null) _usdToggle.IsChecked = _displayCurrency == "USD";
                    SaveUiState();
                    RefreshDisplay();
                }
                // Budget — orchestrate the existing BOQSetBudget command (writes the param).
                if (double.TryParse(_wizardBudget, NumberStyles.Any, CultureInfo.InvariantCulture, out double bud) && bud > 0)
                {
                    StingCommandHandler.SetExtraParam("ProjectBudgetUgx", bud.ToString("F0", CultureInfo.InvariantCulture));
                    DispatchAction("BOQSetBudget");
                }
            }
        }

        private void FinishWizard()
        {
            try
            {
                _wizardCompleted = true;
                SaveUiState();
                RefreshAsync();   // rebuild on the UI thread (picks up the budget just set)
                try
                {
                    if (_boq != null)
                    {
                        StingTools.BOQ.BOQCostManager.SaveSnapshot(Doc, _boq,
                            $"Baseline — {DateTime.Now:yyyy-MM-dd}", "Manual");
                        LoadSnapshotDropdown();
                    }
                }
                catch (Exception ex) { StingLog.Warn($"BOQ wizard snapshot: {ex.Message}"); }
                _actionReportHost.Child = WizardText(
                    "✓ Cost Setup complete. The bill is built and a baseline snapshot is saved. "
                    + "Use Actions → Rate Gap Report to see what still needs a price, or Export to hand it over.");
            }
            catch (Exception ex) { StingLog.Error("BOQ FinishWizard", ex); }
        }

        private UIElement BuildReadinessReport()
        {
            var grid = WizardTwoCol();
            int rooms = 0, phases = 0;
            try { rooms = new FilteredElementCollector(Doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().GetElementCount(); } catch { }
            try { phases = new FilteredElementCollector(Doc).OfClass(typeof(Phase)).GetElementCount(); } catch { }
            int items = _boq?.AllItems.Count ?? 0;
            int priced = _boq?.AllItems.Count(i => i.RateUGX > 0) ?? 0;
            double pricedPct = items > 0 ? 100.0 * priced / items : 0;

            void Row(int r, string k, string v, bool warn)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var lk = new TextBlock { Text = k, FontSize = 11, Foreground = NavyBrush, Margin = new Thickness(0, 3, 8, 3) };
                var lv = new TextBlock { Text = v, FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Foreground = warn ? AmberBrush : GreenBrush, Margin = new Thickness(0, 3, 0, 3), TextWrapping = TextWrapping.Wrap };
                Grid.SetRow(lk, r); Grid.SetColumn(lk, 0); grid.Children.Add(lk);
                Grid.SetRow(lv, r); Grid.SetColumn(lv, 1); grid.Children.Add(lv);
            }
            Row(0, "Rooms", rooms > 0 ? $"{rooms} — spatial codes/locations available" : "none — locations will be blank (place rooms for §spatial grouping)", rooms == 0);
            Row(1, "Phases", phases > 0 ? $"{phases} — used to cost-load the 4D schedule" : "none — the schedule will use one default phase", phases == 0);
            Row(2, "Modelled items", items > 0 ? items.ToString() : "none — the model has no costable elements yet", items == 0);
            Row(3, "Auto-priced", $"{priced} / {items} ({pricedPct:0.#}%)", pricedPct < 50);
            return grid;
        }

        // ── G7 wizard UI helpers ────────────────────────────────────────────
        private TextBlock WizardText(string t) => new TextBlock
        {
            Text = t, FontSize = 11.5, Foreground = NavyBrush, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        };

        private Grid WizardTwoCol()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return g;
        }

        private void WizardRow(Grid g, int r, string label, System.Windows.Controls.Control ctl)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lbl = new TextBlock { Text = label, FontSize = 11, Foreground = NavyBrush,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
            ctl.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetRow(lbl, r); Grid.SetColumn(lbl, 0); g.Children.Add(lbl);
            Grid.SetRow(ctl, r); Grid.SetColumn(ctl, 1); g.Children.Add(ctl);
        }

        private RadioButton WizardRadio(System.Windows.Controls.Panel host, string title, string detail, bool isChecked)
        {
            var rb = new RadioButton { IsChecked = isChecked, GroupName = "WizardPricing", Margin = new Thickness(0, 4, 0, 0) };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = NavyBrush });
            sp.Children.Add(new TextBlock { Text = detail, FontSize = 10.5, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap });
            rb.Content = sp;
            host.Children.Add(rb);
            return rb;
        }

        private Button WizardActionBtn(string label, Action onClick)
        {
            var b = new Button { Content = label, FontSize = 11, Height = 28, MinWidth = 160,
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0),
                Background = Brushes.White, Foreground = NavyBrush, BorderBrush = BorderColor,
                BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            b.Click += (s, e) => { try { onClick(); } catch (Exception ex) { StingLog.Error($"BOQ wizard btn '{label}'", ex); } };
            return Guarded(b);
        }

        private Button WizardNavBtn(string label, bool primary, Action onClick)
        {
            var b = new Button { Content = label, FontSize = 12, FontWeight = FontWeights.Bold, Height = 30, MinWidth = 90,
                Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand,
                Background = primary ? GreenBrush : Brushes.White, Foreground = primary ? Brushes.White : NavyBrush,
                BorderBrush = BorderColor, BorderThickness = new Thickness(primary ? 0 : 1) };
            b.Click += (s, e) => { try { onClick(); } catch (Exception ex) { StingLog.Error($"BOQ wizard nav '{label}'", ex); } };
            return b;
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
                    else if (caption.StartsWith("Export")) b.Click += (s, e) =>
                    {
                        // P0.3 — signal panel context so the export skips its modal
                        // low-coverage warning (coverage is live in the strip + result).
                        StingCommandHandler.SetExtraParam("InlineHost", "1");
                        DispatchAction("BOQExport");
                    };
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

        public void RefreshAsync() => RefreshAsync(false);

        /// <summary>
        /// Rebuild the bill. Phase 2D — when <paramref name="forceFull"/> is false
        /// and the dirty marker is enabled, the host take-off runs incrementally
        /// (re-takes-off only changed/added elements over a cached raw set); the
        /// result is identical to a full rebuild by construction. forceFull (the
        /// "Refresh (full)" button) always re-walks the whole model.
        /// </summary>
        public void RefreshAsync(bool forceFull)
        {
            if (Doc == null) return;
            if (forceFull)
            {
                try { BOQCostManager.ForceHostFull(Doc); } catch (Exception ex) { StingLog.Warn($"BOQ ForceHostFull: {ex.Message}"); }
            }
            bool allowIncremental = !forceFull && StingTools.BOQ.StingCostDirtyMarker.IsEnabled;
            // ── G8: big-model Refresh feedback ───────────────────────────────
            //  BuildBOQDocument MUST run on the Revit API thread: its per-element
            //  costing reads parameters + geometry inline (that IS the heavy
            //  compute), ResolveNrm2Paragraph runs a FilteredElementCollector per
            //  element, the linked-model take-off uses collectors, and the build
            //  ends with a parameter-write Transaction (ClearStaleFlagsForCostedRows).
            //  A clean "read into POCOs on the API thread, then group/rate/aggregate
            //  off-thread" split is infeasible without re-architecting the ~2k-line
            //  take-off engine into separate read/compute/write passes — high
            //  regression risk and an engine fork the brief forbids. Fallback per
            //  the prompt: show a progress dialog during the synchronous build on
            //  large models so Refresh reads as "working", not frozen. Small models
            //  (below the threshold) skip it so nothing regresses. The Revit API is
            //  NEVER touched off the API thread.
            StingProgressDialog progress = null;
            try
            {
                int elemCount = 0;
                try { elemCount = new FilteredElementCollector(Doc).WhereElementIsNotElementType().GetElementCount(); }
                catch { /* count is advisory only */ }
                int bigThreshold = (int)TagConfig.GetConfigDouble("COST_BIG_MODEL_THRESHOLD", 1500);
                if (elemCount >= bigThreshold)
                {
                    progress = StingProgressDialog.Show("BOQ Refresh", 1);
                    progress.SetStatus($"Building bill of quantities from {elemCount:N0} elements — please wait…");
                    PumpDispatcher();   // force the dialog to paint before the blocking build
                }

                _boq = BOQCostManager.BuildBOQDocument(Doc, null, _groupingMode, allowIncremental);
                _health = BOQCostManager.ComputeBOQHealth(_boq);
                LoadSnapshotDropdown();
                // On first load open every section; subsequent refreshes preserve
                // state. P1.3 — suppress the auto-open-all once, so a persisted
                // "collapse all" (empty OpenSections) isn't immediately re-expanded.
                if (_openSections.Count == 0 && _boq != null && !_suppressAutoOpenOnce)
                    foreach (var s in _boq.Sections) _openSections.Add(SectionKey(s));
                _suppressAutoOpenOnce = false;
                RefreshDisplay();
                MaybeOfferWizard();   // G7 — offer the Cost Setup wizard on a fresh, unpriced project
            }
            catch (Exception ex)
            {
                StingLog.Error("BOQCostManagerPanel.RefreshAsync", ex);
                if (_paragraphCoverage != null)
                    _paragraphCoverage.Text = $"Refresh failed — see log. {ex.Message}";
            }
            finally
            {
                progress?.Close();
            }
        }

        /// <summary>
        /// G8 — pump the WPF dispatcher down to Background priority once so a
        /// just-shown modeless window completes its layout + render pass before
        /// the caller blocks the API thread with a long synchronous build. The
        /// standard WPF "DoEvents" idiom; safe to call from the API thread.
        /// </summary>
        private static void PumpDispatcher()
        {
            try
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    new Action(() => { }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex) { StingLog.Warn($"PumpDispatcher: {ex.Message}"); }
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
            // G2 — show provisional-sum movement (Σ actual − original) when reconciled.
            try
            {
                double psMovement = BoqProvisionalTrail.MovementUGX(Doc);
                if (Math.Abs(psMovement) >= 1)
                    _defaultCoverageText += $" | PS movement UGX {psMovement:+#,##0;-#,##0;0}";
            }
            catch (Exception ex) { StingLog.Warn($"BOQ PS movement strip: {ex.Message}"); }
            // G5 — carbon data-quality breakdown (mirrors rate confidence): share of
            // carbon-bearing rows that are EPD-verified vs database vs missing.
            try
            {
                var carbonRows = _boq.AllItems.Where(i => i.Source == BOQRowSource.Model
                    && !string.IsNullOrEmpty(i.CarbonMaterial)).ToList();
                if (carbonRows.Count > 0)
                {
                    int epd = carbonRows.Count(i => string.Equals(i.CarbonQuality, "Verified-EPD", StringComparison.OrdinalIgnoreCase));
                    // WP3 — "missing" is a property of the carbon FACTOR provenance,
                    // not of the computed total. A row with a resolved factor but
                    // zero geometry (EmbodiedCarbonKg == 0) is NOT a missing factor.
                    int miss = carbonRows.Count(i =>
                        string.Equals(i.CarbonQuality, "Missing", StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrEmpty(i.CarbonSource)
                        || string.Equals(i.CarbonSource, "none", StringComparison.OrdinalIgnoreCase));
                    double epdPct = 100.0 * epd / carbonRows.Count;
                    double missPct = 100.0 * miss / carbonRows.Count;
                    _defaultCoverageText += $" | Carbon EPD-verified {epdPct:F0}% · missing {missPct:F0}%";
                }
            }
            catch (Exception ex) { StingLog.Warn($"BOQ carbon-quality strip: {ex.Message}"); }
            _paragraphCoverage.Text = _defaultCoverageText;
            _paragraphCoverage.Foreground = Brushes.Gray;

            UpdateDriftBanner();
        }

        /// <summary>
        /// Phase 2C — passive drift indicator. Cheap checksum compare of the live
        /// bill against the last snapshot; only loads + classifies the snapshot
        /// when the checksum actually differs. Persists to boq_drift.json so the
        /// banner survives reopen until a new snapshot resets it. All file/CPU —
        /// safe on the WPF thread (no Revit API).
        /// </summary>
        private void UpdateDriftBanner()
        {
            if (_driftBanner == null || _boq == null) return;
            try
            {
                var snaps = BOQCostManager.ListSnapshots(Doc);
                if (snaps.Count == 0) { _driftBanner.Visibility = Visibility.Collapsed; return; }

                var last = snaps[0];
                string liveCk = StingTools.BOQ.Sync.BoqSnapshotHasher.ComputeChecksum(_boq);
                BOQDocument snap = null;
                string snapCk = last.Checksum;
                if (string.IsNullOrEmpty(snapCk))
                {
                    snap = BOQCostManager.LoadSnapshot(last.Path);
                    snapCk = snap != null ? StingTools.BOQ.Sync.BoqSnapshotHasher.ComputeChecksum(snap) : "";
                }

                bool drifted = !string.IsNullOrEmpty(snapCk) && !string.Equals(liveCk, snapCk, StringComparison.Ordinal);

                int changed = 0;
                double netDelta = 0;
                if (drifted)
                {
                    if (snap == null) snap = BOQCostManager.LoadSnapshot(last.Path);
                    if (snap != null)
                    {
                        var snapIndex = new HashSet<string>(snap.AllItems.Select(BOQCostManager.LineKey), StringComparer.OrdinalIgnoreCase);
                        var liveIndex = new HashSet<string>(_boq.AllItems.Select(BOQCostManager.LineKey), StringComparer.OrdinalIgnoreCase);
                        var snapByKey = new Dictionary<string, BOQLineItem>(StringComparer.OrdinalIgnoreCase);
                        foreach (var s in snap.AllItems) snapByKey[BOQCostManager.LineKey(s)] = s;

                        foreach (var li in _boq.AllItems.Where(i => i.Source == BOQRowSource.Model))
                        {
                            string k = BOQCostManager.LineKey(li);
                            if (!snapByKey.TryGetValue(k, out var s)) { changed++; continue; }
                            bool qtyCh = s.Quantity > 0 && Math.Abs(li.Quantity - s.Quantity) / s.Quantity > 0.001;
                            bool rateCh = s.RateUGX > 0 && Math.Abs(li.RateUGX - s.RateUGX) / s.RateUGX > 0.01;
                            if (qtyCh || rateCh) changed++;
                        }
                        changed += snap.AllItems.Count(i => i.Source == BOQRowSource.Model && !liveIndex.Contains(BOQCostManager.LineKey(i)));
                        netDelta = _boq.GrandTotalUGX - snap.GrandTotalUGX;
                    }
                }

                // Persist for the banner-on-reopen.
                BoqDriftStore.Save(Doc, new BoqDriftStatus
                {
                    Drifted = drifted, ChangedLines = changed, NetDeltaUgx = netDelta,
                    SnapshotLabel = last.Label ?? "", SnapshotChecksum = snapCk, LiveChecksum = liveCk
                });

                if (drifted)
                {
                    string deltaTxt = Math.Abs(netDelta) >= 1 ? $" · net {(netDelta >= 0 ? "+" : "")}UGX {netDelta:N0}" : "";
                    _driftBannerText.Text = $"⚠ Bill drifted from snapshot ‘{last.Label}’ — {changed} line(s) changed{deltaTxt}. Click to run Drift check.";
                    _driftBanner.Visibility = Visibility.Visible;
                }
                else
                {
                    _driftBanner.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex) { StingLog.Warn($"BOQ UpdateDriftBanner: {ex.Message}"); }
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
            // WP6 — derive the USD summary from the UGX summary ÷ the one project
            // FX rate, not by summing pre-rounded per-line TotalUSD (which drifts
            // by cents × line-count in a dual-currency tender).
            double fxUgxPerUsd = (_boq != null && _boq.ExchangeRateUgxPerUsd > 0) ? _boq.ExchangeRateUgxPerUsd : 0;
            string displayTotal = _displayCurrency == "USD"
                ? $"$ {(fxUgxPerUsd > 0 ? totalShownUgx / fxUgxPerUsd : 0):N2}"
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
            // G4 — optional labour / plant / material rate-split columns (off by
            // default; toggle via the Columns menu). Read-only; "—" when no split.
            AddIfVisible("Labour", new DataGridTextColumn
            {
                Header = "Labour", Binding = new Binding(nameof(BOQItemViewModel.LabourDisplay)),
                Width = new DataGridLength(95), IsReadOnly = true
            });
            AddIfVisible("Plant", new DataGridTextColumn
            {
                Header = "Plant", Binding = new Binding(nameof(BOQItemViewModel.PlantDisplay)),
                Width = new DataGridLength(95), IsReadOnly = true
            });
            AddIfVisible("Material", new DataGridTextColumn
            {
                Header = "Material", Binding = new Binding(nameof(BOQItemViewModel.MaterialDisplay)),
                Width = new DataGridLength(95), IsReadOnly = true
            });
            // G5 — carbon data-quality column (off by default; toggle via Columns).
            AddIfVisible("CO2 Quality", new DataGridTextColumn
            {
                Header = "CO₂ quality", Binding = new Binding(nameof(BOQItemViewModel.CarbonQualityDisplay)),
                Width = new DataGridLength(95), IsReadOnly = true
            });
            // Phase 2A — NRM2 measurement audit columns (off by default; toggle
            // via Columns). Gross geometry → openings/voids deducted → wastage →
            // Net (== Qty). "—" on rows with no separate measurement (manual/PS).
            AddIfVisible("Gross", new DataGridTextColumn
            {
                Header = "Gross", Binding = new Binding(nameof(BOQItemViewModel.GrossDisplay)),
                Width = new DataGridLength(70), IsReadOnly = true
            });
            AddIfVisible("Deduct", new DataGridTextColumn
            {
                Header = "Deduct", Binding = new Binding(nameof(BOQItemViewModel.DeductDisplay)),
                Width = new DataGridLength(70), IsReadOnly = true
            });
            AddIfVisible("Waste", new DataGridTextColumn
            {
                Header = "Waste", Binding = new Binding(nameof(BOQItemViewModel.WasteDisplay)),
                Width = new DataGridLength(70), IsReadOnly = true
            });
            AddIfVisible("Net", new DataGridTextColumn
            {
                Header = "Net", Binding = new Binding(nameof(BOQItemViewModel.NetDisplay)),
                Width = new DataGridLength(70), IsReadOnly = true
            });
            // Phase 2E — WBS / CBS columns (off by default; toggle via Columns).
            AddIfVisible("WBS", new DataGridTextColumn
            {
                Header = "WBS", Binding = new Binding(nameof(BOQItemViewModel.WbsDisplay)),
                Width = new DataGridLength(90), IsReadOnly = true
            });
            AddIfVisible("CBS", new DataGridTextColumn
            {
                Header = "CBS", Binding = new Binding(nameof(BOQItemViewModel.CbsDisplay)),
                Width = new DataGridLength(90), IsReadOnly = true
            });

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

            // Phase 2A — measurement derivation (gross → net), shown only when the
            // row carries a measurement note (model rows). Read-only audit line.
            var measNote = new FrameworkElementFactory(typeof(TextBlock));
            measNote.SetBinding(TextBlock.TextProperty,
                new Binding(nameof(BOQItemViewModel.MeasurementNoteDisplay)));
            measNote.SetValue(TextBlock.FontSizeProperty, 9.5);
            measNote.SetValue(TextBlock.ForegroundProperty,
                new SolidColorBrush(Color.FromRgb(110, 130, 160)));
            measNote.SetValue(TextBlock.MarginProperty, new Thickness(0, 5, 0, 0));
            measNote.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            measNote.SetBinding(TextBlock.VisibilityProperty,
                new Binding(nameof(BOQItemViewModel.MeasurementNoteVisibility)));
            detailSp.AppendChild(measNote);

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
        // ── P0.1 — dispatch busy-guard helpers ───────────────────────────────

        /// <summary>P0.1 — register a dispatch-capable button so the busy-guard can
        /// grey it while a command is in flight. Fluent: returns the button.</summary>
        private Button Guarded(Button b) { if (b != null) _dispatchButtons.Add(b); return b; }

        /// <summary>P0.1 — grey/un-grey every registered dispatch button.</summary>
        private void SetDispatchEnabled(bool enabled)
        {
            try { foreach (var b in _dispatchButtons) if (b != null) b.IsEnabled = enabled; }
            catch (Exception ex) { StingLog.Warn($"BOQ SetDispatchEnabled: {ex.Message}"); }
        }

        /// <summary>P0.1 — claim the single in-flight dispatch slot. Returns false
        /// (caller must abort) when a command is already running, so a second click
        /// can't re-enter the ExternalEvent and wedge it.</summary>
        private bool BeginDispatchGuard()
        {
            if (CommandRunning) return false;
            CommandRunning = true;
            SetDispatchEnabled(false);
            return true;
        }

        /// <summary>P0.1 — release the dispatch slot + re-enable buttons. Idempotent;
        /// called from the completion hook and from the immediate-failure path.</summary>
        private void EndDispatchGuard()
        {
            CommandRunning = false;
            SetDispatchEnabled(true);
        }

        private void DispatchAction(string tag, bool refreshAfter = true)
        {
            // P0.1 — single in-flight dispatch. Ignore clicks while a command runs
            // so a second Raise() can't return Pending and leave the panel lifeless.
            if (!BeginDispatchGuard())
            {
                StingLog.Info($"BOQ DispatchAction('{tag}') ignored — a command is already running.");
                return;
            }
            bool accepted = false;
            try
            {
                accepted = StingDockPanel.DispatchCommand(tag);
                if (accepted && refreshAfter)
                {
                    System.Threading.Tasks.Task.Delay(300)
                        .ContinueWith(_ => Dispatcher.BeginInvoke(new Action(RefreshAsync)));
                }
            }
            catch (Exception ex) { StingLog.Error($"BOQ DispatchAction({tag})", ex); }
            finally
            {
                // If the event wasn't accepted the command never runs, so its
                // completion hook will never fire — release the guard now to avoid a
                // permanently frozen panel. When accepted, Execute's finally releases it.
                if (!accepted) EndDispatchGuard();
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  P2.1 / Phase 1b — Schedule tab (4D programme + cash-flow S-curve + EVM)
        //  All inline, no popups (except the OS file-open for MSP/P6 import). Tasks
        //  persist to the unified store (ScheduleStore, _BIM_COORD/schedule.json);
        //  EVM is computed via the reused EvmCalculator engine; the S-curve
        //  is drawn on a plain Canvas. Reads run on the dockable pane's
        //  thread (Revit main thread) so direct model reads are safe; writes
        //  go through DispatchAction.
        // ══════════════════════════════════════════════════════════════════

        // Phase 1b — the unified store path (_BIM_COORD/schedule.json). Kept as a
        // helper because the EVM CSV export derives its output dir from it.
        private string SchedulePath() => ScheduleStore.PathFor(Doc);

        /// <summary>Find a DataGrid's internal ScrollViewer (DG_ScrollViewer) so the
        /// nested-scroll wheel logic can tell whether the grid can still scroll.</summary>
        private static ScrollViewer FindChildScrollViewer(DependencyObject parent)
        {
            if (parent == null) return null;
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer sv) return sv;
                var nested = FindChildScrollViewer(child);
                if (nested != null) return nested;
            }
            return null;
        }

        /// <summary>Slice 1 — wheel over a schedule grid scrolls the grid's own rows;
        /// at the grid's scroll boundary (or when it has nothing to scroll) the event
        /// is forwarded to the enclosing ScrollViewer so the whole tab still scrolls.</summary>
        private void AttachScheduleGridWheel(DataGrid grid)
        {
            grid.PreviewMouseWheel += (s, e) =>
            {
                if (e.Handled) return;
                var sv = FindChildScrollViewer(grid);
                if (sv != null && sv.ScrollableHeight > 0)
                {
                    bool atTop = sv.VerticalOffset <= 0.0;
                    bool atBottom = sv.VerticalOffset >= sv.ScrollableHeight - 0.5;
                    // Let the grid consume the wheel unless it's at the boundary that way.
                    if ((e.Delta < 0 && !atBottom) || (e.Delta > 0 && !atTop)) return;
                }
                e.Handled = true;
                var fwd = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                { RoutedEvent = UIElement.MouseWheelEvent, Source = grid };
                (grid.Parent as UIElement)?.RaiseEvent(fwd);
            };
        }

        /// <summary>Slice 3 — a fixed-width trailing column with a small ✕ button that
        /// removes the bound row from its ObservableCollection, then persists +
        /// recomputes. No popup — immediate, and re-addable via the ＋ buttons.</summary>
        private DataGridTemplateColumn MakeDeleteColumn<T>(
            System.Collections.ObjectModel.ObservableCollection<T> coll, DataGrid grid)
        {
            var col = new DataGridTemplateColumn
            {
                Header = "", Width = 34, CanUserResize = false, CanUserSort = false, CanUserReorder = false
            };
            var factory = new FrameworkElementFactory(typeof(Button));
            factory.SetValue(System.Windows.Controls.ContentControl.ContentProperty, "✕");
            factory.SetValue(Button.FontSizeProperty, 11.0);
            factory.SetValue(Button.WidthProperty, 22.0);
            factory.SetValue(Button.HeightProperty, 22.0);
            factory.SetValue(Button.PaddingProperty, new Thickness(0));
            factory.SetValue(Button.CursorProperty, Cursors.Hand);
            factory.SetValue(Button.ToolTipProperty, "Remove this row");
            factory.SetValue(Button.BackgroundProperty, Brushes.White);
            factory.SetValue(Button.ForegroundProperty, RedBrush);
            factory.SetValue(Button.BorderBrushProperty, BorderColor);
            factory.SetValue(Button.BorderThicknessProperty, new Thickness(1));
            factory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            factory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            factory.AddHandler(Button.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                if ((s as Button)?.DataContext is T item) DeleteScheduleRow(coll, item, grid);
            }));
            col.CellTemplate = new DataTemplate { VisualTree = factory };
            return col;
        }

        /// <summary>Remove a schedule row + persist + recompute (the S-curve / EVM
        /// update immediately). Selects a sensible neighbour so the grid keeps focus.</summary>
        private void DeleteScheduleRow<T>(
            System.Collections.ObjectModel.ObservableCollection<T> coll, T item, DataGrid grid)
        {
            try
            {
                if (coll == null || item == null) return;
                int idx = coll.IndexOf(item);
                if (idx < 0) return;
                coll.Remove(item);
                SaveSchedule();
                RecalcSchedule();
                // Slice 4 polish — keep a row selected + in view after the delete.
                if (grid != null && coll.Count > 0)
                {
                    int sel = Math.Min(idx, coll.Count - 1);
                    grid.SelectedIndex = sel;
                    try { grid.ScrollIntoView(coll[sel]); } catch { /* non-fatal */ }
                }
            }
            catch (Exception ex) { StingLog.Error("BOQ DeleteScheduleRow", ex); }
        }

        /// <summary>Slice 4 — single full-row selection with a theme-navy highlight, and
        /// the Delete key removes the focused row (same path as the ✕). Editing a cell is
        /// not affected (the keystroke's source is the editor TextBox, not a cell).</summary>
        private void StyleScheduleGrid<T>(
            DataGrid grid, System.Collections.ObjectModel.ObservableCollection<T> coll)
        {
            grid.SelectionMode = DataGridSelectionMode.Single;
            grid.SelectionUnit = DataGridSelectionUnit.FullRow;

            var sel = new SolidColorBrush(Color.FromRgb(0x2E, 0x5E, 0x8E)); sel.Freeze();
            var selInactive = new SolidColorBrush(Color.FromRgb(0xC9, 0xD6, 0xE5)); selInactive.Freeze();
            grid.Resources[SystemColors.HighlightBrushKey] = sel;
            grid.Resources[SystemColors.HighlightTextBrushKey] = Brushes.White;
            grid.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = selInactive;
            grid.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = NavyBrush;

            grid.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Delete
                    && e.OriginalSource is DataGridCell
                    && grid.SelectedItem is T item)
                {
                    DeleteScheduleRow(coll, item, grid);
                    e.Handled = true;
                }
            };
        }

        /// <summary>Slice 4 — select the just-added item and scroll it into view.</summary>
        private void SelectScheduleRow(DataGrid grid, object item)
        {
            try
            {
                if (grid == null || item == null) return;
                grid.SelectedItem = item;
                grid.ScrollIntoView(item);
            }
            catch (Exception ex) { StingLog.Warn($"BOQ SelectScheduleRow: {ex.Message}"); }
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
            barSp.Children.Add(BuildScheduleBtn("＋ Period", AddSchedulePeriodRow));            // G6
            barSp.Children.Add(BuildScheduleBtn("＋ Milestone", AddMilestoneRow));              // G6
            barSp.Children.Add(BuildScheduleBtn("⤿ Sync % from cert", SyncPercentFromCert));   // G6
            barSp.Children.Add(BuildScheduleBtn("⟳ Seed from Revit phases", SeedScheduleFromRevitPhases));
            barSp.Children.Add(BuildScheduleBtn("⤓ Import MSP/P6 XML", ImportProgrammeXmlInline));   // Phase 1c
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

            // Phase grid — full width, bounded height with its own vertical scroll.
            _scheduleGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                Margin = new Thickness(0, 0, 0, 12),
                ItemsSource = _schedulePhases,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxHeight = 240,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                CanUserResizeColumns = true,
                CanUserSortColumns = true
            };
            AttachScheduleGridWheel(_scheduleGrid);
            StyleScheduleGrid(_scheduleGrid, _schedulePhases);
            // Name fills (star); date/number columns Auto-size to header+content so
            // nothing clips. SortMemberPath sorts dates by the real DateTime, not text.
            _scheduleGrid.Columns.Add(new DataGridTextColumn { Header = "Phase", Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 140,
                Binding = new Binding("Name") { Mode = BindingMode.TwoWay } });
            _scheduleGrid.Columns.Add(new DataGridTextColumn { Header = "Start", Width = DataGridLength.Auto, MinWidth = 92, SortMemberPath = "Start",
                Binding = new Binding("StartStr") { Mode = BindingMode.TwoWay } });
            _scheduleGrid.Columns.Add(new DataGridTextColumn { Header = "End", Width = DataGridLength.Auto, MinWidth = 92, SortMemberPath = "End",
                Binding = new Binding("EndStr") { Mode = BindingMode.TwoWay } });
            _scheduleGrid.Columns.Add(new DataGridTextColumn { Header = "% Complete", Width = DataGridLength.Auto, MinWidth = 90,
                Binding = new Binding("PercentComplete") { Mode = BindingMode.TwoWay, StringFormat = "0.#" } });
            var planCol = new DataGridTextColumn { Header = "Planned (UGX)", Width = DataGridLength.Auto, MinWidth = 110, IsReadOnly = true,
                Binding = new Binding("PlannedCost") { StringFormat = "N0" } };
            _scheduleGrid.Columns.Add(planCol);
            _scheduleGrid.Columns.Add(MakeDeleteColumn(_schedulePhases, _scheduleGrid));   // Slice 3
            _scheduleGrid.CellEditEnding += (s, e) =>
            {
                // Commit the edit to the source first, then persist + recompute.
                Dispatcher.BeginInvoke(new Action(() => { SaveSchedule(); RecalcSchedule(); }),
                    System.Windows.Threading.DispatcherPriority.Background);
            };

            // G6 — Periods grid (PV/EV/AC over time). EV driver = overall % complete;
            // AC = cumulative actual at the period end. PV is derived from the baseline.
            _periodsGrid = new DataGrid
            {
                AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                Margin = new Thickness(0, 0, 0, 4), ItemsSource = _schedulePeriods, FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxHeight = 200,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                CanUserResizeColumns = true,
                CanUserSortColumns = true
            };
            AttachScheduleGridWheel(_periodsGrid);
            StyleScheduleGrid(_periodsGrid, _schedulePeriods);
            _periodsGrid.Columns.Add(new DataGridTextColumn { Header = "Period end", Width = DataGridLength.Auto, MinWidth = 100, SortMemberPath = "Date",
                Binding = new Binding("DateStr") { Mode = BindingMode.TwoWay } });
            _periodsGrid.Columns.Add(new DataGridTextColumn { Header = "% Complete (overall)", Width = DataGridLength.Auto, MinWidth = 140,
                Binding = new Binding("PercentComplete") { Mode = BindingMode.TwoWay, StringFormat = "0.#" } });
            // Actual-cost column takes the slack so the periods grid fills the width.
            _periodsGrid.Columns.Add(new DataGridTextColumn { Header = "Actual cost (cum, UGX)", Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 170,
                Binding = new Binding("Acwp") { Mode = BindingMode.TwoWay, StringFormat = "N0" } });
            _periodsGrid.Columns.Add(MakeDeleteColumn(_schedulePeriods, _periodsGrid));   // Slice 3
            _periodsGrid.CellEditEnding += (s, e) =>
                Dispatcher.BeginInvoke(new Action(() => { SaveSchedule(); RecalcSchedule(); }),
                    System.Windows.Threading.DispatcherPriority.Background);

            // G6 — Milestones grid.
            _milestonesGrid = new DataGrid
            {
                AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                Margin = new Thickness(0, 0, 0, 4), ItemsSource = _milestones, FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxHeight = 200,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                CanUserResizeColumns = true,
                CanUserSortColumns = true
            };
            AttachScheduleGridWheel(_milestonesGrid);
            StyleScheduleGrid(_milestonesGrid, _milestones);
            _milestonesGrid.Columns.Add(new DataGridTextColumn { Header = "Milestone", Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 140,
                Binding = new Binding("Name") { Mode = BindingMode.TwoWay } });
            _milestonesGrid.Columns.Add(new DataGridTextColumn { Header = "Date", Width = DataGridLength.Auto, MinWidth = 100, SortMemberPath = "Date",
                Binding = new Binding("DateStr") { Mode = BindingMode.TwoWay } });
            _milestonesGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Done", Width = DataGridLength.Auto, MinWidth = 56,
                Binding = new Binding("Done") { Mode = BindingMode.TwoWay } });
            _milestonesGrid.Columns.Add(MakeDeleteColumn(_milestones, _milestonesGrid));   // Slice 3
            _milestonesGrid.CellEditEnding += (s, e) =>
                Dispatcher.BeginInvoke(new Action(() => { SaveSchedule(); RecalcSchedule(); }),
                    System.Windows.Threading.DispatcherPriority.Background);

            UIElement SubHeader(string t) => new TextBlock
            {
                Text = t, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = NavyBrush,
                Margin = new Thickness(0, 10, 0, 4)
            };

            var lower = new StackPanel { Margin = new Thickness(12, 0, 12, 12) };
            lower.Children.Add(SubHeader("PROGRAMME PHASES (cost-loaded baseline → PV)"));
            lower.Children.Add(_scheduleGrid);
            lower.Children.Add(SubHeader("REPORTING PERIODS (overall % → EV · cumulative actual → AC)"));
            lower.Children.Add(_periodsGrid);
            lower.Children.Add(SubHeader("MILESTONES (red = slipped: past-due and not done)"));
            lower.Children.Add(_milestonesGrid);

            // Slice 1 — horizontal scrolling is DISABLED on the outer ScrollViewer so
            // it bounds the content to the viewport width. (With it Auto/Visible the
            // ScrollViewer hands the content unbounded width, which collapses the
            // star columns and shrinks each grid to ~150px — the truncation bug.)
            // Each grid scrolls horizontally on its own when its columns overflow.
            var gridScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = lower
            };
            root.Children.Add(gridScroll);

            return root;
        }

        // ── G6 — period / milestone row helpers ─────────────────────────────
        private void AddSchedulePeriodRow()
        {
            var lastDate = _schedulePeriods.LastOrDefault()?.Date ?? DateTime.Today;
            double lastPct = _schedulePeriods.LastOrDefault()?.PercentComplete ?? 0;
            double lastAc = _schedulePeriods.LastOrDefault()?.Acwp ?? 0;
            var row = new SchedulePeriod { Date = lastDate.AddMonths(1), PercentComplete = lastPct, Acwp = lastAc };
            _schedulePeriods.Add(row);
            SaveSchedule(); RecalcSchedule();
            SelectScheduleRow(_periodsGrid, row);   // Slice 4 — scroll the new row into view
        }

        private void AddMilestoneRow()
        {
            var row = new ScheduleMilestone { Name = "New milestone", Date = DateTime.Today.AddMonths(1), Done = false };
            _milestones.Add(row);
            SaveSchedule(); RecalcSchedule();
            SelectScheduleRow(_milestonesGrid, row);   // Slice 4
        }

        /// <summary>
        /// G6 — link the schedule's % complete to the Payment-Cert %-complete so
        /// they're one source of truth. Reads the latest non-draft cert's
        /// OverallPercentComplete and writes it into the latest reporting period
        /// (creating one for today if none exists).
        /// </summary>
        private void SyncPercentFromCert()
        {
            try
            {
                var certPaths = StingTools.Core.PaymentCert.PaymentCertEngine.ListCerts(Doc);
                var cert = certPaths
                    .Select(StingTools.Core.PaymentCert.PaymentCertEngine.Load)
                    .Where(c => c != null && c.Status != StingTools.Core.PaymentCert.PaymentCertStatus.Draft
                                          && c.Status != StingTools.Core.PaymentCert.PaymentCertStatus.Superseded)
                    .OrderByDescending(c => c.CertNumber)
                    .FirstOrDefault();
                if (cert == null)
                {
                    FlashHint(null, "No issued payment certificate found — issue a cert first (Payment Certificates).");
                    return;
                }
                double pct = Math.Round(cert.OverallPercentComplete, 1);
                var period = _schedulePeriods.LastOrDefault();
                if (period == null)
                {
                    period = new SchedulePeriod { Date = DateTime.Today, PercentComplete = pct, Acwp = _scheduleActualToDate };
                    _schedulePeriods.Add(period);
                }
                else period.PercentComplete = pct;
                SaveSchedule(); RecalcSchedule();
                FlashHint(null, $"Synced overall % complete from Cert #{cert.CertNumber}: {pct:0.#}%.");
            }
            catch (Exception ex) { StingLog.Error("BOQ SyncPercentFromCert", ex); }
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
            return Guarded(b);
        }

        /// <summary>Phase 1b — load the unified ScheduleModel (ScheduleStore migrates
        /// the legacy stores on first use); seed from Revit phases only when empty.</summary>
        private void EnsureScheduleLoaded()
        {
            if (_scheduleLoaded) return;
            _scheduleLoaded = true;
            try
            {
                _scheduleModel = ScheduleStore.Load(Doc) ?? new ScheduleModel();
                _schedulePhases.Clear();
                foreach (var t in _scheduleModel.Tasks ?? new List<ScheduleTask>())
                    if (t != null) _schedulePhases.Add(t);
                _schedulePeriods.Clear();
                foreach (var pr in _scheduleModel.Periods ?? new List<SchedulePeriod>())
                    if (pr != null) _schedulePeriods.Add(pr);
                _milestones.Clear();
                foreach (var m in _scheduleModel.Milestones ?? new List<ScheduleMilestone>())
                    if (m != null) _milestones.Add(m);
                _scheduleActualToDate = _scheduleModel.ActualCostToDate;
                if (_evmActualsBox != null) _evmActualsBox.Text = _scheduleModel.ActualCostToDate.ToString("F0", CultureInfo.InvariantCulture);
                if (_evmAsOfBox != null && !string.IsNullOrWhiteSpace(_scheduleModel.AsOf)) _evmAsOfBox.Text = _scheduleModel.AsOf;
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
                        _schedulePhases.Add(new ScheduleTask
                        {
                            Id = i + 1,
                            Name = ph.Name ?? $"Phase {i + 1}",
                            Start = start.AddMonths(i),
                            End = start.AddMonths(i + 1),
                            PercentComplete = 0,
                            Category = "Phase"
                        });
                        i++;
                    }
                }
                else
                {
                    // No Revit phases — a single construction phase spanning a year.
                    _schedulePhases.Add(new ScheduleTask
                    { Id = 1, Name = "Construction", Start = start, End = start.AddMonths(12), PercentComplete = 0, Category = "Phase" });
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
            int nextId = _schedulePhases.Count == 0 ? 1 : _schedulePhases.Max(t => t.Id) + 1;
            var row = new ScheduleTask
            { Id = nextId, Name = "New phase", Start = start, End = start.AddMonths(1), PercentComplete = 0, Category = "Phase" };
            _schedulePhases.Add(row);
            SaveSchedule();
            RecalcSchedule();
            SelectScheduleRow(_scheduleGrid, row);   // Slice 4 — scroll the new row into view
        }

        private void SaveSchedule()
        {
            if (!_scheduleLoaded) return;
            try
            {
                if (_scheduleModel == null) _scheduleModel = new ScheduleModel();
                // The collections hold the model's own ScheduleTask instances, so
                // edits (WBS / predecessors / element links included) are preserved;
                // ToList() captures adds / removes / reorders.
                _scheduleModel.Tasks = _schedulePhases.ToList();
                _scheduleModel.Periods = _schedulePeriods.ToList();
                _scheduleModel.Milestones = _milestones.ToList();
                _scheduleModel.ActualCostToDate = ParseActualsBox();
                _scheduleModel.AsOf = _evmAsOfBox?.Text?.Trim();
                ScheduleStore.Save(Doc, _scheduleModel);
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
                double bcws = 0, bcwp = 0, acwp;

                // G6 — when reporting periods exist they are the source of truth for
                // EV + AC: EV = BAC × overall % complete at the latest period; AC =
                // its cumulative actual; PV = the cost-loaded baseline at the period
                // date. Otherwise fall back to the legacy single-as-of computation
                // (phase %-complete + the manual ACWP box) so nothing regresses.
                var lastPeriod = _schedulePeriods.OrderBy(p => p.Date).LastOrDefault();
                if (lastPeriod != null)
                {
                    asOf = lastPeriod.Date;
                    bcwp = bac * (lastPeriod.PercentComplete / 100.0);
                    acwp = lastPeriod.Acwp;
                    foreach (var p in _schedulePhases)
                    {
                        double span = Math.Max(1, (p.End - p.Start).TotalDays);
                        double frac = Math.Max(0, Math.Min(1, (asOf - p.Start).TotalDays / span));
                        bcws += p.PlannedCost * frac;
                    }
                    _scheduleActualToDate = acwp;
                }
                else
                {
                    foreach (var p in _schedulePhases)
                    {
                        double span = Math.Max(1, (p.End - p.Start).TotalDays);
                        double frac = Math.Max(0, Math.Min(1, (asOf - p.Start).TotalDays / span));
                        bcws += p.PlannedCost * frac;
                        bcwp += p.PlannedCost * (p.PercentComplete / 100.0);
                    }
                    acwp = _scheduleActualToDate;
                }

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
            // G6 — show the Payment-Cert overall % alongside so the two % sources
            // are visibly reconciled (one source of truth).
            double? certPct = LatestCertPercent();
            if (certPct.HasValue)
            {
                double schedPct = p.Bac > 0 ? 100.0 * p.Bcwp / p.Bac : 0;
                bool match = Math.Abs(certPct.Value - schedPct) < 0.5;
                _evmStrip.Children.Add(EvmMetric("Cert %", $"{certPct.Value:0.#}%", match ? GreenBrush : AmberBrush));
            }
        }

        /// <summary>G6 — latest issued payment certificate's overall % complete (or null).</summary>
        private double? LatestCertPercent()
        {
            try
            {
                var cert = StingTools.Core.PaymentCert.PaymentCertEngine.ListCerts(Doc)
                    .Select(StingTools.Core.PaymentCert.PaymentCertEngine.Load)
                    .Where(c => c != null && c.Status != StingTools.Core.PaymentCert.PaymentCertStatus.Draft
                                          && c.Status != StingTools.Core.PaymentCert.PaymentCertStatus.Superseded)
                    .OrderByDescending(c => c.CertNumber)
                    .FirstOrDefault();
                return cert != null ? (double?)Math.Round(cert.OverallPercentComplete, 1) : null;
            }
            catch { return null; }
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

            // G6 — period series (PV/EV/AC over time) drives the curve when present.
            var periods = _schedulePeriods.Where(p => p.Date >= t0.AddDays(-1) && p.Date <= t1.AddDays(1))
                .OrderBy(p => p.Date).ToList();
            double bcwp, acwp;
            if (periods.Count > 0)
            {
                var last = periods.Last();
                bcwp = bacTotal * (last.PercentComplete / 100.0);
                acwp = last.Acwp;
                asOf = last.Date;
            }
            else
            {
                bcwp = phases.Sum(p => p.PlannedCost * (p.PercentComplete / 100.0));
                acwp = _scheduleActualToDate;
            }

            // BCWS at as-of (for SPI → forecast band).
            double BcwsAt(DateTime t)
            {
                double cum = 0;
                foreach (var p in phases)
                {
                    double span = Math.Max(1, (p.End - p.Start).TotalDays);
                    cum += p.PlannedCost * Math.Max(0, Math.Min(1, (t - p.Start).TotalDays / span));
                }
                return cum;
            }

            // Forecast band (EAC range) — low = CPI=1, high = SPI×CPI weighted.
            double cpi = acwp > 0 ? bcwp / acwp : 0;
            double spi = BcwsAt(asOf) > 0 ? bcwp / BcwsAt(asOf) : 0;
            double remaining = Math.Max(0, bacTotal - bcwp);
            double eacLow = acwp + remaining;
            double eacHigh = (cpi > 0 && spi > 0) ? acwp + remaining / (cpi * spi)
                           : (cpi > 0 ? acwp + remaining / cpi : eacLow);
            if (eacHigh < eacLow) { var t = eacLow; eacLow = eacHigh; eacHigh = t; }

            double maxVal = Math.Max(bacTotal, Math.Max(bcwp, Math.Max(acwp, periods.Count > 0 ? eacHigh : 0)));
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
                planned.Add(new System.Windows.Point(X(t), Y(BcwsAt(t))));
            }
            c.Children.Add(new Polyline { Points = planned, Stroke = NavyBrush, StrokeThickness = 2 });

            double asOfX = X(asOf);
            if (periods.Count > 0)
            {
                // EV + AC polylines through the reporting periods.
                var evPts = new PointCollection { new System.Windows.Point(X(t0), Y(0)) };
                var acPts = new PointCollection { new System.Windows.Point(X(t0), Y(0)) };
                foreach (var p in periods)
                {
                    double px = X(p.Date);
                    evPts.Add(new System.Windows.Point(px, Y(bacTotal * (p.PercentComplete / 100.0))));
                    acPts.Add(new System.Windows.Point(px, Y(p.Acwp)));
                }
                c.Children.Add(new Polyline { Points = evPts, Stroke = GreenBrush, StrokeThickness = 2 });
                c.Children.Add(new Polyline { Points = acPts, Stroke = AmberBrush, StrokeThickness = 2 });

                // Forecast band from the last actual point to completion.
                double endX = X(t1);
                var band = new System.Windows.Shapes.Polygon
                {
                    Fill = new SolidColorBrush(Color.FromArgb(40, 200, 120, 60)),
                    Points = new PointCollection
                    {
                        new System.Windows.Point(asOfX, Y(acwp)),
                        new System.Windows.Point(endX, Y(eacLow)),
                        new System.Windows.Point(endX, Y(eacHigh)),
                    }
                };
                c.Children.Add(band);
                var lowLine = MakeLine(asOfX, Y(acwp), endX, Y(eacLow), GreenBrush, 1);
                lowLine.StrokeDashArray = new DoubleCollection { 4, 3 }; c.Children.Add(lowLine);
                var highLine = MakeLine(asOfX, Y(acwp), endX, Y(eacHigh), RedBrush, 1);
                highLine.StrokeDashArray = new DoubleCollection { 4, 3 }; c.Children.Add(highLine);
            }
            else
            {
                // Legacy single-as-of ramps.
                c.Children.Add(MakeLine(padL, Y(0), asOfX, Y(bcwp), GreenBrush, 2));
                c.Children.Add(MakeLine(padL, Y(0), asOfX, Y(acwp), AmberBrush, 2));
            }

            // As-of vertical guide.
            var guide = MakeLine(asOfX, padT, asOfX, padT + plotH, Brushes.Gray, 1);
            guide.StrokeDashArray = new DoubleCollection { 3, 3 };
            c.Children.Add(guide);

            // G6 — milestone markers: diamond + tick; red = slipped (past-due, not done).
            foreach (var m in _milestones)
            {
                if (m.Date < t0 || m.Date > t1) continue;
                double mx = X(m.Date);
                bool slipped = !m.Done && m.Date < DateTime.Today;
                Brush mb = m.Done ? GreenBrush : slipped ? RedBrush : Brushes.Gray;
                var tick = MakeLine(mx, padT + 9, mx, padT + plotH, mb, 1);
                tick.StrokeDashArray = new DoubleCollection { 2, 4 };
                c.Children.Add(tick);
                c.Children.Add(new System.Windows.Shapes.Polygon
                {
                    Fill = mb,
                    Points = new PointCollection
                    {
                        new System.Windows.Point(mx, padT - 1),
                        new System.Windows.Point(mx + 4, padT + 4),
                        new System.Windows.Point(mx, padT + 9),
                        new System.Windows.Point(mx - 4, padT + 4),
                    }
                });
            }

            // Legend.
            AddLegend(c, padL + 4, padT, "Planned", NavyBrush, "Earned (EV)", GreenBrush,
                "Actual (AC)", AmberBrush, "EAC band", RedBrush);
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

        // Phase 1c — import a real programme (MS Project XML or Primavera P6 XML)
        // into the unified schedule. The only popup is the OS file-open; tasks land
        // in _BIM_COORD/schedule.json via the 4D adapter and the tab reloads inline.
        // Binary .mpp / .xer are out of scope (export to XML from MS Project / P6).
        private void ImportProgrammeXmlInline()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Import programme — MS Project XML / Primavera P6 XML",
                    Filter = "Programme XML (*.xml)|*.xml"
                };
                if (dlg.ShowDialog() != true) return;

                var schedule = StingTools.BIMManager.Scheduling4DEngine.ImportProgrammeXML(dlg.FileName);
                if (schedule["error"] != null)
                {
                    FlashHint(null, $"Import failed: {schedule["error"]}");
                    return;
                }
                int n = (int)(schedule["total_tasks"] ?? 0);
                if (n == 0)
                {
                    FlashHint(null, "No tasks / activities found. Export to MS Project XML or Primavera P6 XML "
                        + "(.mpp / .xer binaries are not supported).");
                    return;
                }

                // Persist to the single unified store, then reload the tab from it.
                ScheduleStore.Save4d(Doc, schedule);
                _scheduleLoaded = false;
                EnsureScheduleLoaded();
                RecalcSchedule();
                FlashHint(null, $"Imported {n} task(s) from {System.IO.Path.GetFileName(dlg.FileName)} "
                    + "into the unified schedule.");
            }
            catch (Exception ex) { StingLog.Error("BOQ ImportProgrammeXmlInline", ex); }
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
                // G6 — mirror the period-aware EVM used on screen.
                double bcws = 0, bcwp, acwp;
                var lastPeriod = _schedulePeriods.OrderBy(p => p.Date).LastOrDefault();
                if (lastPeriod != null) { asOf = lastPeriod.Date; bcwp = bac * (lastPeriod.PercentComplete / 100.0); acwp = lastPeriod.Acwp; }
                else { bcwp = 0; acwp = _scheduleActualToDate; }
                foreach (var p in _schedulePhases)
                {
                    double span = Math.Max(1, (p.End - p.Start).TotalDays);
                    double frac = Math.Max(0, Math.Min(1, (asOf - p.Start).TotalDays / span));
                    bcws += p.PlannedCost * frac;
                    if (lastPeriod == null) bcwp += p.PlannedCost * (p.PercentComplete / 100.0);
                }
                var period = StingTools.Core.Evm.EvmCalculator.Compute(bac, bcws, bcwp, acwp, asOf);

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
                        // G6 — per-period PV/EV/AC timeline.
                        sb.AppendLine("PeriodEnd,PercentComplete,PV_BCWS_UGX,EV_BCWP_UGX,AC_ACWP_UGX");
                        foreach (var pr in _schedulePeriods.OrderBy(p => p.Date))
                        {
                            double pv = 0;
                            foreach (var ph in _schedulePhases)
                            {
                                double span = Math.Max(1, (ph.End - ph.Start).TotalDays);
                                pv += ph.PlannedCost * Math.Max(0, Math.Min(1, (pr.Date - ph.Start).TotalDays / span));
                            }
                            sb.AppendLine($"{pr.DateStr},{pr.PercentComplete:0.#},{pv:0},{bac * pr.PercentComplete / 100.0:0},{pr.Acwp:0}");
                        }
                        sb.AppendLine();
                        // G6 — milestones with slippage flag.
                        sb.AppendLine("Milestone,Date,Done,Status");
                        foreach (var m in _milestones.OrderBy(m => m.Date))
                        {
                            string status = m.Done ? "Done" : (m.Date < DateTime.Today ? "Slipped" : "Upcoming");
                            sb.AppendLine($"\"{m.Name}\",{m.DateStr},{(m.Done ? "Y" : "N")},{status}");
                        }
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
                if (_milestones.Count > 0)
                {
                    b.AddSection("MILESTONES");
                    foreach (var m in _milestones.OrderBy(m => m.Date))
                    {
                        string status = m.Done ? "Done" : (m.Date < DateTime.Today ? "SLIPPED" : "Upcoming");
                        b.Metric(m.Name, status, m.DateStr);
                    }
                }
                if (!string.IsNullOrEmpty(csvPath)) b.SetCsvPath(csvPath);
                ShowInlineResult(b);
            }
            catch (Exception ex) { StingLog.Error("BOQ ExportScheduleEvmInline", ex); }
        }
    }

    // Phase 1b — the BoqSchedulePhase / BoqScheduleState / BoqSchedulePeriod /
    // BoqMilestone view-models were retired: the Schedule tab now binds directly
    // to the unified Core.Schedule.ScheduleTask / SchedulePeriod / ScheduleMilestone
    // (single source of truth). boq_schedule.json is no longer written — the
    // migration importer reads it once into schedule.json.

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

        // G4 — labour / plant / material rate split (per-unit). "—" when the rate
        // source carries no split (most rows). Read-only.
        public string LabourDisplay => FmtComponent(_item.LabourUGX);
        public string PlantDisplay => FmtComponent(_item.PlantUGX);
        public string MaterialDisplay => FmtComponent(_item.MaterialUGX);
        private string FmtComponent(double? v)
            => v.HasValue
                ? (_currency == "USD"
                    ? $"$ {(_ugxPerUsd > 0 ? v.Value / _ugxPerUsd : 0):N2}"
                    : $"UGX {v.Value:N0}")
                : "—";

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

        // G5 — carbon data-quality band (Verified-EPD / Database / Missing).
        public string CarbonQualityDisplay => string.IsNullOrEmpty(_item.CarbonQuality)
            ? "—" : _item.CarbonQuality;

        // Phase 2A — NRM2 measurement audit columns. "—" on rows with no
        // separate measurement (manual / PS / pre-2A snapshots where Gross == 0).
        public string GrossDisplay => _item.GrossQuantity > 0
            ? _item.GrossQuantity.ToString("N3", CultureInfo.InvariantCulture) : "—";
        public string DeductDisplay => _item.DeductionQuantity > 0.0005
            ? _item.DeductionQuantity.ToString("N3", CultureInfo.InvariantCulture) : "—";
        public string WasteDisplay => _item.WastageQuantity > 0.0005
            ? _item.WastageQuantity.ToString("N3", CultureInfo.InvariantCulture) : "—";
        public string NetDisplay => _item.Quantity.ToString("N3", CultureInfo.InvariantCulture);
        public string MeasurementNoteDisplay => _item.MeasurementNote ?? "";
        // Phase 2E — WBS / CBS.
        public string WbsDisplay => string.IsNullOrEmpty(_item.WbsCode) ? "—" : _item.WbsCode;
        public string CbsDisplay => string.IsNullOrEmpty(_item.CbsCode) ? "—" : _item.CbsCode;
        public Visibility MeasurementNoteVisibility
            => string.IsNullOrEmpty(_item.MeasurementNote) ? Visibility.Collapsed : Visibility.Visible;

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
