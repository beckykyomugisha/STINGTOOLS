using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace StingBIM.Tests;

public class ProjectsControllerTests : IClassFixture<StingBimWebApplicationFactory>
{
    private readonly StingBimWebApplicationFactory _factory;

    public ProjectsControllerTests(StingBimWebApplicationFactory factory) => _factory = factory;

    // ── GET /api/projects ─────────────────────────────────────────────────

    [Fact]
    public async Task GetProjects_Authenticated_ReturnsProjectList()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/projects");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(items.GetArrayLength() >= 1);

        var first = items[0];
        Assert.Equal("TST-001", first.GetProperty("code").GetString());
        Assert.Equal("Test BIM Project", first.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetProjects_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/projects");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProjects_OtherTenant_ReturnsEmpty()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(
            "admin@other.org", "Password123!");
        var response = await client.GetAsync("/api/projects");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, items.GetArrayLength());
    }

    // ── POST /api/projects ────────────────────────────────────────────────

    [Fact]
    public async Task CreateProject_Valid_ReturnsCreated()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "New BIM Project",
            code = "NEW-001",
            description = "A test project",
            phase = "Stage 3"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NEW-001", json.GetProperty("code").GetString());
        Assert.Equal("New BIM Project", json.GetProperty("name").GetString());
    }

    [Fact]
    public async Task CreateProject_DuplicateCode_Returns409()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        // TST-001 already exists from seed data
        var response = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "Duplicate Project",
            code = "TST-001"
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── GET /api/projects/{id}/dashboard ──────────────────────────────────

    [Fact]
    public async Task GetDashboard_ValidProject_ReturnsDashboardData()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"/api/projects/{TestData.ProjectId}/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Test BIM Project", json.GetProperty("name").GetString());
        Assert.Equal("TST-001", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetDashboard_OtherTenantProject_Returns404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(
            "admin@other.org", "Password123!");
        var response = await client.GetAsync($"/api/projects/{TestData.ProjectId}/dashboard");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_NonexistentProject_Returns404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"/api/projects/{Guid.NewGuid()}/dashboard");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
