using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Planscape.Infrastructure.SignalR;

namespace Planscape.Tests;

/// <summary>
/// S1 — server-enforced moderation. <see cref="LiveKitRoomService"/> is the half of
/// M3's mute/remove that a modified client cannot ignore, so the parts that decide
/// WHICH tracks get muted and WHETHER the SFU will accept the call are worth pinning.
///
/// Two tiers, deliberately:
///   • pure unit tests (always run) — response parsing, mic-vs-screen-audio
///     classification, URL normalisation, and the shape of the admin JWT;
///   • a <see cref="SkippableFact"/> integration test that talks to a REAL LiveKit
///     (the docker-compose one on :7880) and proves the Twirp route + grant actually
///     authenticate. It SKIPS — not passes, not fails — when LiveKit isn't running,
///     so CI without the stack stays honest.
/// </summary>
public class LiveKitRoomServiceTests
{
    private static LiveKitRoomService Build(params (string Key, string? Value)[] settings)
        => new(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build());

    private static LiveKitRoomService Configured(string url = "http://localhost:7880") => Build(
        ("LiveKit:ApiKey", "devkey"),
        ("LiveKit:ApiSecret", "secret"),
        ("LiveKit:ServerUrl", url));

    // ── configuration gate ────────────────────────────────────────────────────

    [Fact]
    public void IsConfigured_false_when_credentials_missing()
    {
        Assert.False(Build().IsConfigured);
        Assert.False(Build(("LiveKit:ApiKey", "devkey")).IsConfigured);
        Assert.False(Build(("LiveKit:ApiKey", "devkey"), ("LiveKit:ApiSecret", "secret")).IsConfigured);
        Assert.True(Configured().IsConfigured);
    }

    [Fact]
    public async Task Unconfigured_service_is_a_no_op_not_a_throw()
    {
        // Moderation must degrade to signal-only, never break the meeting.
        var svc = Build();
        Assert.Empty(await svc.ListParticipantsAsync("room", default));
        Assert.False(await svc.RemoveParticipantAsync("room", "user", default));
        Assert.False(await svc.MutePublishedTrackAsync("room", "user", "TR_x", true, default));
        Assert.Equal(0, await svc.MuteAllMicrophonesAsync("room", null, default));
    }

    [Theory]
    // an explicit http ServerUrl wins and is trailing-slash normalised
    [InlineData("http://livekit:7880/", "ws://localhost:7880", "http://livekit:7880")]
    // no ServerUrl → derive from the browser ws(s) URL, preserving the scheme pairing
    [InlineData(null, "wss://x.livekit.cloud", "https://x.livekit.cloud")]
    [InlineData(null, "ws://localhost:7880", "http://localhost:7880")]
    [InlineData(null, null, null)]
    public void ServerUrl_normalisation(string? server, string? ws, string? expected)
        => Assert.Equal(expected, LiveKitRoomService.NormaliseServerUrl(server, ws));

    // ── which tracks a mute-all is allowed to touch ───────────────────────────

    [Fact]
    public void MuteAll_targets_microphones_and_spares_screen_share_audio()
    {
        // A mute-all that killed the presenter's shared-tab audio would be a regression
        // dressed as a feature, so source wins over media type when LiveKit reports it.
        Assert.True(LiveKitRoomService.IsMicrophone(new("TR_a", "MICROPHONE", "AUDIO", false)));
        Assert.False(LiveKitRoomService.IsMicrophone(new("TR_b", "SCREEN_SHARE_AUDIO", "AUDIO", false)));
        Assert.False(LiveKitRoomService.IsMicrophone(new("TR_c", "CAMERA", "VIDEO", false)));
        // …and when source is absent (older payload / proxy), fall back to media type
        // rather than silently muting nothing.
        Assert.True(LiveKitRoomService.IsMicrophone(new("TR_d", "", "AUDIO", false)));
        Assert.False(LiveKitRoomService.IsMicrophone(new("TR_e", "", "VIDEO", false)));
    }

    // ── ListParticipants response parsing ─────────────────────────────────────

    [Fact]
    public void ParseParticipants_reads_camelCase_protobuf_json()
    {
        var json = """
        {"participants":[
          {"sid":"PA_1","identity":"11111111-1111-1111-1111-111111111111","name":"Host",
           "tracks":[{"sid":"TR_1","type":"AUDIO","source":"MICROPHONE","muted":false},
                     {"sid":"TR_2","type":"VIDEO","source":"CAMERA","muted":false}]},
          {"sid":"PA_2","identity":"22222222-2222-2222-2222-222222222222","name":"Guest","tracks":[]}
        ]}
        """;
        var parsed = LiveKitRoomService.ParseParticipants(JsonDocument.Parse(json).RootElement);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("11111111-1111-1111-1111-111111111111", parsed[0].Identity);
        Assert.Equal("Host", parsed[0].Name);
        Assert.Equal(2, parsed[0].Tracks.Count);
        Assert.Equal("TR_1", parsed[0].Tracks[0].Sid);
        Assert.True(LiveKitRoomService.IsMicrophone(parsed[0].Tracks[0]));
        Assert.Empty(parsed[1].Tracks);
    }

    [Fact]
    public void ParseParticipants_tolerates_snake_case_and_reads_muted()
    {
        // A track already muted must be recognised so mute-all stays idempotent
        // (it skips them) rather than re-muting on every click.
        var json = """
        {"participants":[{"sid":"PA_1","identity":"u1",
          "tracks":[{"sid":"TR_1","type":"AUDIO","source":"MICROPHONE","muted":true}]}]}
        """;
        var parsed = LiveKitRoomService.ParseParticipants(JsonDocument.Parse(json).RootElement);
        Assert.True(parsed[0].Tracks[0].Muted);
    }

    [Theory]
    [InlineData("""{}""")]                                        // no participants key
    [InlineData("""{"participants":null}""")]                      // null array
    [InlineData("""{"participants":[]}""")]                        // empty room
    [InlineData("""{"participants":[{"sid":"PA_1"}]}""")]           // participant with no identity
    public void ParseParticipants_never_throws_on_degenerate_payloads(string json)
    {
        var parsed = LiveKitRoomService.ParseParticipants(JsonDocument.Parse(json).RootElement);
        Assert.Empty(parsed);   // an identity-less participant can't be muted or evicted
    }

    [Fact]
    public void ParseParticipants_drops_tracks_without_a_sid()
    {
        // MutePublishedTrack is keyed by sid; a sid-less track is unaddressable.
        var json = """{"participants":[{"identity":"u1","tracks":[{"type":"AUDIO","source":"MICROPHONE"}]}]}""";
        var parsed = LiveKitRoomService.ParseParticipants(JsonDocument.Parse(json).RootElement);
        Assert.Single(parsed);
        Assert.Empty(parsed[0].Tracks);
    }

    // ── the admin grant ───────────────────────────────────────────────────────

    [Fact]
    public void RoomAdminJwt_is_room_scoped_HS256()
    {
        // roomAdmin without a matching `room` claim is refused by LiveKit, which is
        // exactly the silent-401 failure mode this test exists to prevent.
        var jwt = LiveKitRoomService.MintRoomAdminJwt("devkey", "secret", "room-abc");
        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);

        var header = JsonDocument.Parse(B64Url(parts[0])).RootElement;
        Assert.Equal("HS256", header.GetProperty("alg").GetString());

        var payload = JsonDocument.Parse(B64Url(parts[1])).RootElement;
        Assert.Equal("devkey", payload.GetProperty("iss").GetString());
        var video = payload.GetProperty("video");
        Assert.True(video.GetProperty("roomAdmin").GetBoolean());
        Assert.Equal("room-abc", video.GetProperty("room").GetString());
        Assert.True(payload.GetProperty("exp").GetInt64() > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    [Fact]
    public void RoomAdminJwt_signs_a_short_secret()
    {
        // The whole reason LiveKitTokenFactory hand-rolls HMAC: Microsoft's JWT stack
        // rejects keys under 128 bits, and LiveKit's dev secret is 6 bytes.
        var jwt = LiveKitRoomService.MintRoomAdminJwt("devkey", "secret", "r");
        Assert.NotEmpty(jwt.Split('.')[2]);
    }

    private static string B64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    // ── integration: a real LiveKit accepts the grant + the Twirp route ────────

    /// <summary>
    /// Proves the grant and route are right against the running docker-compose LiveKit
    /// (devkey/secret on :7880). A wrong grant returns 401 and <c>ListParticipantsAsync</c>
    /// would swallow it as "empty" — so this asserts on the RAW Twirp response, which is
    /// the only way to tell "authenticated, room is empty" from "rejected".
    /// </summary>
    [SkippableFact]
    public async Task Real_LiveKit_accepts_the_room_admin_grant()
    {
        const string url = "http://localhost:7880";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        try { using var ping = await http.GetAsync(url); }
        catch (Exception e) { Skip.If(true, $"LiveKit not reachable at {url} ({e.GetType().Name}) — start docker compose to run this."); }

        var room = "planscape-test-" + Guid.NewGuid().ToString("N")[..8];
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{url}/twirp/livekit.RoomService/ListParticipants")
        {
            Content = new StringContent($$"""{"room":"{{room}}"}""", Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", LiveKitRoomService.MintRoomAdminJwt("devkey", "secret", room));

        using var resp = await http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();

        // 200 = grant accepted (an empty/absent room lists zero participants, which is fine).
        // 401 = the JWT or its grant is wrong — the failure this test exists to catch.
        Assert.True(resp.IsSuccessStatusCode, $"LiveKit rejected the room-admin grant: {(int)resp.StatusCode} {body}");
        Assert.Empty(LiveKitRoomService.ParseParticipants(JsonDocument.Parse(body).RootElement));

        // And the same call through the service under test returns cleanly.
        Assert.Empty(await Configured(url).ListParticipantsAsync(room, default));
    }
}
