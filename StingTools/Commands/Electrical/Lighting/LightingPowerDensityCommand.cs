using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical.Lighting
{
    public class LpdLimitTable
    {
        public string StandardId  { get; set; } = "ASHRAE_90_1_2019";
        public double DefaultLimitWPerM2 { get; set; } = 11.8;
        public List<(string Type, string[] Patterns, double Limit)> Spaces { get; set; } = new();

        public double LookupLimit(string roomName)
        {
            string n = (roomName ?? "").ToLowerInvariant();
            foreach (var s in Spaces)
                if (s.Patterns.Any(p => !string.IsNullOrEmpty(p) && n.Contains(p)))
                    return s.Limit;
            return DefaultLimitWPerM2;
        }
    }

    /// <summary>
    /// Per-room lighting-power-density calculation against ASHRAE 90.1 / Part L
    /// 2021 / CIBSE LG7 limits loaded from STING_LPD_LIMITS.json. Writes
    /// ELC_LPD_W_PER_M2, ELC_LPD_LIMIT_W_PER_M2, ELC_LPD_STATUS_TXT to each
    /// room and applies graphic overrides (green / amber / red).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LightingPowerDensityCommand : IExternalCommand
    {
        public static List<LpdRow> LastRows { get; private set; } = new();

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            string standardId = StingElectricalCommandHandler.CurrentLpdStandard ?? "ASHRAE_90_1_2019";
            double customLimit = StingElectricalCommandHandler.CurrentLpdCustomLimit;
            var table = LoadLpdLimits(standardId);
            if (customLimit > 0) table.DefaultLimitWPerM2 = customLimit;

            var fixtures = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .ToList();
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(r => r.Area > 0)
                .ToList();

            View view = doc.ActiveView;
            var ogsGreen = MakeOverride(76, 175, 80);
            var ogsAmber = MakeOverride(255, 152, 0);
            var ogsRed   = MakeOverride(244, 67, 54);

            var rows = new List<LpdRow>();
            using (var tx = new Transaction(doc, "STING LPD Calculation"))
            {
                tx.Start();
                foreach (var r in rooms)
                {
                    double areaM2 = r.Area * 0.09290304;
                    double totalW = 0;
                    foreach (var fi in fixtures.Where(fi => InRoom(fi, r)))
                    {
                        totalW += ReadWattage(fi);
                    }
                    double wPerM2 = areaM2 > 0 ? totalW / areaM2 : 0;
                    double limit  = table.LookupLimit(r.Name);
                    string status = wPerM2 == 0 ? "UNCHECKED"
                                    : wPerM2 > limit ? "FAIL"
                                    : wPerM2 > limit * 0.9 ? "WARNING"
                                    : "PASS";

                    try
                    {
                        ParameterHelpers.SetString(r, ParamRegistry.ELC_LPD_W_M2,
                            $"{wPerM2:0.00}", overwrite: true);
                        ParameterHelpers.SetString(r, ParamRegistry.ELC_LPD_LIMIT_W_M2,
                            $"{limit:0.00}", overwrite: true);
                        ParameterHelpers.SetString(r, ParamRegistry.ELC_LPD_STATUS,
                            status, overwrite: true);
                    }
                    catch (Exception ex) { StingLog.Warn($"LPD write: {ex.Message}"); }

                    if (view != null)
                    {
                        try
                        {
                            if (status == "PASS")    view.SetElementOverrides(r.Id, ogsGreen);
                            else if (status == "WARNING") view.SetElementOverrides(r.Id, ogsAmber);
                            else if (status == "FAIL")    view.SetElementOverrides(r.Id, ogsRed);
                        }
                        catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); }
                    }
                    rows.Add(new LpdRow
                    {
                        RoomName    = r.Name ?? "",
                        AreaM2      = areaM2,
                        TotalW      = totalW,
                        WPerM2      = wPerM2,
                        LimitWPerM2 = limit,
                        Status      = status
                    });
                }
                tx.Commit();
            }
            LastRows = rows;
            StingElectricalCommandHandler.LastLpdRows = rows;
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            int pass = rows.Count(r => r.Status == "PASS");
            int fail = rows.Count(r => r.Status == "FAIL");
            TaskDialog.Show("STING LPD",
                $"Calculated LPD for {rows.Count} room(s) against {standardId}. PASS: {pass}. FAIL: {fail}.");
            return Result.Succeeded;
        }

        private static bool InRoom(FamilyInstance fi, Room r)
        {
            try
            {
                if (fi.Room is Room rr && rr.Id == r.Id) return true;
                var pt = (fi.Location as LocationPoint)?.Point;
                return pt != null && r.IsPointInRoom(pt);
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }

        public static double ReadWattage(FamilyInstance fi)
        {
            try
            {
                double w = fi.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD)?.AsDouble() ?? 0;
                if (w > 0) return w;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            try
            {
                double w = ParseDouble(ParameterHelpers.GetString(fi, ParamRegistry.LTG_WATTAGE));
                if (w > 0) return w;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0;
        }
        private static double ParseDouble(string s) => double.TryParse(s, out double v) ? v : 0;

        public static LpdLimitTable LoadLpdLimits(string standardId)
        {
            var table = new LpdLimitTable { StandardId = standardId };
            try
            {
                string path = StingToolsApp.FindDataFile("STING_LPD_LIMITS.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return table;
                var root = JObject.Parse(File.ReadAllText(path));
                var std = root["standards"]?[standardId] as JObject;
                if (std == null) return table;
                foreach (var sp in std["spaces"] as JArray ?? new JArray())
                {
                    string typeName = sp["type"]?.ToString() ?? "";
                    var pats = (sp["pattern"] as JArray)?.Select(t => (t.ToString() ?? "").ToLowerInvariant()).ToArray() ?? new string[0];
                    double limit = sp["limitWPerM2"]?.Value<double>() ?? 0;
                    if (typeName == "Default") table.DefaultLimitWPerM2 = limit;
                    else table.Spaces.Add((typeName, pats, limit));
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadLpdLimits: {ex.Message}"); }
            return table;
        }

        private static OverrideGraphicSettings MakeOverride(int r, int g, int b)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Color((byte)r, (byte)g, (byte)b));
            ogs.SetProjectionLineWeight(5);
            return ogs;
        }
    }

    /// <summary>
    /// Re-applies graphic overrides from the existing ELC_LPD_STATUS_TXT
    /// values without recalculating. Use after switching views.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LpdColorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            var view = doc.ActiveView;
            if (view == null) { TaskDialog.Show("STING LPD", "Activate a graphical view first."); return Result.Cancelled; }

            int colored = 0;
            using (var tx = new Transaction(doc, "STING LPD Re-color"))
            {
                tx.Start();
                foreach (var r in new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .OfType<Room>())
                {
                    string status = ParameterHelpers.GetString(r, ParamRegistry.ELC_LPD_STATUS);
                    if (string.IsNullOrEmpty(status)) continue;
                    var ogs = new OverrideGraphicSettings();
                    if (status == "PASS")        ogs.SetProjectionLineColor(new Color(76, 175, 80));
                    else if (status == "WARNING") ogs.SetProjectionLineColor(new Color(255, 152, 0));
                    else if (status == "FAIL")    ogs.SetProjectionLineColor(new Color(244, 67, 54));
                    else continue;
                    ogs.SetProjectionLineWeight(5);
                    try { view.SetElementOverrides(r.Id, ogs); colored++; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                }
                tx.Commit();
            }
            TaskDialog.Show("STING LPD", $"Re-coloured {colored} room(s) in view.");
            return Result.Succeeded;
        }
    }
}
