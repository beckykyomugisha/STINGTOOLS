namespace Planscape.Infrastructure.Authorization;

/// <summary>
/// Phase 156 — no-op revocation store for unit tests / dev configs
/// that don't wire Redis. <see cref="GetMinIatAsync"/> always returns
/// null so the auth handler treats every iat as acceptable; revoke
/// is a silent no-op.
/// </summary>
public sealed class NullPermissionRevocationStore : IPermissionRevocationStore
{
    public Task<long?> GetMinIatAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<long?>(null);

    public Task RevokeAllPriorTokensAsync(Guid userId, CancellationToken ct = default)
        => Task.CompletedTask;
}
