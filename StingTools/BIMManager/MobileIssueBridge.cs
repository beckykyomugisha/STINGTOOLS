#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.BIMManager
{
    /// <summary>
    /// INT-10 — Mobile↔Plugin issue bridge.
    /// Pulls Planscape server issues for the active project and merges them into the
    /// local STING_BIM_MANAGER/issues.json sidecar (and pushes plugin-side new issues
    /// the other direction). Designed to be called from the existing PlatformSync command
    /// on a manual trigger so it composes with the user's existing workflow.
    /// </summary>
    public static class MobileIssueBridge
    {
        public class BridgeResult
        {
            public int Pulled { get; set; }
            public int Pushed { get; set; }
            public int Conflicts { get; set; }
            public string? Error { get; set; }
        }

        /// <summary>
        /// Sync server issues into local issues.json. Deduplicates by server "id" stored
        /// in the local entry's "server_id" field; updates body fields when local
        /// "modified_date" is older than server "updatedAt".
        /// </summary>
        public static async Task<BridgeResult> SyncAsync(
            Document doc,
            string serverBaseUrl,
            string bearerToken,
            Guid serverProjectId,
            HttpClient? http = null)
        {
            var result = new BridgeResult();
            if (doc == null) { result.Error = "No document"; return result; }
            if (string.IsNullOrEmpty(bearerToken)) { result.Error = "Not authenticated"; return result; }

            http ??= new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

            string issuesPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "issues.json");
            JArray local = LoadOrEmpty(issuesPath);

            // ── Pull from server ──
            try
            {
                var url = $"{serverBaseUrl.TrimEnd('/')}/api/projects/{serverProjectId}/issues?pageSize=500";
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    result.Error = $"Pull failed: HTTP {(int)resp.StatusCode}";
                    return result;
                }
                var body = await resp.Content.ReadAsStringAsync();
                var page = JObject.Parse(body);
                var items = page["items"] as JArray ?? new JArray();

                foreach (var serverIssue in items.OfType<JObject>())
                {
                    string serverId = serverIssue.Value<string>("id") ?? string.Empty;
                    if (string.IsNullOrEmpty(serverId)) continue;

                    var existing = local.OfType<JObject>()
                        .FirstOrDefault(j => j.Value<string>("server_id") == serverId);

                    if (existing == null)
                    {
                        var localEntry = ServerToLocal(serverIssue);
                        local.Add(localEntry);
                        result.Pulled++;
                    }
                    else
                    {
                        // Newer server wins on conflict (server already enforces audit trail)
                        var serverUpdated = serverIssue.Value<DateTime?>("updatedAt") ?? serverIssue.Value<DateTime>("createdAt");
                        var localModified = existing.Value<DateTime?>("modified_date") ?? existing.Value<DateTime>("created_date");
                        if (serverUpdated > localModified)
                        {
                            MergeServerIntoLocal(serverIssue, existing);
                            result.Conflicts++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("MobileIssueBridge.Pull failed", ex);
                result.Error = ex.Message;
            }

            // ── Push local-only entries (no server_id yet) ──
            foreach (var localIssue in local.OfType<JObject>().ToList())
            {
                if (!string.IsNullOrEmpty(localIssue.Value<string>("server_id"))) continue;
                try
                {
                    var payload = LocalToServerCreate(localIssue);
                    var url = $"{serverBaseUrl.TrimEnd('/')}/api/projects/{serverProjectId}/issues";
                    var content = new StringContent(payload.ToString(), System.Text.Encoding.UTF8, "application/json");
                    var resp = await http.PostAsync(url, content);
                    if (resp.IsSuccessStatusCode)
                    {
                        var created = JObject.Parse(await resp.Content.ReadAsStringAsync());
                        localIssue["server_id"] = created.Value<string>("id");
                        localIssue["server_code"] = created.Value<string>("issueCode");
                        result.Pushed++;
                    }
                }
                catch (Exception ex2)
                {
                    StingLog.Warn($"MobileIssueBridge.Push failed for {localIssue.Value<string>("id")}: {ex2.Message}");
                }
            }

            // ── Persist atomically ──
            var tmp = issuesPath + ".tmp";
            File.WriteAllText(tmp, local.ToString(Formatting.Indented));
            if (File.Exists(issuesPath)) File.Replace(tmp, issuesPath, issuesPath + ".bak");
            else File.Move(tmp, issuesPath);

            return result;
        }

        private static JArray LoadOrEmpty(string path)
        {
            try
            {
                if (!File.Exists(path)) return new JArray();
                var text = File.ReadAllText(path);
                return string.IsNullOrWhiteSpace(text) ? new JArray() : JArray.Parse(text);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MobileIssueBridge.LoadOrEmpty corrupt {path}: {ex.Message}");
                return new JArray();
            }
        }

        private static JObject ServerToLocal(JObject server)
        {
            return new JObject
            {
                ["id"] = $"MOB-{server.Value<string>("issueCode")}",
                ["server_id"] = server.Value<string>("id"),
                ["server_code"] = server.Value<string>("issueCode"),
                ["title"] = server.Value<string>("title"),
                ["description"] = server.Value<string>("description"),
                ["type"] = server.Value<string>("type"),
                ["priority"] = server.Value<string>("priority"),
                ["status"] = server.Value<string>("status"),
                ["assignee"] = server.Value<string>("assignee"),
                ["assignee_email"] = server.Value<string>("assigneeEmail"),
                ["discipline"] = server.Value<string>("discipline"),
                ["created_date"] = server.Value<DateTime?>("createdAt") ?? DateTime.UtcNow,
                ["modified_date"] = server.Value<DateTime?>("updatedAt") ?? server.Value<DateTime?>("createdAt") ?? DateTime.UtcNow,
                ["latitude"] = server["latitude"],
                ["longitude"] = server["longitude"],
                ["source"] = "mobile-bridge",
            };
        }

        private static void MergeServerIntoLocal(JObject server, JObject local)
        {
            local["status"] = server.Value<string>("status");
            local["priority"] = server.Value<string>("priority");
            local["assignee"] = server.Value<string>("assignee");
            local["assignee_email"] = server.Value<string>("assigneeEmail");
            local["modified_date"] = server.Value<DateTime?>("updatedAt") ?? DateTime.UtcNow;
        }

        private static JObject LocalToServerCreate(JObject local)
        {
            return new JObject
            {
                ["type"] = local.Value<string>("type") ?? "RFI",
                ["title"] = local.Value<string>("title"),
                ["description"] = local.Value<string>("description"),
                ["priority"] = local.Value<string>("priority") ?? "MEDIUM",
                ["assignee"] = local.Value<string>("assignee"),
                ["assigneeEmail"] = local.Value<string>("assignee_email"),
                ["discipline"] = local.Value<string>("discipline"),
                ["linkedElementIds"] = local.Value<string>("element_ids"),
                ["latitude"] = local["latitude"],
                ["longitude"] = local["longitude"],
                ["source"] = "plugin",
            };
        }
    }
}
