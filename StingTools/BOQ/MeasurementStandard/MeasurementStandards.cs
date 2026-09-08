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
using StingTools.Core;

namespace StingTools.BOQ.MeasurementStandard
{
    // ──────────────────────────────────────────────────────────────────
    //  Nrm2Standard — preserves existing BOQ behaviour as a strategy.
    // ──────────────────────────────────────────────────────────────────
    internal sealed class Nrm2Standard : IMeasurementStandard
    {
        /// <inheritdoc/>
        public bool AppliesDeductions => true;

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

        // Phase 2A — proper NRM2 rules-based deductions. Reads the per-category
        // measurement rule (Wall.FindInserts openings net of the de-minimis,
        // etc.) from MeasurementRuleRegistry; returns the NET-of-deductions
        // quantity. Wastage is applied separately + visibly by the cost manager.
        public double ApplyDeductions(BOQLineItem line, Element el)
        {
            double gross = line?.Quantity ?? 0;
            if (line == null || el == null || el.Document == null) return gross;
            try
            {
                var reg = MeasurementRuleRegistry.Get(el.Document, Id);
                var rule = reg.Match(line.Category, line.Discipline, null);
                if (rule == null) return gross;
                return MeasurementDeductionEngine.ApplyDeductions(el, line.Unit, gross, rule, reg.Defaults);
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("Nrm2Deduct", $"Nrm2 ApplyDeductions: {ex.Message}");
                return gross;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // ──────────────────────────────────────────────────────────────────
    //  CrossWalk — section code → another standard's class.
    // ──────────────────────────────────────────────────────────────────
    internal static class CrossWalk
    {
        /// <summary>
        /// Look a section code up in a cross-walk, and SAY SO when it is not there.
        ///
        /// <para>Both cross-walks used a `_ => "Z"` default, so a section code the map
        /// did not know became Miscellaneous with nothing logged. An unmapped code and a
        /// deliberately-miscellaneous one produced byte-identical output, which is why
        /// nine unmapped sections went unnoticed: the bill looked complete. The fallback
        /// still returns the miscellaneous class — refusing to classify would fail a bill
        /// that is merely imprecise — but it now names the code once per session, so the
        /// gap is visible to whoever issues under that standard.</para>
        /// </summary>
        internal static string Classify(string code, IReadOnlyDictionary<string, string> map,
                                        string standard, string fallback)
        {
            string c = (code ?? "").Trim();
            if (c.Length == 0) return fallback;
            if (map.TryGetValue(c, out string cls)) return cls;

            StingLog.WarnRateLimited(
                "CrossWalk_" + standard + "_" + c,
                $"{standard}: work section '{c}' has no class in the cross-walk — billed as " +
                $"'{fallback}'. Add it to the map in MeasurementStandards.cs; a section that " +
                "falls through collapses a whole trade into miscellaneous.");
            return fallback;
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Cesmm4Standard — civil engineering. Section/group/sub-group lattice.
    // ──────────────────────────────────────────────────────────────────
    internal sealed class Cesmm4Standard : IMeasurementStandard
    {
        /// <inheritdoc/>
        public bool AppliesDeductions => true;

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
            return CrossWalk.Classify(line?.NRM2Section, Cesmm4ByCode, "CESMM4", "Z");
        }

        /// <summary>
        /// Section code → CESMM4 class. EVERY code the section vocabulary defines must
        /// appear here, including the ones whose honest answer is Z — otherwise a section
        /// added to the vocabulary falls through the old `_ => "Z"` default and a whole
        /// trade collapses into Miscellaneous with nothing said. That had already
        /// happened: nine defined sections were unmapped, so a bill issued under CESMM4
        /// billed groundworks, both drainage sections, carpentry, finishes and fittings
        /// as one undifferentiated Z. `Z` written deliberately is a classification;
        /// `Z` reached by omission is a silent failure that looks identical.
        /// </summary>
        private static readonly Dictionary<string, string> Cesmm4ByCode =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["1"]  = "D",   // Demolition and site clearance
            ["2"]  = "E",   // Earthworks
            ["3"]  = "E",   // Earthworks
            ["4"]  = "E",   // Earthworks — see the piling caveat below
            ["5"]  = "F",   // In-situ concrete
            ["14"] = "U",   // Brickwork, blockwork and masonry
            ["15"] = "M",   // Structural metalwork
            ["16"] = "O",   // Timber
            ["17"] = "U",   // External walls / cladding
            ["18"] = "W",   // Waterproofing
            ["19"] = "Z",   // Simple building works incidental to civil engineering
            ["20"] = "Z",   // ditto
            ["21"] = "Z",   // ditto
            ["22"] = "Z",   // ditto
            ["23"] = "Z",   // ditto
            ["30"] = "I",   // Pipework — pipes
            ["31"] = "I",   // Pipework — pipes (manholes are Class K; see caveat)
            ["32"] = "I",   // Pipework — pipes
            ["33"] = "I",   // Pipework — pipes
            ["34"] = "X",   // Miscellaneous work (electrical has no CESMM4 class)
            ["35"] = "X",   // ditto
            ["36"] = "X",   // ditto
            ["40"] = "R",   // Roads and pavings
            ["41"] = "X",   // Miscellaneous work — fences, gates and stiles
            ["42"] = "E",   // Earthworks — topsoiling, seeding and turfing
        };

        // Two caveats a bill issued under CESMM4 must carry, because they cannot be
        // resolved by a per-SECTION map:
        //
        //   * Piling. Section 4 holds piled and non-piled foundations alike, and CESMM4
        //     separates them — Classes P and Q are Piles and Piling ancillaries, while
        //     pad and strip footings are Earthworks/In-situ concrete. Distinguishing
        //     them needs the row's CSI section, not its work section.
        //   * Manholes and drainage structures are CESMM4 Class K, not I. Section 31
        //     holds both the runs and the structures.
        //
        // Both are under-classification (a defensible parent class), not misdirection,
        // and neither existed before this map used sections 4 and 31 at all.

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

        // CESMM4: deduct openings > 0.5 m² from wall areas (Class U §3). Phase 2A
        // routes the geometry through the shared MeasurementDeductionEngine with
        // the CESMM4 0.5 m² threshold pinned, reusing the universal category
        // measurement rules (the engine resolves Wall.FindInserts openings).
        public double ApplyDeductions(BOQLineItem line, Element el)
        {
            double gross = line?.Quantity ?? 0;
            if (line == null || el == null || el.Document == null) return gross;
            try
            {
                var reg = MeasurementRuleRegistry.Get(el.Document, Id);
                var rule = reg.Match(line.Category, line.Discipline, null);
                if (rule == null) return gross;
                // Class U §3 — fixed 0.5 m² de-minimis for wall openings.
                return MeasurementDeductionEngine.ApplyDeductions(
                    el, line.Unit, gross, rule, reg.Defaults, thresholdOverrideM2: 0.5);
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("Cesmm4Deduct", $"CESMM4 ApplyDeductions: {ex.Message}");
                return gross;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  PomiStandard — international. Broad classes, light grammar.
    // ──────────────────────────────────────────────────────────────────
    internal sealed class PomiStandard : IMeasurementStandard
    {
        /// <inheritdoc/>
        public bool AppliesDeductions => false;

        public string Id => "pomi";
        public string Version => "RICS POMI (2014)";
        public string DisplayName => "POMI (International)";

        public string PreferredUnit(string categoryName) =>
            new Nrm2Standard().PreferredUnit(categoryName);

        public string ClassifyRow(BOQLineItem line, Element el)
        {
            return CrossWalk.Classify(line?.NRM2Section, PomiByCode, "POMI", "Z");
        }

        /// <summary>
        /// Section code → POMI trade class. Same completeness rule as the CESMM4 map:
        /// every defined section appears, so a new one cannot fall through unnoticed.
        /// Class H is an addition — the lettering had no external-works class at all,
        /// which is why roads, fencing and landscaping had nowhere to go but Z.
        /// </summary>
        private static readonly Dictionary<string, string> PomiByCode =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["1"]  = "Z",   // Demolition — no POMI class in this lettering
            ["2"]  = "A",   // Substructure
            ["3"]  = "A",   // Groundworks are substructure works
            ["4"]  = "A",   // Substructure
            ["5"]  = "A",   // Substructure
            ["14"] = "B",   // Frame / walls
            ["15"] = "B",   // Frame / walls
            ["16"] = "B",   // Carpentry — frame
            ["17"] = "C",   // Roof / external envelope
            ["18"] = "C",   // Waterproofing — envelope
            ["19"] = "B",   // Linings and partitions
            ["20"] = "D",   // Doors / windows / stairs
            ["21"] = "Z",   // Surface finishes — no class in this lettering
            ["22"] = "Z",   // Furniture, fittings and equipment — ditto
            ["23"] = "Z",   // Building fabric sundries — ditto
            ["30"] = "E",   // Drainage above ground — plumbing
            ["31"] = "E",   // Drainage below ground — plumbing
            ["32"] = "E",   // Mechanical / plumbing
            ["33"] = "E",   // Mechanical / plumbing
            ["34"] = "F",   // Electrical
            ["35"] = "F",   // Electrical
            ["36"] = "G",   // Fire / life safety
            ["40"] = "H",   // External works
            ["41"] = "H",   // External works
            ["42"] = "H",   // External works
        };

        public string BuildDescription(BOQLineItem line, Element el)
            => $"{line?.Category ?? "item"}, complete";

        /// <summary>Returns the quantity UNCHANGED — this standard re-classifies and
        /// re-describes rows, it does not re-measure them. See
        /// <see cref="IMeasurementStandard.ApplyDeductions"/>: only NRM2 and CESMM4
        /// currently deduct. Selecting this standard does not apply its deduction rules
        /// to the areas and volumes in the bill.</summary>
        public double ApplyDeductions(BOQLineItem line, Element el)
            => line?.Quantity ?? 0;
    }

    // ──────────────────────────────────────────────────────────────────
    //  Icms3Standard — single ledger for cost + carbon.
    // ──────────────────────────────────────────────────────────────────
    internal sealed class Icms3Standard : IMeasurementStandard
    {
        /// <inheritdoc/>
        public bool AppliesDeductions => false;

        public string Id => "icms3";
        public string Version => "ICMS 3rd ed. (2021)";
        public string DisplayName => "ICMS 3 (cost + carbon)";

        public string PreferredUnit(string categoryName) =>
            new Nrm2Standard().PreferredUnit(categoryName);

        public string ClassifyRow(BOQLineItem line, Element el)
        {
            // ICMS3 group codes (lifecycle phases):
            //   01  Acquisition   02  Construction   03  Operation   04  End-of-life
            //
            // Phase 184l: classification is driven by
            // Data/STING_ICMS3_PHASE_MAP.json (multi-language keyword
            // dictionary) loaded via Icms3PhaseMap.Get(doc). Falls back
            // to "02 Construction" when no group matches.
            if (el == null || el.Document == null) return "02";
            try
            {
                string createdName = ResolvePhaseName(el, BuiltInParameter.PHASE_CREATED);
                string demoName = ResolvePhaseName(el, BuiltInParameter.PHASE_DEMOLISHED);
                bool isDemolished = !string.IsNullOrEmpty(demoName);
                var map = Icms3PhaseMap.Get(el.Document);
                return map.Classify(createdName, demoName, isDemolished);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Icms3Standard.ClassifyRow: {ex.Message}");
                return "02";
            }
        }

        private static string ResolvePhaseName(Element el, BuiltInParameter bip)
        {
            try
            {
                var p = el.get_Parameter(bip);
                if (p == null || !p.HasValue) return "";
                var pid = p.AsElementId();
                if (pid == null || pid.Value <= 0) return "";
                return (el.Document?.GetElement(pid) as Phase)?.Name ?? "";
            }
            catch { return ""; }
        }

        public string BuildDescription(BOQLineItem line, Element el)
        {
            string co2 = line != null && line.EmbodiedCarbonKg > 0
                ? $"; {line.EmbodiedCarbonKg:F0} kgCO₂e"
                : "";
            return $"{line?.Category ?? "item"}, ICMS3 group 02{co2}";
        }

        /// <summary>Returns the quantity UNCHANGED — this standard re-classifies and
        /// re-describes rows, it does not re-measure them. See
        /// <see cref="IMeasurementStandard.ApplyDeductions"/>: only NRM2 and CESMM4
        /// currently deduct. Selecting this standard does not apply its deduction rules
        /// to the areas and volumes in the bill.</summary>
        public double ApplyDeductions(BOQLineItem line, Element el)
            => line?.Quantity ?? 0;
    }

    // ──────────────────────────────────────────────────────────────────
    //  MmhwStandard — UK highway works.
    // ──────────────────────────────────────────────────────────────────
    internal sealed class MmhwStandard : IMeasurementStandard
    {
        /// <inheritdoc/>
        public bool AppliesDeductions => false;

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

        /// <summary>Returns the quantity UNCHANGED — this standard re-classifies and
        /// re-describes rows, it does not re-measure them. See
        /// <see cref="IMeasurementStandard.ApplyDeductions"/>: only NRM2 and CESMM4
        /// currently deduct. Selecting this standard does not apply its deduction rules
        /// to the areas and volumes in the bill.</summary>
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
