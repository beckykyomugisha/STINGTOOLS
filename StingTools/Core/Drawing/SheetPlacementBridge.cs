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
        public bool HasSize => WidthFt > 1e-9 && HeightFt > 1e-9;
    }

    internal static class SheetPlacementBridge
    {
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
                    var bounds = ResolveUnifiedSlotBounds(slot, ctx);
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

        private static readonly int[] StandardScales =
            { 1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 1250, 2000, 2500, 5000, 10000 };

        /// <summary>P12.A — set the view's scale so its paper footprint fits
        /// inside the slot rect. Computes the required scale from the view's
        /// crop-region outline (paper feet at its current scale) versus the
        /// slot's paper width/height, rounds UP to the next standard scale, and
        /// treats <see cref="SlotPlacement.ScaleHint"/> as a floor/override
        /// (used when it also fits). Only applied to cropped graphical views;
        /// schedules / legends / 3D and uncropped views are left untouched.
        /// Never throws.</summary>
        internal static void ApplyFitScale(Document doc, View v, SlotPlacement sp)
        {
            if (v == null || sp == null || !sp.HasSize) return;
            if (!IsScalableView(v)) return;
            try
            {
                if (!v.CropBoxActive) return; // can't measure intended footprint
                var outline = v.Outline;
                if (outline == null) return;
                double curW = outline.Max.U - outline.Min.U;
                double curH = outline.Max.V - outline.Min.V;
                if (curW < 1e-9 || curH < 1e-9) return;
                int curScale = v.Scale > 0 ? v.Scale : 100;
                double fit = Math.Max(curW * curScale / sp.WidthFt,
                                      curH * curScale / sp.HeightFt);
                int fitScale = RoundUpToStandardScale(fit);
                int target = sp.ScaleHint.HasValue ? Math.Max(fitScale, sp.ScaleHint.Value) : fitScale;
                if (target > 0 && target != v.Scale)
                {
                    try { v.Scale = target; } catch { /* view type rejects scale */ }
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"SheetPlacementBridge.ApplyFitScale: {ex.Message}");
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

        private static int RoundUpToStandardScale(double v)
        {
            if (v <= 1) return 1;
            foreach (var s in StandardScales) if (s >= v - 1e-9) return s;
            return (int)(Math.Ceiling(v / 1000.0) * 1000);
        }

        private static XYZ GetTitleBlockOrigin(Element titleBlock)
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
        // (extends-resolved). Corporate baseline is read-only at runtime so a
        // one-time cache is safe.
        private static Dictionary<string, StingTools.Core.Drawing.DrawableRect> _drawableCache;
        private static readonly object _drawableLock = new object();

        private static StingTools.Core.Drawing.DrawableRect ResolveDrawableForFamily(string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return null;
            var cache = _drawableCache;
            if (cache == null)
            {
                lock (_drawableLock)
                {
                    cache = _drawableCache;
                    if (cache == null)
                    {
                        cache = new Dictionary<string, StingTools.Core.Drawing.DrawableRect>(StringComparer.OrdinalIgnoreCase);
                        try
                        {
                            var lib = StingTools.Core.Drawing.TitleBlockSpecRegistry.Load();
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
                            StingTools.Core.StingLog.Warn($"SheetPlacementBridge.ResolveDrawableForFamily: {ex.Message}");
                        }
                        _drawableCache = cache;
                    }
                }
            }
            return cache.TryGetValue(familyName, out var d) ? d : null;
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
                            ApplyFitScale(doc, v, sp);
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
                            var ssi = ScheduleSheetInstance.Create(doc, sheet.Id, scheduleView.Id, pt);
                            if (ssi != null)
                            {
                                pr.ViewportIds.Add(ssi.Id);
                                try { StingTools.Core.ParameterHelpers.SetInt(ssi, ParamRegistry.STING_AUTO_PLACED_BOOL, 1, overwrite: true); } catch { }
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
                        try { StingTools.Core.ParameterHelpers.SetInt(vp, ParamRegistry.STING_AUTO_PLACED_BOOL, 1, overwrite: true); } catch { }

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
        private static StingTools.Commands.Drawing.SlotBounds ResolveUnifiedSlotBounds(DrawingSlot slot, FamilySlotContext ctx)
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

            return null;
        }

        // SLOT-1 helper — resolves a viewport type ElementId by name.
        private static ElementId FindViewportTypeId(Document doc, string typeName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ElementType))
                .Cast<ElementType>()
                .FirstOrDefault(t => t.Category?.Id?.Value == (long)BuiltInCategory.OST_Viewports
                                  && string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase))
                ?.Id;
        }

        // SLOT-3 helper — returns true when the view's ViewType is compatible
        // with the slot's declared ViewType string. Unknown slot types pass
        // through as compatible (returns true) to avoid false positives.
        private static bool IsViewTypeCompatible(View view, string slotViewType)
        {
            if (view == null || string.IsNullOrWhiteSpace(slotViewType)) return true;
            return slotViewType.ToUpperInvariant() switch
            {
                "PLAN"      => view.ViewType == ViewType.FloorPlan || view.ViewType == ViewType.AreaPlan || view.ViewType == ViewType.EngineeringPlan,
                "RCP"       => view.ViewType == ViewType.CeilingPlan,
                "SECTION"   => view.ViewType == ViewType.Section,
                "ELEVATION" => view.ViewType == ViewType.Elevation,
                "DETAIL"    => view.ViewType == ViewType.Detail,
                "3D"        => view.ViewType == ViewType.ThreeD,
                "SCHEDULE"  => view.ViewType == ViewType.Schedule,
                "LEGEND"    => view.ViewType == ViewType.Legend,
                "ISO"       => view.ViewType == ViewType.ThreeD,
                "SCHEMATIC" => view.ViewType == ViewType.DraftingView || view.ViewType == ViewType.Elevation,
                _           => true  // unknown slot type — allow
            };
        }
    }
}
