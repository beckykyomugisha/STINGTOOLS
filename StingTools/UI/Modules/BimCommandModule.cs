// INT-02: Command registry module for all BIM tab button tags.
using StingTools.UI;

namespace StingTools.UI.Modules
{
    internal sealed class BimCommandModule : ICommandModule
    {
        public void Register(CommandRegistry registry)
        {
            // ── BEP ─────────────────────────────────────────────────────────
            registry.Register("CreateBEP",                 app => StingCommandHandler.RunCommandPublic<BIMManager.CreateBEPCommand>(app));
            registry.Register("GenerateBEP",               app => StingCommandHandler.RunCommandPublic<BIMManager.GenerateBEPCommand>(app));
            registry.Register("UpdateBEP",                 app => StingCommandHandler.RunCommandPublic<BIMManager.UpdateBEPCommand>(app));
            registry.Register("ExportBEP",                 app => StingCommandHandler.RunCommandPublic<BIMManager.ExportBEPCommand>(app));

            // ── CDE / Documents ─────────────────────────────────────────────
            registry.Register("CDEStatus",                 app => StingCommandHandler.RunCommandPublic<BIMManager.CDEStatusCommand>(app));
            registry.Register("ValidateDocNaming",         app => StingCommandHandler.RunCommandPublic<BIMManager.ValidateDocNamingCommand>(app));
            registry.Register("DocumentRegister",          app => StingCommandHandler.RunCommandPublic<BIMManager.DocumentRegisterCommand>(app));
            registry.Register("AddDocument",               app => StingCommandHandler.RunCommandPublic<BIMManager.AddDocumentCommand>(app));
            registry.Register("CreateTransmittal",         app => StingCommandHandler.RunCommandPublic<BIMManager.CreateTransmittalCommand>(app));
            registry.Register("ReviewTracker",             app => StingCommandHandler.RunCommandPublic<BIMManager.ReviewTrackerCommand>(app));
            registry.Register("BIMDashboard",              app => StingCommandHandler.RunCommandPublic<BIMManager.ProjectDashboardCommand>(app));

            // ── Issues ──────────────────────────────────────────────────────
            registry.Register("RaiseIssue",                app => StingCommandHandler.RunCommandPublic<BIMManager.RaiseIssueCommand>(app));
            registry.Register("IssueDashboard",            app => StingCommandHandler.RunCommandPublic<BIMManager.IssueDashboardCommand>(app));
            registry.Register("UpdateIssue",               app => StingCommandHandler.RunCommandPublic<BIMManager.UpdateIssueCommand>(app));
            registry.Register("SelectIssueElements",       app => StingCommandHandler.RunCommandPublic<BIMManager.SelectIssueElementsCommand>(app));
            registry.Register("IssuesBulkClose",           app => StingCommandHandler.RunCommandPublic<BIMManager.BulkCloseIssuesCommand>(app));
            registry.Register("IssueFilter",               app => StingCommandHandler.RunCommandPublic<BIMManager.IssueFilterCommand>(app));
            registry.Register("IssueTimeline",             app => StingCommandHandler.RunCommandPublic<BIMManager.IssueTimelineCommand>(app));
            registry.Register("IssueStatistics",           app => StingCommandHandler.RunCommandPublic<BIMManager.IssueStatisticsCommand>(app));
            registry.Register("IssueBatchUpdate",          app => StingCommandHandler.RunCommandPublic<BIMManager.IssueBatchUpdateCommand>(app));
            registry.Register("CreateIssuesFromWarnings",  app => StingCommandHandler.RunCommandPublic<BIMManager.RaiseIssueCommand>(app));

            // ── COBie ────────────────────────────────────────────────────────
            registry.Register("COBieExport",               app => StingCommandHandler.RunCommandPublic<BIMManager.COBieExportCommand>(app));
            registry.Register("COBieImport",               app => StingCommandHandler.RunCommandPublic<BIMManager.COBieImportCommand>(app));
            registry.Register("COBieExtendedImport",       app => StingCommandHandler.RunCommandPublic<BIMManager.COBieExtendedImportCommand>(app));

            // ── ISO 19650 / Compliance ───────────────────────────────────────
            registry.Register("ISO19650Reference",         app => StingCommandHandler.RunCommandPublic<BIMManager.ISO19650ReferenceCommand>(app));
            registry.Register("BulkBIMExport",             app => StingCommandHandler.RunCommandPublic<BIMManager.BulkBIMExportCommand>(app));
            registry.Register("ExportDashboardHTML",       app => StingCommandHandler.RunCommandPublic<BIMManager.ExportDashboardHTMLCommand>(app));
            registry.Register("BEPStageValidation",        app => StingCommandHandler.RunCommandPublic<BIMManager.BEPStageValidationCommand>(app));
            registry.Register("IssueRevisionLink",         app => StingCommandHandler.RunCommandPublic<BIMManager.IssueRevisionLinkCommand>(app));
            registry.Register("TagRevisionDiff",           app => StingCommandHandler.RunCommandPublic<BIMManager.TagRevisionDiffCommand>(app));
            registry.Register("AutoMeetingMinutes",        app => StingCommandHandler.RunCommandPublic<BIMManager.AutoMeetingMinutesCommand>(app));
            registry.Register("AutoScheduleMeetings",      app => StingCommandHandler.RunCommandPublic<BIMManager.AutoScheduleMeetingsCommand>(app));
            registry.Register("WeeklyReport",              app => StingCommandHandler.RunCommandPublic<BIMManager.WeeklyCoordinatorReportCommand>(app));

            // ── Briefcase ────────────────────────────────────────────────────
            registry.Register("BriefcaseView",             app => StingCommandHandler.RunCommandPublic<BIMManager.BriefcaseViewCommand>(app));
            registry.Register("BriefcaseRead",             app => StingCommandHandler.RunCommandPublic<BIMManager.BriefcaseReadCommand>(app));
            registry.Register("BriefcaseAddFile",          app => StingCommandHandler.RunCommandPublic<BIMManager.BriefcaseAddFileCommand>(app));

            // ── Scheduling 4D/5D ─────────────────────────────────────────────
            registry.Register("AutoSchedule4D",            app => StingCommandHandler.RunCommandPublic<BIMManager.AutoSchedule4DCommand>(app));
            registry.Register("ImportMSProject",           app => StingCommandHandler.RunCommandPublic<BIMManager.ImportMSProjectCommand>(app));
            registry.Register("ViewTimeline4D",            app => StingCommandHandler.RunCommandPublic<BIMManager.ViewTimeline4DCommand>(app));
            registry.Register("ExportSchedule4D",         app => StingCommandHandler.RunCommandPublic<BIMManager.ExportSchedule4DCommand>(app));
            registry.Register("AutoCost5D",                app => StingCommandHandler.RunCommandPublic<BIMManager.AutoCost5DCommand>(app));
            registry.Register("ImportCostRates",           app => StingCommandHandler.RunCommandPublic<BIMManager.ImportCostRatesCommand>(app));
            registry.Register("CostReport5D",              app => StingCommandHandler.RunCommandPublic<BIMManager.CostReport5DCommand>(app));
            registry.Register("CashFlow5D",                app => StingCommandHandler.RunCommandPublic<BIMManager.CashFlow5DCommand>(app));
            registry.Register("NavisworksTimeLiner",       app => StingCommandHandler.RunCommandPublic<BIMManager.NavisworksTimeLinerExportCommand>(app));
            registry.Register("ElementCostTrace",          app => StingCommandHandler.RunCommandPublic<BIMManager.ElementCostTraceCommand>(app));
            registry.Register("ExportCashFlow",            app => StingCommandHandler.RunCommandPublic<BIMManager.CashFlow5DCommand>(app));
            registry.Register("SaveWorkingCalendar",       app => StingCommandHandler.RunCommandPublic<BIMManager.WorkingCalendarCommand>(app));
            registry.Register("ExportMilestones",          app => StingCommandHandler.RunCommandPublic<BIMManager.MilestoneRegisterCommand>(app));

            // ── Sticky Notes ─────────────────────────────────────────────────
            registry.Register("StickyNote",                app => StingCommandHandler.RunCommandPublic<BIMManager.ElementStickyNoteCommand>(app));
            registry.Register("ExportStickyNotes",         app => StingCommandHandler.RunCommandPublic<BIMManager.ExportStickyNotesCommand>(app));
            registry.Register("SelectStickyElements",      app => StingCommandHandler.RunCommandPublic<BIMManager.SelectStickyElementsCommand>(app));
            registry.Register("StickyCategories",          app => StingCommandHandler.RunCommandPublic<BIMManager.StickyNoteCategoriesCommand>(app));
            registry.Register("StickyDashboardBIM",        app => StingCommandHandler.RunCommandPublic<BIMManager.StickyNoteDashboardCommand>(app));
            registry.Register("StickySearch",              app => StingCommandHandler.RunCommandPublic<BIMManager.StickyNoteSearchCommand>(app));

            // ── Warnings / QA ────────────────────────────────────────────────
            registry.Register("WarningReview",             app => StingCommandHandler.RunCommandPublic<BIMManager.WarningReviewCommand>(app));
            registry.Register("WarningExport",             app => StingCommandHandler.RunCommandPublic<BIMManager.WarningExportCommand>(app));
            registry.Register("RunCustomRules",            app => StingCommandHandler.RunCommandPublic<BIMManager.RunCustomRulesCommand>(app));
            registry.Register("ModelHealthScan",           app => StingCommandHandler.RunCommandPublic<BIMManager.ModelHealthScanCommand>(app));
            registry.Register("ModelHealthExportJson",     app => StingCommandHandler.RunCommandPublic<BIMManager.ModelHealthExportJsonCommand>(app));
            registry.Register("QAReport",                  app => StingCommandHandler.RunCommandPublic<BIMManager.QAReportCommand>(app));
            registry.Register("SetupValidation",           app => StingCommandHandler.RunCommandPublic<BIMManager.SetupValidationCommand>(app));

            // ── Worksets ─────────────────────────────────────────────────────
            registry.Register("WorksetAudit",              app => StingCommandHandler.RunCommandPublic<BIMManager.WorksetAuditCommand>(app));
            registry.Register("WorksetAuditExport",        app => StingCommandHandler.RunCommandPublic<BIMManager.WorksetAuditExportCommand>(app));
            registry.Register("CreateStandardWorksets",    app => StingCommandHandler.RunCommandPublic<BIMManager.CreateStandardWorksetsCommand>(app));

            // ── Links ────────────────────────────────────────────────────────
            registry.Register("LinkAudit",                 app => StingCommandHandler.RunCommandPublic<BIMManager.LinkAuditCommand>(app));
            registry.Register("LinkAuditExport",           app => StingCommandHandler.RunCommandPublic<BIMManager.LinkAuditExportCommand>(app));
            registry.Register("LinkStats",                 app => StingCommandHandler.RunCommandPublic<BIMManager.LinkStatsCommand>(app));

            // ── Carbon ───────────────────────────────────────────────────────
            registry.Register("CarbonCalculator",          app => StingCommandHandler.RunCommandPublic<BIMManager.CarbonCalculatorCommand>(app));
            registry.Register("CarbonExport",              app => StingCommandHandler.RunCommandPublic<BIMManager.CarbonExportCommand>(app));

            // ── Snapshots ────────────────────────────────────────────────────
            registry.Register("TakeSnapshot",              app => StingCommandHandler.RunCommandPublic<BIMManager.TakeSnapshotCommand>(app));
            registry.Register("CompareSnapshot",           app => StingCommandHandler.RunCommandPublic<BIMManager.CompareSnapshotCommand>(app));
            registry.Register("SnapshotDiffExport",        app => StingCommandHandler.RunCommandPublic<BIMManager.SnapshotDiffExportCommand>(app));

            // ── LAN Collaboration ────────────────────────────────────────────
            registry.Register("LANEnableWorksharing",      app => StingCommandHandler.RunCommandPublic<BIMManager.LANEnableWorksharingCommand>(app));
            registry.Register("LANSyncToCentral",          app => StingCommandHandler.RunCommandPublic<BIMManager.LANSyncToCentralCommand>(app));
            registry.Register("LANBackup",                 app => StingCommandHandler.RunCommandPublic<BIMManager.LANBackupCommand>(app));
            registry.Register("LANTeamDashboard",          app => StingCommandHandler.RunCommandPublic<BIMManager.LANTeamDashboardCommand>(app));
            registry.Register("LANChangeLog",              app => StingCommandHandler.RunCommandPublic<BIMManager.LANChangeLogCommand>(app));
            registry.Register("LANAutoSyncToggle",         app => StingCommandHandler.RunCommandPublic<BIMManager.LANAutoSyncToggleCommand>(app));
            registry.Register("SpeckleReceive",            app => StingCommandHandler.RunCommandPublic<BIMManager.SpeckleReceiveCommand>(app));

            // ── Coordination Center ──────────────────────────────────────────
            registry.Register("CoordinationCenter",        app => StingCommandHandler.RunCommandPublic<BIMManager.CoordinationCenterCommand>(app));
            registry.Register("GenerateDashboard",         app => StingCommandHandler.RunCommandPublic<BIMManager.GenerateDashboardCommand>(app));
            registry.Register("ToggleFileMonitor",         app => StingCommandHandler.RunCommandPublic<BIMManager.ToggleFileMonitorCommand>(app));
            registry.Register("BroadcastNotification",     app => StingCommandHandler.RunCommandPublic<BIMManager.BroadcastNotificationCommand>(app));
            registry.Register("AccessControl",             app => StingCommandHandler.RunCommandPublic<BIMManager.AccessControlCommand>(app));
            registry.Register("StageGate",                 app => StingCommandHandler.RunCommandPublic<BIMManager.StageComplianceGateCommand>(app));
            registry.Register("ViewPlatformLogs",          app => StingCommandHandler.RunCommandPublic<BIMManager.ExportCoordLogCommand>(app));

            // ── Gap fixes ────────────────────────────────────────────────────
            registry.Register("CDEApprovalWorkflow",       app => StingCommandHandler.RunCommandPublic<BIMManager.CDEApprovalWorkflowCommand>(app));
            registry.Register("CrossSystemLink",           app => StingCommandHandler.RunCommandPublic<BIMManager.CrossSystemLinkCommand>(app));
            registry.Register("RefreshCoordinationData",   app => StingCommandHandler.RunCommandPublic<BIMManager.RefreshCoordinationDataCommand>(app));
            registry.Register("StreamingCOBieImportValidated", app => StingCommandHandler.RunCommandPublic<BIMManager.StreamingCOBieImportCommand>(app));
            registry.Register("Schedule4DHandover",        app => StingCommandHandler.RunCommandPublic<BIMManager.Schedule4DHandoverCommand>(app));
            registry.Register("COBieSystemGroupFix",       app => StingCommandHandler.RunCommandPublic<BIMManager.COBieSystemGroupFixCommand>(app));
            registry.Register("DataDropTracker",           app => StingCommandHandler.RunCommandPublic<BIMManager.DataDropTrackerCommand>(app));
            registry.Register("CDEFolderStructure",        app => StingCommandHandler.RunCommandPublic<BIMManager.CDEFolderStructureCommand>(app));
            registry.Register("ComplianceForecast",        app => StingCommandHandler.RunCommandPublic<BIMManager.ComplianceForecastCommand>(app));

            // ── Excel Link ───────────────────────────────────────────────────
            registry.Register("ExportToExcel",             app => StingCommandHandler.RunCommandPublic<BIMManager.ExportToExcelCommand>(app));
            registry.Register("ImportFromExcel",           app => StingCommandHandler.RunCommandPublic<BIMManager.ImportFromExcelCommand>(app));
            registry.Register("ExcelRoundTrip",            app => StingCommandHandler.RunCommandPublic<BIMManager.ExcelRoundTripCommand>(app));
            registry.Register("ExportSchedulesToExcel",    app => StingCommandHandler.RunCommandPublic<BIMManager.ExportSchedulesToExcelCommand>(app));
            registry.Register("ImportSchedulesFromExcel",  app => StingCommandHandler.RunCommandPublic<BIMManager.ImportSchedulesFromExcelCommand>(app));
            registry.Register("ExcelExchangeWizard",       app => StingCommandHandler.RunCommandPublic<BIMManager.ExcelExchangeWizardCommand>(app));
            registry.Register("DrawingTypes_ExportExcel",  app => StingCommandHandler.RunCommandPublic<BIMManager.DrawingTypeExportExcelCommand>(app));
            registry.Register("DrawingTypes_ImportExcel",  app => StingCommandHandler.RunCommandPublic<BIMManager.DrawingTypeImportExcelCommand>(app));

            // ── Platform Link ────────────────────────────────────────────────
            registry.Register("ACCPublish",                app => StingCommandHandler.RunCommandPublic<BIMManager.ACCPublishCommand>(app));
            registry.Register("CDEPackage",                app => StingCommandHandler.RunCommandPublic<BIMManager.CDEPackageCommand>(app));
            registry.Register("BCFExport",                 app => StingCommandHandler.RunCommandPublic<BIMManager.BCFExportCommand>(app));
            registry.Register("BCFImport",                 app => StingCommandHandler.RunCommandPublic<BIMManager.BCFImportCommand>(app));
            registry.Register("PlatformSync",              app => StingCommandHandler.RunCommandPublic<BIMManager.PlatformSyncCommand>(app));
            registry.Register("SharePointExport",          app => StingCommandHandler.RunCommandPublic<BIMManager.SharePointExportCommand>(app));
            registry.Register("PublishModelToPlanscape",   app => StingCommandHandler.RunCommandPublic<BIMManager.PublishModelCommand>(app));
            registry.Register("PlanscapeTestConnection",   app => StingCommandHandler.RunCommandPublic<BIMManager.PlanscapeConnectCommand>(app));

            // ── Revision Management ──────────────────────────────────────────
            registry.Register("RevisionSync",              app => StingCommandHandler.RunCommandPublic<Docs.RevisionSyncCommand>(app));

            // ── Panels (Phase 176) ───────────────────────────────────────────
            registry.Register("Panel_BatchSchedules",      app => StingCommandHandler.RunCommandPublic<Commands.Panels.BatchPanelSchedulesCommand>(app));
            registry.Register("Panel_Audit",               app => StingCommandHandler.RunCommandPublic<Commands.Panels.PanelScheduleAuditCommand>(app));
            registry.Register("Panel_ExportToExcel",       app => StingCommandHandler.RunCommandPublic<Commands.Panels.ExportPanelSchedulesToExcelCommand>(app));
            registry.Register("Panel_ImportFromExcel",     app => StingCommandHandler.RunCommandPublic<Commands.Panels.ImportPanelSchedulesFromExcelCommand>(app));
            registry.Register("Panel_FillSpares",          app => StingCommandHandler.RunCommandPublic<Commands.Panels.FillEmptySlotsWithSparesCommand>(app));
            registry.Register("Panel_FillSpaces",          app => StingCommandHandler.RunCommandPublic<Commands.Panels.FillEmptySlotsWithSpacesCommand>(app));
            registry.Register("Panel_FillSparesAll",       app => StingCommandHandler.RunCommandPublic<Commands.Panels.FillSparesAllSchedulesCommand>(app));
            registry.Register("Panel_SpacesToSpares",      app => StingCommandHandler.RunCommandPublic<Commands.Panels.ConvertSpacesToSparesCommand>(app));
            registry.Register("Panel_ClearSparesSpaces",   app => StingCommandHandler.RunCommandPublic<Commands.Panels.ClearSparesAndSpacesCommand>(app));

            // ── Team workload ────────────────────────────────────────────────
            registry.Register("BIM_TeamWorkload",          app => StingCommandHandler.RunCommandPublic<BIMManager.TeamWorkloadCommand>(app));

            // ── Folder cloud sync ────────────────────────────────────────────
            registry.Register("Folder_CloudSync",          app => StingCommandHandler.RunCommandPublic<BIMManager.FolderCloudSyncSettingsCommand>(app));
        }
    }
}
