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

        // Raw idempotent SQL (not AddColumn<>) because no migration in the chain
        // CREATEs MeetingSessions — that table is materialised by EnsureCreated /
        // PlatformSchemaPatcher, which on a fresh-DB boot run AFTER Database.Migrate().
        // A plain AddColumn would therefore fail at this migration on a fresh DB.
        // `ALTER TABLE IF EXISTS … ADD COLUMN IF NOT EXISTS` makes Up() a safe no-op
        // when the table isn't there yet (the patcher adds the table + columns), and
        // adds the columns when it exists but predates them. (Verified via a throwaway
        // Postgres `dotnet ef database update`.)

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE IF EXISTS ""MeetingSessions"" ADD COLUMN IF NOT EXISTS ""ActiveSurface"" text;");
            migrationBuilder.Sql(@"ALTER TABLE IF EXISTS ""MeetingSessions"" ADD COLUMN IF NOT EXISTS ""ActiveDocumentId"" uuid;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE IF EXISTS ""MeetingSessions"" DROP COLUMN IF EXISTS ""ActiveSurface"";");
            migrationBuilder.Sql(@"ALTER TABLE IF EXISTS ""MeetingSessions"" DROP COLUMN IF EXISTS ""ActiveDocumentId"";");
        }
    }
}
