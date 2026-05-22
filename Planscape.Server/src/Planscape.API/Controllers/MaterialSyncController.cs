using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planscape.Core.DTOs;

namespace Planscape.API.Controllers;

/// <summary>
/// I-2 — Material library snapshot endpoint.
/// Receives a snapshot of the project's materials from the Revit
/// plugin and stores it as a JSON blob under the server's data
/// directory. Minimum-viable persistence — full per-row DB rows
/// can land in a follow-up migration.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MaterialSyncController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    public MaterialSyncController(IWebHostEnvironment env) { _env = env; }

    [HttpPost("snapshot")]
    public async Task<ActionResult<MaterialSyncResponse>> PostSnapshot(
        [FromBody] MaterialSyncRequest req,
        CancellationToken ct)
    {
        if (req == null || req.ProjectId == Guid.Empty)
            return BadRequest(new { error = "ProjectId required" });

        var dir = Path.Combine(_env.ContentRootPath, "App_Data", "material_snapshots");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{req.ProjectId}.json");
        var json = JsonSerializer.Serialize(req, new JsonSerializerOptions { WriteIndented = false });
        await System.IO.File.WriteAllTextAsync(path, json, ct);

        return Ok(new MaterialSyncResponse
        {
            RowsAccepted = req.Materials?.Count ?? 0,
            Notes = $"Snapshot stored at {path}",
        });
    }

    [HttpGet("snapshot/{projectId:guid}")]
    public async Task<ActionResult<MaterialSyncRequest>> GetSnapshot(Guid projectId, CancellationToken ct)
    {
        var path = Path.Combine(_env.ContentRootPath, "App_Data", "material_snapshots", $"{projectId}.json");
        if (!System.IO.File.Exists(path)) return NotFound();
        var json = await System.IO.File.ReadAllTextAsync(path, ct);
        var snap = JsonSerializer.Deserialize<MaterialSyncRequest>(json);
        return snap != null ? Ok(snap) : (ActionResult<MaterialSyncRequest>)NotFound();
    }
}
