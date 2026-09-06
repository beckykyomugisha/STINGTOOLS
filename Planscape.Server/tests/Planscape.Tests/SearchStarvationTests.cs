using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Tests;

/// <summary>
/// Guards the bug /api/search shipped with: a global search that could not
/// return a document.
///
/// The controller fetched up to `limit` rows PER TYPE, concatenated them
/// tag → issue → document → meeting, then took the first `limit` of that flat
/// list. Any type that filled the budget therefore starved every type after it:
/// with 25+ matching tags and the default limit of 25, the document, issue and
/// meeting rows were fetched from the database and then silently discarded.
///
/// Each sub-query was also unordered, so which rows survived was whatever
/// Postgres felt like returning that call — the source of EF's
/// RowLimitingOperationWithoutOrderByWarning in the production logs, and the
/// reason two identical searches could disagree.
///
/// Requires real PostgreSQL: SearchController uses EF.Functions.ILike, an
/// Npgsql-only translation that client-evaluates (and 500s) on EF InMemory.
/// Set PLANSCAPE_TEST_PG to run these.
/// </summary>
public class SearchStarvationTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;
    public SearchStarvationTests(PlanscapeWebApplicationFactory factory) => _factory = factory;

    // The class fixture is ONE database shared by all three tests, so each test
    // seeds under its own search term and its own RevitElementId block:
    // the term keeps one test's rows out of another's result set (the count
    // assertions below are exact), and the id block avoids the unique index on
    // (ProjectId, RevitElementId).
    private const string TermStarve = "zzstarveterm";
    private const string TermStable = "zzstableterm";
    private const string TermShare  = "zzshareterm";

    /// <summary>
    /// Seed enough matching tags to exhaust the limit on their own, plus exactly
    /// one matching document — the row the old code could never surface.
    /// </summary>
    private async Task SeedAsync(string term, int idBase, int tagCount)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;

        for (var i = 0; i < tagCount; i++)
        {
            db.TaggedElements.Add(new TaggedElement
            {
                TenantId = TestData.TenantId,
                ProjectId = TestData.ProjectId,
                RevitElementId = idBase + i,
                UniqueId = $"{term}-uid-{i}",
                Tag1 = $"M-{term}-{i:D4}",
                CategoryName = "Ducts",
                // Distinct timestamps so "newest first" is a total order and the
                // assertions can't pass by luck.
                SyncedAt = DateTime.UtcNow.AddMinutes(-i),
            });
        }

        db.Documents.Add(new DocumentRecord
        {
            TenantId = TestData.TenantId,
            ProjectId = TestData.ProjectId,
            FileName = $"{term}-spec.pdf",
            DocumentType = "SP",
            UploadedBy = "seed",
            UploadedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    private static List<JsonElement> ResultsOf(JsonElement body) =>
        body.GetProperty("results").EnumerateArray().ToList();

    [SkippableFact]
    public async Task ATypeThatFillsTheLimitDoesNotStarveTheOthers()
    {
        Skip.IfNot(PlanscapeWebApplicationFactory.UsingPostgres,
            "PLANSCAPE_TEST_PG is not set — needs the real-PostgreSQL harness.");

        await SeedAsync(TermStarve, idBase: 900_000, tagCount: 40);   // more tags than the limit
        var client = await _factory.CreateAuthenticatedClientAsync();

        var resp = await client.GetAsync($"/api/search?q={TermStarve}&limit=25");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var results = ResultsOf(await resp.Content.ReadFromJsonAsync<JsonElement>());
        var types = results.Select(r => r.GetProperty("type").GetString()).ToList();

        // THE regression: the single matching document must come back even
        // though 40 tags matched. Before the fix this was always false.
        Assert.Contains("document", types);
        Assert.Contains("tag", types);
        // The budget is still honoured — the fix shares it, it doesn't ignore it.
        Assert.Equal(25, results.Count);
    }

    [SkippableFact]
    public async Task RepeatedIdenticalSearchesReturnTheSameRows()
    {
        Skip.IfNot(PlanscapeWebApplicationFactory.UsingPostgres,
            "PLANSCAPE_TEST_PG is not set — needs the real-PostgreSQL harness.");

        await SeedAsync(TermStable, idBase: 910_000, tagCount: 40);
        var client = await _factory.CreateAuthenticatedClientAsync();

        async Task<List<string?>> IdsAsync()
        {
            var resp = await client.GetAsync($"/api/search?q={TermStable}&limit=10");
            var results = ResultsOf(await resp.Content.ReadFromJsonAsync<JsonElement>());
            return results.Select(r => r.GetProperty("id").GetString()).ToList();
        }

        // Unordered Take(limit) let Postgres pick an arbitrary subset, so the
        // same query could answer differently call to call. Ordering makes the
        // result a function of the data.
        Assert.Equal(await IdsAsync(), await IdsAsync());
    }

    [SkippableFact]
    public async Task ATypeWithNoMatchesDonatesItsShare()
    {
        Skip.IfNot(PlanscapeWebApplicationFactory.UsingPostgres,
            "PLANSCAPE_TEST_PG is not set — needs the real-PostgreSQL harness.");

        // 40 tags + 1 document, nothing else matches. A naive "limit/4 each"
        // split would return 11; the round-robin must fill the budget from the
        // types that actually have rows.
        await SeedAsync(TermShare, idBase: 920_000, tagCount: 40);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var resp = await client.GetAsync($"/api/search?q={TermShare}&limit=25");
        var results = ResultsOf(await resp.Content.ReadFromJsonAsync<JsonElement>());

        Assert.Equal(25, results.Count);
        Assert.Equal(24, results.Count(r => r.GetProperty("type").GetString() == "tag"));
    }
}
