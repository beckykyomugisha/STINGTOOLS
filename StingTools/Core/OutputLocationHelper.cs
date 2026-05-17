using System;
using System.IO;
using Autodesk.Revit.DB;

namespace StingTools.Core
{
    /// <summary>
    /// Centralized output location management for all STING export/save operations.
    /// Provides a user-preferred save directory with fallback chain:
    ///   1. User-configured preferred directory (set via SetPreferredDirectory)
    ///   2. Project directory (alongside .rvt file)
    ///   3. STING_Exports subdirectory in user's Documents
    ///   4. System temp directory
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
        ///   3. System temp (with one-shot warning so the user notices)
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

            // 2. User-preferred directory (explicit override only)
            string dir = PreferredDirectory;
            if (!string.IsNullOrEmpty(dir) && TryEnsureDirectory(dir))
                return dir;

            // 3. Temp directory (last resort — warn so user knows exports are not in a project folder)
            string tempDir = Path.GetTempPath();
            StingLog.Warn($"OutputLocationHelper: All preferred directories failed. Falling back to system temp: {tempDir}");

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
                    Autodesk.Revit.UI.TaskDialog.Show("STING Export Location",
                        "Could not write to the project directory or Documents folder.\n\n" +
                        $"Exports will be saved to the system temp folder:\n{tempDir}\n\n" +
                        "Use 'Set Output Directory' (BIM tab) to choose a permanent location.");
                }
                catch (Exception ex) { StingLog.Warn($"TaskDialog may not be available outside Revit thread: {ex.Message}"); }
            }
            return tempDir;
        }

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

            if (!string.IsNullOrEmpty(lastFolder) && Directory.Exists(lastFolder))
            {
                var qd = new Autodesk.Revit.UI.TaskDialog($"Export — {defaultFileName}");
                qd.MainInstruction = "Choose export location";
                qd.MainContent = $"Last used: {lastFolder}";
                qd.AddCommandLink(Autodesk.Revit.UI.TaskDialogCommandLinkId.CommandLink1,
                    "Use last folder", lastFolder);
                qd.AddCommandLink(Autodesk.Revit.UI.TaskDialogCommandLinkId.CommandLink2,
                    "Navigate to folder", "Open file browser");
                string pd = Path.GetDirectoryName(doc?.PathName ?? "");
                qd.AddCommandLink(Autodesk.Revit.UI.TaskDialogCommandLinkId.CommandLink3,
                    "Project folder", string.IsNullOrEmpty(pd) ? "Save project first" : pd);
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
                InitialDirectory = GetOutputDirectory(doc)
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
