// Healthcare Pack H-14 — Behavioural-health audit (FGI Pt 2 + HBN 03-01).
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Standards.FGI;
using System;
using System.Linq;
using System.Text;

namespace StingTools.Commands.Healthcare.Specialist
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BehaviouralHealthAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType().ToElements()
                    .Where(r => FGIStandards.RequiresAntiLigature(Get(r,"CLN_ROOM_CLASS_TXT")))
                    .ToList();
                var sb = new StringBuilder();
                sb.AppendLine("STING — Behavioural Health Safety Risk Audit").AppendLine();
                if (rooms.Count == 0) sb.AppendLine("No behavioural-health rooms detected.");
                foreach (var r in rooms)
                {
                    var rc = Get(r,"CLN_ROOM_CLASS_TXT");
                    if (Get(r,"CLN_LIGATURE_RES_BOOL") != "1")
                        sb.AppendLine($"[ERROR  ] BH.LIG.OFF  {r.Name} ({rc}) anti-ligature flag not set");
                    var risk = Get(r,"CLN_LIG_RISK_LVL_TXT");
                    if (string.IsNullOrEmpty(risk))
                        sb.AppendLine($"[WARNING] BH.RISK.UNSET {r.Name} ({rc}) CLN_LIG_RISK_LVL_TXT empty");
                    if (Get(r,"CLN_PRIVACY_LVL_TXT") == "OPEN" && rc == "SECL")
                        sb.AppendLine($"[ERROR  ] BH.PRIV.SECL {r.Name} seclusion classified as OPEN privacy");
                }
                StingLog.Info(sb.ToString());
                TaskDialog.Show("STING — Behavioural Audit", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("BehaviouralHealthAuditCommand failed", ex); message = ex.Message; return Result.Failed; }
        }
        private static string Get(Element el, string n) {
            try { var p = el.LookupParameter(n);
                  if (p == null || !p.HasValue) return "";
                  if (p.StorageType==StorageType.String) return p.AsString() ?? "";
                  if (p.StorageType==StorageType.Integer) return p.AsInteger().ToString();
                  return p.AsValueString() ?? ""; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; }
        }
    }
}
