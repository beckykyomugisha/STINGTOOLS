using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 146 — normalised <c>StageGateCriteria</c> table replacing per-call
/// rewrites of <c>StageGate.CriteriaJson</c>. The JSONB column on the parent
/// row is preserved for read-fallback during migration; controller writes go
/// to this table going forward.
/// </remarks>
public partial class AddStageGateCriteria : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "StageGateCriteria",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StageGateId = table.Column<Guid>(type: "uuid", nullable: false),
                Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, defaultValue: ""),
                Label = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false, defaultValue: ""),
                Description = table.Column<string>(type: "text", nullable: true),
                SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                Met = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                EvidenceDocId = table.Column<Guid>(type: "uuid", nullable: true),
                SignedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                SignedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                SignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Comment = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StageGateCriteria", x => x.Id);
                table.ForeignKey(
                    name: "FK_StageGateCriteria_StageGates_StageGateId",
                    column: x => x.StageGateId, principalTable: "StageGates",
                    principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex(name: "IX_StageGateCriteria_StageGateId",
            table: "StageGateCriteria", column: "StageGateId");
        migrationBuilder.CreateIndex(name: "IX_StageGateCriteria_StageGateId_Key",
            table: "StageGateCriteria", columns: new[] { "StageGateId", "Key" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "StageGateCriteria");
    }
}
