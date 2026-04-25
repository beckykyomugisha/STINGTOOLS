// StingTools — Drawing Template Manager · Week 3
//
// DrawingBrowserOrganizer is a diagnostic command — Revit's public
// API neither creates nor activates BrowserOrganization records
// (both operations live in the UI layer). What the command CAN do:
//
//   1. Scan for a BrowserOrganization named 'STING - by Drawing
//      Type' and report whether it's already the current
//      organization for views and sheets.
//   2. Report how many views carry a STING_DRAWING_TYPE_ID_TXT
//      stamp so the user sees propagation progress.
//   3. If the organization is not set up or not current, show the
//      manual steps — right-click Project Browser → Browser
//      Organization → pick it — so the one-time-per-template task
//      is clearly documented inline.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.ReadOnly)]
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

                // Enumerate all BrowserOrganization records; find the
                // ones matching our name (one for Views and one for
                // Sheets both carry the same user-set name).
                var named = new List<BrowserOrganization>();
                try
                {
                    foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(BrowserOrganization)))
                        if (el is BrowserOrganization bo
                            && string.Equals(bo.Name, ORG_NAME, StringComparison.OrdinalIgnoreCase))
                            named.Add(bo);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"BrowserOrganizer: enumerate failed — {ex.Message}");
                }

                // Read current orgs for views + sheets to show the user
                // what's presently active, so they can see whether a
                // manual switch is needed.
                string currentViewsName = "(none)";
                string currentSheetsName = "(none)";
                try
                {
                    var vOrg = BrowserOrganization.GetCurrentBrowserOrganizationForViews(doc);
                    if (vOrg != null) currentViewsName = vOrg.Name;
                }
                catch { /* ignore */ }
                try
                {
                    var sOrg = BrowserOrganization.GetCurrentBrowserOrganizationForSheets(doc);
                    if (sOrg != null) currentSheetsName = sOrg.Name;
                }
                catch { /* ignore */ }

                var (stamped, total) = CountStamped(doc);
                var body = new StringBuilder();

                if (named.Count > 0)
                {
                    body.AppendLine($"Found {named.Count} '{ORG_NAME}' organization(s) in this project.");
                    body.AppendLine();
                    body.AppendLine($"Currently active:");
                    body.AppendLine($"  Views:  {currentViewsName}");
                    body.AppendLine($"  Sheets: {currentSheetsName}");
                    if (!string.Equals(currentViewsName, ORG_NAME, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(currentSheetsName, ORG_NAME, StringComparison.OrdinalIgnoreCase))
                    {
                        body.AppendLine();
                        body.AppendLine($"To switch to '{ORG_NAME}':");
                        body.AppendLine("  1. Right-click the Views or Sheets node in the Project Browser.");
                        body.AppendLine("  2. Choose 'Browser Organization…'.");
                        body.AppendLine($"  3. Tick '{ORG_NAME}' → OK.");
                        body.AppendLine("     (The Revit public API does not expose activation.)");
                    }
                }
                else
                {
                    body.AppendLine($"No BrowserOrganization named '{ORG_NAME}' exists yet.");
                    body.AppendLine();
                    body.AppendLine("One-time setup — create it in Revit's UI:");
                    body.AppendLine("  1. Right-click the Project Browser → Browser Organization…");
                    body.AppendLine("  2. Click 'New…' on the Views tab; name it");
                    body.AppendLine($"     '{ORG_NAME}'.");
                    body.AppendLine("  3. Grouping and Sorting tab → add field");
                    body.AppendLine("     STING_DRAWING_TYPE_ID_TXT.");
                    body.AppendLine("  4. Repeat on the Sheets tab with the same name.");
                    body.AppendLine("  5. Tick it on each tab → OK.");
                }

                body.AppendLine();
                body.AppendLine($"DrawingType stamps: {stamped} / {total} views carry STING_DRAWING_TYPE_ID_TXT.");
                if (stamped < total)
                    body.AppendLine("Run 'Sync Styles' after stamping new views to populate the remainder.");

                TaskDialog.Show("STING — Browser Organizer", body.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingBrowserOrganizer", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }

        private static (int stamped, int total) CountStamped(Document doc)
        {
            int stamped = 0, total = 0;
            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(View)))
                {
                    if (el is View v && !v.IsTemplate)
                    {
                        total++;
                        if (!string.IsNullOrEmpty(DrawingTypeStamper.Read(v))) stamped++;
                    }
                }
            }
            catch { /* best-effort */ }
            return (stamped, total);
        }
    }
}
