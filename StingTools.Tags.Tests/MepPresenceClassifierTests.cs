using System;
using System.Linq;
using StingTools.Core.Mep;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// P3 — MepLevelViewProducer.LevelsByDiscipline now makes one collector pass and
    /// classifies each element here. These pin the rules the five old passes applied,
    /// so the single pass answers exactly as they did.
    /// </summary>
    public class MepPresenceClassifierTests
    {
        private static string[] C(MepPresenceCategory cat, bool fp = false, string disc = null)
            => MepPresenceClassifier.Classify(cat, () => fp, () => disc).ToArray();

        [Theory]
        [InlineData(MepPresenceCategory.DuctCurve)]
        [InlineData(MepPresenceCategory.FlexDuctCurve)]
        [InlineData(MepPresenceCategory.DuctTerminal)]
        public void AirSideIsMechanicalOnly_EvenWhenTokenSaysMG(MepPresenceCategory cat)
            => Assert.Equal(new[] { "M" }, C(cat, disc: "MG"));

        [Fact]
        public void MechanicalEquipmentAlsoCountsForMG_WhenTokenSaysSo()
        {
            Assert.Equal(new[] { "M" }, C(MepPresenceCategory.MechanicalEquipment));
            Assert.Equal(new[] { "M", "MG" }, C(MepPresenceCategory.MechanicalEquipment, disc: " mg "));
        }

        [Theory]
        [InlineData(MepPresenceCategory.ElectricalFixture)]
        [InlineData(MepPresenceCategory.ElectricalEquipment)]
        [InlineData(MepPresenceCategory.LightingFixture)]
        [InlineData(MepPresenceCategory.LightingDevice)]
        [InlineData(MepPresenceCategory.DataDevice)]
        [InlineData(MepPresenceCategory.FireAlarmDevice)]
        public void ElectricalCategoriesAreE(MepPresenceCategory cat)
            => Assert.Equal(new[] { "E" }, C(cat, disc: "MG"));

        [Fact]
        public void SprinklerIsFireProtection()
            => Assert.Equal(new[] { "FP" }, C(MepPresenceCategory.Sprinkler, disc: "MG"));

        [Fact]
        public void PipeSplitsOnFireProtectionAndAddsMG()
        {
            Assert.Equal(new[] { "P" }, C(MepPresenceCategory.PipeCurve));
            Assert.Equal(new[] { "FP" }, C(MepPresenceCategory.PipeCurve, fp: true));
            Assert.Equal(new[] { "P", "MG" }, C(MepPresenceCategory.PipeCurve, disc: "MG"));
            Assert.Equal(new[] { "FP", "MG" }, C(MepPresenceCategory.PipeCurve, fp: true, disc: "MG"));
        }

        [Fact]
        public void FlexPipeIsNeverMG()
            => Assert.Equal(new[] { "P" }, C(MepPresenceCategory.FlexPipeCurve, disc: "MG"));

        [Fact]
        public void PlumbingFixtureIsPAndMayBeMG()
            => Assert.Equal(new[] { "P", "MG" }, C(MepPresenceCategory.PlumbingFixture, disc: "MG"));

        [Theory]
        [InlineData(MepPresenceCategory.PipeAccessory)]
        [InlineData(MepPresenceCategory.SpecialityEquipment)]
        [InlineData(MepPresenceCategory.MedicalEquipment)]
        public void MgOnlyCategoriesCountOnlyWhenTokenIsMG(MepPresenceCategory cat)
        {
            Assert.Empty(C(cat));
            Assert.Empty(C(cat, disc: "M"));
            Assert.Equal(new[] { "MG" }, C(cat, disc: "MG"));
        }

        [Fact]
        public void OtherCategoryCountsForNothing()
            => Assert.Empty(C(MepPresenceCategory.Other, fp: true, disc: "MG"));

        [Fact]
        public void ReadersAreOnlyCalledWhenARuleNeedsThem()
        {
            bool fpCalled = false, discCalled = false;
            MepPresenceClassifier.Classify(MepPresenceCategory.DuctCurve,
                () => { fpCalled = true; return false; }, () => { discCalled = true; return null; });
            Assert.False(fpCalled);
            Assert.False(discCalled);
        }

        [Fact]
        public void AThrowingTokenReaderIsNotMG()
            => Assert.Equal(new[] { "P" }, MepPresenceClassifier.Classify(MepPresenceCategory.PipeCurve,
                () => false, () => throw new InvalidOperationException("boom")).ToArray());
    }
}
