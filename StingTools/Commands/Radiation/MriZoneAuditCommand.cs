// Healthcare Pack H-9 — MRI suite Z1..Z4 + 5-Gauss audit command.
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Radiation;
using System;
using System.Text;

namespace StingTools.Commands.Radiation
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MriZoneAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var findings = MriZoneEngine.Audit(doc);
                var sb = new StringBuilder();
                sb.AppendLine("STING — MRI Suite Audit").AppendLine();
                if (findings.Count == 0)
                    sb.AppendLine("OK — no MRI zoning issues detected.");
                foreach (var f in findings)
                    sb.AppendLine($"[{f.Severity,-7}] {f.Code}  {f.Message}");
                StingLog.Info(sb.ToString());
                TaskDialog.Show("STING — MRI Audit", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MriZoneAuditCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
