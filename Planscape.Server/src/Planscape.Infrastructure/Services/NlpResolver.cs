using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Rule-based NLP resolver with optional LLM fallback. Strategy per NLP-AUTO-LINK:
///
///   1. ISO 19650 tag regex          (deterministic, highest priority)
///   2. Grid reference parsing       (per-project grid name lookup)
///   3. Family/type fuzzy match      (Levenshtein with early exit)
///   4. LLM fallback (optional)      (only when 1–3 returned nothing useful)
///
/// Confidence floor for auto-linking is 0.9 (decision 2.4 = b). Callers always
/// receive the full candidate list so the UI can show a "did you mean?" picker.
/// </summary>
public class NlpResolver : INlpResolver
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INlpLlmResolver _llm;
    private readonly IConfiguration _config;
    private readonly ILogger<NlpResolver> _logger;

    public const double AutoLinkThreshold = 0.9;

    public NlpResolver(
        IServiceScopeFactory scopeFactory,
        INlpLlmResolver llm,
        IConfiguration config,
        ILogger<NlpResolver> logger)
    {
        _scopeFactory = scopeFactory;
        _llm = llm;
        _config = config;
        _logger = logger;
    }

    public async Task<NlpResolution> ResolveAsync(NlpResolveRequest request, CancellationToken ct = default)
    {
        var text = request.Text ?? "";
        var redacted = PiiRedactor.Redact(text);

        var candidates = new List<NlpCandidate>();

        // 1. ISO tag regex — deterministic, always runs.
        candidates.AddRange(ExtractIsoTags(text));

        // 2+3. Grid + fuzzy family — need DB context.
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
            candidates.AddRange(await ExtractGridReferences(db, request.ProjectId, text, ct));
            candidates.AddRange(await ExtractElementMatches(db, request.ProjectId, text, ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rule-based NLP lookup failed — falling back to regex-only");
        }

        var source = candidates.Count > 0 ? candidates[0].Strategy : "None";

        // 4. LLM fallback — only when we have nothing above 0.7 and the caller
        // didn't opt out, and the provider is real (not the null resolver).
        var topConfidence = candidates.Count == 0 ? 0 : candidates.Max(c => c.Confidence);
        var llmAllowed = request.AllowCloudFallback
                        && topConfidence < 0.7
                        && !string.Equals(_llm.ProviderName, "null", StringComparison.OrdinalIgnoreCase)
                        && bool.TryParse(_config["Nlp:EnableLlmFallback"], out var enabled) && enabled;

        if (llmAllowed)
        {
            try
            {
                var llmCandidates = await _llm.ResolveAsync(
                    request.ProjectId, redacted, request.Language,
                    request.MaxCandidates - candidates.Count, ct);
                candidates.AddRange(llmCandidates);
                if (llmCandidates.Count > 0) source = "Llm";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM fallback failed for provider {Provider}", _llm.ProviderName);
            }
        }

        // Sort: highest confidence first, cap to MaxCandidates.
        var ranked = candidates
            .OrderByDescending(c => c.Confidence)
            .Take(request.MaxCandidates)
            .ToList();

        return new NlpResolution(
            InputText: text,
            Candidates: ranked,
            HasAutoLinkCandidate: ranked.Count > 0 && ranked[0].Confidence >= AutoLinkThreshold,
            Source: source,
            RedactedText: redacted != text ? redacted : null);
    }

    // ── 1. ISO 19650 tag regex ────────────────────────────────────────────

    private static readonly Regex IsoTagPattern = new(
        @"\b([A-Z]{1,2})-([A-Z0-9]{2,6})-([A-Z0-9]{2,4})-([A-Z0-9]{2,4})-([A-Z]{2,5})-([A-Z]{2,5})-([A-Z]{2,5})-(\d{3,4})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static IEnumerable<NlpCandidate> ExtractIsoTags(string text)
    {
        foreach (Match m in IsoTagPattern.Matches(text))
        {
            var canonical = m.Value.ToUpperInvariant();
            yield return new NlpCandidate(
                Kind: NlpCandidateKind.IsoTag,
                Label: canonical,
                Confidence: 1.0,
                Strategy: "Regex",
                Target: new Dictionary<string, string?> { ["tag"] = canonical });
        }
    }

    // ── 2. Grid reference parsing ─────────────────────────────────────────

    // Matches "B-3", "grid B-3", "on grid 5/C", "grid 5-C"
    private static readonly Regex GridReferencePattern = new(
        @"\b(?:grid\s+)?(?:on\s+)?([A-Z]{1,2})[\s\-/\\]?(\d{1,3})\b|\b(?:grid\s+)?(\d{1,3})[\s\-/\\]?([A-Z]{1,2})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static async Task<IEnumerable<NlpCandidate>> ExtractGridReferences(
        PlanscapeDbContext db, Guid projectId, string text, CancellationToken ct)
    {
        var result = new List<NlpCandidate>();
        var refs = GridReferencePattern.Matches(text);
        if (refs.Count == 0) return result;

        // Look up tagged elements with a grid_ref parameter on this project.
        // TaggedElement stores the resolved GRID_REF (or ASS_GRID_REF_TXT). When no
        // elements are found, still emit a lower-confidence candidate so the UI can
        // show the grid pick.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in refs)
        {
            var letter = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[4].Value).ToUpperInvariant();
            var number = (m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value);
            var label = $"{letter}-{number}";
            if (!seen.Add(label)) continue;

            var gridRef = $"{letter}/{number}";
            // Low-latency query — projects have hundreds of tagged elements, not thousands.
            var hits = await db.TaggedElements.AsNoTracking()
                .Where(e => e.ProjectId == projectId && e.GridRef != null &&
                            (e.GridRef == label || e.GridRef == gridRef))
                .Take(5)
                .Select(e => new { e.Id, e.Tag1, e.FamilyName })
                .ToListAsync(ct);

            if (hits.Count == 0)
            {
                // Grid location only — no element match yet.
                result.Add(new NlpCandidate(
                    Kind: NlpCandidateKind.GridReference,
                    Label: $"Grid {label}",
                    Confidence: 0.6,
                    Strategy: "Grid",
                    Target: new Dictionary<string, string?> { ["grid"] = label }));
            }
            else
            {
                foreach (var h in hits)
                {
                    result.Add(new NlpCandidate(
                        Kind: NlpCandidateKind.Element,
                        Label: $"{h.Tag1} ({h.FamilyName}) on grid {label}",
                        Confidence: 0.82,
                        Strategy: "Grid",
                        Target: new Dictionary<string, string?>
                        {
                            ["elementId"] = h.Id.ToString(),
                            ["tag"] = h.Tag1,
                            ["grid"] = label,
                        }));
                }
            }
        }
        return result;
    }

    // ── 3. Family / type fuzzy match ──────────────────────────────────────

    private static async Task<IEnumerable<NlpCandidate>> ExtractElementMatches(
        PlanscapeDbContext db, Guid projectId, string text, CancellationToken ct)
    {
        // Heuristic: pull out 2–6-word noun phrases, then fuzzy match against
        // distinct FamilyName values in the project. Short-circuit if text has
        // an ISO tag (already handled).
        if (IsoTagPattern.IsMatch(text)) return Array.Empty<NlpCandidate>();

        var tokens = text.Split(new[] { ' ', ',', '.', ';', ':', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(t => t.Trim())
                         .Where(t => t.Length >= 3)
                         .Take(40) // bound cost
                         .ToArray();
        if (tokens.Length == 0) return Array.Empty<NlpCandidate>();

        // Pull the distinct family names for this project (small list — typically <500).
        var families = await db.TaggedElements.AsNoTracking()
            .Where(e => e.ProjectId == projectId && e.FamilyName != null)
            .Select(e => e.FamilyName!)
            .Distinct()
            .Take(2000)
            .ToListAsync(ct);
        if (families.Count == 0) return Array.Empty<NlpCandidate>();

        var results = new List<(string family, double score)>();
        foreach (var family in families)
        {
            var familyTokens = family.Split(new[] { ' ', '-', '_', '/' }, StringSplitOptions.RemoveEmptyEntries);
            var hits = 0;
            foreach (var token in tokens)
            {
                foreach (var ft in familyTokens)
                {
                    if (string.Equals(ft, token, StringComparison.OrdinalIgnoreCase)) { hits += 2; break; }
                    if (LevenshteinDistance(ft, token) <= 1) { hits += 1; break; }
                }
            }
            if (hits == 0) continue;
            // Normalize by shorter side — short family names shouldn't dominate.
            var score = (double)hits / Math.Max(familyTokens.Length, 1);
            results.Add((family, Math.Min(0.85, score / 2.0))); // cap fuzzy at 0.85
        }
        return results
            .OrderByDescending(r => r.score)
            .Take(5)
            .Select(r => new NlpCandidate(
                Kind: NlpCandidateKind.Element,
                Label: r.family,
                Confidence: r.score,
                Strategy: "Fuzzy",
                Target: new Dictionary<string, string?> { ["family"] = r.family }))
            .ToArray();
    }

    // Iterative Levenshtein with early-exit when we exceed the max distance.
    private static int LevenshteinDistance(string a, string b)
    {
        if (a == b) return 0;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var v0 = new int[b.Length + 1];
        var v1 = new int[b.Length + 1];
        for (var i = 0; i <= b.Length; i++) v0[i] = i;
        for (var i = 0; i < a.Length; i++)
        {
            v1[0] = i + 1;
            for (var j = 0; j < b.Length; j++)
            {
                var cost = char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[j]) ? 0 : 1;
                v1[j + 1] = Math.Min(Math.Min(v1[j] + 1, v0[j + 1] + 1), v0[j] + cost);
            }
            (v0, v1) = (v1, v0);
        }
        return v0[b.Length];
    }
}

/// <summary>
/// Default LLM resolver — refuses to call out, always returns empty. Swap via DI
/// for a real Azure OpenAI / OpenAI / Anthropic implementation when credentials
/// are available. The rule-based path stays intact regardless.
/// </summary>
public class NullLlmResolver : INlpLlmResolver
{
    public string ProviderName => "null";

    public Task<IReadOnlyList<NlpCandidate>> ResolveAsync(
        Guid projectId, string redactedText, string? language,
        int maxCandidates, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<NlpCandidate>>(Array.Empty<NlpCandidate>());
}
