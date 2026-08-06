// ============================================================================
// TagSchedulePlacer.cs — put tag-expander schedules onto sheets.
//
// The tag-expander (ScheduleDisciplineTagExpanderCommand) builds one
// ViewSchedule per model category, but a ViewSchedule that never reaches a
// sheet is not a deliverable — it is a table nobody sees. With ~204 spec
// entries carrying sheet_columns, hand-dragging is not a workable answer.
//
// WHY THIS IS SHAPED THE WAY IT IS
// --------------------------------
// Packing several schedules onto one sheet needs each table's size on paper.
// Three sources were tried and none proved dependable:
//
//   1. ScheduleSheetInstance.get_BoundingBox inside the creating transaction
//      — returns nothing usable; Regenerate does not help.
//   2. The same bounding box measured on a scratch sheet after committing.
//   3. TableSectionData.GetColumnWidth / GetRowHeight — reported ~0 for these
//      schedules, so packing ran on zero heights.
//
// Each attempt packed against a size that was really unknown, and each one
// piled the tables on top of each other.
//
// So correctness no longer depends on knowing the size:
//
//   PASS 1  every schedule goes on its OWN sheet, top-left. This cannot
//           overlap, whatever the API reports. Commit.
//   PASS 2  measure the now-placed, committed instances.
//   PASS 3  ONLY if those measurements look sane, delete the one-per-sheet
//           layout and re-place packed. If they do not, the one-per-sheet
//           result stands.
//
// The worst case is therefore more sheets than strictly necessary — never
// schedules on top of each other. The result says which layout you got.
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
        /// <summary>True when pass 3 ran and schedules share sheets.</summary>
        public bool Compacted;
        /// <summary>How many instances returned a usable measurement in pass 2.</summary>
        public int Measurable;
        /// <summary>Sheets from a previous run that were removed before placing.</summary>
        public int ClearedSheets;
        public readonly List<string> Warnings = new List<string>();
        public readonly List<ElementId> SheetIds = new List<ElementId>();
        /// <summary>Measured height range in mm — 0–0 means measurement failed.</summary>
        public double MinHeightMm, MaxHeightMm;
    }

    internal static class TagSchedulePlacer
    {
        private const string SheetNamePrefix = "STING Tag Schedules";
        private const double GapMm = 10.0;

        /// <summary>Fraction of schedules that must measure sanely before packing is trusted.</summary>
        private const double CompactionThreshold = 0.9;

        private sealed class Sized
        {
            public ViewSchedule Schedule;
            public double Width;
            public double Height;
            /// <summary>bbox.Min.X - Point.X, so left edges line up exactly.</summary>
            public double AnchorDx;
            /// <summary>bbox.Max.Y - Point.Y, so top edges line up exactly.</summary>
            public double AnchorDy;
            public bool Usable => Width > 0 && Height > 0;
        }

        private static double MmToFt(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        private static double FtToMm(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);

        /// <summary>
        /// Place the given schedules onto new sheets. Opens its own transactions,
        /// so the caller must NOT have one open.
        /// </summary>
        internal static TagSchedulePlacementResult Place(Document doc, IList<ViewSchedule> schedules)
        {
            var result = new TagSchedulePlacementResult();
            if (doc == null || schedules == null || schedules.Count == 0) return result;

            var list = schedules.Where(s => s != null)
                                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                                .ToList();
            if (list.Count == 0) return result;

            ElementId titleBlockId = ResolveTitleBlock(doc);
            if (titleBlockId == ElementId.InvalidElementId)
            {
                result.Warnings.Add("No title block family is loaded — cannot create sheets to place schedules on.");
                StingLog.Warn("TagSchedulePlacer: no title block type found; placement skipped.");
                return result;
            }

            // ── PASS 0 — clear this tool's own previous sheets ──
            // Without this a re-run stacks a second layout on top of the first,
            // and any earlier bad layout survives every attempt to fix it.
            result.ClearedSheets = ClearPreviousSheets(doc, result);

            // ── PASS 1 — one per sheet. Cannot overlap. ──
            var instances = PlaceOnePerSheet(doc, list, titleBlockId, result);
            if (instances.Count == 0) return result;

            // ── PASS 2 — measure what is now placed and committed ──
            var sized = MeasurePlaced(doc, instances, result);
            var usable = sized.Where(s => s.Usable).ToList();
            result.Measurable = usable.Count;
            result.MinHeightMm = usable.Count > 0 ? FtToMm(usable.Min(s => s.Height)) : 0;
            result.MaxHeightMm = usable.Count > 0 ? FtToMm(usable.Max(s => s.Height)) : 0;

            StingLog.Info($"TagSchedulePlacer: measured {usable.Count}/{sized.Count} — " +
                          $"heights {result.MinHeightMm:F0}..{result.MaxHeightMm:F0} mm");

            // ── PASS 3 — pack, but only on trustworthy measurements ──
            bool trustworthy = sized.Count > 0
                            && usable.Count >= (int)Math.Ceiling(sized.Count * CompactionThreshold)
                            && result.MaxHeightMm > 1.0;

            if (!trustworthy)
            {
                result.Warnings.Add(
                    $"Revit reported a usable size for only {usable.Count} of {sized.Count} schedules, " +
                    "so they were left one per sheet rather than packed — packing on unknown sizes is what " +
                    "makes them overlap.");
                StingLog.Warn("TagSchedulePlacer: measurements untrustworthy; keeping one-per-sheet layout.");
                return result;
            }

            Compact(doc, sized, titleBlockId, result);
            return result;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Pass 0 — clear this tool's own previous sheets
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Delete sheets this placer created on an earlier run, identified by
        /// the sheet-name prefix. Only sheets carrying nothing but schedule
        /// instances are removed, so a sheet someone has since put drawings on
        /// is left alone.
        /// </summary>
        private static int ClearPreviousSheets(Document doc, TagSchedulePlacementResult result)
        {
            List<ViewSheet> mine;
            try
            {
                mine = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => !s.IsTemplate
                             && (s.Name ?? "").StartsWith(SheetNamePrefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagSchedulePlacer: could not scan for previous sheets — {ex.Message}");
                return 0;
            }

            if (mine.Count == 0) return 0;

            int cleared = 0;
            using (var tx = new Transaction(doc, "STING Clear Previous Tag Schedule Sheets"))
            {
                tx.Start();
                try
                {
                    foreach (var sheet in mine)
                    {
                        // A sheet holding real viewports is somebody's drawing now,
                        // whatever its name — never delete that.
                        bool hasViewports;
                        try { hasViewports = sheet.GetAllViewports().Count > 0; }
                        catch { hasViewports = true; }   // unknown → treat as occupied

                        if (hasViewports)
                        {
                            result.Warnings.Add(
                                $"Sheet '{sheet.SheetNumber} {sheet.Name}' has viewports on it and was left alone.");
                            continue;
                        }

                        try { doc.Delete(sheet.Id); cleared++; }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"TagSchedulePlacer: delete previous sheet '{sheet.Name}' — {ex.Message}");
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"TagSchedulePlacer: clearing previous sheets failed — {ex.Message}");
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    return 0;
                }
            }

            StingLog.Info($"TagSchedulePlacer: cleared {cleared} sheet(s) from a previous run.");
            return cleared;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Pass 1 — one schedule per sheet
        // ──────────────────────────────────────────────────────────────────

        private static List<(ScheduleSheetInstance Ssi, ViewSheet Sheet, ViewSchedule Sched)> PlaceOnePerSheet(
            Document doc, List<ViewSchedule> list, ElementId titleBlockId, TagSchedulePlacementResult result)
        {
            var placed = new List<(ScheduleSheetInstance, ViewSheet, ViewSchedule)>();

            using (var tx = new Transaction(doc, "STING Place Tag Schedules On Sheets"))
            {
                tx.Start();
                try
                {
                    foreach (var sched in list)
                    {
                        var sheet = CreateSheet(doc, titleBlockId, result);
                        if (sheet == null) { result.Failed++; continue; }

                        var zone = SheetManagerEngine.GetDrawableZone(doc, sheet);
                        XYZ pt = zone != null ? new XYZ(zone.Min.X, zone.Max.Y, 0) : XYZ.Zero;

                        try
                        {
                            var ssi = ScheduleSheetInstance.Create(doc, sheet.Id, sched.Id, pt);
                            if (ssi == null) { result.Failed++; continue; }
                            placed.Add((ssi, sheet, sched));
                            result.SheetIds.Add(sheet.Id);
                            result.Placed++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"TagSchedulePlacer: place '{sched.Name}' — {ex.Message}");
                            result.Warnings.Add($"Could not place '{sched.Name}': {ex.Message}");
                            result.Failed++;
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"TagSchedulePlacer: pass 1 failed — {ex.Message}");
                    result.Warnings.Add($"Placing schedules failed: {ex.Message}");
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    placed.Clear();
                }
            }

            return placed;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Pass 2 — measure the committed instances
        // ──────────────────────────────────────────────────────────────────

        private static List<Sized> MeasurePlaced(Document doc,
            List<(ScheduleSheetInstance Ssi, ViewSheet Sheet, ViewSchedule Sched)> instances,
            TagSchedulePlacementResult result)
        {
            var sized = new List<Sized>();

            foreach (var (ssi, sheet, sched) in instances)
            {
                var s = new Sized { Schedule = sched };
                try
                {
                    var bb = ssi.get_BoundingBox(sheet);
                    if (bb != null)
                    {
                        s.Width  = Math.Abs(bb.Max.X - bb.Min.X);
                        s.Height = Math.Abs(bb.Max.Y - bb.Min.Y);
                        s.AnchorDx = bb.Min.X - ssi.Point.X;
                        s.AnchorDy = bb.Max.Y - ssi.Point.Y;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"TagSchedulePlacer: measuring '{sched.Name}' — {ex.Message}");
                }
                sized.Add(s);
            }

            return sized;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Pass 3 — re-place packed, replacing the one-per-sheet layout
        // ──────────────────────────────────────────────────────────────────

        private static void Compact(Document doc, List<Sized> sized,
            ElementId titleBlockId, TagSchedulePlacementResult result)
        {
            double gap = MmToFt(GapMm);
            var oldSheetIds = new List<ElementId>(result.SheetIds);

            using (var tx = new Transaction(doc, "STING Pack Tag Schedules"))
            {
                tx.Start();
                try
                {
                    // Drop the one-per-sheet layout; deleting a sheet takes its
                    // schedule instances with it.
                    foreach (var id in oldSheetIds)
                    {
                        try { doc.Delete(id); }
                        catch (Exception ex) { StingLog.Warn($"TagSchedulePlacer: delete sheet — {ex.Message}"); }
                    }
                    result.SheetIds.Clear();
                    result.SheetsCreated = 0;
                    result.Placed = 0;

                    ViewSheet sheet = null;
                    DrawableZone zone = null;
                    double colLeft = 0, cursorTop = 0, colWidth = 0;
                    bool sheetIsFresh = true;

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
                        sheetIsFresh = true;
                        result.SheetIds.Add(sheet.Id);
                        return true;
                    }

                    if (!NewSheet()) { tx.RollBack(); return; }

                    // Tallest first packs columns tightly and keeps a column's
                    // left edge stable once set. Anything that failed to measure
                    // goes last, on a sheet of its own.
                    var order = sized.Where(s => s.Usable)
                                     .OrderByDescending(s => s.Height)
                                     .ThenBy(s => s.Schedule.Name, StringComparer.OrdinalIgnoreCase)
                                     .Concat(sized.Where(s => !s.Usable)
                                                  .OrderBy(s => s.Schedule.Name, StringComparer.OrdinalIgnoreCase))
                                     .ToList();

                    foreach (var s in order)
                    {
                        if (!s.Usable)
                        {
                            if (!sheetIsFresh && !NewSheet()) { result.Failed++; break; }
                            if (PlaceOne(doc, sheet, s.Schedule, new XYZ(zone.Min.X, zone.Max.Y, 0), result))
                                result.Placed++;
                            if (!NewSheet()) { result.Failed++; break; }
                            continue;
                        }

                        if ((cursorTop - s.Height) < zone.Min.Y - 1e-9)
                        {
                            double nextLeft = colLeft + colWidth + gap;
                            if ((nextLeft + s.Width) <= zone.Max.X + 1e-9 && s.Height <= zone.Height + 1e-9)
                            {
                                colLeft = nextLeft;
                                cursorTop = zone.Max.Y;
                                colWidth = 0;
                            }
                            else if (sheetIsFresh)
                            {
                                // Bigger than any sheet we can make: place it and
                                // flag it rather than drop it.
                                result.Oversized++;
                                result.Warnings.Add($"'{s.Schedule.Name}' is larger than the sheet and overruns its border.");
                            }
                            else if (!NewSheet()) { result.Failed++; break; }
                        }

                        var pt = new XYZ(colLeft - s.AnchorDx, cursorTop - s.AnchorDy, 0);
                        if (!PlaceOne(doc, sheet, s.Schedule, pt, result)) { result.Failed++; continue; }

                        result.Placed++;
                        sheetIsFresh = false;
                        colWidth = Math.Max(colWidth, s.Width);
                        cursorTop -= s.Height + gap;
                    }

                    tx.Commit();
                    result.Compacted = true;
                }
                catch (Exception ex)
                {
                    // Rolling back restores the one-per-sheet layout, which is
                    // correct if verbose — far better than a half-packed pile.
                    StingLog.Warn($"TagSchedulePlacer: packing failed, keeping one-per-sheet — {ex.Message}");
                    result.Warnings.Add($"Packing failed; schedules left one per sheet: {ex.Message}");
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    result.SheetIds.Clear();
                    result.SheetIds.AddRange(oldSheetIds);
                    result.SheetsCreated = oldSheetIds.Count;
                    result.Placed = oldSheetIds.Count;
                    result.Compacted = false;
                }
            }
        }

        // ──────────────────────────────────────────────────────────────────

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
