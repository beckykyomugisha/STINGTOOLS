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
                double dose = 5.0;        // mGy/yr design goal (controlled)
                double workload_GyMonth = 1000.0;
                double tvl_concrete = 450.0;  // mm (10 MV)
                double n_required = Math.Log10(workload_GyMonth * 12 / dose);
                double primaryConcreteMm = n_required * tvl_concrete;
                double mazeAdditionalMm = primaryConcreteMm * 0.4;  // approx scatter/leak
                var sb = new StringBuilder();
                sb.AppendLine("STING — NCRP 151 LINAC Vault First-Pass (10 MV baseline)");
                sb.AppendLine();
                sb.AppendLine($"Annual goal P={dose:F1} mGy/yr (controlled)");
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
