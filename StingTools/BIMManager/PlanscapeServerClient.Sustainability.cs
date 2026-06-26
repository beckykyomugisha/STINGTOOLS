// PlanscapeServerClient — EDGE/LEED sustainability snapshot push (Phase 195, WS A6).
//
// Mirrors PushHvacSnapshotAsync. One authenticated POST per push to:
//
//     POST /api/projects/{projectId}/sustainability/snapshots  (SustainabilityController)
//
// The body is the EdgeKpiSnapshot projection (headline savings %, EDGE level,
// operational carbon) + a verbatim PayloadJson. Returns the new snapshot id on
// success or null on failure (LastError set).

using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace StingTools.BIMManager
{
    public sealed partial class PlanscapeServerClient
    {
        /// <summary>Push one sustainability snapshot. Returns the new id on success,
        /// or <c>null</c> on failure (LastError is set).</summary>
        public async Task<Guid?> PushSustainabilitySnapshotAsync(Guid projectId, object payload)
        {
            if (!await EnsureAuthenticatedAsync()) return null;
            try
            {
                var resp = await PostJsonAsync($"/api/projects/{projectId}/sustainability/snapshots", payload)
                    .ConfigureAwait(false);
                if (!resp.ok)
                {
                    LastError = $"Sustainability snapshot push failed ({resp.status}): {resp.body}";
                    return null;
                }
                var json = JObject.Parse(resp.body);
                var idStr = json["id"]?.Value<string>();
                return Guid.TryParse(idStr, out var id) ? id : Guid.Empty;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return null;
            }
        }
    }
}
