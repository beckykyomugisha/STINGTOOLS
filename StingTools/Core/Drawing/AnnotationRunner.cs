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

namespace StingTools.Core.Drawing
{
    public sealed class AnnotationRunOptions
    {
        public int  ViewScale      { get; set; } = 100;
        public bool SkipAutoTag    { get; set; }
        public bool SkipAutoDim    { get; set; }
        public bool SkipDecorative { get; set; }
        public bool SkipSpots      { get; set; }
    }

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
        public int DimsCreated { get; set; }
        public int TagsPlaced { get; set; }
        public int Skipped    { get; set; }
        public List<string> Warnings { get; } = new List<string>();

        public override string ToString()
            => $"AnnotationRun: {DimsCreated} dim(s), {TagsPlaced} tag(s), {Skipped} skipped, {Warnings.Count} warning(s)";
    }

    public static class AnnotationRunner
    {
        /// <summary>
        /// Run the annotation pass for a given rule pack with explicit options.
        /// Returns an <see cref="AnnotationResult"/> that the caller can inspect.
        /// The caller is responsible for an active Transaction.
        /// </summary>
        public static AnnotationResult Run(Document doc, View view, AnnotationRulePack pack, AnnotationRunOptions opts)
        {
            var result = new AnnotationResult();
            if (doc == null || view == null || pack == null) return result;
            opts ??= new AnnotationRunOptions();

            bool dense = !pack.DenseUntilScale.HasValue || view.Scale <= pack.DenseUntilScale.Value;

#pragma warning disable CS0618 // legacy AutoXxx flags are folded into Rules at load time; readers still consult them for backward compat
            try { if (!opts.SkipAutoDim && pack.AutoDimGrids)  DimGrids(doc, view, pack, new AnnotationRunStats()); } catch (Exception ex) { result.Warnings.Add("AutoDimGrids: " + ex.Message); }
            try { if (!opts.SkipAutoDim && pack.AutoDimLevels) DimLevels(doc, view, pack, new AnnotationRunStats()); } catch (Exception ex) { result.Warnings.Add("AutoDimLevels: " + ex.Message); }

            if (dense && !opts.SkipAutoTag)
            {
                var s = new AnnotationRunStats();
                try { if (pack.AutoTagRooms)     TagCategory(doc, view, pack, BuiltInCategory.OST_Rooms,             "Rooms",    s); } catch (Exception ex) { result.Warnings.Add("AutoTagRooms: " + ex.Message); }
                try { if (pack.AutoTagDoors)     TagCategory(doc, view, pack, BuiltInCategory.OST_Doors,             "Doors",    s); } catch (Exception ex) { result.Warnings.Add("AutoTagDoors: " + ex.Message); }
                try { if (pack.AutoTagWindows)   TagCategory(doc, view, pack, BuiltInCategory.OST_Windows,           "Windows",  s); } catch (Exception ex) { result.Warnings.Add("AutoTagWindows: " + ex.Message); }
                try { if (pack.AutoTagEquipment) TagEquipment(doc, view, pack, s); }                                               catch (Exception ex) { result.Warnings.Add("AutoTagEquipment: " + ex.Message); }
                result.TagsPlaced = s.TagsPlaced;
                result.DimsPlaced = s.DimsCreated;
            }
#pragma warning restore CS0618

            return result;
        }

        /// <summary>
        /// Run the annotation pass defined by drawingType.Annotation
        /// against the given view. The caller is responsible for an
        /// active Transaction — this method performs many Element
        /// creations and expects to be wrapped.
        /// </summary>
        public static AnnotationRunStats Apply(Document doc, View view, DrawingType drawingType)
        {
            var stats = new AnnotationRunStats();
            if (doc == null || view == null || drawingType?.Annotation == null) return stats;
            var pack = drawingType.Annotation;

            // Scale-aware density — at scales coarser than DenseUntilScale,
            // skip per-element tagging. View.Scale is 1:N so a larger
            // number means a coarser drawing.
            bool dense = !pack.DenseUntilScale.HasValue || view.Scale <= pack.DenseUntilScale.Value;

#pragma warning disable CS0618 // legacy AutoXxx flags are folded into Rules at load time; readers still consult them for backward compat
            try { if (pack.AutoDimGrids)  DimGrids(doc, view, pack, stats); } catch (Exception ex) { stats.Warnings.Add("AutoDimGrids: " + ex.Message); }
            try { if (pack.AutoDimLevels) DimLevels(doc, view, pack, stats); } catch (Exception ex) { stats.Warnings.Add("AutoDimLevels: " + ex.Message); }

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
#pragma warning restore CS0618
            else
            {
                stats.Skipped++;
                stats.Warnings.Add($"Per-element tagging skipped — view scale 1:{view.Scale} exceeds denseUntilScale 1:{pack.DenseUntilScale}.");
            }

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
        /// category.
        /// </summary>
        private static void TagCategory(Document doc, View view, AnnotationRulePack pack,
            BuiltInCategory bic, string catKey, AnnotationRunStats stats)
        {
            var elements = new FilteredElementCollector(doc, view.Id)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToElements();
            if (elements.Count == 0) return;

            ElementId tagTypeId = ResolveTagTypeId(doc, pack, catKey, bic);
            if (tagTypeId == ElementId.InvalidElementId)
            {
                stats.Warnings.Add($"No tag family available for {catKey} — skipped.");
                return;
            }

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

        private static ElementId ResolveTagTypeId(Document doc, AnnotationRulePack pack,
            string catKey, BuiltInCategory hostCategory)
        {
            // 1. Named tag family from the rule pack
            if (pack.TagFamilies != null && pack.TagFamilies.TryGetValue(catKey, out var famName)
                && !string.IsNullOrWhiteSpace(famName))
            {
                var byName = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(fs => string.Equals(fs.FamilyName, famName, StringComparison.OrdinalIgnoreCase));
                if (byName != null) return byName.Id;
            }

            // 2. First loaded tag of the host's tag category (project default)
            var fallback = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs => fs.Category != null
                    && fs.Category.CategoryType == CategoryType.Annotation
                    && fs.Category.Id.Value == (long)TagCategoryFor(hostCategory));
            return fallback?.Id ?? ElementId.InvalidElementId;
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
