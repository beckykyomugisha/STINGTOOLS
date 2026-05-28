using Microsoft.EntityFrameworkCore;
using Planscape.Core.Constants;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// K1 keystone implementation over <see cref="ExternalElementMapping"/>.
/// Reads are tenant-scoped by the DbContext global query filter; the bind
/// write stamps TenantId via the SaveChanges hook (set explicitly here too
/// for clarity and parity with IfcController).
/// </summary>
public sealed class IdentityResolverService : IIdentityResolverService
{
    private readonly PlanscapeDbContext _db;

    public IdentityResolverService(PlanscapeDbContext db) => _db = db;

    public async Task<string?> ResolveCanonicalGuidAsync(
        Guid projectId, string host, string hostElementId, CancellationToken ct = default)
    {
        var h = MappingHosts.Normalize(host);
        return await _db.ExternalElementMappings
            .Where(m => m.ProjectId == projectId && m.Host == h && m.HostElementId == hostElementId)
            .OrderByDescending(m => m.LastSeenUtc)
            .Select(m => m.IfcGlobalId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<HostElementRef>> ResolveHostElementsAsync(
        Guid projectId, string ifcGlobalId, string? host = null, CancellationToken ct = default)
    {
        var q = _db.ExternalElementMappings
            .Where(m => m.ProjectId == projectId && m.IfcGlobalId == ifcGlobalId);

        if (!string.IsNullOrWhiteSpace(host))
        {
            var h = MappingHosts.Normalize(host);
            q = q.Where(m => m.Host == h);
        }

        return await q
            .Select(m => new HostElementRef(m.Host, m.HostElementId, m.HostDocumentGuid, m.HostDisplayLabel))
            .ToListAsync(ct);
    }

    public Task<IReadOnlyList<HostElementRef>> GetCrossHostFanoutAsync(
        Guid projectId, string ifcGlobalId, CancellationToken ct = default)
        => ResolveHostElementsAsync(projectId, ifcGlobalId, host: null, ct);

    public async Task<IdentityBindResult> BindIotDeviceAsync(
        Guid projectId, string ifcGlobalId, string deviceId,
        string? label = null, string? hostDocumentGuid = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ifcGlobalId))
            throw new ArgumentException("ifcGlobalId is required", nameof(ifcGlobalId));
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("deviceId is required", nameof(deviceId));

        var existing = await _db.ExternalElementMappings.FirstOrDefaultAsync(
            m => m.ProjectId == projectId
                 && m.IfcGlobalId == ifcGlobalId
                 && m.Host == MappingHosts.Iot
                 && m.HostDocumentGuid == hostDocumentGuid,
            ct);

        if (existing is not null)
        {
            existing.HostElementId = deviceId;
            existing.HostDisplayLabel = label;
            existing.LastSeenUtc = DateTime.UtcNow;
            existing.IngestionCount += 1;
            await _db.SaveChangesAsync(ct);
            return new IdentityBindResult(Created: false, MappingId: existing.Id);
        }

        var row = new ExternalElementMapping
        {
            TenantId = _db.CurrentTenantId,
            ProjectId = projectId,
            IfcGlobalId = ifcGlobalId,
            Host = MappingHosts.Iot,
            HostElementId = deviceId,
            HostDocumentGuid = hostDocumentGuid,
            HostDisplayLabel = label,
        };
        _db.ExternalElementMappings.Add(row);
        await _db.SaveChangesAsync(ct);
        return new IdentityBindResult(Created: true, MappingId: row.Id);
    }
}
