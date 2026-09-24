// EmergencyLoadRoots — Revit-free half of the dual-source generator check.
//
// DualSourceValidationCommand identifies emergency panels by ELC_FEED_TYPE_TXT or
// an emergency keyword in the name, then sums the apparent load of every circuit
// those panels serve. When an emergency panel feeds an emergency sub-panel, the
// feeder circuit already carries the sub-panel's whole load, and the sub-panel's
// own circuits were summed a second time — a false generator FAIL.
//
// The fix: sum only the circuits of ROOT emergency panels — those with no
// emergency panel anywhere upstream. A root's feeder circuits already include
// every downstream board's load, emergency-named or not.

using System.Collections.Generic;

namespace StingTools.Core.Electrical
{
    internal static class EmergencyLoadRoots
    {
        /// <summary>
        /// The members of <paramref name="emergencyPanels"/> with no emergency panel
        /// upstream of them.
        /// </summary>
        /// <param name="emergencyPanels">Ids of panels classed as emergency.</param>
        /// <param name="supplyOf">Panel id → id of the panel feeding it
        /// (the BaseEquipment of the circuit that serves it). Absent = unknown / fed
        /// from outside the model.</param>
        public static HashSet<long> Roots(IEnumerable<long> emergencyPanels,
            IReadOnlyDictionary<long, long> supplyOf)
        {
            var set = new HashSet<long>(emergencyPanels ?? new long[0]);
            var roots = new HashSet<long>();
            foreach (long p in set)
            {
                bool hasEmergencyAncestor = false;
                var seen = new HashSet<long> { p };      // guards a cyclic (mis-modelled) feed
                long cur = p;
                while (supplyOf != null && supplyOf.TryGetValue(cur, out long up) && up != 0 && seen.Add(up))
                {
                    if (set.Contains(up)) { hasEmergencyAncestor = true; break; }
                    cur = up;
                }
                if (!hasEmergencyAncestor) roots.Add(p);
            }
            return roots;
        }
    }
}
