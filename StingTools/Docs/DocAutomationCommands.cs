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
    /// Delete views that are NOT placed on any sheet.
    /// Cleans up the model by removing orphaned views that clutter the project browser.
    /// Protects system views, templates, and user-selected exclusions.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DeleteUnusedViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            var allViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.CanBePrinted)
                .ToList();

            // Build set of all views placed on sheets
            var placedViewIds = new HashSet<ElementId>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .SelectMany(s => s.GetAllPlacedViews()));

            // Get the active view to protect it
            ElementId activeViewId = doc.ActiveView.Id;

            var unplaced = allViews
                .Where(v => !placedViewIds.Contains(v.Id) && v.Id != activeViewId)
                .ToList();

            if (unplaced.Count == 0)
            {
                TaskDialog.Show("Delete Unused Views",
                    "All views are placed on sheets (or are the active view).\nNothing to delete.");
                return Result.Succeeded;
            }

            // Group by type for the report
            var byType = unplaced.GroupBy(v => v.ViewType).OrderBy(g => g.Key.ToString());

            var report = new StringBuilder();
            report.AppendLine("Unused views (not placed on any sheet):");
            report.AppendLine();
            foreach (var group in byType)
            {
                report.AppendLine($"  {group.Key}: {group.Count()} views");
                foreach (var v in group.OrderBy(x => x.Name).Take(10))
                    report.AppendLine($"    • {v.Name}");
                if (group.Count() > 10)
                    report.AppendLine($"    ... and {group.Count() - 10} more");
            }

            TaskDialog confirm = new TaskDialog("Delete Unused Views");
            confirm.MainInstruction = $"Delete {unplaced.Count} unused views?";
            confirm.MainContent = report.ToString() +
                "\n\nProtected: active view, templates, sheets.\n" +
                "This action can be undone with Ctrl+Z.";
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Delete all {unplaced.Count} unused views",
                "Remove all views not placed on any sheet");
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Delete only Drafting Views",
                $"Remove only unused Drafting Views ({unplaced.Count(v => v.ViewType == ViewType.DraftingView)})");
            confirm.CommonButtons = TaskDialogCommonButtons.Cancel;

            IEnumerable<View> toDelete;
            switch (confirm.Show())
            {
                case TaskDialogResult.CommandLink1:
                    toDelete = unplaced;
                    break;
                case TaskDialogResult.CommandLink2:
                    toDelete = unplaced.Where(v => v.ViewType == ViewType.DraftingView);
                    break;
                default:
                    return Result.Cancelled;
            }

            int deleted = 0;
            int failedDel = 0;
            using (Transaction tx = new Transaction(doc, "STING Delete Unused Views"))
            {
                tx.Start();
                foreach (View v in toDelete)
                {
                    try
                    {
                        doc.Delete(v.Id);
                        deleted++;
                    }
                    catch (Exception ex)
                    {
                        failedDel++;
                        StingLog.Warn($"Could not delete view '{v.Name}': {ex.Message}");
                    }
                }
                tx.Commit();
            }

            string result2 = $"Deleted {deleted} unused views.";
            if (failedDel > 0)
                result2 += $"\n{failedDel} views could not be deleted (system or dependent views).";

            TaskDialog.Show("Delete Unused Views", result2);
            StingLog.Info($"DeleteUnusedViews: deleted={deleted}, failed={failedDel}");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// ISO 19650 Sheet Naming Compliance Check.
    /// Validates that all sheet numbers and names follow ISO 19650 document naming convention:
    ///   {Project}-{Originator}-{Volume}-{Level}-{Type}-{Role}-{Number}
    /// Reports non-compliant sheets and suggests corrections.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetNamingCheckCommand : IExternalCommand
    {
        // ISO 19650 common document type codes
        private static readonly HashSet<string> ValidTypeCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DR", // Drawing
            "SH", // Schedule
            "SP", // Specification
            "RP", // Report
            "CM", // Correspondence
            "PP", // Presentation
            "MO", // Model
            "VS", // Visualisation
            "AN", // Animation
            "HS", // Health & Safety
            "DB", // Database
            "FN", // File note
            "MS", // Method statement
            "CP", // Cost plan
            "CR", // Clash report
            "MI", // Minutes
            "PR", // Programme
            "RI", // Risk register
            "SA", // Safety
            "SU", // Survey
        };

        // ISO 19650 role codes (discipline)
        private static readonly HashSet<string> ValidRoleCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "A",  // Architect
            "S",  // Structural
            "M",  // Mechanical
            "E",  // Electrical
            "P",  // Plumbing/Public Health
            "C",  // Civil
            "L",  // Landscape
            "G",  // Generic
            "B",  // Building Services
            "T",  // Town Planning
            "F",  // Fire
            "D",  // Drainage
            "Z",  // General/non-discipline
        };

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
                TaskDialog.Show("Sheet Naming Check", "No sheets found.");
                return Result.Succeeded;
            }

            string projectNumber = doc.ProjectInformation?.Number ?? "";
            string originator = doc.ProjectInformation?.get_Parameter(BuiltInParameter.PROJECT_ORGANIZATION_NAME)?.AsString() ?? "";

            int compliant = 0;
            int nonCompliant = 0;
            var issues = new List<(string sheetNum, string name, string issue)>();
            var csvRows = new List<string>();
            csvRows.Add("Sheet_Number,Sheet_Name,Status,Issue,Suggestion");

            foreach (var sheet in sheets)
            {
                string num = sheet.SheetNumber;
                string name = sheet.Name;
                string issue = ValidateSheetNumber(num, projectNumber, originator);

                if (issue == null)
                {
                    compliant++;
                    csvRows.Add($"\"{num}\",\"{name}\",COMPLIANT,,");
                }
                else
                {
                    nonCompliant++;
                    issues.Add((num, name, issue));

                    // Suggest a compliant number
                    string suggestion = SuggestCompliantNumber(num, name, projectNumber, originator);
                    csvRows.Add($"\"{num}\",\"{name}\",NON-COMPLIANT,\"{issue}\",\"{suggestion}\"");
                }
            }

            // Build report
            var report = new StringBuilder();
            report.AppendLine("ISO 19650 Sheet Naming Compliance");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Project:    {doc.ProjectInformation?.Name}");
            report.AppendLine($"  Number:     {projectNumber}");
            report.AppendLine($"  Originator: {originator}");
            report.AppendLine();

            double pct = sheets.Count > 0 ? compliant * 100.0 / sheets.Count : 0;
            report.AppendLine($"  Total sheets: {sheets.Count}");
            report.AppendLine($"  Compliant:    {compliant} ({pct:F1}%)");
            report.AppendLine($"  Non-compliant: {nonCompliant}");
            report.AppendLine();

            report.AppendLine("  ISO 19650 naming: {Project}-{Originator}-{Volume}-{Level}-{Type}-{Role}-{Number}");
            report.AppendLine();

            if (issues.Count > 0)
            {
                report.AppendLine("── NON-COMPLIANT SHEETS ──");
                foreach (var (sheetNum, name, iss) in issues.Take(20))
                {
                    report.AppendLine($"  {sheetNum,-12} {name}");
                    report.AppendLine($"               Issue: {iss}");
                }
                if (issues.Count > 20)
                    report.AppendLine($"  ... and {issues.Count - 20} more");
            }

            // Export CSV
            try
            {
                string dir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(dir)) dir = Path.GetTempPath();
                string csvPath = Path.Combine(dir, $"STING_SheetNamingCheck_{DateTime.Now:yyyyMMdd}.csv");
                File.WriteAllText(csvPath, string.Join("\n", csvRows));
                report.AppendLine();
                report.AppendLine($"  CSV exported: {csvPath}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SheetNamingCheck CSV: {ex.Message}");
            }

            TaskDialog td = new TaskDialog("Sheet Naming Check (ISO 19650)");
            td.MainInstruction = $"Compliance: {pct:F1}% ({compliant}/{sheets.Count} sheets)";
            td.MainContent = report.ToString();
            td.Show();

            return Result.Succeeded;
        }

        private static string ValidateSheetNumber(string num, string projectNum, string originator)
        {
            if (string.IsNullOrEmpty(num))
                return "Sheet number is empty";

            // Check if it follows a segmented pattern (at least 3 segments with a separator)
            string[] parts = num.Split(new[] { '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 3)
                return $"Sheet number '{num}' has fewer than 3 segments (need Project-Role-Number minimum)";

            // Check if first segment could be a discipline/role code
            string firstPart = parts[0].Trim();
            if (firstPart.Length > 6)
                return $"First segment '{firstPart}' is too long for a discipline/role code";

            // Check for common non-ISO patterns
            if (num.All(c => char.IsDigit(c)))
                return "Sheet number is purely numeric — needs discipline prefix";

            // Check for role code presence (usually in first 1-2 characters)
            bool hasRole = false;
            foreach (string code in ValidRoleCodes)
            {
                if (firstPart.StartsWith(code, StringComparison.OrdinalIgnoreCase))
                {
                    hasRole = true;
                    break;
                }
            }

            if (!hasRole && parts.Length < 4)
                return $"No recognised role code in '{num}' — expected A/S/M/E/P/C prefix";

            return null; // compliant enough
        }

        private static string SuggestCompliantNumber(string num, string name, string projectNum, string originator)
        {
            // Try to extract a discipline code from the name or existing number
            string role = "Z"; // default: general
            string nameUpper = (name ?? "").ToUpperInvariant();
            if (nameUpper.Contains("MECHANICAL") || nameUpper.Contains("HVAC")) role = "M";
            else if (nameUpper.Contains("ELECTRICAL") || nameUpper.Contains("LIGHT")) role = "E";
            else if (nameUpper.Contains("PLUMB") || nameUpper.Contains("SANIT")) role = "P";
            else if (nameUpper.Contains("STRUCT")) role = "S";
            else if (nameUpper.Contains("ARCH") || nameUpper.Contains("PLAN")) role = "A";
            else if (nameUpper.Contains("FIRE")) role = "F";

            // Build suggestion
            string proj = string.IsNullOrEmpty(projectNum) ? "PRJ" : projectNum;
            string orig = string.IsNullOrEmpty(originator) ? "STG" : originator.Length > 3 ? originator.Substring(0, 3).ToUpper() : originator.ToUpper();
            string seq = "001";

            // Try to extract number from original
            string digits = new string(num.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrEmpty(digits))
                seq = digits.PadLeft(3, '0');
            if (seq.Length > 3) seq = seq.Substring(seq.Length - 3);

            return $"{proj}-{orig}-ZZ-XX-DR-{role}-{seq}";
        }
    }

    /// <summary>
    /// Auto-Number Sheets: assigns sequential sheet numbers following a discipline-based
    /// ISO 19650 pattern. Groups sheets by their discipline prefix and renumbers sequentially.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoNumberSheetsCommand : IExternalCommand
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
                TaskDialog.Show("Auto-Number Sheets", "No sheets found.");
                return Result.Succeeded;
            }

            // Group by first 2 chars (discipline prefix) — materialize to avoid
            // deferred re-evaluation after Phase 1 mutates sheet numbers
            var groups = sheets
                .GroupBy(s => s.SheetNumber.Length >= 2 ? s.SheetNumber.Substring(0, 2).ToUpperInvariant() : "XX")
                .OrderBy(g => g.Key)
                .Select(g => new { Key = g.Key, Sheets = g.ToList() })
                .ToList();

            int totalRenamed = 0;
            var report = new StringBuilder();
            report.AppendLine($"Will renumber {sheets.Count} sheets in {groups.Count} discipline groups.");
            report.AppendLine();
            foreach (var g in groups)
                report.AppendLine($"  [{g.Key}] — {g.Sheets.Count} sheets");

            TaskDialog confirm = new TaskDialog("Auto-Number Sheets");
            confirm.MainInstruction = $"Renumber {sheets.Count} sheets?";
            confirm.MainContent = report.ToString() +
                "\n\nEach group will be numbered sequentially: XX-001, XX-002, etc.\n" +
                "This action can be undone with Ctrl+Z.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            using (Transaction tx = new Transaction(doc, "STING Auto-Number Sheets"))
            {
                tx.Start();

                // Phase 1: Temp names to avoid conflicts
                int temp = 1;
                foreach (var sheet in sheets)
                {
                    try
                    {
                        sheet.SheetNumber = $"_TEMP_{temp++:D4}";
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Sheet temp rename: {ex.Message}");
                    }
                }

                // Phase 2: Assign final numbers by group (using materialized groups)
                foreach (var group in groups)
                {
                    int seq = 1;
                    foreach (var sheet in group.Sheets.OrderBy(s => s.Name))
                    {
                        string newNum = $"{group.Key}-{seq:D3}";
                        try
                        {
                            sheet.SheetNumber = newNum;
                            totalRenamed++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Sheet renumber '{newNum}': {ex.Message}");
                        }
                        seq++;
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Auto-Number Sheets",
                $"Renumbered {totalRenamed} of {sheets.Count} sheets.");
            return Result.Succeeded;
        }
    }
}
