using System.Collections.Generic;
using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — placement of the rows no model can produce.
    ///
    /// Both behaviours here were defects in the first implementation:
    ///   • an empty provisional-sum category matched the FIRST section, because
    ///     "ANYTHING".IndexOf("") returns 0 — so a 30m electrical PS could file
    ///     itself under Tools and Equipment;
    ///   • the labour suggestion summed EVERY model row for every section, so
    ///     each stage advertised the whole project's labour as its own.
    /// </summary>
    public class ManualRowPlacerTests
    {
        private static List<StageSection> Stages() => new List<StageSection>
        {
            new StageSection { StageId = "tools",      Title = "TOOLS AND EQUIPMENT" },
            new StageSection { StageId = "electrical", Title = "ELEMENT 06: ELECTRICAL INSTALLATION" },
            new StageSection { StageId = "mechanical", Title = "ELEMENT 07: MECHANICAL INSTALLATION" }
        };

        // ── section matching ────────────────────────────────────────────────

        [Fact]
        public void An_Empty_Category_Matches_Nothing_Rather_Than_The_First_Section()
        {
            Assert.Null(ManualRowPlacer.FindSectionForCategory(Stages(), ""));
            Assert.Null(ManualRowPlacer.FindSectionForCategory(Stages(), null));
            Assert.Null(ManualRowPlacer.FindSectionForCategory(Stages(), "   "));
        }

        [Fact]
        public void A_Real_Category_Still_Matches_Its_Section()
        {
            var hit = ManualRowPlacer.FindSectionForCategory(Stages(), "Electrical");
            Assert.NotNull(hit);
            Assert.Equal("electrical", hit.StageId);
        }

        [Fact]
        public void An_Unmatched_Category_Returns_Null_So_The_Caller_Mints_A_Section()
        {
            Assert.Null(ManualRowPlacer.FindSectionForCategory(Stages(), "Landscaping"));
        }

        [Fact]
        public void Matching_Is_Case_Insensitive()
        {
            Assert.Equal("mechanical",
                ManualRowPlacer.FindSectionForCategory(Stages(), "MECHANICAL")?.StageId);
        }

        // ── labour suggestion ───────────────────────────────────────────────

        private static StageSection SectionTracing(params string[] refs)
        {
            var s = new StageSection { StageId = "substructure", Title = "SUB-STRUCTURE" };
            s.Commodities.Add(new MaterialCommodity
            {
                CommodityKey = "cement",
                TraceRefs = refs.ToList()
            });
            return s;
        }

        [Fact]
        public void The_Suggestion_Counts_Only_The_Rows_That_Fed_This_Section()
        {
            var rows = new List<LabourContribution>
            {
                new LabourContribution { TraceRef = "A", LabourTotalUGX = 1_000_000, HasSplit = true },
                new LabourContribution { TraceRef = "B", LabourTotalUGX = 2_000_000, HasSplit = true },
                new LabourContribution { TraceRef = "Z", LabourTotalUGX = 9_000_000, HasSplit = true } // other stage
            };

            var line = ManualRowPlacer.BuildLabourLine(SectionTracing("A", "B"), rows);

            Assert.Equal(3_000_000, line.SuggestedUGX);
            Assert.Contains("2 of 2", line.SuggestionBasis);
        }

        [Fact]
        public void No_Suggestion_When_Any_Contributing_Row_Lacks_A_Split()
        {
            var rows = new List<LabourContribution>
            {
                new LabourContribution { TraceRef = "A", LabourTotalUGX = 1_000_000, HasSplit = true },
                new LabourContribution { TraceRef = "B", LabourTotalUGX = 0,         HasSplit = false }
            };

            var line = ManualRowPlacer.BuildLabourLine(SectionTracing("A", "B"), rows);

            Assert.Null(line.SuggestedUGX);
            Assert.Contains("1 of 2", line.SuggestionBasis);
        }

        [Fact]
        public void No_Suggestion_When_The_Section_Traces_To_Nothing()
        {
            var rows = new List<LabourContribution>
            {
                new LabourContribution { TraceRef = "A", LabourTotalUGX = 1_000_000, HasSplit = true }
            };

            var line = ManualRowPlacer.BuildLabourLine(SectionTracing(), rows);

            Assert.Null(line.SuggestedUGX);
        }

        [Fact]
        public void The_Labour_Line_Never_Contributes_To_The_Total()
        {
            var rows = new List<LabourContribution>
            {
                new LabourContribution { TraceRef = "A", LabourTotalUGX = 5_000_000, HasSplit = true }
            };

            var line = ManualRowPlacer.BuildLabourLine(SectionTracing("A"), rows);

            // The suggestion is advisory. Only a QS-entered AmountUGX counts.
            Assert.Equal(0, line.AmountUGX);
            Assert.Equal(5_000_000, line.SuggestedUGX);
        }

        [Fact]
        public void Two_Sections_Get_Two_Different_Suggestions()
        {
            var rows = new List<LabourContribution>
            {
                new LabourContribution { TraceRef = "A", LabourTotalUGX = 1_000_000, HasSplit = true },
                new LabourContribution { TraceRef = "B", LabourTotalUGX = 7_000_000, HasSplit = true }
            };

            var first = ManualRowPlacer.BuildLabourLine(SectionTracing("A"), rows);
            var second = ManualRowPlacer.BuildLabourLine(SectionTracing("B"), rows);

            Assert.Equal(1_000_000, first.SuggestedUGX);
            Assert.Equal(7_000_000, second.SuggestedUGX);
        }
    }
}
