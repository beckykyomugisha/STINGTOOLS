// ============================================================================
// StructuralPhase143Postproc.cs — post-conversion enhancements:
//   - SlabRoomSeeder            (DWG-STRUCT-P3B)
//   - StructuralViewCreator     (DWG-STRUCT-P3C)
//   - BeamMaterialInferrer      (DWG-STRUCT-DEEP-1)
//   - FoundationClassifier      (DWG-STRUCT-DEEP-2)
//   - JunctionMarkStamper       (DWG-STRUCT-DEEP-6)
//
// All helpers are side-effect-free or wrap their writes in their own
// short-lived sub-transaction so a failure cannot roll back the main
// conversion. Each closes one row in docs/ROADMAP.md.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Model

{

    /// <summary>P3B — Seed Revit Rooms at each created slab's centroid.
    /// Skips centroids that fall inside a slab void (lift shafts etc.).
    /// Uses any text on the slab layer near the centroid as the room Name.</summary>
    internal static class SlabRoomSeeder
    {
        public class SeedResult
        {
            public int RoomsCreated;
            public int Skipped_HasRoom;
            public int Skipped_InVoid;
            public int Skipped_NoLevel;
        }

        /// <summary>Seed rooms at each slab whose centroid is not already inside
        /// an existing room. Returns the per-category count.</summary>
        public static SeedResult Seed(Document doc, Level level,
            IList<DetectedLoop> outerSlabs, IList<DetectedLoop> voidLoops,
            DWGConversionConfig cfg)
        {
            var result = new SeedResult();
            if (doc == null || outerSlabs == null || outerSlabs.Count == 0) return result;
            if (cfg == null || !cfg.SeedRoomsFromSlabs) return result;

            // Phase plan: rooms must live on a level that has a phase. Use the
            // doc's last phase (latest); the default phase that ships with most
            // templates is "New Construction".
            var phase = new FilteredElementCollector(doc).OfClass(typeof(Phase))
                .Cast<Phase>().LastOrDefault();
            if (phase == null) return result;
            if (level == null) { result.Skipped_NoLevel = outerSlabs.Count; return result; }

            // Existing rooms — used to skip centroids already inside a room.
            var existing = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(r => r.Area > 1e-6)
                .ToList();

            using (var tx = new SubTransaction(doc))
            {
                try
                {
                    tx.Start();
                    foreach (var outer in outerSlabs)
                    {
                        if (outer?.Points == null || outer.Points.Count < 3) continue;
                        var centroid = Centroid(outer.Points);
                        if (centroid == null) continue;
                        if (PointInAnyVoid(centroid, voidLoops)) { result.Skipped_InVoid++; continue; }

                        // Skip if a room already exists at this point.
                        bool roomHere = existing.Any(r =>
                            r.IsPointInRoom(new XYZ(centroid.X, centroid.Y,
                                level.Elevation + 0.1)));
                        if (roomHere) { result.Skipped_HasRoom++; continue; }

                        try
                        {
                            var uv = new UV(centroid.X, centroid.Y);
                            var room = doc.Create.NewRoom(level, uv);
                            if (room != null) result.RoomsCreated++;
                        }
                        catch (Exception ex) { StingLog.Warn($"NewRoom: {ex.Message}"); }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"SlabRoomSeeder: {ex.Message}");
                    if (tx.HasStarted()) tx.RollBack();
                }
            }
            return result;
        }

        private static XYZ Centroid(IList<XYZ> pts)
        {
            if (pts == null || pts.Count == 0) return null;
            double sx = 0, sy = 0;
            foreach (var p in pts) { sx += p.X; sy += p.Y; }
            return new XYZ(sx / pts.Count, sy / pts.Count, pts[0].Z);
        }

        private static bool PointInAnyVoid(XYZ pt, IList<DetectedLoop> voidLoops)
        {
            if (voidLoops == null) return false;
            foreach (var v in voidLoops)
            {
                if (v?.Points == null || v.Points.Count < 3) continue;
                if (PointInPoly(pt, v.Points)) return true;
            }
            return false;
        }

        private static bool PointInPoly(XYZ pt, IList<XYZ> poly)
        {
            int crossings = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                if ((a.Y > pt.Y) != (b.Y > pt.Y))
                {
                    double xCross = (b.X - a.X) * (pt.Y - a.Y) / (b.Y - a.Y + 1e-12) + a.X;
                    if (pt.X < xCross) crossings++;
                }
            }
            return (crossings & 1) == 1;
        }
    }

    /// <summary>P3C — After conversion, create one structural ViewPlan per
    /// level that received elements. Looks up a Phase-113 DrawingType via
    /// DrawingDispatcher and applies via DrawingTypePresentation.Apply().
    /// </summary>
    internal static class StructuralViewCreator
    {
        public class CreateResult
        {
            public List<ElementId> CreatedViewIds = new List<ElementId>();
            public int LevelsProcessed;
            public int LevelsSkipped;
        }

        public static CreateResult CreateViews(Document doc,
            IList<ElementId> createdElementIds, DWGConversionConfig cfg)
        {
            var result = new CreateResult();
            if (doc == null || cfg == null
                || !cfg.CreateStructuralViewsAfterConversion) return result;
            if (createdElementIds == null || createdElementIds.Count == 0) return result;

            // Collect levels referenced by the created elements.
            var levelIds = new HashSet<ElementId>();
            foreach (var id in createdElementIds)
            {
                var el = doc.GetElement(id);
                if (el == null) continue;
                var lvlId = el.LevelId;
                if (lvlId != null && lvlId != ElementId.InvalidElementId)
                    levelIds.Add(lvlId);
            }
            if (levelIds.Count == 0) return result;

            // Find the Structural Plan ViewFamilyType.
            var vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.StructuralPlan);
            if (vft == null)
            {
                StingLog.Warn("StructuralViewCreator: no StructuralPlan ViewFamilyType.");
                return result;
            }

            // Resolve the DrawingType (Phase 113) — best-effort: catch any
            // exception so the conversion isn't blocked by a missing registry.
            DrawingType dt = null;
            try { dt = DrawingDispatcher.Resolve(doc, "S", "*", "PLAN"); }
            catch (Exception ex) { StingLog.Warn($"DrawingDispatcher.Resolve: {ex.Message}"); }

            using (var tx = new SubTransaction(doc))
            {
                try
                {
                    tx.Start();
                    foreach (var lvlId in levelIds)
                    {
                        var lvl = doc.GetElement(lvlId) as Level;
                        if (lvl == null) { result.LevelsSkipped++; continue; }
                        try
                        {
                            var view = ViewPlan.Create(doc, vft.Id, lvl.Id);
                            if (view == null) { result.LevelsSkipped++; continue; }
                            try { view.Name = $"STING - Structural Plan - {lvl.Name}"; }
                            catch { /* duplicate-name; let Revit auto-suffix */ }
                            result.CreatedViewIds.Add(view.Id);

                            // Apply DrawingType if resolved.
                            if (dt != null)
                            {
                                try { DrawingTypePresentation.Apply(doc, view, dt); }
                                catch (Exception ex2)
                                { StingLog.Warn($"DrawingTypePresentation.Apply on {lvl.Name}: {ex2.Message}"); }
                            }
                            result.LevelsProcessed++;
                        }
                        catch (Exception ex2)
                        {
                            StingLog.Warn($"ViewPlan.Create level {lvl.Name}: {ex2.Message}");
                            result.LevelsSkipped++;
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex2)
                {
                    StingLog.Warn($"StructuralViewCreator: {ex2.Message}");
                    if (tx.HasStarted()) tx.RollBack();
                }
            }
            return result;
        }
    }

    /// <summary>DEEP-1 — Heuristically classify each detected beam as steel
    /// I-section vs concrete rectangle. Single-line beams (no parallel-pair
    /// width evidence) hint steel; parallel-pair beams hint concrete.
    /// Mutates DetectedBeam.LayerName with a "STING:Material=" suffix that
    /// downstream type matching can read.</summary>
    internal static class BeamMaterialInferrer
    {
        public enum Material { Unknown, ConcreteRectangle, SteelISection }

        /// <summary>Annotate each beam with a material hint. Returns the
        /// per-material count for logging.</summary>
        public static (int Concrete, int Steel, int Unknown) AnnotateAll(
            IList<DetectedBeam> beams, DWGConversionConfig cfg)
        {
            int concrete = 0, steel = 0, unknown = 0;
            if (beams == null || beams.Count == 0) return (0, 0, 0);
            if (cfg == null || !cfg.InferBeamMaterial) return (0, 0, 0);

            foreach (var b in beams)
            {
                if (b == null) continue;
                Material m = Classify(b);
                string tag = m switch
                {
                    Material.ConcreteRectangle => "STING:Material=Concrete",
                    Material.SteelISection     => "STING:Material=Steel",
                    _                          => "STING:Material=Unknown",
                };
                // Append tag to layer name without losing the original.
                b.LayerName = string.IsNullOrEmpty(b.LayerName)
                    ? tag : $"{b.LayerName}|{tag}";
                if (m == Material.ConcreteRectangle) concrete++;
                else if (m == Material.SteelISection) steel++;
                else unknown++;
            }
            return (concrete, steel, unknown);
        }

        /// <summary>Classifier: parallel-pair detected (WidthDetected==true)
        /// with width ≥ 200 mm → concrete rectangle. WidthDetected==false
        /// (single centreline only) → steel I-section. Otherwise Unknown.</summary>
        public static Material Classify(DetectedBeam b)
        {
            if (b == null) return Material.Unknown;
            // Parallel-pair detection sets WidthDetected=true.
            if (b.WidthDetected && b.WidthMm >= 200) return Material.ConcreteRectangle;
            if (!b.WidthDetected) return Material.SteelISection;
            // WidthDetected=true but very narrow → unusual; default to concrete.
            return Material.ConcreteRectangle;
        }
    }

    /// <summary>DEEP-2 — Foundation classifier. Splits detected foundation
    /// rectangles into Pad / Raft / PileCap based on plan area + clustering.
    /// Pads and pile caps stay on the existing pad-foundation creation path;
    /// rafts route to the slab-creation path so they materialise as
    /// structural floors at the foundation depth.</summary>
    internal static class FoundationClassifier
    {
        public enum FoundationType { Pad, Raft, PileCap }

        public class ClassifiedFoundation
        {
            public FoundationType Type;
            public DetectedRectangle Rect;          // pads + pile caps
            public DetectedLoop      Loop;          // rafts (synth from rect bbox)
            public string CommentsStamp;            // for downstream stamping
        }

        public static List<ClassifiedFoundation> Classify(
            IList<DetectedRectangle> rects, DWGConversionConfig cfg)
        {
            var result = new List<ClassifiedFoundation>();
            if (rects == null || rects.Count == 0) return result;
            if (cfg == null || !cfg.ClassifyFoundations)
            {
                // No-op classifier: every rectangle is a pad.
                foreach (var r in rects)
                    result.Add(new ClassifiedFoundation { Type = FoundationType.Pad, Rect = r });
                return result;
            }

            double raftMinAreaSqFt = cfg.RaftMinAreaM2 * 10.7639; // m² → ft²
            // Pile cap clustering: a rectangle within 2× its size of ≥2 other
            // rectangles is a candidate pile cap. Heuristic — works for
            // typical 3-5-pile arrangements drawn as nested squares.
            // Compute pairwise neighbour count.
            int n = rects.Count;
            int[] neighbourCount = new int[n];
            for (int i = 0; i < n; i++)
            {
                var ri = rects[i];
                if (ri?.Center == null) continue;
                double pileSpacingFt = Math.Max(ri.WidthFt, ri.DepthFt) * 2.5;
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    var rj = rects[j];
                    if (rj?.Center == null) continue;
                    double dx = rj.Center.X - ri.Center.X;
                    double dy = rj.Center.Y - ri.Center.Y;
                    if (dx * dx + dy * dy <= pileSpacingFt * pileSpacingFt)
                        neighbourCount[i]++;
                }
            }

            for (int i = 0; i < n; i++)
            {
                var r = rects[i];
                if (r == null) continue;
                double areaSqFt = r.WidthFt * r.DepthFt;

                FoundationType type;
                if (areaSqFt >= raftMinAreaSqFt)
                    type = FoundationType.Raft;
                else if (neighbourCount[i] >= 2)
                    type = FoundationType.PileCap;
                else
                    type = FoundationType.Pad;

                var cf = new ClassifiedFoundation
                {
                    Type = type,
                    Rect = r,
                    CommentsStamp = type switch
                    {
                        FoundationType.Raft     => "STING: Raft foundation",
                        FoundationType.PileCap  => "STING: PileCap",
                        _                       => null,
                    },
                };
                if (type == FoundationType.Raft)
                {
                    // Build a 4-corner DetectedLoop from the rectangle bbox.
                    double hw = r.WidthFt * 0.5, hd = r.DepthFt * 0.5;
                    cf.Loop = new DetectedLoop
                    {
                        Points = new List<XYZ>
                        {
                            new XYZ(r.Center.X - hw, r.Center.Y - hd, 0),
                            new XYZ(r.Center.X + hw, r.Center.Y - hd, 0),
                            new XYZ(r.Center.X + hw, r.Center.Y + hd, 0),
                            new XYZ(r.Center.X - hw, r.Center.Y + hd, 0),
                        },
                        LayerName = "STING-RAFT",
                    };
                }
                result.Add(cf);
            }
            return result;
        }
    }

    /// <summary>DEEP-6 — Stamp the Mark parameter of every beam/column
    /// participating in a detected junction with a junction-type tag.
    /// Lets engineers find junction participants via schedule filters
    /// without traversing the analytical graph.</summary>
    internal static class JunctionMarkStamper
    {
        public class StampResult
        {
            public int ColumnsStamped;
            public int BeamsStamped;
        }

        public static StampResult Stamp(Document doc,
            IList<(XYZ Point, string JunctionType, int BeamCount)> junctions,
            IList<ElementId> createdElementIds, DWGConversionConfig cfg,
            double clusterToleranceMm = 250)
        {
            var result = new StampResult();
            if (doc == null || junctions == null || junctions.Count == 0) return result;
            if (createdElementIds == null || createdElementIds.Count == 0) return result;
            if (cfg == null || !cfg.StampJunctionMarks) return result;

            double tolFt = clusterToleranceMm / 304.8;
            double tolFtSq = tolFt * tolFt;

            // Index participants by category for fast lookup.
            var columns = new List<(ElementId Id, XYZ Pt)>();
            var beams = new List<(ElementId Id, XYZ Start, XYZ End)>();
            foreach (var id in createdElementIds)
            {
                var el = doc.GetElement(id);
                if (el == null) continue;
                // CS0618: ElementId.IntegerValue is deprecated since Revit 2024; .Value returns long.
                int catId = (int)(el.Category?.Id?.Value ?? 0L);
                if (catId == (int)BuiltInCategory.OST_StructuralColumns
                    && el.Location is LocationPoint lp)
                    columns.Add((id, lp.Point));
                else if (catId == (long)BuiltInCategory.OST_StructuralFraming
                    && el.Location is LocationCurve lc && lc.Curve != null)
                    beams.Add((id, lc.Curve.GetEndPoint(0), lc.Curve.GetEndPoint(1)));
            }

            using (var tx = new SubTransaction(doc))
            {
                try
                {
                    tx.Start();
                    foreach (var (jpt, jtype, _) in junctions)
                    {
                        if (jpt == null || string.IsNullOrEmpty(jtype)) continue;
                        string tag = ShortTag(jtype);
                        if (string.IsNullOrEmpty(tag)) continue;

                        // Stamp columns whose insertion point is within tolerance.
                        foreach (var (id, pt) in columns)
                        {
                            double dx = pt.X - jpt.X, dy = pt.Y - jpt.Y;
                            if (dx * dx + dy * dy <= tolFtSq)
                                if (AppendMark(doc, id, tag)) result.ColumnsStamped++;
                        }
                        // Stamp beams whose either endpoint is within tolerance.
                        foreach (var (id, start, end) in beams)
                        {
                            double sx = start.X - jpt.X, sy = start.Y - jpt.Y;
                            double ex = end.X - jpt.X, ey = end.Y - jpt.Y;
                            if (sx * sx + sy * sy <= tolFtSq
                                || ex * ex + ey * ey <= tolFtSq)
                                if (AppendMark(doc, id, tag)) result.BeamsStamped++;
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"JunctionMarkStamper: {ex.Message}");
                    if (tx.HasStarted()) tx.RollBack();
                }
            }
            return result;
        }

        private static string ShortTag(string jtype)
        {
            if (jtype.IndexOf("Cross", StringComparison.OrdinalIgnoreCase) >= 0)
                return "J:X";
            if (jtype.IndexOf("T-junction", StringComparison.OrdinalIgnoreCase) >= 0)
                return "J:T";
            if (jtype.IndexOf("L-junction", StringComparison.OrdinalIgnoreCase) >= 0)
                return "J:L";
            if (jtype.IndexOf("splice", StringComparison.OrdinalIgnoreCase) >= 0)
                return "J:S";
            return null; // free ends + warnings already surfaced as TextNotes
        }

        private static bool AppendMark(Document doc, ElementId id, string tag)
        {
            var el = doc.GetElement(id);
            var p = el?.LookupParameter("Mark");
            if (p == null || p.IsReadOnly) return false;
            try
            {
                string current = p.AsString() ?? "";
                if (current.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                    return false; // already stamped — skip
                string next = string.IsNullOrEmpty(current) ? tag : $"{current} {tag}";
                p.Set(next);
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }
    }
}
