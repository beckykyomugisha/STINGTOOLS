// Healthcare Pack H-10 — Adjacency + clean/dirty flow audit command.
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Adjacency;
using StingTools.Core.Validation.Healthcare;
using System;
using System.Text;

namespace StingTools.Commands.Adjacency
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AdjacencyAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var sb = new StringBuilder();
                sb.AppendLine("STING — Adjacency + Clean/Dirty Flow Audit").AppendLine();

                // Adjacency targets. Distance threshold derives from the
                // Hc.AdjacencyDepth combo (1..4) as a stand-in for proper
                // door-graph BFS depth (Phase H-10): depth × 10 m.
                int depth = HcOptions.AdjacencyDepth;
                if (depth < 1) depth = 1;
                double distM = depth * 10.0;
                var adj = new AdjacencyValidator
                {
                    MaxMandatoryDistanceM = distM,
                    MinForbiddenDistanceM = distM,
                };
                var adjFindings = adj.Validate(doc);
                sb.AppendLine($"HBN adjacency findings (depth proxy {distM:F0} m): {adjFindings.Count}");
                foreach (var f in adjFindings)
                    sb.AppendLine($"  [{f.Severity,-7}] {f.Code} {f.Message}");
                sb.AppendLine();

                // Clean / dirty flow.
                var flow = CleanDirtyFlowSolver.Audit(doc);
                sb.AppendLine($"Clean/Dirty flow findings: {flow.Count}");
                foreach (var f in flow)
                    sb.AppendLine($"  [{f.Severity,-7}] {f.Code} {f.Message}");

                StingLog.Info(sb.ToString());
                TaskDialog.Show("STING — Adjacency Audit", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("AdjacencyAuditCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
