using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

namespace Planscape.Tests;

/// <summary>
/// TRACK A2 — <see cref="FederatedModelHub.JoinProject"/> had no membership
/// check at all.
///
/// THE DEFECT
/// ----------
/// The hub carried <c>[Authorize]</c>, which proves only that the caller is
/// signed in. JoinProject took a project id off the wire and added the caller
/// straight into <c>model:{projectId}</c> — the group name was the ONLY key.
/// Any authenticated user could therefore subscribe to any project's
/// ModelUpdated stream (the element ids of everything being edited), including
/// projects in other tenants, because a group name carries no tenant scope.
/// <see cref="NotificationHub.JoinProject"/> has validated membership since
/// NEW-LOGIC-15; this sibling hub was missed.
///
/// WHY THE TENANT CHECK IS ASSERTED SEPARATELY
/// -------------------------------------------
/// Everywhere else the ambient global query filter supplies tenant scope, but it
/// reads ITenantContext.TenantId off IHttpContextAccessor, which is not reliably
/// populated inside a SignalR hub method. If the fix had leaned on the ambient
/// filter it would have failed CLOSED — denying every legitimate member — so
/// <see cref="Outsider_from_another_tenant_is_rejected"/> and
/// <see cref="Member_is_admitted"/> are both required: one proves the gate
/// denies, the other proves it does not deny everyone.
/// </summary>
public class FederatedModelHubAuthorizationTests
{
    private sealed class FixedTenant : ITenantContext
    {
        public FixedTenant(Guid id) => TenantId = id;
        public Guid TenantId { get; }
        public string TenantSlug => "t";
        public LicenseTier Tier => LicenseTier.Professional;
        public bool MimEnabled => false;
    }

    private static PlanscapeDbContext NewContext(SqliteConnection conn, Guid tenantId)
        => new(new DbContextOptionsBuilder<PlanscapeDbContext>().UseSqlite(conn).Options,
               httpContextAccessor: null!, tenantContext: new FixedTenant(tenantId));

    private sealed record World(
        SqliteConnection Conn,
        Guid Tenant, Guid OtherTenant,
        Guid VictimProject,
        Guid Insider, Guid Neighbour, Guid Outsider);

    private static World NewWorld()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var victim = Guid.NewGuid();
        var sibling = Guid.NewGuid();
        var insider = Guid.NewGuid();
        var neighbour = Guid.NewGuid();
        var outsider = Guid.NewGuid();

        using (var ctx = NewContext(conn, tenant))
        {
            ctx.Database.EnsureCreated();

            foreach (var (id, slug) in new[] { (tenant, "acme"), (otherTenant, "rival") })
                ctx.Tenants.Add(new Tenant
                {
                    Id = id, Name = slug, Slug = $"{slug}-{Guid.NewGuid():N}"[..14],
                    ContactEmail = $"{slug}@example.com", Tier = LicenseTier.Professional,
                    Plan = BillingPlan.Studio, MaxUsers = 50, MaxProjects = 50,
                });

            ctx.Projects.Add(new Project
            {
                Id = victim, TenantId = tenant, Name = "Victim Tower",
                Code = $"VT-{Guid.NewGuid():N}"[..8], Status = ProjectStatus.Active,
            });
            ctx.Projects.Add(new Project
            {
                Id = sibling, TenantId = tenant, Name = "Sibling Tower",
                Code = $"ST-{Guid.NewGuid():N}"[..8], Status = ProjectStatus.Active,
            });

            // ProjectMember FKs to AppUser; SQLite enforces it.
            foreach (var (id, tid, name) in new[]
                     {
                         (insider, tenant, "Insider"),
                         (neighbour, tenant, "Neighbour"),
                         (outsider, otherTenant, "Outsider"),
                     })
                ctx.Users.Add(new AppUser
                {
                    Id = id, TenantId = tid, Email = $"{name}-{Guid.NewGuid():N}@example.com",
                    DisplayName = name, PasswordHash = "x", IsActive = true,
                });

            ctx.ProjectMembers.Add(new ProjectMember
            {
                TenantId = tenant, ProjectId = victim, UserId = insider,
                ProjectRole = "Contributor", Iso19650Role = "M", IsActive = true,
            });
            // Same tenant, real membership — on the OTHER project.
            ctx.ProjectMembers.Add(new ProjectMember
            {
                TenantId = tenant, ProjectId = sibling, UserId = neighbour,
                ProjectRole = "Contributor", Iso19650Role = "M", IsActive = true,
            });

            ctx.SaveChanges();
        }

        return new World(conn, tenant, otherTenant, victim, insider, neighbour, outsider);
    }

    /// <summary>Records group joins so a silent no-op cannot pass for a denial.</summary>
    private sealed class SpyGroups : IGroupManager
    {
        public List<string> Joined { get; } = new();
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
        {
            Joined.Add(groupName);
            return Task.CompletedTask;
        }
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeCallerContext : HubCallerContext
    {
        private readonly ClaimsPrincipal? _user;
        public FakeCallerContext(ClaimsPrincipal? user) => _user = user;

        public override string ConnectionId => "conn-1";
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => _user;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private static (FederatedModelHub hub, SpyGroups groups) NewHub(World w, Guid userId, Guid tenantId)
    {
        var services = new ServiceCollection();
        // Scoped, so the hub's CreateScope() hands back a fresh context each call.
        services.AddScoped(_ => NewContext(w.Conn, tenantId));

        var groups = new SpyGroups();
        var hub = new FederatedModelHub(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>())
        {
            Groups = groups,
            Context = new FakeCallerContext(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim("user_id", userId.ToString()),
                new Claim("tenant_id", tenantId.ToString()),
                new Claim("role", "Contributor"),   // deliberately NOT Admin/Owner
            }, "test"))),
        };
        return (hub, groups);
    }

    [Fact]
    public async Task Same_tenant_non_member_is_rejected()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            var (hub, groups) = NewHub(w, w.Neighbour, w.Tenant);

            await Assert.ThrowsAsync<HubException>(
                () => hub.JoinProject(w.VictimProject.ToString()));

            Assert.Empty(groups.Joined);
        }
    }

    [Fact]
    public async Task Outsider_from_another_tenant_is_rejected()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            var (hub, groups) = NewHub(w, w.Outsider, w.OtherTenant);

            await Assert.ThrowsAsync<HubException>(
                () => hub.JoinProject(w.VictimProject.ToString()));

            Assert.Empty(groups.Joined);
        }
    }

    [Fact]
    public async Task Member_is_admitted()
    {
        // The load-bearing mirror case: the gate must not deny everyone. See the
        // class remarks on why that is a live risk for a hub specifically.
        var w = NewWorld();
        using (w.Conn)
        {
            var (hub, groups) = NewHub(w, w.Insider, w.Tenant);

            await hub.JoinProject(w.VictimProject.ToString());

            Assert.Equal(new[] { $"model:{w.VictimProject}" }, groups.Joined);
        }
    }

    [Fact]
    public async Task A_malformed_project_id_is_rejected_not_joined()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            var (hub, groups) = NewHub(w, w.Insider, w.Tenant);

            await Assert.ThrowsAsync<HubException>(() => hub.JoinProject("not-a-guid"));
            await Assert.ThrowsAsync<HubException>(() => hub.JoinProject(Guid.Empty.ToString()));

            Assert.Empty(groups.Joined);
        }
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_rejected()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            var services = new ServiceCollection();
            services.AddScoped(_ => NewContext(w.Conn, w.Tenant));
            var groups = new SpyGroups();
            var hub = new FederatedModelHub(
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>())
            {
                Groups = groups,
                Context = new FakeCallerContext(user: null),
            };

            await Assert.ThrowsAsync<HubException>(
                () => hub.JoinProject(w.VictimProject.ToString()));

            Assert.Empty(groups.Joined);
        }
    }
}
