using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Planscape.Infrastructure.Data;

/// <summary>
/// Phase 175 audit P0-9 — sets the Postgres GUC variable
/// <c>app.current_tenant</c> on every connection so the RLS policies
/// installed by <c>EnablePostgresRowLevelSecurity</c> can filter
/// per-tenant. The session variable lives for the lifetime of the
/// physical connection; the Npgsql connection pool issues
/// <c>DISCARD ALL</c> on connection return which clears it (so we
/// re-set on the next checkout).
///
/// We intentionally do NOT touch the variable when
/// <see cref="PlanscapeDbContext.BypassTenantFilter"/> is true (Hangfire
/// jobs, migrations, cross-tenant admin scans). The RLS policies fall
/// back to the "empty setting" branch in that case so unset == bypass.
/// Once the policy is tightened to remove the empty-setting branch,
/// bypass paths will need to switch to a privileged DB role.
///
/// Wired only when <c>Database:RlsEnabled = true</c> in config — this
/// is the rollout safety switch.
/// </summary>
public sealed class RlsConnectionInterceptor : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not PlanscapeDbContext db) return;
        if (db.BypassTenantFilter) return;
        var tenantId = db.CurrentTenantId;
        if (tenantId == Guid.Empty) return;

        await using var cmd = connection.CreateCommand();
        // SET (not SET LOCAL) so the value persists for the connection's
        // lifetime, not just the next implicit transaction. Pool reset
        // (Npgsql `DISCARD ALL`) clears it on return.
        //
        // tenantId comes from a parsed JWT claim (always a Guid), so the
        // string interpolation is safe. We can't parameterise SET in
        // PostgreSQL.
        cmd.CommandText = $"SET app.current_tenant = '{tenantId:D}'";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public override void ConnectionOpened(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        if (eventData.Context is not PlanscapeDbContext db) return;
        if (db.BypassTenantFilter) return;
        var tenantId = db.CurrentTenantId;
        if (tenantId == Guid.Empty) return;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SET app.current_tenant = '{tenantId:D}'";
        cmd.ExecuteNonQuery();
    }
}
