#nullable enable
using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace StingTools.Core
{
    /// <summary>
    /// S8.4 — plugin auto-update poller. Runs once on plugin load (and
    /// every 24 h after) against the server's
    /// <c>/api/plugin/updates/manifest</c>. If the manifest reports a
    /// newer version, the plugin caches the download URL + sha256 and
    /// shows a small notification toast in the dock panel. The actual
    /// upgrade waits for the next Revit launch — Revit add-ins can't
    /// hot-reload — and is performed by a small standalone updater
    /// PluginUpdater.exe (sibling binary).
    ///
    /// Channels: 'stable' (default) | 'beta' (opt-in) | 'enterprise'
    /// (slow ring for procurement-locked customers).
    /// </summary>
    public static class PluginUpdateChecker
    {
        private static readonly HttpClient Http = new();

        public static async Task<UpdateInfo?> CheckAsync(string serverUrl, string channel = "stable")
        {
            try
            {
                var url = serverUrl.TrimEnd('/') + "/api/plugin/updates/manifest?channel=" + Uri.EscapeDataString(channel);
                using var resp = await Http.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return null;
                var json = await resp.Content.ReadAsStringAsync();
                var info = JsonSerializer.Deserialize<UpdateInfo>(json);
                if (info == null) return null;
                if (info.KillSwitch) return null;

                var current = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
                if (string.IsNullOrEmpty(info.Version)) return null;
                return IsNewer(info.Version, current) ? info : null;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Plugin update check failed: {ex.Message}");
                return null;
            }
        }

        public static bool IsNewer(string remote, string local)
        {
            if (Version.TryParse(remote, out var r) && Version.TryParse(local, out var l))
                return r > l;
            return string.Compare(remote, local, StringComparison.OrdinalIgnoreCase) > 0;
        }

        public class UpdateInfo
        {
            public string Channel { get; set; } = "";
            public string Version { get; set; } = "";
            public string Sha256 { get; set; } = "";
            public string DownloadUrl { get; set; } = "";
            public string MinRevit { get; set; } = "";
            public string ReleaseNotes { get; set; } = "";
            public bool KillSwitch { get; set; }
        }
    }
}
