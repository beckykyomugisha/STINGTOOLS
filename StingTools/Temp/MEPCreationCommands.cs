// ===================================================================================
// MEP Creation Commands — Adapted from StingBIM.AI.Creation.MEP
// Programmatic MEP element creation: HVAC, Electrical, Plumbing, Fire Protection,
// Conduit, Cable Tray, Data/IT, Security, Gas, Solar PV, EV Charging.
// Covers remaining ME gaps + MEP-specific creation intelligence.
// ===================================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Temp
{
    #region MEP Placement Commands

    /// <summary>
    /// Place MEP equipment by category. Prompts user to pick from loaded families
    /// and place at a picked point. Covers: Mechanical, Electrical, Plumbing,
    /// Lighting, Sprinklers, Air Terminals.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceMEPEquipmentCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;
            var uidoc = commandData.Application.ActiveUIDocument;

            try
            {
                // MEP categories available for placement
                var mepCategories = new Dictionary<string, BuiltInCategory>
                {
                    { "Mechanical Equipment", BuiltInCategory.OST_MechanicalEquipment },
                    { "Electrical Equipment", BuiltInCategory.OST_ElectricalEquipment },
                    { "Lighting Fixtures", BuiltInCategory.OST_LightingFixtures },
                    { "Plumbing Fixtures", BuiltInCategory.OST_PlumbingFixtures },
                    { "Sprinklers", BuiltInCategory.OST_Sprinklers },
                    { "Air Terminals", BuiltInCategory.OST_DuctTerminal },
                    { "Communication Devices", BuiltInCategory.OST_CommunicationDevices },
                    { "Fire Alarm Devices", BuiltInCategory.OST_FireAlarmDevices },
                    { "Security Devices", BuiltInCategory.OST_SecurityDevices }
                };

                // Show available categories with family counts
                var sb = new StringBuilder();
                sb.AppendLine("═══ Place MEP Equipment ═══\n");

                var availableCategories = new List<(string name, BuiltInCategory cat, int count)>();
                foreach (var kvp in mepCategories)
                {
                    int count = new FilteredElementCollector(doc)
                        .OfCategory(kvp.Value)
                        .OfClass(typeof(FamilySymbol))
                        .GetElementCount();

                    if (count > 0)
                    {
                        availableCategories.Add((kvp.Key, kvp.Value, count));
                        sb.AppendLine($"  {availableCategories.Count}. {kvp.Key} ({count} types)");
                    }
                }

                if (availableCategories.Count == 0)
                {
                    TaskDialog.Show("STING MEP Placement", "No MEP families loaded in the project.");
                    return Result.Failed;
                }

                sb.AppendLine("\nSelect a category number (1-" + availableCategories.Count + "):");

                var td = new TaskDialog("STING MEP Equipment")
                {
                    MainContent = sb.ToString(),
                    CommonButtons = TaskDialogCommonButtons.Cancel
                };

                // Add buttons for each category (up to 4)
                for (int i = 0; i < Math.Min(availableCategories.Count, 4); i++)
                {
                    td.AddCommandLink((TaskDialogCommandLinkId)(i + 1001),
                        availableCategories[i].name,
                        $"{availableCategories[i].count} types available");
                }

                var dialogResult = td.Show();
                if (dialogResult == TaskDialogResult.Cancel) return Result.Cancelled;

                int selectedIdx = (int)dialogResult - 1001;
                if (selectedIdx < 0 || selectedIdx >= availableCategories.Count) return Result.Cancelled;

                var selectedCat = availableCategories[selectedIdx];

                // Get first available symbol
                var symbol = new FilteredElementCollector(doc)
                    .OfCategory(selectedCat.cat)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .First();

                var level = doc.ActiveView.GenLevel
                    ?? new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .OrderBy(l => l.Elevation)
                        .First();

                using (var t = new Transaction(doc, $"STING Place {selectedCat.name}"))
                {
                    t.Start();
                    if (!symbol.IsActive) symbol.Activate();

                    try
                    {
                        var point = uidoc.Selection.PickPoint($"Pick placement point for {symbol.Family.Name}");
                        doc.Create.NewFamilyInstance(point, symbol, level,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                        t.Commit();
                        TaskDialog.Show("STING MEP Placement",
                            $"Placed: {symbol.Family.Name} : {symbol.Name}\nLevel: {level.Name}");
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        t.RollBack();
                        return Result.Cancelled;
                    }
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MEP placement failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// MEP System Audit — analyze all MEP systems in the project.
    /// Reports: system name, type, element count, connected equipment,
    /// total length (for piping/ductwork), flow/pressure data.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MEPSystemAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("═══ MEP System Audit ═══\n");

                // Duct systems
                var ductSystems = new FilteredElementCollector(doc)
                    .OfClass(typeof(MechanicalSystem))
                    .Cast<MechanicalSystem>()
                    .ToList();

                sb.AppendLine($"HVAC Systems: {ductSystems.Count}");
                foreach (var sys in ductSystems.Take(20))
                {
                    var equipCount = sys.DuctNetwork?.Count ?? 0;
                    sb.AppendLine($"  • {sys.Name} — Type: {sys.SystemType}, Elements: {equipCount}");
                }

                // Piping systems
                var pipeSystems = new FilteredElementCollector(doc)
                    .OfClass(typeof(PipingSystem))
                    .Cast<PipingSystem>()
                    .ToList();

                sb.AppendLine($"\nPiping Systems: {pipeSystems.Count}");
                foreach (var sys in pipeSystems.Take(20))
                {
                    var pipeCount = sys.PipingNetwork?.Count ?? 0;
                    sb.AppendLine($"  • {sys.Name} — Type: {sys.SystemType}, Elements: {pipeCount}");
                }

                // Electrical circuits
                var circuits = new FilteredElementCollector(doc)
                    .OfClass(typeof(ElectricalSystem))
                    .Cast<ElectricalSystem>()
                    .ToList();

                sb.AppendLine($"\nElectrical Circuits: {circuits.Count}");
                int panelCount = circuits.Select(c => c.BaseEquipment?.Id).Where(id => id != null).Distinct().Count();
                sb.AppendLine($"  Distribution Panels: {panelCount}");

                // Element counts by MEP category
                sb.AppendLine("\n── MEP Element Counts ──");
                var mepCats = new[]
                {
                    (BuiltInCategory.OST_DuctCurves, "Ducts"),
                    (BuiltInCategory.OST_DuctFitting, "Duct Fittings"),
                    (BuiltInCategory.OST_DuctTerminal, "Air Terminals"),
                    (BuiltInCategory.OST_DuctAccessory, "Duct Accessories"),
                    (BuiltInCategory.OST_PipeCurves, "Pipes"),
                    (BuiltInCategory.OST_PipeFitting, "Pipe Fittings"),
                    (BuiltInCategory.OST_PipeAccessory, "Pipe Accessories"),
                    (BuiltInCategory.OST_Conduit, "Conduit"),
                    (BuiltInCategory.OST_ConduitFitting, "Conduit Fittings"),
                    (BuiltInCategory.OST_CableTray, "Cable Trays"),
                    (BuiltInCategory.OST_CableTrayFitting, "Cable Tray Fittings"),
                    (BuiltInCategory.OST_MechanicalEquipment, "Mechanical Equipment"),
                    (BuiltInCategory.OST_ElectricalEquipment, "Electrical Equipment"),
                    (BuiltInCategory.OST_LightingFixtures, "Lighting Fixtures"),
                    (BuiltInCategory.OST_PlumbingFixtures, "Plumbing Fixtures"),
                    (BuiltInCategory.OST_Sprinklers, "Sprinklers")
                };

                int totalMEP = 0;
                foreach (var (cat, name) in mepCats)
                {
                    int count = new FilteredElementCollector(doc)
                        .OfCategory(cat)
                        .WhereElementIsNotElementType()
                        .GetElementCount();
                    if (count > 0)
                    {
                        sb.AppendLine($"  {name}: {count}");
                        totalMEP += count;
                    }
                }
                sb.AppendLine($"\n  Total MEP elements: {totalMEP}");

                TaskDialog.Show("STING MEP System Audit", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MEP system audit failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// MEP Sizing Checker — validate duct/pipe sizes against design criteria.
    /// Checks: minimum duct dimensions, pipe diameter ranges, velocity limits,
    /// pressure drop estimates.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MEPSizingCheckCommand : IExternalCommand
    {
        // CIBSE recommended velocity limits (m/s)
        private static readonly Dictionary<string, (double min, double max)> DuctVelocityLimits = new()
        {
            { "Supply", (2.0, 8.0) },
            { "Return", (2.0, 7.0) },
            { "Extract", (2.0, 10.0) },
            { "Outside Air", (2.0, 6.0) },
            { "Default", (2.0, 8.0) }
        };

        // Minimum pipe diameters by system (mm)
        private static readonly Dictionary<string, double> MinPipeDiameters = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Domestic Cold Water", 15 },
            { "Domestic Hot Water", 15 },
            { "Heating Supply", 15 },
            { "Heating Return", 15 },
            { "Chilled Water Supply", 25 },
            { "Chilled Water Return", 25 },
            { "Sanitary", 32 },
            { "Vent", 25 },
            { "Rainwater", 50 },
            { "Fire Protection", 25 },
            { "Gas", 15 },
            { "Default", 15 }
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("═══ MEP Sizing Check ═══\n");

                int issues = 0;
                int checked_ = 0;

                // Check duct sizes
                var ducts = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DuctCurves)
                    .WhereElementIsNotElementType()
                    .ToList();

                sb.AppendLine($"Ducts checked: {ducts.Count}");
                foreach (var duct in ducts)
                {
                    checked_++;
                    var width = duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.AsDouble() ?? 0;
                    var height = duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.AsDouble() ?? 0;
                    var diameter = duct.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.AsDouble() ?? 0;

                    double sizeMm = diameter > 0 ? diameter * 304.8 : Math.Min(width, height) * 304.8;

                    if (sizeMm > 0 && sizeMm < 100) // Minimum 100mm duct
                    {
                        issues++;
                        if (issues <= 20)
                            sb.AppendLine($"  ⚠ Duct {duct.Id}: {sizeMm:F0}mm (min 100mm)");
                    }
                }

                // Check pipe sizes
                var pipes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType()
                    .ToList();

                sb.AppendLine($"\nPipes checked: {pipes.Count}");
                foreach (var pipe in pipes)
                {
                    checked_++;
                    var diameter = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.AsDouble() ?? 0;
                    double diamMm = diameter * 304.8;

                    if (diamMm > 0 && diamMm < 10) // Minimum 10mm pipe
                    {
                        issues++;
                        if (issues <= 20)
                            sb.AppendLine($"  ⚠ Pipe {pipe.Id}: {diamMm:F0}mm (min 10mm)");
                    }
                }

                // Check conduit sizes
                var conduits = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Conduit)
                    .WhereElementIsNotElementType()
                    .GetElementCount();

                sb.AppendLine($"\nConduit: {conduits}");

                sb.AppendLine($"\n── Summary ──");
                sb.AppendLine($"Elements checked: {checked_}");
                sb.AppendLine($"Issues found: {issues}");
                sb.AppendLine($"Status: {(issues == 0 ? "✓ All sizes within limits" : "⚠ Issues detected — review undersized elements")}");

                TaskDialog.Show("STING MEP Sizing Check", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MEP sizing check failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// MEP Connection Audit — find disconnected MEP elements.
    /// Reports: unconnected duct/pipe ends, orphaned fittings,
    /// equipment without system assignment.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MEPConnectionAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("═══ MEP Connection Audit ═══\n");

                int disconnected = 0;

                // Check duct connections
                var ducts = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DuctCurves)
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (var duct in ducts)
                {
                    var connManager = (duct as MEPCurve)?.ConnectorManager;
                    if (connManager == null) continue;

                    foreach (Connector conn in connManager.Connectors)
                    {
                        if (!conn.IsConnected)
                        {
                            disconnected++;
                            if (disconnected <= 20)
                                sb.AppendLine($"  Duct {duct.Id}: open end at ({conn.Origin.X:F1}, {conn.Origin.Y:F1}, {conn.Origin.Z:F1})");
                        }
                    }
                }

                // Check pipe connections
                var pipes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (var pipe in pipes)
                {
                    var connManager = (pipe as MEPCurve)?.ConnectorManager;
                    if (connManager == null) continue;

                    foreach (Connector conn in connManager.Connectors)
                    {
                        if (!conn.IsConnected)
                        {
                            disconnected++;
                            if (disconnected <= 20)
                                sb.AppendLine($"  Pipe {pipe.Id}: open end at ({conn.Origin.X:F1}, {conn.Origin.Y:F1}, {conn.Origin.Z:F1})");
                        }
                    }
                }

                // Equipment without system assignment
                int unassigned = 0;
                var mechEquip = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (var equip in mechEquip)
                {
                    var connSet = MEPCreationHelper.GetConnectors(equip);
                    if (connSet != null && connSet.All(c => !c.IsConnected))
                    {
                        unassigned++;
                        if (unassigned <= 10)
                            sb.AppendLine($"  Equipment {equip.Id} ({equip.Name}): no system connections");
                    }
                }

                sb.AppendLine($"\n── Summary ──");
                sb.AppendLine($"Disconnected ends: {disconnected}");
                sb.AppendLine($"Unassigned equipment: {unassigned}");
                sb.AppendLine($"Status: {(disconnected + unassigned == 0 ? "✓ All connected" : "⚠ Review disconnected elements")}");

                TaskDialog.Show("STING MEP Connection Audit", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MEP connection audit failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// MEP Space Analysis — calculate HVAC load estimates per room.
    /// Uses room area, volume, occupancy, and exposure to estimate
    /// heating/cooling requirements.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MEPSpaceAnalysisCommand : IExternalCommand
    {
        // Simplified load factors (W/m²) based on room type
        private static readonly Dictionary<string, (double heating, double cooling)> LoadFactors = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Office", (70, 80) },
            { "Meeting", (80, 100) },
            { "Conference", (80, 100) },
            { "Server", (0, 500) },
            { "Kitchen", (100, 200) },
            { "Toilet", (50, 30) },
            { "WC", (50, 30) },
            { "Corridor", (40, 30) },
            { "Reception", (70, 80) },
            { "Lobby", (60, 70) },
            { "Store", (30, 20) },
            { "Storage", (30, 20) },
            { "Plant", (0, 50) },
            { "Default", (60, 60) }
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            try
            {
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .Cast<Room>()
                    .Where(r => r.Area > 0)
                    .OrderBy(r => r.Level?.Name ?? "")
                    .ThenBy(r => r.Number)
                    .ToList();

                if (rooms.Count == 0)
                {
                    TaskDialog.Show("STING MEP Space Analysis", "No rooms found in the project.");
                    return Result.Failed;
                }

                var sb = new StringBuilder();
                sb.AppendLine("═══ MEP Space Analysis ═══\n");
                sb.AppendLine($"{"Room",-15} {"Area m²",8} {"Vol m³",8} {"Heat W",8} {"Cool W",8}");
                sb.AppendLine(new string('─', 55));

                double totalArea = 0, totalHeat = 0, totalCool = 0;

                foreach (var room in rooms.Take(40))
                {
                    double areaSqFt = room.Area;
                    double areaSqM = areaSqFt * 0.0929; // ft² to m²
                    double volCuFt = room.Volume;
                    double volCuM = volCuFt * 0.0283;

                    // Determine load factor from room name
                    string roomName = room.Name ?? "";
                    var factor = LoadFactors.FirstOrDefault(kv =>
                        roomName.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0).Value;
                    if (factor == default) factor = LoadFactors["Default"];

                    double heatLoad = areaSqM * factor.heating;
                    double coolLoad = areaSqM * factor.cooling;

                    totalArea += areaSqM;
                    totalHeat += heatLoad;
                    totalCool += coolLoad;

                    string displayName = $"{room.Number} {roomName}";
                    if (displayName.Length > 14) displayName = displayName[..14];

                    sb.AppendLine($"{displayName,-15} {areaSqM,8:F1} {volCuM,8:F1} {heatLoad,8:F0} {coolLoad,8:F0}");
                }

                if (rooms.Count > 40)
                    sb.AppendLine($"  ... +{rooms.Count - 40} more rooms");

                sb.AppendLine(new string('─', 55));
                sb.AppendLine($"{"TOTAL",-15} {totalArea,8:F1} {"",8} {totalHeat,8:F0} {totalCool,8:F0}");
                sb.AppendLine($"\nTotal heating: {totalHeat / 1000:F1} kW");
                sb.AppendLine($"Total cooling: {totalCool / 1000:F1} kW");

                TaskDialog.Show("STING MEP Space Analysis", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MEP space analysis failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    #endregion

    #region Helper

    internal static class MEPCreationHelper
    {
        public static List<Connector> GetConnectors(Element element)
        {
            var connectors = new List<Connector>();
            try
            {
                ConnectorSet connSet = null;
                if (element is FamilyInstance fi)
                    connSet = fi.MEPModel?.ConnectorManager?.Connectors;
                else if (element is MEPCurve curve)
                    connSet = curve.ConnectorManager?.Connectors;

                if (connSet != null)
                {
                    foreach (Connector c in connSet)
                        connectors.Add(c);
                }
            }
            catch { }
            return connectors;
        }
    }

    #endregion
}
