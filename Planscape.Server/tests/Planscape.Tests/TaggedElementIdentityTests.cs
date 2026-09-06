using Microsoft.EntityFrameworkCore;
using Planscape.Core.DTOs;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// R1 (Phase A, Increment 1) — the canonical cross-host key (IFC GlobalId) is now
/// persisted on TaggedElement and the ingest origin is stamped, so the changes
/// feed and cross-host matching can key on the GlobalId regardless of ingest door.
/// Exercised against EF InMemory (no host boot → no Jwt/Hangfire).
/// </summary>
public class TaggedElementIdentityTests
{
    private static readonly Guid TenantId  = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid ProjectId = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000b2");
    private const string Gid = "0aBcDeFgHiJkLmNoPqRsT1";

    private static PlanscapeDbContext NewDb() =>
        new(new DbContextOptionsBuilder<PlanscapeDbContext>()
            .UseInMemoryDatabase($"identity-{Guid.NewGuid():N}")
            .Options);

    private static IfcIngestRequest Request(string host, string fullTag = "A-BLD1-Z01-GF-ARC-WAL-WAL-0001") => new()
    {
        Host = host,
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
    public async Task IfcIngest_Insert_PersistsIfcGlobalIdAndSource()
    {
        using var db = NewDb();

        await new IfcIngestService(db).IngestAsync(TenantId, ProjectId, Request("archicad"));

        var row = await db.TaggedElements.IgnoreQueryFilters().FirstAsync(t => t.UniqueId == Gid);
        Assert.Equal(Gid, row.IfcGlobalId);   // R1 — explicit canonical key persisted
        Assert.Equal(Gid, row.UniqueId);      // non-Revit rows still carry the GlobalId in UniqueId
        Assert.Equal("archicad", row.Source); // R1 — origin stamped
        Assert.Equal(0, row.RevitElementId);
    }

    [Fact]
    public async Task IfcIngest_Update_KeepsIfcGlobalIdAndSource()
    {
        using var db = NewDb();
        var ingest = new IfcIngestService(db);

        await ingest.IngestAsync(TenantId, ProjectId, Request("archicad", "A-OLD"));
        // Re-ingest the same GlobalId (update branch) from a different host.
        await ingest.IngestAsync(TenantId, ProjectId, Request("bonsai", "A-NEW"));

        var rows = await db.TaggedElements.IgnoreQueryFilters().Where(t => t.UniqueId == Gid).ToListAsync();
        Assert.Single(rows);                       // matched + updated, not duplicated
        Assert.Equal("A-NEW", rows[0].Tag1);       // update applied
        Assert.Equal(Gid, rows[0].IfcGlobalId);    // canonical key preserved
        Assert.Equal("bonsai", rows[0].Source);    // origin reflects the latest writer
    }
}
