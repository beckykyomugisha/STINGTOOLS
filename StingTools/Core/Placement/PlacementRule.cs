// StingTools v4 MVP — fixture placement rule.
//
// A PlacementRule describes one constraint used by FixturePlacementEngine
// to place a fixture family in a room. Rules are loaded from
// Data/Placement/STING_PLACEMENT_RULES.json (default) and merged with
// the per-project override at Data/Placement/STING_PLACEMENT_RULES.project.json
// by PlacementRuleLoader. Project-level rules win on same (CategoryFilter,
// RoomFilter, AnchorType) key.
//
// All millimetre-valued properties are in millimetres; the engine
// converts to Revit internal feet at placement time.

using System.Collections.Generic;

namespace StingTools.Core.Placement
{
    /// <summary>
    /// Single placement rule. Parameterless constructor required for
    /// Newtonsoft.Json deserialisation. Use Clone() to produce a
    /// mutable copy during merge without aliasing rule library state.
    /// </summary>
    public class PlacementRule
    {
        /// <summary>
        /// Revit category this rule applies to, matched against
        /// Element.Category.Name. Wildcards not supported; use one rule
        /// per category.
        /// </summary>
        public string CategoryFilter { get; set; } = "";

        /// <summary>
        /// Room-name regex (case-insensitive). Empty string matches any
        /// room. Regex is pre-compiled by PlacementScorer.
        /// </summary>
        public string RoomFilter { get; set; } = "";

        /// <summary>
        /// Anchor reference (written to ASS_PLACE_ANCHOR_TXT). One of:
        /// DOOR_HINGE, ROOM_CENTRE, WALL_MIDPOINT, CEILING_CENTRE,
        /// WALL_CORNER, DOOR_JAMB, WINDOW_SILL, ROOM_CENTROID.
        /// </summary>
        public string AnchorType { get; set; } = "ROOM_CENTRE";

        /// <summary>
        /// Signed horizontal offset from the anchor in millimetres
        /// (written to ASS_PLACE_OFFSET_X_MM). Positive = anchor's
        /// +X direction. 0 = on anchor.
        /// </summary>
        public double OffsetXMm { get; set; } = 0.0;

        /// <summary>
        /// Mounting height above finished floor in millimetres.
        /// Applied to the placed family's elevation.
        /// </summary>
        public double MountingHeightMm { get; set; } = 300.0;

        /// <summary>
        /// Side constraint (written to ASS_PLACE_SIDE_TXT).
        /// LEFT / RIGHT / EITHER.
        /// </summary>
        public string SideConstraint { get; set; } = "EITHER";

        /// <summary>
        /// Minimum centre-to-centre spacing in millimetres between
        /// fixtures placed by this rule within the same room.
        /// Scorer penalises candidates closer than this.
        /// </summary>
        public double MinSpacingMm { get; set; } = 1000.0;

        /// <summary>
        /// Hard cap on fixtures placed by this rule per room. 0 = no cap.
        /// </summary>
        public int MaxPerRoom { get; set; } = 0;

        /// <summary>
        /// Rule priority; higher wins when two rules produce overlapping
        /// candidates. Ties broken by candidate score.
        /// </summary>
        public int Priority { get; set; } = 50;

        /// <summary>
        /// Free-text note surfaced in the placement result panel.
        /// Not used by the engine directly.
        /// </summary>
        public string Notes { get; set; } = "";

        /// <summary>
        /// Deep-copy the rule so project-level overrides do not mutate
        /// the shared rule library state.
        /// </summary>
        public PlacementRule Clone()
        {
            return new PlacementRule
            {
                CategoryFilter   = this.CategoryFilter,
                RoomFilter       = this.RoomFilter,
                AnchorType       = this.AnchorType,
                OffsetXMm        = this.OffsetXMm,
                MountingHeightMm = this.MountingHeightMm,
                SideConstraint   = this.SideConstraint,
                MinSpacingMm     = this.MinSpacingMm,
                MaxPerRoom       = this.MaxPerRoom,
                Priority         = this.Priority,
                Notes            = this.Notes,
            };
        }

        /// <summary>
        /// Merge-key tuple used by PlacementRuleLoader to deduplicate
        /// project-level overrides against the default library.
        /// </summary>
        public string MergeKey => $"{CategoryFilter}::{RoomFilter}::{AnchorType}";
    }

    /// <summary>
    /// Wrapper used by Newtonsoft.Json to deserialise the top-level
    /// STING_PLACEMENT_RULES.json schema: { "version": "v4", "rules": [...] }.
    /// </summary>
    public class PlacementRuleSet
    {
        public string Version { get; set; } = "v4";
        public List<PlacementRule> Rules { get; set; } = new List<PlacementRule>();
    }
}
