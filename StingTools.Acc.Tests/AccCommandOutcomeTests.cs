// The last mile: given an AccFetchResult, what does the command do and what does the
// operator read?
//
// That decision is the entire point of the outcome split — a workflow step must FAIL
// rather than pass — and until this file it lived inside two Revit-bound commands that no
// test project can link. It was verified by reading. This covers it.
//
// The table is driven by Enum.GetValues<AccFetchStatus>(), not by a hand-listed set, so a
// status member added later is covered without anyone remembering to add a case. A
// hand-listed table is a copy of the enum that drifts silently, which is the same shape as
// the defect being fixed.

using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccCommandOutcomeTests
    {
        /// <summary>Words that must never appear in a FAILURE message. Each one turns
        /// "our request was wrong" into "ACC lost the data" in a coordinator's head, and
        /// each has a real consequence: "clash-clean" passes a coordination gate,
        /// "not found" reads as issues deleted from the container.</summary>
        private static readonly string[] Forbidden = { "clash-clean", "no clashes", "not found" };

        private static AccFetchStatus[] AllStatuses()
        {
            var all = Enum.GetValues<AccFetchStatus>();
            // A vacuous pass would be the worst outcome here: an empty enumeration makes
            // every [Theory] below trivially true.
            Assert.True(all.Length >= 5,
                $"expected at least the five documented statuses, found {all.Length}. " +
                "The enumeration is broken, so nothing below proves anything.");
            return all;
        }

        [Fact]
        public void EveryStatus_HasAVerdict()
        {
            // Verdict is an exhaustive switch expression with no discard arm, so an
            // unhandled member throws here rather than silently taking a default meaning.
            foreach (var status in AllStatuses())
            {
                var verdict = AccCommandOutcome.Verdict(status);
                Assert.True(Enum.IsDefined(verdict), $"{status} produced an undefined verdict.");
            }
        }

        [Fact]
        public void EveryStatus_HasARemedy()
        {
            foreach (var status in AllStatuses())
                Assert.False(string.IsNullOrWhiteSpace(AccCommandOutcome.Remedy(status)),
                    $"{status} has no remedy text — an operator would be told what failed and not what to do.");
        }

        [Fact]
        public void OnlyOkAndEmptyOk_Succeed()
        {
            var succeeding = AllStatuses()
                .Where(s => AccCommandOutcome.Verdict(s) != AccCommandVerdict.Failed)
                .ToList();

            Assert.Equal(
                new[] { AccFetchStatus.Ok, AccFetchStatus.EmptyOk }.OrderBy(s => s).ToList(),
                succeeding.OrderBy(s => s).ToList());
        }

        [Fact]
        public void Ok_Proceeds_And_EmptyOk_SucceedsWithoutProceeding()
        {
            // The legitimate empty case must stay legitimate. An empty container, a
            // clash-clean model set and a station with nothing commissioned are real
            // states; turning them into errors would be a worse bug than the original.
            Assert.Equal(AccCommandVerdict.Proceed, AccCommandOutcome.Verdict(AccFetchStatus.Ok));
            Assert.Equal(AccCommandVerdict.SucceededEmpty, AccCommandOutcome.Verdict(AccFetchStatus.EmptyOk));
            Assert.False(AccCommandOutcome.IsFailure(AccFetchStatus.Ok));
            Assert.False(AccCommandOutcome.IsFailure(AccFetchStatus.EmptyOk));
        }

        [Fact]
        public void EveryFailingStatus_ProducesAMessageThatCannotReadAsAnEmptyResult()
        {
            var failing = AllStatuses().Where(AccCommandOutcome.IsFailure).ToList();
            Assert.NotEmpty(failing);

            foreach (var status in failing)
            {
                string msg = AccCommandOutcome.FailureMessage(
                    "the ACC issue list", status, 404, detail: null,
                    containerId: "b.11111111-2222-3333-4444-555555555555");

                Assert.False(string.IsNullOrWhiteSpace(msg));
                foreach (var word in Forbidden)
                    Assert.DoesNotContain(word, msg, StringComparison.OrdinalIgnoreCase);

                // It must say nothing was checked, name the failure kind, and name the
                // container — the three facts that stop a failed read being acted on.
                Assert.Contains("NOTHING WAS CHECKED", msg, StringComparison.Ordinal);
                Assert.Contains(status.ToString(), msg, StringComparison.Ordinal);
                Assert.Contains("b.11111111-2222-3333-4444-555555555555", msg, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void FailureMessage_PrefersTheSuppliedDetail_OverTheGenericDescription()
        {
            string msg = AccCommandOutcome.FailureMessage("the ACC issue list",
                AccFetchStatus.TransportFailed, 500,
                detail: "the issue list failed at page 2 (offset 100) after 1 page(s) succeeded",
                containerId: "b.container");

            Assert.Contains("page 2", msg, StringComparison.Ordinal);
            Assert.Contains("1 page(s) succeeded", msg, StringComparison.Ordinal);
        }

        [Fact]
        public void FailureMessage_RefusesASucceedingStatus()
        {
            // Calling the failure path for a success would print "NOTHING WAS CHECKED"
            // over a perfectly good read — the mirror image of the original defect.
            foreach (var ok in new[] { AccFetchStatus.Ok, AccFetchStatus.EmptyOk })
                Assert.Throws<ArgumentException>(() =>
                    AccCommandOutcome.FailureMessage("x", ok, 200, null, "b.container"));
        }

        [Fact]
        public void Describe_NeverUsesTheForbiddenWords_ForAnyFailingStatus()
        {
            // AccFetchOutcome.Describe feeds FailureMessage when no detail is supplied,
            // so it is bound by the same rule. "404 Not Found" is the HTTP reason phrase
            // and is exactly the phrasing that invites the wrong conclusion.
            foreach (var status in AllStatuses().Where(AccCommandOutcome.IsFailure))
            {
                string text = AccFetchOutcome.Describe(status, 404);
                foreach (var word in Forbidden)
                    Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
