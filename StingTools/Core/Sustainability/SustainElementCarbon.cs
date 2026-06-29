// StingTools — Per-element embodied carbon (WS H4, Revit-facing).
//
// One carbon source for the whole module. The dashboard materials take-off
// aggregates by material across elements; the AVF carbon heat-map needs the SAME
// carbon PER ELEMENT. Rather than reimplement a carbon engine for the heat-map
// (the old adapter required a pre-stamped STING_CO2_KG that nothing writes), this
// helper computes a single element's embodied carbon through the EXACT same chain
// the take-off uses — CarbonFactorResolver (per-m³ / per-kg-via-density) + the pure
// SustainMaterialCarbon.Compute — so the heat-map and the dashboard agree.
//
// Revit-facing (reads element geometry) — NOT in the test project. The arithmetic
// it delegates to (SustainMaterialCarbon) is the pure, unit-tested engine.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.BOQ;
using StingTools.Core;

namespace StingTools.Core.Sustainability
{
    public static class SustainElementCarbon
    {
        /// <summary>WBLCA scope (structure + enclosure + reinforcement) — the same
        /// categories the dashboard take-off walks.</summary>
        public static readonly BuiltInCategory[] WblcaCategories =
        {
            BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_Columns, BuiltInCategory.OST_Stairs, BuiltInCategory.OST_Ramps,
            BuiltInCategory.OST_CurtainWallPanels, BuiltInCategory.OST_CurtainWallMullions,
            BuiltInCategory.OST_Rebar, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows
        };

        /// <summary>Net embodied carbon (kgCO₂e, A1–A3 incl. biogenic credit) for one
        /// element, summed across its materials via the shared resolver chain. 0 when
        /// nothing resolves. WS H4.</summary>
        public static double EmbodiedKg(Document doc, Element el, FactorSourceOrder order = null)
        {
            if (doc == null || el == null) return 0;
            order = order ?? new FactorSourceOrder();
            double wastePct = TagConfig.GetConfigDouble("COST_DEFAULT_WASTE_PCT", 5.0);
            double total = 0;
            try
            {
                ICollection<ElementId> matIds = el.GetMaterialIds(false);
                if (matIds == null) return 0;
                foreach (var mid in matIds)
                {
                    double vol;
                    try { vol = el.GetMaterialVolume(mid); } catch { continue; }
                    if (vol <= 0) continue;
                    double m3 = UnitUtils.ConvertFromInternalUnits(vol, UnitTypeId.CubicMeters);

                    var mat = doc.GetElement(mid) as Material;
                    string name = mat?.Name ?? "(unnamed)";
                    var cf = CarbonFactorResolver.Resolve(doc, name);
                    var input = new MaterialCarbonInputs
                    {
                        Material = name,
                        VolumeM3 = m3,
                        DensityKgM3 = DensityFor(name),
                        WastePercent = wastePct,
                        NetFactorPerM3 = cf.PerUnit == CarbonFactorUnit.KgCo2ePerM3 ? cf.Factor : 0,
                        NetFactorPerKg = cf.PerUnit == CarbonFactorUnit.KgCo2ePerKg ? cf.Factor : 0,
                        FactorIsEpdSpecific = cf.Source == "material-param",
                        FossilFactorPerM3 = CarbonFactorResolver.GetCarbonFossilPerM3(doc, name),
                        BiogenicFactorPerM3 = CarbonFactorResolver.GetCarbonBiogenicPerM3(doc, name)
                    };
                    total += SustainMaterialCarbon.Compute(input, order).NetCarbonKg;
                }
            }
            catch (Exception ex) { StingLog.WarnRateLimited("Sustain.ElementCarbon", $"element carbon {el?.Id}: {ex.Message}"); }
            return total;
        }

        /// <summary>Material density (kg/m³) for the per-kg carbon path — corporate
        /// library first, then a small keyword fallback. Single source shared by the
        /// take-off and the per-element heat-map so both use one density.</summary>
        public static double DensityFor(string material)
        {
            try
            {
                double libVal = StingTools.UI.MaterialLookupCsv.GetDensity(material);
                if (libVal > 0) return libVal;
            }
            catch (Exception ex) { StingLog.WarnRateLimited("Sustain.Density", $"density lookup: {ex.Message}"); }

            string lc = (material ?? "").ToLowerInvariant();
            if (lc.Contains("reinforced") && lc.Contains("concrete")) return 2450;
            if (lc.Contains("concrete")) return 2400;
            if (lc.Contains("steel")) return 7850;
            if (lc.Contains("hardwood")) return 700;
            if (lc.Contains("timber") || lc.Contains("wood") || lc.Contains("softwood")) return 480;
            if (lc.Contains("alumin")) return 2700;
            if (lc.Contains("glass")) return 2500;
            if (lc.Contains("brick")) return 1920;
            if (lc.Contains("insulation")) return 40;
            return 0;
        }
    }
}
