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
                // Worked example: CT 64-slice secondary barrier per NCRP 147 Tables A.4/A.5
                double workload = 600.0;     // mA·min/wk DLP-equivalent (high-throughput)
                double useFactor = 0.25;     // secondary barrier
                double occFactor = 0.5;      // staff lounge adjacent
                double distance = 3.0;       // m
                int kVp = 150;
                double providedPb = 2.0;
                var calc = NCRP147Calculator.Compute("SECONDARY", "CONTROLLED",
                    workload, useFactor, occFactor, distance, kVp, providedPb);
                var sb = new StringBuilder();
                sb.AppendLine("STING — NCRP 147 CT Room Secondary Barrier (worked example)");
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
