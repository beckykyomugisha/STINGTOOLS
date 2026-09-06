using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planscape.API.Authorization;
using Planscape.API.Services;
using Planscape.Core.DTOs;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// I-2 — Material library snapshot endpoint.
/// Receives a snapshot of the project's materials from the Revit plugin and
/// stores it as a JSON blob under the server's data directory.
///
/// SECURITY — why this controller looks different from its first version
/// ---------------------------------------------------------------------
/// It carried <c>[Authorize]</c> and nothing else: no <c>[ProjectAccess]</c>, no
/// membership check, no tenant filter. It also does not use EF, so the DbContext's
/// global tenant query filter — which silently protects most controllers that
/// forget — could not reach it. Any authenticated user in any tenant who had a
/// project GUID could READ another tenant's material library, and
/// <c>PostSnapshot</c> was equally unguarded, so they could OVERWRITE it too.
///
/// The project id also had to move from the request BODY into the ROUTE.
/// <see cref="ProjectAccessAttribute"/> resolves its subject from route data and
/// deliberately no-ops when the route carries no project id ("let the action
/// handle it"), so leaving the id in the body would have added the attribute
/// while changing nothing — a fix that reads as protection and isn't.
///
/// PERSISTENCE — deliberately NOT changed here
/// -------------------------------------------
/// Snapshots are still files under ContentRootPath/App_Data. That has two known
/// problems: the container filesystem is ephemeral (a Render redeploy loses every
/// snapshot), and being outside EF is what made the tenant filter unable to help
/// above. Moving this to a table is a persisted-storage change and needs its own
/// decision, so it is only recorded here, not done.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/material-sync")]
[Authorize]
[ProjectAccess]
public class MaterialSyncController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly PlanscapeDbContext _db;

    public MaterialSyncController(IWebHostEnvironment env, PlanscapeDbContext db)
    {
        _env = env;
        _db = db;
    }

    [HttpPost("snapshot")]
    public async Task<ActionResult<MaterialSyncResponse>> PostSnapshot(
        Guid projectId,
        [FromBody] MaterialSyncRequest req,
        CancellationToken ct)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        if (req == null) return BadRequest(new { error = "body_required" });

        // The DTO still carries ProjectId for backwards compatibility. The ROUTE
        // is authoritative — it is what was authorised. A body that disagrees is
        // rejected rather than silently ignored, so a caller can never believe it
        // wrote to a project it did not.
        if (req.ProjectId != Guid.Empty && req.ProjectId != projectId)
            return BadRequest(new { error = "project_id_mismatch" });

        var path = SnapshotPath(projectId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        req.ProjectId = projectId;
        var json = JsonSerializer.Serialize(req, new JsonSerializerOptions { WriteIndented = false });
        await System.IO.File.WriteAllTextAsync(path, json, ct);

        return Ok(new MaterialSyncResponse
        {
            RowsAccepted = req.Materials?.Count ?? 0,
            // Was $"Snapshot stored at {path}", which handed the server's
            // filesystem layout to any caller.
            Notes = "Snapshot stored.",
        });
    }

    [HttpGet("snapshot")]
    public async Task<ActionResult<MaterialSyncRequest>> GetSnapshot(Guid projectId, CancellationToken ct)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;

        var path = SnapshotPath(projectId);
        if (!System.IO.File.Exists(path)) return NotFound();
        var json = await System.IO.File.ReadAllTextAsync(path, ct);
        var snap = JsonSerializer.Deserialize<MaterialSyncRequest>(json);
        return snap != null ? Ok(snap) : (ActionResult<MaterialSyncRequest>)NotFound();
    }

    /// <summary>Path is derived from the ROUTE-authorised project id only —
    /// never from caller-supplied text — so it cannot be steered outside the
    /// snapshot directory.</summary>
    private string SnapshotPath(Guid projectId)
        => Path.Combine(_env.ContentRootPath, "App_Data", "material_snapshots", $"{projectId}.json");
}
