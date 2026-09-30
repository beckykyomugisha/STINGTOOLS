using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.API.Controllers;

/// <summary>
///   GET /api/acc/reconnect-required — the caller's tenant's ACC connections that
///   need a person to reconnect ACC, with the reason and the exact call to make.
///
/// WHY: connections whose tokens were encrypted before the durable DataProtection
/// key store deployed (or under any key ring that has since been lost) read back
/// as undecryptable. The old key ring is gone by design — those tokens cannot be
/// recovered — so the only fix is a reconnect, and an administrator needs one
/// list of who must do it rather than discovering it project by project.
///
/// WHO: tenant Admin / Owner see every connection in the tenant; anyone else sees
/// only the projects they may administer (the same gate as connecting ACC). The
/// tenant query filter scopes the read.
///
/// A connection is listed when it is active and either (a) its stored tokens
/// cannot be decrypted, (b) it has no refresh token, or (c) its last sync or
/// test recorded RECONNECT_REQUIRED (e.g. ACC answered invalid_grant).
/// </summary>
[ApiController]
[Route("api/acc/reconnect-required")]
[Authorize]
public class AccReconnectController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public AccReconnectController(PlanscapeDbContext db) => _db = db;

    public sealed record Entry(Guid ConnectionId, Guid ProjectId, string ProjectName, string ConnectionName,
        string Reason, string? LastSyncStatus, DateTime? LastSyncAt, string Action);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var conns = await _db.PlatformConnections.AsNoTracking()
            .Where(c => c.Platform == PlatformType.ACC && c.IsActive)
            .Include(c => c.Project)
            .ToListAsync(ct);

        var result = new List<Entry>();
        var allowed = new Dictionary<Guid, bool>();
        foreach (var c in conns)
        {
            string? reason = ReasonFor(c);
            if (reason == null) continue;
            if (!allowed.TryGetValue(c.ProjectId, out bool ok))
                allowed[c.ProjectId] = ok = await this.CanAdministerProjectAsync(_db, c.ProjectId, ct);
            if (!ok) continue;
            result.Add(new Entry(c.Id, c.ProjectId, c.Project?.Name ?? "", c.Name, reason, c.LastSyncStatus, c.LastSyncAt,
                $"GET /api/acc/oauth/start?projectId={c.ProjectId} as a project manager or administrator, open the returned authorizeUrl and sign in to Autodesk."));
        }
        return Ok(new { count = result.Count, connections = result });
    }

    /// <summary>Why a connection needs a reconnect, or null when it does not.</summary>
    public static string? ReasonFor(PlatformConnection c)
    {
        if (AccTokenRefresher.TokensUnreadable(c))
            return "Stored tokens cannot be decrypted: they were encrypted under a server key ring that no longer exists (for example, saved before the durable key store was deployed). They cannot be recovered.";
        if (string.IsNullOrWhiteSpace(c.RefreshToken))
            return "No refresh token is stored.";
        if (c.LastSyncStatus == AccSyncService.StatusReconnect)
            return "The last sync or test reported RECONNECT_REQUIRED" + (string.IsNullOrEmpty(c.LastSyncError) ? "." : $": {Trim(c.LastSyncError)}");
        return null;
    }

    private static string Trim(string s)
    {
        // The stored error already carries the reconnect instruction; keep the cause.
        int cut = s.IndexOf(" To fix it, reconnect ACC", StringComparison.Ordinal);
        return cut > 0 ? s[..cut] : s;
    }
}
