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
    /// <summary>
    /// H-1 — the outcome of a stamp run, separating what we LOOKED AT from what
    /// we actually WROTE. The old single "stamped" count was incremented once per
    /// element visited, regardless of whether any parameter took a value, so a
    /// project with the Qto_* shared params unbound reported thousands of
    /// successful stamps and exported an IFC with no quantities in it.
    /// </summary>
    internal sealed class IfcStampTally
    {
        /// <summary>BOQ rows that resolved to a live element — visits, not writes.</summary>
        public int ElementsVisited;

        /// <summary>Elements where at least one parameter actually took a value.</summary>
        public int ElementsWritten;

        /// <summary>Individual parameter writes that landed, of every kind.</summary>
        public int ParametersWritten;

        /// <summary>
        /// Qto_*BaseQuantities writes ONLY — the actual deliverable of this command.
        /// Counted separately because the two parameter families have very different
        /// odds of being bound. Pset_StingCost.* are STING's own shared parameters,
        /// bound as a matter of course by LoadSharedParams; the Qto_* names are
        /// IFC-standard and must be added deliberately. The likeliest field
        /// configuration is therefore "Pset bound, Qto not" — which a combined
        /// counter reads as success while the IFC carries no quantities at all.
        /// </summary>
        public int QuantitiesWritten;

        /// <summary>True when the run produced no model change whatsoever.</summary>
        public bool WroteNothing => ParametersWritten == 0;

        /// <summary>
        /// True when no IFC quantity landed. This — not <see cref="WroteNothing"/> —
        /// is the export gate: an IFC with cost data and zero quantities is the
        /// defect this whole change exists to prevent.
        /// </summary>
        public bool WroteNoQuantities => QuantitiesWritten == 0;
    }

    internal static class IfcQuantitySetWriter
    {
        /// <summary>
        /// Stamp Qto_*.* + Pset_StingCost.* shared params on every BOQ line.
        /// Returns a tally distinguishing elements visited from parameters
        /// actually written — see <see cref="IfcStampTally"/>.
        /// </summary>
        public static IfcStampTally StampAllElements(Document doc, BOQDocument boq)
        {
            var tally = new IfcStampTally();
            if (doc == null || boq == null) return tally;
            foreach (var item in boq.AllItems)
            {
                if (item.RevitElementId <= 0) continue;
                try
                {
                    var el = doc.GetElement(new ElementId(item.RevitElementId));
                    if (el == null) continue;
                    tally.ElementsVisited++;
                    int wroteHere = 0;   // any parameter family
                    int qtyHere   = 0;   // Qto_* only — see IfcStampTally.QuantitiesWritten

                    // IFC4 Qto_*  fields per category. Only set the ones
                    // we can compute from BOQLineItem.
                    string qtoSetName = ResolveQtoSetName(item.Category);
                    if (!string.IsNullOrEmpty(qtoSetName))
                    {
                        // P0-1 — item.Unit is ASCII ("m2"/"m3"/"each"), not the
                        // Unicode glyphs ("m²"/"m³") the old comparisons used, so
                        // every quantity was tested false and StampQuantity's
                        // `value <= 0` guard skipped it. Canonicalise through the
                        // BOQ engine's own table so both sides match.
                        string u = BOQCostManager.NormaliseUnit(item.Unit);
                        // CA-5 — Net* = the NET measured quantity (after NRM2/CESMM
                        // deductions); Gross* = the GROSS modelled quantity. The old
                        // code stamped the net value into BOTH Gross* and Net*, which
                        // mis-reported the gross as net-of-deductions. Fall back to net
                        // when no gross was captured (legacy/aggregated rows).
                        double q = item.Quantity;                                   // net
                        double g = item.GrossQuantity > 0 ? item.GrossQuantity : q;  // gross
                        // Every StampQuantity below is an IFC quantity, so it feeds
                        // qtyHere as well as the all-families wroteHere.
                        if (u == "m2")
                        {
                            if (StampQuantity(el, qtoSetName, "GrossArea", g)) qtyHere++;
                            if (StampQuantity(el, qtoSetName, "NetArea",   q)) qtyHere++;
                        }
                        else if (u == "m3")
                        {
                            if (StampQuantity(el, qtoSetName, "GrossVolume", g)) qtyHere++;
                            if (StampQuantity(el, qtoSetName, "NetVolume",   q)) qtyHere++;
                        }
                        else if (u == "m")
                        {
                            if (StampQuantity(el, qtoSetName, "Length", q)) qtyHere++;
                        }
                        else if (u == "kg")
                        {
                            if (StampQuantity(el, qtoSetName, "GrossWeight", g)) qtyHere++;
                            if (StampQuantity(el, qtoSetName, "NetWeight",   q)) qtyHere++;
                        }
                        else if (u == "each")
                        {
                            // Count is integer-valued in IFC Qto sets but our
                            // params are typically Double/String — StampQuantity
                            // handles both storage types.
                            if (StampQuantity(el, qtoSetName, "Count", q)) qtyHere++;
                        }
                        wroteHere += qtyHere;
                    }

                    // STING-specific cost property set.
                    if (StampString(el,  "Pset_StingCost", "Currency",       "UGX")) wroteHere++;
                    if (StampNumber(el,  "Pset_StingCost", "UnitRate",       item.RateUGX)) wroteHere++;
                    if (StampNumber(el,  "Pset_StingCost", "TotalCost",      item.TotalUGX)) wroteHere++;
                    if (StampString(el,  "Pset_StingCost", "RateSource",     item.RateSource ?? "")) wroteHere++;
                    if (StampString(el,  "Pset_StingCost", "NRM2Section",    item.NRM2Section ?? "")) wroteHere++;
                    if (StampBoolean(el, "Pset_StingCost", "ProvisionalSum", item.Source == BOQRowSource.ProvisionalSum)) wroteHere++;

                    // I-1 — Pset_EnvironmentalImpactIndicators carries the
                    // material's embodied carbon + EPD provenance + Uniclass
                    // code so external LCA tooling that reads the standard
                    // IFC4 environmental impact Pset can consume them
                    // without a STING-specific schema.
                    wroteHere += StingTools.UI.IfcMaterialPsetWriter.Stamp(el, item);

                    // H-1 — was an unconditional `stamped++` here, which counted
                    // the visit rather than the write.
                    if (wroteHere > 0)
                    {
                        tally.ElementsWritten++;
                        tally.ParametersWritten += wroteHere;
                    }
                    tally.QuantitiesWritten += qtyHere;
                }
                catch (Exception ex) { StingLog.Warn($"IfcQuantitySetWriter on {item.RevitElementId}: {ex.Message}"); }
            }

            if (tally.WroteNothing)
                StingLog.Warn($"IfcQuantitySetWriter: visited {tally.ElementsVisited} element(s) and wrote NOTHING — " +
                              "neither the Qto_* nor the Pset_StingCost shared parameters are bound in this project, " +
                              "so an IFC exported now will carry no quantities and no cost. Load the shared " +
                              "parameters and re-run.");
            else if (tally.WroteNoQuantities)
                StingLog.Warn($"IfcQuantitySetWriter: visited {tally.ElementsVisited} element(s) and wrote " +
                              $"{tally.ParametersWritten} parameter(s), but ZERO IFC quantities. The Pset_StingCost " +
                              "parameters are bound and the Qto_*BaseQuantities ones are not, so an IFC exported now " +
                              "would carry cost data against no measured quantities — the exact shape of deliverable " +
                              "this command exists to prevent. Add the Qto_* shared parameters and re-run.");
            else
                StingLog.Info($"IfcQuantitySetWriter: visited {tally.ElementsVisited} element(s); wrote " +
                              $"{tally.QuantitiesWritten} quantity + {tally.ParametersWritten} total parameter(s) " +
                              $"across {tally.ElementsWritten} element(s).");
            return tally;
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

        // H-1 — every helper below returns TRUE only when the value actually
        // landed on a parameter. The Qto_* and Pset_* names are shared params
        // that must be pre-bound; unbound, LookupParameter returns null and the
        // write is a silent no-op. Reporting those as successes is what let the
        // command certify an IFC carrying zero quantities.

        private static bool StampNumber(Element el, string set, string field, double value)
        {
            string p = $"{set}.{field}";
            try
            {
                Parameter par = el.LookupParameter(p);
                if (par == null || par.IsReadOnly) return false;
                if (par.StorageType == StorageType.Double)
                    return par.Set(value);
                if (par.StorageType == StorageType.String)
                    return par.Set(value.ToString("F4", CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { StingLog.Warn($"StampNumber {p}: {ex.Message}"); }
            return false;
        }

        private static bool StampString(Element el, string set, string field, string value)
        {
            string p = $"{set}.{field}";
            try
            {
                Parameter par = el.LookupParameter(p);
                if (par == null || par.IsReadOnly) return false;
                if (par.StorageType == StorageType.String) return par.Set(value ?? "");
            }
            catch (Exception ex) { StingLog.Warn($"StampString {p}: {ex.Message}"); }
            return false;
        }

        private static bool StampBoolean(Element el, string set, string field, bool value)
        {
            string p = $"{set}.{field}";
            try
            {
                Parameter par = el.LookupParameter(p);
                if (par == null || par.IsReadOnly) return false;
                if (par.StorageType == StorageType.Integer) return par.Set(value ? 1 : 0);
            }
            catch (Exception ex) { StingLog.Warn($"StampBoolean {p}: {ex.Message}"); }
            return false;
        }

        private static bool StampQuantity(Element el, string set, string field, double value)
        {
            if (value <= 0) return false;
            return StampNumber(el, set, field, value);
        }
    }
}
