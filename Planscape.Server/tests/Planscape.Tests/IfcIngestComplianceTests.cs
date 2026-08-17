using Microsoft.EntityFrameworkCore;
using Planscape.Core.DTOs;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// Regression test for a round-trip gap: the /ifc/data door (ArchiCAD / Bonsai /
/// Tekla) used to ingest elements without ever touching the Project, so an
/// ArchiCAD/Bonsai-only project never reflected a compliance number and —
/// because ComplianceSnapshotJob filters on LastSyncAt — was never picked up by
/// the periodic job either. IfcIngestService.UpdateProjectComplianceAsync now
/// stamps the project the same way the Revit /tagsync door does.
///
/// Exercises the service directly against an EF InMemory context (no host boot),
/// so it needs neither Jwt__Key nor Postgres/Hangfire.
/// </summary>
public class IfcIngestComplianceTests
{
    private static readonly Guid TenantId  = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid ProjectId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private static PlanscapeDbContext NewDb() =>
        new(new DbContextOptionsBuilder<PlanscapeDbContext>()
            .UseInMemoryDatabase($"ifc-compliance-{Guid.NewGuid():N}")
            .Options);

    [Fact]
    public async Task IfcIngest_RefreshesProjectComplianceAndLastSyncAt()
    {
        using var db = NewDb();

        // Seed a project with STALE stats and a null LastSyncAt — the state a
        // never-Revit-synced project would be stuck in.
        db.Projects.Add(new Project
        {
            Id = ProjectId,
            TenantId = TenantId,
            Name = "ArchiCAD-only project",
            Code = "AC-001",
            Status = ProjectStatus.Active,
            TotalElements = 1000,
            TaggedElements = 800,
            CompliancePercent = 80.0,
            LastSyncAt = null,
        });
        await db.SaveChangesAsync();

        // Two elements from a non-Revit host: one tagged+complete, one untagged.
        var request = new IfcIngestRequest
        {
            Host = "archicad",
            HostDocumentGuid = "doc-guid-1",
            PluginVersion = "test",
            UserName = "tester",
            Elements = new()
            {
                new IfcElementDto
                {
                    IfcGlobalId = "0aBcDeFgHiJkLmNoPqRsT1",
                    HostElementId = "AC-1",
                    Discipline = "A", Location = "BLD1", Zone = "Z01", Level = "GF",
                    System = "ARC", Function = "WAL", Product = "WAL", Sequence = "0001",
                    FullTag = "A-BLD1-Z01-GF-ARC-WAL-WAL-0001",
                    IsComplete = true, IsFullyResolved = true, IsStale = false,
                },
                new IfcElementDto
                {
                    IfcGlobalId = "0aBcDeFgHiJkLmNoPqRsT2",
                    HostElementId = "AC-2",
                    FullTag = "",           // untagged → not counted as tagged
                    IsComplete = false,
                },
            },
        };

        var ingest = new IfcIngestService(db);
        var resp = await ingest.IngestAsync(TenantId, ProjectId, request);
        Assert.Equal(2, resp.NewElements);

        // The project must now reflect the two ingested elements — not the seeded
        // 1000/800/80 — and carry a fresh LastSyncAt (tenant filter bypassed:
        // this context has no ambient tenant, so filtered reads return nothing).
        var after = await db.Projects.IgnoreQueryFilters()
            .FirstAsync(p => p.Id == ProjectId);

        Assert.NotNull(after.LastSyncAt);                          // was null → the job now sees it
        Assert.Equal(2, after.TotalElements);                     // recomputed from TaggedElements
        Assert.Equal(1, after.TaggedElements);                    // only the one with a non-empty Tag1
        Assert.Equal(50.0, after.CompliancePercent, 1);           // 1 / 2
        Assert.Equal(100.0, after.ContainerCompliancePercent, 1); // 1 complete / 1 tagged
        Assert.Equal("AMBER", after.RagStatus);                   // 50% → AMBER
    }

    [Fact]
    public async Task IfcIngest_MissingProject_DoesNotThrow()
    {
        using var db = NewDb();   // no project seeded
        var request = new IfcIngestRequest
        {
            Host = "bonsai",
            Elements = new()
            {
                new IfcElementDto { IfcGlobalId = "0aBcDeFgHiJkLmNoPqRsT9", HostElementId = "B-1" },
            },
        };

        var ingest = new IfcIngestService(db);
        var resp = await ingest.IngestAsync(TenantId, ProjectId, request);

        Assert.Equal(1, resp.NewElements);   // elements still ingest even with no Project row
    }
}
