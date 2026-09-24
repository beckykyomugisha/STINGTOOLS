// ConduitCircuitResolver — which circuit does this conduit carry?
//
// ELEC-9. A conduit's connectors are never owned by an ElectricalSystem, so the
// old lookup (accept a connector whose Owner is an ElectricalSystem) could not
// succeed: Wire Stamp / Batch Stamp always reported "no connected circuit" and
// the wire label fell back to "? Wire".
//
// This walks the conduit + conduit-fitting graph to the family instances at the
// ends of the run (devices, fixtures, panels), collects every circuit those
// endpoints take part in, and lets CircuitEndpointChooser pick the circuit that
// explains the run. A device with no circuit (a junction box, a pull box) is
// walked through rather than treated as an end.
//
// Both the wire-parameter stamp (WireParamSyncCommands) and the wire annotation
// engine (WireAnnotationEngine.GetConnectedCircuit) resolve through here, so
// the two cannot disagree about which circuit a conduit carries.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace StingTools.Core.Electrical
{
    internal sealed class ConduitCircuitResult
    {
        public ElectricalSystem Circuit;
        public string Reason = "";
    }

    internal static class ConduitCircuitResolver
    {
        private const int MaxHops = 40;        // conduit + fitting hops along one run
        private const int MaxEndpoints = 64;   // a run touching more than this is a network, not a circuit

        public static ElectricalSystem Resolve(Element conduit) => ResolveWithReason(conduit).Circuit;

        public static ConduitCircuitResult ResolveWithReason(Element conduit)
        {
            var result = new ConduitCircuitResult();
            if (conduit == null) { result.Reason = "no element"; return result; }
            var doc = conduit.Document;

            var start = ConnectorManagerOf(conduit);
            if (start == null) { result.Reason = "the element has no connectors"; return result; }

            var visited = new HashSet<long> { conduit.Id.Value };
            var frontier = new List<Connector>();
            try { foreach (Connector c in start.Connectors) frontier.Add(c); }
            catch (Exception ex) { result.Reason = "connectors unreadable: " + ex.Message; return result; }

            var endpoints = new List<CircuitEndpoint>();
            var circuitCache = new Dictionary<long, CircuitCandidate>();

            for (int hop = 0; hop < MaxHops && frontier.Count > 0 && endpoints.Count < MaxEndpoints; hop++)
            {
                var next = new List<Connector>();
                foreach (var fc in frontier)
                {
                    ConnectorSet refs;
                    try { if (!fc.IsConnected) continue; refs = fc.AllRefs; }
                    catch { continue; }
                    if (refs == null) continue;

                    foreach (Connector other in refs)
                    {
                        Element owner;
                        try { owner = other?.Owner; } catch { continue; }
                        if (owner == null || owner is MEPSystem) continue;
                        if (!visited.Add(owner.Id.Value)) continue;

                        long cat = owner.Category?.Id?.Value ?? 0;
                        bool isRun = cat == (long)BuiltInCategory.OST_Conduit
                                  || cat == (long)BuiltInCategory.OST_ConduitFitting;

                        if (!isRun && owner is FamilyInstance fi && fi.MEPModel != null)
                        {
                            var ep = EndpointOf(fi, circuitCache);
                            if (ep.IsPanel || ep.Circuits.Count > 0)
                            {
                                endpoints.Add(ep);
                                continue;   // an end of the run — do not walk through it
                            }
                            // No circuit and not a panel: a junction / pull box. Walk through.
                        }
                        else if (!isRun)
                        {
                            continue;       // neither a run piece nor a family instance
                        }

                        var ocm = ConnectorManagerOf(owner);
                        if (ocm == null) continue;
                        try
                        {
                            foreach (Connector pc in ocm.Connectors)
                            {
                                if (pc.Id == other.Id) continue;
                                next.Add(pc);
                            }
                        }
                        catch { }
                    }
                }
                frontier = next;
            }

            var choice = CircuitEndpointChooser.Choose(endpoints);
            if (!choice.Found) { result.Reason = choice.Reason; return result; }
            result.Circuit = doc.GetElement(new ElementId(choice.CircuitId)) as ElectricalSystem;
            if (result.Circuit == null) result.Reason = $"circuit {choice.CircuitId} no longer exists";
            return result;
        }

        private static CircuitEndpoint EndpointOf(FamilyInstance fi, Dictionary<long, CircuitCandidate> cache)
        {
            var ep = new CircuitEndpoint
            {
                ElementId = fi.Id.Value,
                IsPanel = (fi.Category?.Id?.Value ?? 0) == (long)BuiltInCategory.OST_ElectricalEquipment,
            };
            var seen = new HashSet<long>();
            void Add(IEnumerable<ElectricalSystem> systems)
            {
                if (systems == null) return;
                foreach (var s in systems)
                {
                    if (s == null || !seen.Add(s.Id.Value)) continue;
                    ep.Circuits.Add(CandidateOf(s, cache));
                }
            }
            try { Add(fi.MEPModel.GetElectricalSystems()); } catch { }
            // A panel's GetElectricalSystems is the circuit feeding it; the circuits
            // it feeds come from GetAssignedElectricalSystems.
            try { Add(fi.MEPModel.GetAssignedElectricalSystems()); } catch { }
            return ep;
        }

        private static CircuitCandidate CandidateOf(ElectricalSystem s, Dictionary<long, CircuitCandidate> cache)
        {
            if (cache.TryGetValue(s.Id.Value, out var c)) return c;
            c = new CircuitCandidate { CircuitId = s.Id.Value };
            try { c.BaseEquipmentId = s.BaseEquipment?.Id.Value ?? 0; } catch { }
            try
            {
                var els = s.Elements;
                if (els != null)
                    foreach (Element e in els)
                        if (e != null) c.MemberIds.Add(e.Id.Value);
            }
            catch { }
            cache[s.Id.Value] = c;
            return c;
        }

        private static ConnectorManager ConnectorManagerOf(Element el)
        {
            try
            {
                if (el is MEPCurve mc) return mc.ConnectorManager;
                if (el is FamilyInstance fi) return fi.MEPModel?.ConnectorManager;
            }
            catch { }
            return null;
        }
    }
}
