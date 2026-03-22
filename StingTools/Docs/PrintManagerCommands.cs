using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Select;
using StingTools.UI;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  G12: Enhanced Print Manager Commands
    //
    //  Batch PDF export with discipline grouping, naming convention enforcement,
    //  sheet set management, and revision-aware printing.
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Internal Engine: PrintManagerEngine ──

    internal static class PrintManagerEngine
    {
        /// <summary>Get all printable sheets grouped by discipline prefix.</summary>
        internal static Dictionary<string, List<ViewSheet>> GetSheetsByDiscipline(Document doc)
        {
            var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>().OrderBy(s => s.SheetNumber).ToList();

            var groups = new Dictionary<string, List<ViewSheet>>(StringComparer.OrdinalIgnoreCase);
            foreach (var sheet in sheets)
            {
                string prefix = GetDisciplinePrefix(sheet.SheetNumber);
                if (!groups.ContainsKey(prefix)) groups[prefix] = new List<ViewSheet>();
                groups[prefix].Add(sheet);
            }
            return groups;
        }

        /// <summary>Extract discipline prefix from sheet number (e.g., "M-001" → "M").</summary>
        internal static string GetDisciplinePrefix(string sheetNumber)
        {
            if (string.IsNullOrWhiteSpace(sheetNumber)) return "Other";
            int dashIdx = sheetNumber.IndexOf('-');
            if (dashIdx > 0) return sheetNumber.Substring(0, dashIdx).ToUpperInvariant();
            // Try first letter(s)
            string letters = new string(sheetNumber.TakeWhile(char.IsLetter).ToArray());
            return string.IsNullOrEmpty(letters) ? "Other" : letters.ToUpperInvariant();
        }

        /// <summary>Generate ISO 19650-compliant PDF filename.</summary>
        internal static string GetISOFileName(ViewSheet sheet, string projectCode, string revision)
        {
            string number = (sheet.SheetNumber ?? "").Replace("/", "-").Replace("\\", "-");
            string name = (sheet.Name ?? "").Replace("/", "-").Replace("\\", "-");
            // Truncate name to keep filename manageable
            if (name.Length > 50) name = name.Substring(0, 50);

            if (!string.IsNullOrEmpty(projectCode) && !string.IsNullOrEmpty(revision))
                return $"{projectCode}-{number}-{name}-{revision}.pdf";
            return $"{number}-{name}.pdf";
        }

        /// <summary>Export sheets to PDF using Revit's built-in PDF exporter.</summary>
        internal static PrintResult ExportToPDF(Document doc, List<ViewSheet> sheets, string outputDir,
            string projectCode, string revision, bool groupByDiscipline)
        {
            var result = new PrintResult();
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            foreach (var sheet in sheets)
            {
                try
                {
                    string subDir = outputDir;
                    if (groupByDiscipline)
                    {
                        string disc = GetDisciplinePrefix(sheet.SheetNumber);
                        subDir = Path.Combine(outputDir, disc);
                        if (!Directory.Exists(subDir)) Directory.CreateDirectory(subDir);
                    }

                    string fileName = GetISOFileName(sheet, projectCode, revision);
                    string filePath = Path.Combine(subDir, fileName);

                    // Use Revit PDF export
                    var options = new PDFExportOptions
                    {
                        FileName = Path.GetFileNameWithoutExtension(fileName),
                        Combine = false,
                        AlwaysUseRaster = false
                    };

                    var viewIds = new List<ElementId> { sheet.Id };
                    bool success = doc.Export(subDir, viewIds, options);

                    if (success)
                    {
                        result.Exported++;
                        result.ExportedFiles.Add(filePath);
                    }
                    else
                    {
                        result.Failed++;
                        result.Errors.Add($"Failed to export: {sheet.SheetNumber}");
                    }
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add($"{sheet.SheetNumber}: {ex.Message}");
                    StingLog.Warn($"PDF export '{sheet.SheetNumber}': {ex.Message}");
                }
            }

            return result;
        }
    }

    // ── Data types ──

    internal class PrintResult
    {
        public int Exported { get; set; }
        public int Failed { get; set; }
        public List<string> ExportedFiles { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
    }

    #endregion

    #region ── Commands ──

    /// <summary>
    /// Batch PDF export with discipline grouping and ISO 19650 naming.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchPDFExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var sheetGroups = PrintManagerEngine.GetSheetsByDiscipline(doc);
            int totalSheets = sheetGroups.Sum(g => g.Value.Count);
            if (totalSheets == 0)
            {
                TaskDialog.Show("Batch PDF", "No sheets found in the project.");
                return Result.Succeeded;
            }

            // Scope selection
            TaskDialog td = new TaskDialog("Batch PDF Export");
            td.MainInstruction = $"{totalSheets} sheets found across {sheetGroups.Count} disciplines";
            td.MainContent = string.Join("\n", sheetGroups.Select(g => $"  {g.Key}: {g.Value.Count} sheets"));
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"Export ALL {totalSheets} sheets");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Select discipline(s) to export");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Cancel");
            var choice = td.Show();

            List<ViewSheet> sheetsToExport;
            if (choice == TaskDialogResult.CommandLink1)
            {
                sheetsToExport = sheetGroups.Values.SelectMany(v => v).ToList();
            }
            else if (choice == TaskDialogResult.CommandLink2)
            {
                var discItems = sheetGroups.Select(g => new StingListPicker.ListItem { Label = $"{g.Key} ({g.Value.Count} sheets)" }).ToList();
                var picked = StingListPicker.Show("Select Disciplines", "Pick discipline(s) to export:", discItems, true);
                if (picked == null || picked.Count == 0) return Result.Succeeded;

                sheetsToExport = new List<ViewSheet>();
                foreach (var item in picked)
                {
                    string disc = item.Label.Split(' ')[0];
                    if (sheetGroups.TryGetValue(disc, out var sheets))
                        sheetsToExport.AddRange(sheets);
                }
            }
            else return Result.Succeeded;

            // Get output path
            string outputDir = OutputLocationHelper.PromptForExportPath(doc, "Sheets.pdf", "PDF files|*.pdf", "PDF");
            if (string.IsNullOrEmpty(outputDir)) return Result.Succeeded;

            string projectCode = doc.ProjectInformation?.Number ?? "";
            string revision = "";
            try { revision = Core.ParameterHelpers.GetString(doc.ProjectInformation, "ASS_REV_TXT"); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            var progress = StingProgressDialog.Show("PDF Export", sheetsToExport.Count);
            PrintResult result;
            try
            {
                result = PrintManagerEngine.ExportToPDF(doc, sheetsToExport, outputDir, projectCode, revision, true);
                for (int i = 0; i < sheetsToExport.Count && !progress.IsCancelled; i++)
                    progress.Increment($"Exported: {sheetsToExport[i].SheetNumber}");
            }
            finally { progress.Close(); }

            var sb = new StringBuilder();
            sb.AppendLine($"PDF Export Complete\n");
            sb.AppendLine($"  Exported: {result.Exported}");
            sb.AppendLine($"  Failed:   {result.Failed}");
            sb.AppendLine($"  Output:   {outputDir}");
            if (result.Errors.Count > 0)
            {
                sb.AppendLine("\n── Errors ──");
                foreach (var err in result.Errors.Take(10))
                    sb.AppendLine($"  {err}");
            }

            TaskDialog.Show("Batch PDF Export", sb.ToString());
            StingLog.Info($"BatchPDF: {result.Exported} exported, {result.Failed} failed to {outputDir}");
            return Result.Succeeded;
        }
    }

    /// <summary>Show sheet set summary by discipline.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetSetSummaryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var groups = PrintManagerEngine.GetSheetsByDiscipline(ctx.Doc);
            var sb = new StringBuilder();
            sb.AppendLine($"Sheet Set Summary — {groups.Sum(g => g.Value.Count)} sheets\n");
            foreach (var g in groups.OrderBy(g => g.Key))
            {
                sb.AppendLine($"  {g.Key,-5} ({g.Value.Count} sheets)");
                foreach (var sheet in g.Value.Take(5))
                    sb.AppendLine($"         {sheet.SheetNumber,-12} {sheet.Name}");
                if (g.Value.Count > 5) sb.AppendLine($"         ... and {g.Value.Count - 5} more");
            }

            TaskDialog.Show("Sheet Sets", sb.ToString());
            return Result.Succeeded;
        }
    }

    #endregion
}
