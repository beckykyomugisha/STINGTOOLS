using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Planscape.API;

/// <summary>
/// Single source of truth for the origin of the BROWSER app (planscape-web,
/// the Next.js product surface) — as distinct from <see cref="PublicUrl"/>,
/// which is this API's own public origin.
///
/// The two are different hosts in every real deployment (api.planscape.build vs
/// app.planscape.build; planscape-api-free vs planscape-web-free on Render), so
/// anything that must land a human IN THE PRODUCT — the accept-invite page most
/// of all — needs this, not <see cref="PublicUrl"/>.
///
/// Why it exists: reset-password.html used to GUESS the web origin from its own
/// hostname in JavaScript, with `location.origin` as the last resort. On any
/// deployment whose API host doesn't match the api./planscape-api naming (a
/// Cloudflare tunnel, a bare localhost run, a custom host), that fallback sent
/// the freshly-activated invitee to <c>{api-origin}/projects/{id}</c> — a route
/// the API does not serve and has no SPA fallback for, so the browser got an
/// empty 404: the page "starts opening and then stops". Config beats guessing.
///
/// Resolution order:
///   1. <c>Planscape:WebAppUrl</c> (env <c>Planscape__WebAppUrl</c>) — always wins.
///   2. Derived from the API's own public origin using the deployment's naming
///      convention (api. ↔ app., planscape-api* ↔ planscape-web*).
///   3. <c>null</c> — meaning "unknown". Callers must then NOT navigate; showing
///      a plain "your password is set, sign in" result beats a blank 404.
/// </summary>
public static class WebAppUrl
{
    /// <summary>
    /// The browser app's origin, or null when it cannot be determined.
    /// Never falls back to this API's own origin: that fallback is precisely
    /// what produced the blank-page bug.
    /// </summary>
    public static string? Resolve(IConfiguration config, HttpRequest request)
    {
        var configured = config["Planscape:WebAppUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim().TrimEnd('/');

        return DeriveFromApiOrigin(PublicUrl.Resolve(config, request));
    }

    /// <summary>
    /// Map an API origin onto its paired web origin using the two naming
    /// conventions this platform actually deploys under. Returns null for any
    /// host that matches neither — an unknown pairing is not a guess worth making.
    /// </summary>
    public static string? DeriveFromApiOrigin(string? apiOrigin)
    {
        if (string.IsNullOrWhiteSpace(apiOrigin)) return null;
        if (!Uri.TryCreate(apiOrigin, UriKind.Absolute, out var uri)) return null;

        var host = uri.Host;
        string? webHost = null;

        // Custom-domain pairing: api.planscape.build → app.planscape.build
        if (host.StartsWith("api.", StringComparison.OrdinalIgnoreCase))
            webHost = "app." + host.Substring(4);
        // Render service pairing: planscape-api-free → planscape-web-free
        else if (host.StartsWith("planscape-api", StringComparison.OrdinalIgnoreCase))
            webHost = "planscape-web" + host.Substring("planscape-api".Length);

        if (webHost == null) return null;

        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
        return $"{uri.Scheme}://{webHost}{port}";
    }
}
