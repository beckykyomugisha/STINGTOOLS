using StingTools.Core;
// StingTools — Drawing Template Manager · Phase 137
//
// SheetPlacementBridge converts a DrawingType.Slot's normalised
// (0..1) coordinates into a paper-space XYZ position on a sheet,
// using the title block bounding box as the drawable zone with a
// 25mm margin. Used by DrawingProducer when placing the produced
// view as a Viewport on the sheet.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.Placement;

namespace StingTools.Core.Drawing
{
    internal sealed class PlacementResult
    {
        public List<ElementId> ViewportIds { get; } = new List<ElementId>();
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>P12 — the resolved placement for one slot: paper-space centre
    /// plus the slot's paper footprint (feet) and an optional scale hint. The
    /// footprint drives fit-to-slot scaling (ApplyFitScale); before P12 only the
    /// centre was computed and slot [w,h] was ignored.</summary>
    internal sealed class SlotPlacement
    {
        public XYZ Center;
        public double WidthFt;
        public double HeightFt;
        public int? ScaleHint;
        /// <summary>The slot this placement came from. Carried so callers
        /// outside PlaceAccordingToSlots (the producer) can honour the
        /// slot's ViewportType and ViewType without re-resolving it.</summary>
        public DrawingSlot Slot;
        public bool HasSize => WidthFt > 1e-9 && HeightFt > 1e-9;
    }

    internal static class SheetPlacementBridge
    {
        /// <summary>
        /// DTW-75: record that STING placed this viewport / schedule instance, in
        /// Extensible Storage (StingProvenanceSchema; read back with
        /// StingProvenanceSchema.IsAutoCreated). Viewports and schedule sheet
        /// instances take no bound shared parameters, so the old
        /// STING_AUTO_PLACED_BOOL write never landed. A failure is reported once
        /// in the placement result.
        /// </summary>
        internal static bool MarkAutoPlaced(Element placed, PlacementResult pr = null)
        {
            if (placed == null) return false;
            if (StingTools.Core.Storage.StingProvenanceSchema.Stamp(placed, "SheetPlacement", placed.Category?.Name ?? ""))
                return true;
            const string msg = "Could not mark placed viewports as STING-placed (Extensible Storage write failed; see the log).";
            if (pr?.Warnings != null && !pr.Warnings.Contains(msg)) pr.Warnings.Add(msg);
            return false;
        }

        private const double MarginMm = 25.0;
        private const double MmPerFt = 304.8;
        private static double MmToFt(double mm) => mm / MmPerFt;

        internal static XYZ GetSlotPosition(Document doc, ElementId sheetId, DrawingType dt, int slotIndex, ProduceResult result, FamilySlotContext famCtx = null)
            => ResolveSlot(doc, sheetId, dt, slotIndex, result, famCtx)?.Center;

        /// <summary>P12 — resolve a slot's placement: centre + paper footprint +
        /// scale hint. Two reference-frame-consistent paths, both offset by the
        /// title-block instance LocationPoint (P12.C) and both sized off the ONE
        /// drawable rect (P12.B):
        ///   1. family-grid (P1) — the live family's own slot bounds.
        ///   2. norm* fallback — fractions of the family's drawable rect (from
        ///      STING_TITLE_BLOCKS.json). Legacy families with no drawable rect
        ///      fall back to the historic (title-block bbox − 25 mm) frame so
        ///      their placement is byte-for-byte unchanged.</summary>
        internal static SlotPlacement ResolveSlot(Document doc, ElementId sheetId, DrawingType dt, int slotIndex, ProduceResult result, FamilySlotContext famCtx = null)
        {
            if (doc == null || sheetId == null || sheetId == ElementId.InvalidElementId || dt == null) return null;
            DrawingSlot slot = null;
            if (dt.Slots != null && dt.Slots.Count > 0)
            {
                if (slotIndex >= 0 && slotIndex < dt.Slots.Count) slot = dt.Slots[slotIndex];
                else slot = dt.Slots[0];
            }
            if (slot == null) return null;

            try
            {
                var sheet = doc.GetElement(sheetId) as ViewSheet;
                if (sheet == null) return null;

                // P1 — unified slot model. When this slot opts in via PurposeTag
                // / SlotRef, place it against the live title-block family's own
                // slot grid (TitleBlockSpec.SlotSpec) — the single source of
                // truth for where views land. Falls through to the historic
                // norm* subdivision below when it doesn't resolve, so legacy
                // (norm-only) profiles are completely untouched.
                if (SlotOptsIntoFamilyGrid(slot))
                {
                    var ctx = famCtx ?? BuildFamilySlotContext(doc, sheet, dt, result);
                    var bounds = ResolveUnifiedSlotBounds(slot, ctx, result);
                    if (bounds?.Bbox != null)
                    {
                        // Slot bounds are in feet, origin at the title-block
                        // family (0,0). P12.C — offset by the instance
                        // LocationPoint so a title block moved off (0,0) still
                        // places correctly (was previously assumed to be zero).
                        var origin = ctx?.TitleBlockOrigin ?? XYZ.Zero;
                        double cxf = (bounds.Min.X + bounds.Max.X) / 2.0 + origin.X;
                        double cyf = (bounds.Min.Y + bounds.Max.Y) / 2.0 + origin.Y;
                        return new SlotPlacement
                        {
                            Slot = slot,
                            Center    = new XYZ(cxf, cyf, 0),
                            WidthFt   = Math.Abs(bounds.Max.X - bounds.Min.X),
                            HeightFt  = Math.Abs(bounds.Max.Y - bounds.Min.Y),
                            ScaleHint = bounds.ScaleHint,
                        };
                    }
                    // else: unmatched purposeTag / slotRef — fall through to norm.
                }

                var titleBlock = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .FirstOrDefault();
                if (titleBlock == null)
                {
                    result?.Warnings.Add("SheetPlacementBridge: no title block on sheet — cannot derive slot position.");
                    return null;
                }

                double zoneMinX, zoneMinY, zoneW, zoneH;
                // P12.B — single reference frame. Resolve the family's drawable
                // rect (mm, origin = family bottom-left) and position it at the
                // instance LocationPoint (P12.C). Both placement paths now share
                // this one frame.
                var drawable = ResolveDrawableForFamily(
                    StingTools.Commands.Drawing.TitleBlockSlotUtils.GetFamilyName(doc, titleBlock));
                if (drawable != null && drawable.W > 0 && drawable.H > 0)
                {
                    var origin = GetTitleBlockOrigin(titleBlock);
                    zoneMinX = origin.X + MmToFt(drawable.X);
                    zoneMinY = origin.Y + MmToFt(drawable.Y);
                    zoneW    = MmToFt(drawable.W);
                    zoneH    = MmToFt(drawable.H);
                }
                else
                {
                    // Legacy fallback — title-block bbox minus a 25 mm margin.
                    // Unchanged behaviour for families with no drawable rect.
                    var bb = titleBlock.get_BoundingBox(sheet);
                    if (bb == null) return null;
                    double margin = MarginMm / MmPerFt;
                    zoneMinX = bb.Min.X + margin;
                    zoneMinY = bb.Min.Y + margin;
                    zoneW = (bb.Max.X - bb.Min.X) - 2 * margin;
                    zoneH = (bb.Max.Y - bb.Min.Y) - 2 * margin;
                }

                double cx = zoneMinX + (slot.NormX + slot.NormW / 2.0) * zoneW;
                double cy = zoneMinY + (slot.NormY + slot.NormH / 2.0) * zoneH;
                return new SlotPlacement
                {
                    Slot = slot,
                    Center    = new XYZ(cx, cy, 0),
                    WidthFt   = slot.NormW * zoneW,
                    HeightFt  = slot.NormH * zoneH,
                    // DrawingSlot's per-slot Scale is an explicit pin handled by
                    // ApplySlotOverrides, not a fit hint — leave null here.
                    ScaleHint = null,
                };
            }
            catch (Exception ex)
            {
                result?.Warnings.Add($"SheetPlacementBridge.ResolveSlot: {ex.Message}");
                return null;
            }
        }

        // ── P12.A — fit-to-slot scaling ─────────────────────────────────────

        /// <summary>P12.A — make the view's paper footprint fit inside the slot
        /// rect. Computes the required scale from the view's extent versus the
        /// slot's paper width/height and rounds UP to the next standard scale.
        ///
        /// DTW-150: fitting only ever COARSENS. The baseline is
        /// <paramref name="typeScale"/> (the drawing type's scale; 0 = use the
        /// view's current scale), and the view moves off it only when it does
        /// not fit — a small plan on a 1:100 type stays 1:100 instead of
        /// becoming 1:50 or 1:20. <see cref="SlotPlacement.ScaleHint"/> stays a
        /// floor. A coarsening is reported in <paramref name="warnings"/>.
        ///
        /// Only applied to cropped graphical views; schedules / legends / 3D and
        /// uncropped views are left untouched. Never throws.</summary>
        internal static void ApplyFitScale(Document doc, View v, SlotPlacement sp, int typeScale = 0, List<string> warnings = null)
        {
            if (v == null || sp == null || !sp.HasSize) return;
            if (!IsScalableView(v)) return;
            try
            {
                if (!v.CropBoxActive) return; // can't measure intended footprint

                // View.Outline is PAPER-space and only recomputes on regeneration,
                // so measuring it right after setting a crop in the same transaction
                // read stale dimensions. Regenerating here fixed that but cost a
                // full document regeneration per viewport — on a 20-type × 4-level
                // batch that is 80+ regenerations inside one transaction, which
                // reads as a hang. So measure MODEL-space geometry instead: both
                // sources below are readable immediately, with no regeneration.
                double modelW = 0, modelH = 0;

                // 1. A scope box drives the crop — its own bounding box is the
                //    intended extent, and it is independent of the view's
                //    not-yet-regenerated crop.
                //    PLAN VIEWS ONLY. get_BoundingBox(null) is model-space and
                //    axis-aligned, so its X/Y are the view's horizontal axes only
                //    for a plan. On a section or elevation the view's vertical axis
                //    is model Z, so treating model Y as height would compute a
                //    nonsense scale — those fall through to CropBox below, which is
                //    expressed in the view's own frame and is correct for any type.
                if (IsPlanFamily(v))
                {
                    try
                    {
                        var sbId = v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP)?.AsElementId();
                        if (sbId != null && sbId != ElementId.InvalidElementId)
                        {
                            var sbb = doc?.GetElement(sbId)?.get_BoundingBox(null);
                            if (sbb != null)
                            {
                                modelW = Math.Abs(sbb.Max.X - sbb.Min.X);
                                modelH = Math.Abs(sbb.Max.Y - sbb.Min.Y);
                            }
                        }
                    }
                    catch (Exception exS) { StingTools.Core.StingLog.Warn($"ApplyFitScale scope box: {exS.Message}"); }
                }

                // 2. Otherwise the crop box — which DrawingCropApplier just set
                //    itself for TightBbox / RoomBoundary, so it is current.
                if (modelW < 1e-9 || modelH < 1e-9)
                {
                    try
                    {
                        var cb = v.CropBox;
                        if (cb != null)
                        {
                            modelW = Math.Abs(cb.Max.X - cb.Min.X);
                            modelH = Math.Abs(cb.Max.Y - cb.Min.Y);
                        }
                    }
                    catch (Exception exC) { StingTools.Core.StingLog.Warn($"ApplyFitScale crop box: {exC.Message}"); }
                }

                double fit;
                if (modelW > 1e-9 && modelH > 1e-9)
                {
                    // Model feet ÷ paper feet = the scale that makes it fit.
                    fit = Math.Max(modelW / sp.WidthFt, modelH / sp.HeightFt);
                }
                else
                {
                    // Last resort — the historic paper-space path.
                    var outline = v.Outline;
                    if (outline == null) return;
                    double curW = outline.Max.U - outline.Min.U;
                    double curH = outline.Max.V - outline.Min.V;
                    if (curW < 1e-9 || curH < 1e-9) return;
                    int curScale = v.Scale > 0 ? v.Scale : 100;
                    fit = Math.Max(curW * curScale / sp.WidthFt,
                                   curH * curScale / sp.HeightFt);
                }
                // DTW-157 — the crop is not the viewport: grid/level heads, the
                // annotation crop and the title extend past it. Grow the measured
                // extent by the data-driven margin before choosing a scale.
                double margin = SlotFitScale.DefaultAnnotationMarginFactor;
                try
                {
                    var rules = StingTools.Commands.Drawing.ViewportPlacementRules.Load();
                    if (rules != null) margin = rules.AnnotationMarginFactor;
                }
                catch (Exception exR) { StingTools.Core.StingLog.Warn($"ApplyFitScale margin: {exR.Message}"); }
                fit *= SlotFitScale.ClampMarginFactor(margin);

                int target = SlotFitScale.Decide(fit, typeScale, v.Scale, sp.ScaleHint, out bool coarsened);
                if (coarsened)
                {
                    int baseline = typeScale > 0 ? typeScale : v.Scale;
                    warnings?.Add($"View '{v.Name}' does not fit slot '{sp.Slot?.Label}' at 1:{baseline} — coarsened to 1:{target}.");
                }
                if (target > 0 && target != v.Scale)
                {
                    // DTW-157 — a view template that controls View Scale makes this
                    // throw. It used to be swallowed, so the view kept a scale that
                    // does not fit and nothing said so.
                    try { v.Scale = target; }
                    catch (Exception exS)
                    {
                        var msg = $"View '{v.Name}': could not set scale 1:{target} for slot '{sp.Slot?.Label}' " +
                                  $"(stays 1:{v.Scale}; a view template may control View Scale) — {exS.Message}";
                        StingTools.Core.StingLog.Warn("SheetPlacementBridge.ApplyFitScale: " + msg);
                        warnings?.Add(msg);
                    }
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"SheetPlacementBridge.ApplyFitScale: {ex.Message}");
            }
        }

        /// <summary>Plan-family views only — the ones whose paper axes are the
        /// model's X/Y. Sections and elevations map paper-vertical to model Z, so
        /// a model-space axis-aligned box cannot be read as width × height.</summary>
        private static bool IsPlanFamily(View v)
        {
            if (v == null) return false;
            switch (v.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.AreaPlan:
                case ViewType.EngineeringPlan:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsScalableView(View v)
        {
            switch (v.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.AreaPlan:
                case ViewType.EngineeringPlan:
                case ViewType.Section:
                case ViewType.Elevation:
                case ViewType.Detail:
                    return true;
                default:
                    return false;
            }
        }

        internal static XYZ GetTitleBlockOrigin(Element titleBlock)
        {
            try
            {
                if (titleBlock is FamilyInstance fi && fi.Location is LocationPoint lp && lp.Point != null)
                    return lp.Point;
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Suppressed: {ex.Message}"); }
            return XYZ.Zero;
        }

        // P12.B — memoised per-family drawable rect from STING_TITLE_BLOCKS.json
        // (extends-resolved). DTW-161: keyed on the file's path and last-write
        // time, like ViewportPlacementRules.Load, so an edit to the JSON is
        // picked up without restarting Revit. It used to be cached for the session.
        private static Dictionary<string, StingTools.Core.Drawing.DrawableRect> _drawableCache;
        private static string _drawableCachePath;
        private static DateTime _drawableCacheWriteUtc;
        private static readonly object _drawableLock = new object();

        private static StingTools.Core.Drawing.DrawableRect ResolveDrawableForFamily(string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return null;

            string path = null;
            DateTime written = DateTime.MinValue;
            try
            {
                path = StingToolsApp.FindDataFile("STING_TITLE_BLOCKS.json");
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                    written = System.IO.File.GetLastWriteTimeUtc(path);
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"SheetPlacementBridge.ResolveDrawableForFamily stamp: {ex.Message}"); }

            Dictionary<string, StingTools.Core.Drawing.DrawableRect> cache;
            lock (_drawableLock)
            {
                cache = _drawableCache;
                bool current = cache != null && written == _drawableCacheWriteUtc
                    && string.Equals(path, _drawableCachePath, StringComparison.OrdinalIgnoreCase);
                if (!current)
                {
                    cache = new Dictionary<string, StingTools.Core.Drawing.DrawableRect>(StringComparer.OrdinalIgnoreCase);
                    StingTools.Core.Drawing.TitleBlockLibrary lib = null;
                    try
                    {
                        lib = StingTools.Core.Drawing.TitleBlockSpecRegistry.Load();
                        if (lib?.Families != null)
                            foreach (var f in lib.Families)
                            {
                                if (f.Abstract || string.IsNullOrEmpty(f.Id)) continue;
                                var resolved = StingTools.Core.Drawing.TitleBlockSpecRegistry.Resolve(lib, f);
                                if (resolved?.Drawable != null) cache[f.Id] = resolved.Drawable;
                            }
                    }
                    catch (Exception ex)
                    {
                        // DTW-104: a failed load is not a read. Answer this call from what was
                        // read so far, but do not cache it, so the next call tries again.
                        StingTools.Core.StingLog.Warn($"SheetPlacementBridge.ResolveDrawableForFamily: {ex.Message} — not cached; will retry.");
                        return cache.TryGetValue(familyName, out var partial) ? partial : null;
                    }
                    // Load() reports its own failure and returns null; that is not a
                    // read either, so it is not cached.
                    if (lib == null)
                        return null;
                    _drawableCache = cache;
                    _drawableCachePath = path;
                    _drawableCacheWriteUtc = written;
                }
            }
            return cache.TryGetValue(familyName, out var d) ? d : null;
        }

        /// <summary>
        /// DTW-157 — after placement, compare the viewport's box (which includes
        /// annotation and title, unlike the crop the fit measured) with its slot
        /// and report an overrun. Silent when the outline is not available yet.
        /// </summary>
        internal static void ReportViewportOverflow(Viewport vp, SlotPlacement sp, List<string> warnings)
        {
            if (vp == null || sp?.Center == null || !sp.HasSize || warnings == null) return;
            try
            {
                var box = vp.GetBoxOutline();
                if (box == null) return;
                double w = box.MaximumPoint.X - box.MinimumPoint.X, h = box.MaximumPoint.Y - box.MinimumPoint.Y;
                if (w < 1e-9 || h < 1e-9) return;
                if (SlotFitScale.Overflows(box.MinimumPoint.X, box.MinimumPoint.Y, box.MaximumPoint.X, box.MaximumPoint.Y,
                        sp.Center.X, sp.Center.Y, sp.WidthFt, sp.HeightFt, MmToFt(1.0)))
                {
                    string name = (vp.Document?.GetElement(vp.ViewId) as View)?.Name ?? vp.ViewId.ToString();
                    warnings.Add($"Viewport '{name}' ({w * MmPerFt:0} x {h * MmPerFt:0} mm with annotation) runs past slot " +
                                 $"'{sp.Slot?.Label}' ({sp.WidthFt * MmPerFt:0} x {sp.HeightFt * MmPerFt:0} mm).");
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"SheetPlacementBridge.ReportViewportOverflow: {ex.Message}");
            }
        }

        /// <summary>
        /// DTW-151 — place a schedule into a slot. A ScheduleSheetInstance's
        /// point is its TOP-LEFT corner (TitleBlockFactory.PlaceRevisionSchedules
        /// relies on the same), not its centre: passing the slot centre hung the
        /// schedule off the slot's right and bottom edges. With a sized slot the
        /// schedule goes to the slot's top-left; without one, to
        /// <paramref name="fallback"/> as before. Afterwards the placed schedule
        /// is measured and a schedule wider or taller than its slot is reported.
        /// Throws what ScheduleSheetInstance.Create throws.
        /// </summary>
        internal static ScheduleSheetInstance PlaceScheduleInSlot(Document doc, ElementId sheetId,
            ViewSchedule schedule, SlotPlacement sp, XYZ fallback, List<string> warnings)
        {
            XYZ pt = fallback ?? XYZ.Zero;
            if (sp?.Center != null && sp.HasSize)
                pt = new XYZ(sp.Center.X - sp.WidthFt / 2.0, sp.Center.Y + sp.HeightFt / 2.0, 0);

            var ssi = ScheduleSheetInstance.Create(doc, sheetId, schedule.Id, pt);
            if (ssi == null || sp == null || !sp.HasSize) return ssi;

            try
            {
                // A new instance has no extent until the document regenerates.
                doc.Regenerate();
                var sheet = doc.GetElement(sheetId) as View;
                var bb = ssi.get_BoundingBox(sheet);

                // DT-R11: the insertion point is not the visible corner — Revit 2025
                // measured the extent's top-left 2 mm left of the slot's. Move the
                // instance so its MEASURED top-left lands on the slot's top-left
                // (ScheduleSlotAlignment), then measure again.
                if (bb != null)
                {
                    var corr = ScheduleSlotAlignment.Compute(pt.X, pt.Y, bb.Min.X, bb.Max.Y);
                    if (corr.Refused)
                        warnings?.Add(
                            $"Schedule '{schedule.Name}': its extent's top-left is {(pt.X - bb.Min.X) * MmPerFt:0.0}, " +
                            $"{(pt.Y - bb.Max.Y) * MmPerFt:0.0} mm from slot '{sp.Slot?.Label}' — too far to be a border " +
                            "offset, so it was not moved. Check the schedule on the sheet.");
                    else if (corr.Needed)
                    {
                        ssi.Point = new XYZ(ssi.Point.X + corr.Dx, ssi.Point.Y + corr.Dy, ssi.Point.Z);
                        doc.Regenerate();
                        bb = ssi.get_BoundingBox(sheet);
                        if (bb != null && !ScheduleSlotAlignment.IsAligned(pt.X, pt.Y, bb.Min.X, bb.Max.Y,
                                ScheduleSlotAlignment.AlignToleranceFt * 5))
                            warnings?.Add(
                                $"Schedule '{schedule.Name}': top-left still at ({bb.Min.X * MmPerFt:0.0}, {bb.Max.Y * MmPerFt:0.0}) mm " +
                                $"after correction; slot '{sp.Slot?.Label}' top-left is ({pt.X * MmPerFt:0.0}, {pt.Y * MmPerFt:0.0}) mm.");
                    }
                }

                if (bb != null)
                {
                    double w = bb.Max.X - bb.Min.X, h = bb.Max.Y - bb.Min.Y;
                    double tolFt = MmToFt(1.0);
                    if (w > sp.WidthFt + tolFt || h > sp.HeightFt + tolFt)
                        warnings?.Add(
                            $"Schedule '{schedule.Name}' is {w * MmPerFt:0} x {h * MmPerFt:0} mm, larger than slot " +
                            $"'{sp.Slot?.Label}' ({sp.WidthFt * MmPerFt:0} x {sp.HeightFt * MmPerFt:0} mm) — it runs past the slot.");
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"SheetPlacementBridge.PlaceScheduleInSlot: could not measure '{schedule.Name}': {ex.Message}");
            }
            return ssi;
        }

        internal static PlacementResult PlaceAccordingToSlots(Document doc, ViewSheet sheet, DrawingType dt, List<ElementId> viewIds, ProduceResult result)
        {
            var pr = new PlacementResult();
            if (doc == null || sheet == null || dt == null || viewIds == null) return pr;
            int slotCount = dt.Slots?.Count ?? 0;
            var skippedViews = new List<ElementId>();

            // P1 — resolve the title-block family's slot grid once for this
            // sheet (null for norm-only profiles) so the per-view loop reuses
            // it instead of re-opening the family doc for every slot.
            var famCtx = BuildFamilySlotContext(doc, sheet, dt, result);
            for (int i = 0; i < viewIds.Count; i++)
            {
                // Skip views that have no slot defined — do NOT fall back to
                // Slots[0] as that causes multiple viewports stacked at the
                // same coordinate silently.
                if (slotCount > 0 && i >= slotCount)
                {
                    StingTools.Core.StingLog.Warn($"SheetPlacementBridge: view {i + 1} of {viewIds.Count} has no slot — DrawingType '{dt.Id}' defines only {slotCount} slot(s). View skipped. Add more slots to the profile.");
                    skippedViews.Add(viewIds[i]);
                    continue;
                }

                try
                {
                    var sp = ResolveSlot(doc, sheet.Id, dt, i, result, famCtx);
                    var pt = sp?.Center;
                    if (pt == null)
                    {
                        var ol = sheet.Outline;
                        pt = ol != null ? new XYZ((ol.Min.U + ol.Max.U) / 2.0, (ol.Min.V + ol.Max.V) / 2.0, 0) : XYZ.Zero;
                        StingTools.Core.StingLog.Warn($"SheetPlacementBridge.ResolveSlot: no title-block bounding box for sheet '{sheet?.SheetNumber}' — slot '{(dt.Slots != null && i < dt.Slots.Count ? dt.Slots[i]?.Label : null)}' falling back to sheet centre. Ensure a title block is placed on the sheet.");
                    }

                    // Per-slot Scale / DetailLevel / ViewTemplate overrides
                    // declared in the editor land on the view before it is
                    // placed, so a 1:20 detail slot can sit next to a 1:50
                    // overview on the same sheet.
                    DrawingSlot slot = null;
                    if (dt.Slots != null && i >= 0 && i < dt.Slots.Count) slot = dt.Slots[i];
                    if (slot != null && doc.GetElement(viewIds[i]) is View v)
                    {
                        try
                        {
                            DrawingTypePresentation.ApplySlotOverrides(doc, v, slot, null);
                        }
                        catch (Exception ex)
                        {
                            pr.Warnings.Add($"Slot overrides[{i}]: {ex.Message}");
                        }
                        // P12.A — fit the view to its slot when no explicit
                        // per-slot Scale override pins it. Runs after the slot
                        // overrides so an explicit pin always wins.
                        if (sp != null && slot.Scale == null)
                            ApplyFitScale(doc, v, sp, dt.Scale, pr.Warnings);
                    }

                    // SLOT-3: warn when view type doesn't match slot expectation
                    if (!string.IsNullOrWhiteSpace(slot?.ViewType))
                    {
                        bool compatible = IsViewTypeCompatible(doc.GetElement(viewIds[i]) as View, slot.ViewType);
                        if (!compatible)
                            StingTools.Core.StingLog.Warn($"SheetPlacementBridge: view '{(doc.GetElement(viewIds[i]) as View)?.Name}' (type {(doc.GetElement(viewIds[i]) as View)?.ViewType}) placed into slot '{slot.Label}' expecting '{slot.ViewType}' — type mismatch.");
                    }

                    // AUTO-3: Schedule views must be placed as ScheduleSheetInstance,
                    // not Viewport — Viewport.Create throws on ViewSchedule elements.
                    if (doc.GetElement(viewIds[i]) is ViewSchedule scheduleView)
                    {
                        try
                        {
                            var ssi = PlaceScheduleInSlot(doc, sheet.Id, scheduleView, sp, pt, pr.Warnings); // DTW-151
                            if (ssi != null)
                            {
                                pr.ViewportIds.Add(ssi.Id);
                                // DTW-75: the "placed by STING" mark is Extensible Storage, not
                                // STING_AUTO_PLACED_BOOL. A schedule instance (and a viewport)
                                // is not a category a shared parameter can be bound to, so the
                                // parameter write returned false on every placement.
                                MarkAutoPlaced(ssi, pr);
                            }
                        }
                        catch (Exception ex)
                        {
                            StingTools.Core.StingLog.Warn($"SheetPlacementBridge: ScheduleSheetInstance.Create failed for '{scheduleView.Name}' — {ex.Message}. Skipping.");
                            pr.Warnings.Add($"ScheduleSheetInstance.Create('{scheduleView.Name}'): {ex.Message}");
                        }
                        continue;  // skip Viewport.Create below
                    }

                    var vp = Viewport.Create(doc, sheet.Id, viewIds[i], pt);
                    if (vp != null)
                    {
                        pr.ViewportIds.Add(vp.Id);
                        // DTW-75: the viewport branch of the same mark.
                        MarkAutoPlaced(vp, pr);

                        // SLOT-1: apply per-slot viewport type if declared
                        if (!string.IsNullOrWhiteSpace(slot?.ViewportType))
                        {
                            var vpTypeId = FindViewportTypeId(doc, slot.ViewportType);
                            if (vpTypeId != null && vpTypeId != ElementId.InvalidElementId)
                            {
                                try { vp.ChangeTypeId(vpTypeId); }
                                catch (Exception ex) { StingTools.Core.StingLog.Warn($"SheetPlacementBridge: ChangeTypeId('{slot.ViewportType}') failed — {ex.Message}"); }
                            }
                            else
                            {
                                StingTools.Core.StingLog.Warn($"SheetPlacementBridge: viewport type '{slot.ViewportType}' not found in document — slot '{slot?.Label}' uses default.");
                            }
                        }
                        ReportViewportOverflow(vp, sp, pr.Warnings); // DTW-157
                    }
                }
                catch (Exception ex) { pr.Warnings.Add($"PlaceAccordingToSlots[{i}]: {ex.Message}"); }
            }
            if (skippedViews.Count > 0)
                StingTools.Core.StingLog.Warn($"SheetPlacementBridge: {skippedViews.Count} view(s) had no slot and were skipped on sheet '{sheet?.SheetNumber}'. Profile: '{dt?.Id}'.");
            return pr;
        }

        // ── P1 — unified slot model helpers ─────────────────────────────────

        /// <summary>The live title-block family's slot grid (bounds in feet)
        /// plus the alias rules, resolved once per sheet and threaded through
        /// the placement loop so each slot doesn't re-open the family doc.</summary>
        internal sealed class FamilySlotContext
        {
            public Dictionary<string, StingTools.Commands.Drawing.SlotBounds> Map { get; set; }
            public StingTools.Commands.Drawing.ViewportPlacementRules Rules { get; set; }
            /// <summary>P12.C — the title-block instance LocationPoint (feet).
            /// Family slot bounds are relative to the family origin; adding this
            /// places views correctly even when the title block is moved off
            /// (0,0). Defaults to origin.</summary>
            public XYZ TitleBlockOrigin { get; set; } = XYZ.Zero;
        }

        private static bool SlotOptsIntoFamilyGrid(DrawingSlot slot)
            => slot != null && (!string.IsNullOrWhiteSpace(slot.PurposeTag)
                             || !string.IsNullOrWhiteSpace(slot.SlotRef));

        /// <summary>Build the title-block family's slot map for a sheet — but
        /// ONLY when the profile actually uses the unified model. Norm-only
        /// profiles return null here and never pay the (EditFamily) cost, so
        /// their historic behaviour is byte-for-byte unchanged. Returns null
        /// when there's no title block, no matching spec, or an empty slot
        /// set.</summary>
        internal static FamilySlotContext BuildFamilySlotContext(Document doc, ViewSheet sheet, DrawingType dt, ProduceResult result)
        {
            try
            {
                if (doc == null || sheet == null || dt?.Slots == null) return null;
                if (!dt.Slots.Any(SlotOptsIntoFamilyGrid)) return null;

                var tb = StingTools.Commands.Drawing.TitleBlockSlotUtils.FindTitleBlockOnSheet(doc, sheet);
                if (tb == null) return null;
                var map = StingTools.Commands.Drawing.TitleBlockSlotUtils.ReadSlotBoundsFromTitleBlock(doc, tb);
                if (map == null || map.Count == 0) return null;
                return new FamilySlotContext
                {
                    Map             = map,
                    Rules           = StingTools.Commands.Drawing.ViewportPlacementRules.Load(),
                    TitleBlockOrigin = GetTitleBlockOrigin(tb),   // P12.C
                };
            }
            catch (Exception ex)
            {
                result?.Warnings.Add($"SheetPlacementBridge.BuildFamilySlotContext: {ex.Message}");
                return null;
            }
        }

        /// <summary>Resolve a DrawingSlot against the family slot grid:
        /// PurposeTag (semantic, exact then alias chain) first, then SlotRef
        /// (exact family slot id). Returns null when neither resolves, letting
        /// the caller fall back to the norm* subdivision.</summary>
        private static StingTools.Commands.Drawing.SlotBounds ResolveUnifiedSlotBounds(
            DrawingSlot slot, FamilySlotContext ctx, ProduceResult result = null)
        {
            if (slot == null || ctx?.Map == null || ctx.Map.Count == 0) return null;

            // 1. PurposeTag — preferred, semantic, portable across paper sizes.
            if (!string.IsNullOrWhiteSpace(slot.PurposeTag))
            {
                var id = StingTools.Commands.Drawing.TitleBlockSlotUtils
                    .ResolveSlotIdForTag(ctx.Map, slot.PurposeTag, ctx.Rules);
                if (!string.IsNullOrEmpty(id) && ctx.Map.TryGetValue(id, out var b) && b?.Bbox != null)
                    return b;
            }
            // 2. SlotRef — exact family slot id.
            if (!string.IsNullOrWhiteSpace(slot.SlotRef)
                && ctx.Map.TryGetValue(slot.SlotRef, out var byId) && byId?.Bbox != null)
                return byId;

            // SLOT-4: neither key resolved. The caller falls back to the norm*
            // fractions, which usually LOOK plausible — so a mistyped purposeTag
            // or a slotRef the family doesn't carry used to place the view in a
            // silently different pocket with no clue why. Say so, and list what
            // the family actually offers. Mirrors the existing SLOT-3 warning on
            // view/slot TYPE mismatch, which was the only one of the pair present.
            if (result != null)
            {
                var wanted = !string.IsNullOrWhiteSpace(slot.PurposeTag)
                    ? $"purposeTag '{slot.PurposeTag}'"
                    : $"slotRef '{slot.SlotRef}'";
                var offered = string.Join(", ", ctx.Map.Values
                    .Select(b => string.IsNullOrWhiteSpace(b?.PurposeTag) ? b?.Id : $"{b.Id}={b.PurposeTag}")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .OrderBy(s => s));
                result.Warnings.Add(
                    $"Slot '{slot.Label}': {wanted} did not match the title-block family's slot grid — " +
                    $"fell back to the normX/normY fractions, so the view may not land where the profile intends. " +
                    $"Family offers: {offered}.");
            }

            return null;
        }

        // SLOT-1 helper — resolves a viewport type ElementId by name.
        /// <summary>Public face of FindViewportTypeId for callers outside
        /// this class (the producer's per-slot viewport type override).</summary>
        internal static ElementId ResolveViewportTypeId(Document doc, string typeName)
            => FindViewportTypeId(doc, typeName);

        // Viewport naming: one resolver (ViewportTypeResolver) that knows the
        // canonical STING names, their legacy aliases, and mints a missing
        // canonical STING type by duplication. Callers are inside the
        // placement transaction. Non-STING names are looked up, never minted.
        private static ElementId FindViewportTypeId(Document doc, string typeName)
            => ViewportTypeResolver.Resolve(doc, typeName, createIfMissing: true);

        // SLOT-3 helper. The mapping from STING slot terms to Revit view types
        // lives in the Revit-free SlotViewTypeCompatibility so the producer
        // (DTW-63) and this bridge share one copy and it can be unit-tested.
        /// <summary>
        /// The slot viewType terms the compatibility predicate discriminates on;
        /// see <see cref="SlotViewTypeCompatibility.KnownSlotViewTypes"/>.
        /// </summary>
        public static readonly string[] KnownSlotViewTypes = SlotViewTypeCompatibility.KnownSlotViewTypes;

        /// <summary>True when the term is one the compatibility switch discriminates on.</summary>
        public static bool IsKnownSlotViewType(string slotViewType)
            => SlotViewTypeCompatibility.IsKnown(slotViewType);

        private static bool IsViewTypeCompatible(View view, string slotViewType)
            => view == null || SlotViewTypeCompatibility.IsCompatible(view.ViewType.ToString(), slotViewType);
    }
}
