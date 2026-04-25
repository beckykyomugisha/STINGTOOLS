using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Closes SRV-03 (BimIssue GPS columns) and NEW-SRV-23 (Assignee FK).
/// Adds Latitude / Longitude / LocationAccuracy / DeviceId / Source for site capture,
/// plus AssigneeUserId + AssigneeEmail + CreatedByUserId for project-member-validated routing.
/// </remarks>
public partial class AddIssueGpsAndAssigneeFk : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "Latitude", table: "Issues", type: "double precision", nullable: true);
        migrationBuilder.AddColumn<double>(
            name: "Longitude", table: "Issues", type: "double precision", nullable: true);
        migrationBuilder.AddColumn<double>(
            name: "LocationAccuracy", table: "Issues", type: "double precision", nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "DeviceId", table: "Issues", type: "character varying(120)", maxLength: 120, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "Source", table: "Issues", type: "character varying(20)", maxLength: 20, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "AssigneeEmail", table: "Issues", type: "character varying(320)", maxLength: 320, nullable: true);
        migrationBuilder.AddColumn<Guid>(
            name: "AssigneeUserId", table: "Issues", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<Guid>(
            name: "CreatedByUserId", table: "Issues", type: "uuid", nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Issues_ProjectId_AssigneeUserId",
            table: "Issues",
            columns: new[] { "ProjectId", "AssigneeUserId" });

        migrationBuilder.AddForeignKey(
            name: "FK_Issues_Users_AssigneeUserId",
            table: "Issues",
            column: "AssigneeUserId",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_Issues_Users_CreatedByUserId",
            table: "Issues",
            column: "CreatedByUserId",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_Issues_Users_AssigneeUserId", table: "Issues");
        migrationBuilder.DropForeignKey(name: "FK_Issues_Users_CreatedByUserId", table: "Issues");
        migrationBuilder.DropIndex(name: "IX_Issues_ProjectId_AssigneeUserId", table: "Issues");
        migrationBuilder.DropColumn(name: "Latitude", table: "Issues");
        migrationBuilder.DropColumn(name: "Longitude", table: "Issues");
        migrationBuilder.DropColumn(name: "LocationAccuracy", table: "Issues");
        migrationBuilder.DropColumn(name: "DeviceId", table: "Issues");
        migrationBuilder.DropColumn(name: "Source", table: "Issues");
        migrationBuilder.DropColumn(name: "AssigneeEmail", table: "Issues");
        migrationBuilder.DropColumn(name: "AssigneeUserId", table: "Issues");
        migrationBuilder.DropColumn(name: "CreatedByUserId", table: "Issues");
    }
}
