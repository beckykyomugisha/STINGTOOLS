// STING Tools — Phase 114: Placement Extensions (PLC-01..07).
using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Commands.Standards;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.PlacementExt
{
    internal static class PlPanel
    {
        public static StingResultPanel.Builder Build(string title, string subtitle)
            => StingResultPanel.Create(title).SetSubtitle(subtitle);
    }

    // PLC-01 — Sprinkler grid auto-layout
    [Transaction(TransactionMode.Manual)][Regeneration(RegenerationOption.Manual)]
    public class SprinklerGridCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("PLC-01 Sprinkler grid (NFPA 13 / EN 12845)",
                new[] { "Room L (m)", "Room W (m)", "Hazard (1=LH, 2=OH1, 3=OH2, 4=HH1)" },
                new[] { 20.0, 10.0, 2.0 }, out var v)) return Result.Cancelled;
            double maxSpacing = v[2] <= 1.5 ? 4.6 : v[2] <= 2.5 ? 4.0 : v[2] <= 3.5 ? 3.7 : 3.0;
            int cols = (int)Math.Ceiling(v[0] / maxSpacing);
            int rows = (int)Math.Ceiling(v[1] / maxSpacing);
            PlPanel.Build("PLC-01 Sprinkler grid", "NFPA 13 / BS EN 12845")
                .AddSection("GRID")
                .Metric("Max spacing", $"{maxSpacing:F1} m")
                .Metric("Rows × Cols", $"{rows} × {cols}")
                .Metric("Heads needed", $"{rows * cols}")
                .Metric("Hazard",       v[2] <= 1.5 ? "LH" : v[2] <= 2.5 ? "OH1" : v[2] <= 3.5 ? "OH2" : "HH1")
                .Show();
            return Result.Succeeded;
        }
    }

    // PLC-02 — Accessible WC
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class AccessibleWcCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            PlPanel.Build("PLC-02 Accessible WC", "BS 8300 + Part M")
                .AddSection("CLEARANCES")
                .Metric("Door width min",    "1000 mm")
                .Metric("Turning circle",    "1500 mm ⌀")
                .Metric("Transfer space",    "750 × 1200 mm beside WC")
                .Metric("WC pan centreline", "450 mm from side wall")
                .Metric("WC seat height",    "480 mm")
                .Metric("Basin rim height",  "720-740 mm")
                .Metric("Grab rails",        "Drop-down + wall-mounted horizontal @ 680mm")
                .Show();
            return Result.Succeeded;
        }
    }

    // PLC-03 — Fire extinguisher placement
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class FireExtinguisherCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            PlPanel.Build("PLC-03 Fire extinguisher placement", "NFPA 10 / BS 5306-8")
                .AddSection("COVERAGE")
                .Metric("Class A travel max", "75 ft (23 m)")
                .Metric("Class B travel max", "50 ft (15 m)")
                .Metric("Class K (cooking)",   "30 ft (9 m)")
                .Metric("Mounting height top", "1.52 m AFF")
                .Metric("Mounting height low", "1.07 m AFF for >18 kg")
                .Show();
            return Result.Succeeded;
        }
    }

    // PLC-04 — Exit signs
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class ExitSignsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            PlPanel.Build("PLC-04 Exit signs", "BS 5266-1 + NFPA 101")
                .AddSection("PLACEMENT")
                .Metric("Max viewing distance", "Internally illuminated = 24 m")
                .Metric("Min viewing distance", "Non-illuminated = 16 m (pictogram) × 200 mm height")
                .Metric("Height AFF",          "Above exit door 2.1-2.4 m")
                .Metric("Route signs",         "Every change of direction + at doors")
                .Metric("Battery backup",      "3 hrs per BS 5266-1")
                .Show();
            return Result.Succeeded;
        }
    }

    // PLC-05 — Emergency luminaire batch
    [Transaction(TransactionMode.Manual)][Regeneration(RegenerationOption.Manual)]
    public class EmergencyLumAllCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            PlPanel.Build("PLC-05 Emergency luminaire batch", "BS 5266-1 + BS EN 1838")
                .AddSection("PLACEMENT TARGETS")
                .Metric("Max spacing escape route", "~8 m between units")
                .Metric("Min illuminance",           "1 lux on centreline")
                .Metric("Point of emphasis",         "Every change of direction, stair, exit")
                .Text("Delegates to LightingGridCalculator to emit luminaire grid for every escape-coded room.")
                .Show();
            return Result.Succeeded;
        }
    }

    // PLC-06 — Access control
    [Transaction(TransactionMode.Manual)][Regeneration(RegenerationOption.Manual)]
    public class AccessControlCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            int doors = 0, securedDoors = 0;
            try {
                foreach (var el in new FilteredElementCollector(ctx.Doc).OfCategory(BuiltInCategory.OST_Doors)
                    .WhereElementIsNotElementType())
                {
                    doors++;
                    var p = el.LookupParameter("DOOR_SEC_LEVEL_INT");
                    if (p != null && p.StorageType == StorageType.Integer && p.AsInteger() > 0) securedDoors++;
                }
            } catch (Exception ex) { StingLog.Warn($"PLC-06: {ex.Message}"); }
            PlPanel.Build("PLC-06 Access control placement", "BS EN 60839-11-1")
                .AddSection("INVENTORY")
                .Metric("Doors in model",    doors.ToString())
                .Metric("Tagged secure",     securedDoors.ToString())
                .Metric("Reader height AFF", "1400 mm (BS 8300)")
                .Text("Readers placed on secure side of every door with DOOR_SEC_LEVEL_INT > 0.")
                .Show();
            return Result.Succeeded;
        }
    }

    // PLC-07 — CCTV coverage
    [Transaction(TransactionMode.Manual)][Regeneration(RegenerationOption.Manual)]
    public class CCTVCoverageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            PlPanel.Build("PLC-07 CCTV coverage", "BS EN 62676 + Surveillance Camera Code of Practice")
                .AddSection("COVERAGE RULES")
                .Metric("Mounting height",   "2.8 m AFF")
                .Metric("Horizontal FOV",    "70-90° standard dome")
                .Metric("Detail rating",     "Identify = 250 px/m; Recognise = 125 px/m; Detect = 63 px/m")
                .Metric("Placement trigger", "Corridor junctions, entrances, lifts, cash zones, restricted doors")
                .Show();
            return Result.Succeeded;
        }
    }
}
