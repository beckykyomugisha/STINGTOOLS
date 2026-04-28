using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

namespace Planscape.API.Controllers;

/// <summary>
/// Compliance snapshot management — stores and retrieves compliance history for trend analysis.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/compliance")]
[Authorize]
[EnableRateLimiting("mobile")]
public class ComplianceController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IHubContext<NotificationHub> _hub;

    public ComplianceController(PlanscapeDbContext db, IHubContext<NotificationHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    /// <summary>
    /// Push a compliance snapshot from the Revit plugin.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult> PushSnapshot(Guid projectId, [FromBody] PushComplianceRequest req)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");

        var snapshot = new ComplianceSnapshot
        {
            ProjectId = projectId,
            CapturedBy = User.FindFirst("display_name")?.Value ?? "Unknown",
            TotalElements = req.TotalElements,
            TaggedComplete = req.TaggedComplete,
            TaggedIncomplete = req.TaggedIncomplete,
            Untagged = req.Untagged,
            FullyResolved = req.FullyResolved,
            StaleCount = req.StaleCount,
            PlaceholderCount = req.PlaceholderCount,
            WarningCount = req.WarningCount,
            WarningHealthScore = req.WarningHealthScore,
            TagPercent = req.TagPercent,
            StrictPercent = req.StrictPercent,
            ContainerPercent = req.ContainerPercent,
            RagStatus = req.RagStatus,
            ByDisciplineJson = req.ByDisciplineJson,
            ByPhaseJson = req.ByPhaseJson,
            EmptyTokenCountsJson = req.EmptyTokenCountsJson
        };

        _db.ComplianceSnapshots.Add(snapshot);

        // Update project cached metrics
        project.CompliancePercent = req.TagPercent;
        project.ContainerCompliancePercent = req.ContainerPercent;
        project.TotalElements = req.TotalElements;
        project.TaggedElements = req.TaggedComplete + req.TaggedIncomplete;
        project.WarningCount = req.WarningCount;
        project.RagStatus = req.RagStatus;

        var userId = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid) ? uid : (Guid?)null;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            ProjectId = projectId,
            UserId = userId,
            Action = "compliance_snapshot_pushed",
            EntityType = "ComplianceSnapshot",
            EntityId = snapshot.Id.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { req.TagPercent, req.StrictPercent, req.ContainerPercent, req.RagStatus, req.TotalElements }),
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // C2 — real-time push to everyone watching this project so Revit + mobile
        // dashboards refresh without polling.
        _ = _hub.Clients.Group($"project-{projectId}").SendAsync("ComplianceChanged", new
        {
            projectId,
            tagPercent       = req.TagPercent,
            strictPercent    = req.StrictPercent,
            containerPercent = req.ContainerPercent,
            ragStatus        = req.RagStatus,
            warningCount     = req.WarningCount,
            capturedAt       = snapshot.CapturedAt,
            capturedBy       = snapshot.CapturedBy,
        });

        return CreatedAtAction(nameof(GetLatest), new { projectId }, new { id = snapshot.Id, capturedAt = snapshot.CapturedAt });
    }

    /// <summary>
    /// Get the latest compliance snapshot (bare route for mobile compatibility).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult> GetCompliance(Guid projectId)
    {
        var tenantId = GetTenantId();
        var snapshot = await _db.ComplianceSnapshots
            .Where(s => s.ProjectId == projectId && s.Project!.TenantId == tenantId)
            .OrderByDescending(s => s.CapturedAt)
            .FirstOrDefaultAsync();

        if (snapshot == null) return NotFound("No compliance data yet");
        return Ok(snapshot);
    }

    /// <summary>
    /// Get the latest compliance snapshot.
    /// </summary>
    [HttpGet("latest")]
    public async Task<ActionResult> GetLatest(Guid projectId)
    {
        var tenantId = GetTenantId();
        var snapshot = await _db.ComplianceSnapshots
            .Where(s => s.ProjectId == projectId && s.Project!.TenantId == tenantId)
            .OrderByDescending(s => s.CapturedAt)
            .FirstOrDefaultAsync();

        if (snapshot == null) return NotFound("No compliance data yet");
        return Ok(snapshot);
    }

    /// <summary>
    /// Get compliance history for trend analysis.
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult> GetHistory(Guid projectId,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null,
        [FromQuery] int limit = 100)
    {
        var tenantId = GetTenantId();
        var query = _db.ComplianceSnapshots
            .Where(s => s.ProjectId == projectId && s.Project!.TenantId == tenantId);

        if (from.HasValue) query = query.Where(s => s.CapturedAt >= from.Value);
        if (to.HasValue) query = query.Where(s => s.CapturedAt <= to.Value);

        var snapshots = await query
            .OrderByDescending(s => s.CapturedAt)
            .Take(limit)
            .Select(s => new
            {
                s.CapturedAt, s.TagPercent, s.StrictPercent, s.ContainerPercent,
                s.RagStatus, s.TotalElements, s.StaleCount, s.WarningCount,
                s.WarningHealthScore, s.CapturedBy
            })
            .ToListAsync();

        return Ok(snapshots);
    }

    /// <summary>
    /// Get compliance trend (daily averages for charting).
    /// </summary>
    [HttpGet("trend")]
    public async Task<ActionResult> GetTrend(Guid projectId, [FromQuery] int days = 30)
    {
        var tenantId = GetTenantId();
        var since = DateTime.UtcNow.AddDays(-days);

        var trend = await _db.ComplianceSnapshots
            .Where(s => s.ProjectId == projectId && s.Project!.TenantId == tenantId && s.CapturedAt >= since)
            .GroupBy(s => s.CapturedAt.Date)
            .Select(g => new
            {
                Date = g.Key,
                AvgTagPercent = g.Average(s => s.TagPercent),
                AvgStrictPercent = g.Average(s => s.StrictPercent),
                AvgContainerPercent = g.Average(s => s.ContainerPercent),
                MaxWarnings = g.Max(s => s.WarningCount),
                Snapshots = g.Count()
            })
            .OrderBy(t => t.Date)
            .ToListAsync();

        return Ok(trend);
    }

    /// <summary>
    /// Phase 144 — cross-discipline tag completeness heatmap.
    ///
    /// Returns a matrix [discipline][token] = pct, where token is one of the
    /// 8 ISO 19650 tag segments (DISC / LOC / ZONE / LVL / SYS / FUNC / PROD
    /// / SEQ) plus STATUS + REV. Each cell is the percent of elements in
    /// that discipline whose token is non-empty.
    ///
    /// The BIM Coordinator uses this to spot which discipline is letting
    /// which token slip — e.g. "Mechanical is at 98% on SYS but 41% on
    /// FUNC" tells them where to point the team's effort.
    /// </summary>
    [HttpGet("tag-heatmap")]
    public async Task<ActionResult> GetTagHeatmap(Guid projectId, CancellationToken ct = default)
    {
        var tenantId = GetTenantId();
        var projectOk = await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId, ct);
        if (!projectOk) return NotFound("Project not found");

        // Phase 146 — provider-aware dispatch. PostgreSQL gets the raw-SQL
        // path with `COUNT(*) FILTER`; anything else (SQLite for tests,
        // SQL Server, in-memory) falls back to a LINQ aggregation that
        // matches the same response shape. Detection is one-shot off the
        // EF Core provider name.
        var providerName = _db.Database.ProviderName ?? "";
        var isPostgres = providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);
        if (!isPostgres)
        {
            return Ok(await ComputeHeatmapLinqAsync(projectId, ct));
        }

        // Phase 145 — aggregate in the DB with `count(*) FILTER (WHERE …)` so
        // the response time stays flat as the project scales. The previous
        // app-tier implementation was fine for prototype data sets but on a
        // 200k-element federated model it streamed ~4 MB of slim DTO rows
        // into the API process per call. Single SELECT, indexed on
        // (ProjectId, Disc) for the GROUP BY.
        //
        // The "(unset)" bucket is produced via COALESCE(NULLIF(...)) so the
        // query plan stays sortable on the bucket name. PG-only; the
        // `EnsureCreated` path is fine because every prod deployment runs
        // PostgreSQL.
        const string sql = @"
SELECT
  COALESCE(NULLIF(BTRIM(""Disc""), ''), '(unset)')                         AS discipline,
  COUNT(*)::int                                                            AS element_count,
  COUNT(*) FILTER (WHERE BTRIM(""Disc"")    <> '' AND ""Disc""   IS NOT NULL)::int AS disc_n,
  COUNT(*) FILTER (WHERE BTRIM(""Loc"")     <> '' AND ""Loc""    IS NOT NULL)::int AS loc_n,
  COUNT(*) FILTER (WHERE BTRIM(""Zone"")    <> '' AND ""Zone""   IS NOT NULL)::int AS zone_n,
  COUNT(*) FILTER (WHERE BTRIM(""Lvl"")     <> '' AND ""Lvl""    IS NOT NULL)::int AS lvl_n,
  COUNT(*) FILTER (WHERE BTRIM(""Sys"")     <> '' AND ""Sys""    IS NOT NULL)::int AS sys_n,
  COUNT(*) FILTER (WHERE BTRIM(""Func"")    <> '' AND ""Func""   IS NOT NULL)::int AS func_n,
  COUNT(*) FILTER (WHERE BTRIM(""Prod"")    <> '' AND ""Prod""   IS NOT NULL)::int AS prod_n,
  COUNT(*) FILTER (WHERE BTRIM(""Seq"")     <> '' AND ""Seq""    IS NOT NULL)::int AS seq_n,
  COUNT(*) FILTER (WHERE BTRIM(""Status"")  <> '' AND ""Status"" IS NOT NULL)::int AS status_n,
  COUNT(*) FILTER (WHERE BTRIM(""Rev"")     <> '' AND ""Rev""    IS NOT NULL)::int AS rev_n
FROM ""TaggedElements""
WHERE ""ProjectId"" = {0}
GROUP BY 1
ORDER BY (CASE WHEN COALESCE(NULLIF(BTRIM(""Disc""), ''), '(unset)') = '(unset)' THEN 1 ELSE 0 END), 1;";

        var conn = _db.Database.GetDbConnection();
        var raw = new List<RawHeatmapRow>();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sql.Replace("{0}", "@p0");
            var p = cmd.CreateParameter();
            p.ParameterName = "@p0";
            p.Value = projectId;
            cmd.Parameters.Add(p);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                raw.Add(new RawHeatmapRow(
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.GetInt32(7),
                    reader.GetInt32(8),
                    reader.GetInt32(9),
                    reader.GetInt32(10),
                    reader.GetInt32(11)));
            }
        }

        var totalElements = raw.Sum(r => r.ElementCount);
        if (raw.Count == 0)
        {
            return Ok(new
            {
                projectId,
                generatedAt = DateTime.UtcNow,
                totalElements = 0,
                tokens = new[] { "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ", "STATUS", "REV" },
                disciplines = Array.Empty<object>(),
            });
        }

        var groups = raw.Select(r =>
        {
            int Pct(int c) => r.ElementCount == 0 ? 0 : (int)Math.Round(100.0 * c / r.ElementCount);
            return new
            {
                discipline = r.Discipline,
                elementCount = r.ElementCount,
                cells = new
                {
                    DISC = Pct(r.DiscN),
                    LOC = Pct(r.LocN),
                    ZONE = Pct(r.ZoneN),
                    LVL = Pct(r.LvlN),
                    SYS = Pct(r.SysN),
                    FUNC = Pct(r.FuncN),
                    PROD = Pct(r.ProdN),
                    SEQ = Pct(r.SeqN),
                    STATUS = Pct(r.StatusN),
                    REV = Pct(r.RevN),
                }
            };
        }).ToList();

        return Ok(new
        {
            projectId,
            generatedAt = DateTime.UtcNow,
            totalElements,
            tokens = new[] { "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ", "STATUS", "REV" },
            disciplines = groups,
        });
    }

    /// <summary>Internal row shape returned by the heatmap raw SQL. Defined inline
    /// so it doesn't leak into the public DTO surface.</summary>
    private sealed record RawHeatmapRow(
        string Discipline,
        int ElementCount,
        int DiscN, int LocN, int ZoneN, int LvlN, int SysN, int FuncN, int ProdN, int SeqN,
        int StatusN, int RevN);

    /// <summary>
    /// Phase 146 — non-PostgreSQL heatmap path. Streams slim DTO rows via EF
    /// Core then groups in app-tier. Slower at scale than the raw-SQL path
    /// but works on SQLite (tests), SQL Server, and the in-memory provider.
    /// Response shape is identical to <see cref="GetTagHeatmap"/>.
    /// </summary>
    private async Task<object> ComputeHeatmapLinqAsync(Guid projectId, CancellationToken ct)
    {
        var rows = await _db.TaggedElements.AsNoTracking()
            .Where(e => e.ProjectId == projectId)
            .Select(e => new
            {
                Disc = e.Disc,
                Loc = e.Loc,
                Zone = e.Zone,
                Lvl = e.Lvl,
                Sys = e.Sys,
                Func = e.Func,
                Prod = e.Prod,
                Seq = e.Seq,
                Status = e.Status ?? "",
                Rev = e.Rev ?? "",
            })
            .ToListAsync(ct);

        var tokens = new[] { "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ", "STATUS", "REV" };
        if (rows.Count == 0)
        {
            return new
            {
                projectId,
                generatedAt = DateTime.UtcNow,
                totalElements = 0,
                tokens,
                disciplines = Array.Empty<object>(),
            };
        }

        const string UnsetBucket = "(unset)";
        var groups = rows
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Disc) ? UnsetBucket : r.Disc)
            .OrderBy(g => g.Key == UnsetBucket ? 1 : 0)
            .ThenBy(g => g.Key)
            .Select(g =>
            {
                var n = g.Count();
                int Pct(int c) => n == 0 ? 0 : (int)Math.Round(100.0 * c / n);
                return new
                {
                    discipline = g.Key,
                    elementCount = n,
                    cells = new
                    {
                        DISC = Pct(g.Count(r => !string.IsNullOrWhiteSpace(r.Disc))),
                        LOC = Pct(g.Count(r => !string.IsNullOrWhiteSpace(r.Loc))),
                        ZONE = Pct(g.Count(r => !string.IsNullOrWhiteSpace(r.Zone))),
                        LVL = Pct(g.Count(r => !string.IsNullOrWhiteSpace(r.Lvl))),
                        SYS = Pct(g.Count(r => !string.IsNullOrWhiteSpace(r.Sys))),
                        FUNC = Pct(g.Count(r => !string.IsNullOrWhiteSpace(r.Func))),
                        PROD = Pct(g.Count(r => !string.IsNullOrWhiteSpace(r.Prod))),
                        SEQ = Pct(g.Count(r => !string.IsNullOrWhiteSpace(r.Seq))),
                        STATUS = Pct(g.Count(r => !string.IsNullOrWhiteSpace(r.Status))),
                        REV = Pct(g.Count(r => !string.IsNullOrWhiteSpace(r.Rev))),
                    }
                };
            })
            .ToList();

        return new
        {
            projectId,
            generatedAt = DateTime.UtcNow,
            totalElements = rows.Count,
            tokens,
            disciplines = groups,
        };
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record PushComplianceRequest
{
    public int TotalElements { get; init; }
    public int TaggedComplete { get; init; }
    public int TaggedIncomplete { get; init; }
    public int Untagged { get; init; }
    public int FullyResolved { get; init; }
    public int StaleCount { get; init; }
    public int PlaceholderCount { get; init; }
    public int WarningCount { get; init; }
    public int WarningHealthScore { get; init; }
    public double TagPercent { get; init; }
    public double StrictPercent { get; init; }
    public double ContainerPercent { get; init; }
    public string RagStatus { get; init; } = "RED";
    public string? ByDisciplineJson { get; init; }
    public string? ByPhaseJson { get; init; }
    public string? EmptyTokenCountsJson { get; init; }
}
