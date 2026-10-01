// Healthcare Pack H-13 — USP <797> / <800> pharmacy cleanroom audit.
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Validation.Healthcare;
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
                var doc = ParameterHelpers.GetApp(commandData).ActiveUIDocument.Document;

                // Limits per room from STING_HC_PHARMACY_USP.json (UspCascade); the
                // panel values are overrides that may only tighten them (0 = none).
                string std         = HcOptions.UspStandard;        // "USP-797" / "USP-800"
                double achOverride = HcOptions.UspAchMin;
                double dpOverride  = HcOptions.UspDpPa;
                bool   hasBuffer   = HcOptions.UspHasBuffer;
                bool   hasAnteroom = HcOptions.UspHasAnteroom;
                var cascade = HcSpecialistData.UspCascadeData;
                string roomClass = std == "USP-800" ? "PH-CSP-800" : "PH-CSP-797";

                var sb = new StringBuilder();
                sb.AppendLine($"STING — {std.Replace("USP-", "USP <")}> Pharmacy Audit").AppendLine();
                if (cascade == null)
                {
                    sb.AppendLine("NOT CHECKED — STING_HC_PHARMACY_USP.json unusable: " + string.Join("; ", HcSpecialistData.UspCascadeErrors));
                    TaskDialog.Show("STING — USP Audit", sb.ToString());
                    return Result.Succeeded;
                }
                var spec = UspCascade.ForRoomClass(cascade, roomClass);
                if (spec != null)
                {
                    var maxPa = UspCascade.MaxPa(spec);
                    sb.AppendLine($"{spec.Code} ({roomClass}): {spec.Polarity} to {spec.RelativeTo}, " +
                                  $"|ΔP| ≥ {UspCascade.MinPa(spec):0.##} Pa{(maxPa.HasValue ? $" and ≤ {maxPa.Value:0.##} Pa" : "")}, ACH ≥ {spec.AchMin:0}");
                    sb.AppendLine($"  Source: {spec.Source}");
                    if (!string.IsNullOrWhiteSpace(spec.Verify)) sb.AppendLine($"  VERIFY: {spec.Verify}");
                    sb.AppendLine();
                }

                var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType().ToElements()
                    .Where(r => string.Equals(Get(r,"CLN_ROOM_CLASS_TXT"), roomClass, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (rooms.Count == 0) sb.AppendLine($"No {roomClass} rooms found.");
                int fail = 0, notChecked = 0;
                foreach (var r in rooms)
                {
                    var findings = UspCascade.Check(spec, Get(r,"CLN_PRESS_REGIME_TXT"),
                        GetD(r,"CLN_PRESS_DELTA_DESIGN_PA_NR"), GetD(r,"HVC_AIR_CHANGES_PER_HR"),
                        dpOverride, achOverride);
                    if (findings.Count == 0) sb.AppendLine($"[PASS   ] {r.Name}");
                    foreach (var f in findings)
                    {
                        if (f.Status == "FAIL") fail++;
                        else if (f.Status == "NOT CHECKED") notChecked++;
                        sb.AppendLine($"[{f.Status,-11}] {f.Code,-12} {r.Name}: {f.Message}");
                    }
                }
                sb.AppendLine();
                sb.AppendLine($"Rooms: {rooms.Count} · failures: {fail} · not checked: {notChecked}");
                sb.AppendLine("Ante-rooms and C-SCAs have no room class yet, so they are not audited here.");
                if (!hasBuffer)   sb.AppendLine("[WARNING] USP.BUFFER   panel asserts no buffer room — verify PEC/SEC layout");
                if (!hasAnteroom) sb.AppendLine("[WARNING] USP.ANTERM   panel asserts no anteroom — verify clean/dirty cascade");
                sb.AppendLine();
                sb.AppendLine($"Recertification cycle: {cascade.RecertificationCycleMonths} months ({cascade.RecertificationSource})");
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
                  if (p.StorageType==StorageType.String && StingTools.Core.NumberText.TryParse(p.AsString(), out var v)) return v;
                  return null; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }
    }
}
