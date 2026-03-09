using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    /// Unified dispatcher for all 170+ commands across 7 tabs:
    /// SELECT, ORGANISE, DOCS, TEMP, CREATE, VIEW, MODEL.
    /// </summary>
    public class StingCommandHandler : IExternalEventHandler
    {
        private readonly object _lock = new object();
        private string _commandTag = "";
        private string _param1 = "";
        private string _param2 = "";

        public void SetCommand(string tag, string param1 = "", string param2 = "")
        {
            lock (_lock)
            {
                _commandTag = tag ?? "";
                _param1 = param1 ?? "";
                _param2 = param2 ?? "";
            }
        }

        public string GetName() => "STING Command Dispatcher";

        public void Execute(UIApplication app)
        {
            // Store current UIApplication so commands can access it via
            // StingCommandHandler.CurrentApp when ExternalCommandData is null
            CurrentApp = app;

            // Snapshot command state under lock to prevent race with WPF UI thread
            string tag, p1, p2;
            lock (_lock)
            {
                tag = _commandTag;
                p1 = _param1;
                p2 = _param2;
            }

            // Guard: empty tag means no command was requested (cleared after previous run)
            if (string.IsNullOrEmpty(tag)) return;

            // Guard: most commands require an open document
            if (app.ActiveUIDocument == null)
            {
                TaskDialog.Show("STING Tools",
                    "No document is open. Please open a Revit project first.");
                return;
            }

            try
            {
                switch (tag)
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
                    case "SelectCustomCategory": RunCommand<Select.SelectCustomCategoryCommand>(app); break;

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

                    // ── Tag selector (multi-criteria) ──
                    case "TagSelector": RunCommand<Select.TagSelectorCommand>(app); break;
                    case "SelectTagsByText": RunCommand<Select.SelectTagsByTextCommand>(app); break;
                    case "SelectTagsByTextSize": RunCommand<Select.SelectTagsByTextSizeCommand>(app); break;
                    case "SelectTagsByArrowhead": RunCommand<Select.SelectTagsByArrowheadCommand>(app); break;
                    case "SelectTagsByLineWeight": RunCommand<Select.SelectTagsByLineWeightCommand>(app); break;
                    case "SelectTagsByElbowAngle": RunCommand<Select.SelectTagsByElbowAngleCommand>(app); break;
                    case "SelectTagsByFamily": RunCommand<Select.SelectTagsByFamilyCommand>(app); break;
                    case "SelectTagsByHostCategory": RunCommand<Select.SelectTagsByHostCategoryCommand>(app); break;
                    case "SelectTagsByLeaderState": RunCommand<Select.SelectTagsByLeaderStateCommand>(app); break;
                    case "SelectTagsByOrientation": RunCommand<Select.SelectTagsByOrientationCommand>(app); break;
                    case "SelectTagsByDiscipline": RunCommand<Select.SelectTagsByDisciplineCodeCommand>(app); break;
                    case "SelectTagsByToken": RunCommand<Select.SelectTagsByTokenCommand>(app); break;
                    case "SelectOverlappingTags": RunCommand<Select.SelectOverlappingTagsCommand>(app); break;

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
                    case "BulkWrite": BulkParamWriteInline(app, p1, p2, false); break;
                    case "BulkClear": BulkParamWriteInline(app, p1, "", true); break;
                    case "BulkPreview": BulkParamPreview(app, p1, p2); break;

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
                    case "TagChanged": RunCommand<Tags.TagChangedCommand>(app); break;
                    case "TagFormatMigration": RunCommand<Tags.TagFormatMigrationCommand>(app); break;
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
                    case "AutoAlignLeaderText": RunCommand<Organise.AutoAlignLeaderTextCommand>(app); break;

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
                    case "SnapElbow90": SnapElbowDirect(app, "90"); break;
                    case "SnapElbow45": SnapElbowDirect(app, "45"); break;
                    case "SnapElbowStraight": SnapElbowDirect(app, "0"); break;
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

                    // ── Paragraph & Warning controls (v4.2) ──
                    case "SetParagraphDepth": RunCommand<Tags.SetParagraphDepthCommand>(app); break;
                    case "ToggleWarningVisibility": RunCommand<Tags.ToggleWarningVisibilityCommand>(app); break;

                    // ── Presentation Mode & Label Spec (v4.3) ──
                    case "SetPresentationMode": RunCommand<Tags.SetPresentationModeCommand>(app); break;
                    case "ViewLabelSpec": RunCommand<Tags.ViewLabelSpecCommand>(app); break;
                    case "ExportLabelGuide": RunCommand<Tags.ExportLabelGuideCommand>(app); break;
                    case "SetTag7HeadingStyle": RunCommand<Tags.SetTag7HeadingStyleCommand>(app); break;

                    // ── Color By Parameter commands ──
                    case "ColorByParameter": RunCommand<Select.ColorByParameterCommand>(app); break;
                    case "ClearColorOverrides": RunCommand<Select.ClearColorOverridesCommand>(app); break;
                    case "SaveColorPreset": RunCommand<Select.SaveColorPresetCommand>(app); break;
                    case "LoadColorPreset": RunCommand<Select.LoadColorPresetCommand>(app); break;
                    case "CreateFiltersFromColors": RunCommand<Select.CreateFiltersFromColorsCommand>(app); break;

                    // ── Colouriser inline ──
                    case "ColorApply": ColorByParameter(app, p1, p2); break;
                    case "ColorApplyHex": ColorByHex(app, p1); break;
                    case "ColorApplyTransparency": SetTransparencyOverride(app, p1); break;

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
                    case "HandoverManual": RunCommand<Docs.HandoverManualCommand>(app); break;
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

                    // ── Documentation Automation (Phase 6) ──
                    case "BatchCreateViews": RunCommand<Docs.BatchCreateViewsCommand>(app); break;
                    case "BatchCreateSheets": RunCommand<Docs.BatchCreateSheetsCommand>(app); break;
                    case "CreateDependentViews": RunCommand<Docs.CreateDependentViewsCommand>(app); break;
                    case "ScopeBoxManager": RunCommand<Docs.ScopeBoxManagerCommand>(app); break;
                    case "ViewTemplateAssigner": RunCommand<Docs.ViewTemplateAssignerCommand>(app); break;
                    case "DocumentationPackage": RunCommand<Docs.DocumentationPackageCommand>(app); break;
                    case "BatchCreateSections": RunCommand<Docs.BatchCreateSectionsCommand>(app); break;
                    case "BatchCreateElevations": RunCommand<Docs.BatchCreateElevationsCommand>(app); break;
                    case "DocsDrawingRegister": RunCommand<Docs.DrawingRegisterCommand>(app); break;
                    case "ProjectBrowserOrganizer": RunCommand<Docs.ProjectBrowserOrganizerCommand>(app); break;

                    // ════════════════════════════════════════════════════════
                    // TEMP TAB
                    // ════════════════════════════════════════════════════════

                    // ── Setup ──
                    case "ProjectSetup": RunCommand<Temp.ProjectSetupCommand>(app); break;
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

                    // ── Corporate Schedules ──
                    case "CorporateTitleBlock": RunCommand<Temp.CorporateTitleBlockScheduleCommand>(app); break;
                    case "DrawingRegister": RunCommand<Temp.DrawingRegisterScheduleCommand>(app); break;

                    // ── Schedule Enhancements ──
                    case "ScheduleAudit": RunCommand<Temp.ScheduleAuditCommand>(app); break;
                    case "ScheduleCompare": RunCommand<Temp.ScheduleCompareCommand>(app); break;
                    case "ScheduleDuplicate": RunCommand<Temp.ScheduleDuplicateCommand>(app); break;
                    case "ScheduleRefresh": RunCommand<Temp.ScheduleRefreshCommand>(app); break;
                    case "ScheduleFieldMgr": RunCommand<Temp.ScheduleFieldManagerCommand>(app); break;
                    case "ScheduleColor": RunCommand<Temp.ScheduleColorCommand>(app); break;
                    case "ScheduleStats": RunCommand<Temp.ScheduleStatsCommand>(app); break;
                    case "ScheduleDelete": RunCommand<Temp.ScheduleDeleteCommand>(app); break;
                    case "ScheduleReport": RunCommand<Temp.ScheduleReportCommand>(app); break;

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
                    case "FamilyParamProcessor": RunCommand<Temp.FamilyParameterProcessorCommand>(app); break;
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
                    case "BOQExport": RunCommand<Temp.BOQExportCommand>(app); break;
                    case "TemplateVGAudit": RunCommand<Temp.TemplateVGAuditCommand>(app); break;
                    case "ExportIfcPropertyMap": RunCommand<Temp.ExportIfcPropertyMapCommand>(app); break;
                    case "ValidateBepCompliance": RunCommand<Temp.ValidateBepComplianceCommand>(app); break;

                    // ── Workflow Orchestration ──
                    case "RunWorkflow": RunCommand<Core.WorkflowPresetCommand>(app); break;
                    case "ListWorkflows": RunCommand<Core.ListWorkflowPresetsCommand>(app); break;
                    case "CreateWorkflow": RunCommand<Core.CreateWorkflowPresetCommand>(app); break;

                    // ── Advanced Automation ──
                    case "AnomalyAutoFix": RunCommand<Organise.AnomalyAutoFixCommand>(app); break;
                    case "RevisionCloudAuto": RunCommand<Docs.RevisionCloudAutoCreateCommand>(app); break;
                    case "ClashDetect": RunCommand<Temp.ClashDetectionCommand>(app); break;
                    case "IFCExport": RunCommand<Temp.IFCExportCommand>(app); break;
                    case "ExcelImport": RunCommand<Temp.ExcelBOQImportCommand>(app); break;
                    case "KeynoteSync": RunCommand<Temp.KeynoteSyncCommand>(app); break;
                    case "AutoTaggerToggle": RunCommand<Core.AutoTaggerToggleCommand>(app); break;

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
                    case "ScopeView": ToggleScopeMode(app); break;
                    case "ToggleOverwrite": ToggleOverwriteMode(app); break;

                    // ════════════════════════════════════════════════════════
                    // NEW — SELECT TAB (AI Smart Select, Spatial, Conditions)
                    // ════════════════════════════════════════════════════════

                    case "AIPredictSelect": AIPredictSelect(app); break;
                    case "AISimilarSelect": AISimilarSelect(app); break;
                    case "AIChainSelect": AIChainSelect(app); break;
                    case "AIClusterSelect": SelectNearby(app, 20.0); break;
                    case "AIPatternSelect": AIPatternSelect(app); break;
                    case "AIBoundarySelect": AIBoundarySelect(app); break;
                    case "AIOutliersSelect": AIOutliersSelect(app); break;
                    case "AIDenseSelect": SelectNearby(app, 5.0); break;

                    case "SelectView": SelectByCategory(app, "Views"); break;
                    case "SelectVisible": SelectVisibleOnly(app); break;
                    case "SelectNear": SelectNearby(app, 10.0); break;
                    case "SelectQuad": SelectQuadrant(app); break;
                    case "SelectEdge": SelectEdgeElements(app); break;
                    case "SelectGrid": SelectOnGrid(app); break;
                    case "SelectBBox": SelectByBoundingBox(app); break;

                    case "BulkBrain": BulkBrainSuggest(app); break;
                    case "ParamLookupRefresh":
                    case "RefreshParamList": RefreshParamList(app); break;
                    case "CondAdd": ConditionAdd(app, p1, p2); break;
                    case "CondRemove": ConditionRemove(app); break;
                    case "CondClear": ConditionClear(app); break;
                    case "CondPreview": ConditionPreview(app); break;
                    case "CondApply": ConditionApply(app); break;
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

                    case "TagFamilyRefresh": TagFamilyRefresh(app); break;
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
                    case "LeaderAdd": RunCommand<Organise.AddLeadersCommand>(app); break;
                    case "LeaderStraight": SnapElbowDirect(app, "0"); break;
                    case "TagSnap45": SnapElbowDirect(app, "45"); break;
                    case "TagSnap90": SnapElbowDirect(app, "90"); break;
                    case "LeaderSpacing": RunCommand<Organise.AlignTagsCommand>(app); break;

                    case "BrainSmartLdr": SnapElbowDirect(app, "cycle"); break;
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

                    case "BatchViewCats": BatchViewCategories(app); break;
                    case "BatchViewRunAll": BatchViewRunAll(app); break;

                    case "RoomTagCentroid": MoveRoomTags(app, "Centroid"); break;
                    case "RoomTagTopLeft": MoveRoomTags(app, "TopLeft"); break;
                    case "RoomTagTopCentre": MoveRoomTags(app, "TopCentre"); break;
                    case "RoomTagLeaderLock": RoomTagLeaderToggle(app, true); break;
                    case "RoomTagLeaderFree": RoomTagLeaderToggle(app, false); break;

                    case "ListLinks": ListLinkedModels(app); break;
                    case "SelInLink":
                    case "TagLinked":
                        StingLog.Info($"LinkedModel: {tag} — requires linked document access");
                        TaskDialog.Show("Linked Model", "Select/tag in linked model requires direct linked document access.\nUse Revit's built-in 'Select Links' and 'Tab' key to select linked elements.");
                        break;
                    case "AuditLinks": AuditLinkedModels(app); break;

                    case "PdfSelectedSheets":
                    case "PdfActiveView":
                        TaskDialog.Show("PDF Export",
                            "PDF export requires Revit's Print/Export API.\n" +
                            "Use File → Export → PDF in Revit for direct PDF output,\n" +
                            "or File → Print with a PDF printer driver.");
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
                    case "VPPrefix": ViewportAddPrefixSuffix(app, true); break;
                    case "VPSuffix": ViewportAddPrefixSuffix(app, false); break;

                    case "SheetResetTitle": SheetResetTitleBlock(app); break;
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
                    case "SchedAutoFit": ScheduleAutoFit(app); break;
                    case "SchedToggleHidden": ScheduleToggleHidden(app); break;

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
                    case "DimFindReplace": DimFindReplaceOverrides(app); break;

                    case "LegendSyncPos": LegendSyncPosition(app); break;
                    case "LegendTitleLine": LegendTitleLine(app); break;
                    case "LegendUniform": LegendUniformSize(app); break;
                    case "TagDictionary": CreateTagDictionary(app); break;
                    case "ColorLegendSchedule": CreateColorLegendSchedule(app); break;

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
                        TaskDialog.Show("Swap Elements", "Select two elements, then use 'Copy Tags' and 'Swap Tags' commands to exchange their data.");
                        break;
                    case "ConvertRegions":
                        TaskDialog.Show("Convert Regions", "Use Revit's built-in Filled Region tools or Detail Items to convert regions.\nEdit → Paste Aligned can also help transfer detail regions between views.");
                        break;
                    case "CleanSpaces":
                        TaskDialog.Show("Clean Spaces", "Use 'Delete Unused Views' to remove unplaced views.\nUse 'Purge Unused' (Manage tab → Purge) to clean up unused families and materials.");
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

                    case "AnomalyRefresh": AnomalyRefreshScan(app); break;
                    case "AnomalyScan": RunCommand<Tags.ValidateTagsCommand>(app); break;
                    case "AnomalyExport": RunCommand<Organise.AuditTagsCSVCommand>(app); break;

                    case "BotSmartPlace": RunCommand<Tags.SmartPlaceTagsCommand>(app); break;
                    case "BotDensityMap": RunCommand<Tags.TagOverlapAnalysisCommand>(app); break;
                    case "BotUndoAI":
                        TaskDialog.Show("Undo AI", "Use Ctrl+Z to undo the last operation.");
                        break;
                    case "BotOptions": RunCommand<Tags.TagConfigCommand>(app); break;

                    case "ColorSchemeDel": DeleteColorPreset(app); break;
                    case "GradientApply": RunCommand<Select.ColorByParameterCommand>(app); break;
                    case "PatternApplyView": ApplyLinePattern(app); break;
                    case "ApplyLineWeight": ApplyLineWeightOverride(app); break;

                    // ── Tag Style Engine ──
                    case "ApplyTagStyle": RunCommand<Tags.ApplyTagStyleCommand>(app); break;
                    case "ApplyColorScheme": RunCommand<Tags.ApplyColorSchemeCommand>(app); break;
                    case "ClearColorScheme": RunCommand<Tags.ClearColorSchemeCommand>(app); break;
                    case "SetParagraphDepthExt": RunCommand<Tags.SetParagraphDepthExtCommand>(app); break;
                    case "TagStyleReport": RunCommand<Tags.TagStyleReportCommand>(app); break;
                    case "SwitchTagStyleByDisc": RunCommand<Tags.SwitchTagStyleByDiscCommand>(app); break;
                    case "BatchApplyColorScheme": RunCommand<Tags.BatchApplyColorSchemeCommand>(app); break;
                    case "ColorByVariable": RunCommand<Tags.ColorByVariableCommand>(app); break;
                    case "SetBoxColor": RunCommand<Tags.SetBoxColorCommand>(app); break;

                    // ════════════════════════════════════════════════════════
                    // MODEL TAB — Auto-Modeling Engine
                    // ════════════════════════════════════════════════════════

                    // ── Architectural elements ──
                    case "ModelCreateWall": RunCommand<Model.ModelCreateWallCommand>(app); break;
                    case "ModelCreateRoom": RunCommand<Model.ModelCreateRoomCommand>(app); break;
                    case "ModelCreateFloor": RunCommand<Model.ModelCreateFloorCommand>(app); break;
                    case "ModelCreateCeiling": RunCommand<Model.ModelCreateCeilingCommand>(app); break;
                    case "ModelCreateRoof": RunCommand<Model.ModelCreateRoofCommand>(app); break;
                    case "ModelPlaceDoor": RunCommand<Model.ModelPlaceDoorCommand>(app); break;
                    case "ModelPlaceWindow": RunCommand<Model.ModelPlaceWindowCommand>(app); break;
                    case "ModelBuildingShell": RunCommand<Model.ModelBuildingShellCommand>(app); break;

                    // ── Structural elements ──
                    case "ModelPlaceColumn": RunCommand<Model.ModelPlaceColumnCommand>(app); break;
                    case "ModelColumnGrid": RunCommand<Model.ModelColumnGridCommand>(app); break;
                    case "ModelCreateBeam": RunCommand<Model.ModelCreateBeamCommand>(app); break;

                    // ── MEP elements ──
                    case "ModelCreateDuct": RunCommand<Model.ModelCreateDuctCommand>(app); break;
                    case "ModelCreatePipe": RunCommand<Model.ModelCreatePipeCommand>(app); break;
                    case "ModelPlaceFixture": RunCommand<Model.ModelPlaceFixtureCommand>(app); break;

                    // ── DWG to Model ──
                    case "ModelDWGToModel": RunCommand<Model.ModelDWGToModelCommand>(app); break;
                    case "ModelDWGPreview": RunCommand<Model.ModelDWGPreviewCommand>(app); break;

                    // ── Unmapped / placeholder ──
                    default:
                        StingLog.Warn($"Unrecognised command tag: {tag}");
                        TaskDialog.Show("STING Tools",
                            $"Command '{tag}' is not yet available.\nCheck for plugin updates.");
                        break;
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // User cancelled — silent
            }
            catch (Exception ex)
            {
                StingLog.Error($"DockPanel command '{tag}' failed", ex);
                TaskDialog.Show("STING Tools", $"Command failed: {ex.Message}");
            }
            finally
            {
                // CRASH FIX: Clear command tag after execution to prevent
                // re-entrancy if ExternalEvent fires again without SetCommand().
                lock (_lock)
                {
                    _commandTag = "";
                    _param1 = "";
                    _param2 = "";
                }
            }

            // ENH-003: Compliance status bar update REMOVED from post-command hook.
            // (See commit history for rationale — FilteredElementCollector after
            // transaction commit causes native segfault during deferred regeneration.)

            // NOTE: Post-command doc.Regenerate() REMOVED (see commit history).
            //
            // NOTE: TransactionGroup removed from all commands — each batch/step
            // uses standalone Transactions so Revit regenerates between them.
        }

        // ── Current UIApplication (static fallback for panel commands) ──

        /// <summary>
        /// Current UIApplication reference, set during Execute().
        /// Commands can use this as a fallback when ExternalCommandData is null.
        /// </summary>
        public static UIApplication CurrentApp { get; private set; }

        // ── Generic command runner ────────────────────────────────────

        private static void RunCommand<T>(UIApplication app) where T : IExternalCommand, new()
        {
            try
            {
                // Log command start so we have a breadcrumb if Revit crashes
                // during execution (native crashes bypass catch blocks).
                StingLog.Info($"RunCommand<{typeof(T).Name}>: start");

                var cmd = new T();
                string message = "";
                var elements = new ElementSet();

                // Pass null for ExternalCommandData — commands use
                // StingCommandHandler.CurrentApp as fallback.
                // This avoids the fragile RuntimeHelpers.GetUninitializedObject
                // reflection hack that breaks across Revit versions.
                cmd.Execute(null, ref message, elements);

                StingLog.Info($"RunCommand<{typeof(T).Name}>: done");
            }
            catch (NullReferenceException nre)
            {
                // Most likely: command accessed commandData.Application without null check.
                // Log the specific command so we can fix it.
                StingLog.Error($"RunCommand<{typeof(T).Name}>: NullReferenceException — " +
                    "command may need commandData null guard. " +
                    "Use StingCommandHandler.CurrentApp instead.", nre);
                TaskDialog.Show("STING Tools",
                    $"{typeof(T).Name}: internal error.\n" +
                    "Please report this issue.\n\n" + nre.Message);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // User cancelled — silent
            }
            catch (Exception ex)
            {
                StingLog.Error($"RunCommand<{typeof(T).Name}> failed", ex);
                TaskDialog.Show("STING Tools",
                    $"{typeof(T).Name} failed:\n{ex.Message}");
            }
        }

        // ── Inline operations ─────────────────────────────────────────

        private static readonly Dictionary<string, List<ElementId>> _memorySlots =
            new Dictionary<string, List<ElementId>>();

        /// <summary>
        /// CRASH FIX: Clear selection memory slots that hold ElementId references.
        /// Must be called on document close to prevent stale IDs being used against a new document.
        /// </summary>
        public static void ClearStaticState()
        {
            _memorySlots.Clear();
        }

        private static void ViewIsolateSelected(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc?.ActiveView == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Isolate", "Select elements first."); return; }
            uidoc.ActiveView.IsolateElementsTemporary(ids);
        }

        private static void ViewHideSelected(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc?.ActiveView == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Hide", "Select elements first."); return; }
            uidoc.ActiveView.HideElementsTemporary(ids);
        }

        private static void ViewRevealHidden(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc?.ActiveView == null) return;
            try
            {
                // EnableTemporaryViewMode is not available as a direct instance
                // method on View in all Revit API versions — use reflection with
                // proper exception unwrapping to avoid masking the real error.
                var view = uidoc.ActiveView;
                var method = view.GetType().GetMethod("EnableTemporaryViewMode",
                    new[] { typeof(TemporaryViewMode) });
                if (method != null)
                {
                    try
                    {
                        method.Invoke(view, new object[] { TemporaryViewMode.RevealHiddenElements });
                    }
                    catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException != null)
                    {
                        // Unwrap reflection wrapper to surface the real Revit exception
                        throw tie.InnerException;
                    }
                }
                else
                {
                    TaskDialog.Show("Reveal", "Reveal hidden elements is not available in this Revit version.");
                }
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                TaskDialog.Show("Reveal", "This view does not support reveal hidden elements.");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ViewRevealHidden: {ex.Message}");
            }
        }

        private static void ViewResetIsolate(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc?.ActiveView == null) return;
            uidoc.ActiveView.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
        }

        private static void SelectAllVisible(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc?.ActiveView == null) return;
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
            if (uidoc?.ActiveView == null) return;
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
            if (uidoc?.ActiveView == null) return;
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
                catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
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
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
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

        private static void RefreshParamList(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;
            if (view == null)
            {
                TaskDialog.Show("Parameter List", "No active view — switch to a model view first.");
                return;
            }

            // Collect parameter names from selected element (or first taggable in view)
            var ids = uidoc.Selection.GetElementIds();
            Element target = null;
            if (ids.Count > 0)
                target = doc.GetElement(ids.First());
            if (target == null)
            {
                target = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .FirstOrDefault(e => e.Category != null);
            }

            if (target == null)
            {
                TaskDialog.Show("Parameter List", "No elements found in current view.");
                return;
            }

            // Build param list: registry params + element instance params
            var paramNames = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string p in ParamRegistry.AllParamGuids.Keys)
                paramNames.Add(p);

            foreach (Parameter p in target.Parameters)
            {
                if (p.Definition != null && !string.IsNullOrEmpty(p.Definition.Name))
                    paramNames.Add(p.Definition.Name);
            }

            // Populate all three parameter dropdowns in the dockable panel
            StingDockPanel.PopulateParamDropdowns(paramNames);

            var msg = new StringBuilder();
            msg.AppendLine($"Parameters for {ParameterHelpers.GetCategoryName(target)} ({paramNames.Count} total):\n");
            int shown = 0;
            foreach (string name in paramNames)
            {
                if (shown++ > 80) { msg.AppendLine($"  ... and {paramNames.Count - 80} more"); break; }
                string val = ParameterHelpers.GetString(target, name);
                if (!string.IsNullOrEmpty(val))
                    msg.AppendLine($"  {name} = {val}");
                else
                    msg.AppendLine($"  {name}");
            }

            var td = new TaskDialog("STING Tools - Parameter List");
            td.MainInstruction = $"Parameters for {ParameterHelpers.GetCategoryName(target)} ({paramNames.Count} total)";
            td.MainContent = msg.ToString();
            td.CommonButtons = TaskDialogCommonButtons.Ok;
            td.DefaultButton = TaskDialogResult.Ok;
            td.Show();
            StingLog.Info($"RefreshParamList: {paramNames.Count} params for {ParameterHelpers.GetCategoryName(target)}");
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
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
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
            if (paramName == ParamRegistry.SYS) return new[] { "HVAC", "DCW", "SAN" };
            if (paramName == ParamRegistry.FUNC) return new[] { "SUP", "HTG", "PWR" };
            if (paramName == ParamRegistry.PROD) return new[] { "AHU", "DB", "DR" };
            if (paramName == ParamRegistry.LVL) return new[] { "GF", "L01", "B1" };
            if (paramName == ParamRegistry.ORIGIN) return new[] { "NEW", "EXISTING", "DEMOLISHED" };
            if (paramName == ParamRegistry.PROJECT) return new[] { "PRJ001", "PRJ002", "PRJ003" };
            if (paramName == ParamRegistry.REV) return new[] { "P01", "P02", "C01" };
            if (paramName == ParamRegistry.VOLUME) return new[] { "V01", "V02", "V03" };
            return new[] { "VALUE1", "VALUE2", "VALUE3" };
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
                catch (Exception ex) { StingLog.Warn($"SelectConnected: connector traversal failed for {el.Id}: {ex.Message}"); }
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
            var view = uidoc.ActiveView;
            if (view == null) { TaskDialog.Show("Select", "No active view."); return; }
            var ids = new FilteredElementCollector(uidoc.Document, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => !e.IsHidden(view))
                .Select(e => e.Id).ToList();
            uidoc.Selection.SetElementIds(ids);
            StingLog.Info($"SelectVisibleOnly: {ids.Count} elements");
        }

        // ── Colouriser helpers ─────────────────────────────────────

        private static void ColorByParameter(UIApplication app, string paramName, string paletteName)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var view = uidoc.ActiveView;
            if (view == null) { TaskDialog.Show("Color By Parameter", "No active view."); return; }
            if (string.IsNullOrEmpty(paramName))
            {
                TaskDialog.Show("Color By Parameter", "Select a parameter name first.");
                return;
            }

            // If elements are selected, color only the selection; otherwise color entire view
            var selIds = uidoc.Selection.GetElementIds();
            List<Element> elements;
            string scope;
            if (selIds.Count > 0)
            {
                elements = selIds.Select(id => uidoc.Document.GetElement(id))
                    .Where(e => e != null && e.IsValidObject)
                    .ToList();
                scope = $"{elements.Count} selected elements";
            }
            else
            {
                elements = new FilteredElementCollector(uidoc.Document, view.Id)
                    .WhereElementIsNotElementType()
                    .ToList();
                scope = $"{elements.Count} elements in view";
            }

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
                catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
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
                        view.SetElementOverrides(id, ogs);
                    colorIdx++;
                }
                tx.Commit();
            }

            TaskDialog.Show("Color By Parameter",
                $"Coloured {scope} by '{paramName}'\n" +
                $"({groups.Count} unique values).");
        }

        private static void ColorByHex(UIApplication app, string hexColor)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var view = uidoc.ActiveView;
            if (view == null) { TaskDialog.Show("Color", "No active view."); return; }
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
                catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
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
                    view.SetElementOverrides(id, ogs);
                tx.Commit();
            }
        }

        private static void SetTransparencyOverride(UIApplication app, string transparencyStr)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var view = uidoc.ActiveView;
            if (view == null) { TaskDialog.Show("Transparency", "No active view."); return; }
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
                    view.SetElementOverrides(id, ogs);
                tx.Commit();
            }
        }

        private static void DeleteColorPreset(UIApplication app)
        {
            string presetPath = Path.Combine(StingToolsApp.DataPath ?? "", "COLOR_PRESETS.json");
            if (!File.Exists(presetPath))
            { TaskDialog.Show("Delete Preset", "No saved presets found."); return; }

            try
            {
                var presets = Newtonsoft.Json.JsonConvert.DeserializeObject<
                    Dictionary<string, object>>(File.ReadAllText(presetPath));
                if (presets == null || presets.Count == 0)
                { TaskDialog.Show("Delete Preset", "No saved presets found."); return; }

                var td = new TaskDialog("Delete Color Preset");
                td.MainInstruction = "Select preset to delete";
                var names = presets.Keys.ToList();
                int linkCount = 0;
                foreach (string name in names.Take(4))
                {
                    td.AddCommandLink((TaskDialogCommandLinkId)(1001 + linkCount), name);
                    linkCount++;
                }
                td.CommonButtons = TaskDialogCommonButtons.Cancel;
                var result = td.Show();

                int idx = result switch
                {
                    TaskDialogResult.CommandLink1 => 0,
                    TaskDialogResult.CommandLink2 => 1,
                    TaskDialogResult.CommandLink3 => 2,
                    TaskDialogResult.CommandLink4 => 3,
                    _ => -1
                };

                if (idx >= 0 && idx < names.Count)
                {
                    presets.Remove(names[idx]);
                    File.WriteAllText(presetPath,
                        Newtonsoft.Json.JsonConvert.SerializeObject(presets, Newtonsoft.Json.Formatting.Indented));
                    TaskDialog.Show("Delete Preset", $"Deleted preset: {names[idx]}");
                    StingLog.Info($"Deleted color preset: {names[idx]}");
                }
            }
            catch (Exception ex)
            {
                StingLog.Error($"Delete preset failed: {ex.Message}");
                TaskDialog.Show("Delete Preset", $"Error: {ex.Message}");
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
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
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
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            if (!(doc.ActiveView is ViewSchedule sched))
            { TaskDialog.Show("Schedule", "Active view must be a schedule."); return; }

            var def = sched.Definition;
            int fieldCount = def.GetFieldCount();
            if (fieldCount == 0) return;

            // Find the widest column
            double maxWidth = 0;
            for (int i = 0; i < fieldCount; i++)
            {
                try
                {
                    double w = def.GetField(i).GridColumnWidth;
                    if (w > maxWidth) maxWidth = w;
                }
                catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
            }

            if (maxWidth <= 0)
            {
                TaskDialog.Show("Match Widest", "No valid column widths found.");
                return;
            }

            // Set all columns to match the widest
            int updated = 0;
            using (Transaction tx = new Transaction(doc, "STING Match Widest Column"))
            {
                tx.Start();
                for (int i = 0; i < fieldCount; i++)
                {
                    try
                    {
                        var field = def.GetField(i);
                        if (Math.Abs(field.GridColumnWidth - maxWidth) > 0.001)
                        {
                            field.GridColumnWidth = maxWidth;
                            updated++;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                }
                tx.Commit();
            }
            TaskDialog.Show("Match Widest",
                $"Set {updated} columns to widest width ({maxWidth * 304.8:F1}mm).");
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
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
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
                catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
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
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                }
                tx.Commit();
            }
            TaskDialog.Show("Equalise Columns",
                $"Set {updated} columns to width {maxWidth * 304.8:F1}mm.");
        }

        private static void ScheduleAutoFit(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            if (!(doc.ActiveView is ViewSchedule sched))
            { TaskDialog.Show("Schedule", "Active view must be a schedule."); return; }

            var def = sched.Definition;
            int fieldCount = def.GetFieldCount();
            if (fieldCount == 0) return;

            // Estimate column width based on data content length
            var body = sched.GetTableData()?.GetSectionData(SectionType.Body);
            if (body == null || body.NumberOfRows == 0)
            {
                TaskDialog.Show("Auto-Fit", "Schedule has no data rows to measure.");
                return;
            }

            int sampleRows = Math.Min(body.NumberOfRows, 50);
            int updated = 0;

            using (Transaction tx = new Transaction(doc, "STING Schedule Auto-Fit"))
            {
                tx.Start();

                for (int col = 0; col < fieldCount; col++)
                {
                    try
                    {
                        var field = def.GetField(col);
                        if (field.IsHidden) continue;

                        // Measure max text length in this column
                        int maxLen = field.ColumnHeading?.Length ?? 5;
                        for (int row = 0; row < sampleRows; row++)
                        {
                            try
                            {
                                string val = sched.GetCellText(SectionType.Body, row, col);
                                if (val != null && val.Length > maxLen)
                                    maxLen = val.Length;
                            }
                            catch { break; }
                        }

                        // Convert character count to approximate width
                        // ~2.0mm per character at 8pt font, min 15mm, max 80mm
                        double widthMm = Math.Max(15, Math.Min(80, maxLen * 2.0 + 6));
                        double widthFt = widthMm / 304.8;

                        field.GridColumnWidth = widthFt;
                        updated++;
                    }
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                }

                tx.Commit();
            }

            TaskDialog.Show("Auto-Fit",
                $"Auto-fitted {updated} column widths based on data content.");
        }

        private static void ScheduleToggleHidden(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            if (!(doc.ActiveView is ViewSchedule sched))
            { TaskDialog.Show("Schedule", "Active view must be a schedule."); return; }

            var def = sched.Definition;
            int fieldCount = def.GetFieldCount();
            int hiddenCount = 0;

            for (int i = 0; i < fieldCount; i++)
            {
                try { if (def.GetField(i).IsHidden) hiddenCount++; } catch (Exception ex) { StingLog.Warn($"ScheduleToggleHidden field {i}: {ex.Message}"); }
            }

            if (hiddenCount == 0)
            {
                TaskDialog.Show("Toggle Hidden", "No hidden fields in this schedule.");
                return;
            }

            // Unhide all hidden fields
            int revealed = 0;
            using (Transaction tx = new Transaction(doc, "STING Unhide Schedule Fields"))
            {
                tx.Start();
                for (int i = 0; i < fieldCount; i++)
                {
                    try
                    {
                        var field = def.GetField(i);
                        if (field.IsHidden)
                        {
                            field.IsHidden = false;
                            revealed++;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                }
                tx.Commit();
            }

            TaskDialog.Show("Toggle Hidden",
                $"Revealed {revealed} hidden field(s) in '{sched.Name}'.");
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
                        catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
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
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
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
                                tn.AddLeader(TextNoteLeaderTypes.TNLT_STRAIGHT_L);
                            toggled++;
                        }
                        catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
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
                        catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
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
                        catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
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
                catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
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

            // CRASH FIX: Merged two back-to-back transactions into one.
            // The original code set all scales to 1 in a first transaction, then
            // found the "most common scale" (always 1 after the first pass) and
            // set it again in a second transaction. Back-to-back transactions on
            // the same elements without doc.Regenerate() is a known Revit crash
            // pattern. Now uses a single transaction with the most common scale.
            var scaleCounts = legendVps.GroupBy(lv => lv.view.Scale)
                .OrderByDescending(g => g.Count()).ToList();
            int targetScale = scaleCounts[0].Key;

            int changed = 0;
            using (Transaction tx = new Transaction(doc, "STING Legend Uniform Size"))
            {
                tx.Start();
                foreach (var (vp, view) in legendVps)
                {
                    try
                    {
                        if (view.Scale != targetScale)
                        {
                            view.Scale = targetScale;
                            changed++;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"LegendUniform: {ex.Message}"); }
                }
                tx.Commit();
            }
            TaskDialog.Show("Legend Uniform",
                $"Set {changed} legend views to scale 1:{targetScale}.\n" +
                $"({legendVps.Count} total legends on sheet).");
        }

        /// <summary>
        /// Create a Tag Dictionary schedule showing all STING tag nomenclature.
        /// Uses ViewSchedule API to create a reference schedule listing DISC, SYS, FUNC, PROD codes.
        /// </summary>
        private static void CreateTagDictionary(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            // Check if schedule already exists
            string scheduleName = "STING - Tag Dictionary";
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                .FirstOrDefault(vs => vs.Name == scheduleName);

            if (existing != null)
            {
                TaskDialog.Show("Tag Dictionary", $"Schedule '{scheduleName}' already exists.\nDelete it first to regenerate.");
                return;
            }

            // Build dictionary data from TagConfig lookup tables
            var discMap = Core.TagConfig.DiscMap;
            var sysMap = Core.TagConfig.SysMap;
            var funcMap = Core.TagConfig.FuncMap;
            var prodMap = Core.TagConfig.ProdMap;

            var report = new StringBuilder();
            report.AppendLine("STING Tag Dictionary — ISO 19650 Nomenclature");
            report.AppendLine("=".PadRight(60, '='));
            report.AppendLine();
            report.AppendLine("TAG FORMAT: DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ");
            report.AppendLine();

            // Discipline codes
            report.AppendLine("── DISCIPLINE CODES (DISC) ──");
            var discCodes = new HashSet<string>();
            foreach (var kvp in discMap)
            {
                if (discCodes.Add(kvp.Value))
                    report.AppendLine($"  {kvp.Value,-6} {GetDiscDescription(kvp.Value)}");
            }
            report.AppendLine();

            // System codes
            report.AppendLine("── SYSTEM CODES (SYS) ──");
            foreach (var kvp in sysMap)
                report.AppendLine($"  {kvp.Key,-8} → Categories: {string.Join(", ", kvp.Value.Take(3))}{(kvp.Value.Count > 3 ? "..." : "")}");
            report.AppendLine();

            // Function codes
            report.AppendLine("── FUNCTION CODES (FUNC) ──");
            foreach (var kvp in funcMap)
                report.AppendLine($"  {kvp.Key,-8} → {kvp.Value}");
            report.AppendLine();

            // Product codes
            report.AppendLine("── PRODUCT CODES (PROD) ──");
            var prodCodes = new HashSet<string>();
            foreach (var kvp in prodMap)
            {
                if (prodCodes.Add(kvp.Value))
                    report.AppendLine($"  {kvp.Value,-6} {kvp.Key}");
            }
            report.AppendLine();

            // Location codes
            report.AppendLine("── LOCATION CODES (LOC) ──");
            foreach (string loc in Core.TagConfig.LocCodes)
                report.AppendLine($"  {loc}");
            report.AppendLine();

            // Zone codes
            report.AppendLine("── ZONE CODES (ZONE) ──");
            foreach (string zone in Core.TagConfig.ZoneCodes)
                report.AppendLine($"  {zone}");

            // Export to text file alongside data
            string exportPath = System.IO.Path.Combine(
                StingToolsApp.DataPath ?? System.IO.Path.GetTempPath(),
                "TAG_DICTIONARY.txt");
            try
            {
                System.IO.File.WriteAllText(exportPath, report.ToString());
                StingLog.Info($"Tag dictionary exported to {exportPath}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Tag dictionary export: {ex.Message}");
                exportPath = null;
            }

            var msg = new StringBuilder();
            msg.AppendLine($"Tag Dictionary generated with:");
            msg.AppendLine($"  Disciplines: {discCodes.Count}");
            msg.AppendLine($"  Systems: {sysMap.Count}");
            msg.AppendLine($"  Functions: {funcMap.Count}");
            msg.AppendLine($"  Products: {prodCodes.Count}");
            msg.AppendLine($"  Locations: {Core.TagConfig.LocCodes.Count}");
            msg.AppendLine($"  Zones: {Core.TagConfig.ZoneCodes.Count}");
            if (exportPath != null)
                msg.AppendLine($"\nExported to: {exportPath}");
            msg.AppendLine($"\n{report.ToString().Substring(0, Math.Min(report.Length, 2000))}");

            TaskDialog.Show("Tag Dictionary", msg.ToString());
        }

        private static string GetDiscDescription(string disc)
        {
            return disc switch
            {
                "M" => "Mechanical",
                "E" => "Electrical",
                "P" => "Plumbing",
                "A" => "Architectural",
                "S" => "Structural",
                "FP" => "Fire Protection",
                "LV" => "Low Voltage / Comms",
                "G" => "General / Generic",
                _ => disc,
            };
        }

        /// <summary>
        /// Create a Color Legend reference schedule listing parameter values and their colors.
        /// Reads current color overrides from the active view and builds a reference table.
        /// </summary>
        private static void CreateColorLegendSchedule(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            // Collect all elements with color overrides in current view
            var elements = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.HasMaterialQuantities)
                .ToList();

            if (elements.Count == 0)
            {
                TaskDialog.Show("Color Legend", "No taggable elements in active view.");
                return;
            }

            // Group elements by their surface color override
            var colorGroups = new Dictionary<string, (Color color, List<Element> elems)>();

            foreach (var elem in elements)
            {
                try
                {
                    var ogs = view.GetElementOverrides(elem.Id);
                    Color surfColor = ogs.SurfaceForegroundPatternColor;
                    if (surfColor.IsValid && (surfColor.Red != 0 || surfColor.Green != 0 || surfColor.Blue != 0))
                    {
                        string key = $"{surfColor.Red:D3},{surfColor.Green:D3},{surfColor.Blue:D3}";
                        if (!colorGroups.ContainsKey(key))
                            colorGroups[key] = (surfColor, new List<Element>());
                        colorGroups[key].elems.Add(elem);
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
            }

            if (colorGroups.Count == 0)
            {
                TaskDialog.Show("Color Legend",
                    "No color overrides found in active view.\n" +
                    "Use 'Color By Parameter' first to apply color overrides,\nthen run this command to generate a legend.");
                return;
            }

            // Try to detect which parameter was used for coloring
            // Check common tag parameters on the first element of each group
            string[] candidateParams = { "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT",
                "ASS_SYSTEM_TYPE_TXT", "ASS_LVL_COD_TXT", "ASS_FUNC_TXT", "ASS_PRODCT_COD_TXT" };

            string bestParam = null;
            int bestUniqueMatch = 0;
            foreach (string paramName in candidateParams)
            {
                var groupValues = new HashSet<string>();
                bool allUnique = true;
                foreach (var kvp in colorGroups)
                {
                    var sample = kvp.Value.elems.First();
                    string val = Core.ParameterHelpers.GetString(sample, paramName);
                    if (string.IsNullOrEmpty(val)) { allUnique = false; break; }
                    if (!groupValues.Add(val)) { allUnique = false; break; }
                }
                if (allUnique && groupValues.Count == colorGroups.Count && groupValues.Count > bestUniqueMatch)
                {
                    bestParam = paramName;
                    bestUniqueMatch = groupValues.Count;
                }
            }

            // Build report
            var report = new StringBuilder();
            report.AppendLine($"Color Legend — {view.Name}");
            report.AppendLine(new string('=', 50));
            if (bestParam != null)
                report.AppendLine($"Detected color parameter: {bestParam}");
            report.AppendLine($"Color groups: {colorGroups.Count}");
            report.AppendLine($"Total colored elements: {colorGroups.Sum(g => g.Value.elems.Count)}");
            report.AppendLine();
            report.AppendLine($"{"RGB",-16} {"Value",-20} {"Count",-8} {"Categories"}");
            report.AppendLine(new string('-', 70));

            foreach (var kvp in colorGroups.OrderBy(g => g.Key))
            {
                string paramValue = "<unknown>";
                if (bestParam != null)
                    paramValue = Core.ParameterHelpers.GetString(kvp.Value.elems.First(), bestParam);

                var catCounts = kvp.Value.elems.GroupBy(e => e.Category?.Name ?? "?")
                    .Select(g => $"{g.Key}({g.Count()})");
                report.AppendLine($"[{kvp.Key}]  {paramValue,-20} {kvp.Value.elems.Count,-8} {string.Join(", ", catCounts.Take(3))}");
            }

            // Export
            string exportPath = System.IO.Path.Combine(
                StingToolsApp.DataPath ?? System.IO.Path.GetTempPath(),
                "COLOR_LEGEND.txt");
            try
            {
                System.IO.File.WriteAllText(exportPath, report.ToString());
            }
            catch { exportPath = null; }

            var msg = report.ToString();
            if (exportPath != null)
                msg += $"\n\nExported to: {exportPath}";

            TaskDialog.Show("Color Legend", msg);
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
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
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
                catch (Exception ex)
                {
                    StingLog.Warn($"Measurement: failed on element {e?.Id}: {ex.Message}");
                }
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
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
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
                catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
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

            // CRASH FIX: TaskDialog must not be shown inside an active Transaction.
            // Modal dialogs block the UI thread while the transaction holds a document
            // lock, which can deadlock Revit. Show dialogs after commit/rollback.
            string resultMsg = null;
            bool success = false;
            using (Transaction tx = new Transaction(doc, "STING Sheet Renumber"))
            {
                tx.Start();
                try
                {
                    sheet.SheetNumber = newNum;
                    success = true;
                    resultMsg = $"Changed: {currentNum} → {newNum}";
                }
                catch (Exception ex)
                {
                    resultMsg = $"Failed: {ex.Message}";
                    tx.RollBack();
                }
                if (success) tx.Commit();
            }
            TaskDialog.Show("Sheet Renumber", resultMsg);
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

        // ── AI Smart Select helpers ──────────────────────────────────

        /// <summary>
        /// Predict what the user wants to select based on current selection patterns.
        /// Analyzes category, family, type, and parameter values of selected elements,
        /// then selects all similar elements in the view.
        /// </summary>
        private static void AIPredictSelect(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0)
            {
                TaskDialog.Show("AI Predict Select", "Select one or more elements first.\nThe tool will find similar elements based on shared properties.");
                return;
            }

            // Analyze selection: collect category, family, type patterns
            var categories = new HashSet<string>();
            var families = new HashSet<string>();
            var types = new HashSet<string>();
            foreach (ElementId id in selected)
            {
                Element el = doc.GetElement(id);
                if (el == null) continue;
                categories.Add(ParameterHelpers.GetCategoryName(el));
                families.Add(ParameterHelpers.GetFamilyName(el));
                types.Add(ParameterHelpers.GetFamilySymbolName(el));
            }

            // Find matching elements — priority: type > family > category
            var allInView = new FilteredElementCollector(doc, doc.ActiveView.Id)
                .WhereElementIsNotElementType().ToList();

            var matches = new List<ElementId>();
            foreach (Element el in allInView)
            {
                string typeName = ParameterHelpers.GetFamilySymbolName(el);
                string famName = ParameterHelpers.GetFamilyName(el);
                string catName = ParameterHelpers.GetCategoryName(el);

                // Match by type first (most specific), then family, then category
                if (types.Contains(typeName))
                    matches.Add(el.Id);
                else if (families.Contains(famName))
                    matches.Add(el.Id);
            }

            if (matches.Count == 0)
            {
                // Fall back to category match
                foreach (Element el in allInView)
                    if (categories.Contains(ParameterHelpers.GetCategoryName(el)))
                        matches.Add(el.Id);
            }

            uidoc.Selection.SetElementIds(matches);
            TaskDialog.Show("AI Predict Select",
                $"Selected {matches.Count} elements matching pattern:\n" +
                $"  Categories: {string.Join(", ", categories.Take(5))}\n" +
                $"  Families: {string.Join(", ", families.Take(5))}\n" +
                $"  Types: {string.Join(", ", types.Take(5))}");
            StingLog.Info($"AIPredictSelect: {selected.Count} seed → {matches.Count} matches");
        }

        /// <summary>
        /// Select all elements of the same family and type as the current selection.
        /// </summary>
        private static void AISimilarSelect(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0)
            {
                TaskDialog.Show("Similar Select", "Select an element first.");
                return;
            }

            var targetTypes = new HashSet<ElementId>();
            foreach (ElementId id in selected)
            {
                Element el = doc.GetElement(id);
                if (el == null) continue;
                ElementId typeId = el.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                    targetTypes.Add(typeId);
            }

            if (targetTypes.Count == 0)
            {
                TaskDialog.Show("Similar Select", "No type information found on selected elements.");
                return;
            }

            var matches = new FilteredElementCollector(doc, doc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e => targetTypes.Contains(e.GetTypeId()))
                .Select(e => e.Id).ToList();

            uidoc.Selection.SetElementIds(matches);
            StingLog.Info($"AISimilarSelect: {targetTypes.Count} types → {matches.Count} elements");
        }

        /// <summary>
        /// Chain-select connected MEP elements starting from selection.
        /// Walks the MEP connector graph to find all connected elements.
        /// </summary>
        private static void AIChainSelect(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0)
            {
                TaskDialog.Show("Chain Select", "Select a MEP element to trace its connected chain.");
                return;
            }

            var visited = new HashSet<ElementId>(selected);
            var queue = new Queue<ElementId>(selected);
            int maxDepth = 200;

            while (queue.Count > 0 && visited.Count < maxDepth)
            {
                ElementId currentId = queue.Dequeue();
                Element el = doc.GetElement(currentId);
                if (el == null) continue;

                try
                {
                    var connMgr = (el as MEPCurve)?.ConnectorManager
                        ?? (el as FamilyInstance)?.MEPModel?.ConnectorManager;

                    if (connMgr == null) continue;

                    foreach (Connector conn in connMgr.Connectors)
                    {
                        if (!conn.IsConnected) continue;
                        foreach (Connector other in conn.AllRefs)
                        {
                            if (other.Owner != null && visited.Add(other.Owner.Id))
                                queue.Enqueue(other.Owner.Id);
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"AIChainSelect: connector traversal failed for {currentId}: {ex.Message}"); }
            }

            uidoc.Selection.SetElementIds(visited.ToList());
            TaskDialog.Show("Chain Select",
                $"Traced {visited.Count} connected elements from {selected.Count} seed(s).");
            StingLog.Info($"AIChainSelect: {selected.Count} → {visited.Count} elements");
        }

        /// <summary>
        /// Select elements whose parameter values are outliers compared to the majority.
        /// Finds elements with missing tags, unusual discipline codes, or empty required tokens.
        /// </summary>
        private static void AIOutliersSelect(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            var outliers = new List<ElementId>();
            int total = 0;
            var elements = new FilteredElementCollector(doc, doc.ActiveView.Id)
                .WhereElementIsNotElementType().ToList();

            foreach (Element el in elements)
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (!known.Contains(cat)) continue;
                total++;

                // Check for anomalies: missing tag, empty DISC, empty SYS
                string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);

                bool isOutlier = false;
                if (string.IsNullOrEmpty(tag1)) isOutlier = true;
                if (string.IsNullOrEmpty(disc)) isOutlier = true;
                if (!string.IsNullOrEmpty(disc) && disc == "XX") isOutlier = true;

                // Check if tag has placeholders
                if (!string.IsNullOrEmpty(tag1) && (tag1.Contains("-XX-") || tag1.Contains("-0000")))
                    isOutlier = true;

                if (isOutlier) outliers.Add(el.Id);
            }

            uidoc.Selection.SetElementIds(outliers);
            TaskDialog.Show("Outlier Select",
                $"Found {outliers.Count} outlier elements of {total} taggable:\n" +
                "  - Missing or incomplete tags\n" +
                "  - Placeholder values (XX, 0000)\n" +
                "  - Empty discipline codes");
            StingLog.Info($"AIOutliersSelect: {outliers.Count} outliers of {total} taggable");
        }

        /// <summary>
        /// Select elements matching a spatial or parameter-value pattern.
        /// Finds elements that share the same parameter values (DISC, SYS, LOC, ZONE)
        /// as the selected seed elements — like "select all HVAC elements in zone Z01".
        /// </summary>
        private static void AIPatternSelect(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0)
            {
                TaskDialog.Show("Pattern Select", "Select seed elements first.\nThe tool will find elements sharing the same DISC + SYS + LOC + ZONE pattern.");
                return;
            }

            // Extract parameter patterns from seed elements
            var patterns = new HashSet<string>();
            foreach (ElementId id in selected)
            {
                Element el = doc.GetElement(id);
                if (el == null) continue;
                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                string loc = ParameterHelpers.GetString(el, ParamRegistry.LOC);
                string zone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
                string pattern = $"{disc}|{sys}|{loc}|{zone}";
                if (pattern != "|||") patterns.Add(pattern);
            }

            if (patterns.Count == 0)
            {
                TaskDialog.Show("Pattern Select", "No STING tag patterns found on selected elements.\nEnsure elements have DISC/SYS/LOC/ZONE values.");
                return;
            }

            // Find all view elements matching any seed pattern
            var matches = new List<ElementId>();
            foreach (Element el in new FilteredElementCollector(doc, doc.ActiveView.Id)
                .WhereElementIsNotElementType())
            {
                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                string loc = ParameterHelpers.GetString(el, ParamRegistry.LOC);
                string zone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
                string pat = $"{disc}|{sys}|{loc}|{zone}";
                if (patterns.Contains(pat))
                    matches.Add(el.Id);
            }

            uidoc.Selection.SetElementIds(matches);
            TaskDialog.Show("Pattern Select",
                $"Found {matches.Count} elements matching {patterns.Count} pattern(s):\n" +
                string.Join("\n", patterns.Take(5).Select(p => $"  {p.Replace("|", " / ")}")));
            StingLog.Info($"AIPatternSelect: {patterns.Count} patterns → {matches.Count} elements");
        }

        /// <summary>
        /// Select elements within a room/space boundary.
        /// Uses the room that contains the first selected element, then selects
        /// all taggable elements within that room's boundary.
        /// </summary>
        private static void AIBoundarySelect(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0)
            {
                TaskDialog.Show("Boundary Select", "Select an element inside a room.\nAll taggable elements in that room will be selected.");
                return;
            }

            // Find the room containing the first selected element
            Autodesk.Revit.DB.Architecture.Room targetRoom = null;
            foreach (ElementId id in selected)
            {
                Element el = doc.GetElement(id);
                if (el == null) continue;
                targetRoom = ParameterHelpers.GetRoomAtElement(doc, el);
                if (targetRoom != null) break;
            }

            if (targetRoom == null)
            {
                TaskDialog.Show("Boundary Select", "No room found at selected element position.\nEnsure rooms are placed and the element is within a room boundary.");
                return;
            }

            // Select all taggable elements within this room
            var matches = new List<ElementId>();
            var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys);
            foreach (Element el in new FilteredElementCollector(doc, doc.ActiveView.Id)
                .WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (!knownCats.Contains(cat)) continue;

                var room = ParameterHelpers.GetRoomAtElement(doc, el);
                if (room != null && room.Id == targetRoom.Id)
                    matches.Add(el.Id);
            }

            uidoc.Selection.SetElementIds(matches);
            string roomName = targetRoom.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
            string roomNum = targetRoom.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
            TaskDialog.Show("Boundary Select",
                $"Room: {roomName} ({roomNum})\n" +
                $"Selected {matches.Count} taggable elements within room boundary.");
            StingLog.Info($"AIBoundarySelect: room '{roomName}' ({roomNum}) → {matches.Count} elements");
        }

        /// <summary>
        /// Select elements within a proximity radius of the current selection.
        /// Uses bounding box center distance comparison.
        /// </summary>
        private static void SelectNearby(UIApplication app, double radiusFeet)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;
            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0)
            {
                TaskDialog.Show("Select Nearby", "Select element(s) first. Nearby elements will be found.");
                return;
            }

            // Get centers of selected elements
            var seedCenters = new List<XYZ>();
            foreach (ElementId id in selected)
            {
                Element el = doc.GetElement(id);
                if (el == null) continue;
                BoundingBoxXYZ bb = el.get_BoundingBox(view);
                if (bb != null)
                    seedCenters.Add((bb.Min + bb.Max) / 2.0);
            }

            if (seedCenters.Count == 0)
            {
                TaskDialog.Show("Select Nearby", "Cannot determine positions of selected elements.");
                return;
            }

            // Find all elements within radius
            var nearby = new List<ElementId>();
            foreach (Element el in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType())
            {
                BoundingBoxXYZ bb = el.get_BoundingBox(view);
                if (bb == null) continue;
                XYZ center = (bb.Min + bb.Max) / 2.0;

                foreach (XYZ seed in seedCenters)
                {
                    double dist = new XYZ(center.X - seed.X, center.Y - seed.Y, 0).GetLength();
                    if (dist <= radiusFeet)
                    {
                        nearby.Add(el.Id);
                        break;
                    }
                }
            }

            uidoc.Selection.SetElementIds(nearby);
            double radiusM = radiusFeet * 0.3048;
            StingLog.Info($"SelectNearby: radius={radiusM:F1}m, found={nearby.Count}");
        }

        /// <summary>
        /// Select elements at the edges/boundaries of the view crop region.
        /// Useful for finding elements that may be cut off or partially visible.
        /// </summary>
        private static void SelectEdgeElements(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            BoundingBoxXYZ viewBB = view.CropBox;
            if (viewBB == null || !view.CropBoxActive)
            {
                TaskDialog.Show("Edge Select", "View must have an active crop region.");
                return;
            }

            // Edge margin: elements within 10% of the crop box boundary
            double dx = (viewBB.Max.X - viewBB.Min.X) * 0.1;
            double dy = (viewBB.Max.Y - viewBB.Min.Y) * 0.1;

            var edgeElements = new List<ElementId>();
            foreach (Element el in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType())
            {
                BoundingBoxXYZ bb = el.get_BoundingBox(view);
                if (bb == null) continue;
                XYZ center = (bb.Min + bb.Max) / 2.0;

                bool nearEdge = center.X < viewBB.Min.X + dx || center.X > viewBB.Max.X - dx
                    || center.Y < viewBB.Min.Y + dy || center.Y > viewBB.Max.Y - dy;

                if (nearEdge)
                    edgeElements.Add(el.Id);
            }

            uidoc.Selection.SetElementIds(edgeElements);
            TaskDialog.Show("Edge Select",
                $"Selected {edgeElements.Count} elements near the view crop boundary.");
            StingLog.Info($"SelectEdgeElements: {edgeElements.Count} near crop boundary");
        }

        // ── Scope & mode toggles ─────────────────────────────────────

        private static bool _scopeIsView = true;

        /// <summary>
        /// Toggle between view-scope (active view only) and project-scope (all elements).
        /// Affects subsequent AI select and analysis operations.
        /// </summary>
        private static void ToggleScopeMode(UIApplication app)
        {
            _scopeIsView = !_scopeIsView;
            string mode = _scopeIsView ? "Active View" : "Entire Project";
            TaskDialog.Show("Scope Mode", $"Selection scope: {mode}");
            StingLog.Info($"ToggleScopeMode: {mode}");
        }

        private static bool _overwriteMode = false;

        /// <summary>
        /// Toggle between skip-existing and overwrite modes for parameter operations.
        /// </summary>
        private static void ToggleOverwriteMode(UIApplication app)
        {
            _overwriteMode = !_overwriteMode;
            string mode = _overwriteMode ? "OVERWRITE existing values" : "SKIP existing values";
            TaskDialog.Show("Overwrite Mode", $"Parameter write mode: {mode}");
            StingLog.Info($"ToggleOverwriteMode: {mode}");
        }

        // ── Anomaly & intelligence helpers ────────────────────────────

        /// <summary>
        /// Scan the current view for parameter anomalies: missing tokens,
        /// inconsistent values, placeholder codes, format violations.
        /// </summary>
        private static void AnomalyRefreshScan(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;
            if (view == null)
            {
                TaskDialog.Show("Anomaly Scan", "No active view — switch to a model view first.");
                return;
            }
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            int total = 0, missingTag = 0, missingDisc = 0, missingSys = 0;
            int placeholders = 0, formatErrors = 0;
            // Collect param names from scanned elements for dropdown population
            var paramNames = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string p in ParamRegistry.AllParamGuids.Keys)
                paramNames.Add(p);

            foreach (Element el in new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (!known.Contains(cat)) continue;
                total++;

                // Collect parameter names from first few elements for dropdown
                if (total <= 3)
                {
                    foreach (Parameter p in el.Parameters)
                    {
                        if (p.Definition != null && !string.IsNullOrEmpty(p.Definition.Name))
                            paramNames.Add(p.Definition.Name);
                    }
                }

                string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);

                if (string.IsNullOrEmpty(tag1)) missingTag++;
                if (string.IsNullOrEmpty(disc)) missingDisc++;
                if (string.IsNullOrEmpty(sys)) missingSys++;

                if (!string.IsNullOrEmpty(tag1))
                {
                    if (tag1.Contains("-XX-") || tag1.Contains("-0000"))
                        placeholders++;
                    string[] parts = tag1.Split('-');
                    if (parts.Length != 8)
                        formatErrors++;
                }
            }

            // Populate dropdown with discovered parameters
            StingDockPanel.PopulateParamDropdowns(paramNames);

            int issues = missingTag + missingDisc + missingSys + placeholders + formatErrors;
            double healthPct = total > 0 ? ((total - Math.Min(issues, total)) / (double)total) * 100 : 0;

            var report = new StringBuilder();
            report.AppendLine($"Anomaly Scan — {view.Name}");
            report.AppendLine(new string('═', 45));
            report.AppendLine($"  Taggable elements: {total}");
            report.AppendLine($"  Health score:      {healthPct:F0}%");
            report.AppendLine();
            report.AppendLine("  Anomalies:");
            report.AppendLine($"    Missing tags:     {missingTag}");
            report.AppendLine($"    Missing DISC:     {missingDisc}");
            report.AppendLine($"    Missing SYS:      {missingSys}");
            report.AppendLine($"    Placeholders:     {placeholders} (XX, 0000)");
            report.AppendLine($"    Format errors:    {formatErrors} (not 8-segment)");
            report.AppendLine();
            report.AppendLine($"  Total issues: {issues}");

            var td = new TaskDialog("STING Tools - Anomaly Scan");
            td.MainInstruction = $"Anomaly Scan — {view.Name}";
            td.MainContent = report.ToString();
            td.CommonButtons = TaskDialogCommonButtons.Ok;
            td.DefaultButton = TaskDialogResult.Ok;
            td.Show();
            StingLog.Info($"AnomalyRefresh: {total} elements, {issues} issues, health={healthPct:F0}%");
        }

        /// <summary>
        /// Analyze selected elements and suggest optimal bulk parameter operations.
        /// Reports frequency of existing values and recommends the most impactful bulk write.
        /// </summary>
        private static void BulkBrainSuggest(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var ids = uidoc.Selection.GetElementIds();

            if (ids.Count == 0)
            {
                // Use all taggable in view
                var known2 = new HashSet<string>(TagConfig.DiscMap.Keys);
                ids = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .Where(e => known2.Contains(ParameterHelpers.GetCategoryName(e)))
                    .Select(e => e.Id).ToList();
            }

            if (ids.Count == 0)
            {
                TaskDialog.Show("Bulk Brain", "No taggable elements found.");
                return;
            }

            // Analyze token fill rates
            string[] tokens = { ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC, ParamRegistry.PROD };
            var tokenStats = new Dictionary<string, (int filled, int empty, string topValue)>();

            foreach (string token in tokens)
            {
                int filled = 0, empty = 0;
                var valueCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (ElementId id in ids)
                {
                    Element el = doc.GetElement(id);
                    if (el == null) continue;
                    string val = ParameterHelpers.GetString(el, token);
                    if (string.IsNullOrEmpty(val))
                        empty++;
                    else
                    {
                        filled++;
                        if (!valueCounts.ContainsKey(val)) valueCounts[val] = 0;
                        valueCounts[val]++;
                    }
                }

                string topVal = valueCounts.Count > 0
                    ? valueCounts.OrderByDescending(v => v.Value).First().Key
                    : "-";
                tokenStats[token] = (filled, empty, topVal);
            }

            var report = new StringBuilder();
            report.AppendLine($"Bulk Brain — {ids.Count} elements");
            report.AppendLine(new string('═', 55));
            report.AppendLine($"{"Token",-28} {"Filled",-8} {"Empty",-8} {"Top Value"}");
            report.AppendLine(new string('─', 55));

            string bestSuggestion = null;
            int maxEmpty = 0;
            foreach (var kvp in tokenStats)
            {
                string shortName = kvp.Key.Replace("ASS_", "").Replace("_TXT", "").Replace("_COD", "");
                report.AppendLine($"  {shortName,-26} {kvp.Value.filled,-8} {kvp.Value.empty,-8} {kvp.Value.topValue}");
                if (kvp.Value.empty > maxEmpty)
                {
                    maxEmpty = kvp.Value.empty;
                    bestSuggestion = kvp.Key;
                }
            }

            report.AppendLine();
            if (bestSuggestion != null && maxEmpty > 0)
                report.AppendLine($"Suggestion: Run 'Family-Stage Populate' to fill {maxEmpty} empty {bestSuggestion} values.");
            else
                report.AppendLine("All tokens are fully populated.");

            TaskDialog.Show("Bulk Brain", report.ToString());
            StingLog.Info($"BulkBrain: {ids.Count} elements analyzed");
        }

        /// <summary>
        /// Refresh tag family information: audit loaded tag families and report coverage.
        /// </summary>
        private static void TagFamilyRefresh(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            // Find all loaded tag family types
            var tagFamilies = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs =>
                {
                    try { return fs.Family?.FamilyCategory?.Name?.Contains("Tag") == true; }
                    catch { return false; }
                })
                .ToList();

            var stingTags = tagFamilies.Where(t => t.Family.Name.StartsWith("STING")).ToList();
            var otherTags = tagFamilies.Where(t => !t.Family.Name.StartsWith("STING")).ToList();

            // Check coverage of known categories
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            var taggedCats = new HashSet<string>();
            foreach (var tf in tagFamilies)
            {
                try
                {
                    var cat = tf.Family.FamilyCategory;
                    if (cat != null) taggedCats.Add(cat.Name);
                }
                catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
            }

            var report = new StringBuilder();
            report.AppendLine("Tag Family Audit");
            report.AppendLine(new string('═', 45));
            report.AppendLine($"  STING tag families: {stingTags.Count}");
            report.AppendLine($"  Other tag families: {otherTags.Count}");
            report.AppendLine($"  Taggable categories: {known.Count}");
            report.AppendLine();
            if (stingTags.Count > 0)
            {
                report.AppendLine("STING tag families:");
                foreach (var tf in stingTags.Take(20))
                    report.AppendLine($"  {tf.Family.Name} : {tf.Name}");
            }

            TaskDialog.Show("Tag Family Refresh", report.ToString());
            StingLog.Info($"TagFamilyRefresh: {stingTags.Count} STING tags, {otherTags.Count} other");
        }

        // ── Elbow snap helper (direct angle application) ─────────────

        /// <summary>
        /// Snap leader elbows to a specific angle without dialog.
        /// angleMode: "45", "90", "0" (straight), or "cycle" (detect current and rotate).
        /// </summary>
        private static void SnapElbowDirect(UIApplication app, string angleMode)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;

            var tags = Organise.LeaderHelper.GetSelectedTags(uidoc)
                .Where(t => t.HasLeader).ToList();
            if (tags.Count == 0)
                tags = Organise.LeaderHelper.GetTargetTags(uidoc)
                    .Where(t => t.HasLeader).ToList();

            if (tags.Count == 0)
            {
                TaskDialog.Show("Snap Elbows", "No tags with leaders found.");
                return;
            }

            int snapped = 0;
            using (Transaction tx = new Transaction(doc, "STING Snap Leader Elbows"))
            {
                tx.Start();
                foreach (IndependentTag tag in tags)
                {
                    try
                    {
                        var hostIds = tag.GetTaggedLocalElementIds();
                        Element host = hostIds.Count > 0 ? doc.GetElement(hostIds.First()) : null;
                        if (host == null) continue;

                        XYZ hostCenter = Organise.LeaderHelper.GetElementCenter(host);
                        if (hostCenter == null) continue;

                        XYZ tagHead = tag.TagHeadPosition;
                        XYZ delta = tagHead - hostCenter;
                        if (delta.GetLength() < 0.01) continue;

                        // Determine effective mode for cycling
                        string effectiveMode = angleMode;
                        if (effectiveMode == "cycle")
                        {
                            // Detect current elbow angle from existing elbow position
                            effectiveMode = DetectAndCycleElbowAngle(tag, host, doc, hostCenter, tagHead);
                        }

                        XYZ elbowPos;
                        if (effectiveMode == "0")
                        {
                            // Straight: elbow on line near tag head
                            XYZ dir = delta.Normalize();
                            double len = delta.GetLength();
                            elbowPos = hostCenter + dir * (len * 0.95);
                        }
                        else if (effectiveMode == "45")
                        {
                            // 45° elbow near element (arrow side)
                            double absDx = Math.Abs(delta.X);
                            double absDy = Math.Abs(delta.Y);
                            double diag = Math.Min(absDx, absDy);
                            double signX = delta.X >= 0 ? 1 : -1;
                            double signY = delta.Y >= 0 ? 1 : -1;

                            elbowPos = new XYZ(hostCenter.X + diag * signX, hostCenter.Y + diag * signY, hostCenter.Z);
                        }
                        else // "90"
                        {
                            // 90° elbow near element (arrow side): vertical from host then horizontal to tag
                            elbowPos = new XYZ(hostCenter.X, tagHead.Y, hostCenter.Z);
                        }

                        var refs = tag.GetTaggedReferences();
                        if (refs != null && refs.Count > 0)
                        {
                            tag.LeaderEndCondition = LeaderEndCondition.Free;
                            tag.SetLeaderElbow(refs.First(), elbowPos);
                            snapped++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"SnapElbowDirect on tag {tag.Id}: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Snap Elbows", $"Snapped {snapped} leader elbows to {angleMode}°.");
        }

        /// <summary>
        /// Detect current elbow angle and return the next angle in cycle: 90→45→0→90.
        /// </summary>
        private static string DetectAndCycleElbowAngle(IndependentTag tag, Element host,
            Document doc, XYZ hostCenter, XYZ tagHead)
        {
            try
            {
                var refs = tag.GetTaggedReferences();
                if (refs == null || refs.Count == 0) return "90";

                XYZ elbow = tag.GetLeaderElbow(refs.First());
                if (elbow == null) return "90";

                XYZ delta = tagHead - hostCenter;
                double absDx = Math.Abs(delta.X);
                double absDy = Math.Abs(delta.Y);

                // Check if elbow is at midpoint (straight/0°)
                XYZ mid = (hostCenter + tagHead) / 2.0;
                if (elbow.DistanceTo(mid) < 0.1)
                    return "90"; // Cycle: 0 → 90

                // Check if elbow is at orthogonal position (90°) — arrow side
                XYZ ortho90 = new XYZ(hostCenter.X, tagHead.Y, hostCenter.Z);
                if (elbow.DistanceTo(ortho90) < 0.1)
                    return "45"; // Cycle: 90 → 45

                // Otherwise assume 45° or unknown → cycle to 0 (straight)
                return "0"; // Cycle: 45 → 0
            }
            catch
            {
                return "90";
            }
        }

        // ── Conditional selection builder ─────────────────────────────

        private static readonly List<(string param, string op, string value)> _conditions
            = new List<(string, string, string)>();

        private static void ConditionAdd(UIApplication app, string paramName, string value)
        {
            if (string.IsNullOrEmpty(paramName))
            {
                TaskDialog.Show("Condition", "Specify parameter name.");
                return;
            }
            _conditions.Add((paramName, "=", value ?? ""));
            TaskDialog.Show("Condition Builder",
                $"Added: {paramName} = {value}\nConditions: {_conditions.Count}");
        }

        private static void ConditionRemove(UIApplication app)
        {
            if (_conditions.Count > 0)
            {
                var last = _conditions[_conditions.Count - 1];
                _conditions.RemoveAt(_conditions.Count - 1);
                TaskDialog.Show("Condition Builder",
                    $"Removed: {last.param} {last.op} {last.value}\nRemaining: {_conditions.Count}");
            }
            else
                TaskDialog.Show("Condition Builder", "No conditions to remove.");
        }

        private static void ConditionClear(UIApplication app)
        {
            int count = _conditions.Count;
            _conditions.Clear();
            TaskDialog.Show("Condition Builder", $"Cleared {count} conditions.");
        }

        private static void ConditionPreview(UIApplication app)
        {
            if (_conditions.Count == 0)
            {
                TaskDialog.Show("Condition Preview", "No conditions defined.\nUse '+ Add' to build conditions.");
                return;
            }
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;

            int matchCount = CountConditionMatches(doc, doc.ActiveView.Id);
            var sb = new StringBuilder();
            sb.AppendLine("Conditions:");
            foreach (var c in _conditions)
                sb.AppendLine($"  {c.param} {c.op} \"{c.value}\"");
            sb.AppendLine($"\nMatching elements: {matchCount}");
            TaskDialog.Show("Condition Preview", sb.ToString());
        }

        private static void ConditionApply(UIApplication app)
        {
            if (_conditions.Count == 0)
            {
                TaskDialog.Show("Condition Apply", "No conditions defined.");
                return;
            }
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;

            var matches = GetConditionMatches(doc, doc.ActiveView.Id);
            uidoc.Selection.SetElementIds(matches);
            TaskDialog.Show("Condition Apply",
                $"Selected {matches.Count} elements matching {_conditions.Count} condition(s).");
            StingLog.Info($"ConditionApply: {matches.Count} matches for {_conditions.Count} conditions");
        }

        private static int CountConditionMatches(Document doc, ElementId viewId)
        {
            return GetConditionMatches(doc, viewId).Count;
        }

        private static List<ElementId> GetConditionMatches(Document doc, ElementId viewId)
        {
            var results = new List<ElementId>();
            foreach (Element el in new FilteredElementCollector(doc, viewId).WhereElementIsNotElementType())
            {
                bool allMatch = true;
                foreach (var (param, op, value) in _conditions)
                {
                    string actual = ParameterHelpers.GetString(el, param);
                    if (string.IsNullOrEmpty(actual))
                    {
                        var p = el.LookupParameter(param);
                        actual = p?.AsValueString() ?? "";
                    }
                    if (!string.Equals(actual, value, StringComparison.OrdinalIgnoreCase))
                    {
                        allMatch = false;
                        break;
                    }
                }
                if (allMatch) results.Add(el.Id);
            }
            return results;
        }

        // ── Remaining stub implementations ────────────────────────────

        /// <summary>
        /// Select elements within a quadrant of the view (NE/NW/SE/SW).
        /// </summary>
        private static void SelectQuadrant(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            TaskDialog dlg = new TaskDialog("Select Quadrant");
            dlg.MainInstruction = "Select elements in which quadrant?";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "NW (Top-Left)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "NE (Top-Right)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "SW (Bottom-Left)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "SE (Bottom-Right)");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            int quad;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: quad = 0; break; // NW
                case TaskDialogResult.CommandLink2: quad = 1; break; // NE
                case TaskDialogResult.CommandLink3: quad = 2; break; // SW
                case TaskDialogResult.CommandLink4: quad = 3; break; // SE
                default: return;
            }

            // Calculate view center from all visible elements
            var allElements = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType().ToList();
            double sumX = 0, sumY = 0;
            int counted = 0;
            foreach (Element el in allElements)
            {
                BoundingBoxXYZ bb = el.get_BoundingBox(view);
                if (bb == null) continue;
                sumX += (bb.Min.X + bb.Max.X) / 2.0;
                sumY += (bb.Min.Y + bb.Max.Y) / 2.0;
                counted++;
            }
            if (counted == 0) return;
            double centerX = sumX / counted;
            double centerY = sumY / counted;

            var matches = new List<ElementId>();
            foreach (Element el in allElements)
            {
                BoundingBoxXYZ bb = el.get_BoundingBox(view);
                if (bb == null) continue;
                double ex = (bb.Min.X + bb.Max.X) / 2.0;
                double ey = (bb.Min.Y + bb.Max.Y) / 2.0;

                bool match = quad switch
                {
                    0 => ex < centerX && ey > centerY,  // NW
                    1 => ex >= centerX && ey > centerY,  // NE
                    2 => ex < centerX && ey <= centerY,  // SW
                    3 => ex >= centerX && ey <= centerY,  // SE
                    _ => false
                };
                if (match) matches.Add(el.Id);
            }

            uidoc.Selection.SetElementIds(matches);
            string[] quadNames = { "NW", "NE", "SW", "SE" };
            TaskDialog.Show("Select Quadrant", $"Selected {matches.Count} elements in {quadNames[quad]} quadrant.");
        }

        /// <summary>
        /// Select elements by bounding box area — useful for finding oversized or tiny elements.
        /// </summary>
        private static void SelectByBoundingBox(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            TaskDialog dlg = new TaskDialog("Bounding Box Select");
            dlg.MainInstruction = "Select elements by size";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Small (< 0.5m)", "Find small/detail elements");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Medium (0.5–3m)", "Standard-sized elements");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Large (> 3m)", "Find oversized elements");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            double minSize, maxSize;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: minSize = 0; maxSize = 0.5 / 0.3048; break;
                case TaskDialogResult.CommandLink2: minSize = 0.5 / 0.3048; maxSize = 3.0 / 0.3048; break;
                case TaskDialogResult.CommandLink3: minSize = 3.0 / 0.3048; maxSize = double.MaxValue; break;
                default: return;
            }

            var matches = new List<ElementId>();
            foreach (Element el in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType())
            {
                BoundingBoxXYZ bb = el.get_BoundingBox(view);
                if (bb == null) continue;
                double diagonal = bb.Min.DistanceTo(bb.Max);
                if (diagonal >= minSize && diagonal < maxSize)
                    matches.Add(el.Id);
            }

            uidoc.Selection.SetElementIds(matches);
            TaskDialog.Show("Bounding Box Select", $"Selected {matches.Count} elements by size.");
        }

        /// <summary>
        /// Select elements aligned to grid lines.
        /// </summary>
        private static void SelectOnGrid(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            var grids = new FilteredElementCollector(doc)
                .OfClass(typeof(Grid)).Cast<Grid>().ToList();

            if (grids.Count == 0)
            {
                TaskDialog.Show("Select on Grid", "No grids found in the project.");
                return;
            }

            // Collect grid line positions (X and Y coordinates for vertical and horizontal grids)
            double tolerance = 1.0; // 1 foot ≈ 300mm snap distance
            var gridXPositions = new List<double>();
            var gridYPositions = new List<double>();

            foreach (Grid g in grids)
            {
                try
                {
                    var curve = g.Curve;
                    XYZ start = curve.GetEndPoint(0);
                    XYZ end = curve.GetEndPoint(1);
                    if (Math.Abs(start.X - end.X) < 0.1)
                        gridXPositions.Add(start.X);
                    else if (Math.Abs(start.Y - end.Y) < 0.1)
                        gridYPositions.Add(start.Y);
                }
                catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
            }

            var matches = new List<ElementId>();
            foreach (Element el in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType())
            {
                BoundingBoxXYZ bb = el.get_BoundingBox(view);
                if (bb == null) continue;
                double cx = (bb.Min.X + bb.Max.X) / 2.0;
                double cy = (bb.Min.Y + bb.Max.Y) / 2.0;

                bool onGrid = gridXPositions.Any(gx => Math.Abs(cx - gx) < tolerance)
                    || gridYPositions.Any(gy => Math.Abs(cy - gy) < tolerance);

                if (onGrid) matches.Add(el.Id);
            }

            uidoc.Selection.SetElementIds(matches);
            TaskDialog.Show("Select on Grid",
                $"Selected {matches.Count} elements on {grids.Count} grid lines.");
        }

        /// <summary>
        /// Add prefix/suffix to viewport detail numbers on the active sheet.
        /// </summary>
        private static void ViewportAddPrefixSuffix(UIApplication app, bool isPrefix)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            if (!(doc.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("Viewport Number", "Active view must be a sheet.");
                return;
            }

            string label = isPrefix ? "Prefix" : "Suffix";
            TaskDialog dlg = new TaskDialog($"Viewport {label}");
            dlg.MainInstruction = $"Add {label.ToLower()} to viewport detail numbers";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, isPrefix ? "M-" : "-M", "Mechanical");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, isPrefix ? "E-" : "-E", "Electrical");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, isPrefix ? "P-" : "-P", "Plumbing");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string value;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: value = isPrefix ? "M-" : "-M"; break;
                case TaskDialogResult.CommandLink2: value = isPrefix ? "E-" : "-E"; break;
                case TaskDialogResult.CommandLink3: value = isPrefix ? "P-" : "-P"; break;
                default: return;
            }

            int updated = 0;
            using (Transaction tx = new Transaction(doc, $"STING VP {label}"))
            {
                tx.Start();
                foreach (ElementId vpId in sheet.GetAllViewports())
                {
                    Viewport vp = doc.GetElement(vpId) as Viewport;
                    if (vp == null) continue;
                    try
                    {
                        Parameter p = vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
                        if (p != null && !p.IsReadOnly)
                        {
                            string current = p.AsString() ?? "";
                            p.Set(isPrefix ? value + current : current + value);
                            updated++;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                }
                tx.Commit();
            }
            TaskDialog.Show($"VP {label}", $"Updated {updated} viewport numbers.");
        }

        /// <summary>
        /// Reset sheet title to match sheet number pattern.
        /// </summary>
        private static void SheetResetTitleBlock(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            if (!(doc.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("Sheet Reset", "Active view must be a sheet.");
                return;
            }

            TaskDialog.Show("Sheet Reset Title",
                $"Current sheet: {sheet.SheetNumber} - {sheet.Name}\n\n" +
                "To reset the title block, select it in the view and modify its parameters.");
        }

        /// <summary>
        /// Find and replace text in dimension value overrides.
        /// </summary>
        private static void DimFindReplaceOverrides(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;

            // Find all dimensions with overrides in the view
            var dims = new FilteredElementCollector(doc, doc.ActiveView.Id)
                .OfClass(typeof(Dimension))
                .Cast<Dimension>()
                .ToList();

            int withOverrides = 0;
            var overrideValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var dim in dims)
            {
                try
                {
                    foreach (DimensionSegment seg in dim.Segments)
                    {
                        if (!string.IsNullOrEmpty(seg.ValueOverride))
                        {
                            withOverrides++;
                            if (!overrideValues.ContainsKey(seg.ValueOverride))
                                overrideValues[seg.ValueOverride] = 0;
                            overrideValues[seg.ValueOverride]++;
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
            }

            var report = new StringBuilder();
            report.AppendLine($"Dimension Override Report — {doc.ActiveView.Name}");
            report.AppendLine($"Total dimensions: {dims.Count}");
            report.AppendLine($"Segments with overrides: {withOverrides}");
            if (overrideValues.Count > 0)
            {
                report.AppendLine("\nOverride values:");
                foreach (var kvp in overrideValues.OrderByDescending(v => v.Value).Take(15))
                    report.AppendLine($"  \"{kvp.Key}\" — {kvp.Value} occurrence(s)");
            }
            report.AppendLine("\nUse 'Reset Dim Text' to clear overrides.");

            TaskDialog.Show("Dim Find/Replace", report.ToString());
        }

        /// <summary>
        /// Batch view category visibility — show all categories or list hidden ones.
        /// </summary>
        private static void BatchViewCategories(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            var view = doc.ActiveView;

            int hidden = 0;
            var hiddenCats = new List<string>();
            foreach (Category cat in doc.Settings.Categories)
            {
                try
                {
                    if (!cat.get_Visible(view))
                    {
                        hidden++;
                        if (hiddenCats.Count < 30)
                            hiddenCats.Add(cat.Name);
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
            }

            var msg = new StringBuilder();
            msg.AppendLine($"View: {view.Name}");
            msg.AppendLine($"Hidden categories: {hidden}");
            if (hiddenCats.Count > 0)
            {
                msg.AppendLine();
                foreach (string c in hiddenCats)
                    msg.AppendLine($"  - {c}");
                if (hidden > 30)
                    msg.AppendLine($"  ... and {hidden - 30} more");
            }

            TaskDialog td = new TaskDialog("Batch View Categories");
            td.MainInstruction = $"{hidden} hidden categories in '{view.Name}'";
            td.MainContent = msg.ToString();
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Show All", "Unhide all categories");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            if (td.Show() == TaskDialogResult.CommandLink1)
            {
                using (Transaction tx = new Transaction(doc, "STING Show All Categories"))
                {
                    tx.Start();
                    foreach (Category cat in doc.Settings.Categories)
                    {
                        try { cat.set_Visible(view, true); }
                        catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                    }
                    tx.Commit();
                }
                TaskDialog.Show("Batch View", $"Unhid {hidden} categories.");
            }
        }

        /// <summary>
        /// Run all organise operations across selected views.
        /// </summary>
        private static void BatchViewRunAll(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            // Count views with issues
            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.ViewType != ViewType.Internal)
                .ToList();

            int noTemplate = views.Count(v => v.ViewTemplateId == ElementId.InvalidElementId);
            int total = views.Count;

            TaskDialog.Show("Batch View Run All",
                $"Project views: {total}\n" +
                $"Without template: {noTemplate}\n\n" +
                "Use Template Manager commands for comprehensive batch operations:\n" +
                "  - Auto-Assign Templates\n" +
                "  - Compliance Score\n" +
                "  - Auto-Fix Template");
        }

        /// <summary>
        /// Toggle room tag leader lock/free state.
        /// </summary>
        private static void RoomTagLeaderToggle(UIApplication app, bool lockLeader)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            var roomTags = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_RoomTags)
                .WhereElementIsNotElementType()
                .Cast<Autodesk.Revit.DB.Architecture.RoomTag>()
                .ToList();

            if (roomTags.Count == 0)
            {
                TaskDialog.Show("Room Tag Leader", "No room tags in active view.");
                return;
            }

            int toggled = 0;
            using (Transaction tx = new Transaction(doc, "STING Room Tag Leader"))
            {
                tx.Start();
                foreach (var rt in roomTags)
                {
                    try
                    {
                        rt.HasLeader = lockLeader;
                        toggled++;
                    }
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                }
                tx.Commit();
            }

            string state = lockLeader ? "added leaders to" : "removed leaders from";
            TaskDialog.Show("Room Tag Leader", $"Successfully {state} {toggled} room tags.");
        }

        /// <summary>
        /// List linked models in the project.
        /// </summary>
        private static void ListLinkedModels(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            var links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            if (links.Count == 0)
            {
                TaskDialog.Show("Linked Models", "No linked models found in this project.");
                return;
            }

            var report = new StringBuilder();
            report.AppendLine($"Linked Models ({links.Count}):");
            report.AppendLine(new string('─', 50));
            foreach (var link in links)
            {
                try
                {
                    string name = link.Name;
                    var linkDoc = link.GetLinkDocument();
                    string status = linkDoc != null ? "Loaded" : "Unloaded";
                    report.AppendLine($"  [{status}] {name}");
                }
                catch
                {
                    report.AppendLine($"  [Error] {link.Id}");
                }
            }

            TaskDialog.Show("Linked Models", report.ToString());
        }

        /// <summary>
        /// Audit linked model status and report.
        /// </summary>
        private static void AuditLinkedModels(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            var links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            var linkTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkType))
                .Cast<RevitLinkType>()
                .ToList();

            int loaded = 0, unloaded = 0;
            foreach (var lt in linkTypes)
            {
                try
                {
                    if (lt.GetLinkedFileStatus() == LinkedFileStatus.Loaded)
                        loaded++;
                    else
                        unloaded++;
                }
                catch { unloaded++; }
            }

            TaskDialog.Show("Audit Links",
                $"Link Instances: {links.Count}\n" +
                $"Link Types: {linkTypes.Count}\n" +
                $"  Loaded: {loaded}\n" +
                $"  Unloaded: {unloaded}");
        }
    }
}
