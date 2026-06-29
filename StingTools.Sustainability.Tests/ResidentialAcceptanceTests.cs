using StingTools.Core.Hvac.Loads;
using StingTools.Core.Sustainability;
using Xunit;
using Xunit.Abstractions;

namespace StingTools.Sustainability.Tests
{
    // WS K — acceptance: a 170 m² residential building resolves the Residential
    // profile (~5 occupants, not the office 17) and a believable dwelling EUI band,
    // not the office ~200.
    public class ResidentialAcceptanceTests
    {
        private readonly ITestOutputHelper _out;
        public ResidentialAcceptanceTests(ITestOutputHelper o) { _out = o; }

        private static ClimateMonthlySite Climate()
        {
            var s = new ClimateMonthlySite { Id = "temperate", AnnualGhiKwhM2Yr = 1200 };
            for (int m = 0; m < 12; m++) { s.MeanDbC[m] = 18; s.GhiKwhM2Day[m] = 3.3; s.MeanRhPct[m] = 65; }
            return s;
        }

        private static LoadZone Dwelling(LoadProfile profile, double areaM2)
        {
            var z = new LoadZone { Id = "house", Name = "house", FloorAreaM2 = areaM2, HeightM = 3 };
            profile.ApplyTo(z);
            z.OccupantCount = profile.OccupantCountFor(areaM2);   // density-derived
            // A representative envelope so conduction/solar are included.
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.ExteriorWall, AreaM2 = 120, UvalueWm2K = 0.3, OrientationDeg = 180 });
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.Window, AreaM2 = 25, UvalueWm2K = 1.6, SHGC = 0.5, OrientationDeg = 180 });
            z.Envelope.Add(new EnvelopeSegment { Kind = SegmentKind.Roof, AreaM2 = 170, UvalueWm2K = 0.2 });
            return z;
        }

        [Fact]
        public void Residential170m2_Resolves5Occupants_AndBelievableEui_NotOffice()
        {
            var lib = LoadProfileLibrary.FromJson(TestData.Read("STING_LOAD_PROFILES.json"));
            var baselines = GreenBaselineRegistry.LoadFromJson(TestData.Read("STING_GREEN_BASELINES.json"));

            // The use "residential" resolves the Residential profile (not Office).
            var resiProfile = lib.ResolveForUse("residential").Profile;
            Assert.Equal("Residential", resiProfile.Id);

            var resiZone = Dwelling(resiProfile, 170);
            Assert.Equal(5, resiZone.OccupantCount);   // 170 / 35 ≈ 5, NOT office 17

            var resiBaseline = baselines.Resolve("*", "4A", "residential").Baseline;
            var resi = AnnualEnergyEstimator.Estimate(new[] { resiZone }, Climate(), resiBaseline, 3.0);
            _out.WriteLine($"Residential 170 m²: {resiZone.OccupantCount} occ, EUI {resi.DesignEuiKwhM2Yr:F1} kWh/m²·yr");

            Assert.True(resi.Computed);
            // Believable dwelling band — well below the office ~200 the old bug produced.
            Assert.InRange(resi.DesignEuiKwhM2Yr, 30, 150);

            // Same geometry as an OFFICE would resolve 17 occupants and a higher EUI.
            var officeProfile = lib.ResolveForUse("office").Profile;
            var officeZone = Dwelling(officeProfile, 170);
            Assert.Equal(17, officeZone.OccupantCount);
            var office = AnnualEnergyEstimator.Estimate(new[] { officeZone }, Climate(),
                baselines.Resolve("*", "4A", "office").Baseline, 3.0);
            _out.WriteLine($"Office (same shell): {officeZone.OccupantCount} occ, EUI {office.DesignEuiKwhM2Yr:F1} kWh/m²·yr");

            Assert.True(resi.DesignEuiKwhM2Yr < office.DesignEuiKwhM2Yr,
                "residential EUI must be below the office EUI on the same shell");
        }
    }
}
