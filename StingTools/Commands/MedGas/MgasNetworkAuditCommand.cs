// Healthcare Pack H-7 — MGPS network audit command.
//
// Read-only diagnostic: walks the MGAS network, emits per-gas terminal
// counts + diversified flow estimates + zone valve box / alarm panel
// summary. Output goes to a TaskDialog and to StingLog.

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.MedGas;
using System;
using System.Linq;
using System.Text;

namespace StingTools.Commands.MedGas
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MgasNetworkAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var net = MgasNetwork.Build(doc);
                var sb = new StringBuilder();
                sb.AppendLine("STING MGPS Network Audit").AppendLine();
                int total = 0;
                foreach (var gas in net.GasCodes)
                {
                    var nodes = net.Nodes[gas];
                    if (nodes.Count == 0) continue;
                    var tuCount  = nodes.Count(n => n.Role == "TU");
                    var zvbCount = nodes.Count(n => n.Role == "ZVB");
                    var aapCount = nodes.Count(n => n.Role == "AAP");
                    var pipeCount = nodes.Count(n => n.Role == "PIPE");
                    sb.AppendLine($"{gas,-6} TU={tuCount,3}  ZVB={zvbCount,2}  AAP={aapCount,2}  PIPE={pipeCount,4}");
                    total += nodes.Count;
                }
                sb.AppendLine().AppendLine($"Total elements in MGPS network: {total}");

                var loads = MgasFlowSolver.Solve(net);
                sb.AppendLine().AppendLine($"Diversified zone loads (NFPA 99 §5.1.13):");
                if (loads.Count == 0)
                {
                    sb.AppendLine("  (no terminal units found)");
                }
                else
                {
                    sb.AppendLine($"  {"Gas",-6} {"Zone",-16} {"TUs",4} {"Σflow",9} {"div",6} {"diversified",12}");
                    foreach (var l in loads.OrderBy(l => l.GasCode).ThenBy(l => l.ZoneRef))
                    {
                        string divTxt = l.DiversityAssumed ? $"{l.Diversity:F2}*" : $"{l.Diversity:F2}";
                        sb.AppendLine($"  {l.GasCode,-6} {Trunc(l.ZoneRef, 16),-16} {l.TerminalCount,4} " +
                                      $"{l.SumDesignFlowLpm,7:F0}lpm {divTxt,6} {l.DiversifiedFlowLpm,9:F0}lpm");
                    }
                    var assumedGases = loads.Where(l => l.DiversityAssumed)
                                            .Select(l => l.GasCode).Distinct().OrderBy(g => g).ToList();
                    if (assumedGases.Count > 0)
                        sb.AppendLine().AppendLine(
                            $"  * diversity assumed 1.0 (no NFPA 99 table entry — over-sizes, safe): "
                            + string.Join(", ", assumedGases));
                }
                StingLog.Info(sb.ToString());
                TaskDialog.Show("STING — MGPS Audit", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MgasNetworkAuditCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string Trunc(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n));
    }
}
