using StingTools.BOQ.Rates;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// DSCH-43 — the BOQ panel's "Rate: Nil / Included in… / Clear outcome" plan.
    /// Every refusal is a message the user sees; nothing is guessed.
    /// </summary>
    public class RateOutcomeEditTests
    {
        private static OutcomeEditPlan Plan(OutcomeEdit e, string inc = "", bool hasV3 = false,
            RateOutcome cur = RateOutcome.Priced, bool unreadable = false, bool locked = false, string by = "")
            => RateOverrideOutcome.PlanEdit(e, inc, hasV3, cur, unreadable, locked, by);

        [Fact]
        public void Nil_Writes_A_Nil_Override()
        {
            var p = Plan(OutcomeEdit.Nil);
            Assert.Null(p.Refusal);
            Assert.True(p.Write);
            Assert.False(p.DeleteV3);
            Assert.Equal(RateOutcome.Nil, p.Outcome);
            Assert.Equal("", p.IncludedIn);
        }

        [Fact]
        public void Nil_Drops_A_Stray_Reference()
            => Assert.Equal("", Plan(OutcomeEdit.Nil, "E10/2").IncludedIn);

        [Fact]
        public void Included_Writes_The_Trimmed_Reference()
        {
            var p = Plan(OutcomeEdit.Included, "  E10/2 ");
            Assert.True(p.Write);
            Assert.Equal(RateOutcome.Included, p.Outcome);
            Assert.Equal("E10/2", p.IncludedIn);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Included_Without_A_Reference_Is_Refused(string inc)
        {
            var p = Plan(OutcomeEdit.Included, inc);
            Assert.False(p.Write);
            Assert.Contains("which item", p.Refusal);
        }

        [Theory]
        [InlineData(OutcomeEdit.Nil)]
        [InlineData(OutcomeEdit.Included)]
        [InlineData(OutcomeEdit.Clear)]
        public void A_Locked_Override_Is_Never_Touched(OutcomeEdit e)
        {
            var p = Plan(e, "E1", hasV3: true, cur: RateOutcome.Nil, locked: true, by: "QS Cert");
            Assert.False(p.Write);
            Assert.False(p.DeleteV3);
            Assert.Contains("locked by QS Cert", p.Refusal);
        }

        [Theory]
        [InlineData(RateOutcome.Nil)]
        [InlineData(RateOutcome.Included)]
        public void Clear_Removes_A_Declared_V3_Outcome(RateOutcome cur)
        {
            var p = Plan(OutcomeEdit.Clear, hasV3: true, cur: cur);
            Assert.True(p.DeleteV3);
            Assert.False(p.Write);
            Assert.Null(p.Refusal);
        }

        [Fact]
        public void Clear_Removes_An_Unreadable_V3_Entity()
            => Assert.True(Plan(OutcomeEdit.Clear, hasV3: true, cur: RateOutcome.Priced, unreadable: true).DeleteV3);

        [Fact]
        public void Clear_Refuses_A_Priced_V3_Override()
        {
            // A priced rate is not an outcome — Clear must not delete somebody's rate.
            var p = Plan(OutcomeEdit.Clear, hasV3: true, cur: RateOutcome.Priced);
            Assert.False(p.DeleteV3);
            Assert.NotNull(p.Refusal);
        }

        [Fact]
        public void Clear_With_No_V3_Entity_Is_Refused()
            => Assert.NotNull(Plan(OutcomeEdit.Clear, hasV3: false, cur: RateOutcome.Nil).Refusal);

        [Theory]
        [InlineData("Nil", OutcomeEdit.Nil)]
        [InlineData("included", OutcomeEdit.Included)]
        [InlineData(" Clear ", OutcomeEdit.Clear)]
        public void Edit_Names_Parse(string raw, OutcomeEdit expected)
        {
            Assert.True(RateOverrideOutcome.TryParseEdit(raw, out var e));
            Assert.Equal(expected, e);
        }

        [Theory]
        [InlineData("")]
        [InlineData("Priced")]
        [InlineData("7")]
        [InlineData(null)]
        public void Other_Edit_Names_Are_Refused(string raw)
            => Assert.False(RateOverrideOutcome.TryParseEdit(raw, out _));
    }
}
