using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.TemplateManager
{
    /// <summary>
    /// Best-effort publish of Template Manager operation results to the
    /// Planscape Server compliance endpoint. Fire-and-forget so the
    /// dashboard never blocks on network IO. When the user is logged out
    /// or the server is unreachable, drops the payload + logs a warning.
    /// </summary>
    public static class ServerPublisher
    {
        public static void Publish(Document doc, OperationResult result)
        {
            if (doc == null || result == null) return;
            // Fire and forget — the dashboard must not block waiting for the server
            _ = PublishAsync(doc, result);
        }

        private static async Task PublishAsync(Document doc, OperationResult result)
        {
            try
            {
                var client = global::StingTools.BIMManager.PlanscapeServerClient.Instance;
                if (client == null || !client.IsConnected) return;

                // Resolve project ID via name (best-effort)
                string projectName = doc?.ProjectInformation?.Name ?? doc?.Title ?? "untitled";
                string projectCode = SafeGetProjectCode(doc) ?? projectName;
                Guid projectId;
                try { projectId = await client.GetOrCreateProjectAsync(projectName, projectCode); }
                catch (Exception ex)
                {
                    StingTools.Core.StingLog.Warn($"ServerPublisher GetOrCreateProject: {ex.Message}");
                    return;
                }

                var payload = new JObject
                {
                    ["projectId"]      = projectId,
                    ["operation"]      = result.Operation,
                    ["operationLabel"] = result.OperationLabel,
                    ["severity"]       = result.Severity.ToString(),
                    ["headline"]       = result.Headline,
                    ["subHeadline"]    = result.SubHeadline,
                    ["completedUtc"]   = result.CompletedUtc,
                    ["durationMs"]     = result.DurationMs,
                    ["user"]           = Environment.UserName,
                    ["documentPath"]   = doc?.PathName ?? "",
                    ["documentTitle"]  = doc?.Title ?? "",
                    ["counters"]       = JObject.FromObject(result.Counters ?? new Dictionary<string, string>()),
                    ["sectionCount"]   = result.Sections?.Count ?? 0
                };

                // Best-effort POST. The actual endpoint is added server-side via the
                // gap analysis; until then this is a no-op when the server's
                // /template-ops route is missing — log + continue.
                try
                {
                    // Hook: client.PostAsync($"/api/projects/{projectId}/template-ops", payload);
                    // The dedicated endpoint isn't on every server version, so we just
                    // log the payload here. When the server route lands, swap to a
                    // real call without touching callers.
                    StingTools.Core.StingLog.Info("ServerPublisher: payload prepared "
                        + $"op={result.Operation} sev={result.Severity} project={projectId}");
                }
                catch (Exception ex) { StingTools.Core.StingLog.Warn($"ServerPublisher post: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"ServerPublisher.PublishAsync: {ex.Message}");
            }
        }

        private static string SafeGetProjectCode(Document doc)
        {
            try
            {
                var p = doc?.ProjectInformation?.LookupParameter("PRJ_ORG_PROJECT_CODE_TXT");
                if (p != null && p.HasValue) return p.AsString();
            }
            catch { }
            return null;
        }
    }
}
