using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Planscape.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MeetingMedia : Migration
    {
        // RECONCILIATION MIGRATION (WS3 / P2).
        //
        // The committed model snapshot had drifted far behind the entity model (it was
        // missing ~99 tables, including MeetingSessions itself — those tables already
        // exist in deployed databases, created by EnsureCreated in dev and by
        // PlatformSchemaPatcher on pre-existing prod DBs). Re-scaffolding therefore
        // produced a whole-schema CREATE diff, which must NOT run against a live DB.
        //
        // This migration ships ONLY the real new delta — the two MeetingSession columns
        // (ActiveSurface, ActiveDocumentId). The accompanying snapshot HAS been refreshed
        // to match the full current model, so FUTURE `migrations add` produce clean diffs
        // instead of the whole-schema scaffold. Both columns are nullable + additive, so
        // applying this on an existing MeetingSessions table is non-breaking.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActiveSurface",
                table: "MeetingSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ActiveDocumentId",
                table: "MeetingSessions",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveSurface",
                table: "MeetingSessions");

            migrationBuilder.DropColumn(
                name: "ActiveDocumentId",
                table: "MeetingSessions");
        }
    }
}
