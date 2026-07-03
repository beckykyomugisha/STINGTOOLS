using StingTools.Core.Sustainability;
using Xunit;

namespace StingTools.Sustainability.Tests
{
    /// <summary>
    /// CA-3 — one carbon convention. The EDGE dashboard now LEADS with the A1-A3
    /// FOSSIL intensity (RICS/RIBA upfront headline), the same basis the BOQ panel
    /// reports (BOQ EmbodiedCarbonKg is fossil). Net (incl. biogenic credit) is a
    /// separate whole-life line. These pin the headline = fossil invariant.
    /// </summary>
    public class FossilHeadlineTests
    {
        [Fact]
        public void Fossil_Headline_Is_Fossil_Over_Area()
        {
            var m = new MaterialsRollupResult
            {
                FloorAreaM2 = 1000,
                TotalFossilCarbonKg = 300_000,   // 300 kgCO₂e/m² fossil headline
                TotalBiogenicCarbonKg = -50_000, // timber credit
                TotalCarbonKg = 250_000          // net (fossil + biogenic)
            };
            Assert.Equal(300, m.FossilCarbonIntensityKgM2, 3);
        }

        [Fact]
        public void Timber_Model_Fossil_Headline_Exceeds_Net()
        {
            // For a timber model the biogenic credit pulls NET below FOSSIL. The
            // dashboard headline (fossil) is the higher, BOQ-matching number; net is
            // the separate whole-life line.
            var m = new MaterialsRollupResult
            {
                FloorAreaM2 = 1000,
                TotalFossilCarbonKg = 300_000,
                TotalCarbonKg = 250_000
            };
            Assert.True(m.FossilCarbonIntensityKgM2 > m.CarbonIntensityKgM2,
                "fossil headline must exceed net for a biogenic-credited (timber) model");
            Assert.Equal(250, m.CarbonIntensityKgM2, 3); // net whole-life line
        }

        [Fact]
        public void No_Biogenic_Means_Fossil_Equals_Net()
        {
            // A concrete/steel-only model: no biogenic credit ⇒ the two surfaces
            // already agree (fossil == net), so the headline change is a no-op there.
            var m = new MaterialsRollupResult
            {
                FloorAreaM2 = 500,
                TotalFossilCarbonKg = 200_000,
                TotalCarbonKg = 200_000
            };
            Assert.Equal(m.CarbonIntensityKgM2, m.FossilCarbonIntensityKgM2, 6);
        }

        [Fact]
        public void Fossil_Headline_Matches_Boq_Fossil_Per_M2()
        {
            // The BOQ panel reports Σ EmbodiedCarbonKg (fossil) / GIFA. Model that as
            // a fossil total and assert the EDGE fossil headline lands on the same
            // kgCO₂e/m² — the CA-3 reconciliation the prompt asks for.
            double gifa = 1200;
            double boqFossilKg = 360_000;            // Σ BOQ EmbodiedCarbonKg (fossil)
            double boqFossilPerM2 = boqFossilKg / gifa;
            var m = new MaterialsRollupResult
            {
                FloorAreaM2 = gifa,
                TotalFossilCarbonKg = boqFossilKg,
                TotalCarbonKg = 330_000              // net differs, headline must not
            };
            Assert.Equal(boqFossilPerM2, m.FossilCarbonIntensityKgM2, 6);
        }
    }
}
