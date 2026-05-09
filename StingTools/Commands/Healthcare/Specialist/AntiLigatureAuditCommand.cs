// Healthcare Pack H-11 — Anti-ligature audit command (HBN 03-01 / FGI Pt 2).
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Validation.Healthcare;
using System;
using System.Text;

namespace StingTools.Commands.Healthcare.Specialist
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AntiLigatureAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var findings = new AntiLigatureValidator().Validate(doc);
                var sb = new StringBuilder();
                sb.AppendLine("STING — Anti-Ligature Audit (HBN 03-01 / FGI Pt 2)").AppendLine();
                sb.AppendLine($"Findings: {findings.Count}");
                foreach (var f in findings)
                    sb.AppendLine($"  [{f.Severity,-7}] {f.Code} {f.Message}");
                StingLog.Info(sb.ToString());
                TaskDialog.Show("STING — Anti-Ligature Audit", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("AntiLigatureAuditCommand failed", ex); message = ex.Message; return Result.Failed; }
        }
    }
}
