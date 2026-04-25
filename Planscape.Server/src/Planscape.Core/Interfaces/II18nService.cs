namespace Planscape.Core.Interfaces;

/// <summary>
/// FLEX-15 — Minimal translation service. Callers pass a dotted key
/// (e.g. "notification.issue_assigned.title") and the current language code;
/// the service returns the translated string with <c>{placeholder}</c> tokens
/// substituted from <paramref name="vars"/>. Missing keys fall back through
///   language → tenant default → "en" → the literal key.
/// </summary>
public interface II18nService
{
    string T(string key, string? language = null, IDictionary<string, object?>? vars = null);

    /// <summary>Returns the list of language codes the server has resource files for.</summary>
    IReadOnlyList<string> SupportedLanguages { get; }

    /// <summary>Reload on-disk resource files without restarting the server.</summary>
    void Reload();
}
