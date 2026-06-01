using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planscape.API.Controllers;
using Planscape.Core.DTOs;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

namespace Planscape.Tests;

/// <summary>
/// S02 — TagSync conflict resolution (STALE_UPDATE / SERVER_WINS).
/// </summary>
public class TagSyncConflictTests
{
    [Fact]
    public async Task SyncElements_StaleUpdate_KeepsServerData_AndRecordsConflict()
    {
        // ── Arrange ─────────────────────────────────────────────────────────
        var tenantId  = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<PlanscapeDbContext>()
            .UseInMemoryDatabase($"TagSyncConflict_{Guid.NewGuid():N}")
            .Options;

        await using var db = new PlanscapeDbContext(options);

        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Name = "Test Org", Slug = "test-org",
            ContactEmail = "admin@test.org", Tier = LicenseTier.Premium,
            MaxUsers = 10, MaxProjects = 5, IsActive = true
        });
        db.Projects.Add(new Project
        {
            Id = projectId, TenantId = tenantId,
            Name = "Test Project", Code = "TST-001",
            Status = ProjectStatus.Active
        });

        // Server-side element — LastModified 2026-01-01, Version 1, canonical values
        var serverLastModified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var existing = new TaggedElement
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            RevitElementId = 424242,
            UniqueId = "unique-stable-id",
            Disc = "M", Loc = "BLD1", Zone = "Z01", Lvl = "L02",
            Sys = "HVAC", Func = "SUP", Prod = "AHU", Seq = "0001",
            Tag1 = "M-BLD1-Z01-L02-HVAC-SUP-AHU-0001",
            CategoryName = "Mechanical Equipment",
            FamilyName = "AHU_Standard",
            IsComplete = true,
            IsFullyResolved = true,
            LastModifiedUtc = serverLastModified,
            Version = 1
        };
        db.TaggedElements.Add(existing);
        await db.SaveChangesAsync();

        // The controller gained an IServiceScopeFactory dependency (used only by
        // the post-sync cross-host mapping path, which this stale-rejection case
        // never reaches — 0 mappings). Pass a real, empty scope factory so it's
        // non-null and CreateScope() is safe, mirroring the other tests' pattern.
        var scopeFactory = new ServiceCollection()
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        var controller = new TagSyncController(
            db,
            new NullHubContext<TagSyncHub>(),
            new NullHubContext<ComplianceHub>(),
            scopeFactory);

        // Build a ClaimsPrincipal carrying the tenant so GetTenantId() resolves
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("tenant_id", tenantId.ToString())
                }, "TestAuth"))
            }
        };

        // Client tries to overwrite with older timestamp (Dec 1 2025 vs Jan 1 2026)
        var clientTimestamp = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        var request = new TagSyncRequest
        {
            ProjectId = projectId,
            UserName = "StaleClient",
            RevitVersion = "2025",
            PluginVersion = "1.0.0",
            Elements = new List<TagElementDto>
            {
                new()
                {
                    RevitElementId = 424242,
                    UniqueId = "unique-stable-id",
                    Disc = "STALE",                // tampered — must NOT stick
                    Loc = "STALE", Zone = "STALE", Lvl = "STALE",
                    Sys = "STALE", Func = "STALE", Prod = "STALE", Seq = "9999",
                    Tag1 = "STALE-TAG-SHOULD-NOT-OVERWRITE",
                    CategoryName = "Mechanical Equipment",
                    FamilyName = "AHU_Standard",
                    IsComplete = false,
                    IsFullyResolved = false,
                    LastModifiedUtc = clientTimestamp
                }
            }
        };

        // ── Act ─────────────────────────────────────────────────────────────
        var result = await controller.SyncElements(request);

        // ── Assert ──────────────────────────────────────────────────────────
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<TagSyncResponse>(ok.Value);

        // 1. Response includes the conflict
        Assert.Single(response.Conflicts);
        var conflict = response.Conflicts[0];
        Assert.Equal("424242", conflict.ElementId);
        Assert.Equal(serverLastModified, conflict.ServerTimestamp);
        Assert.Equal(clientTimestamp, conflict.ClientTimestamp);
        Assert.Equal("SERVER_WINS", conflict.Resolution);

        // 2. Stale update did NOT count as an update
        Assert.Equal(0, response.Updated);
        Assert.Equal(0, response.Created);

        // 3. Server data is unchanged — original canonical tag preserved
        var reloaded = await db.TaggedElements.AsNoTracking()
            .FirstAsync(e => e.RevitElementId == 424242);
        Assert.Equal("M", reloaded.Disc);
        Assert.Equal("M-BLD1-Z01-L02-HVAC-SUP-AHU-0001", reloaded.Tag1);
        Assert.Equal(1, reloaded.Version);                  // Version NOT bumped
        Assert.Equal(serverLastModified, reloaded.LastModifiedUtc);

        // 4. A persisted SyncConflict row exists
        var persisted = await db.SyncConflicts.AsNoTracking()
            .SingleAsync(c => c.ElementId == "424242");
        Assert.Equal("STALE_UPDATE", persisted.ConflictType);
        Assert.Equal("SERVER_WINS", persisted.Resolution);
        Assert.Equal(serverLastModified, persisted.ServerTimestamp);
        Assert.Equal(clientTimestamp, persisted.ClientTimestamp);
        Assert.Equal("StaleClient", persisted.ClientUserName);
        Assert.Equal(projectId, persisted.ProjectId);
    }

    // ── Minimal SignalR hub-context stubs (the controller fires broadcasts
    //    inside a try/catch so these no-ops are sufficient for unit testing).
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
