using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// ROADMAP ELEC-27: ELC_PNL_CONNECTED_LOAD_KW was filled from the board's APPARENT power
    /// (RBS_ELEC_PANEL_TOTALLOAD_PARAM, VA), so a kVA figure sat under the kW name. It is now
    /// the true power summed over the circuits the board feeds.
    /// </summary>
    public class PanelConnectedLoadTests
    {
        [Fact]
        public void Sums_true_power_per_board_in_kW()
        {
            var kw = PanelConnectedLoadMath.SumKw(new List<(long, double)> { (1, 2300), (1, 4600), (2, 1000) });
            Assert.Equal(6.9, kw[1], 9);
            Assert.Equal(1.0, kw[2], 9);
        }

        [Fact]
        public void Negative_or_non_finite_power_is_not_load_and_a_board_with_no_circuits_is_absent()
        {
            var kw = PanelConnectedLoadMath.SumKw(new List<(long, double)> { (1, -500), (1, double.NaN), (1, 1500) });
            Assert.Equal(1.5, kw[1], 9);
            Assert.False(kw.ContainsKey(3));
        }

        [Fact]
        public void Text_is_invariant_one_decimal() => Assert.Equal("12.5", PanelConnectedLoadMath.KwText(12.46));

        [Theory]
        [InlineData("Commands/Electrical/ElectricalPanelCommands.cs")]
        [InlineData("Core/Panels/PanelScheduleApplyEngine.cs")]
        [InlineData("Core/ParameterHelpers.cs")]
        public void Apparent_power_is_never_written_into_the_kW_field_nor_the_view_name_into_the_designation(string file)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools.addin"))) dir = dir.Parent;
            Assert.NotNull(dir);
            string src = string.Join("\n", File.ReadAllLines(Path.Combine(dir.FullName, "StingTools", file.Replace('/', Path.DirectorySeparatorChar)))
                .Select(l => { int c = l.IndexOf("//", StringComparison.Ordinal); return c >= 0 ? l.Substring(0, c) : l; }));
            Assert.DoesNotMatch(new Regex(@"RBS_ELEC_PANEL_TOTALLOAD_PARAM[^;]*;[^;]*ELC_PNL_LOAD|RBS_ELEC_PANEL_TOTALLOAD_PARAM,\s*ParamRegistry\.ELC_PNL_LOAD"), src);
            Assert.DoesNotMatch(new Regex(@"""Total Connected"""), src);
            Assert.DoesNotMatch(new Regex(@"ELC_PNL_NAME\s*,\s*psv\.Name"), src);
        }
    }
}
