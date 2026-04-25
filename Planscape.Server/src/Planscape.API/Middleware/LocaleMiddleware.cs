using System.Globalization;
using Planscape.Core.Interfaces;

namespace Planscape.API.Middleware;

/// <summary>
/// FLEX-15 — Extracts the caller's preferred language in priority order:
///
///   1. <c>X-Language</c> HTTP header (mobile override, highest priority)
///   2. <c>language</c> query-string param (useful for share links)
///   3. <c>Accept-Language</c> HTTP header — first match against supported languages
///   4. Tenant's <c>DefaultLanguage</c> branding value
///   5. Server fallback (<c>en</c>)
///
/// Result is stored on <c>HttpContext.Items["Language"]</c> for downstream handlers.
/// </summary>
public class LocaleMiddleware
{
    private readonly RequestDelegate _next;

    public LocaleMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, II18nService i18n)
    {
        var lang = ResolveLanguage(ctx, i18n);
        ctx.Items["Language"] = lang;

        // Also set CultureInfo so any framework formatting (money, dates) follows suit.
        try
        {
            var culture = new CultureInfo(lang);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // Fall through — keep the default culture.
        }

        // Expose the resolved language so clients can confirm what the server thinks.
        ctx.Response.Headers["Content-Language"] = lang;
        await _next(ctx);
    }

    private static string ResolveLanguage(HttpContext ctx, II18nService i18n)
    {
        var supported = i18n.SupportedLanguages;

        // 1. X-Language header
        var headerOverride = ctx.Request.Headers["X-Language"].ToString();
        if (!string.IsNullOrWhiteSpace(headerOverride))
        {
            var match = MatchSupported(headerOverride, supported);
            if (match != null) return match;
        }

        // 2. ?language= query
        if (ctx.Request.Query.TryGetValue("language", out var qlang))
        {
            var match = MatchSupported(qlang.ToString(), supported);
            if (match != null) return match;
        }

        // 3. Accept-Language header — "en-GB,en;q=0.9,de;q=0.8"
        var accept = ctx.Request.Headers["Accept-Language"].ToString();
        if (!string.IsNullOrEmpty(accept))
        {
            foreach (var part in accept.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var lang = part.Split(';')[0].Trim();
                var match = MatchSupported(lang, supported);
                if (match != null) return match;
            }
        }

        // 4. Fallback. i18n service uses tenant default + "en" internally when we pass null.
        return "en";
    }

    private static string? MatchSupported(string requested, IReadOnlyList<string> supported)
    {
        if (string.IsNullOrWhiteSpace(requested) || supported.Count == 0) return null;
        var code = requested.Trim().ToLowerInvariant();

        // Exact match first.
        if (supported.Any(s => string.Equals(s, code, StringComparison.OrdinalIgnoreCase)))
            return code;

        // "en-GB" → "en"
        var dash = code.IndexOf('-');
        if (dash > 0)
        {
            var prefix = code.Substring(0, dash);
            var match = supported.FirstOrDefault(s => string.Equals(s, prefix, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        // "en" → "en-GB" (supported language is more specific than request)
        var loose = supported.FirstOrDefault(s => s.StartsWith(code + "-", StringComparison.OrdinalIgnoreCase));
        return loose;
    }
}
