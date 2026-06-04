using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Planscape.API;

/// <summary>
/// WS3 — mints a LiveKit access token. A LiveKit token is a standard HS256 JWT
/// signed with the API secret: <c>iss</c> = API key, <c>sub</c> = participant
/// identity, and a nested <c>video</c> grant claim. We mint it directly (no LiveKit
/// .NET SDK dependency) using the same JWT stack the app already uses for auth.
///
/// Keys come from config (LiveKit:ApiKey / LiveKit:ApiSecret), never hard-coded.
/// </summary>
public static class LiveKitTokenFactory
{
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

        var claims = new Dictionary<string, object> { ["sub"] = identity, ["video"] = video };
        if (!string.IsNullOrWhiteSpace(name)) claims["name"] = name!;

        var now = DateTime.UtcNow;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(apiSecret));
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = apiKey,            // LiveKit requires iss = API key
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(ttl),
            Claims = claims,            // nested 'video' dict serialises to a JSON object
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
