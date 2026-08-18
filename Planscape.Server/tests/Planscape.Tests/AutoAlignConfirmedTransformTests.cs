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
/// TRACK A3 — auto-align silently destroyed a manually-confirmed alignment.
///
/// THE DEFECT
/// ----------
/// <c>AutoAlignService.ComputeAsync</c> persisted its result with a bare
/// "overwrite if exists": it loaded any existing ProjectModelTransform and
/// stamped the computed values over it, ignoring <c>IsConfirmed</c>. The IFC
/// ingest path has always respected the flag ("Only auto-update if not manually
/// confirmed by a coordinator") — this path did not. A coordinator who had
/// aligned a model by hand, against survey data that was wrong or absent, lost
/// that work the moment anyone ran auto-align, and the only signal was the model
/// jumping in the viewer.
///
/// WHY IsConfirmed IS THE RIGHT FLAG
/// ---------------------------------
/// It is set only by a human path: ModelTransformController.Upsert takes it from
/// the request body and forces IsAutoComputed=false alongside. Both automatic
/// writers (ingest, auto-align) store IsConfirmed=false. So "confirmed" means
/// exactly "a coordinator asserted this", which is the thing an automatic
/// process must not overrule.
/// </summary>
public class AutoAlignConfirmedTransformTests
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

    private sealed record World(SqliteConnection Conn, Guid Tenant, Guid Project, Guid Target, Guid Reference);

    /// <summary>
    /// Georeferencing quality of the TARGET model's report, which is what
    /// <see cref="TransformConfidencePolicy"/> grades.
    /// </summary>
    public enum Georef   // public: xunit [InlineData] carries it into a public test signature
    {
        /// <summary>IfcProjectedCRS present + PASS → HIGH → auto-applied.</summary>
        Strong,
        /// <summary>Survey origin but no CRS anchor → LOW → suggestion only.</summary>
        Unanchored,
        /// <summary>Anchored, but the validator FAILed → LOW → suggestion only.</summary>
        Failed,
    }

    /// <summary>
    /// A project with two georeferenced models: a reference at survey origin
    /// (1000, 2000) and a target at (1500, 2500). Auto-align therefore has real
    /// work to do — a fixture where it fails for lack of data would make the
    /// "did not overwrite" assertion pass for the wrong reason.
    /// </summary>
    private static World NewWorld(bool targetTransformConfirmed, Georef georef = Georef.Strong)
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var tenant = Guid.NewGuid();
        var project = Guid.NewGuid();
        var target = Guid.NewGuid();
        var reference = Guid.NewGuid();

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

            // Both models must exist: ProjectModelTransform and
            // IfcAlignmentReport carry real FKs to ProjectModel, and SQLite
            // (unlike EF InMemory) enforces them.
            foreach (var (id, name) in new[] { (reference, "Reference ARCH"), (target, "Target MEP") })
                ctx.ProjectModels.Add(new ProjectModel
                {
                    Id = id, TenantId = tenant, ProjectId = project,
                    Name = name, FileName = $"{name}.glb", StoragePath = $"t_x/{id:N}.glb",
                });

            ctx.IfcAlignmentReports.Add(new IfcAlignmentReport
            {
                TenantId = tenant, ProjectId = project, ProjectModelId = reference,
                HasMapConversion = true,
                SurveyEasting = 1000, SurveyNorthing = 2000, SurveyElevation = 0,
                MapConversionRotationDeg = 0,
                Verdict = "PASS", ValidatedAt = DateTime.UtcNow.AddMinutes(-5),
            });
            ctx.IfcAlignmentReports.Add(new IfcAlignmentReport
            {
                TenantId = tenant, ProjectId = project, ProjectModelId = target,
                HasMapConversion = true,
                // Only the STRONG case declares a projected CRS; the other two
                // are what a real-world file without one looks like.
                HasProjectedCrs = georef == Georef.Strong,
                CrsName = georef == Georef.Strong ? "EPSG:27700" : null,
                SurveyEasting = 1500, SurveyNorthing = 2500, SurveyElevation = 0,
                MapConversionRotationDeg = 0,
                Verdict = georef == Georef.Failed ? "FAIL" : "PASS",
                ValidatedAt = DateTime.UtcNow,
            });

            ctx.Set<ProjectModelTransform>().Add(new ProjectModelTransform
            {
                TenantId = tenant, ProjectId = project, ProjectModelId = target,
                // A hand-set alignment that the survey data does NOT agree with.
                TranslationX = 42.0, TranslationY = 43.0, TranslationZ = 44.0,
                RotationDeg = 7.0, ScaleFactor = 2.0,
                IsConfirmed = targetTransformConfirmed,
                IsAutoComputed = !targetTransformConfirmed,
                AppliedBy = targetTransformConfirmed ? "coordinator@example.com" : "auto-align-service",
                AppliedAt = DateTime.UtcNow.AddDays(-1),
                Notes = "site-agreed base point",
            });

            ctx.SaveChanges();
        }

        return new World(conn, tenant, project, target, reference);
    }

    private static AutoAlignService NewService(World w)
    {
        // P5 — auto-align no longer does its own transform arithmetic; it
        // delegates to the shared writer so it cannot drift from the ingest
        // path. One context across all three so they share a change tracker.
        var db = NewContext(w.Conn, w.Tenant);
        var writer = new ModelGeorefWriter(
            db,
            new SceneNodeAabbRefresher(db, NullLogger<SceneNodeAabbRefresher>.Instance),
            NullLogger<ModelGeorefWriter>.Instance);
        return new AutoAlignService(db, writer, NullLogger<AutoAlignService>.Instance);
    }

    [Fact]
    public async Task A_confirmed_transform_is_not_overwritten()
    {
        var w = NewWorld(targetTransformConfirmed: true);
        using (w.Conn)
        {
            var result = await NewService(w).ComputeAsync(w.Project, w.Tenant, w.Target);

            Assert.False(result.Success);
            Assert.Contains("manually confirmed", result.Message ?? "", StringComparison.OrdinalIgnoreCase);

            using var check = NewContext(w.Conn, w.Tenant);
            var xf = await check.Set<ProjectModelTransform>().SingleAsync();
            Assert.Equal(42.0, xf.TranslationX);
            Assert.Equal(43.0, xf.TranslationY);
            Assert.Equal(44.0, xf.TranslationZ);
            Assert.Equal(7.0, xf.RotationDeg);
            Assert.Equal(2.0, xf.ScaleFactor);
            Assert.True(xf.IsConfirmed);
            Assert.Equal("coordinator@example.com", xf.AppliedBy);
        }
    }

    [Fact]
    public async Task The_refusal_still_reports_what_auto_align_would_have_applied()
    {
        // The coordinator asked an explicit question and deserves the answer,
        // so they can compare it with the transform they confirmed. A bare
        // "refused" would make the feature useless at exactly the moment it
        // matters.
        var w = NewWorld(targetTransformConfirmed: true);
        using (w.Conn)
        {
            var result = await NewService(w).ComputeAsync(w.Project, w.Tenant, w.Target);

            // P5 — the target's own survey origin, in the project frame.
            //
            // This used to assert reference(1000) - target(1500) = -500 m. Two
            // things changed. The sign was wrong (see FederationFrameTests), and
            // the frame is no longer "whichever sibling was validated most
            // recently" — that origin moved every time another model was
            // uploaded, so transforms computed on different days disagreed.
            // With no ProjectCoordinateSystem in this fixture the frame is zero,
            // so the target sits at its raw CRS position of 1500 m. Relative
            // placement is unaffected: the same frame is subtracted from every
            // model.
            Assert.Equal(1_500_000.0, result.TranslationX, 3);
            Assert.Equal(2_500_000.0, result.TranslationY, 3);
        }
    }

    [Fact]
    public async Task An_unconfirmed_transform_is_still_overwritten()
    {
        // The mirror case. A guard that refused unconditionally would satisfy
        // the tests above while disabling auto-align entirely.
        var w = NewWorld(targetTransformConfirmed: false);
        using (w.Conn)
        {
            var result = await NewService(w).ComputeAsync(w.Project, w.Tenant, w.Target);

            Assert.True(result.Success, result.Message);

            using var check = NewContext(w.Conn, w.Tenant);
            var xf = await check.Set<ProjectModelTransform>().SingleAsync();
            Assert.Equal(1_500_000.0, xf.TranslationX, 3);
            Assert.Equal(2_500_000.0, xf.TranslationY, 3);
            Assert.True(xf.IsAutoComputed);
            Assert.False(xf.IsConfirmed);
            Assert.Equal("auto-align-service", xf.AppliedBy);
        }
    }

    // ── B1 — does the computed transform actually go LIVE? ───────────────────

    [Fact]
    public async Task A_high_confidence_result_is_marked_auto_applied()
    {
        // The P1 fix. Before it, this row was written with IsConfirmed=false and
        // nothing else, and the viewer's `isConfirmed !== false` gate discarded
        // it — so a correct survey-derived alignment never rendered.
        var w = NewWorld(targetTransformConfirmed: false, georef: Georef.Strong);
        using (w.Conn)
        {
            var result = await NewService(w).ComputeAsync(w.Project, w.Tenant, w.Target);
            Assert.True(result.Success, result.Message);

            using var check = NewContext(w.Conn, w.Tenant);
            var xf = await check.Set<ProjectModelTransform>().SingleAsync();

            Assert.True(xf.AppliedAutomatically);
            Assert.Equal("HIGH", xf.Confidence);
            Assert.Equal("auto-align", xf.Source);
            // Still not "confirmed" — that word is reserved for a human.
            Assert.False(xf.IsConfirmed);
        }
    }

    [Theory]
    [InlineData(Georef.Unanchored)]   // survey origin, but no CRS to anchor it
    [InlineData(Georef.Failed)]       // anchored, but the validator FAILed
    public async Task A_low_confidence_result_is_stored_but_not_auto_applied(Georef georef)
    {
        // The other half of the contract, and the reason this is not just
        // "apply everything": a model whose georeference cannot be trusted stays
        // where it is instead of being flung across the site by a guess.
        var w = NewWorld(targetTransformConfirmed: false, georef: georef);
        using (w.Conn)
        {
            var result = await NewService(w).ComputeAsync(w.Project, w.Tenant, w.Target);
            Assert.True(result.Success, result.Message);

            using var check = NewContext(w.Conn, w.Tenant);
            var xf = await check.Set<ProjectModelTransform>().SingleAsync();

            // Computed and stored — a coordinator can still confirm it.
            Assert.Equal(1_500_000.0, xf.TranslationX, 3);
            // But not live.
            Assert.False(xf.AppliedAutomatically);
            Assert.Equal("LOW", xf.Confidence);
        }
    }
}
