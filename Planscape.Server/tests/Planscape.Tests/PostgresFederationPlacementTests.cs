using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Planscape.Core.Coordinates;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// TRACK B PAYOFF — the whole point of the placement work, on the production
/// database engine.
///
/// THE SCENARIO
/// ------------
/// A coordinator uploads two models of the SAME site from DIFFERENT tools, with
/// different survey origins and a shared CRS, and does not type a transform.
/// Both must be placed automatically, and the parts of the building they share
/// must end up in the same world position.
///
/// WHAT THIS EXERCISES, AND WHAT IT DOES NOT
/// -----------------------------------------
/// It drives the server-side placement chain end to end — frame resolution,
/// translation, confidence grading, the auto-apply decision, the transform row,
/// and the scene-chunk world AABBs — on real PostgreSQL, so the DDL, the
/// nullable columns added by the startup patcher, and the double-precision
/// arithmetic are all the production ones.
///
/// It does NOT parse IFC. There are no .ifc fixtures in this repo and xbim is a
/// heavy dependency to stand up for a placement test, so the georeferencing is
/// supplied as the values an IfcMapConversion + IfcProjectedCRS would yield.
/// The IFC PARSER is therefore not under test here; the placement chain that
/// consumes it is. Stated so a green run is not read as proof that a real Tekla
/// or ArchiCAD export parses correctly.
///
/// GATING
/// ------
/// Skipped (never passed, never failed) without PLANSCAPE_TEST_PG. Each test
/// runs in a transaction that is rolled back, so it leaves no residue in a
/// shared database.
///
///     export PLANSCAPE_TEST_PG="Host=localhost;Port=55432;Database=planscape;Username=planscape;Password=testpass"
/// </summary>
public class PostgresFederationPlacementTests
{
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("PLANSCAPE_TEST_PG");

    private static string? SkipReason =>
        string.IsNullOrWhiteSpace(ConnectionString)
            ? "PLANSCAPE_TEST_PG is not set — no PostgreSQL to test against."
            : null;

    private sealed class FixedTenant : ITenantContext
    {
        public FixedTenant(Guid id) => TenantId = id;
        public Guid TenantId { get; }
        public string TenantSlug => "fed";
        public LicenseTier Tier => LicenseTier.Professional;
        public bool MimEnabled => false;
    }

    /// <summary>
    /// FIXTURE TRAP — the context MUST carry a tenant.
    ///
    /// PlanscapeDbContext's global filter is TenantId == CurrentTenantId and
    /// falls back to Guid.Empty without an ITenantContext, matching NO rows. A
    /// context built from the options-only constructor therefore writes rows the
    /// very next read cannot see, and the writer's own "is there an existing
    /// transform?" lookup silently finds nothing — so a "not overwritten"
    /// assertion would pass for entirely the wrong reason. The tenant id is
    /// minted before the context so it can be supplied here.
    /// </summary>
    private static PlanscapeDbContext NewContext(Guid tenantId) =>
        new(new DbContextOptionsBuilder<PlanscapeDbContext>().UseNpgsql(ConnectionString).Options,
            httpContextAccessor: null!, tenantContext: new FixedTenant(tenantId));

    /// <summary>Schema bootstrap only — never used for reads.</summary>
    private static PlanscapeDbContext NewSchemaContext() =>
        new(new DbContextOptionsBuilder<PlanscapeDbContext>().UseNpgsql(ConnectionString).Options);

    private static readonly Lazy<bool> SchemaReady = new(() =>
    {
        using var db = NewSchemaContext();
        db.Database.EnsureCreated();
        return true;
    });

    private static ModelGeorefWriter NewWriter(PlanscapeDbContext db)
        => new(db,
               new SceneNodeAabbRefresher(db, NullLogger<SceneNodeAabbRefresher>.Instance),
               NullLogger<ModelGeorefWriter>.Instance);

    // A shared site in British National Grid. The two models are authored about
    // different internal origins — which is exactly what makes this a federation
    // problem rather than a rendering one.
    private const string Crs = "EPSG:27700";
    private const double ArchEast = 432_000.0, ArchNorth = 315_000.0;
    private const double StructEast = 432_060.0, StructNorth = 315_045.0;

    private static ModelGeoref Georef(double e, double n, string source) =>
        new(EastingM: e, NorthingM: n, ElevationM: 0, TrueNorthDeg: 0,
            CrsCode: Crs, HasDeclaredCrs: true, LengthUnit: "m",
            SourceLabel: source, MapConversionScale: 1.0);

    private sealed record Site(Guid Tenant, Guid Project, Guid ArchModel, Guid StructModel);

    private static async Task<Site> SeedAsync(PlanscapeDbContext db, Guid tenantId, bool declareProjectCrs)
    {
        var tenant = new Tenant
        {
            Id = tenantId, Name = "Fed Org", Slug = $"fed-{Guid.NewGuid():N}"[..18],
            ContactEmail = "fed@e.com", Tier = LicenseTier.Professional,
            MaxUsers = 20, MaxProjects = 20, IsActive = true,
        };
        var project = new Project
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "Federated Site",
            Code = $"FED-{Guid.NewGuid():N}"[..12], Phase = "Design", Status = ProjectStatus.Active,
        };
        db.Tenants.Add(tenant);
        db.Projects.Add(project);

        var arch = Guid.NewGuid();
        var str = Guid.NewGuid();
        foreach (var (id, name, disc) in new[] { (arch, "ARCH (ArchiCAD)", "A"), (str, "STRUCT (Tekla)", "S") })
        {
            db.ProjectModels.Add(new ProjectModel
            {
                Id = id, TenantId = tenant.Id, ProjectId = project.Id,
                Name = name, Discipline = disc, FileName = $"{name}.glb",
                StoragePath = $"t_x/{id:N}.glb", Units = "m",
            });
            // One chunk per model, both a 30 m cube about their own origin.
            db.SceneNodes.Add(new SceneNode
            {
                Id = Guid.NewGuid(), TenantId = tenant.Id, ProjectId = project.Id,
                SourceModelId = id, Discipline = disc,
                StoragePath = $"t_x/{id:N}-chunk.glb", ContentHash = Guid.NewGuid().ToString("N"),
                MinX = 0, MinY = 0, MinZ = 0, MaxX = 30_000, MaxY = 30_000, MaxZ = 30_000,
                BaseMinX = 0, BaseMinY = 0, BaseMinZ = 0,
                BaseMaxX = 30_000, BaseMaxY = 30_000, BaseMaxZ = 30_000,
            });
        }

        if (declareProjectCrs)
        {
            db.ProjectCoordinateSystems.Add(new ProjectCoordinateSystem
            {
                TenantId = tenant.Id, ProjectId = project.Id,
                CrsEpsgCode = Crs, OriginEasting = ArchEast, OriginNorthing = ArchNorth,
                LengthUnit = "mm",
            });
        }

        await db.SaveChangesAsync();
        return new Site(tenant.Id, project.Id, arch, str);
    }

    // ── the payoff ──────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Two_models_of_one_site_are_placed_automatically_and_overlay()
    {
        Skip.If(SkipReason is not null, SkipReason!);
        _ = SchemaReady.Value;

        var tenantId = Guid.NewGuid();
        await using var db = NewContext(tenantId);
        await using var tx = await db.Database.BeginTransactionAsync();

        var site = await SeedAsync(db, tenantId, declareProjectCrs: true);
        var writer = NewWriter(db);

        var archWrite = await writer.WriteAsync(site.Project, site.ArchModel, site.Tenant,
            Georef(ArchEast, ArchNorth, "ifc-map-conversion"), verdict: "PASS");
        var structWrite = await writer.WriteAsync(site.Project, site.StructModel, site.Tenant,
            Georef(StructEast, StructNorth, "ifc-map-conversion"), verdict: "PASS");

        // 1. Both were trusted enough to place without a coordinator.
        Assert.Equal(TransformConfidence.High, archWrite.Confidence);
        Assert.Equal(TransformConfidence.High, structWrite.Confidence);

        var rows = await db.ProjectModelTransforms
            .Where(t => t.ProjectId == site.Project).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.AppliedAutomatically,
            "a HIGH-confidence transform was stored but not marked applied — the model would not move"));
        Assert.All(rows, r => Assert.False(r.IsConfirmed,
            "'confirmed' is reserved for a human decision"));

        // 2. Their true relative offset survived: STRUCT is 60 m east and 45 m
        //    north of ARCH on the ground.
        var a = rows.Single(r => r.ProjectModelId == site.ArchModel);
        var s = rows.Single(r => r.ProjectModelId == site.StructModel);
        Assert.Equal(60_000.0, s.TranslationX - a.TranslationX, 3);
        Assert.Equal(45_000.0, s.TranslationY - a.TranslationY, 3);

        // 3. A column both models contain lands in ONE place. This is the
        //    definition of "the models overlay", derived from survey data.
        const double columnE = 432_030.0, columnN = 315_020.0;
        var inArch = ModelTransformMath.ApplyMm(
            a.TranslationX, a.TranslationY, a.TranslationZ, a.RotationDeg, a.ScaleFactor,
            (columnE - ArchEast) * 1000.0, (columnN - ArchNorth) * 1000.0, 0);
        var inStruct = ModelTransformMath.ApplyMm(
            s.TranslationX, s.TranslationY, s.TranslationZ, s.RotationDeg, s.ScaleFactor,
            (columnE - StructEast) * 1000.0, (columnN - StructNorth) * 1000.0, 0);

        Assert.Equal(inArch.X, inStruct.X, 3);
        Assert.Equal(inArch.Y, inStruct.Y, 3);
        Assert.Equal(inArch.Z, inStruct.Z, 3);

        // 4. The manifest AABBs describe where the chunks now ARE. Both cubes
        //    are 30 m and their origins are 60 m / 45 m apart, so they overlap
        //    in Y (45 < 30 is false → no overlap in Y? no: 45 > 30) — assert the
        //    arithmetic explicitly rather than eyeballing it.
        var nodes = await db.SceneNodes.Where(n => n.ProjectId == site.Project).ToListAsync();
        var archNode = nodes.Single(n => n.SourceModelId == site.ArchModel);
        var structNode = nodes.Single(n => n.SourceModelId == site.StructModel);

        Assert.Equal(a.TranslationX, archNode.MinX, 3);
        Assert.Equal(a.TranslationX + 30_000.0, archNode.MaxX, 3);
        Assert.Equal(s.TranslationX, structNode.MinX, 3);

        // Separation is 60 m in X against a 30 m footprint → they must NOT
        // overlap in X. A test that only asserted "the boxes moved" would pass
        // with both models stacked on top of each other.
        Assert.True(structNode.MinX > archNode.MaxX,
            $"the two chunks overlap in X ({structNode.MinX} <= {archNode.MaxX}) — they were placed on top of each other");

        await tx.RollbackAsync();
    }

    [SkippableFact]
    public async Task Relative_placement_does_not_depend_on_whether_a_project_frame_is_declared()
    {
        // The frame only shifts the whole federation; it must not change where
        // the models sit relative to one another. If it did, declaring a
        // benchmark mid-project would silently move buildings apart.
        Skip.If(SkipReason is not null, SkipReason!);
        _ = SchemaReady.Value;

        var offsets = new List<(double dx, double dy)>();

        foreach (var declare in new[] { true, false })
        {
            var tenantId = Guid.NewGuid();
            await using var db = NewContext(tenantId);
            await using var tx = await db.Database.BeginTransactionAsync();

            var site = await SeedAsync(db, tenantId, declareProjectCrs: declare);
            var writer = NewWriter(db);

            await writer.WriteAsync(site.Project, site.ArchModel, site.Tenant,
                Georef(ArchEast, ArchNorth, "ifc-map-conversion"), "PASS");
            await writer.WriteAsync(site.Project, site.StructModel, site.Tenant,
                Georef(StructEast, StructNorth, "ifc-map-conversion"), "PASS");

            var rows = await db.ProjectModelTransforms
                .Where(t => t.ProjectId == site.Project).ToListAsync();
            var a = rows.Single(r => r.ProjectModelId == site.ArchModel);
            var s = rows.Single(r => r.ProjectModelId == site.StructModel);
            offsets.Add((s.TranslationX - a.TranslationX, s.TranslationY - a.TranslationY));

            await tx.RollbackAsync();
        }

        Assert.Equal(offsets[0].dx, offsets[1].dx, 3);
        Assert.Equal(offsets[0].dy, offsets[1].dy, 3);
        Assert.Equal(60_000.0, offsets[0].dx, 3);
    }

    [SkippableFact]
    public async Task A_model_without_georeferencing_stays_at_the_origin()
    {
        // The other half of the contract, on the production engine: an
        // ungeoreferenced model gets NO transform row at all rather than a
        // guessed one. Visibly un-placed beats convincingly mis-placed.
        Skip.If(SkipReason is not null, SkipReason!);
        _ = SchemaReady.Value;

        var tenantId = Guid.NewGuid();
        await using var db = NewContext(tenantId);
        await using var tx = await db.Database.BeginTransactionAsync();

        var site = await SeedAsync(db, tenantId, declareProjectCrs: true);

        var result = await NewWriter(db).WriteAsync(
            site.Project, site.StructModel, site.Tenant,
            new ModelGeoref(null, null, null, 0, null, false, "m", "ifc-map-conversion"),
            verdict: "PASS");

        Assert.Equal(TransformConfidence.None, result.Confidence);
        Assert.False(result.Written);
        Assert.False(await db.ProjectModelTransforms.AnyAsync(t => t.ProjectModelId == site.StructModel));

        await tx.RollbackAsync();
    }

    [SkippableFact]
    public async Task A_coordinators_confirmed_transform_survives_an_automatic_pass()
    {
        Skip.If(SkipReason is not null, SkipReason!);
        _ = SchemaReady.Value;

        var tenantId = Guid.NewGuid();
        await using var db = NewContext(tenantId);
        await using var tx = await db.Database.BeginTransactionAsync();

        var site = await SeedAsync(db, tenantId, declareProjectCrs: true);
        db.ProjectModelTransforms.Add(new ProjectModelTransform
        {
            TenantId = site.Tenant, ProjectId = site.Project, ProjectModelId = site.StructModel,
            TranslationX = 1, TranslationY = 2, TranslationZ = 3,
            IsConfirmed = true, AppliedBy = "coordinator@example.com", Source = "manual",
        });
        await db.SaveChangesAsync();

        var result = await NewWriter(db).WriteAsync(
            site.Project, site.StructModel, site.Tenant,
            Georef(StructEast, StructNorth, "ifc-map-conversion"), "PASS");

        Assert.False(result.Written);
        // …but the caller is still told what the survey data implies.
        Assert.NotEqual(0.0, result.TranslationXMm);

        var row = await db.ProjectModelTransforms.SingleAsync(t => t.ProjectModelId == site.StructModel);
        Assert.Equal(1, row.TranslationX);
        Assert.True(row.IsConfirmed);

        await tx.RollbackAsync();
    }
}
