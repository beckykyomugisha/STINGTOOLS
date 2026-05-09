using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 178c — Post-204 server enhancements bundle:
///   * T3-19  IssueAudioNote columns: DocumentId, TranscribedAt, CreatedBy
///   * T3-12  ApprovalChains + ApprovalStages tables
///   * T3-24  DocumentRevisions table
///
/// All changes are additive — existing rows / endpoints continue to work
/// untouched. The legacy single-approver <c>DocumentApproval</c> table is
/// kept as the back-compat path and is NOT modified by this migration.
/// </remarks>
public partial class AddPost204ServerEnhancements : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder mb)
    {
        // ── T3-19: extend IssueAudioNotes with DocumentId / TranscribedAt / CreatedBy ──
        mb.AddColumn<Guid>(
            name: "DocumentId",
            table: "IssueAudioNotes",
            type: "uuid",
            nullable: true);
        mb.AddColumn<DateTime>(
            name: "TranscribedAt",
            table: "IssueAudioNotes",
            type: "timestamp with time zone",
            nullable: true);
        mb.AddColumn<string>(
            name: "CreatedBy",
            table: "IssueAudioNotes",
            type: "character varying(120)",
            maxLength: 120,
            nullable: true);
        mb.CreateIndex(
            name: "IX_IssueAudioNotes_DocumentId",
            table: "IssueAudioNotes",
            column: "DocumentId");
        mb.AddForeignKey(
            name: "FK_IssueAudioNotes_Documents_DocumentId",
            table: "IssueAudioNotes",
            column: "DocumentId",
            principalTable: "Documents",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        // ── T3-12: ApprovalChains ──
        mb.CreateTable(
            name: "ApprovalChains",
            columns: t => new
            {
                Id          = t.Column<Guid>("uuid", nullable: false),
                TenantId    = t.Column<Guid>("uuid", nullable: false),
                ProjectId   = t.Column<Guid>("uuid", nullable: false),
                DocumentId  = t.Column<Guid>("uuid", nullable: false),
                Transition  = t.Column<string>("character varying(80)", maxLength: 80, nullable: false),
                Status      = t.Column<string>("character varying(20)", maxLength: 20, nullable: false),
                CreatedBy   = t.Column<string>("character varying(120)", maxLength: 120, nullable: false),
                CreatedAt   = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CompletedAt = t.Column<DateTime>("timestamp with time zone", nullable: true),
                Description = t.Column<string>("character varying(500)", maxLength: 500, nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_ApprovalChains", x => x.Id);
                t.ForeignKey(
                    name: "FK_ApprovalChains_Documents_DocumentId",
                    column: x => x.DocumentId,
                    principalTable: "Documents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                t.ForeignKey(
                    name: "FK_ApprovalChains_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
        mb.CreateIndex("IX_ApprovalChains_DocumentId_Transition_Status",
            "ApprovalChains", new[] { "DocumentId", "Transition", "Status" });
        mb.CreateIndex("IX_ApprovalChains_ProjectId", "ApprovalChains", "ProjectId");
        mb.CreateIndex("IX_ApprovalChains_TenantId",  "ApprovalChains", "TenantId");

        // ── T3-12: ApprovalStages ──
        mb.CreateTable(
            name: "ApprovalStages",
            columns: t => new
            {
                Id                    = t.Column<Guid>("uuid", nullable: false),
                TenantId              = t.Column<Guid>("uuid", nullable: false),
                ChainId               = t.Column<Guid>("uuid", nullable: false),
                Order                 = t.Column<int>("integer", nullable: false),
                Mode                  = t.Column<string>("character varying(16)", maxLength: 16, nullable: false),
                RequiredApproversJson = t.Column<string>("text", nullable: false),
                Status                = t.Column<string>("character varying(16)", maxLength: 16, nullable: false),
                DecisionsJson         = t.Column<string>("text", nullable: false),
                StartedAt             = t.Column<DateTime>("timestamp with time zone", nullable: true),
                CompletedAt           = t.Column<DateTime>("timestamp with time zone", nullable: true),
                Label                 = t.Column<string>("character varying(120)", maxLength: 120, nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_ApprovalStages", x => x.Id);
                t.ForeignKey(
                    name: "FK_ApprovalStages_ApprovalChains_ChainId",
                    column: x => x.ChainId,
                    principalTable: "ApprovalChains",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
        mb.CreateIndex("IX_ApprovalStages_ChainId_Order",
            "ApprovalStages", new[] { "ChainId", "Order" });
        mb.CreateIndex("IX_ApprovalStages_TenantId", "ApprovalStages", "TenantId");

        // ── T3-24: DocumentRevisions ──
        mb.CreateTable(
            name: "DocumentRevisions",
            columns: t => new
            {
                Id                    = t.Column<Guid>("uuid", nullable: false),
                TenantId              = t.Column<Guid>("uuid", nullable: false),
                DocumentId            = t.Column<Guid>("uuid", nullable: false),
                Revision              = t.Column<string>("character varying(16)", maxLength: 16, nullable: false),
                CdeStateAtRevision    = t.Column<string>("character varying(20)", maxLength: 20, nullable: false),
                SuitabilityAtRevision = t.Column<string>("character varying(8)",  maxLength: 8,  nullable: true),
                FilePath              = t.Column<string>("character varying(500)", maxLength: 500, nullable: true),
                FileSizeBytes         = t.Column<long>("bigint", nullable: true),
                ContentHash           = t.Column<string>("character varying(128)", maxLength: 128, nullable: true),
                CreatedBy             = t.Column<string>("character varying(120)", maxLength: 120, nullable: false),
                CreatedAt             = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CommentSummary        = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                Source                = t.Column<string>("character varying(40)", maxLength: 40, nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_DocumentRevisions", x => x.Id);
                t.ForeignKey(
                    name: "FK_DocumentRevisions_Documents_DocumentId",
                    column: x => x.DocumentId,
                    principalTable: "Documents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
        // Composite descending-by-CreatedAt so "newest first" pagination is index-only.
        mb.Sql(@"CREATE INDEX ""IX_DocumentRevisions_DocumentId_CreatedAt""
                 ON ""DocumentRevisions"" (""DocumentId"", ""CreatedAt"" DESC);");
        mb.CreateIndex("IX_DocumentRevisions_TenantId", "DocumentRevisions", "TenantId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder mb)
    {
        // T3-24
        mb.DropTable("DocumentRevisions");

        // T3-12
        mb.DropTable("ApprovalStages");
        mb.DropTable("ApprovalChains");

        // T3-19
        mb.DropForeignKey("FK_IssueAudioNotes_Documents_DocumentId", "IssueAudioNotes");
        mb.DropIndex("IX_IssueAudioNotes_DocumentId", "IssueAudioNotes");
        mb.DropColumn("CreatedBy",     "IssueAudioNotes");
        mb.DropColumn("TranscribedAt", "IssueAudioNotes");
        mb.DropColumn("DocumentId",    "IssueAudioNotes");
    }
}
