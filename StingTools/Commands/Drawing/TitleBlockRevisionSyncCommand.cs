// StingTools — TitleBlockRevisionSyncCommand
// Wires TitleBlockRevisionSyncer.SyncAll() to a user-invocable command.
// Syncs revision strip rows (PRJ_TB_REV_COL_n / DATE_n / DESC_n) from
// Revit's Revision sequence onto every STING-stamped sheet's title block.

using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockRevisionSyncCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application?.ActiveUIDocument?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            var result = TitleBlockRevisionSyncer.SyncAll(doc);

            var sb = new StringBuilder();
            sb.AppendLine($"Sheets processed : {result.SheetsProcessed}");
            sb.AppendLine($"Parameters written: {result.ParamsWritten}");
            if (result.SheetsSkipped > 0)
                sb.AppendLine($"Sheets skipped   : {result.SheetsSkipped}");
            if (result.Warnings.Count > 0)
            {
                sb.AppendLine($"\nWarnings ({result.Warnings.Count}):");
                foreach (var w in result.Warnings) sb.AppendLine($"  • {w}");
            }

            TaskDialog.Show("STING — Sync Revision Strip", sb.ToString());
            StingLog.Info($"TitleBlockRevisionSyncCommand: {result.SheetsProcessed} sheets, {result.ParamsWritten} params written.");
            return Result.Succeeded;
        }
    }
}
