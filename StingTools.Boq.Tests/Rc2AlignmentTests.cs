using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using StingTools.BOQ;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>RC-2 — tonne/kg unit split + take-off rule alignment.</summary>
    public class Rc2AlignmentTests
    {
        [Fact]
        public void Tonne_And_Kg_Are_Distinct_Tokens()
        {
            Assert.Equal("tonne", BoqUnits.Normalise("tonne"));
            Assert.Equal("tonne", BoqUnits.Normalise("t"));
            Assert.Equal("kg", BoqUnits.Normalise("kg"));
            Assert.NotEqual(BoqUnits.Normalise("tonne"), BoqUnits.Normalise("kg"));
        }

        [Fact]
        public void MassFactor_Applies_1000x_Across_Tonne_Kg()
        {
            Assert.Equal(1000.0, BoqUnits.MassFactor("tonne", "kg"), 6);
            Assert.Equal(0.001, BoqUnits.MassFactor("kg", "tonne"), 6);
            Assert.Equal(1.0, BoqUnits.MassFactor("kg", "kg"), 6);
            Assert.Equal(1.0, BoqUnits.MassFactor("m2", "m2"), 6);
        }

        [Fact]
        public void Tonne_Kg_Align_But_Non_Mass_Units_Do_Not_Cross()
        {
            Assert.True(BoqUnits.Align("tonne", "kg"));    // compatible mass
            Assert.True(BoqUnits.Align("kg", "kg"));
            Assert.False(BoqUnits.Align("m2", "kg"));
            Assert.False(BoqUnits.Align("m", "each"));
        }

        [Fact]
        public void A_Tonne_Rule_Meeting_A_Kg_Rate_Would_No_Longer_Be_1000x_Wrong()
        {
            // 5 tonnes of rebar, priced per kg → the quantity must scale ×1000.
            double ruleQtyTonnes = 5.0;
            double kgForKgRate = ruleQtyTonnes * BoqUnits.MassFactor("tonne", "kg");
            Assert.Equal(5000.0, kgForKgRate, 3);
        }

        // ── Take-off rules JSON (curtain ordering + linear-rule excludes) ──
        private static JsonElement Rules()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "STING_TAKEOFF_RULES.json");
            return JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("rules");
        }

        [Fact]
        public void CurtainWall_Rule_Precedes_Wall_Rule()
        {
            var ids = Rules().EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();
            int curtain = ids.IndexOf("curtain-wall-area");
            int wall = ids.IndexOf("wall-area");
            Assert.True(curtain >= 0 && wall >= 0);
            Assert.True(curtain < wall, "curtain-wall-area must be evaluated before wall-area");
        }

        [Theory]
        [InlineData("pipe")]
        [InlineData("duct")]
        [InlineData("cable-tray")]
        public void Linear_Rules_Exclude_Fittings_And_Accessories(string ruleId)
        {
            var rule = Rules().EnumerateArray().Single(r => r.GetProperty("id").GetString() == ruleId);
            Assert.True(rule.TryGetProperty("matchCategoryExclude", out var ex), $"{ruleId} needs matchCategoryExclude");
            string tokens = ex.GetString() ?? "";
            Assert.Contains("Fitting", tokens);
            Assert.Contains("Accessor", tokens);
        }
    }
}
