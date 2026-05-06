using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Tests;

/// <summary>
/// Phase 175 audit P2-19 — integration coverage for the new project
/// visibility model and the <c>ProjectAccessAttribute</c> deep-link
/// gate. Without these tests the largest authz change in the codebase
/// shipped untested.
///
/// Visibility model: a project is visible to (a) tenant Admin / Owner /
/// SecurityOfficer, (b) the project author (Project.CreatedById), or
/// (c) anyone with an active ProjectMember row.
/// </summary>
public class ProjectVisibilityTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;

    public ProjectVisibilityTests(PlanscapeWebApplicationFactory factory) => _factory = factory;

    // ── /api/projects list filtering ──────────────────────────────────────

    [Fact]
    public async Task ListProjects_AsTenantAdmin_SeesAllTenantProjects()
    {
        // Seed an extra project with no members and a different author —
        // an admin should still see it via the admin short-circuit.
        var (privateProject, _) = SeedPrivateProject("VIS-ADMIN-001");
        var client = await _factory.CreateAuthenticatedClientAsync();

        var items = await GetProjectListAsync(client);
        var codes = ProjectCodes(items);

        Assert.Contains("VIS-ADMIN-001", codes);
        // The default seeded project is also visible
        Assert.Contains("TST-001", codes);
    }

    [Fact]
    public async Task ListProjects_AsNonMemberNonAuthor_DoesNotSeeProject()
    {
        // Seed a project authored by AdminUser with no members. The
        // "member" seed user is a Member role with no ProjectMember row,
        // so they must not see this project.
        SeedPrivateProject("VIS-PRIVATE-002");
        var client = await _factory.CreateAuthenticatedClientAsync(
            "member@test.org", "Password123!");

        var items = await GetProjectListAsync(client);
        var codes = ProjectCodes(items);

        Assert.DoesNotContain("VIS-PRIVATE-002", codes);
    }

    [Fact]
    public async Task ListProjects_AsAuthor_SeesOwnProjectEvenWithoutMembership()
    {
        // The Member-role user authors a project. They should see it even
        // though no ProjectMember row exists for them on it.
        SeedProjectAuthoredBy("VIS-AUTHOR-003", TestData.MemberUserId);
        var client = await _factory.CreateAuthenticatedClientAsync(
            "member@test.org", "Password123!");

        var items = await GetProjectListAsync(client);
        var codes = ProjectCodes(items);

        Assert.Contains("VIS-AUTHOR-003", codes);
    }

    [Fact]
    public async Task ListProjects_AsActiveMember_SeesJoinedProject()
    {
        var (project, _) = SeedPrivateProject("VIS-MEMBER-004");
        SeedMembership(project.Id, TestData.MemberUserId);
        var client = await _factory.CreateAuthenticatedClientAsync(
            "member@test.org", "Password123!");

        var items = await GetProjectListAsync(client);
        var codes = ProjectCodes(items);

        Assert.Contains("VIS-MEMBER-004", codes);
    }

    [Fact]
    public async Task ListProjects_AsCrossTenantUser_NeverSeesAnotherTenantsProject()
    {
        SeedPrivateProject("VIS-CROSSTENANT-005");
        var client = await _factory.CreateAuthenticatedClientAsync(
            "admin@other.org", "Password123!");

        var items = await GetProjectListAsync(client);
        var codes = ProjectCodes(items);

        Assert.DoesNotContain("VIS-CROSSTENANT-005", codes);
        Assert.DoesNotContain("TST-001", codes);
    }

    // ── ProjectAccessAttribute deep-link gating ───────────────────────────

    [Fact]
    public async Task DeepLink_NonMember_GetsNotFound_NotForbidden()
    {
        var (project, _) = SeedPrivateProject("VIS-GATE-006");
        var client = await _factory.CreateAuthenticatedClientAsync(
            "member@test.org", "Password123!");

        // Issues controller has [ProjectAccess] on the {projectId} route
        var resp = await client.GetAsync($"/api/projects/{project.Id}/issues");

        // 404 not 403 — existence shouldn't leak via status code.
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task DeepLink_Author_PassesGate()
    {
        var project = SeedProjectAuthoredBy("VIS-GATE-007", TestData.MemberUserId);
        var client = await _factory.CreateAuthenticatedClientAsync(
            "member@test.org", "Password123!");

        var resp = await client.GetAsync($"/api/projects/{project.Id}/issues");
        Assert.NotEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task DeepLink_TenantAdmin_PassesGateOnAnyProject()
    {
        var (project, _) = SeedPrivateProject("VIS-GATE-008");
        var client = await _factory.CreateAuthenticatedClientAsync();

        var resp = await client.GetAsync($"/api/projects/{project.Id}/issues");
        Assert.NotEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static async Task<JsonElement> GetProjectListAsync(HttpClient client)
    {
        var resp = await client.GetAsync("/api/projects");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await resp.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static IReadOnlyList<string> ProjectCodes(JsonElement items)
    {
        var list = new List<string>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String)
                list.Add(c.GetString() ?? "");
        }
        return list;
    }

    private (Project project, Guid authorId) SeedPrivateProject(string code)
        => (SeedProjectAuthoredBy(code, TestData.AdminUserId), TestData.AdminUserId);

    private Project SeedProjectAuthoredBy(string code, Guid authorId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;
        var existing = db.Projects.FirstOrDefault(p => p.Code == code);
        if (existing != null) return existing;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            TenantId = TestData.TenantId,
            Name = "Visibility " + code,
            Code = code,
            Phase = "Stage 4",
            Status = ProjectStatus.Active,
            CreatedById = authorId,
        };
        db.Projects.Add(project);
        db.SaveChanges();
        return project;
    }

    private void SeedMembership(Guid projectId, Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;
        if (db.ProjectMembers.Any(m => m.ProjectId == projectId && m.UserId == userId))
            return;
        db.ProjectMembers.Add(new ProjectMember
        {
            TenantId = TestData.TenantId,
            ProjectId = projectId,
            UserId = userId,
            ProjectRole = "Contributor",
            Iso19650Role = "M",
            IsActive = true,
        });
        db.SaveChanges();
    }
}
