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
    /// manifest's un-routed cables, resolves each to its circuit through
    /// <see cref="CableCircuitResolver"/>, and creates Conduit elements along
    /// an L/Z path between the load and panel, sized to ≤40 % fill. The MEP
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
            string emptyWhy = manifest == null ? "The cable manifest could not be loaded." : manifest.DescribeEmpty();
            if (emptyWhy != null)
            {
                TaskDialog.Show("STING Auto-Route", emptyWhy);
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

            var conduitTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(ConduitType)).Cast<ConduitType>().ToList();
            var conduitType = conduitTypes.FirstOrDefault();
            if (conduitType == null)
            {
                TaskDialog.Show("STING Auto-Route",
                    "No conduit type found in the project. Load a conduit family first.");
                return Result.Failed;
            }

            // ELC-7: resolve each cable to its circuit by element id, then
            // source + destination, then panel + circuit number — never by a
            // bare circuit number, which repeats on every panel. The old
            // match compared the "<source>-<destId>" CircuitId AddCable
            // writes against RBS_ELEC_CIRCUIT_NUMBER and so matched nothing.
            var resolver = CableCircuitResolver.Build(doc);
            var unresolved = new List<string>();
            var skipped = new List<string>();
            var byMethod = new Dictionary<CircuitMatchMethod, int>();
            int sizeApplied = 0, sizeSnapped = 0, sizeNotSet = 0;
            var sizeNotes = new List<string>();
            var bendViolations = new List<string>();
            int createFailures = 0;

            int routed = 0;
            using (var tx = new Transaction(doc, "STING Auto-Route Conduit"))
            {
                tx.Start();
                foreach (var cable in unrouted)
                {
                    string label = string.IsNullOrEmpty(cable.CircuitId) ? $"#{cable.SequenceNumber:D4}" : cable.CircuitId;
                    try
                    {
                        var match = resolver.Resolve(cable, out ElectricalSystem sys);
                        if (!match.Found || sys == null)
                        {
                            unresolved.Add($"{label}: {match.Reason}");
                            continue;
                        }
                        byMethod[match.Method] = byMethod.TryGetValue(match.Method, out int n) ? n + 1 : 1;
                        // Persist the resolved id so the next run (and other
                        // consumers) use the authoritative key.
                        cable.CircuitElementId = sys.Id.Value;

                        // Route from THIS cable's destination when it is on the
                        // circuit; the first circuit member is only a fallback
                        // for number-keyed manifests that name no destination.
                        Element loadEl = null;
                        Element panelEl = null;
                        try
                        {
                            if (!string.IsNullOrEmpty(cable.DestEquipmentId))
                                loadEl = doc.GetElement(cable.DestEquipmentId);
                            if (loadEl == null && CableCircuitIdentity.TryParseLegacyDestinationId(cable.CircuitId, out long destId))
                                loadEl = doc.GetElement(new ElementId(destId));
                            if (loadEl == null)
                                loadEl = sys.Elements?.Cast<Element>().FirstOrDefault();
                        }
                        catch (Exception ex3) { StingLog.Warn($"AutoRoute {label} load element: {ex3.Message}"); }
                        try { panelEl = sys.BaseEquipment; } catch (Exception ex4) { StingLog.Warn($"AutoRoute {label} base equipment: {ex4.Message}"); }
                        if (loadEl == null || panelEl == null)
                        {
                            skipped.Add($"{label}: circuit has no {(loadEl == null ? "load element" : "panel (base equipment)")}");
                            continue;
                        }

                        // Converged origin contract (shared with the v4 drop
                        // engine): start the run at the fixture / panel conduit
                        // connector when present, LocationPoint only as fallback.
                        // Previously this read LocationPoint directly and so
                        // began conduit runs at the family insertion point,
                        // ignoring the placed-fixture connectors AutoConduitDrop
                        // already honours.
                        var startPt = StingTools.Core.Routing.RoutingOriginResolver.Resolve(loadEl, out string startSrc)
                                      ?? (loadEl.Location as LocationPoint)?.Point;
                        var endPt   = StingTools.Core.Routing.RoutingOriginResolver.Resolve(panelEl, out string endSrc)
                                      ?? (panelEl.Location as LocationPoint)?.Point;
                        if (startPt == null || endPt == null)
                        {
                            skipped.Add($"{label}: no connector or location point on the {(startPt == null ? "load" : "panel")}");
                            continue;
                        }
                        if (startSrc == "location-point" || endSrc == "location-point")
                            StingLog.Info($"AutoRoute cable {cable.CircuitId}: origin fell back to LocationPoint " +
                                          $"(load={startSrc}, panel={endSrc}) — element has no MEP connector.");

                        var levelId = loadEl.LevelId;
                        if (levelId == null || levelId == ElementId.InvalidElementId)
                            levelId = doc.ActiveView?.GenLevel?.Id ?? ElementId.InvalidElementId;
                        if (levelId == null || levelId == ElementId.InvalidElementId)
                        {
                            skipped.Add($"{label}: no level on the load element or the active view");
                            continue;
                        }

                        double diamMm = ConduitRouteEngine.SelectConduitDiameterMm(
                            new List<StingCable> { cable });                        var segments = ConduitRouteEngine.ComputeRoute(startPt, endPt, diamMm, cable.CircuitId);

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
                            bendViolations.Add($"{label}: {bends} bends");
                            StingLog.Warn(
                                $"AutoRoute cable {cable.CircuitId}: route has {bends} bends, " +
                                $"exceeds BS 7671 §522.8.5 limit of {MaxBendsBetweenDrawIn}. " +
                                "Add a draw-in / junction box to break the run.");
                        }

                        var routeIds = new List<long>();
                        foreach (var seg in segments)
                        {
                            if (seg.Start.DistanceTo(seg.End) < ConduitRouteEngine.MinLegFt) continue;
                            try
                            {
                                // Revit 2025 API: Conduit.Create(Document, ElementId conduitTypeId,
                                //   XYZ startPoint, XYZ endPoint, ElementId levelId).
                                // conduitType is resolved once, before the transaction.
                                var conduit = Conduit.Create(doc, conduitType.Id,
                                    seg.Start, seg.End, levelId);
                                if (conduit != null)
                                {
                                    // ELC-7: the computed diameter used to be discarded,
                                    // leaving every run at the type's default size. Set
                                    // it (mm -> internal feet) and read it back — Revit
                                    // snaps to the sizes its conduit standard defines,
                                    // so report what the model actually holds.
                                    var outcome = ConduitDiameterApplier.Apply(conduit, diamMm, out _, out string sizeNote);
                                    if (outcome == DiameterOutcome.Applied) sizeApplied++;
                                    else
                                    {
                                        if (outcome == DiameterOutcome.Snapped) sizeSnapped++; else sizeNotSet++;
                                        if (sizeNotes.Count < 10) sizeNotes.Add($"{label}: {sizeNote}");
                                    }
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
                                    catch (Exception ex5) { StingLog.Warn($"Suppressed: {ex5.Message}"); }
                                    routeIds.Add(conduit.Id.Value);
                                }
                            }
                            catch (Exception ex5) { createFailures++; StingLog.Warn($"Conduit.Create {label}: {ex5.Message}"); }
                        }
                        if (routeIds.Count > 0)
                        {
                            cable.RouteTrayIds = routeIds;
                            routed++;
                        }
                        else
                        {
                            skipped.Add($"{label}: no conduit segment could be created (see log)");
                        }
                    }
                    catch (Exception ex2)
                    {
                        skipped.Add($"{label}: {ex2.Message}");
                        StingLog.Warn($"AutoRoute cable {cable.CircuitId}: {ex2.Message}");
                    }
                }
                tx.Commit();
            }
            try { manifest.Save(doc); } catch (Exception ex2) { StingLog.Warn($"Manifest save: {ex2.Message}"); }
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

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
                        catch (Exception ex2) { StingLog.Warn($"JB → manifest sync: {ex2.Message}"); }

                        tx2.Commit();
                    }
                }
            }
            catch (Exception ex2) { StingLog.Warn($"JunctionBoxAutoPlacer: {ex2.Message}"); }

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
                        catch (Exception ex2) { StingLog.Warn($"FrpPenetrationPlacer: {ex2.Message}"); }

                        tx2.Commit();
                    }
                }
            }
            catch (Exception ex2) { StingLog.Warn($"SlabPenetrationDetector: {ex2.Message}"); }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Routed {routed} of {unrouted.Count} un-routed cable(s).");
            if (byMethod.Count > 0)
                sb.AppendLine("Circuits matched by: " + string.Join(", ",
                    byMethod.Select(kv => $"{CableCircuitIdentity.Describe(kv.Key)} ×{kv.Value}")) + ".");
            if (unresolved.Count > 0)
            {
                sb.AppendLine($"Not matched to a circuit: {unresolved.Count} (of {resolver.CircuitCount} circuit(s) in the model):");
                foreach (var u in unresolved.Take(8)) sb.AppendLine("  • " + u);
                if (unresolved.Count > 8) sb.AppendLine($"  … and {unresolved.Count - 8} more (see log).");
            }
            if (skipped.Count > 0)
            {
                sb.AppendLine($"Matched but not routed: {skipped.Count}:");
                foreach (var s in skipped.Take(8)) sb.AppendLine("  • " + s);
                if (skipped.Count > 8) sb.AppendLine($"  … and {skipped.Count - 8} more.");
            }
            foreach (var u in unresolved) StingLog.Info($"AutoRoute unmatched: {u}");
            foreach (var s in skipped)    StingLog.Info($"AutoRoute skipped: {s}");

            sb.AppendLine();
            sb.AppendLine($"Conduit type: '{conduitType.Name}'" +
                (conduitTypes.Count > 1
                    ? $" — the first of {conduitTypes.Count} types in the project; the others were not considered."
                    : "."));
            sb.AppendLine($"Diameter (≤40 % fill, BS 7671 App. E): set as computed on {sizeApplied} segment(s)" +
                (sizeSnapped > 0 ? $", snapped to a different size on {sizeSnapped}" : "") +
                (sizeNotSet > 0 ? $", NOT applied on {sizeNotSet} (type default kept)" : "") + ".");
            foreach (var n in sizeNotes) sb.AppendLine("  • " + n);
            if (routed > 0)
                sb.AppendLine("Cable OD/CSA come from the manifest record; where AddCable did not size the " +
                              "cable, that is its 2.5 mm² default, so check the diameter before issue.");
            if (createFailures > 0)
                sb.AppendLine($"Conduit.Create failed for {createFailures} segment(s) — see log.");
            if (bendViolations.Count > 0)
                sb.AppendLine($"Over the BS 7671 §522.8.5 3-bend limit: {string.Join("; ", bendViolations.Take(5))}" +
                              (bendViolations.Count > 5 ? " …" : "") + ".");

            sb.AppendLine();
            sb.AppendLine($"Junction boxes auto-placed: {junctionBoxes}.");
            sb.AppendLine($"Slab penetrations stamped: {penetrations}.");
            sb.AppendLine();
            // Honest about the method: the live path is the fixed rectilinear
            // L/Z of ConduitRouteEngine.ComputeRoute. The A* voxel router
            // (ComputeRouteAdvanced) is NOT used — it emits one conduit per
            // 200 mm voxel with no colinear merge, can seat the start/end on an
            // obstacle cell, and joins the exact endpoints to cell centres with
            // non-orthogonal legs. See ROADMAP ELEC-7.
            sb.AppendLine("Route method: fixed rectilinear L/Z path (along X at the load's elevation, vertical rise/drop, then along Y to the panel). " +
                          "No obstacle or clash avoidance was performed — review every run in Revit.");
            sb.Append("Penetration parameters stamped — FRP_PENETRATION family will install over each marked point once the family ships.");

            TaskDialog.Show("STING Auto-Route", sb.ToString());
            return Result.Succeeded;
        }
    }
}
