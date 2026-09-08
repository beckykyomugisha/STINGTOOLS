using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Standards;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT-8 — the translation from the project region preset into the two engine
    /// region vocabularies.
    ///
    /// <para>The row said the presets "never reach the engines". Two of them already
    /// did (the MEP-A-01 cable-size command, which writes to the model, and the MAT
    /// tab's locale). What did not reach anything was the pair of engines that have
    /// their OWN region — HVAC sizing (<c>UK_SI</c>…) and LPS risk (<c>UK</c>…) — each
    /// pinned to its own hardcoded constant, with <c>StandardsChanged</c> carrying no
    /// subscribers to reconcile them.</para>
    ///
    /// <para>These tests hold the map to the preset table, so a region added to
    /// <see cref="ProjectStandardsManager.RegionalPresets"/> without a mapping is
    /// caught instead of silently defaulting to the UK.</para>
    /// </summary>
    public class EngineRegionMapTests
    {
        // ── Held to the preset table, not to itself ──────────────────────────────

        /// <summary>Every shipped regional preset is mapped BY NAME. A preset that falls
        /// through to the default is the exact failure this closes: it looks configured
        /// and behaves as if it were not.</summary>
        [Fact]
        public void EveryRegionalPresetIsMappedByName()
        {
            var unmapped = ProjectStandardsManager.RegionalPresets.Keys
                .Where(k => !EngineRegionMap.MappedProjectRegions.Contains(k))
                .ToList();

            Assert.True(unmapped.Count == 0,
                "Regional presets with no named mapping in EngineRegionMap — these silently " +
                "fall back to the UK size ladder and the UK lightning band: " + string.Join(", ", unmapped));
        }

        /// <summary>The MEP region a preset maps to must be one the rules file actually
        /// carries. Mapping to a key that is not there falls back inside
        /// <c>MepSizingRegistry</c> — quietly, one layer further down than anyone
        /// would look.</summary>
        [Fact]
        public void EveryMappedMepRegionExistsInTheShippedRulesFile()
        {
            var shipped = ShippedMepRegions();
            Assert.True(shipped.Count > 0, "STING_MEP_SIZING_RULES.json declares no duct standard-size regions.");

            foreach (string preset in ProjectStandardsManager.RegionalPresets.Keys)
            {
                string mapped = EngineRegionMap.ToMepSizingRegion(preset);
                Assert.True(shipped.Contains(mapped),
                    $"Preset '{preset}' maps to MEP region '{mapped}', which " +
                    $"STING_MEP_SIZING_RULES.json does not define. It has: {string.Join(", ", shipped)}");
            }
        }

        /// <summary>The LPS region must be one the panel actually offers, or the combo
        /// cannot select it and the header shows one thing while the engine uses
        /// another.</summary>
        [Fact]
        public void EveryMappedLpsRegionIsOfferedByThePanel()
        {
            var offered = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "UK", "EU", "US", "TROPIC", "AFRICA" };

            foreach (string preset in ProjectStandardsManager.RegionalPresets.Keys)
            {
                string mapped = EngineRegionMap.ToLpsRegion(preset);
                Assert.True(offered.Contains(mapped),
                    $"Preset '{preset}' maps to LPS region '{mapped}', which the panel does not offer.");
            }
        }

        // ── The mappings that carry a decision ───────────────────────────────────

        [Theory]
        [InlineData("USA", "US_IP")]
        [InlineData("UK", "UK_SI")]
        [InlineData("Europe", "EU_SI")]
        [InlineData("Uganda", "UK_SI")]        // EAS/UNBS schedules are BS-derived and metric
        [InlineData("Kenya", "UK_SI")]
        [InlineData("EastAfrica", "UK_SI")]
        [InlineData("SouthAfrica", "UK_SI")]
        [InlineData("Australia", "UK_SI")]     // AS/NZS is not represented; documented as an approximation
        [InlineData("International", "UK_SI")]
        public void MepSizingRegionMapping(string project, string expected)
            => Assert.Equal(expected, EngineRegionMap.ToMepSizingRegion(project));

        /// <summary>The LPS band is a calculation input, not a label — BS EN 62305-2 risk
        /// is proportional to Ng, and the offered bands run 0.5 to 25.</summary>
        [Theory]
        [InlineData("USA", "US")]
        [InlineData("UK", "UK")]
        [InlineData("Europe", "EU")]
        [InlineData("Uganda", "AFRICA")]
        [InlineData("Kenya", "AFRICA")]
        [InlineData("EastAfrica", "AFRICA")]
        [InlineData("SouthAfrica", "AFRICA")]
        [InlineData("Australia", "UK")]        // spans Ng 0.1 to >10; no single band is defensible
        [InlineData("International", "UK")]
        public void LpsRegionMapping(string project, string expected)
            => Assert.Equal(expected, EngineRegionMap.ToLpsRegion(project));

        /// <summary>The named harm: an East African project must not size lightning risk
        /// on the UK ground-flash band.</summary>
        [Fact]
        public void UgandaDoesNotLandOnTheUkLightningBand()
        {
            Assert.NotEqual(EngineRegionMap.LpsDefault, EngineRegionMap.ToLpsRegion("Uganda"));
            Assert.Equal("AFRICA", EngineRegionMap.ToLpsRegion("Uganda"));
        }

        // ── Unknown input must not invent a mapping ──────────────────────────────

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Atlantis")]
        [InlineData("uk ")]        // trimmed → UK, so this one is NOT a fallback
        public void UnknownOrBlankRegionsAreSafe(string input)
        {
            string mep = EngineRegionMap.ToMepSizingRegion(input);
            string lps = EngineRegionMap.ToLpsRegion(input);
            Assert.False(string.IsNullOrWhiteSpace(mep));
            Assert.False(string.IsNullOrWhiteSpace(lps));
        }

        [Fact]
        public void UnrecognisedRegionReturnsTheEnginesOwnDefault()
        {
            Assert.Equal(EngineRegionMap.MepSizingDefault, EngineRegionMap.ToMepSizingRegion("Atlantis"));
            Assert.Equal(EngineRegionMap.LpsDefault, EngineRegionMap.ToLpsRegion("Atlantis"));
        }

        [Fact]
        public void MatchingIsCaseAndWhitespaceInsensitive()
        {
            Assert.Equal("AFRICA", EngineRegionMap.ToLpsRegion("  uGaNdA "));
            Assert.Equal("US_IP", EngineRegionMap.ToMepSizingRegion("usa"));
        }

        /// <summary>The declared MEP default must be the rules file's own default, or the
        /// two drift and an unrecognised region lands somewhere the file does not
        /// consider default.</summary>
        [Fact]
        public void MepSizingDefaultMatchesTheRulesFileDefaultRegion()
        {
            string fileDefault = ShippedDuctDefaultRegion();
            Assert.Equal(EngineRegionMap.MepSizingDefault, fileDefault);
        }

        // ── Shipped-data readers ─────────────────────────────────────────────────

        private static string RulesPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_MEP_SIZING_RULES.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "StingTools", "Data", "STING_MEP_SIZING_RULES.json");
        }

        private static HashSet<string> ShippedMepRegions()
        {
            var j = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(RulesPath()));
            var sizes = j["duct"]?["standardSizesMm"] as Newtonsoft.Json.Linq.JObject;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (sizes != null) foreach (var kv in sizes) set.Add(kv.Key);
            return set;
        }

        private static string ShippedDuctDefaultRegion()
        {
            var j = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(RulesPath()));
            return (string)j["duct"]?["_defaultRegion"];
        }
    }
}
