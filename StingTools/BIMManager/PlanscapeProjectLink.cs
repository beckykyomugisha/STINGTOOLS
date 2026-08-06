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
        /// <remarks>
        /// LEGACY FALLBACK ONLY. A bare model path carries no project root, so this
        /// resolves to the sibling <c>&lt;rvtDir&gt;\STING_BIM_MANAGER\</c> and NOT to the
        /// canonical <c>&lt;root&gt;\_data\STING_BIM_MANAGER\</c> bucket that every reader uses.
        /// <para>
        /// Writing through this path is how issue #570 happened: the BCC link button
        /// persisted the project id beside the .rvt while <c>PluginSyncTickBridge</c> and
        /// <c>SitePhotosTab</c> both read the canonical bucket, so a model that had just
        /// logged "linked to project a3af2ad2-..." reported "no Planscape project linked"
        /// five minutes later, every five minutes, forever.
        /// </para>
        /// <para>
        /// Prefer <see cref="ResolveConfigPath"/>, which upgrades to
        /// <see cref="ConfigPathFor(Document)"/> whenever the document is open. This
        /// overload remains only for genuinely document-less callers and for
        /// <see cref="Load(string)"/>'s legacy-sibling migration.
        /// </para>
        /// </remarks>
        public static string ConfigPathForModel(string? modelPath)
        {
            if (string.IsNullOrEmpty(modelPath)) return "";
            // path-discipline: legacy-fallback -- a bare model path carries no project
            // root, so this cannot resolve the canonical bucket. Callers holding a
            // Document must use ConfigPathFor(doc) or ResolveConfigPath(modelPath).
            string dir = Path.Combine(Path.GetDirectoryName(modelPath) ?? "", "STING_BIM_MANAGER");
            return Path.Combine(dir, ConfigFileName);
        }

        /// <summary>
        /// The config path a caller holding only a model path should use.
        ///
        /// Finds the open <see cref="Document"/> for that model and resolves the
        /// CANONICAL bucket through <see cref="ConfigPathFor(Document)"/>; only falls
        /// back to the legacy sibling when the document genuinely is not open. Callers
        /// that hold a Document should still call <see cref="ConfigPathFor(Document)"/>
        /// directly.
        /// </summary>
        public static string ResolveConfigPath(string? modelPath)
        {
            try
            {
                var app = StingTools.UI.StingCommandHandler.CurrentApp;
                var active = app?.ActiveUIDocument?.Document;

                // No model named: the caller means "the document in front of the user".
                if (string.IsNullOrEmpty(modelPath))
                    return active != null ? ConfigPathFor(active) : "";

                if (active != null && PathsEqual(active.PathName, modelPath))
                    return ConfigPathFor(active);

                // Not the active document - look through the rest before giving up,
                // so a BCC opened over a background document still resolves canonically.
                var docs = app?.Application?.Documents;
                if (docs != null)
                {
                    foreach (Document d in docs)
                    {
                        if (d != null && !d.IsFamilyDocument && PathsEqual(d.PathName, modelPath))
                            return ConfigPathFor(d);
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlanscapeProjectLink.ResolveConfigPath: {ex.Message}");
            }

            StingLog.Warn("PlanscapeProjectLink.ResolveConfigPath: no open document matched " +
                          $"'{modelPath}' - falling back to the legacy sibling path. The link " +
                          "will not be visible to readers that use the canonical bucket.");
            return ConfigPathForModel(modelPath);
        }

        private static bool PathsEqual(string? a, string? b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
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
            try
            {
                string canonical = ConfigPathFor(doc);
                info = Load(canonical);

                // Heal #570 in place. Models linked by an older build wrote the id to
                // the sibling path and nothing has ever read it there, so without this
                // they stay silently unlinked even after the write path is fixed. Only
                // adopt when the canonical bucket has no link of its own - a canonical
                // link is always authoritative over a legacy one.
                if (!info.IsLinked)
                {
                    var legacy = Load(ConfigPathForModel(doc.PathName));
                    if (legacy.IsLinked)
                    {
                        StingLog.Info($"PlanscapeProjectLink: adopting legacy sibling link {legacy.ProjectId} " +
                                      $"({legacy.Label}) for {doc.Title} and migrating it to the canonical bucket.");
                        Set(canonical, legacy.ProjectId, legacy.Name, legacy.Code);
                        info = legacy;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PlanscapeProjectLink.RestoreInto: {ex.Message}"); }

            PlanscapeServerClient.Instance.CurrentProjectId = info.ProjectId; // Empty when not linked
            if (info.IsLinked)
                StingLog.Info($"Planscape: restored project link {info.ProjectId} ({info.Label}) for {doc.Title}");
            return info;
        }
    }
}
