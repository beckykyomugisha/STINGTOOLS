using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 179 — Site-photo workflow enhancements. Adds the missing
/// primitives identified during the BCC site-photo review pass:
///
///   * <c>PhotoAlbums</c> + <c>PhotoAlbumPhotos</c>      — named photo
///     collections (handover bundle, weekly digest, snag list, …) with
///     per-album visibility (Internal / Members / Client / Distribution).
///   * <c>DistributionGroups</c> + <c>DistributionGroupMembers</c> —
///     project-scoped recipient lists for albums, per-photo ACLs, daily
///     digest dispatch, share-link revocation.
///   * <c>PhotoAccessRules</c> — per-photo ACL layered on top of the
///     existing audience state machine. AND-ed at read time.
///   * <c>PhotoChecklists</c> + <c>PhotoChecklistItems</c> — required
///     shots authored by a BIM manager and fulfilled by site coordinators.
///   * <c>PhotoAnnotations</c> — vector markup overlay (arrows / circles
///     / text) stored as normalised 0..1 coords so desktop, mobile, and
///     PDF render the same overlay.
///   * <c>PhotoVoiceNotes</c> — voice notes linked to a SitePhoto, mirroring
///     the IssueAudioNote shape so transcript / playback / scan plumbing
///     applies uniformly.
///   * <c>PhotoShareLinks</c> — opaque-token shareable URL with optional
///     expiry, fetch cap, and revocation. Supports single-photo and
///     whole-album sharing.
///   * <c>PhotoPolicies</c> — per-project policy singleton: allowed
///     reasons, default audience by reason, watermark template, retention
///     window, geofence, digest hour, approval chain, checklist enforcement.
///
/// All changes are additive. The existing <c>SitePhotos</c> table is
/// untouched; v1 readers / writers continue to work without modification.
/// </remarks>
public partial class AddPhotoWorkflowEnhancements : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder mb)
    {
        // ── DistributionGroups ──────────────────────────────────────────
        mb.CreateTable(
            name: "DistributionGroups",
            columns: t => new
            {
                Id          = t.Column<Guid>("uuid", nullable: false),
                TenantId    = t.Column<Guid>("uuid", nullable: false),
                ProjectId   = t.Column<Guid>("uuid", nullable: false),
                Name        = t.Column<string>("character varying(200)", maxLength: 200, nullable: false),
                Description = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                Kind        = t.Column<string>("character varying(20)", maxLength: 20, nullable: false),
                IncludeInDailyDigest = t.Column<bool>("boolean", nullable: false, defaultValue: false),
                ForceRedacted        = t.Column<bool>("boolean", nullable: false, defaultValue: false),
                CreatedAt        = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CreatedByUserId  = t.Column<Guid>("uuid", nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_DistributionGroups", x => x.Id);
                t.ForeignKey("FK_DistributionGroups_Projects_ProjectId",
                    x => x.ProjectId, "Projects", "Id", onDelete: ReferentialAction.Cascade);
            });
        mb.CreateIndex("IX_DistributionGroups_ProjectId", "DistributionGroups", "ProjectId");
        mb.CreateIndex("IX_DistributionGroups_ProjectId_Name", "DistributionGroups",
            new[] { "ProjectId", "Name" }, unique: true);

        // ── DistributionGroupMembers ───────────────────────────────────
        mb.CreateTable(
            name: "DistributionGroupMembers",
            columns: t => new
            {
                Id                  = t.Column<Guid>("uuid", nullable: false),
                DistributionGroupId = t.Column<Guid>("uuid", nullable: false),
                UserId              = t.Column<Guid>("uuid", nullable: true),
                ExternalEmail       = t.Column<string>("character varying(320)", maxLength: 320, nullable: true),
                DisplayName         = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                DisciplineFilter    = t.Column<string>("character varying(8)", maxLength: 8, nullable: true),
                AddedAt             = t.Column<DateTime>("timestamp with time zone", nullable: false),
                AddedByUserId       = t.Column<Guid>("uuid", nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_DistributionGroupMembers", x => x.Id);
                t.ForeignKey("FK_DistributionGroupMembers_DistributionGroups_DistributionGroupId",
                    x => x.DistributionGroupId, "DistributionGroups", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_DistributionGroupMembers_Users_UserId",
                    x => x.UserId, "Users", "Id", onDelete: ReferentialAction.SetNull);
            });
        mb.CreateIndex("IX_DistributionGroupMembers_DistributionGroupId",
            "DistributionGroupMembers", "DistributionGroupId");
        mb.CreateIndex("IX_DistributionGroupMembers_DistributionGroupId_UserId",
            "DistributionGroupMembers", new[] { "DistributionGroupId", "UserId" });
        mb.CreateIndex("IX_DistributionGroupMembers_DistributionGroupId_ExternalEmail",
            "DistributionGroupMembers", new[] { "DistributionGroupId", "ExternalEmail" });

        // ── PhotoAlbums ────────────────────────────────────────────────
        mb.CreateTable(
            name: "PhotoAlbums",
            columns: t => new
            {
                Id        = t.Column<Guid>("uuid", nullable: false),
                TenantId  = t.Column<Guid>("uuid", nullable: false),
                ProjectId = t.Column<Guid>("uuid", nullable: false),
                Name      = t.Column<string>("character varying(200)", maxLength: 200, nullable: false),
                Description = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                Visibility  = t.Column<string>("character varying(20)", maxLength: 20, nullable: false),
                DistributionGroupId = t.Column<Guid>("uuid", nullable: true),
                Kind          = t.Column<string>("character varying(40)", maxLength: 40, nullable: true),
                CoverPhotoId  = t.Column<Guid>("uuid", nullable: true),
                IsLocked      = t.Column<bool>("boolean", nullable: false, defaultValue: false),
                LockedAt      = t.Column<DateTime>("timestamp with time zone", nullable: true),
                LockedByUserId = t.Column<Guid>("uuid", nullable: true),
                AutoArchiveAfterDays = t.Column<int>("integer", nullable: true),
                CreatedAt        = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CreatedByUserId  = t.Column<Guid>("uuid", nullable: true),
                UpdatedAt        = t.Column<DateTime>("timestamp with time zone", nullable: true),
                UpdatedByUserId  = t.Column<Guid>("uuid", nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_PhotoAlbums", x => x.Id);
                t.ForeignKey("FK_PhotoAlbums_Projects_ProjectId",
                    x => x.ProjectId, "Projects", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_PhotoAlbums_DistributionGroups_DistributionGroupId",
                    x => x.DistributionGroupId, "DistributionGroups", "Id", onDelete: ReferentialAction.SetNull);
            });
        mb.CreateIndex("IX_PhotoAlbums_ProjectId", "PhotoAlbums", "ProjectId");
        mb.CreateIndex("IX_PhotoAlbums_ProjectId_Kind", "PhotoAlbums", new[] { "ProjectId", "Kind" });
        mb.CreateIndex("IX_PhotoAlbums_ProjectId_Visibility", "PhotoAlbums", new[] { "ProjectId", "Visibility" });
        mb.CreateIndex("IX_PhotoAlbums_DistributionGroupId", "PhotoAlbums", "DistributionGroupId");

        // ── PhotoAlbumPhotos ───────────────────────────────────────────
        mb.CreateTable(
            name: "PhotoAlbumPhotos",
            columns: t => new
            {
                AlbumId   = t.Column<Guid>("uuid", nullable: false),
                PhotoId   = t.Column<Guid>("uuid", nullable: false),
                SortOrder = t.Column<int>("integer", nullable: false, defaultValue: 0),
                AddedAt        = t.Column<DateTime>("timestamp with time zone", nullable: false),
                AddedByUserId  = t.Column<Guid>("uuid", nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_PhotoAlbumPhotos", x => new { x.AlbumId, x.PhotoId });
                t.ForeignKey("FK_PhotoAlbumPhotos_PhotoAlbums_AlbumId",
                    x => x.AlbumId, "PhotoAlbums", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_PhotoAlbumPhotos_SitePhotos_PhotoId",
                    x => x.PhotoId, "SitePhotos", "Id", onDelete: ReferentialAction.Cascade);
            });
        mb.CreateIndex("IX_PhotoAlbumPhotos_PhotoId", "PhotoAlbumPhotos", "PhotoId");

        // ── PhotoAccessRules ───────────────────────────────────────────
        mb.CreateTable(
            name: "PhotoAccessRules",
            columns: t => new
            {
                Id      = t.Column<Guid>("uuid", nullable: false),
                PhotoId = t.Column<Guid>("uuid", nullable: false),
                DistributionGroupId   = t.Column<Guid>("uuid", nullable: true),
                VisibleDisciplines    = t.Column<string>("character varying(80)", maxLength: 80, nullable: true),
                MinRoleToView         = t.Column<string>("character varying(40)", maxLength: 40, nullable: true),
                VisibleFrom           = t.Column<DateTime>("timestamp with time zone", nullable: true),
                VisibleUntil          = t.Column<DateTime>("timestamp with time zone", nullable: true),
                RequiresNdaAcceptance = t.Column<bool>("boolean", nullable: false, defaultValue: false),
                CreatedAt             = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CreatedByUserId       = t.Column<Guid>("uuid", nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_PhotoAccessRules", x => x.Id);
                t.ForeignKey("FK_PhotoAccessRules_SitePhotos_PhotoId",
                    x => x.PhotoId, "SitePhotos", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_PhotoAccessRules_DistributionGroups_DistributionGroupId",
                    x => x.DistributionGroupId, "DistributionGroups", "Id", onDelete: ReferentialAction.SetNull);
            });
        mb.CreateIndex("IX_PhotoAccessRules_PhotoId", "PhotoAccessRules", "PhotoId");
        mb.CreateIndex("IX_PhotoAccessRules_DistributionGroupId", "PhotoAccessRules", "DistributionGroupId");

        // ── PhotoChecklists ────────────────────────────────────────────
        mb.CreateTable(
            name: "PhotoChecklists",
            columns: t => new
            {
                Id          = t.Column<Guid>("uuid", nullable: false),
                TenantId    = t.Column<Guid>("uuid", nullable: false),
                ProjectId   = t.Column<Guid>("uuid", nullable: false),
                Name        = t.Column<string>("character varying(200)", maxLength: 200, nullable: false),
                Description = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                Kind        = t.Column<string>("character varying(40)", maxLength: 40, nullable: true),
                Status      = t.Column<string>("character varying(20)", maxLength: 20, nullable: false),
                LevelCode   = t.Column<string>("character varying(40)", maxLength: 40, nullable: true),
                ZoneCode    = t.Column<string>("character varying(40)", maxLength: 40, nullable: true),
                WorkPackageId = t.Column<Guid>("uuid", nullable: true),
                DueAt           = t.Column<DateTime>("timestamp with time zone", nullable: true),
                CreatedAt       = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CreatedByUserId = t.Column<Guid>("uuid", nullable: true),
                ClosedAt        = t.Column<DateTime>("timestamp with time zone", nullable: true),
                ClosedByUserId  = t.Column<Guid>("uuid", nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_PhotoChecklists", x => x.Id);
                t.ForeignKey("FK_PhotoChecklists_Projects_ProjectId",
                    x => x.ProjectId, "Projects", "Id", onDelete: ReferentialAction.Cascade);
            });
        mb.CreateIndex("IX_PhotoChecklists_ProjectId", "PhotoChecklists", "ProjectId");
        mb.CreateIndex("IX_PhotoChecklists_ProjectId_Status",
            "PhotoChecklists", new[] { "ProjectId", "Status" });
        mb.CreateIndex("IX_PhotoChecklists_ProjectId_LevelCode_ZoneCode",
            "PhotoChecklists", new[] { "ProjectId", "LevelCode", "ZoneCode" });

        // ── PhotoChecklistItems ────────────────────────────────────────
        mb.CreateTable(
            name: "PhotoChecklistItems",
            columns: t => new
            {
                Id          = t.Column<Guid>("uuid", nullable: false),
                ChecklistId = t.Column<Guid>("uuid", nullable: false),
                Title       = t.Column<string>("character varying(300)", maxLength: 300, nullable: false),
                Description = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                SortOrder   = t.Column<int>("integer", nullable: false, defaultValue: 0),
                DefaultReason = t.Column<string>("character varying(20)", maxLength: 20, nullable: false),
                IsRequired    = t.Column<bool>("boolean", nullable: false, defaultValue: true),
                IsWaived      = t.Column<bool>("boolean", nullable: false, defaultValue: false),
                WaivedReason  = t.Column<string>("character varying(500)", maxLength: 500, nullable: true),
                FulfilledByPhotoId = t.Column<Guid>("uuid", nullable: true),
                FulfilledAt        = t.Column<DateTime>("timestamp with time zone", nullable: true),
                FulfilledByUserId  = t.Column<Guid>("uuid", nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_PhotoChecklistItems", x => x.Id);
                t.ForeignKey("FK_PhotoChecklistItems_PhotoChecklists_ChecklistId",
                    x => x.ChecklistId, "PhotoChecklists", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_PhotoChecklistItems_SitePhotos_FulfilledByPhotoId",
                    x => x.FulfilledByPhotoId, "SitePhotos", "Id", onDelete: ReferentialAction.SetNull);
            });
        mb.CreateIndex("IX_PhotoChecklistItems_ChecklistId", "PhotoChecklistItems", "ChecklistId");
        mb.CreateIndex("IX_PhotoChecklistItems_FulfilledByPhotoId",
            "PhotoChecklistItems", "FulfilledByPhotoId");

        // ── PhotoAnnotations ───────────────────────────────────────────
        mb.CreateTable(
            name: "PhotoAnnotations",
            columns: t => new
            {
                Id      = t.Column<Guid>("uuid", nullable: false),
                PhotoId = t.Column<Guid>("uuid", nullable: false),
                ShapesJson = t.Column<string>("jsonb", nullable: false),
                Summary    = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAt        = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CreatedByUserId  = t.Column<Guid>("uuid", nullable: true),
                CreatedByName    = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                UpdatedAt        = t.Column<DateTime>("timestamp with time zone", nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_PhotoAnnotations", x => x.Id);
                t.ForeignKey("FK_PhotoAnnotations_SitePhotos_PhotoId",
                    x => x.PhotoId, "SitePhotos", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_PhotoAnnotations_Users_CreatedByUserId",
                    x => x.CreatedByUserId, "Users", "Id", onDelete: ReferentialAction.SetNull);
            });
        mb.CreateIndex("IX_PhotoAnnotations_PhotoId", "PhotoAnnotations", "PhotoId");
        mb.CreateIndex("IX_PhotoAnnotations_PhotoId_CreatedByUserId",
            "PhotoAnnotations", new[] { "PhotoId", "CreatedByUserId" });

        // ── PhotoVoiceNotes ────────────────────────────────────────────
        mb.CreateTable(
            name: "PhotoVoiceNotes",
            columns: t => new
            {
                Id         = t.Column<Guid>("uuid", nullable: false),
                TenantId   = t.Column<Guid>("uuid", nullable: false),
                PhotoId    = t.Column<Guid>("uuid", nullable: false),
                UserId     = t.Column<Guid>("uuid", nullable: true),
                DocumentId = t.Column<Guid>("uuid", nullable: false),
                TranscriptText  = t.Column<string>("text", nullable: true),
                Language        = t.Column<string>("character varying(8)", maxLength: 8, nullable: true),
                DurationSeconds = t.Column<int>("integer", nullable: false, defaultValue: 0),
                FileSizeBytes   = t.Column<long>("bigint", nullable: false, defaultValue: 0L),
                MimeType        = t.Column<string>("character varying(60)", maxLength: 60, nullable: false),
                TranscribedAt   = t.Column<DateTime>("timestamp with time zone", nullable: true),
                CreatedBy       = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                CreatedAt       = t.Column<DateTime>("timestamp with time zone", nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_PhotoVoiceNotes", x => x.Id);
                t.ForeignKey("FK_PhotoVoiceNotes_SitePhotos_PhotoId",
                    x => x.PhotoId, "SitePhotos", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_PhotoVoiceNotes_Documents_DocumentId",
                    x => x.DocumentId, "Documents", "Id", onDelete: ReferentialAction.Restrict);
            });
        mb.CreateIndex("IX_PhotoVoiceNotes_PhotoId", "PhotoVoiceNotes", "PhotoId");
        mb.CreateIndex("IX_PhotoVoiceNotes_DocumentId", "PhotoVoiceNotes", "DocumentId");

        // ── PhotoShareLinks ────────────────────────────────────────────
        mb.CreateTable(
            name: "PhotoShareLinks",
            columns: t => new
            {
                Id         = t.Column<Guid>("uuid", nullable: false),
                TenantId   = t.Column<Guid>("uuid", nullable: false),
                ProjectId  = t.Column<Guid>("uuid", nullable: false),
                PhotoId    = t.Column<Guid>("uuid", nullable: true),
                AlbumId    = t.Column<Guid>("uuid", nullable: true),
                Token      = t.Column<string>("character varying(128)", maxLength: 128, nullable: false),
                Label      = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                ExpiresAt  = t.Column<DateTime>("timestamp with time zone", nullable: true),
                ForceRedacted = t.Column<bool>("boolean", nullable: false, defaultValue: true),
                MaxFetches    = t.Column<int>("integer", nullable: true),
                FetchCount    = t.Column<int>("integer", nullable: false, defaultValue: 0),
                CreatedAt        = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CreatedByUserId  = t.Column<Guid>("uuid", nullable: true),
                RevokedAt        = t.Column<DateTime>("timestamp with time zone", nullable: true),
                RevokedByUserId  = t.Column<Guid>("uuid", nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_PhotoShareLinks", x => x.Id);
                t.ForeignKey("FK_PhotoShareLinks_Projects_ProjectId",
                    x => x.ProjectId, "Projects", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_PhotoShareLinks_SitePhotos_PhotoId",
                    x => x.PhotoId, "SitePhotos", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_PhotoShareLinks_PhotoAlbums_AlbumId",
                    x => x.AlbumId, "PhotoAlbums", "Id", onDelete: ReferentialAction.Cascade);
            });
        mb.CreateIndex("IX_PhotoShareLinks_Token", "PhotoShareLinks", "Token", unique: true);
        mb.CreateIndex("IX_PhotoShareLinks_ProjectId", "PhotoShareLinks", "ProjectId");
        mb.CreateIndex("IX_PhotoShareLinks_PhotoId", "PhotoShareLinks", "PhotoId");
        mb.CreateIndex("IX_PhotoShareLinks_AlbumId", "PhotoShareLinks", "AlbumId");

        // ── PhotoPolicies ──────────────────────────────────────────────
        mb.CreateTable(
            name: "PhotoPolicies",
            columns: t => new
            {
                Id        = t.Column<Guid>("uuid", nullable: false),
                TenantId  = t.Column<Guid>("uuid", nullable: false),
                ProjectId = t.Column<Guid>("uuid", nullable: false),
                AllowedReasonsJson         = t.Column<string>("jsonb", nullable: true),
                DefaultAudienceByReasonJson = t.Column<string>("jsonb", nullable: true),
                WatermarkLogoPath          = t.Column<string>("character varying(500)", maxLength: 500, nullable: true),
                WatermarkFooterTemplate    = t.Column<string>("character varying(500)", maxLength: 500, nullable: true),
                WatermarkRequired          = t.Column<bool>("boolean", nullable: false, defaultValue: true),
                FaceBlurRequired           = t.Column<bool>("boolean", nullable: false, defaultValue: true),
                PlateBlurRequired          = t.Column<bool>("boolean", nullable: false, defaultValue: false),
                RetentionDays              = t.Column<int>("integer", nullable: true),
                AutoArchiveAfterHandover   = t.Column<bool>("boolean", nullable: false, defaultValue: false),
                GeofenceWkt                = t.Column<string>("text", nullable: true),
                OffsiteAudience            = t.Column<string>("character varying(20)", maxLength: 20, nullable: true),
                DigestHourLocal            = t.Column<int>("integer", nullable: false, defaultValue: 17),
                DigestDistributionGroupId  = t.Column<Guid>("uuid", nullable: true),
                ApprovalChain              = t.Column<string>("character varying(20)", maxLength: 20, nullable: false),
                EnforceChecklistOnShiftEnd = t.Column<bool>("boolean", nullable: false, defaultValue: false),
                UpdatedAt                  = t.Column<DateTime>("timestamp with time zone", nullable: false),
                UpdatedByUserId            = t.Column<Guid>("uuid", nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_PhotoPolicies", x => x.Id);
                t.ForeignKey("FK_PhotoPolicies_Projects_ProjectId",
                    x => x.ProjectId, "Projects", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_PhotoPolicies_DistributionGroups_DigestDistributionGroupId",
                    x => x.DigestDistributionGroupId, "DistributionGroups", "Id", onDelete: ReferentialAction.SetNull);
            });
        mb.CreateIndex("IX_PhotoPolicies_ProjectId", "PhotoPolicies", "ProjectId", unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable("PhotoPolicies");
        mb.DropTable("PhotoShareLinks");
        mb.DropTable("PhotoVoiceNotes");
        mb.DropTable("PhotoAnnotations");
        mb.DropTable("PhotoChecklistItems");
        mb.DropTable("PhotoChecklists");
        mb.DropTable("PhotoAccessRules");
        mb.DropTable("PhotoAlbumPhotos");
        mb.DropTable("PhotoAlbums");
        mb.DropTable("DistributionGroupMembers");
        mb.DropTable("DistributionGroups");
    }
}
