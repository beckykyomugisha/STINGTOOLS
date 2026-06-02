using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Services;

/// <summary>
/// Server-side dedupe for offline-replay safety (Prompt 18 "build idempotency").
/// The mobile offline queue sends a stable <c>X-Idempotency-Key</c> header on
/// replayable writes; controllers use this guard to short-circuit a retried
/// request to its original result rather than producing a duplicate.
/// </summary>
public static class IdempotencyGuard
{
    public const string HeaderName = "X-Idempotency-Key";

    /// <summary>The client idempotency key for this request, or null if absent.</summary>
    public static string? KeyFrom(HttpRequest req)
    {
        if (req.Headers.TryGetValue(HeaderName, out var v))
        {
            var s = v.ToString();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        return null;
    }

    /// <summary>
    /// Returns the ResultId previously recorded for this (tenant, scope, key),
    /// or null if this key has not been seen — i.e. the request is new.
    /// </summary>
    public static async Task<Guid?> SeenResultAsync(
        PlanscapeDbContext db, Guid tenantId, string scope, string key)
    {
        return await db.Set<IdempotencyRecord>()
            .Where(r => r.TenantId == tenantId && r.Scope == scope && r.Key == key)
            .Select(r => (Guid?)r.ResultId)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Records that (tenant, scope, key) produced <paramref name="resultId"/>.
    /// Swallows the unique-constraint race (a concurrent duplicate replay) so the
    /// caller never fails on a benign double-record.
    /// </summary>
    public static async Task RecordAsync(
        PlanscapeDbContext db, Guid tenantId, string scope, string key, Guid resultId)
    {
        db.Set<IdempotencyRecord>().Add(new IdempotencyRecord
        {
            TenantId = tenantId,
            Scope = scope,
            Key = key,
            ResultId = resultId,
        });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Concurrent replay already recorded the same key — benign.
        }
    }
}
