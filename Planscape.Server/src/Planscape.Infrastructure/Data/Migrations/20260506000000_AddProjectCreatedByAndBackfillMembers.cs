using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// Phase 175 — switch project visibility from "every tenant user sees
/// every project" to a member-based model:
///   1. Tenant Admins / Owners / SecurityOfficers see everything.
///   2. The project author (Project.CreatedById) sees their projects.
///   3. Anyone with an active ProjectMember row sees that project.
///
/// Adds:
///   - Project.CreatedById (nullable Guid)
///   - Index (TenantId, CreatedById) for the visibility predicate.
///
/// Backfill strategy: every existing tenant user is granted an active
/// ProjectMember row for every project in their tenant that they
/// don't already have a row for. This preserves the pre-Phase 175
/// behaviour for legacy projects (everyone in the tenant could see
/// them, so everyone keeps seeing them). Going forward, new projects
/// start private to the author + their invited members. Admins /
/// Owners can prune the legacy memberships via the existing remove
/// endpoint to make a legacy project private.
/// </summary>
public partial class AddProjectCreatedByAndBackfillMembers : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.AddColumn<Guid>(
            name: "CreatedById",
            table: "Projects",
            type: "uuid",
            nullable: true);

        mb.CreateIndex(
            name: "IX_Projects_TenantId_CreatedById",
            table: "Projects",
            columns: new[] { "TenantId", "CreatedById" });

        // gen_random_uuid() needs pgcrypto on PG 12; PG 13+ has it
        // built-in but the IF NOT EXISTS guard is cheap and matches
        // the pattern in BackfillStageGateCriteria.
        mb.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");

        // Phase 175 audit P2-22 — backfill CreatedById to the tenant
        // Owner / first Admin so legacy projects always have a fallback
        // author. Without this, an admin pruning the legacy member
        // backfill (per the docstring above) could orphan a project.
        // Scoped to projects where CreatedById is still NULL so reruns
        // are idempotent.
        mb.Sql(@"
            UPDATE ""Projects"" p
            SET ""CreatedById"" = sub.""UserId""
            FROM (
                SELECT DISTINCT ON (u.""TenantId"") u.""TenantId"", u.""Id"" AS ""UserId""
                FROM ""Users"" u
                WHERE u.""IsActive"" = TRUE
                ORDER BY u.""TenantId"",
                         CASE u.""Role""
                             WHEN 5 THEN 0  -- Owner
                             WHEN 4 THEN 1  -- Admin
                             WHEN 3 THEN 2  -- Manager
                             ELSE 9
                         END,
                         u.""CreatedAt""
            ) AS sub
            WHERE p.""TenantId"" = sub.""TenantId""
              AND p.""CreatedById"" IS NULL;
        ");

        // Phase 175 audit P1-8 — batched backfill. The original single
        // INSERT … SELECT could lock ProjectMembers long enough on a
        // large tenant (1k projects × 500 users = 500k rows) to block
        // live traffic. We chunk by Project so each statement touches
        // at most (users_per_tenant) rows, well under any reasonable
        // lock budget. Idempotent via the unique (ProjectId, UserId)
        // index.
        mb.Sql(@"
            DO $$
            DECLARE
                proj RECORD;
                inserted_total BIGINT := 0;
                inserted_batch BIGINT := 0;
            BEGIN
                FOR proj IN SELECT ""Id"", ""TenantId"" FROM ""Projects"" LOOP
                    INSERT INTO ""ProjectMembers""
                        (""Id"", ""TenantId"", ""ProjectId"", ""UserId"",
                         ""ProjectRole"", ""Iso19650Role"", ""IsActive"",
                         ""JoinedAt"", ""InvitedBy"")
                    SELECT
                        gen_random_uuid(),
                        proj.""TenantId"",
                        proj.""Id"",
                        u.""Id"",
                        'Contributor',
                        COALESCE(NULLIF(u.""Iso19650Role"", ''), 'M'),
                        TRUE,
                        now(),
                        'phase-175-backfill'
                    FROM ""Users"" u
                    WHERE u.""TenantId"" = proj.""TenantId""
                      AND u.""IsActive"" = TRUE
                      AND NOT EXISTS (
                          SELECT 1 FROM ""ProjectMembers"" m
                          WHERE m.""ProjectId"" = proj.""Id""
                            AND m.""UserId"" = u.""Id""
                      );
                    GET DIAGNOSTICS inserted_batch = ROW_COUNT;
                    inserted_total := inserted_total + inserted_batch;
                END LOOP;
                RAISE NOTICE 'Phase 175 backfill: inserted % ProjectMember rows.', inserted_total;
            END$$;
        ");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropIndex(name: "IX_Projects_TenantId_CreatedById", table: "Projects");
        mb.DropColumn(name: "CreatedById", table: "Projects");
        // Note: the backfilled ProjectMember rows are intentionally NOT
        // removed on Down. Membership data is user data; rolling back
        // the schema shouldn't silently delete it. Drop them manually
        // if a clean revert is required.
    }
}
