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
    /// Organise views by discipline, type, and level. Reports unplaced views
    /// and suggests cleanup actions.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ViewOrganizerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.SafeApp().ActiveUIDocument.Document;

            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.CanBePrinted)
                .ToList();

            var byType = views
                .GroupBy(v => v.ViewType)
                .OrderBy(g => g.Key.ToString());

            // Build placed-view set once to avoid O(n²) nested collectors
            var placedViewIds = new HashSet<ElementId>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .SelectMany(s => s.GetAllPlacedViews()));

            int placed = views.Count(v => placedViewIds.Contains(v.Id));

            var report = new System.Text.StringBuilder();
            report.AppendLine("View Organizer — " + doc.Title);
            report.AppendLine(new string('─', 50));

            foreach (var group in byType)
            {
                report.AppendLine($"\n{group.Key} — {group.Count()} views");
                foreach (var v in group.OrderBy(x => x.Name).Take(20))
                {
                    report.AppendLine($"  {v.Name}");
                }
                if (group.Count() > 20)
                    report.AppendLine($"  ... and {group.Count() - 20} more");
            }

            report.AppendLine($"\nTotal: {views.Count} views ({placed} on sheets, {views.Count - placed} unplaced)");

            // Export CSV
            try
            {
                var csv = new StringBuilder();
                csv.AppendLine("View_Name,View_Type,On_Sheet");
                foreach (var v in views.OrderBy(x => x.ViewType.ToString()).ThenBy(x => x.Name))
                {
                    string onSheet = placedViewIds.Contains(v.Id) ? "Yes" : "No";
                    csv.AppendLine($"\"{v.Name}\",{v.ViewType},{onSheet}");
                }
                string dir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(dir)) dir = Path.GetTempPath();
                string csvPath = Path.Combine(dir, "STING_View_Organizer.csv");
                File.WriteAllText(csvPath, csv.ToString());
                report.AppendLine($"\nCSV exported: {csvPath}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ViewOrganizer CSV export: {ex.Message}");
            }

            TaskDialog td = new TaskDialog("View Organizer");
            td.MainInstruction = $"{views.Count} views found";
            td.MainContent = report.ToString();
            td.Show();

            return Result.Succeeded;
        }
    }
}
