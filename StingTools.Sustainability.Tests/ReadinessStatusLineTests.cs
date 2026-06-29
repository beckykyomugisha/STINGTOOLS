using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS I11 — readiness surfaced as a compact health-check / status-bar line.
    public class ReadinessStatusLineTests
    {
        [Fact]
        public void Complete_ReadsReady()
        {
            var r = SustainReadiness.Evaluate(true, true, true, true);
            Assert.Equal("Sustainability: ready", SustainReadiness.StatusLine(r));
        }

        [Fact]
        public void Blocked_ListsBlockAxes()
        {
            var r = SustainReadiness.Evaluate(false, false, true, true);
            string line = SustainReadiness.StatusLine(r);
            Assert.Contains("blocked", line);
            Assert.Contains("location", line);
            Assert.Contains("building use", line);
        }

        [Fact]
        public void ReadyButIncomplete_ListsMissingSofterItems()
        {
            var r = SustainReadiness.Evaluate(true, true, false, false);
            string line = SustainReadiness.StatusLine(r);
            Assert.Contains("indicative", line);
            Assert.Contains("occupancy", line);
            Assert.Contains("plumbing fixtures", line);
            Assert.DoesNotContain("blocked", line);
        }

        [Fact]
        public void Null_IsSafe()
            => Assert.Equal("Sustainability: unknown", SustainReadiness.StatusLine(null));
    }
}
