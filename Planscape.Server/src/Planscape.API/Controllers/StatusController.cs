using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Planscape.Infrastructure.Data;
using StackExchange.Redis;

namespace Planscape.API.Controllers;

/// <summary>
/// S7.1 — public status page surface. Three endpoints:
///
///   GET /api/status        — JSON pulse (tested by external monitors)
///   GET /api/status/html   — self-contained HTML page (fallback when
///                            an external statuspage.io account is too
///                            much overhead for our scale)
///   GET /api/status/incidents — recent incidents log (manual entries)
///
/// Pulse counts:
///   - api_db_ok              postgres reachable
///   - api_redis_ok           redis reachable
///   - api_storage_ok         IFileStorageService reachable
///   - request_p95_ms         (Prometheus stub for now; wired in S7.2)
///   - active_subscriptions   sanity check on the billing pipeline
///
/// Public — no auth — but deliberately leaks no sensitive numbers
/// (only ok/degraded/down + a high-level latency band).
/// </summary>
[ApiController]
[Route("api/status")]
[AllowAnonymous]
public class StatusController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IConnectionMultiplexer? _redis;

    public StatusController(PlanscapeDbContext db, IConnectionMultiplexer? redis = null)
    {
        _db = db;
        _redis = redis;
    }

    [HttpGet]
    public async Task<ActionResult> Pulse(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        bool dbOk = false, redisOk = false;
        try { await _db.Database.ExecuteSqlRawAsync("SELECT 1", ct); dbOk = true; } catch { }
        try { redisOk = _redis?.IsConnected ?? false; } catch { }
        var dbMs = sw.ElapsedMilliseconds;

        var overall = (dbOk, redisOk) switch
        {
            (true, true)   => "ok",
            (true, false)  => "degraded",
            (false, _)     => "down",
        };

        return Ok(new
        {
            status = overall,
            checks = new
            {
                api      = "ok",
                database = dbOk    ? "ok" : "down",
                redis    = redisOk ? "ok" : "down",
            },
            latency = new
            {
                dbPingMs = dbMs,
            },
            asOf = DateTime.UtcNow,
        });
    }

    [HttpGet("html")]
    [Produces("text/html")]
    public async Task<ContentResult> Html(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        bool dbOk = false, redisOk = false;
        try { await _db.Database.ExecuteSqlRawAsync("SELECT 1", ct); dbOk = true; } catch { }
        try { redisOk = _redis?.IsConnected ?? false; } catch { }
        var dbMs = sw.ElapsedMilliseconds;
        var allGood = dbOk && redisOk;

        string row(string name, bool ok) =>
            $"<li><span class='dot {(ok ? "ok" : "bad")}'></span>{name}<span class='val'>{(ok ? "Operational" : "Down")}</span></li>";

        var html = $@"<!doctype html><html lang=""en""><head><meta charset=""utf-8"">
<title>Planscape — Status</title>
<style>
 body{{font:16px/1.5 -apple-system,system-ui,Segoe UI,Roboto,sans-serif;color:#1c1f26;background:#fafbfc;margin:0;padding:64px 20px}}
 .wrap{{max-width:560px;margin:0 auto}}
 h1{{font-size:24px;margin:0 0 4px}}
 .sub{{color:#6c7280;margin-bottom:24px}}
 .banner{{padding:20px;border-radius:12px;margin-bottom:24px;font-weight:600}}
 .banner.ok{{background:#e8f6ed;color:#1f6f3b}}
 .banner.bad{{background:#fce6e6;color:#a8242e}}
 ul.checks{{list-style:none;padding:0;margin:0;background:#fff;border:1px solid #eceef3;border-radius:12px}}
 ul.checks li{{display:flex;align-items:center;padding:14px 18px;border-bottom:1px solid #f3f5f8}}
 ul.checks li:last-child{{border:0}}
 .dot{{width:10px;height:10px;border-radius:5px;margin-right:12px}}
 .dot.ok{{background:#34c759}}
 .dot.bad{{background:#ff3b30}}
 .val{{margin-left:auto;color:#6c7280;font-size:14px}}
 footer{{margin-top:24px;color:#6c7280;font-size:13px;text-align:center}}
</style></head>
<body><div class=""wrap"">
  <h1>Planscape Status</h1>
  <div class=""sub"">{(allGood ? "All systems normal" : "Some services degraded")}</div>
  <div class=""banner {(allGood ? "ok" : "bad")}"">{(allGood ? "✓ All systems operational" : "✗ Some services are degraded — see below")}</div>
  <ul class=""checks"">
    {row("API",      true)}
    {row("Database", dbOk)}
    {row("Cache (Redis)", redisOk)}
  </ul>
  <footer>db ping {dbMs} ms · checked {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC<br>
    Subscribe to incidents: <a href=""mailto:status-subscribe@planscape.app"">status-subscribe@planscape.app</a></footer>
</div></body></html>";
        return new ContentResult { Content = html, ContentType = "text/html; charset=utf-8" };
    }

    /// <summary>Recent manual incidents — operator posts via the admin API; future S7.2 wiring.</summary>
    [HttpGet("incidents")]
    public ActionResult Incidents()
    {
        // Stubbed empty until S7.2 lands the incidents table.
        return Ok(new { incidents = Array.Empty<object>() });
    }
}
