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
/// TRACK B / P2 — the shared georef → transform writer.
///
/// WHY THIS CLASS EXISTS AT ALL
/// ----------------------------
/// Three pipelines produce automatic transforms: IFC ingest, the Revit GLB
/// upload, and AutoAlignService. Before P2 the Revit path had NO georef source
/// (its GLB is exported about the project internal origin, so every Revit model
/// landed at 0,0,0 wherever the building actually was), and the IFC path carried
/// its own private copy of the translation convention. Adding a second copy for
/// Revit is how a building ends up in a different place depending on which
/// pipeline last touched it — the most expensive class of bug here and the
/// hardest to attribute. So there is one writer, and these tests pin its
/// contract.
///
/// THE CONVENTION UNDER TEST
/// -------------------------
/// Each model is moved from its own survey origin back to the project origin:
/// t = -origin, metres → mm. This is what the IFC ingest path has always done;
/// preserving it exactly is what makes introducing the shared writer a no-op for
/// existing IFC uploads.
/// </summary>
public class ModelGeorefWriterTests
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

    private sealed record World(SqliteConnection Conn, Guid Tenant, Guid Project, Guid Model);

    private static World NewWorld(string? projectCrs = null)
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var tenant = Guid.NewGuid();
        var project = Guid.NewGuid();
        var model = Guid.NewGuid();

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
                Name = "ARCH", FileName = "arch.glb", StoragePath = "t_x/arch.glb",
            });
            if (projectCrs != null)
            {
                ctx.ProjectCoordinateSystems.Add(new ProjectCoordinateSystem
                {
                    TenantId = tenant, ProjectId = project, CrsEpsgCode = projectCrs,
                });
            }
            ctx.SaveChanges();
        }

        return new World(conn, tenant, project, model);
    }

    private static ModelGeorefWriter NewWriter(World w)
    {
        // The refresher shares the writer's context so both see one change
        // tracker — the writer saves the transform, the refresher then reads it
        // back to recompute chunk bounds.
        var db = NewContext(w.Conn, w.Tenant);
        return new ModelGeorefWriter(
            db,
            new SceneNodeAabbRefresher(db, NullLogger<SceneNodeAabbRefresher>.Instance),
            NullLogger<ModelGeorefWriter>.Instance);
    }

    /// <summary>A Revit publish from a site at BNG easting 432,000 m.</summary>
    private static ModelGeoref RevitGeoref(string? crs = "EPSG:27700") => new(
        EastingM      : 432_000.0,
        NorthingM     : 315_000.0,
        ElevationM    : 12.5,
        TrueNorthDeg  : 3.25,
        CrsCode       : crs,
        HasDeclaredCrs: crs != null,
        LengthUnit    : "mm",
        SourceLabel   : "revit-georef");

    // ── the placement itself ────────────────────────────────────────────────

    [Fact]
    public async Task A_survey_origin_becomes_a_millimetre_translation_into_the_project_frame()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            var write = await NewWriter(w).WriteAsync(
                w.Project, w.Model, w.Tenant, RevitGeoref(), verdict: "PASS");

            Assert.Equal(TransformConfidence.High, write.Confidence);

            using var check = NewContext(w.Conn, w.Tenant);
            var xf = await check.ProjectModelTransforms.SingleAsync();

            // P5 — metres → mm, NOT negated. The model's geometry is authored
            // about its own internal origin and this transform says where that
            // origin sits, so a physical point shared with another model lands
            // on the same world coordinate. The earlier negation put models
            // mirrored about the origin; see FederationFrameTests.
            // No ProjectCoordinateSystem here, so the frame origin is zero and
            // the model sits at its raw CRS position.
            Assert.Equal(432_000_000.0, xf.TranslationX, 3);
            Assert.Equal(315_000_000.0, xf.TranslationY, 3);
            Assert.Equal(12_500.0, xf.TranslationZ, 3);
            Assert.Equal(3.25, xf.RotationDeg, 6);
            Assert.Equal(1.0, xf.ScaleFactor, 6);
            Assert.Equal("revit-georef", xf.Source);
            Assert.True(xf.IsAutoComputed);
        }
    }

    [Fact]
    public async Task A_declared_crs_makes_the_transform_auto_applied()
    {
        // This is what turns "the numbers are stored" into "the model moves".
        var w = NewWorld();
        using (w.Conn)
        {
            await NewWriter(w).WriteAsync(w.Project, w.Model, w.Tenant, RevitGeoref(), "PASS");

            using var check = NewContext(w.Conn, w.Tenant);
            var xf = await check.ProjectModelTransforms.SingleAsync();
            Assert.True(xf.AppliedAutomatically);
            Assert.Equal("HIGH", xf.Confidence);
        }
    }

    [Fact]
    public async Task The_projects_own_crs_is_an_acceptable_anchor()
    {
        // Revit has no native CRS concept, so a model may arrive without one.
        // If the PROJECT declares the CRS and the model agrees, that is still a
        // real anchor — but only when the model actually names it.
        var w = NewWorld(projectCrs: "EPSG:27700");
        using (w.Conn)
        {
            var georef = RevitGeoref(crs: "27700") with { HasDeclaredCrs = false };
            var write = await NewWriter(w).WriteAsync(w.Project, w.Model, w.Tenant, georef, "PASS");

            Assert.Equal(TransformConfidence.High, write.Confidence);
        }
    }

    [Fact]
    public async Task Without_any_crs_anchor_the_transform_is_stored_but_not_applied()
    {
        // The honest outcome for a Revit model published from a project where
        // nobody has declared the coordinate system: we have coordinates but
        // nothing says which system they are in.
        var w = NewWorld();
        using (w.Conn)
        {
            var write = await NewWriter(w).WriteAsync(
                w.Project, w.Model, w.Tenant, RevitGeoref(crs: null), "PASS");

            Assert.Equal(TransformConfidence.Low, write.Confidence);

            using var check = NewContext(w.Conn, w.Tenant);
            var xf = await check.ProjectModelTransforms.SingleAsync();
            Assert.Equal(432_000_000.0, xf.TranslationX, 3);    // computed
            Assert.False(xf.AppliedAutomatically);              // but not live
            Assert.Equal("LOW", xf.Confidence);
        }
    }

    // ── the "do nothing" cases, which matter as much as the placement ───────

    [Fact]
    public async Task No_survey_origin_writes_no_transform_at_all()
    {
        // Deliberately not "write an identity transform" and emphatically not
        // "guess". An un-placed model sitting at the origin is visibly
        // un-placed; a guessed one looks placed and costs an investigation.
        var w = NewWorld();
        using (w.Conn)
        {
            var georef = RevitGeoref() with { EastingM = null, NorthingM = null };
            var write = await NewWriter(w).WriteAsync(w.Project, w.Model, w.Tenant, georef, "PASS");

            Assert.Equal(TransformConfidence.None, write.Confidence);

            using var check = NewContext(w.Conn, w.Tenant);
            Assert.False(await check.ProjectModelTransforms.AnyAsync());
        }
    }

    [Fact]
    public async Task A_confirmed_transform_is_never_overwritten()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            using (var seed = NewContext(w.Conn, w.Tenant))
            {
                seed.ProjectModelTransforms.Add(new ProjectModelTransform
                {
                    TenantId = w.Tenant, ProjectId = w.Project, ProjectModelId = w.Model,
                    TranslationX = 11, TranslationY = 22, TranslationZ = 33,
                    IsConfirmed = true, AppliedBy = "coordinator@example.com",
                });
                await seed.SaveChangesAsync();
            }

            await NewWriter(w).WriteAsync(w.Project, w.Model, w.Tenant, RevitGeoref(), "PASS");

            using var check = NewContext(w.Conn, w.Tenant);
            var xf = await check.ProjectModelTransforms.SingleAsync();
            Assert.Equal(11, xf.TranslationX);
            Assert.Equal(22, xf.TranslationY);
            Assert.True(xf.IsConfirmed);
            Assert.Equal("coordinator@example.com", xf.AppliedBy);
        }
    }

    [Fact]
    public async Task A_confirmed_refusal_says_so_and_names_who_confirmed_it()
    {
        // The result has to distinguish the two ways a write can be refused, or
        // a caller can only infer the reason from Written=false — and that
        // inference is wrong for the no-survey-origin case. AutoAlignService is
        // the caller that has to tell a human WHY, and this is what lets it do
        // that without re-reading the row and re-testing IsConfirmed itself.
        var confirmedAt = new DateTime(2026, 3, 4, 9, 30, 0, DateTimeKind.Utc);
        var w = NewWorld();
        using (w.Conn)
        {
            using (var seed = NewContext(w.Conn, w.Tenant))
            {
                seed.ProjectModelTransforms.Add(new ProjectModelTransform
                {
                    TenantId = w.Tenant, ProjectId = w.Project, ProjectModelId = w.Model,
                    IsConfirmed = true,
                    AppliedBy = "coordinator@example.com", AppliedAt = confirmedAt,
                });
                await seed.SaveChangesAsync();
            }

            var write = await NewWriter(w).WriteAsync(
                w.Project, w.Model, w.Tenant, RevitGeoref(), "PASS");

            Assert.False(write.Written);
            Assert.True(write.RefusedAsConfirmed);
            Assert.Equal("coordinator@example.com", write.ConfirmedBy);
            Assert.Equal(confirmedAt, write.ConfirmedAt);
            // Still reports what it WOULD have written, so the refusal is useful.
            Assert.Equal(432_000_000.0, write.TranslationXMm, 3);
        }
    }

    [Fact]
    public async Task A_refusal_for_want_of_a_survey_origin_is_not_a_confirmed_refusal()
    {
        // The distinction that makes `!Written` an unsafe proxy for "confirmed":
        // both refusals share Written=false, and reporting this one as "manually
        // confirmed by a coordinator" would be a confident wrong answer.
        var w = NewWorld();
        using (w.Conn)
        {
            var georef = RevitGeoref() with { EastingM = null, NorthingM = null };
            var write = await NewWriter(w).WriteAsync(w.Project, w.Model, w.Tenant, georef, "PASS");

            Assert.False(write.Written);
            Assert.False(write.RefusedAsConfirmed);
            Assert.Null(write.ConfirmedBy);
        }
    }

    [Fact]
    public async Task Re_publishing_updates_the_existing_transform_rather_than_duplicating_it()
    {
        // ProjectModelTransform has a UNIQUE index on ProjectModelId — a second
        // INSERT would throw, so an upsert is required, not merely tidy.
        var w = NewWorld();
        using (w.Conn)
        {
            await NewWriter(w).WriteAsync(w.Project, w.Model, w.Tenant, RevitGeoref(), "PASS");

            var moved = RevitGeoref() with { EastingM = 500_000.0 };
            await NewWriter(w).WriteAsync(w.Project, w.Model, w.Tenant, moved, "PASS");

            using var check = NewContext(w.Conn, w.Tenant);
            var xf = await check.ProjectModelTransforms.SingleAsync();   // exactly one
            Assert.Equal(500_000_000.0, xf.TranslationX, 3);
            Assert.NotNull(xf.UpdatedAt);
        }
    }

    [Fact]
    public async Task A_failed_alignment_verdict_is_stored_but_not_applied()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            var write = await NewWriter(w).WriteAsync(
                w.Project, w.Model, w.Tenant, RevitGeoref(), verdict: "FAIL");

            Assert.Equal(TransformConfidence.Low, write.Confidence);

            using var check = NewContext(w.Conn, w.Tenant);
            Assert.False((await check.ProjectModelTransforms.SingleAsync()).AppliedAutomatically);
        }
    }

    // ── the IFC path must not have changed ──────────────────────────────────

    [Fact]
    public async Task The_ifc_source_label_produces_the_same_numbers_as_the_revit_one()
    {
        // The whole point of the shared writer: two hosts reporting the same
        // survey origin must land in the same place. If this ever diverges,
        // models from different tools stop lining up — silently.
        var w1 = NewWorld();
        var w2 = NewWorld();
        using (w1.Conn)
        using (w2.Conn)
        {
            var ifc = RevitGeoref() with { SourceLabel = "ifc-map-conversion" };

            await NewWriter(w1).WriteAsync(w1.Project, w1.Model, w1.Tenant, RevitGeoref(), "PASS");
            await NewWriter(w2).WriteAsync(w2.Project, w2.Model, w2.Tenant, ifc, "PASS");

            using var c1 = NewContext(w1.Conn, w1.Tenant);
            using var c2 = NewContext(w2.Conn, w2.Tenant);
            var a = await c1.ProjectModelTransforms.SingleAsync();
            var b = await c2.ProjectModelTransforms.SingleAsync();

            Assert.Equal(a.TranslationX, b.TranslationX, 6);
            Assert.Equal(a.TranslationY, b.TranslationY, 6);
            Assert.Equal(a.TranslationZ, b.TranslationZ, 6);
            Assert.Equal(a.RotationDeg, b.RotationDeg, 6);
            Assert.Equal(a.AppliedAutomatically, b.AppliedAutomatically);
            // Only the provenance label differs.
            Assert.Equal("revit-georef", a.Source);
            Assert.Equal("ifc-map-conversion", b.Source);
        }
    }
}
