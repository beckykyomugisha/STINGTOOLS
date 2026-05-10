using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Electrical;

namespace StingTools.Commands.Electrical.Routing
{
    /// <summary>
    /// Best-effort rectilinear conduit auto-routing. Walks the cable
    /// manifest's un-routed cables and creates Conduit elements along an
    /// L/Z path between each circuit's load and panel locations. The MEP
    /// Routing API isn't required (it isn't enabled on every project);
    /// production hardening could swap in proper clash avoidance — Phase
    /// 179 honestly delivers the simple Manhattan path.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ConduitAutoRouteCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            CableManifest manifest;
            try { manifest = CableManifest.Load(doc); }
            catch (Exception ex) { StingLog.Warn($"CableManifest.Load: {ex.Message}"); manifest = null; }
            if (manifest == null || manifest.Cables == null || manifest.Cables.Count == 0)
            {
                TaskDialog.Show("STING Auto-Route",
                    "No cable manifest found. Run Cable Sizer / Add Cable first to populate the manifest.");
                return Result.Cancelled;
            }
            var unrouted = manifest.Cables
                .Where(c => c.RouteTrayIds == null || c.RouteTrayIds.Count == 0)
                .ToList();
            if (unrouted.Count == 0)
            {
                TaskDialog.Show("STING Auto-Route", "All cables in the manifest already have routes assigned.");
                return Result.Succeeded;
            }

            var conduitType = new FilteredElementCollector(doc)
                .OfClass(typeof(ConduitType)).Cast<ConduitType>().FirstOrDefault();
            if (conduitType == null)
            {
                TaskDialog.Show("STING Auto-Route",
                    "No conduit type found in the project. Load a conduit family first.");
                return Result.Failed;
            }

            int routed = 0;
            using (var tx = new Transaction(doc, "STING Auto-Route Conduit"))
            {
                tx.Start();
                foreach (var cable in unrouted)
                {
                    try
                    {
                        ElectricalSystem sys = null;
                        try
                        {
                            sys = new FilteredElementCollector(doc)
                                .OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>()
                                .FirstOrDefault(s => string.Equals(
                                    s.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER)?.AsString() ?? "",
                                    cable.CircuitId, StringComparison.OrdinalIgnoreCase));
                        }
                        catch { }
                        if (sys == null) continue;

                        Element loadEl = null;
                        Element panelEl = null;
                        try { loadEl = sys.Elements?.Cast<Element>().FirstOrDefault(); } catch { }
                        try { panelEl = sys.BaseEquipment; } catch { }
                        if (loadEl == null || panelEl == null) continue;

                        var startPt = (loadEl.Location as LocationPoint)?.Point;
                        var endPt   = (panelEl.Location as LocationPoint)?.Point;
                        if (startPt == null || endPt == null) continue;

                        var levelId = loadEl.LevelId;
                        if (levelId == null || levelId == ElementId.InvalidElementId)
                            levelId = doc.ActiveView?.GenLevel?.Id ?? ElementId.InvalidElementId;
                        if (levelId == null || levelId == ElementId.InvalidElementId) continue;

                        double diamMm = ConduitRouteEngine.SelectConduitDiameterMm(
                            new List<StingCable> { cable });
                        var segments = ConduitRouteEngine.ComputeRoute(startPt, endPt, diamMm, cable.CircuitId);

                        // BS 7671 §522.8.5 — max 3 bends between draw-in
                        // points. Pre-flight: if the proposed run exceeds
                        // the cap, surface a finding so the user can
                        // either pick a different start/end or add
                        // junction boxes manually. We continue with
                        // creation regardless — failing closed would
                        // block the auto-router on the most common
                        // real-world layouts. The gate's role is to
                        // SURFACE the violation, not silently swallow it.
                        const int MaxBendsBetweenDrawIn = 3;
                        int bends = ConduitRouteEngine.CountBends(segments);
                        if (bends > MaxBendsBetweenDrawIn)
                        {
                            StingLog.Warn(
                                $"AutoRoute cable {cable.CircuitId}: route has {bends} bends, " +
                                $"exceeds BS 7671 §522.8.5 limit of {MaxBendsBetweenDrawIn}. " +
                                "Add a draw-in / junction box to break the run.");
                        }

                        var routeIds = new List<long>();
                        foreach (var seg in segments)
                        {
                            if (seg.Start.DistanceTo(seg.End) < 0.01) continue;
                            try
                            {
                                // TODO-VERIFY-API: Conduit.Create(Document, ElementId conduitTypeId,
                                //   XYZ start, XYZ end, ElementId levelId) — signature differs across
                                //   Revit versions. The 5-arg form is the Revit 2024+ canonical.
                                var conduit = Conduit.Create(doc, conduitType.Id,
                                    seg.Start, seg.End, levelId);
                                if (conduit != null)
                                {
                                    try
                                    {
                                        ParameterHelpers.SetString(conduit, ParamRegistry.ELC_CONDUIT_ROUTE,
                                            $"AUTO:{cable.CircuitId}", overwrite: true);
                                        // Stamp the bend count + run length so
                                        // ElectricalStandardsValidator + downstream
                                        // QA can read them without recomputing.
                                        ParameterHelpers.SetString(conduit,
                                            "ELC_CDT_BEND_COUNT_NR", bends.ToString(),
                                            overwrite: true);
                                        double mm = seg.Start.DistanceTo(seg.End) * 304.8;
                                        ParameterHelpers.SetString(conduit,
                                            "ELC_CDT_RUN_LENGTH_M",
                                            (mm / 1000.0).ToString("F3",
                                                System.Globalization.CultureInfo.InvariantCulture),
                                            overwrite: true);
                                    }
                                    catch { }
                                    routeIds.Add(conduit.Id.Value);
                                }
                            }
                            catch (Exception ex) { StingLog.Warn($"Conduit.Create: {ex.Message}"); }
                        }
                        if (routeIds.Count > 0)
                        {
                            cable.RouteTrayIds = routeIds;
                            routed++;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"AutoRoute cable {cable.CircuitId}: {ex.Message}"); }
                }
                tx.Commit();
            }
            try { manifest.Save(doc); } catch (Exception ex) { StingLog.Warn($"Manifest save: {ex.Message}"); }
            try { ComplianceScan.InvalidateCache(); } catch { }

            // Wave F1 — junction box auto-placement. Runs BEFORE slab
            // penetration so any JB-induced split also gets penetration
            // detection on its sub-segments. Walks every routed conduit,
            // identifies BS 7671 §522.8.5 break-points (>3 bends or
            // >6 m run length), places STING_SEED_JunctionBox at each
            // violation point, stamps the conduit with a back-reference.
            // When the seed family isn't loaded, stamps the would-be
            // location on the conduit as ELC_CDT_BREAKPOINT_TXT and
            // surfaces a warning — the schedule still flags the run.
            int junctionBoxes = 0;
            try
            {
                var jbConduitIds = new List<ElementId>();
                foreach (var c in manifest.Cables)
                {
                    if (c.RouteTrayIds == null) continue;
                    foreach (long lid in c.RouteTrayIds)
                        jbConduitIds.Add(new ElementId((long)lid));
                }
                if (jbConduitIds.Count > 0)
                {
                    using (var tx2 = new Transaction(doc, "STING Junction Box Auto-Place"))
                    {
                        tx2.Start();
                        var jbResult = StingTools.Core.Routing.JunctionBoxAutoPlacer.Place(doc, jbConduitIds);
                        junctionBoxes = jbResult.Placed;
                        foreach (var w in jbResult.Warnings) StingLog.Info($"JB placer: {w}");

                        // Wave H4 — after JB placement, propagate the
                        // placed-box ids back into each cable's manifest
                        // entry so downstream tools (cable schedule, FRP
                        // register, swap planner) see the correct routing
                        // topology. Map break-point.ConduitId → cables
                        // whose RouteTrayIds contains that conduit, then
                        // append the placed JB id.
                        try
                        {
                            var byConduit = new Dictionary<long, List<long>>();
                            foreach (var bp in jbResult.Points)
                            {
                                if (bp.PlacedBoxId == null ||
                                    bp.PlacedBoxId == ElementId.InvalidElementId) continue;
                                long key = bp.ConduitId.Value;
                                if (!byConduit.TryGetValue(key, out var list))
                                { list = new List<long>(); byConduit[key] = list; }
                                if (!list.Contains(bp.PlacedBoxId.Value))
                                    list.Add(bp.PlacedBoxId.Value);
                            }
                            foreach (var c in manifest.Cables)
                            {
                                if (c.RouteTrayIds == null || c.RouteTrayIds.Count == 0) continue;
                                if (c.JunctionBoxIds == null) c.JunctionBoxIds = new List<long>();
                                foreach (long routeId in c.RouteTrayIds)
                                {
                                    if (byConduit.TryGetValue(routeId, out var jbIds))
                                    {
                                        foreach (long jb in jbIds)
                                            if (!c.JunctionBoxIds.Contains(jb))
                                                c.JunctionBoxIds.Add(jb);
                                    }
                                }
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"JB → manifest sync: {ex.Message}"); }

                        tx2.Commit();
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"JunctionBoxAutoPlacer: {ex.Message}"); }

            // Phase Wave D — slab-penetration detection. Walks every
            // newly-created conduit, finds floor crossings, stamps
            // STING_PENETRATION_REF_TXT + STING_PENETRATION_FIRE_RATING_TXT
            // so the FRP register can identify every fire-stop the
            // contractor needs to install. Runs in its own transaction
            // because it only writes parameters (not geometry) and we
            // want it visible in the journal as a separate undo step.
            int penetrations = 0;
            try
            {
                var routedIds = new List<ElementId>();
                foreach (var c in manifest.Cables)
                {
                    if (c.RouteTrayIds == null) continue;
                    foreach (long lid in c.RouteTrayIds)
                        routedIds.Add(new ElementId((long)lid));
                }
                if (routedIds.Count > 0)
                {
                    using (var tx2 = new Transaction(doc, "STING Slab Penetration Stamp"))
                    {
                        tx2.Start();
                        var recs = StingTools.Core.Routing.SlabPenetrationDetector.Detect(doc, routedIds);
                        penetrations = recs.Count;

                        // Wave E4 — auto-place STING_SEED_SpecialityEquipment
                        // (or its swapped manufacturer family) at every
                        // penetration record. When the seed isn't loaded,
                        // FrpPenetrationPlacer.Place degrades gracefully
                        // — surfaces a warning + stamps the member-side
                        // PEN_CONTROL_NUMBER_TXT so the register schedule
                        // still works.
                        try
                        {
                            var place = StingTools.Core.Routing.FrpPenetrationPlacer.Place(doc, recs);
                            foreach (var w in place.Warnings) StingLog.Info($"FRP placer: {w}");
                            if (place.Placed > 0)
                                StingLog.Info($"FRP placer: placed {place.Placed} family instance(s).");
                        }
                        catch (Exception ex) { StingLog.Warn($"FrpPenetrationPlacer: {ex.Message}"); }

                        tx2.Commit();
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"SlabPenetrationDetector: {ex.Message}"); }

            TaskDialog.Show("STING Auto-Route",
                $"Routed {routed} of {unrouted.Count} cable(s).\n" +
                $"Junction boxes auto-placed: {junctionBoxes}.\n" +
                $"Slab penetrations stamped: {penetrations}.\n\n" +
                "Note: routes use simplified L-shaped paths.\n" +
                "Review and adjust in Revit for clash avoidance.\n" +
                "Penetration parameters stamped — FRP_PENETRATION family will install over each marked point once the family ships.");
            return Result.Succeeded;
        }
    }
}
