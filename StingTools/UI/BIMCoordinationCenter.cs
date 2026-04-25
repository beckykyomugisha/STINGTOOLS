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
        private const string TabPermissions    = "PERMISSIONS";    // legacy alias → PROJECT MEMBERS
        private const string TabTeam           = "TEAM";           // legacy alias → PROJECT MEMBERS
        private const string TabProjectMembers = "PROJECT MEMBERS";
        private const string TabCoordLog       = "COORD LOG";

        // ── Data ──
        internal CoordData _data;
        private readonly ContentControl _contentArea;
        private readonly TextBlock _statusBar;
        private readonly StackPanel _navPanel;
        private Button _activeNav;

        // Phase 75: Persist last-viewed tab across dialog reopens
        private static string _lastViewedTab = TabOverview;

        // Phase 77 Item 11A: Current tab name for keyboard F5 refresh
        private string _currentTab;

        // Phase 76: Static warning element IDs selected from BCC Warnings DataGrid (stored as long values)
        public static IReadOnlyList<long> SelectedWarningIds { get; private set; } = new List<long>();

        internal static void SetSelectedWarningIds(IEnumerable<long> ids)
            => SelectedWarningIds = new List<long>(ids ?? Array.Empty<long>());

        // Phase 76: Last shown permissions (for SavePermissionsInline)
        private static List<RoleDefinition>   _lastPermissionsRoles   = new();
        private static List<FolderPermission> _lastPermissionsFolders = new();

        internal static List<RoleDefinition>   GetLastPermissionsRoles()   => _lastPermissionsRoles.Count   > 0 ? _lastPermissionsRoles   : GetDefaultRoles();
        internal static List<FolderPermission> GetLastPermissionsFolders() => _lastPermissionsFolders.Count > 0 ? _lastPermissionsFolders : GetDefaultFolderPermissions();

        /// <summary>Phase 103: return the live WarningRow list from the currently
        /// open BCC instance (populated by BuildCoordData). Used by the
        /// ZoomToWarning_ dispatch handler so it matches against real
        /// FailingElement IDs instead of hitting doc.GetWarnings() twice.</summary>
        internal static List<WarningRow> GetLastCoordWarnings()
            => CurrentInstance?._data?.Warnings ?? new List<WarningRow>();

        // Phase 76: Singleton for modeless BCC
        public static BIMCoordinationCenter CurrentInstance { get; private set; }

        // Phase 76: 4D/5D inline panel area
        private ContentControl _4dPanelArea;

        // Phase 76 Item 8: Revisions inline panel area
        private ContentControl _revPanelArea;

        // Phase 76 Item 9: Workflow tab preset panel area
        private ContentControl _workflowPanelArea;

        // Phase 76 Item 14: Issues tab dynamic context area
        private ContentControl _issueContextArea;

        // Phase 77 Item 4: Platform tab detail area
        private ContentControl _platformDetailArea;

        // Phase 77 Item 7: Model Health action panel area
        private ContentControl _modelHealthActionArea;

        // Phase 78 Section 1.1: Additional inline panel ContentControl fields (reserved for future tab detail areas)
#pragma warning disable CS0169 // Field is not used yet — reserved for future inline detail panels
        private ContentControl _meetingDetailArea;       // Meetings tab inline detail
        private ContentControl _meetingMinutesArea;      // Minutes editor area
        private ContentControl _meetingActionsArea;      // Action items area
        private ContentControl _deliverableDetailArea;   // Deliverables detail
        private ContentControl _qaDetailArea;            // QA detail panels
        private ContentControl _overviewDetailArea;      // Overview drill-down
        private ContentControl _warningsDetailArea;      // Warnings full inline panel
        private ContentControl _coordLogDetailArea;      // Coord log filter + detail
        private ContentControl _permissionsDetailArea;   // Permissions inline
        private ContentControl _planscapeDetailArea;      // Planscape hub detail
#pragma warning restore CS0169

        // Phase 76: Delegate set by BIMCoordinationCenterCommand to dispatch actions via ExternalEvent
        internal static Action<string> ActionDispatcher { get; set; }

        private void DispatchAction(string action)
        {
            ActionDispatcher?.Invoke(action);
            Dispatcher.BeginInvoke(new Action(() => { Activate(); Focus(); }),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        // Phase 78 Section 2.1: Canonical ISO 19650-aligned issue type registry
        // Single source of truth — all dropdowns, brushes, and wizards derive from this.
        internal static readonly (string Code, string Label, string Description, Color Colour)[] IsoIssueTypes =
        {
            // Information Management (ISO 19650 Part 1 & 2)
            ("NCR",          "Non-Conformance Report",           "Element/data does not meet EIR or BEP requirements",          Color.FromRgb(0xC6,0x28,0x28)),
            ("RFI",          "Request for Information",          "Clarification required from appointing party or designer",     Color.FromRgb(0x15,0x65,0xC0)),
            ("RFA",          "Request for Approval",             "Formal approval request for design or information model",      Color.FromRgb(0x1A,0x23,0x7E)),
            ("TQ",           "Technical Query",                  "Technical question requiring formal documented response",       Color.FromRgb(0x00,0x69,0x7C)),
            ("SI",           "Site Instruction",                 "Instruction issued to contractor on site",                     Color.FromRgb(0xE8,0x91,0x2D)),
            ("EWN",          "Early Warning Notice",             "Risk or issue likely to affect time, cost or quality",         Color.FromRgb(0xF5,0x7F,0x17)),
            ("CE",           "Compensation Event",               "Change event with cost or programme implications",             Color.FromRgb(0x6A,0x1B,0x9A)),
            ("VO",           "Variation Order",                  "Formal instruction to vary contract works",                    Color.FromRgb(0x6A,0x1B,0x9A)),
            ("AI",           "Architect's Instruction",          "Formal instruction from lead designer/architect",              Color.FromRgb(0x00,0x60,0x64)),
            ("PMI",          "Project Manager's Instruction",    "Instruction issued by project manager",                        Color.FromRgb(0x2E,0x7D,0x32)),
            ("CVI",          "Confirmation of Verbal Instruction","Written record of previously verbal instruction",             Color.FromRgb(0x37,0x47,0x4F)),
            // Coordination & Clash
            ("CLASH",        "Clash / Interference",             "Hard or soft clash between discipline models",                 Color.FromRgb(0xC6,0x28,0x28)),
            ("COORDINATION", "Coordination Issue",               "Cross-discipline coordination gap or conflict",                Color.FromRgb(0x15,0x65,0xC0)),
            ("DESIGN",       "Design Issue",                     "Design intent unclear, incomplete, or conflicting",            Color.FromRgb(0x1A,0x23,0x7E)),
            ("SPATIAL",      "Spatial / Clearance",              "Insufficient clearance, access, or maintenance space",         Color.FromRgb(0x00,0x69,0x7C)),
            // Data & Compliance
            ("DATA",         "Data Integrity Issue",             "Missing, incorrect, or inconsistent parameter data",           Color.FromRgb(0xE8,0x91,0x2D)),
            ("COMPLIANCE",   "Compliance Issue",                 "Non-compliance with ISO 19650, EIR, or BEP requirements",      Color.FromRgb(0xC6,0x28,0x28)),
            ("NAMING",       "Naming Convention Issue",          "Document or element naming does not follow convention",        Color.FromRgb(0xF5,0x7F,0x17)),
            ("LOD",          "Level of Detail Issue",            "LOD/LOI not achieved for current RIBA stage",                  Color.FromRgb(0x6A,0x1B,0x9A)),
            // Construction / Site
            ("SNAGGING",     "Snagging / Defect",                "Construction defect requiring rectification",                  Color.FromRgb(0xC6,0x28,0x28)),
            ("SITE",         "Site Observation",                 "General site observation or non-critical note",                Color.FromRgb(0x45,0x50,0x6E)),
            ("SAFETY",       "Health & Safety",                  "H&S risk, near-miss, or CDM-notifiable issue",                 Color.FromRgb(0xC6,0x28,0x28)),
            ("ENVIRONMENTAL","Environmental",                    "Environmental impact, sustainability, or ecology issue",        Color.FromRgb(0x2E,0x7D,0x32)),
            ("ACCESSIBILITY","Accessibility / DDA",              "Access, inclusion, or Building Regulations Part M issue",      Color.FromRgb(0x00,0x69,0x7C)),
            // Structural / MEP
            ("STRUCTURAL",   "Structural Issue",                 "Structural integrity, loading, or connection concern",         Color.FromRgb(0x37,0x47,0x4F)),
            ("MEP",          "MEP Coordination",                 "Mechanical, electrical, or plumbing routing conflict",         Color.FromRgb(0x15,0x65,0xC0)),
            ("FIRE",         "Fire Strategy",                    "Passive/active fire protection, compartmentation concern",      Color.FromRgb(0xC6,0x28,0x28)),
            // Programme & Cost
            ("PROGRAMME",    "Programme / Schedule",             "Activity delay, float erosion, or milestone at risk",          Color.FromRgb(0xF5,0x7F,0x17)),
            ("COST",         "Cost / Commercial",                "Budget overrun, cost query, or BOQ discrepancy",               Color.FromRgb(0x6A,0x1B,0x9A)),
            ("RISK",         "Risk Register Item",               "Project risk requiring monitoring or mitigation",               Color.FromRgb(0xE8,0x91,0x2D)),
            // Handover / FM
            ("HANDOVER",     "Handover Issue",                   "FM handover, O&M, or COBie data completeness issue",           Color.FromRgb(0x00,0x60,0x64)),
            ("BIM",          "BIM Process Issue",                "BIM workflow, platform, or model management issue",            Color.FromRgb(0x1A,0x23,0x7E)),
            // Admin
            ("CLIENT",       "Client Comment",                   "Comment or query raised by the appointing party",              Color.FromRgb(0x45,0x50,0x6E)),
            ("AUTHORITY",    "Authority / Planning",             "Regulatory, planning, or building control query",              Color.FromRgb(0x37,0x47,0x4F)),
            ("CHANGE",       "Change Request",                   "Requested change to scope, design, or specification",          Color.FromRgb(0x6A,0x1B,0x9A)),
            ("ACTION",       "Action Item",                      "Meeting action requiring follow-up (not a formal issue)",      Color.FromRgb(0x45,0x50,0x6E)),
            ("COMMENT",      "General Comment",                  "Informal observation or comment (no formal response required)",Color.FromRgb(0x78,0x90,0x9C)),
        };

        // Phase 78 Section 3.1: Default ISO 19650 team roles for issue assignment grid
        private static readonly (string Role, string Group)[] DefaultIssueAssignees =
        {
            // Delivery Team — Lead
            ("BIM Manager",               "Lead"),
            ("Information Manager",       "Lead"),
            ("Lead Designer / Architect", "Lead"),
            ("Project Manager",           "Lead"),
            ("Design Manager",            "Lead"),
            // Architecture & Interior
            ("Architect",                 "Architecture"),
            ("Interior Designer",         "Architecture"),
            ("Landscape Architect",       "Architecture"),
            // Structure
            ("Structural Engineer",       "Structure"),
            ("Geotechnical Engineer",     "Structure"),
            // MEP
            ("MEP Engineer",              "MEP"),
            ("Mechanical Engineer",       "MEP"),
            ("Electrical Engineer",       "MEP"),
            ("Plumbing Engineer",         "MEP"),
            ("Fire Protection Engineer",  "MEP"),
            ("Acoustic Engineer",         "MEP"),
            // Specialist
            ("Façade Engineer",           "Specialist"),
            ("Vertical Transport",        "Specialist"),
            ("Sustainability Consultant", "Specialist"),
            ("BREEAM Assessor",           "Specialist"),
            ("Security Consultant",       "Specialist"),
            // Contractor
            ("Main Contractor",           "Contractor"),
            ("Site Manager",              "Contractor"),
            ("Sub-Contractor",            "Contractor"),
            ("MEP Contractor",            "Contractor"),
            ("Steel Fabricator",          "Contractor"),
            // QA / Compliance
            ("QA/QC Manager",             "Compliance"),
            ("CDM Coordinator",           "Compliance"),
            ("Health & Safety",           "Compliance"),
            ("Building Control Officer",  "Compliance"),
            // Client / FM
            ("Client / Employer",         "Client"),
            ("Facilities Manager",        "Client"),
            ("Asset Manager",             "Client"),
            ("Planning Officer",          "Client"),
        };

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
            // Phase 77: Structured warning list for inline tree view
            public List<WarningRow> Warnings = new();

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

            // Phase 77 Item 6: Meetings and action items
            public List<MeetingRow> Meetings { get; set; } = new();
            public List<ActionItemRow> ActionItems { get; set; } = new();
        }

        /// <summary>Phase 77 Item 6: Meeting data row for inline grid.</summary>
        internal class MeetingRow
        {
            public string MeetingId { get; set; }
            public string Title { get; set; }
            public string Type { get; set; }
            public string Date { get; set; }
            public string Time { get; set; }
            public string Location { get; set; }
            public string Status { get; set; }
            public string Agenda { get; set; }
            public int Attendees { get; set; }
            // Phase 98: Chair must be a string (team member name). The DataGrid
            // previously bound the Chair column to Attendees (int) which rendered
            // the numeric attendee count instead of a person's name.
            public string Chair { get; set; }
        }

        /// <summary>Phase 77 Item 6: Action item data row for inline grid.</summary>
        internal class ActionItemRow
        {
            public string ActionId { get; set; }
            public string Description { get; set; }
            public string Owner { get; set; }
            public string DueDate { get; set; }
            public string Priority { get; set; }
            public string Status { get; set; }
            public string MeetingRef { get; set; }
            public bool IsOverdue { get; set; }
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
            public string Location  { get; set; }
            public string RaisedBy  { get; set; }
            /// <summary>Display string: shows first assignee + N more if multi-assigned.</summary>
            public string AssigneeDisplay => AssigneeList.Count <= 1 ? (Assignee ?? "") :
                $"{AssigneeList[0]} +{AssigneeList.Count - 1}";
        }

        /// <summary>Phase 48: Revision data row for DataGrid display.</summary>
        internal class RevisionRow
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Number { get; set; }       // ISO revision code e.g. P01, C02, A
            public string IsoCode { get; set; }      // Full ISO code with series label e.g. "P01 — Preliminary"
            public string Series { get; set; }       // Preliminary / Construction / As-Built
            public string Date { get; set; }
            public string Description { get; set; }
            public string Author { get; set; }
            public string Discipline { get; set; }   // Originating discipline: A / S / M / E / P / FP / LV / G / ALL
            public string Sheets { get; set; }       // Comma-separated sheet numbers
            public int Clouds { get; set; }
            public string Status { get; set; }
        }

        /// <summary>Phase 49: Deliverable tracking row.</summary>
        internal class DeliverableRow
        {
            public string Code { get; set; }           // ISO 19650 file code
            public string Name { get; set; }
            public string Discipline { get; set; }     // ISO discipline code: A/S/M/E/P/FP/LV/G/C/L/ALL
            public string Type { get; set; }           // Model/Drawing/Schedule/Report/Data
            public string DataDrop { get; set; }       // DD1/DD2/DD3/DD4
            public string Status { get; set; }         // Pending/In Progress/Submitted/Approved/Rejected
            public string Suitability { get; set; }    // S0-S7
            public string CDE { get; set; }            // WIP/SHARED/PUBLISHED/ARCHIVE
            public string Owner { get; set; }
            public string DueDate { get; set; }
            public bool IsOverdue { get; set; }

            // ── v1.0 template-engine fields (S02) ──
            public string DocNumber { get; set; }
            public string Revision { get; set; }
            public string FunctionalBreakdown { get; set; } = "ZZ";
            public string SpatialBreakdown { get; set; } = "XX";
            public string Originator { get; set; } = "PLNS";
            public string RoleCode { get; set; }
            public string ContractorRef { get; set; }
            public string System { get; set; }
            public string Subsystem { get; set; }
            public string EquipmentType { get; set; }
            public string IssuedBy { get; set; }
            public string ReviewedBy { get; set; }
            public string ApprovedBy { get; set; }
            public string Supersedes { get; set; }
            public string SupersededBy { get; set; }
            public List<RevisionHistoryEntry> RevisionHistory { get; set; } = new List<RevisionHistoryEntry>();
            public List<HoldEntry> Holds { get; set; } = new List<HoldEntry>();
            public List<ReferenceEntry> References { get; set; } = new List<ReferenceEntry>();

            // ── v1.1 additions (workflow, signature, cross-links) ──
            public string WorkflowState { get; set; }
            public string AssignedTo { get; set; }
            public DateTime? SlaDeadline { get; set; }
            public List<WorkflowHistoryEntry> WorkflowHistory { get; set; } = new List<WorkflowHistoryEntry>();
            public string FileHashSha256 { get; set; }
            public List<string> Tags { get; set; } = new List<string>();
            public List<string> RelatedTransmittalIds { get; set; } = new List<string>();
            public List<string> RelatedRfiIds { get; set; } = new List<string>();
            public bool RequiresSignature { get; set; }
            public string SignatureStatus { get; set; } = "None";
            public string SignedFilePath { get; set; }
        }

        /// <summary>Template engine v1.0 — revision history entry captured in DeliverableRow.RevisionHistory.</summary>
        internal class RevisionHistoryEntry
        {
            public string Revision { get; set; }
            public string Suitability { get; set; }
            public string Timestamp { get; set; }
            public string User { get; set; }
            public string Reason { get; set; }
            public string TemplateId { get; set; }
            public string RenderedFilePath { get; set; }
        }

        /// <summary>Template engine v1.0 — hold entry captured in DeliverableRow.Holds.</summary>
        internal class HoldEntry
        {
            public string Id { get; set; }
            public string Description { get; set; }
            public string RaisedBy { get; set; }
            public string RaisedAt { get; set; }
            public string ClearedBy { get; set; }
            public string ClearedAt { get; set; }
            public bool IsOpen { get; set; } = true;
        }

        /// <summary>Template engine v1.0 — reference row captured in DeliverableRow.References.</summary>
        internal class ReferenceEntry
        {
            public string RefType { get; set; }    // "Drawing", "Spec", "Standard", "RFI", "External"
            public string RefId { get; set; }
            public string RefTitle { get; set; }
            public string RefUrl { get; set; }
        }

        /// <summary>Template engine v1.1 — workflow history entry captured in DeliverableRow.WorkflowHistory.</summary>
        internal class WorkflowHistoryEntry
        {
            public string Timestamp { get; set; }
            public string FromState { get; set; }
            public string ToState { get; set; }
            public string Action { get; set; }
            public string User { get; set; }
            public string Comment { get; set; }
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
            // Phase 77: Extended member directory fields
            public string Company     { get; set; }
            public string Discipline  { get; set; }
            public string Email       { get; set; }
            public string Phone       { get; set; }
            public string CDEUsername { get; set; }
            public bool   CanApprove  { get; set; }
            public bool   CanIssue    { get; set; }
            public bool   Active      { get; set; } = true;
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
            public string SLAHours { get; set; }        // Phase 77: SLA hours for this role (e.g. "24")
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

        /// <summary>Phase 77: Structured Revit warning row for inline Warnings tree.</summary>
        internal class WarningRow
        {
            public string Id          { get; set; }   // Revit FailureDefinitionId or GUID
            public string Description { get; set; }   // Warning message text
            public string Category    { get; set; }   // Matches WarningCategory enum name
            public string Severity    { get; set; }   // Critical / High / Medium / Low / Info
            public int    ElementCount{ get; set; }   // Number of affected elements
            public bool   AutoFixable { get; set; }   // Can be auto-fixed
            public string FixStrategy { get; set; }   // Description of fix strategy
            public List<long> ElementIds { get; set; } = new List<long>(); // Affected element IDs
        }

        // ════════════════════════════════════════════════════════════════
        //  CONSTRUCTOR
        // ════════════════════════════════════════════════════════════════

        private BIMCoordinationCenter(CoordData data)
        {
            _data = data;
            // Phase 104: Rebranded — drop "STING" prefix per user request. The window is now
            // titled just "BIM Coordination Center" so it reads as a role-based tool rather
            // than a product feature. "STINGTOOLS BCC" appears in logs/audit trail only.
            Title = "BIM Coordination Center";
            Width = 1200; Height = 820;
            MinWidth = 900; MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Br(CPageBg);
            FontFamily = new FontFamily("Segoe UI");
            ResizeMode = ResizeMode.CanResizeWithGrip;

            // Phase 101: fix BCC z-order (BCC was dropping behind Revit when the
            // Revit main window was clicked).
            //
            // WindowInteropHelper.Owner MUST be set BEFORE the window's HWND is
            // realised — i.e. during SourceInitialized, not Loaded. Setting it
            // in Loaded (Phase 98) was too late: by then the HWND was already
            // parented to the desktop root, so Windows did not keep it z-ordered
            // above the Revit main window. Moving the owner assignment to
            // SourceInitialized means BCC behaves as a true child of Revit and
            // stays above it for the life of the window.
            this.SourceInitialized += (_, _) =>
            {
                try
                {
                    var revitHwnd = NativeMethods.FindWindow("Rvt_MainWindow", null);
                    var handle = revitHwnd != IntPtr.Zero
                        ? revitHwnd
                        : System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                    if (handle != IntPtr.Zero)
                        new System.Windows.Interop.WindowInteropHelper(this).Owner = handle;
                }
                catch (Exception exOwner) { StingLog.Warn($"BIMCoordCenter SourceInitialized owner: {exOwner.Message}"); }
            };
            this.Loaded += (_, _) =>
            {
                // Pulse Topmost briefly so the BCC is brought to the front after
                // Revit re-activates during startup. Leaving Topmost=true would
                // force BCC above every Windows app (including browsers), which
                // the user doesn't want — so we flip it back off on the next tick.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Topmost = true; Activate(); Focus(); Topmost = false;
                }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            };

            // Phase 104: when BCC regains activation (e.g. user alt-tabs back
            // into it, or a child window closes), pulse Topmost briefly so it
            // jumps over Revit's main window. Without this, closing a child
            // sometimes leaves Revit's main window on top because the z-order
            // fell down the chain Revit → BCC during the child's lifetime.
            this.Activated += (_, _) =>
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!IsActive) return;
                        Topmost = true; Topmost = false;
                    }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
                catch { /* best effort */ }
            };

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

            // Keyboard shortcuts (Phase 77 Item 11A)
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    // Clear any active inline panel areas
                    _4dPanelArea?.SetCurrentValue(ContentControl.ContentProperty, null);
                    _revPanelArea?.SetCurrentValue(ContentControl.ContentProperty, null);
                    _workflowPanelArea?.SetCurrentValue(ContentControl.ContentProperty, null);
                    _modelHealthActionArea?.SetCurrentValue(ContentControl.ContentProperty, null);
                    _issueContextArea?.SetCurrentValue(ContentControl.ContentProperty, null);
                    e.Handled = true;
                }
                if (e.Key == Key.F5)
                {
                    // Phase 101: F5 now triggers a full reload (rebuild CoordData
                    // on the API thread, re-render the current tab) — same as
                    // the Refresh button on the header. Previously F5 only
                    // re-rendered the cached tab without refreshing model data.
                    ReloadAll();
                    e.Handled = true;
                }
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
            Closed += OnClosed;
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
                Text = "BIM COORDINATION CENTER",
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

            // Right: Refresh + RAG indicator + compliance + date
            var rightStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(rightStack, 1);

            // Phase 101: Refresh button FIRST in the right stack so it sits to
            // the LEFT of the compliance % per user feedback. Rebuilds
            // CoordData on the Revit API thread via ExternalEvent, wipes the
            // tab cache so EVERY tab (not just the current one) re-renders on
            // next visit, and shows the current tab with fresh data
            // immediately. Same behaviour as F5.
            var refreshBtn = new Button
            {
                Content = "\u21BB  Refresh",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Background = Br(Color.FromRgb(0x1E, 0x88, 0xE5)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 14, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Reload every BCC tab from the current model state (F5 also works). Rebuilds compliance, warnings, issues, revisions, workflows, QA, 4D/5D, deliverables, meetings, team, and coord log."
            };
            refreshBtn.Click += (s, e) => ReloadAll();
            rightStack.Children.Add(refreshBtn);

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
            wrap.Children.Add(MakeShareButton("📖 Code Legend", "CodeLegend"));

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

            string[] tabs = { TabOverview, TabModelHealth, TabWarnings, TabIssues, TabRevisions, TabPlatform, TabWorkflows, TabQA, Tab4D5D, TabDeliverables, TabMeetings, TabProjectMembers, TabCoordLog };
            int memberCount = _data.Roles.Count + _data.TeamMembers.Count;
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
                memberCount > 0 ? memberCount.ToString() : "", // PROJECT MEMBERS
                _data.CoordLog.Count > 0 ? _data.CoordLog.Count.ToString() : ""
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
            // Phase 77 Item 11A: Track current tab for F5 refresh
            _currentTab = tabName;

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
                    TabPermissions    => BuildProjectMembersTab(),  // legacy alias
                    TabTeam           => BuildProjectMembersTab(),  // legacy alias
                    TabProjectMembers => BuildProjectMembersTab(),
                    TabCoordLog       => BuildCoordLogTab(),
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

            // Phase 91 H3: Forecast KPI card
            try
            {
                string forecastDate = Core.ComplianceTrendTracker.ForecastCompletionDate(StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document, _data.RAGGreenThreshold);
                var forecastGrid = new UniformGrid { Columns = 1, Margin = new Thickness(0, 0, 0, 8) };
                forecastGrid.Children.Add(MakeKPICard("FORECAST", forecastDate,
                    Br(Color.FromRgb(0x00, 0x69, 0x7C)),
                    $"Projected date to reach {_data.RAGGreenThreshold:F0}% compliance (linear regression on workflow history)"));
                stack.Children.Add(forecastGrid);
            }
            catch { /* forecast non-critical */ }

            // RAG compliance bar
            stack.Children.Add(MakeRAGBar("Tag Compliance", _data.TagPct));
            stack.Children.Add(MakeRAGBar("Strict (Fully Resolved)", _data.StrictPct));
            stack.Children.Add(new Border { Height = 12 });

            // Phase 77 Item 11E: Quick Actions toolbar
            stack.Children.Add(MakeSectionHeader("QUICK ACTIONS"));
            var qaWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            qaWrap.Children.Add(MakeActionButton("▶ Morning Check",    "RunWorkflow_MorningHealthCheck", Br(CHeaderBg), "Run Morning Check workflow (8 steps)"));
            qaWrap.Children.Add(MakeActionButton("⚡ Auto-Fix Warnings", "AutoFixWarnings", Br(CAccent), "Auto-fix all auto-fixable warnings"));
            qaWrap.Children.Add(MakeActionButton("✓ Run Compliance",   "CompletenessDashboard", Br(CGreen), "Run tag completeness compliance check"));
            qaWrap.Children.Add(MakeActionButton("⚑ Create Issue",    "RaiseIssue", Br(CRed), "Raise a new issue"));
            qaWrap.Children.Add(MakeActionButton("📅 New Meeting",     "NewMeeting", Br(Color.FromRgb(0x6A, 0x1B, 0x9A)), "Schedule a new BIM coordination meeting"));
            stack.Children.Add(qaWrap);

            // Extended Quick Actions
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

            // Phase 106: Coordination checks — surface rule-based clash, clearance and naming audits
            // on the BCC Overview. Buttons dispatch through ActionDispatcher → BCCActionEventHandler →
            // DispatchCoordAction → WorkflowEngine.GetCommandInstance, which resolves each tag to the
            // matching StingTools.Temp command.
            stack.Children.Add(MakeSectionHeader("COORDINATION CHECKS"));
            var clashWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            clashWrap.Children.Add(MakeActionButton("Run Clash Detection", "ClashDetection", Br(CRed),
                "Rule-based MEP vs structural clash on the active model. Writes JSON report to 12_CLASHES."));
            clashWrap.Children.Add(MakeActionButton("Cross-Model Clash", "CrossModelClash", Br(Color.FromRgb(0xAD, 0x14, 0x57)),
                "Host MEP vs linked-model structural clash using each link's total transform."));
            clashWrap.Children.Add(MakeActionButton("MEP Clearance", "MEPClearance", Br(CAmber),
                "CIBSE Guide W / BS EN 12237 clearance audit: 200 mm ducts, 150 mm pipes. CSV report."));
            clashWrap.Children.Add(MakeActionButton("Naming Audit", "NamingAudit", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "BS 1192 / ISO 19650 view, sheet and workset naming audit. TSV report in .bimmanager."));
            stack.Children.Add(clashWrap);

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

            // Phase 77 Item 8: QR Codes section
            stack.Children.Add(new Border { Height = 12 });
            stack.Children.Add(MakeSectionHeader("QR CODES"));
            var qrWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            qrWrap.Children.Add(MakeActionButton("Generate QR for Selected", "GenerateQRCode", Br(CHeaderBg), "Generate QR code PNG for selected elements (encodes ASS_TAG_1_TXT)"));
            qrWrap.Children.Add(MakeActionButton("QR Sheet Register", "GenerateQRSheet", Br(CGreen), "Generate a sheet of QR codes for all tagged elements"));
            qrWrap.Children.Add(MakeActionButton("Print QR Tags", "PrintQRTags", Br(CAccent), "Print QR code label sheet for FM/handover use"));
            stack.Children.Add(qrWrap);
            var qrResultArea = new System.Windows.Controls.Image { Width = 150, Height = 150, Margin = new Thickness(0, 4, 0, 0), Stretch = System.Windows.Media.Stretch.Uniform };
            var qrCard = MakeCard();
            qrCard.Child = qrResultArea;
            stack.Children.Add(qrCard);

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
                detailsMi.Click += (s, e) => { ShowInlineStatus(_modelHealthActionArea, check, detailText); };
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

            // Phase 77 Item 7: Quick access buttons to show inline action panels
            stack.Children.Add(new Border { Height = 8 });
            stack.Children.Add(MakeSectionHeader("INLINE REPAIR PANELS"));
            var inlineActWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            var fixWarnPnlBtn = new Button { Content = "Fix Warnings Panel", Height = 28, Padding = new Thickness(10, 0, 10, 0), Background = Br(CRed), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
            var fixStalePanel = new Button { Content = "Fix Stale Panel", Height = 28, Padding = new Thickness(10, 0, 10, 0), Background = Br(Color.FromRgb(0x6A, 0x1B, 0x9A)), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
            var valTagsPanel = new Button { Content = "Validate Tags Panel", Height = 28, Padding = new Thickness(10, 0, 10, 0), Background = Br(CGreen), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
            var fixContPanel = new Button { Content = "Fix Containers Panel", Height = 28, Padding = new Thickness(10, 0, 10, 0), Background = Br(CAccent), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand };
            fixWarnPnlBtn.Click += (s, e) => ShowModelHealthAction("FixWarnings");
            fixStalePanel.Click += (s, e) => ShowModelHealthAction("FixStaleElements");
            valTagsPanel.Click += (s, e) => ShowModelHealthAction("ValidateTags");
            fixContPanel.Click += (s, e) => ShowModelHealthAction("FixContainers");
            inlineActWrap.Children.Add(fixWarnPnlBtn);
            inlineActWrap.Children.Add(fixStalePanel);
            inlineActWrap.Children.Add(valTagsPanel);
            inlineActWrap.Children.Add(fixContPanel);
            stack.Children.Add(inlineActWrap);

            // Phase 77 Item 7: Inline action panel area
            _modelHealthActionArea = new ContentControl { MinHeight = 160 };
            stack.Children.Add(_modelHealthActionArea);

            scroll.Content = stack;
            return scroll;
        }

        /// <summary>Phase 77 Item 7: Show inline action panel in Model Health tab.</summary>
        private void ShowModelHealthAction(string action)
        {
            if (_modelHealthActionArea == null) return;
            var panel = new Border
            {
                Background = Br(CCardBg), BorderBrush = Br(CBorder),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16), Margin = new Thickness(0, 4, 0, 4)
            };
            var sp = new StackPanel();
            switch (action)
            {
                case "FixWarnings":
                {
                    sp.Children.Add(new TextBlock { Text = "Auto-Fix Warnings", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Br(CHeaderBg), Margin = new Thickness(0, 0, 0, 8) });
                    sp.Children.Add(new TextBlock { Text = $"Auto-fixable warnings: {_data.WarningAutoFixable} of {_data.WarningTotal} total", FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
                    var warnChecks = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
                    foreach (var wt in new[] { "Critical", "High", "Medium", "Low" })
                        warnChecks.Children.Add(new CheckBox { Content = wt, IsChecked = true, FontSize = 11, Margin = new Thickness(0, 0, 12, 4) });
                    sp.Children.Add(warnChecks);
                    var fixBtn = new Button { Content = "Run Auto-Fix", Height = 28, Padding = new Thickness(14, 0, 14, 0), Background = Br(CRed), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 12, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
                    fixBtn.Click += (s, e) => DispatchAction("AutoFixWarnings");
                    sp.Children.Add(fixBtn);
                    break;
                }
                case "FixStaleElements":
                {
                    sp.Children.Add(new TextBlock { Text = "Retag Stale Elements", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Br(CHeaderBg), Margin = new Thickness(0, 0, 0, 8) });
                    sp.Children.Add(new TextBlock { Text = $"Stale elements: {_data.StaleCount}", FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
                    var discChecks = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
                    foreach (var disc in new[] { "A", "S", "M", "E", "P", "FP" })
                        discChecks.Children.Add(new CheckBox { Content = disc, IsChecked = true, FontSize = 11, Margin = new Thickness(0, 0, 12, 4) });
                    sp.Children.Add(discChecks);
                    var retagBtn = new Button { Content = "Re-tag Stale", Height = 28, Padding = new Thickness(14, 0, 14, 0), Background = Br(Color.FromRgb(0x6A, 0x1B, 0x9A)), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 12, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
                    retagBtn.Click += (s, e) => DispatchAction("RetagStale");
                    sp.Children.Add(retagBtn);
                    break;
                }
                case "ValidateTags":
                {
                    sp.Children.Add(new TextBlock { Text = "Validate Tags", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Br(CHeaderBg), Margin = new Thickness(0, 0, 0, 8) });
                    sp.Children.Add(new TextBlock { Text = $"Tag coverage: {_data.TagPct:F1}%  |  Strict: {_data.StrictPct:F1}%", FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
                    var valDg = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, MaxHeight = 120, FontSize = 11, HeadersVisibility = DataGridHeadersVisibility.Column };
                    valDg.Columns.Add(new DataGridTextColumn { Header = "Discipline", Binding = new Binding("Key"), Width = 80 });
                    valDg.Columns.Add(new DataGridTextColumn { Header = "Coverage %", Binding = new Binding("Value.CompliancePct"), Width = 90 });
                    valDg.ItemsSource = _data.ByDisc;
                    sp.Children.Add(valDg);
                    var valBtn = new Button { Content = "Run Validation", Height = 28, Padding = new Thickness(14, 0, 14, 0), Background = Br(CGreen), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 12, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
                    valBtn.Click += (s, e) => DispatchAction("ValidateTags");
                    sp.Children.Add(valBtn);
                    break;
                }
                case "FixContainers":
                {
                    sp.Children.Add(new TextBlock { Text = "Fix Container Completion", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Br(CHeaderBg), Margin = new Thickness(0, 0, 0, 8) });
                    sp.Children.Add(new TextBlock { Text = $"Container completion: {_data.ContainerCompletePct:F0}%", FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
                    foreach (var disc in new[] { "Architecture", "Structural", "Mechanical", "Electrical" })
                    {
                        var barRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                        barRow.Children.Add(new TextBlock { Text = disc, Width = 100, FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
                        var pb = new ProgressBar { Width = 200, Height = 14, Value = _data.ContainerCompletePct, Maximum = 100 };
                        barRow.Children.Add(pb);
                        sp.Children.Add(barRow);
                    }
                    var fixCBtn = new Button { Content = "Fix Containers", Height = 28, Padding = new Thickness(14, 0, 14, 0), Background = Br(CAccent), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 12, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
                    fixCBtn.Click += (s, e) => DispatchAction("FixContainers");
                    sp.Children.Add(fixCBtn);
                    break;
                }
                default:
                {
                    sp.Children.Add(new TextBlock { Text = action, FontSize = 12 });
                    break;
                }
            }
            panel.Child = sp;
            _modelHealthActionArea.Content = panel;
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
            var state = new WarningsDashboardDialog.WarningsPanelState();

            // ── Root DockPanel ────────────────────────────────────────────
            var dock = new DockPanel { LastChildFill = true, Margin = new Thickness(12, 10, 12, 10) };

            // ── KPI STRIP (top, 8 cards) ──────────────────────────────────
            var kpiGrid = new UniformGrid { Columns = 8, Margin = new Thickness(0, 0, 0, 8) };
            kpiGrid.Children.Add(MakeKPICard("TOTAL", _data.WarningTotal.ToString(),
                _data.WarningTotal > 20 ? Br(CRed) : _data.WarningTotal > 5 ? Br(CAmber) : Br(CGreen),
                $"Total Revit warnings in model\nBaseline trend: {_data.WarningTrend}", "WarningsDashboard"));
            kpiGrid.Children.Add(MakeKPICard("CRITICAL", _data.WarningCritical.ToString(),
                _data.WarningCritical > 0 ? Br(CRed) : Br(CGreen),
                "Critical warnings block handover\nSLA: resolve within 4 hours", "AutoFixWarnings"));
            kpiGrid.Children.Add(MakeKPICard("HIGH", _data.WarningHigh.ToString(),
                _data.WarningHigh > 0 ? Br(CAmber) : Br(CGreen),
                "High-severity warnings affect deliverable quality\nSLA: resolve within 24 hours"));
            kpiGrid.Children.Add(MakeKPICard("MEDIUM", (_data.WarningTotal - _data.WarningCritical - _data.WarningHigh).ToString(),
                Br(CAmber), "Medium-severity warnings — resolve within 1 week"));
            kpiGrid.Children.Add(MakeKPICard("AUTO-FIX", _data.WarningAutoFixable.ToString(), Br(CGreen),
                $"One-click auto-fix available\n{_data.WarningManualReview} require manual review", "AutoFixWarnings"));
            kpiGrid.Children.Add(MakeKPICard("HEALTH", $"{_data.WarningHealthScore}/100",
                _data.WarningHealthScore >= 80 ? Br(CGreen) : _data.WarningHealthScore >= 50 ? Br(CAmber) : Br(CRed),
                "Weighted: Critical=-20, High=-5, Medium=-2, Low=-1 per warning from base 100"));
            kpiGrid.Children.Add(MakeKPICard("SLA BREACH", _data.WarningSLAViolations.ToString(),
                _data.WarningSLAViolations > 0 ? Br(CRed) : Br(CGreen),
                $"Warnings exceeding SLA threshold\nCritical=4h, High=24h\nAvg critical age: {_data.WarningAvgCriticalAgeHours:F1}h"));
            kpiGrid.Children.Add(MakeKPICard("GATE", _data.WarningGatePass ? "PASS" : "FAIL",
                _data.WarningGatePass ? Br(CGreen) : Br(CRed),
                _data.WarningGatePass ? "Warning gate: PASS — deliverable OK" : $"Warning gate: FAIL — {_data.WarningGateReason}"));
            DockPanel.SetDock(kpiGrid, Dock.Top);
            dock.Children.Add(kpiGrid);

            // ── BOTTOM ACTION BAR ─────────────────────────────────────────
            var actionBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            DockPanel.SetDock(actionBar, Dock.Bottom);
            var runBtn = new Button
            {
                Content = "▶  Run Selected Action", Padding = new Thickness(14, 5, 14, 5),
                Background = Br(CAccent), Foreground = Brushes.White, BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold, FontSize = 12, Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 10, 0), ToolTip = "Execute the highlighted operation from the panel below"
            };
            var statusBar = new TextBlock { FontSize = 11, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)), VerticalAlignment = VerticalAlignment.Center };
            state.StatusText = statusBar;
            runBtn.Click += (s, e) =>
            {
                string op = state.SelectedOperation ?? WarningsDashboardDialog.GetSelectedOperation();
                if (string.IsNullOrEmpty(op))
                {
                    statusBar.Text = "Select an operation from a tab below first.";
                    statusBar.Foreground = Br(CRed);
                }
                else { DispatchAction(op); }
            };
            actionBar.Children.Add(runBtn);
            actionBar.Children.Add(statusBar);
            dock.Children.Add(actionBar);

            // ── 6-TAB WARNINGS MANAGEMENT PANEL ──────────────────────────
            var innerTabs = new TabControl { Background = Br(CPageBg), BorderThickness = new Thickness(0) };
            innerTabs.Items.Add(WarningsDashboardDialog.BuildOverviewTab(state));
            innerTabs.Items.Add(WarningsDashboardDialog.BuildBrowseSelectTab(state));
            innerTabs.Items.Add(WarningsDashboardDialog.BuildAutoFixTab(state));
            innerTabs.Items.Add(WarningsDashboardDialog.BuildSelectInspectTab(state));
            innerTabs.Items.Add(WarningsDashboardDialog.BuildBaselineSLATab(state));
            innerTabs.Items.Add(WarningsDashboardDialog.BuildExportIntegrationTab(state));
            dock.Children.Add(innerTabs);   // LastChildFill — takes remaining space

            return dock;
        }

        // PLACEHOLDER for deleted old content — kept to allow compile:
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
            // Phase 76 Item 14: contextual buttons show inline panel in _issueContextArea
            var refreshBtn = new Button
            {
                Content = "↻ Refresh", Height = 26, Padding = new Thickness(10, 0, 10, 0),
                Background = Br(Color.FromRgb(0x45, 0x50, 0x6E)), Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Left
            };
            refreshBtn.Click += (s, e) => { if (_tabCache.ContainsKey(TabIssues)) { _tabCache.Remove(TabIssues); } NavigateTo(TabIssues); };
            root.Children.Add(refreshBtn);

            root.Children.Add(MakeSectionHeader("ACTIONS"));
            var actionGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            for (int i = 0; i < 4; i++) actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 3; i++) actionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var actions = new (string Label, string Tag, Color Clr, string Tip, bool HasCtx)[] {
                ("Raise Issue",       "RaiseIssue",              Color.FromRgb(0xC6, 0x28, 0x28), "Create new RFI/NCR/SI/Clash issue with element linking and BCF integration", true),
                ("Update Status",     "UpdateIssue",             Color.FromRgb(0xE8, 0x91, 0x2D), "Update issue status, priority, or assignees", true),
                ("Bulk Close",        "IssuesBulkClose",         Color.FromRgb(0x6A, 0x1B, 0x9A), "Close multiple resolved issues at once", true),
                ("Select Elements",   "SelectIssueElements",     Color.FromRgb(0x1A, 0x23, 0x7E), "Select model elements linked to the selected issue", false),
                ("BCF Export",        "BCFExport",               Color.FromRgb(0x00, 0x69, 0x7C), "Export issues as BCF 2.1 for ACC/Navisworks/BIMcollab", true),
                ("BCF Import",        "BCFImport",               Color.FromRgb(0x00, 0x69, 0x7C), "Import BCF issues from external clash/coordination tools", false),
                ("Export Excel",      "ExportIssues",            Color.FromRgb(0x45, 0x50, 0x6E), "Export full issue register (all fields) to Excel", false),
                ("From Warnings",     "CreateIssuesFromWarnings",Color.FromRgb(0xE6, 0x5C, 0x00), "Auto-create NCR/SI from critical/high Revit warnings", true),
                ("Issue Timeline",    "IssueTimeline",           Color.FromRgb(0x1A, 0x23, 0x7E), "View issue timeline with status changes and resolution history", false),
                ("Add to Meeting",    "AutoAgenda",              Color.FromRgb(0x00, 0x69, 0x7C), "Add open issues to next meeting agenda grouped by type/priority", false),
                ("Create Transmittal","CreateTransmittal",       Color.FromRgb(0x6A, 0x1B, 0x9A), "Create ISO 19650 transmittal linking issues to document exchange", false),
                ("Assign Issues",     "AssignIssues",            Color.FromRgb(0xE8, 0x91, 0x2D), "Multi-assign issues to team members by role/discipline", true)
            };
            for (int i = 0; i < actions.Length; i++)
            {
                var a = actions[i];
                Button btn;
                if (a.HasCtx)
                    btn = MakeIssueContextButton(a.Label, a.Tag, Br(a.Clr), a.Tip);
                else
                    btn = MakeActionButton(a.Label, a.Tag, Br(a.Clr), a.Tip);
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
                // Build discipline lookup from TeamMembers
                var memberDiscLookup = _data.TeamMembers
                    .Where(m => !string.IsNullOrEmpty(m.Name))
                    .ToDictionary(m => m.Name, m => m.Discipline ?? "", StringComparer.OrdinalIgnoreCase);

                // Header row
                var hdrRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                hdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                hdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                hdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                hdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                hdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
                hdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                AddCellToGrid(hdrRow, "Assignee", 0, 0, true);
                AddCellToGrid(hdrRow, "Disc", 0, 1, true);
                AddCellToGrid(hdrRow, "Open", 0, 2, true);
                AddCellToGrid(hdrRow, "Crit", 0, 3, true);
                AddCellToGrid(hdrRow, "Overdue", 0, 4, true);
                AddCellToGrid(hdrRow, "Load", 0, 5, true);
                assignStack.Children.Add(hdrRow);

                int maxLoad = assigneeLoad.Values.Max(v => v.Open);
                foreach (var kv in assigneeLoad.OrderByDescending(x => x.Value.Open).Take(15))
                {
                    var aRow = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                    aRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                    aRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                    aRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                    aRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                    aRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
                    aRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    string memberDisc = memberDiscLookup.TryGetValue(kv.Key, out string md) ? md : "";
                    AddCellToGrid(aRow, kv.Key, 0, 0, false, null, null);
                    AddCellToGrid(aRow, memberDisc, 0, 1, false, null, Br(Color.FromRgb(0x55, 0x55, 0x55)));
                    AddCellToGrid(aRow, kv.Value.Open.ToString(), 0, 2, false, FontWeights.Bold,
                        kv.Value.Open > 5 ? Br(CRed) : kv.Value.Open > 0 ? Br(CAmber) : Br(CGreen));
                    AddCellToGrid(aRow, kv.Value.Critical.ToString(), 0, 3, false, null,
                        kv.Value.Critical > 0 ? Br(CRed) : null);
                    AddCellToGrid(aRow, kv.Value.Overdue.ToString(), 0, 4, false, null,
                        kv.Value.Overdue > 0 ? Br(CRed) : null);
                    // Mini bar — color: CRed for overdue/critical, CAmber for 1-3 open or high, CGreen for 0
                    var barClr = kv.Value.Overdue > 0 || kv.Value.Critical > 0 ? CRed
                        : kv.Value.Open is >= 1 and <= 3 ? CAmber
                        : kv.Value.Open == 0 ? CGreen : CAccent;
                    var barBg = new Border { Background = Br(Color.FromRgb(0xE0, 0xE0, 0xE0)), Height = 10, CornerRadius = new CornerRadius(3), Margin = new Thickness(4, 3, 0, 0) };
                    var barFill = new Border { Background = Br(barClr),
                        Height = 10, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
                    barBg.Child = barFill;
                    int oCount = kv.Value.Open; int mxL = Math.Max(1, maxLoad);
                    barBg.Loaded += (s, e) => { barFill.Width = barBg.ActualWidth * oCount / mxL; };
                    Grid.SetColumn(barBg, 5);
                    aRow.Children.Add(barBg);
                    aRow.ToolTip = $"{kv.Key}: {kv.Value.Total} total, {kv.Value.Open} open, {kv.Value.Critical} critical, {kv.Value.Overdue} overdue";
                    assignStack.Children.Add(aRow);
                }
                assignCard.Child = assignStack;
                root.Children.Add(assignCard);
            }

            // ── DYNAMIC CONTEXT AREA (Phase 76 Item 14) ─────────────────
            // Replaced static AEC/FM role taxonomy with per-action contextual UI
            root.Children.Add(MakeSectionHeader("ACTION CONTEXT"));
            _issueContextArea = new ContentControl { MinHeight = 160 };
            // Default prompt
            _issueContextArea.Content = new Border
            {
                Background = Br(CCardBg), BorderBrush = Br(CBorder), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(14, 10, 14, 10)
            };
            (_issueContextArea.Content as Border).Child = new TextBlock
            {
                Text = "Click an action button above to see configuration options here.",
                FontSize = 12, Foreground = Brushes.Gray
            };
            root.Children.Add(_issueContextArea);

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
                dg.Columns.Add(new DataGridTextColumn { Header = "Location", Binding = new Binding("Location"), Width = 90 });
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
                           "BCF 2.1 export/import supported for Planscape (native), ACC, Navisworks, BIMcollab, Solibri, Trimble Connect, BIM Track, Revizto, Bentley iTwin, Procore.",
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

        // ── ISO 19650 revision code catalogue ─────────────────────────────
        // Phase 99: comprehensive revision series per ISO 19650 / BS 1192 / UK NA 2021.
        // Groups: Tender (T) → Preliminary (P) → Contract (Co) → Construction (C)
        // → Revision (R) → Building (B) → Digital (D) → Approved (A) → As-Built (Z/AB).
        // Dropdown is grouped by Series, tooltips explain when each is used.
        private static readonly (string Code, string Label, string Series, string Tooltip)[] IsoRevisionCodes =
            BuildIsoRevisionCodes();

        // Phase 104: user-hidden ISO revision codes. BIM coordinators asked for a way to
        // delete codes from the dropdown that are not relevant to their project (e.g., a
        // residential project never uses IFT/IFP tender stamps). Persisted to
        // project_config.json under key BCC_HIDDEN_ISO_REVISIONS as a CSV of codes.
        private static HashSet<string> _hiddenIsoRevisions;
        private static HashSet<string> HiddenIsoRevisions()
        {
            if (_hiddenIsoRevisions != null) return _hiddenIsoRevisions;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string csv = Core.TagConfig.GetConfigValue("BCC_HIDDEN_ISO_REVISIONS");
                if (!string.IsNullOrEmpty(csv))
                    foreach (var c in csv.Split(',')) { var t = c.Trim(); if (t.Length > 0) set.Add(t); }
            }
            catch (Exception ex) { StingLog.Warn($"HiddenIsoRevisions load: {ex.Message}"); }
            _hiddenIsoRevisions = set;
            return set;
        }
        private static void SaveHiddenIsoRevisions()
        {
            try
            {
                var set = HiddenIsoRevisions();
                Core.TagConfig.SetConfigValue("BCC_HIDDEN_ISO_REVISIONS", string.Join(",", set));
            }
            catch (Exception ex) { StingLog.Warn($"SaveHiddenIsoRevisions: {ex.Message}"); }
        }

        private static (string Code, string Label, string Series, string Tooltip)[] BuildIsoRevisionCodes()
        {
            var list = new List<(string, string, string, string)>();

            // ── Tender (T-series) — used during tender / bid phase ─────────
            for (int i = 1; i <= 10; i++)
                list.Add(($"T{i:D2}", $"T{i:D2} — Tender Issue {i}",
                    "Tender", $"Tender package issue {i} — pre-contract pricing / bid submission"));

            // ── Preliminary (P-series) — PAS 1192-2 / ISO 19650-2 pre-contract ──
            for (int i = 1; i <= 20; i++)
                list.Add(($"P{i:D2}", $"P{i:D2} — Preliminary Issue {i}",
                    "Preliminary", $"Preliminary issue {i} — design development / coordination before construction contract"));

            // ── Contract (Co-series) — contract documentation (post-award) ──
            for (int i = 1; i <= 10; i++)
                list.Add(($"Co{i:D2}", $"Co{i:D2} — Contract Issue {i}",
                    "Contract", $"Contract issue {i} — formally contracted documentation, pre-construction"));

            // ── Construction (C-series) — construction-phase issue ────────
            for (int i = 1; i <= 20; i++)
                list.Add(($"C{i:D2}", $"C{i:D2} — Construction Issue {i}",
                    "Construction", $"Construction issue {i} — authorised for building / manufacture"));

            // ── Revision (R-series) — client-driven post-issue revisions ──
            for (int i = 1; i <= 10; i++)
                list.Add(($"R{i:D2}", $"R{i:D2} — Revision {i}",
                    "Revision", $"Client-driven revision {i} after initial issue"));

            // ── Building (B-series) — used on projects with partial sign-off (BS 1192 NA) ──
            for (int i = 1; i <= 5; i++)
                list.Add(($"B{i:D2}", $"B{i:D2} — Partial Sign-off {i}",
                    "Building", $"Partial building sign-off stage {i} — occupation / phased handover"));

            // ── Digital (D-series) — digital-only revisions / modelling updates ──
            for (int i = 1; i <= 10; i++)
                list.Add(($"D{i:D2}", $"D{i:D2} — Digital Revision {i}",
                    "Digital", $"Digital-only revision {i} — model update without drawing reissue"));

            // ── Approved (A1, A2) — client formal approval per ISO 19650-2 §5.6 ──
            list.Add(("A1", "A1  — Approved",
                "Approved", "Client-approved construction issue (ISO 19650-2 §5.6 unconditional approval)"));
            list.Add(("A2", "A2  — Approved with Comments",
                "Approved", "Client-approved with tracked comments — rework required before next revision"));

            // ── As-Built (alphabetic A–Z series) — post-construction record ──
            // Skip letters easily confused with series codes (C, D, P, R, T, B) to
            // avoid accidental collisions with the numbered series above.
            foreach (char ch in "AEFGHIJKLMNOQSUVWXYZ")
                list.Add((ch.ToString(), $"{ch}   — As-Built Revision {ch}",
                    "As-Built", $"As-built / record drawing revision {ch} — final post-construction record"));
            // Allow AB suffix for projects that explicitly distinguish AB01+
            for (int i = 1; i <= 10; i++)
                list.Add(($"AB{i:D2}", $"AB{i:D2} — As-Built Record {i}",
                    "As-Built", $"As-built record {i} — standardised AB-series used by some operators"));

            // ── Special non-numeric codes commonly seen on UK projects ────
            list.Add(("IFC", "IFC  — Issued for Coordination", "Special",
                "Status stamp — document is being shared for clash / design coordination"));
            list.Add(("IFA", "IFA  — Issued for Approval", "Special",
                "Status stamp — document is issued for formal client / stage approval"));
            list.Add(("IFR", "IFR  — Issued for Record", "Special",
                "Status stamp — document is issued as the formal archival record"));
            list.Add(("IFT", "IFT  — Issued for Tender", "Special",
                "Status stamp — document is issued as part of the tender package"));
            list.Add(("IFP", "IFP  — Issued for Planning", "Special",
                "Status stamp — document is issued for planning permission submission"));
            list.Add(("IFI", "IFI  — Issued for Information", "Special",
                "Status stamp — document is issued for information only, not for design decisions"));
            list.Add(("IFPT", "IFPT — Issued for Pre-Tender", "Special",
                "Status stamp — document is issued ahead of formal tender for early supplier engagement"));
            list.Add(("WD", "WD   — Withdrawn", "Special",
                "Document has been withdrawn from the CDE — no longer valid, cannot be restored"));
            list.Add(("SS", "SS   — Superseded", "Special",
                "Document has been replaced by a newer revision — retained for traceability"));
            list.Add(("OB", "OB   — Obsolete", "Special",
                "Document is historically retained but not for use — terminal state"));

            return list.ToArray();
        }

        // ── ISO discipline codes for revisions / deliverables ─────────────
        private static readonly (string Code, string Label, Color Colour)[] IsoDisciplineCodes =
        {
            ("ALL","All Disciplines",                  Color.FromRgb(0x45,0x50,0x6E)),
            ("A",  "A  — Architectural",               Color.FromRgb(0x75,0x75,0x75)),
            ("S",  "S  — Structural",                  Color.FromRgb(0xC6,0x28,0x28)),
            ("M",  "M  — Mechanical (HVAC)",           Color.FromRgb(0x15,0x65,0xC0)),
            ("E",  "E  — Electrical",                  Color.FromRgb(0xFF,0xA0,0x00)),
            ("P",  "P  — Plumbing / Drainage",         Color.FromRgb(0x2E,0x7D,0x32)),
            ("FP", "FP — Fire Protection",             Color.FromRgb(0xD3,0x2F,0x2F)),
            ("LV", "LV — Low Voltage / BMS",           Color.FromRgb(0x6A,0x1B,0x9A)),
            ("G",  "G  — General / Multi-discipline",  Color.FromRgb(0x00,0x69,0x7C)),
            ("C",  "C  — Civil / Infrastructure",      Color.FromRgb(0x4E,0x34,0x2E)),
            ("L",  "L  — Landscape",                   Color.FromRgb(0x33,0x69,0x1E)),
            ("X",  "X  — Existing / Survey",           Color.FromRgb(0x9E,0x9E,0x9E)),
        };

        private UIElement BuildRevisionsTab()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(20) };
            var stack = new StackPanel();

            // ── KPI Strip ─────────────────────────────────────────────────
            int totalClouds = _data.Revisions.Count > 0 ? _data.Revisions.Sum(r => r.Clouds) : _data.RevisionClouds;
            var statusGroups = _data.Revisions.GroupBy(r => r.Status ?? "Unknown").ToDictionary(g => g.Key, g => g.Count());
            int issued       = statusGroups.TryGetValue("Issued",     out int iv) ? iv : 0;
            int draft        = statusGroups.TryGetValue("Draft",      out int dv) ? dv : 0;
            int superseded   = statusGroups.TryGetValue("Superseded", out int sv) ? sv : 0;

            var kpiRow = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, 0, 12) };
            kpiRow.Children.Add(MakeKPICard("REVISIONS",  _data.RevisionCount.ToString(),           Br(CHeaderBg),
                $"Total revisions in project\nDraft: {draft}  Issued: {issued}  Superseded: {superseded}"));
            kpiRow.Children.Add(MakeKPICard("CLOUDS",     totalClouds.ToString(),                   Br(CAccent),
                $"Total revision clouds placed\nUnresolved: {_data.CloudsUnresolved}"));
            kpiRow.Children.Add(MakeKPICard("THIS WEEK",  _data.RevisionsThisWeek.ToString(),
                _data.RevisionsThisWeek > 0 ? Br(Color.FromRgb(0x15,0x65,0xC0)) : Br(Color.FromRgb(0x45,0x50,0x6E)),
                "Revisions created in the last 7 days"));
            kpiRow.Children.Add(MakeKPICard("UNRESOLVED", _data.CloudsUnresolved.ToString(),
                _data.CloudsUnresolved > 0 ? Br(CRed) : Br(CGreen),
                $"Revision clouds not yet addressed\nTotal clouds: {totalClouds} across {_data.CloudsBySheet.Count} sheets"));
            kpiRow.Children.Add(MakeKPICard("ISSUED",     issued.ToString(), Br(CGreen),
                "Revisions formally issued to sheets"));
            stack.Children.Add(kpiRow);

            // ── Revision Status Distribution ───────────────────────────────
            if (statusGroups.Count > 0)
            {
                stack.Children.Add(MakeSectionHeader("REVISION STATUS DISTRIBUTION"));
                var statusCard = MakeCard();
                var statusStack = new StackPanel();
                int maxStatusCount = statusGroups.Values.Max();
                foreach (var kv in statusGroups.OrderByDescending(x => x.Value))
                {
                    var statusColor = kv.Key == "Issued" ? CGreen
                        : kv.Key == "Draft"       ? CAmber
                        : kv.Key == "Superseded"  ? Color.FromRgb(0x75,0x75,0x75)
                        : CHeaderBg;
                    var statusRow = new Grid { Margin = new Thickness(0,2,0,2), Height = 26 };
                    statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                    statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                    statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    // Coloured status chip
                    var chip = new Border { Background = Br(statusColor), CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(6,1,6,1), VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock { Text = kv.Key, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.White } };
                    statusRow.Children.Add(chip);
                    var countTb = new TextBlock { Text = kv.Value.ToString(), FontSize = 12, FontWeight = FontWeights.Bold,
                        Foreground = Br(statusColor), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(countTb, 1); statusRow.Children.Add(countTb);

                    double barPct = maxStatusCount > 0 ? kv.Value * 100.0 / maxStatusCount : 0;
                    var barBg = new Border { Background = Br(Color.FromRgb(0xE8,0xEA,0xED)), Height = 12,
                        CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Center };
                    var barFill = new Border { Background = Br(statusColor), Height = 12,
                        CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.85 };
                    var barGrid = new Grid(); barGrid.Children.Add(barBg); barGrid.Children.Add(barFill);
                    barBg.Loaded += (s, e) => { barFill.Width = Math.Max(6, barBg.ActualWidth * barPct / 100.0); };
                    Grid.SetColumn(barGrid, 2); statusRow.Children.Add(barGrid);
                    statusStack.Children.Add(statusRow);
                }
                statusCard.Child = statusStack;
                stack.Children.Add(statusCard);
            }

            // ── Clouds by Discipline (full ISO codes) ─────────────────────
            if (_data.CloudsByDiscipline.Count > 0)
            {
                stack.Children.Add(MakeSectionHeader("REVISION CLOUDS BY ISO DISCIPLINE"));
                var discCard = MakeCard();
                var discStack = new StackPanel();
                int maxDiscClouds = _data.CloudsByDiscipline.Values.Max();
                foreach (var kv in _data.CloudsByDiscipline.OrderByDescending(x => x.Value))
                {
                    var discEntry = IsoDisciplineCodes.FirstOrDefault(d => d.Code == kv.Key
                        || d.Label.StartsWith(kv.Key + " ") || d.Label.Contains("— " + kv.Key));
                    Color discColor = discEntry.Code != null ? discEntry.Colour : CHeaderBg;
                    string discLabel = discEntry.Code != null ? discEntry.Label : kv.Key;

                    var discRow = new Grid { Margin = new Thickness(0,2,0,2), Height = 24 };
                    discRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                    discRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                    discRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var discBadge = new Border { Background = Br(discColor), CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(5,1,5,1), VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock { Text = discLabel, FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White } };
                    discRow.Children.Add(discBadge);
                    var cntTb = new TextBlock { Text = $"{kv.Value} clouds", FontSize = 10, Foreground = Br(discColor),
                        VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold };
                    Grid.SetColumn(cntTb, 1); discRow.Children.Add(cntTb);

                    double barPct = maxDiscClouds > 0 ? kv.Value * 100.0 / maxDiscClouds : 0;
                    var barBg = new Border { Background = Br(Color.FromRgb(0xE8,0xEA,0xED)), Height = 10,
                        CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
                    var barFill = new Border { Background = Br(discColor), Height = 10,
                        CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.85 };
                    var barGrid = new Grid(); barGrid.Children.Add(barBg); barGrid.Children.Add(barFill);
                    barBg.Loaded += (s, e) => { barFill.Width = Math.Max(4, barBg.ActualWidth * barPct / 100.0); };
                    Grid.SetColumn(barGrid, 2); discRow.Children.Add(barGrid);
                    discStack.Children.Add(discRow);
                }
                discCard.Child = discStack;
                stack.Children.Add(discCard);
            }

            // ── Clouds by Sheet (top 10) ───────────────────────────────────
            if (_data.CloudsBySheet.Count > 0)
            {
                stack.Children.Add(MakeSectionHeader("TOP SHEETS BY REVISION CLOUDS"));
                var sheetCard = MakeCard();
                var sheetStack = new StackPanel();
                int maxSheetClouds = _data.CloudsBySheet.Values.Max();
                foreach (var kv in _data.CloudsBySheet.OrderByDescending(x => x.Value).Take(10))
                {
                    var sheetRow = new Grid { Margin = new Thickness(0,2,0,2), Height = 22 };
                    sheetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    sheetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                    sheetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                    sheetRow.Children.Add(new TextBlock { Text = kv.Key, FontSize = 10,
                        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                    var cntTb = new TextBlock { Text = kv.Value.ToString(), FontSize = 11, FontWeight = FontWeights.Bold,
                        Foreground = Br(CAccent), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
                    Grid.SetColumn(cntTb, 1); sheetRow.Children.Add(cntTb);
                    double barPct = maxSheetClouds > 0 ? kv.Value * 100.0 / maxSheetClouds : 0;
                    var barBg = new Border { Background = Br(Color.FromRgb(0xE8,0xEA,0xED)), Height = 8,
                        CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
                    var barFill = new Border { Background = Br(CAccent), Height = 8,
                        CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.8 };
                    var barGrid = new Grid(); barGrid.Children.Add(barBg); barGrid.Children.Add(barFill);
                    barBg.Loaded += (s, e) => { barFill.Width = Math.Max(4, barBg.ActualWidth * barPct / 100.0); };
                    Grid.SetColumn(barGrid, 2); sheetRow.Children.Add(barGrid);
                    sheetStack.Children.Add(sheetRow);
                }
                sheetCard.Child = sheetStack;
                stack.Children.Add(sheetCard);
            }

            // ── Revision Register DataGrid ─────────────────────────────────
            stack.Children.Add(MakeSectionHeader("REVISION REGISTER"));

            // Phase 103: inline pre-revision compliance banner. Replaces the
            // Revit TaskDialog that used to pop up behind the BCC when tag
            // compliance was below 80%. The banner reads CoordData.TagPct so
            // it updates with every Refresh. A checkbox "Create anyway if
            // below 80%" sets a flag we pass to CreateRevisionCommand via
            // ExtraParam("RevisionComplianceAck"), replacing the modal
            // decision with an inline one.
            bool _revComplianceLow = _data.TagPct < 80;
            var _revComplianceAckCheck = new CheckBox
            {
                Content = $"Create revision anyway below 80% (I accept that COBie data may be incomplete)",
                IsChecked = !_revComplianceLow,
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 0),
                ToolTip = "Tick this if you want to create a revision even though tag compliance is below 80%. Keep unchecked to guard the export pipeline."
            };
            if (_revComplianceLow)
            {
                var gateBorder = new Border
                {
                    Background = Br(Color.FromRgb(0xFF, 0xF3, 0xE0)),
                    BorderBrush = Br(Color.FromRgb(0xE8, 0x91, 0x2D)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                var gateStack = new StackPanel();
                gateStack.Children.Add(new TextBlock
                {
                    Text = $"\u26A0  Tag compliance is {_data.TagPct:F0}% (below the 80% revision gate).",
                    FontWeight = FontWeights.Bold, FontSize = 12,
                    Foreground = Br(Color.FromRgb(0xBF, 0x36, 0x00)),
                    TextWrapping = TextWrapping.Wrap
                });
                gateStack.Children.Add(new TextBlock
                {
                    Text = $"Tagged: {_data.TaggedComplete}   |   Untagged: {_data.Untagged}   |   Stale: {_data.StaleCount}\n" +
                           "Creating a revision with low compliance may result in incomplete COBie data.\n" +
                           "Recommended: raise compliance to \u226580% before creating the revision.",
                    FontSize = 11, TextWrapping = TextWrapping.Wrap,
                    Foreground = Br(Color.FromRgb(0x55, 0x40, 0x20)),
                    Margin = new Thickness(0, 4, 0, 0)
                });
                gateStack.Children.Add(_revComplianceAckCheck);
                gateBorder.Child = gateStack;
                stack.Children.Add(gateBorder);
            }

            // ── Create Revision inline form ────────────────────────────────
            var createFormBorder = new Border
            {
                Background = Br(Color.FromRgb(0xF1,0xF8,0xFF)),
                BorderBrush = Br(CAccent), BorderThickness = new Thickness(1,0,1,1),
                CornerRadius = new CornerRadius(0,0,6,6), Padding = new Thickness(12,10,12,10),
                Margin = new Thickness(0,0,0,8)
            };
            var createFormHeader = new Border
            {
                Background = Br(CHeaderBg), CornerRadius = new CornerRadius(6,6,0,0),
                Padding = new Thickness(12,6,12,6), Margin = new Thickness(0,0,0,0),
                Child = new TextBlock { Text = "\u2295  CREATE NEW REVISION", FontSize = 11,
                    FontWeight = FontWeights.Bold, Foreground = Brushes.White }
            };
            stack.Children.Add(createFormHeader);

            // Phase 101: IsEditable=true so coordinators can type a bespoke
            // revision code directly into the combo (e.g. internal codes like
            // "PQ-01" or stage-gate ids like "G3-A"). Typed values are taken
            // as the Code verbatim; preset items still populate the Tag on
            // selection. Helper below reads the right value depending on mode.
            var codeDropdown = new ComboBox
            {
                Height = 28, FontSize = 11, Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 280,
                IsEditable = true,
                IsTextSearchEnabled = true,
                StaysOpenOnEdit = true,
                ToolTip = "Select a preset ISO 19650 revision code, or type a custom code (9 series: Tender, Preliminary, Contract, Construction, Revision, Building, Digital, Approved, As-Built + status stamps)"
            };
            // Phase 99: all 9 series now rendered. Each series gets a coloured
            // non-selectable header so BIM coordinators can see the section divisions.
            void AddSeriesHeader(string label, Color headerColour)
            {
                codeDropdown.Items.Add(new ComboBoxItem
                {
                    Content = $"\u2500\u2500 {label} \u2500\u2500",
                    IsEnabled = false,
                    FontWeight = FontWeights.Bold,
                    Foreground = Br(headerColour)
                });
            }
            // Phase 104: filter codes the user has hidden via the Delete button.
            var hidden = HiddenIsoRevisions();
            void AddSeries(string seriesKey, string header, Color headerColour)
            {
                var entries = IsoRevisionCodes
                    .Where(r => r.Series == seriesKey && !hidden.Contains(r.Code))
                    .ToList();
                if (entries.Count == 0) return;
                AddSeriesHeader(header, headerColour);
                foreach (var rc in entries)
                    codeDropdown.Items.Add(new ComboBoxItem { Content = rc.Label, Tag = rc.Code, ToolTip = rc.Tooltip });
            }
            AddSeries("Tender",       "TENDER (T-series)",              Color.FromRgb(0x45, 0x50, 0x6E));
            AddSeries("Preliminary",  "PRELIMINARY (P-series)",         Color.FromRgb(0x15, 0x65, 0xC0));
            AddSeries("Contract",     "CONTRACT (Co-series)",           Color.FromRgb(0x00, 0x69, 0x7C));
            AddSeries("Construction", "CONSTRUCTION (C-series)",        Color.FromRgb(0x2E, 0x7D, 0x32));
            AddSeries("Revision",     "REVISION (R-series)",            Color.FromRgb(0xE8, 0x91, 0x2D));
            AddSeries("Building",     "BUILDING / PARTIAL (B-series)",  Color.FromRgb(0x4E, 0x34, 0x2E));
            AddSeries("Digital",      "DIGITAL (D-series)",             Color.FromRgb(0x6A, 0x1B, 0x9A));
            AddSeries("Approved",     "APPROVED (A1/A2)",               Color.FromRgb(0x2E, 0x7D, 0x32));
            AddSeries("As-Built",     "AS-BUILT (letter + AB-series)",  Color.FromRgb(0x6A, 0x1B, 0x9A));
            AddSeries("Special",      "STATUS STAMPS (IFC / IFA / IFR / IFT / WD / SS / OB)",
                Color.FromRgb(0xC6, 0x28, 0x28));
            // Default: P01 — second item (first is the Tender header)
            for (int i = 0; i < codeDropdown.Items.Count; i++)
            {
                if (codeDropdown.Items[i] is ComboBoxItem ci && (ci.Tag as string) == "P01")
                {
                    codeDropdown.SelectedIndex = i;
                    break;
                }
            }

            var discDropdown = new ComboBox { Height = 28, FontSize = 11, Margin = new Thickness(0,0,8,0), MinWidth = 220, ToolTip = "ISO originating discipline" };
            foreach (var dc in IsoDisciplineCodes)
            {
                var item = new ComboBoxItem { Content = dc.Label, Tag = dc.Code };
                discDropdown.Items.Add(item);
            }
            discDropdown.SelectedIndex = 0;

            var descBox = new TextBox { Height = 28, FontSize = 11, Margin = new Thickness(0,0,8,0),
                MinWidth = 220, VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Short revision description (e.g. 'Structural coordination update')" };
            descBox.Text = "Coordination update";

            var createBtn = new Button
            {
                Content = "Create Revision", Height = 28, Padding = new Thickness(14,0,14,0),
                Background = Br(CGreen), Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontSize = 11, FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                ToolTip = "Creates a new Revit revision with the selected ISO code and discipline"
            };
            createBtn.Click += (s, e) =>
            {
                // Phase 103: honour the inline compliance-gate checkbox. If
                // compliance is below 80% and the user hasn't explicitly
                // acknowledged, halt here (inline — no popup) so they see the
                // orange banner above the form. Otherwise set the ExtraParam
                // that CreateRevisionCommand reads for its audit log and
                // proceed with the dispatch.
                bool ack = _revComplianceAckCheck.IsChecked == true;
                if (_revComplianceLow && !ack)
                {
                    if (_statusBar != null)
                        _statusBar.Text = $"Revision blocked: tag compliance {_data.TagPct:F0}% < 80%. Tick the ack checkbox above to override, or tag to \u226580% first.";
                    return;
                }
                StingCommandHandler.SetExtraParam("RevisionComplianceAck", ack ? "true" : "false");

                // Phase 101: dropdown is now IsEditable — fall back to typed Text
                // (with | stripped so it can't break the pipe-delimited dispatch)
                // when the user enters a custom code that isn't in the preset list.
                string selCode = (codeDropdown.SelectedItem as ComboBoxItem)?.Tag as string;
                if (string.IsNullOrWhiteSpace(selCode))
                {
                    selCode = codeDropdown.Text?.Trim()?.Replace("|", "/");
                    if (string.IsNullOrWhiteSpace(selCode)) selCode = "P01";
                }
                string selDisc = (discDropdown.SelectedItem as ComboBoxItem)?.Tag as string ?? "ALL";
                string selDesc = descBox.Text?.Trim()?.Replace("|", "/");
                if (string.IsNullOrEmpty(selDesc)) selDesc = "Revision";
                DispatchAction($"CreateRevision|{selCode}|{selDisc}|{selDesc}");
            };

            // Phase 104: "Delete code" removes the currently-selected ISO revision code
            // from the dropdown (persists to project_config.json). "Restore all" clears the
            // hidden list. These operate on the IsoRevisionCodes catalogue — not on the
            // revisions already created on the model.
            var deleteCodeBtn = new Button
            {
                Content = "\u2212 Delete Code", Height = 28, Padding = new Thickness(10,0,10,0),
                Background = Br(Color.FromRgb(0xC6, 0x28, 0x28)), Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontSize = 10, Cursor = Cursors.Hand,
                Margin = new Thickness(0,0,6,0),
                ToolTip = "Hide the currently-selected ISO revision code from the dropdown (persists per project). Does not affect existing revisions."
            };
            deleteCodeBtn.Click += (s, e) =>
            {
                string code = (codeDropdown.SelectedItem as ComboBoxItem)?.Tag as string;
                if (string.IsNullOrWhiteSpace(code))
                {
                    if (_statusBar != null) _statusBar.Text = "Select a preset ISO code from the dropdown first (typed custom codes cannot be deleted).";
                    return;
                }
                var set = HiddenIsoRevisions();
                set.Add(code);
                SaveHiddenIsoRevisions();
                // Remove the corresponding ComboBoxItem from the dropdown live
                ComboBoxItem toRemove = null;
                foreach (var it in codeDropdown.Items)
                    if (it is ComboBoxItem ci && (ci.Tag as string) == code) { toRemove = ci; break; }
                if (toRemove != null) codeDropdown.Items.Remove(toRemove);
                if (codeDropdown.Items.Count > 0) codeDropdown.SelectedIndex = 1; // skip first header
                if (_statusBar != null) _statusBar.Text = $"Hidden ISO revision code '{code}'. Click 'Restore All' to bring it back.";
            };
            var restoreCodesBtn = new Button
            {
                Content = "\u21BA Restore All", Height = 28, Padding = new Thickness(10,0,10,0),
                Background = Br(Color.FromRgb(0x45, 0x50, 0x6E)), Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontSize = 10, Cursor = Cursors.Hand,
                Margin = new Thickness(0,0,8,0),
                ToolTip = "Restore all hidden ISO revision codes to the dropdown"
            };
            restoreCodesBtn.Click += (s, e) =>
            {
                HiddenIsoRevisions().Clear();
                SaveHiddenIsoRevisions();
                if (_statusBar != null) _statusBar.Text = "All ISO revision codes restored. Close and reopen the Revisions tab to see them.";
                // Navigate away and back to rebuild
                try { if (_tabCache.ContainsKey(TabRevisions)) _tabCache.Remove(TabRevisions); NavigateTo(TabRevisions); } catch (Exception ex) { StingLog.Warn($"restoreCodesBtn: {ex.Message}"); }
            };

            var formRow = new WrapPanel { Margin = new Thickness(0,0,0,4) };
            formRow.Children.Add(new TextBlock { Text = "ISO Code: ", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,4,0), FontWeight = FontWeights.SemiBold });
            formRow.Children.Add(codeDropdown);
            formRow.Children.Add(deleteCodeBtn);
            formRow.Children.Add(restoreCodesBtn);
            formRow.Children.Add(new TextBlock { Text = "Discipline: ", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,4,0), FontWeight = FontWeights.SemiBold });
            formRow.Children.Add(discDropdown);
            formRow.Children.Add(new TextBlock { Text = "Description: ", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,4,0), FontWeight = FontWeights.SemiBold });
            formRow.Children.Add(descBox);
            formRow.Children.Add(createBtn);
            createFormBorder.Child = formRow;
            stack.Children.Add(createFormBorder);

            // Phase 104: custom code row — explicit inline input so the
            // coordinator can add bespoke revision codes (e.g. PQ-01, G3-A,
            // Co11 when they go past the built-in 10 Contract issues) without
            // hunting through the IsEditable combo. Typed code is prepended
            // as a new ComboBoxItem in its own "CUSTOM" group at the top of
            // the dropdown and immediately selected.
            var customCodeBorder = new Border
            {
                Background = Br(Color.FromRgb(0xFF, 0xF8, 0xE1)),
                BorderBrush = Br(Color.FromRgb(0xE8, 0x91, 0x2D)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var customCodeRow = new WrapPanel();
            customCodeRow.Children.Add(new TextBlock
            {
                Text = "\u2795 Add Custom Code:", FontWeight = FontWeights.SemiBold, FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
                Foreground = Br(Color.FromRgb(0x55, 0x40, 0x20)),
                ToolTip = "Append a bespoke ISO code to the dropdown (e.g. PQ-01, G3-A, Co11). Useful when >10 revisions in one series."
            });
            var customCodeBox = new System.Windows.Controls.TextBox
            {
                Width = 100, FontSize = 11, Margin = new Thickness(0, 0, 4, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Custom revision code"
            };
            var customLabelBox = new System.Windows.Controls.TextBox
            {
                Width = 180, FontSize = 11, Margin = new Thickness(0, 0, 4, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Human-readable label (optional)"
            };
            var addCustomBtn = new Button
            {
                Content = "Add to dropdown", Height = 22, Padding = new Thickness(8, 0, 8, 0),
                Background = Br(Color.FromRgb(0xE8, 0x91, 0x2D)), Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontSize = 10, Cursor = Cursors.Hand,
                ToolTip = "Prepend the typed code to the ISO Code dropdown and select it"
            };
            addCustomBtn.Click += (s, e) =>
            {
                string cc = customCodeBox.Text?.Trim()?.Replace("|", "/");
                if (string.IsNullOrWhiteSpace(cc))
                {
                    if (_statusBar != null) _statusBar.Text = "Enter a custom code first.";
                    return;
                }
                string lbl = customLabelBox.Text?.Trim();
                string display = string.IsNullOrEmpty(lbl) ? $"{cc} — Custom revision" : $"{cc} \u2014 {lbl}";
                // Insert at index 0 (above every preset series header) with a
                // gold tint so it stands out as non-standard.
                var item = new ComboBoxItem
                {
                    Content = display, Tag = cc,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Br(Color.FromRgb(0xBF, 0x36, 0x00)),
                    ToolTip = $"Custom revision code — added from BCC Revisions tab"
                };
                codeDropdown.Items.Insert(0, item);
                codeDropdown.SelectedIndex = 0;
                customCodeBox.Clear();
                customLabelBox.Clear();
                if (_statusBar != null) _statusBar.Text = $"Custom code '{cc}' added and selected.";
            };
            customCodeRow.Children.Add(customCodeBox);
            customCodeRow.Children.Add(customLabelBox);
            customCodeRow.Children.Add(addCustomBtn);
            customCodeBorder.Child = customCodeRow;
            stack.Children.Add(customCodeBorder);

            // ── Register DataGrid ──────────────────────────────────────────
            if (_data.Revisions.Count > 0)
            {
                var dg = new DataGrid
                {
                    AutoGenerateColumns = false, IsReadOnly = true,
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                    CanUserSortColumns = true, SelectionMode = DataGridSelectionMode.Single,
                    FontSize = 11, BorderBrush = Br(CBorder), BorderThickness = new Thickness(1),
                    RowHeaderWidth = 0, MaxHeight = 280, Margin = new Thickness(0,0,0,8)
                };
                dg.Columns.Add(new DataGridTextColumn { Header = "Code",        Binding = new Binding("Number"),      Width = 52 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Series",      Binding = new Binding("Series"),      Width = 90 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Disc.",       Binding = new Binding("Discipline"),  Width = 45 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Description", Binding = new Binding("Description"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                dg.Columns.Add(new DataGridTextColumn { Header = "Date",        Binding = new Binding("Date"),        Width = 85 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Author",      Binding = new Binding("Author"),      Width = 80 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Clouds",      Binding = new Binding("Clouds"),      Width = 52 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Status",      Binding = new Binding("Status"),      Width = 75 });
                dg.ItemsSource = _data.Revisions;

                // Row styling: Issued=green tint, Draft=amber tint, Superseded=grey italic
                var rowStyle = new Style(typeof(DataGridRow));
                var issuedT = new DataTrigger { Binding = new Binding("Status"), Value = "Issued" };
                issuedT.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Br(Color.FromRgb(0xE8,0xF5,0xE9))));
                rowStyle.Triggers.Add(issuedT);
                var draftT = new DataTrigger { Binding = new Binding("Status"), Value = "Draft" };
                draftT.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Br(Color.FromRgb(0xFF,0xF8,0xE1))));
                rowStyle.Triggers.Add(draftT);
                var supersededT = new DataTrigger { Binding = new Binding("Status"), Value = "Superseded" };
                supersededT.Setters.Add(new Setter(DataGridRow.ForegroundProperty, Brushes.Gray));
                supersededT.Setters.Add(new Setter(DataGridRow.FontStyleProperty, FontStyles.Italic));
                rowStyle.Triggers.Add(supersededT);
                dg.RowStyle = rowStyle;

                // Double-click → show inline dashboard
                dg.MouseDoubleClick += (s, e) =>
                {
                    if (dg.SelectedItem is RevisionRow rev)
                        ShowRevisionDashboard(rev);
                };

                // Selection changed → show inline dashboard automatically
                dg.SelectionChanged += (s, e) =>
                {
                    if (dg.SelectedItem is RevisionRow rev)
                        ShowRevisionDashboard(rev);
                };

                // ── Right-click context menu ───────────────────────────────
                var ctx = new ContextMenu();

                var ctxDash = new MenuItem { Header = "\U0001F4CA  View Revision Dashboard" };
                ctxDash.Click += (s2, e2) => { if (dg.SelectedItem is RevisionRow rev) ShowRevisionDashboard(rev); };
                ctx.Items.Add(ctxDash);
                ctx.Items.Add(new Separator());

                var ctxSelect = new MenuItem { Header = "\u2601  Select Revision Clouds" };
                ctxSelect.Click += (s2, e2) => { if (dg.SelectedItem is RevisionRow rev) DispatchAction($"SelectRevision_{rev.Id}"); };
                ctx.Items.Add(ctxSelect);

                var ctxZoom = new MenuItem { Header = "\U0001F50D  Zoom to Revision Clouds (Active View)" };
                ctxZoom.Click += (s2, e2) => { if (dg.SelectedItem is RevisionRow rev) DispatchAction($"ZoomToRevision_{rev.Id}"); };
                ctx.Items.Add(ctxZoom);

                var ctxZoom3d = new MenuItem { Header = "\U0001F532  Isolate Revision Clouds in 3D View" };
                ctxZoom3d.Click += (s2, e2) => { if (dg.SelectedItem is RevisionRow rev) DispatchAction($"IsolateRevision3D_{rev.Id}"); };
                ctx.Items.Add(ctxZoom3d);

                var ctxHighlight = new MenuItem { Header = "\U0001F3A8  Highlight Revision Clouds by Discipline" };
                ctxHighlight.Click += (s2, e2) => { if (dg.SelectedItem is RevisionRow rev) DispatchAction($"HighlightRevClouds_{rev.Id}"); };
                ctx.Items.Add(ctxHighlight);

                ctx.Items.Add(new Separator());

                var ctxIssue = new MenuItem { Header = "\U0001F4CB  Issue Sheets for This Revision" };
                ctxIssue.Click += (s2, e2) => { if (dg.SelectedItem is RevisionRow) DispatchAction("IssueSheetsForRevision"); };
                ctx.Items.Add(ctxIssue);

                var ctxSnapshot = new MenuItem { Header = "\U0001F4F8  Take Tag Snapshot for This Revision" };
                ctxSnapshot.Click += (s2, e2) => { DispatchAction("TakeSnapshot"); };
                ctx.Items.Add(ctxSnapshot);

                var ctxCompare = new MenuItem { Header = "\u2194  Compare with Previous Revision" };
                ctxCompare.Click += (s2, e2) => { DispatchAction("RevisionCompare"); };
                ctx.Items.Add(ctxCompare);

                ctx.Items.Add(new Separator());

                var ctxSupersede = new MenuItem { Header = "\u2298  Mark as Superseded" };
                ctxSupersede.Click += (s2, e2) => { if (dg.SelectedItem is RevisionRow rev) DispatchAction($"SupersedeRevision_{rev.Id}"); };
                ctx.Items.Add(ctxSupersede);

                var ctxExport = new MenuItem { Header = "\U0001F4E4  Export Revision Report to CSV" };
                ctxExport.Click += (s2, e2) => { DispatchAction("RevisionExport"); };
                ctx.Items.Add(ctxExport);

                dg.ContextMenu = ctx;
                stack.Children.Add(dg);
            }
            else
            {
                var infoCard = MakeCard();
                infoCard.Child = new TextBlock
                {
                    Text = "No revisions found. Use the form above to create your first ISO 19650 revision.\n\n" +
                           "Revisions track design changes:\n" +
                           "  \u2022 P-series (P01\u2013P10): Preliminary design stages\n" +
                           "  \u2022 C-series (C01\u2013C10): Construction issue stages\n" +
                           "  \u2022 Letter series (A\u2013Z): As-built record revisions\n\n" +
                           "Select a discipline code to tag revision clouds by originating trade.",
                    FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray
                };
                stack.Children.Add(infoCard);
            }

            // ── Action Buttons ─────────────────────────────────────────────
            stack.Children.Add(new Border { Height = 12 });
            stack.Children.Add(MakeSectionHeader("TRACKING & COMPARISON"));
            var trackWrap = new WrapPanel { Margin = new Thickness(0,0,0,8) };
            trackWrap.Children.Add(MakeActionButton("Take Snapshot",     "TakeSnapshot",         Br(Color.FromRgb(0x00,0x69,0x7C)),
                "Capture current tag state as a snapshot for later comparison"));
            trackWrap.Children.Add(MakeRevPanelButton("Compare Revisions", "RevisionCompare",   Br(CHeaderBg),
                "Compare two revision snapshots: see added/changed/removed tags per element"));
            trackWrap.Children.Add(MakeActionButton("Track Elements",    "TrackElementRevisions", Br(Color.FromRgb(0x6A,0x1B,0x9A)),
                "Track per-element revision history across all snapshots"));
            trackWrap.Children.Add(MakeRevPanelButton("Tag Revision Diff","TagRevisionDiff",    Br(Color.FromRgb(0x45,0x50,0x6E)),
                "Token-level diff between two snapshots: which tokens changed, old vs new values"));
            trackWrap.Children.Add(MakeActionButton("Auto Rev Clouds",   "AutoRevisionCloud",    Br(CAccent),
                "Automatically place revision clouds on elements changed since last revision"));
            stack.Children.Add(trackWrap);

            stack.Children.Add(MakeSectionHeader("ISSUANCE & EXPORT"));
            var issueWrap = new WrapPanel { Margin = new Thickness(0,0,0,8) };
            issueWrap.Children.Add(MakeRevPanelButton("Issue Sheets",       "IssueSheetsForRevision", Br(CAmber),
                "Issue selected sheets for the latest revision — adds to revision schedule"));
            issueWrap.Children.Add(MakeActionButton("Naming Enforce",       "RevisionNamingEnforce",  Br(Color.FromRgb(0x45,0x50,0x6E)),
                "Enforce ISO 19650 / BS 1192 revision naming conventions"));
            issueWrap.Children.Add(MakeActionButton("Tag Integration",      "RevisionTagIntegration", Br(Color.FromRgb(0x45,0x50,0x6E)),
                "Integrate revision data with STING tags: stamp REV token on elements"));
            issueWrap.Children.Add(MakeRevPanelButton("Revision Schedule",  "RevisionSchedule",       Br(Color.FromRgb(0x15,0x65,0xC0)),
                "Create/view Revit revision schedule showing all revisions and sheets"));
            issueWrap.Children.Add(MakeActionButton("Bulk Rev Stamp",       "BulkRevisionStamp",      Br(CRed),
                "Stamp revision information across multiple sheets in batch"));
            issueWrap.Children.Add(MakeActionButton("Export CSV",           "RevisionExport",         Br(Color.FromRgb(0x45,0x50,0x6E)),
                "Export revision register to CSV for external tracking"));
            stack.Children.Add(issueWrap);

            // ── Inline Revision Dashboard panel ───────────────────────────
            stack.Children.Add(MakeSectionHeader("REVISION DASHBOARD"));
            _revPanelArea = new ContentControl { MinHeight = 300 };
            // Show placeholder until a revision is selected
            var placeholder = new Border
            {
                Background = Br(Color.FromRgb(0xF8,0xF9,0xFA)),
                BorderBrush = Br(CBorder), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(20),
                Child = new TextBlock
                {
                    Text = "Select a revision in the register above to view its full dashboard here.\n" +
                           "Double-click or right-click \u2192 View Revision Dashboard.",
                    FontSize = 12, TextWrapping = TextWrapping.Wrap,
                    Foreground = Br(Color.FromRgb(0x9E,0x9E,0x9E)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            };
            _revPanelArea.Content = placeholder;
            stack.Children.Add(_revPanelArea);

            scroll.Content = stack;
            return scroll;
        }

        /// <summary>Populate _revPanelArea with a full corporate revision dashboard for the selected revision.</summary>
        private void ShowRevisionDashboard(RevisionRow rev)
        {
            var panel = new StackPanel { Margin = new Thickness(0,4,0,0) };

            // ── Header banner ──────────────────────────────────────────────
            // Determine series colour
            Color seriesColor = (rev.Series ?? rev.Number ?? "").StartsWith("P") ? Color.FromRgb(0x15,0x65,0xC0)
                : (rev.Series ?? rev.Number ?? "").StartsWith("C") ? Color.FromRgb(0x2E,0x7D,0x32)
                : Color.FromRgb(0x6A,0x1B,0x9A);

            var banner = new Border
            {
                Background = Br(CHeaderBg), CornerRadius = new CornerRadius(6,6,0,0),
                Padding = new Thickness(16,12,16,12)
            };
            var bannerRow = new Grid();
            bannerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bannerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bannerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // ISO code badge
            var codeBadge = new Border
            {
                Background = Br(seriesColor), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12,4,12,4), Margin = new Thickness(0,0,16,0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = rev.Number ?? "\u2014", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brushes.White }
            };
            bannerRow.Children.Add(codeBadge);

            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock { Text = rev.Description ?? rev.Name ?? "Revision", FontSize = 14,
                FontWeight = FontWeights.Bold, Foreground = Brushes.White });
            titleStack.Children.Add(new TextBlock
            {
                Text = $"{rev.Series ?? "Unknown"} \u00b7 {rev.Date ?? "\u2014"} \u00b7 Author: {rev.Author ?? "\u2014"} \u00b7 Discipline: {rev.Discipline ?? "ALL"}",
                FontSize = 10, Foreground = Br(Color.FromRgb(0xB0,0xBE,0xC5))
            });
            Grid.SetColumn(titleStack, 1); bannerRow.Children.Add(titleStack);

            // Status chip
            Color statusClr = (rev.Status ?? "") == "Issued" ? CGreen
                : (rev.Status ?? "") == "Draft" ? CAmber
                : Color.FromRgb(0x75,0x75,0x75);
            var statusBadge = new Border
            {
                Background = Br(statusClr), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10,4,10,4), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = rev.Status ?? "\u2014", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.White }
            };
            Grid.SetColumn(statusBadge, 2); bannerRow.Children.Add(statusBadge);
            banner.Child = bannerRow;
            panel.Children.Add(banner);

            // ── Stats row ─────────────────────────────────────────────────
            var statsBorder = new Border
            {
                Background = Br(CCardBg), BorderBrush = Br(seriesColor),
                BorderThickness = new Thickness(0,0,0,2), Padding = new Thickness(16,10,16,10)
            };
            var statsRow = new UniformGrid { Columns = 4 };

            void AddStat(string label, string value, Color colour)
            {
                var st = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                st.Children.Add(new TextBlock { Text = value, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Br(colour), HorizontalAlignment = HorizontalAlignment.Center });
                st.Children.Add(new TextBlock { Text = label,  FontSize = 9,  Foreground = Br(Color.FromRgb(0x75,0x75,0x75)), HorizontalAlignment = HorizontalAlignment.Center });
                statsRow.Children.Add(st);
            }

            AddStat("REVISION CLOUDS",  rev.Clouds.ToString(), seriesColor);
            AddStat("SHEETS AFFECTED",  string.IsNullOrEmpty(rev.Sheets) ? "\u2014" : rev.Sheets.Split(',').Length.ToString(), CAccent);
            AddStat("STATUS",           rev.Status ?? "\u2014", statusClr);
            AddStat("DISCIPLINE",       rev.Discipline ?? "ALL", Color.FromRgb(0x45,0x50,0x6E));
            statsBorder.Child = statsRow;
            panel.Children.Add(statsBorder);

            // ── Sheets affected ───────────────────────────────────────────
            if (!string.IsNullOrEmpty(rev.Sheets) && rev.Sheets != "\u2014")
            {
                var sheetsBorder = new Border
                {
                    Background = Br(CCardBg), BorderBrush = Br(CBorder), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(0,0,4,4), Padding = new Thickness(16,10,16,10)
                };
                var sheetsStack = new StackPanel();
                sheetsStack.Children.Add(new TextBlock { Text = "SHEETS AFFECTED", FontSize = 10, FontWeight = FontWeights.Bold,
                    Foreground = Br(CHeaderBg), Margin = new Thickness(0,0,0,6) });
                var sheetWrap = new WrapPanel();
                foreach (string sheetNo in rev.Sheets.Split(','))
                {
                    string sn = sheetNo.Trim();
                    if (string.IsNullOrEmpty(sn)) continue;
                    sheetWrap.Children.Add(new Border
                    {
                        Background = Br(Color.FromRgb(0xE3,0xF2,0xFD)), CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(8,3,8,3), Margin = new Thickness(0,0,6,4),
                        Child = new TextBlock { Text = sn, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Br(CAccent) }
                    });
                }
                sheetsStack.Children.Add(sheetWrap);
                sheetsBorder.Child = sheetsStack;
                panel.Children.Add(sheetsBorder);
            }

            // ── Quick action buttons ───────────────────────────────────────
            var qaBorder = new Border
            {
                Background = Br(Color.FromRgb(0xF8,0xF9,0xFA)), BorderBrush = Br(CBorder),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(0,0,6,6),
                Padding = new Thickness(12,8,12,8)
            };
            var qaWrap = new WrapPanel();
            void AddQA(string label, string action, Brush bg)
            {
                var btn = new Button { Content = label, Height = 26, Padding = new Thickness(12,0,12,0),
                    Background = bg, Foreground = Brushes.White, BorderThickness = new Thickness(0),
                    FontSize = 10, Cursor = Cursors.Hand, Margin = new Thickness(0,0,6,4) };
                btn.Click += (s, e) => DispatchAction($"{action}_{rev.Id}");
                qaWrap.Children.Add(btn);
            }
            AddQA("\u2601 Select Clouds",           $"SelectRevision",    Br(CAccent));
            AddQA("\U0001F50D Zoom to Clouds",       $"ZoomToRevision",    Br(CHeaderBg));
            AddQA("\U0001F532 Isolate in 3D",        $"IsolateRevision3D", Br(Color.FromRgb(0x45,0x50,0x6E)));
            AddQA("\U0001F3A8 Highlight by Disc.",   $"HighlightRevClouds",Br(Color.FromRgb(0x6A,0x1B,0x9A)));
            AddQA("\U0001F4CB Issue Sheets",         $"SelectRevision",    Br(CAmber));
            AddQA("\U0001F4F8 Take Snapshot",        $"SelectRevision",    Br(Color.FromRgb(0x00,0x69,0x7C)));
            AddQA("\U0001F4E4 Export Report",        $"SelectRevision",    Br(Color.FromRgb(0x37,0x47,0x4F)));
            qaBorder.Child = qaWrap;
            panel.Children.Add(qaBorder);

            SetPanelContent(_revPanelArea, panel);
        }

        // ════════════════════════════════════════════════════════════════
        //  PLATFORM TAB
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildPlatformTab()
        {
            // Phase 77: Two-column layout with platform tiles + inline detail area
            _platformDetailArea = new ContentControl { Margin = new Thickness(8, 0, 0, 0) };
            // Show placeholder message initially
            _platformDetailArea.Content = new Border
            {
                Background = Br(Color.FromRgb(0xF8, 0xF9, 0xFB)),
                BorderBrush = Br(CBorder), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(20),
                Child = new TextBlock
                {
                    Text = "Select a platform to configure the connection.",
                    FontSize = 12, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center
                }
            };

            // Platform definitions: name, connected (placeholder)
            var platforms = new[]
            {
                ("Planscape ★", false),
                ("ACC", false), ("SharePoint", false), ("Procore", false), ("Aconex", false),
                ("Trimble Connect", false), ("Bentley iTwin", false), ("Viewpoint 4P", false), ("BCF Server", false)
            };

            var leftPanel = new StackPanel { Width = 200, Margin = new Thickness(0, 0, 0, 0) };
            leftPanel.Children.Add(MakeSectionHeader("PLATFORMS"));
            foreach (var (pName, connected) in platforms)
            {
                var pName2 = pName; // capture for closure
                var tile = new Border
                {
                    Background = Br(CCardBg), BorderBrush = Br(CBorder), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand
                };
                var tileGrid = new Grid();
                tileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                tileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                tileGrid.Children.Add(new TextBlock { Text = pName2, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                var statusDot = new Ellipse { Width = 10, Height = 10, Fill = connected ? Br(CGreen) : Br(Color.FromRgb(0xBB, 0xBB, 0xBB)), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(statusDot, 1); tileGrid.Children.Add(statusDot);
                tile.Child = tileGrid;
                tile.MouseLeftButtonDown += (s, e) => ShowPlatformDetail(pName2);
                tile.MouseEnter += (s2, e2) => tile.Background = Br(Color.FromRgb(0xEE, 0xF2, 0xFF));
                tile.MouseLeave += (s2, e2) => tile.Background = Br(CCardBg);
                leftPanel.Children.Add(tile);
            }

            // Two-column layout
            var twoColGrid = new Grid { Margin = new Thickness(16, 8, 16, 8) };
            twoColGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            twoColGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            twoColGrid.Children.Add(leftPanel);
            Grid.SetColumn(_platformDetailArea, 1);
            twoColGrid.Children.Add(_platformDetailArea);

            var outerStack = new StackPanel();
            outerStack.Children.Add(twoColGrid);

            // ── Handover & Export section ──
            outerStack.Children.Add(new Border { Height = 1, Background = Br(CBorder), Margin = new Thickness(16, 8, 16, 8) });
            var handoverHeader = MakeSectionHeader("HANDOVER & EXPORT");
            handoverHeader.Margin = new Thickness(16, 4, 16, 4);
            outerStack.Children.Add(handoverHeader);
            var handoverWrap2 = new WrapPanel { Margin = new Thickness(16, 0, 16, 8) };
            handoverWrap2.Children.Add(MakeActionButton("FM Handover",   "FMHandover",           Br(CGreen),                          "Generate FM handover manual"));
            handoverWrap2.Children.Add(MakeActionButton("Stage Gate",    "StageGate",            Br(Color.FromRgb(0x45, 0x50, 0x6E)), "RIBA stage-gated compliance check"));
            handoverWrap2.Children.Add(MakeActionButton("Tag Register",  "TagRegisterExport",    Br(Color.FromRgb(0x45, 0x50, 0x6E)), "Export 40+ column asset register"));
            handoverWrap2.Children.Add(MakeActionButton("Sheet Register","SheetRegister",        Br(Color.FromRgb(0x45, 0x50, 0x6E)), "Export sheet register CSV"));
            handoverWrap2.Children.Add(MakeActionButton("BOQ Export",    "BOQExport",            Br(Color.FromRgb(0x6A, 0x1B, 0x9A)), "Export Bill of Quantities XLSX"));
            handoverWrap2.Children.Add(MakeActionButton("COBie Stream",  "COBieExport",          Br(Color.FromRgb(0x6A, 0x1B, 0x9A)), "COBie V2.4 FM handover export"));
            outerStack.Children.Add(handoverWrap2);

            // ── BCF section ──
            outerStack.Children.Add(new Border { Height = 1, Background = Br(CBorder), Margin = new Thickness(16, 0, 16, 8) });
            var bcfHeader = MakeSectionHeader("BCF — BIM COLLABORATION FORMAT");
            bcfHeader.Margin = new Thickness(16, 4, 16, 4);
            outerStack.Children.Add(bcfHeader);
            var bcfOuter = new StackPanel { Margin = new Thickness(16, 0, 16, 8) };
            var bcfRow1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            bcfRow1.Children.Add(new TextBlock { Text = "BCF Server URL:", Width = 120, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
            bcfRow1.Children.Add(new System.Windows.Controls.TextBox { Width = 300, Margin = new Thickness(4, 0, 12, 0), FontSize = 11 });
            var bcfCountBadge = new Border { Background = Br(CAccent), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = "0 issues", FontSize = 10, Foreground = Brushes.White, FontWeight = FontWeights.Bold } };
            bcfRow1.Children.Add(bcfCountBadge);
            bcfOuter.Children.Add(bcfRow1);
            var bcfBtnRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            bcfBtnRow.Children.Add(MakeActionButton("BCF Export", "BCFExport", Br(CAccent), "Export BCF 2.1 XML with viewpoints"));
            bcfBtnRow.Children.Add(MakeActionButton("BCF Import", "BCFImport", Br(CAccent), "Import BCF issues from external tools"));
            bcfOuter.Children.Add(bcfBtnRow);
            outerStack.Children.Add(bcfOuter);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            scroll.Content = outerStack;
            return scroll;
        }

        // ── Phase 77: ShowPlatformDetail helper ──────────────────────────
        private void ShowPlatformDetail(string platformName)
        {
            if (_platformDetailArea == null) return;
            var navyBrush = Br(CHeaderBg);
            var detailStack = new StackPanel { Margin = new Thickness(8) };

            // ── Planscape: native collaboration hub ──
            if (platformName.StartsWith("Planscape"))
            {
                detailStack.Children.Add(new TextBlock { Text = "Planscape — Native Collaboration Hub", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = navyBrush, Margin = new Thickness(0, 0, 0, 6) });
                detailStack.Children.Add(new TextBlock { Text = "Share coordination data, model health, issues and meeting records with your team — no external platform required.", FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Br(Color.FromRgb(0x44, 0x44, 0x44)), Margin = new Thickness(0, 0, 0, 10) });

                // Quick-share buttons
                detailStack.Children.Add(new TextBlock { Text = "QUICK SHARE", FontWeight = FontWeights.Bold, FontSize = 11, Foreground = Br(CAccent), Margin = new Thickness(0, 0, 0, 4) });
                var shareRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
                var btns = new (string Label, string Action, Color Clr, string Tip)[]
                {
                    ("📋 Copy Dashboard Link", "PlanscapeCopyLink", Color.FromRgb(0x45, 0x50, 0x6E), "Copy shareable HTML dashboard link to clipboard"),
                    ("📧 Email Report",        "PlanscapeEmail",    Color.FromRgb(0x15, 0x65, 0xC0), "Generate email with project status summary (Excel attachment)"),
                    ("💬 Teams Message",       "PlanscapeTeams",    Color.FromRgb(0x46, 0x4E, 0xB8), "Generate Teams/Slack message with coordination status cards"),
                    ("📱 WhatsApp Update",     "PlanscapeWhatsApp", CGreen,                           "Generate WhatsApp-ready text with project summary link"),
                    ("🔗 Generate QR Link",    "PlanscapeQR",       CHeaderBg,                        "Generate QR code linking to the latest exported HTML dashboard"),
                    ("📊 Export HTML Dashboard","PlanscapeHTML",    Color.FromRgb(0x6A, 0x1B, 0x9A), "Export full coordination dashboard as standalone HTML file (shareable, no login needed)"),
                };
                foreach (var (lbl, act, clr, tip) in btns)
                {
                    string capturedAct = act;
                    var b = new Button { Content = lbl, Height = 28, Padding = new Thickness(8, 0, 8, 0), Background = Br(clr), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 10, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 6), ToolTip = tip };
                    b.Click += (s, e) => DispatchAction(capturedAct);
                    shareRow.Children.Add(b);
                }
                detailStack.Children.Add(shareRow);

                // Access management
                detailStack.Children.Add(new TextBlock { Text = "ACCESS MANAGEMENT", FontWeight = FontWeights.Bold, FontSize = 11, Foreground = Br(CAccent), Margin = new Thickness(0, 4, 0, 4) });

                // Phase 102 — comprehensive role dropdown.
                // Previously only 4 generic platform roles (Admin/Coordinator/
                // Viewer/External). Now seeded from the canonical GetDefaultRoles
                // ISO 19650 catalogue (Client, Project Manager, BIM Manager,
                // Architect, Structural/MEP Engineers, Fire Engineer, Civil,
                // Contractor, Clerk of Works, FM, etc.) — same list used by
                // the Permission Groups sub-tab so both views stay in lock-step.
                var accessRoleList = new List<string>();
                accessRoleList.Add("Admin");           // Platform-level
                accessRoleList.Add("Coordinator");
                accessRoleList.Add("Viewer");
                accessRoleList.Add("External");
                accessRoleList.Add("— ISO 19650 Roles —");
                foreach (var r in GetDefaultRoles())
                    accessRoleList.Add($"{r.Code} — {r.Name}");
                var accessRoleStyle = new Style(typeof(ComboBox));
                accessRoleStyle.Setters.Add(new Setter(ComboBox.IsEditableProperty, true));

                // Phase 102 — separate Name and Email columns (user feedback).
                // Previously both were merged into a single "Name / Email"
                // column bound only to Name, so the email field on each
                // TeamMemberRow was invisible on this grid.
                var accessGrid = MakeExcelDataGrid(130);
                accessGrid.IsReadOnly = false;
                accessGrid.Columns.Add(new DataGridTextColumn     { Header = "Name",    Binding = new System.Windows.Data.Binding("Name"),    Width = new DataGridLength(1.4, DataGridLengthUnitType.Star) });
                accessGrid.Columns.Add(new DataGridTextColumn     { Header = "Email",   Binding = new System.Windows.Data.Binding("Email"),   Width = new DataGridLength(1.8, DataGridLengthUnitType.Star) });
                accessGrid.Columns.Add(new DataGridTextColumn     { Header = "Company", Binding = new System.Windows.Data.Binding("Company"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                accessGrid.Columns.Add(new DataGridComboBoxColumn { Header = "Role",    ItemsSource = accessRoleList, SelectedItemBinding = new System.Windows.Data.Binding("Role"), Width = 160, EditingElementStyle = accessRoleStyle });
                accessGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Active",  Binding = new System.Windows.Data.Binding("Active"),  Width = 55 });
                accessGrid.ItemsSource = _data.TeamMembers;
                detailStack.Children.Add(accessGrid);

                // Phase 102 — quick actions on the access grid
                var accessToolbar = new WrapPanel { Margin = new Thickness(0, 4, 0, 10) };
                var addMemberBtnP = new Button
                {
                    Content = "\u2795 Add Member", Height = 24, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 0),
                    Background = Br(CGreen), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 10, Cursor = Cursors.Hand,
                    ToolTip = "Add a new team member. Name + Email are editable inline after insertion."
                };
                addMemberBtnP.Click += (s, e) =>
                {
                    _data.TeamMembers.Add(new TeamMemberRow { Active = true, Role = "Viewer" });
                    accessGrid.Items.Refresh();
                };
                accessToolbar.Children.Add(addMemberBtnP);

                var inviteBtn = new Button
                {
                    Content = "\ud83d\udce7 Invite Selected", Height = 24, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 0),
                    Background = Br(Color.FromRgb(0x15, 0x65, 0xC0)), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 10, Cursor = Cursors.Hand,
                    ToolTip = "Copy an invite email body (link + role) for the highlighted row to the clipboard."
                };
                inviteBtn.Click += (s, e) =>
                {
                    if (accessGrid.SelectedItem is TeamMemberRow tm && !string.IsNullOrWhiteSpace(tm.Email))
                    {
                        string invite =
                            $"Subject: Planscape invitation — {_data.ProjectName}\n\n" +
                            $"Hi {tm.Name},\n\n" +
                            $"You've been added to the Planscape project '{_data.ProjectName}' as {tm.Role}.\n" +
                            $"Sign in at {BIMManager.PlanscapeServerClient.Instance.ServerUrl}\n\n" +
                            "\u2014 Sent from BIM Coordination Center";
                        try { System.Windows.Clipboard.SetText(invite); } catch { }
                        ShowStatus($"Invite copied for {tm.Name}");
                    }
                    else ShowStatus("Select a row with an email first.");
                };
                accessToolbar.Children.Add(inviteBtn);

                var savePlBtn = new Button
                {
                    Content = "\ud83d\udcbe Save Access", Height = 24, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 0),
                    Background = Br(CAccent), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 10, Cursor = Cursors.Hand,
                    ToolTip = "Persist the access roster to _bim_manager/team_members.json"
                };
                savePlBtn.Click += (s, e) => DispatchAction("SaveProjectMembers");
                accessToolbar.Children.Add(savePlBtn);
                detailStack.Children.Add(accessToolbar);

                // ── Server Connection ────────────────────────────────────────
                bool sbConnected = BIMManager.PlanscapeServerClient.Instance.IsConnected;
                var connStatus = new TextBlock
                {
                    Text = sbConnected
                        ? $"🟢 Connected — {BIMManager.PlanscapeServerClient.Instance.ConnectedUser}  |  {BIMManager.PlanscapeServerClient.Instance.TierName}"
                        : "🔴 Not connected",
                    FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Foreground = sbConnected ? Br(CGreen) : Br(CRed),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                detailStack.Children.Add(connStatus);

                detailStack.Children.Add(new TextBlock { Text = "SERVER CONNECTION", FontWeight = FontWeights.Bold, FontSize = 11, Foreground = Br(CAccent), Margin = new Thickness(0, 0, 0, 4) });

                // Pre-load saved connection settings so the fields are populated on open
                string _savedUrl = BIMManager.PlanscapeServerClient.Instance.ServerUrl;
                string _savedEmail = "";
                if (!sbConnected && !string.IsNullOrEmpty(_data.FilePath))
                {
                    try
                    {
                        string _cfgDir = System.IO.Path.Combine(
                            System.IO.Path.GetDirectoryName(_data.FilePath) ?? "",
                            "STING_BIM_MANAGER");
                        string _cfgPath = System.IO.Path.Combine(_cfgDir, "planscape_connection.json");
                        var (_url, _email, _pid) = BIMManager.PlanscapeServerClient.LoadConnectionSettings(_cfgPath);
                        if (!string.IsNullOrEmpty(_url))   _savedUrl   = _url;
                        if (!string.IsNullOrEmpty(_email)) _savedEmail = _email;
                    }
                    catch { /* ignore — config file optional */ }
                }
                else if (sbConnected)
                {
                    _savedEmail = BIMManager.PlanscapeServerClient.Instance.ConnectedUser;
                }

                var sbUrlRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                sbUrlRow.Children.Add(new TextBlock { Text = "Server URL:", Width = 90, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
                var sbUrlBox = new System.Windows.Controls.TextBox
                {
                    Width = 270, FontSize = 11, Margin = new Thickness(4, 0, 0, 0),
                    Text = !string.IsNullOrEmpty(_savedUrl) ? _savedUrl : "https://planscape-api.onrender.com",
                    ToolTip = "Planscape API server URL (your Render.com deployment)"
                };
                sbUrlRow.Children.Add(sbUrlBox);
                detailStack.Children.Add(sbUrlRow);

                var sbEmailRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                sbEmailRow.Children.Add(new TextBlock { Text = "Email:", Width = 90, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
                var sbEmailBox = new System.Windows.Controls.TextBox { Width = 200, FontSize = 11, Margin = new Thickness(4, 0, 0, 0), Text = _savedEmail, ToolTip = "Your Planscape account email" };
                sbEmailRow.Children.Add(sbEmailBox);
                detailStack.Children.Add(sbEmailRow);

                var sbPassRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 6) };
                sbPassRow.Children.Add(new TextBlock { Text = "Password:", Width = 90, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
                var sbPassBox = new System.Windows.Controls.PasswordBox { Width = 200, FontSize = 11, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Password is never saved to disk — only held in memory for this session" };
                sbPassRow.Children.Add(sbPassBox);
                detailStack.Children.Add(sbPassRow);

                var sbConnBtnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
                var sbConnBtn  = new Button { Content = "Connect", Height = 28, Padding = new Thickness(12, 0, 12, 0), Background = Br(CGreen),          Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 0) };
                var sbDiscoBtn = new Button { Content = "Disconnect", Height = 28, Padding = new Thickness(12, 0, 12, 0), Background = Br(CRed),          Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 0), IsEnabled = sbConnected };
                sbConnBtn.Click += (s, e) =>
                {
                    StingCommandHandler.SetExtraParam("PlanscapeServerUrl", sbUrlBox.Text.Trim());
                    StingCommandHandler.SetExtraParam("PlanscapeEmail", sbEmailBox.Text.Trim());
                    StingCommandHandler.SetExtraParam("PlanscapePassword", sbPassBox.Password);
                    DispatchAction("PlanscapeConnect");
                    // Refresh the panel after connect completes (Revit external event is async)
                    var refreshTimer = new System.Windows.Threading.DispatcherTimer
                        { Interval = TimeSpan.FromSeconds(2) };
                    refreshTimer.Tick += (ts, te) => { refreshTimer.Stop(); ShowPlatformDetail("Planscape"); };
                    refreshTimer.Start();
                };
                sbDiscoBtn.Click += (s, e) =>
                {
                    DispatchAction("PlanscapeDisconnect");
                    // Disconnect is synchronous — refresh immediately on next idle cycle
                    Dispatcher.BeginInvoke(new Action(() => ShowPlatformDetail("Planscape")),
                        System.Windows.Threading.DispatcherPriority.Background);
                };
                sbConnBtnRow.Children.Add(sbConnBtn); sbConnBtnRow.Children.Add(sbDiscoBtn);

                // Phase 102: when connected, add a third button to open the
                // web dashboard so coordinators can jump from BCC straight
                // into the browser viewer / mobile hand-off.
                if (sbConnected)
                {
                    var openWebBtn = new Button
                    {
                        Content = "\ud83c\udf10 Open Web Dashboard", Height = 28, Padding = new Thickness(12, 0, 12, 0),
                        Background = Br(Color.FromRgb(0x15, 0x65, 0xC0)), Foreground = Brushes.White,
                        BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand,
                        Margin = new Thickness(0, 0, 6, 0),
                        ToolTip = "Open the Planscape web dashboard in your default browser (mobile app also uses this URL)"
                    };
                    openWebBtn.Click += (s, e) => DispatchAction("PlanscapeOpenWebDashboard");
                    sbConnBtnRow.Children.Add(openWebBtn);
                }
                detailStack.Children.Add(sbConnBtnRow);

                // ── Phase 102/103: Server status card (ALWAYS visible) ──
                // Even when offline the card tells the user what they'll get
                // once connected (tier / MIM / tenant / sync capabilities +
                // server URL they're pointing at). Previously the card only
                // rendered when sbConnected was true, so the user never saw
                // it — which they reported as "I can't see the rich connection
                // info you claimed to have added".
                detailStack.Children.Add(new TextBlock
                {
                    Text = "SERVER STATUS", FontWeight = FontWeights.Bold, FontSize = 11,
                    Foreground = Br(CAccent), Margin = new Thickness(0, 6, 0, 4)
                });
                {
                    var client = BIMManager.PlanscapeServerClient.Instance;
                    string userLine  = sbConnected
                        ? $"\ud83d\udc64  User: {client.ConnectedUser}"
                        : "\ud83d\udc64  User: (not signed in)";
                    string tierLine  = sbConnected
                        ? $"\ud83d\udce6  Tier: {client.TierName}   \u2022   MIM add-on: {(client.MimEnabled ? "enabled" : "disabled")}"
                        : "\ud83d\udce6  Tier: Starter (Free) — sign in to activate Professional / Premium / Enterprise seats";
                    string tenantLine = sbConnected
                        ? $"\ud83c\udfe2  Tenant: {client.TenantId}"
                        : "\ud83c\udfe2  Tenant: (not linked)";
                    string urlLine   = !string.IsNullOrEmpty(client.ServerUrl)
                        ? $"\ud83d\udd17  Server: {client.ServerUrl}"
                        : $"\ud83d\udd17  Server: (no URL set) \u2014 default https://planscape-api.onrender.com";
                    string errLine   = !string.IsNullOrEmpty(client.LastError)
                        ? $"\n\u26A0  Last error: {client.LastError}"
                        : "";
                    var infoBorder = new Border
                    {
                        Background = Br(sbConnected ? Color.FromRgb(0xE8, 0xF4, 0xFF) : Color.FromRgb(0xF5, 0xF5, 0xF5)),
                        BorderBrush = Br(sbConnected ? Color.FromRgb(0xBB, 0xDE, 0xFB) : Color.FromRgb(0xCC, 0xCC, 0xCC)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(10, 8, 10, 8),
                        Margin = new Thickness(0, 0, 0, 10)
                    };
                    var infoStack = new StackPanel();
                    infoStack.Children.Add(new TextBlock { Text = userLine,  FontSize = 11, Margin = new Thickness(0, 0, 0, 2) });
                    infoStack.Children.Add(new TextBlock { Text = tierLine,  FontSize = 11, Margin = new Thickness(0, 0, 0, 2), TextWrapping = TextWrapping.Wrap });
                    infoStack.Children.Add(new TextBlock { Text = tenantLine,FontSize = 10, Foreground = Br(Color.FromRgb(0x55, 0x55, 0x55)), Margin = new Thickness(0, 0, 0, 2) });
                    infoStack.Children.Add(new TextBlock { Text = urlLine + errLine, FontSize = 10, Foreground = Br(Color.FromRgb(0x55, 0x55, 0x55)), TextWrapping = TextWrapping.Wrap });
                    infoBorder.Child = infoStack;
                    detailStack.Children.Add(infoBorder);
                }

                // ── Phase 103: Feature overview card (what Planscape exposes) ──
                // Visible to every user regardless of connection state so the
                // BIM coordinator can see what the platform actually does
                // before committing to a sign-in.
                detailStack.Children.Add(new TextBlock
                {
                    Text = "PLATFORM FEATURES", FontWeight = FontWeights.Bold, FontSize = 11,
                    Foreground = Br(CAccent), Margin = new Thickness(0, 0, 0, 4)
                });
                {
                    var featBorder = new Border
                    {
                        Background = Br(Color.FromRgb(0xF8, 0xF5, 0xFF)),
                        BorderBrush = Br(Color.FromRgb(0xD1, 0xC4, 0xE9)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(10, 8, 10, 8),
                        Margin = new Thickness(0, 0, 0, 10)
                    };
                    var featStack = new StackPanel();
                    void AddFeat(string emoji, string title, string body)
                    {
                        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
                        row.Children.Add(new TextBlock { Text = emoji, FontSize = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Top });
                        var txt = new StackPanel();
                        txt.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold });
                        txt.Children.Add(new TextBlock { Text = body,  FontSize = 10, TextWrapping = TextWrapping.Wrap, Foreground = Br(Color.FromRgb(0x55, 0x55, 0x55)) });
                        row.Children.Add(txt);
                        featStack.Children.Add(row);
                    }
                    AddFeat("\ud83d\udd04", "Bi-directional tag sync",
                        "Tags, compliance snapshots and audit trails flow between Revit plugin and the web/mobile clients.");
                    AddFeat("\ud83d\udea8", "Issues + BCF 2.1 round-trip",
                        "Raise, assign, resolve issues with photo attachments, SLA gates and BCF interoperability for ACC / Navisworks / Solibri.");
                    AddFeat("\ud83d\udcc4", "CDE document lifecycle",
                        "WIP \u2192 SHARED \u2192 PUBLISHED \u2192 ARCHIVE transitions with approval chains, suitability codes and file upload.");
                    AddFeat("\ud83d\udd14", "Push notifications (FCM / APNs)",
                        "Mobile push for new issues, SLA breaches, revision creation and compliance drops.");
                    AddFeat("\ud83d\udcf1", "Mobile companion",
                        "On-site issue capture with photo + GPS, offline queue, 3D viewer, meeting action items.");
                    AddFeat("\ud83d\udcc8", "Real-time SignalR updates",
                        "Compliance %, warnings, issues and presence broadcast to the team without refreshing.");
                    AddFeat("\ud83d\udd0d", "Cross-project global search",
                        "Search tags, issues, documents, meetings across every project you belong to.");
                    AddFeat("\ud83d\udd17", "External platform connectors",
                        "ACC, Procore, Aconex, Trimble, Bentley iTwin, Viewpoint, SharePoint — all via the BCC platform tiles.");
                    featBorder.Child = featStack;
                    detailStack.Children.Add(featBorder);
                }

                // ── Sync settings ────────────────────────────────────────────
                detailStack.Children.Add(new TextBlock { Text = "SYNC OPTIONS", FontWeight = FontWeights.Bold, FontSize = 11, Foreground = Br(CAccent), Margin = new Thickness(0, 4, 0, 4) });
                var syncOptsPlanscape = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
                foreach (var opt in new[] { "Auto-sync on model save", "Include model snapshots", "Notify on new issues", "Send weekly digest", "Compliance snapshot on revision", "Real-time (SignalR) updates" })
                    syncOptsPlanscape.Children.Add(new CheckBox { Content = opt, Margin = new Thickness(0, 0, 14, 4), FontSize = 11 });
                detailStack.Children.Add(syncOptsPlanscape);

                var planscapeBtnRow = new StackPanel { Orientation = Orientation.Horizontal };
                var syncNowBtnS = new Button
                {
                    Content = "⬆ Sync Elements to Server", Height = 28, Padding = new Thickness(12, 0, 12, 0),
                    Background = Br(navyBrush.Color), Foreground = Brushes.White, BorderThickness = new Thickness(0),
                    FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 0),
                    IsEnabled = sbConnected,
                    ToolTip = sbConnected
                        ? "Push all tagged elements (ASS_TAG_1 parameters) to the Planscape server"
                        : "Connect to the Planscape server first"
                };
                var viewDashBtn = new Button { Content = "📊 HTML Dashboard", Height = 28, Padding = new Thickness(12, 0, 12, 0), Background = Br(Color.FromRgb(0x6A, 0x1B, 0x9A)), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Export standalone HTML dashboard (no login required — share with anyone)" };
                syncNowBtnS.Click += (s, e) => DispatchAction("PlanscapeSyncNow");
                viewDashBtn.Click += (s, e) => DispatchAction("PlanscapeHTML");
                planscapeBtnRow.Children.Add(syncNowBtnS); planscapeBtnRow.Children.Add(viewDashBtn);
                detailStack.Children.Add(planscapeBtnRow);

                var detailBorderS = new Border { Background = Br(CCardBg), BorderBrush = Br(CBorder), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(16), Child = detailStack };
                _platformDetailArea.Content = detailBorderS;
                return;
            }

            detailStack.Children.Add(new TextBlock { Text = platformName + " — Connection", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = navyBrush, Margin = new Thickness(0, 0, 0, 10) });

            var urlRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            urlRow.Children.Add(new TextBlock { Text = "Connection URL:", Width = 120, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
            urlRow.Children.Add(new System.Windows.Controls.TextBox { Width = 280, FontSize = 11, Margin = new Thickness(4, 0, 0, 0) });
            detailStack.Children.Add(urlRow);

            var userRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            userRow.Children.Add(new TextBlock { Text = "Username:", Width = 120, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
            userRow.Children.Add(new System.Windows.Controls.TextBox { Width = 200, FontSize = 11, Margin = new Thickness(4, 0, 0, 0) });
            detailStack.Children.Add(userRow);

            var tokenRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
            tokenRow.Children.Add(new TextBlock { Text = "API Token:", Width = 120, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
            tokenRow.Children.Add(new System.Windows.Controls.PasswordBox { Width = 200, FontSize = 11, Margin = new Thickness(4, 0, 0, 0) });
            detailStack.Children.Add(tokenRow);

            detailStack.Children.Add(new TextBlock { Text = "Sync Options:", FontWeight = FontWeights.SemiBold, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
            var syncOpts = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var opt in new[] { "Auto-sync on open", "Sync on close", "Notify on changes", "Validate names" })
                syncOpts.Children.Add(new CheckBox { Content = opt, Margin = new Thickness(0, 0, 12, 4), FontSize = 11 });
            detailStack.Children.Add(syncOpts);

            var btnRow2 = new StackPanel { Orientation = Orientation.Horizontal };
            var syncNowBtn = new Button { Content = "Sync Now", Height = 30, Padding = new Thickness(12, 0, 12, 0), Background = navyBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 8, 0) };
            var disconnBtn = new Button { Content = "Disconnect", Height = 30, Padding = new Thickness(12, 0, 12, 0), Background = Br(CRed), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 8, 0) };
            var logsBtn   = new Button { Content = "View Logs",  Height = 30, Padding = new Thickness(12, 0, 12, 0), Background = Br(Color.FromRgb(0x45, 0x50, 0x6E)), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand };
            syncNowBtn.Click += (s, e) => DispatchAction("PlatformSync");
            disconnBtn.Click += (s, e) => DispatchAction($"Disconnect_{platformName}");
            logsBtn.Click    += (s, e) => DispatchAction($"ViewLogs_{platformName}");
            btnRow2.Children.Add(syncNowBtn); btnRow2.Children.Add(disconnBtn); btnRow2.Children.Add(logsBtn);
            detailStack.Children.Add(btnRow2);

            var detailBorder = new Border
            {
                Background = Br(CCardBg), BorderBrush = Br(CBorder),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16), Child = detailStack
            };
            _platformDetailArea.Content = detailBorder;
        }

        // ── Phase 77: Legacy platform code removed ──────────────────────
        private void _PlatformTab_LegacyFragment_REMOVED()
        {
            // Intentionally empty — old BuildPlatformTab legacy code removed in Phase 77.
            // Platform tab is now fully inline via BuildPlatformTab() + ShowPlatformDetail().
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

            // ── Workflow Preset Tabs (Phase 76 Item 9 — ToggleButton tab strip) ──
            stack.Children.Add(MakeSectionHeader("ALL WORKFLOW PRESETS"));
            stack.Children.Add(new TextBlock
            {
                Text = "Select a preset to configure steps, discipline filters, and schedule options.",
                FontSize = 10, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6)
            });

            // Preset definitions: [name, description, steps...]
            var workflowPresets = new (string Name, string Desc, string[] Steps)[]
            {
                ("Morning Check",   "Daily coordinator routine",
                    new[] { "Retag Stale Elements", "Auto-Fix Warnings", "Tag New Elements", "Pre-Tag Audit", "Validate Tags", "Assign Templates", "Tag Sheets", "Revision Check" }),
                ("Daily QA",        "Adaptive daily sync with compliance gates",
                    new[] { "Compliance Check", "Retag Stale", "Auto-Fix Anomalies", "Tag New Elements", "Validate Tags", "BOQ Update", "Register Export", "Completeness Dashboard", "Issue Tracking", "Meeting Minutes", "Template Check", "Revision Sync", "Final Compliance" }),
                ("Quick Fix",       "Rapid quality improvement cycle",
                    new[] { "Auto-Fix Anomalies", "Resolve All Warnings", "Retag Stale", "Validate Tags", "Completeness Report", "Register Update" }),
                ("Meeting Prep",    "BIM coordination meeting preparation",
                    new[] { "Compliance Check", "Warnings Summary", "Issue Export", "Clashes Report", "Revision Status", "BCF Export", "Meeting Pack" }),
                ("Clash Coord",     "Cross-discipline clash coordination",
                    new[] { "Detect Clashes", "Export BCF", "Create Issues", "Assign To Teams", "Link BCF to Tags" }),
                ("Handover",        "Pre-handover readiness check",
                    new[] { "Tag Stale Elements", "Full Tag Run", "Validate Tags", "Assign Templates", "COBie Export", "Document Register", "BOQ Export", "Update BEP", "Revision Check" }),
                ("Stage Gate",      "RIBA stage transition — full validation",
                    new[] { "Full Validation", "COBie Export", "Document Register", "BEP Update", "Revision Schedule", "Compliance Report", "BCF Archive", "Tag Register", "Stage Gate Check", "Client Package", "RIBA Sign-off" }),
                ("Weekly Drop",     "ISO 19650 weekly information exchange",
                    new[] { "Validate Tags", "Export Register", "COBie Package", "Document Pack", "Revision Check", "Naming Validate", "CDE Upload", "Delta Report" }),
                ("Kickoff",         "Full project setup from scratch",
                    new[] { "Load Shared Params", "Create Materials", "Create Wall Types", "Create Floor Types", "Create Ceiling Types", "Create Roof Types", "Create Duct Types", "Create Pipe Types", "Create Worksets", "Create View Templates", "Batch Schedules", "Auto-Populate Schedules", "Full Tag Run", "Validate Tags" }),
                ("Post-Tag QA",     "Post-tagging validation suite",
                    new[] { "Pre-Tag Audit", "Validate Tags", "Completeness Dashboard", "Tag Register", "Validate Template" }),
                ("Doc Package",     "Batch views, sheets, register, BOQ export",
                    new[] { "Batch Views", "Organise Sheets", "Sheet Index", "Document Register", "BOQ Export", "Transmittal", "Archive" }),
                ("BEP Package",     "BIM Execution Plan creation and export",
                    new[] { "Create BEP", "Enrich BEP Data", "Export BEP", "COBie Export", "Deliverables Package" }),
            };

            // ToggleButton tab strip
            var tabStrip = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            var toggleButtons = new List<ToggleButton>();
            _workflowPanelArea = new ContentControl { MinHeight = 280, Margin = new Thickness(0, 4, 0, 0) };

            foreach (var preset in workflowPresets)
            {
                var localPreset = preset; // capture for closure
                var tb = new ToggleButton
                {
                    Content = localPreset.Name,
                    Height = 28, Padding = new Thickness(12, 0, 12, 0),
                    Margin = new Thickness(0, 0, 4, 4),
                    FontSize = 11, Cursor = Cursors.Hand,
                    ToolTip = $"{localPreset.Desc} ({localPreset.Steps.Length} steps)"
                };
                // Style: navy when checked, lighter when unchecked
                tb.Checked += (s, e) =>
                {
                    foreach (var other in toggleButtons) if (!ReferenceEquals(other, tb)) other.IsChecked = false;
                    tb.Background = Br(CHeaderBg);
                    tb.Foreground = Brushes.White;
                    _workflowPanelArea.Content = BuildWorkflowPanel(localPreset.Name, localPreset.Desc, localPreset.Steps);
                };
                tb.Unchecked += (s, e) =>
                {
                    tb.Background = SystemColors.ControlBrush;
                    tb.Foreground = SystemColors.ControlTextBrush;
                };
                toggleButtons.Add(tb);
                tabStrip.Children.Add(tb);
            }
            stack.Children.Add(tabStrip);
            stack.Children.Add(_workflowPanelArea);

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

            // Phase 76: Key 4D/5D buttons show inline configuration panels instead of dispatching immediately
            actionsWrap.Children.Add(Make4DPanelButton("Auto Schedule 4D", "AutoSchedule4D", Br(CHeaderBg),
                "Configure and generate 4D timeline from model phases, levels, and trade sequence"));
            actionsWrap.Children.Add(Make4DPanelButton("Auto Cost 5D", "AutoCost5D", Br(CGreen),
                "Configure and calculate 5D cost estimate using cost_rates_5d.csv rate model"));
            actionsWrap.Children.Add(Make4DPanelButton("View Timeline", "ViewTimeline4D", Br(CAccent),
                "Configure and display interactive Gantt timeline with phase/trade breakdown"));
            actionsWrap.Children.Add(Make4DPanelButton("Cost Report", "CostReport5D", Br(Color.FromRgb(0x6A, 0x1B, 0x9A)),
                "Configure detailed 5D cost report: by category, discipline, phase with subtotals"));
            // Phase 98: all 4D/5D operations now reveal their inline configuration /
            // summary panels in-place rather than opening new TaskDialog windows that
            // break the BCC z-order and force the coordinator to hunt for the popup.
            // The switch in Build4DPanelFor already has cases for every tag below.
            actionsWrap.Children.Add(Make4DPanelButton("Cash Flow", "CashFlow5D", Br(Color.FromRgb(0x00, 0x69, 0x7C)),
                "S-curve cash flow forecast with monthly planned vs actual spend"));
            actionsWrap.Children.Add(Make4DPanelButton("Export Schedule", "ExportSchedule4D", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Export 4D schedule to CSV for Navisworks TimeLiner / Synchro import"));
            actionsWrap.Children.Add(Make4DPanelButton("Import MS Project", "ImportMSProject", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Import tasks from Microsoft Project XML/CSV for 4D integration"));
            actionsWrap.Children.Add(Make4DPanelButton("Milestone Register", "MilestoneRegister", Br(CAccent),
                "View/manage construction milestones with completion tracking"));
            actionsWrap.Children.Add(Make4DPanelButton("Phase Summary", "PhaseSummary", Br(CHeaderBg),
                "Phase-by-phase summary: element counts, completion status, duration"));
            actionsWrap.Children.Add(Make4DPanelButton("Working Calendar", "WorkingCalendar", Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                "Configure working days, holidays, and shift patterns for scheduling"));
            actionsWrap.Children.Add(Make4DPanelButton("Navisworks Export", "NavisworksTimeLiner", Br(Color.FromRgb(0x00, 0x69, 0x7C)),
                "Export Navisworks TimeLiner CSV with element-to-task mapping"));
            actionsWrap.Children.Add(Make4DPanelButton("Element Cost Trace", "ElementCostTrace", Br(Color.FromRgb(0x6A, 0x1B, 0x9A)),
                "Trace cost allocation per element: material + labour + plant rates"));
            stack.Children.Add(actionsWrap);

            // Phase 76: Inline panel area — populated when a configuration button is clicked
            _4dPanelArea = new ContentControl { MinHeight = 300 };
            stack.Children.Add(_4dPanelArea);

            scroll.Content = stack;
            return scroll;
        }

        /// <summary>Creates a button that shows an inline configuration panel for 4D/5D operations.</summary>
        private Button Make4DPanelButton(string label, string tag, SolidColorBrush bg, string tooltip = null)
        {
            var btn = new Button
            {
                Content = label, Tag = tag,
                Height = 30, Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 6, 6),
                Background = bg, Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 11, Cursor = Cursors.Hand,
                ToolTip = tooltip
            };
            var origBg = bg;
            var c0 = origBg.Color;
            var hoverBrush = Br(Color.FromRgb(
                (byte)Math.Min(255, c0.R + 30),
                (byte)Math.Min(255, c0.G + 30),
                (byte)Math.Min(255, c0.B + 30)));
            btn.MouseEnter += (s, e) => { btn.Background = hoverBrush; };
            btn.MouseLeave += (s, e) => { btn.Background = origBg; };
            btn.Click += (s, e) => { Show4DPanel(tag); };
            return btn;
        }

        /// <summary>Shows the inline configuration panel for a given 4D/5D action tag.</summary>
        private void Show4DPanel(string tag)
        {
            if (_4dPanelArea != null)
                _4dPanelArea.Content = Build4DPanelFor(tag);
        }

        /// <summary>Builds an inline configuration panel for the given 4D/5D action tag.</summary>
        private FrameworkElement Build4DPanelFor(string tag)
        {
            var navyBrush = Br(CHeaderBg);
            var panelBorder = new Border
            {
                Background = Br(CCardBg),
                BorderBrush = Br(CBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 8, 0, 8)
            };

            switch (tag)
            {
                case "AutoSchedule4D":
                {
                    var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
                    sp.Children.Add(new TextBlock { Text = "Auto-Schedule Configuration", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8), Foreground = navyBrush });

                    // Phase combo
                    var phaseRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    phaseRow.Children.Add(new TextBlock { Text = "Phase:", Width = 130, VerticalAlignment = VerticalAlignment.Center });
                    var phaseCb = new ComboBox { Width = 200, Margin = new Thickness(4, 0, 0, 0) };
                    phaseCb.Items.Add("Phase 1"); phaseCb.Items.Add("Phase 2"); phaseCb.Items.Add("New Construction");
                    phaseCb.SelectedIndex = 0;
                    phaseRow.Children.Add(phaseCb);
                    sp.Children.Add(phaseRow);

                    // Start date
                    var dateRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    dateRow.Children.Add(new TextBlock { Text = "Start Date:", Width = 130, VerticalAlignment = VerticalAlignment.Center });
                    var datePicker = new DatePicker { Width = 200, Margin = new Thickness(4, 0, 0, 0), SelectedDate = DateTime.Today };
                    dateRow.Children.Add(datePicker);
                    sp.Children.Add(dateRow);

                    sp.Children.Add(new TextBlock { Text = "Trade Sequence (editable):", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });

                    var dg = new DataGrid
                    {
                        AutoGenerateColumns = false,
                        CanUserAddRows = true,
                        Height = 120,
                        Margin = new Thickness(0, 0, 0, 8)
                    };
                    dg.Columns.Add(new DataGridTextColumn { Header = "Phase", Binding = new System.Windows.Data.Binding("Phase"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    dg.Columns.Add(new DataGridTextColumn { Header = "Trade", Binding = new System.Windows.Data.Binding("Trade"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    dg.Columns.Add(new DataGridTextColumn { Header = "Duration Days", Binding = new System.Windows.Data.Binding("DurationDays"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    dg.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<ScheduleRow>
                    {
                        new ScheduleRow { Phase = "Phase 1", Trade = "Groundworks",    DurationDays = "14" },
                        new ScheduleRow { Phase = "Phase 1", Trade = "Structure",       DurationDays = "28" },
                        new ScheduleRow { Phase = "Phase 2", Trade = "MEP Rough-in",    DurationDays = "21" }
                    };
                    sp.Children.Add(dg);

                    var usePhasesCb = new CheckBox { Content = "Use model phases", Margin = new Thickness(0, 4, 0, 8) };
                    sp.Children.Add(usePhasesCb);

                    var runBtn = new Button
                    {
                        Content = "Run Auto-Schedule", Height = 32, Padding = new Thickness(16, 0, 16, 0),
                        Background = navyBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0),
                        FontSize = 12, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand,
                        HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0)
                    };
                    runBtn.Click += (s, e) => DispatchAction("AutoSchedule4D");
                    sp.Children.Add(runBtn);
                    panelBorder.Child = sp;
                    return panelBorder;
                }

                case "ViewTimeline4D":
                {
                    var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
                    sp.Children.Add(new TextBlock { Text = "Timeline Viewer", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8), Foreground = navyBrush });

                    var fromRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    fromRow.Children.Add(new TextBlock { Text = "From:", Width = 90, VerticalAlignment = VerticalAlignment.Center });
                    fromRow.Children.Add(new DatePicker { Width = 160, Margin = new Thickness(4, 0, 12, 0), SelectedDate = DateTime.Today });
                    fromRow.Children.Add(new TextBlock { Text = "To:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
                    fromRow.Children.Add(new DatePicker { Width = 160, SelectedDate = DateTime.Today.AddMonths(12) });
                    sp.Children.Add(fromRow);

                    var groupRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    groupRow.Children.Add(new TextBlock { Text = "Grouping:", Width = 90, VerticalAlignment = VerticalAlignment.Center });
                    var groupCb = new ComboBox { Width = 160, Margin = new Thickness(4, 0, 0, 0) };
                    groupCb.Items.Add("By Phase"); groupCb.Items.Add("By Trade"); groupCb.Items.Add("By Level"); groupCb.Items.Add("By Discipline");
                    groupCb.SelectedIndex = 0;
                    groupRow.Children.Add(groupCb);
                    sp.Children.Add(groupRow);

                    var zoomRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    zoomRow.Children.Add(new TextBlock { Text = "Zoom:", Width = 90, VerticalAlignment = VerticalAlignment.Center });
                    // Phase 104: TickFrequency + AutoToolTipPlacement so BIM coordinators see the
                    // exact slider value (previously "blind" — you dragged and guessed).
                    var zoomSlider = new Slider
                    {
                        Minimum = 1, Maximum = 10, Value = 5, Width = 200,
                        VerticalAlignment = VerticalAlignment.Center,
                        TickFrequency = 1, TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight,
                        IsSnapToTickEnabled = true, AutoToolTipPlacement = AutoToolTipPlacement.TopLeft
                    };
                    var zoomValueLabel = new TextBlock
                    {
                        Text = $"{zoomSlider.Value:F0}x", Width = 50,
                        FontWeight = FontWeights.SemiBold, FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0), Foreground = navyBrush, TextAlignment = TextAlignment.Right
                    };
                    zoomSlider.ValueChanged += (ss, ee) => zoomValueLabel.Text = $"{ee.NewValue:F0}x";
                    zoomRow.Children.Add(zoomSlider);
                    zoomRow.Children.Add(zoomValueLabel);
                    sp.Children.Add(zoomRow);

                    sp.Children.Add(new TextBlock
                    {
                        Text = "(Timeline renders when data is available)",
                        FontSize = 11, Foreground = Brushes.Gray, FontStyle = FontStyles.Italic,
                        Margin = new Thickness(0, 8, 0, 8)
                    });

                    var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
                    var viewBtn = new Button
                    {
                        Content = "View Timeline", Height = 32, Padding = new Thickness(16, 0, 16, 0),
                        Background = navyBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0),
                        FontSize = 12, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 8, 0)
                    };
                    viewBtn.Click += (s, e) => DispatchAction("ViewTimeline4D");
                    var exportPngBtn = new Button
                    {
                        Content = "Export PNG", Height = 32, Padding = new Thickness(16, 0, 16, 0),
                        Background = Br(CAccent), Foreground = Brushes.White, BorderThickness = new Thickness(0),
                        FontSize = 12, Cursor = Cursors.Hand
                    };
                    exportPngBtn.Click += (s, e) => DispatchAction("ExportTimeline4DPNG");
                    btnRow.Children.Add(viewBtn);
                    btnRow.Children.Add(exportPngBtn);
                    sp.Children.Add(btnRow);
                    panelBorder.Child = sp;
                    return panelBorder;
                }

                case "AutoCost5D":
                {
                    var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
                    sp.Children.Add(new TextBlock { Text = "5D Cost Configuration", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8), Foreground = navyBrush });

                    sp.Children.Add(new TextBlock { Text = "Cost Rates:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
                    var dg = new DataGrid
                    {
                        AutoGenerateColumns = false,
                        CanUserAddRows = true,
                        Height = 150,
                        Margin = new Thickness(0, 0, 0, 8)
                    };
                    // Phase 98: Unit column is now a strict dropdown so the
                    // 5D roll-up doesn't get salted with "m2"/"m²"/"sq.m"/"Sq m"
                    // inconsistencies that broke cost aggregation by unit.
                    var costUnitList = new List<string>
                    {
                        "m²", "m³", "m", "kg", "tonne", "no.", "item", "ea", "l/s", "kW", "kVA", "hour", "day", "sum"
                    };
                    var costUnitStyle = new Style(typeof(ComboBox));
                    costUnitStyle.Setters.Add(new Setter(ComboBox.IsEditableProperty, true));
                    dg.Columns.Add(new DataGridTextColumn     { Header = "Category",  Binding = new System.Windows.Data.Binding("Category"),                                          Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
                    dg.Columns.Add(new DataGridTextColumn     { Header = "Rate UGX",  Binding = new System.Windows.Data.Binding("RateUGX"),                                           Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    dg.Columns.Add(new DataGridTextColumn     { Header = "Rate USD",  Binding = new System.Windows.Data.Binding("RateUSD"),                                           Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    dg.Columns.Add(new DataGridComboBoxColumn { Header = "Unit",      ItemsSource = costUnitList, SelectedItemBinding = new System.Windows.Data.Binding("Unit"),       Width = new DataGridLength(1, DataGridLengthUnitType.Star), EditingElementStyle = costUnitStyle });
                    dg.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<CostRateRow>
                    {
                        new CostRateRow { Category = "Walls",       RateUGX = "850000",  RateUSD = "230",  Unit = "m²" },
                        new CostRateRow { Category = "Floors",      RateUGX = "650000",  RateUSD = "175",  Unit = "m²" },
                        new CostRateRow { Category = "Roofs",       RateUGX = "950000",  RateUSD = "260",  Unit = "m²" },
                        new CostRateRow { Category = "MEP Systems",  RateUGX = "1200000", RateUSD = "325",  Unit = "m²" },
                        new CostRateRow { Category = "Finishes",    RateUGX = "420000",  RateUSD = "115",  Unit = "m²" }
                    };
                    sp.Children.Add(dg);

                    // Phase 104: contingency and overhead sliders now show their live % value
                    // and snap to whole integers via TickFrequency/IsSnapToTickEnabled so the
                    // resulting cost estimate is reproducible.
                    var contingencyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    contingencyRow.Children.Add(new TextBlock { Text = "Contingency %:", Width = 130, VerticalAlignment = VerticalAlignment.Center });
                    var contingencySlider = new Slider
                    {
                        Minimum = 0, Maximum = 25, Value = 10, Width = 200,
                        VerticalAlignment = VerticalAlignment.Center,
                        TickFrequency = 1, IsSnapToTickEnabled = true,
                        TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight,
                        AutoToolTipPlacement = AutoToolTipPlacement.TopLeft
                    };
                    var contingencyValueLabel = new TextBlock
                    {
                        Text = $"{contingencySlider.Value:F0} %", Width = 55,
                        FontWeight = FontWeights.SemiBold, FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0), Foreground = navyBrush, TextAlignment = TextAlignment.Right
                    };
                    contingencySlider.ValueChanged += (ss, ee) => contingencyValueLabel.Text = $"{ee.NewValue:F0} %";
                    contingencyRow.Children.Add(contingencySlider);
                    contingencyRow.Children.Add(contingencyValueLabel);
                    sp.Children.Add(contingencyRow);

                    var overheadRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    overheadRow.Children.Add(new TextBlock { Text = "Overhead %:", Width = 130, VerticalAlignment = VerticalAlignment.Center });
                    var overheadSlider = new Slider
                    {
                        Minimum = 0, Maximum = 20, Value = 8, Width = 200,
                        VerticalAlignment = VerticalAlignment.Center,
                        TickFrequency = 1, IsSnapToTickEnabled = true,
                        TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight,
                        AutoToolTipPlacement = AutoToolTipPlacement.TopLeft
                    };
                    var overheadValueLabel = new TextBlock
                    {
                        Text = $"{overheadSlider.Value:F0} %", Width = 55,
                        FontWeight = FontWeights.SemiBold, FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0), Foreground = navyBrush, TextAlignment = TextAlignment.Right
                    };
                    overheadSlider.ValueChanged += (ss, ee) => overheadValueLabel.Text = $"{ee.NewValue:F0} %";
                    overheadRow.Children.Add(overheadSlider);
                    overheadRow.Children.Add(overheadValueLabel);
                    sp.Children.Add(overheadRow);

                    var runBtn = new Button
                    {
                        Content = "Run Cost Estimate", Height = 32, Padding = new Thickness(16, 0, 16, 0),
                        Background = Br(CGreen), Foreground = Brushes.White, BorderThickness = new Thickness(0),
                        FontSize = 12, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand,
                        HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0)
                    };
                    runBtn.Click += (s, e) => DispatchAction("AutoCost5D");
                    sp.Children.Add(runBtn);
                    panelBorder.Child = sp;
                    return panelBorder;
                }

                case "CostReport5D":
                {
                    var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
                    sp.Children.Add(new TextBlock { Text = "5D Cost Report", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8), Foreground = navyBrush });

                    var breakdownRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
                    breakdownRow.Children.Add(new TextBlock { Text = "Breakdown:", Width = 100, VerticalAlignment = VerticalAlignment.Center });
                    var breakdownCb = new ComboBox { Width = 180, Margin = new Thickness(4, 0, 0, 0) };
                    breakdownCb.Items.Add("By Category"); breakdownCb.Items.Add("By Level"); breakdownCb.Items.Add("By Phase"); breakdownCb.Items.Add("By Discipline");
                    breakdownCb.SelectedIndex = 0;
                    breakdownRow.Children.Add(breakdownCb);
                    sp.Children.Add(breakdownRow);

                    var includeXlsx = new CheckBox { Content = "Include XLSX", IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };
                    var includeDocx = new CheckBox { Content = "Include DOCX", Margin = new Thickness(0, 2, 0, 2) };
                    var includeContingency = new CheckBox { Content = "Include contingency", Margin = new Thickness(0, 2, 0, 8) };
                    sp.Children.Add(includeXlsx);
                    sp.Children.Add(includeDocx);
                    sp.Children.Add(includeContingency);

                    var generateBtn = new Button
                    {
                        Content = "Generate Report", Height = 32, Padding = new Thickness(16, 0, 16, 0),
                        Background = navyBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0),
                        FontSize = 12, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand,
                        HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0)
                    };
                    generateBtn.Click += (s, e) => DispatchAction("CostReport5D");
                    sp.Children.Add(generateBtn);
                    panelBorder.Child = sp;
                    return panelBorder;
                }

                case "ImportMSProject":
                {
                    // Phase 104 rewrite: "Import MS Project" replaced with a
                    // generic "Import Scheduling Data" panel that accepts
                    // every major scheduling tool's export format. Each tool
                    // has a dedicated column template pre-populated so the
                    // coordinator just picks the source tool, browses to the
                    // file, reviews the mapping, and imports — without having
                    // to remember what column number MS Project uses for
                    // "% Complete" (it's different from Primavera).
                    var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
                    sp.Children.Add(new TextBlock { Text = "Import Scheduling Data", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4), Foreground = navyBrush });
                    sp.Children.Add(new TextBlock
                    {
                        Text = "Supports MS Project, Primavera P6, Asta Powerproject, Synchro 4D, Navisworks TimeLiner, Deltek Open Plan and generic CSV. Pick your source tool to preload a column template; every field is editable.",
                        FontSize = 10, TextWrapping = TextWrapping.Wrap,
                        Foreground = Br(Color.FromRgb(0x55, 0x55, 0x55)),
                        Margin = new Thickness(0, 0, 0, 8)
                    });

                    // ── Source tool selector ──────────────────────────────
                    var toolRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                    toolRow.Children.Add(new TextBlock { Text = "Source Tool:", Width = 110, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
                    var toolCb = new ComboBox { Width = 280, Margin = new Thickness(4, 0, 0, 0) };
                    string[] toolOptions = new[]
                    {
                        "MS Project (*.mpp, *.xml, *.mpx, *.csv)",
                        "Primavera P6 (*.xer, *.xml, *.xls, *.csv)",
                        "Asta Powerproject (*.pp, *.astabase, *.csv)",
                        "Synchro 4D (*.spj, *.spx, *.csv)",
                        "Navisworks TimeLiner (*.csv, *.nwc TimeLiner export)",
                        "Deltek Open Plan (*.opp, *.csv)",
                        "Tilos (*.tlp, *.csv)",
                        "Elecosoft Powerproject (*.pp, *.csv)",
                        "Generic CSV (configurable)",
                        "Excel / XLSX (first sheet)"
                    };
                    foreach (var t in toolOptions) toolCb.Items.Add(t);
                    toolCb.SelectedIndex = 0;
                    toolRow.Children.Add(toolCb);
                    sp.Children.Add(toolRow);

                    // ── File browse ───────────────────────────────────────
                    var fileRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    fileRow.Children.Add(new TextBlock { Text = "Project File:", Width = 110, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
                    var fileTb = new System.Windows.Controls.TextBox { Width = 280, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
                    var browseBtn = new Button { Content = "Browse...", Height = 26, Padding = new Thickness(8, 0, 8, 0), Background = Br(Color.FromRgb(0xE0, 0xE0, 0xE8)), BorderThickness = new Thickness(1) };
                    browseBtn.Click += (s, e) =>
                    {
                        // Filter adapts to the selected source tool so the
                        // user doesn't have to scroll past 8 extensions that
                        // aren't theirs.
                        int ti = toolCb.SelectedIndex;
                        string filter = ti switch
                        {
                            0 => "MS Project|*.mpp;*.xml;*.mpx;*.csv|MPP|*.mpp|XML|*.xml|MPX|*.mpx|CSV|*.csv|All|*.*",
                            1 => "Primavera P6|*.xer;*.xml;*.xls;*.xlsx;*.csv|XER|*.xer|XML|*.xml|Excel|*.xls;*.xlsx|CSV|*.csv|All|*.*",
                            2 => "Asta Powerproject|*.pp;*.astabase;*.csv|PP|*.pp|Asta|*.astabase|CSV|*.csv|All|*.*",
                            3 => "Synchro 4D|*.spj;*.spx;*.csv|Synchro|*.spj;*.spx|CSV|*.csv|All|*.*",
                            4 => "Navisworks TimeLiner|*.csv;*.xml|CSV|*.csv|XML|*.xml|All|*.*",
                            5 => "Deltek Open Plan|*.opp;*.csv|OPP|*.opp|CSV|*.csv|All|*.*",
                            6 => "Tilos|*.tlp;*.csv|TLP|*.tlp|CSV|*.csv|All|*.*",
                            7 => "Powerproject|*.pp;*.csv|PP|*.pp|CSV|*.csv|All|*.*",
                            8 => "Generic CSV|*.csv;*.tsv;*.txt|All|*.*",
                            _ => "Excel|*.xls;*.xlsx;*.xlsm|All|*.*"
                        };
                        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = filter, Title = $"Open {toolCb.SelectedItem}" };
                        if (dlg.ShowDialog(this) == true) fileTb.Text = dlg.FileName;
                    };
                    fileRow.Children.Add(fileTb); fileRow.Children.Add(browseBtn);
                    sp.Children.Add(fileRow);

                    // ── Comprehensive column mapping grid ─────────────────
                    sp.Children.Add(new TextBlock { Text = "Column Mapping (19 STING fields):", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
                    var mapDg = MakeExcelDataGrid(280);
                    mapDg.IsReadOnly = false;
                    mapDg.CanUserAddRows = false;
                    mapDg.CanUserDeleteRows = false;
                    mapDg.Columns.Add(new DataGridTextColumn     { Header = "STING Field",       Binding = new System.Windows.Data.Binding("StingField"),  Width = new DataGridLength(1.2, DataGridLengthUnitType.Star), IsReadOnly = true });
                    mapDg.Columns.Add(new DataGridTextColumn     { Header = "Source Column",     Binding = new System.Windows.Data.Binding("SourceColumn"), Width = new DataGridLength(1.8, DataGridLengthUnitType.Star) });
                    mapDg.Columns.Add(new DataGridComboBoxColumn { Header = "Type",              ItemsSource = new[] { "Text", "Date", "Duration", "Integer", "Decimal", "Percent", "Currency", "Bool" }, SelectedItemBinding = new System.Windows.Data.Binding("DataType"), Width = 100 });
                    mapDg.Columns.Add(new DataGridCheckBoxColumn { Header = "Required",         Binding = new System.Windows.Data.Binding("Required"),    Width = 70 });
                    var mapSrc = new System.Collections.ObjectModel.ObservableCollection<ScheduleMappingRow>();
                    // Default is the MS Project column palette — switching tool
                    // rewrites SourceColumn below in SelectionChanged.
                    ReseedScheduleMapping(mapSrc, toolCb.SelectedIndex);
                    mapDg.ItemsSource = mapSrc;
                    sp.Children.Add(mapDg);

                    // Re-seed when the user picks a different source tool
                    toolCb.SelectionChanged += (s, e) => ReseedScheduleMapping(mapSrc, toolCb.SelectedIndex);

                    // ── Options ──────────────────────────────────────────
                    var optsRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 4) };
                    optsRow.Children.Add(new CheckBox { Content = "Header row present", IsChecked = true, Margin = new Thickness(0, 0, 14, 0), FontSize = 11 });
                    optsRow.Children.Add(new CheckBox { Content = "Auto-detect dependencies (FS/SS/FF/SF + lag)", IsChecked = true, Margin = new Thickness(0, 0, 14, 0), FontSize = 11 });
                    optsRow.Children.Add(new CheckBox { Content = "Match tasks to Revit phases by name", IsChecked = false, Margin = new Thickness(0, 0, 14, 0), FontSize = 11 });
                    optsRow.Children.Add(new CheckBox { Content = "Create missing phases", IsChecked = false, FontSize = 11 });
                    sp.Children.Add(optsRow);

                    // ── Preview (first 10 rows) ──────────────────────────
                    sp.Children.Add(new TextBlock { Text = "Preview (first 10 rows — populated after import):", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
                    var previewDg = MakeExcelDataGrid(140);
                    previewDg.CanUserAddRows = false; previewDg.CanUserDeleteRows = false;
                    previewDg.Columns.Add(new DataGridTextColumn { Header = "Task ID",    Width = 70 });
                    previewDg.Columns.Add(new DataGridTextColumn { Header = "Task Name",  Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
                    previewDg.Columns.Add(new DataGridTextColumn { Header = "Start",      Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    previewDg.Columns.Add(new DataGridTextColumn { Header = "Finish",     Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    previewDg.Columns.Add(new DataGridTextColumn { Header = "Duration",   Width = 80 });
                    previewDg.Columns.Add(new DataGridTextColumn { Header = "% Complete", Width = 70 });
                    previewDg.Columns.Add(new DataGridTextColumn { Header = "WBS",        Width = 80 });
                    previewDg.Columns.Add(new DataGridTextColumn { Header = "Predecessors", Width = 110 });
                    sp.Children.Add(previewDg);

                    // ── Action buttons ───────────────────────────────────
                    var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                    var importBtn = new Button { Content = "Import", Height = 32, Padding = new Thickness(16, 0, 16, 0), Background = navyBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 12, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 0) };
                    importBtn.Click += (s, e) => DispatchAction("ImportMSProject");
                    var saveMapBtn = new Button { Content = "Save Mapping Template", Height = 32, Padding = new Thickness(12, 0, 12, 0), Background = Br(Color.FromRgb(0x2E, 0x7D, 0x32)), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Save the column mapping to project_config.json so future imports reuse it." };
                    var loadMapBtn = new Button { Content = "Load Mapping Template", Height = 32, Padding = new Thickness(12, 0, 12, 0), Background = Br(Color.FromRgb(0x45, 0x50, 0x6E)), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, ToolTip = "Load a previously saved mapping template from project_config.json." };
                    btnRow.Children.Add(importBtn); btnRow.Children.Add(saveMapBtn); btnRow.Children.Add(loadMapBtn);
                    sp.Children.Add(btnRow);

                    panelBorder.Child = sp;
                    return panelBorder;
                }

                case "MilestoneRegister":
                {
                    var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
                    sp.Children.Add(new TextBlock { Text = "Milestone Register", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8), Foreground = navyBrush });

                    var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                    var addRowBtn = new Button { Content = "+ Add Row", Height = 26, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 0), Background = Br(CGreen), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
                    var delRowBtn = new Button { Content = "Delete Row", Height = 26, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 0), Background = Br(CRed), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
                    var exportBtn = new Button { Content = "Export Excel", Height = 26, Padding = new Thickness(8, 0, 8, 0), Background = Br(Color.FromRgb(0x2E, 0x7D, 0x32)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
                    toolbar.Children.Add(addRowBtn); toolbar.Children.Add(delRowBtn); toolbar.Children.Add(exportBtn);
                    sp.Children.Add(toolbar);

                    // Phase 98: strict dropdowns on Discipline + Status so
                    // milestone reports roll up cleanly ("ARCH" vs "Architecture"
                    // vs "architecture" have historically fragmented status boards).
                    var mileDiscList = new List<string>
                    {
                        "A", "S", "M", "E", "P", "FP", "LV", "G", "C", "I", "All Disciplines"
                    };
                    var mileStatusList = new List<string>
                    {
                        "PLANNED", "IN PROGRESS", "AT RISK", "ACHIEVED", "MISSED", "CARRIED FORWARD", "CANCELLED"
                    };
                    var mileDg = MakeExcelDataGrid(180);
                    mileDg.Columns.Add(new DataGridTextColumn     { Header = "Milestone",   Binding = new System.Windows.Data.Binding("Milestone"),                                            Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
                    mileDg.Columns.Add(new DataGridTextColumn     { Header = "Date",        Binding = new System.Windows.Data.Binding("Date"),                                                 Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    mileDg.Columns.Add(new DataGridComboBoxColumn { Header = "Discipline",  ItemsSource = mileDiscList,   SelectedItemBinding = new System.Windows.Data.Binding("Discipline"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    mileDg.Columns.Add(new DataGridComboBoxColumn { Header = "Status",      ItemsSource = mileStatusList, SelectedItemBinding = new System.Windows.Data.Binding("Status"),     Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    mileDg.Columns.Add(new DataGridTextColumn { Header = "Notes",       Binding = new System.Windows.Data.Binding("Notes"),       Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
                    var mileSource = new System.Collections.ObjectModel.ObservableCollection<MilestoneEditRow>();
                    foreach (var m in _data.Milestones.Take(20))
                        mileSource.Add(new MilestoneEditRow { Milestone = m.Name, Date = "", Discipline = "", Status = m.IsComplete ? "Complete" : "In Progress", Notes = "" });
                    mileDg.ItemsSource = mileSource;

                    addRowBtn.Click += (s, e) => mileSource.Add(new MilestoneEditRow());
                    delRowBtn.Click += (s, e) => { if (mileDg.SelectedItem is MilestoneEditRow mr) mileSource.Remove(mr); };
                    exportBtn.Click += (s, e) => DispatchAction("ExportMilestones");
                    sp.Children.Add(mileDg);
                    panelBorder.Child = sp;
                    return panelBorder;
                }

                case "CashFlow5D":
                {
                    var sv5D = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 440 };
                    var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
                    sp.Children.Add(new TextBlock { Text = "5D Cash Flow Analysis", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8), Foreground = navyBrush });

                    var rangeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    rangeRow.Children.Add(new TextBlock { Text = "From:", Width = 50, VerticalAlignment = VerticalAlignment.Center });
                    rangeRow.Children.Add(new DatePicker { Width = 140, SelectedDate = DateTime.Today.AddMonths(-3), Margin = new Thickness(4, 0, 12, 0) });
                    rangeRow.Children.Add(new TextBlock { Text = "To:", Width = 30, VerticalAlignment = VerticalAlignment.Center });
                    rangeRow.Children.Add(new DatePicker { Width = 140, SelectedDate = DateTime.Today.AddMonths(9) });
                    sp.Children.Add(rangeRow);

                    var discRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
                    discRow.Children.Add(new TextBlock { Text = "Disciplines: ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                    foreach (var d in new[] { "Architecture", "Structure", "Mechanical", "Electrical", "Plumbing" })
                        discRow.Children.Add(new CheckBox { Content = d, IsChecked = true, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
                    sp.Children.Add(discRow);

                    // Simple bar chart on Canvas
                    sp.Children.Add(new TextBlock { Text = "Cash Flow Chart:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 4) });
                    var chartCanvas = new Canvas { Height = 120, Background = Br(Color.FromRgb(0xF8, 0xF9, 0xFB)), Margin = new Thickness(0, 0, 0, 8) };
                    chartCanvas.Loaded += (s, e) =>
                    {
                        chartCanvas.Children.Clear();
                        var data = _data.CashFlowForecast.Take(12).ToList();
                        if (data.Count == 0) return;
                        double maxVal = data.Max(d2 => Math.Max(d2.Planned, d2.Actual));
                        if (maxVal <= 0) maxVal = 1;
                        double canvasW = chartCanvas.ActualWidth > 10 ? chartCanvas.ActualWidth : 500;
                        double barW = Math.Max(10, (canvasW - 20) / (data.Count * 2 + 1));
                        double x = 10;
                        for (int i = 0; i < data.Count; i++)
                        {
                            double hP = data[i].Planned / maxVal * 100;
                            double hA = data[i].Actual / maxVal * 100;
                            var rP = new System.Windows.Shapes.Rectangle { Width = barW, Height = hP, Fill = Br(CHeaderBg), VerticalAlignment = VerticalAlignment.Bottom };
                            Canvas.SetLeft(rP, x); Canvas.SetBottom(rP, 0); chartCanvas.Children.Add(rP);
                            x += barW + 1;
                            var rA = new System.Windows.Shapes.Rectangle { Width = barW, Height = hA, Fill = Br(CAccent), VerticalAlignment = VerticalAlignment.Bottom };
                            Canvas.SetLeft(rA, x); Canvas.SetBottom(rA, 0); chartCanvas.Children.Add(rA);
                            x += barW + 4;
                        }
                    };
                    sp.Children.Add(chartCanvas);

                    var exportCsvBtn = new Button { Content = "Export CSV", Height = 32, Padding = new Thickness(16, 0, 16, 0), Background = Br(CGreen), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 12, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
                    exportCsvBtn.Click += (s, e) => DispatchAction("ExportCashFlow");
                    sp.Children.Add(exportCsvBtn);
                    sv5D.Content = sp;
                    panelBorder.Child = sv5D;
                    return panelBorder;
                }

                case "ExportSchedule4D":
                {
                    var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
                    sp.Children.Add(new TextBlock { Text = "Export 4D Schedule", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8), Foreground = navyBrush });

                    var fmtRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    fmtRow.Children.Add(new TextBlock { Text = "Format:", Width = 110, VerticalAlignment = VerticalAlignment.Center });
                    var fmtCb = new ComboBox { Width = 200, Margin = new Thickness(4, 0, 0, 0) };
                    foreach (var f in new[] { "CSV", "Navisworks TimeLiner", "Synchro Pro", "Primavera P6 XER" }) fmtCb.Items.Add(f);
                    fmtCb.SelectedIndex = 0;
                    fmtRow.Children.Add(fmtCb);
                    sp.Children.Add(fmtRow);

                    var scopeRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
                    scopeRow.Children.Add(new TextBlock { Text = "Scope: ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                    foreach (var sc in new[] { "All phases", "Current phase only", "Selected elements" })
                        scopeRow.Children.Add(new CheckBox { Content = sc, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
                    sp.Children.Add(scopeRow);

                    var outRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    outRow.Children.Add(new TextBlock { Text = "Output Path:", Width = 110, VerticalAlignment = VerticalAlignment.Center });
                    var outTb = new System.Windows.Controls.TextBox { Width = 260, Margin = new Thickness(4, 0, 4, 0) };
                    var outBrowse = new Button { Content = "...", Width = 28, Height = 26, Background = Br(Color.FromRgb(0xE0, 0xE0, 0xE8)), BorderThickness = new Thickness(1) };
                    outBrowse.Click += (s, e) =>
                    {
                        var dlg2 = new Microsoft.Win32.SaveFileDialog { Filter = "All Files|*.*", Title = "Save Schedule Export" };
                        if (dlg2.ShowDialog(this) == true) outTb.Text = dlg2.FileName;
                    };
                    outRow.Children.Add(outTb); outRow.Children.Add(outBrowse);
                    sp.Children.Add(outRow);

                    var expBtn2 = new Button { Content = "Export", Height = 32, Padding = new Thickness(16, 0, 16, 0), Background = navyBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 12, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
                    expBtn2.Click += (s, e) => DispatchAction("ExportSchedule4D");
                    sp.Children.Add(expBtn2);
                    panelBorder.Child = sp;
                    return panelBorder;
                }

                case "WorkingCalendar":
                {
                    var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
                    sp.Children.Add(new TextBlock { Text = "Working Calendar", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8), Foreground = navyBrush });

                    var calMonthYear = DateTime.Today;
                    var navRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                    var prevBtn = new Button { Content = "<", Width = 30, Height = 28, Background = Br(CHeaderBg), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
                    var monthLabel = new TextBlock { Text = calMonthYear.ToString("MMMM yyyy"), FontWeight = FontWeights.Bold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), Width = 130, TextAlignment = TextAlignment.Center };
                    var nextBtn = new Button { Content = ">", Width = 30, Height = 28, Background = Br(CHeaderBg), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
                    navRow.Children.Add(prevBtn); navRow.Children.Add(monthLabel); navRow.Children.Add(nextBtn);
                    sp.Children.Add(navRow);

                    // 7-column UniformGrid for days
                    var calGrid = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 8) };
                    foreach (var d in new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" })
                        calGrid.Children.Add(new Border { Background = Br(CHeaderBg), Padding = new Thickness(4), Child = new TextBlock { Text = d, FontWeight = FontWeights.Bold, FontSize = 10, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center } });

                    // Fill days
                    var firstDay = new DateTime(calMonthYear.Year, calMonthYear.Month, 1);
                    int offset = ((int)firstDay.DayOfWeek + 6) % 7; // Monday=0
                    for (int i = 0; i < offset; i++) calGrid.Children.Add(new Border());
                    int daysInMonth = DateTime.DaysInMonth(calMonthYear.Year, calMonthYear.Month);
                    for (int d = 1; d <= daysInMonth; d++)
                    {
                        var dt = new DateTime(calMonthYear.Year, calMonthYear.Month, d);
                        bool isWeekend = dt.DayOfWeek == DayOfWeek.Saturday || dt.DayOfWeek == DayOfWeek.Sunday;
                        var dayBg = isWeekend ? Br(Color.FromRgb(0xE0, 0xE0, 0xE0)) : Br(Color.FromRgb(0xE8, 0xF5, 0xE9));
                        calGrid.Children.Add(new Border { Background = dayBg, Margin = new Thickness(1), Padding = new Thickness(4), CornerRadius = new CornerRadius(2), Child = new TextBlock { Text = d.ToString(), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center } });
                    }
                    sp.Children.Add(calGrid);

                    var calBtnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                    var addHolidayBtn = new Button { Content = "+ Add Holiday", Height = 28, Padding = new Thickness(8, 0, 8, 0), Background = Br(CAmber), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 8, 0) };
                    var saveCalBtn = new Button { Content = "Save Calendar", Height = 28, Padding = new Thickness(8, 0, 8, 0), Background = navyBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
                    saveCalBtn.Click += (s, e) => DispatchAction("SaveWorkingCalendar");
                    calBtnRow.Children.Add(addHolidayBtn); calBtnRow.Children.Add(saveCalBtn);
                    sp.Children.Add(calBtnRow);
                    panelBorder.Child = sp;
                    return panelBorder;
                }

                case "ElementCostTrace":
                {
                    var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
                    sp.Children.Add(new TextBlock { Text = "Element Cost Trace", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8), Foreground = navyBrush });

                    var filterRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    filterRow.Children.Add(new TextBlock { Text = "Category:", Width = 90, VerticalAlignment = VerticalAlignment.Center });
                    var catCb = new ComboBox { Width = 160, Margin = new Thickness(4, 0, 16, 0) };
                    foreach (var c in new[] { "All", "Walls", "Floors", "Ceilings", "Roofs", "Mechanical Equipment", "Electrical Fixtures", "Plumbing Fixtures" }) catCb.Items.Add(c);
                    catCb.SelectedIndex = 0;
                    filterRow.Children.Add(catCb);
                    filterRow.Children.Add(new TextBlock { Text = "Discipline:", Width = 80, VerticalAlignment = VerticalAlignment.Center });
                    var discCb2 = new ComboBox { Width = 120, Margin = new Thickness(4, 0, 0, 0) };
                    foreach (var d in new[] { "All", "A", "S", "M", "E", "P", "FP" }) discCb2.Items.Add(d);
                    discCb2.SelectedIndex = 0;
                    filterRow.Children.Add(discCb2);
                    sp.Children.Add(filterRow);

                    sp.Children.Add(new TextBlock { Text = "Cost Rates (editable):", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
                    var rateDg = MakeExcelDataGrid(160);
                    // Phase 98: Unit dropdown — matches the 5D Cost Rates grid above.
                    var rateUnitList = new List<string>
                    {
                        "m²", "m³", "m", "kg", "tonne", "no.", "item", "ea", "l/s", "kW", "kVA", "hour", "day", "sum"
                    };
                    var rateUnitStyle = new Style(typeof(ComboBox));
                    rateUnitStyle.Setters.Add(new Setter(ComboBox.IsEditableProperty, true));
                    rateDg.Columns.Add(new DataGridTextColumn     { Header = "Category",    Binding = new System.Windows.Data.Binding("Category"),                                          Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
                    rateDg.Columns.Add(new DataGridComboBoxColumn { Header = "Unit",        ItemsSource = rateUnitList, SelectedItemBinding = new System.Windows.Data.Binding("Unit"),       Width = 70, EditingElementStyle = rateUnitStyle });
                    rateDg.Columns.Add(new DataGridTextColumn     { Header = "Rate UGX",    Binding = new System.Windows.Data.Binding("RateUGX"),                                           Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    rateDg.Columns.Add(new DataGridTextColumn     { Header = "Rate USD",    Binding = new System.Windows.Data.Binding("RateUSD"),                                           Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    rateDg.Columns.Add(new DataGridTextColumn     { Header = "Description", Binding = new System.Windows.Data.Binding("Description"),                                       Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
                    rateDg.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<ElementCostRow>
                    {
                        new ElementCostRow { Category = "Walls",   Unit = "m²", RateUGX = "850000",  RateUSD = "230",  Description = "Masonry / Blockwork" },
                        new ElementCostRow { Category = "Floors",  Unit = "m²", RateUGX = "650000",  RateUSD = "175",  Description = "RC Slab" },
                        new ElementCostRow { Category = "MEP",     Unit = "m²", RateUGX = "1200000", RateUSD = "325",  Description = "Mechanical, Electrical & Plumbing" }
                    };
                    sp.Children.Add(rateDg);

                    var ectBtnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                    var recalcBtn = new Button { Content = "Recalculate", Height = 30, Padding = new Thickness(12, 0, 12, 0), Background = Br(CAccent), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 8, 0) };
                    var expExcelBtn = new Button { Content = "Export Excel", Height = 30, Padding = new Thickness(12, 0, 12, 0), Background = Br(Color.FromRgb(0x2E, 0x7D, 0x32)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
                    recalcBtn.Click += (s, e) => DispatchAction("ElementCostTrace");
                    expExcelBtn.Click += (s, e) => ExportDataGridToXlsx(rateDg, "ElementCostRates");
                    ectBtnRow.Children.Add(recalcBtn); ectBtnRow.Children.Add(expExcelBtn);
                    sp.Children.Add(ectBtnRow);
                    panelBorder.Child = sp;
                    return panelBorder;
                }

                case "PhaseSummary":
                {
                    var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
                    sp.Children.Add(new TextBlock { Text = "Phase Summary", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8), Foreground = navyBrush });

                    var phaseDg = new DataGrid
                    {
                        AutoGenerateColumns = false, IsReadOnly = true,
                        CanUserSortColumns = true, CanUserResizeColumns = true,
                        HeadersVisibility = DataGridHeadersVisibility.Column,
                        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                        FontSize = 11, RowHeaderWidth = 0, MaxHeight = 200, Margin = new Thickness(0, 0, 0, 8)
                    };
                    phaseDg.Columns.Add(new DataGridTextColumn { Header = "Phase",           Binding = new System.Windows.Data.Binding("Phase"),        Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
                    phaseDg.Columns.Add(new DataGridTextColumn { Header = "Element Count",   Binding = new System.Windows.Data.Binding("ElementCount"),  Width = 100 });
                    phaseDg.Columns.Add(new DataGridTextColumn { Header = "Discipline",      Binding = new System.Windows.Data.Binding("Discipline"),    Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    phaseDg.Columns.Add(new DataGridTextColumn { Header = "Completion %",    Binding = new System.Windows.Data.Binding("CompletionPct"), Width = 90 });
                    phaseDg.Columns.Add(new DataGridTextColumn { Header = "Start",           Binding = new System.Windows.Data.Binding("Start"),         Width = 80 });
                    phaseDg.Columns.Add(new DataGridTextColumn { Header = "End",             Binding = new System.Windows.Data.Binding("End"),           Width = 80 });

                    // Progress bar template column
                    var progTemplate = new DataGridTemplateColumn { Header = "Progress", Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) };
                    var cellTemplate = new DataTemplate();
                    var factory = new FrameworkElementFactory(typeof(ProgressBar));
                    factory.SetBinding(ProgressBar.ValueProperty, new System.Windows.Data.Binding("CompletionPct") { StringFormat = "F0" });
                    factory.SetValue(ProgressBar.MinimumProperty, 0.0);
                    factory.SetValue(ProgressBar.MaximumProperty, 100.0);
                    factory.SetValue(FrameworkElement.HeightProperty, 16.0);
                    cellTemplate.VisualTree = factory;
                    progTemplate.CellTemplate = cellTemplate;
                    phaseDg.Columns.Add(progTemplate);

                    var phaseSrc = new System.Collections.ObjectModel.ObservableCollection<PhaseSummaryRow>();
                    foreach (var kv in _data.ByPhase.Take(10))
                        phaseSrc.Add(new PhaseSummaryRow { Phase = kv.Key, ElementCount = kv.Value.Total, Discipline = "All", CompletionPct = kv.Value.Pct, Start = "", End = "" });
                    phaseDg.ItemsSource = phaseSrc;
                    sp.Children.Add(phaseDg);
                    panelBorder.Child = sp;
                    return panelBorder;
                }

                case "NavisworksTimeLiner":
                {
                    var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
                    sp.Children.Add(new TextBlock { Text = "Navisworks TimeLiner Export", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8), Foreground = navyBrush });

                    var fmtPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
                    fmtPanel.Children.Add(new TextBlock { Text = "Output Format:", Width = 110, VerticalAlignment = VerticalAlignment.Center });
                    var nwdRb = new System.Windows.Controls.RadioButton { Content = "NWD", GroupName = "NavFmt", IsChecked = true, Margin = new Thickness(4, 0, 12, 0) };
                    var nwfRb = new System.Windows.Controls.RadioButton { Content = "NWF", GroupName = "NavFmt", Margin = new Thickness(0, 0, 12, 0) };
                    var fbxRb = new System.Windows.Controls.RadioButton { Content = "FBX", GroupName = "NavFmt" };
                    fmtPanel.Children.Add(nwdRb); fmtPanel.Children.Add(nwfRb); fmtPanel.Children.Add(fbxRb);
                    sp.Children.Add(fmtPanel);

                    sp.Children.Add(new TextBlock { Text = "Element-to-Task Mapping:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 4) });
                    var mapDg = MakeExcelDataGrid(160);
                    mapDg.Columns.Add(new DataGridTextColumn { Header = "Revit Category", Binding = new System.Windows.Data.Binding("RevitCategory"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
                    mapDg.Columns.Add(new DataGridTextColumn { Header = "Task Name",      Binding = new System.Windows.Data.Binding("TaskName"),      Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
                    mapDg.Columns.Add(new DataGridTextColumn { Header = "Phase",          Binding = new System.Windows.Data.Binding("Phase"),          Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    mapDg.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<NavisTaskRow>
                    {
                        new NavisTaskRow { RevitCategory = "Walls",   TaskName = "Wall Construction",   Phase = "Phase 1" },
                        new NavisTaskRow { RevitCategory = "Floors",  TaskName = "Floor Installation",  Phase = "Phase 1" },
                        new NavisTaskRow { RevitCategory = "Roofs",   TaskName = "Roofing Works",       Phase = "Phase 2" }
                    };
                    sp.Children.Add(mapDg);

                    var navExpBtn = new Button { Content = "Export to Navisworks", Height = 32, Padding = new Thickness(16, 0, 16, 0), Background = navyBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 12, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
                    navExpBtn.Click += (s, e) => DispatchAction("NavisworksTimeLiner");
                    sp.Children.Add(navExpBtn);
                    panelBorder.Child = sp;
                    return panelBorder;
                }

                default:
                {
                    var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
                    sp.Children.Add(new TextBlock
                    {
                        Text = $"Panel for: {tag}",
                        FontSize = 13, FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 0, 0, 8),
                        Foreground = navyBrush
                    });
                    var runBtn = new Button
                    {
                        Content = "Run", Height = 32, Padding = new Thickness(16, 0, 16, 0),
                        Background = navyBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0),
                        FontSize = 12, Cursor = Cursors.Hand,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    runBtn.Click += (s, e) => DispatchAction(tag);
                    sp.Children.Add(runBtn);
                    panelBorder.Child = sp;
                    return panelBorder;
                }
            }
        }

        // Helper row classes for 4D/5D DataGrids
        private class ScheduleRow
        {
            public string Phase        { get; set; }
            public string Trade        { get; set; }
            public string DurationDays { get; set; }
        }

        private class CostRateRow
        {
            public string Category { get; set; }
            public string RateUGX  { get; set; }
            public string RateUSD  { get; set; }
            public string Unit     { get; set; }
        }

        private class MilestoneEditRow
        {
            public string Milestone  { get; set; }
            public string Date       { get; set; }
            public string Discipline { get; set; }
            public string Status     { get; set; }
            public string Notes      { get; set; }
        }

        private class ElementCostRow
        {
            public string Category    { get; set; }
            public string Unit        { get; set; }
            public string RateUGX     { get; set; }
            public string RateUSD     { get; set; }
            public string Description { get; set; }
        }

        private class PhaseSummaryRow
        {
            public string Phase         { get; set; }
            public int    ElementCount  { get; set; }
            public string Discipline    { get; set; }
            public double CompletionPct { get; set; }
            public string Start         { get; set; }
            public string End           { get; set; }
        }

        private class NavisTaskRow
        {
            public string RevitCategory { get; set; }
            public string TaskName      { get; set; }
            public string Phase         { get; set; }
        }

        /// <summary>Phase 104: one row per STING 4D/5D field that the import
        /// pipeline recognises. 19 fields cover every scheduling tool we
        /// claim to support — unmapped fields are left blank (so the user
        /// can opt out of a field) and required fields are flagged for
        /// validation before import runs.</summary>
        private class ScheduleMappingRow
        {
            public string StingField   { get; set; }
            public string SourceColumn { get; set; }
            public string DataType     { get; set; }
            public bool   Required     { get; set; }
        }

        /// <summary>Phase 104: per-tool column templates. Each entry maps the
        /// STING field to the column name the source tool exports by default
        /// (verified against each product's CSV / XML schema). The user can
        /// still edit any row after seeding.</summary>
        private static void ReseedScheduleMapping(
            System.Collections.ObjectModel.ObservableCollection<ScheduleMappingRow> rows,
            int toolIndex)
        {
            rows.Clear();
            // Column 1: MS Project, 2: Primavera P6, 3: Asta Powerproject,
            // 4: Synchro 4D, 5: Navisworks TimeLiner, 6: Deltek Open Plan,
            // 7: Tilos, 8: Powerproject, 9: Generic CSV, 10: Excel/XLSX.
            var perTool = new Dictionary<string, string[]>
            {
                ["Task ID"]              = new[] { "ID",            "task_id",         "TaskId",       "TaskID",        "Task ID",        "ID",          "ID",          "ID",          "id",          "A" },
                ["Task Name"]            = new[] { "Name",          "task_name",       "Name",         "TaskName",      "Display Name",   "Name",        "Name",        "Name",        "name",        "B" },
                ["WBS"]                  = new[] { "WBS",           "wbs_short_name",  "WBS",          "WBS",           "WBS",            "WBS",         "WBS",         "WBS",         "wbs",         "C" },
                ["Outline Level"]        = new[] { "Outline Level", "wbs_level",       "Level",        "OutlineLevel",  "Level",          "Level",       "Level",       "Level",       "level",       "D" },
                ["Start"]                = new[] { "Start",         "start_date",      "Start",        "StartDate",     "Planned Start",  "Start",       "Start",       "Start",       "start",       "E" },
                ["Finish"]               = new[] { "Finish",        "end_date",        "Finish",       "FinishDate",    "Planned End",    "Finish",      "Finish",      "Finish",      "finish",      "F" },
                ["Duration"]             = new[] { "Duration",      "target_drtn_hr_cnt", "Duration",  "Duration",      "Duration",       "Duration",    "Duration",    "Duration",    "duration",    "G" },
                ["% Complete"]           = new[] { "% Complete",    "phys_complete_pct", "% Complete","PercentComplete","% Complete",     "% Complete",  "% Complete",  "% Complete",  "percent",     "H" },
                ["Predecessors"]         = new[] { "Predecessors",  "pred_task_id",    "Predecessors", "Predecessors",  "Predecessor IDs","Predecessors","Predecessors","Predecessors","predecessors","I" },
                ["Baseline Start"]       = new[] { "Baseline Start","bl1_start_date",  "Baseline St",  "BaselineStart", "Baseline Start", "Baseline Start","Baseline Start","Baseline Start","baseline_start","J" },
                ["Baseline Finish"]      = new[] { "Baseline Finish","bl1_end_date",   "Baseline Fn",  "BaselineFinish","Baseline End",   "Baseline Finish","Baseline Finish","Baseline Finish","baseline_finish","K" },
                ["Actual Start"]         = new[] { "Actual Start",  "act_start_date",  "Actual St",    "ActualStart",   "Actual Start",   "Actual Start","Actual Start","Actual Start","actual_start","L" },
                ["Actual Finish"]        = new[] { "Actual Finish", "act_end_date",    "Actual Fn",    "ActualFinish",  "Actual End",     "Actual Finish","Actual Finish","Actual Finish","actual_finish","M" },
                ["Resources"]            = new[] { "Resource Names","rsrc_name",       "Resources",    "Resources",     "Resources",      "Resources",   "Resources",   "Resources",   "resources",   "N" },
                ["Cost"]                 = new[] { "Cost",          "target_total_cost","Cost",        "Cost",          "Cost",           "Cost",        "Cost",        "Cost",        "cost",        "O" },
                ["Discipline"]           = new[] { "Text1",         "text1_code",      "Discipline",   "Discipline",    "Discipline",     "Discipline",  "Discipline",  "Discipline",  "discipline",  "P" },
                ["Revit Phase"]          = new[] { "Text2",         "text2_code",      "Phase",        "PhaseId",       "Phase",          "Phase",       "Phase",       "Phase",       "phase",       "Q" },
                ["Constraint Type"]      = new[] { "Constraint Type","restart_date",   "Constraint",   "ConstraintType","Constraint",     "Constraint Type","Constraint","Constraint","constraint_type","R" },
                ["Notes"]                = new[] { "Notes",         "notes",           "Notes",        "Notes",         "Notes",          "Notes",       "Notes",       "Notes",       "notes",       "S" }
            };
            int col = Math.Max(0, Math.Min(toolIndex, 9));
            foreach (var kv in perTool)
            {
                bool isRequired = kv.Key == "Task Name" || kv.Key == "Start" || kv.Key == "Finish";
                string defaultType = kv.Key switch
                {
                    "Start" or "Finish" or "Baseline Start" or "Baseline Finish" or "Actual Start" or "Actual Finish" => "Date",
                    "Duration" => "Duration",
                    "% Complete" => "Percent",
                    "Cost" => "Currency",
                    "Outline Level" or "Task ID" => "Integer",
                    _ => "Text"
                };
                rows.Add(new ScheduleMappingRow
                {
                    StingField   = kv.Key,
                    SourceColumn = kv.Value[col],
                    DataType     = defaultType,
                    Required     = isRequired
                });
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  ISSUES DYNAMIC CONTEXT PANELS  (Phase 76 Item 14)
        // ════════════════════════════════════════════════════════════════

        private Button MakeIssueContextButton(string label, string actionTag, SolidColorBrush bg, string tooltip = null)
        {
            var btn = new Button
            {
                Content = label, Tag = actionTag,
                Height = 30, Padding = new Thickness(10, 0, 10, 0),
                Background = bg, Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand,
                ToolTip = tooltip
            };
            var origBg = bg;
            var c0 = origBg.Color;
            var hoverBrush = Br(Color.FromRgb(
                (byte)Math.Min(255, c0.R + 30),
                (byte)Math.Min(255, c0.G + 30),
                (byte)Math.Min(255, c0.B + 30)));
            btn.MouseEnter += (s, e) => btn.Background = hoverBrush;
            btn.MouseLeave += (s, e) => btn.Background = origBg;
            btn.Click += (s, e) =>
            {
                if (_issueContextArea != null)
                    _issueContextArea.Content = BuildIssueContextPanelFor(actionTag);
            };
            return btn;
        }

        private FrameworkElement BuildIssueContextPanelFor(string tag)
        {
            var navyBrush = Br(CHeaderBg);
            var panel = new Border
            {
                Background = Br(CCardBg), BorderBrush = Br(CBorder),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16), Margin = new Thickness(0, 4, 0, 4)
            };

            switch (tag)
            {
                case "RaiseIssue":
                {
                    var sp = new StackPanel();
                    sp.Children.Add(new TextBlock { Text = "Raise New Issue", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = navyBrush, Margin = new Thickness(0, 0, 0, 8) });
                    var typeRow = MakeCtxRow("Issue Type:");
                    var typeCb = new System.Windows.Controls.ComboBox { Width = 200 };
                    foreach (var it in IsoIssueTypes) typeCb.Items.Add(new ComboBoxItem { Content = $"{it.Code} — {it.Label}", Tag = it.Code, ToolTip = it.Description });
                    typeCb.SelectedIndex = 0;
                    typeRow.Children.Add(typeCb);
                    sp.Children.Add(typeRow);
                    var priRow = MakeCtxRow("Priority:");
                    var priCb = new System.Windows.Controls.ComboBox { Width = 120 };
                    foreach (var p in new[] { "LOW", "MEDIUM", "HIGH", "CRITICAL" }) priCb.Items.Add(p);
                    priCb.SelectedIndex = 2;
                    priRow.Children.Add(priCb);
                    sp.Children.Add(priRow);
                    var titleRow = MakeCtxRow("Title:");
                    var titleBox = new System.Windows.Controls.TextBox { Width = 340 };
                    titleRow.Children.Add(titleBox);
                    sp.Children.Add(titleRow);
                    var descRow = MakeCtxRow("Description:");
                    var descBox = new System.Windows.Controls.TextBox { Width = 340, Height = 48, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true };
                    descRow.Children.Add(descBox);
                    sp.Children.Add(descRow);
                    // ── Multi-person assignee ──
                    sp.Children.Add(new TextBlock { Text = "Assign To (multi-select):", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
                    var assignScroll2 = new ScrollViewer { MaxHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 6) };
                    var assignList2 = new WrapPanel();
                    var assignees2 = _data.TeamMembers.Count > 0
                        ? _data.TeamMembers.Select(m => m.Name).ToList()
                        : GetISOStandardMembers();
                    var assignChecks2 = new List<CheckBox>();
                    foreach (var name in assignees2)
                    {
                        var cb = new CheckBox { Content = name, FontSize = 10, Margin = new Thickness(0, 0, 10, 3) };
                        assignChecks2.Add(cb);
                        assignList2.Children.Add(cb);
                    }
                    assignScroll2.Content = assignList2;
                    sp.Children.Add(assignScroll2);
                    // ── Notify Recipients (CC) ──
                    sp.Children.Add(new TextBlock { Text = "Notify Recipients (CC):", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
                    var notifyScroll = new ScrollViewer { MaxHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 6) };
                    var notifyList = new WrapPanel();
                    var notifyNames = _data.TeamMembers.Count > 0
                        ? _data.TeamMembers.Select(m => m.Name).ToList()
                        : GetISOStandardMembers();
                    var notifyChecks = new List<CheckBox>();
                    foreach (var name in notifyNames)
                    {
                        var cb = new CheckBox { Content = name, FontSize = 10, Margin = new Thickness(0, 0, 10, 3) };
                        notifyChecks.Add(cb);
                        notifyList.Children.Add(cb);
                    }
                    notifyScroll.Content = notifyList;
                    sp.Children.Add(notifyScroll);
                    // ── Location attachment ──
                    var locRow = MakeCtxRow("Location:");
                    var locBox = new System.Windows.Controls.TextBox { Width = 220, FontSize = 10, Background = System.Windows.Media.Brushes.WhiteSmoke, ToolTip = "Auto-populated from selected element tokens; type to override" };
                    locBox.Loaded += (s, e) =>
                    {
                        try
                        {
                            var selDoc = StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document;
                            var selIds = StingCommandHandler.CurrentApp?.ActiveUIDocument?.Selection?.GetElementIds();
                            if (selDoc != null && selIds != null && selIds.Count > 0)
                            {
                                string autoLoc = AutoPopulateIssueLocation(selDoc, selIds);
                                if (!string.IsNullOrEmpty(autoLoc)) locBox.Text = autoLoc;
                            }
                        }
                        catch { }
                    };
                    var locBtn = new Button { Content = "📍 Capture from View", Height = 24, Padding = new Thickness(6, 0, 6, 0), FontSize = 10, Cursor = Cursors.Hand, Margin = new Thickness(4, 0, 0, 0) };
                    locBtn.Click += (s, e) => { DispatchAction("AttachIssueLocation"); locBox.Text = "(Location captured)"; };
                    locRow.Children.Add(locBox); locRow.Children.Add(locBtn);
                    sp.Children.Add(locRow);
                    sp.Children.Add(new TextBlock { Text = "Location auto-populated from selected elements. Edit as needed.", FontSize = 9, Foreground = Br(Color.FromRgb(0x88, 0x88, 0x88)), Margin = new Thickness(0, 0, 0, 3) });
                    // ── Snapshot ──
                    var snapRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
                    var snapStatus = new TextBlock { Text = "No snapshot", FontSize = 10, Foreground = Br(Color.FromRgb(0x90, 0xA4, 0xAE)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                    var snapBtn = new Button { Content = "📸 Capture Snapshot", Height = 26, Padding = new Thickness(8, 0, 8, 0), FontSize = 11, Background = Br(Color.FromRgb(0x45, 0x50, 0x6E)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
                    snapBtn.Click += (s, e) => { DispatchAction("CaptureIssueSnapshot"); snapStatus.Text = "✓ Snapshot attached"; snapStatus.Foreground = Br(CGreen); };
                    snapRow.Children.Add(snapBtn); snapRow.Children.Add(snapStatus);
                    sp.Children.Add(snapRow);
                    // ── Element linking ──
                    var elemRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
                    var elemStatus = new TextBlock { Text = "No elements linked", FontSize = 10, Foreground = Br(Color.FromRgb(0x90, 0xA4, 0xAE)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                    var elemBtn = new Button { Content = "🔗 Link Selected Elements", Height = 26, Padding = new Thickness(8, 0, 8, 0), FontSize = 11, Background = Br(Color.FromRgb(0x1A, 0x23, 0x7E)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
                    elemBtn.Click += (s, e) => { DispatchAction("LinkIssueElements"); elemStatus.Text = "✓ Elements linked"; elemStatus.Foreground = Br(CGreen); };
                    elemRow.Children.Add(elemBtn); elemRow.Children.Add(elemStatus);
                    sp.Children.Add(elemRow);
                    var raiseBtn = MakeCtxActionButton("Raise Issue", Br(CRed));
                    raiseBtn.Click += (s, e) => {
                        StingCommandHandler.SetExtraParam("IssueType", (typeCb.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "NCR");
                        StingCommandHandler.SetExtraParam("IssuePriority", priCb.SelectedItem?.ToString() ?? "HIGH");
                        StingCommandHandler.SetExtraParam("IssueTitle", titleBox.Text);
                        StingCommandHandler.SetExtraParam("Assignees", string.Join(",", assignChecks2.Where(c => c.IsChecked == true).Select(c => c.Content?.ToString() ?? "")));
                        StingCommandHandler.SetExtraParam("NotifyRecipients", string.Join(",", notifyChecks.Where(c => c.IsChecked == true).Select(c => c.Content?.ToString() ?? "")));
                        DispatchAction("RaiseIssue");
                    };
                    sp.Children.Add(raiseBtn);
                    panel.Child = sp;
                    return panel;
                }

                case "UpdateIssue":
                {
                    var sp = new StackPanel();
                    sp.Children.Add(new TextBlock { Text = "Update Issue Status", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = navyBrush, Margin = new Thickness(0, 0, 0, 8) });
                    var statusRow = MakeCtxRow("New Status:");
                    var statusCb = new System.Windows.Controls.ComboBox { Width = 160 };
                    foreach (var s in new[] { "OPEN", "IN PROGRESS", "PENDING INFO", "RESOLVED", "CLOSED", "REJECTED" }) statusCb.Items.Add(s);
                    statusCb.SelectedIndex = 0;
                    statusRow.Children.Add(statusCb);
                    sp.Children.Add(statusRow);
                    var noteRow = MakeCtxRow("Resolution Note:");
                    var noteBox = new System.Windows.Controls.TextBox { Width = 340, Height = 40, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true };
                    noteRow.Children.Add(noteBox);
                    sp.Children.Add(noteRow);
                    var updateBtn = MakeCtxActionButton("Apply Update", Br(CAccent));
                    updateBtn.Click += (s, e) => DispatchAction("UpdateIssue");
                    sp.Children.Add(updateBtn);
                    panel.Child = sp;
                    return panel;
                }

                case "IssuesBulkClose":
                {
                    var sp = new StackPanel();
                    sp.Children.Add(new TextBlock { Text = "Bulk Close Issues", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = navyBrush, Margin = new Thickness(0, 0, 0, 8) });
                    sp.Children.Add(new TextBlock { Text = "Filter issues to close:", FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
                    var filterRow = MakeCtxRow("Status Filter:");
                    var filterCb = new System.Windows.Controls.ComboBox { Width = 140 };
                    foreach (var s in new[] { "All Resolved", "RESOLVED only", "PENDING INFO only" }) filterCb.Items.Add(s);
                    filterCb.SelectedIndex = 0;
                    filterRow.Children.Add(filterCb);
                    sp.Children.Add(filterRow);
                    var noteRow = MakeCtxRow("Closure Note:");
                    var noteBox = new System.Windows.Controls.TextBox { Width = 300, Height = 36, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true };
                    noteRow.Children.Add(noteBox);
                    sp.Children.Add(noteRow);
                    var closeBtn = MakeCtxActionButton("Bulk Close", Br(Color.FromRgb(0x6A, 0x1B, 0x9A)));
                    closeBtn.Click += (s, e) => DispatchAction("IssuesBulkClose");
                    sp.Children.Add(closeBtn);
                    panel.Child = sp;
                    return panel;
                }

                case "BCFExport":
                {
                    var sp = new StackPanel();
                    sp.Children.Add(new TextBlock { Text = "BCF 2.1 Export", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = navyBrush, Margin = new Thickness(0, 0, 0, 8) });
                    var platformRow = MakeCtxRow("Target Platform:");
                    var platCb = new System.Windows.Controls.ComboBox { Width = 220 };
                    // Phase 99: Planscape listed first — BCF 2.1 round-trips through
                    // the native Planscape server (plugin + mobile) as the primary
                    // coordination channel; the external platforms remain available
                    // for exchanges with third parties.
                    foreach (var p in new[] {
                        "Planscape (native)",
                        "Generic BCF 2.1",
                        "Autodesk Construction Cloud (ACC)",
                        "Navisworks",
                        "BIMcollab",
                        "Solibri",
                        "Trimble Connect",
                        "BIM Track",
                        "Revizto",
                        "Bentley iTwin",
                        "Procore"
                    }) platCb.Items.Add(p);
                    platCb.SelectedIndex = 0;
                    platformRow.Children.Add(platCb);
                    sp.Children.Add(platformRow);
                    var inclRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
                    inclRow.Children.Add(new CheckBox { Content = "Open issues only", IsChecked = true, Margin = new Thickness(0, 0, 16, 0) });
                    inclRow.Children.Add(new CheckBox { Content = "Include viewpoints", IsChecked = true });
                    sp.Children.Add(inclRow);
                    var expBtn = MakeCtxActionButton("Export BCF", Br(Color.FromRgb(0x00, 0x69, 0x7C)));
                    expBtn.Click += (s, e) => DispatchAction("BCFExport");
                    sp.Children.Add(expBtn);
                    panel.Child = sp;
                    return panel;
                }

                case "CreateIssuesFromWarnings":
                {
                    var sp = new StackPanel();
                    sp.Children.Add(new TextBlock { Text = "Create Issues from Warnings", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = navyBrush, Margin = new Thickness(0, 0, 0, 8) });
                    var sevRow = MakeCtxRow("Min Severity:");
                    var sevCb = new System.Windows.Controls.ComboBox { Width = 140 };
                    foreach (var s in new[] { "CRITICAL", "HIGH and above", "MEDIUM and above", "All" }) sevCb.Items.Add(s);
                    sevCb.SelectedIndex = 1;
                    sevRow.Children.Add(sevCb);
                    sp.Children.Add(sevRow);
                    var typeRow = MakeCtxRow("Issue Type:");
                    var typeCb = new System.Windows.Controls.ComboBox { Width = 200 };
                    foreach (var it in IsoIssueTypes) typeCb.Items.Add(new ComboBoxItem { Content = $"{it.Code} — {it.Label}", Tag = it.Code, ToolTip = it.Description });
                    typeCb.SelectedIndex = 0;
                    typeRow.Children.Add(typeCb);
                    sp.Children.Add(typeRow);
                    var createBtn = MakeCtxActionButton("Create Issues", Br(Color.FromRgb(0xE6, 0x5C, 0x00)));
                    createBtn.Click += (s, e) => {
                        var selType = typeCb.SelectedItem?.ToString() ?? "NCR";
                        var typeCode = selType.Contains(" — ") ? selType.Substring(0, selType.IndexOf(" — ")) : selType;
                        StingCommandHandler.SetExtraParam("IssueType", typeCode);
                        DispatchAction("CreateIssuesFromWarnings");
                    };
                    sp.Children.Add(createBtn);
                    panel.Child = sp;
                    return panel;
                }

                case "AssignIssues":
                {
                    var sp = new StackPanel();
                    sp.Children.Add(new TextBlock { Text = "Assign Issues to Team Members", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = navyBrush, Margin = new Thickness(0, 0, 0, 8) });
                    // Multi-person checklist
                    sp.Children.Add(new TextBlock { Text = "Select assignees (multiple allowed):", FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
                    var assignScroll = new ScrollViewer { MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 8) };
                    var assignList = new StackPanel();
                    var isoRoles = _data.TeamMembers.Count > 0
                        ? _data.TeamMembers.Select(m => m.Name).ToList()
                        : GetISOStandardMembers();
                    var assignChecks = new List<CheckBox>();
                    foreach (var name in isoRoles)
                    {
                        var cb = new CheckBox { Content = name, FontSize = 11, Margin = new Thickness(0, 2, 0, 2) };
                        assignChecks.Add(cb);
                        assignList.Children.Add(cb);
                    }
                    assignScroll.Content = assignList;
                    sp.Children.Add(assignScroll);
                    var discRow = MakeCtxRow("By Discipline:");
                    var discCb = new System.Windows.Controls.ComboBox { Width = 160 };
                    foreach (var d in new[] { "All", "Architecture (A)", "Structure (S)", "Mechanical (M)", "Electrical (E)", "Plumbing (P)", "Fire Protection (FP)", "Low Voltage (LV)" }) discCb.Items.Add(d);
                    discCb.SelectedIndex = 0;
                    discRow.Children.Add(discCb);
                    sp.Children.Add(discRow);
                    var priRow = MakeCtxRow("Priority Filter:");
                    var priCb = new System.Windows.Controls.ComboBox { Width = 140 };
                    foreach (var p in new[] { "All Priorities", "CRITICAL only", "HIGH and above", "MEDIUM and above" }) priCb.Items.Add(p);
                    priCb.SelectedIndex = 0;
                    priRow.Children.Add(priCb);
                    sp.Children.Add(priRow);
                    var selectedLabel = new TextBlock { FontSize = 10, Foreground = Br(CGreen), Margin = new Thickness(0, 4, 0, 4) };
                    foreach (var cb2 in assignChecks) cb2.Checked += (s2, e2) => { selectedLabel.Text = $"Selected: {string.Join(", ", assignChecks.Where(c => c.IsChecked == true).Select(c => c.Content))}"; };
                    foreach (var cb2 in assignChecks) cb2.Unchecked += (s2, e2) => { selectedLabel.Text = $"Selected: {string.Join(", ", assignChecks.Where(c => c.IsChecked == true).Select(c => c.Content))}"; };
                    sp.Children.Add(selectedLabel);
                    var assignBtn = MakeCtxActionButton("Assign to Selected", Br(CAccent));
                    assignBtn.Click += (s, e) => {
                        var selected = string.Join(",", assignChecks.Where(c => c.IsChecked == true).Select(c => c.Content?.ToString() ?? ""));
                        StingCommandHandler.SetExtraParam("Assignees", selected);
                        DispatchAction("AssignIssues");
                    };
                    sp.Children.Add(assignBtn);
                    panel.Child = sp;
                    return panel;
                }

                default:
                {
                    panel.Child = new TextBlock { Text = $"Select an action button above to see configuration options.", FontSize = 12, Foreground = Brushes.Gray };
                    return panel;
                }
            }
        }

        private static List<string> GetISOStandardMembers()
        {
            return new List<string>
            {
                "Client / Appointing Party", "Project Manager", "BIM Manager",
                "Contract Administrator", "Quantity Surveyor", "Architect",
                "Structural Engineer", "MEP Engineer", "Mechanical Engineer",
                "Electrical Engineer", "Public Health Engineer", "Fire Engineer",
                "Civil Engineer", "Interior Designer", "Landscape Architect",
                "Main Contractor", "Specialist Subcontractor", "Clerk of Works",
                "Facilities Manager", "Operations Manager", "BIM Coordinator",
                "BIM Technician", "General"
            };
        }

        private static StackPanel MakeCtxRow(string label)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new TextBlock { Text = label, Width = 120, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
            return row;
        }

        private static Button MakeCtxActionButton(string label, SolidColorBrush bg)
            => new Button
            {
                Content = label, Height = 30, Padding = new Thickness(18, 0, 18, 0),
                Background = bg, Foreground = Brushes.White, BorderThickness = new Thickness(0),
                FontSize = 12, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0)
            };

        // ════════════════════════════════════════════════════════════════
        //  WORKFLOW PRESET PANEL  (Phase 76 Item 9)
        // ════════════════════════════════════════════════════════════════

        private FrameworkElement BuildWorkflowPanel(string name, string desc, string[] steps)
        {
            var navyBrush = Br(CHeaderBg);
            var panelBorder = new Border
            {
                Background = Br(CCardBg),
                BorderBrush = Br(CBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 4, 0, 8)
            };

            var sp = new StackPanel();
            // Header
            sp.Children.Add(new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = navyBrush, Margin = new Thickness(0, 0, 0, 2) });
            sp.Children.Add(new TextBlock { Text = desc, FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });

            // Discipline filter row
            var filterRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            filterRow.Children.Add(new TextBlock { Text = "Discipline filter:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), FontSize = 11 });
            var discCb = new ComboBox { Width = 180, FontSize = 11 };
            foreach (var d in new[] { "All Disciplines", "Architecture (A)", "Structure (S)", "Mechanical (M)", "Electrical (E)", "Plumbing (P)", "Fire Protection (FP)", "Civil (C)" })
                discCb.Items.Add(d);
            discCb.SelectedIndex = 0;
            filterRow.Children.Add(discCb);
            sp.Children.Add(filterRow);

            // Step checkboxes (two-column grid)
            sp.Children.Add(new TextBlock { Text = "Steps to execute:", FontWeight = FontWeights.SemiBold, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
            var stepGrid = new UniformGrid { Columns = 2 };
            var stepCheckBoxes = new List<CheckBox>();
            foreach (var step in steps)
            {
                var cb = new CheckBox
                {
                    Content = step, IsChecked = true,
                    Margin = new Thickness(0, 2, 16, 2), FontSize = 11
                };
                stepCheckBoxes.Add(cb);
                stepGrid.Children.Add(cb);
            }
            sp.Children.Add(stepGrid);

            // Select All / Deselect All links
            var selRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 8) };
            var selAllBtn = new Button { Content = "Select All", Height = 22, Padding = new Thickness(8, 0, 8, 0), FontSize = 10, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 0) };
            selAllBtn.Click += (s, e) => { foreach (var cb in stepCheckBoxes) cb.IsChecked = true; };
            var deselBtn = new Button { Content = "Deselect All", Height = 22, Padding = new Thickness(8, 0, 8, 0), FontSize = 10, Cursor = Cursors.Hand };
            deselBtn.Click += (s, e) => { foreach (var cb in stepCheckBoxes) cb.IsChecked = false; };
            selRow.Children.Add(selAllBtn);
            selRow.Children.Add(deselBtn);
            sp.Children.Add(selRow);

            // Run / Schedule buttons
            var actionTag = $"RunWorkflow_{name.Replace(" ", "")}";
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var runBtn = new Button
            {
                Content = $"\u25B6  Run {name}", Height = 34, Padding = new Thickness(18, 0, 18, 0),
                Background = navyBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0),
                FontSize = 12, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0)
            };
            runBtn.Click += (s, e) => DispatchAction(actionTag);
            btnRow.Children.Add(runBtn);

            var scheduleTag = $"ScheduleWorkflow_{name.Replace(" ", "")}";
            var schedBtn = new Button
            {
                Content = "\uD83D\uDD52  Schedule", Height = 34, Padding = new Thickness(14, 0, 14, 0),
                Background = Br(CAccent), Foreground = Brushes.White, BorderThickness = new Thickness(0),
                FontSize = 12, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 8, 0)
            };
            schedBtn.Click += (s, e) => DispatchAction(scheduleTag);
            btnRow.Children.Add(schedBtn);

            var stepsLabel = new TextBlock
            {
                Text = $"{steps.Length} steps  |  {name}",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 10, Foreground = Br(CAccent), Margin = new Thickness(8, 0, 0, 0)
            };
            btnRow.Children.Add(stepsLabel);

            sp.Children.Add(btnRow);
            panelBorder.Child = sp;
            return panelBorder;
        }

        // ════════════════════════════════════════════════════════════════
        //  REVISIONS INLINE PANELS  (Phase 76 Item 8)
        // ════════════════════════════════════════════════════════════════

        private Button MakeRevPanelButton(string label, string tag, SolidColorBrush bg, string tooltip = null)
        {
            var btn = new Button
            {
                Content = label, Tag = tag,
                Height = 30, Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 6, 6),
                Background = bg, Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 11, Cursor = Cursors.Hand,
                ToolTip = tooltip
            };
            var origBg = bg;
            var c0 = origBg.Color;
            var hoverBrush = Br(Color.FromRgb(
                (byte)Math.Min(255, c0.R + 30),
                (byte)Math.Min(255, c0.G + 30),
                (byte)Math.Min(255, c0.B + 30)));
            btn.MouseEnter += (s, e) => { btn.Background = hoverBrush; };
            btn.MouseLeave += (s, e) => { btn.Background = origBg; };
            btn.Click += (s, e) => { ShowRevPanel(tag); };
            return btn;
        }

        private void ShowRevPanel(string tag)
        {
            if (_revPanelArea != null)
                _revPanelArea.Content = BuildRevPanelFor(tag);
        }

        private FrameworkElement BuildRevPanelFor(string tag)
        {
            var navyBrush = Br(CHeaderBg);
            var panelBorder = new Border
            {
                Background = Br(CCardBg),
                BorderBrush = Br(CBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 8, 0, 8)
            };

            switch (tag)
            {
                case "CreateRevision":
                {
                    var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
                    sp.Children.Add(new TextBlock { Text = "Create Revision", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10), Foreground = navyBrush });

                    var descRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    descRow.Children.Add(new TextBlock { Text = "Description:", Width = 120, VerticalAlignment = VerticalAlignment.Center });
                    var descBox = new TextBox { Width = 260, Margin = new Thickness(4, 0, 0, 0) };
                    descRow.Children.Add(descBox);
                    sp.Children.Add(descRow);

                    var dateRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    dateRow.Children.Add(new TextBlock { Text = "Date:", Width = 120, VerticalAlignment = VerticalAlignment.Center });
                    var datePicker = new DatePicker { Width = 160, Margin = new Thickness(4, 0, 0, 0), SelectedDate = DateTime.Today };
                    dateRow.Children.Add(datePicker);
                    sp.Children.Add(dateRow);

                    var revNumRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    revNumRow.Children.Add(new TextBlock { Text = "Revision #:", Width = 120, VerticalAlignment = VerticalAlignment.Center });
                    var revNumBox = new TextBox { Width = 80, Margin = new Thickness(4, 0, 0, 0), Text = "P01" };
                    revNumRow.Children.Add(revNumBox);
                    sp.Children.Add(revNumRow);

                    var authorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    authorRow.Children.Add(new TextBlock { Text = "Author:", Width = 120, VerticalAlignment = VerticalAlignment.Center });
                    var authorBox = new TextBox { Width = 200, Margin = new Thickness(4, 0, 0, 0) };
                    authorRow.Children.Add(authorBox);
                    sp.Children.Add(authorRow);

                    var createBtn = new Button
                    {
                        Content = "Create", Height = 32, Padding = new Thickness(20, 0, 20, 0),
                        Background = Br(CGreen), Foreground = Brushes.White, BorderThickness = new Thickness(0),
                        FontSize = 12, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand,
                        HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0)
                    };
                    createBtn.Click += (s, e) => DispatchAction("CreateRevision");
                    sp.Children.Add(createBtn);

                    panelBorder.Child = sp;
                    return panelBorder;
                }

                case "RevisionCompare":
                {
                    var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
                    sp.Children.Add(new TextBlock { Text = "Compare Revisions", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10), Foreground = navyBrush });

                    var revARow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    revARow.Children.Add(new TextBlock { Text = "Revision A (Base):", Width = 130, VerticalAlignment = VerticalAlignment.Center });
                    var revACb = new ComboBox { Width = 200, Margin = new Thickness(4, 0, 0, 0) };
                    foreach (var r in _data.Revisions) revACb.Items.Add(r.Number ?? r.Description ?? "Rev");
                    if (revACb.Items.Count == 0) { revACb.Items.Add("P01"); revACb.Items.Add("P02"); }
                    revACb.SelectedIndex = 0;
                    revARow.Children.Add(revACb);
                    sp.Children.Add(revARow);

                    var revBRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    revBRow.Children.Add(new TextBlock { Text = "Revision B (Compare):", Width = 130, VerticalAlignment = VerticalAlignment.Center });
                    var revBCb = new ComboBox { Width = 200, Margin = new Thickness(4, 0, 0, 0) };
                    foreach (var r in _data.Revisions) revBCb.Items.Add(r.Number ?? r.Description ?? "Rev");
                    if (revBCb.Items.Count == 0) { revBCb.Items.Add("P01"); revBCb.Items.Add("P02"); }
                    revBCb.SelectedIndex = Math.Min(1, revBCb.Items.Count - 1);
                    revBRow.Children.Add(revBCb);
                    sp.Children.Add(revBRow);

                    var diffBtn = new Button
                    {
                        Content = "Show Diff", Height = 32, Padding = new Thickness(20, 0, 20, 0),
                        Background = navyBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0),
                        FontSize = 12, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand,
                        HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 8)
                    };
                    diffBtn.Click += (s, e) => DispatchAction("RevisionCompare");
                    sp.Children.Add(diffBtn);

                    sp.Children.Add(new TextBlock { Text = "Diff Results:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 4) });
                    var dg = new DataGrid
                    {
                        AutoGenerateColumns = false, IsReadOnly = true,
                        Height = 140, Margin = new Thickness(0, 0, 0, 4),
                        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
                    };
                    dg.Columns.Add(new DataGridTextColumn { Header = "Element", Binding = new System.Windows.Data.Binding("Element"), Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
                    dg.Columns.Add(new DataGridTextColumn { Header = "Changed Param", Binding = new System.Windows.Data.Binding("ChangedParam"), Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
                    dg.Columns.Add(new DataGridTextColumn { Header = "Old", Binding = new System.Windows.Data.Binding("OldValue"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    dg.Columns.Add(new DataGridTextColumn { Header = "New", Binding = new System.Windows.Data.Binding("NewValue"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    dg.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<RevDiffRow>();
                    sp.Children.Add(dg);

                    panelBorder.Child = sp;
                    return panelBorder;
                }

                case "IssueSheetsForRevision":
                {
                    var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
                    sp.Children.Add(new TextBlock { Text = "Issue Sheets", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10), Foreground = navyBrush });

                    sp.Children.Add(new TextBlock { Text = "Select sheets to issue:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
                    var sheetDg = new DataGrid
                    {
                        AutoGenerateColumns = false, CanUserAddRows = false,
                        Height = 130, Margin = new Thickness(0, 0, 0, 8),
                        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
                    };
                    var sheetCheckCol = new DataGridTemplateColumn { Header = "Include", Width = 60 };
                    var chkFactory = new FrameworkElementFactory(typeof(CheckBox));
                    chkFactory.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("Include") { Mode = System.Windows.Data.BindingMode.TwoWay });
                    sheetCheckCol.CellTemplate = new DataTemplate { VisualTree = chkFactory };
                    sheetDg.Columns.Add(sheetCheckCol);
                    sheetDg.Columns.Add(new DataGridTextColumn { Header = "Sheet Number", Binding = new System.Windows.Data.Binding("SheetNumber"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    sheetDg.Columns.Add(new DataGridTextColumn { Header = "Sheet Name", Binding = new System.Windows.Data.Binding("SheetName"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
                    sheetDg.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<SheetIssueRow>
                    {
                        new SheetIssueRow { Include = true, SheetNumber = "A-001", SheetName = "Site Plan" },
                        new SheetIssueRow { Include = true, SheetNumber = "A-100", SheetName = "Ground Floor Plan" },
                        new SheetIssueRow { Include = false, SheetNumber = "S-001", SheetName = "Foundation Plan" }
                    };
                    sp.Children.Add(sheetDg);

                    var dateRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    dateRow.Children.Add(new TextBlock { Text = "Issue Date:", Width = 110, VerticalAlignment = VerticalAlignment.Center });
                    dateRow.Children.Add(new DatePicker { Width = 160, Margin = new Thickness(4, 0, 0, 0), SelectedDate = DateTime.Today });
                    sp.Children.Add(dateRow);

                    var suitRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
                    suitRow.Children.Add(new TextBlock { Text = "Suitability:", Width = 110, VerticalAlignment = VerticalAlignment.Center });
                    var suitCb = new ComboBox { Width = 200, Margin = new Thickness(4, 0, 0, 0) };
                    foreach (var s in new[] { "S0 - Work in Progress", "S1 - Suitable for Coordination", "S2 - Suitable for Information", "S3 - Suitable for Review", "S4 - Suitable for Construction", "S5 - Suitable for Manufacture/Install", "A1 - Approved for Construction" })
                        suitCb.Items.Add(s);
                    suitCb.SelectedIndex = 1;
                    suitRow.Children.Add(suitCb);
                    sp.Children.Add(suitRow);

                    var issueBtn = new Button
                    {
                        Content = "Issue Sheets", Height = 32, Padding = new Thickness(20, 0, 20, 0),
                        Background = Br(CAmber), Foreground = Brushes.White, BorderThickness = new Thickness(0),
                        FontSize = 12, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    issueBtn.Click += (s, e) => DispatchAction("IssueSheetsForRevision");
                    sp.Children.Add(issueBtn);

                    panelBorder.Child = sp;
                    return panelBorder;
                }

                case "BulkRevisionStamp":
                {
                    var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
                    sp.Children.Add(new TextBlock { Text = "Bulk Revision Stamp", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10), Foreground = navyBrush });

                    var revRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
                    revRow.Children.Add(new TextBlock { Text = "Revision:", Width = 110, VerticalAlignment = VerticalAlignment.Center });
                    var revCb = new ComboBox { Width = 200, Margin = new Thickness(4, 0, 0, 0) };
                    foreach (var r in _data.Revisions) revCb.Items.Add(r.Number ?? r.Description ?? "Rev");
                    if (revCb.Items.Count == 0) { revCb.Items.Add("P01"); revCb.Items.Add("P02"); }
                    revCb.SelectedIndex = 0;
                    revRow.Children.Add(revCb);
                    sp.Children.Add(revRow);

                    sp.Children.Add(new TextBlock { Text = "Stamp Position:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 4) });
                    var posStack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                    var rb1 = new RadioButton { Content = "Title Block (Bottom Right)", IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };
                    var rb2 = new RadioButton { Content = "Title Block (Bottom Left)", Margin = new Thickness(0, 2, 0, 2) };
                    var rb3 = new RadioButton { Content = "Custom Position", Margin = new Thickness(0, 2, 0, 2) };
                    posStack.Children.Add(rb1);
                    posStack.Children.Add(rb2);
                    posStack.Children.Add(rb3);
                    sp.Children.Add(posStack);

                    var applyBtn = new Button
                    {
                        Content = "Apply Stamp", Height = 32, Padding = new Thickness(20, 0, 20, 0),
                        Background = Br(CRed), Foreground = Brushes.White, BorderThickness = new Thickness(0),
                        FontSize = 12, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    applyBtn.Click += (s, e) => DispatchAction("BulkRevisionStamp");
                    sp.Children.Add(applyBtn);

                    panelBorder.Child = sp;
                    return panelBorder;
                }

                case "RevisionSchedule":
                {
                    var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
                    sp.Children.Add(new TextBlock { Text = "Revision Schedule", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10), Foreground = navyBrush });

                    sp.Children.Add(new TextBlock { Text = "Revision Register (editable):", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
                    var dg = new DataGrid
                    {
                        AutoGenerateColumns = false, CanUserAddRows = true,
                        Height = 150, Margin = new Thickness(0, 0, 0, 10),
                        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
                    };
                    dg.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new System.Windows.Data.Binding("Number"), Width = 50 });
                    dg.Columns.Add(new DataGridTextColumn { Header = "Date", Binding = new System.Windows.Data.Binding("Date"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    dg.Columns.Add(new DataGridTextColumn { Header = "Description", Binding = new System.Windows.Data.Binding("Description"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
                    dg.Columns.Add(new DataGridTextColumn { Header = "Author", Binding = new System.Windows.Data.Binding("Author"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    dg.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new System.Windows.Data.Binding("Status"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    dg.ItemsSource = _data.Revisions.Count > 0
                        ? (System.Collections.IEnumerable)_data.Revisions
                        : new System.Collections.ObjectModel.ObservableCollection<RevisionRow>();
                    sp.Children.Add(dg);

                    var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
                    var createRevBtn = new Button
                    {
                        Content = "Create Schedule in Revit", Height = 30, Padding = new Thickness(14, 0, 14, 0),
                        Background = Br(Color.FromRgb(0x15, 0x65, 0xC0)), Foreground = Brushes.White,
                        BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand,
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    createRevBtn.Click += (s, e) => DispatchAction("RevisionSchedule");
                    btnRow.Children.Add(createRevBtn);

                    var exportXlsxBtn = new Button
                    {
                        Content = "Export XLSX", Height = 30, Padding = new Thickness(14, 0, 14, 0),
                        Background = Br(CGreen), Foreground = Brushes.White,
                        BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand
                    };
                    exportXlsxBtn.Click += (s, e) => DispatchAction("RevisionExportXlsx");
                    btnRow.Children.Add(exportXlsxBtn);
                    sp.Children.Add(btnRow);

                    panelBorder.Child = sp;
                    return panelBorder;
                }

                case "TagRevisionDiff":
                {
                    var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
                    sp.Children.Add(new TextBlock { Text = "Tag Revision Diff", FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10), Foreground = navyBrush });

                    var fromRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    fromRow.Children.Add(new TextBlock { Text = "From Revision:", Width = 120, VerticalAlignment = VerticalAlignment.Center });
                    var fromCb = new ComboBox { Width = 180, Margin = new Thickness(4, 0, 0, 0) };
                    foreach (var r in _data.Revisions) fromCb.Items.Add(r.Number ?? r.Description ?? "Rev");
                    if (fromCb.Items.Count == 0) { fromCb.Items.Add("P01"); fromCb.Items.Add("P02"); }
                    fromCb.SelectedIndex = 0;
                    fromRow.Children.Add(fromCb);
                    sp.Children.Add(fromRow);

                    var toRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
                    toRow.Children.Add(new TextBlock { Text = "To Revision:", Width = 120, VerticalAlignment = VerticalAlignment.Center });
                    var toCb = new ComboBox { Width = 180, Margin = new Thickness(4, 0, 0, 0) };
                    foreach (var r in _data.Revisions) toCb.Items.Add(r.Number ?? r.Description ?? "Rev");
                    if (toCb.Items.Count == 0) { toCb.Items.Add("P01"); toCb.Items.Add("P02"); }
                    toCb.SelectedIndex = Math.Min(1, toCb.Items.Count - 1);
                    toRow.Children.Add(toCb);
                    sp.Children.Add(toRow);

                    sp.Children.Add(new TextBlock { Text = "Highlight Mode:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 4) });
                    var modeStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
                    var rbAdded   = new RadioButton { Content = "Added elements",   IsChecked = true, Margin = new Thickness(0, 0, 16, 0) };
                    var rbChanged = new RadioButton { Content = "Changed tokens",   Margin = new Thickness(0, 0, 16, 0) };
                    var rbRemoved = new RadioButton { Content = "Removed elements", Margin = new Thickness(0, 0, 0, 0) };
                    modeStack.Children.Add(rbAdded);
                    modeStack.Children.Add(rbChanged);
                    modeStack.Children.Add(rbRemoved);
                    sp.Children.Add(modeStack);

                    var runBtn = new Button
                    {
                        Content = "Run Diff", Height = 32, Padding = new Thickness(20, 0, 20, 0),
                        Background = Br(Color.FromRgb(0x45, 0x50, 0x6E)), Foreground = Brushes.White,
                        BorderThickness = new Thickness(0), FontSize = 12, FontWeight = FontWeights.SemiBold,
                        Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left
                    };
                    runBtn.Click += (s, e) => DispatchAction("TagRevisionDiff");
                    sp.Children.Add(runBtn);

                    panelBorder.Child = sp;
                    return panelBorder;
                }

                default:
                {
                    panelBorder.Child = new TextBlock
                    {
                        Text = $"Select an action above to configure options.",
                        FontSize = 12, Foreground = Brushes.Gray, Margin = new Thickness(0, 8, 0, 8)
                    };
                    return panelBorder;
                }
            }
        }

        // ── Helper DTOs for revision diff and sheet issue DataGrids ──
        private class RevDiffRow
        {
            public string Element      { get; set; }
            public string ChangedParam { get; set; }
            public string OldValue     { get; set; }
            public string NewValue     { get; set; }
        }
        private class SheetIssueRow
        {
            public bool   Include     { get; set; }
            public string SheetNumber { get; set; }
            public string SheetName   { get; set; }
        }

        // ════════════════════════════════════════════════════════════════
        //  HELPER METHODS — Cards, KPIs, RAG bars, buttons, etc.
        // ════════════════════════════════════════════════════════════════

        // Phase 78 Section 5.1: Added onClickDetail Action — fires inline drill-down instead of (or alongside) dispatch.
        private Border MakeKPICard(string label, string value, SolidColorBrush valueBrush,
            string tooltip = null, string clickAction = null, Action onClickDetail = null)
        {
            bool isClickable = !string.IsNullOrEmpty(clickAction) || onClickDetail != null;
            var card = new Border
            {
                Background = Br(CCardBg), BorderBrush = Br(CBorder), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(4), Cursor = isClickable ? Cursors.Hand : Cursors.Arrow
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

            if (isClickable)
            {
                card.MouseLeftButtonDown += (s, e) =>
                {
                    if (onClickDetail != null)
                        onClickDetail.Invoke();
                    else if (!string.IsNullOrEmpty(clickAction) && e.ClickCount == 2)
                        DispatchAction(clickAction);
                };
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

        // Phase 78 Section 2.2: Derives brush from IsoIssueTypes — single source of truth.
        private static SolidColorBrush GetIssueTypeBrush(string type)
        {
            var match = IsoIssueTypes.FirstOrDefault(t =>
                t.Code.Equals(type, StringComparison.OrdinalIgnoreCase));
            return match.Code != null ? Br(match.Colour) : Br(Color.FromRgb(0x78, 0x90, 0x9C));
        }

        // Phase 78 Section 10.3: Null-safe panel content setter with dispatcher
        private void SetPanelContent(ContentControl area, UIElement content)
        {
            if (area == null) return;
            Dispatcher.BeginInvoke(new Action(() => area.Content = content),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        // Phase 78 Section 1.2: Show inline status message in a ContentControl area
        private void ShowInlineStatus(ContentControl area, string title, string message,
            bool isError = false)
        {
            if (area == null) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var color = isError ? CRed : CGreen;
                var panel = new Border
                {
                    Background = Br(isError ? Color.FromRgb(0xFF, 0xEB, 0xEE) : Color.FromRgb(0xE8, 0xF5, 0xE9)),
                    BorderBrush = Br(color), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 6, 0, 6)
                };
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold,
                    FontSize = 12, Foreground = Br(color), Margin = new Thickness(0, 0, 0, 4) });
                sp.Children.Add(new TextBlock { Text = message, FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Br(Color.FromRgb(0x33, 0x33, 0x33)) });
                var dismissBtn = new Button { Content = "✕ Dismiss", Height = 24,
                    Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 8, 0, 0),
                    Background = Br(color), Foreground = Brushes.White,
                    BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Left };
                dismissBtn.Click += (s, e) => { area.Content = null; };
                sp.Children.Add(dismissBtn);
                panel.Child = sp;
                area.Content = panel;
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // Phase 78 Section 1.3: Push 4D/5D command result text to the inline _4dPanelArea
        internal void Show4DInlineResult(string action, string content)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_4dPanelArea == null) return;
                var sp = new StackPanel { Margin = new Thickness(8) };
                sp.Children.Add(new TextBlock
                {
                    Text = action.Replace("4D", "").Replace("5D", "").Trim(),
                    FontWeight = FontWeights.Bold, FontSize = 13, Foreground = Br(CHeaderBg),
                    Margin = new Thickness(0, 0, 0, 6)
                });
                foreach (var line in content.Split('\n'))
                    sp.Children.Add(new TextBlock { Text = line, FontSize = 11,
                        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) });
                var closeBtn = new Button { Content = "Close Panel", Height = 26,
                    Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 10, 0, 0),
                    Background = Br(CHeaderBg), Foreground = Brushes.White,
                    BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Left };
                closeBtn.Click += (s, e) => _4dPanelArea.Content = null;
                sp.Children.Add(closeBtn);
                _4dPanelArea.Content = new Border
                {
                    Background = Br(Color.FromRgb(0xF4, 0xF5, 0xF7)),
                    BorderBrush = Br(CBorder), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4), Child = sp
                };
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // Phase 78 Section 10.6: Proper resource cleanup on window close
        private void OnClosed(object sender, EventArgs e)
        {
            ActionDispatcher = null;
            CurrentInstance = null;
            _tabCache.Clear();
            StingLog.Info("BIMCoordinationCenter closed and resources released.");
        }

        // Phase 78 Section 3.1: Scrollable, searchable assignee checkbox grid
        // Used in RaiseIssue, AssignIssues, UpdateIssue context panels and Planscape Sharing tab.
        private UIElement BuildAssigneeCheckboxGrid(out Func<List<string>> getSelected)
        {
            var outer = new StackPanel();
            outer.Children.Add(new TextBlock
            {
                Text = "Assign To (select all that apply):",
                FontWeight = FontWeights.SemiBold, FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var searchBox = new TextBox
            {
                Height = 24, FontSize = 11, Margin = new Thickness(0, 0, 0, 6),
                BorderBrush = Br(CBorder), Padding = new Thickness(4, 2, 4, 2)
            };
            outer.Children.Add(searchBox);

            var scroll = new ScrollViewer
            {
                MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 6)
            };
            var listPanel = new WrapPanel { Orientation = Orientation.Horizontal };

            // Use team registry if available, else fall back to built-in defaults
            var registry = _data?.TeamMembers?.Count > 0
                ? _data.TeamMembers.Select(m => (m.Name, m.Role ?? "Team")).ToList()
                : DefaultIssueAssignees.Select(d => (d.Role, d.Group)).ToList();

            var checkBoxes = new List<(CheckBox cb, string name, string group)>();
            var grouped = registry.GroupBy(r => r.Item2).OrderBy(g => g.Key);

            foreach (var grp in grouped)
            {
                listPanel.Children.Add(new Border
                {
                    Width = 460,
                    Child = new TextBlock
                    {
                        Text = grp.Key.ToUpperInvariant(),
                        FontWeight = FontWeights.Bold, FontSize = 9,
                        Foreground = Br(CAccent),
                        Margin = new Thickness(2, 6, 2, 2)
                    },
                    Background = Brushes.Transparent
                });

                foreach (var member in grp)
                {
                    var cb = new CheckBox
                    {
                        Content = member.Item1,
                        FontSize = 11,
                        Margin = new Thickness(2, 2, 10, 2),
                        MinWidth = 140
                    };
                    checkBoxes.Add((cb, member.Item1, grp.Key));
                    listPanel.Children.Add(cb);
                }
            }

            scroll.Content = listPanel;
            outer.Children.Add(scroll);

            var selRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            var selAllBtn = new Button
            {
                Content = "Select All", Height = 22, Padding = new Thickness(8, 0, 8, 0),
                FontSize = 10, Background = Br(CHeaderBg), Foreground = Brushes.White,
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 4, 0)
            };
            var clearBtn = new Button
            {
                Content = "Clear", Height = 22, Padding = new Thickness(8, 0, 8, 0),
                FontSize = 10, Background = Br(Color.FromRgb(0x45, 0x50, 0x6E)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            selAllBtn.Click += (s, ev) => checkBoxes.ForEach(x => x.cb.IsChecked = true);
            clearBtn.Click  += (s, ev) => checkBoxes.ForEach(x => x.cb.IsChecked = false);
            selRow.Children.Add(selAllBtn);
            selRow.Children.Add(clearBtn);
            outer.Children.Add(selRow);

            // Live search filter
            searchBox.TextChanged += (s, ev) =>
            {
                string q = searchBox.Text.ToLower();
                foreach (var (cb, name, grp2) in checkBoxes)
                    cb.Visibility = string.IsNullOrEmpty(q) || name.ToLower().Contains(q)
                        ? Visibility.Visible : Visibility.Collapsed;
            };

            getSelected = () => checkBoxes
                .Where(x => x.cb.IsChecked == true)
                .Select(x => x.name)
                .ToList();
            return outer;
        }

        // ── Phase 77: Excel-grade DataGrid helper ──────────────────────
        private static DataGrid MakeExcelDataGrid(int height = 160)
        {
            var dg = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = true,
                CanUserDeleteRows = true,
                CanUserResizeColumns = true,
                CanUserSortColumns = true,
                SelectionMode = DataGridSelectionMode.Extended,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                FontSize = 11,
                RowHeaderWidth = 0,
                MaxHeight = height,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var ctx = new ContextMenu();
            var copyItem = new MenuItem { Header = "Copy Row" };
            copyItem.Click += (s, e) =>
            {
                if (dg.SelectedItem != null)
                    Clipboard.SetText(dg.SelectedItem.ToString());
            };
            var deleteItem = new MenuItem { Header = "Delete Row" };
            deleteItem.Click += (s, e) =>
            {
                var items = dg.ItemsSource as System.Collections.IList;
                if (items != null && dg.SelectedItem != null)
                    items.Remove(dg.SelectedItem);
            };
            var insertItem = new MenuItem { Header = "Insert Row Above" };
            insertItem.Click += (s, e) =>
            {
                // Handled by CanUserAddRows — user adds at bottom; insert above is advisory
            };
            ctx.Items.Add(copyItem);
            ctx.Items.Add(deleteItem);
            ctx.Items.Add(insertItem);
            dg.ContextMenu = ctx;
            return dg;
        }

        /// <summary>Phase 77: Export DataGrid rows to XLSX using StingExcelExporter.</summary>
        private void ExportDataGridToXlsx(DataGrid dg, string title)
        {
            try
            {
                string outDir = System.IO.Path.GetDirectoryName(_data?.FilePath ?? "");
                if (string.IsNullOrEmpty(outDir)) outDir = System.IO.Path.GetTempPath();
                string path = System.IO.Path.Combine(outDir,
                    $"{title}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");

                // Build headers from column headers
                var headers = new List<string>();
                foreach (DataGridColumn col in dg.Columns)
                {
                    headers.Add(col.Header?.ToString() ?? "");
                }

                // Build rows from ItemsSource
                var rows = new List<List<string>>();
                var items = dg.ItemsSource as System.Collections.IEnumerable;
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        var rowData = new List<string>();
                        foreach (DataGridColumn col in dg.Columns)
                        {
                            try
                            {
                                if (col is DataGridTextColumn tc && tc.Binding is System.Windows.Data.Binding b)
                                {
                                    var prop = item?.GetType().GetProperty(b.Path.Path);
                                    rowData.Add(prop?.GetValue(item)?.ToString() ?? "");
                                }
                                else { rowData.Add(""); }
                            }
                            catch { rowData.Add(""); }
                        }
                        rows.Add(rowData);
                    }
                }

                Core.StingExcelExporter.ExportTable(path, title, headers, rows, openFolder: true);
            }
            catch (Exception ex) { StingLog.Warn($"ExportDataGridToXlsx({title}): {ex.Message}"); }
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

            // ── Phase 91 H5: DATA DROP MILESTONES from project_bep.json ─────
            try
            {
                var doc91 = StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document;
                if (doc91 != null)
                {
                    string bepPath = BIMManager.BIMManagerEngine.GetBIMManagerFilePath(doc91, "project_bep.json");
                    if (System.IO.File.Exists(bepPath))
                    {
                        root.Children.Add(MakeSectionHeader("DATA DROP MILESTONES"));
                        var bepObj = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(bepPath));
                        var ddPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
                        foreach (string ddCode in new[] { "DD1", "DD2", "DD3", "DD4" })
                        {
                            var ddNode = bepObj["data_drops"]?[ddCode] ?? bepObj[ddCode];
                            if (ddNode == null) continue;
                            string ddDate = ddNode["date"]?.ToString() ?? ddNode["target_date"]?.ToString() ?? "";
                            string ddStatus = (ddNode["status"]?.ToString() ?? "PENDING").ToUpperInvariant();
                            bool isOverdue = false;
                            int daysRem = 0;
                            if (DateTime.TryParse(ddDate, out DateTime ddDt))
                            {
                                daysRem = (int)(ddDt - DateTime.Now).TotalDays;
                                isOverdue = daysRem < 0 && ddStatus != "COMPLETE";
                            }
                            Color dotClr = ddStatus == "COMPLETE" ? CGreen : isOverdue ? CRed : daysRem < 14 ? CAmber : Color.FromRgb(0x15, 0x65, 0xC0);
                            var ddCard = new Border { Background = Br(CCardBg), BorderBrush = Br(dotClr), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 8, 6), Padding = new Thickness(10, 7, 10, 7) };
                            var ddSt = new StackPanel { Orientation = Orientation.Horizontal };
                            ddSt.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = Br(dotClr), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                            ddSt.Children.Add(new TextBlock { Text = $"{ddCode}  ", FontWeight = FontWeights.Bold, FontSize = 12 });
                            ddSt.Children.Add(new TextBlock { Text = ddDate.Length > 10 ? ddDate.Substring(0, 10) : ddDate, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                            ddSt.Children.Add(new TextBlock { Text = ddStatus, FontSize = 10, Foreground = Br(dotClr), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
                            if (!string.IsNullOrEmpty(ddDate))
                                ddSt.Children.Add(new TextBlock { Text = isOverdue ? $"  ⚠ {-daysRem}d overdue" : $"  ({daysRem}d)", FontSize = 9, Foreground = Br(isOverdue ? CRed : Color.FromRgb(0x75, 0x75, 0x75)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
                            ddCard.Child = ddSt;
                            ddPanel.Children.Add(ddCard);
                        }
                        root.Children.Add(ddPanel);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"DD milestones: {ex.Message}"); }

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

            // ── ISO Discipline legend strip ─────────────────────────────
            var discLegendWrap = new WrapPanel { Margin = new Thickness(0,0,0,8) };
            foreach (var dc in IsoDisciplineCodes.Where(d => d.Code != "ALL"))
            {
                discLegendWrap.Children.Add(new Border
                {
                    Background = Br(dc.Colour), CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6,2,6,2), Margin = new Thickness(0,0,4,3),
                    ToolTip = dc.Label,
                    Child = new TextBlock { Text = dc.Code, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.White }
                });
            }
            root.Children.Add(discLegendWrap);

            if (_data.Deliverables.Count > 0)
            {
                // Filter bar
                var filterBar = new WrapPanel { Margin = new Thickness(0,4,0,8) };
                var filterDrop = new ComboBox { Width = 80, Height = 26, FontSize = 10, ToolTip = "Filter by data drop" };
                filterDrop.Items.Add("All Drops");
                foreach (string dd in ddNames) filterDrop.Items.Add(dd);
                filterDrop.SelectedIndex = 0;
                filterBar.Children.Add(new TextBlock { Text = "Drop: ", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,4,0) });
                filterBar.Children.Add(filterDrop);
                var filterStatus = new ComboBox { Width = 100, Height = 26, FontSize = 10, Margin = new Thickness(12,0,0,0), ToolTip = "Filter by status" };
                filterStatus.Items.Add("All Status");
                foreach (string st in new[] { "Pending", "In Progress", "Submitted", "Approved", "Rejected" }) filterStatus.Items.Add(st);
                filterStatus.SelectedIndex = 0;
                filterBar.Children.Add(new TextBlock { Text = "  Status: ", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8,0,4,0) });
                filterBar.Children.Add(filterStatus);
                var filterCDE = new ComboBox { Width = 90, Height = 26, FontSize = 10, Margin = new Thickness(12,0,0,0), ToolTip = "Filter by CDE state" };
                filterCDE.Items.Add("All CDE");
                foreach (string c in cdeStates) filterCDE.Items.Add(c);
                filterCDE.SelectedIndex = 0;
                filterBar.Children.Add(new TextBlock { Text = "  CDE: ", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8,0,4,0) });
                filterBar.Children.Add(filterCDE);
                // ── Discipline filter ──────────────────────────────────────
                var filterDisc = new ComboBox { Width = 120, Height = 26, FontSize = 10, Margin = new Thickness(12,0,0,0), ToolTip = "Filter by ISO discipline" };
                filterDisc.Items.Add("All Disciplines");
                foreach (var dc in IsoDisciplineCodes.Where(d => d.Code != "ALL")) filterDisc.Items.Add(dc.Code + " \u2014 " + dc.Label.Split('\u2014').Last().Trim());
                filterDisc.SelectedIndex = 0;
                filterBar.Children.Add(new TextBlock { Text = "  Disc.: ", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8,0,4,0) });
                filterBar.Children.Add(filterDisc);
                root.Children.Add(filterBar);

                var dg = new DataGrid
                {
                    AutoGenerateColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column,
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, CanUserSortColumns = true,
                    SelectionMode = DataGridSelectionMode.Single, FontSize = 11, MaxHeight = 300,
                    BorderBrush = Br(CBorder), BorderThickness = new Thickness(1), RowHeaderWidth = 0
                };
                dg.Columns.Add(new DataGridTextColumn { Header = "Code",       Binding = new Binding("Code"),       Width = 120 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Name",       Binding = new Binding("Name"),       Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                dg.Columns.Add(new DataGridTextColumn { Header = "Disc.",      Binding = new Binding("Discipline"), Width = 45 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Type",       Binding = new Binding("Type"),       Width = 60 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Drop",       Binding = new Binding("DataDrop"),   Width = 40 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Status",     Binding = new Binding("Status"),     Width = 80 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Suit.",      Binding = new Binding("Suitability"),Width = 35 });
                dg.Columns.Add(new DataGridTextColumn { Header = "CDE",        Binding = new Binding("CDE"),        Width = 60 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Owner",      Binding = new Binding("Owner"),      Width = 80 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Due",        Binding = new Binding("DueDate"),    Width = 80 });
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
                    string dropVal   = filterDrop.SelectedItem   as string;
                    string statusVal = filterStatus.SelectedItem as string;
                    string cdeVal    = filterCDE.SelectedItem    as string;
                    string discRaw   = filterDisc.SelectedItem   as string;
                    string discVal   = (discRaw == "All Disciplines" || discRaw == null) ? null : discRaw.Split('\u2014')[0].Trim();
                    var filtered = _data.Deliverables.Where(d =>
                        (dropVal   == "All Drops"      || d.DataDrop   == dropVal) &&
                        (statusVal == "All Status"     || d.Status     == statusVal) &&
                        (cdeVal    == "All CDE"        || d.CDE        == cdeVal) &&
                        (discVal   == null             || d.Discipline == discVal)
                    ).ToList();
                    dg.ItemsSource = filtered;
                };
                filterDrop.SelectionChanged   += (s, e) => applyFilter();
                filterStatus.SelectionChanged += (s, e) => applyFilter();
                filterCDE.SelectionChanged    += (s, e) => applyFilter();
                filterDisc.SelectionChanged   += (s, e) => applyFilter();

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

            // ── INLINE EDITABLE DELIVERABLE REGISTER ───────────────────
            root.Children.Add(new Border { Height = 8 });
            root.Children.Add(MakeSectionHeader("EDITABLE DELIVERABLE REGISTER"));

            // Toolbar
            var delToolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            var addDelBtn = new Button { Content = "+ Add Deliverable", Height = 26, Padding = new Thickness(10, 0, 10, 0), Background = Br(CGreen), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
            var delDelBtn = new Button { Content = "✕ Delete", Height = 26, Padding = new Thickness(10, 0, 10, 0), Background = Br(CRed), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
            var bulkStatusCb = new System.Windows.Controls.ComboBox { Width = 130, Height = 26, FontSize = 11, Margin = new Thickness(0, 0, 4, 0) };
            foreach (var st in new[] { "WIP", "FOR REVIEW", "FOR APPROVAL", "APPROVED", "SUPERSEDED" }) bulkStatusCb.Items.Add(st);
            bulkStatusCb.SelectedIndex = 0;
            var bulkApplyBtn = new Button { Content = "Bulk Status", Height = 26, Padding = new Thickness(8, 0, 8, 0), Background = Br(CAccent), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
            var exportRegBtn = new Button { Content = "Export Register", Height = 26, Padding = new Thickness(8, 0, 8, 0), Background = Br(CHeaderBg), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
            var filterStatusCb2 = new System.Windows.Controls.ComboBox { Width = 110, Height = 26, FontSize = 11, Margin = new Thickness(8, 0, 0, 0) };
            filterStatusCb2.Items.Add("All Status");
            foreach (var st in new[] { "WIP", "FOR REVIEW", "FOR APPROVAL", "APPROVED", "SUPERSEDED" }) filterStatusCb2.Items.Add(st);
            filterStatusCb2.SelectedIndex = 0;
            delToolbar.Children.Add(addDelBtn);
            delToolbar.Children.Add(delDelBtn);
            delToolbar.Children.Add(bulkStatusCb);
            delToolbar.Children.Add(bulkApplyBtn);
            delToolbar.Children.Add(exportRegBtn);
            delToolbar.Children.Add(new TextBlock { Text = "  Filter: ", VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
            delToolbar.Children.Add(filterStatusCb2);
            root.Children.Add(delToolbar);

            // Inline editable DataGrid
            var editDg = MakeExcelDataGrid(200);
            editDg.IsReadOnly = false;
            var delTypes = new List<string> { "Drawing", "Specification", "Report", "Model", "Schedule", "COBie" };
            var delDiscs = new List<string> { "A", "S", "M", "E", "P", "FP", "LV", "C", "G", "All" };
            var delStatuses = new List<string> { "WIP", "FOR REVIEW", "FOR APPROVAL", "APPROVED", "SUPERSEDED" };
            // Phase 96: BCC-Del-01 fix — previously "Disc" was bound to DeliverableRow.Suitability,
            // so picking a discipline silently overwrote the S0–S7 suitability code.
            // "Rev" was bound to DataDrop (DD1-DD4) — a separate field.
            // Re-binding to the correct properties + adding explicit Suitability (S0-S7),
            // DataDrop (DD1-DD4), and CDE State dropdowns from the canonical ISO 19650 lists.
            var delSuitabilities = new List<string> { "S0", "S1", "S2", "S3", "S4", "S5", "S6", "S7" };
            var delDataDrops = new List<string> { "DD1", "DD2", "DD3", "DD4", "—" };
            var delCDEStates = new List<string> { "WIP", "SHARED", "PUBLISHED", "ARCHIVE", "SUPERSEDED", "WITHDRAWN", "OBSOLETE" };
            editDg.Columns.Add(new DataGridTextColumn     { Header = "ID",          Binding = new Binding("Code"),                                                 Width = 80 });
            editDg.Columns.Add(new DataGridTextColumn     { Header = "Title",       Binding = new Binding("Name"),                                                 Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            editDg.Columns.Add(new DataGridComboBoxColumn { Header = "Type",        ItemsSource = delTypes,          SelectedItemBinding = new Binding("Type"),         Width = 95 });
            editDg.Columns.Add(new DataGridComboBoxColumn { Header = "Disc",        ItemsSource = delDiscs,          SelectedItemBinding = new Binding("Discipline"),   Width = 55 });
            editDg.Columns.Add(new DataGridComboBoxColumn { Header = "Suit.",       ItemsSource = delSuitabilities,  SelectedItemBinding = new Binding("Suitability"),  Width = 55 });
            editDg.Columns.Add(new DataGridComboBoxColumn { Header = "Data Drop",   ItemsSource = delDataDrops,      SelectedItemBinding = new Binding("DataDrop"),     Width = 70 });
            editDg.Columns.Add(new DataGridComboBoxColumn { Header = "Status",      ItemsSource = delStatuses,       SelectedItemBinding = new Binding("Status"),       Width = 110 });
            editDg.Columns.Add(new DataGridTextColumn     { Header = "Due Date",    Binding = new Binding("DueDate"),                                               Width = 80 });
            editDg.Columns.Add(new DataGridTextColumn     { Header = "Assigned To", Binding = new Binding("Owner"),                                                 Width = 90 });
            editDg.Columns.Add(new DataGridComboBoxColumn { Header = "CDE State",   ItemsSource = delCDEStates,      SelectedItemBinding = new Binding("CDE"),          Width = 90 });
            var editDelSource = new System.Collections.ObjectModel.ObservableCollection<DeliverableRow>(_data.Deliverables);
            editDg.ItemsSource = editDelSource;

            addDelBtn.Click += (s, e) => { editDelSource.Add(new DeliverableRow { Code = $"DLV-{(editDelSource.Count + 1):D3}", Status = "WIP", CDE = "WIP", Type = "Drawing", DueDate = DateTime.Today.AddDays(14).ToString("yyyy-MM-dd") }); };
            delDelBtn.Click += (s, e) => { if (editDg.SelectedItem is DeliverableRow dr) editDelSource.Remove(dr); };
            bulkApplyBtn.Click += (s, e) => { var st = bulkStatusCb.SelectedItem?.ToString(); foreach (var sel in editDg.SelectedItems.OfType<DeliverableRow>().ToList()) sel.Status = st; editDg.Items.Refresh(); DispatchAction("BulkDeliverableStatus"); };
            exportRegBtn.Click += (s, e) => DispatchAction("ExportDeliverablesRegister");
            filterStatusCb2.SelectionChanged += (s, e) => { var f = filterStatusCb2.SelectedItem?.ToString(); editDg.ItemsSource = f == "All Status" ? (System.Collections.IEnumerable)editDelSource : editDelSource.Where(d => d.Status == f).ToList(); };
            root.Children.Add(editDg);

            // ── TRANSMITTAL SECTION ─────────────────────────────────────
            root.Children.Add(new Border { Height = 8 });
            root.Children.Add(MakeSectionHeader("CREATE TRANSMITTAL"));
            var txCard = MakeCard();
            var txStack = new StackPanel();
            // TO: multi-person checklist
            txStack.Children.Add(new TextBlock { Text = "To: (select recipients)", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
            var toScroll = new ScrollViewer { MaxHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderBrush = Br(CBorder), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 6) };
            var toWrap = new WrapPanel { Margin = new Thickness(4) };
            var txRecipients = _data.TeamMembers.Count > 0
                ? _data.TeamMembers.Select(m => $"{m.Name} ({m.Role})").ToList()
                : GetISOStandardMembers();
            var toChecks = new List<CheckBox>();
            foreach (var nm in txRecipients)
            {
                var cb = new CheckBox { Content = nm, FontSize = 10, Margin = new Thickness(0, 0, 12, 3) };
                toChecks.Add(cb);
                toWrap.Children.Add(cb);
            }
            toScroll.Content = toWrap;
            txStack.Children.Add(toScroll);
            // CC: additional free-text
            var ccRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            ccRow.Children.Add(new TextBlock { Text = "CC (additional):", Width = 120, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
            ccRow.Children.Add(new System.Windows.Controls.TextBox { Width = 240, Height = 26, FontSize = 11, ToolTip = "Additional CC addresses (comma-separated)" });
            txStack.Children.Add(ccRow);
            var purposeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            purposeRow.Children.Add(new TextBlock { Text = "Purpose:", Width = 60, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
            purposeRow.Children.Add(new System.Windows.Controls.TextBox { Width = 300, Height = 26, FontSize = 11 });
            txStack.Children.Add(purposeRow);
            txStack.Children.Add(new TextBlock { Text = "Revision list auto-populated from selected deliverables", FontSize = 10, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)), FontStyle = FontStyles.Italic, Margin = new Thickness(0, 4, 0, 6) });
            var createTxBtn = new Button { Content = "Create Transmittal", Height = 28, Padding = new Thickness(14, 0, 14, 0), Background = Br(CAccent), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 12, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
            createTxBtn.Click += (s, e) => DispatchAction("CreateTransmittal");
            txStack.Children.Add(createTxBtn);
            txCard.Child = txStack;
            root.Children.Add(txCard);

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
        //  PROJECT MEMBERS TAB  (Phase 76 Item 12)
        //  Replaces separate PERMISSIONS + TEAM navigation buttons with
        //  a unified view: Member Directory, Permission Groups,
        //  CDE Access Matrix, Workload Overview, Activity Log.
        // ════════════════════════════════════════════════════════════════

        private UIElement BuildProjectMembersTab()
        {
            // Phase 77: Replaced with 3-sub-tab inline editable spreadsheet layout
            var outerStack = new StackPanel { Margin = new Thickness(16) };

            // ── KPI strip ──
            int totalMembers = _data.Roles.Count + _data.TeamMembers.Count;
            var kpiRow = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 12) };
            kpiRow.Children.Add(MakeKPICard("MEMBERS",     totalMembers.ToString(),           Br(CHeaderBg), "Total project team members"));
            kpiRow.Children.Add(MakeKPICard("ROLES",       _data.Roles.Count.ToString(),       Br(CGreen),    "Defined permission roles"));
            kpiRow.Children.Add(MakeKPICard("TEAM",        _data.TeamMembers.Count.ToString(), Br(CAccent),   "Named team members"));
            kpiRow.Children.Add(MakeKPICard("DISCIPLINES", "8",                               Br(Color.FromRgb(0x6A, 0x1B, 0x9A)), "Active disciplines on project"));
            outerStack.Children.Add(kpiRow);

            // ── 3-sub-tab TabControl ──
            var tc = new System.Windows.Controls.TabControl { Margin = new Thickness(0, 0, 0, 8) };

            // ══ Sub-tab 3A: Member Directory ══════════════════════════════
            var tabA = new System.Windows.Controls.TabItem { Header = "Member Directory" };
            var tabAStack = new StackPanel { Margin = new Thickness(8) };

            // Toolbar
            var memberToolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            var addMemberBtn = new Button { Content = "+ Add Row",    Height = 26, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 0), Background = Br(CGreen),                          Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            var delMemberBtn = new Button { Content = "Delete Row",   Height = 26, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 0), Background = Br(CRed),                           Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            var impCsvBtn    = new Button { Content = "Import CSV",   Height = 26, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 0), Background = Br(Color.FromRgb(0x45, 0x50, 0x6E)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            var expExlBtn    = new Button { Content = "Export Excel", Height = 26, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 0), Background = Br(Color.FromRgb(0x2E, 0x7D, 0x32)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            var saveMembBtn  = new Button { Content = "Save",         Height = 26, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 0), Background = Br(CAccent),                        Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            memberToolbar.Children.Add(addMemberBtn); memberToolbar.Children.Add(delMemberBtn);
            memberToolbar.Children.Add(impCsvBtn); memberToolbar.Children.Add(expExlBtn); memberToolbar.Children.Add(saveMembBtn);
            tabAStack.Children.Add(memberToolbar);

            var memberDg = MakeExcelDataGrid(180);
            memberDg.Columns.Add(new DataGridTextColumn { Header = "Name",         Binding = new System.Windows.Data.Binding("Name"),         Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
            memberDg.Columns.Add(new DataGridTextColumn { Header = "Company",      Binding = new System.Windows.Data.Binding("Company"),      Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            var isoRoles = new List<string>
            {
                "Client / Employer", "Appointing Party Representative", "Project Manager", "BIM Manager",
                "BIM Coordinator", "BIM Technician", "Information Manager", "CDE Administrator",
                "Delivery Team Leader", "Task Team Leader", "Author", "Checker / Approver",
                "Architect (Lead Designer)", "Structural Engineer", "Building Services / MEP Engineer",
                "Civil Engineer", "Landscape Architect", "Interior Designer", "Façade Engineer",
                "Acoustic Engineer", "Traffic Engineer", "Fire Engineer", "Lighting Designer",
                "Cost Manager / QS", "Project Programmer", "Health & Safety Manager", "Planning Consultant",
                "Principal Contractor", "Construction Manager", "Site Manager", "Subcontractor Manager",
                "Commissioning Manager", "FM Manager", "Asset Manager", "Data Drop Manager",
                "Document Controller", "Other"
            };
            var isoDisciplines = new List<string>
            {
                "A – Architecture", "B – Building Services / MEP", "C – Civil Engineering",
                "D – Drainage", "E – Electrical", "F – Fire Protection", "G – General / Multi-Discipline",
                "H – HVAC", "I – Interior Design", "J – Landscape", "K – Structural",
                "L – Lighting", "M – Mechanical", "N – Noise / Acoustics", "O – Other",
                "P – Plumbing", "Q – Quantity Surveying", "R – Renovation / Refurbishment",
                "S – Structural Engineering", "T – Transport / Traffic", "U – Urban Planning",
                "V – Ventilation", "W – Wet Services", "X – External Works", "Z – General Coordination"
            };
            memberDg.Columns.Add(new DataGridComboBoxColumn { Header = "Role",       ItemsSource = isoRoles,       SelectedItemBinding = new System.Windows.Data.Binding("Role"),       Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            memberDg.Columns.Add(new DataGridComboBoxColumn { Header = "Discipline",  ItemsSource = isoDisciplines, SelectedItemBinding = new System.Windows.Data.Binding("Discipline"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            memberDg.Columns.Add(new DataGridTextColumn { Header = "Email",        Binding = new System.Windows.Data.Binding("Email"),        Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
            memberDg.Columns.Add(new DataGridTextColumn { Header = "Phone",        Binding = new System.Windows.Data.Binding("Phone"),        Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            memberDg.Columns.Add(new DataGridTextColumn { Header = "CDE Username", Binding = new System.Windows.Data.Binding("CDEUsername"),  Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            memberDg.Columns.Add(new DataGridCheckBoxColumn { Header = "Can Approve", Binding = new System.Windows.Data.Binding("CanApprove"), Width = 90 });
            memberDg.Columns.Add(new DataGridCheckBoxColumn { Header = "Can Issue",   Binding = new System.Windows.Data.Binding("CanIssue"),   Width = 80 });
            memberDg.Columns.Add(new DataGridCheckBoxColumn { Header = "Active",      Binding = new System.Windows.Data.Binding("Active"),     Width = 60 });
            var memberSrc = new System.Collections.ObjectModel.ObservableCollection<TeamMemberRow>(_data.TeamMembers);
            memberDg.ItemsSource = memberSrc;
            addMemberBtn.Click += (s, e) => memberSrc.Add(new TeamMemberRow { Active = true });
            delMemberBtn.Click += (s, e) => { if (memberDg.SelectedItem is TeamMemberRow mr2) memberSrc.Remove(mr2); };
            impCsvBtn.Click    += (s, e) => DispatchAction("ImportTeamCSV");
            expExlBtn.Click    += (s, e) => ExportDataGridToXlsx(memberDg, "MemberDirectory");
            saveMembBtn.Click  += (s, e) => { _data.TeamMembers = new List<TeamMemberRow>(memberSrc); DispatchAction("SaveProjectMembers"); };
            tabAStack.Children.Add(memberDg);
            tabA.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = tabAStack };

            // ══ Sub-tab 3B: Permission Groups ════════════════════════════
            var tabB = new System.Windows.Controls.TabItem { Header = "Permission Groups" };
            var tabBStack = new StackPanel { Margin = new Thickness(8) };

            var roleToolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            var addRoleBtn  = new Button { Content = "Add Role",       Height = 26, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 0), Background = Br(CGreen),                          Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            var delRoleBtn  = new Button { Content = "Delete Role",    Height = 26, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 0), Background = Br(CRed),                           Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            var expMatBtn   = new Button { Content = "Export Matrix",  Height = 26, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 0), Background = Br(Color.FromRgb(0x45, 0x50, 0x6E)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            var savePermBtn = new Button { Content = "Save Permissions",Height = 26,Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 0), Background = Br(CAccent),                        Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            roleToolbar.Children.Add(addRoleBtn); roleToolbar.Children.Add(delRoleBtn);
            roleToolbar.Children.Add(expMatBtn); roleToolbar.Children.Add(savePermBtn);
            tabBStack.Children.Add(roleToolbar);

            // Phase 96: strict dropdowns on typo-prone fields
            // Role codes from the canonical ISO 19650 role catalog (GetDefaultRoles) so the Role Code
            // column stays in lock-step with the CDE Access Matrix role references (A,M,E,S,K,F,…).
            var roleCodeList = GetDefaultRoles().Select(r => r.Code).Distinct().OrderBy(c => c).ToList();
            var roleDisciplineList = new List<string>
            {
                "—", "Architecture", "Structural", "Mechanical", "Electrical", "Plumbing",
                "Fire", "Civil", "Landscape", "Interior Design", "Construction", "Cost",
                "FM", "Health & Safety", "Acoustic", "Fabrication", "Other"
            };
            // Comma-separated CDE state combos — covers 95% of real projects; editable for the rest.
            var cdeAccessPresets = new List<string>
            {
                "WIP", "WIP,SHARED", "WIP,SHARED,PUBLISHED", "WIP,SHARED,PUBLISHED,ARCHIVE",
                "SHARED", "SHARED,PUBLISHED", "SHARED,PUBLISHED,ARCHIVE",
                "PUBLISHED", "PUBLISHED,ARCHIVE", "ARCHIVE", "None"
            };
            var yesNoList = new List<string> { "Yes", "No" };
            var slaHoursList = new List<string> { "0", "4", "8", "12", "24", "48", "72", "168", "336" };

            // EditingElementStyle lets the comma-separated combos accept custom values too.
            var editableStyle = new Style(typeof(ComboBox));
            editableStyle.Setters.Add(new Setter(ComboBox.IsEditableProperty, true));

            var roleDg = MakeExcelDataGrid(180);
            roleDg.Columns.Add(new DataGridComboBoxColumn { Header = "Role Code",   ItemsSource = roleCodeList,      SelectedItemBinding = new System.Windows.Data.Binding("Code"),       Width = 80 });
            roleDg.Columns.Add(new DataGridTextColumn     { Header = "Name",        Binding = new System.Windows.Data.Binding("Name"),                                                 Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
            roleDg.Columns.Add(new DataGridComboBoxColumn { Header = "Discipline",  ItemsSource = roleDisciplineList, SelectedItemBinding = new System.Windows.Data.Binding("Discipline"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            roleDg.Columns.Add(new DataGridComboBoxColumn { Header = "CDE Access",  ItemsSource = cdeAccessPresets,  SelectedItemBinding = new System.Windows.Data.Binding("CDEAccess"),  Width = new DataGridLength(1, DataGridLengthUnitType.Star), EditingElementStyle = editableStyle });
            roleDg.Columns.Add(new DataGridComboBoxColumn { Header = "Can Approve", ItemsSource = yesNoList,         SelectedItemBinding = new System.Windows.Data.Binding("CanApprove"), Width = 85 });
            roleDg.Columns.Add(new DataGridComboBoxColumn { Header = "Can Issue",   ItemsSource = yesNoList,         SelectedItemBinding = new System.Windows.Data.Binding("CanIssue"),   Width = 75 });
            roleDg.Columns.Add(new DataGridComboBoxColumn { Header = "SLA Hours",   ItemsSource = slaHoursList,      SelectedItemBinding = new System.Windows.Data.Binding("SLAHours"),   Width = 80, EditingElementStyle = editableStyle });
            var roleSrc = new System.Collections.ObjectModel.ObservableCollection<RoleDefinition>(_data.Roles);
            roleDg.ItemsSource = roleSrc;
            addRoleBtn.Click  += (s, e) => roleSrc.Add(new RoleDefinition());
            delRoleBtn.Click  += (s, e) => { if (roleDg.SelectedItem is RoleDefinition rd) roleSrc.Remove(rd); };
            expMatBtn.Click   += (s, e) => DispatchAction("ExportPermissionMatrix");
            savePermBtn.Click += (s, e) => { _data.Roles = new List<RoleDefinition>(roleSrc); DispatchAction("SavePermissions"); };
            tabBStack.Children.Add(roleDg);
            tabB.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = tabBStack };

            // ══ Sub-tab 3C: CDE Access Matrix ════════════════════════════
            var tabC = new System.Windows.Controls.TabItem { Header = "CDE Access Matrix" };
            var tabCStack = new StackPanel { Margin = new Thickness(8) };

            var cdeToolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            var addCdeBtn = new Button { Content = "Add Row",     Height = 26, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 0), Background = Br(CGreen),                          Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            var delCdeBtn = new Button { Content = "Delete Row",  Height = 26, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 0), Background = Br(CRed),                           Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            var expCdeBtn = new Button { Content = "Export Excel",Height = 26, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 0), Background = Br(Color.FromRgb(0x2E, 0x7D, 0x32)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            cdeToolbar.Children.Add(addCdeBtn); cdeToolbar.Children.Add(delCdeBtn); cdeToolbar.Children.Add(expCdeBtn);
            tabCStack.Children.Add(cdeToolbar);

            // Phase 96: strict dropdowns on typo-prone fields for the CDE matrix.
            // Folder list seeded from the 12 default ISO 19650 folders but kept editable so
            // projects can add sub-folders (e.g. "05_MODELS/COORDINATION") without leaving the combo.
            var cdeFolderList = GetDefaultFolderPermissions().Select(f => f.Folder).Distinct().ToList();
            // CDE states: strict — WIP/SHARED/PUBLISHED/ARCHIVE + 3 ISO 19650-2 terminal states
            // (SUPERSEDED/WITHDRAWN/OBSOLETE). Matches BIMManagerEngine.CDEStates exactly.
            var cdeStateList = new List<string>
            {
                "WIP", "SHARED", "PUBLISHED", "ARCHIVE",
                "SUPERSEDED", "WITHDRAWN", "OBSOLETE"
            };
            // Role-list presets — match the strings produced by GetDefaultFolderPermissions()
            // so the matrix renders without diff on first open. Custom entries remain possible
            // because EditingElementStyle enables ComboBox.IsEditable for these columns.
            var readRolesPresets = new List<string>
            {
                "All team", "All team + Client", "All team + FM", "All team + FM + Client",
                "BIM Manager only", "Coordinator + BIM Manager", "Originator only", "None"
            };
            var writeRolesPresets = new List<string>
            {
                "A,M,E,S", "A,M,E,S,H,P,C", "A,M,E,S,H,P,C,I,Q,L",
                "A,M,E,S (originators)", "All team", "BIM Manager", "BIM Manager only",
                "Coordinator", "Client only", "None"
            };
            var approveRolesPresets = new List<string>
            {
                "", "A", "K", "A,K", "K,A", "K,F", "K,F,A", "BIM Manager only", "Client only"
            };

            var cdeEditableStyle = new Style(typeof(ComboBox));
            cdeEditableStyle.Setters.Add(new Setter(ComboBox.IsEditableProperty, true));

            var cdeDg = MakeExcelDataGrid(180);
            cdeDg.Columns.Add(new DataGridComboBoxColumn { Header = "Folder",        ItemsSource = cdeFolderList,       SelectedItemBinding = new System.Windows.Data.Binding("Folder"),       Width = new DataGridLength(1.2, DataGridLengthUnitType.Star), EditingElementStyle = cdeEditableStyle });
            cdeDg.Columns.Add(new DataGridComboBoxColumn { Header = "CDE State",     ItemsSource = cdeStateList,        SelectedItemBinding = new System.Windows.Data.Binding("CDEState"),     Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            cdeDg.Columns.Add(new DataGridComboBoxColumn { Header = "Read Roles",    ItemsSource = readRolesPresets,    SelectedItemBinding = new System.Windows.Data.Binding("ReadRoles"),    Width = new DataGridLength(1.5, DataGridLengthUnitType.Star), EditingElementStyle = cdeEditableStyle });
            cdeDg.Columns.Add(new DataGridComboBoxColumn { Header = "Write Roles",   ItemsSource = writeRolesPresets,   SelectedItemBinding = new System.Windows.Data.Binding("WriteRoles"),   Width = new DataGridLength(1.5, DataGridLengthUnitType.Star), EditingElementStyle = cdeEditableStyle });
            cdeDg.Columns.Add(new DataGridComboBoxColumn { Header = "Approve Roles", ItemsSource = approveRolesPresets, SelectedItemBinding = new System.Windows.Data.Binding("ApproveRoles"), Width = new DataGridLength(1.5, DataGridLengthUnitType.Star), EditingElementStyle = cdeEditableStyle });
            var cdeSrc = new System.Collections.ObjectModel.ObservableCollection<FolderPermission>(_data.FolderPermissions.Count > 0 ? _data.FolderPermissions : GetDefaultFolderPermissions());
            cdeDg.ItemsSource = cdeSrc;
            addCdeBtn.Click += (s, e) => cdeSrc.Add(new FolderPermission());
            delCdeBtn.Click += (s, e) => { if (cdeDg.SelectedItem is FolderPermission fp) cdeSrc.Remove(fp); };
            expCdeBtn.Click += (s, e) => ExportDataGridToXlsx(cdeDg, "CDEAccessMatrix");
            tabCStack.Children.Add(cdeDg);
            tabC.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = tabCStack };

            tc.Items.Add(tabA);
            tc.Items.Add(tabB);
            tc.Items.Add(tabC);
            outerStack.Children.Add(tc);

            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            sv.Content = outerStack;
            return sv;
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
                // ── ISO 19650-2 Appointing Party ──
                new() { Code = "CL",  Name = "Client / Appointing Party",    Discipline = "—",            CDEAccess = "SHARED,PUBLISHED,ARCHIVE", CanApprove = "Yes", CanIssue = "Yes", SLAHours = "48", Members = "" },
                // ── Lead Appointed Party (LAP) ──
                new() { Code = "PM",  Name = "Project Manager",              Discipline = "—",            CDEAccess = "WIP,SHARED,PUBLISHED",     CanApprove = "Yes", CanIssue = "Yes", SLAHours = "24", Members = "" },
                new() { Code = "BIM", Name = "BIM Manager",                  Discipline = "—",            CDEAccess = "WIP,SHARED,PUBLISHED,ARCHIVE", CanApprove = "Yes", CanIssue = "Yes", SLAHours = "4", Members = "" },
                new() { Code = "CA",  Name = "Contract Administrator",       Discipline = "—",            CDEAccess = "SHARED,PUBLISHED",         CanApprove = "Yes", CanIssue = "Yes", SLAHours = "48", Members = "" },
                new() { Code = "QS",  Name = "Quantity Surveyor",            Discipline = "Cost",         CDEAccess = "WIP,SHARED",               CanApprove = "No",  CanIssue = "Yes", SLAHours = "48", Members = "" },
                // ── Appointed Parties (Designers) ──
                new() { Code = "AR",  Name = "Architect",                    Discipline = "Architecture", CDEAccess = "WIP,SHARED",               CanApprove = "Yes", CanIssue = "Yes", SLAHours = "24", Members = "" },
                new() { Code = "SE",  Name = "Structural Engineer",          Discipline = "Structural",   CDEAccess = "WIP,SHARED",               CanApprove = "No",  CanIssue = "Yes", SLAHours = "24", Members = "" },
                new() { Code = "ME",  Name = "MEP Engineer",                 Discipline = "Mechanical",   CDEAccess = "WIP,SHARED",               CanApprove = "No",  CanIssue = "Yes", SLAHours = "24", Members = "" },
                new() { Code = "M",   Name = "Mechanical Engineer",          Discipline = "Mechanical",   CDEAccess = "WIP,SHARED",               CanApprove = "No",  CanIssue = "Yes", SLAHours = "48", Members = "" },
                new() { Code = "EL",  Name = "Electrical Engineer",          Discipline = "Electrical",   CDEAccess = "WIP,SHARED",               CanApprove = "No",  CanIssue = "Yes", SLAHours = "48", Members = "" },
                new() { Code = "PH",  Name = "Public Health Engineer",       Discipline = "Plumbing",     CDEAccess = "WIP,SHARED",               CanApprove = "No",  CanIssue = "Yes", SLAHours = "48", Members = "" },
                new() { Code = "FP",  Name = "Fire Engineer",                Discipline = "Fire",         CDEAccess = "WIP,SHARED",               CanApprove = "No",  CanIssue = "Yes", SLAHours = "24", Members = "" },
                new() { Code = "CV",  Name = "Civil Engineer",               Discipline = "Civil",        CDEAccess = "WIP,SHARED",               CanApprove = "No",  CanIssue = "Yes", SLAHours = "48", Members = "" },
                new() { Code = "ID",  Name = "Interior Designer",            Discipline = "Architecture", CDEAccess = "WIP",                      CanApprove = "No",  CanIssue = "No",  SLAHours = "48", Members = "" },
                new() { Code = "LA",  Name = "Landscape Architect",          Discipline = "Landscape",    CDEAccess = "WIP,SHARED",               CanApprove = "No",  CanIssue = "Yes", SLAHours = "48", Members = "" },
                // ── Construction ──
                new() { Code = "CT",  Name = "Main Contractor",              Discipline = "Construction", CDEAccess = "SHARED,PUBLISHED",         CanApprove = "No",  CanIssue = "Yes", SLAHours = "24", Members = "" },
                new() { Code = "SC",  Name = "Specialist Subcontractor",     Discipline = "Construction", CDEAccess = "SHARED",                   CanApprove = "No",  CanIssue = "No",  SLAHours = "48", Members = "" },
                new() { Code = "CW",  Name = "Clerk of Works",               Discipline = "—",            CDEAccess = "SHARED,PUBLISHED",         CanApprove = "No",  CanIssue = "Yes", SLAHours = "24", Members = "" },
                // ── Operations ──
                new() { Code = "FM",  Name = "Facilities Manager",           Discipline = "FM",           CDEAccess = "PUBLISHED,ARCHIVE",        CanApprove = "Yes", CanIssue = "No",  SLAHours = "72", Members = "" },
                new() { Code = "OM",  Name = "Operations Manager",           Discipline = "FM",           CDEAccess = "PUBLISHED,ARCHIVE",        CanApprove = "No",  CanIssue = "No",  SLAHours = "72", Members = "" },
                // ── Support Roles ──
                new() { Code = "BC",  Name = "BIM Coordinator",              Discipline = "—",            CDEAccess = "WIP,SHARED",               CanApprove = "No",  CanIssue = "Yes", SLAHours = "8",  Members = "" },
                new() { Code = "BT",  Name = "BIM Technician",               Discipline = "—",            CDEAccess = "WIP",                      CanApprove = "No",  CanIssue = "No",  SLAHours = "24", Members = "" },
                new() { Code = "GN",  Name = "General / Non-disciplinary",   Discipline = "—",            CDEAccess = "WIP",                      CanApprove = "No",  CanIssue = "No",  SLAHours = "0",  Members = "" },
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
            var outerTabs = new TabControl { Background = Br(CPageBg), BorderThickness = new Thickness(0), Padding = new Thickness(0) };

            // ── Sub-tab A: Meetings Register ───────────────────────────────
            var mtgListPanel = new StackPanel { Margin = new Thickness(12) };
            var mtgSource = new System.Collections.ObjectModel.ObservableCollection<MeetingRow>(_data.Meetings);

            // KPI strip
            var mtgKpi = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, 0, 8) };
            int totalMtg = mtgSource.Count;
            int plannedMtg = mtgSource.Count(m => m.Status == "PLANNED");
            int completedMtg = mtgSource.Count(m => m.Status == "COMPLETED");
            int cancelledMtg = mtgSource.Count(m => m.Status == "CANCELLED");
            int openActionsMtg = _data.ActionItems.Count(a => a.Status != "CLOSED");
            mtgKpi.Children.Add(MakeKPICard("TOTAL", totalMtg.ToString(), Br(CHeaderBg), "All meetings on record"));
            mtgKpi.Children.Add(MakeKPICard("PLANNED", plannedMtg.ToString(), Br(CAccent), "Scheduled meetings"));
            mtgKpi.Children.Add(MakeKPICard("COMPLETED", completedMtg.ToString(), Br(CGreen), "Meetings completed"));
            mtgKpi.Children.Add(MakeKPICard("CANCELLED", cancelledMtg.ToString(), Br(CRed), "Cancelled meetings"));
            mtgKpi.Children.Add(MakeKPICard("OPEN ACTIONS", openActionsMtg.ToString(), openActionsMtg > 0 ? Br(CRed) : Br(CGreen), "Outstanding action items across all meetings"));
            mtgListPanel.Children.Add(mtgKpi);

            var mtgDg = MakeExcelDataGrid(200);
            mtgDg.IsReadOnly = false;
            var mtgTypes = new List<string>
            {
                // Pre-Construction
                "Pre-Construction Meeting", "Project Kick-Off", "Client Briefing", "Feasibility Review", "Planning Pre-Application",
                // Design & Coordination
                "BIM Coordination", "Design Review", "Technical Design Meeting", "Clash Detection Review", "Stage Gate Review",
                "RIBA Stage Review", "Design Team Meeting", "Multi-Discipline Coordination",
                // Procurement & Commercial
                "Tender Review Meeting", "Contractor Briefing", "Procurement Strategy Meeting", "Risk Review",
                "Value Engineering Workshop", "Programme Review",
                // Construction
                "Site Progress Meeting", "Construction Phase Meeting", "Subcontractor Coordination",
                "Inspection and Test Meeting", "Health & Safety Review", "Quality Review",
                // Handover & FM
                "Data Drop Review", "Handover Meeting", "COBie Review", "FM Commissioning Meeting",
                "Client Handover Meeting", "Post-Occupancy Evaluation",
                // General
                "Client Meeting", "Stakeholder Engagement", "Project Board Meeting", "Change Control Meeting",
                "Lessons Learned", "Weekly Team Meeting", "Issue Resolution Meeting", "Workshop", "Other"
            };
            var mtgStatuses = new List<string> { "PLANNED", "IN PROGRESS", "COMPLETED", "CANCELLED", "POSTPONED", "NO QUORUM" };
            // Phase 98: strict dropdowns on the remaining typo-prone meeting columns.
            // Chair: seeded from team directory (so BIM Managers can't be typed as
            // "BIM Mgr" / "BIMM" / "B. Manager" on different rows) — editable for
            // client-side chairs not in the team list.
            var mtgChairList = _data.TeamMembers.Select(m => m.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n).ToList();
            if (mtgChairList.Count == 0)
                mtgChairList.AddRange(new[] { "BIM Manager", "Project Manager", "Lead Architect", "Client", "Contractor", "TBC" });
            var mtgLocationPresets = new List<string>
            {
                "Site Meeting Room", "Client Office", "Design Team Office", "Contractor Site Office",
                "MS Teams", "Zoom", "Google Meet", "Webex", "Hybrid (In-Person + MS Teams)", "Hybrid (In-Person + Zoom)",
                "Construction Site (Block A)", "Construction Site (Block B)", "BIM Hub", "TBC"
            };
            var mtgEditableStyle = new Style(typeof(ComboBox));
            mtgEditableStyle.Setters.Add(new Setter(ComboBox.IsEditableProperty, true));
            mtgDg.Columns.Add(new DataGridTextColumn     { Header = "ID",              Binding = new Binding("MeetingId"),                                        Width = 70, IsReadOnly = true });
            mtgDg.Columns.Add(new DataGridTextColumn     { Header = "Title",           Binding = new Binding("Title"),                                             Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            mtgDg.Columns.Add(new DataGridComboBoxColumn { Header = "Type",            ItemsSource = mtgTypes,            SelectedItemBinding = new Binding("Type"),     Width = 140, EditingElementStyle = mtgEditableStyle });
            mtgDg.Columns.Add(new DataGridTextColumn     { Header = "Date",            Binding = new Binding("Date"),                                              Width = 78 });
            mtgDg.Columns.Add(new DataGridTextColumn     { Header = "Time",            Binding = new Binding("Time"),                                              Width = 55 });
            mtgDg.Columns.Add(new DataGridComboBoxColumn { Header = "Location / Link", ItemsSource = mtgLocationPresets,  SelectedItemBinding = new Binding("Location"), Width = 140, EditingElementStyle = mtgEditableStyle });
            mtgDg.Columns.Add(new DataGridComboBoxColumn { Header = "Status",          ItemsSource = mtgStatuses,         SelectedItemBinding = new Binding("Status"),   Width = 110 });
            mtgDg.Columns.Add(new DataGridComboBoxColumn { Header = "Chair",           ItemsSource = mtgChairList,        SelectedItemBinding = new Binding("Chair"),    Width = 130, EditingElementStyle = mtgEditableStyle });
            mtgDg.ItemsSource = mtgSource;

            // Row colouring by status
            var mtgRowStyle = new Style(typeof(DataGridRow));
            var mtgPlannedT = new DataTrigger { Binding = new Binding("Status"), Value = "PLANNED" };
            mtgPlannedT.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Br(Color.FromRgb(0xFF, 0xFB, 0xE5))));
            mtgRowStyle.Triggers.Add(mtgPlannedT);
            var mtgCompletedT = new DataTrigger { Binding = new Binding("Status"), Value = "COMPLETED" };
            mtgCompletedT.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Br(Color.FromRgb(0xE8, 0xF5, 0xE9))));
            mtgRowStyle.Triggers.Add(mtgCompletedT);
            var mtgCancelledT = new DataTrigger { Binding = new Binding("Status"), Value = "CANCELLED" };
            mtgCancelledT.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Br(Color.FromRgb(0xEF, 0xEB, 0xEB))));
            mtgRowStyle.Triggers.Add(mtgCancelledT);
            mtgDg.RowStyle = mtgRowStyle;

            var mtgToolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            var newMtgBtn = new Button { Content = "＋ New Meeting", Height = 26, Padding = new Thickness(10, 0, 10, 0), Background = Br(CHeaderBg), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
            var dupMtgBtn = new Button { Content = "⧉ Duplicate", Height = 26, Padding = new Thickness(8, 0, 8, 0), Background = Br(Color.FromRgb(0x45, 0x50, 0x6E)), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
            var delMtgBtn = new Button { Content = "✕ Delete", Height = 26, Padding = new Thickness(8, 0, 8, 0), Background = Br(CRed), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
            var expMinBtn = new Button { Content = "Export Minutes", Height = 26, Padding = new Thickness(8, 0, 8, 0), Background = Br(CGreen), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
            var expWordBtn2 = new Button { Content = "Export Word", Height = 26, Padding = new Thickness(8, 0, 8, 0), Background = Br(Color.FromRgb(0x15, 0x65, 0xC0)), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
            var expPdfBtn = new Button { Content = "Export PDF", Height = 26, Padding = new Thickness(8, 0, 8, 0), Background = Br(CAccent), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
            var mtgFilterCb = new System.Windows.Controls.ComboBox { Width = 120, Height = 26, FontSize = 11, Margin = new Thickness(8, 0, 0, 0) };
            mtgFilterCb.Items.Add("All Status"); foreach (var s in mtgStatuses) mtgFilterCb.Items.Add(s); mtgFilterCb.SelectedIndex = 0;

            newMtgBtn.Click += (s, e) => {
                var n = mtgSource.Count + 1;
                mtgSource.Add(new MeetingRow { MeetingId = $"MTG-{n:D3}", Status = "PLANNED", Date = DateTime.Today.AddDays(7).ToString("yyyy-MM-dd"), Time = "09:00", Type = "BIM Coordination", Title = $"BIM Coordination Meeting {n}" });
            };
            dupMtgBtn.Click += (s, e) => { if (mtgDg.SelectedItem is MeetingRow src) { var n2 = mtgSource.Count + 1; mtgSource.Add(new MeetingRow { MeetingId = $"MTG-{n2:D3}", Title = src.Title + " (copy)", Type = src.Type, Location = src.Location, Status = "PLANNED", Date = src.Date, Time = src.Time }); } };
            delMtgBtn.Click += (s, e) => { if (mtgDg.SelectedItem is MeetingRow mr) mtgSource.Remove(mr); };
            expMinBtn.Click += (s, e) => DispatchAction("ExportMeetingMinutes");
            expWordBtn2.Click += (s, e) => DispatchAction("ExportMinutesWord");
            expPdfBtn.Click += (s, e) => DispatchAction("ExportMeetingsPDF");
            mtgFilterCb.SelectionChanged += (s, e) => { var f = mtgFilterCb.SelectedItem?.ToString(); mtgDg.ItemsSource = f == "All Status" ? (System.Collections.IEnumerable)mtgSource : mtgSource.Where(m => m.Status == f).ToList(); };

            mtgToolbar.Children.Add(newMtgBtn); mtgToolbar.Children.Add(dupMtgBtn); mtgToolbar.Children.Add(delMtgBtn); mtgToolbar.Children.Add(expMinBtn); mtgToolbar.Children.Add(expWordBtn2); mtgToolbar.Children.Add(expPdfBtn);
            mtgToolbar.Children.Add(new TextBlock { Text = "Filter:", VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Margin = new Thickness(8, 0, 4, 0) }); mtgToolbar.Children.Add(mtgFilterCb);

            // Detail card for selected meeting
            var mtgDetailCard = MakeCard();
            var mtgDetailStack = new StackPanel();
            var selMtgLabel = new TextBlock { Text = "Select a meeting to view details", FontSize = 11, Foreground = Brushes.Gray, FontStyle = FontStyles.Italic };
            var agendaLabel = new TextBlock { Text = "Agenda:", FontWeight = FontWeights.SemiBold, FontSize = 11, Margin = new Thickness(0, 4, 0, 2), Visibility = Visibility.Collapsed };
            var agendaBox = new System.Windows.Controls.TextBox { Height = 48, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 11, Visibility = Visibility.Collapsed };
            var attendeesLabel = new TextBlock { Text = "Attendees: —", FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
            mtgDg.SelectionChanged += (s, e) => {
                if (mtgDg.SelectedItem is MeetingRow sel) {
                    selMtgLabel.Text = $"📋 {sel.MeetingId}: {sel.Title}  |  {sel.Date} {sel.Time}  |  {sel.Location}";
                    selMtgLabel.FontStyle = FontStyles.Normal; selMtgLabel.Foreground = Br(CHeaderBg);
                    agendaLabel.Visibility = Visibility.Visible; agendaBox.Visibility = Visibility.Visible;
                    agendaBox.Text = sel.Agenda ?? ""; attendeesLabel.Text = $"Attendees: {sel.Attendees}";
                }
            };
            mtgDetailStack.Children.Add(selMtgLabel); mtgDetailStack.Children.Add(agendaLabel); mtgDetailStack.Children.Add(agendaBox); mtgDetailStack.Children.Add(attendeesLabel);
            mtgDetailCard.Child = mtgDetailStack;
            mtgListPanel.Children.Add(mtgToolbar); mtgListPanel.Children.Add(mtgDg); mtgListPanel.Children.Add(mtgDetailCard);
            var sv6A = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = mtgListPanel };

            // ── Sub-tab B: Attendee Manager ────────────────────────────────
            var attPanel = new StackPanel { Margin = new Thickness(12) };
            attPanel.Children.Add(MakeSectionHeader("MEETING ATTENDEE CONFIGURATION"));
            attPanel.Children.Add(new TextBlock { Text = "Configure who attends each meeting type. Check each person and their role (Required / Optional / Notify-only).", FontSize = 11, Foreground = Br(Color.FromRgb(0x55, 0x55, 0x55)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

            // Meeting type selector
            var attMtgTypeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            attMtgTypeRow.Children.Add(new TextBlock { Text = "Meeting Type:", Width = 110, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
            var attTypeCb = new System.Windows.Controls.ComboBox { Width = 200, Height = 28, FontSize = 11 };
            // Phase 91: Full AEC/FM grouped meeting type list
            void AddMtgSep(string label) => attTypeCb.Items.Add(new ComboBoxItem { Content = $"── {label} ──", IsEnabled = false, FontStyle = FontStyles.Italic, Foreground = Br(Color.FromRgb(0x75, 0x75, 0x75)) });
            void AddMtgItem(string code, string label, bool isDefault = false) {
                var ci = new ComboBoxItem { Content = label, Tag = code };
                attTypeCb.Items.Add(ci);
                if (isDefault) attTypeCb.SelectedItem = ci;
            }
            AddMtgSep("Pre-Construction");
            AddMtgItem("BRIEF", "BRIEF / Client Briefing");
            AddMtgItem("FES", "FES / Feasibility Study Review");
            AddMtgItem("RIBA_A", "RIBA Stage 0/A — Strategic Definition");
            AddMtgItem("RIBA_1", "RIBA Stage 1 — Preparation & Briefing");
            AddMtgItem("RIBA_2", "RIBA Stage 2 — Concept Design");
            AddMtgItem("RIBA_3", "RIBA Stage 3 — Spatial Coordination");
            AddMtgItem("RIBA_4", "RIBA Stage 4 — Technical Design");
            AddMtgItem("PLAN_APP", "PLAN_APP / Planning Application Review");
            AddMtgSep("Design & Coordination");
            AddMtgItem("BIM_COORD", "BIM_COORD / BIM Coordination", isDefault: true);
            AddMtgItem("DESIGN_REV", "DESIGN_REV / Design Review");
            AddMtgItem("CLASH_DET", "CLASH_DET / Clash Detection");
            AddMtgItem("DATA_DROP", "DATA_DROP / Data Drop Review (DD1–DD4)");
            AddMtgItem("STAGE_GATE", "STAGE_GATE / Stage Gate / RIBA Gateway");
            AddMtgItem("TECH_DES", "TECH_DES / Technical Design Workshop");
            AddMtgItem("SPEC_REV", "SPEC_REV / Specification Review");
            AddMtgItem("VALUE_ENG", "VALUE_ENG / Value Engineering");
            AddMtgSep("Procurement & Commercial");
            AddMtgItem("TENDER", "TENDER / Tender Review");
            AddMtgItem("CONTRACT", "CONTRACT / Contract Award");
            AddMtgItem("COMMERCIAL", "COMMERCIAL / Commercial / Cost Review");
            AddMtgItem("RISK", "RISK / Risk Register Review");
            AddMtgItem("PROGRAMME", "PROGRAMME / Programme / Schedule Review");
            AddMtgSep("Construction");
            AddMtgItem("SITE_PROG", "SITE_PROG / Site Progress");
            AddMtgItem("SITE_VISIT", "SITE_VISIT / Site Inspection / Walk");
            AddMtgItem("SUBCON", "SUBCON / Sub-Contractor Coordination");
            AddMtgItem("SAFETY", "SAFETY / Health & Safety Review");
            AddMtgItem("QA_QC", "QA_QC / QA / QC Inspection");
            AddMtgItem("RFI_REV", "RFI_REV / RFI / NCR Review");
            AddMtgItem("CHANGE", "CHANGE / Change Management");
            AddMtgSep("Handover & FM");
            AddMtgItem("HANDOVER", "HANDOVER / Handover Review");
            AddMtgItem("COMMISSIONING", "COMMISSIONING / Commissioning");
            AddMtgItem("SNAGGING", "SNAGGING / Snagging / Defects Review");
            AddMtgItem("COBIE_REV", "COBIE_REV / COBie / FM Data Review");
            AddMtgItem("FM_OPS", "FM_OPS / FM Operations Briefing");
            AddMtgItem("ASSET_MGMT", "ASSET_MGMT / Asset Management Review");
            AddMtgItem("PPM", "PPM / Planned Preventive Maintenance Review");
            AddMtgSep("General");
            AddMtgItem("CLIENT", "CLIENT / Client / Stakeholder Meeting");
            AddMtgItem("MGMT", "MGMT / Project Management");
            AddMtgItem("INTERNAL", "INTERNAL / Internal Team");
            AddMtgItem("LESSONS", "LESSONS / Lessons Learned");
            AddMtgItem("ADHOC", "ADHOC / Ad-hoc / Other");
            if (attTypeCb.SelectedItem == null && attTypeCb.Items.Count > 1) attTypeCb.SelectedIndex = 1;
            attMtgTypeRow.Children.Add(attTypeCb);
            attPanel.Children.Add(attMtgTypeRow);

            // Attendee grid
            var attDg = MakeExcelDataGrid(200);
            attDg.IsReadOnly = false;
            attDg.Columns.Add(new DataGridCheckBoxColumn { Header = "✓", Binding = new System.Windows.Data.Binding("CanApprove"), Width = 28 });
            attDg.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(1.5, DataGridLengthUnitType.Star), IsReadOnly = true });
            attDg.Columns.Add(new DataGridTextColumn { Header = "Company", Binding = new System.Windows.Data.Binding("Company"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
            attDg.Columns.Add(new DataGridTextColumn { Header = "Role", Binding = new System.Windows.Data.Binding("Role"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
            attDg.Columns.Add(new DataGridTextColumn { Header = "Discipline", Binding = new System.Windows.Data.Binding("Discipline"), Width = 80, IsReadOnly = true });
            attDg.Columns.Add(new DataGridTextColumn { Header = "Email", Binding = new System.Windows.Data.Binding("Email"), Width = new DataGridLength(1.5, DataGridLengthUnitType.Star), IsReadOnly = true });
            attDg.ItemsSource = _data.TeamMembers.Count > 0
                ? (System.Collections.IEnumerable)_data.TeamMembers
                : new List<TeamMemberRow> {
                    new() { Name = "BIM Manager", Company = "STING", Role = "Lead", Discipline = "BIM", CanApprove = true },
                    new() { Name = "Project Manager", Company = "Client", Role = "PM", Discipline = "—", CanApprove = false },
                    new() { Name = "Lead Architect", Company = "Architect", Role = "Design Lead", Discipline = "Architecture", CanApprove = false }
                };

            // Add external attendee row
            var extRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            extRow.Children.Add(new TextBlock { Text = "Add external:", Width = 90, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
            var extNameTb = new System.Windows.Controls.TextBox { Width = 140, Height = 26, FontSize = 11, Margin = new Thickness(0, 0, 4, 0) };
            var extEmailTb = new System.Windows.Controls.TextBox { Width = 180, Height = 26, FontSize = 11, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Email address" };
            var extCompTb = new System.Windows.Controls.TextBox { Width = 120, Height = 26, FontSize = 11, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Company" };
            var addExtBtn = new Button { Content = "+ Add", Height = 26, Padding = new Thickness(8, 0, 8, 0), Background = Br(CGreen), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand };
            addExtBtn.Click += (s, e) => {
                if (!string.IsNullOrWhiteSpace(extNameTb.Text)) {
                    _data.TeamMembers.Add(new TeamMemberRow { Name = extNameTb.Text, Email = extEmailTb.Text, Company = extCompTb.Text, Role = "External", Active = true });
                    attDg.ItemsSource = null; attDg.ItemsSource = _data.TeamMembers;
                    extNameTb.Text = ""; extEmailTb.Text = ""; extCompTb.Text = "";
                }
            };
            extRow.Children.Add(extNameTb); extRow.Children.Add(extEmailTb); extRow.Children.Add(extCompTb); extRow.Children.Add(addExtBtn);

            var attBtnRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var saveAttBtn = new Button { Content = "Save Template", Height = 26, Padding = new Thickness(10, 0, 10, 0), Background = Br(CAccent), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
            var sendInvBtn = new Button { Content = "📧 Send Invites", Height = 26, Padding = new Thickness(10, 0, 10, 0), Background = Br(Color.FromRgb(0x15, 0x65, 0xC0)), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Generate email invites / calendar entries for all checked attendees" };
            var copyListBtn = new Button { Content = "Copy List", Height = 26, Padding = new Thickness(10, 0, 10, 0), Background = Br(Color.FromRgb(0x45, 0x50, 0x6E)), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand };
            saveAttBtn.Click += (s, e) => DispatchAction("SaveProjectMembers");
            sendInvBtn.Click += (s, e) => DispatchAction("SendMeetingInvites");
            copyListBtn.Click += (s, e) => {
                var names = _data.TeamMembers.Where(m => m.Active).Select(m => m.Email ?? m.Name);
                Clipboard.SetText(string.Join("; ", names));
            };
            attBtnRow.Children.Add(saveAttBtn); attBtnRow.Children.Add(sendInvBtn); attBtnRow.Children.Add(copyListBtn);

            attPanel.Children.Add(attDg); attPanel.Children.Add(extRow); attPanel.Children.Add(attBtnRow);
            var sv6B = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = attPanel };

            // ── Sub-tab C: Action Items ────────────────────────────────────
            var actItemsPanel = new StackPanel { Margin = new Thickness(12) };
            var actSource = new System.Collections.ObjectModel.ObservableCollection<ActionItemRow>(_data.ActionItems);
            var actDg = MakeExcelDataGrid(220);
            actDg.IsReadOnly = false;
            var actPriorities = new List<string> { "CRITICAL", "HIGH", "MEDIUM", "LOW" };
            var actStatuses = new List<string> { "OPEN", "IN PROGRESS", "PENDING INFO", "CLOSED" };
            // Phase 96: Owner dropdown seeded from team directory so action items can't be assigned
            // to a mistyped name. Editable fallback keeps ad-hoc owners possible (e.g. "Client TBC").
            var actOwnerList = _data.TeamMembers.Select(m => m.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n).ToList();
            if (actOwnerList.Count == 0)
                actOwnerList.AddRange(new[] { "BIM Manager", "Project Manager", "Lead Architect", "Lead Engineer", "Client", "Contractor", "TBC" });
            var actOwnerStyle = new Style(typeof(ComboBox));
            actOwnerStyle.Setters.Add(new Setter(ComboBox.IsEditableProperty, true));
            actDg.Columns.Add(new DataGridTextColumn     { Header = "Action ID",   Binding = new Binding("ActionId"),                                           Width = 80, IsReadOnly = true });
            actDg.Columns.Add(new DataGridTextColumn     { Header = "Description", Binding = new Binding("Description"),                                         Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            actDg.Columns.Add(new DataGridComboBoxColumn { Header = "Owner",       ItemsSource = actOwnerList,  SelectedItemBinding = new Binding("Owner"),        Width = 110, EditingElementStyle = actOwnerStyle });
            actDg.Columns.Add(new DataGridTextColumn     { Header = "Due Date",    Binding = new Binding("DueDate"),                                              Width = 85 });
            actDg.Columns.Add(new DataGridComboBoxColumn { Header = "Priority",    ItemsSource = actPriorities, SelectedItemBinding = new Binding("Priority"),    Width = 80 });
            actDg.Columns.Add(new DataGridComboBoxColumn { Header = "Status",      ItemsSource = actStatuses,   SelectedItemBinding = new Binding("Status"),      Width = 110 });
            actDg.Columns.Add(new DataGridTextColumn     { Header = "Mtg Ref",     Binding = new Binding("MeetingRef"),                                           Width = 70 });
            actDg.ItemsSource = actSource;
            var actRowStyle = new Style(typeof(DataGridRow));
            var actOverdueT = new DataTrigger { Binding = new Binding("IsOverdue"), Value = true };
            actOverdueT.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Br(Color.FromRgb(0xFF, 0xEB, 0xEE))));
            actRowStyle.Triggers.Add(actOverdueT);
            var actInProgT = new DataTrigger { Binding = new Binding("Status"), Value = "IN PROGRESS" };
            actInProgT.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Br(Color.FromRgb(0xFF, 0xF8, 0xE1))));
            actRowStyle.Triggers.Add(actInProgT);
            actDg.RowStyle = actRowStyle;
            var actToolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            var addActBtn = new Button { Content = "＋ Add Action", Height = 26, Padding = new Thickness(10, 0, 10, 0), Background = Br(CGreen), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
            var delActBtn = new Button { Content = "✕ Delete", Height = 26, Padding = new Thickness(8, 0, 8, 0), Background = Br(CRed), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
            var bulkCloseBtn = new Button { Content = "Bulk Close", Height = 26, Padding = new Thickness(8, 0, 8, 0), Background = Br(CAccent), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
            var expActBtn = new Button { Content = "Export Excel", Height = 26, Padding = new Thickness(8, 0, 8, 0), Background = Br(CHeaderBg), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
            var overdueCheck = new CheckBox { Content = "Overdue Only", VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Margin = new Thickness(8, 0, 0, 0) };
            addActBtn.Click += (s, e) => { var n2 = actSource.Count + 1; actSource.Add(new ActionItemRow { ActionId = $"ACT-{n2:D3}", Status = "OPEN", Priority = "MEDIUM", DueDate = DateTime.Today.AddDays(14).ToString("yyyy-MM-dd") }); };
            delActBtn.Click += (s, e) => { if (actDg.SelectedItem is ActionItemRow ar) actSource.Remove(ar); };
            bulkCloseBtn.Click += (s, e) => { foreach (var sel in actDg.SelectedItems.OfType<ActionItemRow>().ToList()) sel.Status = "CLOSED"; actDg.Items.Refresh(); DispatchAction("BulkCloseActions"); };
            expActBtn.Click += (s, e) => ExportDataGridToXlsx(actDg, "ActionItems");
            overdueCheck.Checked += (s, e) => actDg.ItemsSource = actSource.Where(a => a.IsOverdue).ToList();
            overdueCheck.Unchecked += (s, e) => actDg.ItemsSource = actSource;
            actToolbar.Children.Add(addActBtn); actToolbar.Children.Add(delActBtn); actToolbar.Children.Add(bulkCloseBtn); actToolbar.Children.Add(expActBtn); actToolbar.Children.Add(overdueCheck);
            actItemsPanel.Children.Add(actToolbar); actItemsPanel.Children.Add(actDg);
            var sv6C = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = actItemsPanel };

            // ── Sub-tab D: Minutes Editor ──────────────────────────────────
            var minutesPanel = new StackPanel { Margin = new Thickness(12) };
            minutesPanel.Children.Add(MakeSectionHeader("SELECT MEETING"));
            var meetingSelCb = new System.Windows.Controls.ComboBox { Height = 28, FontSize = 11, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var mr in _data.Meetings) meetingSelCb.Items.Add($"{mr.MeetingId} — {mr.Title}  [{mr.Date}]");
            if (_data.Meetings.Count == 0) meetingSelCb.Items.Add("(No meetings yet — create one in 'Meetings Register')");
            meetingSelCb.SelectedIndex = 0;
            minutesPanel.Children.Add(meetingSelCb);
            minutesPanel.Children.Add(MakeSectionHeader("AGENDA / MINUTES"));
            var agendaDg = MakeExcelDataGrid(160);
            agendaDg.IsReadOnly = false;
            // Phase 98: Decision column is now a strict dropdown (matching real
            // meeting-minute practice: each agenda item gets one of a small set
            // of outcomes). Free text here tends to produce "agreed"/"AGREED"/
            // "Agreed ✓"/"ok" variants that confuse status reporting.
            var decisionList = new List<string>
            {
                "AGREED", "ACTIONED", "DEFERRED", "REJECTED", "NOTED", "PENDING INFO",
                "FOR REVIEW", "SUPERSEDED BY", "CARRIED FORWARD", "FOR APPROVAL"
            };
            agendaDg.Columns.Add(new DataGridTextColumn     { Header = "#",                  Binding = new Binding("ActionId"),                                             Width = 35 });
            agendaDg.Columns.Add(new DataGridTextColumn     { Header = "Topic",              Binding = new Binding("Description"),                                           Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            agendaDg.Columns.Add(new DataGridTextColumn     { Header = "Discussion / Notes", Binding = new Binding("Owner"),                                                 Width = 170 });
            agendaDg.Columns.Add(new DataGridComboBoxColumn { Header = "Decision",           ItemsSource = decisionList, SelectedItemBinding = new Binding("Status"),        Width = 140 });
            agendaDg.Columns.Add(new DataGridTextColumn     { Header = "Action Ref",         Binding = new Binding("MeetingRef"),                                            Width = 80 });
            agendaDg.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<ActionItemRow>();
            minutesPanel.Children.Add(agendaDg);
            minutesPanel.Children.Add(MakeSectionHeader("ATTENDEES PRESENT"));
            var attendeesWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var tm in _data.TeamMembers)
                attendeesWrap.Children.Add(new CheckBox { Content = $"{tm.Name} ({tm.Role})", FontSize = 11, Margin = new Thickness(0, 0, 14, 4), IsChecked = tm.Active });
            if (_data.TeamMembers.Count == 0)
                attendeesWrap.Children.Add(new TextBlock { Text = "(Configure attendees in 'Attendee Manager' tab)", FontSize = 11, Foreground = Brushes.Gray, FontStyle = FontStyles.Italic });
            minutesPanel.Children.Add(attendeesWrap);
            minutesPanel.Children.Add(MakeSectionHeader("NOTES & DECISIONS"));
            var notesBox = new System.Windows.Controls.TextBox { Height = 80, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 8), Background = Br(Color.FromRgb(0xFA, 0xFA, 0xFF)) };
            minutesPanel.Children.Add(notesBox);
            var minutesBtnRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            var autoMinBtn = new Button { Content = "🤖 Auto-generate Minutes", Height = 28, Padding = new Thickness(12, 0, 12, 0), Background = Br(CHeaderBg), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Generate draft minutes from meeting data, agenda items, and open issues" };
            var expWordMinBtn = new Button { Content = "📄 Export Word", Height = 28, Padding = new Thickness(12, 0, 12, 0), Background = Br(Color.FromRgb(0x15, 0x65, 0xC0)), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 0) };
            var expPdfMinBtn = new Button { Content = "🖨 Export PDF", Height = 28, Padding = new Thickness(12, 0, 12, 0), Background = Br(CAccent), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 0) };
            var expExlMinBtn = new Button { Content = "📊 Export Excel", Height = 28, Padding = new Thickness(12, 0, 12, 0), Background = Br(CGreen), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand };
            autoMinBtn.Click += (s, e) => DispatchAction("AutoMeetingMinutes");
            expWordMinBtn.Click += (s, e) => DispatchAction("ExportMinutesWord");
            expPdfMinBtn.Click += (s, e) => DispatchAction("ExportMinutesPDF");
            expExlMinBtn.Click += (s, e) => ExportDataGridToXlsx(agendaDg, "MeetingMinutes");
            minutesBtnRow.Children.Add(autoMinBtn); minutesBtnRow.Children.Add(expWordMinBtn); minutesBtnRow.Children.Add(expPdfMinBtn); minutesBtnRow.Children.Add(expExlMinBtn);
            minutesPanel.Children.Add(minutesBtnRow);
            var sv6D = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = minutesPanel };

            // ── Sub-tab E: Meeting Analytics ───────────────────────────────
            var analyticsPanel = new StackPanel { Margin = new Thickness(12) };
            analyticsPanel.Children.Add(MakeSectionHeader("MEETING ANALYTICS"));
            var anaKpi = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 12) };
            int avgActions = totalMtg > 0 ? _data.ActionItems.Count / Math.Max(1, totalMtg) : 0;
            int closedActions = _data.ActionItems.Count(a => a.Status == "CLOSED");
            int totalActions = _data.ActionItems.Count;
            int completionPct = totalActions > 0 ? closedActions * 100 / totalActions : 100;
            anaKpi.Children.Add(MakeKPICard("TOTAL MEETINGS", totalMtg.ToString(), Br(CHeaderBg), "All meetings recorded"));
            anaKpi.Children.Add(MakeKPICard("AVG ACTIONS/MTG", avgActions.ToString(), Br(CAccent), "Average action items generated per meeting"));
            anaKpi.Children.Add(MakeKPICard("ACTION COMPLETION", $"{completionPct}%", completionPct >= 80 ? Br(CGreen) : completionPct >= 50 ? Br(CAmber) : Br(CRed), $"Closed: {closedActions}/{totalActions} action items"));
            anaKpi.Children.Add(MakeKPICard("OVERDUE ACTIONS", openActionsMtg.ToString(), openActionsMtg > 0 ? Br(CRed) : Br(CGreen), "Action items not yet closed"));
            analyticsPanel.Children.Add(anaKpi);

            // Meeting type distribution
            analyticsPanel.Children.Add(MakeSectionHeader("MEETINGS BY TYPE"));
            var typeGroups = mtgSource.GroupBy(m => m.Type ?? "Unknown").OrderByDescending(g => g.Count()).ToList();
            var typeCard = MakeCard();
            var typeStack = new StackPanel();
            int maxTypeC = typeGroups.Count > 0 ? typeGroups[0].Count() : 1;
            foreach (var tg in typeGroups)
            {
                var tRow = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                tRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                tRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                tRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var lbl = new TextBlock { Text = tg.Key, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                var cnt = new TextBlock { Text = tg.Count().ToString(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Br(CAccent), VerticalAlignment = VerticalAlignment.Center };
                var barBg = new Border { Height = 12, CornerRadius = new CornerRadius(3), Background = Br(Color.FromRgb(0xE0, 0xE0, 0xE0)) };
                var barFill = new Border { Height = 12, CornerRadius = new CornerRadius(3), Background = Br(CHeaderBg), HorizontalAlignment = HorizontalAlignment.Left };
                barBg.Child = barFill;
                int captured = tg.Count(); int capturedMax = maxTypeC;
                barBg.Loaded += (s, e) => barFill.Width = Math.Max(4, barBg.ActualWidth * captured / Math.Max(1, capturedMax));
                Grid.SetColumn(cnt, 1); Grid.SetColumn(barBg, 2);
                tRow.Children.Add(lbl); tRow.Children.Add(cnt); tRow.Children.Add(barBg);
                typeStack.Children.Add(tRow);
            }
            if (typeGroups.Count == 0) typeStack.Children.Add(new TextBlock { Text = "No meetings recorded yet.", FontSize = 11, Foreground = Brushes.Gray, FontStyle = FontStyles.Italic });
            typeCard.Child = typeStack;
            analyticsPanel.Children.Add(typeCard);

            var expAnaBtn = new Button { Content = "Export Analytics Excel", Height = 26, Padding = new Thickness(10, 0, 10, 0), Background = Br(CHeaderBg), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            expAnaBtn.Click += (s, e) => DispatchAction("ExportMeetingAnalytics");
            analyticsPanel.Children.Add(expAnaBtn);
            var sv6E = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = analyticsPanel };

            // ── Sub-tab F: Automation ──────────────────────────────────────
            var autoPanel = new StackPanel { Margin = new Thickness(12) };
            autoPanel.Children.Add(MakeSectionHeader("MEETING AUTOMATION RULES"));
            var autoRules = new[]
            {
                ("Auto-Number New Meetings",          "New meetings are automatically numbered MTG-001, MTG-002… on creation. No manual input required.", "AutoNumberMeetings", true),
                ("Overdue Action → Issue Escalation", "Auto-create HIGH-priority NCR issues from overdue meeting actions with element linking.", "EscalateOverdueActions", false),
                ("Open Issues → Next Meeting Agenda", "Auto-generate agenda from open issues grouped by type and priority before each BIM Coordination meeting.", "AutoAgenda", false),
                ("Compliance Gate → Transmittal",     "Auto-create SHARED transmittal when compliance ≥80%, containers ≥80%, and 0 critical warnings.", "ComplianceGateTransmittal", false),
                ("Meeting Closure → Follow-up",       "Auto-schedule follow-up meeting with open actions and unresolved issues when meeting is marked COMPLETED.", "ScheduleMeetingFollowUp", false),
                ("Attendee RSVP Tracking",            "Track RSVP responses and send reminders 24h before meeting to non-responders.", "MeetingRSVP", false),
                ("Auto-generate Minutes from Model",  "After meeting, extract model health, warning counts, tag compliance, and issue stats into meeting minutes template.", "AutoMeetingMinutes", false),
            };
            foreach (var (ruleTitle, ruleDesc, ruleAction, defaultOn) in autoRules)
            {
                string capturedAction = ruleAction;
                var ruleCard = new Border { Background = Br(CCardBg), BorderBrush = Br(CBorder), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8) };
                var ruleStack = new StackPanel();
                var ruleTitleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                ruleTitleRow.Children.Add(new TextBlock { Text = ruleTitle, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Br(CHeaderBg), VerticalAlignment = VerticalAlignment.Center });
                var ruleTog = new CheckBox { Content = "Enabled", FontSize = 11, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsChecked = defaultOn };
                ruleTitleRow.Children.Add(ruleTog);
                ruleStack.Children.Add(ruleTitleRow);
                ruleStack.Children.Add(new TextBlock { Text = ruleDesc, FontSize = 11, Foreground = Br(Color.FromRgb(0x55, 0x55, 0x55)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
                var ruleResultBlock = new TextBlock { FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 0) };
                var runNowBtn = new Button { Content = "▶ Run Now", Height = 26, Padding = new Thickness(10, 0, 10, 0), Background = Br(CAccent), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
                runNowBtn.Click += (s, e) => { DispatchAction(capturedAction); ruleResultBlock.Text = $"Running {capturedAction}…"; };
                ruleStack.Children.Add(runNowBtn); ruleStack.Children.Add(ruleResultBlock);
                ruleCard.Child = ruleStack;
                autoPanel.Children.Add(ruleCard);
            }
            var sv6F = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = autoPanel };

            // Build sub-tabs
            outerTabs.Items.Add(new TabItem { Header = "Meetings Register", Content = sv6A });
            outerTabs.Items.Add(new TabItem { Header = "Attendee Manager",  Content = sv6B });
            outerTabs.Items.Add(new TabItem { Header = "Action Items",      Content = sv6C });
            outerTabs.Items.Add(new TabItem { Header = "Minutes Editor",    Content = sv6D });
            outerTabs.Items.Add(new TabItem { Header = "Analytics",         Content = sv6E });
            outerTabs.Items.Add(new TabItem { Header = "Automation",        Content = sv6F });

            return outerTabs;
        }

        private UIElement BuildMeetingsTab_Legacy_UNUSED()
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

        /// <summary>Phase 77 Item 10: Handle project members actions dispatched from StingCommandHandler.</summary>
        public void HandleProjectMembersAction(string action)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var doc = StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document;
                    switch (action)
                    {
                        case "SaveProjectMembers":
                        {
                            if (doc != null)
                            {
                                string path = BIMManager.BIMManagerEngine.GetBIMManagerFilePath(doc, "team_members.json");
                                var arr = Newtonsoft.Json.Linq.JArray.FromObject(_data.TeamMembers);
                                System.IO.File.WriteAllText(path, arr.ToString(Newtonsoft.Json.Formatting.Indented));
                                ShowStatus($"Saved {_data.TeamMembers.Count} team members.");
                            }
                            else ShowStatus("No active document — cannot save team members.");
                            break;
                        }
                        case "AddTeamMember":
                        {
                            _data.TeamMembers.Add(new TeamMemberRow { Active = true, Name = "New Member", Role = "Team" });
                            if (_tabCache.ContainsKey(TabProjectMembers)) _tabCache.Remove(TabProjectMembers);
                            NavigateTo(TabProjectMembers);
                            ShowStatus("New team member added.");
                            break;
                        }
                        case "RemoveMember":
                        case "RemoveTeamMember":
                        {
                            string memberName = StingCommandHandler.GetExtraParam("MemberName");
                            if (!string.IsNullOrEmpty(memberName))
                            {
                                int removed = _data.TeamMembers.RemoveAll(m => m.Name == memberName);
                                if (_tabCache.ContainsKey(TabProjectMembers)) _tabCache.Remove(TabProjectMembers);
                                NavigateTo(TabProjectMembers);
                                ShowStatus(removed > 0 ? $"Removed member: {memberName}" : $"Member not found: {memberName}");
                            }
                            else ShowStatus("No MemberName param set.");
                            break;
                        }
                        default:
                            ShowStatus($"Action: {action}");
                            break;
                    }
                }
                catch (Exception ex) { ShowStatus($"Error: {ex.Message}"); }
            });
        }

        /// <summary>Phase 77 Item 11B: Show inline status message with 3-second auto-reset.</summary>
        private void ShowStatus(string message)
        {
            _statusBar.Text = message;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, e) => { _statusBar.Text = "Ready"; timer.Stop(); };
            timer.Start();
        }

        /// <summary>Phase 91: Auto-populate issue location from element tokens and room membership.</summary>
        internal static string AutoPopulateIssueLocation(Autodesk.Revit.DB.Document doc, IEnumerable<Autodesk.Revit.DB.ElementId> elementIds)
        {
            if (doc == null) return "";
            foreach (var id in elementIds ?? Enumerable.Empty<Autodesk.Revit.DB.ElementId>())
            {
                var el = doc.GetElement(id);
                if (el == null) continue;
                string lvl  = Core.ParameterHelpers.GetString(el, Core.ParamRegistry.LVL)  ?? "";
                string zone = Core.ParameterHelpers.GetString(el, Core.ParamRegistry.ZONE) ?? "";
                string loc  = Core.ParameterHelpers.GetString(el, Core.ParamRegistry.LOC)  ?? "";
                if (string.IsNullOrEmpty(lvl) || lvl == "XX")
                    lvl = Core.ParameterHelpers.GetLevelCode(doc, el) ?? "";
                string room = "";
                try
                {
                    if (el is Autodesk.Revit.DB.FamilyInstance fi)
                    {
                        Autodesk.Revit.DB.Phase phase = null;
                        try { phase = doc.GetElement(el.CreatedPhaseId) as Autodesk.Revit.DB.Phase; } catch { }
                        var r = phase != null ? fi.get_Room(phase) : fi.Room;
                        if (r != null) room = r.Name;
                    }
                }
                catch { }
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(lvl)  && lvl  != "XX") parts.Add(lvl);
                if (!string.IsNullOrEmpty(zone) && zone != "XX" && zone != "ZZ") parts.Add(zone);
                if (!string.IsNullOrEmpty(loc)  && loc  != "XX") parts.Add(loc);
                if (!string.IsNullOrEmpty(room)) parts.Add(room);
                string result = string.Join(" / ", parts);
                if (!string.IsNullOrEmpty(result)) return result;
            }
            return "";
        }

        /// <summary>Returns the names of all loaded team members for external use (e.g., IssueTrackerDashboard).</summary>
        public List<string> GetTeamMemberNames()
            => _data?.TeamMembers?.Select(m => m.Name).Where(n => !string.IsNullOrEmpty(n)).ToList()
               ?? new List<string>();

        /// <summary>Phase 101: dispatch action to rebuild CoordData on the Revit API
        /// thread and refresh the whole BCC surface (replaces close + reopen).
        /// Runs through the ActionDispatcher / ExternalEvent pipeline so the data
        /// rebuild (FilteredElementCollector work) happens on the Revit API
        /// thread; <see cref="ApplyReloadedData"/> is then called on the WPF
        /// thread to swap <see cref="_data"/> and re-render.</summary>
        public void ReloadAll()
        {
            try
            {
                // Show a brief visual cue so the user sees something happened.
                if (_statusBar != null) _statusBar.Text = "Refreshing…";
                ActionDispatcher?.Invoke("BCCReload");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BCC.ReloadAll: {ex.Message}");
                if (_statusBar != null) _statusBar.Text = $"Refresh failed: {ex.Message}";
            }
        }

        /// <summary>Phase 101: invoked from the Revit API thread (via BCCReload
        /// action) after CoordData has been rebuilt. Swaps the data, clears the
        /// tab cache, refreshes badges, and re-renders the current tab.</summary>
        public void ApplyReloadedData(CoordData fresh)
        {
            if (fresh == null) return;
            Dispatcher.Invoke(() =>
            {
                try
                {
                    _data = fresh;
                    _tabCache.Clear();
                    _4dPanelArea?.SetCurrentValue(ContentControl.ContentProperty, null);
                    _revPanelArea?.SetCurrentValue(ContentControl.ContentProperty, null);
                    _workflowPanelArea?.SetCurrentValue(ContentControl.ContentProperty, null);
                    _modelHealthActionArea?.SetCurrentValue(ContentControl.ContentProperty, null);
                    _issueContextArea?.SetCurrentValue(ContentControl.ContentProperty, null);
                    RefreshBadges();
                    if (!string.IsNullOrEmpty(_currentTab)) NavigateTo(_currentTab);
                    if (_statusBar != null) _statusBar.Text = $"Refreshed \u2713  {DateTime.Now:HH:mm:ss}";
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"BCC.ApplyReloadedData: {ex.Message}");
                    if (_statusBar != null) _statusBar.Text = $"Refresh failed: {ex.Message}";
                }
            });
        }

        /// <summary>Phase 77 Item 11C: Refresh nav panel badge counts from live data.</summary>
        public void RefreshBadges()
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var child in _navPanel.Children.OfType<Button>())
                {
                    var tag = child.Tag as string;
                    if (tag == null) continue;
                    string badge = tag switch
                    {
                        "WARNINGS"  => _data.WarningTotal > 0 ? _data.WarningTotal.ToString() : "",
                        "ISSUES"    => _data.IssuesOpen > 0 ? _data.IssuesOpen.ToString() : "",
                        "REVISIONS" => _data.RevisionCount.ToString(),
                        _ => ""
                    };
                    if (child.Content is StackPanel sp)
                    {
                        var badgeBlock = sp.Children.OfType<Border>().FirstOrDefault();
                        if (badgeBlock?.Child is TextBlock tb && !string.IsNullOrEmpty(badge))
                            tb.Text = badge;
                    }
                }
            });
        }

        /// <summary>
        /// Show the BIM Coordination Center as a modeless window.
        /// Actions are dispatched via ActionDispatcher (ExternalEvent).
        /// Phase 104: now anchors BCC to Revit's main HWND via WindowInteropHelper
        /// BEFORE Show() so BCC stays z-ordered above Revit. Without this anchor,
        /// switching focus to any child dialog let Revit come to the front and
        /// pushed BCC behind — the user reported "BCC gets behind Revit when its
        /// child UI is activated".
        /// </summary>
        internal static void Show(CoordData data)
        {
            if (CurrentInstance != null)
            {
                CurrentInstance.Dispatcher.Invoke(() =>
                {
                    CurrentInstance.Topmost = true;
                    CurrentInstance.Activate();
                    CurrentInstance.Focus();
                    CurrentInstance.Topmost = false;
                });
                return;
            }
            var dlg = new BIMCoordinationCenter(data);

            // Phase 104: anchor to Revit main window BEFORE first Show() — this is the
            // only time WindowInteropHelper.Owner can be assigned. Sets the native
            // owner so BCC and Revit stay in the same z-group: clicking BCC cannot
            // push Revit behind, clicking Revit cannot push BCC behind.
            try
            {
                IntPtr revitHwnd = NativeMethods.FindWindow("Rvt_MainWindow", null);
                if (revitHwnd == IntPtr.Zero)
                    revitHwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (revitHwnd != IntPtr.Zero)
                    new System.Windows.Interop.WindowInteropHelper(dlg).Owner = revitHwnd;
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"BCC owner anchor: {ex.Message}"); }

            dlg.Show();
            // Bring to front after Revit re-activates (common WPF-in-Revit issue)
            dlg.Dispatcher.BeginInvoke(new Action(() =>
            {
                dlg.Topmost = true;
                dlg.Activate();
                dlg.Focus();
                dlg.Topmost = false;
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
    }

    /// <summary>P/Invoke helpers for Revit window detection.</summary>
    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        internal static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
    }

    /// <summary>Phase 98: Centralised window-owner helper.
    /// <para>
    /// Problem: modeless WPF windows opened from Revit commands default to no owner,
    /// which in Revit produces two bugs — child dialogs open BEHIND the BCC when the
    /// BCC is visible, and when the child is clicked the BCC disappears behind the
    /// Revit main window (because its only Z-parent was the Revit HWND).
    /// </para>
    /// <para>
    /// Fix: call <see cref="ApplyOwner"/> on any WPF Window before <c>Show()</c> or
    /// <c>ShowDialog()</c>. It prefers the BCC (when visible) so child windows stack
    /// above it; otherwise it sets the Revit main window HWND via
    /// <see cref="System.Windows.Interop.WindowInteropHelper"/> so the window still
    /// behaves like a proper Revit child.
    /// </para>
    /// </summary>
    public static class StingWindowHelper
    {
        // Phase 104: global class-handler that auto-owns any WPF Window created from
        // inside Revit. Uses the routed Loaded event (fires after Show but while the
        // HWND exists) — at that point WindowInteropHelper.Owner can still update the
        // native owner via SetWindowLongPtr(GWLP_HWNDPARENT), which is sufficient to
        // fix the z-order. Registered once at plugin load by StingToolsApp.
        // Without this, every command that opens a dialog without explicitly calling
        // ApplyOwner drops behind BCC. With this hook, dialogs correctly stack above
        // BCC and BCC stays above Revit via the anchor set in BIMCoordinationCenter.Show.
        private static bool _globalHandlerInstalled;
        public static void InstallGlobalOwnerHandler()
        {
            if (_globalHandlerInstalled) return;
            _globalHandlerInstalled = true;
            try
            {
                EventManager.RegisterClassHandler(typeof(System.Windows.Window),
                    System.Windows.FrameworkElement.LoadedEvent,
                    new RoutedEventHandler(OnAnyWindowLoaded));
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"InstallGlobalOwnerHandler: {ex.Message}"); }
        }
        private static void OnAnyWindowLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is System.Windows.Window w)) return;
                // Skip BCC itself — its owner is set explicitly in Show().
                if (w is BIMCoordinationCenter) return;
                // Only apply if caller hasn't already set an Owner (WPF or native HWND).
                if (w.Owner != null) return;
                var helper = new System.Windows.Interop.WindowInteropHelper(w);
                if (helper.Owner != IntPtr.Zero) return;
                // Prefer BCC HWND as native owner when available, so the child stacks
                // above BCC. Fall back to Revit HWND.
                IntPtr parentHwnd = IntPtr.Zero;
                var bcc = BIMCoordinationCenter.CurrentInstance;
                if (bcc != null && bcc.IsLoaded)
                    parentHwnd = new System.Windows.Interop.WindowInteropHelper(bcc).Handle;
                if (parentHwnd == IntPtr.Zero)
                {
                    var revitHwnd = NativeMethods.FindWindow("Rvt_MainWindow", null);
                    parentHwnd = revitHwnd != IntPtr.Zero ? revitHwnd
                        : System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                }
                if (parentHwnd != IntPtr.Zero) helper.Owner = parentHwnd;
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"OnAnyWindowLoaded: {ex.Message}"); }
        }

        /// <summary>Set a sensible owner on <paramref name="w"/> so it always stacks
        /// above the BCC / Revit main window. Call BEFORE <c>Show()</c> or
        /// <c>ShowDialog()</c> — WPF doesn't let you change Owner after.</summary>
        public static void ApplyOwner(System.Windows.Window w)
        {
            if (w == null) return;
            try
            {
                // Phase 104 rewrite: use HWND-level owner (via WindowInterop-
                // Helper) in BOTH cases. The old code set w.Owner=bcc (WPF
                // property) which in Revit-hosted WPF doesn't translate to a
                // real HWND owner, so the child was a top-level sibling of
                // BCC and sometimes ended up BEHIND it. Setting the HWND owner
                // explicitly makes the child a Windows-managed owned popup —
                // it's guaranteed to stack above its owner (BCC) while still
                // respecting Revit as the top-of-chain.
                var bcc = BIMCoordinationCenter.CurrentInstance;
                IntPtr ownerHwnd = IntPtr.Zero;
                if (bcc != null && bcc != w && bcc.IsLoaded)
                {
                    try { ownerHwnd = new System.Windows.Interop.WindowInteropHelper(bcc).Handle; }
                    catch { ownerHwnd = IntPtr.Zero; }
                }
                if (ownerHwnd == IntPtr.Zero)
                {
                    var revitHwnd = NativeMethods.FindWindow("Rvt_MainWindow", null);
                    ownerHwnd = revitHwnd != IntPtr.Zero
                        ? revitHwnd
                        : System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                }
                if (ownerHwnd != IntPtr.Zero)
                {
                    // Set the HWND owner. Must be assigned BEFORE the window
                    // is shown (before SourceInitialized). WPF falls through
                    // to this same mechanism when you set w.Owner, but going
                    // direct guarantees we hit the HWND chain in Revit hosts.
                    new System.Windows.Interop.WindowInteropHelper(w).Owner = ownerHwnd;

                    // Also set WPF Owner when the owner IS a managed WPF
                    // Window (gives us input routing + ShowDialog centering).
                    if (bcc != null && bcc != w && bcc.IsLoaded)
                    {
                        try { w.Owner = bcc; } catch { /* WPF refuses after Show */ }
                    }
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"StingWindowHelper.ApplyOwner: {ex.Message}");
            }
        }

        /// <summary>Convenience wrapper — apply owner then call <c>ShowDialog()</c>.
        /// Returns the dialog result.</summary>
        public static bool? ShowDialogOwned(System.Windows.Window w)
        {
            ApplyOwner(w);
            return w.ShowDialog();
        }

        /// <summary>Convenience wrapper — apply owner then call <c>Show()</c>.</summary>
        public static void ShowOwned(System.Windows.Window w)
        {
            ApplyOwner(w);
            w.Show();
        }
    }
}
