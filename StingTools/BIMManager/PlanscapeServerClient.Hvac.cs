// PlanscapeServerClient — HVAC snapshot push (P1-B, fix/hvac-publish-rewire).
//
// Replaces the MergeRecoveryStubs no-op (Task.FromResult(false)) with a real
// authenticated POST to the server's actual HVAC contract:
//
//     POST /api/projects/{projectId}/hvac/snapshots   (HvacController.Push)
//
// The server stores one unified HvacSnapshot row per push, discriminated by a
// "kind" field (loads | balance | drift | carbon | sizing | nc). Per-row detail
// rides in PayloadJson; the KPI columns (inspected/pass/warn/fail/totalKw/
// worstValue/rag) drive the mobile dashboard's RAG cards.
//
// The pre-fix client carried PushHvacLoadsBulkAsync / PushHvacNcAsync targeting
// /hvac/loads + /hvac/nc — routes that never existed on the server. Those are
// removed; HvacPublishToServerCommand now builds snapshot bodies and calls this.

using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace StingTools.BIMManager
{
    public sealed partial class PlanscapeServerClient
    {
        /// <summary>
        /// Push one HVAC snapshot to <c>POST /api/projects/{projectId}/hvac/snapshots</c>.
        /// <paramref name="payload"/> is the snapshot body (a JObject or anonymous
        /// object carrying kind + KPI columns + payloadJson). Returns the new
        /// snapshot id on success, or <c>null</c> on failure (LastError is set).
        /// </summary>
        public async Task<Guid?> PushHvacSnapshotAsync(Guid projectId, object payload)
        {
            if (!await EnsureAuthenticatedAsync()) return null;
            try
            {
                var resp = await PostJsonAsync($"/api/projects/{projectId}/hvac/snapshots", payload)
                    .ConfigureAwait(false);
                if (!resp.ok)
                {
                    LastError = $"HVAC snapshot push failed ({resp.status}): {resp.body}";
                    return null;
                }
                // Server returns { id, capturedAt }. Surface the id; on a parse miss
                // still report success (the row landed) via Guid.Empty.
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
