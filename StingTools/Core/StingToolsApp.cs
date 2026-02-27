using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using StingTools.UI;

namespace StingTools.Core
{
    /// <summary>
    /// Main Revit external application. Creates a single "STING Tools" ribbon
    /// tab with five panels: Select, Docs, Tags, Organise, Temp — porting the
    /// three PyRevit extensions (StingDocs, STINGTags, STINGTemp) into one
    /// compiled plugin with full feature coverage.
    /// </summary>
    public class StingToolsApp : IExternalApplication
    {
        public static string AssemblyPath { get; private set; }
        public static string DataPath { get; private set; }

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                AssemblyPath = Assembly.GetExecutingAssembly().Location;
                DataPath = Path.Combine(
                    Path.GetDirectoryName(AssemblyPath) ?? string.Empty,
                    "data");

                const string tabName = "STING Tools";
                application.CreateRibbonTab(tabName);

                BuildSelectPanel(application, tabName);
                BuildDocsPanel(application, tabName);
                BuildTagsPanel(application, tabName);
                BuildOrganisePanel(application, tabName);
                BuildTempPanel(application, tabName);

                // Register the dockable panel
                RegisterDockablePanel(application);

                StingLog.Info("STING Tools ribbon loaded successfully");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("STING Tools",
                    "Failed to initialise STING Tools:\n" + ex.Message);
                StingLog.Error("Startup failed", ex);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            StingLog.Info("STING Tools shutting down");
            return Result.Succeeded;
        }

        // ── Select Panel ──────────────────────────────────────────────────
        private void BuildSelectPanel(UIControlledApplication app, string tab)
        {
            var panel = app.CreateRibbonPanel(tab, "Select");
            string asmPath = AssemblyPath;

            // Category selectors pulldown
            var catGroup = panel.AddItem(
                new PulldownButtonData("grpCatSelect", "Category")) as PulldownButton;
            if (catGroup != null)
            {
                catGroup.LongDescription = "Select elements by category in active view";
                AddPulldownItem(catGroup, "btnSelLgt", "Lighting",
                    asmPath, typeof(Select.SelectLightingCommand).FullName,
                    "Select all lighting fixtures");
                AddPulldownItem(catGroup, "btnSelElc", "Electrical",
                    asmPath, typeof(Select.SelectElectricalCommand).FullName,
                    "Select all electrical equipment");
                AddPulldownItem(catGroup, "btnSelMch", "Mechanical",
                    asmPath, typeof(Select.SelectMechanicalCommand).FullName,
                    "Select all mechanical equipment");
                AddPulldownItem(catGroup, "btnSelPlb", "Plumbing",
                    asmPath, typeof(Select.SelectPlumbingCommand).FullName,
                    "Select all plumbing fixtures");
                AddPulldownItem(catGroup, "btnSelAir", "Air Terminals",
                    asmPath, typeof(Select.SelectAirTerminalsCommand).FullName,
                    "Select all air terminals");
                AddPulldownItem(catGroup, "btnSelFur", "Furniture",
                    asmPath, typeof(Select.SelectFurnitureCommand).FullName,
                    "Select all furniture");
                AddPulldownItem(catGroup, "btnSelDr", "Doors",
                    asmPath, typeof(Select.SelectDoorsCommand).FullName,
                    "Select all doors");
                AddPulldownItem(catGroup, "btnSelWin", "Windows",
                    asmPath, typeof(Select.SelectWindowsCommand).FullName,
                    "Select all windows");
                AddPulldownItem(catGroup, "btnSelRm", "Rooms",
                    asmPath, typeof(Select.SelectRoomsCommand).FullName,
                    "Select all rooms");
                AddPulldownItem(catGroup, "btnSelSpr", "Sprinklers",
                    asmPath, typeof(Select.SelectSprinklersCommand).FullName,
                    "Select all sprinklers");
                AddPulldownItem(catGroup, "btnSelPipe", "Pipes",
                    asmPath, typeof(Select.SelectPipesCommand).FullName,
                    "Select all pipes");
                AddPulldownItem(catGroup, "btnSelDuct", "Ducts",
                    asmPath, typeof(Select.SelectDuctsCommand).FullName,
                    "Select all ducts");
                AddPulldownItem(catGroup, "btnSelCnd", "Conduits",
                    asmPath, typeof(Select.SelectConduitsCommand).FullName,
                    "Select all conduits");
                AddPulldownItem(catGroup, "btnSelCbl", "Cable Trays",
                    asmPath, typeof(Select.SelectCableTraysCommand).FullName,
                    "Select all cable trays");
                AddPulldownItem(catGroup, "btnSelAll", "ALL Taggable",
                    asmPath, typeof(Select.SelectAllTaggableCommand).FullName,
                    "Select all taggable elements in active view");
            }

            // State selectors pulldown
            var stateGroup = panel.AddItem(
                new PulldownButtonData("grpStateSelect", "State")) as PulldownButton;
            if (stateGroup != null)
            {
                stateGroup.LongDescription = "Select elements by tag/pin/mark state";
                AddPulldownItem(stateGroup, "btnSelUntagged", "Untagged",
                    asmPath, typeof(Select.SelectUntaggedCommand).FullName,
                    "Select elements without ISO tags");
                AddPulldownItem(stateGroup, "btnSelTagged", "Tagged",
                    asmPath, typeof(Select.SelectTaggedCommand).FullName,
                    "Select elements with complete ISO tags");
                AddPulldownItem(stateGroup, "btnSelEmptyMark", "Empty Mark",
                    asmPath, typeof(Select.SelectEmptyMarkCommand).FullName,
                    "Select elements with empty Mark parameter");
                AddPulldownItem(stateGroup, "btnSelPinned", "Pinned",
                    asmPath, typeof(Select.SelectPinnedCommand).FullName,
                    "Select pinned elements");
                AddPulldownItem(stateGroup, "btnSelUnpinned", "Unpinned",
                    asmPath, typeof(Select.SelectUnpinnedCommand).FullName,
                    "Select unpinned elements");
            }

            // Spatial selectors pulldown
            var spatialGroup = panel.AddItem(
                new PulldownButtonData("grpSpatialSelect", "Spatial")) as PulldownButton;
            if (spatialGroup != null)
            {
                spatialGroup.LongDescription = "Select elements by spatial criteria";
                AddPulldownItem(spatialGroup, "btnSelByLevel", "By Level",
                    asmPath, typeof(Select.SelectByLevelCommand).FullName,
                    "Select elements on the active view's level");
                AddPulldownItem(spatialGroup, "btnSelByRoom", "By Room",
                    asmPath, typeof(Select.SelectByRoomCommand).FullName,
                    "Select all elements in the same room as selected element");
            }

            // Bulk operations
            AddButton(panel, "btnBulkWrite", "Bulk\nParam",
                asmPath, typeof(Select.BulkParamWriteCommand).FullName,
                "Write parameter values to all selected elements");

            // Color By Parameter pulldown (Phase 2)
            var colorGroup = panel.AddItem(
                new PulldownButtonData("grpColorBy", "Color By")) as PulldownButton;
            if (colorGroup != null)
            {
                colorGroup.LongDescription = "Color elements by parameter value (Graitec/Naviate-style)";
                AddPulldownItem(colorGroup, "btnColorByParam", "Color By Parameter",
                    asmPath, typeof(Select.ColorByParameterCommand).FullName,
                    "Color elements by any parameter value with 10 built-in palettes");
                AddPulldownItem(colorGroup, "btnClearColorOvr", "Clear Color Overrides",
                    asmPath, typeof(Select.ClearColorOverridesCommand).FullName,
                    "Clear per-element graphic overrides in active view");
                AddPulldownItem(colorGroup, "btnSavePreset", "Save Color Preset",
                    asmPath, typeof(Select.SaveColorPresetCommand).FullName,
                    "Save current color scheme to COLOR_PRESETS.json");
                AddPulldownItem(colorGroup, "btnLoadPreset", "Load Color Preset",
                    asmPath, typeof(Select.LoadColorPresetCommand).FullName,
                    "Load and apply a saved color preset");
                AddPulldownItem(colorGroup, "btnCreateFilters", "Create View Filters",
                    asmPath, typeof(Select.CreateFiltersFromColorsCommand).FullName,
                    "Convert color scheme to persistent Revit ParameterFilterElements");
            }
        }

        // ── Docs Panel ──────────────────────────────────────────────────
        private void BuildDocsPanel(UIControlledApplication app, string tab)
        {
            var panel = app.CreateRibbonPanel(tab, "Docs");
            string asmPath = AssemblyPath;

            AddButton(panel, "btnSheetOrganizer",
                "Sheet\nOrganizer",
                asmPath,
                typeof(Docs.SheetOrganizerCommand).FullName,
                "Organise and manage project sheets by discipline prefix");

            AddButton(panel, "btnViewOrganizer",
                "View\nOrganizer",
                asmPath,
                typeof(Docs.ViewOrganizerCommand).FullName,
                "Organise views by discipline, type, and level");

            AddButton(panel, "btnSheetIndex",
                "Sheet\nIndex",
                asmPath,
                typeof(Docs.SheetIndexCommand).FullName,
                "Generate a sheet index schedule with revision tracking");

            AddButton(panel, "btnTransmittal",
                "Document\nTransmittal",
                asmPath,
                typeof(Docs.TransmittalCommand).FullName,
                "Create ISO 19650-compliant document transmittal records");

            // Viewport operations pulldown
            var vpGroup = panel.AddItem(
                new PulldownButtonData("grpViewports", "Viewports")) as PulldownButton;
            if (vpGroup != null)
            {
                vpGroup.LongDescription = "Viewport alignment, numbering, and text tools";
                AddPulldownItem(vpGroup, "btnAlignVP", "Align Viewports",
                    asmPath, typeof(Docs.AlignViewportsCommand).FullName,
                    "Align viewports on a sheet (top/left/center)");
                AddPulldownItem(vpGroup, "btnRenumVP", "Renumber Viewports",
                    asmPath, typeof(Docs.RenumberViewportsCommand).FullName,
                    "Renumber viewports left-to-right, top-to-bottom");
                AddPulldownItem(vpGroup, "btnTextCase", "Text Case",
                    asmPath, typeof(Docs.TextCaseCommand).FullName,
                    "Convert text notes to UPPER/lower/Title case");
                AddPulldownItem(vpGroup, "btnSumAreas", "Sum Areas",
                    asmPath, typeof(Docs.SumAreasCommand).FullName,
                    "Calculate total area of selected rooms");
            }

            // View management pulldown (Phase 4)
            var viewMgmt = panel.AddItem(
                new PulldownButtonData("grpViewMgmt", "Views")) as PulldownButton;
            if (viewMgmt != null)
            {
                viewMgmt.LongDescription = "View duplication, renaming, settings, and crop tools";
                AddPulldownItem(viewMgmt, "btnDupView", "Duplicate View",
                    asmPath, typeof(Docs.DuplicateViewCommand).FullName,
                    "Duplicate active view with all settings (filters, overrides, visibility)");
                AddPulldownItem(viewMgmt, "btnBatchRename", "Batch Rename Views",
                    asmPath, typeof(Docs.BatchRenameViewsCommand).FullName,
                    "Batch rename views: add prefix, remove suffix, UPPERCASE, standardise levels");
                AddPulldownItem(viewMgmt, "btnCopySettings", "Copy View Settings",
                    asmPath, typeof(Docs.CopyViewSettingsCommand).FullName,
                    "Copy filters and overrides from active view to other views");
                AddPulldownItem(viewMgmt, "btnAutoPlace", "Auto-Place Viewports",
                    asmPath, typeof(Docs.AutoPlaceViewportsCommand).FullName,
                    "Grid-based intelligent viewport placement on sheets");
                AddPulldownItem(viewMgmt, "btnCropContent", "Crop to Content",
                    asmPath, typeof(Docs.CropToContentCommand).FullName,
                    "Auto-crop view boundaries to element extents with padding");
                AddPulldownItem(viewMgmt, "btnBatchAlignVP", "Batch Align Viewports",
                    asmPath, typeof(Docs.BatchAlignViewportsCommand).FullName,
                    "Align viewports across all sheets to consistent position");
            }

            // Document automation pulldown
            var docAutoGroup = panel.AddItem(
                new PulldownButtonData("grpDocAuto", "Automation")) as PulldownButton;
            if (docAutoGroup != null)
            {
                docAutoGroup.LongDescription = "Document automation and ISO 19650 compliance";
                AddPulldownItem(docAutoGroup, "btnDeleteUnused", "Delete Unused Views",
                    asmPath, typeof(Docs.DeleteUnusedViewsCommand).FullName,
                    "Remove views not placed on any sheet (with confirmation)");
                AddPulldownItem(docAutoGroup, "btnSheetNaming", "Sheet Naming Check",
                    asmPath, typeof(Docs.SheetNamingCheckCommand).FullName,
                    "ISO 19650 sheet naming compliance audit");
                AddPulldownItem(docAutoGroup, "btnAutoNumSheets", "Auto-Number Sheets",
                    asmPath, typeof(Docs.AutoNumberSheetsCommand).FullName,
                    "Sequentially renumber sheets within discipline groups");
            }
        }

        // ── Tags Panel (CREATE tab from STINGTags) ────────────────────────
        private void BuildTagsPanel(UIControlledApplication app, string tab)
        {
            var panel = app.CreateRibbonPanel(tab, "Tags");
            string asmPath = AssemblyPath;

            AddButton(panel, "btnAutoTag",
                "Auto\nTag",
                asmPath,
                typeof(Tags.AutoTagCommand).FullName,
                "Apply ISO 19650 asset tags to elements in the active view");

            AddButton(panel, "btnBatchTag",
                "Batch\nTag",
                asmPath,
                typeof(Tags.BatchTagCommand).FullName,
                "Batch-apply tags to all taggable elements in the project");

            AddButton(panel, "btnTagAndCombine",
                "Tag &\nCombine",
                asmPath,
                typeof(Tags.TagAndCombineCommand).FullName,
                "One-click: auto-detect LOC/ZONE + populate tokens + tag + combine all 36 containers");

            // Additional tagging modes pulldown
            var tagModes = panel.AddItem(
                new PulldownButtonData("grpTagModes", "More")) as PulldownButton;
            if (tagModes != null)
            {
                tagModes.LongDescription = "Additional tagging modes and fix tools";
                AddPulldownItem(tagModes, "btnTagNewOnly", "Tag New Only",
                    asmPath, typeof(Tags.TagNewOnlyCommand).FullName,
                    "Tag only new/untagged elements (faster incremental tagging)");
                AddPulldownItem(tagModes, "btnReTag", "Re-Tag Selected",
                    asmPath, typeof(Organise.ReTagCommand).FullName,
                    "Force re-derive and overwrite tags on selected elements");
                AddPulldownItem(tagModes, "btnFixDups", "Fix Duplicates",
                    asmPath, typeof(Organise.FixDuplicateTagsCommand).FullName,
                    "Auto-resolve duplicate tags by assigning new unique SEQ numbers");
                AddPulldownItem(tagModes, "btnPreTagAudit", "Pre-Tag Audit",
                    asmPath, typeof(Tags.PreTagAuditCommand).FullName,
                    "Dry-run audit: predict tag assignments, collisions, ISO violations BEFORE committing");
                AddPulldownItem(tagModes, "btnFamilyPopulate", "Family-Stage Populate",
                    asmPath, typeof(Tags.FamilyStagePopulateCommand).FullName,
                    "Pre-populate all 7 tokens (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD) from category and spatial data");
            }

            // Setup pulldown
            var setupGroup = panel.AddItem(
                new PulldownButtonData("grpTagSetup", "Setup")) as PulldownButton;
            if (setupGroup != null)
            {
                setupGroup.LongDescription = "Tag configuration and shared parameter setup";
                AddPulldownItem(setupGroup, "btnTagConfig", "Tag Config",
                    asmPath, typeof(Tags.TagConfigCommand).FullName,
                    "Configure discipline/system/product/function lookup tables");
                AddPulldownItem(setupGroup, "btnLoadParams", "Load Params",
                    asmPath, typeof(Tags.LoadSharedParamsCommand).FullName,
                    "Bind shared parameters (universal + discipline) to categories");
                AddPulldownItem(setupGroup, "btnConfigEditor", "Configure",
                    asmPath, typeof(Tags.ConfigEditorCommand).FullName,
                    "View/edit/save tag lookup tables (DISC, SYS, PROD, FUNC, LOC, ZONE)");
            }

            // Token writers pulldown
            var tokenGroup = panel.AddItem(
                new PulldownButtonData("grpTokens", "Tokens")) as PulldownButton;
            if (tokenGroup != null)
            {
                tokenGroup.LongDescription = "Set individual ISO 19650 token values";
                AddPulldownItem(tokenGroup, "btnSetDisc", "Set Discipline",
                    asmPath, typeof(Tags.SetDiscCommand).FullName,
                    "Set DISC token (M, E, P, A)");
                AddPulldownItem(tokenGroup, "btnSetLoc", "Set Location",
                    asmPath, typeof(Tags.SetLocCommand).FullName,
                    "Set LOC token (BLD1, BLD2, BLD3, EXT)");
                AddPulldownItem(tokenGroup, "btnSetZone", "Set Zone",
                    asmPath, typeof(Tags.SetZoneCommand).FullName,
                    "Set ZONE token (Z01-Z04)");
                AddPulldownItem(tokenGroup, "btnSetStatus", "Set Status",
                    asmPath, typeof(Tags.SetStatusCommand).FullName,
                    "Set STATUS token (EXISTING, NEW, DEMOLISHED, TEMPORARY)");
                AddPulldownItem(tokenGroup, "btnAssignNums", "Assign Numbers",
                    asmPath, typeof(Tags.AssignNumbersCommand).FullName,
                    "Assign sequential numbers grouped by discipline/system/level");
                AddPulldownItem(tokenGroup, "btnBuildTags", "Build Tags",
                    asmPath, typeof(Tags.BuildTagsCommand).FullName,
                    "Rebuild assembled tags from existing token values");
                AddPulldownItem(tokenGroup, "btnCombineParams", "Combine Parameters",
                    asmPath, typeof(Tags.CombineParametersCommand).FullName,
                    "Populate all tag containers (ASS_TAG_1-6 + discipline tags) from tokens");
            }

            // Smart Tag Placement pulldown (Phase 3)
            var placementGroup = panel.AddItem(
                new PulldownButtonData("grpPlacement", "Placement")) as PulldownButton;
            if (placementGroup != null)
            {
                placementGroup.LongDescription = "Smart annotation tag placement with collision avoidance";
                AddPulldownItem(placementGroup, "btnSmartPlace", "Smart Place Tags",
                    asmPath, typeof(Tags.SmartPlaceTagsCommand).FullName,
                    "Place annotation tags with 8-position scoring and collision avoidance");
                AddPulldownItem(placementGroup, "btnArrangeTags", "Arrange Tags",
                    asmPath, typeof(Tags.ArrangeTagsCommand).FullName,
                    "Reposition existing tags to minimize overlaps");
                AddPulldownItem(placementGroup, "btnRemoveAnnotTags", "Remove Annotation Tags",
                    asmPath, typeof(Tags.RemoveAnnotationTagsCommand).FullName,
                    "Remove visual annotation tags (data tags unaffected)");
                AddPulldownItem(placementGroup, "btnBatchPlaceTags", "Batch Place Tags",
                    asmPath, typeof(Tags.BatchPlaceTagsCommand).FullName,
                    "Place annotation tags across multiple views with progress");
            }

            // QA pulldown
            var qaGroup = panel.AddItem(
                new PulldownButtonData("grpQA", "QA")) as PulldownButton;
            if (qaGroup != null)
            {
                qaGroup.LongDescription = "Tag quality assurance and validation";
                AddPulldownItem(qaGroup, "btnValidateTags", "Validate",
                    asmPath, typeof(Tags.ValidateTagsCommand).FullName,
                    "Validate tag completeness and token counts");
                AddPulldownItem(qaGroup, "btnFindDups", "Find Duplicates",
                    asmPath, typeof(Organise.FindDuplicateTagsCommand).FullName,
                    "Find and select elements with duplicate tag values");
                AddPulldownItem(qaGroup, "btnHighlight", "Highlight Invalid",
                    asmPath, typeof(Organise.HighlightInvalidCommand).FullName,
                    "Colour-code missing (red) and incomplete (orange) tags");
                AddPulldownItem(qaGroup, "btnClearOverrides", "Clear Overrides",
                    asmPath, typeof(Organise.ClearOverridesCommand).FullName,
                    "Reset graphic overrides in active view");
                AddPulldownItem(qaGroup, "btnDashboard", "Completeness Dashboard",
                    asmPath, typeof(Tags.CompletenessDashboardCommand).FullName,
                    "ISO 19650 compliance dashboard by discipline");
            }
        }

        // ── Organise Panel (ORGANISE tab from STINGTags) ──────────────────
        private void BuildOrganisePanel(UIControlledApplication app, string tab)
        {
            var panel = app.CreateRibbonPanel(tab, "Organise");
            string asmPath = AssemblyPath;

            // Tag operations pulldown
            var tagOps = panel.AddItem(
                new PulldownButtonData("grpTagOps", "Tag Ops")) as PulldownButton;
            if (tagOps != null)
            {
                tagOps.LongDescription = "Tag creation, deletion, and management";
                AddPulldownItem(tagOps, "btnTagSelected", "Tag Selected",
                    asmPath, typeof(Organise.TagSelectedCommand).FullName,
                    "Apply ISO tags to selected elements only");
                AddPulldownItem(tagOps, "btnDelTags", "Delete Tags",
                    asmPath, typeof(Organise.DeleteTagsCommand).FullName,
                    "Clear all tag parameters from selected elements");
                AddPulldownItem(tagOps, "btnRenumber", "Renumber",
                    asmPath, typeof(Organise.RenumberTagsCommand).FullName,
                    "Re-sequence tag numbers for selected elements");
                AddPulldownItem(tagOps, "btnCopyTags", "Copy Tags",
                    asmPath, typeof(Organise.CopyTagsCommand).FullName,
                    "Copy tag values from first selected element to all others");
                AddPulldownItem(tagOps, "btnSwapTags", "Swap Tags",
                    asmPath, typeof(Organise.SwapTagsCommand).FullName,
                    "Swap tag values between two selected elements");
                AddPulldownItem(tagOps, "btnReTagOps", "Re-Tag",
                    asmPath, typeof(Organise.ReTagCommand).FullName,
                    "Force re-derive and overwrite all tag tokens on selected elements");
                AddPulldownItem(tagOps, "btnFixDupsOps", "Fix Duplicates",
                    asmPath, typeof(Organise.FixDuplicateTagsCommand).FullName,
                    "Auto-resolve duplicate tags by incrementing SEQ numbers");
            }

            // Leader management pulldown
            var leaderOps = panel.AddItem(
                new PulldownButtonData("grpLeaders", "Leaders")) as PulldownButton;
            if (leaderOps != null)
            {
                leaderOps.LongDescription = "Annotation tag leader management";
                AddPulldownItem(leaderOps, "btnToggleLeaders", "Toggle Leaders",
                    asmPath, typeof(Organise.ToggleLeadersCommand).FullName,
                    "Toggle leaders on/off for selected tags (or all tags in view)");
                AddPulldownItem(leaderOps, "btnAddLeaders", "Add Leaders",
                    asmPath, typeof(Organise.AddLeadersCommand).FullName,
                    "Add leaders to all selected annotation tags");
                AddPulldownItem(leaderOps, "btnRemoveLeaders", "Remove Leaders",
                    asmPath, typeof(Organise.RemoveLeadersCommand).FullName,
                    "Remove leaders from all selected annotation tags");
                AddPulldownItem(leaderOps, "btnAlignTags", "Align Tags",
                    asmPath, typeof(Organise.AlignTagsCommand).FullName,
                    "Align tag heads horizontally, vertically, or in a row");
                AddPulldownItem(leaderOps, "btnResetTags", "Reset Tag Positions",
                    asmPath, typeof(Organise.ResetTagPositionsCommand).FullName,
                    "Move tags back to element centers (remove manual offsets)");
                AddPulldownItem(leaderOps, "btnToggleOrientation", "Toggle Orientation",
                    asmPath, typeof(Organise.ToggleTagOrientationCommand).FullName,
                    "Switch selected tags between horizontal and vertical");
                AddPulldownItem(leaderOps, "btnSnapElbow", "Snap Leader Elbows",
                    asmPath, typeof(Organise.SnapLeaderElbowCommand).FullName,
                    "Snap leader elbows to 45° or 90° angles for clean layout");
                AddPulldownItem(leaderOps, "btnFlipTags", "Flip Tags",
                    asmPath, typeof(Organise.FlipTagsCommand).FullName,
                    "Mirror tag position across element center (left↔right, up↔down)");
                AddPulldownItem(leaderOps, "btnAlignTagText", "Align Tag Text",
                    asmPath, typeof(Organise.AlignTagTextCommand).FullName,
                    "Align annotation text (left/center/right) for tags and text notes");
                AddPulldownItem(leaderOps, "btnPinTags", "Pin/Unpin Tags",
                    asmPath, typeof(Organise.PinTagsCommand).FullName,
                    "Lock tags in place to prevent accidental movement (or unlock)");
                AddPulldownItem(leaderOps, "btnAttachLeader", "Attach/Free Leader",
                    asmPath, typeof(Organise.AttachLeaderCommand).FullName,
                    "Attach leader end to host element (follows when moved) or set free");
                AddPulldownItem(leaderOps, "btnSelectByLeader", "Select Tags By Leader",
                    asmPath, typeof(Organise.SelectTagsWithLeadersCommand).FullName,
                    "Select tags with or without leaders in active view");
            }

            // Appearance pulldown — tag/leader color management
            var appearOps = panel.AddItem(
                new PulldownButtonData("grpAppearance", "Appearance")) as PulldownButton;
            if (appearOps != null)
            {
                appearOps.LongDescription = "Tag text and leader color management";
                AddPulldownItem(appearOps, "btnColorByDisc", "Color by Discipline",
                    asmPath, typeof(Organise.ColorTagsByDisciplineCommand).FullName,
                    "Color annotation tags by discipline (M=Blue, E=Gold, P=Green, etc.)");
                AddPulldownItem(appearOps, "btnSetTextColor", "Set Text Color",
                    asmPath, typeof(Organise.SetTagTextColorCommand).FullName,
                    "Set tag text color (Red, Blue, Black, Green)");
                AddPulldownItem(appearOps, "btnSetLeaderColor", "Set Leader Color",
                    asmPath, typeof(Organise.SetLeaderColorCommand).FullName,
                    "Set leader line color (only tags WITH leaders)");
                AddPulldownItem(appearOps, "btnSplitColor", "Split Text/Leader Color",
                    asmPath, typeof(Organise.SplitTagLeaderColorCommand).FullName,
                    "Set different colors for text tags vs leader tags in one step");
                AddPulldownItem(appearOps, "btnClearAnnotColors", "Clear Tag Colors",
                    asmPath, typeof(Organise.ClearAnnotationColorsCommand).FullName,
                    "Reset all annotation tag color overrides to default");
            }

            // Analysis pulldown
            var analysisOps = panel.AddItem(
                new PulldownButtonData("grpAnalysis", "Analysis")) as PulldownButton;
            if (analysisOps != null)
            {
                analysisOps.LongDescription = "Tag analysis, filtering, and export";
                AddPulldownItem(analysisOps, "btnAuditCSV", "Audit to CSV",
                    asmPath, typeof(Organise.AuditTagsCSVCommand).FullName,
                    "Export complete tag audit to CSV file");
                AddPulldownItem(analysisOps, "btnSelByDisc", "Select by Discipline",
                    asmPath, typeof(Organise.SelectByDisciplineCommand).FullName,
                    "Select all elements of a specific discipline (M, E, P, A, S)");
                AddPulldownItem(analysisOps, "btnTagStats", "Tag Statistics",
                    asmPath, typeof(Organise.TagStatsCommand).FullName,
                    "Quick tag counts by discipline/system/level for active view");
                AddPulldownItem(analysisOps, "btnTagRegister", "★ Tag Register Export",
                    asmPath, typeof(Organise.TagRegisterExportCommand).FullName,
                    "Comprehensive asset register export (40+ columns: tags, identity, spatial, MEP, cost, validation)");
            }
        }

        // ── Temp Panel ──────────────────────────────────────────────────
        private void BuildTempPanel(UIControlledApplication app, string tab)
        {
            var panel = app.CreateRibbonPanel(tab, "Temp");
            string asmPath = AssemblyPath;

            // Setup group
            var setupGroup = panel.AddItem(
                new PulldownButtonData("grpSetup", "Setup")) as PulldownButton;
            if (setupGroup != null)
            {
                setupGroup.LongDescription =
                    "Project setup: parameters, data verification";
                AddPulldownItem(setupGroup, "btnCreateParams",
                    "Create Parameters",
                    asmPath,
                    typeof(Temp.CreateParametersCommand).FullName,
                    "Bind shared parameters to the active project");
                AddPulldownItem(setupGroup, "btnCheckData",
                    "Check Data Files",
                    asmPath,
                    typeof(Temp.CheckDataCommand).FullName,
                    "Verify data files and show file inventory with SHA hashes");
                AddPulldownItem(setupGroup, "btnMasterSetup",
                    "Master Setup",
                    asmPath,
                    typeof(Temp.MasterSetupCommand).FullName,
                    "One-click full project setup: params, materials, types, schedules, templates");
            }

            // Materials group
            var matGroup = panel.AddItem(
                new PulldownButtonData("grpMaterials", "Materials")) as PulldownButton;
            if (matGroup != null)
            {
                matGroup.LongDescription =
                    "Create and manage Revit materials from CSV data";
                AddPulldownItem(matGroup, "btnCreateBLE",
                    "Create BLE Materials",
                    asmPath,
                    typeof(Temp.CreateBLEMaterialsCommand).FullName,
                    "Create building-element materials from BLE_MATERIALS.csv (815 materials)");
                AddPulldownItem(matGroup, "btnCreateMEP",
                    "Create MEP Materials",
                    asmPath,
                    typeof(Temp.CreateMEPMaterialsCommand).FullName,
                    "Create MEP materials from MEP_MATERIALS.csv (464 materials)");
            }

            // Families group
            var famGroup = panel.AddItem(
                new PulldownButtonData("grpFamilies", "Families")) as PulldownButton;
            if (famGroup != null)
            {
                famGroup.LongDescription =
                    "Create wall, ceiling, floor, roof, and MEP family types";
                AddPulldownItem(famGroup, "btnCreateWalls", "Create Walls",
                    asmPath, typeof(Temp.CreateWallsCommand).FullName,
                    "Create wall types with compound layers from BLE_MATERIALS.csv");
                AddPulldownItem(famGroup, "btnCreateFloors", "Create Floors",
                    asmPath, typeof(Temp.CreateFloorsCommand).FullName,
                    "Create floor types from BLE_MATERIALS.csv");
                AddPulldownItem(famGroup, "btnCreateCeilings", "Create Ceilings",
                    asmPath, typeof(Temp.CreateCeilingsCommand).FullName,
                    "Create ceiling types from BLE_MATERIALS.csv");
                AddPulldownItem(famGroup, "btnCreateRoofs", "Create Roofs",
                    asmPath, typeof(Temp.CreateRoofsCommand).FullName,
                    "Create roof types from BLE_MATERIALS.csv");
                AddPulldownItem(famGroup, "btnCreateDucts", "Create Ducts",
                    asmPath, typeof(Temp.CreateDuctsCommand).FullName,
                    "Create duct types from MEP_MATERIALS.csv");
                AddPulldownItem(famGroup, "btnCreatePipes", "Create Pipes",
                    asmPath, typeof(Temp.CreatePipesCommand).FullName,
                    "Create pipe types from MEP_MATERIALS.csv");
                AddPulldownItem(famGroup, "btnCreateCableTrays", "Create Cable Trays",
                    asmPath, typeof(Temp.CreateCableTraysCommand).FullName,
                    "Create cable tray types from MEP_MATERIALS.csv");
                AddPulldownItem(famGroup, "btnCreateConduits", "Create Conduits",
                    asmPath, typeof(Temp.CreateConduitsCommand).FullName,
                    "Create conduit types from MEP_MATERIALS.csv");
            }

            // Schedules group
            var schGroup = panel.AddItem(
                new PulldownButtonData("grpSchedules", "Schedules")) as PulldownButton;
            if (schGroup != null)
            {
                schGroup.LongDescription =
                    "Schedule creation, auto-populate, and CSV export";
                AddPulldownItem(schGroup, "btnFullAutoPopulate",
                    "★ Full Auto-Populate",
                    asmPath,
                    typeof(Temp.FullAutoPopulateCommand).FullName,
                    "ONE-CLICK: Tokens → Dimensions → MEP → Formulas → Tags → Combine → Grid (zero manual input)");
                AddPulldownItem(schGroup, "btnBatchSchedules",
                    "Batch Create Schedules",
                    asmPath,
                    typeof(Temp.BatchSchedulesCommand).FullName,
                    "Multi-discipline schedule creation (168 definitions)");
                AddPulldownItem(schGroup, "btnMatSchedules",
                    "Material Takeoffs",
                    asmPath,
                    typeof(Temp.CreateMaterialSchedulesCommand).FullName,
                    "Create material takeoff schedules for 8 categories");
                AddPulldownItem(schGroup, "btnAutoPopulate",
                    "Auto-Populate (Tokens Only)",
                    asmPath,
                    typeof(Temp.AutoPopulateCommand).FullName,
                    "Auto-populate tag tokens + native param mapping (lighter alternative to Full Auto-Populate)");
                AddPulldownItem(schGroup, "btnFormulaEval",
                    "Evaluate Formulas",
                    asmPath,
                    typeof(Temp.FormulaEvaluatorCommand).FullName,
                    "Evaluate 199 engineering formulas from FORMULAS_WITH_DEPENDENCIES.csv (areas, volumes, flow rates, costs)");
                AddPulldownItem(schGroup, "btnExportCSV",
                    "Export to CSV",
                    asmPath,
                    typeof(Temp.ExportCSVCommand).FullName,
                    "Export schedule data to CSV files");
            }

            // Data Pipeline group (Phase 5)
            var pipeGroup = panel.AddItem(
                new PulldownButtonData("grpDataPipeline", "Data QA")) as PulldownButton;
            if (pipeGroup != null)
            {
                pipeGroup.LongDescription = "Data pipeline validation and dynamic bindings";
                AddPulldownItem(pipeGroup, "btnValidateTemplate",
                    "Validate Template",
                    asmPath,
                    typeof(Temp.ValidateTemplateCommand).FullName,
                    "Run 20+ validation checks on data files, parameters, and project state (C# port of VALIDAT_BIM_TEMPLATE.py)");
                AddPulldownItem(pipeGroup, "btnDynamicBindings",
                    "Dynamic Bindings",
                    asmPath,
                    typeof(Temp.DynamicBindingsCommand).FullName,
                    "Load parameter-category bindings from CATEGORY_BINDINGS.csv (replaces hardcoded bindings)");
                AddPulldownItem(pipeGroup, "btnSchemaValidate",
                    "Schema Validate",
                    asmPath,
                    typeof(Temp.SchemaValidateCommand).FullName,
                    "Validate BLE/MEP material CSVs against MATERIAL_SCHEMA.json");
            }

            // Templates group
            var tplGroup = panel.AddItem(
                new PulldownButtonData("grpTemplates", "Templates")) as PulldownButton;
            if (tplGroup != null)
            {
                tplGroup.LongDescription =
                    "View templates, filters, line patterns, worksets, and project phases";
                AddPulldownItem(tplGroup, "btnCreateFilters",
                    "Create Filters",
                    asmPath,
                    typeof(Temp.CreateFiltersCommand).FullName,
                    "Create 10 multi-category discipline filters (M, E, P, A, S, FP, LV, conduits, rooms, generic)");
                AddPulldownItem(tplGroup, "btnApplyFilters",
                    "Apply Filters to Views",
                    asmPath,
                    typeof(Temp.ApplyFiltersToViewsCommand).FullName,
                    "Apply STING filters to STING view templates");
                AddPulldownItem(tplGroup, "btnCreateWorksets",
                    "Create Worksets",
                    asmPath,
                    typeof(Temp.CreateWorksetsCommand).FullName,
                    "Create 32 AEC UK-aligned discipline worksets");
                AddPulldownItem(tplGroup, "btnViewTemplates",
                    "View Templates",
                    asmPath,
                    typeof(Temp.ViewTemplatesCommand).FullName,
                    "Create 15 view templates: working, coordination, RCP, presentation, sections (with VG overrides)");
                AddPulldownItem(tplGroup, "btnLinePatterns",
                    "Line Patterns",
                    asmPath,
                    typeof(Temp.CreateLinePatternsCommand).FullName,
                    "Create 10 ISO 128 line patterns (dashed, center, hidden, fire compartment, phase boundary, etc.)");
                AddPulldownItem(tplGroup, "btnPhases",
                    "Phases",
                    asmPath,
                    typeof(Temp.CreatePhasesCommand).FullName,
                    "Create 6 project phases (per ISO 19650 best practice)");
            }
        }

        // ── Dockable Panel Registration ──────────────────────────────

        private void RegisterDockablePanel(UIControlledApplication application)
        {
            try
            {
                // Initialise the external event handler for panel button dispatching
                StingDockPanel.Initialise(application);

                // Register the dockable pane with Revit
                var provider = new StingDockPanelProvider();
                application.RegisterDockablePane(
                    StingDockPanelProvider.PaneId,
                    "STING Tools",
                    provider);

                // Add toggle button to the ribbon (on Select panel since it's first)
                // The toggle is added as a separate panel for visibility
                string asmPath = AssemblyPath;
                const string tabName = "STING Tools";
                var togglePanel = application.CreateRibbonPanel(tabName, "Panel");
                AddButton(togglePanel, "btnTogglePanel", "STING\nPanel",
                    asmPath, typeof(ToggleDockPanelCommand).FullName,
                    "Show/hide the STING Tools dockable panel");

                StingLog.Info("Dockable panel registered successfully");
            }
            catch (Exception ex)
            {
                StingLog.Error("Failed to register dockable panel", ex);
            }
        }

        // ── Data file utilities ───────────────────────────────────────

        /// <summary>Find a data file by name, searching DataPath and subdirectories.</summary>
        public static string FindDataFile(string fileName)
        {
            if (string.IsNullOrEmpty(DataPath))
                return null;

            string direct = Path.Combine(DataPath, fileName);
            if (File.Exists(direct)) return direct;

            try
            {
                foreach (string f in Directory.GetFiles(
                    DataPath, fileName, SearchOption.AllDirectories))
                {
                    return f;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FindDataFile '{fileName}': {ex.Message}");
            }
            return null;
        }

        /// <summary>Parse a CSV line respecting quoted fields.</summary>
        public static string[] ParseCsvLine(string line)
        {
            var result = new System.Collections.Generic.List<string>();
            bool inQuote = false;
            var current = new System.Text.StringBuilder();

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuote = !inQuote;
                }
                else if (c == ',' && !inQuote)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result.ToArray();
        }

        // ── Ribbon helpers ───────────────────────────────────────────────

        private static void AddButton(RibbonPanel panel, string name,
            string text, string asmPath, string className,
            string tooltip)
        {
            var data = new PushButtonData(name, text, asmPath, className);
            data.ToolTip = tooltip;
            panel.AddItem(data);
        }

        private static void AddPulldownItem(PulldownButton pulldown,
            string name, string text, string asmPath, string className,
            string tooltip)
        {
            var data = new PushButtonData(name, text, asmPath, className);
            data.ToolTip = tooltip;
            pulldown.AddPushButton(data);
        }
    }

    /// <summary>
    /// Toggle the STING Tools dockable panel visibility.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ToggleDockPanelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                DockablePane pane = commandData.Application
                    .GetDockablePane(StingDockPanelProvider.PaneId);

                if (pane == null)
                {
                    TaskDialog.Show("STING Panel",
                        "Dockable panel not found. Restart Revit to register it.");
                    return Result.Failed;
                }

                if (pane.IsShown())
                    pane.Hide();
                else
                    pane.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Toggle dockable panel failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
