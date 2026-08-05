using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Planscape.Tests;

public class AuthControllerTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;

    public AuthControllerTests(PlanscapeWebApplicationFactory factory) => _factory = factory;

    // ── Login ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokens()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "admin@test.org", password = "Password123!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(json.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrEmpty(json.GetProperty("refreshToken").GetString()));
        Assert.Equal("Test Admin", json.GetProperty("userName").GetString());
        Assert.Equal("Owner", json.GetProperty("role").GetString());
        Assert.Equal("Premium", json.GetProperty("tier").GetString());
    }

    [Fact]
    public async Task Login_InvalidPassword_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "admin@test.org", password = "WrongPassword" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_NonexistentUser_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "nobody@test.org", password = "Password123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Register ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_NewOrg_Returns201WithToken()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            organisationName = "New Corp",
            tenantSlug = "new-corp",
            displayName = "Jane Doe",
            email = "jane@newcorp.com",
            password = "SecurePass99!"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(json.GetProperty("accessToken").GetString()));
        Assert.Equal("Starter", json.GetProperty("tier").GetString());
        Assert.True(json.GetProperty("licenseKey").GetString()!.StartsWith("STING-TRIAL-"));
    }

    [Fact]
    public async Task Register_DuplicateSlug_Returns409()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            organisationName = "Test Copy",
            tenantSlug = "test-org",  // already exists
            displayName = "Copy User",
            email = "copy@test.org",
            password = "Password123!"
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_ShortPassword_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            organisationName = "Short PW",
            tenantSlug = "short-pw",
            displayName = "User",
            email = "short@pw.com",
            password = "abc"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Refresh Token ─────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshToken_ValidToken_ReturnsNewTokens()
    {
        var client = _factory.CreateClient();

        // Login first to get a refresh token
        var loginResp = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "admin@test.org", password = "Password123!" });
        var loginJson = await loginResp.Content.ReadFromJsonAsync<JsonElement>();
        var refreshToken = loginJson.GetProperty("refreshToken").GetString();

        // Use refresh token
        var refreshResp = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshResp.StatusCode);

        var refreshJson = await refreshResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(refreshJson.GetProperty("accessToken").GetString()));
        // New refresh token should be different from old one (rotation)
        Assert.NotEqual(refreshToken, refreshJson.GetProperty("refreshToken").GetString());
    }

    [Fact]
    public async Task RefreshToken_InvalidToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = "invalid-token-xyz" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Me ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Me_Authenticated_ReturnsUserInfo()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("admin@test.org", json.GetProperty("email").GetString());
        Assert.Equal("Test Admin", json.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Me_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Change Password ───────────────────────────────────────────────────

    [Fact]
    public async Task ChangePassword_ValidCurrent_Returns200()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(
            "member@test.org", "Password123!");
        var response = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "Password123!",
            newPassword = "NewSecure456!"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrent_Returns401()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "WrongCurrent",
            newPassword = "NewSecure456!"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── License Activation ────────────────────────────────────────────────

    [Fact]
    public async Task ActivateLicense_ValidKey_ReturnsSuccess()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/license/activate", new
        {
            licenseKey = "STING-TEST-KEY-1234567890",
            machineId = "TEST-MACHINE-001",
            revitVersion = "2025",
            userName = "TestUser"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("valid").GetBoolean());
        Assert.Equal("Premium", json.GetProperty("tier").GetString());
    }

    [Fact]
    public async Task ActivateLicense_InvalidKey_ReturnsInvalid()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/license/activate", new
        {
            licenseKey = "INVALID-KEY-DOES-NOT-EXIST",
            machineId = "TEST",
            revitVersion = "2025",
            userName = "Test"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("valid").GetBoolean());
    }

    // ── Health Check ──────────────────────────────────────────────────────

    [Fact]
    public async Task HealthCheck_ReturnsHealthy()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("healthy", json.GetProperty("status").GetString());
    }
}
