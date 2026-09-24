// StingTools — Drawing Template Manager · Annotation rule-kind registry
//
// ONE source of truth for the AutoAnnotationRule.RuleType vocabulary.
//
// Why this file exists: the runner used to carry the vocabulary inline as
// two private HashSets (_tagRuleKinds) plus a chain of string.Equals in
// DimByRules. Anything outside those sets fell through BOTH filters with
// no warning, so a ruleType nobody had implemented was indistinguishable
// from one that ran. 56 of the 334 rules in the shipped corporate
// catalogue were in that state — AutoDimWallLength, AutoDimOpenings,
// AutoAnnotateSlope, AutoAnnotateFlowArrow, AutoTagRoomName,
// AutoTagRoomNumber, AutoAnnotateSpaceNumber, AutoDimColumnGrid — 17% of
// the authored annotation intent silently producing nothing.
//
// The fix is not "add eight more cases to the switch". It is to make the
// vocabulary a declared, enumerable thing that:
//
//   * the runner dispatches from (Resolve → Kind → pass),
//   * DrawingTypeValidator validates JSON against (DT-139), so an
//     unimplemented ruleType is a build-time/pre-flight ERROR rather
//     than a silent runtime no-op,
//   * DrawingTypeExcelCommands offers as its enum options,
//   * and a unit test can walk with AllRuleTypes to prove every declared
//     name reaches a handler.
//
// Adding a ruleType therefore means adding it HERE, which immediately
// fails the "every kind has a handler" test until the handler exists.
// That is the inexpressible-failure property: you cannot ship a declared
// name with no implementation.
//
// Revit-free by construction — no Autodesk references — so the whole
// registry is exercised by StingTools.Tags.Tests.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    /// <summary>
    /// Which runner pass owns a given <see cref="AutoAnnotationRule.RuleType"/>.
    /// </summary>
    public enum AnnotationPass
    {
        /// <summary>Unrecognised — the runner warns and the validator errors.</summary>
        Unknown = 0,
        /// <summary>IndependentTag placement (TagByRules).</summary>
        Tag,
        /// <summary>Dimension placement (DimByRules).</summary>
        Dim,
        /// <summary>SpotDimension placement (SpotByRules).</summary>
        Spot,
        /// <summary>Annotation-symbol placement (SymbolByRules).</summary>
        Symbol,
        /// <summary>3D tagging — routed to Tag3DCommand, View3D only.</summary>
        ThreeD,
        /// <summary>Explicitly "do nothing" — reserved so a pack can disable a
        /// slot without deleting the row. Never warns.</summary>
        None,
    }

    /// <summary>
    /// One entry in the rule-kind vocabulary: the canonical name, the pass
    /// that owns it, and what it targets.
    /// </summary>
    public sealed class AnnotationRuleKind
    {
        public string Name { get; }
        public AnnotationPass Pass { get; }
        /// <summary>
        /// Human summary used by the validator message and the Excel column
        /// comment, so the vocabulary documents itself in both surfaces.
        /// </summary>
        public string Summary { get; }
        /// <summary>
        /// When set, the rule's Category is forced to this value before
        /// dispatch. Lets AutoTagRoomName / AutoTagRoomNumber be honest
        /// aliases — they only ever meant "tag Rooms" — without every
        /// caller having to know that.
        ///
        /// MUST be the localised DISPLAY name, not the <c>OST_</c> form.
        /// AnnotationRunner forwards the effective category to
        /// ResolveTagTypeId as the <c>pack.TagFamilies</c> lookup key, and
        /// every tagFamilies table in the catalogue is keyed by display name
        /// ("Rooms" on 12 profiles, "Doors" on 3, …). A BIC string there
        /// makes the lookup miss and the declared tag family is silently
        /// replaced by "first loaded tag of the category".
        /// ResolveCategoryId accepts either spelling, so the display name is
        /// the one form that satisfies both consumers.
        /// </summary>
        public string ForcedCategory { get; }

        internal AnnotationRuleKind(string name, AnnotationPass pass, string summary, string forcedCategory = null)
        {
            Name = name; Pass = pass; Summary = summary; ForcedCategory = forcedCategory;
        }

        public override string ToString() => $"{Name} ({Pass})";
    }

    /// <summary>
    /// The declared vocabulary. <see cref="Resolve"/> is the only way the
    /// runner should decide what a rule means.
    /// </summary>
    public static class AnnotationRuleKinds
    {
        // ── Canonical names ───────────────────────────────────────────────
        // Tag pass
        public const string AutoTag             = "AutoTag";
        public const string RoomTag             = "RoomTag";
        public const string SpaceTag            = "SpaceTag";
        public const string AreaTag             = "AreaTag";
        public const string MaterialTag         = "MaterialTag";
        public const string KeynoteTag          = "KeynoteTag";
        public const string MultiCategoryTag    = "MultiCategoryTag";
        public const string AutoTagRoomName     = "AutoTagRoomName";
        public const string AutoTagRoomNumber   = "AutoTagRoomNumber";
        public const string AutoAnnotateSpaceNumber = "AutoAnnotateSpaceNumber";

        // Dim pass
        public const string AutoDim             = "AutoDim";
        public const string GridDim             = "GridDim";
        public const string LevelAnnotation     = "LevelAnnotation";
        public const string AutoDimWallLength   = "AutoDimWallLength";
        public const string AutoDimOpenings     = "AutoDimOpenings";
        public const string AutoDimColumnGrid   = "AutoDimColumnGrid";
        public const string AutoDimMEPRun       = "AutoDimMEPRun";
        public const string AutoDimMEPToGrid    = "AutoDimMEPToGrid";

        // Spot pass
        public const string AutoSpotInvert      = "AutoSpotInvert";
        public const string AutoAnnotateSlope   = "AutoAnnotateSlope";

        // Symbol pass
        public const string AutoAnnotateFlowArrow = "AutoAnnotateFlowArrow";

        // 3D
        public const string Auto3DTag           = "Auto3DTag";

        // Opt-out
        public const string NoAnnotation        = "None";

        private static readonly AnnotationRuleKind[] _all =
        {
            // ── Tag pass ──
            new AnnotationRuleKind(AutoTag, AnnotationPass.Tag,
                "Place the category's tag family on every untagged instance in the view."),
            new AnnotationRuleKind(RoomTag, AnnotationPass.Tag,
                "Room tag.", forcedCategory: "Rooms"),
            new AnnotationRuleKind(SpaceTag, AnnotationPass.Tag,
                "MEP space tag.", forcedCategory: "Spaces"),
            new AnnotationRuleKind(AreaTag, AnnotationPass.Tag,
                "Area tag.", forcedCategory: "Areas"),
            new AnnotationRuleKind(MaterialTag, AnnotationPass.Tag,
                "Material tag."),
            new AnnotationRuleKind(KeynoteTag, AnnotationPass.Tag,
                "Keynote tag."),
            new AnnotationRuleKind(MultiCategoryTag, AnnotationPass.Tag,
                "Multi-category tag."),
            // Aliases the corporate catalogue already uses. Both only ever
            // meant "tag Rooms" — which field the tag prints is a property of
            // the tag family's label, not of the placement rule — so they
            // resolve to the Tag pass with Rooms forced. Kept as distinct
            // names so existing JSON (and the intent it records) survives.
            new AnnotationRuleKind(AutoTagRoomName, AnnotationPass.Tag,
                "Room tag showing the room name (tag family's label decides the field).",
                forcedCategory: "Rooms"),
            new AnnotationRuleKind(AutoTagRoomNumber, AnnotationPass.Tag,
                "Room tag showing the room number (tag family's label decides the field).",
                forcedCategory: "Rooms"),
            new AnnotationRuleKind(AutoAnnotateSpaceNumber, AnnotationPass.Tag,
                "MEP space tag showing the space number.",
                forcedCategory: "Spaces"),

            // ── Dim pass ──
            new AnnotationRuleKind(AutoDim, AnnotationPass.Dim,
                "Grid chain (or level chain when the rule's category names Levels)."),
            new AnnotationRuleKind(GridDim, AnnotationPass.Dim,
                "Grid chain, one per parallel grid set."),
            new AnnotationRuleKind(LevelAnnotation, AnnotationPass.Dim,
                "Level chain on a section / elevation."),
            new AnnotationRuleKind(AutoDimWallLength, AnnotationPass.Dim,
                "One linear dimension along each wall's own length."),
            new AnnotationRuleKind(AutoDimOpenings, AnnotationPass.Dim,
                "Per host wall, a chain from wall end through each door / window centre."),
            new AnnotationRuleKind(AutoDimColumnGrid, AnnotationPass.Dim,
                "Perpendicular offset dimension from each column to its nearest grid."),
            new AnnotationRuleKind(AutoDimMEPRun, AnnotationPass.Dim,
                "Centre-to-centre chain along each connected MEP run."),
            new AnnotationRuleKind(AutoDimMEPToGrid, AnnotationPass.Dim,
                "Perpendicular drop from each MEP run to its nearest grid."),

            // ── Spot pass ──
            new AnnotationRuleKind(AutoSpotInvert, AnnotationPass.Spot,
                "Spot elevation at drainage pipe invert level (BS EN 12056 / AD-H)."),
            new AnnotationRuleKind(AutoAnnotateSlope, AnnotationPass.Spot,
                "Spot slope on every sloped pipe / duct run in the view."),

            // ── Symbol pass ──
            new AnnotationRuleKind(AutoAnnotateFlowArrow, AnnotationPass.Symbol,
                "Flow-direction arrow symbol at the midpoint of each MEP run."),

            // ── 3D ──
            new AnnotationRuleKind(Auto3DTag, AnnotationPass.ThreeD,
                "Tag in a 3D view via Tag3DCommand. Declared once per drawing type; "
                + "per-category rows are redundant because the 3D pass tags the whole view."),

            // ── Opt-out ──
            new AnnotationRuleKind(NoAnnotation, AnnotationPass.None,
                "Explicitly no annotation. Use instead of deleting a row you want to keep for the record."),
        };

        private static readonly Dictionary<string, AnnotationRuleKind> _byName =
            _all.ToDictionary(k => k.Name, StringComparer.OrdinalIgnoreCase);

        /// <summary>Every declared kind, in declaration order.</summary>
        public static IReadOnlyList<AnnotationRuleKind> All => _all;

        /// <summary>Every declared canonical ruleType name.</summary>
        public static IReadOnlyList<string> AllRuleTypes => _all.Select(k => k.Name).ToList();

        /// <summary>
        /// Resolve a ruleType string. Null / empty resolves to
        /// <see cref="AutoTag"/>, matching the POCO default. An unknown
        /// name returns null — callers must treat that as a loud failure,
        /// never as "skip quietly".
        /// </summary>
        public static AnnotationRuleKind Resolve(string ruleType)
        {
            if (string.IsNullOrWhiteSpace(ruleType)) return _byName[AutoTag];
            return _byName.TryGetValue(ruleType.Trim(), out var k) ? k : null;
        }

        public static bool IsKnown(string ruleType) => Resolve(ruleType) != null;

        /// <summary>Pass that owns this ruleType, or Unknown.</summary>
        public static AnnotationPass PassOf(string ruleType)
            => Resolve(ruleType)?.Pass ?? AnnotationPass.Unknown;

        public static bool IsTagKind(string ruleType)  => PassOf(ruleType) == AnnotationPass.Tag;
        public static bool IsDimKind(string ruleType)  => PassOf(ruleType) == AnnotationPass.Dim;
        public static bool IsSpotKind(string ruleType) => PassOf(ruleType) == AnnotationPass.Spot;
        public static bool IsSymbolKind(string ruleType) => PassOf(ruleType) == AnnotationPass.Symbol;
        public static bool IsThreeDKind(string ruleType) => PassOf(ruleType) == AnnotationPass.ThreeD;

        /// <summary>
        /// The category a rule should actually act on: the kind's
        /// <see cref="AnnotationRuleKind.ForcedCategory"/> when it declares
        /// one, else the rule's own category. Keeps "AutoTagRoomName on
        /// category Walls" from tagging walls.
        /// </summary>
        public static string EffectiveCategory(string ruleType, string declaredCategory)
        {
            var k = Resolve(ruleType);
            return string.IsNullOrWhiteSpace(k?.ForcedCategory) ? declaredCategory : k.ForcedCategory;
        }

        /// <summary>
        /// One-line vocabulary listing for validator / dialog messages.
        /// </summary>
        public static string VocabularyFor(AnnotationPass pass)
            => string.Join(", ", _all.Where(k => k.Pass == pass).Select(k => k.Name));

        /// <summary>Comma-joined canonical names, for an Excel enum column.</summary>
        public static string[] EnumOptions() => _all.Select(k => k.Name).ToArray();
    }
}
