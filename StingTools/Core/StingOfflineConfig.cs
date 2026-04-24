// Pack 0 — Offline-mode gate.
//
// STING is offline-first. The four plugin commands that actually touch the
// network (PlanscapeConnect, ACCPublish, SharePointExport, PlatformSync) read
// this flag before attempting any socket work. Every other command is
// naturally offline because it has no network code path.
//
// Source of truth per project: <project>/_BIM_COORD/sting_config.json
//   {
//     "OfflineOnly": true,          // gate the 4 network commands
//     "RealtimeCascades": true      // reserved for Pack 7 DocumentChanged
//   }
//
// Default: OfflineOnly = true. Opting online is deliberate and per-project.
//
// Load order:
//   * OnStartup           — defaults applied (no doc open yet, nothing to read)
//   * OnDocumentOpened    — project config reloaded; dock-panel indicator updated
//
// Offline refusal is loud (TaskDialog + StingLog) not silent — users should
// know when a button did nothing and what to flip to unblock it.

using System;
using System.IO;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    public static class StingOfflineConfig
    {
        private static readonly object _lock = new object();
        private static bool _offlineOnly = true;
        private static bool _realtimeCascades = true;
        private static string _lastLoadedFrom = "(defaults)";

        /// <summary>True when the four network commands should refuse to run.</summary>
        public static bool IsOffline
        {
            get { lock (_lock) return _offlineOnly; }
        }

        /// <summary>Reserved for Pack 7 (DocumentChanged cascades).</summary>
        public static bool RealtimeCascadesEnabled
        {
            get { lock (_lock) return _realtimeCascades; }
        }

        /// <summary>Human-readable source for the current values (logging + status bar tooltip).</summary>
        public static string Source
        {
            get { lock (_lock) return _lastLoadedFrom; }
        }

        /// <summary>
        /// Apply defaults — called once on OnStartup before any document is open.
        /// </summary>
        public static void ApplyDefaults()
        {
            lock (_lock)
            {
                _offlineOnly = true;
                _realtimeCascades = true;
                _lastLoadedFrom = "(defaults)";
            }
            StingLog.Info($"StingOfflineConfig: defaults applied (OfflineOnly={_offlineOnly}, RealtimeCascades={_realtimeCascades})");
        }

        /// <summary>
        /// Merge a project-scoped override from &lt;bimDir&gt;/sting_config.json.
        /// Missing file, missing keys, or parse errors leave defaults untouched.
        /// </summary>
        public static void LoadFromProject(string bimDir)
        {
            if (string.IsNullOrEmpty(bimDir)) return;
            string path = Path.Combine(bimDir, "sting_config.json");
            if (!File.Exists(path)) return;

            try
            {
                var json = JObject.Parse(File.ReadAllText(path));
                bool offline = json.Value<bool?>("OfflineOnly") ?? _offlineOnly;
                bool realtime = json.Value<bool?>("RealtimeCascades") ?? _realtimeCascades;

                lock (_lock)
                {
                    _offlineOnly = offline;
                    _realtimeCascades = realtime;
                    _lastLoadedFrom = path;
                }
                StingLog.Info($"StingOfflineConfig: loaded from '{path}' (OfflineOnly={offline}, RealtimeCascades={realtime})");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingOfflineConfig: failed to parse '{path}' — keeping defaults. {ex.Message}");
            }
        }

        /// <summary>
        /// Gate helper for the four network-touching commands. Returns true when
        /// the command should refuse to run. Shows a TaskDialog explaining the
        /// flag and pointing at the local-export alternative.
        /// </summary>
        public static bool RefuseIfOffline(string commandName, string localAlternative = null)
        {
            if (!IsOffline) return false;

            var td = new TaskDialog("STING — Offline mode")
            {
                MainInstruction = $"{commandName} is blocked by offline mode.",
                MainContent =
                    "STING is configured offline (source: " + Source + "). " +
                    "Network commands (Planscape Connect, ACC Publish, SharePoint Export, Platform Sync) " +
                    "will refuse until you switch the project to online mode.\n\n" +
                    "To go online for this project, set \"OfflineOnly\": false in:\n" +
                    "  <project>/_BIM_COORD/sting_config.json\n\n" +
                    (string.IsNullOrEmpty(localAlternative)
                        ? "Offline alternatives: BCF export, transmittal bundle, document package — all in the BIM tab."
                        : "Offline alternative: " + localAlternative)
            };
            try { td.Show(); } catch { /* headless contexts */ }

            StingLog.Info($"StingOfflineConfig: refused '{commandName}' — offline mode active (source: {Source})");
            return true;
        }
    }
}
