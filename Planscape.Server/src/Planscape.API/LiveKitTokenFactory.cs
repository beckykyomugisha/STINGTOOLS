using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Planscape.API;

/// <summary>
/// WS3 / M1 — mints a LiveKit access token. A LiveKit token is a standard HS256 JWT
/// signed with the API secret: <c>iss</c> = API key, <c>sub</c> = participant identity,
/// and a nested <c>video</c> grant claim.
///
/// The JWT is built + signed with RAW <see cref="HMACSHA256"/> (not Microsoft.IdentityModel)
/// ON PURPOSE: Microsoft's JWT stack rejects keys shorter than 128 bits (IDX10653), but
/// LiveKit's dev secret ("secret", 6 bytes) is shorter than that and LiveKit validates
/// tokens with plain HMAC — no minimum-length rule. Signing manually matches exactly what
/// LiveKit expects and works with dev AND production secrets of any length.
///
/// Keys come from config (LiveKit:ApiKey / LiveKit:ApiSecret), never hard-coded.
/// </summary>
public static class LiveKitTokenFactory
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static string Create(
        string apiKey, string apiSecret, string room, string identity, string? name,
        bool canPublish, bool canSubscribe, bool allowScreenShare, TimeSpan ttl)
    {
        // The LiveKit "video" grant. canPublishSources gates screen-share: only
        // presenters/hosts get the screen_share source.
        var video = new Dictionary<string, object>
        {
            ["room"] = room,
            ["roomJoin"] = true,
            ["canPublish"] = canPublish,
            ["canSubscribe"] = canSubscribe,
            ["canPublishData"] = true,
        };
        if (canPublish)
        {
            video["canPublishSources"] = allowScreenShare
                ? new[] { "camera", "microphone", "screen_share", "screen_share_audio" }
                : new[] { "camera", "microphone" };
        }

        var now = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object>
        {
            ["iss"] = apiKey,                              // LiveKit requires iss = API key
            ["sub"] = identity,
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.Add(ttl).ToUnixTimeSeconds(),
            ["video"] = video,
        };
        if (!string.IsNullOrWhiteSpace(name)) payload["name"] = name!;

        var header = new Dictionary<string, object> { ["alg"] = "HS256", ["typ"] = "JWT" };

        var signingInput = Seg(header) + "." + Seg(payload);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret));
        var sig = hmac.ComputeHash(Encoding.ASCII.GetBytes(signingInput));
        return signingInput + "." + Base64Url(sig);
    }

    private static string Seg(object o) => Base64Url(JsonSerializer.SerializeToUtf8Bytes(o, JsonOpts));

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
