// INT-02: Command registry module for all MODEL tab button tags.
using StingTools.UI;

namespace StingTools.UI.Modules
{
    internal sealed class ModelCommandModule : ICommandModule
    {
        public void Register(CommandRegistry registry)
        {
            // ── Core model creation ─────────────────────────────────────────
            registry.Register("ModelCreateWall",           app => StingCommandHandler.RunCommandPublic<Model.ModelCreateWallCommand>(app));
            registry.Register("ModelCreateRoom",           app => StingCommandHandler.RunCommandPublic<Model.ModelCreateRoomCommand>(app));
            registry.Register("ModelCreateFloor",          app => StingCommandHandler.RunCommandPublic<Model.ModelCreateFloorCommand>(app));
            registry.Register("ModelCreateCeiling",        app => StingCommandHandler.RunCommandPublic<Model.ModelCreateCeilingCommand>(app));
            registry.Register("ModelCreateRoof",           app => StingCommandHandler.RunCommandPublic<Model.ModelCreateRoofCommand>(app));
            registry.Register("ModelPlaceDoor",            app => StingCommandHandler.RunCommandPublic<Model.ModelPlaceDoorCommand>(app));
            registry.Register("ModelPlaceWindow",          app => StingCommandHandler.RunCommandPublic<Model.ModelPlaceWindowCommand>(app));
            registry.Register("ModelBuildingShell",        app => StingCommandHandler.RunCommandPublic<Model.ModelBuildingShellCommand>(app));
            registry.Register("ModelCreateRamp",           app => StingCommandHandler.RunCommandPublic<Model.ModelCreateRampCommand>(app));
            registry.Register("ModelCreateCanopy",         app => StingCommandHandler.RunCommandPublic<Model.ModelCreateCanopyCommand>(app));
            registry.Register("MEPRouteAnalysis",          app => StingCommandHandler.RunCommandPublic<Model.MEPRouteAnalysisCommand>(app));
            registry.Register("ModelPlaceColumn",          app => StingCommandHandler.RunCommandPublic<Model.ModelPlaceColumnCommand>(app));
            registry.Register("ModelColumnGrid",           app => StingCommandHandler.RunCommandPublic<Model.ModelColumnGridCommand>(app));
            registry.Register("ModelCreateBeam",           app => StingCommandHandler.RunCommandPublic<Model.ModelCreateBeamCommand>(app));
            registry.Register("ModelCreateDuct",           app => StingCommandHandler.RunCommandPublic<Model.ModelCreateDuctCommand>(app));
            registry.Register("ModelCreatePipe",           app => StingCommandHandler.RunCommandPublic<Model.ModelCreatePipeCommand>(app));
            registry.Register("ModelPlaceFixture",         app => StingCommandHandler.RunCommandPublic<Model.ModelPlaceFixtureCommand>(app));
            registry.Register("ModelDWGToModel",           app => StingCommandHandler.RunCommandPublic<Model.ModelDWGToModelCommand>(app));
            registry.Register("ModelDWGPreview",           app => StingCommandHandler.RunCommandPublic<Model.ModelDWGPreviewCommand>(app));
            registry.Register("FullModelAuto",             app => StingCommandHandler.RunCommandPublic<Model.FullModelAutoCommand>(app));

            // ── Structural ──────────────────────────────────────────────────
            registry.Register("StrCreatePadFooting",       app => StingCommandHandler.RunCommandPublic<Model.StrCreatePadFootingCommand>(app));
            registry.Register("StrCreateStripFooting",     app => StingCommandHandler.RunCommandPublic<Model.StrCreateStripFootingCommand>(app));
            registry.Register("StrCreateStructuralSlab",   app => StingCommandHandler.RunCommandPublic<Model.StrCreateStructuralSlabCommand>(app));
            registry.Register("StrCreateStructuralWall",   app => StingCommandHandler.RunCommandPublic<Model.StrCreateStructuralWallCommand>(app));
            registry.Register("StrCreateBeamSystem",       app => StingCommandHandler.RunCommandPublic<Model.StrCreateBeamSystemCommand>(app));
            registry.Register("StrCreateBracing",         app => StingCommandHandler.RunCommandPublic<Model.StrCreateBracingCommand>(app));
            registry.Register("StrCreateTruss",            app => StingCommandHandler.RunCommandPublic<Model.StrCreateTrussCommand>(app));
            registry.Register("StrCreateFullBayFrame",     app => StingCommandHandler.RunCommandPublic<Model.StrCreateFullBayFrameCommand>(app));
            registry.Register("StrCreateGridFrame",        app => StingCommandHandler.RunCommandPublic<Model.StrCreateGridFrameCommand>(app));
            registry.Register("StrAnalyzeLoadPaths",       app => StingCommandHandler.RunCommandPublic<Model.StrAnalyzeLoadPathsCommand>(app));
            registry.Register("StrDetectBays",             app => StingCommandHandler.RunCommandPublic<Model.StrDetectBaysCommand>(app));
            registry.Register("StrCADToStructural",        app => StingCommandHandler.RunCommandPublic<Model.StrCADToStructuralCommand>(app));
            registry.Register("StrCADPreview",             app => StingCommandHandler.RunCommandPublic<Model.StrCADPreviewCommand>(app));
            registry.Register("StrRecommendGrid",          app => StingCommandHandler.RunCommandPublic<Model.StrRecommendGridCommand>(app));
            registry.Register("StrCADWizard",              app => StingCommandHandler.RunCommandPublic<Model.StrCADWizardCommand>(app));
            registry.Register("DWGDryRunPreview",          app => StingCommandHandler.RunCommandPublic<Model.DWGDryRunPreviewCommand>(app));
            registry.Register("DWGExplodeImports",         app => StingCommandHandler.RunCommandPublic<Model.DWGExplodeImportsCommand>(app));
            registry.Register("DWGDetectOpenings",         app => StingCommandHandler.RunCommandPublic<Model.DWGDetectOpeningsCommand>(app));
            registry.Register("DWGInteractivePickWall",    app => StingCommandHandler.RunCommandPublic<Model.DWGInteractivePickWallCommand>(app));
            registry.Register("DWGInteractivePickColumn",  app => StingCommandHandler.RunCommandPublic<Model.DWGInteractivePickColumnCommand>(app));
            registry.Register("DWGInteractivePickBeam",    app => StingCommandHandler.RunCommandPublic<Model.DWGInteractivePickBeamCommand>(app));
            registry.Register("QuickStructuralDWG",        app => StingCommandHandler.RunCommandPublic<Model.QuickStructuralDWGCommand>(app));
            registry.Register("StructuralDWGAudit",        app => StingCommandHandler.RunCommandPublic<Model.StructuralDWGAuditCommand>(app));
            registry.Register("StructuralDWGJunctionScan", app => StingCommandHandler.RunCommandPublic<Model.StructuralDWGJunctionScanCommand>(app));
            registry.Register("StrCheckPrerequisites",     app => StingCommandHandler.RunCommandPublic<Model.StrCheckPrerequisitesCommand>(app));
            registry.Register("StrBrowseTypeCatalog",      app => StingCommandHandler.RunCommandPublic<Model.StrBrowseTypeCatalogCommand>(app));
            registry.Register("StrExcelImport",            app => StingCommandHandler.RunCommandPublic<Model.StrExcelImportCommand>(app));
            registry.Register("StrExcelImportColumns",     app => StingCommandHandler.RunCommandPublic<Model.StrExcelImportColumnsCommand>(app));
            registry.Register("StrExcelImportBeams",       app => StingCommandHandler.RunCommandPublic<Model.StrExcelImportBeamsCommand>(app));
            registry.Register("StrExcelExportSchedule",    app => StingCommandHandler.RunCommandPublic<Model.StrExcelExportScheduleCommand>(app));
            registry.Register("StrExcelTemplate",          app => StingCommandHandler.RunCommandPublic<Model.StrExcelTemplateCommand>(app));
            registry.Register("StrAutoRebar",              app => StingCommandHandler.RunCommandPublic<Model.StrAutoRebarCommand>(app));
            registry.Register("StrAutoSizeAll",            app => StingCommandHandler.RunCommandPublic<Model.StrAutoSizeAllCommand>(app));
            registry.Register("StrAutoSizeApply",          app => StingCommandHandler.RunCommandPublic<Model.StrAutoSizeApplyCommand>(app));
            registry.Register("StrRCDesign",               app => StingCommandHandler.RunCommandPublic<Model.StrRCDesignCommand>(app));
            registry.Register("StrSetUgandanDefaults",     app => StingCommandHandler.RunCommandPublic<Commands.Structural.SetUgandanDefaultsCommand>(app));
            registry.Register("StrGridOptimize",           app => StingCommandHandler.RunCommandPublic<Model.StrGridOptimizeCommand>(app));
            registry.Register("StrCarbonOptimize",         app => StingCommandHandler.RunCommandPublic<Model.StrCarbonOptimizeCommand>(app));
            registry.Register("StrBarBending",             app => StingCommandHandler.RunCommandPublic<Model.StrGenerateBarBendingCommand>(app));
            registry.Register("StrDesignReport",           app => StingCommandHandler.RunCommandPublic<Model.StrStructuralReportCommand>(app));
            registry.Register("StrLoadPathVisualizer",     app => StingCommandHandler.RunCommandPublic<Model.StrLoadPathVisualizerCommand>(app));
            registry.Register("StrDesignCheck",            app => StingCommandHandler.RunCommandPublic<Model.StrDesignCheckCommand>(app));
            registry.Register("StrEnhancedCADImport",      app => StingCommandHandler.RunCommandPublic<Model.StrEnhancedCADImportCommand>(app));
        }
    }
}
