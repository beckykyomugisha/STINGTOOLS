using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Planscape.Core.Interfaces;

namespace Planscape.API.Controllers;

/// <summary>
/// T3 — Cloud OCR fallback. Mobile calls this when on-device recognition
/// returns confidence below the auto-apply threshold. Rate-limited on the
/// mobile policy; max 10 MB per image.
/// </summary>
[ApiController]
[Route("api/ocr")]
[Authorize]
[EnableRateLimiting("mobile")]
public class OcrController : ControllerBase
{
    private readonly IOcrService _ocr;

    public OcrController(IOcrService ocr) => _ocr = ocr;

    [HttpPost("recognize")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<OcrServerResult>> Recognize(IFormFile? file, CancellationToken ct)
    {
        if (file == null || file.Length == 0) return BadRequest(new { error = "file_required" });
        using var stream = file.OpenReadStream();
        var result = await _ocr.RecognizeAsync(stream, file.ContentType, ct);
        return Ok(result);
    }

    [HttpGet("provider")]
    public ActionResult Provider() => Ok(new { provider = _ocr.ProviderName });
}
