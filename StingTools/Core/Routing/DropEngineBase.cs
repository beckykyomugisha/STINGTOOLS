// StingTools v4 MVP — base class for auto-drop engines.
//
// Shared behaviour for AutoConduitDrop / AutoPipeDrop / AutoDuctDrop:
//   - Input: an IList<Element> of terminal fixtures.
//   - Output: DropResult with per-element creation status + wire-up
//     counts (connectors connected, takeoffs inserted).
//   - Helpers: FindNearestContainment, ComputeInterceptPoint,
//     FindBestFreeConnector, GetConnectorNearestTo, TryConnect,
//     TryCreateTakeoff.
//
// Phase A (v4 Phase A): after CreateRunBetween returns the new
// MEPCurve ElementId, the base class now wires the fixture connector
// to the drop's near end (Connector.ConnectTo) and inserts a takeoff
// fitting on the host main curve at the drop's far end
// (Document.Create.NewTakeoffFitting). This turns the drop from raw
// geometry into a fully connected MEP system member that propagates
// system ownership, pressure-drop calculations, and schedules.
//
// Per-element failures are caught and surfaced as warnings — a single
// broken fixture never aborts the batch. Connection failures are
// tolerated: the drop is kept even if ConnectTo / NewTakeoffFitting
// throw, because partial connection is strictly better than no drop.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.Routing
{
    public class DropResult
    {
        public List<ElementId> CreatedIds { get; } = new List<ElementId>();
        public int SkippedCount { get; set; }
        public int FailedCount  { get; set; }
        public int ConnectedCount { get; set; }
        public int TakeoffCount  { get; set; }
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
        protected const double ConnectorProximityFt = 0.01; // ~3mm tolerance

        protected Document Doc { get; }

        /// <summary>
        /// MEP domain the drop engine is routing: used to filter fixture
        /// connectors to the correct system (piping / HVAC / electrical /
        /// cable-tray). Override in subclass constructors.
        /// </summary>
        protected Domain ConnectorDomain { get; set; } = Domain.DomainUndefined;

        /// <summary>
        /// Service id used for RoutingRules lookups (BS EN 50174-2
        /// separation + service corridor band enforcement). Values
        /// match the keys in STING_SEPARATION_RULES.json and
        /// STING_SERVICE_CORRIDORS.json: e.g. "ELC_PWR", "PLM_CWS",
        /// "HVC_SA". Empty string disables rule enforcement on this
        /// engine.
        /// </summary>
        public string ServiceId { get; set; } = "";

        /// <summary>
        /// When true, the drop engine snaps the intercept Z to the
        /// centre of the corridor band claimed by ServiceId (within
        /// ±200 mm) so drops stack in their documented stratum.
        /// Defaults false — enable from the UI "Snap to service zone"
        /// checkbox on the Routing tab.
        /// </summary>
        public bool SnapToCorridorBand { get; set; } = false;

        /// <summary>
        /// When true, runs SeparationChecker after intercept is chosen
        /// and logs any BS EN 50174-2 violations to DropResult.Warnings.
        /// </summary>
        public bool EnforceSeparation { get; set; } = true;

        /// <summary>
        /// Phase 139.29 — when true, every drop's segment is checked
        /// against StructuralAwareness for beam-junction / column
        /// proximity (100 mm clearance). Default true; set false to skip
        /// when the structural model isn't loaded into the host project.
        /// </summary>
        public bool CheckSoffitClash { get; set; } = true;

        /// <summary>
        /// When true, the engine routes the drop via the 3D voxel A*
        /// pathfinder (RoutingPathfinder.FindPath) instead of a single
        /// plumb-line intercept. Yields a 6-connected polyline that
        /// dodges walls / floors / structural members. Slightly slower
        /// (~50–200 ms per drop on typical office floor plates) but
        /// produces buildable geometry instead of straight-line clashes.
        /// Off by default — opt in per discipline / per engine to keep
        /// behaviour stable for projects already happy with L/Z drops.
        /// </summary>
        public bool UsePathfinder { get; set; } = false;

        /// <summary>
        /// Hard cap on A* node expansions per drop. Prevents the
        /// solver from spinning when start and goal are separated by a
        /// huge obstacle field. Defaults to 200,000 (≈250 ms on typical
        /// hardware). Drops exceeding the cap fall back to plumb-line.
        /// </summary>
        public int MaxAStarExpansions { get; set; } = 200_000;

        /// <summary>
        /// Optional deterministic seed for the ACO refiner / 3-opt
        /// smoothers when they ride on top of A*. Default 1234 (the
        /// same constant the v4 MVP used). Set to a per-project value
        /// to get repeatable routes across re-runs.
        /// </summary>
        public int RoutingSeed { get; set; } = 1234;

        // Lazy-built StructuralAwareness for soffit clash. Per-engine
        // instance — DropEngineBase isn't shared across runs.
        private StingTools.Core.Placement.StructuralAwareness _soffitAwareness;

        protected DropEngineBase(Document doc)
        {
            Doc = doc;
        }

        // ---- containment search -------------------------------------------------

        /// <summary>
        /// When true, FindNearestContainment scores tray-network
        /// connectivity in addition to raw XY distance. A tray that is
        /// part of an existing connected run scores better than an
        /// equally-close isolated stub, because dropping onto the
        /// connected run actually puts cables on infrastructure that
        /// goes somewhere — the stub leaves them stranded. The score
        /// halves the effective distance per connected neighbour
        /// (capped at 4 neighbours) so a connected tray always beats
        /// an isolated one within the same search radius.
        /// </summary>
        public bool PreferConnectedTrays { get; set; } = true;

        /// <summary>
        /// Find the MEPCurve of the given category whose centreline
        /// passes closest (in 2D XY) to the origin and is within
        /// maxSearchMm. When PreferConnectedTrays is on (default), the
        /// score is biased toward trays that participate in an existing
        /// connector network rather than isolated stubs. Returns null
        /// if nothing is found.
        /// </summary>
        protected Element FindNearestContainment(
            XYZ origin,
            BuiltInCategory cat,
            double maxSearchMm)
        {
            if (origin == null) return null;
            double maxFt = maxSearchMm * MmToFt;
            double bestScore = double.MaxValue;
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

                    IntersectionResult proj = null;
                    try { proj = curve.Project(origin); }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"DropEngineBase: Curve.Project failed on {el.Id}: {ex.Message}");
                        continue;
                    }
                    if (proj == null) continue;

                    double d = proj.XYZPoint.DistanceTo(origin);
                    if (d > maxFt) continue;

                    double score = d;
                    if (PreferConnectedTrays)
                    {
                        int neighbours = CountConnectedNeighbours(el);
                        // Cap at 4 — diminishing returns past that.
                        if (neighbours > 4) neighbours = 4;
                        // Each neighbour cuts the effective distance
                        // by 12.5% (½^¼). A 4-neighbour tray at full
                        // search radius scores like a half-radius
                        // isolated stub.
                        score *= Math.Pow(0.875, neighbours);
                    }
                    if (score < bestScore)
                    {
                        bestScore = score;
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
        /// Count direct connector neighbours of an MEP curve. Used by
        /// FindNearestContainment to bias toward already-connected
        /// trays. Cheap O(connectors) probe — no graph walk.
        /// </summary>
        private static int CountConnectedNeighbours(Element el)
        {
            int n = 0;
            try
            {
                var mgr = (el as MEPCurve)?.ConnectorManager;
                if (mgr == null) return 0;
                foreach (Connector c in mgr.Connectors)
                {
                    if (c == null) continue;
                    foreach (Connector other in c.AllRefs)
                    {
                        if (other?.Owner == null) continue;
                        if (other.Owner.Id == el.Id) continue;
                        n++;
                    }
                }
            }
            catch { }
            return n;
        }

        /// <summary>
        /// Emit a vertical line segment from "from" to a point directly
        /// above (or below) at the same XY, with the target Z equal to
        /// the projection of "from" onto the host containment curve.
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

        // ---- connector helpers --------------------------------------------------

        /// <summary>
        /// Enumerate every connector on the element's ConnectorManager
        /// (walks FamilyInstance.MEPModel and MEPCurve.ConnectorManager).
        /// Returns an empty list when the element has no MEPModel.
        /// </summary>
        protected static IEnumerable<Connector> GetAllConnectors(Element el)
        {
            if (el == null) yield break;
            ConnectorManager cm = null;
            try
            {
                if (el is FamilyInstance fi) cm = fi.MEPModel?.ConnectorManager;
                else if (el is MEPCurve mc)  cm = mc.ConnectorManager;
                else
                {
                    var prop = el.GetType().GetProperty("ConnectorManager");
                    if (prop != null) cm = prop.GetValue(el) as ConnectorManager;
                }
            }
            catch { cm = null; }
            if (cm == null) yield break;

            ConnectorSet set;
            try { set = cm.Connectors; } catch { yield break; }
            if (set == null) yield break;

            foreach (Connector c in set)
            {
                if (c != null) yield return c;
            }
        }

        /// <summary>
        /// Find the first free (unconnected) connector on the element
        /// matching the engine's ConnectorDomain. Falls back to the
        /// first free connector of any domain when domain-specific
        /// filtering yields nothing. Returns null when the element has
        /// no free connectors at all.
        /// </summary>
        protected Connector FindBestFreeConnector(Element el)
        {
            Connector anyFree = null;
            foreach (var c in GetAllConnectors(el))
            {
                bool isConnected;
                try { isConnected = c.IsConnected; } catch { continue; }
                if (isConnected) continue;
                if (anyFree == null) anyFree = c;
                if (ConnectorDomain == Domain.DomainUndefined) return c;
                Domain d;
                try { d = c.Domain; } catch { d = Domain.DomainUndefined; }
                if (d == ConnectorDomain) return c;
            }
            return anyFree;
        }

        /// <summary>
        /// Return the connector on the given MEPCurve whose origin is
        /// closest to the reference point. Null when the curve has none.
        /// </summary>
        protected static Connector GetConnectorNearestTo(MEPCurve curve, XYZ reference)
        {
            if (curve == null || reference == null) return null;
            Connector best = null;
            double bestDist = double.MaxValue;
            foreach (var c in GetAllConnectors(curve))
            {
                try
                {
                    double d = c.Origin.DistanceTo(reference);
                    if (d < bestDist) { bestDist = d; best = c; }
                }
                catch { /* connector has no origin — skip */ }
            }
            return best;
        }

        /// <summary>
        /// Attempt Connector.ConnectTo with full exception trapping.
        /// Returns true when the call succeeded (the connectors now
        /// report IsConnected == true).
        /// </summary>
        protected bool TryConnect(Connector a, Connector b, DropResult result)
        {
            if (a == null || b == null) return false;
            try
            {
                // ConnectTo refuses when the domains don't match or the
                // connectors aren't geometrically compatible. Revit's
                // RoutingPreferenceManager may automatically insert an
                // elbow / transition / union to close the gap — this is
                // the documented "design-intent" connection path.
                a.ConnectTo(b);
                bool ok = false;
                try { ok = a.IsConnected && b.IsConnected; } catch { ok = false; }
                if (ok) result.ConnectedCount++;
                return ok;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Connector.ConnectTo failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Attempt to create a takeoff fitting on the host main curve at
        /// the drop's far-end connector. NewTakeoffFitting respects the
        /// host PipeType/DuctType RoutingPreferenceManager rules.
        /// Conduit / cable-tray have no takeoff fitting API — subclasses
        /// should skip this call for the electrical domain.
        /// </summary>
        protected ElementId TryCreateTakeoff(Connector dropConnector, MEPCurve hostCurve, DropResult result)
        {
            if (dropConnector == null || hostCurve == null) return ElementId.InvalidElementId;
            try
            {
                var fitting = Doc.Create.NewTakeoffFitting(dropConnector, hostCurve);
                if (fitting != null)
                {
                    result.TakeoffCount++;
                    return fitting.Id;
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"NewTakeoffFitting failed: {ex.Message}");
            }
            return ElementId.InvalidElementId;
        }

        /// <summary>
        /// Subclasses implement the actual MEPCurve creation.
        /// Must run inside an active Transaction.
        /// </summary>
        protected abstract ElementId CreateRunBetween(XYZ from, XYZ to, Element host, DropResult result);

        /// <summary>
        /// Subclass hook: return true when a takeoff fitting is
        /// supported for this discipline. Defaults true (piping / HVAC).
        /// AutoConduitDrop overrides to false — cable-tray-to-conduit
        /// transitions use Connector.ConnectTo instead.
        /// </summary>
        protected virtual bool SupportsTakeoff => true;

        /// <summary>
        /// Shared per-element driver. Four stages:
        ///   1. Locate fixture connector (domain-filtered, free).
        ///   2. Find host containment; compute plumb-line intercept.
        ///   3. Subclass creates the MEP run via CreateRunBetween.
        ///   4. Base class wires: fixture↔drop-near with ConnectTo,
        ///      drop-far↔host with NewTakeoffFitting.
        /// </summary>
        protected bool TryDropFromFixture(Element fixtureEl, BuiltInCategory containmentCat,
            double maxSearchMm, DropResult result)
        {
            if (fixtureEl == null)
            {
                result.SkippedCount++;
                return false;
            }

            // Stage 1: pick the best origin point. Prefer the fixture's
            // free connector origin over the LocationPoint — connectors
            // are authored at the correct service terminal, LocationPoint
            // is just the family insertion point.
            Connector fxConn = FindBestFreeConnector(fixtureEl);
            XYZ origin = null;
            if (fxConn != null)
            {
                try { origin = fxConn.Origin; } catch { origin = null; }
            }
            if (origin == null && fixtureEl.Location is LocationPoint lp && lp.Point != null)
                origin = lp.Point;
            if (origin == null)
            {
                result.Warnings.Add($"Fixture {fixtureEl.Id} has no connector or LocationPoint; cannot drop");
                result.SkippedCount++;
                return false;
            }

            // Stage 2: find host and intercept.
            Element host = FindNearestContainment(origin, containmentCat, maxSearchMm);
            if (host == null)
            {
                result.Warnings.Add($"No {containmentCat} found within {maxSearchMm}mm of fixture {fixtureEl.Id}");
                result.SkippedCount++;
                return false;
            }
            XYZ to = ComputeInterceptPoint(origin, host);

            // Stage 2.5: corridor-band snap + separation check.
            to = MaybeSnapToCorridorBand(origin, to, result);
            if (EnforceSeparation && !string.IsNullOrEmpty(ServiceId))
            {
                try
                {
                    var violations = SeparationChecker.Check(Doc, origin, to, ServiceId);
                    foreach (var v in violations)
                        result.Warnings.Add($"Separation: {v}");
                }
                catch (Exception ex)
                { result.Warnings.Add($"SeparationChecker failed: {ex.Message}"); }
            }

            // Phase 139.29 — soffit beam / joist clash check.
            // Suspended drops sit under structural soffits; if a beam
            // sits between origin and to, the drop must dodge it.
            // StructuralAwareness was wired for chase routing; here we
            // hand it the drop segment and warn when it passes within
            // 100 mm of a beam-junction or column.
            if (CheckSoffitClash)
            {
                try
                {
                    if (_soffitAwareness == null) _soffitAwareness = new StingTools.Core.Placement.StructuralAwareness(Doc);
                    const double clearFt = 100.0 / 304.8;
                    if (!_soffitAwareness.SegmentIsRoutable(null, origin, to, clearFt))
                    {
                        result.Warnings.Add(
                            $"Drop {fixtureEl.Id} → host {host?.Id?.Value}: passes within 100 mm of " +
                            "structural beam/junction. Dropped anyway — verify offset or tighten the " +
                            "drop path before issue (BIM coordinator review).");
                    }
                }
                catch (Exception sex) { result.Warnings.Add($"Soffit clash check: {sex.Message}"); }
            }

            // Stage 3: build the geometry.
            //
            // Default behaviour (UsePathfinder=false) — straight plumb-
            // line drop: one CreateRunBetween call from origin to the
            // host intercept. Identical to the v4 MVP behaviour.
            //
            // Opt-in behaviour (UsePathfinder=true) — query the voxel
            // A* pathfinder and create one MEPCurve per polyline edge.
            // Pathfinder failures fall back gracefully to the straight
            // path so a degenerate obstacle field never blocks the drop.
            var pathPoints = ResolveRouteToHost(origin, to, host, result);
            var createdIds = new List<ElementId>();
            for (int i = 0; i < pathPoints.Count - 1; i++)
            {
                XYZ a = pathPoints[i];
                XYZ b = pathPoints[i + 1];
                // Final segment carries the real host so subclasses can
                // resolve the level / takeoff. Intermediate segments
                // pass null host — geometry is independent of host
                // semantics, the segment just bridges between A* nodes.
                Element segHost = (i == pathPoints.Count - 2) ? host : null;
                ElementId segId = CreateRunBetween(a, b, segHost, result);
                if (segId == null || segId == ElementId.InvalidElementId)
                {
                    // Mid-path failure on a multi-segment route is fatal —
                    // an unconnected segment is worse than a single
                    // straight drop. Roll back the segments we just
                    // created and fall through to the plumb-line path.
                    foreach (var bad in createdIds)
                    {
                        try { Doc.Delete(bad); }
                        catch (Exception ex) { StingLog.Warn($"Roll back {bad}: {ex.Message}"); }
                    }
                    createdIds.Clear();
                    if (pathPoints.Count > 2)
                    {
                        result.Warnings.Add($"Pathfinder segment {i + 1}/{pathPoints.Count - 1} failed; falling back to plumb-line drop.");
                        var fallback = CreateRunBetween(origin, to, host, result);
                        if (fallback != null && fallback != ElementId.InvalidElementId)
                            createdIds.Add(fallback);
                    }
                    break;
                }
                createdIds.Add(segId);
            }

            if (createdIds.Count == 0)
            {
                result.FailedCount++;
                return false;
            }
            result.CreatedIds.AddRange(createdIds);

            // Stage 4: wire up the run to both ends. With multi-segment
            // routes, also stitch internal joins so each polyline corner
            // becomes a fitting (the type's RoutingPreferenceManager
            // picks the elbow / tee). Best-effort — connector mismatches
            // or geometry precision issues never destroy the run.
            var firstCurve = Doc.GetElement(createdIds[0])             as MEPCurve;
            var lastCurve  = Doc.GetElement(createdIds[createdIds.Count - 1]) as MEPCurve;
            if (firstCurve != null)
            {
                var nearConn = GetConnectorNearestTo(firstCurve, origin);
                if (fxConn != null && nearConn != null)
                    TryConnect(fxConn, nearConn, result);
            }

            // Internal joins: connect the far connector of segment i to
            // the near connector of segment i+1 at the shared waypoint.
            for (int i = 0; i + 1 < createdIds.Count; i++)
            {
                var c1 = Doc.GetElement(createdIds[i])     as MEPCurve;
                var c2 = Doc.GetElement(createdIds[i + 1]) as MEPCurve;
                if (c1 == null || c2 == null) continue;
                XYZ joint = pathPoints[i + 1];
                var f1 = GetConnectorNearestTo(c1, joint);
                var n2 = GetConnectorNearestTo(c2, joint);
                if (f1 != null && n2 != null) TryConnect(f1, n2, result);
            }

            if (lastCurve != null)
            {
                var farConn = GetConnectorNearestTo(lastCurve, to);
                if (SupportsTakeoff && host is MEPCurve hostCurve && farConn != null)
                {
                    TryCreateTakeoff(farConn, hostCurve, result);
                }
                else if (host is MEPCurve hostCurveAlt && farConn != null)
                {
                    var hostConn = GetConnectorNearestTo(hostCurveAlt, to);
                    if (hostConn != null && farConn.Origin.DistanceTo(hostConn.Origin) < 2.0)
                        TryConnect(farConn, hostConn, result);
                }
            }

            return true;
        }

        /// <summary>
        /// Resolve the polyline that the run should follow. When
        /// UsePathfinder is false (default) returns [origin, to] — a
        /// straight line, identical to the v4 MVP behaviour. When
        /// pathfinder mode is enabled, runs A* against an obstacle list
        /// collected from the active document and returns the resulting
        /// polyline. On any pathfinder failure the method falls back
        /// to the straight line and surfaces the failure reason as a
        /// warning so the caller can investigate.
        /// </summary>
        protected virtual List<XYZ> ResolveRouteToHost(XYZ origin, XYZ to, Element host, DropResult result)
        {
            var fallback = new List<XYZ> { origin, to };
            if (!UsePathfinder) return fallback;
            try
            {
                var obstacles = RoutingPathfinder.CollectObstaclesInAABB(Doc, origin, to);
                var path = RoutingPathfinder.FindPath(origin, to, obstacles, MaxAStarExpansions);
                if (!path.Success || path.Points == null || path.Points.Count < 2)
                {
                    result.Warnings.Add($"Pathfinder failed ({path.FailureReason}); using straight drop.");
                    return fallback;
                }
                return path.Points;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Pathfinder threw: {ex.Message}; using straight drop.");
                return fallback;
            }
        }

        /// <summary>
        /// When SnapToCorridorBand is enabled and the engine's ServiceId
        /// maps to a corridor band, nudge the intercept Z to the band
        /// centre (relative to the host level) provided the existing
        /// intercept is within ±200 mm of the band centre. Anything
        /// further is left alone — the host curve genuinely sits in
        /// another band and forcing a snap would break the topology.
        /// </summary>
        protected XYZ MaybeSnapToCorridorBand(XYZ from, XYZ to, DropResult result)
        {
            if (!SnapToCorridorBand) return to;
            if (string.IsNullOrEmpty(ServiceId)) return to;
            var band = RoutingRules.FindBandForService(ServiceId);
            if (band == null) return to;

            try
            {
                // Band centre is FFL+mm. Derive FFL from the ActiveView's
                // GenLevel, falling back to Z=0.
                double levelZFt = 0;
                var lvlId = Doc.ActiveView?.GenLevel?.Id;
                if (lvlId != null && lvlId != ElementId.InvalidElementId)
                {
                    var lvl = Doc.GetElement(lvlId) as Level;
                    if (lvl != null) levelZFt = lvl.Elevation;
                }
                double bandCentreFt = levelZFt + (band.CentreMm * MmToFt);
                double deltaMm = Math.Abs(to.Z - bandCentreFt) * 304.8;
                if (deltaMm > 200.0) return to; // too far — leave alone

                var snapped = new XYZ(to.X, to.Y, bandCentreFt);
                result.Warnings.Add($"Band snap: {band.Id} @ FFL+{band.CentreMm:F0}mm (adjusted {deltaMm:F0}mm)");
                return snapped;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Corridor band snap failed: {ex.Message}");
                return to;
            }
        }

        // ---- parameter helpers --------------------------------------------------

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
