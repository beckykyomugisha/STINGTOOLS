// ============================================================================
// StructuralPhase140Accuracy.cs — Phase-140 accuracy helpers for the
// structural DWG-to-BIM pipeline.
//
// Each helper is intentionally side-effect-free or wraps its writes in its
// own short-lived sub-transaction so a failure in any one helper cannot
// roll back the main conversion. All depend only on:
//
//   - Detection types in StructuralCADPipeline.cs / CADToModelEngine.cs
//   - DWGConversionConfig in StructuralCADWizard.cs
//   - StingTools.Core.StingLog + Units (ModelEngine.cs)
//
// No new Revit shared parameters are required.
//
// Helpers:
//   - GridSnapper            P1-A: snap column centres to grid intersections
//   - BeamDepthCalculator    P1-B: span-proportional beam depth
//   - BeamTrimmer            P1-D: trim beam ends to column faces
//   - DuplicateDetector      P3-A: skip detected elements that already exist
//   - SlabVoidDetector       P2-B: find nested closed loops as slab voids
//   - GridLabelMarkBuilder   P2-C: build "{vert}/{horiz}" marks from snaps
//   - StructuralWarningPlacer  P2-D: post-creation warnings as TextNotes
//   - WallEndpointBridger    P2-F: virtual extension to close endpoint gaps
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Model
{
    /// <summary>P1-A — Snaps detected column centres to nearest grid intersection.
    /// Pure: returns a mapping; the caller mutates the detection records.</summary>
    internal static class GridSnapper
    {
        /// <summary>Result of snapping a single column centre.</summary>
        public class SnapResult
        {
            public XYZ OriginalCentre;
            public XYZ SnappedCentre;
            public bool DidSnap;
            public string VerticalGridLabel;
            public string HorizontalGridLabel;
        }

        /// <summary>Snap all column centres in <paramref name="columnCentres"/> to the
        /// nearest grid intersection from <paramref name="gridLines"/>, when within
        /// <paramref name="toleranceMm"/>. Returns a parallel list of SnapResult.</summary>
        public static List<SnapResult> Snap(
            IList<XYZ> columnCentres,
            IList<DetectedGridLine> gridLines,
            double toleranceMm)
        {
            var results = new List<SnapResult>(columnCentres?.Count ?? 0);
            if (columnCentres == null || columnCentres.Count == 0)
                return results;

            // No grid lines or zero tolerance — pass through unchanged.
            if (gridLines == null || gridLines.Count == 0 || toleranceMm <= 0)
            {
                foreach (var c in columnCentres)
                    results.Add(new SnapResult { OriginalCentre = c, SnappedCentre = c });
                return results;
            }

            double tolFt = toleranceMm * Units.MmToFeet;
            double tolFtSq = tolFt * tolFt;

            // Pre-classify grids into vertical (constant X) vs horizontal (constant Y),
            // independent of the IsHorizontal flag (which is a draughting hint).
            var verticals   = new List<(double X, string Label)>();
            var horizontals = new List<(double Y, string Label)>();
            foreach (var g in gridLines)
            {
                if (g?.Start == null || g.End == null) continue;
                double dx = Math.Abs(g.End.X - g.Start.X);
                double dy = Math.Abs(g.End.Y - g.Start.Y);
                if (dx < dy * 0.1) verticals.Add((0.5 * (g.Start.X + g.End.X), g.Label ?? ""));
                else if (dy < dx * 0.1) horizontals.Add((0.5 * (g.Start.Y + g.End.Y), g.Label ?? ""));
            }

            foreach (var c in columnCentres)
            {
                var sr = new SnapResult { OriginalCentre = c, SnappedCentre = c };

                // Find the closest vertical (column line) and the closest horizontal (row line).
                double bestVx = double.NaN; double bestVdx = double.MaxValue; string vLabel = "";
                foreach (var (gx, lbl) in verticals)
                {
                    double d = Math.Abs(c.X - gx);
                    if (d < bestVdx) { bestVdx = d; bestVx = gx; vLabel = lbl; }
                }
                double bestHy = double.NaN; double bestHdy = double.MaxValue; string hLabel = "";
                foreach (var (gy, lbl) in horizontals)
                {
                    double d = Math.Abs(c.Y - gy);
                    if (d < bestHdy) { bestHdy = d; bestHy = gy; hLabel = lbl; }
                }

                if (!double.IsNaN(bestVx) && !double.IsNaN(bestHy)
                    && bestVdx * bestVdx + bestHdy * bestHdy <= tolFtSq)
                {
                    sr.SnappedCentre = new XYZ(bestVx, bestHy, c.Z);
                    sr.DidSnap = true;
                    sr.VerticalGridLabel = vLabel;
                    sr.HorizontalGridLabel = hLabel;
                }
                results.Add(sr);
            }
            return results;
        }

        /// <summary>Apply snapping in-place to <see cref="DetectedRectangle"/> centres.
        /// Returns the number of rectangles snapped.</summary>
        public static int SnapRectangles(IList<DetectedRectangle> rects,
            IList<DetectedGridLine> gridLines, double toleranceMm,
            out List<SnapResult> snapInfo)
        {
            snapInfo = new List<SnapResult>();
            if (rects == null || rects.Count == 0) return 0;
            var centres = rects.Select(r => r.Center).ToList();
            var results = Snap(centres, gridLines, toleranceMm);
            int n = 0;
            for (int i = 0; i < rects.Count; i++)
            {
                snapInfo.Add(results[i]);
                if (results[i].DidSnap) { rects[i].Center = results[i].SnappedCentre; n++; }
            }
            return n;
        }

        /// <summary>Apply snapping in-place to <see cref="DetectedCircle"/> centres.</summary>
        public static int SnapCircles(IList<DetectedCircle> circles,
            IList<DetectedGridLine> gridLines, double toleranceMm,
            out List<SnapResult> snapInfo)
        {
            snapInfo = new List<SnapResult>();
            if (circles == null || circles.Count == 0) return 0;
            var centres = circles.Select(r => r.Center).ToList();
            var results = Snap(centres, gridLines, toleranceMm);
            int n = 0;
            for (int i = 0; i < circles.Count; i++)
            {
                snapInfo.Add(results[i]);
                if (results[i].DidSnap) { circles[i].Center = results[i].SnappedCentre; n++; }
            }
            return n;
        }
    }

    /// <summary>P1-B — Span-proportional beam depth.</summary>
    internal static class BeamDepthCalculator
    {
        /// <summary>Compute the depth (mm) for a beam of the given span.</summary>
        public static double ComputeDepthMm(double spanMm, DWGConversionConfig cfg)
        {
            double floor = cfg?.BeamDepthMm ?? 450;
            if (cfg == null || !cfg.UseSpanToDepthRatio || spanMm <= 0
                || cfg.SpanToDepthRatio <= 0) return floor;

            double raw = spanMm / cfg.SpanToDepthRatio;
            double clamped = Math.Max(cfg.BeamDepthMinMm, Math.Min(cfg.BeamDepthMaxMm, raw));

            // Round to nearest 25 mm for practical type matching.
            double rounded = Math.Round(clamped / 25.0) * 25.0;

            // Wizard minimum always wins so users can pin to a project-standard depth.
            return Math.Max(floor, rounded);
        }
    }

    /// <summary>P1-D — Move beam endpoints from junction centroids out to the face
    /// of the connecting column. Pure: mutates the DetectedBeam list passed in
    /// and reports how many endpoints were trimmed.</summary>
    internal static class BeamTrimmer
    {
        public static int TrimEndpointsToColumns(
            IList<DetectedBeam> beams,
            IList<DetectedRectangle> rectColumns,
            IList<DetectedCircle> circleColumns,
            DWGConversionConfig cfg)
        {
            if (beams == null || beams.Count == 0) return 0;
            if (cfg == null || !cfg.TrimBeamsToColumnFaces) return 0;

            // 25 mm cover offset along beam axis from column face.
            double coverFt = 25.0 * Units.MmToFeet;

            int trimmed = 0;
            foreach (var beam in beams)
            {
                if (beam?.Start == null || beam.End == null) continue;
                var dirVec = beam.End - beam.Start;
                double len = dirVec.GetLength();
                if (len < 1e-6) continue;
                XYZ dir = dirVec / len;

                // Trim start end: column whose footprint contains beam.Start.
                if (FindColumnFaceOffset(beam.Start, rectColumns, circleColumns, dir,
                    inwardDirection: true, out double startOffsetFt))
                {
                    beam.Start = beam.Start + dir * (startOffsetFt + coverFt);
                    trimmed++;
                }

                // Trim end end: column whose footprint contains beam.End.
                if (FindColumnFaceOffset(beam.End, rectColumns, circleColumns, dir,
                    inwardDirection: false, out double endOffsetFt))
                {
                    beam.End = beam.End - dir * (endOffsetFt + coverFt);
                    trimmed++;
                }
            }
            return trimmed;
        }

        /// <summary>Returns the offset from the given point to the column face along
        /// the beam direction, IF the point sits inside any column footprint.</summary>
        private static bool FindColumnFaceOffset(XYZ point,
            IList<DetectedRectangle> rectColumns, IList<DetectedCircle> circleColumns,
            XYZ dir, bool inwardDirection, out double offsetFt)
        {
            offsetFt = 0;
            if (rectColumns != null)
            {
                foreach (var r in rectColumns)
                {
                    if (r?.Center == null) continue;
                    double dxBeam = Math.Abs(point.X - r.Center.X);
                    double dyBeam = Math.Abs(point.Y - r.Center.Y);
                    double halfW = r.WidthFt * 0.5;
                    double halfD = r.DepthFt * 0.5;
                    if (dxBeam <= halfW && dyBeam <= halfD)
                    {
                        // Project the half-extent along the beam direction onto the
                        // axis-aligned column footprint to find the face offset.
                        double dxDir = Math.Abs(dir.X);
                        double dyDir = Math.Abs(dir.Y);
                        double tx = dxDir > 1e-6 ? halfW / dxDir : double.MaxValue;
                        double ty = dyDir > 1e-6 ? halfD / dyDir : double.MaxValue;
                        offsetFt = Math.Min(tx, ty);
                        return true;
                    }
                }
            }
            if (circleColumns != null)
            {
                foreach (var c in circleColumns)
                {
                    if (c?.Center == null) continue;
                    double d2 = (point.X - c.Center.X) * (point.X - c.Center.X)
                              + (point.Y - c.Center.Y) * (point.Y - c.Center.Y);
                    if (d2 <= c.RadiusFt * c.RadiusFt)
                    {
                        // Distance from point to far edge along the beam axis.
                        offsetFt = c.RadiusFt;
                        return true;
                    }
                }
            }
            return false;
        }
    }

    /// <summary>Phase-142 — Synthesize strip foundation loops along detected
    /// structural walls. Each wall gets a rectangular loop wider than the wall
    /// by <c>StripFndOversizeMm</c> per side (per EC7 §6.5 reference) and
    /// extending the full length of the wall. Output is a list of
    /// <see cref="DetectedLoop"/> ready for the slab/floor-creation path with
    /// structural-floor flag.</summary>
    internal static class StripFoundationDetector
    {
        public class StripFoundation
        {
            public DetectedLoop Loop;
            public DetectedWall SourceWall;
            public double WidthMm;
            public double LengthMm;
        }

        /// <summary>Build strip foundations under each detected wall.</summary>
        public static List<StripFoundation> Detect(
            IList<DetectedWall> walls, DWGConversionConfig cfg)
        {
            var result = new List<StripFoundation>();
            if (walls == null || walls.Count == 0) return result;
            if (cfg == null || !cfg.DetectStripFoundations) return result;

            double oversideFt = cfg.StripFndOversizeMm * Units.MmToFeet;

            foreach (var w in walls)
            {
                if (w?.CenterStart == null || w.CenterEnd == null) continue;
                var d = w.CenterEnd - w.CenterStart;
                double len = Math.Sqrt(d.X * d.X + d.Y * d.Y);
                if (len < 1e-6) continue;
                XYZ axis = new XYZ(d.X / len, d.Y / len, 0);
                XYZ normal = new XYZ(-axis.Y, axis.X, 0);

                double halfWidthFt = w.ThicknessFt * 0.5 + oversideFt;

                // Rectangle corners — extend slightly beyond the wall ends too.
                XYZ extStart = w.CenterStart - axis * oversideFt;
                XYZ extEnd   = w.CenterEnd   + axis * oversideFt;

                var corners = new List<XYZ>
                {
                    extStart + normal * halfWidthFt,
                    extEnd   + normal * halfWidthFt,
                    extEnd   - normal * halfWidthFt,
                    extStart - normal * halfWidthFt,
                };

                var loop = new DetectedLoop
                {
                    Points = corners,
                    LayerName = "STING-STRIP-FOUNDATION",
                };
                result.Add(new StripFoundation
                {
                    Loop = loop,
                    SourceWall = w,
                    WidthMm = (w.ThicknessFt + oversideFt * 2) * Units.FeetToMm,
                    LengthMm = (len + oversideFt * 2) * Units.FeetToMm,
                });
            }
            return result;
        }
    }

    /// <summary>Phase-142 — Classify each beam endpoint by what supports it
    /// (column / wall / unsupported). Used by per-beam wall-rest offset and
    /// cantilever detection.</summary>
    internal static class BeamSupportClassifier
    {
        public enum SupportType { None, Column, Wall, Both }

        public class BeamSupport
        {
            public DetectedBeam Beam;
            public SupportType StartSupport;
            public SupportType EndSupport;
            public bool RestsOnWall => StartSupport == SupportType.Wall
                                    || EndSupport == SupportType.Wall;
            public bool HasFreeEnd => StartSupport == SupportType.None
                                   || EndSupport == SupportType.None;
            public bool IsCantilever => HasFreeEnd
                                     && (StartSupport != SupportType.None
                                         || EndSupport != SupportType.None);
        }

        /// <summary>Classify support for each beam against detected columns + walls.
        /// Tolerance is in mm.</summary>
        public static List<BeamSupport> ClassifyAll(
            IList<DetectedBeam> beams,
            IList<DetectedRectangle> rectColumns,
            IList<DetectedCircle> circleColumns,
            IList<DetectedWall> walls,
            double toleranceMm = 200)
        {
            var results = new List<BeamSupport>();
            if (beams == null) return results;

            double tolFt = toleranceMm * Units.MmToFeet;
            double tolFtSq = tolFt * tolFt;

            foreach (var b in beams)
            {
                if (b?.Start == null || b.End == null) continue;
                results.Add(new BeamSupport
                {
                    Beam = b,
                    StartSupport = ClassifyEndpoint(b.Start, rectColumns, circleColumns, walls, tolFt, tolFtSq),
                    EndSupport   = ClassifyEndpoint(b.End,   rectColumns, circleColumns, walls, tolFt, tolFtSq),
                });
            }
            return results;
        }

        private static SupportType ClassifyEndpoint(XYZ pt,
            IList<DetectedRectangle> rects, IList<DetectedCircle> circles,
            IList<DetectedWall> walls, double tolFt, double tolFtSq)
        {
            // Column footprint test (rectangle bbox or circle).
            bool nearColumn = false;
            if (rects != null)
            {
                foreach (var r in rects)
                {
                    if (r?.Center == null) continue;
                    double dx = Math.Abs(pt.X - r.Center.X);
                    double dy = Math.Abs(pt.Y - r.Center.Y);
                    if (dx <= r.WidthFt * 0.5 + tolFt && dy <= r.DepthFt * 0.5 + tolFt)
                    { nearColumn = true; break; }
                }
            }
            if (!nearColumn && circles != null)
            {
                foreach (var c in circles)
                {
                    if (c?.Center == null) continue;
                    double dx = pt.X - c.Center.X, dy = pt.Y - c.Center.Y;
                    double r = c.RadiusFt + tolFt;
                    if (dx * dx + dy * dy <= r * r) { nearColumn = true; break; }
                }
            }

            // Wall centreline test — does the beam endpoint lie close to any wall?
            bool nearWall = false;
            if (walls != null)
            {
                foreach (var w in walls)
                {
                    if (w?.CenterStart == null || w.CenterEnd == null) continue;
                    if (DistancePointToSegmentSq(pt, w.CenterStart, w.CenterEnd) <= tolFtSq)
                    { nearWall = true; break; }
                }
            }

            if (nearColumn && nearWall) return SupportType.Both;
            if (nearColumn) return SupportType.Column;
            if (nearWall) return SupportType.Wall;
            return SupportType.None;
        }

        private static double DistancePointToSegmentSq(XYZ p, XYZ a, XYZ b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-12) { dx = p.X - a.X; dy = p.Y - a.Y; return dx * dx + dy * dy; }
            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
            t = Math.Max(0.0, Math.Min(1.0, t));
            double cx = a.X + t * dx, cy = a.Y + t * dy;
            double ex = p.X - cx, ey = p.Y - cy;
            return ex * ex + ey * ey;
        }
    }

    /// <summary>P3-A — Detect existing Revit elements within tolerance of a candidate
    /// insertion point, so we can skip duplicates on re-import.</summary>
    internal static class DuplicateDetector
    {
        public class ExistingIndex
        {
            private readonly Document _doc;
            private readonly Dictionary<BuiltInCategory, List<XYZ>> _byCat = new();

            public ExistingIndex(Document doc, params BuiltInCategory[] categories)
            {
                _doc = doc;
                foreach (var cat in categories)
                {
                    var pts = new List<XYZ>();
                    try
                    {
                        var col = new FilteredElementCollector(doc)
                            .OfCategory(cat).WhereElementIsNotElementType();
                        foreach (var el in col)
                        {
                            var loc = el.Location;
                            if (loc is LocationPoint lp) pts.Add(lp.Point);
                            else if (loc is LocationCurve lc && lc.Curve != null)
                                pts.Add(lc.Curve.Evaluate(0.5, true));
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"DuplicateDetector scan {cat}: {ex.Message}"); }
                    _byCat[cat] = pts;
                }
            }

            /// <summary>True when an existing element of <paramref name="cat"/> sits within
            /// <paramref name="toleranceMm"/> of <paramref name="point"/>.</summary>
            public bool IsDuplicate(BuiltInCategory cat, XYZ point, double toleranceMm)
            {
                if (point == null || toleranceMm <= 0) return false;
                if (!_byCat.TryGetValue(cat, out var pts) || pts.Count == 0) return false;
                double tolFt = toleranceMm * Units.MmToFeet;
                double tolFtSq = tolFt * tolFt;
                foreach (var p in pts)
                {
                    double dx = p.X - point.X, dy = p.Y - point.Y;
                    if (dx * dx + dy * dy <= tolFtSq) return true;
                }
                return false;
            }
        }
    }

    /// <summary>P2-B — Identify nested closed loops in slab boundary detection results
    /// as voids. Produces a list of (outer, voids[]) groupings.</summary>
    internal static class SlabVoidDetector
    {
        public class SlabWithVoids
        {
            public DetectedLoop Outer;
            public List<DetectedLoop> Voids = new List<DetectedLoop>();
        }

        public static List<SlabWithVoids> Group(IList<DetectedLoop> loops)
        {
            var results = new List<SlabWithVoids>();
            if (loops == null || loops.Count == 0) return results;

            // Sort by area descending — larger loops are candidates to contain smaller ones.
            var withArea = loops.Select(l => (loop: l, area: PolygonArea(l.Points))).ToList();
            withArea.Sort((a, b) => b.area.CompareTo(a.area));

            var consumed = new bool[withArea.Count];
            for (int i = 0; i < withArea.Count; i++)
            {
                if (consumed[i]) continue;
                var (outerLoop, outerArea) = withArea[i];
                if (outerArea <= 0) continue;
                var entry = new SlabWithVoids { Outer = outerLoop };
                results.Add(entry);
                consumed[i] = true;

                for (int j = i + 1; j < withArea.Count; j++)
                {
                    if (consumed[j]) continue;
                    var (cand, candArea) = withArea[j];
                    if (candArea <= 0 || candArea >= outerArea) continue;
                    var centroid = PolygonCentroid(cand.Points);
                    if (centroid == null) continue;
                    if (PointInPolygon(centroid, outerLoop.Points))
                    {
                        entry.Voids.Add(cand);
                        consumed[j] = true;
                    }
                }
            }
            return results;
        }

        private static double PolygonArea(IList<XYZ> pts)
        {
            if (pts == null || pts.Count < 3) return 0;
            double a = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                var p1 = pts[i];
                var p2 = pts[(i + 1) % pts.Count];
                a += p1.X * p2.Y - p2.X * p1.Y;
            }
            return Math.Abs(a) * 0.5;
        }

        private static XYZ PolygonCentroid(IList<XYZ> pts)
        {
            if (pts == null || pts.Count == 0) return null;
            double sx = 0, sy = 0;
            foreach (var p in pts) { sx += p.X; sy += p.Y; }
            return new XYZ(sx / pts.Count, sy / pts.Count, pts[0].Z);
        }

        private static bool PointInPolygon(XYZ pt, IList<XYZ> poly)
        {
            if (poly == null || poly.Count < 3 || pt == null) return false;
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

    /// <summary>P2-C — Stamp grid-derived marks onto columns whose centre snapped to a
    /// grid intersection during P1-A snapping.</summary>
    internal static class GridLabelMarkBuilder
    {
        /// <summary>Apply grid-derived marks. Caller passes columns already paired with
        /// their snap result. Returns the number of marks written.</summary>
        public static int ApplyMarks(Document doc,
            IList<(ElementId columnId, GridSnapper.SnapResult snap)> mapping,
            HashSet<string> usedMarks)
        {
            if (doc == null || mapping == null || mapping.Count == 0) return 0;
            int n = 0;
            using (var tx = new Transaction(doc, "STING STRUCT: Grid-Label Marks"))
            {
                tx.Start();
                foreach (var (id, snap) in mapping)
                {
                    if (snap == null || !snap.DidSnap) continue;
                    string mark = BuildMark(snap.VerticalGridLabel, snap.HorizontalGridLabel);
                    if (string.IsNullOrEmpty(mark)) continue;

                    // De-duplicate within the run (multiple columns at same intersection)
                    string finalMark = mark;
                    int suffix = 2;
                    while (usedMarks != null && usedMarks.Contains(finalMark))
                        finalMark = $"{mark}.{suffix++}";
                    usedMarks?.Add(finalMark);

                    var el = doc.GetElement(id);
                    var p = el?.LookupParameter("Mark");
                    if (p != null && !p.IsReadOnly)
                    {
                        try { p.Set(finalMark); n++; }
                        catch (Exception ex) { StingLog.Warn($"GridLabelMark set: {ex.Message}"); }
                    }
                }
                tx.Commit();
            }
            return n;
        }

        private static string BuildMark(string vert, string horiz)
        {
            vert = (vert ?? "").Trim();
            horiz = (horiz ?? "").Trim();
            if (vert.Length == 0 && horiz.Length == 0) return "";
            if (vert.Length == 0) return horiz;
            if (horiz.Length == 0) return vert;
            return $"{vert}/{horiz}";
        }
    }

    /// <summary>P2-D — Place TextNote markers in the active view at each load-path
    /// warning location, so engineers see the warnings without scrolling text logs.</summary>
    internal static class StructuralWarningPlacer
    {
        /// <summary>Phase-141: Place warnings AT specific 2D points in the view (one
        /// TextNote per warning). Used for junction warnings where the location is
        /// known (the junction centroid). Returns the number of notes placed.</summary>
        public static int PlaceWarningsAtPoints(Document doc, View view,
            IList<string> warnings, IList<XYZ> points)
        {
            if (doc == null || view == null) return 0;
            if (warnings == null || points == null) return 0;
            if (warnings.Count == 0 || warnings.Count != points.Count) return 0;

            ElementId noteTypeId = ResolveNoteTypeId(doc);
            if (noteTypeId == null || noteTypeId == ElementId.InvalidElementId) return 0;

            int placed = 0;
            using (var tx = new SubTransaction(doc))
            {
                try
                {
                    tx.Start();
                    for (int i = 0; i < warnings.Count; i++)
                    {
                        if (string.IsNullOrWhiteSpace(warnings[i]) || points[i] == null) continue;
                        try
                        {
                            string body = "⚠ STING-STRUCT: " + warnings[i].Trim();
                            var pos = new XYZ(points[i].X, points[i].Y, 0);
                            var note = TextNote.Create(doc, view.Id, pos, body, noteTypeId);
                            if (note != null) placed++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"PlaceWarningAtPoint #{i}: {ex.Message}");
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"PlaceWarningsAtPoints sub-transaction: {ex.Message}");
                    if (tx.HasStarted()) tx.RollBack();
                }
            }
            return placed;
        }

        private static ElementId ResolveNoteTypeId(Document doc)
        {
            ElementId id = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            if (id != null && id != ElementId.InvalidElementId) return id;
            var t = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType))
                .Cast<ElementType>().FirstOrDefault();
            return t?.Id ?? ElementId.InvalidElementId;
        }

        /// <summary>Place one TextNote per warning at the centre of the active view.
        /// Wraps in its own sub-transaction so a failure here doesn't roll back element
        /// creation. Returns the placed TextNote ElementIds.</summary>
        public static List<ElementId> PlaceWarnings(Document doc, View view,
            IList<string> warnings)
        {
            var ids = new List<ElementId>();
            if (doc == null || view == null || warnings == null || warnings.Count == 0)
                return ids;

            // Pick a default text note type — first available.
            ElementId noteTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            if (noteTypeId == null || noteTypeId == ElementId.InvalidElementId)
            {
                var t = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType))
                    .Cast<ElementType>().FirstOrDefault();
                if (t == null) { StingLog.Warn("PlaceWarnings: no TextNoteType found"); return ids; }
                noteTypeId = t.Id;
            }

            // Stagger notes vertically so they don't overlap.
            BoundingBoxXYZ bb = null;
            try { bb = view.get_BoundingBox(null); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            XYZ origin = bb != null
                ? new XYZ(bb.Min.X + (bb.Max.X - bb.Min.X) * 0.05,
                         bb.Max.Y - (bb.Max.Y - bb.Min.Y) * 0.05, 0)
                : new XYZ(0, 0, 0);
            double rowFt = 5.0 * Units.MmToFeet * 100; // 0.5 m row spacing in plan units

            using (var tx = new SubTransaction(doc))
            {
                try
                {
                    tx.Start();
                    int row = 0;
                    foreach (var w in warnings)
                    {
                        if (string.IsNullOrWhiteSpace(w)) continue;
                        try
                        {
                            string body = "⚠ STING-STRUCT: " + w.Trim();
                            var pos = new XYZ(origin.X, origin.Y - row * rowFt, origin.Z);
                            var note = TextNote.Create(doc, view.Id, pos, body, noteTypeId);
                            if (note != null) ids.Add(note.Id);
                            row++;
                        }
                        catch (Exception ex2) { StingLog.Warn($"PlaceWarning row {row}: {ex2.Message}"); }
                    }
                    tx.Commit();
                }
                catch (Exception ex2)
                {
                    StingLog.Warn($"PlaceWarnings sub-transaction: {ex.Message}");
                    if (tx.HasStarted()) tx.RollBack();
                }
            }
            return ids;
        }
    }
}
