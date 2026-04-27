// Phase 139.2 N — Manufacturer catalogue auto-populate command.
//
// Walks every loaded FamilySymbol and harvests the MK_* shared
// parameters into STING_MANUFACTURER_CATALOGUE.json so dimension
// data lives in family content, not in C#.

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core.Placement;

namespace StingTools.Commands.Placement
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ManufacturerCatalogueAutoPopulateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData?.Application?.ActiveUIDocument?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            var (created, updated, contributing) = ManufacturerCatalogueRegistry.AutoPopulateFromFamilies(doc);

            string body = $"Catalogue updated.\n\n{created} new entries added\n{updated} existing entries updated";
            if (contributing != null && contributing.Count > 0)
            {
                body += "\n\nContributing families:\n  " + string.Join("\n  ",
                    contributing.Take(20));
                if (contributing.Count > 20)
                    body += $"\n  + {contributing.Count - 20} more";
            }
            else
            {
                body += "\n\nNo families carrying MK_* shared parameters were found in this project.";
            }

            TaskDialog.Show("STING - Manufacturer Catalogue", body);
            return Result.Succeeded;
        }
    }
}
