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
                        try { ParameterHelpers.SetString(r, ParamRegistry.ELC_PHOTO_LUX, $"{v.lux:0.0}", overwrite: true); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        // A missing UGR in the IFC parses as 0 — do not write it as a result.
                        if (v.ugr > 0)
                        {
                            try { ParameterHelpers.SetString(r, ParamRegistry.ELC_PHOTO_UGR, $"{v.ugr:0.0}", overwrite: true); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        }
                        matched++;
                    }
                }
                tx.Commit();
            }
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
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
            int written = 0, fromWatts = 0, noData = 0;
            const double UF = 0.65, MF = 0.80;
            var allFixtures = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .WhereElementIsNotElementType().OfType<FamilyInstance>().ToList();
            using (var tx = new Transaction(doc, "STING Photometric Estimate"))
            {
                tx.Start();
                foreach (var room in rooms)
                {
                    try
                    {
                        double areaM2 = (room.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() ?? 0) * 0.0929;
                        if (areaM2 < 0.01) continue;
                        if (!(room is Autodesk.Revit.DB.Architecture.Room rm)) continue;
                        double totalLumens = SumLumensInRoom(rm, allFixtures, ref fromWatts, ref noData);
                        if (totalLumens < 1) continue;
                        double lux = totalLumens * UF * MF / areaM2;
                        try { ParameterHelpers.SetString(room, ParamRegistry.ELC_PHOTO_LUX, $"{lux:0.0}", overwrite: true); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        // UGR is NOT written here. It depends on luminaire luminance,
                        // position and the observer's view — a lumen total cannot give it.
                        // Only a photometric calculation (DIALux import, option 1) writes UGR.
                        written++;
                    }
                    catch (Exception ex) { StingLog.Warn($"PhotoEstimate room: {ex.Message}"); }
                }
                tx.Commit();
            }
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            TaskDialog.Show("STING Photometric Estimate",
                $"Lux estimates written for {written} room(s).\n" +
                "Values use UF=0.65 / MF=0.80 per CIBSE LG7. For accurate results use DIALux evo or ElumTools.\n\n" +
                "UGR was NOT written — it requires a photometric calculation (use option 1, Import DIALux IFC).\n" +
                (fromWatts > 0 ? $"⚠ {fromWatts} fixture(s) had no lumen data: lumens ASSUMED from wattage × 80 lm/W.\n" : "") +
                (noData > 0 ? $"⚠ {noData} fixture(s) had neither lumens nor wattage and were left out of the estimate.\n" : ""));
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

        /// <summary>
        /// Lumens of the fixtures whose location point is inside the room
        /// (Room.IsPointInRoom — a bounding-box test also counts neighbours).
        /// </summary>
        private static double SumLumensInRoom(Autodesk.Revit.DB.Architecture.Room room,
            List<FamilyInstance> fixtures, ref int fromWatts, ref int noData)
        {
            double total = 0;
            foreach (var fi in fixtures)
            {
                try
                {
                    bool inRoom = false;
                    try { inRoom = fi.Room?.Id == room.Id; } catch { }
                    if (!inRoom)
                    {
                        var pt = (fi.Location as LocationPoint)?.Point;
                        inRoom = pt != null && room.IsPointInRoom(pt);
                    }
                    if (!inRoom) continue;

                    // LTG_LUMENS resolves to a NUMBER parameter; GetString read "".
                    double lumens = ParameterHelpers.GetDouble(fi, ParamRegistry.LTG_LUMENS);
                    if (lumens < 1) lumens = LuminaireDataReader.Lumens(fi, out _);
                    if (lumens < 1)
                    {
                        double watts = LuminaireDataReader.Watts(fi, out _);
                        if (watts < 1)
                            watts = StingTools.Core.Electrical.ElecUnits.Read(fi, BuiltInParameter.RBS_ELEC_APPARENT_LOAD);
                        if (watts < 1) { noData++; continue; }
                        lumens = watts * 80.0; // assumed LED efficacy — counted and reported
                        fromWatts++;
                    }
                    total += lumens;
                }
                catch (Exception ex) { StingLog.Warn($"SumLumensInRoom fixture {fi.Id}: {ex.Message}"); }
            }
            return total;
        }

        private static double ParseDouble(string s) => double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
    }
}
