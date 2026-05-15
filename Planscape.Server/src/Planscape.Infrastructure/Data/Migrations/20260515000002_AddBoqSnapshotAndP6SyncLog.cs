using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Adds the BoqSnapshots table (feature gap 2/3 — BOQ cloud cost dashboard)
/// and the P6SyncLogs table (feature gap 6 — Primavera P6 live-link).
/// Also adds the P6ActivityId and PercentComplete columns to TaggedElements
/// required by the P6 live-link write-back path.
/// Run: dotnet ef database update AddBoqSnapshotAndP6SyncLog
/// </remarks>
public partial class AddBoqSnapshotAndP6SyncLog : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── BoqSnapshots ─────────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "BoqSnapshots",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ProjectId       = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId        = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt       = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                SnapshotJson    = table.Column<string>(type: "text", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BoqSnapshots", x => x.Id);
                table.ForeignKey(
                    name: "FK_BoqSnapshots_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BoqSnapshots_ProjectId",
            table: "BoqSnapshots",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_BoqSnapshots_TenantId",
            table: "BoqSnapshots",
            column: "TenantId");

        // ── P6SyncLogs ───────────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "P6SyncLogs",
            columns: table => new
            {
                Id               = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ProjectId        = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId         = table.Column<Guid>(type: "uuid", nullable: false),
                SyncedAt         = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ActivitiesPolled = table.Column<int>(type: "integer", nullable: false),
                ElementsUpdated  = table.Column<int>(type: "integer", nullable: false),
                ErrorMessage     = table.Column<string>(type: "text", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_P6SyncLogs", x => x.Id);
                table.ForeignKey(
                    name: "FK_P6SyncLogs_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_P6SyncLogs_ProjectId",
            table: "P6SyncLogs",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_P6SyncLogs_TenantId",
            table: "P6SyncLogs",
            column: "TenantId");

        // ── TaggedElement — P6 columns ────────────────────────────────────────
        migrationBuilder.AddColumn<string>(
            name: "P6ActivityId",
            table: "TaggedElements",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "PercentComplete",
            table: "TaggedElements",
            type: "double precision",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_TaggedElements_P6ActivityId",
            table: "TaggedElements",
            column: "P6ActivityId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_TaggedElements_P6ActivityId",
            table: "TaggedElements");

        migrationBuilder.DropColumn(
            name: "P6ActivityId",
            table: "TaggedElements");

        migrationBuilder.DropColumn(
            name: "PercentComplete",
            table: "TaggedElements");

        migrationBuilder.DropTable(name: "P6SyncLogs");
        migrationBuilder.DropTable(name: "BoqSnapshots");
    }
}
