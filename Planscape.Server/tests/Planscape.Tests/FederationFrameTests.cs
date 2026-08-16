using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Planscape.Core.Coordinates;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.Tests;

/// <summary>
/// TRACK B / P5 — the translation convention, and the property that actually
/// matters: two models of the same site must end up in the right place RELATIVE
/// TO EACH OTHER.
///
/// THE DEFECT
/// ----------
/// Both automatic writers computed the NEGATION of the correct translation. The
/// IFC ingest path used <c>t = -modelOrigin</c>; AutoAlignService used
/// <c>t = referenceOrigin - modelOrigin</c>.
///
/// A model's geometry is authored about its own internal origin, and its
/// georeferencing says where that origin sits in the survey CRS. So a physical
/// point S sits at local coordinate <c>S - A</c> in model A. The transform maps
/// local to world as <c>world = t + local</c>:
///
///   with t = +A :  world = A + (S - A) = S          ✓ both models agree on S
///   with t = -A :  world = -A + (S - A) = S - 2A    ✗ and B lands at S - 2B
///
/// So two models came out MIRRORED about the origin — an east-west pair swaps
/// sides. The existing overlay proof (ModelTransformMathTests) could not catch
/// it: it inverts a transform and re-applies the SAME transform, which proves
/// <c>Apply(Inverse(w)) == w</c> for any transform whatsoever. It proves the
/// math is self-consistent, never that the transform was DERIVED correctly from
/// survey data. These tests derive it.
/// </summary>
public class FederationFrameTests
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

    private sealed record World(SqliteConnection Conn, Guid Tenant, Guid Project, Guid ModelA, Guid ModelB);

    private static World NewWorld(
        double? frameEasting = null, double? frameNorthing = null, Guid? referenceModelId = null)
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var tenant = Guid.NewGuid();
        var project = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

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
                Id = project, TenantId = tenant, Name = "Campus",
                Code = $"CM-{Guid.NewGuid():N}"[..8], Status = ProjectStatus.Active,
            });
            foreach (var (id, name) in new[] { (a, "Block A"), (b, "Block B") })
                ctx.ProjectModels.Add(new ProjectModel
                {
                    Id = id, TenantId = tenant, ProjectId = project,
                    Name = name, FileName = $"{name}.glb", StoragePath = $"t_x/{id:N}.glb",
                    Units = "m",
                });

            if (frameEasting.HasValue || referenceModelId.HasValue)
            {
                ctx.ProjectCoordinateSystems.Add(new ProjectCoordinateSystem
                {
                    TenantId = tenant, ProjectId = project, CrsEpsgCode = "EPSG:27700",
                    OriginEasting = frameEasting, OriginNorthing = frameNorthing,
                    ReferenceModelId = referenceModelId,
                });
            }
            ctx.SaveChanges();
        }

        return new World(conn, tenant, project, a, b);
    }

    private static ModelGeorefWriter NewWriter(World w)
    {
        var db = NewContext(w.Conn, w.Tenant);
        return new ModelGeorefWriter(
            db,
            new SceneNodeAabbRefresher(db, NullLogger<SceneNodeAabbRefresher>.Instance),
            NullLogger<ModelGeorefWriter>.Instance);
    }

    private static ModelGeoref At(double eastingM, double northingM, double elevationM = 0, double northDeg = 0)
        => new(eastingM, northingM, elevationM, northDeg, "EPSG:27700", true, "m", "ifc-map-conversion");

    // Two blocks 500 m apart: A east/north of the origin, B 500 m further on.
    private const double AEast = 432_000.0, ANorth = 315_000.0;
    private const double BEast = 432_500.0, BNorth = 315_400.0;

    private static async Task<(ProjectModelTransform a, ProjectModelTransform b)> PlaceBoth(World w)
    {
        await NewWriter(w).WriteAsync(w.Project, w.ModelA, w.Tenant, At(AEast, ANorth), "PASS");
        await NewWriter(w).WriteAsync(w.Project, w.ModelB, w.Tenant, At(BEast, BNorth), "PASS");

        using var check = NewContext(w.Conn, w.Tenant);
        var rows = await check.ProjectModelTransforms.AsNoTracking().ToListAsync();
        return (rows.Single(r => r.ProjectModelId == w.ModelA),
                rows.Single(r => r.ProjectModelId == w.ModelB));
    }

    // ── the property that matters ───────────────────────────────────────────

    [Fact]
    public async Task Two_models_keep_their_true_relative_offset()
    {
        // B is 500 m EAST and 400 m NORTH of A on the ground. Whatever frame is
        // chosen, that must survive into world space — same sign, same magnitude.
        var w = NewWorld();
        using (w.Conn)
        {
            var (a, b) = await PlaceBoth(w);

            Assert.Equal((BEast - AEast) * 1000.0, b.TranslationX - a.TranslationX, 3);
            Assert.Equal((BNorth - ANorth) * 1000.0, b.TranslationY - a.TranslationY, 3);
        }
    }

    [Fact]
    public async Task The_offset_is_not_mirrored()
    {
        // The specific regression. Under the old t = -origin convention the
        // difference came out NEGATED: B ended up west and south of A. This
        // asserts the sign explicitly, because a magnitude-only check passes
        // against the bug.
        var w = NewWorld();
        using (w.Conn)
        {
            var (a, b) = await PlaceBoth(w);

            Assert.True(b.TranslationX > a.TranslationX,
                "B is east of A on the ground but landed west of it — the translation is mirrored");
            Assert.True(b.TranslationY > a.TranslationY,
                "B is north of A on the ground but landed south of it — the translation is mirrored");
        }
    }

    [Fact]
    public async Task A_point_shared_by_two_models_maps_to_one_world_coordinate()
    {
        // The real definition of "the models overlay", derived from survey data
        // rather than assumed. A pillar at survey (432,200, 315,150) sits at a
        // different LOCAL coordinate in each model; both must render it in the
        // same place.
        var w = NewWorld();
        using (w.Conn)
        {
            var (a, b) = await PlaceBoth(w);

            const double pillarE = 432_200.0, pillarN = 315_150.0;
            // Local coordinate of the pillar in each model, in mm.
            var localA = ((pillarE - AEast) * 1000.0, (pillarN - ANorth) * 1000.0);
            var localB = ((pillarE - BEast) * 1000.0, (pillarN - BNorth) * 1000.0);

            var wa = ModelTransformMath.ApplyMm(a.TranslationX, a.TranslationY, a.TranslationZ,
                                                a.RotationDeg, a.ScaleFactor, localA.Item1, localA.Item2, 0);
            var wb = ModelTransformMath.ApplyMm(b.TranslationX, b.TranslationY, b.TranslationZ,
                                                b.RotationDeg, b.ScaleFactor, localB.Item1, localB.Item2, 0);

            Assert.Equal(wa.X, wb.X, 3);
            Assert.Equal(wa.Y, wb.Y, 3);
            Assert.Equal(wa.Z, wb.Z, 3);
        }
    }

    // ── the frame ───────────────────────────────────────────────────────────

    [Fact]
    public async Task With_no_declared_frame_models_sit_at_their_raw_crs_position()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            var (a, _) = await PlaceBoth(w);
            Assert.Equal(AEast * 1000.0, a.TranslationX, 3);
        }
    }

    [Fact]
    public async Task A_declared_benchmark_becomes_the_frame_origin()
    {
        // Subtracting a site-local benchmark keeps world coordinates small.
        // A site at easting 432 km rendered about a zero origin puts geometry
        // where 32-bit float stops resolving millimetres.
        var w = NewWorld(frameEasting: 432_000.0, frameNorthing: 315_000.0);
        using (w.Conn)
        {
            var (a, b) = await PlaceBoth(w);

            Assert.Equal(0.0, a.TranslationX, 3);          // A IS the benchmark
            Assert.Equal(500_000.0, b.TranslationX, 3);    // B: 500 m east, in mm

            // …and the relative offset is untouched by the choice of frame.
            Assert.Equal((BEast - AEast) * 1000.0, b.TranslationX - a.TranslationX, 3);
        }
    }

    [Fact]
    public async Task A_nominated_reference_model_becomes_the_frame_origin()
    {
        var w = NewWorld(referenceModelId: null);
        using (w.Conn)
        {
            // Nominate A as the reference, and give it an alignment report so
            // the frame can be resolved from it.
            using (var seed = NewContext(w.Conn, w.Tenant))
            {
                seed.IfcAlignmentReports.Add(new IfcAlignmentReport
                {
                    TenantId = w.Tenant, ProjectId = w.Project, ProjectModelId = w.ModelA,
                    HasMapConversion = true, HasProjectedCrs = true, CrsName = "EPSG:27700",
                    SurveyEasting = AEast, SurveyNorthing = ANorth, SurveyElevation = 0,
                    MapConversionRotationDeg = 0, Verdict = "PASS", ValidatedAt = DateTime.UtcNow,
                });
                seed.ProjectCoordinateSystems.Add(new ProjectCoordinateSystem
                {
                    TenantId = w.Tenant, ProjectId = w.Project, CrsEpsgCode = "EPSG:27700",
                    ReferenceModelId = w.ModelA,
                });
                await seed.SaveChangesAsync();
            }

            await NewWriter(w).WriteAsync(w.Project, w.ModelB, w.Tenant, At(BEast, BNorth), "PASS");

            using var check = NewContext(w.Conn, w.Tenant);
            var b = await check.ProjectModelTransforms.AsNoTracking()
                .SingleAsync(t => t.ProjectModelId == w.ModelB);

            // B expressed relative to A: 500 m east, 400 m north.
            Assert.Equal(500_000.0, b.TranslationX, 3);
            Assert.Equal(400_000.0, b.TranslationY, 3);
        }
    }

    [Fact]
    public async Task Rotation_is_expressed_relative_to_the_frame()
    {
        // A project whose declared coordinate system is itself rotated off grid
        // north must not have that rotation applied twice.
        var w = NewWorld(frameEasting: 432_000.0, frameNorthing: 315_000.0);
        using (w.Conn)
        {
            using (var seed = NewContext(w.Conn, w.Tenant))
            {
                var pcs = await seed.ProjectCoordinateSystems.SingleAsync();
                pcs.TrueNorthDeg = 10.0;
                await seed.SaveChangesAsync();
            }

            await NewWriter(w).WriteAsync(w.Project, w.ModelA, w.Tenant,
                At(AEast, ANorth, northDeg: 25.0), "PASS");

            using var check = NewContext(w.Conn, w.Tenant);
            var a = await check.ProjectModelTransforms.AsNoTracking().SingleAsync();
            Assert.Equal(15.0, a.RotationDeg, 6);   // 25 model − 10 frame
        }
    }
}
