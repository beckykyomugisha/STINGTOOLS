using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace StingTools.Core.TemplateManager
{
    /// <summary>
    /// Pointer to the corporate template library. Stored at
    /// %APPDATA%/STING/corporate_library.json or pinned via the
    /// PRJ_CORPORATE_LIBRARY_PATH_TXT parameter on Project Information.
    /// </summary>
    public sealed class CorporateLibraryConfig
    {
        public string Version { get; set; } = "0.0.0";
        public string Path { get; set; } = "";        // network share / local seed .rvt / JSON folder
        public string Channel { get; set; } = "stable"; // stable | beta | dev
        public DateTime LastSynced { get; set; }
        public Dictionary<string, string> Tags { get; set; } = new();
    }

    /// <summary>
    /// Manages the corporate library reference + version stamping. Pull/push
    /// against a JSON folder structure first (Phase 18a); seed .rvt support
    /// stays a future enhancement when Revit doc-doc transactions are needed.
    /// </summary>
    public static class CorporateLibrary
    {
        private const string SettingsFileName = "corporate_library.json";
        private const string LibParam = "PRJ_CORPORATE_LIBRARY_PATH_TXT";
        private const string VerParam = "PRJ_CORPORATE_LIBRARY_VERSION_TXT";

        public static CorporateLibraryConfig LoadGlobal()
        {
            try
            {
                string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "STING", SettingsFileName);
                if (File.Exists(p))
                    return JsonConvert.DeserializeObject<CorporateLibraryConfig>(File.ReadAllText(p))
                        ?? new CorporateLibraryConfig();
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"CorporateLibrary.Load: {ex.Message}"); }
            return new CorporateLibraryConfig();
        }

        public static void SaveGlobal(CorporateLibraryConfig cfg)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "STING");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, SettingsFileName),
                    JsonConvert.SerializeObject(cfg, Formatting.Indented));
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"CorporateLibrary.Save: {ex.Message}"); }
        }

        /// <summary>Resolve the active library path for this doc — project param overrides global.</summary>
        public static string ResolveLibraryPath(Document doc)
        {
            try
            {
                if (doc?.ProjectInformation != null)
                {
                    var p = doc.ProjectInformation.LookupParameter(LibParam);
                    if (p != null && p.HasValue)
                    {
                        var v = p.AsString();
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                }
            }
            catch { }
            return LoadGlobal().Path ?? "";
        }

        /// <summary>Read the stamped library version, if any.</summary>
        public static string ResolveVersionStamp(Document doc)
        {
            try
            {
                if (doc?.ProjectInformation != null)
                {
                    var p = doc.ProjectInformation.LookupParameter(VerParam);
                    if (p != null && p.HasValue) return p.AsString();
                }
            }
            catch { }
            return "";
        }

        /// <summary>Stamp version onto the document (call inside a transaction).</summary>
        public static bool StampVersion(Document doc, string version)
        {
            try
            {
                var pi = doc?.ProjectInformation;
                if (pi == null) return false;
                var p = pi.LookupParameter(VerParam);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) return false;
                p.Set(version);
                return true;
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"CorporateLibrary.StampVersion: {ex.Message}"); return false; }
        }

        /// <summary>
        /// Pull the latest assignment-rules / weight profile JSONs from the
        /// configured library path. Currently a file-copy operation; future
        /// versions can pull from git or HTTP.
        /// </summary>
        public static List<string> PullJsonAssets(Document doc)
        {
            var pulled = new List<string>();
            string libPath = ResolveLibraryPath(doc);
            if (string.IsNullOrEmpty(libPath) || !Directory.Exists(libPath))
            {
                StingTools.Core.StingLog.Warn("CorporateLibrary.Pull: no library path configured");
                return pulled;
            }
            try
            {
                string projDir = Path.GetDirectoryName(doc?.PathName ?? "") ?? "";
                if (string.IsNullOrEmpty(projDir)) return pulled;
                string targetDir = Path.Combine(projDir, "_BIM_COORD");
                Directory.CreateDirectory(targetDir);
                foreach (string source in Directory.GetFiles(libPath, "*.json"))
                {
                    string name = Path.GetFileName(source);
                    string target = Path.Combine(targetDir, name);
                    File.Copy(source, target, overwrite: true);
                    pulled.Add(target);
                }
                TemplateRulesRegistry.Reload(doc);
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"CorporateLibrary.Pull: {ex.Message}"); }
            return pulled;
        }

        /// <summary>
        /// Push the project's override JSONs back to the corporate library
        /// (creates timestamped backups in the library before overwriting).
        /// </summary>
        public static List<string> PushJsonAssets(Document doc)
        {
            var pushed = new List<string>();
            string libPath = ResolveLibraryPath(doc);
            if (string.IsNullOrEmpty(libPath) || !Directory.Exists(libPath))
            {
                StingTools.Core.StingLog.Warn("CorporateLibrary.Push: no library path configured");
                return pushed;
            }
            try
            {
                string projDir = Path.GetDirectoryName(doc?.PathName ?? "") ?? "";
                string srcDir = Path.Combine(projDir, "_BIM_COORD");
                if (!Directory.Exists(srcDir)) return pushed;
                foreach (string source in Directory.GetFiles(srcDir, "*.json"))
                {
                    string name = Path.GetFileName(source);
                    string target = Path.Combine(libPath, name);
                    if (File.Exists(target))
                    {
                        string backupDir = Path.Combine(libPath, "_backups", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                        Directory.CreateDirectory(backupDir);
                        File.Copy(target, Path.Combine(backupDir, name), overwrite: true);
                    }
                    File.Copy(source, target, overwrite: true);
                    pushed.Add(target);
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"CorporateLibrary.Push: {ex.Message}"); }
            return pushed;
        }
    }
}
