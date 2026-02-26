using System;
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
    /// Create ISO 19650-compliant document transmittal records.
    /// Lists all sheets with their revision status for transmittal documentation.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TransmittalCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .OrderBy(s => s.SheetNumber)
                .ToList();

            if (sheets.Count == 0)
            {
                TaskDialog.Show("Document Transmittal", "No sheets found.");
                return Result.Succeeded;
            }

            string projectName = doc.ProjectInformation?.Name ?? "Unnamed";
            string projectNumber = doc.ProjectInformation?.Number ?? "000";

            var report = new StringBuilder();
            report.AppendLine("═══════════════════════════════════════════════");
            report.AppendLine("  DOCUMENT TRANSMITTAL — ISO 19650");
            report.AppendLine("═══════════════════════════════════════════════");
            report.AppendLine($"  Project: {projectName}");
            report.AppendLine($"  Number:  {projectNumber}");
            report.AppendLine($"  Date:    {DateTime.Now:yyyy-MM-dd}");
            report.AppendLine($"  Sheets:  {sheets.Count}");
            report.AppendLine("───────────────────────────────────────────────");

            foreach (var sheet in sheets)
            {
                string revId = sheet.GetAllRevisionIds().Count > 0
                    ? "Rev" : "---";
                report.AppendLine(
                    $"  {sheet.SheetNumber,-12} {sheet.Name,-30} {revId}");
            }

            report.AppendLine("───────────────────────────────────────────────");
            report.AppendLine($"  Total: {sheets.Count} documents");

            // Export CSV
            string csvPath = null;
            try
            {
                var csv = new StringBuilder();
                csv.AppendLine("Sheet_Number,Sheet_Name,Revision_Status,Project,Date");
                foreach (var sheet in sheets)
                {
                    string revStatus = sheet.GetAllRevisionIds().Count > 0 ? "Revised" : "Current";
                    csv.AppendLine($"\"{sheet.SheetNumber}\",\"{sheet.Name}\",{revStatus},\"{projectName}\",{DateTime.Now:yyyy-MM-dd}");
                }

                string dir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(dir)) dir = Path.GetTempPath();
                csvPath = Path.Combine(dir, $"STING_Transmittal_{DateTime.Now:yyyyMMdd}.csv");
                File.WriteAllText(csvPath, csv.ToString());
                report.AppendLine();
                report.AppendLine($"  CSV exported: {csvPath}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Transmittal CSV export: {ex.Message}");
            }

            TaskDialog td = new TaskDialog("Document Transmittal");
            td.MainInstruction = "ISO 19650 Document Transmittal";
            td.MainContent = report.ToString();
            td.Show();

            return Result.Succeeded;
        }
    }
}
