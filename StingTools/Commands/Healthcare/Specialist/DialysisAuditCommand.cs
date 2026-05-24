// Healthcare Pack H-18 — Dialysis RO loop + station audit (HBN 07-02).
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using System;
using System.Linq;
using System.Text;
using StingTools.Core.Validation.Healthcare;

namespace StingTools.Commands.Healthcare.Specialist
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DialysisAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                // Specialist audits are read-only one-shot commands; they don't
                // run inside RunAllHealthcareValidators, so they build a quick
                // multi-category collector themselves.
                var clinicalCats = new ElementMulticategoryFilter(new[] {
                    BuiltInCategory.OST_MedicalEquipment,
                    BuiltInCategory.OST_NurseCallDevices,
                    BuiltInCategory.OST_SpecialityEquipment });
                var stations = new FilteredElementCollector(doc).WherePasses(clinicalCats)
                    .WhereElementIsNotElementType().ToElements()
                    .Where(e => Get(e,"ASS_PRODCT_COD_TXT")=="RO-DIA")
                    .ToList();
                var roPlants = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                    .WhereElementIsNotElementType().ToElements()
                    .Where(e => Get(e,"ASS_PRODCT_COD_TXT")=="RO-DIA")
                    .ToList();
                // Hc.Specialist.Dial.* overrides:
                //   Stations         → if > 0, declared count to compare against the
                //                       model and flag mismatches.
                //   RequireRoLoop    → checkbox; when false the RO-loop flag check
                //                       is suppressed (e.g. early concept design).
                int declaredStations = HcOptions.DialStations;
                bool requireRoLoop   = HcOptions.DialRoLoopRequired;

                var sb = new StringBuilder();
                sb.AppendLine($"STING — Dialysis Audit (HBN 07-02){(declaredStations > 0 ? $" — declared {declaredStations} stations" : "")}").AppendLine();
                sb.AppendLine($"Dialysis stations / RO drops: {stations.Count}");
                sb.AppendLine($"RO plants:                    {roPlants.Count}");
                if (declaredStations > 0 && stations.Count != declaredStations)
                    sb.AppendLine($"[WARNING] DIA.COUNT     Model has {stations.Count} stations vs panel-declared {declaredStations}");
                if (stations.Count > 0 && roPlants.Count == 0)
                    sb.AppendLine("[ERROR  ] DIA.RO_PLANT  Stations present but no RO plant detected");
                if (requireRoLoop)
                {
                    foreach (var s in stations)
                        if (Get(s,"PLM_RO_LOOP_BOOL") != "1")
                            sb.AppendLine($"[ERROR  ] DIA.RO_FLAG   {s.Name} dialysis station not flagged as RO loop member");
                }
                else
                {
                    sb.AppendLine("[INFO   ] DIA.RO_FLAG   RO-loop flag check suppressed via panel toggle");
                }
                StingLog.Info(sb.ToString());
                TaskDialog.Show("STING — Dialysis Audit", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("DialysisAuditCommand failed", ex); message = ex.Message; return Result.Failed; }
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
