// INT-02: Command registry module for all DOCS tab button tags.
using StingTools.UI;

namespace StingTools.UI.Modules
{
    internal sealed class DocsCommandModule : ICommandModule
    {
        public void Register(CommandRegistry registry)
        {
            // ── Sheet / View organizers ─────────────────────────────────────
            registry.Register("SheetOrganizer",            app => StingCommandHandler.RunCommandPublic<Docs.SheetOrganizerCommand>(app));
            registry.Register("ViewOrganizer",             app => StingCommandHandler.RunCommandPublic<Docs.ViewOrganizerCommand>(app));
            registry.Register("SheetIndex",                app => StingCommandHandler.RunCommandPublic<Docs.SheetIndexCommand>(app));
            registry.Register("SheetRegister",             app => StingCommandHandler.RunCommandPublic<Docs.SheetIndexCommand>(app));
            registry.Register("Transmittal",               app => StingCommandHandler.RunCommandPublic<Docs.TransmittalCommand>(app));
            registry.Register("ComplianceGateTransmittal", app => StingCommandHandler.RunCommandPublic<Docs.TransmittalCommand>(app));
            registry.Register("HandoverManual",            app => StingCommandHandler.RunCommandPublic<Docs.HandoverManualCommand>(app));
            registry.Register("FMHandover",                app => StingCommandHandler.RunCommandPublic<Docs.HandoverManualCommand>(app));

            // ── Doc Automation ──────────────────────────────────────────────
            registry.Register("DeleteUnusedViews",         app => StingCommandHandler.RunCommandPublic<Docs.DeleteUnusedViewsCommand>(app));
            registry.Register("SheetNamingCheck",          app => StingCommandHandler.RunCommandPublic<Docs.SheetNamingCheckCommand>(app));
            registry.Register("AutoNumberSheets",          app => StingCommandHandler.RunCommandPublic<Docs.AutoNumberSheetsCommand>(app));

            // ── Viewports ───────────────────────────────────────────────────
            registry.Register("AlignViewports",            app => StingCommandHandler.RunCommandPublic<Docs.AlignViewportsCommand>(app));
            registry.Register("VPAlignRight",              app => StingCommandHandler.RunCommandPublic<Docs.AlignViewportsCommand>(app));
            registry.Register("RenumberViewports",         app => StingCommandHandler.RunCommandPublic<Docs.RenumberViewportsCommand>(app));
            registry.Register("TextCase",                  app => StingCommandHandler.RunCommandPublic<Docs.TextCaseCommand>(app));
            registry.Register("SumAreas",                  app => StingCommandHandler.RunCommandPublic<Docs.SumAreasCommand>(app));

            // ── View Automation ─────────────────────────────────────────────
            registry.Register("DuplicateView",             app => StingCommandHandler.RunCommandPublic<Docs.DuplicateViewCommand>(app));
            registry.Register("BatchRenameViews",          app => StingCommandHandler.RunCommandPublic<Docs.BatchRenameViewsCommand>(app));
            registry.Register("CopyViewSettings",          app => StingCommandHandler.RunCommandPublic<Docs.CopyViewSettingsCommand>(app));
            registry.Register("AutoPlaceViewports",        app => StingCommandHandler.RunCommandPublic<Docs.AutoPlaceViewportsCommand>(app));
            registry.Register("CropToContent",             app => StingCommandHandler.RunCommandPublic<Docs.CropToContentCommand>(app));
            registry.Register("BatchAlignViewports",       app => StingCommandHandler.RunCommandPublic<Docs.BatchAlignViewportsCommand>(app));
            registry.Register("MagicRename",               app => StingCommandHandler.RunCommandPublic<Docs.MagicRenameCommand>(app));
            registry.Register("ViewTabColour",             app => StingCommandHandler.RunCommandPublic<Docs.ViewTabColourCommand>(app));
            registry.Register("RibbonStyler",              app => StingCommandHandler.RunCommandPublic<Docs.RibbonPanelStylerCommand>(app));

            // ── Sheet Manager ───────────────────────────────────────────────
            registry.Register("SheetManager",              app => StingCommandHandler.RunCommandPublic<Docs.SheetManagerCommand>(app));
            registry.Register("AutoLayout",                app => StingCommandHandler.RunCommandPublic<Docs.AutoLayoutCommand>(app));
            registry.Register("CloneSheet",                app => StingCommandHandler.RunCommandPublic<Docs.CloneSheetCommand>(app));
            registry.Register("PlaceUnplacedViews",        app => StingCommandHandler.RunCommandPublic<Docs.PlaceUnplacedViewsCommand>(app));
            registry.Register("OptimalScale",              app => StingCommandHandler.RunCommandPublic<Docs.OptimalScaleCommand>(app));
            registry.Register("SheetAudit",                app => StingCommandHandler.RunCommandPublic<Docs.SheetAuditCommand>(app));
            registry.Register("BatchArrange",              app => StingCommandHandler.RunCommandPublic<Docs.BatchArrangeCommand>(app));
            registry.Register("MoveViewport",              app => StingCommandHandler.RunCommandPublic<Docs.MoveViewportCommand>(app));
            registry.Register("MaxRectsLayout",            app => StingCommandHandler.RunCommandPublic<Docs.MaxRectsLayoutCommand>(app));
            registry.Register("SaveLayoutPreset",          app => StingCommandHandler.RunCommandPublic<Docs.SaveLayoutPresetCommand>(app));
            registry.Register("ApplyLayoutPreset",         app => StingCommandHandler.RunCommandPublic<Docs.ApplyLayoutPresetCommand>(app));
            registry.Register("BatchCloneSheets",          app => StingCommandHandler.RunCommandPublic<Docs.BatchCloneSheetsCommand>(app));
            registry.Register("BatchRenumberSheets",       app => StingCommandHandler.RunCommandPublic<Docs.BatchRenumberSheetsCommand>(app));
            registry.Register("AutoAssignVPTypes",         app => StingCommandHandler.RunCommandPublic<Docs.AutoAssignVPTypesCommand>(app));
            registry.Register("ExportSheetSet",            app => StingCommandHandler.RunCommandPublic<Docs.ExportSheetSetCommand>(app));
            registry.Register("PlaceWithOverflow",         app => StingCommandHandler.RunCommandPublic<Docs.PlaceWithOverflowCommand>(app));

            // ── Sheet Template ──────────────────────────────────────────────
            registry.Register("CreateFromTemplate",        app => StingCommandHandler.RunCommandPublic<Docs.CreateFromTemplateCommand>(app));
            registry.Register("SaveSheetTemplate",         app => StingCommandHandler.RunCommandPublic<Docs.SaveSheetTemplateCommand>(app));
            registry.Register("SheetComplianceCheck",      app => StingCommandHandler.RunCommandPublic<Docs.SheetComplianceCheckCommand>(app));
            registry.Register("GridAlignViewports",        app => StingCommandHandler.RunCommandPublic<Docs.GridAlignViewportsCommand>(app));
            registry.Register("AlignViewportEdges",        app => StingCommandHandler.RunCommandPublic<Docs.AlignViewportEdgesCommand>(app));
            registry.Register("DistributeViewports",       app => StingCommandHandler.RunCommandPublic<Docs.DistributeViewportsCommand>(app));
            registry.Register("BatchPrintSheets",          app => StingCommandHandler.RunCommandPublic<Docs.ExportCenterPdfCommand>(app));
            registry.Register("ExportSheetRegister",       app => StingCommandHandler.RunCommandPublic<Docs.ExportSheetRegisterCommand>(app));
            registry.Register("ExportCenter",              app => StingCommandHandler.RunCommandPublic<Docs.ExportCenterCommand>(app));
            registry.Register("ExportCenterPDF",           app => StingCommandHandler.RunCommandPublic<Docs.ExportCenterPdfCommand>(app));

            // ── Doc Automation Ext ──────────────────────────────────────────
            registry.Register("BatchCreateViews",          app => StingCommandHandler.RunCommandPublic<Docs.BatchCreateViewsCommand>(app));
            registry.Register("BatchCreateSheets",         app => StingCommandHandler.RunCommandPublic<Docs.BatchCreateSheetsCommand>(app));
            registry.Register("CreateDependentViews",      app => StingCommandHandler.RunCommandPublic<Docs.CreateDependentViewsCommand>(app));
            registry.Register("ScopeBoxManager",           app => StingCommandHandler.RunCommandPublic<Docs.ScopeBoxManagerCommand>(app));
            registry.Register("ViewTemplateAssigner",      app => StingCommandHandler.RunCommandPublic<Docs.ViewTemplateAssignerCommand>(app));
            registry.Register("DocumentationPackage",      app => StingCommandHandler.RunCommandPublic<Docs.DocumentationPackageCommand>(app));
            registry.Register("BatchCreateSections",       app => StingCommandHandler.RunCommandPublic<Docs.BatchCreateSectionsCommand>(app));
            registry.Register("BatchCreateElevations",     app => StingCommandHandler.RunCommandPublic<Docs.BatchCreateElevationsCommand>(app));
            registry.Register("DocsDrawingRegister",       app => StingCommandHandler.RunCommandPublic<Docs.DrawingRegisterCommand>(app));
            registry.Register("DrawingRegister",           app => StingCommandHandler.RunCommandPublic<Docs.DrawingRegisterCommand>(app));
            registry.Register("ProjectBrowserOrganizer",   app => StingCommandHandler.RunCommandPublic<Docs.ProjectBrowserOrganizerCommand>(app));
            registry.Register("RevisionCloudAuto",         app => StingCommandHandler.RunCommandPublic<Docs.RevisionCloudAutoCreateCommand>(app));

            // ── Handover / Audit ────────────────────────────────────────────
            registry.Register("SpaceHandover",             app => StingCommandHandler.RunCommandPublic<Docs.SpaceHandoverReportCommand>(app));
            registry.Register("JournalParser",             app => StingCommandHandler.RunCommandPublic<Docs.JournalParserCommand>(app));
            registry.Register("AssetHealthReport",         app => StingCommandHandler.RunCommandPublic<Docs.AssetHealthReportCommand>(app));
            registry.Register("MaintenanceScheduleExport", app => StingCommandHandler.RunCommandPublic<Docs.MaintenanceScheduleExportCommand>(app));
            registry.Register("OAndMManualExport",         app => StingCommandHandler.RunCommandPublic<Docs.OAndMManualExportCommand>(app));
            registry.Register("SpaceHandoverReport",       app => StingCommandHandler.RunCommandPublic<Docs.SpaceHandoverReportCommand>(app));
            registry.Register("StreamingCOBieExport",      app => StingCommandHandler.RunCommandPublic<Docs.StreamingCOBieExportCommand>(app));
            registry.Register("SpatialValidation",         app => StingCommandHandler.RunCommandPublic<Docs.SpatialValidationCommand>(app));
            registry.Register("SpatialValidationExport",   app => StingCommandHandler.RunCommandPublic<Docs.SpatialValidationExportCommand>(app));
            registry.Register("GridAudit",                 app => StingCommandHandler.RunCommandPublic<Docs.GridAuditCommand>(app));
            registry.Register("LevelAudit",                app => StingCommandHandler.RunCommandPublic<Docs.LevelAuditCommand>(app));
            registry.Register("FamilyAudit",               app => StingCommandHandler.RunCommandPublic<Docs.FamilyAuditCommand>(app));
            registry.Register("FamilyAuditExport",         app => StingCommandHandler.RunCommandPublic<Docs.FamilyAuditExportCommand>(app));
            registry.Register("ViewSheetCompleteness",     app => StingCommandHandler.RunCommandPublic<Docs.ViewSheetCompletenessCommand>(app));
            registry.Register("BatchPDFExport",            app => StingCommandHandler.RunCommandPublic<Docs.BatchPDFExportCommand>(app));
            registry.Register("SheetSetSummary",           app => StingCommandHandler.RunCommandPublic<Docs.SheetSetSummaryCommand>(app));
        }
    }
}
