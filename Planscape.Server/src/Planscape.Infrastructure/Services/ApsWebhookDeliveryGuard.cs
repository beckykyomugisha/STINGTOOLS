using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Atomic "first delivery wins" claim for APS webhook deliveries
/// (<c>x-adsk-delivery-id</c>) — ROADMAP ACC-SRV-7.
///
/// WHY NOT THE CACHE: the receiver used to GET the id from IDistributedCache,
/// process, then SET it. Two concurrent deliveries of one id both passed the GET
/// and were both processed. The claim is now an INSERT on the primary key of
/// <c>ApsWebhookDeliveries</c> — the database decides the single winner, across
/// processes and API instances, and it works whether or not Redis is configured.
///
/// PROVIDERS
///   * Relational (Postgres in production, SQLite): <c>INSERT … ON CONFLICT
///     ("DeliveryId") DO NOTHING</c>; one row affected = claimed, zero = duplicate.
///   * EF InMemory (tests): no ON CONFLICT and no unique-violation guarantee
///     across contexts, so the check-and-add runs under a process-wide lock.
///     Atomic within the process, which is all an InMemory database spans.
///
/// FAILURE: when processing fails after the claim, the caller <see cref="ReleaseAsync"/>s
/// it so APS's redelivery is processed rather than dropped as a duplicate. When
/// the claim itself cannot be taken (database error) the result is
/// <see cref="Claim.Unavailable"/> and the caller processes anyway — the handlers
/// are idempotent in effect, and dropping a real event is the worse failure.
///
/// RETENTION: rows older than <see cref="Retention"/> are pruned on roughly one
/// claim in <see cref="PruneEvery"/> (relational only). APS retries a delivery
/// for far less than that.
/// </summary>
public class ApsWebhookDeliveryGuard
{
    public enum Claim { Claimed, Duplicate, Unavailable }

    public static readonly TimeSpan Retention = TimeSpan.FromHours(48);
    public const int PruneEvery = 100;
    public const int MaxIdLength = 200;

    private static readonly SemaphoreSlim _inMemoryLock = new(1, 1);
    private static int _claims;

    private readonly PlanscapeDbContext _db;
    private readonly ILogger<ApsWebhookDeliveryGuard> _log;

    public ApsWebhookDeliveryGuard(PlanscapeDbContext db, ILogger<ApsWebhookDeliveryGuard> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<Claim> TryClaimAsync(string deliveryId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(deliveryId)) return Claim.Unavailable;
        if (deliveryId.Length > MaxIdLength) deliveryId = deliveryId[..MaxIdLength];
        var now = DateTime.UtcNow;
        try
        {
            if (_db.Database.IsRelational())
            {
                int rows = await _db.Database.ExecuteSqlInterpolatedAsync(
                    $@"INSERT INTO ""ApsWebhookDeliveries"" (""DeliveryId"", ""ReceivedAt"") VALUES ({deliveryId}, {now}) ON CONFLICT (""DeliveryId"") DO NOTHING", ct);
                if (Interlocked.Increment(ref _claims) % PruneEvery == 0) await PruneAsync(now, ct);
                return rows == 1 ? Claim.Claimed : Claim.Duplicate;
            }

            await _inMemoryLock.WaitAsync(ct);
            try
            {
                if (await _db.ApsWebhookDeliveries.AsNoTracking().AnyAsync(d => d.DeliveryId == deliveryId, ct))
                    return Claim.Duplicate;
                var row = new ApsWebhookDelivery { DeliveryId = deliveryId, ReceivedAt = now };
                _db.ApsWebhookDeliveries.Add(row);
                await _db.SaveChangesAsync(ct);
                _db.Entry(row).State = EntityState.Detached;
                return Claim.Claimed;
            }
            finally { _inMemoryLock.Release(); }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "APS webhook: could not claim delivery {Delivery}; processing it without duplicate protection.", deliveryId);
            return Claim.Unavailable;
        }
    }

    /// <summary>Undo a claim whose processing failed, so the redelivery is not dropped.</summary>
    public async Task ReleaseAsync(string deliveryId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(deliveryId)) return;
        if (deliveryId.Length > MaxIdLength) deliveryId = deliveryId[..MaxIdLength];
        try
        {
            if (_db.Database.IsRelational())
            {
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $@"DELETE FROM ""ApsWebhookDeliveries"" WHERE ""DeliveryId"" = {deliveryId}", CancellationToken.None);
                return;
            }
            var row = await _db.ApsWebhookDeliveries.FirstOrDefaultAsync(d => d.DeliveryId == deliveryId, CancellationToken.None);
            if (row != null)
            {
                _db.ApsWebhookDeliveries.Remove(row);
                await _db.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "APS webhook: could not release the claim on delivery {Delivery}; an APS redelivery of it will be ignored as a duplicate.", deliveryId);
        }
    }

    private async Task PruneAsync(DateTime now, CancellationToken ct)
    {
        try
        {
            var cutoff = now - Retention;
            int n = await _db.Database.ExecuteSqlInterpolatedAsync(
                $@"DELETE FROM ""ApsWebhookDeliveries"" WHERE ""ReceivedAt"" < {cutoff}", ct);
            if (n > 0) _log.LogInformation("APS webhook: pruned {Count} delivery claims older than {Hours} h.", n, Retention.TotalHours);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "APS webhook: pruning old delivery claims failed (harmless; retried later).");
        }
    }
}
