// Healthcare Pack H-19 — Hyperbaric chamber + cytotoxic / IVF audit (NFPA 99 Ch.14).
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
    public class HboAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var hboChambers = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_SpecialityEquipment)
                    .WhereElementIsNotElementType().ToElements()
                    .Where(e => Get(e,"ASS_PRODCT_COD_TXT")=="HBO").ToList();
                var sb = new StringBuilder();
                sb.AppendLine("STING — Hyperbaric / Cytotoxic / IVF Audit").AppendLine();
                sb.AppendLine($"HBO chambers detected: {hboChambers.Count}");
                foreach (var c in hboChambers)
                    sb.AppendLine($"  {c.Name} — verify NFPA 99 Ch.14 deluge sprinkler + O2 sensor + 3 m fire envelope");
                var cytoHoods = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_SpecialityEquipment)
                    .WhereElementIsNotElementType().ToElements()
                    .Where(e => Get(e,"CEQ_CATEGORY_TXT")=="PHARMACY"
                             && Get(e,"ASS_PRODCT_COD_TXT")=="ISO-USP").ToList();
                sb.AppendLine($"Cytotoxic / pharmacy isolators: {cytoHoods.Count}");
                StingLog.Info(sb.ToString());
                TaskDialog.Show("STING — Specialist Audit", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("HboAuditCommand failed", ex); message = ex.Message; return Result.Failed; }
        }
        private static string Get(Element el, string n) {
            try { var p = el.LookupParameter(n); return p?.HasValue==true && p.StorageType==StorageType.String ? (p.AsString()??"") : ""; }
            catch { return ""; }
        }
    }
}
