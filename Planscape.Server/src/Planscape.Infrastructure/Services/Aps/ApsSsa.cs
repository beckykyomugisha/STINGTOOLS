using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Planscape.Core.Entities;

namespace Planscape.Infrastructure.Services.Aps;

/// <summary>The server's default ACC grant (AUT-6). One place, shared by the browser OAuth
/// start and the SSA assertion, so the two can never ask for different scopes.</summary>
public static class ApsScopes
{
    public const string Default = "data:read data:write data:create";
}

/// <summary>
/// AUT-7: Secure Service Account (SSA) tokens - an unattended, 3-legged-equivalent ACC token
/// minted by the server itself, with no person signing in and no refresh token to lose.
///
/// Contract, as read from the official APS pages on 2026-10-01:
///   * developers_guide/jwt-assertions - header <c>alg</c> RS256 and <c>kid</c> = the key id
///     returned by Create Key; claims <c>iss</c> = the app's client id, <c>sub</c> = the
///     service account id, <c>aud</c> = https://developer.api.autodesk.com/authentication/v2/token,
///     <c>exp</c> 0-5 minutes in the future, <c>scope</c> an ARRAY of strings.
///   * reference/http/ssa-exchange-jwt-assertion-POST - POST /authentication/v2/token,
///     form-encoded, Basic client_id:client_secret, <c>grant_type</c> =
///     urn:ietf:params:oauth:grant-type:jwt-bearer, <c>assertion</c> = the JWT. 200 returns
///     access_token, token_type, expires_in (no refresh token is documented).
///   * SSA is GA (APS blog, 2025-09-04). Only server-to-server APS apps support it.
///
/// A connection opts in with <c>accAuthMode: "ssa"</c> in its ConfigJson. The key material is
/// server configuration (<c>Acc:Ssa:*</c>), never stored in the database or the repository.
/// NOT EXERCISED against a live APS tenant.
/// </summary>
public static class ApsSsa
{
    public const string GrantType = "urn:ietf:params:oauth:grant-type:jwt-bearer";
    public const string ModeKey = "accAuthMode";
    public const string ModeSsa = "ssa";
    public const string Placeholder = "REPLACE_WITH_";

    public const string KeyClientId = "Acc:Ssa:ClientId";
    public const string KeyClientSecret = "Acc:Ssa:ClientSecret";
    public const string KeyServiceAccountId = "Acc:Ssa:ServiceAccountId";
    public const string KeyKeyId = "Acc:Ssa:KeyId";
    public const string KeyPrivateKeyPem = "Acc:Ssa:PrivateKeyPem";
    public const string KeyPrivateKeyPath = "Acc:Ssa:PrivateKeyPath";
    public const string KeyScopes = "Acc:Ssa:Scopes";

    /// <summary>Documented ceiling is 5 minutes; stay well inside it for clock skew.</summary>
    public static readonly TimeSpan AssertionLifetime = TimeSpan.FromMinutes(4);

    public sealed record Settings(string ServiceAccountId, string KeyId, string PrivateKeyPem, IReadOnlyList<string> Scopes);

    /// <summary>Does this connection ask for SSA tokens? Unreadable ConfigJson is "no".</summary>
    public static bool IsSsa(PlatformConnection c)
    {
        if (string.IsNullOrWhiteSpace(c?.ConfigJson)) return false;
        try { return string.Equals(((string?)JObject.Parse(c.ConfigJson)[ModeKey])?.Trim(), ModeSsa, StringComparison.OrdinalIgnoreCase); }
        catch (Newtonsoft.Json.JsonException) { return false; }
    }

    /// <summary>
    /// The SSA settings, or null with an error that names EVERY missing or placeholder value -
    /// a half-configured SSA is a visible failure, never a fall-back to another token.
    /// </summary>
    public static Settings? Read(IConfiguration config, out string? error)
    {
        var problems = new List<string>();
        string Get(string key)
        {
            string v = (config[key] ?? "").Trim();
            if (v.Length == 0) problems.Add($"{key} is not set");
            else if (v.StartsWith(Placeholder, StringComparison.Ordinal)) { problems.Add($"{key} is still {v}"); v = ""; }
            return v;
        }
        string sa = Get(KeyServiceAccountId);
        string kid = Get(KeyKeyId);

        string pem = (config[KeyPrivateKeyPem] ?? "").Trim();
        string path = (config[KeyPrivateKeyPath] ?? "").Trim();
        if (pem.StartsWith(Placeholder, StringComparison.Ordinal)) { problems.Add($"{KeyPrivateKeyPem} is still a placeholder"); pem = ""; }
        if (pem.Length == 0 && path.Length > 0 && !path.StartsWith(Placeholder, StringComparison.Ordinal))
        {
            try { pem = File.ReadAllText(path).Trim(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { problems.Add($"{KeyPrivateKeyPath} could not be read ({ex.Message})"); }
        }
        else if (pem.Length == 0 && path.StartsWith(Placeholder, StringComparison.Ordinal))
            problems.Add($"{KeyPrivateKeyPath} is still {path} (or set {KeyPrivateKeyPem})");
        else if (pem.Length == 0) problems.Add($"neither {KeyPrivateKeyPem} nor {KeyPrivateKeyPath} is set");
        // Config providers (env vars) often carry a PEM with literal \n: the documented jwt.io note.
        pem = pem.Replace("\\n", "\n");

        string scopeText = (config[KeyScopes] ?? "").Trim();
        var scopes = (scopeText.Length == 0 ? ApsScopes.Default : scopeText)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();

        if (problems.Count > 0)
        {
            error = "SSA is selected for this connection (accAuthMode = ssa) but is not configured: " + string.Join("; ", problems) + ".";
            return null;
        }
        error = null;
        return new Settings(sa, kid, pem, scopes);
    }

    /// <summary>
    /// H-3: the SSA app's client id and secret - a SERVER-TO-SERVER APS app (the SSA overview:
    /// "Only Server-to-Server APS apps support SSA"). It is separate from <c>Acc:ClientId</c>,
    /// the browser-OAuth app, because one APS app cannot be both: a server-to-server app has no
    /// redirect URI. Missing or placeholder values are named, never borrowed from Acc:ClientId.
    /// </summary>
    public static (string? Id, string? Secret, string? Error) AppCredentials(IConfiguration config)
    {
        var problems = new List<string>();
        string Get(string key)
        {
            string v = (config[key] ?? "").Trim();
            if (v.Length == 0) problems.Add($"{key} is not set");
            else if (v.StartsWith(Placeholder, StringComparison.Ordinal)) { problems.Add($"{key} is still {v}"); v = ""; }
            return v;
        }
        string id = Get(KeyClientId), secret = Get(KeyClientSecret);
        return problems.Count > 0
            ? (null, null, "SSA is selected for this connection (accAuthMode = ssa) but its server-to-server APS app is not configured: " +
                           string.Join("; ", problems) + ".")
            : (id, secret, null);
    }

    /// <summary>Build and sign the JWT assertion (RS256). Pure: no I/O, so it is unit-tested.</summary>
    public static string BuildAssertion(string clientId, Settings s, RSA key, DateTimeOffset now, string audience)
    {
        var header = new JObject { ["alg"] = "RS256", ["typ"] = "JWT", ["kid"] = s.KeyId };
        var claims = new JObject
        {
            ["iss"] = clientId,
            ["sub"] = s.ServiceAccountId,
            ["aud"] = audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.Add(AssertionLifetime).ToUnixTimeSeconds(),
            ["scope"] = new JArray(s.Scopes),
        };
        string signingInput = B64(header.ToString(Newtonsoft.Json.Formatting.None)) + "." + B64(claims.ToString(Newtonsoft.Json.Formatting.None));
        byte[] sig = key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return signingInput + "." + B64(sig);
    }

    internal static string B64(string s) => B64(Encoding.UTF8.GetBytes(s));
    internal static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>The exact audience the docs require: the token endpoint on the production host,
    /// whatever host the request goes to (a test stub or a proxy must not change the claim).</summary>
    public const string Audience = ApsEndpoints.DefaultBaseUrl + "/authentication/v2/token";
}
