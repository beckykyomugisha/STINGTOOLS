using System.IO.Compression;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Infrastructure.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ZXing;
using ZXing.Common;
using Planscape.API.Authorization;

namespace Planscape.API.Controllers;

[ApiController]
[Route("api/projects/{projectId}")]
[Authorize]
[ProjectAccess]
public class QrController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public QrController(PlanscapeDbContext db) => _db = db;

    /// <summary>
    /// Generate a QR code PNG for a single element's tag.
    /// </summary>
    [HttpGet("elements/{elementId}/qr")]
    public async Task<ActionResult> GetElementQr(Guid projectId, Guid elementId, [FromQuery] int size = 200)
    {
        var tenantId = GetTenantId();
        var el = await _db.TaggedElements
            .FirstOrDefaultAsync(e => e.Id == elementId && e.ProjectId == projectId && e.Project!.TenantId == tenantId);
        if (el == null) return NotFound();

        var content = !string.IsNullOrEmpty(el.Tag1) ? el.Tag1 : el.Tag7 ?? el.Id.ToString();
        var png = GenerateQrPng(content, size);
        return File(png, "image/png");
    }

    /// <summary>
    /// Generate QR code PNGs for multiple elements, returned as a ZIP archive.
    /// </summary>
    [HttpPost("qr/batch")]
    public async Task<ActionResult> BatchQr(Guid projectId, [FromBody] BatchQrRequest req)
    {
        if (req.ElementIds == null || req.ElementIds.Count == 0)
            return BadRequest("elementIds is required");

        var tenantId = GetTenantId();
        var ids = req.ElementIds.Select(Guid.Parse).ToList();

        var elements = await _db.TaggedElements
            .Where(e => e.ProjectId == projectId && ids.Contains(e.Id) && e.Project!.TenantId == tenantId)
            .ToListAsync();

        if (elements.Count == 0) return NotFound();

        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in elements)
            {
                var content = !string.IsNullOrEmpty(el.Tag1) ? el.Tag1 : el.Tag7 ?? el.Id.ToString();
                var png = GenerateQrPng(content, 200);

                var baseName = !string.IsNullOrEmpty(el.Tag1) ? el.Tag1 : el.Id.ToString();
                var name = baseName;
                var counter = 2;
                while (!usedNames.Add(name))
                    name = $"{baseName}_{counter++}";

                var entry = archive.CreateEntry($"{name}.png", CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(png, 0, png.Length);
            }
        }

        zipStream.Position = 0;
        return File(zipStream.ToArray(), "application/zip", "qr-codes.zip");
    }

    private static byte[] GenerateQrPng(string content, int size)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = size, Height = size, Margin = 1 }
        };

        var pixelData = writer.Write(content);

        using var image = Image.LoadPixelData<Rgba32>(pixelData.Pixels, pixelData.Width, pixelData.Height);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private Guid GetTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return claim != null ? Guid.Parse(claim) : Guid.Empty;
    }

    public record BatchQrRequest(List<string> ElementIds);
}
