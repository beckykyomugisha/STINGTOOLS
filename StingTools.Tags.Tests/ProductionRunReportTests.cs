using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-195 / 204 / 205. One owned view used to roll back a whole level and report
    /// "Run LoadSharedParams"; a large run gave no sign of life and could not be
    /// stopped; warnings past the 20th were dropped without being logged.
    /// </summary>
    public class ProductionRunReportTests
    {
        [Fact]
        public void Nothing_blocking_is_null()
            => Assert.Null(ProductionRunReport.BlockReason(null, new string[0], null));

        [Fact]
        public void Owned_elements_name_the_owner_once_per_person()
        {
            var r = ProductionRunReport.BlockReason(new[]
            {
                new KeyValuePair<string, string>("Level 1 - Power", "jsmith"),
                new KeyValuePair<string, string>("E-101", "jsmith"),
                new KeyValuePair<string, string>("Level 1 - Lighting", "akato"),
            }, null, null);
            Assert.Equal("owned by akato ('Level 1 - Lighting'); owned by jsmith ('Level 1 - Power', 'E-101')", r);
        }

        [Fact]
        public void Out_of_date_tells_the_user_to_reload_latest()
        {
            var r = ProductionRunReport.BlockReason(null, new[] { "E-101" }, null);
            Assert.Contains("not up to date", r);
            Assert.Contains("reload latest", r);
        }

        [Fact]
        public void Skip_line_says_nothing_was_changed_and_never_blames_shared_parameters()
        {
            var line = ProductionRunReport.Skipped("Level 2", "owned by jsmith ('E-102')");
            Assert.StartsWith("Level 2: skipped — owned by jsmith", line);
            Assert.DoesNotContain("LoadSharedParams", line);
        }

        [Fact]
        public void Stopped_line_counts_what_was_done()
            => Assert.Equal("Stopped (Escape) after 3 of 12 level(s); what was done before the stop is kept.",
                ProductionRunReport.Stopped(3, 12, "level(s)"));

        [Fact]
        public void Csv_has_every_warning_quoted()
        {
            var warnings = Enumerable.Range(1, 45).Select(i => $"w{i}, with \"quotes\"").ToList();
            var csv = ProductionRunReport.Csv(warnings).TrimEnd().Split('\n');
            Assert.Equal(46, csv.Length);
            Assert.Equal("No,Warning", csv[0].TrimEnd('\r'));
            Assert.Equal("45,\"w45, with \"\"quotes\"\"\"", csv[45].TrimEnd('\r'));
        }

        [Fact]
        public void Warning_block_points_at_the_file_for_the_rest()
        {
            var warnings = Enumerable.Range(1, 25).Select(i => "w" + i).ToList();
            var block = ProductionRunReport.WarningBlock(warnings, @"C:\out\warnings.csv");
            Assert.Contains("w20", block);
            Assert.DoesNotContain("w21", block);
            Assert.Contains("…and 5 more", block);
            Assert.Contains(@"C:\out\warnings.csv", block);
        }

        [Fact]
        public void Warning_block_says_when_the_file_could_not_be_written()
            => Assert.Contains("could not be written",
                ProductionRunReport.WarningBlock(Enumerable.Range(1, 21).Select(i => "w" + i).ToList(), null));
    }
}
