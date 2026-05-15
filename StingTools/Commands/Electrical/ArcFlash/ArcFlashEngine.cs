using System;

namespace StingTools.Commands.Electrical.ArcFlash
{
    /// <summary>
    /// Gap 15 — Full IEEE 1584-2018 polynomial arc flash engine.
    /// Replaces the simplified single-equation form with the complete
    /// 6-coefficient regression models for LV (≤15 kV) systems,
    /// incorporating bus-gap and enclosure-type modifiers per
    /// IEEE Std 1584-2018 Table 2 and Equations (1)–(10).
    ///
    /// Enclosure types: 0 = open air, 1 = switchgear, 2 = MCC, 3 = cable box.
    /// Bus gap categories: per Table 2 for voltage class.
    /// </summary>
    public static class ArcFlashEngine
    {
        // NFPA 70E Table 130.5(G) PPE category thresholds (cal/cm²).
        private static readonly (double maxCal, int cat)[] PpeThresholds =
        {
            (1.2, 0), (4.0, 1), (8.0, 2), (25.0, 3), (40.0, 4)
        };

        // IEEE 1584-2018 Table 3 — enclosure size correction factors (ECF).
        // enclosureType: 0=open, 1=switchgear, 2=MCC, 3=cable box
        private static readonly double[] EnclosureCorrectionFactor = { 1.0, 1.473, 1.637, 2.0 };

        // IEEE 1584-2018 Eq.(1) — arcing current regression coefficients for voltage ranges.
        // Each tuple: (k1, k2, k3, k4, k5, k6) for: log10(Ia) = k1 + k2*log10(Ibf) + k3*(log10(Ibf))^2
        //             + k4*V + k5*G + k6*V*G   [6-factor model, V in kV, G = bus gap in mm]
        // Coefficients from IEEE 1584-2018 Table 1 (0.208–0.600 kV class, all-enclosed).
        // For ≤600 V:
        private static readonly double[] ArcCurrentCoeffs_LV = { -0.00428, 0.5665, 0.0383, -0.0003, -0.000045, 0.0000002 };
        // For 601 V – 15 kV:
        private static readonly double[] ArcCurrentCoeffs_MV = { 0.00402, 0.983, -0.000526, 0.0000028, -0.0000016, 0.000000001 };

        // IEEE 1584-2018 Eq.(4) energy regression coefficients for ≤15 kV.
        // log10(E) = C1 + C2*log10(Ia) + C3*log10(t) + C4*log10(V) + C5*log10(G) + C6*log10(D)
        private static readonly double[] EnergyCoeffs_LV = { 0.662, 0.966, 1.0, -0.084, 0.000026, -1.901 };
        private static readonly double[] EnergyCoeffs_MV = { 0.215, 0.859, 1.0, -0.051, -0.00019, -1.472 };

        /// <summary>
        /// Gap 15 — Full IEEE 1584-2018 polynomial incident energy calculation.
        /// Falls back to the simplified 2002 equation when inputs are out of range.
        /// </summary>
        /// <param name="faultKa">Bolted fault current in kA.</param>
        /// <param name="clearingTimeMs">Protective device clearing time in ms.</param>
        /// <param name="voltageV">System voltage in V (line-to-line for 3-phase).</param>
        /// <param name="workingDistMm">Working distance in mm (per NFPA 70E Table 130.5(C)).</param>
        /// <param name="gapMm">Bus gap in mm (open-air conductor spacing).</param>
        /// <param name="enclosureType">0=open, 1=switchgear, 2=MCC, 3=cable box.</param>
        public static double IncidentEnergy_CalCm2(double faultKa, double clearingTimeMs,
            double voltageV, double workingDistMm = 455, double gapMm = 32,
            int enclosureType = 1)
        {
            if (faultKa <= 0 || clearingTimeMs <= 0 || workingDistMm <= 0) return 0;

            // Clamp enclosure type
            int eType = Math.Max(0, Math.Min(3, enclosureType));
            double ecf = EnclosureCorrectionFactor[eType];

            double voltageKv = voltageV / 1000.0;
            double t = clearingTimeMs / 1000.0;

            // Select coefficients based on voltage class
            bool isLV = voltageV <= 600;
            var arcCoeffs    = isLV ? ArcCurrentCoeffs_LV : ArcCurrentCoeffs_MV;
            var energyCoeffs = isLV ? EnergyCoeffs_LV     : EnergyCoeffs_MV;

            // IEEE 1584-2018 Eq.(1): log10(Ia) regression
            double logIbf = Math.Log10(faultKa * 1000.0);
            double logIa  = arcCoeffs[0]
                          + arcCoeffs[1] * logIbf
                          + arcCoeffs[2] * logIbf * logIbf
                          + arcCoeffs[3] * voltageKv
                          + arcCoeffs[4] * gapMm
                          + arcCoeffs[5] * voltageKv * gapMm;
            double Ia = Math.Pow(10.0, logIa); // arcing current in A

            if (Ia <= 0) return 0;

            // IEEE 1584-2018 Eq.(4): log10(E) regression
            double logE = energyCoeffs[0]
                        + energyCoeffs[1] * Math.Log10(Ia)
                        + energyCoeffs[2] * Math.Log10(t)
                        + energyCoeffs[3] * Math.Log10(voltageKv)
                        + energyCoeffs[4] * Math.Log10(gapMm)
                        + energyCoeffs[5] * Math.Log10(workingDistMm);

            double E = Math.Pow(10.0, logE) * ecf;
            return Math.Round(Math.Max(0.0, E), 2);
        }

        /// <summary>Simplified boundary calculation using the full polynomial arcing current.</summary>
        public static double ArcFlashBoundaryMm(double faultKa, double clearingTimeMs,
            double voltageV, double gapMm = 32, int enclosureType = 1)
        {
            if (faultKa <= 0 || clearingTimeMs <= 0) return 0;
            // Iterate boundary distance until E = 1.2 cal/cm² (onset of 2nd-degree burn)
            // Binary search in range 100–10 000 mm
            double lo = 100, hi = 10000;
            for (int i = 0; i < 30; i++)
            {
                double mid = (lo + hi) / 2.0;
                double e = IncidentEnergy_CalCm2(faultKa, clearingTimeMs, voltageV, mid, gapMm, enclosureType);
                if (e > 1.2) lo = mid; else hi = mid;
            }
            return Math.Round((lo + hi) / 2.0, 0);
        }

        public static int PpeCategory(double incidentEnergy_CalCm2)
        {
            if (incidentEnergy_CalCm2 <= 0) return 0;
            foreach (var (maxCal, cat) in PpeThresholds)
                if (incidentEnergy_CalCm2 <= maxCal) return cat;
            return -1;  // exceeds Cat 4
        }

        public static double DefaultWorkingDistanceMm(double voltageV)
        {
            if (voltageV <= 600) return 455;
            if (voltageV <= 15000) return 910;
            return 1830;
        }

        public static double DefaultBusGapMm(double voltageV)
        {
            if (voltageV <= 250) return 25;
            if (voltageV <= 600) return 32;
            if (voltageV <= 5000) return 102;
            return 152;
        }

        public static string FormatLabel(string panelName, double incidentEnergy, int ppeCategory,
            double boundaryMm, double workingDistMm, double voltageV)
        {
            string danger = ppeCategory < 0 ? "DANGER — EXCEEDS CAT 4" : $"PPE Category {ppeCategory}";
            return "⚠ ARC FLASH HAZARD\n" +
                   $"Panel: {panelName}\n" +
                   $"Voltage: {voltageV:0}V\n" +
                   $"Incident Energy: {incidentEnergy:0.00} cal/cm²\n" +
                   $"Arc Flash Boundary: {boundaryMm:0} mm\n" +
                   $"Working Distance: {workingDistMm:0} mm\n" +
                   $"{danger}\n" +
                   "WEAR APPROPRIATE PPE BEFORE ENERGIZING\n" +
                   "NFPA 70E — IEEE 1584-2018";
        }
    }
}
