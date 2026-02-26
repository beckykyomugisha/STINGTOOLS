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
    /// Multi-discipline schedule creation from CSV definition files.
    /// Loads SCHEDULE_FIELD_REMAP.csv to auto-remap deprecated field names.
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

            // Load the specific MR_SCHEDULES.csv definition file
            string csvPath = StingToolsApp.FindDataFile("MR_SCHEDULES.csv");
            if (csvPath == null)
            {
                TaskDialog.Show("Batch Schedules",
                    "MR_SCHEDULES.csv not found in data directory.\n" +
                    $"Searched: {dataDir}");
                return Result.Failed;
            }
            var scheduleFiles = new[] { csvPath };

            // Load deprecated field remaps from SCHEDULE_FIELD_REMAP.csv
            var fieldRemaps = LoadFieldRemaps();
            int remapCount = fieldRemaps.Count;
            if (remapCount > 0)
                StingLog.Info($"Loaded {remapCount} field remaps from SCHEDULE_FIELD_REMAP.csv");

            int created = 0;
            int skipped = 0;
            int remapped = 0;
            var existingNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Select(s => s.Name));

            using (Transaction tx = new Transaction(doc, "Batch Create Schedules"))
            {
                tx.Start();

                foreach (string file in scheduleFiles)
                {
                    // Skip comment line (row 0: "# v2.2 ...") and header (row 1)
                    var lines = File.ReadAllLines(file)
                        .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                        .Skip(1); // skip header row

                    foreach (string line in lines)
                    {
                        // MR_SCHEDULES columns: Source_File(0), Discipline(1),
                        // Schedule_Name(2), Category(3), ...
                        string[] cols = StingToolsApp.ParseCsvLine(line);
                        if (cols.Length < 4) continue;

                        string name = cols[2].Trim();
                        string category = cols[3].Trim();

                        if (string.IsNullOrEmpty(name)) continue;
                        if (existingNames.Contains(name))
                        {
                            skipped++;
                            continue;
                        }

                        // Map category string to BuiltInCategory
                        if (!TryGetCategory(category, out BuiltInCategory bic))
                            continue;

                        try
                        {
                            ElementId catId = new ElementId(bic);
                            ViewSchedule vs = ViewSchedule.CreateSchedule(doc, catId);
                            vs.Name = name;

                            // Add fields from CSV column 6 (Fields)
                            if (cols.Length > 6 && !string.IsNullOrWhiteSpace(cols[6]))
                            {
                                remapped += AddFieldsToSchedule(doc, vs, cols[6].Trim(), fieldRemaps);
                            }

                            created++;
                            existingNames.Add(name);
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Schedule create failed '{name}': {ex.Message}");
                            skipped++;
                        }
                    }
                }

                tx.Commit();
            }

            string report = $"Created {created} schedules.\nSkipped {skipped} (exist or failed).\n" +
                $"Scanned {scheduleFiles.Length} definition file(s).";
            if (remapped > 0)
                report += $"\nRemapped {remapped} deprecated field name(s).";

            TaskDialog.Show("Batch Schedules", report);

            return Result.Succeeded;
        }

        /// <summary>
        /// Load SCHEDULE_FIELD_REMAP.csv — maps deprecated field names to their
        /// consolidated replacements. CSV format:
        ///   Old_Schedule_Field, Consolidated_Parameter, Action, ...
        /// Only rows with Action=REMAPPED are loaded.
        /// </summary>
        private static Dictionary<string, string> LoadFieldRemaps()
        {
            var remaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string path = StingToolsApp.FindDataFile("SCHEDULE_FIELD_REMAP.csv");
            if (path == null) return remaps;

            try
            {
                var lines = File.ReadAllLines(path)
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                    .Skip(1); // skip header

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
        /// Add fields to a schedule from a comma-separated field spec string.
        /// Format: "FieldName1, FieldName2, FieldName3" — matches schedulable field names.
        /// If a field name is found in the remap dictionary, the consolidated name is
        /// tried first (auto-migration of deprecated fields).
        /// </summary>
        /// <returns>Number of fields that were remapped from deprecated names.</returns>
        private static int AddFieldsToSchedule(Document doc, ViewSchedule vs, string fieldSpec,
            Dictionary<string, string> fieldRemaps)
        {
            // Parse field names (may contain "=" for formula remaps like "ASS_ID_TXT=Mark")
            string[] fieldEntries = fieldSpec.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            // Build lookup of available schedulable fields
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
                // Handle remap format: "ASS_ID_TXT=Mark" → use the key before '='
                if (fieldName.Contains("="))
                    fieldName = fieldName.Split('=')[0].Trim();

                if (string.IsNullOrEmpty(fieldName)) continue;

                try
                {
                    // Try the field name as-is first
                    if (fieldLookup.TryGetValue(fieldName, out SchedulableField sf))
                    {
                        vs.Definition.AddField(sf);
                    }
                    // If not found, check if it's a deprecated field with a remap
                    else if (fieldRemaps.TryGetValue(fieldName, out string remappedName)
                        && fieldLookup.TryGetValue(remappedName, out sf))
                    {
                        vs.Definition.AddField(sf);
                        remappedCount++;
                        StingLog.Info($"Schedule field remapped: '{fieldName}' → '{remappedName}'");
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Schedule field add '{fieldName}': {ex.Message}");
                }
            }

            return remappedCount;
        }

        private static bool TryGetCategory(string name, out BuiltInCategory bic)
        {
            bic = BuiltInCategory.INVALID;
            var map = new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
            {
                { "Walls", BuiltInCategory.OST_Walls },
                { "Doors", BuiltInCategory.OST_Doors },
                { "Windows", BuiltInCategory.OST_Windows },
                { "Floors", BuiltInCategory.OST_Floors },
                { "Ceilings", BuiltInCategory.OST_Ceilings },
                { "Roofs", BuiltInCategory.OST_Roofs },
                { "Rooms", BuiltInCategory.OST_Rooms },
                { "Furniture", BuiltInCategory.OST_Furniture },
                { "Mechanical Equipment", BuiltInCategory.OST_MechanicalEquipment },
                { "Electrical Equipment", BuiltInCategory.OST_ElectricalEquipment },
                { "Lighting Fixtures", BuiltInCategory.OST_LightingFixtures },
                { "Plumbing Fixtures", BuiltInCategory.OST_PlumbingFixtures },
                { "Pipes", BuiltInCategory.OST_PipeCurves },
                { "Ducts", BuiltInCategory.OST_DuctCurves },
                { "Conduits", BuiltInCategory.OST_Conduit },
                { "Cable Trays", BuiltInCategory.OST_CableTray },
                { "Structural Columns", BuiltInCategory.OST_StructuralColumns },
                { "Structural Framing", BuiltInCategory.OST_StructuralFraming },
                { "Structural Foundations", BuiltInCategory.OST_StructuralFoundation },
                { "Generic Models", BuiltInCategory.OST_GenericModel },
                { "Air Terminals", BuiltInCategory.OST_DuctTerminal },
                { "Sprinklers", BuiltInCategory.OST_Sprinklers },
                { "Fire Alarm Devices", BuiltInCategory.OST_FireAlarmDevices },
                { "Electrical Fixtures", BuiltInCategory.OST_ElectricalFixtures },
                { "Electrical Circuits", BuiltInCategory.OST_ElectricalCircuit },
                { "Pipe Accessories", BuiltInCategory.OST_PipeAccessory },
                { "Pipe Fittings", BuiltInCategory.OST_PipeFitting },
                { "Stairs", BuiltInCategory.OST_Stairs },
                { "Ramps", BuiltInCategory.OST_Ramps },
                { "Curtain Panels", BuiltInCategory.OST_CurtainWallPanels },
                { "Casework", BuiltInCategory.OST_Casework },
                { "Furniture Systems", BuiltInCategory.OST_FurnitureSystems },
            };

            if (map.TryGetValue(name, out bic))
                return true;

            // Try direct enum parse
            if (name.StartsWith("OST_") && Enum.TryParse(name, out bic))
                return true;

            return false;
        }
    }

    /// <summary>
    /// Ported from STINGTemp 5_Schedules.panel — AutoPopulate.
    /// Enhanced for zero-manual-input: auto-populates ALL 7 token fields including
    /// LOC (from room/project data) and ZONE (from room department/name).
    /// Uses family-aware PROD codes for more specific equipment identification.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoPopulateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            // Collect all elements with ASS_TAG_1_TXT parameter
            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();

            // Build spatial index for LOC/ZONE auto-detection
            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);

            int updated = 0;
            int total = 0;
            int locDetected = 0;
            int zoneDetected = 0;

            using (Transaction tx = new Transaction(doc, "Auto-Populate Fields"))
            {
                tx.Start();

                foreach (Element el in collector)
                {
                    string catName = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(catName) || !TagConfig.DiscMap.ContainsKey(catName))
                        continue;

                    total++;

                    // Auto-populate DISC code from category
                    if (ParameterHelpers.SetIfEmpty(el, "ASS_DISCIPLINE_COD_TXT",
                        TagConfig.DiscMap[catName]))
                        updated++;

                    // Auto-populate PROD code (family-aware: FCU, VAV, AHU, etc.)
                    string prod = TagConfig.GetFamilyAwareProdCode(el, catName);
                    if (!string.IsNullOrEmpty(prod))
                    {
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_PRODCT_COD_TXT", prod))
                            updated++;
                    }

                    // Auto-populate SYS code
                    string sys = TagConfig.GetSysCode(catName);
                    if (!string.IsNullOrEmpty(sys))
                    {
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_SYSTEM_TYPE_TXT", sys))
                            updated++;
                    }

                    // Auto-populate FUNC code
                    string func = TagConfig.GetFuncCode(sys);
                    if (!string.IsNullOrEmpty(func))
                    {
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_FUNC_TXT", func))
                            updated++;
                    }

                    // Auto-populate level code
                    string lvl = ParameterHelpers.GetLevelCode(doc, el);
                    if (lvl != "XX")
                    {
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_LVL_COD_TXT", lvl))
                            updated++;
                    }

                    // Auto-populate LOC from spatial data (room / project info)
                    if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, "ASS_LOC_TXT")))
                    {
                        string loc = SpatialAutoDetect.DetectLoc(doc, el, roomIndex, projectLoc);
                        if (!string.IsNullOrEmpty(loc) && ParameterHelpers.SetIfEmpty(el, "ASS_LOC_TXT", loc))
                        {
                            updated++;
                            locDetected++;
                        }
                    }

                    // Auto-populate ZONE from room data (department, name, number)
                    if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, "ASS_ZONE_TXT")))
                    {
                        string zone = SpatialAutoDetect.DetectZone(doc, el, roomIndex);
                        if (!string.IsNullOrEmpty(zone) && ParameterHelpers.SetIfEmpty(el, "ASS_ZONE_TXT", zone))
                        {
                            updated++;
                            zoneDetected++;
                        }
                    }
                }

                tx.Commit();
            }

            string report = $"Auto-populated {updated} field values across {total} elements.";
            if (locDetected > 0) report += $"\nLOC auto-detected: {locDetected} (from rooms/project)";
            if (zoneDetected > 0) report += $"\nZONE auto-detected: {zoneDetected} (from rooms)";

            TaskDialog.Show("Auto-Populate", report);

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

            // Use project folder or temp
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
