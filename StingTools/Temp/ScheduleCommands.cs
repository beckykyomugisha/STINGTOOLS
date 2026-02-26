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

            int created = 0;
            int skipped = 0;
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

            TaskDialog.Show("Batch Schedules",
                $"Created {created} schedules.\nSkipped {skipped} (exist or failed).\n" +
                $"Scanned {scheduleFiles.Length} definition file(s).");

            return Result.Succeeded;
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
    /// Apply field remaps across categories (42 remaps from the STINGTemp data).
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

            int updated = 0;
            int total = 0;

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

                    // Auto-populate PROD code from category
                    if (TagConfig.ProdMap.TryGetValue(catName, out string prod))
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
                }

                tx.Commit();
            }

            TaskDialog.Show("Auto-Populate",
                $"Auto-populated {updated} field values across {total} elements.");

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
