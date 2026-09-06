using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.Tests;

/// <summary>
/// TRACK C7 — an IFC upload that never becomes renderable used to say nothing.
///
/// THE DEFECT
/// ----------
/// `IfcToGlbConversionJob` is deliberately best-effort: a sidecar error is
/// logged and never thrown, so a failed convert leaves the IFC stored and
/// re-uploadable. That is the right call. But nothing recorded the failure
/// anywhere the PRODUCT could see it, while the upload endpoint had already
/// answered 202 with "a renderable GLB derivative is being generated and will
/// appear as a separate model shortly."
///
/// For a conversion that failed, that sentence never stops being false. The
/// coordinator waits, refreshes, waits again, and eventually concludes the
/// platform is broken — which is a fair reading of a promise that is never
/// withdrawn. The job's own log line is not an answer: it is on the worker, and
/// the person waiting is in a browser.
///
/// The job has FIVE early returns. Each one used to leave the model advertising
/// itself as converting forever.
/// </summary>
public class ConversionStatusTests
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

    /// <summary>A converter that is configured but always fails.</summary>
    private sealed class FailingConverter : IConverterClient
    {
        public bool IsConfigured => true;
        public Task<ConverterGlbResult> ConvertIfcToGlbAsync(
            string sourceUrl, string fileName, string? discipline, CancellationToken ct = default)
            => Task.FromResult(new ConverterGlbResult
            {
                Success = false,
                Error = "IfcConvert exited 1: unsupported schema IFC2X3",
            });
    }

    private sealed class UnconfiguredConverter : IConverterClient
    {
        public bool IsConfigured => false;
        public Task<ConverterGlbResult> ConvertIfcToGlbAsync(
            string sourceUrl, string fileName, string? discipline, CancellationToken ct = default)
            => throw new NotSupportedException("should not be called");
    }

    /// <summary>Storage that cannot presign — the local-filesystem dev case.</summary>
    private sealed class NoPresignStorage : IFileStorageService
    {
        public Task<string> GetPresignedGetUrlAsync(string k, TimeSpan v, CancellationToken ct = default, bool b = false)
            => throw new NotSupportedException("local filesystem cannot presign");

        private static Exception No() => new NotSupportedException("not used by these tests");
        public Task<string> SaveScopedAsync(Guid t, Guid p, string f, Stream c, CancellationToken ct = default) => throw No();
        public Task<string> SaveAsync(string t, string p, string f, Stream c, CancellationToken ct = default) => throw No();
        public Task<Stream?> GetAsync(string path, CancellationToken ct = default, bool b = false) => throw No();
        public Task<bool> DeleteAsync(string path, CancellationToken ct = default, bool b = false) => throw No();
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default, bool b = false) => throw No();
        public Task<int> DeleteByPrefixAsync(string prefix, CancellationToken ct = default, bool b = false) => throw No();
        public Task<PresignedUpload> GetPresignedPutUrlAsync(string k, string c, TimeSpan v, long m, CancellationToken ct = default) => throw No();
        public Task MoveAsync(string s, string d, CancellationToken ct = default, bool b = false) => throw No();
    }

    /// <summary>Presigns fine, so the converter is actually reached.</summary>
    private sealed class PresigningStorage : NoPresignStorageBase
    {
        public override Task<string> GetPresignedGetUrlAsync(
            string k, TimeSpan v, CancellationToken ct = default, bool b = false)
            => Task.FromResult("https://example.invalid/signed");
    }

    private abstract class NoPresignStorageBase : IFileStorageService
    {
        public virtual Task<string> GetPresignedGetUrlAsync(string k, TimeSpan v, CancellationToken ct = default, bool b = false)
            => throw new NotSupportedException();
        private static Exception No() => new NotSupportedException("not used by these tests");
        public Task<string> SaveScopedAsync(Guid t, Guid p, string f, Stream c, CancellationToken ct = default) => throw No();
        public Task<string> SaveAsync(string t, string p, string f, Stream c, CancellationToken ct = default) => throw No();
        public Task<Stream?> GetAsync(string path, CancellationToken ct = default, bool b = false) => throw No();
        public Task<bool> DeleteAsync(string path, CancellationToken ct = default, bool b = false) => throw No();
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default, bool b = false) => throw No();
        public Task<int> DeleteByPrefixAsync(string prefix, CancellationToken ct = default, bool b = false) => throw No();
        public Task<PresignedUpload> GetPresignedPutUrlAsync(string k, string c, TimeSpan v, long m, CancellationToken ct = default) => throw No();
        public Task MoveAsync(string s, string d, CancellationToken ct = default, bool b = false) => throw No();
    }

    private sealed record World(SqliteConnection Conn, Guid Tenant, Guid Model);

    private static World NewWorld(string? storagePath = "t_x/model.ifc")
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
                Id = model, TenantId = tenant, ProjectId = project, Name = "Source",
                FileName = "a.ifc", Format = ModelFormat.Ifc,
                StoragePath = storagePath ?? "", ConversionStatus = "Pending",
            });
            ctx.SaveChanges();
        }
        return new World(conn, tenant, model);
    }

    private static async Task<ProjectModel> ReadAsync(World w)
    {
        using var ctx = NewContext(w.Conn, w.Tenant);
        return await ctx.ProjectModels.AsNoTracking().SingleAsync(m => m.Id == w.Model);
    }

    [Fact]
    public async Task A_converter_failure_is_recorded_with_its_reason()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            using (var db = NewContext(w.Conn, w.Tenant))
                await new IfcToGlbConversionJob(db, new PresigningStorage(), new FailingConverter(),
                        NullLogger<IfcToGlbConversionJob>.Instance)
                    .ExecuteAsync(w.Model);

            var row = await ReadAsync(w);
            Assert.Equal("Failed", row.ConversionStatus);
            // The reason has to travel: "Failed" alone sends the coordinator to
            // support, and the actual cause is usually actionable by them.
            Assert.Contains("IFC2X3", row.ConversionError ?? "");
        }
    }

    [Fact]
    public async Task A_backend_that_cannot_presign_is_recorded_rather_than_left_converting()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            using (var db = NewContext(w.Conn, w.Tenant))
                await new IfcToGlbConversionJob(db, new NoPresignStorage(), new FailingConverter(),
                        NullLogger<IfcToGlbConversionJob>.Instance)
                    .ExecuteAsync(w.Model);

            var row = await ReadAsync(w);
            Assert.Equal("Failed", row.ConversionStatus);
            Assert.Contains("presigned", row.ConversionError ?? "", StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_model_with_no_stored_file_is_recorded_as_failed()
    {
        var w = NewWorld(storagePath: "");
        using (w.Conn)
        {
            using (var db = NewContext(w.Conn, w.Tenant))
                await new IfcToGlbConversionJob(db, new PresigningStorage(), new FailingConverter(),
                        NullLogger<IfcToGlbConversionJob>.Instance)
                    .ExecuteAsync(w.Model);

            Assert.Equal("Failed", (await ReadAsync(w)).ConversionStatus);
        }
    }

    [Fact]
    public async Task An_unconfigured_converter_leaves_the_status_alone()
    {
        // Not a failure: nobody promised a derivative, because the upload
        // endpoint only advertises one when a converter is configured. Marking
        // it Failed would report a broken conversion that was never attempted.
        var w = NewWorld();
        using (w.Conn)
        {
            using (var db = NewContext(w.Conn, w.Tenant))
                await new IfcToGlbConversionJob(db, new PresigningStorage(), new UnconfiguredConverter(),
                        NullLogger<IfcToGlbConversionJob>.Instance)
                    .ExecuteAsync(w.Model);

            Assert.Equal("Pending", (await ReadAsync(w)).ConversionStatus);
        }
    }

    [Fact]
    public async Task No_terminal_path_leaves_a_model_stuck_converting()
    {
        // The property that matters more than any individual branch: whatever
        // happens, the row must not still read "Converting" once the job has
        // returned. That is the state a coordinator waits on forever.
        var w = NewWorld();
        using (w.Conn)
        {
            using (var db = NewContext(w.Conn, w.Tenant))
                await new IfcToGlbConversionJob(db, new PresigningStorage(), new FailingConverter(),
                        NullLogger<IfcToGlbConversionJob>.Instance)
                    .ExecuteAsync(w.Model);

            Assert.NotEqual("Converting", (await ReadAsync(w)).ConversionStatus);
        }
    }
}
