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

        private static SolidColorBrush Br(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        // M-02: Cached hover/alternate brushes for discipline row events
        private static readonly SolidColorBrush RowHoverBrush = Br(Color.FromRgb(0xE3, 0xF2, 0xFD));
        private static readonly SolidColorBrush RowAltBrush = Br(Color.FromRgb(0xF5, 0xF5, 0xF5));

        // M-05: Tab name constants — single source of truth for all navigation references
        private const string TabOverview     = "OVERVIEW";
        private const string TabModelHealth  = "MODEL HEALTH";
        private const string TabWarnings     = "WARNINGS";
        private const string TabIssues       = "ISSUES";
        private const string TabRevisions    = "REVISIONS";
        private const string TabPlatform     = "PLATFORM";
        private const string TabWorkflows    = "WORKFLOWS";
        private const string TabQA           = "QA DASHBOARD";
        private const string Tab4D5D         = "4D/5D";
        private const string TabDeliverables = "DELIVERABLES";
        private const string TabMeetings     = "MEETINGS";
        private const string TabPermissions  = "PERMISSIONS";
        private const string TabCoordLog     = "COORD LOG";
        private const string TabTeam         = "TEAM";

        // ── Data ──
        private CoordData _data;
        private readonly ContentControl _contentArea;
        private readonly TextBlock _statusBar;
        private readonly StackPanel _navPanel;
        private Button _activeNav;

        // Phase 75: Persist last-viewed tab across dialog reopens
        private static string _lastViewedTab = TabOverview;

        // Phase 76: Static warning element IDs selected from BCC Warnings DataGrid (stored as long values)
        public static IReadOnlyList<long> SelectedWarningIds { get; private set; } = new List<long>();

        internal static void SetSelectedWarningIds(IEnumerable<long> ids)
            => SelectedWarningIds = new List<long>(ids ?? Array.Empty<long>());

        // Phase 76: Last shown permissions (for SavePermissionsInline)
        private static List<RoleDefinition>   _lastPermissionsRoles   = new();
        private static List<FolderPermission> _lastPermissionsFolders = new();

        internal static List<RoleDefinition>   GetLastPermissionsRoles()   => _lastPermissionsRoles.Count   > 0 ? _lastPermissionsRoles   : GetDefaultRoles();
        internal static List<FolderPermission> GetLastPermissionsFolders() => _lastPermissionsFolders.Count > 0 ? _lastPermissionsFolders : GetDefaultFolderPermissions();

        // Phase 76: Singleton for modeless BCC
        public static BIMCoordinationCenter CurrentInstance { get; private set; }

        // Phase 76: Delegate set by BIMCoordinationCenterCommand to dispatch actions via ExternalEvent
        internal static Action<string> ActionDispatcher { get; set; }

        private void DispatchAction(string action)
        {
            ActionDispatcher?.Invoke(action);
            Dispatcher.BeginInvoke(new Action(() => { Activate(); Focus(); }),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        // BCC-HIGH-01: Cache built tab content — avoids rebuilding full visual tree on every NavigateTo
        private readonly Dictionary<string, UIElement> _tabCache = new Dictionary<string, UIElement>();

        // BCC-HIGH-01: Live-data tabs are cleared from cache on NavigateTo so they always reflect current state.
        // Layout-only tabs (PLATFORM, 4D/5D, DELIVERABLES, PERMISSIONS, COORD LOG, TEAM, MEETINGS) are cached.
        private static readonly HashSet<string> _liveDataTabs = new HashSet<string>(StringComparer.Ordinal)
            { TabOverview, TabModelHealth, TabWarnings, TabIssues, TabRevisions, TabWorkflows, TabQA };

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
            // Phase 87: Extended warning data for Ideate-level Warnings tab
            public int WarningManualReview;
            public double WarningAvgCriticalAgeHours;
            public Dictionary<string, int> WarningByWorkset = new();
            public List<(string Desc, WarningCategory Cat, WarningSeverity Sev, int Count, bool CanAutoFix, string Fix)> WarningRootCauseGroups = new();
            public int WarningImpactCOBie;
            public int WarningImpactIFC;
            public int WarningImpactHandover;
            public int WarningImpactSchedules;
            public int WarningImpactClash;
            public string WarningHighestImpactArea = "";

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
            public Dictionary<string, int> CloudsByDiscipline = new();
            public Dictionary<string, int> CloudsBySheet = new();
            public int RevisionsThisWeek { get; set; }
            public int CloudsUnresolved { get; set; }

            // Platform
            public string LastSyncTime = "";
            public int SyncChanges;

            // Workflows
            public int WorkflowRuns;
            public string LastWorkflow = "none";
            public double LastComplianceBefore { get; set; }
            public double LastComplianceAfter { get; set; }

            // Model Health
            public int ModelHealthScore;
            public string ModelHealthRating = "FAIR";
            public List<(string Check, int Score, int Max, string Detail)> HealthChecks = new();
            public List<string> Recommendations = new();

            // QA Dashboard
            public int AnomalyCount { get; set; }
            public Dictionary<string, int> ValidationErrors = new();

            // Workflow history
            public List<WorkflowRunRow> WorkflowHistory = new();

            // 4D/5D Scheduling
            public int ScheduledTasks { get; set; }
            public double TotalCostEstimate { get; set; }
            public int MilestonesTotal { get; set; }
            public int MilestonesComplete { get; set; }
            public double EarnedValuePct { get; set; }
            public List<(string Phase, double Cost, double Progress)> CostByPhase = new();
            public List<(string Name, string Phase, double PlannedPct, double ActualPct, bool IsComplete)> Milestones = new();
            public double PlannedValuePct { get; set; }
            public double CostVariancePct { get; set; }
            public double ScheduleVariancePct { get; set; }
            public double CostPerformanceIndex { get; set; }
            public double SchedulePerformanceIndex { get; set; }
            public List<(string Month, double Planned, double Actual)> CashFlowForecast = new();

            // Phase 49: Deliverables tracking
            public List<DeliverableRow> Deliverables = new();
            public int DeliverablesPending { get; set; }
            public int DeliverablesSubmitted { get; set; }
            public int DeliverablesApproved { get; set; }
            public int DeliverablesOverdue { get; set; }
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

            // Permission & Access Control
            public string CurrentUserRole = "BIM Manager";
            public string CurrentUserName = "";
            public List<FolderPermission> FolderPermissions = new();
            public List<RoleDefinition> Roles = new();

            // Compliance forecasting
            public double ForecastedCompliancePct;
            public string ForecastLabel = "";

            // SLA violations detail
            public int SLACriticalViolations;
            public int SLAHighViolations;
            public double AvgCriticalAgeHours;
        }

        /// <summary>Phase 48: Issue data row for DataGrid display. Multi-assignee support.</summary>
        internal class IssueRow
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Type { get; set; }
            public string Priority { get; set; }
            public string Status { get; set; }
            public string Assignee { get; set; }
            /// <summary>Comma-separated list of all assignees for multi-assignee display.</summary>
            public string Assignees { get; set; }
            /// <summary>Individual assignee list for programmatic access.</summary>
            public List<string> AssigneeList { get; set; } = new();
            public string Created { get; set; }
            public bool IsOverdue { get; set; }
            public string DaysOpen { get; set; }
            public string Discipline { get; set; }
            public string Revision { get; set; }
            public int ElementCount { get; set; }
            /// <summary>Display string: shows first assignee + N more if multi-assigned.</summary>
            public string AssigneeDisplay => AssigneeList.Count <= 1 ? (Assignee ?? "") :
                $"{AssigneeList[0]} +{AssigneeList.Count - 1}";
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

        /// <summary>ISO 19650 folder permission definition.</summary>
        internal class FolderPermission
        {
            public string Folder { get; set; }         // e.g., "01_WIP", "02_SHARED"
            public string CDEState { get; set; }        // WIP/SHARED/PUBLISHED/ARCHIVE
            public string ReadRoles { get; set; }       // Comma-separated role codes: "A,M,E,S"
            public string WriteRoles { get; set; }      // Comma-separated role codes: "A"
            public string ApproveRoles { get; set; }    // Comma-separated: "K,A" (Client, Architect)
            public bool IsLocked { get; set; }          // True if folder is locked for edits
            public string Description { get; set; }
        }

        /// <summary>ISO 19650 team role definition.</summary>
        internal class RoleDefinition
        {
            public string Code { get; set; }            // A, M, E, S, etc.
            public string Name { get; set; }            // "Architect", "Mechanical Engineer"
            public string Discipline { get; set; }      // "Architecture", "Mechanical"
            public string CDEAccess { get; set; }       // "WIP,SHARED" — CDE states this role can write to
            public string CanApprove { get; set; }      // "true"/"false" — can approve transitions
            public string CanIssue { get; set; }        // "true"/"false" — can issue transmittals
            public string Members { get; set; }         // Comma-separated team member names
        }

        /// <summary>Workflow execution history row.</summary>
        internal class WorkflowRunRow
        {
            public string Timestamp { get; set; }
            public string Preset { get; set; }
            public int Steps { get; set; }
            public int Passed { get; set; }
            public int Failed { get; set; }
            public int Skipped { get; set; }
            public string Duration { get; set; }
            public double CompBefore { get; set; }
            public double CompAfter { get; set; }
            public string User { get; set; }
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
            catch (Exception exOwner) { StingLog.Warn($"BIMCoordCenter window owner: {exOwner.Message}"); }

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });   // Header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });   // Share toolbar
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Body
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });   // Status

            // ── HEADER ──
            root.Children.Add(BuildHeader());

            // ── SHARE TOOLBAR ──
            var shareToolbar = BuildShareToolbar();
            Grid.SetRow(shareToolbar, 1);
            root.Children.Add(shareToolbar);

            // ── BODY (Nav + Content) ──
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) }); // Nav
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Content
            Grid.SetRow(body, 2);

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
            Grid.SetRow(statusBorder, 3);
            root.Children.Add(statusBorder);

            Content = root;

            // Keyboard shortcuts
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) Close();
                if (e.Key == Key.F5) { NavigateTo(TabOverview); e.Handled = true; }
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.E)
                { ResultAction = "ExportReport"; Close(); e.Handled = true; }
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Q)
                { NavigateTo(TabQA); e.Handled = true; }
                if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S)
                { NavigateTo(Tab4D5D); e.Handled = true; }
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D)
                { NavigateTo(TabDeliverables); e.Handled = true; }
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L)
                { NavigateTo(TabCoordLog); e.Handled = true; }
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.T)
                { NavigateTo(TabTeam); e.Handled = true; }
                // Quick-nav: 1-9 for tab by number (first 9 tabs)
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.M)
                { NavigateTo(TabMeetings); e.Handled = true; }
                string[] tabKeys = { TabOverview, TabModelHealth, TabWarnings, TabIssues, TabRevisions, TabPlatform, TabWorkflows, TabQA, Tab4D5D };
                if (e.Key >= Key.D1 && e.Key <= Key.D9 && Keyboard.Modifiers == ModifierKeys.None
                    && !(e.OriginalSource is System.Windows.Controls.TextBox))
                {
                    int idx = (int)(e.Key - Key.D1);
                    if (idx < tabKeys.Length) { NavigateTo(tabKeys[idx]); e.Handled = true; }
                }
            };

            // Phase 75: Restore last-viewed tab across dialog reopens (preserves user context)
            NavigateTo(_lastViewedTab ?? TabOverview);

            // Phase 76: Register singleton instance
            CurrentInstance = this;
            Closed += (s, e) => { CurrentInstance = null; };
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
        //  SHARE TOOLBAR — snapshot / export strip below the header
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildShareToolbar()
        {
            var toolbar = new Border
            {
                Background = Br(Color.FromRgb(0xF8, 0xF9, 0xFB)),
                BorderBrush = Br(Color.FromRgb(0xD0, 0xD5, 0xE0)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(8, 0, 8, 0)
            };
            var wrap = new WrapPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Orientation = Orientation.Horizontal
            };

            var shareLabel = new TextBlock
            {
                Text = "Share:",
                FontSize = 10,
                Foreground = Br(Color.FromRgb(0x88, 0x88, 0x99)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            wrap.Children.Add(shareLabel);
            wrap.Children.Add(MakeShareButton("📷 Snapshot", "BCCSnapshot"));
            wrap.Children.Add(MakeShareButton("📄 PDF",      "BCCExportPDF"));
            wrap.Children.Add(MakeShareButton("📊 Excel",    "BCCExportExcel"));
            wrap.Children.Add(MakeShareButton("📝 Word",     "BCCExportWord"));

            toolbar.Child = wrap;
            return toolbar;
        }

        private Button MakeShareButton(string label, string tag)
        {
            var btn = new Button
            {
                Content = label,
                Tag = tag,
                Height = 22,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 4, 4, 4),
                Background = Brushes.White,
                Foreground = Br(Color.FromRgb(0x33, 0x44, 0x66)),
                BorderBrush = Br(Color.FromRgb(0xC0, 0xC8, 0xD8)),
                BorderThickness = new Thickness(1),
                FontSize = 10,
                Cursor = Cursors.Hand
            };
            btn.Click += ActionBtn_Click;
            btn.MouseEnter += (s, e) => { btn.Background = Br(Color.FromRgb(0xE8, 0xF0, 0xFF)); };
            btn.MouseLeave += (s, e) => { btn.Background = Brushes.White; };
            return btn;
        }

        // ════════════════════════════════════════════════════════════════
        //  NAVIGATION PANEL
        // ════════════════════════════════════════════════════════════════

        private StackPanel BuildNavPanel()
        {
            var nav = new StackPanel { Background = Br(CNavBg) };
            nav.Children.Add(new Border { Height = 8 }); // top spacer

            string[] tabs = { TabOverview, TabModelHealth, TabWarnings, TabIssues, TabRevisions, TabPlatform, TabWorkflows, TabQA, Tab4D5D, TabDeliverables, TabMeetings, TabPermissions, TabCoordLog, TabTeam };
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
                "", // MEETINGS
                _data.Roles.Count > 0 ? _data.Roles.Count.ToString() : "", // PERMISSIONS
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
            // Phase 75: Remember tab for cross-reopen persistence
            _lastViewedTab = tabName;

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

            // BCC-HIGH-01: Live-data tabs rebuilt on every navigation; layout tabs served from class-level cache.
            if (_liveDataTabs.Contains(tabName))
                _tabCache.Remove(tabName);

            if (!_tabCache.TryGetValue(tabName, out var tabContent))
            {
                tabContent = tabName switch
                {
                    TabOverview     => BuildOverviewTab(),
                    TabModelHealth  => BuildModelHealthTab(),
                    TabWarnings     => BuildWarningsTab(),
                    TabIssues       => BuildIssuesTab(),
                    TabRevisions    => BuildRevisionsTab(),
                    TabPlatform     => BuildPlatformTab(),
                    TabWorkflows    => BuildWorkflowsTab(),
                    TabQA           => BuildQADashboardTab(),
                    Tab4D5D         => Build4D5DTab(),
                    TabDeliverables => BuildDeliverablesTab(),
                    TabMeetings     => BuildMeetingsTab(),
                    TabPermissions  => BuildPermissionsTab(),
                    TabCoordLog     => BuildCoordLogTab(),
                    TabTeam         => BuildTeamTab(),
                    _               => new TextBlock { Text = $"Unknown tab: {tabName}" }
                };
                _tabCache[tabName] = tabContent;
            }
            _contentArea.Content = tabContent;
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
            actionsWrap.Children.Add(MakeActionButton("New Meeting", "NewMeeting", Br(Color.FromRgb(0x00, 0x69, 0x7C)),
                "Create a new coordination meeting: BIM Coordination, Design Review, Client Review, Handover, or Clash Resolution"));
            actionsWrap.Children.Add(MakeActionButton("Take Snapshot", "TakeSnapshot", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Capture model compliance state for meeting record — saves tag %, warnings, stale count to snapshots.json"));
            actionsWrap.Children.Add(MakeActionButton("Validate Tags", "ValidateTags", Br(CGreen),
                "Run ISO 19650 tag validation — checks all tokens, cross-validates DISC/SYS, reports 4-bucket compliance"));
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
                    var arText = new TextBlock { Text = text, FontSize = 12, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = $"Click to execute: {action}\n{GetActionTooltip(action) ?? "Run this action to resolve the issue"}" };
                    arText.MouseLeftButtonDown += (s, e) => { DispatchAction(action); };
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
                    // BCC-HIGH-02: Capture returned TextBlock directly — no Cast().LastOrDefault() search
                    int rowIdx = row;
                    string disc = kv.Key;
                    var rowCells = new[]
                    {
                        AddCellToGrid(discGrid, kv.Key, row, 0, false, FontWeights.Bold),
                        AddCellToGrid(discGrid, kv.Value.Total.ToString(), row, 1),
                        AddCellToGrid(discGrid, kv.Value.Tagged.ToString(), row, 2),
                        AddCellToGrid(discGrid, $"{pct:F0}%", row, 3, false, FontWeights.Normal, RagBrush(rag)),
                        AddCellToGrid(discGrid, rag, row, 4, false, FontWeights.Bold, RagBrush(rag))
                    };

                    // Phase 48b: Make entire row clickable — double-click selects discipline elements
                    foreach (var tb in rowCells)
                    {
                        tb.Cursor = Cursors.Hand;
                        tb.ToolTip = $"Double-click to select all {disc} elements ({kv.Value.Total} total, {kv.Value.Untagged} untagged)";
                        tb.MouseLeftButtonDown += (s, e) =>
                        {
                            if (e.ClickCount == 2) { DispatchAction($"SelectByDisc_{disc}"); }
                        };
                        tb.MouseEnter += (s, e) => tb.Background = RowHoverBrush;
                        tb.MouseLeave += (s, e) => tb.Background = rowIdx % 2 == 0 ? Brushes.Transparent : RowAltBrush;
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
                    sugBtn.MouseLeftButtonDown += (s, e) => { DispatchAction(action); };
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

            // SLA violations alert
            if (_data.SLACriticalViolations > 0 || _data.SLAHighViolations > 0)
            {
                stack.Children.Add(MakeSectionHeader("SLA VIOLATIONS"));
                var slaCard = MakeCard();
                var slaStack = new StackPanel();
                if (_data.SLACriticalViolations > 0)
                    slaStack.Children.Add(new TextBlock
                    {
                        Text = $"\u2718 {_data.SLACriticalViolations} CRITICAL issue(s) exceeded 4-hour SLA",
                        FontSize = 12, Foreground = Br(CRed), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 2, 0, 2)
                    });
                if (_data.SLAHighViolations > 0)
                    slaStack.Children.Add(new TextBlock
                    {
                        Text = $"\u26A0 {_data.SLAHighViolations} HIGH issue(s) exceeded 24-hour SLA",
                        FontSize = 12, Foreground = Br(CAmber), Margin = new Thickness(0, 2, 0, 2)
                    });
                if (_data.AvgCriticalAgeHours > 0)
                    slaStack.Children.Add(new TextBlock
                    {
                        Text = $"Average critical issue age: {_data.AvgCriticalAgeHours:F0} hours",
                        FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 2, 0, 2)
                    });
                slaCard.Child = slaStack;
                stack.Children.Add(slaCard);
            }

            // Compliance forecast
            if (_data.ForecastedCompliancePct > 0 && !string.IsNullOrEmpty(_data.ForecastLabel))
            {
                stack.Children.Add(MakeSectionHeader("COMPLIANCE FORECAST"));
                var forecastCard = MakeCard();
                var forecastStack = new StackPanel();
                forecastStack.Children.Add(new TextBlock
                {
                    Text = $"Projected compliance: {_data.ForecastedCompliancePct:F0}%",
                    FontSize = 14, FontWeight = FontWeights.Bold,
                    Foreground = RagBrush(_data.ForecastedCompliancePct >= 80 ? "GREEN" : _data.ForecastedCompliancePct >= 50 ? "AMBER" : "RED")
                });
                forecastStack.Children.Add(new TextBlock
                {
                    Text = _data.ForecastLabel, FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
                });
                forecastCard.Child = forecastStack;
                stack.Children.Add(forecastCard);
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

            // ── KPI Strip: 5 columns ──
            var scoreColor = _data.ModelHealthScore >= 80 ? CGreen : _data.ModelHealthScore >= 50 ? CAmber : CRed;
            var kpiRow = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, 0, 12) };
            kpiRow.Children.Add(MakeKPICard("HEALTH SCORE", $"{_data.ModelHealthScore}/100", Br(scoreColor),
                $"Rating: {_data.ModelHealthRating}\nGREEN ≥80 | AMBER ≥50 | RED <50\nWeighted: Warnings 25% + Tags 25% + Data Quality 25% + Performance 25%",
                "RefreshHealth"));
            kpiRow.Children.Add(MakeKPICard("TAG COVERAGE", $"{_data.TagPct:F0}%",
                _data.TagPct >= 80 ? Br(CGreen) : _data.TagPct >= 50 ? Br(CAmber) : Br(CRed),
                $"Tagged: {_data.TaggedComplete} / {_data.TotalElements}\nStrict (fully resolved): {_data.StrictPct:F0}%\nPlaceholders: {_data.PlaceholderCount}",
                "ValidateTags"));
            kpiRow.Children.Add(MakeKPICard("CONTAINERS", $"{_data.ContainerCompletePct:F0}%",
                _data.ContainerCompletePct >= 80 ? Br(CGreen) : _data.ContainerCompletePct >= 50 ? Br(CAmber) : Br(CRed),
                $"Discipline containers populated\n53 container parameters per element\nGREEN ≥80% | AMBER ≥50% | RED <50%",
                "CombineParameters"));
            kpiRow.Children.Add(MakeKPICard("WARNINGS", _data.WarningTotal.ToString(),
                _data.WarningTotal > 20 ? Br(CRed) : _data.WarningTotal > 5 ? Br(CAmber) : Br(CGreen),
                $"Critical: {_data.WarningCritical} | High: {_data.WarningHigh}\nAuto-fixable: {_data.WarningAutoFixable}\nHealth score: {_data.WarningHealthScore}/100",
                "AutoFixWarnings"));
            kpiRow.Children.Add(MakeKPICard("STALE", _data.StaleCount.ToString(),
                _data.StaleCount == 0 ? Br(CGreen) : Br(CRed),
                "Elements with tags that no longer match context\n(moved level, changed system, changed location)\nDouble-click to retag stale elements",
                "RetagStale"));
            stack.Children.Add(kpiRow);

            // ── RAG Summary Bars ──
            stack.Children.Add(MakeRAGBar("Overall Health", _data.ModelHealthScore));
            stack.Children.Add(MakeRAGBar("Tag Coverage", _data.TagPct));
            stack.Children.Add(MakeRAGBar("Container Completion", _data.ContainerCompletePct));
            stack.Children.Add(MakeRAGBar("Strict Compliance", _data.StrictPct));

            // ── Compliance Trend Sparkline ──
            if (_data.ComplianceTrend.Count >= 2)
            {
                stack.Children.Add(new Border { Height = 12 });
                stack.Children.Add(MakeSectionHeader("COMPLIANCE TREND (LAST 10 SESSIONS)"));
                var trendCard = MakeCard();
                var trendStack = new StackPanel();

                // Direction indicator
                double firstPct = _data.ComplianceTrend.First().Pct;
                double lastPct = _data.ComplianceTrend.Last().Pct;
                double delta = lastPct - firstPct;
                string arrow = delta > 1 ? "\u2191" : delta < -1 ? "\u2193" : "\u2192";
                var trendColor = delta > 1 ? CGreen : delta < -1 ? CRed : CAmber;
                var dirRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                dirRow.Children.Add(new TextBlock { Text = arrow, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Br(trendColor), VerticalAlignment = VerticalAlignment.Center });
                dirRow.Children.Add(new TextBlock { Text = $"  {delta:+0.0;-0.0;0.0}% over {_data.ComplianceTrend.Count} sessions", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                dirRow.Children.Add(new TextBlock { Text = $"   Current: {lastPct:F1}%", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = RagBrush(lastPct >= _data.RAGGreenThreshold ? "GREEN" : lastPct >= _data.RAGAmberThreshold ? "AMBER" : "RED"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
                trendStack.Children.Add(dirRow);

                // Bar chart of compliance trend
                var barRow = new Grid { Height = 60, Margin = new Thickness(0, 4, 0, 0) };
                double minPct = _data.ComplianceTrend.Min(t => t.Pct);
                double maxPct = Math.Max(_data.ComplianceTrend.Max(t => t.Pct), minPct + 1);
                for (int ti = 0; ti < _data.ComplianceTrend.Count; ti++)
                {
                    barRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                }
                for (int ti = 0; ti < _data.ComplianceTrend.Count; ti++)
                {
                    var dp = _data.ComplianceTrend[ti];
                    double normalised = (dp.Pct - minPct) / (maxPct - minPct);
                    var barPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(1, 0, 1, 0) };
                    barPanel.Children.Add(new TextBlock { Text = $"{dp.Pct:F0}%", FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.Gray });
                    var bar = new Border
                    {
                        Height = Math.Max(4, normalised * 44),
                        Width = 20, CornerRadius = new CornerRadius(3, 3, 0, 0),
                        Background = RagBrush(dp.Pct >= _data.RAGGreenThreshold ? "GREEN" : dp.Pct >= _data.RAGAmberThreshold ? "AMBER" : "RED"),
                        ToolTip = $"{dp.Date:dd MMM yyyy}: {dp.Pct:F1}% compliance"
                    };
                    barPanel.Children.Add(bar);
                    barPanel.Children.Add(new TextBlock { Text = dp.Date.ToString("dd/MM"), FontSize = 7, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.Gray });
                    Grid.SetColumn(barPanel, ti);
                    barRow.Children.Add(barPanel);
                }
                trendStack.Children.Add(barRow);
                trendCard.Child = trendStack;
                stack.Children.Add(trendCard);
            }

            stack.Children.Add(new Border { Height = 12 });

            // ── Discipline Health Breakdown ──
            if (_data.ByDisc.Count > 0)
            {
                stack.Children.Add(MakeSectionHeader("HEALTH BY DISCIPLINE"));
                var discCard = MakeCard();
                var discGrid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                discGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });   // DISC code
                discGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });   // count
                discGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });   // pct
                discGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // bar
                discGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });       // fix button

                // Header
                discGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddCellToGrid(discGrid, "Disc", 0, 0, true);
                AddCellToGrid(discGrid, "Tagged/Total", 0, 1, true);
                AddCellToGrid(discGrid, "%", 0, 2, true);
                AddCellToGrid(discGrid, "Coverage", 0, 3, true);

                int discRow = 1;
                foreach (var kv in _data.ByDisc.OrderByDescending(d => d.Value.Total))
                {
                    discGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    double pct = kv.Value.CompliancePct;
                    string rag = pct >= _data.RAGGreenThreshold ? "GREEN" : pct >= _data.RAGAmberThreshold ? "AMBER" : "RED";

                    // Discipline code chip
                    var chipBorder = new Border
                    {
                        Background = Br(Color.FromRgb(0xE3, 0xF2, 0xFD)), CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 2, 4, 2),
                        HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand,
                        ToolTip = $"Discipline: {kv.Key}\nTotal: {kv.Value.Total} | Tagged: {kv.Value.Tagged} | Untagged: {kv.Value.Untagged}\nMissing LOC: {kv.Value.MissingLoc} | Missing SYS: {kv.Value.MissingSys} | Missing PROD: {kv.Value.MissingProd}\n\nDouble-click to select all {kv.Key} elements"
                    };
                    chipBorder.Child = new TextBlock { Text = kv.Key, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Br(CHeaderBg) };
                    string discCode = kv.Key;
                    chipBorder.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { DispatchAction($"SelectByDisc_{discCode}"); } };
                    Grid.SetRow(chipBorder, discRow); Grid.SetColumn(chipBorder, 0);
                    discGrid.Children.Add(chipBorder);

                    AddCellToGrid(discGrid, $"{kv.Value.Tagged}/{kv.Value.Total}", discRow, 1);
                    AddCellToGrid(discGrid, $"{pct:F0}%", discRow, 2, false, FontWeights.Bold, RagBrush(rag));

                    // Coverage bar
                    var miniBarBg = new Border { Background = Br(Color.FromRgb(0xE0, 0xE0, 0xE0)), Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 4, 4, 4), VerticalAlignment = VerticalAlignment.Center };
                    var miniBarFill = new Border { Background = RagBrush(rag), Height = 10, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left };
                    var miniBarGrid = new Grid();
                    miniBarGrid.Children.Add(miniBarBg);
                    miniBarGrid.Children.Add(miniBarFill);
                    Grid.SetRow(miniBarGrid, discRow); Grid.SetColumn(miniBarGrid, 3);
                    miniBarBg.Loaded += (s, e) => { miniBarFill.Width = Math.Max(4, miniBarBg.ActualWidth * pct / 100.0); };
                    discGrid.Children.Add(miniBarGrid);

                    // Fix button for failing disciplines
                    if (pct < _data.RAGGreenThreshold)
                    {
                        var fixBtn = MakeActionButton("Tag", "TagNewOnly", Br(CAccent), $"Tag untagged {kv.Key} elements");
                        fixBtn.Height = 22; fixBtn.FontSize = 9;
                        Grid.SetRow(fixBtn, discRow); Grid.SetColumn(fixBtn, 4);
                        discGrid.Children.Add(fixBtn);
                    }
                    discRow++;
                }
                discCard.Child = discGrid;
                stack.Children.Add(discCard);
            }

            // ── Token Gap Analysis ──
            if (_data.EmptyTokenCounts.Count > 0 && _data.TotalElements > 0)
            {
                stack.Children.Add(new Border { Height = 8 });
                stack.Children.Add(MakeSectionHeader("TOKEN GAP ANALYSIS"));
                var tokenCard = MakeCard();
                var tokenStack = new StackPanel();
                string[] tokens = { "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ", "STATUS", "REV" };
                foreach (string tk in tokens)
                {
                    int empty = _data.EmptyTokenCounts.TryGetValue(tk, out int ec) ? ec : 0;
                    double pct = (_data.TotalElements - empty) * 100.0 / _data.TotalElements;
                    string rag = pct >= _data.RAGGreenThreshold ? "GREEN" : pct >= _data.RAGAmberThreshold ? "AMBER" : "RED";

                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2), Height = 20 };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    row.Children.Add(new TextBlock { Text = tk, FontSize = 11, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
                    var emptyTb = new TextBlock { Text = empty > 0 ? $"{empty} gaps" : "Complete", FontSize = 10, Foreground = empty > 0 ? Br(CRed) : Br(CGreen), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(emptyTb, 1); row.Children.Add(emptyTb);
                    var pctTb = new TextBlock { Text = $"{pct:F0}%", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = RagBrush(rag), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(pctTb, 2); row.Children.Add(pctTb);

                    var miniBarBg = new Border { Background = Br(Color.FromRgb(0xE0, 0xE0, 0xE0)), Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
                    var miniBarFill = new Border { Background = RagBrush(rag), Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left };
                    var miniBarGrid = new Grid();
                    miniBarGrid.Children.Add(miniBarBg);
                    miniBarGrid.Children.Add(miniBarFill);
                    Grid.SetColumn(miniBarGrid, 3);
                    miniBarBg.Loaded += (s, e) => { miniBarFill.Width = Math.Max(4, miniBarBg.ActualWidth * pct / 100.0); };
                    row.Children.Add(miniBarGrid);
                    row.ToolTip = $"{tk}: {_data.TotalElements - empty} populated, {empty} empty/placeholder ({pct:F1}% coverage)";
                    tokenStack.Children.Add(row);
                }
                tokenCard.Child = tokenStack;
                stack.Children.Add(tokenCard);
            }

            // ── Health Check Items with severity indicators ──
            stack.Children.Add(new Border { Height = 8 });
            stack.Children.Add(MakeSectionHeader("HEALTH CHECKS"));
            foreach (var (check, score, max, detail) in _data.HealthChecks)
            {
                bool passing = score >= max * 0.8;
                var scoreBrush = passing ? Br(CGreen) : score >= max * 0.5 ? Br(CAmber) : Br(CRed);
                string statusIcon = passing ? "\u2714" : score >= max * 0.5 ? "\u26A0" : "\u2718";

                var checkCard = new Border
                {
                    Background = Br(CCardBg), BorderBrush = scoreBrush, BorderThickness = new Thickness(2, 0, 0, 0),
                    Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 2, 0, 2),
                    Cursor = Cursors.Hand,
                    ToolTip = $"{check}: Score {score}/{max}\n{detail}\n\n" +
                        (passing ? "Status: PASSING — no action needed" : GetHealthCheckFixTip(check))
                };
                // Right-click context menu
                var checkCm = new ContextMenu();
                string checkAction = GetHealthCheckAction(check);
                if (checkAction != null)
                {
                    var fixMi = new MenuItem { Header = $"Fix: {check}" };
                    string fa = checkAction;
                    fixMi.Click += (s, e) => { DispatchAction(fa); };
                    checkCm.Items.Add(fixMi);
                }
                var detailsMi = new MenuItem { Header = "Show Details" };
                string detailText = $"{check}: {score}/{max}\n{detail}";
                detailsMi.Click += (s, e) => { System.Windows.MessageBox.Show(detailText, check); };
                checkCm.Items.Add(detailsMi);
                checkCard.ContextMenu = checkCm;

                var checkGrid = new Grid();
                checkGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                checkGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                checkGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                checkGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                checkGrid.Children.Add(new TextBlock
                {
                    Text = statusIcon, FontSize = 14, Foreground = scoreBrush,
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
                });
                var scoreText = new TextBlock
                {
                    Text = $"[{score}/{max}]", FontWeight = FontWeights.Bold, Foreground = scoreBrush,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(scoreText, 1);
                checkGrid.Children.Add(scoreText);

                var detailStack = new StackPanel();
                detailStack.Children.Add(new TextBlock { Text = check, FontWeight = FontWeights.SemiBold, FontSize = 12 });
                detailStack.Children.Add(new TextBlock { Text = detail, FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap });
                Grid.SetColumn(detailStack, 2);
                checkGrid.Children.Add(detailStack);

                if (!passing)
                {
                    if (checkAction != null)
                    {
                        var fixBtn = MakeActionButton("Fix", checkAction, Br(CAccent), GetHealthCheckFixTip(check));
                        fixBtn.VerticalAlignment = VerticalAlignment.Center;
                        fixBtn.Height = 24; fixBtn.FontSize = 10;
                        Grid.SetColumn(fixBtn, 3);
                        checkGrid.Children.Add(fixBtn);
                    }
                }

                checkCard.Child = checkGrid;
                stack.Children.Add(checkCard);
            }

            // ── Validation Errors Summary ──
            if (_data.ValidationErrors.Count > 0)
            {
                stack.Children.Add(new Border { Height = 8 });
                stack.Children.Add(MakeSectionHeader("VALIDATION ERRORS"));
                var errCard = MakeCard();
                var errStack = new StackPanel();
                int maxErrCount = _data.ValidationErrors.Values.Max();
                foreach (var kv in _data.ValidationErrors.OrderByDescending(x => x.Value).Take(8))
                {
                    double barPct = maxErrCount > 0 ? kv.Value * 100.0 / maxErrCount : 0;
                    var errRow = new Grid { Margin = new Thickness(0, 2, 0, 2), Height = 18 };
                    errRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    errRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                    errRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                    errRow.Children.Add(new TextBlock { Text = kv.Key, FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
                    var cntTb = new TextBlock { Text = kv.Value.ToString(), FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Br(CRed), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(cntTb, 1); errRow.Children.Add(cntTb);
                    var mBg = new Border { Background = Br(Color.FromRgb(0xE0, 0xE0, 0xE0)), Height = 6, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
                    var mFill = new Border { Background = Br(CRed), Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
                    var mGrid = new Grid(); mGrid.Children.Add(mBg); mGrid.Children.Add(mFill);
                    Grid.SetColumn(mGrid, 2);
                    mBg.Loaded += (s, e) => { mFill.Width = Math.Max(3, mBg.ActualWidth * barPct / 100.0); };
                    errRow.Children.Add(mGrid);
                    errStack.Children.Add(errRow);
                }
                errCard.Child = errStack;
                stack.Children.Add(errCard);
            }

            // ── Actionable Recommendations ──
            if (_data.Recommendations.Count > 0)
            {
                stack.Children.Add(new Border { Height = 8 });
                stack.Children.Add(MakeSectionHeader("RECOMMENDED ACTIONS"));
                foreach (string rec in _data.Recommendations)
                {
                    string recAction = InferRecommendationAction(rec);
                    var recRow = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    recRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    recRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    recRow.Children.Add(new TextBlock
                    {
                        Text = $"  \u2022 {rec}", FontSize = 12, TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    if (recAction != null)
                    {
                        var goBtn = MakeActionButton("Fix", recAction, Br(CGreen));
                        goBtn.Height = 24; goBtn.FontSize = 10;
                        Grid.SetColumn(goBtn, 1);
                        recRow.Children.Add(goBtn);
                    }
                    stack.Children.Add(recRow);
                }
            }

            // ── Phase-Based Health ──
            if (_data.ByPhase.Count > 0)
            {
                stack.Children.Add(new Border { Height = 8 });
                stack.Children.Add(MakeSectionHeader("HEALTH BY PHASE"));
                foreach (var phase in _data.ByPhase)
                {
                    stack.Children.Add(MakeRAGBar($"{phase.Key} ({phase.Value.Tagged}/{phase.Value.Total})", phase.Value.Pct));
                }
            }

            // ── Actions: 3 sections ──
            stack.Children.Add(new Border { Height = 12 });
            stack.Children.Add(MakeSectionHeader("ACTIONS"));

            // Health & Diagnostics
            var diagWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            diagWrap.Children.Add(MakeActionButton("Refresh Health", "RefreshHealth", Br(CHeaderBg),
                "Re-scan model health: warnings, tags, stale elements, parameters, containers"));
            diagWrap.Children.Add(MakeActionButton("Export Report", "ExportHealth", Br(CGreen),
                "Export model health report to CSV with all check results and discipline breakdown"));
            diagWrap.Children.Add(MakeActionButton("45-Point Validation", "RunFullCheck", Br(CAccent),
                "Run 45 validation checks: data files, parameters, formulas, schedules, materials"));
            diagWrap.Children.Add(MakeActionButton("Schema Validate", "SchemaValidate", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Validate material CSV columns match MATERIAL_SCHEMA.json (77-column schema)"));
            stack.Children.Add(diagWrap);

            // Fix & Repair
            var fixWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            fixWrap.Children.Add(MakeActionButton("Auto-Fix Warnings", "AutoFixWarnings", Br(CRed),
                "Auto-fix: duplicate instances, room separation overlaps, duplicate marks, wall overlaps"));
            fixWrap.Children.Add(MakeActionButton("Retag Stale", "RetagStale", Br(Color.FromRgb(0x6A, 0x1B, 0x9A)),
                "Re-derive tags for elements that have moved level, changed system, or changed location"));
            fixWrap.Children.Add(MakeActionButton("Resolve All", "ResolveAllIssues", Br(CRed),
                "One-click ISO 19650 compliance resolution — batched 500 elements at a time"));
            fixWrap.Children.Add(MakeActionButton("Fix Anomalies", "AnomalyAutoFix", Br(CAmber),
                "Auto-fix: DISC/SYS mismatch, invalid PROD, FUNC derivation, TAG7 rebuild, stale clear"));
            stack.Children.Add(fixWrap);

            // Tagging & Compliance
            var tagWrap = new WrapPanel();
            tagWrap.Children.Add(MakeActionButton("Tag New Only", "TagNewOnly", Br(CGreen),
                "Tag only untagged elements — fast incremental tagging"));
            tagWrap.Children.Add(MakeActionButton("Combine Parameters", "CombineParameters", Br(Color.FromRgb(0x15, 0x65, 0xC0)),
                "Write tags to all 53 discipline-specific container parameters"));
            tagWrap.Children.Add(MakeActionButton("Evaluate Formulas", "EvaluateFormulas", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Evaluate 199 formulas in dependency order: cost, flow, area, environmental"));
            tagWrap.Children.Add(MakeActionButton("Load Params", "LoadSharedParams", Br(Color.FromRgb(0x6A, 0x1B, 0x9A)),
                "Load/bind STING shared parameters from MR_PARAMETERS.txt"));
            stack.Children.Add(tagWrap);

            scroll.Content = stack;
            return scroll;
        }

        private static string GetHealthCheckAction(string check) => check switch
        {
            "Warnings" => "AutoFixWarnings",
            "Tag Completeness" => "TagNewOnly",
            "Stale Elements" => "RetagStale",
            "Parameters" => "LoadSharedParams",
            "Containers" => "CombineParameters",
            "Formulas" => "EvaluateFormulas",
            _ => null
        };

        private static string GetHealthCheckFixTip(string check) => check switch
        {
            "Warnings" => "Click Fix to auto-resolve duplicate instances, room separation overlaps, and duplicate marks",
            "Tag Completeness" => "Click Fix to tag untagged elements using the 'Tag New Only' command",
            "Stale Elements" => "Click Fix to re-derive tags for elements that have moved or changed",
            "Parameters" => "Click Fix to load/bind missing STING shared parameters",
            "Containers" => "Click Fix to write tags to all discipline-specific containers",
            "Formulas" => "Click Fix to evaluate 199 formulas in dependency order",
            _ => "Requires manual review"
        };

        private static string InferRecommendationAction(string rec)
        {
            if (rec.Contains("warning")) return "AutoFixWarnings";
            if (rec.Contains("Batch Tag") || rec.Contains("Tag & Combine")) return "TagNewOnly";
            if (rec.Contains("stale")) return "RetagStale";
            if (rec.Contains("parameter")) return "LoadSharedParams";
            return null;
        }

        // ════════════════════════════════════════════════════════════════
        //  WARNINGS TAB
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildWarningsTab()
        {
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var root = new StackPanel { Margin = new Thickness(16) };
            sv.Content = root;

            // ── KPI CARDS (6 columns) ─────────────────────────────────────
            var kpiGrid = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 12) };
            kpiGrid.Children.Add(MakeKPICard("TOTAL", _data.WarningTotal.ToString(),
                _data.WarningTotal > 20 ? Br(CRed) : _data.WarningTotal > 5 ? Br(CAmber) : Br(CGreen),
                $"Total Revit warnings in model\nBaseline trend: {_data.WarningTrend}", "WarningsDashboard"));
            kpiGrid.Children.Add(MakeKPICard("CRITICAL", _data.WarningCritical.ToString(),
                _data.WarningCritical > 0 ? Br(CRed) : Br(CGreen),
                "Critical warnings block handover\nSLA: resolve within 4 hours", "AutoFixWarnings"));
            kpiGrid.Children.Add(MakeKPICard("HIGH", _data.WarningHigh.ToString(),
                _data.WarningHigh > 0 ? Br(CAmber) : Br(CGreen),
                "High-severity warnings affect deliverable quality\nSLA: resolve within 24 hours"));
            kpiGrid.Children.Add(MakeKPICard("AUTO-FIXABLE", _data.WarningAutoFixable.ToString(), Br(CGreen),
                $"One-click auto-fix available\n{_data.WarningManualReview} require manual review", "AutoFixWarnings"));
            kpiGrid.Children.Add(MakeKPICard("HEALTH", $"{_data.WarningHealthScore}/100",
                _data.WarningHealthScore >= 80 ? Br(CGreen) : _data.WarningHealthScore >= 50 ? Br(CAmber) : Br(CRed),
                "Weighted: Critical=-20, High=-5, Medium=-2, Low=-1 per warning from base 100"));
            kpiGrid.Children.Add(MakeKPICard("SLA VIOLATIONS", _data.WarningSLAViolations.ToString(),
                _data.WarningSLAViolations > 0 ? Br(CRed) : Br(CGreen),
                $"Warnings exceeding SLA threshold\nCritical=4h, High=24h, Medium=1wk, Low=2wk\nAvg critical age: {_data.WarningAvgCriticalAgeHours:F1}h"));
            root.Children.Add(kpiGrid);

            // ── WARNING GATE STATUS ───────────────────────────────────────
            var gateCard = MakeCard();
            var gateRow = new StackPanel { Orientation = Orientation.Horizontal };
            var gateDot = new Ellipse { Width = 14, Height = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                Fill = _data.WarningGatePass ? Br(CGreen) : Br(CRed) };
            gateRow.Children.Add(gateDot);
            gateRow.Children.Add(new TextBlock { Text = _data.WarningGatePass ? "WARNING GATE: PASS" : "WARNING GATE: FAIL",
                FontWeight = FontWeights.Bold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center,
                Foreground = _data.WarningGatePass ? Br(CGreen) : Br(CRed) });
            if (!string.IsNullOrEmpty(_data.WarningGateReason))
                gateRow.Children.Add(new TextBlock { Text = $"  —  {_data.WarningGateReason}", FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)) });
            gateCard.Child = gateRow;
            root.Children.Add(gateCard);

            // ── ACTION BUTTONS (3-column grid) ───────────────────────────
            var actGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            actGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actGrid.RowDefinitions.Add(new RowDefinition());
            actGrid.RowDefinitions.Add(new RowDefinition());
            actGrid.RowDefinitions.Add(new RowDefinition());
            var actBtns = new (string Label, string Tag, Color Clr, string Tip, int Row, int Col)[]
            {
                ("Auto-Fix All", "AutoFixWarnings", CGreen, "Run all 16 auto-fix strategies in batch with dry-run preview", 0, 0),
                ("Export CSV", "ExportWarnings", CHeaderBg, "Export all warnings with classification, severity, elements to CSV for Power BI / BIM360", 0, 1),
                ("Save Baseline", "SaveBaseline", CAccent, "Snapshot current warning count as baseline for trend tracking", 0, 2),
                ("Select Elements", "SelectWarningElements", Color.FromRgb(0x6A, 0x1B, 0x9A), "Pick warning type → select affected elements in model view", 1, 0),
                ("Suppress Selected", "SuppressWarnings", Color.FromRgb(0x45, 0x50, 0x6E), "Add patterns to suppression list (persisted, time-limited, auditable)", 1, 1),
                ("Create Issues", "CreateIssuesFromWarnings", CRed, "Auto-create NCR/SI issues from critical/high warnings with element linking", 1, 2),
                ("Compliance Check", "WarningsCompliance", CHeaderBg, "ISO 19650 / CIBSE / BS 7671 compliance report mapping warnings to standards", 2, 0),
                ("Root Cause Analysis", "WarningRootCause", Color.FromRgb(0x00, 0x69, 0x7C), "Weighted root-cause graph with impact scoring and multi-warning element detection", 2, 1),
                ("Warning Prediction", "WarningPrediction", Color.FromRgb(0x45, 0x50, 0x6E), "7-day trend prediction using linear regression on historical warning counts", 2, 2),
            };
            foreach (var (label, tag, clr, tip, row, col) in actBtns)
            {
                var btn = MakeActionButton(label, tag, Br(clr), tip);
                Grid.SetRow(btn, row); Grid.SetColumn(btn, col);
                actGrid.Children.Add(btn);
            }
            root.Children.Add(actGrid);

            // ── DELIVERABLE IMPACT ANALYSIS ──────────────────────────────
            if (_data.WarningImpactCOBie > 0 || _data.WarningImpactIFC > 0 || _data.WarningImpactHandover > 0 ||
                _data.WarningImpactSchedules > 0 || _data.WarningImpactClash > 0)
            {
                root.Children.Add(MakeSectionHeader("DELIVERABLE IMPACT ANALYSIS"));
                var impactCard = MakeCard();
                var impactStack = new StackPanel();
                if (!string.IsNullOrEmpty(_data.WarningHighestImpactArea))
                {
                    impactStack.Children.Add(new TextBlock
                    {
                        Text = $"Highest Impact: {_data.WarningHighestImpactArea}",
                        FontWeight = FontWeights.Bold, FontSize = 13, Foreground = Br(CRed), Margin = new Thickness(0, 0, 0, 8)
                    });
                }
                var impacts = new (string Name, int Count, Color Clr)[]
                {
                    ("COBie Export", _data.WarningImpactCOBie, Color.FromRgb(0x15, 0x65, 0xC0)),
                    ("IFC Export", _data.WarningImpactIFC, Color.FromRgb(0x00, 0x69, 0x7C)),
                    ("FM Handover", _data.WarningImpactHandover, CRed),
                    ("Schedules", _data.WarningImpactSchedules, CAmber),
                    ("Clash Detection", _data.WarningImpactClash, Color.FromRgb(0x6A, 0x1B, 0x9A)),
                };
                int maxImpact = impacts.Max(i => i.Count);
                foreach (var (name, count, clr) in impacts)
                {
                    if (count <= 0) continue;
                    var impRow = new Grid { Margin = new Thickness(0, 3, 0, 3), Height = 24 };
                    impRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                    impRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                    impRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    impRow.Children.Add(new TextBlock { Text = name, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                    var cntTb = new TextBlock { Text = $"{count}", FontWeight = FontWeights.Bold, FontSize = 11, Foreground = Br(clr), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(cntTb, 1); impRow.Children.Add(cntTb);
                    double barPct = maxImpact > 0 ? count * 100.0 / maxImpact : 0;
                    var barBg = new Border { Background = Br(Color.FromRgb(0xE8, 0xEA, 0xED)), Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
                    var barFill = new Border { Background = Br(clr), Height = 10, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.85 };
                    var barGrid = new Grid(); barGrid.Children.Add(barBg); barGrid.Children.Add(barFill);
                    barBg.Loaded += (s, e) => { barFill.Width = Math.Max(4, barBg.ActualWidth * barPct / 100.0); };
                    Grid.SetColumn(barGrid, 2); impRow.Children.Add(barGrid);
                    impactStack.Children.Add(impRow);
                }
                impactCard.Child = impactStack;
                root.Children.Add(impactCard);
            }

            // ── ROOT CAUSE GROUPS ────────────────────────────────────────
            if (_data.WarningRootCauseGroups.Count > 0)
            {
                root.Children.Add(MakeSectionHeader("ROOT CAUSE ANALYSIS (by impact)"));
                var rcCard = MakeCard();
                var rcStack = new StackPanel();
                int maxRcCount = _data.WarningRootCauseGroups.Max(g => g.Count);
                foreach (var (desc, cat, sev, count, canAutoFix, fix) in _data.WarningRootCauseGroups.OrderByDescending(g => g.Count).Take(15))
                {
                    var rcRow = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                    rcRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    rcRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                    rcRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                    rcRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

                    var sevColor = sev == WarningSeverity.Critical ? CRed : sev == WarningSeverity.High ? CAmber :
                        sev == WarningSeverity.Medium ? Color.FromRgb(0xF5, 0xC5, 0x42) : Color.FromRgb(0x90, 0xA4, 0xAE);

                    // Description with severity dot and auto-fix badge
                    var descPanel = new StackPanel { Orientation = Orientation.Horizontal };
                    descPanel.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Br(sevColor), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                    var descTb = new TextBlock { Text = desc, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center, ToolTip = desc };
                    descPanel.Children.Add(descTb);
                    if (canAutoFix)
                    {
                        descPanel.Children.Add(new Border
                        {
                            Background = Br(Color.FromRgb(0xE8, 0xF5, 0xE9)), CornerRadius = new CornerRadius(3),
                            Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(4, 1, 4, 1),
                            Child = new TextBlock { Text = "AUTO-FIX", FontSize = 8, FontWeight = FontWeights.Bold, Foreground = Br(CGreen) }
                        });
                    }
                    rcRow.Children.Add(descPanel);

                    var catTb = new TextBlock { Text = cat.ToString(), FontSize = 10, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(catTb, 1); rcRow.Children.Add(catTb);

                    var cntTb = new TextBlock { Text = $"×{count}", FontWeight = FontWeights.Bold, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(cntTb, 2); rcRow.Children.Add(cntTb);

                    // Mini bar for relative count
                    double rcPct = maxRcCount > 0 ? count * 100.0 / maxRcCount : 0;
                    var rcBar = new Border { Background = Br(sevColor), Height = 6, CornerRadius = new CornerRadius(3),
                        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
                    var rcBarBg = new Border { Background = Br(Color.FromRgb(0xE8, 0xEA, 0xED)), Height = 6, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
                    var rcBarGrid = new Grid(); rcBarGrid.Children.Add(rcBarBg); rcBarGrid.Children.Add(rcBar);
                    rcBarBg.Loaded += (s, e) => { rcBar.Width = Math.Max(4, rcBarBg.ActualWidth * rcPct / 100.0); };
                    Grid.SetColumn(rcBarGrid, 3); rcRow.Children.Add(rcBarGrid);

                    // Interactive: double-click to select, right-click for context menu
                    rcRow.Cursor = Cursors.Hand;
                    rcRow.ToolTip = $"Category: {cat}\nSeverity: {sev}\nCount: {count}\nFix strategy: {(string.IsNullOrEmpty(fix) ? "Manual review" : fix)}\nDouble-click to select affected elements";
                    rcRow.MouseLeftButtonDown += (s, e) =>
                    {
                        if (e.ClickCount == 2) { DispatchAction($"SelectWarning_{cat}|{desc}"); }
                    };
                    var rcCtx = new ContextMenu();
                    var rcZoom = new MenuItem { Header = "Zoom to 3D Section Box" };
                    rcZoom.Click += (s2, e2) => { DispatchAction($"ZoomToWarning_{cat}|{desc}"); };
                    var rcSelect = new MenuItem { Header = "Select Elements in Model" };
                    rcSelect.Click += (s2, e2) => { DispatchAction($"SelectWarning_{cat}|{desc}"); };
                    rcCtx.Items.Add(rcZoom); rcCtx.Items.Add(rcSelect);
                    rcRow.ContextMenu = rcCtx;

                    rcStack.Children.Add(rcRow);
                }
                rcCard.Child = rcStack;
                root.Children.Add(rcCard);
            }

            // ── WARNING TREE (Category > Description) ────────────────────
            root.Children.Add(MakeSectionHeader("WARNING TREE (double-click to select / zoom)"));
            var tree = new TreeView
            {
                BorderBrush = Br(CBorder), BorderThickness = new Thickness(1),
                MaxHeight = 280, Margin = new Thickness(0, 0, 0, 12),
                Background = Br(CCardBg)
            };
            var catGroups = _data.WarningByCategory.OrderByDescending(x => x.Value);
            foreach (var catKv in catGroups)
            {
                var catColor = catKv.Key == WarningCategory.Structural || catKv.Key == WarningCategory.Spatial ? CRed :
                    catKv.Key == WarningCategory.MEP ? CAmber :
                    catKv.Key == WarningCategory.Acoustic ? Color.FromRgb(0x6A, 0x1B, 0x9A) :
                    catKv.Key == WarningCategory.Sustainability ? CGreen :
                    catKv.Key == WarningCategory.Coordination ? Color.FromRgb(0x00, 0x69, 0x7C) :
                    Color.FromRgb(0x37, 0x47, 0x4F);
                var catNode = new TreeViewItem
                {
                    Header = new StackPanel { Orientation = Orientation.Horizontal, Children =
                    {
                        new Ellipse { Width = 10, Height = 10, Fill = Br(catColor), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = $"{catKv.Key} ({catKv.Value})", FontWeight = FontWeights.Bold, FontSize = 12 }
                    }},
                    IsExpanded = catKv.Key == WarningCategory.Spatial || catKv.Key == WarningCategory.MEP || catKv.Key == WarningCategory.Structural
                };
                if (_data.WarningTopByCategory.TryGetValue(catKv.Key, out var topDescs))
                {
                    foreach (var (desc, count) in topDescs)
                    {
                        var descNode = new TreeViewItem
                        {
                            Header = new TextBlock { Text = $"({count}) {desc}", FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 600 },
                            Tag = $"SelectWarning_{catKv.Key}|{desc}", Cursor = Cursors.Hand,
                            ToolTip = "Double-click to zoom to 3D section box\nRight-click for more options"
                        };
                        descNode.MouseDoubleClick += (s, e) =>
                        {
                            if (descNode.Tag is string tag) { DispatchAction("ZoomToWarning_" + tag.Substring("SelectWarning_".Length)); }
                            e.Handled = true;
                        };
                        var ctx = new ContextMenu();
                        var zoomItem = new MenuItem { Header = "Zoom to 3D Section Box" };
                        zoomItem.Click += (s2, e2) => { if (descNode.Tag is string t) { DispatchAction("ZoomToWarning_" + t.Substring("SelectWarning_".Length)); } };
                        var selectItem = new MenuItem { Header = "Select Elements in Model" };
                        selectItem.Click += (s2, e2) => { if (descNode.Tag is string t) { DispatchAction(t); } };
                        ctx.Items.Add(zoomItem); ctx.Items.Add(selectItem);
                        descNode.ContextMenu = ctx;
                        catNode.Items.Add(descNode);
                    }
                    int remainder = catKv.Value - topDescs.Sum(t => t.Count);
                    if (remainder > 0)
                        catNode.Items.Add(new TreeViewItem { Header = new TextBlock { Text = $"...and {remainder} more", FontSize = 10, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)), FontStyle = FontStyles.Italic } });
                }
                tree.Items.Add(catNode);
            }
            root.Children.Add(tree);

            // ── TWO-COLUMN LAYOUT: By Category + By Severity ─────────────
            var twoCol = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            twoCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            twoCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); // spacer
            twoCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // LEFT: By Category with responsive bars
            if (_data.WarningByCategory.Count > 0)
            {
                var catPanel = new StackPanel();
                catPanel.Children.Add(MakeSectionHeader("BY CATEGORY"));
                var catCard = MakeCard();
                var catStack = new StackPanel();
                int maxCatCount = _data.WarningByCategory.Values.Max();
                foreach (var kv in _data.WarningByCategory.OrderByDescending(x => x.Value))
                {
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2), Height = 22 };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.Children.Add(new TextBlock { Text = kv.Key.ToString(), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
                    var countTb = new TextBlock { Text = kv.Value.ToString(), FontWeight = FontWeights.Bold, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(countTb, 1); row.Children.Add(countTb);
                    double barPct = maxCatCount > 0 ? kv.Value * 100.0 / maxCatCount : 0;
                    var barBg = new Border { Background = Br(Color.FromRgb(0xE8, 0xEA, 0xED)), Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
                    var barFill = new Border { Background = Br(CAccent), Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.85 };
                    var barGrid = new Grid(); barGrid.Children.Add(barBg); barGrid.Children.Add(barFill);
                    barBg.Loaded += (s, e) => { barFill.Width = Math.Max(4, barBg.ActualWidth * barPct / 100.0); };
                    Grid.SetColumn(barGrid, 2); row.Children.Add(barGrid);
                    catStack.Children.Add(row);
                }
                catCard.Child = catStack;
                catPanel.Children.Add(catCard);
                twoCol.Children.Add(catPanel);
            }

            // RIGHT: By Severity with priority ranking (Ideate-style 4-tier)
            if (_data.WarningBySeverity.Count > 0)
            {
                var sevPanel = new StackPanel();
                sevPanel.Children.Add(MakeSectionHeader("BY SEVERITY (Priority Rank)"));
                var sevCard = MakeCard();
                var sevStack = new StackPanel();
                int maxSevCount = _data.WarningBySeverity.Values.Max();
                int rank = 1;
                foreach (WarningSeverity sev in new[] { WarningSeverity.Critical, WarningSeverity.High, WarningSeverity.Medium, WarningSeverity.Low, WarningSeverity.Info })
                {
                    if (!_data.WarningBySeverity.TryGetValue(sev, out int cnt) || cnt <= 0) continue;
                    var sevColor = sev == WarningSeverity.Critical ? CRed : sev == WarningSeverity.High ? CAmber :
                        sev == WarningSeverity.Medium ? Color.FromRgb(0xF5, 0xC5, 0x42) : Color.FromRgb(0x90, 0xA4, 0xAE);
                    var slaText = sev == WarningSeverity.Critical ? "SLA: 4h" : sev == WarningSeverity.High ? "SLA: 24h" :
                        sev == WarningSeverity.Medium ? "SLA: 1wk" : sev == WarningSeverity.Low ? "SLA: 2wk" : "";

                    var sevRow = new Grid { Margin = new Thickness(0, 3, 0, 3), Height = 26 };
                    sevRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });
                    sevRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                    sevRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                    sevRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    sevRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });

                    // Rank badge
                    var rankBorder = new Border { Background = Br(sevColor), CornerRadius = new CornerRadius(3), Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock { Text = $"P{rank}", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                    sevRow.Children.Add(rankBorder);

                    var sevLabel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    sevLabel.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = Br(sevColor), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                    sevLabel.Children.Add(new TextBlock { Text = sev.ToString(), FontSize = 11, FontWeight = FontWeights.SemiBold });
                    Grid.SetColumn(sevLabel, 1); sevRow.Children.Add(sevLabel);

                    var cntTb = new TextBlock { Text = cnt.ToString(), FontWeight = FontWeights.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(cntTb, 2); sevRow.Children.Add(cntTb);

                    double barPct = maxSevCount > 0 ? cnt * 100.0 / maxSevCount : 0;
                    var barBg = new Border { Background = Br(Color.FromRgb(0xE8, 0xEA, 0xED)), Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
                    var barFill = new Border { Background = Br(sevColor), Height = 10, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.85 };
                    var barGrid = new Grid(); barGrid.Children.Add(barBg); barGrid.Children.Add(barFill);
                    barBg.Loaded += (s, e) => { barFill.Width = Math.Max(4, barBg.ActualWidth * barPct / 100.0); };
                    Grid.SetColumn(barGrid, 3); sevRow.Children.Add(barGrid);

                    var slaTb = new TextBlock { Text = slaText, FontSize = 9, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(slaTb, 4); sevRow.Children.Add(slaTb);

                    sevStack.Children.Add(sevRow);
                    rank++;
                }
                sevCard.Child = sevStack;
                sevPanel.Children.Add(sevCard);
                Grid.SetColumn(sevPanel, 2);
                twoCol.Children.Add(sevPanel);
            }
            root.Children.Add(twoCol);

            // ── TWO-COLUMN LAYOUT: By Level + By Workset ─────────────────
            var twoCol2 = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            twoCol2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            twoCol2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            twoCol2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // LEFT: By Level (top 10) with bars
            if (_data.WarningByLevel.Count > 0)
            {
                var lvlPanel = new StackPanel();
                lvlPanel.Children.Add(MakeSectionHeader("BY LEVEL (top 10)"));
                var lvlCard = MakeCard();
                var lvlStack = new StackPanel();
                int maxLvl = _data.WarningByLevel.Values.Max();
                foreach (var kv in _data.WarningByLevel.OrderByDescending(x => x.Value).Take(10))
                {
                    var lvlRow = new Grid { Margin = new Thickness(0, 2, 0, 2), Height = 20 };
                    lvlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                    lvlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                    lvlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    lvlRow.Children.Add(new TextBlock { Text = kv.Key, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                    var cntTb = new TextBlock { Text = kv.Value.ToString(), FontWeight = FontWeights.Bold, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(cntTb, 1); lvlRow.Children.Add(cntTb);
                    double barPct = maxLvl > 0 ? kv.Value * 100.0 / maxLvl : 0;
                    var barBg = new Border { Background = Br(Color.FromRgb(0xE8, 0xEA, 0xED)), Height = 7, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
                    var barFill = new Border { Background = Br(CAccent), Height = 7, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.8 };
                    var barGrid = new Grid(); barGrid.Children.Add(barBg); barGrid.Children.Add(barFill);
                    barBg.Loaded += (s, e) => { barFill.Width = Math.Max(4, barBg.ActualWidth * barPct / 100.0); };
                    Grid.SetColumn(barGrid, 2); lvlRow.Children.Add(barGrid);
                    lvlStack.Children.Add(lvlRow);
                }
                lvlCard.Child = lvlStack;
                lvlPanel.Children.Add(lvlCard);
                twoCol2.Children.Add(lvlPanel);
            }

            // RIGHT: By Workset
            if (_data.WarningByWorkset.Count > 0)
            {
                var wsPanel = new StackPanel();
                wsPanel.Children.Add(MakeSectionHeader("BY WORKSET"));
                var wsCard = MakeCard();
                var wsStack = new StackPanel();
                int maxWs = _data.WarningByWorkset.Values.Max();
                foreach (var kv in _data.WarningByWorkset.OrderByDescending(x => x.Value).Take(10))
                {
                    var wsRow = new Grid { Margin = new Thickness(0, 2, 0, 2), Height = 20 };
                    wsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                    wsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                    wsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    wsRow.Children.Add(new TextBlock { Text = kv.Key, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                    var cntTb = new TextBlock { Text = kv.Value.ToString(), FontWeight = FontWeights.Bold, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(cntTb, 1); wsRow.Children.Add(cntTb);
                    double barPct = maxWs > 0 ? kv.Value * 100.0 / maxWs : 0;
                    var barBg = new Border { Background = Br(Color.FromRgb(0xE8, 0xEA, 0xED)), Height = 7, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
                    var barFill = new Border { Background = Br(Color.FromRgb(0x00, 0x69, 0x7C)), Height = 7, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.8 };
                    var barGrid = new Grid(); barGrid.Children.Add(barBg); barGrid.Children.Add(barFill);
                    barBg.Loaded += (s, e) => { barFill.Width = Math.Max(4, barBg.ActualWidth * barPct / 100.0); };
                    Grid.SetColumn(barGrid, 2); wsRow.Children.Add(barGrid);
                    wsStack.Children.Add(wsRow);
                }
                wsCard.Child = wsStack;
                wsPanel.Children.Add(wsCard);
                Grid.SetColumn(wsPanel, 2);
                twoCol2.Children.Add(wsPanel);
            }
            root.Children.Add(twoCol2);

            // ── HOTSPOT ELEMENTS ─────────────────────────────────────────
            if (_data.WarningHotspots.Count > 0)
            {
                root.Children.Add(MakeSectionHeader("HOTSPOT ELEMENTS (most warnings)"));
                var hotCard = MakeCard();
                var hotStack = new StackPanel();
                int maxHot = _data.WarningHotspots.Take(10).Max(h => h.Count);
                foreach (var (name, count) in _data.WarningHotspots.Take(10))
                {
                    var hotRow = new Grid { Margin = new Thickness(0, 2, 0, 2), Height = 22, Cursor = Cursors.Hand };
                    hotRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    hotRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                    hotRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                    hotRow.Children.Add(new TextBlock { Text = name, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                    var cntTb = new TextBlock { Text = $"{count} warn", FontWeight = FontWeights.Bold, FontSize = 10.5, Foreground = Br(CRed), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(cntTb, 1); hotRow.Children.Add(cntTb);
                    double barPct = maxHot > 0 ? count * 100.0 / maxHot : 0;
                    var barBg = new Border { Background = Br(Color.FromRgb(0xE8, 0xEA, 0xED)), Height = 7, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
                    var barFill = new Border { Background = Br(CRed), Height = 7, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.75 };
                    var barGrid = new Grid(); barGrid.Children.Add(barBg); barGrid.Children.Add(barFill);
                    barBg.Loaded += (s, e) => { barFill.Width = Math.Max(4, barBg.ActualWidth * barPct / 100.0); };
                    Grid.SetColumn(barGrid, 2); hotRow.Children.Add(barGrid);
                    hotRow.ToolTip = $"Element: {name}\nWarnings: {count}\nDouble-click to zoom to 3D section box";
                    hotRow.MouseLeftButtonDown += (s, e) =>
                    {
                        if (e.ClickCount == 2) { DispatchAction($"ZoomToWarning_{name}"); }
                    };
                    var hotCtx = new ContextMenu();
                    var hotZoom = new MenuItem { Header = "Zoom to 3D Section Box" };
                    hotZoom.Click += (s2, e2) => { DispatchAction($"ZoomToWarning_{name}"); };
                    var hotSelect = new MenuItem { Header = "Select Element" };
                    hotSelect.Click += (s2, e2) => { DispatchAction($"ZoomToElement_{name}"); };
                    hotCtx.Items.Add(hotZoom); hotCtx.Items.Add(hotSelect);
                    hotRow.ContextMenu = hotCtx;
                    hotStack.Children.Add(hotRow);
                }
                hotCard.Child = hotStack;
                root.Children.Add(hotCard);
            }

            // ── BY DISCIPLINE with double-click selection ─────────────────
            if (_data.WarningByDiscipline.Count > 0)
            {
                root.Children.Add(MakeSectionHeader("BY DISCIPLINE"));
                var discCard = MakeCard();
                var discStack = new StackPanel();
                int maxDisc = _data.WarningByDiscipline.Values.Max();
                foreach (var kv in _data.WarningByDiscipline.OrderByDescending(x => x.Value))
                {
                    var dRow = new Grid { Margin = new Thickness(0, 2, 0, 2), Height = 22, Cursor = Cursors.Hand };
                    dRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                    dRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                    dRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    dRow.Children.Add(new TextBlock { Text = kv.Key, FontWeight = FontWeights.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                    var cntTb = new TextBlock { Text = kv.Value.ToString(), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(cntTb, 1); dRow.Children.Add(cntTb);
                    double barPct = maxDisc > 0 ? kv.Value * 100.0 / maxDisc : 0;
                    var barBg = new Border { Background = Br(Color.FromRgb(0xE8, 0xEA, 0xED)), Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
                    var barFill = new Border { Background = Br(CAccent), Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.85 };
                    var barGrid = new Grid(); barGrid.Children.Add(barBg); barGrid.Children.Add(barFill);
                    barBg.Loaded += (s, e) => { barFill.Width = Math.Max(4, barBg.ActualWidth * barPct / 100.0); };
                    Grid.SetColumn(barGrid, 2); dRow.Children.Add(barGrid);
                    dRow.ToolTip = $"Discipline: {kv.Key} — {kv.Value} warnings\nDouble-click to select all elements of this discipline";
                    dRow.MouseLeftButtonDown += (s, e) =>
                    {
                        if (e.ClickCount == 2) { DispatchAction($"SelectByDisc_{kv.Key}"); }
                    };
                    discStack.Children.Add(dRow);
                }
                discCard.Child = discStack;
                root.Children.Add(discCard);
            }

            // ── REGRESSION ANALYSIS ──────────────────────────────────────
            if (_data.WarningAdded > 0 || _data.WarningRemoved > 0)
            {
                root.Children.Add(MakeSectionHeader("REGRESSION ANALYSIS (vs baseline)"));
                var regCard = MakeCard();
                var regStack = new StackPanel();
                var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                addRow.Children.Add(new TextBlock { Text = "▲ ", Foreground = Br(CRed), FontWeight = FontWeights.Bold, FontSize = 13 });
                addRow.Children.Add(new TextBlock { Text = $"New since baseline:  +{_data.WarningAdded}", FontSize = 12, Foreground = _data.WarningAdded > 0 ? Br(CRed) : Br(CGreen) });
                regStack.Children.Add(addRow);
                var remRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                remRow.Children.Add(new TextBlock { Text = "▼ ", Foreground = Br(CGreen), FontWeight = FontWeights.Bold, FontSize = 13 });
                remRow.Children.Add(new TextBlock { Text = $"Resolved since baseline:  -{_data.WarningRemoved}", FontSize = 12, Foreground = Br(CGreen) });
                regStack.Children.Add(remRow);
                int netChange = _data.WarningAdded - _data.WarningRemoved;
                var netRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                netRow.Children.Add(new TextBlock { Text = netChange >= 0 ? "Net: +" : "Net: ", FontWeight = FontWeights.Bold, FontSize = 12 });
                netRow.Children.Add(new TextBlock { Text = $"{netChange}", FontWeight = FontWeights.Bold, FontSize = 12, Foreground = netChange > 0 ? Br(CRed) : netChange < 0 ? Br(CGreen) : Br(Color.FromRgb(0x75, 0x75, 0x75)) });
                regStack.Children.Add(netRow);
                regCard.Child = regStack;
                root.Children.Add(regCard);
            }

            return sv;
        }

        // ════════════════════════════════════════════════════════════════
        //  ISSUES TAB
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildIssuesTab()
        {
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var root = new StackPanel { Margin = new Thickness(16) };
            sv.Content = root;

            // ── KPI CARDS (6 columns) ─────────────────────────────────────
            var kpiGrid = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 12) };
            kpiGrid.Children.Add(MakeKPICard("TOTAL", _data.IssuesTotal.ToString(), Br(CHeaderBg),
                $"Total issues tracked in project\nTypes: RFI, NCR, SI, Clash, Snagging, Design, Change"));
            kpiGrid.Children.Add(MakeKPICard("OPEN", _data.IssuesOpen.ToString(),
                _data.IssuesOpen > 10 ? Br(CRed) : _data.IssuesOpen > 0 ? Br(CAmber) : Br(CGreen),
                $"Open issues requiring resolution\nClosed: {_data.IssuesTotal - _data.IssuesOpen}"));
            kpiGrid.Children.Add(MakeKPICard("CRITICAL", _data.IssuesCritical.ToString(),
                _data.IssuesCritical > 0 ? Br(CRed) : Br(CGreen),
                "Critical priority issues — SLA: resolve within 4 hours"));
            kpiGrid.Children.Add(MakeKPICard("OVERDUE", _data.IssuesOverdue.ToString(),
                _data.IssuesOverdue > 0 ? Br(CRed) : Br(CGreen),
                "Issues exceeding SLA thresholds\nCRITICAL=4h, HIGH=24h, MEDIUM=1wk, LOW=2wk"));
            int closed = _data.IssuesTotal - _data.IssuesOpen;
            int resRate = _data.IssuesTotal > 0 ? closed * 100 / _data.IssuesTotal : 0;
            kpiGrid.Children.Add(MakeKPICard("RESOLUTION", $"{resRate}%",
                resRate >= 80 ? Br(CGreen) : resRate >= 50 ? Br(CAmber) : Br(CRed),
                $"Issue resolution rate\nClosed: {closed} of {_data.IssuesTotal}"));
            int elemTotal = _data.Issues.Sum(i => i.ElementCount);
            kpiGrid.Children.Add(MakeKPICard("ELEMENTS", elemTotal.ToString(), Br(CHeaderBg),
                "Total model elements linked to issues"));
            root.Children.Add(kpiGrid);

            // ── ISSUE GATE STATUS ─────────────────────────────────────────
            bool gatePass = _data.IssuesCritical == 0 && _data.IssuesOverdue == 0;
            var gateCard = MakeCard();
            var gateRow = new StackPanel { Orientation = Orientation.Horizontal };
            var gateDot = new Ellipse { Width = 14, Height = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                Fill = gatePass ? Br(CGreen) : Br(CRed) };
            gateRow.Children.Add(gateDot);
            gateRow.Children.Add(new TextBlock { Text = gatePass ? "ISSUE GATE: PASS — No critical or overdue issues" : "ISSUE GATE: FAIL — Critical or overdue issues require resolution",
                FontWeight = FontWeights.Bold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center,
                Foreground = gatePass ? Br(CGreen) : Br(CRed) });
            gateCard.Child = gateRow;
            root.Children.Add(gateCard);

            // ── ACTION BUTTONS (3 rows) ──────────────────────────────────
            root.Children.Add(MakeSectionHeader("ACTIONS"));
            var actionGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            for (int i = 0; i < 4; i++) actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 3; i++) actionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var actions = new (string Label, string Tag, Color Clr, string Tip)[] {
                ("Raise Issue", "RaiseIssue", Color.FromRgb(0xC6, 0x28, 0x28), "Create new RFI/NCR/SI/Clash issue with element linking and BCF integration"),
                ("Update Status", "UpdateIssue", Color.FromRgb(0xE8, 0x91, 0x2D), "Update issue status, priority, or assignees"),
                ("Bulk Close", "IssuesBulkClose", Color.FromRgb(0x6A, 0x1B, 0x9A), "Close multiple resolved issues at once"),
                ("Select Elements", "SelectIssueElements", Color.FromRgb(0x1A, 0x23, 0x7E), "Select model elements linked to the selected issue"),
                ("BCF Export", "BCFExport", Color.FromRgb(0x00, 0x69, 0x7C), "Export issues as BCF 2.1 for ACC/Navisworks/BIMcollab"),
                ("BCF Import", "BCFImport", Color.FromRgb(0x00, 0x69, 0x7C), "Import BCF issues from external clash/coordination tools"),
                ("Export CSV", "ExportIssues", Color.FromRgb(0x45, 0x50, 0x6E), "Export issue register to CSV for PowerBI/reporting"),
                ("From Warnings", "CreateIssuesFromWarnings", Color.FromRgb(0xE6, 0x5C, 0x00), "Auto-create NCR/SI from critical/high Revit warnings"),
                ("Issue Timeline", "IssueTimeline", Color.FromRgb(0x1A, 0x23, 0x7E), "View issue timeline with status changes and resolution history"),
                ("Add to Meeting", "AutoAgenda", Color.FromRgb(0x00, 0x69, 0x7C), "Add open issues to next meeting agenda grouped by type/priority"),
                ("Create Transmittal", "CreateTransmittal", Color.FromRgb(0x6A, 0x1B, 0x9A), "Create ISO 19650 transmittal linking issues to document exchange"),
                ("Assign Issues", "AssignIssues", Color.FromRgb(0xE8, 0x91, 0x2D), "Multi-assign issues to team members by role/discipline")
            };
            for (int i = 0; i < actions.Length; i++)
            {
                var btn = MakeActionButton(actions[i].Label, actions[i].Tag, Br(actions[i].Clr), actions[i].Tip);
                btn.Margin = new Thickness(2);
                Grid.SetRow(btn, i / 4);
                Grid.SetColumn(btn, i % 4);
                actionGrid.Children.Add(btn);
            }
            root.Children.Add(actionGrid);

            // ── ISSUE BREAKDOWN: Type + Priority + Discipline (3-column) ──
            if (_data.IssuesTotal > 0)
            {
                root.Children.Add(MakeSectionHeader("ISSUE BREAKDOWN"));
                var breakGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
                breakGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                breakGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
                breakGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                breakGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
                breakGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // By Type
                var typeCard = MakeCard();
                var typeStack = new StackPanel();
                typeStack.Children.Add(new TextBlock { Text = "BY TYPE", FontWeight = FontWeights.Bold, FontSize = 11, Foreground = Br(CAccent), Margin = new Thickness(0, 0, 0, 6) });
                var typeGroups = _data.Issues.GroupBy(i => i.Type).OrderByDescending(g => g.Count()).ToList();
                int maxType = typeGroups.Count > 0 ? typeGroups.Max(g => g.Count()) : 1;
                foreach (var g in typeGroups)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                    row.Children.Add(new TextBlock { Text = g.Key, Width = 60, FontSize = 11 });
                    row.Children.Add(new TextBlock { Text = g.Count().ToString(), Width = 25, FontSize = 11, FontWeight = FontWeights.Bold });
                    var barBg = new Border { Background = Br(Color.FromRgb(0xE0, 0xE0, 0xE0)), Height = 10, CornerRadius = new CornerRadius(3), Width = 80 };
                    var barFill = new Border { Background = Br(CAccent), Height = 10, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
                    barBg.Child = barFill;
                    int count = g.Count(); int mx = maxType;
                    barBg.Loaded += (s, e) => { barFill.Width = barBg.ActualWidth * count / Math.Max(1, mx); };
                    row.Children.Add(barBg);
                    typeStack.Children.Add(row);
                }
                typeCard.Child = typeStack;
                Grid.SetColumn(typeCard, 0);
                breakGrid.Children.Add(typeCard);

                // By Priority
                var priCard = MakeCard();
                var priStack = new StackPanel();
                priStack.Children.Add(new TextBlock { Text = "BY PRIORITY", FontWeight = FontWeights.Bold, FontSize = 11, Foreground = Br(CAccent), Margin = new Thickness(0, 0, 0, 6) });
                var priOrder = new[] { "CRITICAL", "HIGH", "MEDIUM", "LOW" };
                var priColors = new Dictionary<string, Color> { ["CRITICAL"] = CRed, ["HIGH"] = CAmber, ["MEDIUM"] = Color.FromRgb(0xFF, 0xB3, 0x00), ["LOW"] = CGreen };
                foreach (var p in priOrder)
                {
                    int cnt = _data.Issues.Count(i => i.Priority == p);
                    if (cnt == 0) continue;
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                    var dot = new Ellipse { Width = 8, Height = 8, Fill = priColors.TryGetValue(p, out var pc) ? Br(pc) : Br(CHeaderBg), Margin = new Thickness(0, 3, 4, 0) };
                    row.Children.Add(dot);
                    row.Children.Add(new TextBlock { Text = p, Width = 65, FontSize = 11 });
                    row.Children.Add(new TextBlock { Text = cnt.ToString(), FontSize = 11, FontWeight = FontWeights.Bold });
                    priStack.Children.Add(row);
                }
                priCard.Child = priStack;
                Grid.SetColumn(priCard, 2);
                breakGrid.Children.Add(priCard);

                // By Discipline
                var discCard = MakeCard();
                var discStack = new StackPanel();
                discStack.Children.Add(new TextBlock { Text = "BY DISCIPLINE", FontWeight = FontWeights.Bold, FontSize = 11, Foreground = Br(CAccent), Margin = new Thickness(0, 0, 0, 6) });
                var discGroups = _data.Issues.Where(i => !string.IsNullOrEmpty(i.Discipline))
                    .GroupBy(i => i.Discipline).OrderByDescending(g => g.Count()).Take(8).ToList();
                foreach (var g in discGroups)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                    row.Children.Add(new TextBlock { Text = g.Key, Width = 50, FontSize = 11 });
                    row.Children.Add(new TextBlock { Text = g.Count().ToString(), FontSize = 11, FontWeight = FontWeights.Bold });
                    discStack.Children.Add(row);
                }
                if (discGroups.Count == 0)
                    discStack.Children.Add(new TextBlock { Text = "No discipline data", FontSize = 10, FontStyle = FontStyles.Italic, Foreground = Brushes.Gray });
                discCard.Child = discStack;
                Grid.SetColumn(discCard, 4);
                breakGrid.Children.Add(discCard);

                root.Children.Add(breakGrid);
            }

            // ── ASSIGNEE WORKLOAD ────────────────────────────────────────
            if (_data.Issues.Count > 0)
            {
                root.Children.Add(MakeSectionHeader("ASSIGNEE WORKLOAD"));
                var assignCard = MakeCard();
                var assignStack = new StackPanel();
                // Build per-assignee open issue count (multi-assignee aware)
                var assigneeLoad = new Dictionary<string, (int Open, int Critical, int Overdue, int Total)>(StringComparer.OrdinalIgnoreCase);
                foreach (var iss in _data.Issues)
                {
                    var names = iss.AssigneeList.Count > 0 ? iss.AssigneeList : new List<string> { iss.Assignee ?? "Unassigned" };
                    foreach (var name in names)
                    {
                        string key = string.IsNullOrWhiteSpace(name) ? "Unassigned" : name;
                        if (!assigneeLoad.ContainsKey(key)) assigneeLoad[key] = (0, 0, 0, 0);
                        var cur = assigneeLoad[key];
                        assigneeLoad[key] = (
                            cur.Open + (iss.Status == "OPEN" ? 1 : 0),
                            cur.Critical + (iss.Priority == "CRITICAL" ? 1 : 0),
                            cur.Overdue + (iss.IsOverdue ? 1 : 0),
                            cur.Total + 1);
                    }
                }
                // Header row
                var hdrRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                hdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                hdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                hdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                hdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
                hdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                AddCellToGrid(hdrRow, "Assignee", 0, 0, true);
                AddCellToGrid(hdrRow, "Open", 0, 1, true);
                AddCellToGrid(hdrRow, "Crit", 0, 2, true);
                AddCellToGrid(hdrRow, "Overdue", 0, 3, true);
                AddCellToGrid(hdrRow, "Load", 0, 4, true);
                assignStack.Children.Add(hdrRow);

                int maxLoad = assigneeLoad.Values.Max(v => v.Open);
                foreach (var kv in assigneeLoad.OrderByDescending(x => x.Value.Open).Take(15))
                {
                    var aRow = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                    aRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                    aRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                    aRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                    aRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
                    aRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    AddCellToGrid(aRow, kv.Key, 0, 0, false, null, null);
                    AddCellToGrid(aRow, kv.Value.Open.ToString(), 0, 1, false, FontWeights.Bold,
                        kv.Value.Open > 5 ? Br(CRed) : kv.Value.Open > 0 ? Br(CAmber) : Br(CGreen));
                    AddCellToGrid(aRow, kv.Value.Critical.ToString(), 0, 2, false, null,
                        kv.Value.Critical > 0 ? Br(CRed) : null);
                    AddCellToGrid(aRow, kv.Value.Overdue.ToString(), 0, 3, false, null,
                        kv.Value.Overdue > 0 ? Br(CRed) : null);
                    // Mini bar
                    var barBg = new Border { Background = Br(Color.FromRgb(0xE0, 0xE0, 0xE0)), Height = 10, CornerRadius = new CornerRadius(3), Margin = new Thickness(4, 3, 0, 0) };
                    var barFill = new Border { Background = kv.Value.Overdue > 0 ? Br(CRed) : kv.Value.Critical > 0 ? Br(CAmber) : Br(CAccent),
                        Height = 10, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
                    barBg.Child = barFill;
                    int oCount = kv.Value.Open; int mxL = Math.Max(1, maxLoad);
                    barBg.Loaded += (s, e) => { barFill.Width = barBg.ActualWidth * oCount / mxL; };
                    Grid.SetColumn(barBg, 4);
                    aRow.Children.Add(barBg);
                    aRow.ToolTip = $"{kv.Key}: {kv.Value.Total} total, {kv.Value.Open} open, {kv.Value.Critical} critical, {kv.Value.Overdue} overdue";
                    assignStack.Children.Add(aRow);
                }
                assignCard.Child = assignStack;
                root.Children.Add(assignCard);
            }

            // ── AEC/FM ROLE REFERENCE ────────────────────────────────────
            root.Children.Add(MakeSectionHeader("AEC/FM ROLE CATEGORIES"));
            var rolesCard = MakeCard();
            var rolesGrid = new Grid { Margin = new Thickness(0) };
            rolesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rolesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rolesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            // AEC/FM role categories for multi-assign
            var roleCategories = new (string Category, string[] Roles)[] {
                ("CLIENT / EMPLOYER", new[] { "Client Rep", "Employer's Agent", "Project Sponsor", "End User Rep" }),
                ("DESIGN TEAM", new[] { "Architect", "Structural Engineer", "MEP Engineer", "Civil Engineer", "Landscape Architect", "Interior Designer", "Lighting Designer", "Acoustic Consultant", "Fire Engineer", "Sustainability Consultant" }),
                ("BIM / DIGITAL", new[] { "BIM Manager", "BIM Coordinator", "BIM Technician", "Information Manager", "Digital Engineer", "Data Manager" }),
                ("CONSTRUCTION", new[] { "Main Contractor", "Site Manager", "Quantity Surveyor", "Construction Manager", "Planning Engineer", "Temporary Works Coordinator", "Health & Safety Manager" }),
                ("SPECIALIST TRADES", new[] { "MEP Subcontractor", "Steelwork Fabricator", "Cladding Contractor", "Piling Contractor", "Curtain Wall Installer", "Electrical Contractor", "Mechanical Contractor", "Plumbing Contractor", "Fire Protection Contractor" }),
                ("FM / OPERATIONS", new[] { "Facilities Manager", "Building Manager", "Maintenance Manager", "Energy Manager", "Space Planner", "Asset Manager", "CAFM Administrator", "Helpdesk Manager" }),
                ("COMPLIANCE / QA", new[] { "CDM Coordinator", "Building Control", "Planning Officer", "Quality Manager", "Clerk of Works", "Approved Inspector", "Environmental Consultant" }),
                ("SPECIALIST CONSULTANT", new[] { "Access Consultant", "Heritage Consultant", "Transport Planner", "Ecology Consultant", "Geotechnical Engineer", "Facade Engineer", "Vibration Specialist" }),
                ("PROJECT MANAGEMENT", new[] { "Project Manager", "Programme Manager", "Contract Administrator", "Cost Consultant", "Risk Manager", "Procurement Manager" })
            };
            int rolesPerCol = (roleCategories.Length + 2) / 3;
            for (int col = 0; col < 3; col++)
            {
                var colStack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
                for (int i = col * rolesPerCol; i < Math.Min((col + 1) * rolesPerCol, roleCategories.Length); i++)
                {
                    var (cat, roles) = roleCategories[i];
                    colStack.Children.Add(new TextBlock { Text = cat, FontWeight = FontWeights.Bold, FontSize = 10, Foreground = Br(CAccent), Margin = new Thickness(0, 6, 0, 2) });
                    foreach (var role in roles)
                        colStack.Children.Add(new TextBlock { Text = $"  • {role}", FontSize = 10, Foreground = Br(Color.FromRgb(0x42, 0x42, 0x42)) });
                }
                Grid.SetColumn(colStack, col);
                rolesGrid.Children.Add(colStack);
            }
            rolesCard.Child = rolesGrid;
            root.Children.Add(rolesCard);

            // ── SLA STATUS ───────────────────────────────────────────────
            if (_data.Issues.Any(i => i.Status == "OPEN"))
            {
                root.Children.Add(MakeSectionHeader("SLA STATUS"));
                var slaCard = MakeCard();
                var slaStack = new StackPanel();
                var slaThresholds = new (string Priority, double Hours, Color Clr)[] {
                    ("CRITICAL", 4, CRed), ("HIGH", 24, CAmber), ("MEDIUM", 168, Color.FromRgb(0xFF, 0xB3, 0x00)), ("LOW", 336, CGreen)
                };
                foreach (var (pri, hours, clr) in slaThresholds)
                {
                    int openCount = _data.Issues.Count(i => i.Priority == pri && i.Status == "OPEN");
                    int overdueCount = _data.Issues.Count(i => i.Priority == pri && i.IsOverdue);
                    if (openCount == 0) continue;
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                    var dot = new Ellipse { Width = 10, Height = 10, Fill = overdueCount > 0 ? Br(CRed) : Br(CGreen), Margin = new Thickness(0, 2, 6, 0) };
                    row.Children.Add(dot);
                    row.Children.Add(new TextBlock { Text = pri, Width = 70, FontWeight = FontWeights.Bold, FontSize = 11 });
                    string slaText = hours < 24 ? $"{hours}h" : hours < 168 ? $"{hours / 24}d" : $"{hours / 168}wk";
                    row.Children.Add(new TextBlock { Text = $"SLA: {slaText}", Width = 70, FontSize = 10, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)) });
                    row.Children.Add(new TextBlock { Text = $"{openCount} open", Width = 60, FontSize = 11 });
                    if (overdueCount > 0)
                        row.Children.Add(new TextBlock { Text = $"⚠ {overdueCount} overdue", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Br(CRed) });
                    else
                        row.Children.Add(new TextBlock { Text = "✔ within SLA", FontSize = 11, Foreground = Br(CGreen) });
                    slaStack.Children.Add(row);
                }
                slaCard.Child = slaStack;
                root.Children.Add(slaCard);
            }

            // ── ISSUE REGISTER (DataGrid) ────────────────────────────────
            root.Children.Add(MakeSectionHeader("ISSUE REGISTER"));
            if (_data.Issues.Count > 0)
            {
                // Filter bar
                var filterRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                filterRow.Children.Add(new TextBlock { Text = "Filter:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                var filterCombo = new System.Windows.Controls.ComboBox { Width = 100, FontSize = 11, Margin = new Thickness(0, 0, 8, 0) };
                filterCombo.Items.Add("All");
                filterCombo.Items.Add("Open");
                filterCombo.Items.Add("Closed");
                filterCombo.Items.Add("Critical");
                filterCombo.Items.Add("Overdue");
                filterCombo.SelectedIndex = 0;
                filterRow.Children.Add(filterCombo);
                // Search
                filterRow.Children.Add(new TextBlock { Text = "Search:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                var searchBox = new System.Windows.Controls.TextBox { Width = 150, FontSize = 11 };
                filterRow.Children.Add(searchBox);
                root.Children.Add(filterRow);

                var dg = new DataGrid
                {
                    AutoGenerateColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column,
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, CanUserSortColumns = true,
                    SelectionMode = DataGridSelectionMode.Extended, FontSize = 11,
                    BorderBrush = Br(CBorder), BorderThickness = new Thickness(1),
                    RowHeaderWidth = 0, Margin = new Thickness(0, 0, 0, 8),
                    MaxHeight = 350
                };
                VirtualizingPanel.SetIsVirtualizing(dg, true);
                VirtualizingPanel.SetVirtualizationMode(dg, VirtualizationMode.Recycling);
                dg.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new Binding("Id"), Width = 80 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Title", Binding = new Binding("Title"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                dg.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new Binding("Type"), Width = 50 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Priority", Binding = new Binding("Priority"), Width = 65 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding("Status"), Width = 60 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Assignee(s)", Binding = new Binding("AssigneeDisplay"), Width = 110 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Disc", Binding = new Binding("Discipline"), Width = 40 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Elems", Binding = new Binding("ElementCount"), Width = 45 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Created", Binding = new Binding("Created"), Width = 75 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Age", Binding = new Binding("DaysOpen"), Width = 40 });

                // Full issue list, will be filtered via filterCombo/searchBox
                var allIssues = _data.Issues;
                dg.ItemsSource = allIssues;

                // Filter logic
                filterCombo.SelectionChanged += (s, e) =>
                {
                    string filter = filterCombo.SelectedItem?.ToString() ?? "All";
                    string search = searchBox.Text?.Trim().ToLowerInvariant() ?? "";
                    ApplyIssueFilter(dg, allIssues, filter, search);
                };
                searchBox.TextChanged += (s, e) =>
                {
                    string filter = filterCombo.SelectedItem?.ToString() ?? "All";
                    string search = searchBox.Text?.Trim().ToLowerInvariant() ?? "";
                    ApplyIssueFilter(dg, allIssues, filter, search);
                };

                // Double-click to zoom
                dg.MouseDoubleClick += (s, e) =>
                {
                    if (dg.SelectedItem is IssueRow issue)
                    { DispatchAction($"ZoomToIssue_{issue.Id}"); }
                };
                dg.ToolTip = "Double-click: zoom to 3D section box  |  Right-click: context menu  |  Hover assignee to see all assignees";

                // Row style: red for overdue, bold for critical, closed = grey italic
                var rowStyle = new Style(typeof(DataGridRow));
                var overdueT = new DataTrigger { Binding = new Binding("IsOverdue"), Value = true };
                overdueT.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Br(Color.FromRgb(0xFF, 0xEB, 0xEE))));
                overdueT.Setters.Add(new Setter(DataGridRow.ForegroundProperty, Br(Color.FromRgb(0xB7, 0x1C, 0x1C))));
                rowStyle.Triggers.Add(overdueT);
                var criticalT = new DataTrigger { Binding = new Binding("Priority"), Value = "CRITICAL" };
                criticalT.Setters.Add(new Setter(DataGridRow.FontWeightProperty, FontWeights.Bold));
                rowStyle.Triggers.Add(criticalT);
                var closedT = new DataTrigger { Binding = new Binding("Status"), Value = "CLOSED" };
                closedT.Setters.Add(new Setter(DataGridRow.ForegroundProperty, Br(Color.FromRgb(0x9E, 0x9E, 0x9E))));
                closedT.Setters.Add(new Setter(DataGridRow.FontStyleProperty, FontStyles.Italic));
                rowStyle.Triggers.Add(closedT);
                dg.RowStyle = rowStyle;

                // Context menu
                var issueCtx = new ContextMenu();
                var zoomMi = new MenuItem { Header = "Zoom to 3D Section Box" };
                zoomMi.Click += (s2, e2) => { if (dg.SelectedItem is IssueRow iss) { DispatchAction($"ZoomToIssue_{iss.Id}"); } };
                var selectMi = new MenuItem { Header = "Select Linked Elements" };
                selectMi.Click += (s2, e2) => { if (dg.SelectedItem is IssueRow iss) { DispatchAction($"SelectIssue_{iss.Id}"); } };
                var updateMi = new MenuItem { Header = "Update Issue Status" };
                updateMi.Click += (s2, e2) => { DispatchAction("UpdateIssue"); };
                var assignMi = new MenuItem { Header = "Assign / Reassign" };
                assignMi.Click += (s2, e2) => { DispatchAction("AssignIssues"); };
                var meetingMi = new MenuItem { Header = "Add to Meeting Agenda" };
                meetingMi.Click += (s2, e2) => { DispatchAction("AutoAgenda"); };
                var transmitMi = new MenuItem { Header = "Link to Transmittal" };
                transmitMi.Click += (s2, e2) => { DispatchAction("CreateTransmittal"); };
                issueCtx.Items.Add(zoomMi);
                issueCtx.Items.Add(selectMi);
                issueCtx.Items.Add(new Separator());
                issueCtx.Items.Add(updateMi);
                issueCtx.Items.Add(assignMi);
                issueCtx.Items.Add(new Separator());
                issueCtx.Items.Add(meetingMi);
                issueCtx.Items.Add(transmitMi);
                dg.ContextMenu = issueCtx;

                root.Children.Add(dg);
            }
            else
            {
                var infoCard = MakeCard();
                infoCard.Child = new TextBlock
                {
                    Text = "No issues found. Click 'Raise Issue' to create one.\n\n" +
                           "Issue types: RFI (Request for Information), NCR (Non-Conformance Report),\n" +
                           "SI (Site Instruction), Clash, Snagging, Design, Change Order\n\n" +
                           "Issues are stored in _bim_manager/issues.json alongside the project.\n" +
                           "BCF 2.1 export/import supported for ACC, Navisworks, BIMcollab, Solibri.",
                    FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray
                };
                root.Children.Add(infoCard);
            }

            return sv;
        }

        /// <summary>Filter issue DataGrid by status and search text.</summary>
        private void ApplyIssueFilter(DataGrid dg, List<IssueRow> allIssues, string filter, string search)
        {
            IEnumerable<IssueRow> filtered = allIssues;
            switch (filter)
            {
                case "Open": filtered = filtered.Where(i => i.Status == "OPEN"); break;
                case "Closed": filtered = filtered.Where(i => i.Status == "CLOSED"); break;
                case "Critical": filtered = filtered.Where(i => i.Priority == "CRITICAL"); break;
                case "Overdue": filtered = filtered.Where(i => i.IsOverdue); break;
            }
            if (!string.IsNullOrEmpty(search))
                filtered = filtered.Where(i =>
                    (i.Title ?? "").ToLowerInvariant().Contains(search) ||
                    (i.Id ?? "").ToLowerInvariant().Contains(search) ||
                    (i.Assignees ?? "").ToLowerInvariant().Contains(search) ||
                    (i.Discipline ?? "").ToLowerInvariant().Contains(search));
            dg.ItemsSource = filtered.ToList();
        }

        // ════════════════════════════════════════════════════════════════
        //  REVISIONS TAB
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildRevisionsTab()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(20) };
            var stack = new StackPanel();

            // ── KPI Strip ──
            int totalClouds = _data.Revisions.Count > 0 ? _data.Revisions.Sum(r => r.Clouds) : _data.RevisionClouds;
            var statusGroups = _data.Revisions.GroupBy(r => r.Status ?? "Unknown").ToDictionary(g => g.Key, g => g.Count());
            int issued = statusGroups.TryGetValue("Issued", out int iv) ? iv : 0;

            var kpiRow = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, 0, 12) };
            kpiRow.Children.Add(MakeKPICard("REVISIONS", _data.RevisionCount.ToString(), Br(CHeaderBg),
                $"Total revisions in project\nThis week: {_data.RevisionsThisWeek}"));
            kpiRow.Children.Add(MakeKPICard("CLOUDS", totalClouds.ToString(), Br(CAccent),
                $"Total revision clouds placed\nUnresolved: {_data.CloudsUnresolved}"));
            kpiRow.Children.Add(MakeKPICard("THIS WEEK", _data.RevisionsThisWeek.ToString(),
                _data.RevisionsThisWeek > 0 ? Br(Color.FromRgb(0x15, 0x65, 0xC0)) : Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Revisions created in the last 7 days"));
            kpiRow.Children.Add(MakeKPICard("UNRESOLVED", _data.CloudsUnresolved.ToString(),
                _data.CloudsUnresolved > 0 ? Br(CRed) : Br(CGreen),
                "Revision clouds not yet addressed\nRequire action before next data drop"));
            kpiRow.Children.Add(MakeKPICard("ISSUED", issued.ToString(), Br(CGreen),
                "Revisions formally issued to sheets"));
            stack.Children.Add(kpiRow);

            // ── Revision Status Distribution ──
            if (statusGroups.Count > 0)
            {
                stack.Children.Add(MakeSectionHeader("REVISION STATUS DISTRIBUTION"));
                var statusCard = MakeCard();
                var statusStack = new StackPanel();
                int maxStatusCount = statusGroups.Values.Max();
                foreach (var kv in statusGroups.OrderByDescending(x => x.Value))
                {
                    var statusRow = new Grid { Margin = new Thickness(0, 2, 0, 2), Height = 24 };
                    statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                    statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                    statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var statusColor = kv.Key == "Issued" ? CGreen : kv.Key == "Draft" ? CAmber : kv.Key == "Superseded" ? Color.FromRgb(0x75, 0x75, 0x75) : CHeaderBg;
                    statusRow.Children.Add(new TextBlock { Text = kv.Key, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                    var countTb = new TextBlock { Text = kv.Value.ToString(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Br(statusColor), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(countTb, 1); statusRow.Children.Add(countTb);

                    double barPct = maxStatusCount > 0 ? kv.Value * 100.0 / maxStatusCount : 0;
                    var barBg = new Border { Background = Br(Color.FromRgb(0xE8, 0xEA, 0xED)), Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
                    var barFill = new Border { Background = Br(statusColor), Height = 10, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.85 };
                    var barGrid = new Grid(); barGrid.Children.Add(barBg); barGrid.Children.Add(barFill);
                    barBg.Loaded += (s, e) => { barFill.Width = Math.Max(4, barBg.ActualWidth * barPct / 100.0); };
                    Grid.SetColumn(barGrid, 2); statusRow.Children.Add(barGrid);
                    statusStack.Children.Add(statusRow);
                }
                statusCard.Child = statusStack;
                stack.Children.Add(statusCard);
            }

            // ── Clouds by Discipline ──
            if (_data.CloudsByDiscipline.Count > 0)
            {
                stack.Children.Add(MakeSectionHeader("REVISION CLOUDS BY DISCIPLINE"));
                var discCard = MakeCard();
                var discStack = new StackPanel();
                int maxDiscClouds = _data.CloudsByDiscipline.Values.Max();
                foreach (var kv in _data.CloudsByDiscipline.OrderByDescending(x => x.Value))
                {
                    var discColor = kv.Key switch
                    {
                        "M" or "Mechanical" => Color.FromRgb(0x15, 0x65, 0xC0),
                        "E" or "Electrical" => Color.FromRgb(0xFF, 0xA0, 0x00),
                        "P" or "Plumbing" => CGreen,
                        "A" or "Architectural" => Color.FromRgb(0x75, 0x75, 0x75),
                        "S" or "Structural" => CRed,
                        _ => CHeaderBg
                    };
                    var discRow = new Grid { Margin = new Thickness(0, 2, 0, 2), Height = 22 };
                    discRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                    discRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                    discRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    discRow.Children.Add(new TextBlock { Text = kv.Key, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                    var cntTb = new TextBlock { Text = $"{kv.Value} clouds", FontSize = 10, Foreground = Br(discColor), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(cntTb, 1); discRow.Children.Add(cntTb);

                    double barPct = maxDiscClouds > 0 ? kv.Value * 100.0 / maxDiscClouds : 0;
                    var barBg = new Border { Background = Br(Color.FromRgb(0xE8, 0xEA, 0xED)), Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
                    var barFill = new Border { Background = Br(discColor), Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.85 };
                    var barGrid = new Grid(); barGrid.Children.Add(barBg); barGrid.Children.Add(barFill);
                    barBg.Loaded += (s, e) => { barFill.Width = Math.Max(4, barBg.ActualWidth * barPct / 100.0); };
                    Grid.SetColumn(barGrid, 2); discRow.Children.Add(barGrid);
                    discStack.Children.Add(discRow);
                }
                discCard.Child = discStack;
                stack.Children.Add(discCard);
            }

            // ── Clouds by Sheet (top 10) ──
            if (_data.CloudsBySheet.Count > 0)
            {
                stack.Children.Add(MakeSectionHeader("TOP SHEETS BY REVISION CLOUDS"));
                var sheetCard = MakeCard();
                var sheetStack = new StackPanel();
                int maxSheetClouds = _data.CloudsBySheet.Values.Max();
                foreach (var kv in _data.CloudsBySheet.OrderByDescending(x => x.Value).Take(10))
                {
                    var sheetRow = new Grid { Margin = new Thickness(0, 2, 0, 2), Height = 22 };
                    sheetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    sheetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                    sheetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

                    sheetRow.Children.Add(new TextBlock { Text = kv.Key, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                    var cntTb = new TextBlock { Text = kv.Value.ToString(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Br(CAccent), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
                    Grid.SetColumn(cntTb, 1); sheetRow.Children.Add(cntTb);

                    double barPct = maxSheetClouds > 0 ? kv.Value * 100.0 / maxSheetClouds : 0;
                    var barBg = new Border { Background = Br(Color.FromRgb(0xE8, 0xEA, 0xED)), Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
                    var barFill = new Border { Background = Br(CAccent), Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.8 };
                    var barGrid = new Grid(); barGrid.Children.Add(barBg); barGrid.Children.Add(barFill);
                    barBg.Loaded += (s, e) => { barFill.Width = Math.Max(4, barBg.ActualWidth * barPct / 100.0); };
                    Grid.SetColumn(barGrid, 2); sheetRow.Children.Add(barGrid);
                    sheetStack.Children.Add(sheetRow);
                }
                sheetCard.Child = sheetStack;
                stack.Children.Add(sheetCard);
            }

            // ── Revision Register DataGrid ──
            stack.Children.Add(MakeSectionHeader("REVISION REGISTER"));
            if (_data.Revisions.Count > 0)
            {
                var dg = new DataGrid
                {
                    AutoGenerateColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column,
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, CanUserSortColumns = true,
                    SelectionMode = DataGridSelectionMode.Single, FontSize = 11,
                    BorderBrush = Br(CBorder), BorderThickness = new Thickness(1), RowHeaderWidth = 0,
                    MaxHeight = 250, Margin = new Thickness(0, 0, 0, 8)
                };
                dg.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new Binding("Id"), Width = 50 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                dg.Columns.Add(new DataGridTextColumn { Header = "Date", Binding = new Binding("Date"), Width = 85 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Description", Binding = new Binding("Description"), Width = 180 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Clouds", Binding = new Binding("Clouds"), Width = 50 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding("Status"), Width = 60 });
                dg.ItemsSource = _data.Revisions;

                // Row style: Issued=green tint, Superseded=grey
                var rowStyle = new Style(typeof(DataGridRow));
                var issuedTrigger = new DataTrigger { Binding = new Binding("Status"), Value = "Issued" };
                issuedTrigger.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Br(Color.FromRgb(0xE8, 0xF5, 0xE9))));
                rowStyle.Triggers.Add(issuedTrigger);
                var supersededTrigger = new DataTrigger { Binding = new Binding("Status"), Value = "Superseded" };
                supersededTrigger.Setters.Add(new Setter(DataGridRow.ForegroundProperty, Brushes.Gray));
                supersededTrigger.Setters.Add(new Setter(DataGridRow.FontStyleProperty, FontStyles.Italic));
                rowStyle.Triggers.Add(supersededTrigger);
                dg.RowStyle = rowStyle;

                dg.MouseDoubleClick += (s, e) =>
                {
                    if (dg.SelectedItem is RevisionRow rev)
                    { DispatchAction($"SelectRevision_{rev.Id}"); }
                };
                // Right-click context menu
                var revCtx = new ContextMenu();
                var revSelectItem = new MenuItem { Header = "Select Revision Clouds" };
                revSelectItem.Click += (s2, e2) =>
                {
                    if (dg.SelectedItem is RevisionRow rev)
                    { DispatchAction($"SelectRevision_{rev.Id}"); }
                };
                var revZoomItem = new MenuItem { Header = "Zoom to Revision Clouds (3D)" };
                revZoomItem.Click += (s2, e2) =>
                {
                    if (dg.SelectedItem is RevisionRow rev)
                    { DispatchAction($"ZoomToRevision_{rev.Id}"); }
                };
                var revIssueItem = new MenuItem { Header = "Issue Sheets for This Revision" };
                revIssueItem.Click += (s2, e2) =>
                {
                    if (dg.SelectedItem is RevisionRow)
                    { DispatchAction("IssueSheetsForRevision"); }
                };
                var revExportItem = new MenuItem { Header = "Export Revision Report" };
                revExportItem.Click += (s2, e2) =>
                {
                    DispatchAction("RevisionExport");
                };
                revCtx.Items.Add(revSelectItem);
                revCtx.Items.Add(revZoomItem);
                revCtx.Items.Add(new Separator());
                revCtx.Items.Add(revIssueItem);
                revCtx.Items.Add(revExportItem);
                dg.ContextMenu = revCtx;
                stack.Children.Add(dg);
            }
            else
            {
                var infoCard = MakeCard();
                infoCard.Child = new TextBlock
                {
                    Text = "No revisions found. Click 'Create Revision' to start tracking changes.\n\n" +
                           "Revisions track design changes per ISO 19650:\n" +
                           "  \u2022 Automatic cloud placement on changed elements\n" +
                           "  \u2022 Tag snapshot comparison between revisions\n" +
                           "  \u2022 Sheet issuance with revision scheduling\n" +
                           "  \u2022 Naming convention enforcement per BS 1192",
                    FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray
                };
                stack.Children.Add(infoCard);
            }

            // ── Actions ──
            stack.Children.Add(new Border { Height = 12 });
            stack.Children.Add(MakeSectionHeader("REVISION MANAGEMENT"));
            var createWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            createWrap.Children.Add(MakeActionButton("Create Revision", "CreateRevision", Br(CGreen),
                "Create a new revision with ISO 19650 naming and compliance gate check"));
            createWrap.Children.Add(MakeActionButton("Auto Revision Clouds", "AutoRevisionCloud", Br(CAccent),
                "Automatically place revision clouds on elements changed since last revision"));
            createWrap.Children.Add(MakeActionButton("Bulk Revision Stamp", "BulkRevisionStamp", Br(CRed),
                "Stamp revision information across multiple sheets in batch"));
            createWrap.Children.Add(MakeActionButton("Auto Rev on Tag Change", "AutoRevisionOnTagChange", Br(Color.FromRgb(0x6A, 0x1B, 0x9A)),
                "Automatically create revision when tag data changes on elements"));
            stack.Children.Add(createWrap);

            stack.Children.Add(MakeSectionHeader("TRACKING & COMPARISON"));
            var trackWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            trackWrap.Children.Add(MakeActionButton("Take Snapshot", "TakeSnapshot", Br(Color.FromRgb(0x00, 0x69, 0x7C)),
                "Capture current tag state as a snapshot for later comparison"));
            trackWrap.Children.Add(MakeActionButton("Compare Revisions", "RevisionCompare", Br(CHeaderBg),
                "Compare two revision snapshots: see added/changed/removed tags per element"));
            trackWrap.Children.Add(MakeActionButton("Track Elements", "TrackElementRevisions", Br(Color.FromRgb(0x6A, 0x1B, 0x9A)),
                "Track per-element revision history across all snapshots"));
            trackWrap.Children.Add(MakeActionButton("Tag Revision Diff", "TagRevisionDiff", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Token-level diff between two snapshots: which tokens changed, old vs new values"));
            stack.Children.Add(trackWrap);

            stack.Children.Add(MakeSectionHeader("ISSUANCE & EXPORT"));
            var issueWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            issueWrap.Children.Add(MakeActionButton("Issue Sheets", "IssueSheetsForRevision", Br(CAmber),
                "Issue selected sheets for the latest revision — adds to revision schedule"));
            issueWrap.Children.Add(MakeActionButton("Naming Enforce", "RevisionNamingEnforce", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Enforce ISO 19650 / BS 1192 revision naming conventions"));
            issueWrap.Children.Add(MakeActionButton("Tag Integration", "RevisionTagIntegration", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Integrate revision data with STING tags: stamp REV token on elements"));
            issueWrap.Children.Add(MakeActionButton("Revision Schedule", "RevisionSchedule", Br(Color.FromRgb(0x15, 0x65, 0xC0)),
                "Create/view Revit revision schedule showing all revisions and sheets"));
            issueWrap.Children.Add(MakeActionButton("Export CSV", "RevisionExport", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Export revision register to CSV for external tracking"));
            stack.Children.Add(issueWrap);

            scroll.Content = stack;
            return scroll;
        }

        // ════════════════════════════════════════════════════════════════
        //  PLATFORM TAB
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildPlatformTab()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(20) };
            var stack = new StackPanel();

            // ── SYNC STATUS ──
            stack.Children.Add(MakeSectionHeader("PLATFORM SYNC STATUS"));
            var syncCard = MakeCard();
            var syncStack = new StackPanel();
            var syncGrid = new Grid();
            syncGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            syncGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var syncLeft = new StackPanel();
            syncLeft.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(_data.LastSyncTime) ? "No sync performed yet" : $"Last sync: {_data.LastSyncTime}",
                FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4)
            });
            syncLeft.Children.Add(new TextBlock
            {
                Text = $"Changes detected: {_data.SyncChanges}",
                FontSize = 12, Foreground = _data.SyncChanges > 0 ? Br(CAccent) : Br(CGreen)
            });
            syncLeft.Children.Add(new TextBlock
            {
                Text = "Sync compares local model against CDE platform, detects added/modified/deleted files,\n" +
                       "validates ISO 19650 naming convention, and reports delta for review before push.",
                FontSize = 10, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
            });
            syncGrid.Children.Add(syncLeft);
            var syncBtn = MakeActionButton("Sync Now", "PlatformSync", Br(CHeaderBg),
                "Bidirectional sync: detect changes, validate naming, generate delta report");
            syncBtn.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(syncBtn, 1);
            syncGrid.Children.Add(syncBtn);
            syncCard.Child = syncGrid;
            stack.Children.Add(syncCard);

            // ── CDE ──
            stack.Children.Add(new Border { Height = 12 });
            stack.Children.Add(MakeSectionHeader("CDE — COMMON DATA ENVIRONMENT"));
            stack.Children.Add(new TextBlock
            {
                Text = "ISO 19650-2 Common Data Environment: manage document status lifecycle (WIP → SHARED → PUBLISHED → ARCHIVE) with suitability codes and approval gates.",
                FontSize = 10, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            });
            var cdeWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
            cdeWrap.Children.Add(MakeActionButton("CDE Package", "CDEPackage", Br(CGreen),
                "Package deliverables into ISO 19650 CDE folder structure (WIP/Shared/Published/Archive)"));
            cdeWrap.Children.Add(MakeActionButton("Set CDE Status", "CDEStatus", Br(CAccent),
                "Transition document CDE status with lifecycle validation (one-way: WIP→SHARED→PUBLISHED→ARCHIVE)"));
            cdeWrap.Children.Add(MakeActionButton("Validate Naming", "ValidateDocNaming", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Check all document names against ISO 19650 naming convention: Project-Originator-Volume-Level-Type-Role-Number"));
            cdeWrap.Children.Add(MakeActionButton("Document Register", "DocumentRegister", Br(CHeaderBg),
                "View/manage ISO 19650 document register with status tracking"));
            stack.Children.Add(cdeWrap);

            // ── BCF ──
            stack.Children.Add(MakeSectionHeader("BCF — BIM COLLABORATION FORMAT"));
            stack.Children.Add(new TextBlock
            {
                Text = "BCF 2.1 issue exchange for clash detection and coordination with Navisworks, Solibri, BIMcollab, Trimble Connect, and other BCF-compatible platforms.",
                FontSize = 10, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            });
            var bcfWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
            bcfWrap.Children.Add(MakeActionButton("BCF Export", "BCFExport", Br(CAccent),
                "Export issues as BCF 2.1 XML with viewpoints for Navisworks/Solibri/BIMcollab"));
            bcfWrap.Children.Add(MakeActionButton("BCF Import", "BCFImport", Br(CAccent),
                "Import BCF issues from external clash detection tools (with duplicate detection)"));
            bcfWrap.Children.Add(MakeActionButton("Create Transmittal", "CreateTransmittal", Br(Color.FromRgb(0x00, 0x69, 0x7C)),
                "Create ISO 19650 transmittal record with document list and recipient tracking"));
            stack.Children.Add(bcfWrap);

            // ── DATA EXCHANGE ──
            stack.Children.Add(MakeSectionHeader("DATA EXCHANGE"));
            stack.Children.Add(new TextBlock
            {
                Text = "Bidirectional data exchange via Excel (30+ columns), COBie V2.4 (FM handover), and IFC (openBIM). Includes validation rules, change tracking, and audit trail.",
                FontSize = 10, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            });
            var dataWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
            dataWrap.Children.Add(MakeActionButton("Excel Export", "ExportToExcel", Br(Color.FromRgb(0x2E, 0x7D, 0x32)),
                "Export element data to Excel: tags, identity, spatial, MEP data (30+ columns)"));
            dataWrap.Children.Add(MakeActionButton("Excel Import", "ImportFromExcel", Br(Color.FromRgb(0x2E, 0x7D, 0x32)),
                "Import validated data from Excel with change preview and audit trail"));
            dataWrap.Children.Add(MakeActionButton("Excel Round-Trip", "ExcelRoundTrip", Br(Color.FromRgb(0x2E, 0x7D, 0x32)),
                "One-click export → edit in Excel → import cycle with change tracking"));
            dataWrap.Children.Add(MakeActionButton("Export Template", "ExportTemplate", Br(Color.FromRgb(0x2E, 0x7D, 0x32)),
                "Generate blank Excel template with PROD code dropdown validation"));
            dataWrap.Children.Add(MakeActionButton("COBie Export", "COBieExport", Br(Color.FromRgb(0x6A, 0x1B, 0x9A)),
                "Export COBie V2.4 FM handover (19 worksheets, 22 project presets)"));
            dataWrap.Children.Add(MakeActionButton("COBie Stream", "StreamingCOBieExport", Br(Color.FromRgb(0x6A, 0x1B, 0x9A)),
                "Streaming COBie for 100K+ element models — batched low-memory processing"));
            dataWrap.Children.Add(MakeActionButton("IFC Export", "IFCExport", Br(Color.FromRgb(0x6A, 0x1B, 0x9A)),
                "Export IFC with STING property mapping for openBIM exchange"));
            dataWrap.Children.Add(MakeActionButton("BOQ Export", "BOQExport", Br(Color.FromRgb(0x6A, 0x1B, 0x9A)),
                "Export Bill of Quantities with section headings and subtotals (XLSX)"));
            stack.Children.Add(dataWrap);

            // ── CLOUD PLATFORMS ──
            stack.Children.Add(MakeSectionHeader("CLOUD PLATFORMS & INTEGRATIONS"));
            stack.Children.Add(new TextBlock
            {
                Text = "Publish deliverables to cloud coordination platforms. Supports Autodesk Construction Cloud (ACC/BIM 360), SharePoint/Teams, and local CDE folders.",
                FontSize = 10, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            });
            var cloudWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
            cloudWrap.Children.Add(MakeActionButton("ACC / BIM 360", "ACCPublish", Br(Color.FromRgb(0x00, 0x69, 0x7C)),
                "Package for Autodesk Construction Cloud / BIM 360: model, drawings, COBie, BEP"));
            cloudWrap.Children.Add(MakeActionButton("SharePoint / Teams", "SharePointExport", Br(Color.FromRgb(0x00, 0x78, 0xD4)),
                "Export to corporate SharePoint / Microsoft Teams document library"));
            cloudWrap.Children.Add(MakeActionButton("Procore", "PlatformSync", Br(Color.FromRgb(0xF4, 0x7E, 0x20)),
                "Sync deliverables with Procore construction management platform"));
            cloudWrap.Children.Add(MakeActionButton("Aconex / Oracle", "PlatformSync", Br(Color.FromRgb(0xC6, 0x28, 0x28)),
                "Sync with Oracle Aconex document management (ISO 19650 folder mapping)"));
            cloudWrap.Children.Add(MakeActionButton("Trimble Connect", "PlatformSync", Br(Color.FromRgb(0x00, 0x57, 0xA8)),
                "Sync with Trimble Connect for openBIM coordination"));
            cloudWrap.Children.Add(MakeActionButton("Bentley iTwin", "PlatformSync", Br(Color.FromRgb(0x00, 0x84, 0x53)),
                "Exchange with Bentley iTwin / ProjectWise infrastructure platform"));
            cloudWrap.Children.Add(MakeActionButton("Viewpoint 4P", "PlatformSync", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Sync with Viewpoint For Projects (4Projects) document control"));
            cloudWrap.Children.Add(MakeActionButton("Document Center", "DocumentManager", Br(CHeaderBg),
                "Open Document Management Center — folders, issues, revisions, CDE"));
            stack.Children.Add(cloudWrap);

            // ── HANDOVER & EXPORT ──
            stack.Children.Add(MakeSectionHeader("HANDOVER & BULK EXPORT"));
            stack.Children.Add(new TextBlock
            {
                Text = "ISO 19650 information exchange packages for FM handover, stage gate deliverables, and multi-format bulk export.",
                FontSize = 10, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            });
            var handoverWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
            handoverWrap.Children.Add(MakeActionButton("FM Handover", "HandoverManual", Br(CGreen),
                "Generate FM handover manual: asset register, spatial summary, system descriptions, compliance"));
            handoverWrap.Children.Add(MakeActionButton("Bulk Export", "BulkBIMExport", Br(CAccent),
                "Export all: BEP + COBie + 4D/5D + Sheet Register + Model Health"));
            handoverWrap.Children.Add(MakeActionButton("Stage Gate", "StageComplianceGate", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "RIBA stage-gated compliance check: verify tag completeness meets data drop requirements"));
            handoverWrap.Children.Add(MakeActionButton("Tag Register", "TagRegisterExport", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Export comprehensive 40+ column asset register CSV"));
            handoverWrap.Children.Add(MakeActionButton("Sheet Register", "ExportSheetRegister", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Export sheet register CSV with ISO 19650 compliance status"));
            stack.Children.Add(handoverWrap);

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

            // ── KPI Strip ──
            var kpiRow = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 12) };
            double compDelta = _data.LastComplianceAfter - _data.LastComplianceBefore;
            string deltaStr = compDelta > 0 ? $"+{compDelta:F0}%" : compDelta < 0 ? $"{compDelta:F0}%" : "—";
            kpiRow.Children.Add(MakeKPICard("TOTAL RUNS", _data.WorkflowRuns.ToString(), Br(Color.FromRgb(0x15, 0x65, 0xC0)),
                $"Workflow executions logged in STING_WORKFLOW_LOG.json"));
            kpiRow.Children.Add(MakeKPICard("LAST RUN", _data.LastWorkflow, Br(CAccent),
                $"Before: {_data.LastComplianceBefore:F0}%\nAfter: {_data.LastComplianceAfter:F0}%\nImprovement: {deltaStr}"));
            kpiRow.Children.Add(MakeKPICard("COMPLIANCE \u0394", deltaStr,
                compDelta > 0 ? Br(CGreen) : compDelta < 0 ? Br(CRed) : Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Net compliance change from last workflow execution"));
            kpiRow.Children.Add(MakeKPICard("HISTORY", _data.WorkflowHistory.Count.ToString(), Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Workflow execution records available for analysis"));
            stack.Children.Add(kpiRow);

            // ── Quick Workflow Buttons ──
            stack.Children.Add(MakeSectionHeader("QUICK WORKFLOWS"));
            stack.Children.Add(new TextBlock
            {
                Text = "One-click workflow execution — each runs a pre-defined sequence of commands with conditional step skipping.",
                FontSize = 10, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            });
            var quickWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            quickWrap.Children.Add(MakeActionButton("Morning Check", "RunWorkflow_MorningHealthCheck", Br(CHeaderBg),
                "8 steps: retag stale → warnings auto-fix → tag new → pre-tag audit → validate → template assign → tag sheets → revision check"));
            quickWrap.Children.Add(MakeActionButton("Daily QA", "RunWorkflow_DailyQASync", Br(CGreen),
                "13 adaptive steps with compliance gates — skips steps meeting thresholds"));
            quickWrap.Children.Add(MakeActionButton("Quick Fix", "RunWorkflow_QuickFixCycle", Br(CAccent),
                "6 steps: auto-fix anomalies → resolve all → retag stale → validate → completeness → register"));
            quickWrap.Children.Add(MakeActionButton("Handover", "RunWorkflow_HandoverReadiness", Br(CRed),
                "9 steps: stale → full tag → validate → template → COBie → register → BOQ → BEP → revision"));
            quickWrap.Children.Add(MakeActionButton("Weekly Drop", "RunWorkflow_WeeklyDataDrop", Br(Color.FromRgb(0x6A, 0x1B, 0x9A)),
                "8 steps: ISO 19650 information exchange — validate → export → package for CDE"));
            quickWrap.Children.Add(MakeActionButton("Post-Tag QA", "RunWorkflow_PostTaggingQA", Br(Color.FromRgb(0x00, 0x69, 0x7C)),
                "5 steps: PreTagAudit → ValidateTags → CompletenessDashboard → TagRegister → ValidateTemplate"));
            stack.Children.Add(quickWrap);

            // ── Available Preset Cards ──
            stack.Children.Add(MakeSectionHeader("ALL WORKFLOW PRESETS"));
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

                var runBtn = MakeActionButton("Run", $"RunWorkflow_{p[0].Replace(" ", "")}", Br(CHeaderBg),
                    $"Execute '{p[0]}' workflow ({p[2]} steps)\n{p[1]}");
                runBtn.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(runBtn, 1);
                cardGrid.Children.Add(runBtn);

                card.Child = cardGrid;
                card.ToolTip = $"{p[0]}\n{p[1]}\nSteps: {p[2]}";
                stack.Children.Add(card);
            }

            // ── Execution History DataGrid ──
            stack.Children.Add(new Border { Height = 12 });
            stack.Children.Add(MakeSectionHeader("EXECUTION HISTORY"));
            if (_data.WorkflowHistory.Count > 0)
            {
                var dg = new DataGrid
                {
                    AutoGenerateColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column,
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, CanUserSortColumns = true,
                    SelectionMode = DataGridSelectionMode.Single, FontSize = 11,
                    BorderBrush = Br(CBorder), BorderThickness = new Thickness(1),
                    RowHeaderWidth = 0, Margin = new Thickness(0, 0, 0, 8), MaxHeight = 200
                };
                dg.Columns.Add(new DataGridTextColumn { Header = "Time", Binding = new Binding("Timestamp"), Width = 130 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Preset", Binding = new Binding("Preset"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                dg.Columns.Add(new DataGridTextColumn { Header = "Steps", Binding = new Binding("Steps"), Width = 45 });
                dg.Columns.Add(new DataGridTextColumn { Header = "\u2714", Binding = new Binding("Passed"), Width = 30 });
                dg.Columns.Add(new DataGridTextColumn { Header = "\u2718", Binding = new Binding("Failed"), Width = 30 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Skip", Binding = new Binding("Skipped"), Width = 35 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Duration", Binding = new Binding("Duration"), Width = 65 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Before", Binding = new Binding("CompBefore") { StringFormat = "F0" }, Width = 50 });
                dg.Columns.Add(new DataGridTextColumn { Header = "After", Binding = new Binding("CompAfter") { StringFormat = "F0" }, Width = 50 });
                dg.ItemsSource = _data.WorkflowHistory;
                dg.ToolTip = "Workflow execution history loaded from STING_WORKFLOW_LOG.json";
                stack.Children.Add(dg);
            }
            else
            {
                var emptyCard = MakeCard();
                emptyCard.Child = new TextBlock
                {
                    Text = $"Total runs: {_data.WorkflowRuns}  |  Last: {_data.LastWorkflow}\n" +
                        (_data.LastComplianceBefore > 0 || _data.LastComplianceAfter > 0
                            ? $"Last run compliance: {_data.LastComplianceBefore:F0}% → {_data.LastComplianceAfter:F0}%"
                            : "No workflow execution history available yet. Run a workflow preset to begin tracking."),
                    FontSize = 12, TextWrapping = TextWrapping.Wrap
                };
                stack.Children.Add(emptyCard);
            }

            // ── Action buttons ──
            stack.Children.Add(new Border { Height = 12 });
            var actionsWrap = new WrapPanel();
            actionsWrap.Children.Add(MakeActionButton("Run Custom Preset", "RunWorkflowPreset", Br(CHeaderBg),
                "Select and run a workflow preset from the full list"));
            actionsWrap.Children.Add(MakeActionButton("Create New Preset", "CreateWorkflowPreset", Br(CGreen),
                "Create a custom workflow preset with named command steps"));
            actionsWrap.Children.Add(MakeActionButton("View Compliance Trend", "WorkflowTrend", Br(CAccent),
                "Analyse compliance improvement trend from workflow execution history"));
            actionsWrap.Children.Add(MakeActionButton("List All Presets", "ListWorkflowPresets", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Browse all available workflow presets with step details"));
            actionsWrap.Children.Add(MakeActionButton("Repeat Last", "RepeatLastWorkflow", Br(Color.FromRgb(0x6A, 0x1B, 0x9A)),
                $"Re-run last workflow: {_data.LastWorkflow}"));
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

            // ── Validation Summary with severity indicators ──
            stack.Children.Add(MakeSectionHeader("VALIDATION SUMMARY"));
            var valKpi = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 8) };
            valKpi.Children.Add(MakeKPICard("PLACEHOLDERS", _data.PlaceholderCount.ToString(),
                _data.PlaceholderCount > 0 ? Br(CAmber) : Br(CGreen),
                "Elements with generic tokens (GEN/XX/ZZ/0000)\nNeed specific values for production-ready tags"));
            valKpi.Children.Add(MakeKPICard("ANOMALIES", _data.AnomalyCount.ToString(),
                _data.AnomalyCount > 0 ? Br(CRed) : Br(CGreen),
                "Token inconsistencies: DISC/SYS mismatch, invalid PROD codes,\ncross-discipline FUNC errors, empty TAG7 narratives"));
            valKpi.Children.Add(MakeKPICard("STALE", _data.StaleCount.ToString(),
                _data.StaleCount > 0 ? Br(CRed) : Br(CGreen),
                "Elements moved/changed since last tag — tags no longer match context"));
            int totalIssues = _data.ValidationErrors.Values.Sum();
            valKpi.Children.Add(MakeKPICard("VALIDATION ERRORS", totalIssues.ToString(),
                totalIssues > 0 ? Br(CRed) : Br(CGreen),
                "ISO 19650 code validation failures:\nInvalid DISC/SYS/FUNC/PROD/LOC/ZONE codes"));
            stack.Children.Add(valKpi);

            // ── Validation Errors Breakdown ──
            if (_data.ValidationErrors.Count > 0)
            {
                stack.Children.Add(MakeSectionHeader("VALIDATION ERRORS BY TYPE"));
                var errCard = MakeCard();
                var errStack = new StackPanel();
                foreach (var kv in _data.ValidationErrors.OrderByDescending(x => x.Value))
                {
                    var errRow = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    errRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    errRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                    errRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                    errRow.Children.Add(new TextBlock { Text = kv.Key, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
                    var countTb = new TextBlock { Text = kv.Value.ToString(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Br(CRed), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(countTb, 1);
                    errRow.Children.Add(countTb);
                    // Mini bar
                    int maxCount = _data.ValidationErrors.Values.Max();
                    double barPct = maxCount > 0 ? kv.Value * 100.0 / maxCount : 0;
                    var miniBarBg = new Border { Background = Br(Color.FromRgb(0xE0, 0xE0, 0xE0)), Height = 8, CornerRadius = new CornerRadius(4) };
                    var miniBarFill = new Border { Background = Br(CRed), Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left };
                    var miniBarGrid = new Grid();
                    miniBarGrid.Children.Add(miniBarBg);
                    miniBarGrid.Children.Add(miniBarFill);
                    Grid.SetColumn(miniBarGrid, 2);
                    miniBarBg.Loaded += (s, e) => { miniBarFill.Width = Math.Max(4, miniBarBg.ActualWidth * barPct / 100.0); };
                    errRow.Children.Add(miniBarGrid);
                    errRow.ToolTip = $"{kv.Key}: {kv.Value} elements with this validation error";
                    errStack.Children.Add(errRow);
                }
                errCard.Child = errStack;
                stack.Children.Add(errCard);
            }

            // ── Cross-Token Validation ──
            if (_data.WarningsLinkedToIssues > 0 || _data.UnresolvedDependencies > 0)
            {
                stack.Children.Add(MakeSectionHeader("CROSS-SYSTEM INTEGRITY"));
                var crossCard = MakeCard();
                var crossStack = new StackPanel();
                if (_data.StaleLinkedToWarnings > 0)
                    crossStack.Children.Add(new TextBlock { Text = $"\u26A0 {_data.StaleLinkedToWarnings} stale elements also have Revit warnings", FontSize = 11, Foreground = Br(CAmber), Margin = new Thickness(0, 2, 0, 2) });
                if (_data.WarningsLinkedToIssues > 0)
                    crossStack.Children.Add(new TextBlock { Text = $"\u26A0 {_data.WarningsLinkedToIssues} critical warnings may relate to open issues", FontSize = 11, Foreground = Br(CAmber), Margin = new Thickness(0, 2, 0, 2) });
                if (_data.UnresolvedDependencies > 0)
                    crossStack.Children.Add(new TextBlock { Text = $"\u2718 {_data.UnresolvedDependencies} disciplines below 50% compliance — blocking handover", FontSize = 11, Foreground = Br(CRed), Margin = new Thickness(0, 2, 0, 2) });
                crossCard.Child = crossStack;
                stack.Children.Add(crossCard);
            }

            // ── Action buttons ──
            stack.Children.Add(new Border { Height = 12 });
            var actionsWrap = new WrapPanel();
            actionsWrap.Children.Add(MakeActionButton("Validate Tags", "ValidateTags", Br(CHeaderBg),
                "Validate all tags for ISO 19650 compliance: code validation, format checks, completeness"));
            actionsWrap.Children.Add(MakeActionButton("Pre-Tag Audit", "PreTagAudit", Br(CGreen),
                "Dry-run audit: predict tags, collisions, ISO violations BEFORE committing any changes"));
            actionsWrap.Children.Add(MakeActionButton("Auto-Fix Anomalies", "AnomalyAutoFix", Br(CAccent),
                "Auto-fix: DISC/SYS/FUNC/PROD derivation, TAG7 rebuild, stale flag clear"));
            actionsWrap.Children.Add(MakeActionButton("Resolve All Issues", "ResolveAllIssues", Br(CRed),
                "One-click ISO 19650 compliance resolution — batched 500 elements at a time"));
            actionsWrap.Children.Add(MakeActionButton("Tag Register Export", "TagRegisterExport", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Export comprehensive 40+ column asset register CSV"));
            actionsWrap.Children.Add(MakeActionButton("Completeness Dashboard", "CompletenessDashboard", Br(Color.FromRgb(0x6A, 0x1B, 0x9A)),
                "Per-discipline compliance dashboard with percentage breakdown"));
            actionsWrap.Children.Add(MakeActionButton("Schema Validate", "SchemaValidate", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Validate material CSV columns match MATERIAL_SCHEMA.json (77-column schema)"));
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

            // ── KPI cards row 1: Core metrics ──
            var kpiGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 8) };
            kpiGrid.Children.Add(MakeKPICard("TASKS", _data.ScheduledTasks.ToString(), Br(Color.FromRgb(0x15, 0x65, 0xC0)),
                "Scheduled construction tasks\nfrom 4D timeline\n\nDouble-click to run Auto Schedule", "AutoSchedule4D"));
            kpiGrid.Children.Add(MakeKPICard("EST. COST", _data.TotalCostEstimate > 0 ? $"£{_data.TotalCostEstimate:N0}" : "N/A",
                Br(Color.FromRgb(0x2E, 0x7D, 0x32)), "5D cost estimate from cost rate model\n\nDouble-click to run Auto Cost", "AutoCost5D"));
            kpiGrid.Children.Add(MakeKPICard("MILESTONES", $"{_data.MilestonesComplete}/{_data.MilestonesTotal}",
                _data.MilestonesTotal > 0 && _data.MilestonesComplete == _data.MilestonesTotal ? Br(CGreen) : Br(CAmber),
                "Construction milestones completed vs total\n\nDouble-click to open Milestone Register", "MilestoneRegister"));
            kpiGrid.Children.Add(MakeKPICard("EARNED VALUE", _data.EarnedValuePct > 0 ? $"{_data.EarnedValuePct:F0}%" : "N/A",
                _data.EarnedValuePct >= 80 ? Br(CGreen) : _data.EarnedValuePct >= 50 ? Br(CAmber) : Br(CRed),
                $"Earned value (EV) percentage\nPlanned value (PV): {_data.PlannedValuePct:F0}%\n" +
                $"CPI: {_data.CostPerformanceIndex:F2} | SPI: {_data.SchedulePerformanceIndex:F2}\n\nDouble-click for Cost Report", "CostReport5D"));
            stack.Children.Add(kpiGrid);

            // ── EVM Performance Indicators ──
            if (_data.EarnedValuePct > 0 || _data.PlannedValuePct > 0)
            {
                stack.Children.Add(MakeSectionHeader("EARNED VALUE MANAGEMENT (EVM)"));
                var evmCard = MakeCard();
                var evmGrid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                evmGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                evmGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                evmGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                evmGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Planned vs Earned bars
                var pvStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                pvStack.Children.Add(new TextBlock { Text = "Planned Value", FontSize = 10, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center });
                pvStack.Children.Add(new TextBlock { Text = $"{_data.PlannedValuePct:F0}%", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Br(Color.FromRgb(0x15, 0x65, 0xC0)), HorizontalAlignment = HorizontalAlignment.Center });
                evmGrid.Children.Add(pvStack);

                var evStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                evStack.Children.Add(new TextBlock { Text = "Earned Value", FontSize = 10, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center });
                evStack.Children.Add(new TextBlock { Text = $"{_data.EarnedValuePct:F0}%", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Br(CGreen), HorizontalAlignment = HorizontalAlignment.Center });
                Grid.SetColumn(evStack, 1);
                evmGrid.Children.Add(evStack);

                // CPI indicator
                var cpiColor = _data.CostPerformanceIndex >= 1.0 ? CGreen : _data.CostPerformanceIndex >= 0.9 ? CAmber : CRed;
                var cpiStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                cpiStack.Children.Add(new TextBlock { Text = "Cost Perf. Index", FontSize = 10, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center });
                cpiStack.Children.Add(new TextBlock { Text = $"{_data.CostPerformanceIndex:F2}", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Br(cpiColor), HorizontalAlignment = HorizontalAlignment.Center });
                cpiStack.Children.Add(new TextBlock { Text = _data.CostPerformanceIndex >= 1.0 ? "Under budget" : "Over budget", FontSize = 9, Foreground = Br(cpiColor), HorizontalAlignment = HorizontalAlignment.Center });
                Grid.SetColumn(cpiStack, 2);
                evmGrid.Children.Add(cpiStack);

                // SPI indicator
                var spiColor = _data.SchedulePerformanceIndex >= 1.0 ? CGreen : _data.SchedulePerformanceIndex >= 0.9 ? CAmber : CRed;
                var spiStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                spiStack.Children.Add(new TextBlock { Text = "Schedule Perf. Index", FontSize = 10, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center });
                spiStack.Children.Add(new TextBlock { Text = $"{_data.SchedulePerformanceIndex:F2}", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Br(spiColor), HorizontalAlignment = HorizontalAlignment.Center });
                spiStack.Children.Add(new TextBlock { Text = _data.SchedulePerformanceIndex >= 1.0 ? "Ahead of schedule" : "Behind schedule", FontSize = 9, Foreground = Br(spiColor), HorizontalAlignment = HorizontalAlignment.Center });
                Grid.SetColumn(spiStack, 3);
                evmGrid.Children.Add(spiStack);

                evmCard.Child = evmGrid;
                stack.Children.Add(evmCard);

                // Variance summary
                var varianceRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
                varianceRow.Children.Add(MakeMetricChip($"Cost Variance: {_data.CostVariancePct:+0.0;-0.0;0.0}%",
                    _data.CostVariancePct >= 0 ? Br(CGreen) : Br(CRed)));
                varianceRow.Children.Add(MakeMetricChip($"Schedule Variance: {_data.ScheduleVariancePct:+0.0;-0.0;0.0}%",
                    _data.ScheduleVariancePct >= 0 ? Br(CGreen) : Br(CRed)));
                stack.Children.Add(varianceRow);
            }

            // ── Milestone Timeline ──
            if (_data.Milestones.Count > 0)
            {
                stack.Children.Add(MakeSectionHeader("MILESTONE TIMELINE"));
                var msCard = MakeCard();
                var msStack = new StackPanel();
                foreach (var ms in _data.Milestones)
                {
                    var msRow = new Grid { Margin = new Thickness(0, 3, 0, 3), Height = 28 };
                    msRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                    msRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
                    msRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                    msRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    msRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });

                    // Status icon
                    var icon = new TextBlock
                    {
                        Text = ms.IsComplete ? "\u2714" : ms.ActualPct >= ms.PlannedPct ? "\u25CF" : "\u26A0",
                        FontSize = 14, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = ms.IsComplete ? Br(CGreen) : ms.ActualPct >= ms.PlannedPct ? Br(CAccent) : Br(CAmber)
                    };
                    msRow.Children.Add(icon);

                    // Name
                    var nameTb = new TextBlock
                    {
                        Text = ms.Name, FontSize = 11, FontWeight = ms.IsComplete ? FontWeights.Normal : FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextDecorations = ms.IsComplete ? TextDecorations.Strikethrough : null,
                        Foreground = ms.IsComplete ? Brushes.Gray : Brushes.Black
                    };
                    Grid.SetColumn(nameTb, 1);
                    msRow.Children.Add(nameTb);

                    // Phase chip
                    var phaseChip = new Border
                    {
                        Background = Br(Color.FromRgb(0xE3, 0xF2, 0xFD)), CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(6, 2, 6, 2), VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    phaseChip.Child = new TextBlock { Text = ms.Phase, FontSize = 9, Foreground = Br(CHeaderBg) };
                    Grid.SetColumn(phaseChip, 2);
                    msRow.Children.Add(phaseChip);

                    // Gantt-style progress bar (planned in blue outline, actual in orange fill)
                    var barGrid = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
                    var barBg = new Border { Background = Br(Color.FromRgb(0xE8, 0xEA, 0xED)), Height = 12, CornerRadius = new CornerRadius(6) };
                    barGrid.Children.Add(barBg);
                    // Planned extent (blue border)
                    var plannedBar = new Border
                    {
                        BorderBrush = Br(Color.FromRgb(0x15, 0x65, 0xC0)), BorderThickness = new Thickness(1.5),
                        Height = 12, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left,
                        Background = Brushes.Transparent
                    };
                    barGrid.Children.Add(plannedBar);
                    // Actual fill (orange/green)
                    var actualBar = new Border
                    {
                        Background = ms.IsComplete ? Br(CGreen) : Br(CAccent),
                        Height = 12, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left,
                        Opacity = 0.8
                    };
                    barGrid.Children.Add(actualBar);
                    barBg.Loaded += (s, e) =>
                    {
                        double w = barBg.ActualWidth;
                        plannedBar.Width = Math.Max(4, w * ms.PlannedPct / 100.0);
                        actualBar.Width = Math.Max(2, w * ms.ActualPct / 100.0);
                    };
                    Grid.SetColumn(barGrid, 3);
                    msRow.Children.Add(barGrid);

                    // Percentage
                    var pctTb = new TextBlock
                    {
                        Text = $"{ms.ActualPct:F0}%", FontSize = 11, FontWeight = FontWeights.Bold,
                        Foreground = ms.IsComplete ? Br(CGreen) : ms.ActualPct >= ms.PlannedPct ? Br(CGreen) : Br(CRed),
                        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right
                    };
                    Grid.SetColumn(pctTb, 4);
                    msRow.Children.Add(pctTb);

                    msRow.ToolTip = $"{ms.Name}\nPhase: {ms.Phase}\nPlanned: {ms.PlannedPct:F0}% | Actual: {ms.ActualPct:F0}%\nStatus: {(ms.IsComplete ? "COMPLETE" : ms.ActualPct >= ms.PlannedPct ? "On Track" : "Behind")}";
                    msStack.Children.Add(msRow);
                }
                // Legend
                var legendRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                legendRow.Children.Add(new Border { Background = Br(CAccent), Width = 12, Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
                legendRow.Children.Add(new TextBlock { Text = "Actual", FontSize = 9, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 12, 0) });
                legendRow.Children.Add(new Border { BorderBrush = Br(Color.FromRgb(0x15, 0x65, 0xC0)), BorderThickness = new Thickness(1.5), Width = 12, Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
                legendRow.Children.Add(new TextBlock { Text = "Planned", FontSize = 9, Foreground = Brushes.Gray });
                msStack.Children.Add(legendRow);
                msCard.Child = msStack;
                stack.Children.Add(msCard);
            }

            // ── Cost by Phase (Gantt-style) ──
            if (_data.CostByPhase.Count > 0)
            {
                stack.Children.Add(MakeSectionHeader("COST BY PHASE"));
                var costCard = MakeCard();
                var costStack = new StackPanel();

                // Header row
                var headerRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
                headerRow.Children.Add(new TextBlock { Text = "Phase", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.Gray });
                var costHdr = new TextBlock { Text = "Cost", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.Gray };
                Grid.SetColumn(costHdr, 1); headerRow.Children.Add(costHdr);
                var barHdr = new TextBlock { Text = "Progress", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.Gray };
                Grid.SetColumn(barHdr, 2); headerRow.Children.Add(barHdr);
                costStack.Children.Add(headerRow);

                double maxCost = _data.CostByPhase.Max(c => c.Cost);
                foreach (var (phase, cost, progress) in _data.CostByPhase)
                {
                    var phaseRow = new Grid { Margin = new Thickness(0, 2, 0, 2), Height = 26 };
                    phaseRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                    phaseRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                    phaseRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    phaseRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });

                    phaseRow.Children.Add(new TextBlock { Text = phase, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                    var costTb = new TextBlock { Text = $"£{cost:N0}", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(costTb, 1); phaseRow.Children.Add(costTb);

                    // Stacked bar: cost proportion in grey, progress fill in accent color
                    var barContainer = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
                    var barBg = new Border { Background = Br(Color.FromRgb(0xE8, 0xEA, 0xED)), Height = 14, CornerRadius = new CornerRadius(7) };
                    barContainer.Children.Add(barBg);
                    var barFill = new Border
                    {
                        Background = progress >= 80 ? Br(CGreen) : progress >= 50 ? Br(CAccent) : Br(Color.FromRgb(0x15, 0x65, 0xC0)),
                        Height = 14, CornerRadius = new CornerRadius(7), HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.85
                    };
                    barContainer.Children.Add(barFill);
                    barBg.Loaded += (s, e) => { barFill.Width = Math.Max(4, barBg.ActualWidth * progress / 100.0); };
                    Grid.SetColumn(barContainer, 2); phaseRow.Children.Add(barContainer);

                    var pctTb = new TextBlock
                    {
                        Text = $"{progress:F0}%", FontSize = 11, FontWeight = FontWeights.Bold,
                        Foreground = progress >= 80 ? Br(CGreen) : progress >= 50 ? Br(CAmber) : Br(CRed),
                        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right
                    };
                    Grid.SetColumn(pctTb, 3); phaseRow.Children.Add(pctTb);

                    phaseRow.ToolTip = $"{phase}\nCost: £{cost:N0} ({(maxCost > 0 ? cost / maxCost * 100 : 0):F0}% of largest phase)\nProgress: {progress:F0}%";
                    costStack.Children.Add(phaseRow);
                }

                // Total row
                double totalCost = _data.CostByPhase.Sum(c => c.Cost);
                double avgProgress = _data.CostByPhase.Count > 0 ? _data.CostByPhase.Average(c => c.Progress) : 0;
                var totalRow = new Grid { Margin = new Thickness(0, 6, 0, 0), Height = 26 };
                totalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                totalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                totalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                totalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
                totalRow.Children.Add(new TextBlock { Text = "TOTAL", FontSize = 11, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
                var totalCostTb = new TextBlock { Text = $"£{totalCost:N0}", FontSize = 11, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(totalCostTb, 1); totalRow.Children.Add(totalCostTb);
                var avgTb = new TextBlock { Text = $"{avgProgress:F0}%", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Br(CAccent), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetColumn(avgTb, 3); totalRow.Children.Add(avgTb);
                costStack.Children.Add(new Border { BorderBrush = Br(CBorder), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 4, 0, 0) });
                costStack.Children.Add(totalRow);

                costCard.Child = costStack;
                stack.Children.Add(costCard);
            }

            // ── Cash Flow Forecast ──
            if (_data.CashFlowForecast.Count > 0)
            {
                stack.Children.Add(MakeSectionHeader("CASH FLOW FORECAST"));
                var cfCard = MakeCard();
                var cfStack = new StackPanel();
                double cfMax = Math.Max(1, _data.CashFlowForecast.Max(c => Math.Max(c.Planned, c.Actual)));
                foreach (var (month, planned, actual) in _data.CashFlowForecast)
                {
                    var cfRow = new Grid { Margin = new Thickness(0, 2, 0, 2), Height = 24 };
                    cfRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                    cfRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    cfRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                    cfRow.Children.Add(new TextBlock { Text = month, FontSize = 10, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });

                    // Dual bar: planned (outline) vs actual (fill)
                    var dualBar = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
                    var bgBar = new Border { Background = Br(Color.FromRgb(0xE8, 0xEA, 0xED)), Height = 10, CornerRadius = new CornerRadius(5) };
                    dualBar.Children.Add(bgBar);
                    var plannedBarCf = new Border { BorderBrush = Br(Color.FromRgb(0x15, 0x65, 0xC0)), BorderThickness = new Thickness(1.5), Height = 10, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left };
                    dualBar.Children.Add(plannedBarCf);
                    var actualBarCf = new Border { Background = Br(CAccent), Height = 10, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.8 };
                    dualBar.Children.Add(actualBarCf);
                    bgBar.Loaded += (s, e) =>
                    {
                        double w = bgBar.ActualWidth;
                        plannedBarCf.Width = Math.Max(4, w * planned / cfMax);
                        actualBarCf.Width = Math.Max(2, w * actual / cfMax);
                    };
                    Grid.SetColumn(dualBar, 1); cfRow.Children.Add(dualBar);

                    var cfVals = new TextBlock { Text = $"P: £{planned:N0} | A: £{actual:N0}", FontSize = 9, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(cfVals, 2); cfRow.Children.Add(cfVals);

                    cfRow.ToolTip = $"{month}\nPlanned: £{planned:N0}\nActual: £{actual:N0}\nVariance: £{actual - planned:+N0;-N0;0}";
                    cfStack.Children.Add(cfRow);
                }
                cfCard.Child = cfStack;
                stack.Children.Add(cfCard);
            }

            // ── Actions ──
            stack.Children.Add(new Border { Height = 12 });
            stack.Children.Add(MakeSectionHeader("SCHEDULING OPERATIONS"));
            var actionsWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            actionsWrap.Children.Add(MakeActionButton("Auto Schedule 4D", "AutoSchedule4D", Br(CHeaderBg),
                "Generate 4D timeline from model phases, levels, and trade sequence (32 trades)"));
            actionsWrap.Children.Add(MakeActionButton("Auto Cost 5D", "AutoCost5D", Br(CGreen),
                "Calculate 5D cost estimate using cost_rates_5d.csv rate model"));
            actionsWrap.Children.Add(MakeActionButton("View Timeline", "ViewTimeline4D", Br(CAccent),
                "Display interactive Gantt timeline with phase/trade breakdown"));
            actionsWrap.Children.Add(MakeActionButton("Cost Report", "CostReport5D", Br(Color.FromRgb(0x6A, 0x1B, 0x9A)),
                "Detailed 5D cost report: by category, discipline, phase with subtotals"));
            actionsWrap.Children.Add(MakeActionButton("Cash Flow", "CashFlow5D", Br(Color.FromRgb(0x00, 0x69, 0x7C)),
                "S-curve cash flow forecast with monthly planned vs actual spend"));
            actionsWrap.Children.Add(MakeActionButton("Export Schedule", "ExportSchedule4D", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Export 4D schedule to CSV for Navisworks TimeLiner / Synchro import"));
            actionsWrap.Children.Add(MakeActionButton("Import MS Project", "ImportMSProject", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Import tasks from Microsoft Project XML/CSV for 4D integration"));
            actionsWrap.Children.Add(MakeActionButton("Milestone Register", "MilestoneRegister", Br(CAccent),
                "View/manage construction milestones with completion tracking"));
            actionsWrap.Children.Add(MakeActionButton("Phase Summary", "PhaseSummary", Br(CHeaderBg),
                "Phase-by-phase summary: element counts, completion status, duration"));
            actionsWrap.Children.Add(MakeActionButton("Working Calendar", "WorkingCalendar", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Configure working days, holidays, and shift patterns for scheduling"));
            actionsWrap.Children.Add(MakeActionButton("Navisworks Export", "NavisworksTimeLiner", Br(Color.FromRgb(0x00, 0x69, 0x7C)),
                "Export Navisworks TimeLiner CSV with element-to-task mapping"));
            actionsWrap.Children.Add(MakeActionButton("Element Cost Trace", "ElementCostTrace", Br(Color.FromRgb(0x6A, 0x1B, 0x9A)),
                "Trace cost allocation per element: material + labour + plant rates"));
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
                    if (e.ClickCount == 2) { DispatchAction(clickAction); }
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

        private Button MakeActionButton(string label, string actionTag, SolidColorBrush bg, string tooltip = null)
        {
            var btn = new Button
            {
                Content = label, Tag = actionTag,
                Height = 30, Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 6, 6),
                Background = bg, Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 11, Cursor = Cursors.Hand,
                ToolTip = tooltip ?? GetActionTooltip(actionTag)
            };
            btn.Click += ActionBtn_Click;
            // Phase 48: Hover effect — lighten on enter, restore on leave
            var origBg = bg;
            // BCC-MEDIUM-02: Pre-compute hover brush once per button instead of on every MouseEnter
            var c0 = origBg.Color;
            var hoverBrush = Br(Color.FromRgb(
                (byte)Math.Min(255, c0.R + 30),
                (byte)Math.Min(255, c0.G + 30),
                (byte)Math.Min(255, c0.B + 30)));
            btn.MouseEnter += (s, e) => { btn.Background = hoverBrush; };
            btn.MouseLeave += (s, e) => { btn.Background = origBg; };
            return btn;
        }

        private void ActionBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string action)
            {
                DispatchAction(action);
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

        // BCC-HIGH-02: Return the TextBlock so callers can reference it directly
        // instead of doing Cast<UIElement>().LastOrDefault() over all children.
        private TextBlock AddCellToGrid(Grid grid, string text, int row, int col, bool isHeader = false,
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
            return tb;
        }

        // ════════════════════════════════════════════════════════════════
        //  DELIVERABLES TAB (Phase 49)
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildDeliverablesTab()
        {
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var root = new StackPanel { Margin = new Thickness(16) };
            sv.Content = root;

            // ── KPI CARDS ──────────────────────────────────────────────
            var kpiGrid = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 12) };
            int rejected = _data.Deliverables.Count(d => d.Status == "Rejected");
            int inProgress = _data.Deliverables.Count(d => d.Status == "In Progress");
            kpiGrid.Children.Add(MakeKPICard("TOTAL", _data.Deliverables.Count.ToString(), Br(CHeaderBg),
                "Total deliverables tracked across all data drops", "DataDropReadiness"));
            kpiGrid.Children.Add(MakeKPICard("PENDING", _data.DeliverablesPending.ToString(),
                _data.DeliverablesPending > 0 ? Br(CAmber) : Br(CGreen),
                $"{_data.DeliverablesPending} deliverables awaiting action"));
            kpiGrid.Children.Add(MakeKPICard("IN PROGRESS", inProgress.ToString(), Br(Color.FromRgb(0x15, 0x65, 0xC0)),
                $"{inProgress} deliverables currently being worked on"));
            kpiGrid.Children.Add(MakeKPICard("SUBMITTED", _data.DeliverablesSubmitted.ToString(), Br(Color.FromRgb(0x00, 0x97, 0xA7)),
                $"{_data.DeliverablesSubmitted} deliverables submitted for review", "DocumentRegister"));
            kpiGrid.Children.Add(MakeKPICard("APPROVED", _data.DeliverablesApproved.ToString(), Br(CGreen),
                $"{_data.DeliverablesApproved} deliverables approved and accepted"));
            kpiGrid.Children.Add(MakeKPICard("OVERDUE", _data.DeliverablesOverdue.ToString(),
                _data.DeliverablesOverdue > 0 ? Br(CRed) : Br(CGreen),
                _data.DeliverablesOverdue > 0 ? $"{_data.DeliverablesOverdue} deliverables past due date — immediate action required" : "No overdue deliverables"));
            root.Children.Add(kpiGrid);

            // ── ISO 19650 DATA DROP PROGRESS ───────────────────────────
            root.Children.Add(MakeSectionHeader("ISO 19650 DATA DROP PROGRESS"));
            var ddGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 4, 0, 12) };
            string[] ddNames = { "DD1", "DD2", "DD3", "DD4" };
            string[] ddLabels = { "Brief & BEP", "Concept Design", "Developed Design", "Production & Handover" };
            string[] ddThresholds = { "30%", "60%", "85%", "95%" };
            for (int i = 0; i < ddNames.Length; i++)
            {
                double ddPct = _data.DataDropProgress.TryGetValue(ddNames[i], out double dp) ? dp : 0;
                bool isCurrent = _data.CurrentDataDrop == ddNames[i];
                bool isPast = Array.IndexOf(ddNames, _data.CurrentDataDrop) > i;
                var ddCard = new Border
                {
                    Background = isCurrent ? Br(Color.FromRgb(0xE3, 0xF2, 0xFD)) : isPast ? Br(Color.FromRgb(0xE8, 0xF5, 0xE9)) : Br(CCardBg),
                    BorderBrush = isCurrent ? Br(CAccent) : isPast ? Br(CGreen) : Br(CBorder),
                    BorderThickness = new Thickness(isCurrent ? 2 : 1),
                    CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(3),
                    Cursor = Cursors.Hand,
                    ToolTip = $"{ddNames[i]}: {ddLabels[i]}\nTarget: {ddThresholds[i]} tag compliance\nCurrent: {ddPct:F0}%\n{(isCurrent ? "◆ Active data drop" : isPast ? "✓ Completed" : "○ Upcoming")}"
                };
                var ddStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                ddStack.Children.Add(new TextBlock { Text = ddNames[i], FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Br(CHeaderBg), HorizontalAlignment = HorizontalAlignment.Center });
                ddStack.Children.Add(new TextBlock { Text = ddLabels[i], FontSize = 9, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)), HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap });
                // RAG progress bar for this drop
                var barBg = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Br(Color.FromRgb(0xE0, 0xE0, 0xE0)), Margin = new Thickness(4, 6, 4, 2) };
                var barFill = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = RagBrush(ddPct >= 80 ? "GREEN" : ddPct >= 50 ? "AMBER" : "RED"), HorizontalAlignment = HorizontalAlignment.Left };
                barBg.Child = barFill;
                barBg.Loaded += (s, e) => { barFill.Width = Math.Max(0, barBg.ActualWidth * ddPct / 100.0); };
                ddStack.Children.Add(barBg);
                ddStack.Children.Add(new TextBlock { Text = $"{ddPct:F0}%  (target {ddThresholds[i]})", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = RagBrush(ddPct >= 80 ? "GREEN" : ddPct >= 50 ? "AMBER" : "RED"), HorizontalAlignment = HorizontalAlignment.Center });
                if (isCurrent)
                    ddStack.Children.Add(new TextBlock { Text = "◆ CURRENT", FontSize = 8, Foreground = Br(CAccent), FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });
                else if (isPast)
                    ddStack.Children.Add(new TextBlock { Text = "✓ COMPLETE", FontSize = 8, Foreground = Br(CGreen), FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });
                // Per-drop deliverable count
                int dropCount = _data.Deliverables.Count(d => d.DataDrop == ddNames[i]);
                int dropApproved = _data.Deliverables.Count(d => d.DataDrop == ddNames[i] && d.Status == "Approved");
                if (dropCount > 0)
                    ddStack.Children.Add(new TextBlock { Text = $"{dropApproved}/{dropCount} deliverables", FontSize = 8, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)), HorizontalAlignment = HorizontalAlignment.Center });
                ddCard.Child = ddStack;
                // Double-click to filter DataGrid to this drop
                string ddName = ddNames[i];
                ddCard.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { DispatchAction("DataDropReadiness"); } };
                ddGrid.Children.Add(ddCard);
            }
            root.Children.Add(ddGrid);

            // ── CDE STATE LIFECYCLE ────────────────────────────────────
            root.Children.Add(MakeSectionHeader("CDE STATE LIFECYCLE — ISO 19650-2"));
            var cdePanel = new StackPanel { Margin = new Thickness(0, 4, 0, 12) };
            // CDE flow diagram: WIP → SHARED → PUBLISHED → ARCHIVE
            var cdeFlow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
            string[] cdeStates = { "WIP", "SHARED", "PUBLISHED", "ARCHIVE" };
            string[] cdeSuit = { "S0-S2", "S3", "S4-S6", "S7" };
            string[] cdeDesc = { "Work In Progress\nInternal development", "Shared for\ncoordination review", "Published for\napproval & use", "Archived for\nrecord keeping" };
            Color[] cdeColors = { Color.FromRgb(0xFF, 0xB3, 0x00), Color.FromRgb(0x15, 0x65, 0xC0), CGreen, Color.FromRgb(0x75, 0x75, 0x75) };
            for (int i = 0; i < cdeStates.Length; i++)
            {
                int stateCount = _data.Deliverables.Count(d => d.CDE == cdeStates[i]);
                var stateCard = new Border
                {
                    Background = Br(CCardBg), BorderBrush = Br(cdeColors[i]), BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), MinWidth = 130
                };
                var stateStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                stateStack.Children.Add(new TextBlock { Text = cdeStates[i], FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Br(cdeColors[i]), HorizontalAlignment = HorizontalAlignment.Center });
                stateStack.Children.Add(new TextBlock { Text = cdeDesc[i], FontSize = 9, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center });
                stateStack.Children.Add(new TextBlock { Text = $"Suitability: {cdeSuit[i]}", FontSize = 8, Foreground = Br(Color.FromRgb(0x9E, 0x9E, 0x9E)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
                stateStack.Children.Add(new TextBlock { Text = $"{stateCount} documents", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Br(CHeaderBg), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
                stateCard.Child = stateStack;
                cdeFlow.Children.Add(stateCard);
                // Arrow between states
                if (i < cdeStates.Length - 1)
                    cdeFlow.Children.Add(new TextBlock { Text = "→", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Br(Color.FromRgb(0xBD, 0xBD, 0xBD)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
            }
            cdePanel.Children.Add(cdeFlow);
            root.Children.Add(cdePanel);

            // ── SUITABILITY CODE LEGEND ─────────────────────────────────
            root.Children.Add(MakeSectionHeader("SUITABILITY CODES — ISO 19650-2"));
            var suitGrid = new Grid { Margin = new Thickness(0, 4, 0, 12) };
            suitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            suitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            string[,] suitCodes = {
                { "S0", "WIP — Suitability not assessed" },
                { "S1", "Fit for coordination" },
                { "S2", "Fit for information" },
                { "S3", "Fit for review & comment" },
                { "S4", "Fit for stage approval" },
                { "S5", "Fit for manufacture/procurement" },
                { "S6", "Fit for PIM authorisation" },
                { "S7", "Fit for AIM (archive)" }
            };
            var suitLeft = new StackPanel();
            var suitRight = new StackPanel();
            for (int i = 0; i < 8; i++)
            {
                int codeCount = _data.Deliverables.Count(d => d.Suitability == suitCodes[i, 0]);
                var suitRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                suitRow.Children.Add(new Border
                {
                    Background = Br(codeCount > 0 ? CAccent : Color.FromRgb(0xBD, 0xBD, 0xBD)),
                    CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 6, 0),
                    Child = new TextBlock { Text = suitCodes[i, 0], FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.White }
                });
                suitRow.Children.Add(new TextBlock { Text = suitCodes[i, 1], FontSize = 10, Foreground = Br(Color.FromRgb(0x42, 0x42, 0x42)), VerticalAlignment = VerticalAlignment.Center });
                if (codeCount > 0)
                    suitRow.Children.Add(new TextBlock { Text = $" ({codeCount})", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Br(CAccent), VerticalAlignment = VerticalAlignment.Center });
                if (i < 4) suitLeft.Children.Add(suitRow); else suitRight.Children.Add(suitRow);
            }
            Grid.SetColumn(suitLeft, 0); Grid.SetColumn(suitRight, 1);
            suitGrid.Children.Add(suitLeft); suitGrid.Children.Add(suitRight);
            root.Children.Add(suitGrid);

            // ── PER-DROP DELIVERABLE BREAKDOWN ──────────────────────────
            root.Children.Add(MakeSectionHeader("DELIVERABLES BY DATA DROP"));
            var breakdownGrid = new Grid { Margin = new Thickness(0, 4, 0, 12) };
            breakdownGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });  // DD label
            breakdownGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });  // count
            breakdownGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // bar
            breakdownGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) });  // % text
            for (int i = 0; i < ddNames.Length; i++)
            {
                breakdownGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var items = _data.Deliverables.Where(d => d.DataDrop == ddNames[i]).ToList();
                int approved = items.Count(d => d.Status == "Approved");
                double pct = items.Count > 0 ? (double)approved / items.Count * 100 : 0;
                var ddLabel = new TextBlock { Text = ddNames[i], FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Br(CHeaderBg), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
                var countLabel = new TextBlock { Text = $"{approved}/{items.Count}", FontSize = 10, Foreground = Br(Color.FromRgb(0x61, 0x61, 0x61)), VerticalAlignment = VerticalAlignment.Center };
                var barBg2 = new Border { Height = 14, CornerRadius = new CornerRadius(3), Background = Br(Color.FromRgb(0xE8, 0xEA, 0xED)), Margin = new Thickness(6, 2, 6, 2) };
                var barFill2 = new Border { Height = 14, CornerRadius = new CornerRadius(3), Background = RagBrush(pct >= 80 ? "GREEN" : pct >= 50 ? "AMBER" : "RED"), HorizontalAlignment = HorizontalAlignment.Left };
                barBg2.Child = barFill2;
                double capturedPct = pct;
                barBg2.Loaded += (s, e) => { barFill2.Width = Math.Max(0, barBg2.ActualWidth * capturedPct / 100.0); };
                var pctLabel = new TextBlock { Text = $"{pct:F0}%", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = RagBrush(pct >= 80 ? "GREEN" : pct >= 50 ? "AMBER" : "RED"), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(ddLabel, i); Grid.SetColumn(ddLabel, 0);
                Grid.SetRow(countLabel, i); Grid.SetColumn(countLabel, 1);
                Grid.SetRow(barBg2, i); Grid.SetColumn(barBg2, 2);
                Grid.SetRow(pctLabel, i); Grid.SetColumn(pctLabel, 3);
                breakdownGrid.Children.Add(ddLabel); breakdownGrid.Children.Add(countLabel);
                breakdownGrid.Children.Add(barBg2); breakdownGrid.Children.Add(pctLabel);
            }
            root.Children.Add(breakdownGrid);

            // ── BY TYPE DISTRIBUTION ────────────────────────────────────
            if (_data.Deliverables.Count > 0)
            {
                root.Children.Add(MakeSectionHeader("DELIVERABLES BY TYPE"));
                var typeGrid = new Grid { Margin = new Thickness(0, 4, 0, 12) };
                typeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                typeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                typeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var typeGroups = _data.Deliverables.GroupBy(d => d.Type ?? "Unknown").OrderByDescending(g => g.Count()).ToList();
                int maxTypeCount = typeGroups.Count > 0 ? typeGroups[0].Count() : 1;
                for (int i = 0; i < typeGroups.Count && i < 8; i++)
                {
                    typeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var tg = typeGroups[i];
                    Color typeColor = tg.Key == "Model" ? Color.FromRgb(0x15, 0x65, 0xC0) :
                        tg.Key == "Drawing" ? CAccent : tg.Key == "Schedule" ? Color.FromRgb(0x6A, 0x1B, 0x9A) :
                        tg.Key == "Report" ? CGreen : Color.FromRgb(0x45, 0x50, 0x6E);
                    var typeLabel = new TextBlock { Text = tg.Key, FontSize = 10, Foreground = Br(Color.FromRgb(0x42, 0x42, 0x42)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
                    var typeCount = new TextBlock { Text = tg.Count().ToString(), FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Br(typeColor), VerticalAlignment = VerticalAlignment.Center };
                    var typeBarBg = new Border { Height = 12, CornerRadius = new CornerRadius(3), Background = Br(Color.FromRgb(0xE8, 0xEA, 0xED)), Margin = new Thickness(6, 2, 6, 2) };
                    var typeBarFill = new Border { Height = 12, CornerRadius = new CornerRadius(3), Background = Br(typeColor), HorizontalAlignment = HorizontalAlignment.Left };
                    typeBarBg.Child = typeBarFill;
                    int captured = tg.Count(); int capturedMax = maxTypeCount;
                    typeBarBg.Loaded += (s, e) => { typeBarFill.Width = Math.Max(0, typeBarBg.ActualWidth * captured / Math.Max(1, capturedMax)); };
                    Grid.SetRow(typeLabel, i); Grid.SetColumn(typeLabel, 0);
                    Grid.SetRow(typeCount, i); Grid.SetColumn(typeCount, 1);
                    Grid.SetRow(typeBarBg, i); Grid.SetColumn(typeBarBg, 2);
                    typeGrid.Children.Add(typeLabel); typeGrid.Children.Add(typeCount); typeGrid.Children.Add(typeBarBg);
                }
                root.Children.Add(typeGrid);
            }

            // ── DELIVERABLES DATA GRID ──────────────────────────────────
            root.Children.Add(MakeSectionHeader("DELIVERABLE REGISTER"));
            if (_data.Deliverables.Count > 0)
            {
                // Filter bar
                var filterBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
                var filterDrop = new ComboBox { Width = 80, Height = 26, FontSize = 10, ToolTip = "Filter by data drop" };
                filterDrop.Items.Add("All Drops");
                foreach (string dd in ddNames) filterDrop.Items.Add(dd);
                filterDrop.SelectedIndex = 0;
                filterBar.Children.Add(new TextBlock { Text = "Drop: ", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
                filterBar.Children.Add(filterDrop);
                var filterStatus = new ComboBox { Width = 100, Height = 26, FontSize = 10, Margin = new Thickness(12, 0, 0, 0), ToolTip = "Filter by status" };
                filterStatus.Items.Add("All Status");
                foreach (string st in new[] { "Pending", "In Progress", "Submitted", "Approved", "Rejected" }) filterStatus.Items.Add(st);
                filterStatus.SelectedIndex = 0;
                filterBar.Children.Add(new TextBlock { Text = "  Status: ", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
                filterBar.Children.Add(filterStatus);
                var filterCDE = new ComboBox { Width = 90, Height = 26, FontSize = 10, Margin = new Thickness(12, 0, 0, 0), ToolTip = "Filter by CDE state" };
                filterCDE.Items.Add("All CDE");
                foreach (string c in cdeStates) filterCDE.Items.Add(c);
                filterCDE.SelectedIndex = 0;
                filterBar.Children.Add(new TextBlock { Text = "  CDE: ", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
                filterBar.Children.Add(filterCDE);
                root.Children.Add(filterBar);

                var dg = new DataGrid
                {
                    AutoGenerateColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column,
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, CanUserSortColumns = true,
                    SelectionMode = DataGridSelectionMode.Single, FontSize = 11, MaxHeight = 280,
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

                // Row styling: overdue = red, rejected = orange, approved = green tint
                var rowStyle = new Style(typeof(DataGridRow));
                var overdueT = new DataTrigger { Binding = new Binding("IsOverdue"), Value = true };
                overdueT.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Br(Color.FromRgb(0xFF, 0xEB, 0xEE))));
                overdueT.Setters.Add(new Setter(DataGridRow.FontWeightProperty, FontWeights.Bold));
                rowStyle.Triggers.Add(overdueT);
                var rejectedT = new DataTrigger { Binding = new Binding("Status"), Value = "Rejected" };
                rejectedT.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Br(Color.FromRgb(0xFF, 0xF3, 0xE0))));
                rowStyle.Triggers.Add(rejectedT);
                var approvedT = new DataTrigger { Binding = new Binding("Status"), Value = "Approved" };
                approvedT.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Br(Color.FromRgb(0xE8, 0xF5, 0xE9))));
                rowStyle.Triggers.Add(approvedT);
                dg.RowStyle = rowStyle;

                // Double-click to view/edit deliverable
                dg.MouseDoubleClick += (s, e) =>
                {
                    if (dg.SelectedItem is DeliverableRow del)
                    { DispatchAction("ViewDocument_" + del.Code); }
                };

                // Right-click context menu
                var dgCtx = new ContextMenu();
                var ctxView = new MenuItem { Header = "View Document Details" };
                ctxView.Click += (s, e) => { if (dg.SelectedItem is DeliverableRow d2) { DispatchAction("ViewDocument_" + d2.Code); } };
                dgCtx.Items.Add(ctxView);
                var ctxCDE = new MenuItem { Header = "Update CDE Status" };
                ctxCDE.Click += (s, e) => { DispatchAction("CDEStatus"); };
                dgCtx.Items.Add(ctxCDE);
                var ctxTransmit = new MenuItem { Header = "Create Transmittal for Selection" };
                ctxTransmit.Click += (s, e) => { DispatchAction("CreateTransmittal"); };
                dgCtx.Items.Add(ctxTransmit);
                dgCtx.Items.Add(new Separator());
                var ctxApprove = new MenuItem { Header = "Submit for Approval" };
                ctxApprove.Click += (s, e) => { DispatchAction("ApprovalWorkflow"); };
                dgCtx.Items.Add(ctxApprove);
                var ctxExport = new MenuItem { Header = "Export to Register CSV" };
                ctxExport.Click += (s, e) => { DispatchAction("DocumentRegister"); };
                dgCtx.Items.Add(ctxExport);
                dg.ContextMenu = dgCtx;

                // Wire filters to DataGrid
                Action applyFilter = () =>
                {
                    string dropVal = filterDrop.SelectedItem as string;
                    string statusVal = filterStatus.SelectedItem as string;
                    string cdeVal = filterCDE.SelectedItem as string;
                    var filtered = _data.Deliverables.Where(d =>
                        (dropVal == "All Drops" || d.DataDrop == dropVal) &&
                        (statusVal == "All Status" || d.Status == statusVal) &&
                        (cdeVal == "All CDE" || d.CDE == cdeVal)
                    ).ToList();
                    dg.ItemsSource = filtered;
                };
                filterDrop.SelectionChanged += (s, e) => applyFilter();
                filterStatus.SelectionChanged += (s, e) => applyFilter();
                filterCDE.SelectionChanged += (s, e) => applyFilter();

                root.Children.Add(dg);
            }
            else
            {
                var infoCard = MakeCard();
                var infoStack = new StackPanel();
                infoStack.Children.Add(new TextBlock { Text = "No deliverables tracked yet.", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
                infoStack.Children.Add(new TextBlock { Text = "Use 'Add Deliverable' to start tracking, or run a Document Package workflow to auto-populate from the model.\n\nISO 19650 deliverables include: BIM Execution Plan, models, drawings, schedules, COBie data, transmittals, and handover documentation.", FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Br(Color.FromRgb(0x61, 0x61, 0x61)) });
                infoCard.Child = infoStack;
                root.Children.Add(infoCard);
            }

            // ── ACTION BUTTONS ──────────────────────────────────────────
            root.Children.Add(MakeSectionHeader("ACTIONS"));
            var actGrid = new Grid { Margin = new Thickness(0, 4, 0, 8) };
            actGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Column 1: Document Management
            var col1 = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            col1.Children.Add(new TextBlock { Text = "DOCUMENT MANAGEMENT", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)), Margin = new Thickness(0, 0, 0, 4) });
            col1.Children.Add(MakeActionButton("Add Deliverable", "AddDocument", Br(CGreen), "Register a new deliverable in the tracker"));
            col1.Children.Add(MakeActionButton("Document Register", "DocumentRegister", Br(CHeaderBg), "Export full document register to CSV"));
            col1.Children.Add(MakeActionButton("Drawing Register", "DrawingRegisterSync", Br(Color.FromRgb(0x45, 0x50, 0x6E)), "Sync drawing register from model sheets"));
            col1.Children.Add(MakeActionButton("Document Center", "DocumentManager", Br(Color.FromRgb(0x37, 0x47, 0x4F)), "Open Document Management Center"));
            Grid.SetColumn(col1, 0);

            // Column 2: CDE & Transmittals
            var col2 = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            col2.Children.Add(new TextBlock { Text = "CDE & TRANSMITTALS", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)), Margin = new Thickness(0, 0, 0, 4) });
            col2.Children.Add(MakeActionButton("CDE Package", "CDEPackage", Br(Color.FromRgb(0x15, 0x65, 0xC0)), "Create ISO 19650 CDE folder package"));
            col2.Children.Add(MakeActionButton("Update CDE Status", "CDEStatus", Br(Color.FromRgb(0x00, 0x97, 0xA7)), "Update CDE state for selected documents"));
            col2.Children.Add(MakeActionButton("Create Transmittal", "CreateTransmittal", Br(CAccent), "Create document transmittal for selected items"));
            col2.Children.Add(MakeActionButton("Approval Workflow", "ApprovalWorkflow", Br(Color.FromRgb(0x6A, 0x1B, 0x9A)), "Submit documents for ISO 19650 approval"));
            Grid.SetColumn(col2, 1);

            // Column 3: Handover & Compliance
            var col3 = new StackPanel();
            col3.Children.Add(new TextBlock { Text = "HANDOVER & COMPLIANCE", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)), Margin = new Thickness(0, 0, 0, 4) });
            col3.Children.Add(MakeActionButton("Stage Gate Check", "StageComplianceGate", Br(CRed), "ISO 19650 stage compliance assessment"));
            col3.Children.Add(MakeActionButton("Data Drop Readiness", "DataDropReadiness", Br(Color.FromRgb(0xBF, 0x36, 0x0C)), "Check readiness for current data drop milestone"));
            col3.Children.Add(MakeActionButton("Handover Package", "DocumentBriefcase", Br(Color.FromRgb(0x00, 0x69, 0x5C)), "Assemble FM handover documentation package"));
            col3.Children.Add(MakeActionButton("Validate Naming", "ValidateDocNaming", Br(Color.FromRgb(0x45, 0x50, 0x6E)), "Check ISO 19650 document naming compliance"));
            Grid.SetColumn(col3, 2);

            actGrid.Children.Add(col1); actGrid.Children.Add(col2); actGrid.Children.Add(col3);
            root.Children.Add(actGrid);

            return sv;
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

            // Search and category filter bar
            var filterPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(filterPanel, Dock.Top);
            var searchBox = new TextBox
            {
                Width = 200, Height = 28, FontSize = 11, Padding = new Thickness(6, 4, 6, 4),
                BorderBrush = Br(CBorder), BorderThickness = new Thickness(1),
                ToolTip = "Search log entries by action, detail, or user"
            };
            // Watermark
            searchBox.GotFocus += (s, e) => { if (searchBox.Text == "Search log...") { searchBox.Text = ""; searchBox.Foreground = Brushes.Black; } };
            searchBox.LostFocus += (s, e) => { if (string.IsNullOrWhiteSpace(searchBox.Text)) { searchBox.Text = "Search log..."; searchBox.Foreground = Brushes.Gray; } };
            searchBox.Text = "Search log..."; searchBox.Foreground = Brushes.Gray;
            filterPanel.Children.Add(searchBox);
            filterPanel.Children.Add(new TextBlock { Text = "  Category: ", VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Margin = new Thickness(8, 0, 0, 0) });
            var catFilter = new ComboBox { Width = 100, Height = 28, FontSize = 11, ToolTip = "Filter by log category" };
            catFilter.Items.Add("All");
            foreach (string cat in catGroups.Keys.OrderBy(k => k)) catFilter.Items.Add(cat);
            catFilter.SelectedIndex = 0;
            filterPanel.Children.Add(catFilter);
            filterPanel.Children.Add(new TextBlock { Text = "  Impact: ", VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Margin = new Thickness(8, 0, 0, 0) });
            var impactFilter = new ComboBox { Width = 80, Height = 28, FontSize = 11, ToolTip = "Filter by impact level" };
            impactFilter.Items.Add("All"); impactFilter.Items.Add("HIGH"); impactFilter.Items.Add("MEDIUM"); impactFilter.Items.Add("LOW");
            impactFilter.SelectedIndex = 0;
            filterPanel.Children.Add(impactFilter);
            root.Children.Add(filterPanel);

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
                var allLogItems = _data.CoordLog;
                dg.ItemsSource = allLogItems;

                // Search and filter handler
                Action applyFilter = () =>
                {
                    string searchText = (searchBox.Text == "Search log..." ? "" : searchBox.Text ?? "").ToLowerInvariant();
                    string catSel = catFilter.SelectedItem?.ToString() ?? "All";
                    string impSel = impactFilter.SelectedItem?.ToString() ?? "All";
                    dg.ItemsSource = allLogItems.Where(entry =>
                        (string.IsNullOrEmpty(searchText) ||
                         (entry.Action ?? "").ToLowerInvariant().Contains(searchText) ||
                         (entry.Detail ?? "").ToLowerInvariant().Contains(searchText) ||
                         (entry.User ?? "").ToLowerInvariant().Contains(searchText)) &&
                        (catSel == "All" || entry.Category == catSel) &&
                        (impSel == "All" || entry.Impact == impSel)
                    ).ToList();
                };
                searchBox.TextChanged += (s, e) => applyFilter();
                catFilter.SelectionChanged += (s, e) => applyFilter();
                impactFilter.SelectionChanged += (s, e) => applyFilter();
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

            // ── Task Distribution by Assignee (uses CoordData.TasksByAssignee) ──
            if (_data.TasksByAssignee.Count > 0 || _data.IssuesByAssignee.Count > 0)
            {
                stack.Children.Add(MakeSectionHeader("WORKLOAD DISTRIBUTION"));
                var distCard = MakeCard();
                var distStack = new StackPanel();
                var allAssignees = _data.TasksByAssignee.Keys
                    .Union(_data.IssuesByAssignee.Keys)
                    .Distinct().OrderBy(a => a).ToList();
                int maxWorkload = Math.Max(1,
                    allAssignees.Max(a =>
                        (_data.TasksByAssignee.TryGetValue(a, out int tv) ? tv : 0) +
                        (_data.IssuesByAssignee.TryGetValue(a, out int iv) ? iv : 0)));

                foreach (string assignee in allAssignees)
                {
                    int tasks = _data.TasksByAssignee.TryGetValue(assignee, out int t) ? t : 0;
                    int issues = _data.IssuesByAssignee.TryGetValue(assignee, out int iss) ? iss : 0;
                    double barPct = (tasks + issues) * 100.0 / maxWorkload;

                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

                    row.Children.Add(new TextBlock { Text = assignee, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });

                    // Stacked bar (tasks=blue, issues=orange)
                    var barGrid = new Grid { VerticalAlignment = VerticalAlignment.Center, Height = 14 };
                    barGrid.Children.Add(new Border { Background = Br(Color.FromRgb(0xE0, 0xE0, 0xE0)), CornerRadius = new CornerRadius(3) });
                    double taskPct = tasks * 100.0 / maxWorkload;
                    double issPct = issues * 100.0 / maxWorkload;
                    var taskBar = new Border { Background = Br(CHeaderBg), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
                    var issBar = new Border { Background = Br(CAccent), CornerRadius = new CornerRadius(0, 3, 3, 0), HorizontalAlignment = HorizontalAlignment.Left };
                    barGrid.Children.Add(taskBar);
                    barGrid.Loaded += (s, e) =>
                    {
                        double w = barGrid.ActualWidth;
                        taskBar.Width = Math.Max(2, w * (taskPct + issPct) / 100.0);
                        issBar.Width = Math.Max(0, w * issPct / 100.0);
                        issBar.Margin = new Thickness(w * taskPct / 100.0, 0, 0, 0);
                    };
                    barGrid.Children.Add(issBar);
                    Grid.SetColumn(barGrid, 1);
                    row.Children.Add(barGrid);

                    var countTb = new TextBlock { Text = $"{tasks}T / {issues}I", FontSize = 10, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
                    Grid.SetColumn(countTb, 2);
                    row.Children.Add(countTb);
                    row.ToolTip = $"{assignee}\nTasks: {tasks}\nIssues: {issues}\nTotal workload: {tasks + issues}";
                    distStack.Children.Add(row);
                }
                // Legend
                var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                legend.Children.Add(new Border { Background = Br(CHeaderBg), Width = 12, Height = 12, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 4, 0) });
                legend.Children.Add(new TextBlock { Text = "Tasks", FontSize = 10, Margin = new Thickness(0, 0, 12, 0) });
                legend.Children.Add(new Border { Background = Br(CAccent), Width = 12, Height = 12, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 4, 0) });
                legend.Children.Add(new TextBlock { Text = "Issues", FontSize = 10 });
                distStack.Children.Add(legend);
                distCard.Child = distStack;
                stack.Children.Add(distCard);
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
        //  TOOLTIP DICTIONARY
        // ════════════════════════════════════════════════════════════════

        private static string GetActionTooltip(string actionTag)
        {
            return actionTag switch
            {
                // Overview
                "RunDailyQA" => "Run Daily QA workflow: retag stale → validate → audit → dashboard",
                "RunMorningCheck" => "Morning health check: warnings → tags → templates → issues → revisions",
                "RetagStale" => "Find elements with stale tags (moved/changed) and re-derive their tags",
                "TagNewOnly" => "Tag only new/untagged elements — skips already-tagged elements",
                "ExportCOBie" => "Export COBie V2.4 FM handover data (17 worksheets, XLSX)",
                "FullComplianceDashboard" => "Full project compliance report with per-discipline breakdown",
                "DocumentManager" => "Open Document Management Center — folders, issues, revisions, CDE",
                "RepeatLastWorkflow" => $"Re-run last workflow preset",
                // Model Health
                "RefreshHealth" => "Refresh model health metrics (warnings, tags, stale elements)",
                "ExportHealth" => "Export model health report to CSV/HTML",
                "RunFullCheck" => "Run 45-point template validation check (data files, parameters, formulas)",
                // Warnings
                "AutoFixWarnings" => "Auto-fix: duplicate instances, room separation overlaps, duplicate marks",
                "CreateIssuesFromWarnings" => "Create NCR/SI issues from critical/high severity warnings",
                "ExportWarnings" => "Export all classified warnings to CSV for BIM360/Aconex",
                "SaveBaseline" => "Save current warning count as baseline for trend tracking",
                "SaveExtendedBaseline" => "Save warning types + counts for regression analysis",
                "SelectWarningElements" => "Select elements associated with a specific warning type",
                "SuppressWarnings" => "Suppress warning types from dashboard (persisted to config)",
                "WarningsCompliance" => "Map warnings to ISO 19650 / CIBSE / BS 7671 requirements",
                // Issues
                "RaiseIssue" => "Raise RFI/Clash/NCR/Snagging issue with element linking + BCF",
                "UpdateIssue" => "Update issue status, priority, assignee, or close issues",
                "BCFExport" => "Export issues as BCF 2.1 XML for Navisworks/Solibri/BIMcollab",
                "BCFImport" => "Import BCF issues from external clash detection tools",
                "CreateTransmittal" => "Create ISO 19650 document transmittal record",
                // Revisions
                "CreateRevision" => "Create new revision with ISO 19650 naming and compliance gate",
                "AutoRevisionCloud" => "Auto-generate revision clouds for changed elements",
                "TakeSnapshot" => "Capture model compliance snapshot: tag %, container %, warnings, stale count, per-discipline breakdown. Saved to snapshots.json for trend tracking.",
                "RevisionCompare" => "Compare tag values between revision snapshots",
                // Platform
                "PlatformSync" => "Bidirectional sync with CDE platform (delta detection)",
                "CDEPackage" => "Package files into ISO 19650 CDE folder structure",
                "CDEStatus" => "Set CDE status (WIP → SHARED → PUBLISHED → ARCHIVE)",
                "ValidateDocNaming" => "Validate document naming against ISO 19650 convention",
                "ExportToExcel" => "Export element data to Excel (30+ columns with tags, identity, spatial)",
                "ImportFromExcel" => "Import data from Excel with validation and change tracking",
                "ExcelRoundTrip" => "One-click export → edit → import Excel data exchange",
                "COBieExport" => "Export COBie V2.4 (17 worksheets) for FM handover",
                "IFCExport" => "Export model as IFC with STING property mapping",
                "ACCPublish" => "Package for Autodesk Construction Cloud / BIM 360",
                "SharePointExport" => "Export to corporate SharePoint / Microsoft Teams",
                // 4D/5D
                "AutoSchedule4D" => "Auto-generate 4D construction schedule from model phases",
                "AutoCost5D" => "Auto-calculate 5D cost estimates from element quantities",
                "ViewTimeline4D" => "Visualise 4D timeline as Gantt chart",
                "CostReport5D" => "Generate 5D cost breakdown report",
                "CashFlow5D" => "Generate cash flow forecast from scheduled costs",
                "ImportMSProject" => "Import task schedule from Microsoft Project (.mpp/.xml)",
                // QA
                "ValidateTags" => "Validate tag completeness and ISO 19650 compliance",
                "PreTagAudit" => "Dry-run audit: predict tags, collisions, ISO violations before tagging",
                "AnomalyAutoFix" => "Auto-fix tag anomalies (DISC/SYS/FUNC/PROD/TAG7/stale)",
                "ResolveAllIssues" => "One-click ISO 19650 compliance resolution (batched, 500 elements)",
                // Deliverables
                "AddDocument" => "Register new deliverable in document register",
                "DocumentRegister" => "View/manage document register entries",
                "StageComplianceGate" => "RIBA stage-gated compliance check with data drop requirements",
                // Meeting Manager
                "NewMeeting" => "Create new meeting: BIM Coordination, Design Review, Client Review, Handover, Clash Resolution. Auto-populates attendees from team registry and carries forward open actions from previous meetings.",
                "AddActionItem" => "Create action item with description, assignee (from team registry), due date, and priority. Links to active meeting with unique ACT-NNNN ID.",
                "AutoAgenda" => "Auto-generate meeting agenda from: open issues (grouped by type/priority), pending transmittals, recent revisions, compliance status, outstanding action items.",
                "LogMinutes" => "Record timestamped meeting minutes with topic/discussion/action format. Saved alongside meeting record in meetings.json.",
                "MeetingTemplates" => "Browse 5 meeting templates: BIM Coordination (weekly), Design Team (fortnightly), Client Progress (monthly), Stage Gate (RIBA), Ad-hoc (on-demand).",
                "MeetingHistory" => "View all past meetings with minutes, action items, outcomes, and attendee lists. Drill down per meeting.",
                "OpenActions" => "View all outstanding action items grouped by: overdue first, then by assignee and due date. Options to mark complete, reassign, or escalate.",
                "ExportMinutes" => "Export meeting minutes to timestamped text file for distribution via email or CDE.",
                "SendReminder" => "Generate email reminder text for outstanding action items with overdue highlighting.",
                "EscalateActions" => "Auto-create NCR issues from overdue meeting actions. Original actions marked ESCALATED with cross-reference to new issue ID.",
                // Permissions
                "EditUserRole" => "Change your active ISO 19650 role (A/M/E/S/C/I/K/F/etc.). Determines CDE folder access, approval rights, and notification routing.",
                "SavePermissions" => "Save permission matrix to project_config.json for team-wide consistency.",
                "CreateFolders" => "Create ISO 19650 CDE folder structure (WIP/SHARED/PUBLISHED/ARCHIVE + discipline sub-folders).",
                "ExportPermissionMatrix" => "Export role-based permission matrix to CSV for auditing and BEP compliance.",
                // Workflow
                "ExportReport" => "Export current model health and compliance report to CSV or HTML",
                "SelectAllTaggable" => "Select all taggable elements in the active view for batch operations",
                "CombineParameters" => "Write tag values to all 53 discipline-specific container parameters",
                _ => null
            };
        }

        // ════════════════════════════════════════════════════════════════
        //  PERMISSIONS & ACCESS CONTROL TAB
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildPermissionsTab()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(20) };
            var stack = new StackPanel();

            // Phase 76: Cache current role/folder data for SavePermissionsInline
            _lastPermissionsRoles   = _data.Roles.Count   > 0 ? _data.Roles   : GetDefaultRoles();
            _lastPermissionsFolders = _data.FolderPermissions.Count > 0 ? _data.FolderPermissions : GetDefaultFolderPermissions();

            // ── Current User ──
            stack.Children.Add(MakeSectionHeader("CURRENT USER"));
            var userCard = MakeCard();
            var userGrid = new Grid();
            userGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            userGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var userInfo = new StackPanel();
            userInfo.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(_data.CurrentUserName) ? Environment.UserName : _data.CurrentUserName,
                FontSize = 16, FontWeight = FontWeights.Bold
            });
            userInfo.Children.Add(new TextBlock
            {
                Text = $"Role: {_data.CurrentUserRole}  |  CDE Access: WIP, SHARED",
                FontSize = 12, Foreground = Brushes.Gray
            });
            userInfo.Children.Add(new TextBlock
            {
                Text = "Can approve: Yes  |  Can issue transmittals: Yes  |  Can modify published: No",
                FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 2, 0, 0)
            });
            userGrid.Children.Add(userInfo);
            var editRoleBtn = MakeActionButton("Change Role", "EditUserRole", Br(CAccent),
                "Change your active role for CDE permission evaluation");
            editRoleBtn.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(editRoleBtn, 1);
            userGrid.Children.Add(editRoleBtn);
            userCard.Child = userGrid;
            stack.Children.Add(userCard);

            // ── ISO 19650 Role Definitions ──
            stack.Children.Add(new Border { Height = 8 });
            stack.Children.Add(MakeSectionHeader("ISO 19650 ROLE DEFINITIONS"));
            stack.Children.Add(new TextBlock
            {
                Text = "Roles define CDE folder access, approval rights, and document naming originator codes per ISO 19650-2.",
                FontSize = 10, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            });

            // Role definition table
            var roleCard = MakeCard();
            var roleStack = new StackPanel();

            // Header
            var roleHdr = new Grid();
            var roleColWidths = new[] { 40.0, 140, 100, 130, 70, 70 };
            foreach (double w in roleColWidths) roleHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            string[] roleHeaders = { "Code", "Role", "Discipline", "CDE Write Access", "Approve", "Issue" };
            for (int c = 0; c < roleHeaders.Length; c++)
            {
                var tb = new TextBlock
                {
                    Text = roleHeaders[c], FontWeight = FontWeights.Bold, FontSize = 10,
                    Padding = new Thickness(4, 3, 4, 3), Background = Br(Color.FromRgb(0xE8, 0xEA, 0xED))
                };
                Grid.SetColumn(tb, c);
                roleHdr.Children.Add(tb);
            }
            roleStack.Children.Add(roleHdr);

            // Default ISO 19650 roles
            var defaultRoles = _data.Roles.Count > 0 ? _data.Roles : GetDefaultRoles();
            foreach (var role in defaultRoles)
            {
                var rRow = new Grid { ToolTip = $"Role {role.Code}: {role.Name}\nDiscipline: {role.Discipline}\nCDE Access: {role.CDEAccess}\nCan Approve: {role.CanApprove}\nCan Issue: {role.CanIssue}\nMembers: {role.Members}" };
                foreach (double w in roleColWidths) rRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
                string[] vals = { role.Code, role.Name, role.Discipline, role.CDEAccess, role.CanApprove, role.CanIssue };
                for (int c = 0; c < vals.Length; c++)
                {
                    var tb = new TextBlock
                    {
                        Text = vals[c], FontSize = 10, Padding = new Thickness(4, 2, 4, 2),
                        Foreground = (c == 4 || c == 5) && vals[c] == "Yes" ? Br(CGreen) :
                                     (c == 4 || c == 5) && vals[c] == "No" ? Brushes.Gray : Brushes.Black
                    };
                    Grid.SetColumn(tb, c);
                    rRow.Children.Add(tb);
                }
                roleStack.Children.Add(rRow);
            }
            roleCard.Child = roleStack;
            stack.Children.Add(roleCard);

            // ── CDE Folder Permissions ──
            stack.Children.Add(new Border { Height = 8 });
            stack.Children.Add(MakeSectionHeader("CDE FOLDER PERMISSIONS"));
            stack.Children.Add(new TextBlock
            {
                Text = "ISO 19650 CDE folder access matrix — controls who can read, write, and approve documents in each CDE state folder.",
                FontSize = 10, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            });

            var folderCard = MakeCard();
            var folderStack = new StackPanel();
            var defaultFolders = _data.FolderPermissions.Count > 0 ? _data.FolderPermissions : GetDefaultFolderPermissions();
            foreach (var fp in defaultFolders)
            {
                var fpBorder = new Border
                {
                    BorderBrush = Br(CBorder), BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(8, 6, 8, 6),
                    ToolTip = $"Folder: {fp.Folder}\nCDE State: {fp.CDEState}\nRead: {fp.ReadRoles}\nWrite: {fp.WriteRoles}\nApprove: {fp.ApproveRoles}\n{fp.Description}"
                };
                var fpGrid = new Grid();
                fpGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                fpGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                fpGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                fpGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var folderName = new TextBlock { Text = fp.Folder, FontWeight = FontWeights.SemiBold, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                fpGrid.Children.Add(folderName);

                var cdeChip = MakeMetricChip(fp.CDEState,
                    fp.CDEState == "WIP" ? Br(CAmber) :
                    fp.CDEState == "SHARED" ? Br(Color.FromRgb(0x15, 0x65, 0xC0)) :
                    fp.CDEState == "PUBLISHED" ? Br(CGreen) : Br(Color.FromRgb(0x45, 0x50, 0x6E)));
                Grid.SetColumn(cdeChip, 1);
                fpGrid.Children.Add(cdeChip);

                var accessText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                accessText.Children.Add(new TextBlock { Text = $"Read: {fp.ReadRoles}", FontSize = 10, Foreground = Brushes.Gray });
                accessText.Children.Add(new TextBlock { Text = $"Write: {fp.WriteRoles}", FontSize = 10, Foreground = Br(CHeaderBg) });
                if (!string.IsNullOrEmpty(fp.ApproveRoles))
                    accessText.Children.Add(new TextBlock { Text = $"Approve: {fp.ApproveRoles}", FontSize = 10, Foreground = Br(CGreen) });
                Grid.SetColumn(accessText, 2);
                fpGrid.Children.Add(accessText);

                if (fp.IsLocked)
                {
                    var lockIcon = new TextBlock { Text = "\uD83D\uDD12", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Folder locked — no modifications allowed" };
                    Grid.SetColumn(lockIcon, 3);
                    fpGrid.Children.Add(lockIcon);
                }

                fpBorder.Child = fpGrid;
                folderStack.Children.Add(fpBorder);
            }
            folderCard.Child = folderStack;
            stack.Children.Add(folderCard);

            // ── CDE State Transition Rules ──
            stack.Children.Add(new Border { Height = 8 });
            stack.Children.Add(MakeSectionHeader("CDE STATE TRANSITION RULES"));
            var transCard = MakeCard();
            var transStack = new StackPanel();
            var transitions = new[]
            {
                ("WIP", "SHARED", "Share for coordination/review — requires originator approval", "A,M,E,S,H,P"),
                ("SHARED", "PUBLISHED", "Publish for approved use — requires client/lead approval", "K,A"),
                ("SHARED", "WIP", "Return to WIP for rework — originator-initiated", "A,M,E,S,H,P"),
                ("PUBLISHED", "ARCHIVE", "Archive after supersession — requires BIM Manager", "K,A"),
                ("PUBLISHED", "SUPERSEDED", "Replaced by newer revision", "K,A"),
                ("Any", "WITHDRAWN", "Remove from CDE — requires BIM Manager approval", "K,A"),
                ("ARCHIVE", "OBSOLETE", "Terminal state — retained for audit trail only", "K"),
            };
            foreach (var (from, to, desc, approvers) in transitions)
            {
                var tRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
                tRow.Children.Add(MakeMetricChip(from, Br(CAmber)));
                tRow.Children.Add(new TextBlock { Text = " → ", FontSize = 14, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
                tRow.Children.Add(MakeMetricChip(to, Br(CGreen)));
                tRow.Children.Add(new TextBlock { Text = $"  {desc}", FontSize = 10, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), TextWrapping = TextWrapping.Wrap });
                tRow.ToolTip = $"{from} → {to}\n{desc}\nApprovers: {approvers}";
                transStack.Children.Add(tRow);
            }
            transCard.Child = transStack;
            stack.Children.Add(transCard);

            // ── Actions ──
            stack.Children.Add(new Border { Height = 12 });
            var actionsWrap = new WrapPanel();
            actionsWrap.Children.Add(MakeActionButton("Save Permissions", "SavePermissions", Br(CHeaderBg),
                "Save folder permissions and role definitions to project_config.json"));
            actionsWrap.Children.Add(MakeActionButton("Set CDE Status", "CDEStatus", Br(CGreen),
                "Transition document CDE status with role-based approval checking"));
            actionsWrap.Children.Add(MakeActionButton("Validate Naming", "ValidateDocNaming", Br(CAccent),
                "Check all files against ISO 19650 naming with originator role validation"));
            actionsWrap.Children.Add(MakeActionButton("Create Folders", "CreateFolders", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Create ISO 19650 project folder structure with permission metadata"));
            actionsWrap.Children.Add(MakeActionButton("Export Matrix", "ExportPermissionMatrix", Br(Color.FromRgb(0x6A, 0x1B, 0x9A)),
                "Export permission matrix to CSV for BIM coordination plan"));
            stack.Children.Add(actionsWrap);

            scroll.Content = stack;
            return scroll;
        }

        private static List<RoleDefinition> GetDefaultRoles()
        {
            return new List<RoleDefinition>
            {
                new() { Code = "A", Name = "Architect", Discipline = "Architecture", CDEAccess = "WIP,SHARED", CanApprove = "Yes", CanIssue = "Yes", Members = "" },
                new() { Code = "M", Name = "Mechanical Engineer", Discipline = "Mechanical", CDEAccess = "WIP,SHARED", CanApprove = "No", CanIssue = "Yes", Members = "" },
                new() { Code = "E", Name = "Electrical Engineer", Discipline = "Electrical", CDEAccess = "WIP,SHARED", CanApprove = "No", CanIssue = "Yes", Members = "" },
                new() { Code = "S", Name = "Structural Engineer", Discipline = "Structural", CDEAccess = "WIP,SHARED", CanApprove = "No", CanIssue = "Yes", Members = "" },
                new() { Code = "H", Name = "HVAC Engineer", Discipline = "Mechanical", CDEAccess = "WIP,SHARED", CanApprove = "No", CanIssue = "Yes", Members = "" },
                new() { Code = "P", Name = "Public Health Engineer", Discipline = "Plumbing", CDEAccess = "WIP,SHARED", CanApprove = "No", CanIssue = "Yes", Members = "" },
                new() { Code = "C", Name = "Civil Engineer", Discipline = "Civil", CDEAccess = "WIP,SHARED", CanApprove = "No", CanIssue = "Yes", Members = "" },
                new() { Code = "I", Name = "Interior Designer", Discipline = "Architecture", CDEAccess = "WIP", CanApprove = "No", CanIssue = "No", Members = "" },
                new() { Code = "K", Name = "Client / Employer", Discipline = "—", CDEAccess = "SHARED,PUBLISHED", CanApprove = "Yes", CanIssue = "No", Members = "" },
                new() { Code = "Q", Name = "Quantity Surveyor", Discipline = "Cost", CDEAccess = "WIP,SHARED", CanApprove = "No", CanIssue = "Yes", Members = "" },
                new() { Code = "F", Name = "Facilities Manager", Discipline = "FM", CDEAccess = "PUBLISHED,ARCHIVE", CanApprove = "Yes", CanIssue = "No", Members = "" },
                new() { Code = "W", Name = "Contractor", Discipline = "Construction", CDEAccess = "SHARED", CanApprove = "No", CanIssue = "No", Members = "" },
                new() { Code = "L", Name = "Landscape Architect", Discipline = "Landscape", CDEAccess = "WIP,SHARED", CanApprove = "No", CanIssue = "Yes", Members = "" },
                new() { Code = "Z", Name = "General / Non-disciplinary", Discipline = "—", CDEAccess = "WIP", CanApprove = "No", CanIssue = "No", Members = "" },
            };
        }

        private static List<FolderPermission> GetDefaultFolderPermissions()
        {
            return new List<FolderPermission>
            {
                new() { Folder = "01_WIP", CDEState = "WIP", ReadRoles = "All team", WriteRoles = "A,M,E,S,H,P,C,I,Q,L", ApproveRoles = "", IsLocked = false, Description = "Work in progress — originator's active workspace" },
                new() { Folder = "02_SHARED", CDEState = "SHARED", ReadRoles = "All team", WriteRoles = "A,M,E,S (originators)", ApproveRoles = "A,K", IsLocked = false, Description = "Shared for coordination — approved by originator for team review" },
                new() { Folder = "03_PUBLISHED", CDEState = "PUBLISHED", ReadRoles = "All team + Client", WriteRoles = "BIM Manager only", ApproveRoles = "K,A", IsLocked = true, Description = "Published — approved deliverables, locked for modification" },
                new() { Folder = "04_ARCHIVE", CDEState = "ARCHIVE", ReadRoles = "All team", WriteRoles = "None", ApproveRoles = "K", IsLocked = true, Description = "Archive — superseded documents retained for audit trail" },
                new() { Folder = "05_MODELS", CDEState = "WIP", ReadRoles = "All team", WriteRoles = "A,M,E,S,H,P,C", ApproveRoles = "", IsLocked = false, Description = "Revit models (.rvt), IFC exports, Navisworks coordination" },
                new() { Folder = "06_DRAWINGS", CDEState = "SHARED", ReadRoles = "All team + Client", WriteRoles = "A,M,E,S", ApproveRoles = "A", IsLocked = false, Description = "Drawing sheets (PDF, DWG), plotted layouts" },
                new() { Folder = "07_SCHEDULES", CDEState = "SHARED", ReadRoles = "All team", WriteRoles = "A,M,E,S,Q", ApproveRoles = "", IsLocked = false, Description = "Equipment schedules, door/window schedules, BOQ" },
                new() { Folder = "08_COBIE", CDEState = "PUBLISHED", ReadRoles = "All team + FM", WriteRoles = "BIM Manager", ApproveRoles = "K,F", IsLocked = true, Description = "COBie V2.4 FM handover data — locked after approval" },
                new() { Folder = "09_BEP", CDEState = "PUBLISHED", ReadRoles = "All team + Client", WriteRoles = "BIM Manager", ApproveRoles = "K", IsLocked = true, Description = "BIM Execution Plan — ISO 19650-2 §5.3 pre-contract document" },
                new() { Folder = "10_ISSUES", CDEState = "SHARED", ReadRoles = "All team", WriteRoles = "All team", ApproveRoles = "", IsLocked = false, Description = "RFI, clash, NCR, snagging issues and BCF files" },
                new() { Folder = "11_CLASHES", CDEState = "SHARED", ReadRoles = "All team", WriteRoles = "Coordinator", ApproveRoles = "", IsLocked = false, Description = "Clash detection reports and coordination records" },
                new() { Folder = "12_HANDOVER", CDEState = "PUBLISHED", ReadRoles = "All team + FM + Client", WriteRoles = "BIM Manager", ApproveRoles = "K,F", IsLocked = true, Description = "FM handover package — O&M, asset register, maintenance schedules" },
            };
        }

        // ════════════════════════════════════════════════════════════════
        //  MEETINGS TAB
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildMeetingsTab()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(20) };
            var stack = new StackPanel();

            // ── UPCOMING MEETINGS ──
            stack.Children.Add(MakeSectionHeader("UPCOMING MEETINGS"));
            var upcomingCard = MakeCard();
            var ucStack = new StackPanel();
            // Load meetings from JSON sidecar
            var meetings = LoadMeetings();
            var upcoming = meetings.Where(m => m.Status == "PLANNED" || m.Status == "IN_PROGRESS")
                .OrderBy(m => m.Date).Take(5).ToList();
            if (upcoming.Count == 0)
            {
                ucStack.Children.Add(new TextBlock
                {
                    Text = "No upcoming meetings scheduled", FontSize = 12,
                    Foreground = Brushes.Gray, FontStyle = FontStyles.Italic, Margin = new Thickness(0, 4, 0, 4)
                });
            }
            foreach (var mtg in upcoming)
            {
                var row = new Border
                {
                    BorderBrush = Br(CBorder), BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 2)
                };
                var rGrid = new Grid();
                rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var leftSp = new StackPanel();
                leftSp.Children.Add(new TextBlock { Text = mtg.Title, FontWeight = FontWeights.SemiBold, FontSize = 12 });
                leftSp.Children.Add(new TextBlock
                {
                    Text = $"{mtg.Type}  •  {mtg.Date}  •  {mtg.Attendees} attendees",
                    FontSize = 10, Foreground = Brushes.Gray
                });
                rGrid.Children.Add(leftSp);

                var statusChip = MakeMetricChip(mtg.Status,
                    mtg.Status == "IN_PROGRESS" ? Br(CAccent) : Br(Color.FromRgb(0x45, 0x50, 0x6E)));
                Grid.SetColumn(statusChip, 1);
                rGrid.Children.Add(statusChip);

                row.Child = rGrid;
                row.Cursor = Cursors.Hand;
                row.ToolTip = new ToolTip
                {
                    Content = new TextBlock
                    {
                        Text = $"Meeting: {mtg.Title}\nType: {mtg.Type}\nDate: {mtg.Date}\nStatus: {mtg.Status}\nAttendees: {mtg.Attendees}\n\n" +
                               "Click: View meeting history\nRight-click: Log minutes / Add action",
                        MaxWidth = 350, TextWrapping = TextWrapping.Wrap, FontSize = 12
                    },
                    Background = Br(Color.FromRgb(0x2D, 0x2D, 0x30)),
                    Foreground = Brushes.White, Padding = new Thickness(10, 6, 10, 6)
                };
                // Hover
                row.MouseEnter += (s, e) => { row.Background = Br(Color.FromRgb(0xE3, 0xF2, 0xFD)); };
                row.MouseLeave += (s, e) => { row.Background = Brushes.Transparent; };
                // Click → meeting history
                row.MouseLeftButtonDown += (s, e) => { DispatchAction("MeetingHistory"); };
                // Context menu
                var mtgCtx = new ContextMenu();
                var logMin = new MenuItem { Header = "Log Minutes" };
                logMin.Click += (s, e) => { DispatchAction("LogMinutes"); };
                mtgCtx.Items.Add(logMin);
                var addAct = new MenuItem { Header = "Add Action Item" };
                addAct.Click += (s, e) => { DispatchAction("AddActionItem"); };
                mtgCtx.Items.Add(addAct);
                var expMin = new MenuItem { Header = "Export Minutes" };
                expMin.Click += (s, e) => { DispatchAction("ExportMinutes"); };
                mtgCtx.Items.Add(expMin);
                var sendRem = new MenuItem { Header = "Send Reminder" };
                sendRem.Click += (s, e) => { DispatchAction("SendReminder"); };
                mtgCtx.Items.Add(sendRem);
                row.ContextMenu = mtgCtx;
                ucStack.Children.Add(row);
            }
            upcomingCard.Child = ucStack;
            stack.Children.Add(upcomingCard);

            // ── PREPARE ──
            stack.Children.Add(new Border { Height = 12 });
            stack.Children.Add(MakeSectionHeader("PREPARE"));
            var prepWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
            prepWrap.Children.Add(MakeActionButton("New Meeting", "NewMeeting", Br(CHeaderBg),
                "Create new meeting: BIM Coordination, Design Review, Client Review, Handover, or Clash Resolution"));
            prepWrap.Children.Add(MakeActionButton("Auto Agenda", "AutoAgenda", Br(CGreen),
                "Auto-generate agenda from open issues, pending transmittals, recent revisions, and compliance status"));
            prepWrap.Children.Add(MakeActionButton("Meeting Templates", "MeetingTemplates", Br(Color.FromRgb(0x00, 0x69, 0x7C)),
                "Browse and apply meeting templates for recurring coordination sessions"));
            stack.Children.Add(prepWrap);

            // ── DURING MEETING ──
            stack.Children.Add(MakeSectionHeader("DURING MEETING"));
            var duringWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
            duringWrap.Children.Add(MakeActionButton("Log Minutes", "LogMinutes", Br(CAccent),
                "Record meeting minutes with timestamped notes"));
            duringWrap.Children.Add(MakeActionButton("Add Action Item", "AddActionItem", Br(CGreen),
                "Create action item with assignee, due date, and priority"));
            duringWrap.Children.Add(MakeActionButton("Quick Issue", "RaiseIssue", Br(CRed),
                "Raise RFI/NCR/SI issue directly from meeting context"));
            duringWrap.Children.Add(MakeActionButton("Take Snapshot", "TakeSnapshot", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Capture model state snapshot for meeting record"));
            stack.Children.Add(duringWrap);

            // ── REVIEW ──
            stack.Children.Add(MakeSectionHeader("REVIEW & FOLLOW-UP"));
            var reviewWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
            reviewWrap.Children.Add(MakeActionButton("Meeting History", "MeetingHistory", Br(Color.FromRgb(0x00, 0x69, 0x7C)),
                "View all past meetings with minutes, actions, and outcomes"));
            reviewWrap.Children.Add(MakeActionButton("Open Actions", "OpenActions", Br(CRed),
                "View all outstanding action items grouped by overdue/upcoming"));
            reviewWrap.Children.Add(MakeActionButton("Export Minutes", "ExportMinutes", Br(CGreen),
                "Export meeting minutes to timestamped text file"));
            reviewWrap.Children.Add(MakeActionButton("Send Reminder", "SendReminder", Br(CAccent),
                "Generate email reminder for outstanding action items"));
            stack.Children.Add(reviewWrap);

            // ── ACTION ITEMS SUMMARY ──
            stack.Children.Add(MakeSectionHeader("ACTION ITEMS SUMMARY"));
            var actionsCard = MakeCard();
            var actStack = new StackPanel();
            var actions = LoadActionItems();
            int overdueActions = actions.Count(a => a.IsOverdue);
            int openActions = actions.Count(a => a.Status == "OPEN");

            var actChips = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            actChips.Children.Add(MakeMetricChip($"Total: {actions.Count}", Br(Color.FromRgb(0x45, 0x50, 0x6E))));
            actChips.Children.Add(MakeMetricChip($"Open: {openActions}", openActions > 0 ? Br(CAmber) : Br(CGreen)));
            if (overdueActions > 0)
                actChips.Children.Add(MakeMetricChip($"Overdue: {overdueActions}", Br(CRed)));
            actStack.Children.Add(actChips);

            // Show top 8 overdue/open actions — interactive with hover highlight and context menu
            foreach (var act in actions.Where(a => a.Status == "OPEN").OrderByDescending(a => a.IsOverdue).Take(8))
            {
                var actBorder = new Border
                {
                    Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 1, 0, 1),
                    CornerRadius = new CornerRadius(3), Cursor = Cursors.Hand,
                    Background = act.IsOverdue
                        ? Br(Color.FromRgb(0xFF, 0xEB, 0xEE))
                        : Brushes.Transparent,
                    BorderBrush = act.IsOverdue ? Br(Color.FromRgb(0xFF, 0xCD, 0xD2)) : Brushes.Transparent,
                    BorderThickness = new Thickness(act.IsOverdue ? 1 : 0)
                };

                var actGrid = new Grid();
                actGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                actGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                actGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

                var descText = new TextBlock
                {
                    Text = $"{(act.IsOverdue ? "⚠ " : "• ")}{act.Description}",
                    FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = act.IsOverdue ? Br(CRed) : Brushes.Black,
                    FontWeight = act.IsOverdue ? FontWeights.SemiBold : FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center
                };
                actGrid.Children.Add(descText);

                var assignText = new TextBlock
                {
                    Text = act.Assignee, FontSize = 10,
                    Foreground = Br(Color.FromRgb(0x60, 0x60, 0x60)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetColumn(assignText, 1);
                actGrid.Children.Add(assignText);

                var dueText = new TextBlock
                {
                    Text = act.DueDate, FontSize = 10,
                    Foreground = act.IsOverdue ? Br(CRed) : Br(Color.FromRgb(0x60, 0x60, 0x60)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(dueText, 2);
                actGrid.Children.Add(dueText);

                actBorder.Child = actGrid;

                // Rich tooltip with full details
                actBorder.ToolTip = new ToolTip
                {
                    Content = new TextBlock
                    {
                        Text = $"Action: {act.Description}\n" +
                               $"Assignee: {act.Assignee}\n" +
                               $"Due: {act.DueDate}\n" +
                               $"Status: {act.Status}\n" +
                               (act.IsOverdue ? "⚠ OVERDUE — right-click to escalate to NCR issue\n" : "") +
                               "\nLeft-click: Open action details\nRight-click: Options menu",
                        FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                        MaxWidth = 350, TextWrapping = TextWrapping.Wrap
                    },
                    Background = Br(Color.FromRgb(0x2D, 0x2D, 0x30)),
                    Foreground = Brushes.White, Padding = new Thickness(10, 6, 10, 6)
                };

                // Hover effect
                var origBg = actBorder.Background;
                actBorder.MouseEnter += (s, e) =>
                {
                    actBorder.Background = Br(Color.FromRgb(0xE3, 0xF2, 0xFD));
                    actBorder.BorderBrush = Br(CAccent);
                    actBorder.BorderThickness = new Thickness(1);
                };
                actBorder.MouseLeave += (s, e) =>
                {
                    actBorder.Background = origBg;
                    actBorder.BorderBrush = act.IsOverdue ? Br(Color.FromRgb(0xFF, 0xCD, 0xD2)) : Brushes.Transparent;
                    actBorder.BorderThickness = new Thickness(act.IsOverdue ? 1 : 0);
                };

                // Click to view open actions
                actBorder.MouseLeftButtonDown += (s, e) =>
                {
                    DispatchAction("OpenActions");
                };

                // Context menu
                var ctxMenu = new ContextMenu();
                var markDone = new MenuItem { Header = "Mark as Completed" };
                markDone.Click += (s, e) => { DispatchAction("OpenActions"); };
                ctxMenu.Items.Add(markDone);

                if (act.IsOverdue)
                {
                    var escalate = new MenuItem { Header = "Escalate to NCR Issue", Foreground = Br(CRed) };
                    escalate.Click += (s, e) => { DispatchAction("EscalateActions"); };
                    ctxMenu.Items.Add(escalate);
                }

                var reassign = new MenuItem { Header = "Reassign..." };
                reassign.Click += (s, e) => { DispatchAction("OpenActions"); };
                ctxMenu.Items.Add(reassign);

                var addNote = new MenuItem { Header = "Add to Meeting Agenda" };
                addNote.Click += (s, e) => { DispatchAction("AutoAgenda"); };
                ctxMenu.Items.Add(addNote);

                actBorder.ContextMenu = ctxMenu;
                actStack.Children.Add(actBorder);
            }

            // Show count of remaining actions not displayed
            int remaining = actions.Count(a => a.Status == "OPEN") - 8;
            if (remaining > 0)
            {
                var moreText = new TextBlock
                {
                    Text = $"  + {remaining} more open action(s) — click 'Open Actions' to view all",
                    FontSize = 10, FontStyle = FontStyles.Italic,
                    Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)),
                    Margin = new Thickness(0, 4, 0, 0), Cursor = Cursors.Hand
                };
                moreText.MouseLeftButtonDown += (s, e) => { DispatchAction("OpenActions"); };
                moreText.MouseEnter += (s, e) => moreText.TextDecorations = TextDecorations.Underline;
                moreText.MouseLeave += (s, e) => moreText.TextDecorations = null;
                actStack.Children.Add(moreText);
            }

            actionsCard.Child = actStack;
            stack.Children.Add(actionsCard);

            // ── AUTOMATION RULES ──
            stack.Children.Add(new Border { Height = 12 });
            stack.Children.Add(MakeSectionHeader("AUTOMATION RULES"));
            stack.Children.Add(new TextBlock
            {
                Text = "Cross-system automation links meetings ↔ issues ↔ transmittals ↔ compliance for seamless BIM coordination workflows.",
                FontSize = 10, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            });
            var autoCard = MakeCard();
            var autoStack = new StackPanel();

            // Rule 1: Overdue actions → auto-escalate to issues
            int overdueNotEscalated = actions.Count(a => a.IsOverdue && a.Status == "OPEN");
            var rule1 = MakeAutomationRule(
                "Overdue Action → Issue Escalation",
                overdueNotEscalated > 0
                    ? $"{overdueNotEscalated} overdue action(s) can be escalated to NCR issues"
                    : "No overdue actions requiring escalation",
                overdueNotEscalated > 0, "EscalateActions",
                "Auto-create HIGH-priority NCR issues from overdue meeting actions with element linking");
            autoStack.Children.Add(rule1);

            // Rule 2: Open issues → auto-populate next meeting agenda
            int openIssuesForAgenda = _data.IssuesOpen;
            var rule2 = MakeAutomationRule(
                "Open Issues → Next Meeting Agenda",
                openIssuesForAgenda > 0
                    ? $"{openIssuesForAgenda} open issue(s) will auto-populate next meeting agenda"
                    : "No open issues for agenda",
                openIssuesForAgenda > 0, "AutoAgenda",
                "Auto-generate meeting agenda from open issues grouped by type and priority");
            autoStack.Children.Add(rule2);

            // Rule 3: Compliance gate → auto-transmittal trigger
            bool complianceReady = _data.TagPct >= 80 && _data.ContainerCompletePct >= 80 && _data.WarningCritical == 0;
            var rule3 = MakeAutomationRule(
                "Compliance Gate → Transmittal Trigger",
                complianceReady
                    ? $"Model ready for transmittal: {_data.TagPct:F0}% tagged, {_data.ContainerCompletePct:F0}% containers, 0 critical warnings"
                    : $"Not ready: {_data.TagPct:F0}% tagged (need ≥80%), {_data.WarningCritical} critical warnings (need 0)",
                complianceReady, "CreateTransmittal",
                "Auto-create SHARED transmittal when compliance ≥80%, containers ≥80%, and 0 critical warnings");
            autoStack.Children.Add(rule3);

            // Rule 4: Meeting closure → follow-up meeting creation
            int plannedMeetings = meetings.Count(m => m.Status == "PLANNED");
            var rule4 = MakeAutomationRule(
                "Meeting Closure → Follow-Up Scheduling",
                plannedMeetings == 0
                    ? "No upcoming meetings scheduled — create follow-up"
                    : $"{plannedMeetings} meeting(s) already scheduled",
                plannedMeetings == 0, "NewMeeting",
                "Auto-schedule follow-up meeting carrying forward open actions and unresolved issues");
            autoStack.Children.Add(rule4);

            // Rule 5: SLA violation → issue priority escalation
            int slaViolations = _data.SLACriticalViolations + _data.SLAHighViolations;
            var rule5 = MakeAutomationRule(
                "SLA Violation → Priority Escalation",
                slaViolations > 0
                    ? $"{slaViolations} SLA violation(s) — issues should be escalated or reassigned"
                    : "No SLA violations detected",
                slaViolations > 0, "UpdateIssue",
                "Auto-escalate issue priority when SLA threshold exceeded (CRITICAL=4h, HIGH=24h)");
            autoStack.Children.Add(rule5);

            // Rule 6: Stale elements → retag trigger
            var rule6 = MakeAutomationRule(
                "Stale Elements → Auto-Retag",
                _data.StaleCount > 0
                    ? $"{_data.StaleCount} stale element(s) detected — tags no longer match context"
                    : "No stale elements",
                _data.StaleCount > 0, "RetagStale",
                "Auto-retag elements that have moved level, changed system, or been modified since last tag");
            autoStack.Children.Add(rule6);

            autoCard.Child = autoStack;
            stack.Children.Add(autoCard);

            // ── CROSS-SYSTEM LINKS ──
            stack.Children.Add(new Border { Height = 8 });
            stack.Children.Add(MakeSectionHeader("CROSS-SYSTEM LINKS"));
            var linksCard = MakeCard();
            var linksStack = new StackPanel();
            linksStack.Children.Add(new TextBlock
            {
                Text = $"Meetings → Issues: {_data.IssuesOpen} open issues linked to coordination\n" +
                       $"Issues → Transmittals: {_data.DeliverablesSubmitted} transmittals issued with linked issues\n" +
                       $"Transmittals → Compliance: {_data.TagPct:F0}% tag compliance at last transmittal\n" +
                       $"Compliance → Warnings: {_data.WarningsLinkedToIssues} warnings linked to issues\n" +
                       $"Warnings → Stale: {_data.StaleLinkedToWarnings} stale elements with active warnings",
                FontSize = 11, LineHeight = 20, TextWrapping = TextWrapping.Wrap
            });
            linksCard.Child = linksStack;
            stack.Children.Add(linksCard);

            // ── COORDINATION METRICS ──
            stack.Children.Add(new Border { Height = 12 });
            stack.Children.Add(MakeSectionHeader("COORDINATION METRICS"));
            var metricsCard = MakeCard();
            var metStack = new StackPanel();
            int totalMeetings = meetings.Count;
            int completedMeetings = meetings.Count(m => m.Status == "COMPLETED");
            int totalActions2 = actions.Count;
            int closedActions = actions.Count(a => a.Status == "CLOSED");
            double actionCloseRate = totalActions2 > 0 ? (closedActions * 100.0 / totalActions2) : 0;

            var metGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 8) };
            metGrid.Children.Add(MakeKPICard("MEETINGS", totalMeetings.ToString(), Br(Color.FromRgb(0x15, 0x65, 0xC0)),
                $"Completed: {completedMeetings}\nPlanned: {totalMeetings - completedMeetings}"));
            metGrid.Children.Add(MakeKPICard("ACTIONS", totalActions2.ToString(), Br(CAccent),
                $"Open: {openActions}\nOverdue: {overdueActions}\nClosed: {closedActions}"));
            metGrid.Children.Add(MakeKPICard("CLOSE RATE", $"{actionCloseRate:F0}%",
                actionCloseRate >= 80 ? Br(CGreen) : actionCloseRate >= 50 ? Br(CAmber) : Br(CRed),
                "Percentage of action items closed vs total created"));
            metGrid.Children.Add(MakeKPICard("OVERDUE", overdueActions.ToString(),
                overdueActions == 0 ? Br(CGreen) : Br(CRed),
                $"Action items past their due date\nRequires immediate follow-up"));
            metStack.Children.Add(metGrid);

            metricsCard.Child = metStack;
            stack.Children.Add(metricsCard);

            scroll.Content = stack;
            return scroll;
        }

        // ── Automation rule helper ──
        private UIElement MakeAutomationRule(string title, string status, bool actionable, string actionTag, string tooltip)
        {
            var border = new Border
            {
                BorderBrush = Br(actionable ? CAccent : CBorder),
                BorderThickness = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 2, 0, 2),
                ToolTip = tooltip
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textStack = new StackPanel();
            textStack.Children.Add(new TextBlock
            {
                Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = actionable ? Br(CAccent) : Brushes.Black
            });
            textStack.Children.Add(new TextBlock
            {
                Text = status, FontSize = 10, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap
            });
            grid.Children.Add(textStack);

            if (actionable)
            {
                var btn = MakeActionButton("Run", actionTag, Br(CAccent), tooltip);
                btn.VerticalAlignment = VerticalAlignment.Center;
                btn.Height = 24; btn.FontSize = 10;
                Grid.SetColumn(btn, 1);
                grid.Children.Add(btn);
            }
            else
            {
                var check = new TextBlock
                {
                    Text = "\u2714", FontSize = 14, Foreground = Br(CGreen),
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                Grid.SetColumn(check, 1);
                grid.Children.Add(check);
            }

            border.Child = grid;
            return border;
        }

        // ── Meeting data helpers ──
        private class MeetingInfo { public string Title; public string Type; public string Date; public string Status; public int Attendees; }
        private class ActionItemInfo { public string Description; public string Assignee; public string DueDate; public string Status; public bool IsOverdue; }

        private List<MeetingInfo> LoadMeetings()
        {
            var result = new List<MeetingInfo>();
            try
            {
                string dir = System.IO.Path.GetDirectoryName(_data.FilePath ?? "");
                if (string.IsNullOrEmpty(dir)) return result;
                string path = System.IO.Path.Combine(dir, "_bim_manager", "meetings.json");
                if (!File.Exists(path)) return result;
                var arr = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(path));
                foreach (var item in arr)
                {
                    result.Add(new MeetingInfo
                    {
                        Title = item.Value<string>("title") ?? item.Value<string>("type") ?? "Meeting",
                        Type = item.Value<string>("type") ?? "",
                        Date = item.Value<string>("date") ?? "",
                        Status = item.Value<string>("status") ?? "PLANNED",
                        Attendees = (item["attendees"] as Newtonsoft.Json.Linq.JArray)?.Count ?? 0
                    });
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"LoadMeetings: {ex.Message}"); }
            return result;
        }

        private List<ActionItemInfo> LoadActionItems()
        {
            var result = new List<ActionItemInfo>();
            try
            {
                string dir = System.IO.Path.GetDirectoryName(_data.FilePath ?? "");
                if (string.IsNullOrEmpty(dir)) return result;
                string path = System.IO.Path.Combine(dir, "_bim_manager", "meetings.json");
                if (!File.Exists(path)) return result;
                var arr = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(path));
                foreach (var mtg in arr)
                {
                    var actions = mtg["action_items"] as Newtonsoft.Json.Linq.JArray;
                    if (actions == null) continue;
                    foreach (var act in actions)
                    {
                        string due = act.Value<string>("due") ?? "";
                        bool overdue = false;
                        if (DateTime.TryParse(due, out DateTime dueDate))
                            overdue = dueDate < DateTime.Now;
                        string st = act.Value<string>("status") ?? "OPEN";
                        if (st == "CLOSED") overdue = false;
                        result.Add(new ActionItemInfo
                        {
                            Description = act.Value<string>("description") ?? "",
                            Assignee = act.Value<string>("assignee") ?? "",
                            DueDate = due.Length > 10 ? due.Substring(0, 10) : due,
                            Status = st,
                            IsOverdue = overdue
                        });
                    }
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"LoadActionItems: {ex.Message}"); }
            return result;
        }

        // ════════════════════════════════════════════════════════════════
        //  STATIC SHOW METHOD
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Show the BIM Coordination Center as a modeless window.
        /// Actions are dispatched via ActionDispatcher (ExternalEvent).
        /// </summary>
        internal static void Show(CoordData data)
        {
            if (CurrentInstance != null)
            {
                CurrentInstance.Activate();
                CurrentInstance.Focus();
                return;
            }
            var dlg = new BIMCoordinationCenter(data);
            dlg.Show();
        }
    }
}
