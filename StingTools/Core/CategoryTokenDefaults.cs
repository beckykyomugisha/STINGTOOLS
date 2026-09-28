// CategoryTokenDefaults.cs — the category-level token rules that the tagger, the
// populator and the validator must all agree on, kept Revit-free so
// StingTools.Tags.Tests can <Compile Include> it and check every category.
//
// WHY THIS EXISTS (2026-09-27)
//
// GetSysCode returned the FIRST system whose SysMap list named the category, in
// declaration order. LPS is declared before ARC / STR / GEN because Walls, Roofs,
// foundations, rebar and Generic Models may ACT as lightning protection — so every
// wall, roof, foundation and generic model in every project was tagged SYS=LPS by
// default. Membership in a system's list says a category MAY belong to it; the
// category's own discipline says what it USUALLY is.

using System;
using System.Collections.Generic;

namespace StingTools.Core
{
    public static class CategoryTokenDefaults
    {
        /// <summary>
        /// Categories whose DISC follows the piping system they carry (a DCW pipe is
        /// Plumbing, a sprinkler main is Fire Protection). Pipe Insulation and
        /// fabrication pipework carry the system of the pipe they belong to.
        /// </summary>
        public static readonly HashSet<string> PipeCategories = new HashSet<string>(StringComparer.Ordinal)
        {
            "Pipes", "Pipe Fittings", "Pipe Accessories", "Flex Pipes",
            "Pipe Insulation", "MEP Fabrication Pipework",
        };

        /// <summary>
        /// Guaranteed SYS for a discipline: M→HVAC, E→LV, P→DCW, A→ARC, S→STR,
        /// FP→FP, LV→LV, anything else→GEN.
        /// </summary>
        public static string DisciplineDefaultSys(string disc)
        {
            switch (disc)
            {
                case "M":  return "HVAC";
                case "E":  return "LV";
                case "P":  return "DCW"; // Cold water is more prevalent than DHW for unconnected pipes
                case "A":  return "ARC";
                case "S":  return "STR";
                case "FP": return "FP";
                case "LV": return "LV";
                default:   return "GEN";
            }
        }

        /// <summary>
        /// The category-fallback SYS: the discipline's default when the category is
        /// listed under it, otherwise the first listed system. Order in SysMap no
        /// longer decides a wall's system.
        /// </summary>
        public static string ChooseCategorySys(IReadOnlyList<string> candidates, string disc)
        {
            if (candidates == null || candidates.Count == 0) return string.Empty;
            string preferred = DisciplineDefaultSys(disc);
            foreach (string c in candidates)
                if (string.Equals(c, preferred, StringComparison.Ordinal)) return c;
            return candidates[0];
        }

        /// <summary>
        /// DISC corrected by the system a pipe-type element carries.
        /// Domestic services (DCW / DHW / SAN / RWD / GAS) are Plumbing; FP is Fire
        /// Protection; HVAC and HWS (the heating-water system, LTHW / MTHW — see
        /// ISO19650Validator and FuncMap HWS→HTG) are Mechanical. HWS used to map to P,
        /// which filed every heating pipe under Plumbing.
        /// </summary>
        public static string SystemAwareDisc(string disc, string sys, string categoryName)
        {
            if (categoryName == null || !PipeCategories.Contains(categoryName))
                return disc;
            switch (sys)
            {
                case "DCW":
                case "DHW":
                case "SAN":
                case "RWD":
                case "GAS":
                case "MGS":   // medical gas pipework is Plumbing, as STING_FUNC_SYS_MATRIX files it
                case "SWD":
                case "GWR":
                case "RWH":
                case "SDS":
                case "SEP":
                case "STW":
                case "BGD":
                case "SPH":
                case "INT":
                case "POL":
                case "LBW":
                case "IRR":
                    return "P";
                case "FP":
                    return "FP";
                case "HVAC":
                case "HWS":
                case "CHW":
                case "CDW":
                case "REF":
                case "CMP":
                case "FOL":
                case "STM":
                case "CON":
                case "CHE":
                    return "M";
                default:
                    return disc;
            }
        }

        /// <summary>
        /// The PROD codes each DISCIPLINE can legitimately carry, built from the same data
        /// the resolver uses: the category defaults (ProdMap) and the family rules
        /// (STING_PROD_CODES.csv), filed under each category's DISC — and, for pipe-type
        /// categories, under P and FP too, since their DISC follows the system.
        ///
        /// <para>The validator's hand-written ProdCodesByDisc list disagreed with the
        /// resolver: a Pipe with no detected system is DISC M with PROD PP, a Mechanical
        /// Equipment rule emits PAC / SPT / MCP, and every one of those was reported as
        /// "belongs to another discipline" although the plugin itself wrote it.</para>
        /// </summary>
        public static Dictionary<string, HashSet<string>> ProdVocabularyByDiscipline(
            IReadOnlyDictionary<string, string> discMap,
            IReadOnlyDictionary<string, string> prodMap,
            IEnumerable<KeyValuePair<string, string>> categoryRuleCodes)
        {
            var byDisc = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            void Add(string category, string code)
            {
                if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(code) || discMap == null) return;
                if (!discMap.TryGetValue(category, out string disc) || string.IsNullOrEmpty(disc)) return;
                foreach (string d in PipeCategories.Contains(category) ? new[] { disc, "P", "FP" } : new[] { disc })
                {
                    if (!byDisc.TryGetValue(d, out var set))
                        byDisc[d] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    set.Add(code);
                }
            }
            if (prodMap != null)
                foreach (var kv in prodMap) Add(kv.Key, kv.Value);
            if (categoryRuleCodes != null)
                foreach (var kv in categoryRuleCodes) Add(kv.Key, kv.Value);
            return byDisc;
        }
    }
}
