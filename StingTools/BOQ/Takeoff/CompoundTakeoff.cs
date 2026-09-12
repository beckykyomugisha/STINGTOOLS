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
                                    // "rebar" | "formwork" | "floor_tile" |
                                    // "wall_tile" | "tile_adhesive" | "tile_grout" |
                                    // "screed" | "screed_cement" | "screed_sand" |
                                    // "ceiling_board" | "ceiling_furring" |
                                    // "dpm" | "roof_underlay" | "fascia_board"
        public string Description;
        public string Unit;         // "m2" | "m3" | "nr" | "kg" | "bag"
        public double Quantity;
        public string Nrm2Section;

        /// <summary>
        /// The wastage this line's own VARIANT implies, or -1 for "the supplier
        /// rule's default is fine".
        ///
        /// Wastage still lives in exactly one place — the supplier rule applies
        /// it, and this only tells the rule a better number than its flat
        /// default. Applying it here as well is the double-waste bug that was
        /// removed earlier; nothing multiplies by this.
        ///
        /// MATERIAL_LOOKUP has carried per-bond cutting waste all along (stack
        /// 3%, stretcher 5, garden wall 6, English 7, Flemish 8). It was
        /// resolved per wall into UnitWastePct and read by NOTHING, so every
        /// wall got the rule's flat 5 and a Flemish bond was under-ordered by
        /// three points.
        /// </summary>
        public double WastePctOverride;

        public CompoundLine(string kind, string description, string unit, double quantity, string nrm2)
        {
            Kind = kind; Description = description; Unit = unit;
            Quantity = Math.Round(quantity, 4); Nrm2Section = nrm2;
            WastePctOverride = -1;
        }
    }

    public struct MasonryWallInput
    {
        public double FaceAreaM2;        // one elevation (net) area of the wall
        public bool IsBrick;             // brickwork vs blockwork
        public double UnitsPerM2;        // BRICKS_PER_M2 / BLOCKS_PER_M2
        /// <summary>RETIRED. Wastage belongs to the supplier-unit rule; leaving it
        /// here as well double-counted it. Kept so existing callers still compile.</summary>
        public double UnitWastePct;      // cutting waste on the units
        public int PlasterFaces;         // 0 / 1 / 2 plastered faces
        public double PlasterThicknessM; // plaster coat thickness

        /// <summary>
        /// The coat's application waste, from PLASTER {type} WASTE_PCT — thin 15%,
        /// standard and lime 20, thick 25.
        ///
        /// It was RETIRED because applying it in the take-off AND letting the
        /// supplier rule apply its own wasted the cement twice. It is live again
        /// on the other side of that fix: nothing here multiplies by it, it is
        /// handed to the supplier rule as the allowance to use INSTEAD of the
        /// rule's default.
        ///
        /// That substitution is the decision. The cement rule's 2.5% is bag
        /// handling and spillage; plaster's 15-25% is material lost in mixing
        /// and application, and it is the larger, governing allowance for
        /// plaster-derived cement and sand rather than an addition to it.
        /// </summary>
        public double PlasterWastePct;
        public double MortarRatioM3PerM2;     // mortar volume per m² of wall
        public double MortarCementBagsPerM3;  // from MORTAR mix (MAT-2)
        public double MortarSandRatio;        // m³ sand per m³ mortar (MAT-2)
        public double PlasterCementBagsPerM3; // from PLASTER mix (MAT-2)
        public double PlasterSandRatio;
        public bool IsRcWall;            // adds formwork (both faces) when true
        /// <summary>Exterior walls paint as weather-guard, interior as silk —
        /// different products at different prices, so they are separate
        /// commodities. Read from WallType.Function by the Revit-side builder.</summary>
        public bool IsExteriorWall;

        /// <summary>
        /// Faces carrying a TILED finish (0-2), read from the wall type's
        /// compound structure. Those faces are plastered as a backing but NOT
        /// painted — you do not paint a tiled wall — so they are deducted from
        /// the painted area. Without this the same face is finished twice.
        /// </summary>
        public int TiledFaces;
    }

    /// <summary>
    /// MAT-SCHED — tiling, measured from the host's own finish LAYER.
    ///
    /// A floor's tiled area is not its slab area and cannot be inferred from
    /// one: the RC-slab path knows only concrete, rebar and formwork, and has
    /// no way to say whether a floor is tiled at all. An earlier attempt to
    /// convert whole ELEMENTS to tiles from a unit table was withdrawn for
    /// exactly that reason — it priced entire floors as tiling.
    ///
    /// The compound structure answers it directly: a Finish1/Finish2 layer whose
    /// material reads as tile IS the tiled area, and its absence means the
    /// surface is not tiled.
    /// </summary>
    public struct TiledFinishInput
    {
        public double AreaM2;            // net tiled area — one face, not the element
        public bool IsWall;              // wall tiling and floor tiling are different products
        public string TileLabel;         // material name, for the description
        public double AdhesiveKgPerM2;   // manufacturer spreading rate
        public double GroutKgPerM2;      // joint width / tile size dependent

        /// <summary>
        /// Cutting waste for this tile's SIZE band, or -1 when unstated.
        ///
        /// MATERIAL_LOOKUP has banded it 20 / 12 / 10 / 8 for mosaic / small /
        /// medium / large since before this schedule existed; the supplier rule
        /// applied a flat 10% to all of them, so a mosaic splashback was
        /// under-ordered by half its cutting allowance.
        /// </summary>
        public double WastePctOverride;
    }

    /// <summary>
    /// MATSCHED-T1 — screed, measured from the host's own SUBSTRATE (or finish)
    /// layer, exactly as tiling is measured from its finish layer.
    ///
    /// A 40 mm cement screed under a tiled floor is a real purchase whose
    /// thickness the model already STATES. It contributed nothing before,
    /// because the layer walk inspected finish layers only and only for tile
    /// materials, so every screeded floor in every project was missing its
    /// cement and sand.
    ///
    /// The thickness is the driver and it is never assumed: a screed layer that
    /// declares zero width yields nothing, because a screed with no volume has
    /// no cement in it and a default thickness would be an invention.
    /// </summary>
    public struct ScreedInput
    {
        public double AreaM2;            // the screeded area — the host's own area
        public double ThicknessM;        // from CompoundStructureLayer.Width, feet → m
        public string ScreedLabel;       // material name, for the description
        public double CementBagsPerM3;   // from the SCREED mix (MATERIAL_LOOKUP)
        public double SandRatio;         // m³ sand per m³ of screed
    }

    /// <summary>
    /// MATSCHED-T2 — a ceiling, read from its own compound structure.
    ///
    /// `CeilingType` appeared NOWHERE in the take-off, so a suspended gypsum
    /// ceiling yielded zero materials: no boards, no furring, no skim. It
    /// yielded them silently, which looks exactly like a model with no ceilings.
    ///
    /// Board and plaster are two independent findings, not one classification.
    /// A plasterboard ceiling skimmed after fixing genuinely carries both, and
    /// the model states both; collapsing them into a single "ceiling finish"
    /// would drop whichever lost.
    /// </summary>
    public struct CeilingInput
    {
        public double AreaM2;

        /// <summary>Board material name, or "" when no board layer. Non-empty is
        /// the ONLY condition under which furring is emitted — boards need a
        /// frame to fix to, a skim on a concrete soffit does not.</summary>
        public string BoardLabel;

        /// <summary>RATIO-DERIVED, not measured: the model does not state grid
        /// spacing. 0 emits no furring row at all.</summary>
        public double FurringMPerM2;

        /// <summary>Plaster/skim material name, or "" when no such layer.</summary>
        public string PlasterLabel;
        /// <summary>From the layer's own declared width. 0 emits the plastered
        /// AREA but no cement and no sand — the volume driver is missing, and a
        /// default coat thickness would be an invention.</summary>
        public double PlasterThicknessM;
        public double PlasterCementBagsPerM3;
        public double PlasterSandRatio;
    }

    /// <summary>
    /// MATSCHED-T3 — sheet membranes, read from the host's own Membrane layers.
    ///
    /// `MaterialFunctionAssignment.Membrane` was ignored entirely, so a
    /// ground-bearing slab's DPM and a roof's underlay — both real purchased
    /// materials whose area the model already STATES — produced nothing.
    ///
    /// Layers are COUNTED, not merged: two membrane layers on one build-up are
    /// two purchases of that area, exactly as two tiled faces are two areas of
    /// tiling. Insulation never reaches here; it is bought by thickness and is
    /// reported by the scan instead of being absorbed.
    /// </summary>
    public struct MembraneInput
    {
        public double AreaM2;

        /// <summary>Number of layers classified as damp-proof membrane.</summary>
        public int DpmLayers;
        public string DpmLabel;

        /// <summary>Number of layers classified as roof underlay / sarking.</summary>
        public int UnderlayLayers;
        public string UnderlayLabel;
    }

    /// <summary>
    /// MATSCHED-T5 — roof edge accessories, measured from the roof's own
    /// footprint boundary.
    ///
    /// Only ONE of the three accessories a roof edge carries has a length the
    /// footprint states outright. A FASCIA runs along the eaves, an eave is
    /// horizontal, so the boundary's plan length IS its length. A BARGE BOARD
    /// runs up the rake of a gable, which is longer than the gable's plan
    /// length by 1/cos(pitch). A RIDGE is an internal line the footprint does
    /// not carry at all.
    ///
    /// The other two are therefore NOT emitted, and the scan says so by name.
    /// Deriving a length from the roof AREA would be exactly the guess this
    /// feature exists to refuse.
    /// </summary>
    public struct RoofEdgeInput
    {
        /// <summary>Total length of the SLOPE-DEFINING footprint edges — the
        /// eaves. 0 emits nothing.</summary>
        public double EavesLengthM;
        public string RoofLabel;
    }

    public struct RcElementInput
    {
        public string ElementKind;   // "slab" | "beam" | "column" | "wall"
        public double ConcreteM3Net;  // net of MAT-1 void factor
        public double RebarBandKgPerM3;
        public double FormworkM2;     // soffit + sides / both wall faces
    }

    /// <summary>
    /// MAT-SCHED — RC column inputs (all m). A column shutters on ALL FOUR faces
    /// and has no soffit, so its formwork is perimeter × height.
    /// </summary>
    public struct RcColumnInput
    {
        public double WidthM;             // rectangular b
        public double DepthM;             // rectangular d
        public double DiameterM;          // > 0 → round column; overrides W×D
        public double HeightM;
        public double ConcreteM3Override; // > 0 → Revit's own solid volume wins
        public double RebarBandKgPerM3;
    }

    /// <summary>
    /// MAT-SCHED — RC foundation inputs (all m). A pad or strip bears on the
    /// ground, so there is no soffit shutter; only the SIDES are formed, and only
    /// when it is not cast against a neat excavation.
    /// </summary>
    public struct RcFoundationInput
    {
        public double LengthM;
        public double WidthM;
        public double DepthM;
        public double ConcreteM3Override;
        public double RebarBandKgPerM3;
        /// <summary>Blinding is unreinforced by definition.</summary>
        public bool IsBlinding;
        /// <summary>False when cast against the excavation face.</summary>
        public bool FormworkToSides;
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

            // 2. Units (bricks/blocks) nr — NET of waste.
            //
            // Wastage lives in ONE place: the supplier-unit rule. It used to be
            // applied here as well, so blocks and bricks carried cutting waste
            // twice (engine ~5% then the rule's 5%, ≈10% effective) and nobody
            // could see which allowance was which.
            //
            // Brick and block are DISTINCT kinds. A single "units" kind sent a
            // brick wall's brick count into the block commodity, so bricks were
            // ordered as blocks.
            if (m.UnitsPerM2 > 0)
            {
                double units = area * m.UnitsPerM2;
                lines.Add(new CompoundLine(m.IsBrick ? "brick_units" : "block_units",
                    m.IsBrick ? "Bricks" : "Blocks", "nr", units, SecMasonry)
                {
                    // The BOND's cutting waste, handed to the supplier rule
                    // rather than applied here.
                    WastePctOverride = m.UnitWastePct > 0 ? m.UnitWastePct : -1
                });
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
                // NET of waste — the supplier-unit rule owns the allowance. This
                // used to multiply by PlasterWastePct (20%), so the cement and
                // sand derived from it were wasted twice.
                double plasterVol = plasterArea * Math.Max(0, m.PlasterThicknessM);
                // The coat's application allowance rides on the DERIVED lines
                // only. The plaster m2 above is measured work a QS prices by
                // area — it is not bought, so it takes no material allowance.
                double coatWaste = m.PlasterWastePct > 0 ? m.PlasterWastePct : -1;
                if (plasterVol > 0 && m.PlasterCementBagsPerM3 > 0)
                    lines.Add(new CompoundLine("plaster_cement", "Plaster — cement", "bag",
                        plasterVol * m.PlasterCementBagsPerM3, SecPlaster)
                    { WastePctOverride = coatWaste });
                if (plasterVol > 0 && m.PlasterSandRatio > 0)
                    lines.Add(new CompoundLine("plaster_sand", "Plaster — sand", "m3",
                        plasterVol * m.PlasterSandRatio, SecPlaster)
                    { WastePctOverride = coatWaste });

                // Painted area IS the plastered face area — no new measurement,
                // just the quantity already derived above given its own kind so
                // paint routes and converts like any other constituent. Wastage
                // stays with the supplier-unit rule (the spreading rate absorbs
                // over-application), exactly as it does for plaster.
                // An unplastered wall is not painted: this whole branch is
                // guarded by PlasterFaces > 0.
                // A tiled face is plastered as backing but never painted, so it
                // comes off the painted area. Clamped at 0: a wall tiled on more
                // faces than it is plastered on must not emit NEGATIVE paint.
                int paintedFaces = Math.Max(0, m.PlasterFaces - Math.Max(0, m.TiledFaces));
                if (paintedFaces > 0)
                    lines.Add(new CompoundLine(
                        m.IsExteriorWall ? "paint_exterior" : "paint_interior",
                        m.IsExteriorWall ? "Paint — exterior (weather-guard)" : "Paint — interior",
                        "m2", area * paintedFaces, SecPlaster));
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
        /// <summary>
        /// Column constituents. Formwork is the perimeter × height — all four
        /// faces, no soffit. A round column uses its circumference: treating
        /// Ø152 as a 152 square would over-order shuttering by about 27%.
        /// </summary>
        public static List<CompoundLine> RcColumn(RcColumnInput c)
        {
            double h = Math.Max(0, c.HeightM);
            bool round = c.DiameterM > 0;

            double derivedM3 = round
                ? Math.PI * c.DiameterM * c.DiameterM / 4.0 * h
                : Math.Max(0, c.WidthM) * Math.Max(0, c.DepthM) * h;
            double conc = c.ConcreteM3Override > 0 ? c.ConcreteM3Override : derivedM3;

            double perimeter = round
                ? Math.PI * c.DiameterM
                : 2.0 * (Math.Max(0, c.WidthM) + Math.Max(0, c.DepthM));
            double formwork = perimeter * h;

            return RcElement(new RcElementInput
            {
                ElementKind = "column",
                ConcreteM3Net = conc,
                RebarBandKgPerM3 = c.RebarBandKgPerM3,
                FormworkM2 = formwork
            });
        }

        /// <summary>
        /// Foundation constituents. Only the SIDES are shuttered — a pad bears on
        /// the ground, so there is no soffit — and not even those when it is cast
        /// against the excavation. Blinding carries no reinforcement.
        /// </summary>
        public static List<CompoundLine> RcFoundation(RcFoundationInput f)
        {
            double l = Math.Max(0, f.LengthM), w = Math.Max(0, f.WidthM), d = Math.Max(0, f.DepthM);
            double conc = f.ConcreteM3Override > 0 ? f.ConcreteM3Override : l * w * d;
            double formwork = f.FormworkToSides ? 2.0 * (l + w) * d : 0;

            return RcElement(new RcElementInput
            {
                ElementKind = f.IsBlinding ? "blinding" : "foundation",
                ConcreteM3Net = conc,
                RebarBandKgPerM3 = f.IsBlinding ? 0 : f.RebarBandKgPerM3,
                FormworkM2 = formwork
            });
        }

        /// <summary>
        /// MAT-SCHED — tiling constituents for ONE tiled face or floor.
        ///
        /// Quantities are NET of wastage; the supplier-unit rule owns the
        /// allowance, as it does for every other constituent. Tiles carry the
        /// largest allowance of anything in the schedule (cuts at every edge and
        /// every penetration), which is precisely why it belongs in one visible,
        /// arguable place rather than baked in here.
        ///
        /// Skirting is NOT measured: it runs to a room's PERIMETER, which a
        /// floor's area cannot yield. Deriving it from area would be a guess.
        /// </summary>
        public static List<CompoundLine> TiledFinish(TiledFinishInput t)
        {
            var lines = new List<CompoundLine>();
            double area = Math.Max(0, t.AreaM2);
            if (area <= 0) return lines;

            string label = string.IsNullOrWhiteSpace(t.TileLabel) ? "tiling" : t.TileLabel.Trim();
            // The TILE row carries the size band's waste. Adhesive and grout
            // do NOT: they are spread over the area and their own rules own
            // their allowance, so a mosaic's cutting waste is not a reason to
            // buy half as much adhesive again.
            lines.Add(new CompoundLine(t.IsWall ? "wall_tile" : "floor_tile",
                t.IsWall ? $"Wall tiling — {label}" : $"Floor tiling — {label}",
                "m2", area, SecPlaster)
            { WastePctOverride = t.WastePctOverride > 0 ? t.WastePctOverride : -1 });

            if (t.AdhesiveKgPerM2 > 0)
                lines.Add(new CompoundLine("tile_adhesive", "Tile adhesive", "kg",
                    area * t.AdhesiveKgPerM2, SecPlaster));
            if (t.GroutKgPerM2 > 0)
                lines.Add(new CompoundLine("tile_grout", "Tile grout", "kg",
                    area * t.GroutKgPerM2, SecPlaster));

            return lines;
        }

        /// <summary>
        /// MATSCHED-T1 — screed constituents for one screeded surface.
        ///
        /// Modelled on the plaster block of MasonryWall: an area and a thickness
        /// give a volume, and the mix ratios turn that volume into cement and
        /// sand. Quantities are NET of wastage — the supplier-unit rule owns the
        /// allowance, as it does for every other constituent; applying it here
        /// too is what made blocks arrive at ~10% instead of 5%.
        ///
        /// The area row is an INTERMEDIATE measure: you do not buy square metres
        /// of screed, you buy the cement and sand below it. It is declared in
        /// intermediateMeasures so the document keeps it for checking and zeroes
        /// its money.
        /// </summary>
        public static List<CompoundLine> Screed(ScreedInput s)
        {
            var lines = new List<CompoundLine>();
            double area = Math.Max(0, s.AreaM2);
            double thk = Math.Max(0, s.ThicknessM);
            // No area or no stated thickness → emit NOTHING. Never a default
            // quantity: a missing driver is a fact about the model, and the
            // scan reports it by name rather than papering over it here.
            if (area <= 0 || thk <= 0) return lines;

            string label = string.IsNullOrWhiteSpace(s.ScreedLabel)
                ? "cement/sand screed" : s.ScreedLabel.Trim();
            lines.Add(new CompoundLine("screed", $"Screed — {label}", "m2", area, SecPlaster));

            double vol = area * thk;
            if (s.CementBagsPerM3 > 0)
                lines.Add(new CompoundLine("screed_cement", "Screed — cement", "bag",
                    vol * s.CementBagsPerM3, SecPlaster));
            if (s.SandRatio > 0)
                lines.Add(new CompoundLine("screed_sand", "Screed — sand", "m3",
                    vol * s.SandRatio, SecPlaster));

            return lines;
        }

        /// <summary>
        /// MATSCHED-T2 — ceiling constituents.
        ///
        /// Emits board (m², converted to sheets by the supplier rule), furring
        /// (m, RATIO-DERIVED and flagged as such) and a wet plaster coat with
        /// its cement and sand — reusing the existing plaster kinds rather than
        /// minting ceiling-only twins of them, because a bag of cement is a bag
        /// of cement and two commodities would split one order into two
        /// part-loads and round each up separately.
        ///
        /// Ceiling PAINT is deliberately not emitted. Nothing in the model
        /// states whether a ceiling is painted, and inferring it from the fact
        /// that a ceiling exists is the same guess that priced whole walls as
        /// paint in the rules withdrawn by #710.
        /// </summary>
        public static List<CompoundLine> Ceiling(CeilingInput c)
        {
            var lines = new List<CompoundLine>();
            double area = Math.Max(0, c.AreaM2);
            if (area <= 0) return lines;

            string board = (c.BoardLabel ?? "").Trim();
            if (board.Length > 0)
            {
                lines.Add(new CompoundLine("ceiling_board", $"Ceiling board — {board}",
                    "m2", area, SecPlaster));

                // Furring rides on the BOARD, not on the ceiling. A skim coat on
                // a concrete soffit has no grid, and emitting one for it would
                // invent a frame the model never described.
                if (c.FurringMPerM2 > 0)
                    lines.Add(new CompoundLine("ceiling_furring",
                        "Ceiling furring / suspension grid (derived from area — not measured)",
                        "m", area * c.FurringMPerM2, SecPlaster));
            }

            string plaster = (c.PlasterLabel ?? "").Trim();
            if (plaster.Length > 0)
            {
                lines.Add(new CompoundLine("plaster", $"Ceiling plaster — {plaster}",
                    "m2", area, SecPlaster));

                double vol = area * Math.Max(0, c.PlasterThicknessM);
                if (vol > 0 && c.PlasterCementBagsPerM3 > 0)
                    lines.Add(new CompoundLine("plaster_cement", "Ceiling plaster — cement", "bag",
                        vol * c.PlasterCementBagsPerM3, SecPlaster));
                if (vol > 0 && c.PlasterSandRatio > 0)
                    lines.Add(new CompoundLine("plaster_sand", "Ceiling plaster — sand", "m3",
                        vol * c.PlasterSandRatio, SecPlaster));
            }

            return lines;
        }

        /// <summary>
        /// MATSCHED-T3 — membrane constituents. One row per membrane kind, its
        /// area multiplied by how many layers of that kind the build-up declares.
        ///
        /// Quantities are NET. Laps are a real and substantial allowance on a
        /// DPM — 150-300 mm at every joint, plus the turn-up at the perimeter —
        /// and they belong to the supplier-unit rule's wastage where they are
        /// visible and arguable, not baked in here where they would be applied
        /// twice as they were for blocks.
        /// </summary>
        public static List<CompoundLine> Membranes(MembraneInput m)
        {
            var lines = new List<CompoundLine>();
            double area = Math.Max(0, m.AreaM2);
            if (area <= 0) return lines;

            int dpm = Math.Max(0, m.DpmLayers);
            if (dpm > 0)
            {
                string label = string.IsNullOrWhiteSpace(m.DpmLabel)
                    ? "damp-proof membrane" : m.DpmLabel.Trim();
                lines.Add(new CompoundLine("dpm", $"Damp-proof membrane — {label}",
                    "m2", area * dpm, SecMasonry));
            }

            int under = Math.Max(0, m.UnderlayLayers);
            if (under > 0)
            {
                string label = string.IsNullOrWhiteSpace(m.UnderlayLabel)
                    ? "roof underlay" : m.UnderlayLabel.Trim();
                lines.Add(new CompoundLine("roof_underlay", $"Roof underlay — {label}",
                    "m2", area * under, SecMasonry));
            }

            return lines;
        }

        /// <summary>
        /// MATSCHED-T5 — roof edge accessories. One row: the fascia along the
        /// eaves.
        ///
        /// Quantities are NET; the supplier-unit rule owns the cutting and
        /// jointing allowance, as it does for every other constituent.
        /// </summary>
        // ══ What a decomposition actually MEASURED ═════════════════
        //
        //  A constituent row either measures the host itself (its concrete, its
        //  tiling, its screed) or sits ON the host without measuring it (a
        //  fascia runs along an edge and says nothing about the roof's area).
        //
        //  The distinction exists because a non-empty decomposition SUPPRESSES
        //  the composite row for the element. When the only thing a roof
        //  produced was its fascia, suppressing the composite deleted 856 m² of
        //  roof covering from an issued schedule with no warning anywhere — and
        //  took the roof_covering_m2 consumable driver down with it, so the roof
        //  fastener rule reported "driver is zero" and looked like a considered
        //  refusal rather than a missing input.
        //
        //  Kept as a LIST rather than a "not in the measuring set" test: a new
        //  kind added tomorrow measures its host until somebody says otherwise,
        //  which is the safe default — it can double-count, which review catches,
        //  where the other default silently drops quantities, which review does
        //  not.
        private static readonly HashSet<string> AccessoryKinds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fascia_board" };

        /// <summary>True for a row that sits on the host without measuring it.</summary>
        public static bool IsAccessoryKind(string kind) =>
            !string.IsNullOrWhiteSpace(kind) && AccessoryKinds.Contains(kind.Trim());

        /// <summary>
        /// True when a decomposition measured the host, so the composite row for
        /// that element may stand down. An empty decomposition measured nothing.
        /// </summary>
        public static bool MeasuresHost(IEnumerable<string> constituentKinds)
        {
            if (constituentKinds == null) return false;
            foreach (string k in constituentKinds)
                if (!IsAccessoryKind(k)) return true;
            // Nothing, or accessories only. Both mean the host is unmeasured.
            return false;
        }

        public static List<CompoundLine> RoofAccessories(RoofEdgeInput r)
        {
            var lines = new List<CompoundLine>();
            // The clamp is deliberately redundant with the guard below - either
            // alone rejects a negative length. Kept for the idiom the sibling
            // engines use, and so a future edit that loosens one still has the
            // other. Removing ONLY the clamp changes no behaviour, which is why
            // that mutation does not move any test.
            double eaves = Math.Max(0, r.EavesLengthM);
            if (eaves <= 0) return lines;

            string label = string.IsNullOrWhiteSpace(r.RoofLabel) ? "roof" : r.RoofLabel.Trim();
            lines.Add(new CompoundLine("fascia_board", $"Fascia board along eaves — {label}",
                "m", eaves, SecMasonry));
            return lines;
        }

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
