using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Planscape.Infrastructure.SignalR;

/// <summary>
/// Server-side moderation against the LiveKit <c>RoomService</c> Twirp API — the
/// enforcement half of M3's host controls.
///
/// Until now mute-all / remove were co-presence <em>signals</em>: the hub gated who
/// could send them (<see cref="HubTenantGuard.IsSessionHostAsync"/>) but the target
/// client self-applied them, so a modified client could simply ignore the request.
/// This class actually mutes the track / evicts the participant on the SFU.
///
/// Transport + auth deliberately mirror <c>LiveKitEgressClient</c> (Planscape.API):
/// Twirp POST to <c>{ServerUrl}/twirp/livekit.RoomService/{Method}</c> with a raw
/// HMAC-SHA256 JWT. It lives in Infrastructure rather than API because the caller is
/// <see cref="MeetingHub"/>, and Infrastructure cannot reference API.
///
/// The grant differs from the egress one: room-scoped admin APIs require BOTH
/// <c>video.roomAdmin</c> AND <c>video.room</c> naming the specific room, so the JWT
/// is minted per room rather than once per process.
///
/// Configuration (env-only — never commit secrets):
///   LiveKit:ApiKey / LiveKit:ApiSecret     (same pair used for participant tokens)
///   LiveKit:ServerUrl                      (server→livekit http, e.g. http://livekit:7880)
///
/// <see cref="IsConfigured"/> is false when any of those is missing. Callers then keep
/// the legacy signal-only behaviour instead of failing the moderation action — the
/// feature degrades rather than breaking a meeting on an unconfigured deployment.
/// </summary>
public sealed class LiveKitRoomService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly string? _apiKey;
    private readonly string? _apiSecret;
    private readonly string? _serverHttpUrl;

    public LiveKitRoomService(IConfiguration config)
    {
        _apiKey = config["LiveKit:ApiKey"] ?? config["LIVEKIT_API_KEY"];
        _apiSecret = config["LiveKit:ApiSecret"] ?? config["LIVEKIT_API_SECRET"];
        _serverHttpUrl = NormaliseServerUrl(
            config["LiveKit:ServerUrl"] ?? config["LIVEKIT_SERVER_URL"],
            config["LiveKit:Url"] ?? config["LIVEKIT_URL"]);
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_apiKey) &&
        !string.IsNullOrWhiteSpace(_apiSecret) &&
        !string.IsNullOrWhiteSpace(_serverHttpUrl);

    /// <summary>server→LiveKit must be http(s); fall back to converting the browser ws(s) URL.</summary>
    internal static string? NormaliseServerUrl(string? serverUrl, string? browserWsUrl)
    {
        var server = serverUrl;
        if (string.IsNullOrWhiteSpace(server))
        {
            server = (browserWsUrl ?? "")
                .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
                .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase);
        }
        return string.IsNullOrWhiteSpace(server) ? null : server.TrimEnd('/');
    }

    /// <summary>A track published by a participant. <paramref name="Source"/> is LiveKit's
    /// semantic source (MICROPHONE / CAMERA / SCREEN_SHARE …); <paramref name="Type"/> is the
    /// media kind (AUDIO / VIDEO).</summary>
    public sealed record PublishedTrack(string Sid, string Source, string Type, bool Muted);

    public sealed record RoomParticipant(string Identity, string Name, IReadOnlyList<PublishedTrack> Tracks);

    /// <summary>List the participants currently connected to <paramref name="room"/>.
    /// Empty when unconfigured, on transport error, or when the room has no one in it —
    /// callers cannot distinguish those and should not need to.</summary>
    public async Task<IReadOnlyList<RoomParticipant>> ListParticipantsAsync(string room, CancellationToken ct)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(room)) return Array.Empty<RoomParticipant>();
        var resp = await CallAsync(room, "ListParticipants",
            new Dictionary<string, object?> { ["room"] = room }, ct);
        return resp is null ? Array.Empty<RoomParticipant>() : ParseParticipants(resp.Value);
    }

    /// <summary>Mute (or unmute) one published track on the SFU. True when LiveKit accepted it.</summary>
    public async Task<bool> MutePublishedTrackAsync(
        string room, string identity, string trackSid, bool muted, CancellationToken ct)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(room)
            || string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(trackSid)) return false;
        var resp = await CallAsync(room, "MutePublishedTrack", new Dictionary<string, object?>
        {
            ["room"] = room,
            ["identity"] = identity,
            ["track_sid"] = trackSid,
            ["muted"] = muted,
        }, ct);
        return resp is not null;
    }

    /// <summary>Evict a participant from the room. True when LiveKit accepted it.
    /// The client's LiveKit SDK fires a disconnect; it cannot decline.</summary>
    public async Task<bool> RemoveParticipantAsync(string room, string identity, CancellationToken ct)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(room) || string.IsNullOrWhiteSpace(identity))
            return false;
        var resp = await CallAsync(room, "RemoveParticipant", new Dictionary<string, object?>
        {
            ["room"] = room,
            ["identity"] = identity,
        }, ct);
        return resp is not null;
    }

    /// <summary>
    /// Mute every microphone track in the room except <paramref name="exceptIdentity"/>
    /// (the host, who is doing the muting). Returns how many tracks were actually muted;
    /// 0 when unconfigured, when nobody is publishing audio, or when everyone is already
    /// muted (already-muted tracks are skipped, so the call is idempotent).
    /// </summary>
    public async Task<int> MuteAllMicrophonesAsync(string room, string? exceptIdentity, CancellationToken ct)
    {
        if (!IsConfigured) return 0;
        var muted = 0;
        foreach (var p in await ListParticipantsAsync(room, ct))
        {
            if (!string.IsNullOrEmpty(exceptIdentity)
                && string.Equals(p.Identity, exceptIdentity, StringComparison.Ordinal)) continue;
            foreach (var t in p.Tracks)
            {
                if (!IsMicrophone(t) || t.Muted) continue;
                if (await MutePublishedTrackAsync(room, p.Identity, t.Sid, true, ct)) muted++;
            }
        }
        return muted;
    }

    /// <summary>A track counts as a microphone by SOURCE when LiveKit reports one, else by
    /// media type — screen-share audio must NOT be caught by a mute-all, and older/edge
    /// payloads sometimes omit <c>source</c>.</summary>
    internal static bool IsMicrophone(PublishedTrack t)
    {
        if (!string.IsNullOrEmpty(t.Source))
            return t.Source.Equals("MICROPHONE", StringComparison.OrdinalIgnoreCase);
        return t.Type.Equals("AUDIO", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Parse a ListParticipants response. Twirp emits protobuf-JSON (lowerCamelCase)
    /// but snake_case is tolerated so a proxy or older server can't silently yield zero tracks.</summary>
    internal static IReadOnlyList<RoomParticipant> ParseParticipants(JsonElement root)
    {
        var result = new List<RoomParticipant>();
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("participants", out var arr)
            || arr.ValueKind != JsonValueKind.Array) return result;

        foreach (var p in arr.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object) continue;
            var tracks = new List<PublishedTrack>();
            if (p.TryGetProperty("tracks", out var ta) && ta.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in ta.EnumerateArray())
                {
                    if (t.ValueKind != JsonValueKind.Object) continue;
                    var sid = Str(t, "sid");
                    if (string.IsNullOrEmpty(sid)) continue;
                    tracks.Add(new PublishedTrack(sid, Str(t, "source"), Str(t, "type"), Bool(t, "muted")));
                }
            }
            var identity = Str(p, "identity");
            if (string.IsNullOrEmpty(identity)) continue;
            result.Add(new RoomParticipant(identity, Str(p, "name"), tracks));
        }
        return result;
    }

    private static string Str(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var v) || el.TryGetProperty(ToSnake(name), out v))
            return v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : v.ToString();
        return "";
    }

    private static bool Bool(JsonElement el, string name)
        => (el.TryGetProperty(name, out var v) || el.TryGetProperty(ToSnake(name), out v))
           && v.ValueKind == JsonValueKind.True;

    private static string ToSnake(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (char.IsUpper(c)) { sb.Append('_'); sb.Append(char.ToLowerInvariant(c)); }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private async Task<JsonElement?> CallAsync(
        string room, string method, object body, CancellationToken ct)
    {
        var url = $"{_serverHttpUrl}/twirp/livekit.RoomService/{method}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintRoomAdminJwt(room));
        try
        {
            using var resp = await Http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch { return null; }
    }

    /// <summary>Room-scoped admin JWT (roomAdmin + room), raw-HMAC HS256 — see
    /// LiveKitTokenFactory for why Microsoft's JWT stack is not used (it rejects
    /// keys under 128 bits, which the LiveKit dev secret is).</summary>
    internal string MintRoomAdminJwt(string room) => MintRoomAdminJwt(_apiKey!, _apiSecret!, room);

    internal static string MintRoomAdminJwt(string apiKey, string apiSecret, string room)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object>
        {
            ["iss"] = apiKey,
            ["sub"] = apiKey,
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(10).ToUnixTimeSeconds(),
            // roomAdmin is only honoured together with the specific room name.
            ["video"] = new Dictionary<string, object>
            {
                ["roomAdmin"] = true,
                ["roomList"] = true,
                ["room"] = room,
            },
        };
        var header = new Dictionary<string, object> { ["alg"] = "HS256", ["typ"] = "JWT" };
        var signingInput = Seg(header) + "." + Seg(payload);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret));
        var sig = hmac.ComputeHash(Encoding.ASCII.GetBytes(signingInput));
        return signingInput + "." + Base64Url(sig);
    }

    private static string Seg(object o) => Base64Url(JsonSerializer.SerializeToUtf8Bytes(o, JsonOpts));
    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
