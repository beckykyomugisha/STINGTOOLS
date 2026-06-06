using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// N2 — receives LiveKit Egress webhooks and FINALISES the matching
/// <see cref="Planscape.Core.Entities.MeetingRecording"/> row when a recording ends:
/// Status → COMPLETE/FAILED, plus StorageKey / FileSizeBytes / DurationSeconds from the
/// egress file result. Matched by EgressId (cross-tenant — webhooks carry no tenant
/// context, so the lookup ignores the tenant query filter).
///
/// LiveKit signs each webhook with an HS256 JWT in the Authorization header whose
/// <c>sha256</c> claim is the base64 SHA-256 of the raw body; we verify the signature
/// with the API secret + the body hash before trusting it (configured in livekit.yaml
/// `webhook.api_key`).
/// </summary>
[ApiController]
[Route("api/livekit")]
[AllowAnonymous]
public class LiveKitWebhookController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<LiveKitWebhookController> _logger;

    public LiveKitWebhookController(PlanscapeDbContext db, IConfiguration config, ILogger<LiveKitWebhookController> logger)
    {
        _db = db; _config = config; _logger = logger;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        string body;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            body = await reader.ReadToEndAsync(ct);

        var secret = _config["LiveKit:ApiSecret"] ?? _config["LIVEKIT_API_SECRET"];
        var auth = Request.Headers["Authorization"].ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) auth = auth.Substring(7).Trim();
        if (string.IsNullOrWhiteSpace(secret) || !VerifyWebhook(auth, body, secret))
            return Unauthorized();

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var ev = GetStr(root, "event");
            if (!root.TryGetProperty("egressInfo", out var info)) return Ok();

            var egressId = GetStr(info, "egressId");
            if (string.IsNullOrEmpty(egressId)) return Ok();

            var rec = await _db.MeetingRecordings.IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.EgressId == egressId, ct);
            if (rec is null) { _logger.LogWarning("[livekit-webhook] no recording for egress {Egress}", egressId); return Ok(); }

            var status = GetStr(info, "status");          // EGRESS_ACTIVE / EGRESS_COMPLETE / EGRESS_FAILED / EGRESS_ABORTED …
            var terminal = status is "EGRESS_COMPLETE" or "EGRESS_FAILED" or "EGRESS_ABORTED" or "EGRESS_LIMIT_REACHED";
            if (ev == "egress_ended" || terminal)
            {
                // Only a clean EGRESS_COMPLETE counts as a usable recording; aborted /
                // failed / limit-reached are FAILED (the Error field carries the reason).
                rec.Status = status == "EGRESS_COMPLETE" ? "COMPLETE" : "FAILED";
                rec.EndedAt ??= DateTime.UtcNow;
                var err = GetStr(info, "error");
                if (!string.IsNullOrEmpty(err)) rec.Error = err;

                // file results — newer egress: fileResults[]; older: file{}.
                JsonElement file = default; var haveFile = false;
                if (info.TryGetProperty("fileResults", out var fr) && fr.ValueKind == JsonValueKind.Array && fr.GetArrayLength() > 0)
                { file = fr[0]; haveFile = true; }
                else if (info.TryGetProperty("file", out var f) && f.ValueKind == JsonValueKind.Object)
                { file = f; haveFile = true; }

                if (haveFile)
                {
                    var fn = GetStr(file, "filename");
                    if (!string.IsNullOrEmpty(fn)) { rec.StorageKey = fn; rec.FileName = System.IO.Path.GetFileName(fn); }
                    if (TryGetLong(file, "size", out var size)) rec.FileSizeBytes = size;
                    if (TryGetLong(file, "duration", out var durNs) && durNs > 0) rec.DurationSeconds = durNs / 1_000_000_000.0;
                }
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("[livekit-webhook] recording {Id} → {Status} ({Key})", rec.Id, rec.Status, rec.StorageKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[livekit-webhook] parse/handle failed");
        }
        return Ok();
    }

    private static bool VerifyWebhook(string token, string body, string secret)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return false;
            var signingInput = parts[0] + "." + parts[1];
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var expected = Base64Url(hmac.ComputeHash(Encoding.ASCII.GetBytes(signingInput)));
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[2])))
                return false;
            // body integrity: payload.sha256 == base64(SHA256(body)).
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var pdoc = JsonDocument.Parse(payloadJson);
            if (!pdoc.RootElement.TryGetProperty("sha256", out var sha)) return true;  // some events omit it
            var want = sha.GetString();
            var got = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
            return want == got;
        }
        catch { return false; }
    }

    private static string GetStr(JsonElement el, string name)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";
    private static bool TryGetLong(JsonElement el, string name, out long val)
    {
        val = 0;
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var v)) return false;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out val)) return true;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out val)) return true;  // protojson int64 → string
        return false;
    }
    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }
}
