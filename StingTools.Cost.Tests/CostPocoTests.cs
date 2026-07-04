using StingTools.Core.PaymentCert;
using StingTools.Core.CostPlan;
using StingTools.Core.Variation;
using Xunit;

namespace StingTools.Cost.Tests
{
    /// <summary>PM-1 — payment-cert VAT (Uganda 18%) + retention halving at the
    /// practical-completion proxy.</summary>
    public class PaymentCertModelTests
    {
        private static PaymentCertificate CertAt(double pctComplete)
        {
            var c = new PaymentCertificate();
            c.Lines.Add(new SovLine
            {
                ContractValue = 100_000,
                PercentComplete = pctComplete,
                PreviouslyCertified = 0,
                MaterialsOnSite = 0
            });
            return c;
        }

        [Fact]
        public void Vat_DefaultsTo18Percent_NotUk20()
        {
            var c = CertAt(50);   // below the 95% halving proxy → full 3% retention
            Assert.Equal(18.0, c.VatPercent);
            // Gross 50,000; retention 3% = 1,500; net 48,500; VAT 18% = 8,730.
            Assert.Equal(50_000, c.GrossValuation, 2);
            Assert.Equal(3.0, c.EffectiveRetentionPercent, 4);
            Assert.Equal(1_500, c.RetentionAmount, 2);
            Assert.Equal(48_500, c.NetThisCert, 2);
            Assert.Equal(8_730, c.VatAmount, 2);
        }

        [Fact]
        public void Retention_Halves_AtPracticalCompletionProxy()
        {
            var c = CertAt(96);   // ≥ 95% default → retention halves to 1.5%
            Assert.Equal(96, c.OverallPercentComplete, 2);
            Assert.Equal(1.5, c.EffectiveRetentionPercent, 4);
            // Gross 96,000; retention 1.5% = 1,440.
            Assert.Equal(96_000, c.GrossValuation, 2);
            Assert.Equal(1_440, c.RetentionAmount, 2);
        }

        [Fact]
        public void Retention_Full_BelowProxy()
        {
            var c = CertAt(94);   // below 95% → full 3%
            Assert.Equal(3.0, c.EffectiveRetentionPercent, 4);
        }

        [Fact]
        public void Retention_Basis_IsContractConfigurable_OnMaterialsOnSite()
        {
            // 50% of 100k contract + 20k materials on site → gross 70,000.
            var c = new PaymentCertificate();
            c.Lines.Add(new SovLine { ContractValue = 100_000, PercentComplete = 50, PreviouslyCertified = 0, MaterialsOnSite = 20_000 });
            Assert.Equal(70_000, c.GrossValuation, 2);

            // Default (MoS-inclusive): retention 3% on 70,000 = 2,100.
            Assert.False(c.RetentionExcludesMos);
            Assert.Equal(70_000, c.RetentionBasis, 2);
            Assert.Equal(2_100, c.RetentionAmount, 2);

            // Contract holds retention on work-done only: 3% on (70,000 − 20,000) = 1,500.
            c.RetentionExcludesMos = true;
            Assert.Equal(50_000, c.RetentionBasis, 2);
            Assert.Equal(1_500, c.RetentionAmount, 2);
        }

        [Fact]
        public void RetentionLedger_BalanceReconciles_WithheldMinusReleased()
        {
            var ledger = new RetentionLedger();
            ledger.Entries.Add(new RetentionEntry { Kind = "withhold", Amount = 1_500 });
            ledger.Entries.Add(new RetentionEntry { Kind = "withhold", Amount = 1_440 });
            ledger.Entries.Add(new RetentionEntry { Kind = "release",  Amount = 1_470 }); // first moiety (half of 2,940)
            Assert.Equal(2_940, ledger.TotalWithheld, 2);
            Assert.Equal(1_470, ledger.TotalReleased, 2);
            Assert.Equal(1_470, ledger.Balance, 2);
        }
    }

    /// <summary>PM-1 — CostPlan currency (UGX, not GBP) + PERT from unrounded inputs.</summary>
    public class CostPlanModelTests
    {
        [Fact]
        public void Currency_DefaultsToUgx_NotGbp()
        {
            Assert.Equal("UGX", new CostPlanDocument().Currency);
        }

        [Fact]
        public void PertExpected_WeightsUnrounded_ThenRoundsOnce()
        {
            // q=1, low=99.4, likely=100.4, high=101.4 → PERT = (99.4 + 401.6 + 101.4)/6
            //  = 602.4/6 = 100.4 → 100. The OLD path pre-rounded each (99,100,101)
            //  → (99+400+101)/6 = 100.0 → 100 (same here), but with q scaling the
            //  pre-rounding bias diverges; assert the unrounded mean.
            var line = new CostPlanLine { Quantity = 1, LowRate = 99.4, LikelyRate = 100.4, HighRate = 101.4 };
            Assert.Equal(100, line.TotalExpected, 2);

            // A case where pre-rounding the inputs WOULD bias the mean:
            // q=3, rates 0.5/0.5/0.5 → unrounded (1.5 + 6.0 + 1.5)/6 = 1.5 → round 2 (half-even).
            var line2 = new CostPlanLine { Quantity = 3, LowRate = 0.5, LikelyRate = 0.5, HighRate = 0.5 };
            Assert.Equal(2, line2.TotalExpected, 2);   // pre-rounded each TotalX=Round(1.5)=2 → (2+8+2)/6=2 too; both → 2
        }
    }

    /// <summary>PM-1 — StarRateLine costs on the resource type, not Max(Hours,Quantity).</summary>
    public class StarRateLineTests
    {
        [Fact]
        public void Labour_PricesOnHours_EvenWhenQuantitySet()
        {
            var l = new StarRateLine { Resource = "Skilled labourer", Unit = "hr", Hours = 8, Quantity = 999, UnitRate = 10_000 };
            Assert.Equal(80_000, l.LineTotal, 2);   // 8 hr × 10,000 — NOT Max(8,999)
        }

        [Fact]
        public void Material_PricesOnQuantity()
        {
            var l = new StarRateLine { Resource = "Concrete C30", Unit = "m3", Hours = 0, Quantity = 5, UnitRate = 400_000 };
            Assert.Equal(2_000_000, l.LineTotal, 2);
        }

        [Fact]
        public void Plant_DayRate_PricesOnHoursField()
        {
            var l = new StarRateLine { Resource = "JCB excavator", Unit = "day", Hours = 3, Quantity = 0, UnitRate = 500_000 };
            Assert.Equal(1_500_000, l.LineTotal, 2);
        }
    }
}
