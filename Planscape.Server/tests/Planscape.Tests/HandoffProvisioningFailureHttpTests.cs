using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Planscape.Tests;

/// <summary>
/// DEP-7 — the HTTP-level provisioning-failure test that Phase 212 had to drop.
///
/// The guarantee: a handoff whose starter-project provisioning FAILS must still
/// issue a session (200), never deny login for a *provisioning* reason.
/// `HandoffProvisioningSqliteTests` proves the recovery by calling
/// `EnsureStarterProjectAsync` directly; this proves it end-to-end through
/// `POST /api/auth/handoff/exchange`, where the unguarded SaveChangesAsync that
/// persists the refresh token actually runs.
///
/// RESTORED BY THE DEP-7 FIX. The blocker was never the failure injection — it was
/// that a SECOND WebApplicationFactory could not be stood up: the ~31 static
/// `RecurringJob.AddOrUpdate` registrations read the process-global
/// `JobStorage.Current`, disposed per host, so an extra factory's host raced a
/// disposed storage. Phase 227 register those through the injected
/// `IRecurringJobManager` (this host's own storage), so no host reads the global
/// during build and a second factory is safe. This class is also pinned to a
/// non-parallel collection — the "serialized collection hosting the extra factory"
/// option from the DEP-7 plan.
///
/// WHY IN-MEMORY + AN INTERCEPTOR, NOT SQLITE. A WebApplicationFactory cannot boot
/// against SQLite (Program.cs's schema block issues Postgres-only
/// `information_schema` queries with no try/catch), so the failure is injected the
/// only way that survives an HTTP boot: a SaveChanges interceptor that throws the
/// same `DbUpdateException` the real (TenantId, Code) unique-index race would.
/// </summary>
[Collection(HandoffFailureCollection.Name)]
public class HandoffProvisioningFailureHttpTests
    : IClassFixture<HandoffProvisioningFailureFactory>
{
    private readonly HandoffProvisioningFailureFactory _factory;
    private const string Secret = "test-handoff-secret-not-a-real-one-0123456789";

    public HandoffProvisioningFailureHttpTests(HandoffProvisioningFailureFactory factory)
    {
        _factory = factory;
        Environment.SetEnvironmentVariable("PLANSCAPE_HANDOFF_SECRET", Secret);
    }

    private static string B64Url(byte[] b) =>
        Convert.ToBase64String(b).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string MintTicket(string email, string tenantSlug)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = JsonSerializer.Serialize(new
        {
            jti = Guid.NewGuid().ToString(),
            email,
            tenantSlug,
            tenantName = "Provisioning-Failure Org",
            role = "owner",
            iat = now,
            exp = now + 120
        });
        var bytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        return $"{B64Url(bytes)}.{B64Url(hmac.ComputeHash(bytes))}";
    }

    [Fact]
    public async Task Handoff_WhenStarterProjectProvisioningFails_StillIssuesASession()
    {
        var email = $"provfail-{Guid.NewGuid():N}@example.com";
        var slug  = $"provfail-{Guid.NewGuid():N}"[..20];

        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/handoff/exchange",
            new { ticket = MintTicket(email, slug) });

        // The whole point: provisioning threw, but the session is still issued.
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var session = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(session.GetProperty("accessToken").GetString()));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;

        // The user landed…
        var user = db.Users.Single(u => u.Email == email);
        // …but the interceptor blocked the starter project, and the recovery path
        // detached it rather than re-throwing into the refresh-token save.
        Assert.Empty(db.Projects.IgnoreQueryFilters().Where(p => p.TenantId == user.TenantId));
        Assert.Empty(db.ProjectMembers.IgnoreQueryFilters().Where(m => m.UserId == user.Id));
    }
}

/// <summary>Serialises this class against the rest of the suite (DEP-7 option).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class HandoffFailureCollection
{
    public const string Name = "Handoff provisioning-failure (serial)";
}

/// <summary>
/// A factory whose DbContext refuses to insert a Project — reproducing the
/// (TenantId, Code) unique-index violation the starter-project race hits, but
/// deterministically and without a relational provider.
/// </summary>
public class HandoffProvisioningFailureFactory : PlanscapeWebApplicationFactory
{
    private readonly string _failDbName = $"HandoffFail_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Runs AFTER the base ConfigureServices, so this replacement wins. A fresh
        // empty store is fine: the handoff provisions its own tenant + user.
        builder.ConfigureTestServices(services =>
        {
            foreach (var d in services
                         .Where(d => d.ServiceType == typeof(DbContextOptions<PlanscapeDbContext>)
                                  || d.ServiceType == typeof(PlanscapeDbContext))
                         .ToList())
                services.Remove(d);

            services.AddDbContext<PlanscapeDbContext>(o => o
                .UseInMemoryDatabase(_failDbName)
                .AddInterceptors(new FailProjectInsertInterceptor())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        });
    }
}

/// <summary>Throws the moment a Project is about to be inserted.</summary>
public sealed class FailProjectInsertInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Throw(eventData);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Throw(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Throw(DbContextEventData eventData)
    {
        var addingProject = eventData.Context?.ChangeTracker.Entries()
            .Any(e => e.State == EntityState.Added && e.Entity is Project) == true;
        if (addingProject)
            throw new DbUpdateException(
                "simulated (TenantId, Code) unique-index violation on the starter project");
    }
}
