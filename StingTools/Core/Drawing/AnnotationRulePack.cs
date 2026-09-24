// StingTools — Drawing Template Manager · Phase 137
//
// AnnotationRulePack is the "what to tag, dimension, and decorate"
// payload referenced by both DrawingType (per-profile rules) and
// ProductionRule (per-view overrides). Lives in its own file rather
// than inside DrawingType so the rule pack — and its supporting
// AutoAnnotationRule / SpotAnnotationRule POCOs — can be edited
// independently of the host DrawingType definition.
//
// Phase 137 additions:
//   * Generic Rules collection replaces 9 hardcoded bool flags
//   * Decorative annotation: north arrow / scale bar / key plan
//   * Matchline auto-placement
//   * Spot-elevation / spot-coordinate rules

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    public sealed class AnnotationRulePack
    {
        // ── Legacy boolean flags (retained for back-compat). MigrateFromLegacy
        //    folds any true value into the Rules collection on first load. ──
        [Obsolete("Use Rules collection instead.")] [JsonProperty("autoDimGrids",    DefaultValueHandling = DefaultValueHandling.Ignore)] public bool AutoDimGrids { get; set; }
        [Obsolete("Use Rules collection instead.")] [JsonProperty("autoDimLevels",   DefaultValueHandling = DefaultValueHandling.Ignore)] public bool AutoDimLevels { get; set; }
        [Obsolete("Use Rules collection instead.")] [JsonProperty("autoTagRooms",    DefaultValueHandling = DefaultValueHandling.Ignore)] public bool AutoTagRooms { get; set; }
        [Obsolete("Use Rules collection instead.")] [JsonProperty("autoTagDoors",    DefaultValueHandling = DefaultValueHandling.Ignore)] public bool AutoTagDoors { get; set; }
        [Obsolete("Use Rules collection instead.")] [JsonProperty("autoTagWindows",  DefaultValueHandling = DefaultValueHandling.Ignore)] public bool AutoTagWindows { get; set; }
        [Obsolete("Use Rules collection instead.")] [JsonProperty("autoTagEquipment",DefaultValueHandling = DefaultValueHandling.Ignore)] public bool AutoTagEquipment { get; set; }
        [Obsolete("Use Rules collection instead.")] [JsonProperty("autoTagWelds",    DefaultValueHandling = DefaultValueHandling.Ignore)] public bool AutoTagWelds { get; set; }
        [Obsolete("Use Rules collection instead.")] [JsonProperty("autoTagSupports", DefaultValueHandling = DefaultValueHandling.Ignore)] public bool AutoTagSupports { get; set; }
        [Obsolete("Use Rules collection instead.")] [JsonProperty("autoTagBends",    DefaultValueHandling = DefaultValueHandling.Ignore)] public bool AutoTagBends { get; set; }

        /// <summary>
        /// Pre-Phase 137 boolean: when true and Rules is empty, the
        /// runner synthesises one AutoTag rule per known taggable
        /// category. Retained for back-compat with simpler profiles.
        /// </summary>
        [JsonProperty("autoTag", NullValueHandling = NullValueHandling.Ignore)]
        public bool? AutoTag { get; set; }

        /// <summary>
        /// Pre-Phase 137 boolean: when true the runner places dim
        /// chains across grid intersections. Retained for back-compat.
        /// </summary>
        [JsonProperty("autoDim", NullValueHandling = NullValueHandling.Ignore)]
        public bool? AutoDim { get; set; }

        // ── Rule collection (Phase 137 generic API) ──

        [JsonProperty("rules", NullValueHandling = NullValueHandling.Ignore)]
        public List<AutoAnnotationRule> Rules { get; set; }

        // ── Dimensioning ──

        /// <summary>Linear | Ordinate | Chain.</summary>
        [JsonProperty("dimensionStrategy")] public string DimensionStrategy { get; set; } = "Linear";
        [JsonProperty("dimensionStyle")]    public string DimensionStyle { get; set; }

        // ── Tag families + depth + density ──

        [JsonProperty("tagFamilies")] public Dictionary<string, string> TagFamilies { get; set; }
            = new Dictionary<string, string>();

        /// <summary>
        /// Per-category TAG7 paragraph depth (1..10) for THIS drawing type,
        /// keyed like <see cref="TagFamilies"/>. Layered by
        /// TagDepthLayering between the token profile's categoryDepths
        /// (wins) and the style pack's categoryDepths (loses); written to
        /// element types by TokenProfileApplier.WriteCategoryDepths.
        /// </summary>
        [JsonProperty("tagDepths", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, int> TagDepths { get; set; }
            = new Dictionary<string, int>();

        [JsonProperty("denseUntilScale", NullValueHandling = NullValueHandling.Ignore)]
        public int? DenseUntilScale { get; set; }

        // ── Decorative annotation (Phase 137) ──

        [JsonProperty("northArrowFamily",   NullValueHandling = NullValueHandling.Ignore)] public string NorthArrowFamily { get; set; }
        [JsonProperty("northArrowPosition", NullValueHandling = NullValueHandling.Ignore)] public string NorthArrowPosition { get; set; }
        [JsonProperty("northArrowSizeMm",   NullValueHandling = NullValueHandling.Ignore)] public double? NorthArrowSizeMm { get; set; }
        [JsonProperty("scaleBarFamily",     NullValueHandling = NullValueHandling.Ignore)] public string ScaleBarFamily { get; set; }
        [JsonProperty("scaleBarPosition",   NullValueHandling = NullValueHandling.Ignore)] public string ScaleBarPosition { get; set; }
        [JsonProperty("keyPlanFamily",      NullValueHandling = NullValueHandling.Ignore)] public string KeyPlanFamily { get; set; }
        [JsonProperty("keyPlanPosition",    NullValueHandling = NullValueHandling.Ignore)] public string KeyPlanPosition { get; set; }
        [JsonProperty("matchlineOffsetMm",  NullValueHandling = NullValueHandling.Ignore)] public double? MatchlineOffsetMm { get; set; }

        // ── Spot annotations (Phase 137) ──

        [JsonProperty("spotElevationRules",  NullValueHandling = NullValueHandling.Ignore)] public List<SpotAnnotationRule> SpotElevationRules { get; set; }
        [JsonProperty("spotCoordinateRules", NullValueHandling = NullValueHandling.Ignore)] public List<SpotAnnotationRule> SpotCoordinateRules { get; set; }

        /// <summary>True when any legacy per-category flag is set. A METHOD, not a
        /// property, so it is never serialised (and never changes a checksum).</summary>
        public bool HasLegacyFlags()
        {
#pragma warning disable CS0618
            return AutoDimGrids || AutoDimLevels || AutoTagRooms || AutoTagDoors || AutoTagWindows
                || AutoTagEquipment || AutoTagWelds || AutoTagSupports || AutoTagBends;
#pragma warning restore CS0618
        }

        /// <summary>Fold the legacy flags into <see cref="Rules"/>. Called by
        /// AnnotationRunner.Apply when <see cref="HasLegacyFlags"/> is true.</summary>
        public void MigrateFromLegacy()
        {
            if (Rules == null) Rules = new List<AutoAnnotationRule>();
#pragma warning disable CS0618
            void Add(string cat, string rt)
            {
                foreach (var r in Rules)
                    if (string.Equals(r.Category, cat, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.RuleType, rt, StringComparison.OrdinalIgnoreCase))
                        return;
                Rules.Add(new AutoAnnotationRule { Category = cat, RuleType = rt });
            }
            if (AutoDimGrids)     Add("Grids", "AutoDim");
            if (AutoDimLevels)    Add("Levels", "AutoDim");
            if (AutoTagRooms)     Add("Rooms", "AutoTag");
            if (AutoTagDoors)     Add("Doors", "AutoTag");
            if (AutoTagWindows)   Add("Windows", "AutoTag");
            if (AutoTagEquipment) Add("Mechanical Equipment", "AutoTag");
            if (AutoTagWelds)     Add("Pipe Fittings", "AutoTag");
            if (AutoTagSupports)  Add("Structural Framing", "AutoTag");
            if (AutoTagBends)     Add("Duct Fittings", "AutoTag");
            AutoDimGrids = AutoDimLevels = false;
            AutoTagRooms = AutoTagDoors = AutoTagWindows = false;
            AutoTagEquipment = AutoTagWelds = AutoTagSupports = AutoTagBends = false;
#pragma warning restore CS0618
        }
    }

    public sealed class AutoAnnotationRule
    {
        /// <summary>
        /// AutoTag / AutoDim / GridDim / LevelAnnotation / RoomTag /
        /// SpaceTag / AreaTag / MaterialTag / KeynoteTag / MultiCategoryTag /
        /// WireAnnotation.
        /// </summary>
        [JsonProperty("ruleType")] public string RuleType { get; set; } = "AutoTag";

        /// <summary>
        /// BIC name, localised display name, or "*" for all taggable
        /// categories.
        /// </summary>
        [JsonProperty("category")] public string Category { get; set; }

        // A-2: every field below has an engine reader. tag7Depth, addTickMarks
        // and batchScope were declared with none (tag7Depth only bound to a
        // grid column) and were removed; AnnotationRulePackConsumerTests fails
        // if a new field arrives without a reader.
        [JsonProperty("tagFamily",   NullValueHandling = NullValueHandling.Ignore)] public string TagFamily { get; set; }
        /// <summary>TAG rules: NoLeader / Attached / Free (AnnotationRunner.TagCategory).</summary>
        [JsonProperty("leaderStyle", NullValueHandling = NullValueHandling.Ignore)] public string LeaderStyle { get; set; }
        [JsonProperty("orientation", NullValueHandling = NullValueHandling.Ignore)] public string Orientation { get; set; }   // Horizontal / Vertical / Model
        [JsonProperty("skipIfTagged")] public bool SkipIfTagged { get; set; } = true;
        /// <summary>
        /// Skip elements smaller than this. Measured by ElementSize: MEP curve
        /// section size, else curve length, else plan bounding-box extent.
        /// Read by AutoTag and by every dimension / MEP-annotation kind.
        /// </summary>
        [JsonProperty("minSizeMm",   NullValueHandling = NullValueHandling.Ignore)] public double? MinSizeMm { get; set; }
        [JsonProperty("condition",   NullValueHandling = NullValueHandling.Ignore)] public string Condition { get; set; }
        /// <summary>
        /// Case-insensitive regex on "Family : Type" narrowing the rule within its
        /// category (e.g. rainwater outlets among Plumbing Fixtures). See
        /// RuleFamilyFilter; an invalid pattern disables the rule, loudly.
        /// </summary>
        [JsonProperty("familyMatch", NullValueHandling = NullValueHandling.Ignore)] public string FamilyMatch { get; set; }
        [JsonProperty("enabled")] public bool Enabled { get; set; } = true;
        /// <summary>
        /// TAG rules: TAG7 paragraph depth (1..10) for this rule's category.
        /// Folded into the drawing type's depth layer by TagDepthLayering;
        /// it beats <see cref="AnnotationRulePack.TagDepths"/> for the same key.
        /// </summary>
        [JsonProperty("depth", NullValueHandling = NullValueHandling.Ignore)] public int? Depth { get; set; }
    }

    public sealed class SpotAnnotationRule
    {
        [JsonProperty("category")]     public string Category { get; set; }
        [JsonProperty("symbolFamily",  NullValueHandling = NullValueHandling.Ignore)] public string SymbolFamily { get; set; }
        [JsonProperty("leaderStyle",   NullValueHandling = NullValueHandling.Ignore)] public string LeaderStyle { get; set; }
        // A-2: slopeArrow was declared and read by nothing; removed.
    }
}
