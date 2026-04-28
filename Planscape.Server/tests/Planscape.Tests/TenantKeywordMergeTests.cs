using System.Collections.Generic;
using Planscape.Infrastructure.Workflow;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// Phase 151 — exercises the n-ary
/// <see cref="DeliverableStateMachine.MergeKeywordLayers"/> helper used
/// to combine project + tenant + platform keyword layers, plus the
/// <see cref="DbTenantKeywordResolver.ParseForValidation"/> validator
/// used by the admin tenant-keywords PUT endpoint.
/// </summary>
public class TenantKeywordMergeTests
{
    // ── n-ary merge ──────────────────────────────────────────────

    [Fact]
    public void Merge_NoLayers_ReturnsEmpty()
    {
        var merged = DeliverableStateMachine.MergeKeywordLayers();
        Assert.Empty(merged);
    }

    [Fact]
    public void Merge_AllNullLayers_ReturnsEmpty()
    {
        var merged = DeliverableStateMachine.MergeKeywordLayers(null, null, null);
        Assert.Empty(merged);
    }

    [Fact]
    public void Merge_SingleLayerPassesThrough()
    {
        var single = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["working"] = new[] { "PARKED" }
        };
        var merged = DeliverableStateMachine.MergeKeywordLayers(single);
        Assert.Same(single, merged);   // single non-empty short-circuits
    }

    [Fact]
    public void Merge_TwoLayers_HighPriorityWins()
    {
        var project = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["working"] = new[] { "PROJECT_KEYWORD" }
        };
        var platform = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["working"] = new[] { "PLATFORM_KEYWORD" }
        };
        var merged = DeliverableStateMachine.MergeKeywordLayers(project, platform);
        // Both contribute (concatenated + deduped); project entries
        // appear first because the higher-priority layer goes first
        // in the input array.
        Assert.Contains("PROJECT_KEYWORD", merged["working"]);
        Assert.Contains("PLATFORM_KEYWORD", merged["working"]);
    }

    [Fact]
    public void Merge_ThreeLayers_ProjectGtTenantGtPlatform()
    {
        var project = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["working"] = new[] { "PROJ_X" }
        };
        var tenant = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["working"] = new[] { "TENANT_X" },
            ["terminal"] = new[] { "TENANT_Y" }
        };
        var platform = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["accepting"] = new[] { "PLAT_Z" }
        };
        var merged = DeliverableStateMachine.MergeKeywordLayers(project, tenant, platform);
        Assert.Equal(3, merged.Count);
        Assert.Contains("PROJ_X", merged["working"]);
        Assert.Contains("TENANT_X", merged["working"]);
        Assert.Contains("TENANT_Y", merged["terminal"]);
        Assert.Contains("PLAT_Z", merged["accepting"]);
    }

    [Fact]
    public void Merge_DedupesEqualEntriesAcrossLayers()
    {
        var a = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["working"] = new[] { "PARKED" }
        };
        var b = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["working"] = new[] { "parked" }   // same word, different case
        };
        var merged = DeliverableStateMachine.MergeKeywordLayers(a, b);
        Assert.Single(merged["working"]);
        Assert.Equal("PARKED", System.Linq.Enumerable.First(merged["working"]));
    }

    [Fact]
    public void Merge_SkipsEmptyLayers()
    {
        var realLayer = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["working"] = new[] { "X" }
        };
        var emptyLayer = new Dictionary<string, IReadOnlyCollection<string>>();
        var merged = DeliverableStateMachine.MergeKeywordLayers(emptyLayer, realLayer, null);
        Assert.Single(merged);
        Assert.Contains("X", merged["working"]);
    }

    // ── ParseForValidation (admin endpoint validator) ────────────

    [Fact]
    public void ParseForValidation_ValidJson_ReturnsBuckets()
    {
        const string json = """
        { "working": ["PARKED"], "terminal": ["FROZEN", "DECOMMISSIONED"] }
        """;
        var parsed = DbTenantKeywordResolver.ParseForValidation(json);
        Assert.Equal(2, parsed.Count);
        Assert.Single(parsed["working"]);
        Assert.Equal(2, parsed["terminal"].Count);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]                          // wrong root
    [InlineData("\"a string\"")]                // wrong root
    [InlineData("{ \"bananas\": [\"X\"] }")]    // unknown bucket
    [InlineData("{ \"working\": \"not an array\" }")] // wrong value shape
    [InlineData("{}")]                          // empty
    public void ParseForValidation_RejectsMalformed(string json)
    {
        var parsed = DbTenantKeywordResolver.ParseForValidation(json);
        Assert.Empty(parsed);
    }

    [Fact]
    public void ParseForValidation_FiltersNonStringEntries()
    {
        const string json = """
        { "working": [1, true, null, "  ", "PARKED"] }
        """;
        var parsed = DbTenantKeywordResolver.ParseForValidation(json);
        Assert.Single(parsed["working"]);
        Assert.Contains("PARKED", parsed["working"]);
    }
}
