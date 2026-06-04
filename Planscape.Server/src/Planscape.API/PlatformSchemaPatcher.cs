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
            ""MetadataJson"" text,
            ""ServiceIntervalHours"" double precision,
            ""LastServiceRunHours"" double precision)",
        @"ALTER TABLE ""DeviceTwins"" ADD COLUMN IF NOT EXISTS ""ServiceIntervalHours"" double precision",
        @"ALTER TABLE ""DeviceTwins"" ADD COLUMN IF NOT EXISTS ""LastServiceRunHours"" double precision",
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

        // ── BLK-4 — tables configured in OnModelCreating but with NO migration
        //    AND previously absent from this patcher, so on a pre-existing prod
        //    DB (where EnsureCreated short-circuits) the first write threw
        //    'relation does not exist'. HVAC engine snapshots, ArchiCAD event
        //    log, and the cross-host GlobalId registry (written by the ArchiCAD
        //    IFC ingest path) are now materialised here too.
        @"CREATE TABLE IF NOT EXISTS ""HvacLoadSnapshot""(
            ""Id"" uuid PRIMARY KEY,
            ""TenantId"" uuid NOT NULL,
            ""ProjectId"" uuid NOT NULL,
            ""SystemId"" text NOT NULL DEFAULT '',
            ""ClimateSiteId"" text NOT NULL DEFAULT '',
            ""ClimateSiteLabel"" text NOT NULL DEFAULT '',
            ""ConstructionProfile"" text NOT NULL DEFAULT '',
            ""RtsClass"" text NOT NULL DEFAULT 'Reactive',
            ""Cooling"" boolean NOT NULL DEFAULT true,
            ""BlockSensibleW"" double precision NOT NULL DEFAULT 0,
            ""BlockLatentW"" double precision NOT NULL DEFAULT 0,
            ""BlockHour"" integer NOT NULL DEFAULT 0,
            ""SumOfPeaksSensibleW"" double precision NOT NULL DEFAULT 0,
            ""DiversityFactor"" double precision NOT NULL DEFAULT 0,
            ""ZoneCount"" integer NOT NULL DEFAULT 0,
            ""ZonesJson"" text NOT NULL DEFAULT '',
            ""CapturedAt"" timestamp with time zone NOT NULL DEFAULT now(),
            ""CapturedBy"" text NOT NULL DEFAULT '',
            ""Source"" text NOT NULL DEFAULT 'PLUGIN')",
        @"CREATE INDEX IF NOT EXISTS ""IX_HvacLoadSnapshots_Project_CapturedAt"" ON ""HvacLoadSnapshot""(""ProjectId"", ""CapturedAt"")",
        @"CREATE INDEX IF NOT EXISTS ""IX_HvacLoadSnapshots_Project_System"" ON ""HvacLoadSnapshot""(""ProjectId"", ""SystemId"")",
        @"CREATE TABLE IF NOT EXISTS ""HvacNcSnapshot""(
            ""Id"" uuid PRIMARY KEY,
            ""TenantId"" uuid NOT NULL,
            ""ProjectId"" uuid NOT NULL,
            ""PathLabel"" text NOT NULL DEFAULT '',
            ""ReceiverRoom"" text NOT NULL DEFAULT '',
            ""PredictedNc"" integer NOT NULL DEFAULT 0,
            ""TargetNc"" integer NOT NULL DEFAULT 0,
            ""PathFlowLs"" double precision NOT NULL DEFAULT 0,
            ""PathPressureDropPa"" double precision NOT NULL DEFAULT 0,
            ""OctaveLpJson"" text NOT NULL DEFAULT '',
            ""ElementBreakdownJson"" text NOT NULL DEFAULT '',
            ""CapturedAt"" timestamp with time zone NOT NULL DEFAULT now(),
            ""CapturedBy"" text NOT NULL DEFAULT '')",
        @"CREATE INDEX IF NOT EXISTS ""IX_HvacNcSnapshots_Project_PredictedNc"" ON ""HvacNcSnapshot""(""ProjectId"", ""PredictedNc"")",
        @"CREATE TABLE IF NOT EXISTS ""HvacRefrigerantSizing""(
            ""Id"" uuid PRIMARY KEY,
            ""TenantId"" uuid NOT NULL,
            ""ProjectId"" uuid NOT NULL,
            ""RefrigerantId"" text NOT NULL DEFAULT '',
            ""Leg"" text NOT NULL DEFAULT '',
            ""CapacityKw"" double precision NOT NULL DEFAULT 0,
            ""EquivLengthM"" double precision NOT NULL DEFAULT 0,
            ""LiftM"" double precision NOT NULL DEFAULT 0,
            ""HasVerticalRiser"" boolean NOT NULL DEFAULT false,
            ""MaxPressureDropKpa"" double precision NOT NULL DEFAULT 0,
            ""SubcoolingReserveK"" double precision NOT NULL DEFAULT 0,
            ""Ok"" boolean NOT NULL DEFAULT false,
            ""SelectedBoreMm"" double precision NOT NULL DEFAULT 0,
            ""VelocityMs"" double precision NOT NULL DEFAULT 0,
            ""PressureDropKpa"" double precision NOT NULL DEFAULT 0,
            ""LiftPenaltyKpa"" double precision NOT NULL DEFAULT 0,
            ""SatTempDropK"" double precision NOT NULL DEFAULT 0,
            ""WarningsJson"" text NOT NULL DEFAULT '',
            ""CapturedAt"" timestamp with time zone NOT NULL DEFAULT now(),
            ""CapturedBy"" text NOT NULL DEFAULT '')",
        @"CREATE INDEX IF NOT EXISTS ""IX_HvacRefrigerantSizings_Project_Refrigerant"" ON ""HvacRefrigerantSizing""(""ProjectId"", ""RefrigerantId"")",
        @"CREATE TABLE IF NOT EXISTS ""ArchiCADEventLogs"" (
            ""Id"" uuid PRIMARY KEY,
            ""TenantId"" uuid NOT NULL,
            ""ProjectId"" uuid NOT NULL,
            ""Kind"" text NOT NULL DEFAULT 'Changed',
            ""ElementId"" text NOT NULL DEFAULT '',
            ""ElementType"" text NOT NULL DEFAULT '',
            ""IfcGlobalId"" text,
            ""PropertiesJson"" text,
            ""EventTimestampUtc"" timestamp with time zone NOT NULL DEFAULT now(),
            ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT now())",
        @"CREATE INDEX IF NOT EXISTS ""IX_ArchiCADEventLogs_Project_Created"" ON ""ArchiCADEventLogs"" (""ProjectId"", ""CreatedAt"")",
        @"CREATE TABLE IF NOT EXISTS ""GlobalIdRegistry"" (
            ""Id"" uuid PRIMARY KEY,
            ""TenantId"" uuid NOT NULL,
            ""ProjectId"" uuid NOT NULL,
            ""IfcGlobalId"" text,
            ""ArchiCadGuid"" text,
            ""RevitUniqueId"" text,
            ""TeklaGuid"" text,
            ""Discipline"" text,
            ""IfcType"" text,
            ""ElementName"" text,
            ""NormalizedLevelName"" text,
            ""MappingStatus"" text NOT NULL DEFAULT 'AutoMatched',
            ""MappedBy"" text,
            ""Notes"" text,
            ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
            ""UpdatedAt"" timestamp with time zone)",
        @"CREATE INDEX IF NOT EXISTS ""IX_GlobalIdRegistry_Project_Ifc"" ON ""GlobalIdRegistry"" (""ProjectId"", ""IfcGlobalId"")",

        // ── H-3 — duplicate model rows: unique filtered index so the DB rejects
        //    concurrent same-geometry uploads on pre-existing prod DBs too.
        //    Wrapped non-fatally in ApplyAsync (a DB with pre-existing dupes
        //    can't build the unique index until they're merged).
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ProjectModels_Tenant_Project_Hash""
            ON ""ProjectModels"" (""TenantId"", ""ProjectId"", ""ContentHash"") WHERE ""DeletedAt"" IS NULL",

        // ── Pre-merge Gate 2 — cross-host + coordination tables ──
        //    EF entities (configured in OnModelCreating, in db.Model) but with
        //    NO applicable EF migration. On a fresh DB EnsureCreated/CreateTables
        //    materialises them; on a PRE-EXISTING DB (EnsureCreated short-circuits
        //    once Tenants exists) the first write threw 'relation does not exist'.
        //    DDL mirrors exactly what EF's RelationalDatabaseCreator emits
        //    (verified via pg_dump of a CreateTables DB). FK constraints are
        //    intentionally omitted — these are remediation creates for existing
        //    DBs and the table existing + accepting writes is what matters; the
        //    SchemaDriftChecker (below, run after this patcher) asserts the
        //    column set matches the EF model so silent drift is impossible.

        // ExternalElementMapping — the cross-host IFC-GlobalId ↔ host-element-id
        // registry this branch is built around (BLK-1 / H-1 / BLK-3).
        @"CREATE TABLE IF NOT EXISTS ""ExternalElementMappings"" (
            ""Id"" uuid PRIMARY KEY,
            ""TenantId"" uuid NOT NULL,
            ""ProjectId"" uuid NOT NULL,
            ""IfcGlobalId"" character varying(22) NOT NULL DEFAULT '',
            ""Host"" character varying(20) NOT NULL DEFAULT '',
            ""HostElementId"" character varying(200) NOT NULL DEFAULT '',
            ""HostDocumentGuid"" character varying(64),
            ""HostDisplayLabel"" text,
            ""FirstSeenUtc"" timestamp with time zone NOT NULL DEFAULT now(),
            ""LastSeenUtc"" timestamp with time zone NOT NULL DEFAULT now(),
            ""IngestionCount"" integer NOT NULL DEFAULT 1)",
        @"CREATE INDEX IF NOT EXISTS ""IX_ExternalElementMappings_TenantId"" ON ""ExternalElementMappings"" (""TenantId"")",
        @"CREATE INDEX IF NOT EXISTS ""IX_ExternalElementMappings_ProjectId_IfcGlobalId"" ON ""ExternalElementMappings"" (""ProjectId"", ""IfcGlobalId"")",
        @"CREATE INDEX IF NOT EXISTS ""IX_ExternalElementMappings_ProjectId_Host_HostElementId"" ON ""ExternalElementMappings"" (""ProjectId"", ""Host"", ""HostElementId"")",
        // Composite unique. Name reproduces EF's 63-char identifier truncation
        // (…HostDocu~) so a fresh-EF DB's IF NOT EXISTS short-circuits exactly.
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ExternalElementMappings_ProjectId_IfcGlobalId_Host_HostDocu~"" ON ""ExternalElementMappings"" (""ProjectId"", ""IfcGlobalId"", ""Host"", ""HostDocumentGuid"")",

        // IdempotencyRecord — offline-replay dedupe (Prompt 18; branch-era migration 20260602).
        @"CREATE TABLE IF NOT EXISTS ""IdempotencyRecords"" (
            ""Id"" uuid PRIMARY KEY,
            ""TenantId"" uuid NOT NULL,
            ""Scope"" text NOT NULL DEFAULT '',
            ""Key"" text NOT NULL DEFAULT '',
            ""ResultId"" uuid NOT NULL,
            ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT now())",
        @"CREATE INDEX IF NOT EXISTS ""IX_IdempotencyRecords_TenantId"" ON ""IdempotencyRecords"" (""TenantId"")",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_IdempotencyRecords_TenantId_Scope_Key"" ON ""IdempotencyRecords"" (""TenantId"", ""Scope"", ""Key"")",

        // ClashRecords — coordination clash store (clash kernel push target).
        @"CREATE TABLE IF NOT EXISTS ""ClashRecords"" (
            ""Id"" uuid PRIMARY KEY,
            ""TenantId"" uuid NOT NULL,
            ""ProjectId"" uuid NOT NULL,
            ""ClashHash"" character varying(64) NOT NULL DEFAULT '',
            ""Kind"" integer NOT NULL DEFAULT 0,
            ""Severity"" integer NOT NULL DEFAULT 0,
            ""Status"" integer NOT NULL DEFAULT 0,
            ""ModelAId"" uuid NOT NULL,
            ""ElementAGuid"" character varying(80) NOT NULL DEFAULT '',
            ""ElementAName"" character varying(400),
            ""ElementAType"" character varying(80),
            ""DisciplineA"" character varying(8),
            ""ModelBId"" uuid NOT NULL,
            ""ElementBGuid"" character varying(80) NOT NULL DEFAULT '',
            ""ElementBName"" character varying(400),
            ""ElementBType"" character varying(80),
            ""DisciplineB"" character varying(8),
            ""DistanceMm"" double precision NOT NULL DEFAULT 0,
            ""CentreX"" double precision NOT NULL DEFAULT 0,
            ""CentreY"" double precision NOT NULL DEFAULT 0,
            ""CentreZ"" double precision NOT NULL DEFAULT 0,
            ""OverlapVolumeMm3"" double precision NOT NULL DEFAULT 0,
            ""LevelCode"" character varying(40),
            ""ZoneCode"" character varying(40),
            ""AssignedTo"" character varying(200),
            ""ResolutionNote"" character varying(2000),
            ""IssueId"" uuid,
            ""BcfTopicGuid"" character varying(80),
            ""DetectedAt"" timestamp with time zone NOT NULL DEFAULT now(),
            ""AcknowledgedAt"" timestamp with time zone,
            ""ResolvedAt"" timestamp with time zone,
            ""ClosedAt"" timestamp with time zone,
            ""DetectedByJobId"" character varying(80))",
        @"CREATE INDEX IF NOT EXISTS ""IX_ClashRecords_TenantId"" ON ""ClashRecords"" (""TenantId"")",
        @"CREATE INDEX IF NOT EXISTS ""IX_ClashRecords_IssueId"" ON ""ClashRecords"" (""IssueId"")",
        @"CREATE INDEX IF NOT EXISTS ""IX_ClashRecords_ProjectId_Status"" ON ""ClashRecords"" (""ProjectId"", ""Status"")",
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ClashRecords_ProjectId_ClashHash"" ON ""ClashRecords"" (""ProjectId"", ""ClashHash"")",

        // IfcAlignmentReports — cross-host federation alignment audit (BLK-2 sibling).
        @"CREATE TABLE IF NOT EXISTS ""IfcAlignmentReports"" (
            ""Id"" uuid PRIMARY KEY,
            ""TenantId"" uuid NOT NULL,
            ""ProjectId"" uuid NOT NULL,
            ""ProjectModelId"" uuid NOT NULL,
            ""SchemaVersion"" text,
            ""IfcSiteGuid"" text,
            ""LengthUnit"" text,
            ""TrueNorthDegrees"" double precision,
            ""SurveyEasting"" double precision,
            ""SurveyNorthing"" double precision,
            ""SurveyElevation"" double precision,
            ""HasMapConversion"" boolean NOT NULL DEFAULT false,
            ""HasProjectedCrs"" boolean NOT NULL DEFAULT false,
            ""CrsName"" text,
            ""MapConversionScale"" double precision,
            ""MapConversionRotationDeg"" double precision,
            ""GeometryCentroidX"" double precision,
            ""GeometryCentroidY"" double precision,
            ""GeometryCentroidZ"" double precision,
            ""Verdict"" text NOT NULL DEFAULT '',
            ""FindingsJson"" text NOT NULL DEFAULT '',
            ""ValidatedAt"" timestamp with time zone NOT NULL DEFAULT now())",
        @"CREATE INDEX IF NOT EXISTS ""IX_IfcAlignmentReports_TenantId"" ON ""IfcAlignmentReports"" (""TenantId"")",
        @"CREATE INDEX IF NOT EXISTS ""IX_IfcAlignmentReports_ProjectId_ProjectModelId"" ON ""IfcAlignmentReports"" (""ProjectId"", ""ProjectModelId"")",
        @"CREATE INDEX IF NOT EXISTS ""IX_IfcAlignmentReports_ProjectId_ValidatedAt"" ON ""IfcAlignmentReports"" (""ProjectId"", ""ValidatedAt"")",
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
