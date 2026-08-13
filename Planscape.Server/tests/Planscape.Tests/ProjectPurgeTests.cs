using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Tests;

/// <summary>
/// Hard delete is the only irreversible operation in the platform, so what is
/// tested here is the GUARDS, not the happy path. Each test corresponds to one
/// gate that, if it silently regressed, would let a project be destroyed by
/// someone or something that should not be able to destroy it.
///
/// Everything is asserted THROUGH THE API rather than by reading the DbContext
/// back. That is not stylistic: a scope resolved from _factory.Services does not
/// observe writes made inside a request — schedule returns 200 and the row is
/// genuinely updated, yet a fresh test-side context still reads the old value,
/// so a direct read-back assertion fails while the feature works. It is also the
/// better assertion, because "scheduled" only matters insofar as it changes what
/// a caller can see.
///
/// The purge job itself is deliberately not exercised end-to-end here: it
/// depends on the FK cascade across ~170 tables, which the in-memory provider
/// does not model faithfully. A passing in-memory "it deleted" would be
/// actively misleading about real cascade behaviour; that belongs in an
/// integration run against Postgres.
/// </summary>
public class ProjectPurgeTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;

    public ProjectPurgeTests(PlanscapeWebApplicationFactory factory) => _factory = factory;

    /// <summary>Seeds an archived project in the default test tenant.</summary>
    private Guid SeedProject(string code, ProjectStatus status = ProjectStatus.Archived)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        var project = new Project
        {
            TenantId = TestData.TenantId,
            Name = $"Purge test {code}",
            Code = code,
            Status = status,
        };
        db.Projects.Add(project);
        db.SaveChanges();
        return project.Id;
    }

    /// <summary>Is the project still visible via the API? See the class remarks.</summary>
    private static async Task<bool> IsListedAsync(HttpClient client, string code)
        => (await client.GetStringAsync("/api/projects")).Contains(code);

    [Fact]
    public async Task Purge_RequiresOwnerRole()
    {
        var id = SeedProject("PURGE-ROLE-01");
        // member@test.org is a Contributor, not the tenant Owner.
        var client = await _factory.CreateAuthenticatedClientAsync("member@test.org", "Password123!");

        var resp = await client.PostAsync($"/api/projects/{id}/purge?confirmCode=PURGE-ROLE-01", null);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var owner = await _factory.CreateAuthenticatedClientAsync();
        Assert.True(await IsListedAsync(owner, "PURGE-ROLE-01"), "a refused purge must change nothing");
    }

    [Fact]
    public async Task Purge_RefusesWhenProjectIsNotArchived()
    {
        // The "always a reversible step first" gate.
        var id = SeedProject("PURGE-ACTIVE-02", ProjectStatus.Active);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var resp = await client.PostAsync($"/api/projects/{id}/purge?confirmCode=PURGE-ACTIVE-02", null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.True(await IsListedAsync(client, "PURGE-ACTIVE-02"));
    }

    [Fact]
    public async Task Purge_RefusesWithoutMatchingConfirmCode()
    {
        var id = SeedProject("PURGE-CODE-03");
        var client = await _factory.CreateAuthenticatedClientAsync();

        var resp = await client.PostAsync($"/api/projects/{id}/purge?confirmCode=WRONG-CODE", null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.True(await IsListedAsync(client, "PURGE-CODE-03"));
    }

    [Fact]
    public async Task Purge_RefusesWithNoConfirmCodeAtAll()
    {
        var id = SeedProject("PURGE-NOCODE-04");
        var client = await _factory.CreateAuthenticatedClientAsync();

        var resp = await client.PostAsync($"/api/projects/{id}/purge", null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.True(await IsListedAsync(client, "PURGE-NOCODE-04"));
    }

    [Fact]
    public async Task Purge_SchedulesRatherThanDeletingImmediately()
    {
        // THE core safety property: a successful call must not destroy anything.
        var id = SeedProject("PURGE-OK-05");
        var client = await _factory.CreateAuthenticatedClientAsync();

        var resp = await client.PostAsync($"/api/projects/{id}/purge?confirmCode=PURGE-OK-05", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // The response says when destruction happens — it must be a real window
        // in the future, not "now".
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var purgeAfter = body.GetProperty("purgeAfter").GetDateTime();
        Assert.True(purgeAfter > DateTime.UtcNow.AddDays(1),
            $"purgeAfter must be a real grace window, got {purgeAfter:u}");

        // And it is still fully recoverable, which is only true if the schedule
        // destroyed nothing.
        var cancel = await client.DeleteAsync($"/api/projects/{id}/purge");
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.True(await IsListedAsync(client, "PURGE-OK-05"));
    }

    [Fact]
    public async Task ScheduledProject_DisappearsFromTheProjectList()
    {
        var id = SeedProject("PURGE-HIDE-06");
        var client = await _factory.CreateAuthenticatedClientAsync();
        Assert.True(await IsListedAsync(client, "PURGE-HIDE-06"));

        var resp = await client.PostAsync($"/api/projects/{id}/purge?confirmCode=PURGE-HIDE-06", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        Assert.False(await IsListedAsync(client, "PURGE-HIDE-06"));
    }

    [Fact]
    public async Task CancelPurge_RestoresTheProject()
    {
        // The grace window is worthless if cancelling doesn't actually work.
        var id = SeedProject("PURGE-CANCEL-07");
        var client = await _factory.CreateAuthenticatedClientAsync();
        var sched = await client.PostAsync($"/api/projects/{id}/purge?confirmCode=PURGE-CANCEL-07", null);
        Assert.Equal(HttpStatusCode.OK, sched.StatusCode);
        Assert.False(await IsListedAsync(client, "PURGE-CANCEL-07"), "should be hidden once scheduled");

        var resp = await client.DeleteAsync($"/api/projects/{id}/purge");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(await IsListedAsync(client, "PURGE-CANCEL-07"), "cancelling must bring it back");
    }

    [Fact]
    public async Task CancelPurge_RequiresOwnerRole()
    {
        var id = SeedProject("PURGE-CANCELROLE-08");
        var owner = await _factory.CreateAuthenticatedClientAsync();
        var sched = await owner.PostAsync($"/api/projects/{id}/purge?confirmCode=PURGE-CANCELROLE-08", null);
        Assert.Equal(HttpStatusCode.OK, sched.StatusCode);

        var member = await _factory.CreateAuthenticatedClientAsync("member@test.org", "Password123!");
        var resp = await member.DeleteAsync($"/api/projects/{id}/purge");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        // A refused cancel must not quietly un-schedule it.
        Assert.False(await IsListedAsync(owner, "PURGE-CANCELROLE-08"));
    }

    [Fact]
    public async Task Purge_CannotReachAnotherTenantsProject()
    {
        var id = SeedProject("PURGE-TENANT-09");
        // Owner of a DIFFERENT tenant — the role check alone would pass, so this
        // proves tenant scoping is what stops it.
        var other = await _factory.CreateAuthenticatedClientAsync("admin@other.org", "Password123!");

        var resp = await other.PostAsync($"/api/projects/{id}/purge?confirmCode=PURGE-TENANT-09", null);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var owner = await _factory.CreateAuthenticatedClientAsync();
        Assert.True(await IsListedAsync(owner, "PURGE-TENANT-09"));
    }
}
