// C1 prep — NOT wired. Activated in Program.cs when chunk C1 lands.
// Do not call from controllers until then.

using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Planscape.API.Auth;

/// <summary>
/// C1 prep — NOT wired. Activated in Program.cs when chunk C1 lands.
/// Do not call from controllers until then.
///
/// <para>
/// Builds the <see cref="TokenValidationParameters"/> for the heavy-BIM API so
/// it accepts JWTs minted by the Cloudflare Workers auth layer (B1) at
/// <c>planscape.build</c>. HS256, signed with the shared secret read from the
/// <c>JWT_SECRET</c> environment variable — the SAME value the Workers sign
/// with. (The current server still reads <c>Jwt:Key</c>; the C1 commit renames
/// those references. This stub deliberately uses the Workers-side name.)
/// </para>
/// </summary>
public static class PlanscapeJwtHandler
{
    /// <summary>Environment variable carrying the shared HS256 secret (== Cloudflare Workers JWT_SECRET).</summary>
    public const string SecretEnvVar = "JWT_SECRET";

    /// <summary>Issuer the Workers stamp into the <c>iss</c> claim.</summary>
    public const string Issuer = "planscape";

    /// <summary>
    /// Audiences this API accepts. <c>planscape-heavy</c> is its own audience;
    /// <c>planscape</c> is accepted so general platform tokens validate here too.
    /// </summary>
    public static readonly string[] Audiences = { "planscape-heavy", "planscape" };

    /// <summary>Clock skew allowance for <c>exp</c>/<c>nbf</c> — 60 seconds.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Construct the validation parameters for the JwtBearer handler. C1 wires
    /// this into <c>AddAuthentication().AddJwtBearer(o =&gt; o.TokenValidationParameters
    /// = PlanscapeJwtHandler.CreateValidationParameters(secret))</c>.
    /// </summary>
    /// <param name="secret">The shared HS256 secret (32+ chars). Read from <see cref="SecretEnvVar"/>.</param>
    public static TokenValidationParameters CreateValidationParameters(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            throw new InvalidOperationException(
                $"{SecretEnvVar} is required and must be at least 32 characters (HMAC-SHA256 minimum). " +
                "It MUST equal the Cloudflare Workers JWT_SECRET so cross-stack tokens validate.");

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudiences = Audiences,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ClockSkew = ClockSkew,
            // Map the short claim the Workers emit (`role`) as the role claim so
            // [Authorize(Roles=...)] / RequireRoleAttribute can read it.
            RoleClaimType = "role",
            NameClaimType = "sub",
        };
    }

    /// <summary>
    /// Convenience: read the secret from the environment and build the params.
    /// Throws if <see cref="SecretEnvVar"/> is unset/too short.
    /// </summary>
    public static TokenValidationParameters CreateValidationParametersFromEnv()
        => CreateValidationParameters(Environment.GetEnvironmentVariable(SecretEnvVar) ?? string.Empty);
}
