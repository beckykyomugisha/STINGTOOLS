// CloudProjectRootResolver.cs — the Revit-bound half of ACC-HARD-3.
//
// Reads the cloud identity of a Document (IsModelInCloud, GetCloudModelPath →
// GetProjectGUID / GetModelGUID), reads the machine mapping, asks CloudProjectRoot.Decide,
// and — only when the decision is PromptUser — calls the registered prompt. Every decision
// is logged (once per change, not once per path lookup). ProjectFolderEngine.GetRootPath
// calls this FIRST for a cloud model and returns null when it yields nothing: a cloud model
// never falls through to the <rvtDir>/<CODE> or Documents/<CODE> resolution.
//
// Local models never reach anything in this file beyond the IsCloud check.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Autodesk.Revit.DB;

namespace StingTools.Core
{
    /// <summary>What the prompt is told about the model it is choosing a root for.</summary>
    public sealed class CloudRootPromptRequest
    {
        public string Key;
        public string ModelDisplayPath;
        public string ProjectCode;
        public string Reason;
        /// <summary>The currently recorded folder, if any (unreachable or unusable).</summary>
        public string CurrentFolder;
    }

    /// <summary>Cloud identity of an open Document.</summary>
    public sealed class CloudModelInfo
    {
        public bool IsCloud;
        public string PathName;
        public string UserVisiblePath;
        public string ProjectGuid;
        public string ModelGuid;
        public string Key => CloudProjectRoot.KeyFor(ProjectGuid, ModelGuid);
    }

    public static class CloudProjectRootResolver
    {
        /// <summary>
        /// The interactive prompt: returns the folder the user picked (NOT yet the root — see
        /// CloudProjectRoot.RootForPickedFolder), or null when they declined. Registered by
        /// StingToolsApp.OnStartup; null means "never prompt" (e.g. headless hosts).
        /// </summary>
        public static Func<CloudRootPromptRequest, string> PromptHandler;

        /// <summary>Managed id of Revit's main thread, captured in OnStartup. 0 = unknown ⇒ never prompt.</summary>
        public static int UiThreadId;

        /// <summary>Override for the mapping file location (tests / diagnostics). Null ⇒ %APPDATA%.</summary>
        public static string MappingPathOverride;

        [ThreadStatic] private static int _suppressDepth;

        private static readonly HashSet<string> _promptedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> _rootByKey = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> _lastLogged = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, CloudRootDecision> _lastDecision = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _fileLock = new object();

        public static string MappingPath => MappingPathOverride ?? CloudProjectRoot.DefaultMappingPath();

        /// <summary>Suppress prompting on this thread for the scope (unattended workflow runs).</summary>
        public static IDisposable SuppressPrompts() => new Suppressor();

        private sealed class Suppressor : IDisposable
        {
            private bool _done;
            public Suppressor() { _suppressDepth++; }
            public void Dispose() { if (_done) return; _done = true; _suppressDepth--; }
        }

        /// <summary>True for a Revit cloud model (Autodesk Docs / BIM 360, incl. Cloud Worksharing).</summary>
        public static bool IsCloud(Document doc)
        {
            if (doc == null) return false;
            try { if (doc.IsModelInCloud) return true; }
            catch (Exception ex) { StingLog.Warn($"CloudProjectRootResolver.IsCloud: {ex.Message}"); }
            // Belt and braces: a PathName that is a cloud URL is never a usable local path,
            // whatever IsModelInCloud said.
            try { return CloudProjectRoot.LooksLikeCloudPath(doc.PathName); } catch { return false; }
        }

        /// <summary>Read the cloud identity. Never throws.</summary>
        public static CloudModelInfo Describe(Document doc)
        {
            var info = new CloudModelInfo();
            if (doc == null) return info;
            try { info.PathName = doc.PathName; } catch { }
            info.IsCloud = IsCloud(doc);
            if (!info.IsCloud) return info;
            try
            {
                ModelPath mp = doc.GetCloudModelPath();
                if (mp != null)
                {
                    try { info.ProjectGuid = mp.GetProjectGUID().ToString("D"); } catch (Exception ex) { StingLog.Warn($"Cloud GetProjectGUID: {ex.Message}"); }
                    try { info.ModelGuid = mp.GetModelGUID().ToString("D"); } catch (Exception ex) { StingLog.Warn($"Cloud GetModelGUID: {ex.Message}"); }
                    try { info.UserVisiblePath = ModelPathUtils.ConvertModelPathToUserVisiblePath(mp); } catch { }
                }
            }
            catch (Exception ex) { StingLog.Warn($"CloudProjectRootResolver.Describe: {ex.Message}"); }
            if (string.IsNullOrEmpty(info.UserVisiblePath)) info.UserVisiblePath = info.PathName;
            return info;
        }

        /// <summary>The most recent decision for a model path (diagnostics / the command).</summary>
        public static CloudRootDecision LastDecision(string pathName)
            => !string.IsNullOrEmpty(pathName) && _lastDecision.TryGetValue(pathName, out var d) ? d : null;

        /// <summary>Raw mapping text; null when the file does not exist. readError set on IO failure.</summary>
        public static string ReadMappingText(out string readError)
        {
            readError = null;
            try
            {
                string p = MappingPath;
                return File.Exists(p) ? File.ReadAllText(p) : null;
            }
            catch (Exception ex) { readError = ex.Message; return null; }
        }

        /// <summary>
        /// Resolve the root for a cloud model; null when unresolved (the decision says why).
        /// Returns null with Kind NotCloud for a local model — the caller continues with the
        /// local resolution, which this never touches.
        /// </summary>
        public static string TryResolve(Document doc, out CloudRootDecision decision, bool allowPrompt = true)
        {
            var info = Describe(doc);
            if (!info.IsCloud)
            {
                decision = CloudProjectRoot.Decide(new CloudRootInputs { IsCloud = false });
                return null;
            }

            string key = info.Key;
            if (key != null && _rootByKey.TryGetValue(key, out string cached) && Directory.Exists(cached))
            {
                decision = new CloudRootDecision { Kind = CloudRootDecisionKind.Mapped, Key = key, Root = cached, Reason = "mapped (cached)" };
                return cached;
            }

            string text = ReadMappingText(out string readError);
            if (readError != null)
            {
                decision = new CloudRootDecision
                {
                    Kind = CloudRootDecisionKind.Refuse, Key = key, MappingMalformed = true,
                    Reason = $"{MappingPath} could not be read: {readError}",
                };
                Record(info, decision);
                return null;
            }

            bool interactive;
            lock (_promptedKeys)
            {
                interactive = allowPrompt
                    && PromptHandler != null
                    && UiThreadId != 0
                    && Thread.CurrentThread.ManagedThreadId == UiThreadId
                    && _suppressDepth == 0
                    && key != null
                    && !_promptedKeys.Contains(key);
            }

            decision = CloudProjectRoot.Decide(new CloudRootInputs
            {
                IsCloud = true,
                PathName = info.UserVisiblePath ?? info.PathName,
                CloudProjectGuid = info.ProjectGuid,
                ModelGuid = info.ModelGuid,
                MappingJson = text,
                Interactive = interactive,
            });

            if (decision.Kind == CloudRootDecisionKind.Mapped)
            {
                _rootByKey[key] = decision.Root;
                Record(info, decision);
                return decision.Root;
            }

            if (decision.Kind == CloudRootDecisionKind.PromptUser)
            {
                lock (_promptedKeys) _promptedKeys.Add(key); // once per session, answered or not
                Record(info, decision);
                string root = PromptAndRecord(doc, info, decision);
                if (!string.IsNullOrEmpty(root))
                {
                    decision = new CloudRootDecision { Kind = CloudRootDecisionKind.Mapped, Key = key, Root = root, Reason = "chosen by the user and recorded" };
                    Record(info, decision);
                    return root;
                }
                decision = new CloudRootDecision
                {
                    Kind = CloudRootDecisionKind.Refuse, Key = key,
                    Reason = "the user declined to choose a project root; STING project files are unavailable " +
                             "for this model until one is set (run 'Cloud project root')",
                };
            }

            Record(info, decision);
            return null;
        }

        private static string PromptAndRecord(Document doc, CloudModelInfo info, CloudRootDecision decision)
        {
            string code = ProjectFolderEngine.DetectProjectCode(doc);
            string current = null;
            try
            {
                var map = CloudProjectRoot.ParseMapping(ReadMappingText(out _), out _);
                map?.TryGetValue(decision.Key, out current);
            }
            catch { }

            string picked;
            try
            {
                picked = PromptHandler(new CloudRootPromptRequest
                {
                    Key = decision.Key,
                    ModelDisplayPath = info.UserVisiblePath ?? info.PathName,
                    ProjectCode = code,
                    Reason = decision.Reason,
                    CurrentFolder = current,
                });
            }
            catch (Exception ex) { StingLog.Error("CloudProjectRootResolver prompt failed", ex); return null; }
            if (string.IsNullOrWhiteSpace(picked)) return null;

            string root = CloudProjectRoot.RootForPickedFolder(picked, code);
            if (!SetMapping(decision.Key, root, out string error))
            {
                StingLog.Error($"CloudProjectRootResolver: could not record root '{root}' for {decision.Key}: {error}");
                return null;
            }
            return root;
        }

        /// <summary>
        /// Record (or replace) the root for a key: creates the folder, rewrites the mapping
        /// atomically, preserves every other entry, and refuses to touch a malformed file.
        /// </summary>
        public static bool SetMapping(string key, string root, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(key)) { error = "no cloud project/model GUID to key the mapping by"; return false; }
            if (!CloudProjectRoot.IsUsableFolder(root, out string why)) { error = why; return false; }
            lock (_fileLock)
            {
                try
                {
                    string text = ReadMappingText(out string readError);
                    if (readError != null) { error = $"cannot read {MappingPath}: {readError}"; return false; }
                    string updated = CloudProjectRoot.WithMapping(text, key, root, out error);
                    if (updated == null) return false;

                    Directory.CreateDirectory(root);
                    WriteAtomic(updated);
                    _rootByKey[key] = root;
                    ProjectFolderEngine.InvalidateAllSetupCaches();
                    StingLog.Info($"CLOUD ROOT: recorded {key} → {root} in {MappingPath}");
                    return true;
                }
                catch (Exception ex) { error = ex.Message; return false; }
            }
        }

        /// <summary>Forget the root for a key (the model then prompts / refuses again).</summary>
        public static bool RemoveMapping(string key, out string error)
        {
            error = null;
            lock (_fileLock)
            {
                try
                {
                    string text = ReadMappingText(out string readError);
                    if (readError != null) { error = readError; return false; }
                    if (text == null) return true;
                    string updated = CloudProjectRoot.WithoutMapping(text, key, out error);
                    if (updated == null) return false;
                    WriteAtomic(updated);
                    _rootByKey.TryRemove(key ?? "", out _);
                    lock (_promptedKeys) _promptedKeys.Remove(key ?? "");
                    ProjectFolderEngine.InvalidateAllSetupCaches();
                    StingLog.Info($"CLOUD ROOT: removed mapping for {key}");
                    return true;
                }
                catch (Exception ex) { error = ex.Message; return false; }
            }
        }

        /// <summary>Allow the once-per-session prompt to run again for a key (the explicit command).</summary>
        public static void ResetPrompt(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_promptedKeys) _promptedKeys.Remove(key);
        }

        private static void WriteAtomic(string text)
        {
            string path = MappingPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        private static void Record(CloudModelInfo info, CloudRootDecision d)
        {
            string p = info.PathName ?? "<cloud>";
            _lastDecision[p] = d;
            string sig = d.Kind + "|" + d.Root + "|" + d.Reason;
            if (_lastLogged.TryGetValue(p, out string prev) && prev == sig) return;
            _lastLogged[p] = sig;
            string line = $"CLOUD ROOT [{info.UserVisiblePath ?? p}] project={info.ProjectGuid ?? "-"} model={info.ModelGuid ?? "-"}: {d}";
            if (d.Kind == CloudRootDecisionKind.Refuse) StingLog.Warn(line);
            else StingLog.Info(line);
        }
    }
}
