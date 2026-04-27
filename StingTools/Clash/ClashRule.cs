// ClashRule.cs — tier-1 deterministic filter rules applied before user sees a clash.
// Each rule either keeps (return null), reclassifies as intentional, or drops as pseudo.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace StingTools.Core.Clash
{
    public enum ClashVerdict { Keep, Intentional, Pseudo }

    public sealed class ClashRuleDefinition
    {
        public string Id;
        public string Description;
        public string FilterA;     // optional
        public string FilterB;     // optional
        // C5: project-tunable predicate inputs loaded from default_clash_rules.json.
        // Predicates that read here fall back to a hardcoded constant when the
        // key is missing so a rule still has sane defaults outside any JSON.
        public Dictionary<string, double> Params = new Dictionary<string, double>();
        public Func<ClashHit, ElementFacts, ElementFacts, ClashRuleDefinition, ClashVerdict> Predicate;
    }

    public sealed class ClashRule
    {
        // C5: Param keys for project-tunable thresholds. Reading from def.Params
        // with a fallback constant lets BuiltIns() ship sane defaults that
        // ClashRuleLibrary.LoadAugmented can override per-project without
        // recompiling. Sites use ParamOr to keep the read concise.
        internal const string PARAM_VOLUME_THRESHOLD_MM3 = "volume_threshold_mm3";
        internal const string PARAM_HOST_DEPTH_FT = "host_depth_ft";

        internal static double ParamOr(ClashRuleDefinition def, string key, double fallback)
        {
            if (def?.Params == null) return fallback;
            return def.Params.TryGetValue(key, out var v) ? v : fallback;
        }

        public static List<ClashRuleDefinition> BuiltIns()
        {
            return new List<ClashRuleDefinition>
            {
                new ClashRuleDefinition {
                    Id = "R001_TESSELLATION_ARTIFACT",
                    Description = "Drop hits with volume below 100 mm^3 (rounding/tessellation artifact)",
                    Params = { [PARAM_VOLUME_THRESHOLD_MM3] = 100.0 },
                    Predicate = (h, a, b, def) => h.VolumeMm3 < (float)ParamOr(def, PARAM_VOLUME_THRESHOLD_MM3, 100.0)
                        ? ClashVerdict.Pseudo : ClashVerdict.Keep
                },
                new ClashRuleDefinition {
                    Id = "R002_SELF_INSULATION",
                    Description = "Duct insulation vs its own duct",
                    FilterA = "Category=OST_DuctInsulations",
                    FilterB = "Category=OST_DuctCurves",
                    Predicate = (h, a, b, def) => ClashVerdict.Intentional
                },
                new ClashRuleDefinition {
                    Id = "R003_SELF_LINING",
                    Description = "Duct lining vs its own duct",
                    FilterA = "Category=OST_DuctLinings",
                    FilterB = "Category=OST_DuctCurves",
                    Predicate = (h, a, b, def) => ClashVerdict.Intentional
                },
                new ClashRuleDefinition {
                    Id = "R004_PIPE_INSULATION_OWN",
                    Description = "Pipe insulation vs its own pipe",
                    FilterA = "Category=OST_PipeInsulations",
                    FilterB = "Category=OST_PipeCurves",
                    Predicate = (h, a, b, def) => ClashVerdict.Intentional
                },
                new ClashRuleDefinition {
                    Id = "R005_SPRINKLER_CEILING_GRID",
                    Description = "Sprinkler head sitting in ceiling grid (expected)",
                    FilterA = "Category=OST_Sprinklers",
                    FilterB = "Category=OST_Ceilings",
                    Params = { [PARAM_HOST_DEPTH_FT] = 0.164 },
                    Predicate = (h, a, b, def) => (h.AabbMax.Z - h.AabbMin.Z) < (float)ParamOr(def, PARAM_HOST_DEPTH_FT, 0.164)
                        ? ClashVerdict.Intentional : ClashVerdict.Keep
                },
                new ClashRuleDefinition {
                    Id = "R006_LIGHT_FIXTURE_CEILING",
                    Description = "Light fixture hosted in ceiling",
                    FilterA = "Category=OST_LightingFixtures",
                    FilterB = "Category=OST_Ceilings",
                    Params = { [PARAM_HOST_DEPTH_FT] = 0.328 },
                    Predicate = (h, a, b, def) => (h.AabbMax.Z - h.AabbMin.Z) < (float)ParamOr(def, PARAM_HOST_DEPTH_FT, 0.328)
                        ? ClashVerdict.Intentional : ClashVerdict.Keep
                },
                new ClashRuleDefinition {
                    Id = "R007_DIFFUSER_CEILING",
                    Description = "Air terminal hosted in ceiling",
                    FilterA = "Category=OST_DuctTerminal",
                    FilterB = "Category=OST_Ceilings",
                    Predicate = (h, a, b, def) => ClashVerdict.Intentional
                },
                new ClashRuleDefinition {
                    Id = "R008_STRUCTURAL_JOINT",
                    Description = "Structural column to structural beam at joint — expected connection",
                    FilterA = "Category=OST_StructuralColumns",
                    FilterB = "Category=OST_StructuralFraming",
                    // rec-23: Threshold bumped 100,000 mm³ (10×10×10 cm cube) to
                    //         5e8 mm³ (500 L ≈ 80×80×80 cm). Real beam-column joints
                    //         routinely run 30-80 cm in each dimension producing
                    //         volumes 27M – 1B mm³; the old 100k threshold fell
                    //         through to Keep for essentially every joint, so
                    //         every joint got flagged as a clash. 500 L catches
                    //         normal joints as Intentional while still flagging
                    //         actual overlaps (e.g. column passing through a
                    //         parallel beam mid-span).
                    // C5: Threshold now read from Params with the rec-23 constant
                    //     as fallback; default_clash_rules.json may override
                    //     per project.
                    Params = { [PARAM_VOLUME_THRESHOLD_MM3] = 5e8 },
                    Predicate = (h, a, b, def) => h.VolumeMm3 < (float)ParamOr(def, PARAM_VOLUME_THRESHOLD_MM3, 5e8)
                        ? ClashVerdict.Intentional : ClashVerdict.Keep
                },
                new ClashRuleDefinition {
                    Id = "R009_FLOOR_WALL_JOIN",
                    Description = "Floor meeting wall at slab edge",
                    FilterA = "Category=OST_Floors",
                    FilterB = "Category=OST_Walls",
                    // C5: Threshold now read from Params (default 500000 mm³).
                    Params = { [PARAM_VOLUME_THRESHOLD_MM3] = 500000.0 },
                    Predicate = (h, a, b, def) => h.VolumeMm3 < (float)ParamOr(def, PARAM_VOLUME_THRESHOLD_MM3, 500000.0)
                        ? ClashVerdict.Intentional : ClashVerdict.Keep
                },
                new ClashRuleDefinition {
                    Id = "R010_MULLION_CURTAIN_PANEL",
                    Description = "Curtain mullion meeting its own panel",
                    FilterA = "Category=OST_CurtainWallMullions",
                    FilterB = "Category=OST_CurtainWallPanels",
                    Predicate = (h, a, b, def) => ClashVerdict.Intentional
                },
            };
        }
    }
}
