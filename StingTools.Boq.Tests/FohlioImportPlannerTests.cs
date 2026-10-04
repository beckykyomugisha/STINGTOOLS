using System.Collections.Generic;
using System.Linq;
using StingTools.ExLink;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// KUT deep review FOH-1/2/5/6/10/13: the Revit-free matching and money rules of the
    /// Fohlio round trip. Each test names the behaviour it replaces.
    /// </summary>
    public class FohlioImportPlannerTests
    {
        private static FohlioCandidate C(long id, string key, string uid = "", string fref = "")
            => new FohlioCandidate { Id = id, Key = key, UniqueId = uid, FohlioRef = fref };

        private static FohlioRowIdentity R(int i, string key, string uid = "", string fref = "")
            => new FohlioRowIdentity { RowIndex = i, Key = key, UniqueId = uid, FohlioRef = fref };

        // ── rooms: one site-wide register, one model per building ─────────────

        [Fact]
        public void ARowFromAnotherBuildingModelNeverLandsOnARoomWithTheSameNumber()
        {
            // Meetinghouse model has room 101. The register also carries the Temple's 101,
            // identified by the Temple model's UniqueId. Old behaviour: matched by number.
            var rooms = new[] { C(1, "101", uid: "mh-101") };
            var rows = new[] { R(0, "101", uid: "temple-101"), R(1, "101", uid: "mh-101") };

            var plan = FohlioImportPlanner.Match(rows, rooms, useFohlioRef: false);

            Assert.Single(plan.Matches);
            Assert.Equal(1, plan.Matches[0].RowIndex);
            Assert.Equal("unique id", plan.Matches[0].By);
            Assert.Contains("another model", plan.Unmatched.Single(u => u.RowIndex == 0).Reason);
        }

        [Fact]
        public void ARoomNumberThatAppearsTwiceInTheFileIsRefusedNotGuessed()
        {
            // A register without ids (rows typed in Fohlio): two rows say 101.
            var rooms = new[] { C(1, "101") };
            var rows = new[] { R(0, "101"), R(1, "101") };

            var plan = FohlioImportPlanner.Match(rows, rooms, useFohlioRef: false);

            Assert.Empty(plan.Matches);
            Assert.All(plan.Unmatched, u => Assert.Contains("rows in the file share", u.Reason));
        }

        [Fact]
        public void ARoomNumberSharedByTwoRoomsInTheModelIsRefused()
        {
            var rooms = new[] { C(1, "101"), C(2, "101") };
            var plan = FohlioImportPlanner.Match(new[] { R(0, "101") }, rooms, useFohlioRef: false);
            Assert.Empty(plan.Matches);
            Assert.Contains("2 elements in the model", plan.Unmatched.Single().Reason);
        }

        [Fact]
        public void KeysAreTrimmedOnBothSides()
        {
            var plan = FohlioImportPlanner.Match(new[] { R(0, " 101") }, new[] { C(1, "101 ") }, false);
            Assert.Single(plan.Matches);
        }

        // ── FF&E: the Fohlio ref is the link key ──────────────────────────────

        [Fact]
        public void TheFohlioRefWinsOverATagThatMovedToAnotherElement()
        {
            // Element 1 is linked to F-9 and was re-tagged; element 2 now carries its old tag.
            var els = new[] { C(1, "A-BLD1-NEW-0002", fref: "F-9"), C(2, "A-BLD1-OLD-0001") };
            var rows = new[] { R(0, "A-BLD1-OLD-0001", fref: "F-9") };

            var plan = FohlioImportPlanner.Match(rows, els, useFohlioRef: true);

            Assert.Equal(1, plan.Matches.Single().CandidateId);
            Assert.Equal("Fohlio ref", plan.Matches.Single().By);
        }

        [Fact]
        public void AnImportNeverRepointsAnExistingLink()
        {
            var els = new[] { C(1, "TAG-1", fref: "F-1") };
            var plan = FohlioImportPlanner.Match(new[] { R(0, "TAG-1", fref: "F-2") }, els, useFohlioRef: true);
            Assert.Empty(plan.Matches);
            Assert.Contains("already linked", plan.Unmatched.Single().Reason);
        }

        [Fact]
        public void ADuplicateTagInTheModelIsReportedNotSentToTheFirstElement()
        {
            var els = new[] { C(1, "TAG-1"), C(2, "TAG-1") };
            var plan = FohlioImportPlanner.Match(new[] { R(0, "TAG-1") }, els, useFohlioRef: true);
            Assert.Empty(plan.Matches);
        }

        [Fact]
        public void TwoRowsCannotClaimOneElement()
        {
            var els = new[] { C(1, "TAG-1", fref: "F-1") };
            var rows = new[] { R(0, "TAG-1", fref: "F-1"), R(1, "", fref: "F-1") };
            var plan = FohlioImportPlanner.Match(rows, els, useFohlioRef: true);
            Assert.Single(plan.Matches);
            Assert.Contains("already claimed", plan.Unmatched.Single().Reason);
        }

        // ── money ─────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("1250", 1250, null)]
        [InlineData("1,250.00", 1250, null)]
        [InlineData("$1,250.00", 1250, null)]
        [InlineData("UGX 4,500,000", 4500000, "UGX")]
        [InlineData("4 500 000 UGX", 4500000, "UGX")]
        [InlineData("1.250,00", 1250, null)]
        [InlineData("12,5", 12.5, null)]
        [InlineData("USD 99.95", 99.95, "USD")]
        [InlineData("US$ 10", 10, "USD")]
        [InlineData("€ 1.250,50", 1250.5, "EUR")]
        public void PriceCellsAsFohlioAndExcelWriteThem(string raw, double expected, string expectedCur)
        {
            // Old behaviour: invariant TryParse, anything but "1250" became 0 and was dropped.
            Assert.True(FohlioMoney.TryParseCost(raw, out double v, out string cur), raw);
            Assert.Equal(expected, v, 6);
            Assert.Equal(expectedCur, cur);
        }

        [Theory]
        [InlineData("")]
        [InlineData("TBC")]
        [InlineData("1,25,0")]
        [InlineData("XYZW 10")]
        [InlineData("-5")]
        public void UnreadablePriceCellsAreRefusedNotZeroed(string raw)
        {
            Assert.False(FohlioMoney.TryParseCost(raw, out _, out _), raw);
        }

        [Theory]
        [InlineData("ugx", "UGX")]
        [InlineData("USh", "UGX")]
        [InlineData("US$", "USD")]
        [InlineData("€", "EUR")]
        [InlineData("$", null)]      // ambiguous: USD, AUD, CAD…
        [InlineData("", null)]
        [InlineData("dollars", null)]
        public void CurrencyIsNeverGuessed(string raw, string expected)
        {
            Assert.Equal(expected, FohlioMoney.NormalizeCurrency(raw));
        }

        // ── empty scope ───────────────────────────────────────────────────────

        [Fact]
        public void NoFfeInScopeIsNotOneHundredPercentLinked()
        {
            // Old behaviour: total == 0 -> 100.0 (green) in Fohlio_Audit and the KPI dashboard.
            Assert.Null(FohlioImportPlanner.LinkedPct(0, 0));
            Assert.Equal(50.0, FohlioImportPlanner.LinkedPct(4, 2));
        }
    }
}
