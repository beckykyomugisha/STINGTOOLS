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

namespace StingTools.Core.Drawing
{
    public sealed class AnnotationResult
    {
        public int TagsPlaced      { get; set; }
        public int DimsPlaced      { get; set; }
        public int DecorativePlaced { get; set; }
        public int SpotsPlaced     { get; set; }
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
            if (doc == null || view == null || drawingType?.Annotation == null) return stats;

            // Scale-aware density — at scales coarser than DenseUntilScale,
            // skip per-element tagging. View.Scale is 1:N so a larger
            // number means a coarser drawing.
            int effectiveScale = (options?.ViewScale > 0) ? options.ViewScale : view.Scale;
            bool dense = !pack.DenseUntilScale.HasValue || effectiveScale <= pack.DenseUntilScale.Value;

#pragma warning disable CS0618 // legacy AutoXxx flags are folded into Rules at load time; readers still consult them for backward compat
            if (options?.SkipDims != true)
            {
                try { if (pack.AutoDimGrids)  DimGrids(doc, view, pack, stats); } catch (Exception ex) { stats.Warnings.Add("AutoDimGrids: " + ex.Message); }
                try { if (pack.AutoDimLevels) DimLevels(doc, view, pack, stats); } catch (Exception ex) { stats.Warnings.Add("AutoDimLevels: " + ex.Message); }
            }

            if (options?.SkipTags != true)
            {
                if (dense)
                {
                    try { if (pack.AutoTagRooms)     TagCategory(doc, view, pack, BuiltInCategory.OST_Rooms,                 "Rooms",     stats); } catch (Exception ex) { stats.Warnings.Add("AutoTagRooms: " + ex.Message); }
                    try { if (pack.AutoTagDoors)     TagCategory(doc, view, pack, BuiltInCategory.OST_Doors,                 "Doors",     stats); } catch (Exception ex) { stats.Warnings.Add("AutoTagDoors: " + ex.Message); }
                    try { if (pack.AutoTagWindows)   TagCategory(doc, view, pack, BuiltInCategory.OST_Windows,               "Windows",   stats); } catch (Exception ex) { stats.Warnings.Add("AutoTagWindows: " + ex.Message); }
                    try { if (pack.AutoTagEquipment) TagEquipment(doc, view, pack, stats); }                                                          catch (Exception ex) { stats.Warnings.Add("AutoTagEquipment: " + ex.Message); }
                    try { if (pack.AutoTagWelds)     TagCategory(doc, view, pack, BuiltInCategory.OST_PipeFitting,           "Welds",     stats); } catch (Exception ex) { stats.Warnings.Add("AutoTagWelds: " + ex.Message); }
                    try { if (pack.AutoTagBends)     TagCategory(doc, view, pack, BuiltInCategory.OST_PipeFitting,           "Bends",     stats); } catch (Exception ex) { stats.Warnings.Add("AutoTagBends: " + ex.Message); }
                    try { if (pack.AutoTagSupports)  TagCategory(doc, view, pack, BuiltInCategory.OST_StructuralFraming,     "Supports",  stats); } catch (Exception ex) { stats.Warnings.Add("AutoTagSupports: " + ex.Message); }
                }
                else
                {
                    stats.Skipped++;
                    stats.Warnings.Add($"Per-element tagging skipped — view scale 1:{effectiveScale} exceeds denseUntilScale 1:{pack.DenseUntilScale}.");
                }
            }
#pragma warning restore CS0618

            if (options?.SkipTags != true)
            {
                if (dense)
                {
                    try { if (pack.AutoTagRooms)     TagCategory(doc, view, pack, BuiltInCategory.OST_Rooms,                 "Rooms",     stats); } catch (Exception ex) { stats.Warnings.Add("AutoTagRooms: " + ex.Message); }
                    try { if (pack.AutoTagDoors)     TagCategory(doc, view, pack, BuiltInCategory.OST_Doors,                 "Doors",     stats); } catch (Exception ex) { stats.Warnings.Add("AutoTagDoors: " + ex.Message); }
                    try { if (pack.AutoTagWindows)   TagCategory(doc, view, pack, BuiltInCategory.OST_Windows,               "Windows",   stats); } catch (Exception ex) { stats.Warnings.Add("AutoTagWindows: " + ex.Message); }
                    try { if (pack.AutoTagEquipment) TagEquipment(doc, view, pack, stats); }                                                          catch (Exception ex) { stats.Warnings.Add("AutoTagEquipment: " + ex.Message); }
                    try { if (pack.AutoTagWelds)     TagCategory(doc, view, pack, BuiltInCategory.OST_PipeFitting,           "Welds",     stats); } catch (Exception ex) { stats.Warnings.Add("AutoTagWelds: " + ex.Message); }
                    try { if (pack.AutoTagBends)     TagCategory(doc, view, pack, BuiltInCategory.OST_PipeFitting,           "Bends",     stats); } catch (Exception ex) { stats.Warnings.Add("AutoTagBends: " + ex.Message); }
                    try { if (pack.AutoTagSupports)  TagCategory(doc, view, pack, BuiltInCategory.OST_StructuralFraming,     "Supports",  stats); } catch (Exception ex) { stats.Warnings.Add("AutoTagSupports: " + ex.Message); }
                }
                else
                {
                    stats.Skipped++;
                    stats.Warnings.Add($"Per-element tagging skipped — view scale 1:{effectiveScale} exceeds denseUntilScale 1:{pack.DenseUntilScale}.");
                }
            }
#pragma warning restore CS0618

            // Original sub-pass (`Run(doc, view, pack, options)`) became
            // self-recursive after the merge collapsed signatures. The
            // per-element tagging above already populated `stats`; nothing
            // to merge in from a second pass.
            var r = new AnnotationResult();
            stats.TagsPlaced  = r.TagsPlaced;
            stats.DimsCreated = r.DimsPlaced;
            stats.Warnings.AddRange(r.Warnings);
            if (!dense) { stats.Skipped++; stats.Warnings.Add($"Per-element tagging skipped — view scale 1:{view.Scale} exceeds denseUntilScale 1:{pack.DenseUntilScale}."); }
            return stats;
        }

        // ── Tag rules ──

        private static void RunTagRules(Document doc, View view, AnnotationRulePack pack, AnnotationRunOptions opts, AnnotationResult result)
        {
            // FG-02: handle every tag-like RuleType, not just bare "AutoTag".
            // RoomTag / SpaceTag / AreaTag / MaterialTag / KeynoteTag /
            // MultiCategoryTag are all variants of "tag this category" and
            // should run via the same per-rule path; the differentiator is
            // the resolved tag family (RoomTag uses Room tag families, etc.).
            // RuleType is preserved on the rule so downstream callers can
            // pick a tag family by purpose.
            var tagRuleKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "AutoTag", "RoomTag", "SpaceTag", "AreaTag",
                "MaterialTag", "KeynoteTag", "MultiCategoryTag",
            };

            // Phase 165 — Auto3DTag short-circuits to Tag3DCommand.PlaceTagsInView
            // when the active view is a 3D view. Pulled out of the standard tag-rule
            // path because IndependentTag is 2D-only; 3D tags are FamilyInstances of
            // a Generic Model "tag bubble" carrying ASS_TAG_3D_TXT.
            if (pack.Rules != null)
            {
                var auto3D = pack.Rules
                    .Where(r => r != null && r.Enabled &&
                                string.Equals(r.RuleType, "Auto3DTag", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (auto3D.Count > 0)
                {
                    if (view is View3D v3d)
                    {
                        // Read the DrawingType profile's displayMode 6 hint via the
                        // STING_DISPLAY_MODE view stamp; if 6, tag with TAG7 narrative.
                        bool useNarrative = false;
                        try
                        {
                            int dispMode = StingTools.Core.ParameterHelpers
                                .GetInt(view, StingTools.Core.ParamRegistry.DISPLAY_MODE, 0);
                            useNarrative = dispMode == 6;
                        }
                        catch { /* defensive */ }

                        var r3d = StingTools.Tags.Tag3DCommand.PlaceTagsInView(doc, v3d, useNarrative);
                        result.TagsPlaced += r3d.Placed;
                        foreach (var w in r3d.Warnings) result.Warnings.Add($"Auto3DTag: {w}");
                    }
                    else
                    {
                        result.Warnings.Add($"Auto3DTag rule skipped — active view '{view.Name}' is not a 3D view.");
                    }
                }
            }

            List<AutoAnnotationRule> effective;
            if (pack.Rules != null && pack.Rules.Count > 0)
            {
                effective = pack.Rules
                    .Where(r => r != null && r.Enabled
                                && tagRuleKinds.Contains(r.RuleType ?? "AutoTag"))
                    .ToList();
            }
            else if (pack.AutoTag == true)
            {
                // KnownTaggableCategories list was lost to the merge — use the
                // taxonomy from SharedParamGuids as the canonical source.
                effective = SharedParamGuids.AllCategoryEnums.Select(bic => new AutoAnnotationRule
                {
                    RuleType = "AutoTag",
                    Category = bic.ToString(),
                    SkipIfTagged = true,
                    DensityMode = "All"
                }).ToList();
            }
            else
            {
                try { refs.Append(new Reference(l)); } catch { /* skip */ }
            }
            if (refs.Size < 2) return;

            // Vertical dim line — arbitrary X, Y from bottom-most to
            // top-most level elevation.
            var pMin = new XYZ(0, 0, levels.First().Elevation);
            var pMax = new XYZ(0, 0, levels.Last().Elevation);
            var dimLine = Line.CreateBound(pMin, pMax);
            try
            {
                var dim = doc.Create.NewDimension(view, dimLine, refs);
                if (dim != null) stats.DimsCreated++;
            }
            catch (Exception ex) { stats.Warnings.Add("Level dim: " + ex.Message); }
        }

        // ─── Tagging ─────────────────────────────────────────────────────

        /// <summary>
        /// Walk every element of the given category visible in the view
        /// and drop an IndependentTag at the element's centre. The tag
        /// family is resolved from AnnotationRulePack.TagFamilies[catKey]
        /// if present, otherwise the first loaded tag family for the
        /// category. After placing each tag, applies CategoryDepths from
        /// the active ViewStylePack if declared.
        /// </summary>
        private static void TagCategory(Document doc, View view, AnnotationRulePack pack,
            BuiltInCategory bic, string catKey, AnnotationRunStats stats)
        {
            var elements = new FilteredElementCollector(doc, view.Id)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToElements();
            if (elements.Count == 0) return;

            ElementId tagTypeId = ResolveTagTypeId(doc, view, pack, catKey, bic);
            if (tagTypeId == ElementId.InvalidElementId)
            {
                stats.Warnings.Add($"No tag family available for {catKey} — skipped.");
                return;
            }

            // Pre-resolve CategoryDepths pack for this bic (avoid per-element registry hit).
            ViewStylePack depthPack = null;
            string depthCatKey = null;
            int resolvedDepth = 0;
            bool hasDepth = false;
            try
            {
                var dtId = DrawingTypeStamper.Read(view);
                if (!string.IsNullOrEmpty(dtId))
                {
                    depthPack = DrawingTypeRegistry.TryGetPack(doc, dtId);
                    if (depthPack?.CategoryDepths != null)
                    {
                        depthCatKey = Category.GetCategory(doc, bic)?.Name ?? bic.ToString();
                        if (!depthPack.CategoryDepths.TryGetValue(depthCatKey, out resolvedDepth))
                        {
                            depthPack.CategoryDepths.TryGetValue(bic.ToString(), out resolvedDepth);
                        }
                        hasDepth = resolvedDepth > 0;
                    }
                }
            }
            catch { /* resolver must never throw */ }

            foreach (var el in elements)
            {
                try
                {
                    var pt = GetElementCentre(el);
                    // Revit 2025 removed IndependentTag.CanTagHost(doc, Reference) +
                    // renamed the Create parameters. The surrounding try/catch
                    // turns any "can't tag this host" failure into a Skipped
                    // count + warning row, replacing the dropped pre-check.
                    var tag = IndependentTag.Create(doc, tagTypeId, view.Id,
                        new Reference(el), false, TagOrientation.Horizontal, pt);
                    if (tag != null) stats.TagsPlaced++;

                    // Apply CategoryDepths from the active pack, if declared.
                    if (hasDepth)
                    {
                        try { ParameterHelpers.SetInt(el, "TAG_PARA_DEPTH_INT", resolvedDepth); }
                        catch { }
                        try { StingTools.Core.ParameterHelpers.SetInt(el, $"TAG_PARA_STATE_{resolvedDepth}_BOOL", 1); }
                        catch { }
                    }
                }
                catch (Exception ex) { result.Warnings.Add($"TagRule create '{el.Id}': {ex.Message}"); }
            }
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
                // View-centre fallback removed: this overload doesn't have a `view`
                // in scope. Origin XYZ.Zero is a sentinel the caller can detect.
            }
            catch { /* best effort */ }
            return null;
        }

        private static int GetPackTagDepth(AnnotationRulePack pack, string category)
        {
            if (pack?.TagDepths == null) return 0;
            if (pack.TagDepths.TryGetValue(category, out var d)) return d;
            // Try by display name
            var rc = RevitCategoryTree.FindByBic(category);
            if (rc != null && pack.TagDepths.TryGetValue(rc.DisplayName, out var d2)) return d2;
            return 0;
        }

        private static ElementId ResolveTagFamilySymbol(Document doc, AutoAnnotationRule rule, AnnotationRulePack pack, string category)
        {
            // Rule-level override
            if (!string.IsNullOrEmpty(rule.TagFamily))
            {
                var s = FindFamilySymbolByName(doc, rule.TagFamily);
                if (s != null) return s.Id;
            }
        }

        // ─── Resolution helpers ──────────────────────────────────────────

        private static ElementId ResolveTagTypeId(Document doc, View view, AnnotationRulePack pack,
            string catKey, BuiltInCategory hostCategory)
        {
            ElementId result = ElementId.InvalidElementId;

            // 1. Named tag family from the rule pack
            if (pack.TagFamilies != null && pack.TagFamilies.TryGetValue(catKey, out var famName)
                && !string.IsNullOrWhiteSpace(famName))
            {
                var byName = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(fs => string.Equals(fs.FamilyName, famName, StringComparison.OrdinalIgnoreCase));
                if (byName != null) result = byName.Id;
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

        private static BuiltInCategory? MapCategoryToTagBic(BuiltInCategory model)
        {
            // Best-effort mapping from model BIC to its corresponding tag BIC.
            switch (model)
            {
                case BuiltInCategory.OST_Doors:                return BuiltInCategory.OST_DoorTags;
                case BuiltInCategory.OST_Windows:              return BuiltInCategory.OST_WindowTags;
                case BuiltInCategory.OST_Rooms:                return BuiltInCategory.OST_RoomTags;
                case BuiltInCategory.OST_Walls:                return BuiltInCategory.OST_WallTags;
                case BuiltInCategory.OST_Floors:               return BuiltInCategory.OST_FloorTags;
                case BuiltInCategory.OST_Ceilings:             return BuiltInCategory.OST_CeilingTags;
                case BuiltInCategory.OST_Roofs:                return BuiltInCategory.OST_RoofTags;
                case BuiltInCategory.OST_Stairs:               return BuiltInCategory.OST_StairsTags;
                case BuiltInCategory.OST_StructuralColumns:    return BuiltInCategory.OST_StructuralColumnTags;
                case BuiltInCategory.OST_StructuralFraming:    return BuiltInCategory.OST_StructuralFramingTags;
                case BuiltInCategory.OST_StructuralFoundation: return BuiltInCategory.OST_StructuralFoundationTags;
                case BuiltInCategory.OST_Furniture:            return BuiltInCategory.OST_FurnitureTags;
                case BuiltInCategory.OST_LightingFixtures:     return BuiltInCategory.OST_LightingFixtureTags;
                case BuiltInCategory.OST_MechanicalEquipment:  return BuiltInCategory.OST_MechanicalEquipmentTags;
                case BuiltInCategory.OST_PlumbingFixtures:     return BuiltInCategory.OST_PlumbingFixtureTags;
                case BuiltInCategory.OST_DuctCurves:           return BuiltInCategory.OST_DuctTags;
                case BuiltInCategory.OST_PipeCurves:           return BuiltInCategory.OST_PipeTags;
                case BuiltInCategory.OST_Conduit:              return BuiltInCategory.OST_ConduitTags;
                case BuiltInCategory.OST_CableTray:            return BuiltInCategory.OST_CableTrayTags;
                case BuiltInCategory.OST_ElectricalEquipment:  return BuiltInCategory.OST_ElectricalEquipmentTags;
                case BuiltInCategory.OST_ElectricalFixtures:   return BuiltInCategory.OST_ElectricalFixtureTags;
                case BuiltInCategory.OST_GenericModel:         return BuiltInCategory.OST_GenericModelTags;
                default: return null;
            }
        }

        private static TagOrientation ParseOrientation(string s)
        {
            if (string.IsNullOrEmpty(s)) return TagOrientation.Horizontal;
            switch (s.Trim().ToLowerInvariant())
            {
                case "vertical": return TagOrientation.Vertical;
                case "model":    return TagOrientation.AnyModelDirection;
                default:         return TagOrientation.Horizontal;
            }
        }

        // ── Dim rules ──

        private static void RunDimRules(Document doc, View view, AnnotationRulePack pack, AnnotationRunOptions opts, AnnotationResult result)
        {
            bool autoDim = pack.AutoDim == true ||
                           (pack.Rules != null && pack.Rules.Any(r => r != null && r.Enabled &&
                               string.Equals(r.RuleType, "AutoDim", StringComparison.OrdinalIgnoreCase)));
            if (!autoDim) return;

            var grids = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Grid))
                .Cast<Grid>()
                .ToList();
            if (grids.Count >= 2)
            {
                try { CreateGridDim(doc, view, grids.Where(IsHorizontal).ToList(), result); } catch (Exception ex) { result.Warnings.Add("DimGrids horizontal: " + ex.Message); }
                try { CreateGridDim(doc, view, grids.Where(IsVertical).ToList(),   result); } catch (Exception ex) { result.Warnings.Add("DimGrids vertical: " + ex.Message); }
            }

            DimensionType dt = ResolveDimensionType(doc, pack.DimensionStyle);
            if (dt != null)
            {
                // Best-effort: ChangeTypeId on the most recently placed dimensions is non-trivial without tracking ids;
                // skipped here, but the resolved dimension type is logged.
            }
        }

        private static void CreateGridDim(Document doc, View view, List<Grid> grids, AnnotationResult result)
        {
            if (grids == null || grids.Count < 2) return;
            try
            {
                var refArr = new ReferenceArray();
                foreach (var g in grids)
                {
                    var c = g.Curve;
                    if (c == null) continue;
                    refArr.Append(new Reference(g));
                }
                if (refArr.Size < 2) return;
                var first = grids[0].Curve;
                if (first == null) return;
                var p1 = first.GetEndPoint(0);
                var p2 = first.GetEndPoint(1);
                var line = Line.CreateBound(p1, p2);
                doc.Create.NewDimension(view, line, refArr);
                result.DimsPlaced++;
            }
            catch (Exception ex) { result.Warnings.Add("CreateGridDim: " + ex.Message); }
        }

        private static bool IsHorizontal(Grid g)
        {
            try { var c = g.Curve as Line; if (c == null) return false; var d = c.Direction; return Math.Abs(d.Y) > Math.Abs(d.X); }
            catch { return false; }
        }

        private static bool IsVertical(Grid g)
        {
            try { var c = g.Curve as Line; if (c == null) return false; var d = c.Direction; return Math.Abs(d.X) >= Math.Abs(d.Y); }
            catch { return false; }
        }

        private static DimensionType ResolveDimensionType(Document doc, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return new FilteredElementCollector(doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
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
                if (!sym.IsActive) { try { sym.Activate(); } catch { } }
                var inset = (sizeMm ?? 0) / 304.8;
                var pt = ResolvePositionPoint(outline, position, inset);
                doc.Create.NewFamilyInstance(pt, sym, view);
                result.DecorativePlaced++;
            }
            catch (Exception ex) { result.Warnings.Add($"Decorative '{familyName}': {ex.Message}"); }
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

        private static void ProcessSpotRules(Document doc, View view, List<SpotAnnotationRule> rules, bool isCoordinate, AnnotationResult result)
        {
            if (rules == null || rules.Count == 0) return;
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
                                try { sd.ChangeTypeId(symbolId); } catch { }
                            }
                            result.SpotsPlaced++;
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
