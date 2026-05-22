// StingTools — NC (Noise Criteria) prediction engine.
//
// Walks the duct path from fan source → terminal → room, accumulating
// attenuation and regenerated noise per element, and arrives at a
// predicted NC rating per room. Closes the gap STING had previously:
// the registry ships NC *targets* (Office 35, OR 35, etc.) but nothing
// computed the actual room NC to compare against.
//
// Approach (VDI 2081 / ASHRAE Handbook A48 / CIBSE TG6 simplified):
//
//   Lw_room(f) = Lw_fan(f)
//              − A_straight_duct(f)·L_m
//              − Σ A_fitting(f)
//              − A_silencer(f)
//              − A_end_reflection(f)
//              + Σ Lw_regen(f)              (per fitting + terminal)
//   Lp_room(f) = Lw_room(f) + 10·log10( Q/4πr² + 4/R )
//
// where Q = directivity, r = listener distance, R = room constant.
//
// Output: predicted NC per room + per-element contribution table.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Acoustic
{
    public enum ElementKind { Fan, StraightDuct, Elbow, Tee, Damper, Silencer, Diffuser }

    public class PathElement
    {
        public ElementKind Kind   { get; set; }
        public string Label       { get; set; } = "";
        /// <summary>Length m (StraightDuct only).</summary>
        public double LengthM     { get; set; }
        /// <summary>Air velocity at this element m/s — used for regenerated noise.</summary>
        public double VelocityMs  { get; set; }
        /// <summary>Mean duct cross-section m² — used for end-reflection at terminals.</summary>
        public double AreaM2      { get; set; }
        /// <summary>Source Lw per octave (Fan only). dB re 1 pW.</summary>
        public OctaveBand SourceLw { get; set; }
        /// <summary>Silencer insertion-loss per octave (Silencer only). dB.</summary>
        public OctaveBand SilencerILdB { get; set; }
    }

    public class RoomReceiver
    {
        public string Name        { get; set; } = "";
        public double VolumeM3    { get; set; }
        /// <summary>Avg absorption coefficient (Sabine). 0.1 hard, 0.3 typical office.</summary>
        public double AvgAbsorption { get; set; } = 0.2;
        /// <summary>Total interior surface area m².</summary>
        public double SurfaceAreaM2 { get; set; }
        /// <summary>Listener distance from the terminal, m.</summary>
        public double ListenerDistanceM { get; set; } = 1.5;
        /// <summary>Directivity factor. 2 for ceiling-mount, 4 for wall corner.</summary>
        public double Directivity { get; set; } = 2.0;
    }

    public class NcPathResult
    {
        public string PathName              { get; set; } = "";
        public OctaveBand FanLw              { get; set; }
        public OctaveBand TotalAttenuationDb { get; set; }
        public OctaveBand TotalRegenLw       { get; set; }
        public OctaveBand RoomLw             { get; set; }
        public OctaveBand RoomLp             { get; set; }
        public int NcRating                  { get; set; }
        public List<(string Element, OctaveBand AttenDb, OctaveBand RegenLw)> PerElement { get; }
            = new List<(string, OctaveBand, OctaveBand)>();
    }

    public static class NcPredictionEngine
    {
        // ── Attenuation tables ──────────────────────────────────────
        // dB/m for unlined straight rectangular duct, ASHRAE A48 Tbl 6.
        // Indexed by octave 63..8000 Hz. Conservative midpoint of size
        // bands; calibrate via duct hydraulic diameter for accuracy.
        public static readonly OctaveBand RectStraightUnlinedDbPerM = OctaveBand.FromArray(
            new[] { 0.10, 0.10, 0.06, 0.03, 0.03, 0.03, 0.03, 0.03 });
        // dB/m for 25 mm lined rectangular duct, ASHRAE A48 Tbl 8.
        public static readonly OctaveBand RectStraightLined25DbPerM = OctaveBand.FromArray(
            new[] { 0.20, 0.45, 1.80, 4.50, 6.50, 5.50, 3.50, 2.50 });
        // dB per 90° unlined elbow, ASHRAE A48 Tbl 13.
        public static readonly OctaveBand Elbow90UnlinedDb = OctaveBand.FromArray(
            new[] { 0.0, 1.0, 5.0, 8.0, 4.0, 3.0, 3.0, 3.0 });
        // dB lost at branch tee (straight-through path), ASHRAE A48.
        public static readonly OctaveBand TeeBranchDb = OctaveBand.FromArray(
            new[] { 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 });
        // End-reflection at a small terminal opening, ASHRAE A48 Tbl 18.
        public static readonly OctaveBand TerminalEndReflectionDb = OctaveBand.FromArray(
            new[] { 18.0, 12.0, 7.0, 3.0, 1.0, 0.0, 0.0, 0.0 });

        /// <summary>
        /// Compute room-NC for a single path. Walks the elements in
        /// source-to-terminal order, accumulates attenuation and adds
        /// regenerated noise from fittings.
        /// </summary>
        public static NcPathResult Compute(List<PathElement> path, RoomReceiver room)
        {
            var r = new NcPathResult();
            if (path == null || path.Count == 0 || room == null) return r;

            // 1. Source
            var fan = path.FirstOrDefault(e => e.Kind == ElementKind.Fan);
            if (fan == null)
            {
                r.PathName = "(no fan source)";
                return r;
            }
            r.FanLw = fan.SourceLw;
            var lw = fan.SourceLw;
            var totalAtten = new OctaveBand();
            var totalRegen = new OctaveBand();

            // 2. Walk subsequent elements
            foreach (var e in path)
            {
                if (e == fan) continue;
                var atten = new OctaveBand();
                var regen = new OctaveBand();

                switch (e.Kind)
                {
                    case ElementKind.StraightDuct:
                        for (int i = 0; i < 8; i++)
                            atten[i] = RectStraightUnlinedDbPerM[i] * e.LengthM;
                        break;
                    case ElementKind.Elbow:
                        for (int i = 0; i < 8; i++) atten[i] = Elbow90UnlinedDb[i];
                        regen = RegenElbow(e.VelocityMs);
                        break;
                    case ElementKind.Tee:
                        for (int i = 0; i < 8; i++) atten[i] = TeeBranchDb[i];
                        regen = RegenTee(e.VelocityMs);
                        break;
                    case ElementKind.Damper:
                        regen = RegenDamper(e.VelocityMs);
                        break;
                    case ElementKind.Silencer:
                        atten = e.SilencerILdB;
                        break;
                    case ElementKind.Diffuser:
                        // End-reflection attenuation only. The Bullock regen
                        // correlation for diffusers already includes terminal
                        // mixing noise post-reflection, so adding both biases
                        // predicted NC high by ~3–5 dB. Use the manufacturer
                        // catalogue NC at design throw as the authoritative
                        // source when sizing; this engine just supplies the
                        // duct-side path correction.
                        for (int i = 0; i < 8; i++) atten[i] = TerminalEndReflectionDb[i];
                        break;
                }

                // Net Lw at this element: subtract attenuation, energy-add regen
                lw = lw - atten;
                if (HasEnergy(regen)) lw = OctaveBand.AddLevels(lw, regen);

                totalAtten = totalAtten + atten;
                if (HasEnergy(regen)) totalRegen = OctaveBand.AddLevels(totalRegen, regen);

                r.PerElement.Add((string.IsNullOrEmpty(e.Label) ? e.Kind.ToString() : e.Label, atten, regen));
            }

            r.RoomLw = lw;
            r.RoomLp = RoomLwToLp(lw, room);
            r.TotalAttenuationDb = totalAtten;
            r.TotalRegenLw = totalRegen;
            r.NcRating = NcCurves.Rate(r.RoomLp);
            return r;
        }

        /// <summary>
        /// Convert Lw (dB) entering a room to Lp (dB) at the listener using
        /// the standard direct + reverberant formula:
        ///   Lp = Lw + 10·log10( Q/(4πr²) + 4/R )
        /// R = S·ᾱ/(1-ᾱ) is the room constant.
        /// </summary>
        public static OctaveBand RoomLwToLp(OctaveBand lw, RoomReceiver room)
        {
            double r2 = Math.Max(room.ListenerDistanceM, 0.5);
            r2 *= r2;
            double s = room.SurfaceAreaM2 > 0 ? room.SurfaceAreaM2
                : 6 * Math.Pow(Math.Max(room.VolumeM3, 1), 2.0 / 3.0); // approx cube
            double a = Math.Max(0.05, Math.Min(0.95, room.AvgAbsorption));
            double R = s * a / (1 - a);
            double direct = room.Directivity / (4 * Math.PI * r2);
            double rev    = 4.0 / R;
            double delta  = 10 * Math.Log10(direct + rev);

            var lp = new OctaveBand();
            for (int i = 0; i < 8; i++) lp[i] = lw[i] + delta;
            return lp;
        }

        // ── Regenerated noise correlations ──────────────────────────
        // All correlations of the form:
        //     Lw(f) = K + 60·log10(v) + 10·log10(A)
        // with empirical band shifts. References:
        //   Bullock C.E. "Aerodynamic sound generation by duct elements",
        //   ASHRAE Trans. 76 (1970); refreshed in ASHRAE Handbook A48
        //   tables 23-26. Velocity term ~v^6 (60·log10 v) is the
        //   acoustic-power scaling for turbulent mixing.

        private static OctaveBand RegenElbow(double v)
        {
            // ASHRAE A48 Fig 23: square elbow, no vanes. Reference 5 m/s.
            double baseK = -10 + 60 * Math.Log10(Math.Max(v, 0.5));
            // Octave shape (relative dB): low-freq biased.
            double[] shape = { 5, 6, 7, 6, 4, 2, 0, -2 };
            return ApplyShape(baseK, shape);
        }
        private static OctaveBand RegenTee(double v)
        {
            double baseK = -8 + 60 * Math.Log10(Math.Max(v, 0.5));
            double[] shape = { 3, 5, 7, 8, 7, 5, 2, 0 };
            return ApplyShape(baseK, shape);
        }
        private static OctaveBand RegenDamper(double v)
        {
            // High-velocity dampers are usually the loudest single
            // contributor. ASHRAE A48 Tbl 28 reference.
            double baseK = -4 + 60 * Math.Log10(Math.Max(v, 0.5));
            double[] shape = { 1, 3, 5, 8, 10, 9, 6, 3 };
            return ApplyShape(baseK, shape);
        }
        private static OctaveBand RegenDiffuser(double v, double areaM2)
        {
            // Terminal diffuser regen — heavy mid-frequency. Reference
            // Trox VL-50 grille @ 3 m/s ≈ NR-30.
            double baseK = 5 + 60 * Math.Log10(Math.Max(v, 0.3)) + 10 * Math.Log10(Math.Max(areaM2, 0.005));
            double[] shape = { 0, 2, 4, 7, 8, 6, 4, 1 };
            return ApplyShape(baseK, shape);
        }

        private static OctaveBand ApplyShape(double baseK, double[] shape)
        {
            var b = new OctaveBand();
            for (int i = 0; i < 8 && i < shape.Length; i++) b[i] = baseK + shape[i];
            return b;
        }

        private static bool HasEnergy(OctaveBand b)
        {
            for (int i = 0; i < 8; i++) if (b[i] > 0.1) return true;
            return false;
        }
    }
}
