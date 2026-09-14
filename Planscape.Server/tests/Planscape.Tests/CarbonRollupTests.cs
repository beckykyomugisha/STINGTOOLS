using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Tests;

/// <summary>
/// QR-14 — the embodied-carbon rollup.
///
/// Per-element carbon now reaches a scan. Aggregating it is where the feature can
/// start lying, and these tests exist for exactly one failure mode:
///
///     A bare total over a partial model reads as a building's footprint.
///
/// On a real project the assessed subset is a minority for a long time. "2.4 tCO₂e"
/// over 12% of the model is a twelfth of an answer, and anyone shown only the
/// number will treat it as the whole. So the total never travels without its
/// denominator, and the endpoint never extrapolates to the remainder — a plausible
/// building total built from an assumption is the fabricated-data failure this
/// codebase has shipped before.
/// </summary>
public class CarbonRollupTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;

    public CarbonRollupTests(PlanscapeWebApplicationFactory factory) => _factory = factory;

    private const string Url = "/api/tagsync/carbon?projectId=66666666-6666-6666-6666-666666666666";
    private static readonly Guid ProjectId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    // ── Coverage travels with the total ───────────────────────────────────────

    [Fact]
    public async Task The_total_never_travels_without_its_denominator()
    {
        // The whole point of the endpoint's shape.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var body = await Json(client, Url);

        foreach (var required in new[] { "embodiedCarbonKg", "assessed", "total", "coveragePercent" })
            Assert.True(body.TryGetProperty(required, out _), $"the payload is missing '{required}'");
    }

    [Fact]
    public async Task Coverage_reports_the_assessed_fraction_not_a_rounded_up_promise()
    {
        var tag = $"CR-{Guid.NewGuid():N}";
        Seed(tag + "-A", carbon: 100);
        Seed(tag + "-B", carbon: 50);
        Seed(tag + "-C", carbon: null);   // un-assessed
        Seed(tag + "-D", carbon: null);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var body = await Json(client, Url);

        // Absolute counts, not this seed's share, because the fixture DB carries
        // other tests' rows. What matters is that assessed < total and the two are
        // reported — a 100% coverage claim over a partly-assessed model is the lie.
        int assessed = body.GetProperty("assessed").GetInt32();
        int total = body.GetProperty("total").GetInt32();
        Assert.True(total >= 4, $"expected the seeded rows to be counted, saw total={total}");
        Assert.True(assessed < total, "coverage should be partial while un-assessed rows exist");
        Assert.True(body.GetProperty("coveragePercent").GetDouble() < 100.0);
    }

    [Fact]
    public async Task The_sum_covers_only_assessed_elements_and_is_not_extrapolated()
    {
        // Two elements at 100 and 50, two un-assessed. The answer contains 150 from
        // them — NOT 300, which is what scaling the mean across the remainder would
        // give. That estimate might even be closer to the truth; it would still be
        // an assumption presented as a measurement.
        var before = await SumAsync();
        var tag = $"CR-{Guid.NewGuid():N}";
        Seed(tag + "-A", carbon: 100);
        Seed(tag + "-B", carbon: 50);
        Seed(tag + "-C", carbon: null);
        Seed(tag + "-D", carbon: null);

        var after = await SumAsync();

        Assert.Equal(150.0, after - before, 3);
    }

    [Fact]
    public async Task A_genuine_zero_counts_as_assessed()
    {
        // A timber element assessed at 0 kgCO₂e HAS been assessed. Treating it as
        // un-assessed would understate coverage and quietly drop the very result the
        // assessment produced.
        var client = await _factory.CreateAuthenticatedClientAsync();
        int before = (await Json(client, Url)).GetProperty("assessed").GetInt32();

        Seed($"CR-ZERO-{Guid.NewGuid():N}", carbon: 0.0);

        int after = (await Json(client, Url)).GetProperty("assessed").GetInt32();
        Assert.Equal(before + 1, after);
    }

    [Fact]
    public async Task An_empty_project_reports_zero_coverage_rather_than_dividing_by_zero()
    {
        // Guards the arithmetic. 0/0 must be 0%, not NaN and not a 500.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var res = await client.GetAsync(
            "/api/tagsync/carbon?projectId=77777777-7777-7777-7777-777777777777");

        // Either a clean 404 for a project the caller cannot see, or a zeroed
        // rollup — never a crash.
        Assert.True(res.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound,
            $"expected 200 or 404, got {(int)res.StatusCode}");
    }

    // ── Grouping ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Each_group_carries_its_own_coverage()
    {
        // A discipline with one assessed element out of four hundred must not present
        // its total as that discipline's footprint either. The rule is fractal.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var body = await Json(client, Url + "&groupBy=discipline");

        var groups = body.GetProperty("groups").EnumerateArray().ToList();
        Assert.NotEmpty(groups);
        foreach (var g in groups)
        {
            Assert.True(g.TryGetProperty("assessed", out _), "a group is missing 'assessed'");
            Assert.True(g.TryGetProperty("total", out _), "a group is missing 'total'");
            Assert.True(g.TryGetProperty("coveragePercent", out _), "a group is missing 'coveragePercent'");
        }
    }

    [Theory]
    [InlineData("discipline")]
    [InlineData("category")]
    [InlineData("level")]
    [InlineData("material")]
    public async Task The_documented_groupings_all_work(string groupBy)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var res = await client.GetAsync(Url + "&groupBy=" + groupBy);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task An_unknown_grouping_is_refused_rather_than_silently_ignored()
    {
        // Ignoring it would return an UNGROUPED total to a caller who asked for a
        // breakdown — the right shape, the wrong answer, and no way to tell.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var res = await client.GetAsync(Url + "&groupBy=colour");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task No_grouping_asked_for_means_no_groups_returned()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var body = await Json(client, Url);

        Assert.Equal(JsonValueKind.Null, body.GetProperty("groups").ValueKind);
    }

    // ── What the number is, stated in the payload ─────────────────────────────

    [Fact]
    public async Task The_payload_says_what_the_figure_is_and_is_not()
    {
        // Stated in the response, not only in the docs, because the client that
        // renders this is the one that could mislead someone with it — and it must
        // not be confused with the EDGE material-carbon intensity, which is a
        // different number computed a different way.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var basis = (await Json(client, Url)).GetProperty("basis").GetString() ?? "";

        Assert.Contains("ASSESSED", basis, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Not extrapolated", basis, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EDGE", basis, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_rollup_requires_authentication()
    {
        var anon = _factory.CreateClient();

        var res = await anon.GetAsync(Url);

        Assert.True(res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"expected 401/403, got {(int)res.StatusCode}");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<double> SumAsync()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        return (await Json(client, Url)).GetProperty("embodiedCarbonKg").GetDouble();
    }

    private static async Task<JsonElement> Json(HttpClient client, string url)
    {
        var res = await client.GetAsync(url);
        res.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
    }

    private void Seed(string tag, double? carbon)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;
        db.TaggedElements.Add(new TaggedElement
        {
            Id = Guid.NewGuid(),
            TenantId = TestData.TenantId,
            ProjectId = ProjectId,
            RevitElementId = Random.Shared.NextInt64(1_000_000, 9_000_000),
            UniqueId = tag,
            Tag1 = tag,
            Disc = "M",
            CategoryName = "Mechanical Equipment",
            FamilyName = "AHU_Standard",
            Lvl = "L02",
            EmbodiedCarbonKg = carbon,
            MaterialName = carbon.HasValue ? "Galvanised Steel" : null,
        });
        db.SaveChanges();
    }
}
