// How many clashes get escalated to ACC Issues, and above what triage score.
//
// This is the seam where automation is most dangerous. Escalation creates ACC Issues
// assigned to real people, so an unattended run that escalates on a default nobody chose
// generates work for a team out of a config file's silence.
//
// THE ASSERTION THAT MATTERS: no policy -> an unattended run escalates ZERO, and it is
// asserted against a NON-EMPTY input (500 scored clashes). A test that fed it nothing would
// pass against any implementation, including one that escalates the top 10.
//
// The second is "both, never either": a count alone escalates trivia on a clean model, a
// score alone escalates hundreds on a bad one. A half-configuration is refused with its
// reason named, not half-applied.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccEscalationPolicyTests : IDisposable
    {
        private readonly string _dir;

        public AccEscalationPolicyTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sting-accescalate-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        /// <summary>A fresh settings file per call, so no test reads what another wrote.</summary>
        private AccOperatingPolicy Policy(string json)
        {
            string path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json");
            if (json != null) File.WriteAllText(path, json);
            return AccOperatingPolicy.Load(path);
        }

        /// <summary>Scores descend from 0.99 in steps of 0.001, so a threshold selects a
        /// known prefix and the counts below are arithmetic, not guesses.</summary>
        private static List<ScoredClash> Scored(int n) =>
            Enumerable.Range(0, n)
                      .Select(i => new ScoredClash
                      {
                          ClashId = $"c{i:D4}",
                          Score = Math.Round(0.99 - i * 0.001, 4),
                          Category = "structural-vs-services",
                      })
                      .ToList();

        private static string Sig(ScoredClash s) => "sig:" + s.ClashId;
        private static ISet<string> Tracked(params int[] indices) =>
            new HashSet<string>(indices.Select(i => $"sig:c{i:D4}"), StringComparer.Ordinal);
        private static ISet<string> NothingTracked() => new HashSet<string>(StringComparer.Ordinal);

        // ── No policy: an unattended run escalates nothing ──────────────────

        [Fact]
        public void NoPolicy_Unattended_EscalatesZero_AgainstFiveHundredClashes()
        {
            var scored = Scored(500);
            Assert.Equal(500, scored.Count);        // the input is non-empty; the result is not

            var plan = Policy("{\"unattended\":true}").PlanEscalation(scored, Sig, NothingTracked());

            Assert.Empty(plan.ToPush);
            Assert.False(plan.OfferInteractively);
            Assert.Contains("escalated nothing", plan.Reason, StringComparison.Ordinal);
            Assert.Contains("no escalation policy is configured", plan.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public void NoPolicy_Interactive_StillOffersTodaysTopTen()
        {
            // Unchanged behaviour for a person clicking the button: the click IS the consent
            // the policy would otherwise carry. Removing this would be a silent regression
            // dressed up as safety.
            var plan = Policy(null).PlanEscalation(Scored(500), Sig, NothingTracked());

            Assert.Equal(AccOperatingPolicy.InteractiveFallbackCount, plan.ToPush.Count);
            Assert.True(plan.OfferInteractively);
            Assert.Equal("c0000", plan.ToPush[0].ClashId);          // highest score first
            Assert.Contains("escalation is OFF", plan.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public void NoPolicy_Interactive_WithNothingScored_OffersNothing()
        {
            var plan = Policy(null).PlanEscalation(new List<ScoredClash>(), Sig, NothingTracked());
            Assert.Empty(plan.ToPush);
            Assert.False(plan.OfferInteractively);
        }

        // ── Half a policy is refused, with the reason named ─────────────────

        [Fact]
        public void CountWithoutScore_IsRefused_NotHalfApplied()
        {
            var p = Policy("{\"escalateMaxCount\":5}");

            Assert.False(p.Escalation.Enabled);
            Assert.Contains("escalateMinScore is not", p.Escalation.DisabledReason, StringComparison.Ordinal);
            Assert.Contains("trivia on a clean model", p.Escalation.DisabledReason, StringComparison.Ordinal);
            // ...and with the run also unattended, that refusal means zero escalations.
            var unattended = Policy("{\"unattended\":true,\"escalateMaxCount\":5}");
            Assert.Empty(unattended.PlanEscalation(Scored(500), Sig, NothingTracked()).ToPush);
        }

        [Fact]
        public void ScoreWithoutCount_IsRefused_NotHalfApplied()
        {
            var p = Policy("{\"escalateMinScore\":0.8}");

            Assert.False(p.Escalation.Enabled);
            Assert.Contains("escalateMaxCount is not", p.Escalation.DisabledReason, StringComparison.Ordinal);
            Assert.Contains("hundreds of clashes", p.Escalation.DisabledReason, StringComparison.Ordinal);
        }

        [Fact]
        public void HalfAPolicy_Unattended_StillEscalatesZero()
        {
            foreach (var half in new[] { "{\"unattended\":true,\"escalateMaxCount\":5}",
                                         "{\"unattended\":true,\"escalateMinScore\":0.8}" })
            {
                var plan = Policy(half).PlanEscalation(Scored(500), Sig, NothingTracked());
                Assert.Empty(plan.ToPush);
            }
        }

        [Fact]
        public void ACountOfZero_IsRefused_WithItsOwnReason()
        {
            var p = Policy("{\"escalateMaxCount\":0,\"escalateMinScore\":0.8}");
            Assert.False(p.Escalation.Enabled);
            Assert.Contains("nothing would be escalated", p.Escalation.DisabledReason, StringComparison.Ordinal);
        }

        // ── Both set: exactly the intersection ──────────────────────────────

        [Fact]
        public void BothSet_SelectsTheIntersectionOfScoreAndCount()
        {
            // 500 clashes scoring 0.990 down to 0.491. >= 0.95 is the first 41 (0.990..0.950).
            // maxCount 5 takes the top 5 of those.
            var plan = Policy("{\"unattended\":true,\"escalateMaxCount\":5,\"escalateMinScore\":0.95}")
                       .PlanEscalation(Scored(500), Sig, NothingTracked());

            Assert.Equal(5, plan.ToPush.Count);
            Assert.Equal(new[] { "c0000", "c0001", "c0002", "c0003", "c0004" },
                         plan.ToPush.Select(s => s.ClashId).ToArray());
            Assert.All(plan.ToPush, s => Assert.True(s.Score >= 0.95));
            Assert.Contains("41 of 500 clash(es) met the score", plan.Reason, StringComparison.Ordinal);
            Assert.Contains("5 to push", plan.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public void TheScoreThresholdCanSelectFewerThanTheCount()
        {
            // A clean model: only 3 clashes clear the bar, so 3 are escalated, not 10.
            var plan = Policy("{\"unattended\":true,\"escalateMaxCount\":10,\"escalateMinScore\":0.988}")
                       .PlanEscalation(Scored(500), Sig, NothingTracked());

            Assert.Equal(3, plan.ToPush.Count);      // 0.990, 0.989, 0.988
            Assert.All(plan.ToPush, s => Assert.True(s.Score >= 0.988));
        }

        [Fact]
        public void NothingClearsTheBar_EscalatesNothing()
        {
            var plan = Policy("{\"unattended\":true,\"escalateMaxCount\":10,\"escalateMinScore\":0.999}")
                       .PlanEscalation(Scored(500), Sig, NothingTracked());
            Assert.Empty(plan.ToPush);
            Assert.Contains("0 to push", plan.Reason, StringComparison.Ordinal);
        }

        // ── Already-tracked clashes ─────────────────────────────────────────

        [Fact]
        public void AlreadyTrackedClashes_AreExcluded_BeforeTheCountIsApplied()
        {
            // The top 3 were escalated last fortnight. A quota filled with rows the push
            // would skip anyway escalates nothing new; excluding first moves down the list.
            var plan = Policy("{\"unattended\":true,\"escalateMaxCount\":3,\"escalateMinScore\":0.95}")
                       .PlanEscalation(Scored(500), Sig, Tracked(0, 1, 2));

            Assert.Equal(3, plan.ToPush.Count);
            Assert.Equal(new[] { "c0003", "c0004", "c0005" }, plan.ToPush.Select(s => s.ClashId).ToArray());
            Assert.Equal(3, plan.AlreadyTracked);
            Assert.Contains("3 already tracked", plan.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public void EverythingQualifyingAlreadyTracked_EscalatesNothing_AndSaysWhy()
        {
            var scored = Scored(4);
            var plan = Policy("{\"unattended\":true,\"escalateMaxCount\":10,\"escalateMinScore\":0.0}")
                       .PlanEscalation(scored, Sig, Tracked(0, 1, 2, 3));

            Assert.Empty(plan.ToPush);
            Assert.Equal(4, plan.AlreadyTracked);
            // "nothing to do" is distinguishable from "nothing qualified".
            Assert.Contains("4 of 4 clash(es) met the score", plan.Reason, StringComparison.Ordinal);
            Assert.Contains("4 already tracked", plan.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public void RaisingTheCount_DoesNotRePushWhatIsAlreadyTracked()
        {
            // An IM widening the policy must not re-raise closed/duplicate issues.
            var scored = Scored(20);
            var before = Policy("{\"unattended\":true,\"escalateMaxCount\":3,\"escalateMinScore\":0.9}")
                         .PlanEscalation(scored, Sig, NothingTracked());
            var tracked = new HashSet<string>(before.ToPush.Select(Sig), StringComparer.Ordinal);

            var after = Policy("{\"unattended\":true,\"escalateMaxCount\":10,\"escalateMinScore\":0.9}")
                        .PlanEscalation(scored, Sig, tracked);

            Assert.DoesNotContain(after.ToPush, s => tracked.Contains(Sig(s)));
            Assert.Equal(3, after.AlreadyTracked);
        }

        // ── Interactive with a policy ───────────────────────────────────────

        [Fact]
        public void InteractiveWithAPolicy_OffersExactlyThePolicySet()
        {
            var plan = Policy("{\"escalateMaxCount\":4,\"escalateMinScore\":0.95}")
                       .PlanEscalation(Scored(500), Sig, NothingTracked());

            Assert.True(plan.OfferInteractively);
            Assert.Equal(4, plan.ToPush.Count);
            Assert.Contains("escalate at most 4 clash(es) scoring 0.95 or higher",
                            plan.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public void Describe_StatesTheRuleThatWillApply()
        {
            Assert.Equal("escalate at most 4 clash(es) scoring 0.95 or higher",
                         AccEscalationPolicy.On(4, 0.95).Describe());
            Assert.Contains("escalation is OFF", AccEscalationPolicy.Off("because").Describe(), StringComparison.Ordinal);
        }

        [Fact]
        public void NullInputs_DoNotThrow()
        {
            var p = Policy("{\"escalateMaxCount\":4,\"escalateMinScore\":0.5}");
            var plan = p.PlanEscalation(null, null, null);
            Assert.Empty(plan.ToPush);
        }
    }
}
