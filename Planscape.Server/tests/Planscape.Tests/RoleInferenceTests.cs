using Planscape.Infrastructure.Workflow;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// Phase 148 — exercises the substring-keyword role inference that
/// kicks in for state names not in <c>CanonicalRoles</c>. These tests
/// pin the priority order documented on
/// <see cref="DeliverableStateMachine.InferRoleByKeyword"/> so a
/// regression in that order surfaces in CI rather than as silent
/// metadata-loss in production.
///
/// Priority: rejecting > accepting > submitting > terminal > working > initial.
/// </summary>
public class RoleInferenceTests
{
    // ── Rejecting (negative outcomes) ─────────────────────────────

    [Theory]
    [InlineData("REJECTED")]
    [InlineData("DECLINED_BY_CLIENT")]
    [InlineData("RETURNED_FOR_REWORK")]
    [InlineData("ARCH_REWORK_NEEDED")]
    [InlineData("FAILED_QA")]
    [InlineData("VOIDED")]
    public void InferRoleByKeyword_RecognisesRejecting(string state)
    {
        Assert.Equal("rejecting", DeliverableStateMachine.InferRoleByKeyword(state));
    }

    // ── Accepting (positive outcomes) ─────────────────────────────

    [Theory]
    [InlineData("ACCEPTED")]
    [InlineData("APPROVED_BY_PM")]
    [InlineData("PUBLISHED_TO_CDE")]
    [InlineData("ME_FINAL_APPROVAL")]
    [InlineData("CLIENT_SIGNED_OFF")]
    [InlineData("MANAGER_SIGNOFF")]
    [InlineData("PASSED_REVIEW")]
    public void InferRoleByKeyword_RecognisesAccepting(string state)
    {
        Assert.Equal("accepting", DeliverableStateMachine.InferRoleByKeyword(state));
    }

    // ── Submitting (in-review) ────────────────────────────────────

    [Theory]
    [InlineData("SUBMITTED")]
    [InlineData("AWAITING_REVIEW")]
    [InlineData("UNDER_REVIEW")]
    [InlineData("ISSUED_FOR_APPROVAL")]
    [InlineData("FOR_INFORMATION")]
    [InlineData("FOR_COMMENT")]
    public void InferRoleByKeyword_RecognisesSubmitting(string state)
    {
        Assert.Equal("submitting", DeliverableStateMachine.InferRoleByKeyword(state));
    }

    // ── Terminal (archival / closure) ─────────────────────────────

    [Theory]
    [InlineData("ARCHIVED")]
    [InlineData("CLOSED")]
    [InlineData("CANCELLED")]
    [InlineData("WAIVED")]
    [InlineData("SUPERSEDED")]
    [InlineData("COMPLETE")]
    [InlineData("FINAL_RELEASE")]
    [InlineData("DONE")]
    public void InferRoleByKeyword_RecognisesTerminal(string state)
    {
        Assert.Equal("terminal", DeliverableStateMachine.InferRoleByKeyword(state));
    }

    // ── Working (in-flight) ───────────────────────────────────────

    [Theory]
    [InlineData("IN_PROGRESS")]
    [InlineData("WIP")]
    [InlineData("DRAFTING")]
    [InlineData("ACTIVELY_AUTHORING")]
    [InlineData("BUILD_PHASE")]
    [InlineData("ONGOING")]
    public void InferRoleByKeyword_RecognisesWorking(string state)
    {
        Assert.Equal("working", DeliverableStateMachine.InferRoleByKeyword(state));
    }

    // ── Initial (queued) ──────────────────────────────────────────

    [Theory]
    [InlineData("PENDING")]
    [InlineData("BACKLOG")]
    [InlineData("TODO")]
    [InlineData("NEW")]
    [InlineData("QUEUED")]
    [InlineData("OPEN")]
    [InlineData("INITIAL_BRIEF")]
    public void InferRoleByKeyword_RecognisesInitial(string state)
    {
        Assert.Equal("initial", DeliverableStateMachine.InferRoleByKeyword(state));
    }

    // ── Priority — outcomes win over in-flight ───────────────────

    [Fact]
    public void InferRoleByKeyword_RejectingBeatsSubmitting()
    {
        // FINAL_REVIEW_REJECTED — REJECT (rejecting) AND REVIEW (submitting).
        // Rejecting must win because it's the more specific outcome.
        Assert.Equal("rejecting",
            DeliverableStateMachine.InferRoleByKeyword("FINAL_REVIEW_REJECTED"));
    }

    [Fact]
    public void InferRoleByKeyword_AcceptingBeatsSubmitting()
    {
        // POST_REVIEW_APPROVED — APPROV (accepting) AND REVIEW (submitting).
        Assert.Equal("accepting",
            DeliverableStateMachine.InferRoleByKeyword("POST_REVIEW_APPROVED"));
    }

    [Fact]
    public void InferRoleByKeyword_AcceptingBeatsTerminal()
    {
        // FINAL_APPROVED — APPROV (accepting) AND FINAL (terminal).
        Assert.Equal("accepting",
            DeliverableStateMachine.InferRoleByKeyword("FINAL_APPROVED"));
    }

    [Fact]
    public void InferRoleByKeyword_SubmittingBeatsWorking()
    {
        // ACTIVE_REVIEW — ACTIVE (working) AND REVIEW (submitting).
        Assert.Equal("submitting",
            DeliverableStateMachine.InferRoleByKeyword("ACTIVE_REVIEW"));
    }

    [Fact]
    public void InferRoleByKeyword_NoMatchReturnsNull()
    {
        Assert.Null(DeliverableStateMachine.InferRoleByKeyword("ALPHA_BRAVO_CHARLIE"));
        Assert.Null(DeliverableStateMachine.InferRoleByKeyword("FOOBAR"));
    }

    [Fact]
    public void InferRoleByKeyword_EmptyOrNullReturnsNull()
    {
        Assert.Null(DeliverableStateMachine.InferRoleByKeyword(""));
        Assert.Null(DeliverableStateMachine.InferRoleByKeyword("   "));
        Assert.Null(DeliverableStateMachine.InferRoleByKeyword(null!));
    }

    [Fact]
    public void InferRoleByKeyword_CaseInsensitive()
    {
        Assert.Equal("submitting", DeliverableStateMachine.InferRoleByKeyword("under_review"));
        Assert.Equal("accepting", DeliverableStateMachine.InferRoleByKeyword("approved"));
    }

    // ── Integration with LoadOrDefault ───────────────────────────

    [Fact]
    public void LoadOrDefault_BespokeNamesNowGetInferredRoles()
    {
        // Phase 147 rejected this case (test was BespokeNamesStayNone).
        // Phase 148 substring inference should now resolve sensible roles.
        const string json = """
        {
          "states": ["INTAKE", "DRAFTING", "AWAITING_REVIEW", "PUBLISHED_TO_CDE"],
          "transitions": [
            { "from": "INTAKE", "to": "DRAFTING" },
            { "from": "DRAFTING", "to": "AWAITING_REVIEW" },
            { "from": "AWAITING_REVIEW", "to": "PUBLISHED_TO_CDE" }
          ]
        }
        """;
        var m = DeliverableStateMachine.LoadOrDefault(json);
        Assert.True(m.IsCustom);
        Assert.Equal("working", m.RoleOf("DRAFTING"));
        Assert.Equal("submitting", m.RoleOf("AWAITING_REVIEW"));
        Assert.Equal("accepting", m.RoleOf("PUBLISHED_TO_CDE"));
        // INTAKE has no keyword match — stays "none" rather than guessing.
        Assert.Equal("none", m.RoleOf("INTAKE"));
    }

    [Fact]
    public void LoadOrDefault_ExplicitRolesStillWinOverFuzzyInference()
    {
        // SUBMITTED would infer to "submitting"; explicit override to
        // "working" must still win.
        const string json = """
        {
          "transitions": [{ "from": "SUBMITTED", "to": "REJECTED_BY_QA" }],
          "roles": { "SUBMITTED": "working" }
        }
        """;
        var m = DeliverableStateMachine.LoadOrDefault(json);
        Assert.Equal("working", m.RoleOf("SUBMITTED"));
        // REJECTED_BY_QA isn't in the explicit roles block — and because
        // the JSON DID provide a roles block, the inference path is
        // disabled entirely. Documented behaviour from Phase 146.
        Assert.Equal("none", m.RoleOf("REJECTED_BY_QA"));
    }
}
