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
        return $"{request.Scheme}://{request.Host}".TrimEnd('/');
    }
}
