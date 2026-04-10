using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class AddPlanscapeDocumentFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Description",
            table: "Documents",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Originator",
            table: "Documents",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "UpdatedAt",
            table: "Documents",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Documents_ProjectId_Discipline",
            table: "Documents",
            columns: new[] { "ProjectId", "Discipline" });

        migrationBuilder.CreateIndex(
            name: "IX_Documents_ProjectId_UploadedAt",
            table: "Documents",
            columns: new[] { "ProjectId", "UploadedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Documents_ProjectId_UploadedAt",
            table: "Documents");

        migrationBuilder.DropIndex(
            name: "IX_Documents_ProjectId_Discipline",
            table: "Documents");

        migrationBuilder.DropColumn(
            name: "UpdatedAt",
            table: "Documents");

        migrationBuilder.DropColumn(
            name: "Originator",
            table: "Documents");

        migrationBuilder.DropColumn(
            name: "Description",
            table: "Documents");
    }
}
