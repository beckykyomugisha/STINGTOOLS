// PlanscapeServerClient — read-only dashboard + cross-host identity getters.
//
// Three thin GETs the BIM Coordination Center renders:
//   • GET /api/projects/{id}/healthcare/dashboard    (HealthcareController.Dashboard)
//   • GET /api/projects/{id}/penetrations/dashboard   (PenetrationsController.Dashboard)
//   • GET /api/projects/{id}/ifc/mappings?ifcGuid=…    (IfcController.GetMappings)
//
// All mirror the existing GetMimDashboardAsync shape exactly: authenticate,
// GET, return the parsed JObject or null (LastError set on failure). No new
// transport — they ride the private GetAsync helper on the main partial.

using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace StingTools.BIMManager
{
    public sealed partial class PlanscapeServerClient
    {
        /// <summary>
        /// GET <c>/api/projects/{id}/healthcare/dashboard</c>. Returns the
        /// pressure / mgas / antiLigature RAG block + rdsCount, or null when
        /// not signed in / on error.
        /// </summary>
        public async Task<JObject?> GetHealthcareDashboardAsync(Guid projectId)
        {
            if (!await EnsureAuthenticatedAsync()) return null;
            try
            {
                var resp = await GetAsync($"/api/projects/{projectId}/healthcare/dashboard");
                return resp.ok ? JObject.Parse(resp.body) : null;
            }
            catch (Exception ex) { LastError = ex.Message; return null; }
        }

        /// <summary>
        /// GET <c>/api/projects/{id}/penetrations/dashboard</c>. Returns the
        /// byStatus / byHost group counts, or null when not signed in / on error.
        /// </summary>
        public async Task<JObject?> GetPenetrationsDashboardAsync(Guid projectId)
        {
            if (!await EnsureAuthenticatedAsync()) return null;
            try
            {
                var resp = await GetAsync($"/api/projects/{projectId}/penetrations/dashboard");
                return resp.ok ? JObject.Parse(resp.body) : null;
            }
            catch (Exception ex) { LastError = ex.Message; return null; }
        }

        /// <summary>
        /// GET <c>/api/projects/{id}/ifc/mappings?ifcGuid=…</c> — cross-host
        /// identity lookup. <paramref name="ifcGuid"/> is the canonical IFC
        /// GlobalId (the Revit UniqueId in our pipeline). Returns the
        /// MappingsPage JObject (items[] + paging) or null when not signed in /
        /// on error. NOTE the server query key is camelCase <c>ifcGuid</c>;
        /// ASP.NET model binding will not match a snake_case <c>ifc_guid</c>.
        /// </summary>
        public async Task<JObject?> GetIfcMappingsAsync(Guid projectId, string ifcGuid)
        {
            if (string.IsNullOrWhiteSpace(ifcGuid)) return null;
            if (!await EnsureAuthenticatedAsync()) return null;
            try
            {
                var resp = await GetAsync(
                    $"/api/projects/{projectId}/ifc/mappings?ifcGuid={Uri.EscapeDataString(ifcGuid)}");
                return resp.ok ? JObject.Parse(resp.body) : null;
            }
            catch (Exception ex) { LastError = ex.Message; return null; }
        }
    }
}
