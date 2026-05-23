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

                // Hc.Specialist.Hbo.* overrides:
                //   Mode             → "HBO" / "Cytotoxic" / "IVF" — narrows the
                //                       sub-audit. Default HBO preserves history.
                //   RequireNfpa14    → when false, suppresses the per-chamber
                //                       NFPA 99 Ch.14 verification line.
                string mode = (HcOptions.HboMode ?? "HBO");
                bool requireNfpa = HcOptions.HboRequireNfpa14;
                bool wantHbo  = string.Equals(mode, "HBO",       StringComparison.OrdinalIgnoreCase);
                bool wantCyto = string.Equals(mode, "Cytotoxic", StringComparison.OrdinalIgnoreCase);
                bool wantIvf  = string.Equals(mode, "IVF",       StringComparison.OrdinalIgnoreCase);

                var clinicalCats = new ElementMulticategoryFilter(new[] {
                    BuiltInCategory.OST_MedicalEquipment,
                    BuiltInCategory.OST_NurseCallDevices,
                    BuiltInCategory.OST_SpecialityEquipment });

                var sb = new StringBuilder();
                sb.AppendLine($"STING — Specialist Audit (mode {mode}{(requireNfpa ? "" : ", NFPA 99 Ch.14 lenient")})").AppendLine();

                if (wantHbo)
                {
                    var hboChambers = new FilteredElementCollector(doc).WherePasses(clinicalCats)
                        .WhereElementIsNotElementType().ToElements()
                        .Where(e => Get(e,"ASS_PRODCT_COD_TXT")=="HBO").ToList();
                    sb.AppendLine($"HBO chambers detected: {hboChambers.Count}");
                    if (requireNfpa)
                        foreach (var c in hboChambers)
                            sb.AppendLine($"  {c.Name} — verify NFPA 99 Ch.14 deluge sprinkler + O2 sensor + 3 m fire envelope");
                }
                if (wantCyto)
                {
                    var cytoHoods = new FilteredElementCollector(doc).WherePasses(clinicalCats)
                        .WhereElementIsNotElementType().ToElements()
                        .Where(e => Get(e,"CEQ_CATEGORY_TXT")=="PHARMACY"
                                 && Get(e,"ASS_PRODCT_COD_TXT")=="ISO-USP").ToList();
                    sb.AppendLine($"Cytotoxic / pharmacy isolators: {cytoHoods.Count}");
                }
                if (wantIvf)
                {
                    var ivf = new FilteredElementCollector(doc).WherePasses(clinicalCats)
                        .WhereElementIsNotElementType().ToElements()
                        .Where(e => Get(e,"CEQ_CATEGORY_TXT")=="IVF").ToList();
                    sb.AppendLine($"IVF equipment detected: {ivf.Count}");
                }
                StingLog.Info(sb.ToString());
                TaskDialog.Show("STING — Specialist Audit", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("HboAuditCommand failed", ex); message = ex.Message; return Result.Failed; }
        }
        private static string Get(Element el, string n) {
            try { var p = el.LookupParameter(n); return p?.HasValue==true && p.StorageType==StorageType.String ? (p.AsString()??"") : ""; }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; }
        }
    }
}
