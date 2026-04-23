// StingTools v4 MVP — SpoolWeightCalculator.
//
// Estimates a spool's weight from its member elements so AssemblyGrouper
// can apply the industry-standard 200-500 kg manual-handling cap and
// so AssemblyBuilder can write the computed total back to
// ASS_WEIGHT_KG.
//
// Method:
//   weight_kg = Σ (member_volume_m3 × material_density_kg_m3)
//
// Volume extraction:
//   MEPCurve: π·(d/2)²·L for round; Width·Height·L for rectangular.
//   FamilyInstance: sum the Geometry Solids.
//
// Density fallback table (kg/m³):
//   STEEL       7850
//   CAST_IRON   7200
//   COPPER      8960
//   ALUMINIUM   2700
//   UPVC        1400
//   ABS         1040
//   PEX         940
//   GI_SHEET    7850 (sheet steel for duct)
//   AL_SHEET    2700

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.Fabrication
{
    public static class SpoolWeightCalculator
    {
        private const double FtToM = 0.3048;

        private static readonly Dictionary<string, double> DensityKgM3 =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "STEEL",      7850 },
            { "CARBON",     7850 },
            { "STAINLESS",  8000 },
            { "CAST_IRON",  7200 },
            { "COPPER",     8960 },
            { "BRASS",      8550 },
            { "ALUMINIUM",  2700 },
            { "UPVC",       1400 },
            { "PVC",        1400 },
            { "ABS",        1040 },
            { "PEX",         940 },
            { "HDPE",        960 },
            { "GI_SHEET",   7850 },
            { "AL_SHEET",   2700 },
            { "INSULATION",   30 }, // nominal — ignore unless large jacket
        };

        /// <summary>
        /// Sum the kilograms for a list of elements. Returns 0 when no
        /// members have extractable volume (e.g. pure annotation
        /// families). Non-zero result is written to the assembly
        /// ASS_WEIGHT_KG parameter.
        /// </summary>
        public static double WeightKg(Document doc, IEnumerable<ElementId> memberIds)
        {
            if (doc == null || memberIds == null) return 0;
            double totalKg = 0;
            foreach (var id in memberIds)
            {
                if (id == null || id == ElementId.InvalidElementId) continue;
                var el = doc.GetElement(id);
                if (el == null) continue;
                double vm3 = VolumeM3(el);
                if (vm3 <= 0) continue;
                double rho = LookupDensity(el);
                totalKg += vm3 * rho;
            }
            return totalKg;
        }

        private static double VolumeM3(Element el)
        {
            try
            {
                // MEPCurve path — analytic volume.
                if (el is MEPCurve mep)
                {
                    var curve = (mep.Location as LocationCurve)?.Curve;
                    if (curve == null) return 0;
                    double lM = curve.Length * FtToM;
                    if (mep is Autodesk.Revit.DB.Plumbing.Pipe p)
                    {
                        double d = p.Diameter * FtToM; // outer dia
                        // Assume thin-walled: use outer diameter volume
                        // and subtract 90% internal to approximate wall
                        // volume — captures the fact that a pipe is a
                        // shell, not a solid rod.
                        double dInner = d * 0.93; // ~3% wall
                        double aOuter = Math.PI * d * d * 0.25;
                        double aInner = Math.PI * dInner * dInner * 0.25;
                        return (aOuter - aInner) * lM;
                    }
                    if (mep is Autodesk.Revit.DB.Mechanical.Duct duct)
                    {
                        // Sheet-metal duct: perimeter × wall × length.
                        double wallM = 1.2e-3; // 1.2 mm GI default
                        double wM = duct.Width  * FtToM;
                        double hM = duct.Height * FtToM;
                        if (wM > 0 && hM > 0) return 2 * (wM + hM) * wallM * lM;
                        double dM = duct.Diameter * FtToM;
                        return Math.PI * dM * wallM * lM;
                    }
                    if (mep is Autodesk.Revit.DB.Electrical.Conduit c)
                    {
                        double d = c.Diameter * FtToM;
                        double dInner = d * 0.90; // ~10% wall on conduit
                        double aOuter = Math.PI * d * d * 0.25;
                        double aInner = Math.PI * dInner * dInner * 0.25;
                        return (aOuter - aInner) * lM;
                    }
                    if (mep is Autodesk.Revit.DB.Electrical.CableTray ct)
                    {
                        double wallM = 1.5e-3;
                        double wM = ct.Width  * FtToM;
                        double hM = ct.Height * FtToM;
                        if (wM > 0 && hM > 0) return (wM + 2 * hM) * wallM * lM;
                        return 0;
                    }
                }

                // FamilyInstance fallback — sum Solid volumes.
                if (el is FamilyInstance || el is AssemblyInstance)
                {
                    double sum = 0;
                    var opts = new Options { DetailLevel = ViewDetailLevel.Medium };
                    var geom = el.get_Geometry(opts);
                    if (geom == null) return 0;
                    foreach (var g in geom)
                    {
                        if (g is Solid s)
                            sum += s.Volume;
                        else if (g is GeometryInstance gi)
                        {
                            foreach (var gg in gi.GetInstanceGeometry())
                                if (gg is Solid s2) sum += s2.Volume;
                        }
                    }
                    // Revit internal volume is ft³.
                    return sum * Math.Pow(FtToM, 3);
                }
            }
            catch (Exception ex)
            { StingLog.Warn($"SpoolWeightCalculator: volume for {el?.Id}: {ex.Message}"); }
            return 0;
        }

        private static double LookupDensity(Element el)
        {
            try
            {
                string mat = el.LookupParameter("PLM_PPE_MAT_TXT")?.AsString()
                          ?? el.LookupParameter("HVC_DCT_MAT_TXT")?.AsString()
                          ?? el.LookupParameter("ELC_CDT_MAT_TXT")?.AsString()
                          ?? "";
                mat = mat.ToUpperInvariant().Trim();
                if (!string.IsNullOrEmpty(mat) && DensityKgM3.TryGetValue(mat, out var rho))
                    return rho;

                // Category fallback:
                if (el is Autodesk.Revit.DB.Plumbing.Pipe)                 return DensityKgM3["STEEL"];
                if (el is Autodesk.Revit.DB.Mechanical.Duct)               return DensityKgM3["GI_SHEET"];
                if (el is Autodesk.Revit.DB.Electrical.Conduit)            return DensityKgM3["STEEL"];
                if (el is Autodesk.Revit.DB.Electrical.CableTray)          return DensityKgM3["GI_SHEET"];
            }
            catch { }
            return 7850; // steel default
        }
    }
}
