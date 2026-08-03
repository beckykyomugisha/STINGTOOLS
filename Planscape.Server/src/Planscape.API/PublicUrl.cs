using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Planscape.API;

/// <summary>
/// Single source of truth for the public-facing base URL used in every outward
/// link (invite / password-reset emails, share + QR links, accept-invite pages).
///
/// When <c>Planscape:PublicBaseUrl</c> is set (env: <c>Planscape__PublicBaseUrl</c>)
/// it WINS — this is required behind a reverse proxy / Cloudflare tunnel, where the
/// request Host the app sees is the internal origin (localhost:5000), NOT the URL the
/// recipient must click. Falling back to <c>{scheme}://{host}</c> keeps a bare
/// localhost dev run working with zero config.
///
/// Config-first by design: moving from tunnel → cloud is an env change only
/// (set PublicBaseUrl to the cloud hostname), no rebuild.
/// </summary>
public static class PublicUrl
{
    public static string Resolve(IConfiguration config, HttpRequest request)
    {
        var configured = config["Planscape:PublicBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim().TrimEnd('/');

        // Behind a TLS-terminating proxy (Render, Cloudflare, most PaaS) the
        // request reaches the app over PLAIN HTTP, so request.Scheme is "http"
        // even though the public URL is https. Links built from it came out as
        // http://…, and the host answers those with a redirect or an outright
        // closed connection — a real invitee clicked one and got
        // ERR_CONNECTION_CLOSED, i.e. every emailed link was dead.
        //
        // X-Forwarded-Proto is what the proxy sets to report the ORIGINAL
        // scheme. Trusting it is appropriate here specifically because this
        // value only ever builds an outward display link; it makes no
        // authorisation decision, so a spoofed header cannot escalate anything.
        var forwarded = request.Headers["X-Forwarded-Proto"].ToString();
        var scheme = string.IsNullOrWhiteSpace(forwarded)
            ? request.Scheme
            // The header is a comma-separated list when several proxies chain;
            // the ORIGINAL client scheme is the first entry.
            : forwarded.Split(',')[0].Trim();

        return $"{scheme}://{request.Host}".TrimEnd('/');
    }
}
