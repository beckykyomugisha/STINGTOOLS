using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Planscape.Infrastructure.Workflow;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// Phase 150 — exercises the platform-wide keyword-extension layer.
/// Two surfaces:
///   - <see cref="ConfigPlatformKeywordRegistry"/> reads from
///     IConfiguration ("DeliverableStateMachine:Keywords" section).
///   - <see cref="DeliverableStateMachine.LoadOrDefault(string?, IReadOnlyDictionary{string, IReadOnlyCollection{string}})"/>
///     merges platform layer with project layer (project wins on
///     bucket collisions, otherwise the lists concatenate + dedupe).
/// </summary>
public class PlatformKeywordTests
{
    // ── Config registry ──────────────────────────────────────────

    [Fact]
    public void ConfigRegistry_AbsentSection_YieldsEmpty()
    {
        var config = new ConfigurationBuilder().Build(); // no providers
        var reg = new ConfigPlatformKeywordRegistry(config);
        Assert.Empty(reg.Keywords);
    }

    [Fact]
    public void ConfigRegistry_PopulatesValidBuckets()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DeliverableStateMachine:Keywords:working:0"] = "PARKED",
                ["DeliverableStateMachine:Keywords:working:1"] = "WAITING_ON_X",
                ["DeliverableStateMachine:Keywords:terminal:0"] = "DECOMMISSIONED",
            })
            .Build();
        var reg = new ConfigPlatformKeywordRegistry(config);
        Assert.Equal(2, reg.Keywords.Count);
        Assert.Contains("PARKED", reg.Keywords["working"]);
        Assert.Contains("WAITING_ON_X", reg.Keywords["working"]);
        Assert.Contains("DECOMMISSIONED", reg.Keywords["terminal"]);
    }

    [Fact]
    public void ConfigRegistry_DropsUnknownBucketNames()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DeliverableStateMachine:Keywords:bananas:0"] = "WHATEVER",
                ["DeliverableStateMachine:Keywords:working:0"] = "PARKED",
            })
            .Build();
        var reg = new ConfigPlatformKeywordRegistry(config);
        Assert.Single(reg.Keywords);
        Assert.True(reg.Keywords.ContainsKey("working"));
    }

    [Fact]
    public void ConfigRegistry_DedupesEntries()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DeliverableStateMachine:Keywords:working:0"] = "PARKED",
                ["DeliverableStateMachine:Keywords:working:1"] = "parked",  // dup, different case
                ["DeliverableStateMachine:Keywords:working:2"] = "  ",       // whitespace, dropped
            })
            .Build();
        var reg = new ConfigPlatformKeywordRegistry(config);
        Assert.Single(reg.Keywords["working"]);
        Assert.Equal("PARKED", System.Linq.Enumerable.First(reg.Keywords["working"]));
    }

    [Fact]
    public void EmptyRegistry_HasNoKeywords()
    {
        var reg = new EmptyPlatformKeywordRegistry();
        Assert.Empty(reg.Keywords);
    }

    // ── LoadOrDefault layered merge ─────────────────────────────

    [Fact]
    public void LoadOrDefault_PlatformOnly_AppliesToDefaultMachine()
    {
        var platform = new Dictionary<string, IReadOnlyCollection<string>>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["working"] = new[] { "PARKED" },
        };
        // Null project JSON → loader returns Default; with platform
        // layer it's a Default copy carrying the platform keywords.
        var m = DeliverableStateMachine.LoadOrDefault(null, platform);
        Assert.Equal("ISO_19650_v1", m.Name);
        // RoleOf for an unknown state should pick up via platform layer.
        Assert.Equal("working", m.RoleOf("PARKED"));
    }

    [Fact]
    public void LoadOrDefault_PlatformOnly_NullDoesNotCloneDefault()
    {
        // Pass null platform — must return the singleton Default
        // unchanged so existing callers keep their behaviour.
        var m1 = DeliverableStateMachine.LoadOrDefault(null, platformKeywords: null);
        Assert.Same(DeliverableStateMachine.Default, m1);
    }

    [Fact]
    public void LoadOrDefault_PlatformAndProject_MergesWithProjectWinning()
    {
        const string json = """
        {
          "transitions": [
            { "from": "DRAFT", "to": "LOCKED" },
            { "from": "LOCKED", "to": "DELIVERED" }
          ],
          "keywords": { "working": ["LOCKED"] }
        }
        """;
        var platform = new Dictionary<string, IReadOnlyCollection<string>>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["terminal"] = new[] { "LOCKED" },           // platform says LOCKED → terminal
            ["accepting"] = new[] { "DELIVERED" },       // platform contributes DELIVERED
        };
        var m = DeliverableStateMachine.LoadOrDefault(json, platform);
        Assert.True(m.IsCustom);
        // Project says working → project wins.
        Assert.Equal("working", m.RoleOf("LOCKED"));
        // DELIVERED only in platform layer → still applies.
        Assert.Equal("accepting", m.RoleOf("DELIVERED"));
    }

    [Fact]
    public void LoadOrDefault_BackwardCompat_SingleArgStillWorks()
    {
        // The pre-Phase-150 single-arg signature is preserved.
        const string json = """
        {
          "transitions": [{ "from": "A", "to": "B" }]
        }
        """;
        var m = DeliverableStateMachine.LoadOrDefault(json);
        Assert.True(m.IsCustom);
        Assert.Empty(m.CustomKeywords);
    }
}
