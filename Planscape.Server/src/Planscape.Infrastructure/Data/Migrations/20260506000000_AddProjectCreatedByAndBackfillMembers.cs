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

        // Backfill: insert an active ProjectMember row for every
        // (project, tenant user) pair that isn't already represented.
        // Defaults: ProjectRole='Contributor', Iso19650Role='M'. The
        // unique (ProjectId, UserId) index ensures idempotency if the
        // migration is replayed.
        mb.Sql(@"
            INSERT INTO ""ProjectMembers""
                (""Id"", ""TenantId"", ""ProjectId"", ""UserId"",
                 ""ProjectRole"", ""Iso19650Role"", ""IsActive"",
                 ""JoinedAt"", ""InvitedBy"")
            SELECT
                gen_random_uuid(),
                p.""TenantId"",
                p.""Id"",
                u.""Id"",
                'Contributor',
                COALESCE(NULLIF(u.""Iso19650Role"", ''), 'M'),
                TRUE,
                now(),
                'phase-175-backfill'
            FROM ""Projects"" p
            JOIN ""Users"" u ON u.""TenantId"" = p.""TenantId"" AND u.""IsActive"" = TRUE
            WHERE NOT EXISTS (
                SELECT 1 FROM ""ProjectMembers"" m
                WHERE m.""ProjectId"" = p.""Id"" AND m.""UserId"" = u.""Id""
            );
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
