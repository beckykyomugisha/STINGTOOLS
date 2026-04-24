// StingTools — Drawing Template Manager · Week 3
//
// DrawingBrowserOrganizer writes a Project Browser Organization
// preset that groups views and sheets by
// STING_DRAWING_TYPE_ID_TXT. Effect: as soon as the preset is
// activated, every view stamped by a STING generator collapses
// under its Drawing Type node — all arch-plan-A1-1to100 views in
// one group, all pipe-spool-A1-1to50 in another.
//
// Revit API: BrowserOrganization is read via
// BrowserOrganization.GetCurrentBrowserOrganizationForViews /
// ForSheets, and set via .SetCurrent(id). We construct two new
// organizations ("STING - by Drawing Type" for Views and for
// Sheets) if they don't already exist, then activate them.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DrawingBrowserOrganizerCommand : IExternalCommand
    {
        private const string ORG_NAME = "STING - by Drawing Type";

        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            try
            {
                var doc = data?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { msg = "No document open."; return Result.Failed; }

                using (var tx = new Transaction(doc, "STING — Browser Organization by Drawing Type"))
                {
                    tx.Start();

                    var viewsOrg  = EnsureOrganization(doc, forSheets: false);
                    var sheetsOrg = EnsureOrganization(doc, forSheets: true);
                    if (viewsOrg != null)  BrowserOrganization.SetCurrentBrowserOrganizationForViews(doc, viewsOrg.Id);
                    if (sheetsOrg != null) BrowserOrganization.SetCurrentBrowserOrganizationForSheets(doc, sheetsOrg.Id);

                    tx.Commit();
                }

                var (stamped, total) = CountStamped(doc);
                TaskDialog.Show("STING — Browser Organizer",
                    $"Project Browser now grouped by '{ORG_NAME}'.\n\n" +
                    $"{stamped} of {total} views/sheets currently carry a DrawingType stamp.\n\n" +
                    "Run Sync Styles (Week 4) on unstamped items to populate them.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingBrowserOrganizer", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }

        private static BrowserOrganization EnsureOrganization(Document doc, bool forSheets)
        {
            // Reuse existing if present.
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(BrowserOrganization))
                .Cast<BrowserOrganization>()
                .FirstOrDefault(b => string.Equals(b.Name, ORG_NAME, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;

            // Create new organization keyed off STING_DRAWING_TYPE_ID_TXT.
            try
            {
                var org = forSheets
                    ? BrowserOrganization.CreateSheetOrganization(doc, ORG_NAME)
                    : BrowserOrganization.CreateViewOrganization(doc, ORG_NAME);
                if (org == null) return null;

                // Build a single grouping folder field bound to the shared parameter
                var guid = StingTools.Core.SharedParamGuids.ParamGuids
                    .TryGetValue(DrawingTypeStamper.PARAM_DRAWING_TYPE_ID, out var g) ? g : Guid.Empty;
                if (guid == Guid.Empty)
                {
                    StingLog.Warn($"BrowserOrganizer: '{DrawingTypeStamper.PARAM_DRAWING_TYPE_ID}' not in SharedParamGuids — organization created but ungrouped.");
                    return org;
                }

                var folderField = new FolderItemsParameter(guid);
                var settings = org.GetFolderItems().ToList();
                settings.Clear();
                settings.Add(folderField);
                org.SetFolderItems(settings);
                return org;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BrowserOrganizer.Create({(forSheets ? "sheets" : "views")}): {ex.Message}");
                return null;
            }
        }

        private static (int stamped, int total) CountStamped(Document doc)
        {
            int stamped = 0, total = 0;
            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(View)))
                {
                    if (el is View v && !v.IsTemplate) { total++;
                        if (!string.IsNullOrEmpty(DrawingTypeStamper.Read(v))) stamped++; }
                }
            }
            catch { }
            return (stamped, total);
        }
    }
}
