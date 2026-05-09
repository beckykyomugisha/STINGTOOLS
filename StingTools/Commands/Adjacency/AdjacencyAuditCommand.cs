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

                // Adjacency targets.
                var adjFindings = new AdjacencyValidator().Validate(doc);
                sb.AppendLine($"HBN adjacency findings: {adjFindings.Count}");
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
