using System;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Text;
using Autodesk.Revit.UI;

namespace StingTools.Core
{
    /// <summary>
    /// Centralized output location management for all STING export/save operations.
    /// Fallback chain (GetOutputDirectory below is the authoritative version):
    ///   1. The unified project container (needs a SAVED project)
    ///   2. User-configured PreferredDirectory, if explicitly set
    ///   3. %LOCALAPPDATA%\\STING\\exports - stable, survives the Revit session
    ///   4. System temp, which under Revit is a per-session GUID folder
    ///
    /// There is deliberately NO Documents fallback - it was removed so exports stop
    /// sprawling into sibling folders. This list said otherwise for long enough that
    /// the user-facing dialog below was written from it, and told people their
    /// Documents folder had been tried when it never was.
    ///
    /// All export commands should use GetOutputPath() instead of hardcoding paths.
    /// Users can set their preferred directory once and all exports will use it.
    /// </summary>
    public static class OutputLocationHelper
    {
        private static string _preferredDirectory;
        private static readonly object _lock = new object();
        private static volatile bool _tempFallbackWarned;

        /// <summary>
        /// Get or set the user's preferred output directory.
        /// Persisted to project_config.json via ConfigEditorCommand.
        /// </summary>
        public static string PreferredDirectory
        {
            get { lock (_lock) { return _preferredDirectory; } }
            set
            {
                lock (_lock)
                {
                    _preferredDirectory = value;
                    StingLog.Info($"Output directory set to: {value}");
                }
            }
        }

        /// <summary>
        /// Resolve the best output directory using the fallback chain.
        /// Creates the directory if it doesn't exist.
        ///
        /// Resolution order:
        ///   1. Phase 167 unified project root (auto-bootstrapped if missing) → 20_MISC_<code>
        ///   2. User-configured PreferredDirectory (overrides only if explicitly set)
        ///   3. %LOCALAPPDATA%\\STING\\exports (stable across sessions)
        ///   4. System temp (with one-shot warning so the user notices)
        ///
        /// The legacy {projectDir}/STING_Exports/ and {Documents}/STING_Exports/
        /// fallbacks have been removed: every export now lands inside the single
        /// project container so the user no longer sees a sprawl of sibling
        /// folders next to the .rvt.
        /// </summary>
        public static string GetOutputDirectory(Document doc = null)
        {
            // 1. Phase 167 — unified project folder structure (auto-bootstrap if needed)
            try
            {
                if (doc != null)
                {
                    var setup = ProjectFolderEngine.LoadOrBootstrapSetup(doc);
                    if (setup != null)
                    {
                        string projRoot = ProjectFolderEngine.GetExportFolder(doc, "MISC");
                        if (!string.IsNullOrEmpty(projRoot) && TryEnsureDirectory(projRoot))
                            return projRoot;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetOutputDirectory setup lookup: {ex.Message}"); }

            // A cloud model with no recorded project root lands below in a per-user folder.
            // That is tolerable for a report the user is shown the path of, never for state
            // (GetStorePath refuses). Say which case this is, rather than "unsaved".
            try
            {
                if (doc != null && CloudProjectRootResolver.IsCloud(doc))
                    StingLog.Warn("GetOutputDirectory: cloud model has no recorded project root (ACC-HARD-3) — " +
                                  "this export goes to a per-user folder, not the shared project root. " +
                                  "Run 'Cloud project root' to set one.");
            }
            catch (Exception ex) { StingLog.Warn($"GetOutputDirectory cloud check: {ex.Message}"); }

            // 2. User-preferred directory (explicit override only)
            string dir = PreferredDirectory;
            if (!string.IsNullOrEmpty(dir) && TryEnsureDirectory(dir))
                return dir;

            // 3. A stable per-user folder. Reached whenever the project is unsaved,
            //    which is normal on a scratch model - and where this chain used to go
            //    straight to Path.GetTempPath(). Under Revit that is a PER-SESSION
            //    GUID folder (Temp\\b1f76786-...\\), so a report written there is
            //    orphaned the moment Revit closes: the path printed in the "done"
            //    dialog stops resolving, which reads as the export never happening.
            string stableDir = null;
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(localAppData))
                    stableDir = Path.Combine(localAppData, "STING", "exports");
            }
            catch (Exception ex) { StingLog.Warn($"OutputLocationHelper: LocalApplicationData lookup: {ex.Message}"); }

            if (!string.IsNullOrEmpty(stableDir) && TryEnsureDirectory(stableDir))
            {
                if (!_tempFallbackWarned)
                {
                    _tempFallbackWarned = true;
                    StingLog.Warn("OutputLocationHelper: no project container and no preferred directory - " +
                                  $"exports go to {stableDir}");
                    try
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("STING Export Location",
                            "This project has no STING output folder yet - usually because it has not " +
                            "been saved." + "\n\n" +
                            "Exports will be saved to:" + "\n" + stableDir + "\n\n" +
                            "Save the project to keep exports beside it, or use 'Set Output Directory' " +
                            "(BIM tab) to choose a permanent location.");
                    }
                    catch (Exception ex2) { StingLog.Warn($"TaskDialog may not be available outside Revit thread: {ex2.Message}"); }
                }
                return stableDir;
            }

            // 4. Temp directory (true last resort)
            string tempDir = Path.GetTempPath();
            StingLog.Warn("OutputLocationHelper: project container, preferred directory and the " +
                          "STING exports folder all failed. " +
                          $"Falling back to system temp: {tempDir}");

            // Check project_config.json for failOnOutputPathMissing flag
            try
            {
                string configPath = TagConfig.ConfigSource;
                if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    var config = Newtonsoft.Json.Linq.JObject.Parse(json);
                    bool failOnMissing = config["failOnOutputPathMissing"]?.ToObject<bool>() == true;
                    if (failOnMissing)
                        throw new InvalidOperationException(
                            "Output path could not be resolved and failOnOutputPathMissing is set. " +
                            "Configure OUTPUT_DIRECTORY in project_config.json.");
                }
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception ex) { StingLog.Warn($"config read failure is non-fatal: {ex.Message}"); }

            if (!_tempFallbackWarned)
            {
                _tempFallbackWarned = true;
                try
                {
                    // Names what was actually tried. The old text named the
                    // Documents folder, which this chain has never attempted.
                    Autodesk.Revit.UI.TaskDialog.Show("STING Export Location",
                        "Could not write to the project folder, the configured output directory, " +
                        "or " + (stableDir ?? "the STING exports folder") + "." + "\n\n" +
                        "Exports will be saved to the system temp folder:" + "\n" + tempDir + "\n" +
                        "Under Revit that folder is per-session, so move anything you want to keep." + "\n\n" +
                        "Use 'Set Output Directory' (BIM tab) to choose a permanent location.");
                }
                catch (Exception ex2) { StingLog.Warn($"TaskDialog may not be available outside Revit thread: {ex2.Message}"); }
            }
            return tempDir;
        }

        /// <summary>
        /// The project folder for an export type ("Excel" → 07_SCHEDULES, "COBie" →
        /// 08_COBie, "PDF" → 06_DRAWINGS …), optionally in a discipline's sub-folder.
        /// Falls back to <see cref="GetOutputDirectory(Document)"/> when the project has
        /// no folder structure (unsaved model).
        ///
        /// GetOutputDirectory(doc) has no idea what is being written, so everything
        /// that uses it lands in MISC. New and migrated exports should say what they
        /// are — and a round-trip's import picker must use the same key as its export,
        /// or it opens in the wrong folder.
        /// </summary>
        public static string GetRoutedDirectory(Document doc, string exportTypeKey, string discipline = null)
        {
            if (doc != null && !string.IsNullOrEmpty(exportTypeKey))
            {
                try
                {
                    string dir = !string.IsNullOrWhiteSpace(discipline)
                        ? ProjectFolderEngine.GetDeliverableFolder(doc, exportTypeKey, discipline)
                        : ProjectFolderEngine.GetExportFolder(doc, exportTypeKey);
                    if (!string.IsNullOrEmpty(dir) && TryEnsureDirectory(dir)) return dir;
                }
                catch (Exception ex) { StingLog.Warn($"GetRoutedDirectory '{exportTypeKey}': {ex.Message}"); }
            }
            return GetOutputDirectory(doc);
        }

        /// <summary>Copy a directory tree, creating folders as needed. Existing files in
        /// the destination are kept (the caller only copies into a fresh folder).</summary>
        internal static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from))
            {
                string dest = Path.Combine(to, Path.GetFileName(f));
                if (!File.Exists(dest)) File.Copy(f, dest);
            }
            foreach (string d in Directory.GetDirectories(from))
                CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
        }

        /// <summary>
        /// A plugin DATA STORE — a file the plugin writes and later reads back (clash
        /// results, logs, overrides) — under &lt;root&gt;/_data/coord/&lt;area&gt;/.
        ///
        /// These used to sit in the MISC export folder beside the user's reports, where a
        /// tidy-up deleted history and nothing marked them as state. On first use the old
        /// MISC copy (and any listed sibling folders, such as the clash <c>archive/</c>) is
        /// copied forward, then renamed <c>*.migrated_yyyyMMdd</c> — the convention
        /// Folders_Consolidate uses — so exactly one live copy exists and none is lost.
        /// An unsaved model has no _data folder and keeps the old location.
        /// </summary>
        public static string GetStorePath(Document doc, string fileName, string area = null,
            params string[] carrySiblingDirs)
        {
            string legacyDir = null;
            try { legacyDir = GetOutputDirectory(doc); }
            catch (Exception ex) { StingLog.Warn($"GetStorePath legacy dir: {ex.Message}"); }

            string target = string.IsNullOrEmpty(area)
                ? StingPaths.MetaFile(doc, "_BIM_COORD", fileName)
                : StingPaths.MetaFile(doc, "_BIM_COORD", area, fileName);
            if (string.IsNullOrEmpty(target))
            {
                // A cloud model with no recorded root (ACC-HARD-3): GetOutputDirectory would hand
                // back a per-user folder, and a DATA STORE there silently forks project state per
                // user. Refuse instead — callers already treat null as "no store".
                if (CloudProjectRootResolver.IsCloud(doc))
                {
                    StingLog.Warn($"GetStorePath({fileName}): cloud model has no project root — refusing to " +
                                  "place a data store in a per-user folder. Run 'Cloud project root'.");
                    return null;
                }
                return string.IsNullOrEmpty(legacyDir) ? null : Path.Combine(legacyDir, fileName);
            }

            try
            {
                string targetDir = Path.GetDirectoryName(target);
                Directory.CreateDirectory(targetDir);
                if (!string.IsNullOrEmpty(legacyDir) &&
                    !string.Equals(Path.GetFullPath(legacyDir).TrimEnd('\\', '/'),
                                   Path.GetFullPath(targetDir).TrimEnd('\\', '/'),
                                   StringComparison.OrdinalIgnoreCase))
                {
                    string stamp = ".migrated_" + DateTime.Now.ToString("yyyyMMdd");
                    string legacyFile = Path.Combine(legacyDir, fileName);
                    if (File.Exists(legacyFile) && !File.Exists(target))
                    {
                        File.Copy(legacyFile, target);
                        File.Move(legacyFile, legacyFile + stamp);
                        StingLog.Info($"Store carried forward: {legacyFile} -> {target}");
                    }
                    foreach (string sib in carrySiblingDirs ?? Array.Empty<string>())
                    {
                        string from = Path.Combine(legacyDir, sib);
                        string to = Path.Combine(targetDir, sib);
                        if (!Directory.Exists(from) || Directory.Exists(to)) continue;
                        // Whole tree, not just the top level: a sub-folder left behind would
                        // move with the renamed legacy folder and never be read again.
                        CopyTree(from, to);
                        Directory.Move(from, from + stamp);
                        StingLog.Info($"Store folder carried forward: {from} -> {to}");
                    }
                }
            }
            catch (Exception ex)
            {
                // A failed carry-forward must not lose the old data: it stays where it was
                // and is still readable from there by hand; the new store starts empty.
                StingLog.Warn($"GetStorePath carry-forward '{fileName}': {ex.Message}");
            }
            return target;
        }

        /// <summary><see cref="GetRoutedDirectory"/> + "baseName_yyyyMMdd_HHmmss.ext".</summary>
        public static string GetRoutedTimestampedPath(Document doc, string exportTypeKey,
            string baseName, string extension, string discipline = null)
            => Path.Combine(GetRoutedDirectory(doc, exportTypeKey, discipline),
                            $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}{extension}");

        /// <summary><see cref="GetRoutedDirectory"/> + a file name.</summary>
        public static string GetRoutedPath(Document doc, string exportTypeKey, string fileName, string discipline = null)
            => Path.Combine(GetRoutedDirectory(doc, exportTypeKey, discipline), fileName);

        /// <summary>
        /// Get the full output path for a named file.
        /// Example: GetOutputPath(doc, "STING_Tag_Audit.csv")
        /// </summary>
        public static string GetOutputPath(Document doc, string fileName)
        {
            return Path.Combine(GetOutputDirectory(doc), fileName);
        }

        /// <summary>
        /// Get the full output path with timestamp suffix.
        /// Example: GetOutputPath(doc, "STING_Validation", ".csv") → "STING_Validation_20260314_093045.csv"
        /// </summary>
        public static string GetTimestampedPath(Document doc, string baseName, string extension)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{baseName}_{timestamp}{extension}";
            return Path.Combine(GetOutputDirectory(doc), fileName);
        }

        /// <summary>
        /// Show a SaveFileDialog for user to choose export location.
        /// Falls back to GetOutputDirectory for the initial directory.
        /// Returns null if user cancels.
        /// </summary>
        public static string PromptForSaveLocation(Document doc, string title,
            string defaultFileName, string filter)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = title,
                FileName = defaultFileName,
                Filter = filter,
                InitialDirectory = GetOutputDirectory(doc)
            };
            if (dlg.ShowDialog() == true)
            {
                // Remember the directory for future exports
                string chosenDir = Path.GetDirectoryName(dlg.FileName);
                if (!string.IsNullOrEmpty(chosenDir))
                    PreferredDirectory = chosenDir;
                return dlg.FileName;
            }
            return null;
        }

        /// <summary>
        /// Load the preferred directory from project_config.json.
        /// Called during startup or when config is reloaded.
        /// </summary>
        public static void LoadFromConfig()
        {
            try
            {
                string configPath = TagConfig.ConfigSource;
                if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
                    return;
                string json = File.ReadAllText(configPath);
                var config = Newtonsoft.Json.Linq.JObject.Parse(json);
                string dir = config["OUTPUT_DIRECTORY"]?.ToString();
                if (!string.IsNullOrEmpty(dir))
                    PreferredDirectory = dir;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"OutputLocationHelper.LoadFromConfig: {ex.Message}");
            }
        }

        /// <summary>
        /// Save the preferred directory to project_config.json.
        /// </summary>
        public static void SaveToConfig()
        {
            try
            {
                string configPath = TagConfig.ConfigSource;
                if (string.IsNullOrEmpty(configPath)) return;

                Newtonsoft.Json.Linq.JObject config;
                if (File.Exists(configPath))
                    config = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(configPath));
                else
                    config = new Newtonsoft.Json.Linq.JObject();

                config["OUTPUT_DIRECTORY"] = PreferredDirectory ?? "";
                File.WriteAllText(configPath, config.ToString(Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"OutputLocationHelper.SaveToConfig: {ex.Message}");
            }
        }

        /// <summary>
        /// Show the user a dialog to set their preferred export directory.
        /// Uses a SaveFileDialog with a dummy filename to pick a folder (WPF-compatible).
        /// Returns the chosen directory, or null if cancelled.
        /// </summary>
        public static string PromptSetPreferredDirectory()
        {
            // Use SaveFileDialog to pick a directory (WPF-compatible, no WindowsForms needed)
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Choose folder for STING exports — save this dummy file to select the folder",
                FileName = "STING_EXPORTS_HERE",
                Filter = "Folder selection|*.*",
                CheckPathExists = true,
                OverwritePrompt = false
            };
            if (!string.IsNullOrEmpty(PreferredDirectory) && Directory.Exists(PreferredDirectory))
                dlg.InitialDirectory = PreferredDirectory;

            if (dlg.ShowDialog() == true)
            {
                string chosenDir = Path.GetDirectoryName(dlg.FileName);
                if (!string.IsNullOrEmpty(chosenDir))
                {
                    PreferredDirectory = chosenDir;
                    SaveToConfig();
                    return chosenDir;
                }
            }
            return null;
        }

        // FIX-5.1: Session-level folder memory per export type
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string>
            _sessionFolders = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// FIX-5.1: Per-export folder navigation with session memory.
        /// </summary>
        public static string PromptForExportPath(Document doc, string defaultFileName,
            string filter, string exportTypeKey = null)
        {
            _sessionFolders.TryGetValue(exportTypeKey ?? "", out string lastFolder);

            // The project's own folder for this export type (06_DRAWINGS for PDF,
            // 08_COBie for COBie …). The shortcut used to offer the directory holding
            // the .rvt — outside the project structure — and the browser opened in
            // MISC whatever was being exported.
            string routed = null;
            try
            {
                if (doc != null && !string.IsNullOrEmpty(exportTypeKey))
                    routed = ProjectFolderEngine.GetExportFolder(doc, exportTypeKey);
            }
            catch (Exception ex) { StingLog.Warn($"PromptForExportPath route '{exportTypeKey}': {ex.Message}"); }
            if (!string.IsNullOrEmpty(routed) && !TryEnsureDirectory(routed)) routed = null;

            if (!string.IsNullOrEmpty(lastFolder) && Directory.Exists(lastFolder))
            {
                var qd = new Autodesk.Revit.UI.TaskDialog($"Export — {defaultFileName}");
                qd.MainInstruction = "Choose export location";
                qd.MainContent = $"Last used: {lastFolder}";
                qd.AddCommandLink(Autodesk.Revit.UI.TaskDialogCommandLinkId.CommandLink1,
                    "Use last folder", lastFolder);
                qd.AddCommandLink(Autodesk.Revit.UI.TaskDialogCommandLinkId.CommandLink2,
                    "Navigate to folder", "Open file browser");
                string pd = routed ?? Path.GetDirectoryName(doc?.PathName ?? "");
                qd.AddCommandLink(Autodesk.Revit.UI.TaskDialogCommandLinkId.CommandLink3,
                    routed != null ? "Project folder for this export" : "Project folder",
                    string.IsNullOrEmpty(pd) ? "Save project first" : pd);
                qd.CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Cancel;
                switch (qd.Show())
                {
                    case Autodesk.Revit.UI.TaskDialogResult.CommandLink1:
                        return Path.Combine(lastFolder, defaultFileName);
                    case Autodesk.Revit.UI.TaskDialogResult.CommandLink3:
                        return string.IsNullOrEmpty(pd) ? null : Path.Combine(pd, defaultFileName);
                    case Autodesk.Revit.UI.TaskDialogResult.CommandLink2: break;
                    default: return null;
                }
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = $"Export — {defaultFileName}",
                FileName = defaultFileName,
                Filter = string.IsNullOrEmpty(filter) ? "All Files|*.*" : filter,
                InitialDirectory = routed ?? GetOutputDirectory(doc)
            };
            if (dlg.ShowDialog() != true) return null;

            string chosenDir = Path.GetDirectoryName(dlg.FileName);
            if (!string.IsNullOrEmpty(chosenDir))
            {
                PreferredDirectory = chosenDir;
                if (!string.IsNullOrEmpty(exportTypeKey))
                    _sessionFolders[exportTypeKey] = chosenDir;
            }
            return dlg.FileName;
        }

        private static bool TryEnsureDirectory(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                // Verify we can write
                string testFile = Path.Combine(path, ".sting_write_test");
                File.WriteAllText(testFile, "");
                File.Delete(testFile);
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }

        /// <summary>
        /// Atomic text write: writes to a temp file alongside, then uses
        /// File.Replace to swap it into place. A crash partway through
        /// leaves the previous file intact (or restored from the automatic
        /// .bak sibling). Several command registers and config writers
        /// previously did raw File.WriteAllText — losing the entire file
        /// if Revit crashed during the write window.
        /// </summary>
        public static void WriteAllTextAtomic(string path, string content,
            System.Text.Encoding encoding = null)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            encoding = encoding ?? new System.Text.UTF8Encoding(false);
            string tmp = path + ".tmp";
            string bak = path + ".bak";
            File.WriteAllText(tmp, content ?? string.Empty, encoding);
            try
            {
                if (File.Exists(path))
                    File.Replace(tmp, path, bak);
                else
                    File.Move(tmp, path);
            }
            catch (Exception)
            {
                // Replace can fail across volumes / on locked files — fall
                // back to a copy + delete so callers never end up with a
                // missing destination. If even copy fails let it propagate.
                File.Copy(tmp, path, true);
                try { File.Delete(tmp); } catch { }
            }
        }

        /// <summary>
        /// Canonical "make filename safe" helper — replaces every
        /// Path.GetInvalidFileNameChars() match (plus optional extras)
        /// with a single replacement char, trims repeated replacements,
        /// and clamps to maxLength. Three callers previously rolled their
        /// own version with subtle differences (BOQTemplateLibrary also
        /// replaced spaces + slashes; ParameterDiffEngine clamped at 50).
        /// Pass <paramref name="extraInvalid"/> / <paramref name="maxLength"/>
        /// to reproduce those behaviours when needed.
        /// </summary>
        public static string MakeSafeFileName(string name, char replacement = '_',
            char[] extraInvalid = null, int maxLength = 0, string fallback = "item")
        {
            if (string.IsNullOrEmpty(name)) return fallback;
            var invalid = Path.GetInvalidFileNameChars();
            var arr = new char[name.Length];
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool isInvalid = Array.IndexOf(invalid, c) >= 0
                    || (extraInvalid != null && Array.IndexOf(extraInvalid, c) >= 0);
                arr[i] = isInvalid ? replacement : c;
            }
            string r = new string(arr).Trim(replacement);
            if (string.IsNullOrEmpty(r)) r = fallback;
            if (maxLength > 0 && r.Length > maxLength) r = r.Substring(0, maxLength);
            return r;
        }
    }
}
