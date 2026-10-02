using System;
using StingTools.BOQ.Rates;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// DSCH-33. The element rate override (Extensible Storage) could not say Nil or
    /// Included: v1 / v2 have no field for it and ES schemas are immutable. v3 stores
    /// Outcome + IncludedIn; these tests hold the Revit-free half — the field encoding,
    /// the write rule, and the answer the ES provider gives the chain.
    /// </summary>
    public class RateOverrideOutcomeTests
    {
        [Theory]
        [InlineData(RateOutcome.Priced)]
        [InlineData(RateOutcome.Nil)]
        [InlineData(RateOutcome.Included)]
        public void Every_outcome_round_trips_through_the_stored_field(RateOutcome outcome)
        {
            string stored = RateOverrideOutcome.Encode(outcome);
            Assert.True(RateOverrideOutcome.TryDecode(stored, out var back));
            Assert.Equal(outcome, back);
        }

        [Fact]
        public void Every_enum_member_has_a_distinct_stored_name()
        {
            // A new RateOutcome member must get its own name, not fall into "Priced".
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RateOutcome o in Enum.GetValues(typeof(RateOutcome)))
            {
                string name = RateOverrideOutcome.Encode(o);
                Assert.True(seen.Add(name), $"{o} shares stored name '{name}'");
                Assert.True(RateOverrideOutcome.TryDecode(name, out var back) && back == o, $"{o} does not round-trip");
            }
        }

        [Fact]
        public void Stored_text_is_the_enum_name_not_its_ordinal()
        {
            Assert.Equal("Nil", RateOverrideOutcome.Encode(RateOutcome.Nil));
            Assert.Equal("Included", RateOverrideOutcome.Encode(RateOutcome.Included));
            Assert.Equal("Priced", RateOverrideOutcome.Encode(RateOutcome.Priced));
        }

        [Theory]
        [InlineData(" nil ", RateOutcome.Nil)]
        [InlineData("INCLUDED", RateOutcome.Included)]
        [InlineData("priced", RateOutcome.Priced)]
        public void Decode_ignores_case_and_space(string stored, RateOutcome expected)
        {
            Assert.True(RateOverrideOutcome.TryDecode(stored, out var o));
            Assert.Equal(expected, o);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("1")]
        [InlineData("0")]
        [InlineData("NIL:x")]
        [InlineData("INCL")]   // a rate-cell token, not the stored field name
        [InlineData("Zero")]
        public void Undecodable_outcome_is_refused_not_read_as_priced(string stored)
        {
            Assert.False(RateOverrideOutcome.TryDecode(stored, out _));
        }

        [Fact]
        public void A_nil_override_answers_nil_at_zero_and_stops_the_chain()
        {
            var a = RateOverrideOutcome.Resolve(0, RateOutcome.Nil, "", overheadPercent: 10, profitPercent: 5);
            Assert.Equal(RateOutcome.Nil, a.Outcome);
            Assert.Equal(0, a.UnitRate);
            Assert.Equal("", a.IncludedIn);
            Assert.StartsWith("Nil", a.OutcomeText);
            Assert.Equal(RateChainStep.Accept, RateChainRule.Decide(true, a.Outcome, a.UnitRate));
        }

        [Fact]
        public void An_included_override_carries_its_reference_and_stops_the_chain()
        {
            var a = RateOverrideOutcome.Resolve(0, RateOutcome.Included, " E10/2 ", 0, 0);
            Assert.Equal(RateOutcome.Included, a.Outcome);
            Assert.Equal(0, a.UnitRate);
            Assert.Equal("E10/2", a.IncludedIn);
            Assert.StartsWith("Incl. in E10/2", a.OutcomeText);
            Assert.Equal(RateChainStep.Accept, RateChainRule.Decide(true, a.Outcome, a.UnitRate));
        }

        [Fact]
        public void A_priced_override_is_loaded_with_overhead_then_profit()
        {
            var a = RateOverrideOutcome.Resolve(1000, RateOutcome.Priced, "", overheadPercent: 10, profitPercent: 5);
            Assert.Equal(RateOutcome.Priced, a.Outcome);
            Assert.Equal(1000 * 1.10 * 1.05, a.UnitRate, 6);
            Assert.Equal(RateChainStep.Accept, RateChainRule.Decide(true, a.Outcome, a.UnitRate));
        }

        [Fact]
        public void A_priced_zero_from_a_v1_or_v2_entity_is_an_undeclared_zero()
        {
            // v1 / v2 entities read as Priced. A stored 0 there is NOT a nil.
            var a = RateOverrideOutcome.Resolve(0, RateOutcome.Priced, "", 10, 5);
            Assert.Equal(0, a.UnitRate);
            Assert.Equal(RateChainStep.ContinueUndeclaredZero, RateChainRule.Decide(true, a.Outcome, a.UnitRate));
        }

        [Fact]
        public void Write_rule_accepts_the_three_valid_shapes()
        {
            Assert.Null(RateOverrideOutcome.CheckWrite(1500, RateOutcome.Priced, ""));
            Assert.Null(RateOverrideOutcome.CheckWrite(0, RateOutcome.Priced, null));   // v1 copy-forward
            Assert.Null(RateOverrideOutcome.CheckWrite(0, RateOutcome.Nil, ""));
            Assert.Null(RateOverrideOutcome.CheckWrite(0, RateOutcome.Included, "E10/2"));
            Assert.Null(RateOverrideOutcome.CheckWrite(0, RateOutcome.Included, ""));
        }

        [Theory]
        [InlineData(-1, RateOutcome.Priced, "")]
        [InlineData(double.NaN, RateOutcome.Priced, "")]
        [InlineData(250, RateOutcome.Nil, "")]
        [InlineData(250, RateOutcome.Included, "E10/2")]
        [InlineData(250, RateOutcome.Priced, "E10/2")]
        [InlineData(0, RateOutcome.Nil, "E10/2")]
        public void Write_rule_refuses_contradictions(double rate, RateOutcome outcome, string includedIn)
        {
            Assert.NotNull(RateOverrideOutcome.CheckWrite(rate, outcome, includedIn));
        }
    }
}
