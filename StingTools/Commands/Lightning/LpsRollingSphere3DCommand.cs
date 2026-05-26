// StingTools — LpsRollingSphere3DCommand.cs
//
// 3D coverage analyser for the LPS module. Implements the rolling sphere
// method protection check per BS EN 62305-3 Annex E.4 against the model's
// roof geometry. For each sample point on each roof, tests whether any
// air terminal's rolling-sphere protected zone covers it. Unprotected
// points are flagged with red DirectShape markers in a dedicated 3D view.
//
// Geometry: a roof point P is PROTECTED by an air terminal AT when it lies
// inside AT's down-hanging rolling sphere — the sphere of radius R whose
// highest point sits at AT's tip. With H = za - zp (0 < H < 2R), the
// protected horizontal radius at depth H is sqrt(2R·H - H²); a point within
// that radius of AT cannot be touched by the rolling sphere resting on AT,
// so it is shielded. P is PROTECTED if any terminal covers it (union of
// single-terminal protected zones) and EXPOSED otherwise.
//
// This is an air-terminal-only model and conservative by design: it credits
// protection only from air-termination rods — NOT from roof-level mesh
// conductors, parapets, taller adjacent roof sections, or the ground plane.
// Gaps between sparse or low rods will read as exposed even where roof-level
// mesh would protect them. Treat the result as indicative coverage, not a
// certifiable BS EN 62305-3 check.
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
                 .Text("Per BS EN 62305-3 §E.4: a roof point P is PROTECTED by an air terminal at height H above it")
                 .Text("when its horizontal distance is ≤ √(2R·H − H²) with 0 < H < 2R — i.e. P lies inside the")
                 .Text("terminal's down-hanging rolling sphere. P is protected if any terminal covers it, exposed otherwise.")
                 .Text("Air-terminal-only model: protection is credited from rods only — NOT from roof-level mesh")
                 .Text("conductors, parapets, taller adjacent roofs, or the ground. Gaps between sparse / low rods read")
                 .Text("as exposed even where roof mesh would protect them. Conservative — indicative coverage, not a")
                 .Text("certifiable check.")
                 .Text($"Grid resolution: {DEFAULT_GRID_M:F1} m. Roof sampling walks each roof's top faces (normal Z>0.5)")
                 .Text("on a UV grid translated to world-space spacing using face area / UV area. Falls back to bbox-top")
                 .Text("when no extractable top face is found.");
            panel.Show();
            return Result.Succeeded;
        }

        // ── Helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Rolling-sphere protection test (BS EN 62305-3 §E.4), air-terminal-only
        /// model. P is PROTECTED when it lies inside the down-hanging rolling
        /// sphere of at least one air terminal: with H = Tz − Pz and 0 &lt; H &lt; 2R,
        /// the protected horizontal radius at depth H is √(2R·H − H²). A point
        /// within that radius of any terminal cannot be touched by the rolling
        /// sphere resting on that terminal, so it is shielded. P is EXPOSED when
        /// no terminal covers it.
        ///
        /// This is the union of single-terminal protected zones — conservative
        /// by design: it credits protection only from air-termination rods, not
        /// from roof-level mesh conductors, parapets, taller adjacent roof
        /// sections, or the ground plane. Gaps between sparse / low rods read as
        /// exposed even where roof-level mesh would protect them. Indicative
        /// coverage, not a certifiable check.
        /// </summary>
        private static bool IsProtected(XYZ P, IList<XYZ> terminalTops, double Rft)
        {
            foreach (var T in terminalTops)
            {
                double H = T.Z - P.Z;          // terminal tip above the point
                if (H <= 0 || H >= 2.0 * Rft) continue;
                double rMax = Math.Sqrt(2.0 * Rft * H - H * H);
                double dx = T.X - P.X, dy = T.Y - P.Y;
                if (Math.Sqrt(dx * dx + dy * dy) <= rMax) return true; // inside a protected zone
            }
            return false; // no terminal shields P → exposed
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
                            catch (Exception ex2) { StingLog.Warn($"face bbox: {ex2.Message}"); continue; }
                            if (bb == null) continue;

                            UV centerUV = new UV(0.5 * (bb.Min.U + bb.Max.U), 0.5 * (bb.Min.V + bb.Max.V));
                            XYZ n;
                            try { n = face.ComputeNormal(centerUV); }
                            catch (Exception ex3) { StingLog.Warn($"face normal: {ex3.Message}"); continue; }
                            if (n.Z < 0.5) continue;

                            double area;
                            try { area = face.Area; } catch (Exception ex4) { StingLog.Warn($"Suppressed: {ex4.Message}"); area = 0; }
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
                                    catch (Exception ex5) { StingLog.Warn($"Suppressed: {ex5.Message}"); continue; }
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
