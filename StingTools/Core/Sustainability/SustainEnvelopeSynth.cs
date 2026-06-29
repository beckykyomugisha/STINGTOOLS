// StingTools — Representative envelope synthesizer (Phase 195, gap fix #1).
//
// The sustainability zone-gather (SustainabilityEngine.ZoneFromSpace /
// ZoneFromRoom) previously built LoadZones with NO envelope segments, so the
// AnnualEnergyEstimator saw UA = 0 and under-counted conduction + solar gains
// (energy was "fabric-blind"). Measuring real per-wall boundary geometry per
// zone is expensive and not always available (linked architectural model), so
// this pure helper synthesises a REPRESENTATIVE envelope from the zone's floor
// area + height + the active construction profile's U-values.
//
// It is an ESTIMATE, not a measurement — the caller flags it as such. But a
// representative fabric is far better than none: a 2 000 m² residential block
// now carries walls + glazing + (top-level) roof conduction instead of pure
// internal-gain + ventilation load.
//
// Pure POCO / Revit-free + unit-tested. The Revit adapter passes plain numbers.

using System;
using System.Collections.Generic;
using StingTools.Core.Hvac.Loads;   // EnvelopeSegment / SegmentKind (Revit-free POCOs)

namespace StingTools.Core.Sustainability
{
    /// <summary>U-values + glazing assumptions for the synthesised envelope.
    /// Mirrors the fields the HVAC ConstructionProfile carries so the adapter
    /// can map one onto the other without a Revit dependency here.</summary>
    public class EnvelopeSynthInputs
    {
        public double WallUvalue   { get; set; } = 0.30;   // Part L 2021 wall
        public double RoofUvalue   { get; set; } = 0.20;   // Part L 2021 roof
        public double WindowUvalue { get; set; } = 1.40;
        public double WindowShgc   { get; set; } = 0.40;
        public double WindowShading{ get; set; } = 0.90;

        /// <summary>Window-to-(gross-)wall ratio. 0.30 is a reasonable mixed-use
        /// default; residential ~0.20-0.25, commercial ~0.30-0.40.</summary>
        public double Wwr { get; set; } = 0.30;

        /// <summary>Plan perimeter ≈ factor × √(floorArea). A fully-detached square
        /// is 4.0; interior zones share walls so less perimeter is exterior — 3.5
        /// is a representative average exposure for a mixed plan.</summary>
        public double PerimeterFactor { get; set; } = 3.5;
    }

    public static class SustainEnvelopeSynth
    {
        // Four cardinal orientations (deg from North) so solar gain is balanced
        // across the building rather than dumped on one face.
        private static readonly double[] CardinalDeg = { 0, 90, 180, 270 };

        /// <summary>
        /// Build a representative envelope (4 walls + 4 windows + optional roof)
        /// from floor area. Returns an empty list for a degenerate zone
        /// (area ≤ 0). The wall + window area is split evenly across the four
        /// cardinal orientations.
        /// </summary>
        public static List<EnvelopeSegment> FromFloorArea(
            double floorAreaM2, double heightM, bool isTopLevel, EnvelopeSynthInputs inp)
        {
            var segs = new List<EnvelopeSegment>();
            if (floorAreaM2 <= 0) return segs;
            inp = inp ?? new EnvelopeSynthInputs();
            if (heightM <= 0.1) heightM = 3.0;

            double perimeterFactor = inp.PerimeterFactor > 0 ? inp.PerimeterFactor : 3.5;
            double perimeterM = perimeterFactor * Math.Sqrt(floorAreaM2);
            double grossWallM2 = perimeterM * heightM;

            double wwr = Math.Min(0.9, Math.Max(0.0, inp.Wwr));
            double windowM2 = grossWallM2 * wwr;
            double netWallM2 = Math.Max(0, grossWallM2 - windowM2);

            // Split across the four cardinal faces.
            double wallPerFace = netWallM2 / 4.0;
            double windowPerFace = windowM2 / 4.0;
            foreach (var deg in CardinalDeg)
            {
                if (wallPerFace > 1e-6)
                    segs.Add(new EnvelopeSegment
                    {
                        Kind = SegmentKind.ExteriorWall, AreaM2 = wallPerFace,
                        UvalueWm2K = inp.WallUvalue, OrientationDeg = deg
                    });
                if (windowPerFace > 1e-6)
                    segs.Add(new EnvelopeSegment
                    {
                        Kind = SegmentKind.Window, AreaM2 = windowPerFace,
                        UvalueWm2K = inp.WindowUvalue, SHGC = inp.WindowShgc,
                        ShadingFactor = inp.WindowShading, OrientationDeg = deg
                    });
            }

            // Roof only on the top level — the floor-area projection.
            if (isTopLevel)
                segs.Add(new EnvelopeSegment
                {
                    Kind = SegmentKind.Roof, AreaM2 = floorAreaM2,
                    UvalueWm2K = inp.RoofUvalue, OrientationDeg = 0
                });

            return segs;
        }
    }
}
