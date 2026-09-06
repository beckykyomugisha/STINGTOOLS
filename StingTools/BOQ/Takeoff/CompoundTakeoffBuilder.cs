// ══════════════════════════════════════════════════════════════════════════
//  CompoundTakeoffBuilder.cs — MAT-3 Revit-side compound take-off.
//
//  Gathers an element's geometry + BLE_* params + the MATERIAL_LOOKUP.csv ratios,
//  feeds them to the Document-free CompoundTakeoff engine, and emits the
//  constituent BOQLineItems (blockwork/brickwork + units + mortar(+cement+sand) +
//  plaster(×faces)(+cement+sand) + formwork; concrete(net) + rebar + formwork).
//
//  Gated by COST_COMPOUND_TAKEOFF (default off) — TryBuild returns null when the
//  toggle is off or the element isn't a compound wall/RC element, so the caller
//  falls back to the single composite-rate line (legacy bills unchanged).
//
//  Only the FIRST (primary) constituent carries the RevitElementId for cost
//  write-back; the sub-constituents are report-only (id −1) so the per-element
//  CST_* stamp / IFC Qto write once, exactly as a linked-model row.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Core.Materials;
using StingTools.UI;

namespace StingTools.BOQ.Takeoff
{
    internal static class CompoundTakeoffBuilder
    {
        /// <summary>Config toggle. Default OFF so legacy composite-rate bills are
        /// untouched until a project opts in.</summary>
        /// <summary>
        /// MAT-SCHED-7 — per-run override, so the material-schedule export can turn
        /// compound take-off on for ITS OWN build without editing project config.
        ///
        /// The flag defaults off and is set in no shipped file, so the material
        /// schedule could not produce cement, sand, blocks or bricks at all until a
        /// user discovered an undocumented key. Null = defer to config, which is
        /// every other caller's behaviour, unchanged.
        /// </summary>
        [ThreadStatic] internal static bool? SessionOverride;

        /// <summary>Set the override and restore it on Dispose. Use with `using`
        /// so an exception mid-build cannot leave it stuck on.</summary>
        /// <summary>Force compound mode for one document's build. Scoped to the
        /// calling thread and to THAT document's cache — a material-schedule
        /// export in one project must not force a full BOQ re-takeoff in every
        /// other open project.</summary>
        internal static IDisposable ForceEnabled(Document doc) => new OverrideScope(true, doc);

        private sealed class OverrideScope : IDisposable
        {
            private readonly bool? _prev;
            private readonly Document _doc;

            public OverrideScope(bool value, Document doc)
            {
                _prev = SessionOverride;
                _doc = doc;
                SessionOverride = value;
                // BOQCostManager caches the host take-off. Without dropping it the
                // override changes nothing — the previous non-compound rows are
                // handed straight back. Invalidate on the way OUT too, so the next
                // ordinary BOQ build is not served compound rows it did not ask for.
                if (_doc != null) BOQCostManager.ForceHostFull(_doc);
            }

            public void Dispose()
            {
                SessionOverride = _prev;
                if (_doc != null) BOQCostManager.ForceHostFull(_doc);
            }
        }

        internal static bool Enabled()
        {
            if (SessionOverride.HasValue) return SessionOverride.Value;
            string v = TagConfig.GetConfigValue("COST_COMPOUND_TAKEOFF");
            if (string.IsNullOrWhiteSpace(v)) return false;
            v = v.Trim().ToLowerInvariant();
            return v == "1" || v == "true" || v == "yes" || v == "on";
        }

        /// <summary>Build constituent lines for a compound wall / RC element, or
        /// null to fall back to the composite line.</summary>
        internal static List<BOQLineItem> TryBuild(Document doc, Element el,
            Dictionary<string, (double rate, string unit)> csvRates,
            Dictionary<string, string> cobieCostCodes,
            StingTools.BOQ.MeasurementStandard.IMeasurementStandard measStd)
            => TryBuild(doc, el, csvRates, cobieCostCodes, measStd, out _);

        /// <summary>
        /// As above, and says whether the decomposition MEASURED the host.
        ///
        /// It can return rows and still leave the host unmeasured: a roof whose
        /// structure this path will not take off, but which produced a fascia
        /// along its eaves, yields one row that describes an edge and nothing
        /// that describes the roof. The caller must keep the composite row in
        /// that case, because a non-empty decomposition otherwise replaces it.
        /// </summary>
        internal static List<BOQLineItem> TryBuild(Document doc, Element el,
            Dictionary<string, (double rate, string unit)> csvRates,
            Dictionary<string, string> cobieCostCodes,
            StingTools.BOQ.MeasurementStandard.IMeasurementStandard measStd,
            out bool hostMeasured)
        {
            var lines = TryBuildCore(doc, el, csvRates, cobieCostCodes, measStd);
            hostMeasured = lines != null && lines.Count > 0
                && CompoundTakeoff.MeasuresHost(lines.Select(l => l.ConstituentKind));
            return lines;
        }

        private static List<BOQLineItem> TryBuildCore(Document doc, Element el,
            Dictionary<string, (double rate, string unit)> csvRates,
            Dictionary<string, string> cobieCostCodes,
            StingTools.BOQ.MeasurementStandard.IMeasurementStandard measStd)
        {
            if (doc == null || el == null) return null;
            try
            {
                string cat = ParameterHelpers.GetCategoryName(el) ?? "";
                if (cat.IndexOf("Wall", StringComparison.OrdinalIgnoreCase) >= 0)
                    return BuildWall(doc, el, csvRates);
                if (cat.IndexOf("Floor", StringComparison.OrdinalIgnoreCase) >= 0)
                    return BuildRcSlab(doc, el, csvRates);
                // A concrete roof slab is a slab. Until now Roofs decomposed into
                // nothing, so 856 m2 of 225mm roof reached one export as three
                // unpriced area rows: the supplier table could not convert them
                // (they are not sheets or tiles) and the take-off never offered
                // concrete, rebar or formwork to price instead.
                //
                // requireExplicitConcrete, unlike the Floors path: a floor with no
                // material assigned is almost always a slab, whereas an unassigned
                // ROOF is just as likely to be sheeting, and calling that concrete
                // would invent 137 m3 that does not exist.
                if (cat.IndexOf("Roof", StringComparison.OrdinalIgnoreCase) >= 0)
                    return BuildRcSlab(doc, el, csvRates, requireExplicitConcrete: true, hostIsRoof: true);
                // MATSCHED-T2 — Ceilings appeared in NO branch, so a suspended
                // gypsum ceiling decomposed into nothing at all, silently. It is
                // matched before the framing/column tests because none of those
                // can claim it, and after Roofs because neither name overlaps.
                if (cat.IndexOf("Ceiling", StringComparison.OrdinalIgnoreCase) >= 0)
                    return BuildCeiling(doc, el, csvRates);
                if (cat.IndexOf("Structural Framing", StringComparison.OrdinalIgnoreCase) >= 0
                    || cat.IndexOf("Beam", StringComparison.OrdinalIgnoreCase) >= 0)
                    return BuildRcBeam(doc, el, csvRates);
                if (cat.IndexOf("Column", StringComparison.OrdinalIgnoreCase) >= 0)
                    return BuildRcColumn(doc, el, csvRates);
                if (cat.IndexOf("Foundation", StringComparison.OrdinalIgnoreCase) >= 0)
                    return BuildRcFoundation(doc, el, csvRates);
                return null;
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("CompoundBuild", $"CompoundTakeoffBuilder.TryBuild {el?.Id}: {ex.Message}");
                return null;
            }
        }

        // ── Masonry / RC wall ───────────────────────────────────────────────
        private static List<BOQLineItem> BuildWall(Document doc, Element el,
            Dictionary<string, (double rate, string unit)> csvRates)
        {
            double areaM2 = ReadAreaM2(el);
            if (areaM2 <= 0) return null;   // can't measure → composite fallback

            string material = (GetPrimaryMaterialName(doc, el) ?? "").ToLowerInvariant();
            bool isBrick = IsBrickWall(doc, el, material);
            bool isRc = material.Contains("concrete") || material.Contains("rc") || material.Contains("reinforced");
            var res = new Resolution();

            // Units per m² + cutting waste + mortar-per-m² from bond/block tables.
            // RC-1 — canonicalise the param VALUE before composing the key, and flag
            // any lookup that falls to DEFAULT (empty or unmatched) rather than
            // silently substituting a wrong ratio.
            double unitsPerM2, unitWaste, mortarRatio;
            if (isBrick)
            {
                string bondRaw = ParameterHelpers.GetString(el, "BLE_BRICK_BOND_TYPE_TXT");
                string bond = InferOrCanon("brick bond", MaterialKeyCanonicaliser.BrickBond(bondRaw),
                    () => InferBrickBond(doc, el), res, "STRETCHER");
                unitsPerM2 = Resolve(res, "brick bond", $"BRICK_BOND {bond}", "BRICKS_PER_M2", "BRICK_BOND DEFAULT");
                unitWaste = Prop($"BRICK_BOND {bond}", "WASTE_PCT", "BRICK_BOND DEFAULT");
                mortarRatio = Prop($"BRICK_BOND {bond}", "MORTAR_RATIO", "BRICK_BOND DEFAULT");
            }
            else
            {
                string sizeRaw = ParameterHelpers.GetString(el, "BLE_BLOCK_SIZE_TXT");
                string size = InferOrCanon("block size", MaterialKeyCanonicaliser.BlockSize(sizeRaw),
                    () => InferBlockSize(doc, el), res, "DEFAULT");
                unitsPerM2 = Resolve(res, "block size", $"BLOCK {size}", "BLOCKS_PER_M2", "BLOCK DEFAULT");
                unitWaste = 5;
                mortarRatio = Prop($"BLOCK {size}", "MORTAR_VOLUME_FACTOR", "BLOCK DEFAULT");
            }

            // Mortar mix (MAT-2 corrected ratios).
            string mixRaw = ParameterHelpers.GetString(el, "BLE_MORTAR_MIX_TXT");
            string mix = InferOrCanon("mortar mix", MaterialKeyCanonicaliser.MortarMix(mixRaw),
                () => isBrick ? "1:5" : "1:4", res, isBrick ? "1:5" : "1:4");
            double mortarCement = Resolve(res, "mortar mix", $"MORTAR {mix}", "CEMENT_BAGS_PER_M3", "MORTAR DEFAULT");
            double mortarSand = Prop($"MORTAR {mix}", "SAND_RATIO", "MORTAR DEFAULT");

            // Plaster: faces (default both) + the MAT-2 plaster mix.
            int faces = ParameterHelpers.GetInt(el, "BLE_PLASTER_FACES_NR", 2);
            if (faces < 0) faces = 0; if (faces > 2) faces = 2;
            string plasterRaw = ParameterHelpers.GetString(el, "BLE_PLASTER_TYPE_TXT");
            string plasterType = InferOrCanon("plaster type", MaterialKeyCanonicaliser.PlasterType(plasterRaw),
                () => "STANDARD", res, "STANDARD");
            double plasterThk = Prop($"PLASTER {plasterType}", "THICKNESS_M", "PLASTER DEFAULT");
            double plasterWaste = Prop($"PLASTER {plasterType}", "WASTE_PCT", "PLASTER DEFAULT");
            double plasterCement = Prop($"PLASTER {plasterType}", "MIX_CEMENT_BAGS_PER_M3", "PLASTER DEFAULT");
            double plasterSand = Prop($"PLASTER {plasterType}", "MIX_SAND_RATIO", "PLASTER DEFAULT");

            var input = new MasonryWallInput
            {
                FaceAreaM2 = areaM2,
                IsBrick = isBrick,
                UnitsPerM2 = unitsPerM2,
                UnitWastePct = unitWaste,
                PlasterFaces = faces,
                PlasterThicknessM = plasterThk,
                PlasterWastePct = plasterWaste,
                MortarRatioM3PerM2 = mortarRatio,
                MortarCementBagsPerM3 = mortarCement,
                MortarSandRatio = mortarSand,
                PlasterCementBagsPerM3 = plasterCement,
                PlasterSandRatio = plasterSand,
                IsRcWall = isRc,
                IsExteriorWall = IsExteriorWall(doc, el),
                // Deducted from the PAINTED area inside the engine — a tiled face
                // is plastered as backing but never painted.
                TiledFaces = ReadTiledFinish(doc, el).Faces
            };

            // MATSCHED-T3 — counted, never measured. See NoteWallMembranes.
            NoteWallMembranes(doc, el);
            var constituents = CompoundTakeoff.MasonryWall(input);
            // Up to both faces: unlike a floor, a wall can be tiled on each side,
            // and the compound structure names one finish layer per side.
            constituents.AddRange(TilingConstituents(doc, el, areaM2, isWall: true, faces: 2));
            if (constituents.Count == 0) return null;
            return Materialise(doc, el, constituents, csvRates, isRc ? "S" : "A", res);
        }

        // ── RC slab (concrete net + rebar + formwork) ───────────────────────
        private static List<BOQLineItem> BuildRcSlab(Document doc, Element el,
            Dictionary<string, (double rate, string unit)> csvRates,
            bool requireExplicitConcrete = false, bool hostIsRoof = false)
        {
            string material = (GetPrimaryMaterialName(doc, el) ?? "").ToLowerInvariant();
            bool isConcrete = material.Contains("concrete") || material.Contains("rc");
            // Blank material reads as concrete for a FLOOR and as unknown for a
            // roof — see the Roofs branch in TryBuild.
            bool isRc = isConcrete || !(requireExplicitConcrete || material.Length != 0);

            double areaM2 = ReadAreaM2(el);

            // Tiling is read from the finish LAYER, so it survives a floor whose
            // structure this path will not measure: a timber or screeded deck
            // still has a tiled area, and dropping the element wholesale would
            // lose it. MAT-SCHED-3.
            var finishes = TilingConstituents(doc, el, areaM2, isWall: false, faces: 1);

            // MATSCHED-T1 — the screed under that finish. Read from the same
            // compound structure and, like tiling, independent of whether this
            // path can measure the STRUCTURE: a timber deck still has a screed,
            // and dropping the element wholesale would lose it.
            finishes.AddRange(ScreedConstituents(doc, el, areaM2));

            // MATSCHED-T3 — the DPM under a ground slab, or the underlay under a
            // roof covering. Same layer walk again. hostIsRoof is its OWN
            // parameter rather than a reuse of requireExplicitConcrete, which
            // happens to be true on the same call today: two meanings behind one
            // flag agree until the day one of them changes, and then they
            // disagree silently.
            finishes.AddRange(MembraneConstituents(doc, el, areaM2, hostIsRoof));

            // MATSCHED-T5 — the fascia along the eaves, measured from the roof's
            // own footprint. Roofs only: a floor has no eaves. Ridge caps and
            // barge boards are NOT emitted; the scan says why.
            if (hostIsRoof) finishes.AddRange(RoofAccessoryConstituents(doc, el));

            if (!isRc)
                return finishes.Count > 0
                    ? Materialise(doc, el, finishes, csvRates, "A", new Resolution())
                    : null;   // non-RC, unfinished floor → composite fallback

            double grossM3 = ReadVolumeM3(el);
            if (grossM3 <= 0)
                return finishes.Count > 0
                    ? Materialise(doc, el, finishes, csvRates, "A", new Resolution())
                    : null;

            // MAT-4 — parameter-driven net-concrete resolution.
            var net = Core.Materials.SlabSystemLoader.ResolveNetConcrete(doc, el, grossM3, areaM2);

            List<CompoundLine> constituents;
            if (net.IsVoid && net.Method == "calculator" && net.Calc.Valid && areaM2 > 0)
            {
                // Void slab with resolved dims → precast/block-aware constituent
                // split (in-situ net + precast ribs + blocks + mesh + rib rebar +
                // rib/edge formwork). Rib reinforcement uses a beam-like band.
                double ribBand = PropOr("REBAR_ELEMENT BEAM", "STEEL_KG_PER_M3", 120);
                constituents = CompoundTakeoff.VoidSlab(net.Calc, areaM2, net.Match.Label, ribBand);
            }
            else
            {
                // Solid slab, or a void slab resolved by geometry / flat factor →
                // the simple concrete(net) + rebar + formwork split.
                double band = PropOr("REBAR_ELEMENT SLAB", "STEEL_KG_PER_M3", 80);
                constituents = CompoundTakeoff.RcElement(new RcElementInput
                {
                    ElementKind = "slab",
                    ConcreteM3Net = net.NetConcreteM3,
                    RebarBandKgPerM3 = band,
                    FormworkM2 = net.IsVoid ? 0 : areaM2  // don't take gross soffit for void slabs
                });
            }
            constituents.AddRange(finishes);
            if (constituents.Count == 0) return null;
            return Materialise(doc, el, constituents, csvRates, "S", new Resolution());
        }

        // ── RC beam (MAT-4.3) ───────────────────────────────────────────────
        private static List<BOQLineItem> BuildRcBeam(Document doc, Element el,
            Dictionary<string, (double rate, string unit)> csvRates)
        {
            // Steel beams are not RC — leave them on the composite line.
            string material = (GetPrimaryMaterialName(doc, el) ?? "").ToLowerInvariant();
            var fi = el as FamilyInstance;
            string fam = fi?.Symbol?.FamilyName?.ToLowerInvariant() ?? "";
            if (fam.Contains("steel") || fam.Contains("ub") || fam.Contains("uc") || fam.Contains("shs")
                || material.Contains("steel"))
                return null;

            // Net length from the location curve (columns naturally deducted when
            // the beam solid is joined; SolidVolume is the accurate net concrete).
            double lengthM = 0;
            if (el.Location is LocationCurve lc && lc.Curve != null) lengthM = lc.Curve.Length * 0.3048;
            double solidM3 = SolidVolumeM3(el);
            double bM = ReadDimM(el, "b", "Width", "b1");
            double dM = ReadDimM(el, "h", "Height", "d", "Depth");
            if (solidM3 <= 0 && (bM <= 0 || dM <= 0 || lengthM <= 0)) return null;

            double band = PropOr("REBAR_ELEMENT BEAM", "STEEL_KG_PER_M3", 120);
            var constituents = CompoundTakeoff.RcBeam(new RcBeamInput
            {
                WidthM = bM,
                DepthM = dM,
                NetLengthM = lengthM,
                SlabBearingM = 0.150,   // typical slab bearing; refine per project
                ConcreteM3Override = solidM3,  // accurate net (columns trimmed)
                RebarBandKgPerM3 = band
            });
            if (constituents.Count == 0) return null;
            return Materialise(doc, el, constituents, csvRates, "S", new Resolution());
        }

        // ── RC-1 param-resolution tracking (silent-DEFAULT elimination) ──────
        private sealed class Resolution
        {
            public readonly List<string> Empty = new List<string>();      // param unset → project default
            public readonly List<string> Unmatched = new List<string>();  // value set but not in table (typo)
            public bool Any => Empty.Count > 0 || Unmatched.Count > 0;
            public int ConfidenceFloor => Unmatched.Count > 0 ? 35 : (Empty.Count > 0 ? 55 : 100);
            public string Note()
            {
                var parts = new List<string>();
                if (Unmatched.Count > 0) parts.Add("UNMATCHED→DEFAULT (check value): " + string.Join(", ", Unmatched));
                if (Empty.Count > 0) parts.Add("param empty→project default: " + string.Join(", ", Empty));
                return parts.Count > 0 ? "[Ratio: " + string.Join("; ", parts) + "]" : "";
            }
        }

        // ── Materialise constituent lines into BOQLineItems ─────────────────
        private static List<BOQLineItem> Materialise(Document doc, Element el,
            List<CompoundLine> constituents,
            Dictionary<string, (double rate, string unit)> csvRates, string discipline,
            Resolution res)
        {
            // RC-1 — surface DEFAULT falls: a value set but unmatched after
            // normalisation is the dangerous silent-wrong case, so warn once.
            if (res != null && res.Unmatched.Count > 0)
                StingLog.WarnRateLimited("CompoundDefault",
                    $"Compound take-off {el?.Id}: ratio param(s) did not resolve, DEFAULT used — {string.Join("; ", res.Unmatched)}. Row confidence lowered.");
            int confFloor = res?.ConfidenceFloor ?? 100;
            string resNote = res?.Note() ?? "";

            var outList = new List<BOQLineItem>(constituents.Count);
            long elId = el.Id?.Value ?? -1;
            bool firstWriteback = true;
            foreach (var c in constituents)
            {
                (double rate, string source, int conf) = ResolveConstituentRate(csvRates, c);
                // A DEFAULTed ratio lowers the row's confidence so it routes to the
                // uncosted / low-confidence at-risk rollup instead of reading as a
                // confident number.
                conf = Math.Min(conf, confFloor);
                var line = new BOQLineItem
                {
                    NRM2Section = c.Nrm2Section,
                    Category = ConstituentCategory(c.Kind),
                    Discipline = discipline,
                    ItemName = c.Description,
                    FamilyName = GetFamilyName(doc, el),
                    TypeName = el.Name ?? "",
                    MaterialName = GetPrimaryMaterialName(doc, el) ?? "",
                    Quantity = Math.Round(c.Quantity, 3),
                    Unit = c.Unit,
                    ConstituentKind = c.Kind,
                    GrossQuantity = Math.Round(c.Quantity, 3),
                    RateUGX = rate,
                    RateSource = source,
                    RateConfidence = conf,
                    Source = BOQRowSource.Model,
                    // Only the first (primary) constituent writes back to the element.
                    RevitElementId = firstWriteback ? elId : -1,
                    UniqueId = firstWriteback ? el.UniqueId : "",
                    Note = string.IsNullOrEmpty(resNote) ? $"[Compound: {c.Kind}]" : $"[Compound: {c.Kind}] {resNote}",
                    LastCosted = DateTime.UtcNow
                };
                outList.Add(line);
                firstWriteback = false;
            }
            return outList;
        }

        private static (double rate, string source, int conf) ResolveConstituentRate(
            Dictionary<string, (double rate, string unit)> csvRates, CompoundLine c)
        {
            // Constituent rates come from cost_rates_5d.csv by a constituent key
            // (e.g. "Blockwork", "Plaster", "Formwork", "Reinforcement"). When a
            // project hasn't priced a constituent the line is honestly flagged
            // low-confidence (rate 0) rather than borrowing a composite rate.
            string key = ConstituentCategory(c.Kind);
            if (csvRates != null && csvRates.TryGetValue(key, out var hit) && hit.rate > 0)
                return (hit.rate, "CSV", 80);
            return (0, "None", 20);
        }

        private static string ConstituentCategory(string kind)
        {
            switch (kind)
            {
                case "blockwork": return "Blockwork";
                case "brickwork": return "Brickwork";
                case "units": return "Masonry Units";
                case "mortar": return "Mortar";
                case "mortar_cement": return "Cement";
                case "mortar_sand": return "Sand";
                case "plaster": return "Plaster";
                case "screed": return "Screed";
                case "dpm": return "Damp-proof Membrane";
                case "fascia_board": return "Fascia Board";
                case "roof_underlay": return "Roof Underlay";
                case "ceiling_board": return "Ceiling Board";
                case "ceiling_furring": return "Ceiling Furring";
                case "screed_cement": return "Cement";
                case "screed_sand": return "Sand";
                case "paint_interior": return "Painting";
                case "paint_exterior": return "Painting";
                case "plaster_cement": return "Cement";
                case "plaster_sand": return "Sand";
                case "concrete": return "In-situ Concrete";
                case "precast_rib": return "Precast Concrete";
                case "infill_block": return "Infill Blocks";
                case "mesh": return "Mesh Reinforcement";
                case "rebar": return "Reinforcement";
                case "formwork": return "Formwork";
                default: return kind;
            }
        }

        // ── RC column ───────────────────────────────────────────────────────
        //  MAT-SCHED — columns used to keep the composite line, so their
        //  concrete, rebar and formwork never reached the material schedule and
        //  the bill was under-measured.
        private static List<BOQLineItem> BuildRcColumn(Document doc, Element el,
            Dictionary<string, (double rate, string unit)> csvRates)
        {
            string material = (GetPrimaryMaterialName(doc, el) ?? "").ToLowerInvariant();
            // A steel or timber column is not RC — leave it as the composite line
            // rather than inventing concrete for it.
            if (material.Contains("steel") || material.Contains("timber") || material.Contains("wood"))
                return null;

            double volM3 = ReadSolidVolumeM3(doc, el);
            var (bx, by, hz) = ReadBoundingBoxM(el);
            if (volM3 <= 0 && (bx <= 0 || by <= 0 || hz <= 0)) return null;

            double band = PropOr("REBAR_ELEMENT COLUMN", "STEEL_KG_PER_M3", 160);
            bool round = LooksRound(doc, el);

            var constituents = CompoundTakeoff.RcColumn(new RcColumnInput
            {
                WidthM = bx, DepthM = by, HeightM = hz,
                DiameterM = round ? Math.Min(bx, by) : 0,
                ConcreteM3Override = volM3,
                RebarBandKgPerM3 = band
            });
            return constituents.Count == 0 ? null : Materialise(doc, el, constituents, csvRates, "S", new Resolution());
        }

        // ── RC foundation ───────────────────────────────────────────────────
        private static List<BOQLineItem> BuildRcFoundation(Document doc, Element el,
            Dictionary<string, (double rate, string unit)> csvRates)
        {
            double volM3 = ReadSolidVolumeM3(doc, el);
            var (bx, by, hz) = ReadBoundingBoxM(el);
            if (volM3 <= 0 && (bx <= 0 || by <= 0 || hz <= 0)) return null;

            string typeName = (el.Name ?? "").ToLowerInvariant();
            bool blinding = typeName.Contains("blinding") || typeName.Contains("lean");
            double band = PropOr("REBAR_ELEMENT FOUNDATION", "STEEL_KG_PER_M3", 100);

            var constituents = CompoundTakeoff.RcFoundation(new RcFoundationInput
            {
                LengthM = bx, WidthM = by, DepthM = hz,
                ConcreteM3Override = volM3,
                RebarBandKgPerM3 = band,
                IsBlinding = blinding,
                // Formed by default. Omitting it silently under-measures, which is
                // the failure this whole change exists to fix; a project casting
                // against the excavation can zero the formwork rate instead.
                FormworkToSides = true
            });
            return constituents.Count == 0 ? null : Materialise(doc, el, constituents, csvRates, "S", new Resolution());
        }

        /// <summary>Solid volume in m³, summed over the element's geometry.</summary>
        private static double ReadSolidVolumeM3(Document doc, Element el)
        {
            try
            {
                var opt = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine };
                var ge = el?.get_Geometry(opt);
                if (ge == null) return 0;
                double ft3 = SumSolidsFt3(ge);
                return ft3 * 0.0283168;
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("RcVolume", $"ReadSolidVolumeM3 {el?.Id}: {ex.Message}");
                return 0;
            }
        }

        private static double SumSolidsFt3(GeometryElement ge)
        {
            double total = 0;
            foreach (GeometryObject go in ge)
            {
                if (go is Solid s && s.Volume > 0) total += s.Volume;
                else if (go is GeometryInstance gi)
                {
                    var inner = gi.GetInstanceGeometry();
                    if (inner != null) total += SumSolidsFt3(inner);
                }
            }
            return total;
        }

        /// <summary>Bounding-box extents in metres (x, y, z). Used for formwork
        /// only — the solid volume, when available, is what prices the concrete.</summary>
        private static (double x, double y, double z) ReadBoundingBoxM(Element el)
        {
            try
            {
                var bb = el?.get_BoundingBox(null);
                if (bb == null) return (0, 0, 0);
                return ((bb.Max.X - bb.Min.X) * 0.3048,
                        (bb.Max.Y - bb.Min.Y) * 0.3048,
                        (bb.Max.Z - bb.Min.Z) * 0.3048);
            }
            catch { return (0, 0, 0); }
        }

        /// <summary>Round columns shutter to their circumference, not a box.</summary>
        private static bool LooksRound(Document doc, Element el)
        {
            try
            {
                string n = ((el?.Name ?? "") + " " + (doc?.GetElement(el.GetTypeId())?.Name ?? "")).ToLowerInvariant();
                return n.Contains("round") || n.Contains("circular") || n.Contains("diameter") || n.Contains("dia.");
            }
            catch { return false; }
        }

        // ── Small Revit helpers ─────────────────────────────────────────────

        /// <summary>
        /// True when the wall type is marked Exterior. Exterior walls take
        /// weather-guard, interior walls silk — different products at different
        /// prices. Unknown/unreadable defaults to INTERIOR, the cheaper and more
        /// common case, rather than inflating a bill with exterior-grade paint.
        /// </summary>
        private static bool IsExteriorWall(Document doc, Element el)
        {
            try
            {
                var wt = doc?.GetElement(el?.GetTypeId()) as WallType;
                return wt != null && wt.Function == WallFunction.Exterior;
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("WallFunction", $"IsExteriorWall {el?.Id}: {ex.Message}");
                return false;
            }
        }

        private static double ReadAreaM2(Element el)
        {
            var p = el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
            return (p != null && p.HasValue) ? p.AsDouble() * 0.092903 : 0;
        }

        private static double ReadVolumeM3(Element el)
        {
            var p = el.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
            return (p != null && p.HasValue) ? p.AsDouble() * 0.0283168 : 0;
        }

        private static double SolidVolumeM3(Element el)
        {
            try
            {
                var opt = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine };
                var geo = el.get_Geometry(opt);
                double ft3 = 0;
                if (geo != null)
                    foreach (GeometryObject g in geo)
                    {
                        if (g is Solid s && s.Volume > 0) ft3 += s.Volume;
                        else if (g is GeometryInstance gi)
                        {
                            var inst = gi.GetInstanceGeometry();
                            if (inst != null)
                                foreach (GeometryObject g2 in inst)
                                    if (g2 is Solid s2 && s2.Volume > 0) ft3 += s2.Volume;
                        }
                    }
                return ft3 * 0.0283168;
            }
            catch (Exception ex) { StingLog.WarnRateLimited("CompoundBeamVol", $"SolidVolumeM3: {ex.Message}"); return 0; }
        }

        // Read a cross-section dimension (mm-family param) in metres. Tries the
        // instance then the type; family length params are internal ft → m.
        private static double ReadDimM(Element el, params string[] names)
        {
            foreach (var n in names)
            {
                try
                {
                    var p = el.LookupParameter(n);
                    if (p != null && p.HasValue && p.StorageType == StorageType.Double && p.AsDouble() > 0)
                        return p.AsDouble() * 0.3048;
                }
                catch { }
            }
            // Type-level fallback.
            try
            {
                var typeId = el.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId
                    && el.Document.GetElement(typeId) is Element t)
                    foreach (var n in names)
                    {
                        var p = t.LookupParameter(n);
                        if (p != null && p.HasValue && p.StorageType == StorageType.Double && p.AsDouble() > 0)
                            return p.AsDouble() * 0.3048;
                    }
            }
            catch (Exception ex) { StingLog.WarnRateLimited("CompoundDim", $"ReadDimM: {ex.Message}"); }
            return 0;
        }

        // E-5 — was the FIRST id from GetMaterialIds(false). Now the shared
        // dominant-by-volume resolver, so a compound element's constituent lines
        // name the same governing material the rate, carbon and description use.
        private static string GetPrimaryMaterialName(Document doc, Element el)
        {
            string n = StingTools.BOQ.PrimaryMaterial.Resolve(el);
            return string.IsNullOrEmpty(n) ? null : n;
        }

        private static string GetFamilyName(Document doc, Element el)
        {
            try { return ParameterHelpers.GetFamilyName(el); } catch { return ""; }
        }


        // ── Tiling from the finish layer (MAT-SCHED-3) ──────────────────────

        /// <summary>
        /// What the tile scan looked at, so "no tiling appeared" stops being
        /// silence and starts being evidence.
        ///
        /// The first export after tiling shipped produced ZERO tiling rows and
        /// no errors, and two very different causes fit that output exactly:
        /// the model describes no finish layers at all, or it describes them
        /// under names the pattern does not recognise. Nothing in the workbook
        /// or the log separated the two. An absent side effect never tells you
        /// why — so the scan now reports its own denominator.
        ///
        /// Keyed by TYPE: a compound structure belongs to the type, so this also
        /// stops re-walking the same layers once per instance.
        /// </summary>
        /// <summary>
        /// Per-run tile-scan cache and tally. Keyed by TYPE: a compound
        /// structure belongs to the type, so this also stops re-walking the same
        /// layers once per instance. See TileScanTally for why the counts exist.
        /// </summary>
        internal static class TileFinishScan
        {
            private static readonly Dictionary<long, (int Faces, string Label)> Cache =
                new Dictionary<long, (int, string)>();

            public static readonly StingTools.Core.MaterialSchedule.TileScanTally Tally =
                new StingTools.Core.MaterialSchedule.TileScanTally();

            public static void Reset() { Cache.Clear(); Tally.Reset(); }

            public static bool TryGet(long typeId, out (int Faces, string Label) hit)
                => Cache.TryGetValue(typeId, out hit);

            public static void Store(long typeId, (int Faces, string Label) hit)
                => Cache[typeId] = hit;

            public static string Summary() => Tally.Summary();
        }

        // -- the shared layer walk (MATSCHED-T1) -----------------------------
        //
        //  ReadTiledFinish used to own the CompoundStructure walk outright, and
        //  it was tile-specific: it looked at Finish1/Finish2 only and asked one
        //  question of each layer. A 40 mm Substrate screed sitting in the same
        //  structure was therefore invisible, not by decision but by omission.
        //
        //  The walk is now a primitive that returns EVERY layer with its
        //  function, its material name and its thickness; each consumer applies
        //  its own predicate and keeps its own tally. Nothing about the tile
        //  answer changed - the same layers, the same predicate, the same counts.

        /// <summary>One compound-structure layer, as the take-off needs it.</summary>
        internal struct HostLayer
        {
            public MaterialFunctionAssignment Function;
            /// <summary>"" when the layer names no material - that is a distinct
            /// finding from a material that failed a predicate, and the tallies
            /// report the two separately.</summary>
            public string MaterialName;
            public double ThicknessM;
        }

        /// <summary>Feet to metres. Exact; CompoundStructureLayer.Width is internal units.</summary>
        private const double FeetToM = 0.3048;

        /// <summary>
        /// Per-run compound-structure cache, keyed by TYPE. A compound structure
        /// belongs to the type, so this stops re-walking the same layers once per
        /// instance - the reason the tile scan was type-keyed in the first place.
        /// </summary>
        internal static class HostLayerCache
        {
            private static readonly Dictionary<long, List<HostLayer>> Cache =
                new Dictionary<long, List<HostLayer>>();

            public static void Reset() { Cache.Clear(); }

            /// <summary>
            /// The type's layers, or NULL when it declares no compound structure
            /// at all. Null and empty are DIFFERENT answers: "the model does not
            /// describe this build-up" is not "the build-up is empty", and only
            /// the first is a reason for a consumer to report nothing inspected.
            /// </summary>
            public static List<HostLayer> Get(Document doc, Element el)
            {
                var typeId = el?.GetTypeId();
                if (doc == null || typeId == null || typeId == ElementId.InvalidElementId) return null;
                long key = typeId.Value;
                if (Cache.TryGetValue(key, out var cached)) return cached;

                List<HostLayer> hit = null;
                try
                {
                    var hoa = doc.GetElement(typeId) as HostObjAttributes;
                    var cs = hoa?.GetCompoundStructure();
                    var layers = cs?.GetLayers();
                    if (layers != null)
                    {
                        hit = new List<HostLayer>(layers.Count);
                        foreach (var layer in layers)
                        {
                            if (layer == null) continue;
                            string name = "";
                            if (layer.MaterialId != null
                             && layer.MaterialId != ElementId.InvalidElementId
                             && doc.GetElement(layer.MaterialId) is Material mat
                             && !string.IsNullOrWhiteSpace(mat.Name))
                                name = mat.Name.Trim();
                            hit.Add(new HostLayer
                            {
                                Function = layer.Function,
                                MaterialName = name,
                                ThicknessM = layer.Width * FeetToM
                            });
                        }
                    }
                    Cache[key] = hit;   // cached only on success
                }
                catch (Exception ex)
                {
                    // NOT cached: a failed read is not the same finding as a type
                    // with no compound structure, and caching it as one would make
                    // a transient failure look like a permanent fact about the model.
                    StingLog.WarnRateLimited("HostLayers", $"HostLayerCache {el?.Id}: {ex.Message}");
                    return null;
                }
                return hit;
            }
        }

        // -- screed scan (MATSCHED-T1) ---------------------------------------

        /// <summary>A type's screed thickness (summed over its screed layers) and its name.</summary>
        internal struct ScreedHit
        {
            public double ThicknessM;
            public string Label;
        }

        /// <summary>Per-run screed-scan cache and tally. See ScreedScanTally for
        /// why the counts exist.</summary>
        internal static class ScreedScan
        {
            private static readonly Dictionary<long, ScreedHit> Cache = new Dictionary<long, ScreedHit>();

            public static readonly StingTools.Core.MaterialSchedule.ScreedScanTally Tally =
                new StingTools.Core.MaterialSchedule.ScreedScanTally();

            public static void Reset() { Cache.Clear(); Tally.Reset(); }

            public static bool TryGet(long typeId, out ScreedHit hit) => Cache.TryGetValue(typeId, out hit);
            public static void Store(long typeId, ScreedHit hit) { Cache[typeId] = hit; }
            public static string Summary() => Tally.Summary();
        }

        /// <summary>Reset every per-run layer scan. Called once per material-schedule
        /// build, before the take-off: a stale count from a previous export answers
        /// the wrong question.</summary>
        internal static void ResetLayerScans()
        {
            HostLayerCache.Reset();
            TileFinishScan.Reset();
            ScreedScan.Reset();
            CeilingScan.Reset();
            MembraneScan.Reset();
            RoofAccessoryScan.Reset();
        }

        /// <summary>
        /// Layer functions a screed can legitimately occupy. Substrate is the
        /// ordinary case - a screed under a tiled or vinyl floor. Finish1/Finish2
        /// are admitted too because a granolithic screed IS the wearing surface;
        /// restricting to Substrate would silently drop it. There is no risk of
        /// measuring a tiled face twice: FinishTextClassifier.IsScreed returns
        /// false for anything IsTile accepts, by construction.
        /// </summary>
        private static bool IsScreedCandidateFunction(MaterialFunctionAssignment f)
            => f == MaterialFunctionAssignment.Substrate
            || f == MaterialFunctionAssignment.Finish1
            || f == MaterialFunctionAssignment.Finish2;

        /// <summary>Screed layers on a host type: total thickness, named by the first.</summary>
        private static ScreedHit ReadScreed(Document doc, Element el)
        {
            try
            {
                var typeId = el?.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId) return default(ScreedHit);
                long key = typeId.Value;
                if (ScreedScan.TryGet(key, out var cached)) return cached;

                var layers = HostLayerCache.Get(doc, el);
                if (layers == null) { ScreedScan.Store(key, default(ScreedHit)); return default(ScreedHit); }

                ScreedScan.Tally.TypesInspected++;
                double thk = 0; string label = ""; bool anyCandidate = false;
                foreach (var layer in layers)
                {
                    if (!IsScreedCandidateFunction(layer.Function)) continue;
                    anyCandidate = true;
                    if (string.IsNullOrEmpty(layer.MaterialName)) continue;
                    if (!StingTools.Core.MaterialSchedule.FinishTextClassifier.IsScreed(layer.MaterialName))
                    {
                        // Recorded, not discarded: if the pattern is the thing
                        // that is wrong, these names are the evidence for it.
                        ScreedScan.Tally.RejectedMaterials.Add(layer.MaterialName);
                        continue;
                    }
                    if (layer.ThicknessM <= 0)
                    {
                        // The name was right and the DRIVER is missing. Emit
                        // nothing and say so, rather than assume a thickness.
                        ScreedScan.Tally.MatchedButZeroThickness++;
                        continue;
                    }
                    thk += layer.ThicknessM;
                    if (label.Length == 0) label = layer.MaterialName;
                }
                if (anyCandidate) ScreedScan.Tally.TypesWithCandidateLayer++;
                if (thk > 0) ScreedScan.Tally.TypesMatched++;

                var hit = new ScreedHit { ThicknessM = thk, Label = label };
                ScreedScan.Store(key, hit);
                return hit;
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("ScreedFinish", $"ReadScreed {el?.Id}: {ex.Message}");
                return default(ScreedHit);
            }
        }

        /// <summary>
        /// Screed constituents for a host, or an empty list when it carries no
        /// screed layer. Wired into the FLOOR/ROOF path only: a screed is laid on
        /// a horizontal surface, and a wall's "sand-cement" substrate is its
        /// render, which the wall path already measures as plaster.
        /// </summary>
        private static List<CompoundLine> ScreedConstituents(Document doc, Element el, double areaM2)
        {
            var empty = new List<CompoundLine>();
            if (areaM2 <= 0) return empty;

            var found = ReadScreed(doc, el);
            if (found.ThicknessM <= 0) return empty;

            var mix = ScreedMix(found.Label);
            return CompoundTakeoff.Screed(new ScreedInput
            {
                AreaM2 = areaM2,
                ThicknessM = found.ThicknessM,
                ScreedLabel = found.Label,
                CementBagsPerM3 = mix.CementBagsPerM3,
                SandRatio = mix.SandRatio
            });
        }

        /// <summary>
        /// Screed mix ratios for a material name, from the SCREED rows that
        /// MATERIAL_LOOKUP already ships. Internal so the shipped-data test
        /// resolves the keys through the SAME key composition the builder uses —
        /// a key the builder spells one way and the CSV another is not an error,
        /// it is a zero, and a zero drops the cement and sand rows without a word.
        ///
        /// Two SCREED properties are deliberately NOT read:
        ///   * THICKNESS_M — the compound-structure layer states the thickness,
        ///     and falling back to a table default would invent a screed volume
        ///     for a layer that declares none. A missing driver emits nothing.
        ///   * WASTE_PCT — wastage lives in exactly one place, the supplier-unit
        ///     rule. Applying it here as well is what put ~10% on blocks.
        /// </summary>
        internal static (double CementBagsPerM3, double SandRatio) ScreedMix(string screedName)
        {
            string key = StingTools.Core.MaterialSchedule.FinishTextClassifier.ScreedKey(screedName);
            return (Prop($"SCREED {key}", "MIX_CEMENT_BAGS_PER_M3", "SCREED DEFAULT"),
                    Prop($"SCREED {key}", "MIX_SAND_RATIO", "SCREED DEFAULT"));
        }

        // -- roof accessory scan (MATSCHED-T5) -------------------------------

        /// <summary>Per-run roof-accessory tally. Instance-keyed work, so unlike
        /// the layer scans there is no type cache: a footprint belongs to the
        /// ELEMENT, and two roofs of one type have different boundaries.</summary>
        internal static class RoofAccessoryScan
        {
            public static readonly StingTools.Core.MaterialSchedule.RoofAccessoryTally Tally =
                new StingTools.Core.MaterialSchedule.RoofAccessoryTally();

            public static void Reset() { Tally.Reset(); }
            public static string Summary() => Tally.Summary();
        }

        /// <summary>
        /// Eaves length for a roof, or 0.
        ///
        /// A FootPrintRoof's sketch says which of its boundary curves DEFINE
        /// SLOPE. Those are the eaves — horizontal, so their plan length is
        /// their true length, and a fascia runs along them. The rest are gable
        /// edges, whose plan length is NOT the rake a barge board runs; those
        /// are totalled for the scan to report and are never emitted.
        ///
        /// Only the FIRST profile loop is measured. Later loops are openings,
        /// and an opening's edge is not an eave.
        /// </summary>
        private static double ReadRoofEavesLengthM(Document doc, Element el)
        {
            try
            {
                RoofAccessoryScan.Tally.RoofsInspected++;

                // A flat RC slab roof carries no fascia. Checked before the
                // footprint, because a concrete roof usually HAS one and would
                // otherwise produce a confident fascia run for a parapet.
                string material = (GetPrimaryMaterialName(doc, el) ?? "").ToLowerInvariant();
                if (material.Contains("concrete") || material.Contains("rc")
                 || material.Contains("reinforced"))
                {
                    RoofAccessoryScan.Tally.ConcreteRoofsSkipped++;
                    return 0;
                }

                if (!(el is FootPrintRoof fp))
                {
                    // An ExtrusionRoof or a roof by face carries no boundary
                    // sketch. Reported, not guessed at.
                    RoofAccessoryScan.Tally.RoofsWithoutFootprint++;
                    return 0;
                }

                var profiles = fp.GetProfiles();
                if (profiles == null || profiles.Size == 0)
                {
                    RoofAccessoryScan.Tally.RoofsWithoutFootprint++;
                    return 0;
                }
                RoofAccessoryScan.Tally.RoofsWithFootprint++;
                if (profiles.Size > 1) RoofAccessoryScan.Tally.RoofsWithInnerLoops++;

                double eaves = 0, verge = 0;
                var outer = profiles.get_Item(0);
                foreach (var obj in outer)
                {
                    var mc = obj as ModelCurve;
                    double len = mc?.GeometryCurve?.Length ?? 0;
                    if (len <= 0) continue;
                    len *= FeetToM;

                    bool definesSlope;
                    try { definesSlope = fp.get_DefinesSlope(mc); }
                    catch { continue; }   // cannot classify the edge -> do not guess

                    if (definesSlope) eaves += len; else verge += len;
                }

                RoofAccessoryScan.Tally.EavesLengthM += eaves;
                RoofAccessoryScan.Tally.VergePlanLengthM += verge;
                if (eaves > 0) RoofAccessoryScan.Tally.RoofsWithEaves++;
                return eaves;
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("RoofEdges", $"ReadRoofEavesLengthM {el?.Id}: {ex.Message}");
                return 0;
            }
        }

        /// <summary>Roof accessory constituents, or an empty list.</summary>
        private static List<CompoundLine> RoofAccessoryConstituents(Document doc, Element el)
        {
            double eaves = ReadRoofEavesLengthM(doc, el);
            if (eaves <= 0) return new List<CompoundLine>();

            return CompoundTakeoff.RoofAccessories(new RoofEdgeInput
            {
                EavesLengthM = eaves,
                RoofLabel = el?.Name ?? ""
            });
        }

        // -- membrane scan (MATSCHED-T3) -------------------------------------

        /// <summary>A type's membrane layers, counted by kind and named by the first.</summary>
        internal struct MembraneHit
        {
            public int DpmLayers;
            public string DpmLabel;
            public int UnderlayLayers;
            public string UnderlayLabel;

            public bool Any => DpmLayers > 0 || UnderlayLayers > 0;
        }

        /// <summary>Per-run membrane-scan cache and tally. See MembraneScanTally
        /// for why the counts exist — including the two counters that report
        /// what this take-off deliberately does NOT price.</summary>
        internal static class MembraneScan
        {
            private static readonly Dictionary<long, MembraneHit> Cache = new Dictionary<long, MembraneHit>();

            public static readonly StingTools.Core.MaterialSchedule.MembraneScanTally Tally =
                new StingTools.Core.MaterialSchedule.MembraneScanTally();

            /// <summary>Wall types already counted, so a 300-instance wall does
            /// not report its one membrane layer 300 times.</summary>
            private static readonly HashSet<long> WallTypesCounted = new HashSet<long>();

            public static void Reset() { Cache.Clear(); WallTypesCounted.Clear(); Tally.Reset(); }

            public static bool TryGet(long typeId, out MembraneHit hit) => Cache.TryGetValue(typeId, out hit);
            public static void Store(long typeId, MembraneHit hit) { Cache[typeId] = hit; }
            public static bool FirstWallSighting(long typeId) => WallTypesCounted.Add(typeId);
            public static string Summary() => Tally.Summary();
        }

        /// <summary>Membrane layers on a floor or roof type.</summary>
        private static MembraneHit ReadMembranes(Document doc, Element el, bool hostIsRoof)
        {
            try
            {
                var typeId = el?.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId) return default(MembraneHit);
                long key = typeId.Value;
                if (MembraneScan.TryGet(key, out var cached)) return cached;

                var layers = HostLayerCache.Get(doc, el);
                if (layers == null) { MembraneScan.Store(key, default(MembraneHit)); return default(MembraneHit); }

                MembraneScan.Tally.TypesInspected++;
                var hit = new MembraneHit();
                bool anyMembraneLayer = false;

                foreach (var layer in layers)
                {
                    // Insulation is reported wherever it sits, membrane-function
                    // or its own. The caution for this task is not "do not match
                    // it" but "say that you saw it": insulation is bought by
                    // THICKNESS, so absorbing it into a per-m2 roll commodity
                    // would silently mis-price both.
                    if (layer.Function == MaterialFunctionAssignment.Insulation
                     || (!string.IsNullOrEmpty(layer.MaterialName)
                         && StingTools.Core.MaterialSchedule.FinishTextClassifier.IsInsulation(layer.MaterialName)))
                    {
                        MembraneScan.Tally.InsulationLayersSeen++;
                        if (!string.IsNullOrEmpty(layer.MaterialName))
                            MembraneScan.Tally.InsulationMaterials.Add(layer.MaterialName);
                        continue;
                    }

                    if (layer.Function != MaterialFunctionAssignment.Membrane) continue;
                    anyMembraneLayer = true;
                    if (string.IsNullOrEmpty(layer.MaterialName)) continue;

                    string kind = StingTools.Core.MaterialSchedule.FinishTextClassifier
                        .MembraneKind(layer.MaterialName, hostIsRoof);
                    if (kind == "dpm")
                    {
                        hit.DpmLayers++;
                        if (string.IsNullOrEmpty(hit.DpmLabel)) hit.DpmLabel = layer.MaterialName;
                    }
                    else if (kind == "roof_underlay")
                    {
                        hit.UnderlayLayers++;
                        if (string.IsNullOrEmpty(hit.UnderlayLabel)) hit.UnderlayLabel = layer.MaterialName;
                    }
                    else
                    {
                        // Recorded, not discarded: if the pattern is the thing
                        // that is wrong, these names are the evidence for it.
                        MembraneScan.Tally.RejectedMaterials.Add(layer.MaterialName);
                    }
                }

                if (anyMembraneLayer) MembraneScan.Tally.TypesWithMembraneLayer++;
                if (hit.Any) MembraneScan.Tally.TypesMatched++;

                MembraneScan.Store(key, hit);
                return hit;
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("Membranes", $"ReadMembranes {el?.Id}: {ex.Message}");
                return default(MembraneHit);
            }
        }

        /// <summary>
        /// Membrane constituents for a floor or roof, or an empty list.
        /// </summary>
        private static List<CompoundLine> MembraneConstituents(Document doc, Element el,
            double areaM2, bool hostIsRoof)
        {
            var empty = new List<CompoundLine>();
            if (areaM2 <= 0) return empty;

            var found = ReadMembranes(doc, el, hostIsRoof);
            if (!found.Any) return empty;

            return CompoundTakeoff.Membranes(new MembraneInput
            {
                AreaM2 = areaM2,
                DpmLayers = found.DpmLayers,
                DpmLabel = found.DpmLabel,
                UnderlayLayers = found.UnderlayLayers,
                UnderlayLabel = found.UnderlayLabel
            });
        }

        /// <summary>
        /// A wall's membrane layers are COUNTED and never measured.
        ///
        /// A layer's area on a wall is the wall FACE area, and a horizontal
        /// damp-proof course occupies one course of it — measuring the face
        /// would over-order by an order of magnitude, and nothing in the layer
        /// distinguishes a one-course DPC from a full-height cavity membrane.
        /// Emitting nothing is the honest answer; emitting nothing SILENTLY is
        /// the failure this whole task exists to remove, so the scope boundary
        /// is reported instead of assumed.
        /// </summary>
        private static void NoteWallMembranes(Document doc, Element el)
        {
            try
            {
                var typeId = el?.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId) return;
                if (!MembraneScan.FirstWallSighting(typeId.Value)) return;   // once per TYPE

                var layers = HostLayerCache.Get(doc, el);
                if (layers == null) return;
                foreach (var layer in layers)
                {
                    if (layer.Function != MaterialFunctionAssignment.Membrane) continue;
                    MembraneScan.Tally.WallMembraneLayersSeen++;
                    return;   // one sighting per type is the finding, not per layer
                }
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("WallMembranes", $"NoteWallMembranes {el?.Id}: {ex.Message}");
            }
        }

        // -- ceiling scan (MATSCHED-T2) --------------------------------------

        /// <summary>A type's ceiling finish: the board it names, the wet coat it
        /// names, and that coat's declared thickness.</summary>
        internal struct CeilingHit
        {
            public string BoardLabel;
            public string PlasterLabel;
            public double PlasterThicknessM;

            /// <summary>False when the type named neither, so the caller falls
            /// back to the composite line instead of emitting an empty section.</summary>
            public bool Any => !string.IsNullOrEmpty(BoardLabel) || !string.IsNullOrEmpty(PlasterLabel);
        }

        /// <summary>Per-run ceiling-scan cache and tally. See CeilingScanTally for
        /// why the counts exist.</summary>
        internal static class CeilingScan
        {
            private static readonly Dictionary<long, CeilingHit> Cache = new Dictionary<long, CeilingHit>();

            public static readonly StingTools.Core.MaterialSchedule.CeilingScanTally Tally =
                new StingTools.Core.MaterialSchedule.CeilingScanTally();

            public static void Reset() { Cache.Clear(); Tally.Reset(); }

            public static bool TryGet(long typeId, out CeilingHit hit) => Cache.TryGetValue(typeId, out hit);
            public static void Store(long typeId, CeilingHit hit) { Cache[typeId] = hit; }
            public static string Summary() => Tally.Summary();
        }

        /// <summary>Board and plaster layers on a ceiling type.</summary>
        private static CeilingHit ReadCeilingFinish(Document doc, Element el)
        {
            try
            {
                var typeId = el?.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId) return default(CeilingHit);
                long key = typeId.Value;
                if (CeilingScan.TryGet(key, out var cached)) return cached;

                var layers = HostLayerCache.Get(doc, el);
                if (layers == null)
                {
                    // A ceiling drawn from a plain type is the likeliest reason
                    // for an empty result, and it is a fact about the MODEL. It
                    // gets its own counter so the export can say so instead of
                    // reporting nothing and leaving it to be guessed at.
                    CeilingScan.Tally.TypesWithoutCompoundStructure++;
                    CeilingScan.Store(key, default(CeilingHit));
                    return default(CeilingHit);
                }

                CeilingScan.Tally.TypesInspected++;
                var hit = new CeilingHit();
                bool anyFinishLayer = false;
                foreach (var layer in layers)
                {
                    if (layer.Function != MaterialFunctionAssignment.Finish1
                     && layer.Function != MaterialFunctionAssignment.Finish2) continue;
                    anyFinishLayer = true;
                    if (string.IsNullOrEmpty(layer.MaterialName)) continue;

                    if (StingTools.Core.MaterialSchedule.FinishTextClassifier.IsCeilingBoard(layer.MaterialName))
                    {
                        if (string.IsNullOrEmpty(hit.BoardLabel)) hit.BoardLabel = layer.MaterialName;
                        continue;
                    }
                    if (StingTools.Core.MaterialSchedule.FinishTextClassifier.IsCeilingPlaster(layer.MaterialName))
                    {
                        if (string.IsNullOrEmpty(hit.PlasterLabel))
                        {
                            hit.PlasterLabel = layer.MaterialName;
                            hit.PlasterThicknessM = layer.ThicknessM;
                        }
                        continue;
                    }
                    // Recorded, not absorbed. A mineral-fibre tile or a PVC
                    // ceiling is a real product bought by the tile or the length;
                    // converting it at 2.88 m2 a sheet would be wrong in both the
                    // count and the rate.
                    CeilingScan.Tally.RejectedMaterials.Add(layer.MaterialName);
                }

                if (anyFinishLayer) CeilingScan.Tally.TypesWithFinishLayer++;
                if (!string.IsNullOrEmpty(hit.BoardLabel)) CeilingScan.Tally.TypesWithBoard++;
                if (!string.IsNullOrEmpty(hit.PlasterLabel)) CeilingScan.Tally.TypesWithPlaster++;

                CeilingScan.Store(key, hit);
                return hit;
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("CeilingFinish", $"ReadCeilingFinish {el?.Id}: {ex.Message}");
                return default(CeilingHit);
            }
        }

        /// <summary>
        /// Furring metres per m2 of boarded ceiling. RATIO-DERIVED: the model
        /// does not state grid spacing, so this is a practice figure and the
        /// export says so. 0 (missing row) emits no furring at all rather than a
        /// default quantity.
        /// </summary>
        internal static double CeilingFurringRatio()
            => MaterialLookupCsv.GetProperty("CEILING DEFAULT", "FURRING_M_PER_M2");

        // -- Ceiling (MATSCHED-T2) -------------------------------------------
        private static List<BOQLineItem> BuildCeiling(Document doc, Element el,
            Dictionary<string, (double rate, string unit)> csvRates)
        {
            // Scan FIRST, so the type tally is honest even for an instance whose
            // area cannot be read: those are different failures with different fixes.
            var found = ReadCeilingFinish(doc, el);
            if (!found.Any) return null;   // nothing recognised -> composite fallback

            double areaM2 = ReadAreaM2(el);
            if (areaM2 <= 0)
            {
                CeilingScan.Tally.InstancesWithNoArea++;
                return null;
            }

            double furring = string.IsNullOrEmpty(found.BoardLabel) ? 0 : CeilingFurringRatio();
            if (furring > 0) CeilingScan.Tally.FurringDerived = true;

            // The wet-coat mix reuses the PLASTER rows, exactly as the wall path
            // does. The THICKNESS does not: the ceiling layer states its own, and
            // falling back to a table default would invent a coat.
            var lines = CompoundTakeoff.Ceiling(new CeilingInput
            {
                AreaM2 = areaM2,
                BoardLabel = found.BoardLabel,
                FurringMPerM2 = furring,
                PlasterLabel = found.PlasterLabel,
                PlasterThicknessM = found.PlasterThicknessM,
                PlasterCementBagsPerM3 = Prop("PLASTER STANDARD", "MIX_CEMENT_BAGS_PER_M3", "PLASTER DEFAULT"),
                PlasterSandRatio = Prop("PLASTER STANDARD", "MIX_SAND_RATIO", "PLASTER DEFAULT")
            });

            if (lines.Count == 0) return null;
            return Materialise(doc, el, lines, csvRates, "A", new Resolution());
        }

        /// <summary>Tiled finish layers on a host type: how many, and named by the first.</summary>
        private static (int Faces, string Label) ReadTiledFinish(Document doc, Element el)
        {
            try
            {
                var typeId = el?.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId) return (0, "");
                long key = typeId.Value;
                if (TileFinishScan.TryGet(key, out var cached)) return cached;

                var layers = HostLayerCache.Get(doc, el);
                if (layers == null) { TileFinishScan.Store(key, (0, "")); return (0, ""); }

                TileFinishScan.Tally.TypesInspected++;
                int faces = 0; string label = ""; bool anyFinishLayer = false;
                foreach (var layer in layers)
                {
                    // Finish layers only. A tile is never Structure or Substrate,
                    // and admitting those would count a screed as tiling.
                    if (layer.Function != MaterialFunctionAssignment.Finish1
                     && layer.Function != MaterialFunctionAssignment.Finish2) continue;
                    anyFinishLayer = true;
                    if (string.IsNullOrEmpty(layer.MaterialName)) continue;
                    if (!StingTools.Core.MaterialSchedule.FinishTextClassifier.IsTile(layer.MaterialName))
                    {
                        // Recorded, not discarded: if the pattern is the thing
                        // that is wrong, these names are the evidence for it.
                        TileFinishScan.Tally.RejectedMaterials.Add(layer.MaterialName);
                        continue;
                    }

                    faces++;
                    if (label.Length == 0) label = layer.MaterialName;
                }
                if (anyFinishLayer) TileFinishScan.Tally.TypesWithFinishLayer++;
                if (faces > 0) TileFinishScan.Tally.TypesMatched++;

                var hit = (faces, label);
                TileFinishScan.Store(key, hit);
                return hit;
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("TileFinish", $"ReadTiledFinish {el?.Id}: {ex.Message}");
                return (0, "");
            }
        }

        /// <summary>
        /// Tiling constituents for a host, or an empty list when it carries no
        /// tiled finish layer. <paramref name="faces"/> caps how many of the
        /// detected layers are measured — a floor is tiled on top only, however
        /// many finish layers its type declares.
        /// </summary>
        private static List<CompoundLine> TilingConstituents(Document doc, Element el,
            double areaM2, bool isWall, int faces)
        {
            var empty = new List<CompoundLine>();
            if (areaM2 <= 0) return empty;

            var found = ReadTiledFinish(doc, el);
            if (found.Faces <= 0) return empty;

            int measured = Math.Min(found.Faces, Math.Max(0, faces));
            if (measured <= 0) return empty;

            var cover = TileCoverages(found.Label);
            return CompoundTakeoff.TiledFinish(new TiledFinishInput
            {
                AreaM2 = areaM2 * measured,
                IsWall = isWall,
                TileLabel = found.Label,
                AdhesiveKgPerM2 = cover.AdhesiveKgPerM2,
                GroutKgPerM2 = cover.GroutKgPerM2
            });
        }


        /// <summary>
        /// Tile coverages for a finish name, from MATERIAL_LOOKUP. Internal so
        /// the ROOM-driven source reads the same rows through the same key
        /// composition — two lookups spelling the key differently is exactly the
        /// silent-zero failure the tiling data test was written for.
        /// </summary>
        internal static (double AdhesiveKgPerM2, double GroutKgPerM2) TileCoverages(string finishName)
        {
            string key = StingTools.Core.MaterialSchedule.FinishTextClassifier.TileKey(finishName);
            return (Prop($"TILE {key}", "ADHESIVE_KG_PER_M2", "TILE DEFAULT"),
                    Prop($"TILE {key}", "GROUT_KG_PER_M2", "TILE DEFAULT"));
        }

        private static double Prop(string key, string property, string fallbackKey)
        {
            double v = MaterialLookupCsv.GetProperty(key, property);
            if (v != 0) return v;
            return MaterialLookupCsv.GetProperty(fallbackKey, property);
        }

        // RC-1 — canonicalise the (already-canonicalised) param value, else try a
        // type/family inference, else fall to the project default AND flag it.
        private static string InferOrCanon(string what, string canon, Func<string> infer,
            Resolution res, string fallback)
        {
            if (!string.IsNullOrEmpty(canon)) return canon;   // param provided
            string inferred = null;
            try { inferred = infer?.Invoke(); } catch { }
            if (!string.IsNullOrWhiteSpace(inferred)) return inferred;   // genuine inference
            res.Empty.Add(what);   // truly unset → project default, flagged
            return fallback;
        }

        // RC-1 — resolve a ratio; when the composed key MISSES the table (a value
        // was set but didn't match after normalisation — likely a typo) flag it
        // Unmatched and fall to the DEFAULT row.
        private static double Resolve(Resolution res, string what, string exactKey,
            string property, string defaultKey)
        {
            double v = MaterialLookupCsv.GetProperty(exactKey, property);
            if (v != 0) return v;
            if (!res.Empty.Contains(what)) res.Unmatched.Add($"{what}='{exactKey}'");
            return MaterialLookupCsv.GetProperty(defaultKey, property);
        }

        // Infer the block size from the type name (e.g. "390x190 Block" → "390x190").
        /// <summary>
        /// Decide brick vs block from DATA, not from a substring of the material name.
        /// <para>
        /// The old test was <c>material.Contains("brick")</c>, so "Brick-faced blockwork"
        /// took the brick branch and was measured with brick bond ratios — 2.27x the
        /// block figure on 200mm work, with nothing flagged.
        /// </para>
        /// <para>
        /// The proposed fix — "presence of a bond type is the brick signal" — does NOT
        /// hold, and was checked before implementing: <see cref="InferBrickBond"/> exists
        /// precisely because a genuine brick wall may carry no
        /// <c>BLE_BRICK_BOND_TYPE_TXT</c> and resolve its bond from the type name. A
        /// bond-presence test would misclassify every such wall as block.
        /// </para>
        /// <para>
        /// So the order is BLOCK-evidence first. A block size ("440x215") is unambiguous
        /// and a brick wall never carries one, which makes it the reliable discriminator;
        /// "Brick-faced blockwork" carries a block size and now takes the block branch.
        /// The material name survives only as the last resort, where no dimensional
        /// evidence exists either way.
        /// </para>
        /// </summary>
        private static bool IsBrickWall(Document doc, Element el, string materialLower)
        {
            // Gather the evidence here (Revit-facing), decide in MasonryClassifier
            // (Revit-free, so the decision is unit-testable).
            string blockSize = MaterialKeyCanonicaliser.BlockSize(
                ParameterHelpers.GetString(el, "BLE_BLOCK_SIZE_TXT"));
            if (string.IsNullOrWhiteSpace(blockSize)) blockSize = InferBlockSize(doc, el);

            string bond = MaterialKeyCanonicaliser.BrickBond(
                ParameterHelpers.GetString(el, "BLE_BRICK_BOND_TYPE_TXT"));
            if (string.IsNullOrWhiteSpace(bond)) bond = InferBrickBond(doc, el);

            return MasonryClassifier.IsBrick(blockSize, bond, materialLower);
        }

        private static string InferBlockSize(Document doc, Element el)
        {
            string n = TypeAndName(doc, el);
            string canon = MaterialKeyCanonicaliser.BlockSize(n);
            return System.Text.RegularExpressions.Regex.IsMatch(canon, @"^\d+x\d+$") ? canon : null;
        }

        // Infer the brick bond from type-name keywords.
        private static string InferBrickBond(Document doc, Element el)
        {
            string n = (TypeAndName(doc, el) ?? "").ToUpperInvariant();
            if (n.Contains("FLEMISH")) return "FLEMISH";
            if (n.Contains("ENGLISH GARDEN") || n.Contains("GARDEN WALL")) return "ENGLISH_GARDEN_WALL";
            if (n.Contains("ENGLISH")) return "ENGLISH";
            if (n.Contains("HEADER")) return "HEADER";
            if (n.Contains("STACK")) return "STACK";
            if (n.Contains("STRETCHER") || n.Contains("RUNNING")) return "STRETCHER";
            return null;
        }

        private static string TypeAndName(Document doc, Element el)
        {
            try
            {
                var typeId = el.GetTypeId();
                string typeName = (typeId != null && typeId != ElementId.InvalidElementId)
                    ? doc.GetElement(typeId)?.Name : null;
                return string.IsNullOrEmpty(typeName) ? (el.Name ?? "") : $"{typeName} {el.Name}";
            }
            catch { return el.Name ?? ""; }
        }

        private static double PropOr(string key, string property, double def)
        {
            double v = MaterialLookupCsv.GetProperty(key, property);
            return v != 0 ? v : def;
        }

        private static string NonEmpty(string s, string fallback)
            => string.IsNullOrWhiteSpace(s) ? fallback : s.Trim();
    }
}
