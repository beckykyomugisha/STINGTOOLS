// StingTools v4 MVP — base class for auto-drop engines.
//
// Shared behaviour for AutoConduitDrop / AutoPipeDrop / AutoDuctDrop:
//   - Input: an IList<Element> of terminal fixtures (or their
//     ElementIds resolved through the Document).
//   - Output: DropResult with per-element creation status.
//   - Helpers: FindNearestContainment(origin, category, maxSearchMm)
//     scans the active view for a MEPCurve of the target category
//     whose centreline passes closest to the origin, CreateVerticalDrop
//     emits a plumb stub from the fixture connector up to the host
//     containment intercept point.
//
// Per-element failures are caught and surfaced as warnings — a single
// broken fixture never aborts the batch.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Routing
{
    public class DropResult
    {
        public List<ElementId> CreatedIds { get; } = new List<ElementId>();
        public int SkippedCount { get; set; }
        public int FailedCount  { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public string Discipline { get; set; } = "";
    }

    /// <summary>
    /// Base class for the three drop engines. Lifetime: one instance
    /// per command invocation; not thread-safe.
    /// </summary>
    public abstract class DropEngineBase
    {
        protected const double MmToFt = 1.0 / 304.8;

        protected Document Doc { get; }

        protected DropEngineBase(Document doc)
        {
            Doc = doc;
        }

        /// <summary>
        /// Find the MEPCurve of the given category whose centreline
        /// passes closest (in 2D XY) to the origin and is within
        /// maxSearchMm. Returns null if nothing is found.
        /// </summary>
        protected Element FindNearestContainment(
            XYZ origin,
            BuiltInCategory cat,
            double maxSearchMm)
        {
            if (origin == null) return null;
            double maxFt = maxSearchMm * MmToFt;
            double best = double.MaxValue;
            Element winner = null;

            try
            {
                var collector = new FilteredElementCollector(Doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType();
                foreach (var el in collector)
                {
                    var loc = el?.Location as LocationCurve;
                    if (loc == null) continue;
                    var curve = loc.Curve;
                    if (curve == null) continue;

                    // project origin onto curve and measure distance
                    IntersectionResult proj = null;
                    try { proj = curve.Project(origin); }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"DropEngineBase: Curve.Project failed on {el.Id}: {ex.Message}");
                        continue;
                    }
                    if (proj == null) continue;

                    double d = proj.XYZPoint.DistanceTo(origin);
                    if (d < best && d <= maxFt)
                    {
                        best = d;
                        winner = el;
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DropEngineBase: FindNearestContainment failed: {ex.Message}");
            }
            return winner;
        }

        /// <summary>
        /// Emit a vertical line segment from "from" to a point directly
        /// above (or below) at the same XY, with the target Z equal to
        /// the projection of "from" onto the host containment curve.
        /// Subclasses override CreateRunBetween to spawn the actual
        /// MEPCurve (conduit / pipe / duct).
        /// </summary>
        protected XYZ ComputeInterceptPoint(XYZ from, Element host)
        {
            if (from == null || host == null) return from;
            var curve = (host.Location as LocationCurve)?.Curve;
            if (curve == null) return from;
            try
            {
                var proj = curve.Project(from);
                if (proj != null && proj.XYZPoint != null) return proj.XYZPoint;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DropEngineBase: intercept compute failed: {ex.Message}");
            }
            return from;
        }

        /// <summary>
        /// Subclasses implement the actual MEPCurve creation.
        /// Must run inside an active Transaction.
        /// </summary>
        protected abstract ElementId CreateRunBetween(XYZ from, XYZ to, Element host, DropResult result);

        /// <summary>
        /// Shared per-element driver: find host containment, compute
        /// intercept, create run, tag the result with fabrication
        /// metadata. Returns true if a run was created.
        /// </summary>
        protected bool TryDropFromFixture(Element fixtureEl, BuiltInCategory containmentCat,
            double maxSearchMm, DropResult result)
        {
            if (fixtureEl?.Location is LocationPoint lp && lp.Point != null)
            {
                var origin = lp.Point;
                Element host = FindNearestContainment(origin, containmentCat, maxSearchMm);
                if (host == null)
                {
                    result.Warnings.Add($"No {containmentCat} found within {maxSearchMm}mm of fixture {fixtureEl.Id}");
                    result.SkippedCount++;
                    return false;
                }
                XYZ to = ComputeInterceptPoint(origin, host);
                var id = CreateRunBetween(origin, to, host, result);
                if (id != null && id != ElementId.InvalidElementId)
                {
                    result.CreatedIds.Add(id);
                    return true;
                }
                result.FailedCount++;
                return false;
            }
            result.Warnings.Add($"Fixture {fixtureEl?.Id} has no LocationPoint; cannot drop");
            result.SkippedCount++;
            return false;
        }

        protected void TrySetString(Element el, string paramName, string value)
        {
            if (el == null || string.IsNullOrEmpty(paramName) || value == null) return;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    p.Set(value);
            }
            catch (Exception ex) { StingLog.Warn($"DropEngineBase: set {paramName} failed: {ex.Message}"); }
        }
    }
}
