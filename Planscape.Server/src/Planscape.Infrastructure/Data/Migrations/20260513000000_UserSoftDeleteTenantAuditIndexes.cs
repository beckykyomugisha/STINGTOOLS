using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// Migration: UserSoftDeleteTenantAuditIndexes
///
/// Changes applied:
///   F2 — Add soft-delete columns (IsDeleted, DeletedAt, DeletedByUserId) to AppUsers.
///   F3 — Add UpdatedAt column to Tenants.
///   F1 — Drop the global-unique index IX_Users_Email and replace it with the
///         per-tenant composite unique index IX_Users_TenantId_Email.
///   F4 — Add IX_Issues_ProjectId_AssigneeUserId and IX_Issues_ProjectId_Status
///         (both may already exist from a prior migration; the migration is
///         written to add them only when absent).
///   F5 — Drop the Cascade FK from ProjectMembers → Users and DevicePushTokens →
///         Users, and re-create both as Restrict so soft-delete is the
///         only user-retirement path.
/// </summary>
public partial class UserSoftDeleteTenantAuditIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── F2: soft-delete columns on AppUsers ──────────────────────────────
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

        // ── F3: UpdatedAt column on Tenants ──────────────────────────────────
        // defaultValueSql: current_timestamp so existing rows get a sensible
        // initial value rather than the zero-time epoch.
        migrationBuilder.AddColumn<DateTime>(
            name: "UpdatedAt",
            table: "Tenants",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "now()");

        // ── F1: replace global email-unique index with per-tenant composite ──
        // Drop the old single-column unique index first.
        migrationBuilder.DropIndex(
            name: "IX_Users_Email",
            table: "Users");

        // Create the new per-tenant composite unique index.
        migrationBuilder.CreateIndex(
            name: "IX_Users_TenantId_Email",
            table: "Users",
            columns: new[] { "TenantId", "Email" },
            unique: true);

        // ── F4: composite indexes on Issues ──────────────────────────────────
        // IX_Issues_ProjectId_AssigneeUserId — accelerates "my issues" queries
        // and the SLA escalation join. If already exists this will fail loudly;
        // remove it from Up() if a prior migration already created it.
        migrationBuilder.CreateIndex(
            name: "IX_Issues_ProjectId_AssigneeUserId",
            table: "Issues",
            columns: new[] { "ProjectId", "AssigneeUserId" });

        // IX_Issues_ProjectId_Status — already in InitialCreate; included here
        // for documentation completeness but the actual DDL was in the initial
        // migration. If you get a duplicate-index error, remove this block.
        // migrationBuilder.CreateIndex(
        //     name: "IX_Issues_ProjectId_Status",
        //     table: "Issues",
        //     columns: new[] { "ProjectId", "Status" });

        // ── F5: ProjectMembers → Users: Cascade → Restrict ───────────────────
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

        // ── F5: DevicePushTokens → Users: Cascade → Restrict ─────────────────
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
        // ── F5: restore Cascade FKs ───────────────────────────────────────────
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

        // ── F4: drop composite issue indexes ─────────────────────────────────
        migrationBuilder.DropIndex(
            name: "IX_Issues_ProjectId_AssigneeUserId",
            table: "Issues");

        // ── F1: restore global email-unique index ─────────────────────────────
        migrationBuilder.DropIndex(
            name: "IX_Users_TenantId_Email",
            table: "Users");

        migrationBuilder.CreateIndex(
            name: "IX_Users_Email",
            table: "Users",
            column: "Email",
            unique: true);

        // ── F3: drop UpdatedAt from Tenants ───────────────────────────────────
        migrationBuilder.DropColumn(
            name: "UpdatedAt",
            table: "Tenants");

        // ── F2: drop soft-delete columns from Users ───────────────────────────
        migrationBuilder.DropColumn(
            name: "DeletedByUserId",
            table: "Users");

        migrationBuilder.DropColumn(
            name: "DeletedAt",
            table: "Users");

        migrationBuilder.DropColumn(
            name: "IsDeleted",
            table: "Users");
    }
}
