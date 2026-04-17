namespace Planscape.Core.Interfaces;

/// <summary>
/// NLP-AUTO-LINK — resolves free-form text (issue description, search query,
/// meeting-minute line) to structured BIM references:
///   - exact ISO 19650 tags (regex)
///   - grid references (per-project lookup)
///   - family / type fuzzy matches
///
/// Strategy is (d) rule-based + LLM fallback for ambiguous cases. Callers get
/// deterministic results for the common path and only pay for an LLM call when
/// the rules returned zero or ambiguous hits.
/// </summary>
public interface INlpResolver
{
    Task<NlpResolution> ResolveAsync(NlpResolveRequest request, CancellationToken ct = default);
}

public sealed record NlpResolveRequest(
    Guid ProjectId,
    string Text,
    /// <summary>Language hint for PII redaction + LLM prompt locale.</summary>
    string? Language = null,
    /// <summary>When false, the LLM fallback is skipped even if the rules return empty.</summary>
    bool AllowCloudFallback = true,
    /// <summary>Per-call override of the default Max-LLM-Calls rate limiter.</summary>
    int MaxCandidates = 10);

public sealed record NlpResolution(
    /// <summary>Verbatim inputs — useful for replay + audit.</summary>
    string InputText,
    IReadOnlyList<NlpCandidate> Candidates,
    /// <summary>true = at least one candidate had confidence &gt;= AutoLinkThreshold.</summary>
    bool HasAutoLinkCandidate,
    /// <summary>Which strategy produced the top candidate — useful for metrics.</summary>
    string Source,
    /// <summary>Empty when no PII redaction ran or the text had no PII markers.</summary>
    string? RedactedText);

/// <summary>
/// A single resolved reference. Mobile shows the whole list for ambiguous
/// matches (decision 2.4 = b) and auto-links the top one silently when
/// <paramref name="Confidence"/> &gt;= AutoLinkThreshold.
/// </summary>
public sealed record NlpCandidate(
    NlpCandidateKind Kind,
    /// <summary>Human-readable label — rendered in the picker.</summary>
    string Label,
    /// <summary>Confidence 0–1. &gt;=0.9 auto-links, &lt;0.9 waits for user confirm.</summary>
    double Confidence,
    /// <summary>The strategy that produced this candidate (Regex / Grid / Fuzzy / Llm).</summary>
    string Strategy,
    /// <summary>Opaque target — mobile/web dereference based on Kind.</summary>
    IDictionary<string, string?> Target);

public enum NlpCandidateKind
{
    IsoTag = 0,
    GridReference = 1,
    Element = 2,
    Room = 3,
    Discipline = 4,
    Unknown = 99,
}

/// <summary>
/// Pluggable escape hatch for cloud LLMs (OpenAI / Anthropic / Azure OpenAI).
/// The default implementation is <c>NullLlmResolver</c> — returns no hits but
/// never throws, so the rule-based path always wins when no LLM is configured.
/// </summary>
public interface INlpLlmResolver
{
    /// <summary>
    /// Called only when the rule-based path returned zero or very-low confidence.
    /// The caller has already redacted PII; implementations should not re-encode.
    /// </summary>
    Task<IReadOnlyList<NlpCandidate>> ResolveAsync(
        Guid projectId, string redactedText, string? language,
        int maxCandidates, CancellationToken ct);

    /// <summary>Provider name surfaced in audit logs (e.g. "openai", "null").</summary>
    string ProviderName { get; }
}
