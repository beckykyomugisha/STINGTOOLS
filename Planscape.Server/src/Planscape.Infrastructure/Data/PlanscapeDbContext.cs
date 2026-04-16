using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;

namespace Planscape.Infrastructure.Data;

public class PlanscapeDbContext : DbContext
{
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public PlanscapeDbContext(DbContextOptions<PlanscapeDbContext> options) : base(options) { }

    public PlanscapeDbContext(DbContextOptions<PlanscapeDbContext> options, IHttpContextAccessor httpContextAccessor)
        : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
    }

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
        return await base.SaveChangesAsync(cancellationToken);
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
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
    public DbSet<IssueAttachment> IssueAttachments => Set<IssueAttachment>();
    public DbSet<DocumentApproval> DocumentApprovals => Set<DocumentApproval>();
    public DbSet<PlatformConnection> PlatformConnections => Set<PlatformConnection>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();

    // Planscape MIM entities (loaded when MIM is enabled)
    public DbSet<MIM.Entities.Asset> Assets => Set<MIM.Entities.Asset>();
    public DbSet<MIM.Entities.MaintenanceTask> MaintenanceTasks => Set<MIM.Entities.MaintenanceTask>();

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
        });

        // ── BimIssue ──
        modelBuilder.Entity<BimIssue>(e =>
        {
            e.HasKey(i => i.Id);
            e.HasOne(i => i.Project).WithMany(p => p.Issues).HasForeignKey(i => i.ProjectId);
            e.HasIndex(i => new { i.ProjectId, i.IssueCode }).IsUnique();
            e.HasIndex(i => new { i.ProjectId, i.Status });
            e.HasIndex(i => i.DueDate).HasFilter("\"Status\" NOT IN ('CLOSED','RESOLVED')");
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
    }
}
