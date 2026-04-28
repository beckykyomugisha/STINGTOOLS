using Planscape.Infrastructure.Workflow;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// Phase 147 — exercises the deliverable state machine: canonical default,
/// custom JSON parsing, inferred-role heuristic, and the RoleOf side-effect
/// driver used by DeliverablesController.Transition.
///
/// These are pure-Compute tests — no DbContext, no HTTP — so they run in
/// milliseconds and can be wired into CI as a guard against the four
/// failure modes the controller relies on:
///   1. Default machine validates canonical transitions
///   2. Default machine reports the canonical roles
///   3. Forgiving loader: malformed / empty JSON falls back to Default
///   4. Inferred roles when "roles" block is missing (Phase 147 fix)
/// </summary>
public class DeliverableStateMachineTests
{
    // ── Default machine ────────────────────────────────────────────

    [Fact]
    public void Default_AcceptsCanonicalForwardTransitions()
    {
        var m = DeliverableStateMachine.Default;
        Assert.True(m.IsValidTransition("PENDING", "IN_PROGRESS"));
        Assert.True(m.IsValidTransition("IN_PROGRESS", "SUBMITTED"));
        Assert.True(m.IsValidTransition("SUBMITTED", "ACCEPTED"));
    }

    [Fact]
    public void Default_RejectsBackwardJump()
    {
        var m = DeliverableStateMachine.Default;
        Assert.False(m.IsValidTransition("PENDING", "ACCEPTED"));
        Assert.False(m.IsValidTransition("ACCEPTED", "REJECTED"));
    }

    [Fact]
    public void Default_RoleOfCanonicalNamesIsSeeded()
    {
        var m = DeliverableStateMachine.Default;
        Assert.Equal("submitting", m.RoleOf("SUBMITTED"));
        Assert.Equal("accepting", m.RoleOf("ACCEPTED"));
        Assert.Equal("rejecting", m.RoleOf("REJECTED"));
        Assert.Equal("initial", m.RoleOf("PENDING"));
        Assert.Equal("terminal", m.RoleOf("WAIVED"));
    }

    [Fact]
    public void Default_RoleOfUnknownStateIsNone()
    {
        var m = DeliverableStateMachine.Default;
        Assert.Equal("none", m.RoleOf("MARS"));
        Assert.Equal("none", m.RoleOf(""));
    }

    [Fact]
    public void Default_IsCustomFalse()
    {
        Assert.False(DeliverableStateMachine.Default.IsCustom);
    }

    [Fact]
    public void Default_TerminalChecks()
    {
        var m = DeliverableStateMachine.Default;
        Assert.True(m.IsTerminal("ACCEPTED"));
        Assert.True(m.IsTerminal("WAIVED"));
        Assert.False(m.IsTerminal("PENDING"));
    }

    // ── Forgiving loader ──────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]                         // wrong root type
    [InlineData("\"a string\"")]               // wrong root type
    [InlineData("{ \"transitions\": [] }")]    // empty arcs → unusable
    [InlineData("{ \"states\": [\"A\"] }")]    // no arcs → unusable
    public void LoadOrDefault_FallsBack(string? json)
    {
        var m = DeliverableStateMachine.LoadOrDefault(json);
        Assert.False(m.IsCustom);
        Assert.Equal("ISO_19650_v1", m.Name);
    }

    [Fact]
    public void LoadOrDefault_AcceptsExplicitRoles()
    {
        const string json = """
        {
          "name": "review-flow",
          "states": ["DRAFT", "UNDER_REVIEW", "DONE"],
          "initial": "DRAFT",
          "transitions": [
            { "from": "DRAFT", "to": "UNDER_REVIEW" },
            { "from": "UNDER_REVIEW", "to": "DONE" }
          ],
          "terminal": ["DONE"],
          "roles": {
            "DRAFT": "working",
            "UNDER_REVIEW": "submitting",
            "DONE": "accepting"
          }
        }
        """;
        var m = DeliverableStateMachine.LoadOrDefault(json);
        Assert.True(m.IsCustom);
        Assert.Equal("review-flow", m.Name);
        Assert.Equal("submitting", m.RoleOf("UNDER_REVIEW"));
        Assert.Equal("accepting", m.RoleOf("DONE"));
    }

    [Fact]
    public void LoadOrDefault_DropsUnknownRoleValues()
    {
        const string json = """
        {
          "transitions": [{ "from": "A", "to": "B" }],
          "roles": { "A": "BANANAS", "B": "submitting" }
        }
        """;
        var m = DeliverableStateMachine.LoadOrDefault(json);
        Assert.True(m.IsCustom);
        Assert.Equal("none", m.RoleOf("A"));        // unknown role dropped
        Assert.Equal("submitting", m.RoleOf("B")); // valid role kept
    }

    // ── Phase 147: inferred roles when "roles" block is missing ──

    [Fact]
    public void LoadOrDefault_NoRolesBlock_InfersFromCanonicalNames()
    {
        // No "roles" key — but the state names match the canonical
        // dictionary so RoleOf should still return the right answer.
        const string json = """
        {
          "states": ["PENDING", "IN_PROGRESS", "SUBMITTED", "ACCEPTED"],
          "transitions": [
            { "from": "PENDING", "to": "IN_PROGRESS" },
            { "from": "IN_PROGRESS", "to": "SUBMITTED" },
            { "from": "SUBMITTED", "to": "ACCEPTED" }
          ]
        }
        """;
        var m = DeliverableStateMachine.LoadOrDefault(json);
        Assert.True(m.IsCustom);
        Assert.Equal("submitting", m.RoleOf("SUBMITTED"));
        Assert.Equal("accepting", m.RoleOf("ACCEPTED"));
        Assert.Equal("working", m.RoleOf("IN_PROGRESS"));
    }

    [Fact]
    public void LoadOrDefault_NoRolesBlock_RecognisesAliases()
    {
        // Synonyms in CanonicalRoles (e.g. APPROVED for accepting,
        // DRAFT for working) should also be inferred.
        const string json = """
        {
          "transitions": [
            { "from": "DRAFT", "to": "APPROVED" }
          ]
        }
        """;
        var m = DeliverableStateMachine.LoadOrDefault(json);
        Assert.Equal("working", m.RoleOf("DRAFT"));
        Assert.Equal("accepting", m.RoleOf("APPROVED"));
    }

    [Fact]
    public void LoadOrDefault_NoRolesBlock_BespokeNamesStayNone()
    {
        // Totally bespoke state names with no "roles" block → no
        // inference possible; controller skips side-effects which is
        // the documented behaviour.
        const string json = """
        {
          "transitions": [
            { "from": "ALPHA", "to": "BRAVO" },
            { "from": "BRAVO", "to": "CHARLIE" }
          ]
        }
        """;
        var m = DeliverableStateMachine.LoadOrDefault(json);
        Assert.True(m.IsCustom);
        Assert.Equal("none", m.RoleOf("ALPHA"));
        Assert.Equal("none", m.RoleOf("BRAVO"));
        Assert.Equal("none", m.RoleOf("CHARLIE"));
    }

    [Fact]
    public void LoadOrDefault_ExplicitRolesWinOverInference()
    {
        // SUBMITTED would infer to "submitting", but the JSON marks it
        // as "working" — explicit wins.
        const string json = """
        {
          "transitions": [{ "from": "SUBMITTED", "to": "ACCEPTED" }],
          "roles": { "SUBMITTED": "working" }
        }
        """;
        var m = DeliverableStateMachine.LoadOrDefault(json);
        Assert.Equal("working", m.RoleOf("SUBMITTED"));
        Assert.Equal("none", m.RoleOf("ACCEPTED")); // not in roles, not inferred (block was provided)
    }

    // ── Convenience helpers ──────────────────────────────────────

    [Fact]
    public void NextStates_ReturnsAllForwardTargets()
    {
        var m = DeliverableStateMachine.Default;
        var next = m.NextStates("IN_PROGRESS");
        Assert.Contains("SUBMITTED", next);
        Assert.Contains("WAIVED", next);
        Assert.Contains("PENDING", next);
    }

    [Fact]
    public void NextStates_EmptyForUnknownState()
    {
        var m = DeliverableStateMachine.Default;
        Assert.Empty(m.NextStates("NEPTUNE"));
    }
}
