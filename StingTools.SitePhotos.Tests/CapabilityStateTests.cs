using System;
using System.Text;
using System.Threading.Tasks;
using StingTools.BIMManager;
using Xunit;

namespace StingTools.SitePhotos.Tests;

/// <summary>
/// The three-state capability rule (#547 / #558 / #634), pinned.
///
/// WHY THESE EXIST. The BCC now disables controls from the server's answer to
/// GET members/capabilities. The entire correctness of that hinges on one
/// distinction that is invisible in the type system and easy to "tidy" away:
///
///     Denied   the server said no                 → disable, name the capability
///     Unknown  we never got an answer             → LEAVE IT ENABLED
///
/// Collapsing Unknown into Denied — "absence of an explicit true is false" —
/// locks legitimate users out of a working system whenever the network hiccups,
/// and shows them a permissions message while doing it. It is the same failure
/// as an empty list standing in for a failed load, one layer up: an absent
/// answer rendered as a definite one.
///
/// A 404 is the one exception and is asserted as such: it says the caller cannot
/// see the project at all, which IS an answer.
///
/// Every case below is driven through the real client against a real socket, so
/// what is pinned is the shipped behaviour and not a re-statement of the rule.
/// </summary>
public class CapabilityStateTests
{
    private static PlanscapeServerClient Client => PlanscapeServerClient.Instance;

    private static async Task<CaptureServer> AuthedServerAsync()
    {
        var srv = new CaptureServer();
        var ok = await Client.LoginAsync(srv.BaseUrl, "harness@test", "pw");
        Assert.True(ok, $"harness login failed against the capture server: {Client.LastError}");
        return srv;
    }

    private static void ServeCapabilities(CaptureServer srv, int status, string body)
    {
        srv.Routes.Add((p => p.EndsWith("/members/capabilities", StringComparison.OrdinalIgnoreCase),
            _ => (status, "application/json", Encoding.UTF8.GetBytes(body))));
    }

    // ── The happy path, both directions ──────────────────────────────────

    [Fact]
    public async Task Explicit_true_is_Allowed_and_explicit_false_is_Denied()
    {
        using var srv = await AuthedServerAsync();
        ServeCapabilities(srv, 200,
            "{\"canCurateProject\":true,\"canApproveSitePhotos\":false}");

        var caps = await Client.GetProjectCapabilitiesAsync(Guid.NewGuid());

        Assert.Equal(CapabilityState.Allowed, caps.CurateProject);
        Assert.Equal(CapabilityState.Denied, caps.ApproveSitePhotos);
    }

    // ── 404 is authoritative-false ───────────────────────────────────────

    [Fact]
    public async Task Not_found_denies_everything_because_the_project_is_invisible()
    {
        using var srv = await AuthedServerAsync();
        ServeCapabilities(srv, 404, "");

        var caps = await Client.GetProjectCapabilitiesAsync(Guid.NewGuid());

        // A 404 from this endpoint means the caller cannot see the project.
        // Nothing is possible on it, so Denied is the honest answer — this is
        // the ONE status that may narrow the UI.
        Assert.Equal(CapabilityState.Denied, caps.CurateProject);
        Assert.Equal(CapabilityState.Denied, caps.ApproveSitePhotos);
    }

    // ── Everything else is Unknown ───────────────────────────────────────

    [Fact]
    public async Task Server_down_is_Unknown_not_Denied()
    {
        var srv = await AuthedServerAsync();
        srv.Kill(); // session established, then the API goes away

        var caps = await Client.GetProjectCapabilitiesAsync(Guid.NewGuid());

        // The regression this file exists to catch. Denied here would disable
        // Approve / Reject for a user who has every right to them, and tell
        // them it was a permissions problem.
        Assert.Equal(CapabilityState.Unknown, caps.CurateProject);
        Assert.Equal(CapabilityState.Unknown, caps.ApproveSitePhotos);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(401)]
    public async Task Non_404_error_statuses_are_Unknown(int status)
    {
        using var srv = await AuthedServerAsync();
        ServeCapabilities(srv, status, "{\"error\":\"boom\"}");

        var caps = await Client.GetProjectCapabilitiesAsync(Guid.NewGuid());

        Assert.Equal(CapabilityState.Unknown, caps.CurateProject);
        Assert.Equal(CapabilityState.Unknown, caps.ApproveSitePhotos);
    }

    [Fact]
    public async Task Unparseable_body_is_Unknown()
    {
        using var srv = await AuthedServerAsync();
        ServeCapabilities(srv, 200, "<html>proxy error</html>");

        var caps = await Client.GetProjectCapabilitiesAsync(Guid.NewGuid());

        Assert.Equal(CapabilityState.Unknown, caps.CurateProject);
        Assert.Equal(CapabilityState.Unknown, caps.ApproveSitePhotos);
    }

    [Fact]
    public async Task Missing_field_is_Unknown_not_Denied()
    {
        using var srv = await AuthedServerAsync();
        // A 200 that carries one flag and not the other — a shape change, or a
        // partial rollout. The field that IS there is honoured; the one that is
        // not stays unknown rather than silently disabling half the pane.
        ServeCapabilities(srv, 200, "{\"canCurateProject\":true}");

        var caps = await Client.GetProjectCapabilitiesAsync(Guid.NewGuid());

        Assert.Equal(CapabilityState.Allowed, caps.CurateProject);
        Assert.Equal(CapabilityState.Unknown, caps.ApproveSitePhotos);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"true\"")]   // string, not boolean
    [InlineData("1")]          // number, not boolean
    public async Task Non_boolean_values_are_Unknown(string literal)
    {
        using var srv = await AuthedServerAsync();
        ServeCapabilities(srv, 200,
            "{\"canCurateProject\":" + literal + ",\"canApproveSitePhotos\":" + literal + "}");

        var caps = await Client.GetProjectCapabilitiesAsync(Guid.NewGuid());

        // "true" the STRING is not true the BOOLEAN. Coercing it would mean a
        // contract drift silently granted a capability; reading it as false
        // would silently remove one. Neither is an answer we were given.
        Assert.Equal(CapabilityState.Unknown, caps.CurateProject);
        Assert.Equal(CapabilityState.Unknown, caps.ApproveSitePhotos);
    }

    // ── LastStatus, which the forbidden-vs-failure split depends on ──────

    [Fact]
    public async Task LastStatus_carries_403_so_a_refusal_is_not_reported_as_an_outage()
    {
        using var srv = await AuthedServerAsync();
        srv.Routes.Add((p => p.Contains("/photo-albums", StringComparison.OrdinalIgnoreCase),
            _ => (403, "application/json", Encoding.UTF8.GetBytes(""))));

        var albums = await Client.ListPhotoAlbumsAsync(Guid.NewGuid());

        Assert.Null(albums);              // still the failure channel
        Assert.Equal(403, Client.LastStatus);
    }

    [Fact]
    public async Task LastStatus_is_null_when_there_was_no_response_at_all()
    {
        var srv = await AuthedServerAsync();
        srv.Kill();

        await Client.ListPhotoAlbumsAsync(Guid.NewGuid());

        // Null, not 0 and not a stale 200 from the login that preceded it.
        // The UI branches on == 403; a stale value would misroute the report.
        Assert.Null(Client.LastStatus);
    }

    // ── The list that used to lie ────────────────────────────────────────

    [Fact]
    public async Task ListSitePhotos_returns_null_on_failure_not_an_empty_list()
    {
        var srv = await AuthedServerAsync();
        srv.Kill();

        var photos = await Client.ListSitePhotosAsync(Guid.NewGuid());

        // Until #558 this returned `new List<SitePhotoDto>()` on every failure
        // path, and the BCC review queue rendered "✓ No photos awaiting review."
        // over an unreachable server. Same fabrication #550 removed elsewhere.
        Assert.Null(photos);
        Assert.False(string.IsNullOrWhiteSpace(Client.LastError));
    }

    [Fact]
    public async Task ListSitePhotos_still_returns_an_empty_list_when_the_project_really_has_none()
    {
        using var srv = await AuthedServerAsync();
        srv.Routes.Add((p => p.Contains("/photos", StringComparison.OrdinalIgnoreCase),
            _ => (200, "application/json",
                  Encoding.UTF8.GetBytes("{\"items\":[],\"total\":0,\"page\":1,\"pageSize\":50}"))));

        var photos = await Client.ListSitePhotosAsync(Guid.NewGuid());

        // The other half of the distinction: empty must stay empty, or the fix
        // would have replaced one wrong answer with another.
        Assert.NotNull(photos);
        Assert.Empty(photos);
    }
}
