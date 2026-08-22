using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Tests;

/// <summary>
/// #631 — the Phase 177 per-folder ACL, exercised end to end over HTTP.
///
/// Between cb503b024 (2026-05-16) and the fix these tests cover,
/// <c>ProjectMemberAcl.ResolveAsync</c> projected all three allow-list columns
/// to <c>(string?)null</c>, so <c>ApplyTo</c> never added a predicate and the
/// ACL restricted nothing for anyone. It was a deliberate crash workaround for
/// an unapplied migration — a premise this codebase invalidated when it settled
/// on EnsureCreated + patchers (docs/adr/0001-schema-management.md).
///
/// Two things these tests are careful about:
///
///   • They assert on NON-EMPTY results. "The member sees nothing" would pass a
///     naive narrowing test just as well as correct filtering does, and it is
///     also what a broken tenant filter produces. Every restriction test below
///     names the documents that MUST still be visible as well as the one that
///     must not.
///
///   • They pin <c>null = all</c> in both directions. That is the documented
///     semantic and existing rows depend on it: a member with no allow-list
///     must keep seeing the whole project, not suddenly see nothing.
/// </summary>
public class ProjectMemberAclTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;

    public ProjectMemberAclTests(PlanscapeWebApplicationFactory factory) => _factory = factory;

    // ── The restriction that had never actually restricted ────────────────────

    [Fact]
    public async Task Member_restricted_to_WIP_and_SHARED_cannot_see_PUBLISHED()
    {
        var project = NewProject();
        SeedMember(project, cde: "WIP,SHARED");
        SeedDocument(project, "ACL-WIP.pdf", cde: "WIP");
        SeedDocument(project, "ACL-SHARED.pdf", cde: "SHARED");
        SeedDocument(project, "ACL-PUBLISHED.pdf", cde: "PUBLISHED");

        var names = await ListAsync(await MemberClientAsync(), project);

        // Non-empty, and specifically the two that are allowed. Asserting only
        // the absence of PUBLISHED would pass if the member saw nothing at all.
        Assert.Contains("ACL-WIP.pdf", names);
        Assert.Contains("ACL-SHARED.pdf", names);
        Assert.DoesNotContain("ACL-PUBLISHED.pdf", names);
    }

    [Fact]
    public async Task Member_restricted_by_discipline_sees_only_that_discipline()
    {
        var project = NewProject();
        SeedMember(project, disciplines: "M,E");
        SeedDocument(project, "ACL-DISC-M.pdf", cde: "SHARED", discipline: "M");
        SeedDocument(project, "ACL-DISC-E.pdf", cde: "SHARED", discipline: "E");
        SeedDocument(project, "ACL-DISC-A.pdf", cde: "SHARED", discipline: "A");

        var names = await ListAsync(await MemberClientAsync(), project);

        Assert.Contains("ACL-DISC-M.pdf", names);
        Assert.Contains("ACL-DISC-E.pdf", names);
        Assert.DoesNotContain("ACL-DISC-A.pdf", names);
    }

    [Fact]
    public async Task Member_restricted_by_suitability_sees_only_those_codes()
    {
        var project = NewProject();
        SeedMember(project, suitabilities: "S2,S3");
        SeedDocument(project, "ACL-SUIT-S2.pdf", cde: "SHARED", suitability: "S2");
        SeedDocument(project, "ACL-SUIT-S3.pdf", cde: "SHARED", suitability: "S3");
        SeedDocument(project, "ACL-SUIT-S4.pdf", cde: "SHARED", suitability: "S4");

        var names = await ListAsync(await MemberClientAsync(), project);

        Assert.Contains("ACL-SUIT-S2.pdf", names);
        Assert.Contains("ACL-SUIT-S3.pdf", names);
        Assert.DoesNotContain("ACL-SUIT-S4.pdf", names);
    }

    [Fact]
    public async Task Axes_combine_with_AND_not_OR()
    {
        // A document must satisfy EVERY populated axis. A document that clears
        // the CDE list but fails the discipline list must not slip through.
        var project = NewProject();
        SeedMember(project, cde: "WIP,SHARED", disciplines: "M");
        SeedDocument(project, "ACL-AND-BOTH.pdf", cde: "SHARED", discipline: "M");
        SeedDocument(project, "ACL-AND-CDE-ONLY.pdf", cde: "SHARED", discipline: "A");
        SeedDocument(project, "ACL-AND-DISC-ONLY.pdf", cde: "PUBLISHED", discipline: "M");

        var names = await ListAsync(await MemberClientAsync(), project);

        Assert.Contains("ACL-AND-BOTH.pdf", names);
        Assert.DoesNotContain("ACL-AND-CDE-ONLY.pdf", names);
        Assert.DoesNotContain("ACL-AND-DISC-ONLY.pdf", names);
    }

    // ── null = all, in both directions ────────────────────────────────────────

    [Fact]
    public async Task Member_with_no_allow_lists_still_sees_the_whole_project()
    {
        // The regression that would hurt most on deploy: turning the ACL back on
        // must not restrict a member who was never given an allow-list. Every
        // ProjectMember row in the local database is this shape.
        var project = NewProject();
        SeedMember(project); // all three columns null
        SeedDocument(project, "ACL-NULL-WIP.pdf", cde: "WIP");
        SeedDocument(project, "ACL-NULL-PUBLISHED.pdf", cde: "PUBLISHED");

        var names = await ListAsync(await MemberClientAsync(), project);

        Assert.Contains("ACL-NULL-WIP.pdf", names);
        Assert.Contains("ACL-NULL-PUBLISHED.pdf", names);
    }

    [Fact]
    public async Task An_empty_string_allow_list_means_all_not_none()
    {
        // ParseAllowList treats "" and "   " as null. Worth pinning: a UI that
        // clears every chip writes an empty string, and reading that as "allow
        // nothing" would lock the member out of the entire project.
        var project = NewProject();
        SeedMember(project, cde: "", disciplines: "   ", suitabilities: "");
        SeedDocument(project, "ACL-EMPTY-WIP.pdf", cde: "WIP");
        SeedDocument(project, "ACL-EMPTY-PUBLISHED.pdf", cde: "PUBLISHED");

        var names = await ListAsync(await MemberClientAsync(), project);

        Assert.Contains("ACL-EMPTY-WIP.pdf", names);
        Assert.Contains("ACL-EMPTY-PUBLISHED.pdf", names);
    }

    // ── Bypass ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tenant_owner_bypasses_the_ACL()
    {
        // Admin / Owner / SecurityOfficer keep cross-project audit reach even
        // where a member row would narrow them.
        var project = NewProject();
        SeedMember(project, cde: "WIP", userId: TestData.AdminUserId);
        SeedDocument(project, "ACL-BYPASS-WIP.pdf", cde: "WIP");
        SeedDocument(project, "ACL-BYPASS-PUBLISHED.pdf", cde: "PUBLISHED");

        var names = await ListAsync(await _factory.CreateAuthenticatedClientAsync(), project);

        Assert.Contains("ACL-BYPASS-WIP.pdf", names);
        Assert.Contains("ACL-BYPASS-PUBLISHED.pdf", names);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Task<HttpClient> MemberClientAsync() =>
        _factory.CreateAuthenticatedClientAsync("member@test.org", "Password123!");

    private static async Task<HashSet<string>> ListAsync(HttpClient client, Guid projectId)
    {
        var res = await client.GetAsync($"/api/projects/{projectId}/documents?pageSize=500");
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("fileName").GetString()!)
            .ToHashSet();
    }

    /// <summary>A fresh project per test so allow-lists cannot leak between them.</summary>
    private Guid NewProject()
    {
        var id = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;
        db.Projects.Add(new Project
        {
            Id = id,
            TenantId = TestData.TenantId,
            Name = $"ACL Project {id:N}",
            Code = $"ACL-{id.ToString("N")[..6]}",
            Status = ProjectStatus.Active,
        });
        db.SaveChanges();
        return id;
    }

    private void SeedMember(
        Guid projectId,
        string? cde = null,
        string? disciplines = null,
        string? suitabilities = null,
        Guid? userId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;
        db.ProjectMembers.Add(new ProjectMember
        {
            Id = Guid.NewGuid(),
            TenantId = TestData.TenantId,
            ProjectId = projectId,
            UserId = userId ?? TestData.MemberUserId,
            ProjectRole = "Contributor",
            Iso19650Role = "E",
            IsActive = true,
            AllowedCdeStates = cde,
            AllowedDisciplines = disciplines,
            AllowedSuitabilities = suitabilities,
        });
        db.SaveChanges();
    }

    private void SeedDocument(
        Guid projectId,
        string fileName,
        string cde = "SHARED",
        string? discipline = null,
        string? suitability = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;
        db.Documents.Add(new DocumentRecord
        {
            Id = Guid.NewGuid(),
            TenantId = TestData.TenantId,
            ProjectId = projectId,
            FileName = fileName,
            DocumentType = "DR",
            CdeStatus = cde,
            Discipline = discipline,
            SuitabilityCode = suitability ?? (cde == "PUBLISHED" ? "S4" : "S3"),
            Revision = "P01",
            UploadedBy = "Test Admin",
            UploadedAt = DateTime.UtcNow.AddMinutes(-5),
            ScanStatus = "CLEAN",
        });
        db.SaveChanges();
    }
}
