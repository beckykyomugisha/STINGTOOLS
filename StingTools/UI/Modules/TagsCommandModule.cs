// INT-02: Command registry module for all TAGS/CREATE tab button tags.
using StingTools.UI;

namespace StingTools.UI.Modules
{
    internal sealed class TagsCommandModule : ICommandModule
    {
        public void Register(CommandRegistry registry)
        {
            // ── Core tagging ────────────────────────────────────────────────
            registry.Register("AutoTag",                   app => StingCommandHandler.RunCommandPublic<Tags.AutoTagCommand>(app));
            registry.Register("BatchTag",                  app => StingCommandHandler.RunCommandPublic<Tags.BatchTagCommand>(app));
            registry.Register("TagAndCombine",             app => StingCommandHandler.RunCommandPublic<Tags.TagAndCombineCommand>(app));
            registry.Register("TagNewOnly",                app => StingCommandHandler.RunCommandPublic<Tags.TagNewOnlyCommand>(app));
            registry.Register("TagChanged",                app => StingCommandHandler.RunCommandPublic<Tags.TagChangedCommand>(app));
            registry.Register("TagFormatMigration",        app => StingCommandHandler.RunCommandPublic<Tags.TagFormatMigrationCommand>(app));

            // ── Smart placement ─────────────────────────────────────────────
            registry.Register("SmartPlaceTags",            app => StingCommandHandler.RunCommandPublic<Tags.SmartPlaceTagsCommand>(app));
            registry.Register("ArrangeTags",               app => StingCommandHandler.RunCommandPublic<Tags.ArrangeTagsCommand>(app));
            registry.Register("BatchPlaceTags",            app => StingCommandHandler.RunCommandPublic<Tags.BatchPlaceTagsCommand>(app));
            registry.Register("RemoveAnnotationTags",      app => StingCommandHandler.RunCommandPublic<Tags.RemoveAnnotationTagsCommand>(app));
            registry.Register("LearnTagPlacement",         app => StingCommandHandler.RunCommandPublic<Tags.LearnTagPlacementCommand>(app));
            registry.Register("ApplyTagTemplate",          app => StingCommandHandler.RunCommandPublic<Tags.ApplyTagTemplateCommand>(app));
            registry.Register("TagOverlapAnalysis",        app => StingCommandHandler.RunCommandPublic<Tags.TagOverlapAnalysisCommand>(app));
            registry.Register("BatchTagTextSize",          app => StingCommandHandler.RunCommandPublic<Tags.BatchTagTextSizeCommand>(app));
            registry.Register("SetTagCatLineWeight",       app => StingCommandHandler.RunCommandPublic<Tags.SetTagCategoryLineWeightCommand>(app));
            registry.Register("Tag3D",                     app => StingCommandHandler.RunCommandPublic<Tags.Tag3DCommand>(app));
            registry.Register("RepairDuplicateSeq",        app => StingCommandHandler.RunCommandPublic<Tags.RepairDuplicateSeqCommand>(app));

            // ── Rich TAG7 ───────────────────────────────────────────────────
            registry.Register("RichTagNote",               app => StingCommandHandler.RunCommandPublic<Tags.RichTagNoteCommand>(app));
            registry.Register("ExportRichTagReport",       app => StingCommandHandler.RunCommandPublic<Tags.ExportRichTagReportCommand>(app));
            registry.Register("ViewTag7Sections",          app => StingCommandHandler.RunCommandPublic<Tags.ViewTag7SectionsCommand>(app));
            registry.Register("SwitchTag7Preset",         app => StingCommandHandler.RunCommandPublic<Tags.SwitchTag7PresetCommand>(app));
            registry.Register("RichSegmentNote",           app => StingCommandHandler.RunCommandPublic<Tags.RichSegmentNoteCommand>(app));
            registry.Register("ViewSegments",              app => StingCommandHandler.RunCommandPublic<Tags.ViewSegmentsCommand>(app));

            // ── Legend builder ──────────────────────────────────────────────
            registry.Register("CreateColorLegend",         app => StingCommandHandler.RunCommandPublic<Tags.CreateColorLegendCommand>(app));
            registry.Register("ExportColorLegendHtml",     app => StingCommandHandler.RunCommandPublic<Tags.ExportColorLegendHtmlCommand>(app));
            registry.Register("AutoCreateLegends",         app => StingCommandHandler.RunCommandPublic<Tags.AutoCreateLegendsCommand>(app));
            registry.Register("LegendFromView",            app => StingCommandHandler.RunCommandPublic<Tags.LegendFromViewCommand>(app));
            registry.Register("PlaceLegendOnSheet",        app => StingCommandHandler.RunCommandPublic<Tags.PlaceLegendOnSheetCommand>(app));
            registry.Register("SheetContextLegend",        app => StingCommandHandler.RunCommandPublic<Tags.SheetContextLegendCommand>(app));
            registry.Register("PlaceLegendOnAllSheets",    app => StingCommandHandler.RunCommandPublic<Tags.PlaceLegendOnAllSheetsCommand>(app));
            registry.Register("BatchSheetContextLegends",  app => StingCommandHandler.RunCommandPublic<Tags.BatchSheetContextLegendsCommand>(app));
            registry.Register("CreateTagLegend",           app => StingCommandHandler.RunCommandPublic<Tags.CreateTagLegendCommand>(app));
            registry.Register("SheetTagLegend",            app => StingCommandHandler.RunCommandPublic<Tags.SheetTagLegendCommand>(app));
            registry.Register("BatchTagLegends",           app => StingCommandHandler.RunCommandPublic<Tags.BatchTagLegendsCommand>(app));
            registry.Register("UpdateLegend",              app => StingCommandHandler.RunCommandPublic<Tags.UpdateLegendCommand>(app));
            registry.Register("DeleteStaleLegend",         app => StingCommandHandler.RunCommandPublic<Tags.DeleteStaleLegendCommand>(app));
            registry.Register("OneClickLegendPipeline",    app => StingCommandHandler.RunCommandPublic<Tags.OneClickLegendPipelineCommand>(app));
            registry.Register("MepSystemLegend",           app => StingCommandHandler.RunCommandPublic<Tags.MepSystemLegendCommand>(app));
            registry.Register("MaterialLegend",            app => StingCommandHandler.RunCommandPublic<Tags.MaterialLegendCommand>(app));
            registry.Register("CompoundTypeLegend",        app => StingCommandHandler.RunCommandPublic<Tags.CompoundTypeLegendCommand>(app));
            registry.Register("EquipmentLegend",           app => StingCommandHandler.RunCommandPublic<Tags.EquipmentLegendCommand>(app));
            registry.Register("FireRatingLegend",          app => StingCommandHandler.RunCommandPublic<Tags.FireRatingLegendCommand>(app));
            registry.Register("MasterLegendPipeline",      app => StingCommandHandler.RunCommandPublic<Tags.MasterLegendPipelineCommand>(app));
            registry.Register("FilterLegend",              app => StingCommandHandler.RunCommandPublic<Tags.FilterLegendCommand>(app));
            registry.Register("TemplateLegend",            app => StingCommandHandler.RunCommandPublic<Tags.TemplateLegendCommand>(app));
            registry.Register("VGCategoryLegend",          app => StingCommandHandler.RunCommandPublic<Tags.VGCategoryLegendCommand>(app));
            registry.Register("BatchTemplateLegend",       app => StingCommandHandler.RunCommandPublic<Tags.BatchTemplateLegendCommand>(app));
            registry.Register("FlexibleLegend",            app => StingCommandHandler.RunCommandPublic<Tags.FlexibleLegendCommand>(app));
            registry.Register("LegendFromPreset",          app => StingCommandHandler.RunCommandPublic<Tags.LegendFromPresetCommand>(app));
            registry.Register("ComponentTypeLegend",       app => StingCommandHandler.RunCommandPublic<Tags.ComponentTypeLegendCommand>(app));
            registry.Register("ColorReferenceLegend",      app => StingCommandHandler.RunCommandPublic<Tags.ColorReferenceLegendCommand>(app));
            registry.Register("LegendSyncAudit",           app => StingCommandHandler.RunCommandPublic<Tags.LegendSyncAuditCommand>(app));
            registry.Register("StatusLegend",              app => StingCommandHandler.RunCommandPublic<Tags.StatusLegendCommand>(app));
            registry.Register("WorksetLegend",             app => StingCommandHandler.RunCommandPublic<Tags.WorksetLegendCommand>(app));

            // ── System param push ───────────────────────────────────────────
            registry.Register("SystemParamPush",           app => StingCommandHandler.RunCommandPublic<Tags.SystemParamPushCommand>(app));
            registry.Register("BatchSystemPush",           app => StingCommandHandler.RunCommandPublic<Tags.BatchSystemPushCommand>(app));
            registry.Register("SelectSystemElements",      app => StingCommandHandler.RunCommandPublic<Tags.SelectSystemElementsCommand>(app));

            // ── QR / Code ───────────────────────────────────────────────────
            registry.Register("QRCode",                    app => StingCommandHandler.RunCommandPublic<Tags.QRCodeCommand>(app));
            registry.Register("GenerateQRCode",            app => StingCommandHandler.RunCommandPublic<Tags.QRCodeCommand>(app));
            registry.Register("GenerateQRSheet",           app => StingCommandHandler.RunCommandPublic<Tags.QRCodeCommand>(app));
            registry.Register("PrintQRTags",               app => StingCommandHandler.RunCommandPublic<Tags.QRCodeCommand>(app));
            registry.Register("CodeLegend",                app => StingCommandHandler.RunCommandPublic<Tags.CodeLegendCommand>(app));

            // ── Validation / QA ─────────────────────────────────────────────
            registry.Register("ValidateTags",              app => StingCommandHandler.RunCommandPublic<Tags.ValidateTagsCommand>(app));
            registry.Register("CompletenessDashboard",     app => StingCommandHandler.RunCommandPublic<Tags.CompletenessDashboardCommand>(app));
            registry.Register("PreTagAudit",               app => StingCommandHandler.RunCommandPublic<Tags.PreTagAuditCommand>(app));
            registry.Register("ResolveAllIssues",          app => StingCommandHandler.RunCommandPublic<Tags.ResolveAllIssuesCommand>(app));
            registry.Register("DiscComplianceReport",      app => StingCommandHandler.RunCommandPublic<Tags.CompletenessDashboardCommand>(app));

            // ── Paragraph / Presentation ────────────────────────────────────
            registry.Register("SetParagraphDepth",         app => StingCommandHandler.RunCommandPublic<Tags.SetParagraphDepthCommand>(app));
            registry.Register("ToggleWarningVisibility",   app => StingCommandHandler.RunCommandPublic<Tags.ToggleWarningVisibilityCommand>(app));
            registry.Register("SetPresentationMode",       app => StingCommandHandler.RunCommandPublic<Tags.SetPresentationModeCommand>(app));
            registry.Register("ViewLabelSpec",             app => StingCommandHandler.RunCommandPublic<Tags.ViewLabelSpecCommand>(app));
            registry.Register("ExportLabelGuide",          app => StingCommandHandler.RunCommandPublic<Tags.ExportLabelGuideCommand>(app));
            registry.Register("SetTag7HeadingStyle",       app => StingCommandHandler.RunCommandPublic<Tags.SetTag7HeadingStyleCommand>(app));

            // ── Tag style engine commands ────────────────────────────────────
            registry.Register("ApplyTagStyles",            app => StingCommandHandler.RunCommandPublic<Tags.ApplyTagStylesCommand>(app));
            registry.Register("PreviewTagStyles",          app => StingCommandHandler.RunCommandPublic<Tags.PreviewTagStylesCommand>(app));
            registry.Register("SetTagStyleRule",           app => StingCommandHandler.RunCommandPublic<Tags.SetTagStyleRuleCommand>(app));
            registry.Register("SaveTagStylePreset",        app => StingCommandHandler.RunCommandPublic<Tags.SaveTagStylePresetCommand>(app));
            registry.Register("LoadTagStylePreset",        app => StingCommandHandler.RunCommandPublic<Tags.LoadTagStylePresetCommand>(app));
            registry.Register("ApplyParamStyles",          app => StingCommandHandler.RunCommandPublic<Tags.ApplyParamDrivenStylesCommand>(app));
            registry.Register("PreviewParamStyles",        app => StingCommandHandler.RunCommandPublic<Tags.PreviewParamDrivenStylesCommand>(app));
            registry.Register("ClearParamStyles",          app => StingCommandHandler.RunCommandPublic<Tags.ClearParamDrivenStylesCommand>(app));
            registry.Register("BatchParamStyles",          app => StingCommandHandler.RunCommandPublic<Tags.BatchApplyParamDrivenStylesCommand>(app));
            registry.Register("SwitchTagStyleByDisc",      app => StingCommandHandler.RunCommandPublic<Tags.SwitchTagStyleByDiscCommand>(app));
            registry.Register("BatchApplyColorScheme",     app => StingCommandHandler.RunCommandPublic<Tags.BatchApplyColorSchemeCommand>(app));
            registry.Register("ColorByVariable",           app => StingCommandHandler.RunCommandPublic<Tags.ColorByVariableCommand>(app));
            registry.Register("SetBoxColor",               app => StingCommandHandler.RunCommandPublic<Tags.SetBoxColorCommand>(app));
            registry.Register("ApplyParagraphPreset",      app => StingCommandHandler.RunCommandPublic<Tags.ApplyParagraphPresetCommand>(app));
            registry.Register("SetHandoverMode",           app => StingCommandHandler.RunCommandPublic<Tags.SetHandoverModeCommand>(app));

            // ── Shared params / tokens ──────────────────────────────────────
            registry.Register("LoadSharedParams",          app => StingCommandHandler.RunCommandPublic<Tags.LoadSharedParamsCommand>(app));
            registry.Register("FixContainers",             app => StingCommandHandler.RunCommandPublic<Tags.LoadSharedParamsCommand>(app));

            // ── Tag Studio aliases ──────────────────────────────────────────
            registry.Register("TagStudio_SmartPlace",      app => StingCommandHandler.RunCommandPublic<Tags.SmartPlaceTagsCommand>(app));
            registry.Register("TagStudio_Arrange",         app => StingCommandHandler.RunCommandPublic<Tags.ArrangeTagsCommand>(app));
            registry.Register("TagStudio_AlignBands",      app => StingCommandHandler.RunCommandPublic<Tags.AlignTagBandsCommand>(app));
            registry.Register("TagStudio_AdjustElbows",    app => StingCommandHandler.RunCommandPublic<Tags.AdjustElbowsCommand>(app));
            registry.Register("TagStudio_SetArrows",       app => StingCommandHandler.RunCommandPublic<Tags.SetArrowheadStyleCommand>(app));
            registry.Register("TagStudio_APIGaps",         app => StingCommandHandler.RunCommandPublic<Tags.PreTagAuditCommand>(app));
            registry.Register("TagStudio_Explain",         app => StingCommandHandler.RunCommandPublic<Tags.ValidateTagsCommand>(app));
            registry.Register("TagStudio_Pipeline",        app => StingCommandHandler.RunCommandPublic<Tags.CompletenessDashboardCommand>(app));
            registry.Register("TagStudio_Generate",        app => StingCommandHandler.RunCommandPublic<Tags.FamilyStagePopulateCommand>(app));
            registry.Register("TagStudio_GapReview",       app => StingCommandHandler.RunCommandPublic<Tags.ResolveAllIssuesCommand>(app));
            registry.Register("TagStudio_ApplyStyle",      app => StingCommandHandler.RunCommandPublic<Tags.ApplyTagStyleCommand>(app));
            registry.Register("TagStudio_ApplyScheme",     app => StingCommandHandler.RunCommandPublic<Tags.ApplyColorSchemeCommand>(app));
            registry.Register("TagStudio_ClearOverrides",  app => StingCommandHandler.RunCommandPublic<Tags.ClearColorSchemeCommand>(app));
            registry.Register("SwitchTagPos",              app => StingCommandHandler.RunCommandPublic<Tags.SwitchTagPositionCommand>(app));
            registry.Register("ExportTagPositions",        app => StingCommandHandler.RunCommandPublic<Tags.ExportTagPositionsCommand>(app));
            registry.Register("AlignTagBands",             app => StingCommandHandler.RunCommandPublic<Tags.AlignTagBandsCommand>(app));
            registry.Register("BatchPlaceLinkedTags",      app => StingCommandHandler.RunCommandPublic<Tags.BatchPlaceLinkedTagsCommand>(app));
            registry.Register("ExportLinkedManifest",      app => StingCommandHandler.RunCommandPublic<Tags.ExportLinkedModelManifestCommand>(app));
            registry.Register("Scale_ApplyTagSize",        app => StingCommandHandler.RunCommandPublic<Tags.SetScaleAwareTagSizeCommand>(app));
            registry.Register("SetViewTagStyle",           app => StingCommandHandler.RunCommandPublic<Tags.SetViewTagStyleCommand>(app));

            // ── Family tools ────────────────────────────────────────────────
            registry.Register("FamilyParamCreator",        app => StingCommandHandler.RunCommandPublic<Tags.FamilyParamCreatorCommand>(app));
            registry.Register("FamilyInjectAutomationPack",app => StingCommandHandler.RunCommandPublic<Tags.InjectAutomationPackCommand>(app));

            // ── Tag Intelligence ─────────────────────────────────────────────
            registry.Register("TagStudioAPIGaps",          app => StingCommandHandler.RunCommandPublic<Tags.PreTagAuditCommand>(app));
            registry.Register("TagStudioExplain",          app => StingCommandHandler.RunCommandPublic<Tags.ValidateTagsCommand>(app));
            registry.Register("TagStudioPipeline",         app => StingCommandHandler.RunCommandPublic<Tags.CompletenessDashboardCommand>(app));
            registry.Register("TagStudioGenerate",         app => StingCommandHandler.RunCommandPublic<Tags.FamilyStagePopulateCommand>(app));
            registry.Register("TagStudioGapReview",        app => StingCommandHandler.RunCommandPublic<Tags.ResolveAllIssuesCommand>(app));
            registry.Register("ConfigureTagFormat",        app => StingCommandHandler.RunCommandPublic<Tags.ConfigEditorCommand>(app));
            registry.Register("ConfigurableTagFormat",     app => StingCommandHandler.RunCommandPublic<Tags.ConfigurableTagFormatCommand>(app));
            registry.Register("NLPCommandProcessor",       app => StingCommandHandler.RunCommandPublic<Tags.NLPCommandProcessorCommand>(app));
            registry.Register("SmartTagSuggest",           app => StingCommandHandler.RunCommandPublic<Tags.SmartTagSuggestCommand>(app));
            registry.Register("TagAnalyticsDashboard",     app => StingCommandHandler.RunCommandPublic<Tags.TagAnalyticsDashboardCommand>(app));
            registry.Register("TagBatchChain",             app => StingCommandHandler.RunCommandPublic<Tags.TagBatchChainCommand>(app));
            registry.Register("TagPropagation",            app => StingCommandHandler.RunCommandPublic<Tags.TagPropagationCommand>(app));
            registry.Register("TagQualityAnalyzer",        app => StingCommandHandler.RunCommandPublic<Tags.TagQualityAnalyzerCommand>(app));
            registry.Register("TagRuleEngine",             app => StingCommandHandler.RunCommandPublic<Tags.TagRuleEngineCommand>(app));
            registry.Register("TagVersionControl",         app => StingCommandHandler.RunCommandPublic<Tags.TagVersionControlCommand>(app));
            registry.Register("BimKnowledgeBase",          app => StingCommandHandler.RunCommandPublic<Tags.BimKnowledgeBaseCommand>(app));
            registry.Register("CommandSuggestion",         app => StingCommandHandler.RunCommandPublic<Tags.CommandSuggestionCommand>(app));

            // ── Migration ────────────────────────────────────────────────────
            registry.Register("Tags_MigrateStyleCode",     app => StingCommandHandler.RunCommandPublic<Tags.MigrateTagStyleCodeCommand>(app));
        }
    }
}
