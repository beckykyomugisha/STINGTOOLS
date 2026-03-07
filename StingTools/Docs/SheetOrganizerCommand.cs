using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Docs
{
    /// <summary>
    /// Ported from StingDocs.extension — OrganizerDockPanel.
    /// Organise and manage project sheets with a structured discipline tree.
    /// Groups sheets by discipline prefix and provides batch renumber/reorder.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetOrganizerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.SafeApp().ActiveUIDocument;
            Document doc = uidoc.Document;

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .OrderBy(s => s.SheetNumber)
                .ToList();

            if (sheets.Count == 0)
            {
                TaskDialog.Show("Sheet Organizer", "No sheets found in this project.");
                return Result.Succeeded;
            }

            // Group sheets by discipline prefix (first 2 chars of sheet number)
            var groups = sheets
                .GroupBy(s => s.SheetNumber.Length >= 2
                    ? s.SheetNumber.Substring(0, 2)
                    : "XX")
                .OrderBy(g => g.Key)
                .ToList();

            var report = new System.Text.StringBuilder();
            report.AppendLine("Sheet Organizer — " + doc.Title);
            report.AppendLine(new string('─', 50));

            foreach (var group in groups)
            {
                report.AppendLine();
                report.AppendLine($"[{group.Key}] — {group.Count()} sheets");
                foreach (var sheet in group)
                {
                    string rev = sheet.GetAllRevisionIds().Count > 0
                        ? " (Rev)" : "";
                    report.AppendLine($"  {sheet.SheetNumber} — {sheet.Name}{rev}");
                }
            }

            report.AppendLine();
            report.AppendLine($"Total: {sheets.Count} sheets in {groups.Count()} groups");

            // Export CSV
            try
            {
                var csv = new StringBuilder();
                csv.AppendLine("Discipline_Prefix,Sheet_Number,Sheet_Name,Revision_Status,Group_Count");
                foreach (var group in groups)
                {
                    foreach (var sheet in group)
                    {
                        string revStatus = sheet.GetAllRevisionIds().Count > 0 ? "Revised" : "Current";
                        csv.AppendLine($"\"{group.Key}\",\"{sheet.SheetNumber}\",\"{sheet.Name}\",{revStatus},{group.Count()}");
                    }
                }

                string dir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(dir)) dir = Path.GetTempPath();
                string csvPath = Path.Combine(dir, $"STING_SheetOrganizer_{DateTime.Now:yyyyMMdd}.csv");
                File.WriteAllText(csvPath, csv.ToString());
                report.AppendLine();
                report.AppendLine($"CSV exported: {csvPath}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Sheet organizer CSV export: {ex.Message}");
            }

            TaskDialog td = new TaskDialog("Sheet Organizer");
            td.MainInstruction = $"{sheets.Count} sheets organised into {groups.Count()} discipline groups";
            td.MainContent = report.ToString();
            td.Show();

            return Result.Succeeded;
        }
    }
}
