using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// INT-03 (partial) — Adds the <c>LastModifiedUtc</c> and <c>Version</c>
/// columns to the <c>TaggedElements</c> table so the server can perform
/// last-write-wins conflict detection and return true deltas on
/// <c>GET /api/tagsync/elements/{projectId}</c> instead of treating every
/// plugin sync as a full refresh.
///
/// Both columns already exist on the <c>TaggedElement</c> entity and are
/// populated by <c>TagSyncController.SyncElements</c>; this migration
/// brings the PostgreSQL schema back in line with the model and adds a
/// supporting index for the <c>(ProjectId, LastModifiedUtc)</c> delta-sync
/// query pattern used by <c>GET /api/tagsync/elements</c>.
///
/// Authored by hand per the INT-03 ticket — not scaffolded via
/// <c>dotnet ef migrations add</c> — to keep the change reviewable and
/// avoid spurious snapshot diffs on unrelated entities.
/// </summary>
public partial class AddTagLastModified : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Nullable timestamp — null for rows that predate INT-03; the sync
        // controller treats nulls as "legacy client, accept the update"
        // (see TagSyncController.SyncElements conflict-detection block).
        migrationBuilder.AddColumn<DateTime>(
            name: "LastModifiedUtc",
            table: "TaggedElements",
            type: "timestamp with time zone",
            nullable: true);

        // Optimistic-concurrency counter. Default 1 matches the entity
        // initializer so existing rows get a sensible starting value
        // without a separate backfill step.
        migrationBuilder.AddColumn<int>(
            name: "Version",
            table: "TaggedElements",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        // Delta-sync index — mirrors the (ProjectId, LastModifiedUtc)
        // filter in TagSyncController.GetElements(...lastSyncUtc). Not
        // unique — many elements can share a modification timestamp
        // when a batch operation commits all in one transaction.
        migrationBuilder.CreateIndex(
            name: "IX_TaggedElements_ProjectId_LastModifiedUtc",
            table: "TaggedElements",
            columns: new[] { "ProjectId", "LastModifiedUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_TaggedElements_ProjectId_LastModifiedUtc",
            table: "TaggedElements");

        migrationBuilder.DropColumn(
            name: "Version",
            table: "TaggedElements");

        migrationBuilder.DropColumn(
            name: "LastModifiedUtc",
            table: "TaggedElements");
    }
}
