using Planscape.Infrastructure.Workflow;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// Phase 154 — pins the canonical role-bucket contract that the
/// dashboard JS validator relies on. Adding / removing / renaming a
/// bucket should be a deliberate update to this test alongside the
/// production list — no silent drift.
/// </summary>
public class RoleBucketsTests
{
    [Fact]
    public void Canonical_HasExactlySixBuckets()
    {
        Assert.Equal(6, RoleBuckets.Canonical.Count);
    }

    [Fact]
    public void Canonical_ContainsTheExpectedNames()
    {
        // Sorted comparison so the test doesn't pin priority order
        // (RoleBucketsTests covers that separately if it ever needs to).
        var sorted = new System.Collections.Generic.SortedSet<string>(RoleBuckets.Canonical);
        Assert.Equal(
            new System.Collections.Generic.SortedSet<string>(new[]
            {
                "accepting", "initial", "rejecting", "submitting", "terminal", "working",
            }),
            sorted);
    }

    [Theory]
    [InlineData("WORKING")]
    [InlineData("working")]
    [InlineData("Working")]
    public void Set_IsCaseInsensitive(string probe)
    {
        Assert.Contains(probe, RoleBuckets.Set);
    }

    [Fact]
    public void WithNone_AddsTheNoneSentinel()
    {
        Assert.Equal(7, RoleBuckets.WithNone.Count);
        Assert.Contains("none", RoleBuckets.WithNone);
    }

    [Fact]
    public void Canonical_DoesNotContainNone()
    {
        // "none" is a sentinel, not a bucket tenants can author.
        Assert.DoesNotContain("none", RoleBuckets.Canonical);
    }

    [Fact]
    public void Set_RejectsUnknownBucket()
    {
        Assert.DoesNotContain("BANANAS", RoleBuckets.Set);
    }
}
