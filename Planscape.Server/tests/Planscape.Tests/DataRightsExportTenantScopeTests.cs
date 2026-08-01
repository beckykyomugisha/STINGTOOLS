using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Tests;

/// <summary>
/// S7.4 — the GDPR/POPIA subject-access export
/// (<c>GET /api/data-rights/export</c>).
///
/// The export reads ten tables and streams them into a ZIP. Nine of the ten
/// entity types are <c>ITenantScoped</c> and therefore covered by the global
/// query filter; <c>Tenant</c> itself is deliberately NOT filtered (see the
/// remarks on <c>ApplyTenantQueryFilters</c>) and is scoped by an explicit
/// <c>Where(t =&gt; t.Id == tenantId)</c> in the controller instead. That split
/// is the thing worth guarding: adding an unscoped DbSet, or an
/// <c>IgnoreQueryFilters()</c>, would silently ship one tenant's data to
/// another and nothing else in the suite would notice.
///
/// The isolation assertion here is deliberately paired with a NON-EMPTY
/// assertion. <c>ITenantContext.TenantId</c> falls back to <c>Guid.Empty</c>
/// when no tenant resolves, which matches no rows — so a test that only
/// asserted "the other tenant's data is absent" would pass just as happily
/// against a completely empty archive, proving nothing.
/// </summary>
public class DataRightsExportTenantScopeTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;

    public DataRightsExportTenantScopeTests(PlanscapeWebApplicationFactory factory)
        => _factory = factory;

    private const string ExportPath = "/api/data-rights/export";

    private const string OtherTenantProjectName = "OTHER-TENANT-PROJECT-MUST-NOT-LEAK";

    /// <summary>
    /// Gives the *other* tenant a project, so `projects.json` has a genuine
    /// cross-tenant candidate to leak. Without this the caller's tenant is the
    /// only one with projects and the isolation assertion is untested.
    /// </summary>
    private void SeedOtherTenantProject()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;

        if (db.Projects.Any(p => p.Name == OtherTenantProjectName)) return;

        db.Projects.Add(new Project
        {
            Id = Guid.NewGuid(),
            TenantId = TestData.OtherTenantId,
            Name = OtherTenantProjectName,
            Code = "OTH-999",
            Phase = "Stage 4",
            Status = ProjectStatus.Active
        });
        db.SaveChanges();
    }

    private static async Task<Dictionary<string, string>> ReadArchiveAsync(HttpResponseMessage res)
    {
        var bytes = await res.Content.ReadAsByteArrayAsync();
        using var ms = new MemoryStream(bytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            entries[entry.FullName] = await reader.ReadToEndAsync();
        }
        return entries;
    }

    [Fact]
    public async Task Export_contains_only_the_callers_tenant_rows_and_is_not_empty()
    {
        SeedOtherTenantProject();

        var client = await _factory.CreateAuthenticatedClientAsync(); // admin@test.org — Owner
        var res = await client.GetAsync(ExportPath);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var entries = await ReadArchiveAsync(res);
        var all = string.Join("\n", entries.Values);

        // ── Non-empty: the archive really does carry the caller's own data. ──
        // If ITenantContext ever resolved to Guid.Empty these would fail, which
        // is precisely what stops the isolation assertions below being vacuous.
        Assert.NotEmpty(entries);
        Assert.Contains("tenant.json", entries.Keys);
        Assert.Contains("projects.json", entries.Keys);
        Assert.Contains("users.json", entries.Keys);

        Assert.Contains(TestData.TenantId.ToString(), entries["tenant.json"]);
        Assert.Contains("Test BIM Project", entries["projects.json"]);
        Assert.Contains("admin@test.org", entries["users.json"]);

        // ── Isolation: nothing belonging to the other tenant appears anywhere. ──
        Assert.DoesNotContain(TestData.OtherTenantId.ToString(), all);
        Assert.DoesNotContain(OtherTenantProjectName, all);
        Assert.DoesNotContain("admin@other.org", all);
        Assert.DoesNotContain("Other Organisation", all);

        // ── No credentials: the archive goes to whoever holds Owner/Admin, and
        // must not disclose every member's password hash or refresh token. ──
        Assert.DoesNotContain("PasswordHash", all);
        Assert.DoesNotContain("RefreshToken", all);
        Assert.DoesNotContain("$2a$", all);   // BCrypt hash prefix, in case the
        Assert.DoesNotContain("$2b$", all);   // property is ever renamed.
    }

    [Fact]
    public async Task Export_is_refused_to_roles_below_Owner_or_Admin()
    {
        // member@test.org is a Contributor. The controller is
        // [Authorize(Roles = "Owner,Admin")], so this must not return data.
        var client = await _factory.CreateAuthenticatedClientAsync("member@test.org");
        var res = await client.GetAsync(ExportPath);

        Assert.True(
            res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"Contributor reached the export; got {(int)res.StatusCode} {res.StatusCode}.");
    }
}
