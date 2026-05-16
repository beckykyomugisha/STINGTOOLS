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
            for (int i = 0; i < viewIds.Count; i++)
            {
                try
                {
                    var pt = GetSlotPosition(doc, sheet.Id, dt, i, result);
                    if (pt == null)
                    {
                        var ol = sheet.Outline;
                        pt = ol != null ? new XYZ((ol.Min.U + ol.Max.U) / 2.0, (ol.Min.V + ol.Max.V) / 2.0, 0) : XYZ.Zero;
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

                    var vp = Viewport.Create(doc, sheet.Id, viewIds[i], pt);
                    if (vp != null)
                    {
                        pr.ViewportIds.Add(vp.Id);
                        try { StingTools.Core.ParameterHelpers.SetInt(vp, ParamRegistry.STING_AUTO_PLACED_BOOL, 1, overwrite: true); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    }
                }
                catch (Exception ex) { pr.Warnings.Add($"PlaceAccordingToSlots[{i}]: {ex.Message}"); }
            }
            return pr;
        }
    }
}
