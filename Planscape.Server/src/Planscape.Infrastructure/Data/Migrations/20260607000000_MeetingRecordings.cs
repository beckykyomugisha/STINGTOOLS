using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// N2 (LiveKit Egress) — creates the <c>MeetingRecordings</c> table backing
/// server-side recording of live meeting sessions. One row per start→stop:
/// (TenantId, ProjectId, SessionId, MeetingId?, EgressId, Kind, Status,
/// StorageKey, file metadata). Indexed on TenantId (tenant filter), SessionId,
/// (ProjectId, MeetingId) (artifact flow-back), and EgressId (webhook match).
///
/// NOTE on repo convention (mirrors 20260602000000_IdempotencyRecords): this
/// project's migrations are hand-authored without .Designer.cs companions and are
/// NOT discovered by EF's Migrate() (no [Migration] attribute). Dev / container
/// stacks build schema from OnModelCreating via CreateTables() + the idempotent
/// PatchDevSchemaAsync CREATE TABLE in Program.cs, so this table exists there from
/// the model. This file is the exact DDL EF Core would emit, kept so the change is
/// covered once the prod migration pipeline is repaired (backlog P3-2).
///
/// UNLIKE the older hand-authored migrations, the model snapshot WAS updated in the
/// same commit for this entity (P3-2 forward-practice) so MeetingRecording does not
/// add NEW snapshot drift — the pre-existing drift on other entities is P3-2's job.
/// </summary>
public partial class MeetingRecordings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MeetingRecordings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                MeetingId = table.Column<Guid>(type: "uuid", nullable: true),
                EgressId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, defaultValue: ""),
                Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false, defaultValue: "room-composite"),
                Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "STARTING"),
                StorageKey = table.Column<string>(type: "text", nullable: true),
                FileName = table.Column<string>(type: "text", nullable: true),
                FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                DurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                Error = table.Column<string>(type: "text", nullable: true),
                StartedBy = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                StartedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MeetingRecordings", x => x.Id);
            });

        migrationBuilder.CreateIndex(name: "IX_MeetingRecordings_TenantId", table: "MeetingRecordings", column: "TenantId");
        migrationBuilder.CreateIndex(name: "IX_MeetingRecordings_SessionId", table: "MeetingRecordings", column: "SessionId");
        migrationBuilder.CreateIndex(name: "IX_MeetingRecordings_ProjectId_MeetingId", table: "MeetingRecordings", columns: new[] { "ProjectId", "MeetingId" });
        migrationBuilder.CreateIndex(name: "IX_MeetingRecordings_EgressId", table: "MeetingRecordings", column: "EgressId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MeetingRecordings");
    }
}
