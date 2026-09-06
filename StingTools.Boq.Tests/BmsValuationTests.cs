using System.Collections.Generic;
using StingTools.Core.Twin;
using Xunit;

namespace StingTools.Boq.Tests
{
    // Phase H3 (KUT lifecycle) — BMS commissioning-valuation arithmetic.
    public class BmsValuationTests
    {
        [Theory]
        [InlineData("ok", true, true)]
        [InlineData("", true, true)]        // Baja empty-status object = OK
        [InlineData("{ok}", true, true)]    // decorated status
        [InlineData("online", true, true)]
        [InlineData("fault", true, false)]  // in-service status but faulted
        [InlineData("down", true, false)]
        [InlineData("stale", true, false)]
        [InlineData("disabled", true, false)]
        [InlineData("ok", false, false)]    // live status but no present value = dead point
        [InlineData(null, true, true)]      // absent status coalesces to Baja empty-OK; hasValue gate still protects
        [InlineData(null, false, false)]    // absent status + no value = not commissioned
        public void IsCommissioned_StatusAndValue(string status, bool hasVal, bool expected)
            => Assert.Equal(expected, BmsValuation.IsCommissioned(status, hasVal));

        [Fact]
        public void Compute_RollsUpValueAndCounts()
        {
            var assets = new List<BmsAsset>
            {
                new BmsAsset { DeviceId = "AHU-1", ValueUGX = 1_000_000, Commissioned = true },
                new BmsAsset { DeviceId = "FCU-2", ValueUGX = 400_000,   Commissioned = false }, // configured, not live
                new BmsAsset { DeviceId = "",      ValueUGX = 200_000,   Commissioned = false }, // no point yet
            };
            var r = BmsValuation.Compute(assets);
            Assert.Equal(3, r.MonitorableCount);
            Assert.Equal(1_600_000, r.MonitorableValueUGX);
            Assert.Equal(1, r.CommissionedCount);
            Assert.Equal(1_000_000, r.CommissionedValueUGX);
            Assert.Equal(1, r.NoPointCount);
            Assert.Equal(1_000_000.0 / 1_600_000.0, r.CommissionedValueFraction, 4);
        }

        [Fact]
        public void Compute_EmptyScope_ZeroFractionNoDivByZero()
        {
            var r = BmsValuation.Compute(new List<BmsAsset>());
            Assert.Equal(0, r.MonitorableCount);
            Assert.Equal(0.0, r.CommissionedValueFraction);
        }
    }
}
