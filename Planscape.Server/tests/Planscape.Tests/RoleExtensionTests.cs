using Planscape.Infrastructure.Workflow;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// Phase 149 — exercises the expanded keyword vocabulary, the
/// tenant-supplied "keywords" extension block, and the memoised
/// <see cref="DeliverableStateMachine.RoleOf"/> path for unknown states.
/// </summary>
public class RoleExtensionTests
{
    // ── Phase 149 vocab additions ─────────────────────────────────

    [Theory]
    [InlineData("ON_HOLD",        "working")]
    [InlineData("ONHOLD",         "working")]
    [InlineData("BLOCKED_BY_RFI", "working")]
    [InlineData("WAITING_ON_QA",  "working")]
    [InlineData("PAUSED_FOR_REVIEW", "submitting")]   // REVIEW (submit) wins over PAUSED (working)
    public void InferRoleByKeyword_RecognisesExtendedWorking(string state, string expected)
    {
        Assert.Equal(expected, DeliverableStateMachine.InferRoleByKeyword(state));
    }

    [Theory]
    [InlineData("LOCKED")]
    [InlineData("FROZEN")]
    [InlineData("ABANDONED_BY_CONTRACTOR")]
    [InlineData("WITHDRAWN")]
    [InlineData("HANDED_OVER")]
    [InlineData("HANDOVER_DONE")]
    public void InferRoleByKeyword_RecognisesExtendedTerminal(string state)
    {
        Assert.Equal("terminal", DeliverableStateMachine.InferRoleByKeyword(state));
    }

    [Theory]
    [InlineData("ESCALATED")]
    [InlineData("ESCALATED_TO_DIRECTOR")]
    public void InferRoleByKeyword_RecognisesEscalatedAsSubmitting(string state)
    {
        Assert.Equal("submitting", DeliverableStateMachine.InferRoleByKeyword(state));
    }

    // ── Tenant-supplied "keywords" block ─────────────────────────

    [Fact]
    public void LoadOrDefault_TenantKeywordsExtendBuiltInVocabulary()
    {
        const string json = """
        {
          "states": ["NEW", "PARKED", "DELIVERED"],
          "transitions": [
            { "from": "NEW", "to": "PARKED" },
            { "from": "PARKED", "to": "DELIVERED" }
          ],
          "keywords": {
            "working":  ["PARKED"],
            "accepting": ["DELIVERED"]
          }
        }
        """;
        var m = DeliverableStateMachine.LoadOrDefault(json);
        Assert.True(m.IsCustom);
        Assert.Equal("working", m.RoleOf("PARKED"));
        Assert.Equal("accepting", m.RoleOf("DELIVERED"));
        // NEW resolves via the built-in InitialKeywords list since it
        // wasn't overridden by the tenant block.
        Assert.Equal("initial", m.RoleOf("NEW"));
    }

    [Fact]
    public void LoadOrDefault_TenantKeywordsCanOverrideCanonical()
    {
        // Project uses "LOCKED" to mean "engineer has reserved this row
        // for editing" — i.e. working, not terminal. Tenant keywords
        // win even though the canonical built-in maps LOCKED → terminal.
        const string json = """
        {
          "transitions": [{ "from": "DRAFT", "to": "LOCKED" }],
          "keywords": { "working": ["LOCKED"] }
        }
        """;
        var m = DeliverableStateMachine.LoadOrDefault(json);
        Assert.Equal("working", m.RoleOf("LOCKED"));
    }

    [Fact]
    public void LoadOrDefault_TenantKeywordsRespectPriorityOrder()
    {
        // Both buckets claim the same substring; rejecting beats working
        // because it's earlier in RolePriority.
        const string json = """
        {
          "transitions": [{ "from": "FOO", "to": "FOO_BLOCK" }],
          "keywords": {
            "working":   ["BLOCK"],
            "rejecting": ["BLOCK"]
          }
        }
        """;
        var m = DeliverableStateMachine.LoadOrDefault(json);
        Assert.Equal("rejecting", m.RoleOf("FOO_BLOCK"));
    }

    [Fact]
    public void LoadOrDefault_UnknownRoleNamesInKeywordsBlockAreDropped()
    {
        const string json = """
        {
          "transitions": [{ "from": "A", "to": "B" }],
          "keywords": {
            "BANANAS": ["A"],
            "working": ["B"]
          }
        }
        """;
        var m = DeliverableStateMachine.LoadOrDefault(json);
        Assert.Equal("none", m.RoleOf("A"));      // bucket name dropped
        Assert.Equal("working", m.RoleOf("B"));   // valid bucket honoured
    }

    [Fact]
    public void LoadOrDefault_NonStringEntriesInKeywordArrayAreSkipped()
    {
        const string json = """
        {
          "transitions": [{ "from": "X", "to": "Y" }],
          "keywords": {
            "working": [1, true, "X", null]
          }
        }
        """;
        var m = DeliverableStateMachine.LoadOrDefault(json);
        Assert.Equal("working", m.RoleOf("X"));   // only "X" survived
    }

    [Fact]
    public void LoadOrDefault_CustomKeywordsCanCoexistWithExplicitRoles()
    {
        // Explicit "roles" block disables built-in inference per Phase
        // 146 / 147 contract — but tenant CustomKeywords should still
        // populate at runtime via RoleOf for states that the explicit
        // block didn't cover.
        const string json = """
        {
          "transitions": [
            { "from": "S1", "to": "S2" },
            { "from": "S2", "to": "S3_PARKED" }
          ],
          "roles": { "S1": "initial", "S2": "submitting" },
          "keywords": { "working": ["PARKED"] }
        }
        """;
        var m = DeliverableStateMachine.LoadOrDefault(json);
        Assert.Equal("initial", m.RoleOf("S1"));
        Assert.Equal("submitting", m.RoleOf("S2"));
        // S3_PARKED wasn't in the explicit roles block — runtime
        // inference via custom keywords should resolve it.
        Assert.Equal("working", m.RoleOf("S3_PARKED"));
    }

    // ── Memoised RoleOf ──────────────────────────────────────────

    [Fact]
    public void RoleOf_AnswersConsistentlyAcrossCalls()
    {
        var m = DeliverableStateMachine.LoadOrDefault("""
        {
          "transitions": [{ "from": "PENDING", "to": "DONE" }]
        }
        """);
        // First call → cache miss + inference; second call → cache hit.
        // Both must agree.
        var first = m.RoleOf("UNKNOWN_STATE");
        var second = m.RoleOf("UNKNOWN_STATE");
        Assert.Equal(first, second);
        Assert.Equal("none", first);
    }

    [Fact]
    public void RoleOf_PrecomputedStatesBypassRuntimeInference()
    {
        // Every state in transitions[] should be in SemanticRoles after
        // construction, so RoleOf is a single dict lookup.
        const string json = """
        {
          "transitions": [
            { "from": "DRAFT", "to": "PUBLISHED" }
          ]
        }
        """;
        var m = DeliverableStateMachine.LoadOrDefault(json);
        Assert.Contains("DRAFT", m.SemanticRoles.Keys);
        Assert.Contains("PUBLISHED", m.SemanticRoles.Keys);
        Assert.Equal("working", m.RoleOf("DRAFT"));
        Assert.Equal("accepting", m.RoleOf("PUBLISHED"));
    }

    [Fact]
    public void RoleOf_RuntimeInferenceUsesCustomKeywords()
    {
        // PARKED isn't in transitions[], so the loader can't
        // pre-populate. RoleOf for it must consult CustomKeywords at
        // runtime.
        const string json = """
        {
          "transitions": [{ "from": "A", "to": "B" }],
          "keywords": { "working": ["PARKED"] }
        }
        """;
        var m = DeliverableStateMachine.LoadOrDefault(json);
        Assert.DoesNotContain("PARKED", m.SemanticRoles.Keys);
        Assert.Equal("working", m.RoleOf("PARKED"));
    }

    [Fact]
    public void CustomKeywords_DefaultMachineHasEmptyExtensions()
    {
        Assert.Empty(DeliverableStateMachine.Default.CustomKeywords);
    }
}
