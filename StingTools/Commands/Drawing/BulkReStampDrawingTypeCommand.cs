// StingTools — BulkReStampDrawingTypeCommand
// Re-applies a chosen DrawingType profile to every selected sheet
// (or all project sheets when nothing is selected).
// Runs the full DrawingTypePresentation.Apply pipeline per sheet:
// stamp / scale / detail-level / view-template / crop / style-pack / annotation.
// Skips sheets where STING_STYLE_LOCKED_BOOL == 1.

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
    public class BulkReStampDrawingTypeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDoc = commandData.Application?.ActiveUIDocument;
            var doc   = uiDoc?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            // ── 1. Collect target sheets ───────────────────────────────────
            var selectedIds = uiDoc.Selection?.GetElementIds() ?? new List<ElementId>();
            List<ViewSheet> sheets;
            if (selectedIds.Count > 0)
            {
                sheets = selectedIds
                    .Select(id => doc.GetElement(id))
                    .OfType<ViewSheet>()
                    .ToList();
                if (sheets.Count == 0)
                {
                    TaskDialog.Show("STING — Re-Stamp", "No sheets in selection. Select sheets in the Project Browser then run again.");
                    return Result.Cancelled;
                }
            }
            else
            {
                sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .ToList();
            }

            // ── 2. Pick DrawingType profile ────────────────────────────────
            var allTypes = DrawingTypeRegistry.ListAll(doc)
                .OrderBy(d => d.Id)
                .ToList();
            if (allTypes.Count == 0)
            {
                TaskDialog.Show("STING — Re-Stamp", "No DrawingType profiles loaded. Check STING_DRAWING_TYPES.json.");
                return Result.Cancelled;
            }

            var pickerItems = allTypes
                .Select(d => new StingTools.Select.StingListPicker.ListItem
                {
                    Label  = d.Id,
                    Detail = d.Name
                })
                .ToList();

            var picked = StingTools.Select.StingListPicker.Show(
                "Re-Stamp Sheets — Choose DrawingType",
                $"Select a profile to apply to {sheets.Count} sheet(s).",
                pickerItems,
                allowMultiSelect: false);

            if (picked == null || picked.Count == 0)
                return Result.Cancelled;

            var chosenId = picked[0].Label;
            var chosen   = allTypes.FirstOrDefault(d =>
                string.Equals(d.Id, chosenId, StringComparison.OrdinalIgnoreCase));
            if (chosen == null)
            {
                TaskDialog.Show("STING — Re-Stamp", $"Could not resolve DrawingType '{chosenId}'.");
                return Result.Cancelled;
            }

            // ── 3. Apply ───────────────────────────────────────────────────
            int stamped = 0, skipped = 0, failed = 0;
            var warnings = new List<string>();

            using (var tx = new Transaction(doc, $"STING Re-Stamp: {chosen.Id}"))
            {
                tx.Start();
                foreach (var sheet in sheets)
                {
                    try
                    {
                        if (DrawingTypeStamper.IsLocked(sheet))
                        {
                            skipped++;
                            continue;
                        }
                        // Stamp the sheet itself
                        DrawingTypeStamper.Stamp(sheet, chosen.Id);
                        // Apply to views placed on this sheet
                        var viewIds = sheet.GetAllPlacedViews();
                        foreach (var vid in viewIds)
                        {
                            var view = doc.GetElement(vid) as View;
                            if (view == null) continue;
                            var applyResult = DrawingTypePresentation.Apply(doc, view, chosen);
                            if (applyResult?.Warnings?.Count > 0)
                                warnings.AddRange(applyResult.Warnings.Select(w =>
                                    $"{sheet.SheetNumber}/{view.Name}: {w}"));
                        }
                        stamped++;
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"{sheet.SheetNumber}: {ex.Message}");
                        failed++;
                    }
                }
                tx.Commit();
            }

            var msg = $"Profile applied: {chosen.Id}\nSheets stamped: {stamped}";
            if (skipped > 0) msg += $"\nSkipped (locked): {skipped}";
            if (failed  > 0) msg += $"\nFailed: {failed}";
            if (warnings.Count > 0)
                msg += "\n\nWarnings:\n" + string.Join("\n",
                    warnings.Take(10).Select(w => $"  • {w}"));
            TaskDialog.Show("STING — Re-Stamp Complete", msg);
            StingLog.Info($"BulkReStampDrawingTypeCommand: {stamped} stamped, {skipped} skipped, {failed} failed.");
            return Result.Succeeded;
        }
    }
}
