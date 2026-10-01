// StingTools — Drawing Template Manager · Phase 137
//
// AnnotationRunner consumes an AnnotationRulePack and runs four
// passes against a single View, in order:
//
//   1. Tag rules    — IndependentTag.Create per resolved rule; rooms,
//                     spaces and areas take a SpatialElementTag (DTW-83)
//   2. Dim rules    — chained dims across grids / levels
//   3. Decorative   — north arrow, scale bar, key plan, matchlines
//   4. Spot rules   — spot elevations / spot coordinates
//
// Caller is responsible for an open Transaction. The runner does not
// open or close transactions.
//
// Phase 137 changes:
//   * Hardcoded BIC list replaced by RevitCategoryTree.TaggableCategories
//   * Generic rule list (AnnotationRulePack.Rules) — one rule per
//     (category, ruleType) pair drives the runner; legacy bool flags
//     fold into rules via MigrateFromLegacy.
//   * New Run(doc, view, pack, options) entry point + AnnotationResult.
//   * Legacy Apply(doc, view, drawingType) retained as a thin shim
//     so DrawingTypePresentation continues to compile.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Core.Drawing.Dimensioning;

namespace StingTools.Core.Drawing
{
    public sealed class AnnotationResult
    {
        public int TagsPlaced      { get; set; }
        public int DimsPlaced      { get; set; }
        public int DecorativePlaced { get; set; }
        public int SpotsPlaced     { get; set; }
        /// <summary>Annotations not re-created because the view already had them (C-4).</summary>
        public int Skipped         { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    public sealed class AnnotationRunStats
    {
        public int DimsCreated      { get; set; }
        /// <summary>Alias for DimsCreated used by DrawingTypePresentation.</summary>
        public int DimsPlaced       { get => DimsCreated; set => DimsCreated = value; }
        public int TagsPlaced       { get; set; }
        /// <summary>Decorative elements placed (spots, keynotes, etc.) — reserved for future passes.</summary>
        public int DecorativePlaced { get; set; }
        public int Skipped          { get; set; }
        public List<string> Warnings { get; } = new List<string>();

        public override string ToString()
            => $"AnnotationRun: {DimsCreated} dim(s), {TagsPlaced} tag(s), {Skipped} skipped, {Warnings.Count} warning(s)";
    }

    /// <summary>Runtime switches for the annotation pass.</summary>
    public sealed class AnnotationRunOptions
    {
        /// <summary>Skip auto-dimension steps (grids, levels).</summary>
        public bool SkipDims    { get; set; } = false;
        /// <summary>Skip auto-dim steps — alias matching DrawingTypePresentation usage.</summary>
        public bool SkipAutoDim { get => SkipDims; set => SkipDims = value; }
        /// <summary>Skip auto-tag steps.</summary>
        public bool SkipTags    { get; set; } = false;
        /// <summary>Skip auto-tag steps — alias matching DrawingTypePresentation usage.</summary>
        public bool SkipAutoTag { get => SkipTags; set => SkipTags = value; }
        /// <summary>Skip decorative / spot annotation steps.</summary>
        public bool SkipDecorative { get; set; } = false;
        /// <summary>Skip spot-elevation / spot-coordinate annotation steps.</summary>
        public bool SkipSpots      { get; set; } = false;
        /// <summary>View scale hint (1:N) supplied by the caller for density checks.</summary>
        public int  ViewScale      { get; set; } = 0;
        /// <summary>
        /// The pack to run instead of the drawing type's own — already composed
        /// by <see cref="AnnotationPackLayering.Compose"/> from the production
        /// rule and preset overrides. Null = the drawing type's pack.
        /// </summary>
        public AnnotationRulePack PackOverride { get; set; }
    }

    public static partial class AnnotationRunner
    {
        // ─── Public entry points ─────────────────────────────────────────

        /// <summary>
        /// Primary overload: accepts a full <see cref="DrawingType"/> and
        /// optional runtime options. Delegates to <see cref="Apply"/>.
        /// </summary>
        public static AnnotationRunStats Run(
            Document doc, View view, DrawingType drawingType, AnnotationRunOptions options = null)
        {
            return Apply(doc, view, drawingType, options);
        }

        /// <summary>
        /// Run the annotation pass defined by drawingType.Annotation
        /// against the given view. The caller is responsible for an
        /// active Transaction — this method performs many Element
        /// creations and expects to be wrapped.
        /// </summary>
        public static AnnotationRunStats Apply(
            Document doc, View view, DrawingType drawingType, AnnotationRunOptions options = null)
        {
            // The loaded-symbol index lives for a production batch (DrawingProducer
            // opens one); a stand-alone call gets its own, dropped when it returns.
            bool ownsIndex = !_symbolBatchActive;
            try
            {
                RevalidateSymbolIndex(doc);
                return ApplyCore(doc, view, drawingType, options);
            }
            finally
            {
                if (ownsIndex) ResetSymbolIndex();
            }
        }

        private static AnnotationRunStats ApplyCore(
            Document doc, View view, DrawingType drawingType, AnnotationRunOptions options)
        {
            var stats = new AnnotationRunStats();
            var pack = options?.PackOverride ?? drawingType?.Annotation;
            if (doc == null || view == null || drawingType == null || pack == null) return stats;

            // A-2: the comments here and in AnnotationRulePack said the legacy
            // per-category bools (autoTagRooms, autoDimGrids, ...) "fold into
            // rules via MigrateFromLegacy at load" - but MigrateFromLegacy had
            // no caller, so a profile still using them annotated nothing. Fold
            // them now, and only when one is set, so a modern pack is not
            // touched (the registry checksums packs at load, before this).
            if (pack.HasLegacyFlags())
            {
                pack.MigrateFromLegacy();
                stats.Warnings.Add($"Drawing type '{drawingType.Id}' uses legacy autoTag*/autoDim* flags; " +
                                   "they were folded into rules for this run — re-save the type to persist rules.");
            }

            // Surface unimplemented ruleTypes before any pass runs, so a
            // typo or an un-migrated name reads as a warning rather than as
            // a quietly empty drawing.
            ReportUnknownRuleTypes(pack, stats);

            // Scale-aware density — at scales coarser than DenseUntilScale,
            // skip per-element tagging. View.Scale is 1:N so a larger
            // number means a coarser drawing.
            int effectiveScale = (options?.ViewScale > 0) ? options.ViewScale : view.Scale;
            bool dense = !pack.DenseUntilScale.HasValue || effectiveScale <= pack.DenseUntilScale.Value;

            // ── Tagging — Phase 137 Rules path. MigrateFromLegacy folds the
            // legacy per-category bools into pack.Rules at load, so a single
            // rule walk covers both old and new formats; routed through the
            // proven TagCategory helper. Density-gated. (The previous body
            // read the per-category bools directly — but those are zeroed by
            // MigrateFromLegacy, so it was inert, and pack.Rules — used by 48
            // shipped drawing types — was never processed at all.)
            if (options?.SkipTags != true)
            {
                if (dense)
                {
                    try { TagByRules(doc, view, pack, stats, drawingType); }
                    catch (Exception ex) { stats.Warnings.Add("TagByRules: " + ex.Message); }
                }
                else
                {
                    stats.Skipped++;
                    stats.Warnings.Add($"Per-element tagging skipped — view scale 1:{effectiveScale} exceeds denseUntilScale 1:{pack.DenseUntilScale}.");
                }
            }

            // ── Dimensioning — every dim kind in AnnotationRuleKinds.
            if (options?.SkipDims != true)
            {
                try { DimByRules(doc, view, pack, stats); }
                catch (Exception ex) { stats.Warnings.Add("DimByRules: " + ex.Message); }
            }

            // ── Spot + symbol rules carried in pack.Rules. These share the
            // Spots / Decorative opt-outs with the array-driven passes below
            // because they produce the same kinds of element; what is new is
            // that a rule row naming AutoSpotInvert / AutoAnnotateSlope /
            // AutoAnnotateFlowArrow now reaches an engine at all.
            if (options?.SkipSpots != true)
            {
                try { SpotByRules(doc, view, pack, stats); }
                catch (Exception ex) { stats.Warnings.Add("SpotByRules: " + ex.Message); }
            }
            if (options?.SkipDecorative != true)
            {
                try { SymbolByRules(doc, view, pack, stats); }
                catch (Exception ex) { stats.Warnings.Add("SymbolByRules: " + ex.Message); }
            }

            // ── Decorative (north arrow / scale bar / key plan / matchlines)
            // + spot (elevation / coordinate) — additive Phase 137 passes that
            // had no legacy equivalent and were never wired into Apply.
            if (options?.SkipDecorative != true || options?.SkipSpots != true)
            {
                var aux = new AnnotationResult();
                if (options?.SkipDecorative != true)
                {
                    try { RunDecorativeAnnotation(doc, view, pack, options, aux); }
                    catch (Exception ex) { aux.Warnings.Add("Decorative: " + ex.Message); }
                }
                if (options?.SkipSpots != true)
                {
                    try { RunSpotAnnotation(doc, view, pack, options, aux); }
                    catch (Exception ex) { aux.Warnings.Add("Spot: " + ex.Message); }
                }
                stats.DecorativePlaced += aux.DecorativePlaced + aux.SpotsPlaced;
                // Surface decorative + spot idempotency skips (C-4) so a no-op
                // re-run reads as "skipped N" rather than looking like a failure.
                stats.Skipped += aux.Skipped;
                stats.Warnings.AddRange(aux.Warnings);
            }

            return stats;
        }

        // ─── Rules-based drivers (wire pack.Rules into the proven helpers) ──

        // The rule-type vocabulary lives in AnnotationRuleKinds — ONE
        // declared registry the runner dispatches from, DrawingTypeValidator
        // validates against (DT-139) and DrawingTypeExcelCommands offers as
        // enum options. It replaced a private HashSet here plus a chain of
        // string.Equals in DimByRules, which between them let any
        // unrecognised ruleType fall through BOTH passes in silence: 56 of
        // the 334 rules in the shipped catalogue were in that state.
        //
        // ReportUnknownRuleTypes below is the other half of the fix. A name
        // the registry does not know is now a WARNING naming the rule and
        // listing the valid vocabulary, so "declared but unimplemented" can
        // never again look identical to "ran and placed nothing".

        /// <summary>
        /// Warn once per unrecognised ruleType in the pack. Called at the top
        /// of the run so the report lands even when every pass then declines
        /// the rule.
        /// </summary>
        private static void ReportUnknownRuleTypes(AnnotationRulePack pack, AnnotationRunStats stats)
        {
            if (pack?.Rules == null) return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in pack.Rules)
            {
                if (r == null || !r.Enabled) continue;
                var rt = r.RuleType;
                if (AnnotationRuleKinds.IsKnown(rt)) continue;
                if (!seen.Add(rt ?? "<null>")) continue;
                stats.Warnings.Add(
                    $"Unknown annotation ruleType '{rt}' (category '{r.Category}') — no pass claims it, so nothing was placed. "
                    + $"Valid values: {string.Join(", ", AnnotationRuleKinds.AllRuleTypes)}.");
            }
        }

        /// <summary>
        /// Phase 137 Rules-based tagging. Walks pack.Rules (which, post-
        /// MigrateFromLegacy, also holds the folded legacy per-category bools);
        /// when Rules is empty and the general AutoTag bool is set, synthesises
        /// one AutoTag rule per taggable category. Auto3DTag short-circuits to
        /// Tag3DCommand (IndependentTag is 2D-only). Each resolved built-in
        /// category is tagged at most once via the proven TagCategory helper;
        /// custom (non-built-in) categories are skipped.
        /// </summary>
        private static void TagByRules(Document doc, View view, AnnotationRulePack pack, AnnotationRunStats stats,
            DrawingType drawingType = null)
        {
            if (pack.Rules != null && pack.Rules.Any(r => r != null && r.Enabled &&
                    AnnotationRuleKinds.IsThreeDKind(r.RuleType)))
            {
                try
                {
                    if (view is View3D v3d)
                    {
                        bool useNarrative = false;
                        try { useNarrative = ParameterHelpers.GetInt(view, ParamRegistry.DISPLAY_MODE, 0) == 6; }
                        catch { /* defensive */ }
                        var r3d = StingTools.Tags.Tag3DCommand.PlaceTagsInView(doc, v3d, useNarrative);
                        stats.TagsPlaced += r3d.Placed;
                        foreach (var w in r3d.Warnings) stats.Warnings.Add($"Auto3DTag: {w}");
                    }
                    else
                    {
                        stats.Warnings.Add($"Auto3DTag rule skipped — view '{view.Name}' is not a 3D view.");
                    }
                }
                catch (Exception ex) { stats.Warnings.Add("Auto3DTag: " + ex.Message); }
            }

            List<AutoAnnotationRule> effective;
            if (pack.Rules != null && pack.Rules.Count > 0)
                effective = pack.Rules
                    .Where(r => r != null && r.Enabled && AnnotationRuleKinds.IsTagKind(r.RuleType))
                    .ToList();
            else if (pack.AutoTag == true)
                effective = SharedParamGuids.AllCategoryEnums
                    .Select(bic => new AutoAnnotationRule { RuleType = "AutoTag", Category = bic.ToString() })
                    .ToList();
            else
                return;

            // C-4: one scan of this view's existing tags, shared by every
            // category below. Lazy so a pack with no taggable category never
            // pays for it. Without this the runner had no idempotency at all:
            // AutoAnnotationRule.SkipIfTagged (default true) was read nowhere,
            // so re-running SyncStyles, a drift heal, or DrawingTypePresentation
            // .Apply doubled every tag on the view.
            var taggedIndex = new Lazy<Dictionary<string, List<string>>>(() => BuildTaggedElementIndex(doc, view, stats));

            // One pass per (category, rule tag family, familyMatch) — not per
            // category, which silently dropped the second of two rules on one
            // category (room tag + pressure-regime tag). See TagRuleIdentity.
            var doneRules = new HashSet<string>(StringComparer.Ordinal);
            var specialistFamilies = TagRuleIdentity.SpecialistFamilies(effective.Select(r => r?.TagFamily));
            // Primary rules first, so a specialist tag placed earlier in the run
            // can never be what a later primary rule reads as "already tagged".
            effective = effective.OrderBy(r => string.IsNullOrWhiteSpace(r?.TagFamily) ? 0 : 1).ToList();
            foreach (var rule in effective)
            {
                if (rule == null) continue;
                try
                {
                    // B1: honour the rule's `condition`. AnnotationConditionEvaluator
                    // is a clean fail-open parser that had ZERO call sites, so this
                    // field was a silent no-op on every shipped type that declares
                    // it. Fail-open means an unparseable condition still runs the
                    // rule rather than silently dropping requested annotation.
                    if (!string.IsNullOrWhiteSpace(rule.Condition))
                    {
                        var cctx = ConditionContext.FromView(doc, view, rule.Category);
                        if (!AnnotationConditionEvaluator.Evaluate(rule.Condition, cctx))
                        {
                            stats.Skipped++;
                            continue;
                        }
                    }

                    // The kind's forced category wins over the row's own, so
                    // RoomTag / AutoTagRoomName / AutoTagRoomNumber /
                    // SpaceTag / AutoAnnotateSpaceNumber / AreaTag always act
                    // on the category they name, never on whatever the row
                    // happened to carry. A row with no resolvable category is
                    // reported, not skipped in silence.
                    // Material callouts tag FACES of hosts and resolve a Material Tags
                    // family, never the host's own tag category — their own path.
                    var kind = AnnotationRuleKinds.Resolve(rule.RuleType)?.Name;
                    if (kind == AnnotationRuleKinds.MaterialTag || kind == AnnotationRuleKinds.MaterialTagLayers)
                    {
                        if (doneRules.Add(kind + "|" + (rule.Category ?? "") + "|" + (rule.FamilyMatch ?? "")))
                            RunMaterialCallouts(doc, view, pack, rule, kind == AnnotationRuleKinds.MaterialTagLayers,
                                stats, drawingType);
                        continue;
                    }

                    var effCat = AnnotationRuleKinds.EffectiveCategory(rule.RuleType, rule.Category);
                    var catId = ResolveCategoryId(doc, effCat);
                    if (catId == ElementId.InvalidElementId)
                    {
                        stats.Warnings.Add($"Tag rule '{rule.RuleType}': category '{effCat}' not found in this document — skipped.");
                        continue;
                    }
                    long cv = catId.Value;
                    if (!doneRules.Add(TagRuleIdentity.DedupKey(cv, rule.TagFamily, rule.FamilyMatch))) continue;
                    // BuiltInCategory's underlying type is long (Revit 2024+), so
                    // handing Enum.IsDefined an int threw "Enum underlying type
                    // and the object must be same type" for EVERY rule — the
                    // per-rule catch below swallowed it as a warning, so the
                    // whole auto-tag pass silently placed nothing. Pass the long.
                    if (!Enum.IsDefined(typeof(BuiltInCategory), cv)) continue; // skip custom categories

                    // DTW-83: rooms, spaces and areas take a SpatialElementTag
                    // (NewRoomTag / NewSpaceTag / NewAreaTag). IndependentTag.Create
                    // on a room threw once per room (or duplicated), so the 40
                    // shipped room / space / area rules placed nothing.
                    var spatial = SpatialTagRouting.KindOf(((BuiltInCategory)cv).ToString());
                    if (spatial != SpatialTagKind.None)
                    {
                        TagSpatialCategory(doc, view, pack, (BuiltInCategory)cv, spatial, effCat, stats, rule,
                            taggedIndex.Value, drawingType, specialistFamilies);
                        continue;
                    }
                    TagCategory(doc, view, pack, (BuiltInCategory)cv, effCat, stats, rule, taggedIndex.Value, drawingType,
                        specialistFamilies);
                }
                catch (Exception ex) { stats.Warnings.Add($"Tag rule '{rule.Category}': {ex.Message}"); }
            }
        }

        /// <summary>
        /// Rules-based dimensioning, dispatched from AnnotationRuleKinds.
        ///
        /// Grid and level chains are placed at most once per view however
        /// many rules ask for them (they are whole-view chains, so a second
        /// is always a duplicate). The element and MEP kinds are per-rule,
        /// because each carries its own category / minSizeMm narrowing, and
        /// each engine holds its own idempotency index.
        ///
        /// Every dim kind the registry declares reaches a handler here; the
        /// default arm is unreachable while registry and switch agree, and
        /// says so loudly rather than dropping the rule — which is exactly
        /// what the old three-string.Equals form did for AutoDimWallLength,
        /// AutoDimOpenings, AutoDimColumnGrid, AutoDimMEPRun and
        /// AutoDimMEPToGrid.
        /// </summary>
        private static void DimByRules(Document doc, View view, AnnotationRulePack pack, AnnotationRunStats stats)
        {
            bool didGrids = false, didLevels = false;

            // dimensionStrategy "None" means the profile wants no automatic
            // dimensioning. Honoured once here rather than in each engine.
            if (DimensionStrategy.Suppresses(pack?.DimensionStrategy))
            {
                int asked = pack?.Rules?.Count(x => x != null && x.Enabled && AnnotationRuleKinds.IsDimKind(x.RuleType)) ?? 0;
                if (asked > 0)
                {
                    stats.Skipped += asked;
                    stats.Warnings.Add(
                        $"dimensionStrategy is \"None\" — {asked} dimension rule(s) deliberately skipped. "
                        + "Set Linear / Chain / Ordinate to enable them.");
                }
                return;
            }

            if (pack.Rules != null)
            {
                foreach (var r in pack.Rules)
                {
                    if (r == null || !r.Enabled) continue;
                    if (!AnnotationRuleKinds.IsDimKind(r.RuleType)) continue;
                    if (!RuleConditionPasses(doc, view, r, "Dim", stats)) continue;

                    var aux = new AnnotationResult();
                    try
                    {
                        switch (AnnotationRuleKinds.Resolve(r.RuleType).Name)
                        {
                            case AnnotationRuleKinds.AutoDim:
                            case AnnotationRuleKinds.GridDim:
                            case AnnotationRuleKinds.LevelAnnotation:
                            {
                                // AutoDim is polymorphic on the row's category —
                                // the catalogue writes {Grids, AutoDim} and
                                // {Levels, AutoDim} — whereas GridDim and
                                // LevelAnnotation name their target outright.
                                bool isLevels =
                                    string.Equals(r.RuleType, AnnotationRuleKinds.LevelAnnotation, StringComparison.OrdinalIgnoreCase)
                                    || (!string.Equals(r.RuleType, AnnotationRuleKinds.GridDim, StringComparison.OrdinalIgnoreCase)
                                        && (r.Category ?? "").IndexOf("Level", StringComparison.OrdinalIgnoreCase) >= 0);
                                if (isLevels) { if (!didLevels) { DimLevels(doc, view, pack, stats); didLevels = true; } else stats.Skipped++; }
                                else          { if (!didGrids)  { DimGrids(doc, view, pack, stats);  didGrids  = true; } else stats.Skipped++; }
                                break;
                            }

                            case AnnotationRuleKinds.AutoDimWallLength:
                                ElementDimensioner.RunWallLength(doc, view, pack, r, aux);
                                break;

                            case AnnotationRuleKinds.AutoDimOpenings:
                                ElementDimensioner.RunOpenings(doc, view, pack, r, aux);
                                break;

                            case AnnotationRuleKinds.AutoDimColumnGrid:
                                ElementDimensioner.RunColumnToGrid(doc, view, pack, r, aux);
                                break;

                            case AnnotationRuleKinds.AutoDimMEPRun:
                                MEPDimensioner.RunChain(doc, view, pack, r, aux);
                                break;

                            case AnnotationRuleKinds.AutoDimMEPToGrid:
                                MEPDimensioner.RunGridDrop(doc, view, pack, r, aux);
                                break;

                            default:
                                stats.Warnings.Add(
                                    $"Dim ruleType '{r.RuleType}' is declared in AnnotationRuleKinds but has no handler in "
                                    + "AnnotationRunner.DimByRules — nothing placed. This is a wiring bug, not a data error.");
                                break;
                        }
                    }
                    catch (Exception ex) { stats.Warnings.Add($"Dim rule '{r.RuleType}/{r.Category}': {ex.Message}"); }

                    stats.DimsCreated += aux.DimsPlaced;
                    stats.Skipped     += aux.Skipped;
                    stats.Warnings.AddRange(aux.Warnings);
                }
            }

            if (pack.AutoDim == true && !didGrids)
            {
                try { DimGrids(doc, view, pack, stats); }
                catch (Exception ex) { stats.Warnings.Add("AutoDim grids: " + ex.Message); }
            }
        }

        /// <summary>
        /// Evaluate a rule's optional <c>condition</c>. Fail-open — an
        /// unparseable condition runs the rule rather than silently dropping
        /// requested annotation — and shared by the dim / spot / symbol
        /// passes so all four honour the field the tag pass already did.
        /// </summary>
        private static bool RuleConditionPasses(Document doc, View view, AutoAnnotationRule r,
            string passLabel, AnnotationRunStats stats)
        {
            if (string.IsNullOrWhiteSpace(r?.Condition)) return true;
            try
            {
                var cctx = ConditionContext.FromView(doc, view, r.Category);
                if (AnnotationConditionEvaluator.Evaluate(r.Condition, cctx)) return true;
                stats.Skipped++;
                return false;
            }
            catch (Exception ex)
            {
                stats.Warnings.Add($"{passLabel} rule condition '{r.Condition}': {ex.Message} — rule run anyway (fail-open).");
                return true;
            }
        }

        /// <summary>
        /// Spot-annotation rules carried in pack.Rules — AutoSpotInvert
        /// (drainage invert levels) and AutoAnnotateSlope (spot slopes).
        /// Distinct from ProcessSpotRules, which serves the separate
        /// spotElevationRules / spotCoordinateRules arrays; these two arrive
        /// as ordinary rows in pack.Rules and so had no route at all —
        /// DrainageInvertDimensioner had zero call sites anywhere in the tree.
        /// </summary>
        private static void SpotByRules(Document doc, View view, AnnotationRulePack pack, AnnotationRunStats stats)
        {
            if (pack?.Rules == null) return;
            foreach (var r in pack.Rules)
            {
                if (r == null || !r.Enabled) continue;
                if (!AnnotationRuleKinds.IsSpotKind(r.RuleType)) continue;
                if (!RuleConditionPasses(doc, view, r, "Spot", stats)) continue;

                var aux = new AnnotationResult();
                try
                {
                    switch (AnnotationRuleKinds.Resolve(r.RuleType).Name)
                    {
                        case AnnotationRuleKinds.AutoSpotInvert:
                            DrainageInvertDimensioner.Run(doc, view, pack, r, aux);
                            break;
                        case AnnotationRuleKinds.AutoAnnotateSlope:
                            MepAnnotator.RunSlope(doc, view, pack, r, aux);
                            break;
                        default:
                            stats.Warnings.Add(
                                $"Spot ruleType '{r.RuleType}' is declared in AnnotationRuleKinds but has no handler in "
                                + "AnnotationRunner.SpotByRules — nothing placed. This is a wiring bug, not a data error.");
                            break;
                    }
                }
                catch (Exception ex) { stats.Warnings.Add($"Spot rule '{r.RuleType}/{r.Category}': {ex.Message}"); }

                stats.DecorativePlaced += aux.SpotsPlaced;
                stats.Skipped          += aux.Skipped;
                stats.Warnings.AddRange(aux.Warnings);
            }
        }

        /// <summary>
        /// Annotation-symbol rules carried in pack.Rules — currently
        /// AutoAnnotateFlowArrow. Its own pass because a symbol is neither a
        /// tag (no host element) nor a dimension (no references).
        /// </summary>
        private static void SymbolByRules(Document doc, View view, AnnotationRulePack pack, AnnotationRunStats stats)
        {
            if (pack?.Rules == null) return;
            foreach (var r in pack.Rules)
            {
                if (r == null || !r.Enabled) continue;
                if (!AnnotationRuleKinds.IsSymbolKind(r.RuleType)) continue;
                if (!RuleConditionPasses(doc, view, r, "Symbol", stats)) continue;

                var aux = new AnnotationResult();
                try
                {
                    switch (AnnotationRuleKinds.Resolve(r.RuleType).Name)
                    {
                        case AnnotationRuleKinds.AutoAnnotateFlowArrow:
                            MepAnnotator.RunFlowArrow(doc, view, pack, r, aux);
                            break;
                        default:
                            stats.Warnings.Add(
                                $"Symbol ruleType '{r.RuleType}' is declared in AnnotationRuleKinds but has no handler in "
                                + "AnnotationRunner.SymbolByRules — nothing placed. This is a wiring bug, not a data error.");
                            break;
                    }
                }
                catch (Exception ex) { stats.Warnings.Add($"Symbol rule '{r.RuleType}/{r.Category}': {ex.Message}"); }

                stats.DecorativePlaced += aux.DecorativePlaced;
                stats.Skipped          += aux.Skipped;
                stats.Warnings.AddRange(aux.Warnings);
            }
        }

        // ─── Dimensioning ────────────────────────────────────────────────

        /// <summary>
        /// Drop a single overall dimension chain across all grids
        /// visible in the view. Each grid contributes one reference.
        /// </summary>
        /// <summary>
        /// C-4 idempotency for the two dimension chains.
        ///
        /// Strategy: detect an existing chain by what it REFERENCES rather than
        /// by a stamped marker. A marker would need a new shared parameter bound
        /// to Dimensions — provisioning across MR_PARAMETERS.txt, the .csv
        /// mirror, PARAMETER_REGISTRY.json and a category binding — whereas
        /// DimByRules places at most one grid chain and one level chain per
        /// view, so a coarse "does this view already hold a dimension
        /// referencing Grids / Levels?" question is exactly as precise as the
        /// runner needs and requires nothing new.
        ///
        /// Residual limitation: Revit can report AreReferencesAvailable == false
        /// (references lost, or the dimension is in a linked/【unloaded】 state).
        /// Such a dimension is treated as "unknown" and skipped over rather than
        /// counted as a match, so in the rare case where every dimension in the
        /// view is unreadable AND a prior STING chain exists, a duplicate is
        /// still possible. Failing that way round is deliberate: the alternative
        /// silently refuses to dimension views that merely contain odd geometry.
        /// </summary>
        /// <summary>True when this view already holds a chain that
        /// <paramref name="producer"/> stamped — exact, whatever Revit can still
        /// read of its references.</summary>
        private static bool ViewHasStampedChain(Document doc, View view, string producer)
            => Storage.StingAnnotationProvenanceSchema.Index(doc, view, typeof(Dimension), producer)
                .Keys.Any(k => AnnotationProvenance.HostOf(k) == view.UniqueId);

        private static bool ViewHasDimensionReferencing(Document doc, View view, BuiltInCategory targetCat)
        {
            try
            {
                foreach (var el in new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(Dimension))
                    .WhereElementIsNotElementType())
                {
                    if (!(el is Dimension dim)) continue;
                    try
                    {
                        if (!dim.AreReferencesAvailable) continue;   // unknown — not a match
                        var refs = dim.References;
                        if (refs == null) continue;
                        foreach (Reference r in refs)
                        {
                            // Through the link, so a chain to linked grids counts (DTW-85).
                            var host = ViewLinks.Resolve(doc, r);
                            if (host?.Category == null) continue;
                            if (host.Category.Id.Value == (long)targetCat) return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"ViewHasDimensionReferencing: dimension {dim.Id} — {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ViewHasDimensionReferencing({view?.Name}): {ex.Message}");
            }
            return false;
        }

        private static void DimGrids(Document doc, View view, AnnotationRulePack pack, AnnotationRunStats stats)
        {
            // Stamped chain first (exact); the reference test covers chains placed
            // before stamping and dimensions a person drew to the grids.
            if (ViewHasStampedChain(doc, view, AnnotationProvenance.DimGridChain)
                || ViewHasDimensionReferencing(doc, view, BuiltInCategory.OST_Grids))
            {
                stats.Skipped++;
                return;
            }

            // DTW-85: the host's grids AND those of loaded links shown in the view —
            // an MEP model whose grids live in the linked architectural model found
            // none and placed no chain. Linked grids carry link references.
            var gridLines = ViewLinks.StraightGrids(doc, view, stats.Warnings, out int arcs);
            // DTW-140: where a linked grid lies on a host grid (copy / monitor, possibly a
            // hair off) the host one is kept — StraightGrids lists host grids first and
            // KeepFirstOfCoincident keeps the first, as the column-grid dimensioner does.
            // Without it the offset link copy could win and the chain measure the link.
            var gridSegs = gridLines.Select((g, i) => new GridSeg(i,
                g.Line.GetEndPoint(0).X, g.Line.GetEndPoint(0).Y,
                g.Line.GetEndPoint(1).X, g.Line.GetEndPoint(1).Y)).ToList();
            var lines = GridChainGeometry.KeepFirstOfCoincident(gridSegs)
                .Select(i => (gridLines[i].Line, gridLines[i].Ref)).ToList();
            if (arcs > 0 && lines.Count >= 2)
                stats.Warnings.Add($"Grid dim: {arcs} arc grid(s) cannot join a linear chain — left out.");
            if (lines.Count < 2) return;

            // B1: honour the pack's dimensionStrategy. This was a declared
            // rule-pack field with no consumer — GridDimensioner read it but
            // nothing called GridDimensioner. DimensionStrategy.ResolveType
            // applies the strategy (and still lets an explicit DimensionStyle
            // name win), so the field now reaches the only grid-dimensioning
            // path that runs.
            ElementId dimStyleId = ElementId.InvalidElementId;
            try
            {
                var kind = DimensionStrategy.Parse(pack.DimensionStrategy);
                var dimType = DimensionStrategy.ResolveType(doc, kind, pack.DimensionStyle);
                if (dimType != null) dimStyleId = dimType.Id;
            }
            catch (Exception ex) { StingLog.Warn($"DimGrids strategy: {ex.Message}"); }
            if (dimStyleId == ElementId.InvalidElementId)
                dimStyleId = ResolveDimensionStyleId(doc, pack.DimensionStyle);

            PlaceGridChains(doc, view, lines, dimStyleId, stats);
        }

        /// <summary>
        /// One chain per set of parallel grids. A-4 split the grids by world axis
        /// (a dimension can only measure mutually parallel references); DTW-86
        /// groups them by their own direction instead, measures each grid ACROSS
        /// its set (dot with the set's normal) and runs the line along that normal
        /// — so a rotated building is dimensioned rather than reported as
        /// "coincident" from equal world-Y origins. Geometry: GridChainGeometry.
        /// </summary>
        private static void PlaceGridChains(Document doc, View view, List<(Line Line, Reference Ref)> lines,
            ElementId dimStyleId, AnnotationRunStats stats)
        {
            const double marginFt = 10.0;   // ~3 m clear of the grid extent
            double zPlane = lines[0].Line.GetEndPoint(0).Z;
            var segs = lines.Select((t, i) =>
            {
                var a = t.Line.GetEndPoint(0); var b = t.Line.GetEndPoint(1);
                return new GridSeg(i, a.X, a.Y, b.X, b.Y);
            }).ToList();

            foreach (var plan in GridChainGeometry.Plan(segs, marginFt))
            {
                if (!plan.Placeable)
                {
                    if (plan.Reason == "grids are coincident")
                        stats.Warnings.Add($"Grid dim ({plan.Label}): grids are coincident — no chain placed.");
                    continue;
                }
                try
                {
                    var refs = new ReferenceArray();
                    foreach (int i in plan.Members) refs.Append(lines[i].Ref);
                    if (refs.Size < 2) continue;

                    var dimLine = Line.CreateBound(new XYZ(plan.LineX0, plan.LineY0, zPlane),
                                                   new XYZ(plan.LineX1, plan.LineY1, zPlane));
                    var dim = (dimStyleId == null || dimStyleId == ElementId.InvalidElementId)
                        ? doc.Create.NewDimension(view, dimLine, refs)
                        : doc.Create.NewDimension(view, dimLine, refs, (DimensionType)doc.GetElement(dimStyleId));
                    if (dim != null)
                    {
                        stats.DimsCreated++;
                        Storage.StingAnnotationProvenanceSchema.Stamp(dim, AnnotationProvenance.DimGridChain,
                            AnnotationProvenance.Key(view.UniqueId, plan.Label));
                    }
                }
                catch (Exception ex) { stats.Warnings.Add($"Grid dim ({plan.Label}): {ex.Message}"); }
            }
        }

        /// <summary>
        /// Horizontal anchor for the level chain: a model-space point just
        /// outside the left edge of the view's crop, at mid height. Falls back
        /// to the view origin, then to the project origin, so a view with no
        /// active crop still gets a chain somewhere sensible rather than none.
        /// </summary>
        private static XYZ ResolveLevelChainAnchor(View view)
        {
            const double marginFt = 5.0;
            try
            {
                var cb = view.CropBox;
                if (cb != null)
                {
                    var frame = cb.Transform ?? Transform.Identity;
                    var pf = new XYZ(cb.Min.X - marginFt, (cb.Min.Y + cb.Max.Y) * 0.5, 0);
                    return frame.OfPoint(pf);
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveLevelChainAnchor({view?.Name}): {ex.Message}"); }
            try { if (view.Origin != null) return view.Origin; }
            catch (Exception ex) { StingLog.Warn($"ResolveLevelChainAnchor origin: {ex.Message}"); }
            return XYZ.Zero;
        }

        /// <summary>
        /// Drop a vertical dimension chain across all Levels visible in
        /// the section / elevation view.
        /// </summary>
        private static void DimLevels(Document doc, View view, AnnotationRulePack pack, AnnotationRunStats stats)
        {
            if (ViewHasStampedChain(doc, view, AnnotationProvenance.DimLevelChain)
                || ViewHasDimensionReferencing(doc, view, BuiltInCategory.OST_Levels))
            {
                stats.Skipped++;
                return;
            }

            var levels = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Levels)
                .WhereElementIsNotElementType()
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();
            if (levels.Count < 2) return;

            var refs = new ReferenceArray();
            foreach (var l in levels)
            {
                try { refs.Append(new Reference(l)); }
                catch (Exception ex) { StingLog.Warn($"Level ref {l.Id}: {ex.Message}"); }
            }
            if (refs.Size < 2) return;

            // A-10: the chain used to be pinned at X=0, Y=0 — the project
            // origin — so on any section not passing through the origin it
            // landed outside the crop and was invisible. Anchor it to the
            // view's own crop box instead, just outside the left edge.
            //
            // The anchor is computed in the crop's FRAME and mapped back to
            // model space (the E-2 lesson: crop Min/Max are frame coords, not
            // model coords), so this works on rotated plans and sections alike.
            double zLo = levels.First().Elevation;
            double zHi = levels.Last().Elevation;
            if (Math.Abs(zHi - zLo) < 1e-6)
            {
                // Every visible level shares an elevation — Line.CreateBound
                // would throw on a zero-length line.
                stats.Warnings.Add("Level dim: all visible levels share one elevation — no chain placed.");
                return;
            }

            var anchor = ResolveLevelChainAnchor(view);
            var dimLine = Line.CreateBound(
                new XYZ(anchor.X, anchor.Y, zLo),
                new XYZ(anchor.X, anchor.Y, zHi));
            try
            {
                var dim = doc.Create.NewDimension(view, dimLine, refs);
                if (dim != null)
                {
                    stats.DimsCreated++;
                    Storage.StingAnnotationProvenanceSchema.Stamp(dim, AnnotationProvenance.DimLevelChain,
                        AnnotationProvenance.Key(view.UniqueId));
                }
            }
            catch (Exception ex) { stats.Warnings.Add("Level dim: " + ex.Message); }
        }

        // ─── Tagging ─────────────────────────────────────────────────────

        // TagCategory walks every element of a category visible in the view
        // and drops an IndependentTag at its centre (tag family: rule, then
        // pack TagFamilies, then first loaded). Depth is NOT written here -- see
        // TagDepthLayering / TokenProfileApplier.WriteCategoryDepths.

        /// <summary>
        /// Hosts already carrying a tag in this view — IndependentTags and (DTW-83)
        /// room / space / area tags — keyed by <see cref="TaggedHostKey"/>, each with
        /// the families of those tags ("" when the family cannot be read — still a
        /// tag). Built once per view and shared across every tag rule.
        /// </summary>
        private static Dictionary<string, List<string>> BuildTaggedElementIndex(Document doc, View view, AnnotationRunStats stats)
        {
            var set = new Dictionary<string, List<string>>();
            try
            {
                foreach (var el in new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag))
                    .WhereElementIsNotElementType())
                {
                    if (!(el is IndependentTag tag)) continue;
                    try
                    {
                        string fam = (doc.GetElement(tag.GetTypeId()) as FamilySymbol)?.FamilyName ?? "";
                        // GetTaggedElementIds, not ...LocalElementIds: a tag on a
                        // linked element is a tag too (DTW-85), keyed by link instance.
                        foreach (var lid in tag.GetTaggedElementIds())
                        {
                            if (lid == null) continue;
                            long linkInst = lid.LinkInstanceId?.Value ?? -1;
                            long linked = lid.LinkedElementId?.Value ?? -1;
                            long hostId = lid.HostElementId?.Value ?? -1;
                            if (hostId <= 0 && (linkInst <= 0 || linked <= 0)) continue;
                            var key = TaggedHostKey.From(hostId, linkInst, linked);
                            if (!set.TryGetValue(key, out var fams)) set[key] = fams = new List<string>();
                            fams.Add(fam);
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"BuildTaggedElementIndex: tag {tag.Id} — {ex.Message}");
                    }
                }
                AddSpatialTagsToIndex(doc, view, set);
            }
            catch (Exception ex)
            {
                // Fail open with a warning: an empty index means "tag
                // everything", i.e. the previous behaviour, rather than
                // silently skipping work the user asked for.
                stats.Warnings.Add($"Could not index existing tags in '{view.Name}' ({ex.Message}); " +
                                   "duplicate tags are possible on this view.");
            }
            return set;
        }

        /// <summary>
        /// A face of <paramref name="el"/> a material tag can reference, and a point on it for
        /// the tag head — for an AutoTag rule that names a Material Tag family. Same face logic
        /// as the MaterialTag rule kind (FaceCandidates): host finish faces, else the element's
        /// own geometry; a painted face towards the viewer first, else the one facing the
        /// viewer (FaceChoice). NOT VERIFIED IN REVIT.
        /// </summary>
        private static Reference FaceReferenceFor(Element el, View view, out XYZ point)
        {
            point = null;
            var faces = FaceCandidates(el.Document, el, view);
            int best = FaceChoice.Best(faces.Select(f => f.Dot).ToList(), faces.Select(f => f.Area).ToList(),
                                       faces.Select(f => f.Painted).ToList());
            if (best < 0) return null;
            point = faces[best].Point;
            return faces[best].Ref;
        }

        /// <summary>
        /// Map a rule's declared <c>orientation</c> onto Revit's TagOrientation.
        /// The three accepted values mirror the enum exactly — Horizontal,
        /// Vertical, Model (= AnyModelDirection) — so no interpretation is
        /// involved. Unset or unrecognised falls back to Horizontal, which is
        /// what every tag got before the field was read, and warns once per
        /// category so a typo is visible rather than silently ignored.
        /// </summary>
        private static TagOrientation ResolveTagOrientation(
            AutoAnnotationRule rule, string catKey, AnnotationRunStats stats)
        {
            var declared = rule?.Orientation;
            if (string.IsNullOrWhiteSpace(declared)) return TagOrientation.Horizontal;

            switch (declared.Trim().ToLowerInvariant())
            {
                case "horizontal": return TagOrientation.Horizontal;
                case "vertical":   return TagOrientation.Vertical;
                case "model":
                case "anymodeldirection": return TagOrientation.AnyModelDirection;
                default:
                    stats?.Warnings.Add(
                        $"Rule orientation '{declared}' for {catKey} is not one of " +
                        "Horizontal / Vertical / Model — tagging horizontally.");
                    return TagOrientation.Horizontal;
            }
        }

        private static void TagCategory(Document doc, View view, AnnotationRulePack pack,
            BuiltInCategory bic, string catKey, AnnotationRunStats stats,
            AutoAnnotationRule rule = null, Dictionary<string, List<string>> alreadyTagged = null,
            DrawingType drawingType = null, ISet<string> specialistFamilies = null)
        {
            var elements = new FilteredElementCollector(doc, view.Id)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToElements();
            // DTW-85: the same category in loaded links the view shows.
            var linked = LinkedElementsOf(doc, view, bic, catKey, stats);
            if (elements.Count == 0 && linked.Count == 0) return;

            // B1: a per-rule tagFamily wins over the pack-level TagFamilies map.
            // AutoAnnotationRule.TagFamily was declared and read nowhere, so a rule
            // naming its own tag family was silently served the pack default.
            ElementId tagTypeId = ElementId.InvalidElementId;
            if (!string.IsNullOrWhiteSpace(rule?.TagFamily))
            {
                var byRule = SymbolIndexFor(doc).FindByName(rule.TagFamily);
                if (byRule != null) tagTypeId = byRule.Id;
                else stats.Warnings.Add(
                    $"Rule tag family '{rule.TagFamily}' for {catKey} is not loaded; using the pack default.");
            }
            if (tagTypeId == ElementId.InvalidElementId)
                tagTypeId = ResolveTagTypeId(doc, view, pack, catKey, bic, stats);
            if (tagTypeId == ElementId.InvalidElementId)
            {
                stats.Warnings.Add($"No tag family available for {catKey} — skipped.");
                return;
            }
            // Tag text size for this drawing (1:50 → 2.5 mm …). Inert until size
            // variants of the chosen family are loaded — see TagSizeVariant.
            if (drawingType != null)
                tagTypeId = ApplyTagSizeVariant(doc, tagTypeId, drawingType, catKey, stats);

            // The family this rule actually places — what "already tagged" is
            // measured against (TagRuleIdentity.ShouldSkip).
            var placedSymbol = doc.GetElement(tagTypeId) as FamilySymbol;
            string placedFamily = placedSymbol?.FamilyName ?? "";
            bool isSpecialistRule = !string.IsNullOrWhiteSpace(rule?.TagFamily);
            // A material tag labels a FACE's material and cannot tag a whole
            // element — handing it new Reference(el) failed once per element.
            bool tagsFaces = placedSymbol?.Category?.Id.Value == (long)BuiltInCategory.OST_MaterialTags;
            int noFace = 0;

            // Paragraph depth is NOT resolved or written here any more.
            //
            // This used to pre-resolve a depth from the pack's CategoryDepths (and
            // the rule's tag7Depth) and then write TAG_PARA_DEPTH_INT +
            // TAG_PARA_STATE_1..10_BOOL onto each host ELEMENT. Those writes could
            // never land, for two independent reasons:
            //
            //   • TAG_PARA_DEPTH_INT has no binding at all — zero rows in
            //     CATEGORY_BINDINGS.csv, RESOLVED_BINDINGS.csv AND
            //     FAMILY_PARAMETER_BINDINGS.csv. It exists only as a definition in
            //     MR_PARAMETERS.txt, so nothing anywhere can read it.
            //   • TAG_PARA_STATE_*_BOOL / TAG_WARN_VISIBLE_BOOL are bound as TYPE
            //     parameters (FAMILY_PARAMETER_BINDINGS.csv, 42 categories each).
            //     ParameterHelpers.SetInt resolves via Element.LookupParameter, which
            //     does not see type parameters from an instance — so an instance-scoped
            //     write is unreachable by construction, not merely unbound.
            //
            // The live per-category depth path is TokenProfileApplier.WriteCategoryDepths,
            // which writes the same gates to the element TYPE and therefore matches the
            // Type binding. Do not reinstate an element-scoped writer here.
            //
            // Per-rule tag orientation. IndependentTag.Create took a hardcoded
            // TagOrientation.Horizontal, so the field was inert.
            TagOrientation orientation = ResolveTagOrientation(rule, catKey, stats);

            // C-4: honour the rule's skipIfTagged (POCO default true). Only the
            // config dialog ever read this field before.
            bool skipIfTagged = rule?.SkipIfTagged ?? true;

            // A-2: leaderStyle on a TAG rule. Create() took a hard-coded
            // addLeader:false, so Attached / Free were ignored.
            var leader = TagLeader.Parse(rule?.LeaderStyle);
            if (leader == TagLeaderMode.Unrecognised)
            {
                stats.Warnings.Add($"Rule leaderStyle '{rule.LeaderStyle}' for {catKey} is not one of " +
                                   "NoLeader / Attached / Free — tagging without a leader.");
                leader = TagLeaderMode.None;
            }
            bool addLeader = leader == TagLeaderMode.Attached || leader == TagLeaderMode.Free;

            // A-2: minSizeMm on a TAG rule. Only the dimensioners read it, so a
            // rule saying "nothing under 50 mm" tagged every stub. Size is
            // ElementSize.MeasureFt: MEP section, else curve length, else plan
            // bbox extent. Unmeasurable elements are kept and counted.
            double? minSizeMm = rule?.MinSizeMm;
            int belowMin = 0, unmeasured = 0;

            // DRAW-8: familyMatch narrows the rule inside its category. An
            // invalid pattern skips the rule rather than tagging the category.
            var familyRx = RuleFamilyFilter.Compile(rule?.FamilyMatch, out var familyRxError);
            if (familyRxError != null) { stats.Warnings.Add($"{catKey}: {familyRxError}"); return; }
            int outsideFamily = 0;

            foreach (var el in elements)
            {
                try
                {
                    if (skipIfTagged && alreadyTagged != null
                        && alreadyTagged.TryGetValue(TaggedHostKey.Local(el.Id.Value), out var onElement)
                        && TagRuleIdentity.ShouldSkip(onElement, isSpecialistRule, placedFamily, specialistFamilies))
                    {
                        stats.Skipped++;
                        continue;
                    }

                    if (familyRx != null)
                    {
                        var et = doc.GetElement(el.GetTypeId()) as ElementType;
                        if (!RuleFamilyFilter.Matches(familyRx, et?.FamilyName, et?.Name))
                        {
                            outsideFamily++;   // out of scope, not a skip: nothing to tag
                            continue;
                        }
                    }

                    if (minSizeMm.HasValue)
                    {
                        bool keep = ElementSize.Keeps(el, view, minSizeMm, out bool noSize);
                        if (noSize) unmeasured++;
                        if (!keep) { belowMin++; stats.Skipped++; continue; }
                    }

                    var pt = GetElementCentre(el);
                    if (pt == null)
                    {
                        // A-9: GetElementCentre's comment promised an XYZ.Zero
                        // sentinel "the caller can detect" but returned null,
                        // and this call site passed it straight into
                        // IndependentTag.Create — one exception per element on
                        // floor / ceiling-heavy categories, flooding the
                        // warning list and tagging nothing. It now falls back
                        // to the bounding-box centre, so those categories tag
                        // properly instead of being skipped; null here means
                        // even that failed.
                        stats.Skipped++;
                        stats.Warnings.Add($"No taggable point for {catKey} element {el.Id} — skipped.");
                        continue;
                    }

                    // Revit 2025 removed IndependentTag.CanTagHost(doc, Reference) +
                    // renamed the Create parameters. The surrounding try/catch
                    // turns any "can't tag this host" failure into a Skipped
                    // count + warning row, replacing the dropped pre-check.
                    Reference hostRef;
                    if (tagsFaces)
                    {
                        hostRef = FaceReferenceFor(el, view, out var facePt);
                        if (hostRef == null) { noFace++; stats.Skipped++; continue; }
                        if (facePt != null) pt = facePt;
                    }
                    else hostRef = new Reference(el);

                    var tag = IndependentTag.Create(doc, tagTypeId, view.Id,
                        hostRef, addLeader, orientation, pt);
                    if (tag != null && leader == TagLeaderMode.Free)
                    {
                        try { tag.LeaderEndCondition = LeaderEndCondition.Free; }
                        catch (Exception exL)
                        {
                            StingLog.WarnRateLimited("AnnotationRunner.FreeLeader",
                                $"Free leader on tag {tag.Id} for {catKey}: {exL.Message} — left attached");
                        }
                    }
                    if (tag != null)
                    {
                        stats.TagsPlaced++;
                        // Keep the index current so a later rule covering the
                        // same element in this run doesn't tag it twice.
                        if (alreadyTagged != null)
                        {
                            var key = TaggedHostKey.Local(el.Id.Value);
                            if (!alreadyTagged.TryGetValue(key, out var fams))
                                alreadyTagged[key] = fams = new List<string>();
                            fams.Add(placedFamily);
                        }
                    }

                    // CategoryDepths are applied by TokenProfileApplier.WriteCategoryDepths
                    // (element TYPE scope, matching the Type binding). The element-scoped
                    // writes that used to sit here could not land — see the note above.
                }
                catch (Exception ex) { stats.Warnings.Add($"TagRule create '{el.Id}': {ex.Message}"); }
            }

            // ── DTW-85: linked elements. Same rule filters; the tag references the
            // element through its link instance and sits at the element's centre
            // mapped into host coordinates. Material tags need a face reference
            // through the link, which this pass does not build — counted, not tried.
            int linkedPlaced = 0, linkedFailed = 0, linkedFaces = 0;
            string firstLinkedFailure = null;
            foreach (var (link, les) in linked)
            {
                if (tagsFaces) { linkedFaces += les.Count; continue; }
                foreach (var le in les)
                {
                    try
                    {
                        var key = TaggedHostKey.Linked(link.Instance.Id.Value, le.Id.Value);
                        if (skipIfTagged && alreadyTagged != null
                            && alreadyTagged.TryGetValue(key, out var onLinked)
                            && TagRuleIdentity.ShouldSkip(onLinked, isSpecialistRule, placedFamily, specialistFamilies))
                        {
                            stats.Skipped++;
                            continue;
                        }
                        if (familyRx != null)
                        {
                            var et = link.Doc.GetElement(le.GetTypeId()) as ElementType;
                            if (!RuleFamilyFilter.Matches(familyRx, et?.FamilyName, et?.Name)) continue;
                        }
                        if (minSizeMm.HasValue)
                        {
                            // No host view for a linked element's own document: measure unviewed.
                            bool keep = ElementSize.Keeps(le, null, minSizeMm, out bool noSize);
                            if (noSize) unmeasured++;
                            if (!keep) { belowMin++; stats.Skipped++; continue; }
                        }

                        var local = GetElementCentre(le);
                        if (local == null) { stats.Skipped++; continue; }
                        var pt = link.Transform.OfPoint(local);
                        var linkRef = new Reference(le).CreateLinkReference(link.Instance);

                        var tag = IndependentTag.Create(doc, tagTypeId, view.Id, linkRef, addLeader, orientation, pt);
                        if (tag == null) { linkedFailed++; firstLinkedFailure ??= $"{link.Name}/{le.Id}: Revit returned no tag"; continue; }
                        if (leader == TagLeaderMode.Free)
                        {
                            try { tag.LeaderEndCondition = LeaderEndCondition.Free; }
                            catch (Exception exL)
                            {
                                StingLog.WarnRateLimited("AnnotationRunner.FreeLeader",
                                    $"Free leader on linked tag {tag.Id} for {catKey}: {exL.Message} — left attached");
                            }
                        }
                        linkedPlaced++;
                        stats.TagsPlaced++;
                        if (alreadyTagged != null)
                        {
                            if (!alreadyTagged.TryGetValue(key, out var fams)) alreadyTagged[key] = fams = new List<string>();
                            fams.Add(placedFamily);
                        }
                    }
                    catch (Exception ex)
                    {
                        linkedFailed++;
                        firstLinkedFailure ??= $"{link.Name}/{le.Id}: {ex.Message}";
                    }
                }
            }
            if (linkedFailed > 0)
                stats.Warnings.Add($"{catKey}: {linkedFailed} linked element(s) could not be tagged — first: {firstLinkedFailure}");
            if (linkedFaces > 0)
                stats.Warnings.Add($"{catKey}: {linkedFaces} element(s) in linked models not given material tags — " +
                                   "material callouts through a link are not supported yet.");
            if (linkedPlaced > 0)
                StingLog.Info($"AnnotationRunner {catKey} in '{view.Name}': {linkedPlaced} linked element(s) tagged.");

            if (belowMin > 0)
                stats.Warnings.Add($"{catKey}: {belowMin} element(s) under minSizeMm {minSizeMm:0.#} not tagged.");
            if (noFace > 0)
                stats.Warnings.Add($"{catKey}: {noFace} element(s) had no face a material tag could reference — not tagged. " +
                                   "Hosts (walls, floors, roofs, ceilings) and plain solids are supported; family instances are not yet.");
            // An empty result under a family filter is the case worth hearing
            // about: the pattern may not match this project's family names.
            if (familyRx != null && outsideFamily > 0 && outsideFamily == elements.Count())
                stats.Warnings.Add($"{catKey}: no element in view matched familyMatch '{rule.FamilyMatch}' " +
                                   $"({outsideFamily} checked) — nothing tagged. Adjust the pattern if this project names them differently.");
            if (unmeasured > 0)
                stats.Warnings.Add($"{catKey}: {unmeasured} element(s) could not be measured for minSizeMm and were tagged anyway.");
        }

        /// <summary>The tag category that tags <paramref name="host"/>. Covers every
        /// category a STING tag family is built for; the previous nine-entry switch
        /// returned the HOST category for walls, pipes, ducts, conduits, trays,
        /// sprinklers and the rest, so the "a tag of this category" fallback could
        /// never find one and the category-match check in ResolveTagTypeId never held.
        /// Unknown hosts still return themselves.</summary>
        internal static BuiltInCategory TagCategoryFor(BuiltInCategory host)
        {
            switch (host)
            {
                case BuiltInCategory.OST_Rooms:                  return BuiltInCategory.OST_RoomTags;
                case BuiltInCategory.OST_Areas:                  return BuiltInCategory.OST_AreaTags;
                case BuiltInCategory.OST_MEPSpaces:              return BuiltInCategory.OST_MEPSpaceTags;
                case BuiltInCategory.OST_Doors:                  return BuiltInCategory.OST_DoorTags;
                case BuiltInCategory.OST_Windows:                return BuiltInCategory.OST_WindowTags;
                case BuiltInCategory.OST_Walls:                  return BuiltInCategory.OST_WallTags;
                case BuiltInCategory.OST_Floors:                 return BuiltInCategory.OST_FloorTags;
                case BuiltInCategory.OST_Ceilings:               return BuiltInCategory.OST_CeilingTags;
                case BuiltInCategory.OST_Roofs:                  return BuiltInCategory.OST_RoofTags;
                case BuiltInCategory.OST_Stairs:                 return BuiltInCategory.OST_StairsTags;
                case BuiltInCategory.OST_StairsRailing:
                case BuiltInCategory.OST_Railings:               return BuiltInCategory.OST_StairsRailingTags;
                case BuiltInCategory.OST_Ramps:                  return BuiltInCategory.OST_RampTags;
                case BuiltInCategory.OST_CurtainWallPanels:      return BuiltInCategory.OST_CurtainWallPanelTags;
                case BuiltInCategory.OST_Casework:               return BuiltInCategory.OST_CaseworkTags;
                case BuiltInCategory.OST_Furniture:              return BuiltInCategory.OST_FurnitureTags;
                case BuiltInCategory.OST_Parking:                return BuiltInCategory.OST_ParkingTags;
                case BuiltInCategory.OST_Site:                   return BuiltInCategory.OST_SiteTags;
                case BuiltInCategory.OST_GenericModel:           return BuiltInCategory.OST_GenericModelTags;
                case BuiltInCategory.OST_SpecialityEquipment:    return BuiltInCategory.OST_SpecialityEquipmentTags;
                case BuiltInCategory.OST_MedicalEquipment:       return BuiltInCategory.OST_MedicalEquipmentTags;
                case BuiltInCategory.OST_StructuralColumns:      return BuiltInCategory.OST_StructuralColumnTags;
                case BuiltInCategory.OST_StructuralFraming:      return BuiltInCategory.OST_StructuralFramingTags;
                case BuiltInCategory.OST_StructuralFoundation:   return BuiltInCategory.OST_StructuralFoundationTags;
                case BuiltInCategory.OST_Rebar:                  return BuiltInCategory.OST_RebarTags;
                case BuiltInCategory.OST_MechanicalEquipment:    return BuiltInCategory.OST_MechanicalEquipmentTags;
                case BuiltInCategory.OST_DuctCurves:             return BuiltInCategory.OST_DuctTags;
                case BuiltInCategory.OST_DuctFitting:            return BuiltInCategory.OST_DuctFittingTags;
                case BuiltInCategory.OST_DuctAccessory:          return BuiltInCategory.OST_DuctAccessoryTags;
                case BuiltInCategory.OST_DuctTerminal:           return BuiltInCategory.OST_DuctTerminalTags;
                case BuiltInCategory.OST_PipeCurves:             return BuiltInCategory.OST_PipeTags;
                case BuiltInCategory.OST_PipeFitting:            return BuiltInCategory.OST_PipeFittingTags;
                case BuiltInCategory.OST_PipeAccessory:          return BuiltInCategory.OST_PipeAccessoryTags;
                case BuiltInCategory.OST_PlumbingFixtures:       return BuiltInCategory.OST_PlumbingFixtureTags;
                case BuiltInCategory.OST_Sprinklers:             return BuiltInCategory.OST_SprinklerTags;
                case BuiltInCategory.OST_ElectricalEquipment:    return BuiltInCategory.OST_ElectricalEquipmentTags;
                case BuiltInCategory.OST_ElectricalFixtures:     return BuiltInCategory.OST_ElectricalFixtureTags;
                case BuiltInCategory.OST_LightingFixtures:       return BuiltInCategory.OST_LightingFixtureTags;
                case BuiltInCategory.OST_LightingDevices:        return BuiltInCategory.OST_LightingDeviceTags;
                case BuiltInCategory.OST_Conduit:                return BuiltInCategory.OST_ConduitTags;
                case BuiltInCategory.OST_ConduitFitting:         return BuiltInCategory.OST_ConduitFittingTags;
                case BuiltInCategory.OST_CableTray:              return BuiltInCategory.OST_CableTrayTags;
                case BuiltInCategory.OST_CableTrayFitting:       return BuiltInCategory.OST_CableTrayFittingTags;
                case BuiltInCategory.OST_FireAlarmDevices:       return BuiltInCategory.OST_FireAlarmDeviceTags;
                case BuiltInCategory.OST_CommunicationDevices:   return BuiltInCategory.OST_CommunicationDeviceTags;
                case BuiltInCategory.OST_DataDevices:            return BuiltInCategory.OST_DataDeviceTags;
                case BuiltInCategory.OST_NurseCallDevices:       return BuiltInCategory.OST_NurseCallDeviceTags;
                case BuiltInCategory.OST_SecurityDevices:        return BuiltInCategory.OST_SecurityDeviceTags;
                default:                                         return host;
            }
        }

        private static ElementId ResolveDimensionStyleId(Document doc, string styleName)
        {
            if (string.IsNullOrWhiteSpace(styleName)) return ElementId.InvalidElementId;
            var dt = new FilteredElementCollector(doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .FirstOrDefault(d => string.Equals(d.Name, styleName, StringComparison.OrdinalIgnoreCase));
            return dt?.Id ?? ElementId.InvalidElementId;
        }

        private static XYZ GetElementCentre(Element el)
        {
            try
            {
                if (el.Location is LocationPoint lp && lp.Point != null) return lp.Point;
                if (el.Location is LocationCurve lc && lc.Curve != null)
                {
                    var c = lc.Curve;
                    return (c.GetEndPoint(0) + c.GetEndPoint(1)) * 0.5;
                }
                // A-9: floors, ceilings, roofs and most system families have no
                // LocationPoint and no LocationCurve, so both branches above
                // miss and this used to return null — which the caller fed
                // straight to IndependentTag.Create. The bounding-box centre is
                // the natural point for those categories and makes them
                // taggable rather than merely non-crashing. XYZ.Zero would have
                // satisfied the old comment but piled every tag at the project
                // origin, which is worse than skipping.
                var bb = el.get_BoundingBox(null);
                if (bb?.Min != null && bb.Max != null)
                    return (bb.Min + bb.Max) * 0.5;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GetElementCentre({el?.Id}): {ex.Message}");
            }
            // Null means "no usable point" — the caller skips and reports.
            return null;
        }

        // ─── Resolution helpers ──────────────────────────────────────────

        /// <summary>
        /// Swap the chosen tag for its size variant for this drawing, when one is
        /// loaded: a family "&lt;base&gt; &lt;n&gt;mm", else a type "&lt;n&gt;mm" in the base
        /// family, nearest available size to DrawingType.EffectiveTagTextSizeMm.
        /// Returns the base unchanged when no variant exists.
        /// </summary>
        private static ElementId ApplyTagSizeVariant(Document doc, ElementId baseTypeId, DrawingType dt,
            string catKey, AnnotationRunStats stats)
        {
            try
            {
                if (!(doc.GetElement(baseTypeId) is FamilySymbol baseSym)) return baseTypeId;
                string baseFam = baseSym.FamilyName;
                var sameCat = baseSym.Category == null
                    ? new List<FamilySymbol>()
                    : SymbolIndexFor(doc).InCategory(baseSym.Category.Id.Value).Select(x => x.Symbol).ToList();

                var famVariants = sameCat
                    .Select(fs => (fs, size: TagSizeVariant.SizeOfFamilyVariant(fs.FamilyName, baseFam)))
                    .Where(x => x.size.HasValue).ToList();
                // Types of the base family that differ from the base type in SIZE ONLY:
                // "2mm" beside "2.5mm", or "2_BOLD_RED_Open30_T2" beside
                // "2.5_BOLD_RED_Open30_T2" — never a type of another style or colour.
                string baseStyle = TagSizeVariant.StyleOfTypeName(baseSym.Name);
                var typeVariants = sameCat
                    .Where(fs => string.Equals(fs.FamilyName, baseFam, StringComparison.OrdinalIgnoreCase)
                              && string.Equals(TagSizeVariant.StyleOfTypeName(fs.Name), baseStyle, StringComparison.OrdinalIgnoreCase))
                    .Select(fs => (fs, size: TagSizeVariant.SizeOfTypeName(fs.Name)))
                    .Where(x => x.size.HasValue).ToList();

                var choice = TagSizeVariant.Choose(dt,
                    famVariants.Select(x => x.size.Value), typeVariants.Select(x => x.size.Value));
                if (choice.Kind == TagSizeVariant.Kind.None) return baseTypeId;

                FamilySymbol pick;
                if (choice.Kind == TagSizeVariant.Kind.Family)
                {
                    var inFamily = famVariants.Where(x => Math.Abs(x.size.Value - choice.SizeMm) < 1e-6).Select(x => x.fs).ToList();
                    // Keep the base's type (e.g. "Standard") if the variant family has it.
                    pick = inFamily.FirstOrDefault(fs => string.Equals(fs.Name, baseSym.Name, StringComparison.OrdinalIgnoreCase))
                        ?? inFamily.FirstOrDefault();
                }
                else
                    pick = typeVariants.Where(x => Math.Abs(x.size.Value - choice.SizeMm) < 1e-6).Select(x => x.fs).FirstOrDefault();

                if (pick == null) return baseTypeId;
                if (Math.Abs(choice.SizeMm - dt.EffectiveTagTextSizeMm()) > 1e-6)
                    stats.Warnings.Add($"{catKey}: no {DrawingType.TagSizeToken(dt.EffectiveTagTextSizeMm())} variant of " +
                                       $"'{baseFam}' is loaded; used the nearest, {DrawingType.TagSizeToken(choice.SizeMm)}.");
                return pick.Id;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ApplyTagSizeVariant {catKey}: {ex.Message}");
                return baseTypeId;
            }
        }

        /// <summary>
        /// The tag type for <paramref name="catKey"/>, memoised for the symbol index's
        /// lifetime. The answer depends only on the pack's family for the category, the
        /// view's drawing-type style for it and the host category, so those (and
        /// catKey, which the warnings name) are the key; the warnings the first
        /// resolution raised are replayed into each caller's stats.
        /// </summary>
        private static ElementId ResolveTagTypeId(Document doc, View view, AnnotationRulePack pack,
            string catKey, BuiltInCategory hostCategory, AnnotationRunStats stats = null)
        {
            string famName = null;
            if (pack.TagFamilies != null && pack.TagFamilies.TryGetValue(catKey, out var fn)
                && !string.IsNullOrWhiteSpace(fn))
                famName = fn;
            string styleName = ViewTagStyleFor(doc, view, hostCategory);

            var index = SymbolIndexFor(doc);
            string key = string.Join("\u001f", catKey ?? "", famName ?? "", styleName ?? "", ((long)hostCategory).ToString());
            if (!index.TagTypeMemo.TryGetValue(key, out var memo))
            {
                var warnings = new AnnotationRunStats();
                var id = ResolveTagTypeIdCore(doc, index, famName, styleName, catKey, hostCategory, warnings);
                memo = (id, warnings.Warnings.ToList());
                index.TagTypeMemo[key] = memo;
            }
            if (stats != null) stats.Warnings.AddRange(memo.Warnings);
            return memo.Id;
        }

        /// <summary>Step 2's input: the CategoryTagStyles entry of the view's drawing
        /// type for this category, or null.</summary>
        private static string ViewTagStyleFor(Document doc, View view, BuiltInCategory hostCategory)
        {
            try
            {
                var dtId2 = view != null ? DrawingTypeStamper.Read(view) : null;
                if (string.IsNullOrEmpty(dtId2)) return null;
                var pack2 = DrawingTypeRegistry.TryGetPack(doc, dtId2);
                if (pack2?.CategoryTagStyles == null) return null;
                // Try the exact category name first, then BuiltInCategory string.
                string catKey2 = Category.GetCategory(doc, hostCategory)?.Name ?? hostCategory.ToString();
                if (!pack2.CategoryTagStyles.TryGetValue(catKey2, out var styleName))
                    pack2.CategoryTagStyles.TryGetValue(hostCategory.ToString(), out styleName);
                return string.IsNullOrEmpty(styleName) ? null : styleName;
            }
            catch { return null; /* resolver must never throw */ }
        }

        private static ElementId ResolveTagTypeIdCore(Document doc, SymbolIndex index, string famName, string styleName,
            string catKey, BuiltInCategory hostCategory, AnnotationRunStats stats)
        {
            ElementId result = ElementId.InvalidElementId;

            // 1. Named tag family from the rule pack
            if (!string.IsNullOrWhiteSpace(famName))
            {
                var named = index.OfFamily(famName).Select(x => x.Symbol).ToList();

                // A-8: this matched by family name across EVERY category, so a
                // model family sharing the name could win and then throw once
                // per element inside IndependentTag.Create. Prefer a symbol in
                // the expected tag category. The name-only match is kept as a
                // second choice because TagCategoryFor falls through to the host
                // category for anything not in its switch, so an exact category
                // match is not always available — but a non-annotation match is
                // reported, since that is the case that fails per element.
                var wantCat = (long)TagCategoryFor(hostCategory);
                var byCat = named.FirstOrDefault(fs => fs.Category != null && fs.Category.Id.Value == wantCat);
                var chosen = byCat ?? named.FirstOrDefault();

                if (chosen != null)
                {
                    if (byCat == null && chosen.Category?.CategoryType != CategoryType.Annotation)
                        stats?.Warnings.Add(
                            $"Tag family '{famName}' for {catKey} is not an annotation family " +
                            $"(category '{chosen.Category?.Name ?? "none"}') — tagging will likely fail.");
                    result = chosen.Id;
                }
                else
                {
                    // Previously silent: a rule naming a family that isn't
                    // loaded fell straight through to "first loaded tag of the
                    // category", so the drawing quietly used the wrong tag.
                    // This is the mechanism that hides the known tagFamilies
                    // debt (~87 key mismatches, 19 nonexistent STING_TAG_*
                    // families) at runtime.
                    stats?.Warnings.Add(
                        $"Tag family '{famName}' declared for {catKey} is not loaded; " +
                        "falling back to the first loaded tag of that category.");
                }
            }

            // 2. CategoryTagStyles: check the active view's DrawingType pack.
            if ((result == null || result == ElementId.InvalidElementId) && !string.IsNullOrEmpty(styleName))
            {
                try
                {
                    // Find any FamilySymbol whose name contains the style preset name.
                    var match = index.All.FirstOrDefault(x =>
                        (x.Name ?? "").IndexOf(styleName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (x.FamilyObjectName ?? "").IndexOf(styleName, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (match != null) result = match.Id;
                }
                catch { /* resolver must never throw */ }
            }

            // 3. The STING family built for this category ("STING - Door Tag"). Before
            //    this, a drawing type with no tagFamilies entry for a category — 50+
            //    shipped AutoTag rules — got whatever tag of the category happened to
            //    load first, usually Revit's stock tag, which does not show the ISO 19650
            //    asset tag. The STING family carries the tag containers the pipeline writes.
            if (result == null || result == ElementId.InvalidElementId)
            {
                string stingName = Tags.TagFamilyConfig.CategoryTemplateMap.ContainsKey(hostCategory)
                    ? Tags.TagFamilyConfig.GetFamilyName(hostCategory) : null;
                if (!string.IsNullOrEmpty(stingName))
                {
                    var wantCat = (long)TagCategoryFor(hostCategory);
                    var sting = index.OfFamily(stingName)
                        .OrderByDescending(x => x.CategoryId == wantCat)
                        .FirstOrDefault();
                    if (sting != null) result = sting.Id;
                }
            }

            // 4. Last resort: the first loaded tag of the host's tag category. Said out loud,
            //    because a non-STING tag does not display the ISO 19650 tag.
            if (result == null || result == ElementId.InvalidElementId)
            {
                var fallback = index.InCategory((long)TagCategoryFor(hostCategory))
                    .Where(x => x.CategoryType == CategoryType.Annotation)
                    .Select(x => x.Symbol)
                    .FirstOrDefault();
                if (fallback != null)
                {
                    result = fallback.Id;
                    stats?.Warnings.Add(
                        $"{catKey}: no STING tag family loaded — used '{fallback.FamilyName}', which may not " +
                        "show the ISO 19650 tag. Load it with Create Tag Families / Load Tag Families.");
                }
            }

            return result ?? ElementId.InvalidElementId;
        }

        // ─── Loaded-symbol index (P4) ─────────────────────────────────────
        //
        // ResolveTagTypeId and ApplyTagSizeVariant used to run a full FamilySymbol
        // collector per category, per rule, per view — and read Category / FamilyName
        // off every symbol each time. The index reads each symbol once and lives for
        // a production batch (DrawingProducer.PrimeBatchCaches opens it, Reset drops
        // it) or for one stand-alone Apply. It is rebuilt when the document or the
        // number of loaded symbols changes (a family loaded or a type created or
        // deleted mid-batch), checked at the start of every Apply.

        [ThreadStatic] private static SymbolIndex _symbolIndex;
        [ThreadStatic] private static bool _symbolBatchActive;

        /// <summary>Open a batch: the symbol index (and the tag-type memo on it) is
        /// kept across Apply calls until <see cref="EndSymbolBatch"/>.</summary>
        internal static void BeginSymbolBatch()
        {
            _symbolIndex = null;
            _symbolBatchActive = true;
        }

        internal static void EndSymbolBatch()
        {
            _symbolBatchActive = false;
            _symbolIndex = null;
        }

        private static void ResetSymbolIndex() => _symbolIndex = null;

        /// <summary>Drop the index if it was built for another document or the set of
        /// loaded symbols has changed size since. GetElementCount on a class filter
        /// does not materialise the symbols.</summary>
        private static void RevalidateSymbolIndex(Document doc)
        {
            var idx = _symbolIndex;
            if (idx == null) return;
            try
            {
                if (!ReferenceEquals(idx.Doc, doc)
                    || new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).GetElementCount() != idx.Count)
                    _symbolIndex = null;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"AnnotationRunner symbol index check: {ex.Message}");
                _symbolIndex = null;
            }
        }

        private static SymbolIndex SymbolIndexFor(Document doc)
        {
            var idx = _symbolIndex;
            if (idx == null || !ReferenceEquals(idx.Doc, doc))
                _symbolIndex = idx = SymbolIndex.Build(doc);
            return idx;
        }

        private sealed class SymbolInfo
        {
            public FamilySymbol Symbol;
            public ElementId Id;
            public string Name;
            public string FamilyName;
            public string FamilyObjectName;   // Family?.Name — step 2 matched on this
            public long? CategoryId;
            public CategoryType? CategoryType;
        }

        private sealed class SymbolIndex
        {
            public Document Doc;
            public int Count;
            public readonly List<SymbolInfo> All = new List<SymbolInfo>();
            private readonly Dictionary<long, List<SymbolInfo>> _byCategory = new Dictionary<long, List<SymbolInfo>>();
            private readonly Dictionary<string, List<SymbolInfo>> _byFamily
                = new Dictionary<string, List<SymbolInfo>>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, (ElementId Id, List<string> Warnings)> TagTypeMemo
                = new Dictionary<string, (ElementId, List<string>)>(StringComparer.Ordinal);
            private static readonly List<SymbolInfo> None = new List<SymbolInfo>();

            public static SymbolIndex Build(Document doc)
            {
                var idx = new SymbolIndex { Doc = doc };
                try
                {
                    foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)))
                    {
                        idx.Count++;
                        if (!(el is FamilySymbol fs)) continue;
                        var info = new SymbolInfo { Symbol = fs, Id = fs.Id };
                        try { info.Name = fs.Name; } catch { info.Name = null; }
                        try { info.FamilyName = fs.FamilyName; } catch { info.FamilyName = null; }
                        try { info.FamilyObjectName = fs.Family?.Name; } catch { info.FamilyObjectName = null; }
                        try
                        {
                            var cat = fs.Category;
                            if (cat != null) { info.CategoryId = cat.Id.Value; info.CategoryType = cat.CategoryType; }
                        }
                        catch { info.CategoryId = null; }
                        idx.All.Add(info);
                        if (info.CategoryId.HasValue)
                        {
                            if (!idx._byCategory.TryGetValue(info.CategoryId.Value, out var cl))
                                idx._byCategory[info.CategoryId.Value] = cl = new List<SymbolInfo>();
                            cl.Add(info);
                        }
                        if (info.FamilyName != null)
                        {
                            if (!idx._byFamily.TryGetValue(info.FamilyName, out var fl))
                                idx._byFamily[info.FamilyName] = fl = new List<SymbolInfo>();
                            fl.Add(info);
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"AnnotationRunner symbol index: {ex.Message}"); }
                return idx;
            }

            public List<SymbolInfo> InCategory(long categoryId)
                => _byCategory.TryGetValue(categoryId, out var l) ? l : None;

            public List<SymbolInfo> OfFamily(string familyName)
                => familyName != null && _byFamily.TryGetValue(familyName, out var l) ? l : None;

            /// <summary>As FindFamilySymbolByName: first symbol whose type or family
            /// name matches, in collector order.</summary>
            public FamilySymbol FindByName(string name)
                => All.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(x.FamilyName, name, StringComparison.OrdinalIgnoreCase))?.Symbol;
        }

        private static FamilySymbol FindFamilySymbolByName(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(s.FamilyName, name, StringComparison.OrdinalIgnoreCase));
        }

        // ── Decorative annotation: north arrow / scale bar / key plan / matchlines ──

        private static void RunDecorativeAnnotation(Document doc, View view, AnnotationRulePack pack, AnnotationRunOptions opts, AnnotationResult result)
        {
            BoundingBoxXYZ outline = null;
            try { outline = view.CropBox; } catch { }
            if (outline == null)
            {
                try
                {
                    var ol = view.Outline;
                    if (ol != null)
                    {
                        outline = new BoundingBoxXYZ
                        {
                            Min = new XYZ(ol.Min.U, ol.Min.V, 0),
                            Max = new XYZ(ol.Max.U, ol.Max.V, 0)
                        };
                    }
                }
                catch { }
            }
            if (outline == null) return;

            PlaceDecorativeIfDeclared(doc, view, pack.NorthArrowFamily, pack.NorthArrowPosition,
                pack.NorthArrowSizeMm, outline, result);
            PlaceDecorativeIfDeclared(doc, view, pack.ScaleBarFamily, pack.ScaleBarPosition,
                null, outline, result);
            PlaceDecorativeIfDeclared(doc, view, pack.KeyPlanFamily, pack.KeyPlanPosition,
                null, outline, result);

            if (pack.MatchlineOffsetMm.HasValue && view is ViewPlan vp && vp.CropBoxActive)
            {
                try
                {
                    // DTW-130: the frame is stamped, so a re-run finds it and draws no second one.
                    if (Storage.StingAnnotationProvenanceSchema.Index(doc, view, typeof(CurveElement),
                            AnnotationProvenance.DecoMatchlineFrame).Count > 0)
                    {
                        result.Skipped++;
                        return;
                    }
                    var inset = pack.MatchlineOffsetMm.Value / 304.8;
                    var min = outline.Min;
                    var max = outline.Max;
                    var p00 = new XYZ(min.X + inset, min.Y + inset, 0);
                    var p10 = new XYZ(max.X - inset, min.Y + inset, 0);
                    var p11 = new XYZ(max.X - inset, max.Y - inset, 0);
                    var p01 = new XYZ(min.X + inset, max.Y - inset, 0);
                    int side = 0;
                    foreach (var (a, b) in new[] { (p00, p10), (p10, p11), (p11, p01), (p01, p00) })
                    {
                        side++;
                        try
                        {
                            var dc = doc.Create.NewDetailCurve(view, Line.CreateBound(a, b));
                            if (!Storage.StingAnnotationProvenanceSchema.Stamp(dc, AnnotationProvenance.DecoMatchlineFrame,
                                    AnnotationProvenance.Key(view.UniqueId, "side" + side)))
                                result.Warnings.Add("Matchline frame line not stamped — a re-run may draw it again.");
                        }
                        catch (Exception ex) { result.Warnings.Add("Matchline detail curve: " + ex.Message); }
                    }
                }
                catch (Exception ex) { result.Warnings.Add("Matchline pass: " + ex.Message); }
            }
        }

        private static void PlaceDecorativeIfDeclared(Document doc, View view, string familyName, string position, double? sizeMm, BoundingBoxXYZ outline, AnnotationResult result)
        {
            if (string.IsNullOrEmpty(familyName)) return;
            try
            {
                var sym = FindFamilySymbolByName(doc, familyName);
                if (sym == null) { result.Warnings.Add($"Decorative family '{familyName}' not found — skipped."); return; }

                // C-4: the decorative pass had no idempotency either, so every
                // re-run stacked another north arrow / scale bar / key plan on
                // the view. One instance of a given family per view is the
                // whole intent, so an existing instance is the guard.
                if (ViewHasInstanceOfFamily(doc, view, sym))
                {
                    result.Skipped++;
                    return;
                }

                if (!sym.IsActive) { try { sym.Activate(); } catch { } }
                var inset = (sizeMm ?? 0) / 304.8;
                var pt = ResolvePositionPoint(outline, position, inset);
                doc.Create.NewFamilyInstance(pt, sym, view);
                result.DecorativePlaced++;
            }
            catch (Exception ex) { result.Warnings.Add($"Decorative '{familyName}': {ex.Message}"); }
        }

        /// <summary>
        /// True when this view already owns an instance of the given family
        /// symbol's family — the guard that keeps the decorative pass from
        /// re-stacking a north arrow / scale bar / key plan on every run.
        /// Matched on family rather than symbol so a type swap still counts.
        /// </summary>
        private static bool ViewHasInstanceOfFamily(Document doc, View view, FamilySymbol sym)
        {
            if (doc == null || view == null || sym == null) return false;
            try
            {
                var famId = sym.Family?.Id;
                if (famId == null) return false;
                foreach (var el in new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(FamilyInstance))
                    .WhereElementIsNotElementType())
                {
                    if (el is FamilyInstance fi && fi.Symbol?.Family?.Id == famId) return true;
                }
            }
            catch (Exception ex)
            {
                // Fail open: place it rather than silently omit a required
                // north arrow because the scan failed.
                StingLog.Warn($"ViewHasInstanceOfFamily('{sym.Name}'): {ex.Message}");
            }
            return false;
        }

        private static XYZ ResolvePositionPoint(BoundingBoxXYZ outline, string position, double inset)
        {
            if (outline == null) return XYZ.Zero;
            var min = outline.Min;
            var max = outline.Max;
            switch ((position ?? "BottomLeft").Trim())
            {
                case "BottomRight": return new XYZ(max.X - inset, min.Y + inset, 0);
                case "TopLeft":     return new XYZ(min.X + inset, max.Y - inset, 0);
                case "TopRight":    return new XYZ(max.X - inset, max.Y - inset, 0);
                default:            return new XYZ(min.X + inset, min.Y + inset, 0);
            }
        }

        // ── Spot annotation rules ──

        private static void RunSpotAnnotation(Document doc, View view, AnnotationRulePack pack, AnnotationRunOptions opts, AnnotationResult result)
        {
            ProcessSpotRules(doc, view, pack.SpotElevationRules, isCoordinate: false, result);
            ProcessSpotRules(doc, view, pack.SpotCoordinateRules, isCoordinate: true, result);
        }

        /// <summary>
        /// Element ids already carrying a spot elevation — or a spot coordinate,
        /// when <paramref name="isCoordinate"/> is true — in this view. Built
        /// once per kind and shared across every spot rule: the guard that keeps
        /// the spot pass from re-stacking a SpotDimension on every run. Like the
        /// dimension guard (ViewHasDimensionReferencing) it detects an existing
        /// spot by what it REFERENCES; a spot whose references are unavailable is
        /// treated as unknown rather than a match. Fails open with a warning so a
        /// scan failure places spots (previous behaviour) instead of silently
        /// omitting them.
        /// </summary>
        private static HashSet<ElementId> BuildSpottedElementIndex(Document doc, View view, bool isCoordinate, AnnotationResult result)
        {
            var set = new HashSet<ElementId>();
            try
            {
                var bic = isCoordinate ? BuiltInCategory.OST_SpotCoordinates : BuiltInCategory.OST_SpotElevations;
                foreach (var el in new FilteredElementCollector(doc, view.Id)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType())
                {
                    if (!(el is SpotDimension sd)) continue;
                    try
                    {
                        if (!sd.AreReferencesAvailable) continue;   // unknown — not a match
                        var refs = sd.References;
                        if (refs == null) continue;
                        foreach (Reference r in refs)
                        {
                            var host = doc.GetElement(r);
                            if (host != null && host.Id != ElementId.InvalidElementId) set.Add(host.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"BuildSpottedElementIndex: spot {sd.Id} — {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Fail open: an empty index means "place every spot", i.e. the
                // previous behaviour, rather than silently skipping requested work.
                result.Warnings.Add($"Could not index existing spot {(isCoordinate ? "coordinates" : "elevations")} " +
                                    $"in '{view.Name}' ({ex.Message}); duplicate spot dims are possible on this view.");
            }
            return set;
        }

        private static void ProcessSpotRules(Document doc, View view, List<SpotAnnotationRule> rules, bool isCoordinate, AnnotationResult result)
        {
            if (rules == null || rules.Count == 0) return;

            // C-4: index elements that already carry a spot dimension of this
            // kind in the view, once and shared across every rule below, so a
            // re-run doesn't stack a second SpotElevation / SpotCoordinate onto
            // each element. The tag, dimension and decorative passes each got an
            // idempotency guard; the spot pass was the one kind left uncovered,
            // so re-running DrawingTypePresentation.Apply doubled every spot dim.
            var spotted = BuildSpottedElementIndex(doc, view, isCoordinate, result);

            foreach (var r in rules)
            {
                if (r == null || string.IsNullOrEmpty(r.Category)) continue;
                try
                {
                    var catId = ResolveCategoryId(doc, r.Category);
                    if (catId == ElementId.InvalidElementId)
                    {
                        result.Warnings.Add($"Spot rule category '{r.Category}' not found.");
                        continue;
                    }

                    ElementId symbolId = ElementId.InvalidElementId;
                    if (!string.IsNullOrEmpty(r.SymbolFamily))
                    {
                        var s = FindFamilySymbolByName(doc, r.SymbolFamily);
                        if (s != null) symbolId = s.Id;
                    }

                    var elements = new FilteredElementCollector(doc, view.Id)
                        .OfCategoryId(catId)
                        .WhereElementIsNotElementType()
                        .ToList();

                    bool hasLeader = !string.Equals(r.LeaderStyle, "NoLeader", StringComparison.OrdinalIgnoreCase);

                    foreach (var el in elements)
                    {
                        try
                        {
                            // C-4: skip an element that already carries a spot
                            // dim of this kind so a re-run places nothing.
                            if (spotted != null && spotted.Contains(el.Id))
                            {
                                result.Skipped++;
                                continue;
                            }

                            var bb = el.get_BoundingBox(view);
                            if (bb == null) continue;
                            var origin = new XYZ((bb.Min.X + bb.Max.X) / 2.0, (bb.Min.Y + bb.Max.Y) / 2.0, bb.Max.Z);
                            var bend = origin + new XYZ(1, 1, 0);
                            var end  = origin + new XYZ(2, 1, 0);
                            var refPt= origin;
                            var faceRef = new Reference(el);
                            SpotDimension sd = isCoordinate
                                ? doc.Create.NewSpotCoordinate(view, faceRef, origin, bend, end, refPt, hasLeader)
                                : doc.Create.NewSpotElevation(view, faceRef, origin, bend, end, refPt, hasLeader);
                            if (symbolId != ElementId.InvalidElementId && sd != null)
                            {
                                // H-4 — was a silent catch. ChangeTypeId THROWS, so the
                                // exception is the signal here. A swallowed failure
                                // leaves the spot dimension placed but carrying the
                                // WRONG symbol type — and result.SpotsPlaced++ on the
                                // next line still counts it as a success. That is the
                                // exact shape this batch keeps finding: the count says
                                // done, the drawing says otherwise.
                                SafeWrite.Try(() => sd.ChangeTypeId(symbolId),
                                    "AnnotationRunner.Spot",
                                    $"spot {(isCoordinate ? "coordinate" : "elevation")} symbol type",
                                    result?.Warnings);
                            }
                            result.SpotsPlaced++;
                            // Keep the index current so a later rule of the same
                            // kind in this run doesn't re-spot the same element.
                            spotted?.Add(el.Id);
                        }
                        catch (Exception ex) { result.Warnings.Add($"Spot {(isCoordinate ? "coord" : "elev")} '{r.Category}/{el.Id}': {ex.Message}"); }
                    }
                }
                catch (Exception ex) { result.Warnings.Add($"Spot rules '{r.Category}': {ex.Message}"); }
            }
        }

        // ── Resolve helpers ──

        private static ElementId ResolveCategoryId(Document doc, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return ElementId.InvalidElementId;
            try
            {
                if (Enum.TryParse<BuiltInCategory>(key, true, out var bic))
                {
                    var c = Category.GetCategory(doc, bic);
                    if (c != null) return c.Id;
                }
                foreach (Category c in doc.Settings.Categories)
                    if (string.Equals(c.Name, key, StringComparison.OrdinalIgnoreCase))
                        return c.Id;
            }
            catch { }
            return ElementId.InvalidElementId;
        }
    }
}
