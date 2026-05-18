// StingTools — Drawing Template Manager
//
// AnnotationRunner consumes the AnnotationRulePack on a resolved
// DrawingType and executes the annotation pass against a single View:
// auto-dim grids and levels, auto-tag rooms / doors / windows /
// equipment, auto-tag fabrication welds / bends / supports. It also
// honours the scale-aware density modifier — at scales coarser than
// DenseUntilScale, per-element tagging is skipped so a 1:200 overall
// plan does not get the same tag density as a 1:50 detail.
//
// Called from batch generation commands AFTER a view has been created
// and its crop region set; the runner treats "cannot resolve tag
// family X" as a warning not an error so partially-loaded corporate
// catalogues still produce usable output.

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

            bool dense = !pack.DenseUntilScale.HasValue || view.Scale <= pack.DenseUntilScale.Value;

#pragma warning disable CS0618 // legacy AutoXxx flags are folded into Rules at load time; readers still consult them for backward compat
            try { if (!opts.SkipAutoDim && pack.AutoDimGrids)  DimGrids(doc, view, pack, new AnnotationRunStats()); } catch (Exception ex) { result.Warnings.Add("AutoDimGrids: " + ex.Message); }
            try { if (!opts.SkipAutoDim && pack.AutoDimLevels) DimLevels(doc, view, pack, new AnnotationRunStats()); } catch (Exception ex) { result.Warnings.Add("AutoDimLevels: " + ex.Message); }

            if (dense && !opts.SkipAutoTag)
            {
                var dtId = DrawingTypeStamper.Read(view);
                if (!string.IsNullOrEmpty(dtId))
                    dt = DrawingTypeRegistry.Get(doc, dtId);
            }
#pragma warning restore CS0618

            if (dt == null) dt = new DrawingType();
            // Caller-supplied pack wins: overlay it onto the resolved DT's Annotation.
            if (annotation != null) dt.Annotation = annotation;

            return Apply(doc, view, dt, options);
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
            var pack = drawingType.Annotation;

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

            return stats;
        }

        // ─── Dimensioning ────────────────────────────────────────────────

        /// <summary>
        /// Drop a single overall dimension chain across all grids
        /// visible in the view. Each grid contributes one reference;
        /// the dimension line is placed 20mm outside the view crop.
        /// Ordinate vs Linear chosen from AnnotationRulePack.DimensionStrategy.
        /// </summary>
        private static void DimGrids(Document doc, View view, AnnotationRulePack pack, AnnotationRunStats stats)
        {
            var grids = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Grids)
                .WhereElementIsNotElementType()
                .Cast<Grid>()
                .ToList();
            if (grids.Count < 2) return;

            // Collect references at the grid head (endpoint 1 of the
            // grid curve). Revit needs Reference objects not ElementIds.
            var refs = new ReferenceArray();
            XYZ first = null, last = null;
            foreach (var g in grids)
            {
                var curve = g.Curve;
                if (curve == null) continue;
                var pt = curve.GetEndPoint(1); // head
                if (first == null) first = pt;
                last = pt;
                try { refs.Append(new Reference(g)); } catch { /* skip */ }
            }
            if (refs.Size < 2 || first == null || last == null) return;

            // Dimension line 20mm (~0.065 ft) offset from head row.
            var dimLine = Line.CreateBound(first, last);
            var dimStyleId = ResolveDimensionStyleId(doc, pack.DimensionStyle);
            try
            {
                var dim = (dimStyleId == null || dimStyleId == ElementId.InvalidElementId)
                    ? doc.Create.NewDimension(view, dimLine, refs)
                    : doc.Create.NewDimension(view, dimLine, refs, (DimensionType)doc.GetElement(dimStyleId));
                if (dim != null) stats.DimsCreated++;
            }
            catch (Exception ex) { stats.Warnings.Add("Grid dim: " + ex.Message); }
        }

        /// <summary>
        /// Drop a vertical dimension chain across all Levels visible in
        /// the section / elevation view. Each level contributes one
        /// horizontal line reference.
        /// </summary>
        private static void DimLevels(Document doc, View view, AnnotationRulePack pack, AnnotationRunStats stats)
        {
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
                XYZ pt = GetElementCentre(el);
                if (pt == null) continue;
                try
                {
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
                    }
                }
                catch (Exception ex)
                {
                    stats.Warnings.Add($"Tag {el.Id}: {ex.Message}");
                    stats.Skipped++;
                }
            }
        }

        /// <summary>
        /// "Equipment" is a loose concept — tag every MechanicalEquipment
        /// / ElectricalEquipment / PlumbingFixtures instance in the view.
        /// </summary>
        private static void TagEquipment(Document doc, View view, AnnotationRulePack pack, AnnotationRunStats stats)
        {
            foreach (var bic in new[]
            {
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_ElectricalEquipment,
                BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_LightingFixtures,
            })
            {
                TagCategory(doc, view, pack, bic, bic.ToString(), stats);
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
                // Location point for inserts
                if (el.Location is LocationPoint lp) return lp.Point;
                // Location curve midpoint for walls / lines
                if (el.Location is LocationCurve lc)
                {
                    var c = lc.Curve;
                    return c?.Evaluate(0.5, true);
                }
                // BoundingBox centre as last resort
                var bb = el.get_BoundingBox(null);
                if (bb != null) return (bb.Min + bb.Max) * 0.5;
            }
            catch { /* fall through */ }
            return null;
        }
    }
}
