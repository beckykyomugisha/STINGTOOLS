using Planscape.Shared.Commissioning;

namespace Planscape.Tests;

/// <summary>
/// The commissioning ladder — the rules the Revit plugin and this server now BOTH
/// call, from one copy in Planscape.Shared.
///
/// They were written once inside the plugin, where the phone could not reach them.
/// Building the mobile path meant either sharing them or writing them twice, and
/// twice drifts invisibly: a desktop that refuses a skip-state transition and an
/// API that allows it produce a handover record nobody can reconcile, months later,
/// with no error anywhere.
///
/// So the rules are pinned here, and the interesting cases are the REFUSALS — each
/// one is a claim about the real world that the record would otherwise assert
/// falsely.
/// </summary>
public class CommissioningStateMachineTests
{
    private static CommissioningRequest By(string operative, string? witness = null, string? target = null)
        => new() { Operative = operative, Witness = witness, RequestedState = target };

    // ── The happy ladder ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "RECEIVED")]
    [InlineData("", "RECEIVED")]
    [InlineData("NOT_STARTED", "RECEIVED")]
    [InlineData("RECEIVED", "INSTALLED")]
    [InlineData("INSTALLED", "TESTED")]
    public void Each_step_advances_to_the_next(string? from, string expected)
    {
        var d = CommissioningStateMachine.Decide(from, By("A. Fitter"));

        Assert.True(d.Ok, d.Reason);
        Assert.Equal(expected, d.ToState);
    }

    [Fact]
    public void An_empty_state_is_not_started_not_unknown()
    {
        // An element that has never been touched has no parameter value and no row.
        // That is NOT_STARTED — a real answer, not an error.
        Assert.Equal(0, CommissioningStates.RankOf(null));
        Assert.Equal(0, CommissioningStates.RankOf(""));
        Assert.True(CommissioningStateMachine.Decide(null, By("A. Fitter")).Ok);
    }

    // ── The refusals ──────────────────────────────────────────────────────────

    [Fact]
    public void It_refuses_to_go_backwards()
    {
        // Commissioning records what HAPPENED. Rewinding it destroys the only
        // evidence that it did.
        var d = CommissioningStateMachine.Decide("TESTED", By("A. Fitter", target: "RECEIVED"));

        Assert.False(d.Ok);
        Assert.Equal(CommissioningRefusal.Regression, d.Refusal);
    }

    [Fact]
    public void It_refuses_to_repeat_a_state()
    {
        // "Advance to the state it is already in" is a no-op, and a caller must not
        // be able to report it as progress.
        var d = CommissioningStateMachine.Decide("INSTALLED", By("A. Fitter", target: "INSTALLED"));

        Assert.False(d.Ok);
        Assert.Equal(CommissioningRefusal.Regression, d.Refusal);
    }

    [Fact]
    public void It_refuses_to_skip_a_state()
    {
        // Each state is evidence the previous one occurred. TESTED without INSTALLED
        // is a claim about an asset nobody has fitted.
        var d = CommissioningStateMachine.Decide("RECEIVED", By("A. Fitter", target: "TESTED"));

        Assert.False(d.Ok);
        Assert.Equal(CommissioningRefusal.SkippedState, d.Refusal);
        Assert.Contains("INSTALLED", d.Reason);   // it names the step actually next
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void It_refuses_an_unattributed_step(string? operative)
    {
        // An unattributed sign-off is not a sign-off. Whitespace is not a name.
        var d = CommissioningStateMachine.Decide("RECEIVED", new CommissioningRequest { Operative = operative });

        Assert.False(d.Ok);
        Assert.Equal(CommissioningRefusal.MissingOperative, d.Refusal);
    }

    [Fact]
    public void Commissioned_requires_a_witness()
    {
        // This is the point the asset is declared fit for use — the one a second
        // person must have seen.
        var d = CommissioningStateMachine.Decide("TESTED", By("A. Fitter"));

        Assert.False(d.Ok);
        Assert.Equal(CommissioningRefusal.MissingWitness, d.Refusal);

        var withWitness = CommissioningStateMachine.Decide("TESTED", By("A. Fitter", witness: "B. Supervisor"));
        Assert.True(withWitness.Ok, withWitness.Reason);
        Assert.Equal("COMMISSIONED", withWitness.ToState);
    }

    [Fact]
    public void Handover_does_not_require_a_witness()
    {
        // Only COMMISSIONED does. Requiring one everywhere would be a different rule
        // to the desktop's, which is exactly the drift this shared copy prevents.
        var d = CommissioningStateMachine.Decide("COMMISSIONED", By("A. Fitter"));

        Assert.True(d.Ok, d.Reason);
        Assert.Equal("HANDOVER", d.ToState);
    }

    [Fact]
    public void Past_the_end_of_the_ladder_there_is_nowhere_to_go()
    {
        var d = CommissioningStateMachine.Decide("HANDOVER", By("A. Fitter"));

        Assert.False(d.Ok);
        Assert.Equal(CommissioningRefusal.Regression, d.Refusal);
        Assert.True(CommissioningStates.IsTerminal("HANDOVER"));
    }

    // ── Unknown states ────────────────────────────────────────────────────────

    [Fact]
    public void An_unrecognised_STORED_state_is_refused_not_treated_as_not_started()
    {
        // The behaviour this REPLACES: the old IndexOfState returned 0 for anything
        // it did not recognise, so a typo — or a state written by a newer build —
        // read as NOT_STARTED and could be silently advanced over, erasing it.
        var d = CommissioningStateMachine.Decide("ALMOST_DONE", By("A. Fitter"));

        Assert.False(d.Ok);
        Assert.Equal(CommissioningRefusal.UnknownState, d.Refusal);
    }

    [Fact]
    public void An_unrecognised_REQUESTED_state_is_refused()
    {
        var d = CommissioningStateMachine.Decide("RECEIVED", By("A. Fitter", target: "DONE"));

        Assert.False(d.Ok);
        Assert.Equal(CommissioningRefusal.UnknownState, d.Refusal);
    }

    [Fact]
    public void RankOf_separates_unknown_from_not_started()
    {
        Assert.Equal(-1, CommissioningStates.RankOf("NONSENSE"));
        Assert.Equal(0, CommissioningStates.RankOf(null));
        Assert.False(CommissioningStates.IsKnown("NONSENSE"));
        Assert.True(CommissioningStates.IsKnown("HANDOVER"));
    }

    [Fact]
    public void Next_from_an_unknown_state_is_null_not_a_guess()
    {
        Assert.Null(CommissioningStates.Next("NONSENSE"));
        Assert.Equal("RECEIVED", CommissioningStates.Next(null));
    }

    // ── Case and whitespace ───────────────────────────────────────────────────

    [Theory]
    [InlineData("received")]
    [InlineData("Received")]
    [InlineData("  RECEIVED  ")]
    public void A_requested_state_is_matched_case_and_whitespace_insensitively(string target)
    {
        // A phone form and a Revit parameter will not agree on casing, and that must
        // not be the difference between a recorded step and a refusal.
        var d = CommissioningStateMachine.Decide("NOT_STARTED", By("A. Fitter", target: target));
        Assert.True(d.Ok, d.Reason);
        Assert.Equal("RECEIVED", d.ToState);
    }

    // ── The ladder itself ─────────────────────────────────────────────────────

    [Fact]
    public void The_ladder_is_the_one_both_sides_expect()
    {
        // Pinned by VALUE. The desktop reads a stored state back by name, so a
        // reorder or rename silently changes what "advance" means for every record
        // ever written — including ones on drawings already issued.
        Assert.Equal(
            new[] { "NOT_STARTED", "RECEIVED", "INSTALLED", "TESTED", "COMMISSIONED", "HANDOVER" },
            CommissioningStates.All);
    }

    [Fact]
    public void Walking_the_whole_ladder_one_step_at_a_time_succeeds()
    {
        // End to end, the way a real asset goes through it — so a rule added later
        // that happens to block a legitimate step is caught here.
        string state = CommissioningStates.NotStarted;
        for (int i = 0; i < CommissioningStates.All.Length - 1; i++)
        {
            var d = CommissioningStateMachine.Decide(state, By("A. Fitter", witness: "B. Supervisor"));
            Assert.True(d.Ok, $"step {i} from {state}: {d.Reason}");
            state = d.ToState!;
        }
        Assert.Equal(CommissioningStates.Handover, state);
    }
}
