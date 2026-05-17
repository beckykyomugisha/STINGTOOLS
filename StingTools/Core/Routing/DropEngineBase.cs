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
        /// When true, the scoring in FindNearestContainment biases toward
        /// containment elements that participate in an existing connector
        /// network (i.e. have neighbours), reducing the risk of routing to
        /// isolated stubs. Default true.
        /// </summary>
        public bool PreferConnectedTrays { get; set; } = true;

        /// <summary>
        /// When true, the drop engine iterates every connector on the
        /// fixture rather than just the best free connector, emitting one
        /// drop per unique unconnected service connector. Useful when a
        /// fixture hosts both a hot-water and a cold-water connection.
        /// </summary>
        public bool MultiServiceMode { get; set; } = false;

        protected DropEngineBase(Document doc)
        {
            Doc = doc;
        }

        // ---- containment search -------------------------------------------------

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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
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

            // Stage 3: subclass creates the run geometry.
            var id = CreateRunBetween(origin, to, host, result);
            if (id == null || id == ElementId.InvalidElementId)
            {
                result.FailedCount++;
                return false;
            }
            result.CreatedIds.Add(id);

            // Stage 4: wire up the new run to both ends. Both steps are
            // best-effort — connector mismatches or geometry precision
            // issues must not destroy the drop we just created.
            var createdCurve = Doc.GetElement(id) as MEPCurve;
            if (createdCurve != null)
            {
                var nearConn = GetConnectorNearestTo(createdCurve, origin);
                var farConn  = GetConnectorNearestTo(createdCurve, to);

                if (fxConn != null && nearConn != null)
                    TryConnect(fxConn, nearConn, result);

                if (SupportsTakeoff && host is MEPCurve hostCurve && farConn != null)
                {
                    TryCreateTakeoff(farConn, hostCurve, result);
                }
                else if (host is MEPCurve hostCurveAlt && farConn != null)
                {
                    // Electrical / cable-tray: try direct ConnectTo.
                    // Find the closest host connector and attempt a
                    // fitting-mediated join. Will no-op when the host
                    // has no adjacent connector in range.
                    var hostConn = GetConnectorNearestTo(hostCurveAlt, to);
                    if (hostConn != null && farConn.Origin.DistanceTo(hostConn.Origin) < 2.0)
                        TryConnect(farConn, hostConn, result);
                }
            }

            return true;
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
