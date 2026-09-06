using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Planscape.API.Controllers;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.Tests;

/// <summary>
/// TRACK C4 — model lifecycle: deletion, retention, and superseding.
///
/// THE DEFECTS
/// -----------
/// • **Soft-delete did not cascade.** `ModelsController.Delete` set `DeletedAt`
///   and nothing else, so the model's scene chunks and federated elements
///   stayed live — the model was "deleted" everywhere except where a user would
///   notice. `GetScene` and the federation manifest filtered on
///   `SceneNode.DeletedAt`, which NOTHING ever set, so a deleted model kept
///   streaming into the viewer.
/// • **The promised purge job did not exist.** `ProjectModel.DeletedAt`'s own
///   doc comment reads "file is purged by a Hangfire job after 30 days". There
///   was no such job. Every soft-deleted model's GLB, element map and thumbnail
///   stayed in object storage indefinitely. A documented retention promise that
///   nothing implements is worse than no promise: it is the reason nobody went
///   looking.
/// • **`Force` was never read.** The flag has been on `UploadModelRequest`,
///   documented, and sent by the plugin's "Publish as a new revision (forced)"
///   mode since it shipped. The dedup branch ran regardless, so the coordinator
///   got a metadata refresh on the OLD row and no new revision — and the
///   operation reported success.
/// </summary>
public class ModelLifecycleTests
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

    /// <summary>Records what it was asked to delete; can refuse, to test deferral.</summary>
    private sealed class RecordingStorage : IFileStorageService
    {
        private readonly HashSet<string> _refuse;
        public RecordingStorage(params string[] refuse) => _refuse = new HashSet<string>(refuse);
        public List<string> Deleted { get; } = new();

        public Task<bool> DeleteAsync(string path, CancellationToken ct = default, bool b = false)
        {
            if (_refuse.Contains(path)) throw new IOException("object store unavailable");
            Deleted.Add(path);
            return Task.FromResult(true);
        }

        private static Exception No() => new NotSupportedException("not used by these tests");
        public Task<string> SaveScopedAsync(Guid t, Guid p, string f, Stream c, CancellationToken ct = default) => throw No();
        public Task<string> SaveAsync(string t, string p, string f, Stream c, CancellationToken ct = default) => throw No();
        public Task<Stream?> GetAsync(string path, CancellationToken ct = default, bool b = false) => throw No();
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default, bool b = false) => throw No();
        public Task<int> DeleteByPrefixAsync(string prefix, CancellationToken ct = default, bool b = false) => throw No();
        public Task<PresignedUpload> GetPresignedPutUrlAsync(string k, string c, TimeSpan v, long m, CancellationToken ct = default) => throw No();
        public Task<string> GetPresignedGetUrlAsync(string k, TimeSpan v, CancellationToken ct = default, bool b = false) => throw No();
        public Task MoveAsync(string s, string d, CancellationToken ct = default, bool b = false) => throw No();
    }

    /// <summary>Neither Delete nor the purge job georeferences anything.</summary>
    private sealed class UnusedGeorefWriter : IModelGeorefWriter
    {
        public Task<GeorefWriteResult> WriteAsync(
            Guid projectId, Guid projectModelId, Guid tenantId,
            ModelGeoref georef, string? verdict, CancellationToken ct = default)
            => throw new NotSupportedException("Delete does not georeference");
    }

    private sealed class UnusedConverter : IConverterClient
    {
        public bool IsConfigured => false;
        public Task<ConverterGlbResult> ConvertIfcToGlbAsync(
            string sourceUrl, string fileName, string? discipline, CancellationToken ct = default)
            => throw new NotSupportedException("Delete does not convert");
    }

    private static ModelsController NewController(PlanscapeDbContext db, Guid tenantId)
    {
        var ctrl = new ModelsController(
            db,
            new RecordingStorage(),
            new UnusedConverter(),
            new UnusedGeorefWriter(),
            NullLogger<ModelsController>.Instance);

        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("tenant_id", tenantId.ToString()),
                    new Claim("user_id", Guid.NewGuid().ToString()),
                }, "test")),
            },
        };
        return ctrl;
    }

    private sealed record World(SqliteConnection Conn, Guid Tenant, Guid Project, Guid Model);

    private const string GlbPath = "t_x/model.glb";
    private const string MapPath = "t_x/model-elements.json";
    private const string ChunkPath = "t_x/chunk.glb";

    private static World NewWorld(DateTime? deletedAt = null)
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
                ContactEmail = "a@e.com", Tier = LicenseTier.Professional,
                Plan = BillingPlan.Studio, MaxUsers = 50, MaxProjects = 50,
            });
            ctx.Projects.Add(new Project
            {
                Id = project, TenantId = tenant, Name = "Tower",
                Code = $"TW-{Guid.NewGuid():N}"[..8], Status = ProjectStatus.Active,
            });
            ctx.ProjectModels.Add(new ProjectModel
            {
                Id = model, TenantId = tenant, ProjectId = project, Name = "ARCH",
                FileName = "a.glb", StoragePath = GlbPath, ElementMapPath = MapPath,
                DeletedAt = deletedAt,
            });
            ctx.SceneNodes.Add(new SceneNode
            {
                Id = Guid.NewGuid(), TenantId = tenant, ProjectId = project, SourceModelId = model,
                Discipline = "A", StoragePath = ChunkPath, ContentHash = "abc",
            });
            ctx.ProjectModelTransforms.Add(new ProjectModelTransform
            {
                TenantId = tenant, ProjectId = project, ProjectModelId = model, TranslationX = 5,
            });
            ctx.SaveChanges();
        }
        return new World(conn, tenant, project, model);
    }

    private static ModelPurgeJob NewJob(PlanscapeDbContext db, IFileStorageService storage)
        => new(db, storage, NullLogger<ModelPurgeJob>.Instance);

    // ── the purge job that never existed ────────────────────────────────────

    [Fact]
    public async Task A_model_deleted_longer_ago_than_the_grace_is_purged_with_its_bytes()
    {
        var w = NewWorld(deletedAt: DateTime.UtcNow.AddDays(-31));
        using (w.Conn)
        {
            var storage = new RecordingStorage();
            using (var db = NewContext(w.Conn, w.Tenant))
                await NewJob(db, storage).ExecuteAsync();

            Assert.Contains(GlbPath, storage.Deleted);
            Assert.Contains(MapPath, storage.Deleted);
            Assert.Contains(ChunkPath, storage.Deleted);

            using var check = NewContext(w.Conn, w.Tenant);
            Assert.False(await check.ProjectModels.IgnoreQueryFilters().AnyAsync(m => m.Id == w.Model));
            Assert.False(await check.SceneNodes.IgnoreQueryFilters().AnyAsync(n => n.SourceModelId == w.Model));
            // The transform positions a model that no longer exists.
            Assert.False(await check.ProjectModelTransforms.IgnoreQueryFilters()
                .AnyAsync(t => t.ProjectModelId == w.Model));
        }
    }

    [Fact]
    public async Task A_recently_deleted_model_is_kept_for_the_grace_period()
    {
        // The mirror case: a purge that ignored the cutoff would satisfy the
        // test above and destroy the audit trail the soft-delete exists for.
        var w = NewWorld(deletedAt: DateTime.UtcNow.AddDays(-2));
        using (w.Conn)
        {
            var storage = new RecordingStorage();
            using (var db = NewContext(w.Conn, w.Tenant))
                await NewJob(db, storage).ExecuteAsync();

            Assert.Empty(storage.Deleted);
            using var check = NewContext(w.Conn, w.Tenant);
            Assert.True(await check.ProjectModels.IgnoreQueryFilters().AnyAsync(m => m.Id == w.Model));
        }
    }

    [Fact]
    public async Task A_live_model_is_never_purged()
    {
        var w = NewWorld(deletedAt: null);
        using (w.Conn)
        {
            var storage = new RecordingStorage();
            using (var db = NewContext(w.Conn, w.Tenant))
                await NewJob(db, storage).ExecuteAsync();

            Assert.Empty(storage.Deleted);
            using var check = NewContext(w.Conn, w.Tenant);
            Assert.True(await check.ProjectModels.AnyAsync(m => m.Id == w.Model));
        }
    }

    [Fact]
    public async Task A_failed_byte_deletion_keeps_the_row_for_the_next_run()
    {
        // Deleting the row while its bytes survive creates an orphan that
        // nothing references any more — unfindable, and therefore unrecoverable
        // storage cost. Deferring is the only safe direction.
        var w = NewWorld(deletedAt: DateTime.UtcNow.AddDays(-31));
        using (w.Conn)
        {
            var storage = new RecordingStorage(refuse: GlbPath);
            using (var db = NewContext(w.Conn, w.Tenant))
                await NewJob(db, storage).ExecuteAsync();

            using var check = NewContext(w.Conn, w.Tenant);
            Assert.True(await check.ProjectModels.IgnoreQueryFilters().AnyAsync(m => m.Id == w.Model),
                "the row was purged even though its bytes could not be deleted — the storage is now orphaned");
        }
    }

    // ── the federated-element cascade, and its false-positive guards ────────
    //
    // ModelsController.Delete used to try to retire a model's federated
    // elements by matching FederatedElement.GlbStoragePath ==
    // ProjectModel.StoragePath. Those are keys from different pipelines — the
    // per-delta GLB key vs the uploaded-GLB key — so it matched nothing while
    // the log reported a count. That join was removed and the gap documented;
    // ProjectModel.SourceDocGuid is the real linkage, and these pin both what
    // it must retire and what it must not touch.

    private const string DocGuid = "doc-guid-arch";

    private static void SeedFederatedElement(
        PlanscapeDbContext ctx, Guid tenant, Guid project, string sourceDocGuid, long elementId)
        => ctx.FederatedElements.Add(new FederatedElement
        {
            TenantId = tenant, ProjectId = project,
            SourceDocGuid = sourceDocGuid, Source = "revit-plugin",
            ElementId = elementId, UniqueId = $"uid-{elementId}",
            GlbStoragePath = $"t_x/{elementId}-delta.glb", IsDeleted = false,
        });

    private static async Task<bool> IsElementDeletedAsync(World w, long elementId)
    {
        using var ctx = NewContext(w.Conn, w.Tenant);
        return await ctx.FederatedElements
            .Where(e => e.ElementId == elementId).Select(e => e.IsDeleted).SingleAsync();
    }

    [Fact]
    public async Task Deleting_a_model_retires_the_federated_elements_its_document_pushed()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            using (var seed = NewContext(w.Conn, w.Tenant))
            {
                (await seed.ProjectModels.SingleAsync(m => m.Id == w.Model)).SourceDocGuid = DocGuid;
                SeedFederatedElement(seed, w.Tenant, w.Project, DocGuid, 101);
                SeedFederatedElement(seed, w.Tenant, w.Project, DocGuid, 102);
                await seed.SaveChangesAsync();
            }

            using (var db = NewContext(w.Conn, w.Tenant))
            {
                var result = await NewController(db, w.Tenant).Delete(w.Project, w.Model, default);
                Assert.IsType<NoContentResult>(result);
            }

            Assert.True(await IsElementDeletedAsync(w, 101));
            Assert.True(await IsElementDeletedAsync(w, 102));
        }
    }

    [Fact]
    public async Task Deleting_a_model_leaves_another_documents_elements_alone()
    {
        // The false-positive guard. A federated element from a DIFFERENT source
        // document — a second discipline model federated into the same project —
        // must survive; retiring it would blank live geometry the coordinator
        // never asked to remove.
        var w = NewWorld();
        using (w.Conn)
        {
            using (var seed = NewContext(w.Conn, w.Tenant))
            {
                (await seed.ProjectModels.SingleAsync(m => m.Id == w.Model)).SourceDocGuid = DocGuid;
                SeedFederatedElement(seed, w.Tenant, w.Project, DocGuid, 101);
                SeedFederatedElement(seed, w.Tenant, w.Project, "doc-guid-mech", 202);
                await seed.SaveChangesAsync();
            }

            using (var db = NewContext(w.Conn, w.Tenant))
                await NewController(db, w.Tenant).Delete(w.Project, w.Model, default);

            Assert.True(await IsElementDeletedAsync(w, 101));
            Assert.False(await IsElementDeletedAsync(w, 202),
                "another document's federated elements were retired by this model's delete");
        }
    }

    [Fact]
    public async Task Deleting_a_model_leaves_another_projects_elements_alone()
    {
        // Same guard across the project boundary — the join is scoped by tenant
        // and project as well as document, so a doc GUID that recurs in a second
        // project (a template model copied between them) cannot leak.
        var w = NewWorld();
        using (w.Conn)
        {
            var otherProject = Guid.NewGuid();
            using (var seed = NewContext(w.Conn, w.Tenant))
            {
                (await seed.ProjectModels.SingleAsync(m => m.Id == w.Model)).SourceDocGuid = DocGuid;
                seed.Projects.Add(new Project
                {
                    Id = otherProject, TenantId = w.Tenant, Name = "Annex",
                    Code = $"AX-{Guid.NewGuid():N}"[..8], Status = ProjectStatus.Active,
                });
                SeedFederatedElement(seed, w.Tenant, w.Project, DocGuid, 101);
                SeedFederatedElement(seed, w.Tenant, otherProject, DocGuid, 303);
                await seed.SaveChangesAsync();
            }

            using (var db = NewContext(w.Conn, w.Tenant))
                await NewController(db, w.Tenant).Delete(w.Project, w.Model, default);

            Assert.True(await IsElementDeletedAsync(w, 101));
            Assert.False(await IsElementDeletedAsync(w, 303),
                "another project's federated elements were retired by this model's delete");
        }
    }

    [Fact]
    public async Task A_model_with_no_source_document_retires_nothing()
    {
        // Every model published before SourceDocGuid existed carries null. The
        // cascade must skip rather than fall back to some looser match — a
        // wrong match here retires geometry a live model still needs.
        var w = NewWorld();
        using (w.Conn)
        {
            using (var seed = NewContext(w.Conn, w.Tenant))
            {
                SeedFederatedElement(seed, w.Tenant, w.Project, DocGuid, 101);
                await seed.SaveChangesAsync();
            }

            using (var db = NewContext(w.Conn, w.Tenant))
                Assert.IsType<NoContentResult>(
                    await NewController(db, w.Tenant).Delete(w.Project, w.Model, default));

            Assert.False(await IsElementDeletedAsync(w, 101));
        }
    }

    [Fact]
    public async Task The_placeholder_source_document_retires_nothing()
    {
        // "revit-plugin" is what every delta landed under before the plugin
        // started sending a real GUID — a shared bucket holding elements from
        // many documents. Treating it as an identity would let one model's
        // delete wipe every Revit-sourced element in the project.
        var w = NewWorld();
        using (w.Conn)
        {
            using (var seed = NewContext(w.Conn, w.Tenant))
            {
                (await seed.ProjectModels.SingleAsync(m => m.Id == w.Model)).SourceDocGuid =
                    FederatedElement.UnknownSourceDocGuid;
                SeedFederatedElement(
                    seed, w.Tenant, w.Project, FederatedElement.UnknownSourceDocGuid, 101);
                await seed.SaveChangesAsync();
            }

            using (var db = NewContext(w.Conn, w.Tenant))
                await NewController(db, w.Tenant).Delete(w.Project, w.Model, default);

            Assert.False(await IsElementDeletedAsync(w, 101),
                "the shared placeholder was treated as a document identity");
        }
    }

    [Fact]
    public async Task A_second_live_model_from_the_same_document_holds_the_elements()
    {
        // Two live revisions of the same document (published with different
        // bytes, so both rows exist). They describe the SAME elements, and one
        // of them is not being deleted — so nothing is retired.
        var w = NewWorld();
        using (w.Conn)
        {
            using (var seed = NewContext(w.Conn, w.Tenant))
            {
                (await seed.ProjectModels.SingleAsync(m => m.Id == w.Model)).SourceDocGuid = DocGuid;
                seed.ProjectModels.Add(new ProjectModel
                {
                    Id = Guid.NewGuid(), TenantId = w.Tenant, ProjectId = w.Project,
                    Name = "ARCH rev B", FileName = "b.glb", StoragePath = "t_x/b.glb",
                    SourceDocGuid = DocGuid,
                });
                SeedFederatedElement(seed, w.Tenant, w.Project, DocGuid, 101);
                await seed.SaveChangesAsync();
            }

            using (var db = NewContext(w.Conn, w.Tenant))
                await NewController(db, w.Tenant).Delete(w.Project, w.Model, default);

            Assert.False(await IsElementDeletedAsync(w, 101),
                "elements were retired while a live sibling model still publishes the same document");
        }
    }

    [Fact]
    public async Task The_model_and_its_scene_chunks_are_still_retired_when_the_cascade_is_skipped()
    {
        // The mirror case: skipping the federated half must not skip the halves
        // that always worked.
        var w = NewWorld();
        using (w.Conn)
        {
            using (var db = NewContext(w.Conn, w.Tenant))
                await NewController(db, w.Tenant).Delete(w.Project, w.Model, default);

            using var check = NewContext(w.Conn, w.Tenant);
            Assert.NotNull((await check.ProjectModels.IgnoreQueryFilters()
                .SingleAsync(m => m.Id == w.Model)).DeletedAt);
            Assert.NotNull((await check.SceneNodes.IgnoreQueryFilters()
                .SingleAsync(n => n.SourceModelId == w.Model)).DeletedAt);
        }
    }

    [Fact]
    public async Task The_job_works_without_a_tenant_context()
    {
        // Hangfire runs it with no tenant, so the global filter would match
        // nothing and the job would quietly purge zero rows forever — the exact
        // failure mode it exists to fix.
        var w = NewWorld(deletedAt: DateTime.UtcNow.AddDays(-31));
        using (w.Conn)
        {
            var storage = new RecordingStorage();
            using (var db = new PlanscapeDbContext(
                new DbContextOptionsBuilder<PlanscapeDbContext>().UseSqlite(w.Conn).Options))
            {
                await NewJob(db, storage).ExecuteAsync();
            }

            Assert.Contains(GlbPath, storage.Deleted);
        }
    }
}
