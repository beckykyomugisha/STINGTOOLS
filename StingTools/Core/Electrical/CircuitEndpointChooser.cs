// CircuitEndpointChooser — Revit-free half of the conduit → circuit resolver.
//
// A conduit never belongs to an ElectricalSystem, so the circuit it carries
// has to be inferred from what its run connects: the devices and panels at the
// ends of the conduit/fitting graph. ConduitCircuitResolver walks that graph in
// Revit and hands the endpoints here; this class only decides.
//
// The rule is "the circuit that explains the most endpoints". A run from panel
// DB1 to a socket is carried by the socket's circuit whose panel is DB1 (it
// touches both ends), not by whichever of DB1's forty circuits enumerates
// first. A run that touches only a panel is ambiguous and says so, rather than
// picking one.

using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Electrical
{
    internal sealed class CircuitCandidate
    {
        public long CircuitId;
        /// <summary>The panel the circuit is fed from (ElectricalSystem.BaseEquipment), or 0.</summary>
        public long BaseEquipmentId;
        /// <summary>Elements the circuit serves (ElectricalSystem.Elements).</summary>
        public HashSet<long> MemberIds = new HashSet<long>();
    }

    internal sealed class CircuitEndpoint
    {
        public long ElementId;
        /// <summary>Electrical Equipment (a panel / board), as opposed to a load device.</summary>
        public bool IsPanel;
        /// <summary>Every circuit the endpoint takes part in — supplied by or feeding.</summary>
        public List<CircuitCandidate> Circuits = new List<CircuitCandidate>();
    }

    internal sealed class CircuitChoice
    {
        /// <summary>The chosen circuit, or 0 when none could be chosen.</summary>
        public long CircuitId;
        /// <summary>Why nothing was chosen. Empty when <see cref="CircuitId"/> is set.</summary>
        public string Reason = "";
        public bool Found => CircuitId != 0;
    }

    internal static class CircuitEndpointChooser
    {
        public static CircuitChoice Choose(IReadOnlyList<CircuitEndpoint> endpoints)
        {
            if (endpoints == null || endpoints.Count == 0)
                return new CircuitChoice
                {
                    Reason = "the conduit run reaches no electrical device or panel (is it connected at either end?)"
                };

            var candidates = new Dictionary<long, CircuitCandidate>();
            foreach (var ep in endpoints)
                foreach (var c in ep.Circuits ?? new List<CircuitCandidate>())
                    if (c != null && c.CircuitId != 0 && !candidates.ContainsKey(c.CircuitId))
                        candidates[c.CircuitId] = c;

            if (candidates.Count == 0)
                return new CircuitChoice
                {
                    Reason = $"the run reaches {endpoints.Count} element(s), none of which is on a circuit"
                };

            // Support = how many distinct endpoints the circuit touches.
            var support = candidates.Values
                .Select(c => new
                {
                    c.CircuitId,
                    Count = endpoints.Count(ep => Touches(c, ep)),
                    OnDevice = endpoints.Any(ep => !ep.IsPanel && Touches(c, ep)),
                })
                .ToList();

            int best = support.Max(s => s.Count);
            var top = support.Where(s => s.Count == best).ToList();
            if (top.Count == 1)
                return new CircuitChoice { CircuitId = top[0].CircuitId };

            // A tie. Prefer circuits that reach an actual load: a panel alone
            // enumerates every circuit it feeds, which says nothing about this run.
            var onDevice = top.Where(s => s.OnDevice).ToList();
            if (onDevice.Count == 1)
                return new CircuitChoice { CircuitId = onDevice[0].CircuitId };

            bool panelsOnly = endpoints.All(ep => ep.IsPanel);
            return new CircuitChoice
            {
                Reason = panelsOnly && endpoints.Count == 1
                    ? $"the run reaches only a panel, which has {top.Count} circuits — cannot tell which one this conduit carries"
                    : $"the run is shared by {top.Count} circuits — cannot tell which one this conduit carries"
            };
        }

        private static bool Touches(CircuitCandidate c, CircuitEndpoint ep)
            => ep.ElementId != 0
               && (c.BaseEquipmentId == ep.ElementId || (c.MemberIds != null && c.MemberIds.Contains(ep.ElementId)));
    }
}
