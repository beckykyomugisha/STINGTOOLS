using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// P7 + P8 — Hangfire job that produces renderable derivatives (glTF + PNG
/// thumbnail) for any <see cref="ProjectModel"/> that has a non-glTF geometry.
/// Runs every 10 minutes; picks up the oldest unconverted row.
///
/// Wire in Program.cs:
///   RecurringJob.AddOrUpdate&lt;ModelDerivativeJob&gt;("model-derivatives",
///     j =&gt; j.ExecuteAsync(CancellationToken.None), "*/10 * * * *");
/// </summary>
public class ModelDerivativeJob
{
    private readonly PlanscapeDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly IModelConverter _converter;
    private readonly IModelThumbnailGenerator _thumbnail;
    private readonly ILogger<ModelDerivativeJob> _logger;

    public ModelDerivativeJob(
        PlanscapeDbContext db,
        IFileStorageService storage,
        IModelConverter converter,
        IModelThumbnailGenerator thumbnail,
        ILogger<ModelDerivativeJob> logger)
    {
        _db = db;
        _storage = storage;
        _converter = converter;
        _thumbnail = thumbnail;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        // Candidates: undeleted models where either (a) the format is IFC/RVT
        // and no derived glb has been produced yet (ElementMapPath is used as
        // a flag here — set to "derivative_pending" during processing) OR
        // (b) ThumbnailPath is null and format is GLB.
        var candidates = await _db.ProjectModels.AsNoTracking()
            .Where(m => m.DeletedAt == null
                     && ((m.Format == ModelFormat.Ifc || m.Format == ModelFormat.Rvt) && m.ThumbnailPath == null
                         || m.Format == ModelFormat.Glb && m.ThumbnailPath == null))
            .OrderBy(m => m.UploadedAt)
            .Take(5)
            .ToListAsync(ct);

        foreach (var model in candidates)
        {
            try { await ProcessAsync(model, ct); }
            catch (Exception ex) { _logger.LogError(ex, "Model derivative failed for {ModelId}", model.Id); }
        }
    }

    private async Task ProcessAsync(ProjectModel model, CancellationToken ct)
    {
        var workDir = Path.Combine(Path.GetTempPath(), "planscape-derive", model.Id.ToString("N"));
        Directory.CreateDirectory(workDir);
        var inputPath = Path.Combine(workDir, "in" + Path.GetExtension(model.FileName));
        var glbPath   = Path.Combine(workDir, "out.glb");
        var thumbPath = Path.Combine(workDir, "thumb.png");

        try
        {
            // 1. Copy storage → local.
            using (var src = await _storage.GetAsync(model.StoragePath, ct))
            using (var dst = File.Create(inputPath))
            {
                if (src == null)
                {
                    _logger.LogWarning("Model {ModelId} storage path missing", model.Id);
                    return;
                }
                await src.CopyToAsync(dst, ct);
            }

            // 2. IFC → GLB (skipped for native GLB).
            if (model.Format is ModelFormat.Ifc or ModelFormat.Rvt)
            {
                var conv = await _converter.ConvertToGlbAsync(inputPath, glbPath, ct);
                if (!conv.Success)
                {
                    _logger.LogInformation("Converter {P} not available for {ModelId}: {Err}",
                        conv.ProviderName, model.Id, conv.Error);
                    return;
                }
                // Attach the derived GLB back to storage (sidecar path).
                // We don't overwrite the original IFC — both paths coexist.
                using var glbStream = File.OpenRead(glbPath);
                var derivedPath = await _storage.SaveAsync(
                    "derivatives", model.ProjectId.ToString("N"),
                    $"{model.Id:N}.glb", glbStream, ct);
                using var refresh = _db;
                var row = await refresh.ProjectModels.FindAsync(new object[] { model.Id }, ct);
                if (row != null)
                {
                    // Replace StoragePath on the row so the viewer loads the GLB.
                    // (The original IFC is kept at its own path for archival.)
                    row.StoragePath = derivedPath;
                    row.Format      = ModelFormat.Glb;
                }
            }

            // 3. Thumbnail.
            var thumb = await _thumbnail.GenerateAsync(
                model.Format == ModelFormat.Glb ? inputPath : glbPath,
                thumbPath, ct);
            if (thumb.Success && File.Exists(thumbPath))
            {
                using var thumbStream = File.OpenRead(thumbPath);
                var thumbStorage = await _storage.SaveAsync(
                    "thumbnails", model.ProjectId.ToString("N"),
                    $"{model.Id:N}.png", thumbStream, ct);
                using var refresh = _db;
                var row = await refresh.ProjectModels.FindAsync(new object[] { model.Id }, ct);
                if (row != null) row.ThumbnailPath = thumbStorage;
            }

            await _db.SaveChangesAsync(ct);
        }
        finally
        {
            // Clean up working directory.
            try { Directory.Delete(workDir, recursive: true); } catch { }
        }
    }
}
