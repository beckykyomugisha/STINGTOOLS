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

namespace StingTools.Core.Drawing
{
    internal sealed class PlacementResult
    {
        public List<ElementId> ViewportIds { get; } = new List<ElementId>();
        public List<string> Warnings { get; } = new List<string>();
    }

    internal static class SheetPlacementBridge
    {
        private const double MarginMm = 25.0;
        private const double MmPerFt = 304.8;

        internal static XYZ GetSlotPosition(Document doc, ElementId sheetId, DrawingType dt, int slotIndex, ProduceResult result)
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

                var bb = titleBlock.get_BoundingBox(sheet);
                if (bb == null) return null;
                double margin = MarginMm / MmPerFt;

                double zoneMinX = bb.Min.X + margin;
                double zoneMinY = bb.Min.Y + margin;
                double zoneW = (bb.Max.X - bb.Min.X) - 2 * margin;
                double zoneH = (bb.Max.Y - bb.Min.Y) - 2 * margin;

                double cx = zoneMinX + (slot.NormX + slot.NormW / 2.0) * zoneW;
                double cy = zoneMinY + (slot.NormY + slot.NormH / 2.0) * zoneH;
                return new XYZ(cx, cy, 0);
            }
            catch (Exception ex)
            {
                result?.Warnings.Add($"SheetPlacementBridge.GetSlotPosition: {ex.Message}");
                return null;
            }
        }

        internal static PlacementResult PlaceAccordingToSlots(Document doc, ViewSheet sheet, DrawingType dt, List<ElementId> viewIds, ProduceResult result)
        {
            var pr = new PlacementResult();
            if (doc == null || sheet == null || dt == null || viewIds == null) return pr;
            int slotCount = dt.Slots?.Count ?? 0;
            var skippedViews = new List<ElementId>();
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
                    var pt = GetSlotPosition(doc, sheet.Id, dt, i, result);
                    if (pt == null)
                    {
                        var ol = sheet.Outline;
                        pt = ol != null ? new XYZ((ol.Min.U + ol.Max.U) / 2.0, (ol.Min.V + ol.Max.V) / 2.0, 0) : XYZ.Zero;
                        StingTools.Core.StingLog.Warn($"SheetPlacementBridge.GetSlotPosition: no title-block bounding box for sheet '{sheet?.SheetNumber}' — slot '{(dt.Slots != null && i < dt.Slots.Count ? dt.Slots[i]?.Label : null)}' falling back to sheet centre. Ensure a title block is placed on the sheet.");
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
