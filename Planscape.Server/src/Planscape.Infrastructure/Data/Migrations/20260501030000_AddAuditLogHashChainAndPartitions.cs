using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// S1.8 — turns the audit log into a tamper-evident, partition-friendly
/// structure:
///
///   1. Adds <c>PrevHash</c> + <c>RowHash</c> (SHA-256 hex, 64 chars) to
///      <c>AuditLogs</c>.
///   2. Installs a Postgres BEFORE-INSERT trigger that computes
///      <c>RowHash = sha256(PrevHash || canonical payload)</c> using the
///      most recent row's <c>RowHash</c> as <c>PrevHash</c>. Per-tenant
///      chain (so one tenant's bursts don't reorder another's).
///      Application code can't bypass — the trigger fires on every INSERT.
///   3. Adds a <c>VerifyAuditChain(uuid)</c> SQL function: replays a
///      tenant's chain and returns the first inconsistent row id, or NULL
///      when the chain is intact. Quarterly compliance check just runs
///      <c>SELECT verify_audit_chain('&lt;tenant_uuid&gt;');</c>.
///
/// Partitioning: not declared here. Switching <c>AuditLogs</c> to a partitioned
/// table requires recreating it, which is too disruptive to do in one
/// migration. We instead document the recipe and run it manually during a
/// maintenance window once the table grows past ~10 M rows. Recipe lives in
/// <c>docs/audit-log-partitioning.md</c> (referenced from this migration).
/// </remarks>
public partial class AddAuditLogHashChainAndPartitions : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        // 1. Hash columns.
        mb.AddColumn<string>(
            name: "PrevHash",
            table: "AuditLogs",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);
        mb.AddColumn<string>(
            name: "RowHash",
            table: "AuditLogs",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        // pgcrypto is needed for sha256(); part of postgres-contrib but not
        // enabled by default. CREATE EXTENSION IF NOT EXISTS keeps re-runs safe.
        mb.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");

        // 2. Hash-chain trigger. Reads the previous row's RowHash for the
        //    same tenant (subquery) and computes the new RowHash. Composite
        //    payload is the concatenation of immutable fields so the chain
        //    remains valid even if mutable display fields are later edited.
        mb.Sql(@"
CREATE OR REPLACE FUNCTION audit_log_hash_chain()
RETURNS trigger AS $$
DECLARE
    prev_hash text;
    payload   text;
BEGIN
    SELECT ""RowHash"" INTO prev_hash
    FROM ""AuditLogs""
    WHERE ""TenantId"" = NEW.""TenantId""
    ORDER BY ""Id"" DESC
    LIMIT 1;
    NEW.""PrevHash"" := COALESCE(prev_hash, '0000000000000000000000000000000000000000000000000000000000000000');
    payload :=
        COALESCE(NEW.""TenantId""::text, '') || '|' ||
        COALESCE(NEW.""ProjectId""::text, '') || '|' ||
        COALESCE(NEW.""UserId""::text, '') || '|' ||
        COALESCE(NEW.""Action"", '') || '|' ||
        COALESCE(NEW.""EntityType"", '') || '|' ||
        COALESCE(NEW.""EntityId"", '') || '|' ||
        COALESCE(NEW.""DetailsJson"", '') || '|' ||
        COALESCE(NEW.""IpAddress"", '') || '|' ||
        COALESCE(to_char(NEW.""Timestamp"" AT TIME ZONE 'UTC', 'YYYY-MM-DD\""T\""HH24:MI:SS.US\""Z\""'), '') || '|' ||
        COALESCE(NEW.""DeviceId"", '') || '|' ||
        COALESCE(NEW.""Source"", '');
    NEW.""RowHash"" := encode(digest(NEW.""PrevHash"" || '|' || payload, 'sha256'), 'hex');
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS audit_log_hash_chain_trigger ON ""AuditLogs"";
CREATE TRIGGER audit_log_hash_chain_trigger
    BEFORE INSERT ON ""AuditLogs""
    FOR EACH ROW EXECUTE FUNCTION audit_log_hash_chain();
");

        // 3. Verify function. Returns the Id of the first broken row for a
        //    tenant, or NULL when the chain is intact. O(N) per tenant; run
        //    quarterly during a maintenance window.
        mb.Sql(@"
CREATE OR REPLACE FUNCTION verify_audit_chain(tid uuid)
RETURNS bigint AS $$
DECLARE
    expected_prev text := '0000000000000000000000000000000000000000000000000000000000000000';
    rec record;
BEGIN
    FOR rec IN
        SELECT ""Id"", ""PrevHash"", ""RowHash""
        FROM ""AuditLogs""
        WHERE ""TenantId"" = tid
        ORDER BY ""Id"" ASC
    LOOP
        IF rec.""PrevHash"" IS DISTINCT FROM expected_prev THEN
            RETURN rec.""Id"";
        END IF;
        expected_prev := rec.""RowHash"";
    END LOOP;
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;
");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.Sql("DROP TRIGGER IF EXISTS audit_log_hash_chain_trigger ON \"AuditLogs\";");
        mb.Sql("DROP FUNCTION IF EXISTS audit_log_hash_chain();");
        mb.Sql("DROP FUNCTION IF EXISTS verify_audit_chain(uuid);");
        mb.DropColumn(name: "RowHash",  table: "AuditLogs");
        mb.DropColumn(name: "PrevHash", table: "AuditLogs");
    }
}
