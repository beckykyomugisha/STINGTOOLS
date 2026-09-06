using System.Reflection;
using Planscape.Shared.Models;

namespace Planscape.Tests;

/// <summary>
/// Guards the wire model against the failure that broke element sync: a
/// hand-maintained field-copy list that silently stopped carrying fields.
///
/// The original bug copied ten of ~thirty fields from the plugin's internal
/// payload onto <see cref="TagElementSync"/>. RevitElementId was one of the
/// twenty it dropped, so every element arrived with RevitElementId = 0, the
/// server deduped them all onto one row, and a whole project collapsed to a
/// single row. Nothing failed loudly; the dashboard just read 0%.
///
/// These tests are deliberately reflection-driven so they FAIL when someone
/// adds a field and forgets the copy path, rather than passing against a
/// hard-coded list that rots the same way the original code did.
/// </summary>
public class PluginSyncMappingTests
{
    // ── PluginSyncPayload.WithElements ────────────────────────────────────

    [Fact]
    public void WithElements_preserves_every_payload_field_except_the_elements()
    {
        var original = new PluginSyncPayload
        {
            ProjectId = Guid.NewGuid(),
            PluginVersion = "2.2.0",
            RevitVersion = "2025",
            UserName = "sting",
            Timestamp = new DateTime(2026, 8, 2, 10, 30, 0, DateTimeKind.Utc),
            SeqCounters = new Dictionary<string, int> { ["M|HVAC"] = 42 },
            Compliance = new ComplianceSync { TotalElements = 7, RagStatus = "GREEN" },
            Issues = new List<IssueSync> { new() { IssueCode = "RFI-001" } },
            WorkflowRuns = new List<WorkflowRunSync> { new() { PresetName = "DailyQA" } },
            TagElements = new List<TagElementSync> { new() { RevitElementId = 1 } },
        };

        var copy = original.WithElements(new List<TagElementSync> { new() { RevitElementId = 99 } });

        // Every property except TagElements must survive the rebuild. Driven by
        // reflection so a newly added payload field that WithElements forgets
        // shows up here as a failure instead of vanishing on every filtered drain.
        foreach (var prop in typeof(PluginSyncPayload).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.Name == nameof(PluginSyncPayload.TagElements)) continue;
            Assert.Equal(prop.GetValue(original), prop.GetValue(copy));
        }

        Assert.Single(copy.TagElements!);
        Assert.Equal(99, copy.TagElements![0].RevitElementId);
    }

    [Fact]
    public void WithElements_leaves_the_source_payload_untouched()
    {
        var original = new PluginSyncPayload
        {
            TagElements = new List<TagElementSync> { new() { RevitElementId = 1 } },
        };

        original.WithElements(new List<TagElementSync>());

        Assert.Single(original.TagElements!);
    }

    // ── TagElementSync field coverage ─────────────────────────────────────

    /// <summary>
    /// Every field the plugin's TagElementSyncMapper populates. The mapper lives
    /// in the Revit-dependent StingTools assembly and cannot be referenced from
    /// a net8.0 test project, so this is the canary: adding a property to
    /// TagElementSync without adding it here fails, which forces whoever adds it
    /// to go and decide how the mapper fills it. That is the exact step that was
    /// skipped when the wire model gained fields the copy list never learned about.
    /// </summary>
    private static readonly HashSet<string> MappedFields = new()
    {
        nameof(TagElementSync.RevitElementId),
        nameof(TagElementSync.UniqueId),
        nameof(TagElementSync.IfcGlobalId),
        nameof(TagElementSync.Disc),
        nameof(TagElementSync.Loc),
        nameof(TagElementSync.Zone),
        nameof(TagElementSync.Lvl),
        nameof(TagElementSync.Sys),
        nameof(TagElementSync.Func),
        nameof(TagElementSync.Prod),
        nameof(TagElementSync.Seq),
        nameof(TagElementSync.Tag1),
        nameof(TagElementSync.Tag7),
        nameof(TagElementSync.CategoryName),
        nameof(TagElementSync.FamilyName),
        nameof(TagElementSync.Status),
        nameof(TagElementSync.Rev),
        nameof(TagElementSync.IsComplete),
        nameof(TagElementSync.IsFullyResolved),
        nameof(TagElementSync.IsStale),
        nameof(TagElementSync.IsDeleted),
        nameof(TagElementSync.LastModifiedUtc),
        nameof(TagElementSync.Tag7A),
        nameof(TagElementSync.Tag7B),
        nameof(TagElementSync.Tag7C),
        nameof(TagElementSync.Tag7D),
        nameof(TagElementSync.Tag7E),
        nameof(TagElementSync.Tag7F),
        nameof(TagElementSync.T4Commissioning),
        nameof(TagElementSync.T5Cost),
        nameof(TagElementSync.T6Carbon),
        nameof(TagElementSync.T7Fabrication),
        nameof(TagElementSync.T8ClashTriage),
        nameof(TagElementSync.T9AsBuilt),
        nameof(TagElementSync.T10Compliance),
        nameof(TagElementSync.ParaDepth),
        nameof(TagElementSync.PatternMode),
    };

    [Fact]
    public void Every_TagElementSync_field_is_accounted_for_by_the_mapper()
    {
        var actual = typeof(TagElementSync)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        var unmapped = actual.Except(MappedFields).OrderBy(x => x).ToList();
        Assert.True(unmapped.Count == 0,
            "TagElementSync gained field(s) the plugin mapper may not populate: "
            + string.Join(", ", unmapped)
            + ". Add them to StingTools.Core.Sync.TagElementSyncMapper.MapElement (and to "
            + "SyncReconciler.ComputeHash if the value should trigger a re-send), then list "
            + "them in MappedFields. Silently skipping this step is how RevitElementId came "
            + "to be dropped on the wire.");

        var stale = MappedFields.Except(actual).OrderBy(x => x).ToList();
        Assert.True(stale.Count == 0,
            "MappedFields lists field(s) that no longer exist on TagElementSync: " + string.Join(", ", stale));
    }

    [Fact]
    public void Identity_fields_exist_and_are_writable()
    {
        // The specific fields whose loss broke sync. Named explicitly so the
        // regression is legible even if the reflection test above is edited.
        foreach (var name in new[]
                 {
                     nameof(TagElementSync.RevitElementId),
                     nameof(TagElementSync.UniqueId),
                     nameof(TagElementSync.IfcGlobalId),
                 })
        {
            var prop = typeof(TagElementSync).GetProperty(name);
            Assert.NotNull(prop);
            Assert.True(prop!.CanWrite, $"{name} must be settable by the mapper");
        }
    }

    [Fact]
    public void IsDeleted_defaults_to_false()
    {
        // Forward-compatible flag: transmitted now, honoured by the server once
        // the parallel delete-channel work lands. It must never default to true.
        Assert.False(new TagElementSync().IsDeleted);
    }
}
