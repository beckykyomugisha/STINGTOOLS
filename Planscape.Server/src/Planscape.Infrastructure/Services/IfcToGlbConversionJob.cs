using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Background job that converts a stored IFC ProjectModel into a renderable GLB
/// derivative via the converter sidecar. Enqueued by ModelsController when an
/// IFC is uploaded and a converter is configured.
///
/// Flow: load the IFC row → presign a short-lived GET URL for its bytes →
/// ask the sidecar to convert + STREAM the GLB back → store that GLB ourselves
/// (<see cref="IFileStorageService.SaveScopedAsync"/>) and insert a new
/// renderable ProjectModel row with the SAME tenant/project as the source IFC.
///
/// Why we store it here rather than letting the sidecar POST it back to the
/// authed /models endpoint: a single shared platform bearer would need
/// Admin/Owner/Coordinator + project membership across EVERY tenant, which is
/// fragile and over-privileged. Storing in the job keeps one credential surface,
/// attributes the derivative to the right tenant, and keeps the GLB bytes off
/// the API web process (this job runs on the "heavy" worker queue).
///
/// Runs without a tenant context (Hangfire), so it bypasses the tenant filter
/// for the lookup, the tenant-prefix check for the presign, and stamps TenantId
/// explicitly on the new row. Best-effort — a sidecar error is logged, never
/// thrown, so a failed convert leaves the IFC stored (and re-uploadable).
///
/// CAVEAT: not yet exercised against a deployed sidecar.
/// </summary>
public class IfcToGlbConversionJob
{
    private readonly PlanscapeDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly IConverterClient _converter;
    private readonly ILogger<IfcToGlbConversionJob> _logger;

    // Long enough for the sidecar to finish a big IfcConvert before the URL dies.
    private static readonly TimeSpan SourceUrlTtl = TimeSpan.FromMinutes(30);

    public IfcToGlbConversionJob(
        PlanscapeDbContext db,
        IFileStorageService storage,
        IConverterClient converter,
        ILogger<IfcToGlbConversionJob> logger)
    {
        _db = db;
        _storage = storage;
        _converter = converter;
        _logger = logger;
    }

    // "heavy" queue is worker-only (see Program.cs Hangfire server config) — keep
    // the up-to-10-min IfcConvert round-trip off the API's 2 default-queue workers
    // so it can't starve compliance / notification / platform-sync jobs.
    [Hangfire.Queue("heavy")]
    [Hangfire.AutomaticRetry(Attempts = 2, OnAttemptsExceeded = Hangfire.AttemptsExceededAction.Delete)]
    public async Task ExecuteAsync(Guid modelId, CancellationToken ct = default)
    {
        if (!_converter.IsConfigured)
        {
            _logger.LogInformation("IfcToGlbConversionJob: converter not configured; skipping {ModelId}", modelId);
            return;
        }

        _db.BypassTenantFilter = true;
        var ifc = await _db.ProjectModels
            .FirstOrDefaultAsync(m => m.Id == modelId && m.DeletedAt == null, ct);
        if (ifc == null)
        {
            _logger.LogWarning("IfcToGlbConversionJob: model {ModelId} not found", modelId);
            return;
        }
        if (ifc.Format != ModelFormat.Ifc)
        {
            _logger.LogInformation("IfcToGlbConversionJob: model {ModelId} is {Format}, not IFC; skipping", modelId, ifc.Format);
            return;
        }
        if (string.IsNullOrWhiteSpace(ifc.StoragePath))
        {
            _logger.LogWarning("IfcToGlbConversionJob: model {ModelId} has no StoragePath; skipping", modelId);
            return;
        }

        string sourceUrl;
        try
        {
            sourceUrl = await _storage.GetPresignedGetUrlAsync(ifc.StoragePath, SourceUrlTtl, ct, bypassTenantCheck: true);
        }
        catch (NotSupportedException)
        {
            _logger.LogWarning(
                "IfcToGlbConversionJob: storage backend can't presign (local FS dev?); cannot convert {ModelId}", modelId);
            return;
        }

        using var result = await _converter.ConvertIfcToGlbAsync(sourceUrl, ifc.FileName, ifc.Discipline, ct);
        if (!result.Success || result.Glb == null)
        {
            _logger.LogWarning(
                "IfcToGlbConversionJob: conversion of IFC {ModelId} failed: {Error}", modelId, result.Error);
            return;
        }

        // Dedup on the GLB hash (sidecar supplies it before we read the body) so a
        // re-run doesn't pile up identical derivatives.
        if (!string.IsNullOrEmpty(result.Sha256))
        {
            bool exists = await _db.ProjectModels.AnyAsync(
                m => m.TenantId == ifc.TenantId && m.ProjectId == ifc.ProjectId
                  && m.ContentHash == result.Sha256 && m.DeletedAt == null, ct);
            if (exists)
            {
                _logger.LogInformation(
                    "IfcToGlbConversionJob: GLB for IFC {ModelId} already exists (hash {Hash}); skipping", modelId, result.Sha256);
                return;
            }
        }

        var glbName = Path.GetFileNameWithoutExtension(ifc.FileName) + ".glb";

        // Spool the (non-seekable, unknown-length) HTTP stream to a temp file so
        // the S3 SDK gets a seekable stream with a known Content-Length — and so
        // we don't hold the whole GLB in RAM. Then store + record the real size.
        string tmp = Path.Combine(Path.GetTempPath(), $"sting-ifc-glb-{Guid.NewGuid():N}.glb");
        string key;
        long sizeBytes;
        try
        {
            await using (var tmpWrite = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                await result.Glb.CopyToAsync(tmpWrite, ct);
            sizeBytes = new FileInfo(tmp).Length;

            await using var tmpRead = new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.Read);
            key = await _storage.SaveScopedAsync(ifc.TenantId, ifc.ProjectId, glbName, tmpRead, ct);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort temp cleanup */ }
        }

        var row = new ProjectModel
        {
            TenantId = ifc.TenantId,
            ProjectId = ifc.ProjectId,
            Name = string.IsNullOrWhiteSpace(ifc.Name) ? Path.GetFileNameWithoutExtension(glbName) : ifc.Name,
            Description = $"Converted from IFC {ifc.FileName}",
            Discipline = ifc.Discipline,
            FileName = glbName,
            Format = ModelFormat.Glb,
            StoragePath = key,
            ContentHash = result.Sha256,
            FileSizeBytes = sizeBytes > 0 ? sizeBytes : result.Bytes,
            Units = string.IsNullOrWhiteSpace(ifc.Units) ? "mm" : ifc.Units,
            Revision = ifc.Revision,
            UploadedBy = "IFC→GLB converter",
            UploadedAt = DateTime.UtcNow,
        };
        _db.ProjectModels.Add(row);
        try
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "IfcToGlbConversionJob: converted IFC {ModelId} → GLB {GlbId} ({Bytes} bytes) for project {ProjectId}",
                modelId, row.Id, row.FileSizeBytes, ifc.ProjectId);
        }
        catch (DbUpdateException)
        {
            // A concurrent conversion won the unique (TenantId, ProjectId, ContentHash)
            // index — the derivative already exists, so this run is a no-op.
            _db.Entry(row).State = EntityState.Detached;
            _logger.LogInformation(
                "IfcToGlbConversionJob: GLB for IFC {ModelId} inserted concurrently; skipping", modelId);
        }
    }
}
