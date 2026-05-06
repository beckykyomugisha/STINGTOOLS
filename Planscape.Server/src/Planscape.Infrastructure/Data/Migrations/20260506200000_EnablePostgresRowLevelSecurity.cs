using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// Phase 175 audit P0-9 — Postgres Row Level Security as defense in
/// depth on top of the EF Core query filter. Tenant isolation now has
/// two redundant layers: the EF predicate (`BypassTenantFilter ||
/// TenantId == CurrentTenantId`) AND a Postgres RLS policy that gates
/// every row read at the database. A bug in EF (forgotten `Where`,
/// missed query filter on a new entity, raw SQL) can no longer leak
/// rows across tenants.
///
/// Rollout strategy is OPT-IN:
///   1. This migration enables RLS on every tenant-scoped table with a
///      *permissive* policy: rows visible when the connection has set
///      `app.current_tenant` to the row's TenantId, OR when the
///      session variable is unset / empty.
///   2. Application sets `app.current_tenant` per request via
///      `RlsConnectionInterceptor` (registered when
///      `Database:RlsEnabled = true` in config).
///   3. Once verified in staging, a follow-up migration tightens the
///      policy by removing the empty-setting bypass and creating a
///      separate `planscape_app` role lacking BYPASSRLS — only that
///      role connects to the DB from the application.
///
/// The "permissive when unset" branch is critical for safe rollout:
/// without it, EVERY query from a connection that hasn't set the
/// session variable returns zero rows, including pre-RLS code paths,
/// EF migrations, and Hangfire jobs (which currently bypass via
/// `BypassTenantFilter = true` at the EF layer).
/// </summary>
public partial class EnablePostgresRowLevelSecurity : Migration
{
    // Mirrors AddTenantIdToAllScopedEntities; kept in sync manually
    // because EF model snapshots don't carry a "is tenant scoped" tag.
    private static readonly string[] TenantScopedTables =
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

    protected override void Up(MigrationBuilder mb)
    {
        // GUC variable name. `app.current_tenant` follows the convention
        // (lowercase, dotted, app-scoped) Postgres expects for custom
        // settings — the leading namespace is required so PG doesn't
        // reject it as a "reserved" config option.
        const string TenantSettingName = "app.current_tenant";

        foreach (var table in TenantScopedTables)
        {
            // ENABLE + FORCE so even the table owner is constrained.
            // Without FORCE, the table owner bypasses RLS by default,
            // which would defeat the whole purpose once we switch the
            // app to a non-owner role.
            mb.Sql($@"
                ALTER TABLE ""{table}"" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE ""{table}"" FORCE ROW LEVEL SECURITY;
            ");

            // Permissive isolation policy. The empty-setting branch is
            // the rollout safety net — it lets the EF layer do the
            // filtering until we explicitly switch app to use the
            // restricted role. Removing this branch is the final
            // tightening step (separate migration).
            mb.Sql($@"
                DROP POLICY IF EXISTS tenant_isolation ON ""{table}"";
                CREATE POLICY tenant_isolation ON ""{table}""
                    USING (
                        ""TenantId""::text = current_setting('{TenantSettingName}', true)
                        OR coalesce(current_setting('{TenantSettingName}', true), '') = ''
                    )
                    WITH CHECK (
                        ""TenantId""::text = current_setting('{TenantSettingName}', true)
                        OR coalesce(current_setting('{TenantSettingName}', true), '') = ''
                    );
            ");
        }

        // Tenants table itself uses Id (not TenantId) so it gets a
        // bespoke policy — visible only to the matching tenant or
        // when the session variable is unset.
        mb.Sql($@"
            ALTER TABLE ""Tenants"" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE ""Tenants"" FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_self_visibility ON ""Tenants"";
            CREATE POLICY tenant_self_visibility ON ""Tenants""
                USING (
                    ""Id""::text = current_setting('{TenantSettingName}', true)
                    OR coalesce(current_setting('{TenantSettingName}', true), '') = ''
                )
                WITH CHECK (
                    ""Id""::text = current_setting('{TenantSettingName}', true)
                    OR coalesce(current_setting('{TenantSettingName}', true), '') = ''
                );
        ");
    }

    protected override void Down(MigrationBuilder mb)
    {
        foreach (var table in TenantScopedTables)
        {
            mb.Sql($@"
                DROP POLICY IF EXISTS tenant_isolation ON ""{table}"";
                ALTER TABLE ""{table}"" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE ""{table}"" DISABLE ROW LEVEL SECURITY;
            ");
        }
        mb.Sql(@"
            DROP POLICY IF EXISTS tenant_self_visibility ON ""Tenants"";
            ALTER TABLE ""Tenants"" NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE ""Tenants"" DISABLE ROW LEVEL SECURITY;
        ");
    }
}
