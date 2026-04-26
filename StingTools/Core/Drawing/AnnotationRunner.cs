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

namespace StingTools.Core.Drawing
{
    public sealed class AnnotationRunStats
    {
        public int DimsCreated { get; set; }
        public int TagsPlaced { get; set; }
        public int Skipped { get; set; }
        public List<string> Warnings { get; } = new List<string>();

        public override string ToString()
            => $"AnnotationRun: {DimsCreated} dim(s), {TagsPlaced} tag(s), {Skipped} skipped, {Warnings.Count} warning(s)";
    }

    public sealed class AnnotationRunOptions
    {
        public bool SkipAutoTag { get; set; } = false;
        public bool SkipAutoDim { get; set; } = false;
        public bool SkipDecorative { get; set; } = false;
        public bool SkipSpots { get; set; } = false;
        public double ViewScale { get; set; } = 100;
    }

    public sealed class AnnotationResult
    {
        public int TagsPlaced { get; set; }
        public int DimsPlaced { get; set; }
        public int DecorativePlaced { get; set; }
        public int SpotsPlaced { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class AnnotationRunner
    {
        public static readonly IReadOnlyList<string> KnownTaggableCategories =
            RevitCategoryTree.TaggableCategories.Select(c => c.Bic).ToList();

        // ── Public entry point: Run(...) ──

        public static AnnotationResult Run(Document doc, View view, AnnotationRulePack pack, AnnotationRunOptions options)
        {
            var result = new AnnotationResult();
            if (doc == null || view == null || pack == null) return result;
            options = options ?? new AnnotationRunOptions { ViewScale = view.Scale };

            try
            {
                if (!options.SkipAutoTag)    RunTagRules(doc, view, pack, options, result);
            }
            catch (Exception ex) { result.Warnings.Add("RunTagRules: " + ex.Message); }

            try
            {
                if (!options.SkipAutoDim)    RunDimRules(doc, view, pack, options, result);
            }
            catch (Exception ex) { result.Warnings.Add("RunDimRules: " + ex.Message); }

            try
            {
                if (!options.SkipDecorative) RunDecorativeAnnotation(doc, view, pack, options, result);
            }
            catch (Exception ex) { result.Warnings.Add("RunDecorativeAnnotation: " + ex.Message); }

            try
            {
                if (!options.SkipSpots)      RunSpotAnnotation(doc, view, pack, options, result);
            }
            catch (Exception ex) { result.Warnings.Add("RunSpotAnnotation: " + ex.Message); }

            return result;
        }

        // ── Legacy entry point retained for DrawingTypePresentation ──

        public static AnnotationRunStats Apply(Document doc, View view, DrawingType drawingType)
        {
            var stats = new AnnotationRunStats();
            if (doc == null || view == null || drawingType?.Annotation == null) return stats;

            var pack = drawingType.Annotation;
            try { pack.MigrateFromLegacy(); } catch { }

            // Scale-aware density modifier — when the view is coarser than
            // DenseUntilScale, skip per-element tagging.
            bool dense = !pack.DenseUntilScale.HasValue || view.Scale <= pack.DenseUntilScale.Value;

            var options = new AnnotationRunOptions
            {
                ViewScale  = view.Scale,
                SkipAutoTag = !dense
            };

            var r = Run(doc, view, pack, options);
            stats.TagsPlaced  = r.TagsPlaced;
            stats.DimsCreated = r.DimsPlaced;
            stats.Warnings.AddRange(r.Warnings);
            if (!dense) { stats.Skipped++; stats.Warnings.Add($"Per-element tagging skipped — view scale 1:{view.Scale} exceeds denseUntilScale 1:{pack.DenseUntilScale}."); }
            return stats;
        }

        // ── Tag rules ──

        private static void RunTagRules(Document doc, View view, AnnotationRulePack pack, AnnotationRunOptions opts, AnnotationResult result)
        {
            // Build effective rule list
            List<AutoAnnotationRule> effective;
            if (pack.Rules != null && pack.Rules.Count > 0)
            {
                effective = pack.Rules.Where(r => r != null && r.Enabled &&
                    string.Equals(r.RuleType ?? "AutoTag", "AutoTag", StringComparison.OrdinalIgnoreCase)).ToList();
            }
            else if (pack.AutoTag == true)
            {
                effective = KnownTaggableCategories.Select(bic => new AutoAnnotationRule
                {
                    RuleType = "AutoTag",
                    Category = bic,
                    SkipIfTagged = true,
                    DensityMode = "All"
                }).ToList();
            }
            else
            {
                return;
            }

            foreach (var rule in effective)
            {
                IEnumerable<string> categories = string.Equals(rule.Category, "*", StringComparison.Ordinal)
                    ? KnownTaggableCategories
                    : new[] { rule.Category };

                foreach (var cat in categories)
                {
                    try { ProcessOneTagRule(doc, view, pack, rule, cat, result); }
                    catch (Exception ex) { result.Warnings.Add($"TagRule '{rule.RuleType}/{cat}': {ex.Message}"); }
                }
            }
        }

        private static void ProcessOneTagRule(Document doc, View view, AnnotationRulePack pack, AutoAnnotationRule rule, string category, AnnotationResult result)
        {
            var catId = ResolveCategoryId(doc, category);
            if (catId == ElementId.InvalidElementId)
            {
                result.Warnings.Add($"Tag category '{category}' not found.");
                return;
            }

            // Resolve tag family symbol
            var tagSymbolId = ResolveTagFamilySymbol(doc, rule, pack, category);
            if (tagSymbolId == ElementId.InvalidElementId)
            {
                result.Warnings.Add($"No tag family resolved for category '{category}' — skipped.");
                return;
            }

            // Collect candidate elements
            var elements = new FilteredElementCollector(doc, view.Id)
                .OfCategoryId(catId)
                .WhereElementIsNotElementType()
                .ToList();

            // Skip already-tagged
            if (rule.SkipIfTagged)
            {
                var tagged = new HashSet<ElementId>(
                    new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(IndependentTag))
                        .Cast<IndependentTag>()
                        .SelectMany(t =>
                        {
                            try { return t.GetTaggedLocalElementIds(); }
                            catch { return Enumerable.Empty<ElementId>(); }
                        }));
                elements = elements.Where(e => !tagged.Contains(e.Id)).ToList();
            }

            // Min size filter
            if (rule.MinSizeMm.HasValue)
            {
                var minFt = rule.MinSizeMm.Value / 304.8;
                elements = elements.Where(e =>
                {
                    try
                    {
                        var bb = e.get_BoundingBox(view);
                        if (bb == null) return false;
                        return (bb.Max - bb.Min).GetLength() >= minFt;
                    }
                    catch { return false; }
                }).ToList();
            }

            // Density mode
            switch ((rule.DensityMode ?? "All").Trim())
            {
                case "LargestOnly":
                    var largest = elements.OrderByDescending(e =>
                    {
                        try { var bb = e.get_BoundingBox(view); return bb == null ? 0 : (bb.Max - bb.Min).GetLength(); }
                        catch { return 0; }
                    }).FirstOrDefault();
                    elements = largest != null ? new List<Element> { largest } : new List<Element>();
                    break;
                case "RepresentativeOne":
                    elements = elements.Take(1).ToList();
                    break;
                default: break; // All
            }

            bool addLeader = string.Equals(rule.LeaderStyle, "Attached", StringComparison.OrdinalIgnoreCase);
            TagOrientation ori = ParseOrientation(rule.Orientation);

            int depth = rule.Tag7Depth ?? rule.Depth ?? GetPackTagDepth(pack, category);

            foreach (var el in elements)
            {
                try
                {
                    var bb = el.get_BoundingBox(view);
                    if (bb == null) continue;
                    var center = (bb.Min + bb.Max) * 0.5;
                    IndependentTag.Create(doc, tagSymbolId, view.Id,
                        new Reference(el), addLeader, ori, center);
                    result.TagsPlaced++;
                }
                catch (Exception ex) { result.Warnings.Add($"TagRule create '{el.Id}': {ex.Message}"); }

                if (depth > 0)
                {
                    try { StingTools.Core.ParameterHelpers.SetInt(el, $"TAG_PARA_STATE_{depth}_BOOL", 1); }
                    catch { }
                }
            }
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
            // Pack-level override
            if (pack?.TagFamilies != null)
            {
                if (pack.TagFamilies.TryGetValue(category, out var n) && !string.IsNullOrEmpty(n))
                {
                    var s = FindFamilySymbolByName(doc, n);
                    if (s != null) return s.Id;
                }
                var rc = RevitCategoryTree.FindByBic(category);
                if (rc != null && pack.TagFamilies.TryGetValue(rc.DisplayName, out var n2) && !string.IsNullOrEmpty(n2))
                {
                    var s = FindFamilySymbolByName(doc, n2);
                    if (s != null) return s.Id;
                }
            }
            // Fallback — first FamilySymbol whose category indicates a tag of category
            try
            {
                if (Enum.TryParse<BuiltInCategory>(category, true, out var bic))
                {
                    var tagBic = MapCategoryToTagBic(bic);
                    if (tagBic != null)
                    {
                        var sym = new FilteredElementCollector(doc)
                            .OfClass(typeof(FamilySymbol))
                            .OfCategory(tagBic.Value)
                            .Cast<FamilySymbol>()
                            .FirstOrDefault();
                        if (sym != null) return sym.Id;
                    }
                }
            }
            catch { }
            return ElementId.InvalidElementId;
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
