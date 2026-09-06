using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// C4 — the purge job <see cref="Planscape.Core.Entities.ProjectModel.DeletedAt"/>
/// has always promised and never had.
///
/// <para>The entity's own doc comment reads "file is purged by a Hangfire job
/// after 30 days". No such job existed. Every soft-deleted model's GLB, element
/// map and thumbnail stayed in object storage indefinitely — on a project that
/// re-publishes weekly that is the entire history of the model, paid for per
/// gigabyte per month, with nothing in the product that could ever remove it.
/// A documented retention promise that nothing implements is worse than no
/// promise: it is the reason nobody went looking.</para>
///
/// <para><b>Archive then purge.</b> Rows keep their audit value for the grace
/// period, so this deletes the BYTES first and only then the row. If byte
/// deletion fails the row is left alone, so the next run retries rather than
/// orphaning storage that nothing references any more — an orphan with no row
/// pointing at it is unfindable, which is the one state worth avoiding.</para>
/// </summary>
public class ModelPurgeJob
{
    private readonly PlanscapeDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly ILogger<ModelPurgeJob> _logger;

    /// <summary>Matches the 30 days the entity documents.</summary>
    private static readonly TimeSpan PurgeGrace = TimeSpan.FromDays(30);

    public ModelPurgeJob(
        PlanscapeDbContext db, IFileStorageService storage, ILogger<ModelPurgeJob> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    [Hangfire.Queue("heavy")]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        // Runs without a tenant context (Hangfire), so the global filter would
        // otherwise match nothing and the job would silently purge zero rows
        // forever — the same failure mode it is here to fix.
        _db.BypassTenantFilter = true;

        var cutoff = DateTime.UtcNow - PurgeGrace;
        var stale = await _db.ProjectModels
            .Where(m => m.DeletedAt != null && m.DeletedAt < cutoff)
            .ToListAsync(ct);

        if (stale.Count == 0)
        {
            _logger.LogInformation("ModelPurgeJob: nothing to purge");
            return;
        }

        int purged = 0, deferred = 0;
        foreach (var model in stale)
        {
            var paths = new[] { model.StoragePath, model.ElementMapPath, model.ThumbnailPath }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct()
                .ToList();

            bool bytesGone = true;
            foreach (var path in paths)
            {
                try
                {
                    // bypassTenantCheck: no tenant context in a background job.
                    await _storage.DeleteAsync(path!, ct, bypassTenantCheck: true);
                }
                catch (Exception ex)
                {
                    bytesGone = false;
                    _logger.LogWarning(ex,
                        "ModelPurgeJob: could not delete {Path} for model {ModelId}; row retained for retry.",
                        path, model.Id);
                }
            }

            if (!bytesGone) { deferred++; continue; }

            // The chunks reference bytes that no longer exist; they go with it.
            var chunks = await _db.SceneNodes.Where(n => n.SourceModelId == model.Id).ToListAsync(ct);
            foreach (var chunk in chunks)
            {
                if (!string.IsNullOrWhiteSpace(chunk.StoragePath))
                {
                    try { await _storage.DeleteAsync(chunk.StoragePath, ct, bypassTenantCheck: true); }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "ModelPurgeJob: could not delete chunk {Path} for model {ModelId}.",
                            chunk.StoragePath, model.Id);
                    }
                }
            }
            _db.SceneNodes.RemoveRange(chunks);

            // The transform is meaningless without the model it positions.
            var transforms = await _db.ProjectModelTransforms
                .Where(t => t.ProjectModelId == model.Id).ToListAsync(ct);
            _db.ProjectModelTransforms.RemoveRange(transforms);

            _db.ProjectModels.Remove(model);
            purged++;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "ModelPurgeJob: purged {Purged} model(s) deleted before {Cutoff:u}; {Deferred} deferred to the next run.",
            purged, cutoff, deferred);
    }
}
