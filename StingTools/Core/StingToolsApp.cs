using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;

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
            }

            // Token writers pulldown
            var tokenGroup = panel.AddItem(
                new PulldownButtonData("grpTokens", "Tokens")) as PulldownButton;
            if (tokenGroup != null)
            {
                tokenGroup.LongDescription = "Set individual ISO 19650 token values";
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
                AddPulldownItem(tagOps, "btnAuditCSV", "Audit to CSV",
                    asmPath, typeof(Organise.AuditTagsCSVCommand).FullName,
                    "Export complete tag audit to CSV file");
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
                    "Auto-Populate",
                    asmPath,
                    typeof(Temp.AutoPopulateCommand).FullName,
                    "Auto-populate DISC/PROD/SYS/FUNC/LVL on all elements");
                AddPulldownItem(schGroup, "btnExportCSV",
                    "Export to CSV",
                    asmPath,
                    typeof(Temp.ExportCSVCommand).FullName,
                    "Export schedule data to CSV files");
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
                    "Create 6 discipline view filters");
                AddPulldownItem(tplGroup, "btnApplyFilters",
                    "Apply Filters to Views",
                    asmPath,
                    typeof(Temp.ApplyFiltersToViewsCommand).FullName,
                    "Apply STING filters to STING view templates");
                AddPulldownItem(tplGroup, "btnCreateWorksets",
                    "Create Worksets",
                    asmPath,
                    typeof(Temp.CreateWorksetsCommand).FullName,
                    "Create 27 discipline worksets");
                AddPulldownItem(tplGroup, "btnViewTemplates",
                    "View Templates",
                    asmPath,
                    typeof(Temp.ViewTemplatesCommand).FullName,
                    "Create 7 discipline view templates");
                AddPulldownItem(tplGroup, "btnLinePatterns",
                    "Line Patterns",
                    asmPath,
                    typeof(Temp.CreateLinePatternsCommand).FullName,
                    "Create 6 standard line patterns (dashed, center, hidden, etc.)");
                AddPulldownItem(tplGroup, "btnPhases",
                    "Phases",
                    asmPath,
                    typeof(Temp.CreatePhasesCommand).FullName,
                    "Create 7 project phases");
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
}
