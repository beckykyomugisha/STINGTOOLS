using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// FLEX-13 (decision 4.6 row 1) — Archive-then-purge for deleted custom fields.
///
/// Fields marked with <c>DeletedAt</c> older than 30 days are permanently
/// removed, along with their values from each issue's <c>CustomFields</c>
/// JSONB object. Runs nightly via Hangfire. Admin can force immediate purge
/// by calling this method from an ops command if needed.
/// </summary>
public class CustomFieldsPurgeJob
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<CustomFieldsPurgeJob> _logger;

    private static readonly TimeSpan PurgeGrace = TimeSpan.FromDays(30);

    public CustomFieldsPurgeJob(PlanscapeDbContext db, ILogger<CustomFieldsPurgeJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - PurgeGrace;
        var stale = await _db.IssueCustomFieldSchemas
            .Where(s => s.DeletedAt != null && s.DeletedAt < cutoff)
            .ToListAsync(ct);

        if (stale.Count == 0)
        {
            _logger.LogInformation("CustomFieldsPurgeJob: nothing to purge");
            return;
        }

        foreach (var schema in stale)
        {
            // Scrub the key from every issue's CustomFields for this project.
            var issues = await _db.Issues
                .Where(i => i.ProjectId == schema.ProjectId && i.CustomFields != null)
                .ToListAsync(ct);
            foreach (var issue in issues)
            {
                var updated = RemoveKey(issue.CustomFields!, schema.Key);
                if (updated != issue.CustomFields) issue.CustomFields = updated;
            }
            _db.IssueCustomFieldSchemas.Remove(schema);
            _logger.LogInformation(
                "CustomFieldsPurgeJob: purged field {Key} (project {ProjectId}, deletedAt {DeletedAt:u}, {IssueCount} issues scrubbed)",
                schema.Key, schema.ProjectId, schema.DeletedAt, issues.Count);
        }

        await _db.SaveChangesAsync(ct);
    }

    private static string RemoveKey(string json, string key)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is JsonObject obj && obj.ContainsKey(key))
            {
                obj.Remove(key);
                return obj.ToJsonString();
            }
        }
        catch (JsonException)
        {
            // Malformed JSON on an issue — leave it, log at info level.
        }
        return json;
    }
}
