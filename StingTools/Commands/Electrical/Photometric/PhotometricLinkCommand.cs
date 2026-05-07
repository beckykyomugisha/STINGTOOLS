using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Commands.Electrical.Lighting;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Photometric
{
    /// <summary>
    /// Three-way photometric integration: import lux + UGR from a DIALux
    /// evo IFC export; estimate lux from connected watts using a simplified
    /// CIBSE LG7 lumen-method formula; or surface a guidance dialog for the
    /// ElumTools / DIALux handoff. Phase 179 ships all three modes; the
    /// IFC parser uses a simple regex-based reader sufficient for standard
    /// DIALux evo output — production hardening could swap in xbim /
    /// GeometryGym.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PhotometricLinkCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var dlg = new TaskDialog("STING Photometric Link")
            {
                MainInstruction = "Photometric Link — choose action",
                MainContent =
                    "1. Import DIALux IFC — read lux/UGR from a DIALux evo export\n" +
                    "2. Estimate from Watts — rough CIBSE LG7 lumen-method estimate\n" +
                    "3. Export guide — workflow notes for ElumTools / DIALux evo",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Import DIALux IFC");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Estimate from Watts (CIBSE LG7)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Export guide for ElumTools / DIALux");

            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: return ImportDialux(doc);
                case TaskDialogResult.CommandLink2: return EstimateFromWatts(doc);
                case TaskDialogResult.CommandLink3: return ShowGuide();
                default: return Result.Cancelled;
            }
        }

        private static Result ImportDialux(Document doc)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "IFC Files (*.ifc)|*.ifc",
                Title  = "Select DIALux evo IFC Export"
            };
            if (ofd.ShowDialog() != true) return Result.Cancelled;

            var luxByRoom = ParseDialuxIfc(ofd.FileName);
            if (luxByRoom.Count == 0)
            {
                TaskDialog.Show("STING Photometric",
                    "No illuminance values found. Ensure DIALux exported with photometric properties enabled.");
                return Result.Cancelled;
            }
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().OfType<SpatialElement>()
                .Where(r => (r.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() ?? 0) > 0)
                .ToList();
            int matched = 0;
            using (var tx = new Transaction(doc, "STING Photometric Import"))
            {
                tx.Start();
                foreach (var r in rooms)
                {
                    string key = Normalise(r.Name);
                    if (luxByRoom.TryGetValue(key, out var v))
                    {
                        try { ParameterHelpers.SetString(r, ParamRegistry.ELC_PHOTO_LUX, $"{v.lux:0.0}", overwrite: true); } catch { }
                        try { ParameterHelpers.SetString(r, ParamRegistry.ELC_PHOTO_UGR, $"{v.ugr:0.0}", overwrite: true); } catch { }
                        matched++;
                    }
                }
                tx.Commit();
            }
            try { ComplianceScan.InvalidateCache(); } catch { }
            TaskDialog.Show("STING Photometric",
                $"Imported photometric data for {matched} of {rooms.Count} room(s).");
            return Result.Succeeded;
        }

        private static Result EstimateFromWatts(Document doc)
        {
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().OfType<SpatialElement>()
                .Where(r => (r.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() ?? 0) > 0)
                .ToList();
            int written = 0;
            const double UF = 0.65, MF = 0.80;
            using (var tx = new Transaction(doc, "STING Photometric Estimate"))
            {
                tx.Start();
                foreach (var room in rooms)
                {
                    try
                    {
                        double areaM2 = (room.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() ?? 0) * 0.0929;
                        if (areaM2 < 0.01) continue;
                        double totalLumens = SumLumensInRoom(doc, room);
                        if (totalLumens < 1) continue;
                        double lux = totalLumens * UF * MF / areaM2;
                        double ugr = EstimateUGR(totalLumens, areaM2);
                        try { ParameterHelpers.SetString(room, ParamRegistry.ELC_PHOTO_LUX, $"{lux:0.0}", overwrite: true); } catch { }
                        try { ParameterHelpers.SetString(room, ParamRegistry.ELC_PHOTO_UGR, $"{ugr:0.0}", overwrite: true); } catch { }
                        written++;
                    }
                    catch (Exception ex) { StingLog.Warn($"PhotoEstimate room: {ex.Message}"); }
                }
                tx.Commit();
            }
            try { ComplianceScan.InvalidateCache(); } catch { }
            TaskDialog.Show("STING Photometric Estimate",
                $"Lux estimates written for {written} room(s).\n" +
                "Values use UF=0.65 / MF=0.80 per CIBSE LG7. For accurate results use DIALux evo or ElumTools.");
            return Result.Succeeded;
        }

        private static Result ShowGuide()
        {
            TaskDialog.Show("STING — ElumTools / DIALux Guide",
                "ElumTools (Revit-integrated):\n" +
                "  1. STING → Electrical → DIALux Export → IFC 4 file\n" +
                "  2. ElumTools: File → Import Rooms from IFC\n" +
                "  3. Assign ElumTools luminaire types to STING families\n" +
                "  4. Run calculation; export results CSV\n" +
                "  5. STING → Photometric Link → Import DIALux IFC\n\n" +
                "DIALux evo (standalone):\n" +
                "  1. STING → Electrical → DIALux Export → IFC 4 file\n" +
                "  2. DIALux evo: File → Import IFC\n" +
                "  3. Assign luminaire catalogue entries\n" +
                "  4. Calculate; export IFC with photometric results\n" +
                "  5. STING → Photometric Link → Import DIALux IFC");
            return Result.Succeeded;
        }

        private static Dictionary<string, (double lux, double ugr)> ParseDialuxIfc(string path)
        {
            var result = new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string currentRoom = null;
                double lux = 0, ugr = 0;
                foreach (var line in File.ReadLines(path))
                {
                    if (line.Contains("IFCSPACE"))
                    {
                        var m = Regex.Match(line, @"IFCSPACE\([^,]+,[^,]+,'([^']+)'");
                        if (m.Success)
                        {
                            if (currentRoom != null && lux > 0)
                                result[Normalise(currentRoom)] = (lux, ugr);
                            currentRoom = m.Groups[1].Value;
                            lux = 0; ugr = 0;
                        }
                    }
                    else if (line.Contains("MaintainedIlluminance") && currentRoom != null)
                    {
                        var m = Regex.Match(line, @"IFCREAL\(([0-9.]+)\)");
                        if (m.Success) lux = double.Parse(m.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture);
                    }
                    else if (line.Contains("'UGR'") && currentRoom != null)
                    {
                        var m = Regex.Match(line, @"IFCREAL\(([0-9.]+)\)");
                        if (m.Success) ugr = double.Parse(m.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                if (currentRoom != null && lux > 0)
                    result[Normalise(currentRoom)] = (lux, ugr);
            }
            catch (Exception ex) { StingLog.Warn($"ParseDialuxIfc: {ex.Message}"); }
            return result;
        }

        private static string Normalise(string s)
            => Regex.Replace(s?.ToUpperInvariant() ?? "", @"\s+", "");

        private static double SumLumensInRoom(Document doc, SpatialElement room)
        {
            double total = 0;
            try
            {
                var bb = room.get_BoundingBox(null);
                if (bb == null) return 0;
                var outline = new Outline(bb.Min, bb.Max);
                var bbf = new BoundingBoxIntersectsFilter(outline);
                var fixtures = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_LightingFixtures)
                    .WherePasses(bbf).WhereElementIsNotElementType()
                    .OfType<FamilyInstance>().ToList();
                foreach (var fi in fixtures)
                {
                    double lumens = ParseDouble(ParameterHelpers.GetString(fi, ParamRegistry.LTG_LUMENS));
                    if (lumens < 1)
                    {
                        double watts = ParseDouble(ParameterHelpers.GetString(fi, ParamRegistry.LTG_WATTAGE));
                        if (watts < 1)
                        {
                            try { watts = fi.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD)?.AsDouble() ?? 0; } catch { }
                        }
                        lumens = watts * 80.0;
                    }
                    total += lumens;
                }
            }
            catch (Exception ex) { StingLog.Warn($"SumLumensInRoom: {ex.Message}"); }
            return total;
        }

        private static double EstimateUGR(double lumens, double areaM2)
        {
            double lpd = lumens / Math.Max(areaM2, 1.0);
            if (lpd < 200) return 16;
            if (lpd < 400) return 19;
            if (lpd < 700) return 22;
            return 25;
        }

        private static double ParseDouble(string s) => double.TryParse(s, out double v) ? v : 0;
    }
}
