using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS I13 — the staleness flag the IUpdater flips on model change and the
    // dashboard clears after a fresh run. (Collection-disabled parallelism so the
    // static state isn't raced by other tests.)
    [Collection("StaleState")]
    public class StaleStateTests
    {
        [Fact]
        public void MarkStale_ThenFresh_Transitions()
        {
            SustainStaleState.MarkStale("walls changed");
            Assert.True(SustainStaleState.IsStale);
            Assert.Equal("walls changed", SustainStaleState.Reason);

            SustainStaleState.MarkFresh();
            Assert.False(SustainStaleState.IsStale);
            Assert.Equal("", SustainStaleState.Reason);
        }

        [Fact]
        public void MarkStale_NullReason_IsEmptyString()
        {
            SustainStaleState.MarkStale(null);
            Assert.True(SustainStaleState.IsStale);
            Assert.Equal("", SustainStaleState.Reason);
            SustainStaleState.MarkFresh();   // reset for other tests
        }
    }
}
