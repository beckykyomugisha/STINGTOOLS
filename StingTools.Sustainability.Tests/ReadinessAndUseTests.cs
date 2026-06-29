using System.Collections.Generic;
using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS I1 — location + building-use must be resolved, not silently defaulted to
    // office/4A. The use resolver derives use from model signals; the readiness gate
    // blocks (with a banner) when location or use is unset.
    public class ReadinessAndUseTests
    {
        // ── Building-use resolver ─────────────────────────────────────────────
        [Theory]
        [InlineData("Tendo Main house", "residential")]
        [InlineData("Master Bedroom", "residential")]
        [InlineData("Open Plan Office", "office")]
        [InlineData("In-Patient Ward", "healthcare")]
        [InlineData("Year 3 Classroom", "education")]
        [InlineData("Cold Storage Warehouse", "warehouse")]
        public void MapText_DerivesUseFromModelText(string text, string expected)
            => Assert.Equal(expected, BuildingUseResolver.MapText(text));

        [Fact]
        public void MapText_UnknownText_ReturnsNull()
            => Assert.Null(BuildingUseResolver.MapText("Plot 47"));

        [Fact]
        public void Resolve_ResidentialHouse_DerivesResidential_NotOffice()
        {
            // A residential house model must NOT come back as office.
            var signals = new List<(string, string)>
            {
                ("model", "Tendo Main house"),
                ("model", "Sitting Room"),
                ("model", "Master Bedroom"),
            };
            var r = BuildingUseResolver.Resolve(signals, BuildingUseCatalog.CommonUses);
            Assert.True(r.Found);
            Assert.Equal("residential", r.Use);
            Assert.NotEqual("office", r.Use);
        }

        [Fact]
        public void Resolve_NoMappableSignals_IsUnset_NotOffice()
        {
            var signals = new List<(string, string)> { ("model", "Plot 47"), ("model", "Area A") };
            var r = BuildingUseResolver.Resolve(signals, BuildingUseCatalog.CommonUses);
            Assert.False(r.Found);
            Assert.Equal("unset", r.Source);
            Assert.Equal("", r.Use);   // never office
        }

        [Fact]
        public void Resolve_EmptySignals_IsUnset()
        {
            var r = BuildingUseResolver.Resolve(null, BuildingUseCatalog.CommonUses);
            Assert.False(r.Found);
            Assert.Equal("unset", r.Source);
        }

        // ── Readiness gate ────────────────────────────────────────────────────
        [Fact]
        public void Readiness_LocationAndUseSet_IsReady()
        {
            var r = SustainReadiness.Evaluate(locationSet: true, useSet: true, occupancySet: true, fixturesModelled: true);
            Assert.True(r.Ready);
            Assert.True(r.Complete);
            Assert.Equal("", r.Banner);
        }

        [Fact]
        public void Readiness_NoLocationOrUse_Blocks_WithBanner()
        {
            var r = SustainReadiness.Evaluate(locationSet: false, useSet: false, occupancySet: false, fixturesModelled: false);
            Assert.False(r.Ready);
            Assert.Contains("generic proxy, not your project", r.Banner);
            Assert.Contains("location", r.Banner);
            Assert.Contains("building use", r.Banner);
        }

        [Fact]
        public void Readiness_LocationSetUseUnset_StillBlocks()
        {
            var r = SustainReadiness.Evaluate(locationSet: true, useSet: false, occupancySet: true, fixturesModelled: true);
            Assert.False(r.Ready);
            Assert.Contains("building use", r.Banner);
        }

        [Fact]
        public void Readiness_ReadyButIncomplete_SoftWarning()
        {
            var r = SustainReadiness.Evaluate(locationSet: true, useSet: true, occupancySet: false, fixturesModelled: false);
            Assert.True(r.Ready);
            Assert.False(r.Complete);
            Assert.Contains("Indicative", r.Banner);
            Assert.DoesNotContain("not your project", r.Banner);
        }

        [Fact]
        public void Setup_UseExplicit_ChangesContentHash()
        {
            var a = SustainProjectSetup.CreateDefault(170, 17);
            var b = SustainProjectSetup.CreateDefault(170, 17);
            b.UseExplicit = true;
            Assert.NotEqual(a.ContentHash(), b.ContentHash());
        }

        [Fact]
        public void CreateDefault_UseIsNotExplicit_SoFreshModelBlocks()
        {
            // The seeded "office" on a fresh setup must NOT count as a chosen use.
            var s = SustainProjectSetup.CreateDefault(170, 17);
            Assert.False(s.UseExplicit);
        }
    }
}
