using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
/// TagSync element tombstones — POST /api/tagsync/sync with <c>isDeleted</c>.
///
/// Before this channel existed the endpoint was insert-or-update only, so an
/// element deleted in Revit left its TaggedElement row on the server forever,
/// inflating element counts and skewing compliance. These tests pin the
/// tombstone behaviour AND the read-path exclusion that makes it meaningful.
///
/// Structure mirrors <see cref="TagSyncConflictTests"/>: the controller is
/// constructed directly against an in-memory PlanscapeDbContext with stubbed
/// SignalR hubs and an injected tenant_id claim.
/// </summary>
public class TagSyncSoftDeleteTests
{
    private const long ElementId = 424242;

    // ── Test rig ────────────────────────────────────────────────────────────

    private static PlanscapeDbContext NewDb(string name)
    {
        var options = new DbContextOptionsBuilder<PlanscapeDbContext>()
            .UseInMemoryDatabase($"{name}_{Guid.NewGuid():N}")
            // SyncElements wraps its batches in an explicit RepeatableRead
            // transaction the InMemory provider cannot honour; EF escalates
            // TransactionIgnoredWarning to an exception without this. Production
            // is PostgreSQL, where the transaction is real.
            .ConfigureWarnings(w =>
                w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId
                    .TransactionIgnoredWarning))
            .Options;

        var db = new PlanscapeDbContext(options);
        // Built directly rather than through DI, so _tenantContext is null and
        // CurrentTenantId is Guid.Empty — the global tenant filter would hide
        // the rows seeded below. BypassTenantFilter is the documented escape
        // hatch. Note it deliberately does NOT relax the soft-delete predicate,
        // which several assertions below rely on.
        db.BypassTenantFilter = true;
        return db;
    }

    private static async Task SeedAsync(PlanscapeDbContext db, Guid tenantId, Guid projectId,
        params TaggedElement[] elements)
    {
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Name = "Test Org", Slug = $"test-{Guid.NewGuid():N}",
            ContactEmail = "admin@test.org", Tier = LicenseTier.Premium,
            MaxUsers = 10, MaxProjects = 5, IsActive = true
        });
        db.Projects.Add(new Project
        {
            Id = projectId, TenantId = tenantId,
            Name = "Test Project", Code = $"TST-{Guid.NewGuid():N}"[..10],
            Status = ProjectStatus.Active
        });
        foreach (var e in elements)
        {
            e.TenantId = tenantId;
            e.ProjectId = projectId;
            db.TaggedElements.Add(e);
        }
        await db.SaveChangesAsync();
    }

    private static TaggedElement LiveElement(long revitId, string tag1, DateTime lastModified) => new()
    {
        Id = Guid.NewGuid(),
        RevitElementId = revitId,
        UniqueId = $"unique-{revitId}",
        Disc = "M", Loc = "BLD1", Zone = "Z01", Lvl = "L02",
        Sys = "HVAC", Func = "SUP", Prod = "AHU", Seq = "0001",
        Tag1 = tag1,
        CategoryName = "Mechanical Equipment",
        FamilyName = "AHU_Standard",
        IsComplete = true, IsFullyResolved = true,
        LastModifiedUtc = lastModified,
        Version = 1
    };

    private static TagSyncController NewController(PlanscapeDbContext db, Guid tenantId)
    {
        var scopeFactory = new ServiceCollection()
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        var controller = new TagSyncController(
            db,
            new NullHubContext<TagSyncHub>(),
            new NullHubContext<ComplianceHub>(),
            scopeFactory,
            new NullHubContext<NotificationHub>());

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
        return controller;
    }

    private static TagElementDto Dto(long revitId, bool isDeleted, DateTime? lastModified,
        string tag1 = "M-BLD1-Z01-L02-HVAC-SUP-AHU-0001") => new()
    {
        RevitElementId = revitId,
        UniqueId = $"unique-{revitId}",
        Disc = "M", Loc = "BLD1", Zone = "Z01", Lvl = "L02",
        Sys = "HVAC", Func = "SUP", Prod = "AHU", Seq = "0001",
        Tag1 = tag1,
        CategoryName = "Mechanical Equipment",
        FamilyName = "AHU_Standard",
        IsComplete = true, IsFullyResolved = true,
        IsDeleted = isDeleted,
        LastModifiedUtc = lastModified
    };

    private static TagSyncResponse Sync(ActionResult<TagSyncResponse> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<TagSyncResponse>(ok.Value);
    }

    // ── 1. Delete marks the row ─────────────────────────────────────────────

    [Fact]
    public async Task Sync_DeleteFlag_TombstonesRow_AndHidesItFromNormalReads()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var serverTs = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var clientTs = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await using var db = NewDb(nameof(Sync_DeleteFlag_TombstonesRow_AndHidesItFromNormalReads));
        await SeedAsync(db, tenantId, projectId,
            LiveElement(ElementId, "M-BLD1-Z01-L02-HVAC-SUP-AHU-0001", serverTs));

        var controller = NewController(db, tenantId);

        var response = Sync(await controller.SyncElements(new TagSyncRequest
        {
            ProjectId = projectId,
            UserName = "Deleter",
            Elements = new List<TagElementDto> { Dto(ElementId, isDeleted: true, clientTs) }
        }));

        // Reported as a delete — NOT as a create or an update.
        Assert.Equal(1, response.Deleted);
        Assert.Equal(0, response.Created);
        Assert.Equal(0, response.Updated);
        Assert.Empty(response.Conflicts);

        // The row still exists (tombstone, not hard delete) and carries when.
        var tombstoned = await db.TaggedElements.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(e => e.RevitElementId == ElementId);
        Assert.NotNull(tombstoned.DeletedAtUtc);
        Assert.Equal(clientTs, tombstoned.DeletedAtUtc);
        Assert.Equal(2, tombstoned.Version);                  // Version bumped
        Assert.Equal(clientTs, tombstoned.LastModifiedUtc);
        // The delete payload must NOT overwrite the last known good tag data —
        // preserving it is the reason the row is kept at all.
        Assert.Equal("M-BLD1-Z01-L02-HVAC-SUP-AHU-0001", tombstoned.Tag1);

        // ...and it is invisible to an ordinary read. Note BypassTenantFilter
        // is on, proving the bypass relaxes tenancy only, never the tombstone.
        Assert.False(await db.TaggedElements.AnyAsync(e => e.RevitElementId == ElementId));
    }

    // ── 2. Delete of an unknown element is a no-op ──────────────────────────

    [Fact]
    public async Task Sync_DeleteOfUnknownElement_IsNoOp_NotAnInsert()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        await using var db = NewDb(nameof(Sync_DeleteOfUnknownElement_IsNoOp_NotAnInsert));
        await SeedAsync(db, tenantId, projectId); // no elements at all

        var controller = NewController(db, tenantId);

        var response = Sync(await controller.SyncElements(new TagSyncRequest
        {
            ProjectId = projectId,
            UserName = "Deleter",
            Elements = new List<TagElementDto>
            {
                Dto(999_999, isDeleted: true, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc))
            }
        }));

        // Accepted (200), but nothing happened — and crucially no row was
        // inserted, which would have resurrected the element as a dead row.
        Assert.Equal(1, response.Received);
        Assert.Equal(0, response.Created);
        Assert.Equal(0, response.Updated);
        Assert.Equal(0, response.Deleted);
        Assert.Empty(response.Conflicts);
        Assert.Empty(await db.TaggedElements.IgnoreQueryFilters().ToListAsync());
    }

    // ── 3. A STALE delete loses, and records a SERVER_WINS conflict ─────────

    [Fact]
    public async Task Sync_StaleDelete_LosesToNewerServerRow_AndRecordsServerWinsConflict()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        // Server was edited AFTER the client's delete was issued.
        var serverTs = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var staleClientTs = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await using var db = NewDb(nameof(Sync_StaleDelete_LosesToNewerServerRow_AndRecordsServerWinsConflict));
        await SeedAsync(db, tenantId, projectId,
            LiveElement(ElementId, "M-BLD1-Z01-L02-HVAC-SUP-AHU-0001", serverTs));

        var controller = NewController(db, tenantId);

        var response = Sync(await controller.SyncElements(new TagSyncRequest
        {
            ProjectId = projectId,
            UserName = "StaleClient",
            Elements = new List<TagElementDto> { Dto(ElementId, isDeleted: true, staleClientTs) }
        }));

        // The delete was REJECTED — it did not bypass the conflict path.
        Assert.Equal(0, response.Deleted);
        Assert.Equal(0, response.Updated);
        Assert.Equal(0, response.Created);

        var conflict = Assert.Single(response.Conflicts);
        Assert.Equal(ElementId.ToString(), conflict.ElementId);
        Assert.Equal("SERVER_WINS", conflict.Resolution);
        Assert.Equal(serverTs, conflict.ServerTimestamp);
        Assert.Equal(staleClientTs, conflict.ClientTimestamp);

        // A SyncConflict row was persisted, exactly as a stale update does.
        var persisted = await db.SyncConflicts.AsNoTracking()
            .SingleAsync(c => c.ElementId == ElementId.ToString());
        Assert.Equal("SERVER_WINS", persisted.Resolution);
        // Typed as a delete so an operator can tell "your delete was refused"
        // from "your edit was refused".
        Assert.Equal("STALE_DELETE", persisted.ConflictType);
        Assert.Equal(serverTs, persisted.ServerTimestamp);
        Assert.Equal(staleClientTs, persisted.ClientTimestamp);
        Assert.Equal("StaleClient", persisted.ClientUserName);

        // The element survived, untombstoned and unbumped.
        var survivor = await db.TaggedElements.AsNoTracking()
            .SingleAsync(e => e.RevitElementId == ElementId);
        Assert.Null(survivor.DeletedAtUtc);
        Assert.Equal(1, survivor.Version);
        Assert.Equal(serverTs, survivor.LastModifiedUtc);
    }

    // ── 4. Undelete restores ────────────────────────────────────────────────

    [Fact]
    public async Task Sync_LiveElementAfterDelete_UndeletesAndUpdates()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var serverTs = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var deleteTs = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var restoreTs = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        await using var db = NewDb(nameof(Sync_LiveElementAfterDelete_UndeletesAndUpdates));
        await SeedAsync(db, tenantId, projectId,
            LiveElement(ElementId, "M-BLD1-Z01-L02-HVAC-SUP-AHU-0001", serverTs));

        var controller = NewController(db, tenantId);

        // Delete it...
        var deleteResponse = Sync(await controller.SyncElements(new TagSyncRequest
        {
            ProjectId = projectId, UserName = "Deleter",
            Elements = new List<TagElementDto> { Dto(ElementId, isDeleted: true, deleteTs) }
        }));
        Assert.Equal(1, deleteResponse.Deleted);

        // ...then Revit undo restores it and the plugin re-sends it as live.
        var restoreResponse = Sync(await controller.SyncElements(new TagSyncRequest
        {
            ProjectId = projectId, UserName = "Restorer",
            Elements = new List<TagElementDto>
            {
                Dto(ElementId, isDeleted: false, restoreTs, tag1: "M-BLD1-Z01-L02-HVAC-SUP-AHU-0099")
            }
        }));

        // An undelete is an ordinary update, not a create — the row was reused,
        // so no duplicate was inserted against the unique (ProjectId, RevitElementId).
        Assert.Equal(1, restoreResponse.Updated);
        Assert.Equal(0, restoreResponse.Created);
        Assert.Equal(0, restoreResponse.Deleted);
        Assert.Empty(restoreResponse.Conflicts);

        // Visible to ordinary reads again, tombstone cleared, data updated.
        var restored = await db.TaggedElements.AsNoTracking()
            .SingleAsync(e => e.RevitElementId == ElementId);
        Assert.Null(restored.DeletedAtUtc);
        Assert.Equal("M-BLD1-Z01-L02-HVAC-SUP-AHU-0099", restored.Tag1);
        Assert.Equal(3, restored.Version); // 1 seed -> 2 delete -> 3 undelete
        Assert.Single(await db.TaggedElements.IgnoreQueryFilters().ToListAsync());
    }

    // ── 5a. Deleted elements drop out of compliance ─────────────────────────

    [Fact]
    public async Task Sync_DeletedElement_StopsCountingTowardComplianceAndTotals()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var serverTs = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var deleteTs = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await using var db = NewDb(nameof(Sync_DeletedElement_StopsCountingTowardComplianceAndTotals));
        // One TAGGED element and one UNTAGGED element => 1/2 = 50% compliant.
        await SeedAsync(db, tenantId, projectId,
            LiveElement(1001, "M-BLD1-Z01-L02-HVAC-SUP-AHU-0001", serverTs),
            LiveElement(1002, "", serverTs));

        var controller = NewController(db, tenantId);

        // Delete the UNTAGGED one. If tombstones still counted, compliance would
        // stay at 50%; if they are excluded it becomes 1/1 = 100%.
        var response = Sync(await controller.SyncElements(new TagSyncRequest
        {
            ProjectId = projectId, UserName = "Deleter",
            Elements = new List<TagElementDto> { Dto(1002, isDeleted: true, deleteTs, tag1: "") }
        }));

        Assert.Equal(1, response.Deleted);
        Assert.Equal(100d, response.CompliancePercent);

        // The denominator on the project row shrank too.
        var project = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId);
        Assert.Equal(1, project.TotalElements);
        Assert.Equal(1, project.TaggedElements);
        Assert.Equal(100d, project.CompliancePercent);

        // And the dedicated compliance endpoint agrees.
        var complianceResult = await controller.GetCompliance(projectId);
        var complianceOk = Assert.IsType<OkObjectResult>(complianceResult.Result);
        var summary = Assert.IsType<ComplianceSummaryDto>(complianceOk.Value);
        Assert.Equal(1, summary.TotalElements);
        Assert.Equal(0, summary.Untagged);
    }

    // ── 5b. Deleted elements drop out of the element pull ───────────────────

    [Fact]
    public async Task GetElements_ExcludesTombstonedElements()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var serverTs = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var deleteTs = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await using var db = NewDb(nameof(GetElements_ExcludesTombstonedElements));
        await SeedAsync(db, tenantId, projectId,
            LiveElement(1001, "M-BLD1-Z01-L02-HVAC-SUP-AHU-0001", serverTs),
            LiveElement(1002, "M-BLD1-Z01-L02-HVAC-SUP-AHU-0002", serverTs));

        var controller = NewController(db, tenantId);

        await controller.SyncElements(new TagSyncRequest
        {
            ProjectId = projectId, UserName = "Deleter",
            Elements = new List<TagElementDto> { Dto(1002, isDeleted: true, deleteTs) }
        });

        // NOTE: called without `lastSyncUtc`. The delta branch upserts the
        // per-device watermark with ExecuteSqlRawAsync, which the InMemory
        // provider cannot run. That branch only ADDS a LastModifiedUtc cutoff on
        // top of the same `_db.TaggedElements` source, and the tombstone
        // exclusion comes from the global query filter on that source — so it is
        // exercised either way. The deleted row's LastModifiedUtc (Feb) is in
        // fact NEWER than the live one's (Jan), so a delta pull would have
        // surfaced it first if the filter were not applied.
        var result = await controller.GetElements(projectId);
        var ok = Assert.IsType<OkObjectResult>(result);

        var payload = ok.Value!;
        var totalProp = payload.GetType().GetProperty("total")!;
        var elementsProp = payload.GetType().GetProperty("elements")!;
        var total = (int)totalProp.GetValue(payload)!;
        var elements = ((IEnumerable<TaggedElement>)elementsProp.GetValue(payload)!).ToList();

        Assert.Equal(1, total);
        Assert.Equal(1001, Assert.Single(elements).RevitElementId);
    }

    // ── 6. Backward compatibility: no isDeleted field at all ────────────────

    [Fact]
    public async Task Sync_WithoutIsDeletedField_BehavesExactlyAsBefore()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var serverTs = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var clientTs = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await using var db = NewDb(nameof(Sync_WithoutIsDeletedField_BehavesExactlyAsBefore));
        await SeedAsync(db, tenantId, projectId,
            LiveElement(1001, "M-BLD1-Z01-L02-HVAC-SUP-AHU-0001", serverTs));

        var controller = NewController(db, tenantId);

        // An OLD plugin build: the DTOs below never set IsDeleted, exactly as a
        // JSON payload with no `isDeleted` key would bind. One update + one insert.
        var response = Sync(await controller.SyncElements(new TagSyncRequest
        {
            ProjectId = projectId, UserName = "LegacyPlugin",
            Elements = new List<TagElementDto>
            {
                new()
                {
                    RevitElementId = 1001, UniqueId = "unique-1001",
                    Disc = "M", Loc = "BLD1", Zone = "Z01", Lvl = "L02",
                    Sys = "HVAC", Func = "SUP", Prod = "AHU", Seq = "0001",
                    Tag1 = "M-BLD1-Z01-L02-HVAC-SUP-AHU-0001-EDITED",
                    CategoryName = "Mechanical Equipment", FamilyName = "AHU_Standard",
                    IsComplete = true, IsFullyResolved = true,
                    LastModifiedUtc = clientTs
                },
                new()
                {
                    RevitElementId = 1002, UniqueId = "unique-1002",
                    Disc = "E", Loc = "BLD1", Zone = "Z01", Lvl = "L02",
                    Sys = "LV", Func = "PWR", Prod = "DB", Seq = "0002",
                    Tag1 = "E-BLD1-Z01-L02-LV-PWR-DB-0002",
                    CategoryName = "Electrical Equipment", FamilyName = "DB_Standard",
                    IsComplete = true, IsFullyResolved = true,
                    LastModifiedUtc = clientTs
                }
            }
        }));

        Assert.Equal(1, response.Updated);
        Assert.Equal(1, response.Created);
        Assert.Equal(0, response.Deleted);        // absent field => never a delete
        Assert.Empty(response.Conflicts);

        // Nothing was tombstoned, and both rows are live.
        Assert.Equal(2, await db.TaggedElements.CountAsync());
        Assert.All(await db.TaggedElements.AsNoTracking().ToListAsync(),
            e => Assert.Null(e.DeletedAtUtc));
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
