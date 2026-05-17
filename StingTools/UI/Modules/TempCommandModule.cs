// INT-02: Command registry module for all TEMP tab button tags.
using StingTools.UI;

namespace StingTools.UI.Modules
{
    internal sealed class TempCommandModule : ICommandModule
    {
        public void Register(CommandRegistry registry)
        {
            // ── Setup ───────────────────────────────────────────────────────
            registry.Register("ProjectSetup",              app => StingCommandHandler.RunCommandPublic<Temp.ProjectSetupCommand>(app));
            registry.Register("MasterSetup",               app => StingCommandHandler.RunCommandPublic<Temp.MasterSetupCommand>(app));
            registry.Register("CreateParameters",          app => StingCommandHandler.RunCommandPublic<Temp.CreateParametersCommand>(app));

            // ── Materials ───────────────────────────────────────────────────
            registry.Register("CreateBLEMaterials",        app => StingCommandHandler.RunCommandPublic<Temp.CreateBLEMaterialsCommand>(app));
            registry.Register("CreateMEPMaterials",        app => StingCommandHandler.RunCommandPublic<Temp.CreateMEPMaterialsCommand>(app));
            registry.Register("MaterialManagerFull",       app => StingCommandHandler.RunCommandPublic<Temp.MaterialManagerCommand>(app));

            // ── Families ────────────────────────────────────────────────────
            registry.Register("CreateWalls",               app => StingCommandHandler.RunCommandPublic<Temp.CreateWallsCommand>(app));
            registry.Register("CreateFloors",              app => StingCommandHandler.RunCommandPublic<Temp.CreateFloorsCommand>(app));
            registry.Register("CreateCeilings",            app => StingCommandHandler.RunCommandPublic<Temp.CreateCeilingsCommand>(app));
            registry.Register("CreateRoofs",               app => StingCommandHandler.RunCommandPublic<Temp.CreateRoofsCommand>(app));
            registry.Register("CreateDucts",               app => StingCommandHandler.RunCommandPublic<Temp.CreateDuctsCommand>(app));
            registry.Register("CreatePipes",               app => StingCommandHandler.RunCommandPublic<Temp.CreatePipesCommand>(app));
            registry.Register("CreateCableTrays",          app => StingCommandHandler.RunCommandPublic<Temp.CreateCableTraysCommand>(app));
            registry.Register("CreateConduits",            app => StingCommandHandler.RunCommandPublic<Temp.CreateConduitsCommand>(app));

            // ── Schedules ───────────────────────────────────────────────────
            registry.Register("FullAuto",                  app => StingCommandHandler.RunCommandPublic<Temp.FullAutoPopulateCommand>(app));
            registry.Register("CreateBatch",               app => StingCommandHandler.RunCommandPublic<Temp.BatchSchedulesCommand>(app));
            registry.Register("MaterialSchedules",         app => StingCommandHandler.RunCommandPublic<Temp.CreateMaterialSchedulesCommand>(app));
            registry.Register("AutoPopulate",              app => StingCommandHandler.RunCommandPublic<Temp.AutoPopulateCommand>(app));
            registry.Register("FormulaEvaluator",          app => StingCommandHandler.RunCommandPublic<Temp.FormulaEvaluatorCommand>(app));
            registry.Register("ExportCSV",                 app => StingCommandHandler.RunCommandPublic<Temp.ExportCSVCommand>(app));
            registry.Register("CorporateTitleBlock",       app => StingCommandHandler.RunCommandPublic<Temp.CorporateTitleBlockScheduleCommand>(app));
            registry.Register("DrawingRegisterSchedule",   app => StingCommandHandler.RunCommandPublic<Temp.DrawingRegisterScheduleCommand>(app));
            registry.Register("ScheduleAudit",             app => StingCommandHandler.RunCommandPublic<Temp.ScheduleAuditCommand>(app));
            registry.Register("ScheduleCompare",           app => StingCommandHandler.RunCommandPublic<Temp.ScheduleCompareCommand>(app));
            registry.Register("ScheduleDuplicate",         app => StingCommandHandler.RunCommandPublic<Temp.ScheduleDuplicateCommand>(app));
            registry.Register("ScheduleRefresh",           app => StingCommandHandler.RunCommandPublic<Temp.ScheduleRefreshCommand>(app));
            registry.Register("ScheduleFieldMgr",          app => StingCommandHandler.RunCommandPublic<Temp.ScheduleFieldManagerCommand>(app));
            registry.Register("ScheduleColor",             app => StingCommandHandler.RunCommandPublic<Temp.ScheduleColorCommand>(app));
            registry.Register("ScheduleStats",             app => StingCommandHandler.RunCommandPublic<Temp.ScheduleStatsCommand>(app));
            registry.Register("ScheduleDelete",            app => StingCommandHandler.RunCommandPublic<Temp.ScheduleDeleteCommand>(app));
            registry.Register("ScheduleReport",            app => StingCommandHandler.RunCommandPublic<Temp.ScheduleReportCommand>(app));
            registry.Register("ScheduleFieldRemapAudit",   app => StingCommandHandler.RunCommandPublic<Temp.ScheduleFieldRemapAuditCommand>(app));

            // ── Templates ───────────────────────────────────────────────────
            registry.Register("CreateFilters",             app => StingCommandHandler.RunCommandPublic<Temp.CreateFiltersCommand>(app));
            registry.Register("ApplyFilters",              app => StingCommandHandler.RunCommandPublic<Temp.ApplyFiltersToViewsCommand>(app));
            registry.Register("CreateWorksets",            app => StingCommandHandler.RunCommandPublic<Temp.CreateWorksetsCommand>(app));
            registry.Register("ViewTemplates",             app => StingCommandHandler.RunCommandPublic<Temp.ViewTemplatesCommand>(app));
            registry.Register("CreateLinePatterns",        app => StingCommandHandler.RunCommandPublic<Temp.CreateLinePatternsCommand>(app));
            registry.Register("CreatePhases",              app => StingCommandHandler.RunCommandPublic<Temp.CreatePhasesCommand>(app));
            registry.Register("TemplateSetupWizard",       app => StingCommandHandler.RunCommandPublic<Temp.TemplateSetupWizardCommand>(app));
            registry.Register("AutoAssignTemplates",       app => StingCommandHandler.RunCommandPublic<Temp.AutoAssignTemplatesCommand>(app));
            registry.Register("TemplateAudit",             app => StingCommandHandler.RunCommandPublic<Temp.TemplateAuditCommand>(app));
            registry.Register("TemplateDiff",              app => StingCommandHandler.RunCommandPublic<Temp.TemplateDiffCommand>(app));
            registry.Register("TemplateComplianceScore",   app => StingCommandHandler.RunCommandPublic<Temp.TemplateComplianceScoreCommand>(app));
            registry.Register("AutoFixTemplate",           app => StingCommandHandler.RunCommandPublic<Temp.AutoFixTemplateCommand>(app));
            registry.Register("SyncTemplateOverrides",     app => StingCommandHandler.RunCommandPublic<Temp.SyncTemplateOverridesCommand>(app));
            registry.Register("CreateVGOverrides",         app => StingCommandHandler.RunCommandPublic<Temp.CreateVGOverridesCommand>(app));
            registry.Register("CloneTemplate",             app => StingCommandHandler.RunCommandPublic<Temp.CloneTemplateCommand>(app));
            registry.Register("BatchVGReset",              app => StingCommandHandler.RunCommandPublic<Temp.BatchVGResetCommand>(app));
            registry.Register("BatchAddFamilyParams",      app => StingCommandHandler.RunCommandPublic<Temp.BatchAddFamilyParamsCommand>(app));
            registry.Register("FamilyParamProcessor",      app => StingCommandHandler.RunCommandPublic<Temp.FamilyParameterProcessorCommand>(app));
            registry.Register("CreateTemplateSchedules",   app => StingCommandHandler.RunCommandPublic<Temp.CreateTemplateSchedulesCommand>(app));
            registry.Register("CreateFillPatterns",        app => StingCommandHandler.RunCommandPublic<Temp.CreateFillPatternsCommand>(app));
            registry.Register("CreateLineStyles",          app => StingCommandHandler.RunCommandPublic<Temp.CreateLineStylesCommand>(app));
            registry.Register("CreateObjectStyles",        app => StingCommandHandler.RunCommandPublic<Temp.CreateObjectStylesCommand>(app));
            registry.Register("CreateTextStyles",          app => StingCommandHandler.RunCommandPublic<Temp.CreateTextStylesCommand>(app));
            registry.Register("CreateDimensionStyles",     app => StingCommandHandler.RunCommandPublic<Temp.CreateDimensionStylesCommand>(app));

            // ── Data Pipeline ───────────────────────────────────────────────
            registry.Register("ValidateTemplate",          app => StingCommandHandler.RunCommandPublic<Temp.ValidateTemplateCommand>(app));
            registry.Register("DynamicBindings",           app => StingCommandHandler.RunCommandPublic<Temp.DynamicBindingsCommand>(app));
            registry.Register("SchemaValidate",            app => StingCommandHandler.RunCommandPublic<Temp.SchemaValidateCommand>(app));
            registry.Register("BOQExportLegacy",           app => StingCommandHandler.RunCommandPublic<Temp.BOQExportCommand>(app));
            registry.Register("IFCExport",                 app => StingCommandHandler.RunCommandPublic<Temp.IFCExportCommand>(app));
            registry.Register("CrossValidateRegistry",     app => StingCommandHandler.RunCommandPublic<Temp.CrossValidateRegistryCommand>(app));
            registry.Register("DataIntegrityCheck",        app => StingCommandHandler.RunCommandPublic<Temp.DataIntegrityCheckCommand>(app));
            registry.Register("DataReport",                app => StingCommandHandler.RunCommandPublic<Temp.DataReportCommand>(app));
            registry.Register("ExportUnifiedRegistry",     app => StingCommandHandler.RunCommandPublic<Temp.ExportUnifiedRegistryCommand>(app));
            registry.Register("IFCExportEnhanced",         app => StingCommandHandler.RunCommandPublic<Temp.IFCExportEnhancedCommand>(app));
            registry.Register("ModelElementAudit",         app => StingCommandHandler.RunCommandPublic<Temp.ModelElementAuditCommand>(app));
            registry.Register("ValidateBindingMatrix",     app => StingCommandHandler.RunCommandPublic<Temp.ValidateBindingMatrixCommand>(app));
            registry.Register("ValidateFamilyBindings",    app => StingCommandHandler.RunCommandPublic<Temp.ValidateFamilyBindingsCommand>(app));
            registry.Register("ViewParameterMetadata",     app => StingCommandHandler.RunCommandPublic<Temp.ViewParameterMetadataCommand>(app));
            registry.Register("HandoverPackage",           app => StingCommandHandler.RunCommandPublic<Temp.HandoverPackageCommand>(app));
            registry.Register("ClashDetectionEnhanced",    app => StingCommandHandler.RunCommandPublic<Temp.ClashDetectionEnhancedCommand>(app));

            // ── COBie Data ──────────────────────────────────────────────────
            registry.Register("COBieValidator",            app => StingCommandHandler.RunCommandPublic<Temp.COBieDataSummaryCommand>(app));
            registry.Register("COBieTypeMap",              app => StingCommandHandler.RunCommandPublic<Temp.COBieTypeMapCommand>(app));
            registry.Register("COBieSystemMap",            app => StingCommandHandler.RunCommandPublic<Temp.COBieSystemMapCommand>(app));
            registry.Register("COBiePickLists",            app => StingCommandHandler.RunCommandPublic<Temp.COBiePickListsCommand>(app));
            registry.Register("COBieAttributes",           app => StingCommandHandler.RunCommandPublic<Temp.COBieAttributeTemplatesCommand>(app));
            registry.Register("COBieAttributeTemplates",   app => StingCommandHandler.RunCommandPublic<Temp.COBieAttributeTemplatesCommand>(app));
            registry.Register("COBieJobTemplates",         app => StingCommandHandler.RunCommandPublic<Temp.COBieJobTemplatesCommand>(app));
            registry.Register("COBieSpareParts",           app => StingCommandHandler.RunCommandPublic<Temp.COBieSparePartsCommand>(app));
            registry.Register("COBieDocTypes",             app => StingCommandHandler.RunCommandPublic<Temp.COBieDocumentTypesCommand>(app));
            registry.Register("COBieDocumentTypes",        app => StingCommandHandler.RunCommandPublic<Temp.COBieDocumentTypesCommand>(app));
            registry.Register("COBieZoneTypes",            app => StingCommandHandler.RunCommandPublic<Temp.COBieZoneTypesCommand>(app));
            registry.Register("COBieDocTypeAudit",         app => StingCommandHandler.RunCommandPublic<Temp.COBieDocumentTypeAuditCommand>(app));
            registry.Register("COBieZoneTypeAudit",        app => StingCommandHandler.RunCommandPublic<Temp.COBieZoneTypeAuditCommand>(app));
            registry.Register("COBieAutoMatch",            app => StingCommandHandler.RunCommandPublic<Temp.COBieAutoMatchCommand>(app));
            registry.Register("COBieDataSummary",          app => StingCommandHandler.RunCommandPublic<Temp.COBieDataSummaryCommand>(app));

            // ── MEP Schedules ───────────────────────────────────────────────
            registry.Register("MEPScheduleHVAC",           app => StingCommandHandler.RunCommandPublic<Temp.MechanicalEquipmentScheduleCommand>(app));
            registry.Register("MEPScheduleElec",           app => StingCommandHandler.RunCommandPublic<Temp.ElectricalDeviceScheduleCommand>(app));
            registry.Register("MEPSchedulePlumb",          app => StingCommandHandler.RunCommandPublic<Temp.PlumbingFixtureScheduleCommand>(app));
            registry.Register("MEPScheduleFire",           app => StingCommandHandler.RunCommandPublic<Temp.FireDeviceScheduleCommand>(app));
            registry.Register("MEPScheduleAll",            app => StingCommandHandler.RunCommandPublic<Temp.BatchMEPSchedulesCommand>(app));
            registry.Register("MechanicalEquipmentSchedule",app => StingCommandHandler.RunCommandPublic<Temp.MechanicalEquipmentScheduleCommand>(app));
            registry.Register("MEPConnectionAudit",        app => StingCommandHandler.RunCommandPublic<Temp.MEPConnectionAuditCommand>(app));
            registry.Register("MEPSizingCheck",            app => StingCommandHandler.RunCommandPublic<Temp.MEPSizingCheckCommand>(app));
            registry.Register("MEPSpaceAnalysis",          app => StingCommandHandler.RunCommandPublic<Temp.MEPSpaceAnalysisCommand>(app));
            registry.Register("MEPSystemAudit",            app => StingCommandHandler.RunCommandPublic<Temp.MEPSystemAuditCommand>(app));

            // ── Room / Space ────────────────────────────────────────────────
            registry.Register("RoomAudit",                 app => StingCommandHandler.RunCommandPublic<Temp.RoomAuditCommand>(app));
            registry.Register("RoomSchedule",              app => StingCommandHandler.RunCommandPublic<Temp.RoomScheduleCommand>(app));
            registry.Register("RoomZoneAssign",            app => StingCommandHandler.RunCommandPublic<Temp.RoomZoneAssignCommand>(app));
            registry.Register("RoomParamPush",             app => StingCommandHandler.RunCommandPublic<Temp.RoomBasedParamPushCommand>(app));
            registry.Register("RoomBasedParamPush",        app => StingCommandHandler.RunCommandPublic<Temp.RoomBasedParamPushCommand>(app));
            registry.Register("RoomDataExport",            app => StingCommandHandler.RunCommandPublic<Temp.RoomDataExportCommand>(app));
            registry.Register("SpaceManagement",           app => StingCommandHandler.RunCommandPublic<Temp.SpaceManagementCommand>(app));

            // ── Standards ───────────────────────────────────────────────────
            registry.Register("Bs7671Compliance",          app => StingCommandHandler.RunCommandPublic<Temp.Bs7671ComplianceCommand>(app));
            registry.Register("Bs8300Accessibility",       app => StingCommandHandler.RunCommandPublic<Temp.Bs8300AccessibilityCommand>(app));
            registry.Register("CibseVelocityCheck",        app => StingCommandHandler.RunCommandPublic<Temp.CibseVelocityCheckCommand>(app));
            registry.Register("Iso19650DeepCompliance",    app => StingCommandHandler.RunCommandPublic<Temp.Iso19650DeepComplianceCommand>(app));
            registry.Register("PartLCompliance",           app => StingCommandHandler.RunCommandPublic<Temp.PartLComplianceCommand>(app));
            registry.Register("StandardsDashboard",        app => StingCommandHandler.RunCommandPublic<Temp.StandardsDashboardCommand>(app));
            registry.Register("UniclassClassify",          app => StingCommandHandler.RunCommandPublic<Temp.UniclassClassifyCommand>(app));
            registry.Register("NamingAudit",               app => StingCommandHandler.RunCommandPublic<Temp.NamingConventionAuditCommand>(app));
            registry.Register("MEPClearance",              app => StingCommandHandler.RunCommandPublic<Temp.MEPClearanceValidationCommand>(app));
            registry.Register("IFCPropertyValidation",     app => StingCommandHandler.RunCommandPublic<Temp.IFCPropertyValidationCommand>(app));
            registry.Register("CrossModelClash",           app => StingCommandHandler.RunCommandPublic<Temp.CrossModelClashCommand>(app));

            // ── IoT / Maintenance ───────────────────────────────────────────
            registry.Register("AssetCondition",            app => StingCommandHandler.RunCommandPublic<Temp.AssetConditionCommand>(app));
            registry.Register("CommissioningChecklist",    app => StingCommandHandler.RunCommandPublic<Temp.CommissioningChecklistCommand>(app));
            registry.Register("DigitalTwinExport",         app => StingCommandHandler.RunCommandPublic<Temp.DigitalTwinExportCommand>(app));
            registry.Register("EnergyAnalysis",            app => StingCommandHandler.RunCommandPublic<Temp.EnergyAnalysisCommand>(app));
            registry.Register("LifecycleCost",             app => StingCommandHandler.RunCommandPublic<Temp.LifecycleCostCommand>(app));
            registry.Register("SensorPointMapper",         app => StingCommandHandler.RunCommandPublic<Temp.SensorPointMapperCommand>(app));
            registry.Register("WarrantyTracker",           app => StingCommandHandler.RunCommandPublic<Temp.WarrantyTrackerCommand>(app));

            // ── MEP Creation ─────────────────────────────────────────────────
            registry.Register("CreateWallsInteractive",    app => StingCommandHandler.RunCommandPublic<Temp.CreateWallsInteractiveCommand>(app));
            registry.Register("PlaceDoors",                app => StingCommandHandler.RunCommandPublic<Temp.PlaceDoorsCommand>(app));
            registry.Register("PlaceMEPEquipment",         app => StingCommandHandler.RunCommandPublic<Temp.PlaceMEPEquipmentCommand>(app));
            registry.Register("PlaceWindows",              app => StingCommandHandler.RunCommandPublic<Temp.PlaceWindowsCommand>(app));
        }
    }
}
