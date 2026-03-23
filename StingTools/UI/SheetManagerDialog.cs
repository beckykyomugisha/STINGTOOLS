using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.Core;
using StingTools.Select;

namespace StingTools.UI
{
    /// <summary>
    /// Result returned from the SheetManagerDialog.
    /// </summary>
    public class SheetManagerResult
    {
        public bool Confirmed { get; set; }
        public string Operation { get; set; }
        public Dictionary<string, object> Options { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// ISO 19650 compliant dual-panel modeless WPF Sheet Manager.
    /// Left: Views browser (by type/discipline/level/template/scope box).
    /// Right: Sheets browser (by discipline, with viewport children).
    /// Bottom: Layout tools + QA tools toolbar.
    ///
    /// Key features:
    ///   - Ceiling plans distinct color from floor plans + discipline colors
    ///   - View template badges, assignment, and audit
    ///   - Dependent view hierarchy with scope box linking
    ///   - Scope box browser section
    ///   - Title block management (browse, swap, audit)
    ///   - Expand All / Collapse All tree buttons
    ///   - Smart automation: one-click doc package, auto-place, intelligent layout
    ///   - All operations execute live via IExternalEventHandler
    /// </summary>
    internal static class SheetManagerDialog
    {
        // ── Theme colours ───────────────────────────────────────────────
        private static readonly SolidColorBrush BrBgLight = new(Color.FromRgb(0xF5, 0xF5, 0xF5));
        private static readonly SolidColorBrush BrBgWhite = new(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush BrBgHeader = new(Color.FromRgb(0x2D, 0x2D, 0x30));
        private static readonly SolidColorBrush BrAccent = new(Color.FromRgb(0xE8, 0x91, 0x2D));
        private static readonly SolidColorBrush BrFgDark = new(Color.FromRgb(0x22, 0x22, 0x22));
        private static readonly SolidColorBrush BrFgWhite = new(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush BrFgSubtle = new(Color.FromRgb(0x77, 0x77, 0x77));
        private static readonly SolidColorBrush BrBorder = new(Color.FromRgb(0xD0, 0xD0, 0xD0));
        private static readonly SolidColorBrush BrHover = new(Color.FromRgb(0xFD, 0xF0, 0xE0));
        private static readonly SolidColorBrush BrGreen = new(Color.FromRgb(0x4C, 0xAF, 0x50));
        private static readonly SolidColorBrush BrRed = new(Color.FromRgb(0xF4, 0x43, 0x36));
        private static readonly SolidColorBrush BrBlue = new(Color.FromRgb(0x21, 0x96, 0xF3));
        private static readonly SolidColorBrush BrDropHighlight = new(Color.FromRgb(0xC8, 0xE6, 0xC9));
        private static readonly SolidColorBrush BrDropBorder = new(Color.FromRgb(0x4C, 0xAF, 0x50));

        // ── View type colours (Floor vs Ceiling DISTINCT) ──────────────
        private static readonly SolidColorBrush BrFloorPlan = new(Color.FromRgb(0x42, 0xA5, 0xF5));   // Blue
        private static readonly SolidColorBrush BrCeilingPlan = new(Color.FromRgb(0x7E, 0x57, 0xC2));  // Deep Purple
        private static readonly SolidColorBrush BrSection = new(Color.FromRgb(0xAB, 0x47, 0xBC));      // Purple
        private static readonly SolidColorBrush BrElevation = new(Color.FromRgb(0x26, 0xA6, 0x9A));    // Teal
        private static readonly SolidColorBrush BrLegend = new(Color.FromRgb(0x66, 0xBB, 0x6A));       // Green
        private static readonly SolidColorBrush BrDrafting = new(Color.FromRgb(0xFF, 0xA7, 0x26));      // Orange
        private static readonly SolidColorBrush BrSchedule = new(Color.FromRgb(0x78, 0x90, 0x9C));     // Blue Grey
        private static readonly SolidColorBrush BrThreeD = new(Color.FromRgb(0xEF, 0x53, 0x50));       // Red
        private static readonly SolidColorBrush BrAreaPlan = new(Color.FromRgb(0x8D, 0x6E, 0x63));     // Brown
        private static readonly SolidColorBrush BrTemplate = new(Color.FromRgb(0xFF, 0xB3, 0x00));     // Amber (template badge)
        private static readonly SolidColorBrush BrDependent = new(Color.FromRgb(0x00, 0xAC, 0xC1));    // Cyan (dependent views)
        private static readonly SolidColorBrush BrScopeBox = new(Color.FromRgb(0xFF, 0x70, 0x43));     // Deep Orange

        // ── Discipline colours (ISO 19650) ─────────────────────────────
        private static readonly Dictionary<string, SolidColorBrush> DiscColors = new()
        {
            ["A"]  = new SolidColorBrush(Color.FromRgb(0x78, 0x90, 0x9C)),  // Blue Grey — Architectural
            ["S"]  = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50)),  // Red — Structural
            ["M"]  = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)),  // Blue — Mechanical
            ["E"]  = new SolidColorBrush(Color.FromRgb(0xFF, 0xCA, 0x28)),  // Amber — Electrical
            ["P"]  = new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A)),  // Green — Plumbing
            ["FP"] = new SolidColorBrush(Color.FromRgb(0xFF, 0x70, 0x43)),  // Deep Orange — Fire
            ["C"]  = new SolidColorBrush(Color.FromRgb(0xAB, 0x47, 0xBC)),  // Purple — Coordination
            ["L"]  = new SolidColorBrush(Color.FromRgb(0x8D, 0x6E, 0x63)),  // Brown — Landscape
            ["G"]  = new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD)),  // Grey — General
            ["LV"] = new SolidColorBrush(Color.FromRgb(0x00, 0x97, 0xA7)),  // Cyan — Low Voltage
        };

        // ── State ───────────────────────────────────────────────────────
        private static Window _window;
        private static TextBlock _statusText;
        private static TextBlock _headerStats;
        private static TreeView _viewsTree;
        private static TreeView _sheetsTree;
        private static CheckBox _hidePlacedCheck;
        private static ComboBox _viewBrowserMode;
        private static ComboBox _sheetBrowserMode;

        // ── Data passed from caller ─────────────────────────────────────
        private static List<SheetNode> _sheetNodes;
        private static List<UnplacedViewNode> _unplacedViews;
        private static List<AllViewNode> _allViews;

        // ── Callback for live execution via IExternalEventHandler ───────
        private static Action<string, Dictionary<string, object>> _executeCallback;
        private static Func<List<SheetNode>> _refreshSheetsFunc;
        private static Func<List<UnplacedViewNode>> _refreshUnplacedFunc;
        private static Func<List<AllViewNode>> _refreshAllViewsFunc;

        // ── Drag state ──────────────────────────────────────────────────
        private static TreeViewItem _dragSource;
        private static Point _dragStartPoint;
        private static TreeViewItem _lastHighlighted;

        // ── Multi-selection state ─────────────────────────────────────
        private static readonly HashSet<TreeViewItem> _selectedViewItems = new();
        private static readonly HashSet<TreeViewItem> _selectedSheetItems = new();
        private static TreeViewItem _lastClickedViewItem;
        private static TreeViewItem _lastClickedSheetItem;
        private static readonly SolidColorBrush BrSelected = new(Color.FromRgb(0xE3, 0xF2, 0xFD)); // Light blue selection

        // ── Template / scope box names cache for context menu ─────────
        internal static List<string> _cachedTemplateNames;
        internal static List<KeyValuePair<long, string>> _cachedScopeBoxes;

        /// <summary>Data model for sheet tree nodes.</summary>
        internal class SheetNode
        {
            public string SheetNumber { get; set; }
            public string SheetName { get; set; }
            public string Discipline { get; set; }
            public int ViewportCount { get; set; }
            public string PaperSize { get; set; }
            public string TitleBlockName { get; set; }
            public string DrawableArea { get; set; }
            public object Tag { get; set; } // ElementId
            public List<ViewportNode> Viewports { get; set; } = new List<ViewportNode>();
        }

        /// <summary>Data model for viewport tree child nodes.</summary>
        internal class ViewportNode
        {
            public string ViewName { get; set; }
            public string Scale { get; set; }
            public string PaperSize { get; set; }
            public string Position { get; set; }
            public object Tag { get; set; } // ElementId (viewport)
            public object ViewTag { get; set; } // ElementId (view)
            public string HostSheetNumber { get; set; }
        }

        /// <summary>Data model for unplaced views.</summary>
        internal class UnplacedViewNode
        {
            public string ViewName { get; set; }
            public string ViewType { get; set; }
            public string Scale { get; set; }
            public object Tag { get; set; } // ElementId
        }

        /// <summary>Data model for ALL views used in views browser.</summary>
        internal class AllViewNode
        {
            public string ViewName { get; set; }
            public string ViewType { get; set; }
            public string Scale { get; set; }
            public string PlacedOnSheet { get; set; }
            public string Discipline { get; set; }
            public string Level { get; set; }
            public string TemplateName { get; set; }    // View template applied
            public string ScopeBoxName { get; set; }    // Scope box applied
            public bool IsDependent { get; set; }       // Dependent view flag
            public string ParentViewName { get; set; }  // Parent view for dependents
            public object Tag { get; set; } // ElementId
            public bool IsPlaced { get; set; }
        }


        // ═══════════════════════════════════════════════════════════════════
        //  MAIN ENTRY POINT — legacy modal (for backwards compat)
        // ═══════════════════════════════════════════════════════════════════

        public static SheetManagerResult Show(List<SheetNode> sheets, List<UnplacedViewNode> unplacedViews)
        {
            return Show(sheets, unplacedViews, null);
        }

        public static SheetManagerResult Show(List<SheetNode> sheets, List<UnplacedViewNode> unplacedViews, List<AllViewNode> allViews)
        {
            _sheetNodes = sheets ?? new List<SheetNode>();
            _unplacedViews = unplacedViews ?? new List<UnplacedViewNode>();
            _allViews = allViews ?? BuildAllViewsFromData();
            _executeCallback = null;
            _refreshSheetsFunc = null;
            _refreshUnplacedFunc = null;
            _refreshAllViewsFunc = null;
            _dragSource = null;
            _lastHighlighted = null;

            var result = new SheetManagerResult();
            BuildWindow(result, modal: true);
            bool? dialogResult = _window.ShowDialog();
            result.Confirmed = dialogResult == true;
            return result;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  MODELESS ENTRY POINT — floating window with live execution
        // ═══════════════════════════════════════════════════════════════════

        public static void ShowModeless(
            List<SheetNode> sheets,
            List<UnplacedViewNode> unplacedViews,
            List<AllViewNode> allViews,
            Action<string, Dictionary<string, object>> executeCallback,
            Func<List<SheetNode>> refreshSheets = null,
            Func<List<UnplacedViewNode>> refreshUnplaced = null,
            Func<List<AllViewNode>> refreshAllViews = null)
        {
            if (_window != null && _window.IsVisible) { _window.Activate(); return; }

            _sheetNodes = sheets ?? new List<SheetNode>();
            _unplacedViews = unplacedViews ?? new List<UnplacedViewNode>();
            _allViews = allViews ?? BuildAllViewsFromData();
            _executeCallback = executeCallback;
            _refreshSheetsFunc = refreshSheets;
            _refreshUnplacedFunc = refreshUnplaced;
            _refreshAllViewsFunc = refreshAllViews;
            _dragSource = null;
            _lastHighlighted = null;

            BuildWindow(null, modal: false);
            _window.Show();
        }

        /// <summary>Refresh all data and rebuild trees (called after operations).</summary>
        public static void RefreshData()
        {
            if (_window == null || !_window.IsVisible) return;
            try
            {
                if (_refreshSheetsFunc != null) _sheetNodes = _refreshSheetsFunc();
                if (_refreshUnplacedFunc != null) _unplacedViews = _refreshUnplacedFunc();
                if (_refreshAllViewsFunc != null) _allViews = _refreshAllViewsFunc();
                else _allViews = BuildAllViewsFromData();

                RebuildViewsTree();
                RebuildSheetsTree();
                UpdateHeaderStats();
                UpdateStatus("Ready \u2014 data refreshed.");
            }
            catch (Exception ex) { StingLog.Warn($"SheetManager RefreshData: {ex.Message}"); }
        }

        public static void CloseIfOpen()
        {
            if (_window != null && _window.IsVisible)
            {
                try { _window.Close(); } catch (Exception ex) { StingLog.Warn($"Window close: {ex.Message}"); }
            }
            _window = null;
        }

        public static bool IsOpen => _window != null && _window.IsVisible;
        private static bool IsModeless => _executeCallback != null;

        // ═══════════════════════════════════════════════════════════════════
        //  WINDOW BUILDER
        // ═══════════════════════════════════════════════════════════════════

        private static void BuildWindow(SheetManagerResult modalResult, bool modal)
        {
            _window = new Window
            {
                Title = "STING Sheet Manager  \u2014  ISO 19650",
                Width = 1280, Height = 800,
                MinWidth = 960, MinHeight = 600,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = BrBgLight,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                Topmost = !modal, ShowInTaskbar = !modal,
            };

            if (!modal)
            {
                _window.Deactivated += (s, e) => { if (_window != null) _window.Topmost = false; };
                _window.Activated += (s, e) => { /* user can re-pin */ };
            }

            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(_window);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"Window owner: {ex.Message}"); }

            var root = new DockPanel { LastChildFill = true };

            // ── Header bar ──────────────────────────────────────────────
            var header = CreateHeader();
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // ── Top tools ribbon (Sheets + Views + Automation) ──────────
            var toolsBar = CreateTopToolsRibbon();
            DockPanel.SetDock(toolsBar, Dock.Top);
            root.Children.Add(toolsBar);

            // ── Bottom bar: Layout + QA + status ────────────────────────
            var bottomBar = CreateBottomBar(modalResult);
            DockPanel.SetDock(bottomBar, Dock.Bottom);
            root.Children.Add(bottomBar);

            // ── Main content: Views (left) | Splitter | Sheets (right) ─
            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 320 });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star), MinWidth = 380 });

            var leftPanel = CreateViewsPanel();
            Grid.SetColumn(leftPanel, 0);
            mainGrid.Children.Add(leftPanel);

            var splitter = new GridSplitter
            {
                Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = BrBorder, Cursor = Cursors.SizeWE
            };
            Grid.SetColumn(splitter, 1);
            mainGrid.Children.Add(splitter);

            var rightPanel = CreateSheetsPanel();
            Grid.SetColumn(rightPanel, 2);
            mainGrid.Children.Add(rightPanel);

            root.Children.Add(mainGrid);
            _window.Content = root;
        }

        private static List<AllViewNode> BuildAllViewsFromData()
        {
            var list = new List<AllViewNode>();
            foreach (var uv in _unplacedViews)
            {
                list.Add(new AllViewNode
                {
                    ViewName = uv.ViewName, ViewType = uv.ViewType,
                    Scale = uv.Scale, Tag = uv.Tag, IsPlaced = false
                });
            }
            foreach (var sn in _sheetNodes)
            {
                foreach (var vp in sn.Viewports)
                {
                    list.Add(new AllViewNode
                    {
                        ViewName = vp.ViewName, ViewType = "Placed",
                        Scale = vp.Scale, Tag = vp.ViewTag ?? vp.Tag,
                        IsPlaced = true, PlacedOnSheet = sn.SheetNumber
                    });
                }
            }
            return list;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  HEADER BAR
        // ═══════════════════════════════════════════════════════════════════

        private static Border CreateHeader()
        {
            var border = new Border
            {
                Background = BrBgHeader,
                Padding = new Thickness(16, 8, 16, 8),
            };
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new TextBlock
            {
                Text = "\u25A0 STING Sheet Manager",
                FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = BrAccent, VerticalAlignment = VerticalAlignment.Center
            });
            _headerStats = new TextBlock
            {
                FontSize = 12, Foreground = BrFgWhite,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0)
            };
            UpdateHeaderStats();
            stack.Children.Add(_headerStats);

            if (IsModeless)
            {
                var refreshBtn = CreateSmallButton("\u21BB Refresh", BrAccent);
                refreshBtn.Margin = new Thickness(16, 0, 0, 0);
                refreshBtn.Foreground = BrFgWhite;
                refreshBtn.Click += (s, e) => RefreshData();
                stack.Children.Add(refreshBtn);
            }

            border.Child = stack;
            return border;
        }

        private static void UpdateHeaderStats()
        {
            if (_headerStats == null) return;
            int depCount = _allViews?.Count(v => v.IsDependent) ?? 0;
            int withTemplate = _allViews?.Count(v => !string.IsNullOrEmpty(v.TemplateName)) ?? 0;
            int noTemplate = (_allViews?.Count ?? 0) - withTemplate;
            int scopeBoxed = _allViews?.Count(v => !string.IsNullOrEmpty(v.ScopeBoxName)) ?? 0;

            // Replace single TextBlock with clickable metric links
            var parent = _headerStats.Parent as Panel;
            if (parent == null) return;
            int idx = -1;
            for (int i = 0; i < parent.Children.Count; i++)
            {
                if (parent.Children[i] == _headerStats) { idx = i; break; }
            }
            if (idx < 0) return;

            var metricsPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
            metricsPanel.Children.Add(MakeStatBadge($"{_sheetNodes?.Count ?? 0} sheets", BrFgWhite, "Sheets",
                () => HighlightByFilter(v => false, s => true, "All sheets")));
            metricsPanel.Children.Add(MakeStatBadge($"{_unplacedViews?.Count ?? 0} unplaced", BrRed, "Unplaced",
                () => HighlightByFilter(v => !v.IsPlaced, s => false, "Unplaced views")));
            metricsPanel.Children.Add(MakeStatBadge($"{_allViews?.Count ?? 0} views", BrFgWhite, "Views",
                () => HighlightByFilter(v => true, s => false, "All views")));
            metricsPanel.Children.Add(MakeStatBadge($"{withTemplate} templated", BrGreen, "Templated",
                () => HighlightByFilter(v => !string.IsNullOrEmpty(v.TemplateName), s => false, "Views with templates")));
            metricsPanel.Children.Add(MakeStatBadge($"{noTemplate} no template", BrAccent, "NoTemplate",
                () => HighlightByFilter(v => string.IsNullOrEmpty(v.TemplateName), s => false, "Views WITHOUT templates")));
            metricsPanel.Children.Add(MakeStatBadge($"{depCount} dependent", BrDependent, "Dependent",
                () => HighlightByFilter(v => v.IsDependent, s => false, "Dependent views")));
            metricsPanel.Children.Add(MakeStatBadge($"{scopeBoxed} scoped", BrFgWhite, "ScopeBoxed",
                () => HighlightByFilter(v => !string.IsNullOrEmpty(v.ScopeBoxName), s => false, "Views with scope boxes")));

            parent.Children.RemoveAt(idx);
            parent.Children.Insert(idx, metricsPanel);
            _headerStats = new TextBlock(); // keep reference valid but unused
        }

        /// <summary>Creates a clickable header metric badge.</summary>
        private static Border MakeStatBadge(string text, SolidColorBrush color, string tag, Action onClick)
        {
            var tb = new TextBlock
            {
                Text = text, FontSize = 11, Foreground = color,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
            var border = new Border
            {
                Child = tb, Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(2, 0, 2, 0), Tag = tag,
                CornerRadius = new CornerRadius(3),
                Background = Brushes.Transparent
            };
            border.MouseEnter += (s, e) => border.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            border.MouseLeave += (s, e) => border.Background = Brushes.Transparent;
            border.MouseLeftButtonDown += (s, e) => { onClick?.Invoke(); e.Handled = true; };
            border.ToolTip = $"Click to highlight: {text}";
            return border;
        }

        /// <summary>
        /// Highlight views/sheets in the trees matching a filter predicate.
        /// Sends matching element IDs to the execution callback for Revit selection.
        /// </summary>
        private static void HighlightByFilter(Func<AllViewNode, bool> viewFilter,
            Func<SheetNode, bool> sheetFilter, string description)
        {
            // Collect matching element IDs
            var matchIds = new List<object>();
            if (_allViews != null)
                matchIds.AddRange(_allViews.Where(viewFilter).Select(v => v.Tag));
            if (_sheetNodes != null)
                matchIds.AddRange(_sheetNodes.Where(sheetFilter).Select(s => s.Tag));

            // Highlight matching items in trees
            int highlighted = 0;
            HighlightMatchingItems(_viewsTree, v =>
            {
                if (v is AllViewNode avn) return viewFilter(avn);
                return false;
            }, ref highlighted);
            HighlightMatchingItems(_sheetsTree, v =>
            {
                if (v is SheetNode sn) return sheetFilter(sn);
                return false;
            }, ref highlighted);

            // Send IDs to Revit for element selection
            if (matchIds.Count > 0)
            {
                var opts = new Dictionary<string, object>
                {
                    { "ElementIds", string.Join(",", matchIds.Select(id => id?.ToString() ?? "")) },
                    { "Description", description }
                };
                ExecuteOp("SM_SelectElementIds", opts);
            }

            UpdateStatus($"\u2714 {description}: {matchIds.Count} elements");
        }

        private static void HighlightMatchingItems(TreeView tree, Func<object, bool> matcher, ref int count)
        {
            if (tree == null) return;
            foreach (TreeViewItem item in tree.Items)
                HighlightMatchingItemsRecursive(item, matcher, ref count);
        }

        private static void HighlightMatchingItemsRecursive(TreeViewItem item, Func<object, bool> matcher, ref int count)
        {
            if (item.Tag != null && matcher(item.Tag))
            {
                item.Background = BrSelected;
                item.BringIntoView();
                count++;
            }
            else
            {
                item.Background = Brushes.Transparent;
            }
            foreach (TreeViewItem child in item.Items)
                HighlightMatchingItemsRecursive(child, matcher, ref count);
        }


        // ═══════════════════════════════════════════════════════════════════
        //  TOP TOOLS RIBBON — Sheets + Views + Automation (compact)
        // ═══════════════════════════════════════════════════════════════════

        private static Border CreateTopToolsRibbon()
        {
            var border = new Border
            {
                Background = BrBgWhite, BorderBrush = BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(8, 4, 8, 4)
            };
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };

            // ── SHEETS section ──
            wrap.Children.Add(MakeRibbonLabel("SHEETS"));
            wrap.Children.Add(MakeToolBtn("+ New Sheet", "CreateSheet", BrGreen, "\u2795 Create new sheet with title block selection"));
            wrap.Children.Add(MakeToolBtn("Clone", "CloneSheet", BrBlue, "Clone selected sheet with viewports"));
            wrap.Children.Add(MakeToolBtn("Place Unplaced", "AutoPlaceUnplaced", BrGreen, "Auto-place all unplaced views on sheets"));
            wrap.Children.Add(MakeToolBtn("Batch Clone", "BatchCloneSheets", BrBlue, "Clone multiple sheets"));
            wrap.Children.Add(MakeToolBtn("Renumber", "SM_RenumberDisc", BrAccent, "ISO 19650 two-pass sheet renumbering"));
            wrap.Children.Add(MakeToolBtn("From Template", "CreateFromTemplate", BrBlue, "Create sheet from template"));
            wrap.Children.Add(MakeRibbonSep());

            // ── VIEWS section ──
            wrap.Children.Add(MakeRibbonLabel("VIEWS"));
            wrap.Children.Add(MakeToolBtn("Duplicate", "SM_DuplicateView", BrBlue, "Duplicate selected view (with detailing)"));
            wrap.Children.Add(MakeToolBtn("Dependent", "CreateDependentViews", BrDependent, "Create scope-box-scoped dependent views"));
            wrap.Children.Add(MakeToolBtn("Batch Rename", "BatchRenameViews", BrBlue, "Batch rename views with 7 operations"));
            wrap.Children.Add(MakeToolBtn("Copy Settings", "CopyViewSettings", BrBlue, "Copy VG/filters from source view"));
            wrap.Children.Add(MakeToolBtn("Auto VP Types", "AutoAssignVPTypes", BrAccent, "Rule-based viewport type assignment"));
            wrap.Children.Add(MakeRibbonSep());

            // ── TEMPLATES section ──
            wrap.Children.Add(MakeRibbonLabel("TEMPLATES"));
            wrap.Children.Add(MakeToolBtn("Assign", "AutoAssignTemplates", BrTemplate, "5-layer intelligent template assignment"));
            wrap.Children.Add(MakeToolBtn("Audit", "TemplateAudit", BrFgSubtle, "View template coverage report"));
            wrap.Children.Add(MakeToolBtn("Clone VT", "CloneTemplate", BrBlue, "Deep clone view template"));
            wrap.Children.Add(MakeRibbonSep());

            // ── TITLE BLOCKS section ──
            wrap.Children.Add(MakeRibbonLabel("TITLE BLOCKS"));
            wrap.Children.Add(MakeToolBtn("Swap", "SM_SwapTitleBlock", BrAccent, "Swap title block on sheet(s)"));
            wrap.Children.Add(MakeToolBtn("Reset", "TitleBlockReset", BrFgSubtle, "Reset title block to origin"));
            wrap.Children.Add(MakeToolBtn("Rescue", "TitleBlockRescue", BrRed, "Find sheets missing title blocks"));
            wrap.Children.Add(MakeRibbonSep());

            // ── AUTOMATION section ──
            wrap.Children.Add(MakeRibbonLabel("\u26A1 AUTOMATION"));
            wrap.Children.Add(MakeToolBtn("Doc Package", "DocumentationPackage", BrAccent, "One-click: create full documentation set (views + sheets + templates)"));
            wrap.Children.Add(MakeToolBtn("Batch Views", "BatchCreateViews", BrAccent, "Batch create views from levels \u00D7 disciplines \u00D7 scope boxes"));
            wrap.Children.Add(MakeToolBtn("Batch Sheets", "BatchCreateSheets", BrAccent, "Batch create sheets with views placed from template"));
            wrap.Children.Add(MakeToolBtn("Scope Boxes", "ScopeBoxManager", BrScopeBox, "Audit and assign scope boxes"));

            border.Child = wrap;
            return border;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  LEFT PANEL — VIEWS BROWSER
        //  Groups by: Type | Discipline | Level | Template | Scope Box
        //  Shows: ceiling vs floor plan colors, template badges, dependent hierarchy
        // ═══════════════════════════════════════════════════════════════════

        private static Border CreateViewsPanel()
        {
            var border = new Border
            {
                Background = BrBgWhite, Margin = new Thickness(4),
                BorderBrush = BrBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4)
            };
            var panel = new DockPanel { LastChildFill = true };

            // ── Panel header with controls ───────────────────────────────
            var headerGrid = new Grid { Margin = new Thickness(8, 6, 8, 4) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleBlock = new StackPanel { Orientation = Orientation.Horizontal };
            titleBlock.Children.Add(new TextBlock
            {
                Text = "VIEWS", FontWeight = FontWeights.Bold, FontSize = 13,
                Foreground = BrFgDark, VerticalAlignment = VerticalAlignment.Center
            });
            // Expand/Collapse All buttons
            var expandBtn = CreateSmallButton("\u25BC", BrFgSubtle);
            expandBtn.ToolTip = "Expand All"; expandBtn.Margin = new Thickness(8, 0, 0, 0);
            expandBtn.Click += (s, e) => ExpandCollapseAll(_viewsTree, true);
            titleBlock.Children.Add(expandBtn);
            var collapseBtn = CreateSmallButton("\u25B6", BrFgSubtle);
            collapseBtn.ToolTip = "Collapse All"; collapseBtn.Margin = new Thickness(2, 0, 0, 0);
            collapseBtn.Click += (s, e) => ExpandCollapseAll(_viewsTree, false);
            titleBlock.Children.Add(collapseBtn);

            Grid.SetColumn(titleBlock, 0);
            headerGrid.Children.Add(titleBlock);

            // ── Search box ──
            var searchBox = CreateSearchBox("Search views...");
            searchBox.TextChanged += (s, e) => FilterTree(_viewsTree, searchBox.Text);
            Grid.SetColumn(searchBox, 1);
            searchBox.Margin = new Thickness(8, 0, 8, 0);
            headerGrid.Children.Add(searchBox);

            // ── Group-by mode ──
            _viewBrowserMode = new ComboBox
            {
                FontSize = 11, MinWidth = 110, VerticalAlignment = VerticalAlignment.Center
            };
            _viewBrowserMode.Items.Add("By Type");
            _viewBrowserMode.Items.Add("By Discipline");
            _viewBrowserMode.Items.Add("By Level");
            _viewBrowserMode.Items.Add("By Template");
            _viewBrowserMode.Items.Add("By Scope Box");
            _viewBrowserMode.Items.Add("Not on Sheets");
            _viewBrowserMode.Items.Add("All Views");
            _viewBrowserMode.SelectedIndex = 0;
            _viewBrowserMode.SelectionChanged += (s, e) => RebuildViewsTree();
            Grid.SetColumn(_viewBrowserMode, 2);
            headerGrid.Children.Add(_viewBrowserMode);

            DockPanel.SetDock(headerGrid, Dock.Top);
            panel.Children.Add(headerGrid);

            // ── Filters row ──────────────────────────────────────────────
            var filterRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 4) };
            _hidePlacedCheck = new CheckBox
            {
                Content = "Hide placed views", FontSize = 11, IsChecked = false,
                VerticalAlignment = VerticalAlignment.Center
            };
            _hidePlacedCheck.Checked += (s, e) => RebuildViewsTree();
            _hidePlacedCheck.Unchecked += (s, e) => RebuildViewsTree();
            filterRow.Children.Add(_hidePlacedCheck);
            DockPanel.SetDock(filterRow, Dock.Top);
            panel.Children.Add(filterRow);

            // ── Tree view ────────────────────────────────────────────────
            _viewsTree = new TreeView
            {
                Background = BrBgWhite, BorderThickness = new Thickness(0),
                Margin = new Thickness(4), Padding = new Thickness(0, 0, 0, 8)
            };
            _viewsTree.MouseDoubleClick += ViewsTree_DoubleClick;
            _viewsTree.PreviewMouseLeftButtonDown += ViewsTree_PreviewMouseLeftButtonDown;
            _viewsTree.PreviewMouseLeftButtonUp += (s, e) => HandleMultiSelect(_viewsTree, e, _selectedViewItems, ref _lastClickedViewItem);
            _viewsTree.MouseMove += ViewsTree_MouseMove;
            _viewsTree.ContextMenu = CreateViewsContextMenu();
            panel.Children.Add(_viewsTree);
            RebuildViewsTree();

            border.Child = panel;
            return border;
        }

        private static void RebuildViewsTree()
        {
            if (_viewsTree == null) return;
            _viewsTree.Items.Clear();

            string mode = _viewBrowserMode?.SelectedItem?.ToString() ?? "By Type";
            bool hidePlaced = _hidePlacedCheck?.IsChecked == true;
            var views = _allViews ?? new List<AllViewNode>();

            if (mode == "Not on Sheets")
            {
                views = views.Where(v => !v.IsPlaced).ToList();
            }
            else if (hidePlaced)
            {
                views = views.Where(v => !v.IsPlaced).ToList();
            }

            // Sort alphabetically
            views = views.OrderBy(v => v.ViewName ?? "").ToList();

            // Choose grouping function
            Func<AllViewNode, string> grouper = mode switch
            {
                "By Discipline" => v => v.Discipline ?? "General",
                "By Level" => v => v.Level ?? "No Level",
                "By Template" => v => string.IsNullOrEmpty(v.TemplateName) ? "\u26A0 No Template" : v.TemplateName,
                "By Scope Box" => v => string.IsNullOrEmpty(v.ScopeBoxName) ? "\u2014 No Scope Box" : v.ScopeBoxName,
                _ => v => ClassifyViewType(v.ViewType) // "By Type" default
            };

            var groups = views.GroupBy(grouper).OrderBy(g => g.Key);

            foreach (var group in groups)
            {
                var groupItem = MakeGroupNode($"{group.Key} ({group.Count()})", GetGroupColor(mode, group.Key));
                groupItem.IsExpanded = mode != "All Views"; // collapse "All Views" by default

                // For "By Type" mode with dependent views, show hierarchy
                var independents = group.Where(v => !v.IsDependent).ToList();
                var dependents = group.Where(v => v.IsDependent).ToList();

                foreach (var view in independents)
                {
                    var viewItem = MakeViewNode(view);

                    // Nest dependent views under their parent
                    var children = dependents.Where(d =>
                        !string.IsNullOrEmpty(d.ParentViewName) &&
                        d.ParentViewName == view.ViewName).ToList();
                    foreach (var dep in children)
                    {
                        viewItem.Items.Add(MakeViewNode(dep));
                        dependents.Remove(dep); // avoid duplicates
                    }

                    groupItem.Items.Add(viewItem);
                }

                // Orphaned dependents (parent not in this group)
                foreach (var dep in dependents)
                    groupItem.Items.Add(MakeViewNode(dep));

                _viewsTree.Items.Add(groupItem);
            }
        }

        /// <summary>Classify view type string into display group names.</summary>
        private static string ClassifyViewType(string viewType)
        {
            if (string.IsNullOrEmpty(viewType)) return "Other";
            var vt = viewType.ToLowerInvariant();
            if (vt.Contains("ceiling") || vt.Contains("rcp")) return "Ceiling Plans";
            if (vt.Contains("floor") || vt.Contains("plan")) return "Floor Plans";
            if (vt.Contains("section")) return "Sections";
            if (vt.Contains("elevation")) return "Elevations";
            if (vt.Contains("legend")) return "Legends";
            if (vt.Contains("drafting") || vt.Contains("detail")) return "Drafting Views";
            if (vt.Contains("schedule") || vt.Contains("report")) return "Schedules";
            if (vt.Contains("3d") || vt.Contains("three")) return "3D Views";
            if (vt.Contains("area")) return "Area Plans";
            return "Other";
        }


        // ═══════════════════════════════════════════════════════════════════
        //  RIGHT PANEL — SHEETS BROWSER
        //  Groups by: Discipline | Paper Size | Title Block | All Sheets
        //  Shows: discipline colours, title block info, viewport children
        // ═══════════════════════════════════════════════════════════════════

        private static Border CreateSheetsPanel()
        {
            var border = new Border
            {
                Background = BrBgWhite, Margin = new Thickness(4),
                BorderBrush = BrBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4)
            };
            var panel = new DockPanel { LastChildFill = true };

            // ── Panel header ─────────────────────────────────────────────
            var headerGrid = new Grid { Margin = new Thickness(8, 6, 8, 4) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleBlock = new StackPanel { Orientation = Orientation.Horizontal };
            titleBlock.Children.Add(new TextBlock
            {
                Text = "SHEETS", FontWeight = FontWeights.Bold, FontSize = 13,
                Foreground = BrFgDark, VerticalAlignment = VerticalAlignment.Center
            });
            var expandBtn = CreateSmallButton("\u25BC", BrFgSubtle);
            expandBtn.ToolTip = "Expand All"; expandBtn.Margin = new Thickness(8, 0, 0, 0);
            expandBtn.Click += (s, e) => ExpandCollapseAll(_sheetsTree, true);
            titleBlock.Children.Add(expandBtn);
            var collapseBtn = CreateSmallButton("\u25B6", BrFgSubtle);
            collapseBtn.ToolTip = "Collapse All"; collapseBtn.Margin = new Thickness(2, 0, 0, 0);
            collapseBtn.Click += (s, e) => ExpandCollapseAll(_sheetsTree, false);
            titleBlock.Children.Add(collapseBtn);

            Grid.SetColumn(titleBlock, 0);
            headerGrid.Children.Add(titleBlock);

            var searchBox = CreateSearchBox("Search sheets...");
            searchBox.TextChanged += (s, e) => FilterTree(_sheetsTree, searchBox.Text);
            Grid.SetColumn(searchBox, 1);
            searchBox.Margin = new Thickness(8, 0, 8, 0);
            headerGrid.Children.Add(searchBox);

            _sheetBrowserMode = new ComboBox
            {
                FontSize = 11, MinWidth = 110, VerticalAlignment = VerticalAlignment.Center
            };
            _sheetBrowserMode.Items.Add("By Discipline");
            _sheetBrowserMode.Items.Add("By Title Block");
            _sheetBrowserMode.Items.Add("By Paper Size");
            _sheetBrowserMode.Items.Add("All Sheets");
            _sheetBrowserMode.SelectedIndex = 0;
            _sheetBrowserMode.SelectionChanged += (s, e) => RebuildSheetsTree();
            Grid.SetColumn(_sheetBrowserMode, 2);
            headerGrid.Children.Add(_sheetBrowserMode);

            DockPanel.SetDock(headerGrid, Dock.Top);
            panel.Children.Add(headerGrid);

            // ── Tree view ────────────────────────────────────────────────
            _sheetsTree = new TreeView
            {
                Background = BrBgWhite, BorderThickness = new Thickness(0),
                Margin = new Thickness(4), Padding = new Thickness(0, 0, 0, 8),
                AllowDrop = true
            };
            _sheetsTree.MouseDoubleClick += SheetsTree_DoubleClick;
            _sheetsTree.PreviewMouseLeftButtonDown += SheetsTree_PreviewMouseLeftButtonDown;
            _sheetsTree.PreviewMouseLeftButtonUp += (s, e) => HandleMultiSelect(_sheetsTree, e, _selectedSheetItems, ref _lastClickedSheetItem);
            _sheetsTree.MouseMove += SheetsTree_MouseMove;
            _sheetsTree.DragOver += SheetsTree_DragOver;
            _sheetsTree.DragLeave += SheetsTree_DragLeave;
            _sheetsTree.Drop += SheetsTree_Drop;
            _sheetsTree.ContextMenu = CreateSheetsContextMenu();
            panel.Children.Add(_sheetsTree);
            RebuildSheetsTree();

            border.Child = panel;
            return border;
        }

        private static void RebuildSheetsTree()
        {
            if (_sheetsTree == null) return;
            _sheetsTree.Items.Clear();

            string mode = _sheetBrowserMode?.SelectedItem?.ToString() ?? "By Discipline";
            var sheets = (_sheetNodes ?? new List<SheetNode>()).OrderBy(s => s.SheetNumber).ToList();

            Func<SheetNode, string> grouper = mode switch
            {
                "By Title Block" => s => s.TitleBlockName ?? "Unknown Title Block",
                "By Paper Size" => s => s.PaperSize ?? "Unknown Size",
                "All Sheets" => s => "All Sheets",
                _ => s => s.Discipline ?? "General"
            };

            var groups = sheets.GroupBy(grouper).OrderBy(g => g.Key);

            foreach (var group in groups)
            {
                // Use discipline colour for discipline grouping
                SolidColorBrush groupColor = BrFgDark;
                if (mode == "By Discipline")
                {
                    string discCode = group.Key.Split(' ').FirstOrDefault()?.Trim() ?? "";
                    if (DiscColors.TryGetValue(discCode, out var dc)) groupColor = dc;
                }

                var groupItem = MakeGroupNode($"{group.Key} ({group.Count()})", groupColor);
                groupItem.IsExpanded = true;

                foreach (var sheet in group)
                {
                    var sheetItem = MakeSheetNode(sheet);
                    foreach (var vp in sheet.Viewports)
                        sheetItem.Items.Add(MakeViewportNode(vp));
                    groupItem.Items.Add(sheetItem);
                }

                _sheetsTree.Items.Add(groupItem);
            }

            // ── Unplaced views section ───────────────────────────────────
            if (_unplacedViews != null && _unplacedViews.Count > 0)
            {
                var unplacedGroup = MakeGroupNode($"\u26A0 Unplaced Views ({_unplacedViews.Count})", BrRed);
                unplacedGroup.IsExpanded = false;
                foreach (var uv in _unplacedViews.OrderBy(v => v.ViewName))
                {
                    var item = new TreeViewItem
                    {
                        Tag = uv.Tag,
                        Padding = new Thickness(2),
                        Header = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Children =
                            {
                                MakeColorDot(GetViewTypeColor(uv.ViewType)),
                                new TextBlock { Text = uv.ViewName, FontSize = 11, Foreground = BrFgDark },
                                new TextBlock { Text = $"  1:{uv.Scale}", FontSize = 10, Foreground = BrFgSubtle }
                            }
                        },
                        ToolTip = $"Unplaced: {uv.ViewType}\nDrag to a sheet to place."
                    };
                    unplacedGroup.Items.Add(item);
                }
                _sheetsTree.Items.Add(unplacedGroup);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  BOTTOM BAR — Layout tools + QA tools + Status
        // ═══════════════════════════════════════════════════════════════════

        private static Border CreateBottomBar(SheetManagerResult modalResult)
        {
            var border = new Border
            {
                Background = BrBgLight, BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(8, 4, 8, 4)
            };

            var outerStack = new DockPanel { LastChildFill = true };

            // ── Right side: modal buttons OR status ──────────────────────
            if (modalResult != null)
            {
                // Modal: OK / Cancel buttons
                var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var okBtn = CreateButton("Apply", BrGreen);
                okBtn.Width = 90;
                okBtn.Click += (s, e) => { modalResult.Confirmed = true; _window.DialogResult = true; };
                var cancelBtn = CreateButton("Close", BrFgSubtle);
                cancelBtn.Width = 90; cancelBtn.Margin = new Thickness(8, 0, 0, 0);
                cancelBtn.Click += (s, e) => { _window.DialogResult = false; };
                btnPanel.Children.Add(okBtn);
                btnPanel.Children.Add(cancelBtn);
                DockPanel.SetDock(btnPanel, Dock.Right);
                outerStack.Children.Add(btnPanel);
            }
            else
            {
                // Modeless: close button on right
                var closeBtn = CreateSmallButton("\u2715 Close", BrFgSubtle);
                closeBtn.Click += (s, e) => { try { _window.Close(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); } };
                DockPanel.SetDock(closeBtn, Dock.Right);
                outerStack.Children.Add(closeBtn);
            }

            // ── Bottom tools area: Layout + QA ───────────────────────────
            var toolsPanel = new StackPanel();

            // Layout tools row
            var layoutRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
            layoutRow.Children.Add(MakeRibbonLabel("LAYOUT"));
            layoutRow.Children.Add(MakeToolBtn("Auto Layout", "SM_ArrangeOnSheet", BrAccent, "Shelf-packing auto-arrange viewports on active sheet"));
            layoutRow.Children.Add(MakeToolBtn("MaxRects", "MaxRectsLayout", BrAccent, "MaxRects bin packing (BSSF) for optimal space usage"));
            layoutRow.Children.Add(MakeToolBtn("Grid Align", "GridAlignViewports", BrBlue, "Snap viewport centres to alignment grid"));
            layoutRow.Children.Add(MakeToolBtn("Align Edges", "AlignViewportEdges", BrBlue, "Align viewport edges (L/R/T/B/Centre)"));
            layoutRow.Children.Add(MakeToolBtn("Distribute", "DistributeViewports", BrBlue, "Distribute viewports evenly"));
            layoutRow.Children.Add(MakeToolBtn("Batch Arrange", "SM_BatchArrange", BrAccent, "Auto-arrange across ALL sheets"));
            layoutRow.Children.Add(MakeToolBtn("Auto Scale", "SM_AutoScaleSheet", BrAccent, "Calculate and apply optimal viewport scales"));
            layoutRow.Children.Add(MakeToolBtn("Save Preset", "SaveLayoutPreset", BrFgSubtle, "Save current layout as named preset"));
            layoutRow.Children.Add(MakeToolBtn("Apply Preset", "ApplyLayoutPreset", BrBlue, "Apply saved layout preset"));
            layoutRow.Children.Add(MakeToolBtn("Crop to Content", "CropToContent", BrAccent, "Smart crop views to element extents"));
            toolsPanel.Children.Add(layoutRow);

            // QA tools row
            var qaRow = new WrapPanel { Orientation = Orientation.Horizontal };
            qaRow.Children.Add(MakeRibbonLabel("QA"));
            qaRow.Children.Add(MakeToolBtn("Audit", "SheetAudit", BrBlue, "Sheet/viewport statistics audit"));
            qaRow.Children.Add(MakeToolBtn("ISO Check", "SheetComplianceCheck", BrGreen, "ISO 19650 sheet compliance (10 rules)"));
            qaRow.Children.Add(MakeToolBtn("\u26A0 Enforce ISO", "SM_EnforceISONaming", BrAccent, "Force ISO 19650 naming on sheets"));
            qaRow.Children.Add(MakeToolBtn("\u21A9 Revert ISO", "SM_RevertISONaming", BrFgSubtle, "Revert to original sheet names before ISO rename"));
            qaRow.Children.Add(MakeToolBtn("Template Compliance", "TemplateComplianceScore", BrGreen, "View template compliance scoring"));
            qaRow.Children.Add(MakeToolBtn("Export CSV", "ExportSheetSet", BrFgSubtle, "Export sheet inventory to CSV"));
            qaRow.Children.Add(MakeToolBtn("Register", "ExportSheetRegister", BrFgSubtle, "Export comprehensive sheet register"));
            qaRow.Children.Add(MakeToolBtn("Batch PDF", "BatchPrintSheets", BrRed, "Export sheets to PDF (all/discipline/selected)"));
            qaRow.Children.Add(MakeToolBtn("Save Template", "SaveSheetTemplate", BrFgSubtle, "Save current sheet as reusable template"));
            qaRow.Children.Add(MakeToolBtn("Drawing Register", "DocsDrawingRegister", BrBlue, "ISO 19650 drawing register with CDE tracking"));
            toolsPanel.Children.Add(qaRow);

            outerStack.Children.Add(toolsPanel);

            // ── Status text ──────────────────────────────────────────────
            _statusText = new TextBlock
            {
                Text = "Ready", FontSize = 10, Foreground = BrFgSubtle,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            };
            var statusRow = new DockPanel();
            statusRow.Children.Add(_statusText);
            toolsPanel.Children.Add(statusRow);

            border.Child = outerStack;
            return border;
        }


        // ═══════════════════════════════════════════════════════════════════
        //  DOUBLE-CLICK HANDLERS — open/activate views/sheets in Revit
        // ═══════════════════════════════════════════════════════════════════

        private static void ViewsTree_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = GetTreeViewItemUnderMouse(_viewsTree, e);
            if (item == null) return;
            object viewId = null;
            if (item.Tag is AllViewNode avn) viewId = avn.Tag;
            else viewId = item.Tag;
            if (viewId == null) return;
            ExecuteOp("ActivateView", new Dictionary<string, object> { { "ViewTag", viewId } });
        }

        private static void SheetsTree_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = GetTreeViewItemUnderMouse(_sheetsTree, e);
            if (item == null) return;
            object viewId = null;
            if (item.Tag is SheetNode sn) viewId = sn.Tag;
            else if (item.Tag is ViewportNode vn) viewId = vn.ViewTag ?? vn.Tag;
            else viewId = item.Tag;
            if (viewId == null) return;
            ExecuteOp("ActivateView", new Dictionary<string, object> { { "ViewTag", viewId } });
        }

        // ═══════════════════════════════════════════════════════════════════
        //  DRAG-DROP — Views → Sheets (place) and Viewports → Sheets (move)
        // ═══════════════════════════════════════════════════════════════════

        // ── Views tree: drag source ──────────────────────────────────────
        private static void ViewsTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(_viewsTree);
            _dragSource = GetTreeViewItemUnderMouse(_viewsTree, e);
        }

        private static void ViewsTree_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragSource == null) return;
            var pos = e.GetPosition(_viewsTree);
            if (Math.Abs(pos.X - _dragStartPoint.X) < 6 && Math.Abs(pos.Y - _dragStartPoint.Y) < 6) return;

            object tag = _dragSource.Tag;
            string name = "";
            if (tag is AllViewNode avn) { name = avn.ViewName; tag = avn.Tag; }
            else if (tag is UnplacedViewNode uvn) { name = uvn.ViewName; }

            if (tag == null) return;
            UpdateStatus($"\u2195 Dragging: {name}");
            var data = new DataObject("ViewDrag", tag);
            DragDrop.DoDragDrop(_viewsTree, data, DragDropEffects.Copy);
            _dragSource = null;
        }

        // ── Sheets tree: drag source (viewport move) ─────────────────────
        private static void SheetsTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(_sheetsTree);
            _dragSource = GetTreeViewItemUnderMouse(_sheetsTree, e);
        }

        private static void SheetsTree_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragSource == null) return;
            var pos = e.GetPosition(_sheetsTree);
            if (Math.Abs(pos.X - _dragStartPoint.X) < 6 && Math.Abs(pos.Y - _dragStartPoint.Y) < 6) return;

            // Only drag ViewportNode items
            if (!(_dragSource.Tag is ViewportNode vpn)) return;
            UpdateStatus($"\u2195 Moving: {vpn.ViewName}");
            var data = new DataObject("ViewportMove", _dragSource.Tag);
            DragDrop.DoDragDrop(_sheetsTree, data, DragDropEffects.Move);
            _dragSource = null;
        }

        // ── Sheets tree: drop target ─────────────────────────────────────
        private static void SheetsTree_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            ClearDropHighlight();

            var targetItem = GetTreeViewItemUnderDragEvent(_sheetsTree, e);
            if (targetItem == null) return;

            // Resolve to sheet node
            SheetNode targetSheet = ResolveSheetFromItem(targetItem);
            if (targetSheet == null) return;

            // Highlight target
            targetItem.Background = BrDropHighlight;
            targetItem.BorderBrush = BrDropBorder;
            targetItem.BorderThickness = new Thickness(2);
            _lastHighlighted = targetItem;

            if (e.Data.GetDataPresent("ViewDrag")) e.Effects = DragDropEffects.Copy;
            else if (e.Data.GetDataPresent("ViewportMove")) e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private static void SheetsTree_DragLeave(object sender, DragEventArgs e)
        {
            ClearDropHighlight();
        }

        private static void SheetsTree_Drop(object sender, DragEventArgs e)
        {
            ClearDropHighlight();
            var targetItem = GetTreeViewItemUnderDragEvent(_sheetsTree, e);
            if (targetItem == null) return;

            SheetNode targetSheet = ResolveSheetFromItem(targetItem);
            if (targetSheet == null) return;

            if (e.Data.GetDataPresent("ViewDrag"))
            {
                var viewTag = e.Data.GetData("ViewDrag");
                if (viewTag == null) return;
                UpdateStatus($"\u2713 Placing view on {targetSheet.SheetNumber}...");
                ExecuteOp("PlaceViewOnSheet", new Dictionary<string, object>
                {
                    { "ViewTag", viewTag },
                    { "SheetTag", targetSheet.Tag },
                });
            }
            else if (e.Data.GetDataPresent("ViewportMove"))
            {
                var vpNode = e.Data.GetData("ViewportMove") as ViewportNode;
                if (vpNode == null) return;
                if (vpNode.HostSheetNumber == targetSheet.SheetNumber) return; // same sheet
                UpdateStatus($"\u2713 Moving viewport to {targetSheet.SheetNumber}...");
                ExecuteOp("MoveViewportToSheet", new Dictionary<string, object>
                {
                    { "ViewportTag", vpNode.Tag },
                    { "TargetSheetTag", targetSheet.Tag },
                    { "TargetSheetNumber", targetSheet.SheetNumber },
                });
            }
        }

        private static SheetNode ResolveSheetFromItem(TreeViewItem item)
        {
            if (item?.Tag is SheetNode sn) return sn;
            // Viewport child — resolve parent sheet
            if (item?.Tag is ViewportNode)
            {
                var parent = FindParent<TreeViewItem>(item);
                if (parent?.Tag is SheetNode psn) return psn;
            }
            return null;
        }

        private static TreeViewItem FindSheetItem(SheetNode target)
        {
            foreach (TreeViewItem group in _sheetsTree.Items)
            {
                foreach (TreeViewItem sheetItem in group.Items)
                {
                    if (sheetItem.Tag is SheetNode sn && sn.SheetNumber == target.SheetNumber)
                        return sheetItem;
                }
            }
            return null;
        }

        private static void ClearDropHighlight()
        {
            if (_lastHighlighted != null)
            {
                _lastHighlighted.Background = Brushes.Transparent;
                _lastHighlighted.BorderBrush = Brushes.Transparent;
                _lastHighlighted.BorderThickness = new Thickness(0);
                _lastHighlighted = null;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  PLACE VIEW ON SHEET (right-click action)
        // ═══════════════════════════════════════════════════════════════════

        private static void PlaceSelectedViewOnSheet()
        {
            var viewTag = GetSelectedViewTag();
            if (viewTag == null) { UpdateStatus("Select a view first."); return; }

            // Build list of available sheets
            var sheetNames = _sheetNodes?.Select(s => $"{s.SheetNumber} - {s.SheetName}").ToList();
            if (sheetNames == null || sheetNames.Count == 0) { UpdateStatus("No sheets available."); return; }

            var pickedSheet = StingListPicker.Show("Place View on Sheet",
                "Select target sheet:", sheetNames);
            if (string.IsNullOrEmpty(pickedSheet)) return;
            var picker = new List<string> { pickedSheet };

            int idx = sheetNames.IndexOf(picker[0]);
            if (idx < 0 || idx >= _sheetNodes.Count) return;
            var targetSheet = _sheetNodes[idx];

            ExecuteOp("PlaceViewOnSheet", new Dictionary<string, object>
            {
                { "ViewTag", viewTag },
                { "SheetTag", targetSheet.Tag },
            });
        }


        // ═══════════════════════════════════════════════════════════════════
        //  CONTEXT MENUS
        // ═══════════════════════════════════════════════════════════════════

        private static ContextMenu CreateViewsContextMenu()
        {
            var menu = new ContextMenu();

            var openItem = new MenuItem { Header = "Open View", FontWeight = FontWeights.Bold };
            openItem.Click += (s, e) =>
            {
                var tag = GetSelectedViewTag();
                if (tag != null) ExecuteOp("ActivateView", new Dictionary<string, object> { { "ViewTag", tag } });
            };
            menu.Items.Add(openItem);
            menu.Items.Add(new Separator());

            var placeItem = new MenuItem { Header = "Place on Sheet..." };
            placeItem.Click += (s, e) => PlaceSelectedViewOnSheet();
            menu.Items.Add(placeItem);

            var placeNewItem = new MenuItem { Header = "Place on New Sheet" };
            placeNewItem.Click += (s, e) =>
            {
                var vt = GetSelectedViewTag();
                if (vt != null) ExecuteOp("PlaceOnNewSheet", new Dictionary<string, object> { { "SelectedTag", vt } });
            };
            menu.Items.Add(placeNewItem);
            menu.Items.Add(new Separator());

            var dupItem = new MenuItem { Header = "Duplicate View" };
            dupItem.Click += (s, e) =>
            {
                var vt = GetSelectedViewTag();
                if (vt != null) ExecuteOp("DuplicateView", new Dictionary<string, object> { { "SelectedTag", vt } });
            };
            menu.Items.Add(dupItem);

            var depItem = new MenuItem { Header = "Create Dependent View" };
            depItem.Click += (s, e) => ExecuteOp("CreateDependentViews");
            depItem.Foreground = BrDependent;
            menu.Items.Add(depItem);

            var assignTpl = new MenuItem { Header = "Assign View Template \u25B6" };
            assignTpl.Foreground = BrTemplate;
            // Dynamically populate submenu with all project templates on open
            assignTpl.SubmenuOpened += (s, e) =>
            {
                assignTpl.Items.Clear();
                // Auto-assign option (5-layer intelligence)
                var autoItem = new MenuItem { Header = "\u2728 Auto-Assign (intelligent)", FontWeight = FontWeights.Bold };
                autoItem.Click += (s2, e2) => ExecuteOp("AutoAssignTemplates");
                assignTpl.Items.Add(autoItem);
                assignTpl.Items.Add(new Separator());
                // Remove template option
                var removeItem = new MenuItem { Header = "\u2715 Remove Template", Foreground = BrRed };
                removeItem.Click += (s2, e2) =>
                {
                    var vt = GetSelectedViewTag();
                    if (vt != null)
                        ExecuteOp("SM_RemoveViewTemplate", new Dictionary<string, object> { { "SelectedTag", vt } });
                };
                assignTpl.Items.Add(removeItem);
                assignTpl.Items.Add(new Separator());
                // Populate with all project templates
                ExecuteOp("SM_GetViewTemplates", new Dictionary<string, object>());
                if (_cachedTemplateNames != null)
                {
                    foreach (var tplName in _cachedTemplateNames.OrderBy(n => n))
                    {
                        var tplItem = new MenuItem { Header = tplName };
                        string capturedName = tplName;
                        tplItem.Click += (s2, e2) =>
                        {
                            var vt = GetSelectedViewTag();
                            if (vt != null)
                                ExecuteOp("SM_AssignSpecificTemplate", new Dictionary<string, object>
                                {
                                    { "SelectedTag", vt },
                                    { "TemplateName", capturedName }
                                });
                        };
                        assignTpl.Items.Add(tplItem);
                    }
                }
                else
                {
                    assignTpl.Items.Add(new MenuItem { Header = "(loading...)", IsEnabled = false });
                }
            };
            // Seed with placeholder so the arrow shows
            assignTpl.Items.Add(new MenuItem { Header = "(loading...)", IsEnabled = false });
            menu.Items.Add(assignTpl);

            var scopeItem = new MenuItem { Header = "Assign Scope Box \u25B6" };
            scopeItem.Foreground = BrScopeBox;
            scopeItem.SubmenuOpened += (s, e) =>
            {
                scopeItem.Items.Clear();
                // Remove scope box option
                var removeItem = new MenuItem { Header = "\u2715 Remove Scope Box", Foreground = BrRed };
                removeItem.Click += (s2, e2) =>
                {
                    var vt = GetSelectedViewTag();
                    if (vt != null)
                        ExecuteOp("SM_RemoveScopeBox", new Dictionary<string, object> { { "SelectedTag", vt } });
                };
                scopeItem.Items.Add(removeItem);
                scopeItem.Items.Add(new Separator());
                // Manage scope boxes option
                var manageItem = new MenuItem { Header = "\u2699 Manage Scope Boxes..." };
                manageItem.Click += (s2, e2) => ExecuteOp("ScopeBoxManager");
                scopeItem.Items.Add(manageItem);
                scopeItem.Items.Add(new Separator());
                // Populate with all project scope boxes
                ExecuteOp("SM_GetScopeBoxes", new Dictionary<string, object>());
                if (_cachedScopeBoxes != null && _cachedScopeBoxes.Count > 0)
                {
                    foreach (var sb in _cachedScopeBoxes)
                    {
                        var sbItem = new MenuItem { Header = sb.Value };
                        long capturedId = sb.Key;
                        string capturedName = sb.Value;
                        sbItem.Click += (s2, e2) =>
                        {
                            var vt = GetSelectedViewTag();
                            if (vt != null)
                                ExecuteOp("SM_AssignScopeBox", new Dictionary<string, object>
                                {
                                    { "SelectedTag", vt },
                                    { "ScopeBoxId", capturedId.ToString() },
                                    { "ScopeBoxName", capturedName }
                                });
                        };
                        scopeItem.Items.Add(sbItem);
                    }
                }
                else
                {
                    scopeItem.Items.Add(new MenuItem { Header = "(no scope boxes in project)", IsEnabled = false });
                }
            };
            // Seed with placeholder so the arrow shows
            scopeItem.Items.Add(new MenuItem { Header = "(loading...)", IsEnabled = false });
            menu.Items.Add(scopeItem);

            var cropItem = new MenuItem { Header = "Crop to Content" };
            cropItem.Click += (s, e) => ExecuteOp("CropToContent");
            menu.Items.Add(cropItem);

            var renameItem = new MenuItem { Header = "Batch Rename Views..." };
            renameItem.Click += (s, e) => ExecuteOp("BatchRenameViews");
            menu.Items.Add(renameItem);

            menu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = "Delete View", Foreground = BrRed };
            deleteItem.Click += (s, e) =>
            {
                var vt = GetSelectedViewTag();
                if (vt != null) ExecuteOp("DeleteView", new Dictionary<string, object> { { "SelectedTag", vt } });
            };
            menu.Items.Add(deleteItem);

            return menu;
        }

        private static ContextMenu CreateSheetsContextMenu()
        {
            var menu = new ContextMenu();

            var openItem = new MenuItem { Header = "Open Sheet", FontWeight = FontWeights.Bold };
            openItem.Click += (s, e) =>
            {
                var tag = GetSelectedSheetTag();
                if (tag != null) ExecuteOp("ActivateView", new Dictionary<string, object> { { "ViewTag", tag } });
            };
            menu.Items.Add(openItem);
            menu.Items.Add(new Separator());

            var cloneItem = new MenuItem { Header = "Clone Sheet" };
            cloneItem.Click += (s, e) =>
            {
                var sel = GetSelectedSheetTag();
                if (sel != null)
                    ExecuteOp("CloneSheet", new Dictionary<string, object> { { "SelectedTag", sel } });
            };
            menu.Items.Add(cloneItem);

            var arrangeItem = new MenuItem { Header = "Arrange Viewports" };
            arrangeItem.Click += (s, e) =>
            {
                var sel = GetSelectedSheetTag();
                var opts = new Dictionary<string, object>();
                if (sel != null) opts["SelectedTag"] = sel;
                ExecuteOp("ArrangeOnSheet", opts);
            };
            menu.Items.Add(arrangeItem);

            var autoScaleItem = new MenuItem { Header = "Auto-Scale Viewports" };
            autoScaleItem.Click += (s, e) => ExecuteOp("AutoScaleSheet");
            menu.Items.Add(autoScaleItem);

            var gridItem = new MenuItem { Header = "Grid Align Viewports" };
            gridItem.Click += (s, e) => ExecuteOp("GridAlignViewports");
            menu.Items.Add(gridItem);

            var alignItem = new MenuItem { Header = "Align Viewport Edges" };
            alignItem.Click += (s, e) => ExecuteOp("AlignViewportEdges");
            menu.Items.Add(alignItem);

            var distItem = new MenuItem { Header = "Distribute Viewports" };
            distItem.Click += (s, e) => ExecuteOp("DistributeViewports");
            menu.Items.Add(distItem);
            menu.Items.Add(new Separator());

            // ── Title block submenu ──
            var tbMenu = new MenuItem { Header = "Title Block \u25B6" };
            var tbSwap = new MenuItem { Header = "Swap Title Block" };
            tbSwap.Click += (s, e) =>
            {
                var sel = GetSelectedSheetTag();
                var opts = new Dictionary<string, object>();
                if (sel != null) opts["SelectedTag"] = sel;
                ExecuteOp("SM_SwapTitleBlock", opts);
            };
            tbMenu.Items.Add(tbSwap);
            var tbReset = new MenuItem { Header = "Reset to Origin" };
            tbReset.Click += (s, e) => ExecuteOp("TitleBlockReset");
            tbMenu.Items.Add(tbReset);
            menu.Items.Add(tbMenu);

            // ── ISO & Naming ──
            var isoMenu = new MenuItem { Header = "ISO 19650 \u25B6" };
            var isoCheck = new MenuItem { Header = "\u2714 Compliance Check", Foreground = BrGreen };
            isoCheck.Click += (s, e) => ExecuteOp("SheetComplianceCheck");
            isoMenu.Items.Add(isoCheck);
            var isoEnforce = new MenuItem { Header = "\u270E Enforce ISO Naming" };
            isoEnforce.Click += (s, e) => ExecuteOp("EnforceISONaming");
            isoMenu.Items.Add(isoEnforce);
            var isoRevert = new MenuItem { Header = "\u21A9 Revert to Original Names" };
            isoRevert.Click += (s, e) => ExecuteOp("RevertISONaming");
            isoMenu.Items.Add(isoRevert);
            menu.Items.Add(isoMenu);

            var renumItem = new MenuItem { Header = "Renumber Sheets..." };
            renumItem.Click += (s, e) => ExecuteOp("RenumberDisc");
            menu.Items.Add(renumItem);

            menu.Items.Add(new Separator());

            // ── Export ──
            var exportMenu = new MenuItem { Header = "Export \u25B6" };
            var pdfItem = new MenuItem { Header = "\U0001F4C4 Print/Export to PDF" };
            pdfItem.Click += (s, e) => ExecuteOp("BatchPrintSheets");
            exportMenu.Items.Add(pdfItem);
            var csvItem = new MenuItem { Header = "\U0001F4CA Export Sheet Register (CSV)" };
            csvItem.Click += (s, e) => ExecuteOp("ExportSheetRegister");
            exportMenu.Items.Add(csvItem);
            var setItem = new MenuItem { Header = "\U0001F4CB Export Sheet Set" };
            setItem.Click += (s, e) => ExecuteOp("ExportSheetSet");
            exportMenu.Items.Add(setItem);
            menu.Items.Add(exportMenu);

            menu.Items.Add(new Separator());

            // ── Viewport operations (when viewport selected) ──
            var moveVpItem = new MenuItem { Header = "Move Viewport to Sheet \u25B6" };
            moveVpItem.Click += (s, e) => ShowMoveViewportPicker();
            menu.Items.Add(moveVpItem);

            var removeVpItem = new MenuItem { Header = "Remove Viewport from Sheet", Foreground = BrRed };
            removeVpItem.Click += (s, e) =>
            {
                var vpTag = GetSelectedViewportTag();
                if (vpTag != null)
                    ExecuteOp("RemoveViewport", new Dictionary<string, object> { { "SelectedTag", vpTag } });
            };
            menu.Items.Add(removeVpItem);

            return menu;
        }

        private static void ShowMoveViewportPicker()
        {
            var vpTag = GetSelectedViewportTag();
            if (vpTag == null) { UpdateStatus("Select a viewport first."); return; }

            var sheetNames = _sheetNodes?.Select(s => $"{s.SheetNumber} - {s.SheetName}").ToList();
            if (sheetNames == null || sheetNames.Count == 0) return;

            var pickedSheet = StingListPicker.Show("Move Viewport",
                "Select target sheet:", sheetNames);
            if (string.IsNullOrEmpty(pickedSheet)) return;
            var picker = new List<string> { pickedSheet };

            int idx = sheetNames.IndexOf(picker[0]);
            if (idx < 0 || idx >= _sheetNodes.Count) return;

            ExecuteOp("MoveViewportToSheet", new Dictionary<string, object>
            {
                { "ViewportTag", vpTag },
                { "TargetSheetTag", _sheetNodes[idx].Tag },
                { "TargetSheetNumber", _sheetNodes[idx].SheetNumber }
            });
        }


        // ═══════════════════════════════════════════════════════════════════
        //  OPERATION EXECUTION — route to callback or dispatch
        // ═══════════════════════════════════════════════════════════════════

        private static void ExecuteOp(string operation, Dictionary<string, object> options = null)
        {
            if (string.IsNullOrEmpty(operation)) return;
            options ??= new Dictionary<string, object>();

            if (IsModeless)
            {
                try
                {
                    _executeCallback?.Invoke(operation, options);
                    UpdateStatus($"\u2713 {operation} executed.");
                }
                catch (Exception ex)
                {
                    UpdateStatus($"\u2717 {operation} failed: {ex.Message}");
                    StingLog.Warn($"SheetManager ExecuteOp: {ex.Message}");
                }
            }
            else
            {
                // Pass options as ExtraParams so StingCommandHandler can read them
                foreach (var kv in options)
                    StingCommandHandler.SetExtraParam("SM_" + kv.Key, kv.Value?.ToString() ?? "");

                if (StingDockPanel.DispatchCommand(operation))
                    UpdateStatus($"Running: {operation}...");
                else
                    UpdateStatus($"Cannot run {operation} \u2014 Revit may be busy.");
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  NODE BUILDERS — TreeViewItem factories with colour coding
        // ═══════════════════════════════════════════════════════════════════

        private static TreeViewItem MakeGroupNode(string text, SolidColorBrush color)
        {
            return new TreeViewItem
            {
                IsExpanded = true,
                FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = color ?? BrFgDark,
                Header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        MakeColorDot(color ?? BrFgDark, 10),
                        new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) }
                    }
                },
                Padding = new Thickness(2, 3, 2, 3)
            };
        }

        private static TreeViewItem MakeViewNode(AllViewNode view)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal };

            // Colour dot — ceiling vs floor vs other (DISTINCT)
            var typeColor = GetViewTypeColor(view.ViewType);
            stack.Children.Add(MakeColorDot(typeColor));

            // Dependent view indicator
            if (view.IsDependent)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "\u2514\u2500 ", FontSize = 10, Foreground = BrDependent,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            // View name
            var nameBlock = new TextBlock
            {
                Text = view.ViewName ?? "(unnamed)",
                FontSize = 11,
                Foreground = BrFgDark,
                FontStyle = view.IsPlaced ? FontStyles.Italic : FontStyles.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.Children.Add(nameBlock);

            // Scale
            if (!string.IsNullOrEmpty(view.Scale))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"  1:{view.Scale}", FontSize = 10,
                    Foreground = BrFgSubtle, VerticalAlignment = VerticalAlignment.Center
                });
            }

            // Template badge [T]
            if (!string.IsNullOrEmpty(view.TemplateName))
            {
                stack.Children.Add(new Border
                {
                    Background = BrTemplate, CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(3, 0, 3, 0),
                    Child = new TextBlock
                    {
                        Text = "T", FontSize = 8, FontWeight = FontWeights.Bold,
                        Foreground = BrFgWhite, VerticalAlignment = VerticalAlignment.Center
                    },
                    ToolTip = $"Template: {view.TemplateName}"
                });
            }

            // Scope box badge [SB]
            if (!string.IsNullOrEmpty(view.ScopeBoxName))
            {
                stack.Children.Add(new Border
                {
                    Background = BrScopeBox, CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(3, 0, 0, 0), Padding = new Thickness(3, 0, 3, 0),
                    Child = new TextBlock
                    {
                        Text = "SB", FontSize = 8, FontWeight = FontWeights.Bold,
                        Foreground = BrFgWhite, VerticalAlignment = VerticalAlignment.Center
                    },
                    ToolTip = $"Scope Box: {view.ScopeBoxName}"
                });
            }

            // Discipline badge
            if (!string.IsNullOrEmpty(view.Discipline) && DiscColors.TryGetValue(view.Discipline, out var discBrush))
            {
                stack.Children.Add(new Border
                {
                    Background = discBrush, CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(3, 0, 0, 0), Padding = new Thickness(4, 0, 4, 0),
                    Child = new TextBlock
                    {
                        Text = view.Discipline, FontSize = 8, FontWeight = FontWeights.Bold,
                        Foreground = BrFgWhite, VerticalAlignment = VerticalAlignment.Center
                    }
                });
            }

            // Placed-on-sheet badge
            if (view.IsPlaced && !string.IsNullOrEmpty(view.PlacedOnSheet))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $" [{view.PlacedOnSheet}]", FontSize = 10,
                    Foreground = BrAccent, VerticalAlignment = VerticalAlignment.Center
                });
            }

            // Build tooltip
            var tooltipParts = new List<string>();
            tooltipParts.Add($"Type: {view.ViewType ?? "Unknown"}");
            if (!string.IsNullOrEmpty(view.Scale)) tooltipParts.Add($"Scale: 1:{view.Scale}");
            if (!string.IsNullOrEmpty(view.Level)) tooltipParts.Add($"Level: {view.Level}");
            if (!string.IsNullOrEmpty(view.TemplateName)) tooltipParts.Add($"Template: {view.TemplateName}");
            if (!string.IsNullOrEmpty(view.ScopeBoxName)) tooltipParts.Add($"Scope Box: {view.ScopeBoxName}");
            if (!string.IsNullOrEmpty(view.Discipline)) tooltipParts.Add($"Discipline: {view.Discipline}");
            if (view.IsDependent) tooltipParts.Add($"Dependent of: {view.ParentViewName}");
            if (view.IsPlaced) tooltipParts.Add($"On sheet: {view.PlacedOnSheet}");
            else tooltipParts.Add("Not placed \u2014 drag to a sheet");
            tooltipParts.Add("Double-click to open");

            return new TreeViewItem
            {
                Tag = view,
                Header = stack,
                ToolTip = string.Join("\n", tooltipParts),
                Padding = new Thickness(2, 1, 2, 1)
            };
        }

        private static TreeViewItem MakeSheetNode(SheetNode sheet)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal };

            // Discipline colour dot
            string disc = sheet.Discipline?.Split(' ').FirstOrDefault() ?? "";
            SolidColorBrush discColor = DiscColors.TryGetValue(disc, out var dc) ? dc : BrFgSubtle;
            stack.Children.Add(MakeColorDot(discColor));

            // Sheet number (accent)
            stack.Children.Add(new TextBlock
            {
                Text = sheet.SheetNumber, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = BrAccent, MinWidth = 65, VerticalAlignment = VerticalAlignment.Center
            });

            // Sheet name
            stack.Children.Add(new TextBlock
            {
                Text = $"\u2014 {sheet.SheetName}", FontSize = 11,
                Foreground = BrFgDark, VerticalAlignment = VerticalAlignment.Center
            });

            // Metadata badge
            stack.Children.Add(new TextBlock
            {
                Text = $"  [{sheet.ViewportCount}vp, {sheet.PaperSize ?? "?"}]",
                FontSize = 10, Foreground = BrFgSubtle,
                VerticalAlignment = VerticalAlignment.Center
            });

            // Title block badge
            if (!string.IsNullOrEmpty(sheet.TitleBlockName))
            {
                stack.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(4, 0, 4, 0),
                    Child = new TextBlock
                    {
                        Text = "TB", FontSize = 8, FontWeight = FontWeights.Bold,
                        Foreground = BrFgDark, VerticalAlignment = VerticalAlignment.Center
                    },
                    ToolTip = $"Title Block: {sheet.TitleBlockName}"
                });
            }

            return new TreeViewItem
            {
                Tag = sheet,
                Header = stack,
                IsExpanded = true,
                ToolTip = $"Title block: {sheet.TitleBlockName ?? "None"}\n" +
                    $"Drawable: {sheet.DrawableArea ?? "?"}\n" +
                    $"Viewports: {sheet.ViewportCount}\n" +
                    "Double-click to open  |  Drop views here to place",
                Padding = new Thickness(2, 2, 2, 2)
            };
        }

        private static TreeViewItem MakeViewportNode(ViewportNode vp)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal };

            // Indent arrow
            stack.Children.Add(new TextBlock
            {
                Text = "\u21B3 ", FontSize = 10, Foreground = BrFgSubtle,
                VerticalAlignment = VerticalAlignment.Center
            });

            // Colour dot for view type
            var typeColor = GetViewTypeColor(vp.ViewName);
            stack.Children.Add(MakeColorDot(typeColor, 6));

            // View name
            stack.Children.Add(new TextBlock
            {
                Text = vp.ViewName, FontSize = 11,
                Foreground = BrFgDark, VerticalAlignment = VerticalAlignment.Center
            });

            // Scale
            stack.Children.Add(new TextBlock
            {
                Text = $"  1:{vp.Scale}", FontSize = 10,
                Foreground = BrFgSubtle, VerticalAlignment = VerticalAlignment.Center
            });

            return new TreeViewItem
            {
                Tag = vp,
                Header = stack,
                ToolTip = $"View: {vp.ViewName}\nScale: 1:{vp.Scale}\n" +
                    $"Position: {vp.Position ?? "?"}\nSize: {vp.PaperSize ?? "?"}\n" +
                    $"Sheet: {vp.HostSheetNumber}\n" +
                    "Double-click to open  |  Drag to move to another sheet",
                Padding = new Thickness(2, 1, 2, 1)
            };
        }


        // ═══════════════════════════════════════════════════════════════════
        //  COLOUR LOGIC — view type + discipline + ceiling distinction
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Get colour for a view type string. Ceiling plans get DISTINCT purple
        /// (not same blue as floor plans). ISO 19650 discipline colors available.
        /// </summary>
        private static SolidColorBrush GetViewTypeColor(string viewType)
        {
            if (string.IsNullOrEmpty(viewType)) return BrFgSubtle;
            var vt = viewType.ToLowerInvariant();
            if (vt.Contains("ceiling") || vt.Contains("rcp")) return BrCeilingPlan;  // Deep Purple — DISTINCT
            if (vt.Contains("floor") || vt.Contains("plan")) return BrFloorPlan;      // Blue
            if (vt.Contains("section")) return BrSection;                              // Purple
            if (vt.Contains("elevation")) return BrElevation;                          // Teal
            if (vt.Contains("legend")) return BrLegend;                                // Green
            if (vt.Contains("drafting") || vt.Contains("detail")) return BrDrafting;   // Orange
            if (vt.Contains("schedule") || vt.Contains("report")) return BrSchedule;   // Blue Grey
            if (vt.Contains("3d") || vt.Contains("three")) return BrThreeD;            // Red
            if (vt.Contains("area")) return BrAreaPlan;                                // Brown
            return BrFgSubtle;
        }

        /// <summary>Get colour for group headers based on grouping mode.</summary>
        private static SolidColorBrush GetGroupColor(string mode, string groupKey)
        {
            if (mode == "By Discipline")
            {
                string code = groupKey.Split(' ').FirstOrDefault()?.Trim() ?? "";
                if (DiscColors.TryGetValue(code, out var dc)) return dc;
            }
            if (mode == "By Type")
            {
                var key = groupKey.ToLowerInvariant();
                if (key.Contains("ceiling")) return BrCeilingPlan;
                if (key.Contains("floor") || key.Contains("plan")) return BrFloorPlan;
                if (key.Contains("section")) return BrSection;
                if (key.Contains("elevation")) return BrElevation;
                if (key.Contains("legend")) return BrLegend;
                if (key.Contains("drafting")) return BrDrafting;
                if (key.Contains("schedule")) return BrSchedule;
                if (key.Contains("3d")) return BrThreeD;
                if (key.Contains("area")) return BrAreaPlan;
            }
            if (mode == "By Template")
            {
                if (groupKey.StartsWith("\u26A0")) return BrRed; // warning = no template
                return BrTemplate;
            }
            if (mode == "By Scope Box")
            {
                if (groupKey.StartsWith("\u2014")) return BrFgSubtle; // no scope box
                return BrScopeBox;
            }
            return BrFgDark;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  EXPAND / COLLAPSE ALL
        // ═══════════════════════════════════════════════════════════════════

        private static void ExpandCollapseAll(TreeView tree, bool expand)
        {
            if (tree == null) return;
            foreach (var item in tree.Items)
            {
                if (item is TreeViewItem tvi)
                    SetExpanded(tvi, expand);
            }
        }

        private static void SetExpanded(TreeViewItem item, bool expand)
        {
            item.IsExpanded = expand;
            foreach (var child in item.Items)
            {
                if (child is TreeViewItem childItem)
                    SetExpanded(childItem, expand);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  SEARCH / FILTER
        // ═══════════════════════════════════════════════════════════════════

        private static void FilterTree(TreeView tree, string query)
        {
            if (tree == null) return;
            if (string.IsNullOrWhiteSpace(query))
            {
                SetAllVisible(tree);
                return;
            }

            string lower = query.ToLowerInvariant();
            foreach (TreeViewItem group in tree.Items)
            {
                bool anyChildVisible = false;
                foreach (TreeViewItem child in group.Items)
                {
                    bool childMatch = false;
                    // Check child header text
                    string childText = GetHeaderText(child);
                    if (childText.ToLowerInvariant().Contains(lower))
                        childMatch = true;

                    // Check grandchildren
                    bool anyGrandchildVisible = false;
                    foreach (var gc in child.Items)
                    {
                        if (gc is TreeViewItem gcItem)
                        {
                            string gcText = GetHeaderText(gcItem);
                            bool gcMatch = gcText.ToLowerInvariant().Contains(lower);
                            gcItem.Visibility = gcMatch ? Visibility.Visible : Visibility.Collapsed;
                            if (gcMatch) anyGrandchildVisible = true;
                        }
                    }

                    child.Visibility = (childMatch || anyGrandchildVisible) ? Visibility.Visible : Visibility.Collapsed;
                    if (child.Visibility == Visibility.Visible)
                    {
                        anyChildVisible = true;
                        if (anyGrandchildVisible) child.IsExpanded = true;
                    }
                }
                group.Visibility = anyChildVisible ? Visibility.Visible : Visibility.Collapsed;
                if (anyChildVisible) group.IsExpanded = true;
            }
        }

        private static void SetAllVisible(TreeView tree)
        {
            foreach (TreeViewItem group in tree.Items)
            {
                group.Visibility = Visibility.Visible;
                foreach (var child in group.Items)
                {
                    if (child is TreeViewItem ci)
                    {
                        ci.Visibility = Visibility.Visible;
                        foreach (var gc in ci.Items)
                            if (gc is TreeViewItem gci) gci.Visibility = Visibility.Visible;
                    }
                }
            }
        }

        private static string GetHeaderText(TreeViewItem item)
        {
            if (item.Header is StackPanel sp)
            {
                foreach (var child in sp.Children)
                {
                    if (child is TextBlock tb && !string.IsNullOrEmpty(tb.Text) && tb.Text.Length > 2)
                        return tb.Text;
                }
            }
            return item.Header?.ToString() ?? "";
        }

        // ═══════════════════════════════════════════════════════════════════
        //  SELECTION HELPERS
        // ═══════════════════════════════════════════════════════════════════

        private static object GetSelectedViewTag()
        {
            var sel = _viewsTree?.SelectedItem as TreeViewItem;
            if (sel?.Tag is AllViewNode avn) return avn.Tag;
            return sel?.Tag;
        }

        private static object GetSelectedSheetTag()
        {
            var sel = _sheetsTree?.SelectedItem as TreeViewItem;
            if (sel?.Tag is SheetNode sn) return sn.Tag;
            return null;
        }

        private static object GetSelectedViewportTag()
        {
            var sel = _sheetsTree?.SelectedItem as TreeViewItem;
            if (sel?.Tag is ViewportNode vn) return vn.Tag;
            return null;
        }

        private static object GetSelectedViewportViewTag()
        {
            var sel = _sheetsTree?.SelectedItem as TreeViewItem;
            if (sel?.Tag is ViewportNode vn) return vn.ViewTag ?? vn.Tag;
            return null;
        }

        private static void UpdateStatus(string text)
        {
            if (_statusText != null)
                _statusText.Text = text;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  UI HELPERS
        // ═══════════════════════════════════════════════════════════════════

        private static TreeViewItem GetTreeViewItemUnderMouse(TreeView tree, MouseEventArgs e)
        {
            var hit = tree.InputHitTest(e.GetPosition(tree)) as DependencyObject;
            while (hit != null && !(hit is TreeViewItem))
                hit = VisualTreeHelper.GetParent(hit);
            return hit as TreeViewItem;
        }

        private static TreeViewItem GetTreeViewItemUnderDragEvent(TreeView tree, DragEventArgs e)
        {
            var hit = tree.InputHitTest(e.GetPosition(tree)) as DependencyObject;
            while (hit != null && !(hit is TreeViewItem))
                hit = VisualTreeHelper.GetParent(hit);
            return hit as TreeViewItem;
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is T))
                parent = VisualTreeHelper.GetParent(parent);
            return parent as T;
        }

        private static Border MakeColorDot(SolidColorBrush color, int size = 8)
        {
            return new Border
            {
                Width = size, Height = size,
                CornerRadius = new CornerRadius(size / 2.0),
                Background = color,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static TextBox CreateSearchBox(string placeholder)
        {
            var box = new TextBox
            {
                FontSize = 11, MinWidth = 120, Height = 24,
                VerticalContentAlignment = VerticalAlignment.Center,
                BorderBrush = BrBorder, BorderThickness = new Thickness(1),
                Foreground = BrFgDark, Background = BrBgWhite,
                Text = placeholder, FontStyle = FontStyles.Italic,
                Tag = placeholder // store placeholder
            };
            box.GotFocus += (s, e) =>
            {
                if (box.Text == (string)box.Tag)
                {
                    box.Text = "";
                    box.FontStyle = FontStyles.Normal;
                    box.Foreground = BrFgDark;
                }
            };
            box.LostFocus += (s, e) =>
            {
                if (string.IsNullOrEmpty(box.Text))
                {
                    box.Text = (string)box.Tag;
                    box.FontStyle = FontStyles.Italic;
                    box.Foreground = BrFgSubtle;
                }
            };
            return box;
        }

        private static Button CreateSmallButton(string text, SolidColorBrush color)
        {
            return new Button
            {
                Content = text, FontSize = 10,
                Padding = new Thickness(6, 2, 6, 2),
                Background = Brushes.Transparent, Foreground = color,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static Button CreateButton(string text, SolidColorBrush color)
        {
            return new Button
            {
                Content = text, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(12, 6, 12, 6),
                Background = color, Foreground = BrFgWhite,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
        }

        // ── Ribbon helpers ───────────────────────────────────────────────

        private static TextBlock MakeRibbonLabel(string text)
        {
            return new TextBlock
            {
                Text = text, FontSize = 10, FontWeight = FontWeights.Bold,
                Foreground = BrFgSubtle, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 4, 0)
            };
        }

        private static Border MakeRibbonSep()
        {
            return new Border
            {
                Width = 1, Height = 18, Background = BrBorder,
                Margin = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        // ═══════════════════════════════════════════════════════════════════
        //  MULTI-SELECTION — Shift/Ctrl + Click on TreeView items
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Handles Shift/Ctrl multi-selection on TreeView items.
        /// Ctrl+Click toggles individual items.
        /// Shift+Click selects range from last clicked to current.
        /// </summary>
        private static void HandleMultiSelect(TreeView tree, MouseButtonEventArgs e,
            HashSet<TreeViewItem> selectedSet, ref TreeViewItem lastClicked)
        {
            var item = GetTreeViewItemUnderMouse(tree, e);
            if (item == null || item.Tag == null) return;

            bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            if (isCtrl)
            {
                // Toggle selection of this item
                if (selectedSet.Contains(item))
                {
                    selectedSet.Remove(item);
                    item.Background = Brushes.Transparent;
                }
                else
                {
                    selectedSet.Add(item);
                    item.Background = BrSelected;
                }
                lastClicked = item;
                UpdateMultiSelectStatus(selectedSet);
            }
            else if (isShift && lastClicked != null)
            {
                // Select range from last clicked to current
                var flatList = FlattenTreeItems(tree);
                int startIdx = flatList.IndexOf(lastClicked);
                int endIdx = flatList.IndexOf(item);
                if (startIdx >= 0 && endIdx >= 0)
                {
                    int lo = Math.Min(startIdx, endIdx);
                    int hi = Math.Max(startIdx, endIdx);
                    for (int i = lo; i <= hi; i++)
                    {
                        if (flatList[i].Tag != null)
                        {
                            selectedSet.Add(flatList[i]);
                            flatList[i].Background = BrSelected;
                        }
                    }
                }
                UpdateMultiSelectStatus(selectedSet);
            }
            else
            {
                // Normal click — clear previous selection
                ClearMultiSelect(selectedSet);
                selectedSet.Add(item);
                item.Background = BrSelected;
                lastClicked = item;
            }
        }

        private static void ClearMultiSelect(HashSet<TreeViewItem> selectedSet)
        {
            foreach (var item in selectedSet)
                item.Background = Brushes.Transparent;
            selectedSet.Clear();
        }

        private static List<TreeViewItem> FlattenTreeItems(TreeView tree)
        {
            var result = new List<TreeViewItem>();
            foreach (TreeViewItem item in tree.Items)
                FlattenRecursive(item, result);
            return result;
        }

        private static void FlattenRecursive(TreeViewItem item, List<TreeViewItem> result)
        {
            result.Add(item);
            foreach (TreeViewItem child in item.Items)
                FlattenRecursive(child, result);
        }

        private static void UpdateMultiSelectStatus(HashSet<TreeViewItem> selectedSet)
        {
            if (selectedSet.Count > 1)
                UpdateStatus($"\u2714 {selectedSet.Count} items selected (Ctrl+Click to toggle, Shift+Click for range)");
        }

        /// <summary>Gets all multi-selected element IDs from the current selection set.</summary>
        internal static List<object> GetMultiSelectedTags(HashSet<TreeViewItem> selectedSet)
        {
            var tags = new List<object>();
            foreach (var item in selectedSet)
            {
                if (item.Tag is AllViewNode avn) tags.Add(avn.Tag);
                else if (item.Tag is SheetNode sn) tags.Add(sn.Tag);
                else if (item.Tag is ViewportNode vn) tags.Add(vn.Tag);
                else if (item.Tag is UnplacedViewNode un) tags.Add(un.Tag);
                else if (item.Tag != null) tags.Add(item.Tag);
            }
            return tags;
        }

        private static Button MakeToolBtn(string label, string tag, SolidColorBrush color, string tooltip = null)
        {
            var btn = new Button
            {
                Content = label, Tag = tag,
                FontSize = 10, Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(1), Cursor = Cursors.Hand,
                Background = BrBgLight, Foreground = color,
                BorderBrush = BrBorder, BorderThickness = new Thickness(1),
                ToolTip = tooltip ?? tag
            };
            btn.MouseEnter += (s, e) => btn.Background = BrHover;
            btn.MouseLeave += (s, e) => btn.Background = BrBgLight;
            btn.Click += (s, e) => ExecuteOp(tag);
            return btn;
        }
    }
}
