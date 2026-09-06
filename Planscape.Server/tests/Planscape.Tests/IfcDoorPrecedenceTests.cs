using Microsoft.EntityFrameworkCore;
using Planscape.Core.DTOs;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// R1 (Phase A, Increment 2b) — upsert precedence on the /ifc/data door.
/// An ArchiCAD/Bonsai push now resolves on the canonical IfcGlobalId FIRST, so it
/// converges onto an existing REVIT-origin row for the same element instead of
/// inserting a second row. The DB-level UNIQUE-constraint enforcement + the Revit
/// /tagsync door (which needs transactions) are covered by the Postgres
/// integration test; here we pin the match behaviour on EF InMemory.
/// </summary>
public class IfcDoorPrecedenceTests
{
    private static readonly Guid TenantId  = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000d1");
    private static readonly Guid ProjectId = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000d2");
    private const string Gid = "0aBcDeFgHiJkLmNoPqRsT1";

    private static PlanscapeDbContext NewDb() =>
        new(new DbContextOptionsBuilder<PlanscapeDbContext>()
            .UseInMemoryDatabase($"precedence-{Guid.NewGuid():N}")
            .Options);

    private static IfcIngestRequest ArchicadPush(string fullTag) => new()
    {
        Host = "archicad",
        HostDocumentGuid = "doc-1",
        Elements = new()
        {
            new IfcElementDto
            {
                IfcGlobalId = Gid, HostElementId = "AC-1",
                Discipline = "A", Location = "BLD1", Zone = "Z01", Level = "GF",
                System = "ARC", Function = "WAL", Product = "WAL", Sequence = "0001",
                FullTag = fullTag, IsComplete = true,
            },
        },
    };

    [Fact]
    public async Task IfcIngest_ConvergesOntoExistingRevitRow_NoDuplicate()
    {
        using var db = NewDb();
        // A Revit-origin row already exists for element G (pushed via /tagsync,
        // carrying its IfcGlobalId per Increment 1).
        db.TaggedElements.Add(new TaggedElement
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId,
            RevitElementId = 42, UniqueId = "revit-unique-id-42", IfcGlobalId = Gid,
            Tag1 = "A-OLD", Disc = "A", Source = "revit",
            LastModifiedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();

        // ArchiCAD pushes the SAME element (same GlobalId) via /ifc/data.
        await new IfcIngestService(db).IngestAsync(TenantId, ProjectId, ArchicadPush("A-NEW"));

        var rows = await db.TaggedElements.IgnoreQueryFilters()
            .Where(t => t.IfcGlobalId == Gid).ToListAsync();
        Assert.Single(rows);                              // converged — NOT a second row
        Assert.Equal(42, rows[0].RevitElementId);         // Revit identity preserved (update didn't clobber it)
        Assert.Equal("revit-unique-id-42", rows[0].UniqueId);
        Assert.Equal("A-NEW", rows[0].Tag1);              // the ArchiCAD update applied
        Assert.Equal("archicad", rows[0].Source);
    }

    [Fact]
    public async Task IfcIngest_NoMatchingRow_InsertsOne()
    {
        using var db = NewDb();   // empty — no existing row for G

        await new IfcIngestService(db).IngestAsync(TenantId, ProjectId, ArchicadPush("A-NEW"));

        var rows = await db.TaggedElements.IgnoreQueryFilters()
            .Where(t => t.IfcGlobalId == Gid).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(0, rows[0].RevitElementId);          // non-Revit insert
        Assert.Equal(Gid, rows[0].UniqueId);              // GlobalId parked in UniqueId too
    }

    [Fact]
    public async Task IfcIngest_LegacyRowWithoutIfcGlobalId_StillMatchesByUniqueId()
    {
        using var db = NewDb();
        // A pre-Increment-1 non-Revit row: GlobalId only in UniqueId, IfcGlobalId null.
        db.TaggedElements.Add(new TaggedElement
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId,
            RevitElementId = 0, UniqueId = Gid, IfcGlobalId = null,
            Tag1 = "A-OLD", Disc = "A", Source = "archicad",
        });
        await db.SaveChangesAsync();

        await new IfcIngestService(db).IngestAsync(TenantId, ProjectId, ArchicadPush("A-NEW"));

        var rows = await db.TaggedElements.IgnoreQueryFilters()
            .Where(t => t.UniqueId == Gid).ToListAsync();
        Assert.Single(rows);                              // matched the legacy row via UniqueId fallback
        Assert.Equal("A-NEW", rows[0].Tag1);
        Assert.Equal(Gid, rows[0].IfcGlobalId);           // …and healed its IfcGlobalId
    }
}
