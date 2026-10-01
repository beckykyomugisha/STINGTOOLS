using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Plumbing;
using Xunit;

namespace StingTools.Mep.Tests
{
    /// <summary>DSCH-25 — TMV outlet limits by outlet / scheme / assisted bathing and
    /// dead-leg limits, all from the shipped STING_TMV_STANDARDS.json (no constant).</summary>
    public class WaterSafetyLimitsTests
    {
        private static WaterSafetyLimitsFile Shipped()
        {
            var f = WaterSafetyLimits.Parse(RepoData.Read("Plumbing/STING_TMV_STANDARDS.json"), out var errors);
            Assert.True(errors.Count == 0, string.Join("; ", errors));
            return f;
        }

        private static TmvCheck Tmv(string outlet, string scheme, bool? assisted, double setC, double measuredC = 0, bool healthcare = true)
            => WaterSafetyLimits.CheckTmv(Shipped(), outlet, scheme, assisted, healthcare, setC, measuredC);

        [Fact]
        public void ShippedFileParsesAndCoversEveryOutletUnderBothSchemes()
        {
            var f = Shipped();
            foreach (var scheme in WaterSafetyLimits.Schemes)
                foreach (var outlet in WaterSafetyLimits.Outlets)
                    Assert.Contains(f.OutletLimits, r => r.Scheme == scheme && r.Outlet == outlet && !r.Assisted);
            Assert.Equal("TMV3", f.HealthcareRequiredScheme);
        }

        [Fact]
        public void HealthcareBathAt44PassesAt45FailsUnassisted()
        {
            Assert.Equal(WaterCheckStatus.Pass, Tmv("BATH", "TMV3", false, 44).Status);
            Assert.Equal(WaterCheckStatus.Fail, Tmv("BATH", "TMV3", false, 45).Status);
        }

        [Fact]
        public void AssistedBathAllows46Not47()
        {
            Assert.Equal(WaterCheckStatus.Pass, Tmv("BATH", "TMV3", true, 46).Status);
            Assert.Equal(WaterCheckStatus.Fail, Tmv("BATH", "TMV3", true, 47).Status);
        }

        [Fact]
        public void BathBetween44And46WithAssistedUnknownIsNotChecked()
        {
            var c = Tmv("BATH", "TMV3", null, 45);
            Assert.Equal(WaterCheckStatus.NotChecked, c.Status);
            Assert.Contains("PLM_TMV_ASSISTED_BOOL", c.Reason);
            Assert.Equal(WaterCheckStatus.Fail, Tmv("BATH", "TMV3", null, 47).Status);
            Assert.Equal(WaterCheckStatus.Pass, Tmv("BATH", "TMV3", null, 43).Status);
        }

        [Fact]
        public void ShowerAbove41FailsAndBidetAbove38Fails()
        {
            Assert.Equal(WaterCheckStatus.Fail, Tmv("SHOWER", "TMV3", null, 42).Status);
            Assert.Equal(WaterCheckStatus.Pass, Tmv("SHOWER", "TMV3", null, 41).Status);
            Assert.Equal(WaterCheckStatus.Fail, Tmv("BIDET", "TMV3", null, 39).Status);
        }

        [Fact]
        public void MeasuredAboveNeverExceedFails()
        {
            Assert.Equal(WaterCheckStatus.Pass, Tmv("SHOWER", "TMV3", null, 41, 43).Status);
            Assert.Equal(WaterCheckStatus.Fail, Tmv("SHOWER", "TMV3", null, 41, 43.5).Status);
        }

        [Fact]
        public void UnknownOutletSchemeOrDataIsNotCheckedNeverPassed()
        {
            Assert.Equal(WaterCheckStatus.NotChecked, Tmv(null, "TMV3", null, 40).Status);
            Assert.Equal(WaterCheckStatus.NotChecked, Tmv("BATH", null, null, 40).Status);
            Assert.Equal(WaterCheckStatus.NotChecked,
                WaterSafetyLimits.CheckTmv(null, "BATH", "TMV3", false, true, 40, 0).Status);
            Assert.Equal(WaterCheckStatus.NotChecked, Tmv("BATH", "TMV3", false, 0, 0).Status);
        }

        [Fact]
        public void Tmv2OnHealthcarePremisesFailsButIsCheckedOutsideHealthcare()
        {
            Assert.Equal(WaterCheckStatus.Fail, Tmv("BASIN", "TMV2", null, 41).Status);
            Assert.Equal(WaterCheckStatus.Pass, Tmv("BASIN", "TMV2", null, 41, healthcare: false).Status);
            // TMV2 has no assisted-bath row: 44 applies even when assisted is recorded.
            Assert.Equal(WaterCheckStatus.Fail, Tmv("BATH", "TMV2", true, 46, healthcare: false).Status);
        }

        [Theory]
        [InlineData("WHB", "BASIN")]
        [InlineData("wash basin", "BASIN")]
        [InlineData("Bath", "BATH")]
        [InlineData("SINK", null)]
        [InlineData("", null)]
        public void OutletNormalisation(string raw, string expected)
            => Assert.Equal(expected, WaterSafetyLimits.NormaliseOutlet(raw));

        [Theory]
        [InlineData("TMV3", "TMV3")]
        [InlineData("tmv 2", "TMV2")]
        [InlineData("Type-3", "TMV3")]
        [InlineData("C", null)]
        public void SchemeNormalisation(string raw, string expected)
            => Assert.Equal(expected, WaterSafetyLimits.NormaliseScheme(raw));

        [Fact]
        public void HealthcareSpurIs3mAndBlendedIs2m()
        {
            var f = Shipped();
            Assert.Equal(3.0, WaterSafetyLimits.DeadLegLimitFor(f, true, false, false, true, 15, 15).LimitM);
            Assert.Equal(2.0, WaterSafetyLimits.DeadLegLimitFor(f, true, false, true, true, 15, 15).LimitM);
            Assert.Equal(2.0, WaterSafetyLimits.DeadLegLimitFor(f, false, false, true, true, 15, 15).LimitM);
        }

        [Theory]
        [InlineData(12, 20)]
        [InlineData(15, 12)]
        [InlineData(22, 12)]
        [InlineData(28, 8)]
        [InlineData(35, 3)]
        public void NonHealthcareHotLimitByOutsideDiameterCarriesVerify(double odMm, double expectedM)
        {
            var r = WaterSafetyLimits.DeadLegLimitFor(Shipped(), false, false, false, true, odMm, odMm);
            Assert.Equal(expectedM, r.LimitM);
            Assert.False(string.IsNullOrWhiteSpace(r.Verify));
        }

        [Fact]
        public void NonHealthcareColdBranchIsNotChecked()
        {
            var r = WaterSafetyLimits.DeadLegLimitFor(Shipped(), false, false, false, false, 15, 15);
            Assert.Null(r.LimitM);
            Assert.False(string.IsNullOrWhiteSpace(r.NotCheckedReason));
        }

        [Fact]
        public void OpenEndedBranchIsTwoDiameters()
        {
            var r = WaterSafetyLimits.DeadLegLimitFor(Shipped(), true, true, false, true, 22, 20);
            Assert.Equal(0.04, r.LimitM.Value, 6);
        }

        [Fact]
        public void MissingDataGivesNoLimit()
            => Assert.Null(WaterSafetyLimits.DeadLegLimitFor(null, true, false, false, true, 15, 15).LimitM);

        [Fact]
        public void OverrideMayOnlyTighten()
        {
            Assert.Equal(2.5, WaterSafetyLimits.TightenOnly(3.0, 2.5, out bool i1)); Assert.False(i1);
            Assert.Equal(3.0, WaterSafetyLimits.TightenOnly(3.0, 4.0, out bool i2)); Assert.True(i2);
            Assert.Equal(3.0, WaterSafetyLimits.TightenOnly(3.0, 0, out bool i3)); Assert.False(i3);
        }

        [Fact]
        public void InvalidFileIsRefusedWithReasons()
        {
            Assert.Null(WaterSafetyLimits.Parse("{\"outletLimits\":[]}", out var errors));
            Assert.NotEmpty(errors);
            Assert.Null(WaterSafetyLimits.Parse(null, out var e2));
            Assert.NotEmpty(e2);
        }
    }
}
