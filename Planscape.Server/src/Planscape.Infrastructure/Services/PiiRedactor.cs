using System.Text.RegularExpressions;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Strips common PII before sending text to an external LLM (decision 2.5 =
/// redact). Deliberately conservative — over-redacting a cloud prompt costs
/// nothing, under-redacting leaks data.
///
///   email addresses       → [email]
///   phone numbers (E.164) → [phone]
///   UK NI numbers         → [ni]
///   credit-card-ish       → [card]
///   /api/.../{guid}       → [resource]
///   8+ digit runs         → [digits]
///
/// Bypass this entirely by calling <see cref="INlpResolver.ResolveAsync"/>
/// with <c>AllowCloudFallback = false</c>.
/// </summary>
public static class PiiRedactor
{
    private static readonly (Regex pattern, string replacement)[] _rules =
    {
        (new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",                       RegexOptions.Compiled), "[email]"),
        (new Regex(@"\b\+?[1-9]\d{1,3}[\s\-]?\(?\d{2,5}\)?[\s\-]?\d{3,4}[\s\-]?\d{3,4}\b",      RegexOptions.Compiled), "[phone]"),
        (new Regex(@"\b[A-CEGHJ-PR-TW-Z]{2}\d{6}[A-D]\b",                                       RegexOptions.Compiled), "[ni]"),
        (new Regex(@"\b(?:\d[ -]*?){13,19}\b",                                                   RegexOptions.Compiled), "[card]"),
        (new Regex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.Compiled), "[guid]"),
        (new Regex(@"\b\d{8,}\b",                                                                RegexOptions.Compiled), "[digits]"),
    };

    public static string Redact(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var redacted = input;
        foreach (var (pattern, replacement) in _rules)
        {
            redacted = pattern.Replace(redacted, replacement);
        }
        return redacted;
    }
}
