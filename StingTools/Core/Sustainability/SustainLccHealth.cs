// StingTools — LCC integrity / health (WS I6).
//
// LCC measures derive their savings from the energy / water / materials gates. When
// a gate wasn't computed (location/use unset, no GFA/occupancy, blocked run) the
// per-measure saving is off a broken baseline and must be flagged "indicative —
// gate not computed", not presented as a confident negative; and the headline net
// benefit needs a health caveat when its inputs are proxies.
//
// Pure POCO — no Revit dependency. Unit-tested.

namespace StingTools.Core.Sustainability
{
    public class LccHealth
    {
        public bool   HasCaveat { get; set; }
        public string Caveat    { get; set; } = "";
    }

    public static class SustainLccHealth
    {
        /// <summary>Was the gate a measure draws its saving from actually computed?
        /// Folds in readiness (a blocked run computes nothing real).</summary>
        public static bool GateComputed(string gate, bool ready, bool energyComputed, bool waterComputed, bool materialsComputed)
        {
            if (!ready) return false;
            switch ((gate ?? "").Trim().ToLowerInvariant())
            {
                case "energy":    return energyComputed;
                case "water":     return waterComputed;
                case "materials": return materialsComputed;
                default:          return false;
            }
        }

        /// <summary>Evaluate the overall LCC health. A caveat is raised when the run is
        /// blocked, any measure's gate wasn't computed, measures were proxy-sized, or
        /// there's no operational saving (so net benefit is just −capex).</summary>
        public static LccHealth Evaluate(bool ready, int measuresOnNotComputedGate,
            int proxySizedMeasures, bool noOperationalSaving)
        {
            var h = new LccHealth();
            var reasons = new System.Collections.Generic.List<string>();
            if (!ready) reasons.Add("location/use not set — the baseline is a generic proxy");
            if (measuresOnNotComputedGate > 0)
                reasons.Add($"{measuresOnNotComputedGate} measure(s) draw savings from a gate that wasn't computed");
            if (proxySizedMeasures > 0)
                reasons.Add($"{proxySizedMeasures} measure(s) were proxy-sized (no model quantity)");
            if (noOperationalSaving)
                reasons.Add("no operational saving computed — net benefit is capex-only, not a loss");

            h.HasCaveat = reasons.Count > 0;
            h.Caveat = h.HasCaveat
                ? "Indicative LCC — " + string.Join("; ", reasons) + ". Treat figures as a proxy, not a confident position."
                : "";
            return h;
        }
    }
}
