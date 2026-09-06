using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// The only thing in the platform that permanently destroys a project.
///
/// Projects are never hard-deleted inline by a request. POST
/// /api/projects/{id}/purge only sets <c>Project.PurgeAfter</c> (owner-only,
/// archived-only, code-confirmed) and hides the project everywhere; this
/// nightly job is what eventually destroys it, and only once that timestamp
/// has passed. The gap is the safety property — until this job runs, DELETE
/// /api/projects/{id}/purge fully restores the project.
///
/// Same archive-then-purge shape as <see cref="CustomFieldsPurgeJob"/>.
///
/// <para>Deletion strategy: <c>Projects.Remove</c> plus EF's configured
/// cascade. A project is referenced by ~170 project-scoped tables; hand-writing
/// deletes for each would rot the moment anyone adds a table, so the schema's
/// own FK cascade is the source of truth. Anything NOT covered by a cascade
/// (blob storage) is handled explicitly below.</para>
/// </summary>
public class ProjectPurgeJob
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<ProjectPurgeJob> _logger;
    private readonly Planscape.Core.Interfaces.IFileStorageService? _storage;

    public ProjectPurgeJob(
        PlanscapeDbContext db,
        ILogger<ProjectPurgeJob> logger,
        Planscape.Core.Interfaces.IFileStorageService? storage = null)
    {
        _db = db;
        _logger = logger;
        _storage = storage;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var due = await _db.Projects
            .Where(p => p.PurgeAfter != null && p.PurgeAfter < now)
            .ToListAsync(ct);

        if (due.Count == 0)
        {
            _logger.LogInformation("ProjectPurgeJob: nothing due");
            return;
        }

        foreach (var project in due)
        {
            // Log BEFORE destroying: once the rows are gone this is the only
            // remaining record that the project ever existed, and an audit row
            // is no good if it lives in a table the cascade just emptied.
            _logger.LogWarning(
                "[purge] PERMANENTLY deleting project {ProjectId} ({Code} — {Name}), tenant {TenantId}, "
              + "scheduled {PurgeAfter:u} by {RequestedBy}",
                project.Id, project.Code, project.Name, project.TenantId,
                project.PurgeAfter, project.PurgeRequestedById);

            // Blob storage is outside the database, so no FK cascade reaches it.
            // Best-effort: an orphaned blob costs storage, but a storage error
            // must not strand the project half-deleted in the database.
            //
            // TWO prefixes, because the storage layer has two path conventions
            // and both are live: SaveScopedAsync writes t_{tenantId}/{projectId}/
            // while SaveAsync writes {tenantSlug}/{projectCode}/ — the latter is
            // what document and model uploads actually use today (observed:
            // "exo/PRJ-001/…"). Deleting only one shape would silently orphan
            // every real file.
            //
            // The trailing slash is load-bearing: prefix "exo/PRJ-1" also matches
            // "exo/PRJ-10/…", so without it purging one project could delete a
            // DIFFERENT project's files. Never remove it.
            if (_storage != null)
            {
                var tenantSlug = await _db.Tenants.Where(t => t.Id == project.TenantId)
                    .Select(t => t.Slug).FirstOrDefaultAsync(ct);

                var prefixes = new List<string> { $"t_{project.TenantId:N}/{project.Id:N}/" };
                if (!string.IsNullOrWhiteSpace(tenantSlug) && !string.IsNullOrWhiteSpace(project.Code))
                    prefixes.Add($"{tenantSlug}/{project.Code}/");

                foreach (var prefix in prefixes)
                {
                    try
                    {
                        int removed = await _storage.DeleteByPrefixAsync(prefix, ct, bypassTenantCheck: true);
                        _logger.LogInformation("[purge] removed {Count} object(s) under {Prefix}", removed, prefix);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "[purge] storage cleanup failed for prefix {Prefix} (project {ProjectId}); DB rows still "
                          + "purged, blobs may be orphaned", prefix, project.Id);
                    }
                }
            }

            _db.Projects.Remove(project);

            // One SaveChanges per project rather than one for the batch: a
            // cascade failure on project A must not roll back the successful
            // purge of project B, and must not abort the loop.
            try
            {
                await _db.SaveChangesAsync(ct);
                _logger.LogWarning("[purge] project {ProjectId} ({Code}) permanently deleted", project.Id, project.Code);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[purge] FAILED to delete project {ProjectId} ({Code}) — it stays scheduled and will be retried "
                  + "on the next run", project.Id, project.Code);
                _db.Entry(project).State = EntityState.Unchanged;
            }
        }
    }
}
