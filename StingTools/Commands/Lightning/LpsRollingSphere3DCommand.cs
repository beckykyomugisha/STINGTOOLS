// StingTools — LpsRollingSphere3DCommand.cs
//
// 3D coverage analyser for the LPS module. Implements the rolling sphere
// method protection check per BS EN 62305-3 Annex E.4 against the model's
// roof geometry. For each sample point on each roof, tests whether any
// air terminal's rolling-sphere zone covers it. Unprotected points are
// flagged with red DirectShape markers in a dedicated 3D view.
//
// Geometry: a point P at height zp is single-handedly reachable by air
// terminal AT at height za if horizontal_distance(P, AT) <= sqrt(2R·H - H²)
// where H = za - zp and 0 < H < 2R. Cooperative coverage extends this:
// for every reaching terminal, the apex sphere resting on AT and P from
// above is computed; if any *other* terminal lies inside that sphere, it
// physically blocks the rolling sphere from reaching P. P is exposed iff
// at least one reaching terminal has a clear (unblocked) apex sphere.
//
// Roof sampling walks each roof's solid faces, picks top-facing surfaces
// (face normal Z > 0.5), and emits a uniform UV grid translated to a
// world-space step approximating DEFAULT_GRID_M. Falls back to a bbox
// scan when no top face is extractable (mass-derived / weird roofs).

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Lightning;
using StingTools.Core.Fabrication;
using StingTools.UI;

namespace StingTools.Commands.Lightning
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LpsRollingSphere3DCommand : IExternalCommand, IPanelCommand
    {
        // ── Tuning constants ─────────────────────────────────────────
        private const double DEFAULT_GRID_M    = 2.0;   // sample spacing
        private const double MARKER_HALF_FT    = 0.4;   // ~120 mm half-width
        private const int    MAX_POINTS_PER_RF = 4000;  // safety cap per roof
        private const string VIEW_NAME         = "STING - LPS Coverage 3D";
        private const string MARKER_COMMENT    = "STING_LPS_EXPOSED";

        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            return RunInternal(ctx.App, ctx.Doc);
        }

        public Result Execute(UIApplication app)
        {
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { TaskDialog.Show("STING — LPS 3D Coverage", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            string classId = ParameterHelpers.GetString(doc.ProjectInformation, LpsParams.CLASS_TXT);
            if (string.IsNullOrWhiteSpace(classId))
            {
                TaskDialog.Show("STING — LPS 3D Coverage", "Run LPS Class Setup first to define the project class.");
                return Result.Cancelled;
            }
            var def = LpsEngine.LoadClass(classId);
            if (def == null || def.RollingSphereRadiusM <= 0)
            {
                TaskDialog.Show("STING — LPS 3D Coverage", $"Unknown LPS class '{classId}'.");
                return Result.Cancelled;
            }
            double Rft = UnitUtils.ConvertToInternalUnits(def.RollingSphereRadiusM, UnitTypeId.Meters);

            var terminals = LpsEngine.CollectLpsFamily(doc, "Air Terminal", "Air_Terminal", "Franklin", "Air-Terminal");
            if (terminals.Count == 0)
            {
                TaskDialog.Show("STING — LPS 3D Coverage",
                    "No air terminal families found. Place LPS air terminal families before running coverage analysis.");
                return Result.Cancelled;
            }
            var terminalTops = terminals
                .Select(t => GetTopPoint(t))
                .Where(p => p != null)
                .ToList();
            if (terminalTops.Count == 0)
            {
                TaskDialog.Show("STING — LPS 3D Coverage", "Air terminals have no resolvable top point.");
                return Result.Cancelled;
            }

            var roofs = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Roofs)
                .WhereElementIsNotElementType()
                .ToList();
            if (roofs.Count == 0)
            {
                TaskDialog.Show("STING — LPS 3D Coverage",
                    "No roofs found. The 3D coverage analyser samples roof bounding boxes; place roof elements first.");
                return Result.Cancelled;
            }

            // Sample every roof top — collect (point, isProtected) pairs.
            double gridFt = UnitUtils.ConvertToInternalUnits(DEFAULT_GRID_M, UnitTypeId.Meters);
            var exposed = new List<XYZ>();
            int sampled = 0, protectedCount = 0;
            StingProgressDialog progress = roofs.Count > 5
                ? StingProgressDialog.Show("LPS 3D coverage", roofs.Count) : null;
            foreach (var rf in roofs)
            {
                progress?.Increment(rf.Name ?? "");
                int perRoof = 0;
                foreach (var P in SampleRoofTopFaces(rf, gridFt))
                {
                    if (perRoof >= MAX_POINTS_PER_RF) break;
                    sampled++; perRoof++;
                    if (IsProtected(P, terminalTops, Rft)) protectedCount++;
                    else exposed.Add(P);
                }
            }
            progress?.Close();

            // Render markers
            int markersPlaced = 0;
            string viewName = "";
            try
            {
                using (var t = new Transaction(doc, "STING — LPS 3D Coverage"))
                {
                    t.Start();
                    var view = EnsureCoverageView(doc);
                    viewName = view?.Name ?? VIEW_NAME;
                    ClearPreviousMarkers(doc);
                    foreach (var P in exposed)
                    {
                        if (PlaceMarker(doc, P)) markersPlaced++;
                    }
                    t.Commit();
                    try
                    {
                        var uidoc = app?.ActiveUIDocument;
                        if (uidoc != null && view != null) uidoc.ActiveView = view;
                    }
                    catch (Exception ex) { StingLog.Warn($"Activate 3D view: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("LPS 3D Coverage failed", ex);
                TaskDialog.Show("STING — LPS 3D Coverage", "Render failed: " + ex.Message);
                return Result.Failed;
            }

            double pct = sampled > 0 ? (100.0 * protectedCount / sampled) : 0.0;
            var panel = StingResultPanel.Create("LPS Rolling Sphere — 3D Coverage");
            panel.SetSubtitle($"Class {classId} (R = {def.RollingSphereRadiusM:F0} m) · view '{viewName}'");
            panel.AddSection("SUMMARY")
                 .Metric("Roofs analysed",         roofs.Count.ToString())
                 .Metric("Air terminals",          terminalTops.Count.ToString())
                 .Metric("Sample points",          sampled.ToString("N0"))
                 .MetricHighlight("Protected",     protectedCount.ToString("N0"), $"{pct:F1}% of sampled area")
                 .MetricError("Exposed",           exposed.Count.ToString("N0"), $"{100 - pct:F1}% of sampled area")
                 .Metric("3D markers placed",      markersPlaced.ToString("N0"));
            panel.AddSection("METHOD")
                 .Text("Per BS EN 62305-3 §E.4: a point is single-handedly reachable by an air terminal at height H")
                 .Text("above it when its horizontal distance is ≤ √(2R·H − H²) and 0 < H < 2R. Cooperative coverage:")
                 .Text("for every reaching terminal, compute the apex sphere of radius R touching AT and P from above")
                 .Text("(the unique max-z sphere centred in the AT–P vertical plane). If any other terminal lies inside")
                 .Text("that sphere, it physically blocks the rolling sphere from reaching P. P is exposed iff at least")
                 .Text("one reaching terminal has a clear (unblocked) apex sphere.")
                 .Text($"Grid resolution: {DEFAULT_GRID_M:F1} m. Roof sampling walks each roof's top faces (normal Z>0.5)")
                 .Text("on a UV grid translated to world-space spacing using face area / UV area. Falls back to bbox-top")
                 .Text("when no extractable top face is found.");
            panel.Show();
            return Result.Succeeded;
        }

        // ── Helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Cooperative protection test. P is protected iff every terminal AT
        /// that single-handedly could reach P (sqrt(2RH - H²) check) has
        /// some *other* terminal AT' lying inside the apex rolling sphere
        /// resting on AT and P from above. The apex sphere is the unique
        /// position where the sphere of radius R touches AT and P from above
        /// while sitting in the vertical plane through them. AT' inside that
        /// sphere physically blocks it from reaching P → AT's reach is
        /// occluded. If every reaching terminal is blocked, the sphere has
        /// no path to P and the cooperative geometry shields it.
        /// </summary>
        private static bool IsProtected(XYZ P, IList<XYZ> terminalTops, double Rft)
        {
            // Phase 1 — collect terminals that could single-handedly reach P.
            var reaches = new List<XYZ>();
            foreach (var T in terminalTops)
            {
                double H = T.Z - P.Z;
                if (H <= 0 || H >= 2.0 * Rft) continue;
                double rMax = Math.Sqrt(2.0 * Rft * H - H * H);
                double dx = T.X - P.X, dy = T.Y - P.Y;
                if (Math.Sqrt(dx * dx + dy * dy) <= rMax) reaches.Add(T);
            }
            if (reaches.Count == 0) return true;

            // Phase 2 — for each reaching terminal, check pair occlusion.
            foreach (var T in reaches)
            {
                var apex = ApexSphereCenter(T, P, Rft);
                if (apex == null) continue; // sphere geometrically can't form an apex above both
                bool blocked = false;
                foreach (var Tp in terminalTops)
                {
                    if (ReferenceEquals(Tp, T)) continue;
                    if ((apex - Tp).GetLength() < Rft - 1e-6)
                    {
                        blocked = true; break;
                    }
                }
                if (!blocked) return false; // unblocked path exists → P is exposed
            }
            return true;
        }

        /// <summary>Apex sphere centre: |C-AT|=|C-P|=R with C in the vertical
        /// plane through AT-P, max-z solution. Returns null when no such
        /// sphere can rest on AT and P from above.</summary>
        private static XYZ ApexSphereCenter(XYZ AT, XYZ P, double R)
        {
            var v = P - AT;
            double d = v.GetLength();
            if (d < 1e-9 || d > 2 * R) return null;
            var M = (AT + P) * 0.5;
            double rc = Math.Sqrt(R * R - (d * 0.5) * (d * 0.5));
            var vUnit = v / d;
            // Component of +z perpendicular to v, normalised. If v is purely
            // vertical, no canonical "above" direction in the locus circle.
            var n = XYZ.BasisZ - (XYZ.BasisZ.DotProduct(vUnit)) * vUnit;
            double nLen = n.GetLength();
            if (nLen < 1e-9) return null;
            n = n / nLen;
            var C = M + rc * n;
            if (C.Z <= AT.Z || C.Z <= P.Z) return null;
            return C;
        }

        private static XYZ GetTopPoint(FamilyInstance fi)
        {
            try
            {
                var bb = fi.get_BoundingBox(null);
                if (bb != null)
                {
                    var p = (fi.Location as LocationPoint)?.Point ?? new XYZ(
                        (bb.Min.X + bb.Max.X) / 2.0,
                        (bb.Min.Y + bb.Max.Y) / 2.0,
                        bb.Max.Z);
                    return new XYZ(p.X, p.Y, bb.Max.Z);
                }
                return (fi.Location as LocationPoint)?.Point;
            }
            catch (Exception ex) { StingLog.Warn($"GetTopPoint: {ex.Message}"); return null; }
        }

        private static View3D EnsureCoverageView(Document doc)
        {
            var existing = new FilteredElementCollector(doc).OfClass(typeof(View3D))
                .Cast<View3D>().FirstOrDefault(v => !v.IsTemplate &&
                    string.Equals(v.Name, VIEW_NAME, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;

            var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>().FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);
            if (vft == null) return null;
            var view = View3D.CreateIsometric(doc, vft.Id);
            try { view.Name = VIEW_NAME; } catch (Exception ex) { StingLog.Warn($"View rename: {ex.Message}"); }
            return view;
        }

        private static void ClearPreviousMarkers(Document doc)
        {
            try
            {
                var prior = new FilteredElementCollector(doc)
                    .OfClass(typeof(DirectShape))
                    .Cast<DirectShape>()
                    .Where(d => string.Equals(
                        d.LookupParameter("Comments")?.AsString(), MARKER_COMMENT,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(d => d.Id)
                    .ToList();
                if (prior.Count > 0) doc.Delete(prior);
            }
            catch (Exception ex) { StingLog.Warn($"ClearPreviousMarkers: {ex.Message}"); }
        }

        private static bool PlaceMarker(Document doc, XYZ centre)
        {
            try
            {
                var p0 = new XYZ(centre.X - MARKER_HALF_FT, centre.Y - MARKER_HALF_FT, centre.Z);
                var p1 = new XYZ(centre.X + MARKER_HALF_FT, centre.Y - MARKER_HALF_FT, centre.Z);
                var p2 = new XYZ(centre.X + MARKER_HALF_FT, centre.Y + MARKER_HALF_FT, centre.Z);
                var p3 = new XYZ(centre.X - MARKER_HALF_FT, centre.Y + MARKER_HALF_FT, centre.Z);
                var loop = new CurveLoop();
                loop.Append(Line.CreateBound(p0, p1));
                loop.Append(Line.CreateBound(p1, p2));
                loop.Append(Line.CreateBound(p2, p3));
                loop.Append(Line.CreateBound(p3, p0));
                var solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                    new List<CurveLoop> { loop }, XYZ.BasisZ, 2.0 * MARKER_HALF_FT);
                if (solid == null) return false;

                var ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                ds.SetShape(new List<GeometryObject> { solid });
                ds.Name = "STING_LPS_EXPOSED";
                var c = ds.LookupParameter("Comments");
                if (c != null && !c.IsReadOnly && c.StorageType == StorageType.String)
                    c.Set(MARKER_COMMENT);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlaceMarker: {ex.Message}");
                return false;
            }
        }

        /// <summary>Walks roof solids, picks faces whose normal at face-centre
        /// has Z &gt; 0.5 (top-facing), and yields a regular grid of XYZ points
        /// across each face's UV bounding box. UV step is sized so the
        /// average world-space spacing approximates <paramref name="gridFt"/>
        /// using the face's area / UV-area ratio.
        ///
        /// Falls back to a bbox-top scan when the roof has no extractable
        /// top face (rare, e.g. mass-derived roofs).</summary>
        private static IEnumerable<XYZ> SampleRoofTopFaces(Element roof, double gridFt)
        {
            var opts = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false };
            GeometryElement geom;
            try { geom = roof.get_Geometry(opts); }
            catch (Exception ex) { StingLog.Warn($"SampleRoofTopFaces geom: {ex.Message}"); geom = null; }

            int yielded = 0;
            if (geom != null)
            {
                foreach (var go in geom)
                {
                    foreach (var solid in EnumerateSolids(go))
                    {
                        if (solid?.Faces == null) continue;
                        foreach (Face face in solid.Faces)
                        {
                            BoundingBoxUV bb;
                            try { bb = face.GetBoundingBox(); }
                            catch (Exception ex) { StingLog.Warn($"face bbox: {ex.Message}"); continue; }
                            if (bb == null) continue;

                            UV centerUV = new UV(0.5 * (bb.Min.U + bb.Max.U), 0.5 * (bb.Min.V + bb.Max.V));
                            XYZ n;
                            try { n = face.ComputeNormal(centerUV); }
                            catch (Exception ex) { StingLog.Warn($"face normal: {ex.Message}"); continue; }
                            if (n.Z < 0.5) continue;

                            double area;
                            try { area = face.Area; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); area = 0; }
                            double uvArea = (bb.Max.U - bb.Min.U) * (bb.Max.V - bb.Min.V);
                            if (area < 1e-6 || uvArea < 1e-9) continue;
                            double scale = Math.Sqrt(area / uvArea); // world-units per uv-unit
                            double uStep = gridFt / scale;
                            double vStep = gridFt / scale;
                            if (uStep < 1e-9 || vStep < 1e-9) continue;

                            for (double u = bb.Min.U; u <= bb.Max.U; u += uStep)
                            {
                                for (double v = bb.Min.V; v <= bb.Max.V; v += vStep)
                                {
                                    var uv = new UV(u, v);
                                    XYZ p;
                                    try { p = face.Evaluate(uv); }
                                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); continue; }
                                    if (p == null) continue;
                                    yielded++;
                                    yield return p;
                                }
                            }
                        }
                    }
                }
            }

            // Fallback: bbox-top grid when no top faces sampled.
            if (yielded == 0)
            {
                var bb = roof.get_BoundingBox(null);
                if (bb == null) yield break;
                double topZ = bb.Max.Z;
                for (double x = bb.Min.X; x <= bb.Max.X; x += gridFt)
                    for (double y = bb.Min.Y; y <= bb.Max.Y; y += gridFt)
                        yield return new XYZ(x, y, topZ);
            }
        }

        private static IEnumerable<Solid> EnumerateSolids(GeometryObject go)
        {
            if (go is Solid s) { if (s.Volume > 1e-6) yield return s; yield break; }
            if (go is GeometryInstance gi)
            {
                GeometryElement child = null;
                try { child = gi.GetInstanceGeometry(); }
                catch (Exception ex) { StingLog.Warn($"GetInstanceGeometry: {ex.Message}"); }
                if (child == null) yield break;
                foreach (var sub in child)
                    foreach (var s2 in EnumerateSolids(sub))
                        yield return s2;
            }
        }
    }
}
