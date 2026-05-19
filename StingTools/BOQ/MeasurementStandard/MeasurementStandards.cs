// ══════════════════════════════════════════════════════════════════════════
//  MeasurementStandards.cs — 5 concrete IMeasurementStandard impls + registry.
//
//  P6 of the Cost Management Implementation Plan.
//
//  Each standard supplies its own section-code grammar, description
//  template grammar and deduction rules. The classifications are
//  authored from the published standards:
//
//    NRM2  — RICS New Rules of Measurement 2 (Building Works), 2nd ed.
//    CESMM4 — Civil Engineering Standard Method of Measurement, 4th ed.
//    POMI  — RICS Principles of Measurement (International), 2014
//    ICMS3 — International Cost Management Standard, 3rd ed.
//    MMHW  — Method of Measurement for Highway Works (Vol 4, MCHW)
//
//  Heavy-lifting (rule engine for deductions, full description grammar)
//  is intentionally minimal here — the interface + 5 implementations
//  unlock multi-standard exports and let project work refine the
//  per-standard grammar against real data.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.BOQ.MeasurementStandard
{
    // ──────────────────────────────────────────────────────────────────
    //  Nrm2Standard — preserves existing BOQ behaviour as a strategy.
    // ──────────────────────────────────────────────────────────────────
    internal sealed class Nrm2Standard : IMeasurementStandard
    {
        public string Id => "nrm2";
        public string Version => "NRM2 (2nd ed., 2012, reprint 2021)";
        public string DisplayName => "RICS NRM2";

        public string PreferredUnit(string categoryName)
        {
            string lower = (categoryName ?? "").ToLowerInvariant();
            if (lower.Contains("wall") || lower.Contains("floor") || lower.Contains("slab")
                || lower.Contains("roof") || lower.Contains("ceiling")) return "m²";
            if (lower.Contains("foundation")) return "m³";
            if (lower.Contains("duct") || lower.Contains("pipe") || lower.Contains("conduit")
                || lower.Contains("cable") || lower.Contains("framing") || lower.Contains("beam"))
                return "m";
            if (lower.Contains("rebar")) return "kg";
            return "each";
        }

        public string ClassifyRow(BOQLineItem line, Element el)
            => string.IsNullOrEmpty(line?.NRM2Section) ? "99" : line.NRM2Section;

        public string BuildDescription(BOQLineItem line, Element el)
            => string.IsNullOrEmpty(line?.ResolvedNRM2Paragraph)
                ? $"Supply and fix {line?.Category?.ToLowerInvariant() ?? "item"}."
                : line.ResolvedNRM2Paragraph;

        public double ApplyDeductions(BOQLineItem line, Element el)
            => line?.Quantity ?? 0;
    }

    // ──────────────────────────────────────────────────────────────────
    //  Cesmm4Standard — civil engineering. Section/group/sub-group lattice.
    // ──────────────────────────────────────────────────────────────────
    internal sealed class Cesmm4Standard : IMeasurementStandard
    {
        public string Id => "cesmm4";
        public string Version => "CESMM4 (2012)";
        public string DisplayName => "CESMM4";

        public string PreferredUnit(string categoryName)
        {
            string lower = (categoryName ?? "").ToLowerInvariant();
            // CESMM4 has stronger conventions per Class.
            if (lower.Contains("foundation"))               return "m³";   // Class E / F
            if (lower.Contains("concrete"))                 return "m³";   // Class F
            if (lower.Contains("formwork"))                 return "m²";   // Class G
            if (lower.Contains("reinforcement") || lower.Contains("rebar"))  return "t";
            if (lower.Contains("structural") || lower.Contains("steel"))     return "t";   // Class M
            if (lower.Contains("brick") || lower.Contains("block"))          return "m²";   // Class U
            if (lower.Contains("pipe") || lower.Contains("drain"))           return "m";   // Class I / J
            if (lower.Contains("road") || lower.Contains("pavement"))        return "m²";   // Class R
            return "each";
        }

        public string ClassifyRow(BOQLineItem line, Element el)
        {
            // CESMM4 classes A-Z. Map NRM2 section to closest CESMM4 class.
            string nrm2 = line?.NRM2Section ?? "";
            return nrm2 switch
            {
                "4"  => "E",      // Earthworks
                "5"  => "F",      // In-situ concrete
                "14" => "U",      // Brickwork, blockwork and masonry
                "15" => "M",      // Structural metalwork
                "17" => "U",      // External walls / cladding
                "20" => "Z",      // Misc work
                "32" => "I",      // Pipework — pipes
                "33" => "I",      // Pipework — pipes
                "34" => "X",      // Miscellaneous (electrical not directly in CESMM4)
                _    => "Z"
            };
        }

        public string BuildDescription(BOQLineItem line, Element el)
        {
            // CESMM4 descriptions follow a strict feature ladder. Stub here
            // with first feature + material; project-override layer can
            // extend.
            string material = ParameterHelpers.GetString(el, "MAT_CODE") ?? "";
            string baseDesc = line?.Category ?? "item";
            return string.IsNullOrEmpty(material)
                ? $"{baseDesc}; as drawn"
                : $"{baseDesc}; {material}; as drawn";
        }

        public double ApplyDeductions(BOQLineItem line, Element el)
        {
            // CESMM4: deduct openings > 0.5 m² from wall areas (Class U §3).
            if (line == null) return 0;
            double q = line.Quantity;
            if (line.Unit == "m²" && line.Category != null &&
                line.Category.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                q -= EstimateLargeOpeningsM2(el);
            }
            return Math.Max(0, q);
        }

        private double EstimateLargeOpeningsM2(Element el)
        {
            // Stub — full deduction algorithm requires opening-host
            // traversal (`Wall.FindInserts`). Project-override hook lives
            // in the cesmm4 standard JSON when authored.
            return 0;
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  PomiStandard — international. Broad classes, light grammar.
    // ──────────────────────────────────────────────────────────────────
    internal sealed class PomiStandard : IMeasurementStandard
    {
        public string Id => "pomi";
        public string Version => "RICS POMI (2014)";
        public string DisplayName => "POMI (International)";

        public string PreferredUnit(string categoryName) =>
            new Nrm2Standard().PreferredUnit(categoryName);

        public string ClassifyRow(BOQLineItem line, Element el)
        {
            // POMI works at the trade level — simpler 1-letter classes.
            string nrm2 = line?.NRM2Section ?? "";
            return nrm2 switch
            {
                "4" or "5"        => "A",      // Substructure
                "14" or "15"      => "B",      // Frame / walls
                "17"              => "C",      // Roof / external envelope
                "20"              => "D",      // Doors / windows / stairs
                "32" or "33"      => "E",      // Mechanical / plumbing
                "34" or "35"      => "F",      // Electrical
                "36"              => "G",      // Fire / life safety
                _                 => "Z"
            };
        }

        public string BuildDescription(BOQLineItem line, Element el)
            => $"{line?.Category ?? "item"}, complete";

        public double ApplyDeductions(BOQLineItem line, Element el)
            => line?.Quantity ?? 0;
    }

    // ──────────────────────────────────────────────────────────────────
    //  Icms3Standard — single ledger for cost + carbon.
    // ──────────────────────────────────────────────────────────────────
    internal sealed class Icms3Standard : IMeasurementStandard
    {
        public string Id => "icms3";
        public string Version => "ICMS 3rd ed. (2021)";
        public string DisplayName => "ICMS 3 (cost + carbon)";

        public string PreferredUnit(string categoryName) =>
            new Nrm2Standard().PreferredUnit(categoryName);

        public string ClassifyRow(BOQLineItem line, Element el)
        {
            // ICMS3 group codes (lifecycle phases):
            //   01  Acquisition           — site purchase, legal, fees
            //   02  Construction          — main capex
            //   03  Operation             — maintenance, replacement, run cost
            //   04  End-of-life           — decommission, disposal
            //
            // Phase 184k refinement: read Revit phase parameters to bucket
            // each line correctly rather than collapsing everything to 02.
            // - PHASE_DEMOLISHED set + phase name contains "demolition" → 04
            // - PHASE_DEMOLISHED set (any other phase)                  → 03
            // - PHASE_CREATED phase name contains "existing" / "site"   → 01
            // - Default                                                  → 02 Construction
            if (el == null) return "02";
            try
            {
                var demoParam = el.get_Parameter(BuiltInParameter.PHASE_DEMOLISHED);
                if (demoParam != null && demoParam.HasValue)
                {
                    var demoId = demoParam.AsElementId();
                    if (demoId != null && demoId.Value > 0)
                    {
                        Phase demoPhase = el.Document?.GetElement(demoId) as Phase;
                        string demoName = (demoPhase?.Name ?? "").ToLowerInvariant();
                        if (demoName.Contains("demolition") || demoName.Contains("end-of-life") ||
                            demoName.Contains("decommission"))
                            return "04";
                        return "03";  // Demolished in a non-demolition phase → operation cycle
                    }
                }

                var createdParam = el.get_Parameter(BuiltInParameter.PHASE_CREATED);
                if (createdParam != null && createdParam.HasValue)
                {
                    var createdId = createdParam.AsElementId();
                    Phase createdPhase = el.Document?.GetElement(createdId) as Phase;
                    string createdName = (createdPhase?.Name ?? "").ToLowerInvariant();
                    if (createdName.Contains("existing") || createdName.Contains("acquisition") ||
                        createdName.Contains("site preparation") || createdName.Contains("enabling"))
                        return "01";
                    if (createdName.Contains("operation") || createdName.Contains("maintenance"))
                        return "03";
                }
            }
            catch (Exception ex) { StingLog.Warn($"Icms3Standard.ClassifyRow: {ex.Message}"); }
            return "02";  // Default: construction
        }

        public string BuildDescription(BOQLineItem line, Element el)
        {
            string co2 = line != null && line.EmbodiedCarbonKg > 0
                ? $"; {line.EmbodiedCarbonKg:F0} kgCO₂e"
                : "";
            return $"{line?.Category ?? "item"}, ICMS3 group 02{co2}";
        }

        public double ApplyDeductions(BOQLineItem line, Element el)
            => line?.Quantity ?? 0;
    }

    // ──────────────────────────────────────────────────────────────────
    //  MmhwStandard — UK highway works.
    // ──────────────────────────────────────────────────────────────────
    internal sealed class MmhwStandard : IMeasurementStandard
    {
        public string Id => "mmhw";
        public string Version => "MMHW (DMRB Vol 4, 2021)";
        public string DisplayName => "MMHW (Highway works)";

        public string PreferredUnit(string categoryName)
        {
            string lower = (categoryName ?? "").ToLowerInvariant();
            if (lower.Contains("road") || lower.Contains("pavement")) return "m²";
            if (lower.Contains("kerb") || lower.Contains("edging"))   return "m";
            if (lower.Contains("drain") || lower.Contains("pipe"))    return "m";
            if (lower.Contains("excavation") || lower.Contains("fill")) return "m³";
            if (lower.Contains("sign") || lower.Contains("light"))    return "each";
            return "each";
        }

        public string ClassifyRow(BOQLineItem line, Element el)
        {
            // MMHW series 100-3000 — top-level 100s. Map common categories.
            string lower = (line?.Category ?? "").ToLowerInvariant();
            if (lower.Contains("excavation") || lower.Contains("fill")) return "600";   // Earthworks
            if (lower.Contains("drain") || lower.Contains("pipe"))      return "500";   // Drainage
            if (lower.Contains("road") || lower.Contains("pavement"))   return "700";   // Road pavements
            if (lower.Contains("kerb"))                                  return "1100";  // Kerbs
            if (lower.Contains("sign") || lower.Contains("marking"))    return "1200";  // Traffic signs
            if (lower.Contains("light"))                                 return "1400";  // Street lighting
            return "3000";  // Misc
        }

        public string BuildDescription(BOQLineItem line, Element el)
            => $"{line?.Category ?? "item"}, in accordance with the Specification";

        public double ApplyDeductions(BOQLineItem line, Element el)
            => line?.Quantity ?? 0;
    }

    // ──────────────────────────────────────────────────────────────────
    //  Registry
    // ──────────────────────────────────────────────────────────────────
    public static class MeasurementStandardRegistry
    {
        private static readonly Dictionary<string, IMeasurementStandard> _byId
            = new Dictionary<string, IMeasurementStandard>(StringComparer.OrdinalIgnoreCase)
            {
                ["nrm2"]   = new Nrm2Standard(),
                ["cesmm4"] = new Cesmm4Standard(),
                ["pomi"]   = new PomiStandard(),
                ["icms3"]  = new Icms3Standard(),
                ["mmhw"]   = new MmhwStandard()
            };

        public static IMeasurementStandard Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return _byId["nrm2"];
            return _byId.TryGetValue(id, out var s) ? s : _byId["nrm2"];
        }

        public static IEnumerable<IMeasurementStandard> All() =>
            _byId.Values.OrderBy(s => s.DisplayName);
    }
}
