using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Planscape.API.Controllers;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// DSCH-44 follow-up — QuantityLine.ClassificationCodeId is a required foreign key
/// (ON DELETE RESTRICT) and the plugin's BOQ sync never sent it, so every new line
/// arrived as Guid.Empty and PostgreSQL refused the insert (23503) — an HTTP 500 on
/// the first push of any baseline. Lines are now classified by an existing code id or
/// by (system code, code) resolved inside the tenant; a line that resolves to nothing
/// is a 400 naming the missing codes, and nothing is stored.
///
/// The InMemory tests exercise the controller's own resolution. EF InMemory does NOT
/// enforce foreign keys, so they cannot show the original failure: the
/// <c>Postgres_*</c> tests do, and are Skipped (never Passed) without PLANSCAPE_TEST_PG.
/// </summary>
public class BoqLineClassificationTests
{
    private sealed class FixedTenant : ITenantContext
    {
        public FixedTenant(Guid id) => TenantId = id;
        public Guid TenantId { get; }
        public string TenantSlug => "t";
        public LicenseTier Tier => LicenseTier.Professional;
        public bool MimEnabled => false;
    }

    private sealed record World(Func<Guid, PlanscapeDbContext> Ctx, Guid Tenant, Guid Project, Guid Baseline, Guid Code14);

    private static World Seed(Func<Guid, PlanscapeDbContext> ctx)
    {
        var tenant = Guid.NewGuid();
        var project = Guid.NewGuid();
        var system = Guid.NewGuid();
        var code14 = Guid.NewGuid();
        var baseline = Guid.NewGuid();
        using (var db = ctx(tenant))
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenant, Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}"[..14],
                ContactEmail = "a@e.com", Tier = LicenseTier.Professional,
                Plan = BillingPlan.Studio, MaxUsers = 50, MaxProjects = 50, IsActive = true,
            });
            db.Projects.Add(new Project
            {
                Id = project, TenantId = tenant, Name = "Tower",
                Code = $"TW-{Guid.NewGuid():N}"[..8], Status = ProjectStatus.Active,
            });
            db.ClassificationSystems.Add(new ClassificationSystem { Id = system, TenantId = tenant, Code = "NRM2", Name = "NRM2" });
            db.ClassificationCodes.Add(new ClassificationCode { Id = code14, TenantId = tenant, SystemId = system, Code = "14", Title = "Masonry", Path = "14" });
            db.BoqBaselines.Add(new BoqBaseline { Id = baseline, TenantId = tenant, ProjectId = project, Name = "Tender" });
            db.SaveChanges();
        }
        return new World(ctx, tenant, project, baseline, code14);
    }

    private static World NewInMemoryWorld()
    {
        string name = "boq-class-" + Guid.NewGuid().ToString("N");
        return Seed(t => new PlanscapeDbContext(
            new DbContextOptionsBuilder<PlanscapeDbContext>().UseInMemoryDatabase(name).Options,
            httpContextAccessor: null!, tenantContext: new FixedTenant(t)));
    }

    private static BoqController Controller(World w, PlanscapeDbContext db) => new(db, NullLogger<BoqController>.Instance, storage: null!)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("tenant_id", w.Tenant.ToString()) }, "test")),
            },
        },
    };

    /// <summary>A line as the plugin sends it: no classificationCodeId (Guid.Empty).</summary>
    private static UpsertQuantityLineRequest PluginLine(string guid, string? sys, string? code)
        => new(Guid.Empty, null, null, null, guid, "IfcWall", "1", "L1", "Z1", code ?? "", "Wall", "m2",
               10, 0, 50m, "UGX", "Measured", "Remeasure", null,
               QuantityIsFinal: true, PayloadSchemaVersion: 2,
               ClassificationSystemCode: sys, ClassificationCode: code);

    private static UpsertQuantityLineRequest IdLine(string guid, Guid codeId)
        => PluginLine(guid, null, null) with { ClassificationCodeId = codeId, SectionCode = "14" };

    private static async Task<IActionResult> Push(World w, params UpsertQuantityLineRequest[] lines)
    {
        using var db = w.Ctx(w.Tenant);
        return await Controller(w, db).UpsertQuantityLines(w.Project, w.Baseline, lines.ToList());
    }

    private static List<QuantityLine> Lines(World w)
    {
        using var db = w.Ctx(w.Tenant);
        return db.QuantityLines.AsNoTracking().Where(l => l.BaselineId == w.Baseline).ToList();
    }

    // ── the controller's resolution (InMemory) ──────────────────────────────

    [Theory]
    [InlineData("NRM2", "14")]
    [InlineData("nrm2", " 14 ")]   // system code is case-insensitive; both are trimmed
    public async Task A_Line_Classified_By_System_And_Code_Is_Stored_Against_That_Code(string sys, string code)
    {
        var w = NewInMemoryWorld();
        Assert.IsType<OkObjectResult>(await Push(w, PluginLine("A1", sys, code)));
        Assert.Equal(w.Code14, Assert.Single(Lines(w)).ClassificationCodeId);
    }

    [Fact]
    public async Task A_Known_Code_Id_Still_Works()
    {
        var w = NewInMemoryWorld();
        Assert.IsType<OkObjectResult>(await Push(w, IdLine("A2", w.Code14)));
        Assert.Equal(w.Code14, Assert.Single(Lines(w)).ClassificationCodeId);
    }

    [Fact]
    public async Task An_Unclassified_Line_Is_Refused_And_Nothing_Is_Stored()
    {
        var w = NewInMemoryWorld();
        var result = Assert.IsType<BadRequestObjectResult>(
            await Push(w, PluginLine("OK1", "NRM2", "14"), PluginLine("NO1", null, null)));
        Assert.Contains("no classification", (string)result.Value!);
        Assert.Empty(Lines(w));   // all-or-nothing: the good line is not stored either
    }

    [Fact]
    public async Task An_Undefined_Code_Is_Refused_By_Name()
    {
        var w = NewInMemoryWorld();
        var result = Assert.IsType<BadRequestObjectResult>(await Push(w, PluginLine("X1", "NRM2", "99")));
        Assert.Contains("NRM2 code '99' is not defined", (string)result.Value!);
        Assert.Empty(Lines(w));
    }

    [Fact]
    public async Task An_Unknown_Code_Id_Is_Refused()
    {
        var w = NewInMemoryWorld();
        Assert.IsType<BadRequestObjectResult>(await Push(w, IdLine("X2", Guid.NewGuid())));
        Assert.Empty(Lines(w));
    }

    [Fact]
    public async Task Another_Tenants_Code_Is_Not_Borrowed()
    {
        var w = NewInMemoryWorld();
        var other = Guid.NewGuid();
        Guid foreignCode = Guid.NewGuid();
        using (var db = w.Ctx(other))
        {
            var sys = new ClassificationSystem { TenantId = other, Code = "UNICLASS", Name = "U" };
            db.ClassificationSystems.Add(sys);
            db.ClassificationCodes.Add(new ClassificationCode { Id = foreignCode, TenantId = other, SystemId = sys.Id, Code = "Ss_25", Title = "x", Path = "Ss_25" });
            db.SaveChanges();
        }
        Assert.IsType<BadRequestObjectResult>(await Push(w, PluginLine("X3", "UNICLASS", "Ss_25")));
        Assert.IsType<BadRequestObjectResult>(await Push(w, IdLine("X4", foreignCode)));
        Assert.Empty(Lines(w));
    }

    // ── against a real PostgreSQL (FKs enforced) ─────────────────────────────

    private static string? Pg => Environment.GetEnvironmentVariable("PLANSCAPE_TEST_PG");
    private static string? PgSkip => string.IsNullOrWhiteSpace(Pg) ? "PLANSCAPE_TEST_PG is not set — no PostgreSQL to test against." : null;

    private static PlanscapeDbContext PgContext(Guid tenant) => new(
        new DbContextOptionsBuilder<PlanscapeDbContext>().UseNpgsql(Pg).Options,
        httpContextAccessor: null!, tenantContext: new FixedTenant(tenant));

    private static readonly Lazy<bool> PgSchema = new(() =>
    {
        using var db = PgContext(Guid.Empty);
        db.Database.EnsureCreated();
        return true;
    });

    private static async Task PgCleanup(World w)
    {
        try
        {
            await using var db = PgContext(w.Tenant);
            foreach (var t in new[] { "QuantityLines", "BoqBaselines", "ClassificationCodes", "ClassificationSystems", "Projects" })
                await db.Database.ExecuteSqlRawAsync($"DELETE FROM \"{t}\" WHERE \"TenantId\" = {{0}}", w.Tenant);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Tenants\" WHERE \"Id\" = {0}", w.Tenant);
        }
        catch { /* best effort — GUID-keyed, inert */ }
    }

    [SkippableFact]
    public async Task Postgres_A_Line_Without_A_Code_Violates_The_Foreign_Key()
    {
        // The original defect, shown at the database: what the controller used to insert.
        Skip.If(PgSkip is not null, PgSkip!);
        _ = PgSchema.Value;
        var w = Seed(PgContext);
        try
        {
            await using var db = PgContext(w.Tenant);
            db.QuantityLines.Add(new QuantityLine
            {
                TenantId = w.Tenant, ProjectId = w.Project, BaselineId = w.Baseline,
                ClassificationCodeId = Guid.Empty, SectionCode = "14", ItemDescription = "Wall", Unit = "m2",
            });
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("23503", ex.InnerException?.Message + (ex.InnerException as Npgsql.PostgresException)?.SqlState);
        }
        finally { await PgCleanup(w); }
    }

    [SkippableFact]
    public async Task Postgres_A_Plugin_Line_Classified_By_Code_Is_Stored()
    {
        Skip.If(PgSkip is not null, PgSkip!);
        _ = PgSchema.Value;
        var w = Seed(PgContext);
        try
        {
            Assert.IsType<OkObjectResult>(await Push(w, PluginLine("PG1", "NRM2", "14")));
            Assert.Equal(w.Code14, Assert.Single(Lines(w)).ClassificationCodeId);
        }
        finally { await PgCleanup(w); }
    }

    [SkippableFact]
    public async Task Postgres_An_Unclassified_Plugin_Line_Is_A_400_Not_A_500()
    {
        Skip.If(PgSkip is not null, PgSkip!);
        _ = PgSchema.Value;
        var w = Seed(PgContext);
        try
        {
            Assert.IsType<BadRequestObjectResult>(await Push(w, PluginLine("PG2", null, null)));
            Assert.Empty(Lines(w));
        }
        finally { await PgCleanup(w); }
    }
}
