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
                                    // "ceiling_board" | "ceiling_furring"
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
        /// <summary>RETIRED. Wastage belongs to the supplier-unit rule; leaving it
        /// here as well double-counted it. Kept so existing callers still compile.</summary>
        public double UnitWastePct;      // cutting waste on the units
        public int PlasterFaces;         // 0 / 1 / 2 plastered faces
        public double PlasterThicknessM; // plaster coat thickness
        /// <summary>RETIRED — see UnitWastePct.</summary>
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
                    m.IsBrick ? "Bricks" : "Blocks", "nr", units, SecMasonry));
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
                if (plasterVol > 0 && m.PlasterCementBagsPerM3 > 0)
                    lines.Add(new CompoundLine("plaster_cement", "Plaster — cement", "bag",
                        plasterVol * m.PlasterCementBagsPerM3, SecPlaster));
                if (plasterVol > 0 && m.PlasterSandRatio > 0)
                    lines.Add(new CompoundLine("plaster_sand", "Plaster — sand", "m3",
                        plasterVol * m.PlasterSandRatio, SecPlaster));

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
            lines.Add(new CompoundLine(t.IsWall ? "wall_tile" : "floor_tile",
                t.IsWall ? $"Wall tiling — {label}" : $"Floor tiling — {label}",
                "m2", area, SecPlaster));

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
