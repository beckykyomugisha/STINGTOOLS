// CloudProjectRoot.cs — where a CLOUD model's STING project root lives (ACC-HARD-3).
//
// WHY THIS EXISTS
//
// Every STING project path hangs off one root: <rvtDir>/<PROJECT_CODE>/ for a model on
// disk. A Revit cloud model (Autodesk Docs / BIM 360, including Cloud Worksharing) has
// no <rvtDir>: Document.PathName is "Autodesk Docs://<project>/<model>.rvt". Measured on
// .NET 8 (2026-09-30), Path.GetDirectoryName turns that into "Autodesk Docs:\<project>",
// CreateDirectory under it fails with "The filename, directory name, or volume label syntax
// is incorrect", and ProjectFolderEngine.GetRootPath then fell through to
// %USERPROFILE%\Documents\<CODE>. Every user got their own private root, so the issue
// register, the escalation record, acc_settings.json, SEQ sidecars and the audit log split
// per user and per machine — silently, because nothing failed.
//
// THE RULE
//
// A cloud model's root is an EXPLICIT choice recorded in a machine mapping
// (%APPDATA%\Planscape\cloud_project_roots.json, { "<ACC project GUID>": "<folder>" }).
// Keyed by the ACC project GUID so every model in the project shares one root; the model
// GUID is used only when the project GUID is unavailable. No mapping ⇒ ask (interactive)
// or refuse (unattended). Never a per-user default.
//
// This file is the pure decision: no Revit, no UI, no logging, no disk writes. The
// Revit-bound half is CloudProjectRootResolver; the prompt lives in
// Commands/Cloud/CloudProjectRootCommands.cs.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    /// <summary>What the resolver knows about the open model and this machine.</summary>
    public sealed class CloudRootInputs
    {
        /// <summary>Document.IsModelInCloud.</summary>
        public bool IsCloud;
        /// <summary>Document.PathName (for a cloud model a URL-like string, not a file path).</summary>
        public string PathName;
        /// <summary>ModelPath.GetProjectGUID() of the cloud model path, as text; null/empty when unavailable.</summary>
        public string CloudProjectGuid;
        /// <summary>ModelPath.GetModelGUID() of the cloud model path, as text; null/empty when unavailable.</summary>
        public string ModelGuid;
        /// <summary>Raw text of the mapping file; null when the file does not exist.</summary>
        public string MappingJson;
        /// <summary>True when a user can be asked right now (UI thread, not an unattended run).</summary>
        public bool Interactive;
        /// <summary>Directory probe; injected so the decision is testable. Null ⇒ Directory.Exists.</summary>
        public Func<string, bool> DirectoryExists;
    }

    public enum CloudRootDecisionKind
    {
        /// <summary>Not a cloud model — the local resolution applies, unchanged.</summary>
        NotCloud,
        /// <summary>A mapping exists and its folder is reachable: use <see cref="CloudRootDecision.Root"/>.</summary>
        Mapped,
        /// <summary>No usable mapping, and a user can be asked: prompt, then record the answer.</summary>
        PromptUser,
        /// <summary>No usable root and nobody to ask (or the mapping file is unreadable): fail, visibly.</summary>
        Refuse,
    }

    /// <summary>The outcome, with the reason in words a log line or dialog can carry.</summary>
    public sealed class CloudRootDecision
    {
        public CloudRootDecisionKind Kind;
        /// <summary>The root folder, when <see cref="Kind"/> is Mapped.</summary>
        public string Root;
        /// <summary>The mapping key this model resolves under ("&lt;project guid&gt;" or "model:&lt;guid&gt;").</summary>
        public string Key;
        /// <summary>Human-readable reason. Always set.</summary>
        public string Reason;
        /// <summary>True when the mapping file exists but cannot be parsed. Writers must not overwrite it.</summary>
        public bool MappingMalformed;

        public override string ToString() => $"{Kind} key={Key ?? "-"} root={Root ?? "-"} — {Reason}";
    }

    public static class CloudProjectRoot
    {
        /// <summary>Folder (under %APPDATA%) and file name of the machine mapping.</summary>
        public const string MappingFolderName = "Planscape";
        public const string MappingFileName = "cloud_project_roots.json";
        /// <summary>Prefix used when only the model GUID is available.</summary>
        public const string ModelKeyPrefix = "model:";

        /// <summary>%APPDATA%\Planscape\cloud_project_roots.json.</summary>
        public static string DefaultMappingPath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            MappingFolderName, MappingFileName);

        /// <summary>
        /// Heuristic for path-only callers that never see a Document: a cloud model path is
        /// "&lt;scheme&gt;://…" ("Autodesk Docs://", "BIM 360://", "cloud://"). A local path
        /// ("C:\…") or UNC path ("\\server\…") never contains "://".
        /// </summary>
        public static bool LooksLikeCloudPath(string path)
            => !string.IsNullOrEmpty(path) && path.IndexOf("://", StringComparison.Ordinal) > 0;

        /// <summary>Normalise a GUID-ish text to lower-case "D" format; null when blank or all-zero.</summary>
        public static string NormalizeGuid(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (Guid.TryParse(raw.Trim(), out Guid g))
                return g == Guid.Empty ? null : g.ToString("D");
            return null;
        }

        /// <summary>
        /// The mapping key: the ACC project GUID when known (all models in a project share one
        /// root), else "model:&lt;model GUID&gt;", else null.
        /// </summary>
        public static string KeyFor(string cloudProjectGuid, string modelGuid)
        {
            string p = NormalizeGuid(cloudProjectGuid);
            if (p != null) return p;
            string m = NormalizeGuid(modelGuid);
            return m != null ? ModelKeyPrefix + m : null;
        }

        /// <summary>
        /// Parse the mapping. Missing file (null text) or blank text ⇒ empty mapping, no error.
        /// Anything that is not a JSON object of string→string ⇒ error (the caller must refuse
        /// and must not overwrite the file — it may hold another project's root).
        /// </summary>
        public static Dictionary<string, string> ParseMapping(string json, out string error)
        {
            error = null;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (json == null || json.Trim().Length == 0) return map;
            try
            {
                var token = JToken.Parse(json);
                if (!(token is JObject obj))
                {
                    error = "the mapping file is not a JSON object";
                    return null;
                }
                foreach (var prop in obj.Properties())
                {
                    if (prop.Value.Type != JTokenType.String)
                    {
                        error = $"the value for '{prop.Name}' is not a string";
                        return null;
                    }
                    string key = prop.Name.Trim();
                    string norm = key.StartsWith(ModelKeyPrefix, StringComparison.OrdinalIgnoreCase)
                        ? (NormalizeGuid(key.Substring(ModelKeyPrefix.Length)) is string mg ? ModelKeyPrefix + mg : key)
                        : (NormalizeGuid(key) ?? key);
                    map[norm] = (string)prop.Value;
                }
                return map;
            }
            catch (JsonException ex)
            {
                error = "the mapping file is not valid JSON (" + ex.Message + ")";
                return null;
            }
        }

        /// <summary>
        /// A mapped folder is usable only when it is an absolute local or UNC path. A relative
        /// path would resolve against Revit's working directory (different per machine), and a
        /// cloud URL is exactly the thing this mapping exists to replace.
        /// </summary>
        public static bool IsUsableFolder(string folder, out string why)
        {
            why = null;
            if (string.IsNullOrWhiteSpace(folder)) { why = "the mapped folder is blank"; return false; }
            if (LooksLikeCloudPath(folder)) { why = $"'{folder}' is a cloud URL, not a folder on disk"; return false; }
            try
            {
                if (!Path.IsPathFullyQualified(folder)) { why = $"'{folder}' is not an absolute path"; return false; }
            }
            catch (Exception ex) { why = $"'{folder}' is not a valid path ({ex.Message})"; return false; }
            return true;
        }

        /// <summary>The decision. Pure: reads nothing but its inputs.</summary>
        public static CloudRootDecision Decide(CloudRootInputs i)
        {
            if (i == null || !i.IsCloud)
                return new CloudRootDecision
                {
                    Kind = CloudRootDecisionKind.NotCloud,
                    Reason = "not a cloud model — local resolution applies",
                };

            string key = KeyFor(i.CloudProjectGuid, i.ModelGuid);
            string where = string.IsNullOrEmpty(i.PathName) ? "this cloud model" : $"'{i.PathName}'";
            if (key == null)
                return new CloudRootDecision
                {
                    Kind = CloudRootDecisionKind.Refuse,
                    Reason = $"{where} reports neither a cloud project GUID nor a model GUID, so there is " +
                             "no stable key to record a project root under",
                };

            var map = ParseMapping(i.MappingJson, out string parseError);
            if (map == null)
                return new CloudRootDecision
                {
                    Kind = CloudRootDecisionKind.Refuse,
                    Key = key,
                    MappingMalformed = true,
                    Reason = $"{MappingFileName} cannot be read: {parseError}. Fix or remove the file; " +
                             "STING will not overwrite it",
                };

            var exists = i.DirectoryExists ?? Directory.Exists;
            string problem;
            if (map.TryGetValue(key, out string folder))
            {
                if (!IsUsableFolder(folder, out string why))
                    problem = $"the recorded root for {key} is unusable: {why}";
                else if (!exists(folder))
                    problem = $"the recorded root '{folder}' for {key} is not reachable on this machine " +
                              "(not synced, share offline, or drive letter differs)";
                else
                    return new CloudRootDecision
                    {
                        Kind = CloudRootDecisionKind.Mapped,
                        Key = key,
                        Root = folder,
                        Reason = $"mapped in {MappingFileName}",
                    };
            }
            else
            {
                problem = $"no project root is recorded for {where} (key {key})";
            }

            return new CloudRootDecision
            {
                Kind = i.Interactive ? CloudRootDecisionKind.PromptUser : CloudRootDecisionKind.Refuse,
                Key = key,
                Reason = i.Interactive
                    ? problem + " — asking the user to choose a shared folder"
                    : problem + " — nobody to ask (unattended), so STING refuses rather than write to a per-user folder",
            };
        }

        /// <summary>
        /// Given the folder a user picked, the root to record: the picked folder itself when it is
        /// already a STING root (named after the project code, or holding _data/project_setup.json),
        /// else &lt;picked&gt;/&lt;CODE&gt; — the same "&lt;parent&gt;/&lt;CODE&gt;" shape a local model gets.
        /// </summary>
        public static string RootForPickedFolder(string picked, string projectCode, Func<string, bool> fileExists = null)
        {
            if (string.IsNullOrWhiteSpace(picked)) return null;
            string trimmed = picked.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (trimmed.Length == 0) trimmed = picked;
            var fe = fileExists ?? File.Exists;
            if (fe(Path.Combine(trimmed, "_data", "project_setup.json"))) return trimmed;
            string leaf = Path.GetFileName(trimmed);
            if (!string.IsNullOrEmpty(projectCode) && string.Equals(leaf, projectCode, StringComparison.OrdinalIgnoreCase))
                return trimmed;
            return string.IsNullOrEmpty(projectCode) ? trimmed : Path.Combine(trimmed, projectCode);
        }

        /// <summary>
        /// Add or replace one entry and return the new file text. Refuses (returns null with an
        /// error) when the existing text is malformed — it may hold other projects' roots, and
        /// replacing it would lose them. Other entries are preserved.
        /// </summary>
        public static string WithMapping(string existingJson, string key, string folder, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(key)) { error = "no mapping key"; return null; }
            if (!IsUsableFolder(folder, out string why)) { error = why; return null; }
            var map = ParseMapping(existingJson, out string parseError);
            if (map == null) { error = $"{MappingFileName} cannot be read ({parseError}); not overwriting it"; return null; }
            map[key] = folder;
            var ordered = new SortedDictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
            return JsonConvert.SerializeObject(ordered, Formatting.Indented);
        }

        /// <summary>Remove one entry. Same malformed-file refusal as <see cref="WithMapping"/>.</summary>
        public static string WithoutMapping(string existingJson, string key, out string error)
        {
            error = null;
            var map = ParseMapping(existingJson, out string parseError);
            if (map == null) { error = $"{MappingFileName} cannot be read ({parseError}); not overwriting it"; return null; }
            if (!string.IsNullOrEmpty(key)) map.Remove(key);
            var ordered = new SortedDictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
            return JsonConvert.SerializeObject(ordered, Formatting.Indented);
        }
    }
}
