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
    /// Naviate/Ideate-style dual-panel modeless WPF Sheet Manager dialog.
    /// Left panel: Views browser (unplaced + all views, color-coded by type).
    /// Right panel: Sheets browser (sheets grouped by discipline, with viewport children).
    /// Operations execute LIVE via IExternalEventHandler — no queue-then-apply.
    /// Double-click opens/activates a view or sheet in Revit.
    /// Drag-drop places views on sheets with immediate visual feedback.
    /// Viewports can be dragged between sheets.
    /// All sheet and view tools are integrated into the toolbar.
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
        // View type colours
        private static readonly SolidColorBrush BrPlan = new(Color.FromRgb(0x42, 0xA5, 0xF5));
        private static readonly SolidColorBrush BrSection = new(Color.FromRgb(0xAB, 0x47, 0xBC));
        private static readonly SolidColorBrush BrElevation = new(Color.FromRgb(0x26, 0xA6, 0x9A));
        private static readonly SolidColorBrush BrLegend = new(Color.FromRgb(0x66, 0xBB, 0x6A));
        private static readonly SolidColorBrush BrDrafting = new(Color.FromRgb(0xFF, 0xA7, 0x26));
        private static readonly SolidColorBrush BrSchedule = new(Color.FromRgb(0x78, 0x90, 0x9C));
        private static readonly SolidColorBrush BrThreeD = new(Color.FromRgb(0xEF, 0x53, 0x50));

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

        /// <summary>
        /// Show the Sheet Manager as a modeless floating window.
        /// Operations execute immediately via the callback.
        /// </summary>
        /// <param name="sheets">Initial sheet data.</param>
        /// <param name="unplacedViews">Initial unplaced views.</param>
        /// <param name="allViews">Initial all-views list.</param>
        /// <param name="executeCallback">Called to execute an operation on the Revit API thread.
        ///   Receives (operationName, options dictionary).</param>
        /// <param name="refreshSheets">Called to rebuild sheet data after an operation.</param>
        /// <param name="refreshUnplaced">Called to rebuild unplaced view data.</param>
        /// <param name="refreshAllViews">Called to rebuild all-views data.</param>
        public static void ShowModeless(
            List<SheetNode> sheets,
            List<UnplacedViewNode> unplacedViews,
            List<AllViewNode> allViews,
            Action<string, Dictionary<string, object>> executeCallback,
            Func<List<SheetNode>> refreshSheets = null,
            Func<List<UnplacedViewNode>> refreshUnplaced = null,
            Func<List<AllViewNode>> refreshAllViews = null)
        {
            // If already open, bring to front
            if (_window != null && _window.IsVisible)
            {
                _window.Activate();
                return;
            }

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
                UpdateStatus("Ready — data refreshed.");
            }
            catch (Exception ex) { StingLog.Warn($"SheetManager RefreshData: {ex.Message}"); }
        }

        /// <summary>Close the sheet manager if open.</summary>
        public static void CloseIfOpen()
        {
            if (_window != null && _window.IsVisible)
            {
                try { _window.Close(); } catch (Exception) { }
            }
            _window = null;
        }

        /// <summary>True if the modeless window is open.</summary>
        public static bool IsOpen => _window != null && _window.IsVisible;

        private static bool IsModeless => _executeCallback != null;



        // ═══════════════════════════════════════════════════════════════════
        //  WINDOW BUILDER
        // ═══════════════════════════════════════════════════════════════════

        private static void BuildWindow(SheetManagerResult modalResult, bool modal)
        {
            _window = new Window
            {
                Title = "STING Sheet Manager",
                Width = 1200,
                Height = 750,
                MinWidth = 900,
                MinHeight = 550,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = BrBgLight,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                Topmost = !modal, // float above Revit when modeless
                ShowInTaskbar = !modal,
            };

            if (!modal)
            {
                // Allow user to toggle topmost
                _window.Deactivated += (s, e) => { if (_window != null) _window.Topmost = false; };
                _window.Activated += (s, e) => { /* user can re-pin via title bar */ };
            }

            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(_window);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception) { /* non-critical */ }

            var root = new DockPanel { LastChildFill = true };

            // ── Header bar ──────────────────────────────────────────────
            var header = CreateHeader();
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // ── Tools ribbon ────────────────────────────────────────────
            var toolsBar = CreateToolsRibbon();
            DockPanel.SetDock(toolsBar, Dock.Top);
            root.Children.Add(toolsBar);

            // ── Bottom action bar ───────────────────────────────────────
            var bottomBar = CreateBottomBar(modalResult);
            DockPanel.SetDock(bottomBar, Dock.Bottom);
            root.Children.Add(bottomBar);

            // ── Main content: Views (left) | Splitter | Sheets (right) ─
            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 300 });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star), MinWidth = 350 });

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

        /// <summary>Build AllViewNode list from unplaced + sheet viewport data when caller doesn't provide it.</summary>
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
                Text = "STING Sheet Manager",
                FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = BrAccent, VerticalAlignment = VerticalAlignment.Center
            });
            _headerStats = new TextBlock
            {
                Text = $"  |  {_sheetNodes.Count} sheets  \u2022  {_unplacedViews.Count} unplaced  \u2022  {_allViews.Count} total views",
                FontSize = 12, Foreground = BrFgWhite,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0)
            };
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
            if (_headerStats != null)
                _headerStats.Text = $"  |  {_sheetNodes.Count} sheets  \u2022  {_unplacedViews.Count} unplaced  \u2022  {_allViews.Count} total views";
        }


        // ═══════════════════════════════════════════════════════════════════
        //  TOOLS RIBBON — integrated sheet & view commands
        // ═══════════════════════════════════════════════════════════════════

        private static Border CreateToolsRibbon()
        {
            var border = new Border
            {
                Background = BrBgWhite,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(8, 4, 8, 4)
            };

            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };

            // ── Sheet Tools ──
            wrap.Children.Add(MakeRibbonLabel("SHEETS:"));
            wrap.Children.Add(MakeToolBtn("+ New Sheet", "CreateSheet", BrGreen));
            wrap.Children.Add(MakeToolBtn("Clone", "CloneSheet", BrBlue));
            wrap.Children.Add(MakeToolBtn("Place Unplaced", "AutoPlaceUnplaced", BrGreen));
            wrap.Children.Add(MakeToolBtn("Batch Clone", "BatchCloneSheets", BrBlue));
            wrap.Children.Add(MakeToolBtn("Renumber", "BatchRenumberSheets", BrAccent));
            wrap.Children.Add(MakeToolBtn("From Template", "CreateFromTemplate", BrBlue));
            wrap.Children.Add(MakeToolBtn("Save Template", "SaveSheetTemplate", BrFgSubtle));

            wrap.Children.Add(MakeRibbonSep());

            // ── Layout Tools ──
            wrap.Children.Add(MakeRibbonLabel("LAYOUT:"));
            wrap.Children.Add(MakeToolBtn("Auto Layout", "AutoLayout", BrAccent));
            wrap.Children.Add(MakeToolBtn("MaxRects", "MaxRectsLayout", BrAccent));
            wrap.Children.Add(MakeToolBtn("Grid Align", "GridAlignViewports", BrBlue));
            wrap.Children.Add(MakeToolBtn("Align Edges", "AlignViewportEdges", BrBlue));
            wrap.Children.Add(MakeToolBtn("Distribute", "DistributeViewports", BrBlue));
            wrap.Children.Add(MakeToolBtn("Batch Arrange", "BatchArrange", BrAccent));
            wrap.Children.Add(MakeToolBtn("Save Preset", "SaveLayoutPreset", BrFgSubtle));
            wrap.Children.Add(MakeToolBtn("Apply Preset", "ApplyLayoutPreset", BrBlue));

            wrap.Children.Add(MakeRibbonSep());

            // ── View Tools ──
            wrap.Children.Add(MakeRibbonLabel("VIEWS:"));
            wrap.Children.Add(MakeToolBtn("Duplicate", "DuplicateView", BrBlue));
            wrap.Children.Add(MakeToolBtn("Batch Rename", "BatchRenameViews", BrBlue));
            wrap.Children.Add(MakeToolBtn("Copy Settings", "CopyViewSettings", BrBlue));
            wrap.Children.Add(MakeToolBtn("Crop to Content", "CropToContent", BrAccent));
            wrap.Children.Add(MakeToolBtn("Auto VP Types", "AutoAssignVPTypes", BrAccent));

            wrap.Children.Add(MakeRibbonSep());

            // ── QA/Export Tools ──
            wrap.Children.Add(MakeRibbonLabel("QA:"));
            wrap.Children.Add(MakeToolBtn("Audit", "SheetAudit", BrBlue));
            wrap.Children.Add(MakeToolBtn("ISO Check", "SheetComplianceCheck", BrGreen));
            wrap.Children.Add(MakeToolBtn("Export CSV", "ExportSheetSet", BrFgSubtle));
            wrap.Children.Add(MakeToolBtn("Register", "ExportSheetRegister", BrFgSubtle));
            wrap.Children.Add(MakeToolBtn("Batch PDF", "BatchPrintSheets", BrRed));

            border.Child = wrap;
            return border;
        }

        private static TextBlock MakeRibbonLabel(string text)
        {
            return new TextBlock
            {
                Text = text, FontSize = 10, FontWeight = FontWeights.Bold,
                Foreground = BrFgSubtle, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 4, 0)
            };
        }

        private static Border MakeRibbonSep()
        {
            return new Border
            {
                Width = 1, Background = BrBorder, Margin = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Stretch
            };
        }

        private static Button MakeToolBtn(string label, string cmdTag, SolidColorBrush fg)
        {
            var btn = new Button
            {
                Content = label, FontSize = 10, Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(1), Background = BrBgLight,
                Foreground = fg, BorderBrush = BrBorder, BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand, ToolTip = cmdTag
            };
            btn.MouseEnter += (s, e) => btn.Background = BrHover;
            btn.MouseLeave += (s, e) => btn.Background = BrBgLight;
            string tag = cmdTag;
            btn.Click += (s, e) => ExecuteOp(tag);
            return btn;
        }



        // ═══════════════════════════════════════════════════════════════════
        //  LEFT PANEL — Views Browser
        // ═══════════════════════════════════════════════════════════════════

        private static Border CreateViewsPanel()
        {
            var border = new Border
            {
                Background = BrBgWhite,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 0, 1, 0),
            };

            var dock = new DockPanel { LastChildFill = true };

            // Panel header
            var panelHeader = new Border
            {
                Background = BrBgHeader, Padding = new Thickness(10, 6, 10, 6)
            };
            var headerRow = new DockPanel();
            headerRow.Children.Add(new TextBlock
            {
                Text = "VIEWS",
                FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = BrAccent, VerticalAlignment = VerticalAlignment.Center
            });

            // Browser mode dropdown
            _viewBrowserMode = new ComboBox
            {
                Width = 140, FontSize = 11, Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _viewBrowserMode.Items.Add("Not on Sheets");
            _viewBrowserMode.Items.Add("All Views");
            _viewBrowserMode.Items.Add("By Type");
            _viewBrowserMode.Items.Add("By Discipline");
            _viewBrowserMode.Items.Add("By Level");
            _viewBrowserMode.SelectedIndex = 0;
            _viewBrowserMode.SelectionChanged += (s, e) => RebuildViewsTree();
            DockPanel.SetDock(_viewBrowserMode, Dock.Right);
            headerRow.Children.Add(_viewBrowserMode);

            panelHeader.Child = headerRow;
            DockPanel.SetDock(panelHeader, Dock.Top);
            dock.Children.Add(panelHeader);

            // Toolbar
            var toolbar = new StackPanel { Margin = new Thickness(8, 6, 8, 2) };
            var searchBox = CreateSearchBox("Search views...");
            searchBox.TextChanged += (s, e) =>
            {
                string f = searchBox.Foreground == BrFgSubtle ? "" : searchBox.Text;
                FilterTree(_viewsTree, f);
            };
            toolbar.Children.Add(searchBox);

            _hidePlacedCheck = new CheckBox
            {
                Content = "Hide views already placed on sheets",
                FontSize = 11, Margin = new Thickness(2, 4, 0, 2),
                IsChecked = true, Foreground = BrFgDark
            };
            _hidePlacedCheck.Checked += (s, e) => RebuildViewsTree();
            _hidePlacedCheck.Unchecked += (s, e) => RebuildViewsTree();
            toolbar.Children.Add(_hidePlacedCheck);

            DockPanel.SetDock(toolbar, Dock.Top);
            dock.Children.Add(toolbar);

            // View action buttons
            var btnRow = new WrapPanel { Margin = new Thickness(8, 2, 8, 4) };
            var btnPlace = CreateSmallButton("Place on Sheet \u25B6", BrGreen);
            btnPlace.Click += (s, e) => PlaceSelectedViewOnSheet();
            btnRow.Children.Add(btnPlace);

            var btnPlaceNew = CreateSmallButton("+ Place on New Sheet", BrBlue);
            btnPlaceNew.Click += (s, e) =>
            {
                var vt = GetSelectedViewTag();
                if (vt == null) { UpdateStatus("Select a view first."); return; }
                ExecuteOp("PlaceOnNewSheet", new Dictionary<string, object> { { "SelectedTag", vt } });
            };
            btnRow.Children.Add(btnPlaceNew);

            DockPanel.SetDock(btnRow, Dock.Top);
            dock.Children.Add(btnRow);

            // TreeView
            _viewsTree = new TreeView
            {
                Margin = new Thickness(4), BorderThickness = new Thickness(0),
                Background = BrBgWhite, AllowDrop = false
            };
            _viewsTree.PreviewMouseLeftButtonDown += ViewsTree_PreviewMouseLeftButtonDown;
            _viewsTree.MouseMove += ViewsTree_MouseMove;
            _viewsTree.PreviewMouseDoubleClick += ViewsTree_DoubleClick;
            BuildViewsTreeContent();

            _viewsTree.ContextMenu = CreateViewsContextMenu();

            dock.Children.Add(_viewsTree);
            border.Child = dock;
            return border;
        }

        private static void BuildViewsTreeContent()
        {
            _viewsTree.Items.Clear();
            string mode = _viewBrowserMode?.SelectedItem?.ToString() ?? "Not on Sheets";
            bool hidePlaced = _hidePlacedCheck?.IsChecked == true;

            var views = _allViews.AsEnumerable();
            if (hidePlaced || mode == "Not on Sheets")
                views = views.Where(v => !v.IsPlaced);

            var viewList = views.OrderBy(v => v.ViewName).ToList();

            Func<AllViewNode, string> grouper = mode switch
            {
                "By Discipline" => v => v.Discipline ?? "General",
                "By Level" => v => v.Level ?? "No Level",
                _ => v => v.ViewType ?? "Unknown"
            };

            foreach (var g in viewList.GroupBy(grouper).OrderBy(g => g.Key))
            {
                var groupItem = MakeGroupNode($"{g.Key} ({g.Count()})", $"V:{g.Key}");
                foreach (var v in g)
                    groupItem.Items.Add(MakeViewNode(v));
                _viewsTree.Items.Add(groupItem);
            }

            UpdateStatus($"{viewList.Count} views shown");
        }

        private static void RebuildViewsTree()
        {
            if (_viewsTree == null) return;
            BuildViewsTreeContent();
        }



        // ═══════════════════════════════════════════════════════════════════
        //  RIGHT PANEL — Sheets Browser
        // ═══════════════════════════════════════════════════════════════════

        private static Border CreateSheetsPanel()
        {
            var border = new Border { Background = BrBgWhite };

            var dock = new DockPanel { LastChildFill = true };

            // Panel header
            var panelHeader = new Border
            {
                Background = BrBgHeader, Padding = new Thickness(10, 6, 10, 6)
            };
            var headerRow = new DockPanel();
            headerRow.Children.Add(new TextBlock
            {
                Text = "SHEETS",
                FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = BrAccent, VerticalAlignment = VerticalAlignment.Center
            });

            _sheetBrowserMode = new ComboBox
            {
                Width = 140, FontSize = 11, Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _sheetBrowserMode.Items.Add("By Discipline");
            _sheetBrowserMode.Items.Add("All Sheets");
            _sheetBrowserMode.Items.Add("By Paper Size");
            _sheetBrowserMode.SelectedIndex = 0;
            _sheetBrowserMode.SelectionChanged += (s, e) => RebuildSheetsTree();
            DockPanel.SetDock(_sheetBrowserMode, Dock.Right);
            headerRow.Children.Add(_sheetBrowserMode);

            panelHeader.Child = headerRow;
            DockPanel.SetDock(panelHeader, Dock.Top);
            dock.Children.Add(panelHeader);

            // Toolbar
            var toolbar = new StackPanel { Margin = new Thickness(8, 6, 8, 2) };
            var searchBox = CreateSearchBox("Search sheets...");
            searchBox.TextChanged += (s, e) =>
            {
                string f = searchBox.Foreground == BrFgSubtle ? "" : searchBox.Text;
                FilterTree(_sheetsTree, f);
            };
            toolbar.Children.Add(searchBox);
            DockPanel.SetDock(toolbar, Dock.Top);
            dock.Children.Add(toolbar);

            // Sheet action buttons
            var btnRow = new WrapPanel { Margin = new Thickness(8, 2, 8, 4) };

            var btnNewSheet = CreateSmallButton("+ New Sheet", BrGreen);
            btnNewSheet.Click += (s, e) => ExecuteOp("CreateSheet");
            btnRow.Children.Add(btnNewSheet);

            var btnClone = CreateSmallButton("Clone Sheet", BrBlue);
            btnClone.Click += (s, e) =>
            {
                var sel = GetSelectedSheetTag();
                if (sel != null)
                    ExecuteOp("CloneSheet", new Dictionary<string, object> { { "SelectedTag", sel } });
                else
                    UpdateStatus("Select a sheet to clone.");
            };
            btnRow.Children.Add(btnClone);

            // Auto Layout dropdown
            var btnLayout = CreateSmallButton("Auto Layout \u25BC", BrAccent);
            btnLayout.Click += (s, e) => ShowAutoLayoutMenu(btnLayout);
            btnRow.Children.Add(btnLayout);

            var btnPlaceUnplaced = CreateSmallButton("Place Unplaced", BrGreen);
            btnPlaceUnplaced.Click += (s, e) => ExecuteOp("AutoPlaceUnplaced");
            btnRow.Children.Add(btnPlaceUnplaced);

            DockPanel.SetDock(btnRow, Dock.Top);
            dock.Children.Add(btnRow);

            // TreeView — drop target for views AND viewports
            _sheetsTree = new TreeView
            {
                Margin = new Thickness(4), BorderThickness = new Thickness(0),
                Background = BrBgWhite, AllowDrop = true
            };
            _sheetsTree.Drop += SheetsTree_Drop;
            _sheetsTree.DragOver += SheetsTree_DragOver;
            _sheetsTree.DragLeave += SheetsTree_DragLeave;
            _sheetsTree.PreviewMouseLeftButtonDown += SheetsTree_PreviewMouseLeftButtonDown;
            _sheetsTree.MouseMove += SheetsTree_MouseMove;
            _sheetsTree.PreviewMouseDoubleClick += SheetsTree_DoubleClick;
            BuildSheetsTreeContent();

            _sheetsTree.ContextMenu = CreateSheetsContextMenu();

            dock.Children.Add(_sheetsTree);
            border.Child = dock;
            return border;
        }

        private static void BuildSheetsTreeContent()
        {
            _sheetsTree.Items.Clear();
            string mode = _sheetBrowserMode?.SelectedItem?.ToString() ?? "By Discipline";

            IEnumerable<IGrouping<string, SheetNode>> grouped = mode switch
            {
                "All Sheets" => _sheetNodes.GroupBy(s => "All Sheets"),
                "By Paper Size" => _sheetNodes.GroupBy(s => s.PaperSize ?? "Unknown"),
                _ => _sheetNodes.GroupBy(s => s.Discipline ?? "General")
            };

            foreach (var group in grouped.OrderBy(g => g.Key))
            {
                var discItem = MakeGroupNode($"{group.Key} ({group.Count()})", $"DISC:{group.Key}");

                foreach (var sheet in group.OrderBy(s => s.SheetNumber))
                {
                    var sheetItem = MakeSheetNode(sheet);
                    foreach (var vp in sheet.Viewports)
                        sheetItem.Items.Add(MakeViewportNode(vp));
                    discItem.Items.Add(sheetItem);
                }

                _sheetsTree.Items.Add(discItem);
            }
        }

        private static void RebuildSheetsTree()
        {
            if (_sheetsTree == null) return;
            BuildSheetsTreeContent();
        }

        /// <summary>Show auto layout options as a dropdown context menu.</summary>
        private static void ShowAutoLayoutMenu(Button anchor)
        {
            var menu = new ContextMenu();

            var layouts = new[]
            {
                ("Top-Left Corner", "TopLeft"),
                ("Top-Right Corner", "TopRight"),
                ("Bottom-Left Corner", "BottomLeft"),
                ("Center", "Center"),
                ("Grid (2 columns)", "Grid2"),
                ("Grid (3 columns)", "Grid3"),
                ("Stacked (vertical)", "Stacked"),
                ("Side by Side", "SideBySide"),
                ("Default Margins (shelf-pack)", "Default"),
                ("ISO A1 Margins", "A1"),
                ("ISO A3 Margins", "A3"),
                ("Compact Margins", "Compact"),
            };

            foreach (var (label, mode) in layouts)
            {
                var mi = new MenuItem { Header = label };
                string m = mode;
                mi.Click += (s, e) =>
                {
                    var sel = GetSelectedSheetTag();
                    var opts = new Dictionary<string, object> { { "LayoutMode", m } };
                    if (sel != null) opts["SelectedTag"] = sel;
                    ExecuteOp("AutoLayoutMode", opts);
                };
                menu.Items.Add(mi);
            }

            menu.PlacementTarget = anchor;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }



        // ═══════════════════════════════════════════════════════════════════
        //  DOUBLE-CLICK — Open/Activate view or sheet in Revit
        // ═══════════════════════════════════════════════════════════════════

        private static void ViewsTree_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = GetTreeViewItemUnderMouse(_viewsTree, e);
            if (item == null) return;

            object viewId = null;
            string viewName = "";

            if (item.Tag is AllViewNode av)
            {
                viewId = av.Tag;
                viewName = av.ViewName;
            }
            else if (item.Tag is UnplacedViewNode uv)
            {
                viewId = uv.Tag;
                viewName = uv.ViewName;
            }

            if (viewId == null) return;

            e.Handled = true;
            UpdateStatus($"Opening: {viewName}...");
            ExecuteOp("ActivateView", new Dictionary<string, object> { { "ViewTag", viewId } });
        }

        private static void SheetsTree_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = GetTreeViewItemUnderMouse(_sheetsTree, e);
            if (item == null) return;

            object viewId = null;
            string name = "";

            if (item.Tag is SheetNode sn)
            {
                viewId = sn.Tag;
                name = $"{sn.SheetNumber} - {sn.SheetName}";
            }
            else if (item.Tag is ViewportNode vn)
            {
                // Double-click viewport → open the underlying view
                viewId = vn.ViewTag ?? vn.Tag;
                name = vn.ViewName;
            }

            if (viewId == null) return;

            e.Handled = true;
            UpdateStatus($"Opening: {name}...");
            ExecuteOp("ActivateView", new Dictionary<string, object> { { "ViewTag", viewId } });
        }


        // ═══════════════════════════════════════════════════════════════════
        //  DRAG & DROP — Views → Sheets (with visual feedback)
        // ═══════════════════════════════════════════════════════════════════

        private static void ViewsTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragSource = GetTreeViewItemUnderMouse(_viewsTree, e);
            _dragStartPoint = e.GetPosition(_viewsTree);
        }

        private static void ViewsTree_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragSource == null) return;

            var pos = e.GetPosition(_viewsTree);
            if (Math.Abs(pos.X - _dragStartPoint.X) < 5 && Math.Abs(pos.Y - _dragStartPoint.Y) < 5)
                return;

            object dragData = null;
            string name = "";
            if (_dragSource.Tag is AllViewNode av && !av.IsPlaced)
            {
                dragData = av;
                name = av.ViewName;
            }
            else if (_dragSource.Tag is UnplacedViewNode uv)
            {
                dragData = uv;
                name = uv.ViewName;
            }

            if (dragData == null)
            {
                if (_dragSource.Tag is AllViewNode pv && pv.IsPlaced)
                    UpdateStatus($"'{pv.ViewName}' is already placed on sheet {pv.PlacedOnSheet}. Remove it first.");
                return;
            }

            UpdateStatus($"\u2195 Dragging: {name} \u2014 drop on a sheet to place");
            DragDrop.DoDragDrop(_viewsTree, dragData, DragDropEffects.Copy);
            ClearDropHighlight();
            _dragSource = null;
        }

        // ── Drag from sheets tree (viewport cross-sheet move) ───────────

        private static void SheetsTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var item = GetTreeViewItemUnderMouse(_sheetsTree, e);
            if (item?.Tag is ViewportNode)
            {
                _dragSource = item;
                _dragStartPoint = e.GetPosition(_sheetsTree);
            }
        }

        private static void SheetsTree_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragSource == null) return;
            if (!(_dragSource.Tag is ViewportNode vn)) return;

            var pos = e.GetPosition(_sheetsTree);
            if (Math.Abs(pos.X - _dragStartPoint.X) < 5 && Math.Abs(pos.Y - _dragStartPoint.Y) < 5)
                return;

            UpdateStatus($"\u2195 Moving: {vn.ViewName} \u2014 drop on another sheet");
            DragDrop.DoDragDrop(_sheetsTree, vn, DragDropEffects.Move);
            ClearDropHighlight();
            _dragSource = null;
        }

        // ── Drop target handling ─────────────────────────────────────────

        private static void SheetsTree_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            ClearDropHighlight();

            var target = GetTreeViewItemUnderDragEvent(_sheetsTree, e);
            SheetNode targetSheet = ResolveSheetFromItem(target);

            if (targetSheet != null)
            {
                // Highlight the target sheet node
                if (target != null)
                {
                    var sheetItem = FindSheetItem(target);
                    if (sheetItem != null)
                    {
                        sheetItem.Background = BrDropHighlight;
                        sheetItem.BorderBrush = BrDropBorder;
                        sheetItem.BorderThickness = new Thickness(2);
                        _lastHighlighted = sheetItem;
                    }
                }

                // Accept views or viewport moves
                if (e.Data.GetDataPresent(typeof(AllViewNode))
                    || e.Data.GetDataPresent(typeof(UnplacedViewNode)))
                {
                    e.Effects = DragDropEffects.Copy;
                }
                else if (e.Data.GetDataPresent(typeof(ViewportNode)))
                {
                    // Don't allow dropping on same sheet
                    var vn = e.Data.GetData(typeof(ViewportNode)) as ViewportNode;
                    if (vn != null && vn.HostSheetNumber != targetSheet.SheetNumber)
                        e.Effects = DragDropEffects.Move;
                }
            }
            e.Handled = true;
        }

        private static void SheetsTree_DragLeave(object sender, DragEventArgs e)
        {
            ClearDropHighlight();
        }

        private static void SheetsTree_Drop(object sender, DragEventArgs e)
        {
            ClearDropHighlight();

            var target = GetTreeViewItemUnderDragEvent(_sheetsTree, e);
            SheetNode targetSheet = ResolveSheetFromItem(target);

            if (targetSheet == null)
            {
                UpdateStatus("Drop cancelled \u2014 target is not a sheet.");
                return;
            }

            // ── Drop: View → Sheet (place view) ─────────────────────────
            object viewTag = null;
            string viewName = "";

            if (e.Data.GetDataPresent(typeof(AllViewNode)))
            {
                var av = (AllViewNode)e.Data.GetData(typeof(AllViewNode));
                viewTag = av.Tag;
                viewName = av.ViewName;
            }
            else if (e.Data.GetDataPresent(typeof(UnplacedViewNode)))
            {
                var uv = (UnplacedViewNode)e.Data.GetData(typeof(UnplacedViewNode));
                viewTag = uv.Tag;
                viewName = uv.ViewName;
            }

            if (viewTag != null)
            {
                UpdateStatus($"\u2713 Placing '{viewName}' on sheet '{targetSheet.SheetNumber}'...");
                ExecuteOp("PlaceViewOnSheet", new Dictionary<string, object>
                {
                    { "ViewTag", viewTag },
                    { "SheetTag", targetSheet.Tag },
                    { "ViewName", viewName },
                    { "SheetNumber", targetSheet.SheetNumber }
                });
                return;
            }

            // ── Drop: Viewport → Sheet (move viewport between sheets) ───
            if (e.Data.GetDataPresent(typeof(ViewportNode)))
            {
                var vn = (ViewportNode)e.Data.GetData(typeof(ViewportNode));
                if (vn.HostSheetNumber == targetSheet.SheetNumber)
                {
                    UpdateStatus("Cannot move viewport to the same sheet.");
                    return;
                }

                UpdateStatus($"\u2713 Moving '{vn.ViewName}' to sheet '{targetSheet.SheetNumber}'...");
                ExecuteOp("MoveViewportToSheet", new Dictionary<string, object>
                {
                    { "ViewportTag", vn.Tag },
                    { "TargetSheetTag", targetSheet.Tag },
                    { "ViewportName", vn.ViewName },
                    { "TargetSheetNumber", targetSheet.SheetNumber }
                });
            }
        }

        private static SheetNode ResolveSheetFromItem(TreeViewItem item)
        {
            if (item == null) return null;
            if (item.Tag is SheetNode sn) return sn;
            if (item.Tag is ViewportNode)
            {
                var parent = item.Parent as TreeViewItem;
                return parent?.Tag as SheetNode;
            }
            return null;
        }

        private static TreeViewItem FindSheetItem(TreeViewItem item)
        {
            if (item?.Tag is SheetNode) return item;
            if (item?.Tag is ViewportNode)
                return item.Parent as TreeViewItem;
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
        //  PLACE VIEW — sheet picker from views panel
        // ═══════════════════════════════════════════════════════════════════

        private static void PlaceSelectedViewOnSheet()
        {
            var viewTag = GetSelectedViewTag();
            if (viewTag == null)
            {
                UpdateStatus("Select a view to place.");
                return;
            }

            string viewName = "";
            var sel = _viewsTree?.SelectedItem as TreeViewItem;
            if (sel?.Tag is AllViewNode av) viewName = av.ViewName;
            else if (sel?.Tag is UnplacedViewNode uv) viewName = uv.ViewName;

            var sheetItems = _sheetNodes
                .OrderBy(s => s.SheetNumber)
                .Select(s => $"{s.SheetNumber} - {s.SheetName}")
                .ToList();

            if (sheetItems.Count == 0)
            {
                UpdateStatus("No sheets in project.");
                return;
            }

            string picked = StingListPicker.Show("Place View on Sheet",
                $"Select destination sheet for '{viewName}':", sheetItems);
            if (picked == null) return;

            string targetNum = picked.Split(new[] { " - " }, StringSplitOptions.None).FirstOrDefault();
            var targetSheet = _sheetNodes.FirstOrDefault(s => s.SheetNumber == targetNum);
            if (targetSheet == null) return;

            UpdateStatus($"\u2713 Placing '{viewName}' on '{targetSheet.SheetNumber}'...");
            ExecuteOp("PlaceViewOnSheet", new Dictionary<string, object>
            {
                { "ViewTag", viewTag },
                { "SheetTag", targetSheet.Tag },
                { "ViewName", viewName },
                { "SheetNumber", targetSheet.SheetNumber }
            });
        }


        // ═══════════════════════════════════════════════════════════════════
        //  CONTEXT MENUS
        // ═══════════════════════════════════════════════════════════════════

        private static ContextMenu CreateViewsContextMenu()
        {
            var menu = new ContextMenu();

            var miOpen = new MenuItem { Header = "Open View", FontWeight = FontWeights.Bold };
            miOpen.Click += (s, e) =>
            {
                var vt = GetSelectedViewTag();
                if (vt != null) ExecuteOp("ActivateView", new Dictionary<string, object> { { "ViewTag", vt } });
            };
            menu.Items.Add(miOpen);

            menu.Items.Add(new Separator());

            var miPlace = new MenuItem { Header = "Place on Sheet..." };
            miPlace.Click += (s, e) => PlaceSelectedViewOnSheet();
            menu.Items.Add(miPlace);

            var miPlaceNew = new MenuItem { Header = "Place on New Sheet" };
            miPlaceNew.Click += (s, e) =>
            {
                var vt = GetSelectedViewTag();
                if (vt != null) ExecuteOp("PlaceOnNewSheet", new Dictionary<string, object> { { "SelectedTag", vt } });
            };
            menu.Items.Add(miPlaceNew);

            menu.Items.Add(new Separator());

            var miDup = new MenuItem { Header = "Duplicate View" };
            miDup.Click += (s, e) =>
            {
                var vt = GetSelectedViewTag();
                if (vt != null) ExecuteOp("DuplicateView", new Dictionary<string, object> { { "SelectedTag", vt } });
            };
            menu.Items.Add(miDup);

            var miCrop = new MenuItem { Header = "Crop to Content" };
            miCrop.Click += (s, e) => ExecuteOp("CropToContent");
            menu.Items.Add(miCrop);

            var miRename = new MenuItem { Header = "Batch Rename Views..." };
            miRename.Click += (s, e) => ExecuteOp("BatchRenameViews");
            menu.Items.Add(miRename);

            menu.Items.Add(new Separator());

            var miDelete = new MenuItem { Header = "Delete View", Foreground = BrRed };
            miDelete.Click += (s, e) =>
            {
                var vt = GetSelectedViewTag();
                if (vt != null) ExecuteOp("DeleteView", new Dictionary<string, object> { { "SelectedTag", vt } });
            };
            menu.Items.Add(miDelete);

            return menu;
        }

        private static ContextMenu CreateSheetsContextMenu()
        {
            var menu = new ContextMenu();

            var miOpen = new MenuItem { Header = "Open Sheet", FontWeight = FontWeights.Bold };
            miOpen.Click += (s, e) =>
            {
                var st = GetSelectedSheetTag();
                if (st != null) ExecuteOp("ActivateView", new Dictionary<string, object> { { "ViewTag", st } });
                else
                {
                    // If viewport selected, open its view
                    var vp = GetSelectedViewportViewTag();
                    if (vp != null) ExecuteOp("ActivateView", new Dictionary<string, object> { { "ViewTag", vp } });
                }
            };
            menu.Items.Add(miOpen);

            menu.Items.Add(new Separator());

            var miClone = new MenuItem { Header = "Clone Sheet" };
            miClone.Click += (s, e) =>
            {
                var st = GetSelectedSheetTag();
                if (st != null) ExecuteOp("CloneSheet", new Dictionary<string, object> { { "SelectedTag", st } });
            };
            menu.Items.Add(miClone);

            var miArrange = new MenuItem { Header = "Arrange Viewports" };
            miArrange.Click += (s, e) =>
            {
                var sel = GetSelectedSheetTag();
                if (sel != null) ExecuteOp("ArrangeOnSheet", new Dictionary<string, object> { { "SelectedTag", sel } });
            };
            menu.Items.Add(miArrange);

            var miScale = new MenuItem { Header = "Auto-Scale Viewports" };
            miScale.Click += (s, e) =>
            {
                var sel = GetSelectedSheetTag();
                if (sel != null) ExecuteOp("AutoScaleSheet", new Dictionary<string, object> { { "SelectedTag", sel } });
            };
            menu.Items.Add(miScale);

            var miGridAlign = new MenuItem { Header = "Grid Align Viewports" };
            miGridAlign.Click += (s, e) => ExecuteOp("GridAlignViewports");
            menu.Items.Add(miGridAlign);

            var miAlignEdges = new MenuItem { Header = "Align Viewport Edges" };
            miAlignEdges.Click += (s, e) => ExecuteOp("AlignViewportEdges");
            menu.Items.Add(miAlignEdges);

            var miDistribute = new MenuItem { Header = "Distribute Viewports" };
            miDistribute.Click += (s, e) => ExecuteOp("DistributeViewports");
            menu.Items.Add(miDistribute);

            menu.Items.Add(new Separator());

            var miRenumber = new MenuItem { Header = "Renumber Sheets..." };
            miRenumber.Click += (s, e) => ExecuteOp("BatchRenumberSheets");
            menu.Items.Add(miRenumber);

            var miISO = new MenuItem { Header = "ISO 19650 Compliance Check" };
            miISO.Click += (s, e) => ExecuteOp("SheetComplianceCheck");
            menu.Items.Add(miISO);

            menu.Items.Add(new Separator());

            // Viewport-level actions
            var miMoveVp = new MenuItem { Header = "Move Viewport to Sheet \u25B6" };
            miMoveVp.Click += (s, e) => ShowMoveViewportPicker();
            menu.Items.Add(miMoveVp);

            var miRemoveVp = new MenuItem { Header = "Remove Viewport from Sheet", Foreground = BrRed };
            miRemoveVp.Click += (s, e) =>
            {
                var vpTag = GetSelectedViewportTag();
                if (vpTag != null) ExecuteOp("RemoveViewport", new Dictionary<string, object> { { "SelectedTag", vpTag } });
            };
            menu.Items.Add(miRemoveVp);

            return menu;
        }

        /// <summary>Show sheet picker for moving a viewport to a different sheet.</summary>
        private static void ShowMoveViewportPicker()
        {
            var vpTag = GetSelectedViewportTag();
            if (vpTag == null)
            {
                UpdateStatus("Select a viewport (view under a sheet) to move.");
                return;
            }

            string vpName = "";
            string currentSheet = "";
            var sel = _sheetsTree?.SelectedItem as TreeViewItem;
            if (sel?.Tag is ViewportNode vn)
            {
                vpName = vn.ViewName;
                currentSheet = vn.HostSheetNumber;
            }

            var sheetItems = _sheetNodes
                .Where(s => s.SheetNumber != currentSheet)
                .OrderBy(s => s.SheetNumber)
                .Select(s => $"{s.SheetNumber} - {s.SheetName}")
                .ToList();

            if (sheetItems.Count == 0)
            {
                UpdateStatus("No other sheets to move to.");
                return;
            }

            string picked = StingListPicker.Show("Move Viewport",
                $"Move '{vpName}' from {currentSheet} to:", sheetItems);
            if (picked == null) return;

            string targetNum = picked.Split(new[] { " - " }, StringSplitOptions.None).FirstOrDefault();
            var targetSheet = _sheetNodes.FirstOrDefault(s => s.SheetNumber == targetNum);
            if (targetSheet == null) return;

            UpdateStatus($"\u2713 Moving '{vpName}' to '{targetSheet.SheetNumber}'...");
            ExecuteOp("MoveViewportToSheet", new Dictionary<string, object>
            {
                { "ViewportTag", vpTag },
                { "TargetSheetTag", targetSheet.Tag },
                { "ViewportName", vpName },
                { "TargetSheetNumber", targetSheet.SheetNumber }
            });
        }



        // ═══════════════════════════════════════════════════════════════════
        //  BOTTOM ACTION BAR
        // ═══════════════════════════════════════════════════════════════════

        private static Border CreateBottomBar(SheetManagerResult finalResult)
        {
            var border = new Border
            {
                Background = BrBgLight,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 8, 12, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                Text = IsModeless
                    ? "Double-click to open. Drag views to sheets. Right-click for actions."
                    : "Drag views to sheets. Right-click for actions. Operations queue until Apply.",
                FontSize = 11, Foreground = BrFgSubtle,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap, MaxWidth = 550
            };
            Grid.SetColumn(_statusText, 0);
            grid.Children.Add(_statusText);

            var btnStack = new StackPanel { Orientation = Orientation.Horizontal };

            if (IsModeless)
            {
                var btnRefresh = CreateButton("\u21BB Refresh", BrBlue, BrFgWhite);
                btnRefresh.Click += (s, e) => RefreshData();
                btnStack.Children.Add(btnRefresh);

                var btnClose = CreateButton("Close", BrBorder, BrFgDark);
                btnClose.Click += (s, e) => _window.Close();
                btnStack.Children.Add(btnClose);
            }
            else
            {
                // Legacy modal mode — keep Apply & Close pattern
                var btnBatchPlace = CreateButton("Place All Unplaced", BrGreen, BrFgWhite);
                btnBatchPlace.Click += (s, e) => ExecuteOp("AutoPlaceUnplaced");
                btnStack.Children.Add(btnBatchPlace);

                var btnAudit = CreateButton("Audit", BrBlue, BrFgWhite);
                btnAudit.Click += (s, e) => ExecuteOp("SheetAudit");
                btnStack.Children.Add(btnAudit);

                var btnApply = CreateButton("Apply && Close", BrAccent, BrFgWhite);
                btnApply.Click += (s, e) =>
                {
                    if (finalResult != null) _window.DialogResult = true;
                };
                btnStack.Children.Add(btnApply);

                var btnClose = CreateButton("Close", BrBorder, BrFgDark);
                btnClose.Click += (s, e) =>
                {
                    if (finalResult != null) _window.DialogResult = false;
                };
                btnStack.Children.Add(btnClose);
            }

            Grid.SetColumn(btnStack, 1);
            grid.Children.Add(btnStack);

            border.Child = grid;
            return border;
        }


        // ═══════════════════════════════════════════════════════════════════
        //  EXECUTE OPERATION — live or queued
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Execute an operation immediately (modeless) or dispatch via dockable panel.</summary>
        private static void ExecuteOp(string operation, Dictionary<string, object> options = null)
        {
            if (string.IsNullOrEmpty(operation)) return;
            options ??= new Dictionary<string, object>();

            if (IsModeless)
            {
                // Live execution via callback
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
                // Dispatch via StingDockPanel's external event
                if (StingDockPanel.DispatchCommand(operation))
                    UpdateStatus($"Running: {operation}...");
                else
                    UpdateStatus($"Cannot run {operation} \u2014 Revit may be busy.");
            }
        }



        // ═══════════════════════════════════════════════════════════════════
        //  TREE NODE BUILDERS
        // ═══════════════════════════════════════════════════════════════════

        private static TreeViewItem MakeGroupNode(string text, string tag)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13, Foreground = BrFgDark
            });
            return new TreeViewItem { Header = sp, Tag = tag, IsExpanded = true };
        }

        private static TreeViewItem MakeViewNode(AllViewNode v)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };

            // Color-coded type indicator
            sp.Children.Add(new Border
            {
                Width = 8, Height = 8,
                Background = GetViewTypeColor(v.ViewType),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            sp.Children.Add(new TextBlock
            {
                Text = v.ViewName,
                FontSize = 12,
                Foreground = v.IsPlaced ? BrFgSubtle : BrFgDark,
                FontStyle = v.IsPlaced ? FontStyles.Italic : FontStyles.Normal
            });

            if (!string.IsNullOrEmpty(v.Scale))
            {
                sp.Children.Add(new TextBlock
                {
                    Text = $"  1:{v.Scale}", FontSize = 10, Foreground = BrFgSubtle,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            if (v.IsPlaced && !string.IsNullOrEmpty(v.PlacedOnSheet))
            {
                sp.Children.Add(new TextBlock
                {
                    Text = $"  [{v.PlacedOnSheet}]", FontSize = 10, Foreground = BrAccent,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            var item = new TreeViewItem { Header = sp, Tag = v };
            item.ToolTip = $"{v.ViewType} | Scale 1:{v.Scale}" +
                (v.IsPlaced ? $" | On sheet {v.PlacedOnSheet}" : " | Not placed");
            return item;
        }

        private static TreeViewItem MakeSheetNode(SheetNode sheet)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };

            sp.Children.Add(new TextBlock
            {
                Text = sheet.SheetNumber,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12, Foreground = BrAccent, MinWidth = 65
            });

            sp.Children.Add(new TextBlock
            {
                Text = $" - {sheet.SheetName}",
                FontSize = 12, Foreground = BrFgDark
            });

            sp.Children.Add(new TextBlock
            {
                Text = $"  [{sheet.ViewportCount}vp, {sheet.PaperSize ?? "?"}]",
                FontSize = 10, Foreground = BrFgSubtle,
                VerticalAlignment = VerticalAlignment.Center
            });

            var item = new TreeViewItem { Header = sp, Tag = sheet, IsExpanded = false };
            item.ToolTip = $"Title block: {sheet.TitleBlockName}\nDrawable: {sheet.DrawableArea}\n{sheet.ViewportCount} viewport(s)";
            return item;
        }

        private static TreeViewItem MakeViewportNode(ViewportNode vp)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };

            sp.Children.Add(new TextBlock
            {
                Text = "  \u21B3 ", FontSize = 11, Foreground = BrFgSubtle
            });
            sp.Children.Add(new TextBlock
            {
                Text = vp.ViewName, FontSize = 11, Foreground = BrFgDark
            });
            sp.Children.Add(new TextBlock
            {
                Text = $"  (1:{vp.Scale})", FontSize = 10, Foreground = BrFgSubtle,
                VerticalAlignment = VerticalAlignment.Center
            });

            var item = new TreeViewItem { Header = sp, Tag = vp };
            item.ToolTip = $"View: {vp.ViewName}\nScale: 1:{vp.Scale}\nSize: {vp.PaperSize}\nSheet: {vp.HostSheetNumber}\n\nDouble-click to open view\nDrag to another sheet to move";
            return item;
        }

        private static SolidColorBrush GetViewTypeColor(string viewType)
        {
            if (string.IsNullOrEmpty(viewType)) return BrFgSubtle;
            string vt = viewType.ToLowerInvariant();
            if (vt.Contains("floor") || vt.Contains("plan") || vt.Contains("ceiling")) return BrPlan;
            if (vt.Contains("section")) return BrSection;
            if (vt.Contains("elevation")) return BrElevation;
            if (vt.Contains("legend")) return BrLegend;
            if (vt.Contains("drafting") || vt.Contains("detail")) return BrDrafting;
            if (vt.Contains("schedule") || vt.Contains("report")) return BrSchedule;
            if (vt.Contains("3d") || vt.Contains("three")) return BrThreeD;
            return BrFgSubtle;
        }


        // ═══════════════════════════════════════════════════════════════════
        //  SEARCH / FILTER
        // ═══════════════════════════════════════════════════════════════════

        private static void FilterTree(TreeView tree, string filter)
        {
            if (tree == null) return;
            if (string.IsNullOrWhiteSpace(filter)) { SetAllVisible(tree); return; }

            string lower = filter.ToLowerInvariant();
            foreach (TreeViewItem groupItem in tree.Items)
            {
                bool anyChildVisible = false;
                foreach (TreeViewItem child in groupItem.Items)
                {
                    bool match = false;
                    if (child.Tag is SheetNode sn)
                        match = sn.SheetNumber.ToLowerInvariant().Contains(lower) || sn.SheetName.ToLowerInvariant().Contains(lower);
                    else if (child.Tag is AllViewNode av)
                        match = av.ViewName.ToLowerInvariant().Contains(lower);
                    else if (child.Tag is UnplacedViewNode uv)
                        match = uv.ViewName.ToLowerInvariant().Contains(lower);

                    child.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                    if (match) anyChildVisible = true;

                    foreach (TreeViewItem gc in child.Items)
                    {
                        bool gcMatch = false;
                        if (gc.Tag is ViewportNode vpChild)
                            gcMatch = vpChild.ViewName.ToLowerInvariant().Contains(lower);
                        gc.Visibility = gcMatch ? Visibility.Visible : Visibility.Collapsed;
                        if (gcMatch) { anyChildVisible = true; child.Visibility = Visibility.Visible; }
                    }
                }
                groupItem.Visibility = anyChildVisible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static void SetAllVisible(TreeView tree)
        {
            foreach (TreeViewItem item in tree.Items)
            {
                item.Visibility = Visibility.Visible;
                foreach (TreeViewItem child in item.Items)
                {
                    child.Visibility = Visibility.Visible;
                    foreach (TreeViewItem gc in child.Items)
                        gc.Visibility = Visibility.Visible;
                }
            }
        }



        // ═══════════════════════════════════════════════════════════════════
        //  SELECTION HELPERS
        // ═══════════════════════════════════════════════════════════════════

        private static object GetSelectedViewTag()
        {
            var sel = _viewsTree?.SelectedItem as TreeViewItem;
            if (sel?.Tag is AllViewNode av) return av.Tag;
            if (sel?.Tag is UnplacedViewNode uv) return uv.Tag;
            return null;
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

        /// <summary>Update status bar text.</summary>
        private static void UpdateStatus(string text)
        {
            if (_statusText != null) _statusText.Text = text;
        }


        // ═══════════════════════════════════════════════════════════════════
        //  UI HELPERS — hit testing, search box, buttons
        // ═══════════════════════════════════════════════════════════════════

        private static TreeViewItem GetTreeViewItemUnderMouse(TreeView tree, MouseEventArgs e)
        {
            var hit = tree.InputHitTest(e.GetPosition(tree)) as DependencyObject;
            return FindParent<TreeViewItem>(hit);
        }

        private static TreeViewItem GetTreeViewItemUnderDragEvent(TreeView tree, DragEventArgs e)
        {
            var hit = tree.InputHitTest(e.GetPosition(tree)) as DependencyObject;
            return FindParent<TreeViewItem>(hit);
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T found) return found;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private static TextBox CreateSearchBox(string placeholder)
        {
            var box = new TextBox
            {
                Padding = new Thickness(6, 4, 6, 4),
                FontSize = 12, Background = BrBgLight,
                BorderBrush = BrBorder, BorderThickness = new Thickness(1),
                Text = placeholder, Foreground = BrFgSubtle
            };
            box.GotFocus += (s, e) =>
            {
                if (box.Foreground == BrFgSubtle) { box.Text = ""; box.Foreground = BrFgDark; }
            };
            box.LostFocus += (s, e) =>
            {
                if (string.IsNullOrEmpty(box.Text)) { box.Text = placeholder; box.Foreground = BrFgSubtle; }
            };
            return box;
        }

        private static Button CreateSmallButton(string text, SolidColorBrush fg)
        {
            var btn = new Button
            {
                Content = text, FontSize = 11, FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 4, 0),
                Background = BrBgLight, Foreground = fg,
                BorderBrush = BrBorder, BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            btn.MouseEnter += (s, e) => btn.Background = BrHover;
            btn.MouseLeave += (s, e) => btn.Background = BrBgLight;
            return btn;
        }

        private static Button CreateButton(string text, SolidColorBrush bg, SolidColorBrush fg)
        {
            var btn = new Button
            {
                Content = text, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(16, 6, 16, 6),
                Margin = new Thickness(4, 0, 0, 0),
                Background = bg, Foreground = fg,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            var origBg = bg.Color;
            btn.MouseEnter += (s, e) =>
            {
                byte r = (byte)Math.Min(255, origBg.R + 20);
                byte g2 = (byte)Math.Min(255, origBg.G + 20);
                byte b = (byte)Math.Min(255, origBg.B + 20);
                btn.Background = new SolidColorBrush(Color.FromRgb(r, g2, b));
            };
            btn.MouseLeave += (s, e) => btn.Background = bg;
            return btn;
        }
    }
}
