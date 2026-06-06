using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Planscape.API;

/// <summary>
/// N2 — thin client over the LiveKit Egress Twirp API for recording a live meeting
/// room to the object store (MinIO / S3). Start a room-composite (A/V) or audio-only
/// egress for room = session id; stop it by egress id. The recorder is the LiveKit
/// <c>egress</c> service (separate container) which the LiveKit server dispatches to.
///
/// Auth mirrors <see cref="LiveKitTokenFactory"/>: a raw-HMAC HS256 JWT signed with
/// the API secret, but carrying the EGRESS grant (<c>video.roomRecord = true</c>)
/// rather than a room-join grant.
///
/// Configuration (env-only — never commit secrets):
///   LiveKit:Url / LiveKit:ApiKey / LiveKit:ApiSecret      (already used for tokens)
///   LiveKit:ServerUrl                                      (server→livekit http, e.g. http://livekit:7880)
///   LiveKit:Egress:S3:Endpoint / Bucket / Region / AccessKey / Secret / ForcePathStyle
///
/// <see cref="IsConfigured"/> is false until BOTH LiveKit creds AND an S3 target are
/// present; callers return HTTP 501 in that case (same pattern as the livekit-token
/// endpoint), so the feature ships dark until the egress infra is deployed.
/// </summary>
public sealed class LiveKitEgressClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly string? _apiKey;
    private readonly string? _apiSecret;
    private readonly string? _serverHttpUrl;
    private readonly S3Config _s3;

    public LiveKitEgressClient(IConfiguration config)
    {
        _apiKey = config["LiveKit:ApiKey"] ?? config["LIVEKIT_API_KEY"];
        _apiSecret = config["LiveKit:ApiSecret"] ?? config["LIVEKIT_API_SECRET"];

        // server→livekit must be http(s); fall back to converting the browser ws(s) URL.
        var server = config["LiveKit:ServerUrl"] ?? config["LIVEKIT_SERVER_URL"];
        if (string.IsNullOrWhiteSpace(server))
        {
            var ws = config["LiveKit:Url"] ?? config["LIVEKIT_URL"] ?? "";
            server = ws.Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
                       .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase);
        }
        _serverHttpUrl = string.IsNullOrWhiteSpace(server) ? null : server.TrimEnd('/');

        _s3 = new S3Config
        {
            Endpoint = config["LiveKit:Egress:S3:Endpoint"] ?? config["EGRESS_S3_ENDPOINT"],
            Bucket = config["LiveKit:Egress:S3:Bucket"] ?? config["EGRESS_S3_BUCKET"],
            Region = config["LiveKit:Egress:S3:Region"] ?? config["EGRESS_S3_REGION"] ?? "us-east-1",
            AccessKey = config["LiveKit:Egress:S3:AccessKey"] ?? config["EGRESS_S3_ACCESS_KEY"],
            Secret = config["LiveKit:Egress:S3:Secret"] ?? config["EGRESS_S3_SECRET"],
            ForcePathStyle = (config["LiveKit:Egress:S3:ForcePathStyle"] ?? "true")
                .Equals("true", StringComparison.OrdinalIgnoreCase),
        };
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_apiSecret) &&
        !string.IsNullOrWhiteSpace(_serverHttpUrl) &&
        !string.IsNullOrWhiteSpace(_s3.Bucket) && !string.IsNullOrWhiteSpace(_s3.AccessKey) &&
        !string.IsNullOrWhiteSpace(_s3.Secret) && !string.IsNullOrWhiteSpace(_s3.Endpoint);

    public record EgressResult(string EgressId, string Status, string? StorageKey);

    /// <summary>Start recording a room (= session id). Returns null on transport error.</summary>
    public async Task<EgressResult?> StartAsync(string room, bool audioOnly, CancellationToken ct)
    {
        if (!IsConfigured) return null;
        var ext = audioOnly ? "ogg" : "mp4";
        var fileType = audioOnly ? "OGG" : "MP4";
        var key = $"recordings/{room}/{DateTime.UtcNow:yyyyMMddHHmmss}.{ext}";
        var body = new Dictionary<string, object?>
        {
            ["room_name"] = room,
            ["layout"] = "grid",
            ["audio_only"] = audioOnly,
            ["file_outputs"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["file_type"] = fileType,
                    ["filepath"] = key,
                    ["s3"] = new Dictionary<string, object?>
                    {
                        ["access_key"] = _s3.AccessKey,
                        ["secret"] = _s3.Secret,
                        ["bucket"] = _s3.Bucket,
                        ["region"] = _s3.Region,
                        ["endpoint"] = _s3.Endpoint,
                        ["force_path_style"] = _s3.ForcePathStyle,
                    },
                }
            },
        };
        var info = await CallAsync("StartRoomCompositeEgress", body, ct);
        if (info is null) return null;
        return new EgressResult(info.Value.GetPropOrEmpty("egressId", "egress_id"),
                                info.Value.GetPropOrEmpty("status"), key);
    }

    /// <summary>Stop a running egress by id. Returns null on transport error.</summary>
    public async Task<EgressResult?> StopAsync(string egressId, CancellationToken ct)
    {
        if (!IsConfigured) return null;
        var info = await CallAsync("StopEgress", new Dictionary<string, object?> { ["egress_id"] = egressId }, ct);
        if (info is null) return null;
        return new EgressResult(egressId, info.Value.GetPropOrEmpty("status"), null);
    }

    private async Task<JsonElement?> CallAsync(string method, object body, CancellationToken ct)
    {
        var url = $"{_serverHttpUrl}/twirp/livekit.Egress/{method}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintEgressJwt());
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

    // Egress grant JWT (roomRecord), raw-HMAC HS256 — see LiveKitTokenFactory rationale.
    private string MintEgressJwt()
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object>
        {
            ["iss"] = _apiKey!,
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(10).ToUnixTimeSeconds(),
            ["video"] = new Dictionary<string, object> { ["roomRecord"] = true },
        };
        var header = new Dictionary<string, object> { ["alg"] = "HS256", ["typ"] = "JWT" };
        var signingInput = Seg(header) + "." + Seg(payload);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_apiSecret!));
        var sig = hmac.ComputeHash(Encoding.ASCII.GetBytes(signingInput));
        return signingInput + "." + Base64Url(sig);
    }

    private static string Seg(object o) => Base64Url(JsonSerializer.SerializeToUtf8Bytes(o, JsonOpts));
    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class S3Config
    {
        public string? Endpoint, Bucket, Region, AccessKey, Secret;
        public bool ForcePathStyle = true;
    }
}

internal static class JsonElementEgressExt
{
    public static string GetPropOrEmpty(this JsonElement el, params string[] names)
    {
        foreach (var n in names)
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(n, out var v))
                return v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : v.ToString();
        return "";
    }
}
