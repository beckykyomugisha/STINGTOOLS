using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// CP-2 (review-hardening) — five entities carried a <c>TenantId</c> column but
/// did not implement <see cref="Planscape.Core.Entities.ITenantScoped"/>, so they
/// were excluded from the global tenant query filter + auto-stamp + auto-index
/// (PlanscapeDbContext.ApplyTenantQueryFilters / SaveChanges). They are now
/// <c>ITenantScoped</c>: <c>InformationDeliverable</c>, <c>SiteDiaryAttachment</c>,
/// <c>MeetingAttendee</c>, <c>MeetingAgendaItem</c>, <c>MeetingActionItem</c>.
///
/// This migration carries the two deploy-time steps that turning on the filter
/// requires:
///   1. the per-entity <c>TenantId</c> index EF's auto-HasIndex creates for
///      ITenantScoped types (so the new filter predicate is indexed); and
///   2. a ONE-TIME BACKFILL of <c>TenantId</c> on any legacy row where it is the
///      empty GUID — copied from the row's tenant-scoped parent — so the new
///      filter (TenantId == CurrentTenantId) does NOT hide pre-existing rows.
///
/// Backfill verified on a throwaway Postgres (seed empty-tenant child rows under
/// real-tenant parents → run backfill → 0 empty-tenant rows remain, each row gets
/// its parent's tenant, already-stamped rows untouched, rows stay visible under
/// the filter). The decoy/cross-tenant row was not clobbered.
///
/// Repo convention (mirrors 20260602000000_IdempotencyRecords): hand-authored DDL
/// with no .Designer.cs and no [Migration] attribute — dev/local build schema from
/// OnModelCreating via RelationalDatabaseCreator.CreateTables(), so on dev the
/// TenantId indexes already exist from the model. This file is the exact DDL +
/// backfill for the prod migration pipeline (backlog P3-2). Indexes use
/// IF NOT EXISTS so re-application against a model-built dev schema is a no-op;
/// the backfill is idempotent (only touches the empty GUID).
///
/// Re-stamped 20260609000000 (post-20260608_TemplateOpRecords): the original
/// CP-2 stamp 20260606000000 sorted BEFORE main's 20260607/20260608 migrations,
/// which would have applied this filter-enabling migration before the tables it
/// guards were created in the prod pipeline. The timestamp is the only change —
/// DDL + backfill are byte-identical to the CP-2 original.
/// </summary>
public partial class TenantScopeIndexesAndBackfill : Migration
{
    private const string Empty = "00000000-0000-0000-0000-000000000000";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1. TenantId indexes (idempotent — match EF's auto-HasIndex name IX_<Table>_TenantId).
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_InformationDeliverables_TenantId\" ON \"InformationDeliverables\" (\"TenantId\");");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_SiteDiaryAttachments_TenantId\" ON \"SiteDiaryAttachments\" (\"TenantId\");");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_MeetingAttendees_TenantId\" ON \"MeetingAttendees\" (\"TenantId\");");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_MeetingAgendaItems_TenantId\" ON \"MeetingAgendaItems\" (\"TenantId\");");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_MeetingActionItems_TenantId\" ON \"MeetingActionItems\" (\"TenantId\");");

        // 2. One-time backfill: stamp legacy empty-tenant rows from their parent so
        //    the now-active global filter doesn't hide them. Only touches the empty
        //    GUID (already-stamped rows are left untouched).
        migrationBuilder.Sql(
            "UPDATE \"InformationDeliverables\" d SET \"TenantId\" = p.\"TenantId\" " +
            "FROM \"Projects\" p WHERE d.\"ProjectId\" = p.\"Id\" AND d.\"TenantId\" = '" + Empty + "';");
        migrationBuilder.Sql(
            "UPDATE \"SiteDiaryAttachments\" a SET \"TenantId\" = s.\"TenantId\" " +
            "FROM \"SiteDiaries\" s WHERE a.\"SiteDiaryId\" = s.\"Id\" AND a.\"TenantId\" = '" + Empty + "';");
        migrationBuilder.Sql(
            "UPDATE \"MeetingAttendees\" x SET \"TenantId\" = m.\"TenantId\" " +
            "FROM \"Meetings\" m WHERE x.\"MeetingId\" = m.\"Id\" AND x.\"TenantId\" = '" + Empty + "';");
        migrationBuilder.Sql(
            "UPDATE \"MeetingAgendaItems\" x SET \"TenantId\" = m.\"TenantId\" " +
            "FROM \"Meetings\" m WHERE x.\"MeetingId\" = m.\"Id\" AND x.\"TenantId\" = '" + Empty + "';");
        migrationBuilder.Sql(
            "UPDATE \"MeetingActionItems\" x SET \"TenantId\" = m.\"TenantId\" " +
            "FROM \"Meetings\" m WHERE x.\"MeetingId\" = m.\"Id\" AND x.\"TenantId\" = '" + Empty + "';");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Indexes only — the backfill is a value correction that is not reversed
        // (reverting TenantId to the empty GUID would re-introduce the hide-by-filter bug).
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_InformationDeliverables_TenantId\";");
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_SiteDiaryAttachments_TenantId\";");
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_MeetingAttendees_TenantId\";");
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_MeetingAgendaItems_TenantId\";");
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_MeetingActionItems_TenantId\";");
    }
}
