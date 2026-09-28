using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Planscape.Tests;

/// <summary>
/// IM-11 / IM-12: a pushed warning report is a point on the warnings trend, a clean
/// model (zero warnings) stays on it, and none of this leaks into the compliance charts.
/// Each test makes its own project so the assertions run against known, non-empty data.
/// </summary>
public class WarningsTrendTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;
    public WarningsTrendTests(PlanscapeWebApplicationFactory factory) => _factory = factory;

    private static async Task<string> NewProjectAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "Warnings trend " + Guid.NewGuid().ToString("N")[..6],
            code = "WT-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> TrendAsync(HttpClient client, string projectId)
    {
        var resp = await client.GetAsync($"/api/projects/{projectId}/warnings/trend?days=7");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await resp.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task A_pushed_report_appears_on_the_trend_even_at_zero_warnings()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        string id = await NewProjectAsync(client);

        var push = await client.PostAsJsonAsync($"/api/projects/{id}/warnings/report",
            new { totalWarnings = 12, healthScore = 70 });
        Assert.Equal(HttpStatusCode.Created, push.StatusCode);
        push = await client.PostAsJsonAsync($"/api/projects/{id}/warnings/report",
            new { totalWarnings = 0, healthScore = 100 });
        Assert.Equal(HttpStatusCode.Created, push.StatusCode);

        var trend = await TrendAsync(client, id);
        Assert.Equal(2, trend.GetArrayLength());
        Assert.Equal(12, trend[0].GetProperty("warningCount").GetInt32());
        Assert.Equal(0, trend[1].GetProperty("warningCount").GetInt32());
    }

    [Fact]
    public async Task An_unchanged_report_is_not_stored_twice()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        string id = await NewProjectAsync(client);

        for (int i = 0; i < 3; i++)
            await client.PostAsJsonAsync($"/api/projects/{id}/warnings/report",
                new { totalWarnings = 5, healthScore = 88 });

        Assert.Equal(1, (await TrendAsync(client, id)).GetArrayLength());
    }

    [Fact]
    public async Task A_warning_report_is_not_a_compliance_snapshot()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        string id = await NewProjectAsync(client);

        await client.PostAsJsonAsync($"/api/projects/{id}/warnings/report",
            new { totalWarnings = 3, healthScore = 90 });

        // Compliance was never measured on this project, so there is still no compliance data.
        var latest = await client.GetAsync($"/api/projects/{id}/compliance/latest");
        Assert.Equal(HttpStatusCode.NotFound, latest.StatusCode);
        var ctrend = await client.GetAsync($"/api/projects/{id}/compliance/trend?days=7");
        Assert.Equal(HttpStatusCode.OK, ctrend.StatusCode);
        Assert.Equal(0, (await ctrend.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
    }
}
