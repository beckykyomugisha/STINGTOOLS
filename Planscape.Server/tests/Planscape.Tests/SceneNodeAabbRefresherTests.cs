using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.Tests;

/// <summary>
/// TRACK B / P5 — scene-chunk bounds after a model moves.
///
/// THE DEFECT
/// ----------
/// The world-space AABB recompute lived inline in
/// <c>ModelTransformController.Upsert</c> and had two problems that compounded
/// each other:
///
/// 1. It ran ONLY there. Both automatic transform writers (IFC ingest,
///    auto-align) moved models without touching the bounds, so the federation
///    manifest described where the chunks USED to be. The viewer culls against
///    those bounds, so the symptom is geometry that disappears when the camera
///    looks straight at it — or streams in when it is nowhere near the frustum.
///
/// 2. It read the STORED (already-transformed) box and transformed it AGAIN. So
///    the obvious fix — "call it after every transform write" — would have made
///    things worse, not better: each call would have compounded the transform
///    and walked the bounds further from the geometry.
///
/// Fixing (2) had to come first, which is why <see cref="SceneNode.BaseMinX"/>…
/// exists: the world box is now a pure function of (local box, transform) and
/// can be recomputed any number of times with the same answer.
/// </summary>
public class SceneNodeAabbRefresherTests
{
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

    private sealed record World(SqliteConnection Conn, Guid Tenant, Guid Project, Guid Model, Guid Node);

    /// <param name="withBaseBox">
    /// false reproduces a row written BEFORE the local box existed.
    /// </param>
    private static World NewWorld(bool withBaseBox = true)
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var tenant = Guid.NewGuid();
        var project = Guid.NewGuid();
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
                Id = project, TenantId = tenant, Name = "Tower",
                Code = $"TW-{Guid.NewGuid():N}"[..8], Status = ProjectStatus.Active,
            });
            ctx.ProjectModels.Add(new ProjectModel
            {
                Id = model, TenantId = tenant, ProjectId = project,
                Name = "ARCH", FileName = "a.glb", StoragePath = "t_x/a.glb",
            });

            var n = new SceneNode
            {
                Id = node, TenantId = tenant, ProjectId = project, SourceModelId = model,
                Discipline = "A", StoragePath = "t_x/c.glb", ContentHash = "abc",
                MinX = 0, MinY = 0, MinZ = 0, MaxX = 1000, MaxY = 2000, MaxZ = 3000,
            };
            if (withBaseBox)
            {
                n.BaseMinX = 0; n.BaseMinY = 0; n.BaseMinZ = 0;
                n.BaseMaxX = 1000; n.BaseMaxY = 2000; n.BaseMaxZ = 3000;
            }
            ctx.SceneNodes.Add(n);
            ctx.SaveChanges();
        }

        return new World(conn, tenant, project, model, node);
    }

    private static SceneNodeAabbRefresher NewRefresher(PlanscapeDbContext db)
        => new(db, NullLogger<SceneNodeAabbRefresher>.Instance);

    private static async Task AddTransformAsync(World w, double tx, double rotDeg = 0, double scale = 1.0)
    {
        using var ctx = NewContext(w.Conn, w.Tenant);
        ctx.ProjectModelTransforms.Add(new ProjectModelTransform
        {
            TenantId = w.Tenant, ProjectId = w.Project, ProjectModelId = w.Model,
            TranslationX = tx, RotationDeg = rotDeg, ScaleFactor = scale,
        });
        await ctx.SaveChangesAsync();
    }

    private static async Task<SceneNode> ReadNodeAsync(World w)
    {
        using var ctx = NewContext(w.Conn, w.Tenant);
        return await ctx.SceneNodes.AsNoTracking().SingleAsync();
    }

    // ── the idempotence that made this safe to call everywhere ──────────────

    [Fact]
    public async Task Refreshing_twice_gives_the_same_answer_as_once()
    {
        // THE regression. The old in-place version transformed an
        // already-transformed box, so the second call moved the bounds another
        // 5 m and every subsequent call moved them further.
        var w = NewWorld();
        using (w.Conn)
        {
            await AddTransformAsync(w, tx: 5000);

            using (var db = NewContext(w.Conn, w.Tenant))
                await NewRefresher(db).RefreshAsync(w.Project, w.Model, w.Tenant);
            var once = await ReadNodeAsync(w);

            using (var db = NewContext(w.Conn, w.Tenant))
                await NewRefresher(db).RefreshAsync(w.Project, w.Model, w.Tenant);
            var twice = await ReadNodeAsync(w);

            Assert.Equal(once.MinX, twice.MinX, 6);
            Assert.Equal(once.MaxX, twice.MaxX, 6);
            Assert.Equal(5000.0, twice.MinX, 6);
            Assert.Equal(6000.0, twice.MaxX, 6);
        }
    }

    [Fact]
    public async Task The_local_box_is_never_mutated()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            await AddTransformAsync(w, tx: 5000);
            using (var db = NewContext(w.Conn, w.Tenant))
                await NewRefresher(db).RefreshAsync(w.Project, w.Model, w.Tenant);

            var n = await ReadNodeAsync(w);
            Assert.Equal(0.0, n.BaseMinX);
            Assert.Equal(1000.0, n.BaseMaxX);
        }
    }

    [Fact]
    public async Task Removing_the_transform_returns_the_box_to_its_local_position()
    {
        // Only possible because the local box survives. The old in-place
        // version had overwritten it, so a DELETE could never restore anything.
        var w = NewWorld();
        using (w.Conn)
        {
            await AddTransformAsync(w, tx: 5000);
            using (var db = NewContext(w.Conn, w.Tenant))
                await NewRefresher(db).RefreshAsync(w.Project, w.Model, w.Tenant);
            Assert.Equal(5000.0, (await ReadNodeAsync(w)).MinX, 6);

            using (var db = NewContext(w.Conn, w.Tenant))
            {
                db.ProjectModelTransforms.RemoveRange(db.ProjectModelTransforms);
                await db.SaveChangesAsync();
            }
            using (var db = NewContext(w.Conn, w.Tenant))
                await NewRefresher(db).RefreshAsync(w.Project, w.Model, w.Tenant);

            var n = await ReadNodeAsync(w);
            Assert.Equal(0.0, n.MinX, 6);
            Assert.Equal(1000.0, n.MaxX, 6);
        }
    }

    // ── back-compat with rows written before the local box existed ──────────

    [Fact]
    public async Task A_pre_existing_row_has_its_local_box_captured_on_first_refresh()
    {
        var w = NewWorld(withBaseBox: false);
        using (w.Conn)
        {
            await AddTransformAsync(w, tx: 5000);

            using (var db = NewContext(w.Conn, w.Tenant))
                await NewRefresher(db).RefreshAsync(w.Project, w.Model, w.Tenant);

            var n = await ReadNodeAsync(w);
            Assert.Equal(0.0, n.BaseMinX);      // captured from the pre-refresh box
            Assert.Equal(5000.0, n.MinX, 6);    // and the world box moved once
        }
    }

    // ── the rotation case that a two-corner shortcut gets wrong ─────────────

    [Fact]
    public async Task A_rotated_box_encloses_all_eight_transformed_corners()
    {
        // Rotating 90° about Z maps (x, y) → (-y, x), so a 1000 x 2000 footprint
        // becomes 2000 x 1000. Transforming only the min and max corners would
        // produce min=(-2000, 0) max=(0, 1000) read in the wrong order and give
        // an inverted, too-small box — which culls geometry that is on screen.
        var w = NewWorld();
        using (w.Conn)
        {
            await AddTransformAsync(w, tx: 0, rotDeg: 90);
            using (var db = NewContext(w.Conn, w.Tenant))
                await NewRefresher(db).RefreshAsync(w.Project, w.Model, w.Tenant);

            var n = await ReadNodeAsync(w);
            Assert.Equal(-2000.0, n.MinX, 6);
            Assert.Equal(0.0, n.MaxX, 6);
            Assert.Equal(0.0, n.MinY, 6);
            Assert.Equal(1000.0, n.MaxY, 6);
            Assert.True(n.MaxX >= n.MinX && n.MaxY >= n.MinY, "the box came out inverted");
        }
    }

    [Fact]
    public async Task Scale_is_applied_before_translation()
    {
        // T·R·S — the composition ModelTransformMath documents. If scale were
        // applied after translation the offset would be scaled too.
        var w = NewWorld();
        using (w.Conn)
        {
            await AddTransformAsync(w, tx: 1000, scale: 2.0);
            using (var db = NewContext(w.Conn, w.Tenant))
                await NewRefresher(db).RefreshAsync(w.Project, w.Model, w.Tenant);

            var n = await ReadNodeAsync(w);
            Assert.Equal(1000.0, n.MinX, 6);              // 0*2 + 1000
            Assert.Equal(3000.0, n.MaxX, 6);              // 1000*2 + 1000
        }
    }

    [Fact]
    public async Task A_model_with_no_chunks_is_a_no_op()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            using var db = NewContext(w.Conn, w.Tenant);
            var updated = await NewRefresher(db).RefreshAsync(w.Project, Guid.NewGuid(), w.Tenant);
            Assert.Equal(0, updated);
        }
    }
}
