using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Phase 180 — Single read-side facade over <see cref="PhotoPolicy"/>.
/// Existing consumers (capture / redact worker / digest job / approve
/// endpoint) used hard-coded defaults; this resolver returns the
/// project's overrides when present, defaults otherwise.
///
/// Cached per-request via the (projectId) key so the hot capture path
/// doesn't re-read on every photo. Non-thread-safe by design — scoped
/// service, one per request.
/// </summary>
public class PhotoPolicyResolver
{
    private readonly PlanscapeDbContext _db;
    private readonly Dictionary<Guid, PhotoPolicy?> _cache = new();

    public PhotoPolicyResolver(PlanscapeDbContext db) { _db = db; }

    public async Task<PhotoPolicy?> GetAsync(Guid projectId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(projectId, out var hit)) return hit;
        var pol = await _db.PhotoPolicies.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProjectId == projectId, ct);
        _cache[projectId] = pol;
        return pol;
    }

    /// <summary>
    /// Returns the per-project allowed-reason set (parsed from the
    /// policy's AllowedReasonsJson) or the built-in
    /// <see cref="SitePhoto.ValidReasons"/> when unset / unparsable.
    /// </summary>
    public static string[] AllowedReasonsOrDefault(PhotoPolicy? pol)
    {
        if (pol?.AllowedReasonsJson is { } json && !string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var arr = System.Text.Json.JsonSerializer.Deserialize<string[]>(json);
                if (arr != null && arr.Length > 0) return arr;
            }
            catch { /* fall through to defaults */ }
        }
        return SitePhoto.ValidReasons;
    }

    /// <summary>
    /// Returns the audience the policy says photos with this reason
    /// should default to, or null if the policy doesn't override.
    /// Caller falls back to <see cref="SitePhoto.DefaultToReview"/>.
    /// </summary>
    public static string? DefaultAudienceFor(PhotoPolicy? pol, string reason)
    {
        if (pol?.DefaultAudienceByReasonJson is { } json && !string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict != null && dict.TryGetValue(reason, out var aud)) return aud;
            }
            catch { /* ignore */ }
        }
        return null;
    }

    /// <summary>
    /// Returns true when the photo is inside the project's geofence
    /// polygon. Photos without GPS, or projects without a geofence,
    /// always pass. Implemented with a tiny ray-casting WKT polygon
    /// check that handles single POLYGON((lon lat, …)) WKT — sufficient
    /// for the BIM-manager-authored simple boundary case. Multi-polygon
    /// or holes-in-poly callers should return true (best-effort).
    /// </summary>
    public static bool InGeofence(PhotoPolicy? pol, double? lat, double? lon)
    {
        if (pol?.GeofenceWkt is null || lat is null || lon is null) return true;
        var ring = TryParsePolygonRing(pol.GeofenceWkt);
        if (ring == null) return true;
        return PointInRing(lon.Value, lat.Value, ring);
    }

    private static List<(double X, double Y)>? TryParsePolygonRing(string wkt)
    {
        try
        {
            var open  = wkt.IndexOf("((", StringComparison.Ordinal);
            var close = wkt.IndexOf("))", StringComparison.Ordinal);
            if (open < 0 || close < 0 || close <= open) return null;
            var pts = wkt.Substring(open + 2, close - open - 2)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var ring = new List<(double X, double Y)>(pts.Length);
            foreach (var pt in pts)
            {
                var parts = pt.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) return null;
                if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)) return null;
                if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)) return null;
                ring.Add((x, y));
            }
            return ring.Count >= 3 ? ring : null;
        }
        catch { return null; }
    }

    private static bool PointInRing(double x, double y, IList<(double X, double Y)> ring)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            if (((ring[i].Y > y) != (ring[j].Y > y)) &&
                (x < (ring[j].X - ring[i].X) * (y - ring[i].Y) / (ring[j].Y - ring[i].Y + 1e-12) + ring[i].X))
                inside = !inside;
        }
        return inside;
    }

    /// <summary>
    /// Returns the digest-recipient distribution group (when the policy
    /// names one and the group exists), else null. The digest job
    /// falls back to the legacy "every project member + every
    /// ClientGuest" resolution when null.
    /// </summary>
    public async Task<DistributionGroup?> GetDigestGroupAsync(Guid projectId, CancellationToken ct = default)
    {
        var pol = await GetAsync(projectId, ct);
        if (pol?.DigestDistributionGroupId is null) return null;
        return await _db.DistributionGroups.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == pol.DigestDistributionGroupId.Value, ct);
    }

    /// <summary>
    /// True when the policy's ApprovalChain demands a second approver
    /// for the given reason. Single = never; TwoStepSafety = Safety
    /// only; TwoStepAll = every reason.
    /// </summary>
    public static bool RequiresSecondApprover(PhotoPolicy? pol, string reason)
    {
        if (pol?.ApprovalChain is null or "Single") return false;
        if (pol.ApprovalChain == "TwoStepAll") return true;
        if (pol.ApprovalChain == "TwoStepSafety" && reason == "Safety") return true;
        return false;
    }
}
