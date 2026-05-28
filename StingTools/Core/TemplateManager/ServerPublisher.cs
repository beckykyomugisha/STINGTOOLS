using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;

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

                // Parse created/skipped/failed counts from the Counters bag (if present).
                int created = 0, skipped = 0, failed = 0;
                if (result.Counters != null)
                {
                    if (result.Counters.TryGetValue("created", out var c) && int.TryParse(c, out var ci)) created = ci;
                    if (result.Counters.TryGetValue("skipped", out var s) && int.TryParse(s, out var si)) skipped = si;
                    if (result.Counters.TryGetValue("failed",  out var f) && int.TryParse(f, out var fi)) failed = fi;
                }

                var payload = new
                {
                    operation      = result.Operation,
                    operationLabel = result.OperationLabel,
                    severity       = result.Severity.ToString(),
                    headline       = result.Headline,
                    subHeadline    = result.SubHeadline,
                    completedUtc   = result.CompletedUtc,
                    durationMs     = result.DurationMs,
                    user           = string.IsNullOrEmpty(result.UserName) ? Environment.UserName : result.UserName,
                    documentPath   = doc?.PathName ?? "",
                    documentTitle  = doc?.Title ?? "",
                    createdCount   = created,
                    skippedCount   = skipped,
                    failedCount    = failed,
                    sectionCount   = result.Sections?.Count ?? 0,
                    counters       = result.Counters
                };

                bool ok = false;
                try { ok = await client.PushTemplateOpAsync(projectId, payload); }
                catch (Exception ex) { StingTools.Core.StingLog.Warn($"ServerPublisher post: {ex.Message}"); }
                if (!ok)
                {
                    StingTools.Core.StingLog.Info("ServerPublisher: push skipped or returned non-2xx "
                        + $"op={result.Operation} project={projectId} (server route may be unavailable)");
                }
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
