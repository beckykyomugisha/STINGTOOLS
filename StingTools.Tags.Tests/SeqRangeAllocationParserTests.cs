using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DSCH-27 — SEQ_RANGE_ALLOCATION was documented and read but never loaded. The
    /// parser accepts both documented value shapes, keyed by DISC, and refuses (with
    /// a reported problem) anything it cannot read, rather than guessing a range.
    /// </summary>
    public class SeqRangeAllocationParserTests
    {
        [Fact]
        public void ReadsArrayAndObjectShapes()
        {
            var problems = new List<string>();
            var r = SeqRangeAllocationParser.Parse(JObject.Parse(
                @"{ ""M"": [1, 4999], ""e"": { ""min"": 5000, ""max"": 7999 }, ""_note"": ""x"" }"), problems);
            Assert.Empty(problems);
            Assert.Equal(2, r.Count);
            Assert.Equal((1, 4999), r["M"]);
            Assert.Equal((5000, 7999), r["E"]);   // keys are case-insensitive DISC codes
        }

        [Theory]
        [InlineData(@"{ ""M"": [10, 5] }")]
        [InlineData(@"{ ""M"": [0, 5] }")]
        [InlineData(@"{ ""M"": [1] }")]
        [InlineData(@"{ ""M"": ""1-9999"" }")]
        [InlineData(@"{ ""M"": { ""min"": 1 } }")]
        [InlineData(@"{ ""M"": [1.5, 9] }")]
        public void MalformedEntries_AreIgnoredAndReported(string json)
        {
            var problems = new List<string>();
            var r = SeqRangeAllocationParser.Parse(JObject.Parse(json), problems);
            Assert.Empty(r);
            Assert.Single(problems);
        }

        [Fact]
        public void AbsentOrNonObject_GivesNoAllocation()
        {
            var problems = new List<string>();
            Assert.Empty(SeqRangeAllocationParser.Parse(null, problems));
            Assert.Empty(problems);
            Assert.Empty(SeqRangeAllocationParser.Parse(JArray.Parse("[1,2]"), problems));
            Assert.Single(problems);
        }
    }
}
