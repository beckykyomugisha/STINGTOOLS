// PlacementPackRegistry.cs — which STING_PLACEMENT_RULES.*.json files load where.
//
// Revit-free on purpose: StingTools.Tags.Tests <Compile Include>s this file and
// checks that EVERY STING_PLACEMENT_RULES.*.json shipped in Data/Placement is
// either merged into the fixture-placement run (DisciplinePacks) or listed in
// NotAutoMerged with the reason and its real consumer. Five packs sat unloaded
// for months because nothing noticed a file had been authored but never
// registered; the test makes that state impossible to reach silently.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Placement
{
    public static class PlacementPackRegistry
    {
        /// <summary>The baseline file, always loaded first.</summary>
        public const string BaselineFileName = "STING_PLACEMENT_RULES.json";

        /// <summary>
        /// Packs merged on top of the baseline for every Place Fixtures run, in
        /// order, with the SourcePack tag stamped on their rules.
        /// </summary>
        public static readonly IReadOnlyList<(string FileName, string PackTag)> DisciplinePacks =
            new (string, string)[]
            {
                ("STING_PLACEMENT_RULES.architecture.json", "Architecture"),
                ("STING_PLACEMENT_RULES.mechanical.json", "Mechanical"),
                ("STING_PLACEMENT_RULES.electrical.json", "Electrical"),
                ("STING_PLACEMENT_RULES.healthcare-education.json", "Healthcare-Education"),
                ("STING_PLACEMENT_RULES.toilet-fixtures.json", "Toilet-Fixtures"),  // Phase 177 — full toilet-room fixture coverage
                // These general packs were authored but never registered, so
                // residential lighting (ceiling-pendants → living/dining/bedroom),
                // residential MK sockets/switches, the extended baselines and the
                // accessibility + glazing rules never loaded. Registered so dwelling
                // projects place lights/sockets, not just commercial ones.
                ("STING_PLACEMENT_RULES.ceiling-pendants.json", "Lighting-Pendants"),
                ("STING_PLACEMENT_RULES.mk-electrical.json", "MK-Electrical"),
                ("STING_PLACEMENT_RULES.baseline-extensions.json", "Baseline-Ext"),
                ("STING_PLACEMENT_RULES.baseline-extensions2.json", "Baseline-Ext2"),
                ("STING_PLACEMENT_RULES.accessibility.json", "Accessibility"),
                ("STING_PLACEMENT_RULES.windows-glazing.json", "Windows-Glazing"),
                // HTM 02-01 terminal units. Every rule is name-scoped to clinical
                // rooms (RoomFilter + ExcludeRoomFilter — never a residential
                // Bedroom or a lecture theatre), because BuildingType=Healthcare is
                // only enforced when a building profile is active.
                ("STING_PLACEMENT_RULES.medical-gases.json", "Medical-Gases"),
            };

        /// <summary>
        /// Rule files in Data/Placement that are deliberately NOT merged into the
        /// fixture-placement run. Each entry says why and who reads it instead.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> NotAutoMerged =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Same schema, different engine: WALL_CHASE routing rules for
                // InWallChaseRouter. Consumer: RunWallChaseCommand, which loads this
                // file by name (PlacementRuleLoader.LoadPack). Merging it would hand
                // the fixture engine "Pipes" rules to place as family instances.
                ["STING_PLACEMENT_RULES.in-wall-chase.json"] =
                    "WALL_CHASE routing rules — read by RunWallChaseCommand via PlacementRuleLoader.LoadPack",

                // Routing directives keyed on categories that are already placed
                // (Electrical Fixtures, Plumbing Fixtures, Air Terminals ...). No
                // AnchorType, so each rule would default to ROOM_CENTRE and place an
                // extra fixture of that category in every room. No consumer exists:
                // the engine's RouteAfterPlacement only routes fixtures the SAME rule
                // placed; route existing fixtures with Routing_AutoDrop instead.
                ["STING_PLACEMENT_RULES.routing.json"] =
                    "routing directives, not placements — no consumer; Routing_AutoDrop covers existing fixtures",

                // Commissioning points are mostly unscoped (no RoomFilter) Specialty
                // Equipment rules, and a VariantHint miss falls back to the first
                // Specialty Equipment family in the project — merged, it would put a
                // random family in every room. Several DependsOn targets
                // (duct-run, elec-equipment-db) exist in no pack. No consumer.
                ["STING_PLACEMENT_RULES.commissioning.json"] =
                    "unscoped commissioning catalogue with dangling DependsOn — no consumer until scoped",

                // First-fix conduit boxes (TwoPhaseEnabled, RoomFilter ".*"). Merging
                // would turn every Place run into a two-phase construction run.
                // Consumer: TwoPhaseBoxPlacer, via a project override
                // (STING_PLACEMENT_RULES.project.json next to the .rvt) on projects
                // that opt into two-phase conduiting.
                ["STING_PLACEMENT_RULES.conduiting-phase.json"] =
                    "opt-in two-phase first-fix rules — TwoPhaseBoxPlacer, via a project override",
            };

        // D1: test-oracle - StingTools.Tags.Tests/PlacementPackRegistryTests.cs
        /// <summary>True when the file is a discipline pack merged into every run.</summary>
        public static bool IsAutoMerged(string fileName) =>
            DisciplinePacks.Any(p => string.Equals(p.FileName, fileName, StringComparison.OrdinalIgnoreCase));
    }
}
