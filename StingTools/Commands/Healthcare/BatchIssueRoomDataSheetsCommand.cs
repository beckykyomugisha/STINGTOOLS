// Healthcare Pack H-8 — Batch RDS issue command (every clinical room).
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Planscape.Docs.Templates;
using StingTools.Core;
using System;
using System.Linq;
using System.Text;
using System.IO;

namespace StingTools.Commands.Healthcare
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchIssueRoomDataSheetsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType().ToElements()
                    .Where(r => !string.IsNullOrEmpty(GetParam(r, "CLN_ROOM_CLASS_TXT")))
                    .ToList();
                if (rooms.Count == 0)
                {
                    TaskDialog.Show("STING — Batch RDS",
                        "No rooms with CLN_ROOM_CLASS_TXT populated. Run Phase H-1 LoadSharedParams first.");
                    return Result.Succeeded;
                }
                int ok = 0, fail = 0;
                var sb = new StringBuilder();
                foreach (var r in rooms)
                {
                    var path = RdsRenderer.Render(doc, r);
                    if (string.IsNullOrEmpty(path)) { fail++; sb.AppendLine($"FAIL  {r.Name}"); }
                    else { ok++; sb.AppendLine($"OK    {r.Name}  -> {System.IO.Path.GetFileName(path)}"); }
                }
                StingLog.Info($"Batch RDS: ok={ok} fail={fail}\n{sb}");
                TaskDialog.Show("STING — Batch RDS complete",
                    $"Rooms processed: {rooms.Count}\nOK: {ok}\nFailed: {fail}\nDetails in StingTools.log");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BatchIssueRoomDataSheetsCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string GetParam(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || !p.HasValue) return "";
                if (p.StorageType == StorageType.String) return p.AsString() ?? "";
                return p.AsValueString() ?? "";
            } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; }
        }
    }
}
