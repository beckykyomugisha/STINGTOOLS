using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planscape.Core.Interfaces;

namespace Planscape.API.Controllers;

/// <summary>
/// NLP-AUTO-LINK — /api/nlp/resolve. Accepts free text + project id, returns
/// a ranked list of structured references the mobile client can auto-link
/// (top candidate &gt;= 0.9) or show as a "did you mean?" picker.
/// </summary>
[ApiController]
[Route("api/nlp")]
[Authorize]
public class NlpController : ControllerBase
{
    private readonly INlpResolver _resolver;

    public NlpController(INlpResolver resolver) => _resolver = resolver;

    [HttpPost("resolve")]
    public async Task<ActionResult<NlpResolution>> Resolve(
        [FromBody] ResolveRequest req, CancellationToken ct)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Text))
            return BadRequest(new { error = "text_required" });
        if (req.ProjectId == Guid.Empty)
            return BadRequest(new { error = "project_required" });
        if (req.Text.Length > 4000)
            return BadRequest(new { error = "text_too_long", maxLength = 4000 });

        var language = (HttpContext.Items["Language"] as string) ?? req.Language;
        var request = new NlpResolveRequest(
            ProjectId: req.ProjectId,
            Text: req.Text,
            Language: language,
            AllowCloudFallback: req.AllowCloudFallback ?? true,
            MaxCandidates: Math.Clamp(req.MaxCandidates ?? 10, 1, 25));

        var result = await _resolver.ResolveAsync(request, ct);
        return Ok(result);
    }
}

public record ResolveRequest(
    Guid ProjectId,
    string Text,
    string? Language = null,
    bool? AllowCloudFallback = null,
    int? MaxCandidates = null);
