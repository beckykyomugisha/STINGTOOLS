// ============================================================================
// TagSchedulePlacer.cs — put tag-expander schedules onto sheets.
//
// The tag-expander (ScheduleDisciplineTagExpanderCommand) builds one
// ViewSchedule per model category, but a ViewSchedule that never reaches a
// sheet is not a deliverable — it is a table nobody sees. With ~204 spec
// entries carrying sheet_columns, hand-dragging is not a workable answer.
//
// SIZING
// ------
// Packing needs each table's size on paper. Two earlier attempts measured a
// placed ScheduleSheetInstance's bounding box — inside the creating
// transaction, then after committing it. Neither is reliable, and packing
// against an unreliable size is what piled the tables on top of each other.
//
// The dependable source is the schedule's own table data: TableSectionData
// reports GetColumnWidth and GetRowHeight in sheet space (feet) directly,
// with nothing placed and no transaction needed. Width is the widest
// section's summed column widths; height is every section's summed row
// heights. That is what this uses.
//
// A schedule whose table data cannot be read is placed on a sheet of its own
// rather than packed on a guess — an isolated sheet is easy to spot and fix,
// a silent overlap is not.
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
        /// <summary>Schedules whose table data could not be read, so given their own sheet.</summary>
        public int Unsized;
        public readonly List<string> Warnings = new List<string>();
        public readonly List<ElementId> SheetIds = new List<ElementId>();
        /// <summary>Smallest and largest measured height, in mm — a sanity read-out for the report.</summary>
        public double MinHeightMm, MaxHeightMm;
    }

    internal static class TagSchedulePlacer
    {
        private const string SheetNamePrefix = "STING Tag Schedules";
        private const double GapMm = 10.0;

        /// <summary>Paper-space size of one schedule, in feet.</summary>
        private sealed class Sized
        {
            public ViewSchedule Schedule;
            public double Width;
            public double Height;
            /// <summary>True when the table data was unreadable — gets its own sheet.</summary>
            public bool Unknown;
        }

        private static double MmToFt(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        private static double FtToMm(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);

        /// <summary>
        /// Place the given schedules onto new sheets. Opens its own transaction,
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

            var sized = schedules.Where(s => s != null).Select(s => MeasureFromTableData(s, result)).ToList();
            if (sized.Count == 0) return result;

            var real = sized.Where(s => !s.Unknown).ToList();
            result.MinHeightMm = real.Count > 0 ? FtToMm(real.Min(s => s.Height)) : 0;
            result.MaxHeightMm = real.Count > 0 ? FtToMm(real.Max(s => s.Height)) : 0;
            result.Unsized = sized.Count - real.Count;

            StingLog.Info($"TagSchedulePlacer: sizing {sized.Count} schedule(s) — " +
                          $"heights {result.MinHeightMm:F0}..{result.MaxHeightMm:F0} mm, unsized={result.Unsized}");

            Arrange(doc, sized, titleBlockId, result);

            StingLog.Info($"TagSchedulePlacer: placed={result.Placed}, sheets={result.SheetsCreated}, " +
                          $"oversized={result.Oversized}, unsized={result.Unsized}, failed={result.Failed}");
            return result;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Sizing — straight from the schedule's table data
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Width and height on paper, in feet, summed from the schedule's own
        /// column widths and row heights. No placement, no transaction.
        /// </summary>
        private static Sized MeasureFromTableData(ViewSchedule sched, TagSchedulePlacementResult result)
        {
            double width = 0, height = 0;
            bool readAnything = false;

            // Header carries the title and column-heading rows; Body the data.
            // Both contribute height; width is the widest of them.
            foreach (SectionType st in new[] { SectionType.Header, SectionType.Body, SectionType.Summary, SectionType.Footer })
            {
                TableSectionData sd;
                try { sd = sched.GetTableData()?.GetSectionData(st); }
                catch (Exception ex)
                {
                    StingLog.Warn($"TagSchedulePlacer: section {st} of '{sched.Name}' — {ex.Message}");
                    continue;
                }
                if (sd == null) continue;

                try
                {
                    double sectionWidth = 0;
                    for (int c = 0; c < sd.NumberOfColumns; c++) sectionWidth += sd.GetColumnWidth(c);
                    for (int r = 0; r < sd.NumberOfRows; r++) height += sd.GetRowHeight(r);
                    width = Math.Max(width, sectionWidth);
                    readAnything = true;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"TagSchedulePlacer: sizing section {st} of '{sched.Name}' — {ex.Message}");
                }
            }

            if (!readAnything || width <= 0 || height <= 0)
            {
                StingLog.Warn($"TagSchedulePlacer: '{sched.Name}' has no readable table size — giving it its own sheet.");
                result.Warnings.Add($"'{sched.Name}' could not be sized; placed on a sheet of its own.");
                return new Sized { Schedule = sched, Width = 0, Height = 0, Unknown = true };
            }

            return new Sized { Schedule = sched, Width = width, Height = height };
        }

        // ──────────────────────────────────────────────────────────────────
        //  Arrange onto sheets
        // ──────────────────────────────────────────────────────────────────

        private static void Arrange(Document doc, List<Sized> sized,
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

                    // Tallest first packs columns far more tightly than name order
                    // and keeps a column's left edge stable once it is set.
                    // Unsized ones go last so they do not disturb the packed run.
                    var order = sized.Where(s => !s.Unknown)
                                     .OrderByDescending(s => s.Height)
                                     .ThenBy(s => s.Schedule.Name, StringComparer.OrdinalIgnoreCase)
                                     .Concat(sized.Where(s => s.Unknown)
                                                  .OrderBy(s => s.Schedule.Name, StringComparer.OrdinalIgnoreCase))
                                     .ToList();

                    bool sheetIsFresh = true;   // nothing placed on the current sheet yet

                    foreach (var s in order)
                    {
                        if (s.Unknown)
                        {
                            // Unknown size: give it a sheet to itself rather than
                            // pack it against a guess and risk landing on a neighbour.
                            if (!sheetIsFresh && !NewSheet()) { result.Failed++; break; }
                            if (PlaceOne(doc, sheet, s.Schedule, new XYZ(zone.Min.X, zone.Max.Y, 0), result))
                                result.Placed++;
                            if (!NewSheet()) { result.Failed++; break; }
                            sheetIsFresh = true;
                            continue;
                        }

                        bool fitsInColumn = (cursorTop - s.Height) >= zone.Min.Y - 1e-9;
                        if (!fitsInColumn)
                        {
                            double nextLeft = colLeft + colWidth + gap;
                            bool fitsNextColumn = (nextLeft + s.Width) <= zone.Max.X + 1e-9
                                                  && s.Height <= zone.Height + 1e-9;
                            if (fitsNextColumn)
                            {
                                colLeft = nextLeft;
                                cursorTop = zone.Max.Y;
                                colWidth = 0;
                            }
                            else if (sheetIsFresh)
                            {
                                // Does not fit even on an empty sheet — place it
                                // anyway and flag it rather than drop it.
                                result.Oversized++;
                                result.Warnings.Add($"'{s.Schedule.Name}' is larger than the sheet and overruns its border.");
                            }
                            else
                            {
                                if (!NewSheet()) { result.Failed++; break; }
                                sheetIsFresh = true;
                            }
                        }

                        if (!PlaceOne(doc, sheet, s.Schedule, new XYZ(colLeft, cursorTop, 0), result))
                        {
                            result.Failed++;
                            continue;
                        }

                        result.Placed++;
                        sheetIsFresh = false;
                        colWidth = Math.Max(colWidth, s.Width);
                        cursorTop -= s.Height + gap;
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"TagSchedulePlacer: arrange failed — {ex.Message}");
                    result.Warnings.Add($"Placing schedules failed: {ex.Message}");
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                }
            }
        }

        private static bool PlaceOne(Document doc, ViewSheet sheet, ViewSchedule sched,
            XYZ point, TagSchedulePlacementResult result)
        {
            try
            {
                return ScheduleSheetInstance.Create(doc, sheet.Id, sched.Id, point) != null;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagSchedulePlacer: place '{sched.Name}' — {ex.Message}");
                result.Warnings.Add($"Could not place '{sched.Name}': {ex.Message}");
                return false;
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
