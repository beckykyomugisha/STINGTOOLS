// Healthcare Pack H-9 — NCRP 151 LINAC vault primary + leakage stub.
// First-pass estimate; full calc lives with the QE.
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using System;
using System.Text;

namespace StingTools.Commands.Radiation
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RadCalcLinacVaultCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // NCRP 151 worked baseline (≥ 10 MV; concrete primary).
                // Hc.Rad.* re-purpose for LINAC inputs:
                //   RadW (mA·min/wk) → Gy/month workload (the panel slider's
                //                       semantic differs but the user is the
                //                       authority — they enter what they want).
                //   RadKvp           → MV scale; kVp ≥ 1000 ⇒ uses 10 MV TVL,
                //                       else 6 MV TVL (350 mm). This means the
                //                       150-default flips to 6 MV, while users
                //                       intentionally entering 10000 get 10 MV.
                //   RadArea          → CONTROLLED = 5 mGy/yr, else 1 mGy/yr.
                double workload_GyMonth = HcOptions.RadW;            // panel default 600
                double mv               = HcOptions.RadKvp;
                double tvl_concrete     = mv >= 1000 ? 450.0 : 350.0; // mm
                string area             = string.Equals(HcOptions.RadArea, "Uncontrolled",
                                              StringComparison.OrdinalIgnoreCase) ? "uncontrolled" : "controlled";
                double dose             = area == "controlled" ? 5.0 : 1.0;
                double n_required       = Math.Log10(workload_GyMonth * 12 / dose);
                double primaryConcreteMm = n_required * tvl_concrete;
                double mazeAdditionalMm = primaryConcreteMm * 0.4;  // approx scatter/leak
                var sb = new StringBuilder();
                sb.AppendLine($"STING — NCRP 151 LINAC Vault First-Pass ({(mv >= 1000 ? "10 MV" : "6 MV")} baseline, {area})");
                sb.AppendLine();
                sb.AppendLine($"Annual goal P={dose:F1} mGy/yr ({area})");
                sb.AppendLine($"Workload W={workload_GyMonth:F0} Gy/month");
                sb.AppendLine($"Required N (TVLs) = {n_required:F2}");
                sb.AppendLine($"Concrete primary barrier ≈ {primaryConcreteMm:F0} mm");
                sb.AppendLine($"Maze / secondary additional concrete ≈ {mazeAdditionalMm:F0} mm");
                sb.AppendLine();
                sb.AppendLine("Neutron component (≥ 10 MV) requires polyethylene shielding");
                sb.AppendLine("at maze door — handled by Qualified Expert.");
                sb.AppendLine("Output is a draft for QE sign-off — STING does not certify.");
                StingLog.Info(sb.ToString());
                TaskDialog.Show("STING — Rad LINAC Vault Calc", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("RadCalcLinacVaultCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
