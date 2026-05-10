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
                // Hc.Rad.* overrides come from the Healthcare tab → Radiation
                // sub-tab inputs grid. Defaults match the historic worked
                // example (NCRP 147 §A.1) so callers who never opened the dock
                // get the same numbers as before.
                double workload  = HcOptions.RadW;     // 50 default
                double useFactor = HcOptions.RadU;     // 1.0
                double occFactor = HcOptions.RadT;     // 1.0
                double distance  = HcOptions.RadD;     // 2 m
                int    kVp       = (int)HcOptions.RadKvp;  // 125
                string area      = string.Equals(HcOptions.RadArea, "Controlled",
                                       StringComparison.OrdinalIgnoreCase) ? "CONTROLLED" : "UNCONTROLLED";
                double providedPb = 1.6;      // mm typical
                var calc = NCRP147Calculator.Compute("PRIMARY", area,
                    workload, useFactor, occFactor, distance, kVp, providedPb);
                var sb = new StringBuilder();
                sb.AppendLine($"STING — NCRP 147 Chest Room Shielding ({area})");
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
