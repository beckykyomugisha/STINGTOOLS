using System.Data.Common;

namespace Planscape.API;

/// <summary>
/// Postgres Row Level Security policies, applied through the patcher path
/// rather than through an EF migration.
///
/// <para><b>Why this exists.</b> The RLS policies were written in
/// <c>20260506200000_EnablePostgresRowLevelSecurity</c>, but production does not
/// run migrations (see <c>docs/adr/0001-schema-management.md</c>) — the patcher
/// is this codebase's schema-management mechanism. So the policies have never
/// been applied to any database, and the config key that claims to enable them
/// only ever registered the interceptor that sets the session variable. This
/// moves the policy SQL onto the path that actually executes.</para>
///
/// <para><b>This is a no-op unless deliberately switched on.</b>
/// <see cref="ApplyAsync"/> is only called when <c>Database:RlsEnabled</c> is
/// true. That key is set nowhere in the repository — not in any
/// <c>appsettings*.json</c>, not in <c>render.yaml</c>, not in any env file.
/// Merging this changes nothing at runtime until an operator sets it.</para>
///
/// <para><b>These policies FAIL CLOSED.</b> The migration's policy carried an
/// <c>OR coalesce(current_setting(...), '') = ''</c> branch, so a connection
/// that never set <c>app.current_tenant</c> matched <i>every</i> row. That made
/// the policy a no-op precisely when the application layer had already failed —
/// the one case where a second layer of defence is supposed to earn its keep.
/// The version here omits that branch: unset session variable means
/// <c>current_setting</c> returns NULL, the predicate is NULL, and no row is
/// visible.</para>
///
/// <para><b>Consequence of failing closed — read before enabling.</b> Any
/// connection that does not set the GUC now sees zero rows. That includes every
/// path which sets <c>PlanscapeDbContext.BypassTenantFilter = true</c> (Hangfire
/// jobs, cross-tenant admin scans), because
/// <c>RlsConnectionInterceptor.ConnectionOpenedAsync</c> returns early for those
/// and never issues the <c>SET</c>. Enabling this key without first giving those
/// paths a role that carries BYPASSRLS will not leak data — it will make
/// background jobs silently find nothing. That is the open decision this class
/// is meant to make cheap to take, not one it takes for you.</para>
///
/// <para><b>Idempotency.</b> Postgres has no <c>CREATE POLICY IF NOT EXISTS</c>
/// (unlike <c>CREATE TABLE</c>), so each policy is written as
/// <c>DROP POLICY IF EXISTS</c> followed by <c>CREATE POLICY</c> — re-running is
/// safe and converges on the same definition, which also means this file is how
/// you correct a policy later. <c>ENABLE</c>/<c>FORCE ROW LEVEL SECURITY</c> are
/// natural no-ops when already set.</para>
/// </summary>
internal static class RlsPolicyPatcher
{
    /// <summary>
    /// Postgres GUC holding the caller's tenant. Set per connection by
    /// <c>RlsConnectionInterceptor</c>. The <c>app.</c> prefix is required —
    /// Postgres rejects un-namespaced custom settings.
    /// </summary>
    private const string TenantSetting = "app.current_tenant";

    /// <summary>
    /// Tenant-scoped tables carrying a <c>TenantId</c> column.
    ///
    /// Mirrors the list in the (inert) <c>EnablePostgresRowLevelSecurity</c>
    /// migration, which in turn mirrors <c>AddTenantIdToAllScopedEntities</c>.
    /// EF model snapshots carry no "is tenant scoped" tag, so this is kept in
    /// sync by hand. A table missing from this list is NOT protected by RLS —
    /// <see cref="VerifyAsync"/> exists so that gap is measurable rather than
    /// assumed.
    /// </summary>
    internal static readonly string[] TenantScopedTables =
    {
        // Direct project children
        "TaggedElements", "Issues", "Documents", "WorkflowRuns",
        "ComplianceSnapshots", "SeqCounters", "Meetings", "Transmittals",
        "ProjectMembers", "ProjectModels", "ScheduleTasks", "CostItems",
        "SyncConflicts", "SyncWatermarks", "SiteDiaries", "StageGates",
        "IssueCustomFieldSchemas",
        // Indirect children (TenantId backfilled via parent)
        "IssueAttachments", "IssueComments", "DocumentMarkups",
        "DocumentVersions", "DocumentApprovals", "MeetingActionItems",
        "SiteDiaryAttachments", "InformationDeliverables", "StageGateCriteria",
        // Top-level tenant-scoped
        "Projects", "Users", "AuditLogs",
        "DevicePushToken", "UserNotificationPreferences", "PlatformConnections",
        "TenantBranding", "Subscriptions", "Invoices", "Payments",
        "Assets", "MaintenanceTasks", "OutboxMessages", "OutboundWebhooks",
        "PinCrdtUpdates", "ModelMarkups", "IssueAudioNotes", "SceneNodes",
    };

    /// <summary>Fail-closed isolation policy for one <c>TenantId</c> table.</summary>
    internal static string PolicyFor(string table) => $@"
        ALTER TABLE ""{table}"" ENABLE ROW LEVEL SECURITY;
        ALTER TABLE ""{table}"" FORCE ROW LEVEL SECURITY;
        DROP POLICY IF EXISTS tenant_isolation ON ""{table}"";
        CREATE POLICY tenant_isolation ON ""{table}""
            USING (""TenantId""::text = current_setting('{TenantSetting}', true))
            WITH CHECK (""TenantId""::text = current_setting('{TenantSetting}', true));";

    /// <summary>
    /// The Tenants table keys on <c>Id</c>, not <c>TenantId</c>, so it needs its
    /// own policy. Same fail-closed shape.
    /// </summary>
    internal static string TenantsPolicy() => $@"
        ALTER TABLE ""Tenants"" ENABLE ROW LEVEL SECURITY;
        ALTER TABLE ""Tenants"" FORCE ROW LEVEL SECURITY;
        DROP POLICY IF EXISTS tenant_self_visibility ON ""Tenants"";
        CREATE POLICY tenant_self_visibility ON ""Tenants""
            USING (""Id""::text = current_setting('{TenantSetting}', true))
            WITH CHECK (""Id""::text = current_setting('{TenantSetting}', true));";

    /// <summary>Every statement this patcher would run, in order.</summary>
    internal static IEnumerable<string> BuildStatements()
    {
        foreach (var t in TenantScopedTables) yield return PolicyFor(t);
        yield return TenantsPolicy();
    }

    /// <summary>
    /// Rollback: drop the policies and disable RLS. Emitted here rather than
    /// left to a runbook so the undo ships with the change. See the PR body for
    /// the procedure; this is the exact SQL it references.
    /// </summary>
    internal static IEnumerable<string> BuildRollbackStatements()
    {
        foreach (var t in TenantScopedTables)
            yield return $@"
                DROP POLICY IF EXISTS tenant_isolation ON ""{t}"";
                ALTER TABLE ""{t}"" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE ""{t}"" DISABLE ROW LEVEL SECURITY;";
        yield return @"
            DROP POLICY IF EXISTS tenant_self_visibility ON ""Tenants"";
            ALTER TABLE ""Tenants"" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE ""Tenants"" DISABLE ROW LEVEL SECURITY;";
    }

    /// <summary>
    /// Apply the policies. Call only when <c>Database:RlsEnabled</c> is true.
    ///
    /// <para>Unlike <see cref="PlatformSchemaPatcher"/>, which logs failures and
    /// carries on, this throws. A partially-applied security control is a worse
    /// state than an unapplied one: RLS enabled on a table whose policy failed to
    /// create means default-deny, so that table returns zero rows to everyone —
    /// a silent, total outage of one table that a "N ok, M failed" log line at
    /// boot would not make anyone act on. Failing the boot is the honest
    /// response.</para>
    ///
    /// <para>Tables absent from the database are skipped rather than treated as
    /// failures — the list spans entities that may not exist on every
    /// deployment.</para>
    /// </summary>
    public static async Task ApplyAsync(DbConnection conn)
    {
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        var present = await LoadExistingTablesAsync(conn);
        int applied = 0, skipped = 0;
        var failures = new List<string>();

        foreach (var table in TenantScopedTables.Concat(new[] { "Tenants" }))
        {
            if (!present.Contains(table)) { skipped++; continue; }
            var sql = table == "Tenants" ? TenantsPolicy() : PolicyFor(table);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();
                applied++;
            }
            catch (Exception ex)
            {
                failures.Add($"{table}: {ex.Message}");
            }
        }

        Console.WriteLine($"[rls] applied {applied} policies, skipped {skipped} absent tables, {failures.Count} failed");

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "RLS policy application failed for " + failures.Count + " table(s); refusing to start with a " +
                "partially-applied tenant-isolation policy, because an RLS-enabled table without a policy " +
                "returns zero rows to every caller. Fix the listed tables or set Database:RlsEnabled=false " +
                "and re-run the rollback SQL. Failures: " + string.Join(" | ", failures));
        }
    }

    /// <summary>
    /// Read-only check: which tenant-scoped tables currently have RLS enabled
    /// and a policy attached. Used by the rehearsal to assert on real state
    /// instead of assuming the apply worked.
    /// </summary>
    public static async Task<List<(string Table, bool RlsEnabled, int Policies)>> VerifyAsync(DbConnection conn)
    {
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        var rows = new List<(string, bool, int)>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT c.relname::text,
                   c.relrowsecurity,
                   (SELECT count(*) FROM pg_policy p WHERE p.polrelid = c.oid)::int
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind = 'r'
            ORDER BY c.relname";

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            rows.Add((r.GetString(0), r.GetBoolean(1), r.GetInt32(2)));
        return rows;
    }

    private static async Task<HashSet<string>> LoadExistingTablesAsync(DbConnection conn)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT tablename::text FROM pg_tables WHERE schemaname = 'public'";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) set.Add(r.GetString(0));
        return set;
    }
}
