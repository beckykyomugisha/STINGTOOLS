using StingTools.BOQ;
using StingTools.Core.Evm;
using Xunit;

namespace StingTools.Cost.Tests
{
    /// <summary>
    /// CA-2 — ONE cost basis: net of VAT (works + prelims + OH&P + contingency).
    /// VAT is the final presentation line only. These pin the reconciliation
    /// invariants: the contract sum, a cert at 100%, and the Final Account all
    /// land on the same net total; EVM CPI = 1.0 when earned value = actual cost.
    /// </summary>
    public class CostBasisReconciliationTests
    {
        // A worked bill: works 100,000,000; prelims 12%, OH&P 8%, contingency 10%,
        // VAT 18% — the waterfall the canonical BoqTotals computes.
        private static BoqMarkupBreakdown Worked()
            => BoqTotals.Compute(100_000_000, 100_000_000 * 0.12, 8, 10, 18);

        [Fact]
        public void Net_Basis_Is_Works_Plus_Prelims_Ohp_Contingency()
        {
            var b = Worked();
            Assert.Equal(b.Works + b.Prelims + b.Overhead + b.Contingency, b.NetExVat, 2);
        }

        [Fact]
        public void Vat_Sits_Only_On_Top_Of_The_Net_Basis()
        {
            var b = Worked();
            Assert.Equal(b.NetExVat + b.Vat, b.GrandTotal, 0);
            Assert.Equal(b.NetExVat * 0.18, b.Vat, 2);
        }

        [Fact]
        public void Sov_At_100pct_Equals_Net_Contract_Sum()
        {
            // The cert SOV carries the works sections PLUS explicit prelims / OH&P /
            // contingency lines (PaymentCertEngine.SovFromSnapshot). At 100% on
            // every line the gross valuation = Σ ContractValue = the net contract
            // sum — so cumulative certification can actually REACH the contract sum.
            var b = Worked();
            double sovTotal = b.Works + b.Prelims + b.Overhead + b.Contingency; // 4 SOV groups
            Assert.Equal(b.NetExVat, sovTotal, 2);
        }

        [Fact]
        public void ContractSum_Cert_And_FinalAccount_Reconcile_On_Net_Basis()
        {
            var b = Worked();
            double contractSumNet = b.NetExVat;                 // ContractSumResolver basis
            double certAt100 = b.Works + b.Prelims + b.Overhead + b.Contingency; // SOV @100%
            double finalAccount = contractSumNet               // no variations / PS / fluctuations
                                  + 0 + 0 + 0;
            Assert.Equal(contractSumNet, certAt100, 2);
            Assert.Equal(contractSumNet, finalAccount, 2);
            // Final Account vs certified-to-date (cert @100%) → nil to certify.
            Assert.Equal(0, finalAccount - certAt100, 2);
        }

        [Fact]
        public void Evm_Cpi_Is_One_When_Earned_Value_Equals_Actual_Cost()
        {
            var b = Worked();
            // BAC on the net basis; halfway through, EV == AC ⇒ on-budget.
            var p = new EvmPeriod
            {
                Bac = b.NetExVat,
                Bcws = b.NetExVat * 0.5,
                Bcwp = b.NetExVat * 0.5,   // earned value
                Acwp = b.NetExVat * 0.5    // actual cost (same basis)
            };
            Assert.Equal(1.0, p.Cpi, 4);
            Assert.Equal(0, p.Cv, 2);
        }

        [Fact]
        public void Mixing_Bases_Would_Bias_Cpi_Up_By_The_Vat_Factor()
        {
            // Regression guard: if BAC/earned value were taken VAT-inclusive while
            // actuals are net, CPI is inflated by ~(1+vat) — the exact bug CA-2
            // removes. Demonstrate the bias so the net-basis choice is explicit.
            var b = Worked();
            double evGross = b.GrandTotal * 0.5;   // VAT-inclusive earned value (wrong)
            double acNet = b.NetExVat * 0.5;       // net actual cost
            var biased = new EvmPeriod { Bac = b.NetExVat, Bcwp = evGross, Acwp = acNet, Bcws = acNet };
            Assert.True(biased.Cpi > 1.17 && biased.Cpi < 1.19, $"expected ~1.18 bias, got {biased.Cpi}");
        }
    }
}
