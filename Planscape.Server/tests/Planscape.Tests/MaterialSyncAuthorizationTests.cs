using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Hosting;
using Planscape.API.Controllers;
using Planscape.Core.DTOs;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Tests;

/// <summary>
/// MaterialSyncController's authorization, which did not exist.
///
/// THE DEFECT
/// ----------
/// The controller carried [Authorize] and nothing else — no [ProjectAccess], no
/// membership check, no tenant filter — and it does not use EF, so the DbContext's
/// global tenant query filter could not cover for it the way it silently does for
/// most controllers that forget. Any authenticated user in ANY tenant who had a
/// project GUID could read another tenant's material library, and PostSnapshot was
/// equally unguarded, so they could overwrite it too. It also returned the server's
/// filesystem path in the response.
///
/// WHAT THESE TESTS COVER, AND WHAT THEY DO NOT
/// --------------------------------------------
/// They construct the controller directly (the HandoffProvisioningSqliteTests
/// precedent) and exercise the IN-ACTION guard, RequireProjectMemberAsync, against
/// a real relational database with real ProjectMember rows.
///
/// They do NOT exercise [ProjectAccess]. That is an IAsyncActionFilter and only
/// runs inside the MVC pipeline, which needs a booted host — and the host cannot
/// boot against SQLite (Program.cs's schema block issues a Postgres-only
/// information_schema query with no try/catch). So [ProjectAccess] is defence in
/// depth here and is verified in the runtime pass, not by this file. Stated
/// explicitly so nobody reads a green run as proof of the filter.
///
/// FIXTURE TRAP
/// ------------
/// PlanscapeDbContext's global filter is TenantId == CurrentTenantId and falls back
/// to Guid.Empty without an ITenantContext, matching NO rows — under which a
/// "denied" assertion passes for the wrong reason. Every context here is built with
/// a tenant, and the happy-path test asserts on NON-EMPTY round-tripped content.
/// </summary>
public class MaterialSyncAuthorizationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "matsync-tests-" + Guid.NewGuid().ToString("N"));

    private sealed class FixedTenant : ITenantContext
    {
        public FixedTenant(Guid id) => TenantId = id;
        public Guid TenantId { get; }
        public string TenantSlug => "t";
        public LicenseTier Tier => LicenseTier.Professional;
        public bool MimEnabled => false;
    }

    /// <summary>Minimal IWebHostEnvironment — only ContentRootPath is read.</summary>
    private sealed class TempEnv : IWebHostEnvironment
    {
        public TempEnv(string root) { ContentRootPath = root; Directory.CreateDirectory(root); }
        public string ContentRootPath { get; set; }
        public string ApplicationName { get; set; } = "tests";
        public string EnvironmentName { get; set; } = "Development";
        public string WebRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private PlanscapeDbContext NewContext(SqliteConnection conn, Guid tenantId)
        => new(new DbContextOptionsBuilder<PlanscapeDbContext>().UseSqlite(conn).Options,
               httpContextAccessor: null!, tenantContext: new FixedTenant(tenantId));

    private sealed record World(
        SqliteConnection Conn, Guid TenantA, Guid TenantB,
        Guid ProjectA, Guid MemberOfA, Guid OutsiderInB);

    /// <summary>Two tenants, each with a project; one member in A, one in B.</summary>
    private World NewWorld()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var tA = Guid.NewGuid(); var tB = Guid.NewGuid();
        var pA = Guid.NewGuid();
        var memberA = Guid.NewGuid(); var outsiderB = Guid.NewGuid();

        using (var ctx = NewContext(conn, tA))
        {
            ctx.Database.EnsureCreated();
            foreach (var (tid, slug) in new[] { (tA, "acme"), (tB, "other") })
                ctx.Tenants.Add(new Tenant
                {
                    Id = tid, Name = slug, Slug = $"{slug}-{Guid.NewGuid():N}"[..14],
                    ContactEmail = $"{slug}@example.com", Tier = LicenseTier.Professional,
                    Plan = BillingPlan.Studio, MaxUsers = 50, MaxProjects = 50,
                });

            ctx.Projects.Add(new Project
            {
                Id = pA, TenantId = tA, Name = "Tower A",
                Code = $"PA-{Guid.NewGuid():N}"[..8], Status = ProjectStatus.Active,
            });

            ctx.Users.Add(new AppUser
            {
                Id = memberA, TenantId = tA, Email = $"a-{Guid.NewGuid():N}@example.com",
                DisplayName = "Member A", PasswordHash = "x", IsActive = true,
            });
            ctx.Users.Add(new AppUser
            {
                Id = outsiderB, TenantId = tB, Email = $"b-{Guid.NewGuid():N}@example.com",
                DisplayName = "Outsider B", PasswordHash = "x", IsActive = true,
            });

            // Only Member A is on Project A. Outsider B is a perfectly valid,
            // authenticated user — just of a different tenant.
            ctx.ProjectMembers.Add(new ProjectMember
            {
                TenantId = tA, ProjectId = pA, UserId = memberA,
                ProjectRole = "Contributor", Iso19650Role = "M", IsActive = true,
            });
            ctx.SaveChanges();
        }
        return new World(conn, tA, tB, pA, memberA, outsiderB);
    }

    private MaterialSyncController NewController(World w, Guid actingUserId, Guid actingTenantId)
    {
        var ctrl = new MaterialSyncController(new TempEnv(_root), NewContext(w.Conn, actingTenantId));
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("user_id", actingUserId.ToString()),
            new Claim("sub", actingUserId.ToString()),
            new Claim("role", "Contributor"),      // deliberately NOT Admin/Owner
        }, "test");
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
        return ctrl;
    }

    private static MaterialSyncRequest Snapshot(Guid projectId) => new()
    {
        ProjectId = projectId,
        RevitDocPath = @"C:\models\tower.rvt",
        Materials = { new MaterialSyncRow { Name = "Concrete C30", Class = "Concrete", Origin = "Revit" } },
    };

    // ── Cross-tenant denial ──────────────────────────────────────────────────

    [Fact]
    public async Task Outsider_from_another_tenant_is_denied_on_GET()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            var ctrl = NewController(w, w.OutsiderInB, w.TenantB);
            var result = await ctrl.GetSnapshot(w.ProjectA, default);

            var denied = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(403, denied.StatusCode);
        }
    }

    [Fact]
    public async Task Outsider_from_another_tenant_is_denied_on_POST()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            var ctrl = NewController(w, w.OutsiderInB, w.TenantB);
            var result = await ctrl.PostSnapshot(w.ProjectA, Snapshot(w.ProjectA), default);

            var denied = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(403, denied.StatusCode);
        }
    }

    [Fact]
    public async Task A_denied_POST_does_not_write_the_file()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            var path = Path.Combine(_root, "App_Data", "material_snapshots", $"{w.ProjectA}.json");
            Assert.False(File.Exists(path));

            var ctrl = NewController(w, w.OutsiderInB, w.TenantB);
            await ctrl.PostSnapshot(w.ProjectA, Snapshot(w.ProjectA), default);

            // The original controller would have created it. Overwriting another
            // tenant's snapshot was the more damaging half of the defect.
            Assert.False(File.Exists(path));
        }
    }

    // ── Member happy path ────────────────────────────────────────────────────

    [Fact]
    public async Task Project_member_can_write_then_read_back_the_snapshot()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            var post = await NewController(w, w.MemberOfA, w.TenantA)
                .PostSnapshot(w.ProjectA, Snapshot(w.ProjectA), default);
            var posted = Assert.IsType<OkObjectResult>(post.Result);
            var body = Assert.IsType<MaterialSyncResponse>(posted.Value);
            Assert.Equal(1, body.RowsAccepted);

            var get = await NewController(w, w.MemberOfA, w.TenantA)
                .GetSnapshot(w.ProjectA, default);
            var got = Assert.IsType<OkObjectResult>(get.Result);
            var snap = Assert.IsType<MaterialSyncRequest>(got.Value);

            // NON-EMPTY on purpose: a guard that denied everything, or a fixture
            // whose tenant filter matched nothing, would still satisfy a
            // "no exception" test.
            Assert.NotEmpty(snap.Materials);
            Assert.Equal("Concrete C30", snap.Materials[0].Name);
            Assert.Equal(w.ProjectA, snap.ProjectId);
        }
    }

    // ── Information disclosure ───────────────────────────────────────────────

    [Fact]
    public async Task Response_does_not_leak_the_server_filesystem_path()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            var post = await NewController(w, w.MemberOfA, w.TenantA)
                .PostSnapshot(w.ProjectA, Snapshot(w.ProjectA), default);
            var body = Assert.IsType<MaterialSyncResponse>(
                Assert.IsType<OkObjectResult>(post.Result).Value);

            Assert.NotNull(body.Notes);
            Assert.DoesNotContain(_root, body.Notes!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("App_Data", body.Notes!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".json", body.Notes!, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ── Route is authoritative over the body ─────────────────────────────────

    [Fact]
    public async Task A_body_naming_a_different_project_is_rejected_not_ignored()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            var ctrl = NewController(w, w.MemberOfA, w.TenantA);
            var mismatched = Snapshot(Guid.NewGuid());          // body != route

            var result = await ctrl.PostSnapshot(w.ProjectA, mismatched, default);

            var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Contains("project_id_mismatch", bad.Value!.ToString());
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* temp dir cleanup is best-effort */ }
    }
}
