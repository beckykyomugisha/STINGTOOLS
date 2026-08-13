using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

namespace Planscape.Tests;

/// <summary>
/// Document sync — the hub half.
///
/// The one property worth real tests here is the tenant gate. A hub connection
/// has no HttpContext, so the DbContext's tenant query filter resolves to an
/// empty TenantId and cannot be relied on; the gate is an explicit claims check
/// (<c>HubTenantGuard.OwnsProjectAsync</c>) against a query that ignores filters.
/// Get that wrong and any authenticated user joins another firm's project group
/// by guessing a GUID — and nothing throws, so the failure is silent and the
/// symptom is one firm's document traffic arriving on another firm's machines.
///
/// These are unit tests with a hand-rolled hub context (no Moq in this repo, and
/// the existing doubles are hand-rolled too). Over-the-wire confirmation is in
/// the plan's PENDING-HUMAN-VERIFY list.
/// </summary>
public class DocumentSyncHubTests
{
    private static readonly Guid FirmA = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
    private static readonly Guid FirmB = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");
    private static readonly Guid FirmAProject = Guid.Parse("cccccccc-3333-3333-3333-333333333333");
    private static readonly Guid FirmBProject = Guid.Parse("dddddddd-4444-4444-4444-444444444444");

    private static PlanscapeDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<PlanscapeDbContext>()
            .UseInMemoryDatabase($"docsync_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new PlanscapeDbContext(options);
        db.Projects.Add(new Project { Id = FirmAProject, TenantId = FirmA, Name = "A", Code = "A-1" });
        db.Projects.Add(new Project { Id = FirmBProject, TenantId = FirmB, Name = "B", Code = "B-1" });
        db.SaveChanges();
        return db;
    }

    private static DocumentSyncHub HubFor(PlanscapeDbContext db, Guid tenantId, out FakeGroups groups)
    {
        groups = new FakeGroups();
        return new DocumentSyncHub(db)
        {
            Context = new FakeHubContext(tenantId),
            Groups = groups,
        };
    }

    // ── The gate ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task JoinProject_joins_a_project_in_the_callers_own_tenant()
    {
        using var db = NewDb();
        var hub = HubFor(db, FirmA, out var groups);

        await hub.JoinProject(FirmAProject.ToString());

        Assert.Equal(new[] { $"docsync:{FirmAProject}" }, groups.Added);
    }

    [Fact]
    public async Task JoinProject_refuses_another_firms_project()
    {
        // The regression this whole class exists for. Firm A's connection naming
        // firm B's project GUID must join nothing at all.
        using var db = NewDb();
        var hub = HubFor(db, FirmA, out var groups);

        await hub.JoinProject(FirmBProject.ToString());

        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task JoinProject_refuses_a_connection_with_no_tenant_claim()
    {
        // An absent tenant claim must be "no tenant", never a wildcard.
        using var db = NewDb();
        var hub = new DocumentSyncHub(db)
        {
            Context = new FakeHubContext(null),
            Groups = new FakeGroups(),
        };

        await hub.JoinProject(FirmAProject.ToString());

        Assert.Empty(((FakeGroups)hub.Groups).Added);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task JoinProject_ignores_a_malformed_or_empty_project_id(string projectId)
    {
        // Guid.Empty specifically: OwnsProjectAsync treats it as a refusal, and a
        // row could otherwise be matched by a default-valued ProjectId.
        using var db = NewDb();
        var hub = HubFor(db, FirmA, out var groups);

        await hub.JoinProject(projectId);

        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task JoinProject_refuses_a_project_that_does_not_exist()
    {
        using var db = NewDb();
        var hub = HubFor(db, FirmA, out var groups);

        await hub.JoinProject(Guid.NewGuid().ToString());

        Assert.Empty(groups.Added);
    }

    // ── Leaving ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task LeaveProject_removes_the_group()
    {
        using var db = NewDb();
        var hub = HubFor(db, FirmA, out var groups);

        await hub.JoinProject(FirmAProject.ToString());
        await hub.LeaveProject(FirmAProject.ToString());

        Assert.Equal(new[] { $"docsync:{FirmAProject}" }, groups.Removed);
    }

    [Fact]
    public async Task LeaveProject_is_not_tenant_gated()
    {
        // Deliberate: leaving a group you were never in is a no-op, and refusing
        // to let someone leave is not a security property. Asserting it so a
        // later "tighten every method" pass doesn't add a gate that can strand a
        // client subscribed to a project it just lost access to.
        using var db = NewDb();
        var hub = HubFor(db, FirmA, out var groups);

        await hub.LeaveProject(FirmBProject.ToString());

        Assert.Equal(new[] { $"docsync:{FirmBProject}" }, groups.Removed);
    }

    // ── Wire format ───────────────────────────────────────────────────────────

    [Fact]
    public void Group_name_is_project_scoped_and_stable()
    {
        // The group name is load-bearing in two places that must not drift apart:
        // JoinProject builds it, and NotifyDocumentChanged pushes to it. A
        // mismatch is silent — the push just reaches nobody.
        Assert.Equal($"docsync:{FirmAProject}", DocumentSyncHub.Group(FirmAProject));
        Assert.Equal(DocumentSyncHub.Group(FirmAProject), DocumentSyncHub.Group(FirmAProject));
        Assert.NotEqual(DocumentSyncHub.Group(FirmAProject), DocumentSyncHub.Group(FirmBProject));
    }

    [Fact]
    public void Payload_carries_a_reference_and_never_the_document_itself()
    {
        // The Companion answers a push by calling changed-since, which re-runs the
        // caller's ACL server-side. If the payload ever started carrying file
        // paths or content, a project-scoped push would become the thing that
        // widens what a client sees, bypassing per-user CDE narrowing entirely.
        var docId = Guid.NewGuid();
        var payload = DocumentSyncHub.Payload(FirmAProject, docId, "cde_transition", "PUBLISHED");
        var json = System.Text.Json.JsonSerializer.Serialize(payload);

        Assert.Contains(docId.ToString(), json);
        Assert.Contains("cde_transition", json);
        Assert.Contains("PUBLISHED", json);
        Assert.DoesNotContain("filePath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contentHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fileName", json, StringComparison.OrdinalIgnoreCase);
    }

    // ── Hand-rolled doubles ───────────────────────────────────────────────────

    private sealed class FakeGroups : IGroupManager
    {
        public List<string> Added { get; } = new();
        public List<string> Removed { get; } = new();

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
        {
            Added.Add(groupName);
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
        {
            Removed.Add(groupName);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHubContext : HubCallerContext
    {
        private readonly Dictionary<object, object?> _items = new();

        public FakeHubContext(Guid? tenantId)
        {
            var claims = new List<Claim>();
            if (tenantId.HasValue) claims.Add(new Claim("tenant_id", tenantId.Value.ToString()));
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }

        public override string ConnectionId => "conn-1";
        public override string? UserIdentifier => "user-1";
        public override ClaimsPrincipal? User { get; }
        public override IDictionary<object, object?> Items => _items;
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }
}
