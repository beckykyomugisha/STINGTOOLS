using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical.Lighting
{
    /// <summary>
    /// Per-room emergency-lighting audit. A fixture is treated as emergency
    /// when its family / type-mark / LTG_FIX_TYPE_CLASSIFICATION_TXT matches an emergency-style pattern.
    /// Rooms with normal-only fixtures get amber; rooms with no emergency
    /// at all get red; rooms where an emergency is on the same circuit as
    /// a normal fixture get the SAME_CIRCUIT warning.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class EmergencyLightingAuditCommand : IExternalCommand
    {
        private static readonly string[] EmergPatterns =
            { "emergency", "emerg", "exit", "em-", "e-", "maintained", "non-maintained" };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(r => r.Area > 0)
                .ToList();
            var fixtures = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .ToList();

            var occupancyMap = LoadOccupancyTypes();

            var rows = new List<EmergAuditRow>();
            View view = doc.ActiveView;
            var ogsRed = MakeOverride(244, 67, 54);
            var ogsAmber = MakeOverride(255, 152, 0);

            using (var tx = new Transaction(doc, "STING Emergency Lighting Audit"))
            {
                tx.Start();
                foreach (var r in rooms)
                {
                    var inRoom = fixtures.Where(fi => InRoom(fi, r)).ToList();
                    var emerg = inRoom.Where(IsEmergency).ToList();
                    var normal = inRoom.Where(fi => !IsEmergency(fi)).ToList();
                    bool sameCircuit = AnySharedCircuit(emerg, normal);
                    string status =
                        emerg.Count == 0 ? "NONE" :
                        sameCircuit ? "SAME_CIRCUIT" : "OK";
                    string occ = OccupancyFor(r.Name ?? "", occupancyMap);

                    try
                    {
                        ParameterHelpers.SetString(r, ParamRegistry.ELC_EMERG_COVERED,
                            emerg.Count > 0 ? "1" : "0", overwrite: true);
                    }
                    catch (Exception ex) { StingLog.Warn($"EmergCovered write: {ex.Message}"); }

                    if (view != null)
                    {
                        try
                        {
                            if (status == "NONE") view.SetElementOverrides(r.Id, ogsRed);
                            else if (status == "SAME_CIRCUIT") view.SetElementOverrides(r.Id, ogsAmber);
                        }
                        catch { }
                    }

                    rows.Add(new EmergAuditRow
                    {
                        RoomName       = r.Name ?? "",
                        OccupancyType  = occ,
                        NormalCircuits = CountCircuits(normal),
                        EmergCircuits  = CountCircuits(emerg),
                        SameCircuit    = sameCircuit,
                        Status         = status
                    });
                }
                tx.Commit();
            }
            StingElectricalCommandHandler.LastEmergAudit = rows;
            try { ComplianceScan.InvalidateCache(); } catch { }
            int none = rows.Count(r => r.Status == "NONE");
            int same = rows.Count(r => r.Status == "SAME_CIRCUIT");
            TaskDialog.Show("STING Emergency Lighting",
                $"Audited {rows.Count} room(s). No emergency: {none}. Same-circuit faults: {same}.");
            return Result.Succeeded;
        }

        private static bool InRoom(FamilyInstance fi, Room r)
        {
            try
            {
                if (fi.Room is Room rr && rr.Id == r.Id) return true;
                var pt = (fi.Location as LocationPoint)?.Point;
                if (pt == null) return false;
                return r.IsPointInRoom(pt);
            }
            catch { return false; }
        }

        public static bool IsEmergency(FamilyInstance fi)
        {
            if (fi == null) return false;
            try
            {
                string fname = (fi.Symbol?.FamilyName ?? "").ToLowerInvariant();
                if (EmergPatterns.Any(p => fname.Contains(p))) return true;
                string tm = (fi.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString() ?? "").ToLowerInvariant();
                if (tm.StartsWith("em")) return true;
                // Canonical via MR_PARAMETERS: LTG_FIX_TYPE_CLASSIFICATION_TXT
                // is the project-wide fixture type discriminator (Phase 188 fix
                // — earlier ELC_EMERG_TYPE literal had no canonical mapping).
                // Match emergency-style classifications (e.g. "Emergency",
                // "Self-contained EM", "Maintained EM") rather than any non-empty
                // value, since the param now also carries non-emergency types.
                string emergType = ParameterHelpers.GetString(fi, "LTG_FIX_TYPE_CLASSIFICATION_TXT");
                if (!string.IsNullOrEmpty(emergType) &&
                    emergType.IndexOf("emerg", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            catch { }
            return false;
        }

        private static int CountCircuits(IEnumerable<FamilyInstance> fixtures)
        {
            var ids = new HashSet<long>();
            foreach (var fi in fixtures)
            {
                try
                {
                    var sys = fi.MEPModel?.GetElectricalSystems()?.FirstOrDefault();
                    if (sys != null) ids.Add(sys.Id.Value);
                }
                catch { }
            }
            return ids.Count;
        }

        private static bool AnySharedCircuit(List<FamilyInstance> a, List<FamilyInstance> b)
        {
            var setA = new HashSet<long>();
            foreach (var fi in a)
            {
                try
                {
                    var sys = fi.MEPModel?.GetElectricalSystems()?.FirstOrDefault();
                    if (sys != null) setA.Add(sys.Id.Value);
                }
                catch { }
            }
            foreach (var fi in b)
            {
                try
                {
                    var sys = fi.MEPModel?.GetElectricalSystems()?.FirstOrDefault();
                    if (sys != null && setA.Contains(sys.Id.Value)) return true;
                }
                catch { }
            }
            return false;
        }

        private static string OccupancyFor(string name, Dictionary<string, string[]> map)
        {
            string n = (name ?? "").ToLowerInvariant();
            foreach (var kv in map)
                if (kv.Value.Any(p => n.Contains(p))) return kv.Key;
            return "general";
        }

        private static Dictionary<string, string[]> LoadOccupancyTypes()
        {
            try
            {
                string path = StingToolsApp.FindDataFile("STING_LPD_LIMITS.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new Dictionary<string, string[]>();
                var root = JObject.Parse(File.ReadAllText(path));
                var occ = root["occupancyTypes"] as JObject;
                if (occ == null) return new Dictionary<string, string[]>();
                return occ.Properties().ToDictionary(p => p.Name,
                    p => (p.Value as JArray)?.Select(t => t.ToString().ToLowerInvariant()).ToArray() ?? new string[0]);
            }
            catch (Exception ex) { StingLog.Warn($"LoadOccupancyTypes: {ex.Message}"); return new Dictionary<string, string[]>(); }
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
    /// Visually marks emergency lighting fixtures in the active view with a
    /// distinctive blue projection-line override. Read-only — no model writes.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class EmergencyLightingMarkCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            var view = doc.ActiveView;
            if (view == null) { TaskDialog.Show("STING Emergency", "Activate a graphical view first."); return Result.Cancelled; }

            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Color(33, 150, 243));
            ogs.SetProjectionLineWeight(7);

            int marked = 0;
            using (var tx = new Transaction(doc, "STING Mark Emergency Fixtures"))
            {
                tx.Start();
                foreach (var fi in new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_LightingFixtures)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>())
                {
                    try
                    {
                        if (EmergencyLightingAuditCommand.IsEmergency(fi))
                        {
                            view.SetElementOverrides(fi.Id, ogs);
                            marked++;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"MarkEmerg: {ex.Message}"); }
                }
                tx.Commit();
            }
            TaskDialog.Show("STING Emergency Lighting", $"Marked {marked} emergency fixture(s) in view.");
            return Result.Succeeded;
        }
    }
}
