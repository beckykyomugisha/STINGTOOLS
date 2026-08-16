using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Planscape.API.Authorization;
using Planscape.API.Controllers;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.Tests;

/// <summary>
/// TRACK A1 — cross-PROJECT access control on the federation controllers.
///
/// THE DEFECT
/// ----------
/// ModelTransformController, CoordinateSystemController, AlignmentController,
/// SceneNodesController and ModelDiffController carried <c>[Authorize]</c> and
/// filtered by TENANT only. Tenant isolation is sound — the global ITenantScoped
/// filter sees to that — but tenant is not the unit of authorization here:
/// a user invited to ANY ONE project in a tenant could read and overwrite EVERY
/// OTHER project's coordinate system, model transforms and geometry in the same
/// tenant. ModelsController and IfcIngestController already applied the
/// documented gate; these five were missed.
///
/// WHY THE TESTS ATTACK WITHIN A TENANT
/// ------------------------------------
/// This is the distinguishing detail, and the reason the pre-existing
/// tenant-isolation tests never caught it. Every attacker below is a real,
/// active, authenticated member of the SAME tenant as the victim project — just
/// a member of a DIFFERENT project in it. A cross-tenant test passes against the
/// unfixed code (the tenant filter already stopped that) and proves nothing.
///
/// WHAT IS COVERED, AND WHAT IS NOT
/// --------------------------------
///   • The in-action write gate (RequireProjectMemberAsync → 403) is driven by
///     constructing each controller directly against real SQLite with real
///     ProjectMember rows — the MaterialSyncAuthorizationTests precedent.
///   • <see cref="ProjectAccessAttribute"/> (the read gate → 404) IS exercised,
///     by driving the filter directly with a hand-built ActionExecutingContext —
///     the ProjectAccessCacheOutageTests precedent. That existing file only
///     covers the CROSS-TENANT case; the within-tenant non-member case added
///     here is the one that was broken.
///   • <see cref="ControllersCarryTheReadGate"/> pins the attribute onto each
///     controller by reflection, so removing it fails a test rather than
///     silently re-opening the hole.
///
/// It does NOT boot a host: a WebApplicationFactory cannot run against SQLite
/// (Program.cs issues a Postgres-only information_schema query with no
/// try/catch) and a second factory is unreliable in this assembly (ROADMAP
/// DEP-7, process-wide Hangfire teardown). Stated so a green run is not read as
/// proof of end-to-end MVC wiring.
///
/// FIXTURE TRAP
/// ------------
/// PlanscapeDbContext's global filter is TenantId == CurrentTenantId and falls
/// back to Guid.Empty without an ITenantContext, matching NO rows — under which
/// a "denied" assertion passes for the wrong reason. Every context here is built
/// WITH a tenant, and every denial test is paired with a member happy-path test
/// asserting a non-empty effect.
/// </summary>
public class FederationAuthorizationTests
{
    // ── fixture ──────────────────────────────────────────────────────────────

    private sealed class FixedTenant : ITenantContext
    {
        public FixedTenant(Guid id) => TenantId = id;
        public Guid TenantId { get; }
        public string TenantSlug => "t";
        public LicenseTier Tier => LicenseTier.Professional;
        public bool MimEnabled => false;
    }

    private static PlanscapeDbContext NewContext(SqliteConnection conn, Guid tenantId)
        => new(new DbContextOptionsBuilder<PlanscapeDbContext>().UseSqlite(conn).Options,
               httpContextAccessor: null!, tenantContext: new FixedTenant(tenantId));

    /// <summary>
    /// One tenant, two projects. <c>Insider</c> is an active member of
    /// <c>Victim</c>. <c>Neighbour</c> is an active member of <c>Other</c> only —
    /// same tenant, no membership on Victim. Neighbour is the attacker.
    /// </summary>
    private sealed record World(
        SqliteConnection Conn,
        Guid Tenant,
        Guid VictimProject, Guid OtherProject,
        Guid Insider, Guid Neighbour,
        Guid VictimModel, Guid VictimNode);

    private static World NewWorld()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var tenant = Guid.NewGuid();
        var victim = Guid.NewGuid();
        var other = Guid.NewGuid();
        var insider = Guid.NewGuid();
        var neighbour = Guid.NewGuid();
        var model = Guid.NewGuid();
        var node = Guid.NewGuid();

        using (var ctx = NewContext(conn, tenant))
        {
            ctx.Database.EnsureCreated();

            ctx.Tenants.Add(new Tenant
            {
                Id = tenant, Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}"[..14],
                ContactEmail = "acme@example.com", Tier = LicenseTier.Professional,
                Plan = BillingPlan.Studio, MaxUsers = 50, MaxProjects = 50,
            });

            ctx.Projects.Add(new Project
            {
                Id = victim, TenantId = tenant, Name = "Victim Tower",
                Code = $"VT-{Guid.NewGuid():N}"[..8], Status = ProjectStatus.Active,
            });
            ctx.Projects.Add(new Project
            {
                Id = other, TenantId = tenant, Name = "Other Tower",
                Code = $"OT-{Guid.NewGuid():N}"[..8], Status = ProjectStatus.Active,
            });

            foreach (var (id, name) in new[] { (insider, "Insider"), (neighbour, "Neighbour") })
                ctx.Users.Add(new AppUser
                {
                    Id = id, TenantId = tenant, Email = $"{name}-{Guid.NewGuid():N}@example.com",
                    DisplayName = name, PasswordHash = "x", IsActive = true,
                });

            ctx.ProjectMembers.Add(new ProjectMember
            {
                TenantId = tenant, ProjectId = victim, UserId = insider,
                ProjectRole = "Contributor", Iso19650Role = "M", IsActive = true,
            });
            // The whole point: Neighbour IS a member — of the other project.
            ctx.ProjectMembers.Add(new ProjectMember
            {
                TenantId = tenant, ProjectId = other, UserId = neighbour,
                ProjectRole = "Contributor", Iso19650Role = "M", IsActive = true,
            });

            ctx.ProjectModels.Add(new ProjectModel
            {
                Id = model, TenantId = tenant, ProjectId = victim,
                Name = "Victim ARCH", FileName = "victim.glb", StoragePath = "t_x/victim.glb",
            });

            ctx.SceneNodes.Add(new SceneNode
            {
                Id = node, TenantId = tenant, ProjectId = victim, SourceModelId = model,
                Discipline = "A", StoragePath = "t_x/chunk.glb", ContentHash = "deadbeef",
            });

            ctx.SaveChanges();
        }

        return new World(conn, tenant, victim, other, insider, neighbour, model, node);
    }

    /// <summary>
    /// A non-admin principal — Admin/Owner bypasses membership by design.
    ///
    /// <c>tenant_id</c> is required, not decorative: SceneNodesController's
    /// chunk-file check runs through <see cref="ProjectVisibility"/>, which
    /// reads the tenant off the claims and treats a missing one as
    /// <see cref="Guid.Empty"/> — i.e. sees nothing. Omitting it would make the
    /// non-member test pass while the member test failed, which is exactly the
    /// "denied for the wrong reason" trap.
    /// </summary>
    private static ControllerContext ContextFor(Guid userId, Guid tenantId) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim("user_id", userId.ToString()),
                new Claim("sub", userId.ToString()),
                new Claim("tenant_id", tenantId.ToString()),
                new Claim("role", "Contributor"),
            }, "test")),
        },
    };

    private static ModelTransformController NewTransformController(World w, Guid actor)
    {
        var c = new ModelTransformController(
            NewContext(w.Conn, w.Tenant), new FixedTenant(w.Tenant),
            delta: null!, logger: NullLogger<ModelTransformController>.Instance)
        { ControllerContext = ContextFor(actor, w.Tenant) };
        return c;
    }

    private static CoordinateSystemController NewCrsController(World w, Guid actor)
        => new(NewContext(w.Conn, w.Tenant), new FixedTenant(w.Tenant))
        { ControllerContext = ContextFor(actor, w.Tenant) };

    private static AlignmentController NewAlignmentController(World w, Guid actor)
        => new(NewContext(w.Conn, w.Tenant), new FixedTenant(w.Tenant))
        { ControllerContext = ContextFor(actor, w.Tenant) };

    private static SceneNodesController NewSceneController(World w, Guid actor, IFileStorageService storage)
        => new(NewContext(w.Conn, w.Tenant), new FixedTenant(w.Tenant), storage)
        { ControllerContext = ContextFor(actor, w.Tenant) };

    private static TransformUpsertDto SomeTransform(double tx = 12_345.0) =>
        new(tx, 0, 0, RotationDeg: 0, ScaleFactor: 1.0, IsConfirmed: true, Notes: "hostile");

    private static void AssertForbidden(ActionResult? result)
    {
        var denied = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, denied.StatusCode);
    }

    // ── ModelTransformController — the highest-value write ───────────────────

    [Fact]
    public async Task Neighbour_cannot_overwrite_another_projects_model_transform()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            var result = await NewTransformController(w, w.Neighbour)
                .Upsert(w.VictimProject, w.VictimModel, SomeTransform(), default);

            AssertForbidden(result);

            // The denial must be a denial, not a 403 issued after the write.
            using var check = NewContext(w.Conn, w.Tenant);
            Assert.False(await check.Set<ProjectModelTransform>().AnyAsync());
        }
    }

    [Fact]
    public async Task Neighbour_cannot_delete_another_projects_model_transform()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            using (var seed = NewContext(w.Conn, w.Tenant))
            {
                seed.Set<ProjectModelTransform>().Add(new ProjectModelTransform
                {
                    TenantId = w.Tenant, ProjectId = w.VictimProject,
                    ProjectModelId = w.VictimModel, TranslationX = 999, IsConfirmed = true,
                });
                await seed.SaveChangesAsync();
            }

            var result = await NewTransformController(w, w.Neighbour)
                .Delete(w.VictimProject, w.VictimModel, default);

            var denied = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, denied.StatusCode);

            using var check = NewContext(w.Conn, w.Tenant);
            Assert.True(await check.Set<ProjectModelTransform>().AnyAsync(),
                "the coordinator's transform was deleted by a non-member");
        }
    }

    [Fact]
    public async Task Insider_can_still_upsert_the_transform()
    {
        // The mirror case. A gate that denied everyone would pass every test
        // above while breaking the feature.
        var w = NewWorld();
        using (w.Conn)
        {
            var result = await NewTransformController(w, w.Insider)
                .Upsert(w.VictimProject, w.VictimModel, SomeTransform(777.0), default);

            Assert.IsType<OkObjectResult>(result);

            using var check = NewContext(w.Conn, w.Tenant);
            var saved = await check.Set<ProjectModelTransform>().SingleAsync();
            Assert.Equal(777.0, saved.TranslationX);
        }
    }

    // ── CoordinateSystemController — the frame everything aligns to ──────────

    [Fact]
    public async Task Neighbour_cannot_create_another_projects_coordinate_system()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            var result = await NewCrsController(w, w.Neighbour)
                .Create(w.VictimProject, new CoordinateSystemDto { CrsEpsgCode = "EPSG:9999" }, default);

            AssertForbidden(result.Result);

            using var check = NewContext(w.Conn, w.Tenant);
            Assert.False(await check.ProjectCoordinateSystems.AnyAsync());
        }
    }

    [Fact]
    public async Task Neighbour_cannot_update_or_delete_another_projects_coordinate_system()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            using (var seed = NewContext(w.Conn, w.Tenant))
            {
                seed.ProjectCoordinateSystems.Add(new ProjectCoordinateSystem
                {
                    TenantId = w.Tenant, ProjectId = w.VictimProject,
                    CrsEpsgCode = "EPSG:27700", OriginEasting = 100, OriginNorthing = 200,
                });
                await seed.SaveChangesAsync();
            }

            var update = await NewCrsController(w, w.Neighbour)
                .Update(w.VictimProject, new CoordinateSystemDto { CrsEpsgCode = "EPSG:9999" }, default);
            AssertForbidden(update.Result);

            var delete = await NewCrsController(w, w.Neighbour).Delete(w.VictimProject, default);
            var denied = Assert.IsType<ObjectResult>(delete);
            Assert.Equal(403, denied.StatusCode);

            using var check = NewContext(w.Conn, w.Tenant);
            var crs = await check.ProjectCoordinateSystems.SingleAsync();
            Assert.Equal("EPSG:27700", crs.CrsEpsgCode);
        }
    }

    [Fact]
    public async Task Insider_can_still_create_the_coordinate_system()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            var result = await NewCrsController(w, w.Insider)
                .Create(w.VictimProject, new CoordinateSystemDto { CrsEpsgCode = "EPSG:27700" }, default);

            Assert.IsType<CreatedAtActionResult>(result.Result);

            using var check = NewContext(w.Conn, w.Tenant);
            Assert.Equal("EPSG:27700", (await check.ProjectCoordinateSystems.SingleAsync()).CrsEpsgCode);
        }
    }

    // ── AlignmentController — denial must happen BEFORE the work ─────────────

    private sealed class SpyCoherenceJob : IFederatedCoherenceJob
    {
        public bool Ran { get; private set; }
        public Task<FederatedCoherenceReport> RunAsync(Guid projectId, Guid tenantId, CancellationToken ct)
        {
            Ran = true;
            return Task.FromResult(new FederatedCoherenceReport(
                projectId, 0, 0, 0, 0, Array.Empty<CoherenceIssue>(), DateTime.UtcNow));
        }
    }

    private sealed class SpyAutoAlign : IAutoAlignService
    {
        public bool Ran { get; private set; }
        public Task<AutoAlignResult> ComputeAsync(
            Guid projectId, Guid tenantId, Guid targetModelId,
            Microsoft.AspNetCore.SignalR.IHubContext<Planscape.Infrastructure.SignalR.FederatedModelHub>? modelHub = null,
            CancellationToken ct = default,
            Microsoft.AspNetCore.SignalR.IHubContext<Planscape.Infrastructure.SignalR.NotificationHub>? notificationHub = null)
        {
            Ran = true;
            return Task.FromResult(new AutoAlignResult(true, 1, 2, 3, 4, 5, null, null));
        }
    }

    [Fact]
    public async Task Neighbour_cannot_run_another_projects_coherence_scan()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            var job = new SpyCoherenceJob();

            var result = await NewAlignmentController(w, w.Neighbour)
                .RunCoherence(w.VictimProject, job, default);

            AssertForbidden(result);
            Assert.False(job.Ran, "the scan ran before the caller was authorized");
        }
    }

    [Fact]
    public async Task Neighbour_cannot_auto_align_another_projects_model()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            var svc = new SpyAutoAlign();

            var result = await NewAlignmentController(w, w.Neighbour)
                .AutoAlign(w.VictimProject, w.VictimModel, svc,
                           modelHub: null!, notificationHub: null!, default);

            AssertForbidden(result);
            Assert.False(svc.Ran, "auto-align wrote a transform before the caller was authorized");
        }
    }

    [Fact]
    public async Task Insider_can_still_run_the_coherence_scan()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            var job = new SpyCoherenceJob();

            var result = await NewAlignmentController(w, w.Insider)
                .RunCoherence(w.VictimProject, job, default);

            Assert.IsType<OkObjectResult>(result);
            Assert.True(job.Ran);
        }
    }

    // ── SceneNodesController — geometry download by node id ──────────────────

    /// <summary>Serves one fixed blob; every other member is out of scope here.</summary>
    private sealed class StubStorage : IFileStorageService
    {
        public bool Served { get; private set; }

        public Task<Stream?> GetAsync(string path, CancellationToken ct = default, bool bypassTenantCheck = false)
        {
            Served = true;
            return Task.FromResult<Stream?>(new MemoryStream(new byte[] { 0x67, 0x6C, 0x54, 0x46 }));
        }

        private static Exception No() => new NotSupportedException("not used by these tests");
        public Task<string> SaveScopedAsync(Guid t, Guid p, string f, Stream c, CancellationToken ct = default) => throw No();
        public Task<string> SaveAsync(string t, string p, string f, Stream c, CancellationToken ct = default) => throw No();
        public Task<bool> DeleteAsync(string path, CancellationToken ct = default, bool b = false) => throw No();
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default, bool b = false) => throw No();
        public Task<int> DeleteByPrefixAsync(string prefix, CancellationToken ct = default, bool b = false) => throw No();
        public Task<PresignedUpload> GetPresignedPutUrlAsync(string k, string c, TimeSpan v, long m, CancellationToken ct = default) => throw No();
        public Task<string> GetPresignedGetUrlAsync(string k, TimeSpan v, CancellationToken ct = default, bool b = false) => throw No();
        public Task MoveAsync(string s, string d, CancellationToken ct = default, bool b = false) => throw No();
    }

    [Fact]
    public async Task Neighbour_cannot_download_another_projects_geometry_chunk()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            var storage = new StubStorage();

            var result = await NewSceneController(w, w.Neighbour, storage)
                .GetChunkFile(w.VictimNode, default);

            // 404, not 403 — matching [ProjectAccess], so the response does not
            // confirm the chunk exists.
            Assert.IsType<NotFoundResult>(result);
            Assert.False(storage.Served, "the geometry was read from storage before the caller was authorized");
        }
    }

    [Fact]
    public async Task Insider_can_still_download_the_geometry_chunk()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            var storage = new StubStorage();

            var result = await NewSceneController(w, w.Insider, storage)
                .GetChunkFile(w.VictimNode, default);

            Assert.IsType<FileStreamResult>(result);
            Assert.True(storage.Served);
        }
    }

    // ── [ProjectAccess] — the read gate, driven directly ─────────────────────

    /// <summary>
    /// The within-tenant case the existing ProjectAccessCacheOutageTests does
    /// not cover: same tenant, active user, member of a different project.
    /// </summary>
    [Fact]
    public async Task ReadGate_404s_a_same_tenant_non_member()
    {
        var (ctx, next) = BuildFilterContext(member: false);

        await new ProjectAccessAttribute().OnActionExecutionAsync(ctx, next.Delegate);

        Assert.IsType<NotFoundResult>(ctx.Result);
        Assert.False(next.WasCalled);
    }

    [Fact]
    public async Task ReadGate_allows_a_project_member()
    {
        var (ctx, next) = BuildFilterContext(member: true);

        await new ProjectAccessAttribute().OnActionExecutionAsync(ctx, next.Delegate);

        Assert.Null(ctx.Result);
        Assert.True(next.WasCalled);
    }

    /// <summary>
    /// The gate is an attribute, so it can be deleted in a refactor without
    /// breaking a single behavioural test. Pin it.
    /// </summary>
    [Theory]
    [InlineData(typeof(ModelTransformController))]
    [InlineData(typeof(CoordinateSystemController))]
    [InlineData(typeof(AlignmentController))]
    [InlineData(typeof(SceneNodesController))]
    [InlineData(typeof(ModelDiffController))]
    public void ControllersCarryTheReadGate(Type controller)
    {
        Assert.True(
            controller.GetCustomAttributes(typeof(ProjectAccessAttribute), inherit: true).Length > 0,
            $"{controller.Name} lost [ProjectAccess] — any authenticated user in the tenant can reach "
            + "another project's federation data again.");
    }

    private static (ActionExecutingContext, NextSpy) BuildFilterContext(bool member)
    {
        var tenant = Guid.NewGuid();
        var project = Guid.NewGuid();
        var caller = Guid.NewGuid();
        var author = Guid.NewGuid();   // NOT the caller — the author bypass must not fire

        var db = new PlanscapeDbContext(
            new DbContextOptionsBuilder<PlanscapeDbContext>()
                .UseInMemoryDatabase($"FedAuthz_{Guid.NewGuid():N}").Options,
            new HttpContextAccessor(),
            new FixedTenant(tenant));

        db.Projects.Add(new Project
        {
            Id = project, TenantId = tenant, CreatedById = author,
            Code = "FA-001", Name = "Victim",
        });
        if (member)
        {
            db.ProjectMembers.Add(new ProjectMember
            {
                TenantId = tenant, ProjectId = project, UserId = caller,
                ProjectRole = "Contributor", Iso19650Role = "M", IsActive = true,
            });
        }
        db.SaveChanges();

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<IDistributedCache>(new NoopDistributedCache());

        var http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, caller.ToString()),
                new Claim("tenant_id", tenant.ToString()),
                new Claim("role", "Contributor"),
            }, "test")),
        };

        var routeData = new RouteData();
        routeData.Values["projectId"] = project.ToString();

        var ctx = new ActionExecutingContext(
            new ActionContext(http, routeData, new ControllerActionDescriptor()),
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: null!);

        return (ctx, new NextSpy(ctx));
    }

    /// <summary>Always-miss cache, so every call reaches the authoritative query.</summary>
    private sealed class NoopDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options,
                             CancellationToken token = default) => Task.CompletedTask;
    }

    private sealed class NextSpy
    {
        private readonly ActionExecutingContext _ctx;
        public NextSpy(ActionExecutingContext ctx) => _ctx = ctx;
        public bool WasCalled { get; private set; }

        public ActionExecutionDelegate Delegate => () =>
        {
            WasCalled = true;
            return Task.FromResult(new ActionExecutedContext(
                _ctx, new List<IFilterMetadata>(), controller: null!));
        };
    }
}
