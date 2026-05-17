// Healthcare Pack H-17 — HSDU compartment + flow audit (HBN 13 / HTM 01-01).
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
    public class HsduAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType().ToElements()
                    .Where(r => Get(r,"CLN_ROOM_CLASS_TXT") is "HSDU-W" or "HSDU-P" or "HSDU-S")
                    .ToList();
                var sb = new StringBuilder();
                sb.AppendLine("STING — HSDU Compartment Audit (HBN 13)").AppendLine();
                bool hasW = rooms.Any(r => Get(r,"CLN_ROOM_CLASS_TXT")=="HSDU-W");
                bool hasP = rooms.Any(r => Get(r,"CLN_ROOM_CLASS_TXT")=="HSDU-P");
                if (rooms.Count > 0 && (!hasW || !hasP))
                    sb.AppendLine("[ERROR  ] HSDU.MISS  HSDU present but missing wash (HSDU-W) or pack (HSDU-P) compartment");
                foreach (var r in rooms)
                {
                    var rc = Get(r,"CLN_ROOM_CLASS_TXT");
                    var pol = Get(r,"CLN_PRESS_REGIME_TXT");
                    string need = rc=="HSDU-W" ? "NEG" : "POS";
                    if (!string.Equals(pol, need, StringComparison.OrdinalIgnoreCase))
                        sb.AppendLine($"[ERROR  ] HSDU.POL   {r.Name} ({rc}) polarity={pol} expected {need}");
                }
                StingLog.Info(sb.ToString());
                TaskDialog.Show("STING — HSDU Audit", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("HsduAuditCommand failed", ex); message = ex.Message; return Result.Failed; }
        }
        private static string Get(Element el, string n) {
            try { var p = el.LookupParameter(n); return p?.HasValue==true && p.StorageType==StorageType.String ? (p.AsString()??"") : ""; }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; }
        }
    }
}
