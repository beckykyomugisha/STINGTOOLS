using System.Linq;
using StingTools.Core.Hvac.Loads;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    // WS L3 — ONE outdoor-air basis across all profiles: CIBSE all-in per-person
    // (per-area 0 for occupied spaces; area-based only for unoccupied). No mixing of
    // CIBSE all-in per-person WITH ASHRAE per-area adders (that double-counts).
    public class VentilationBasisTests
    {
        private static LoadProfileLibrary Lib()
            => LoadProfileLibrary.FromJson(TestData.Read("STING_LOAD_PROFILES.json"));

        [Fact]
        public void OccupiedProfiles_HaveZeroPerArea_NotADoubleCount()
        {
            foreach (var p in Lib().ById.Values)
                if (p.OaLpsPerPerson > 0)
                    Assert.Equal(0, p.OaLpsPerM2, 3);   // all-in per-person carries the OA
        }

        [Fact]
        public void UnoccupiedProfiles_KeepAreaBasedVentilation()
        {
            // Parking has no per-person rate (occ 0) — its ventilation is area-driven.
            var parking = Lib().ById["Parking"];
            Assert.Equal(0, parking.OaLpsPerPerson, 3);
            Assert.True(parking.OaLpsPerM2 > 0);
        }

        [Fact]
        public void OfficeOa_IsSane_AllInPerPerson_NotDoubled()
        {
            var office = Lib().ResolveForUse("office").Profile;
            var z = new LoadZone { Id = "z", FloorAreaM2 = 1000, HeightM = 3 };
            office.ApplyTo(z);
            z.OccupantCount = office.OccupantCountFor(1000);   // 1000 / 10 = 100 people

            // CIBSE all-in: 100 × 10 + 1000 × 0 = 1000 L/s — NOT the old 1300 (≈ +30%
            // double-count from a stray per-area adder).
            Assert.Equal(1000, z.OaLs, 0);
        }

        [Fact]
        public void EveryProfileSource_StatesTheOaBasis()
        {
            foreach (var p in Lib().ById.Values)
                Assert.Contains("OA:", p.Source);
        }
    }
}
