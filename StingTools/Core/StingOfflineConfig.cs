// Pack 0 — Online/Offline posture manager.
//
// STING is built to work equally well online and offline. Both modes are
// first-class:
//
//   * Online (default) — every command works, including the four that
//     touch the network: PlanscapeConnect, ACCPublish, SharePointExport,
//     PlatformSync. Live sync scheduler + APS webhooks (Pack 13) + the
//     Automation API headless pipeline (Pack 14) are all available.
//
//   * Offline (opt-in)  — for air-gapped projects, secure-estate work, MOD
//     engagements, or sensitive client sites where network egress is not
//     permitted. The four network commands refuse with a clear TaskDialog
//     pointing at the offline-equivalent workflow (BCF export, transmittal
//     bundle, document package). Every other command — tagging, placement,
//     validation, compliance, drawing production, family injection — is
//     naturally offline because it has no network code path, so nothing
//     else changes.
//
// Source of truth per project: <project>/_BIM_COORD/sting_config.json
//   {
//     "OfflineOnly": false,         // default — all features enabled
//     "RealtimeCascades": true      // DocumentChanged cascades (Pack 7)
//   }
//
// Set "OfflineOnly": true only in projects where network access is
// prohibited. The flag is per-project so a firm can have online projects
// (normal work) and offline projects (secure estate) on the same machine.
//
// Load order:
//   * OnStartup           — defaults applied (no doc open yet, nothing to read)
//   * OnDocumentOpened    — project config loaded; dock-panel indicator updated
//
// When a network command refuses it is loud — TaskDialog + StingLog — so
// users never wonder why a button did nothing.

using System;
using System.IO;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    public static class StingOfflineConfig
    {
        private static readonly object _lock = new object();
        // Default: ONLINE. Offline is opt-in per project via sting_config.json.
        private static bool _offlineOnly = false;
        private static bool _realtimeCascades = true;
        private static string _lastLoadedFrom = "(defaults)";

        /// <summary>
        /// True when the four network commands should refuse to run. False
        /// in the default configuration — every command is available.
        /// </summary>
        public static bool IsOffline
        {
            get { lock (_lock) return _offlineOnly; }
        }

        /// <summary>Convenience inverse — matches the default posture.</summary>
        public static bool IsOnline => !IsOffline;

        /// <summary>DocumentChanged cascade enable — read by Pack 7.</summary>
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
        /// Defaults to online so a brand-new install has every feature working;
        /// per-project opt-in flips offline on for air-gapped work.
        /// </summary>
        public static void ApplyDefaults()
        {
            lock (_lock)
            {
                _offlineOnly = false;
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
                StingLog.Warn($"StingOfflineConfig: failed to parse '{path}' — keeping current values. {ex.Message}");
            }
        }

        /// <summary>
        /// Persist the current in-memory state to &lt;bimDir&gt;/sting_config.json.
        /// Used by the dock-panel toggle so users can flip modes without
        /// hand-editing JSON. Preserves any other keys already in the file.
        /// </summary>
        public static bool SaveToProject(string bimDir)
        {
            if (string.IsNullOrEmpty(bimDir)) return false;
            try
            {
                Directory.CreateDirectory(bimDir);
                string path = Path.Combine(bimDir, "sting_config.json");
                JObject json;
                try
                {
                    json = File.Exists(path)
                        ? JObject.Parse(File.ReadAllText(path))
                        : new JObject();
                }
                catch { json = new JObject(); }

                lock (_lock)
                {
                    json["OfflineOnly"] = _offlineOnly;
                    json["RealtimeCascades"] = _realtimeCascades;
                    _lastLoadedFrom = path;
                }
                File.WriteAllText(path, json.ToString(Newtonsoft.Json.Formatting.Indented));
                StingLog.Info($"StingOfflineConfig: saved to '{path}' (OfflineOnly={_offlineOnly})");
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingOfflineConfig.SaveToProject: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Toggle offline mode at runtime and persist to the project config.
        /// Safe to call from the dock-panel badge click.
        /// </summary>
        public static void SetOffline(bool offline, string bimDir = null)
        {
            lock (_lock) { _offlineOnly = offline; }
            if (!string.IsNullOrEmpty(bimDir)) SaveToProject(bimDir);
            StingLog.Info($"StingOfflineConfig.SetOffline({offline}) — persisted to {bimDir ?? "(memory only)"}");
        }

        /// <summary>
        /// Gate helper for the four network-touching commands. Returns true
        /// when the command should refuse to run. In the default (online)
        /// posture this is a hot no-op — no dialog, no log line, no overhead.
        /// Only fires when the project has explicitly opted into offline mode.
        /// </summary>
        public static bool RefuseIfOffline(string commandName, string localAlternative = null)
        {
            if (!IsOffline) return false;

            var td = new TaskDialog("STING — Offline project mode")
            {
                MainInstruction = $"{commandName} is disabled for this project.",
                MainContent =
                    "This project has been set to offline mode " +
                    "(source: " + Source + "). Network-touching commands " +
                    "— Planscape Connect, ACC Publish, SharePoint Export, " +
                    "and Platform Sync — are disabled so STING never contacts " +
                    "external services for this project.\n\n" +
                    "To go online for this project, set \"OfflineOnly\": false in:\n" +
                    "  <project>/_BIM_COORD/sting_config.json\n\n" +
                    (string.IsNullOrEmpty(localAlternative)
                        ? "Offline-equivalent workflows: BCF export, transmittal bundle, document package — all in the BIM tab. Every other STING command works normally."
                        : "Offline-equivalent workflow: " + localAlternative)
            };
            try { td.Show(); } catch { /* headless contexts */ }

            StingLog.Info($"StingOfflineConfig: refused '{commandName}' — project is in offline mode (source: {Source})");
            return true;
        }
    }
}
