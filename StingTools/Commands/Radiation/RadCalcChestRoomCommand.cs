// Healthcare Pack H-9 — NCRP 147 chest-radiography shielding calc.
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
    public class RadCalcChestRoomCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Worked-example defaults — chest radiographic room per NCRP 147 §A.1.
                double workload = 50.0;       // mA·min/wk patient-typical
                double useFactor = 1.0;       // wall behind chest stand = primary
                double occFactor = 1.0;       // adjacent corridor / waiting
                double distance = 2.0;        // m
                int kVp = 125;
                double providedPb = 1.6;      // mm typical
                var calc = NCRP147Calculator.Compute("PRIMARY", "UNCONTROLLED",
                    workload, useFactor, occFactor, distance, kVp, providedPb);
                var sb = new StringBuilder();
                sb.AppendLine("STING — NCRP 147 Chest Room Shielding (worked example)");
                sb.AppendLine();
                sb.AppendLine($"Workload W={workload} mA·min/wk");
                sb.AppendLine($"Use factor U={useFactor}, Occupancy T={occFactor}");
                sb.AppendLine($"Distance d={distance} m, kVp={kVp}");
                sb.AppendLine($"Design goal P={calc.DesignGoalMGyPerWeek:F4} mGy/wk (UNCONTROLLED)");
                sb.AppendLine($"Required transmission B={calc.TransmissionRequired:E3}");
                sb.AppendLine($"Required Pb≈{calc.LeadMmRequired:F2} mm");
                sb.AppendLine($"Provided Pb={providedPb:F2} mm — {(calc.Sufficient ? "SUFFICIENT" : "UNDER")}");
                sb.AppendLine();
                sb.AppendLine(calc.Note);
                StingLog.Info(sb.ToString());
                TaskDialog.Show("STING — Rad Chest Room Calc", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("RadCalcChestRoomCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
