// StingTools — Demand-Controlled Ventilation (DCV) hourly OA calc.
//
// ASHRAE Standard 62.1-2019 §6.2.7 + Appendix A2. The design-day OA
// rate (the static HVC_OA_LS we already stamp) is the WORST hour. A
// DCV system modulates OA hour-by-hour based on the actual occupancy
// schedule via CO₂ control, dropping OA when the room is partly-empty.
//
// The math per zone:
//   R_p   = OA per person (L/s)
//   R_a   = OA per area   (L/s/m²)
//   N(t)  = occupants at hour t = z.OccupantCount × occupancy(t)
//   V_oz(t) = (R_p · N(t)  +  R_a · A)   ← zone-outdoor airflow
//
// We extend the BlockLoadEngine ZoneLoadResult with an HourlyOaLs[24]
// array so downstream commands (annual energy sims, BMS commissioning)
// can use the modulated profile rather than the design-day single
// value.
//
// Note: the R_a per-area term doesn't modulate — it's always required
// regardless of occupancy (off-gassing from materials). Only the
// per-person R_p term varies. The zone-system formula combines.

using System;

namespace StingTools.Core.Hvac.Loads
{
    public static class DcvVentilationCalc
    {
        /// <summary>
        /// Compute the hourly OA L/s profile for a zone over a 24-hour
        /// design day, using the LoadZone's OaLpsPerPerson + OaLpsPerM2
        /// constants and the same OccupancySchedule that drives the
        /// internal-gain calc.
        ///
        /// Returns null when the zone has no per-person OA, no occupants,
        /// or no schedule — caller falls back to the static OaLs.
        /// </summary>
        public static double[] HourlyOa(LoadZone z)
        {
            if (z == null) return null;
            if (z.OccupantCount <= 0 || z.OaLpsPerPerson <= 0) return null;
            if (z.OccupancySchedule == null || z.OccupancySchedule.Length == 0) return null;

            var oa = new double[24];
            double perAreaBase = z.OaLpsPerM2 * z.FloorAreaM2;
            for (int h = 0; h < 24; h++)
            {
                double occH = h < z.OccupancySchedule.Length
                    ? Math.Max(0, Math.Min(1, z.OccupancySchedule[h]))
                    : 0;
                double nPeople  = z.OccupantCount * occH;
                double perPerson = z.OaLpsPerPerson * nPeople;
                oa[h] = perPerson + perAreaBase;
            }
            return oa;
        }

        /// <summary>
        /// Aggregate OA savings from DCV over the design day vs. holding
        /// the design-day max. Returns (designLs, avgLs, savingsPct).
        /// </summary>
        public static (double designLs, double avgLs, double savingsPct) Stats(double[] hourly)
        {
            if (hourly == null || hourly.Length == 0) return (0, 0, 0);
            double max = hourly[0], sum = 0;
            for (int h = 0; h < hourly.Length; h++)
            {
                if (hourly[h] > max) max = hourly[h];
                sum += hourly[h];
            }
            double avg = sum / hourly.Length;
            double pct = max > 0 ? 100.0 * (1.0 - avg / max) : 0;
            return (max, avg, pct);
        }
    }
}
