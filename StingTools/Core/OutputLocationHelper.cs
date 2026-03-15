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
        /// </summary>
        public static string GetOutputDirectory(Document doc = null)
        {
            // 1. User-preferred directory
            string dir = PreferredDirectory;
            if (!string.IsNullOrEmpty(dir) && TryEnsureDirectory(dir))
                return dir;

            // 2. Project directory / STING_Exports subfolder
            if (doc != null && !string.IsNullOrEmpty(doc.PathName))
            {
                string projDir = Path.GetDirectoryName(doc.PathName);
                if (!string.IsNullOrEmpty(projDir))
                {
                    string exportsDir = Path.Combine(projDir, "STING_Exports");
                    if (TryEnsureDirectory(exportsDir))
                        return exportsDir;
                    if (TryEnsureDirectory(projDir))
                        return projDir;
                }
            }

            // 3. Documents/STING_Exports
            string docsDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrEmpty(docsDir))
            {
                string stingDocsDir = Path.Combine(docsDir, "STING_Exports");
                if (TryEnsureDirectory(stingDocsDir))
                    return stingDocsDir;
            }

            // 4. Temp directory
            return Path.GetTempPath();
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
            catch
            {
                return false;
            }
        }
    }
}
