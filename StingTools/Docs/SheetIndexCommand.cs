using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace StingTools.Docs
{
    /// <summary>
    /// Generate a sheet index schedule with revision tracking.
    /// Creates a ViewSchedule listing all sheets with their current revision status.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetIndexCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = ParameterHelpers.GetApp(commandData).ActiveUIDocument.Document;

            // Check if a sheet index schedule already exists
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(s => s.Name == "STING - Sheet Index");

            if (existing != null)
            {
                TaskDialog.Show("Sheet Index",
                    "Sheet index schedule 'STING - Sheet Index' already exists.\n" +
                    "Delete it first to recreate.");
                return Result.Succeeded;
            }

            using (Transaction tx = new Transaction(doc, "Create Sheet Index"))
            {
                tx.Start();

                ElementId catId = new ElementId(BuiltInCategory.OST_Sheets);
                ViewSchedule schedule = ViewSchedule.CreateSchedule(doc, catId);
                schedule.Name = "STING - Sheet Index";

                // Add fields: Sheet Number, Sheet Name, Drawn By, Checked By
                var fields = schedule.Definition;
                var schedulableFields = fields.GetSchedulableFields();

                foreach (var sf in schedulableFields)
                {
                    string name = sf.GetName(doc);
                    if (name == "Sheet Number" || name == "Sheet Name" ||
                        name == "Drawn By" || name == "Checked By" ||
                        name == "Current Revision")
                    {
                        fields.AddField(sf);
                    }
                }

                // Sort by sheet number
                if (fields.GetFieldCount() > 0)
                {
                    var sortField = fields.GetField(0);
                    var sortGroupField = new ScheduleSortGroupField(sortField.FieldId);
                    fields.AddSortGroupField(sortGroupField);
                }

                tx.Commit();
            }

            TaskDialog.Show("Sheet Index",
                "Created schedule 'STING - Sheet Index' successfully.");
            return Result.Succeeded;
        }
    }
}
