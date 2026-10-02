using System;
using System.Linq;
using StingTools.BOQ;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// DSCH-35. A provisional sum did not record whether it was Defined or Undefined
    /// (NRM2 2.9.1), which decides whether the contractor is deemed to have allowed for
    /// programming, planning and preliminaries. The declaration is now carried and shown;
    /// a sum without one is NOT defaulted to either kind.
    /// </summary>
    public class ProvisionalSumTypeTests
    {
        [Theory]
        [InlineData("Defined", ProvisionalSumType.Defined)]
        [InlineData(" defined ", ProvisionalSumType.Defined)]
        [InlineData("D", ProvisionalSumType.Defined)]
        [InlineData("UNDEFINED", ProvisionalSumType.Undefined)]
        [InlineData("u", ProvisionalSumType.Undefined)]
        public void Declarations_parse(string raw, ProvisionalSumType expected)
        {
            Assert.True(ProvisionalSumTypes.TryParse(raw, out var t));
            Assert.Equal(expected, t);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("Def.")]
        [InlineData("yes")]
        [InlineData("1")]
        [InlineData("Undeclared")]
        [InlineData("NOT DECLARED")]
        public void Anything_else_is_refused_and_stays_undeclared(string raw)
        {
            Assert.False(ProvisionalSumTypes.TryParse(raw, out var t));
            Assert.Equal(ProvisionalSumType.Undeclared, t);
        }

        [Fact]
        public void The_default_is_undeclared_not_defined()
        {
            // An older stored row, or one nobody classified, must not read as either kind.
            Assert.Equal(ProvisionalSumType.Undeclared, default(ProvisionalSumType));
        }

        [Fact]
        public void Every_type_has_a_marker_and_the_declared_ones_round_trip()
        {
            foreach (ProvisionalSumType t in Enum.GetValues(typeof(ProvisionalSumType)))
            {
                string m = ProvisionalSumTypes.Marker(t);
                Assert.False(string.IsNullOrWhiteSpace(m));
                if (t == ProvisionalSumType.Undeclared)
                    Assert.False(ProvisionalSumTypes.TryParse(m, out _));   // "NOT DECLARED" can never be read back as a declaration
                else
                    Assert.True(ProvisionalSumTypes.TryParse(m, out var back) && back == t);
            }
            Assert.Equal("NOT DECLARED", ProvisionalSumTypes.Marker(ProvisionalSumType.Undeclared));
        }

        [Fact]
        public void An_undeclared_sum_is_flagged_and_a_declared_one_is_not()
        {
            string f = ProvisionalSumTypes.Finding(ProvisionalSumType.Undeclared, "PS.03");
            Assert.NotNull(f);
            Assert.Contains("PS.03", f);
            Assert.Contains("NRM2 2.9.1", f);
            Assert.Null(ProvisionalSumTypes.Finding(ProvisionalSumType.Defined, "PS.01"));
            Assert.Null(ProvisionalSumTypes.Finding(ProvisionalSumType.Undefined, "PS.02"));
        }

        [Fact]
        public void Count_splits_by_declaration()
        {
            var (d, u, n) = ProvisionalSumTypes.Count(new[]
            {
                ProvisionalSumType.Defined, ProvisionalSumType.Undeclared,
                ProvisionalSumType.Undefined, ProvisionalSumType.Undeclared,
            });
            Assert.Equal((1, 1, 2), (d, u, n));
        }

        [Fact]
        public void Preamble_states_both_definitions_and_the_preliminaries_consequence()
        {
            var clauses = ProvisionalSumTypes.PreambleClauses(0);
            string all = string.Join(" ", clauses);
            Assert.Contains("Defined Provisional Sums", all);
            Assert.Contains("Undefined Provisional Sums", all);
            Assert.Contains("deemed to have made due allowance in programming", all);
            Assert.Contains("shall not be deemed", all);
            Assert.DoesNotContain(ProvisionalSumTypes.NotDeclaredMarker, all);
        }

        [Fact]
        public void Preamble_warns_when_any_sum_is_undeclared()
        {
            var clauses = ProvisionalSumTypes.PreambleClauses(3);
            var warning = clauses.Last();
            Assert.Contains("3 Provisional Sum(s)", warning);
            Assert.Contains(ProvisionalSumTypes.NotDeclaredMarker, warning);
        }
    }
}
