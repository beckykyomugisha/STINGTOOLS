using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Hvac.Loads;
using StingTools.Core.Sustainability;
using Xunit;
using Xunit.Abstractions;

namespace StingTools.Sustainability.Tests
{
    // WS N — the building-use split-brain. One precedence rule: user-explicit >
    // model-derived > seed default. A residential model with the SETUP building-use at
    // its non-explicit "(auto-detect)" default must resolve "residential" for BOTH
    // occupancy and energy (occ ~5, EUI ~110-120), not the seeded office (occ 17).
    public class BuildingUseExplicitTests
    {
        private readonly ITestOutputHelper _out;
        public BuildingUseExplicitTests(ITestOutputHelper o) { _out = o; }

        private static LoadProfileLibrary Lib()
            => LoadProfileLibrary.FromJson(TestData.Read("STING_LOAD_PROFILES.json"));

        private static ClimateMonthlySite Bangui()
        {
            var s = new ClimateMonthlySite { Id = "bangui", AnnualGhiKwhM2Yr = 1850 };
            for (int m = 0; m < 12; m++) { s.MeanDbC[m] = 26.5; s.GhiKwhM2Day[m] = 5.0; s.MeanRhPct[m] = 75; }
            return s;
        }

        // Mirrors the engine: when the use is NOT explicit and a model use resolves,
        // the dominant zone's BuildingUse is overwritten with the model-resolved use
        // (SustainabilityEngine.Compute lines 140-145).
        private static string EffectiveUse(SustainProjectSetup setup, BuildingUseResolution resolved)
        {
            if (resolved.Found && resolved.Source == "model"
                && setup.Zones.Count > 0 && !setup.UseExplicit)
                return resolved.Use;
            return setup.DominantBuildingUse;
        }

        [Fact]
        public void ModelResolvesResidential_FromRoomProgram()
        {
            var r = BuildingUseResolver.Resolve(
                new List<(string, string)> { ("model", "Apartment Block") }, BuildingUseCatalog.CommonUses);
            Assert.True(r.Found);
            Assert.Equal("residential", r.Use);
            Assert.Equal("model", r.Source);
        }

        [Fact]
        public void NonExplicitDefault_ResolvesResidential_ForOccupancyAndEnergy_NotOffice()
        {
            var lib = Lib();
            var baselines = GreenBaselineRegistry.LoadFromJson(TestData.Read("STING_GREEN_BASELINES.json"));

            // SETUP at its default: combo "(auto-detect)" → UseExplicit=false; the seeded
            // zone use is the harmless "office" placeholder (ReadSetupForm behaviour).
            var setup = SustainProjectSetup.CreateDefault(170, 0);
            setup.Zones[0].BuildingUse = "office";
            setup.UseExplicit = false;
            setup.OccupancyExplicit = false;

            // Model says residential.
            var resolved = BuildingUseResolver.Resolve(
                new List<(string, string)> { ("model", "Dwelling") }, BuildingUseCatalog.CommonUses);
            string use = EffectiveUse(setup, resolved);
            Assert.Equal("residential", use);   // not the office placeholder

            // Occupancy from the resolved use's profile density → ~5 (not office 17).
            var profile = lib.ResolveForUse(use).Profile;
            Assert.Equal("Residential", profile.Id);
            int zoneDerived = profile.OccupantCountFor(170);
            Assert.Equal(5, zoneDerived);
            var occ = SustainOccupancy.Resolve(setup.TotalOccupancy, zoneDerived, setup.OccupancyExplicit);
            Assert.Equal(5, occ.Occupancy);
            Assert.Equal("model", occ.Source);

            // Energy uses the SAME resolved use + occupancy → believable dwelling EUI.
            var z = new LoadZone { Id = "z", FloorAreaM2 = 170, HeightM = 3 };
            profile.ApplyTo(z);
            z.OccupantCount = zoneDerived;
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.ExteriorWall, AreaM2 = 120, UvalueWm2K = 0.4 });
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.Window, AreaM2 = 25, UvalueWm2K = 1.6, SHGC = 0.5, OrientationDeg = 180 });
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.Roof, AreaM2 = 170, UvalueWm2K = 0.3 });
            var energy = AnnualEnergyEstimator.Estimate(new[] { z }, Bangui(),
                baselines.Resolve("*", "1A", "residential").Baseline, 3.2);
            _out.WriteLine($"Non-explicit residential: occ {energy.Occupancy}, EUI {energy.DesignEuiKwhM2Yr:F1}");
            Assert.Equal(5, energy.Occupancy);
            Assert.InRange(energy.DesignEuiKwhM2Yr, 110, 120);
        }

        [Fact]
        public void ExplicitOffice_StaysOffice_Occ17()
        {
            var lib = Lib();
            var setup = SustainProjectSetup.CreateDefault(170, 0);
            setup.Zones[0].BuildingUse = "office";
            setup.UseExplicit = true;   // the user EXPLICITLY picked office

            // Model says residential, but the explicit pick wins (precedence rule).
            var resolved = BuildingUseResolver.Resolve(
                new List<(string, string)> { ("model", "Dwelling") }, BuildingUseCatalog.CommonUses);
            string use = EffectiveUse(setup, resolved);
            Assert.Equal("office", use);

            int occ = lib.ResolveForUse(use).Profile.OccupantCountFor(170);
            Assert.Equal(17, occ);   // office density honoured because the user chose it
        }

        [Fact]
        public void UseChange_ReKeysTheRun()
        {
            // WS N4 — an explicit use change re-keys the cache (no stale carry-over).
            var office = SustainProjectSetup.CreateDefault(170, 0); office.Zones[0].BuildingUse = "office"; office.UseExplicit = true;
            var resi   = SustainProjectSetup.CreateDefault(170, 0); resi.Zones[0].BuildingUse = "residential"; resi.UseExplicit = true;
            Assert.NotEqual(office.ContentHash(), resi.ContentHash());
        }
    }
}
