using StingTools.Core.Electrical;
using StingTools.Standards.NEC2023;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DSCH-30 follow-up — NEC conductor + OCPD selection with the 240.4(D) small-conductor
    /// limit. The old CalculateNec capped the breaker at the limit AFTER choosing it, so a
    /// 17 A continuous load (21.25 A sizing current) on 12 AWG got a 20 A breaker. Now the
    /// conductor is upsized until the device it needs is permitted. Limits (checked 2026-10-02 against
    /// NFPA 70-2023 240.4(D), as reproduced in NFPA PI 705-NFPA 70-2023): Cu 14/12/10 AWG 15/20/30 A; Al 12/10 AWG 15/25 A.
    /// Ampacities: Table 310.16 75 °C, 30 °C ambient, ≤ 3 CCC (no correction).
    /// </summary>
    public class NecConductorSelectionTests
    {
        private static readonly int[] Nec =
        {
            10, 15, 20, 25, 30, 35, 40, 45, 50, 60, 70, 80, 90, 100, 110, 125, 150, 175, 200,
            225, 250, 300, 350, 400, 450, 500, 600, 700, 800, 1000, 1200, 1600, 2000, 2500,
            3000, 4000, 5000, 6000
        };

        private static NecConductorPick Cu(double ib, bool cont)
            => NecConductorSelection.Pick(ib, cont, ConductorMaterial.Copper, 30, 2, Nec);

        [Fact]
        public void Twelve_AWG_needing_25A_is_upsized_to_ten_not_capped_at_20A()
        {
            var p = Cu(17, cont: true);            // 21.25 A sizing current
            Assert.Equal("10", p.Size);
            Assert.Equal(25, p.Device.ProposedA);
            Assert.Contains(p.UpsizedPast, s => s.StartsWith("12 AWG"));
        }

        [Fact]
        public void Fourteen_AWG_needing_20A_is_upsized_to_twelve()
        {
            var p = Cu(14, cont: true);            // 17.5 A → 20 A device > 15 A limit
            Assert.Equal("12", p.Size);
            Assert.Equal(20, p.Device.ProposedA);
        }

        [Fact]
        public void Fourteen_AWG_within_its_limit_is_kept()
        {
            var p = Cu(12, cont: false);
            Assert.Equal("14", p.Size);
            Assert.Equal(15, p.Device.ProposedA);
            Assert.Empty(p.UpsizedPast);
        }

        [Fact]
        public void Ten_AWG_needing_35A_is_upsized_to_eight()
        {
            var p = Cu(26, cont: true);            // 32.5 A → 35 A device > 30 A limit
            Assert.Equal("8", p.Size);
            Assert.Equal(35, p.Device.ProposedA);
        }

        [Fact]
        public void Aluminium_uses_its_own_240_4D_limits()
        {
            var ok = NecConductorSelection.Pick(22, false, ConductorMaterial.Aluminum, 30, 2, Nec);
            Assert.Equal("10", ok.Size);           // 30 A ampacity, 25 A device = Al 10 AWG limit
            Assert.Equal(25, ok.Device.ProposedA);
            var up = NecConductorSelection.Pick(26, false, ConductorMaterial.Aluminum, 30, 2, Nec);
            Assert.Equal("8", up.Size);            // 30 A device > 25 A Al limit
            Assert.Equal(30, up.Device.ProposedA);
        }

        [Fact]
        public void Device_is_never_below_the_sizing_current_nor_above_the_small_conductor_limit()
        {
            foreach (bool cont in new[] { false, true })
                for (double ib = 1; ib <= 300; ib += 0.5)
                {
                    var p = Cu(ib, cont);
                    if (p.Size == null) continue;
                    double sizing = cont ? ib * 1.25 : ib;
                    Assert.True(p.Device.ProposedA >= sizing, $"Ib {ib} cont {cont}: {p.Device.ProposedA} A < {sizing} A");
                    int limit = NECStandards.GetSmallConductorMaxOcpd(p.Size, ConductorMaterial.Copper);
                    Assert.True(limit == 0 || p.Device.ProposedA <= limit, $"Ib {ib}: {p.Size} AWG got {p.Device.ProposedA} A");
                    Assert.True(p.AmpacityA >= sizing);
                }
        }

        [Theory]
        [InlineData("14", 15)]
        [InlineData("12", 20)]
        [InlineData("10", 30)]
        [InlineData("8", 0)]     // not a 240.4(D) size; the old table said 40 A (below its 50 A ampacity)
        [InlineData("4/0", 0)]
        public void Small_conductor_limits_copper(string size, int expected)
            => Assert.Equal(expected, NECStandards.GetSmallConductorMaxOcpd(size, ConductorMaterial.Copper));

        [Theory]
        [InlineData("12", 15)]
        [InlineData("10", 25)]
        [InlineData("8", 0)]
        public void Small_conductor_limits_aluminium(string size, int expected)
            => Assert.Equal(expected, NECStandards.GetSmallConductorMaxOcpd(size, ConductorMaterial.Aluminum));

        [Fact]
        public void Breaker_on_an_existing_12_AWG_above_20A_is_blocked_by_240_4D()
        {
            // What the Breaker Sizer does with a circuit's recorded wire size: 22 A on 12 AWG
            // Cu (25 A ampacity) → 25 A passes 240.4(B) on ampacity but not 240.4(D).
            var sel = ProtectiveDeviceSelection.Select(22, true, false, Nec, izA: 25);
            Assert.False(sel.Blocked);
            Assert.True(NecConductorSelection.ApplySmallConductorLimit(sel, "12", ConductorMaterial.Copper));
            Assert.True(sel.Blocked);
            Assert.Contains("240.4(D)", sel.Note);
            // 8 AWG has no 240.4(D) limit: untouched.
            var big = ProtectiveDeviceSelection.Select(42, true, false, Nec, izA: 50);
            Assert.False(NecConductorSelection.ApplySmallConductorLimit(big, "8", ConductorMaterial.Copper));
            Assert.False(big.Blocked);
        }

        [Fact]
        public void Eight_AWG_is_no_longer_capped_below_its_ampacity()
        {
            // 42 A: 8 AWG (50 A) with a 45 A device. The old table capped 8 AWG at 40 A < 42 A.
            var p = Cu(42, cont: false);
            Assert.Equal("8", p.Size);
            Assert.Equal(45, p.Device.ProposedA);
        }
    }
}
