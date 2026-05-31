using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Planscape.Tests;

/// <summary>
/// Phase 159 — exercises the recommended-audit-categories endpoint
/// that ships alongside the SecurityController revoke-tokens flow.
/// Covers the built-in fallback, ETag stability, and the 304
/// short-circuit. The merge-with-appsettings path is covered by
/// <see cref="AuditCategoriesConfiguredTests"/>, which subclasses the
/// factory to inject a configured list before the host boots.
/// </summary>
public class AuditCategoriesControllerTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;

    public AuditCategoriesControllerTests(PlanscapeWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task Get_NoConfig_ReturnsBuiltInList()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/audit/categories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var categories = json.GetProperty("categories")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        // Built-in defaults must always be present so a fresh
        // deployment renders a usable dropdown.
        Assert.Contains("suspected_credential_leak", categories);
        Assert.Contains("employee_offboarding", categories);
        Assert.Contains("scheduled_rotation", categories);
        Assert.Contains("suspicious_activity", categories);
        Assert.Contains("policy_change", categories);
        Assert.Contains("regulatory_request", categories);
        Assert.Contains("unspecified", categories);
    }

    [Fact]
    public async Task Get_AdvisoryNote_IsPresent()
    {
        // Operators must not assume the list is enforced server-side
        // — the note prevents a future schema migration from being
        // mistakenly added based on dashboard behaviour alone.
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/audit/categories");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var note = json.GetProperty("note").GetString();
        Assert.False(string.IsNullOrWhiteSpace(note));
        Assert.Contains("advisory", note!, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_SetsETagAndCacheControl()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/audit/categories");
        Assert.NotNull(response.Headers.ETag);
        Assert.False(string.IsNullOrEmpty(response.Headers.ETag!.Tag));

        var cacheControl = response.Headers.CacheControl;
        Assert.NotNull(cacheControl);
        Assert.True(cacheControl!.Private);
        Assert.Equal(System.TimeSpan.FromHours(1), cacheControl.MaxAge);
    }

    [Fact]
    public async Task Get_StableETag_AcrossCalls()
    {
        // Two consecutive reads with the same configuration must
        // produce the same ETag — that's what makes the 304 path
        // useful. Drift here would invalidate every dashboard cache
        // on every poll.
        var client = await _factory.CreateAuthenticatedClientAsync();
        var first = await client.GetAsync("/api/audit/categories");
        var second = await client.GetAsync("/api/audit/categories");
        Assert.Equal(first.Headers.ETag!.Tag, second.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Get_WithMatchingIfNoneMatch_Returns304()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var first = await client.GetAsync("/api/audit/categories");
        var etag = first.Headers.ETag!.Tag;

        client.DefaultRequestHeaders.IfNoneMatch.Add(
            new System.Net.Http.Headers.EntityTagHeaderValue(etag));
        var second = await client.GetAsync("/api/audit/categories");
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task Get_Unauthenticated_Returns401()
    {
        // No JWT — the [Authorize] attribute should bounce.
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/audit/categories");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_AsMemberRole_StillReturnsList()
    {
        // No RBAC gate on the endpoint — any authenticated user can
        // fetch the recommended list (it's advisory data, not a
        // security action). A coordinator user filing an issue
        // should be able to fetch the dropdown without escalating.
        var client = await _factory.CreateAuthenticatedClientAsync(
            "member@test.org", "Password123!");
        var response = await client.GetAsync("/api/audit/categories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

/// <summary>
/// Phase 159 — sub-factory that injects a configured
/// <c>Audit:Categories</c> list before host bootstrap so the merge
/// branch (configured ∪ built-ins, case-insensitive dedupe) is
/// exercised end-to-end through the real controller.
/// </summary>
public class AuditCategoriesConfiguredFactory : PlanscapeWebApplicationFactory
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Operator-supplied entries — first one is bespoke
                // (regulatory_request is also in the built-ins so we
                // can verify case-insensitive dedupe).
                ["Audit:Categories:0"] = "lost_device",
                ["Audit:Categories:1"] = "REGULATORY_REQUEST",
                ["Audit:Categories:2"] = "tabletop_exercise",
            });
        });
        base.ConfigureWebHost(builder);
    }
}

public class AuditCategoriesConfiguredTests
    : IClassFixture<AuditCategoriesConfiguredFactory>
{
    private readonly AuditCategoriesConfiguredFactory _factory;

    public AuditCategoriesConfiguredTests(AuditCategoriesConfiguredFactory factory)
        => _factory = factory;

    [Fact]
    public async Task Get_WithConfig_MergesConfiguredAndBuiltIns()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/audit/categories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var categories = json.GetProperty("categories")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        // Configured entries are present.
        Assert.Contains("lost_device", categories);
        Assert.Contains("tabletop_exercise", categories);

        // Built-ins still surfaced.
        Assert.Contains("suspected_credential_leak", categories);
        Assert.Contains("employee_offboarding", categories);
    }

    [Fact]
    public async Task Get_WithConfig_CaseInsensitiveDedupe()
    {
        // Configured "REGULATORY_REQUEST" overlaps the built-in
        // "regulatory_request"; the merged list must contain exactly
        // one entry (case-insensitive dedupe). The configured form
        // wins on order.
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/audit/categories");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var categories = json.GetProperty("categories")
            .EnumerateArray().Select(e => e.GetString()!).ToList();

        var matches = categories.Count(c =>
            string.Equals(c, "regulatory_request", System.StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, matches);
    }

    [Fact]
    public async Task Get_WithConfig_ConfiguredOrderingFirst()
    {
        // Operator entries must surface ahead of built-ins so a
        // tenant's preferred taxonomy renders at the top of the
        // dropdown rather than buried below "unspecified".
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/audit/categories");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var categories = json.GetProperty("categories")
            .EnumerateArray().Select(e => e.GetString()!).ToList();

        var lostIdx = categories.IndexOf("lost_device");
        var unspecIdx = categories.FindIndex(c =>
            string.Equals(c, "unspecified", System.StringComparison.OrdinalIgnoreCase));
        Assert.True(lostIdx >= 0 && unspecIdx >= 0);
        Assert.True(lostIdx < unspecIdx,
            "Configured entries should sort ahead of the built-in tail.");
    }

    [Fact]
    public async Task Get_WithConfig_ETagDiffersFromBuiltInOnly()
    {
        // A configured deployment and a vanilla one must produce
        // different ETags — otherwise dashboards on the configured
        // host would 304-cache the vanilla list and never refresh.
        var configuredClient = await _factory.CreateAuthenticatedClientAsync();
        var configuredResp = await configuredClient.GetAsync("/api/audit/categories");
        var configuredEtag = configuredResp.Headers.ETag!.Tag;

        using var vanillaFactory = new PlanscapeWebApplicationFactory();
        var vanillaClient = await vanillaFactory.CreateAuthenticatedClientAsync();
        var vanillaResp = await vanillaClient.GetAsync("/api/audit/categories");
        var vanillaEtag = vanillaResp.Headers.ETag!.Tag;

        Assert.NotEqual(configuredEtag, vanillaEtag);
    }
}
