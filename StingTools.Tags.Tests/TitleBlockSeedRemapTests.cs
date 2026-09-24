using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// T-7 — master-seed propagation geometry. Before: text stepped down one
    /// ISO tier only when the spec id contained "_A3", so A2 kept A1-size
    /// text and A0 was untouched by accident; arcs were left 1:1.
    /// </summary>
    public class TitleBlockSeedRemapTests
    {
        private static TitleBlockSeedRemap Make(string master, string target)
        {
            Assert.True(TitleBlockSeedRemap.TryCreate(master, target, out var m));
            return m;
        }

        [Theory]
        [InlineData("STING_TB_A0_BIM_v2.0",        0)]
        [InlineData("STING_TB_A2_BIM_v2.0",       -1)]   // was 0: A2 kept A1 text
        [InlineData("STING_TB_A3_BIM_v2.0",       -1)]
        [InlineData("STING_TB_A2_PORT_NONBIM_v2.0", -1)]
        [InlineData("STING_TB_COVER_A2_v1.0",     -1)]
        [InlineData("STING_TB_COVER_A0_v1.0",      0)]
        public void Text_tier_steps_follow_ISO_3098_paper_class(string target, int steps)
        {
            var master = TitleBlockSeedRemap.ResolveMasterSeedId(target);
            Assert.NotNull(master);
            Assert.Equal(steps, Make(master, target).TextTierSteps);
        }

        [Theory]
        [InlineData(3.5, -1, 2.5)]
        [InlineData(7.0, -1, 5.0)]
        [InlineData(1.8, -1, 1.8)]   // clamped at the smallest tier
        [InlineData(3.0, -1, 2.0)]   // off-series snaps down to 2.5, then one tier
        [InlineData(3.0,  0, 3.0)]   // no step: authored height kept
        [InlineData(7.0, +1, 10.0)]
        public void Step_text_tier(double h, int steps, double expected)
            => Assert.Equal(expected, TitleBlockSeedRemap.StepTextTier(h, steps), 6);

        [Fact]
        public void A2_label_actually_shrinks()
        {
            var m = Make("STING_TB_A1_BIM_v2.0", "STING_TB_A2_BIM_v2.0");
            Assert.Equal(2.5, m.MapTextHeight(3.5), 6);
        }

        [Fact]
        public void Points_scale_by_paper_ratio_and_portrait_uses_portrait_master()
        {
            Assert.Equal("STING_TB_A1_PORT_BIM_v2.0", TitleBlockSeedRemap.ResolveMasterSeedId("STING_TB_A3_PORT_BIM_v2.0"));
            var m = Make("STING_TB_A1_PORT_BIM_v2.0", "STING_TB_A3_PORT_BIM_v2.0");
            var (x, y) = m.MapPoint(594, 841);   // portrait A1 far corner
            Assert.Equal(297, x, 6);
            Assert.Equal(420, y, 6);
        }

        [Fact]
        public void Arc_centre_and_radius_scale_and_ISO_ratios_keep_arcs_round()
        {
            var m = Make("STING_TB_A1_BIM_v2.0", "STING_TB_A3_BIM_v2.0");
            Assert.True(m.ArcSurvives);
            Assert.Equal(10 * System.Math.Sqrt(m.Kx * m.Ky), m.MapRadius(10), 9);
            Assert.InRange(m.MapRadius(10), 4.99, 5.0);
        }

        [Fact]
        public void Masters_and_fab_families_have_no_master()
        {
            Assert.Null(TitleBlockSeedRemap.ResolveMasterSeedId("STING_TB_A1_BIM_v2.0"));
            Assert.Null(TitleBlockSeedRemap.ResolveMasterSeedId("STING_TB_COVER_A1_v1.0"));
            Assert.Null(TitleBlockSeedRemap.ResolveMasterSeedId("STING_TB_ASSEMBLY_PIPE_v1.0"));
            Assert.False(TitleBlockSeedRemap.TryCreate("STING_TB_A1_BIM_v2.0", "STING_TB_ASSEMBLY_PIPE_v1.0", out _));
        }
    }
}
