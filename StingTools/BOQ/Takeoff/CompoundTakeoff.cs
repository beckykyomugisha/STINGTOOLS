// ══════════════════════════════════════════════════════════════════════════
//  CompoundTakeoff.cs — MAT-3 type-aware compound wall/slab take-off (engine).
//
//  Walls and slabs were priced as ONE composite m² rate, type-blind: bond/block
//  size were read by nothing and the MATERIAL_LOOKUP.csv ratios were
//  reference-only. This engine turns a measured element into its CONSTITUENT
//  line items — blockwork/brickwork, plaster (× faces), mortar (+ its cement &
//  sand), and formwork for RC; concrete (net of MAT-1 voids), rebar and formwork
//  for RC slabs/beams/columns — consuming the corrected MAT-2 ratios.
//
//  Document-free (no Autodesk.Revit.*) so the constituent quantities are
//  unit-tested. The Revit-side CompoundTakeoffBuilder gathers the geometry +
//  BLE params + lookup ratios and feeds them in.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using StingTools.Core.Materials;

namespace StingTools.BOQ.Takeoff
{
    /// <summary>One measured constituent of a compound element.</summary>
    public struct CompoundLine
    {
        public string Kind;         // "blockwork" | "brickwork" | "units" | "mortar" |
                                    // "mortar_cement" | "mortar_sand" | "plaster" |
                                    // "plaster_cement" | "plaster_sand" | "concrete" |
                                    // "rebar" | "formwork"
        public string Description;
        public string Unit;         // "m2" | "m3" | "nr" | "kg" | "bag"
        public double Quantity;
        public string Nrm2Section;

        public CompoundLine(string kind, string description, string unit, double quantity, string nrm2)
        { Kind = kind; Description = description; Unit = unit; Quantity = Math.Round(quantity, 4); Nrm2Section = nrm2; }
    }

    public struct MasonryWallInput
    {
        public double FaceAreaM2;        // one elevation (net) area of the wall
        public bool IsBrick;             // brickwork vs blockwork
        public double UnitsPerM2;        // BRICKS_PER_M2 / BLOCKS_PER_M2
        public double UnitWastePct;      // cutting waste on the units
        public int PlasterFaces;         // 0 / 1 / 2 plastered faces
        public double PlasterThicknessM; // plaster coat thickness
        public double PlasterWastePct;
        public double MortarRatioM3PerM2;     // mortar volume per m² of wall
        public double MortarCementBagsPerM3;  // from MORTAR mix (MAT-2)
        public double MortarSandRatio;        // m³ sand per m³ mortar (MAT-2)
        public double PlasterCementBagsPerM3; // from PLASTER mix (MAT-2)
        public double PlasterSandRatio;
        public bool IsRcWall;            // adds formwork (both faces) when true
    }

    public struct RcElementInput
    {
        public string ElementKind;   // "slab" | "beam" | "column" | "wall"
        public double ConcreteM3Net;  // net of MAT-1 void factor
        public double RebarBandKgPerM3;
        public double FormworkM2;     // soffit + sides / both wall faces
    }

    /// <summary>MAT-4.3 — RC beam inputs (all m).</summary>
    public struct RcBeamInput
    {
        public double WidthM;             // b
        public double DepthM;             // D (overall)
        public double NetLengthM;         // clear span (column widths deducted)
        public double SlabBearingM;       // ds — slab thickness on top (no side form there)
        public double ConcreteM3Override; // > 0 → use this net volume (e.g. SolidVolume)
        public double RebarBandKgPerM3;
    }

    public static class CompoundTakeoff
    {
        // NRM2 sections (per the MAT-3 brief): concrete §13, formwork §11,
        // reinforcement §15, masonry §14, wall finishes/plaster §28.
        private const string SecConcrete = "13";
        private const string SecFormwork = "11";
        private const string SecRebar    = "15";
        private const string SecMasonry  = "14";
        private const string SecPlaster  = "28";

        /// <summary>Constituent lines for a masonry (block/brick) wall.</summary>
        public static List<CompoundLine> MasonryWall(MasonryWallInput m)
        {
            var lines = new List<CompoundLine>();
            double area = Math.Max(0, m.FaceAreaM2);
            if (area <= 0) return lines;

            string masonryKind = m.IsBrick ? "brickwork" : "blockwork";
            string masonryWord = m.IsBrick ? "Brickwork" : "Blockwork";

            // 1. The walling itself, measured m² (the QS prices £/m² by type).
            lines.Add(new CompoundLine(masonryKind, $"{masonryWord} wall", "m2", area, SecMasonry));

            // 2. Units (bricks/blocks) nr, incl. cutting waste.
            if (m.UnitsPerM2 > 0)
            {
                double units = area * m.UnitsPerM2 * (1.0 + Math.Max(0, m.UnitWastePct) / 100.0);
                lines.Add(new CompoundLine("units", m.IsBrick ? "Bricks" : "Blocks", "nr", units, SecMasonry));
            }

            // 3. Mortar m³ and its cement (bags) + sand (m³) from the MAT-2 mix.
            if (m.MortarRatioM3PerM2 > 0)
            {
                double mortarM3 = area * m.MortarRatioM3PerM2;
                lines.Add(new CompoundLine("mortar", "Bedding mortar", "m3", mortarM3, SecMasonry));
                if (m.MortarCementBagsPerM3 > 0)
                    lines.Add(new CompoundLine("mortar_cement", "Mortar — cement", "bag",
                        mortarM3 * m.MortarCementBagsPerM3, SecMasonry));
                if (m.MortarSandRatio > 0)
                    lines.Add(new CompoundLine("mortar_sand", "Mortar — sand", "m3",
                        mortarM3 * m.MortarSandRatio, SecMasonry));
            }

            // 4. Plaster m² × faces, and its cement + sand from the plaster volume.
            if (m.PlasterFaces > 0)
            {
                double plasterArea = area * m.PlasterFaces;
                lines.Add(new CompoundLine("plaster", $"Plaster ({m.PlasterFaces} face{(m.PlasterFaces > 1 ? "s" : "")})",
                    "m2", plasterArea, SecPlaster));
                double plasterVol = plasterArea * Math.Max(0, m.PlasterThicknessM)
                                    * (1.0 + Math.Max(0, m.PlasterWastePct) / 100.0);
                if (plasterVol > 0 && m.PlasterCementBagsPerM3 > 0)
                    lines.Add(new CompoundLine("plaster_cement", "Plaster — cement", "bag",
                        plasterVol * m.PlasterCementBagsPerM3, SecPlaster));
                if (plasterVol > 0 && m.PlasterSandRatio > 0)
                    lines.Add(new CompoundLine("plaster_sand", "Plaster — sand", "m3",
                        plasterVol * m.PlasterSandRatio, SecPlaster));
            }

            // 5. Formwork for an RC wall (both faces).
            if (m.IsRcWall)
                lines.Add(new CompoundLine("formwork", "Wall formwork (both faces)", "m2", area * 2.0, SecFormwork));

            return lines;
        }

        /// <summary>
        /// MAT-4.2 — constituent lines for a VOID slab, splitting precast/blocks
        /// from in-situ concrete. Emits: in-situ concrete m³ (net — topping only
        /// for precast systems), precast ribs (m) for maxspan/beam-block, infill
        /// blocks/pots (nr), topping mesh (m²), in-situ rib reinforcement (kg by
        /// rib volume, NOT a flat slab band), and formwork = rib/edge/props (never
        /// gross soffit — pots/blocks are permanent formwork).
        /// </summary>
        public static List<CompoundLine> VoidSlab(SlabCalcResult calc, double areaM2,
            string systemLabel, double ribRebarBandKgPerM3)
        {
            var lines = new List<CompoundLine>();
            if (!calc.Valid || areaM2 <= 0) return lines;
            string label = string.IsNullOrEmpty(systemLabel) ? "void slab" : systemLabel;

            // 1. In-situ concrete m³ (net). For precast systems this is topping only.
            double insitu = calc.InsituConcreteM3PerM2 * areaM2;
            if (insitu > 0)
                lines.Add(new CompoundLine("concrete", $"In-situ concrete — {label} (net)", "m3", insitu, SecConcrete));

            // 2. Precast ribs/beams (m) — supplied, EXCLUDED from in-situ concrete.
            double precastLen = calc.PrecastRibLengthMPerM2 * areaM2;
            if (precastLen > 0)
                lines.Add(new CompoundLine("precast_rib", $"Precast ribs/beams — {label}", "m", precastLen, SecConcrete));

            // 3. Infill blocks / clay pots (nr) — not structural concrete.
            double blocks = calc.InfillBlockCountPerM2 * areaM2;
            if (blocks > 0)
                lines.Add(new CompoundLine("infill_block", $"Infill blocks/pots — {label}", "nr", blocks, SecMasonry));

            // 4. Structural topping mesh (m²).
            lines.Add(new CompoundLine("mesh", "Topping mesh", "m2", areaM2, SecRebar));

            // 5. In-situ rib reinforcement (kg) — by rib CONCRETE volume × a rib
            //    band (ribs act like small beams), NOT the ~80 kg/m³ solid-slab band
            //    on the gross volume. Precast ribs carry their own (excluded) rebar.
            double ribConcrete = calc.InsituRibM3PerM2 * areaM2;
            if (ribConcrete > 0 && ribRebarBandKgPerM3 > 0)
                lines.Add(new CompoundLine("rebar", $"Rib reinforcement — {label}", "kg",
                    ribConcrete * ribRebarBandKgPerM3, SecRebar));

            // 6. Formwork = rib-side/edge forms ONLY when the ribs are cast against
            //    removable forms. Pots/blocks (InfillBlockCount > 0) are PERMANENT
            //    formwork → props only (no measured soffit form). Never gross soffit.
            bool permanentFormwork = calc.InfillBlockCountPerM2 > 0 || calc.PrecastRibLengthMPerM2 > 0;
            if (!permanentFormwork)
            {
                double ribSide = 2.0 * calc.RibDepthM * calc.RibLengthMPerM2 * areaM2; // both rib faces
                if (ribSide > 0)
                    lines.Add(new CompoundLine("formwork", $"Rib/edge formwork — {label}", "m2", ribSide, SecFormwork));
            }
            else
            {
                lines.Add(new CompoundLine("formwork", $"Formwork — {label} (props only; pots/blocks are permanent formwork)",
                    "item", 1, SecFormwork));
            }

            return lines;
        }

        /// <summary>
        /// MAT-4.3 — RC beam constituents. Concrete = section × net length (columns
        /// deducted) or a supplied SolidVolume; formwork = (b + 2·(D − ds)) × L
        /// (soffit + two sides, less the slab-bearing top); rebar by the beam band,
        /// applied ONCE (no double-count with composite column/beam rates).
        /// </summary>
        public static List<CompoundLine> RcBeam(RcBeamInput b)
        {
            var lines = new List<CompoundLine>();
            double L = Math.Max(0, b.NetLengthM);
            double concrete = b.ConcreteM3Override > 0 ? b.ConcreteM3Override : b.WidthM * b.DepthM * L;
            if (concrete <= 0) return lines;

            lines.Add(new CompoundLine("concrete", "In-situ concrete — beam", "m3", concrete, SecConcrete));

            double sides = Math.Max(0, b.DepthM - b.SlabBearingM);
            double formwork = (b.WidthM + 2.0 * sides) * L;   // soffit + two sides
            if (formwork > 0)
                lines.Add(new CompoundLine("formwork", "Formwork — beam (soffit + sides)", "m2", formwork, SecFormwork));

            if (b.RebarBandKgPerM3 > 0)
                lines.Add(new CompoundLine("rebar", "Reinforcement — beam", "kg", concrete * b.RebarBandKgPerM3, SecRebar));

            return lines;
        }

        /// <summary>Constituent lines for an RC slab / beam / column / wall.</summary>
        public static List<CompoundLine> RcElement(RcElementInput r)
        {
            var lines = new List<CompoundLine>();
            double conc = Math.Max(0, r.ConcreteM3Net);
            string kindLabel = string.IsNullOrEmpty(r.ElementKind) ? "element" : r.ElementKind;
            if (conc > 0)
            {
                lines.Add(new CompoundLine("concrete", $"In-situ concrete — {kindLabel}", "m3", conc, SecConcrete));
                if (r.RebarBandKgPerM3 > 0)
                    lines.Add(new CompoundLine("rebar", $"Reinforcement — {kindLabel}", "kg",
                        conc * r.RebarBandKgPerM3, SecRebar));
            }
            if (r.FormworkM2 > 0)
                lines.Add(new CompoundLine("formwork", $"Formwork — {kindLabel}", "m2", r.FormworkM2, SecFormwork));
            return lines;
        }
    }
}
