using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Core.TemplateManager;
using Color = System.Windows.Media.Color;

namespace StingTools.UI
{
    /// <summary>
    /// Template Manager Dashboard v2 — sidebar nav + master/detail + inline log.
    /// Replaces the v1 TabControl-of-cards layout. Same dispatch contract
    /// (TemplateManagerResult) so StingCommandHandler's existing
    /// "TemplateDashboard" case keeps working without modification.
    ///
    /// Key differences vs v1:
    ///  - Left rail TreeView with per-op (done/total) badges + RAG dots
    ///  - Right pane shows readiness lights, op detail, preview grid, results
    ///  - Bottom collapsible log pane shows OperationResultBus history
    ///  - Search box at top of nav filters live across all groups
    ///  - Free space used for inline result rendering instead of TaskDialogs
    ///
    /// Document is provided externally (Revit API thread). When null the
    /// dashboard still opens but disables run buttons.
    /// </summary>
    internal static class TemplateManagerDashboardV2
    {
        // ── Theme-routed brushes (follow active theme) ──────────────────
        private static SolidColorBrush BrBg => ThemeManager.GetBrush("AltRowBg");
        private static SolidColorBrush BrPanel => ThemeManager.GetBrush("CardBg");
        private static SolidColorBrush BrHeader => ThemeManager.GetBrush("HeaderBg");
        private static SolidColorBrush BrAccent => ThemeManager.GetBrush("AccentBrush");
        private static SolidColorBrush BrFg => ThemeManager.GetBrush("PanelFg");
        private static SolidColorBrush BrFgDim => ThemeManager.GetBrush("SubtleFg");
        private static SolidColorBrush BrBorder => ThemeManager.GetBrush("BorderColor");
        private static SolidColorBrush BrCard => ThemeManager.GetBrush("CardBg");
        private static SolidColorBrush BrGreen => ThemeManager.GetBrush("SuccessColor");
        private static SolidColorBrush BrHover => ThemeManager.GetBrush("RowHover");
        private static SolidColorBrush BrHeaderFg => ThemeManager.GetBrush("HeaderFg");

        // RAG dot colours (independent of theme — semantic)
        private static readonly SolidColorBrush DotRed = new(Color.FromRgb(0xE5, 0x39, 0x35));
        private static readonly SolidColorBrush DotAmber = new(Color.FromRgb(0xFB, 0x8C, 0x00));
        private static readonly SolidColorBrush DotGreen = new(Color.FromRgb(0x43, 0xA0, 0x47));
        private static readonly SolidColorBrush DotGrey = new(Color.FromRgb(0x99, 0x99, 0x99));

        static TemplateManagerDashboardV2()
        {
            DotRed.Freeze(); DotAmber.Freeze(); DotGreen.Freeze(); DotGrey.Freeze();
        }

        // ── Per-show mutable state ──────────────────────────────────────
        private sealed class DashboardState
        {
            public Document Doc;
            public ReadinessSnapshot Snapshot;
            public OpDefinition SelectedOp;
            public Window Window;
            public TextBlock StatusText;
            public StackPanel LightsPanel;
            public Border DetailHost;
            public StackPanel LogStack;
            public TreeView Nav;
            public TextBox SearchBox;
            public TextBlock LastRunText;
            public IDisposable BusSubscription;
            public TemplateManagerResult Result;
            public List<OperationResult> SessionResults = new();
            public Dictionary<string, TextBlock> BadgeBlocks = new();
            public Dictionary<string, Border> BadgeDots = new();
        }

        private static bool _providersRegistered;

        /// <summary>
        /// Show the v2 dashboard. Doc may be null (read-only inspection mode).
        /// Returns the same TemplateManagerResult contract as v1.
        /// </summary>
        public static TemplateManagerResult Show(Document doc)
        {
            // Lazy-register preview providers on first show
            if (!_providersRegistered)
            {
                try { PreviewProviders.RegisterAll(); _providersRegistered = true; }
                catch (Exception ex) { StingLog.Warn($"PreviewProviders.RegisterAll: {ex.Message}"); }
            }

            var state = new DashboardState
            {
                Doc = doc,
                Result = new TemplateManagerResult()
            };

            try { state.Snapshot = ProjectReadiness.Compute(doc, forceRefresh: true); }
            catch (Exception ex) { StingLog.Warn($"TemplateManagerDashboardV2: readiness scan failed — {ex.Message}"); }

            BuildWindow(state);
            SubscribeToBus(state);

            bool? dlg = false;
            try { dlg = state.Window.ShowDialog(); }
            catch (Exception ex) { StingLog.Warn($"TemplateManagerDashboardV2: ShowDialog — {ex.Message}"); }
            finally { try { state.BusSubscription?.Dispose(); } catch { } }

            if (dlg != true) state.Result.Confirmed = false;
            return state.Result;
        }

        // ── Root window + layout ────────────────────────────────────────
        private static void BuildWindow(DashboardState s)
        {
            var win = new Window
            {
                Title = "STING Template Manager",
                Width = 1200,
                Height = 780,
                MinWidth = 980,
                MinHeight = 620,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = BrBg,
                FontFamily = new FontFamily("Segoe UI"),
                ResizeMode = ResizeMode.CanResizeWithGrip
            };
            s.Window = win;

            try
            {
                var handle = Process.GetCurrentProcess().MainWindowHandle;
                if (handle != IntPtr.Zero)
                    new System.Windows.Interop.WindowInteropHelper(win).Owner = handle;
            }
            catch (Exception ex) { StingLog.Warn($"TemplateManagerDashboardV2 owner: {ex.Message}"); }

            // Root DockPanel
            var root = new DockPanel { LastChildFill = true, Background = BrBg };
            root.Children.Add(BuildHeader(s));
            DockPanel.SetDock(root.Children[root.Children.Count - 1], Dock.Top);

            root.Children.Add(BuildFooter(s));
            DockPanel.SetDock(root.Children[root.Children.Count - 1], Dock.Bottom);

            // Main 3-column grid: nav | detail | log
            var bodyGrid = new Grid { Margin = new Thickness(8) };
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300), MinWidth = 0 });

            // Nav column
            var navHost = BuildNav(s);
            Grid.SetColumn(navHost, 0);
            bodyGrid.Children.Add(navHost);

            // Splitter 1
            var sp1 = new GridSplitter
            {
                Width = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = BrBorder,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext
            };
            Grid.SetColumn(sp1, 1);
            bodyGrid.Children.Add(sp1);

            // Detail column
            var detail = BuildDetail(s);
            Grid.SetColumn(detail, 2);
            bodyGrid.Children.Add(detail);

            // Splitter 2
            var sp2 = new GridSplitter
            {
                Width = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = BrBorder,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext
            };
            Grid.SetColumn(sp2, 3);
            bodyGrid.Children.Add(sp2);

            // Log pane
            var logHost = BuildLogPane(s);
            Grid.SetColumn(logHost, 4);
            bodyGrid.Children.Add(logHost);

            root.Children.Add(bodyGrid);
            win.Content = root;

            // Keyboard
            win.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { win.DialogResult = false; }
                else if (e.Key == Key.F5) { RefreshReadiness(s); }
                else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
                {
                    s.SearchBox?.Focus();
                    s.SearchBox?.SelectAll();
                }
            };

            // Initial render: pre-select first highlighted op
            var first = OperationRegistry.All.FirstOrDefault(o => o.IsHighlighted)
                     ?? OperationRegistry.All.FirstOrDefault();
            if (first != null) SelectOp(s, first);
        }

        // ── Header ──────────────────────────────────────────────────────
        private static UIElement BuildHeader(DashboardState s)
        {
            var border = new Border
            {
                Background = BrHeader,
                Padding = new Thickness(16, 10, 16, 10)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "Template Manager",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrHeaderFg
            });
            string docName = s.Doc?.Title ?? "(no document)";
            stack.Children.Add(new TextBlock
            {
                Text = $"Templates · Styles · Filters · VG Overrides · Automation    |    {docName}",
                FontSize = 11,
                Foreground = BrHeaderFg,
                Opacity = 0.85,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(stack, 0);
            grid.Children.Add(stack);

            // Right-side: refresh + close hint
            var rightStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var btnRefresh = MakeHeaderButton("↻ Refresh (F5)");
            btnRefresh.Click += (_, __) => RefreshReadiness(s);
            rightStack.Children.Add(btnRefresh);
            var btnTheme = MakeHeaderButton("Theme");
            btnTheme.Click += (_, __) =>
            {
                try
                {
                    var next = ThemeManager.CycleTheme();
                    s.StatusText.Text = $"Theme: {next}";
                }
                catch (Exception ex) { StingLog.Warn($"Cycle theme: {ex.Message}"); }
            };
            rightStack.Children.Add(btnTheme);
            Grid.SetColumn(rightStack, 1);
            grid.Children.Add(rightStack);

            border.Child = grid;
            return border;
        }

        private static Button MakeHeaderButton(string text)
        {
            return new Button
            {
                Content = text,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(10, 4, 10, 4),
                Background = Brushes.Transparent,
                Foreground = BrHeaderFg,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xff, 0xff, 0xff)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                FontSize = 11
            };
        }

        // ── Footer ──────────────────────────────────────────────────────
        private static UIElement BuildFooter(DashboardState s)
        {
            var footer = new Border
            {
                Padding = new Thickness(12, 6, 12, 8),
                Background = BrPanel,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 1, 0, 0)
            };
            var dp = new DockPanel { LastChildFill = false };

            var btnClose = new Button
            {
                Content = "Close",
                Width = 80, Height = 26,
                FontSize = 12,
                Background = BrCard,
                BorderBrush = BrBorder,
                Cursor = Cursors.Hand,
                Margin = new Thickness(6, 0, 0, 0)
            };
            btnClose.Click += (_, __) => s.Window.DialogResult = false;
            DockPanel.SetDock(btnClose, Dock.Right);
            dp.Children.Add(btnClose);

            s.LastRunText = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = BrFgDim,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0)
            };
            DockPanel.SetDock(s.LastRunText, Dock.Right);
            dp.Children.Add(s.LastRunText);

            s.StatusText = new TextBlock
            {
                Text = "Select an operation from the left to view details and run.",
                FontSize = 11,
                Foreground = BrFgDim,
                VerticalAlignment = VerticalAlignment.Center
            };
            dp.Children.Add(s.StatusText);

            footer.Child = dp;
            return footer;
        }

        // ── Nav (left rail) ─────────────────────────────────────────────
        private static UIElement BuildNav(DashboardState s)
        {
            var border = new Border
            {
                Background = BrPanel,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2)
            };
            var dp = new DockPanel { LastChildFill = true, Margin = new Thickness(0) };

            // Search + favourites toggle
            var topBar = new StackPanel { Margin = new Thickness(8, 8, 8, 4) };
            s.SearchBox = new TextBox
            {
                Height = 26,
                FontSize = 12,
                Padding = new Thickness(6, 3, 6, 3),
                BorderBrush = BrBorder,
                Tag = "Search ops…  (Ctrl+F)"
            };
            // Placeholder via Tag hack
            s.SearchBox.GotFocus += (_, __) => { if ((string)s.SearchBox.Tag == s.SearchBox.Text) s.SearchBox.Text = ""; };
            s.SearchBox.TextChanged += (_, __) => RebuildNavTree(s);
            topBar.Children.Add(s.SearchBox);

            DockPanel.SetDock(topBar, Dock.Top);
            dp.Children.Add(topBar);

            // Tree
            s.Nav = new TreeView
            {
                BorderThickness = new Thickness(0),
                Background = BrPanel
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(s.Nav, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(s.Nav, ScrollBarVisibility.Auto);
            VirtualizingPanel.SetIsVirtualizing(s.Nav, true);
            dp.Children.Add(s.Nav);

            border.Child = dp;
            RebuildNavTree(s);
            return border;
        }

        private static void RebuildNavTree(DashboardState s)
        {
            string query = s.SearchBox?.Text ?? "";
            if (query == (string)s.SearchBox?.Tag) query = "";

            s.Nav.Items.Clear();
            s.BadgeBlocks.Clear();
            s.BadgeDots.Clear();

            foreach (string group in OperationRegistry.Groups)
            {
                var ops = OperationRegistry.ByGroup(group).ToList();
                if (!string.IsNullOrEmpty(query))
                    ops = ops.Where(o => OperationRegistry.Search(query).Contains(o)).ToList();
                if (ops.Count == 0) continue;

                var groupItem = new TreeViewItem
                {
                    Header = BuildGroupHeader(group, ops.Count),
                    IsExpanded = true,
                    Foreground = BrFg,
                    FontWeight = FontWeights.SemiBold
                };
                foreach (var op in ops)
                {
                    var item = new TreeViewItem
                    {
                        Header = BuildOpHeader(op, s),
                        Tag = op,
                        Foreground = BrFg,
                        FontWeight = FontWeights.Normal
                    };
                    item.Selected += (sender, e) =>
                    {
                        if (sender is TreeViewItem tvi && tvi.Tag is OpDefinition od)
                        {
                            SelectOp(s, od);
                            e.Handled = true;
                        }
                    };
                    groupItem.Items.Add(item);
                }
                s.Nav.Items.Add(groupItem);
            }
        }

        private static UIElement BuildGroupHeader(string group, int count)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = group, FontSize = 11, FontWeight = FontWeights.SemiBold });
            sp.Children.Add(new TextBlock { Text = $"  ({count})", FontSize = 10, Foreground = BrFgDim, Margin = new Thickness(4, 0, 0, 0) });
            return sp;
        }

        private static UIElement BuildOpHeader(OpDefinition op, DashboardState s)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 4, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var badge = s.Snapshot?.BadgeOrDefault(op.Tag);
            var dot = new Border
            {
                Width = 8, Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = DotFor(badge?.Status),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            Grid.SetColumn(dot, 0);
            grid.Children.Add(dot);
            s.BadgeDots[op.Tag] = dot;

            var title = new TextBlock
            {
                Text = op.Title,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = op.IsHighlighted ? BrGreen : BrFg,
                FontWeight = op.IsHighlighted ? FontWeights.SemiBold : FontWeights.Normal
            };
            Grid.SetColumn(title, 1);
            grid.Children.Add(title);

            var badgeText = new TextBlock
            {
                Text = badge != null && badge.Total > 0 ? $"{badge.Done}/{badge.Total}" : "",
                FontSize = 10,
                Foreground = BrFgDim,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            };
            Grid.SetColumn(badgeText, 2);
            grid.Children.Add(badgeText);
            s.BadgeBlocks[op.Tag] = badgeText;

            return grid;
        }

        private static SolidColorBrush DotFor(string status) => status switch
        {
            "Green" => DotGreen,
            "Amber" => DotAmber,
            "Red" => DotRed,
            _ => DotGrey
        };

        // ── Detail pane (centre) ────────────────────────────────────────
        private static UIElement BuildDetail(DashboardState s)
        {
            var outer = new Border
            {
                Background = BrPanel,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2)
            };
            var dp = new DockPanel { LastChildFill = true };

            // Readiness lights strip
            s.LightsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(12, 10, 12, 10)
            };
            var lightsBorder = new Border
            {
                Child = s.LightsPanel,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = BrCard
            };
            RefreshLights(s);
            DockPanel.SetDock(lightsBorder, Dock.Top);
            dp.Children.Add(lightsBorder);

            // Detail host (replaced when op selected)
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(16)
            };
            s.DetailHost = new Border { Background = Brushes.Transparent };
            scroll.Content = s.DetailHost;
            dp.Children.Add(scroll);

            outer.Child = dp;
            return outer;
        }

        private static void RefreshLights(DashboardState s)
        {
            s.LightsPanel.Children.Clear();
            if (s.Snapshot == null || s.Snapshot.Lights.Count == 0)
            {
                s.LightsPanel.Children.Add(new TextBlock
                {
                    Text = "No document open — open a Revit project to see readiness.",
                    FontSize = 11, Foreground = BrFgDim
                });
                return;
            }
            foreach (var light in s.Snapshot.Lights)
            {
                s.LightsPanel.Children.Add(BuildLightTile(light));
            }
            // Refresh badges in the nav tree
            foreach (var kvp in s.BadgeBlocks)
            {
                var b = s.Snapshot.BadgeOrDefault(kvp.Key);
                kvp.Value.Text = b.Total > 0 ? $"{b.Done}/{b.Total}" : "";
                if (s.BadgeDots.TryGetValue(kvp.Key, out var dot))
                    dot.Background = DotFor(b.Status);
            }
        }

        private static UIElement BuildLightTile(ReadinessLight l)
        {
            var border = new Border
            {
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(10, 6, 12, 6),
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Background = BrPanel
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new Border
            {
                Width = 10, Height = 10,
                CornerRadius = new CornerRadius(5),
                Background = DotFor(l.Status),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            var ts = new StackPanel();
            ts.Children.Add(new TextBlock { Text = l.Label, FontSize = 11, FontWeight = FontWeights.SemiBold });
            string body = l.Total == 0 ? (string.IsNullOrEmpty(l.Note) ? "n/a" : l.Note)
                                       : $"{l.Done}/{l.Total} · {l.Pct:0}%";
            ts.Children.Add(new TextBlock { Text = body, FontSize = 10, Foreground = BrFgDim });
            sp.Children.Add(ts);
            border.Child = sp;
            return border;
        }

        // ── Op detail panel rendering ───────────────────────────────────
        private static void SelectOp(DashboardState s, OpDefinition op)
        {
            s.SelectedOp = op;
            var stack = new StackPanel();

            // Suggestion strip (top of detail pane)
            try
            {
                if (s.Doc != null)
                {
                    var sugg = StingTools.Core.TemplateManager.SuggestionEngine.Compute(s.Doc, s.Snapshot, 3);
                    if (sugg != null && sugg.Count > 0)
                        stack.Children.Add(BuildSuggestionStrip(sugg, s));
                }
            }
            catch (Exception ex) { StingLog.Warn($"SuggestionStrip: {ex.Message}"); }

            // Title bar
            var titleBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            titleBar.Children.Add(new TextBlock
            {
                Text = op.Title,
                FontSize = 18, FontWeight = FontWeights.SemiBold,
                Foreground = BrFg
            });
            if (op.IsDestructive)
                titleBar.Children.Add(MakePill("Destructive", Color.FromRgb(0xE5, 0x39, 0x35)));
            if (op.IsReadOnly)
                titleBar.Children.Add(MakePill("Read-only", Color.FromRgb(0x43, 0xA0, 0x47)));
            stack.Children.Add(titleBar);

            stack.Children.Add(new TextBlock
            {
                Text = op.Description,
                FontSize = 12, Foreground = BrFgDim,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            // Badge summary
            var b = s.Snapshot?.BadgeOrDefault(op.Tag);
            if (b != null && b.Total > 0)
            {
                stack.Children.Add(BuildBadgeRow(b));
            }

            // Dependencies
            if (op.RequiresOps != null && op.RequiresOps.Length > 0)
            {
                var depBox = new Border
                {
                    Background = BrCard,
                    BorderBrush = BrBorder,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 8, 0, 8)
                };
                var depStack = new StackPanel();
                depStack.Children.Add(new TextBlock
                {
                    Text = "Requires", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = BrFgDim
                });
                foreach (var dep in op.RequiresOps)
                {
                    var depOp = OperationRegistry.Get(dep);
                    var depB = s.Snapshot?.BadgeOrDefault(dep);
                    string statusTxt = depB != null && depB.Total > 0 ? $"({depB.Done}/{depB.Total})" : "";
                    var row = new StackPanel { Orientation = Orientation.Horizontal };
                    row.Children.Add(new Border
                    {
                        Width = 8, Height = 8,
                        CornerRadius = new CornerRadius(4),
                        Background = DotFor(depB?.Status),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 4, 0)
                    });
                    row.Children.Add(new TextBlock
                    {
                        Text = $"{depOp?.Title ?? dep}  {statusTxt}",
                        FontSize = 11, Foreground = BrFg
                    });
                    depStack.Children.Add(row);
                }
                depBox.Child = depStack;
                stack.Children.Add(depBox);
            }

            // Preview section (if op has a provider)
            OperationPreview preview = null;
            try
            {
                if (op.Preview != null && s.Doc != null)
                    preview = op.Preview(s.Doc);
            }
            catch (Exception ex) { StingLog.Warn($"Preview {op.Tag}: {ex.Message}"); }

            if (preview != null)
            {
                stack.Children.Add(BuildPreviewGrid(preview, s));
            }
            else
            {
                stack.Children.Add(new Border
                {
                    Background = BrCard,
                    BorderBrush = BrBorder,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 4, 0, 12),
                    Child = new TextBlock
                    {
                        Text = "Click Run to execute. Results will appear here and in the log pane.",
                        FontSize = 11, Foreground = BrFgDim,
                        TextWrapping = TextWrapping.Wrap
                    }
                });
            }

            // Last result for this op (from session)
            var lastResult = s.SessionResults.FirstOrDefault(r =>
                string.Equals(r.Operation, op.Tag, StringComparison.OrdinalIgnoreCase));
            if (lastResult != null)
            {
                stack.Children.Add(BuildResultPanel(lastResult));
            }

            // Action bar
            var actionBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            bool depsOk = AreDependenciesMet(op, s);
            if (!depsOk)
            {
                actionBar.Children.Add(new TextBlock
                {
                    Text = "⚠ Run dependencies first",
                    Foreground = DotAmber, FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                });
            }

            var btnRun = new Button
            {
                Content = preview != null ? $"▶  Run ({preview.SelectedCount})" : "▶  Run",
                Width = 130, Height = 32,
                FontSize = 12, FontWeight = FontWeights.SemiBold,
                Background = op.IsHighlighted ? BrGreen : BrAccent,
                Foreground = Brushes.White,
                BorderBrush = op.IsHighlighted ? BrGreen : BrAccent,
                Cursor = Cursors.Hand,
                IsEnabled = s.Doc != null
            };
            string tag = op.Tag;
            btnRun.Click += (_, __) => OnRun(s, tag, preview);
            actionBar.Children.Add(btnRun);

            stack.Children.Add(actionBar);

            s.DetailHost.Child = stack;
            s.StatusText.Text = $"Selected: {op.Title}";
        }

        private static UIElement BuildSuggestionStrip(System.Collections.Generic.List<Suggestion> sugg, DashboardState s)
        {
            var border = new Border
            {
                Background = BrCard,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var stk = new StackPanel();
            stk.Children.Add(new TextBlock
            {
                Text = "★ Suggestions",
                FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = BrFgDim, Margin = new Thickness(0, 0, 0, 4)
            });
            foreach (var sug in sugg)
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Color c = sug.Severity == "critical" ? Color.FromRgb(0xE5, 0x39, 0x35)
                       : sug.Severity == "warning"  ? Color.FromRgb(0xFB, 0x8C, 0x00)
                                                    : Color.FromRgb(0x43, 0xA0, 0x47);
                var d = new SolidColorBrush(c); d.Freeze();
                var dot = new Border
                {
                    Width = 8, Height = 8, CornerRadius = new CornerRadius(4),
                    Background = d, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0)
                };
                Grid.SetColumn(dot, 0);
                row.Children.Add(dot);
                var ts = new StackPanel();
                ts.Children.Add(new TextBlock
                {
                    Text = sug.Title, FontSize = 12, FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                });
                ts.Children.Add(new TextBlock
                {
                    Text = sug.Detail, FontSize = 11, Foreground = BrFgDim,
                    TextWrapping = TextWrapping.Wrap
                });
                Grid.SetColumn(ts, 1);
                row.Children.Add(ts);
                if (!string.IsNullOrEmpty(sug.OpTag))
                {
                    var jumpBtn = new Button
                    {
                        Content = "→ Open",
                        Padding = new Thickness(8, 2, 8, 2),
                        FontSize = 11,
                        Background = BrAccent,
                        Foreground = Brushes.White,
                        BorderBrush = BrAccent,
                        Cursor = Cursors.Hand,
                        Margin = new Thickness(8, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    string tag = sug.OpTag;
                    jumpBtn.Click += (_, __) =>
                    {
                        var jump = OperationRegistry.Get(tag);
                        if (jump != null) SelectOp(s, jump);
                    };
                    Grid.SetColumn(jumpBtn, 2);
                    row.Children.Add(jumpBtn);
                }
                stk.Children.Add(row);
            }
            border.Child = stk;
            return border;
        }

        private static UIElement BuildBadgeRow(OpBadge b)
        {
            var border = new Border
            {
                Padding = new Thickness(10, 6, 10, 6),
                Background = BrCard,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new Border
            {
                Width = 10, Height = 10, CornerRadius = new CornerRadius(5),
                Background = DotFor(b.Status),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            sp.Children.Add(new TextBlock
            {
                Text = $"Current state: {b.Done}/{b.Total}",
                FontSize = 11, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (!string.IsNullOrEmpty(b.Note))
            {
                sp.Children.Add(new TextBlock
                {
                    Text = "  ·  " + b.Note,
                    FontSize = 11, Foreground = BrFgDim,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            border.Child = sp;
            return border;
        }

        private static bool AreDependenciesMet(OpDefinition op, DashboardState s)
        {
            if (op.RequiresOps == null || op.RequiresOps.Length == 0) return true;
            if (s.Snapshot == null) return true; // unknown — don't block
            foreach (var dep in op.RequiresOps)
            {
                var b = s.Snapshot.BadgeOrDefault(dep);
                if (b == null || b.Total == 0) continue;
                if (b.Done == 0) return false;
            }
            return true;
        }

        // ── Preview grid (Phase 3 surface; populated by Phase 6) ────────
        private static UIElement BuildPreviewGrid(OperationPreview preview, DashboardState s)
        {
            var outer = new Border
            {
                Background = BrCard,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 4, 0, 12)
            };
            var dp = new DockPanel { LastChildFill = true };

            // Toolbar: select all/none, discipline filter, source filter, hide existing
            var toolbar = new WrapPanel { Margin = new Thickness(8, 8, 8, 4) };
            var lblCount = new TextBlock
            {
                Text = $"{preview.SelectedCount} of {preview.Rows.Count} selected  ·  {preview.NewCount} new, {preview.ExistingCount} existing",
                FontSize = 11, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            toolbar.Children.Add(lblCount);

            var btnAll = MakeSmallButton("Select All");
            var btnNone = MakeSmallButton("Select None");
            var btnHideExisting = MakeSmallButton("Hide Existing");
            toolbar.Children.Add(btnAll);
            toolbar.Children.Add(btnNone);
            toolbar.Children.Add(btnHideExisting);

            // Discipline picker
            if (preview.AvailableDisciplines != null && preview.AvailableDisciplines.Count > 1)
            {
                var lbl = new TextBlock
                {
                    Text = "  Discipline:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 4, 0),
                    FontSize = 11,
                    Foreground = BrFgDim
                };
                toolbar.Children.Add(lbl);
                var cb = new ComboBox
                {
                    Width = 120,
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 0)
                };
                cb.Items.Add("All");
                foreach (var d in preview.AvailableDisciplines) cb.Items.Add(d);
                cb.SelectedIndex = 0;
                cb.SelectionChanged += (_, __) =>
                {
                    string pick = cb.SelectedItem as string ?? "All";
                    foreach (var r in preview.Rows)
                        r.IsSelected = pick == "All" || string.Equals(r.Discipline, pick, StringComparison.OrdinalIgnoreCase);
                    lblCount.Text = $"{preview.SelectedCount} of {preview.Rows.Count} selected  ·  {preview.NewCount} new, {preview.ExistingCount} existing";
                    RefreshGridSource(dp, preview);
                };
                toolbar.Children.Add(cb);
            }

            DockPanel.SetDock(toolbar, Dock.Top);
            dp.Children.Add(toolbar);

            // Data grid
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserResizeRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                Background = BrPanel,
                AlternatingRowBackground = BrCard,
                RowBackground = BrPanel,
                BorderThickness = new Thickness(0),
                MaxHeight = 320,
                Margin = new Thickness(8, 4, 8, 8),
                FontSize = 11,
                Tag = preview
            };
            grid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "",
                Binding = new Binding(nameof(PreviewRow.IsSelected)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = 34
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Name",
                Binding = new Binding(nameof(PreviewRow.Name)),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                IsReadOnly = true
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Discipline",
                Binding = new Binding(nameof(PreviewRow.Discipline)),
                Width = 90, IsReadOnly = true
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Category",
                Binding = new Binding(nameof(PreviewRow.Category)),
                Width = 120, IsReadOnly = true
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Exists",
                Binding = new Binding(nameof(PreviewRow.Exists)),
                Width = 60, IsReadOnly = true
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Action",
                Binding = new Binding(nameof(PreviewRow.Action)),
                Width = 90
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Source",
                Binding = new Binding(nameof(PreviewRow.Source)),
                Width = 90, IsReadOnly = true
            });
            grid.ItemsSource = preview.Rows;
            dp.Children.Add(grid);

            // Refresh count on cell edit
            grid.CellEditEnding += (_, __) =>
            {
                lblCount.Text = $"{preview.SelectedCount} of {preview.Rows.Count} selected  ·  {preview.NewCount} new, {preview.ExistingCount} existing";
            };

            btnAll.Click += (_, __) =>
            {
                foreach (var r in preview.Rows) r.IsSelected = true;
                lblCount.Text = $"{preview.SelectedCount} of {preview.Rows.Count} selected  ·  {preview.NewCount} new, {preview.ExistingCount} existing";
                grid.Items.Refresh();
            };
            btnNone.Click += (_, __) =>
            {
                foreach (var r in preview.Rows) r.IsSelected = false;
                lblCount.Text = $"{preview.SelectedCount} of {preview.Rows.Count} selected  ·  {preview.NewCount} new, {preview.ExistingCount} existing";
                grid.Items.Refresh();
            };
            bool hideExisting = false;
            btnHideExisting.Click += (_, __) =>
            {
                hideExisting = !hideExisting;
                grid.ItemsSource = hideExisting
                    ? preview.Rows.Where(r => !r.Exists).ToList()
                    : preview.Rows;
                btnHideExisting.Content = hideExisting ? "Show All" : "Hide Existing";
            };

            outer.Child = dp;
            return outer;
        }

        private static void RefreshGridSource(DockPanel host, OperationPreview p)
        {
            foreach (var child in host.Children)
            {
                if (child is DataGrid g) g.Items.Refresh();
            }
        }

        private static Button MakeSmallButton(string text)
        {
            return new Button
            {
                Content = text,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 4, 0),
                FontSize = 11,
                Background = BrPanel,
                BorderBrush = BrBorder,
                Cursor = Cursors.Hand
            };
        }

        private static UIElement MakePill(string text, Color colour)
        {
            var b = new SolidColorBrush(colour); b.Freeze();
            return new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 1, 8, 1),
                Margin = new Thickness(8, 0, 0, 0),
                Background = b,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = text, FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White
                }
            };
        }

        // ── Result panel (Phase 2 — replaces TaskDialog) ────────────────
        private static UIElement BuildResultPanel(OperationResult r)
        {
            var border = new Border
            {
                Background = BrCard,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 0, 0, 0),
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(0)
            };
            var stack = new StackPanel();

            // Header strip
            var head = new Border
            {
                Background = SeverityBg(r.Severity),
                Padding = new Thickness(12, 8, 12, 8)
            };
            var headStack = new StackPanel();
            headStack.Children.Add(new TextBlock
            {
                Text = "Result · " + r.OperationLabel,
                FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });
            headStack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(r.Headline) ? "" : r.Headline,
                FontSize = 11, Foreground = Brushes.White, Opacity = 0.9
            });
            if (!string.IsNullOrEmpty(r.SubHeadline))
                headStack.Children.Add(new TextBlock
                {
                    Text = r.SubHeadline,
                    FontSize = 10, Foreground = Brushes.White, Opacity = 0.8
                });
            head.Child = headStack;
            stack.Children.Add(head);

            foreach (var section in r.Sections)
            {
                var card = new Border
                {
                    Padding = new Thickness(10, 8, 10, 8),
                    BorderBrush = BrBorder,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Background = BrPanel
                };
                var cs = new StackPanel();
                var hdr = new StackPanel { Orientation = Orientation.Horizontal };
                hdr.Children.Add(new Border
                {
                    Width = 8, Height = 8, CornerRadius = new CornerRadius(4),
                    Background = SeverityDot(section.Severity),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                });
                hdr.Children.Add(new TextBlock
                {
                    Text = section.Name, FontSize = 12, FontWeight = FontWeights.SemiBold
                });
                if (!string.IsNullOrEmpty(section.Headline))
                    hdr.Children.Add(new TextBlock
                    {
                        Text = "  · " + section.Headline,
                        FontSize = 11, Foreground = BrFgDim,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 0, 0)
                    });
                cs.Children.Add(hdr);
                if (section.Metrics != null && section.Metrics.Count > 0)
                {
                    var wp = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
                    foreach (var (label, val) in section.Metrics)
                    {
                        var pb = new Border
                        {
                            Margin = new Thickness(0, 0, 8, 4),
                            Padding = new Thickness(6, 2, 6, 2),
                            Background = BrCard,
                            BorderBrush = BrBorder,
                            BorderThickness = new Thickness(1)
                        };
                        pb.Child = new TextBlock
                        {
                            Text = $"{label}: {val}", FontSize = 11
                        };
                        wp.Children.Add(pb);
                    }
                    cs.Children.Add(wp);
                }
                if (section.Rows != null && section.Rows.Count > 0)
                {
                    var rg = new DataGrid
                    {
                        AutoGenerateColumns = false,
                        CanUserAddRows = false,
                        IsReadOnly = true,
                        HeadersVisibility = DataGridHeadersVisibility.Column,
                        Background = BrPanel,
                        AlternatingRowBackground = BrCard,
                        MaxHeight = 200,
                        FontSize = 11,
                        Margin = new Thickness(0, 4, 0, 0),
                        BorderThickness = new Thickness(0)
                    };
                    rg.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(ResultRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    rg.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding(nameof(ResultRow.Status)), Width = 90 });
                    rg.Columns.Add(new DataGridTextColumn { Header = "Discipline", Binding = new Binding(nameof(ResultRow.Discipline)), Width = 90 });
                    rg.Columns.Add(new DataGridTextColumn { Header = "Detail", Binding = new Binding(nameof(ResultRow.Detail)), Width = 200 });
                    rg.ItemsSource = section.Rows;
                    cs.Children.Add(rg);
                }
                if (!string.IsNullOrEmpty(section.Notes))
                {
                    cs.Children.Add(new TextBlock
                    {
                        Text = section.Notes,
                        FontSize = 11, Foreground = BrFgDim,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 4, 0, 0)
                    });
                }
                card.Child = cs;
                stack.Children.Add(card);
            }

            border.Child = stack;
            return border;
        }

        private static SolidColorBrush SeverityBg(ResultSeverity sev) => sev switch
        {
            ResultSeverity.Success => DotGreen,
            ResultSeverity.Warning => DotAmber,
            ResultSeverity.Error => DotRed,
            _ => BrAccent
        };

        private static SolidColorBrush SeverityDot(ResultSeverity sev) => sev switch
        {
            ResultSeverity.Success => DotGreen,
            ResultSeverity.Warning => DotAmber,
            ResultSeverity.Error => DotRed,
            _ => DotGrey
        };

        // ── Log pane (right) ────────────────────────────────────────────
        private static UIElement BuildLogPane(DashboardState s)
        {
            var border = new Border
            {
                Background = BrPanel,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2)
            };
            var dp = new DockPanel { LastChildFill = true };

            var head = new Border
            {
                Background = BrCard,
                Padding = new Thickness(10, 6, 10, 6),
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            head.Child = new TextBlock
            {
                Text = "Session log", FontSize = 12, FontWeight = FontWeights.SemiBold
            };
            DockPanel.SetDock(head, Dock.Top);
            dp.Children.Add(head);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(6)
            };
            s.LogStack = new StackPanel();
            scroll.Content = s.LogStack;
            dp.Children.Add(scroll);

            border.Child = dp;
            RebuildLog(s);
            return border;
        }

        private static void RebuildLog(DashboardState s)
        {
            s.LogStack.Children.Clear();
            // Newest first (session results)
            for (int i = s.SessionResults.Count - 1; i >= 0; i--)
            {
                var r = s.SessionResults[i];
                s.LogStack.Children.Add(BuildLogRow(r, s));
            }
            if (s.SessionResults.Count == 0)
            {
                s.LogStack.Children.Add(new TextBlock
                {
                    Text = "No operations run this session.",
                    FontSize = 11, Foreground = BrFgDim,
                    Margin = new Thickness(6)
                });
            }
        }

        private static UIElement BuildLogRow(OperationResult r, DashboardState s)
        {
            var b = new Border
            {
                Margin = new Thickness(2, 2, 2, 2),
                Padding = new Thickness(8, 6, 8, 6),
                Background = BrCard,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Cursor = Cursors.Hand
            };
            var dp = new DockPanel { LastChildFill = true };

            var dot = new Border
            {
                Width = 8, Height = 8, CornerRadius = new CornerRadius(4),
                Background = SeverityDot(r.Severity),
                Margin = new Thickness(0, 4, 6, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            DockPanel.SetDock(dot, Dock.Left);
            dp.Children.Add(dot);

            var stk = new StackPanel();
            stk.Children.Add(new TextBlock
            {
                Text = r.CompletedUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                     + "  ·  " + r.OperationLabel,
                FontSize = 11, FontWeight = FontWeights.SemiBold
            });
            if (!string.IsNullOrEmpty(r.Headline))
                stk.Children.Add(new TextBlock
                {
                    Text = r.Headline, FontSize = 11,
                    Foreground = BrFgDim, TextWrapping = TextWrapping.Wrap
                });
            dp.Children.Add(stk);

            b.MouseLeftButtonUp += (_, __) =>
            {
                // Jump to the op in the detail pane and show the result again
                var op = OperationRegistry.Get(r.Operation);
                if (op != null) SelectOp(s, op);
            };
            b.Child = dp;
            return b;
        }

        // ── Run handler ─────────────────────────────────────────────────
        private static void OnRun(DashboardState s, string opTag, OperationPreview preview)
        {
            // Stash selection options into the result dictionary so the caller can pass them on.
            s.Result.Confirmed = true;
            s.Result.Operation = opTag;
            s.Result.Options = new Dictionary<string, string>();

            if (preview != null)
            {
                // Serialize selected row names so commands that wire in v2 can read them
                var selected = preview.Rows.Where(r => r.IsSelected).Select(r => r.Key ?? r.Name).ToList();
                s.Result.Options["selected_count"] = selected.Count.ToString(CultureInfo.InvariantCulture);
                s.Result.Options["selected_keys"] = string.Join("|", selected);
                s.Result.Options["scope"] = preview.SupportsScope ? "project" : "project";
            }
            s.Window.DialogResult = true;
        }

        // ── Subscribe to bus and route results back into the dashboard ──
        private static void SubscribeToBus(DashboardState s)
        {
            try
            {
                s.BusSubscription = OperationResultBus.Subscribe(r =>
                {
                    // Capture for later — used when the dashboard is re-shown
                    s.SessionResults.Add(r);
                    // Audit log + server publish (best-effort, never blocks)
                    try { StingTools.Core.TemplateManager.AuditLog.Append(s.Doc, r); }
                    catch (Exception ex) { StingLog.Warn($"AuditLog.Append: {ex.Message}"); }
                    try { StingTools.Core.TemplateManager.ServerPublisher.Publish(s.Doc, r); }
                    catch (Exception ex) { StingLog.Warn($"ServerPublisher.Publish: {ex.Message}"); }
                });
            }
            catch (Exception ex) { StingLog.Warn($"OperationResultBus subscribe: {ex.Message}"); }
        }

        private static void RefreshReadiness(DashboardState s)
        {
            try
            {
                s.Snapshot = ProjectReadiness.Compute(s.Doc, forceRefresh: true);
                RefreshLights(s);
                RebuildNavTree(s);
                if (s.SelectedOp != null) SelectOp(s, s.SelectedOp);
                s.StatusText.Text = "Readiness refreshed.";
            }
            catch (Exception ex) { StingLog.Warn($"RefreshReadiness: {ex.Message}"); }
        }
    }
}
