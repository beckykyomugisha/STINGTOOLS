using System;

namespace StingTools.Commands.Electrical.ArcFlash
{
    /// <summary>
    /// Pure-math arc flash engine — no Revit API. Implements the IEEE
    /// 1584-2018 simplified empirical formula:
    ///   E = 0.0093 × F^0.9956 × t × (610^x / D^x)
    /// where E = incident energy (cal/cm²), F = arcing current in amps,
    /// t = arc duration in seconds, D = working distance in mm, and
    /// x = 1.641 for systems ≤1 kV, 2.000 above. Production hardening
    /// should switch to the full polynomial regression with bus-gap and
    /// enclosure-type modifiers; the simplified form is documented as
    /// best-effort in the result message.
    /// </summary>
    public static class ArcFlashEngine
    {
        // NFPA 70E Table 130.5(G) PPE category thresholds (cal/cm²).
        private static readonly (double maxCal, int cat)[] PpeThresholds =
        {
            (1.2, 0), (4.0, 1), (8.0, 2), (25.0, 3), (40.0, 4)
        };

        public static double IncidentEnergy_CalCm2(double faultKa, double clearingTimeMs,
            double voltageV, double workingDistMm = 455, double gapMm = 32)
        {
            if (faultKa <= 0 || clearingTimeMs <= 0 || workingDistMm <= 0) return 0;
            double t = clearingTimeMs / 1000.0;
            double x = voltageV <= 1000 ? 1.641 : 2.000;
            double F = faultKa * 1000.0;
            double E = 0.0093 * Math.Pow(F, 0.9956) * t * (Math.Pow(610.0, x) / Math.Pow(workingDistMm, x));
            return Math.Round(Math.Max(0.0, E), 2);
        }

        public static double ArcFlashBoundaryMm(double faultKa, double clearingTimeMs,
            double voltageV, double gapMm = 32)
        {
            if (faultKa <= 0 || clearingTimeMs <= 0) return 0;
            double t = clearingTimeMs / 1000.0;
            double x = voltageV <= 1000 ? 1.641 : 2.000;
            double F = faultKa * 1000.0;
            double inner = 0.0093 * Math.Pow(F, 0.9956) * t * Math.Pow(610.0, x) / 1.2;
            if (inner <= 0) return 0;
            return Math.Round(Math.Pow(inner, 1.0 / x), 0);
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
