using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// FLEX-03 + FLEX-07 — Minimal safe template engine for outbound email.
///
/// Templates live on disk at <c>EmailTemplates/{name}.{lang}.html</c> with a
/// <c>.txt</c> sibling for the plain-text alternative. The engine:
///   - loads + caches templates once at first use (hot-reload via <see cref="Reload"/>),
///   - falls back through lang → en → baseline string,
///   - substitutes <c>{{Placeholder}}</c> tokens with HTML-escaped values,
///   - accepts a <c>{{#if flag}}…{{/if}}</c> lightweight conditional block.
///
/// Intentionally NOT Handlebars / Razor / Scriban — no external dep, no arbitrary code
/// execution, no expression parser to audit. If richer templating is needed, swap the
/// <see cref="IEmailTemplateRenderer"/> implementation, not the interface.
/// </summary>
public interface IEmailTemplateRenderer
{
    /// <summary>Render the given template name into an HTML string with branding baked in.</summary>
    Task<RenderedEmail> RenderAsync(string templateName, string language,
        IDictionary<string, string?> model, ResolvedBranding branding,
        CancellationToken ct = default);

    /// <summary>Clears the template cache. Called when a tenant uploads a new template.</summary>
    void Reload();
}

public sealed record RenderedEmail(string Subject, string Html, string Text);

public class FileEmailTemplateRenderer : IEmailTemplateRenderer
{
    private readonly IHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<FileEmailTemplateRenderer> _logger;
    private readonly ConcurrentDictionary<string, string> _cache = new();

    public FileEmailTemplateRenderer(
        IHostEnvironment env,
        IConfiguration config,
        ILogger<FileEmailTemplateRenderer> logger)
    {
        _env = env;
        _config = config;
        _logger = logger;
    }

    public Task<RenderedEmail> RenderAsync(
        string templateName, string language,
        IDictionary<string, string?> model, ResolvedBranding branding,
        CancellationToken ct = default)
    {
        // The .subject file owns the subject line so tenants can localise it without
        // editing the HTML. Missing subject → fall back to "{{ProductName}} notification".
        var subject = LoadTemplate(templateName, language, "subject")
                      ?? $"{{ProductName}} notification";
        var html    = LoadTemplate(templateName, language, "html")
                      ?? FallbackHtmlShell(templateName);
        var text    = LoadTemplate(templateName, language, "txt")
                      ?? StripHtml(html);

        // Merge branding into the model without overwriting caller-supplied keys.
        var merged = new Dictionary<string, string?>(model, StringComparer.Ordinal);
        TryAdd(merged, "ProductName",      branding.ProductName);
        TryAdd(merged, "AccentColor",      branding.AccentColor);
        TryAdd(merged, "HeaderColor",      branding.HeaderColor);
        TryAdd(merged, "LogoUrl",          branding.LogoUrl ?? "");
        TryAdd(merged, "SupportEmail",     branding.SupportEmail);
        TryAdd(merged, "EmailSignature",   branding.EmailSignature ?? "");
        TryAdd(merged, "HasLogo",          string.IsNullOrEmpty(branding.LogoUrl) ? "" : "1");
        TryAdd(merged, "HasSignature",     string.IsNullOrEmpty(branding.EmailSignature) ? "" : "1");
        TryAdd(merged, "Year",             DateTime.UtcNow.Year.ToString());

        return Task.FromResult(new RenderedEmail(
            Subject: Render(subject, merged, escape: false).Trim(),
            Html:    Render(html, merged, escape: true),
            Text:    Render(text, merged, escape: false)));
    }

    public void Reload() => _cache.Clear();

    // ── Template I/O ──

    private string? LoadTemplate(string name, string language, string kind)
    {
        // Resolve root once. Prefer configurable Tenant:EmailTemplatesPath, else
        // ContentRoot/EmailTemplates (works for dev + docker image copy).
        var root = _config["Tenant:EmailTemplatesPath"]
                   ?? Path.Combine(_env.ContentRootPath, "EmailTemplates");

        string[] candidates =
        {
            Path.Combine(root, $"{name}.{language}.{kind}"),
            Path.Combine(root, $"{name}.en.{kind}"),
            Path.Combine(root, $"{name}.{kind}"),
        };
        foreach (var path in candidates)
        {
            if (_cache.TryGetValue(path, out var cached)) return cached;
            if (File.Exists(path))
            {
                try
                {
                    var content = File.ReadAllText(path);
                    _cache[path] = content;
                    return content;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read email template {Path}", path);
                }
            }
        }
        return null;
    }

    // ── Rendering ──

    /// <summary>
    /// Placeholders whose values are already-rendered, server-composed HTML
    /// fragments — never raw user input. The layout's <c>{{Body}}</c> slot holds
    /// a pre-rendered template body (e.g. the invite HTML with its <c>&lt;h2&gt;</c>
    /// heading + "Set password &amp; join" button). These MUST be injected
    /// verbatim; HTML-encoding them is the double-escape bug that makes the
    /// recipient see literal <c>&lt;h2&gt;</c> tags. Every other placeholder
    /// stays HTML-encoded.
    /// </summary>
    private static readonly HashSet<string> RawHtmlKeys =
        new(StringComparer.Ordinal) { "Body" };

    private static string Render(string template, IDictionary<string, string?> model, bool escape)
    {
        // Handle simple {{#if Key}}…{{/if}} blocks first — strip block entirely when key is
        // empty/null. No nesting supported; keep complexity out of the pipeline.
        var ifPattern = new System.Text.RegularExpressions.Regex(
            @"\{\{#if\s+([A-Za-z0-9_]+)\}\}(.*?)\{\{/if\}\}",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        template = ifPattern.Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            model.TryGetValue(key, out var val);
            return string.IsNullOrEmpty(val) ? "" : m.Groups[2].Value;
        });

        // Placeholder substitution. Escape when rendering HTML; leave subject/text raw so
        // newlines in signature bodies survive.
        var placeholderPattern = new System.Text.RegularExpressions.Regex(@"\{\{([A-Za-z0-9_]+)\}\}");
        return placeholderPattern.Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            model.TryGetValue(key, out var val);
            val ??= "";
            // Raw-HTML slots (e.g. the layout {{Body}}) carry pre-rendered, trusted
            // markup and must be injected verbatim even in HTML mode — otherwise the
            // layout escapes the inner template and the recipient sees raw <h2> tags.
            return (escape && !RawHtmlKeys.Contains(key)) ? WebUtility.HtmlEncode(val) : val;
        });
    }

    // ── Fallbacks ──

    private static string FallbackHtmlShell(string templateName) => $@"
<!DOCTYPE html><html><body style=""font-family:sans-serif"">
  <h2>{{{{ProductName}}}}</h2>
  <p>(Template <code>{templateName}</code> was not found — rendering the built-in
  fallback. Provide a template to customise this email.)</p>
  <p>{{{{Body}}}}</p>
</body></html>";

    private static string StripHtml(string html)
    {
        var noTags = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "");
        return WebUtility.HtmlDecode(noTags).Trim();
    }

    private static void TryAdd(IDictionary<string, string?> dict, string key, string? value)
    {
        if (!dict.ContainsKey(key)) dict[key] = value;
    }
}
