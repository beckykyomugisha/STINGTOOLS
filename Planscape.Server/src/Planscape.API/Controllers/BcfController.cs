using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.API.Authorization;

namespace Planscape.API.Controllers;

/// <summary>
/// T3 — BCF 2.1 bidirectional round-trip.
///
///   GET  /api/projects/{projectId}/bcf/export  → returns a .bcfzip with all
///        open issues serialised as BCF topics + viewpoints.
///   POST /api/projects/{projectId}/bcf/import  → accepts a .bcfzip, upserts
///        matching issues by BcfGuid.
///
/// BCF 2.1 (buildingSMART) structure:
///   bcf.version (XML)
///   {topic-guid}/markup.bcf (XML: Topic + Comment nodes)
///   {topic-guid}/viewpoint.bcfv (XML: camera + visibility)
///
/// Scaffold stores the essentials; camera serialisation matches the existing
/// BCF writer in the plugin so exports round-trip cleanly.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/bcf")]
[Authorize]
[ProjectAccess]
public class BcfController : ControllerBase
{
    private const string BcfXmlns  = "http://www.buildingsmart-tech.org/BCFXML/2.1";
    private const string XsiXmlns  = "http://www.w3.org/2001/XMLSchema-instance";

    private readonly PlanscapeDbContext _db;
    public BcfController(PlanscapeDbContext db) => _db = db;

    [HttpGet("export")]
    public async Task<IActionResult> Export(Guid projectId, [FromQuery] string? status, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();

        var q = _db.Issues.AsNoTracking().Where(i => i.ProjectId == projectId);
        if (!string.IsNullOrEmpty(status)) q = q.Where(i => i.Status == status);
        var issues = await q.ToListAsync(ct);

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // bcf.version
            await WriteEntry(zip, "bcf.version",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<Version VersionId=\"2.1\" xmlns:xsi=\"" + XsiXmlns + "\">" +
                  "<DetailedVersion>2.1</DetailedVersion>" +
                "</Version>");

            foreach (var issue in issues)
            {
                var guid = string.IsNullOrEmpty(issue.BcfGuid) ? issue.Id.ToString() : issue.BcfGuid!;
                await WriteEntry(zip, $"{guid}/markup.bcf",   BuildMarkup(issue, guid));
                await WriteEntry(zip, $"{guid}/viewpoint.bcfv", BuildViewpoint(issue));
            }
        }
        ms.Position = 0;
        return File(ms.ToArray(), "application/zip", $"planscape-{projectId:N}.bcfzip");
    }

    [HttpPost("import")]
    [RequestSizeLimit(100 * 1024 * 1024)]
    [Authorize(Roles = "Admin,Owner,Coordinator,Manager")]
    public async Task<ActionResult> Import(Guid projectId, IFormFile? file, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        if (file == null || file.Length == 0) return BadRequest(new { error = "file_required" });

        using var zipStream = file.OpenReadStream();
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
        int added = 0, updated = 0;

        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith("markup.bcf", StringComparison.OrdinalIgnoreCase)))
        {
            using var reader = new StreamReader(entry.Open());
            var xml = await reader.ReadToEndAsync(ct);
            var doc = XDocument.Parse(xml);
            var topic = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Topic");
            if (topic == null) continue;

            var guidAttr = topic.Attribute("Guid")?.Value ?? Guid.NewGuid().ToString();
            var title    = topic.Elements().FirstOrDefault(e => e.Name.LocalName == "Title")?.Value ?? "(no title)";
            var desc     = topic.Elements().FirstOrDefault(e => e.Name.LocalName == "Description")?.Value;
            var type     = topic.Elements().FirstOrDefault(e => e.Name.LocalName == "TopicType")?.Value ?? "RFI";
            var statusEl = topic.Elements().FirstOrDefault(e => e.Name.LocalName == "TopicStatus")?.Value ?? "OPEN";
            var priority = topic.Elements().FirstOrDefault(e => e.Name.LocalName == "Priority")?.Value ?? "MEDIUM";
            var assignee = topic.Elements().FirstOrDefault(e => e.Name.LocalName == "AssignedTo")?.Value;

            var existing = await _db.Issues.FirstOrDefaultAsync(i => i.BcfGuid == guidAttr && i.ProjectId == projectId, ct);
            if (existing == null)
            {
                _db.Issues.Add(new BimIssue
                {
                    ProjectId = projectId,
                    IssueCode = $"BCF-{guidAttr.Substring(0, 8)}",
                    Title = title.Length > 240 ? title[..240] : title,
                    Description = desc,
                    Type = type.ToUpperInvariant(),
                    Priority = priority.ToUpperInvariant(),
                    Status = statusEl.ToUpperInvariant(),
                    Assignee = assignee,
                    BcfGuid = guidAttr,
                    CreatedBy = User.FindFirst("display_name")?.Value ?? "bcf-import",
                    Source = "bcf",
                });
                added++;
            }
            else
            {
                existing.Title = title.Length > 240 ? title[..240] : title;
                existing.Description = desc ?? existing.Description;
                existing.Priority = priority.ToUpperInvariant();
                existing.Status   = statusEl.ToUpperInvariant();
                existing.Assignee = assignee ?? existing.Assignee;
                updated++;
            }
        }
        await _db.SaveChangesAsync(ct);
        return Ok(new { added, updated });
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<bool> ProjectInTenant(Guid projectId, CancellationToken ct)
    {
        var tenantId = Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
        if (tenantId == Guid.Empty) return false;
        return await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId, ct);
    }

    private static async Task WriteEntry(ZipArchive zip, string path, string content)
    {
        var e = zip.CreateEntry(path, CompressionLevel.Fastest);
        await using var s = e.Open();
        await using var w = new StreamWriter(s, Encoding.UTF8);
        await w.WriteAsync(content);
    }

    private static string BuildMarkup(BimIssue i, string guid)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<Markup xmlns=\"").Append(BcfXmlns).Append("\">");
        sb.Append("<Topic Guid=\"").Append(X(guid)).Append("\" TopicType=\"").Append(X(i.Type)).Append("\" TopicStatus=\"").Append(X(i.Status)).Append("\">");
        sb.Append("<Title>").Append(X(i.Title)).Append("</Title>");
        if (!string.IsNullOrEmpty(i.Description)) sb.Append("<Description>").Append(X(i.Description)).Append("</Description>");
        sb.Append("<Priority>").Append(X(i.Priority)).Append("</Priority>");
        sb.Append("<CreationDate>").Append(i.CreatedAt.ToString("o")).Append("</CreationDate>");
        sb.Append("<CreationAuthor>").Append(X(i.CreatedBy ?? "")).Append("</CreationAuthor>");
        if (!string.IsNullOrEmpty(i.Assignee))
            sb.Append("<AssignedTo>").Append(X(i.Assignee)).Append("</AssignedTo>");
        if (i.DueDate.HasValue)
            sb.Append("<DueDate>").Append(i.DueDate.Value.ToString("o")).Append("</DueDate>");
        sb.Append("</Topic>");
        sb.Append("</Markup>");
        return sb.ToString();
    }

    private static string BuildViewpoint(BimIssue i)
    {
        // Minimal orthogonal camera anchored on the issue's model XYZ when set.
        var hasXyz = i.ModelX.HasValue && i.ModelY.HasValue && i.ModelZ.HasValue;
        var px = hasXyz ? i.ModelX!.Value : 0;
        var py = hasXyz ? i.ModelY!.Value : 0;
        var pz = hasXyz ? i.ModelZ!.Value : 10;
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<VisualizationInfo xmlns=\"" + BcfXmlns + "\">" +
              "<OrthogonalCamera>" +
                $"<CameraViewPoint><X>{px:0.###}</X><Y>{py:0.###}</Y><Z>{pz:0.###}</Z></CameraViewPoint>" +
                "<CameraDirection><X>0</X><Y>0</Y><Z>-1</Z></CameraDirection>" +
                "<CameraUpVector><X>0</X><Y>1</Y><Z>0</Z></CameraUpVector>" +
                "<ViewToWorldScale>10</ViewToWorldScale>" +
              "</OrthogonalCamera>" +
            "</VisualizationInfo>";
    }

    private static string X(string s) => System.Security.SecurityElement.Escape(s ?? "") ?? "";
}
