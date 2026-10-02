using System;
using StingTools.Standards.NEC2023;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// NEC 2023 Table 310.16, every row, written from the NFPA text — not from the code.
    /// Source: the 2023 table reproduced unmarked by NFPA as the base of Public Inputs
    /// 1432, 221 and 773-NFPA 70-2023 [Section 310.16] (all three agree), NEC CMP-6 First
    /// Draft public-input report pp. 78-79/307,
    /// https://docinfofiles.nfpa.org/files/AboutTheCodes/70/70_A2025_NEC_P06_FD_PIResponses.pdf.
    /// The aluminium columns are headed "ALUMINUM OR COPPER-CLAD ALUMINUM"; their 14 AWG
    /// row is blank in 2023. Columns: 60 °C, 75 °C, 90 °C.
    /// </summary>
    public class Nec310_16TableTests
    {
        [Theory]
        [InlineData("14", 15, 20, 25)]
        [InlineData("12", 20, 25, 30)]
        [InlineData("10", 30, 35, 40)]
        [InlineData("8", 40, 50, 55)]
        [InlineData("6", 55, 65, 75)]
        [InlineData("4", 70, 85, 95)]
        [InlineData("3", 85, 100, 115)]
        [InlineData("2", 95, 115, 130)]
        [InlineData("1", 110, 130, 145)]
        [InlineData("1/0", 125, 150, 170)]
        [InlineData("2/0", 145, 175, 195)]
        [InlineData("3/0", 165, 200, 225)]
        [InlineData("4/0", 195, 230, 260)]
        [InlineData("250", 215, 255, 290)]
        [InlineData("300", 240, 285, 320)]
        [InlineData("350", 260, 310, 350)]
        [InlineData("400", 280, 335, 380)]
        [InlineData("500", 320, 380, 430)]
        [InlineData("600", 350, 420, 475)]
        [InlineData("700", 385, 460, 520)]
        [InlineData("750", 400, 475, 535)]
        [InlineData("800", 410, 490, 555)]
        [InlineData("900", 435, 520, 585)]
        [InlineData("1000", 455, 545, 615)]
        [InlineData("1250", 495, 590, 665)]
        [InlineData("1500", 525, 625, 705)]
        [InlineData("1750", 545, 650, 735)]
        [InlineData("2000", 555, 665, 750)]
        public void Copper_row_matches_the_2023_table(string size, int c60, int c75, int c90)
        {
            Assert.Equal(c60, NECStandards.GetConductorAmpacity(size, ConductorMaterial.Copper, 60));
            Assert.Equal(c75, NECStandards.GetConductorAmpacity(size, ConductorMaterial.Copper, 75));
            Assert.Equal(c90, NECStandards.GetConductorAmpacity(size, ConductorMaterial.Copper, 90));
        }

        [Theory]
        [InlineData("12", 15, 20, 25)]
        [InlineData("10", 25, 30, 35)]
        [InlineData("8", 35, 40, 45)]
        [InlineData("6", 40, 50, 55)]
        [InlineData("4", 55, 65, 75)]
        [InlineData("3", 65, 75, 85)]
        [InlineData("2", 75, 90, 100)]
        [InlineData("1", 85, 100, 115)]
        [InlineData("1/0", 100, 120, 135)]
        [InlineData("2/0", 115, 135, 150)]
        [InlineData("3/0", 130, 155, 175)]
        [InlineData("4/0", 150, 180, 205)]
        [InlineData("250", 170, 205, 230)]
        [InlineData("300", 195, 230, 260)]
        [InlineData("350", 210, 250, 280)]
        [InlineData("400", 225, 270, 305)]
        [InlineData("500", 260, 310, 350)]
        [InlineData("600", 285, 340, 385)]
        [InlineData("700", 315, 375, 425)]
        [InlineData("750", 320, 385, 435)]
        [InlineData("800", 330, 395, 445)]
        [InlineData("900", 355, 425, 480)]
        [InlineData("1000", 375, 445, 500)]
        [InlineData("1250", 405, 485, 545)]
        [InlineData("1500", 435, 520, 585)]
        [InlineData("1750", 455, 545, 615)]
        [InlineData("2000", 470, 560, 630)]
        public void Aluminium_or_CCA_row_matches_the_2023_table(string size, int c60, int c75, int c90)
        {
            Assert.Equal(c60, NECStandards.GetConductorAmpacity(size, ConductorMaterial.Aluminum, 60));
            Assert.Equal(c75, NECStandards.GetConductorAmpacity(size, ConductorMaterial.Aluminum, 75));
            Assert.Equal(c90, NECStandards.GetConductorAmpacity(size, ConductorMaterial.Aluminum, 90));
        }

        [Fact]
        public void Aluminium_14_AWG_is_blank_in_2023()
            => Assert.Throws<ArgumentException>(() => NECStandards.GetConductorAmpacity("14", ConductorMaterial.Aluminum, 75));
    }
}
