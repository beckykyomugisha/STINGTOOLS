using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// External event handler that dispatches dockable panel button clicks
    /// to the appropriate IExternalCommand classes. This ensures all Revit API
    /// calls happen on the correct thread (main Revit API context).
    ///
    /// Each button Tag string maps to a command class or inline operation.
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
                    // ── SELECT: Category selectors ──
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

                    // ── SELECT: State selectors ──
                    case "SelectUntagged": RunCommand<Select.SelectUntaggedCommand>(app); break;
                    case "SelectTagged": RunCommand<Select.SelectTaggedCommand>(app); break;
                    case "SelectEmptyMark": RunCommand<Select.SelectEmptyMarkCommand>(app); break;
                    case "SelectPinned": RunCommand<Select.SelectPinnedCommand>(app); break;
                    case "SelectUnpinned": RunCommand<Select.SelectUnpinnedCommand>(app); break;

                    // ── SELECT: Spatial selectors ──
                    case "SelectByLevel": RunCommand<Select.SelectByLevelCommand>(app); break;
                    case "SelectByRoom": RunCommand<Select.SelectByRoomCommand>(app); break;

                    // ── SELECT: Bulk param write ──
                    case "BulkParamWrite": RunCommand<Select.BulkParamWriteCommand>(app); break;

                    // ── SELECT: View isolate/hide (inline) ──
                    case "ViewIsolate": ViewIsolateSelected(app); break;
                    case "ViewHide": ViewHideSelected(app); break;
                    case "ViewReveal": ViewRevealHidden(app); break;
                    case "ViewReset": ViewResetIsolate(app); break;

                    // ── SELECT: Selection ops (inline) ──
                    case "SelectAll": SelectAllVisible(app); break;
                    case "SelectClear": ClearSelection(app); break;
                    case "Deselect": ClearSelection(app); break;
                    case "InvertSelection": InvertSelection(app); break;
                    case "DeleteSelected": DeleteSelected(app); break;
                    case "SelectTags": SelectAnnotationTags(app); break;
                    case "SelectHostElements": SelectHostElements(app); break;

                    // ── SELECT: Selection memory (inline) ──
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

                    // ── SELECT: Quick param filter (inline) ──
                    case "QuickMark": QuickParamFilter(app, "Mark"); break;
                    case "QuickType": QuickParamFilter(app, "Type Name"); break;
                    case "QuickFamily": QuickParamFilter(app, "Family"); break;
                    case "QuickSystem": QuickParamFilter(app, "ASS_SYSTEM_TYPE_TXT"); break;

                    // ── SELECT: Bulk write from panel (inline) ──
                    case "BulkWrite": BulkParamWriteInline(app, _param1, _param2, false); break;
                    case "BulkClear": BulkParamWriteInline(app, _param1, "", true); break;
                    case "BulkPreview": BulkParamPreview(app, _param1, _param2); break;

                    // ── ORGANISE: Tag operations ──
                    case "TagSelected": RunCommand<Organise.TagSelectedCommand>(app); break;
                    case "AutoTag": RunCommand<Tags.AutoTagCommand>(app); break;
                    case "BatchTag": RunCommand<Tags.BatchTagCommand>(app); break;
                    case "DeleteTags": RunCommand<Organise.DeleteTagsCommand>(app); break;
                    case "RenumberTags": RunCommand<Organise.RenumberTagsCommand>(app); break;
                    case "CopyTags": RunCommand<Organise.CopyTagsCommand>(app); break;
                    case "SwapTags": RunCommand<Organise.SwapTagsCommand>(app); break;
                    case "ReTag": RunCommand<Organise.ReTagCommand>(app); break;
                    case "FixDuplicates": RunCommand<Organise.FixDuplicateTagsCommand>(app); break;
                    case "FindDuplicates": RunCommand<Organise.FindDuplicateTagsCommand>(app); break;
                    case "TagNewOnly": RunCommand<Tags.TagNewOnlyCommand>(app); break;

                    // ── ORGANISE: Orientation & text alignment ──
                    case "ToggleTagOrientation": RunCommand<Organise.ToggleTagOrientationCommand>(app); break;
                    case "FlipTags":
                    case "FlipTagsH":
                    case "FlipTagsV": RunCommand<Organise.FlipTagsCommand>(app); break;
                    case "AlignTextLeft":
                    case "AlignTextCenter":
                    case "AlignTextRight": RunCommand<Organise.AlignTagTextCommand>(app); break;

                    // ── ORGANISE: Align & distribute ──
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

                    // ── ORGANISE: Leaders ──
                    case "AddLeaders": RunCommand<Organise.AddLeadersCommand>(app); break;
                    case "RemoveLeaders": RunCommand<Organise.RemoveLeadersCommand>(app); break;
                    case "ToggleLeaders": RunCommand<Organise.ToggleLeadersCommand>(app); break;
                    case "SnapElbow90":
                    case "SnapElbow45":
                    case "SnapElbowStraight": RunCommand<Organise.SnapLeaderElbowCommand>(app); break;
                    case "PinTags": RunCommand<Organise.PinTagsCommand>(app); break;
                    case "AttachLeader": RunCommand<Organise.AttachLeaderCommand>(app); break;
                    case "ResetTagPositions": RunCommand<Organise.ResetTagPositionsCommand>(app); break;
                    case "SelectTagsWithLeaders": RunCommand<Organise.SelectTagsWithLeadersCommand>(app); break;
                    case "LeaderLength025":
                    case "LeaderLength05":
                    case "LeaderLength1":
                    case "LeaderEqualSpacing":
                    case "LeaderEqualise": RunCommand<Organise.SnapLeaderElbowCommand>(app); break;

                    // ── ORGANISE: Analysis ──
                    case "TagStats": RunCommand<Organise.TagStatsCommand>(app); break;
                    case "AuditTagsCSV": RunCommand<Organise.AuditTagsCSVCommand>(app); break;
                    case "SelectByDiscipline": RunCommand<Organise.SelectByDisciplineCommand>(app); break;
                    case "TagRegisterExport": RunCommand<Organise.TagRegisterExportCommand>(app); break;

                    // ── CREATE: Setup ──
                    case "LoadSharedParams": RunCommand<Tags.LoadSharedParamsCommand>(app); break;
                    case "ConfigEditor": RunCommand<Tags.ConfigEditorCommand>(app); break;
                    case "TagConfig": RunCommand<Tags.TagConfigCommand>(app); break;

                    // ── CREATE: Populate tokens ──
                    case "FullAutoPopulate": RunCommand<Temp.FullAutoPopulateCommand>(app); break;
                    case "AutoPopulate": RunCommand<Temp.AutoPopulateCommand>(app); break;
                    case "FamilyStagePopulate": RunCommand<Tags.FamilyStagePopulateCommand>(app); break;
                    case "AssignNumbers": RunCommand<Tags.AssignNumbersCommand>(app); break;
                    case "BuildTags": RunCommand<Tags.BuildTagsCommand>(app); break;
                    case "TagAndCombine": RunCommand<Tags.TagAndCombineCommand>(app); break;
                    case "CombineParameters": RunCommand<Tags.CombineParametersCommand>(app); break;

                    // ── CREATE: Manual tokens ──
                    case "SetDisc": RunCommand<Tags.SetDiscCommand>(app); break;
                    case "SetLoc": RunCommand<Tags.SetLocCommand>(app); break;
                    case "SetZone": RunCommand<Tags.SetZoneCommand>(app); break;
                    case "SetStatus": RunCommand<Tags.SetStatusCommand>(app); break;
                    case "SetSys": WriteTokenToSelected(app, "ASS_SYSTEM_TYPE_TXT", "System Code (SYS)"); break;
                    case "SetFunc": WriteTokenToSelected(app, "ASS_FUNC_TXT", "Function Code (FUNC)"); break;
                    case "SetProd": WriteTokenToSelected(app, "ASS_PRODCT_COD_TXT", "Product Code (PROD)"); break;
                    case "SetLvl": WriteTokenToSelected(app, "ASS_LVL_COD_TXT", "Level Code (LVL)"); break;
                    case "SetOrig": WriteTokenToSelected(app, "ASS_ORIGIN_TXT", "Origin Code (ORIG)"); break;
                    case "SetProj": WriteTokenToSelected(app, "ASS_PROJECT_TXT", "Project Code (PROJ)"); break;
                    case "SetRev": WriteTokenToSelected(app, "ASS_REV_TXT", "Revision Code (REV)"); break;
                    case "SetVol": WriteTokenToSelected(app, "ASS_VOL_TXT", "Volume Code (VOL)"); break;

                    // ── CREATE: QA ──
                    case "ValidateTags": RunCommand<Tags.ValidateTagsCommand>(app); break;
                    case "HighlightInvalid": RunCommand<Organise.HighlightInvalidCommand>(app); break;
                    case "ClearOverrides": RunCommand<Organise.ClearOverridesCommand>(app); break;
                    case "CompletenessDashboard": RunCommand<Tags.CompletenessDashboardCommand>(app); break;
                    case "PreTagAudit": RunCommand<Tags.PreTagAuditCommand>(app); break;

                    // ── VIEW: Docs ──
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

                    // ── VIEW: Templates ──
                    case "MasterSetup": RunCommand<Temp.MasterSetupCommand>(app); break;
                    case "CreateBLEMaterials": RunCommand<Temp.CreateBLEMaterialsCommand>(app); break;
                    case "CreateMEPMaterials": RunCommand<Temp.CreateMEPMaterialsCommand>(app); break;
                    case "CreateWalls": RunCommand<Temp.CreateWallsCommand>(app); break;
                    case "CreateFloors": RunCommand<Temp.CreateFloorsCommand>(app); break;
                    case "CreateCeilings": RunCommand<Temp.CreateCeilingsCommand>(app); break;
                    case "CreateRoofs": RunCommand<Temp.CreateRoofsCommand>(app); break;
                    case "CreateDucts": RunCommand<Temp.CreateDuctsCommand>(app); break;
                    case "CreatePipes": RunCommand<Temp.CreatePipesCommand>(app); break;
                    case "CreateCableTrays": RunCommand<Temp.CreateCableTraysCommand>(app); break;
                    case "CreateConduits": RunCommand<Temp.CreateConduitsCommand>(app); break;
                    case "BatchSchedules": RunCommand<Temp.BatchSchedulesCommand>(app); break;
                    case "FormulaEvaluator": RunCommand<Temp.FormulaEvaluatorCommand>(app); break;
                    case "CreateFilters": RunCommand<Temp.CreateFiltersCommand>(app); break;
                    case "CreateWorksets": RunCommand<Temp.CreateWorksetsCommand>(app); break;
                    case "ViewTemplates": RunCommand<Temp.ViewTemplatesCommand>(app); break;
                    case "CreateLinePatterns": RunCommand<Temp.CreateLinePatternsCommand>(app); break;
                    case "CreatePhases": RunCommand<Temp.CreatePhasesCommand>(app); break;
                    case "CheckData": RunCommand<Temp.CheckDataCommand>(app); break;
                    case "ExportCSV": RunCommand<Temp.ExportCSVCommand>(app); break;
                    case "CreateParameters": RunCommand<Temp.CreateParametersCommand>(app); break;
                    case "MaterialSchedules": RunCommand<Temp.CreateMaterialSchedulesCommand>(app); break;
                    case "ApplyFilters": RunCommand<Temp.ApplyFiltersToViewsCommand>(app); break;

                    // ── VIEW: Template Manager ──
                    case "AutoAssignTemplates": RunCommand<Temp.AutoAssignTemplatesCommand>(app); break;
                    case "TemplateAudit": RunCommand<Temp.TemplateAuditCommand>(app); break;
                    case "TemplateDiff": RunCommand<Temp.TemplateDiffCommand>(app); break;
                    case "TemplateComplianceScore": RunCommand<Temp.TemplateComplianceScoreCommand>(app); break;
                    case "AutoFixTemplate": RunCommand<Temp.AutoFixTemplateCommand>(app); break;
                    case "SyncTemplateOverrides": RunCommand<Temp.SyncTemplateOverridesCommand>(app); break;
                    case "CreateFillPatterns": RunCommand<Temp.CreateFillPatternsCommand>(app); break;
                    case "CreateLineStyles": RunCommand<Temp.CreateLineStylesCommand>(app); break;
                    case "CreateObjectStyles": RunCommand<Temp.CreateObjectStylesCommand>(app); break;
                    case "CreateTextStyles": RunCommand<Temp.CreateTextStylesCommand>(app); break;
                    case "CreateDimensionStyles": RunCommand<Temp.CreateDimensionStylesCommand>(app); break;
                    case "CreateVGOverrides": RunCommand<Temp.CreateVGOverridesCommand>(app); break;
                    case "BatchAddFamilyParams": RunCommand<Temp.BatchAddFamilyParamsCommand>(app); break;
                    case "CreateTemplateSchedules": RunCommand<Temp.CreateTemplateSchedulesCommand>(app); break;
                    case "TemplateSetupWizard": RunCommand<Temp.TemplateSetupWizardCommand>(app); break;
                    case "CloneTemplate": RunCommand<Temp.CloneTemplateCommand>(app); break;
                    case "BatchVGReset": RunCommand<Temp.BatchVGResetCommand>(app); break;

                    // ── SELECT: Refresh param list (inline) ──
                    case "RefreshParamList": RefreshParameterList(app); break;

                    // ── VIEW: Graphic overrides (inline) ──
                    case "HalftoneOn": SetHalftone(app, true); break;
                    case "HalftoneOff": SetHalftone(app, false); break;
                    case "PermHide": PermanentHide(app); break;
                    case "PermUnhide": PermanentUnhide(app); break;
                    case "UnhideCategory": UnhideCategory(app); break;

                    // ── SELECT: Project filters (inline) ──
                    case "FilterWorkset": QuickParamFilter(app, "Workset"); break;
                    case "FilterPhase": QuickParamFilter(app, "Phase Created"); break;
                    case "FilterDesignOption": QuickParamFilter(app, "Design Option"); break;
                    case "FilterGroup": QuickParamFilter(app, "Model Group"); break;
                    case "FilterAssembly": QuickParamFilter(app, "Assembly Name"); break;
                    case "FilterConnected": SelectConnectedElements(app); break;

                    // ── CREATE: Scope / toggles (inline) ──
                    case "ScopeView": /* View scope selection handled by UI radio buttons */ break;
                    case "ToggleOverwrite": /* Overwrite toggle handled by UI checkbox */ break;

                    // ── VIEW: Colouriser (inline) ──
                    case "ColorApply": ColorByParameter(app, _param1, _param2); break;
                    case "ColorApplyHex": ColorByHex(app, _param1); break;
                    case "ColorApplyTransparency": SetTransparencyOverride(app, _param1); break;

                    // ── Unsupported / placeholder ──
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

        /// <summary>
        /// Invokes an IExternalCommand from the dockable panel context.
        /// Since ExternalCommandData is created internally by Revit (no public constructor),
        /// we use FormatterServices.GetUninitializedObject + reflection to set the
        /// UIApplication field. This is the standard approach used by Revit test
        /// frameworks and production addins that invoke commands from event handlers.
        /// </summary>
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
                    "Please use the ribbon button instead.");
            }
        }

        /// <summary>
        /// Creates an ExternalCommandData instance via reflection.
        /// ExternalCommandData has no public constructor (Revit creates it internally),
        /// so we use FormatterServices.GetUninitializedObject to bypass the constructor,
        /// then find and set the UIApplication backing field via reflection.
        /// </summary>
        private static ExternalCommandData CreateCommandData(UIApplication app)
        {
            try
            {
                // Create instance without calling any constructor
                var data = (ExternalCommandData)FormatterServices
                    .GetUninitializedObject(typeof(ExternalCommandData));

                // Find the UIApplication backing field and set it
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

                // Fallback: try property with auto-generated backing field name
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

        // ── Inline operations (no IExternalCommand class needed) ──────

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
            uidoc.ActiveView.EnableRevealHiddenMode();
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
                    if (selected.Contains(tag.TaggedLocalElementId))
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
                    try { hostIds.Add(tag.TaggedLocalElementId); }
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

        private static void RefreshParameterList(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;

            var paramNames = new SortedSet<string>();

            // Add common STING parameters
            string[] stingParams = {
                "ASS_TAG_1_TXT", "ASS_TAG_2_TXT", "ASS_TAG_3_TXT",
                "ASS_TAG_4_TXT", "ASS_TAG_5_TXT", "ASS_TAG_6_TXT",
                "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT",
                "ASS_LVL_COD_TXT", "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT",
                "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT", "ASS_STATUS_TXT",
                "Mark", "Comments"
            };
            foreach (var p in stingParams) paramNames.Add(p);

            // Add text parameters from selected elements
            var ids = uidoc.Selection.GetElementIds();
            foreach (ElementId id in ids)
            {
                Element el = uidoc.Document.GetElement(id);
                if (el == null) continue;
                foreach (Parameter p in el.Parameters)
                {
                    if (p.Definition != null && p.StorageType == StorageType.String)
                        paramNames.Add(p.Definition.Name);
                }
                break; // Only need first element's params
            }

            // Update the panel combo box (same thread in Revit)
            var panel = StingDockPanel.Instance;
            if (panel != null)
            {
                panel.PopulateParamList(paramNames);
                panel.UpdateBulkStatus($"Loaded {paramNames.Count} parameters");
            }
            StingLog.Info($"Refreshed parameter list: {paramNames.Count} params");
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
            // Unhide all categories
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

            // Build a TaskDialog with common values for each token type
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
                "ASS_SYSTEM_TYPE_TXT" => new[] { "HVAC", "DCW", "SAN" },
                "ASS_FUNC_TXT" => new[] { "SUP", "HTG", "PWR" },
                "ASS_PRODCT_COD_TXT" => new[] { "AHU", "DB", "DR" },
                "ASS_LVL_COD_TXT" => new[] { "GF", "L01", "B1" },
                "ASS_ORIGIN_TXT" => new[] { "NEW", "EXISTING", "DEMOLISHED" },
                "ASS_PROJECT_TXT" => new[] { "PRJ001", "PRJ002", "PRJ003" },
                "ASS_REV_TXT" => new[] { "P01", "P02", "C01" },
                "ASS_VOL_TXT" => new[] { "V01", "V02", "V03" },
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

                // Get connected elements via connectors (MEP)
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

            // Group by parameter value
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

            // Generate colors from palette
            Color[] palette = GetColorPalette(paletteName, groups.Count);

            // Find solid fill pattern
            FillPatternElement solidFill = null;
            solidFill = ParameterHelpers.GetSolidFillPattern(uidoc.Document);

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

            FillPatternElement solidFill = ParameterHelpers.GetSolidFillPattern(uidoc.Document);

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

        private static Color[] GetColorPalette(string name, int count)
        {
            // Built-in palettes
            return (name?.ToLower()) switch
            {
                "rag" => new[] {
                    new Color(244, 67, 54),   // Red
                    new Color(255, 152, 0),   // Amber
                    new Color(76, 175, 80)    // Green
                },
                "monochrome" => new[] {
                    new Color(0, 0, 0),
                    new Color(85, 85, 85),
                    new Color(170, 170, 170),
                    new Color(212, 212, 212),
                    new Color(255, 255, 255)
                },
                "discipline" => new[] {
                    new Color(33, 150, 243),   // M = Blue
                    new Color(255, 235, 59),   // E = Yellow
                    new Color(76, 175, 80),    // P = Green
                    new Color(158, 158, 158),  // A = Grey
                    new Color(244, 67, 54),    // S = Red
                    new Color(255, 152, 0),    // FP = Orange
                    new Color(156, 39, 176),   // LV = Purple
                    new Color(121, 85, 72)     // G = Brown
                },
                _ => new[] {
                    // Material Design 500 palette (default)
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
