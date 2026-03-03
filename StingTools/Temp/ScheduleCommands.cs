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
    /// Now uses ALL 15 CSV columns:
    ///   0: Source_File, 1: Discipline, 2: Schedule_Name, 3: Category,
    ///   4: Schedule_Type (Material Takeoff), 5: Multi_Categories,
    ///   6: Fields, 7: Filters, 8: Sorting, 9: Grouping, 10: Totals,
    ///   11: Formulas (field header aliases), 12-14: Header/Text/Background Color (reserved)
    /// Also loads SCHEDULE_FIELD_REMAP.csv for deprecated field name auto-remapping.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchSchedulesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
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

            int created = 0;
            int skipped = 0;
            int remapped = 0;
            int matTakeoffs = 0;
            int formatted = 0;
            var existingNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Select(s => s.Name));

            using (Transaction tx = new Transaction(doc, "STING Batch Create Schedules"))
            {
                tx.Start();

                var lines = File.ReadAllLines(csvPath)
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                    .Skip(1); // skip header row

                foreach (string line in lines)
                {
                    string[] cols = StingToolsApp.ParseCsvLine(line);
                    if (cols.Length < 4) continue;

                    // Parse all 15 columns
                    string name = cols[2].Trim();
                    string category = cols[3].Trim();
                    string scheduleType = cols.Length > 4 ? cols[4].Trim() : "";
                    string multiCats = cols.Length > 5 ? cols[5].Trim() : "";
                    string fieldsSpec = cols.Length > 6 ? cols[6].Trim() : "";
                    string filterSpec = cols.Length > 7 ? cols[7].Trim() : "";
                    string sortSpec = cols.Length > 8 ? cols[8].Trim() : "";
                    string groupSpec = cols.Length > 9 ? cols[9].Trim() : "";
                    string totalSpec = cols.Length > 10 ? cols[10].Trim() : "";
                    string formulaSpec = cols.Length > 11 ? cols[11].Trim() : "";

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

                        // Add fields from CSV column 6, with field remap fallback + formula fallback
                        var addedFieldIds = new Dictionary<string, ScheduleFieldId>(
                            StringComparer.OrdinalIgnoreCase);

                        if (!string.IsNullOrEmpty(fieldsSpec))
                        {
                            remapped += ScheduleHelper.AddFieldsTracked(
                                doc, vs, fieldsSpec, fieldRemaps, formulaMap, addedFieldIds);
                        }

                        // Apply column heading overrides from Formulas column
                        bool didFormat = false;
                        if (formulaMap.Count > 0)
                            didFormat |= ScheduleHelper.ApplyFieldHeaders(vs, formulaMap);

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
            if (formatted > 0)
                report.AppendLine($"Applied formatting (sort/group/totals/filters) to {formatted}.");
            if (remapped > 0)
                report.AppendLine($"Remapped {remapped} deprecated field name(s).");
            report.Append($"Source: MR_SCHEDULES.csv (168 definitions)");

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
        /// Apply column heading overrides from the Formulas map.
        /// Sets each field's ColumnHeading to be the display-friendly name.
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

                    // Check if this field's current heading matches a formula key
                    if (formulaMap.TryGetValue(currentHeading, out string displayName))
                    {
                        field.ColumnHeading = displayName;
                        applied = true;
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
            Document doc = commandData.Application.ActiveUIDocument.Document;

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
            Document doc = commandData.Application.ActiveUIDocument.Document;

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
            Document doc = commandData.Application.ActiveUIDocument.Document;

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
}
