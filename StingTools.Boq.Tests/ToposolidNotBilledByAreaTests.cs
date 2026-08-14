using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// Ground is not a supply-and-fix item.
    ///
    /// KNP26, 2026-08-14: the site model's only BOQ row was the existing
    /// toposolid, billed as 13,698.976 m² of "supply and fix generic toposolid"
    /// at UGX 222,000/m² — UGX 3.04bn and 7,393 tCO₂e, 64% of the project total,
    /// for the ground itself.
    ///
    /// The chain: DeriveQuantity consults a matching take-off rule only when
    /// UnitsAlign(rule.Unit, callerUnit). earthworks-cut correctly matched the
    /// Toposolid and correctly declares m³, but the rate found was per m², the
    /// units disagreed, and the code fell through to the legacy geometry path —
    /// which measured area. The rule that governs the element was overruled by
    /// the unit of the rate that happened to be found for it.
    ///
    /// The fix is the strictUnit flag: a rule may declare that its Unit is the
    /// only unit its elements may be measured in, and a mismatch is reported
    /// unresolved rather than silently re-measured. These tests lock the
    /// corporate baseline so a toposolid can never be billed by area again.
    /// </summary>
    public class ToposolidNotBilledByAreaTests
    {
        private static JsonElement Rules()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "STING_TAKEOFF_RULES.json");
            return JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("rules");
        }

        [Theory]
        [InlineData("earthworks-cut")]
        [InlineData("earthworks-fill")]
        public void Earthworks_Rules_Are_StrictUnit(string ruleId)
        {
            var rule = Rules().EnumerateArray()
                .Single(r => r.GetProperty("id").GetString() == ruleId);

            Assert.True(rule.TryGetProperty("strictUnit", out var strict),
                $"{ruleId} must declare strictUnit, or an m² rate will re-measure the " +
                "toposolid by area through the legacy geometry path.");
            Assert.True(strict.GetBoolean(), $"{ruleId}.strictUnit must be true");
        }

        [Theory]
        [InlineData("earthworks-cut")]
        [InlineData("earthworks-fill")]
        public void Earthworks_Rules_Measure_Volume(string ruleId)
        {
            var rule = Rules().EnumerateArray()
                .Single(r => r.GetProperty("id").GetString() == ruleId);
            Assert.Equal("m3", rule.GetProperty("unit").GetString());
        }

        /// <summary>
        /// The guard that matters. A rule matching Toposolid in an area unit
        /// would win by first-match-wins and put the ground back on the bill —
        /// which is exactly what the earthworks-cut note warns against.
        /// </summary>
        [Fact]
        public void No_Rule_Measures_Toposolid_By_Area()
        {
            var offenders = Rules().EnumerateArray()
                .Where(r => (r.GetProperty("matchCategory").GetString() ?? "")
                                .Contains("Topo", System.StringComparison.OrdinalIgnoreCase))
                .Where(r =>
                {
                    var u = (r.GetProperty("unit").GetString() ?? "").ToLowerInvariant();
                    return u is "m2" or "m²" or "sqm";
                })
                .Select(r => r.GetProperty("id").GetString())
                .ToList();

            Assert.True(offenders.Count == 0,
                "Toposolid must never be measured by area — ground is not supplied and " +
                "fixed. Earthworks are cut/fill volumes (Site_CutFillTakeoff). Offending " +
                "rule(s): " + string.Join(", ", offenders));
        }

        /// <summary>
        /// strictUnit only helps if the rule is reached. Any Toposolid rule that
        /// is NOT strictUnit and sits ahead of the earthworks rules would match
        /// first and reopen the same hole.
        /// </summary>
        [Fact]
        public void Every_Toposolid_Rule_Is_StrictUnit()
        {
            var loose = Rules().EnumerateArray()
                .Where(r => (r.GetProperty("matchCategory").GetString() ?? "")
                                .Contains("Topo", System.StringComparison.OrdinalIgnoreCase))
                .Where(r => !r.TryGetProperty("strictUnit", out var s) || !s.GetBoolean())
                .Select(r => r.GetProperty("id").GetString())
                .ToList();

            Assert.True(loose.Count == 0,
                "Every Toposolid take-off rule must be strictUnit, or a mismatched rate " +
                "falls through to the legacy area measurement. Offending rule(s): " +
                string.Join(", ", loose));
        }
    }
}
