// LpsCarbonContributor.cs — Wave D #10.
//
// Static contributor that lets the SustainabilityEngine + any whole-
// project carbon roll-up include LPS conductor + electrode + SPD
// embodied carbon. The full per-element breakdown is in
// LpsCarbonReportCommand; this is the headline-number entry point
// callers consume to add LPS into a BREEAM Mat 02 / BS EN 15978 A1-A3
// total.

using System;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.Fabrication;

namespace StingTools.Core.Lightning
{
    public static class LpsCarbonContributor
    {
        // ICE Database v3.0 factors (kg-CO2-eq / kg)
        private const double Co2_Cu  = 3.0;
        private const double Co2_Al  = 8.24;
        private const double Co2_St  = 1.46;
        private const double Co2_Ss  = 6.15;
        // SPD body — typical thermoplastic + Cu busbar mix per DEHN EPD ≈ 4 kg
        private const double Co2_PerSpd = 4.0;
        // Earth electrode (Cu-bonded steel, ~2.4 m × 16 mm) ≈ 1.6 kg embodied
        private const double Co2_PerEarthRod = 1.6;

        // Densities (kg/m³)
        private const double Rho_Cu  = 8960;
        private const double Rho_Al  = 2700;
        private const double Rho_St  = 7850;

        /// <summary>
        /// Total LPS A1-A3 embodied carbon in kg-CO₂-eq for the project.
        /// Includes down conductors (length × CS × density × factor),
        /// earth electrodes (flat per-unit) and SPDs (flat per-unit).
        /// Designed to be summed into the SustainabilityEngine totals
        /// without depending on the full LpsCarbonReportCommand UI.
        /// </summary>
        public static double ComputeProjectKgCo2(Document doc)
        {
            if (doc == null) return 0;
            double total = 0;
            try
            {
                // Down conductors — length × cross-section × density × factor
                foreach (var dc in LpsElementIndex.DownConductors(doc))
                {
                    double L  = LpsEngine.GetConductorLengthM(dc);
                    string mat = StingTools.Core.ParameterHelpers.GetString(dc, LpsParams.CONDUCTOR_MATERIAL_TXT);
                    if (string.IsNullOrWhiteSpace(mat)) mat = "COPPER";
                    double cs = LpsEngine.GetDoubleParam(dc, LpsParams.CONDUCTOR_CROSS_SECT_MM2);
                    if (cs <= 0)
                        cs = string.Equals(mat, "ALUMINIUM", StringComparison.OrdinalIgnoreCase) ? 70 : 50;

                    double rho = string.Equals(mat, "ALUMINIUM", StringComparison.OrdinalIgnoreCase) ? Rho_Al
                               : string.Equals(mat, "STEEL",     StringComparison.OrdinalIgnoreCase) ? Rho_St
                               : Rho_Cu;
                    double co2 = string.Equals(mat, "ALUMINIUM", StringComparison.OrdinalIgnoreCase) ? Co2_Al
                               : string.Equals(mat, "STEEL",     StringComparison.OrdinalIgnoreCase) ? Co2_St
                               : string.Equals(mat, "STAINLESS", StringComparison.OrdinalIgnoreCase) ? Co2_Ss
                               : Co2_Cu;

                    double massKg = (cs * 1e-6) * L * rho;
                    total += massKg * co2;
                }

                // Earth electrodes — flat per unit
                total += LpsElementIndex.EarthElectrodes(doc).Count * Co2_PerEarthRod * Co2_St;

                // SPDs — flat per unit (body + busbar mix)
                total += LpsElementIndex.Spds(doc).Count * Co2_PerSpd;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"LpsCarbonContributor.ComputeProjectKgCo2: {ex.Message}");
            }
            return total;
        }
    }
}
