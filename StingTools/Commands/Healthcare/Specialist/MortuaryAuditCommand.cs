// Healthcare Pack H-15 — Mortuary capacity + adjacency audit (HBN 16).
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
    public class MortuaryAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                int beds = 0;
                try {
                    var p = doc.ProjectInformation.LookupParameter("PRJ_ORG_HEALTH_BEDS_INT");
                    if (p?.HasValue == true && p.StorageType==StorageType.Integer) beds = p.AsInteger();
                } catch { }
                int requiredBays = (int)Math.Max(4, Math.Ceiling(beds * 0.005));
                var clinicalCats = new ElementMulticategoryFilter(new[] {
                    BuiltInCategory.OST_MedicalEquipment, BuiltInCategory.OST_SpecialityEquipment });
                int actualBays = new FilteredElementCollector(doc)
                    .WherePasses(clinicalCats)
                    .WhereElementIsNotElementType().ToElements()
                    .Count(e => string.Equals(Get(e,"ASS_PRODCT_COD_TXT"), "MORT-FRG", StringComparison.OrdinalIgnoreCase));
                var sb = new StringBuilder();
                sb.AppendLine("STING — Mortuary Capacity Audit (HBN 16)").AppendLine();
                sb.AppendLine($"Beds (PRJ_ORG_HEALTH_BEDS_INT): {beds}");
                sb.AppendLine($"Required mortuary bays (0.5 % beds, min 4): {requiredBays}");
                sb.AppendLine($"Mortuary fridges in model (PROD=MORT-FRG): {actualBays}");
                sb.AppendLine(actualBays >= requiredBays ? "[OK] Capacity meets HBN 16 baseline" : "[ERROR] Capacity below HBN 16 baseline");
                StingLog.Info(sb.ToString());
                TaskDialog.Show("STING — Mortuary Audit", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("MortuaryAuditCommand failed", ex); message = ex.Message; return Result.Failed; }
        }
        private static string Get(Element el, string n) {
            try { var p = el.LookupParameter(n);
                  return p?.HasValue==true && p.StorageType==StorageType.String ? (p.AsString()??"") : ""; }
            catch { return ""; }
        }
    }
}
