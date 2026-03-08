using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Temp
{
    /// <summary>
    /// Ported from STINGTemp 5_Schedules.panel — Batch Create Schedules.
    /// Multi-discipline schedule creation from MR_SCHEDULES.csv definition file.
    /// CSV columns (16 total):
    ///   0: Record_Type, 1: Source_File, 2: Discipline, 3: Schedule_Name,
    ///   4: Category, 5: Schedule_Type (Material Takeoff),
    ///   6: Multi_Categories, 7: Fields, 8: Filters, 9: Sorting,
    ///   10: Grouping, 11: Totals, 12: Formulas (field header aliases),
    ///   13-15: Header/Text/Background Color (reserved)
    /// Also loads SCHEDULE_FIELD_REMAP.csv for deprecated field name auto-remapping.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchSchedulesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            string dataDir = StingToolsApp.DataPath;

            if (string.IsNullOrEmpty(dataDir) || !Directory.Exists(dataDir))
            {
                TaskDialog.Show("Batch Schedules",
                    "Data directory not found. Place schedule CSV files " +
                    "in the 'data' folder alongside StingTools.dll.");
                return Result.Failed;
            }

            string csvPath = StingToolsApp.FindDataFile("MR_SCHEDULES.csv");
            if (csvPath == null)
            {
                TaskDialog.Show("Batch Schedules",
                    "MR_SCHEDULES.csv not found in data directory.\n" +
                    $"Searched: {dataDir}");
                return Result.Failed;
            }

            // Load deprecated field remaps from SCHEDULE_FIELD_REMAP.csv
            var fieldRemaps = ScheduleHelper.LoadFieldRemaps();
            int remapCount = fieldRemaps.Count;
            if (remapCount > 0)
                StingLog.Info($"Loaded {remapCount} field remaps from SCHEDULE_FIELD_REMAP.csv");

            // ── Parse all CSV lines and collect available disciplines ──
            var allLines = File.ReadAllLines(csvPath)
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                .Skip(1) // skip header row
                .ToList();

            // Gather unique disciplines from SCHEDULE records for the selection dialog
            var disciplineSet = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            int totalScheduleRows = 0;
            int totalFilterRows = 0;
            foreach (string line in allLines)
            {
                string[] cols = StingToolsApp.ParseCsvLine(line);
                if (cols.Length < 5) continue;
                string rt = cols[0].Trim();
                if (rt.Equals("SCHEDULE", StringComparison.OrdinalIgnoreCase) ||
                    rt.Equals("MATERIAL_TAKEOFF", StringComparison.OrdinalIgnoreCase))
                {
                    totalScheduleRows++;
                    string disc = cols[2].Trim();
                    if (!string.IsNullOrEmpty(disc))
                        disciplineSet.Add(disc);
                }
                else if (rt.Equals("VIEW_FILTER", StringComparison.OrdinalIgnoreCase))
                {
                    totalFilterRows++;
                }
            }

            // ── Schedule type selection dialog ──
            var td = new TaskDialog("STING Tools - Batch Schedules");
            td.MainInstruction = "Choose which schedules to create";
            td.MainContent =
                $"MR_SCHEDULES.csv contains {totalScheduleRows} schedule definitions " +
                $"across {disciplineSet.Count} disciplines, plus {totalFilterRows} view filters.\n\n" +
                "Disciplines: " + string.Join(", ", disciplineSet) + "\n\n" +
                "Choose what to create:";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"All Schedules ({totalScheduleRows} schedules + {totalFilterRows} filters)",
                "Create every schedule and view filter defined in the CSV");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Choose by Discipline...",
                "Select which discipline(s) to include");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "View Filters Only",
                $"Create only the {totalFilterRows} view filters (no schedules)");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;
            td.DefaultButton = TaskDialogResult.CommandLink1;

            TaskDialogResult choice = td.Show();
            if (choice == TaskDialogResult.Cancel)
                return Result.Cancelled;

            // Build allowed disciplines set
            var allowedDisciplines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool createSchedules = true;
            bool createFilters = true;

            if (choice == TaskDialogResult.CommandLink1)
            {
                // All
                foreach (string d in disciplineSet) allowedDisciplines.Add(d);
            }
            else if (choice == TaskDialogResult.CommandLink2)
            {
                // Discipline picker: checkboxes via multi-command-link dialog
                // Build a selection string for each discipline with count
                var discCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in allLines)
                {
                    string[] cols = StingToolsApp.ParseCsvLine(line);
                    if (cols.Length < 5) continue;
                    string rt = cols[0].Trim();
                    if (!rt.Equals("SCHEDULE", StringComparison.OrdinalIgnoreCase) &&
                        !rt.Equals("MATERIAL_TAKEOFF", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string disc = cols[2].Trim();
                    if (!discCounts.ContainsKey(disc)) discCounts[disc] = 0;
                    discCounts[disc]++;
                }

                // Show discipline selection (use comma-separated input via TaskDialog)
                var discList = disciplineSet.ToList();
                var discDesc = new StringBuilder();
                for (int i = 0; i < discList.Count; i++)
                {
                    int cnt = discCounts.TryGetValue(discList[i], out int c) ? c : 0;
                    discDesc.AppendLine($"  {i + 1}. {discList[i]} ({cnt} schedules)");
                }

                var pickDlg = new TaskDialog("STING Tools - Select Disciplines");
                pickDlg.MainInstruction = "Select disciplines to create";
                pickDlg.MainContent =
                    "Available disciplines:\n" + discDesc.ToString() +
                    "\nChoose a scope:";

                // Offer common groupings as command links
                pickDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "MEP Only (Mechanical + Electrical + Plumbing + Fire Protection)",
                    "Most common for MEP engineers");
                pickDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Architecture + Structure Only",
                    "Architectural and structural schedules");
                pickDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    "All Disciplines",
                    $"Create all {totalScheduleRows} schedules");
                pickDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
                pickDlg.DefaultButton = TaskDialogResult.CommandLink1;

                TaskDialogResult pickResult = pickDlg.Show();
                if (pickResult == TaskDialogResult.Cancel)
                    return Result.Cancelled;

                if (pickResult == TaskDialogResult.CommandLink1)
                {
                    // MEP
                    foreach (string d in disciplineSet)
                    {
                        string dl = d.ToUpperInvariant();
                        if (dl.Contains("MECH") || dl.Contains("ELEC") || dl.Contains("PLUMB") ||
                            dl.Contains("FIRE") || dl.Contains("MEP") || dl.Contains("HVAC") ||
                            dl.Contains("LIGHT") || dl.Contains("COMM") || dl.Contains("DATA") ||
                            dl.Contains("SECUR") || dl.Contains("SPRINK"))
                            allowedDisciplines.Add(d);
                    }
                }
                else if (pickResult == TaskDialogResult.CommandLink2)
                {
                    // Architecture + Structure
                    foreach (string d in disciplineSet)
                    {
                        string dl = d.ToUpperInvariant();
                        if (dl.Contains("ARCH") || dl.Contains("STRUCT") || dl.Contains("GENERAL"))
                            allowedDisciplines.Add(d);
                    }
                }
                else
                {
                    // All
                    foreach (string d in disciplineSet) allowedDisciplines.Add(d);
                }

                if (allowedDisciplines.Count == 0)
                {
                    TaskDialog.Show("Batch Schedules", "No disciplines matched. No schedules created.");
                    return Result.Cancelled;
                }
            }
            else if (choice == TaskDialogResult.CommandLink3)
            {
                // Filters only
                createSchedules = false;
                createFilters = true;
            }

            int created = 0;
            int skipped = 0;
            int remapped = 0;
            int matTakeoffs = 0;
            int formatted = 0;
            int viewFiltersCreated = 0;
            var existingNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Select(s => s.Name));

            // DAT-004: Collect existing view filter names
            var existingFilterNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ParameterFilterElement))
                    .Cast<ParameterFilterElement>()
                    .Select(f => f.Name));

            using (Transaction tx = new Transaction(doc, "STING Batch Create Schedules"))
            {
                tx.Start();

                foreach (string line in allLines)
                {
                    string[] cols = StingToolsApp.ParseCsvLine(line);
                    if (cols.Length < 5) continue;

                    // CSV columns (16): Record_Type(0), Source_File(1), Discipline(2),
                    // Schedule_Name(3), Category(4), Schedule_Type(5), Multi_Categories(6),
                    // Fields(7), Filters(8), Sorting(9), Grouping(10), Totals(11),
                    // Formulas(12), Header_Color(13), Text_Color(14), Background_Color(15)

                    // DAT-004: Handle VIEW_FILTER records
                    string recordType = cols[0].Trim();
                    if (recordType.Equals("VIEW_FILTER", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!createFilters) continue;
                        string filterName = cols.Length > 3 ? cols[3].Trim() : "";
                        string filterCats = cols.Length > 4 ? cols[4].Trim() : "";
                        if (string.IsNullOrEmpty(filterName) || existingFilterNames.Contains(filterName))
                            continue;

                        try
                        {
                            // Parse category names to BuiltInCategory set
                            var catIds = new List<ElementId>();
                            string[] catNames = filterCats.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (string catStr in catNames)
                            {
                                string cn = catStr.Trim();
                                Category cat = doc.Settings.Categories.Cast<Category>()
                                    .FirstOrDefault(c => c.Name.Equals(cn, StringComparison.OrdinalIgnoreCase));
                                if (cat != null)
                                    catIds.Add(cat.Id);
                            }

                            if (catIds.Count > 0)
                            {
                                ParameterFilterElement.Create(doc, filterName, catIds);
                                viewFiltersCreated++;
                                existingFilterNames.Add(filterName);
                                StingLog.Info($"Created view filter: {filterName} ({catIds.Count} categories)");
                            }
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"View filter create failed '{filterName}': {ex.Message}");
                        }
                        continue;
                    }

                    // Skip non-SCHEDULE records
                    if (!recordType.Equals("SCHEDULE", StringComparison.OrdinalIgnoreCase) &&
                        !recordType.Equals("MATERIAL_TAKEOFF", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!createSchedules) continue;

                    // FIX: Correct column indices — CSV has Record_Type at col 0
                    // Col 0: Record_Type, 1: Source_File, 2: Discipline, 3: Schedule_Name,
                    // 4: Category, 5: Schedule_Type, 6: Multi_Categories, 7: Fields,
                    // 8: Filters, 9: Sorting, 10: Grouping, 11: Totals, 12: Formulas
                    string discipline = cols[2].Trim();
                    string name = cols[3].Trim();
                    string category = cols.Length > 4 ? cols[4].Trim() : "";
                    string scheduleType = cols.Length > 5 ? cols[5].Trim() : "";
                    string multiCats = cols.Length > 6 ? cols[6].Trim() : "";
                    string fieldsSpec = cols.Length > 7 ? cols[7].Trim() : "";
                    string filterSpec = cols.Length > 8 ? cols[8].Trim() : "";
                    string sortSpec = cols.Length > 9 ? cols[9].Trim() : "";
                    string groupSpec = cols.Length > 10 ? cols[10].Trim() : "";
                    string totalSpec = cols.Length > 11 ? cols[11].Trim() : "";
                    string formulaSpec = cols.Length > 12 ? cols[12].Trim() : "";

                    // Filter by selected discipline(s)
                    if (allowedDisciplines.Count > 0 && !allowedDisciplines.Contains(discipline))
                    {
                        skipped++;
                        continue;
                    }

                    if (string.IsNullOrEmpty(name)) continue;
                    if (existingNames.Contains(name))
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        bool isMaterialTakeoff = scheduleType.Equals(
                            "Material Takeoff", StringComparison.OrdinalIgnoreCase);

                        // Create the schedule or material takeoff
                        ViewSchedule vs = CreateScheduleByType(
                            doc, isMaterialTakeoff, category, multiCats);

                        if (vs == null)
                        {
                            skipped++;
                            continue;
                        }

                        vs.Name = name;
                        if (isMaterialTakeoff) matTakeoffs++;

                        // Build formula map: custom param name → built-in field name (for fallback lookup)
                        var formulaMap = ScheduleHelper.ParseFormulaSpec(formulaSpec);

                        // Add fields from CSV column 7, with field remap fallback + formula fallback
                        var addedFieldIds = new Dictionary<string, ScheduleFieldId>(
                            StringComparer.OrdinalIgnoreCase);

                        if (!string.IsNullOrEmpty(fieldsSpec))
                        {
                            remapped += ScheduleHelper.AddFieldsTracked(
                                doc, vs, fieldsSpec, fieldRemaps, formulaMap, addedFieldIds);
                        }

                        // Apply column heading overrides from Formulas column + auto-humanize
                        bool didFormat = false;
                        didFormat |= ScheduleHelper.ApplyFieldHeaders(vs,
                            formulaMap.Count > 0 ? formulaMap : null);

                        // Apply grouping (inserts as first sort field with ShowHeader)
                        if (!string.IsNullOrEmpty(groupSpec))
                            didFormat |= ScheduleHelper.ApplyGrouping(
                                doc, vs, groupSpec, addedFieldIds);

                        // Apply sorting
                        if (!string.IsNullOrEmpty(sortSpec))
                            didFormat |= ScheduleHelper.ApplySorting(
                                doc, vs, sortSpec, addedFieldIds);

                        // Apply totals
                        if (!string.IsNullOrEmpty(totalSpec))
                            didFormat |= ScheduleHelper.ApplyTotals(vs, totalSpec, addedFieldIds);

                        // Apply filters
                        if (!string.IsNullOrEmpty(filterSpec))
                            didFormat |= ScheduleHelper.ApplyFilters(
                                doc, vs, filterSpec, addedFieldIds);

                        if (didFormat) formatted++;
                        created++;
                        existingNames.Add(name);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Schedule create failed '{name}': {ex.Message}");
                        skipped++;
                    }
                }

                tx.Commit();
            }
            var report = new StringBuilder();
            report.AppendLine($"Created {created} schedules ({matTakeoffs} material takeoffs).");
            report.AppendLine($"Skipped {skipped} (exist or failed).");
            // ENH-010: Auto-link created filters to matching STING view templates
            int filtersLinked = 0;
            if (viewFiltersCreated > 0)
            {
                report.AppendLine($"Created {viewFiltersCreated} view filters from VIEW_FILTER records.");

                try
                {
                    // Get all STING view templates
                    var stingTemplates = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Where(v => v.IsTemplate && v.Name.StartsWith("STING"))
                        .ToList();

                    // Get all filters we just created (by name)
                    var newFilters = new FilteredElementCollector(doc)
                        .OfClass(typeof(ParameterFilterElement))
                        .Cast<ParameterFilterElement>()
                        .Where(f => existingFilterNames.Contains(f.Name))
                        .ToList();

                    // Discipline keywords for matching filters to templates
                    var disciplineKeywords = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Mechanical"] = new[] { "Mech", "HVAC", "Mechanical" },
                        ["Electrical"] = new[] { "Elec", "Electrical", "Lighting" },
                        ["Plumbing"] = new[] { "Plumb", "Plumbing", "Hydraulic" },
                        ["Fire"] = new[] { "Fire", "Sprinkler" },
                        ["Architectural"] = new[] { "Arch", "Architectural" },
                        ["Structural"] = new[] { "Struct", "Structural" },
                    };

                    using (Transaction linkTx = new Transaction(doc, "STING Link Filters to Templates"))
                    {
                        linkTx.Start();
                        foreach (var filter in newFilters)
                        {
                            foreach (var template in stingTemplates)
                            {
                                // Match filter to template by discipline keyword
                                bool shouldLink = false;
                                foreach (var kvp in disciplineKeywords)
                                {
                                    bool filterMatches = kvp.Value.Any(k =>
                                        filter.Name.Contains(k, StringComparison.OrdinalIgnoreCase));
                                    bool templateMatches = kvp.Value.Any(k =>
                                        template.Name.Contains(k, StringComparison.OrdinalIgnoreCase));
                                    if (filterMatches && templateMatches)
                                    {
                                        shouldLink = true;
                                        break;
                                    }
                                }

                                if (shouldLink)
                                {
                                    try
                                    {
                                        template.AddFilter(filter.Id);
                                        template.SetFilterVisibility(filter.Id, true);
                                        filtersLinked++;
                                    }
                                    catch { /* Filter may already be on template */ }
                                }
                            }
                        }
                        linkTx.Commit();
                    }

                    if (filtersLinked > 0)
                        report.AppendLine($"Auto-linked {filtersLinked} filters to STING view templates.");
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Filter auto-linking failed: {ex.Message}");
                }
            }
            if (formatted > 0)
                report.AppendLine($"Applied formatting (sort/group/totals/filters) to {formatted}.");
            if (remapped > 0)
                report.AppendLine($"Remapped {remapped} deprecated field name(s).");
            report.Append($"Source: MR_SCHEDULES.csv");

            TaskDialog.Show("Batch Schedules", report.ToString());

            return Result.Succeeded;
        }

        /// <summary>Create schedule or material takeoff based on type and category.</summary>
        private static ViewSchedule CreateScheduleByType(Document doc,
            bool isMaterialTakeoff, string category, string multiCats)
        {
            if (isMaterialTakeoff)
            {
                // Material Takeoff: resolve category
                ElementId catId = ElementId.InvalidElementId;

                if (!string.IsNullOrEmpty(multiCats))
                {
                    // Use first category from semicolon-separated multi-cat list
                    string firstCat = multiCats.Split(';')[0].Trim();
                    if (ScheduleHelper.TryGetCategory(firstCat, out BuiltInCategory bic))
                        catId = new ElementId(bic);
                }
                else if (!string.IsNullOrEmpty(category) &&
                    ScheduleHelper.TryGetCategory(category, out BuiltInCategory singleBic))
                {
                    catId = new ElementId(singleBic);
                }

                return ViewSchedule.CreateMaterialTakeoff(doc, catId);
            }
            else
            {
                // Regular schedule
                if (string.IsNullOrEmpty(category)) return null;
                if (!ScheduleHelper.TryGetCategory(category, out BuiltInCategory bic))
                    return null;
                return ViewSchedule.CreateSchedule(doc, new ElementId(bic));
            }
        }
    }

    /// <summary>Shared schedule formatting helpers for batch schedule creation.</summary>
    internal static class ScheduleHelper
    {
        /// <summary>Map category display name to Revit BuiltInCategory.</summary>
        public static bool TryGetCategory(string name, out BuiltInCategory bic)
        {
            bic = BuiltInCategory.INVALID;
            if (string.IsNullOrEmpty(name)) return false;

            if (CategoryMap.TryGetValue(name, out bic))
                return true;

            // Try direct enum parse for OST_ names
            if (name.StartsWith("OST_") && Enum.TryParse(name, out bic))
                return true;

            return false;
        }

        private static readonly Dictionary<string, BuiltInCategory> CategoryMap =
            new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
        {
            // Architectural
            { "Walls", BuiltInCategory.OST_Walls },
            { "Doors", BuiltInCategory.OST_Doors },
            { "Windows", BuiltInCategory.OST_Windows },
            { "Floors", BuiltInCategory.OST_Floors },
            { "Ceilings", BuiltInCategory.OST_Ceilings },
            { "Roofs", BuiltInCategory.OST_Roofs },
            { "Rooms", BuiltInCategory.OST_Rooms },
            { "Stairs", BuiltInCategory.OST_Stairs },
            { "Ramps", BuiltInCategory.OST_Ramps },
            { "Curtain Panels", BuiltInCategory.OST_CurtainWallPanels },
            { "Furniture", BuiltInCategory.OST_Furniture },
            { "Furniture Systems", BuiltInCategory.OST_FurnitureSystems },
            { "Casework", BuiltInCategory.OST_Casework },
            { "Generic Models", BuiltInCategory.OST_GenericModel },
            // Structural
            { "Structural Columns", BuiltInCategory.OST_StructuralColumns },
            { "Structural Framing", BuiltInCategory.OST_StructuralFraming },
            { "Structural Foundations", BuiltInCategory.OST_StructuralFoundation },
            { "Structural Rebar", BuiltInCategory.OST_Rebar },
            // MEP — Mechanical
            { "Mechanical Equipment", BuiltInCategory.OST_MechanicalEquipment },
            { "Ducts", BuiltInCategory.OST_DuctCurves },
            { "Duct Fittings", BuiltInCategory.OST_DuctFitting },
            { "Duct Accessories", BuiltInCategory.OST_DuctAccessory },
            { "Air Terminals", BuiltInCategory.OST_DuctTerminal },
            { "Flex Ducts", BuiltInCategory.OST_FlexDuctCurves },
            // MEP — Electrical
            { "Electrical Equipment", BuiltInCategory.OST_ElectricalEquipment },
            { "Electrical Fixtures", BuiltInCategory.OST_ElectricalFixtures },
            { "Lighting Fixtures", BuiltInCategory.OST_LightingFixtures },
            { "Lighting Devices", BuiltInCategory.OST_LightingDevices },
            { "Conduits", BuiltInCategory.OST_Conduit },
            { "Cable Trays", BuiltInCategory.OST_CableTray },
            { "Electrical Circuits", BuiltInCategory.OST_ElectricalCircuit },
            // MEP — Plumbing / Fire
            { "Plumbing Fixtures", BuiltInCategory.OST_PlumbingFixtures },
            { "Pipes", BuiltInCategory.OST_PipeCurves },
            { "Pipe Fittings", BuiltInCategory.OST_PipeFitting },
            { "Pipe Accessories", BuiltInCategory.OST_PipeAccessory },
            { "Flex Pipes", BuiltInCategory.OST_FlexPipeCurves },
            { "Sprinklers", BuiltInCategory.OST_Sprinklers },
            { "Fire Alarm Devices", BuiltInCategory.OST_FireAlarmDevices },
            // Site
            { "Topography", BuiltInCategory.OST_Topography },
        };

        /// <summary>
        /// Load SCHEDULE_FIELD_REMAP.csv — maps deprecated field names to their
        /// consolidated replacements. Only rows with Action=REMAPPED are loaded.
        /// </summary>
        public static Dictionary<string, string> LoadFieldRemaps()
        {
            var remaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string path = StingToolsApp.FindDataFile("SCHEDULE_FIELD_REMAP.csv");
            if (path == null) return remaps;

            try
            {
                var lines = File.ReadAllLines(path)
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                    .Skip(1);

                foreach (string line in lines)
                {
                    string[] cols = StingToolsApp.ParseCsvLine(line);
                    if (cols.Length < 3) continue;

                    string oldField = cols[0].Trim();
                    string newField = cols[1].Trim();
                    string action = cols[2].Trim();

                    if (action.Equals("REMAPPED", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(oldField)
                        && !string.IsNullOrEmpty(newField))
                    {
                        if (remaps.ContainsKey(oldField))
                            StingLog.Warn($"SCHEDULE_FIELD_REMAP: duplicate source key '{oldField}' — " +
                                $"overwriting '{remaps[oldField]}' with '{newField}'");
                        remaps[oldField] = newField;
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Failed to load SCHEDULE_FIELD_REMAP.csv: {ex.Message}");
            }

            return remaps;
        }

        /// <summary>
        /// Parse Formulas column spec into dictionary: custom_param_name → builtin_field_name.
        /// Format: "ASS_ID_TXT=Mark, ASS_LOC_TXT=Level, ..."
        /// </summary>
        public static Dictionary<string, string> ParseFormulaSpec(string formulaSpec)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(formulaSpec)) return map;

            string[] entries = formulaSpec.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string entry in entries)
            {
                string[] parts = entry.Split(new[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    string customName = parts[0].Trim();
                    string builtinName = parts[1].Trim();
                    if (!string.IsNullOrEmpty(customName) && !string.IsNullOrEmpty(builtinName))
                        map[customName] = builtinName;
                }
            }
            return map;
        }

        /// <summary>
        /// Add fields to schedule from comma-separated field spec, tracking field IDs by name.
        /// Uses three-tier fallback: (1) exact name, (2) deprecated remap, (3) formula alias.
        /// </summary>
        /// <returns>Number of fields that were remapped from deprecated names.</returns>
        public static int AddFieldsTracked(Document doc, ViewSchedule vs, string fieldSpec,
            Dictionary<string, string> fieldRemaps,
            Dictionary<string, string> formulaMap,
            Dictionary<string, ScheduleFieldId> addedFieldIds)
        {
            string[] fieldEntries = fieldSpec.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            // Build lookup of schedulable fields by name
            var available = vs.Definition.GetSchedulableFields();
            var fieldLookup = new Dictionary<string, SchedulableField>(StringComparer.OrdinalIgnoreCase);
            foreach (var sf in available)
            {
                string sfName = sf.GetName(doc);
                if (!string.IsNullOrEmpty(sfName) && !fieldLookup.ContainsKey(sfName))
                    fieldLookup[sfName] = sf;
            }

            int remappedCount = 0;

            foreach (string entry in fieldEntries)
            {
                string fieldName = entry.Trim();
                if (string.IsNullOrEmpty(fieldName)) continue;

                try
                {
                    SchedulableField sf = null;
                    string resolvedName = fieldName;

                    // Tier 1: Try exact name
                    if (fieldLookup.TryGetValue(fieldName, out sf))
                    {
                        resolvedName = fieldName;
                    }
                    // Tier 2: Try deprecated field remap
                    else if (fieldRemaps.TryGetValue(fieldName, out string remapped)
                        && fieldLookup.TryGetValue(remapped, out sf))
                    {
                        resolvedName = remapped;
                        remappedCount++;
                        StingLog.Info($"Schedule field remapped: '{fieldName}' → '{remapped}'");
                    }
                    // Tier 3: Try formula alias (builtin field name)
                    else if (formulaMap.TryGetValue(fieldName, out string builtinName)
                        && fieldLookup.TryGetValue(builtinName, out sf))
                    {
                        resolvedName = builtinName;
                    }

                    if (sf != null)
                    {
                        ScheduleField added = vs.Definition.AddField(sf);
                        if (added != null && !addedFieldIds.ContainsKey(fieldName))
                            addedFieldIds[fieldName] = added.FieldId;
                        // Also register by the resolved name for sort/group lookups
                        if (added != null && resolvedName != fieldName
                            && !addedFieldIds.ContainsKey(resolvedName))
                            addedFieldIds[resolvedName] = added.FieldId;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Schedule field add '{fieldName}': {ex.Message}");
                }
            }

            return remappedCount;
        }

        /// <summary>
        /// Apply column heading overrides from the Formulas map, then auto-humanize
        /// any remaining raw parameter names (e.g. ASS_ID_TXT → Asset ID).
        /// </summary>
        public static bool ApplyFieldHeaders(ViewSchedule vs,
            Dictionary<string, string> formulaMap)
        {
            bool applied = false;
            int fieldCount = vs.Definition.GetFieldCount();

            for (int i = 0; i < fieldCount; i++)
            {
                try
                {
                    ScheduleField field = vs.Definition.GetField(i);
                    string currentHeading = field.ColumnHeading;

                    // Priority 1: Explicit formula map override
                    if (formulaMap != null &&
                        formulaMap.TryGetValue(currentHeading, out string displayName))
                    {
                        field.ColumnHeading = displayName;
                        applied = true;
                    }
                    // Priority 2: Auto-humanize raw STING parameter names
                    else if (IsRawParamName(currentHeading))
                    {
                        string humanized = HumanizeParamName(currentHeading);
                        if (humanized != currentHeading)
                        {
                            field.ColumnHeading = humanized;
                            applied = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Apply field header [{i}]: {ex.Message}");
                }
            }
            return applied;
        }

        /// <summary>
        /// Returns true if the heading looks like a raw STING parameter name
        /// (e.g. ASS_ID_TXT, BLE_ELE_AREA_SQ_M, PLM_PPE_FLW_LPS).
        /// </summary>
        private static bool IsRawParamName(string heading)
        {
            if (string.IsNullOrEmpty(heading) || heading.Length < 4) return false;
            // STING params use UPPERCASE with underscores and known prefixes
            return _paramPrefixes.Any(p => heading.StartsWith(p, StringComparison.Ordinal))
                && heading.Contains('_');
        }

        private static readonly string[] _paramPrefixes = new[]
        {
            "ASS_", "BLE_", "CST_", "ELC_", "ELE_", "HVC_", "PLM_", "MAT_",
            "PRJ_", "LTG_", "FLS_", "COM_", "SEC_", "NCL_", "ICT_", "MEP_",
            "TAG_", "FP_"
        };

        /// <summary>
        /// Convert a raw parameter name like ASS_ID_TXT to a human-readable
        /// column heading like "Asset ID". Uses known abbreviation expansions
        /// and strips type suffixes (_TXT, _NR, _BOOL).
        /// </summary>
        internal static string HumanizeParamName(string paramName)
        {
            if (string.IsNullOrEmpty(paramName)) return paramName;

            // Check well-known display names first
            if (_knownDisplayNames.TryGetValue(paramName, out string known))
                return known;

            // Strip the group prefix (ASS_, BLE_, etc.)
            string working = paramName;
            foreach (string prefix in _paramPrefixes)
            {
                if (working.StartsWith(prefix, StringComparison.Ordinal))
                {
                    working = working.Substring(prefix.Length);
                    break;
                }
            }

            // Strip type suffixes
            foreach (string suffix in _typeSuffixes)
            {
                if (working.EndsWith(suffix, StringComparison.Ordinal))
                {
                    working = working.Substring(0, working.Length - suffix.Length);
                    break;
                }
            }

            // Check for unit suffixes and convert to parenthetical
            string unitLabel = "";
            foreach (var kvp in _unitSuffixes)
            {
                if (working.EndsWith(kvp.Key, StringComparison.Ordinal))
                {
                    working = working.Substring(0, working.Length - kvp.Key.Length);
                    unitLabel = $" ({kvp.Value})";
                    break;
                }
            }

            // Remove trailing underscores
            working = working.TrimEnd('_');

            // Expand known abbreviations in segments
            string[] segments = working.Split('_');
            for (int i = 0; i < segments.Length; i++)
            {
                if (_abbreviations.TryGetValue(segments[i], out string expanded))
                    segments[i] = expanded;
                else
                    segments[i] = ToTitleCase(segments[i]);
            }

            return string.Join(" ", segments) + unitLabel;
        }

        private static string ToTitleCase(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.Length == 1) return s.ToUpper();
            return char.ToUpper(s[0]) + s.Substring(1).ToLower();
        }

        private static readonly string[] _typeSuffixes = new[]
        {
            "_TXT", "_NR", "_BOOL", "_NUM"
        };

        private static readonly Dictionary<string, string> _unitSuffixes =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_SQ_M"] = "m\u00B2",
            ["_CU_M"] = "m\u00B3",
            ["_MM"] = "mm",
            ["_M"] = "m",
            ["_KW"] = "kW",
            ["_KPA"] = "kPa",
            ["_LPS"] = "L/s",
            ["_MPS"] = "m/s",
            ["_PCT"] = "%",
            ["_DEG"] = "\u00B0",
            ["_BAR"] = "bar",
            ["_GPH"] = "gal/hr",
            ["_YRS"] = "yrs",
            ["_MONTHS"] = "months",
            ["_UGX"] = "UGX",
            ["_USD"] = "USD",
            ["_MICRON"] = "\u00B5m",
        };

        private static readonly Dictionary<string, string> _abbreviations =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ID"] = "ID",
            ["COD"] = "Code",
            ["TYP"] = "Type",
            ["LOC"] = "Location",
            ["LVL"] = "Level",
            ["RM"] = "Room",
            ["SYS"] = "System",
            ["FUNC"] = "Function",
            ["PRODCT"] = "Product",
            ["SEQ"] = "Sequence",
            ["NUM"] = "Number",
            ["REF"] = "Reference",
            ["NR"] = "No.",
            ["CAT"] = "Category",
            ["CST"] = "Cost",
            ["DESC"] = "Description",
            ["MFR"] = "Manufacturer",
            ["MDL"] = "Model",
            ["SER"] = "Serial",
            ["INST"] = "Installation",
            ["MAINT"] = "Maintenance",
            ["FREQ"] = "Frequency",
            ["ELE"] = "Element",
            ["EQP"] = "Equipment",
            ["FLR"] = "Floor",
            ["STR"] = "Strainer",
            ["STRUCT"] = "Structural",
            ["PPE"] = "Pipe",
            ["VLV"] = "Valve",
            ["VNT"] = "Vent",
            ["FLW"] = "Flow",
            ["PSR"] = "Pressure",
            ["PWR"] = "Power",
            ["HED"] = "Head",
            ["DRN"] = "Drain",
            ["INS"] = "Insulation",
            ["THK"] = "Thickness",
            ["SZ"] = "Size",
            ["MAT"] = "Material",
            ["SPT"] = "Support",
            ["LD"] = "Load",
            ["CAP"] = "Capacity",
            ["GSK"] = "Gasket",
            ["EXJ"] = "Expansion Joint",
            ["EXP"] = "Expansion",
            ["GRT"] = "Grate",
            ["VEL"] = "Velocity",
            ["BKWTR"] = "Backwater",
            ["FIT"] = "Fitting",
            ["CONN"] = "Connection",
            ["DIA"] = "Diameter",
            ["AMP"] = "Amperage",
            ["VOLT"] = "Voltage",
            ["FREQ"] = "Frequency",
            ["DCT"] = "Duct",
            ["FLX"] = "Flex",
            ["FIX"] = "Fixture",
            ["CDT"] = "Conduit",
            ["CTR"] = "Cable Tray",
            ["DEV"] = "Device",
            ["PMT"] = "Procurement",
            ["PRO"] = "Purchase",
            ["PO"] = "PO",
            ["QTY"] = "Qty",
            ["DEPT"] = "Department",
            ["HOTWTR"] = "Hot Water",
            ["TEMP"] = "Temperature",
            ["EFF"] = "Efficiency",
            ["ACTU"] = "Actuation",
            ["CV"] = "Cv",
            ["OANDM"] = "O&M",
            ["TAG"] = "Tag",
            ["DISC"] = "Discipline",
            ["ZONE"] = "Zone",
            ["AREA"] = "Area",
            ["VOL"] = "Volume",
            ["HTG"] = "Heating",
            ["CLG"] = "Cooling",
            ["SUP"] = "Supply",
            ["RET"] = "Return",
            ["CEIL"] = "Ceiling",
            ["STAIR"] = "Stair",
            ["RAMP"] = "Ramp",
            ["WALL"] = "Wall",
            ["FIN"] = "Finish",
            ["INT"] = "Interior",
            ["EXT"] = "Exterior",
        };

        /// <summary>Well-known parameter → display name overrides for exact matches.</summary>
        private static readonly Dictionary<string, string> _knownDisplayNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Universal tag fields
            ["ASS_ID_TXT"] = "Asset ID",
            ["ASS_TAG_1_TXT"] = "STING Tag",
            ["ASS_TAG_2_TXT"] = "Tag (Top)",
            ["ASS_TAG_3_TXT"] = "Tag (Bottom L)",
            ["ASS_TAG_4_TXT"] = "Tag (Bottom R)",
            ["ASS_TAG_5_TXT"] = "Tag (Multi Top)",
            ["ASS_TAG_6_TXT"] = "Tag (Multi Bottom)",
            ["ASS_DISCIPLINE_COD_TXT"] = "Discipline",
            ["ASS_LOC_TXT"] = "Location",
            ["ASS_ZONE_TXT"] = "Zone",
            ["ASS_LVL_COD_TXT"] = "Level Code",
            ["ASS_SYSTEM_TYPE_TXT"] = "System Type",
            ["ASS_FUNC_TXT"] = "Function",
            ["ASS_PRODCT_COD_TXT"] = "Product Code",
            ["ASS_SEQ_NUM_TXT"] = "Sequence No.",
            ["ASS_STATUS_TXT"] = "Status",
            ["ASS_RM_COD_TXT"] = "Room Ref.",
            ["ASS_DESCRIPTION_TXT"] = "Description",
            ["ASS_CATEGORY_TXT"] = "Category",
            ["ASS_CAT_TXT"] = "Category",
            ["ASS_MANUFACTURER_TXT"] = "Manufacturer",
            ["ASS_MODEL_NR_TXT"] = "Model No.",
            ["ASS_SERIAL_NR_TXT"] = "Serial No.",
            ["ASS_PART_NUMBER_TXT"] = "Part Number",
            ["ASS_CAPACITY_TXT"] = "Capacity",
            ["ASS_EQUIPMENT_TAG_TXT"] = "Equipment Tag",
            ["ASS_DEPARTMENT_ASSIGNMENT_TXT"] = "Department",
            ["ASS_INST_DATE_TXT"] = "Install Date",
            ["ASS_INST_DETAIL_TXT"] = "Install Detail",
            ["ASS_WARRANTY_PERIOD_TXT"] = "Warranty Period",
            ["ASS_WARRANTY_EXPIRATION_DATE_TXT"] = "Warranty Expiry",
            ["ASS_MAINTENANCE_SCHEDULE_TXT"] = "Maintenance Schedule",
            ["ASS_MAINTENANCE_FREQUENCY_MONTHS"] = "Maintenance Freq. (months)",
            ["ASS_EXPECTED_LIFE_YEARS_YRS"] = "Expected Life (yrs)",
            ["ASS_LOCAL_SERVICE_AVAILABLE_TXT"] = "Local Service Available",
            ["ASS_OANDM_MANUALS_PROVIDED_TXT"] = "O&M Manuals Provided",
            ["ASS_CRITICALITY_RATING_NR"] = "Criticality Rating",
            // Cost fields
            ["ASS_CST_QUANTITY_NR"] = "Quantity",
            ["ASS_CST_UNIT_PRICE_UGX_NR"] = "Unit Price (UGX)",
            ["ASS_CST_TOTAL_UGX_NR"] = "Total Cost (UGX)",
            ["ASS_CST_INSTALL_UGX_NR"] = "Installation Cost (UGX)",
            ["ASS_CST_ANNUAL_MAINTENANCE_UGX_NR"] = "Annual Maintenance (UGX)",
            ["ASS_CST_LIFECYCLE_UGX_NR"] = "Lifecycle Cost (UGX)",
            // Procurement
            ["ASS_PMT_CST_UNIT_RATE_UGX_NR"] = "Unit Rate (UGX)",
            ["ASS_PMT_CST_MAT_CST_UGX_NR"] = "Material Cost (UGX)",
            ["ASS_PMT_CST_LABOUR_CST_UGX_NR"] = "Labour Cost (UGX)",
            ["ASS_PMT_CST_TOTAL_CST_UGX_NR"] = "Total Cost (UGX)",
            ["ASS_PMT_INV_UNIT_TXT"] = "Unit of Measure",
            ["ASS_PMT_PRO_SUPPLIER_TXT"] = "Supplier",
            ["ASS_PMT_PRO_PO_NR_TXT"] = "PO Number",
            ["ASS_PMT_PRO_DELIVERY_DATE_TXT"] = "Delivery Date",
            ["ASS_PMT_QTY_ORDERED_NR"] = "Qty Ordered",
            // BLE fields
            ["BLE_ELE_AREA_SQ_M"] = "Element Area (m\u00B2)",
            ["BLE_ROOM_NAME_TXT"] = "Room Name",
            ["BLE_ROOM_NUMBER_TXT"] = "Room Number",
            ["BLE_WALL_LENGTH_MM"] = "Wall Length (mm)",
            ["BLE_WALL_HEIGHT_MM"] = "Wall Height (mm)",
            ["BLE_WALL_INTERIOR_FINISH_TXT"] = "Interior Finish",
            ["BLE_FLR_THICKNESS_MM"] = "Floor Thickness (mm)",
            ["BLE_FLR_LVL_NR"] = "Floor Level",
            ["BLE_CEILING_HEIGHT_MM"] = "Ceiling Height (mm)",
            ["BLE_DOOR_WIDTH_MM"] = "Door Width (mm)",
            ["BLE_DOOR_HEIGHT_MM"] = "Door Height (mm)",
            ["BLE_DOOR_MATERIAL_FINISH_TXT"] = "Door Material/Finish",
            ["BLE_DOOR_OPERATION_TYPE_SWING_SLIDE_FOLD_ROLL_TXT"] = "Door Operation Type",
            ["BLE_FINISH_TYPE_TXT"] = "Finish Type",
            ["BLE_FINISH_PAINT_AREA_SQ_M"] = "Paint Area (m\u00B2)",
            ["BLE_FINISH_PLASTER_AREA_SQ_M"] = "Plaster Area (m\u00B2)",
            ["BLE_FINISH_TILE_AREA_SQ_M"] = "Tile Area (m\u00B2)",
            ["BLE_STAIR_WIDTH_MM"] = "Stair Width (mm)",
            ["BLE_STAIR_RISE_MM"] = "Stair Rise (mm)",
            ["BLE_STAIR_GOING_MM"] = "Stair Going (mm)",
            ["BLE_RAMP_WIDTH_MM"] = "Ramp Width (mm)",
            ["BLE_RAMP_SLOPE_PCT"] = "Ramp Slope (%)",
            ["BLE_ROOF_SLOPE_DEG"] = "Roof Slope (\u00B0)",
            ["BLE_STRUCT_CONCRETE_GRADE_TXT"] = "Concrete Grade",
            ["BLE_STRUCT_ELE_TYPE_TXT"] = "Structural Element Type",
            ["BLE_STRUCT_STEEL_SECTION_TXT"] = "Steel Section",
            ["BLE_STANDARDS_TXT"] = "Standards",
            ["ASS_ROOM_AREA_SQ_M"] = "Room Area (m\u00B2)",
            ["ASS_ROOM_VOLUME_CU_M"] = "Room Volume (m\u00B3)",
            // Project fields
            ["PRJ_COMMENTS_TXT"] = "Remarks",
            ["PRJ_AREA_NAME_TXT"] = "Area Name",
            ["PRJ_GRID_REF_TXT"] = "Grid Reference",
        };

        /// <summary>
        /// Apply sorting from CSV sort spec.
        /// Format: "Level: Ascending; Type Mark: Ascending" or "CST_S_CON_GRADE_TXT"
        /// </summary>
        public static bool ApplySorting(Document doc, ViewSchedule vs, string sortSpec,
            Dictionary<string, ScheduleFieldId> addedFieldIds)
        {
            if (string.IsNullOrWhiteSpace(sortSpec)) return false;

            bool applied = false;
            string[] sortEntries = sortSpec.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string entry in sortEntries)
            {
                try
                {
                    string trimmed = entry.Trim();
                    string fieldName;
                    ScheduleSortOrder order = ScheduleSortOrder.Ascending;

                    // Parse "FieldName: Ascending" or "FieldName: Descending" or just "FieldName"
                    int colonIdx = trimmed.LastIndexOf(':');
                    if (colonIdx > 0)
                    {
                        fieldName = trimmed.Substring(0, colonIdx).Trim();
                        string direction = trimmed.Substring(colonIdx + 1).Trim();
                        if (direction.Equals("Descending", StringComparison.OrdinalIgnoreCase))
                            order = ScheduleSortOrder.Descending;
                    }
                    else
                    {
                        fieldName = trimmed;
                    }

                    ScheduleFieldId fieldId = FindFieldId(vs, fieldName, addedFieldIds);
                    if (fieldId == null) continue;

                    var sortField = new ScheduleSortGroupField(fieldId, order);
                    vs.Definition.AddSortGroupField(sortField);
                    applied = true;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Apply sort '{entry.Trim()}': {ex.Message}");
                }
            }
            return applied;
        }

        /// <summary>
        /// Apply grouping from CSV group spec.
        /// Format: "Level" or "System Type" or "Category"
        /// Inserts as the first sort field with ShowHeader and ShowFooter enabled.
        /// </summary>
        public static bool ApplyGrouping(Document doc, ViewSchedule vs, string groupSpec,
            Dictionary<string, ScheduleFieldId> addedFieldIds)
        {
            if (string.IsNullOrWhiteSpace(groupSpec)) return false;

            try
            {
                string fieldName = groupSpec.Trim();

                ScheduleFieldId fieldId = FindFieldId(vs, fieldName, addedFieldIds);
                if (fieldId == null) return false;

                var groupField = new ScheduleSortGroupField(fieldId, ScheduleSortOrder.Ascending);
                groupField.ShowHeader = true;
                groupField.ShowFooter = true;
                groupField.ShowBlankLine = true;

                // Insert at position 0 so grouping comes before other sort fields
                vs.Definition.InsertSortGroupField(groupField, 0);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Apply grouping '{groupSpec}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Apply totals from CSV total spec.
        /// Format: "BLE_ELE_AREA_SQ_M: SUM" or "ASS_CST_QUANTITY_NR: SUM; ASS_CST_UNIT_PRICE_UGX_NR: SUM"
        /// Enables grand totals on the schedule and marks specified fields for totalling.
        /// </summary>
        public static bool ApplyTotals(ViewSchedule vs, string totalSpec,
            Dictionary<string, ScheduleFieldId> addedFieldIds)
        {
            if (string.IsNullOrWhiteSpace(totalSpec)) return false;

            bool applied = false;
            string[] totalEntries = totalSpec.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string entry in totalEntries)
            {
                try
                {
                    string trimmed = entry.Trim();
                    // Parse "FieldName: SUM"
                    int colonIdx = trimmed.LastIndexOf(':');
                    if (colonIdx <= 0) continue;

                    string fieldName = trimmed.Substring(0, colonIdx).Trim();

                    // Find the field in the schedule
                    int fieldCount = vs.Definition.GetFieldCount();
                    for (int i = 0; i < fieldCount; i++)
                    {
                        ScheduleField field = vs.Definition.GetField(i);
                        if (addedFieldIds.TryGetValue(fieldName, out ScheduleFieldId targetId)
                            && field.FieldId == targetId)
                        {
                            field.DisplayType = ScheduleFieldDisplayType.Totals;
                            applied = true;
                            break;
                        }
                    }

                    if (applied)
                    {
                        vs.Definition.ShowGrandTotal = true;
                        vs.Definition.ShowGrandTotalTitle = true;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Apply total '{entry.Trim()}': {ex.Message}");
                }
            }
            return applied;
        }

        /// <summary>
        /// Apply filters from CSV filter spec.
        /// Formats:
        ///   "Level: (not equals) &lt;None&gt;" → NotEqual filter
        ///   "System Type: (has a value)" → HasValue filter
        ///   "Material: Name: (has a value)" → HasValue on Material: Name field
        /// </summary>
        public static bool ApplyFilters(Document doc, ViewSchedule vs, string filterSpec,
            Dictionary<string, ScheduleFieldId> addedFieldIds)
        {
            if (string.IsNullOrWhiteSpace(filterSpec)) return false;

            bool applied = false;
            // Filters may contain multiple semicolon-separated entries
            string[] filterEntries = filterSpec.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string entry in filterEntries)
            {
                try
                {
                    string trimmed = entry.Trim();

                    // Parse "(has a value)" filter
                    if (trimmed.EndsWith("(has a value)", StringComparison.OrdinalIgnoreCase))
                    {
                        string fieldName = trimmed
                            .Replace("(has a value)", "").Trim().TrimEnd(':').Trim();

                        ScheduleFieldId fieldId = FindFieldId(vs, fieldName, addedFieldIds);
                        if (fieldId != null)
                        {
                            var filter = new ScheduleFilter(fieldId, ScheduleFilterType.HasValue);
                            vs.Definition.AddFilter(filter);
                            applied = true;
                        }
                    }
                    // Parse "(not equals) <Value>" filter
                    else if (trimmed.Contains("(not equals)"))
                    {
                        int notEqIdx = trimmed.IndexOf("(not equals)",
                            StringComparison.OrdinalIgnoreCase);
                        string fieldName = trimmed.Substring(0, notEqIdx).Trim().TrimEnd(':').Trim();
                        string value = trimmed.Substring(notEqIdx + "(not equals)".Length).Trim();

                        // "<None>" in Revit means empty/null
                        if (value.Equals("<None>", StringComparison.OrdinalIgnoreCase))
                            value = "";

                        ScheduleFieldId fieldId = FindFieldId(vs, fieldName, addedFieldIds);
                        if (fieldId != null)
                        {
                            var filter = new ScheduleFilter(fieldId,
                                ScheduleFilterType.NotEqual, value);
                            vs.Definition.AddFilter(filter);
                            applied = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Apply filter '{entry.Trim()}': {ex.Message}");
                }
            }
            return applied;
        }

        /// <summary>
        /// Find a ScheduleFieldId by name — checks added fields, then scans
        /// the schedule's field list by column heading.
        /// </summary>
        private static ScheduleFieldId FindFieldId(ViewSchedule vs, string fieldName,
            Dictionary<string, ScheduleFieldId> addedFieldIds)
        {
            // Check tracked added fields first
            if (addedFieldIds.TryGetValue(fieldName, out ScheduleFieldId id))
                return id;

            // Scan schedule field list by column heading
            int fieldCount = vs.Definition.GetFieldCount();
            for (int i = 0; i < fieldCount; i++)
            {
                try
                {
                    ScheduleField f = vs.Definition.GetField(i);
                    if (f?.ColumnHeading != null &&
                        f.ColumnHeading.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                        return f.FieldId;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"FindFieldId scan [{i}]: {ex.Message}");
                }
            }

            StingLog.Warn($"Schedule field '{fieldName}' not found for sort/group/filter.");
            return null;
        }
    }

    /// <summary>
    /// Full Schedule Automation — zero-manual-input, one-click pipeline that chains:
    ///   Step 1: Auto-populate all 7 tag tokens (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD)
    ///   Step 2: Map Revit native params → STING shared params (dimensional, MEP, identity)
    ///   Step 3: Evaluate 199 engineering formulas (areas, volumes, flow rates, costs)
    ///   Step 4: Build ISO 19650 tags (ASS_TAG_1_TXT) and assign SEQ numbers
    ///   Step 5: Combine into all 37 discipline-specific tag containers
    ///   Step 6: Auto-populate Grid Reference from nearest grid lines
    ///
    /// This is the "ultimate automation" command — a project that runs this once
    /// will have all schedule fields populated without any manual input.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FullAutoPopulateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // ── Prepare indexes ────────────────────────────────────────────────
            var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
            var (tagIndex, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);

            // Load formula engine
            string csvPath = StingToolsApp.FindDataFile("FORMULAS_WITH_DEPENDENCIES.csv");
            var formulas = new List<FormulaEngine.FormulaDefinition>();
            if (csvPath != null)
            {
                formulas = FormulaEngine.LoadFormulas(csvPath);
                formulas.Sort((a, b) => a.DependencyLevel.CompareTo(b.DependencyLevel));
            }

            // Grid lines for grid reference auto-detection
            var gridLines = new FilteredElementCollector(doc)
                .OfClass(typeof(Grid))
                .Cast<Grid>()
                .ToList();

            // Collect all elements
            var allElements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToList();

            int tokensFilled = 0, nativesMapped = 0, formulasWritten = 0;
            int tagged = 0, combined = 0, gridRefSet = 0;
            int statusDetected = 0, revSet = 0;
            int totalElements = 0;
            int errors = 0;

            // Container definitions loaded from ParamRegistry.ContainerGroups

            using (Transaction tx = new Transaction(doc, "STING Full Auto-Populate"))
            {
                tx.Start();
                foreach (Element el in allElements)
                {
                    string catName = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(catName) || !popCtx.KnownCategories.Contains(catName))
                        continue;

                    totalElements++;

                    try
                    {
                    // ── STEP 1: Full 9-token population via TokenAutoPopulator ──
                    var popResult = TokenAutoPopulator.PopulateAll(doc, el, popCtx);
                    tokensFilled += popResult.TokensSet;
                    if (popResult.StatusDetected) statusDetected++;
                    if (popResult.RevSet) revSet++;

                    // ── STEP 2: Native parameter mapping ───────────────────────
                    nativesMapped += NativeParamMapper.MapAll(doc, el);

                    // ── STEP 3: Formula evaluation ─────────────────────────────
                    foreach (var formula in formulas)
                    {
                        try
                        {
                            Parameter targetParam = el.LookupParameter(formula.ParameterName);
                            if (targetParam == null || targetParam.IsReadOnly) continue;

                            var context = FormulaEngine.BuildContext(el, formula);
                            if (context == null) continue;

                            if (formula.DataType == "TEXT")
                            {
                                string result = FormulaEngine.EvaluateText(formula.Expression, context);
                                if (result != null && targetParam.StorageType == StorageType.String)
                                {
                                    if (string.IsNullOrEmpty(targetParam.AsString()))
                                    {
                                        targetParam.Set(result);
                                        formulasWritten++;
                                    }
                                }
                            }
                            else
                            {
                                double? result = FormulaEngine.EvaluateNumeric(formula.Expression, context);
                                if (result.HasValue && !double.IsNaN(result.Value) && !double.IsInfinity(result.Value))
                                {
                                    if (FormulaEngine.WriteNumericResult(targetParam, result.Value))
                                        formulasWritten++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Formula '{formula.ParameterName}' el {el.Id}: {ex.Message}");
                        }
                    }

                    // ── STEP 4: Build tag (with collision detection) ───────────
                    if (TagConfig.BuildAndWriteTag(doc, el, seqCounters, existingTags: tagIndex))
                        tagged++;

                    // ── STEP 5: Combine into all containers via ParamRegistry ───
                    string[] tokenValues = ParamRegistry.ReadTokenValues(el);
                    combined += ParamRegistry.WriteContainers(el, tokenValues, catName,
                        skipParam: ParamRegistry.TAG7);

                    // Write TAG7 + sub-sections (TAG7A-TAG7F) — rich descriptive narrative
                    combined += TagConfig.WriteTag7All(doc, el, catName, tokenValues, overwrite: true);

                    // ── STEP 6: Grid Reference ─────────────────────────────────
                    if (gridLines.Count > 0 &&
                        string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.GRID_REF)))
                    {
                        string gridRef = GetNearestGridRef(el, gridLines);
                        if (!string.IsNullOrEmpty(gridRef) &&
                            ParameterHelpers.SetIfEmpty(el, ParamRegistry.GRID_REF, gridRef))
                            gridRefSet++;
                    }

                    // Progress logging
                    if (totalElements % 500 == 0)
                        StingLog.Info($"FullAutoPopulate: {totalElements} elements processed...");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error($"FullAutoPopulate: element {el?.Id}: {ex.Message}", ex);
                        errors++;
                    }
                }

                tx.Commit();
            }
            sw.Stop();

            var report = new StringBuilder();
            report.AppendLine("Full Schedule Auto-Populate Complete");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Elements processed:    {totalElements}");
            report.AppendLine($"  Tag tokens filled:     {tokensFilled}");
            if (statusDetected > 0)
                report.AppendLine($"  STATUS auto-detected:  {statusDetected} (from Revit phases/worksets)");
            if (revSet > 0)
                report.AppendLine($"  REV auto-set:          {revSet} (revision '{popCtx.ProjectRev}')");
            report.AppendLine($"  Native params mapped:  {nativesMapped}");
            report.AppendLine($"  Formulas evaluated:    {formulasWritten}");
            report.AppendLine($"  Tags built (SEQ):      {tagged}");
            report.AppendLine($"  Containers combined:   {combined}");
            if (gridRefSet > 0)
                report.AppendLine($"  Grid refs assigned:    {gridRefSet}");
            if (errors > 0)
                report.AppendLine($"  Errors (skipped):      {errors}");
            report.AppendLine($"  Duration:              {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine();
            report.AppendLine("Pipeline: Tokens(9) → Dimensions → MEP → Formulas → Tags → Combine → Grid");

            TaskDialog.Show("Full Auto-Populate", report.ToString());

            StingLog.Info($"FullAutoPopulate: {totalElements} elements, " +
                $"tokens={tokensFilled}, statusDetect={statusDetected}, revSet={revSet}, " +
                $"natives={nativesMapped}, formulas={formulasWritten}, " +
                $"tags={tagged}, combined={combined}, grids={gridRefSet}, " +
                $"elapsed={sw.Elapsed.TotalSeconds:F1}s");

            return Result.Succeeded;
        }

        /// <summary>
        /// Find nearest grid intersection reference for an element.
        /// Returns grid reference like "A/3" (nearest X-grid / nearest Y-grid).
        /// </summary>
        private static string GetNearestGridRef(Element el, List<Grid> grids)
        {
            try
            {
                // Get element location point
                LocationPoint lp = el.Location as LocationPoint;
                LocationCurve lc = el.Location as LocationCurve;
                XYZ point;

                if (lp != null)
                    point = lp.Point;
                else if (lc != null)
                    point = (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) / 2.0;
                else
                    return null;

                string nearestX = null;
                string nearestY = null;
                double minDistX = double.MaxValue;
                double minDistY = double.MaxValue;

                foreach (Grid grid in grids)
                {
                    try
                    {
                        Curve curve = grid.Curve;
                        XYZ start = curve.GetEndPoint(0);
                        XYZ end = curve.GetEndPoint(1);
                        XYZ dir = (end - start).Normalize();

                        // Classify grid as X-direction or Y-direction
                        bool isXGrid = Math.Abs(dir.X) > Math.Abs(dir.Y); // runs along X = horizontal
                        double dist = curve.Distance(point);

                        if (isXGrid)
                        {
                            // Horizontal grid → Y reference
                            if (dist < minDistY)
                            {
                                minDistY = dist;
                                nearestY = grid.Name;
                            }
                        }
                        else
                        {
                            // Vertical grid → X reference
                            if (dist < minDistX)
                            {
                                minDistX = dist;
                                nearestX = grid.Name;
                            }
                        }
                    }
                    catch { }
                }

                if (nearestX != null && nearestY != null)
                    return $"{nearestX}/{nearestY}";
                if (nearestX != null)
                    return nearestX;
                if (nearestY != null)
                    return nearestY;
            }
            catch { }

            return null;
        }
    }

    /// <summary>
    /// Ported from STINGTemp 5_Schedules.panel — AutoPopulate.
    /// Enhanced for zero-manual-input: auto-populates ALL 7 token fields including
    /// LOC (from room/project data) and ZONE (from room department/name).
    /// Uses MEP-system-aware SYS codes (DCW, SAN, RWD, GAS via piping system detection)
    /// and family-aware PROD codes for more specific equipment identification.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoPopulateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();

            // Build spatial index for LOC/ZONE auto-detection
            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);

            int updated = 0;
            int total = 0;
            int locDetected = 0;
            int zoneDetected = 0;
            int sysAware = 0;
            int nativeMapped = 0;

            using (Transaction tx = new Transaction(doc, "STING Auto-Populate Fields"))
            {
                tx.Start();

                int apErrors = 0;
                foreach (Element el in collector)
                {
                    string catName = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(catName) || !TagConfig.DiscMap.ContainsKey(catName))
                        continue;

                    total++;

                    try
                    {
                    // ── Layer 1: Tag token population (DISC/PROD/SYS/FUNC/LVL/LOC/ZONE) ──

                    // Auto-populate DISC code from category
                    if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.DISC,
                        TagConfig.DiscMap[catName]))
                        updated++;

                    // Auto-populate PROD code (family-aware: FCU, VAV, AHU, etc.)
                    string prod = TagConfig.GetFamilyAwareProdCode(el, catName);
                    if (!string.IsNullOrEmpty(prod))
                    {
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.PROD, prod))
                            updated++;
                    }

                    // Auto-populate SYS code — 6-layer detection
                    // (connector → sys param → circuit → family → room → category)
                    string sys = TagConfig.GetMepSystemAwareSysCode(el, catName);
                    if (!string.IsNullOrEmpty(sys))
                    {
                        string prevSys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.SYS, sys))
                        {
                            updated++;
                            if (string.IsNullOrEmpty(prevSys)) sysAware++;
                        }
                    }

                    // Auto-populate FUNC code — smart detection (HVAC: SUP/RTN/EXH, HWS: HTG/DHW)
                    string func = TagConfig.GetSmartFuncCode(el, sys);
                    if (!string.IsNullOrEmpty(func))
                    {
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.FUNC, func))
                            updated++;
                    }

                    // Auto-populate level code
                    string lvl = ParameterHelpers.GetLevelCode(doc, el);
                    if (lvl != "XX")
                    {
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.LVL, lvl))
                            updated++;
                    }

                    // Auto-populate LOC from spatial data (room / project info)
                    if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.LOC)))
                    {
                        string loc = SpatialAutoDetect.DetectLoc(doc, el, roomIndex, projectLoc);
                        if (!string.IsNullOrEmpty(loc) && ParameterHelpers.SetIfEmpty(el, ParamRegistry.LOC, loc))
                        {
                            updated++;
                            locDetected++;
                        }
                    }

                    // Auto-populate ZONE from room data (department, name, number)
                    if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.ZONE)))
                    {
                        string zone = SpatialAutoDetect.DetectZone(doc, el, roomIndex);
                        if (!string.IsNullOrEmpty(zone) && ParameterHelpers.SetIfEmpty(el, ParamRegistry.ZONE, zone))
                        {
                            updated++;
                            zoneDetected++;
                        }
                    }

                    // ── Layer 2: Native parameter mapping (Revit built-in → STING shared) ──
                    // Copies Mark, Comments, Description, Manufacturer, Model, Room data,
                    // MEP parameters (flow, voltage, pressure), Type params as fallback
                    int mapped = NativeParamMapper.MapAll(doc, el);
                    nativeMapped += mapped;
                    updated += mapped;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error($"AutoPopulate: element {el?.Id}: {ex.Message}", ex);
                        apErrors++;
                    }
                }

                tx.Commit();
            }
            var report = new StringBuilder();
            report.AppendLine($"Auto-populated {updated} field values across {total} elements.");
            report.AppendLine();
            report.AppendLine("Tag tokens:");
            if (sysAware > 0) report.AppendLine($"  SYS detected from MEP systems: {sysAware}");
            if (locDetected > 0) report.AppendLine($"  LOC auto-detected from rooms/project: {locDetected}");
            if (zoneDetected > 0) report.AppendLine($"  ZONE auto-detected from rooms: {zoneDetected}");
            if (nativeMapped > 0)
            {
                report.AppendLine();
                report.AppendLine("Native parameter mapping:");
                report.AppendLine($"  Revit built-in → STING shared: {nativeMapped} values");
                report.AppendLine("  (Mark, Comments, Description, Manufacturer, Model,");
                report.AppendLine("   Room, MEP params, Type params, Uniformat, OmniClass)");
            }

            TaskDialog.Show("Auto-Populate", report.ToString());

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Ported from STINGTemp 5_Schedules.panel — Export to CSV.
    /// Exports schedule data to CSV files.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportCSVCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(s => !s.IsTitleblockRevisionSchedule)
                .OrderBy(s => s.Name)
                .ToList();

            if (schedules.Count == 0)
            {
                TaskDialog.Show("Export CSV", "No schedules found to export.");
                return Result.Succeeded;
            }

            string outputDir = Path.GetDirectoryName(doc.PathName);
            if (string.IsNullOrEmpty(outputDir))
                outputDir = Path.GetTempPath();

            string exportDir = Path.Combine(outputDir, "STING_Exports");
            Directory.CreateDirectory(exportDir);

            int exported = 0;
            foreach (var schedule in schedules)
            {
                try
                {
                    var options = new ViewScheduleExportOptions
                    {
                        FieldDelimiter = ",",
                        Title = false,
                    };

                    string fileName = SanitizeFileName(schedule.Name) + ".csv";
                    schedule.Export(exportDir, fileName, options);
                    exported++;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Schedule export failed '{schedule.Name}': {ex.Message}");
                }
            }

            TaskDialog.Show("Export CSV",
                $"Exported {exported} of {schedules.Count} schedules to:\n{exportDir}");

            return Result.Succeeded;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Corporate Title Block Schedule — Project Information Dashboard
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a corporate standard "Project Information" schedule collecting
    /// all key project metadata into a single standalone schedule view.
    /// Formatted to East African / ISO corporate practice:
    ///   - Company header area (name, logo placeholder, address)
    ///   - Project identification (name, number, client, address)
    ///   - Discipline summary with element counts
    ///   - Key dates and document control fields
    ///   - ISO 19650 compliance summary
    ///   - Revision history from Revit revision data
    ///
    /// This is a Revit "Sheet List" schedule (BuiltInCategory.OST_Sheets) filtered
    /// to a single summary row, augmented with calculated value columns derived
    /// from Project Information parameters and STING tag data.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CorporateTitleBlockScheduleCommand : IExternalCommand
    {
        // ── Corporate identity defaults (overridable via project_config.json) ──
        private const string CompanyName = "STING BIM Consultants";
        private const string CompanyAddress = "Kampala, Uganda";
        private const string CompanyPhone = "+256 700 000000";

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // ── Check for existing schedule ──
            string scheduleName = "STING - Corporate Project Information";
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(s => s.Name.Equals(scheduleName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                var td0 = new TaskDialog("Corporate Title Block");
                td0.MainInstruction = "Schedule already exists";
                td0.MainContent = $"'{scheduleName}' already exists in the project.";
                td0.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Delete and recreate", "Remove old schedule and create fresh");
                td0.CommonButtons = TaskDialogCommonButtons.Cancel;
                if (td0.Show() != TaskDialogResult.CommandLink1)
                    return Result.Cancelled;
            }

            // ── Gather project data ──
            ProjectInfo pi = doc.ProjectInformation;
            string projName = pi?.Name ?? "Unnamed Project";
            string projNumber = pi?.Number ?? "000";
            string projClient = "";
            string projAddress = "";
            string projStatus = "";
            try
            {
                projClient = pi?.ClientName ?? "";
                projAddress = pi?.Address ?? "";
                projStatus = pi?.Status ?? "";
            }
            catch { }

            // ── STING tag parameters from Project Information ──
            string projLoc = ParameterHelpers.GetString(pi, ParamRegistry.LOC);
            string projZone = ParameterHelpers.GetString(pi, ParamRegistry.ZONE);
            string projRev = ParameterHelpers.GetString(pi, "ASS_REV_TXT");

            // ── Element and discipline statistics ──
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            int totalTaggable = 0, totalTagged = 0;
            var discCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (!known.Contains(cat)) continue;
                totalTaggable++;

                string disc = TagConfig.DiscMap.TryGetValue(cat, out string d) ? d : "G";
                if (!discCounts.ContainsKey(disc)) discCounts[disc] = 0;
                discCounts[disc]++;

                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (TagConfig.TagIsComplete(tag)) totalTagged++;
            }

            double compliancePct = totalTaggable > 0 ? totalTagged * 100.0 / totalTaggable : 0;

            // ── Sheet statistics ──
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .ToList();
            int sheetCount = sheets.Count;

            // ── Revision data ──
            var revisions = new FilteredElementCollector(doc)
                .OfClass(typeof(Revision))
                .Cast<Revision>()
                .OrderBy(r => r.SequenceNumber)
                .ToList();

            // ── Level data ──
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            // ── Build output as a drafting view with text notes ──
            // (Revit schedules cannot mix data types, so we use a drafting view
            // with programmatic TextNote placement for a rich corporate layout)

            using (Transaction tx = new Transaction(doc, "STING Corporate Title Block"))
            {
                tx.Start();

                // Remove old schedule if exists
                if (existing != null)
                {
                    try { doc.Delete(existing.Id); } catch { }
                }

                // Create drafting view
                ViewDrafting draftView = null;
                var draftTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .Where(vft => vft.ViewFamily == ViewFamily.Drafting)
                    .ToList();

                if (draftTypes.Count > 0)
                    draftView = ViewDrafting.Create(doc, draftTypes[0].Id);

                if (draftView == null)
                {
                    tx.RollBack();
                    TaskDialog.Show("Corporate Title Block",
                        "Could not create drafting view. Check that a Drafting view type exists.");
                    return Result.Failed;
                }

                draftView.Name = scheduleName;
                draftView.Scale = 1; // 1:1 for text layout

                // ── Get or create text note type ──
                ElementId headerTypeId = ElementId.InvalidElementId;
                ElementId bodyTypeId = ElementId.InvalidElementId;
                ElementId smallTypeId = ElementId.InvalidElementId;

                var textTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>()
                    .ToList();

                // Find types by size, or use default
                TextNoteType defaultType = textTypes.FirstOrDefault();
                if (defaultType != null)
                {
                    headerTypeId = defaultType.Id;
                    bodyTypeId = defaultType.Id;
                    smallTypeId = defaultType.Id;
                }

                // Try to find specific sizes
                foreach (var tnt in textTypes)
                {
                    double height = tnt.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? 0;
                    double heightMm = height * 304.8;
                    if (heightMm >= 5 && heightMm < 8 && headerTypeId == bodyTypeId)
                        headerTypeId = tnt.Id;
                    if (heightMm >= 2.5 && heightMm < 4)
                        bodyTypeId = tnt.Id;
                    if (heightMm >= 1.5 && heightMm < 2.5)
                        smallTypeId = tnt.Id;
                }

                if (headerTypeId == ElementId.InvalidElementId)
                {
                    tx.RollBack();
                    TaskDialog.Show("Corporate Title Block", "No text note types found in project.");
                    return Result.Failed;
                }

                // ── Place text notes in a structured layout ──
                double xPos = 0;
                double yPos = 0;
                double lineSpacing = 8.0 / 304.8; // 8mm in feet
                double sectionGap = 15.0 / 304.8; // 15mm gap between sections
                double headerSpacing = 12.0 / 304.8; // 12mm for headers

                // Helper to place a text note and advance Y
                void PlaceText(string text, ElementId typeId, double spacing)
                {
                    TextNote.Create(doc, draftView.Id, new XYZ(xPos, yPos, 0),
                        text, typeId);
                    yPos -= spacing;
                }

                // ═══ SECTION 1: Company Header ═══
                PlaceText($"══════════════════════════════════════════", headerTypeId, lineSpacing);
                PlaceText($"  {CompanyName}", headerTypeId, lineSpacing);
                PlaceText($"  {CompanyAddress}  |  {CompanyPhone}", bodyTypeId, lineSpacing);
                PlaceText($"══════════════════════════════════════════", headerTypeId, sectionGap);

                // ═══ SECTION 2: Project Identification ═══
                PlaceText("PROJECT INFORMATION", headerTypeId, headerSpacing);
                PlaceText($"  Project Name:     {projName}", bodyTypeId, lineSpacing);
                PlaceText($"  Project Number:   {projNumber}", bodyTypeId, lineSpacing);
                if (!string.IsNullOrEmpty(projClient))
                    PlaceText($"  Client:           {projClient}", bodyTypeId, lineSpacing);
                if (!string.IsNullOrEmpty(projAddress))
                    PlaceText($"  Address:          {projAddress}", bodyTypeId, lineSpacing);
                if (!string.IsNullOrEmpty(projStatus))
                    PlaceText($"  Status:           {projStatus}", bodyTypeId, lineSpacing);
                if (!string.IsNullOrEmpty(projLoc))
                    PlaceText($"  STING Location:   {projLoc}", bodyTypeId, lineSpacing);
                if (!string.IsNullOrEmpty(projZone))
                    PlaceText($"  STING Zone:       {projZone}", bodyTypeId, lineSpacing);
                yPos -= sectionGap;

                // ═══ SECTION 3: Building Summary ═══
                PlaceText("BUILDING SUMMARY", headerTypeId, headerSpacing);
                PlaceText($"  Levels:       {levels.Count}", bodyTypeId, lineSpacing);
                if (levels.Count > 0)
                {
                    PlaceText($"  Lowest:       {levels.First().Name} ({levels.First().Elevation * 0.3048:F1}m)",
                        bodyTypeId, lineSpacing);
                    PlaceText($"  Highest:      {levels.Last().Name} ({levels.Last().Elevation * 0.3048:F1}m)",
                        bodyTypeId, lineSpacing);
                }
                PlaceText($"  Sheets:       {sheetCount}", bodyTypeId, lineSpacing);
                yPos -= sectionGap;

                // ═══ SECTION 4: Discipline Summary ═══
                PlaceText("DISCIPLINE SUMMARY", headerTypeId, headerSpacing);
                PlaceText($"  Total taggable elements: {totalTaggable:N0}", bodyTypeId, lineSpacing);
                PlaceText($"  Tagged (complete):       {totalTagged:N0}", bodyTypeId, lineSpacing);
                PlaceText($"  Compliance:              {compliancePct:F1}%", bodyTypeId, lineSpacing);
                yPos -= lineSpacing / 2;

                foreach (var kvp in discCounts.OrderByDescending(x => x.Value))
                {
                    string discFull = kvp.Key switch
                    {
                        "M" => "Mechanical", "E" => "Electrical", "P" => "Plumbing",
                        "A" => "Architectural", "S" => "Structural", "FP" => "Fire Protection",
                        "LV" => "Low Voltage", "G" => "General", _ => kvp.Key
                    };
                    PlaceText($"    {kvp.Key,-4} {discFull,-18} {kvp.Value,6:N0} elements",
                        smallTypeId, lineSpacing);
                }
                yPos -= sectionGap;

                // ═══ SECTION 5: Revision History ═══
                if (revisions.Count > 0)
                {
                    PlaceText("REVISION HISTORY", headerTypeId, headerSpacing);
                    foreach (var rev in revisions.TakeLast(10))
                    {
                        string revDate = "";
                        try { revDate = rev.RevisionDate; } catch { }
                        PlaceText($"  Rev {rev.SequenceNumber}: {rev.Description}  [{revDate}]",
                            smallTypeId, lineSpacing);
                    }
                    yPos -= sectionGap;
                }

                // ═══ SECTION 6: Document Control ═══
                PlaceText("DOCUMENT CONTROL", headerTypeId, headerSpacing);
                PlaceText($"  Generated:    {DateTime.Now:yyyy-MM-dd HH:mm}", bodyTypeId, lineSpacing);
                PlaceText($"  Tool:         STING Tools v1.0", bodyTypeId, lineSpacing);
                PlaceText($"  Standard:     ISO 19650-2:2018", bodyTypeId, lineSpacing);
                if (!string.IsNullOrEmpty(projRev))
                    PlaceText($"  Revision:     {projRev}", bodyTypeId, lineSpacing);
                PlaceText($"══════════════════════════════════════════", headerTypeId, lineSpacing);

                tx.Commit();
            }
            var report = new StringBuilder();
            report.AppendLine("Corporate Title Block Schedule Created");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  View: {scheduleName}");
            report.AppendLine($"  Project: {projName} ({projNumber})");
            report.AppendLine($"  Elements: {totalTaggable:N0} ({compliancePct:F1}% tagged)");
            report.AppendLine($"  Disciplines: {discCounts.Count}");
            report.AppendLine($"  Sheets: {sheetCount}");
            report.AppendLine($"  Revisions: {revisions.Count}");
            report.AppendLine();
            report.AppendLine("The schedule is a standalone drafting view.");
            report.AppendLine("Place it on a sheet or use it as a reference.");

            TaskDialog.Show("Corporate Title Block", report.ToString());
            StingLog.Info($"Corporate title block created: {totalTaggable} elements, {compliancePct:F1}% compliance");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Drawing Register Schedule — Sheet-level Document Control
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a corporate standard "Drawing Register" schedule that lists
    /// all sheets with document control metadata. Auto-populates from
    /// existing sheet data and STING tag parameters.
    ///
    /// Columns (matching East African / ISO 19650 practice):
    ///   1. DWG NO (Sheet Number)
    ///   2. DRAWING TITLE (Sheet Name)
    ///   3. DISCIPLINE (derived from sheet number prefix)
    ///   4. SCALE (from views on sheet)
    ///   5. PAPER SIZE (A0/A1/A2/A3/A4 from title block)
    ///   6. REVISION (latest revision on sheet)
    ///   7. REV DATE (date of latest revision)
    ///   8. STATUS (For Construction / For Information / Preliminary)
    ///   9. DRAWN BY (from sheet parameter)
    ///  10. CHECKED BY (from sheet parameter)
    ///  11. APPROVED BY (from sheet parameter)
    ///  12. STING TAG (ASS_TAG_1_TXT if bound to sheets)
    ///  13. LOCATION (ASS_LOC_TXT)
    ///  14. REMARKS
    ///
    /// The schedule is a Revit Sheet List (BuiltInCategory.OST_Sheets) with
    /// sorting by discipline prefix then sheet number, grouping by discipline,
    /// and header formatting matching corporate standards.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DrawingRegisterScheduleCommand : IExternalCommand
    {
        // ── Discipline prefix mappings (sheet number prefix → discipline name) ──
        private static readonly Dictionary<string, string> SheetPrefixToDisc =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "M", "MECHANICAL" }, { "ME", "MECHANICAL" },
            { "E", "ELECTRICAL" }, { "EL", "ELECTRICAL" },
            { "P", "PLUMBING" }, { "PL", "PLUMBING" },
            { "FP", "FIRE PROTECTION" }, { "FA", "FIRE ALARM" },
            { "A", "ARCHITECTURAL" }, { "AR", "ARCHITECTURAL" },
            { "S", "STRUCTURAL" }, { "ST", "STRUCTURAL" },
            { "L", "LANDSCAPE" }, { "LV", "LOW VOLTAGE" },
            { "G", "GENERAL" }, { "C", "CIVIL" },
            { "ID", "INTERIOR DESIGN" },
        };

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            string scheduleName = "STING - Drawing Register";

            // Check for existing
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(s => s.Name.Equals(scheduleName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                var td0 = new TaskDialog("Drawing Register");
                td0.MainInstruction = "Schedule already exists";
                td0.MainContent = $"'{scheduleName}' already exists.\nRecreate it?";
                td0.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Delete and recreate", "Remove old schedule and create fresh");
                td0.CommonButtons = TaskDialogCommonButtons.Cancel;
                if (td0.Show() != TaskDialogResult.CommandLink1)
                    return Result.Cancelled;
            }

            // ── Collect sheet data ──
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .OrderBy(s => s.SheetNumber)
                .ToList();

            if (sheets.Count == 0)
            {
                TaskDialog.Show("Drawing Register", "No sheets found in the project.");
                return Result.Succeeded;
            }

            // ── Project metadata ──
            string projName = doc.ProjectInformation?.Name ?? "Unnamed Project";
            string projNumber = doc.ProjectInformation?.Number ?? "000";

            // ── Build register data ──
            var registerRows = new List<RegisterRow>();

            foreach (ViewSheet sheet in sheets)
            {
                var row = new RegisterRow
                {
                    SheetNumber = sheet.SheetNumber,
                    SheetName = sheet.Name,
                };

                // Derive discipline from sheet number prefix
                row.Discipline = DeriveDiscipline(sheet.SheetNumber);

                // Scale — from first view on sheet
                try
                {
                    var viewIds = sheet.GetAllPlacedViews();
                    if (viewIds.Count > 0)
                    {
                        View firstView = doc.GetElement(viewIds.First()) as View;
                        if (firstView != null)
                        {
                            int scale = firstView.Scale;
                            row.Scale = scale > 0 ? $"1:{scale}" : "NTS";
                        }
                    }
                }
                catch { row.Scale = "NTS"; }

                // Paper size — from title block family instance dimensions
                try
                {
                    var titleBlocks = new FilteredElementCollector(doc, sheet.Id)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .WhereElementIsNotElementType()
                        .FirstOrDefault();

                    if (titleBlocks != null)
                    {
                        double width = titleBlocks.get_Parameter(BuiltInParameter.SHEET_WIDTH)?.AsDouble() ?? 0;
                        double height = titleBlocks.get_Parameter(BuiltInParameter.SHEET_HEIGHT)?.AsDouble() ?? 0;
                        double widthMm = width * 304.8;
                        double heightMm = height * 304.8;
                        row.PaperSize = ClassifyPaperSize(widthMm, heightMm);
                    }
                }
                catch { row.PaperSize = "A1"; }

                // Revision
                var revIds = sheet.GetAllRevisionIds();
                if (revIds.Count > 0)
                {
                    try
                    {
                        Revision latestRev = doc.GetElement(revIds.Last()) as Revision;
                        if (latestRev != null)
                        {
                            row.Revision = $"Rev {latestRev.SequenceNumber}";
                            row.RevDate = latestRev.RevisionDate;
                            row.Status = latestRev.Description?.Contains("Construction") == true
                                ? "FOR CONSTRUCTION"
                                : latestRev.Description?.Contains("Information") == true
                                    ? "FOR INFORMATION" : "PRELIMINARY";
                        }
                    }
                    catch { }
                }

                if (string.IsNullOrEmpty(row.Status)) row.Status = "PRELIMINARY";
                if (string.IsNullOrEmpty(row.PaperSize)) row.PaperSize = "A1";
                if (string.IsNullOrEmpty(row.Scale)) row.Scale = "NTS";
                if (string.IsNullOrEmpty(row.Revision)) row.Revision = "-";
                if (string.IsNullOrEmpty(row.RevDate)) row.RevDate = "-";

                // Sheet parameters
                row.DrawnBy = sheet.get_Parameter(BuiltInParameter.SHEET_DRAWN_BY)?.AsString() ?? "";
                row.CheckedBy = sheet.get_Parameter(BuiltInParameter.SHEET_CHECKED_BY)?.AsString() ?? "";
                row.ApprovedBy = sheet.get_Parameter(BuiltInParameter.SHEET_APPROVED_BY)?.AsString() ?? "";

                // STING tag parameters (if bound to sheets)
                row.StingTag = ParameterHelpers.GetString(sheet, ParamRegistry.TAG1);
                row.Location = ParameterHelpers.GetString(sheet, ParamRegistry.LOC);

                registerRows.Add(row);
            }

            // ── Create drafting view with formatted text layout ──
            using (Transaction tx = new Transaction(doc, "STING Drawing Register"))
            {
                tx.Start();

                // Remove existing if applicable
                if (existing != null)
                {
                    try { doc.Delete(existing.Id); } catch { }
                }

                // Create drafting view
                ViewDrafting draftView = null;
                var draftTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .Where(vft => vft.ViewFamily == ViewFamily.Drafting)
                    .ToList();

                if (draftTypes.Count > 0)
                    draftView = ViewDrafting.Create(doc, draftTypes[0].Id);

                if (draftView == null)
                {
                    tx.RollBack();
                    TaskDialog.Show("Drawing Register",
                        "Could not create drafting view.");
                    return Result.Failed;
                }

                draftView.Name = scheduleName;
                draftView.Scale = 1;

                // Get text note type
                var textType = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>()
                    .FirstOrDefault();

                if (textType == null)
                {
                    tx.RollBack();
                    TaskDialog.Show("Drawing Register", "No text note types found.");
                    return Result.Failed;
                }

                ElementId typeId = textType.Id;
                double y = 0;
                double lineH = 6.0 / 304.8; // 6mm line spacing
                double gapH = 12.0 / 304.8;

                void PlaceLine(string text)
                {
                    TextNote.Create(doc, draftView.Id, new XYZ(0, y, 0), text, typeId);
                    y -= lineH;
                }

                // ═══ Header ═══
                PlaceLine("════════════════════════════════════════════════════════════════════════════════════════════════════");
                PlaceLine($"  DRAWING REGISTER — {projName} ({projNumber})");
                PlaceLine($"  Date: {DateTime.Now:yyyy-MM-dd}  |  Standard: ISO 19650  |  Total Sheets: {sheets.Count}");
                PlaceLine("════════════════════════════════════════════════════════════════════════════════════════════════════");
                y -= lineH;

                // ═══ Column headers ═══
                PlaceLine(FormatRegisterRow("DWG NO", "DRAWING TITLE", "DISC", "SCALE",
                    "SIZE", "REV", "REV DATE", "STATUS", "DRAWN", "CHK'D", "APP'D"));
                PlaceLine("────────────────────────────────────────────────────────────────────────────────────────────────────");

                // ═══ Data rows grouped by discipline ═══
                string lastDisc = "";
                int rowCount = 0;
                foreach (var row in registerRows.OrderBy(r => r.Discipline).ThenBy(r => r.SheetNumber))
                {
                    if (row.Discipline != lastDisc)
                    {
                        if (!string.IsNullOrEmpty(lastDisc))
                            y -= lineH / 2; // gap between groups
                        PlaceLine($"  ── {row.Discipline} ──");
                        lastDisc = row.Discipline;
                    }

                    PlaceLine(FormatRegisterRow(
                        row.SheetNumber, Truncate(row.SheetName, 30), row.Discipline.Substring(0, Math.Min(4, row.Discipline.Length)),
                        row.Scale, row.PaperSize, row.Revision, row.RevDate,
                        Truncate(row.Status, 12), Truncate(row.DrawnBy, 6),
                        Truncate(row.CheckedBy, 6), Truncate(row.ApprovedBy, 6)));
                    rowCount++;
                }

                PlaceLine("────────────────────────────────────────────────────────────────────────────────────────────────────");
                PlaceLine($"  TOTAL: {rowCount} DRAWINGS");

                // ═══ STING tag summary (if tags exist on any sheets) ═══
                var taggedSheets = registerRows.Where(r => !string.IsNullOrEmpty(r.StingTag)).ToList();
                if (taggedSheets.Count > 0)
                {
                    y -= gapH;
                    PlaceLine("  STING TAG REFERENCES:");
                    foreach (var row in taggedSheets.Take(20))
                    {
                        string locInfo = !string.IsNullOrEmpty(row.Location) ? $" [{row.Location}]" : "";
                        PlaceLine($"    {row.SheetNumber}: {row.StingTag}{locInfo}");
                    }
                    if (taggedSheets.Count > 20)
                        PlaceLine($"    ... and {taggedSheets.Count - 20} more");
                }

                PlaceLine("════════════════════════════════════════════════════════════════════════════════════════════════════");

                tx.Commit();
            }
            // ── Also export as CSV ──
            string csvPath = null;
            try
            {
                var csv = new StringBuilder();
                csv.AppendLine("DWG_No,Drawing_Title,Discipline,Scale,Paper_Size,Revision,Rev_Date,Status,Drawn_By,Checked_By,Approved_By,STING_Tag,Location");

                foreach (var row in registerRows.OrderBy(r => r.Discipline).ThenBy(r => r.SheetNumber))
                {
                    csv.AppendLine($"\"{row.SheetNumber}\",\"{row.SheetName}\",\"{row.Discipline}\"," +
                        $"\"{row.Scale}\",\"{row.PaperSize}\",\"{row.Revision}\",\"{row.RevDate}\"," +
                        $"\"{row.Status}\",\"{row.DrawnBy}\",\"{row.CheckedBy}\",\"{row.ApprovedBy}\"," +
                        $"\"{row.StingTag}\",\"{row.Location}\"");
                }

                string dir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(dir)) dir = Path.GetTempPath();
                csvPath = Path.Combine(dir, $"STING_DrawingRegister_{DateTime.Now:yyyyMMdd}.csv");
                File.WriteAllText(csvPath, csv.ToString());
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Drawing register CSV export failed: {ex.Message}");
            }

            // ── Report ──
            var report = new StringBuilder();
            report.AppendLine("Drawing Register Created");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  View: {scheduleName}");
            report.AppendLine($"  Sheets: {sheets.Count}");

            // Discipline breakdown
            var discGroups = registerRows.GroupBy(r => r.Discipline)
                .OrderByDescending(g => g.Count());
            foreach (var g in discGroups)
                report.AppendLine($"    {g.Key,-20} {g.Count(),4} sheets");

            report.AppendLine();
            var statusGroups = registerRows.GroupBy(r => r.Status);
            foreach (var g in statusGroups)
                report.AppendLine($"  {g.Key}: {g.Count()}");

            if (!string.IsNullOrEmpty(csvPath))
                report.AppendLine($"\n  CSV exported: {csvPath}");

            TaskDialog.Show("Drawing Register", report.ToString());
            StingLog.Info($"Drawing register created: {sheets.Count} sheets, CSV exported to {csvPath}");
            return Result.Succeeded;
        }

        private static string DeriveDiscipline(string sheetNumber)
        {
            if (string.IsNullOrEmpty(sheetNumber)) return "GENERAL";

            // Try 2-char prefix first, then 1-char
            string prefix2 = sheetNumber.Length >= 2 ? sheetNumber.Substring(0, 2) : "";
            string prefix1 = sheetNumber.Substring(0, 1);

            // Skip digits in prefix
            string letters = new string(sheetNumber.TakeWhile(c => char.IsLetter(c)).ToArray());
            if (!string.IsNullOrEmpty(letters))
            {
                if (SheetPrefixToDisc.TryGetValue(letters, out string disc2))
                    return disc2;
            }

            if (SheetPrefixToDisc.TryGetValue(prefix2, out string disc))
                return disc;
            if (SheetPrefixToDisc.TryGetValue(prefix1, out disc))
                return disc;
            return "GENERAL";
        }

        private static string ClassifyPaperSize(double widthMm, double heightMm)
        {
            double maxDim = Math.Max(widthMm, heightMm);
            double minDim = Math.Min(widthMm, heightMm);

            // ISO 216 sizes with 5% tolerance
            if (maxDim > 1120 && maxDim < 1240 && minDim > 810 && minDim < 900) return "A0";
            if (maxDim > 790 && maxDim < 880 && minDim > 560 && minDim < 630) return "A1";
            if (maxDim > 560 && maxDim < 630 && minDim > 395 && minDim < 445) return "A2";
            if (maxDim > 395 && maxDim < 445 && minDim > 278 && minDim < 315) return "A3";
            if (maxDim > 278 && maxDim < 315 && minDim > 195 && minDim < 222) return "A4";
            return "A1"; // Default assumption
        }

        private static string FormatRegisterRow(string dwgNo, string title, string disc,
            string scale, string size, string rev, string revDate,
            string status, string drawn, string chkd, string appd)
        {
            return $"  {dwgNo,-12} {title,-30} {disc,-5} {scale,-8} {size,-4} {rev,-6} " +
                   $"{revDate,-12} {status,-14} {drawn,-6} {chkd,-6} {appd,-6}";
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= maxLen ? s : s.Substring(0, maxLen);
        }

        private class RegisterRow
        {
            public string SheetNumber = "", SheetName = "", Discipline = "";
            public string Scale = "", PaperSize = "", Revision = "", RevDate = "";
            public string Status = "", DrawnBy = "", CheckedBy = "", ApprovedBy = "";
            public string StingTag = "", Location = "";
        }
    }
}
