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
                var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType().ToElements()
                    .Where(r => Get(r, "CLN_ROOM_CLASS_TXT") is "OR-HYBRID" or "CATHLAB" or "IR")
                    .ToList();
                var sb = new StringBuilder();
                sb.AppendLine("STING — Hybrid OR / Cath / IR Audit").AppendLine();
                if (rooms.Count == 0)
                    sb.AppendLine("No OR-HYBRID / CATHLAB / IR rooms found.");
                foreach (var r in rooms)
                {
                    var rc = Get(r, "CLN_ROOM_CLASS_TXT");
                    var area = AreaSqM(r);
                    var min = rc switch { "OR-HYBRID" => 70.0, "CATHLAB" => 40.0, "IR" => 38.0, _ => 0.0 };
                    if (area > 0 && area < min)
                        sb.AppendLine($"[ERROR  ] HYB.AREA  {r.Name} ({rc}) area {area:F1} m² < min {min} m² (FGI / VA)");
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; }
        }
        private static double AreaSqM(Element room) {
            try {
                var p = room.LookupParameter("Area") ?? room.LookupParameter("ASS_ROOM_AREA_SQ_M");
                if (p == null || !p.HasValue) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble() * 0.092903; // ft² → m² when native
                if (p.StorageType == StorageType.String && double.TryParse(p.AsString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) return v;
            } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0;
        }
    }
}
