using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Data;

public class PlanscapeDbContext : DbContext
{
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ITenantContext? _tenantContext;

    public PlanscapeDbContext(DbContextOptions<PlanscapeDbContext> options) : base(options) { }

    public PlanscapeDbContext(DbContextOptions<PlanscapeDbContext> options, IHttpContextAccessor httpContextAccessor)
        : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// S1.1 — DI-friendly ctor used at runtime so the global tenant
    /// query filter has access to the resolved tenant id without
    /// reaching into HttpContext on every read. Migrations + tooling
    /// keep using the parameterless ctor (TenantContext stays null and
    /// the filter degrades to "no rows" — safe by default).
    /// </summary>
    public PlanscapeDbContext(DbContextOptions<PlanscapeDbContext> options,
                              IHttpContextAccessor httpContextAccessor,
                              ITenantContext tenantContext)
        : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Returns the resolved tenant id for query-filter use. Defaults to
    /// <see cref="Guid.Empty"/> when no tenant context is wired (design-time,
    /// EF tooling, background jobs that bypass tenant scoping). Empty matches
    /// no rows so a missing tenant context fails closed, never open.
    /// </summary>
    public Guid CurrentTenantId => _tenantContext?.TenantId ?? Guid.Empty;

    /// <summary>
    /// Background jobs / migrations / cross-tenant admin tools set this to
    /// disable the global query filter for the lifetime of the DbContext.
    /// Use sparingly and audit every call site. Defaults to false.
    /// </summary>
    public bool BypassTenantFilter { get; set; }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext != null)
        {
            foreach (var entry in ChangeTracker.Entries<AuditLog>().Where(e => e.State == EntityState.Added))
            {
                var log = entry.Entity;
                if (httpContext.Items.TryGetValue("DeviceId", out var devId) && devId is string deviceId)
                    log.DeviceId = deviceId;
                if (httpContext.Items.TryGetValue("Latitude", out var lat) && lat is double latitude)
                    log.Latitude = latitude;
                if (httpContext.Items.TryGetValue("Longitude", out var lng) && lng is double longitude)
                    log.Longitude = longitude;
                if (log.DeviceId != null)
                    log.Source = "mobile";
            }
        }

        // S1.1 — auto-stamp TenantId on every Added entity that implements
        // ITenantScoped. Prevents the second-most-common tenant leak: a
        // controller that forgot to set TenantId before saving. Existing
        // values are preserved (admin tooling can set TenantId explicitly
        // when crossing tenants is intentional).
        var tid = CurrentTenantId;
        if (tid != Guid.Empty)
        {
            foreach (var entry in ChangeTracker.Entries<ITenantScoped>().Where(e => e.State == EntityState.Added))
            {
                if (entry.Entity.TenantId == Guid.Empty)
                    entry.Entity.TenantId = tid;
            }
        }
        return await base.SaveChangesAsync(cancellationToken);
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantBranding> TenantBrandings => Set<TenantBranding>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<TaggedElement> TaggedElements => Set<TaggedElement>();
    public DbSet<BimIssue> Issues => Set<BimIssue>();
    public DbSet<DocumentRecord> Documents => Set<DocumentRecord>();
    public DbSet<LicenseKey> LicenseKeys => Set<LicenseKey>();
    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();
    public DbSet<ComplianceSnapshot> ComplianceSnapshots => Set<ComplianceSnapshot>();
    public DbSet<SeqCounter> SeqCounters => Set<SeqCounter>();
    public DbSet<Meeting> Meetings => Set<Meeting>();
    public DbSet<MeetingActionItem> MeetingActionItems => Set<MeetingActionItem>();
    public DbSet<Transmittal> Transmittals => Set<Transmittal>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<DevicePushToken> DevicePushTokens => Set<DevicePushToken>();
    public DbSet<UserNotificationPreferences> UserNotificationPreferences => Set<UserNotificationPreferences>();
    public DbSet<IssueAttachment> IssueAttachments => Set<IssueAttachment>();
    public DbSet<IssueCustomFieldSchema> IssueCustomFieldSchemas => Set<IssueCustomFieldSchema>();
    public DbSet<ProjectModel> ProjectModels => Set<ProjectModel>();
    public DbSet<IssueComment> IssueComments => Set<IssueComment>();
    public DbSet<DocumentMarkup> DocumentMarkups => Set<DocumentMarkup>();
    public DbSet<ScheduleTask> ScheduleTasks => Set<ScheduleTask>();
    public DbSet<CostItem> CostItems => Set<CostItem>();
    public DbSet<DocumentApproval> DocumentApprovals => Set<DocumentApproval>();
    public DbSet<PlatformConnection> PlatformConnections => Set<PlatformConnection>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<SyncConflict> SyncConflicts => Set<SyncConflict>();
    public DbSet<SyncWatermark> SyncWatermarks => Set<SyncWatermark>();

    // Phase 142 — daily site diary
    public DbSet<SiteDiary> SiteDiaries => Set<SiteDiary>();
    public DbSet<SiteDiaryAttachment> SiteDiaryAttachments => Set<SiteDiaryAttachment>();

    // Phase 144 — RIBA stage gates + MIDP / IE deliverables
    public DbSet<StageGate> StageGates => Set<StageGate>();
    public DbSet<InformationDeliverable> InformationDeliverables => Set<InformationDeliverable>();
    // Phase 146 — normalised criterion rows for per-row sign-off at scale
    public DbSet<StageGateCriterion> StageGateCriteria => Set<StageGateCriterion>();

    // Planscape MIM entities (loaded when MIM is enabled)
    public DbSet<MIM.Entities.Asset> Assets => Set<MIM.Entities.Asset>();
    public DbSet<MIM.Entities.MaintenanceTask> MaintenanceTasks => Set<MIM.Entities.MaintenanceTask>();

    // Phase 165 (NEW-08) — outbound webhook subscriptions.
    public DbSet<OutboundWebhook> OutboundWebhooks => Set<OutboundWebhook>();

    // S2.1 — billing entities (provider-agnostic).
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Invoice>      Invoices      => Set<Invoice>();
    public DbSet<Payment>      Payments      => Set<Payment>();
    // S3.2 — transactional outbox.
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    // S5.1 — scene-index chunks (one row per discipline/level/system slice
    // of a federated ProjectModel; mobile streams these on demand).
    public DbSet<SceneNode> SceneNodes => Set<SceneNode>();
    // S6.1 — voice notes on issues.
    public DbSet<IssueAudioNote> IssueAudioNotes => Set<IssueAudioNote>();
    // S6.2 — 3D markup polylines anchored to a model / project.
    public DbSet<ModelMarkup> ModelMarkups => Set<ModelMarkup>();
    // S6.3 — CRDT update log for collaborative pin / issue editing.
    public DbSet<PinCrdtUpdate> PinCrdtUpdates => Set<PinCrdtUpdate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Tenant ──
        modelBuilder.Entity<Tenant>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.Slug).IsUnique();
            e.Property(t => t.Name).HasMaxLength(200);
            e.Property(t => t.Slug).HasMaxLength(50);
        });

        // ── IssueCustomFieldSchema (FLEX-13) ──
        modelBuilder.Entity<IssueCustomFieldSchema>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.Key }).IsUnique();
            e.HasIndex(x => x.ProjectId);
            e.Property(x => x.Key).HasMaxLength(80);
            e.Property(x => x.Label).HasMaxLength(200);
            e.Property(x => x.HelpText).HasMaxLength(500);
            e.Property(x => x.DefaultValueJson).HasColumnType("jsonb");
            e.Property(x => x.OptionsJson).HasColumnType("jsonb");
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── ProjectModel (MODEL-VIEWER) ──
        modelBuilder.Entity<ProjectModel>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => x.ContentHash);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Discipline).HasMaxLength(8);
            e.Property(x => x.FileName).HasMaxLength(260);
            e.Property(x => x.StoragePath).HasMaxLength(600);
            e.Property(x => x.ContentHash).HasMaxLength(64);
            e.Property(x => x.ThumbnailPath).HasMaxLength(600);
            e.Property(x => x.ElementMapPath).HasMaxLength(600);
            e.Property(x => x.Units).HasMaxLength(8);
            e.Property(x => x.Revision).HasMaxLength(30);
            e.Property(x => x.UploadedBy).HasMaxLength(200);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.UploadedByUser).WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        // Issue → ProjectModel anchor (MODEL-VIEWER)
        modelBuilder.Entity<BimIssue>(e =>
        {
            e.HasIndex(x => x.ModelId);
            e.Property(x => x.ModelElementGuid).HasMaxLength(80);
        });

        // ── IssueComment (P2) ──
        modelBuilder.Entity<IssueComment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.IssueId);
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.Body).HasMaxLength(4000);
            e.Property(x => x.AuthorName).HasMaxLength(200);
            e.Property(x => x.Source).HasMaxLength(20);
            e.HasOne(x => x.Issue).WithMany().HasForeignKey(x => x.IssueId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.AuthorUser).WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.SetNull);
        });

        // ── DocumentMarkup (P3) ──
        modelBuilder.Entity<DocumentMarkup>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.DocumentId);
            e.Property(x => x.ShapesJson).HasColumnType("jsonb");
            e.Property(x => x.Summary).HasMaxLength(2000);
            e.Property(x => x.CreatedByName).HasMaxLength(200);
            e.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        // ── ScheduleTask (P4) ──
        modelBuilder.Entity<ScheduleTask>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => new { x.ProjectId, x.Code }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(80);
            e.Property(x => x.Name).HasMaxLength(400);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Discipline).HasMaxLength(8);
            e.Property(x => x.LinkedMetric).HasMaxLength(200);
            e.Property(x => x.PredecessorIds).HasMaxLength(2000);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── CostItem (P5) ──
        modelBuilder.Entity<CostItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => new { x.ProjectId, x.Code });
            e.Property(x => x.Code).HasMaxLength(80);
            e.Property(x => x.Description).HasMaxLength(400);
            e.Property(x => x.Discipline).HasMaxLength(8);
            e.Property(x => x.TradeBucket).HasMaxLength(100);
            e.Property(x => x.Unit).HasMaxLength(16);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.UnitRate).HasPrecision(18, 4);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ScheduleTask).WithMany().HasForeignKey(x => x.ScheduleTaskId).OnDelete(DeleteBehavior.SetNull);
        });

        // Phase 142 — Daily site diary
        modelBuilder.Entity<SiteDiary>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => new { x.ProjectId, x.DiaryDate });
            e.HasIndex(x => x.Status);
            e.Property(x => x.AuthorName).HasMaxLength(200);
            e.Property(x => x.AuthorRole).HasMaxLength(40);
            e.Property(x => x.Status).HasMaxLength(24);
            e.Property(x => x.Weather).HasMaxLength(200);
            e.Property(x => x.Narrative).HasColumnType("text");
            e.Property(x => x.ManpowerByTradeJson).HasColumnType("jsonb");
            e.Property(x => x.EquipmentJson).HasColumnType("jsonb");
            e.Property(x => x.DeliveriesJson).HasColumnType("jsonb");
            e.Property(x => x.ChecklistJson).HasColumnType("jsonb");
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.AuthorUser).WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<SiteDiaryAttachment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SiteDiaryId);
            e.HasIndex(x => x.DocumentId);
            e.Property(x => x.AttachedBy).HasMaxLength(200);
            e.Property(x => x.Caption).HasMaxLength(400);
            e.HasOne(x => x.SiteDiary).WithMany(x => x.Attachments).HasForeignKey(x => x.SiteDiaryId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });

        // Phase 144 — RIBA stage gates + MIDP deliverables
        modelBuilder.Entity<StageGate>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => new { x.ProjectId, x.StageCode }).IsUnique();
            e.HasIndex(x => x.Status);
            e.Property(x => x.StageCode).HasMaxLength(40);
            e.Property(x => x.StageName).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(24);
            e.Property(x => x.Description).HasColumnType("text");
            e.Property(x => x.CriteriaJson).HasColumnType("jsonb");
            e.Property(x => x.DecidedBy).HasMaxLength(200);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });
        // Phase 146 — normalised criterion rows
        modelBuilder.Entity<StageGateCriterion>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.StageGateId);
            e.HasIndex(x => new { x.StageGateId, x.Key }).IsUnique();
            e.Property(x => x.Key).HasMaxLength(80);
            e.Property(x => x.Label).HasMaxLength(400);
            e.Property(x => x.Description).HasColumnType("text");
            e.Property(x => x.Comment).HasColumnType("text");
            e.Property(x => x.SignedBy).HasMaxLength(200);
            e.HasOne(x => x.StageGate).WithMany().HasForeignKey(x => x.StageGateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InformationDeliverable>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => new { x.ProjectId, x.Code }).IsUnique();
            e.HasIndex(x => x.StageGateId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.DueDate);
            e.Property(x => x.Code).HasMaxLength(80);
            e.Property(x => x.Title).HasMaxLength(400);
            e.Property(x => x.Description).HasColumnType("text");
            e.Property(x => x.Type).HasMaxLength(8);
            e.Property(x => x.OwnerRole).HasMaxLength(8);
            e.Property(x => x.Discipline).HasMaxLength(8);
            e.Property(x => x.SuitabilityTarget).HasMaxLength(8);
            e.Property(x => x.Status).HasMaxLength(24);
            e.Property(x => x.SubmittedBy).HasMaxLength(200);
            e.Property(x => x.AcceptedBy).HasMaxLength(200);
            e.Property(x => x.RejectionReason).HasColumnType("text");
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.StageGate).WithMany(s => s.Deliverables).HasForeignKey(x => x.StageGateId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.SetNull);
        });

        // ── BimIssue.CustomFields JSONB (FLEX-13) ──
        // Decision 4.5 = (c) — JSONB column with GIN index. The GIN index is
        // created by the migration (raw SQL) rather than through HasIndex, to
        // avoid the model snapshot trying to re-create/re-drop it on later
        // unrelated migrations.
        modelBuilder.Entity<BimIssue>(e =>
        {
            e.Property(x => x.CustomFields).HasColumnType("jsonb");
        });

        // ── TenantBranding (FLEX-03) ──
        modelBuilder.Entity<TenantBranding>(e =>
        {
            e.HasKey(b => b.Id);
            e.HasIndex(b => b.TenantId).IsUnique();
            // S15 — direction is "Tenant (principal) → TenantBranding (dependent)".
            // OnDelete(Cascade) here means: deleting a Tenant cascade-deletes
            // its branding row. It does NOT mean deleting a branding row
            // deletes the tenant — the FK runs from branding to tenant, not
            // the reverse. Previous audit flagged this; the configuration is
            // correct as written.
            e.HasOne(b => b.Tenant).WithMany().HasForeignKey(b => b.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.Property(b => b.ProductName).HasMaxLength(100);
            e.Property(b => b.AccentColor).HasMaxLength(20);
            e.Property(b => b.HeaderColor).HasMaxLength(20);
            e.Property(b => b.LogoUrl).HasMaxLength(500);
            e.Property(b => b.SupportEmail).HasMaxLength(200);
            e.Property(b => b.EmailFromName).HasMaxLength(100);
            e.Property(b => b.EmailFromAddress).HasMaxLength(200);
            e.Property(b => b.EmailSignature).HasMaxLength(2000);
            e.Property(b => b.DefaultLanguage).HasMaxLength(8);
        });

        // ── AppUser ──
        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Email).IsUnique();
            e.HasOne(u => u.Tenant).WithMany(t => t.Users).HasForeignKey(u => u.TenantId);
        });

        // ── Project ──
        modelBuilder.Entity<Project>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasOne(p => p.Tenant).WithMany(t => t.Projects).HasForeignKey(p => p.TenantId);
            e.HasIndex(p => new { p.TenantId, p.Code }).IsUnique();
            // Phase 175 — author lookup for the visibility predicate
            e.HasIndex(p => new { p.TenantId, p.CreatedById });
        });

        // ── TaggedElement ──
        modelBuilder.Entity<TaggedElement>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasOne(t => t.Project).WithMany(p => p.Elements).HasForeignKey(t => t.ProjectId);
            e.HasIndex(t => new { t.ProjectId, t.RevitElementId }).IsUnique();
            e.HasIndex(t => t.Tag1);
            e.HasIndex(t => t.Disc);
            e.HasIndex(t => t.IsStale);
            // Delta-sync cutoff queries (`(LastModifiedUtc ?? SyncedAt) > cutoff`)
            // benefit from an index on the modification timestamp.
            e.HasIndex(t => t.LastModifiedUtc);
        });

        // ── BimIssue ──
        modelBuilder.Entity<BimIssue>(e =>
        {
            e.HasKey(i => i.Id);
            e.HasOne(i => i.Project).WithMany(p => p.Issues).HasForeignKey(i => i.ProjectId);
            e.HasIndex(i => new { i.ProjectId, i.IssueCode }).IsUnique();
            e.HasIndex(i => new { i.ProjectId, i.Status });
            e.HasIndex(i => i.DueDate).HasFilter("\"Status\" NOT IN ('CLOSED','RESOLVED')");
            // NEW-SRV-23: nullable FK to AppUser for assignee + creator (SetNull on user delete)
            e.HasOne(i => i.AssigneeUser).WithMany().HasForeignKey(i => i.AssigneeUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(i => i.CreatedByUser).WithMany().HasForeignKey(i => i.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(i => new { i.ProjectId, i.AssigneeUserId });
            e.Property(i => i.AssigneeEmail).HasMaxLength(320);
            e.Property(i => i.DeviceId).HasMaxLength(120);
            e.Property(i => i.Source).HasMaxLength(20);
        });

        // ── IssueAttachment ──
        modelBuilder.Entity<IssueAttachment>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasIndex(a => new { a.IssueId, a.DocumentId }).IsUnique();
            e.HasOne(a => a.Issue).WithMany(i => i.Attachments).HasForeignKey(a => a.IssueId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Document).WithMany().HasForeignKey(a => a.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── DocumentRecord ──
        modelBuilder.Entity<DocumentRecord>(e =>
        {
            e.HasKey(d => d.Id);
            e.HasOne(d => d.Project).WithMany(p => p.Documents).HasForeignKey(d => d.ProjectId);
            e.HasIndex(d => new { d.ProjectId, d.CdeStatus });
            e.HasIndex(d => new { d.ProjectId, d.Discipline });
            e.HasIndex(d => new { d.ProjectId, d.UploadedAt });
            e.Property(d => d.Description).HasMaxLength(1000);
            e.Property(d => d.Originator).HasMaxLength(50);
        });

        // ── DocumentApproval ──
        modelBuilder.Entity<DocumentApproval>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasOne(a => a.Document).WithMany().HasForeignKey(a => a.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Project).WithMany().HasForeignKey(a => a.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => new { a.DocumentId, a.Transition, a.Status });
        });

        // ── LicenseKey ──
        modelBuilder.Entity<LicenseKey>(e =>
        {
            e.HasKey(l => l.Id);
            e.HasIndex(l => l.Key).IsUnique();
            e.HasOne(l => l.Tenant).WithMany(t => t.LicenseKeys).HasForeignKey(l => l.TenantId);
        });

        // ── WorkflowRun ──
        modelBuilder.Entity<WorkflowRun>(e =>
        {
            e.HasKey(w => w.Id);
            e.HasOne(w => w.Project).WithMany(p => p.WorkflowRuns).HasForeignKey(w => w.ProjectId);
        });

        // ── ComplianceSnapshot ──
        modelBuilder.Entity<ComplianceSnapshot>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasOne(s => s.Project).WithMany().HasForeignKey(s => s.ProjectId);
            e.HasIndex(s => new { s.ProjectId, s.CapturedAt });
        });

        // ── SeqCounter ──
        modelBuilder.Entity<SeqCounter>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasOne(s => s.Project).WithMany().HasForeignKey(s => s.ProjectId);
            e.HasIndex(s => new { s.ProjectId, s.CounterKey }).IsUnique();
        });

        // ── Meeting ──
        modelBuilder.Entity<Meeting>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasOne(m => m.Project).WithMany().HasForeignKey(m => m.ProjectId);
            e.HasIndex(m => new { m.ProjectId, m.ScheduledAt });
        });

        // ── MeetingActionItem ──
        modelBuilder.Entity<MeetingActionItem>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasOne(a => a.Meeting).WithMany(m => m.ActionItems).HasForeignKey(a => a.MeetingId);
        });

        // ── Transmittal ──
        modelBuilder.Entity<Transmittal>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasOne(t => t.Project).WithMany().HasForeignKey(t => t.ProjectId);
            e.HasIndex(t => new { t.ProjectId, t.TransmittalCode }).IsUnique();
        });

        // ── AuditLog ──
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasIndex(a => new { a.TenantId, a.Timestamp });
            e.HasIndex(a => new { a.ProjectId, a.Timestamp });
        });

        // ── ProjectMember ──
        modelBuilder.Entity<ProjectMember>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => new { m.ProjectId, m.UserId }).IsUnique();
            e.HasOne(m => m.Project).WithMany().HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── DevicePushToken ──
        modelBuilder.Entity<DevicePushToken>(e =>
        {
            e.HasKey(d => d.Id);
            e.HasIndex(d => new { d.UserId, d.Token }).IsUnique();
            e.HasIndex(d => d.TenantId);
            e.HasOne(d => d.User).WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.Tenant).WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.Property(d => d.Token).HasMaxLength(512);
            e.Property(d => d.DeviceName).HasMaxLength(200);
        });

        // ── UserNotificationPreferences (NEW-FLEX-12) ──
        modelBuilder.Entity<UserNotificationPreferences>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.UserId).IsUnique();
            e.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Tenant).WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.Property(p => p.Channel).HasMaxLength(20);
            e.Property(p => p.QuietHoursStart).HasMaxLength(5);
            e.Property(p => p.QuietHoursEnd).HasMaxLength(5);
            e.Property(p => p.TimeZone).HasMaxLength(64);
        });

        // ── PlatformConnection ──
        modelBuilder.Entity<PlatformConnection>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => new { c.TenantId, c.ProjectId, c.Platform }).IsUnique();
            e.HasIndex(c => c.TenantId);
            e.HasOne(c => c.Tenant).WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Project).WithMany().HasForeignKey(c => c.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.ExternalProjectId).HasMaxLength(500);
            e.Property(c => c.AccessToken).HasMaxLength(4000);
            e.Property(c => c.RefreshToken).HasMaxLength(4000);
            e.Property(c => c.WebhookSecret).HasMaxLength(500);
        });

        // ── DocumentVersion ──
        modelBuilder.Entity<DocumentVersion>(e =>
        {
            e.HasKey(v => v.Id);
            e.HasOne(v => v.Document).WithMany(d => d.Versions).HasForeignKey(v => v.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(v => new { v.DocumentId, v.VersionNumber }).IsUnique();
        });

        // ── SyncWatermark (S06 — per-device delta-sync cursor) ──
        modelBuilder.Entity<SyncWatermark>(e =>
        {
            e.HasKey(w => w.Id);
            e.Property(w => w.DeviceId).HasMaxLength(128).IsRequired();
            // One watermark per (project, device) — upserts key on this pair.
            e.HasIndex(w => new { w.ProjectId, w.DeviceId }).IsUnique();
            // Tenant scope filtering on admin/list endpoints.
            e.HasIndex(w => w.TenantId);
            // "Most recent activity" queries / monitoring sort by LastSyncUtc.
            e.HasIndex(w => w.LastSyncUtc);
            e.HasOne(w => w.Project).WithMany().HasForeignKey(w => w.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── SyncConflict (S01 — log of last-write-wins sync rejections) ──
        modelBuilder.Entity<SyncConflict>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.ElementId).HasMaxLength(128).IsRequired();
            e.Property(c => c.ConflictType).HasMaxLength(40).IsRequired();
            e.Property(c => c.Resolution).HasMaxLength(20).IsRequired();
            // Drill-down: list every conflict that happened on a given element.
            e.HasIndex(c => new { c.ProjectId, c.ElementId });
            // Time-windowed queries ("conflicts in the last 24h") + dashboard sort.
            e.HasIndex(c => c.DetectedAt);
            // Tenant scope filtering for cross-project admin views.
            e.HasIndex(c => c.TenantId);
            e.HasOne(c => c.Project).WithMany().HasForeignKey(c => c.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.TaggedElement).WithMany().HasForeignKey(c => c.TaggedElementId).OnDelete(DeleteBehavior.SetNull);
        });

        // ── Planscape MIM Entities ──
        modelBuilder.Entity<MIM.Entities.Asset>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasIndex(a => new { a.ProjectId, a.AssetTag }).IsUnique();
        });

        modelBuilder.Entity<MIM.Entities.MaintenanceTask>(e =>
        {
            e.HasKey(m => m.Id);
        });

        // ── OutboundWebhook (Phase 165 / NEW-08) ──
        modelBuilder.Entity<OutboundWebhook>(e =>
        {
            e.HasKey(w => w.Id);
            e.HasIndex(w => new { w.TenantId, w.EventType, w.IsActive });
            e.HasIndex(w => w.ProjectId);
            e.Property(w => w.TargetUrl).IsRequired().HasMaxLength(2048);
            e.Property(w => w.SecretHash).IsRequired().HasMaxLength(128);
            e.HasOne(w => w.Tenant).WithMany().HasForeignKey(w => w.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(w => w.Project).WithMany().HasForeignKey(w => w.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── S2.1 — Billing entities ─────────────────────────────────────
        modelBuilder.Entity<Subscription>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasIndex(x => x.ProviderSubscriptionId);
            e.Property(x => x.Provider).IsRequired().HasMaxLength(20);
            e.Property(x => x.Plan).IsRequired().HasMaxLength(20);
            e.Property(x => x.Currency).IsRequired().HasMaxLength(3);
            e.Property(x => x.ProviderCustomerId).HasMaxLength(120);
            e.Property(x => x.ProviderSubscriptionId).HasMaxLength(120);
        });
        modelBuilder.Entity<Invoice>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.InvoiceNumber }).IsUnique();
            e.HasIndex(x => x.SubscriptionId);
            e.HasIndex(x => x.Status);
            e.Property(x => x.Provider).IsRequired().HasMaxLength(20);
            e.Property(x => x.ProviderInvoiceId).HasMaxLength(120);
            e.Property(x => x.InvoiceNumber).IsRequired().HasMaxLength(40);
            e.Property(x => x.Currency).IsRequired().HasMaxLength(3);
            e.Property(x => x.PdfStoragePath).HasMaxLength(500);
            e.Property(x => x.PurchaseOrderRef).HasMaxLength(80);
        });
        modelBuilder.Entity<Payment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.InvoiceId);
            e.HasIndex(x => new { x.Provider, x.ProviderEventId }).IsUnique()
                .HasFilter("\"ProviderEventId\" IS NOT NULL");
            e.Property(x => x.Provider).IsRequired().HasMaxLength(20);
            e.Property(x => x.ProviderTransactionId).HasMaxLength(120);
            e.Property(x => x.ProviderEventId).HasMaxLength(120);
            e.Property(x => x.Currency).IsRequired().HasMaxLength(3);
            e.Property(x => x.MethodKind).HasMaxLength(40);
            e.Property(x => x.MethodSuffix).HasMaxLength(20);
        });

        // ── S1.1 — Global tenant query filter ───────────────────────────
        // Every entity that implements ITenantScoped is filtered by the
        // currently-resolved tenant id automatically. The filter degrades
        // to "no rows" when no tenant context is set (Guid.Empty), so an
        // unauthenticated background job sees nothing rather than
        // everything. Hot reads use the indexed TenantId column directly,
        // no joins required.
        ApplyTenantQueryFilters(modelBuilder);
        // Ensure every tenant-scoped entity has an index on TenantId so the
        // query filter doesn't degenerate into a sequential scan.
        AddTenantIdIndexes(modelBuilder);
    }

    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        var entityTypes = modelBuilder.Model.GetEntityTypes()
            .Where(t => typeof(ITenantScoped).IsAssignableFrom(t.ClrType));

        foreach (var entityType in entityTypes)
        {
            var clrType = entityType.ClrType;
            var parameter = System.Linq.Expressions.Expression.Parameter(clrType, "e");
            var tenantIdProperty = System.Linq.Expressions.Expression.Property(parameter, nameof(ITenantScoped.TenantId));
            var currentTenantIdProperty = System.Linq.Expressions.Expression.Property(
                System.Linq.Expressions.Expression.Constant(this), nameof(CurrentTenantId));
            var bypass = System.Linq.Expressions.Expression.Property(
                System.Linq.Expressions.Expression.Constant(this), nameof(BypassTenantFilter));
            var equality = System.Linq.Expressions.Expression.Equal(tenantIdProperty, currentTenantIdProperty);
            var body = System.Linq.Expressions.Expression.OrElse(bypass, equality);
            var lambda = System.Linq.Expressions.Expression.Lambda(body, parameter);
            modelBuilder.Entity(clrType).HasQueryFilter(lambda);
        }
    }

    private static void AddTenantIdIndexes(ModelBuilder modelBuilder)
    {
        var entityTypes = modelBuilder.Model.GetEntityTypes()
            .Where(t => typeof(ITenantScoped).IsAssignableFrom(t.ClrType));
        foreach (var entityType in entityTypes)
        {
            var hasIndex = entityType.GetIndexes().Any(i =>
                i.Properties.Count == 1 && i.Properties[0].Name == nameof(ITenantScoped.TenantId));
            if (!hasIndex)
                modelBuilder.Entity(entityType.ClrType).HasIndex(nameof(ITenantScoped.TenantId));
        }
    }
}
