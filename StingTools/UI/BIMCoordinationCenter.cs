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
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using StingTools.Core;

namespace StingTools.UI
{
    // ══════════════════════════════════════════════════════════════════════
    //  STING BIM COORDINATION CENTER — Unified Corporate Dashboard
    //
    //  Phase 49: Comprehensive BIM coordination platform with 12 tabs:
    //    OVERVIEW | MODEL HEALTH | WARNINGS | ISSUES | REVISIONS |
    //    PLATFORM | WORKFLOWS | QA DASHBOARD | 4D/5D |
    //    DELIVERABLES | COORDINATION LOG | TEAM
    //
    //  Enhancements:
    //    - Interactive drill-down panels with element selection
    //    - Live coordination log with action audit trail
    //    - Deliverables tracker with ISO 19650 data drop milestones
    //    - Team workload dashboard with responsibility matrix
    //    - Enhanced issue/revision tabs with inline filtering + statistics
    //    - Smart action suggestions based on model state analysis
    //    - Predictive analytics for compliance trend forecasting
    //    - Cross-system correlation (warnings ↔ issues ↔ tags)
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

            // Phase 48: Configurable RAG thresholds
            public double RAGGreenThreshold = 80;
            public double RAGAmberThreshold = 50;

            // Phase 48: Container compliance
            public double ContainerCompletePct;

            // Phase 48: Phase-based compliance
            public Dictionary<string, (int Total, int Tagged, double Pct)> ByPhase = new();

            // Phase 48: Placeholder count
            public int PlaceholderCount;

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
            public int WarningSLAViolations;
            public Dictionary<WarningCategory, List<(string Desc, int Count)>> WarningTopByCategory = new();

            // Issues
            public int IssuesOpen;
            public int IssuesCritical;
            public int IssuesOverdue;
            public int IssuesTotal;
            public List<IssueRow> Issues = new();

            // Revisions
            public int RevisionCount;
            public int RevisionClouds;
            public List<RevisionRow> Revisions = new();

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

            // QA Dashboard
            public int AnomalyCount;
            public Dictionary<string, int> ValidationErrors = new();

            // 4D/5D Scheduling
            public int ScheduledTasks;
            public double TotalCostEstimate;
            public int MilestonesTotal;
            public int MilestonesComplete;
            public double EarnedValuePct;
            public List<(string Phase, double Cost, double Progress)> CostByPhase = new();

            // Phase 49: Deliverables tracking
            public List<DeliverableRow> Deliverables = new();
            public int DeliverablesPending;
            public int DeliverablesSubmitted;
            public int DeliverablesApproved;
            public int DeliverablesOverdue;
            public string CurrentDataDrop = "DD1";  // DD1-DD4
            public Dictionary<string, double> DataDropProgress = new(); // DD1→%, DD2→% etc.

            // Phase 49: Coordination log
            public List<CoordLogEntry> CoordLog = new();

            // Phase 49: Team workload
            public List<TeamMemberRow> TeamMembers = new();
            public Dictionary<string, int> TasksByAssignee = new();
            public Dictionary<string, int> IssuesByAssignee = new();

            // Phase 49: Smart suggestions
            public List<(string Text, string Action, string Priority)> SmartSuggestions = new();

            // Phase 49: Compliance trend (last 10 data points)
            public List<(DateTime Date, double Pct)> ComplianceTrend = new();

            // Phase 49: Cross-system correlation
            public int WarningsLinkedToIssues;
            public int StaleLinkedToWarnings;
            public int UnresolvedDependencies;
        }

        /// <summary>Phase 48: Issue data row for DataGrid display.</summary>
        internal class IssueRow
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Type { get; set; }
            public string Priority { get; set; }
            public string Status { get; set; }
            public string Assignee { get; set; }
            public string Created { get; set; }
            public bool IsOverdue { get; set; }
            public string DaysOpen { get; set; }
        }

        /// <summary>Phase 48: Revision data row for DataGrid display.</summary>
        internal class RevisionRow
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Date { get; set; }
            public string Description { get; set; }
            public int Clouds { get; set; }
            public string Status { get; set; }
        }

        /// <summary>Phase 49: Deliverable tracking row.</summary>
        internal class DeliverableRow
        {
            public string Code { get; set; }           // ISO 19650 file code
            public string Name { get; set; }
            public string Type { get; set; }            // Model/Drawing/Schedule/Report/Data
            public string DataDrop { get; set; }        // DD1/DD2/DD3/DD4
            public string Status { get; set; }          // Pending/In Progress/Submitted/Approved/Rejected
            public string Suitability { get; set; }     // S0-S7
            public string CDE { get; set; }             // WIP/SHARED/PUBLISHED/ARCHIVE
            public string Owner { get; set; }
            public string DueDate { get; set; }
            public bool IsOverdue { get; set; }
        }

        /// <summary>Phase 49: Coordination log entry for audit trail.</summary>
        internal class CoordLogEntry
        {
            public string Timestamp { get; set; }
            public string User { get; set; }
            public string Action { get; set; }
            public string Category { get; set; }        // Tag/Issue/Revision/Warning/Workflow/Export/Sync
            public string Detail { get; set; }
            public string Impact { get; set; }           // HIGH/MEDIUM/LOW
        }

        /// <summary>Phase 49: Team member workload row.</summary>
        internal class TeamMemberRow
        {
            public string Name { get; set; }
            public string Role { get; set; }            // BIM Manager/Coordinator/Modeller/Checker
            public int OpenIssues { get; set; }
            public int AssignedTasks { get; set; }
            public int CompletedTasks { get; set; }
            public double CompliancePct { get; set; }
            public string LastActivity { get; set; }
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
                { ResultAction = "ExportReport"; Close(); e.Handled = true; }
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Q)
                { NavigateTo("QA DASHBOARD"); e.Handled = true; }
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
                { NavigateTo("4D/5D"); e.Handled = true; }
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D)
                { NavigateTo("DELIVERABLES"); e.Handled = true; }
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L)
                { NavigateTo("COORD LOG"); e.Handled = true; }
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.T)
                { NavigateTo("TEAM"); e.Handled = true; }
                // Quick-nav: 1-9 for tab by number (first 9 tabs)
                string[] tabKeys = { "OVERVIEW", "MODEL HEALTH", "WARNINGS", "ISSUES", "REVISIONS", "PLATFORM", "WORKFLOWS", "QA DASHBOARD", "4D/5D" };
                if (e.Key >= Key.D1 && e.Key <= Key.D9 && Keyboard.Modifiers == ModifierKeys.None)
                {
                    int idx = (int)(e.Key - Key.D1);
                    if (idx < tabKeys.Length) { NavigateTo(tabKeys[idx]); e.Handled = true; }
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

            string[] tabs = { "OVERVIEW", "MODEL HEALTH", "WARNINGS", "ISSUES", "REVISIONS", "PLATFORM", "WORKFLOWS", "QA DASHBOARD", "4D/5D", "DELIVERABLES", "COORD LOG", "TEAM" };
            string[] badges = {
                "", $"{_data.ModelHealthScore}/100",
                _data.WarningTotal > 0 ? _data.WarningTotal.ToString() : "",
                _data.IssuesOpen > 0 ? _data.IssuesOpen.ToString() : "",
                _data.RevisionCount.ToString(),
                _data.SyncChanges > 0 ? _data.SyncChanges.ToString() : "",
                _data.WorkflowRuns > 0 ? _data.WorkflowRuns.ToString() : "",
                _data.PlaceholderCount > 0 ? _data.PlaceholderCount.ToString() : "",
                _data.ScheduledTasks > 0 ? _data.ScheduledTasks.ToString() : "",
                _data.DeliverablesOverdue > 0 ? _data.DeliverablesOverdue.ToString() : $"{_data.DeliverablesApproved}/{_data.Deliverables.Count}",
                _data.CoordLog.Count > 0 ? _data.CoordLog.Count.ToString() : "",
                _data.TeamMembers.Count > 0 ? _data.TeamMembers.Count.ToString() : ""
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
                case "OVERVIEW":      _contentArea.Content = BuildOverviewTab(); break;
                case "MODEL HEALTH":  _contentArea.Content = BuildModelHealthTab(); break;
                case "WARNINGS":      _contentArea.Content = BuildWarningsTab(); break;
                case "ISSUES":        _contentArea.Content = BuildIssuesTab(); break;
                case "REVISIONS":     _contentArea.Content = BuildRevisionsTab(); break;
                case "PLATFORM":      _contentArea.Content = BuildPlatformTab(); break;
                case "WORKFLOWS":     _contentArea.Content = BuildWorkflowsTab(); break;
                case "QA DASHBOARD":  _contentArea.Content = BuildQADashboardTab(); break;
                case "4D/5D":         _contentArea.Content = Build4D5DTab(); break;
                case "DELIVERABLES":  _contentArea.Content = BuildDeliverablesTab(); break;
                case "COORD LOG":     _contentArea.Content = BuildCoordLogTab(); break;
                case "TEAM":          _contentArea.Content = BuildTeamTab(); break;
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  OVERVIEW TAB
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildOverviewTab()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(20) };
            var stack = new StackPanel();

            // KPI Cards row — Phase 48: 5 cards with hover tooltips and click actions
            var kpiGrid = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, 0, 16) };
            kpiGrid.Children.Add(MakeKPICard("TOTAL ELEMENTS", _data.TotalElements.ToString("N0"), Br(Color.FromRgb(0x15, 0x65, 0xC0)),
                $"Tagged: {_data.TaggedComplete}\nUntagged: {_data.Untagged}\nPlaceholders: {_data.PlaceholderCount}\nStale: {_data.StaleCount}",
                "SelectAllTaggable"));
            kpiGrid.Children.Add(MakeKPICard("TAG COMPLIANCE", $"{_data.TagPct:F1}%", RagBrush(_data.RAGStatus),
                $"Strict (fully resolved): {_data.StrictPct:F1}%\nWith placeholders: {_data.PlaceholderCount}\nSheets: {_data.SheetsTagged}/{_data.SheetsTotal}",
                "ValidateTags"));
            kpiGrid.Children.Add(MakeKPICard("WARNINGS", _data.WarningTotal.ToString(),
                _data.WarningTotal > 20 ? Br(CRed) : _data.WarningTotal > 5 ? Br(CAmber) : Br(CGreen),
                $"Critical: {_data.WarningCritical}\nHigh: {_data.WarningHigh}\nAuto-fixable: {_data.WarningAutoFixable}\nSLA violations: {_data.WarningSLAViolations}\nHealth: {_data.WarningHealthScore}/100",
                "WarningsDashboard"));
            kpiGrid.Children.Add(MakeKPICard("OPEN ISSUES", _data.IssuesOpen.ToString(),
                _data.IssuesOpen > 5 ? Br(CRed) : _data.IssuesOpen > 0 ? Br(CAmber) : Br(CGreen),
                $"Total: {_data.IssuesTotal}\nCritical: {_data.IssuesCritical}\nOverdue: {_data.IssuesOverdue}",
                "IssueDashboard"));
            kpiGrid.Children.Add(MakeKPICard("CONTAINERS", $"{_data.ContainerCompletePct:F0}%",
                _data.ContainerCompletePct >= _data.RAGGreenThreshold ? Br(CGreen) :
                _data.ContainerCompletePct >= _data.RAGAmberThreshold ? Br(CAmber) : Br(CRed),
                $"Discipline container completion\nRequired for COBie export\nand platform deliverables",
                "CombineParameters"));
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
            actionsWrap.Children.Add(MakeActionButton("Repeat Last Workflow", "RepeatLastWorkflow", Br(Color.FromRgb(0x00, 0x69, 0x7C))));
            actionsWrap.Children.Add(MakeActionButton("Full Compliance", "FullComplianceDashboard", Br(Color.FromRgb(0x6A, 0x1B, 0x9A))));
            actionsWrap.Children.Add(MakeActionButton("Document Center", "DocumentManager", Br(Color.FromRgb(0x45, 0x50, 0x6E))));
            stack.Children.Add(actionsWrap);

            // Phase 48b: Action Required section — top 3 priority items
            var actionRequired = new List<(string text, string action, SolidColorBrush color)>();
            if (_data.StaleCount > 0)
                actionRequired.Add(($"{_data.StaleCount} stale elements need re-tagging", "RetagStale", Br(CRed)));
            if (_data.IssuesOverdue > 0)
                actionRequired.Add(($"{_data.IssuesOverdue} overdue issues need attention", "IssueDashboard", Br(CRed)));
            if (_data.WarningCritical > 0)
                actionRequired.Add(($"{_data.WarningCritical} critical warnings need resolution", "AutoFixWarnings", Br(CAmber)));
            if (_data.Untagged > 50)
                actionRequired.Add(($"{_data.Untagged} untagged elements", "TagNewOnly", Br(CAmber)));
            if (_data.PlaceholderCount > 20)
                actionRequired.Add(($"{_data.PlaceholderCount} elements with placeholder tokens", "ResolveAllIssues", Br(CAmber)));
            if (_data.WarningSLAViolations > 0)
                actionRequired.Add(($"{_data.WarningSLAViolations} warnings exceeding SLA", "WarningsDashboard", Br(CRed)));

            if (actionRequired.Count > 0)
            {
                stack.Children.Add(MakeSectionHeader("ACTION REQUIRED"));
                var arCard = new Border
                {
                    Background = Br(Color.FromRgb(0xFF, 0xF8, 0xE1)), BorderBrush = Br(CAmber),
                    BorderThickness = new Thickness(1, 1, 1, 1), CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12)
                };
                var arStack = new StackPanel();
                foreach (var (text, action, color) in actionRequired.Take(5))
                {
                    var arRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
                    arRow.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = color, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
                    var arText = new TextBlock { Text = text, FontSize = 12, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
                    arText.MouseLeftButtonDown += (s, e) => { ResultAction = action; DialogResult = true; Close(); };
                    arText.MouseEnter += (s, e) => arText.TextDecorations = TextDecorations.Underline;
                    arText.MouseLeave += (s, e) => arText.TextDecorations = null;
                    arRow.Children.Add(arText);
                    arStack.Children.Add(arRow);
                }
                arCard.Child = arStack;
                stack.Children.Add(arCard);
            }

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
                    string rag = pct >= _data.RAGGreenThreshold ? "GREEN" : pct >= _data.RAGAmberThreshold ? "AMBER" : "RED";
                    AddCellToGrid(discGrid, kv.Key, row, 0, false, FontWeights.Bold);
                    AddCellToGrid(discGrid, kv.Value.Total.ToString(), row, 1);
                    AddCellToGrid(discGrid, kv.Value.Tagged.ToString(), row, 2);
                    AddCellToGrid(discGrid, $"{pct:F0}%", row, 3, false, FontWeights.Normal, RagBrush(rag));
                    AddCellToGrid(discGrid, rag, row, 4, false, FontWeights.Bold, RagBrush(rag));

                    // Phase 48b: Make entire row clickable — double-click selects discipline elements
                    int rowIdx = row;
                    string disc = kv.Key;
                    for (int c = 0; c < 5; c++)
                    {
                        var cell = discGrid.Children.Cast<UIElement>().LastOrDefault(e => Grid.GetRow(e) == rowIdx && Grid.GetColumn(e) == c);
                        if (cell is TextBlock tb)
                        {
                            tb.Cursor = Cursors.Hand;
                            tb.ToolTip = $"Double-click to select all {disc} elements ({kv.Value.Total} total, {kv.Value.Untagged} untagged)";
                            tb.MouseLeftButtonDown += (s, e) =>
                            {
                                if (e.ClickCount == 2) { ResultAction = $"SelectByDisc_{disc}"; DialogResult = true; Close(); }
                            };
                            tb.MouseEnter += (s, e) => tb.Background = Br(Color.FromRgb(0xE3, 0xF2, 0xFD));
                            tb.MouseLeave += (s, e) => tb.Background = rowIdx % 2 == 0 ? Brushes.Transparent : Br(Color.FromRgb(0xF5, 0xF5, 0xF5));
                        }
                    }
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

            // Phase 48: Phase-based compliance
            if (_data.ByPhase.Count > 0)
            {
                stack.Children.Add(MakeSectionHeader("COMPLIANCE BY PHASE"));
                var phaseCard = MakeCard();
                var phaseStack = new StackPanel();
                foreach (var kv in _data.ByPhase.OrderByDescending(x => x.Value.Total))
                {
                    string pRag = kv.Value.Pct >= _data.RAGGreenThreshold ? "GREEN" : kv.Value.Pct >= _data.RAGAmberThreshold ? "AMBER" : "RED";
                    var phaseRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                    phaseRow.Children.Add(new TextBlock { Text = $"{kv.Key,-20}", FontSize = 12, Width = 160, FontWeight = FontWeights.Bold });
                    phaseRow.Children.Add(new TextBlock { Text = $"{kv.Value.Tagged}/{kv.Value.Total}", FontSize = 12, Width = 80 });
                    phaseRow.Children.Add(new TextBlock { Text = $"{kv.Value.Pct:F0}%", FontSize = 12, FontWeight = FontWeights.Bold,
                        Foreground = RagBrush(pRag), Width = 60 });
                    // Mini progress bar
                    var miniBar = new Border { Background = Br(Color.FromRgb(0xE0, 0xE0, 0xE0)), Height = 8, Width = 120, CornerRadius = new CornerRadius(4),
                        VerticalAlignment = VerticalAlignment.Center };
                    var miniBarGrid = new Grid();
                    miniBarGrid.Children.Add(miniBar);
                    var miniFill = new Border { Background = RagBrush(pRag), Height = 8, CornerRadius = new CornerRadius(4),
                        HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(4, 120 * kv.Value.Pct / 100.0) };
                    miniBarGrid.Children.Add(miniFill);
                    phaseRow.Children.Add(miniBarGrid);
                    phaseStack.Children.Add(phaseRow);
                }
                phaseCard.Child = phaseStack;
                stack.Children.Add(phaseCard);
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

            // Phase 49: Smart suggestions — AI-driven action recommendations
            if (_data.SmartSuggestions.Count > 0)
            {
                stack.Children.Add(MakeSectionHeader("SMART SUGGESTIONS"));
                var sugCard = new Border
                {
                    Background = Br(Color.FromRgb(0xE8, 0xF5, 0xE9)), BorderBrush = Br(CGreen),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12)
                };
                var sugStack = new StackPanel();
                foreach (var (text, action, priority) in _data.SmartSuggestions.Take(6))
                {
                    var prColor = priority == "HIGH" ? CRed : priority == "MEDIUM" ? CAmber : CGreen;
                    var sugRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
                    sugRow.Children.Add(new Border
                    {
                        Background = Br(prColor), CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(4, 1, 4, 1), Margin = new Thickness(0, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock { Text = priority, FontSize = 8, Foreground = Brushes.White, FontWeight = FontWeights.Bold }
                    });
                    var sugBtn = new TextBlock { Text = text, FontSize = 12, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
                    sugBtn.MouseLeftButtonDown += (s, e) => { ResultAction = action; DialogResult = true; Close(); };
                    sugBtn.MouseEnter += (s, e) => sugBtn.TextDecorations = TextDecorations.Underline;
                    sugBtn.MouseLeave += (s, e) => sugBtn.TextDecorations = null;
                    sugRow.Children.Add(sugBtn);
                    sugStack.Children.Add(sugRow);
                }
                sugCard.Child = sugStack;
                stack.Children.Add(sugCard);
            }

            // Phase 49: Compliance trend sparkline
            if (_data.ComplianceTrend.Count >= 2)
            {
                stack.Children.Add(MakeSectionHeader("COMPLIANCE TREND"));
                var trendCard = MakeCard();
                var trendGrid = new Grid { Height = 60 };
                var trendCanvas = new Canvas { ClipToBounds = true };
                trendGrid.Children.Add(trendCanvas);
                trendCard.Loaded += (s, e) =>
                {
                    double w = trendGrid.ActualWidth > 0 ? trendGrid.ActualWidth - 24 : 400;
                    double h = 50;
                    trendCanvas.Children.Clear();
                    // Background grid lines at 25%, 50%, 75%
                    foreach (double pctLine in new[] { 25.0, 50.0, 75.0 })
                    {
                        double y = h - (h * pctLine / 100.0);
                        var gl = new Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = Br(Color.FromRgb(0xE0, 0xE0, 0xE0)), StrokeThickness = 0.5 };
                        trendCanvas.Children.Add(gl);
                    }
                    // Plot points
                    var points = _data.ComplianceTrend;
                    for (int i = 0; i < points.Count - 1; i++)
                    {
                        double x1 = w * i / (points.Count - 1);
                        double y1 = h - (h * Math.Max(0, Math.Min(100, points[i].Pct)) / 100.0);
                        double x2 = w * (i + 1) / (points.Count - 1);
                        double y2 = h - (h * Math.Max(0, Math.Min(100, points[i + 1].Pct)) / 100.0);
                        var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = Br(CAccent), StrokeThickness = 2 };
                        trendCanvas.Children.Add(line);
                        // Dot
                        var dot = new Ellipse { Width = 6, Height = 6, Fill = Br(CHeaderBg) };
                        Canvas.SetLeft(dot, x1 - 3); Canvas.SetTop(dot, y1 - 3);
                        dot.ToolTip = $"{points[i].Date:dd MMM}: {points[i].Pct:F0}%";
                        trendCanvas.Children.Add(dot);
                    }
                    // Last dot
                    if (points.Count > 0)
                    {
                        var lastDot = new Ellipse { Width = 8, Height = 8, Fill = Br(CAccent) };
                        double lx = w; double ly = h - (h * Math.Max(0, Math.Min(100, points.Last().Pct)) / 100.0);
                        Canvas.SetLeft(lastDot, lx - 4); Canvas.SetTop(lastDot, ly - 4);
                        lastDot.ToolTip = $"Latest: {points.Last().Pct:F1}%";
                        trendCanvas.Children.Add(lastDot);
                    }
                };
                trendCard.Child = trendGrid;
                stack.Children.Add(trendCard);
            }

            // Phase 49: Cross-system correlation summary
            if (_data.WarningsLinkedToIssues > 0 || _data.StaleLinkedToWarnings > 0 || _data.UnresolvedDependencies > 0)
            {
                stack.Children.Add(MakeSectionHeader("CROSS-SYSTEM CORRELATION"));
                var corrCard = MakeCard();
                var corrStack = new StackPanel();
                if (_data.WarningsLinkedToIssues > 0)
                    corrStack.Children.Add(new TextBlock { Text = $"  {_data.WarningsLinkedToIssues} warnings linked to open issues", FontSize = 12, Foreground = Br(CAmber) });
                if (_data.StaleLinkedToWarnings > 0)
                    corrStack.Children.Add(new TextBlock { Text = $"  {_data.StaleLinkedToWarnings} stale elements with active warnings", FontSize = 12, Foreground = Br(CRed) });
                if (_data.UnresolvedDependencies > 0)
                    corrStack.Children.Add(new TextBlock { Text = $"  {_data.UnresolvedDependencies} unresolved cross-discipline dependencies", FontSize = 12, Foreground = Br(CAmber) });
                corrCard.Child = corrStack;
                stack.Children.Add(corrCard);
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
            if (_data.WarningSLAViolations > 0)
                summaryWrap.Children.Add(MakeMetricChip($"SLA Violations: {_data.WarningSLAViolations}", Br(CRed)));
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

            // Phase 48b: Warning TreeView — interactive, double-click to select+zoom
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var contentStack = new StackPanel();

            // TREE VIEW: warnings grouped by Category > Severity > Description
            contentStack.Children.Add(MakeSectionHeader("WARNING TREE (double-click to select elements)"));
            var tree = new TreeView
            {
                BorderBrush = Br(CBorder), BorderThickness = new Thickness(1),
                MaxHeight = 300, Margin = new Thickness(0, 0, 0, 12),
                Background = Br(CCardBg)
            };

            // Group: Category → Severity → Description
            var catGroups = _data.WarningByCategory.OrderByDescending(x => x.Value);
            foreach (var catKv in catGroups)
            {
                var catColor = catKv.Key == WarningCategory.Structural || catKv.Key == WarningCategory.Spatial ? CRed :
                    catKv.Key == WarningCategory.MEP ? CAmber : Color.FromRgb(0x37, 0x47, 0x4F);
                var catNode = new TreeViewItem
                {
                    Header = new StackPanel { Orientation = Orientation.Horizontal, Children =
                    {
                        new Ellipse { Width = 10, Height = 10, Fill = Br(catColor), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = $"{catKv.Key} ({catKv.Value})", FontWeight = FontWeights.Bold, FontSize = 12 }
                    }},
                    IsExpanded = catKv.Key == WarningCategory.Spatial || catKv.Key == WarningCategory.MEP
                };

                // Sub-group by severity within category
                if (_data.WarningTopByCategory.TryGetValue(catKv.Key, out var topDescs))
                {
                    foreach (var (desc, count) in topDescs)
                    {
                        var descNode = new TreeViewItem
                        {
                            Header = new TextBlock { Text = $"({count}) {desc}", FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 600 },
                            Tag = $"SelectWarning_{catKv.Key}_{desc}",
                            Cursor = Cursors.Hand,
                            ToolTip = "Double-click to select affected elements in model"
                        };
                        descNode.MouseDoubleClick += (s, e) =>
                        {
                            if (descNode.Tag is string tag)
                            { ResultAction = tag; DialogResult = true; Close(); }
                            e.Handled = true;
                        };
                        catNode.Items.Add(descNode);
                    }
                    if (catKv.Value > topDescs.Sum(t => t.Count))
                    {
                        catNode.Items.Add(new TreeViewItem
                        {
                            Header = new TextBlock { Text = $"...and {catKv.Value - topDescs.Sum(t => t.Count)} more", FontSize = 10,
                                Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)), FontStyle = FontStyles.Italic }
                        });
                    }
                }
                tree.Items.Add(catNode);
            }
            contentStack.Children.Add(tree);

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

            // By Discipline
            if (_data.WarningByDiscipline.Count > 0)
            {
                contentStack.Children.Add(MakeSectionHeader("BY DISCIPLINE"));
                var discCard = MakeCard();
                var discStack = new StackPanel();
                foreach (var kv in _data.WarningByDiscipline.OrderByDescending(x => x.Value))
                {
                    var dRow = new Grid();
                    dRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                    dRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                    dRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    dRow.Children.Add(new TextBlock { Text = kv.Key, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(0, 2, 0, 2) });
                    var cntTb = new TextBlock { Text = kv.Value.ToString(), FontSize = 12 };
                    Grid.SetColumn(cntTb, 1);
                    dRow.Children.Add(cntTb);
                    double dBarPct = _data.WarningTotal > 0 ? kv.Value * 100.0 / _data.WarningTotal : 0;
                    var dBar = new Border { Background = Br(CAccent), Height = 8, CornerRadius = new CornerRadius(4),
                        Width = Math.Max(4, dBarPct * 2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(dBar, 2);
                    dRow.Children.Add(dBar);
                    discStack.Children.Add(dRow);
                }
                discCard.Child = discStack;
                contentStack.Children.Add(discCard);
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
            summaryWrap.Children.Add(MakeMetricChip($"Open: {_data.IssuesOpen}", _data.IssuesOpen > 0 ? Br(CAmber) : Br(CGreen)));
            summaryWrap.Children.Add(MakeMetricChip($"Critical: {_data.IssuesCritical}", _data.IssuesCritical > 0 ? Br(CRed) : Br(CGreen)));
            summaryWrap.Children.Add(MakeMetricChip($"Overdue: {_data.IssuesOverdue}", _data.IssuesOverdue > 0 ? Br(CRed) : Br(CGreen)));
            root.Children.Add(summaryWrap);

            // Actions at bottom
            var actionsWrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(actionsWrap, Dock.Bottom);
            actionsWrap.Children.Add(MakeActionButton("Raise Issue", "RaiseIssue", Br(CRed)));
            actionsWrap.Children.Add(MakeActionButton("Update Status", "UpdateIssue", Br(CAccent)));
            actionsWrap.Children.Add(MakeActionButton("Bulk Close", "IssuesBulkClose", Br(Color.FromRgb(0x6A, 0x1B, 0x9A))));
            actionsWrap.Children.Add(MakeActionButton("Select Elements", "SelectIssueElements", Br(CHeaderBg)));
            actionsWrap.Children.Add(MakeActionButton("BCF Export", "BCFExport", Br(Color.FromRgb(0x00, 0x69, 0x7C))));
            actionsWrap.Children.Add(MakeActionButton("Export CSV", "ExportIssues", Br(Color.FromRgb(0x45, 0x50, 0x6E))));
            actionsWrap.Children.Add(MakeActionButton("Create from Warnings", "CreateIssuesFromWarnings", Br(CAmber)));
            actionsWrap.Children.Add(MakeActionButton("Issue Timeline", "IssueTimeline", Br(CHeaderBg)));
            root.Children.Add(actionsWrap);

            // Phase 49: Issue statistics mini-panel
            if (_data.IssuesTotal > 0)
            {
                var statsPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
                DockPanel.SetDock(statsPanel, Dock.Top);
                // Issue type breakdown
                var typeGroups = _data.Issues.GroupBy(i => i.Type).ToDictionary(g => g.Key, g => g.Count());
                foreach (var kv in typeGroups.OrderByDescending(x => x.Value))
                    statsPanel.Children.Add(MakeMetricChip($"{kv.Key}: {kv.Value}", Br(Color.FromRgb(0x45, 0x50, 0x6E))));
                // Average age
                var ages = _data.Issues.Where(i => int.TryParse(i.DaysOpen, out _)).Select(i => int.Parse(i.DaysOpen)).ToList();
                if (ages.Count > 0)
                    statsPanel.Children.Add(MakeMetricChip($"Avg age: {ages.Average():F0}d", Br(CHeaderBg)));
                // Resolution rate
                int closed = _data.IssuesTotal - _data.IssuesOpen;
                if (_data.IssuesTotal > 0)
                    statsPanel.Children.Add(MakeMetricChip($"Resolved: {closed * 100 / _data.IssuesTotal}%", closed > _data.IssuesOpen ? Br(CGreen) : Br(CAmber)));
                root.Children.Add(statsPanel);
            }

            // Phase 48: Full DataGrid for issues
            if (_data.Issues.Count > 0)
            {
                var dg = new DataGrid
                {
                    AutoGenerateColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column,
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, CanUserSortColumns = true,
                    SelectionMode = DataGridSelectionMode.Single, FontSize = 11,
                    BorderBrush = Br(CBorder), BorderThickness = new Thickness(1),
                    RowHeaderWidth = 0, Margin = new Thickness(0, 0, 0, 8)
                };
                dg.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new Binding("Id"), Width = 80 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Title", Binding = new Binding("Title"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                dg.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new Binding("Type"), Width = 50 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Priority", Binding = new Binding("Priority"), Width = 70 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding("Status"), Width = 65 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Assignee", Binding = new Binding("Assignee"), Width = 90 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Created", Binding = new Binding("Created"), Width = 80 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Age", Binding = new Binding("DaysOpen"), Width = 45 });
                dg.ItemsSource = _data.Issues;
                // Double-click to select issue elements
                dg.MouseDoubleClick += (s, e) =>
                {
                    if (dg.SelectedItem is IssueRow issue)
                    { ResultAction = $"SelectIssue_{issue.Id}"; DialogResult = true; Close(); }
                };
                // Row style: red for overdue, amber for critical
                var rowStyle = new Style(typeof(DataGridRow));
                var overdueT = new DataTrigger { Binding = new Binding("IsOverdue"), Value = true };
                overdueT.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Br(Color.FromRgb(0xFF, 0xEB, 0xEE))));
                rowStyle.Triggers.Add(overdueT);
                var criticalT = new DataTrigger { Binding = new Binding("Priority"), Value = "CRITICAL" };
                criticalT.Setters.Add(new Setter(DataGridRow.FontWeightProperty, FontWeights.Bold));
                rowStyle.Triggers.Add(criticalT);
                dg.RowStyle = rowStyle;
                root.Children.Add(dg);
            }
            else
            {
                var infoCard = MakeCard();
                infoCard.Child = new TextBlock { Text = "No issues found. Click 'Raise Issue' to create one.", FontSize = 13 };
                root.Children.Add(infoCard);
            }
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
            actionsWrap.Children.Add(MakeActionButton("Auto Clouds", "AutoRevisionCloud", Br(CAccent)));
            actionsWrap.Children.Add(MakeActionButton("Take Snapshot", "TakeSnapshot", Br(Color.FromRgb(0x00, 0x69, 0x7C))));
            actionsWrap.Children.Add(MakeActionButton("Compare", "RevisionCompare", Br(CHeaderBg)));
            actionsWrap.Children.Add(MakeActionButton("Track Elements", "TrackElementRevisions", Br(Color.FromRgb(0x6A, 0x1B, 0x9A))));
            actionsWrap.Children.Add(MakeActionButton("Issue Sheets", "IssueSheetsForRevision", Br(CAmber)));
            actionsWrap.Children.Add(MakeActionButton("Naming Check", "RevisionNamingEnforce", Br(Color.FromRgb(0x45, 0x50, 0x6E))));
            actionsWrap.Children.Add(MakeActionButton("Bulk Stamp", "BulkRevisionStamp", Br(CRed)));
            actionsWrap.Children.Add(MakeActionButton("Export CSV", "ExportRevisions", Br(Color.FromRgb(0x45, 0x50, 0x6E))));
            root.Children.Add(actionsWrap);

            // Phase 49: Revision summary statistics
            if (_data.Revisions.Count > 0)
            {
                var revStatsWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
                DockPanel.SetDock(revStatsWrap, Dock.Top);
                int totalClouds = _data.Revisions.Sum(r => r.Clouds);
                var statusGroups = _data.Revisions.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count());
                foreach (var kv in statusGroups)
                    revStatsWrap.Children.Add(MakeMetricChip($"{kv.Key}: {kv.Value}", Br(Color.FromRgb(0x45, 0x50, 0x6E))));
                revStatsWrap.Children.Add(MakeMetricChip($"Total clouds: {totalClouds}", Br(CAccent)));
                root.Children.Add(revStatsWrap);
            }

            // Phase 48: Full DataGrid for revisions
            if (_data.Revisions.Count > 0)
            {
                var dg = new DataGrid
                {
                    AutoGenerateColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column,
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, CanUserSortColumns = true,
                    SelectionMode = DataGridSelectionMode.Single, FontSize = 11,
                    BorderBrush = Br(CBorder), BorderThickness = new Thickness(1), RowHeaderWidth = 0
                };
                dg.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new Binding("Id"), Width = 60 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                dg.Columns.Add(new DataGridTextColumn { Header = "Date", Binding = new Binding("Date"), Width = 90 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Description", Binding = new Binding("Description"), Width = 200 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Clouds", Binding = new Binding("Clouds"), Width = 55 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding("Status"), Width = 65 });
                dg.ItemsSource = _data.Revisions;
                dg.MouseDoubleClick += (s, e) =>
                {
                    if (dg.SelectedItem is RevisionRow rev)
                    { ResultAction = $"ViewRevision_{rev.Id}"; DialogResult = true; Close(); }
                };
                root.Children.Add(dg);
            }
            else
            {
                var infoCard = MakeCard();
                infoCard.Child = new TextBlock { Text = "No revisions found. Click 'Create Revision' to add one.", FontSize = 13 };
                root.Children.Add(infoCard);
            }
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

            // Platform operations by category
            stack.Children.Add(new Border { Height = 12 });
            stack.Children.Add(MakeSectionHeader("CDE — COMMON DATA ENVIRONMENT"));
            var cdeWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
            cdeWrap.Children.Add(MakeActionButton("Sync Now", "PlatformSync", Br(CHeaderBg)));
            cdeWrap.Children.Add(MakeActionButton("CDE Package", "CDEPackage", Br(CGreen)));
            cdeWrap.Children.Add(MakeActionButton("Set CDE Status", "CDEStatus", Br(CAccent)));
            cdeWrap.Children.Add(MakeActionButton("Validate Naming", "ValidateDocNaming", Br(Color.FromRgb(0x45, 0x50, 0x6E))));
            stack.Children.Add(cdeWrap);

            stack.Children.Add(MakeSectionHeader("BCF — BIM COLLABORATION FORMAT"));
            var bcfWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
            bcfWrap.Children.Add(MakeActionButton("BCF Export", "BCFExport", Br(CAccent)));
            bcfWrap.Children.Add(MakeActionButton("BCF Import", "BCFImport", Br(CAccent)));
            bcfWrap.Children.Add(MakeActionButton("Create Transmittal", "CreateTransmittal", Br(Color.FromRgb(0x00, 0x69, 0x7C))));
            stack.Children.Add(bcfWrap);

            stack.Children.Add(MakeSectionHeader("DATA EXCHANGE"));
            var dataWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
            dataWrap.Children.Add(MakeActionButton("Excel Export", "ExportToExcel", Br(Color.FromRgb(0x2E, 0x7D, 0x32))));
            dataWrap.Children.Add(MakeActionButton("Excel Import", "ImportFromExcel", Br(Color.FromRgb(0x2E, 0x7D, 0x32))));
            dataWrap.Children.Add(MakeActionButton("Excel Round-Trip", "ExcelRoundTrip", Br(Color.FromRgb(0x2E, 0x7D, 0x32))));
            dataWrap.Children.Add(MakeActionButton("COBie Export", "COBieExport", Br(Color.FromRgb(0x6A, 0x1B, 0x9A))));
            dataWrap.Children.Add(MakeActionButton("IFC Export", "IFCExport", Br(Color.FromRgb(0x6A, 0x1B, 0x9A))));
            stack.Children.Add(dataWrap);

            stack.Children.Add(MakeSectionHeader("CLOUD PLATFORMS"));
            var cloudWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
            cloudWrap.Children.Add(MakeActionButton("ACC Publish", "ACCPublish", Br(Color.FromRgb(0x45, 0x50, 0x6E))));
            cloudWrap.Children.Add(MakeActionButton("SharePoint", "SharePointExport", Br(Color.FromRgb(0x6A, 0x1B, 0x9A))));
            cloudWrap.Children.Add(MakeActionButton("Document Center", "DocumentManager", Br(CHeaderBg)));
            stack.Children.Add(cloudWrap);

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
                new[] { "Quick Fix Cycle", "Rapid quality improvement: auto-fix, resolve, re-tag, validate", "6" },
                new[] { "Coordination Meeting Prep", "Prepare for BIM coordination meeting: compliance, warnings, reports", "7" },
                new[] { "Clash Coordination", "Cross-discipline: detect clashes, export BCF, create issues", "5" },
                new[] { "Handover Readiness", "Pre-handover: validate, COBie, register, BEP, revision", "9" },
                new[] { "End of Stage Gate", "RIBA stage transition: full validation, COBie, BEP, register", "11" },
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
        //  QA DASHBOARD TAB (Phase 48)
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildQADashboardTab()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(20) };
            var stack = new StackPanel();

            // Token coverage matrix
            stack.Children.Add(MakeSectionHeader("TOKEN COVERAGE MATRIX"));
            var tokenCard = MakeCard();
            var tokenStack = new StackPanel();
            string[] tokenNames = { "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ", "STATUS", "REV" };
            // Header
            var hdr = new Grid();
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hdr.Children.Add(new TextBlock { Text = "Token", FontWeight = FontWeights.Bold, FontSize = 11 });
            var hdrEmpty = new TextBlock { Text = "Empty/Placeholder", FontWeight = FontWeights.Bold, FontSize = 11 };
            Grid.SetColumn(hdrEmpty, 1);
            hdr.Children.Add(hdrEmpty);
            var hdrBar = new TextBlock { Text = "Coverage", FontWeight = FontWeights.Bold, FontSize = 11 };
            Grid.SetColumn(hdrBar, 2);
            hdr.Children.Add(hdrBar);
            tokenStack.Children.Add(hdr);

            foreach (string tk in tokenNames)
            {
                int empty = _data.EmptyTokenCounts.TryGetValue(tk, out int ec) ? ec : 0;
                double pct = _data.TotalElements > 0 ? (_data.TotalElements - empty) * 100.0 / _data.TotalElements : 100;
                string rag = pct >= _data.RAGGreenThreshold ? "GREEN" : pct >= _data.RAGAmberThreshold ? "AMBER" : "RED";

                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                row.Children.Add(new TextBlock { Text = tk, FontSize = 12, FontWeight = FontWeights.Bold });
                var emptyTb = new TextBlock { Text = empty.ToString(), FontSize = 12, Foreground = empty > 0 ? Br(CRed) : Br(CGreen) };
                Grid.SetColumn(emptyTb, 1);
                row.Children.Add(emptyTb);

                var miniBarBg = new Border { Background = Br(Color.FromRgb(0xE0, 0xE0, 0xE0)), Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
                var miniBarFill = new Border { Background = RagBrush(rag), Height = 10, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left };
                var miniBarGrid = new Grid();
                miniBarGrid.Children.Add(miniBarBg);
                miniBarGrid.Children.Add(miniBarFill);
                Grid.SetColumn(miniBarGrid, 2);
                miniBarBg.Loaded += (s, e) => { miniBarFill.Width = Math.Max(4, miniBarBg.ActualWidth * pct / 100.0); };
                row.Children.Add(miniBarGrid);
                tokenStack.Children.Add(row);
            }
            tokenCard.Child = tokenStack;
            stack.Children.Add(tokenCard);

            // Validation errors by type
            if (_data.EmptyTokenCounts.Values.Any(v => v > 0) || _data.AnomalyCount > 0)
            {
                stack.Children.Add(MakeSectionHeader("VALIDATION SUMMARY"));
                var valCard = MakeCard();
                var valStack = new StackPanel();
                valStack.Children.Add(new TextBlock { Text = $"Placeholder elements: {_data.PlaceholderCount}", FontSize = 12, Margin = new Thickness(0, 2, 0, 2) });
                valStack.Children.Add(new TextBlock { Text = $"Anomaly count: {_data.AnomalyCount}", FontSize = 12, Margin = new Thickness(0, 2, 0, 2) });
                if (_data.StaleCount > 0)
                    valStack.Children.Add(new TextBlock { Text = $"Stale elements: {_data.StaleCount}", FontSize = 12, Foreground = Br(CRed), Margin = new Thickness(0, 2, 0, 2) });
                valCard.Child = valStack;
                stack.Children.Add(valCard);
            }

            // Action buttons
            stack.Children.Add(new Border { Height = 12 });
            var actionsWrap = new WrapPanel();
            actionsWrap.Children.Add(MakeActionButton("Validate Tags", "ValidateTags", Br(CHeaderBg)));
            actionsWrap.Children.Add(MakeActionButton("Pre-Tag Audit", "PreTagAudit", Br(CGreen)));
            actionsWrap.Children.Add(MakeActionButton("Auto-Fix Anomalies", "AnomalyAutoFix", Br(CAccent)));
            actionsWrap.Children.Add(MakeActionButton("Resolve All Issues", "ResolveAllIssues", Br(CRed)));
            actionsWrap.Children.Add(MakeActionButton("Tag Register Export", "TagRegisterExport", Br(Color.FromRgb(0x45, 0x50, 0x6E))));
            actionsWrap.Children.Add(MakeActionButton("Completeness Dashboard", "CompletenessDashboard", Br(Color.FromRgb(0x6A, 0x1B, 0x9A))));
            stack.Children.Add(actionsWrap);

            scroll.Content = stack;
            return scroll;
        }

        // ════════════════════════════════════════════════════════════════
        //  4D/5D SCHEDULING TAB (Phase 48)
        // ════════════════════════════════════════════════════════════════

        private UIElement Build4D5DTab()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(20) };
            var stack = new StackPanel();

            // KPI cards
            var kpiGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 16) };
            kpiGrid.Children.Add(MakeKPICard("TASKS", _data.ScheduledTasks.ToString(), Br(Color.FromRgb(0x15, 0x65, 0xC0)),
                "Scheduled construction tasks\nfrom 4D timeline", "AutoSchedule4D"));
            kpiGrid.Children.Add(MakeKPICard("EST. COST", _data.TotalCostEstimate > 0 ? $"£{_data.TotalCostEstimate:N0}" : "N/A",
                Br(Color.FromRgb(0x2E, 0x7D, 0x32)), "5D cost estimate\nfrom cost rate model", "AutoCost5D"));
            kpiGrid.Children.Add(MakeKPICard("MILESTONES", $"{_data.MilestonesComplete}/{_data.MilestonesTotal}",
                _data.MilestonesTotal > 0 && _data.MilestonesComplete == _data.MilestonesTotal ? Br(CGreen) : Br(CAmber),
                "Construction milestones\ncompleted vs total", "MilestoneRegister"));
            kpiGrid.Children.Add(MakeKPICard("EARNED VALUE", _data.EarnedValuePct > 0 ? $"{_data.EarnedValuePct:F0}%" : "N/A",
                _data.EarnedValuePct >= 80 ? Br(CGreen) : _data.EarnedValuePct >= 50 ? Br(CAmber) : Br(CRed),
                "Earned value percentage\n(actual vs planned progress)", "CostReport5D"));
            stack.Children.Add(kpiGrid);

            // Cost by phase
            if (_data.CostByPhase.Count > 0)
            {
                stack.Children.Add(MakeSectionHeader("COST BY PHASE"));
                var costCard = MakeCard();
                var costStack = new StackPanel();
                foreach (var (phase, cost, progress) in _data.CostByPhase)
                {
                    var phaseRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
                    phaseRow.Children.Add(new TextBlock { Text = phase, FontSize = 12, FontWeight = FontWeights.Bold, Width = 160 });
                    phaseRow.Children.Add(new TextBlock { Text = $"£{cost:N0}", FontSize = 12, Width = 100 });
                    phaseRow.Children.Add(new TextBlock { Text = $"{progress:F0}%", FontSize = 12, Foreground = progress >= 80 ? Br(CGreen) : progress >= 50 ? Br(CAmber) : Br(CRed), Width = 50 });
                    var bar = new Border { Background = Br(Color.FromRgb(0xE0, 0xE0, 0xE0)), Height = 8, Width = 120, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
                    var barFill = new Border { Background = Br(CAccent), Height = 8, Width = Math.Max(4, 120 * progress / 100.0), CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left };
                    var barGrid = new Grid();
                    barGrid.Children.Add(bar);
                    barGrid.Children.Add(barFill);
                    phaseRow.Children.Add(barGrid);
                    costStack.Children.Add(phaseRow);
                }
                costCard.Child = costStack;
                stack.Children.Add(costCard);
            }

            // Actions
            stack.Children.Add(new Border { Height = 12 });
            stack.Children.Add(MakeSectionHeader("SCHEDULING OPERATIONS"));
            var actionsWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            actionsWrap.Children.Add(MakeActionButton("Auto Schedule 4D", "AutoSchedule4D", Br(CHeaderBg)));
            actionsWrap.Children.Add(MakeActionButton("Auto Cost 5D", "AutoCost5D", Br(CGreen)));
            actionsWrap.Children.Add(MakeActionButton("View Timeline", "ViewTimeline4D", Br(CAccent)));
            actionsWrap.Children.Add(MakeActionButton("Cost Report", "CostReport5D", Br(Color.FromRgb(0x6A, 0x1B, 0x9A))));
            actionsWrap.Children.Add(MakeActionButton("Cash Flow", "CashFlow5D", Br(Color.FromRgb(0x00, 0x69, 0x7C))));
            actionsWrap.Children.Add(MakeActionButton("Export Schedule", "ExportSchedule4D", Br(Color.FromRgb(0x45, 0x50, 0x6E))));
            actionsWrap.Children.Add(MakeActionButton("Import MS Project", "ImportMSProject", Br(Color.FromRgb(0x45, 0x50, 0x6E))));
            actionsWrap.Children.Add(MakeActionButton("Milestone Register", "MilestoneRegister", Br(CAccent)));
            actionsWrap.Children.Add(MakeActionButton("Phase Summary", "PhaseSummary", Br(CHeaderBg)));
            stack.Children.Add(actionsWrap);

            scroll.Content = stack;
            return scroll;
        }

        // ════════════════════════════════════════════════════════════════
        //  HELPER METHODS — Cards, KPIs, RAG bars, buttons, etc.
        // ════════════════════════════════════════════════════════════════

        private Border MakeKPICard(string label, string value, SolidColorBrush valueBrush,
            string tooltip = null, string clickAction = null)
        {
            var card = new Border
            {
                Background = Br(CCardBg), BorderBrush = Br(CBorder), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(4), Cursor = clickAction != null ? Cursors.Hand : Cursors.Arrow
            };
            var cardStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            cardStack.Children.Add(new TextBlock
            {
                Text = value, FontSize = 28, FontWeight = FontWeights.Bold,
                Foreground = valueBrush, HorizontalAlignment = HorizontalAlignment.Center
            });
            cardStack.Children.Add(new TextBlock
            {
                Text = label, FontSize = 10, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0)
            });
            card.Child = cardStack;

            // Phase 48: Hover tooltip with drill-down details
            if (!string.IsNullOrEmpty(tooltip))
            {
                card.ToolTip = new ToolTip
                {
                    Content = new TextBlock { Text = tooltip, FontFamily = new FontFamily("Segoe UI"), FontSize = 12, MaxWidth = 300, TextWrapping = TextWrapping.Wrap },
                    Background = Br(Color.FromRgb(0x2D, 0x2D, 0x30)),
                    Foreground = Brushes.White, BorderBrush = Br(CAccent),
                    Padding = new Thickness(10, 6, 10, 6)
                };
            }

            // Phase 48: Double-click to drill down
            if (!string.IsNullOrEmpty(clickAction))
            {
                card.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.ClickCount == 2) { ResultAction = clickAction; DialogResult = true; Close(); }
                };
                // Hover effect
                card.MouseEnter += (s, e) => { card.BorderBrush = Br(CAccent); card.BorderThickness = new Thickness(2); };
                card.MouseLeave += (s, e) => { card.BorderBrush = Br(CBorder); card.BorderThickness = new Thickness(1); };
            }
            return card;
        }

        private UIElement MakeRAGBar(string label, double pct)
        {
            pct = Math.Max(0, Math.Min(100, pct));
            var ragColor = pct >= _data.RAGGreenThreshold ? CGreen : pct >= _data.RAGAmberThreshold ? CAmber : CRed;

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
            // Phase 48: Hover effect — lighten on enter, restore on leave
            var origBg = bg;
            btn.MouseEnter += (s, e) =>
            {
                var c = origBg.Color;
                btn.Background = Br(Color.FromRgb(
                    (byte)Math.Min(255, c.R + 30),
                    (byte)Math.Min(255, c.G + 30),
                    (byte)Math.Min(255, c.B + 30)));
            };
            btn.MouseLeave += (s, e) => { btn.Background = origBg; };
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
        //  DELIVERABLES TAB (Phase 49)
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildDeliverablesTab()
        {
            var root = new DockPanel { Margin = new Thickness(16) };

            // Data drop progress strip
            var ddStrip = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            DockPanel.SetDock(ddStrip, Dock.Top);
            ddStrip.Children.Add(MakeSectionHeader("ISO 19650 DATA DROP PROGRESS"));
            var ddGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 4, 0, 12) };
            string[] ddNames = { "DD1", "DD2", "DD3", "DD4" };
            string[] ddLabels = { "Brief & BEP", "Concept Design", "Developed Design", "Production & Handover" };
            for (int i = 0; i < ddNames.Length; i++)
            {
                double ddPct = _data.DataDropProgress.TryGetValue(ddNames[i], out double dp) ? dp : 0;
                bool isCurrent = _data.CurrentDataDrop == ddNames[i];
                var ddCard = new Border
                {
                    Background = isCurrent ? Br(Color.FromRgb(0xE3, 0xF2, 0xFD)) : Br(CCardBg),
                    BorderBrush = isCurrent ? Br(CAccent) : Br(CBorder),
                    BorderThickness = new Thickness(isCurrent ? 2 : 1),
                    CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(3)
                };
                var ddStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                ddStack.Children.Add(new TextBlock { Text = ddNames[i], FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Br(CHeaderBg), HorizontalAlignment = HorizontalAlignment.Center });
                ddStack.Children.Add(new TextBlock { Text = ddLabels[i], FontSize = 9, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)), HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap });
                ddStack.Children.Add(new TextBlock { Text = $"{ddPct:F0}%", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = RagBrush(ddPct >= 80 ? "GREEN" : ddPct >= 50 ? "AMBER" : "RED"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
                if (isCurrent)
                    ddStack.Children.Add(new TextBlock { Text = "CURRENT", FontSize = 8, Foreground = Br(CAccent), FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
                ddCard.Child = ddStack;
                ddGrid.Children.Add(ddCard);
            }
            ddStrip.Children.Add(ddGrid);

            // Summary chips
            var summaryWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            summaryWrap.Children.Add(MakeMetricChip($"Total: {_data.Deliverables.Count}", Br(CHeaderBg)));
            summaryWrap.Children.Add(MakeMetricChip($"Pending: {_data.DeliverablesPending}", _data.DeliverablesPending > 0 ? Br(CAmber) : Br(CGreen)));
            summaryWrap.Children.Add(MakeMetricChip($"Submitted: {_data.DeliverablesSubmitted}", Br(Color.FromRgb(0x15, 0x65, 0xC0))));
            summaryWrap.Children.Add(MakeMetricChip($"Approved: {_data.DeliverablesApproved}", Br(CGreen)));
            summaryWrap.Children.Add(MakeMetricChip($"Overdue: {_data.DeliverablesOverdue}", _data.DeliverablesOverdue > 0 ? Br(CRed) : Br(CGreen)));
            ddStrip.Children.Add(summaryWrap);
            root.Children.Add(ddStrip);

            // Actions at bottom
            var actionsWrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(actionsWrap, Dock.Bottom);
            actionsWrap.Children.Add(MakeActionButton("Add Deliverable", "AddDocument", Br(CGreen)));
            actionsWrap.Children.Add(MakeActionButton("CDE Package", "CDEPackage", Br(CHeaderBg)));
            actionsWrap.Children.Add(MakeActionButton("Create Transmittal", "CreateTransmittal", Br(CAccent)));
            actionsWrap.Children.Add(MakeActionButton("Export Register", "DocumentRegister", Br(Color.FromRgb(0x45, 0x50, 0x6E))));
            actionsWrap.Children.Add(MakeActionButton("Handover Package", "DocumentBriefcase", Br(Color.FromRgb(0x6A, 0x1B, 0x9A))));
            actionsWrap.Children.Add(MakeActionButton("Stage Gate Check", "StageComplianceGate", Br(CRed)));
            root.Children.Add(actionsWrap);

            // Deliverables DataGrid
            if (_data.Deliverables.Count > 0)
            {
                var dg = new DataGrid
                {
                    AutoGenerateColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column,
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, CanUserSortColumns = true,
                    SelectionMode = DataGridSelectionMode.Single, FontSize = 11,
                    BorderBrush = Br(CBorder), BorderThickness = new Thickness(1), RowHeaderWidth = 0
                };
                dg.Columns.Add(new DataGridTextColumn { Header = "Code", Binding = new Binding("Code"), Width = 120 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                dg.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new Binding("Type"), Width = 60 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Drop", Binding = new Binding("DataDrop"), Width = 40 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding("Status"), Width = 80 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Suit.", Binding = new Binding("Suitability"), Width = 35 });
                dg.Columns.Add(new DataGridTextColumn { Header = "CDE", Binding = new Binding("CDE"), Width = 60 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Owner", Binding = new Binding("Owner"), Width = 80 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Due", Binding = new Binding("DueDate"), Width = 80 });
                dg.ItemsSource = _data.Deliverables;
                // Row style: red for overdue
                var rowStyle = new Style(typeof(DataGridRow));
                var overdueT = new DataTrigger { Binding = new Binding("IsOverdue"), Value = true };
                overdueT.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Br(Color.FromRgb(0xFF, 0xEB, 0xEE))));
                overdueT.Setters.Add(new Setter(DataGridRow.FontWeightProperty, FontWeights.Bold));
                rowStyle.Triggers.Add(overdueT);
                dg.RowStyle = rowStyle;
                root.Children.Add(dg);
            }
            else
            {
                var infoCard = MakeCard();
                infoCard.Child = new TextBlock { Text = "No deliverables tracked yet. Use 'Add Deliverable' or run a Document Package workflow to populate.", FontSize = 13 };
                root.Children.Add(infoCard);
            }
            return root;
        }

        // ════════════════════════════════════════════════════════════════
        //  COORDINATION LOG TAB (Phase 49)
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildCoordLogTab()
        {
            var root = new DockPanel { Margin = new Thickness(16) };

            // Summary strip
            var summaryWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            DockPanel.SetDock(summaryWrap, Dock.Top);
            summaryWrap.Children.Add(MakeMetricChip($"Total entries: {_data.CoordLog.Count}", Br(CHeaderBg)));
            var catGroups = _data.CoordLog.GroupBy(e => e.Category).ToDictionary(g => g.Key, g => g.Count());
            foreach (var kv in catGroups.OrderByDescending(x => x.Value).Take(5))
            {
                var catColor = kv.Key == "Issue" ? CRed : kv.Key == "Warning" ? CAmber :
                    kv.Key == "Tag" ? CGreen : kv.Key == "Workflow" ? Color.FromRgb(0x6A, 0x1B, 0x9A) : CHeaderBg;
                summaryWrap.Children.Add(MakeMetricChip($"{kv.Key}: {kv.Value}", Br(catColor)));
            }
            root.Children.Add(summaryWrap);

            // Actions at bottom
            var actionsWrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(actionsWrap, Dock.Bottom);
            actionsWrap.Children.Add(MakeActionButton("Export Log CSV", "ExportCoordLog", Br(CHeaderBg)));
            actionsWrap.Children.Add(MakeActionButton("Clear Log", "ClearCoordLog", Br(Color.FromRgb(0x45, 0x50, 0x6E))));
            actionsWrap.Children.Add(MakeActionButton("Run Audit Trail", "WorkflowTrend", Br(CAccent)));
            root.Children.Add(actionsWrap);

            // Log entries DataGrid
            if (_data.CoordLog.Count > 0)
            {
                var dg = new DataGrid
                {
                    AutoGenerateColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column,
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, CanUserSortColumns = true,
                    SelectionMode = DataGridSelectionMode.Single, FontSize = 11,
                    BorderBrush = Br(CBorder), BorderThickness = new Thickness(1), RowHeaderWidth = 0,
                    EnableRowVirtualization = true
                };
                dg.Columns.Add(new DataGridTextColumn { Header = "Time", Binding = new Binding("Timestamp"), Width = 130 });
                dg.Columns.Add(new DataGridTextColumn { Header = "User", Binding = new Binding("User"), Width = 80 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Category", Binding = new Binding("Category"), Width = 70 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Action", Binding = new Binding("Action"), Width = 150 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Detail", Binding = new Binding("Detail"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                dg.Columns.Add(new DataGridTextColumn { Header = "Impact", Binding = new Binding("Impact"), Width = 55 });
                dg.ItemsSource = _data.CoordLog;
                // Row style by impact
                var rowStyle = new Style(typeof(DataGridRow));
                var highT = new DataTrigger { Binding = new Binding("Impact"), Value = "HIGH" };
                highT.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Br(Color.FromRgb(0xFF, 0xEB, 0xEE))));
                rowStyle.Triggers.Add(highT);
                var medT = new DataTrigger { Binding = new Binding("Impact"), Value = "MEDIUM" };
                medT.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Br(Color.FromRgb(0xFF, 0xF8, 0xE1))));
                rowStyle.Triggers.Add(medT);
                dg.RowStyle = rowStyle;
                root.Children.Add(dg);
            }
            else
            {
                var infoCard = MakeCard();
                infoCard.Child = new TextBlock
                {
                    Text = "No coordination log entries yet.\n\nThe log captures all BIM coordination actions:\n  - Tagging operations (batch tag, auto-tag, re-tag)\n  - Issue creation and resolution\n  - Revision creation and tracking\n  - Warning auto-fix operations\n  - Workflow execution results\n  - Platform sync and export events\n  - Compliance gate checks\n\nRun any coordination command to start populating the log.",
                    FontSize = 12, TextWrapping = TextWrapping.Wrap
                };
                root.Children.Add(infoCard);
            }
            return root;
        }

        // ════════════════════════════════════════════════════════════════
        //  TEAM TAB (Phase 49)
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildTeamTab()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(20) };
            var stack = new StackPanel();

            // Team workload overview
            stack.Children.Add(MakeSectionHeader("TEAM WORKLOAD OVERVIEW"));
            if (_data.TeamMembers.Count > 0)
            {
                // KPI cards: Team summary
                var kpiGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 16) };
                int totalOpen = _data.TeamMembers.Sum(m => m.OpenIssues);
                int totalAssigned = _data.TeamMembers.Sum(m => m.AssignedTasks);
                int totalCompleted = _data.TeamMembers.Sum(m => m.CompletedTasks);
                double avgCompliance = _data.TeamMembers.Average(m => m.CompliancePct);
                kpiGrid.Children.Add(MakeKPICard("TEAM SIZE", _data.TeamMembers.Count.ToString(), Br(CHeaderBg),
                    "Active team members\nwith assigned responsibilities"));
                kpiGrid.Children.Add(MakeKPICard("OPEN TASKS", totalAssigned.ToString(),
                    totalAssigned > 20 ? Br(CRed) : totalAssigned > 10 ? Br(CAmber) : Br(CGreen),
                    $"Assigned: {totalAssigned}\nCompleted: {totalCompleted}"));
                kpiGrid.Children.Add(MakeKPICard("OPEN ISSUES", totalOpen.ToString(),
                    totalOpen > 10 ? Br(CRed) : totalOpen > 5 ? Br(CAmber) : Br(CGreen),
                    "Issues assigned across team"));
                kpiGrid.Children.Add(MakeKPICard("AVG COMPLIANCE", $"{avgCompliance:F0}%",
                    RagBrush(avgCompliance >= 80 ? "GREEN" : avgCompliance >= 50 ? "AMBER" : "RED"),
                    "Average tag compliance\nacross team members"));
                stack.Children.Add(kpiGrid);

                // Team member cards
                stack.Children.Add(MakeSectionHeader("TEAM MEMBERS"));
                foreach (var member in _data.TeamMembers.OrderByDescending(m => m.OpenIssues + m.AssignedTasks))
                {
                    var memberCard = MakeCard();
                    var memberGrid = new Grid();
                    memberGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    memberGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var leftStack = new StackPanel();
                    var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
                    nameRow.Children.Add(new TextBlock { Text = member.Name, FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 8, 0) });
                    nameRow.Children.Add(new Border
                    {
                        Background = Br(Color.FromRgb(0x45, 0x50, 0x6E)), CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 1, 6, 1), VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock { Text = member.Role, FontSize = 9, Foreground = Brushes.White }
                    });
                    leftStack.Children.Add(nameRow);

                    var statsRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                    if (member.OpenIssues > 0)
                        statsRow.Children.Add(MakeMetricChip($"Issues: {member.OpenIssues}", member.OpenIssues > 5 ? Br(CRed) : Br(CAmber)));
                    statsRow.Children.Add(MakeMetricChip($"Tasks: {member.AssignedTasks}", Br(CHeaderBg)));
                    statsRow.Children.Add(MakeMetricChip($"Done: {member.CompletedTasks}", Br(CGreen)));
                    leftStack.Children.Add(statsRow);

                    if (!string.IsNullOrEmpty(member.LastActivity))
                        leftStack.Children.Add(new TextBlock { Text = $"Last active: {member.LastActivity}", FontSize = 10, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)), Margin = new Thickness(0, 2, 0, 0) });
                    memberGrid.Children.Add(leftStack);

                    // Compliance indicator
                    var compStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 0, 0) };
                    string mRag = member.CompliancePct >= 80 ? "GREEN" : member.CompliancePct >= 50 ? "AMBER" : "RED";
                    compStack.Children.Add(new TextBlock { Text = $"{member.CompliancePct:F0}%", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = RagBrush(mRag), HorizontalAlignment = HorizontalAlignment.Center });
                    compStack.Children.Add(new TextBlock { Text = "compliance", FontSize = 9, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)), HorizontalAlignment = HorizontalAlignment.Center });
                    Grid.SetColumn(compStack, 1);
                    memberGrid.Children.Add(compStack);

                    memberCard.Child = memberGrid;
                    stack.Children.Add(memberCard);
                }
            }
            else
            {
                // No team data — show role-based responsibility matrix template
                stack.Children.Add(new TextBlock { Text = "No team data available yet.", FontSize = 13, Margin = new Thickness(0, 0, 0, 12) });
            }

            // Responsibility matrix
            stack.Children.Add(MakeSectionHeader("RESPONSIBILITY MATRIX (RACI)"));
            var raciCard = MakeCard();
            var raciStack = new StackPanel();
            var raciItems = new[]
            {
                ("Tag Compliance", "BIM Manager", "Coordinator", "Modeller", "Checker"),
                ("Issue Resolution", "Coordinator", "Modeller", "—", "Manager"),
                ("Revision Creation", "Manager", "Coordinator", "—", "Manager"),
                ("COBie Export", "Coordinator", "Manager", "—", "Manager"),
                ("Model Health", "Manager", "Coordinator", "Modeller", "—"),
                ("Warning Resolution", "Coordinator", "Modeller", "—", "Manager"),
                ("Data Drop Submission", "Manager", "Coordinator", "—", "Client"),
            };
            // Header
            var raciHdr = new Grid();
            raciHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            raciHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            raciHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            raciHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            raciHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            var raciHeaders = new[] { "Task", "Responsible", "Accountable", "Consulted", "Informed" };
            for (int c = 0; c < raciHeaders.Length; c++)
            {
                var tb = new TextBlock { Text = raciHeaders[c], FontWeight = FontWeights.Bold, FontSize = 11, Padding = new Thickness(4, 3, 4, 3), Background = Br(Color.FromRgb(0xE8, 0xEA, 0xED)) };
                Grid.SetColumn(tb, c);
                raciHdr.Children.Add(tb);
            }
            raciStack.Children.Add(raciHdr);

            foreach (var (task, r, a, c, i) in raciItems)
            {
                var raciRow = new Grid();
                raciRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                raciRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                raciRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                raciRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                raciRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                var vals = new[] { task, r, a, c, i };
                for (int col = 0; col < vals.Length; col++)
                {
                    var tb = new TextBlock { Text = vals[col], FontSize = 11, Padding = new Thickness(4, 2, 4, 2), FontWeight = col == 0 ? FontWeights.Bold : FontWeights.Normal };
                    Grid.SetColumn(tb, col);
                    raciRow.Children.Add(tb);
                }
                raciStack.Children.Add(raciRow);
            }
            raciCard.Child = raciStack;
            stack.Children.Add(raciCard);

            // Actions
            stack.Children.Add(new Border { Height = 12 });
            var teamActionsWrap = new WrapPanel();
            teamActionsWrap.Children.Add(MakeActionButton("Assign Issues", "IssueBatchUpdate", Br(CHeaderBg)));
            teamActionsWrap.Children.Add(MakeActionButton("Team Report", "ExportReport", Br(CGreen)));
            teamActionsWrap.Children.Add(MakeActionButton("Compliance by Discipline", "DiscComplianceReport", Br(CAccent)));
            stack.Children.Add(teamActionsWrap);

            scroll.Content = stack;
            return scroll;
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
