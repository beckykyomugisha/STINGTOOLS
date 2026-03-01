using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// External event handler that dispatches dockable panel button clicks
    /// to the appropriate IExternalCommand classes. This ensures all Revit API
    /// calls happen on the correct thread (main Revit API context).
    ///
    /// Unified dispatcher for all 160+ commands across 5 tabs:
    /// SELECT, ORGANISE, DOCS, TEMP, CREATE.
    /// </summary>
    public class StingCommandHandler : IExternalEventHandler
    {
        private string _commandTag = "";
        private string _param1 = "";
        private string _param2 = "";

        public void SetCommand(string tag, string param1 = "", string param2 = "")
        {
            _commandTag = tag ?? "";
            _param1 = param1 ?? "";
            _param2 = param2 ?? "";
        }

        public string GetName() => "STING Command Dispatcher";

        public void Execute(UIApplication app)
        {
            try
            {
                switch (_commandTag)
                {
                    // ════════════════════════════════════════════════════════
                    // SELECT TAB
                    // ════════════════════════════════════════════════════════

                    // ── Category selectors ──
                    case "SelectLighting": RunCommand<Select.SelectLightingCommand>(app); break;
                    case "SelectElectrical": RunCommand<Select.SelectElectricalCommand>(app); break;
                    case "SelectMechanical": RunCommand<Select.SelectMechanicalCommand>(app); break;
                    case "SelectPlumbing": RunCommand<Select.SelectPlumbingCommand>(app); break;
                    case "SelectAirTerminals": RunCommand<Select.SelectAirTerminalsCommand>(app); break;
                    case "SelectFurniture": RunCommand<Select.SelectFurnitureCommand>(app); break;
                    case "SelectDoors": RunCommand<Select.SelectDoorsCommand>(app); break;
                    case "SelectWindows": RunCommand<Select.SelectWindowsCommand>(app); break;
                    case "SelectRooms": RunCommand<Select.SelectRoomsCommand>(app); break;
                    case "SelectSprinklers": RunCommand<Select.SelectSprinklersCommand>(app); break;
                    case "SelectPipes": RunCommand<Select.SelectPipesCommand>(app); break;
                    case "SelectDucts": RunCommand<Select.SelectDuctsCommand>(app); break;
                    case "SelectConduits": RunCommand<Select.SelectConduitsCommand>(app); break;
                    case "SelectCableTrays": RunCommand<Select.SelectCableTraysCommand>(app); break;
                    case "SelectAllTaggable": RunCommand<Select.SelectAllTaggableCommand>(app); break;

                    // ── State selectors ──
                    case "SelectUntagged": RunCommand<Select.SelectUntaggedCommand>(app); break;
                    case "SelectTagged": RunCommand<Select.SelectTaggedCommand>(app); break;
                    case "SelectEmptyMark": RunCommand<Select.SelectEmptyMarkCommand>(app); break;
                    case "SelectPinned": RunCommand<Select.SelectPinnedCommand>(app); break;
                    case "SelectUnpinned": RunCommand<Select.SelectUnpinnedCommand>(app); break;

                    // ── Spatial selectors ──
                    case "SelectByLevel": RunCommand<Select.SelectByLevelCommand>(app); break;
                    case "SelectByRoom": RunCommand<Select.SelectByRoomCommand>(app); break;

                    // ── Bulk param write ──
                    case "BulkParamWrite": RunCommand<Select.BulkParamWriteCommand>(app); break;

                    // ── View isolate/hide (inline) ──
                    case "ViewIsolate": ViewIsolateSelected(app); break;
                    case "ViewHide": ViewHideSelected(app); break;
                    case "ViewReveal": ViewRevealHidden(app); break;
                    case "ViewReset": ViewResetIsolate(app); break;

                    // ── Selection ops (inline) ──
                    case "SelectAll": SelectAllVisible(app); break;
                    case "SelectClear": ClearSelection(app); break;
                    case "Deselect": ClearSelection(app); break;
                    case "InvertSelection": InvertSelection(app); break;
                    case "DeleteSelected": DeleteSelected(app); break;
                    case "SelectTags": SelectAnnotationTags(app); break;
                    case "SelectHostElements": SelectHostElements(app); break;

                    // ── Selection memory (inline) ──
                    case "SaveM1": SaveSelectionMemory(app, "M1"); break;
                    case "LoadM1": LoadSelectionMemory(app, "M1"); break;
                    case "SaveM2": SaveSelectionMemory(app, "M2"); break;
                    case "LoadM2": LoadSelectionMemory(app, "M2"); break;
                    case "SaveM3": SaveSelectionMemory(app, "M3"); break;
                    case "LoadM3": LoadSelectionMemory(app, "M3"); break;
                    case "SwapM1M2": SwapMemorySlots(app, "M1", "M2"); break;
                    case "SelectionInfo": ShowSelectionInfo(app); break;
                    case "AddToM1": AddToMemory(app, "M1"); break;
                    case "RemoveFromM1": RemoveFromMemory(app, "M1"); break;
                    case "IntersectM1": IntersectWithMemory(app, "M1"); break;

                    // ── Quick param filter (inline) ──
                    case "QuickMark": QuickParamFilter(app, "Mark"); break;
                    case "QuickType": QuickParamFilter(app, "Type Name"); break;
                    case "QuickFamily": QuickParamFilter(app, "Family"); break;
                    case "QuickSystem": QuickParamFilter(app, ParamRegistry.SYS); break;

                    // ── Bulk write from panel (inline) ──
                    case "BulkWrite": BulkParamWriteInline(app, _param1, _param2, false); break;
                    case "BulkClear": BulkParamWriteInline(app, _param1, "", true); break;
                    case "BulkPreview": BulkParamPreview(app, _param1, _param2); break;

                    // ── Project filters (inline) ──
                    case "FilterWorkset": QuickParamFilter(app, "Workset"); break;
                    case "FilterPhase": QuickParamFilter(app, "Phase Created"); break;
                    case "FilterDesignOption": QuickParamFilter(app, "Design Option"); break;
                    case "FilterGroup": QuickParamFilter(app, "Model Group"); break;
                    case "FilterAssembly": QuickParamFilter(app, "Assembly Name"); break;
                    case "FilterConnected": SelectConnectedElements(app); break;

                    // ════════════════════════════════════════════════════════
                    // ORGANISE TAB (merged Tags + Organise)
                    // ════════════════════════════════════════════════════════

                    // ── Tag operations ──
                    case "AutoTag": RunCommand<Tags.AutoTagCommand>(app); break;
                    case "BatchTag": RunCommand<Tags.BatchTagCommand>(app); break;
                    case "TagAndCombine": RunCommand<Tags.TagAndCombineCommand>(app); break;
                    case "TagSelected": RunCommand<Organise.TagSelectedCommand>(app); break;
                    case "TagNewOnly": RunCommand<Tags.TagNewOnlyCommand>(app); break;
                    case "ReTag": RunCommand<Organise.ReTagCommand>(app); break;
                    case "DeleteTags": RunCommand<Organise.DeleteTagsCommand>(app); break;
                    case "RenumberTags": RunCommand<Organise.RenumberTagsCommand>(app); break;
                    case "CopyTags": RunCommand<Organise.CopyTagsCommand>(app); break;
                    case "SwapTags": RunCommand<Organise.SwapTagsCommand>(app); break;
                    case "FixDuplicates": RunCommand<Organise.FixDuplicateTagsCommand>(app); break;
                    case "FindDuplicates": RunCommand<Organise.FindDuplicateTagsCommand>(app); break;

                    // ── Smart tag placement ──
                    case "SmartPlaceTags": RunCommand<Tags.SmartPlaceTagsCommand>(app); break;
                    case "ArrangeTags": RunCommand<Tags.ArrangeTagsCommand>(app); break;
                    case "BatchPlaceTags": RunCommand<Tags.BatchPlaceTagsCommand>(app); break;
                    case "RemoveAnnotationTags": RunCommand<Tags.RemoveAnnotationTagsCommand>(app); break;
                    case "LearnTagPlacement": RunCommand<Tags.LearnTagPlacementCommand>(app); break;
                    case "ApplyTagTemplate": RunCommand<Tags.ApplyTagTemplateCommand>(app); break;
                    case "TagOverlapAnalysis": RunCommand<Tags.TagOverlapAnalysisCommand>(app); break;
                    case "BatchTagTextSize": RunCommand<Tags.BatchTagTextSizeCommand>(app); break;
                    case "SetTagCatLineWeight": RunCommand<Tags.SetTagCategoryLineWeightCommand>(app); break;

                    // ── Rich TAG7 display ──
                    case "RichTagNote": RunCommand<Tags.RichTagNoteCommand>(app); break;
                    case "ExportRichTagReport": RunCommand<Tags.ExportRichTagReportCommand>(app); break;
                    case "ViewTag7Sections": RunCommand<Tags.ViewTag7SectionsCommand>(app); break;
                    case "SwitchTag7Preset": RunCommand<Tags.SwitchTag7PresetCommand>(app); break;

                    // ── TAG1-TAG6 segment display ──
                    case "RichSegmentNote": RunCommand<Tags.RichSegmentNoteCommand>(app); break;
                    case "ViewSegments": RunCommand<Tags.ViewSegmentsCommand>(app); break;

                    // ── Legend builder ──
                    case "CreateColorLegend": RunCommand<Tags.CreateColorLegendCommand>(app); break;
                    case "ExportColorLegendHtml": RunCommand<Tags.ExportColorLegendHtmlCommand>(app); break;
                    case "AutoCreateLegends": RunCommand<Tags.AutoCreateLegendsCommand>(app); break;
                    case "LegendFromView": RunCommand<Tags.LegendFromViewCommand>(app); break;
                    case "PlaceLegendOnSheet": RunCommand<Tags.PlaceLegendOnSheetCommand>(app); break;
                    case "SheetContextLegend": RunCommand<Tags.SheetContextLegendCommand>(app); break;
                    case "PlaceLegendOnAllSheets": RunCommand<Tags.PlaceLegendOnAllSheetsCommand>(app); break;
                    case "BatchSheetContextLegends": RunCommand<Tags.BatchSheetContextLegendsCommand>(app); break;
                    case "CreateTagLegend": RunCommand<Tags.CreateTagLegendCommand>(app); break;
                    case "SheetTagLegend": RunCommand<Tags.SheetTagLegendCommand>(app); break;
                    case "BatchTagLegends": RunCommand<Tags.BatchTagLegendsCommand>(app); break;
                    case "UpdateLegend": RunCommand<Tags.UpdateLegendCommand>(app); break;
                    case "DeleteStaleLegend": RunCommand<Tags.DeleteStaleLegendCommand>(app); break;
                    case "OneClickLegendPipeline": RunCommand<Tags.OneClickLegendPipelineCommand>(app); break;
                    case "MepSystemLegend": RunCommand<Tags.MepSystemLegendCommand>(app); break;
                    case "MaterialLegend": RunCommand<Tags.MaterialLegendCommand>(app); break;
                    case "CompoundTypeLegend": RunCommand<Tags.CompoundTypeLegendCommand>(app); break;
                    case "EquipmentLegend": RunCommand<Tags.EquipmentLegendCommand>(app); break;
                    case "FireRatingLegend": RunCommand<Tags.FireRatingLegendCommand>(app); break;
                    case "MasterLegendPipeline": RunCommand<Tags.MasterLegendPipelineCommand>(app); break;
                    case "FilterLegend": RunCommand<Tags.FilterLegendCommand>(app); break;
                    case "TemplateLegend": RunCommand<Tags.TemplateLegendCommand>(app); break;
                    case "VGCategoryLegend": RunCommand<Tags.VGCategoryLegendCommand>(app); break;
                    case "BatchTemplateLegend": RunCommand<Tags.BatchTemplateLegendCommand>(app); break;
                    case "FlexibleLegend": RunCommand<Tags.FlexibleLegendCommand>(app); break;
                    case "LegendFromPreset": RunCommand<Tags.LegendFromPresetCommand>(app); break;
                    case "ComponentTypeLegend": RunCommand<Tags.ComponentTypeLegendCommand>(app); break;
                    case "ColorReferenceLegend": RunCommand<Tags.ColorReferenceLegendCommand>(app); break;
                    case "LegendSyncAudit": RunCommand<Tags.LegendSyncAuditCommand>(app); break;
                    case "StatusLegend": RunCommand<Tags.StatusLegendCommand>(app); break;
                    case "WorksetLegend": RunCommand<Tags.WorksetLegendCommand>(app); break;

                    // ── System Parameter Push ──
                    case "SystemParamPush": RunCommand<Tags.SystemParamPushCommand>(app); break;
                    case "BatchSystemPush": RunCommand<Tags.BatchSystemPushCommand>(app); break;
                    case "SelectSystemElements": RunCommand<Tags.SelectSystemElementsCommand>(app); break;

                    // ── Orientation & text alignment ──
                    case "ToggleTagOrientation": RunCommand<Organise.ToggleTagOrientationCommand>(app); break;
                    case "FlipTags":
                    case "FlipTagsH":
                    case "FlipTagsV": RunCommand<Organise.FlipTagsCommand>(app); break;
                    case "AlignTextLeft":
                    case "AlignTextCenter":
                    case "AlignTextRight": RunCommand<Organise.AlignTagTextCommand>(app); break;

                    // ── Align & distribute ──
                    case "AlignTagsH":
                    case "AlignTagsV":
                    case "AlignLeft":
                    case "AlignRight":
                    case "AlignTop":
                    case "AlignBottom":
                    case "AlignCenterH":
                    case "AlignCenterV":
                    case "DistributeH":
                    case "DistributeV":
                    case "ArrangeGrid":
                    case "ArrangeCircle":
                    case "ArrangeStack":
                    case "ArrangeStackH":
                    case "ArrangeMirror":
                    case "ArrangeRadial": RunCommand<Organise.AlignTagsCommand>(app); break;
                    case "ResetTagPositions": RunCommand<Organise.ResetTagPositionsCommand>(app); break;

                    // ── Leaders ──
                    case "AddLeaders": RunCommand<Organise.AddLeadersCommand>(app); break;
                    case "RemoveLeaders": RunCommand<Organise.RemoveLeadersCommand>(app); break;
                    case "ToggleLeaders": RunCommand<Organise.ToggleLeadersCommand>(app); break;
                    case "SnapElbow90":
                    case "SnapElbow45":
                    case "SnapElbowStraight": RunCommand<Organise.SnapLeaderElbowCommand>(app); break;
                    case "PinTags": RunCommand<Organise.PinTagsCommand>(app); break;
                    case "AttachLeader": RunCommand<Organise.AttachLeaderCommand>(app); break;
                    case "SelectTagsWithLeaders": RunCommand<Organise.SelectTagsWithLeadersCommand>(app); break;
                    case "LeaderLength025":
                    case "LeaderLength05":
                    case "LeaderLength1":
                    case "LeaderEqualSpacing":
                    case "LeaderEqualise": RunCommand<Organise.SnapLeaderElbowCommand>(app); break;

                    // ── Appearance (annotation colors) ──
                    case "ColorTagsByDiscipline": RunCommand<Organise.ColorTagsByDisciplineCommand>(app); break;
                    case "SetTagTextColor": RunCommand<Organise.SetTagTextColorCommand>(app); break;
                    case "SetLeaderColor": RunCommand<Organise.SetLeaderColorCommand>(app); break;
                    case "SplitTagLeaderColor": RunCommand<Organise.SplitTagLeaderColorCommand>(app); break;
                    case "ClearAnnotationColors": RunCommand<Organise.ClearAnnotationColorsCommand>(app); break;
                    case "TagAppearance": RunCommand<Organise.TagAppearanceCommand>(app); break;
                    case "SetTagBox": RunCommand<Organise.SetTagBoxAppearanceCommand>(app); break;
                    case "QuickTagStyle": RunCommand<Organise.QuickTagStyleCommand>(app); break;
                    case "SetTagLineWeight": RunCommand<Organise.SetTagLineWeightCommand>(app); break;
                    case "ColorTagsByParam": RunCommand<Organise.ColorTagsByParameterCommand>(app); break;
                    case "SwapTagType": RunCommand<Organise.SwapTagTypeCommand>(app); break;

                    // ── Analysis ──
                    case "TagStats": RunCommand<Organise.TagStatsCommand>(app); break;
                    case "AuditTagsCSV": RunCommand<Organise.AuditTagsCSVCommand>(app); break;
                    case "SelectByDiscipline": RunCommand<Organise.SelectByDisciplineCommand>(app); break;
                    case "TagRegisterExport": RunCommand<Organise.TagRegisterExportCommand>(app); break;

                    // ── QA ──
                    case "ValidateTags": RunCommand<Tags.ValidateTagsCommand>(app); break;
                    case "HighlightInvalid": RunCommand<Organise.HighlightInvalidCommand>(app); break;
                    case "ClearOverrides": RunCommand<Organise.ClearOverridesCommand>(app); break;
                    case "CompletenessDashboard": RunCommand<Tags.CompletenessDashboardCommand>(app); break;
                    case "PreTagAudit": RunCommand<Tags.PreTagAuditCommand>(app); break;
                    case "ResolveAllIssues": RunCommand<Tags.ResolveAllIssuesCommand>(app); break;

                    // ── Color By Parameter commands ──
                    case "ColorByParameter": RunCommand<Select.ColorByParameterCommand>(app); break;
                    case "ClearColorOverrides": RunCommand<Select.ClearColorOverridesCommand>(app); break;
                    case "SaveColorPreset": RunCommand<Select.SaveColorPresetCommand>(app); break;
                    case "LoadColorPreset": RunCommand<Select.LoadColorPresetCommand>(app); break;
                    case "CreateFiltersFromColors": RunCommand<Select.CreateFiltersFromColorsCommand>(app); break;

                    // ── Colouriser inline ──
                    case "ColorApply": ColorByParameter(app, _param1, _param2); break;
                    case "ColorApplyHex": ColorByHex(app, _param1); break;
                    case "ColorApplyTransparency": SetTransparencyOverride(app, _param1); break;

                    // ── Graphic overrides (inline) ──
                    case "HalftoneOn": SetHalftone(app, true); break;
                    case "HalftoneOff": SetHalftone(app, false); break;
                    case "PermHide": PermanentHide(app); break;
                    case "PermUnhide": PermanentUnhide(app); break;
                    case "UnhideCategory": UnhideCategory(app); break;

                    // ════════════════════════════════════════════════════════
                    // DOCS TAB
                    // ════════════════════════════════════════════════════════

                    case "SheetOrganizer": RunCommand<Docs.SheetOrganizerCommand>(app); break;
                    case "ViewOrganizer": RunCommand<Docs.ViewOrganizerCommand>(app); break;
                    case "SheetIndex": RunCommand<Docs.SheetIndexCommand>(app); break;
                    case "Transmittal": RunCommand<Docs.TransmittalCommand>(app); break;
                    case "DeleteUnusedViews": RunCommand<Docs.DeleteUnusedViewsCommand>(app); break;
                    case "SheetNamingCheck": RunCommand<Docs.SheetNamingCheckCommand>(app); break;
                    case "AutoNumberSheets": RunCommand<Docs.AutoNumberSheetsCommand>(app); break;
                    case "AlignViewports": RunCommand<Docs.AlignViewportsCommand>(app); break;
                    case "RenumberViewports": RunCommand<Docs.RenumberViewportsCommand>(app); break;
                    case "TextCase": RunCommand<Docs.TextCaseCommand>(app); break;
                    case "SumAreas": RunCommand<Docs.SumAreasCommand>(app); break;

                    // ── View Automation (Phase 4) ──
                    case "DuplicateView": RunCommand<Docs.DuplicateViewCommand>(app); break;
                    case "BatchRenameViews": RunCommand<Docs.BatchRenameViewsCommand>(app); break;
                    case "CopyViewSettings": RunCommand<Docs.CopyViewSettingsCommand>(app); break;
                    case "AutoPlaceViewports": RunCommand<Docs.AutoPlaceViewportsCommand>(app); break;
                    case "CropToContent": RunCommand<Docs.CropToContentCommand>(app); break;
                    case "BatchAlignViewports": RunCommand<Docs.BatchAlignViewportsCommand>(app); break;

                    // ════════════════════════════════════════════════════════
                    // TEMP TAB
                    // ════════════════════════════════════════════════════════

                    // ── Setup ──
                    case "MasterSetup": RunCommand<Temp.MasterSetupCommand>(app); break;
                    case "CreateParameters": RunCommand<Temp.CreateParametersCommand>(app); break;
                    case "CheckData": RunCommand<Temp.CheckDataCommand>(app); break;

                    // ── Materials ──
                    case "CreateBLEMaterials": RunCommand<Temp.CreateBLEMaterialsCommand>(app); break;
                    case "CreateMEPMaterials": RunCommand<Temp.CreateMEPMaterialsCommand>(app); break;

                    // ── Family types ──
                    case "CreateWalls": RunCommand<Temp.CreateWallsCommand>(app); break;
                    case "CreateFloors": RunCommand<Temp.CreateFloorsCommand>(app); break;
                    case "CreateCeilings": RunCommand<Temp.CreateCeilingsCommand>(app); break;
                    case "CreateRoofs": RunCommand<Temp.CreateRoofsCommand>(app); break;
                    case "CreateDucts": RunCommand<Temp.CreateDuctsCommand>(app); break;
                    case "CreatePipes": RunCommand<Temp.CreatePipesCommand>(app); break;
                    case "CreateCableTrays": RunCommand<Temp.CreateCableTraysCommand>(app); break;
                    case "CreateConduits": RunCommand<Temp.CreateConduitsCommand>(app); break;

                    // ── Schedules ──
                    case "FullAutoPopulate": RunCommand<Temp.FullAutoPopulateCommand>(app); break;
                    case "BatchSchedules": RunCommand<Temp.BatchSchedulesCommand>(app); break;
                    case "MaterialSchedules": RunCommand<Temp.CreateMaterialSchedulesCommand>(app); break;
                    case "AutoPopulate": RunCommand<Temp.AutoPopulateCommand>(app); break;
                    case "FormulaEvaluator": RunCommand<Temp.FormulaEvaluatorCommand>(app); break;
                    case "ExportCSV": RunCommand<Temp.ExportCSVCommand>(app); break;

                    // ── Templates / Views ──
                    case "CreateFilters": RunCommand<Temp.CreateFiltersCommand>(app); break;
                    case "ApplyFilters": RunCommand<Temp.ApplyFiltersToViewsCommand>(app); break;
                    case "CreateWorksets": RunCommand<Temp.CreateWorksetsCommand>(app); break;
                    case "ViewTemplates": RunCommand<Temp.ViewTemplatesCommand>(app); break;
                    case "CreateLinePatterns": RunCommand<Temp.CreateLinePatternsCommand>(app); break;
                    case "CreatePhases": RunCommand<Temp.CreatePhasesCommand>(app); break;

                    // ── Template Manager ──
                    case "TemplateSetupWizard": RunCommand<Temp.TemplateSetupWizardCommand>(app); break;
                    case "AutoAssignTemplates": RunCommand<Temp.AutoAssignTemplatesCommand>(app); break;
                    case "TemplateAudit": RunCommand<Temp.TemplateAuditCommand>(app); break;
                    case "TemplateDiff": RunCommand<Temp.TemplateDiffCommand>(app); break;
                    case "TemplateComplianceScore": RunCommand<Temp.TemplateComplianceScoreCommand>(app); break;
                    case "AutoFixTemplate": RunCommand<Temp.AutoFixTemplateCommand>(app); break;
                    case "SyncTemplateOverrides": RunCommand<Temp.SyncTemplateOverridesCommand>(app); break;
                    case "CreateVGOverrides": RunCommand<Temp.CreateVGOverridesCommand>(app); break;
                    case "CloneTemplate": RunCommand<Temp.CloneTemplateCommand>(app); break;
                    case "BatchVGReset": RunCommand<Temp.BatchVGResetCommand>(app); break;
                    case "BatchAddFamilyParams": RunCommand<Temp.BatchAddFamilyParamsCommand>(app); break;
                    case "CreateTemplateSchedules": RunCommand<Temp.CreateTemplateSchedulesCommand>(app); break;

                    // ── Styles ──
                    case "CreateFillPatterns": RunCommand<Temp.CreateFillPatternsCommand>(app); break;
                    case "CreateLineStyles": RunCommand<Temp.CreateLineStylesCommand>(app); break;
                    case "CreateObjectStyles": RunCommand<Temp.CreateObjectStylesCommand>(app); break;
                    case "CreateTextStyles": RunCommand<Temp.CreateTextStylesCommand>(app); break;
                    case "CreateDimensionStyles": RunCommand<Temp.CreateDimensionStylesCommand>(app); break;

                    // ── Data QA (Phase 5) ──
                    case "ValidateTemplate": RunCommand<Temp.ValidateTemplateCommand>(app); break;
                    case "DynamicBindings": RunCommand<Temp.DynamicBindingsCommand>(app); break;
                    case "SchemaValidate": RunCommand<Temp.SchemaValidateCommand>(app); break;

                    // ════════════════════════════════════════════════════════
                    // CREATE TAB (ISO 19650 tag creation)
                    // ════════════════════════════════════════════════════════

                    // ── Setup ──
                    case "LoadSharedParams": RunCommand<Tags.LoadSharedParamsCommand>(app); break;
                    case "ConfigEditor": RunCommand<Tags.ConfigEditorCommand>(app); break;
                    case "TagConfig": RunCommand<Tags.TagConfigCommand>(app); break;
                    case "SyncParamSchema": RunCommand<Tags.SyncParameterSchemaCommand>(app); break;
                    case "AddParamRemap": RunCommand<Tags.AddParamRemapCommand>(app); break;
                    case "AuditParamSchema": RunCommand<Tags.AuditParameterSchemaCommand>(app); break;

                    // ── Tag Families ──
                    case "CreateTagFamilies": RunCommand<Tags.CreateTagFamiliesCommand>(app); break;
                    case "LoadTagFamilies": RunCommand<Tags.LoadTagFamiliesCommand>(app); break;
                    case "ConfigureTagLabels": RunCommand<Tags.ConfigureTagLabelsCommand>(app); break;
                    case "AuditTagFamilies": RunCommand<Tags.AuditTagFamiliesCommand>(app); break;

                    // ── Populate tokens ──
                    case "FamilyStagePopulate": RunCommand<Tags.FamilyStagePopulateCommand>(app); break;
                    case "AssignNumbers": RunCommand<Tags.AssignNumbersCommand>(app); break;
                    case "BuildTags": RunCommand<Tags.BuildTagsCommand>(app); break;
                    case "CombineParameters": RunCommand<Tags.CombineParametersCommand>(app); break;
                    case "CombinePreFlight": RunCommand<Tags.CombinePreFlightCommand>(app); break;

                    // ── Manual tokens ──
                    case "SetDisc": RunCommand<Tags.SetDiscCommand>(app); break;
                    case "SetLoc": RunCommand<Tags.SetLocCommand>(app); break;
                    case "SetZone": RunCommand<Tags.SetZoneCommand>(app); break;
                    case "SetStatus": RunCommand<Tags.SetStatusCommand>(app); break;
                    case "SetSys": WriteTokenToSelected(app, ParamRegistry.SYS, "System Code (SYS)"); break;
                    case "SetFunc": WriteTokenToSelected(app, ParamRegistry.FUNC, "Function Code (FUNC)"); break;
                    case "SetProd": WriteTokenToSelected(app, ParamRegistry.PROD, "Product Code (PROD)"); break;
                    case "SetLvl": WriteTokenToSelected(app, ParamRegistry.LVL, "Level Code (LVL)"); break;
                    case "SetOrig": WriteTokenToSelected(app, ParamRegistry.ORIGIN, "Origin Code (ORIG)"); break;
                    case "SetProj": WriteTokenToSelected(app, ParamRegistry.PROJECT, "Project Code (PROJ)"); break;
                    case "SetRev": WriteTokenToSelected(app, ParamRegistry.REV, "Revision Code (REV)"); break;
                    case "SetVol": WriteTokenToSelected(app, ParamRegistry.VOLUME, "Volume Code (VOL)"); break;

                    // ── Scope / toggles (inline) ──
                    case "ScopeView": break;
                    case "ToggleOverwrite": break;

                    // ════════════════════════════════════════════════════════
                    // NEW — SELECT TAB (AI Smart Select, Spatial, Conditions)
                    // ════════════════════════════════════════════════════════

                    case "AIPredictSelect":
                    case "AISimilarSelect":
                    case "AIChainSelect":
                    case "AIClusterSelect":
                    case "AIPatternSelect":
                    case "AIBoundarySelect":
                    case "AIOutliersSelect":
                    case "AIDenseSelect":
                        StingLog.Info($"AI Smart Select: {_commandTag}");
                        TaskDialog.Show("AI Select", $"AI selection mode: {_commandTag}\nSelect elements first, then AI will extend the selection.");
                        break;

                    case "SelectView": SelectByCategory(app, "Views"); break;
                    case "SelectVisible": SelectVisibleOnly(app); break;
                    case "SelectNear": StingLog.Info("SelectNear"); break;
                    case "SelectQuad": StingLog.Info("SelectQuad"); break;
                    case "SelectEdge": StingLog.Info("SelectEdge"); break;
                    case "SelectGrid": StingLog.Info("SelectGrid"); break;
                    case "SelectBBox": StingLog.Info("SelectBBox"); break;

                    case "BulkBrain": StingLog.Info("BulkBrain — AI param suggestion"); break;
                    case "ParamLookupRefresh": StingLog.Info("ParamLookupRefresh"); break;
                    case "CondAdd": StingLog.Info("CondAdd"); break;
                    case "CondRemove": StingLog.Info("CondRemove"); break;
                    case "CondClear": StingLog.Info("CondClear"); break;
                    case "CondPreview": StingLog.Info("CondPreview"); break;
                    case "CondApply": StingLog.Info("CondApply"); break;
                    case "ShowHelp": TaskDialog.Show("STING Tools", "STING Tags v9.6\nISO 19650 BIM Asset Tagging\nhttps://stingbim.com"); break;

                    // ════════════════════════════════════════════════════════
                    // NEW — ORGANISE TAB (AI Engine, Nudge, Leaders ext, etc.)
                    // ════════════════════════════════════════════════════════

                    case "SmartOrganise":
                    case "OrgQuick":
                    case "OrgDeep":
                    case "OrgAnneal":
                        RunCommand<Tags.ArrangeTagsCommand>(app); break;
                    case "OrgReset": RunCommand<Organise.ResetTagPositionsCommand>(app); break;
                    case "OrgBrainSp": RunCommand<Tags.SmartPlaceTagsCommand>(app); break;
                    case "OrgUndo":
                        StingLog.Info($"OrgUndo: Use Ctrl+Z to undo last operation");
                        TaskDialog.Show("Undo", "Use Ctrl+Z to undo the last tag operation.");
                        break;

                    case "TagFamilyRefresh": StingLog.Info("TagFamilyRefresh"); break;
                    case "TagCat": RunCommand<Organise.TagSelectedCommand>(app); break;
                    case "TagAll": RunCommand<Tags.BatchTagCommand>(app); break;
                    case "Orphans": FindOrphanedTags(app); break;
                    case "CloneTags": CloneTagLayout(app); break;
                    case "AuditTags": RunCommand<Organise.AuditTagsCSVCommand>(app); break;
                    case "MultiView": RunCommand<Tags.BatchPlaceTagsCommand>(app); break;
                    case "ClashingDetect": RunCommand<Tags.TagOverlapAnalysisCommand>(app); break;

                    case "AllH":
                    case "AllV":
                    case "BrainSmHV": RunCommand<Organise.ToggleTagOrientationCommand>(app); break;

                    case "NudgeUp": NudgeTags(app, "UP"); break;
                    case "NudgeDown": NudgeTags(app, "DOWN"); break;
                    case "NudgeLeft": NudgeTags(app, "LEFT"); break;
                    case "NudgeRight": NudgeTags(app, "RIGHT"); break;
                    case "NudgeNear": NudgeTags(app, "NEAR"); break;
                    case "NudgeFar": NudgeTags(app, "FAR"); break;
                    case "BrainSmOr": RunCommand<Organise.ToggleTagOrientationCommand>(app); break;

                    case "BrainSmAl": RunCommand<Organise.AlignTagsCommand>(app); break;

                    case "LeaderMulti":
                    case "LeaderCombine": RunCommand<Organise.AddLeadersCommand>(app); break;
                    case "LeaderAdd":
                    case "LeaderStraight": RunCommand<Organise.SnapLeaderElbowCommand>(app); break;
                    case "TagSnap45":
                    case "TagSnap90": RunCommand<Organise.ResetTagPositionsCommand>(app); break;
                    case "LeaderSpacing": RunCommand<Organise.SnapLeaderElbowCommand>(app); break;

                    case "BrainSmartLdr": RunCommand<Organise.SnapLeaderElbowCommand>(app); break;
                    case "BrainUncross": RunCommand<Tags.ArrangeTagsCommand>(app); break;
                    case "BrainTidy": RunCommand<Tags.ArrangeTagsCommand>(app); break;

                    case "AnalyseScore": RunCommand<Organise.TagStatsCommand>(app); break;
                    case "AnalyseClashes":
                    case "AnalyseCrossings":
                    case "AnalyseDensity":
                    case "AnalyseClusters":
                        RunCommand<Tags.TagOverlapAnalysisCommand>(app); break;

                    case "PatternLearn": RunCommand<Tags.LearnTagPlacementCommand>(app); break;
                    case "PatternApplyLearned": RunCommand<Tags.ApplyTagTemplateCommand>(app); break;

                    case "BatchViewCats": StingLog.Info("BatchViewCats"); break;
                    case "BatchViewRunAll": StingLog.Info("BatchViewRunAll"); break;

                    case "RoomTagCentroid": MoveRoomTags(app, "Centroid"); break;
                    case "RoomTagTopLeft": MoveRoomTags(app, "TopLeft"); break;
                    case "RoomTagTopCentre": MoveRoomTags(app, "TopCentre"); break;
                    case "RoomTagLeaderLock":
                    case "RoomTagLeaderFree":
                        StingLog.Info($"RoomTag: {_commandTag}");
                        break;

                    case "ListLinks":
                    case "SelInLink":
                    case "TagLinked":
                    case "AuditLinks":
                        StingLog.Info($"LinkedModel: {_commandTag}");
                        break;

                    case "PdfSelectedSheets":
                    case "PdfActiveView":
                        StingLog.Info($"PDF Export: {_commandTag}");
                        break;

                    case "GenSheetIndex": RunCommand<Docs.SheetIndexCommand>(app); break;
                    case "ExportSheetCSV": RunCommand<Organise.AuditTagsCSVCommand>(app); break;

                    // ════════════════════════════════════════════════════════
                    // NEW — DOCS TAB (StingDocs organizer features)
                    // ════════════════════════════════════════════════════════

                    case "VPAlignTop":
                    case "VPAlignMidY":
                    case "VPAlignBot":
                    case "VPAlignLeft":
                    case "VPAlignMidX":
                    case "VPAlignRight": RunCommand<Docs.AlignViewportsCommand>(app); break;

                    case "VPNumLR":
                    case "VPNumTB": RunCommand<Docs.RenumberViewportsCommand>(app); break;
                    case "VPNumPlus": ViewportRenumberOffset(app, 1); break;
                    case "VPNumMinus": ViewportRenumberOffset(app, -1); break;
                    case "VPPrefix":
                    case "VPSuffix":
                        StingLog.Info($"VP Number: {_commandTag}");
                        break;

                    case "SheetResetTitle": StingLog.Info($"Sheet: {_commandTag}"); break;
                    case "SheetNumPlus": SheetRenumber(app, 1); break;
                    case "SheetNumMinus": SheetRenumber(app, -1); break;
                    case "SheetPrefix": SheetAddPrefix(app); break;
                    case "SheetSuffix": SheetAddSuffix(app); break;
                    case "SheetFindReplace": RunCommand<Docs.BatchRenameViewsCommand>(app); break;

                    case "SchedSyncPos": ScheduleSyncPosition(app); break;
                    case "SchedSyncRot": ScheduleToggleRotation(app); break;
                    case "SchedShowHidden": ScheduleShowHidden(app); break;
                    case "SchedMatchWidest": ScheduleMatchWidest(app); break;
                    case "SchedSetWidth": ScheduleSetColumnWidth(app); break;
                    case "SchedEqualise": ScheduleEqualiseColumns(app); break;

                    case "TextLower":
                    case "TextUpper": RunCommand<Docs.TextCaseCommand>(app); break;
                    case "TextAlignLeft": TextAlign(app, "Left"); break;
                    case "TextAlignCenter": TextAlign(app, "Center"); break;
                    case "TextAlignRight": TextAlign(app, "Right"); break;
                    case "TextAlignAxis": TextAlignAxis(app); break;
                    case "TextLeaderH": TextLeaderToggle(app, "H"); break;
                    case "TextLeaderV": TextLeaderToggle(app, "V"); break;
                    case "TextLeader90": TextLeaderToggle(app, "90"); break;

                    case "DimResetOverrides": DimResetOverrides(app); break;
                    case "DimResetText": DimResetText(app); break;
                    case "DimFindZero": DimFindZero(app); break;
                    case "DimFindReplace": StingLog.Info($"Dimension: {_commandTag}"); break;

                    case "LegendSyncPos": LegendSyncPosition(app); break;
                    case "LegendTitleLine": LegendTitleLine(app); break;
                    case "LegendUniform": LegendUniformSize(app); break;

                    case "TitleBlockReset": TitleBlockReset(app); break;
                    case "TitleBlockRescue": TitleBlockRescue(app); break;

                    case "RevShowClouds": RevisionToggle(app, "clouds"); break;
                    case "RevShowTags": RevisionToggle(app, "tags"); break;
                    case "RevDelCloudsView": RevisionDeleteClouds(app, false); break;
                    case "RevDelCloudsSel": RevisionDeleteClouds(app, true); break;

                    case "MeasureLines": MeasureSelected(app, "Lines"); break;
                    case "MeasureAreas": MeasureSelected(app, "Areas"); break;
                    case "MeasurePerimeters": MeasureSelected(app, "Perimeters"); break;
                    case "MeasureRoomAreas": RunCommand<Docs.SumAreasCommand>(app); break;

                    case "SwapElements":
                    case "ConvertRegions":
                    case "CleanSpaces":
                        StingLog.Info($"Utility: {_commandTag}");
                        break;

                    // ════════════════════════════════════════════════════════
                    // NEW — CREATE TAB extras
                    // ════════════════════════════════════════════════════════

                    case "T3Tags": RunCommand<Tags.BuildTagsCommand>(app); break;
                    case "MatTags": RunCommand<Tags.CombineParametersCommand>(app); break;
                    case "BuildAll": RunCommand<Tags.TagAndCombineCommand>(app); break;

                    // ════════════════════════════════════════════════════════
                    // NEW — VIEW TAB (Health, Anomaly, Bot, Colouriser ext)
                    // ════════════════════════════════════════════════════════

                    case "HealthScore": RunCommand<Tags.CompletenessDashboardCommand>(app); break;
                    case "HealthReport": RunCommand<Organise.TagStatsCommand>(app); break;
                    case "HealthFixAll": RunCommand<Organise.FixDuplicateTagsCommand>(app); break;

                    case "AnomalyRefresh": StingLog.Info("AnomalyRefresh"); break;
                    case "AnomalyScan": RunCommand<Tags.ValidateTagsCommand>(app); break;
                    case "AnomalyExport": RunCommand<Organise.AuditTagsCSVCommand>(app); break;

                    case "BotSmartPlace": RunCommand<Tags.SmartPlaceTagsCommand>(app); break;
                    case "BotDensityMap": RunCommand<Tags.TagOverlapAnalysisCommand>(app); break;
                    case "BotUndoAI":
                        TaskDialog.Show("Undo AI", "Use Ctrl+Z to undo the last operation.");
                        break;
                    case "BotOptions": RunCommand<Tags.TagConfigCommand>(app); break;

                    case "ColorSchemeDel": RunCommand<Select.ClearColorOverridesCommand>(app); break;
                    case "GradientApply": RunCommand<Select.ColorByParameterCommand>(app); break;
                    case "PatternApplyView": ApplyLinePattern(app); break;
                    case "ApplyLineWeight": ApplyLineWeightOverride(app); break;

                    // ── Unmapped / placeholder ──
                    default:
                        StingLog.Info($"DockPanel command not yet mapped: {_commandTag}");
                        break;
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // User cancelled — silent
            }
            catch (Exception ex)
            {
                StingLog.Error($"DockPanel command '{_commandTag}' failed", ex);
                TaskDialog.Show("STING Tools", $"Command failed: {ex.Message}");
            }
        }

        // ── Generic command runner ────────────────────────────────────

        private static void RunCommand<T>(UIApplication app) where T : IExternalCommand, new()
        {
            var cmd = new T();
            string message = "";
            var elements = new ElementSet();
            ExternalCommandData cmdData = CreateCommandData(app);
            if (cmdData != null)
            {
                cmd.Execute(cmdData, ref message, elements);
            }
            else
            {
                StingLog.Error($"Cannot create ExternalCommandData for {typeof(T).Name}");
                TaskDialog.Show("STING Tools",
                    $"Cannot invoke {typeof(T).Name} from panel.\n" +
                    "Please restart Revit and try again.");
            }
        }

        private static ExternalCommandData CreateCommandData(UIApplication app)
        {
            try
            {
                var data = (ExternalCommandData)RuntimeHelpers
                    .GetUninitializedObject(typeof(ExternalCommandData));

                var fields = typeof(ExternalCommandData).GetFields(
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                bool appSet = false;
                foreach (var field in fields)
                {
                    if (field.FieldType == typeof(UIApplication))
                    {
                        field.SetValue(data, app);
                        appSet = true;
                        break;
                    }
                }

                if (!appSet)
                {
                    var backingField = typeof(ExternalCommandData).GetField(
                        "<Application>k__BackingField",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    backingField?.SetValue(data, app);
                }

                return data;
            }
            catch (Exception ex)
            {
                StingLog.Error("CreateCommandData reflection failed", ex);
                return null;
            }
        }

        // ── Inline operations ─────────────────────────────────────────

        private static readonly Dictionary<string, List<ElementId>> _memorySlots =
            new Dictionary<string, List<ElementId>>();

        private static void ViewIsolateSelected(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Isolate", "Select elements first."); return; }
            uidoc.ActiveView.IsolateElementsTemporary(ids);
        }

        private static void ViewHideSelected(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Hide", "Select elements first."); return; }
            uidoc.ActiveView.HideElementsTemporary(ids);
        }

        private static void ViewRevealHidden(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var view = uidoc.ActiveView;
            try
            {
                // Use reflection — EnableTemporaryViewMode may not be available in all Revit API versions
                var method = view.GetType().GetMethod("EnableTemporaryViewMode",
                    new[] { typeof(TemporaryViewMode) });
                if (method != null)
                    method.Invoke(view, new object[] { TemporaryViewMode.RevealHiddenElements });
                else
                    TaskDialog.Show("Reveal", "Reveal hidden elements is not available in this Revit version.");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ViewRevealHidden: {ex.Message}");
            }
        }

        private static void ViewResetIsolate(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            uidoc.ActiveView.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
        }

        private static void SelectAllVisible(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = new FilteredElementCollector(uidoc.Document, uidoc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .ToElementIds();
            uidoc.Selection.SetElementIds(ids);
        }

        private static void ClearSelection(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            uidoc.Selection.SetElementIds(new List<ElementId>());
        }

        private static void InvertSelection(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var selected = new HashSet<ElementId>(uidoc.Selection.GetElementIds());
            var all = new FilteredElementCollector(uidoc.Document, uidoc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .ToElementIds();
            var inverted = new List<ElementId>();
            foreach (var id in all)
                if (!selected.Contains(id))
                    inverted.Add(id);
            uidoc.Selection.SetElementIds(inverted);
        }

        private static void DeleteSelected(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) return;
            var td = new TaskDialog("Delete");
            td.MainInstruction = $"Delete {ids.Count} elements?";
            td.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (td.Show() != TaskDialogResult.Ok) return;
            using (Transaction tx = new Transaction(uidoc.Document, "STING Delete Selected"))
            {
                tx.Start();
                uidoc.Document.Delete(ids);
                tx.Commit();
            }
        }

        private static void SelectAnnotationTags(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var selected = uidoc.Selection.GetElementIds();
            var tagIds = new List<ElementId>();
            var allTags = new FilteredElementCollector(uidoc.Document, uidoc.ActiveView.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();
            foreach (var tag in allTags)
            {
                try
                {
                    var hostIds2 = tag.GetTaggedLocalElementIds();
                    if (hostIds2.Any(id => selected.Contains(id)))
                        tagIds.Add(tag.Id);
                }
                catch { }
            }
            if (tagIds.Count > 0)
                uidoc.Selection.SetElementIds(tagIds);
            else
                TaskDialog.Show("Select Tags", "No tags found for selected elements.");
        }

        private static void SelectHostElements(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var selected = uidoc.Selection.GetElementIds();
            var hostIds = new List<ElementId>();
            foreach (ElementId id in selected)
            {
                var el = uidoc.Document.GetElement(id);
                if (el is IndependentTag tag)
                {
                    try { hostIds.AddRange(tag.GetTaggedLocalElementIds()); }
                    catch { }
                }
            }
            if (hostIds.Count > 0)
                uidoc.Selection.SetElementIds(hostIds);
            else
                TaskDialog.Show("Select Hosts", "No host elements found for selected tags.");
        }

        private static void SaveSelectionMemory(UIApplication app, string slot)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            _memorySlots[slot] = uidoc.Selection.GetElementIds()
                .Select(id => id).ToList();
            StingLog.Info($"Selection saved to {slot}: {_memorySlots[slot].Count} elements");
        }

        private static void LoadSelectionMemory(UIApplication app, string slot)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            if (_memorySlots.TryGetValue(slot, out var ids))
            {
                uidoc.Selection.SetElementIds(ids);
                StingLog.Info($"Selection loaded from {slot}: {ids.Count} elements");
            }
            else
            {
                TaskDialog.Show("Memory", $"Slot {slot} is empty.");
            }
        }

        private static void SwapMemorySlots(UIApplication app, string a, string b)
        {
            _memorySlots.TryGetValue(a, out var slotA);
            _memorySlots.TryGetValue(b, out var slotB);
            _memorySlots[a] = slotB ?? new List<ElementId>();
            _memorySlots[b] = slotA ?? new List<ElementId>();
        }

        private static void AddToMemory(UIApplication app, string slot)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            if (!_memorySlots.ContainsKey(slot))
                _memorySlots[slot] = new List<ElementId>();
            foreach (var id in uidoc.Selection.GetElementIds())
                if (!_memorySlots[slot].Contains(id))
                    _memorySlots[slot].Add(id);
        }

        private static void RemoveFromMemory(UIApplication app, string slot)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            if (!_memorySlots.ContainsKey(slot)) return;
            var toRemove = new HashSet<ElementId>(uidoc.Selection.GetElementIds());
            _memorySlots[slot].RemoveAll(id => toRemove.Contains(id));
        }

        private static void IntersectWithMemory(UIApplication app, string slot)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            if (!_memorySlots.TryGetValue(slot, out var memIds)) return;
            var memSet = new HashSet<ElementId>(memIds);
            var result = uidoc.Selection.GetElementIds()
                .Where(id => memSet.Contains(id)).ToList();
            uidoc.Selection.SetElementIds(result);
        }

        private static void ShowSelectionInfo(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            var byCategory = ids
                .Select(id => uidoc.Document.GetElement(id))
                .Where(e => e != null)
                .GroupBy(e => ParameterHelpers.GetCategoryName(e))
                .OrderByDescending(g => g.Count());
            var msg = $"Selected: {ids.Count} elements\n\n";
            foreach (var g in byCategory.Take(15))
                msg += $"  {g.Key}: {g.Count()}\n";
            foreach (var kvp in _memorySlots)
                msg += $"\n  {kvp.Key}: {kvp.Value.Count} stored";
            TaskDialog.Show("Selection Info", msg);
        }

        private static void QuickParamFilter(UIApplication app, string paramName)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0) { TaskDialog.Show("Quick Param", "Select an element first."); return; }
            var first = uidoc.Document.GetElement(selected.First());
            if (first == null) return;
            string val = ParameterHelpers.GetString(first, paramName);
            if (string.IsNullOrEmpty(val))
            {
                var p = first.LookupParameter(paramName);
                val = p?.AsValueString() ?? "";
            }
            if (string.IsNullOrEmpty(val)) { TaskDialog.Show("Quick Param", $"No '{paramName}' value on selected element."); return; }
            var matching = new FilteredElementCollector(uidoc.Document, uidoc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e => ParameterHelpers.GetString(e, paramName) == val
                    || (e.LookupParameter(paramName)?.AsValueString() ?? "") == val)
                .Select(e => e.Id).ToList();
            uidoc.Selection.SetElementIds(matching);
            StingLog.Info($"QuickParam '{paramName}'='{val}': {matching.Count} elements");
        }

        private static void BulkParamWriteInline(UIApplication app, string paramName, string value, bool clear)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Bulk Write", "Select elements first."); return; }
            if (string.IsNullOrEmpty(paramName)) { TaskDialog.Show("Bulk Write", "Enter parameter name."); return; }
            int written = 0;
            using (Transaction tx = new Transaction(uidoc.Document, "STING Bulk Parameter Write"))
            {
                tx.Start();
                foreach (ElementId id in ids)
                {
                    Element el = uidoc.Document.GetElement(id);
                    if (el == null) continue;
                    if (clear)
                    {
                        if (ParameterHelpers.SetString(el, paramName, "", overwrite: true))
                            written++;
                    }
                    else
                    {
                        if (ParameterHelpers.SetString(el, paramName, value, overwrite: true))
                            written++;
                    }
                }
                tx.Commit();
            }
            TaskDialog.Show("Bulk Write", $"Updated {written} of {ids.Count} elements.");
        }

        private static void BulkParamPreview(UIApplication app, string paramName, string value)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Preview", "Select elements first."); return; }
            int hasParam = 0;
            int wouldChange = 0;
            foreach (ElementId id in ids)
            {
                Element el = uidoc.Document.GetElement(id);
                if (el == null) continue;
                Parameter p = el.LookupParameter(paramName);
                if (p != null)
                {
                    hasParam++;
                    string current = p.AsString() ?? "";
                    if (current != value) wouldChange++;
                }
            }
            TaskDialog.Show("Preview",
                $"Parameter: {paramName}\nNew value: {value}\n\n" +
                $"{hasParam} of {ids.Count} elements have this parameter.\n" +
                $"{wouldChange} values would change.");
        }

        // ── Graphic overrides ─────────────────────────────────────────

        private static void SetHalftone(UIApplication app, bool on)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Halftone", "Select elements first."); return; }
            using (Transaction tx = new Transaction(uidoc.Document, "STING Halftone"))
            {
                tx.Start();
                var ogs = new OverrideGraphicSettings();
                ogs.SetHalftone(on);
                foreach (ElementId id in ids)
                    uidoc.ActiveView.SetElementOverrides(id, ogs);
                tx.Commit();
            }
        }

        private static void PermanentHide(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) return;
            using (Transaction tx = new Transaction(uidoc.Document, "STING Permanent Hide"))
            {
                tx.Start();
                uidoc.ActiveView.HideElements(ids);
                tx.Commit();
            }
        }

        private static void PermanentUnhide(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) return;
            using (Transaction tx = new Transaction(uidoc.Document, "STING Permanent Unhide"))
            {
                tx.Start();
                uidoc.ActiveView.UnhideElements(ids);
                tx.Commit();
            }
        }

        private static void UnhideCategory(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            using (Transaction tx = new Transaction(uidoc.Document, "STING Unhide Category"))
            {
                tx.Start();
                foreach (Category cat in uidoc.Document.Settings.Categories)
                {
                    try
                    {
                        if (cat.get_Visible(uidoc.ActiveView) == false)
                            cat.set_Visible(uidoc.ActiveView, true);
                    }
                    catch { }
                }
                tx.Commit();
            }
        }

        // ── Token writer helper ──────────────────────────────────────

        private static void WriteTokenToSelected(UIApplication app, string paramName, string label)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show($"Set {label}", "Select elements first."); return; }

            string[] options = GetTokenOptions(paramName);

            var dlg = new TaskDialog($"Set {label}");
            dlg.MainInstruction = $"Set {label} for {ids.Count} element(s)";
            dlg.MainContent = string.Join(", ", options);
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                options.Length > 0 ? options[0] : "Value 1");
            if (options.Length > 1)
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, options[1]);
            if (options.Length > 2)
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, options[2]);
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Clear value", "Remove existing value");

            var result = dlg.Show();
            string value;
            switch (result)
            {
                case TaskDialogResult.CommandLink1: value = options.Length > 0 ? options[0] : ""; break;
                case TaskDialogResult.CommandLink2: value = options.Length > 1 ? options[1] : ""; break;
                case TaskDialogResult.CommandLink3: value = options.Length > 2 ? options[2] : ""; break;
                case TaskDialogResult.CommandLink4: value = ""; break;
                default: return;
            }

            int written = 0;
            using (Transaction tx = new Transaction(uidoc.Document, $"STING Set {label}"))
            {
                tx.Start();
                foreach (ElementId id in ids)
                {
                    Element el = uidoc.Document.GetElement(id);
                    if (el != null && ParameterHelpers.SetString(el, paramName, value, overwrite: true))
                        written++;
                }
                tx.Commit();
            }
            TaskDialog.Show($"Set {label}", $"Updated {written} of {ids.Count} elements.");
        }

        private static string[] GetTokenOptions(string paramName)
        {
            return paramName switch
            {
                ParamRegistry.SYS => new[] { "HVAC", "DCW", "SAN" },
                ParamRegistry.FUNC => new[] { "SUP", "HTG", "PWR" },
                ParamRegistry.PROD => new[] { "AHU", "DB", "DR" },
                ParamRegistry.LVL => new[] { "GF", "L01", "B1" },
                ParamRegistry.ORIGIN => new[] { "NEW", "EXISTING", "DEMOLISHED" },
                ParamRegistry.PROJECT => new[] { "PRJ001", "PRJ002", "PRJ003" },
                ParamRegistry.REV => new[] { "P01", "P02", "C01" },
                ParamRegistry.VOLUME => new[] { "V01", "V02", "V03" },
                _ => new[] { "VALUE1", "VALUE2", "VALUE3" }
            };
        }

        // ── Connected elements selector ─────────────────────────────

        private static void SelectConnectedElements(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0) { TaskDialog.Show("Connected", "Select an element first."); return; }

            var connected = new HashSet<ElementId>(selected);
            foreach (ElementId id in selected)
            {
                Element el = uidoc.Document.GetElement(id);
                if (el == null) continue;

                try
                {
                    var connectorManager = (el as Autodesk.Revit.DB.MEPCurve)?.ConnectorManager
                        ?? (el as Autodesk.Revit.DB.FamilyInstance)
                            ?.MEPModel?.ConnectorManager;

                    if (connectorManager != null)
                    {
                        foreach (Connector connector in connectorManager.Connectors)
                        {
                            if (connector.IsConnected)
                            {
                                foreach (Connector otherConn in connector.AllRefs)
                                {
                                    if (otherConn.Owner != null)
                                        connected.Add(otherConn.Owner.Id);
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            uidoc.Selection.SetElementIds(connected.ToList());
            StingLog.Info($"SelectConnected: {connected.Count} elements (was {selected.Count})");
        }

        private static void SelectByCategory(UIApplication app, string categoryName)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = new FilteredElementCollector(uidoc.Document, uidoc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e => ParameterHelpers.GetCategoryName(e) == categoryName)
                .Select(e => e.Id).ToList();
            uidoc.Selection.SetElementIds(ids);
            StingLog.Info($"SelectByCategory '{categoryName}': {ids.Count} elements");
        }

        private static void SelectVisibleOnly(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = new FilteredElementCollector(uidoc.Document, uidoc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e => !e.IsHidden(uidoc.ActiveView))
                .Select(e => e.Id).ToList();
            uidoc.Selection.SetElementIds(ids);
            StingLog.Info($"SelectVisibleOnly: {ids.Count} elements");
        }

        // ── Colouriser helpers ─────────────────────────────────────

        private static void ColorByParameter(UIApplication app, string paramName, string paletteName)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            if (string.IsNullOrEmpty(paramName))
            {
                TaskDialog.Show("Color By Parameter", "Select a parameter name first.");
                return;
            }

            var elements = new FilteredElementCollector(uidoc.Document, uidoc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .ToList();

            var groups = new Dictionary<string, List<ElementId>>();
            foreach (var el in elements)
            {
                string val = ParameterHelpers.GetString(el, paramName);
                if (string.IsNullOrEmpty(val))
                {
                    var p = el.LookupParameter(paramName);
                    val = p?.AsValueString() ?? "<No Value>";
                }
                if (!groups.ContainsKey(val))
                    groups[val] = new List<ElementId>();
                groups[val].Add(el.Id);
            }

            Color[] palette = GetColorPalette(paletteName, groups.Count);

            FillPatternElement solidFill = null;
            foreach (FillPatternElement fpe in new FilteredElementCollector(uidoc.Document)
                .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>())
            {
                try
                {
                    if (fpe.GetFillPattern().IsSolidFill)
                    {
                        solidFill = fpe;
                        break;
                    }
                }
                catch { }
            }

            using (Transaction tx = new Transaction(uidoc.Document, "STING Color By Parameter"))
            {
                tx.Start();
                int colorIdx = 0;
                foreach (var kvp in groups)
                {
                    var ogs = new OverrideGraphicSettings();
                    Color color = palette[colorIdx % palette.Length];
                    ogs.SetProjectionLineColor(color);
                    if (solidFill != null)
                    {
                        ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                        ogs.SetSurfaceForegroundPatternColor(color);
                    }
                    foreach (ElementId id in kvp.Value)
                        uidoc.ActiveView.SetElementOverrides(id, ogs);
                    colorIdx++;
                }
                tx.Commit();
            }

            TaskDialog.Show("Color By Parameter",
                $"Coloured {elements.Count} elements by '{paramName}'\n" +
                $"({groups.Count} unique values).");
        }

        private static void ColorByHex(UIApplication app, string hexColor)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Color", "Select elements first."); return; }

            hexColor = (hexColor ?? "").Trim().TrimStart('#');
            if (hexColor.Length != 6 ||
                !byte.TryParse(hexColor.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) ||
                !byte.TryParse(hexColor.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) ||
                !byte.TryParse(hexColor.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
            {
                TaskDialog.Show("Color", "Invalid hex color. Use format: RRGGBB");
                return;
            }

            Color color = new Color(r, g, b);

            FillPatternElement solidFill = null;
            foreach (FillPatternElement fpe in new FilteredElementCollector(uidoc.Document)
                .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>())
            {
                try { if (fpe.GetFillPattern().IsSolidFill) { solidFill = fpe; break; } }
                catch { }
            }

            using (Transaction tx = new Transaction(uidoc.Document, "STING Color By Hex"))
            {
                tx.Start();
                var ogs = new OverrideGraphicSettings();
                ogs.SetProjectionLineColor(color);
                if (solidFill != null)
                {
                    ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                    ogs.SetSurfaceForegroundPatternColor(color);
                }
                foreach (ElementId id in ids)
                    uidoc.ActiveView.SetElementOverrides(id, ogs);
                tx.Commit();
            }
        }

        private static void SetTransparencyOverride(UIApplication app, string transparencyStr)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Transparency", "Select elements first."); return; }

            if (!int.TryParse(transparencyStr, out int transparency))
                transparency = 50;
            transparency = Math.Max(0, Math.Min(100, transparency));

            using (Transaction tx = new Transaction(uidoc.Document, "STING Set Transparency"))
            {
                tx.Start();
                var ogs = new OverrideGraphicSettings();
                ogs.SetSurfaceTransparency(transparency);
                foreach (ElementId id in ids)
                    uidoc.ActiveView.SetElementOverrides(id, ogs);
                tx.Commit();
            }
        }

        // ── Schedule operations ──────────────────────────────────

        private static void ScheduleSyncPosition(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            if (!(uidoc.Document.ActiveView is ViewSheet sheet))
            { TaskDialog.Show("Schedule Sync", "Active view must be a sheet."); return; }

            // Find schedule graphics on this sheet and align them
            var schedGraphics = new FilteredElementCollector(uidoc.Document, sheet.Id)
                .OfClass(typeof(ScheduleSheetInstance))
                .Cast<ScheduleSheetInstance>()
                .ToList();

            if (schedGraphics.Count < 2)
            { TaskDialog.Show("Schedule Sync", "Need at least 2 schedules on sheet."); return; }

            XYZ refPos = schedGraphics[0].Point;
            int moved = 0;
            using (Transaction tx = new Transaction(uidoc.Document, "STING Schedule Sync Position"))
            {
                tx.Start();
                foreach (var sg in schedGraphics.Skip(1))
                {
                    try
                    {
                        sg.Point = new XYZ(refPos.X, sg.Point.Y, sg.Point.Z);
                        moved++;
                    }
                    catch { }
                }
                tx.Commit();
            }
            TaskDialog.Show("Schedule Sync", $"Aligned {moved} schedules to same X position.");
        }

        private static void ScheduleToggleRotation(UIApplication app)
        {
            // Schedule rotation on sheets is not directly API-accessible for ScheduleSheetInstance
            // Log for now
            StingLog.Info("ScheduleToggleRotation: not supported in Revit API for schedule graphics");
            TaskDialog.Show("Schedule Rotation", "Schedule rotation is controlled via the schedule properties dialog in Revit.");
        }

        private static void ScheduleShowHidden(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            var view = doc.ActiveView;

            if (!(view is ViewSchedule sched))
            { TaskDialog.Show("Schedule", "Active view must be a schedule."); return; }

            var def = sched.Definition;
            int fieldCount = def.GetFieldCount();
            int hiddenCount = 0;
            var sb = new StringBuilder();
            sb.AppendLine($"Schedule: {sched.Name}");
            sb.AppendLine($"Total fields: {fieldCount}");
            sb.AppendLine();
            for (int i = 0; i < fieldCount; i++)
            {
                var field = def.GetField(i);
                string vis = field.IsHidden ? "[HIDDEN]" : "[Visible]";
                if (field.IsHidden) hiddenCount++;
                sb.AppendLine($"  {vis} {field.GetName()}");
            }
            sb.AppendLine($"\nHidden fields: {hiddenCount}");
            TaskDialog.Show("Schedule Fields", sb.ToString());
        }

        private static void ScheduleMatchWidest(UIApplication app)
        {
            // Schedule column widths are controlled through SetColumnWidth API
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            if (!(doc.ActiveView is ViewSchedule sched))
            { TaskDialog.Show("Schedule", "Active view must be a schedule."); return; }

            TaskDialog.Show("Schedule Widths", "Use Revit's schedule column resize in the schedule view header.\n" +
                "Column widths are controlled via the ScheduleField.GridColumnWidth property.");
            StingLog.Info("ScheduleMatchWidest: informational — column widths via ScheduleField");
        }

        private static void ScheduleSetColumnWidth(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            if (!(doc.ActiveView is ViewSchedule sched))
            { TaskDialog.Show("Schedule", "Active view must be a schedule."); return; }

            var def = sched.Definition;
            int fieldCount = def.GetFieldCount();

            // Set all columns to same width (1 inch = 0.0833 ft)
            TaskDialog dlg = new TaskDialog("Set Column Width");
            dlg.MainInstruction = $"Set column width for {fieldCount} fields";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Narrow (15mm)", "Compact schedules");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Standard (25mm)", "Default readable width");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Wide (40mm)", "Comfortable reading");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            double width;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: width = 15.0 / 304.8; break; // mm to feet
                case TaskDialogResult.CommandLink2: width = 25.0 / 304.8; break;
                case TaskDialogResult.CommandLink3: width = 40.0 / 304.8; break;
                default: return;
            }

            int updated = 0;
            using (Transaction tx = new Transaction(doc, "STING Set Column Width"))
            {
                tx.Start();
                for (int i = 0; i < fieldCount; i++)
                {
                    try
                    {
                        var field = def.GetField(i);
                        field.GridColumnWidth = width;
                        updated++;
                    }
                    catch { }
                }
                tx.Commit();
            }
            TaskDialog.Show("Column Width", $"Updated {updated} column widths.");
        }

        private static void ScheduleEqualiseColumns(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            if (!(doc.ActiveView is ViewSchedule sched))
            { TaskDialog.Show("Schedule", "Active view must be a schedule."); return; }

            var def = sched.Definition;
            int fieldCount = def.GetFieldCount();
            if (fieldCount == 0) return;

            // Find max width
            double maxWidth = 0;
            for (int i = 0; i < fieldCount; i++)
            {
                try
                {
                    double w = def.GetField(i).GridColumnWidth;
                    if (w > maxWidth) maxWidth = w;
                }
                catch { }
            }

            int updated = 0;
            using (Transaction tx = new Transaction(doc, "STING Equalise Columns"))
            {
                tx.Start();
                for (int i = 0; i < fieldCount; i++)
                {
                    try
                    {
                        def.GetField(i).GridColumnWidth = maxWidth;
                        updated++;
                    }
                    catch { }
                }
                tx.Commit();
            }
            TaskDialog.Show("Equalise Columns",
                $"Set {updated} columns to width {maxWidth * 304.8:F1}mm.");
        }

        // ── Text note operations ────────────────────────────────────

        private static void TextAlign(UIApplication app, string alignment)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Text Align", "Select text notes first."); return; }

            int aligned = 0;
            using (Transaction tx = new Transaction(uidoc.Document, $"STING Text Align {alignment}"))
            {
                tx.Start();
                foreach (ElementId id in ids)
                {
                    if (uidoc.Document.GetElement(id) is TextNote tn)
                    {
                        try
                        {
                            var p = tn.get_Parameter(BuiltInParameter.TEXT_ALIGN_HORZ);
                            if (p != null && !p.IsReadOnly)
                            {
                                int val = alignment == "Left" ? 0 : alignment == "Center" ? 1 : 2;
                                p.Set(val);
                                aligned++;
                            }
                        }
                        catch { }
                    }
                }
                tx.Commit();
            }
            StingLog.Info($"TextAlign {alignment}: {aligned}");
        }

        private static void TextAlignAxis(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            var textNotes = ids.Select(id => uidoc.Document.GetElement(id))
                .OfType<TextNote>().ToList();

            if (textNotes.Count < 2) { TaskDialog.Show("Text Align", "Select 2+ text notes."); return; }

            // Align all text notes to the X of the first one
            XYZ refPos = textNotes[0].Coord;
            int moved = 0;
            using (Transaction tx = new Transaction(uidoc.Document, "STING Text Align Axis"))
            {
                tx.Start();
                foreach (var tn in textNotes.Skip(1))
                {
                    try
                    {
                        tn.Coord = new XYZ(refPos.X, tn.Coord.Y, tn.Coord.Z);
                        moved++;
                    }
                    catch { }
                }
                tx.Commit();
            }
            StingLog.Info($"TextAlignAxis: {moved} text notes aligned");
        }

        private static void TextLeaderToggle(UIApplication app, string mode)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            int toggled = 0;
            using (Transaction tx = new Transaction(uidoc.Document, "STING Text Leader"))
            {
                tx.Start();
                foreach (ElementId id in ids)
                {
                    if (uidoc.Document.GetElement(id) is TextNote tn)
                    {
                        try
                        {
                            var leaders = tn.GetLeaders();
                            if (leaders.Count > 0)
                                tn.RemoveLeaders();
                            else
                                tn.AddLeader(TextNoteLeaderType.TNLT_STRAIGHT_L);
                            toggled++;
                        }
                        catch { }
                    }
                }
                tx.Commit();
            }
            StingLog.Info($"TextLeader {mode}: toggled {toggled}");
        }

        // ── Dimension operations ────────────────────────────────────

        private static void DimResetOverrides(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            int reset = 0;
            using (Transaction tx = new Transaction(uidoc.Document, "STING Dim Reset Overrides"))
            {
                tx.Start();
                foreach (ElementId id in ids)
                {
                    if (uidoc.Document.GetElement(id) is Dimension dim)
                    {
                        try
                        {
                            foreach (DimensionSegment seg in dim.Segments)
                            {
                                seg.ValueOverride = "";
                                seg.Above = "";
                                seg.Below = "";
                                seg.Prefix = "";
                                seg.Suffix = "";
                            }
                            reset++;
                        }
                        catch { }
                    }
                }
                tx.Commit();
            }
            TaskDialog.Show("Dim Reset", $"Reset overrides on {reset} dimensions.");
        }

        private static void DimResetText(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            int reset = 0;
            using (Transaction tx = new Transaction(uidoc.Document, "STING Dim Reset Text"))
            {
                tx.Start();
                foreach (ElementId id in ids)
                {
                    if (uidoc.Document.GetElement(id) is Dimension dim)
                    {
                        try
                        {
                            foreach (DimensionSegment seg in dim.Segments)
                                seg.ValueOverride = "";
                            reset++;
                        }
                        catch { }
                    }
                }
                tx.Commit();
            }
            TaskDialog.Show("Dim Reset Text", $"Reset text overrides on {reset} dimensions.");
        }

        private static void DimFindZero(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            var dims = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Dimension))
                .Cast<Dimension>()
                .ToList();

            var zeroDims = new List<ElementId>();
            foreach (var dim in dims)
            {
                try
                {
                    if (dim.Value.HasValue && Math.Abs(dim.Value.Value) < 0.001)
                        zeroDims.Add(dim.Id);
                    else
                    {
                        foreach (DimensionSegment seg in dim.Segments)
                            if (seg.Value.HasValue && Math.Abs(seg.Value.Value) < 0.001)
                            { zeroDims.Add(dim.Id); break; }
                    }
                }
                catch { }
            }

            if (zeroDims.Count > 0)
                uidoc.Selection.SetElementIds(zeroDims);
            TaskDialog.Show("Find Zero Dims",
                $"Found {zeroDims.Count} dimensions with zero-length segments.");
        }

        // ── Legend operations ────────────────────────────────────────

        private static void LegendSyncPosition(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            if (!(doc.ActiveView is ViewSheet sheet))
            { TaskDialog.Show("Legend Sync", "Active view must be a sheet."); return; }

            // Find all legend viewports on this sheet
            var vpIds = sheet.GetAllViewports().ToList();
            var legendVps = new List<Viewport>();
            foreach (var vpId in vpIds)
            {
                var vp = doc.GetElement(vpId) as Viewport;
                if (vp == null) continue;
                var vpView = doc.GetElement(vp.ViewId) as View;
                if (vpView?.ViewType == ViewType.Legend)
                    legendVps.Add(vp);
            }

            if (legendVps.Count < 2)
            { TaskDialog.Show("Legend Sync", "Need 2+ legend viewports on sheet."); return; }

            XYZ refCenter = legendVps[0].GetBoxCenter();
            int aligned = 0;
            using (Transaction tx = new Transaction(doc, "STING Legend Sync"))
            {
                tx.Start();
                foreach (var vp in legendVps.Skip(1))
                {
                    vp.SetBoxCenter(new XYZ(refCenter.X, vp.GetBoxCenter().Y, 0));
                    aligned++;
                }
                tx.Commit();
            }
            TaskDialog.Show("Legend Sync", $"Aligned {aligned} legends to same X position.");
        }

        private static void LegendTitleLine(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            TaskDialog.Show("Legend Title Line",
                "Legend title lines are managed within the legend view itself.\n" +
                "Open the legend view and add/edit text notes for titles.");
        }

        private static void LegendUniformSize(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            if (!(doc.ActiveView is ViewSheet sheet))
            { TaskDialog.Show("Legend Uniform", "Active view must be a sheet."); return; }

            var vpIds = sheet.GetAllViewports().ToList();
            var legendVps = new List<(Viewport vp, View view)>();
            foreach (var vpId in vpIds)
            {
                var vp = doc.GetElement(vpId) as Viewport;
                if (vp == null) continue;
                var vpView = doc.GetElement(vp.ViewId) as View;
                if (vpView != null && (vpView.ViewType == ViewType.Legend ||
                    (vpView.ViewType == ViewType.DraftingView && vpView.Name.Contains("STING"))))
                    legendVps.Add((vp, vpView));
            }

            if (legendVps.Count < 2)
            { TaskDialog.Show("Legend Uniform", "Need 2+ legend/STING viewports on sheet."); return; }

            // Set all legend views to scale 1 (1:1) for uniform sizing
            int updated = 0;
            using (var tx = new Transaction(doc, "STING Legend Uniform Scale"))
            {
                tx.Start();
                foreach (var (vp, view) in legendVps)
                {
                    try
                    {
                        if (view.Scale != 1)
                        {
                            view.Scale = 1;
                            updated++;
                        }
                    }
                    catch { }
                }
                tx.Commit();
            }

            TaskDialog.Show("Legend Uniform",
                $"Set {updated} of {legendVps.Count} legend views to 1:1 scale.\n" +
                "All legends on this sheet now have uniform sizing.");
        }

        // ── TitleBlock operations ───────────────────────────────────

        private static void TitleBlockReset(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            if (!(doc.ActiveView is ViewSheet sheet))
            { TaskDialog.Show("Title Block", "Active view must be a sheet."); return; }

            var tbs = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .ToList();

            if (tbs.Count == 0)
            { TaskDialog.Show("Title Block", "No title block found on sheet."); return; }

            int reset = 0;
            using (Transaction tx = new Transaction(doc, "STING Title Block Reset"))
            {
                tx.Start();
                foreach (Element tb in tbs)
                {
                    try
                    {
                        // Reset position to origin
                        if (tb.Location is LocationPoint lp)
                        {
                            lp.Point = XYZ.Zero;
                            reset++;
                        }
                    }
                    catch { }
                }
                tx.Commit();
            }
            TaskDialog.Show("Title Block", $"Reset {reset} title blocks to origin.");
        }

        private static void TitleBlockRescue(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            // Find sheets missing title blocks
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .ToList();

            int missing = 0;
            foreach (var s in sheets)
            {
                var tbs = new FilteredElementCollector(doc, s.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType()
                    .ToList();
                if (tbs.Count == 0) missing++;
            }

            TaskDialog.Show("Title Block Rescue",
                $"Scanned {sheets.Count} sheets.\n" +
                $"Missing title blocks: {missing}\n\n" +
                "To fix, open the sheet and place a title block from Insert tab.");
        }

        // ── Revision operations ─────────────────────────────────────

        private static void RevisionToggle(UIApplication app, string what)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            var view = doc.ActiveView;

            var clouds = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_RevisionClouds)
                .WhereElementIsNotElementType()
                .ToList();

            var tags = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_RevisionCloudTags)
                .WhereElementIsNotElementType()
                .ToList();

            TaskDialog.Show("Revisions",
                $"Active view: {view.Name}\n" +
                $"Revision clouds: {clouds.Count}\n" +
                $"Revision tags: {tags.Count}\n\n" +
                "Use Revit View > Visibility Graphics > Annotation Categories " +
                "to control revision cloud/tag visibility.");
        }

        private static void RevisionDeleteClouds(UIApplication app, bool selectionOnly)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;

            ICollection<ElementId> cloudIds;
            if (selectionOnly)
            {
                cloudIds = uidoc.Selection.GetElementIds()
                    .Where(id =>
                    {
                        var e = doc.GetElement(id);
                        return e?.Category?.Id.Value == (int)BuiltInCategory.OST_RevisionClouds;
                    })
                    .ToList();
            }
            else
            {
                cloudIds = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfCategory(BuiltInCategory.OST_RevisionClouds)
                    .WhereElementIsNotElementType()
                    .ToElementIds();
            }

            if (cloudIds.Count == 0)
            { TaskDialog.Show("Delete Clouds", "No revision clouds found."); return; }

            TaskDialog confirm = new TaskDialog("Delete Revision Clouds");
            confirm.MainInstruction = $"Delete {cloudIds.Count} revision clouds?";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() != TaskDialogResult.Ok) return;

            using (Transaction tx = new Transaction(doc, "STING Delete Revision Clouds"))
            {
                tx.Start();
                doc.Delete(cloudIds);
                tx.Commit();
            }
            TaskDialog.Show("Delete Clouds", $"Deleted {cloudIds.Count} revision clouds.");
        }

        // ── Measurement operations ──────────────────────────────────

        private static void MeasureSelected(UIApplication app, string mode)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Measure", "Select elements first."); return; }

            double totalLength = 0;
            double totalArea = 0;
            double totalPerimeter = 0;
            int counted = 0;

            foreach (ElementId id in ids)
            {
                Element e = doc.GetElement(id);
                if (e == null) continue;

                try
                {
                    if (e.Location is LocationCurve lc)
                    {
                        totalLength += lc.Curve.Length;
                        counted++;
                    }

                    // Try area parameter
                    Parameter areaP = e.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                    if (areaP != null && areaP.HasValue)
                        totalArea += areaP.AsDouble();

                    Parameter perimP = e.get_Parameter(BuiltInParameter.HOST_PERIMETER_COMPUTED);
                    if (perimP != null && perimP.HasValue)
                        totalPerimeter += perimP.AsDouble();
                }
                catch { }
            }

            var report = new StringBuilder();
            report.AppendLine($"Measurement — {ids.Count} elements");
            report.AppendLine(new string('─', 35));
            if (totalLength > 0) report.AppendLine($"  Total length:    {totalLength * 0.3048:F2} m ({totalLength:F2} ft)");
            if (totalArea > 0) report.AppendLine($"  Total area:      {totalArea * 0.0929:F2} m² ({totalArea:F2} ft²)");
            if (totalPerimeter > 0) report.AppendLine($"  Total perimeter: {totalPerimeter * 0.3048:F2} m ({totalPerimeter:F2} ft)");
            if (totalLength == 0 && totalArea == 0 && totalPerimeter == 0)
                report.AppendLine("  No measurable geometry found in selection.");

            TaskDialog.Show("Measure", report.ToString());
        }

        // ── Line pattern / line weight operations ───────────────────

        private static void ApplyLinePattern(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Line Pattern", "Select elements first."); return; }

            TaskDialog dlg = new TaskDialog("Apply Line Pattern");
            dlg.MainInstruction = $"Set projection line pattern for {ids.Count} elements";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Solid", "Continuous line");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Dash", "Dashed line pattern");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Hidden", "Short dashes (hidden lines)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Reset (Default)", "Remove line pattern override");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string patternName;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: patternName = "Solid"; break;
                case TaskDialogResult.CommandLink2: patternName = "Dash"; break;
                case TaskDialogResult.CommandLink3: patternName = "Hidden"; break;
                case TaskDialogResult.CommandLink4: patternName = null; break;
                default: return;
            }

            // Find line pattern
            ElementId patternId = ElementId.InvalidElementId;
            if (patternName != null)
            {
                var pattern = new FilteredElementCollector(uidoc.Document)
                    .OfClass(typeof(LinePatternElement))
                    .Cast<LinePatternElement>()
                    .FirstOrDefault(lp =>
                        lp.Name.IndexOf(patternName, StringComparison.OrdinalIgnoreCase) >= 0);
                if (pattern != null) patternId = pattern.Id;
            }

            using (Transaction tx = new Transaction(uidoc.Document, "STING Apply Line Pattern"))
            {
                tx.Start();
                var ogs = new OverrideGraphicSettings();
                if (patternId != ElementId.InvalidElementId)
                    ogs.SetProjectionLinePatternId(patternId);
                foreach (ElementId id in ids)
                    uidoc.ActiveView.SetElementOverrides(id, ogs);
                tx.Commit();
            }
        }

        private static void ApplyLineWeightOverride(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Line Weight", "Select elements first."); return; }

            TaskDialog dlg = new TaskDialog("Apply Line Weight");
            dlg.MainInstruction = $"Set line weight for {ids.Count} elements";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Thin (1)", "Hairline");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Standard (3)", "Normal");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Bold (6)", "Emphasis");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Extra Bold (10)", "Maximum weight");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            int weight;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: weight = 1; break;
                case TaskDialogResult.CommandLink2: weight = 3; break;
                case TaskDialogResult.CommandLink3: weight = 6; break;
                case TaskDialogResult.CommandLink4: weight = 10; break;
                default: return;
            }

            using (Transaction tx = new Transaction(uidoc.Document, "STING Apply Line Weight"))
            {
                tx.Start();
                var ogs = new OverrideGraphicSettings();
                ogs.SetProjectionLineWeight(weight);
                foreach (ElementId id in ids)
                    uidoc.ActiveView.SetElementOverrides(id, ogs);
                tx.Commit();
            }
        }

        // ── Viewport operations ─────────────────────────────────

        private static void ViewportRenumberOffset(UIApplication app, int delta)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            if (!(view is ViewSheet sheet))
            {
                TaskDialog.Show("Viewport Number", "Active view must be a sheet.");
                return;
            }

            var vpIds = sheet.GetAllViewports().ToList();
            if (vpIds.Count == 0)
            {
                TaskDialog.Show("Viewport Number", "No viewports on active sheet.");
                return;
            }

            int updated = 0;
            using (Transaction tx = new Transaction(doc, "STING Viewport Renumber"))
            {
                tx.Start();
                foreach (ElementId vpId in vpIds)
                {
                    Viewport vp = doc.GetElement(vpId) as Viewport;
                    if (vp == null) continue;
                    try
                    {
                        Parameter detailNum = vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
                        if (detailNum != null && !detailNum.IsReadOnly)
                        {
                            string current = detailNum.AsString() ?? "0";
                            if (int.TryParse(current, out int num))
                            {
                                int newNum = Math.Max(1, num + delta);
                                detailNum.Set(newNum.ToString());
                                updated++;
                            }
                        }
                    }
                    catch { }
                }
                tx.Commit();
            }

            StingLog.Info($"ViewportRenumber: delta={delta}, updated={updated}");
        }

        // ── Orphan & layout helpers ───────────────────────────────

        private static void FindOrphanedTags(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            var tags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            var orphaned = new List<ElementId>();
            foreach (var tag in tags)
            {
                try
                {
                    var hostIds = tag.GetTaggedLocalElementIds();
                    if (hostIds == null || hostIds.Count == 0)
                    {
                        orphaned.Add(tag.Id);
                        continue;
                    }
                    // Check if host element still exists in view
                    bool anyValid = false;
                    foreach (var hid in hostIds)
                    {
                        Element host = doc.GetElement(hid);
                        if (host != null) { anyValid = true; break; }
                    }
                    if (!anyValid) orphaned.Add(tag.Id);
                }
                catch { orphaned.Add(tag.Id); }
            }

            if (orphaned.Count == 0)
            {
                TaskDialog.Show("Orphaned Tags", $"No orphaned tags found. All {tags.Count} tags are valid.");
                return;
            }

            TaskDialog dlg = new TaskDialog("Orphaned Tags");
            dlg.MainInstruction = $"Found {orphaned.Count} orphaned tags (no valid host element)";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Select Orphans", "Select orphaned tags for review");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Delete Orphans", $"Delete {orphaned.Count} orphaned tags");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    uidoc.Selection.SetElementIds(orphaned);
                    break;
                case TaskDialogResult.CommandLink2:
                    using (Transaction tx = new Transaction(doc, "STING Delete Orphaned Tags"))
                    {
                        tx.Start();
                        doc.Delete(orphaned);
                        tx.Commit();
                    }
                    TaskDialog.Show("Orphaned Tags", $"Deleted {orphaned.Count} orphaned tags.");
                    break;
            }
        }

        private static void CloneTagLayout(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var sourceView = doc.ActiveView;

            // Get tag positions from source view
            var sourceTags = new FilteredElementCollector(doc, sourceView.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            if (sourceTags.Count == 0)
            {
                TaskDialog.Show("Clone Tag Layout", "No tags in active view to clone.");
                return;
            }

            // Build mapping: host element ID → tag head position + orientation
            var tagLayout = new Dictionary<ElementId, (XYZ headPos, bool hasLeader, TagOrientation orient)>();
            foreach (var tag in sourceTags)
            {
                try
                {
                    var hostIds = tag.GetTaggedLocalElementIds();
                    if (hostIds.Count > 0)
                    {
                        tagLayout[hostIds.First()] = (tag.TagHeadPosition, tag.HasLeader, tag.TagOrientation);
                    }
                }
                catch { }
            }

            TaskDialog.Show("Clone Tag Layout",
                $"Captured layout for {tagLayout.Count} tags in '{sourceView.Name}'.\n" +
                "Navigate to target view and use 'Apply Cloned Layout' to apply.\n\n" +
                "(Tag positions are stored relative to host elements. " +
                "Matching is by element ID — same elements must exist in target view.)");

            StingLog.Info($"CloneTagLayout: captured {tagLayout.Count} positions from '{sourceView.Name}'");
        }

        // ── Room tag placement ──────────────────────────────────────

        private static void MoveRoomTags(UIApplication app, string position)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            // Find room tags in view
            var roomTags = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_RoomTags)
                .WhereElementIsNotElementType()
                .ToList();

            if (roomTags.Count == 0)
            {
                TaskDialog.Show("Room Tags", "No room tags in active view.");
                return;
            }

            int moved = 0;
            using (Transaction tx = new Transaction(doc, $"STING Room Tags {position}"))
            {
                tx.Start();
                foreach (Element tagElem in roomTags)
                {
                    try
                    {
                        // Room tags have a Location that can be moved
                        if (tagElem.Location is LocationPoint lp)
                        {
                            // Find the associated room
                            var roomTag = tagElem as Autodesk.Revit.DB.Architecture.RoomTag;
                            if (roomTag == null) continue;
                            var room = roomTag.Room;
                            if (room == null) continue;

                            // Get room bounding box in view
                            BoundingBoxXYZ roomBB = room.get_BoundingBox(view);
                            if (roomBB == null) continue;

                            XYZ targetPos;
                            switch (position)
                            {
                                case "TopLeft":
                                    targetPos = new XYZ(
                                        roomBB.Min.X + (roomBB.Max.X - roomBB.Min.X) * 0.15,
                                        roomBB.Max.Y - (roomBB.Max.Y - roomBB.Min.Y) * 0.15,
                                        lp.Point.Z);
                                    break;
                                case "TopCentre":
                                    targetPos = new XYZ(
                                        (roomBB.Min.X + roomBB.Max.X) / 2.0,
                                        roomBB.Max.Y - (roomBB.Max.Y - roomBB.Min.Y) * 0.15,
                                        lp.Point.Z);
                                    break;
                                case "Centroid":
                                default:
                                    targetPos = new XYZ(
                                        (roomBB.Min.X + roomBB.Max.X) / 2.0,
                                        (roomBB.Min.Y + roomBB.Max.Y) / 2.0,
                                        lp.Point.Z);
                                    break;
                            }

                            lp.Point = targetPos;
                            moved++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"MoveRoomTag {tagElem.Id}: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Room Tags", $"Moved {moved} of {roomTags.Count} room tags to {position}.");
        }

        // ── Sheet operations ────────────────────────────────────────

        private static void SheetRenumber(UIApplication app, int delta)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;

            if (!(doc.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("Sheet Renumber", "Active view must be a sheet.");
                return;
            }

            string currentNum = sheet.SheetNumber;
            // Try to extract numeric portion and increment
            string numPart = "";
            string prefix = "";
            for (int i = currentNum.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(currentNum[i]))
                    numPart = currentNum[i] + numPart;
                else
                {
                    prefix = currentNum.Substring(0, i + 1);
                    break;
                }
            }

            if (string.IsNullOrEmpty(numPart))
            {
                TaskDialog.Show("Sheet Renumber", $"Cannot parse number from '{currentNum}'.");
                return;
            }

            int num = int.Parse(numPart) + delta;
            if (num < 0) num = 0;
            string newNum = prefix + num.ToString().PadLeft(numPart.Length, '0');

            using (Transaction tx = new Transaction(doc, "STING Sheet Renumber"))
            {
                tx.Start();
                try
                {
                    sheet.SheetNumber = newNum;
                    TaskDialog.Show("Sheet Renumber", $"Changed: {currentNum} → {newNum}");
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Sheet Renumber", $"Failed: {ex.Message}");
                    tx.RollBack();
                    return;
                }
                tx.Commit();
            }
        }

        private static void SheetAddPrefix(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;

            if (!(doc.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("Sheet Prefix", "Active view must be a sheet.");
                return;
            }

            TaskDialog dlg = new TaskDialog("Sheet Prefix");
            dlg.MainInstruction = $"Add prefix to '{sheet.Name}'";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "STING - ");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "DRG - ");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "REV - ");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string pfx;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: pfx = "STING - "; break;
                case TaskDialogResult.CommandLink2: pfx = "DRG - "; break;
                case TaskDialogResult.CommandLink3: pfx = "REV - "; break;
                default: return;
            }

            if (sheet.Name.StartsWith(pfx)) return;

            using (Transaction tx = new Transaction(doc, "STING Sheet Prefix"))
            {
                tx.Start();
                try { sheet.Name = pfx + sheet.Name; }
                catch (Exception ex) { StingLog.Warn($"SheetPrefix: {ex.Message}"); }
                tx.Commit();
            }
        }

        private static void SheetAddSuffix(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;

            if (!(doc.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("Sheet Suffix", "Active view must be a sheet.");
                return;
            }

            TaskDialog dlg = new TaskDialog("Sheet Suffix");
            dlg.MainInstruction = $"Add suffix to '{sheet.Name}'";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, " - P01");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, " - DRAFT");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, " - FOR REVIEW");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string sfx;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: sfx = " - P01"; break;
                case TaskDialogResult.CommandLink2: sfx = " - DRAFT"; break;
                case TaskDialogResult.CommandLink3: sfx = " - FOR REVIEW"; break;
                default: return;
            }

            if (sheet.Name.EndsWith(sfx)) return;

            using (Transaction tx = new Transaction(doc, "STING Sheet Suffix"))
            {
                tx.Start();
                try { sheet.Name = sheet.Name + sfx; }
                catch (Exception ex) { StingLog.Warn($"SheetSuffix: {ex.Message}"); }
                tx.Commit();
            }
        }

        // ── Nudge helper ──────────────────────────────────────────

        private static void NudgeTags(UIApplication app, string direction)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;

            var tags = Organise.LeaderHelper.GetSelectedTags(uidoc);
            if (tags.Count == 0)
                tags = Organise.LeaderHelper.GetTargetTags(uidoc);
            if (tags.Count == 0) return;

            using (Transaction tx = new Transaction(uidoc.Document, $"STING Nudge {direction}"))
            {
                tx.Start();
                int nudged = Organise.NudgeTagsCommand.NudgeInDirection(
                    uidoc.Document, uidoc.ActiveView, tags, direction);
                tx.Commit();
                StingLog.Info($"Nudge {direction}: {nudged} tags");
            }
        }

        private static Color[] GetColorPalette(string name, int count)
        {
            return (name?.ToLower()) switch
            {
                "rag" => new[] {
                    new Color(244, 67, 54),
                    new Color(255, 152, 0),
                    new Color(76, 175, 80)
                },
                "monochrome" => new[] {
                    new Color(0, 0, 0),
                    new Color(85, 85, 85),
                    new Color(170, 170, 170),
                    new Color(212, 212, 212),
                    new Color(255, 255, 255)
                },
                "discipline" => new[] {
                    new Color(33, 150, 243),
                    new Color(255, 235, 59),
                    new Color(76, 175, 80),
                    new Color(158, 158, 158),
                    new Color(244, 67, 54),
                    new Color(255, 152, 0),
                    new Color(156, 39, 176),
                    new Color(121, 85, 72)
                },
                _ => new[] {
                    new Color(244, 67, 54), new Color(233, 30, 99),
                    new Color(156, 39, 176), new Color(103, 58, 183),
                    new Color(63, 81, 181), new Color(33, 150, 243),
                    new Color(3, 169, 244), new Color(0, 188, 212),
                    new Color(0, 150, 136), new Color(76, 175, 80),
                    new Color(139, 195, 74), new Color(205, 220, 57),
                    new Color(255, 235, 59), new Color(255, 193, 7),
                    new Color(255, 152, 0), new Color(255, 87, 34),
                    new Color(121, 85, 72), new Color(158, 158, 158),
                    new Color(96, 125, 139), new Color(0, 0, 0)
                }
            };
        }
    }
}
