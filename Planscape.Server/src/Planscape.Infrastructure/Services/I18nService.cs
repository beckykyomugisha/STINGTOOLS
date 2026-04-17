using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// FLEX-15 — JSON-resource translation service.
///
/// Resource files live at <c>I18n/{lang}.json</c> next to the API DLL (copied via
/// csproj &lt;None Include="I18n\**"&gt;). Format is a dotted-key tree; "_note" keys are
/// ignored. Missing keys fall back through language → "en" → literal key.
/// </summary>
public class I18nService : II18nService
{
    private readonly IHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<I18nService> _logger;

    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _translations = new();
    private IReadOnlyList<string> _supportedLanguages = Array.Empty<string>();
    private string _fallbackLanguage = "en";

    public I18nService(IHostEnvironment env, IConfiguration config, ILogger<I18nService> logger)
    {
        _env = env;
        _config = config;
        _logger = logger;
        LoadAll();
    }

    public IReadOnlyList<string> SupportedLanguages => _supportedLanguages;

    public void Reload()
    {
        _translations.Clear();
        LoadAll();
    }

    public string T(string key, string? language = null, IDictionary<string, object?>? vars = null)
    {
        if (string.IsNullOrEmpty(key)) return "";

        var lang = Normalize(language) ?? Normalize(_config["Tenant:DefaultBranding:DefaultLanguage"]) ?? _fallbackLanguage;

        var value = Lookup(key, lang) ?? Lookup(key, _fallbackLanguage) ?? key;

        if (vars != null && vars.Count > 0)
        {
            value = PlaceholderPattern.Replace(value, m =>
            {
                var varName = m.Groups[1].Value;
                return vars.TryGetValue(varName, out var val) && val != null
                    ? val.ToString() ?? ""
                    : m.Value; // leave unresolved placeholders untouched for diagnostics
            });
        }
        return value;
    }

    // ── loading ──

    private void LoadAll()
    {
        var root = _config["I18n:ResourcesPath"]
                   ?? Path.Combine(_env.ContentRootPath, "I18n");
        if (!Directory.Exists(root))
        {
            _logger.LogWarning("i18n directory not found at {Root} — running without translations", root);
            _supportedLanguages = Array.Empty<string>();
            return;
        }

        var supported = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.json"))
        {
            var lang = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            try
            {
                var flat = LoadFile(file);
                _translations[lang] = flat;
                supported.Add(lang);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load i18n file {File}", file);
            }
        }
        _supportedLanguages = supported;
        _fallbackLanguage = _config["I18n:FallbackLanguage"] ?? "en";
    }

    private static Dictionary<string, string> LoadFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        Flatten(doc.RootElement, "", result);
        return result;
    }

    private static void Flatten(JsonElement element, string prefix, Dictionary<string, string> acc)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Name.StartsWith("_")) continue; // skip metadata keys
                    var key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                    Flatten(prop.Value, key, acc);
                }
                break;
            case JsonValueKind.String:
                acc[prefix] = element.GetString() ?? "";
                break;
            case JsonValueKind.Number:
                acc[prefix] = element.ToString();
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                acc[prefix] = element.GetBoolean().ToString();
                break;
            // Arrays / null — skipped. Translation values should be strings.
        }
    }

    private string? Lookup(string key, string lang)
    {
        if (_translations.TryGetValue(lang, out var dict) && dict.TryGetValue(key, out var val))
            return val;
        return null;
    }

    private static string? Normalize(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return null;
        var code = lang.Trim().ToLowerInvariant();
        // Handle "en-GB" → "en"; keep "zh-hans" because that maps to a resource file.
        var dash = code.IndexOf('-');
        return dash > 0 ? code.Substring(0, dash) : code;
    }

    private static readonly Regex PlaceholderPattern = new(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);
}
