using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Refresh-and-persist for a team-shared OAuth connection whose provider ROTATES
/// the refresh token on every refresh (ACC does).
///
/// Two failure modes this closes:
///
///  1. Lost rotation. The connector rotates the new pair onto the entity; if the
///     caller's SaveChanges then never runs (exception later in the sync, one
///     SaveChanges for a whole multi-tenant sweep that throws, process restart),
///     the database still holds the refresh token that ACC has just invalidated,
///     and the connection is dead. So the rotation is saved HERE, immediately,
///     in its own SaveChanges.
///
///  2. Cross-process race. The API and the worker (and PlatformSyncJob vs
///     AccSyncService on the same 30-minute cron) can refresh the same row at
///     once; the loser presents an already-rotated refresh token and gets
///     invalid_grant. On Postgres the refresh runs under
///     <c>pg_advisory_xact_lock</c> keyed on the connection id, and the row is
///     re-read from the database after the lock is taken, so a waiter adopts the
///     winner's fresh token instead of refreshing again. On providers without
///     advisory locks (tests: SQLite / InMemory) the fallback is the connector's
///     in-process lock plus a re-read after a failed refresh — if another process
///     already rotated the row and its access token is fresh, that is adopted.
///
/// NOTE: the re-read overwrites ONLY the three token columns on the tracked
/// entity (current and original values, so EF does not see a spurious edit).
/// </summary>
public static class AccTokenRefresher
{
    public static readonly TimeSpan DefaultBuffer = TimeSpan.FromMinutes(5);

    /// <param name="ReconnectRequired">
    /// True when no amount of retrying will help and a person must reconnect ACC:
    /// the stored tokens could not be decrypted, there is no refresh token, or
    /// ACC answered invalid_grant. Callers surface it as RECONNECT_REQUIRED.
    /// </param>
    public sealed record Outcome(bool Success, string? Error = null, bool ReconnectRequired = false);

    /// <summary>
    /// What a person must DO about RECONNECT_REQUIRED. There is no Reconnect
    /// button in the Planscape web or mobile app yet, so this names the call.
    /// </summary>
    public const string ReconnectInstruction =
        "To fix it, reconnect ACC: a project manager or tenant administrator calls GET /api/acc/oauth/start?projectId=<this project's id>, " +
        "opens the returned authorizeUrl and signs in to Autodesk with an account that can see the ACC project. " +
        "The issue mapping and chosen hub / project are kept. GET /api/acc/reconnect-required lists every connection that needs this.";

    public const string UnreadableTokenError =
        "The stored ACC tokens could not be decrypted — reconnect ACC. They were encrypted under a server key ring that no longer exists " +
        "(tokens saved before the durable key store was deployed, or a lost / replaced key ring) and cannot be recovered; restoring the old " +
        "key ring is the only alternative. " + ReconnectInstruction;

    public const string NoRefreshTokenError = "No refresh token is stored — reconnect ACC. " + ReconnectInstruction;

    public const string InvalidGrantHint = " ACC rejected the stored refresh token (invalid_grant: revoked, expired, or already rotated) — reconnect ACC. " + ReconnectInstruction;

    /// <summary>True when either stored token is still ciphertext (decrypt failed).</summary>
    public static bool TokensUnreadable(PlatformConnection c)
        => Planscape.Infrastructure.Security.PlatformTokenProtection.IsUnreadable(c.AccessToken)
           || Planscape.Infrastructure.Security.PlatformTokenProtection.IsUnreadable(c.RefreshToken);

    public static bool IsFresh(PlatformConnection c, TimeSpan buffer)
        => !string.IsNullOrEmpty(c.AccessToken)
           && !Planscape.Infrastructure.Security.PlatformTokenProtection.IsUnreadable(c.AccessToken)
           && c.TokenExpiresAt.HasValue
           && c.TokenExpiresAt.Value > DateTime.UtcNow.Add(buffer);

    public static async Task<Outcome> EnsureFreshAsync(
        PlanscapeDbContext db,
        IPlatformConnector connector,
        PlatformConnection conn,
        ILogger? logger,
        CancellationToken ct,
        TimeSpan? buffer = null)
    {
        var buf = buffer ?? DefaultBuffer;
        if (IsFresh(conn, buf)) return new Outcome(true);

        bool npgsql = db.Database.IsNpgsql() && db.Database.CurrentTransaction == null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        try
        {
            if (npgsql)
            {
                tx = await db.Database.BeginTransactionAsync(ct);
                long key = LockKey("acc-token", conn.Id);
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
                await ReloadTokensAsync(db, conn, ct);
                if (IsFresh(conn, buf))
                {
                    await tx.CommitAsync(ct);
                    return new Outcome(true);
                }
            }

            if (Planscape.Infrastructure.Security.PlatformTokenProtection.IsUnreadable(conn.RefreshToken))
            {
                logger?.LogError("ACC connection {Id}: stored refresh token cannot be decrypted — RECONNECT_REQUIRED.", conn.Id);
                if (tx != null) await tx.RollbackAsync(ct);
                return new Outcome(false, UnreadableTokenError, ReconnectRequired: true);
            }
            if (string.IsNullOrWhiteSpace(conn.RefreshToken))
            {
                if (tx != null) await tx.RollbackAsync(ct);
                return new Outcome(false, NoRefreshTokenError, ReconnectRequired: true);
            }

            var result = await connector.RefreshTokenAsync(conn, ct);   // rotates onto the entity
            if (result.Success)
            {
                // Persist the rotation NOW — ACC has already invalidated the old refresh token.
                await db.SaveChangesAsync(ct);
                if (tx != null) await tx.CommitAsync(ct);
                return new Outcome(true);
            }

            if (tx != null) { await tx.RollbackAsync(ct); tx.Dispose(); tx = null; }

            // Another process may have rotated the row while our refresh was failing
            // with invalid_grant. Adopt its token if it is fresh.
            await ReloadTokensAsync(db, conn, ct);
            if (IsFresh(conn, buf))
            {
                logger?.LogInformation("ACC connection {Id}: refresh failed but another process had rotated the token; adopted it.", conn.Id);
                return new Outcome(true);
            }

            logger?.LogWarning("ACC connection {Id}: token refresh failed: {Error}", conn.Id, result.Error);
            bool invalidGrant = result.Error?.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase) == true;
            return new Outcome(false, (result.Error ?? "Token refresh failed.") + (invalidGrant ? InvalidGrantHint : ""), ReconnectRequired: invalidGrant);
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }
    }

    /// <summary>Re-read the token columns from the database, bypassing the tracked copy and the tenant filter.</summary>
    internal static async Task ReloadTokensAsync(PlanscapeDbContext db, PlatformConnection conn, CancellationToken ct)
    {
        var fresh = await db.PlatformConnections
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == conn.Id)
            .Select(c => new { c.AccessToken, c.RefreshToken, c.TokenExpiresAt })
            .FirstOrDefaultAsync(ct);
        if (fresh == null) return;

        var entry = db.Entry(conn);
        void Set<T>(string name, T value)
        {
            var p = entry.Property(name);
            p.CurrentValue = value;
            if (entry.State != EntityState.Added && entry.State != EntityState.Detached)
            {
                p.OriginalValue = value;
                p.IsModified = false;
            }
        }
        Set(nameof(PlatformConnection.AccessToken), fresh.AccessToken);
        Set(nameof(PlatformConnection.RefreshToken), fresh.RefreshToken);
        Set(nameof(PlatformConnection.TokenExpiresAt), fresh.TokenExpiresAt);
    }

    /// <summary>Stable 64-bit advisory-lock key for (purpose, connection id).</summary>
    public static long LockKey(string purpose, Guid id)
    {
        var h = SHA256.HashData(Encoding.UTF8.GetBytes($"{purpose}:{id:N}"));
        return BitConverter.ToInt64(h, 0);
    }
}
