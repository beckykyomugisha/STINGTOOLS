using System;
using StingTools.Core.Evm;
using Xunit;

namespace StingTools.Cost.Tests
{
    /// <summary>PM-1 — EVM EAC/ETC/VAC regression. The audit found EAC collapsing
    /// to 0 at CPI=0 (project start), making ETC negative and VAC falsely "on
    /// budget" exactly when the forecast matters.</summary>
    public class EvmPeriodTests
    {
        [Fact]
        public void Eac_DoesNotCollapse_WhenCpiZero_AtProjectStart()
        {
            // AC spent, nothing earned yet → CPI = 0.
            var p = new EvmPeriod { Bac = 1_000_000, Bcws = 100_000, Bcwp = 0, Acwp = 50_000 };
            Assert.Equal(0, p.Cpi);
            // Budget-rate fallback: EAC = AC + (BAC − EV) = 50,000 + 1,000,000 = 1,050,000.
            Assert.Equal(1_050_000, p.Eac, 2);
            // ETC must stay POSITIVE (the old bug made it −AC).
            Assert.True(p.Etc > 0, $"ETC should be positive, was {p.Etc}");
            Assert.Equal(1_000_000, p.Etc, 2);   // EAC − AC
            // VAC reflects the real overrun forecast, not a false 0.
            Assert.Equal(-50_000, p.Vac, 2);      // BAC − EAC
        }

        [Fact]
        public void Eac_CpiTypical_EqualsBacOverCpi_ForCpiPositive()
        {
            // Overrun: earned 400k for 500k spent → CPI = 0.8.
            var p = new EvmPeriod { Bac = 1_000_000, Bcws = 500_000, Bcwp = 400_000, Acwp = 500_000 };
            Assert.Equal(0.8, p.Cpi, 4);
            // CPI-typical EAC = AC + (BAC−EV)/CPI = 500,000 + 600,000/0.8 = 1,250,000
            // (== BAC/CPI = 1,000,000/0.8 = 1,250,000).
            Assert.Equal(1_250_000, p.Eac, 2);
            Assert.Equal(1_000_000 / 0.8, p.Eac, 2);
            Assert.Equal(750_000, p.Etc, 2);      // EAC − AC
            Assert.Equal(-250_000, p.Vac, 2);     // BAC − EAC (overrun)
        }

        [Fact]
        public void EacScheduleBlended_UsesCpiTimesSpi()
        {
            var p = new EvmPeriod { Bac = 1_000_000, Bcws = 500_000, Bcwp = 400_000, Acwp = 500_000 };
            // CPI = 0.8, SPI = 400k/500k = 0.8 → factor 0.64.
            Assert.Equal(0.8, p.Spi, 4);
            // EAC_blended = AC + (BAC−EV)/(CPI×SPI) = 500,000 + 600,000/0.64 = 1,437,500.
            Assert.Equal(1_437_500, p.EacScheduleBlended, 2);
        }

        [Fact]
        public void EacBudgetRate_IsAcPlusRemainingAtBudget()
        {
            var p = new EvmPeriod { Bac = 1_000_000, Bcws = 500_000, Bcwp = 400_000, Acwp = 500_000 };
            Assert.Equal(1_100_000, p.EacBudgetRate, 2);   // 500,000 + (1,000,000 − 400,000)
        }

        [Fact]
        public void Cpi_And_Spi_GuardZeroDenominators()
        {
            var p = new EvmPeriod { Bac = 1_000_000, Bcws = 0, Bcwp = 0, Acwp = 0 };
            Assert.Equal(0, p.Cpi);   // AC=0 → guarded
            Assert.Equal(0, p.Spi);   // BCWS=0 → guarded
            // EAC at CPI=0 with AC=0 → AC + (BAC−EV) = 1,000,000.
            Assert.Equal(1_000_000, p.Eac, 2);
        }

        [Fact]
        public void OnBudget_EacEqualsBac()
        {
            var p = new EvmPeriod { Bac = 1_000_000, Bcws = 500_000, Bcwp = 500_000, Acwp = 500_000 };
            Assert.Equal(1.0, p.Cpi, 4);
            Assert.Equal(1_000_000, p.Eac, 2);
            Assert.Equal(0, p.Vac, 2);
        }
    }
}
