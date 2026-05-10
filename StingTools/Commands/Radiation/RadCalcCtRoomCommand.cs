// Healthcare Pack H-9 — NCRP 147 CT room secondary-barrier estimate.
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Standards.NCRP147;
using System;
using System.Text;

namespace StingTools.Commands.Radiation
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RadCalcCtRoomCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Hc.Rad.* overrides come from the Healthcare tab → Radiation
                // inputs grid. Panel defaults match the historic worked example
                // (NCRP 147 Tables A.4 / A.5: kVp 150, W 600, U 0.25, T 0.5, d 3 m).
                double workload  = HcOptions.RadW;
                double useFactor = HcOptions.RadU;
                double occFactor = HcOptions.RadT;
                double distance  = HcOptions.RadD;
                int    kVp       = (int)HcOptions.RadKvp;
                string area      = string.Equals(HcOptions.RadArea, "Uncontrolled",
                                       StringComparison.OrdinalIgnoreCase) ? "UNCONTROLLED" : "CONTROLLED";
                double providedPb = 2.0;
                var calc = NCRP147Calculator.Compute("SECONDARY", area,
                    workload, useFactor, occFactor, distance, kVp, providedPb);
                var sb = new StringBuilder();
                sb.AppendLine($"STING — NCRP 147 CT Room Secondary Barrier ({area})");
                sb.AppendLine();
                sb.AppendLine($"Workload W={workload} mA·min/wk DLP-equiv");
                sb.AppendLine($"Use factor U={useFactor}, Occupancy T={occFactor}");
                sb.AppendLine($"Distance d={distance} m, kVp={kVp}");
                sb.AppendLine($"Design goal P={calc.DesignGoalMGyPerWeek:F4} mGy/wk (CONTROLLED)");
                sb.AppendLine($"Required Pb≈{calc.LeadMmRequired:F2} mm");
                sb.AppendLine($"Provided Pb={providedPb:F2} mm — {(calc.Sufficient ? "SUFFICIENT" : "UNDER")}");
                sb.AppendLine();
                sb.AppendLine(calc.Note);
                StingLog.Info(sb.ToString());
                TaskDialog.Show("STING — Rad CT Room Calc", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("RadCalcCtRoomCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
