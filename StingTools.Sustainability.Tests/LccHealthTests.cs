using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS I6 — LCC integrity: a measure's saving is only credible when its gate was
    // computed and the run isn't blocked; the headline needs a health caveat when
    // its inputs are proxies.
    public class LccHealthTests
    {
        [Fact]
        public void GateComputed_RequiresReadyAndGate()
        {
            Assert.True(SustainLccHealth.GateComputed("energy", ready: true, energyComputed: true, false, false));
            Assert.False(SustainLccHealth.GateComputed("energy", ready: false, energyComputed: true, false, false));   // blocked
            Assert.False(SustainLccHealth.GateComputed("energy", ready: true, energyComputed: false, false, false));   // gate not computed
            Assert.True(SustainLccHealth.GateComputed("water", true, false, true, false));
            Assert.False(SustainLccHealth.GateComputed("unknown", true, true, true, true));
        }

        [Fact]
        public void Evaluate_CleanRun_NoCaveat()
        {
            var h = SustainLccHealth.Evaluate(ready: true, measuresOnNotComputedGate: 0, proxySizedMeasures: 0, noOperationalSaving: false);
            Assert.False(h.HasCaveat);
            Assert.Equal("", h.Caveat);
        }

        [Fact]
        public void Evaluate_BlockedRun_HasCaveat()
        {
            var h = SustainLccHealth.Evaluate(ready: false, 5, 0, true);
            Assert.True(h.HasCaveat);
            Assert.Contains("location/use not set", h.Caveat);
            Assert.Contains("gate that wasn't computed", h.Caveat);
            Assert.Contains("capex-only", h.Caveat);
        }

        [Fact]
        public void Evaluate_ProxySizedOnly_HasCaveat()
        {
            var h = SustainLccHealth.Evaluate(ready: true, measuresOnNotComputedGate: 0, proxySizedMeasures: 3, noOperationalSaving: false);
            Assert.True(h.HasCaveat);
            Assert.Contains("proxy-sized", h.Caveat);
        }
    }
}
