using System;
using System.IO;
using System.Linq;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// G-34 — two block-count formulas disagreed by 15.8 %, on two axes at once.
    ///
    ///   CST_CALC_BLOCKS_NR   = NET   area × BLOCKS_PER_M2 × 1.03            (frozen waste)
    ///   CST_S_MAS_BLOCKS_NR  = GROSS area × BLOCKS_PER_M2 × (1 + waste/100) (parameter waste)
    ///
    /// 50 m² gross, 6 m² openings, BLOCK 400x200 (12.5/m²), waste 5 %:
    ///   566 vs 656 blocks — 90 blocks on ONE wall, across seven cottages a four-figure quantity.
    ///
    /// Neither flags, because neither DEFAULTS — both lookups resolve cleanly. G-27's
    /// measured/assumed flag cannot catch this class at all, which is why it needed
    /// its own sweep.
    ///
    /// Resolved to ONE owner on the basis that neither formula as written was right:
    /// blocks are not bought for door openings (so NET), and 3 % should not be frozen
    /// in a formula when a project-tunable wastage parameter already exists.
    ///
    /// These are data regression locks, not arithmetic tests — the defect was in the
    /// formula CSV, so that is what is asserted.
    /// </summary>
    public class BlockCountG34Tests
    {
        private static string CsvPath =>
            Path.Combine(AppContext.BaseDirectory, "Data", "FORMULAS_WITH_DEPENDENCIES.csv");

        private static string RowFor(string name) =>
            File.ReadAllLines(CsvPath)
                .FirstOrDefault(l => l.Split(',').Length > 1 && l.Split(',')[1] == name);

        [Fact]
        public void Only_One_Block_Count_Formula_Survives()
        {
            Assert.NotNull(RowFor("CST_CALC_BLOCKS_NR"));
            // The gross-area/parameter-waste twin is gone. Two owners of one physical
            // quantity is the defect; which one survived is the decision.
            Assert.Null(RowFor("CST_S_MAS_BLOCKS_NR"));
        }

        [Fact]
        public void The_Survivor_Uses_NET_Area_Not_Gross()
        {
            string row = RowFor("CST_CALC_BLOCKS_NR");
            Assert.Contains("CST_S_MAS_NET_AREA_SQ_M", row);
            Assert.DoesNotContain("CST_S_MAS_WALL_AREA_SQ_M", row);
        }

        [Fact]
        public void The_Survivor_Uses_The_Wastage_Parameter_Not_A_Frozen_Constant()
        {
            string row = RowFor("CST_CALC_BLOCKS_NR");
            Assert.Contains("CST_S_MAS_WASTAGE_FCT_PCT", row);
            // The frozen 3 % is gone. An unset wastage parameter now SKIPS the formula
            // (G-5: a failure is absent, not blank) rather than silently applying zero
            // waste, so removing the constant cannot under-measure.
            Assert.DoesNotContain("* 1.03", row);
        }

        [Fact]
        public void Nothing_Still_References_The_Deleted_Twin()
        {
            // The G-15 lesson: deleting a formula strands anything that summed it.
            string all = File.ReadAllText(CsvPath);
            Assert.DoesNotContain("CST_S_MAS_BLOCKS_NR", all);
        }
    }
}
