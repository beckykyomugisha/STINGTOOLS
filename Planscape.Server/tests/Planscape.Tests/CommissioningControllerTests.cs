using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Planscape.Tests;

/// <summary>
/// The commissioning API — what a scanned QR on a phone now POSTs to.
///
/// Before this existed, "QR commissioning" had no scan path at all: the desktop
/// command read the Revit selection, and this server had no commissioning
/// controller and no commissioning entity, so a phone had nothing to call.
///
/// The state-machine rules are tested once in CommissioningStateMachineTests
/// against the shared copy. What is tested HERE is what the HTTP layer adds and
/// could get wrong on its own: that a refusal reaches the client as something it
/// can branch on, that history is append-only, and that two people scanning the
/// same asset cannot both record the same step.
/// </summary>
public class CommissioningControllerTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;

    public CommissioningControllerTests(PlanscapeWebApplicationFactory factory) => _factory = factory;

    private const string Base = "/api/projects/66666666-6666-6666-6666-666666666666/commissioning";

    private static string NewElement() => $"{Guid.NewGuid()}-0000abcd";

    private static object Step(string element, string operative, string? witness = null,
                               string? expected = null, string? target = null) => new
    {
        elementUniqueId = element,
        operative,
        witness,
        expectedCurrentState = expected,
        requestedState = target,
        source = "mobile-scan",
        elementTag = "M-BLD1-Z01-L02-HVAC-SUP-AHU-0003",
    };

    // ── Reading an element nobody has touched ─────────────────────────────────

    [Fact]
    public async Task An_element_with_no_history_is_NOT_STARTED_not_a_404()
    {
        // "Never commissioned" and "no such element" are different answers, and only
        // one of them is a problem. A 404 here would make the phone show an error for
        // the entirely normal first scan of a new asset.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var res = await client.GetAsync($"{Base}/{NewElement()}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("NOT_STARTED", body.GetProperty("currentState").GetString());
        Assert.Equal("RECEIVED", body.GetProperty("nextState").GetString());
        Assert.Empty(body.GetProperty("history").EnumerateArray());
        Assert.False(body.GetProperty("isTerminal").GetBoolean());
    }

    // ── Advancing ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_first_scan_records_a_step_and_reports_the_new_state()
    {
        var element = NewElement();
        var client = await _factory.CreateAuthenticatedClientAsync();

        var res = await client.PostAsJsonAsync($"{Base}/advance", Step(element, "A. Fitter"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("RECEIVED", body.GetProperty("currentState").GetString());
        Assert.Equal("INSTALLED", body.GetProperty("nextState").GetString());
        Assert.Equal("NOT_STARTED", body.GetProperty("record").GetProperty("fromState").GetString());
        Assert.Equal("mobile-scan", body.GetProperty("record").GetProperty("source").GetString());
    }

    [Fact]
    public async Task History_is_append_only_and_keeps_every_operative()
    {
        // The reason this table is append-only at all: a single mutable row would
        // answer "what state is it in" and destroy "who signed it off and when",
        // which is the half that gets asked in a dispute years later.
        var element = NewElement();
        var client = await _factory.CreateAuthenticatedClientAsync();

        await client.PostAsJsonAsync($"{Base}/advance", Step(element, "A. Fitter"));
        await client.PostAsJsonAsync($"{Base}/advance", Step(element, "B. Fitter"));
        await client.PostAsJsonAsync($"{Base}/advance", Step(element, "C. Tester"));

        var body = await Json(await client.GetAsync($"{Base}/{element}"));
        var history = body.GetProperty("history").EnumerateArray().ToList();

        Assert.Equal(3, history.Count);
        Assert.Equal("TESTED", body.GetProperty("currentState").GetString());
        Assert.Equal(
            new[] { "A. Fitter", "B. Fitter", "C. Tester" },
            history.Select(h => h.GetProperty("operative").GetString()).ToArray());
        // Oldest first, so the ladder reads top to bottom.
        Assert.Equal("NOT_STARTED", history[0].GetProperty("fromState").GetString());
        Assert.Equal("TESTED", history[2].GetProperty("toState").GetString());
    }

    // ── Refusals reach the client in a usable shape ───────────────────────────

    [Fact]
    public async Task A_refusal_carries_an_enum_to_branch_on_not_just_a_sentence()
    {
        // A client matching on message text stops working the moment someone rewords
        // it — and nothing would fail, it would just stop recognising the case.
        var element = NewElement();
        var client = await _factory.CreateAuthenticatedClientAsync();
        await client.PostAsJsonAsync($"{Base}/advance", Step(element, "A. Fitter")); // -> RECEIVED

        var res = await client.PostAsJsonAsync($"{Base}/advance",
            Step(element, "A. Fitter", target: "TESTED"));   // skips INSTALLED

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("transition_refused", body.GetProperty("error").GetString());
        Assert.Equal("SkippedState", body.GetProperty("refusal").GetString());
        Assert.Equal("RECEIVED", body.GetProperty("currentState").GetString());
    }

    [Fact]
    public async Task A_missing_witness_at_COMMISSIONED_is_refused_by_the_api_too()
    {
        // The rule lives in the shared state machine; this proves the API actually
        // calls it rather than having its own, more permissive, copy — which is the
        // whole reason the machine was moved to Planscape.Shared.
        var element = NewElement();
        var client = await _factory.CreateAuthenticatedClientAsync();
        foreach (var who in new[] { "A", "B", "C" })
            await client.PostAsJsonAsync($"{Base}/advance", Step(element, who));   // -> TESTED

        var res = await client.PostAsJsonAsync($"{Base}/advance", Step(element, "D. Engineer"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("MissingWitness", (await Json(res)).GetProperty("refusal").GetString());

        var ok = await client.PostAsJsonAsync($"{Base}/advance",
            Step(element, "D. Engineer", witness: "E. Client Rep"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("COMMISSIONED", (await Json(ok)).GetProperty("currentState").GetString());
    }

    [Fact]
    public async Task An_unattributed_step_is_refused()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var res = await client.PostAsJsonAsync($"{Base}/advance",
            new { elementUniqueId = NewElement(), operative = "" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("MissingOperative", (await Json(res)).GetProperty("refusal").GetString());
    }

    [Fact]
    public async Task A_regression_is_refused_so_history_cannot_be_rewritten()
    {
        var element = NewElement();
        var client = await _factory.CreateAuthenticatedClientAsync();
        await client.PostAsJsonAsync($"{Base}/advance", Step(element, "A"));
        await client.PostAsJsonAsync($"{Base}/advance", Step(element, "B"));   // -> INSTALLED

        var res = await client.PostAsJsonAsync($"{Base}/advance",
            Step(element, "C", target: "RECEIVED"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("Regression", (await Json(res)).GetProperty("refusal").GetString());
        // And the record really is unchanged, not merely reported as such.
        var after = await Json(await client.GetAsync($"{Base}/{element}"));
        Assert.Equal("INSTALLED", after.GetProperty("currentState").GetString());
        Assert.Equal(2, after.GetProperty("history").GetArrayLength());
    }

    // ── Two people, one asset ─────────────────────────────────────────────────

    [Fact]
    public async Task A_stale_client_view_is_rejected_rather_than_double_recording()
    {
        // Two fitters scan the same asset seconds apart. Both see INSTALLED. Without
        // this, both advance and one step is recorded twice under two names, which is
        // a false record nothing downstream can detect.
        var element = NewElement();
        var client = await _factory.CreateAuthenticatedClientAsync();
        await client.PostAsJsonAsync($"{Base}/advance", Step(element, "A"));   // -> RECEIVED

        var first = await client.PostAsJsonAsync($"{Base}/advance",
            Step(element, "B", expected: "RECEIVED"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Second phone still believes RECEIVED.
        var second = await client.PostAsJsonAsync($"{Base}/advance",
            Step(element, "C", expected: "RECEIVED"));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await Json(second);
        Assert.Equal("state_moved", body.GetProperty("error").GetString());
        Assert.Equal("INSTALLED", body.GetProperty("currentState").GetString());
    }

    [Fact]
    public async Task Omitting_the_expected_state_keeps_the_old_permissive_behaviour()
    {
        // The concurrency check is opt-in: a client that does not send
        // expectedCurrentState must behave exactly as before, or adding the field
        // would break every existing caller.
        var element = NewElement();
        var client = await _factory.CreateAuthenticatedClientAsync();
        await client.PostAsJsonAsync($"{Base}/advance", Step(element, "A"));

        var res = await client.PostAsJsonAsync($"{Base}/advance", Step(element, "B"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    // ── Register ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_register_reports_the_current_state_per_element_not_every_step()
    {
        var element = NewElement();
        var client = await _factory.CreateAuthenticatedClientAsync();
        await client.PostAsJsonAsync($"{Base}/advance", Step(element, "A"));
        await client.PostAsJsonAsync($"{Base}/advance", Step(element, "B"));

        var body = await Json(await client.GetAsync(Base));
        var mine = body.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("elementUniqueId").GetString() == element)
            .ToList();

        Assert.Single(mine);   // two steps, one row in the register
        Assert.Equal("INSTALLED", mine[0].GetProperty("toState").GetString());
    }

    [Fact]
    public async Task The_register_counts_every_state_including_the_empty_ones()
    {
        // An absent bar and a zero bar mean different things: "nothing has reached
        // COMMISSIONED" is the answer a project manager needs, and a dashboard that
        // only renders present states cannot give it.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var byState = (await Json(await client.GetAsync(Base))).GetProperty("byState");

        foreach (var s in new[] { "NOT_STARTED", "RECEIVED", "INSTALLED", "TESTED", "COMMISSIONED", "HANDOVER" })
            Assert.True(byState.TryGetProperty(s, out _), $"byState is missing '{s}'");
    }

    // ── Shape and access ──────────────────────────────────────────────────────

    [Fact]
    public async Task The_states_endpoint_serves_the_shared_ladder()
    {
        // So a client never keeps its own copy of the list — the copy is what drifts,
        // and the user finds out by being rejected for a state the app offered.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var body = await Json(await client.GetAsync("/api/commissioning/states"));

        Assert.Equal(
            new[] { "NOT_STARTED", "RECEIVED", "INSTALLED", "TESTED", "COMMISSIONED", "HANDOVER" },
            body.GetProperty("states").EnumerateArray().Select(s => s.GetString()).ToArray());
        Assert.Equal("COMMISSIONED", body.GetProperty("witnessRequiredFor").GetString());
    }

    [Fact]
    public async Task Advancing_requires_authentication()
    {
        var anon = _factory.CreateClient();

        var res = await anon.PostAsJsonAsync($"{Base}/advance", Step(NewElement(), "A. Stranger"));

        Assert.True(res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"expected 401/403, got {(int)res.StatusCode}");
    }

    [Fact]
    public async Task Advancing_in_a_project_that_does_not_exist_is_a_404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var res = await client.PostAsJsonAsync(
            "/api/projects/00000000-0000-0000-0000-0000000000ff/commissioning/advance",
            Step(NewElement(), "A. Fitter"));

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
}
