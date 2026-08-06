// ============================================================================
// TagSchedulePlacer.cs — put tag-expander schedules onto sheets.
//
// The tag-expander (ScheduleDisciplineTagExpanderCommand) builds one
// ViewSchedule per model category, but a ViewSchedule that never reaches a
// sheet is not a deliverable — it is a table nobody sees. With ~204 spec
// entries carrying sheet_columns, hand-dragging is not a workable answer.
//
// MEASURE, THEN ARRANGE
// ---------------------
// Revit will not tell you how big a schedule renders until it exists, and
// ScheduleSheetInstance.get_BoundingBox returns nothing usable while the
// creating transaction is still open — regenerating is not enough. Packing
// against that during the build therefore degrades to a guessed row height
// and the tables land on top of each other.
//
// So this runs in three transactions:
//
//   1. MEASURE   every schedule is dropped on one scratch sheet at the same
//                point. Overlap is irrelevant — a bounding box is per element.
//   2. (commit)  sizes become readable.
//   3. ARRANGE   the scratch sheet is deleted and each schedule is re-placed
//                with its true width and height into a packed column layout.
//
// The cost is that build+place is no longer a single undo step. Correct
// output beats a tidy undo stack.
//
// ANCHORING
// ---------
// ScheduleSheetInstance.Point is not necessarily the table's visual top-left,
// so the measure pass also records each instance's Point→bounding-box offset.
// Placement subtracts it, which is what makes every table in a column share
// an exact left edge instead of being within a few millimetres of one.
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
        private const double GapMm = 10.0;

        /// <summary>Measured geometry for one schedule, in feet, sheet space.</summary>
        private sealed class Measured
        {
            public ViewSchedule Schedule;
            public double Width;
            public double Height;
            /// <summary>bbox.Min.X - Point.X — subtracted so left edges line up exactly.</summary>
            public double AnchorDx;
            /// <summary>bbox.Max.Y - Point.Y — subtracted so top edges line up exactly.</summary>
            public double AnchorDy;
        }

        private static double MmToFt(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);

        /// <summary>
        /// Place the given schedules onto new sheets. Opens its own transactions,
        /// so the caller must NOT have one open.
        /// </summary>
        internal static TagSchedulePlacementResult Place(Document doc, IList<ViewSchedule> schedules)
        {
            var result = new TagSchedulePlacementResult();
            if (doc == null || schedules == null || schedules.Count == 0) return result;

            ElementId titleBlockId = ResolveTitleBlock(doc);
            if (titleBlockId == ElementId.InvalidElementId)
            {
                result.Warnings.Add("No title block family is loaded — cannot create sheets to place schedules on.");
                StingLog.Warn("TagSchedulePlacer: no title block type found; placement skipped.");
                return result;
            }

            var measured = Measure(doc, schedules, titleBlockId, result);
            if (measured.Count == 0) return result;

            Arrange(doc, measured, titleBlockId, result);

            StingLog.Info($"TagSchedulePlacer: placed={result.Placed}, sheets={result.SheetsCreated}, " +
                          $"oversized={result.Oversized}, failed={result.Failed}");
            return result;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Pass 1 — measure on a scratch sheet
        // ──────────────────────────────────────────────────────────────────

        private static List<Measured> Measure(Document doc, IList<ViewSchedule> schedules,
            ElementId titleBlockId, TagSchedulePlacementResult result)
        {
            var measured = new List<Measured>();
            ViewSheet scratch = null;
            var probes = new List<(ScheduleSheetInstance Ssi, ViewSchedule Sched)>();

            using (var tx = new Transaction(doc, "STING Measure Tag Schedules"))
            {
                tx.Start();
                try
                {
                    scratch = ViewSheet.Create(doc, titleBlockId);
                    if (scratch == null)
                    {
                        result.Warnings.Add("Could not create the scratch sheet used to measure schedule sizes.");
                        tx.RollBack();
                        return measured;
                    }
                    try { scratch.Name = "STING ~ measuring"; } catch { /* name clash is harmless here */ }

                    foreach (var sched in schedules)
                    {
                        if (sched == null) continue;
                        try
                        {
                            var ssi = ScheduleSheetInstance.Create(doc, scratch.Id, sched.Id, XYZ.Zero);
                            if (ssi != null) probes.Add((ssi, sched));
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"TagSchedulePlacer: measure probe for '{sched.Name}' — {ex.Message}");
                            result.Warnings.Add($"Could not measure '{sched.Name}': {ex.Message}");
                            result.Failed++;
                        }
                    }
                    tx.Commit();   // sizes only become readable once committed
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"TagSchedulePlacer: measure pass failed — {ex.Message}");
                    result.Warnings.Add($"Measuring schedule sizes failed: {ex.Message}");
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    return measured;
                }
            }

            foreach (var (ssi, sched) in probes)
            {
                double w = 0, h = 0, adx = 0, ady = 0;
                try
                {
                    var bb = ssi.get_BoundingBox(scratch);
                    if (bb != null)
                    {
                        w = Math.Abs(bb.Max.X - bb.Min.X);
                        h = Math.Abs(bb.Max.Y - bb.Min.Y);
                        adx = bb.Min.X - ssi.Point.X;
                        ady = bb.Max.Y - ssi.Point.Y;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"TagSchedulePlacer: bounding box for '{sched.Name}' — {ex.Message}");
                }

                if (w <= 0 || h <= 0)
                {
                    // Never silently pack against a zero size — that is exactly
                    // what produces a pile of overlapping tables. Fall back to a
                    // row-count estimate and say so.
                    (w, h) = EstimateSize(doc, sched, w, h);
                    result.Warnings.Add($"'{sched.Name}' could not be measured; used an estimated size.");
                    StingLog.Warn($"TagSchedulePlacer: '{sched.Name}' unmeasurable, estimated {w:F2}x{h:F2} ft.");
                }

                measured.Add(new Measured
                {
                    Schedule = sched, Width = w, Height = h, AnchorDx = adx, AnchorDy = ady
                });
            }

            // Drop the scratch sheet — deleting the sheet takes its instances too.
            using (var tx = new Transaction(doc, "STING Clear Measuring Sheet"))
            {
                tx.Start();
                try { doc.Delete(scratch.Id); tx.Commit(); }
                catch (Exception ex)
                {
                    StingLog.Warn($"TagSchedulePlacer: could not delete scratch sheet — {ex.Message}");
                    result.Warnings.Add("The temporary measuring sheet could not be removed; delete 'STING ~ measuring' by hand.");
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                }
            }

            return measured;
        }

        /// <summary>
        /// Last-resort size when the bounding box is unreadable: derive height
        /// from the schedule's actual row count and width from its column count.
        /// </summary>
        private static (double Width, double Height) EstimateSize(Document doc, ViewSchedule sched,
            double knownW, double knownH)
        {
            const double RowMm = 8.0;      // default Revit schedule row
            const double ColMm = 38.0;     // default column width
            int rows = 8, cols = 6;
            try
            {
                var body = sched.GetTableData().GetSectionData(SectionType.Body);
                if (body != null)
                {
                    rows = Math.Max(rows, body.NumberOfRows + 2);   // + header rows
                    cols = Math.Max(1, body.NumberOfColumns);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagSchedulePlacer: row count for '{sched.Name}' — {ex.Message}");
            }
            double w = knownW > 0 ? knownW : MmToFt(ColMm * cols);
            double h = knownH > 0 ? knownH : MmToFt(RowMm * rows);
            return (w, h);
        }

        // ──────────────────────────────────────────────────────────────────
        //  Pass 2 — arrange onto real sheets
        // ──────────────────────────────────────────────────────────────────

        private static void Arrange(Document doc, List<Measured> measured,
            ElementId titleBlockId, TagSchedulePlacementResult result)
        {
            double gap = MmToFt(GapMm);

            using (var tx = new Transaction(doc, "STING Place Tag Schedules On Sheets"))
            {
                tx.Start();
                try
                {
                    ViewSheet sheet = null;
                    DrawableZone zone = null;
                    double colLeft = 0, cursorTop = 0, colWidth = 0;

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
                        colLeft = zone.Min.X;
                        cursorTop = zone.Max.Y;
                        colWidth = 0;
                        result.SheetIds.Add(sheet.Id);
                        return true;
                    }

                    if (!NewSheet()) { tx.RollBack(); return; }

                    // Tallest first packs columns far more tightly than name order,
                    // and keeps a column's left edge stable once it is set.
                    foreach (var m in measured.OrderByDescending(x => x.Height)
                                              .ThenBy(x => x.Schedule.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        bool fitsInColumn = (cursorTop - m.Height) >= zone.Min.Y - 1e-9;

                        if (!fitsInColumn)
                        {
                            double nextLeft = colLeft + colWidth + gap;
                            bool fitsNextColumn = (nextLeft + m.Width) <= zone.Max.X + 1e-9
                                                  && m.Height <= zone.Height + 1e-9;

                            if (fitsNextColumn)
                            {
                                colLeft = nextLeft;
                                cursorTop = zone.Max.Y;
                                colWidth = 0;
                            }
                            else
                            {
                                if (!NewSheet()) { result.Failed++; break; }
                                if (m.Height > zone.Height + 1e-9)
                                {
                                    // Taller than any sheet we can make. Place it
                                    // rather than drop it, and flag it.
                                    result.Oversized++;
                                    result.Warnings.Add(
                                        $"'{m.Schedule.Name}' is taller than the sheet and overruns its border.");
                                }
                            }
                        }

                        var point = new XYZ(colLeft - m.AnchorDx, cursorTop - m.AnchorDy, 0);
                        try
                        {
                            var ssi = ScheduleSheetInstance.Create(doc, sheet.Id, m.Schedule.Id, point);
                            if (ssi == null) { result.Failed++; continue; }
                            result.Placed++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"TagSchedulePlacer: place '{m.Schedule.Name}' — {ex.Message}");
                            result.Warnings.Add($"Could not place '{m.Schedule.Name}': {ex.Message}");
                            result.Failed++;
                            continue;
                        }

                        colWidth = Math.Max(colWidth, m.Width);
                        cursorTop -= m.Height + gap;
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"TagSchedulePlacer: arrange pass failed — {ex.Message}");
                    result.Warnings.Add($"Placing schedules failed: {ex.Message}");
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                }
            }
        }

        // ──────────────────────────────────────────────────────────────────

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
