using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planscape.API.Controllers;
using Planscape.Core.DTOs;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Planscape.Infrastructure.SignalR;

namespace Planscape.Tests;

/// <summary>
/// SUS-QR — embodied carbon and EPD on the scanned element.
///
/// WHY IT IS CARRIED AT ALL
/// ------------------------
/// The QR label on a duct is the only interface a site operative has to the
/// model. Embodied carbon is otherwise locked in a spreadsheet nobody on site
/// opens, so the scan is the one place it can reach the person standing next to
/// the asset.
///
/// THE ONE RULE THESE TESTS EXIST TO HOLD
/// --------------------------------------
/// NULL MEANS UNKNOWN. NEVER ZERO.
///
/// Revit reports an unset double as 0.0, so the lazy version of this feature
/// turns "nobody has assessed this element" into "this element emits nothing" —
/// a measurement, arriving in a carbon rollup, that no one ever made. It would
/// not error, it would not look wrong, and it would make a building's footprint
/// smaller with every un-assessed element added to it.
///
/// The second rule: an older plugin build omits these fields entirely, and a
/// blind assignment would blank an EPD reference that took someone an afternoon
/// to source — silently, on every sync from an un-upgraded seat.
/// </summary>
public class TagSyncSustainabilityTests
{
    private const string Uid = "sus-qr-stable-uid";
    private const long RevitId = 909090;

    private static PlanscapeDbContext NewDb(out Guid tenantId, out Guid projectId)
    {
        tenantId = Guid.NewGuid();
        projectId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<PlanscapeDbContext>()
            .UseInMemoryDatabase($"SusQr_{Guid.NewGuid():N}")
            // SyncElements wraps batches in an explicit RepeatableRead transaction,
            // which the InMemory provider cannot honour. Production is PostgreSQL.
            .ConfigureWarnings(w =>
                w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId
                    .TransactionIgnoredWarning))
            .Options;

        var db = new PlanscapeDbContext(options) { BypassTenantFilter = true };
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Name = "Test Org", Slug = "sus-org",
            ContactEmail = "admin@test.org", Tier = LicenseTier.Premium,
            MaxUsers = 10, MaxProjects = 5, IsActive = true,
        });
        db.Projects.Add(new Project
        {
            Id = projectId, TenantId = tenantId,
            Name = "Test Project", Code = "SUS-001", Status = ProjectStatus.Active,
        });
        db.SaveChanges();
        return db;
    }

    private static TagSyncController NewController(PlanscapeDbContext db, Guid tenantId)
    {
        var scopeFactory = new ServiceCollection().BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        var c = new TagSyncController(
            db,
            new NullHubContext<TagSyncHub>(),
            new NullHubContext<ComplianceHub>(),
            scopeFactory,
            new NullHubContext<NotificationHub>());
        c.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim("tenant_id", tenantId.ToString()) }, "TestAuth")),
            },
        };
        return c;
    }

    private static TagSyncRequest Push(Guid projectId, TagElementDto el) => new()
    {
        ProjectId = projectId,
        UserName = "Tester",
        RevitVersion = "2025",
        PluginVersion = "2.2.0",
        Elements = new List<TagElementDto> { el },
    };

    private static TagElementDto Element(
        string? epd = null, double? carbon = null, string? material = null) => new()
    {
        RevitElementId = RevitId,
        UniqueId = Uid,
        Disc = "M", Loc = "BLD1", Zone = "Z01", Lvl = "L02",
        Sys = "HVAC", Func = "SUP", Prod = "AHU", Seq = "0001",
        Tag1 = "M-BLD1-Z01-L02-HVAC-SUP-AHU-0001",
        CategoryName = "Mechanical Equipment",
        FamilyName = "AHU_Standard",
        IsComplete = true,
        IsFullyResolved = true,
        EpdRef = epd,
        EmbodiedCarbonKg = carbon,
        MaterialName = material,
    };

    // ── The round trip ────────────────────────────────────────────────────────

    [Fact]
    public async Task A_pushed_element_carries_its_carbon_and_EPD_through()
    {
        using var db = NewDb(out var tenantId, out var projectId);
        var controller = NewController(db, tenantId);

        await controller.SyncElements(Push(projectId, Element(
            epd: "EPD-NOR-2024-0912", carbon: 1842.5, material: "Galvanised Steel")));

        var saved = await db.TaggedElements.FirstAsync(e => e.UniqueId == Uid);
        Assert.Equal("EPD-NOR-2024-0912", saved.EpdRef);
        Assert.Equal(1842.5, saved.EmbodiedCarbonKg!.Value, 3);
        Assert.Equal("Galvanised Steel", saved.MaterialName);
    }

    // ── The rule: null is unknown, not zero ───────────────────────────────────

    [Fact]
    public async Task An_element_with_no_assessment_stores_NULL_not_zero()
    {
        // The defect this whole file exists to prevent. A 0 here is a claim that
        // the element emits nothing, and it would sum into a project footprint
        // indistinguishable from a real measurement.
        using var db = NewDb(out var tenantId, out var projectId);
        var controller = NewController(db, tenantId);

        await controller.SyncElements(Push(projectId, Element()));

        var saved = await db.TaggedElements.FirstAsync(e => e.UniqueId == Uid);
        Assert.Null(saved.EmbodiedCarbonKg);
        Assert.Null(saved.EpdRef);
        Assert.Null(saved.MaterialName);
    }

    [Fact]
    public async Task A_genuine_zero_is_stored_as_zero_and_is_distinguishable_from_unknown()
    {
        // The other half of the same rule. A timber element really can be assessed
        // at ~0 kgCO₂e, and that is a MEASUREMENT. If the ingest only accepted
        // non-zero values it would throw away the very result the assessment
        // produced, and the element would read as un-assessed.
        using var db = NewDb(out var tenantId, out var projectId);
        var controller = NewController(db, tenantId);

        await controller.SyncElements(Push(projectId, Element(carbon: 0.0, material: "Timber")));

        var saved = await db.TaggedElements.FirstAsync(e => e.UniqueId == Uid);
        Assert.NotNull(saved.EmbodiedCarbonKg);
        Assert.Equal(0.0, saved.EmbodiedCarbonKg!.Value, 6);
    }

    // ── Older plugin builds ───────────────────────────────────────────────────

    [Fact]
    public async Task A_push_that_omits_the_fields_does_not_blank_what_is_already_stored()
    {
        // An un-upgraded seat syncing the same model must not wipe an EPD reference
        // someone spent an afternoon sourcing. Silently, on every sync.
        using var db = NewDb(out var tenantId, out var projectId);
        var controller = NewController(db, tenantId);

        await controller.SyncElements(Push(projectId, Element(
            epd: "EPD-NOR-2024-0912", carbon: 1842.5, material: "Galvanised Steel")));

        // Second push from an older build: the fields are simply absent.
        await controller.SyncElements(Push(projectId, Element()));

        var saved = await db.TaggedElements.FirstAsync(e => e.UniqueId == Uid);
        Assert.Equal("EPD-NOR-2024-0912", saved.EpdRef);
        Assert.Equal(1842.5, saved.EmbodiedCarbonKg!.Value, 3);
        Assert.Equal("Galvanised Steel", saved.MaterialName);
    }

    [Fact]
    public async Task A_push_that_CARRIES_a_new_value_does_overwrite()
    {
        // The preserve-on-absent rule must not become preserve-always: a re-assessed
        // element has to be able to report its new figure, including a lower one.
        using var db = NewDb(out var tenantId, out var projectId);
        var controller = NewController(db, tenantId);

        await controller.SyncElements(Push(projectId, Element(carbon: 1842.5, material: "Galvanised Steel")));
        await controller.SyncElements(Push(projectId, Element(carbon: 1100.0, material: "Recycled Steel")));

        var saved = await db.TaggedElements.FirstAsync(e => e.UniqueId == Uid);
        Assert.Equal(1100.0, saved.EmbodiedCarbonKg!.Value, 3);
        Assert.Equal("Recycled Steel", saved.MaterialName);
    }

    [Fact]
    public async Task A_re_assessment_down_to_zero_still_overwrites()
    {
        // The nastiest corner of "only write when carried": if the check were
        // `carbon > 0` rather than `HasValue`, a genuine re-assessment to zero would
        // silently keep the old, higher figure — and the improvement would never
        // show up in the rollup.
        using var db = NewDb(out var tenantId, out var projectId);
        var controller = NewController(db, tenantId);

        await controller.SyncElements(Push(projectId, Element(carbon: 1842.5)));
        await controller.SyncElements(Push(projectId, Element(carbon: 0.0)));

        var saved = await db.TaggedElements.FirstAsync(e => e.UniqueId == Uid);
        Assert.Equal(0.0, saved.EmbodiedCarbonKg!.Value, 6);
    }

    [Fact]
    public async Task Whitespace_is_not_a_value()
    {
        // A parameter box someone tabbed through is empty, not an EPD reference.
        using var db = NewDb(out var tenantId, out var projectId);
        var controller = NewController(db, tenantId);

        await controller.SyncElements(Push(projectId, Element(epd: "EPD-NOR-2024-0912")));
        await controller.SyncElements(Push(projectId, Element(epd: "   ")));

        var saved = await db.TaggedElements.FirstAsync(e => e.UniqueId == Uid);
        Assert.Equal("EPD-NOR-2024-0912", saved.EpdRef);
    }

    // ── SignalR stubs ─────────────────────────────────────────────────────────
    // Nested per file, matching TagSyncConflictTests and TagSyncSoftDeleteTests.
    // Hoisting them into shared test infrastructure is a worthwhile tidy-up and a
    // separate change from adding a feature.

    private sealed class NullHubContext<T> : IHubContext<T> where T : Hub
    {
        public IHubClients Clients { get; } = new NullHubClients();
        public IGroupManager Groups { get; } = new NullGroupManager();
    }

    private sealed class NullHubClients : IHubClients
    {
        private static readonly IClientProxy Proxy = new NullClientProxy();
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }

    private sealed class NullClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NullGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
