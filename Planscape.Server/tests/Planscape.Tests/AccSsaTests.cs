using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Services;
using Planscape.Infrastructure.Services.Aps;
using Xunit;

namespace Planscape.Tests;

/// <summary>AUT-7: Secure Service Account tokens. The key below is generated per test; no real
/// key, id or secret exists in this file.</summary>
public partial class AccServerIntegrationTests
{
    private static (RSA Key, string Pem) TestKey()
    {
        var rsa = RSA.Create(2048);
        return (rsa, rsa.ExportRSAPrivateKeyPem());
    }

    private static IConfiguration SsaConfig(IConfiguration baseCfg, string? pem, string sa = "SA-TEST", string kid = "kid-test")
        => new ConfigurationBuilder().AddConfiguration(baseCfg).AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ApsSsa.KeyClientId] = "s2s-client",
            [ApsSsa.KeyClientSecret] = "s2s-secret",
            [ApsSsa.KeyServiceAccountId] = sa,
            [ApsSsa.KeyKeyId] = kid,
            [ApsSsa.KeyPrivateKeyPem] = pem,
        }).Build();

    private static JObject Part(string jwt, int i)
    {
        string p = jwt.Split('.')[i].Replace('-', '+').Replace('_', '/');
        p = p.PadRight(p.Length + (4 - p.Length % 4) % 4, '=');
        return JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(p)));
    }

    [Fact]
    public void The_SSA_assertion_carries_the_documented_header_and_claims_and_verifies()
    {
        var (key, _) = TestKey();
        var s = new ApsSsa.Settings("SA-1", "kid-1", "", new[] { "data:read", "data:write" });
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

        string jwt = ApsSsa.BuildAssertion("client-1", s, key, now, ApsSsa.Audience);

        var h = Part(jwt, 0);
        Assert.Equal("RS256", (string?)h["alg"]);
        Assert.Equal("kid-1", (string?)h["kid"]);
        var c = Part(jwt, 1);
        Assert.Equal("client-1", (string?)c["iss"]);
        Assert.Equal("SA-1", (string?)c["sub"]);
        Assert.Equal("https://developer.api.autodesk.com/authentication/v2/token", (string?)c["aud"]);
        long exp = (long)c["exp"]!;
        Assert.InRange(exp - now.ToUnixTimeSeconds(), 1, 300);                    // documented: 0-5 minutes
        Assert.Equal(JTokenType.Array, c["scope"]!.Type);                          // documented: an ARRAY, not a string
        Assert.Equal(new[] { "data:read", "data:write" }, c["scope"]!.Select(t => (string)t!));

        var parts = jwt.Split('.');
        string sig = parts[2].Replace('-', '+').Replace('_', '/');
        sig = sig.PadRight(sig.Length + (4 - sig.Length % 4) % 4, '=');
        Assert.True(key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Convert.FromBase64String(sig),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public void Missing_or_placeholder_SSA_settings_are_named_never_defaulted()
    {
        var none = new ConfigurationBuilder().Build();
        Assert.Null(ApsSsa.Read(none, out var err));
        Assert.Contains(ApsSsa.KeyServiceAccountId, err);
        Assert.Contains(ApsSsa.KeyKeyId, err);
        Assert.Contains(ApsSsa.KeyPrivateKeyPem, err);

        var placeholders = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ApsSsa.KeyServiceAccountId] = "REPLACE_WITH_SSA_SERVICE_ACCOUNT_ID",
            [ApsSsa.KeyKeyId] = "REPLACE_WITH_SSA_KEY_ID",
            [ApsSsa.KeyPrivateKeyPem] = "REPLACE_WITH_SSA_PRIVATE_KEY_PEM",
        }).Build();
        Assert.Null(ApsSsa.Read(placeholders, out err));
        Assert.Contains("REPLACE_WITH_SSA_SERVICE_ACCOUNT_ID", err);
        Assert.Contains("placeholder", err);

        // The shipped template's shape: PEM empty, path still the placeholder - named as such.
        var template = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ApsSsa.KeyServiceAccountId] = "SA-1", [ApsSsa.KeyKeyId] = "k-1",
            [ApsSsa.KeyPrivateKeyPem] = "", [ApsSsa.KeyPrivateKeyPath] = "REPLACE_WITH_PATH_TO_SSA_PRIVATE_KEY_PEM",
        }).Build();
        Assert.Null(ApsSsa.Read(template, out err));
        Assert.Contains("REPLACE_WITH_PATH_TO_SSA_PRIVATE_KEY_PEM", err);

        var (_, pem) = TestKey();
        var ok = SsaConfig(new ConfigurationBuilder().Build(), pem.Replace("\n", "\\n"));   // env-var style
        var s = ApsSsa.Read(ok, out err);
        Assert.Null(err);
        Assert.Contains("\n", s!.PrivateKeyPem);
        Assert.Equal(ApsScopes.Default.Split(' '), s.Scopes);
    }

    private async Task<PlatformConnection> SeedSsaConnectionAsync(Fx fx)
    {
        await fx.SeedAsync(configJson: "{\"accIssueSubtypeId\":\"sub-1\",\"accAuthMode\":\"ssa\"}", openIssues: 0,
            access: "", refresh: "", expires: DateTime.UtcNow.AddHours(-1));
        return await fx.ReadConnAsync();
    }

    [Fact]
    public async Task An_SSA_connection_mints_its_token_from_a_signed_assertion_with_no_refresh_token()
    {
        var fx = new Fx();
        await SeedSsaConnectionAsync(fx);
        var (key, pem) = TestKey();
        var cfg = SsaConfig(fx.Config, pem);
        fx.Http.Respond = (req, body) => req.RequestUri!.AbsolutePath.EndsWith("/authentication/v2/token")
            ? Json(HttpStatusCode.OK, new { access_token = "ssa-access", token_type = "Bearer", expires_in = 3600 })
            : new HttpResponseMessage(HttpStatusCode.NotFound);

        using var db = fx.Db();
        var conn = await db.PlatformConnections.SingleAsync(c => c.Id == fx.ConnId);
        var connector = new AccConnector(cfg, new Factory(fx.Http), NullLogger<AccConnector>.Instance);
        var outcome = await AccTokenRefresher.EnsureFreshAsync(db, connector, conn, null, default);

        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal("ssa-access", (await fx.ReadConnAsync()).AccessToken);
        var call = Assert.Single(fx.Http.Calls, c => c.Url.EndsWith("/authentication/v2/token"));
        var form = call.Body!.Split('&').Select(kv => kv.Split('=', 2))
            .ToDictionary(kv => Uri.UnescapeDataString(kv[0]), kv => Uri.UnescapeDataString(kv[1].Replace('+', ' ')));
        Assert.Equal("urn:ietf:params:oauth:grant-type:jwt-bearer", form["grant_type"]);
        Assert.False(form.ContainsKey("refresh_token"));
        string assertion = form["assertion"];
        Assert.Equal("SA-TEST", (string?)Part(assertion, 1)["sub"]);
        // H-3: the SSA's own server-to-server app, not the browser-OAuth app (Acc:ClientId = "cid").
        Assert.Equal("s2s-client", (string?)Part(assertion, 1)["iss"]);
    }

    [Fact]
    public async Task An_SSA_connection_without_settings_fails_naming_them_and_is_not_a_reconnect()
    {
        var fx = new Fx();
        await SeedSsaConnectionAsync(fx);
        using var db = fx.Db();
        var conn = await db.PlatformConnections.SingleAsync(c => c.Id == fx.ConnId);
        var connector = new AccConnector(fx.Config, new Factory(fx.Http), NullLogger<AccConnector>.Instance);

        var outcome = await AccTokenRefresher.EnsureFreshAsync(db, connector, conn, null, default);

        Assert.False(outcome.Success);
        Assert.False(outcome.ReconnectRequired);                // a server setting, not a person's sign-in
        Assert.Contains(ApsSsa.KeyClientId, outcome.Error);       // its own app is named first
        Assert.Empty(fx.Http.Calls);                             // nothing sent with half a configuration

        // With the app configured but not the account, the account settings are named.
        var appOnly = new ConfigurationBuilder().AddConfiguration(fx.Config).AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ApsSsa.KeyClientId] = "s2s-client", [ApsSsa.KeyClientSecret] = "s2s-secret",
        }).Build();
        var c2 = new AccConnector(appOnly, new Factory(fx.Http), NullLogger<AccConnector>.Instance);
        var o2 = await AccTokenRefresher.EnsureFreshAsync(db, c2, conn, null, default);
        Assert.Contains(ApsSsa.KeyServiceAccountId, o2.Error);
        Assert.Empty(fx.Http.Calls);
    }

    [Fact]
    public async Task An_SSA_assertion_ACC_refuses_is_reported_with_what_to_check()
    {
        var fx = new Fx();
        await SeedSsaConnectionAsync(fx);
        var (_, pem) = TestKey();
        fx.Http.Respond = (req, _) => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":\"invalid_grant\"}") };
        using var db = fx.Db();
        var conn = await db.PlatformConnections.SingleAsync(c => c.Id == fx.ConnId);
        var connector = new AccConnector(SsaConfig(fx.Config, pem), new Factory(fx.Http), NullLogger<AccConnector>.Instance);

        var outcome = await AccTokenRefresher.EnsureFreshAsync(db, connector, conn, null, default);

        Assert.False(outcome.Success);
        Assert.Contains("SSA assertion", outcome.Error);
        Assert.Contains("HTTP 400", outcome.Error);
    }
}
