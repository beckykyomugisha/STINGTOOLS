using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Commands.Electrical.ArcFlash;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DSCH-25 (b) — arc-flash presentation. Four colour sets (the data file, the boundary
    /// view, the element fill, the label sheet) disagreed, two of them painted the lowest
    /// categories green, and the label printed "PPE Category N (by incident energy)" next to
    /// the incident energy. Now: one owner (STING_ARC_FLASH_PPE.json), incident-energy bands
    /// rather than PPE categories, and an ANSI Z535.4 signal-word header.
    /// </summary>
    public class ArcFlashPresentationTests
    {
        internal static string ShippedPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", ArcFlashPresentation.FileName)))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data/" + ArcFlashPresentation.FileName);
            return Path.Combine(dir.FullName, "StingTools", "Data", ArcFlashPresentation.FileName);
        }

        internal static ArcFlashPresentationSet Shipped()
        {
            var set = ArcFlashPresentation.Load(ShippedPath());
            Assert.True(set.Loaded, set.LoadError);
            return set;
        }

        private static JObject ShippedJson() => JObject.Parse(File.ReadAllText(ShippedPath()));

        private static ArcFlashResult Calculated(double cal) => new ArcFlashResult
        {
            Calculated = true, IncidentEnergyCalCm2 = cal, WorkingDistanceMm = 455, BoundaryMm = 900,
            GoverningClearingTimeS = 0.1, GapMm = 25
        };

        [Fact]
        public void Shipped_file_loads_six_ascending_bands_with_an_unbounded_top()
        {
            var p = Shipped();
            Assert.Equal(6, p.Bands.Count);
            Assert.Equal(new double?[] { 1.2, 4, 8, 25, 40, null }, p.Bands.Select(b => b.MaxCalCm2).ToArray());
        }

        /// <summary>Energised equipment is never painted "safe": the lowest band is grey.</summary>
        [Fact]
        public void Lowest_band_is_neutral_grey_not_green()
        {
            var c = Shipped().Bands[0].ViewColour;
            Assert.Equal(c.R, c.G);
            Assert.Equal(c.G, c.B);
        }

        [Fact]
        public void No_band_is_green()
            => Assert.All(Shipped().Bands, b => Assert.False(b.ViewColour.G > b.ViewColour.R && b.ViewColour.G > b.ViewColour.B,
                   $"{b.Label} is green ({b.ViewColour})"));

        /// <summary>No PPE categories, no Category 0 (removed in NFPA 70E-2015), no clothing
        /// text: every value a reader binds is checked; "_" comments may explain why.</summary>
        [Fact]
        public void Shipped_data_carries_no_PPE_category_or_clothing_text()
        {
            var root = ShippedJson();
            Assert.Null(root["ppeCategories"]);
            var bound = root.Descendants().OfType<JProperty>()
                .Where(pr => !pr.Name.StartsWith("_") && pr.Name != "verify").ToList();
            Assert.DoesNotContain(bound, pr => pr.Name == "cat" || pr.Name == "description");
            Assert.DoesNotContain(bound, pr => pr.Value.Type == JTokenType.String &&
                (pr.Value.ToString().IndexOf("categor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 pr.Value.ToString().IndexOf("arc-rated", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [Theory]
        [InlineData(0.5, "WARNING")]
        [InlineData(40.0, "WARNING")]
        [InlineData(40.01, "DANGER")]
        [InlineData(120.0, "DANGER")]
        public void Signal_word_is_DANGER_only_above_the_threshold(double cal, string word)
            => Assert.Equal(word, Shipped().HeaderFor(cal).SignalWord);

        [Fact]
        public void Z535_header_text_colours_black_on_warning_white_on_danger()
        {
            var p = Shipped();
            Assert.Equal(new ArcRgb(0, 0, 0), p.Warning.Text);
            Assert.Equal(new ArcRgb(255, 255, 255), p.Danger.Text);
        }

        [Fact]
        public void Null_threshold_means_WARNING_on_every_label()
        {
            var root = ShippedJson();
            root["labelHeader"]["dangerAboveCalCm2"] = JValue.CreateNull();
            var p = ArcFlashPresentation.Parse(root);
            Assert.True(p.Loaded, p.LoadError);
            Assert.Equal("WARNING", p.HeaderFor(500).SignalWord);
        }

        [Fact]
        public void Label_shows_the_signal_word_and_no_PPE_category()
        {
            var p = Shipped();
            string warn = ArcFlashEngine.FormatLabel("DB-1", 400, ArcEquipmentClass.PanelMcc, Calculated(6.2), "fixed", p);
            string danger = ArcFlashEngine.FormatLabel("DB-2", 400, ArcEquipmentClass.PanelMcc, Calculated(55), "fixed", p);
            Assert.StartsWith("WARNING — ARC FLASH HAZARD", warn);
            Assert.StartsWith("DANGER — ARC FLASH HAZARD", danger);
            foreach (var s in new[] { warn, danger })
            {
                Assert.DoesNotContain("Category", s);
                Assert.Contains("Incident Energy:", s);   // 130.5(H): energy + working distance
                Assert.Contains("Arc Flash Boundary:", s);
                Assert.Contains("Voltage:", s);
            }
        }

        [Fact]
        public void Unloaded_presentation_prints_no_signal_word_and_says_why()
        {
            var p = ArcFlashPresentation.Load(Path.Combine(Path.GetTempPath(), "no-such-dir", ArcFlashPresentation.FileName));
            Assert.False(p.Loaded);
            Assert.Contains("not found", p.LoadError);
            Assert.Null(p.BandFor(10));
            Assert.Null(p.HeaderFor(10));
            string label = ArcFlashEngine.FormatLabel("DB-1", 400, ArcEquipmentClass.PanelMcc, Calculated(6.2), "fixed", p);
            Assert.Contains("signal word not set", label);
            Assert.DoesNotContain("WARNING", label);
            Assert.DoesNotContain("DANGER", label);
        }

        /// <summary>In the test host FindDataFile finds nothing — the same as an install
        /// with the file missing. There is no built-in colour set to fall back on.</summary>
        [Fact]
        public void Current_without_a_data_file_is_unloaded_not_defaulted()
        {
            Assert.False(ArcFlashPresentation.Current.Loaded);
            Assert.Empty(ArcFlashPresentation.Current.Bands);
        }

        [Fact]
        public void Unbounded_band_not_last_is_rejected()
        {
            var root = ShippedJson();
            root["energyBands"][2]["maxCalCm2"] = JValue.CreateNull();
            var p = ArcFlashPresentation.Parse(root);
            Assert.False(p.Loaded);
            Assert.Contains("only the last band", p.LoadError);
        }

        [Fact]
        public void Bands_out_of_order_are_rejected()
        {
            var root = ShippedJson();
            root["energyBands"][1]["maxCalCm2"] = 0.5;
            Assert.Contains("not above the previous band", ArcFlashPresentation.Parse(root).LoadError);
        }

        [Fact]
        public void Bounded_top_band_is_rejected()
        {
            var root = ShippedJson();
            ((JArray)root["energyBands"]).RemoveAt(5);
            Assert.Contains("must be unbounded", ArcFlashPresentation.Parse(root).LoadError);
        }

        [Fact]
        public void Bad_colour_is_rejected_whole_file()
        {
            var root = ShippedJson();
            root["energyBands"][0]["viewColour"] = "grey";
            var p = ArcFlashPresentation.Parse(root);
            Assert.False(p.Loaded);
            Assert.Null(p.BandFor(1.0));
        }

        [Fact]
        public void Renamed_signal_word_is_rejected()
        {
            var root = ShippedJson();
            root["labelHeader"]["danger"]["signalWord"] = "CAUTION";
            Assert.Contains("must be 'DANGER'", ArcFlashPresentation.Parse(root).LoadError);
        }

        [Fact]
        public void Missing_label_header_is_rejected()
        {
            var root = ShippedJson();
            root.Remove("labelHeader");
            Assert.Contains("labelHeader is missing", ArcFlashPresentation.Parse(root).LoadError);
        }
    }
}
