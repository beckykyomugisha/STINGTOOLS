using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    /// <summary>
    /// Manages the ISO 19650 project folder structure for all STING outputs.
    /// Creates, maintains, and indexes a standardised directory tree linked
    /// to the Document Manager for browsing, deletion, renaming and drag-to-view.
    ///
    /// Root: {ProjectDir}/STING_Project/ (or user-chosen path)
    ///
    /// Folder tree:
    ///   01_WIP/               — Work in progress deliverables
    ///   02_SHARED/            — Shared for coordination
    ///   03_PUBLISHED/         — Approved deliverables
    ///   04_ARCHIVE/           — Superseded versions
    ///   05_MODELS/            — Native RVT, IFC, NWC exports
    ///   06_DRAWINGS/          — PDF drawings and sheet exports
    ///   07_SCHEDULES/         — Schedule CSV/XLSX exports
    ///   08_COBie/             — COBie V2.4 XLSX and CSV sheets
    ///   09_BEP/               — BIM Execution Plan documents
    ///   10_TRANSMITTALS/      — Transmittal records and packages
    ///   11_ISSUES/            — RFI, TQ, NCR, EWN and issue exports
    ///   12_CLASHES/           — Clash detection reports and BCF files
    ///   13_HANDOVER/          — FM handover, O&amp;M, maintenance, asset health
    ///   14_REVISIONS/         — Revision snapshots and comparison reports
    ///   15_REGISTERS/         — Document register, tag register, asset register CSV
    ///   16_COMPLIANCE/        — Compliance reports, validation audits
    ///   17_BRIEFCASE/         — Reference documents (BEP, specs, standards)
    ///   18_PHOTOS/            — Site photos and observations
    ///   19_CORRESPONDENCE/    — Letters, emails, meeting minutes
    ///   20_MISC/              — Uncategorised exports
    /// </summary>
    public static class ProjectFolderEngine
    {
        // ── Folder definitions ────────────────────────────────────────────
        public static readonly (string Id, string Name, string Description, string CDE)[] Folders = new[]
        {
            ("WIP",            "01_WIP",            "Work in progress deliverables",              "WIP"),
            ("SHARED",         "02_SHARED",         "Shared for coordination",                    "SHARED"),
            ("PUBLISHED",      "03_PUBLISHED",      "Approved deliverables",                      "PUBLISHED"),
            ("ARCHIVE",        "04_ARCHIVE",        "Superseded versions",                        "ARCHIVE"),
            ("MODELS",         "05_MODELS",         "Native RVT, IFC, NWC exports",               ""),
            ("DRAWINGS",       "06_DRAWINGS",       "PDF drawings and sheet exports",              ""),
            ("SCHEDULES",      "07_SCHEDULES",      "Schedule CSV/XLSX exports",                   ""),
            ("COBIE",          "08_COBie",          "COBie V2.4 XLSX and CSV sheets",              ""),
            ("BEP",            "09_BEP",            "BIM Execution Plan documents",                ""),
            ("TRANSMITTALS",   "10_TRANSMITTALS",   "Transmittal records and packages",            ""),
            ("ISSUES",         "11_ISSUES",         "RFI, TQ, NCR, EWN and issue exports",        ""),
            ("CLASHES",        "12_CLASHES",        "Clash detection reports and BCF files",       ""),
            ("HANDOVER",       "13_HANDOVER",       "FM handover, O&M, maintenance, asset health", ""),
            ("REVISIONS",      "14_REVISIONS",      "Revision snapshots and comparison reports",   ""),
            ("REGISTERS",      "15_REGISTERS",      "Document, tag, and asset register exports",   ""),
            ("COMPLIANCE",     "16_COMPLIANCE",      "Compliance reports and validation audits",    ""),
            ("BRIEFCASE",      "17_BRIEFCASE",      "Reference documents (BEP, specs, standards)", ""),
            ("PHOTOS",         "18_PHOTOS",         "Site photos and observations",                ""),
            ("CORRESPONDENCE", "19_CORRESPONDENCE", "Letters, emails, meeting minutes",            ""),
            ("MISC",           "20_MISC",           "Uncategorised exports",                       ""),
        };

        /// <summary>Maps export type keys to folder IDs for automatic routing.</summary>
        public static readonly Dictionary<string, string> ExportTypeToFolder = new(StringComparer.OrdinalIgnoreCase)
        {
            ["PDF"]            = "DRAWINGS",
            ["IFC"]            = "MODELS",
            ["NWC"]            = "MODELS",
            ["RVT"]            = "MODELS",
            ["COBie"]          = "COBIE",
            ["COBieStream"]    = "COBIE",
            ["BEP"]            = "BEP",
            ["Transmittal"]    = "TRANSMITTALS",
            ["Issue"]          = "ISSUES",
            ["RFI"]            = "ISSUES",
            ["BCF"]            = "CLASHES",
            ["Clash"]          = "CLASHES",
            ["Handover"]       = "HANDOVER",
            ["OandM"]          = "HANDOVER",
            ["Maintenance"]    = "HANDOVER",
            ["AssetHealth"]    = "HANDOVER",
            ["SpaceHandover"]  = "HANDOVER",
            ["Revision"]       = "REVISIONS",
            ["TagRegister"]    = "REGISTERS",
            ["DocRegister"]    = "REGISTERS",
            ["AssetRegister"]  = "REGISTERS",
            ["BOQ"]            = "SCHEDULES",
            ["Schedule"]       = "SCHEDULES",
            ["Excel"]          = "SCHEDULES",
            ["Compliance"]     = "COMPLIANCE",
            ["Validation"]     = "COMPLIANCE",
            ["ModelHealth"]    = "COMPLIANCE",
            ["Briefcase"]      = "BRIEFCASE",
            ["Photo"]          = "PHOTOS",
            ["Minutes"]        = "CORRESPONDENCE",
            ["Letter"]         = "CORRESPONDENCE",
        };

        private static string _rootPath;

        // ── Root path ─────────────────────────────────────────────────────

        /// <summary>Get or set the project folder root. Persisted to project_config.json.</summary>
        public static string RootPath
        {
            get => _rootPath;
            set { _rootPath = value; StingLog.Info($"ProjectFolderEngine root set: {value}"); }
        }

        /// <summary>
        /// Resolve the root path. Uses: config → project dir → user docs → temp.
        /// </summary>
        public static string GetRootPath(Document doc)
        {
            if (!string.IsNullOrEmpty(_rootPath) && Directory.Exists(_rootPath))
                return _rootPath;

            // Try project directory
            if (doc != null && !string.IsNullOrEmpty(doc.PathName))
            {
                string projDir = Path.GetDirectoryName(doc.PathName);
                if (!string.IsNullOrEmpty(projDir))
                {
                    string stingRoot = Path.Combine(projDir, "STING_Project");
                    try { Directory.CreateDirectory(stingRoot); _rootPath = stingRoot; return stingRoot; }
                    catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine: Cannot create at project dir: {ex.Message}"); }
                }
            }

            // Fallback to Documents
            string docsDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string fallback = Path.Combine(docsDir, "STING_Project");
            try { Directory.CreateDirectory(fallback); _rootPath = fallback; return fallback; }
            catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine: Documents fallback failed: {ex.Message}"); }

            return Path.GetTempPath();
        }

        // ── Folder creation ───────────────────────────────────────────────

        /// <summary>Create the full ISO 19650 folder structure.</summary>
        public static int CreateFolderStructure(Document doc)
        {
            string root = GetRootPath(doc);
            int created = 0;
            foreach (var (id, name, desc, _) in Folders)
            {
                string path = Path.Combine(root, name);
                if (!Directory.Exists(path))
                {
                    try { Directory.CreateDirectory(path); created++; }
                    catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine: Cannot create {name}: {ex.Message}"); }
                }
            }
            // Create sub-folders for CDE folders
            foreach (string cdeFolder in new[] { "01_WIP", "02_SHARED", "03_PUBLISHED" })
            {
                string cdePath = Path.Combine(root, cdeFolder);
                foreach (string disc in new[] { "A_Architectural", "M_Mechanical", "E_Electrical", "P_Plumbing", "S_Structural", "FP_Fire", "Z_General" })
                {
                    string discPath = Path.Combine(cdePath, disc);
                    if (!Directory.Exists(discPath))
                    {
                        try { Directory.CreateDirectory(discPath); created++; }
                        catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine: Cannot create {discPath}: {ex.Message}"); }
                    }
                }
            }

            // Create clash sub-folders
            string clashRoot = Path.Combine(root, "12_CLASHES");
            foreach (string sub in new[] { "BCF", "Reports", "Snapshots" })
            {
                string subPath = Path.Combine(clashRoot, sub);
                if (!Directory.Exists(subPath))
                {
                    try { Directory.CreateDirectory(subPath); created++; }
                    catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine: Cannot create clash sub: {ex.Message}"); }
                }
            }

            // Create issue sub-folders by type
            string issueRoot = Path.Combine(root, "11_ISSUES");
            foreach (string sub in new[] { "RFI", "TQ", "NCR", "EWN", "SI", "VO", "AI", "CVI", "CE", "DESIGN", "CLASH", "SNAGGING", "RFA", "PMI" })
            {
                string subPath = Path.Combine(issueRoot, sub);
                if (!Directory.Exists(subPath))
                {
                    try { Directory.CreateDirectory(subPath); created++; }
                    catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine: Cannot create issue sub: {ex.Message}"); }
                }
            }

            // Write folder index
            WriteFolderIndex(root);
            SaveRootToConfig();
            StingLog.Info($"ProjectFolderEngine: Created {created} folders at {root}");
            return created;
        }

        // ── Folder resolution ─────────────────────────────────────────────

        /// <summary>Get the path for a specific folder by ID.</summary>
        public static string GetFolderPath(Document doc, string folderId)
        {
            string root = GetRootPath(doc);
            var folder = Folders.FirstOrDefault(f => f.Id.Equals(folderId, StringComparison.OrdinalIgnoreCase));
            if (folder.Name == null) return Path.Combine(root, "20_MISC");
            string path = Path.Combine(root, folder.Name);
            if (!Directory.Exists(path))
            {
                try { Directory.CreateDirectory(path); }
                catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine.GetFolderPath: {ex.Message}"); }
            }
            return path;
        }

        /// <summary>Get the folder path for an export type key (e.g. "PDF", "COBie", "BCF").</summary>
        public static string GetExportFolder(Document doc, string exportTypeKey)
        {
            if (ExportTypeToFolder.TryGetValue(exportTypeKey, out string folderId))
                return GetFolderPath(doc, folderId);
            return GetFolderPath(doc, "MISC");
        }

        /// <summary>Get timestamped export path routed to the correct folder.</summary>
        public static string GetExportPath(Document doc, string exportTypeKey, string baseName, string extension)
        {
            string folder = GetExportFolder(doc, exportTypeKey);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(folder, $"{baseName}_{timestamp}{extension}");
        }

        // ── File inventory ────────────────────────────────────────────────

        /// <summary>
        /// Scan the folder structure and return all files with metadata.
        /// </summary>
        public static List<ProjectFile> GetAllFiles(Document doc)
        {
            var files = new List<ProjectFile>();
            string root = GetRootPath(doc);
            if (!Directory.Exists(root)) return files;

            foreach (var (id, name, desc, cde) in Folders)
            {
                string folderPath = Path.Combine(root, name);
                if (!Directory.Exists(folderPath)) continue;

                try
                {
                    foreach (string filePath in Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories))
                    {
                        var fi = new FileInfo(filePath);
                        if (fi.Name.StartsWith(".")) continue; // skip hidden/temp files

                        files.Add(new ProjectFile
                        {
                            FileName = fi.Name,
                            FilePath = fi.FullName,
                            FolderId = id,
                            FolderName = name,
                            Extension = fi.Extension.ToUpperInvariant().TrimStart('.'),
                            SizeBytes = fi.Length,
                            SizeDisplay = FormatSize(fi.Length),
                            Created = fi.CreationTime,
                            Modified = fi.LastWriteTime,
                            CDEStatus = !string.IsNullOrEmpty(cde) ? cde : "N/A",
                            RelativePath = filePath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar)
                        });
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ProjectFolderEngine.GetAllFiles: Error scanning {name}: {ex.Message}");
                }
            }

            return files.OrderByDescending(f => f.Modified).ToList();
        }

        /// <summary>Get files in a specific folder.</summary>
        public static List<ProjectFile> GetFilesInFolder(Document doc, string folderId)
        {
            return GetAllFiles(doc).Where(f => f.FolderId.Equals(folderId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>Get folder statistics.</summary>
        public static List<FolderStats> GetFolderStats(Document doc)
        {
            var stats = new List<FolderStats>();
            string root = GetRootPath(doc);
            if (!Directory.Exists(root)) return stats;

            foreach (var (id, name, desc, cde) in Folders)
            {
                string folderPath = Path.Combine(root, name);
                int fileCount = 0;
                long totalSize = 0;
                DateTime? lastModified = null;

                if (Directory.Exists(folderPath))
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(folderPath);
                        var allFiles = dirInfo.GetFiles("*.*", SearchOption.AllDirectories)
                            .Where(f => !f.Name.StartsWith(".")).ToArray();
                        fileCount = allFiles.Length;
                        totalSize = allFiles.Sum(f => f.Length);
                        if (allFiles.Length > 0)
                            lastModified = allFiles.Max(f => f.LastWriteTime);
                    }
                    catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine.GetFolderStats: {ex.Message}"); }
                }

                stats.Add(new FolderStats
                {
                    FolderId = id,
                    FolderName = name,
                    Description = desc,
                    FileCount = fileCount,
                    TotalSize = totalSize,
                    TotalSizeDisplay = FormatSize(totalSize),
                    LastModified = lastModified,
                    Exists = Directory.Exists(folderPath)
                });
            }
            return stats;
        }

        // ── File operations ───────────────────────────────────────────────

        /// <summary>Delete a file and log the activity.</summary>
        public static bool DeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    string name = Path.GetFileName(filePath);
                    File.Delete(filePath);
                    StingLog.Info($"ProjectFolderEngine: Deleted {filePath}");
                    LogActivity(null, "DELETE", name, filePath);
                    return true;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine.DeleteFile: {ex.Message}"); }
            return false;
        }

        /// <summary>Rename a file and log the activity.</summary>
        public static bool RenameFile(string filePath, string newName)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                string dir = Path.GetDirectoryName(filePath);
                string oldName = Path.GetFileName(filePath);
                string newPath = Path.Combine(dir, newName);
                if (File.Exists(newPath)) return false;
                File.Move(filePath, newPath);
                StingLog.Info($"ProjectFolderEngine: Renamed {oldName} → {newName}");
                LogActivity(null, "RENAME", newName, $"{oldName} -> {newName}");
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine.RenameFile: {ex.Message}"); }
            return false;
        }

        /// <summary>Move a file to a different folder. Auto-logs transmittal when target is CDE folder.</summary>
        public static bool MoveFile(Document doc, string filePath, string targetFolderId)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                string targetDir = GetFolderPath(doc, targetFolderId);
                string fileName = Path.GetFileName(filePath);
                string newPath = Path.Combine(targetDir, fileName);
                if (File.Exists(newPath)) newPath = GetUniqueFileName(newPath);
                File.Move(filePath, newPath);
                StingLog.Info($"ProjectFolderEngine: Moved {fileName} → {targetFolderId}");
                LogActivity(doc, "MOVE", fileName, $"→ {targetFolderId}");

                // AUTO-001: Auto-log transmittal when moving to CDE folders
                if (targetFolderId == "SHARED" || targetFolderId == "PUBLISHED")
                    AutoLogTransmittal(doc, new List<string> { newPath }, targetFolderId);

                return true;
            }
            catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine.MoveFile: {ex.Message}"); }
            return false;
        }

        /// <summary>Copy external file into the project folder structure with activity logging.</summary>
        public static string ImportFile(Document doc, string sourcePath, string targetFolderId)
        {
            try
            {
                if (!File.Exists(sourcePath)) return null;
                string targetDir = GetFolderPath(doc, targetFolderId);
                string fileName = Path.GetFileName(sourcePath);
                string targetPath = Path.Combine(targetDir, fileName);
                if (File.Exists(targetPath)) targetPath = GetUniqueFileName(targetPath);
                File.Copy(sourcePath, targetPath);
                StingLog.Info($"ProjectFolderEngine: Imported {fileName} → {targetFolderId}");
                LogActivity(doc, "IMPORT", fileName, $"→ {targetFolderId}");
                return targetPath;
            }
            catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine.ImportFile: {ex.Message}"); }
            return null;
        }

        // ── Index / config ────────────────────────────────────────────────

        private static void WriteFolderIndex(string root)
        {
            try
            {
                string indexPath = Path.Combine(root, "FOLDER_INDEX.txt");
                var lines = new List<string>
                {
                    "STING Project Folder Structure — ISO 19650",
                    $"Created: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    new string('=', 60),
                    ""
                };
                foreach (var (id, name, desc, cde) in Folders)
                {
                    string cdeLabel = !string.IsNullOrEmpty(cde) ? $" [CDE: {cde}]" : "";
                    lines.Add($"  {name,-25} {desc}{cdeLabel}");
                }
                lines.Add("");
                lines.Add("Generated by StingTools — https://stingbim.com");
                File.WriteAllLines(indexPath, lines);
            }
            catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine.WriteFolderIndex: {ex.Message}"); }
        }

        public static void LoadRootFromConfig()
        {
            try
            {
                string configPath = TagConfig.ConfigSource;
                if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath)) return;
                var config = JObject.Parse(File.ReadAllText(configPath));
                string root = config["PROJECT_FOLDER_ROOT"]?.ToString();
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                    _rootPath = root;
            }
            catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine.LoadRootFromConfig: {ex.Message}"); }
        }

        public static void SaveRootToConfig()
        {
            try
            {
                string configPath = TagConfig.ConfigSource;
                if (string.IsNullOrEmpty(configPath)) return;
                JObject config;
                if (File.Exists(configPath))
                    config = JObject.Parse(File.ReadAllText(configPath));
                else
                    config = new JObject();
                config["PROJECT_FOLDER_ROOT"] = _rootPath ?? "";
                File.WriteAllText(configPath, config.ToString(Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine.SaveRootToConfig: {ex.Message}"); }
        }

        // ── Helpers ───────────────────────────────────────────────────────

        internal static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        private static string GetUniqueFileName(string path)
        {
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            int counter = 1;
            string newPath;
            do
            {
                newPath = Path.Combine(dir, $"{name}_{counter}{ext}");
                counter++;
            } while (File.Exists(newPath) && counter < 999);
            return newPath;
        }

        // ── Data models ───────────────────────────────────────────────────

        public class ProjectFile
        {
            public string FileName { get; set; }
            public string FilePath { get; set; }
            public string FolderId { get; set; }
            public string FolderName { get; set; }
            public string Extension { get; set; }
            public long SizeBytes { get; set; }
            public string SizeDisplay { get; set; }
            public DateTime Created { get; set; }
            public DateTime Modified { get; set; }
            public string CDEStatus { get; set; }
            public string RelativePath { get; set; }
        }

        public class FolderStats
        {
            public string FolderId { get; set; }
            public string FolderName { get; set; }
            public string Description { get; set; }
            public int FileCount { get; set; }
            public long TotalSize { get; set; }
            public string TotalSizeDisplay { get; set; }
            public DateTime? LastModified { get; set; }
            public bool Exists { get; set; }
        }

        // ══════════════════════════════════════════════════════════════════
        //  AUTOMATION ENGINE — ISO 19650 naming, activity log, data drops
        // ══════════════════════════════════════════════════════════════════

        // ── ISO 19650 Naming Validator & Auto-Corrector ───────────────────

        /// <summary>
        /// Validate a filename against ISO 19650 naming convention.
        /// Format: Project-Originator-Volume-Level-Type-Role-Class_Subclass-Number
        /// Returns (isValid, suggestedName, errors).
        /// </summary>
        public static (bool IsValid, string Suggested, List<string> Errors) ValidateFileName(
            Document doc, string fileName)
        {
            var errors = new List<string>();
            string nameOnly = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            string[] parts = nameOnly.Split('-');

            // Need at least 6 segments
            if (parts.Length < 6)
            {
                errors.Add($"Expected 6+ segments separated by '-', got {parts.Length}");
                // Auto-suggest a compliant name
                string projCode = "PRJ";
                try { projCode = doc?.ProjectInformation?.Number ?? "PRJ"; } catch { }
                if (string.IsNullOrEmpty(projCode) || projCode.Length < 2) projCode = "PRJ";
                string suggested = $"{projCode}-ZZ-ZZ-XX-DR-Z-{nameOnly}{ext}";
                return (false, suggested, errors);
            }

            // Validate each segment
            string project = parts[0];
            string originator = parts[1];
            string volume = parts[2];
            string level = parts[3];
            string docType = parts[4];
            string role = parts.Length > 5 ? parts[5] : "Z";

            if (project.Length < 2 || project.Length > 6)
                errors.Add($"Project code '{project}' should be 2-6 chars");
            if (originator.Length < 1 || originator.Length > 6)
                errors.Add($"Originator '{originator}' should be 1-6 chars");
            if (!BIMManager.BIMManagerEngine.DocumentTypes.ContainsKey(docType.ToUpperInvariant()))
                errors.Add($"Document type '{docType}' not in ISO 19650 (valid: DR, SH, CA, M3, RP, etc.)");
            if (!BIMManager.BIMManagerEngine.RoleCodes.ContainsKey(role.ToUpperInvariant()))
                errors.Add($"Role code '{role}' not in ISO 19650 (valid: A, E, M, S, etc.)");

            bool valid = errors.Count == 0;
            return (valid, valid ? fileName : null, errors);
        }

        /// <summary>
        /// Auto-correct a filename to ISO 19650 format.
        /// </summary>
        public static string AutoCorrectFileName(Document doc, string fileName, string discipline = "Z",
            string docType = "DR", string suitability = "S3")
        {
            string ext = Path.GetExtension(fileName);
            string projCode = "PRJ";
            try
            {
                string num = doc?.ProjectInformation?.Number;
                if (!string.IsNullOrEmpty(num) && num.Length >= 2) projCode = num;
            }
            catch { }

            string originator = "ZZ";
            string volume = "ZZ";
            string level = "XX";
            string role = discipline.Length == 1 ? discipline : "Z";
            string number = DateTime.Now.ToString("HHmmss");

            return $"{projCode}-{originator}-{volume}-{level}-{docType}-{role}-{suitability}_{number}{ext}";
        }

        // ── Activity Log ──────────────────────────────────────────────────

        /// <summary>
        /// Log a document activity to the activity log file.
        /// </summary>
        public static void LogActivity(Document doc, string action, string docId,
            string details = "", string user = "")
        {
            try
            {
                string root = GetRootPath(doc);
                string logPath = Path.Combine(root, "ACTIVITY_LOG.jsonl");
                if (string.IsNullOrEmpty(user))
                {
                    try { user = doc?.Application?.Username ?? Environment.UserName; }
                    catch { user = Environment.UserName; }
                }

                var entry = new JObject
                {
                    ["timestamp"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    ["action"] = action,
                    ["doc_id"] = docId,
                    ["details"] = details,
                    ["user"] = user
                };

                File.AppendAllText(logPath, entry.ToString(Newtonsoft.Json.Formatting.None) + "\n");
            }
            catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine.LogActivity: {ex.Message}"); }
        }

        /// <summary>
        /// Get recent activity log entries.
        /// </summary>
        public static List<ActivityEntry> GetRecentActivity(Document doc, int maxEntries = 50)
        {
            var entries = new List<ActivityEntry>();
            try
            {
                string root = GetRootPath(doc);
                string logPath = Path.Combine(root, "ACTIVITY_LOG.jsonl");
                if (!File.Exists(logPath)) return entries;

                var lines = File.ReadAllLines(logPath);
                foreach (string line in lines.Reverse().Take(maxEntries))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var obj = JObject.Parse(line);
                        entries.Add(new ActivityEntry
                        {
                            Timestamp = obj["timestamp"]?.ToString() ?? "",
                            Action = obj["action"]?.ToString() ?? "",
                            DocId = obj["doc_id"]?.ToString() ?? "",
                            Details = obj["details"]?.ToString() ?? "",
                            User = obj["user"]?.ToString() ?? ""
                        });
                    }
                    catch { }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine.GetRecentActivity: {ex.Message}"); }
            return entries;
        }

        // ── Data Drop / Milestone Tracking ────────────────────────────────

        /// <summary>RIBA data drop milestones with COBie requirements.</summary>
        public static readonly (string Id, string Stage, string Description, string[] RequiredExports)[] DataDrops = new[]
        {
            ("DD1", "Stage 2 (Concept)", "Spatial programme, outline specification",
                new[] { "BEP", "TagRegister" }),
            ("DD2", "Stage 3 (Developed Design)", "Coordinated design with key specs",
                new[] { "BEP", "TagRegister", "COBie", "Schedule" }),
            ("DD3", "Stage 4 (Technical Design)", "Full technical design and contractor info",
                new[] { "BEP", "TagRegister", "COBie", "Schedule", "IFC", "PDF", "Clash" }),
            ("DD4", "Stage 5-6 (Construction/Handover)", "As-built data, O&M, commissioning",
                new[] { "BEP", "TagRegister", "COBie", "Schedule", "IFC", "PDF", "Handover", "Maintenance", "AssetHealth" }),
        };

        /// <summary>
        /// Check data drop readiness: which required exports exist in the folder structure.
        /// </summary>
        public static DataDropStatus CheckDataDropReadiness(Document doc, string dataDropId)
        {
            var dd = DataDrops.FirstOrDefault(d => d.Id == dataDropId);
            if (dd.Id == null) return null;

            var status = new DataDropStatus
            {
                DataDropId = dd.Id,
                Stage = dd.Stage,
                Description = dd.Description
            };

            foreach (string exportType in dd.RequiredExports)
            {
                string folder = GetExportFolder(doc, exportType);
                bool hasFiles = false;
                if (Directory.Exists(folder))
                {
                    try { hasFiles = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories).Length > 0; }
                    catch { }
                }
                status.Items.Add(new DataDropItem
                {
                    ExportType = exportType,
                    FolderPath = folder,
                    HasFiles = hasFiles,
                    Status = hasFiles ? "READY" : "MISSING"
                });
            }

            status.ReadyCount = status.Items.Count(i => i.HasFiles);
            status.TotalCount = status.Items.Count;
            status.ReadyPercent = status.TotalCount > 0 ? (double)status.ReadyCount / status.TotalCount * 100 : 0;
            return status;
        }

        /// <summary>Get readiness for all data drops.</summary>
        public static List<DataDropStatus> CheckAllDataDrops(Document doc)
        {
            return DataDrops.Select(dd => CheckDataDropReadiness(doc, dd.Id)).Where(s => s != null).ToList();
        }

        // ── Auto-Transmittal on CDE Publish ───────────────────────────────

        /// <summary>
        /// When files are moved to PUBLISHED or SHARED, auto-log a transmittal record.
        /// Called from BulkUpdateCDE and MoveFile when target is a CDE folder.
        /// </summary>
        public static void AutoLogTransmittal(Document doc, List<string> filePaths, string cdeStatus)
        {
            if (filePaths == null || filePaths.Count == 0) return;
            if (cdeStatus != "SHARED" && cdeStatus != "PUBLISHED") return;

            try
            {
                string bimDir = "";
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                    bimDir = Path.Combine(Path.GetDirectoryName(doc.PathName) ?? "", "STING_BIM_MANAGER");
                if (string.IsNullOrEmpty(bimDir) || !Directory.Exists(bimDir))
                {
                    try { Directory.CreateDirectory(bimDir); } catch { return; }
                }

                string transPath = Path.Combine(bimDir, "transmittals.json");
                JArray arr;
                if (File.Exists(transPath))
                    arr = JArray.Parse(File.ReadAllText(transPath));
                else
                    arr = new JArray();

                string transId = $"TR-{DateTime.Now:yyyyMMdd-HHmmss}";
                string user = "";
                try { user = doc?.Application?.Username ?? Environment.UserName; } catch { user = Environment.UserName; }

                var trans = new JObject
                {
                    ["transmittal_id"] = transId,
                    ["title"] = $"Auto-transmittal: {filePaths.Count} files → {cdeStatus}",
                    ["date"] = DateTime.Now.ToString("yyyy-MM-dd"),
                    ["status"] = "AUTO_GENERATED",
                    ["cde_status"] = cdeStatus,
                    ["recipient"] = "(auto-logged)",
                    ["created_by"] = user,
                    ["revision"] = "",
                    ["documents"] = new JArray(filePaths.Select(f => Path.GetFileName(f)))
                };
                arr.Add(trans);
                File.WriteAllText(transPath, arr.ToString(Newtonsoft.Json.Formatting.Indented));

                // Log activity
                LogActivity(doc, "AUTO_TRANSMITTAL", transId,
                    $"{filePaths.Count} files moved to {cdeStatus}");

                StingLog.Info($"ProjectFolderEngine: Auto-transmittal {transId} created for {filePaths.Count} files → {cdeStatus}");
            }
            catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine.AutoLogTransmittal: {ex.Message}"); }
        }

        // ── Clash Grouping ────────────────────────────────────────────────

        /// <summary>
        /// Group clash issues by discipline pair and severity.
        /// </summary>
        public static List<ClashGroup> GroupClashes(Document doc)
        {
            var groups = new List<ClashGroup>();
            try
            {
                string bimDir = "";
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                    bimDir = Path.Combine(Path.GetDirectoryName(doc.PathName) ?? "", "STING_BIM_MANAGER");
                string issuePath = Path.Combine(bimDir, "issues.json");
                if (!File.Exists(issuePath)) return groups;

                var arr = JArray.Parse(File.ReadAllText(issuePath));
                var clashes = arr.Where(i => i["type"]?.ToString() == "CLASH").ToList();

                // Group by discipline
                var byDisc = clashes.GroupBy(c =>
                {
                    string disc = c["discipline"]?.ToString() ?? "Z";
                    return disc;
                });

                foreach (var g in byDisc.OrderByDescending(x => x.Count()))
                {
                    int open = g.Count(c => c["status"]?.ToString() != "CLOSED");
                    int critical = g.Count(c => c["priority"]?.ToString() == "CRITICAL" || c["priority"]?.ToString() == "HIGH");
                    groups.Add(new ClashGroup
                    {
                        Discipline = g.Key,
                        TotalClashes = g.Count(),
                        OpenClashes = open,
                        CriticalClashes = critical,
                        ClashIds = g.Select(c => c["issue_id"]?.ToString() ?? "").ToList()
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine.GroupClashes: {ex.Message}"); }
            return groups;
        }

        // ── Additional data models ────────────────────────────────────────

        public class ActivityEntry
        {
            public string Timestamp { get; set; }
            public string Action { get; set; }
            public string DocId { get; set; }
            public string Details { get; set; }
            public string User { get; set; }
        }

        public class DataDropStatus
        {
            public string DataDropId { get; set; }
            public string Stage { get; set; }
            public string Description { get; set; }
            public int ReadyCount { get; set; }
            public int TotalCount { get; set; }
            public double ReadyPercent { get; set; }
            public List<DataDropItem> Items { get; set; } = new();
        }

        public class DataDropItem
        {
            public string ExportType { get; set; }
            public string FolderPath { get; set; }
            public bool HasFiles { get; set; }
            public string Status { get; set; }
        }

        public class ClashGroup
        {
            public string Discipline { get; set; }
            public int TotalClashes { get; set; }
            public int OpenClashes { get; set; }
            public int CriticalClashes { get; set; }
            public List<string> ClashIds { get; set; } = new();
        }
    }
}
