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
        public Func<ClashHit, ElementFacts, ElementFacts, ClashVerdict> Predicate;
    }

    public sealed class ClashRule
    {
        public static List<ClashRuleDefinition> BuiltIns()
        {
            return new List<ClashRuleDefinition>
            {
                new ClashRuleDefinition {
                    Id = "R001_TESSELLATION_ARTIFACT",
                    Description = "Drop hits with volume below 100 mm^3 (rounding/tessellation artifact)",
                    Predicate = (h, a, b) => h.VolumeMm3 < 100f ? ClashVerdict.Pseudo : ClashVerdict.Keep
                },
                new ClashRuleDefinition {
                    Id = "R002_SELF_INSULATION",
                    Description = "Duct insulation vs its own duct",
                    FilterA = "Category=OST_DuctInsulations",
                    FilterB = "Category=OST_DuctCurves",
                    Predicate = (h, a, b) => ClashVerdict.Intentional
                },
                new ClashRuleDefinition {
                    Id = "R003_SELF_LINING",
                    Description = "Duct lining vs its own duct",
                    FilterA = "Category=OST_DuctLinings",
                    FilterB = "Category=OST_DuctCurves",
                    Predicate = (h, a, b) => ClashVerdict.Intentional
                },
                new ClashRuleDefinition {
                    Id = "R004_PIPE_INSULATION_OWN",
                    Description = "Pipe insulation vs its own pipe",
                    FilterA = "Category=OST_PipeInsulations",
                    FilterB = "Category=OST_PipeCurves",
                    Predicate = (h, a, b) => ClashVerdict.Intentional
                },
                new ClashRuleDefinition {
                    Id = "R005_SPRINKLER_CEILING_GRID",
                    Description = "Sprinkler head sitting in ceiling grid (expected)",
                    FilterA = "Category=OST_Sprinklers",
                    FilterB = "Category=OST_Ceilings",
                    Predicate = (h, a, b) => (h.AabbMax.Z - h.AabbMin.Z) < 0.164f ? ClashVerdict.Intentional : ClashVerdict.Keep
                },
                new ClashRuleDefinition {
                    Id = "R006_LIGHT_FIXTURE_CEILING",
                    Description = "Light fixture hosted in ceiling",
                    FilterA = "Category=OST_LightingFixtures",
                    FilterB = "Category=OST_Ceilings",
                    Predicate = (h, a, b) => (h.AabbMax.Z - h.AabbMin.Z) < 0.328f ? ClashVerdict.Intentional : ClashVerdict.Keep
                },
                new ClashRuleDefinition {
                    Id = "R007_DIFFUSER_CEILING",
                    Description = "Air terminal hosted in ceiling",
                    FilterA = "Category=OST_DuctTerminal",
                    FilterB = "Category=OST_Ceilings",
                    Predicate = (h, a, b) => ClashVerdict.Intentional
                },
                new ClashRuleDefinition {
                    Id = "R008_STRUCTURAL_JOINT",
                    Description = "Structural column to structural beam at joint — expected connection",
                    FilterA = "Category=OST_StructuralColumns",
                    FilterB = "Category=OST_StructuralFraming",
                    Predicate = (h, a, b) => h.VolumeMm3 < 100000f ? ClashVerdict.Intentional : ClashVerdict.Keep
                },
                new ClashRuleDefinition {
                    Id = "R009_FLOOR_WALL_JOIN",
                    Description = "Floor meeting wall at slab edge",
                    FilterA = "Category=OST_Floors",
                    FilterB = "Category=OST_Walls",
                    Predicate = (h, a, b) => h.VolumeMm3 < 500000f ? ClashVerdict.Intentional : ClashVerdict.Keep
                },
                new ClashRuleDefinition {
                    Id = "R010_MULLION_CURTAIN_PANEL",
                    Description = "Curtain mullion meeting its own panel",
                    FilterA = "Category=OST_CurtainWallMullions",
                    FilterB = "Category=OST_CurtainWallPanels",
                    Predicate = (h, a, b) => ClashVerdict.Intentional
                },
            };
        }
    }
}
