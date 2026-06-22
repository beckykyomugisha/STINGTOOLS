// StingTools — MEP System Filter Generator (Phase D).
//
// Turns the Phase A system-type definitions into AEC filter definitions so the
// SAME single source (STING_MEP_SYSTEM_TYPES.json) drives system types, instance
// names, AND view colours.
//
// Two filter shapes:
//
//   1. Abbreviation-keyed (preferred) — keys on ASS_MEP_SYS_NAME_TXT begins-with
//      "<abbr>-". Phase B stamps that param on every member as "<abbr>-NN", so this
//      DISTINGUISHES services that share one MEPSystemClassification (CHW-flow vs
//      LTHW-flow vs CW-flow are all SupplyHydronic, but CHWF- / LTHWF- / CWF- differ).
//      This is the fix for the Phase C "flow/return ambiguity" caveat.
//
//   2. Classification-synthesised (fallback / auto-author) — keys on
//      RBS_SYSTEM_CLASSIFICATION_PARAM equals "<display>". Used when a classification
//      is present in the model but no corporate AEC filter covers it: the colour
//      comes from the matching Phase A def so every present system always colours.
//
// All generated definitions carry origin="project" and ids prefixed "sting-sys-"
// so they round-trip cleanly through AecFilterRegistry's project-override merge.

using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;

namespace StingTools.Core.Mep
{
    public static class MepSystemFilterGenerator
    {
        public const string IdPrefix    = "sting-sys-";
        public const string SysNameParam = "ASS_MEP_SYS_NAME_TXT"; // == ParamRegistry.MEP_SYS_NAME

        private static readonly string[] DuctCats =
        {
            "OST_DuctCurves", "OST_DuctFitting", "OST_DuctAccessory",
            "OST_DuctTerminal", "OST_FlexDuctCurves", "OST_DuctInsulations"
        };
        private static readonly string[] PipeCats =
        {
            "OST_PipeCurves", "OST_PipeFitting", "OST_PipeAccessory",
            "OST_FlexPipeCurves", "OST_PipeInsulations", "OST_Sprinklers", "OST_PlumbingFixtures"
        };

        /// <summary>
        /// One abbreviation-keyed AEC filter per enabled Phase A definition that has
        /// both an abbreviation and a colour. These are the "STING - Sys: …" filters.
        /// </summary>
        public static List<AecFilterDefinition> Generate(MepSystemTypeRules rules)
        {
            var defs = new List<AecFilterDefinition>();
            if (rules == null) return defs;
            foreach (var d in rules.Enabled)
            {
                if (string.IsNullOrWhiteSpace(d.Abbreviation) || d.LineColor == null) continue;
                if (!d.IsDuct && !d.IsPipe) continue;
                defs.Add(AbbreviationFilter(d));
            }
            return defs;
        }

        /// <summary>Build the abbreviation-keyed filter for a single Phase A def.</summary>
        public static AecFilterDefinition AbbreviationFilter(MepSystemTypeDef d)
        {
            return new AecFilterDefinition
            {
                Id   = IdPrefix + d.Id,
                Name = $"STING - Sys: {d.Name}",
                Origin = "project",
                Categories = (d.IsDuct ? DuctCats : PipeCats).ToList(),
                Rule = new AecFilterRule
                {
                    Param = SysNameParam,
                    Kind  = "shared",
                    Op    = "beginswith",
                    Value = d.Abbreviation + "-"
                },
                DefaultOverride = OverrideFromColor(d.LineColor, d.LineWeight),
                Tags = new List<string> { "mep", d.IsDuct ? "duct" : "pipe", "sting-sys" },
                Standard = "STING",
                Notes = $"Auto-generated from STING_MEP_SYSTEM_TYPES.json '{d.Id}' " +
                        $"(classification {d.Classification}, abbreviation {d.Abbreviation})."
            };
        }

        /// <summary>
        /// Synthesise a classification-keyed filter from the Phase A def whose
        /// classification matches <paramref name="enumClassification"/> (enum name,
        /// e.g. "SupplyAir"). The rule keys on the DISPLAY value
        /// (<paramref name="displayClassification"/>, e.g. "Supply Air") since that
        /// is what RBS_SYSTEM_CLASSIFICATION_PARAM stores. Returns null when no
        /// Phase A def carries that classification (no colour to use).
        /// </summary>
        public static AecFilterDefinition SynthesiseForClassification(
            MepSystemTypeRules rules, string enumClassification, string displayClassification)
        {
            var d = rules?.Enabled.FirstOrDefault(x =>
                string.Equals(x.Classification, enumClassification, StringComparison.OrdinalIgnoreCase)
                && x.LineColor != null);
            if (d == null) return null;

            return new AecFilterDefinition
            {
                Id   = IdPrefix + "cls-" + Sanitise(displayClassification),
                Name = $"STING - Sys (class): {displayClassification}",
                Origin = "project",
                Categories = (d.IsDuct ? DuctCats : PipeCats).ToList(),
                Rule = new AecFilterRule
                {
                    Param = "RBS_SYSTEM_CLASSIFICATION_PARAM",
                    Kind  = "builtin",
                    Op    = "equals",
                    Value = displayClassification
                },
                DefaultOverride = OverrideFromColor(d.LineColor, d.LineWeight),
                Tags = new List<string> { "mep", "sting-sys", "auto" },
                Standard = "STING",
                Notes = $"Auto-authored for present classification '{displayClassification}' " +
                        $"using colour from Phase A def '{d.Id}'."
            };
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private static FilterDefaultOverride OverrideFromColor(int[] rgb, int weight)
        {
            string hex = Hex(rgb);
            var ov = new FilterDefaultOverride
            {
                ProjColor   = hex,
                CutColor    = hex,
                SurfFgColor = hex,
                SurfFgPattern = "Solid fill"
            };
            if (weight >= 1 && weight <= 16) { ov.ProjWeight = weight; ov.CutWeight = weight; }
            return ov;
        }

        private static string Hex(int[] rgb)
        {
            if (rgb == null || rgb.Length < 3) return null;
            int r = Clamp(rgb[0]), g = Clamp(rgb[1]), b = Clamp(rgb[2]);
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        private static int Clamp(int v) => v < 0 ? 0 : (v > 255 ? 255 : v);

        private static string Sanitise(string s)
            => new string((s ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
    }
}
