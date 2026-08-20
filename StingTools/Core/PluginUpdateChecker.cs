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
                StingLog.Warn($"Plugin update check failed: {Diagnose(serverUrl, ex)}");
                return null;
            }
        }

        /// <summary>
        /// Turns a transport exception into something a reader can act on.
        ///
        /// <para>The default .NET text for an unresolvable host is
        /// <c>"No such host is known. (api.planscape.build:443)"</c>, which reads as a
        /// transient network problem. It is not: as measured on 2026-08-20 against
        /// Google's resolver (with controls — a host that certainly exists resolved
        /// normally, and a host that certainly does not returned NXDOMAIN), the
        /// documented custom domain <c>api.planscape.build</c> has no DNS record at all,
        /// while <c>app.planscape.build</c> resolves. See #705: the record was never
        /// attached to the Render service.</para>
        ///
        /// <para>Naming the host and separating "this name does not exist" from "this
        /// name exists and did not answer" is the whole point. Every Revit session logs
        /// this line at startup, and for as long as it said "No such host is known" the
        /// honest conclusion — a documented hostname that was never configured — was
        /// indistinguishable from the reader being offline.</para>
        ///
        /// <para>This deliberately does NOT substitute a different hostname. The
        /// intended address is correct; the DNS record is missing, and that is the
        /// owner's to add. Papering over it in code would remove the only signal that
        /// it is still missing.</para>
        /// </summary>
        internal static string Diagnose(string serverUrl, Exception ex)
        {
            string host;
            try { host = new Uri(serverUrl).Host; }
            catch { host = serverUrl; }

            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                if (e is System.Net.Sockets.SocketException se
                    && (se.SocketErrorCode == System.Net.Sockets.SocketError.HostNotFound
                     || se.SocketErrorCode == System.Net.Sockets.SocketError.NoData))
                {
                    return $"'{host}' does not resolve — no DNS record exists for it. "
                         + "This is a configuration gap, not a network outage: the hostname is "
                         + "correct but was never pointed at the running service (see issue #705). "
                         + "Set STING_PLANSCAPE_URL, or the server URL in Coordination Center "
                         + "settings, to a host that resolves. "
                         + $"[{ex.GetType().Name}: {ex.Message}]";
                }
            }

            if (ex is TaskCanceledException || ex is OperationCanceledException)
                return $"'{host}' resolved but did not answer in time (timeout). [{ex.Message}]";

            return $"'{host}': {ex.Message}";
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
