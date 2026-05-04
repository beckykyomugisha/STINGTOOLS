// StingTools — LpsRollingSphere3DCommand.cs
//
// 3D coverage analyser for the LPS module. Implements the rolling sphere
// method protection check per BS EN 62305-3 Annex E.4 against the model's
// roof geometry. For each sample point on each roof, tests whether any
// air terminal's rolling-sphere zone covers it. Unprotected points are
// flagged with red DirectShape markers in a dedicated 3D view.
//
// Geometry: a point P at height zp is protected by air terminal AT at
// height za if horizontal_distance(P, AT) <= sqrt(2R·H - H²) where
// H = za - zp and 0 < H < 2R. Outside that band the terminal cannot
// protect P (it is below P, equal in height, or so high above that the
// rolling sphere can fit between the terminal and the point's plane).

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
                var bb = rf.get_BoundingBox(null);
                if (bb == null) continue;
                double topZ = bb.Max.Z;
                int perRoof = 0;
                for (double x = bb.Min.X; x <= bb.Max.X && perRoof < MAX_POINTS_PER_RF; x += gridFt)
                {
                    for (double y = bb.Min.Y; y <= bb.Max.Y && perRoof < MAX_POINTS_PER_RF; y += gridFt)
                    {
                        sampled++; perRoof++;
                        var P = new XYZ(x, y, topZ);
                        if (IsProtected(P, terminalTops, Rft)) protectedCount++;
                        else exposed.Add(P);
                    }
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
                 .Text("Per BS EN 62305-3 §E.4: a point is protected by an air terminal at height H above it when its")
                 .Text("horizontal distance is ≤ √(2R·H − H²). Below height-bands outside (0, 2R) the terminal cannot")
                 .Text("provide single-terminal protection. Multi-terminal cooperative coverage is approximated as the")
                 .Text("union of single-terminal zones (conservative — actual protection may be wider).")
                 .Text($"Grid resolution: {DEFAULT_GRID_M:F1} m. Roof sampling uses bounding-box top z (sloped roofs may")
                 .Text("over-report exposure on lower slopes).");
            panel.Show();
            return Result.Succeeded;
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static bool IsProtected(XYZ P, IList<XYZ> terminalTops, double Rft)
        {
            double zp = P.Z;
            foreach (var T in terminalTops)
            {
                double H = T.Z - zp;
                if (H <= 0) continue;          // terminal not above point
                if (H >= 2.0 * Rft) continue;  // sphere can fit between
                double rMax = Math.Sqrt(2.0 * Rft * H - H * H);
                double dx = T.X - P.X, dy = T.Y - P.Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d <= rMax) return true;
            }
            return false;
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
    }
}
