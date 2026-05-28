using System.Data.Common;

namespace Planscape.API;

/// <summary>
/// Idempotent CREATE-TABLE patcher for the Phase 189 entities (platform event
/// spine, meeting viewer, digital twin). This codebase materialises schema via
/// EnsureCreated/CreateTables in dev and acknowledges the EF migration set is
/// incomplete (see Program.cs), so — exactly like PatchDevSchemaAsync does for
/// new COLUMNS — this adds the new TABLES with CREATE TABLE IF NOT EXISTS so
/// they exist on pre-existing databases too. PascalCase quoted identifiers +
/// Postgres types match what OnModelCreating/EF produce.
///
/// The canonical schema source remains EF migrations: once the model is stable
/// run `dotnet ef migrations add Phase189Platform` and drop this patcher.
/// </summary>
internal static class PlatformSchemaPatcher
{
    private static readonly string[] Statements =
    {
        // ── K2 — PlatformEvents ──
        @"CREATE TABLE IF NOT EXISTS ""PlatformEvents"" (
            ""Id"" uuid PRIMARY KEY,
            ""TenantId"" uuid NOT NULL,
            ""ProjectId"" uuid NOT NULL,
            ""Sequence"" bigint NOT NULL,
            ""Source"" text NOT NULL DEFAULT '',
            ""Type"" text NOT NULL DEFAULT '',
            ""PayloadJson"" text NOT NULL DEFAULT '{}',
            ""TargetIfcGlobalId"" text,
            ""BaseRevisionId"" text,
            ""Status"" integer NOT NULL DEFAULT 0,
            ""StatusDetail"" text,
            ""Attempts"" integer NOT NULL DEFAULT 0,
            ""ActorUserId"" uuid,
            ""CreatedUtc"" timestamp with time zone NOT NULL DEFAULT now(),
            ""AppliedUtc"" timestamp with time zone,
            ""PrevHash"" text,
            ""RowHash"" text)",
        // Idempotent column add for DBs created before Attempts existed.
        @"ALTER TABLE ""PlatformEvents"" ADD COLUMN IF NOT EXISTS ""Attempts"" integer NOT NULL DEFAULT 0",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PlatformEvents_Project_Seq"" ON ""PlatformEvents"" (""ProjectId"", ""Sequence"")",
        @"CREATE INDEX IF NOT EXISTS ""IX_PlatformEvents_Project_Status_Seq"" ON ""PlatformEvents"" (""ProjectId"", ""Status"", ""Sequence"")",

        // ── Pillar A — MeetingSessions / participants / snapshots ──
        @"CREATE TABLE IF NOT EXISTS ""MeetingSessions"" (
            ""Id"" uuid PRIMARY KEY,
            ""TenantId"" uuid NOT NULL,
            ""ProjectId"" uuid NOT NULL,
            ""MeetingId"" uuid,
            ""HostUserId"" uuid,
            ""ModelId"" uuid,
            ""BaseRevisionId"" text,
            ""Status"" text NOT NULL DEFAULT 'ACTIVE',
            ""CreatedBy"" text NOT NULL DEFAULT '',
            ""CreatedByUserId"" uuid,
            ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
            ""EndedAt"" timestamp with time zone)",
        @"CREATE INDEX IF NOT EXISTS ""IX_MeetingSessions_Project_Status"" ON ""MeetingSessions"" (""ProjectId"", ""Status"")",
        @"CREATE TABLE IF NOT EXISTS ""MeetingViewerParticipants"" (
            ""Id"" uuid PRIMARY KEY,
            ""TenantId"" uuid NOT NULL,
            ""SessionId"" uuid NOT NULL,
            ""UserId"" uuid NOT NULL,
            ""DisplayName"" text NOT NULL DEFAULT '',
            ""IsHost"" boolean NOT NULL DEFAULT false,
            ""IsFollowingHost"" boolean NOT NULL DEFAULT false,
            ""Surface"" text NOT NULL DEFAULT '',
            ""JoinedAt"" timestamp with time zone NOT NULL DEFAULT now(),
            ""LastSeenAt"" timestamp with time zone NOT NULL DEFAULT now(),
            ""LeftAt"" timestamp with time zone)",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_MeetingParticipants_Session_User"" ON ""MeetingViewerParticipants"" (""SessionId"", ""UserId"")",
        @"CREATE TABLE IF NOT EXISTS ""MeetingSnapshots"" (
            ""Id"" uuid PRIMARY KEY,
            ""TenantId"" uuid NOT NULL,
            ""ProjectId"" uuid NOT NULL,
            ""SessionId"" uuid NOT NULL,
            ""Label"" text NOT NULL DEFAULT '',
            ""StateJson"" text NOT NULL DEFAULT '{}',
            ""CapturedBy"" text NOT NULL DEFAULT '',
            ""CapturedByUserId"" uuid,
            ""CapturedAt"" timestamp with time zone NOT NULL DEFAULT now())",
        @"CREATE INDEX IF NOT EXISTS ""IX_MeetingSnapshots_Session_At"" ON ""MeetingSnapshots"" (""SessionId"", ""CapturedAt"")",

        // ── Pillar B — DeviceTwins / Telemetry / Rules / Alerts / WorkOrders ──
        @"CREATE TABLE IF NOT EXISTS ""DeviceTwins"" (
            ""Id"" uuid PRIMARY KEY,
            ""TenantId"" uuid NOT NULL,
            ""ProjectId"" uuid NOT NULL,
            ""DeviceId"" text NOT NULL DEFAULT '',
            ""IfcGlobalId"" text,
            ""Protocol"" text NOT NULL DEFAULT 'mqtt',
            ""AssetTag"" text,
            ""Serial"" text,
            ""Manufacturer"" text,
            ""Model"" text,
            ""HealthState"" text NOT NULL DEFAULT 'UNKNOWN',
            ""LastStateJson"" text,
            ""LastSeenAt"" timestamp with time zone,
            ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
            ""MetadataJson"" text)",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_DeviceTwins_Project_Device"" ON ""DeviceTwins"" (""ProjectId"", ""DeviceId"")",
        @"CREATE INDEX IF NOT EXISTS ""IX_DeviceTwins_Project_Health"" ON ""DeviceTwins"" (""ProjectId"", ""HealthState"")",
        @"CREATE INDEX IF NOT EXISTS ""IX_DeviceTwins_Project_Guid"" ON ""DeviceTwins"" (""ProjectId"", ""IfcGlobalId"")",

        @"CREATE TABLE IF NOT EXISTS ""TelemetryPoints"" (
            ""Id"" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            ""TenantId"" uuid NOT NULL,
            ""ProjectId"" uuid NOT NULL,
            ""DeviceId"" text NOT NULL DEFAULT '',
            ""Metric"" text NOT NULL DEFAULT '',
            ""Value"" double precision NOT NULL DEFAULT 0,
            ""Unit"" text,
            ""Ts"" timestamp with time zone NOT NULL DEFAULT now())",
        @"CREATE INDEX IF NOT EXISTS ""IX_TelemetryPoints_Project_Device_Metric_Ts"" ON ""TelemetryPoints"" (""ProjectId"", ""DeviceId"", ""Metric"", ""Ts"")",

        @"CREATE TABLE IF NOT EXISTS ""TwinRules"" (
            ""Id"" uuid PRIMARY KEY,
            ""TenantId"" uuid NOT NULL,
            ""ProjectId"" uuid NOT NULL,
            ""Name"" text NOT NULL DEFAULT '',
            ""DeviceId"" text,
            ""Metric"" text NOT NULL DEFAULT '',
            ""Operator"" text NOT NULL DEFAULT 'gt',
            ""Threshold"" double precision,
            ""AnomalySigma"" double precision NOT NULL DEFAULT 3.0,
            ""Severity"" text NOT NULL DEFAULT 'WARNING',
            ""Enabled"" boolean NOT NULL DEFAULT true,
            ""RaiseWorkOrder"" boolean NOT NULL DEFAULT false,
            ""ConsecutiveBreaches"" integer NOT NULL DEFAULT 1,
            ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT now())",
        @"CREATE INDEX IF NOT EXISTS ""IX_TwinRules_Project_Metric_Enabled"" ON ""TwinRules"" (""ProjectId"", ""Metric"", ""Enabled"")",

        @"CREATE TABLE IF NOT EXISTS ""TwinAlerts"" (
            ""Id"" uuid PRIMARY KEY,
            ""TenantId"" uuid NOT NULL,
            ""ProjectId"" uuid NOT NULL,
            ""RuleId"" uuid,
            ""DeviceId"" text NOT NULL DEFAULT '',
            ""IfcGlobalId"" text,
            ""Metric"" text NOT NULL DEFAULT '',
            ""Value"" double precision NOT NULL DEFAULT 0,
            ""Severity"" text NOT NULL DEFAULT 'WARNING',
            ""Message"" text NOT NULL DEFAULT '',
            ""Status"" text NOT NULL DEFAULT 'OPEN',
            ""FiredAt"" timestamp with time zone NOT NULL DEFAULT now(),
            ""AcknowledgedAt"" timestamp with time zone,
            ""AcknowledgedByUserId"" uuid,
            ""ResolvedAt"" timestamp with time zone)",
        @"CREATE INDEX IF NOT EXISTS ""IX_TwinAlerts_Project_Status_Fired"" ON ""TwinAlerts"" (""ProjectId"", ""Status"", ""FiredAt"")",
        @"CREATE INDEX IF NOT EXISTS ""IX_TwinAlerts_Project_Device"" ON ""TwinAlerts"" (""ProjectId"", ""DeviceId"")",

        @"CREATE TABLE IF NOT EXISTS ""WorkOrders"" (
            ""Id"" uuid PRIMARY KEY,
            ""TenantId"" uuid NOT NULL,
            ""ProjectId"" uuid NOT NULL,
            ""Code"" text NOT NULL DEFAULT '',
            ""DeviceTwinId"" uuid,
            ""IfcGlobalId"" text,
            ""AlertId"" uuid,
            ""Title"" text NOT NULL DEFAULT '',
            ""Description"" text,
            ""Priority"" text NOT NULL DEFAULT 'MEDIUM',
            ""Status"" text NOT NULL DEFAULT 'OPEN',
            ""Source"" text NOT NULL DEFAULT 'manual',
            ""AssigneeUserId"" uuid,
            ""DueDate"" timestamp with time zone,
            ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
            ""CompletedAt"" timestamp with time zone,
            ""CompletionNotes"" text)",
        @"CREATE INDEX IF NOT EXISTS ""IX_WorkOrders_Project_Status"" ON ""WorkOrders"" (""ProjectId"", ""Status"")",
        @"CREATE INDEX IF NOT EXISTS ""IX_WorkOrders_Project_Device"" ON ""WorkOrders"" (""ProjectId"", ""DeviceTwinId"")",
    };

    public static async Task ApplyAsync(DbConnection conn)
    {
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        int ok = 0, failed = 0;
        foreach (var sql in Statements)
        {
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();
                ok++;
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"[platform-schema] FAILED: {ex.Message}");
            }
        }
        Console.WriteLine($"[platform-schema] done — {ok} ok, {failed} failed");
    }
}
