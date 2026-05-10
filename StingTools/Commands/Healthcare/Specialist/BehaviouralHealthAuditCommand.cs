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

                // Hc.Specialist.Bh.* overrides:
                //   UseFgi/UseHbn → at least one must be on; both off ⇒ skip.
                //   RiskLevel     → flags rooms whose CLN_LIG_RISK_LVL_TXT
                //                   numeric value is below the panel level.
                bool useFgi   = HcOptions.BhUseFgi;
                bool useHbn   = HcOptions.BhUseHbn;
                int  riskMin  = HcOptions.BhRiskLevel;

                var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType().ToElements()
                    .Where(r => (useFgi && FGIStandards.RequiresAntiLigature(Get(r,"CLN_ROOM_CLASS_TXT")))
                             || (useHbn && Get(r,"CLN_LIGATURE_RES_BOOL") == "1"))
                    .ToList();
                var sb = new StringBuilder();
                string srcs = (useFgi && useHbn) ? "FGI + HBN" : (useFgi ? "FGI" : (useHbn ? "HBN" : "none"));
                sb.AppendLine($"STING — Behavioural Health Safety Risk Audit (sources {srcs}, risk ≥ {riskMin})").AppendLine();
                if (!useFgi && !useHbn)
                {
                    sb.AppendLine("Both FGI and HBN sources are disabled on the panel — no rules will fire.");
                }
                if (rooms.Count == 0) sb.AppendLine("No behavioural-health rooms detected.");
                foreach (var r in rooms)
                {
                    var rc = Get(r,"CLN_ROOM_CLASS_TXT");
                    if (Get(r,"CLN_LIGATURE_RES_BOOL") != "1")
                        sb.AppendLine($"[ERROR  ] BH.LIG.OFF  {r.Name} ({rc}) anti-ligature flag not set");
                    var risk = Get(r,"CLN_LIG_RISK_LVL_TXT");
                    if (string.IsNullOrEmpty(risk))
                        sb.AppendLine($"[WARNING] BH.RISK.UNSET {r.Name} ({rc}) CLN_LIG_RISK_LVL_TXT empty");
                    else if (int.TryParse(risk, out int rv) && rv < riskMin)
                        sb.AppendLine($"[WARNING] BH.RISK.LOW   {r.Name} ({rc}) risk {rv} < panel min {riskMin}");
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
                  return p.AsValueString() ?? ""; } catch { return ""; }
        }
    }
}
