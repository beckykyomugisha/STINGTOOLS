using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using StingTools.Core;

namespace StingTools.UI
{
    // ══════════════════════════════════════════════════════════════════════
    //  STING BIM COORDINATION CENTER — Unified Corporate Dashboard
    //
    //  Phase 47: Merges 6 separate dialogs into a single 7-tab interface:
    //    OVERVIEW | MODEL HEALTH | WARNINGS | ISSUES | REVISIONS | PLATFORM | WORKFLOWS
    //
    //  Replaces plain-text TaskDialogs for Model Health, Project Dashboard,
    //  Platform Sync, and Warnings Manager with rich WPF panels. Preserves
    //  DataGrid views for Issues and Revisions with inline filtering.
    //
    //  Corporate theme: dark blue #1A237E header, orange #E8912D accents,
    //  white cards with subtle borders, Segoe UI font.
    // ══════════════════════════════════════════════════════════════════════

    internal class BIMCoordinationCenter : Window
    {
        // ── Theme colours ──
        private static readonly Color CHeaderBg    = Color.FromRgb(0x1A, 0x23, 0x7E);
        private static readonly Color CAccent      = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color CCardBg      = Colors.White;
        private static readonly Color CPageBg      = Color.FromRgb(0xF4, 0xF5, 0xF7);
        private static readonly Color CBorder      = Color.FromRgb(0xE0, 0xE0, 0xE8);
        private static readonly Color CNavBg       = Color.FromRgb(0x1E, 0x27, 0x45);
        private static readonly Color CNavHover    = Color.FromRgb(0x2A, 0x35, 0x5A);
        private static readonly Color CNavSelected = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color CGreen       = Color.FromRgb(0x2E, 0x7D, 0x32);
        private static readonly Color CAmber       = Color.FromRgb(0xF5, 0x7F, 0x17);
        private static readonly Color CRed         = Color.FromRgb(0xC6, 0x28, 0x28);

        private static SolidColorBrush Br(Color c) => new SolidColorBrush(c);

        // ── Data ──
        private CoordData _data;
        private readonly ContentControl _contentArea;
        private readonly TextBlock _statusBar;
        private readonly StackPanel _navPanel;
        private Button _activeNav;

        /// <summary>Result action tag returned to the command handler.</summary>
        public string ResultAction { get; set; }

        // ── Data transfer object ──
        internal class CoordData
        {
            public string ProjectName = "";
            public string FilePath = "";

            // Compliance
            public double TagPct;
            public double StrictPct;
            public string RAGStatus = "RED";
            public int TotalElements;
            public int TaggedComplete;
            public int Untagged;
            public int StaleCount;
            public int SheetsTagged;
            public int SheetsTotal;
            public Dictionary<string, DiscComplianceData> ByDisc = new();
            public Dictionary<string, int> EmptyTokenCounts = new();

            // Warnings
            public int WarningTotal;
            public int WarningCritical;
            public int WarningHigh;
            public int WarningAutoFixable;
            public int WarningHealthScore;
            public string WarningTrend = "→0";
            public bool WarningGatePass;
            public string WarningGateReason = "";
            public int WarningAdded;
            public int WarningRemoved;
            public Dictionary<WarningCategory, int> WarningByCategory = new();
            public Dictionary<WarningSeverity, int> WarningBySeverity = new();
            public Dictionary<string, int> WarningByLevel = new();
            public Dictionary<string, int> WarningByDiscipline = new();
            public List<(string Name, int Count)> WarningHotspots = new();

            // Issues
            public int IssuesOpen;
            public int IssuesCritical;
            public int IssuesOverdue;
            public int IssuesTotal;

            // Revisions
            public int RevisionCount;
            public int RevisionClouds;

            // Platform
            public string LastSyncTime = "";
            public int SyncChanges;

            // Workflows
            public int WorkflowRuns;
            public string LastWorkflow = "none";
            public double LastComplianceBefore;
            public double LastComplianceAfter;

            // Model Health
            public int ModelHealthScore;
            public string ModelHealthRating = "FAIR";
            public List<(string Check, int Score, int Max, string Detail)> HealthChecks = new();
            public List<string> Recommendations = new();
        }

        // ════════════════════════════════════════════════════════════════
        //  CONSTRUCTOR
        // ════════════════════════════════════════════════════════════════

        private BIMCoordinationCenter(CoordData data)
        {
            _data = data;
            Title = "STING BIM Coordination Center";
            Width = 1200; Height = 820;
            MinWidth = 900; MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Br(CPageBg);
            FontFamily = new FontFamily("Segoe UI");
            ResizeMode = ResizeMode.CanResizeWithGrip;

            // Set owner to Revit main window
            try
            {
                var handle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (handle != IntPtr.Zero)
                    new System.Windows.Interop.WindowInteropHelper(this).Owner = handle;
            }
            catch { }

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });   // Header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Body
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });   // Status

            // ── HEADER ──
            root.Children.Add(BuildHeader());

            // ── BODY (Nav + Content) ──
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) }); // Nav
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Content
            Grid.SetRow(body, 1);

            _navPanel = BuildNavPanel();
            body.Children.Add(_navPanel);

            _contentArea = new ContentControl { Margin = new Thickness(0) };
            Grid.SetColumn(_contentArea, 1);
            body.Children.Add(_contentArea);

            root.Children.Add(body);

            // ── STATUS BAR ──
            var statusBorder = new Border
            {
                Background = Br(Color.FromRgb(0x37, 0x47, 0x4F)),
                Padding = new Thickness(12, 4, 12, 4)
            };
            _statusBar = new TextBlock
            {
                Foreground = Brushes.White, FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Text = BuildStatusText()
            };
            statusBorder.Child = _statusBar;
            Grid.SetRow(statusBorder, 2);
            root.Children.Add(statusBorder);

            Content = root;

            // Keyboard shortcuts
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) Close();
                if (e.Key == Key.F5) { NavigateTo("OVERVIEW"); e.Handled = true; }
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.E)
                {
                    ResultAction = "ExportReport"; Close(); e.Handled = true;
                }
            };

            // Default to Overview
            NavigateTo("OVERVIEW");
        }

        private string BuildStatusText()
        {
            return $"{_data.RAGStatus} {_data.TagPct:F0}% tagged | " +
                   $"{_data.WarningTotal} warnings (health: {_data.WarningHealthScore}/100) | " +
                   $"{_data.IssuesOpen} open issues | " +
                   $"{_data.StaleCount} stale | " +
                   $"{_data.SheetsTagged}/{_data.SheetsTotal} sheets tagged";
        }

        // ════════════════════════════════════════════════════════════════
        //  HEADER STRIP
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildHeader()
        {
            var header = new Border { Background = Br(CHeaderBg), Padding = new Thickness(16, 0, 16, 0) };
            var hGrid = new Grid();
            hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left: Title + project
            var leftStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            leftStack.Children.Add(new TextBlock
            {
                Text = "STING BIM COORDINATION CENTER",
                Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0)
            });
            leftStack.Children.Add(new TextBlock
            {
                Text = _data.ProjectName,
                Foreground = Br(Color.FromRgb(0xBB, 0xDE, 0xFB)), FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            hGrid.Children.Add(leftStack);

            // Right: RAG indicator + compliance + date
            var rightStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(rightStack, 1);

            // RAG circle
            var ragColor = _data.RAGStatus == "GREEN" ? CGreen : _data.RAGStatus == "AMBER" ? CAmber : CRed;
            var ragCircle = new Ellipse { Width = 14, Height = 14, Fill = Br(ragColor), Margin = new Thickness(0, 0, 6, 0) };
            rightStack.Children.Add(ragCircle);
            rightStack.Children.Add(new TextBlock
            {
                Text = $"{_data.TagPct:F0}%", Foreground = Brushes.White, FontSize = 18,
                FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            rightStack.Children.Add(new TextBlock
            {
                Text = $"compliant  |  {DateTime.Now:dd MMM yyyy HH:mm}",
                Foreground = Br(Color.FromRgb(0x90, 0xCA, 0xF9)), FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });
            hGrid.Children.Add(rightStack);

            header.Child = hGrid;
            Grid.SetRow(header, 0);
            return header;
        }

        // ════════════════════════════════════════════════════════════════
        //  NAVIGATION PANEL
        // ════════════════════════════════════════════════════════════════

        private StackPanel BuildNavPanel()
        {
            var nav = new StackPanel { Background = Br(CNavBg) };
            nav.Children.Add(new Border { Height = 8 }); // top spacer

            string[] tabs = { "OVERVIEW", "MODEL HEALTH", "WARNINGS", "ISSUES", "REVISIONS", "PLATFORM", "WORKFLOWS" };
            string[] badges = {
                "", $"{_data.ModelHealthScore}/100",
                _data.WarningTotal > 0 ? _data.WarningTotal.ToString() : "",
                _data.IssuesOpen > 0 ? _data.IssuesOpen.ToString() : "",
                _data.RevisionCount.ToString(),
                _data.SyncChanges > 0 ? _data.SyncChanges.ToString() : "",
                _data.WorkflowRuns > 0 ? _data.WorkflowRuns.ToString() : ""
            };

            for (int i = 0; i < tabs.Length; i++)
            {
                var btn = MakeNavButton(tabs[i], badges[i]);
                nav.Children.Add(btn);
            }

            // Bottom spacer + close button
            nav.Children.Add(new Border { Height = 20 });
            var closeBtn = new Button
            {
                Content = "Close", Height = 30, Margin = new Thickness(12, 0, 12, 8),
                Background = Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                FontSize = 11, Cursor = Cursors.Hand
            };
            closeBtn.Click += (s, e) => Close();
            nav.Children.Add(closeBtn);

            return nav;
        }

        private Button MakeNavButton(string label, string badge)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            if (!string.IsNullOrEmpty(badge))
            {
                var badgeBorder = new Border
                {
                    Background = Br(CAccent), CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                badgeBorder.Child = new TextBlock { Text = badge, FontSize = 9, Foreground = Brushes.White, FontWeight = FontWeights.Bold };
                sp.Children.Add(badgeBorder);
            }

            var btn = new Button
            {
                Content = sp, Tag = label,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Height = 36, Padding = new Thickness(16, 0, 8, 0),
                Margin = new Thickness(0, 1, 0, 1),
                Background = Brushes.Transparent, Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontSize = 12,
                Cursor = Cursors.Hand
            };
            btn.Click += Nav_Click;
            btn.MouseEnter += (s, e) => { if (btn != _activeNav) btn.Background = Br(CNavHover); };
            btn.MouseLeave += (s, e) => { if (btn != _activeNav) btn.Background = Brushes.Transparent; };
            return btn;
        }

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tabName)
                NavigateTo(tabName);
        }

        private void NavigateTo(string tabName)
        {
            // Reset all nav buttons
            foreach (var child in _navPanel.Children)
            {
                if (child is Button nb)
                {
                    nb.Background = Brushes.Transparent;
                    nb.FontWeight = FontWeights.Normal;
                }
            }
            // Highlight active
            foreach (var child in _navPanel.Children)
            {
                if (child is Button nb && nb.Tag as string == tabName)
                {
                    nb.Background = Br(CNavSelected);
                    nb.Foreground = Brushes.White;
                    nb.FontWeight = FontWeights.Bold;
                    _activeNav = nb;
                }
            }

            switch (tabName)
            {
                case "OVERVIEW":     _contentArea.Content = BuildOverviewTab(); break;
                case "MODEL HEALTH": _contentArea.Content = BuildModelHealthTab(); break;
                case "WARNINGS":     _contentArea.Content = BuildWarningsTab(); break;
                case "ISSUES":       _contentArea.Content = BuildIssuesTab(); break;
                case "REVISIONS":    _contentArea.Content = BuildRevisionsTab(); break;
                case "PLATFORM":     _contentArea.Content = BuildPlatformTab(); break;
                case "WORKFLOWS":    _contentArea.Content = BuildWorkflowsTab(); break;
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  OVERVIEW TAB
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildOverviewTab()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(20) };
            var stack = new StackPanel();

            // KPI Cards row
            var kpiGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 16) };
            kpiGrid.Children.Add(MakeKPICard("TOTAL ELEMENTS", _data.TotalElements.ToString("N0"), Br(Color.FromRgb(0x15, 0x65, 0xC0))));
            kpiGrid.Children.Add(MakeKPICard("TAG COMPLIANCE", $"{_data.TagPct:F1}%", RagBrush(_data.RAGStatus)));
            kpiGrid.Children.Add(MakeKPICard("WARNINGS", _data.WarningTotal.ToString(), _data.WarningTotal > 20 ? Br(CRed) : _data.WarningTotal > 5 ? Br(CAmber) : Br(CGreen)));
            kpiGrid.Children.Add(MakeKPICard("OPEN ISSUES", _data.IssuesOpen.ToString(), _data.IssuesOpen > 5 ? Br(CRed) : _data.IssuesOpen > 0 ? Br(CAmber) : Br(CGreen)));
            stack.Children.Add(kpiGrid);

            // RAG compliance bar
            stack.Children.Add(MakeRAGBar("Tag Compliance", _data.TagPct));
            stack.Children.Add(MakeRAGBar("Strict (Fully Resolved)", _data.StrictPct));
            stack.Children.Add(new Border { Height = 12 });

            // Quick Actions
            stack.Children.Add(MakeSectionHeader("QUICK ACTIONS"));
            var actionsWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
            actionsWrap.Children.Add(MakeActionButton("Run Morning Check", "RunMorningCheck", Br(CHeaderBg)));
            actionsWrap.Children.Add(MakeActionButton("Run Daily QA", "RunDailyQA", Br(CHeaderBg)));
            actionsWrap.Children.Add(MakeActionButton("Auto-Fix Warnings", "AutoFixWarnings", Br(CGreen)));
            actionsWrap.Children.Add(MakeActionButton("Retag Stale", "RetagStale", Br(CAccent)));
            actionsWrap.Children.Add(MakeActionButton("Tag New Elements", "TagNewOnly", Br(CAccent)));
            actionsWrap.Children.Add(MakeActionButton("Export COBie", "ExportCOBie", Br(Color.FromRgb(0x6A, 0x1B, 0x9A))));
            actionsWrap.Children.Add(MakeActionButton("Export Report", "ExportReport", Br(Color.FromRgb(0x45, 0x50, 0x6E))));
            stack.Children.Add(actionsWrap);

            // Per-discipline compliance table
            if (_data.ByDisc.Count > 0)
            {
                stack.Children.Add(MakeSectionHeader("COMPLIANCE BY DISCIPLINE"));
                var discGrid = new Grid { Margin = new Thickness(0, 4, 0, 12) };
                discGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                discGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                discGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                discGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                discGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Header row
                int row = 0;
                discGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddCellToGrid(discGrid, "DISC", row, 0, true);
                AddCellToGrid(discGrid, "Total", row, 1, true);
                AddCellToGrid(discGrid, "Tagged", row, 2, true);
                AddCellToGrid(discGrid, "%", row, 3, true);
                AddCellToGrid(discGrid, "RAG", row, 4, true);
                row++;

                foreach (var kv in _data.ByDisc.OrderByDescending(x => x.Value.Total))
                {
                    discGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    double pct = kv.Value.CompliancePct;
                    string rag = pct >= 80 ? "GREEN" : pct >= 50 ? "AMBER" : "RED";
                    AddCellToGrid(discGrid, kv.Key, row, 0, false, FontWeights.Bold);
                    AddCellToGrid(discGrid, kv.Value.Total.ToString(), row, 1);
                    AddCellToGrid(discGrid, kv.Value.Tagged.ToString(), row, 2);
                    AddCellToGrid(discGrid, $"{pct:F0}%", row, 3, false, FontWeights.Normal, RagBrush(rag));
                    AddCellToGrid(discGrid, rag, row, 4, false, FontWeights.Bold, RagBrush(rag));
                    row++;
                }
                var discBorder = new Border
                {
                    Background = Br(CCardBg), BorderBrush = Br(CBorder), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8)
                };
                discBorder.Child = discGrid;
                stack.Children.Add(discBorder);
            }

            // Stale / empty token summary
            if (_data.StaleCount > 0 || _data.EmptyTokenCounts.Values.Any(v => v > 0))
            {
                stack.Children.Add(MakeSectionHeader("TOKEN COVERAGE GAPS"));
                var gapWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
                if (_data.StaleCount > 0)
                    gapWrap.Children.Add(MakeMetricChip($"Stale: {_data.StaleCount}", Br(CRed)));
                foreach (var kv in _data.EmptyTokenCounts.Where(x => x.Value > 0).OrderByDescending(x => x.Value))
                    gapWrap.Children.Add(MakeMetricChip($"{kv.Key}: {kv.Value} empty", Br(CAmber)));
                stack.Children.Add(gapWrap);
            }

            scroll.Content = stack;
            return scroll;
        }

        // ════════════════════════════════════════════════════════════════
        //  MODEL HEALTH TAB
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildModelHealthTab()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(20) };
            var stack = new StackPanel();

            // Health score header
            var scoreColor = _data.ModelHealthScore >= 80 ? CGreen : _data.ModelHealthScore >= 50 ? CAmber : CRed;
            var scoreHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            scoreHeader.Children.Add(new TextBlock
            {
                Text = $"Model Health: {_data.ModelHealthScore}/100",
                FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Br(scoreColor),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0)
            });
            scoreHeader.Children.Add(new TextBlock
            {
                Text = $"({_data.ModelHealthRating})",
                FontSize = 16, Foreground = Br(scoreColor),
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(scoreHeader);

            // RAG bar
            stack.Children.Add(MakeRAGBar("Overall Health", _data.ModelHealthScore));
            stack.Children.Add(new Border { Height = 12 });

            // Health check items
            stack.Children.Add(MakeSectionHeader("HEALTH CHECKS"));
            foreach (var (check, score, max, detail) in _data.HealthChecks)
            {
                var checkCard = new Border
                {
                    Background = Br(CCardBg), BorderBrush = Br(CBorder), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 2, 0, 2)
                };
                var checkGrid = new Grid();
                checkGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                checkGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var scoreBrush = score >= max * 0.8 ? Br(CGreen) : score >= max * 0.5 ? Br(CAmber) : Br(CRed);
                var scoreText = new TextBlock
                {
                    Text = $"[{score}/{max}]", FontWeight = FontWeights.Bold, Foreground = scoreBrush,
                    VerticalAlignment = VerticalAlignment.Center
                };
                checkGrid.Children.Add(scoreText);

                var detailText = new TextBlock
                {
                    Text = $"{check}: {detail}", TextWrapping = TextWrapping.Wrap, FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(detailText, 1);
                checkGrid.Children.Add(detailText);

                checkCard.Child = checkGrid;
                stack.Children.Add(checkCard);
            }

            // Recommendations
            if (_data.Recommendations.Count > 0)
            {
                stack.Children.Add(new Border { Height = 8 });
                stack.Children.Add(MakeSectionHeader("RECOMMENDATIONS"));
                foreach (string rec in _data.Recommendations)
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = $"  \u2022 {rec}", FontSize = 12, Margin = new Thickness(0, 2, 0, 2),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
            }

            // Actions
            stack.Children.Add(new Border { Height = 12 });
            var actionsWrap = new WrapPanel();
            actionsWrap.Children.Add(MakeActionButton("Refresh", "RefreshHealth", Br(CHeaderBg)));
            actionsWrap.Children.Add(MakeActionButton("Export CSV", "ExportHealth", Br(CGreen)));
            actionsWrap.Children.Add(MakeActionButton("Run Full Check", "RunFullCheck", Br(CAccent)));
            stack.Children.Add(actionsWrap);

            scroll.Content = stack;
            return scroll;
        }

        // ════════════════════════════════════════════════════════════════
        //  WARNINGS TAB
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildWarningsTab()
        {
            var root = new DockPanel { Margin = new Thickness(16) };

            // Summary strip at top
            var summaryWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            DockPanel.SetDock(summaryWrap, Dock.Top);
            summaryWrap.Children.Add(MakeMetricChip($"Total: {_data.WarningTotal} {_data.WarningTrend}",
                _data.WarningTotal > 20 ? Br(CRed) : Br(CHeaderBg)));
            summaryWrap.Children.Add(MakeMetricChip($"Critical: {_data.WarningCritical}",
                _data.WarningCritical > 0 ? Br(CRed) : Br(CGreen)));
            summaryWrap.Children.Add(MakeMetricChip($"High: {_data.WarningHigh}",
                _data.WarningHigh > 0 ? Br(CAmber) : Br(CGreen)));
            summaryWrap.Children.Add(MakeMetricChip($"Auto-Fixable: {_data.WarningAutoFixable}", Br(CGreen)));
            summaryWrap.Children.Add(MakeMetricChip($"Health: {_data.WarningHealthScore}/100",
                _data.WarningHealthScore >= 80 ? Br(CGreen) : _data.WarningHealthScore >= 50 ? Br(CAmber) : Br(CRed)));
            if (!_data.WarningGatePass)
                summaryWrap.Children.Add(MakeMetricChip($"GATE: FAIL", Br(CRed)));
            root.Children.Add(summaryWrap);

            // Actions at bottom
            var actionsWrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(actionsWrap, Dock.Bottom);
            actionsWrap.Children.Add(MakeActionButton("Auto-Fix All", "AutoFixWarnings", Br(CGreen)));
            actionsWrap.Children.Add(MakeActionButton("Export CSV", "ExportWarnings", Br(CHeaderBg)));
            actionsWrap.Children.Add(MakeActionButton("Save Baseline", "SaveBaseline", Br(CAccent)));
            actionsWrap.Children.Add(MakeActionButton("Select Elements", "SelectWarningElements", Br(Color.FromRgb(0x6A, 0x1B, 0x9A))));
            actionsWrap.Children.Add(MakeActionButton("Suppress Selected", "SuppressWarnings", Br(Color.FromRgb(0x45, 0x50, 0x6E))));
            actionsWrap.Children.Add(MakeActionButton("Create Issues", "CreateIssuesFromWarnings", Br(CRed)));
            actionsWrap.Children.Add(MakeActionButton("Compliance Check", "WarningsCompliance", Br(CHeaderBg)));
            root.Children.Add(actionsWrap);

            // Warning breakdown cards
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var contentStack = new StackPanel();

            // By Category
            if (_data.WarningByCategory.Count > 0)
            {
                contentStack.Children.Add(MakeSectionHeader("BY CATEGORY"));
                var catCard = MakeCard();
                var catStack = new StackPanel();
                foreach (var kv in _data.WarningByCategory.OrderByDescending(x => x.Value))
                {
                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.Children.Add(new TextBlock { Text = kv.Key.ToString(), FontSize = 12, Margin = new Thickness(0, 2, 0, 2) });
                    var countTb = new TextBlock { Text = kv.Value.ToString(), FontWeight = FontWeights.Bold, FontSize = 12 };
                    Grid.SetColumn(countTb, 1);
                    row.Children.Add(countTb);
                    // Mini bar
                    double barPct = _data.WarningTotal > 0 ? kv.Value * 100.0 / _data.WarningTotal : 0;
                    var bar = new Border
                    {
                        Background = Br(CAccent), Height = 8, CornerRadius = new CornerRadius(4),
                        Width = Math.Max(4, barPct * 2), HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(bar, 2);
                    row.Children.Add(bar);
                    catStack.Children.Add(row);
                }
                catCard.Child = catStack;
                contentStack.Children.Add(catCard);
            }

            // By Severity
            if (_data.WarningBySeverity.Count > 0)
            {
                contentStack.Children.Add(MakeSectionHeader("BY SEVERITY"));
                var sevCard = MakeCard();
                var sevStack = new StackPanel();
                foreach (WarningSeverity sev in new[] { WarningSeverity.Critical, WarningSeverity.High, WarningSeverity.Medium, WarningSeverity.Low, WarningSeverity.Info })
                {
                    if (_data.WarningBySeverity.TryGetValue(sev, out int cnt) && cnt > 0)
                    {
                        var sevColor = sev == WarningSeverity.Critical ? CRed : sev == WarningSeverity.High ? CAmber :
                            sev == WarningSeverity.Medium ? Color.FromRgb(0xF5, 0xC5, 0x42) : Color.FromRgb(0x90, 0xA4, 0xAE);
                        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                        row.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = Br(sevColor), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
                        row.Children.Add(new TextBlock { Text = $"{sev}: {cnt}", FontSize = 12 });
                        sevStack.Children.Add(row);
                    }
                }
                sevCard.Child = sevStack;
                contentStack.Children.Add(sevCard);
            }

            // By Level (top 10)
            if (_data.WarningByLevel.Count > 0)
            {
                contentStack.Children.Add(MakeSectionHeader("BY LEVEL (top 10)"));
                var lvlCard = MakeCard();
                var lvlStack = new StackPanel();
                foreach (var kv in _data.WarningByLevel.OrderByDescending(x => x.Value).Take(10))
                {
                    lvlStack.Children.Add(new TextBlock { Text = $"  {kv.Key,-25} {kv.Value}", FontSize = 12, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 1, 0, 1) });
                }
                lvlCard.Child = lvlStack;
                contentStack.Children.Add(lvlCard);
            }

            // Hotspot elements (top 10)
            if (_data.WarningHotspots.Count > 0)
            {
                contentStack.Children.Add(MakeSectionHeader("HOTSPOT ELEMENTS"));
                var hotCard = MakeCard();
                var hotStack = new StackPanel();
                foreach (var (name, count) in _data.WarningHotspots.Take(10))
                {
                    hotStack.Children.Add(new TextBlock { Text = $"  {name,-40} {count} warnings", FontSize = 11, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 1, 0, 1) });
                }
                hotCard.Child = hotStack;
                contentStack.Children.Add(hotCard);
            }

            // Regression info
            if (_data.WarningAdded > 0 || _data.WarningRemoved > 0)
            {
                contentStack.Children.Add(MakeSectionHeader("REGRESSION ANALYSIS"));
                var regCard = MakeCard();
                var regStack = new StackPanel();
                regStack.Children.Add(new TextBlock { Text = $"  New warnings since baseline:     +{_data.WarningAdded}", FontSize = 12, Foreground = _data.WarningAdded > 0 ? Br(CRed) : Br(CGreen) });
                regStack.Children.Add(new TextBlock { Text = $"  Resolved since baseline:         -{_data.WarningRemoved}", FontSize = 12, Foreground = Br(CGreen) });
                regCard.Child = regStack;
                contentStack.Children.Add(regCard);
            }

            scroll.Content = contentStack;
            root.Children.Add(scroll);
            return root;
        }

        // ════════════════════════════════════════════════════════════════
        //  ISSUES TAB
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildIssuesTab()
        {
            var root = new DockPanel { Margin = new Thickness(16) };

            // Summary strip
            var summaryWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            DockPanel.SetDock(summaryWrap, Dock.Top);
            summaryWrap.Children.Add(MakeMetricChip($"Total: {_data.IssuesTotal}", Br(CHeaderBg)));
            summaryWrap.Children.Add(MakeMetricChip($"Open: {_data.IssuesOpen}",
                _data.IssuesOpen > 0 ? Br(CAmber) : Br(CGreen)));
            summaryWrap.Children.Add(MakeMetricChip($"Critical: {_data.IssuesCritical}",
                _data.IssuesCritical > 0 ? Br(CRed) : Br(CGreen)));
            summaryWrap.Children.Add(MakeMetricChip($"Overdue: {_data.IssuesOverdue}",
                _data.IssuesOverdue > 0 ? Br(CRed) : Br(CGreen)));
            root.Children.Add(summaryWrap);

            // Actions at bottom
            var actionsWrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(actionsWrap, Dock.Bottom);
            actionsWrap.Children.Add(MakeActionButton("Raise Issue", "RaiseIssue", Br(CRed)));
            actionsWrap.Children.Add(MakeActionButton("Update Status", "UpdateIssue", Br(CAccent)));
            actionsWrap.Children.Add(MakeActionButton("Select Elements", "SelectIssueElements", Br(CHeaderBg)));
            actionsWrap.Children.Add(MakeActionButton("Issue Dashboard", "IssueDashboard", Br(CGreen)));
            actionsWrap.Children.Add(MakeActionButton("Export CSV", "ExportIssues", Br(Color.FromRgb(0x45, 0x50, 0x6E))));
            root.Children.Add(actionsWrap);

            // Placeholder info
            var infoCard = MakeCard();
            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock
            {
                Text = "Issue Tracker integrates with the full Issue Dashboard.",
                FontSize = 13, Margin = new Thickness(0, 0, 0, 8)
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"Open: {_data.IssuesOpen}  |  Critical: {_data.IssuesCritical}  |  Overdue: {_data.IssuesOverdue}",
                FontSize = 14, FontWeight = FontWeights.Bold
            });
            infoStack.Children.Add(new Border { Height = 8 });
            infoStack.Children.Add(new TextBlock
            {
                Text = "Click 'Issue Dashboard' for the full DataGrid view with filtering,\nor 'Raise Issue' to create a new issue with the 4-page wizard.",
                FontSize = 12, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)), TextWrapping = TextWrapping.Wrap
            });
            infoCard.Child = infoStack;
            root.Children.Add(infoCard);

            return root;
        }

        // ════════════════════════════════════════════════════════════════
        //  REVISIONS TAB
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildRevisionsTab()
        {
            var root = new DockPanel { Margin = new Thickness(16) };

            // Summary strip
            var summaryWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            DockPanel.SetDock(summaryWrap, Dock.Top);
            summaryWrap.Children.Add(MakeMetricChip($"Revisions: {_data.RevisionCount}", Br(CHeaderBg)));
            summaryWrap.Children.Add(MakeMetricChip($"Clouds: {_data.RevisionClouds}", Br(CAccent)));
            root.Children.Add(summaryWrap);

            // Actions at bottom
            var actionsWrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(actionsWrap, Dock.Bottom);
            actionsWrap.Children.Add(MakeActionButton("Create Revision", "CreateRevision", Br(CGreen)));
            actionsWrap.Children.Add(MakeActionButton("Take Snapshot", "TakeSnapshot", Br(CAccent)));
            actionsWrap.Children.Add(MakeActionButton("Compare", "RevisionCompare", Br(CHeaderBg)));
            actionsWrap.Children.Add(MakeActionButton("Revision Dashboard", "RevisionDashboard", Br(Color.FromRgb(0x6A, 0x1B, 0x9A))));
            actionsWrap.Children.Add(MakeActionButton("Export CSV", "ExportRevisions", Br(Color.FromRgb(0x45, 0x50, 0x6E))));
            root.Children.Add(actionsWrap);

            // Info card
            var infoCard = MakeCard();
            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock
            {
                Text = $"Revisions: {_data.RevisionCount}  |  Revision Clouds: {_data.RevisionClouds}",
                FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8)
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = "Click 'Revision Dashboard' for the full DataGrid view with filtering\nand snapshot management, or 'Create Revision' to add a new ISO 19650 revision.",
                FontSize = 12, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)), TextWrapping = TextWrapping.Wrap
            });
            infoCard.Child = infoStack;
            root.Children.Add(infoCard);

            return root;
        }

        // ════════════════════════════════════════════════════════════════
        //  PLATFORM TAB
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildPlatformTab()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(20) };
            var stack = new StackPanel();

            // Last sync info
            stack.Children.Add(MakeSectionHeader("PLATFORM SYNC STATUS"));
            var syncCard = MakeCard();
            var syncStack = new StackPanel();
            syncStack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(_data.LastSyncTime) ? "No sync performed yet" : $"Last sync: {_data.LastSyncTime}",
                FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4)
            });
            syncStack.Children.Add(new TextBlock
            {
                Text = $"Changes detected: {_data.SyncChanges}",
                FontSize = 13, Foreground = _data.SyncChanges > 0 ? Br(CAccent) : Br(CGreen)
            });
            syncCard.Child = syncStack;
            stack.Children.Add(syncCard);

            // Actions
            stack.Children.Add(new Border { Height = 12 });
            stack.Children.Add(MakeSectionHeader("PLATFORM OPERATIONS"));
            var actionsWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
            actionsWrap.Children.Add(MakeActionButton("Sync Now", "PlatformSync", Br(CHeaderBg)));
            actionsWrap.Children.Add(MakeActionButton("CDE Package", "CDEPackage", Br(CGreen)));
            actionsWrap.Children.Add(MakeActionButton("BCF Export", "BCFExport", Br(CAccent)));
            actionsWrap.Children.Add(MakeActionButton("BCF Import", "BCFImport", Br(CAccent)));
            actionsWrap.Children.Add(MakeActionButton("Excel Export", "ExcelExport", Br(Color.FromRgb(0x2E, 0x7D, 0x32))));
            actionsWrap.Children.Add(MakeActionButton("Excel Import", "ExcelImport", Br(Color.FromRgb(0x2E, 0x7D, 0x32))));
            actionsWrap.Children.Add(MakeActionButton("SharePoint", "SharePointExport", Br(Color.FromRgb(0x6A, 0x1B, 0x9A))));
            actionsWrap.Children.Add(MakeActionButton("ACC Publish", "ACCPublish", Br(Color.FromRgb(0x45, 0x50, 0x6E))));
            stack.Children.Add(actionsWrap);

            scroll.Content = stack;
            return scroll;
        }

        // ════════════════════════════════════════════════════════════════
        //  WORKFLOWS TAB
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildWorkflowsTab()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(20) };
            var stack = new StackPanel();

            stack.Children.Add(MakeSectionHeader("AVAILABLE WORKFLOW PRESETS"));

            string[][] presets =
            {
                new[] { "Morning Health Check", "Daily coordinator routine: stale fix, warnings, compliance, templates", "8" },
                new[] { "Daily QA Sync", "Adaptive daily sync — skips steps meeting compliance thresholds", "13" },
                new[] { "Handover Readiness", "Pre-handover: validate, COBie, register, BEP, revision", "9" },
                new[] { "Weekly Data Drop", "ISO 19650 information exchange: validate, export, package", "8" },
                new[] { "Project Kickoff", "Full setup: params, materials, types, tags, schedules, views", "28" },
                new[] { "Post-Tagging QA", "Validate results: ISO compliance, tokens, containers, register", "5" },
                new[] { "Document Package", "Batch views, sheets, register, BOQ export", "7" },
                new[] { "BEP Package", "BIM Execution Plan: create, enrich, export, COBie, briefcase", "5" },
            };

            foreach (var p in presets)
            {
                var card = MakeCard();
                var cardGrid = new Grid();
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var leftStack = new StackPanel();
                leftStack.Children.Add(new TextBlock { Text = p[0], FontSize = 13, FontWeight = FontWeights.Bold });
                leftStack.Children.Add(new TextBlock { Text = p[1], FontSize = 11, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)), TextWrapping = TextWrapping.Wrap });
                leftStack.Children.Add(new TextBlock { Text = $"{p[2]} steps", FontSize = 10, Foreground = Br(CAccent), Margin = new Thickness(0, 2, 0, 0) });
                cardGrid.Children.Add(leftStack);

                var runBtn = MakeActionButton("Run", $"RunWorkflow_{p[0].Replace(" ", "")}", Br(CHeaderBg));
                runBtn.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(runBtn, 1);
                cardGrid.Children.Add(runBtn);

                card.Child = cardGrid;
                stack.Children.Add(card);
            }

            // Run history
            stack.Children.Add(new Border { Height = 12 });
            stack.Children.Add(MakeSectionHeader("RUN HISTORY"));
            var histCard = MakeCard();
            var histStack = new StackPanel();
            histStack.Children.Add(new TextBlock
            {
                Text = $"Total runs: {_data.WorkflowRuns}  |  Last: {_data.LastWorkflow}",
                FontSize = 13, FontWeight = FontWeights.Bold
            });
            if (_data.LastComplianceBefore > 0 || _data.LastComplianceAfter > 0)
            {
                histStack.Children.Add(new TextBlock
                {
                    Text = $"Last run compliance: {_data.LastComplianceBefore:F0}% → {_data.LastComplianceAfter:F0}%",
                    FontSize = 12, Margin = new Thickness(0, 4, 0, 0)
                });
            }
            histCard.Child = histStack;
            stack.Children.Add(histCard);

            // Action buttons
            stack.Children.Add(new Border { Height = 12 });
            var actionsWrap = new WrapPanel();
            actionsWrap.Children.Add(MakeActionButton("Run Preset", "RunWorkflowPreset", Br(CHeaderBg)));
            actionsWrap.Children.Add(MakeActionButton("Create Preset", "CreateWorkflowPreset", Br(CGreen)));
            actionsWrap.Children.Add(MakeActionButton("View Trend", "WorkflowTrend", Br(CAccent)));
            actionsWrap.Children.Add(MakeActionButton("List All", "ListWorkflowPresets", Br(Color.FromRgb(0x45, 0x50, 0x6E))));
            stack.Children.Add(actionsWrap);

            scroll.Content = stack;
            return scroll;
        }

        // ════════════════════════════════════════════════════════════════
        //  HELPER METHODS — Cards, KPIs, RAG bars, buttons, etc.
        // ════════════════════════════════════════════════════════════════

        private Border MakeKPICard(string label, string value, SolidColorBrush valueBrush)
        {
            var card = new Border
            {
                Background = Br(CCardBg), BorderBrush = Br(CBorder), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(4)
            };
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = value, FontSize = 28, FontWeight = FontWeights.Bold,
                Foreground = valueBrush, HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = label, FontSize = 10, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0)
            });
            card.Child = stack;
            return card;
        }

        private UIElement MakeRAGBar(string label, double pct)
        {
            pct = Math.Max(0, Math.Min(100, pct));
            var ragColor = pct >= 80 ? CGreen : pct >= 50 ? CAmber : CRed;

            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });

            grid.Children.Add(new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });

            // Bar background
            var barBg = new Border
            {
                Background = Br(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                Height = 14, CornerRadius = new CornerRadius(7),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(barBg, 1);
            grid.Children.Add(barBg);

            // Bar fill
            var barFill = new Border
            {
                Background = Br(ragColor), Height = 14,
                CornerRadius = new CornerRadius(7),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0, VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(barFill, 1);
            grid.Children.Add(barFill);

            // Set width after layout
            barBg.Loaded += (s, e) =>
            {
                barFill.Width = Math.Max(4, barBg.ActualWidth * pct / 100.0);
            };

            var pctText = new TextBlock
            {
                Text = $"{pct:F0}%", FontSize = 12, FontWeight = FontWeights.Bold,
                Foreground = Br(ragColor), HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(pctText, 2);
            grid.Children.Add(pctText);

            return grid;
        }

        private TextBlock MakeSectionHeader(string text)
        {
            return new TextBlock
            {
                Text = text, FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = Br(CHeaderBg), Margin = new Thickness(0, 8, 0, 4),
                Padding = new Thickness(0, 0, 0, 4),
            };
        }

        private Button MakeActionButton(string label, string actionTag, SolidColorBrush bg)
        {
            var btn = new Button
            {
                Content = label, Tag = actionTag,
                Height = 30, Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 6, 6),
                Background = bg, Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 11, Cursor = Cursors.Hand
            };
            btn.Click += ActionBtn_Click;
            return btn;
        }

        private void ActionBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string action)
            {
                ResultAction = action;
                DialogResult = true;
                Close();
            }
        }

        private Border MakeMetricChip(string text, SolidColorBrush bg)
        {
            var chip = new Border
            {
                Background = bg, CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 4)
            };
            chip.Child = new TextBlock
            {
                Text = text, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold
            };
            return chip;
        }

        private Border MakeCard()
        {
            return new Border
            {
                Background = Br(CCardBg), BorderBrush = Br(CBorder), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        private SolidColorBrush RagBrush(string rag)
        {
            return rag == "GREEN" ? Br(CGreen) : rag == "AMBER" ? Br(CAmber) : Br(CRed);
        }

        private void AddCellToGrid(Grid grid, string text, int row, int col, bool isHeader = false,
            FontWeight? weight = null, SolidColorBrush fg = null)
        {
            var tb = new TextBlock
            {
                Text = text, FontSize = 12, Padding = new Thickness(4, 3, 4, 3),
                FontWeight = isHeader ? FontWeights.Bold : weight ?? FontWeights.Normal,
                Foreground = fg ?? Brushes.Black,
                Background = isHeader ? Br(Color.FromRgb(0xE8, 0xEA, 0xED)) : Brushes.Transparent
            };
            Grid.SetRow(tb, row);
            Grid.SetColumn(tb, col);
            grid.Children.Add(tb);
        }

        // ════════════════════════════════════════════════════════════════
        //  STATIC SHOW METHOD
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Show the unified BIM Coordination Center dialog.
        /// Returns an action tag string (e.g., "RunDailyQA", "AutoFixWarnings") or null if closed.
        /// </summary>
        internal static string Show(CoordData data)
        {
            var dlg = new BIMCoordinationCenter(data);
            bool? result = dlg.ShowDialog();
            return (result == true) ? dlg.ResultAction : null;
        }
    }
}
