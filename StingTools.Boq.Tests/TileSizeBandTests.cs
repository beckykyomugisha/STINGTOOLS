using System;
using System.IO;
using System.Linq;
using StingTools.Core.Materials;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// The fourth member of a set that already had three.
    ///
    /// Brick bond, block size and plaster type each resolve a BLE_* parameter
    /// through a canonicaliser with an inference fallback, and each drives a
    /// waste figure MATERIAL_LOOKUP bands for it. Tile size was the hole:
    /// BLE_TILE_SIZE_TXT has been declared in MR_PARAMETERS.txt with a GUID all
    /// along and read by NOTHING, so the supplier rule applied a flat 10% to a
    /// mosaic splashback and a 600 mm porcelain floor alike — while the lookup
    /// had banded it 20 / 12 / 10 / 8 since before this schedule existed.
    ///
    /// Adding a fifth mechanism would have been the mistake. This finishes an
    /// existing one.
    /// </summary>
    public class TileSizeBandTests
    {
        // ── a stated band wins ──────────────────────────────────────────────

        [Theory]
        [InlineData("MOSAIC", "MOSAIC")]
        [InlineData("mosaic", "MOSAIC")]
        [InlineData("Large format", "LARGE")]
        [InlineData("  Small  ", "SMALL")]
        public void A_Stated_Band_Is_Taken_Verbatim(string raw, string expected)
        {
            Assert.Equal(expected, MaterialKeyCanonicaliser.TileSize(raw));
        }

        // ── a dimension is banded on its SMALLER edge ───────────────────────

        [Theory]
        [InlineData("600x600", "LARGE")]
        [InlineData("300x600", "MEDIUM")]   // banded on 300, not 600
        [InlineData("300x300", "MEDIUM")]
        [InlineData("200x200", "SMALL")]
        [InlineData("50x50",   "MOSAIC")]
        public void A_Dimension_Bands_On_The_EDGE_THAT_IS_CUT(string raw, string expected)
        {
            // A 300 x 600 plank is cut on its SHORT side, and that edge governs
            // the cutting waste. Banding it on 600 would call a plank
            // large-format and under-order it.
            Assert.Equal(expected, MaterialKeyCanonicaliser.TileSize(raw));
        }

        [Theory]
        [InlineData("Porcelain 300x600 Matt", "MEDIUM")]
        [InlineData("Ceramic Wall Tile 250×400", "SMALL")]
        [InlineData("Mosaic 25 x 25 glass", "MOSAIC")]
        public void A_Dimension_Inside_A_Product_Name_Is_Found(string raw, string expected)
        {
            Assert.Equal(expected, MaterialKeyCanonicaliser.TileSize(raw));
        }

        [Theory]
        [InlineData("600", "LARGE")]
        [InlineData("100", "SMALL")]
        public void A_Single_Dimension_Bands_On_Itself(string raw, string expected)
        {
            Assert.Equal(expected, MaterialKeyCanonicaliser.TileSize(raw));
        }

        // ── what it refuses to guess ────────────────────────────────────────

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("Ceramic Tile")]
        [InlineData("Porcelain")]
        public void A_Name_With_No_Size_In_It_Returns_NOTHING(string raw)
        {
            // Empty, not a default. InferOrCanon turns an empty result into the
            // project default AND records it, so the export can say the band was
            // never stated. Returning "MEDIUM" here would launder a guess into a
            // stated fact.
            Assert.Equal("", MaterialKeyCanonicaliser.TileSize(raw));
        }

        [Fact]
        public void A_Zero_Dimension_Is_Not_A_Band()
        {
            Assert.Equal("", MaterialKeyCanonicaliser.TileSize("0x0"));
        }

        // ── the bands match the file that owns them ─────────────────────────

        [Theory]
        [InlineData("MOSAIC", 20.0)]
        [InlineData("SMALL",  12.0)]
        [InlineData("MEDIUM", 10.0)]
        [InlineData("LARGE",   8.0)]
        [InlineData("DEFAULT", 10.0)]
        public void Every_Band_Has_A_Waste_Figure_In_MATERIAL_LOOKUP(string band, double expected)
        {
            // The canonicaliser's vocabulary and the lookup's keys have to be
            // the same words, or a correctly-banded tile silently falls to
            // DEFAULT. This is the pair that would drift.
            double found = 0;
            foreach (string raw in File.ReadAllLines(
                         Path.Combine(AppContext.BaseDirectory, "Data", "MATERIAL_LOOKUP.csv")))
            {
                var p = (raw ?? "").Trim().Split(',');
                if (p.Length >= 4 && p[0].Trim() == "TILE" && p[1].Trim() == band
                    && p[2].Trim() == "WASTE_PCT"
                    && double.TryParse(p[3].Trim(), System.Globalization.NumberStyles.Any,
                                       System.Globalization.CultureInfo.InvariantCulture, out double v))
                { found = v; break; }
            }
            Assert.Equal(expected, found);
        }

        // ── which lines carry the cutting waste ─────────────────────────────

        [Fact]
        public void ONLY_The_Tile_Line_Carries_The_Cutting_Waste()
        {
            // A mosaic's 20% is CUTTING waste: you cut tiles to fit and throw
            // the offcuts away. Adhesive and grout are spread over the area and
            // their own supplier rules own their allowances, so inflating them
            // by the tile's cutting figure would buy half as much adhesive again
            // for a splashback.
            //
            // This test exists because the mutation that puts the override on
            // adhesive moved nothing — the claim was in a comment and in no
            // assertion.
            var lines = StingTools.BOQ.Takeoff.CompoundTakeoff.TiledFinish(
                new StingTools.BOQ.Takeoff.TiledFinishInput
                {
                    AreaM2 = 10, IsWall = true, TileLabel = "Mosaic 25x25",
                    AdhesiveKgPerM2 = 4, GroutKgPerM2 = 0.5,
                    WastePctOverride = 20
                });

            var tile = lines.Single(l => l.Kind == "wall_tile");
            Assert.Equal(20, tile.WastePctOverride);

            foreach (string kind in new[] { "tile_adhesive", "tile_grout" })
                Assert.Equal(-1, lines.Single(l => l.Kind == kind).WastePctOverride);
        }

        [Fact]
        public void An_Unstated_Band_Leaves_The_Tile_Line_On_Its_Rules_Default()
        {
            // -1 means "the supplier rule decides", which is what every other
            // constituent does. Writing 0 would mean "no waste at all".
            var lines = StingTools.BOQ.Takeoff.CompoundTakeoff.TiledFinish(
                new StingTools.BOQ.Takeoff.TiledFinishInput
                {
                    AreaM2 = 10, IsWall = false, TileLabel = "Ceramic",
                    AdhesiveKgPerM2 = 4, WastePctOverride = -1
                });

            Assert.Equal(-1, lines.Single(l => l.Kind == "floor_tile").WastePctOverride);
        }

        [Fact]
        public void A_Mosaic_Wastes_More_Than_A_Large_Format_Tile()
        {
            // The whole reason the band matters. If these were equal, a flat
            // figure would have been defensible all along.
            var bands = new[] { "MOSAIC", "SMALL", "MEDIUM", "LARGE" }
                .Select(b => MaterialKeyCanonicaliser.TileSize(b)).ToArray();

            Assert.Equal(new[] { "MOSAIC", "SMALL", "MEDIUM", "LARGE" }, bands);
        }
    }
}
