// Healthcare Pack H-16 — Maternity / NICU audit (HBN 21 + 09-03).
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StingTools.Commands.Healthcare.Specialist
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MaternityNicuAuditCommand : IExternalCommand
    {
        private static readonly Dictionary<string,double> MinAreaM2 = new(StringComparer.OrdinalIgnoreCase)
        {
            { "MAT-LDR", 14 }, { "MAT-DEL", 22 }, { "NICU", 14 },
            { "BIRTH-POOL", 22 }, { "MILK-KIT", 9 }
        };
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType().ToElements()
                    .Where(r => MinAreaM2.ContainsKey(Get(r,"CLN_ROOM_CLASS_TXT"))).ToList();
                var sb = new StringBuilder();
                sb.AppendLine("STING — Maternity / NICU Audit").AppendLine();
                if (rooms.Count == 0) sb.AppendLine("No maternity / NICU rooms detected.");
                foreach (var r in rooms)
                {
                    var rc = Get(r,"CLN_ROOM_CLASS_TXT");
                    var area = AreaSqM(r);
                    var min = MinAreaM2[rc];
                    if (area > 0 && area < min)
                        sb.AppendLine($"[ERROR  ] MAT.AREA   {r.Name} ({rc}) area {area:F1} m² < min {min} m²");
                    else if (area > 0)
                        sb.AppendLine($"[OK     ] MAT.AREA   {r.Name} ({rc}) area {area:F1} m²");
                    if (rc == "NICU")
                    {
                        // Noise check.
                        if (Get(r,"PER_ACOUSTICS_BACKGROUND_NOISE_DB") is var n && !string.IsNullOrEmpty(n) &&
                            double.TryParse(n, out var nv) && nv > 35)
                            sb.AppendLine($"[WARNING] MAT.NICU.NR {r.Name} background noise {nv:F0} dB > 35 (HBN 09-03)");
                    }
                }
                StingLog.Info(sb.ToString());
                TaskDialog.Show("STING — Maternity / NICU Audit", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("MaternityNicuAuditCommand failed", ex); message = ex.Message; return Result.Failed; }
        }
        private static string Get(Element el, string n) {
            try { var p = el.LookupParameter(n);
                  if (p == null || !p.HasValue) return "";
                  if (p.StorageType==StorageType.String) return p.AsString() ?? "";
                  return p.AsValueString() ?? ""; } catch { return ""; }
        }
        private static double AreaSqM(Element room) {
            try {
                var p = room.LookupParameter("Area");
                if (p?.HasValue == true && p.StorageType==StorageType.Double)
                    return p.AsDouble() * 0.092903;
            } catch { }
            return 0;
        }
    }
}
