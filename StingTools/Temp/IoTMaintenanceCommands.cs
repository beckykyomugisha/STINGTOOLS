// IoTMaintenanceCommands.cs — IoT, Maintenance & Facility Management commands
// Covers gaps: Asset condition tracking, maintenance scheduling, digital twin sync,
// energy analysis, commissioning checklists, space management, lifecycle costing,
// sensor data integration, warranty tracking, handover package
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using Autodesk.Revit.DB.Architecture;

namespace StingTools.Temp
{
    // ════════════════════════════════════════════════════════════════
    //  COMMAND 1: Asset Condition Assessment
    // ════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AssetConditionCommand : IExternalCommand
    {
        // Condition ratings per ISO 15686 / RICS
        internal static readonly string[] ConditionRatings = { "A - Good", "B - Satisfactory", "C - Poor", "D - Bad", "E - Urgent" };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var uidoc = _ctx.UIDoc;
                var doc = _ctx.Doc;

                // Get all equipment for condition assessment
                var equipmentCategories = new[]
                {
                    BuiltInCategory.OST_MechanicalEquipment,
                    BuiltInCategory.OST_ElectricalEquipment,
                    BuiltInCategory.OST_PlumbingFixtures,
                    BuiltInCategory.OST_LightingFixtures,
                    BuiltInCategory.OST_Sprinklers
                };

                var allEquipment = new List<Element>();
                foreach (var cat in equipmentCategories)
                {
                    allEquipment.AddRange(new FilteredElementCollector(doc)
                        .OfCategory(cat).WhereElementIsNotElementType().ToList());
                }

                var report = new System.Text.StringBuilder();
                report.AppendLine("═══ ASSET CONDITION ASSESSMENT ═══\n");
                report.AppendLine($"Total assets: {allEquipment.Count}\n");

                // Categorize by existing condition data
                var conditionGroups = new Dictionary<string, int>();
                int noCondition = 0;

                foreach (var el in allEquipment)
                {
                    string condition = ParameterHelpers.GetString(el, "ASS_CONDITION_TXT");
                    if (string.IsNullOrEmpty(condition))
                    {
                        noCondition++;
                        continue;
                    }
                    conditionGroups.TryGetValue(condition, out int cg);
                    conditionGroups[condition] = cg + 1;
                }

                report.AppendLine("── CONDITION SUMMARY ──");
                foreach (var rating in ConditionRatings)
                {
                    int count = conditionGroups.GetValueOrDefault(rating, 0);
                    report.AppendLine($"  {rating}: {count}");
                }
                report.AppendLine($"  Not assessed: {noCondition}");

                // Unassessed assets are reported, not rated. This used to write "A - Good" and
                // today's date onto every one — a condition survey nobody carried out, which
                // then read as a fully assessed estate (KUT deep review MEP-14).
                if (noCondition > 0)
                    report.AppendLine($"\n{noCondition} asset(s) have no condition recorded. Record ASS_CONDITION_TXT " +
                                      "(and ASS_CONDITION_DATE_TXT) from a condition survey; nothing is assumed.");

                TaskDialog.Show("Asset Condition", report.ToString());
                StingLog.Info($"Asset condition: {allEquipment.Count} assets assessed");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Asset condition failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMAND 2: Maintenance Schedule Generator
    // ════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MaintenanceScheduleCommand : IExternalCommand
    {
        // KUT deep review MEP-14: the per-category default intervals (6 / 12 / 24 months) and
        // "next due = today + interval" are gone. Interval = ASS_MAINTENANCE_FREQUENCY_MONTHS,
        // else MNT_SERVICE_INTERVAL_TXT; next due = MNT_LAST_SERVICE_DATE_TXT + interval.

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
                var report = new System.Text.StringBuilder();
                report.AppendLine("═══ MAINTENANCE SCHEDULE ═══\n");

                var csvLines = new List<string> { "AssetTag,Category,Family,Room,Interval_Months,LastService,NextDue,Condition,Missing" };
                int noInterval = 0, noLast = 0, wroteNext = 0;

                var categories = new[]
                {
                    BuiltInCategory.OST_MechanicalEquipment,
                    BuiltInCategory.OST_ElectricalEquipment,
                    BuiltInCategory.OST_PlumbingFixtures,
                    BuiltInCategory.OST_LightingFixtures,
                    BuiltInCategory.OST_Sprinklers,
                };

                int totalAssets = 0;
                using (var t = new Transaction(doc, "STING Maintenance Schedule"))
                {
                    t.Start();
                    foreach (var cat in categories)
                    {
                        var elems = new FilteredElementCollector(doc)
                            .OfCategory(cat).WhereElementIsNotElementType().ToList();

                        string catName = elems.FirstOrDefault()?.Category?.Name ?? "Unknown";
                        report.AppendLine($"── {catName} ({elems.Count}) ──");

                        foreach (var el in elems)
                        {
                            string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                            string family = ParameterHelpers.GetFamilyName(el);
                            string room = "";
                            var roomEl = ParameterHelpers.GetRoomAtElement(doc, el);
                            if (roomEl != null)
                                room = roomEl.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";

                            string condition = ParameterHelpers.GetString(el, "ASS_CONDITION_TXT");

                            int? interval = ServiceDue.IntervalMonths(
                                ParameterHelpers.GetValueText(el, "ASS_MAINTENANCE_FREQUENCY_MONTHS"),
                                ParameterHelpers.GetValueText(el, "MNT_SERVICE_INTERVAL_TXT"));
                            string last = ParameterHelpers.GetValueText(el, "MNT_LAST_SERVICE_DATE_TXT");
                            string nextDue = ServiceDue.NextDue(last, interval);
                            var missing = new List<string>();
                            if (interval == null) { noInterval++; missing.Add("interval"); }
                            else ParameterHelpers.SetString(el, "ASS_MAINT_INTERVAL_TXT", $"{interval} months", false);
                            if (string.IsNullOrEmpty(nextDue)) { if (interval != null) { noLast++; missing.Add("last service date"); } }
                            else if (ParameterHelpers.SetString(el, "ASS_MAINT_NEXT_TXT", nextDue, false)) wroteNext++;

                            csvLines.Add($"{tag},{catName},{family},{room},{interval?.ToString() ?? ""},{last},{nextDue},{condition},{string.Join("; ", missing)}");
                            totalAssets++;
                        }
                    }
                    t.Commit();
                }

                string folder = OutputLocationHelper.GetRoutedDirectory(doc, "Maintenance");
                string csvPath = Path.Combine(folder, "STING_MaintenanceSchedule.csv");
                File.WriteAllLines(csvPath, csvLines);

                report.AppendLine($"\nTotal: {totalAssets} assets examined; next-due written for {wroteNext}.");
                report.AppendLine($"MISSING maintenance interval: {noInterval} (record ASS_MAINTENANCE_FREQUENCY_MONTHS).");
                report.AppendLine($"MISSING last service date: {noLast} (record MNT_LAST_SERVICE_DATE_TXT).");
                report.AppendLine("No interval or date is assumed.");
                report.AppendLine($"CSV: {csvPath}");

                TaskDialog.Show("Maintenance Schedule", report.ToString());
                StingLog.Info($"Maintenance schedule: {totalAssets} assets");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Maintenance schedule failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMAND 3: Digital Twin Data Export
    // ════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DigitalTwinExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
                string folder = OutputLocationHelper.GetRoutedDirectory(doc, "Handover");
                string twinFolder = Path.Combine(folder, "STING_DigitalTwin");
                Directory.CreateDirectory(twinFolder);

                var report = new System.Text.StringBuilder();
                report.AppendLine("═══ DIGITAL TWIN DATA EXPORT ═══\n");

                // Export spatial data
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Autodesk.Revit.DB.Architecture.Room>()
                    .Where(r => r.Area > 0)
                    .ToList();

                var spatialJson = new List<string> { "[" };
                foreach (var room in rooms)
                {
                    string name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                    string number = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
                    double area = room.Area * 0.092903;
                    string levelName = doc.GetElement(room.LevelId)?.Name ?? "";
                    spatialJson.Add($"  {{\"id\":{room.Id.Value},\"name\":\"{EscapeJson(name)}\",\"number\":\"{number}\",\"area\":{area:F2},\"level\":\"{EscapeJson(levelName)}\"}},");
                }
                if (spatialJson.Count > 1) spatialJson[spatialJson.Count - 1] = spatialJson.Last().TrimEnd(',');
                spatialJson.Add("]");
                File.WriteAllLines(Path.Combine(twinFolder, "spaces.json"), spatialJson);
                report.AppendLine($"Spaces: {rooms.Count} exported");

                // Export assets
                var assetCategories = new[] {
                    BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_ElectricalEquipment,
                    BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_Sprinklers
                };

                var assetJson = new List<string> { "[" };
                int assetCount = 0;
                foreach (var cat in assetCategories)
                {
                    var elems = new FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType().ToList();
                    foreach (var el in elems)
                    {
                        string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                        string catName = global::StingTools.Core.ParameterHelpers.GetCategoryName(el);
                        string family = ParameterHelpers.GetFamilyName(el);
                        string condition = ParameterHelpers.GetString(el, "ASS_CONDITION_TXT");
                        var loc = (el.Location as LocationPoint)?.Point;
                        string xyz = loc != null ? $"[{loc.X * 0.3048:F2},{loc.Y * 0.3048:F2},{loc.Z * 0.3048:F2}]" : "null";
                        assetJson.Add($"  {{\"id\":{el.Id.Value},\"tag\":\"{EscapeJson(tag)}\",\"category\":\"{EscapeJson(catName)}\",\"family\":\"{EscapeJson(family)}\",\"condition\":\"{EscapeJson(condition)}\",\"position\":{xyz}}},");
                        assetCount++;
                    }
                }
                if (assetJson.Count > 1) assetJson[assetJson.Count - 1] = assetJson.Last().TrimEnd(',');
                assetJson.Add("]");
                File.WriteAllLines(Path.Combine(twinFolder, "assets.json"), assetJson);
                report.AppendLine($"Assets: {assetCount} exported");

                // Export levels
                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
                var levelJson = new List<string> { "[" };
                foreach (var lvl in levels)
                    levelJson.Add($"  {{\"id\":{lvl.Id.Value},\"name\":\"{EscapeJson(lvl.Name)}\",\"elevation\":{lvl.Elevation * 0.3048:F2}}},");
                if (levelJson.Count > 1) levelJson[levelJson.Count - 1] = levelJson.Last().TrimEnd(',');
                levelJson.Add("]");
                File.WriteAllLines(Path.Combine(twinFolder, "levels.json"), levelJson);
                report.AppendLine($"Levels: {levels.Count} exported");

                report.AppendLine($"\nOutput: {twinFolder}");

                TaskDialog.Show("Digital Twin Export", report.ToString());
                StingLog.Info($"Digital twin: exported to {twinFolder}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Digital twin export failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private string EscapeJson(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMAND 4: Energy Analysis Summary
    // ════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class EnergyAnalysisCommand : IExternalCommand
    {
        internal static readonly Dictionary<string, (double Heating, double Cooling)> LoadFactors = new()
        {
            ["Office"] = (70, 80), ["Meeting"] = (80, 100), ["Corridor"] = (40, 30),
            ["WC"] = (50, 0), ["Kitchen"] = (60, 120), ["Server"] = (30, 300),
            ["Reception"] = (70, 70), ["Plant"] = (30, 50), ["Default"] = (65, 65),
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
                var report = new System.Text.StringBuilder();
                report.AppendLine("═══ ENERGY ANALYSIS SUMMARY ═══\n");

                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
                    .Cast<Autodesk.Revit.DB.Architecture.Room>().Where(r => r.Area > 0).ToList();

                double totalArea = 0, totalHeating = 0, totalCooling = 0;

                foreach (var room in rooms)
                {
                    double areaSqM = room.Area * 0.092903;
                    string name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "Default";
                    string matchedType = LoadFactors.Keys.FirstOrDefault(k => k != "Default" && name.ToLower().Contains(k.ToLower())) ?? "Default";
                    var (heat, cool) = LoadFactors[matchedType];
                    totalArea += areaSqM;
                    totalHeating += areaSqM * heat;
                    totalCooling += areaSqM * cool;
                }

                report.AppendLine($"Total area: {totalArea:F0} m²");
                report.AppendLine($"Heating load: {totalHeating / 1000:F1} kW ({(totalArea > 0 ? totalHeating / totalArea : 0):F1} W/m²)");
                report.AppendLine($"Cooling load: {totalCooling / 1000:F1} kW ({(totalArea > 0 ? totalCooling / totalArea : 0):F1} W/m²)");
                report.AppendLine("\nNote: Estimates based on CIBSE Guide A. Detailed simulation recommended.");

                TaskDialog.Show("Energy Analysis", report.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Energy analysis failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMAND 5: Commissioning Checklist
    // ════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CommissioningChecklistCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
                var csvLines = new List<string> { "System,AssetTag,Category,Family,CheckItem,Status" };

                var systemChecks = new Dictionary<string, string[]>
                {
                    ["HVAC"] = new[] { "Air flow rate", "Temperature differential", "Noise level", "Controls operation", "BMS verification" },
                    ["Electrical"] = new[] { "Insulation resistance", "Earth loop impedance", "RCD trip test", "Phase rotation", "Emergency lighting" },
                    ["Plumbing"] = new[] { "Flow rate test", "Pressure test", "Temperature check", "Backflow prevention", "Legionella compliance" },
                    ["Fire"] = new[] { "Detector test", "Alarm sounder", "Cause & effect", "Sprinkler flow", "Panel programming" },
                };

                int totalChecks = 0;
                using (var t = new Transaction(doc, "STING Commissioning"))
                {
                    t.Start();

                    var catChecks = new (BuiltInCategory Cat, string System)[]
                    {
                        (BuiltInCategory.OST_MechanicalEquipment, "HVAC"),
                        (BuiltInCategory.OST_ElectricalEquipment, "Electrical"),
                        (BuiltInCategory.OST_PlumbingFixtures, "Plumbing"),
                        (BuiltInCategory.OST_Sprinklers, "Fire"),
                    };

                    foreach (var (cat, sys) in catChecks)
                    {
                        var elems = new FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType().ToList();
                        foreach (var el in elems)
                        {
                            string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                            string family = ParameterHelpers.GetFamilyName(el);
                            foreach (var check in systemChecks[sys])
                            {
                                csvLines.Add($"{sys},{tag},{(global::StingTools.Core.ParameterHelpers.GetCategoryName(el))},{family},{check},PENDING");
                                totalChecks++;
                            }
                            ParameterHelpers.SetString(el, "COM_COMMISSION_STATUS_TXT", "PENDING", false);
                        }
                    }
                    t.Commit();
                }

                string folder = OutputLocationHelper.GetRoutedDirectory(doc, "Handover");
                string csvPath = Path.Combine(folder, "STING_CommissioningChecklist.csv");
                File.WriteAllLines(csvPath, csvLines);

                TaskDialog.Show("Commissioning Checklist", $"Generated {totalChecks} checks.\nCSV: {csvPath}");
                StingLog.Info($"Commissioning: {totalChecks} checks");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Commissioning checklist failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMAND 6: Space Management Analysis
    // ════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SpaceManagementCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
                    .Cast<Autodesk.Revit.DB.Architecture.Room>().ToList();

                var placed = rooms.Where(r => r.Area > 0).ToList();
                var report = new System.Text.StringBuilder();
                report.AppendLine("═══ SPACE MANAGEMENT ═══\n");
                report.AppendLine($"Rooms: {rooms.Count} total ({placed.Count} placed)\n");

                double totalArea = 0;
                var byLevel = placed.GroupBy(r => doc.GetElement(r.LevelId)?.Name ?? "Unknown").OrderBy(g => g.Key);
                foreach (var group in byLevel)
                {
                    double levelArea = group.Sum(r => r.Area * 0.092903);
                    totalArea += levelArea;
                    report.AppendLine($"  {group.Key}: {group.Count()} rooms, {levelArea:F0} m²");
                }
                report.AppendLine($"\nTotal usable area: {totalArea:F0} m²");

                TaskDialog.Show("Space Management", report.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Space management failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMAND 7: Lifecycle Cost Estimator
    // ════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LifecycleCostCommand : IExternalCommand
    {
        internal static readonly Dictionary<string, (double CostPer, int LifeYears)> LifecycleData = new()
        {
            ["Mechanical Equipment"] = (5000, 20), ["Electrical Equipment"] = (3000, 25),
            ["Plumbing Fixtures"] = (800, 20), ["Lighting Fixtures"] = (200, 15),
            ["Sprinklers"] = (150, 25), ["Doors"] = (1200, 30), ["Windows"] = (2000, 30),
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
                var report = new System.Text.StringBuilder();
                report.AppendLine("═══ LIFECYCLE COST ESTIMATE (60yr) ═══\n");

                double totalCapital = 0, totalReplacement = 0, totalMaintenance = 0;
                int period = 60;

                var catMap = new Dictionary<BuiltInCategory, string>
                {
                    [BuiltInCategory.OST_MechanicalEquipment] = "Mechanical Equipment",
                    [BuiltInCategory.OST_ElectricalEquipment] = "Electrical Equipment",
                    [BuiltInCategory.OST_PlumbingFixtures] = "Plumbing Fixtures",
                    [BuiltInCategory.OST_LightingFixtures] = "Lighting Fixtures",
                    [BuiltInCategory.OST_Sprinklers] = "Sprinklers",
                    [BuiltInCategory.OST_Doors] = "Doors",
                    [BuiltInCategory.OST_Windows] = "Windows",
                };

                foreach (var (cat, name) in catMap)
                {
                    int count = new FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType().GetElementCount();
                    if (count == 0) continue;
                    var (costPer, lifeYears) = LifecycleData.GetValueOrDefault(name, (1000, 25));
                    double capital = count * costPer;
                    int replacements = Math.Max(0, (period / lifeYears) - 1);
                    double replCost = count * costPer * replacements;
                    double maint = capital * 0.03 * period;
                    totalCapital += capital; totalReplacement += replCost; totalMaintenance += maint;
                    report.AppendLine($"  {name}: {count} × £{costPer:N0} = £{capital:N0} (replace {replacements}×)");
                }

                double total = totalCapital + totalReplacement + totalMaintenance;
                report.AppendLine($"\nCapital: £{totalCapital:N0} | Replacement: £{totalReplacement:N0} | Maintenance: £{totalMaintenance:N0}");
                report.AppendLine($"TOTAL WLC: £{total:N0}");

                TaskDialog.Show("Lifecycle Cost", report.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Lifecycle cost failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMAND 8: Warranty Tracker
    // ════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WarrantyTrackerCommand : IExternalCommand
    {
        // KUT deep review MEP-3: the per-category default periods (5/3/2/5/10 years) and
        // "today" as the start are gone. An expiry is computed only from a start date and a
        // duration recorded on the element (WarrantyExpiry.Plan); anything else is reported.

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
                var csvLines = new List<string> { "AssetTag,ElementId,Category,Family,WarrantyStart,Duration,ExpiryDate,State,Status,Detail" };
                var counts = new Dictionary<WarrantyState, int>();
                int written = 0, expired = 0, total = 0;
                DateTime today = DateTime.Today;

                using (var t = new Transaction(doc, "STING Warranty Tracker"))
                {
                    t.Start();
                    var catMap = new Dictionary<BuiltInCategory, string>
                    {
                        [BuiltInCategory.OST_MechanicalEquipment] = "Mechanical Equipment",
                        [BuiltInCategory.OST_ElectricalEquipment] = "Electrical Equipment",
                        [BuiltInCategory.OST_PlumbingFixtures] = "Plumbing Fixtures",
                        [BuiltInCategory.OST_LightingFixtures] = "Lighting Fixtures",
                        [BuiltInCategory.OST_Sprinklers] = "Sprinklers",
                    };

                    foreach (var (cat, name) in catMap)
                    {
                        var elems = new FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType().ToList();
                        foreach (var el in elems)
                        {
                            total++;
                            string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                            string start = FirstNonEmpty(el, ParamRegistry.WARRANTY_START, ParamRegistry.INSTALL_DATE, "COM_INSTALL_DATE_TXT");
                            string dur = FirstNonEmpty(el, ParamRegistry.WARR_DUR_PARTS, ParamRegistry.WARR_DUR_LABOR);
                            string unit = ParameterHelpers.GetValueText(el, ParamRegistry.WARR_DUR_UNIT);
                            string rec = ParameterHelpers.GetValueText(el, "MNT_WARRANTY_EXPIRY_TXT");

                            var plan = WarrantyExpiry.Plan(rec, start, dur, unit, today);
                            counts[plan.State] = counts.TryGetValue(plan.State, out int c) ? c + 1 : 1;
                            if (plan.ShouldWrite && ParameterHelpers.SetString(el, "MNT_WARRANTY_EXPIRY_TXT", plan.Expiry, false))
                                written++;
                            if (plan.Expired == true) expired++;

                            string status = plan.Expired == null ? "UNKNOWN" : plan.Expired.Value ? "EXPIRED" : "ACTIVE";
                            csvLines.Add(string.Join(",", Csv(tag), el.Id.Value, Csv(name), Csv(ParameterHelpers.GetFamilyName(el)),
                                Csv(plan.Start), Csv(dur), Csv(plan.Expiry), plan.State, status, Csv(plan.Detail)));
                        }
                    }
                    t.Commit();
                }

                string folder = OutputLocationHelper.GetRoutedDirectory(doc, "Handover");
                string csvPath = Path.Combine(folder, "STING_WarrantyTracker.csv");
                File.WriteAllLines(csvPath, csvLines);

                int Get(WarrantyState s) => counts.TryGetValue(s, out int n) ? n : 0;
                string summary = total == 0
                    ? "No Mechanical / Electrical / Plumbing / Lighting / Sprinkler elements in the model — nothing assessed."
                    : $"Assets examined: {total}\n" +
                      $"  Expiry already recorded: {Get(WarrantyState.Recorded)}\n" +
                      $"  Expiry computed from start + duration: {Get(WarrantyState.Computed)} ({written} written)\n" +
                      $"  MISSING start / installation date: {Get(WarrantyState.MissingStart)}\n" +
                      $"  MISSING warranty duration: {Get(WarrantyState.MissingDuration)}\n" +
                      $"  Unreadable value: {Get(WarrantyState.Unreadable)}\n" +
                      $"  Expired: {expired}\n\n" +
                      "No date or duration is assumed: record the warranty start (or installation date) and " +
                      "the duration from the O&M data, then re-run.";
                TaskDialog.Show("Warranty Tracker", summary + $"\n\nCSV: {csvPath}");
                StingLog.Info($"Warranty: {total} examined, {written} expiries computed, {Get(WarrantyState.MissingStart) + Get(WarrantyState.MissingDuration)} missing data");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Warranty tracker failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string FirstNonEmpty(Element el, params string[] names)
        {
            foreach (var n in names)
            {
                string v = ParameterHelpers.GetValueText(el, n);
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            return "";
        }

        private static string Csv(string v)
        {
            v ??= "";
            return v.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMAND 9: Handover Package Generator
    // ════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HandoverPackageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
                string folder = OutputLocationHelper.GetRoutedDirectory(doc, "Handover");
                string hFolder = Path.Combine(folder, "STING_Handover");
                Directory.CreateDirectory(hFolder);

                // Asset register
                var csvLines = new List<string> { "Tag,Category,Family,Type,Level,Room,Condition,Warranty" };
                var categories = new[] {
                    BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_ElectricalEquipment,
                    BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_LightingFixtures,
                    BuiltInCategory.OST_Sprinklers, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows
                };

                int count = 0;
                foreach (var cat in categories)
                {
                    foreach (var el in new FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType())
                    {
                        string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                        string catName = global::StingTools.Core.ParameterHelpers.GetCategoryName(el);
                        string family = ParameterHelpers.GetFamilyName(el);
                        string typeName = ParameterHelpers.GetFamilySymbolName(el);
                        string room = "";
                        var roomEl = ParameterHelpers.GetRoomAtElement(doc, el);
                        if (roomEl != null) room = roomEl.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                        string condition = ParameterHelpers.GetString(el, "ASS_CONDITION_TXT");
                        string warranty = ParameterHelpers.GetString(el, "ASS_WARRANTY_TXT");
                        csvLines.Add($"{tag},{catName},{family},{typeName},,{room},{condition},{warranty}");
                        count++;
                    }
                }
                File.WriteAllLines(Path.Combine(hFolder, "AssetRegister.csv"), csvLines);

                // Room schedule
                var roomLines = new List<string> { "Name,Number,Level,Department,Area_m2" };
                var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
                    .Cast<Autodesk.Revit.DB.Architecture.Room>().Where(r => r.Area > 0).ToList();
                foreach (var r in rooms)
                {
                    string n = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                    string num = r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
                    string lvl = doc.GetElement(r.LevelId)?.Name ?? "";
                    string dept = r.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? "";
                    roomLines.Add($"{n},{num},{lvl},{dept},{r.Area * 0.092903:F1}");
                }
                File.WriteAllLines(Path.Combine(hFolder, "RoomSchedule.csv"), roomLines);

                TaskDialog.Show("Handover Package", $"Assets: {count}\nRooms: {rooms.Count}\nOutput: {hFolder}");
                StingLog.Info($"Handover: {count} assets, {rooms.Count} rooms");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Handover package failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMAND 10: Sensor Point Mapper
    // ════════════════════════════════════════════════════════════════
    // KUT deep review MEP-14: this wrote "BMS/{type}/{tag}" into ASS_BMS_ADDRESS_TXT — a
    // made-up string in the field that holds the real controller address, and the first of
    // three sensor types won on mechanical equipment. It now writes nothing: the CSV lists a
    // PROPOSED point name beside whatever address the element already records.
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SensorPointMapperCommand : IExternalCommand
    {
        internal static readonly Dictionary<string, BuiltInCategory[]> SensorCategories = new()
        {
            ["Temperature"] = new[] { BuiltInCategory.OST_MechanicalEquipment },
            ["Humidity"] = new[] { BuiltInCategory.OST_MechanicalEquipment },
            ["CO2"] = new[] { BuiltInCategory.OST_MechanicalEquipment },
            ["Occupancy"] = new[] { BuiltInCategory.OST_LightingFixtures },
            ["Power"] = new[] { BuiltInCategory.OST_ElectricalEquipment },
            ["Flow"] = new[] { BuiltInCategory.OST_PlumbingFixtures },
            ["Smoke"] = new[] { BuiltInCategory.OST_Sprinklers },
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
                var csvLines = new List<string> { "SensorType,AssetTag,Category,Family,Room,ProposedPointName,RecordedBmsAddress" };
                int sensorCount = 0, withAddress = 0;

                foreach (var (sensorType, cats) in SensorCategories)
                {
                    foreach (var cat in cats)
                    {
                        foreach (var el in new FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType())
                        {
                            string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                            if (string.IsNullOrEmpty(tag)) continue;
                            string family = ParameterHelpers.GetFamilyName(el);
                            string room = "";
                            var roomEl = ParameterHelpers.GetRoomAtElement(doc, el);
                            if (roomEl != null) room = roomEl.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                            string recorded = ParameterHelpers.GetValueText(el, "ASS_BMS_ADDRESS_TXT");
                            if (!string.IsNullOrEmpty(recorded)) withAddress++;
                            csvLines.Add($"{sensorType},{tag},{(global::StingTools.Core.ParameterHelpers.GetCategoryName(el))},{family},{room},BMS/{sensorType}/{tag},{recorded}");
                            sensorCount++;
                        }
                    }
                }

                string folder = OutputLocationHelper.GetRoutedDirectory(doc, "AssetRegister");
                string csvPath = Path.Combine(folder, "STING_SensorPoints.csv");
                File.WriteAllLines(csvPath, csvLines);

                TaskDialog.Show("Sensor Point Mapper",
                    $"Listed {sensorCount} candidate sensor points; {withAddress} already record a BMS address.\n" +
                    "Point names in the CSV are PROPOSALS for the BMS integrator — nothing was written to the model.\n" +
                    $"CSV: {csvPath}");
                StingLog.Info($"Sensor mapper: {sensorCount} points");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Sensor mapper failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
