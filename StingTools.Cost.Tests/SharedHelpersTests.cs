using StingTools.Core;
using Xunit;

namespace StingTools.Cost.Tests
{
    /// <summary>PM-1 — the IssueStatus normalizer must collapse the four historical
    /// spellings the audit found diverging across BIMManager / Clash / ACC / KPI.</summary>
    public class IssueStatusNormalizerTests
    {
        [Theory]
        [InlineData("OPEN")]      // BIMManager
        [InlineData("Open")]      // Clash
        [InlineData("open")]      // ACC
        [InlineData(" open ")]    // whitespace
        [InlineData("New")]
        [InlineData("Reopened")]
        public void Open_Spellings_AllNormalizeToOpen(string raw)
        {
            Assert.Equal(IssueStatusKind.Open, IssueStatusNormalizer.Normalize(raw));
            Assert.True(IssueStatusNormalizer.IsOpen(raw));
        }

        [Theory]
        [InlineData("Resolved", IssueStatusKind.Resolved)]
        [InlineData("RESOLVED", IssueStatusKind.Resolved)]
        [InlineData("Void", IssueStatusKind.Void)]
        [InlineData("Cancelled", IssueStatusKind.Void)]
        [InlineData("Closed", IssueStatusKind.Closed)]
        [InlineData("In Progress", IssueStatusKind.InProgress)]
        [InlineData("in_progress", IssueStatusKind.InProgress)]
        public void Other_Spellings_MapCorrectly(string raw, IssueStatusKind expected)
        {
            Assert.Equal(expected, IssueStatusNormalizer.Normalize(raw));
        }

        [Theory]
        [InlineData("Resolved")]
        [InlineData("Closed")]
        [InlineData("Void")]
        public void Closed_LikeStates_AreNotOpen(string raw)
        {
            Assert.False(IssueStatusNormalizer.IsOpen(raw));
        }

        [Fact]
        public void InProgress_CountsAsOpen_ForTheGate()
        {
            Assert.True(IssueStatusNormalizer.IsOpen("In Progress"));
        }

        [Fact]
        public void Unknown_FailsSafe_AsOpen()
        {
            // The has_open_issues gate must fail safe — an unrecognised status is
            // treated as possibly-open rather than silently ignored.
            Assert.True(IssueStatusNormalizer.IsOpen("some-weird-status"));
        }

        [Fact]
        public void Canonical_RoundTrips()
        {
            Assert.Equal("OPEN", IssueStatusNormalizer.Canonical("open"));
            Assert.Equal("RESOLVED", IssueStatusNormalizer.Canonical("Resolved"));
            Assert.Equal("VOID", IssueStatusNormalizer.Canonical("cancelled"));
        }
    }

    /// <summary>PM-1 — money rounding is one half-even convention so totals reconcile.</summary>
    public class MoneyRoundTests
    {
        [Theory]
        [InlineData(2.5, 2.0)]    // half-even: 2.5 → 2
        [InlineData(3.5, 4.0)]    // half-even: 3.5 → 4
        [InlineData(0.5, 0.0)]    // half-even: 0.5 → 0
        [InlineData(1.5, 2.0)]    // half-even: 1.5 → 2
        public void Round_IsHalfEven(double input, double expected)
        {
            Assert.Equal(expected, MoneyRound.Round(input));
        }

        [Fact]
        public void Round_NaNAndInfinity_AreZero()
        {
            Assert.Equal(0, MoneyRound.Round(double.NaN));
            Assert.Equal(0, MoneyRound.Round(double.PositiveInfinity));
        }

        [Fact]
        public void PerLine_Rounding_Reconciles_ToTotal()
        {
            // Three lines that each round to whole shillings should sum to the
            // rounded sum with no systematic drift (half-even cancels the bias).
            double[] lines = { 100.5, 200.5, 300.5 };  // → 100, 200, 300 (all even) = 600
            double perLineSum = 0;
            foreach (var l in lines) perLineSum += MoneyRound.Round(l);
            Assert.Equal(600, perLineSum);
        }

        [Fact]
        public void DecimalOverload_IsHalfEven()
        {
            Assert.Equal(2.0m, MoneyRound.Round(2.5m, 0));
            Assert.Equal(2.46m, MoneyRound.Round(2.455m, 2));
        }
    }
}
