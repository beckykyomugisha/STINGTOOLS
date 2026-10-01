using System;
using StingTools.Core.Electrical;
using StingTools.Standards.NEC2023;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// The NEC 2023 tables held in NECStandards.cs other than 310.16, written from the NFPA
    /// text — not from the code. Each is the 2023 table reproduced unmarked by NFPA as the
    /// base of a 2026-cycle public input (docinfofiles.nfpa.org, First Draft reports):
    /// <list type="bullet">
    /// <item>Table 310.15(B)(1)(1) — PIs 1309 / 960 [310.15(B)(2)], CMP-6 PI report pp. 54, 59/307
    /// (identical), and FR base text in the CMP-6 First Revision report.</item>
    /// <item>Table 310.15(C)(1) — PI 3958 [310.15(C)(1)], CMP-6 PI report p. 63/307.</item>
    /// <item>Table 250.122 — PI 3222 [250.122], CMP-5 PI report p. 302/383.</item>
    /// <item>Table 250.66 — PI 1748 [250.66], CMP-5 PI report p. 192/383.</item>
    /// <item>Chapter 9 Table 1 — CMP-8 First Revision report p. 161/163.</item>
    /// <item>Chapter 9 Table 8, circular mils — PI 2158 / 259, CMP-6 PI report pp. 301, 305/307.</item>
    /// </list>
    /// </summary>
    public class NecChapter9AndFactorTablesTests
    {
        // ── Table 310.15(B)(1)(1), ambient correction, 30 °C base ───────────────

        [Theory]
        //           ambient  60°C   75°C   90°C   (NaN = "—" in the table)
        [InlineData(10.0, 1.29, 1.20, 1.15)]
        [InlineData(11.0, 1.22, 1.15, 1.12)]
        [InlineData(15.0, 1.22, 1.15, 1.12)]
        [InlineData(16.0, 1.15, 1.11, 1.08)]
        [InlineData(20.0, 1.15, 1.11, 1.08)]
        [InlineData(21.0, 1.08, 1.05, 1.04)]
        [InlineData(25.0, 1.08, 1.05, 1.04)]
        [InlineData(26.0, 1.00, 1.00, 1.00)]
        [InlineData(30.0, 1.00, 1.00, 1.00)]
        [InlineData(31.0, 0.91, 0.94, 0.96)]
        [InlineData(35.0, 0.91, 0.94, 0.96)]
        [InlineData(36.0, 0.82, 0.88, 0.91)]
        [InlineData(40.0, 0.82, 0.88, 0.91)]
        [InlineData(41.0, 0.71, 0.82, 0.87)]
        [InlineData(45.0, 0.71, 0.82, 0.87)]
        [InlineData(46.0, 0.58, 0.75, 0.82)]
        [InlineData(50.0, 0.58, 0.75, 0.82)]
        [InlineData(51.0, 0.41, 0.67, 0.76)]
        [InlineData(55.0, 0.41, 0.67, 0.76)]
        [InlineData(56.0, double.NaN, 0.58, 0.71)]
        [InlineData(60.0, double.NaN, 0.58, 0.71)]
        [InlineData(61.0, double.NaN, 0.47, 0.65)]
        [InlineData(65.0, double.NaN, 0.47, 0.65)]
        [InlineData(66.0, double.NaN, 0.33, 0.58)]
        [InlineData(70.0, double.NaN, 0.33, 0.58)]
        [InlineData(71.0, double.NaN, double.NaN, 0.50)]
        [InlineData(75.0, double.NaN, double.NaN, 0.50)]
        [InlineData(76.0, double.NaN, double.NaN, 0.41)]
        [InlineData(80.0, double.NaN, double.NaN, 0.41)]
        [InlineData(81.0, double.NaN, double.NaN, 0.29)]
        [InlineData(85.0, double.NaN, double.NaN, 0.29)]
        [InlineData(86.0, double.NaN, double.NaN, double.NaN)]
        public void Ambient_correction_matches_Table_310_15_B_1_1(double ambientC, double f60, double f75, double f90)
        {
            AssertFactor(f60, NECStandards.GetTemperatureCorrectionFactor(ambientC, 60));
            AssertFactor(f75, NECStandards.GetTemperatureCorrectionFactor(ambientC, 75));
            AssertFactor(f90, NECStandards.GetTemperatureCorrectionFactor(ambientC, 90));
        }

        private static void AssertFactor(double expected, double actual)
        {
            if (double.IsNaN(expected)) Assert.True(double.IsNaN(actual), $"expected no factor, got {actual}");
            else Assert.Equal(expected, actual, 3);
        }

        [Fact]
        public void A_fractional_ambient_takes_the_hotter_band()
        {
            // The bands are whole degrees; 30.5 °C is above 30, so it is corrected as 31-35.
            Assert.Equal(0.94, NECStandards.GetTemperatureCorrectionFactor(30.5, 75), 3);
        }

        [Fact]
        public void The_sizer_correction_uses_the_75C_column()
        {
            Assert.Equal(100 * 0.88, NECStandards.ApplyTemperatureCorrection(100, 40), 6);
            Assert.Equal(100 * 0.75, NECStandards.ApplyTemperatureCorrection(100, 48), 6);
            Assert.Equal(0.0, NECStandards.ApplyTemperatureCorrection(100, 72));
        }

        [Fact]
        public void Above_the_table_the_conductor_selection_refuses_and_says_why()
        {
            int[] nec = { 10, 15, 20, 25, 30, 40, 50, 60 };
            var p = NecConductorSelection.Pick(20, false, ConductorMaterial.Copper, 72, 2, nec);
            Assert.Null(p.Size);
            Assert.Contains("310.15(B)(1)(1)", p.Refusal);
        }

        // ── Table 310.15(C)(1), more than three current-carrying conductors ─────

        [Theory]
        [InlineData(3, 1.00)]
        [InlineData(4, 0.80)]
        [InlineData(6, 0.80)]
        [InlineData(7, 0.70)]
        [InlineData(9, 0.70)]
        [InlineData(10, 0.50)]
        [InlineData(20, 0.50)]
        [InlineData(21, 0.45)]
        [InlineData(30, 0.45)]
        [InlineData(31, 0.40)]
        [InlineData(40, 0.40)]
        [InlineData(41, 0.35)]
        public void Adjustment_matches_Table_310_15_C_1(int conductors, double factor)
            => Assert.Equal(100 * factor, NECStandards.ApplyBundlingAdjustment(100, conductors), 6);

        // ── Table 250.122, copper column ────────────────────────────────────────

        [Theory]
        [InlineData(15, "14")]
        [InlineData(20, "12")]
        [InlineData(25, "10")]
        [InlineData(60, "10")]
        [InlineData(100, "8")]
        [InlineData(200, "6")]
        [InlineData(300, "4")]
        [InlineData(400, "3")]
        [InlineData(500, "2")]
        [InlineData(600, "1")]
        [InlineData(800, "1/0")]
        [InlineData(1000, "2/0")]
        [InlineData(1200, "3/0")]
        [InlineData(1600, "4/0")]
        [InlineData(2000, "250")]
        [InlineData(2500, "350")]
        [InlineData(3000, "400")]
        [InlineData(4000, "500")]
        [InlineData(5000, "700")]
        [InlineData(6000, "800")]
        public void EGC_matches_Table_250_122_copper(int ocpdA, string expected)
            => Assert.Equal(expected, NECStandards.GetEquipmentGroundingConductor(ocpdA));

        // ── Table 250.66, copper service conductors → copper GEC ────────────────

        [Theory]
        [InlineData("14", "8")]
        [InlineData("6", "8")]
        [InlineData("2", "8")]
        [InlineData("1", "6")]
        [InlineData("1/0", "6")]
        [InlineData("2/0", "4")]
        [InlineData("3/0", "4")]
        [InlineData("4/0", "2")]
        [InlineData("250", "2")]
        [InlineData("300", "2")]
        [InlineData("350", "2")]
        [InlineData("400", "1/0")]
        [InlineData("500", "1/0")]
        [InlineData("600", "1/0")]
        [InlineData("700", "2/0")]
        [InlineData("1000", "2/0")]
        [InlineData("1100", "2/0")]
        [InlineData("1250", "3/0")]
        [InlineData("2000", "3/0")]
        public void GEC_matches_Table_250_66_copper(string serviceSize, string expected)
            => Assert.Equal(expected, NECStandards.GetGroundingElectrodeConductor(serviceSize));

        [Fact]
        public void GEC_unknown_size_says_consult_the_table()
            => Assert.StartsWith("Consult NEC Table 250.66", NECStandards.GetGroundingElectrodeConductor("12 mm2"));

        // ── Chapter 9 Table 1 and Table 8 ───────────────────────────────────────

        [Theory]
        [InlineData(1, 0.53)]
        [InlineData(2, 0.31)]
        [InlineData(3, 0.40)]
        [InlineData(12, 0.40)]
        public void Conduit_fill_matches_Chapter_9_Table_1(int conductors, double fill)
            => Assert.Equal(fill, NECStandards.GetConduitFillFraction(conductors), 3);

        [Theory]
        [InlineData("18", 1620)]
        [InlineData("16", 2580)]
        [InlineData("14", 4110)]
        [InlineData("12", 6530)]
        [InlineData("10", 10380)]
        [InlineData("8", 16510)]
        [InlineData("6", 26240)]
        [InlineData("4", 41740)]
        [InlineData("3", 52620)]
        [InlineData("2", 66360)]
        [InlineData("1", 83690)]
        [InlineData("1/0", 105600)]
        [InlineData("2/0", 133100)]
        [InlineData("3/0", 167800)]
        [InlineData("4/0", 211600)]
        [InlineData("250", 250000)]
        [InlineData("1000", 1000000)]
        [InlineData("2000", 2000000)]
        public void Circular_mils_match_Chapter_9_Table_8(string size, double cmil)
            => Assert.Equal(cmil, NECStandards.GetCircularMils(size));

        [Fact]
        public void Unknown_size_has_no_circular_mils()
            => Assert.Equal(0.0, NECStandards.GetCircularMils("2.5mm2"));
    }
}
