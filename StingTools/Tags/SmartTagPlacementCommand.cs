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
    /// with grid-based spatial index, actual tag BB measurement, STING
    /// priority cascading, and tag placement template save/recall.
    ///
    /// Key improvements over v1:
    ///   - Grid-based spatial index for O(1) average overlap queries (was O(n²))
    ///   - Actual tag bounding box measurement via temporary transaction rollback
    ///   - 16 candidate positions (8 cardinal + 8 intermediate at 1.5x offset)
    ///   - Alignment bonus: tags aligned with existing nearby tags score higher
    ///   - Performance: suppress annotation regeneration during batch placement
    ///   - STING priority cascade: try preferred side first, then rotate
    ///   - Tag placement template system: save/recall relative positions per category
    /// </summary>
    internal static class TagPlacementEngine
    {
        /// <summary>ENH-03: Default clearance margin (in feet) for leader elbow avoidance.</summary>
        private const double LeaderClearanceMargin = 0.5;

        // ── B-1 SpatialGrid view cache ───────────────────────────────────
        // Memoise the (existing-tag) SpatialGrid per (docKey, viewId) so two
        // back-to-back placement runs against the same view don't both walk
        // every IndependentTag and rebuild a 300-entry grid. TTL of 30 s is
        // sufficient for sequential command runs and falls back to a fresh
        // build when the user makes intervening edits.
        private static readonly object _spatialGridCacheLock = new object();
        private static readonly Dictionary<(string docKey, long viewId), (SpatialGrid grid, DateTime stamp, double cellSize, int tagCount)>
            _spatialGridCache = new Dictionary<(string, long), (SpatialGrid, DateTime, double, int)>();
        private static readonly TimeSpan _spatialGridTtl = TimeSpan.FromSeconds(30);

        /// <summary>
        /// B-1: invalidate the cached SpatialGrid for a given view (or for the
        /// whole document when viewId is invalid). Public so external callers
        /// can flush after non-trivial tag edits.
        /// </summary>
        public static void InvalidateViewCache(Document doc, ElementId viewId)
        {
            string docKey = doc?.PathName ?? doc?.Title ?? "";
            lock (_spatialGridCacheLock)
            {
                if (viewId == null || viewId == ElementId.InvalidElementId)
                {
                    var keysToRemove = _spatialGridCache.Keys
                        .Where(k => string.Equals(k.docKey, docKey, StringComparison.Ordinal))
                        .ToList();
                    foreach (var k in keysToRemove) _spatialGridCache.Remove(k);
                }
                else
                {
                    var key = (docKey, viewId.Value);
                    if (_spatialGridCache.ContainsKey(key)) _spatialGridCache.Remove(key);
                }
            }
        }

        internal static bool TryGetCachedSpatialGrid(Document doc, View view, double cellSize, int existingTagCount, out SpatialGrid grid)
        {
            grid = null;
            if (doc == null || view == null) return false;
            string docKey = doc.PathName ?? doc.Title ?? "";
            var key = (docKey, view.Id.Value);
            lock (_spatialGridCacheLock)
            {
                if (_spatialGridCache.TryGetValue(key, out var entry))
                {
                    bool fresh = (DateTime.UtcNow - entry.stamp) < _spatialGridTtl;
                    bool cellMatch = Math.Abs(entry.cellSize - cellSize) < 1e-9;
                    bool countMatch = entry.tagCount == existingTagCount;
                    if (fresh && cellMatch && countMatch)
                    {
                        grid = entry.grid;
                        return true;
                    }
                    _spatialGridCache.Remove(key);
                }
            }
            return false;
        }

        internal static void StoreSpatialGrid(Document doc, View view, double cellSize, int existingTagCount, SpatialGrid grid)
        {
            if (doc == null || view == null || grid == null) return;
            string docKey = doc.PathName ?? doc.Title ?? "";
            var key = (docKey, view.Id.Value);
            lock (_spatialGridCacheLock)
            {
                _spatialGridCache[key] = (grid, DateTime.UtcNow, cellSize, existingTagCount);
            }
        }

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

                // GAP-STP-01 FIX: Use HashSet<Box2D> with value equality instead of HashSet<int>
                // with GetHashCode(). Two distinct Box2D values can hash to the same int,
                // causing missed overlap checks and overlapping placed tags.
                var checked_ = new HashSet<Box2D>();
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    for (int cy = minCy; cy <= maxCy; cy++)
                    {
                        long key = CellKey(cx, cy);
                        if (!_cells.TryGetValue(key, out var list)) continue;
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (!checked_.Add(list[i])) continue;
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

                var checked_ = new HashSet<Box2D>();
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    for (int cy = minCy; cy <= maxCy; cy++)
                    {
                        long key = CellKey(cx, cy);
                        if (!_cells.TryGetValue(key, out var list)) continue;
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (!checked_.Add(list[i])) continue;
                            if (candidate.Overlaps(list[i]))
                                count++;
                        }
                    }
                }
                return count;
            }
        }

        // ── Directional offset configuration ─────────────────────────

        /// <summary>
        /// Per-axis offset multipliers for directional tag placement.
        /// Each multiplier scales the base offset for that cardinal direction.
        /// Ring2Scale controls how much further ring 2 positions are placed.
        /// </summary>
        public class DirectionalOffsetConfig
        {
            public double NOffset { get; set; } = 1.0;
            public double EOffset { get; set; } = 1.0;
            public double SOffset { get; set; } = 1.0;
            public double WOffset { get; set; } = 1.0;
            public double Ring2Scale { get; set; } = 1.5;
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

        /// <summary>
        /// Directional 16-position candidate generation with per-axis offset multipliers.
        /// Each cardinal direction uses its own multiplier from config, and diagonals
        /// combine the relevant axis multipliers. Ring 2 positions are scaled by config.Ring2Scale.
        /// </summary>
        public static XYZ[] GetCandidateOffsets(double offset, DirectionalOffsetConfig config)
        {
            if (config == null) return GetCandidateOffsets(offset);

            double n = offset * config.NOffset;
            double e = offset * config.EOffset;
            double s = offset * config.SOffset;
            double w = offset * config.WOffset;
            double r2 = config.Ring2Scale;

            return new[]
            {
                // Ring 1: cardinal directions with per-axis multipliers
                new XYZ(0, n, 0),            // N  (P1)
                new XYZ(e, 0, 0),            // E  (P2)
                new XYZ(0, -s, 0),           // S  (P3)
                new XYZ(-w, 0, 0),           // W  (P4)
                new XYZ(e, n, 0),            // NE (P5)
                new XYZ(e, -s, 0),           // SE (P6)
                new XYZ(-w, -s, 0),          // SW (P7)
                new XYZ(-w, n, 0),           // NW (P8)
                // Ring 2: scaled by Ring2Scale
                new XYZ(0, n * r2, 0),                       // N far  (P9)
                new XYZ(e * r2, 0, 0),                       // E far  (P10)
                new XYZ(0, -s * r2, 0),                      // S far  (P11)
                new XYZ(-w * r2, 0, 0),                      // W far  (P12)
                new XYZ(e * r2 * 0.7, n * r2 * 0.7, 0),     // NE far (P13)
                new XYZ(e * r2 * 0.7, -s * r2 * 0.7, 0),    // SE far (P14)
                new XYZ(-w * r2 * 0.7, -s * r2 * 0.7, 0),   // SW far (P15)
                new XYZ(-w * r2 * 0.7, n * r2 * 0.7, 0),    // NW far (P16)
            };
        }

        /// <summary>
        /// Pack 4 — anchor-aware candidate generator. Reads STING_TAG_ANCHOR_X_MM
        /// and STING_TAG_ANCHOR_Y_MM off the host element's family type and
        /// shifts the whole 16-candidate ring by that vector. Families that
        /// declare no anchor (the majority today) fall back to the zero-offset
        /// ring. Pack 2 directional clearance and Pack 3 variant resolution
        /// are independent — tag anchor only changes where the ring sits
        /// relative to the element centroid.
        /// </summary>
        public static XYZ[] GetCandidateOffsetsWithAnchor(double offset, Element host)
        {
            var ring = GetCandidateOffsets(offset);
            if (host == null) return ring;
            double dxMm = ReadAnchorMm(host, "STING_TAG_ANCHOR_X_MM");
            double dyMm = ReadAnchorMm(host, "STING_TAG_ANCHOR_Y_MM");
            if (dxMm == 0 && dyMm == 0) return ring;
            double dxFt = dxMm / 304.8;
            double dyFt = dyMm / 304.8;
            var shifted = new XYZ[ring.Length];
            for (int i = 0; i < ring.Length; i++)
                shifted[i] = new XYZ(ring[i].X + dxFt, ring[i].Y + dyFt, ring[i].Z);
            return shifted;
        }

        /// <summary>
        /// Pack 4 — reads an integer priority from TAG_PRIORITY_INT on the
        /// element's type, falling back to 5 (mid-range). Higher wins when
        /// two tags compete for the same location.
        /// </summary>
        public static int ReadTagPriority(Element host)
        {
            if (host == null) return 5;
            try
            {
                Element type = null;
                try { type = host.Document.GetElement(host.GetTypeId()); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                var p = type?.LookupParameter("TAG_PRIORITY_INT");
                if (p != null && p.HasValue && p.StorageType == StorageType.Integer)
                {
                    int v = p.AsInteger();
                    if (v >= 0 && v <= 10) return v;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ReadTagPriority {host?.Id}: {ex.Message}"); }
            return 5;
        }

        /// <summary>
        /// Pack 4 — reads the clustering key that identifies tags which can
        /// collapse into a single "group" annotation. Empty string means no
        /// clustering. DeclusterTagsCommand reads this to decide merging.
        /// </summary>
        public static string ReadTagClusterKey(Element host)
        {
            if (host == null) return "";
            try
            {
                Element type = null;
                try { type = host.Document.GetElement(host.GetTypeId()); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                return type?.LookupParameter("TAG_CLUSTER_KEY_TXT")?.AsString()
                    ?? host.LookupParameter("TAG_CLUSTER_KEY_TXT")?.AsString()
                    ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; }
        }

        /// <summary>
        /// Pack 4 — reads the family hint steering DrawingDispatcher to prefer a
        /// particular tag family for this category. Returns empty when not set.
        /// </summary>
        public static string ReadTagFamilyHint(Element host)
        {
            if (host == null) return "";
            try
            {
                Element type = null;
                try { type = host.Document.GetElement(host.GetTypeId()); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                return type?.LookupParameter("TAG_FAMILY_HINT_TXT")?.AsString()
                    ?? host.LookupParameter("TAG_FAMILY_HINT_TXT")?.AsString()
                    ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; }
        }

        /// <summary>
        /// Pack 4 — reads display-scale limits (1:N). Tags honour these in
        /// view visibility filtering. Returns (min, max) or (0, 0) for
        /// "no constraint".
        /// </summary>
        public static (int min, int max) ReadTagScaleRange(Element host)
        {
            if (host == null) return (0, 0);
            try
            {
                Element type = null;
                try { type = host.Document.GetElement(host.GetTypeId()); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                int mn = ReadIntInternal(type, "TAG_DISPLAY_SCALE_MIN_INT");
                int mx = ReadIntInternal(type, "TAG_DISPLAY_SCALE_MAX_INT");
                return (mn, mx);
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return (0, 0); }
        }

        private static int ReadIntInternal(Element el, string paramName)
        {
            if (el == null) return 0;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || !p.HasValue || p.StorageType != StorageType.Integer) return 0;
                return p.AsInteger();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
        }

        private static double ReadAnchorMm(Element host, string paramName)
        {
            try
            {
                Element type = null;
                try { type = host.Document.GetElement(host.GetTypeId()); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                var p = type?.LookupParameter(paramName) ?? host.LookupParameter(paramName);
                if (p == null || !p.HasValue) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble() * 304.8;  // feet → mm
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ReadAnchorMm({paramName}) {host?.Id}: {ex.Message}");
            }
            return 0;
        }

        /// <summary>
        /// Back-compat shim. Reads the cap from <see cref="Core.ScaleTiers.OffsetCapFt"/>,
        /// which is driven by <c>Data/SCALE_TIERS.json</c> or a per-project
        /// <c>project_config.json:SCALE_TIERS.offset_cap_ft</c> override. Writing
        /// to the setter is ignored — use <c>ScaleTiers.SaveProjectOverride</c>.
        /// </summary>
        public static double MaxOffsetCapFt
        {
            get => Core.ScaleTiers.OffsetCapFt;
            set { /* cap is config-driven now; setter kept for API back-compat */ }
        }

        /// <summary>
        /// Scale-tier-aware offset. Tier mm and cap come from
        /// <see cref="Core.ScaleTiers"/>, which resolves per-project
        /// overrides then the bundled SCALE_TIERS.json then a hardcoded
        /// fallback. Result is mm → ft × viewScale, clamped to the cap.
        /// </summary>
        /// <summary>
        /// B-3: estimate a tag's bounding box when get_BoundingBox returns
        /// null. Uses the view's tag-text height × character count as a width
        /// approximation centred on the tag's head position. Returns
        /// <see cref="Box2D.IsEmpty"/> when no head position is available.
        /// </summary>
        internal static Box2D EstimateTagBoxFallback(IndependentTag tag, View view, double tagWidth, double tagHeight)
        {
            try
            {
                XYZ head = tag?.TagHeadPosition;
                if (head == null) return new Box2D(0, 0, 0, 0);
                string text = "";
                try { text = tag.TagText ?? ""; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                int chars = Math.Max(8, text.Length);
                // Tag text width ~ tagWidth * chars/8 (calibrated to 8-char base width).
                double estW = Math.Max(tagWidth, tagWidth * chars / 8.0);
                double estH = tagHeight;
                return new Box2D(
                    head.X - estW / 2.0, head.Y - estH / 2.0,
                    head.X + estW / 2.0, head.Y + estH / 2.0);
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return new Box2D(0, 0, 0, 0); }
        }

        public static double GetModelOffset(View view, double baseOffset = 0.01)
            => GetModelOffset(view, null, baseOffset);

        /// <summary>
        /// Phase 165 — overload that applies the per-category multiplier from
        /// SCALE_CATEGORY_MULTIPLIERS (Tag Studio Scale tab sliders) to the
        /// resolved tier offset. Pass an Element to look up its category and
        /// multiply automatically; pass null for the base behaviour.
        /// </summary>
        public static double GetModelOffset(View view, Element host, double baseOffset = 0.01)
        {
            int viewScale = (view != null && view.Scale > 0) ? view.Scale : 100;
            Core.ScaleTiers.Tier tier = Core.ScaleTiers.ForView(view);
            double offsetFt = (tier.OffsetMm / 304.8) * viewScale;
            // Per-category multiplier from the Scale tab sliders. Defaults to 1.0
            // for unmapped categories, so callers that pass null host behave
            // exactly as before (offsetFt unchanged).
            if (host != null)
            {
                string key = MultiplierKeyForCategory(host);
                if (!string.IsNullOrEmpty(key))
                {
                    double mult = Core.ScaleTiers.GetCategoryMultiplier(key);
                    if (mult > 0 && Math.Abs(mult - 1.0) > 0.001) offsetFt *= mult;
                }
            }
            return Math.Min(offsetFt, Core.ScaleTiers.OffsetCapFt);
        }

        // Phase 165 — map a Revit category to one of the four multiplier
        // keys exposed in the Scale tab. Returns null when no bucket matches
        // so the offset is left untouched.
        private static string MultiplierKeyForCategory(Element host)
        {
            try
            {
                string cat = host?.Category?.Name ?? "";
                if (string.IsNullOrEmpty(cat)) return null;
                if (cat.IndexOf("Duct", StringComparison.OrdinalIgnoreCase) >= 0)     return "DUCTS";
                if (cat.IndexOf("Pipe", StringComparison.OrdinalIgnoreCase) >= 0)     return "PIPES";
                // Equipment buckets — Mechanical Equipment, Electrical Equipment
                if (cat.IndexOf("Equipment", StringComparison.OrdinalIgnoreCase) >= 0) return "EQUIPMENT";
                // Fixture buckets — Lighting Fixtures, Plumbing Fixtures, Electrical Fixtures
                if (cat.IndexOf("Fixture", StringComparison.OrdinalIgnoreCase) >= 0)   return "FIXTURES";
                return null;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
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
        public struct Box2D : IEquatable<Box2D>
        {
            public double MinX, MinY, MaxX, MaxY;

            public Box2D(double minX, double minY, double maxX, double maxY)
            {
                MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
            }

            /// <summary>B-3: zero-extent placeholder (used when bbox estimation fails).</summary>
            public bool IsEmpty => MinX == 0 && MaxX == 0 && MinY == 0 && MaxY == 0;

            public bool Overlaps(Box2D other)
            {
                return MinX < other.MaxX && MaxX > other.MinX
                    && MinY < other.MaxY && MaxY > other.MinY;
            }

            public bool Equals(Box2D other)
            {
                return MinX == other.MinX && MinY == other.MinY
                    && MaxX == other.MaxX && MaxY == other.MaxY;
            }

            public override bool Equals(object obj) => obj is Box2D b && Equals(b);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + MinX.GetHashCode();
                    hash = hash * 31 + MinY.GetHashCode();
                    hash = hash * 31 + MaxX.GetHashCode();
                    hash = hash * 31 + MaxY.GetHashCode();
                    return hash;
                }
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
        /// view-edge penalty, and STING priority cascade.
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

            // Preferred side bonus (STING priority)
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
            catch (Exception ex) { StingLog.Warn($"Get element bounding box for preferred side: {ex.Message}"); }

            return categoryDefault;
        }

        // ── Tag type finder ──────────────────────────────────────────

        /// <summary>
        /// Find a tag family type for the given element category.
        /// Priority: STING family > category name match > multi-category/generic.
        /// </summary>
        // TAG-M-04: Single atomic tuple replaces two separate static fields — prevents torn read
        // where one thread sees new docKey but stale types list (or null list with valid key).
        private static (string docKey, List<FamilySymbol> types) _tagTypeCache;

        /// <summary>Clear cached tag types on document close/switch.</summary>
        public static void ClearTagTypeCache()
        {
            // TAG-M-04: Assign atomically — both fields clear in one write.
            _tagTypeCache = (null, null);
        }

        public static FamilySymbol FindTagType(Document doc, Category elementCategory)
        {
            if (elementCategory == null) return null;
            string catName = elementCategory.Name ?? "";

            string docKey = doc.PathName ?? doc.Title ?? "Untitled";
            // TAG-M-04: Snapshot the tuple atomically before checking — avoids torn read.
            var cacheSnapshot = _tagTypeCache;
            if (cacheSnapshot.types == null || cacheSnapshot.docKey != docKey)
            {
                var newTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(fs => fs.Category != null &&
                        fs.Category.CategoryType == CategoryType.Annotation)
                    .ToList();
                // TAG-M-04: Assign atomically as a single tuple write.
                _tagTypeCache = (docKey, newTypes);
                cacheSnapshot = _tagTypeCache;
            }
            var tagTypes = cacheSnapshot.types;

            // Pass 1: STING tag family
            string stingName = GetStingFamilyName(elementCategory);
            foreach (var tt in tagTypes)
            {
                try
                {
                    if (tt.Family?.Name?.Equals(stingName, StringComparison.OrdinalIgnoreCase) == true)
                        return tt;
                }
                catch (Exception ex) { StingLog.Warn($"Check STING tag family match: {ex.Message}"); }
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
                catch (Exception ex) { StingLog.Warn($"Check category name tag match: {ex.Message}"); }
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return $"{TagFamilyConfig.FamilyPrefix} - {cat.Name} Tag"; }
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
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
                    catch (Exception ex) { StingLog.Warn($"Hide annotation category {cat.Id}: {ex.Message}"); }
                }
                if (hidden.Count > 0)
                {
                    // doc.Regenerate() REMOVED — causes native Revit crashes (see StingCommandHandler.cs:759)
                }
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
                    catch (Exception ex) { StingLog.Warn($"Restore annotation category {catId}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"RestoreAnnotations: {ex.Message}"); }
        }

        // ── Enhanced batch placement ─────────────────────────────────

        /// <summary>
        /// Categorised skip reasons populated by the most recent <see cref="PlaceTagsInView"/> call.
        /// Lets callers report a precise breakdown instead of "no tag family or invalid".
        /// </summary>
        public class SkipBreakdown
        {
            public int NoCenter;             // element center at origin / outside view bounds
            public int NoTagFamily;          // no FamilySymbol (tag) resolved for category
            public int TagCreationFailed;    // Revit InvalidOperationException during IndependentTag.Create
            public int OtherException;       // any other exception during placement
            public int Total => NoCenter + NoTagFamily + TagCreationFailed + OtherException;
            public string Format()
                => Total == 0 ? "0"
                              : $"{Total} (no tag family: {NoTagFamily}, creation failed: {TagCreationFailed}, " +
                                $"outside bounds: {NoCenter}, other: {OtherException})";
        }

        /// <summary>Skip breakdown from the most recent PlaceTagsInView invocation.</summary>
        public static SkipBreakdown LastSkipBreakdown { get; private set; } = new SkipBreakdown();

        /// <summary>
        /// B-4: filter the candidate element list down to those whose derived
        /// discipline matches the active view's <see cref="DrawingType.Discipline"/>.
        /// Returns the number of elements dropped. No-op when the view is not
        /// stamped, the drawing type can't be resolved, or its discipline is "*"
        /// / empty (all-discipline / coordination view).
        /// </summary>
        internal static int ApplyDrawingTypeDisciplineFilter(
            Document doc, View view, List<Element> elements)
        {
            if (doc == null || view == null || elements == null || elements.Count == 0) return 0;
            string dtId = null;
            // GAP-N: route through Stamper.Read so a template-controlled
            // pack=…|cs=… stamp doesn't leak into the registry lookup.
            try { dtId = StingTools.Core.Drawing.DrawingTypeStamper.Read(view); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
            if (string.IsNullOrWhiteSpace(dtId)) return 0;

            StingTools.Core.Drawing.DrawingType dt;
            try { dt = StingTools.Core.Drawing.DrawingTypeRegistry.Get(doc, dtId); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
            if (dt == null) return 0;
            string disc = dt.Discipline;
            if (string.IsNullOrWhiteSpace(disc) || disc == "*") return 0;

            int before = elements.Count;
            elements.RemoveAll(e =>
            {
                try
                {
                    string elDisc = ParameterHelpers.GetString(e, ParamRegistry.DISC);
                    if (string.IsNullOrEmpty(elDisc))
                    {
                        string cat = ParameterHelpers.GetCategoryName(e);
                        if (!TagConfig.DiscMap.TryGetValue(cat, out elDisc) || string.IsNullOrEmpty(elDisc))
                            return false; // unknown discipline — leave for placement, no false drops
                    }
                    return !string.Equals(elDisc, disc, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
            });
            return before - elements.Count;
        }

        /// <summary>
        /// Enhanced tag placement with grid spatial index, 16 candidates, alignment
        /// bonus, view crop boundary awareness, and performance optimization.
        /// </summary>
        public static (int placed, int skipped, int collisions) PlaceTagsInView(
            Document doc, View view, bool addLeaders, bool tagOnlyUntagged)
        {
            var sb = new SkipBreakdown();
            LastSkipBreakdown = sb;
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
                catch (Exception ex) { StingLog.Warn($"Get tagged element IDs: {ex.Message}"); }
            }

            var elements = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null &&
                    e.Category.HasMaterialQuantities &&
                    e.Category.AllowsBoundParameters)
                .ToList();

            if (tagOnlyUntagged)
                elements = elements.Where(e => !taggedIds.Contains(e.Id)).ToList();

            // B-4: when the active view is stamped with a DrawingType, restrict
            // visual placement to elements whose derived discipline matches the
            // drawing type's discipline (skip when the type's discipline is "*"
            // or empty). Avoids planting electrical tags on architectural plans.
            int filteredOut = ApplyDrawingTypeDisciplineFilter(doc, view, elements);
            if (filteredOut > 0)
                StingLog.Info($"SmartTagPlacement: DrawingType discipline filter dropped {filteredOut} of {elements.Count + filteredOut} candidate elements");

            if (elements.Count == 0) return (0, 0, 0);

            // SmartTagPlacement data-tag prerequisite: run RunFullPipeline on untagged
            // elements before placing visual annotations. This ensures TAG1 + containers
            // are populated so the visual tags display meaningful data.
            int pipelineRan = 0;
            try
            {
                var untaggedForPipeline = elements.Where(e =>
                    string.IsNullOrEmpty(ParameterHelpers.GetString(e, ParamRegistry.TAG1))).ToList();
                if (untaggedForPipeline.Count > 0)
                {
                    var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
                    var (tagIdx, seqCtrs) = TagConfig.BuildTagIndexAndCounters(doc);
                    var formulas = TagPipelineHelper.LoadFormulas();
                    var grids = TagPipelineHelper.LoadGridLines(doc);
                    foreach (var el in untaggedForPipeline)
                    {
                        if (TagPipelineHelper.RunFullPipeline(doc, el, popCtx, tagIdx, seqCtrs,
                            formulas, grids, overwrite: false, skipComplete: true,
                            collisionMode: TagCollisionMode.AutoIncrement))
                            pipelineRan++;
                    }
                    if (pipelineRan > 0)
                    {
                        TagConfig.SaveSeqSidecar(doc, seqCtrs);
                        StingLog.Info($"SmartTagPlacement: auto-tagged {pipelineRan} untagged elements before visual placement");
                    }
                }
            }
            catch (Exception pipeEx)
            {
                StingLog.Warn($"SmartTagPlacement data-tag prerequisite: {pipeEx.Message}");
            }

            double offset = GetModelOffset(view);
            double tagWidth = offset * 3.0;
            double tagHeight = offset * 1.0;
            double cellSize = Math.Max(tagWidth, tagHeight) * 2.0;

            // B-2: pre-sort placement candidates by TAG_PRIORITY_INT desc so
            // critical (priority 10) elements claim optimal positions first
            // and only optional (priority 0) elements end up with leaders.
            // Stable sort is preserved by OrderByDescending (LINQ).
            elements = elements
                .OrderByDescending(e => ParameterHelpers.GetInt(e, "TAG_PRIORITY_INT", 5))
                .ToList();

            // B-1: try the cache before walking every existing tag.
            var tagYs = new List<double>();
            var tagXs = new List<double>();
            SpatialGrid grid;
            bool gridFromCache = TryGetCachedSpatialGrid(doc, view, cellSize, existingTags.Count, out grid);
            if (gridFromCache)
            {
                // Cached grid still needs the per-tag center coordinates for
                // alignment scoring — those are cheap to recompute from the
                // existing-tag bounding boxes. B-3: do that without the
                // TransactionGroup workaround since the grid itself is reused.
                foreach (var tag in existingTags)
                {
                    BoundingBoxXYZ bb = tag.get_BoundingBox(view);
                    if (bb == null) continue;
                    var box = Box2D.FromBoundingBox(bb);
                    tagYs.Add(box.CenterY);
                    tagXs.Add(box.CenterX);
                }
            }
            else
            {
                grid = new SpatialGrid(cellSize);
                // B-3: collect bounding boxes in a batched pass so we don't
                // open one TransactionGroup per tag. Tags whose bbox is null
                // get a width-by-text approximation in
                // EstimateTagBoxFallback so the grid still reserves space.
                foreach (var tag in existingTags)
                {
                    BoundingBoxXYZ bb = tag.get_BoundingBox(view);
                    Box2D box;
                    if (bb != null)
                    {
                        box = Box2D.FromBoundingBox(bb);
                    }
                    else
                    {
                        box = EstimateTagBoxFallback(tag, view, tagWidth, tagHeight);
                        if (box.IsEmpty) continue;
                    }
                    grid.Insert(box);
                    tagYs.Add(box.CenterY);
                    tagXs.Add(box.CenterX);
                }

                // Also register element bounding boxes for element-overlap avoidance
                foreach (var elem in elements)
                {
                    BoundingBoxXYZ bb = elem.get_BoundingBox(view);
                    if (bb != null)
                        grid.Insert(Box2D.FromBoundingBox(bb));
                }

                StoreSpatialGrid(doc, view, cellSize, existingTags.Count, grid);
            }

            Box2D? viewCrop = GetViewCropBox(view);
            var tagTypeCache = new Dictionary<ElementId, ElementId>();

            // Load category scale multipliers from presets if available
            var scaleMultipliers = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string presetsPath = TagPlacementPresets.GetPresetsPath();
                var presets = TagPlacementPresets.LoadPresets(presetsPath);
                if (presets.Count > 0)
                {
                    var latest = presets[presets.Count - 1];
                    foreach (var kvp in latest.Rules)
                    {
                        if (Math.Abs(kvp.Value.ScaleMultiplier - 1.0) > 0.001)
                            scaleMultipliers[kvp.Key] = kvp.Value.ScaleMultiplier;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Load tag placement presets: {ex.Message}"); }

            foreach (Element elem in elements)
            {
                XYZ center = GetElementCenter(elem, view);
                if (center.IsAlmostEqualTo(XYZ.Zero))
                {
                    sb.NoCenter++; skipped++;
                    StingLog.Info($"SmartPlace skip (no center): element {elem.Id} category='{elem.Category?.Name}'");
                    continue;
                }

                ElementId catId = elem.Category.Id;
                if (!tagTypeCache.TryGetValue(catId, out ElementId tagTypeId))
                {
                    FamilySymbol tagType = FindTagType(doc, elem.Category);
                    tagTypeId = tagType?.Id ?? ElementId.InvalidElementId;
                    tagTypeCache[catId] = tagTypeId;
                }
                if (tagTypeId == ElementId.InvalidElementId)
                {
                    sb.NoTagFamily++; skipped++;
                    StingLog.Info($"SmartPlace skip (no tag family loaded): element {elem.Id} category='{elem.Category?.Name}'");
                    continue;
                }

                // Task 5: resolve the Tag Studio size/style/colour/arrowhead/depth combo
                // to a specific type variant BEFORE IndependentTag.Create. Falls back to
                // the base type when the variant does not exist in the family (run
                // Migrate Tag Families to create it).
                try
                {
                    var baseType = doc.GetElement(tagTypeId) as FamilySymbol;
                    if (baseType != null)
                    {
                        string disc = ParameterHelpers.GetString(elem, ParamRegistry.DISC);
                        ElementId variantId = TagStyleEngine.ResolveTagTypeForPlacement(doc, baseType, disc);
                        if (variantId != ElementId.InvalidElementId) tagTypeId = variantId;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"ResolveTagTypeForPlacement: {ex.Message}"); }

                string catName = elem.Category?.Name ?? "";
                // Apply per-category scale multiplier to offset
                double catOffset = offset;
                if (scaleMultipliers.TryGetValue(catName, out double scaleMult))
                    catOffset = offset * scaleMult;

                // Use smart preferred side that considers element orientation
                int preferred = GetSmartPreferredSide(elem, view);
                var offsets = GetCandidateOffsets(catOffset);

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

                // Leader length clamping: enforce min/max distance from element center
                double leaderMinFt = 3.0;
                double leaderMaxFt = 40.0;
                double distToBest = bestPos.DistanceTo(center);
                if (distToBest > 0.001)
                {
                    XYZ direction = (bestPos - center).Normalize();
                    if (distToBest < leaderMinFt)
                    {
                        bestPos = center + direction * leaderMinFt;
                    }
                    else if (distToBest > leaderMaxFt)
                    {
                        bestPos = center + direction * leaderMaxFt;
                        needsLeader = true;
                    }
                }

                // Log TAG_SEG_MASK_TXT if set on element (mask application happens in TagConfig.BuildAndWriteTag)
                try
                {
                    string segMask = ParameterHelpers.GetString(elem, "TAG_SEG_MASK_TXT");
                    if (!string.IsNullOrEmpty(segMask))
                        StingLog.Info($"PlaceTagsInView: element {elem.Id} has TAG_SEG_MASK_TXT={segMask}");
                }
                catch (Exception ex) { StingLog.Warn($"Read TAG_SEG_MASK_TXT for element {elem.Id}: {ex.Message}"); }

                var finalBox = Box2D.EstimateTag(bestPos, tagWidth, tagHeight);
                if (grid.HasOverlap(finalBox)) collisions++;

                try
                {
                    bool useLeader = addLeaders || needsLeader;
                    var elemRef = new Reference(elem);
                    IndependentTag tag = IndependentTag.Create(
                        doc, tagTypeId, view.Id, elemRef, useLeader,
                        TagOrientation.Horizontal, bestPos);

                    if (tag != null)
                    {
                        // ENH-03: Adjust leader elbow to avoid overlapping placed tags
                        if (tag.HasLeader)
                            TagPlacementPresets.AdjustLeaderElbow(doc, view, tag, elemRef, grid);

                        BoundingBoxXYZ tagBB = tag.get_BoundingBox(view);
                        Box2D regBox = tagBB != null ? Box2D.FromBoundingBox(tagBB) : finalBox;
                        grid.Insert(regBox);
                        tagYs.Add(regBox.CenterY);
                        tagXs.Add(regBox.CenterX);
                        placed++;
                    }
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException iopEx)
                {
                    sb.TagCreationFailed++; skipped++;
                    StingLog.Info($"SmartPlace skip (tag creation failed): element {elem.Id} - {iopEx.Message}");
                }
                catch (Exception ex)
                {
                    sb.OtherException++; skipped++;
                    StingLog.Warn($"Tag placement failed for {elem.Id}: {ex.Message}");
                }
            }

            return (placed, skipped, collisions);
        }

        // ── Linked model visual tagging ─────────────────────────────────

        /// <summary>
        /// Place visual annotation tags on elements in linked Revit models.
        /// These are visual-only — no parameter data is written to linked elements.
        /// </summary>
        public static (int placed, int skipped, int collisions) PlaceTagsInLinkedViews(
            Document doc, View view, bool tagOnlyUntagged)
        {
            int placed = 0, skipped = 0, collisions = 0;

            var linkInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Where(li => li.GetLinkDocument() != null)
                .ToList();

            if (linkInstances.Count == 0) return (0, 0, 0);

            var existingTagRefs = new HashSet<string>();
            foreach (var existingTag in new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>())
            {
                try
                {
                    var refs = existingTag.GetTaggedReferences();
                    foreach (var r in refs)
                        existingTagRefs.Add(r.ConvertToStableRepresentation(doc));
                }
                catch (Exception ex) { StingLog.Warn($"Get tagged references for linked model check: {ex.Message}"); }
            }

            foreach (RevitLinkInstance linkInst in linkInstances)
            {
                try
                {
                    Document linkDoc = linkInst.GetLinkDocument();
                    if (linkDoc == null) continue;

                    var linkElements = new FilteredElementCollector(linkDoc)
                        .WhereElementIsNotElementType()
                        .Where(e => e.Category != null && TagConfig.DiscMap.ContainsKey(e.Category.Name ?? ""))
                        .ToList();

                    foreach (Element linkEl in linkElements)
                    {
                        try
                        {
                            Reference linkRef = new Reference(linkEl)
                                .CreateLinkReference(linkInst);
                            if (linkRef == null) { skipped++; continue; }

                            string refKey = linkRef.ConvertToStableRepresentation(doc);
                            if (existingTagRefs.Contains(refKey))
                            {
                                skipped++;
                                continue;
                            }

                            FamilySymbol tagType = FindTagType(doc, linkEl.Category);
                            if (tagType == null) { skipped++; continue; }

                            XYZ center = GetElementCenter(linkEl, view);
                            double offset = GetModelOffset(view);
                            int preferred = GetPreferredSide(linkEl.Category?.Name ?? "");
                            XYZ[] candidates = GetCandidateOffsets(offset);
                            XYZ tagPos = center + candidates[preferred < candidates.Length ? preferred : 0];

                            // Task 5: Pick the correct style/size/colour/arrow/depth variant
                            // BEFORE creating the tag so placement is atomic.
                            ElementId tagTypeIdLink = tagType.Id;
                            try
                            {
                                ElementId vId = TagStyleEngine.ResolveTagTypeForPlacement(doc, tagType, null);
                                if (vId != ElementId.InvalidElementId) tagTypeIdLink = vId;
                            }
                            catch (Exception ex) { StingLog.Warn($"ResolveTagTypeForPlacement (linked): {ex.Message}"); }

                            IndependentTag tag = IndependentTag.Create(
                                doc, tagTypeIdLink, view.Id, linkRef,
                                false, TagOrientation.Horizontal, tagPos);

                            if (tag != null) placed++;
                            else skipped++;
                        }
                        catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); skipped++; }
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"PlaceTagsInLinkedViews link: {ex.Message}");
                }
            }

            return (placed, skipped, collisions);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Tag Placement Preset — save/recall relative tag positions
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tag placement preset system (tag-from-template approach).
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
            /// <summary>Minimum leader length in feet. Tags closer than this are pushed out.</summary>
            public double LeaderLengthMin { get; set; } = 3.0;
            /// <summary>Maximum leader length in feet. Tags farther than this are pulled in.</summary>
            public double LeaderLengthMax { get; set; } = 40.0;
            /// <summary>Per-category scale multiplier applied to offset distance (1.0 = default).</summary>
            public double ScaleMultiplier { get; set; } = 1.0;
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
                catch (Exception ex) { StingLog.Warn($"Learn tag placement for tag {tag.Id}: {ex.Message}"); }
            }

            // Average the offsets per category
            foreach (var kvp in catOffsets)
            {
                if (kvp.Value.Count == 0) continue;
                double avgDx = kvp.Value.Average(o => o.dx);
                double avgDy = kvp.Value.Average(o => o.dy);
                bool useLeader = kvp.Value.Count(o => o.hasLeader) > kvp.Value.Count / 2;
                var orientGroups = kvp.Value.GroupBy(o => o.orient)
                    .OrderByDescending(g => g.Count()).ToList();
                string orient = orientGroups.Count > 0 ? orientGroups[0].Key : "Horizontal";

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
                catch (Exception ex) { StingLog.Warn($"Get tagged IDs for preset apply: {ex.Message}"); }
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

                // Task 5: resolve style/size/colour/arrow/depth variant before IndependentTag.Create
                try
                {
                    var baseType = doc.GetElement(tagTypeId) as FamilySymbol;
                    if (baseType != null)
                    {
                        string disc = ParameterHelpers.GetString(elem, ParamRegistry.DISC);
                        ElementId vId = TagStyleEngine.ResolveTagTypeForPlacement(doc, baseType, disc);
                        if (vId != ElementId.InvalidElementId) tagTypeId = vId;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"ResolveTagTypeForPlacement (apply preset): {ex.Message}"); }

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
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); skipped++; }
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

        /// <summary>
        /// Align nearby tags into horizontal bands by snapping them to the median Y position
        /// of their group.
        /// </summary>
        public static int AlignTagBands(Document doc, View view, double tagHeightEstimate)
        {
            var tags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .OrderBy(t => { try { return t.TagHeadPosition.Y; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0.0; } })
                .ToList();

            if (tags.Count < 2) return 0;

            var groups = new List<List<IndependentTag>>();
            var current = new List<IndependentTag> { tags[0] };
            for (int i = 1; i < tags.Count; i++)
            {
                double prevY, thisY;
                try { prevY = tags[i - 1].TagHeadPosition.Y; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); prevY = 0; }
                try { thisY = tags[i].TagHeadPosition.Y; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); thisY = 0; }

                if (Math.Abs(thisY - prevY) <= tagHeightEstimate * 1.2)
                    current.Add(tags[i]);
                else
                {
                    if (current.Count > 1) groups.Add(current);
                    current = new List<IndependentTag> { tags[i] };
                }
            }
            if (current.Count > 1) groups.Add(current);

            int moved = 0;
            foreach (var group in groups)
            {
                var ys = group.Select(t => { try { return t.TagHeadPosition.Y; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0.0; } })
                    .OrderBy(y => y).ToList();
                double medianY = ys[ys.Count / 2];
                foreach (var tag in group)
                {
                    try
                    {
                        XYZ pos = tag.TagHeadPosition;
                        if (Math.Abs(pos.Y - medianY) > 0.001)
                        {
                            tag.TagHeadPosition = new XYZ(pos.X, medianY, pos.Z);
                            moved++;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Align tag band position for tag {tag.Id}: {ex.Message}"); }
                }
            }
            return moved;
        }

        // ── ENH-03: Leader elbow path avoidance ─────────────────────────

        /// <summary>ENH-03: Adjust leader elbow to avoid overlapping other tags.</summary>
        internal static void AdjustLeaderElbow(Document doc, View view, IndependentTag tag,
            Reference tagRef, TagPlacementEngine.SpatialGrid grid, double clearanceMargin = 0.5)
        {
            try
            {
                if (!tag.HasLeader) return;

                XYZ headPos = tag.TagHeadPosition;
                XYZ leaderEnd;
                try { leaderEnd = tag.GetLeaderEnd(tagRef); }
                catch (Exception ex) { StingLog.Warn($"Get leader end point: {ex.Message}"); return; }
                if (headPos == null || leaderEnd == null) return;

                XYZ leaderVec = headPos - leaderEnd;
                double leaderLen = leaderVec.GetLength();
                if (leaderLen < 0.01) return;

                XYZ leaderDir = leaderVec.Normalize();
                // Perpendicular direction in XY plane
                XYZ perpDir = new XYZ(-leaderDir.Y, leaderDir.X, 0);

                XYZ midPoint = (headPos + leaderEnd) / 2.0;

                // Check if leader midpoint overlaps any placed tag boxes in the grid
                var midBox = TagPlacementEngine.Box2D.EstimateTag(midPoint, clearanceMargin, clearanceMargin);
                if (!grid.HasOverlap(midBox)) return;

                // Try shifting elbow perpendicular to leader direction
                double[] shifts = { clearanceMargin, -clearanceMargin,
                                    clearanceMargin * 2, -clearanceMargin * 2 };
                foreach (double shift in shifts)
                {
                    XYZ elbowCandidate = midPoint + perpDir * shift;
                    var elbowBox = TagPlacementEngine.Box2D.EstimateTag(elbowCandidate,
                        clearanceMargin * 0.5, clearanceMargin * 0.5);
                    if (!grid.HasOverlap(elbowBox))
                    {
                        try
                        {
                            tag.LeaderEndCondition = LeaderEndCondition.Free;
                            tag.SetLeaderElbow(tagRef, elbowCandidate);
                        }
                        catch (Exception ex) { StingLog.Warn($"Set leader elbow position: {ex.Message}"); }
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"AdjustLeaderElbow: {ex.Message}");
            }
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
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;
            View view = ctx.ActiveView;

            if (view is ViewSheet)
            {
                TaskDialog.Show("Smart Place Tags", "Cannot tag on a sheet view.\nOpen a floor plan or section.");
                return Result.Succeeded;
            }

            // Launch Smart Placement Wizard for interactive configuration
            var wizSettings = UI.SmartPlacementWizard.Show();
            if (wizSettings == null) return Result.Cancelled;

            bool tagUntaggedOnly = wizSettings.Scope == "Untagged";
            bool selectedOnly = wizSettings.Scope == "Selected";
            bool addLeaders = wizSettings.LeaderMode == "Always";

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

                    // Gap fix: Run data tagging pipeline on untagged elements before
                    // creating visual annotations, so tags display meaningful content.
                    var pipelineCtx = TokenAutoPopulator.PopulationContext.Build(doc);
                    var (tagIdx, seqCtrs) = TagConfig.BuildTagIndexAndCounters(doc);
                    foreach (ElementId pid in selectedIds)
                    {
                        Element pEl = doc.GetElement(pid);
                        if (pEl?.Category == null) continue;
                        string existingTag = ParameterHelpers.GetString(pEl, ParamRegistry.TAG1);
                        if (string.IsNullOrEmpty(existingTag))
                        {
                            try
                            {
                                TagPipelineHelper.RunFullPipeline(doc, pEl, pipelineCtx,
                                    tagIdx, seqCtrs, null, null,
                                    overwrite: false, skipComplete: true,
                                    collisionMode: TagCollisionMode.AutoIncrement);
                            }
                            catch (Exception pipeEx)
                            {
                                StingLog.Warn($"SmartPlace pipeline for {pEl.Id}: {pipeEx.Message}");
                            }
                        }
                    }

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

                        // Task 5: variant resolution before placement
                        try
                        {
                            var baseType = doc.GetElement(tagTypeId) as FamilySymbol;
                            if (baseType != null)
                            {
                                string disc = ParameterHelpers.GetString(elem, ParamRegistry.DISC);
                                ElementId vId = TagStyleEngine.ResolveTagTypeForPlacement(doc, baseType, disc);
                                if (vId != ElementId.InvalidElementId) tagTypeId = vId;
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"ResolveTagTypeForPlacement (smart sel): {ex.Message}"); }

                        XYZ center = TagPlacementEngine.GetElementCenter(elem, view);
                        // Pack 4 — anchor-aware candidates. Reads STING_TAG_ANCHOR_{X,Y}_MM
                        // off the host element's family type and shifts the whole 16-candidate
                        // ring. Elements with no anchor behave exactly as before.
                        var offsets = TagPlacementEngine.GetCandidateOffsetsWithAnchor(offset, elem);
                        string catName = elem.Category?.Name ?? "";
                        int preferred = TagPlacementEngine.GetPreferredSide(catName);

                        // UI-08: Override preferred position from dockable panel compass
                        string prefPosStr = UI.StingCommandHandler.GetExtraParam("PreferredTagPos");
                        if (int.TryParse(prefPosStr, out int prefPos) && prefPos >= 1 && prefPos <= offsets.Length)
                            preferred = prefPos - 1;

                        // UI-06: Override direction from DirOverride radio
                        string dirStr = UI.StingCommandHandler.GetExtraParam("DirOverride");
                        if (int.TryParse(dirStr, out int dirIdx) && dirIdx >= 0 && dirIdx < offsets.Length)
                            preferred = dirIdx;

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

                    // Gap fix: Pre-tag untagged elements before visual placement
                    if (tagUntaggedOnly)
                    {
                        var pipeCtx = TokenAutoPopulator.PopulationContext.Build(doc);
                        var (tIdx, sCtrs) = TagConfig.BuildTagIndexAndCounters(doc);
                        foreach (Element vEl in new FilteredElementCollector(doc, view.Id)
                            .WhereElementIsNotElementType())
                        {
                            if (vEl?.Category == null) continue;
                            string et = ParameterHelpers.GetString(vEl, ParamRegistry.TAG1);
                            if (string.IsNullOrEmpty(et))
                            {
                                try
                                {
                                    TagPipelineHelper.RunFullPipeline(doc, vEl, pipeCtx,
                                        tIdx, sCtrs, null, null,
                                        overwrite: false, skipComplete: true,
                                        collisionMode: TagCollisionMode.AutoIncrement);
                                }
                                catch (Exception pEx)
                                {
                                    StingLog.Warn($"SmartPlace pipeline (view) {vEl.Id}: {pEx.Message}");
                                }
                            }
                        }
                    }

                    (placed, skipped, collisions) =
                        TagPlacementEngine.PlaceTagsInView(doc, view, addLeaders, tagUntaggedOnly);
                    tx.Commit();
                }
            }

            // Persist SEQ counters and invalidate caches
            try { var (_, spSeq) = TagConfig.BuildTagIndexAndCounters(doc); TagConfig.SaveSeqSidecar(doc, spSeq); }
            catch (Exception ex) { StingLog.Warn($"SmartPlace SEQ sidecar: {ex.Message}"); }
            ComplianceScan.InvalidateCache();
            StingAutoTagger.InvalidateContext();

            sw.Stop();
            var report = new StringBuilder();
            report.AppendLine($"Placed: {placed} annotation tags");
            if (skipped > 0)
            {
                var bd = TagPlacementEngine.LastSkipBreakdown;
                report.AppendLine($"Skipped: {bd.Format()}");
                if (bd.NoTagFamily > 0)
                    report.AppendLine("  Hint: load STING tag families for the affected categories (Tags → Load Tag Families).");
            }
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
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;
            View view = ctx.ActiveView;

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
                    catch (Exception ex) { StingLog.Warn($"Get host center for arrange: {ex.Message}"); }

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
                    catch (Exception ex) { StingLog.Warn($"Get host element for arrange: {ex.Message}"); }

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
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;
            View view = ctx.ActiveView;

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

            StingLog.Info($"RemoveAnnotationTags: deleting {tagsToRemove.Count} tags ({scope})");

            // Collect IDs upfront — batch deletion is safer and faster than one-by-one
            var idsToDelete = new List<ElementId>(tagsToRemove.Count);
            foreach (var tag in tagsToRemove)
                idsToDelete.Add(tag.Id);

            // Clear selection BEFORE deleting — if deleted tags remain in the
            // active selection, Revit's deferred graphics update can crash
            // accessing disposed element references.
            try { uidoc.Selection.SetElementIds(new List<ElementId>()); }
            catch (Exception ex) { StingLog.Warn($"best effort: {ex.Message}"); }

            int removed = 0;
            using (Transaction tx = new Transaction(doc, "STING Remove Annotation Tags"))
            {
                tx.Start();
                try
                {
                    // Single batch delete — avoids per-element internal bookkeeping
                    // that can trigger cascading graphics cache invalidation
                    doc.Delete(idsToDelete);
                    removed = idsToDelete.Count;
                }
                catch (Exception ex)
                {
                    StingLog.Error($"RemoveAnnotationTags: batch delete failed, falling back to one-by-one", ex);
                    // Fallback: delete individually
                    foreach (var id in idsToDelete)
                    {
                        try { doc.Delete(id); removed++; }
                        catch (Exception ex2) { StingLog.Warn($"Could not delete tag {id}: {ex2.Message}"); }
                    }
                }

                // doc.Regenerate() REMOVED — causes native Revit crashes (see StingCommandHandler.cs:759)
                tx.Commit();
            }

            StingLog.Info($"RemoveAnnotationTags: deleted {removed} of {idsToDelete.Count} tags");
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
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

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

            // CRASH FIX: Single transaction for all views instead of one per view.
            // Rapid-fire tx.Commit() calls trigger Revit's deferred regeneration
            // which causes native segfaults (same root cause as ENH-003).
            using (Transaction tx = new Transaction(doc, "STING Batch Place Tags"))
            {
                tx.Start();
                foreach (View v in targetViews)
                {
                    try
                    {
                        var (p, s, c) = TagPlacementEngine.PlaceTagsInView(
                            doc, v, addLeaders: false, tagOnlyUntagged: true);
                        totalPlaced += p; totalSkipped += s; totalCollisions += c;
                        perView.Add((v.Name, p, s));
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"BatchPlaceTags: view '{v.Name}' failed: {ex.Message}");
                        perView.Add((v.Name, 0, 0));
                    }
                    viewsProcessed++;
                    if (viewsProcessed % 10 == 0)
                        StingLog.Info($"BatchPlaceTags: {viewsProcessed}/{targetViews.Count} views done");
                }
                tx.Commit();
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
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            if (ctx.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            Document doc = ctx.Doc;
            View view = ctx.ActiveView;

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
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            if (ctx.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            Document doc = ctx.Doc;
            View view = ctx.ActiveView;

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
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;
            View view = ctx.ActiveView;

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
                        catch (Exception ex) { StingLog.Warn($"Get overlap tag category i: {ex.Message}"); }
                        try
                        {
                            var hj = boxes[j].tag.GetTaggedLocalElementIds();
                            if (hj.Count > 0) catJ = doc.GetElement(hj.First())?.Category?.Name ?? "";
                        }
                        catch (Exception ex) { StingLog.Warn($"Get overlap tag category j: {ex.Message}"); }

                        string key = string.Compare(catI, catJ, StringComparison.Ordinal) <= 0
                            ? $"{catI} / {catJ}" : $"{catJ} / {catI}";
                        catPairs.TryGetValue(key, out int cp);
                        catPairs[key] = cp + 1;
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
            catch (Exception ex) { StingLog.Warn($"Calculate view crop area: {ex.Message}"); }

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
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;

            // Find tag families to modify
            var tagFamilies = new FilteredElementCollector(doc)
                .OfClass(typeof(Family)).Cast<Family>()
                .Where(f =>
                {
                    try { return f.FamilyCategory?.CategoryType == CategoryType.Annotation; }
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
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
                    using (Transaction ft = new Transaction(famDoc, "STING Change Text Size"))
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
                            catch (Exception ex) { StingLog.Warn($"Set text size on TextType: {ex.Message}"); }
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
                            catch (Exception ex) { StingLog.Warn($"Set text size on AnnotationType: {ex.Message}"); }
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
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

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
                    catch (Exception ex) { StingLog.Warn($"Set line weight on category {bic}: {ex.Message}"); }
                }
                tx.Commit();
            }

            TaskDialog.Show("Set Tag Category Line Weight",
                $"Set line weight to pen {pen} on {changed} tag categories.\n" +
                "This affects leader lines and tag borders project-wide.");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Align Tag Bands — snap nearby tags to horizontal bands
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Align tags in the active view into horizontal bands.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignTagBandsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            View view = ctx.ActiveView;
            if (view == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }

            double tagH = TagPlacementEngine.GetModelOffset(view);
            int moved = 0;
            using (Transaction tx = new Transaction(doc, "STING Align Tag Bands"))
            {
                tx.Start();
                moved = TagPlacementPresets.AlignTagBands(doc, view, tagH);
                tx.Commit();
            }
            TaskDialog.Show("STING \u2014 Align Tag Bands",
                $"Aligned {moved} tags into horizontal bands in '{view.Name}'.");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Switch Tag Position — set STING_TAG_POS on family types
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Switch STING_TAG_POS integer on family types to control tag position.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SwitchTagPositionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var td = new TaskDialog("STING \u2014 Tag Position");
            td.MainInstruction = "Select tag position";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "1 \u2014 Above");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "2 \u2014 Right");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "3 \u2014 Below");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "4 \u2014 Left");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;
            var result = td.Show();

            int posValue;
            switch (result)
            {
                case TaskDialogResult.CommandLink1: posValue = 1; break;
                case TaskDialogResult.CommandLink2: posValue = 2; break;
                case TaskDialogResult.CommandLink3: posValue = 3; break;
                case TaskDialogResult.CommandLink4: posValue = 4; break;
                default: return Result.Cancelled;
            }

            // Get scope elements
            UIDocument uidoc = ctx.UIDoc;
            View view = ctx.ActiveView;
            var selectedIds = uidoc.Selection.GetElementIds();
            IList<Element> scope;
            if (selectedIds.Count > 0)
            {
                scope = selectedIds.Select(id => doc.GetElement(id)).Where(e => e != null).ToList();
            }
            else if (view != null)
            {
                scope = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null && TagConfig.DiscMap.ContainsKey(e.Category.Name ?? ""))
                    .ToList();
            }
            else
            {
                TaskDialog.Show("STING", "No active view or selection.");
                return Result.Failed;
            }

            var typeIds = new HashSet<ElementId>(
                scope.Select(e => { try { return e.GetTypeId(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ElementId.InvalidElementId; } })
                    .Where(id => id != ElementId.InvalidElementId));

            int updated = 0;
            using (Transaction tx = new Transaction(doc, "STING Switch Tag Position"))
            {
                tx.Start();
                foreach (ElementId typeId in typeIds)
                {
                    try
                    {
                        Element typeEl = doc.GetElement(typeId);
                        Parameter p = typeEl?.LookupParameter(ParamRegistry.TAG_POS);
                        if (p != null && !p.IsReadOnly) { p.Set(posValue); updated++; }

                        // Gap 2 — dual-write to ES so post-migration reads see the
                        // same value. ES write is best-effort; a failure here does
                        // not abort the shared-parameter update above.
                        try
                        {
                            if (typeEl != null)
                            {
                                var existing = StingTools.Core.Storage.StingPositionSchema.Read(typeEl);
                                StingTools.Core.Storage.StingPositionSchema.Write(typeEl,
                                    new StingTools.Core.Storage.StingPositionSchema.PositionData
                                    {
                                        TagPos        = posValue,
                                        TokenPresence = existing?.TokenPresence ?? 0,
                                    });
                            }
                        }
                        catch (Exception esEx) { StingLog.Warn($"ES dual-write TAG_POS on {typeId}: {esEx.Message}"); }
                    }
                    catch (Exception ex) { StingLog.Warn($"Set TAG_POS on type {typeId}: {ex.Message}"); }
                }
                tx.Commit();
            }

            TaskDialog.Show("STING \u2014 Tag Position",
                $"Set position {posValue} on {updated} element types.");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Export Tag Positions — CSV export for analysis
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Export tag positions to CSV for analysis and documentation.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportTagPositionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            View view = ctx.ActiveView;
            if (view == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }

            var tags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            if (tags.Count == 0)
            {
                TaskDialog.Show("STING", "No tags found in active view.");
                return Result.Succeeded;
            }

            var csv = new StringBuilder();
            csv.AppendLine("ViewName,ViewType,ViewScale,ElementId,Category,TagFamilyName," +
                "TagTypeName,TagPosX_mm,TagPosY_mm,OffsetX_mm,OffsetY_mm,HasLeader,STING_TAG");

            double mmPerFt = 304.8;
            foreach (var tag in tags)
            {
                try
                {
                    var refs = tag.GetTaggedReferences();
                    if (refs == null || refs.Count == 0) continue;
                    Element host = doc.GetElement(refs[0]);
                    if (host == null) continue;

                    XYZ tagPos;
                    try { tagPos = tag.TagHeadPosition; }
                    catch (Exception ex) { StingLog.Warn($"Get tag head position: {ex.Message}"); continue; }

                    XYZ hostCenter = TagPlacementEngine.GetElementCenter(host, view);
                    double offsetX = (tagPos.X - hostCenter.X) * mmPerFt;
                    double offsetY = (tagPos.Y - hostCenter.Y) * mmPerFt;

                    string stingTag = ParameterHelpers.GetString(host, ParamRegistry.TAG1);
                    bool hasLeader = false;
                    try { hasLeader = tag.HasLeader; } catch (Exception ex) { StingLog.Warn($"Check tag leader: {ex.Message}"); }

                    csv.AppendLine($"\"{view.Name}\",{view.ViewType},{view.Scale}," +
                        $"{host.Id.Value},\"{host.Category?.Name ?? ""}\",\"{tag.TagText}\"," +
                        $"\"\",{tagPos.X * mmPerFt:F1},{tagPos.Y * mmPerFt:F1}," +
                        $"{offsetX:F1},{offsetY:F1},{hasLeader},\"{stingTag}\"");
                }
                catch (Exception ex) { StingLog.Warn($"Export tag position for tag {tag.Id}: {ex.Message}"); }
            }

            string dir = !string.IsNullOrEmpty(doc.PathName)
                ? System.IO.Path.GetDirectoryName(doc.PathName)
                : StingToolsApp.DataPath ?? "";
            string csvPath = System.IO.Path.Combine(dir ?? "",
                $"STING_TagPositions_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            try
            {
                System.IO.File.WriteAllText(csvPath, csv.ToString());
                TaskDialog.Show("STING \u2014 Export Tag Positions",
                    $"Exported {tags.Count} tag positions to:\n{csvPath}");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("STING", $"Export failed: {ex.Message}");
            }

            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Linked Model Visual Tagging — visual-only tags on linked elements
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Place visual annotation tags on elements in linked Revit models.
    /// Visual-only — no parameter data is written to linked elements.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchPlaceLinkedTagsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            View view = ctx.ActiveView;
            if (view == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }

            var td = new TaskDialog("STING — Linked Model Tags");
            td.MainInstruction = "Place visual tags on linked model elements";
            td.MainContent = "WARNING: Linked elements are read-only.\n" +
                "Visual tags only — no parameter data will be written.\n\n" +
                "This places annotation tags on elements visible in linked Revit models.";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Tag linked elements in active view");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;
            if (td.Show() != TaskDialogResult.CommandLink1) return Result.Cancelled;

            int placed, skipped, collisions;
            using (Transaction tx = new Transaction(doc, "STING Place Linked Tags"))
            {
                tx.Start();
                (placed, skipped, collisions) = TagPlacementEngine.PlaceTagsInLinkedViews(doc, view, true);
                tx.Commit();
            }

            TaskDialog.Show("STING — Linked Model Tags",
                $"Placed {placed} visual tags on linked elements.\n" +
                $"Skipped: {skipped}, Collisions: {collisions}");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Linked Model Sidecar Export — token manifest for linked models
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Exports a JSON sidecar manifest for each linked Revit model, containing
    /// derived ISO 19650 tokens for all taggable elements. This enables tag
    /// coordination across linked models without writing to read-only documents.
    /// The manifest is written alongside the host document as
    /// {LinkedModelTitle}_LINKED_TOKENS.json.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportLinkedModelManifestCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            string hostDir = Path.GetDirectoryName(doc.PathName);
            if (string.IsNullOrEmpty(hostDir))
            {
                TaskDialog.Show("STING", "Save the host document first to establish a file path.");
                return Result.Failed;
            }

            // Collect all RevitLinkInstance elements
            var linkInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            if (linkInstances.Count == 0)
            {
                TaskDialog.Show("STING", "No linked Revit models found in this project.");
                return Result.Succeeded;
            }

            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            int totalFiles = 0;
            int totalElements = 0;
            var summaryLines = new List<string>();

            foreach (var linkInst in linkInstances)
            {
                Document linkedDoc = linkInst.GetLinkDocument();
                if (linkedDoc == null)
                {
                    summaryLines.Add($"  {linkInst.Name}: not loaded (skipped)");
                    continue;
                }

                string linkedTitle = linkedDoc.Title ?? "UnknownLinked";
                // Sanitize filename
                foreach (char c in Path.GetInvalidFileNameChars())
                    linkedTitle = linkedTitle.Replace(c, '_');

                var popCtx = TokenAutoPopulator.PopulationContext.Build(linkedDoc);
                var entries = new List<Dictionary<string, string>>();

                foreach (Element el in new FilteredElementCollector(linkedDoc)
                    .WhereElementIsNotElementType())
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (!known.Contains(cat)) continue;

                    // Derive tokens without writing (read-only)
                    string disc = TagConfig.DiscMap.TryGetValue(cat, out string d) ? d : "A";
                    string loc = SpatialAutoDetect.DetectLoc(linkedDoc, el, popCtx.RoomIndex, popCtx.ProjectLoc);
                    if (string.IsNullOrEmpty(loc)) loc = "XX";
                    string zone = SpatialAutoDetect.DetectZone(linkedDoc, el, popCtx.RoomIndex);
                    if (string.IsNullOrEmpty(zone)) zone = "XX";
                    string lvl = ParameterHelpers.GetLevelCode(linkedDoc, el);
                    string sys = TagConfig.GetMepSystemAwareSysCode(el, cat);
                    if (string.IsNullOrEmpty(sys)) sys = TagConfig.GetDiscDefaultSysCode(disc);
                    string func = TagConfig.GetSmartFuncCode(el, sys);
                    if (string.IsNullOrEmpty(func))
                        func = TagConfig.FuncMap.TryGetValue(sys, out string fv) ? fv : "GEN";
                    string prod = TagConfig.GetFamilyAwareProdCode(el, cat);
                    string status = PhaseAutoDetect.DetectStatus(linkedDoc, el);
                    if (string.IsNullOrEmpty(status)) status = "NEW";
                    string rev = !string.IsNullOrEmpty(popCtx.ProjectRev) ? popCtx.ProjectRev : "P01";
                    string derivedTag = string.Join(ParamRegistry.Separator,
                        disc, loc, zone, lvl, sys, func, prod, "0000");

                    entries.Add(new Dictionary<string, string>
                    {
                        ["UniqueId"] = el.UniqueId,
                        ["Category"] = cat,
                        ["Discipline"] = disc,
                        ["Loc"] = loc,
                        ["Zone"] = zone,
                        ["Lvl"] = lvl,
                        ["Sys"] = sys,
                        ["Func"] = func,
                        ["Prod"] = prod,
                        ["Status"] = status,
                        ["Rev"] = rev,
                        ["DerivedTag"] = derivedTag,
                    });
                }

                if (entries.Count == 0)
                {
                    summaryLines.Add($"  {linkedDoc.Title}: 0 taggable elements (skipped)");
                    continue;
                }

                // Write JSON sidecar
                string jsonPath = Path.Combine(hostDir, $"{linkedTitle}_LINKED_TOKENS.json");
                try
                {
                    string json = JsonConvert.SerializeObject(entries, Formatting.Indented);
                    File.WriteAllText(jsonPath, json);
                    totalFiles++;
                    totalElements += entries.Count;
                    summaryLines.Add($"  {linkedDoc.Title}: {entries.Count} elements -> {Path.GetFileName(jsonPath)}");
                    StingLog.Info($"ExportLinkedManifest: wrote {entries.Count} entries to {jsonPath}");
                }
                catch (Exception ex)
                {
                    summaryLines.Add($"  {linkedDoc.Title}: FAILED — {ex.Message}");
                    StingLog.Error($"ExportLinkedManifest: failed for {linkedDoc.Title}", ex);
                }
            }

            var report = new StringBuilder();
            report.AppendLine("Linked Model Token Manifest Export");
            report.AppendLine(new string('=', 50));
            report.AppendLine($"  Links found:    {linkInstances.Count}");
            report.AppendLine($"  Files written:  {totalFiles}");
            report.AppendLine($"  Total elements: {totalElements}");
            report.AppendLine();
            foreach (string line in summaryLines)
                report.AppendLine(line);

            TaskDialog td = new TaskDialog("STING — Linked Model Manifest");
            td.MainInstruction = $"Exported {totalElements} element tokens from {totalFiles} linked models";
            td.MainContent = report.ToString();
            td.Show();

            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Adjust Elbows — reposition leader elbows on annotation tags
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Adjust leader elbow positions on IndependentTag annotations in the active view.
    /// Supports Straight (midpoint), 90-degree (horizontal-first), 45-degree (angled),
    /// and Free (user-specified delta) elbow styles.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AdjustElbowsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.ActiveView == null)
            {
                TaskDialog.Show("STING", "No active view.");
                return Result.Failed;
            }
            Document doc = ctx.Doc;
            View view = ctx.ActiveView;

            // FIX-4.3: Read ElbowMode from ExtraParams (set by Tag Studio sliders)
            int mode = -1;
            string epMode = UI.StingCommandHandler.GetExtraParam("ElbowMode");
            if (!string.IsNullOrEmpty(epMode) && int.TryParse(epMode, out int parsed) && parsed >= 0 && parsed <= 3)
            {
                mode = parsed;
            }

            if (mode < 0)
            {
                // Fallback: show dialog if not set by Tag Studio
                TaskDialog dlg = new TaskDialog("STING — Adjust Elbows");
                dlg.MainInstruction = "Select leader elbow style";
                dlg.MainContent = "Adjusts elbows on all tags with leaders in the active view.";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Straight", "Elbow at midpoint of element-to-tag vector");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "90 degrees", "Horizontal-first: elbow at (tagX, elemY)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    "45 degrees", "Angled: elbow offset to create 45-degree bend");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                    "Free", "No elbow adjustment — set elbow at leader end");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                switch (dlg.Show())
                {
                    case TaskDialogResult.CommandLink1: mode = 0; break;
                    case TaskDialogResult.CommandLink2: mode = 1; break;
                    case TaskDialogResult.CommandLink3: mode = 2; break;
                    case TaskDialogResult.CommandLink4: mode = 3; break;
                    default: return Result.Cancelled;
                }
            }

            var tags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            int adjusted = 0, skipped = 0;

            using (Transaction tx = new Transaction(doc, "STING Adjust Elbows"))
            {
                tx.Start();
                foreach (IndependentTag tag in tags)
                {
                    try
                    {
                        if (!tag.HasLeader) { skipped++; continue; }

                        var hostIds = tag.GetTaggedLocalElementIds();
                        if (hostIds.Count == 0) { skipped++; continue; }

                        Element host = doc.GetElement(hostIds.First());
                        if (host == null) { skipped++; continue; }

                        XYZ elemCenter = TagPlacementEngine.GetElementCenter(host, view);
                        XYZ tagPos = tag.TagHeadPosition;

                        Reference hostRef = new Reference(host);
                        XYZ elbowPos;

                        switch (mode)
                        {
                            case 0: // Straight: midpoint
                                elbowPos = (elemCenter + tagPos) / 2.0;
                                break;
                            case 1: // 90 degrees: horizontal-first
                                elbowPos = new XYZ(tagPos.X, elemCenter.Y, elemCenter.Z);
                                break;
                            case 2: // 45 degrees: midpoint + perpendicular offset
                                XYZ mid = (elemCenter + tagPos) / 2.0;
                                XYZ dir = tagPos - elemCenter;
                                double halfDist = dir.GetLength() / 2.0;
                                // Perpendicular in XY plane
                                XYZ perp = new XYZ(-dir.Y, dir.X, 0);
                                double perpLen = perp.GetLength();
                                if (perpLen > 0.001)
                                    elbowPos = mid + perp * (halfDist * 0.5 / perpLen);
                                else
                                    elbowPos = mid;
                                break;
                            case 3: // Free: set elbow at leader end (minimal adjustment)
                            default:
                                try
                                {
                                    elbowPos = tag.GetLeaderEnd(hostRef);
                                }
                                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); elbowPos = elemCenter; }
                                break;
                        }

                        tag.SetLeaderElbow(hostRef, elbowPos);
                        adjusted++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"AdjustElbows: tag {tag.Id}: {ex.Message}");
                        skipped++;
                    }
                }
                tx.Commit();
            }

            string modeName = mode switch
            {
                0 => "Straight",
                1 => "90 degrees",
                2 => "45 degrees",
                3 => "Free",
                _ => "Unknown"
            };

            TaskDialog.Show("STING — Adjust Elbows",
                $"Adjusted {adjusted} leader elbows to '{modeName}' style.\nSkipped: {skipped}");
            StingLog.Info($"AdjustElbows: adjusted={adjusted}, skipped={skipped}, mode={modeName}");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Set Arrowhead Style — annotation category line weight control
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Set arrowhead style on selected tags by overriding the instance
    /// LEADER_ARROWHEAD parameter (Task 4). When nothing is selected, falls
    /// back to the legacy pen/line-weight control on annotation categories.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetArrowheadStyleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            UIDocument uidoc = ctx.UIDoc;

            // ── Selection-first path: instance-level arrowhead override on tags ──
            var selIds = uidoc?.Selection?.GetElementIds();
            var tagIds = new List<ElementId>();
            if (selIds != null)
            {
                foreach (var id in selIds)
                {
                    if (doc.GetElement(id) is IndependentTag) tagIds.Add(id);
                }
            }

            string arrowName = UI.StingCommandHandler.GetExtraParam("ArrowStyle");
            if (tagIds.Count > 0 && !string.IsNullOrEmpty(arrowName) && tagIds.Count <= 200)
            {
                int updated;
                using (var tx = new Transaction(doc, "STING Override Tag Arrowhead"))
                {
                    tx.Start();
                    updated = TagStyleEngine.OverrideArrowheadOnSelection(doc, tagIds, arrowName);
                    tx.Commit();
                }
                TaskDialog.Show("STING — Set Arrowhead Style",
                    $"Overrode arrowhead on {updated}/{tagIds.Count} selected tags.\n" +
                    "Other tags in the view are unaffected.");
                return Result.Succeeded;
            }

            StingLog.Warn("SetArrowheadStyle: The Revit API does not expose IndependentTag.ArrowheadType " +
                "directly. Providing line weight control on annotation categories as alternative.");

            TaskDialog dlg = new TaskDialog("STING — Set Arrowhead Style");
            dlg.MainInstruction = "Annotation leader line weight";
            dlg.MainContent = "NOTE: The Revit API does not directly expose arrowhead type " +
                "on IndependentTag annotations. This command controls leader line weight " +
                "on annotation categories as the closest available control.\n\n" +
                "To change arrowhead style, edit the tag family .rfa directly.";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Hairline (pen 1)", "Thinnest leader lines");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Medium (pen 3)", "Standard leader weight");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Bold (pen 6)", "Heavy leader lines for emphasis");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            int pen;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: pen = 1; break;
                case TaskDialogResult.CommandLink2: pen = 3; break;
                case TaskDialogResult.CommandLink3: pen = 6; break;
                default: return Result.Cancelled;
            }

            int changed = 0;
            using (Transaction tx = new Transaction(doc, "STING Set Arrowhead Style"))
            {
                tx.Start();
                foreach (Category cat in doc.Settings.Categories)
                {
                    try
                    {
                        if (cat.CategoryType != CategoryType.Annotation) continue;
                        string name = cat.Name?.ToUpperInvariant() ?? "";
                        if (!name.Contains("TAG")) continue;

                        cat.SetLineWeight(pen, GraphicsStyleType.Projection);
                        changed++;
                    }
                    catch (Exception ex) { StingLog.Warn($"Set arrowhead line weight on category: {ex.Message}"); }
                }
                tx.Commit();
            }

            TaskDialog.Show("STING — Set Arrowhead Style",
                $"Set leader line weight to pen {pen} on {changed} tag annotation categories.");
            StingLog.Info($"SetArrowheadStyle: set pen {pen} on {changed} annotation categories");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  TagControlSession — aggregate all panel settings for batch apply
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Aggregates all tag control panel settings into a single session object.
    /// Allows batch application of position, leader, elbow, text, color, and
    /// segment mask settings in a single transaction.
    /// </summary>
    internal class TagControlSession
    {
        // Position settings (1-16 for 16 candidate positions)
        public int Position { get; set; } = 1;
        public double OffsetN { get; set; } = 1.0;
        public double OffsetE { get; set; } = 1.0;
        public double OffsetS { get; set; } = 1.0;
        public double OffsetW { get; set; } = 1.0;
        public double Ring2Scale { get; set; } = 1.5;

        // Leader settings
        public enum LeaderModeEnum { Auto, Always, Never, Smart }
        public LeaderModeEnum LeaderMode { get; set; } = LeaderModeEnum.Auto;
        public double LeaderLengthMin { get; set; } = 3.0;
        public double LeaderLengthMax { get; set; } = 40.0;

        // Elbow settings
        public enum ElbowTypeEnum { Straight, FortyFive, Ninety, Free }
        public ElbowTypeEnum ElbowType { get; set; } = ElbowTypeEnum.Straight;
        public double ElbowX { get; set; } = 0.0;
        public double ElbowY { get; set; } = 0.0;

        // Text settings
        public double TextSize { get; set; } = 2.5;
        public string TextWeight { get; set; } = "NOM";
        public string TextColor { get; set; } = "BLACK";
        public string BoxStyle { get; set; } = "NONE";
        public double BoxOpacity { get; set; } = 1.0;

        // Color and style
        public string ColorSchemeName { get; set; } = "";
        public int ParaDepth { get; set; } = 3;

        // Segment mask (8 segments: DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ)
        public bool[] TagSegmentMask { get; set; } = new bool[] { true, true, true, true, true, true, true, true };

        // Scope
        public string Scope { get; set; } = "ActiveView";
        public string CategoryFilter { get; set; } = "";

        /// <summary>
        /// Apply all session settings to tags in the specified view.
        /// Uses a single transaction for atomicity.
        /// </summary>
        public int Apply(Document doc, View view)
        {
            if (doc == null || view == null) return 0;
            int applied = 0;

            using (Transaction tx = new Transaction(doc, "STING Apply Tag Control Session"))
            {
                tx.Start();

                var tags = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag))
                    .Cast<IndependentTag>()
                    .ToList();

                // Build directional offset config from session
                var dirConfig = new TagPlacementEngine.DirectionalOffsetConfig
                {
                    NOffset = OffsetN,
                    EOffset = OffsetE,
                    SOffset = OffsetS,
                    WOffset = OffsetW,
                    Ring2Scale = Ring2Scale,
                };

                double baseOffset = TagPlacementEngine.GetModelOffset(view);

                foreach (IndependentTag tag in tags)
                {
                    try
                    {
                        // Apply category filter if specified
                        if (!string.IsNullOrEmpty(CategoryFilter))
                        {
                            var hostIds = tag.GetTaggedLocalElementIds();
                            if (hostIds.Count > 0)
                            {
                                Element host = doc.GetElement(hostIds.First());
                                string catName = host?.Category?.Name ?? "";
                                if (!catName.Equals(CategoryFilter, StringComparison.OrdinalIgnoreCase))
                                    continue;
                            }
                        }

                        // Apply leader mode
                        switch (LeaderMode)
                        {
                            case LeaderModeEnum.Always:
                                if (!tag.HasLeader) tag.HasLeader = true;
                                break;
                            case LeaderModeEnum.Never:
                                if (tag.HasLeader) tag.HasLeader = false;
                                break;
                            case LeaderModeEnum.Smart:
                            case LeaderModeEnum.Auto:
                                // Auto/Smart: leave as-is
                                break;
                        }

                        // Apply elbow type if tag has leader
                        if (tag.HasLeader)
                        {
                            try
                            {
                                var hostIds = tag.GetTaggedLocalElementIds();
                                if (hostIds.Count > 0)
                                {
                                    Element host = doc.GetElement(hostIds.First());
                                    if (host != null)
                                    {
                                        XYZ elemCenter = TagPlacementEngine.GetElementCenter(host, view);
                                        XYZ tagPos = tag.TagHeadPosition;
                                        Reference hostRef = new Reference(host);
                                        XYZ elbowPos;

                                        switch (ElbowType)
                                        {
                                            case ElbowTypeEnum.Straight:
                                                elbowPos = (elemCenter + tagPos) / 2.0;
                                                break;
                                            case ElbowTypeEnum.Ninety:
                                                elbowPos = new XYZ(tagPos.X, elemCenter.Y, elemCenter.Z);
                                                break;
                                            case ElbowTypeEnum.FortyFive:
                                                XYZ mid = (elemCenter + tagPos) / 2.0;
                                                XYZ dir = tagPos - elemCenter;
                                                double halfDist = dir.GetLength() / 2.0;
                                                XYZ perp = new XYZ(-dir.Y, dir.X, 0);
                                                double perpLen = perp.GetLength();
                                                elbowPos = perpLen > 0.001
                                                    ? mid + perp * (halfDist * 0.5 / perpLen)
                                                    : mid;
                                                break;
                                            case ElbowTypeEnum.Free:
                                            default:
                                                elbowPos = tagPos + new XYZ(ElbowX, ElbowY, 0);
                                                break;
                                        }

                                        tag.SetLeaderElbow(hostRef, elbowPos);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"TagControlSession elbow: {tag.Id}: {ex.Message}");
                            }
                        }

                        // Write segment mask as TAG_SEG_MASK_TXT to host element
                        if (TagSegmentMask != null && TagSegmentMask.Length == 8)
                        {
                            try
                            {
                                var hostIds = tag.GetTaggedLocalElementIds();
                                if (hostIds.Count > 0)
                                {
                                    Element host = doc.GetElement(hostIds.First());
                                    if (host != null)
                                    {
                                        string maskStr = string.Join("",
                                            TagSegmentMask.Select(b => b ? "1" : "0"));
                                        // Only write if not all segments enabled (default)
                                        if (maskStr != "11111111")
                                        {
                                            ParameterHelpers.SetString(host, "TAG_SEG_MASK_TXT",
                                                maskStr, overwrite: true);
                                        }
                                    }
                                }
                            }
                            catch (Exception ex) { StingLog.Warn($"Write TAG_SEG_MASK_TXT: {ex.Message}"); }
                        }

                        // Write TAG style BOOL params if text settings differ from default
                        if (!string.IsNullOrEmpty(TextWeight) && !string.IsNullOrEmpty(TextColor))
                        {
                            try
                            {
                                var hostIds = tag.GetTaggedLocalElementIds();
                                if (hostIds.Count > 0)
                                {
                                    Element host = doc.GetElement(hostIds.First());
                                    if (host != null)
                                    {
                                        // Build the TAG style param name: TAG_{SIZE}{STYLE}_{COLOR}_BOOL
                                        string sizeKey = TextSize.ToString("F1").Replace(".", "").TrimStart('0');
                                        if (string.IsNullOrEmpty(sizeKey)) sizeKey = "25";
                                        string paramName = $"TAG_{sizeKey}{TextWeight}_{TextColor}_BOOL";
                                        Parameter p = host.LookupParameter(paramName);
                                        if (p != null && p.StorageType == StorageType.Integer && !p.IsReadOnly)
                                            p.Set(1);
                                    }
                                }
                            }
                            catch (Exception ex) { StingLog.Warn($"Write tag style BOOL param: {ex.Message}"); }
                        }

                        // Write paragraph depth
                        if (ParaDepth >= 1 && ParaDepth <= 10)
                        {
                            try
                            {
                                var hostIds = tag.GetTaggedLocalElementIds();
                                if (hostIds.Count > 0)
                                {
                                    Element host = doc.GetElement(hostIds.First());
                                    if (host != null)
                                    {
                                        for (int tier = 1; tier <= 3; tier++)
                                        {
                                            // TAG_PARA_STATE_N_BOOL is TEXT in v5.3+ (Revit label
                                            // Calculated Values cannot reference YESNO). Route through
                                            // SetYesNo so both TEXT and legacy INTEGER storage work.
                                            string boolParam = $"TAG_PARA_STATE_{tier}_BOOL";
                                            ParameterHelpers.SetYesNo(host, boolParam,
                                                tier <= ParaDepth, overwrite: true);
                                        }
                                    }
                                }
                            }
                            catch (Exception ex) { StingLog.Warn($"Write paragraph depth params: {ex.Message}"); }
                        }

                        applied++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"TagControlSession: tag {tag.Id}: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            StingLog.Info($"TagControlSession.Apply: applied settings to {applied} tags in '{view.Name}'");
            return applied;
        }
    }
}
