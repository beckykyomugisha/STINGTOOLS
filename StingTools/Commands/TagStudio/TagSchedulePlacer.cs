// ============================================================================
// TagSchedulePlacer.cs — put tag-expander schedules onto sheets.
//
// The tag-expander (ScheduleDisciplineTagExpanderCommand) builds one
// ViewSchedule per model category, but a ViewSchedule that never reaches a
// sheet is not a deliverable — it is a table nobody sees. With ~204 spec
// entries carrying sheet_columns, hand-dragging is not a workable answer.
//
// This placer packs schedules onto as many sheets as it takes:
//
//   • a cursor walks DOWN a column from the top-left of the drawable zone
//   • when the next schedule would overflow the column, the cursor moves
//     RIGHT by the widest table in that column (+ gap) and returns to the top
//   • when a column would overflow the zone's right edge, a new sheet is minted
//
// Revit gives no way to ask how big a schedule will render before it is
// placed, so each ScheduleSheetInstance is created at the cursor, the document
// is regenerated, and its bounding box is measured. A table that does not fit
// even on an empty sheet is placed anyway (top-left of a fresh sheet) and
// reported — dropping it silently would be worse than an oversized sheet.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Docs;

namespace StingTools.Commands.TagStudio
{
    /// <summary>Outcome of a sheet-placement run.</summary>
    internal sealed class TagSchedulePlacementResult
    {
        public int Placed;
        public int SheetsCreated;
        public int Oversized;
        public int Failed;
        public readonly List<string> Warnings = new List<string>();
        public readonly List<ElementId> SheetIds = new List<ElementId>();
    }

    internal static class TagSchedulePlacer
    {
        private const string SheetNamePrefix = "STING Tag Schedules";
        private const double GapMm = 8.0;

        private static double MmToFt(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);

        /// <summary>
        /// Place the given schedules onto newly-created sheets. Must be called
        /// inside an open Transaction — the caller owns the transaction so a
        /// build+place run commits as one undo step.
        /// </summary>
        internal static TagSchedulePlacementResult Place(Document doc, IList<ViewSchedule> schedules)
        {
            var result = new TagSchedulePlacementResult();
            if (doc == null || schedules == null || schedules.Count == 0) return result;

            ElementId titleBlockId = ResolveTitleBlock(doc);
            if (titleBlockId == null || titleBlockId == ElementId.InvalidElementId)
            {
                result.Warnings.Add("No title block family is loaded — cannot create sheets to place schedules on.");
                StingLog.Warn("TagSchedulePlacer: no title block type found; placement skipped.");
                return result;
            }

            double gap = MmToFt(GapMm);

            ViewSheet sheet = null;
            DrawableZone zone = null;
            double cursorX = 0, cursorY = 0, columnWidth = 0;

            // Start a fresh sheet and reset the packing cursor to its top-left.
            bool NewSheet()
            {
                sheet = CreateSheet(doc, titleBlockId, result);
                if (sheet == null) return false;
                zone = SheetManagerEngine.GetDrawableZone(doc, sheet);
                if (zone == null || zone.Width <= 0 || zone.Height <= 0)
                {
                    result.Warnings.Add($"Sheet '{sheet.SheetNumber}' has no usable drawable zone.");
                    return false;
                }
                cursorX = zone.Min.X;
                cursorY = zone.Max.Y;
                columnWidth = 0;
                result.SheetIds.Add(sheet.Id);
                return true;
            }

            if (!NewSheet()) return result;

            foreach (var sched in schedules)
            {
                if (sched == null) continue;

                ScheduleSheetInstance ssi = TryCreate(doc, sheet, sched, new XYZ(cursorX, cursorY, 0), result);
                if (ssi == null) { result.Failed++; continue; }

                // Revit only knows the rendered extents once the instance exists.
                doc.Regenerate();
                var size = MeasureSize(ssi, sheet);
                double w = size.Width, h = size.Height;

                bool fitsColumn = h <= 0 || (cursorY - h) >= zone.Min.Y;
                if (!fitsColumn)
                {
                    // Try the next column on this sheet.
                    double nextX = cursorX + Math.Max(columnWidth, w) + gap;
                    if (nextX + w <= zone.Max.X)
                    {
                        cursorX = nextX;
                        cursorY = zone.Max.Y;
                        columnWidth = 0;
                        MoveTo(doc, ssi, new XYZ(cursorX, cursorY, 0), result, sched.Name);
                    }
                    else
                    {
                        // Column and sheet are both full — start a new sheet.
                        // Delete the probe instance first so it does not linger
                        // half-off the old sheet.
                        try { doc.Delete(ssi.Id); }
                        catch (Exception ex) { StingLog.Warn($"TagSchedulePlacer: could not remove probe instance for '{sched.Name}' — {ex.Message}"); }

                        if (!NewSheet()) { result.Failed++; break; }

                        ssi = TryCreate(doc, sheet, sched, new XYZ(cursorX, cursorY, 0), result);
                        if (ssi == null) { result.Failed++; continue; }
                        doc.Regenerate();
                        size = MeasureSize(ssi, sheet);
                        w = size.Width; h = size.Height;

                        // Still too tall for an empty sheet: keep it, flag it.
                        if (h > 0 && h > zone.Height)
                        {
                            result.Oversized++;
                            result.Warnings.Add($"'{sched.Name}' is taller than the sheet and overruns its border.");
                        }
                    }
                }

                result.Placed++;
                columnWidth = Math.Max(columnWidth, w);
                cursorY -= (h > 0 ? h : MmToFt(40)) + gap;
            }

            StingLog.Info($"TagSchedulePlacer: placed={result.Placed}, sheets={result.SheetsCreated}, " +
                          $"oversized={result.Oversized}, failed={result.Failed}");
            return result;
        }

        // ──────────────────────────────────────────────────────────────────

        private static ScheduleSheetInstance TryCreate(Document doc, ViewSheet sheet,
            ViewSchedule sched, XYZ pt, TagSchedulePlacementResult result)
        {
            try
            {
                return ScheduleSheetInstance.Create(doc, sheet.Id, sched.Id, pt);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagSchedulePlacer: ScheduleSheetInstance.Create('{sched.Name}') — {ex.Message}");
                result.Warnings.Add($"Could not place '{sched.Name}': {ex.Message}");
                return null;
            }
        }

        private static void MoveTo(Document doc, ScheduleSheetInstance ssi, XYZ target,
            TagSchedulePlacementResult result, string schedName)
        {
            try
            {
                var delta = target - ssi.Point;
                if (!delta.IsZeroLength()) ElementTransformUtils.MoveElement(doc, ssi.Id, delta);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagSchedulePlacer: move '{schedName}' — {ex.Message}");
                result.Warnings.Add($"Could not reposition '{schedName}': {ex.Message}");
            }
        }

        private static (double Width, double Height) MeasureSize(ScheduleSheetInstance ssi, ViewSheet sheet)
        {
            try
            {
                var bb = ssi.get_BoundingBox(sheet);
                if (bb != null)
                    return (Math.Abs(bb.Max.X - bb.Min.X), Math.Abs(bb.Max.Y - bb.Min.Y));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagSchedulePlacer: bounding box unavailable — {ex.Message}");
            }
            return (0, 0);
        }

        private static ViewSheet CreateSheet(Document doc, ElementId titleBlockId, TagSchedulePlacementResult result)
        {
            try
            {
                var sheet = ViewSheet.Create(doc, titleBlockId);
                if (sheet == null) return null;

                int index = result.SheetsCreated + 1;
                try { sheet.Name = $"{SheetNamePrefix} {index:D2}"; }
                catch (Exception ex) { StingLog.Warn($"TagSchedulePlacer: sheet name — {ex.Message}"); }

                try
                {
                    string number = SheetManagerEngine.GetNextSheetNumber(doc, "TS");
                    if (!string.IsNullOrEmpty(number)) sheet.SheetNumber = number;
                }
                catch (Exception ex) { StingLog.Warn($"TagSchedulePlacer: sheet number — {ex.Message}"); }

                result.SheetsCreated++;
                return sheet;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagSchedulePlacer: ViewSheet.Create — {ex.Message}");
                result.Warnings.Add($"Could not create sheet: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Prefer an A1 title block when one is loaded (tag schedules are wide),
        /// otherwise take whatever title block the project has.
        /// </summary>
        private static ElementId ResolveTitleBlock(Document doc)
        {
            var types = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .ToList();

            if (types.Count == 0) return ElementId.InvalidElementId;

            var a1 = types.FirstOrDefault(t =>
                (t.Name ?? "").IndexOf("A1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (t.FamilyName ?? "").IndexOf("A1", StringComparison.OrdinalIgnoreCase) >= 0);

            return (a1 ?? types[0]).Id;
        }
    }
}
