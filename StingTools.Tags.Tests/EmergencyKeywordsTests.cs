using System;
using System.IO;
using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// ELEC-17 — one data-driven emergency keyword list, replacing three English-only
    /// copies (audit matcher, circuit wizard, separation checker).
    /// </summary>
    public class EmergencyKeywordsTests
    {
        private static string ShippedJson() =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "STING_EMERGENCY_KEYWORDS.json"));

        [Fact]
        public void ShippedFileEqualsTheBuiltInFallback()
        {
            var shipped = EmergencyKeywords.FromJson(ShippedJson());
            var builtIn = EmergencyKeywords.BuiltIn();
            Assert.NotNull(shipped);
            Assert.Equal(builtIn.LongKeywords, shipped.LongKeywords);
            Assert.Equal(builtIn.WordStartKeywords, shipped.WordStartKeywords);
            Assert.Equal(builtIn.Abbreviations, shipped.Abbreviations);
            Assert.False(shipped.ReplaceBaseline);
        }

        [Theory]
        [InlineData("Luz de emergencia LED")]           // es
        [InlineData("Bloc autonome de secours")]         // fr
        [InlineData("Éclairage de Secours 1h")]          // fr, accented
        [InlineData("Notbeleuchtung Rettungszeichen")]   // de
        [InlineData("Sicherheitsbeleuchtung_LED")]       // de, CamelCase-ish
        [InlineData("Taa ya Dharura")]                   // sw
        public void NonEnglishFittingsAreEmergency(string family)
        {
            var kw = EmergencyKeywords.FromJson(ShippedJson());
            Assert.True(EmergencyNameMatcher.IsEmergencyName(family, kw));
            Assert.True(EmergencyNameMatcher.IsEmergencyName(family)); // built-in agrees
        }

        [Theory]
        [InlineData("Emergency Bulkhead")]
        [InlineData("LED Panel EM")]
        [InlineData("ExitSign-Wall")]
        [InlineData("EMBulkhead")]
        public void ShippedListReproducesTheEnglishBehaviour(string family)
            => Assert.True(EmergencyNameMatcher.IsEmergencyName(family, EmergencyKeywords.FromJson(ShippedJson())));

        [Theory]
        [InlineData("Surface-Mounted LED")]
        [InlineData("System-Panel 600")]
        [InlineData("Emerald Pendant")]
        [InlineData("EMBASSY Wall Light")]
        [InlineData("Deexited Linear")]
        public void ShippedListStillRejectsOrdinaryFittings(string family)
            => Assert.False(EmergencyNameMatcher.IsEmergencyName(family, EmergencyKeywords.FromJson(ShippedJson())));

        [Fact]
        public void ProjectOverrideIsAdditiveByDefault()
        {
            var merged = EmergencyKeywords.Merge(EmergencyKeywords.BuiltIn(),
                EmergencyKeywords.FromJson("{ \"longKeywords\": [\"nooduitgang\"], \"abbreviations\": [\"NV\"] }"));
            Assert.True(EmergencyNameMatcher.IsEmergencyName("Nooduitgang armatuur", merged));
            Assert.True(EmergencyNameMatcher.IsEmergencyName("Downlight NV", merged));
            Assert.True(EmergencyNameMatcher.IsEmergencyName("Emergency Bulkhead", merged)); // baseline kept
            Assert.False(EmergencyNameMatcher.IsEmergencyName("Envoy Pendant", merged));      // NV token rule
        }

        [Fact]
        public void ProjectOverrideCanReplaceTheBaseline()
        {
            var merged = EmergencyKeywords.Merge(EmergencyKeywords.BuiltIn(),
                EmergencyKeywords.FromJson("{ \"replaceBaseline\": true, \"longKeywords\": [\"nood\"] }"));
            Assert.True(EmergencyNameMatcher.IsEmergencyName("Noodverlichting", merged));
            Assert.False(EmergencyNameMatcher.IsEmergencyName("Emergency Bulkhead", merged));
        }
    }
}
