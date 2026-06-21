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
/// hand that URL to the sidecar (<see cref="IConverterClient.ConvertIfcToGlbAsync"/>),
/// which downloads, runs IfcConvert, and POSTs the GLB back to the models
/// endpoint as a normal (renderable) ProjectModel.
///
/// Runs without a tenant context (Hangfire), so it bypasses the tenant filter
/// for the lookup and the tenant-prefix check for the presign. The IFC row's
/// own TenantId/ProjectId are used verbatim — no cross-tenant leakage because
/// the job is only ever enqueued with a freshly-inserted row's id.
///
/// CAVEAT: not yet exercised against a deployed sidecar. Conversion is
/// best-effort — a sidecar error is logged + stamped on the row, never thrown,
/// so a failed convert leaves the IFC stored (and re-uploadable) rather than
/// erroring the user's upload.
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

        var result = await _converter.ConvertIfcToGlbAsync(
            sourceUrl, ifc.ProjectId, ifc.FileName, ifc.Discipline, ct);

        if (result.Success)
            _logger.LogInformation(
                "IfcToGlbConversionJob: converted IFC {ModelId} → GLB {GlbId} for project {ProjectId}",
                modelId, result.GlbModelId, ifc.ProjectId);
        else
            _logger.LogWarning(
                "IfcToGlbConversionJob: conversion of IFC {ModelId} failed: {Error}", modelId, result.Error);
    }
}
