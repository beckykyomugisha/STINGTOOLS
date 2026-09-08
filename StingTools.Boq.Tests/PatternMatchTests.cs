using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// A pattern matches a WORD, not a run of letters.
    ///
    /// Found by running the schedule over a second project. An oil painting
    /// arrived in ELEMENT 03: ROOF —
    ///
    ///     Art_Piece_-_Draw_Bridge_at_Arles_-_Van_Gogh_5866
    ///
    /// — because the roof stage lists the type pattern "ridge" and "bRIDGEe"
    /// contains it. Every bridge, cartridge and porridge in every future
    /// project routed to the roof.
    ///
    /// The same bug sat in the wall take-off: `material.Contains("rc")` decides
    /// a wall is reinforced concrete, and "poRCelain" contains "rc" — so a
    /// porcelain-tiled surface would decompose into concrete, rebar and
    /// formwork.
    ///
    /// Neither produced an error. Both produced a confident wrong row in a
    /// section a reader has no reason to question.
    /// </summary>
    public class PatternMatchTests
    {
        // ── the two that were live ──────────────────────────────────────────

        [Fact]
        public void BRIDGE_Does_Not_Match_RIDGE()
        {
            Assert.False(PatternMatch.Contains("Art_Piece_-_Draw_Bridge_at_Arles_-_Van_Gogh_5866",
                                               "ridge"));
        }

        [Fact]
        public void But_A_REAL_Ridge_Still_Matches()
        {
            // The pattern has to keep working, or the fix trades one silent
            // wrong for another.
            Assert.True(PatternMatch.Contains("Roof ridge capping", "ridge"));
            Assert.True(PatternMatch.Contains("RIDGE-TILE", "ridge"));
            Assert.True(PatternMatch.Contains("ridge", "ridge"));
        }

        [Fact]
        public void PORCELAIN_Does_Not_Match_RC()
        {
            Assert.False(PatternMatch.Contains("STING RC Slab 200 - Porcelain Tiled".ToLowerInvariant()
                                                   .Replace("rc slab", "x slab"),
                                               "rc"));
            Assert.False(PatternMatch.Contains("porcelain tiled", "rc"));
        }

        [Fact]
        public void But_A_REAL_RC_Still_Matches()
        {
            Assert.True(PatternMatch.Contains("rc slab 150", "rc"));
            Assert.True(PatternMatch.Contains("200mm RC wall", "rc"));
        }

        // ── the boundary rule ───────────────────────────────────────────────

        [Theory]
        [InlineData("roof-cap", "cap", true)]      // hyphen is a boundary
        [InlineData("roof cap", "cap", true)]      // space is a boundary
        [InlineData("roof_cap", "cap", true)]      // underscore is a boundary
        [InlineData("capping", "cap", false)]      // letter is not
        [InlineData("handicap", "cap", false)]
        public void A_Boundary_Is_Anything_That_Is_Not_A_Letter_Or_Digit(
            string text, string pattern, bool expected)
        {
            Assert.Equal(expected, PatternMatch.Contains(text, pattern));
        }

        [Fact]
        public void A_Digit_Counts_As_Part_Of_A_Word()
        {
            // A gauge is not a prefix of another gauge: g28 sheeting and g285
            // are different products.
            Assert.True(PatternMatch.Contains("sheet g28 corrugated", "g28"));
            Assert.False(PatternMatch.Contains("sheet g285 corrugated", "g28"));
        }

        [Fact]
        public void A_Multi_Word_Pattern_Is_Bounded_At_Both_Ends()
        {
            Assert.True(PatternMatch.Contains("Steel barge board 150", "barge board"));
            Assert.False(PatternMatch.Contains("barge boarding", "barge board"));
        }

        [Theory]
        [InlineData(null, "ridge")]
        [InlineData("", "ridge")]
        [InlineData("anything", null)]
        [InlineData("anything", "")]
        [InlineData("anything", "   ")]
        public void Nothing_Matches_Nothing(string text, string pattern)
        {
            Assert.False(PatternMatch.Contains(text, pattern));
        }

        [Fact]
        public void An_Inner_Hit_Does_Not_Stop_The_Search()
        {
            // "bridge" first, a real "ridge" after. Returning on the first
            // occurrence would miss it.
            Assert.True(PatternMatch.Contains("Bridge detail and ridge capping", "ridge"));
        }

        // ── the shipped patterns, against the names that broke them ─────────

        [Fact]
        public void No_Shipped_Stage_Pattern_Claims_The_Van_Gogh()
        {
            var lib = JsonConvert.DeserializeObject<StageLibrary>(
                File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory,
                                              "Data", "STING_MATERIAL_STAGES.json")));

            const string painting = "Art_Piece_-_Draw_Bridge_at_Arles_-_Van_Gogh_5866";

            foreach (var st in lib.Stages)
                foreach (string p in st.TypePatterns ?? new List<string>())
                    Assert.False(PatternMatch.Contains(painting, p),
                                 $"stage '{st.StageId}' pattern '{p}' claims a painting");
        }
    }
}
