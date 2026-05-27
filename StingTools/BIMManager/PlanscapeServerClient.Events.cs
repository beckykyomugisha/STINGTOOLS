#nullable enable
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StingTools.BIMManager.PlatformEvents;
using StingTools.Core;

namespace StingTools.BIMManager;

/// <summary>
/// K2 — platform event spine client. The drainer pulls pending events and
/// acks/rejects them through these methods. Reuses the existing private
/// GetAsync / PostJsonAsync helpers (this is a partial of the singleton).
/// </summary>
public sealed partial class PlanscapeServerClient
{
    /// <summary>GET /api/projects/{projectId}/events/pending?sinceSeq=N</summary>
    public async Task<(bool ok, List<PlatformEventDto> events, long nextSeq)> GetPendingEventsAsync(
        Guid projectId, long sinceSeq = 0, int max = 200)
    {
        var list = new List<PlatformEventDto>();
        try
        {
            var resp = await GetAsync($"/api/projects/{projectId}/events/pending?sinceSeq={sinceSeq}&max={max}")
                .ConfigureAwait(false);
            if (!resp.ok) { LastError = $"events/pending HTTP {resp.status}"; return (false, list, sinceSeq); }

            var json = JObject.Parse(resp.body);
            var items = json["items"] as JArray ?? new JArray();
            foreach (var it in items)
            {
                list.Add(new PlatformEventDto
                {
                    Id                = Guid.TryParse((string?)it["id"], out var g) ? g : Guid.Empty,
                    Sequence          = (long?)it["sequence"] ?? 0,
                    Source            = (string?)it["source"] ?? "",
                    Type              = (string?)it["type"] ?? "",
                    PayloadJson       = (string?)it["payloadJson"] ?? "{}",
                    TargetIfcGlobalId = (string?)it["targetIfcGlobalId"],
                    BaseRevisionId    = (string?)it["baseRevisionId"],
                });
            }
            var nextSeq = (long?)json["nextSeq"] ?? sinceSeq;
            return (true, list, nextSeq);
        }
        catch (Exception ex)
        {
            StingLog.Error("GetPendingEventsAsync failed", ex);
            LastError = ex.Message;
            return (false, list, sinceSeq);
        }
    }

    /// <summary>POST /api/projects/{projectId}/events/{eventId}/ack</summary>
    public async Task<bool> AckEventAsync(Guid projectId, Guid eventId)
    {
        try
        {
            var resp = await PostJsonAsync($"/api/projects/{projectId}/events/{eventId}/ack", new { })
                .ConfigureAwait(false);
            return resp.ok;
        }
        catch (Exception ex) { StingLog.Error("AckEventAsync failed", ex); return false; }
    }

    /// <summary>POST /api/projects/{projectId}/events/{eventId}/reject</summary>
    public async Task<bool> RejectEventAsync(Guid projectId, Guid eventId, string reason, bool retryable = false)
    {
        try
        {
            var resp = await PostJsonAsync(
                $"/api/projects/{projectId}/events/{eventId}/reject",
                new { reason, retryable }).ConfigureAwait(false);
            return resp.ok;
        }
        catch (Exception ex) { StingLog.Error("RejectEventAsync failed", ex); return false; }
    }
}
