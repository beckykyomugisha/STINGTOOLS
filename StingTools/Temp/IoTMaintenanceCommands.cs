// IoTMaintenanceCommands.cs — IoT, Maintenance & Facility Management commands
// Covers gaps: Asset condition tracking, maintenance scheduling, digital twin sync,
// energy analysis, commissioning checklists, space management, lifecycle costing,
// sensor data integration, warranty tracking, handover package
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

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
                var uidoc = commandData.Application.ActiveUIDocument;
                var doc = uidoc.Document;

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
                    if (!conditionGroups.ContainsKey(condition)) conditionGroups[condition] = 0;
                    conditionGroups[condition]++;
                }

                report.AppendLine("── CONDITION SUMMARY ──");
                foreach (var rating in ConditionRatings)
                {
                    int count = conditionGroups.GetValueOrDefault(rating, 0);
                    report.AppendLine($"  {rating}: {count}");
                }
                report.AppendLine($"  Not assessed: {noCondition}");

                // Set default condition for unassessed assets
                if (noCondition > 0)
                {
                    using (var t = new Transaction(doc, "STING Asset Condition"))
                    {
                        t.Start();
                        int written = 0;
                        foreach (var el in allEquipment)
                        {
                            string existing = ParameterHelpers.GetString(el, "ASS_CONDITION_TXT");
                            if (string.IsNullOrEmpty(existing))
                            {
                                ParameterHelpers.SetString(el, "ASS_CONDITION_TXT", "A - Good", false);
                                ParameterHelpers.SetString(el, "ASS_CONDITION_DATE_TXT",
                                    DateTime.Now.ToString("yyyy-MM-dd"), false);
                                written++;
                            }
                        }
                        t.Commit();
                        report.AppendLine($"\nDefault condition 'A - Good' set for {written} unassessed assets.");
                    }
                }

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
        internal static readonly Dictionary<string, int> MaintenanceIntervals = new()
        {
            ["Mechanical Equipment"] = 6,
            ["Electrical Equipment"] = 12,
            ["Plumbing Fixtures"] = 12,
            ["Lighting Fixtures"] = 12,
            ["Sprinklers"] = 6,
            ["Fire Alarm Devices"] = 6,
            ["Air Terminals"] = 12,
            ["Ducts"] = 24,
            ["Pipes"] = 24,
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var report = new System.Text.StringBuilder();
                report.AppendLine("═══ MAINTENANCE SCHEDULE ═══\n");

                var csvLines = new List<string> { "AssetTag,Category,Family,Room,Interval_Months,NextDue,Condition" };

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
                        int interval = MaintenanceIntervals.GetValueOrDefault(catName, 12);
                        string nextDue = DateTime.Now.AddMonths(interval).ToString("yyyy-MM-dd");

                        report.AppendLine($"── {catName} ({elems.Count}) — Every {interval} months ──");

                        foreach (var el in elems)
                        {
                            string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                            string family = ParameterHelpers.GetFamilyName(el);
                            string room = "";
                            var roomEl = ParameterHelpers.GetRoomAtElement(doc, el);
                            if (roomEl != null)
                                room = roomEl.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";

                            string condition = ParameterHelpers.GetString(el, "ASS_CONDITION_TXT");

                            ParameterHelpers.SetString(el, "ASS_MAINT_INTERVAL_TXT", $"{interval} months", false);
                            ParameterHelpers.SetString(el, "ASS_MAINT_NEXT_TXT", nextDue, false);

                            csvLines.Add($"{tag},{catName},{family},{room},{interval},{nextDue},{condition}");
                            totalAssets++;
                        }
                    }
                    t.Commit();
                }

                string folder = Path.GetDirectoryName(doc.PathName) ?? Path.GetTempPath();
                string csvPath = Path.Combine(folder, "STING_MaintenanceSchedule.csv");
                File.WriteAllLines(csvPath, csvLines);

                report.AppendLine($"\nTotal: {totalAssets} assets scheduled");
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
                var doc = commandData.Application.ActiveUIDocument.Document;
                string folder = Path.GetDirectoryName(doc.PathName) ?? Path.GetTempPath();
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
                    spatialJson.Add($"  {{\"id\":{room.Id.IntegerValue},\"name\":\"{EscapeJson(name)}\",\"number\":\"{number}\",\"area\":{area:F2},\"level\":\"{EscapeJson(levelName)}\"}},");
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
                        string catName = el.Category?.Name ?? "";
                        string family = ParameterHelpers.GetFamilyName(el);
                        string condition = ParameterHelpers.GetString(el, "ASS_CONDITION_TXT");
                        var loc = (el.Location as LocationPoint)?.Point;
                        string xyz = loc != null ? $"[{loc.X * 0.3048:F2},{loc.Y * 0.3048:F2},{loc.Z * 0.3048:F2}]" : "null";
                        assetJson.Add($"  {{\"id\":{el.Id.IntegerValue},\"tag\":\"{EscapeJson(tag)}\",\"category\":\"{EscapeJson(catName)}\",\"family\":\"{EscapeJson(family)}\",\"condition\":\"{EscapeJson(condition)}\",\"position\":{xyz}}},");
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
                    levelJson.Add($"  {{\"id\":{lvl.Id.IntegerValue},\"name\":\"{EscapeJson(lvl.Name)}\",\"elevation\":{lvl.Elevation * 0.3048:F2}}},");
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
                var doc = commandData.Application.ActiveUIDocument.Document;
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
                var doc = commandData.Application.ActiveUIDocument.Document;
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
                                csvLines.Add($"{sys},{tag},{el.Category?.Name},{family},{check},PENDING");
                                totalChecks++;
                            }
                            ParameterHelpers.SetString(el, "ASS_COMMISSION_STATUS_TXT", "PENDING", false);
                        }
                    }
                    t.Commit();
                }

                string folder = Path.GetDirectoryName(doc.PathName) ?? Path.GetTempPath();
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
                var doc = commandData.Application.ActiveUIDocument.Document;
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
                var doc = commandData.Application.ActiveUIDocument.Document;
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
        internal static readonly Dictionary<string, int> WarrantyPeriods = new()
        {
            ["Mechanical Equipment"] = 5, ["Electrical Equipment"] = 3,
            ["Plumbing Fixtures"] = 2, ["Lighting Fixtures"] = 5, ["Sprinklers"] = 10,
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var csvLines = new List<string> { "AssetTag,Category,Family,WarrantyYears,InstallDate,ExpiryDate,Status" };
                string installDate = DateTime.Now.ToString("yyyy-MM-dd");
                int total = 0;

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
                        int yrs = WarrantyPeriods.GetValueOrDefault(name, 2);
                        string expiry = DateTime.Now.AddYears(yrs).ToString("yyyy-MM-dd");
                        var elems = new FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType().ToList();
                        foreach (var el in elems)
                        {
                            string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                            ParameterHelpers.SetString(el, "ASS_WARRANTY_TXT", $"{yrs} years", false);
                            ParameterHelpers.SetString(el, "ASS_WARRANTY_EXPIRY_TXT", expiry, false);
                            csvLines.Add($"{tag},{name},{ParameterHelpers.GetFamilyName(el)},{yrs},{installDate},{expiry},ACTIVE");
                            total++;
                        }
                    }
                    t.Commit();
                }

                string folder = Path.GetDirectoryName(doc.PathName) ?? Path.GetTempPath();
                string csvPath = Path.Combine(folder, "STING_WarrantyTracker.csv");
                File.WriteAllLines(csvPath, csvLines);

                TaskDialog.Show("Warranty Tracker", $"Tracked {total} assets.\nCSV: {csvPath}");
                StingLog.Info($"Warranty: {total} assets tracked");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Warranty tracker failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
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
                var doc = commandData.Application.ActiveUIDocument.Document;
                string folder = Path.GetDirectoryName(doc.PathName) ?? Path.GetTempPath();
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
                        string catName = el.Category?.Name ?? "";
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
    [Transaction(TransactionMode.Manual)]
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
                var doc = commandData.Application.ActiveUIDocument.Document;
                var csvLines = new List<string> { "SensorType,AssetTag,Category,Family,Room,BMS_Address" };
                int sensorCount = 0;

                using (var t = new Transaction(doc, "STING Sensor Mapper"))
                {
                    t.Start();
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
                                string bmsAddr = $"BMS/{sensorType}/{tag}";
                                ParameterHelpers.SetString(el, "ASS_BMS_ADDRESS_TXT", bmsAddr, false);
                                csvLines.Add($"{sensorType},{tag},{el.Category?.Name},{family},{room},{bmsAddr}");
                                sensorCount++;
                            }
                        }
                    }
                    t.Commit();
                }

                string folder = Path.GetDirectoryName(doc.PathName) ?? Path.GetTempPath();
                string csvPath = Path.Combine(folder, "STING_SensorPoints.csv");
                File.WriteAllLines(csvPath, csvLines);

                TaskDialog.Show("Sensor Point Mapper", $"Mapped {sensorCount} sensor points.\nCSV: {csvPath}");
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
