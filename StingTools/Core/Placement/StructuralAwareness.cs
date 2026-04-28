// Phase 139.3 — Structural-awareness adapter for the placement centre.
//
// Wraps the existing StingTools.Model engines (AnalyzeLoadPaths,
// DetectJunctions, OpeningDetector) and the live-model wall-opening
// query behind one room-bounded API the scorer + InWallChaseRouter
// can call cheaply.
//
// The four engine functions are not all relevant at placement time:
//   - AnalyzeLoadPaths   → useful: identifies columns + beams + foundations
//                          we must not penetrate or chase through.
//   - DetectJunctions    → useful: T/L/Cross beam intersections are
//                          forbidden routing zones.
//   - OpeningDetector    → DWG-import-time only. Replaced here by a
//                          live-model Wall.FindInserts() lookup so chase
//                          routing can pass through doors / windows.
//   - DetectStructuralWalls / FindOrCreateBeamType / DetectCircularColumns
//                        → not relevant at placement time.
//
// The adapter is invoked at most once per (document, room) by callers,
// caches its results, and never throws — degraded mode is "no
// structural data, route freely" (logged as warning).

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;

namespace StingTools.Core.Placement
{
    public class StructuralAwareness
    {
        // Per-document cache. The adapter is shared across one Place run.
        private readonly Document _doc;
        private HashSet<ElementId> _loadBearingIds;
        private List<XYZ> _junctionPoints;
        private readonly Dictionary<ElementId, List<BoundingBoxXYZ>> _wallOpeningCache
            = new Dictionary<ElementId, List<BoundingBoxXYZ>>();

        public StructuralAwareness(Document doc) { _doc = doc; }

        /// <summary>
        /// True when the supplied element is structurally load-bearing —
        /// chase routing must not penetrate it. Built lazily from the
        /// project's columns, beams, foundations, and from any walls
        /// whose Structural usage flag is set.
        /// </summary>
        public bool IsLoadBearing(Element el)
        {
            if (el == null) return false;
            EnsureLoadBearingSet();
            return _loadBearingIds.Contains(el.Id);
        }

        /// <summary>
        /// Returns true when any beam-junction (T/L/Cross from
        /// DetectJunctions, or any column centre) sits within
        /// clearanceFt of <paramref name="point"/>. Routing helpers
        /// avoid these points.
        /// </summary>
        public bool IsNearJunction(XYZ point, double clearanceFt)
        {
            if (point == null) return false;
            EnsureJunctions();
            double sq = clearanceFt * clearanceFt;
            foreach (var j in _junctionPoints)
            {
                double dx = j.X - point.X, dy = j.Y - point.Y;
                if (dx * dx + dy * dy <= sq) return true;
            }
            return false;
        }

        /// <summary>
        /// Returns the world-aligned bounding boxes of every door /
        /// window / opening hosted in the wall. Used by the chase
        /// router to permit routing across an opening rather than
        /// detouring around it.
        /// </summary>
        public List<BoundingBoxXYZ> GetWallOpenings(Wall wall)
        {
            if (wall == null) return new List<BoundingBoxXYZ>();
            if (_wallOpeningCache.TryGetValue(wall.Id, out var hit)) return hit;
            var list = new List<BoundingBoxXYZ>();
            try
            {
                var inserts = wall.FindInserts(true, true, true, true);
                if (inserts != null)
                {
                    foreach (var id in inserts)
                    {
                        var ins = _doc.GetElement(id);
                        var bb = ins?.get_BoundingBox(null);
                        if (bb != null) list.Add(bb);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"StructuralAwareness.GetWallOpenings {wall.Id}: {ex.Message}"); }
            _wallOpeningCache[wall.Id] = list;
            return list;
        }

        /// <summary>True if <paramref name="point"/> is inside a wall opening
        /// (door / window) on the given wall.</summary>
        public bool PointIsInOpening(Wall wall, XYZ point, double slackFt = 0.05)
        {
            if (wall == null || point == null) return false;
            foreach (var bb in GetWallOpenings(wall))
            {
                if (point.X >= bb.Min.X - slackFt && point.X <= bb.Max.X + slackFt
                 && point.Y >= bb.Min.Y - slackFt && point.Y <= bb.Max.Y + slackFt
                 && point.Z >= bb.Min.Z - slackFt && point.Z <= bb.Max.Z + slackFt)
                    return true;
            }
            return false;
        }

        /// <summary>True when chase routing the given segment is permitted —
        /// the segment must not start/end inside a load-bearing column or
        /// beam-junction zone.</summary>
        public bool SegmentIsRoutable(Wall hostWall, XYZ a, XYZ b, double clearanceFt = 0.5)
        {
            if (a == null || b == null) return false;
            if (IsNearJunction(a, clearanceFt) || IsNearJunction(b, clearanceFt)) return false;
            return true;
        }

        // ── Internal ────────────────────────────────────────────────

        private void EnsureLoadBearingSet()
        {
            if (_loadBearingIds != null) return;
            _loadBearingIds = new HashSet<ElementId>();
            if (_doc == null) return;
            try
            {
                var cats = new[]
                {
                    BuiltInCategory.OST_StructuralColumns,
                    BuiltInCategory.OST_StructuralFraming,
                    BuiltInCategory.OST_StructuralFoundation,
                };
                foreach (var bic in cats)
                {
                    foreach (var el in new FilteredElementCollector(_doc)
                        .OfCategory(bic).WhereElementIsNotElementType())
                        _loadBearingIds.Add(el.Id);
                }
                // Walls flagged as Structural / shear / bearing.
                foreach (var el in new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType())
                {
                    if (!(el is Wall w)) continue;
                    if (w.StructuralUsage != StructuralWallUsage.NonBearing)
                        _loadBearingIds.Add(w.Id);
                }
            }
            catch (Exception ex) { StingLog.Warn($"StructuralAwareness.EnsureLoadBearingSet: {ex.Message}"); }
        }

        private void EnsureJunctions()
        {
            if (_junctionPoints != null) return;
            _junctionPoints = new List<XYZ>();
            if (_doc == null) return;
            try
            {
                // Beam-endpoint clustering (lightweight version of
                // StructuralCADPipeline.DetectJunctions; runs on the live
                // model rather than on a DWG ExtractionResult).
                var beamEndpoints = new List<XYZ>();
                foreach (var el in new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType())
                {
                    if (el.Location is LocationCurve lc && lc.Curve != null)
                    {
                        beamEndpoints.Add(lc.Curve.GetEndPoint(0));
                        beamEndpoints.Add(lc.Curve.GetEndPoint(1));
                    }
                }
                const double clusterFt = 25.0 / 304.8; // 25mm
                var used = new HashSet<int>();
                for (int i = 0; i < beamEndpoints.Count; i++)
                {
                    if (used.Contains(i)) continue;
                    used.Add(i);
                    int n = 1;
                    XYZ acc = beamEndpoints[i];
                    for (int j = i + 1; j < beamEndpoints.Count; j++)
                    {
                        if (used.Contains(j)) continue;
                        if (beamEndpoints[i].DistanceTo(beamEndpoints[j]) < clusterFt)
                        { acc += beamEndpoints[j]; n++; used.Add(j); }
                    }
                    if (n >= 2) _junctionPoints.Add(acc * (1.0 / n));
                }
                // Add column centres — chase routing should also avoid these.
                foreach (var bic in new[] { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns })
                {
                    foreach (var el in new FilteredElementCollector(_doc)
                        .OfCategory(bic).WhereElementIsNotElementType())
                    {
                        var pt = (el.Location as LocationPoint)?.Point;
                        if (pt != null) _junctionPoints.Add(pt);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"StructuralAwareness.EnsureJunctions: {ex.Message}"); }
        }
    }
}
