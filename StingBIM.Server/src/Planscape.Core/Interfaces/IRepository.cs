namespace Planscape.Core.Interfaces;

public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task<T> AddAsync(T entity, CancellationToken ct = default);
    Task UpdateAsync(T entity, CancellationToken ct = default);
    Task DeleteAsync(T entity, CancellationToken ct = default);
}

public interface ITaggedElementRepository : IRepository<Entities.TaggedElement>
{
    Task<IReadOnlyList<Entities.TaggedElement>> GetByProjectAsync(Guid projectId, CancellationToken ct = default);
    Task BulkUpsertAsync(Guid projectId, IEnumerable<Entities.TaggedElement> elements, CancellationToken ct = default);
    Task<Entities.TaggedElement?> GetByRevitIdAsync(Guid projectId, long revitElementId, CancellationToken ct = default);
}

public interface IProjectRepository : IRepository<Entities.Project>
{
    Task<IReadOnlyList<Entities.Project>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
}

public interface ILicenseService
{
    Task<Entities.LicenseKey?> ValidateKeyAsync(string key, string machineId, CancellationToken ct = default);
    Task<Entities.LicenseKey> GenerateKeyAsync(Guid tenantId, Entities.LicenseTier tier, bool mimEnabled, CancellationToken ct = default);
    Task<bool> DeactivateKeyAsync(Guid keyId, CancellationToken ct = default);
}

public interface ITenantContext
{
    Guid TenantId { get; }
    string TenantSlug { get; }
    Entities.LicenseTier Tier { get; }
    bool MimEnabled { get; }
}
