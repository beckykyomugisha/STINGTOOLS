using StingTools.Core.Plumbing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Drainage invert levels. Three copies of this calculation disagreed: the
    /// annotation used the NOMINAL diameter and the pipe midpoint, the engine also
    /// assumed endpoint 0 was upstream and wrote to parameters defined nowhere, and
    /// the manhole schedule used connector radii. The invert is the bottom of the
    /// BORE — centreline minus the internal radius (BS EN 752 / BS EN 12056-2).
    /// </summary>
    public class InvertMathTests
    {
        [Fact]
        public void Internal_diameter_gives_the_bore_invert()
        {
            // 110 mm uPVC soil pipe: OD 110, ID ~103.2. Centreline at 10.000 m.
            var il = InvertMath.Invert(10.000, 0.1032, 0.110, out var src);
            Assert.Equal(9.9484, il.Value, 4);
            Assert.Equal(InvertSource.InnerDiameter, src);
        }

        [Fact]
        public void Nominal_is_only_a_flagged_fallback()
        {
            var il = InvertMath.Invert(10.000, null, 0.110, out var src);
            Assert.Equal(9.945, il.Value, 4);
            Assert.Equal(InvertSource.NominalFallback, src);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-0.05)]
        public void A_non_positive_internal_diameter_falls_through(double badId)
        {
            InvertMath.Invert(10.0, badId, 0.110, out var src);
            Assert.Equal(InvertSource.NominalFallback, src);
        }

        [Fact]
        public void No_diameter_means_no_invert_not_the_centreline()
        {
            Assert.Null(InvertMath.Invert(10.0, null, null, out var src));
            Assert.Equal(InvertSource.None, src);
        }

        [Fact]
        public void An_invert_below_datum_is_a_real_level()
        {
            // The manhole schedule used to treat <= 0 as "missing".
            Assert.Equal(-1.5516, InvertMath.Invert(-1.500, 0.1032, null, out _).Value, 4);
        }

        [Theory]
        [InlineData(10.0, 9.9, 0)]    // end 0 higher → upstream
        [InlineData(9.9, 10.0, 1)]    // drawn the other way round → end 1 upstream
        public void Upstream_is_the_higher_end_not_endpoint_zero(double z0, double z1, int expected)
        {
            Assert.Equal(expected, InvertMath.UpstreamIndex(z0, z1, out var level));
            Assert.False(level);
        }

        [Fact]
        public void Level_pipe_is_reported_as_level()
        {
            InvertMath.UpstreamIndex(10.0, 10.0002, out var level);
            Assert.True(level);
            Assert.Null(InvertMath.GradientText(10.0, 10.0, 10.0));
        }

        [Fact]
        public void Gradient_prints_as_one_in_x()
            => Assert.Equal("1:100", InvertMath.GradientText(10.000, 9.900, 10.0));

        [Fact]
        public void IL_label_uses_the_configured_precision_invariantly()
        {
            Assert.Equal("IL 9.95", InvertMath.FormatIl(9.9484, 2));
            Assert.Equal("IL 9.948", InvertMath.FormatIl(9.9484, 3));
        }

        [Theory]
        [InlineData("IL 9.95", true)]
        [InlineData("1:80", true)]
        [InlineData("IL LEVELS SEE DRG 101", false)]   // a person's note is never ours to rewrite
        [InlineData("Note: 1:50 scale", false)]
        [InlineData("", false)]
        public void Only_our_own_notes_are_recognised_for_update(string text, bool ours)
            => Assert.Equal(ours, InvertMath.IsOurNote(text));

        [Theory]
        [InlineData("9.948", 9.948)]
        [InlineData(" 9.948 m", 9.948)]
        [InlineData("-1.200", -1.2)]
        public void Stored_text_parses_as_metres(string text, double expected)
            => Assert.Equal(expected, InvertMath.ParseMetres(text).Value, 4);

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("n/a")]
        public void Blank_or_garbage_is_unknown_not_zero(string text)
            => Assert.Null(InvertMath.ParseMetres(text));

        [Fact]
        public void Param_text_round_trips()
            => Assert.Equal(9.948, InvertMath.ParseMetres(InvertMath.ToParamText(9.9484)).Value, 3);

        [Fact]
        public void Default_reporting_is_survey_point_two_decimals()
        {
            // The owner decision point. Changing it is a deliberate act; this makes it one.
            var o = IlReportingOptions.Default;
            Assert.Equal(IlDatum.SurveyPoint, o.Datum);
            Assert.Equal(2, o.Decimals);
        }
    }
}
