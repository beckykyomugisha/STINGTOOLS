#nullable enable
using System;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.BIMManager
{
    /// <summary>
    /// Single source of truth for the per-Revit-document → Planscape server
    /// project link.
    ///
    /// The link is persisted into <c>{STING_BIM_MANAGER}/planscape_connection.json</c>
    /// beside the model (the same file the connection settings + the
    /// <see cref="PluginSyncTickBridge"/> already read), and mirrored onto the
    /// in-memory <see cref="PlanscapeServerClient.CurrentProjectId"/> so every
    /// consumer (invite, sync-tick payload build, BOQ sync, activity timelines)
    /// reads one consistent value.
    ///
    /// Two ways to obtain the config path:
    ///   * <see cref="ConfigPathFor(Document)"/> — when a live Document is in hand
    ///     (creates the directory). Resolves to the same file as
    ///     <see cref="BIMManagerEngine.GetBIMManagerDir(Document)"/>.
    ///   * <see cref="ConfigPathForModel(string)"/> — when only the .rvt path is
    ///     known (the BCC holds <c>CoordData.FilePath = doc.PathName</c>).
    ///
    /// Both resolve to <c>Path.GetDirectoryName(rvtPath)\STING_BIM_MANAGER\planscape_connection.json</c>.
    /// </summary>
    internal static class PlanscapeProjectLink
    {
        public const string ConfigFileName = "planscape_connection.json";

        /// <summary>Immutable snapshot of the persisted link.</summary>
        public readonly struct LinkInfo
        {
            public readonly Guid ProjectId;
            public readonly string Name;
            public readonly string Code;

            public LinkInfo(Guid id, string? name, string? code)
            {
                ProjectId = id;
                Name = name ?? "";
                Code = code ?? "";
            }

            public bool IsLinked => ProjectId != Guid.Empty;

            /// <summary>"Name (CODE)" — or "Name", or "" when unlinked.</summary>
            public string Label =>
                !IsLinked ? "" :
                string.IsNullOrWhiteSpace(Name)
                    ? (string.IsNullOrWhiteSpace(Code) ? ProjectId.ToString() : Code)
                    : (string.IsNullOrWhiteSpace(Code) ? Name : $"{Name} ({Code})");
        }

        /// <summary>Config path for a live document (creates the BIM-manager dir).</summary>
        public static string ConfigPathFor(Document doc)
            => Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), ConfigFileName);

        /// <summary>Config path derived from a model (.rvt) file path. No directory creation.</summary>
        public static string ConfigPathForModel(string? modelPath)
        {
            if (string.IsNullOrEmpty(modelPath)) return "";
            string dir = Path.Combine(Path.GetDirectoryName(modelPath) ?? "", "STING_BIM_MANAGER");
            return Path.Combine(dir, ConfigFileName);
        }

        /// <summary>Read the persisted link. Returns an empty <see cref="LinkInfo"/> when no file / no link.</summary>
        public static LinkInfo Load(string? configPath)
        {
            try
            {
                if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
                    return default;
                var json = JObject.Parse(File.ReadAllText(configPath));
                Guid.TryParse(json["projectId"]?.Value<string>(), out var id);
                return new LinkInfo(
                    id,
                    json["projectName"]?.Value<string>(),
                    json["projectCode"]?.Value<string>());
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlanscapeProjectLink.Load: {ex.Message}");
                return default;
            }
        }

        /// <summary>
        /// Persist the link to disk (preserving any existing serverUrl / email /
        /// lastConnected fields) AND set the in-memory
        /// <see cref="PlanscapeServerClient.CurrentProjectId"/>. Idempotent.
        /// </summary>
        public static void Set(string configPath, Guid projectId, string? name, string? code, string? email = null)
        {
            if (projectId == Guid.Empty) { Unlink(configPath); return; }
            try
            {
                JObject json = File.Exists(configPath)
                    ? JObject.Parse(File.ReadAllText(configPath))
                    : new JObject();

                json["projectId"]   = projectId.ToString();
                json["projectName"] = name ?? "";
                json["projectCode"] = code ?? "";

                // Backfill the connection fields so a link made before/without
                // an explicit "Save connection" still records where it points.
                if (string.IsNullOrWhiteSpace(json["serverUrl"]?.Value<string>())
                    && !string.IsNullOrWhiteSpace(PlanscapeServerClient.Instance.ServerUrl))
                    json["serverUrl"] = PlanscapeServerClient.Instance.ServerUrl;
                if (!string.IsNullOrWhiteSpace(email))
                    json["email"] = email;
                json["lastConnected"] = DateTime.UtcNow.ToString("o");

                var dir = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(configPath, json.ToString(Formatting.Indented));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlanscapeProjectLink.Set: {ex.Message}");
            }

            PlanscapeServerClient.Instance.CurrentProjectId = projectId;
            StingLog.Info($"Planscape: model linked to project {projectId} ({name} / {code})");
        }

        /// <summary>Remove the link from disk and clear the in-memory CurrentProjectId.</summary>
        public static void Unlink(string configPath)
        {
            try
            {
                if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
                {
                    var json = JObject.Parse(File.ReadAllText(configPath));
                    json.Remove("projectId");
                    json.Remove("projectName");
                    json.Remove("projectCode");
                    File.WriteAllText(configPath, json.ToString(Formatting.Indented));
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlanscapeProjectLink.Unlink: {ex.Message}");
            }

            PlanscapeServerClient.Instance.CurrentProjectId = Guid.Empty;
            StingLog.Info("Planscape: model unlinked from project");
        }

        /// <summary>
        /// Restore the persisted link for a freshly-opened document into the
        /// in-memory <see cref="PlanscapeServerClient.CurrentProjectId"/>. When
        /// the document is NOT linked, clears CurrentProjectId so a stale link
        /// from a previously-active document doesn't leak across project switches.
        /// Returns the link state for callers that want to display it.
        /// </summary>
        public static LinkInfo RestoreInto(Document doc)
        {
            LinkInfo info = default;
            try { info = Load(ConfigPathFor(doc)); }
            catch (Exception ex) { StingLog.Warn($"PlanscapeProjectLink.RestoreInto: {ex.Message}"); }

            PlanscapeServerClient.Instance.CurrentProjectId = info.ProjectId; // Empty when not linked
            if (info.IsLinked)
                StingLog.Info($"Planscape: restored project link {info.ProjectId} ({info.Label}) for {doc.Title}");
            return info;
        }
    }
}
