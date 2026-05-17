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
                sb.AppendLine($"Diversified zone loads computed: {loads.Count}");
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
    }
}
