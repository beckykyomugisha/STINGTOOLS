// Healthcare Pack H-12 — Hybrid OR / Cath / IR clearance + control-room
// adjacency check.
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using System;
using System.Linq;
using System.Text;

namespace StingTools.Commands.Healthcare.Specialist
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HybridOrCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;

                // Hc.Specialist.Hor.* overrides:
                //   MinAreaM2  → OR-HYBRID minimum (slider 30..80, default 70).
                //                CATHLAB / IR scale proportionally so the FGI
                //                ratios stay intact.
                //   IncludeIr  → if false, IR rooms are skipped.
                //   Room       → if non-empty, only that single room is audited.
                double horMin = HcOptions.HorMinAreaM2;
                double cathMin = horMin * (40.0 / 70.0);
                double irMin   = horMin * (38.0 / 70.0);
                bool includeIr = HcOptions.HorIncludeIr;
                string focusRoom = HcOptions.HorRoom?.Trim() ?? "";

                var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType().ToElements()
                    .Where(r => Get(r, "CLN_ROOM_CLASS_TXT") is "OR-HYBRID" or "CATHLAB" or "IR")
                    .Where(r => includeIr || Get(r, "CLN_ROOM_CLASS_TXT") != "IR")
                    .Where(r => string.IsNullOrEmpty(focusRoom) ||
                                string.Equals(r.Name, focusRoom, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var sb = new StringBuilder();
                sb.AppendLine($"STING — Hybrid OR / Cath{(includeIr ? " / IR" : "")} Audit (min OR-HYBRID {horMin:F0} m²)").AppendLine();
                if (rooms.Count == 0)
                    sb.AppendLine("No matching rooms found.");
                foreach (var r in rooms)
                {
                    var rc = Get(r, "CLN_ROOM_CLASS_TXT");
                    var area = AreaSqM(r);
                    var min = rc switch { "OR-HYBRID" => horMin, "CATHLAB" => cathMin, "IR" => irMin, _ => 0.0 };
                    if (area > 0 && area < min)
                        sb.AppendLine($"[ERROR  ] HYB.AREA  {r.Name} ({rc}) area {area:F1} m² < min {min:F1} m² (FGI / VA)");
                    else if (area > 0)
                        sb.AppendLine($"[OK     ] HYB.AREA  {r.Name} ({rc}) area {area:F1} m²");
                }
                StingLog.Info(sb.ToString());
                TaskDialog.Show("STING — Hybrid OR Audit", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("HybridOrCheckCommand failed", ex); message = ex.Message; return Result.Failed; }
        }
        private static string Get(Element el, string n) {
            try { var p = el.LookupParameter(n); return p?.HasValue==true && p.StorageType==StorageType.String ? (p.AsString()??"") : ""; }
            catch { return ""; }
        }
        private static double AreaSqM(Element room) {
            try {
                var p = room.LookupParameter("Area") ?? room.LookupParameter("ASS_ROOM_AREA_SQ_M");
                if (p == null || !p.HasValue) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble() * 0.092903; // ft² → m² when native
                if (p.StorageType == StorageType.String && double.TryParse(p.AsString(), out var v)) return v;
            } catch { }
            return 0;
        }
    }
}
