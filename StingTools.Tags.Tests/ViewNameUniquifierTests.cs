using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>DTW-53: the unique view name is never one that exists. The plugin calls the same file.</summary>
    public class ViewNameUniquifierTests
    {

        [Fact]
        public void The_unique_name_is_never_one_that_exists()
        {
            var taken = new HashSet<string> { "Plan" };
            for (int i = 2; i <= 150; i++) taken.Add($"Plan_({i})");
            var got = ViewNameUniquifier.Next("Plan", taken.Contains);
            Assert.Equal("Plan_(151)", got);
            Assert.DoesNotContain(got, taken);
        }

        [Fact]
        public void Running_out_is_null_not_a_taken_name()
            => Assert.Null(ViewNameUniquifier.Next("Plan", _ => true, limit: 5));
    }
}
