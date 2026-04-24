// StingTools — Drawing Template Manager · Week 3
//
// DrawingBrowserOrganizer activates a Project Browser Organization
// that groups views and sheets by STING_DRAWING_TYPE_ID_TXT. The
// Revit public API does NOT expose BrowserOrganization creation
// (that lives in the UI), so this command's job is:
//
//   1. Find existing BrowserOrganization(s) named 'STING - by
//      Drawing Type' — both the Views and Sheets variants share
//      the same name in their respective lists.
//   2. Activate whichever are found via SetAsCurrent().
//   3. Report stamped-vs-total counts so the user sees how many
//      views still need a DrawingType stamp.
//
// If the organizations don't yet exist the command shows step-by-
// step instructions for creating them once in Revit's Browser
// Organization dialog — a one-time manual setup per project
// template. Re-running after that setup flips them to current.

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

                // Enumerate BrowserOrganization instances — the Views
                // variant and the Sheets variant are both derived
                // BrowserOrganization elements, distinguishable only by
                // which active slot they sit in. Match by name.
                var matches = new List<BrowserOrganization>();
                try
                {
                    foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(BrowserOrganization)))
                        if (el is BrowserOrganization bo
                            && string.Equals(bo.Name, ORG_NAME, StringComparison.OrdinalIgnoreCase))
                            matches.Add(bo);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"BrowserOrganizer: enumerate failed — {ex.Message}");
                }

                int activated = 0;
                if (matches.Count > 0)
                {
                    using (var tx = new Transaction(doc, "STING — Activate Browser Organization"))
                    {
                        tx.Start();
                        foreach (var bo in matches)
                        {
                            try { bo.SetAsCurrent(); activated++; }
                            catch (Exception ex) { StingLog.Warn($"SetAsCurrent '{bo.Name}': {ex.Message}"); }
                        }
                        tx.Commit();
                    }
                }

                var (stamped, total) = CountStamped(doc);
                var body = new StringBuilder();

                if (activated > 0)
                {
                    body.AppendLine($"Activated {activated} '{ORG_NAME}' organization(s).");
                    body.AppendLine();
                }
                else
                {
                    body.AppendLine($"No BrowserOrganization named '{ORG_NAME}' found.");
                    body.AppendLine();
                    body.AppendLine("One-time setup — create it in Revit's UI:");
                    body.AppendLine("  1. Right-click the Project Browser → Browser Organization…");
                    body.AppendLine("  2. Click 'New…' on the Views tab; name it");
                    body.AppendLine($"     '{ORG_NAME}'.");
                    body.AppendLine("  3. Grouping and Sorting tab → add field");
                    body.AppendLine("     STING_DRAWING_TYPE_ID_TXT.");
                    body.AppendLine("  4. Repeat on the Sheets tab with the same name.");
                    body.AppendLine("  5. Click OK; then re-run this command to activate both.");
                    body.AppendLine();
                }

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
