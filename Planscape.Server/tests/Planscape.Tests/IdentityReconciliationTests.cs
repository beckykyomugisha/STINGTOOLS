using Microsoft.EntityFrameworkCore;
using Planscape.Core.Constants;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// R1 (Phase A, Increment 2) — the identity dedup/backfill tool. Merges the
/// two-row duplication (a Revit row + its ArchiCAD/IFC twin) into one, keeping
/// the Revit identity and the freshest data, and backfills IfcGlobalId onto
/// Revit rows from ExternalElementMapping. EF InMemory (unique indexes are not
/// enforced there, so duplicates can be seeded).
/// </summary>
public class IdentityReconciliationTests
{
    private static readonly Guid TenantId  = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000c1");
    private static readonly Guid ProjectId = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000c2");
    private const string Gid  = "0aBcDeFgHiJkLmNoPqRsT1";
    private const string Gid2 = "0aBcDeFgHiJkLmNoPqRsT2";
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    private static PlanscapeDbContext NewDb() =>
        new(new DbContextOptionsBuilder<PlanscapeDbContext>()
            .UseInMemoryDatabase($"reconcile-{Guid.NewGuid():N}")
            .Options);

    private static TaggedElement El(long revitId, string uniqueId, string? gid, string tag1,
        DateTime? lastMod, string source) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId,
        RevitElementId = revitId, UniqueId = uniqueId, IfcGlobalId = gid,
        Tag1 = tag1, Disc = "A", LastModifiedUtc = lastMod, Source = source, Version = 1,
    };

    // ── Merge: Revit row + IFC row for the same GlobalId collapse to one, keeping
    //    the Revit identity (as primary) and the freshest data. ──
    [Fact]
    public async Task Apply_MergesDuplicate_KeepsRevitIdentityAndFreshestData()
    {
        using var db = NewDb();
        db.TaggedElements.Add(El(42, "revit-uid-42", Gid, "A-OLD", T0, "revit")); // Revit, older
        db.TaggedElements.Add(El(0,  Gid,           Gid, "A-NEW", T1, "bonsai")); // IFC twin, fresher
        await db.SaveChangesAsync();

        var report = await new IdentityReconciliationService(db).ApplyAsync(TenantId);

        Assert.Equal(1, report.DuplicateGroups);
        Assert.Equal(1, report.RowsMerged);

        var rows = await db.TaggedElements.IgnoreQueryFilters().Where(t => t.IfcGlobalId == Gid).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(42, rows[0].RevitElementId);          // Revit identity preserved (primary)
        Assert.Equal("revit-uid-42", rows[0].UniqueId);    // …UniqueId never mutated
        Assert.Equal("A-NEW", rows[0].Tag1);               // …data taken from the freshest row
        Assert.Equal(T1, rows[0].LastModifiedUtc);         // …newest modification stamp wins
    }

    // ── Backfill: a Revit row with no IfcGlobalId gets it from ExternalElementMapping. ──
    [Fact]
    public async Task Apply_BackfillsRevitIfcGlobalIdFromMapping()
    {
        using var db = NewDb();
        db.TaggedElements.Add(El(7, "revit-uid-7", null, "A", T0, "revit"));
        db.ExternalElementMappings.Add(new ExternalElementMapping
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId,
            IfcGlobalId = Gid, Host = MappingHosts.Revit, HostElementId = "7",
        });
        await db.SaveChangesAsync();

        var report = await new IdentityReconciliationService(db).ApplyAsync(TenantId);

        Assert.Equal(1, report.RevitRowsBackfilled);
        var row = await db.TaggedElements.IgnoreQueryFilters().FirstAsync(t => t.RevitElementId == 7);
        Assert.Equal(Gid, row.IfcGlobalId);
    }

    // ── Backfill THEN merge: the realistic case — a Revit row (unkeyed) + an IFC
    //    twin; backfill keys the Revit row, then the two merge. ──
    [Fact]
    public async Task Apply_BackfillThenMerge_CollapsesToOne()
    {
        using var db = NewDb();
        db.TaggedElements.Add(El(9, "revit-uid-9", null, "A-OLD", T0, "revit")); // no GlobalId yet
        db.TaggedElements.Add(El(0, Gid,           Gid,  "A-NEW", T1, "bonsai"));
        db.ExternalElementMappings.Add(new ExternalElementMapping
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId,
            IfcGlobalId = Gid, Host = MappingHosts.Revit, HostElementId = "9",
        });
        await db.SaveChangesAsync();

        var report = await new IdentityReconciliationService(db).ApplyAsync(TenantId);

        Assert.Equal(1, report.RevitRowsBackfilled);
        Assert.Equal(1, report.RowsMerged);
        var rows = await db.TaggedElements.IgnoreQueryFilters().Where(t => t.IfcGlobalId == Gid).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(9, rows[0].RevitElementId);   // the (now-keyed) Revit row is primary
        Assert.Equal("A-NEW", rows[0].Tag1);        // freshest data
    }

    // ── Analyze is a pure dry-run: reports the counts, mutates nothing. ──
    [Fact]
    public async Task Analyze_ReportsButDoesNotMutate()
    {
        using var db = NewDb();
        db.TaggedElements.Add(El(42, "revit-uid-42", Gid, "A-OLD", T0, "revit"));
        db.TaggedElements.Add(El(0,  Gid,           Gid, "A-NEW", T1, "bonsai"));
        await db.SaveChangesAsync();

        var report = await new IdentityReconciliationService(db).AnalyzeAsync(TenantId);

        Assert.False(report.Applied);
        Assert.Equal(1, report.DuplicateGroups);
        Assert.Equal(1, report.DuplicateRows);
        Assert.Equal(0, report.RowsMerged);                                   // nothing removed
        Assert.Equal(2, await db.TaggedElements.IgnoreQueryFilters().CountAsync()); // both rows intact
    }

    // ── Idempotent: a second apply is a no-op. ──
    [Fact]
    public async Task Apply_Twice_IsNoOpSecondTime()
    {
        using var db = NewDb();
        db.TaggedElements.Add(El(42, "revit-uid-42", Gid, "A-OLD", T0, "revit"));
        db.TaggedElements.Add(El(0,  Gid,           Gid, "A-NEW", T1, "bonsai"));
        await db.SaveChangesAsync();
        var svc = new IdentityReconciliationService(db);

        await svc.ApplyAsync(TenantId);
        var second = await svc.ApplyAsync(TenantId);

        Assert.Equal(0, second.DuplicateGroups);
        Assert.Equal(0, second.RowsMerged);
        Assert.Equal(0, second.RevitRowsBackfilled);
    }

    // ── Clean project: distinct elements are left alone. ──
    [Fact]
    public async Task Apply_NoDuplicates_IsNoOp()
    {
        using var db = NewDb();
        db.TaggedElements.Add(El(1, "revit-uid-1", Gid,  "A", T0, "revit"));
        db.TaggedElements.Add(El(2, "revit-uid-2", Gid2, "A", T0, "revit"));
        await db.SaveChangesAsync();

        var report = await new IdentityReconciliationService(db).ApplyAsync(TenantId);

        Assert.Equal(0, report.DuplicateGroups);
        Assert.Equal(0, report.RowsMerged);
        Assert.Equal(2, await db.TaggedElements.IgnoreQueryFilters().CountAsync());
    }
}
