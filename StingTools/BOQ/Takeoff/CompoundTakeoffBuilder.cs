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
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.BOQ.Takeoff
{
    internal static class CompoundTakeoffBuilder
    {
        /// <summary>Config toggle. Default OFF so legacy composite-rate bills are
        /// untouched until a project opts in.</summary>
        internal static bool Enabled()
        {
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
        {
            if (doc == null || el == null) return null;
            try
            {
                string cat = ParameterHelpers.GetCategoryName(el) ?? "";
                if (cat.IndexOf("Wall", StringComparison.OrdinalIgnoreCase) >= 0)
                    return BuildWall(doc, el, csvRates);
                if (cat.IndexOf("Floor", StringComparison.OrdinalIgnoreCase) >= 0)
                    return BuildRcSlab(doc, el, csvRates);
                // Beams/columns/foundations covered by MAT-4's accurate path.
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
            bool isBrick = material.Contains("brick");
            bool isRc = material.Contains("concrete") || material.Contains("rc") || material.Contains("reinforced");

            // Units per m² + cutting waste + mortar-per-m² from bond/block tables.
            double unitsPerM2, unitWaste, mortarRatio;
            if (isBrick)
            {
                string bond = NonEmpty(ParameterHelpers.GetString(el, "BLE_BRICK_BOND_TYPE_TXT"), "STRETCHER").ToUpperInvariant();
                unitsPerM2 = Prop($"BRICK_BOND {bond}", "BRICKS_PER_M2", "BRICK_BOND DEFAULT");
                unitWaste = Prop($"BRICK_BOND {bond}", "WASTE_PCT", "BRICK_BOND DEFAULT");
                mortarRatio = Prop($"BRICK_BOND {bond}", "MORTAR_RATIO", "BRICK_BOND DEFAULT");
            }
            else
            {
                string size = NonEmpty(ParameterHelpers.GetString(el, "BLE_BLOCK_SIZE_TXT"), "DEFAULT");
                unitsPerM2 = Prop($"BLOCK {size}", "BLOCKS_PER_M2", "BLOCK DEFAULT");
                unitWaste = 5;
                mortarRatio = Prop($"BLOCK {size}", "MORTAR_VOLUME_FACTOR", "BLOCK DEFAULT");
            }

            // Mortar mix (MAT-2 corrected ratios).
            string mix = NonEmpty(ParameterHelpers.GetString(el, "BLE_MORTAR_MIX_TXT"), isBrick ? "1:5" : "1:4");
            double mortarCement = Prop($"MORTAR {mix}", "CEMENT_BAGS_PER_M3", "MORTAR DEFAULT");
            double mortarSand = Prop($"MORTAR {mix}", "SAND_RATIO", "MORTAR DEFAULT");

            // Plaster: faces (default both) + the MAT-2 plaster mix.
            int faces = ParameterHelpers.GetInt(el, "BLE_PLASTER_FACES_NR", 2);
            if (faces < 0) faces = 0; if (faces > 2) faces = 2;
            string plasterType = NonEmpty(ParameterHelpers.GetString(el, "BLE_PLASTER_TYPE_TXT"), "STANDARD").ToUpperInvariant();
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
                IsRcWall = isRc
            };
            var constituents = CompoundTakeoff.MasonryWall(input);
            if (constituents.Count == 0) return null;
            return Materialise(doc, el, constituents, csvRates, isRc ? "S" : "A");
        }

        // ── RC slab (concrete net + rebar + formwork) ───────────────────────
        private static List<BOQLineItem> BuildRcSlab(Document doc, Element el,
            Dictionary<string, (double rate, string unit)> csvRates)
        {
            string material = (GetPrimaryMaterialName(doc, el) ?? "").ToLowerInvariant();
            if (!(material.Contains("concrete") || material.Contains("rc") || material.Length == 0))
                return null;   // non-RC floor (timber deck etc.) → composite fallback

            double grossM3 = ReadVolumeM3(el);
            if (grossM3 <= 0) return null;

            // MAT-1 flat factor (MAT-4 supersedes with the parameter calculator).
            double net = BOQCostManager.ApplySlabVoidFactor(el, grossM3);
            double band = PropOr("REBAR_ELEMENT SLAB", "STEEL_KG_PER_M3", 80);
            double soffit = ReadAreaM2(el);

            var input = new RcElementInput
            {
                ElementKind = "slab",
                ConcreteM3Net = net,
                RebarBandKgPerM3 = band,
                FormworkM2 = soffit
            };
            var constituents = CompoundTakeoff.RcElement(input);
            if (constituents.Count == 0) return null;
            return Materialise(doc, el, constituents, csvRates, "S");
        }

        // ── Materialise constituent lines into BOQLineItems ─────────────────
        private static List<BOQLineItem> Materialise(Document doc, Element el,
            List<CompoundLine> constituents,
            Dictionary<string, (double rate, string unit)> csvRates, string discipline)
        {
            var outList = new List<BOQLineItem>(constituents.Count);
            long elId = el.Id?.Value ?? -1;
            bool firstWriteback = true;
            foreach (var c in constituents)
            {
                (double rate, string source, int conf) = ResolveConstituentRate(csvRates, c);
                var line = new BOQLineItem
                {
                    NRM2Section = c.Nrm2Section,
                    Category = ConstituentCategory(c.Kind),
                    Discipline = discipline,
                    ItemName = c.Description,
                    FamilyName = GetFamilyName(doc, el),
                    TypeName = el.Name ?? "",
                    Quantity = Math.Round(c.Quantity, 3),
                    Unit = c.Unit,
                    GrossQuantity = Math.Round(c.Quantity, 3),
                    RateUGX = rate,
                    RateSource = source,
                    RateConfidence = conf,
                    Source = BOQRowSource.Model,
                    // Only the first (primary) constituent writes back to the element.
                    RevitElementId = firstWriteback ? elId : -1,
                    UniqueId = firstWriteback ? el.UniqueId : "",
                    Note = $"[Compound: {c.Kind}]",
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
                case "plaster_cement": return "Cement";
                case "plaster_sand": return "Sand";
                case "concrete": return "In-situ Concrete";
                case "rebar": return "Reinforcement";
                case "formwork": return "Formwork";
                default: return kind;
            }
        }

        // ── Small Revit helpers ─────────────────────────────────────────────
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

        private static string GetPrimaryMaterialName(Document doc, Element el)
        {
            try
            {
                var ids = el.GetMaterialIds(false);
                if (ids != null)
                    foreach (var id in ids)
                        if (id != null && id.Value > 0)
                            return doc.GetElement(id)?.Name;
            }
            catch (Exception ex) { StingLog.WarnRateLimited("CompoundMat", $"GetPrimaryMaterialName: {ex.Message}"); }
            return null;
        }

        private static string GetFamilyName(Document doc, Element el)
        {
            try { return ParameterHelpers.GetFamilyName(el); } catch { return ""; }
        }

        private static double Prop(string key, string property, string fallbackKey)
        {
            double v = MaterialLookupCsv.GetProperty(key, property);
            if (v != 0) return v;
            return MaterialLookupCsv.GetProperty(fallbackKey, property);
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
