// ══════════════════════════════════════════════════════════════════════════
//  IfcQuantitySetWriter.cs — Populate IFC4 Qto_* property sets so
//  external cost tools (Cost-X, CostOS, Candy, Bluebeam Revu) can read
//  BOQ quantities directly from the IFC without re-measuring geometry.
//
//  Strategy: Revit's IFC exporter consumes the value of the
//  `IfcExportAs` parameter and any matching shared parameter prefixed
//  with `Qto_<entity>.Quantity` to populate IFC4 quantity sets. We
//  stamp those params from BOQ line items inside a transaction so the
//  next IFC export carries them.
//
//  IFC4 Qto sets per category (per buildingSMART):
//    Wall      → Qto_WallBaseQuantities  (Length, Width, Height,
//                                         GrossSideArea, NetSideArea,
//                                         GrossVolume, NetVolume)
//    Beam      → Qto_BeamBaseQuantities  (Length, CrossSectionArea,
//                                         OuterSurfaceArea, GrossVolume,
//                                         NetVolume)
//    Slab      → Qto_SlabBaseQuantities  (Width, Length, Depth,
//                                         Perimeter, GrossArea,
//                                         NetArea, GrossVolume,
//                                         NetVolume, GrossWeight,
//                                         NetWeight)
//    Space     → Qto_SpaceBaseQuantities (Height, FinishCeilingHeight,
//                                         GrossPerimeter, NetPerimeter,
//                                         GrossFloorArea, NetFloorArea,
//                                         GrossWallArea, NetWallArea,
//                                         GrossVolume, NetVolume)
//
//  STING extension: Pset_StingCost — non-standard but namespaced cleanly.
//    Pset_StingCost.UnitRate        (double)
//    Pset_StingCost.Currency        (string)
//    Pset_StingCost.TotalCost       (double)
//    Pset_StingCost.ProvisionalSum  (bool)
//    Pset_StingCost.RateSource      (string)
//    Pset_StingCost.NRM2Section     (string)
//
//  Caller must have an active transaction open.
//
//  P8 of the Cost Management Implementation Plan.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.BOQ
{
    internal static class IfcQuantitySetWriter
    {
        /// <summary>
        /// Stamp Qto_*.* + Pset_StingCost.* shared params on every BOQ
        /// line. Returns the number of elements successfully stamped.
        /// </summary>
        public static int StampAllElements(Document doc, BOQDocument boq)
        {
            if (doc == null || boq == null) return 0;
            int stamped = 0;
            foreach (var item in boq.AllItems)
            {
                if (item.RevitElementId <= 0) continue;
                try
                {
                    var el = doc.GetElement(new ElementId(item.RevitElementId));
                    if (el == null) continue;

                    // IFC4 Qto_*  fields per category. Only set the ones
                    // we can compute from BOQLineItem.
                    string qtoSetName = ResolveQtoSetName(item.Category);
                    if (!string.IsNullOrEmpty(qtoSetName))
                    {
                        StampQuantity(el, qtoSetName, "GrossArea", item.Unit == "m²" ? item.Quantity : 0);
                        StampQuantity(el, qtoSetName, "NetArea",   item.Unit == "m²" ? item.Quantity : 0);
                        StampQuantity(el, qtoSetName, "GrossVolume", item.Unit == "m³" ? item.Quantity : 0);
                        StampQuantity(el, qtoSetName, "NetVolume",   item.Unit == "m³" ? item.Quantity : 0);
                        StampQuantity(el, qtoSetName, "Length",      item.Unit == "m"  ? item.Quantity : 0);
                        StampQuantity(el, qtoSetName, "GrossWeight", item.Unit == "kg" ? item.Quantity : 0);
                        StampQuantity(el, qtoSetName, "NetWeight",   item.Unit == "kg" ? item.Quantity : 0);
                    }

                    // STING-specific cost property set.
                    StampString(el,  "Pset_StingCost", "Currency",        "UGX");
                    StampNumber(el,  "Pset_StingCost", "UnitRate",        item.RateUGX);
                    StampNumber(el,  "Pset_StingCost", "TotalCost",       item.TotalUGX);
                    StampString(el,  "Pset_StingCost", "RateSource",      item.RateSource ?? "");
                    StampString(el,  "Pset_StingCost", "NRM2Section",     item.NRM2Section ?? "");
                    StampBoolean(el, "Pset_StingCost", "ProvisionalSum",  item.Source == BOQRowSource.ProvisionalSum);

                    // I-1 — Pset_EnvironmentalImpactIndicators carries the
                    // material's embodied carbon + EPD provenance + Uniclass
                    // code so external LCA tooling that reads the standard
                    // IFC4 environmental impact Pset can consume them
                    // without a STING-specific schema.
                    StingTools.UI.IfcMaterialPsetWriter.Stamp(el, item);
                    stamped++;
                }
                catch (Exception ex) { StingLog.Warn($"IfcQuantitySetWriter on {item.RevitElementId}: {ex.Message}"); }
            }
            StingLog.Info($"IfcQuantitySetWriter: stamped {stamped} element(s) with Qto_* + Pset_StingCost.");
            return stamped;
        }

        /// <summary>
        /// Map Revit category name → IFC4 Qto set name. Returns empty
        /// string for categories that have no standard Qto set; the
        /// Pset_StingCost layer still applies.
        /// </summary>
        private static string ResolveQtoSetName(string categoryName)
        {
            string lower = (categoryName ?? "").ToLowerInvariant();
            if (lower.Contains("wall")) return "Qto_WallBaseQuantities";
            if (lower.Contains("beam") || lower.Contains("framing")) return "Qto_BeamBaseQuantities";
            if (lower.Contains("column")) return "Qto_ColumnBaseQuantities";
            if (lower.Contains("slab") || lower.Contains("floor")) return "Qto_SlabBaseQuantities";
            if (lower.Contains("roof")) return "Qto_SlabBaseQuantities";  // closest analogue
            if (lower.Contains("ceiling")) return "Qto_CoveringBaseQuantities";
            if (lower.Contains("door")) return "Qto_DoorBaseQuantities";
            if (lower.Contains("window")) return "Qto_WindowBaseQuantities";
            if (lower.Contains("space") || lower.Contains("room")) return "Qto_SpaceBaseQuantities";
            if (lower.Contains("pipe")) return "Qto_PipeSegmentBaseQuantities";
            if (lower.Contains("duct")) return "Qto_DuctSegmentBaseQuantities";
            return "";
        }

        private static void StampNumber(Element el, string set, string field, double value)
        {
            string p = $"{set}.{field}";
            try
            {
                Parameter par = el.LookupParameter(p);
                if (par == null || par.IsReadOnly) return;
                if (par.StorageType == StorageType.Double)
                    par.Set(value);
                else if (par.StorageType == StorageType.String)
                    par.Set(value.ToString("F4", CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { StingLog.Warn($"StampNumber {p}: {ex.Message}"); }
        }

        private static void StampString(Element el, string set, string field, string value)
        {
            string p = $"{set}.{field}";
            try
            {
                Parameter par = el.LookupParameter(p);
                if (par == null || par.IsReadOnly) return;
                if (par.StorageType == StorageType.String) par.Set(value ?? "");
            }
            catch (Exception ex) { StingLog.Warn($"StampString {p}: {ex.Message}"); }
        }

        private static void StampBoolean(Element el, string set, string field, bool value)
        {
            string p = $"{set}.{field}";
            try
            {
                Parameter par = el.LookupParameter(p);
                if (par == null || par.IsReadOnly) return;
                if (par.StorageType == StorageType.Integer) par.Set(value ? 1 : 0);
            }
            catch (Exception ex) { StingLog.Warn($"StampBoolean {p}: {ex.Message}"); }
        }

        private static void StampQuantity(Element el, string set, string field, double value)
        {
            if (value <= 0) return;
            StampNumber(el, set, field, value);
        }
    }
}
