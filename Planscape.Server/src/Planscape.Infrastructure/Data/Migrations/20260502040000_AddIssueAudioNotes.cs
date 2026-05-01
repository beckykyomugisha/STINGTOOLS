using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class AddIssueAudioNotes : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable(
            name: "IssueAudioNotes",
            columns: t => new
            {
                Id              = t.Column<Guid>("uuid", nullable: false),
                TenantId        = t.Column<Guid>("uuid", nullable: false),
                IssueId         = t.Column<Guid>("uuid", nullable: false),
                UserId          = t.Column<Guid>("uuid", nullable: true),
                StoragePath     = t.Column<string>("character varying(500)", maxLength: 500, nullable: false),
                TranscriptText  = t.Column<string>("text", nullable: true),
                Language        = t.Column<string>("character varying(8)", maxLength: 8, nullable: true),
                DurationSeconds = t.Column<int>("integer", nullable: false),
                FileSizeBytes   = t.Column<long>("bigint", nullable: false),
                MimeType        = t.Column<string>("character varying(40)", maxLength: 40, nullable: false),
                CreatedAt       = t.Column<DateTime>("timestamp with time zone", nullable: false),
            },
            constraints: t => t.PrimaryKey("PK_IssueAudioNotes", x => x.Id));
        mb.CreateIndex("IX_IssueAudioNotes_IssueId", "IssueAudioNotes", "IssueId");
    }

    protected override void Down(MigrationBuilder mb) => mb.DropTable("IssueAudioNotes");
}
