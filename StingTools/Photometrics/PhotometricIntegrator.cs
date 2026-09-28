using System;
using System.Collections.Generic;

namespace StingTools.Photometrics
{
    /// <summary>
    /// Zonal-lumen integration of a Type C candela grid:
    ///
    ///   Φ = Σ Ī(cell) · ΔΩ(cell),   ΔΩ = Δφ · (cos γ₁ − cos γ₂)
    ///
    /// where γ is the vertical angle from nadir (0..180°) and φ the C-plane
    /// (horizontal) angle. Ī is the mean of the cell's corner intensities.
    /// The C-planes given are treated as covering a SECTOR of the sphere; the
    /// result is scaled to the full 360° (a single plane = rotational symmetry;
    /// 0..90 = quadrant ×4; 0..180 = bilateral ×2; 0..360 = none).
    ///
    /// Revit-free and pure so the method is unit-testable: a constant I over the
    /// whole sphere must integrate to exactly 4π·I.
    /// </summary>
    public static class PhotometricIntegrator
    {
        /// <summary>
        /// Luminous flux (lm) of a Type C distribution. <paramref name="candela"/>
        /// is indexed [horizontal][vertical] in candela. Returns 0 for an empty
        /// or malformed grid (the caller must treat 0 as "not computed").
        /// </summary>
        public static double ZonalLumens(IList<double> verticalAnglesDeg,
                                         IList<double> horizontalAnglesDeg,
                                         IList<List<double>> candela)
        {
            if (verticalAnglesDeg == null || horizontalAnglesDeg == null || candela == null) return 0;
            int nV = verticalAnglesDeg.Count, nH = horizontalAnglesDeg.Count;
            if (nV < 2 || nH < 1 || candela.Count < nH) return 0;
            for (int h = 0; h < nH; h++)
                if (candela[h] == null || candela[h].Count < nV) return 0;

            // Solid-angle weight of each vertical band [γv, γv+1] per radian of azimuth.
            var band = new double[nV - 1];
            for (int v = 0; v < nV - 1; v++)
                band[v] = Math.Cos(Rad(verticalAnglesDeg[v])) - Math.Cos(Rad(verticalAnglesDeg[v + 1]));

            if (nH == 1)
            {
                // Rotationally symmetric: one plane stands for the full 2π of azimuth.
                double sum = 0;
                for (int v = 0; v < nV - 1; v++)
                    sum += 0.5 * (candela[0][v] + candela[0][v + 1]) * band[v];
                return 2 * Math.PI * sum;
            }

            double coverageDeg = horizontalAnglesDeg[nH - 1] - horizontalAnglesDeg[0];
            if (coverageDeg <= 0) return 0;

            double sector = 0;
            for (int h = 0; h < nH - 1; h++)
            {
                double dPhi = Rad(horizontalAnglesDeg[h + 1] - horizontalAnglesDeg[h]);
                if (dPhi <= 0) continue;
                for (int v = 0; v < nV - 1; v++)
                {
                    double iAvg = 0.25 * (candela[h][v] + candela[h][v + 1]
                                        + candela[h + 1][v] + candela[h + 1][v + 1]);
                    sector += iAvg * dPhi * band[v];
                }
            }
            if (coverageDeg > 180.0 + 1e-6 && coverageDeg < 360.0 - 1e-6)
            {
                // Full-circle data that omits the duplicate 360° plane (e.g. 0..345):
                // close the gap back to the first plane rather than scaling.
                double dPhi = Rad(360.0 - coverageDeg);
                for (int v = 0; v < nV - 1; v++)
                {
                    double iAvg = 0.25 * (candela[nH - 1][v] + candela[nH - 1][v + 1]
                                        + candela[0][v] + candela[0][v + 1]);
                    sector += iAvg * dPhi * band[v];
                }
                return sector;
            }
            // Scale the covered sector up to the full circle (symmetry).
            return sector * (360.0 / coverageDeg);
        }

        private static double Rad(double deg) => deg * Math.PI / 180.0;
    }
}
