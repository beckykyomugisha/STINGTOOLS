using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// Migration: UserSoftDeleteTenantAuditIndexes (2026-05-13)
///
/// Applies the following schema changes:
///   F1 — Drop global IX_Users_Email unique index; add composite
///         IX_Users_TenantId_Email unique index (email is per-tenant, not global).
///   F2 — Add IsDeleted / DeletedAt / DeletedByUserId soft-delete columns to
///         AppUsers so user rows are never hard-deleted while audit / membership
///         data still references them.
///   F3 — Add UpdatedAt column to Tenants, auto-stamped by SaveChangesAsync.
///   F4 — Add composite index (ProjectId, AssigneeUserId) on BimIssues for
///         assignee-filtered list queries and SlaEscalationJob push path.
///         (ProjectId, Status) index already existed from InitialCreate.
///   F5 — Alter FK on ProjectMembers.UserId and DevicePushTokens.UserId from
///         CASCADE to RESTRICT so the soft-delete workflow is enforced before
///         hard-removing a user.
/// </summary>
public partial class UserSoftDeleteTenantAuditIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── F2 — Add soft-delete columns to AppUsers ─────────────────────────
        migrationBuilder.AddColumn<bool>(
            name: "IsDeleted",
            table: "Users",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "DeletedAt",
            table: "Users",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DeletedByUserId",
            table: "Users",
            type: "text",
            nullable: true);

        // ── F1 — Replace global email uniqueness with per-tenant uniqueness ──
        // Drop the old single-column unique index created by InitialCreate.
        migrationBuilder.DropIndex(
            name: "IX_Users_Email",
            table: "Users");

        // Add the composite unique index: a consultant may hold accounts on
        // multiple tenants with the same email address.
        migrationBuilder.CreateIndex(
            name: "IX_Users_TenantId_Email",
            table: "Users",
            columns: new[] { "TenantId", "Email" },
            unique: true);

        // ── F3 — Add UpdatedAt column to Tenants ─────────────────────────────
        migrationBuilder.AddColumn<DateTime>(
            name: "UpdatedAt",
            table: "Tenants",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "now()");

        // ── F4 — Add composite index (ProjectId, AssigneeUserId) on Issues ───
        // Used by the issues list endpoint when filtering by assignee, and by
        // SlaEscalationJob's push-to-assignee path.
        // (ProjectId, Status) already exists from InitialCreate — no-op here.
        migrationBuilder.CreateIndex(
            name: "IX_Issues_ProjectId_AssigneeUserId",
            table: "Issues",
            columns: new[] { "ProjectId", "AssigneeUserId" });

        // ── F5 — Change ProjectMembers.UserId FK from CASCADE to RESTRICT ────
        migrationBuilder.DropForeignKey(
            name: "FK_ProjectMembers_Users_UserId",
            table: "ProjectMembers");

        migrationBuilder.AddForeignKey(
            name: "FK_ProjectMembers_Users_UserId",
            table: "ProjectMembers",
            column: "UserId",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        // ── F5 — Change DevicePushTokens.UserId FK from CASCADE to RESTRICT ──
        migrationBuilder.DropForeignKey(
            name: "FK_DevicePushTokens_Users_UserId",
            table: "DevicePushTokens");

        migrationBuilder.AddForeignKey(
            name: "FK_DevicePushTokens_Users_UserId",
            table: "DevicePushTokens",
            column: "UserId",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // ── F5 — Revert DevicePushTokens FK back to CASCADE ──────────────────
        migrationBuilder.DropForeignKey(
            name: "FK_DevicePushTokens_Users_UserId",
            table: "DevicePushTokens");

        migrationBuilder.AddForeignKey(
            name: "FK_DevicePushTokens_Users_UserId",
            table: "DevicePushTokens",
            column: "UserId",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        // ── F5 — Revert ProjectMembers FK back to CASCADE ─────────────────────
        migrationBuilder.DropForeignKey(
            name: "FK_ProjectMembers_Users_UserId",
            table: "ProjectMembers");

        migrationBuilder.AddForeignKey(
            name: "FK_ProjectMembers_Users_UserId",
            table: "ProjectMembers",
            column: "UserId",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        // ── F4 — Drop composite BimIssue assignee index ───────────────────────
        migrationBuilder.DropIndex(
            name: "IX_Issues_ProjectId_AssigneeUserId",
            table: "Issues");

        // ── F3 — Drop UpdatedAt from Tenants ─────────────────────────────────
        migrationBuilder.DropColumn(
            name: "UpdatedAt",
            table: "Tenants");

        // ── F1 — Restore global email uniqueness index ────────────────────────
        migrationBuilder.DropIndex(
            name: "IX_Users_TenantId_Email",
            table: "Users");

        migrationBuilder.CreateIndex(
            name: "IX_Users_Email",
            table: "Users",
            column: "Email",
            unique: true);

        // ── F2 — Remove soft-delete columns from AppUsers ────────────────────
        migrationBuilder.DropColumn(name: "DeletedByUserId", table: "Users");
        migrationBuilder.DropColumn(name: "DeletedAt", table: "Users");
        migrationBuilder.DropColumn(name: "IsDeleted", table: "Users");
    }
}
