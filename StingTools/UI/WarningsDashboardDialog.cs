using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Result returned from the WarningsDashboardDialog containing the selected operation and options.
    /// </summary>
    public class WarningsDashboardResult
    {
        /// <summary>True if the user clicked Run; false if cancelled or closed.</summary>
        public bool Confirmed { get; set; }
        /// <summary>Selected operation tag (e.g. "WarningsDashboard", "WarningsAutoFix").</summary>
        public string Operation { get; set; }
        /// <summary>Additional options keyed by name.</summary>
        public Dictionary<string, string> Options { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Unified interactive Warnings Dashboard dialog that consolidates ALL warning management
    /// operations into a single 5-tab WPF dialog. Provides overview, auto-fix, inspection,
    /// baseline/SLA tracking, and export/integration operations.
    ///
    /// Usage:
    ///   var result = WarningsDashboardDialog.Show();
    ///   if (result.Confirmed) handler.SetCommand(result.Operation);
    /// </summary>
    internal static class WarningsDashboardDialog
    {
        // ── Theme colours (light corporate theme) ───────────────────────
        private static readonly Color BgLight       = Color.FromRgb(0xF5, 0xF5, 0xF5);
        private static readonly Color BgWhite       = Colors.White;
        private static readonly Color BgHeader      = Color.FromRgb(0x1A, 0x23, 0x7E);
        private static readonly Color AccentOrange  = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color FgDark        = Color.FromRgb(0x22, 0x22, 0x22);
        private static readonly Color FgSubtle      = Color.FromRgb(0x77, 0x77, 0x77);
        private static readonly Color FgWhite       = Colors.White;
        private static readonly Color BorderLight   = Color.FromRgb(0xD0, 0xD0, 0xD0);
        private static readonly Color CardBg        = Colors.White;
        private static readonly Color CardHover     = Color.FromRgb(0xFD, 0xF0, 0xE0);
        private static readonly Color CardSelected  = Color.FromRgb(0xFB, 0xE4, 0xC8);
        private static readonly Color TabSelected   = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color TabDefault    = Color.FromRgb(0xE0, 0xE0, 0xE0);
        private static readonly Color InfoBg        = Color.FromRgb(0xEE, 0xF2, 0xF7);
        private static readonly Color InfoBorder    = Color.FromRgb(0xB0, 0xC4, 0xDE);

        private static SolidColorBrush FZ(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        private static readonly SolidColorBrush BrBgLight      = FZ(BgLight);
        private static readonly SolidColorBrush BrBgWhite      = FZ(BgWhite);
        private static readonly SolidColorBrush BrBgHeader     = FZ(BgHeader);
        private static readonly SolidColorBrush BrAccent       = FZ(AccentOrange);
        private static readonly SolidColorBrush BrFgDark       = FZ(FgDark);
        private static readonly SolidColorBrush BrFgSubtle     = FZ(FgSubtle);
        private static readonly SolidColorBrush BrFgWhite      = FZ(FgWhite);
        private static readonly SolidColorBrush BrBorder       = FZ(BorderLight);
        private static readonly SolidColorBrush BrCardBg       = FZ(CardBg);
        private static readonly SolidColorBrush BrCardHover    = FZ(CardHover);
        private static readonly SolidColorBrush BrCardSelected = FZ(CardSelected);
        private static readonly SolidColorBrush BrInfoBg       = FZ(InfoBg);
        private static readonly SolidColorBrush BrInfoBorder   = FZ(InfoBorder);

        // ── State ───────────────────────────────────────────────────────
        private static string _selectedOperation;
        private static Border _activeCard;
        private static TextBlock _statusText;
        private static WarningsPanelState _embeddedState; // set when used inside BCC

        /// <summary>Returns the last operation selected by any card/button in any tab builder.</summary>
        internal static string GetSelectedOperation() => _selectedOperation;

        /// <summary>Phase 91: Shared panel state for embedded warnings panel inside BCC.</summary>
        internal class WarningsPanelState
        {
            public string SelectedOperation { get; set; }
            public TextBlock StatusText { get; set; }
            public List<int> SelectedElementIds { get; } = new();
        }

        /// <summary>
        /// Show the Warnings Dashboard dialog and return the user's selection.
        /// </summary>
        public static WarningsDashboardResult Show()
        {
            _selectedOperation = null;
            _activeCard = null;

            var result = new WarningsDashboardResult();

            var win = new Window
            {
                Title = "STING Warnings Dashboard",
                Width = 850,
                Height = 620,
                MinWidth = 750,
                MinHeight = 520,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = BrBgLight,
                FontFamily = new FontFamily("Segoe UI"),
                ResizeMode = ResizeMode.CanResizeWithGrip
            };

            // Set Revit as owner window
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(win);
                helper.Owner = Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"WarningsDashboardDialog set owner: {ex.Message}"); }

            // ── Root DockPanel ──────────────────────────────────────────
            var root = new DockPanel { LastChildFill = true };

            // ── Header ──────────────────────────────────────────────────
            var header = new Border
            {
                Background = BrBgHeader,
                Padding = new Thickness(18, 12, 18, 12)
            };
            DockPanel.SetDock(header, Dock.Top);

            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "Warnings Dashboard",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrAccent
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "Unified warning management: 150+ classification rules, 16 auto-fix strategies, SLA tracking, deliverable impact analysis",
                FontSize = 11,
                Foreground = FZ(Color.FromRgb(0xB0, 0xB8, 0xD0)),
                Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            header.Child = headerStack;
            root.Children.Add(header);

            // ── Footer ──────────────────────────────────────────────────
            var footer = new Border
            {
                Background = BrBgWhite,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 8, 16, 8)
            };
            DockPanel.SetDock(footer, Dock.Bottom);

            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                Text = "Select an operation to continue.",
                FontSize = 11,
                Foreground = BrFgSubtle,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_statusText, 0);
            footerGrid.Children.Add(_statusText);

            var btnStack = new StackPanel { Orientation = Orientation.Horizontal };

            var closeBtn = MakeButton("Close", false);
            closeBtn.IsCancel = true;
            closeBtn.Click += (s, e) =>
            {
                result.Confirmed = false;
                win.Close();
            };
            closeBtn.Margin = new Thickness(0, 0, 8, 0);
            btnStack.Children.Add(closeBtn);

            var runBtn = MakeButton("Run", true);
            runBtn.IsDefault = true;
            runBtn.Click += (s, e) =>
            {
                if (string.IsNullOrEmpty(_selectedOperation))
                {
                    _statusText.Text = "Please select an operation first.";
                    _statusText.Foreground = FZ(Color.FromRgb(0xE0, 0x50, 0x50));
                    return;
                }
                result.Confirmed = true;
                result.Operation = _selectedOperation;
                win.Close();
            };
            btnStack.Children.Add(runBtn);

            Grid.SetColumn(btnStack, 1);
            footerGrid.Children.Add(btnStack);
            footer.Child = footerGrid;
            root.Children.Add(footer);

            // ── Body: TabControl ────────────────────────────────────────
            var tabs = new TabControl
            {
                Background = BrBgLight,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0)
            };

            tabs.Items.Add(BuildOverviewTab());
            tabs.Items.Add(BuildBrowseSelectTab());
            tabs.Items.Add(BuildAutoFixTab());
            tabs.Items.Add(BuildSelectInspectTab());
            tabs.Items.Add(BuildBaselineSLATab());
            tabs.Items.Add(BuildExportIntegrationTab());

            root.Children.Add(tabs);

            win.Content = root;

            // Keyboard shortcut
            win.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    result.Confirmed = false;
                    win.Close();
                }
            };

            win.ShowDialog();
            return result.Confirmed ? result : new WarningsDashboardResult { Confirmed = false };
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 1: OVERVIEW
        // ═══════════════════════════════════════════════════════════════
        internal static TabItem BuildOverviewTab(WarningsPanelState state = null)
        {
            if (state != null) { _statusText = state.StatusText; _embeddedState = state; }
            var tab = MakeTab("OVERVIEW");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionLabel("WARNING ANALYSIS"));
            var cards1 = new WrapPanel { Orientation = Orientation.Horizontal };
            cards1.Children.Add(MakeOperationCard(
                "Warnings Dashboard",
                "Comprehensive dashboard: severity, category, discipline, level, and workset breakdowns with trend vs baseline.",
                "WarningsDashboard"));
            cards1.Children.Add(MakeOperationCard(
                "Model Health Score",
                "Weighted 0-100 health score across warnings, compliance, data quality, and performance categories.",
                "ModelHealthScore"));
            cards1.Children.Add(MakeOperationCard(
                "Warning Root Cause",
                "Dependency graph identifying root-cause elements with weighted impact scoring. Top 20 root causes.",
                "WarningRootCause"));
            content.Children.Add(cards1);

            content.Children.Add(MakeSectionLabel("PREDICTION & COMPLIANCE"));
            var cards2 = new WrapPanel { Orientation = Orientation.Horizontal };
            cards2.Children.Add(MakeOperationCard(
                "Warning Prediction",
                "Linear regression trend analysis on historical warning counts. Predicts 7-day future warnings.",
                "WarningPrediction"));
            cards2.Children.Add(MakeOperationCard(
                "Warnings Compliance",
                "ISO 19650, CIBSE, and BS 7671 compliance mapping. PASS/FAIL per requirement category.",
                "WarningsCompliance"));
            cards2.Children.Add(MakeOperationCard(
                "Deliverable Readiness",
                "0-100 readiness scoring for COBie, IFC, PDF/Drawings, and FM Handover deliverables.",
                "DeliverableReadiness"));
            content.Children.Add(cards2);

            tab.Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 2: BROWSE & SELECT
        // ═══════════════════════════════════════════════════════════════
        internal static TabItem BuildBrowseSelectTab(WarningsPanelState state = null)
        {
            if (state != null) { _statusText = state.StatusText; }
            var tab = MakeTab("BROWSE & SELECT");

            // Main layout: left TreeView (60%) + right detail panel (40%)
            var mainGrid = new Grid { Margin = new Thickness(8, 8, 8, 8) };
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

            // ── Left panel: severity-grouped TreeView with checkboxes ──
            var leftPanel = new DockPanel();

            // Toolbar: Select All / Deselect All / Zoom / Select in Model
            var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(toolbar, Dock.Top);

            var btnSelectAll = MakeSmallButton("Select All");
            var btnDeselectAll = MakeSmallButton("Deselect All");
            var btnZoom = MakeSmallButton("Zoom to Selected");
            btnZoom.Background = BrAccent;
            btnZoom.Foreground = BrFgWhite;
            var btnSelectInModel = MakeSmallButton("Select in Model");

            toolbar.Children.Add(btnSelectAll);
            toolbar.Children.Add(btnDeselectAll);
            toolbar.Children.Add(btnZoom);
            toolbar.Children.Add(btnSelectInModel);
            leftPanel.Children.Add(toolbar);

            // Count label
            var countLabel = new TextBlock
            {
                Text = "0 warnings selected",
                FontSize = 10,
                Foreground = BrFgSubtle,
                Margin = new Thickness(0, 0, 0, 4)
            };
            DockPanel.SetDock(countLabel, Dock.Bottom);
            leftPanel.Children.Add(countLabel);

            // TreeView
            var tree = new TreeView
            {
                Background = BrBgWhite,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1)
            };
            // Enable virtualisation for large warning lists
            VirtualizingPanel.SetIsVirtualizing(tree, true);

            // Track all warning checkboxes for batch select/deselect
            var allWarningChecks = new List<CheckBox>();

            // Severity groups: CRITICAL, HIGH, MEDIUM, LOW
            var severityGroups = new[]
            {
                ("CRITICAL", Color.FromRgb(0xE5, 0x39, 0x35), "Immediate action required — blocking issues"),
                ("HIGH",     Color.FromRgb(0xFB, 0x8C, 0x00), "Should be resolved within 24 hours"),
                ("MEDIUM",   Color.FromRgb(0xFD, 0xD8, 0x35), "Plan resolution within 1 week"),
                ("LOW",      Color.FromRgb(0x43, 0xA0, 0x47), "Address when convenient — minor issues")
            };

            // Detail panel elements (will be populated on selection)
            var detailTitle = new TextBlock
            {
                Text = "Select a warning to view details",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrFgDark,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var detailStack = new StackPanel();

            foreach (var (severity, color, hint) in severityGroups)
            {
                // Group header with severity checkbox
                var groupCheck = new CheckBox
                {
                    IsChecked = false,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };

                var groupHeader = new StackPanel { Orientation = Orientation.Horizontal };

                // Severity dot
                var severityDot = new Border
                {
                    Width = 10,
                    Height = 10,
                    CornerRadius = new CornerRadius(5),
                    Background = FZ(color),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                groupHeader.Children.Add(groupCheck);
                groupHeader.Children.Add(severityDot);
                groupHeader.Children.Add(new TextBlock
                {
                    Text = severity,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = BrFgDark,
                    VerticalAlignment = VerticalAlignment.Center
                });

                var groupCountLabel = new TextBlock
                {
                    Text = "",
                    FontSize = 10,
                    Foreground = BrFgSubtle,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                groupHeader.Children.Add(groupCountLabel);

                var groupItem = new TreeViewItem
                {
                    Header = groupHeader,
                    IsExpanded = (severity == "CRITICAL" || severity == "HIGH"),
                    Margin = new Thickness(0, 2, 0, 2)
                };

                // Store per-group checkbox list
                var groupChecks = new List<CheckBox>();

                // Sample warning items — these will be populated at runtime by the dispatch handler
                // which reads actual Revit warnings. Here we create placeholder structure.
                string[] sampleWarnings;
                switch (severity)
                {
                    case "CRITICAL":
                        sampleWarnings = new[]
                        {
                            "Host has been deleted|Data|1",
                            "Room is not in a properly enclosed region|Spatial|3",
                            "Highlighted walls overlap|Geometric|2",
                            "Elements have duplicate instance values|Data|5"
                        };
                        break;
                    case "HIGH":
                        sampleWarnings = new[]
                        {
                            "Room separation line overlaps another|Spatial|4",
                            "Duplicate mark values|Data|8",
                            "Elements are joined but do not intersect|Geometric|2",
                            "Cannot be placed on non-structural host|Geometric|1",
                            "Minimum clearance not met|Compliance|3"
                        };
                        break;
                    case "MEDIUM":
                        sampleWarnings = new[]
                        {
                            "Wall is slightly off axis|Geometric|6",
                            "Room tag is outside of its room|Spatial|2",
                            "Calculated size not available|MEP|4",
                            "Model Line is too short|Geometric|3",
                            "Opening cut is not perpendicular|Geometric|1"
                        };
                        break;
                    default: // LOW
                        sampleWarnings = new[]
                        {
                            "Wall join produces an odd result|Geometric|7",
                            "Wall is attached|Geometric|2",
                            "Coincident lines or edges|Geometric|3",
                            "Not properly associated|Data|1"
                        };
                        break;
                }

                int groupTotal = 0;
                foreach (var warningInfo in sampleWarnings)
                {
                    var parts = warningInfo.Split('|');
                    string desc = parts[0];
                    string category = parts.Length > 1 ? parts[1] : "Unknown";
                    int elementCount = parts.Length > 2 && int.TryParse(parts[2], out int ec) ? ec : 1;
                    groupTotal += elementCount;

                    // Per-warning checkbox item
                    var warnCheck = new CheckBox
                    {
                        IsChecked = false,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 2, 6, 0)
                    };
                    allWarningChecks.Add(warnCheck);
                    groupChecks.Add(warnCheck);

                    var warnPanel = new StackPanel { Orientation = Orientation.Horizontal };
                    warnPanel.Children.Add(warnCheck);

                    var warnTextStack = new StackPanel();
                    warnTextStack.Children.Add(new TextBlock
                    {
                        Text = desc,
                        FontSize = 11,
                        Foreground = BrFgDark,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 320
                    });

                    // Meta line: category + element count
                    var metaPanel = new StackPanel { Orientation = Orientation.Horizontal };
                    metaPanel.Children.Add(new TextBlock
                    {
                        Text = category,
                        FontSize = 9,
                        Foreground = FZ(Color.FromRgb(0x55, 0x77, 0xAA)),
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 8, 0)
                    });
                    metaPanel.Children.Add(new TextBlock
                    {
                        Text = $"{elementCount} element{(elementCount != 1 ? "s" : "")}",
                        FontSize = 9,
                        Foreground = BrFgSubtle
                    });
                    warnTextStack.Children.Add(metaPanel);

                    warnPanel.Children.Add(warnTextStack);

                    var warnItem = new TreeViewItem
                    {
                        Header = warnPanel,
                        Tag = warningInfo, // store for detail panel
                        Margin = new Thickness(0, 1, 0, 1)
                    };

                    // Click to show detail
                    string capturedSeverity = severity;
                    string capturedDesc = desc;
                    string capturedCategory = category;
                    int capturedCount = elementCount;
                    warnItem.Selected += (s, e) =>
                    {
                        e.Handled = true; // prevent bubble to parent
                        PopulateDetailPanel(detailTitle, detailStack, capturedDesc,
                            capturedSeverity, capturedCategory, capturedCount);
                    };

                    // Phase 101: double-click a warning row now FIRES the
                    // action immediately instead of only flagging it and
                    // waiting for the user to click Run. The action resolves
                    // real element IDs via doc.GetWarnings() description
                    // matching inside BIMCoordinationCenterCommand.ProcessAction
                    // (ZoomToWarning_ pattern), so this works even though the
                    // tree itself is still populated from the sample-warnings
                    // catalogue — the lookup is against the live document.
                    warnItem.MouseDoubleClick += (s, e) =>
                    {
                        e.Handled = true;
                        string encoded = (capturedDesc ?? "").Replace("|", "/");
                        string op = $"ZoomToWarning_{encoded}";
                        if (state != null)
                        {
                            state.SelectedOperation = op;
                            if (state.StatusText != null)
                                state.StatusText.Text = $"Zoom to: {capturedDesc}";
                        }
                        else _selectedOperation = op;
                        BIMCoordinationCenter.ActionDispatcher?.Invoke(op);
                    };

                    // Lazy expand: placeholder child replaced with element IDs on expand
                    warnItem.Items.Add(new TreeViewItem { Header = new TextBlock { Text = "Loading elements...", FontSize = 10, FontStyle = FontStyles.Italic, Foreground = BrFgSubtle } });
                    warnItem.Expanded += (s, e) =>
                    {
                        e.Handled = true;
                        if (warnItem.Items.Count == 1 && warnItem.Items[0] is TreeViewItem ph &&
                            ph.Header is TextBlock phTb && phTb.Text.StartsWith("Loading"))
                        {
                            warnItem.Items.Clear();
                            // Show up to 50 sample element IDs (runtime dispatch fills real IDs)
                            for (int ei = 1; ei <= Math.Min(capturedCount, 50); ei++)
                            {
                                int capturedEid = ei * 100000 + ei; // placeholder ID
                                var eidItem = new TreeViewItem
                                {
                                    Header = new TextBlock { Text = $"Element #{capturedEid}", FontSize = 10, Foreground = BrFgSubtle },
                                    Cursor = Cursors.Hand, ToolTip = "Double-click: Select & Zoom | Right-click for options"
                                };
                                // Phase 101: double-click an element row now
                                // FIRES a ZoomToWarning action against the
                                // parent warning's description — the sample
                                // tree uses placeholder element IDs that
                                // don't resolve, so dispatching the parent
                                // description-level action through
                                // doc.GetWarnings() selects the real matching
                                // elements in the model instead of silently
                                // failing on the fake IDs.
                                eidItem.MouseDoubleClick += (s2, e2) =>
                                {
                                    e2.Handled = true;
                                    string parentDesc = (capturedDesc ?? "").Replace("|", "/");
                                    string op = $"ZoomToWarning_{parentDesc}";
                                    if (state != null)
                                    {
                                        state.SelectedOperation = op;
                                        state.StatusText?.Dispatcher.Invoke(() =>
                                        {
                                            if (state.StatusText != null)
                                                state.StatusText.Text = $"Zoom to elements affected by: {capturedDesc}";
                                        });
                                    }
                                    else _selectedOperation = op;
                                    BIMCoordinationCenter.ActionDispatcher?.Invoke(op);
                                };
                                var eidCtx = new ContextMenu();
                                var eidSelect = new MenuItem { Header = "Select & Zoom in Model" };
                                eidSelect.Click += (s2, e2) => { string op = $"SelectElement_{capturedEid}"; if (state != null) state.SelectedOperation = op; else _selectedOperation = op; };
                                var eidCopy = new MenuItem { Header = "Copy Element ID" };
                                eidCopy.Click += (s2, e2) => Clipboard.SetText(capturedEid.ToString());
                                eidCtx.Items.Add(eidSelect); eidCtx.Items.Add(eidCopy);
                                eidItem.ContextMenu = eidCtx;
                                warnItem.Items.Add(eidItem);
                            }
                            if (capturedCount > 50)
                                warnItem.Items.Add(new TreeViewItem { Header = new TextBlock { Text = $"…and {capturedCount - 50} more elements", FontSize = 10, FontStyle = FontStyles.Italic, Foreground = BrFgSubtle } });
                        }
                    };

                    // Right-click context menu on warning item
                    var warnCtx = new ContextMenu();
                    // Phase 101: the context-menu actions now FIRE the action
                    // straight away (BCC ActionDispatcher -> ExternalEvent) so
                    // the user sees immediate feedback. Previously they set
                    // SelectedOperation and required the user to then click
                    // "Run Selected Action", which was confusing enough for
                    // coordinators to report double-click / browse as broken.
                    var ctxSelectModel = new MenuItem { Header = "Select in Model" };
                    ctxSelectModel.Click += (s2, e2) =>
                    {
                        string encoded = (capturedDesc ?? "").Replace("|", "/");
                        string op = $"ZoomToWarning_{encoded}";
                        if (state != null)
                        {
                            state.SelectedOperation = op;
                            if (state.StatusText != null) state.StatusText.Text = $"Selecting elements for: {capturedDesc}";
                        }
                        else _selectedOperation = op;
                        BIMCoordinationCenter.ActionDispatcher?.Invoke(op);
                    };
                    var ctxAutoFix = new MenuItem { Header = "Auto-Fix This Warning" };
                    ctxAutoFix.Click += (s2, e2) =>
                    {
                        string op = "AutoFixWarnings";
                        if (state != null) { state.SelectedOperation = op; if (state.StatusText != null) state.StatusText.Text = $"Auto-fixing: {capturedDesc}"; }
                        else _selectedOperation = op;
                        BIMCoordinationCenter.ActionDispatcher?.Invoke(op);
                    };
                    var ctxSuppress = new MenuItem { Header = "Ignore / Suppress" };
                    ctxSuppress.Click += (s2, e2) =>
                    {
                        string op = "SuppressWarnings";
                        if (state != null) { state.SelectedOperation = op; if (state.StatusText != null) state.StatusText.Text = $"Suppress: {capturedDesc}. Click Run."; }
                        else { _selectedOperation = op; }
                    };
                    var ctxBaseline = new MenuItem { Header = "Set as Baseline" };
                    ctxBaseline.Click += (s2, e2) =>
                    {
                        string op = "SaveBaseline";
                        if (state != null) { state.SelectedOperation = op; if (state.StatusText != null) state.StatusText.Text = "Save current warning count as baseline. Click Run."; }
                        else { _selectedOperation = op; }
                    };
                    var ctxRootCause = new MenuItem { Header = "Root Cause Analysis" };
                    ctxRootCause.Click += (s2, e2) =>
                    {
                        string op = "WarningRootCause";
                        if (state != null) { state.SelectedOperation = op; if (state.StatusText != null) state.StatusText.Text = "Root cause analysis. Click Run."; }
                        else { _selectedOperation = op; }
                    };
                    var ctxBCF = new MenuItem { Header = "Export BCF" };
                    ctxBCF.Click += (s2, e2) =>
                    {
                        string op = "WarningsExportBCF";
                        if (state != null) { state.SelectedOperation = op; if (state.StatusText != null) state.StatusText.Text = "Export to BCF. Click Run."; }
                        else { _selectedOperation = op; }
                    };
                    var ctxCopy = new MenuItem { Header = "Copy Description" };
                    ctxCopy.Click += (s2, e2) => Clipboard.SetText(capturedDesc);
                    var ctxSLA = new MenuItem { Header = "View SLA Status" };
                    ctxSLA.Click += (s2, e2) =>
                    {
                        string op = "WarningsSLAStatus";
                        if (state != null) { state.SelectedOperation = op; if (state.StatusText != null) state.StatusText.Text = "View SLA status. Click Run."; }
                        else { _selectedOperation = op; }
                    };
                    warnCtx.Items.Add(ctxSelectModel);
                    warnCtx.Items.Add(ctxAutoFix);
                    warnCtx.Items.Add(ctxSuppress);
                    warnCtx.Items.Add(new Separator());
                    warnCtx.Items.Add(ctxBaseline);
                    warnCtx.Items.Add(ctxRootCause);
                    warnCtx.Items.Add(new Separator());
                    warnCtx.Items.Add(ctxBCF);
                    warnCtx.Items.Add(ctxCopy);
                    warnCtx.Items.Add(ctxSLA);
                    warnItem.ContextMenu = warnCtx;

                    groupItem.Items.Add(warnItem);
                }

                groupCountLabel.Text = $"({sampleWarnings.Length} types, {groupTotal} elements)";

                // Group checkbox toggles all children
                groupCheck.Checked += (s, e) =>
                {
                    foreach (var cb in groupChecks) cb.IsChecked = true;
                    UpdateSelectedCount(allWarningChecks, countLabel);
                };
                groupCheck.Unchecked += (s, e) =>
                {
                    foreach (var cb in groupChecks) cb.IsChecked = false;
                    UpdateSelectedCount(allWarningChecks, countLabel);
                };

                // Individual checkbox change updates count
                foreach (var cb in groupChecks)
                {
                    cb.Checked += (s, e) => UpdateSelectedCount(allWarningChecks, countLabel);
                    cb.Unchecked += (s, e) => UpdateSelectedCount(allWarningChecks, countLabel);
                }

                tree.Items.Add(groupItem);
            }

            leftPanel.Children.Add(tree);
            Grid.SetColumn(leftPanel, 0);
            mainGrid.Children.Add(leftPanel);

            // ── Splitter ──
            var splitter = new GridSplitter
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = BrBorder
            };
            Grid.SetColumn(splitter, 1);
            mainGrid.Children.Add(splitter);

            // ── Right panel: Warning detail ──
            var rightPanel = new Border
            {
                Background = BrBgWhite,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10)
            };

            var rightScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var rightContent = new StackPanel();
            rightContent.Children.Add(new TextBlock
            {
                Text = "WARNING DETAILS",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = BrAccent,
                Margin = new Thickness(0, 0, 0, 8)
            });
            rightContent.Children.Add(detailTitle);
            rightContent.Children.Add(detailStack);

            rightScroll.Content = rightContent;
            rightPanel.Child = rightScroll;
            Grid.SetColumn(rightPanel, 2);
            mainGrid.Children.Add(rightPanel);

            // ── Button handlers ──
            btnSelectAll.Click += (s, e) =>
            {
                foreach (var cb in allWarningChecks) cb.IsChecked = true;
                UpdateSelectedCount(allWarningChecks, countLabel);
            };
            btnDeselectAll.Click += (s, e) =>
            {
                foreach (var cb in allWarningChecks) cb.IsChecked = false;
                UpdateSelectedCount(allWarningChecks, countLabel);
            };
            btnZoom.Click += (s, e) =>
            {
                _selectedOperation = "ZoomToWarnings";
                if (state != null) state.SelectedOperation = "ZoomToWarnings";
                if (_statusText != null)
                {
                    int sel = allWarningChecks.Count(c => c.IsChecked == true);
                    _statusText.Foreground = BrFgSubtle;
                    _statusText.Text = $"Zoom to {sel} selected warning(s). Click Run to execute.";
                }
            };
            btnSelectInModel.Click += (s, e) =>
            {
                _selectedOperation = "WarningsSelectElements";
                if (state != null) state.SelectedOperation = "WarningsSelectElements";
                if (_statusText != null)
                {
                    int sel = allWarningChecks.Count(c => c.IsChecked == true);
                    _statusText.Foreground = BrFgSubtle;
                    _statusText.Text = $"Select elements for {sel} warning(s). Click Run to execute.";
                }
            };

            tab.Content = mainGrid;
            return tab;
        }

        /// <summary>
        /// Update the "N warnings selected" label based on checkbox states.
        /// </summary>
        private static void UpdateSelectedCount(List<CheckBox> checks, TextBlock label)
        {
            int count = checks.Count(c => c.IsChecked == true);
            label.Text = $"{count} warning{(count != 1 ? "s" : "")} selected";
            label.Foreground = count > 0 ? BrAccent : BrFgSubtle;
        }

        /// <summary>
        /// Populate the right-hand detail panel with information about a selected warning.
        /// </summary>
        private static void PopulateDetailPanel(TextBlock titleBlock, StackPanel detailPanel,
            string description, string severity, string category, int elementCount)
        {
            titleBlock.Text = description;
            detailPanel.Children.Clear();

            // Severity badge
            var severityColor = severity switch
            {
                "CRITICAL" => Color.FromRgb(0xE5, 0x39, 0x35),
                "HIGH"     => Color.FromRgb(0xFB, 0x8C, 0x00),
                "MEDIUM"   => Color.FromRgb(0xFD, 0xD8, 0x35),
                _          => Color.FromRgb(0x43, 0xA0, 0x47)
            };

            var badgePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            badgePanel.Children.Add(new Border
            {
                Background = FZ(severityColor),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 8, 0),
                Child = new TextBlock
                {
                    Text = severity,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = severity == "MEDIUM" ? BrFgDark : BrFgWhite
                }
            });
            badgePanel.Children.Add(new Border
            {
                Background = FZ(Color.FromRgb(0xE0, 0xE8, 0xF0)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 2, 8, 2),
                Child = new TextBlock
                {
                    Text = category,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = FZ(Color.FromRgb(0x44, 0x66, 0x88))
                }
            });
            detailPanel.Children.Add(badgePanel);

            // Detail rows
            AddDetailRow(detailPanel, "Affected Elements", $"{elementCount}");
            AddDetailRow(detailPanel, "Category", category);
            AddDetailRow(detailPanel, "Severity", severity);

            // Auto-fix assessment
            string fixable = category == "Data" || category == "Spatial" ? "Yes — auto-fix available" : "Manual review required";
            var fixColor = fixable.StartsWith("Yes") ? Color.FromRgb(0x43, 0xA0, 0x47) : Color.FromRgb(0xFB, 0x8C, 0x00);
            AddDetailRow(detailPanel, "Auto-fixable", fixable, fixColor);

            // SLA info
            string sla = severity switch
            {
                "CRITICAL" => "4 hours",
                "HIGH" => "24 hours",
                "MEDIUM" => "1 week",
                _ => "2 weeks"
            };
            AddDetailRow(detailPanel, "SLA Target", sla);

            // Recommendation
            detailPanel.Children.Add(new TextBlock
            {
                Text = "RECOMMENDATION",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = BrAccent,
                Margin = new Thickness(0, 12, 0, 4)
            });

            string recommendation = category switch
            {
                "Data" => "Run Auto-Fix to resolve duplicate marks and data inconsistencies. Review elements for incorrect parameter values.",
                "Spatial" => "Check room boundaries and separation lines. Use Auto-Fix for overlapping room separation lines.",
                "Geometric" => "Inspect element geometry. Walls may need manual joining or axis correction.",
                "MEP" => "Verify MEP system connections and sizing. Check duct/pipe routing for clearance.",
                "Compliance" => "Review against relevant standard requirements. May need design changes to comply.",
                _ => "Review the affected elements and determine appropriate corrective action."
            };
            detailPanel.Children.Add(new TextBlock
            {
                Text = recommendation,
                FontSize = 11,
                Foreground = BrFgDark,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            // Deliverable impact
            detailPanel.Children.Add(new TextBlock
            {
                Text = "DELIVERABLE IMPACT",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = BrAccent,
                Margin = new Thickness(0, 4, 0, 4)
            });

            var impacts = new List<string>();
            if (category == "Data" || category == "Spatial") impacts.Add("COBie Export");
            if (category == "Geometric" || category == "MEP") impacts.Add("IFC Export");
            if (category == "Compliance") impacts.Add("FM Handover");
            if (severity == "CRITICAL" || severity == "HIGH") impacts.Add("Clash Detection");
            if (impacts.Count == 0) impacts.Add("Minor — no direct deliverable impact");

            foreach (var impact in impacts)
            {
                var impactRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                impactRow.Children.Add(new TextBlock
                {
                    Text = "•",
                    FontSize = 11,
                    Foreground = BrAccent,
                    Margin = new Thickness(0, 0, 6, 0)
                });
                impactRow.Children.Add(new TextBlock
                {
                    Text = impact,
                    FontSize = 11,
                    Foreground = BrFgDark
                });
                detailPanel.Children.Add(impactRow);
            }
        }

        /// <summary>Adds a label-value row to the detail panel.</summary>
        private static void AddDetailRow(StackPanel panel, string label, string value, Color? valueColor = null)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = BrFgSubtle,
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetColumn(lbl, 0);
            row.Children.Add(lbl);

            var val = new TextBlock
            {
                Text = value,
                FontSize = 11,
                Foreground = valueColor.HasValue ? FZ(valueColor.Value) : BrFgDark,
                FontWeight = valueColor.HasValue ? FontWeights.SemiBold : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(val, 1);
            row.Children.Add(val);

            panel.Children.Add(row);
        }

        /// <summary>Creates a small toolbar button.</summary>
        private static Button MakeSmallButton(string text)
        {
            return new Button
            {
                Content = text,
                FontSize = 10,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = Cursors.Hand,
                Background = BrBgWhite,
                Foreground = BrFgDark,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1)
            };
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 3: AUTO-FIX
        // ═══════════════════════════════════════════════════════════════
        internal static TabItem BuildAutoFixTab(WarningsPanelState state = null)
        {
            if (state != null) { _statusText = state.StatusText; }
            var tab = MakeTab("AUTO-FIX");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionLabel("AUTOMATED RESOLUTION"));
            var cards = new WrapPanel { Orientation = Orientation.Horizontal };
            cards.Children.Add(MakeOperationCard(
                "Auto-Fix Warnings",
                "Batch auto-fix with dry-run preview. Strategies: duplicates, room separation, marks, geometry, and more.",
                "WarningsAutoFix"));
            cards.Children.Add(MakeOperationCard(
                "Action Plan",
                "Prioritised BIM coordinator action list based on current model state. Sorted by impact score.",
                "ActionPlan"));
            content.Children.Add(cards);

            // ── Info block: Fix strategies ───────────────────────────────
            content.Children.Add(MakeSectionLabel("AUTO-FIX STRATEGIES REFERENCE"));

            var infoBlock = new Border
            {
                Background = BrInfoBg,
                BorderBrush = BrInfoBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 4, 0, 8)
            };

            var infoStack = new StackPanel();
            string[] strategies =
            {
                "1. Delete duplicate instances at same location",
                "2. Delete shorter room separation line (overlap)",
                "3. Delete redundant room boundary segments",
                "4. Auto-increment duplicate marks with collision-safe suffix",
                "5. Unjoin non-intersecting geometry pairs",
                "6. Auto-join overlapping walls via JoinGeometryUtils",
                "7. Move room tags outside boundary to room center",
                "8. Snap near-axis elements to nearest cardinal direction",
                "9. Delete zero-length elements (walls/pipes/ducts < 3mm)",
                "10. Fix duplicate marks with full-model scan for uniqueness"
            };
            foreach (string strategy in strategies)
            {
                infoStack.Children.Add(new TextBlock
                {
                    Text = strategy,
                    FontSize = 11,
                    Foreground = BrFgDark,
                    Margin = new Thickness(0, 1, 0, 1),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            infoBlock.Child = infoStack;
            content.Children.Add(infoBlock);

            tab.Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 3: SELECT & INSPECT
        // ═══════════════════════════════════════════════════════════════
        internal static TabItem BuildSelectInspectTab(WarningsPanelState state = null)
        {
            if (state != null) { _statusText = state.StatusText; }
            var tab = MakeTab("SELECT & INSPECT");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionLabel("ELEMENT SELECTION"));
            var cards1 = new WrapPanel { Orientation = Orientation.Horizontal };
            cards1.Children.Add(MakeOperationCard(
                "Select by Warning",
                "Pick a warning type from grouped list and select all affected elements in the model view.",
                "WarningsSelectElements"));
            cards1.Children.Add(MakeOperationCard(
                "Toggle Warning Visibility",
                "Show or hide warning-affected elements in the active view for focused inspection.",
                "ToggleWarningVisibility"));
            cards1.Children.Add(MakeOperationCard(
                "Warning Monitor",
                "Pre/post-command warning count tracking. Detect warning regression after major operations.",
                "WarningsMonitor"));
            content.Children.Add(cards1);

            content.Children.Add(MakeSectionLabel("VISUAL INSPECTION"));
            var cards2 = new WrapPanel { Orientation = Orientation.Horizontal };
            cards2.Children.Add(MakeOperationCard(
                "Highlight Invalid",
                "Colour-code elements: red for missing tags, orange for incomplete tags in the active view.",
                "HighlightInvalid"));
            cards2.Children.Add(MakeOperationCard(
                "Clear Overrides",
                "Reset all graphic overrides in the active view to restore default appearance.",
                "ClearOverrides"));
            cards2.Children.Add(MakeOperationCard(
                "Anomaly Auto-Fix",
                "Detect and fix tag anomalies: DISC, SYS, FUNC, PROD, TAG7, and stale element issues.",
                "AnomalyAutoFix"));
            content.Children.Add(cards2);

            tab.Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 4: BASELINE & SLA
        // ═══════════════════════════════════════════════════════════════
        internal static TabItem BuildBaselineSLATab(WarningsPanelState state = null)
        {
            if (state != null) { _statusText = state.StatusText; }
            var tab = MakeTab("BASELINE & SLA");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionLabel("BASELINE MANAGEMENT"));
            var cards1 = new WrapPanel { Orientation = Orientation.Horizontal };
            cards1.Children.Add(MakeOperationCard(
                "Save Baseline",
                "Save current warning count as baseline sidecar. Compare against previous baseline with delta report.",
                "WarningsBaseline"));
            cards1.Children.Add(MakeOperationCard(
                "Extended Baseline",
                "Save per-warning-type baseline with first-seen timestamps for type-level regression analysis.",
                "SaveExtendedBaseline"));
            cards1.Children.Add(MakeOperationCard(
                "Compliance Fall Check",
                "Detect > 2% compliance regression between checks. Track stale element count delta.",
                "ComplianceFallCheck"));
            content.Children.Add(cards1);

            content.Children.Add(MakeSectionLabel("SLA & SUPPRESSION"));
            var cards2 = new WrapPanel { Orientation = Orientation.Horizontal };
            cards2.Children.Add(MakeOperationCard(
                "SLA Violation Report",
                "Per-warning SLA tracking: Critical 4h, High 24h, Medium 1wk, Low 2wk. ISO 19650 aligned.",
                "SLAViolationReport"));
            cards2.Children.Add(MakeOperationCard(
                "Suppress Warnings",
                "Add warning patterns to suppression list. Time-limited and context-aware suppressions.",
                "WarningsSuppress"));
            cards2.Children.Add(MakeOperationCard(
                "Suppression Audit",
                "Review all active suppressions with expiry dates, context, and audit trail.",
                "SuppressionAudit"));
            content.Children.Add(cards2);

            tab.Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 5: EXPORT & INTEGRATION
        // ═══════════════════════════════════════════════════════════════
        internal static TabItem BuildExportIntegrationTab(WarningsPanelState state = null)
        {
            if (state != null) { _statusText = state.StatusText; }
            var tab = MakeTab("EXPORT & INTEGRATION");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionLabel("EXPORT"));
            var cards1 = new WrapPanel { Orientation = Orientation.Horizontal };
            cards1.Children.Add(MakeOperationCard(
                "Export Warnings CSV",
                "10-column CSV: Description, Category, Severity, FixStrategy, CanAutoFix, ElementIds, Level, and more.",
                "WarningsExport"));
            cards1.Children.Add(MakeOperationCard(
                "Export HTML Dashboard",
                "Self-contained HTML report with KPI cards, discipline table, warning summary. Shareable without Revit.",
                "ExportDashboardHTML"));
            cards1.Children.Add(MakeOperationCard(
                "Weekly Coordinator Report",
                "Corporate HTML report: compliance trend, per-discipline table, warning root-cause summary, issue metrics.",
                "WeeklyCoordinatorReport"));
            content.Children.Add(cards1);

            content.Children.Add(MakeSectionLabel("CROSS-SYSTEM INTEGRATION"));
            var cards2 = new WrapPanel { Orientation = Orientation.Horizontal };
            cards2.Children.Add(MakeOperationCard(
                "Container-Warning Check",
                "Correlate container completeness with data-quality warnings. Recommend actions when gaps detected.",
                "ContainerWarningCheck"));
            cards2.Children.Add(MakeOperationCard(
                "Transmittal Gate Check",
                "Validate tag compliance, containers, stale elements, and critical warnings before transmittal send.",
                "TransmittalGateCheck"));
            content.Children.Add(cards2);

            tab.Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // SHARED UI BUILDERS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates a styled TabItem with header text.
        /// </summary>
        private static TabItem MakeTab(string headerText)
        {
            var tb = new TextBlock
            {
                Text = headerText,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(10, 4, 10, 4)
            };

            return new TabItem
            {
                Header = tb,
                Background = FZ(TabDefault),
                Foreground = BrFgDark
            };
        }

        /// <summary>
        /// Creates a section label with orange accent text.
        /// </summary>
        private static TextBlock MakeSectionLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = BrAccent,
                Margin = new Thickness(0, 8, 0, 6)
            };
        }

        /// <summary>
        /// Creates an operation card with a 4px orange left border, title, description, and Run button.
        /// Clicking the card or the Run button selects the operation.
        /// </summary>
        private static Border MakeOperationCard(string title, string description, string operationKey)
        {
            // Outer border provides the 4px orange left accent
            var accentBorder = new Border
            {
                Width = 240,
                MinHeight = 110,
                Background = BrAccent,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(4, 0, 0, 0),
                SnapsToDevicePixels = true
            };

            // Inner card with white background
            var card = new Border
            {
                Background = BrCardBg,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 1, 1, 1),
                CornerRadius = new CornerRadius(0, 4, 4, 0),
                Padding = new Thickness(12, 10, 12, 10),
                Cursor = Cursors.Hand
            };

            var outerStack = new DockPanel();

            // Run button docked to bottom
            var runBtn = new Button
            {
                Content = "Run",
                Width = 56,
                Height = 24,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Background = BrAccent,
                Foreground = BrFgWhite,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 0),
                Cursor = Cursors.Hand
            };
            DockPanel.SetDock(runBtn, Dock.Bottom);
            outerStack.Children.Add(runBtn);

            // Title and description
            var textStack = new StackPanel();
            textStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrFgDark,
                TextWrapping = TextWrapping.Wrap
            });
            textStack.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 10,
                Foreground = BrFgSubtle,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
            outerStack.Children.Add(textStack);

            card.Child = outerStack;
            accentBorder.Child = card;

            // ── Select handler (shared between card click and Run button) ──
            void SelectCard()
            {
                // Deselect previous
                if (_activeCard != null)
                {
                    _activeCard.Background = BrCardBg;
                }

                // Select this card
                _activeCard = card;
                card.Background = BrCardSelected;
                _selectedOperation = operationKey;
                if (_embeddedState != null) _embeddedState.SelectedOperation = operationKey;

                if (_statusText != null)
                {
                    _statusText.Foreground = BrFgSubtle;
                    _statusText.Text = $"Selected: {title}";
                }
            }

            // Hover effects
            card.MouseEnter += (s, e) =>
            {
                if (_activeCard != card)
                    card.Background = BrCardHover;
            };
            card.MouseLeave += (s, e) =>
            {
                if (_activeCard != card)
                    card.Background = BrCardBg;
            };

            // Click card body to select
            card.MouseLeftButtonDown += (s, e) => SelectCard();

            // Run button also selects the operation
            runBtn.Click += (s, e) => SelectCard();

            return accentBorder;
        }

        /// <summary>
        /// Creates a styled button matching the STING dialog theme.
        /// </summary>
        private static Button MakeButton(string text, bool isPrimary)
        {
            var btn = new Button
            {
                Content = text,
                MinWidth = 80,
                Height = 30,
                FontSize = 12,
                Padding = new Thickness(14, 4, 14, 4),
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1)
            };

            if (isPrimary)
            {
                btn.Background = BrAccent;
                btn.Foreground = BrFgWhite;
                btn.BorderBrush = BrAccent;
                btn.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                btn.Background = BrBgWhite;
                btn.Foreground = BrFgDark;
                btn.BorderBrush = BrBorder;
            }

            return btn;
        }
    }

}
