using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// R1 (Phase A, Increment 2b) — the UNIQUE (ProjectId, IfcGlobalId) index on a
/// REAL PostgreSQL server (the production engine). In-memory SQLite already proves
/// the constraint semantics; this confirms the production DDL parses + enforces on
/// Postgres itself. Gated on PLANSCAPE_TEST_PG — reports Skipped (never Passed,
/// never Failed) when there is no database, and runs in CI against the fresh
/// service-container Postgres (where EnsureCreated materialises the model,
/// including the unique index). Every test runs inside a transaction that is
/// rolled back, so it leaves no residue in a shared database.
///
///     export PLANSCAPE_TEST_PG="Host=localhost;Port=5432;Database=planscape;Username=planscape;Password=..."
///
/// The guarded patcher conversion (the Postgres DO-block in Program.cs that turns
/// the Increment-1 non-unique index unique only when the data is clean) is
/// reviewed by inspection: it must not run against a shared TaggedElements index
/// from a test, so it is intentionally NOT exercised here.
/// </summary>
public class Postgres2bIntegrationTests
{
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("PLANSCAPE_TEST_PG");

    private static string? SkipReason =>
        string.IsNullOrWhiteSpace(ConnectionString)
            ? "PLANSCAPE_TEST_PG is not set — no PostgreSQL to test against."
            : null;

    private static PlanscapeDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PlanscapeDbContext>().UseNpgsql(ConnectionString).Options);

    private static readonly Lazy<bool> SchemaReady = new(() =>
    {
        using var db = NewContext();
        db.Database.EnsureCreated();   // fresh CI DB → builds the model incl. the unique index
        return true;
    });

    private static async Task<(Guid tid, Guid pid)> SeedTenantProjectAsync(PlanscapeDbContext db)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Name = "PG 2b Org", Slug = $"pg2b-{Guid.NewGuid():N}"[..18],
            ContactEmail = "pg2b@e.com", Tier = LicenseTier.Starter, MaxUsers = 5, MaxProjects = 5, IsActive = true,
        };
        var project = new Project
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "PG 2b P", Code = $"PG2B-{Guid.NewGuid():N}"[..12],
            Phase = "Design", Status = ProjectStatus.Active,
        };
        db.Tenants.Add(tenant);
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return (tenant.Id, project.Id);
    }

    private static TaggedElement El(Guid tid, Guid pid, long revitId, string uid, string? gid)
        => new()
        {
            Id = Guid.NewGuid(), TenantId = tid, ProjectId = pid,
            RevitElementId = revitId, UniqueId = uid, IfcGlobalId = gid, Tag1 = "A", Disc = "A",
        };

    [SkippableFact]
    public async Task UniqueIndex_RejectsDuplicateIfcGlobalId_OnPostgres()
    {
        Skip.If(SkipReason is not null, SkipReason!);
        _ = SchemaReady.Value;

        using var db = NewContext();
        // Rolled back on dispose — nothing this test writes survives.
        await using var tx = await db.Database.BeginTransactionAsync();
        var (tid, pid) = await SeedTenantProjectAsync(db);
        var gid = $"PG{Guid.NewGuid():N}"[..22];

        db.TaggedElements.Add(El(tid, pid, 1, "u1-" + gid, gid));
        await db.SaveChangesAsync();

        db.TaggedElements.Add(El(tid, pid, 2, "u2-" + gid, gid));   // same (project, gid)
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());

        await tx.RollbackAsync();
    }

    [SkippableFact]
    public async Task UniqueIndex_AllowsNullsAndOtherProjects_OnPostgres()
    {
        Skip.If(SkipReason is not null, SkipReason!);
        _ = SchemaReady.Value;

        using var db = NewContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var (tid, pid) = await SeedTenantProjectAsync(db);
        var project2 = new Project
        {
            Id = Guid.NewGuid(), TenantId = tid, Name = "PG 2b P2", Code = $"PG2B2-{Guid.NewGuid():N}"[..12],
            Phase = "Design", Status = ProjectStatus.Active,
        };
        db.Projects.Add(project2);
        var gid = $"PG{Guid.NewGuid():N}"[..22];

        // Two nulls in one project + the same GlobalId across two projects — all legal.
        db.TaggedElements.Add(El(tid, pid, 1, "n1-" + gid, null));
        db.TaggedElements.Add(El(tid, pid, 2, "n2-" + gid, null));
        db.TaggedElements.Add(El(tid, pid, 3, "g1-" + gid, gid));
        db.TaggedElements.Add(El(tid, project2.Id, 4, "g2-" + gid, gid));
        await db.SaveChangesAsync();   // must NOT throw

        await tx.RollbackAsync();
    }
}
