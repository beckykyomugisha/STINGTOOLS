using System.Linq;
using Planscape.Infrastructure.Workflow;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// Phase 155 — pins the role-block-vs-keyword-block asymmetry that
/// the dashboard JS validator relies on.
///
/// The asymmetry is intentional:
///   • Keyword blocks (`"keywords"` in tenant / project / platform
///     JSON) cannot use "none" because there's no notion of a
///     "no-role keyword vocabulary" — every entry must map a
///     state-name substring to one of the six canonical buckets.
///   • Role blocks (`"roles"` in custom-machine JSON) CAN use "none"
///     because that's the explicit way to say "this state has no
///     side-effect role; skip the metadata stamp on transition".
///
/// Both sides reference <see cref="RoleBuckets"/> as the source of
/// truth so we need a test that pins the contract: WithNone is a
/// strict superset of Set with exactly one extra entry.
/// </summary>
public class RoleBucketsAsymmetryTests
{
    [Fact]
    public void WithNone_IsStrictSupersetOfSet()
    {
        Assert.True(RoleBuckets.Set.IsSubsetOf(RoleBuckets.WithNone));
        Assert.True(RoleBuckets.WithNone.Count > RoleBuckets.Set.Count);
    }

    [Fact]
    public void WithNone_HasExactlyOneExtraEntry()
    {
        Assert.Equal(RoleBuckets.Set.Count + 1, RoleBuckets.WithNone.Count);
    }

    [Fact]
    public void WithNone_AddsTheNoneSentinel()
    {
        var extras = RoleBuckets.WithNone.Except(RoleBuckets.Set).ToList();
        Assert.Single(extras);
        Assert.Contains("none", extras, System.StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Canonical_PriorityOrderUnchanged()
    {
        // Pins the priority order RoleBuckets.Canonical advertises so
        // the loader's inference path stays "rejecting first, initial
        // last" — outcome-beats-in-flight (Phase 148 contract).
        Assert.Equal(
            new[] { "rejecting", "accepting", "submitting", "terminal", "working", "initial" },
            RoleBuckets.Canonical.ToArray());
    }
}
