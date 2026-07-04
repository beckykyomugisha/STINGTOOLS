// ══════════════════════════════════════════════════════════════════════════
//  SlabConcreteCalculator.cs — MAT-4 parameter-driven slab net-concrete calc.
//
//  Replaces the flat solid-fraction (MAT-1) as the DEFAULT method for void slabs.
//  From the system's rib/pot dimensions it computes, per m² of slab:
//    • net IN-SITU concrete (topping + in-situ rib concrete)
//    • topping vs in-situ-rib split
//    • PRECAST rib concrete + rib length (maxspan / beam-block — excluded from
//      in-situ concrete: leaving precast ribs in the in-situ line over-states it
//      ~3-4× and mis-bills precast as in-situ)
//    • infill block / clay-pot count
//    • the solid fraction (net in-situ ÷ gross depth) for reporting + as the
//      bridge to the flat-factor fallback.
//
//  Net concrete per m² by system (thicknesses in m):
//    Ribbed (one-way): topping + (ribW / ribSpacing) · ribD
//    Waffle (two-way): topping + 2·(ribW/s)·ribD − (ribW²/s²)·ribD  (rib crossing)
//    Hollow-pot:       topping + (ribW / (potW + ribW)) · ribD  → SF ~0.55-0.70
//    Maxspan/BeamBlock: topping ONLY (ribs precast) + precast rib volume reported
//
//  Document-free — no Autodesk.Revit.* — so the accuracy invariants are tested.
// ══════════════════════════════════════════════════════════════════════════
using System;

namespace StingTools.Core.Materials
{
    /// <summary>Resolved slab dimensions (mm) + system flags for the calculator.</summary>
    public struct SlabCalcInput
    {
        public double ToppingMm;
        public double RibWidthMm;
        public double RibSpacingMm;   // rib centre-to-centre (for pots = potW + ribW)
        public double RibDepthMm;     // rib depth BELOW the topping
        public double PotWidthMm;     // infill block/pot width (0 = no discrete pots)
        public double PotLengthMm;    // infill block/pot length (0 = continuous former)
        public bool TwoWay;
        public bool RibsArePrecast;
    }

    /// <summary>Per-m² take-off for a void slab system.</summary>
    public struct SlabCalcResult
    {
        public double InsituConcreteM3PerM2;   // topping + in-situ rib concrete
        public double ToppingM3PerM2;
        public double InsituRibM3PerM2;        // 0 when ribs are precast
        public double PrecastRibConcreteM3PerM2;
        public double PrecastRibLengthMPerM2;  // m of precast rib per m²
        public double RibLengthMPerM2;         // m of rib per m² (precast or in-situ) = 1/pitch
        public double RibDepthM;               // rib depth below topping
        public double InfillBlockCountPerM2;   // pots/blocks nr per m²
        public double GrossDepthM;             // topping + ribDepth (modelled thickness)
        public double SolidFraction;           // InsituConcreteM3PerM2 ÷ GrossDepthM
        public bool Valid;                     // false → inputs insufficient
    }

    public static class SlabConcreteCalculator
    {
        /// <summary>Returns false when the dimensions are insufficient to compute
        /// (caller then falls back to the flat solid-fraction).</summary>
        public static SlabCalcResult Compute(SlabCalcInput i)
        {
            var r = new SlabCalcResult();
            double topping = i.ToppingMm / 1000.0;
            double ribW = i.RibWidthMm / 1000.0;
            double ribD = i.RibDepthMm / 1000.0;
            double spacing = i.RibSpacingMm / 1000.0;
            double potW = i.PotWidthMm / 1000.0;
            double potL = i.PotLengthMm / 1000.0;

            // Need at least a topping + rib depth + a spacing (or pot width) to work.
            if (topping <= 0 && ribD <= 0) return r;
            double grossDepth = topping + ribD;
            if (grossDepth <= 0) return r;
            r.GrossDepthM = grossDepth;
            r.ToppingM3PerM2 = topping;
            r.RibDepthM = ribD;

            // Effective rib spacing: pot systems pitch at potW + ribW; else the
            // stated rib spacing. Guard against zero.
            double pitch = spacing > 0 ? spacing : (potW + ribW);
            if (pitch <= 0) return r;
            double ribFraction = ribW / pitch;   // plan fraction occupied by ribs
            r.RibLengthMPerM2 = 1.0 / pitch;     // m of rib per m² (precast or in-situ)

            double ribConcretePerM2;
            if (i.TwoWay)
            {
                // Two ortho rib sets minus the double-counted intersection.
                ribConcretePerM2 = (2.0 * ribFraction - ribFraction * ribFraction) * ribD;
            }
            else
            {
                ribConcretePerM2 = ribFraction * ribD;
            }

            if (i.RibsArePrecast)
            {
                // In-situ = topping ONLY. The ribs are supplied precast.
                r.InsituRibM3PerM2 = 0;
                r.InsituConcreteM3PerM2 = topping;
                r.PrecastRibConcreteM3PerM2 = ribConcretePerM2;
                r.PrecastRibLengthMPerM2 = pitch > 0 ? 1.0 / pitch : 0; // m of rib per m²
            }
            else
            {
                r.InsituRibM3PerM2 = ribConcretePerM2;
                r.InsituConcreteM3PerM2 = topping + ribConcretePerM2;
            }

            // Infill blocks / clay pots per m² (only when discrete pots are defined).
            if (potW > 0 && potL > 0)
                r.InfillBlockCountPerM2 = 1.0 / ((potW + ribW) * potL);

            r.SolidFraction = grossDepth > 0 ? r.InsituConcreteM3PerM2 / grossDepth : 1.0;
            r.Valid = r.InsituConcreteM3PerM2 > 0;
            return r;
        }
    }
}
