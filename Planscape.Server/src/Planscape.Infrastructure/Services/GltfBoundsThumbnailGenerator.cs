using Microsoft.Extensions.Logging;
using Planscape.Core.Interfaces;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// P8 — Pure-managed thumbnail generator. Parses a GLB header to extract the
/// scene bounds (taken from the largest POSITION accessor's min/max), projects
/// the resulting AABB into an isometric view, and renders a 512×512 PNG with
/// three faces shaded for depth + a 1-px outline. No GPU / native dependencies
/// — runs in any container.
///
/// This is a placeholder until a true headless renderer is wired in. It's
/// vastly better than the emoji fallback because each model gets a unique
/// silhouette reflecting its real footprint and aspect ratio, and the file
/// size badge gives a hint at model complexity.
/// </summary>
public class GltfBoundsThumbnailGenerator : IModelThumbnailGenerator
{
    private readonly ILogger<GltfBoundsThumbnailGenerator> _logger;
    public string ProviderName => "gltf-bounds";

    private const int Size = 512;

    public GltfBoundsThumbnailGenerator(ILogger<GltfBoundsThumbnailGenerator> logger)
    {
        _logger = logger;
    }

    public async Task<ThumbnailResult> GenerateAsync(string modelPath, string outputPngPath, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var bounds = await ReadGlbBoundsAsync(modelPath, ct);
            using var img = new Image<Rgba32>(Size, Size, new Rgba32(34, 38, 47, 255));
            DrawIsoBox(img, bounds);
            DrawFooter(img, modelPath, bounds);
            await using var fs = File.Create(outputPngPath);
            await img.SaveAsPngAsync(fs, ct);
            sw.Stop();
            return new ThumbnailResult(true, ProviderName, sw.ElapsedMilliseconds, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Bounds thumbnail failed for {Path}", modelPath);
            return new ThumbnailResult(false, ProviderName, sw.ElapsedMilliseconds, ex.Message);
        }
    }

    private static async Task<(double sx, double sy, double sz)> ReadGlbBoundsAsync(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var br = new BinaryReader(fs);
        if (fs.Length < 20) return (1, 1, 1);
        uint magic = br.ReadUInt32();
        br.ReadUInt32();
        br.ReadUInt32();
        if (magic != 0x46546C67) return (1, 1, 1);

        uint jsonLen = br.ReadUInt32();
        uint jsonType = br.ReadUInt32();
        if (jsonType != 0x4E4F534A) return (1, 1, 1);
        var jsonBytes = br.ReadBytes((int)jsonLen);
        var json = System.Text.Encoding.UTF8.GetString(jsonBytes).TrimEnd('\0', ' ');
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        // Walk meshes → primitives → attributes.POSITION to collect only the
        // POSITION accessor indices. Unioning every VEC3 accessor would pull
        // in NORMAL (range -1..1) and TANGENT, which contaminate the box.
        var positionAccessors = new HashSet<int>();
        if (doc.RootElement.TryGetProperty("meshes", out var meshes))
        {
            foreach (var mesh in meshes.EnumerateArray())
            {
                if (!mesh.TryGetProperty("primitives", out var prims)) continue;
                foreach (var prim in prims.EnumerateArray())
                {
                    if (!prim.TryGetProperty("attributes", out var attrs)) continue;
                    if (attrs.TryGetProperty("POSITION", out var posIdx) && posIdx.ValueKind == System.Text.Json.JsonValueKind.Number)
                        positionAccessors.Add(posIdx.GetInt32());
                }
            }
        }

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        bool any = false;

        if (doc.RootElement.TryGetProperty("accessors", out var accs))
        {
            int idx = 0;
            foreach (var a in accs.EnumerateArray())
            {
                int thisIdx = idx++;
                // If we found POSITION refs, restrict to those; otherwise fall
                // back to "any VEC3 with min/max" so old/odd glTFs still work.
                bool isPosition = positionAccessors.Count > 0
                    ? positionAccessors.Contains(thisIdx)
                    : (a.TryGetProperty("type", out var t) && t.GetString() == "VEC3");
                if (!isPosition) continue;
                if (!a.TryGetProperty("min", out var mn) || !a.TryGetProperty("max", out var mx)) continue;
                if (mn.GetArrayLength() < 3 || mx.GetArrayLength() < 3) continue;
                double aMinX = mn[0].GetDouble(), aMinY = mn[1].GetDouble(), aMinZ = mn[2].GetDouble();
                double aMaxX = mx[0].GetDouble(), aMaxY = mx[1].GetDouble(), aMaxZ = mx[2].GetDouble();
                if (aMinX < minX) minX = aMinX; if (aMinY < minY) minY = aMinY; if (aMinZ < minZ) minZ = aMinZ;
                if (aMaxX > maxX) maxX = aMaxX; if (aMaxY > maxY) maxY = aMaxY; if (aMaxZ > maxZ) maxZ = aMaxZ;
                any = true;
            }
        }
        if (!any) return (1, 1, 1);
        return (Math.Max(maxX - minX, 1e-6), Math.Max(maxY - minY, 1e-6), Math.Max(maxZ - minZ, 1e-6));
    }

    private static void DrawIsoBox(Image<Rgba32> img, (double sx, double sy, double sz) b)
    {
        double maxDim = Math.Max(b.sx, Math.Max(b.sy, b.sz));
        double nx = b.sx / maxDim, ny = b.sy / maxDim, nz = b.sz / maxDim;

        const float scale = 140f;
        var cx = Size / 2f;
        var cy = Size / 2f + 30f;

        PointF P(double x, double y, double z)
        {
            float px = (float)((x - y) * 0.866 * scale);
            float py = (float)(((x + y) * 0.5 - z) * scale);
            return new PointF(cx + px, cy + py);
        }

        var p000 = P(0, 0, 0);
        var p100 = P(nx, 0, 0);
        var p010 = P(0, ny, 0);
        var p110 = P(nx, ny, 0);
        var p001 = P(0, 0, nz);
        var p101 = P(nx, 0, nz);
        var p011 = P(0, ny, nz);
        var p111 = P(nx, ny, nz);

        img.Mutate(ctx =>
        {
            ctx.FillPolygon(new Rgba32(94, 116, 158, 255), p001, p101, p111, p011);
            ctx.FillPolygon(new Rgba32(74, 92, 130, 255),  p100, p110, p111, p101);
            ctx.FillPolygon(new Rgba32(58, 74, 108, 255),  p010, p110, p111, p011);
            var pen = Pens.Solid(new Rgba32(220, 232, 255, 255), 1.5f);
            void L(PointF a, PointF b) => ctx.DrawLine(pen, a, b);
            L(p000, p100); L(p000, p010); L(p000, p001);
            L(p100, p110); L(p100, p101);
            L(p010, p110); L(p010, p011);
            L(p001, p101); L(p001, p011);
            L(p110, p111); L(p101, p111); L(p011, p111);
        });
    }

    private static void DrawFooter(Image<Rgba32> img, string modelPath, (double sx, double sy, double sz) b)
    {
        try
        {
            var family = SystemFonts.Collection.Families.FirstOrDefault();
            if (family.Name == null) return;
            var font = family.CreateFont(13f, FontStyle.Regular);
            string size = $"{b.sx / 1000:0.0} × {b.sy / 1000:0.0} × {b.sz / 1000:0.0} m";
            string name = Path.GetFileNameWithoutExtension(modelPath);
            if (name.Length > 36) name = name.Substring(0, 33) + "…";
            img.Mutate(ctx =>
            {
                ctx.DrawText(name, font, new Rgba32(238, 242, 252, 255), new PointF(16, 14));
                ctx.DrawText(size, font, new Rgba32(168, 184, 220, 255), new PointF(16, Size - 28));
            });
        }
        catch { }
    }
}
