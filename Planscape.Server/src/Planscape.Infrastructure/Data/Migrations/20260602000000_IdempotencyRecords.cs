using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// Offline-replay dedupe (Prompt 18) — creates the <c>IdempotencyRecords</c>
/// table that backs server-side idempotency for issue create/update and
/// meeting-action add. The mobile offline queue sends a stable
/// <c>X-Idempotency-Key</c> header on those writes; the controllers record
/// (TenantId, Scope, Key) → ResultId here so an at-least-once replay resolves
/// to the original result instead of creating a duplicate. Unique on
/// (TenantId, Scope, Key).
///
/// NOTE on repo convention (mirrors 20260601000000_CrossHostIdentityFields):
/// this project's migrations are hand-authored without .Designer.cs companions
/// and are NOT discovered by EF's Migrate() (no [Migration] attribute). Dev /
/// local stacks build schema from OnModelCreating via
/// RelationalDatabaseCreator.CreateTables() (Program.cs), so this table already
/// exists there from the model. This file is the exact DDL EF Core would emit,
/// kept so the change is covered once the prod migration pipeline is repaired
/// (backlog P3-2). The model snapshot is intentionally left stale per that same
/// backlog item.
/// </summary>
public partial class IdempotencyRecords : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "IdempotencyRecords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                Scope = table.Column<string>(type: "text", nullable: false),
                Key = table.Column<string>(type: "text", nullable: false),
                ResultId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_IdempotencyRecords_TenantId_Scope_Key",
            table: "IdempotencyRecords",
            columns: new[] { "TenantId", "Scope", "Key" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "IdempotencyRecords");
    }
}
