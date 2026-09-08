using System;
using System.IO;
using System.Linq;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// Mortar volume was computed TWICE, by two rules that disagree.
    ///
    /// <para><c>CST_S_MAS_MORTAR_VOLUME_CU_M</c> measures net wall face against the
    /// bond-type mortar ratio, and feeds cement bags and sand. <c>CST_CALC_MORTAR_M3</c>
    /// multiplied the block count by a flat 0.0063 m³ and fed nothing — but it is a
    /// SCHEDULE parameter, so it was a visible column carrying a second, quietly
    /// different answer to the same question.</para>
    ///
    /// <para>The duplicate is resolved by ALIAS rather than deletion. Deleting the
    /// formula would leave the schedule column blank, and deleting the parameter is
    /// forbidden — it may be bound in live models. Aliasing leaves one calculation and
    /// one answer, with the column still populated.</para>
    /// </summary>
    public class MortarSingleSourceTests
    {
        private const string Authority = "CST_S_MAS_MORTAR_VOLUME_CU_M";
        private const string Alias = "CST_CALC_MORTAR_M3";

        private static string[] Rows()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data",
                       "FORMULAS_WITH_DEPENDENCIES.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "FORMULAS_WITH_DEPENDENCIES.csv not found above " + AppContext.BaseDirectory);
            return File.ReadAllLines(Path.Combine(dir.FullName, "StingTools", "Data",
                                                  "FORMULAS_WITH_DEPENDENCIES.csv"));
        }

        private static string RowFor(string param) =>
            Rows().FirstOrDefault(l => l.Split(',').Length > 1 && l.Split(',')[1].Trim() == param);

        [Fact]
        public void Only_One_Rule_Computes_Mortar()
        {
            string alias = RowFor(Alias);
            Assert.NotNull(alias);
            // The alias must DERIVE from the authority, not restate its arithmetic.
            Assert.Contains(Authority, alias);
            Assert.DoesNotContain("0.0063", alias);
        }

        [Fact]
        public void The_Authority_Still_Measures_Net_Area_Against_The_Bond_Ratio()
        {
            string row = RowFor(Authority);
            Assert.NotNull(row);
            Assert.Contains("CST_S_MAS_NET_AREA_SQ_M", row);
            Assert.Contains("MORTAR_RATIO", row);
        }

        [Fact]
        public void The_Alias_Is_Still_A_Schedule_Parameter_So_Its_Column_Is_Not_Blank()
        {
            // Deleting the formula was the obvious fix and the wrong one: this
            // parameter is published on a schedule, so an absent formula shows a
            // reader an empty cell rather than a number.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data",
                       "TPL_SCHEDULE_METADATA.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "TPL_SCHEDULE_METADATA.csv not found");
            string meta = File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "Data",
                                                        "TPL_SCHEDULE_METADATA.csv"));
            Assert.Contains(Alias, meta);
        }

        [Fact]
        public void Cement_And_Sand_Still_Read_The_Authority_Not_The_Alias()
        {
            // If a consumer ever moved to the alias the chain would be circular:
            // alias -> authority -> ... -> alias. Nothing evaluates, silently.
            foreach (string consumer in new[] { "CST_S_MAS_CEMENT_BAGS_NR", "CST_S_MAS_SAND_VOLUME_CU_M" })
            {
                string row = RowFor(consumer);
                Assert.NotNull(row);
                Assert.Contains(Authority, row);
                Assert.DoesNotContain(Alias, row);
            }
        }
    }
}
