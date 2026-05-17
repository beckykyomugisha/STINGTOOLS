// Phase 139.28 — Eurocode 2 / UK NA / BS 8500 concrete-cover lookup.
//
// Used by InWallChaseRouter when a chased pipe / conduit is to be cast
// into a concrete element. Returns the minimum nominal cover from the
// finished concrete face to the pipe's outer surface.
//
// Source: BS EN 1992-1-1:2004 §4.4.1 + UK National Annex Table NA.4.4N
// for structural class S4 (50-year design life). Values are c_nom =
// c_min,dur + Δc_dev (10 mm allowance per UK NA). For pipework cast
// into concrete the UK practice is to use the structural-rebar c_nom
// as a starting point, then add half the pipe outer diameter so the
// pipe centreline is offset accordingly.
//
// Usage:
//
//   double cover = ConcreteCoverTable.GetNominalCoverMm(
//       exposureClass: "XC2",       // typical interior chase
//       structuralClass: 4,         // 50-year life
//       fireResistance: "REI60");   // 60-min fire rating
//
// Then InWallChaseRouter offsets the route from the wall finish face by
// (cover + pipeOuterDiameter / 2). Matches Uganda construction
// practice — UNBS adopts BS EN directly and the UK NA values are the
// reference used by most consulting engineers in Kampala.

using System;
using System.Collections.Generic;

namespace StingTools.Core.Calc
{
    public static class ConcreteCoverTable
    {
        /// <summary>
        /// Default exposure class when the rule doesn't specify one —
        /// XC2 = "wet, rarely dry" — typical for interior wall / slab
        /// chases below DPC.
        /// </summary>
        public const string DefaultExposureClass = "XC2";

        /// <summary>UK NA default structural class (50-year design life).</summary>
        public const int DefaultStructuralClass = 4;

        /// <summary>
        /// Δc_dev — UK NA placing-tolerance allowance added to c_min,dur
        /// to derive c_nom. 10 mm is the BS EN 1992-1-1 §4.4.1.3 default.
        /// </summary>
        public const double DeltaDevMm = 10.0;

        // c_min,dur (mm) per UK NA Table NA.4.4N — Structural class S4.
        // Indexed by exposure class. Values are the durability-driven
        // minimum cover before the placing-tolerance allowance Δc_dev.
        private static readonly Dictionary<string, double> CMinDurS4
            = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            // Carbonation
            { "XC1", 15.0 },   // dry / permanently wet (interior)
            { "XC2", 25.0 },   // wet, rarely dry (foundations, buried)
            { "XC3", 25.0 },   // moderate humidity (sheltered external)
            { "XC4", 30.0 },   // cyclic wet/dry (exposed external)
            // Chloride from de-icing salt
            { "XD1", 35.0 },
            { "XD2", 40.0 },
            { "XD3", 45.0 },
            // Chloride from sea
            { "XS1", 35.0 },   // airborne salt (coastal)
            { "XS2", 40.0 },
            { "XS3", 45.0 },
            // Freeze / thaw
            { "XF1", 30.0 },
            { "XF2", 35.0 },
            { "XF3", 35.0 },
            { "XF4", 40.0 },
            // Chemical
            { "XA1", 30.0 },
            { "XA2", 35.0 },
            { "XA3", 40.0 },
        };

        // Adjustment ΔcS per UK NA Table NA.4.3N — structural class.
        // Class 4 = 0 mm (baseline). Classes 1..3 = -5 mm each step
        // (shorter design life). Classes 5..6 = +5 mm each step (longer
        // life e.g. tunnel, bridge).
        private static double StructuralClassAdjustmentMm(int sClass)
        {
            switch (sClass)
            {
                case 1: return -15.0;
                case 2: return -10.0;
                case 3: return -5.0;
                case 4: return 0.0;
                case 5: return 5.0;
                case 6: return 10.0;
                default: return 0.0;
            }
        }

        // Fire-resistance cover increment per BS EN 1992-1-2 Table 5.5
        // (slab simply supported, REI 30..240). Conservative — applies
        // to slab; wall is usually less but we use the slab value as a
        // safe upper bound for pipe-in-concrete.
        private static double FireResistanceAdjustmentMm(string fire)
        {
            if (string.IsNullOrEmpty(fire)) return 0.0;
            string f = fire.Trim().ToUpperInvariant().Replace(" ", "");
            // Match REIxxx / Rxxx / Exxx / xxxMIN
            int mins = ExtractMinutes(f);
            if (mins <= 0) return 0.0;
            if (mins <= 30)  return 5.0;
            if (mins <= 60)  return 10.0;
            if (mins <= 90)  return 15.0;
            if (mins <= 120) return 20.0;
            if (mins <= 180) return 25.0;
            return 30.0;
        }

        private static int ExtractMinutes(string token)
        {
            int n = 0;
            bool any = false;
            foreach (char c in token)
            {
                if (c >= '0' && c <= '9') { n = n * 10 + (c - '0'); any = true; }
                else if (any) break;
            }
            return any ? n : 0;
        }

        /// <summary>
        /// Returns the minimum nominal cover c_nom (mm) for a concrete
        /// element. For pipework cast into the element, callers should
        /// add half the pipe outer diameter to keep the rebar zone clear.
        /// Falls back to DefaultExposureClass when the supplied class is
        /// blank / unknown.
        /// </summary>
        public static double GetNominalCoverMm(
            string exposureClass = DefaultExposureClass,
            int structuralClass = DefaultStructuralClass,
            string fireResistance = "")
        {
            string key = string.IsNullOrEmpty(exposureClass) ? DefaultExposureClass : exposureClass.Trim();
            if (!CMinDurS4.TryGetValue(key, out double cMinDur))
                cMinDur = CMinDurS4[DefaultExposureClass];
            double adjusted = cMinDur + StructuralClassAdjustmentMm(structuralClass)
                            + FireResistanceAdjustmentMm(fireResistance);
            // BS EN 1992-1-1 §4.4.1.2(3) — c_min cannot fall below 10 mm.
            if (adjusted < 10.0) adjusted = 10.0;
            return adjusted + DeltaDevMm;
        }

        /// <summary>
        /// Pipe-in-concrete effective offset from the concrete finish face
        /// to the pipe centreline. Equals nominal cover + pipeOuterDiameter / 2.
        /// </summary>
        public static double GetPipeOffsetFromFaceMm(
            double pipeOuterDiameterMm,
            string exposureClass = DefaultExposureClass,
            int structuralClass = DefaultStructuralClass,
            string fireResistance = "")
        {
            double cover = GetNominalCoverMm(exposureClass, structuralClass, fireResistance);
            return cover + Math.Max(0.0, pipeOuterDiameterMm * 0.5);
        }

        /// <summary>
        /// Diagnostic — list every exposure class and its nominal cover
        /// at structural class 4 + no fire rating. Used by the Placement
        /// Centre's standards-info dialog.
        /// </summary>
        public static IReadOnlyDictionary<string, double> AllNominalCoversS4()
        {
            var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in CMinDurS4) dict[kv.Key] = kv.Value + DeltaDevMm;
            return dict;
        }
    }
}
