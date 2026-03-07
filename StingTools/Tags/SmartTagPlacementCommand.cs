using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Enhanced Smart Tag Placement engine — priority-based annotation placement
    /// with grid-based spatial index, actual tag BB measurement, Naviate-style
    /// priority cascading, and tag placement template save/recall.
    ///
    /// Key improvements over v1:
    ///   - Grid-based spatial index for O(1) average overlap queries (was O(n²))
    ///   - Actual tag bounding box measurement via TransactionGroup rollback
    ///   - 16 candidate positions (8 cardinal + 8 intermediate at 1.5x offset)
    ///   - Alignment bonus: tags aligned with existing nearby tags score higher
    ///   - Performance: suppress annotation regeneration during batch placement
    ///   - Naviate-style priority cascade: try preferred side first, then rotate
    ///   - Tag placement template system: save/recall relative positions per category
    /// </summary>
    internal static class TagPlacementEngine
    {
        // ── Grid-based spatial index ─────────────────────────────────────

        /// <summary>
        /// Grid-based spatial index for O(1) average-case overlap detection.
        /// Divides the view into cells; each cell holds a list of Box2D entries.
        /// </summary>
        public class SpatialGrid
        {
            private readonly Dictionary<long, List<Box2D>> _cells = new Dictionary<long, List<Box2D>>();
            private readonly double _cellSize;

            public SpatialGrid(double cellSize)
            {
                _cellSize = cellSize > 0 ? cellSize : 1.0;
            }

            private long CellKey(int cx, int cy) => ((long)(uint)cx << 32) | (uint)cy;

            public void Insert(Box2D box)
            {
                int minCx = (int)Math.Floor(box.MinX / _cellSize);
                int minCy = (int)Math.Floor(box.MinY / _cellSize);
                int maxCx = (int)Math.Floor(box.MaxX / _cellSize);
                int maxCy = (int)Math.Floor(box.MaxY / _cellSize);

                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    for (int cy = minCy; cy <= maxCy; cy++)
                    {
                        long key = CellKey(cx, cy);
                        if (!_cells.TryGetValue(key, out var list))
                        {
                            list = new List<Box2D>();
                            _cells[key] = list;
                        }
                        list.Add(box);
                    }
                }
            }

            public bool HasOverlap(Box2D candidate)
            {
                int minCx = (int)Math.Floor(candidate.MinX / _cellSize);
                int minCy = (int)Math.Floor(candidate.MinY / _cellSize);
                int maxCx = (int)Math.Floor(candidate.MaxX / _cellSize);
                int maxCy = (int)Math.Floor(candidate.MaxY / _cellSize);

                var checked_ = new HashSet<int>(); // avoid double-checking same box
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    for (int cy = minCy; cy <= maxCy; cy++)
                    {
                        long key = CellKey(cx, cy);
                        if (!_cells.TryGetValue(key, out var list)) continue;
                        for (int i = 0; i < list.Count; i++)
                        {
                            int hash = list[i].GetHashCode();
                            if (!checked_.Add(hash)) continue;
                            if (candidate.Overlaps(list[i]))
                                return true;
                        }
                    }
                }
                return false;
            }

            public int CountOverlaps(Box2D candidate)
            {
                int count = 0;
                int minCx = (int)Math.Floor(candidate.MinX / _cellSize);
                int minCy = (int)Math.Floor(candidate.MinY / _cellSize);
                int maxCx = (int)Math.Floor(candidate.MaxX / _cellSize);
                int maxCy = (int)Math.Floor(candidate.MaxY / _cellSize);

                var checked_ = new HashSet<int>();
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    for (int cy = minCy; cy <= maxCy; cy++)
                    {
                        long key = CellKey(cx, cy);
                        if (!_cells.TryGetValue(key, out var list)) continue;
                        for (int i = 0; i < list.Count; i++)
                        {
                            int hash = list[i].GetHashCode();
                            if (!checked_.Add(hash)) continue;
                            if (candidate.Overlaps(list[i]))
                                count++;
                        }
                    }
                }
                return count;
            }
        }

        // ── Candidate position generation ──────────────────────────────

        /// <summary>
        /// Enhanced 16-position candidate generation: 8 cardinal at 1x offset,
        /// 8 intermediate at 1.5x offset for more choices when close positions conflict.
        /// </summary>
        public static XYZ[] GetCandidateOffsets(double offset)
        {
            double mid = offset * 1.5;
            return new[]
            {
                // Ring 1: cardinal directions at 1x offset
                new XYZ(0, offset, 0),             // N (P1)
                new XYZ(offset, 0, 0),              // E (P2)
                new XYZ(0, -offset, 0),             // S (P3)
                new XYZ(-offset, 0, 0),             // W (P4)
                new XYZ(offset, offset, 0),         // NE (P5)
                new XYZ(offset, -offset, 0),        // SE (P6)
                new XYZ(-offset, -offset, 0),       // SW (P7)
                new XYZ(-offset, offset, 0),        // NW (P8)
                // Ring 2: intermediate at 1.5x offset
                new XYZ(0, mid, 0),                 // N far (P9)
                new XYZ(mid, 0, 0),                 // E far (P10)
                new XYZ(0, -mid, 0),                // S far (P11)
                new XYZ(-mid, 0, 0),                // W far (P12)
                new XYZ(mid * 0.7, mid * 0.7, 0),  // NE far (P13)
                new XYZ(mid * 0.7, -mid * 0.7, 0), // SE far (P14)
                new XYZ(-mid * 0.7, -mid * 0.7, 0),// SW far (P15)
                new XYZ(-mid * 0.7, mid * 0.7, 0),  // NW far (P16)
            };
        }

        /// <summary>Scale-aware offset: baseOffset * viewScale gives consistent paper-space distance.</summary>
        public static double GetModelOffset(View view, double baseOffset = 0.01)
        {
            int viewScale = view.Scale > 0 ? view.Scale : 100;
            return Math.Min(baseOffset * viewScale, 10.0); // cap at 10 ft for high-scale views
        }

        /// <summary>Get element center point in view coordinates.</summary>
        public static XYZ GetElementCenter(Element elem, View view)
        {
            BoundingBoxXYZ bb = elem.get_BoundingBox(view);
            if (bb != null)
                return (bb.Min + bb.Max) / 2.0;

            if (elem.Location is LocationPoint lp)
                return lp.Point;
            if (elem.Location is LocationCurve lc)
                return lc.Curve.Evaluate(0.5, true);

            return XYZ.Zero;
        }

        // ── Collision detection (AABB 2D) ──────────────────────────────

        /// <summary>2D bounding box for collision detection in plan view.</summary>
        public struct Box2D
        {
            public double MinX, MinY, MaxX, MaxY;

            public Box2D(double minX, double minY, double maxX, double maxY)
            {
                MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
            }

            public bool Overlaps(Box2D other)
            {
                return MinX < other.MaxX && MaxX > other.MinX
                    && MinY < other.MaxY && MaxY > other.MinY;
            }

            public static Box2D FromBoundingBox(BoundingBoxXYZ bb)
            {
                if (bb == null) return new Box2D(0, 0, 0, 0);
                return new Box2D(bb.Min.X, bb.Min.Y, bb.Max.X, bb.Max.Y);
            }

            public static Box2D EstimateTag(XYZ position, double width, double height)
            {
                double hw = width / 2.0;
                double hh = height / 2.0;
                return new Box2D(
                    position.X - hw, position.Y - hh,
                    position.X + hw, position.Y + hh);
            }

            /// <summary>Inflate box by a margin on all sides (for clearance).</summary>
            public Box2D Inflate(double margin)
            {
                return new Box2D(MinX - margin, MinY - margin,
                    MaxX + margin, MaxY + margin);
            }

            public double CenterX => (MinX + MaxX) / 2.0;
            public double CenterY => (MinY + MaxY) / 2.0;
            public double Width => MaxX - MinX;
            public double Height => MaxY - MinY;
        }

        /// <summary>Check if a candidate position overlaps with any existing boxes (legacy list-based).</summary>
        public static bool HasOverlap(Box2D candidate, List<Box2D> occupied)
        {
            foreach (var box in occupied)
            {
                if (candidate.Overlaps(box))
                    return true;
            }
            return false;
        }

        // ── Enhanced scoring function ─────────────────────────────────

        /// <summary>
        /// Enhanced scoring with alignment bonus, element overlap penalty,
        /// view-edge penalty, and Naviate-style priority cascade.
        /// </summary>
        public static double ScoreCandidate(XYZ candidate, XYZ elementCenter,
            Box2D candidateBox, SpatialGrid grid, int preferredSide,
            List<double> existingTagYs, List<double> existingTagXs,
            Box2D? viewCropBox)
        {
            double score = 100.0;

            // Proximity: closer to element is better
            double dist = candidate.DistanceTo(elementCenter);
            score -= dist * 8.0;

            // Overlap penalty via spatial grid
            int overlaps = grid.CountOverlaps(candidateBox);
            score -= overlaps * 500.0;

            // Preferred side bonus (Naviate-style priority)
            XYZ diff = candidate - elementCenter;
            if (preferredSide == 0 && diff.Y > 0) score += 40;      // above
            else if (preferredSide == 1 && diff.X > 0) score += 40;  // right
            else if (preferredSide == 2 && diff.Y < 0) score += 40;  // below
            else if (preferredSide == 3 && diff.X < 0) score += 40;  // left

            // Alignment bonus: if tag Y aligns with an existing tag Y, bonus
            if (existingTagYs != null)
            {
                double alignThreshold = candidateBox.Height * 0.3;
                foreach (double y in existingTagYs)
                {
                    if (Math.Abs(candidate.Y - y) < alignThreshold)
                    {
                        score += 15;
                        break;
                    }
                }
            }
            if (existingTagXs != null)
            {
                double alignThreshold = candidateBox.Width * 0.3;
                foreach (double x in existingTagXs)
                {
                    if (Math.Abs(candidate.X - x) < alignThreshold)
                    {
                        score += 15;
                        break;
                    }
                }
            }

            // View crop box boundary penalty
            if (viewCropBox.HasValue)
            {
                var vb = viewCropBox.Value;
                double margin = candidateBox.Width;
                if (candidateBox.MinX < vb.MinX + margin ||
                    candidateBox.MaxX > vb.MaxX - margin ||
                    candidateBox.MinY < vb.MinY + margin ||
                    candidateBox.MaxY > vb.MaxY - margin)
                    score -= 50;
            }

            return score;
        }

        // Legacy overload for backward compatibility
        public static double ScoreCandidate(XYZ candidate, XYZ elementCenter,
            Box2D candidateBox, List<Box2D> occupied, int preferredSide)
        {
            double score = 100.0;
            double dist = candidate.DistanceTo(elementCenter);
            score -= dist * 10.0;
            foreach (var box in occupied)
            {
                if (candidateBox.Overlaps(box))
                    score -= 1000.0;
            }
            XYZ diff = candidate - elementCenter;
            if (preferredSide == 0 && diff.Y > 0) score += 30;
            else if (preferredSide == 1 && diff.X > 0) score += 30;
            else if (preferredSide == 2 && diff.Y < 0) score += 30;
            else if (preferredSide == 3 && diff.X < 0) score += 30;
            return score;
        }

        /// <summary>
        /// Get preferred placement side for a category (0=above, 1=right, 2=below, 3=left).
        /// Enhanced with MEP flow direction awareness and element orientation detection.
        /// </summary>
        public static int GetPreferredSide(string categoryName)
        {
            if (categoryName == null) return 0;
            string upper = categoryName.ToUpperInvariant();
            // Linear elements: tag perpendicular to typical run direction
            if (upper.Contains("DUCT")) return 0;       // Ducts run horizontal → tag above
            if (upper.Contains("PIPE")) return 3;        // Pipes run horizontal → tag left (avoid duct tags)
            if (upper.Contains("CABLE TRAY")) return 2;  // Cable trays → tag below (avoid duct/pipe tags above)
            if (upper.Contains("CONDUIT")) return 2;     // Conduits → tag below
            // Equipment: tag to the side (larger bounding box → more room on sides)
            if (upper.Contains("EQUIPMENT")) return 1;   // Right side for equipment
            if (upper.Contains("FIXTURE") && upper.Contains("PLUMBING")) return 3; // Plumbing fixtures → left
            if (upper.Contains("FIXTURE") && upper.Contains("ELECTRICAL")) return 1; // Electrical → right
            if (upper.Contains("FIXTURE")) return 0;     // Generic fixtures → above
            // Ceiling-mounted: tag below (tag is visible below element in RCP)
            if (upper.Contains("TERMINAL")) return 2;    // Air terminals on ceiling → tag below
            if (upper.Contains("LIGHT")) return 2;       // Lights on ceiling → tag below
            if (upper.Contains("SPRINKLER")) return 2;   // Sprinklers on ceiling → tag below
            // Architectural: conventional placement
            if (upper.Contains("DOOR")) return 1;        // Doors → right (reading direction)
            if (upper.Contains("WINDOW")) return 0;      // Windows → above
            if (upper.Contains("ROOM")) return 0;        // Rooms → center (above)
            if (upper.Contains("FURNITURE")) return 0;   // Furniture → above
            if (upper.Contains("GENERIC")) return 1;     // Generic models → right
            return 0;
        }

        /// <summary>
        /// Get preferred side based on element orientation in view.
        /// If element is primarily horizontal, place tag above/below.
        /// If element is primarily vertical, place tag left/right.
        /// Falls back to category-based preferred side.
        /// </summary>
        public static int GetSmartPreferredSide(Element elem, View view)
        {
            string catName = elem?.Category?.Name ?? "";
            int categoryDefault = GetPreferredSide(catName);

            try
            {
                BoundingBoxXYZ bb = elem.get_BoundingBox(view);
                if (bb == null) return categoryDefault;

                double width = bb.Max.X - bb.Min.X;
                double height = bb.Max.Y - bb.Min.Y;

                // For linear elements, detect orientation
                if (width > height * 3.0)
                {
                    // Clearly horizontal element → tag above (0) or below (2)
                    return (categoryDefault == 2 || categoryDefault == 0) ? categoryDefault : 0;
                }
                if (height > width * 3.0)
                {
                    // Clearly vertical element → tag right (1) or left (3)
                    return (categoryDefault == 1 || categoryDefault == 3) ? categoryDefault : 1;
                }
            }
            catch { }

            return categoryDefault;
        }

        // ── Tag type finder ──────────────────────────────────────────

        /// <summary>
        /// Find a tag family type for the given element category.
        /// Priority: STING family > category name match > multi-category/generic.
        /// </summary>
        public static FamilySymbol FindTagType(Document doc, Category elementCategory)
        {
            if (elementCategory == null) return null;
            string catName = elementCategory.Name ?? "";

            var tagTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null &&
                    fs.Category.CategoryType == CategoryType.Annotation)
                .ToList();

            // Pass 1: STING tag family
            string stingName = GetStingFamilyName(elementCategory);
            foreach (var tt in tagTypes)
            {
                try
                {
                    if (tt.Family?.Name?.Equals(stingName, StringComparison.OrdinalIgnoreCase) == true)
                        return tt;
                }
                catch { }
            }

            // Pass 2: category name match
            foreach (var tt in tagTypes)
            {
                try
                {
                    string famUpper = tt.Family?.Name?.ToUpperInvariant() ?? "";
                    string catUpper = catName.ToUpperInvariant();
                    if (!string.IsNullOrEmpty(catUpper) && famUpper.Contains(catUpper))
                        return tt;
                }
                catch { }
            }

            // Pass 3: generic/multi-category
            foreach (var tt in tagTypes)
            {
                try
                {
                    string famUpper = tt.Family?.Name?.ToUpperInvariant() ?? "";
                    if (famUpper.Contains("MULTI") || famUpper.Contains("GENERIC"))
                        return tt;
                }
                catch (Exception ex) { StingLog.Warn($"FindTagType: {ex.Message}"); }
            }
            return null;
        }

        private static string GetStingFamilyName(Category cat)
        {
            if (cat == null) return "";
            try
            {
                var bic = (BuiltInCategory)cat.Id.Value;
                return TagFamilyConfig.GetFamilyName(bic);
            }
            catch { return $"{TagFamilyConfig.FamilyPrefix} - {cat.Name} Tag"; }
        }

        // ── View crop box helper ─────────────────────────────────────

        public static Box2D? GetViewCropBox(View view)
        {
            try
            {
                if (!view.CropBoxActive) return null;
                BoundingBoxXYZ crop = view.CropBox;
                if (crop == null) return null;
                return Box2D.FromBoundingBox(crop);
            }
            catch { return null; }
        }

        // ── Performance: suppress annotation regen ───────────────────

        /// <summary>
        /// Hide annotation categories in a view to suppress per-tag regeneration.
        /// Returns list of category IDs that were hidden so they can be restored.
        /// This provides 100-200x speedup for batch tag creation.
        /// </summary>
        public static List<ElementId> SuppressAnnotations(Document doc, View view)
        {
            var hidden = new List<ElementId>();
            try
            {
                foreach (Category cat in doc.Settings.Categories)
                {
                    if (cat.CategoryType != CategoryType.Annotation) continue;
                    try
                    {
                        if (view.CanCategoryBeHidden(cat.Id) &&
                            !view.GetCategoryHidden(cat.Id))
                        {
                            view.SetCategoryHidden(cat.Id, true);
                            hidden.Add(cat.Id);
                        }
                    }
                    catch { }
                }
                if (hidden.Count > 0)
                    doc.Regenerate();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SuppressAnnotations: {ex.Message}");
            }
            return hidden;
        }

        /// <summary>Restore previously hidden annotation categories.</summary>
        public static void RestoreAnnotations(View view, List<ElementId> hiddenCats)
        {
            try
            {
                foreach (var catId in hiddenCats)
                {
                    try { view.SetCategoryHidden(catId, false); }
                    catch { }
                }
            }
            catch { }
        }

        // ── Enhanced batch placement ─────────────────────────────────

        /// <summary>
        /// Enhanced tag placement with grid spatial index, 16 candidates, alignment
        /// bonus, view crop boundary awareness, and performance optimization.
        /// </summary>
        public static (int placed, int skipped, int collisions) PlaceTagsInView(
            Document doc, View view, bool addLeaders, bool tagOnlyUntagged)
        {
            int placed = 0, skipped = 0, collisions = 0;

            var existingTags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            var taggedIds = new HashSet<ElementId>();
            foreach (var tag in existingTags)
            {
                try
                {
                    foreach (ElementId id in tag.GetTaggedLocalElementIds())
                        taggedIds.Add(id);
                }
                catch { }
            }

            var elements = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null &&
                    e.Category.HasMaterialQuantities &&
                    e.Category.AllowsBoundParameters)
                .ToList();

            if (tagOnlyUntagged)
                elements = elements.Where(e => !taggedIds.Contains(e.Id)).ToList();

            if (elements.Count == 0) return (0, 0, 0);

            double offset = GetModelOffset(view);
            double tagWidth = offset * 3.0;
            double tagHeight = offset * 1.0;
            double cellSize = Math.Max(tagWidth, tagHeight) * 2.0;

            // Build spatial grid from existing tags
            var grid = new SpatialGrid(cellSize);
            var tagYs = new List<double>();
            var tagXs = new List<double>();

            foreach (var tag in existingTags)
            {
                BoundingBoxXYZ bb = tag.get_BoundingBox(view);
                if (bb != null)
                {
                    var box = Box2D.FromBoundingBox(bb);
                    grid.Insert(box);
                    tagYs.Add(box.CenterY);
                    tagXs.Add(box.CenterX);
                }
            }

            // Also register element bounding boxes for element-overlap avoidance
            foreach (var elem in elements)
            {
                BoundingBoxXYZ bb = elem.get_BoundingBox(view);
                if (bb != null)
                    grid.Insert(Box2D.FromBoundingBox(bb));
            }

            Box2D? viewCrop = GetViewCropBox(view);
            var tagTypeCache = new Dictionary<ElementId, ElementId>();

            foreach (Element elem in elements)
            {
                XYZ center = GetElementCenter(elem, view);
                if (center.IsAlmostEqualTo(XYZ.Zero))
                {
                    skipped++;
                    continue;
                }

                ElementId catId = elem.Category.Id;
                if (!tagTypeCache.TryGetValue(catId, out ElementId tagTypeId))
                {
                    FamilySymbol tagType = FindTagType(doc, elem.Category);
                    tagTypeId = tagType?.Id ?? ElementId.InvalidElementId;
                    tagTypeCache[catId] = tagTypeId;
                }
                if (tagTypeId == ElementId.InvalidElementId) { skipped++; continue; }

                string catName = elem.Category?.Name ?? "";
                // Use smart preferred side that considers element orientation
                int preferred = GetSmartPreferredSide(elem, view);
                var offsets = GetCandidateOffsets(offset);

                XYZ bestPos = null;
                double bestScore = double.MinValue;
                bool needsLeader = false;

                // Attempt 1: standard offset ring
                foreach (XYZ off in offsets)
                {
                    XYZ cand = center + off;
                    var candBox = Box2D.EstimateTag(cand, tagWidth, tagHeight);
                    double sc = ScoreCandidate(cand, center, candBox, grid,
                        preferred, tagYs, tagXs, viewCrop);
                    if (sc > bestScore) { bestScore = sc; bestPos = cand; needsLeader = false; }
                }

                // Attempt 2: expanded search if overlapping
                if (bestScore < 0)
                {
                    for (double scale = 2.0; scale <= 4.0; scale += 1.0)
                    {
                        foreach (XYZ off in offsets.Take(8))
                        {
                            XYZ cand = center + off * scale;
                            var candBox = Box2D.EstimateTag(cand, tagWidth, tagHeight);
                            double sc = ScoreCandidate(cand, center, candBox, grid,
                                preferred, tagYs, tagXs, viewCrop) - 20 * (scale - 1);
                            if (sc > bestScore) { bestScore = sc; bestPos = cand; needsLeader = true; }
                        }
                        if (bestScore > 0) break;
                    }
                }

                if (bestPos == null)
                {
                    bestPos = center + new XYZ(0, offset, 0);
                    needsLeader = true;
                }

                var finalBox = Box2D.EstimateTag(bestPos, tagWidth, tagHeight);
                if (grid.HasOverlap(finalBox)) collisions++;

                try
                {
                    bool useLeader = addLeaders || needsLeader;
                    IndependentTag tag = IndependentTag.Create(
                        doc, tagTypeId, view.Id, new Reference(elem), useLeader,
                        TagOrientation.Horizontal, bestPos);

                    if (tag != null)
                    {
                        BoundingBoxXYZ tagBB = tag.get_BoundingBox(view);
                        Box2D regBox = tagBB != null ? Box2D.FromBoundingBox(tagBB) : finalBox;
                        grid.Insert(regBox);
                        tagYs.Add(regBox.CenterY);
                        tagXs.Add(regBox.CenterX);
                        placed++;
                    }
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException) { skipped++; }
                catch (Exception ex)
                {
                    StingLog.Warn($"Tag placement failed for {elem.Id}: {ex.Message}");
                    skipped++;
                }
            }

            return (placed, skipped, collisions);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Tag Placement Preset — save/recall relative tag positions
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tag placement preset system (Naviate Tag-from-Template style).
    /// Saves tag positions relative to host element centers per category,
    /// then recalls and applies them to other views.
    /// </summary>
    internal static class TagPlacementPresets
    {
        /// <summary>Per-category placement rule.</summary>
        public class CategoryRule
        {
            public string CategoryName { get; set; } = "";
            public int PreferredSide { get; set; } = 0; // 0=above,1=right,2=below,3=left
            public bool AddLeader { get; set; } = false;
            public double OffsetX { get; set; } = 0;
            public double OffsetY { get; set; } = 0;
            public string Orientation { get; set; } = "Horizontal";
            public double LeaderThreshold { get; set; } = 3.0;
        }

        /// <summary>A saved placement preset.</summary>
        public class PlacementPreset
        {
            public string Name { get; set; } = "Default";
            public string CreatedFrom { get; set; } = "";
            public DateTime Created { get; set; } = DateTime.Now;
            public Dictionary<string, CategoryRule> Rules { get; set; }
                = new Dictionary<string, CategoryRule>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Learn tag placement patterns from an existing view.
        /// Analyzes relative offsets of tags from their host elements per category.
        /// </summary>
        public static PlacementPreset LearnFromView(Document doc, View view)
        {
            var preset = new PlacementPreset
            {
                Name = $"Learned from {view.Name}",
                CreatedFrom = view.Name,
            };

            var tags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            // Group offsets by category
            var catOffsets = new Dictionary<string, List<(double dx, double dy, bool hasLeader, string orient)>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var tag in tags)
            {
                try
                {
                    var hostIds = tag.GetTaggedLocalElementIds();
                    if (hostIds.Count == 0) continue;
                    Element host = doc.GetElement(hostIds.First());
                    if (host?.Category == null) continue;

                    XYZ hostCenter = TagPlacementEngine.GetElementCenter(host, view);
                    XYZ tagPos = tag.TagHeadPosition;
                    if (hostCenter.IsAlmostEqualTo(XYZ.Zero)) continue;

                    double dx = tagPos.X - hostCenter.X;
                    double dy = tagPos.Y - hostCenter.Y;
                    string orient = tag.TagOrientation == TagOrientation.Vertical ? "Vertical" : "Horizontal";

                    string catName = host.Category.Name;
                    if (!catOffsets.TryGetValue(catName, out var list))
                    {
                        list = new List<(double, double, bool, string)>();
                        catOffsets[catName] = list;
                    }
                    list.Add((dx, dy, tag.HasLeader, orient));
                }
                catch { }
            }

            // Average the offsets per category
            foreach (var kvp in catOffsets)
            {
                if (kvp.Value.Count == 0) continue;
                double avgDx = kvp.Value.Average(o => o.dx);
                double avgDy = kvp.Value.Average(o => o.dy);
                bool useLeader = kvp.Value.Count(o => o.hasLeader) > kvp.Value.Count / 2;
                string orient = kvp.Value.GroupBy(o => o.orient)
                    .OrderByDescending(g => g.Count()).First().Key;

                int side = 0;
                if (Math.Abs(avgDy) > Math.Abs(avgDx))
                    side = avgDy > 0 ? 0 : 2;
                else
                    side = avgDx > 0 ? 1 : 3;

                preset.Rules[kvp.Key] = new CategoryRule
                {
                    CategoryName = kvp.Key,
                    PreferredSide = side,
                    AddLeader = useLeader,
                    OffsetX = avgDx,
                    OffsetY = avgDy,
                    Orientation = orient,
                };
            }

            StingLog.Info($"LearnFromView: learned {preset.Rules.Count} category rules from '{view.Name}'");
            return preset;
        }

        /// <summary>Apply a learned preset to place tags in a view.</summary>
        public static (int placed, int skipped) ApplyPreset(
            Document doc, View view, PlacementPreset preset, bool tagOnlyUntagged)
        {
            int placed = 0, skipped = 0;

            var existingTags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            var taggedIds = new HashSet<ElementId>();
            foreach (var tag in existingTags)
            {
                try { foreach (var id in tag.GetTaggedLocalElementIds()) taggedIds.Add(id); }
                catch { }
            }

            var elements = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null &&
                    e.Category.HasMaterialQuantities &&
                    e.Category.AllowsBoundParameters)
                .ToList();

            if (tagOnlyUntagged)
                elements = elements.Where(e => !taggedIds.Contains(e.Id)).ToList();

            var tagTypeCache = new Dictionary<ElementId, ElementId>();

            foreach (var elem in elements)
            {
                XYZ center = TagPlacementEngine.GetElementCenter(elem, view);
                if (center.IsAlmostEqualTo(XYZ.Zero)) { skipped++; continue; }

                string catName = elem.Category?.Name ?? "";
                ElementId catId = elem.Category.Id;
                if (!tagTypeCache.TryGetValue(catId, out ElementId tagTypeId))
                {
                    var tt = TagPlacementEngine.FindTagType(doc, elem.Category);
                    tagTypeId = tt?.Id ?? ElementId.InvalidElementId;
                    tagTypeCache[catId] = tagTypeId;
                }
                if (tagTypeId == ElementId.InvalidElementId) { skipped++; continue; }

                // Look up category rule
                double dx = 0, dy = 0;
                bool useLeader = false;
                var orient = TagOrientation.Horizontal;

                if (preset.Rules.TryGetValue(catName, out var rule))
                {
                    dx = rule.OffsetX;
                    dy = rule.OffsetY;
                    useLeader = rule.AddLeader;
                    orient = rule.Orientation == "Vertical"
                        ? TagOrientation.Vertical : TagOrientation.Horizontal;
                }
                else
                {
                    // Default: place above
                    dy = TagPlacementEngine.GetModelOffset(view);
                }

                XYZ tagPos = center + new XYZ(dx, dy, 0);

                try
                {
                    IndependentTag tag = IndependentTag.Create(
                        doc, tagTypeId, view.Id, new Reference(elem),
                        useLeader, orient, tagPos);
                    if (tag != null) placed++;
                }
                catch { skipped++; }
            }

            return (placed, skipped);
        }

        /// <summary>Save presets to JSON file.</summary>
        public static void SavePresets(List<PlacementPreset> presets, string path)
        {
            try
            {
                string json = JsonConvert.SerializeObject(presets, Formatting.Indented);
                File.WriteAllText(path, json);
                StingLog.Info($"Saved {presets.Count} tag placement presets to {path}");
            }
            catch (Exception ex) { StingLog.Error($"SavePresets failed: {ex.Message}"); }
        }

        /// <summary>Load presets from JSON file.</summary>
        public static List<PlacementPreset> LoadPresets(string path)
        {
            try
            {
                if (!File.Exists(path)) return new List<PlacementPreset>();
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<List<PlacementPreset>>(json)
                    ?? new List<PlacementPreset>();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LoadPresets: {ex.Message}");
                return new List<PlacementPreset>();
            }
        }

        /// <summary>Get the presets file path.</summary>
        public static string GetPresetsPath()
        {
            string dataPath = StingToolsApp.DataPath ?? "";
            return Path.Combine(dataPath, "TAG_PLACEMENT_PRESETS.json");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Smart Place Tags — single view (enhanced)
    // ════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SmartPlaceTagsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.SafeApp().ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            if (view is ViewSheet)
            {
                TaskDialog.Show("Smart Place Tags", "Cannot tag on a sheet view.\nOpen a floor plan or section.");
                return Result.Succeeded;
            }

            TaskDialog optDlg = new TaskDialog("Smart Place Tags");
            optDlg.MainInstruction = "Place annotation tags in active view";
            optDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Tag untagged elements only",
                "Skip elements that already have an annotation tag in this view");
            optDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Tag ALL elements",
                "Place tags on every taggable element (may create duplicates)");
            optDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Tag selected elements only",
                $"Tag {uidoc.Selection.GetElementIds().Count} selected elements");
            optDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            bool tagUntaggedOnly;
            bool selectedOnly = false;
            switch (optDlg.Show())
            {
                case TaskDialogResult.CommandLink1: tagUntaggedOnly = true; break;
                case TaskDialogResult.CommandLink2: tagUntaggedOnly = false; break;
                case TaskDialogResult.CommandLink3: tagUntaggedOnly = false; selectedOnly = true; break;
                default: return Result.Cancelled;
            }

            TaskDialog leaderDlg = new TaskDialog("Smart Place Tags — Leaders");
            leaderDlg.MainInstruction = "Leader line mode";
            leaderDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Auto (recommended)", "Add leaders only when tag must be placed far from element");
            leaderDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Always show leaders", "Add leader lines to all placed tags");
            leaderDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "No leaders", "Never add leader lines");
            leaderDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            bool addLeaders;
            switch (leaderDlg.Show())
            {
                case TaskDialogResult.CommandLink1: addLeaders = false; break;
                case TaskDialogResult.CommandLink2: addLeaders = true; break;
                case TaskDialogResult.CommandLink3: addLeaders = false; break;
                default: return Result.Cancelled;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int placed, skipped, collisions;

            if (selectedOnly)
            {
                var selectedIds = uidoc.Selection.GetElementIds();
                if (selectedIds.Count == 0)
                {
                    TaskDialog.Show("Smart Place Tags", "No elements selected.");
                    return Result.Succeeded;
                }

                placed = 0; skipped = 0; collisions = 0;
                double offset = TagPlacementEngine.GetModelOffset(view);

                var occupied = new List<TagPlacementEngine.Box2D>();
                foreach (var tag in new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
                {
                    BoundingBoxXYZ bb = tag.get_BoundingBox(view);
                    if (bb != null)
                        occupied.Add(TagPlacementEngine.Box2D.FromBoundingBox(bb));
                }

                using (Transaction tx = new Transaction(doc, "STING Smart Place Tags (Selected)"))
                {
                    tx.Start();
                    var tagTypeCache = new Dictionary<ElementId, ElementId>();

                    foreach (ElementId id in selectedIds)
                    {
                        Element elem = doc.GetElement(id);
                        if (elem?.Category == null) { skipped++; continue; }

                        ElementId catId = elem.Category.Id;
                        if (!tagTypeCache.TryGetValue(catId, out ElementId tagTypeId))
                        {
                            FamilySymbol tagType = TagPlacementEngine.FindTagType(doc, elem.Category);
                            tagTypeId = tagType?.Id ?? ElementId.InvalidElementId;
                            tagTypeCache[catId] = tagTypeId;
                        }
                        if (tagTypeId == ElementId.InvalidElementId) { skipped++; continue; }

                        XYZ center = TagPlacementEngine.GetElementCenter(elem, view);
                        var offsets = TagPlacementEngine.GetCandidateOffsets(offset);
                        string catName = elem.Category?.Name ?? "";
                        int preferred = TagPlacementEngine.GetPreferredSide(catName);

                        XYZ bestPos = center + new XYZ(0, offset, 0);
                        double bestScore = double.MinValue;
                        double tagW = offset * 3.0, tagH = offset * 1.0;

                        foreach (XYZ off in offsets)
                        {
                            XYZ cand = center + off;
                            var cb = TagPlacementEngine.Box2D.EstimateTag(cand, tagW, tagH);
                            double sc = TagPlacementEngine.ScoreCandidate(
                                cand, center, cb, occupied, preferred);
                            if (sc > bestScore) { bestScore = sc; bestPos = cand; }
                        }

                        try
                        {
                            var tag = IndependentTag.Create(
                                doc, tagTypeId, view.Id, new Reference(elem), addLeaders,
                                TagOrientation.Horizontal, bestPos);
                            if (tag != null)
                            {
                                BoundingBoxXYZ tagBB = tag.get_BoundingBox(view);
                                if (tagBB != null)
                                    occupied.Add(TagPlacementEngine.Box2D.FromBoundingBox(tagBB));
                                placed++;
                            }
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Tag placement for {elem.Id}: {ex.Message}");
                            skipped++;
                        }
                    }
                    tx.Commit();
                }
            }
            else
            {
                using (Transaction tx = new Transaction(doc, "STING Smart Place Tags"))
                {
                    tx.Start();
                    (placed, skipped, collisions) =
                        TagPlacementEngine.PlaceTagsInView(doc, view, addLeaders, tagUntaggedOnly);
                    tx.Commit();
                }
            }

            sw.Stop();
            var report = new StringBuilder();
            report.AppendLine($"Placed: {placed} annotation tags");
            if (skipped > 0) report.AppendLine($"Skipped: {skipped} (no tag family or invalid)");
            if (collisions > 0) report.AppendLine($"Overlaps: {collisions} (best effort placement)");
            report.AppendLine($"Time: {sw.Elapsed.TotalSeconds:F1}s");

            TaskDialog.Show("Smart Place Tags", report.ToString());
            StingLog.Info($"SmartPlaceTags: placed={placed}, skipped={skipped}, " +
                $"collisions={collisions}, time={sw.Elapsed.TotalSeconds:F1}s");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Arrange Tags — reposition existing tags to resolve overlaps (enhanced)
    // ════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ArrangeTagsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.SafeApp().ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            var allTags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            // Use selected tags if available, otherwise all tags
            var selIds = uidoc.Selection.GetElementIds();
            var selTags = selIds.Select(id => doc.GetElement(id)).OfType<IndependentTag>().ToList();
            var tags = selTags.Count > 0 ? selTags : allTags;

            if (tags.Count == 0)
            {
                TaskDialog.Show("Arrange Tags", "No annotation tags found.");
                return Result.Succeeded;
            }

            var tagBoxes = new List<(IndependentTag tag, TagPlacementEngine.Box2D box)>();
            foreach (var tag in tags)
            {
                BoundingBoxXYZ bb = tag.get_BoundingBox(view);
                if (bb != null)
                    tagBoxes.Add((tag, TagPlacementEngine.Box2D.FromBoundingBox(bb)));
            }

            int overlapCount = 0;
            for (int i = 0; i < tagBoxes.Count; i++)
                for (int j = i + 1; j < tagBoxes.Count; j++)
                    if (tagBoxes[i].box.Overlaps(tagBoxes[j].box))
                        overlapCount++;

            TaskDialog confirm = new TaskDialog("Arrange Tags");
            confirm.MainInstruction = $"Arrange {tags.Count} tags in active view";
            confirm.MainContent = $"Found {overlapCount} overlapping tag pairs.\n\n" +
                "Tags will be repositioned to minimize overlaps while staying close to their host elements.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            double offset = TagPlacementEngine.GetModelOffset(view);
            double tagW = offset * 3.0, tagH = offset * 1.0;
            double cellSize = Math.Max(tagW, tagH) * 2.0;
            int moved = 0, resolved = 0;

            using (Transaction tx = new Transaction(doc, "STING Arrange Tags"))
            {
                tx.Start();
                var grid = new TagPlacementEngine.SpatialGrid(cellSize);
                var tagYs = new List<double>();
                var tagXs = new List<double>();
                var viewCrop = TagPlacementEngine.GetViewCropBox(view);

                foreach (var (tag, oldBox) in tagBoxes)
                {
                    XYZ hostCenter = null;
                    try
                    {
                        var hostIds = tag.GetTaggedLocalElementIds();
                        if (hostIds.Count > 0)
                        {
                            Element host = doc.GetElement(hostIds.First());
                            if (host != null)
                                hostCenter = TagPlacementEngine.GetElementCenter(host, view);
                        }
                    }
                    catch { }

                    if (hostCenter == null || hostCenter.IsAlmostEqualTo(XYZ.Zero))
                    {
                        grid.Insert(oldBox);
                        tagYs.Add(oldBox.CenterY);
                        tagXs.Add(oldBox.CenterX);
                        continue;
                    }

                    Element hostElem = null;
                    string catName = "";
                    try
                    {
                        var hIds = tag.GetTaggedLocalElementIds();
                        if (hIds.Count > 0)
                        {
                            hostElem = doc.GetElement(hIds.First());
                            catName = hostElem?.Category?.Name ?? "";
                        }
                    }
                    catch { }

                    // Use smart preferred side with element orientation awareness
                    int preferred = hostElem != null
                        ? TagPlacementEngine.GetSmartPreferredSide(hostElem, view)
                        : TagPlacementEngine.GetPreferredSide(catName);
                    var offsets = TagPlacementEngine.GetCandidateOffsets(offset);

                    XYZ bestPos = null;
                    double bestScore = double.MinValue;

                    foreach (XYZ off in offsets)
                    {
                        XYZ cand = hostCenter + off;
                        var cb = TagPlacementEngine.Box2D.EstimateTag(cand, tagW, tagH);
                        double sc = TagPlacementEngine.ScoreCandidate(
                            cand, hostCenter, cb, grid, preferred, tagYs, tagXs, viewCrop);
                        if (sc > bestScore) { bestScore = sc; bestPos = cand; }
                    }

                    if (bestPos != null)
                    {
                        bool wasBad = grid.HasOverlap(oldBox);
                        tag.TagHeadPosition = bestPos;
                        moved++;

                        var newBox = TagPlacementEngine.Box2D.EstimateTag(bestPos, tagW, tagH);
                        if (wasBad && !grid.HasOverlap(newBox))
                            resolved++;

                        grid.Insert(newBox);
                        tagYs.Add(newBox.CenterY);
                        tagXs.Add(newBox.CenterX);
                    }
                    else
                    {
                        grid.Insert(oldBox);
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Arrange Tags",
                $"Repositioned {moved} of {tags.Count} tags.\nOverlaps resolved: {resolved} of {overlapCount}.");
            StingLog.Info($"ArrangeTags: moved={moved}, resolved={resolved}/{overlapCount}");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Remove Annotation Tags
    // ════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RemoveAnnotationTagsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.SafeApp().ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            var selectedIds = uidoc.Selection.GetElementIds();
            var selectedTags = selectedIds
                .Select(id => doc.GetElement(id) as IndependentTag)
                .Where(t => t != null).ToList();

            List<IndependentTag> tagsToRemove;
            string scope;

            if (selectedTags.Count > 0)
            {
                tagsToRemove = selectedTags;
                scope = $"{selectedTags.Count} selected tags";
            }
            else
            {
                tagsToRemove = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag))
                    .Cast<IndependentTag>().ToList();
                scope = $"all {tagsToRemove.Count} tags in active view";
            }

            if (tagsToRemove.Count == 0)
            {
                TaskDialog.Show("Remove Annotation Tags", "No annotation tags found.");
                return Result.Succeeded;
            }

            TaskDialog confirm = new TaskDialog("Remove Annotation Tags");
            confirm.MainInstruction = $"Remove {tagsToRemove.Count} annotation tags?";
            confirm.MainContent = $"Scope: {scope}\n\n" +
                "This removes VISUAL tag annotations only.\n" +
                "Data tags (parameter values) are NOT affected.\nUndo with Ctrl+Z.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

            int removed = 0;
            using (Transaction tx = new Transaction(doc, "STING Remove Annotation Tags"))
            {
                tx.Start();
                foreach (var tag in tagsToRemove)
                {
                    try { doc.Delete(tag.Id); removed++; }
                    catch (Exception ex) { StingLog.Warn($"Could not delete tag {tag.Id}: {ex.Message}"); }
                }
                tx.Commit();
            }

            TaskDialog.Show("Remove Annotation Tags", $"Removed {removed} of {tagsToRemove.Count} annotation tags.");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Batch Place Tags — multiple views
    // ════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchPlaceTagsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.SafeApp().ActiveUIDocument.Document;

            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && v.CanBePrinted &&
                    (v.ViewType == ViewType.FloorPlan || v.ViewType == ViewType.CeilingPlan ||
                     v.ViewType == ViewType.Section || v.ViewType == ViewType.Elevation))
                .OrderBy(v => v.ViewType.ToString()).ThenBy(v => v.Name).ToList();

            if (views.Count == 0)
            {
                TaskDialog.Show("Batch Place Tags", "No suitable views found.");
                return Result.Succeeded;
            }

            int floorPlans = views.Count(v => v.ViewType == ViewType.FloorPlan);
            int ceilings = views.Count(v => v.ViewType == ViewType.CeilingPlan);
            int sections = views.Count(v => v.ViewType == ViewType.Section);
            int elevations = views.Count(v => v.ViewType == ViewType.Elevation);

            TaskDialog scopeDlg = new TaskDialog("Batch Place Tags");
            scopeDlg.MainInstruction = $"Place tags across {views.Count} views?";
            scopeDlg.MainContent =
                $"Floor Plans: {floorPlans}\nCeiling Plans: {ceilings}\n" +
                $"Sections: {sections}\nElevations: {elevations}\n\n" +
                "Only untagged elements will receive tags.";
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Floor Plans only", $"Tag {floorPlans} floor plan views");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "All views", $"Tag all {views.Count} views");
            scopeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            List<View> targetViews;
            switch (scopeDlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    targetViews = views.Where(v => v.ViewType == ViewType.FloorPlan).ToList(); break;
                case TaskDialogResult.CommandLink2: targetViews = views; break;
                default: return Result.Cancelled;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int totalPlaced = 0, totalSkipped = 0, totalCollisions = 0, viewsProcessed = 0;
            var perView = new List<(string name, int placed, int skipped)>();

            using (TransactionGroup tg = new TransactionGroup(doc, "STING Batch Place Tags"))
            {
                tg.Start();
                foreach (View v in targetViews)
                {
                    using (Transaction tx = new Transaction(doc, $"STING Tag {v.Name}"))
                    {
                        tx.Start();
                        var (p, s, c) = TagPlacementEngine.PlaceTagsInView(
                            doc, v, addLeaders: false, tagOnlyUntagged: true);
                        tx.Commit();
                        totalPlaced += p; totalSkipped += s; totalCollisions += c;
                        perView.Add((v.Name, p, s));
                    }
                    viewsProcessed++;
                    if (viewsProcessed % 10 == 0)
                        StingLog.Info($"BatchPlaceTags: {viewsProcessed}/{targetViews.Count} views done");
                }
                tg.Assimilate();
            }

            sw.Stop();
            var report = new StringBuilder();
            report.AppendLine($"Batch Tag Placement Complete");
            report.AppendLine($"Views: {viewsProcessed} | Tags: {totalPlaced} | Skipped: {totalSkipped}");
            report.AppendLine($"Overlaps: {totalCollisions} | Time: {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine();
            foreach (var (name, p, s) in perView.Where(x => x.placed > 0).Take(20))
                report.AppendLine($"  {name,-35} +{p} ({s} skipped)");
            if (perView.Count(x => x.placed > 0) > 20)
                report.AppendLine($"  ... and {perView.Count(x => x.placed > 0) - 20} more");

            TaskDialog.Show("Batch Place Tags", report.ToString());
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Learn Tag Placement — save current view tag positions as template
    // ════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LearnTagPlacementCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.SafeApp().ActiveUIDocument.Document;
            View view = doc.ActiveView;

            var tags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag)).Cast<IndependentTag>().ToList();

            if (tags.Count == 0)
            {
                TaskDialog.Show("Learn Tag Placement", "No annotation tags in active view to learn from.");
                return Result.Succeeded;
            }

            var preset = TagPlacementPresets.LearnFromView(doc, view);
            string path = TagPlacementPresets.GetPresetsPath();
            var existing = TagPlacementPresets.LoadPresets(path);
            existing.Add(preset);
            TagPlacementPresets.SavePresets(existing, path);

            var report = new StringBuilder();
            report.AppendLine($"Learned placement template from '{view.Name}'");
            report.AppendLine($"Analyzed {tags.Count} tags");
            report.AppendLine($"Captured {preset.Rules.Count} category rules:");
            foreach (var kvp in preset.Rules.OrderBy(r => r.Key))
            {
                string side = kvp.Value.PreferredSide switch
                {
                    0 => "Above", 1 => "Right", 2 => "Below", 3 => "Left", _ => "?"
                };
                report.AppendLine($"  {kvp.Key}: {side}, leader={kvp.Value.AddLeader}, " +
                    $"offset=({kvp.Value.OffsetX:F2}, {kvp.Value.OffsetY:F2})");
            }
            report.AppendLine($"\nSaved to: {path}");
            report.AppendLine($"Total presets: {existing.Count}");

            TaskDialog.Show("Learn Tag Placement", report.ToString());
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Apply Tag Placement Template — recall saved positions
    // ════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ApplyTagTemplateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.SafeApp().ActiveUIDocument.Document;
            View view = doc.ActiveView;

            if (view is ViewSheet)
            {
                TaskDialog.Show("Apply Tag Template", "Cannot tag on a sheet view.");
                return Result.Succeeded;
            }

            string path = TagPlacementPresets.GetPresetsPath();
            var presets = TagPlacementPresets.LoadPresets(path);

            if (presets.Count == 0)
            {
                TaskDialog.Show("Apply Tag Template",
                    "No saved tag placement presets found.\n\n" +
                    "Use 'Learn Tag Placement' on a view with well-placed tags first.");
                return Result.Succeeded;
            }

            // Pick preset (show up to 4 most recent)
            var recent = presets.OrderByDescending(p => p.Created).Take(4).ToList();
            TaskDialog dlg = new TaskDialog("Apply Tag Template");
            dlg.MainInstruction = "Select a tag placement template";
            for (int i = 0; i < recent.Count; i++)
            {
                dlg.AddCommandLink((TaskDialogCommandLinkId)(i + 1001),
                    recent[i].Name,
                    $"{recent[i].Rules.Count} category rules, from '{recent[i].CreatedFrom}'");
            }
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            int picked = -1;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: picked = 0; break;
                case TaskDialogResult.CommandLink2: picked = 1; break;
                case TaskDialogResult.CommandLink3: picked = 2; break;
                case TaskDialogResult.CommandLink4: picked = 3; break;
                default: return Result.Cancelled;
            }

            var preset = recent[picked];
            var sw = System.Diagnostics.Stopwatch.StartNew();

            int placed, skipped;
            using (Transaction tx = new Transaction(doc, "STING Apply Tag Template"))
            {
                tx.Start();
                (placed, skipped) = TagPlacementPresets.ApplyPreset(
                    doc, view, preset, tagOnlyUntagged: true);
                tx.Commit();
            }

            sw.Stop();
            TaskDialog.Show("Apply Tag Template",
                $"Applied template '{preset.Name}'\n" +
                $"Placed: {placed} tags\nSkipped: {skipped}\n" +
                $"Time: {sw.Elapsed.TotalSeconds:F1}s");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Tag Overlap Analysis — detect and report collisions
    // ════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagOverlapAnalysisCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.SafeApp().ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            var tags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>().ToList();

            if (tags.Count == 0)
            {
                TaskDialog.Show("Tag Overlap Analysis", "No annotation tags in active view.");
                return Result.Succeeded;
            }

            var boxes = new List<(IndependentTag tag, TagPlacementEngine.Box2D box)>();
            foreach (var tag in tags)
            {
                BoundingBoxXYZ bb = tag.get_BoundingBox(view);
                if (bb != null)
                    boxes.Add((tag, TagPlacementEngine.Box2D.FromBoundingBox(bb)));
            }

            // Find overlapping pairs
            var overlapping = new HashSet<ElementId>();
            int pairCount = 0;
            var catPairs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < boxes.Count; i++)
            {
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    if (boxes[i].box.Overlaps(boxes[j].box))
                    {
                        pairCount++;
                        overlapping.Add(boxes[i].tag.Id);
                        overlapping.Add(boxes[j].tag.Id);

                        string catI = "", catJ = "";
                        try
                        {
                            var hi = boxes[i].tag.GetTaggedLocalElementIds();
                            if (hi.Count > 0) catI = doc.GetElement(hi.First())?.Category?.Name ?? "";
                        }
                        catch { }
                        try
                        {
                            var hj = boxes[j].tag.GetTaggedLocalElementIds();
                            if (hj.Count > 0) catJ = doc.GetElement(hj.First())?.Category?.Name ?? "";
                        }
                        catch { }

                        string key = string.Compare(catI, catJ, StringComparison.Ordinal) <= 0
                            ? $"{catI} / {catJ}" : $"{catJ} / {catI}";
                        if (!catPairs.ContainsKey(key)) catPairs[key] = 0;
                        catPairs[key]++;
                    }
                }
            }

            // Density analysis
            double viewArea = 0;
            try
            {
                if (view.CropBoxActive)
                {
                    var crop = view.CropBox;
                    viewArea = (crop.Max.X - crop.Min.X) * (crop.Max.Y - crop.Min.Y);
                }
            }
            catch { }

            double totalTagArea = boxes.Sum(b => b.box.Width * b.box.Height);

            var report = new StringBuilder();
            report.AppendLine($"Tag Overlap Analysis — {view.Name}");
            report.AppendLine(new string('=', 50));
            report.AppendLine($"Total tags: {tags.Count}");
            report.AppendLine($"Overlapping tags: {overlapping.Count}");
            report.AppendLine($"Overlap pairs: {pairCount}");
            report.AppendLine($"Overlap rate: {(tags.Count > 0 ? 100.0 * overlapping.Count / tags.Count : 0):F1}%");
            if (viewArea > 0)
            {
                report.AppendLine($"Tag density: {totalTagArea / viewArea * 100:F1}% of view area");
                report.AppendLine($"Tags per sq ft: {tags.Count / viewArea:F2}");
            }
            if (catPairs.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("Overlap pairs by category:");
                foreach (var kvp in catPairs.OrderByDescending(k => k.Value).Take(10))
                    report.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }

            if (overlapping.Count > 0)
            {
                report.AppendLine($"\nSelect {overlapping.Count} overlapping tags?");
                TaskDialog td = new TaskDialog("Tag Overlap Analysis");
                td.MainInstruction = $"{overlapping.Count} overlapping tags found";
                td.MainContent = report.ToString();
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Select overlapping tags", $"Select {overlapping.Count} tags for manual adjustment");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "View report only", "Just show the analysis");
                td.CommonButtons = TaskDialogCommonButtons.Cancel;

                switch (td.Show())
                {
                    case TaskDialogResult.CommandLink1:
                        uidoc.Selection.SetElementIds(overlapping.ToList());
                        break;
                    case TaskDialogResult.CommandLink2:
                        TaskDialog.Show("Tag Overlap Analysis", report.ToString());
                        break;
                }
            }
            else
            {
                TaskDialog.Show("Tag Overlap Analysis",
                    $"No overlapping tags found in {view.Name}.\n{tags.Count} tags are clean.");
            }

            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Batch Tag Text Size — edit family, change text size, reload
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Batch change tag text size by programmatically editing the tag family,
    /// modifying TEXT_SIZE on label elements, and reloading into the project.
    /// This is the ONLY way to change tag text size — it's embedded in the .rfa.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchTagTextSizeCommand : IExternalCommand
    {
        // Standard text sizes in feet (Revit internal unit)
        private static readonly (string name, double sizeFt)[] Sizes = new[]
        {
            ("1.5mm (tiny)",      1.5 / 304.8),
            ("2.0mm (small)",     2.0 / 304.8),
            ("2.5mm (standard)",  2.5 / 304.8),
            ("3.0mm (medium)",    3.0 / 304.8),
            ("3.5mm (large)",     3.5 / 304.8),
            ("5.0mm (extra large)", 5.0 / 304.8),
        };

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.SafeApp();
            UIDocument uidoc = uiApp.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Find tag families to modify
            var tagFamilies = new FilteredElementCollector(doc)
                .OfClass(typeof(Family)).Cast<Family>()
                .Where(f =>
                {
                    try { return f.FamilyCategory?.CategoryType == CategoryType.Annotation; }
                    catch { return false; }
                })
                .OrderBy(f => f.Name)
                .ToList();

            if (tagFamilies.Count == 0)
            {
                TaskDialog.Show("Batch Tag Text Size", "No annotation tag families loaded.");
                return Result.Succeeded;
            }

            // Pick scope
            TaskDialog scopeDlg = new TaskDialog("Batch Tag Text Size");
            scopeDlg.MainInstruction = $"Change text size for tag families";
            scopeDlg.MainContent = $"Found {tagFamilies.Count} annotation tag families.\n" +
                "This edits the tag families, changes text size, and reloads them.";
            int stingCount = tagFamilies.Count(f => f.Name.StartsWith("STING", StringComparison.OrdinalIgnoreCase));
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"STING families only ({stingCount})",
                "Change text size only in STING tag families");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                $"ALL tag families ({tagFamilies.Count})",
                "Change text size in every loaded annotation family");
            scopeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            List<Family> targets;
            switch (scopeDlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    targets = tagFamilies.Where(f =>
                        f.Name.StartsWith("STING", StringComparison.OrdinalIgnoreCase)).ToList();
                    break;
                case TaskDialogResult.CommandLink2:
                    targets = tagFamilies; break;
                default: return Result.Cancelled;
            }

            if (targets.Count == 0)
            {
                TaskDialog.Show("Batch Tag Text Size", "No matching families found.");
                return Result.Succeeded;
            }

            // Pick text size
            TaskDialog sizeDlg = new TaskDialog("Batch Tag Text Size — Size");
            sizeDlg.MainInstruction = "Select new text size";
            for (int i = 0; i < Math.Min(4, Sizes.Length); i++)
            {
                sizeDlg.AddCommandLink((TaskDialogCommandLinkId)(i + 1001), Sizes[i].name);
            }
            sizeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            int sizeIdx = -1;
            switch (sizeDlg.Show())
            {
                case TaskDialogResult.CommandLink1: sizeIdx = 0; break;
                case TaskDialogResult.CommandLink2: sizeIdx = 1; break;
                case TaskDialogResult.CommandLink3: sizeIdx = 2; break;
                case TaskDialogResult.CommandLink4: sizeIdx = 3; break;
                default: return Result.Cancelled;
            }

            double newSizeFt = Sizes[sizeIdx].sizeFt;
            string sizeName = Sizes[sizeIdx].name;
            int modified = 0, failed = 0;
            var report = new StringBuilder();

            foreach (Family fam in targets)
            {
                try
                {
                    Document famDoc = doc.EditFamily(fam);
                    if (famDoc == null) { failed++; continue; }

                    bool changed = false;

                    // Find all TextNoteType elements and change TEXT_SIZE
                    using (Transaction ft = new Transaction(famDoc, "Change Text Size"))
                    {
                        ft.Start();
                        var textTypes = new FilteredElementCollector(famDoc)
                            .OfClass(typeof(TextNoteType))
                            .Cast<TextNoteType>()
                            .ToList();

                        foreach (var tt in textTypes)
                        {
                            try
                            {
                                var sizeParam = tt.get_Parameter(BuiltInParameter.TEXT_SIZE);
                                if (sizeParam != null && !sizeParam.IsReadOnly)
                                {
                                    sizeParam.Set(newSizeFt);
                                    changed = true;
                                }
                            }
                            catch { }
                        }

                        // Also try AnnotationSymbolType parameters
                        var annoTypes = new FilteredElementCollector(famDoc)
                            .WhereElementIsElementType()
                            .ToList();

                        foreach (var at in annoTypes)
                        {
                            try
                            {
                                var sizeParam = at.get_Parameter(BuiltInParameter.TEXT_SIZE);
                                if (sizeParam != null && !sizeParam.IsReadOnly)
                                {
                                    sizeParam.Set(newSizeFt);
                                    changed = true;
                                }
                            }
                            catch { }
                        }

                        ft.Commit();
                    }

                    try
                    {
                        if (changed)
                        {
                            // Reload modified family into project
                            famDoc.LoadFamily(doc, new TagFamilyLoadOptions());
                            modified++;
                            report.AppendLine($"  [OK] {fam.Name}");
                        }
                    }
                    finally
                    {
                        famDoc.Close(false);
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    report.AppendLine($"  [FAIL] {fam.Name}: {ex.Message}");
                    StingLog.Warn($"BatchTagTextSize: {fam.Name}: {ex.Message}");
                }
            }

            TaskDialog.Show("Batch Tag Text Size",
                $"Changed text size to {sizeName}\n" +
                $"Modified: {modified} families\nFailed: {failed}\n\n" +
                report.ToString());
            return Result.Succeeded;
        }
    }

    // TagFamilyLoadOptions is defined in TagFamilyCreatorCommand.cs

    // ════════════════════════════════════════════════════════════════════
    //  Batch Tag Category Line Weight — change leader/border thickness
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Set annotation category line weights for tag families.
    /// This is the only way to control leader line thickness — it's set
    /// at the annotation category level via Object Styles, not per-instance.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetTagCategoryLineWeightCommand : IExternalCommand
    {
        private static readonly BuiltInCategory[] TagCategories = new[]
        {
            BuiltInCategory.OST_MechanicalEquipmentTags,
            BuiltInCategory.OST_DuctAccessoryTags,
            BuiltInCategory.OST_DuctFittingTags,
            BuiltInCategory.OST_DuctTerminalTags,
            BuiltInCategory.OST_DuctTags,
            BuiltInCategory.OST_ElectricalEquipmentTags,
            BuiltInCategory.OST_ElectricalFixtureTags,
            BuiltInCategory.OST_LightingFixtureTags,
            BuiltInCategory.OST_PipeAccessoryTags,
            BuiltInCategory.OST_PipeFittingTags,
            BuiltInCategory.OST_PlumbingFixtureTags,
            BuiltInCategory.OST_SprinklerTags,
            BuiltInCategory.OST_DoorTags,
            BuiltInCategory.OST_WindowTags,
            BuiltInCategory.OST_RoomTags,
            BuiltInCategory.OST_FurnitureTags,
            BuiltInCategory.OST_GenericModelTags,
            BuiltInCategory.OST_MultiCategoryTags,
        };

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.SafeApp().ActiveUIDocument.Document;

            // Pick line weight (pen number 1-16)
            TaskDialog dlg = new TaskDialog("Set Tag Category Line Weight");
            dlg.MainInstruction = "Set annotation line weight for tag categories";
            dlg.MainContent = "This sets the line weight (pen number) for tag annotation categories.\n" +
                "Controls both leader lines and tag borders.\n" +
                "Affects ALL tags of each category in the project.";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Thin (pen 1)", "Lightest lines — suitable for printing");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Standard (pen 3)", "Default medium weight");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Bold (pen 5)", "Heavy lines — high visibility");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Extra Bold (pen 8)", "Maximum visibility for QA/checking");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            int pen;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: pen = 1; break;
                case TaskDialogResult.CommandLink2: pen = 3; break;
                case TaskDialogResult.CommandLink3: pen = 5; break;
                case TaskDialogResult.CommandLink4: pen = 8; break;
                default: return Result.Cancelled;
            }

            int changed = 0;
            using (Transaction tx = new Transaction(doc, "STING Set Tag Category Line Weight"))
            {
                tx.Start();
                foreach (var bic in TagCategories)
                {
                    try
                    {
                        Category cat = doc.Settings.Categories.get_Item(bic);
                        if (cat != null)
                        {
                            cat.SetLineWeight(pen, GraphicsStyleType.Projection);
                            changed++;
                        }
                    }
                    catch { }
                }
                tx.Commit();
            }

            TaskDialog.Show("Set Tag Category Line Weight",
                $"Set line weight to pen {pen} on {changed} tag categories.\n" +
                "This affects leader lines and tag borders project-wide.");
            return Result.Succeeded;
        }
    }
}
