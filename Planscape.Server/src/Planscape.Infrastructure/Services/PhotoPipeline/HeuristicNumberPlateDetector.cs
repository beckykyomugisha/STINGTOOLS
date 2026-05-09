using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Planscape.Infrastructure.Services.PhotoPipeline;

/// <summary>
/// Phase 178b — Heuristic number-plate detector. No ML model — finds
/// rectangular bright (white) or yellow regions with a 4–5:1 aspect
/// ratio in the lower 60% of the image (where vehicle plates land in
/// site photos). Adequate for the conservative privacy bar set by the
/// "blur for client publish" decision: false positives blur a sign or
/// a banner, which is fine; false negatives are the only real concern,
/// and the &gt; 20-faces crowd-shot quarantine in the pipeline catches
/// the worst cases anyway.
///
/// To swap in a real ALPR ONNX model later, bind a different
/// <see cref="INumberPlateDetector"/> impl in Program.cs — the
/// pipeline contract is unchanged.
/// </summary>
public sealed class HeuristicNumberPlateDetector : INumberPlateDetector
{
    // Plate aspect-ratio gates — UK / EU plates are 4.7:1; US 2:1; we
    // accept the union with a margin.
    private const float MinAspect = 1.8f;
    private const float MaxAspect = 5.5f;
    private const int   MinSidePx = 24;          // smaller than 24 px → noise
    private const int   ScanStridePx = 4;        // sub-sample stride for speed

    private readonly ILogger<HeuristicNumberPlateDetector> _logger;

    public HeuristicNumberPlateDetector(ILogger<HeuristicNumberPlateDetector> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<SKRectI>> DetectAsync(SKBitmap source, CancellationToken ct)
    {
        try
        {
            // Build a binary mask of "looks-plate-coloured" pixels. Two
            // colour bands: near-white (R,G,B > 200, low spread) and
            // UK rear yellow (R > 200, G > 180, B < 120).
            int w = source.Width, h = source.Height;
            int yMin = (int)(h * 0.4);            // lower 60% of image
            var mask = new bool[w, h];
            var px = source.Pixels;
            for (int y = yMin; y < h; y += ScanStridePx)
            {
                for (int x = 0; x < w; x += ScanStridePx)
                {
                    var c = px[y * w + x];
                    bool isWhite = c.Red > 200 && c.Green > 200 && c.Blue > 200
                                && Math.Abs(c.Red - c.Green) < 30
                                && Math.Abs(c.Green - c.Blue) < 30;
                    bool isYellow = c.Red > 200 && c.Green > 170 && c.Blue < 120;
                    mask[x, y] = isWhite || isYellow;
                }
            }

            // Connected-component labelling with a flood fill, keeping
            // only components whose bounding box matches plate dimensions.
            var visited = new bool[w, h];
            var hits = new List<SKRectI>();
            for (int y = yMin; y < h; y += ScanStridePx)
            {
                for (int x = 0; x < w; x += ScanStridePx)
                {
                    if (!mask[x, y] || visited[x, y]) continue;
                    var rect = FloodBounds(mask, visited, x, y, w, h);
                    int rw = rect.Width, rh = rect.Height;
                    if (rw < MinSidePx || rh < MinSidePx) continue;
                    float aspect = rw / (float)rh;
                    if (aspect < MinAspect || aspect > MaxAspect) continue;
                    // Sanity-cap area at 6% of the image — anything bigger
                    // is probably a sign, not a plate.
                    long area = (long)rw * rh;
                    if (area > w * h * 0.06) continue;
                    hits.Add(rect);
                }
            }
            return Task.FromResult<IReadOnlyList<SKRectI>>(hits);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HeuristicNumberPlateDetector: detection threw");
            return Task.FromResult<IReadOnlyList<SKRectI>>(Array.Empty<SKRectI>());
        }
    }

    /// <summary>
    /// Iterative 4-connected flood fill over the sub-sampled mask
    /// returning the AABB of the connected component including (sx, sy).
    /// We avoid recursion to keep the stack flat on large blobs.
    /// </summary>
    private static SKRectI FloodBounds(bool[,] mask, bool[,] visited, int sx, int sy, int w, int h)
    {
        int minX = sx, minY = sy, maxX = sx, maxY = sy;
        var stack = new Stack<(int x, int y)>();
        stack.Push((sx, sy));
        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if (x < 0 || y < 0 || x >= w || y >= h) continue;
            if (visited[x, y] || !mask[x, y]) continue;
            visited[x, y] = true;
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
            stack.Push((x + ScanStridePx, y));
            stack.Push((x - ScanStridePx, y));
            stack.Push((x, y + ScanStridePx));
            stack.Push((x, y - ScanStridePx));
        }
        return new SKRectI(minX, minY, maxX + ScanStridePx, maxY + ScanStridePx);
    }
}
