using System.Reflection;
using Planscape.API.Controllers;
using Planscape.Core.Entities;

namespace Planscape.Tests;

/// <summary>
/// #633 — the CDE state machine, served with the document instead of re-derived
/// by every client.
///
/// WHAT THIS IS FOR
/// ----------------
/// Mobile kept its own copy of both halves and both had drifted, in OPPOSITE
/// directions, which is why neither showed up as an obvious bug:
///
///   VALID: mobile said PUBLISHED -> [ARCHIVE]. The server also allows
///          SUPERSEDED and WITHDRAWN, and SHARED -> WITHDRAWN. Three legal
///          transitions the user could not see at all.
///
///   APPROVAL: mobile said {WIP->SHARED, SHARED->PUBLISHED}. The server says
///          {SHARED->PUBLISHED, PUBLISHED->SUPERSEDED}. So WIP->SHARED was
///          routed through an approval workflow the server does not require,
///          and PUBLISHED->SUPERSEDED was sent at the transition endpoint,
///          which refuses it for want of an approval record.
///
/// These tests read the controller's OWN dictionaries by reflection rather than
/// restating them. A test that repeats the table is a fourth copy, and would
/// happily agree with a wrong one.
/// </summary>
public class AllowedTransitionsTests
{
    private static readonly MethodInfo Compute =
        typeof(DocumentsController).GetMethod(
            "AllowedTransitionsFor", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static IReadOnlyList<CdeTransitionOption> For(string state)
        => (IReadOnlyList<CdeTransitionOption>)Compute.Invoke(null, new object[] { state })!;

    private static Dictionary<string, string[]> ValidTransitions =>
        (Dictionary<string, string[]>)typeof(DocumentsController)
            .GetField("ValidTransitions", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    private static HashSet<string> ApprovalRequired =>
        (HashSet<string>)typeof(DocumentsController)
            .GetField("ApprovalRequiredTransitions", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    // ── Sanity — reflection actually found the real tables ───────────────────

    [Fact]
    public void Sanity_the_controller_tables_were_found_and_are_not_empty()
    {
        // Reflection that silently returns nothing would make every assertion
        // below pass vacuously — the exact failure mode this suite keeps hitting.
        Assert.NotNull(Compute);
        Assert.NotEmpty(ValidTransitions);
        Assert.NotEmpty(ApprovalRequired);
    }

    // ── The served list IS the controller's table ────────────────────────────

    [Fact]
    public void Every_state_offers_exactly_what_the_state_machine_allows()
    {
        foreach (var (state, targets) in ValidTransitions)
        {
            var served = For(state).Select(o => o.To).ToArray();
            Assert.Equal(targets, served);
        }
    }

    [Fact]
    public void Approval_is_flagged_for_exactly_the_transitions_that_require_it()
    {
        foreach (var (state, targets) in ValidTransitions)
        foreach (var option in For(state))
            Assert.Equal(
                ApprovalRequired.Contains($"{state}->{option.To}"),
                option.RequiresApproval);

        // And the flag is actually used somewhere — an all-false result would
        // satisfy the loop above if ApprovalRequired never matched anything.
        Assert.Contains(
            ValidTransitions.Keys.SelectMany(For),
            o => o.RequiresApproval);
    }

    // ── The specific drifts that motivated this ──────────────────────────────

    [Fact]
    public void PUBLISHED_offers_the_two_transitions_mobile_could_not_see()
    {
        var served = For("PUBLISHED").Select(o => o.To).ToArray();

        Assert.Contains("ARCHIVE", served);      // mobile knew this one
        Assert.Contains("SUPERSEDED", served);   // it did not
        Assert.Contains("WITHDRAWN", served);    // nor this
    }

    [Fact]
    public void SHARED_offers_WITHDRAWN_which_mobile_could_not_see()
        => Assert.Contains("WITHDRAWN", For("SHARED").Select(o => o.To));

    [Fact]
    public void WIP_to_SHARED_does_NOT_require_approval()
    {
        // Mobile asserted it did, and routed it through the approval workflow —
        // filing a request for a transition the server would have performed
        // directly, then telling the user to wait for a decision nobody was
        // asked to make.
        var toShared = Assert.Single(For("WIP"), o => o.To == "SHARED");
        Assert.False(toShared.RequiresApproval);
    }

    [Fact]
    public void PUBLISHED_to_SUPERSEDED_DOES_require_approval()
    {
        // The other half of the same drift: mobile did not know, so it would
        // have called the direct endpoint and been refused.
        var toSuperseded = Assert.Single(For("PUBLISHED"), o => o.To == "SUPERSEDED");
        Assert.True(toSuperseded.RequiresApproval);
    }

    // ── Terminal and unknown states ──────────────────────────────────────────

    [Theory]
    [InlineData("ARCHIVE")]
    [InlineData("SUPERSEDED")]
    [InlineData("WITHDRAWN")]
    [InlineData("OBSOLETE")]
    public void A_terminal_state_serves_an_empty_list_not_null(string state)
    {
        // Empty is a real answer: the state machine was consulted and offers
        // nothing. Null is reserved for "not computed", which clients read as
        // unknown. Conflating them would make a terminal document look like an
        // old server, and vice versa.
        var served = For(state);
        Assert.NotNull(served);
        Assert.Empty(served);
    }

    [Fact]
    public void An_unrecognised_state_serves_empty_rather_than_throwing()
    {
        // A document carrying a state the machine does not know must not take
        // the whole document list down with it.
        var served = For("NOT_A_REAL_STATE");
        Assert.NotNull(served);
        Assert.Empty(served);
    }

    // ── Affordance, not authority ────────────────────────────────────────────

    [Fact]
    public void The_computation_depends_on_nothing_but_the_current_state()
    {
        // Deliberately excludes TransitionRoleRequirements and the per-folder
        // ACL: those are per-caller, and folding them in here would make a list
        // projection do a per-row authorization pass while implying the result
        // is a permission grant. If this method ever gains a caller or document
        // parameter, that decision is being reversed and should be argued.
        var parameters = Compute.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    // ── The field is additive, not persisted ─────────────────────────────────

    [Fact]
    public void AllowedTransitions_is_NotMapped_so_no_schema_changes()
    {
        var prop = typeof(DocumentRecord).GetProperty(nameof(DocumentRecord.AllowedTransitions))!;
        Assert.NotNull(prop.GetCustomAttribute<
            System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute>());
    }

    [Fact]
    public void AllowedTransitions_defaults_to_null_meaning_not_computed()
    {
        // A freshly-constructed document must not claim "no transitions
        // available" — that is a statement the state machine has not made.
        Assert.Null(new DocumentRecord().AllowedTransitions);
    }
}
