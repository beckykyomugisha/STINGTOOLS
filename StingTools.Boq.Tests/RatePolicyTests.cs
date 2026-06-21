using StingTools.BOQ.Rates;
using Xunit;

namespace StingTools.Boq.Tests
{
    // Phase 195 — host-free rate-provider policy overlay (boq_rate_policy.json).
    public class RatePolicyTests
    {
        [Fact]
        public void Parse_Null_Or_Blank_Yields_NoOp_Policy()
        {
            foreach (var raw in new[] { null, "", "   " })
            {
                var p = RatePolicy.Parse(raw);
                Assert.True(p.IsEnabled("es-override"));
                // Unset provider keeps its baseline priority.
                Assert.Equal(95, p.EffectivePriority("es-override", 95));
            }
        }

        [Fact]
        public void Parse_Malformed_Json_Falls_Back_To_NoOp()
        {
            var p = RatePolicy.Parse("{ this is not json ");
            Assert.True(p.IsEnabled("bcis-http"));
            Assert.Equal(50, p.EffectivePriority("bcis-http", 50));
        }

        [Fact]
        public void Priority_Override_Is_Applied_Baseline_Otherwise()
        {
            string json = @"{
              ""providers"": {
                ""es-override"":       { ""priority"": 98 },
                ""project-rate-card"": { ""priority"": 93 },
                ""material-library"":  { ""priority"": 85 }
              }
            }";
            var p = RatePolicy.Parse(json);

            Assert.Equal(98, p.EffectivePriority("es-override", 95));        // overridden
            Assert.Equal(93, p.EffectivePriority("project-rate-card", 87));  // overridden
            Assert.Equal(85, p.EffectivePriority("material-library", 95));   // overridden
            Assert.Equal(96, p.EffectivePriority("fohlio", 96));             // untouched → baseline
            Assert.Equal(100, p.EffectivePriority("param-override", 100));   // untouched → baseline
        }

        [Fact]
        public void Enabled_False_Disables_Only_The_Named_Provider()
        {
            string json = @"{ ""providers"": { ""bcis-http"": { ""enabled"": false } } }";
            var p = RatePolicy.Parse(json);

            Assert.False(p.IsEnabled("bcis-http"));
            Assert.True(p.IsEnabled("csv-default"));   // unnamed → enabled
            Assert.True(p.IsEnabled("es-override"));    // unnamed → enabled
        }

        [Fact]
        public void Provider_Ids_Match_Case_Insensitively()
        {
            string json = @"{ ""providers"": { ""ES-Override"": { ""priority"": 98, ""enabled"": false } } }";
            var p = RatePolicy.Parse(json);

            Assert.Equal(98, p.EffectivePriority("es-override", 95));
            Assert.False(p.IsEnabled("es-override"));
        }

        [Fact]
        public void Entry_With_Only_Enabled_Leaves_Priority_At_Baseline()
        {
            string json = @"{ ""providers"": { ""fohlio"": { ""enabled"": true } } }";
            var p = RatePolicy.Parse(json);

            Assert.True(p.IsEnabled("fohlio"));
            Assert.Equal(96, p.EffectivePriority("fohlio", 96)); // priority unset → baseline
        }
    }
}
