using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable enable

namespace StingBIM.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── Tenants ──────────────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "Tenants",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ContactEmail = table.Column<string>(type: "text", nullable: false),
                Tier = table.Column<int>(type: "integer", nullable: false),
                MimEnabled = table.Column<bool>(type: "boolean", nullable: false),
                MimTier = table.Column<int>(type: "integer", nullable: false),
                MaxUsers = table.Column<int>(type: "integer", nullable: false),
                MaxProjects = table.Column<int>(type: "integer", nullable: false),
                StorageLimitBytes = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                TrialExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                StripeCustomerId = table.Column<string>(type: "text", nullable: true),
                StripeSubscriptionId = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Tenants", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Tenants_Slug",
            table: "Tenants",
            column: "Slug",
            unique: true);

        // ── Users ────────────────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "Users",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                Email = table.Column<string>(type: "text", nullable: false),
                DisplayName = table.Column<string>(type: "text", nullable: false),
                PasswordHash = table.Column<string>(type: "text", nullable: false),
                Role = table.Column<int>(type: "integer", nullable: false),
                Iso19650Role = table.Column<string>(type: "text", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RefreshToken = table.Column<string>(type: "text", nullable: true),
                RefreshTokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Users", x => x.Id);
                table.ForeignKey(
                    name: "FK_Users_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Users_Email",
            table: "Users",
            column: "Email",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Users_TenantId",
            table: "Users",
            column: "TenantId");

        // ── Projects ─────────────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "Projects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                Code = table.Column<string>(type: "text", nullable: false),
                Description = table.Column<string>(type: "text", nullable: true),
                Phase = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                TagSeparator = table.Column<string>(type: "text", nullable: false),
                SeqNumPad = table.Column<int>(type: "integer", nullable: false),
                TagPrefix = table.Column<string>(type: "text", nullable: true),
                TagSuffix = table.Column<string>(type: "text", nullable: true),
                ConfigJson = table.Column<string>(type: "text", nullable: true),
                CompliancePercent = table.Column<double>(type: "double precision", nullable: false),
                ContainerCompliancePercent = table.Column<double>(type: "double precision", nullable: false),
                TotalElements = table.Column<int>(type: "integer", nullable: false),
                TaggedElements = table.Column<int>(type: "integer", nullable: false),
                WarningCount = table.Column<int>(type: "integer", nullable: false),
                RagStatus = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Projects", x => x.Id);
                table.ForeignKey(
                    name: "FK_Projects_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Projects_TenantId_Code",
            table: "Projects",
            columns: new[] { "TenantId", "Code" },
            unique: true);

        // ── LicenseKeys ──────────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "LicenseKeys",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                Key = table.Column<string>(type: "text", nullable: false),
                Tier = table.Column<int>(type: "integer", nullable: false),
                MimEnabled = table.Column<bool>(type: "boolean", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                MaxActivations = table.Column<int>(type: "integer", nullable: false),
                CurrentActivations = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ActivatedMachineIds = table.Column<string>(type: "text", nullable: true),
                LastActivatedBy = table.Column<string>(type: "text", nullable: true),
                LastActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LicenseKeys", x => x.Id);
                table.ForeignKey(
                    name: "FK_LicenseKeys_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_LicenseKeys_Key",
            table: "LicenseKeys",
            column: "Key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_LicenseKeys_TenantId",
            table: "LicenseKeys",
            column: "TenantId");

        // ── TaggedElements ───────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "TaggedElements",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                RevitElementId = table.Column<long>(type: "bigint", nullable: false),
                UniqueId = table.Column<string>(type: "text", nullable: false),
                Disc = table.Column<string>(type: "text", nullable: false),
                Loc = table.Column<string>(type: "text", nullable: false),
                Zone = table.Column<string>(type: "text", nullable: false),
                Lvl = table.Column<string>(type: "text", nullable: false),
                Sys = table.Column<string>(type: "text", nullable: false),
                Func = table.Column<string>(type: "text", nullable: false),
                Prod = table.Column<string>(type: "text", nullable: false),
                Seq = table.Column<string>(type: "text", nullable: false),
                Tag1 = table.Column<string>(type: "text", nullable: false),
                Tag7 = table.Column<string>(type: "text", nullable: true),
                Tag7A = table.Column<string>(type: "text", nullable: true),
                Tag7B = table.Column<string>(type: "text", nullable: true),
                Tag7C = table.Column<string>(type: "text", nullable: true),
                Tag7D = table.Column<string>(type: "text", nullable: true),
                Tag7E = table.Column<string>(type: "text", nullable: true),
                Tag7F = table.Column<string>(type: "text", nullable: true),
                CategoryName = table.Column<string>(type: "text", nullable: false),
                FamilyName = table.Column<string>(type: "text", nullable: false),
                TypeName = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(type: "text", nullable: true),
                Rev = table.Column<string>(type: "text", nullable: true),
                GridRef = table.Column<string>(type: "text", nullable: true),
                RoomName = table.Column<string>(type: "text", nullable: true),
                Level = table.Column<string>(type: "text", nullable: true),
                IsStale = table.Column<bool>(type: "boolean", nullable: false),
                IsComplete = table.Column<bool>(type: "boolean", nullable: false),
                IsFullyResolved = table.Column<bool>(type: "boolean", nullable: false),
                ValidationErrors = table.Column<string>(type: "text", nullable: true),
                PreviousTag = table.Column<string>(type: "text", nullable: true),
                TagModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                SyncedBy = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TaggedElements", x => x.Id);
                table.ForeignKey(
                    name: "FK_TaggedElements_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TaggedElements_ProjectId_RevitElementId",
            table: "TaggedElements",
            columns: new[] { "ProjectId", "RevitElementId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TaggedElements_Tag1",
            table: "TaggedElements",
            column: "Tag1");

        migrationBuilder.CreateIndex(
            name: "IX_TaggedElements_Disc",
            table: "TaggedElements",
            column: "Disc");

        migrationBuilder.CreateIndex(
            name: "IX_TaggedElements_IsStale",
            table: "TaggedElements",
            column: "IsStale");

        // ── Issues ───────────────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "Issues",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                IssueCode = table.Column<string>(type: "text", nullable: false),
                Type = table.Column<string>(type: "text", nullable: false),
                Title = table.Column<string>(type: "text", nullable: false),
                Description = table.Column<string>(type: "text", nullable: true),
                Priority = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(type: "text", nullable: false),
                Assignee = table.Column<string>(type: "text", nullable: true),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Discipline = table.Column<string>(type: "text", nullable: true),
                Revision = table.Column<string>(type: "text", nullable: true),
                LinkedElementIds = table.Column<string>(type: "text", nullable: true),
                BcfGuid = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Issues", x => x.Id);
                table.ForeignKey(
                    name: "FK_Issues_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Issues_ProjectId_IssueCode",
            table: "Issues",
            columns: new[] { "ProjectId", "IssueCode" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Issues_ProjectId_Status",
            table: "Issues",
            columns: new[] { "ProjectId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_Issues_DueDate",
            table: "Issues",
            column: "DueDate",
            filter: "\"Status\" NOT IN ('CLOSED','RESOLVED')");

        // ── Documents ────────────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "Documents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                FileName = table.Column<string>(type: "text", nullable: false),
                FilePath = table.Column<string>(type: "text", nullable: true),
                DocumentType = table.Column<string>(type: "text", nullable: false),
                CdeStatus = table.Column<string>(type: "text", nullable: false),
                SuitabilityCode = table.Column<string>(type: "text", nullable: false),
                Revision = table.Column<string>(type: "text", nullable: true),
                Discipline = table.Column<string>(type: "text", nullable: true),
                FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                ContentHash = table.Column<string>(type: "text", nullable: true),
                UploadedBy = table.Column<string>(type: "text", nullable: false),
                UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                StatusHistoryJson = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Documents", x => x.Id);
                table.ForeignKey(
                    name: "FK_Documents_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Documents_ProjectId_CdeStatus",
            table: "Documents",
            columns: new[] { "ProjectId", "CdeStatus" });

        // ── WorkflowRuns ─────────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "WorkflowRuns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                PresetName = table.Column<string>(type: "text", nullable: false),
                UserName = table.Column<string>(type: "text", nullable: false),
                StepsPassed = table.Column<int>(type: "integer", nullable: false),
                StepsFailed = table.Column<int>(type: "integer", nullable: false),
                StepsSkipped = table.Column<int>(type: "integer", nullable: false),
                DurationMs = table.Column<double>(type: "double precision", nullable: false),
                ComplianceBefore = table.Column<double>(type: "double precision", nullable: false),
                ComplianceAfter = table.Column<double>(type: "double precision", nullable: false),
                StepResultsJson = table.Column<string>(type: "text", nullable: true),
                ExecutedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WorkflowRuns", x => x.Id);
                table.ForeignKey(
                    name: "FK_WorkflowRuns_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowRuns_ProjectId",
            table: "WorkflowRuns",
            column: "ProjectId");

        // ── ComplianceSnapshots ──────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "ComplianceSnapshots",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                CapturedBy = table.Column<string>(type: "text", nullable: false),
                CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                TotalElements = table.Column<int>(type: "integer", nullable: false),
                TaggedComplete = table.Column<int>(type: "integer", nullable: false),
                TaggedIncomplete = table.Column<int>(type: "integer", nullable: false),
                Untagged = table.Column<int>(type: "integer", nullable: false),
                FullyResolved = table.Column<int>(type: "integer", nullable: false),
                StaleCount = table.Column<int>(type: "integer", nullable: false),
                PlaceholderCount = table.Column<int>(type: "integer", nullable: false),
                WarningCount = table.Column<int>(type: "integer", nullable: false),
                WarningHealthScore = table.Column<int>(type: "integer", nullable: false),
                TagPercent = table.Column<double>(type: "double precision", nullable: false),
                StrictPercent = table.Column<double>(type: "double precision", nullable: false),
                ContainerPercent = table.Column<double>(type: "double precision", nullable: false),
                RagStatus = table.Column<string>(type: "text", nullable: false),
                ByDisciplineJson = table.Column<string>(type: "text", nullable: true),
                ByPhaseJson = table.Column<string>(type: "text", nullable: true),
                EmptyTokenCountsJson = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ComplianceSnapshots", x => x.Id);
                table.ForeignKey(
                    name: "FK_ComplianceSnapshots_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ComplianceSnapshots_ProjectId_CapturedAt",
            table: "ComplianceSnapshots",
            columns: new[] { "ProjectId", "CapturedAt" });

        // ── SeqCounters ──────────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "SeqCounters",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                CounterKey = table.Column<string>(type: "text", nullable: false),
                CurrentValue = table.Column<int>(type: "integer", nullable: false),
                UpdatedBy = table.Column<string>(type: "text", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SeqCounters", x => x.Id);
                table.ForeignKey(
                    name: "FK_SeqCounters_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SeqCounters_ProjectId_CounterKey",
            table: "SeqCounters",
            columns: new[] { "ProjectId", "CounterKey" },
            unique: true);

        // ── Meetings ─────────────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "Meetings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                Title = table.Column<string>(type: "text", nullable: false),
                MeetingType = table.Column<string>(type: "text", nullable: false),
                ScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Minutes = table.Column<string>(type: "text", nullable: true),
                AgendaJson = table.Column<string>(type: "text", nullable: true),
                AttendeesJson = table.Column<string>(type: "text", nullable: true),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Meetings", x => x.Id);
                table.ForeignKey(
                    name: "FK_Meetings_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Meetings_ProjectId_ScheduledAt",
            table: "Meetings",
            columns: new[] { "ProjectId", "ScheduledAt" });

        // ── MeetingActionItems ───────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "MeetingActionItems",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                MeetingId = table.Column<Guid>(type: "uuid", nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                Assignee = table.Column<string>(type: "text", nullable: true),
                DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Status = table.Column<string>(type: "text", nullable: false),
                LinkedIssueId = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MeetingActionItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_MeetingActionItems_Meetings_MeetingId",
                    column: x => x.MeetingId,
                    principalTable: "Meetings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MeetingActionItems_MeetingId",
            table: "MeetingActionItems",
            column: "MeetingId");

        // ── Transmittals ─────────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "Transmittals",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                TransmittalCode = table.Column<string>(type: "text", nullable: false),
                Recipient = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(type: "text", nullable: false),
                Notes = table.Column<string>(type: "text", nullable: true),
                DocumentIdsJson = table.Column<string>(type: "text", nullable: true),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Transmittals", x => x.Id);
                table.ForeignKey(
                    name: "FK_Transmittals_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Transmittals_ProjectId_TransmittalCode",
            table: "Transmittals",
            columns: new[] { "ProjectId", "TransmittalCode" },
            unique: true);

        // ── AuditLogs ────────────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "AuditLogs",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                UserId = table.Column<Guid>(type: "uuid", nullable: true),
                Action = table.Column<string>(type: "text", nullable: false),
                EntityType = table.Column<string>(type: "text", nullable: false),
                EntityId = table.Column<string>(type: "text", nullable: true),
                DetailsJson = table.Column<string>(type: "text", nullable: true),
                IpAddress = table.Column<string>(type: "text", nullable: true),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuditLogs", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AuditLogs_TenantId_Timestamp",
            table: "AuditLogs",
            columns: new[] { "TenantId", "Timestamp" });

        migrationBuilder.CreateIndex(
            name: "IX_AuditLogs_ProjectId_Timestamp",
            table: "AuditLogs",
            columns: new[] { "ProjectId", "Timestamp" });

        // ── ProjectMembers ───────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "ProjectMembers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectRole = table.Column<string>(type: "text", nullable: false),
                Iso19650Role = table.Column<string>(type: "text", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                InvitedBy = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectMembers", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProjectMembers_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ProjectMembers_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectMembers_ProjectId_UserId",
            table: "ProjectMembers",
            columns: new[] { "ProjectId", "UserId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ProjectMembers_UserId",
            table: "ProjectMembers",
            column: "UserId");

        // ── DevicePushTokens ─────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "DevicePushTokens",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                Token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                Platform = table.Column<int>(type: "integer", nullable: false),
                DeviceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DevicePushTokens", x => x.Id);
                table.ForeignKey(
                    name: "FK_DevicePushTokens_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_DevicePushTokens_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DevicePushTokens_UserId_Token",
            table: "DevicePushTokens",
            columns: new[] { "UserId", "Token" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DevicePushTokens_TenantId",
            table: "DevicePushTokens",
            column: "TenantId");

        // ── Assets (StingMIM) ────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "Assets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                TaggedElementId = table.Column<Guid>(type: "uuid", nullable: true),
                AssetTag = table.Column<string>(type: "text", nullable: false),
                AssetName = table.Column<string>(type: "text", nullable: false),
                Manufacturer = table.Column<string>(type: "text", nullable: true),
                ModelNumber = table.Column<string>(type: "text", nullable: true),
                SerialNumber = table.Column<string>(type: "text", nullable: true),
                BarCode = table.Column<string>(type: "text", nullable: true),
                CategoryName = table.Column<string>(type: "text", nullable: false),
                FamilyName = table.Column<string>(type: "text", nullable: false),
                UniclassCode = table.Column<string>(type: "text", nullable: true),
                OmniClassCode = table.Column<string>(type: "text", nullable: true),
                CobieType = table.Column<string>(type: "text", nullable: true),
                CobieSpace = table.Column<string>(type: "text", nullable: true),
                Discipline = table.Column<string>(type: "text", nullable: false),
                SystemCode = table.Column<string>(type: "text", nullable: false),
                FunctionCode = table.Column<string>(type: "text", nullable: false),
                ProductCode = table.Column<string>(type: "text", nullable: false),
                Location = table.Column<string>(type: "text", nullable: false),
                Level = table.Column<string>(type: "text", nullable: false),
                LifecycleStatus = table.Column<string>(type: "text", nullable: false),
                CriticalityRating = table.Column<string>(type: "text", nullable: true),
                InstallationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CommissioningDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ExpectedLifeYears = table.Column<int>(type: "integer", nullable: true),
                ExpectedReplacementDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ConditionGrade = table.Column<string>(type: "text", nullable: false),
                ConditionScore = table.Column<double>(type: "double precision", nullable: true),
                WarrantyProvider = table.Column<string>(type: "text", nullable: true),
                WarrantyStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                WarrantyEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                WarrantyDurationMonths = table.Column<int>(type: "integer", nullable: true),
                CapitalCost = table.Column<decimal>(type: "numeric", nullable: true),
                ReplacementCost = table.Column<decimal>(type: "numeric", nullable: true),
                AnnualMaintenanceCost = table.Column<decimal>(type: "numeric", nullable: true),
                CostCurrency = table.Column<string>(type: "text", nullable: true),
                Building = table.Column<string>(type: "text", nullable: true),
                Floor = table.Column<string>(type: "text", nullable: true),
                Room = table.Column<string>(type: "text", nullable: true),
                Zone = table.Column<string>(type: "text", nullable: true),
                SensorId = table.Column<string>(type: "text", nullable: true),
                DigitalTwinId = table.Column<string>(type: "text", nullable: true),
                LastSensorReading = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SensorDataJson = table.Column<string>(type: "text", nullable: true),
                EnergyConsumptionKwh = table.Column<double>(type: "double precision", nullable: true),
                EmbodiedCarbonKgCo2 = table.Column<double>(type: "double precision", nullable: true),
                DocumentRefsJson = table.Column<string>(type: "text", nullable: true),
                SparePartsJson = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Assets", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Assets_ProjectId_AssetTag",
            table: "Assets",
            columns: new[] { "ProjectId", "AssetTag" },
            unique: true);

        // ── MaintenanceTasks (StingMIM) ──────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "MaintenanceTasks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                TaskCode = table.Column<string>(type: "text", nullable: false),
                Title = table.Column<string>(type: "text", nullable: false),
                Description = table.Column<string>(type: "text", nullable: true),
                Type = table.Column<int>(type: "integer", nullable: false),
                Priority = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(type: "text", nullable: false),
                AssignedTo = table.Column<string>(type: "text", nullable: true),
                FrequencyDays = table.Column<int>(type: "integer", nullable: false),
                ScheduledDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CompletedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                NextDueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                StandardReference = table.Column<string>(type: "text", nullable: true),
                IsStatutory = table.Column<bool>(type: "boolean", nullable: false),
                RegulatoryBody = table.Column<string>(type: "text", nullable: true),
                EstimatedCost = table.Column<decimal>(type: "numeric", nullable: true),
                ActualCost = table.Column<decimal>(type: "numeric", nullable: true),
                EstimatedHours = table.Column<double>(type: "double precision", nullable: true),
                ActualHours = table.Column<double>(type: "double precision", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MaintenanceTasks", x => x.Id);
                table.ForeignKey(
                    name: "FK_MaintenanceTasks_Assets_AssetId",
                    column: x => x.AssetId,
                    principalTable: "Assets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MaintenanceTasks_AssetId",
            table: "MaintenanceTasks",
            column: "AssetId");

        // ── IssueAttachments ────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "IssueAttachments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                AttachedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                AttachedBy = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IssueAttachments", x => x.Id);
                table.ForeignKey(
                    name: "FK_IssueAttachments_Issues_IssueId",
                    column: x => x.IssueId,
                    principalTable: "Issues",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_IssueAttachments_Documents_DocumentId",
                    column: x => x.DocumentId,
                    principalTable: "Documents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_IssueAttachments_IssueId_DocumentId",
            table: "IssueAttachments",
            columns: new[] { "IssueId", "DocumentId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_IssueAttachments_DocumentId",
            table: "IssueAttachments",
            column: "DocumentId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "IssueAttachments");
        migrationBuilder.DropTable(name: "MaintenanceTasks");
        migrationBuilder.DropTable(name: "Assets");
        migrationBuilder.DropTable(name: "DevicePushTokens");
        migrationBuilder.DropTable(name: "ProjectMembers");
        migrationBuilder.DropTable(name: "AuditLogs");
        migrationBuilder.DropTable(name: "Transmittals");
        migrationBuilder.DropTable(name: "MeetingActionItems");
        migrationBuilder.DropTable(name: "Meetings");
        migrationBuilder.DropTable(name: "SeqCounters");
        migrationBuilder.DropTable(name: "ComplianceSnapshots");
        migrationBuilder.DropTable(name: "WorkflowRuns");
        migrationBuilder.DropTable(name: "Documents");
        migrationBuilder.DropTable(name: "Issues");
        migrationBuilder.DropTable(name: "TaggedElements");
        migrationBuilder.DropTable(name: "LicenseKeys");
        migrationBuilder.DropTable(name: "Projects");
        migrationBuilder.DropTable(name: "Users");
        migrationBuilder.DropTable(name: "Tenants");
    }
}
