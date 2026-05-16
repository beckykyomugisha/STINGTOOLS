// Healthcare Pack H-13 — USP <797> / <800> pharmacy cleanroom audit.
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Standards.USP797800;
using System;
using System.Linq;
using System.Text;

namespace StingTools.Commands.Healthcare.Specialist
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PharmacyUspAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType().ToElements()
                    .Where(r => Get(r,"CLN_ROOM_CLASS_TXT") is "PH-CSP-797" or "PH-CSP-800")
                    .ToList();
                var sb = new StringBuilder();
                sb.AppendLine("STING — USP <797> / <800> Pharmacy Audit").AppendLine();
                if (rooms.Count == 0) sb.AppendLine("No PH-CSP-* rooms found.");
                foreach (var r in rooms)
                {
                    var rc = Get(r,"CLN_ROOM_CLASS_TXT");
                    var pol = Get(r,"CLN_PRESS_REGIME_TXT");
                    string expected = rc=="PH-CSP-797" ? "POS" : "NEG";
                    if (!string.Equals(pol, expected, StringComparison.OrdinalIgnoreCase))
                        sb.AppendLine($"[ERROR  ] USP.POL    {r.Name} ({rc}) polarity={pol} expected {expected}");
                    var ach = GetD(r,"HVC_AIR_CHANGES_PER_HR");
                    if (ach.HasValue && ach.Value < 30)
                        sb.AppendLine($"[ERROR  ] USP.ACH    {r.Name} ({rc}) ACH={ach:F1} < 30 (USP minimum)");
                    if (rc=="PH-CSP-800" && Get(r,"PLM_RO_LOOP_BOOL")=="No") {} // placeholder hook
                }
                sb.AppendLine();
                sb.AppendLine($"USP <800> recertification cycle: {USPStandards.RecertificationCycleMonths} months");
                StingLog.Info(sb.ToString());
                TaskDialog.Show("STING — USP Audit", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("PharmacyUspAuditCommand failed", ex); message = ex.Message; return Result.Failed; }
        }
        private static string Get(Element el, string n) {
            try { var p = el.LookupParameter(n); return p?.HasValue==true && p.StorageType==StorageType.String ? (p.AsString()??"") : ""; }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; }
        }
        private static double? GetD(Element el, string n) {
            try { var p = el.LookupParameter(n); if (p?.HasValue!=true) return null;
                  if (p.StorageType==StorageType.Double) return p.AsDouble();
                  if (p.StorageType==StorageType.Integer) return (double)p.AsInteger();
                  if (p.StorageType==StorageType.String && double.TryParse(p.AsString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) return v;
                  return null; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }
    }
}
