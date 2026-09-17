using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// The NOT WRITTEN block has to name the CATEGORY, not just the parameter.
    /// A binding is repaired for one parameter ON one category in Manage > Project
    /// Parameters, so "ASS_FUNC_TXT x 27" alone still leaves the operator hunting
    /// for where — which is the same half-answer as "run FamilyStagePopulate".
    /// </summary>
    public class TokenWriteFailureReportTests
    {
        private static TaggingStats StatsWithFailures()
        {
            var s = new TaggingStats();
            for (int i = 0; i < 27; i++)
                s.RecordTokenWriteFailure(1000 + i, "Rooms",
                    new[] { "ASS_FUNC_TXT", "ASS_PRODCT_COD_TXT" });
            for (int i = 0; i < 175; i++)
                s.RecordTokenWriteFailure(2000 + i, "Doors", new[] { "ASS_SEQ_NUM_TXT" });
            return s;
        }

        [Fact]
        public void Counts_elements_not_parameter_writes()
            => Assert.Equal(202, StatsWithFailures().TokenWriteFailureCount);

        [Fact]
        public void Groups_element_counts_by_category()
        {
            var s = StatsWithFailures();
            Assert.Equal(27, s.TokenWriteFailuresByCategory["Rooms"]);
            Assert.Equal(175, s.TokenWriteFailuresByCategory["Doors"]);
        }

        [Fact]
        public void Keeps_the_category_and_parameter_paired()
        {
            var s = StatsWithFailures();
            Assert.Equal(27, s.TokenWriteFailureDetail["Rooms"]["ASS_FUNC_TXT"]);
            Assert.Equal(175, s.TokenWriteFailureDetail["Doors"]["ASS_SEQ_NUM_TXT"]);
            Assert.False(s.TokenWriteFailureDetail["Doors"].ContainsKey("ASS_FUNC_TXT"));
        }

        [Fact]
        public void Report_names_the_category_beside_the_parameter()
        {
            string report = StatsWithFailures().BuildReport();

            Assert.Contains("NOT WRITTEN", report);
            // The category must appear, with its element count...
            Assert.Contains("Rooms", report);
            Assert.Contains("Doors", report);
            // ...and each parameter must be nested under the category it failed on.
            // Categories are ordered worst-first, so Doors (175) precedes Rooms (27).
            int doors = report.IndexOf("Doors");
            int seq   = report.IndexOf("ASS_SEQ_NUM_TXT");
            int rooms = report.IndexOf("Rooms");
            int func  = report.IndexOf("ASS_FUNC_TXT");

            Assert.True(doors >= 0 && rooms > doors,
                "Categories are listed worst-first: Doors (175) before Rooms (27). " + report);
            Assert.True(seq > doors && seq < rooms,
                "ASS_SEQ_NUM_TXT must sit inside the Doors block. " + report);
            Assert.True(func > rooms,
                "ASS_FUNC_TXT must sit inside the Rooms block. " + report);
        }

        // ── CONTAINER-1 ─────────────────────────────────────────────────────
        // 175 of these sat in a log unread: the catch logged a bare message, kept
        // no count and told the report nothing, so the only way to know was to go
        // looking. The failure is non-fatal - the tag is already written - but it
        // has to be visible.

        [Fact]
        public void Container_failures_reach_the_report()
        {
            var s = new TaggingStats();
            for (int i = 0; i < 175; i++)
                s.RecordContainerWriteFailure("Doors", "Object reference not set");

            string report = s.BuildReport();
            Assert.Equal(175, s.ContainerWriteFailureCount);
            Assert.Contains("CONTAINERS", report);
            Assert.Contains("Doors", report);
            Assert.Contains("Object reference not set", report);
        }

        [Fact]
        public void Container_failure_samples_are_deduplicated_and_capped()
        {
            var s = new TaggingStats();
            for (int i = 0; i < 50; i++) s.RecordContainerWriteFailure("Doors", "same message");
            s.RecordContainerWriteFailure("Rooms", "another");
            Assert.Equal(2, s.ContainerWriteFailureSamples.Count);
            Assert.Equal(50, s.ContainerWriteFailuresByCategory["Doors"]);
            Assert.Equal(1, s.ContainerWriteFailuresByCategory["Rooms"]);
        }

        [Fact]
        public void No_container_section_when_none_failed()
            => Assert.DoesNotContain("CONTAINERS", new TaggingStats().BuildReport());

        [Fact]
        public void Silent_when_nothing_failed()
            => Assert.DoesNotContain("NOT WRITTEN", new TaggingStats().BuildReport());

        [Fact]
        public void A_missing_category_is_labelled_not_blank()
        {
            var s = new TaggingStats();
            s.RecordTokenWriteFailure(1, null, new[] { "ASS_FUNC_TXT" });
            Assert.Contains("(unknown category)", s.BuildReport());
        }
    }
}
