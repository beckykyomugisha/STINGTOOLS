// StingTools — Drawing Template Manager · Phase 137
//
// AnnotationRunner consumes an AnnotationRulePack and runs four
// passes against a single View, in order:
//
//   1. Tag rules    — IndependentTag.Create per resolved rule
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

    public static class AnnotationRunner
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
                    try { TagByRules(doc, view, pack, stats); }
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
        private static void TagByRules(Document doc, View view, AnnotationRulePack pack, AnnotationRunStats stats)
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
            var taggedIndex = new Lazy<HashSet<ElementId>>(() => BuildTaggedElementIndex(doc, view, stats));

            var doneCats = new HashSet<long>(); // tag each category at most once
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
                    var effCat = AnnotationRuleKinds.EffectiveCategory(rule.RuleType, rule.Category);
                    var catId = ResolveCategoryId(doc, effCat);
                    if (catId == ElementId.InvalidElementId)
                    {
                        stats.Warnings.Add($"Tag rule '{rule.RuleType}': category '{effCat}' not found in this document — skipped.");
                        continue;
                    }
                    long cv = catId.Value;
                    if (!doneCats.Add(cv)) continue;
                    // BuiltInCategory's underlying type is long (Revit 2024+), so
                    // handing Enum.IsDefined an int threw "Enum underlying type
                    // and the object must be same type" for EVERY rule — the
                    // per-rule catch below swallowed it as a warning, so the
                    // whole auto-tag pass silently placed nothing. Pass the long.
                    if (!Enum.IsDefined(typeof(BuiltInCategory), cv)) continue; // skip custom categories
                    TagCategory(doc, view, pack, (BuiltInCategory)cv, effCat, stats, rule, taggedIndex.Value);
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
                            var host = doc.GetElement(r);
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
            if (ViewHasDimensionReferencing(doc, view, BuiltInCategory.OST_Grids))
            {
                stats.Skipped++;
                return;
            }

            var grids = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Grids)
                .WhereElementIsNotElementType()
                .Cast<Grid>()
                .ToList();
            if (grids.Count < 2) return;

            // A-4: split by orientation. Every grid used to go into ONE
            // ReferenceArray with a dimension line running between the end
            // points of the first and last grid in COLLECTOR order — arbitrary
            // in both direction and position. A dimension can only measure
            // mutually parallel references, so on any project with orthogonal
            // grids (i.e. essentially all of them) NewDimension threw and the
            // per-view catch swallowed it: grid auto-dimensioning never once
            // succeeded on a real model.
            //
            // Each parallel set now gets its own chain, on a line PERPENDICULAR
            // to that set — which is the only orientation that can measure the
            // spacing between them — placed just outside the grid extent.
            var eastWest = new List<Grid>();   // run along X; spaced along Y
            var northSouth = new List<Grid>(); // run along Y; spaced along X
            double zPlane = 0; bool haveZ = false;
            double xMin = double.MaxValue, xMax = double.MinValue;
            double yMin = double.MaxValue, yMax = double.MinValue;

            foreach (var g in grids)
            {
                var line = g.Curve as Line;
                if (line == null) continue;    // arc grids cannot join a linear chain
                var d = line.Direction;
                var a = line.GetEndPoint(0);
                var b = line.GetEndPoint(1);
                if (!haveZ) { zPlane = a.Z; haveZ = true; }
                xMin = Math.Min(xMin, Math.Min(a.X, b.X)); xMax = Math.Max(xMax, Math.Max(a.X, b.X));
                yMin = Math.Min(yMin, Math.Min(a.Y, b.Y)); yMax = Math.Max(yMax, Math.Max(a.Y, b.Y));
                if (RunsEastWest(d.X, d.Y)) eastWest.Add(g); else northSouth.Add(g);
            }
            if (!haveZ) return;

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

            double marginFt = 10.0;   // ~3 m clear of the grid extent

            // East-west grids are stacked along Y, so their chain runs along Y,
            // offset beyond the eastern extent.
            PlaceGridChain(doc, view, eastWest, dimStyleId, stats, "east-west",
                positionOf: g => ((Line)g.Curve).Origin.Y,
                pointAt: (pos, off) => new XYZ(xMax + off, pos, zPlane),
                marginFt: marginFt);

            // North-south grids are stacked along X, so their chain runs along
            // X, offset beyond the northern extent.
            PlaceGridChain(doc, view, northSouth, dimStyleId, stats, "north-south",
                positionOf: g => ((Line)g.Curve).Origin.X,
                pointAt: (pos, off) => new XYZ(pos, yMax + off, zPlane),
                marginFt: marginFt);
        }

        /// <summary>
        /// Orientation test for a grid line: true when the curve runs
        /// predominantly along model X (an "east-west" grid on plan), which
        /// means the set is spaced along Y and must be dimensioned by a chain
        /// running along Y.
        /// Revit-free so the classification is testable; ties (|dx| == |dy|,
        /// a 45-degree grid) resolve to east-west deterministically rather
        /// than by collector order.
        /// </summary>
        internal static bool RunsEastWest(double dirX, double dirY)
            => Math.Abs(dirX) >= Math.Abs(dirY);

        /// <summary>
        /// Span of a parallel grid set along its spacing axis, widened by a
        /// margin. Returns false when there are fewer than two DISTINCT
        /// positions — coincident grids cannot be dimensioned and produced a
        /// zero-length dimension line.
        /// Revit-free so the degenerate cases are testable.
        /// </summary>
        internal static bool TryGridSpan(IReadOnlyList<double> positions, double marginFt,
            out double lo, out double hi)
        {
            lo = hi = 0;
            if (positions == null || positions.Count < 2) return false;
            double mn = double.MaxValue, mx = double.MinValue;
            foreach (var p in positions) { if (p < mn) mn = p; if (p > mx) mx = p; }
            if (mx - mn < 1e-6) return false;
            lo = mn - marginFt;
            hi = mx + marginFt;
            return true;
        }

        private static void PlaceGridChain(Document doc, View view, List<Grid> set,
            ElementId dimStyleId, AnnotationRunStats stats, string label,
            Func<Grid, double> positionOf, Func<double, double, XYZ> pointAt, double marginFt)
        {
            if (set == null || set.Count < 2) return;
            try
            {
                var positions = set.Select(positionOf).ToList();
                if (!TryGridSpan(positions, marginFt, out double lo, out double hi))
                {
                    stats.Warnings.Add($"Grid dim ({label}): grids are coincident — no chain placed.");
                    return;
                }

                var refs = new ReferenceArray();
                foreach (var g in set)
                {
                    try { refs.Append(new Reference(g)); }
                    catch (Exception ex) { StingLog.Warn($"Grid ref {g.Id}: {ex.Message}"); }
                }
                if (refs.Size < 2) return;

                var dimLine = Line.CreateBound(pointAt(lo, marginFt), pointAt(hi, marginFt));
                var dim = (dimStyleId == null || dimStyleId == ElementId.InvalidElementId)
                    ? doc.Create.NewDimension(view, dimLine, refs)
                    : doc.Create.NewDimension(view, dimLine, refs, (DimensionType)doc.GetElement(dimStyleId));
                if (dim != null) stats.DimsCreated++;
            }
            catch (Exception ex) { stats.Warnings.Add($"Grid dim ({label}): {ex.Message}"); }
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
            if (ViewHasDimensionReferencing(doc, view, BuiltInCategory.OST_Levels))
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
                if (dim != null) stats.DimsCreated++;
            }
            catch (Exception ex) { stats.Warnings.Add("Level dim: " + ex.Message); }
        }

        // ─── Tagging ─────────────────────────────────────────────────────

        // TagCategory walks every element of a category visible in the view
        // and drops an IndependentTag at its centre (tag family: rule, then
        // pack TagFamilies, then first loaded). Depth is NOT written here -- see
        // TagDepthLayering / TokenProfileApplier.WriteCategoryDepths.

        /// <summary>
        /// Element ids already carrying an IndependentTag in this view.
        /// Built once per view and shared across every tag rule.
        /// </summary>
        private static HashSet<ElementId> BuildTaggedElementIndex(Document doc, View view, AnnotationRunStats stats)
        {
            var set = new HashSet<ElementId>();
            try
            {
                foreach (var el in new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag))
                    .WhereElementIsNotElementType())
                {
                    if (!(el is IndependentTag tag)) continue;
                    try
                    {
                        foreach (var id in tag.GetTaggedLocalElementIds())
                            if (id != null && id != ElementId.InvalidElementId) set.Add(id);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"BuildTaggedElementIndex: tag {tag.Id} — {ex.Message}");
                    }
                }
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
            AutoAnnotationRule rule = null, HashSet<ElementId> alreadyTagged = null)
        {
            var elements = new FilteredElementCollector(doc, view.Id)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToElements();
            if (elements.Count == 0) return;

            // B1: a per-rule tagFamily wins over the pack-level TagFamilies map.
            // AutoAnnotationRule.TagFamily was declared and read nowhere, so a rule
            // naming its own tag family was silently served the pack default.
            ElementId tagTypeId = ElementId.InvalidElementId;
            if (!string.IsNullOrWhiteSpace(rule?.TagFamily))
            {
                var byRule = FindFamilySymbolByName(doc, rule.TagFamily);
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

            foreach (var el in elements)
            {
                try
                {
                    if (skipIfTagged && alreadyTagged != null && alreadyTagged.Contains(el.Id))
                    {
                        stats.Skipped++;
                        continue;
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
                    var tag = IndependentTag.Create(doc, tagTypeId, view.Id,
                        new Reference(el), addLeader, orientation, pt);
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
                        alreadyTagged?.Add(el.Id);
                    }

                    // CategoryDepths are applied by TokenProfileApplier.WriteCategoryDepths
                    // (element TYPE scope, matching the Type binding). The element-scoped
                    // writes that used to sit here could not land — see the note above.
                }
                catch (Exception ex) { stats.Warnings.Add($"TagRule create '{el.Id}': {ex.Message}"); }
            }

            if (belowMin > 0)
                stats.Warnings.Add($"{catKey}: {belowMin} element(s) under minSizeMm {minSizeMm:0.#} not tagged.");
            if (unmeasured > 0)
                stats.Warnings.Add($"{catKey}: {unmeasured} element(s) could not be measured for minSizeMm and were tagged anyway.");
        }

        private static BuiltInCategory TagCategoryFor(BuiltInCategory host)
        {
            switch (host)
            {
                case BuiltInCategory.OST_Rooms:                return BuiltInCategory.OST_RoomTags;
                case BuiltInCategory.OST_Doors:                return BuiltInCategory.OST_DoorTags;
                case BuiltInCategory.OST_Windows:              return BuiltInCategory.OST_WindowTags;
                case BuiltInCategory.OST_MechanicalEquipment:  return BuiltInCategory.OST_MechanicalEquipmentTags;
                case BuiltInCategory.OST_ElectricalEquipment:  return BuiltInCategory.OST_ElectricalEquipmentTags;
                case BuiltInCategory.OST_PlumbingFixtures:     return BuiltInCategory.OST_PlumbingFixtureTags;
                case BuiltInCategory.OST_LightingFixtures:     return BuiltInCategory.OST_LightingFixtureTags;
                case BuiltInCategory.OST_PipeFitting:          return BuiltInCategory.OST_PipeFittingTags;
                case BuiltInCategory.OST_StructuralFraming:    return BuiltInCategory.OST_StructuralFramingTags;
                default:                                       return host;
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

        private static ElementId ResolveTagTypeId(Document doc, View view, AnnotationRulePack pack,
            string catKey, BuiltInCategory hostCategory, AnnotationRunStats stats = null)
        {
            ElementId result = ElementId.InvalidElementId;

            // 1. Named tag family from the rule pack
            if (pack.TagFamilies != null && pack.TagFamilies.TryGetValue(catKey, out var famName)
                && !string.IsNullOrWhiteSpace(famName))
            {
                var named = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(fs => string.Equals(fs.FamilyName, famName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

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

            // 2. First loaded tag of the host's tag category (project default)
            if (result == null || result == ElementId.InvalidElementId)
            {
                var fallback = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(fs => fs.Category != null
                        && fs.Category.CategoryType == CategoryType.Annotation
                        && fs.Category.Id.Value == (long)TagCategoryFor(hostCategory));
                if (fallback != null) result = fallback.Id;
            }

            // 3. CategoryTagStyles fallback: check the active view's DrawingType pack.
            if (result == null || result == ElementId.InvalidElementId)
            {
                try
                {
                    var dtId2 = view != null ? DrawingTypeStamper.Read(view) : null;
                    if (!string.IsNullOrEmpty(dtId2))
                    {
                        var pack2 = DrawingTypeRegistry.TryGetPack(doc, dtId2);
                        if (pack2?.CategoryTagStyles != null)
                        {
                            // Try the exact category name first, then BuiltInCategory string.
                            string catKey2 = Category.GetCategory(doc, hostCategory)?.Name ?? hostCategory.ToString();
                            if (!pack2.CategoryTagStyles.TryGetValue(catKey2, out var styleName))
                                pack2.CategoryTagStyles.TryGetValue(hostCategory.ToString(), out styleName);

                            if (!string.IsNullOrEmpty(styleName))
                            {
                                // Find any FamilySymbol whose name contains the style preset name.
                                var match = new FilteredElementCollector(doc)
                                    .OfClass(typeof(FamilySymbol))
                                    .Cast<FamilySymbol>()
                                    .FirstOrDefault(fs =>
                                        fs.Name.IndexOf(styleName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        (fs.Family?.Name ?? "").IndexOf(styleName, StringComparison.OrdinalIgnoreCase) >= 0);
                                if (match != null) result = match.Id;
                            }
                        }
                    }
                }
                catch { /* resolver must never throw */ }
            }

            return result ?? ElementId.InvalidElementId;
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
                    var inset = pack.MatchlineOffsetMm.Value / 304.8;
                    var min = outline.Min;
                    var max = outline.Max;
                    var p00 = new XYZ(min.X + inset, min.Y + inset, 0);
                    var p10 = new XYZ(max.X - inset, min.Y + inset, 0);
                    var p11 = new XYZ(max.X - inset, max.Y - inset, 0);
                    var p01 = new XYZ(min.X + inset, max.Y - inset, 0);
                    foreach (var (a, b) in new[] { (p00, p10), (p10, p11), (p11, p01), (p01, p00) })
                    {
                        try { doc.Create.NewDetailCurve(view, Line.CreateBound(a, b)); }
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
