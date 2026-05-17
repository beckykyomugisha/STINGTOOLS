using System;
using System.Collections.Concurrent;
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

        // ── Phase 167: Per-document ProjectSetup cache ────────────────────
        private static readonly ConcurrentDictionary<string, ProjectSetup> _setupCache = new();

        /// <summary>Drop the cached setup for a specific document (call on close).</summary>
        public static void InvalidateSetupCache(string docPath)
        {
            if (string.IsNullOrEmpty(docPath)) return;
            _setupCache.TryRemove(docPath, out _);
        }

        /// <summary>Drop all cached setups.</summary>
        public static void InvalidateAllSetupCaches() => _setupCache.Clear();

        // PERF-02: Folder stats cache
        private static List<FolderStats> _folderStatsCache;
        private static DateTime _folderStatsCacheTime = DateTime.MinValue;
        private static readonly TimeSpan FolderStatsCacheDuration = TimeSpan.FromSeconds(10);

        /// <summary>Invalidate the folder stats cache (call after file operations).</summary>
        public static void InvalidateFolderStatsCache() { _folderStatsCacheTime = DateTime.MinValue; }

        // CONFIG-02: Configurable discipline list
        private static string[] _disciplineFolders = new[]
        {
            "A_Architectural", "M_Mechanical", "E_Electrical",
            "P_Plumbing", "S_Structural", "FP_Fire", "Z_General"
        };

        /// <summary>Set custom discipline folder names from config.</summary>
        public static void SetDisciplineFolders(string[] folders)
        {
            if (folders != null && folders.Length > 0) _disciplineFolders = folders;
        }

        // ── Allowed file extensions for import validation (OP-002) ──
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            "PDF", "XLSX", "XLS", "CSV", "JSON", "TXT", "XML", "DWG", "DXF", "DGN",
            "RVT", "RFA", "RTE", "RFT", "IFC", "NWC", "NWD", "NWF", "BCF", "BCFZIP",
            "DOC", "DOCX", "PPT", "PPTX", "JPG", "JPEG", "PNG", "BMP", "TIFF", "TIF",
            "MP4", "AVI", "MOV", "ZIP", "7Z", "RAR"
        };

        // ── Root path ─────────────────────────────────────────────────────

        /// <summary>Get or set the project folder root. Persisted to project_config.json.</summary>
        public static string RootPath
        {
            get => _rootPath;
            set { _rootPath = value; StingLog.Info($"ProjectFolderEngine root set: {value}"); }
        }

        /// <summary>
        /// Resolve the root path. Uses: ProjectSetup → explicit RootPath → project dir → user docs → temp.
        /// </summary>
        public static string GetRootPath(Document doc)
        {
            // Phase 167: Honour persisted ProjectSetup first
            try
            {
                var setup = LoadOrDetectSetup(doc);
                if (setup != null && doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string resolved = setup.ResolveRootPath(doc.PathName);
                    if (!string.IsNullOrEmpty(resolved))
                    {
                        try { Directory.CreateDirectory(resolved); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        if (Directory.Exists(resolved)) return resolved;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetRootPath setup lookup: {ex.Message}"); }

            if (!string.IsNullOrEmpty(_rootPath) && Directory.Exists(_rootPath))
                return _rootPath;

            // Try project directory: name root after the project code so the
            // user sees one container per project (e.g. FIRESTONE_LIBERIA/),
            // not a generic "STING_Project" sibling.
            if (doc != null && !string.IsNullOrEmpty(doc.PathName))
            {
                string projDir = Path.GetDirectoryName(doc.PathName);
                if (!string.IsNullOrEmpty(projDir))
                {
                    string code = DetectProjectCode(doc);
                    string stingRoot = Path.Combine(projDir, code);
                    try { Directory.CreateDirectory(stingRoot); _rootPath = stingRoot; return stingRoot; }
                    catch (Exception ex2) { StingLog.Warn($"ProjectFolderEngine: Cannot create at project dir: {ex2.Message}"); }
                }
            }

            // Fallback to Documents/<code> when project dir is unwritable
            string docsDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string codeDocs = doc != null ? DetectProjectCode(doc) : "STING_Project";
            string fallback = Path.Combine(docsDir, codeDocs);
            try { Directory.CreateDirectory(fallback); _rootPath = fallback; return fallback; }
            catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine: Documents fallback failed: {ex.Message}"); }

            return Path.GetTempPath();
        }

        // ══════════════════════════════════════════════════════════════════
        //  Phase 167 — Unified project folder system
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Detect or load the persisted ProjectSetup for this document.
        /// Returns null if the user hasn't run the setup wizard yet.
        /// </summary>
        public static ProjectSetup LoadOrDetectSetup(Document doc)
        {
            if (doc == null || string.IsNullOrEmpty(doc.PathName)) return null;
            string docKey = doc.PathName;
            if (_setupCache.TryGetValue(docKey, out var cached) && cached != null) return cached;

            try
            {
                string projDir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(projDir)) return null;

                // Look for {projDir}/{ProjectCode}/_data/project_setup.json
                string code = DetectProjectCode(doc);
                string candidateRoot = Path.Combine(projDir, code);
                string candidateData = Path.Combine(candidateRoot, "_data");
                var setup = ProjectSetup.Load(candidateData);
                if (setup != null)
                {
                    _setupCache[docKey] = setup;
                    return setup;
                }

                // FOLDER-06: Multi-model workspace — check sibling .rvt files with same project code prefix.
                // Sort alphabetically so the first (lowest name) is always the root anchor.
                try
                {
                    string rvtCode = DetectProjectCode(doc);
                    string prefix8 = rvtCode.Length >= 8 ? rvtCode.Substring(0, 8) : rvtCode;
                    var siblings = Directory.GetFiles(projDir, "*.rvt")
                        .Where(f => !string.Equals(f, doc.PathName, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f => f)
                        .ToList();
                    foreach (var sibling in siblings)
                    {
                        string sibName = Path.GetFileNameWithoutExtension(sibling);
                        string sibPre8 = sibName.Length >= 8 ? sibName.Substring(0, 8) : sibName;
                        if (string.Equals(sibPre8, prefix8, StringComparison.OrdinalIgnoreCase))
                        {
                            // These models share a project — root is anchored to first alphabetically
                            string rootSibling = siblings[0];
                            string rootDir = Path.GetDirectoryName(rootSibling);
                            string rootCode = SanitizeCode(Path.GetFileNameWithoutExtension(rootSibling));
                            string sharedData = Path.Combine(rootDir, rootCode, "_data");
                            var sharedSetup = ProjectSetup.Load(sharedData);
                            if (sharedSetup != null)
                            {
                                StingLog.Info($"LoadOrDetectSetup: {Path.GetFileName(doc.PathName)} shares root with {Path.GetFileName(rootSibling)} → {sharedData}");
                                _setupCache[docKey] = sharedSetup;
                                return sharedSetup;
                            }
                        }
                    }
                }
                catch (Exception exSib) { StingLog.Warn($"LoadOrDetectSetup sibling scan: {exSib.Message}"); }

                // Also search any sibling folder containing _data/project_setup.json
                try
                {
                    foreach (var sub in Directory.GetDirectories(projDir))
                    {
                        string sd = Path.Combine(sub, "_data");
                        var found = ProjectSetup.Load(sd);
                        if (found != null)
                        {
                            _setupCache[docKey] = found;
                            return found;
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }
            catch (Exception ex) { StingLog.Warn($"LoadOrDetectSetup: {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// Like LoadOrDetectSetup, but if nothing is persisted, mints a default
        /// BIM ProjectSetup rooted at <projDir>/<projectCode>/ and persists it.
        /// Lets every subsystem call into the unified folder structure without
        /// asking the user to run the Folder Setup wizard first. Idempotent.
        /// </summary>
        public static ProjectSetup LoadOrBootstrapSetup(Document doc)
        {
            var existing = LoadOrDetectSetup(doc);
            if (existing != null) return existing;
            if (doc == null || string.IsNullOrEmpty(doc.PathName) || doc.IsFamilyDocument) return null;
            try
            {
                string code = DetectProjectCode(doc);
                var setup = ProjectSetup.CreateBIM(code, code); // root = relative folder named after the code
                setup.RootPathIsRelative = true;
                setup.ProjectName = "";
                try { setup.ProjectName = doc.ProjectInformation?.Name ?? ""; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                InitializeSetup(doc, setup);
                StingLog.Info($"LoadOrBootstrapSetup: minted default BIM setup for {code}");
                return setup;
            }
            catch (Exception ex) { StingLog.Warn($"LoadOrBootstrapSetup: {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// Resolve a metadata bucket path inside the consolidated <root>/_data/
        /// folder. Replaces 47-site hardcoded sibling-of-RVT folders like
        /// "_BIM_COORD" / "_bim_manager" / "STING_BIM_MANAGER" by nesting them
        /// inside the project root, so the user only sees ONE folder per project.
        /// </summary>
        public static string GetMetaPath(Document doc, string bucket = null, params string[] subParts)
        {
            string data = GetDataPath(doc);
            if (string.IsNullOrEmpty(data)) return null;
            string p = string.IsNullOrEmpty(bucket) ? data : Path.Combine(data, bucket);
            foreach (var part in subParts ?? Array.Empty<string>())
                if (!string.IsNullOrEmpty(part)) p = Path.Combine(p, part);
            try { Directory.CreateDirectory(p); } catch (Exception ex) { StingLog.Warn($"GetMetaPath: {ex.Message}"); }
            return p;
        }

        /// <summary>
        /// Persist the setup, build folder structure on disk, write FOLDER_INDEX.txt,
        /// and cache it for the active document.
        /// </summary>
        public static void InitializeSetup(Document doc, ProjectSetup setup)
        {
            if (doc == null || setup == null) return;
            try
            {
                string root = setup.ResolveRootPath(doc.PathName);
                if (string.IsNullOrEmpty(root)) return;
                Directory.CreateDirectory(root);

                // Create folders
                foreach (var f in setup.CustomFolders)
                {
                    if (setup.HiddenFolders.Contains(f.Id, StringComparer.OrdinalIgnoreCase)) continue;
                    string folderPath = Path.Combine(root, f.DisplayName);
                    try { Directory.CreateDirectory(folderPath); } catch (Exception ex) { StingLog.Warn($"InitSetup: {f.DisplayName}: {ex.Message}"); continue; }

                    if (f.HasDisciplineSubfolders && setup.Disciplines != null)
                    {
                        foreach (string disc in setup.Disciplines)
                        {
                            try { Directory.CreateDirectory(Path.Combine(folderPath, disc)); }
                            catch (Exception ex2) { StingLog.Warn($"InitSetup disc {disc}: {ex2.Message}"); }
                        }
                    }
                    if (f.SubFolders != null)
                    {
                        foreach (string s in f.SubFolders)
                        {
                            try { Directory.CreateDirectory(Path.Combine(folderPath, s)); }
                            catch (Exception ex2) { StingLog.Warn($"InitSetup sub {s}: {ex2.Message}"); }
                        }
                    }
                }

                // _data folder
                string dataPath = Path.Combine(root, "_data");
                Directory.CreateDirectory(dataPath);
                Directory.CreateDirectory(Path.Combine(dataPath, "folder_templates"));

                setup.Save(dataPath);
                _setupCache[doc.PathName] = setup;

                WriteFolderIndex(root);
                StingLog.Info($"ProjectFolderEngine: Setup initialised at {root}");
            }
            catch (Exception ex) { StingLog.Warn($"InitializeSetup: {ex.Message}"); }
        }

        /// <summary>Resolve the _data folder path; create it if missing. Optionally append a filename.</summary>
        public static string GetDataPath(Document doc, string fileName = null)
        {
            try
            {
                string root = GetRootPath(doc);
                if (string.IsNullOrEmpty(root)) return null;
                string dataDir = Path.Combine(root, "_data");
                if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
                return string.IsNullOrEmpty(fileName) ? dataDir : Path.Combine(dataDir, fileName);
            }
            catch (Exception ex) { StingLog.Warn($"GetDataPath: {ex.Message}"); return null; }
        }

        /// <summary>Detect the project code from Revit Project Information (Number → Name → "PRJ").</summary>
        public static string DetectProjectCode(Document doc)
        {
            if (doc == null) return "PRJ";
            try
            {
                string num = doc.ProjectInformation?.Number;
                if (!string.IsNullOrWhiteSpace(num)) return SanitizeCode(num);
                string name = doc.ProjectInformation?.Name;
                if (!string.IsNullOrWhiteSpace(name)) return SanitizeCode(name.Substring(0, Math.Min(3, name.Length)));
            }
            catch (Exception ex) { StingLog.Warn($"DetectProjectCode: {ex.Message}"); }
            return "PRJ";
        }

        private static string SanitizeCode(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "PRJ";
            var sb = new System.Text.StringBuilder();
            foreach (char c in raw)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
            }
            string s = sb.ToString().ToUpperInvariant();
            if (string.IsNullOrEmpty(s)) return "PRJ";
            if (s.Length > 8) s = s.Substring(0, 8);
            return s;
        }

        /// <summary>Per-folder health row used by FolderHealthPanel.</summary>
        public class FolderHealthEntry
        {
            public string Id { get; set; }
            public string DisplayName { get; set; }
            public bool Exists { get; set; }
            public int FileCount { get; set; }
            public DateTime? LastModified { get; set; }
            public bool IsEmpty { get; set; }
            public string FullPath { get; set; }
        }

        /// <summary>Build a per-folder health snapshot for the active setup.</summary>
        public static List<FolderHealthEntry> GetFolderHealth(Document doc)
        {
            var list = new List<FolderHealthEntry>();
            try
            {
                var setup = LoadOrDetectSetup(doc);
                if (setup == null)
                {
                    // Fall back to legacy folder list if no setup
                    string codeH = doc != null ? DetectProjectCode(doc) : "";
                    foreach (var (id, name, _, _) in Folders)
                    {
                        string root = GetRootPath(doc);
                        string suffixedH = ProjectSetup.WithCodeSuffix(name, codeH);
                        string p = Path.Combine(root, suffixedH);
                        bool dirExists = Directory.Exists(p);
                        int n = 0; DateTime? lm = null;
                        if (dirExists)
                        {
                            try
                            {
                                var di = new DirectoryInfo(p);
                                var files = di.GetFiles("*.*", SearchOption.AllDirectories);
                                n = files.Length;
                                if (n > 0) lm = files.Max(f => f.LastWriteTime);
                            }
                            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        }
                        list.Add(new FolderHealthEntry
                        {
                            Id = id,
                            DisplayName = name,
                            Exists = dirExists,
                            FileCount = n,
                            LastModified = lm,
                            IsEmpty = dirExists && n == 0,
                            FullPath = p,
                        });
                    }
                    return list;
                }

                string root2 = setup.ResolveRootPath(doc?.PathName);
                if (string.IsNullOrEmpty(root2)) return list;
                foreach (var f in setup.CustomFolders)
                {
                    if (setup.HiddenFolders.Contains(f.Id, StringComparer.OrdinalIgnoreCase)) continue;
                    string p = Path.Combine(root2, f.DisplayName);
                    bool dirExists2 = Directory.Exists(p);
                    int n = 0; DateTime? lm = null;
                    if (dirExists2)
                    {
                        try
                        {
                            var di = new DirectoryInfo(p);
                            var files = di.GetFiles("*.*", SearchOption.AllDirectories);
                            n = files.Length;
                            if (n > 0) lm = files.Max(fi => fi.LastWriteTime);
                        }
                        catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    }
                    list.Add(new FolderHealthEntry
                    {
                        Id = f.Id,
                        DisplayName = f.DisplayName,
                        Exists = dirExists2,
                        FileCount = n,
                        LastModified = lm,
                        IsEmpty = dirExists2 && n == 0,
                        FullPath = p,
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetFolderHealth: {ex.Message}"); }
            return list;
        }

        /// <summary>Migration record returned by MigrateFromLegacy.</summary>
        public class MigrationReport
        {
            public int FilesMoved { get; set; }
            public int FoldersRemoved { get; set; }
            public List<string> Warnings { get; set; } = new();
        }

        /// <summary>
        /// Detect legacy STING folders (_BIM_COORD, STING_BIM_MANAGER, STING_Exports,
        /// STING_Project) and .sting_*.json sidecar files alongside the .rvt; consolidate
        /// them into the new {ProjectCode}\ root.
        /// </summary>
        public static MigrationReport MigrateFromLegacy(Document doc)
        {
            var rep = new MigrationReport();
            if (doc == null || string.IsNullOrEmpty(doc.PathName)) return rep;

            try
            {
                string projDir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(projDir)) return rep;

                string dataPath = GetDataPath(doc);
                string root = GetRootPath(doc);
                if (string.IsNullOrEmpty(dataPath) || string.IsNullOrEmpty(root)) return rep;

                // 1. Move .sting_*.json sidecars adjacent to the .rvt
                try
                {
                    foreach (string f in Directory.GetFiles(projDir, "*.sting_*.json"))
                    {
                        try
                        {
                            string dest = Path.Combine(dataPath, Path.GetFileName(f).Replace(".sting_", ""));
                            if (File.Exists(dest)) dest = GetUniqueFileName(dest);
                            File.Move(f, dest);
                            rep.FilesMoved++;
                        }
                        catch (Exception ex) { rep.Warnings.Add($"Move {f}: {ex.Message}"); }
                    }
                    foreach (string f in Directory.GetFiles(projDir, "*_STING_SEQ.json"))
                    {
                        try
                        {
                            string dest = Path.Combine(dataPath, "seq_counters.json");
                            if (File.Exists(dest)) dest = GetUniqueFileName(dest);
                            File.Move(f, dest);
                            rep.FilesMoved++;
                        }
                        catch (Exception ex) { rep.Warnings.Add($"Move {f}: {ex.Message}"); }
                    }
                }
                catch (Exception ex) { rep.Warnings.Add($"Sidecar scan: {ex.Message}"); }

                // 2. Move legacy metadata buckets into _data/<bucket>/ — preserves
                //    subsystem code that hard-codes these names while tucking the
                //    files inside the unified project root.
                foreach (string legacyName in new[] { "_BIM_COORD", "_bim_manager", "STING_BIM_MANAGER" })
                {
                    string legacy = Path.Combine(projDir, legacyName);
                    if (!Directory.Exists(legacy)) continue;
                    string bucket = Path.Combine(dataPath, legacyName);
                    try
                    {
                        Directory.CreateDirectory(bucket);
                        foreach (string f in Directory.GetFiles(legacy, "*.*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                string rel = f.Substring(legacy.Length).TrimStart(Path.DirectorySeparatorChar);
                                string dest = Path.Combine(bucket, rel);
                                string ddir = Path.GetDirectoryName(dest);
                                if (!string.IsNullOrEmpty(ddir)) Directory.CreateDirectory(ddir);
                                if (File.Exists(dest)) dest = GetUniqueFileName(dest);
                                File.Move(f, dest);
                                rep.FilesMoved++;
                            }
                            catch (Exception ex2) { rep.Warnings.Add($"Move {f}: {ex2.Message}"); }
                        }
                        try { if (!Directory.EnumerateFiles(legacy, "*.*", SearchOption.AllDirectories).Any()) { Directory.Delete(legacy, true); rep.FoldersRemoved++; } }
                        catch (Exception ex2) { rep.Warnings.Add($"Delete {legacy}: {ex2.Message}"); }
                    }
                    catch (Exception ex2) { rep.Warnings.Add($"Process {legacy}: {ex2.Message}"); }
                }

                // 2b. Hidden helper folder used by clash detection
                {
                    string hiddenLegacy = Path.Combine(projDir, ".bimmanager");
                    if (Directory.Exists(hiddenLegacy))
                    {
                        string dest = Path.Combine(dataPath, ".bimmanager");
                        try
                        {
                            Directory.CreateDirectory(dest);
                            foreach (string f in Directory.GetFiles(hiddenLegacy, "*.*", SearchOption.AllDirectories))
                            {
                                try
                                {
                                    string rel = f.Substring(hiddenLegacy.Length).TrimStart(Path.DirectorySeparatorChar);
                                    string dst = Path.Combine(dest, rel);
                                    string ddir = Path.GetDirectoryName(dst);
                                    if (!string.IsNullOrEmpty(ddir)) Directory.CreateDirectory(ddir);
                                    if (File.Exists(dst)) dst = GetUniqueFileName(dst);
                                    File.Move(f, dst);
                                    rep.FilesMoved++;
                                }
                                catch (Exception ex2) { rep.Warnings.Add($"Move {f}: {ex2.Message}"); }
                            }
                            try { if (!Directory.EnumerateFiles(hiddenLegacy, "*.*", SearchOption.AllDirectories).Any()) { Directory.Delete(hiddenLegacy, true); rep.FoldersRemoved++; } }
                            catch (Exception ex2) { rep.Warnings.Add($"Delete {hiddenLegacy}: {ex2.Message}"); }
                        }
                        catch (Exception ex2) { rep.Warnings.Add($"Process {hiddenLegacy}: {ex2.Message}"); }
                    }
                }

                // 2c. Workflow run log written as a flat file alongside the .rvt
                foreach (string logName in new[] { "STING_WORKFLOW_LOG.json", "STING_WORKFLOW_LOG.jsonl" })
                {
                    string src = Path.Combine(projDir, logName);
                    if (!File.Exists(src)) continue;
                    try
                    {
                        string dst = Path.Combine(dataPath, "workflow_log" + Path.GetExtension(logName));
                        if (File.Exists(dst)) dst = GetUniqueFileName(dst);
                        File.Move(src, dst);
                        rep.FilesMoved++;
                    }
                    catch (Exception ex2) { rep.Warnings.Add($"Move {src}: {ex2.Message}"); }
                }

                // 2d. BOQ rate-source heat-map exports
                {
                    string boqLegacy = Path.Combine(projDir, "STING_BOQ_RateHeatMap");
                    if (Directory.Exists(boqLegacy))
                    {
                        string dest = Path.Combine(GetFolderPath(doc, "COMPLIANCE"), "RateHeatMap");
                        try
                        {
                            Directory.CreateDirectory(dest);
                            foreach (string f in Directory.GetFiles(boqLegacy, "*.*", SearchOption.AllDirectories))
                            {
                                try
                                {
                                    string rel = f.Substring(boqLegacy.Length).TrimStart(Path.DirectorySeparatorChar);
                                    string dst = Path.Combine(dest, rel);
                                    string ddir = Path.GetDirectoryName(dst);
                                    if (!string.IsNullOrEmpty(ddir)) Directory.CreateDirectory(ddir);
                                    if (File.Exists(dst)) dst = GetUniqueFileName(dst);
                                    File.Move(f, dst);
                                    rep.FilesMoved++;
                                }
                                catch (Exception ex2) { rep.Warnings.Add($"Move {f}: {ex2.Message}"); }
                            }
                            try { if (!Directory.EnumerateFiles(boqLegacy, "*.*", SearchOption.AllDirectories).Any()) { Directory.Delete(boqLegacy, true); rep.FoldersRemoved++; } }
                            catch (Exception ex2) { rep.Warnings.Add($"Delete {boqLegacy}: {ex2.Message}"); }
                        }
                        catch (Exception ex2) { rep.Warnings.Add($"Process {boqLegacy}: {ex2.Message}"); }
                    }
                }

                // 2e. Briefcase export folders ({ModelName}_Briefcase_{ts}/)
                try
                {
                    string briefcaseTarget = GetFolderPath(doc, "BRIEFCASE");
                    if (!string.IsNullOrEmpty(briefcaseTarget) && Directory.Exists(projDir))
                    {
                        foreach (string sub in Directory.GetDirectories(projDir, "*_Briefcase_*"))
                        {
                            try
                            {
                                string subName = Path.GetFileName(sub);
                                string dest = Path.Combine(briefcaseTarget, subName);
                                if (Directory.Exists(dest)) dest = GetUniqueFileName(dest);
                                Directory.Move(sub, dest);
                                rep.FoldersRemoved++;
                            }
                            catch (Exception ex2) { rep.Warnings.Add($"Move {sub}: {ex2.Message}"); }
                        }
                    }
                }
                catch (Exception ex) { rep.Warnings.Add($"Briefcase scan: {ex.Message}"); }

                // 3. Move legacy STING_Exports / STING_Project — route by extension
                foreach (string legacyName in new[] { "STING_Exports", "STING_Project" })
                {
                    string legacy = Path.Combine(projDir, legacyName);
                    if (!Directory.Exists(legacy)) continue;
                    if (string.Equals(Path.GetFullPath(legacy), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
                        continue; // never move the new root into itself

                    try
                    {
                        foreach (string f in Directory.GetFiles(legacy, "*.*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                string ext = Path.GetExtension(f).TrimStart('.').ToUpperInvariant();
                                string targetFolderId = ext switch
                                {
                                    "PDF" => "DRAWINGS",
                                    "XLSX" or "XLS" or "CSV" => "SCHEDULES",
                                    "IFC" or "RVT" or "NWC" or "NWD" or "NWF" or "DWG" or "DXF" => "MODELS",
                                    "JSON" => null, // → _data
                                    "BCF" or "BCFZIP" => "CLASHES",
                                    "DOCX" or "DOC" => "TRANSMITTALS",
                                    "JPG" or "JPEG" or "PNG" or "BMP" or "TIFF" or "TIF" => "DRAWINGS",
                                    _ => "MISC",
                                };
                                string destDir = targetFolderId == null
                                    ? dataPath
                                    : GetFolderPath(doc, targetFolderId);
                                if (string.IsNullOrEmpty(destDir)) destDir = root;
                                Directory.CreateDirectory(destDir);
                                string dest = Path.Combine(destDir, Path.GetFileName(f));
                                if (File.Exists(dest)) dest = GetUniqueFileName(dest);
                                File.Move(f, dest);
                                rep.FilesMoved++;
                            }
                            catch (Exception ex2) { rep.Warnings.Add($"Move {f}: {ex2.Message}"); }
                        }
                        try
                        {
                            // Only delete if empty after migration
                            if (!Directory.EnumerateFiles(legacy, "*.*", SearchOption.AllDirectories).Any())
                            {
                                Directory.Delete(legacy, true);
                                rep.FoldersRemoved++;
                            }
                        }
                        catch (Exception ex2) { rep.Warnings.Add($"Delete {legacy}: {ex2.Message}"); }
                    }
                    catch (Exception ex2) { rep.Warnings.Add($"Process {legacy}: {ex2.Message}"); }
                }

                InvalidateFolderStatsCache();
                StingLog.Info($"MigrateFromLegacy: {rep.FilesMoved} files moved, {rep.FoldersRemoved} folders removed.");
            }
            catch (Exception ex)
            {
                rep.Warnings.Add(ex.Message);
                StingLog.Warn($"MigrateFromLegacy: {ex.Message}");
            }
            return rep;
        }

        /// <summary>UI-bindable folder tree node.</summary>
        public class FolderNode
        {
            public string Id { get; set; }
            public string DisplayName { get; set; }
            public string FullPath { get; set; }
            public bool Exists { get; set; }
            public int FileCount { get; set; }
            public bool IsExpanded { get; set; }
            public List<FolderNode> Children { get; set; } = new();
        }

        /// <summary>Build a hierarchical folder browser tree (top-level + discipline/sub children).</summary>
        public static List<FolderNode> BuildFolderBrowserTree(Document doc)
        {
            var tree = new List<FolderNode>();
            try
            {
                var setup = LoadOrDetectSetup(doc);
                string root = GetRootPath(doc);
                if (string.IsNullOrEmpty(root)) return tree;

                IEnumerable<(string Id, string Display, bool DiscSubs, List<string> Subs)> defs;
                if (setup != null)
                {
                    defs = setup.CustomFolders
                        .Where(f => !setup.HiddenFolders.Contains(f.Id, StringComparer.OrdinalIgnoreCase))
                        .Select(f => (f.Id, f.DisplayName, f.HasDisciplineSubfolders, f.SubFolders ?? new List<string>()));
                }
                else
                {
                    string code0 = doc != null ? DetectProjectCode(doc) : "";
                    defs = Folders.Select(f => (f.Id, ProjectSetup.WithCodeSuffix(f.Name, code0), false, new List<string>()));
                }

                foreach (var (id, display, discSubs, subs) in defs)
                {
                    string p = Path.Combine(root, display);
                    var n = new FolderNode
                    {
                        Id = id,
                        DisplayName = display,
                        FullPath = p,
                        Exists = Directory.Exists(p),
                        IsExpanded = false,
                    };
                    try
                    {
                        if (n.Exists)
                            n.FileCount = Directory.GetFiles(p, "*.*", SearchOption.TopDirectoryOnly).Length;
                    }
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    if (discSubs && setup?.Disciplines != null)
                    {
                        foreach (string disc in setup.Disciplines)
                        {
                            string dp = Path.Combine(p, disc);
                            n.Children.Add(new FolderNode
                            {
                                Id = id + "/" + disc,
                                DisplayName = disc,
                                FullPath = dp,
                                Exists = Directory.Exists(dp),
                            });
                        }
                    }
                    foreach (string s in subs)
                    {
                        string sp = Path.Combine(p, s);
                        n.Children.Add(new FolderNode
                        {
                            Id = id + "/" + s,
                            DisplayName = s,
                            FullPath = sp,
                            Exists = Directory.Exists(sp),
                        });
                    }
                    tree.Add(n);
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildFolderBrowserTree: {ex.Message}"); }
            return tree;
        }

        /// <summary>
        /// Phase 167-aware export path resolver.
        /// Honours setup.ExportRoutes, discipline subfolders, and naming convention.
        /// </summary>
        public static string GetExportPath(Document doc, string exportTypeKey, string baseName,
            string extension, string disciplineCode)
        {
            try
            {
                var setup = LoadOrDetectSetup(doc);
                string folderId = null;
                if (setup != null && setup.ExportRoutes != null &&
                    setup.ExportRoutes.TryGetValue(exportTypeKey ?? "", out string routed))
                {
                    folderId = routed;
                }
                if (string.IsNullOrEmpty(folderId) && ExportTypeToFolder.TryGetValue(exportTypeKey ?? "", out string fb))
                    folderId = fb;
                if (string.IsNullOrEmpty(folderId)) folderId = "MISC";

                string folder;
                if (string.Equals(folderId, "_DATA", StringComparison.OrdinalIgnoreCase))
                    folder = GetDataPath(doc);
                else
                    folder = GetFolderPath(doc, folderId);
                if (string.IsNullOrEmpty(folder)) folder = GetRootPath(doc);

                // Discipline sub-routing
                if (!string.IsNullOrEmpty(disciplineCode) && setup != null)
                {
                    var fdef = setup.GetFolder(folderId);
                    if (fdef != null && fdef.HasDisciplineSubfolders && setup.Disciplines != null)
                    {
                        string match = setup.Disciplines.FirstOrDefault(d =>
                            d.StartsWith(disciplineCode + "_", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(d, disciplineCode, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(match))
                        {
                            folder = Path.Combine(folder, match);
                            try { Directory.CreateDirectory(folder); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        }
                    }
                }
                else
                {
                    Directory.CreateDirectory(folder);
                }

                // Apply naming
                NamingConvention nc = setup?.NamingConvention ?? NamingConvention.Timestamp;
                string ext = extension ?? "";
                if (!string.IsNullOrEmpty(ext) && !ext.StartsWith(".")) ext = "." + ext;
                string fileName;
                switch (nc)
                {
                    case NamingConvention.ISO19650:
                        fileName = AutoCorrectFileName(doc,
                            (baseName ?? "export") + ext,
                            disciplineCode ?? "Z",
                            "DR", "S3");
                        break;
                    case NamingConvention.Custom:
                        fileName = ApplyCustomPattern(setup?.CustomNamingPattern, baseName, ext, doc);
                        break;
                    default:
                        fileName = $"{baseName ?? "export"}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
                        break;
                }
                return Path.Combine(folder, fileName);
            }
            catch (Exception ex) { StingLog.Warn($"GetExportPath: {ex.Message}"); }
            return GetExportPath(doc, exportTypeKey, baseName, extension);
        }

        private static string ApplyCustomPattern(string pattern, string baseName, string ext, Document doc)
        {
            if (string.IsNullOrEmpty(pattern))
                return $"{baseName ?? "export"}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
            string code = "PRJ";
            try { code = doc?.ProjectInformation?.Number ?? "PRJ"; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            string s = pattern
                .Replace("{name}", baseName ?? "export")
                .Replace("{date}", DateTime.Now.ToString("yyyyMMdd"))
                .Replace("{time}", DateTime.Now.ToString("HHmmss"))
                .Replace("{code}", code)
                .Replace("{ext}", ext);
            if (!s.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) s += ext;
            return s;
        }

        // ── Folder creation ───────────────────────────────────────────────

        /// <summary>Create the full ISO 19650 folder structure.</summary>
        public static int CreateFolderStructure(Document doc)
        {
            string root = GetRootPath(doc);
            string code = doc != null ? DetectProjectCode(doc) : "";
            int created = 0;
            foreach (var (id, name, desc, _) in Folders)
            {
                string suffixed = ProjectSetup.WithCodeSuffix(name, code);
                string path = Path.Combine(root, suffixed);
                if (!Directory.Exists(path))
                {
                    try { Directory.CreateDirectory(path); created++; }
                    catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine: Cannot create {suffixed}: {ex.Message}"); }
                }
            }
            // Create sub-folders for CDE folders (CONFIG-02: configurable)
            foreach (string cdeFolder in new[] { "01_WIP", "02_SHARED", "03_PUBLISHED" })
            {
                string cdePath = Path.Combine(root, ProjectSetup.WithCodeSuffix(cdeFolder, code));
                foreach (string disc in _disciplineFolders)
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
            string clashRoot = Path.Combine(root, ProjectSetup.WithCodeSuffix("12_CLASHES", code));
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
            string issueRoot = Path.Combine(root, ProjectSetup.WithCodeSuffix("11_ISSUES", code));
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

            // Phase 167: prefer ProjectSetup folder def (uses user-edited display name)
            try
            {
                var setup = LoadOrBootstrapSetup(doc);
                var def = setup?.GetFolder(folderId);
                if (def != null)
                {
                    string p = Path.Combine(root, def.DisplayName);
                    try { Directory.CreateDirectory(p); } catch (Exception ex) { StingLog.Warn($"GetFolderPath: {ex.Message}"); }
                    return p;
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetFolderPath setup: {ex.Message}"); }

            // No setup — apply the project-code suffix to the built-in name so
            // we still produce a uniquely-named folder (e.g. 20_MISC_FIRESTONE).
            string code = doc != null ? DetectProjectCode(doc) : "";
            var folder = Folders.FirstOrDefault(f => f.Id.Equals(folderId, StringComparison.OrdinalIgnoreCase));
            string name = folder.Name ?? "20_MISC";
            string suffixed = ProjectSetup.WithCodeSuffix(name, code);
            string path = Path.Combine(root, suffixed);
            if (!Directory.Exists(path))
            {
                try { Directory.CreateDirectory(path); }
                catch (Exception ex2) { StingLog.Warn($"ProjectFolderEngine.GetFolderPath: {ex2.Message}"); }
            }
            return path;
        }

        /// <summary>Get the folder path for an export type key (e.g. "PDF", "COBie", "BCF").</summary>
        public static string GetExportFolder(Document doc, string exportTypeKey)
        {
            // Phase 167: honour ProjectSetup ExportRoutes first
            try
            {
                var setup = LoadOrDetectSetup(doc);
                if (setup != null && setup.ExportRoutes != null &&
                    !string.IsNullOrEmpty(exportTypeKey) &&
                    setup.ExportRoutes.TryGetValue(exportTypeKey, out string folderId) &&
                    !string.IsNullOrEmpty(folderId))
                {
                    if (string.Equals(folderId, "_DATA", StringComparison.OrdinalIgnoreCase))
                        return GetDataPath(doc);
                    return GetFolderPath(doc, folderId);
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetExportFolder setup lookup: {ex.Message}"); }

            if (ExportTypeToFolder.TryGetValue(exportTypeKey ?? "", out string folderId2))
                return GetFolderPath(doc, folderId2);
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
            string codeAF = doc != null ? DetectProjectCode(doc) : "";

            foreach (var (id, name, desc, cde) in Folders)
            {
                string folderPath = Path.Combine(root, ProjectSetup.WithCodeSuffix(name, codeAF));
                if (!Directory.Exists(folderPath))
                {
                    // Backwards-compat: fall back to the un-suffixed legacy path
                    string legacyPath = Path.Combine(root, name);
                    if (!Directory.Exists(legacyPath)) continue;
                    folderPath = legacyPath;
                }

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

        /// <summary>Get folder statistics (cached for 10 seconds — PERF-02).</summary>
        public static List<FolderStats> GetFolderStats(Document doc)
        {
            if (_folderStatsCache != null && (DateTime.Now - _folderStatsCacheTime) < FolderStatsCacheDuration)
                return _folderStatsCache;

            var stats = new List<FolderStats>();
            string root = GetRootPath(doc);
            if (!Directory.Exists(root)) return stats;
            string codeFS = doc != null ? DetectProjectCode(doc) : "";

            foreach (var (id, name, desc, cde) in Folders)
            {
                string suffixedName = ProjectSetup.WithCodeSuffix(name, codeFS);
                string folderPath = Path.Combine(root, suffixedName);
                if (!Directory.Exists(folderPath))
                {
                    string legacyFs = Path.Combine(root, name);
                    if (Directory.Exists(legacyFs)) folderPath = legacyFs;
                }
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
            _folderStatsCache = stats;
            _folderStatsCacheTime = DateTime.Now;
            return stats;
        }

        // ── File operations ───────────────────────────────────────────────

        /// <summary>Soft-delete: move file to _RECYCLE subfolder (OP-005). Hard-delete if recycle fails.</summary>
        public static bool DeleteFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                string name = Path.GetFileName(filePath);
                string dir = Path.GetDirectoryName(filePath) ?? "";

                // Try soft-delete to _RECYCLE
                string recycleDir = Path.Combine(dir, "_RECYCLE");
                try
                {
                    if (!Directory.Exists(recycleDir)) Directory.CreateDirectory(recycleDir);
                    string recyclePath = Path.Combine(recycleDir, $"{DateTime.Now:yyyyMMdd_HHmmss}_{name}");
                    File.Move(filePath, recyclePath);
                    StingLog.Info($"ProjectFolderEngine: Recycled {name} → _RECYCLE");
                    LogActivity(null, "RECYCLE", name, filePath);
                    InvalidateFolderStatsCache();
                    RaiseFileChanged("RECYCLE", name, filePath);
                    return true;
                }
                catch
                {
                    // Fall back to hard delete
                    File.Delete(filePath);
                    StingLog.Info($"ProjectFolderEngine: Hard-deleted {name}");
                    LogActivity(null, "DELETE", name, filePath);
                    InvalidateFolderStatsCache();
                    RaiseFileChanged("DELETE", name, filePath);
                    return true;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine.DeleteFile: {ex.Message}"); }
            return false;
        }

        /// <summary>Restore a file from the _RECYCLE folder.</summary>
        public static bool RestoreFile(string recyclePath, string originalDir = null)
        {
            try
            {
                if (!File.Exists(recyclePath)) return false;
                string name = Path.GetFileName(recyclePath);
                // Strip timestamp prefix (yyyyMMdd_HHmmss_)
                if (name.Length > 16 && name[15] == '_')
                    name = name.Substring(16);
                string targetDir = originalDir ?? Path.GetDirectoryName(Path.GetDirectoryName(recyclePath)) ?? "";
                string targetPath = Path.Combine(targetDir, name);
                if (File.Exists(targetPath)) targetPath = GetUniqueFileName(targetPath);
                File.Move(recyclePath, targetPath);
                StingLog.Info($"ProjectFolderEngine: Restored {name}");
                LogActivity(null, "RESTORE", name, targetPath);
                InvalidateFolderStatsCache();
                RaiseFileChanged("RESTORE", name, targetPath);
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine.RestoreFile: {ex.Message}"); }
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
                InvalidateFolderStatsCache();
                RaiseFileChanged("RENAME", newName, Path.Combine(dir, newName));
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
                InvalidateFolderStatsCache();
                RaiseFileChanged("MOVE", fileName, Path.Combine(targetDir, fileName));

                // AUTO-001: Auto-log transmittal when moving to CDE folders
                if (targetFolderId == "SHARED" || targetFolderId == "PUBLISHED")
                    AutoLogTransmittal(doc, new List<string> { newPath }, targetFolderId);

                return true;
            }
            catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine.MoveFile: {ex.Message}"); }
            return false;
        }

        /// <summary>Copy external file into the project folder structure with validation and activity logging.</summary>
        public static string ImportFile(Document doc, string sourcePath, string targetFolderId)
        {
            try
            {
                if (!File.Exists(sourcePath)) return null;
                string fileName = Path.GetFileName(sourcePath);

                // OP-002: Validate file extension
                string ext = Path.GetExtension(sourcePath).TrimStart('.').ToUpperInvariant();
                if (!string.IsNullOrEmpty(ext) && !AllowedExtensions.Contains(ext))
                {
                    StingLog.Warn($"ProjectFolderEngine: Blocked import of unsupported file type .{ext}: {fileName}");
                    return null;
                }

                string targetDir = GetFolderPath(doc, targetFolderId);
                string targetPath = Path.Combine(targetDir, fileName);
                if (File.Exists(targetPath)) targetPath = GetUniqueFileName(targetPath);
                File.Copy(sourcePath, targetPath);
                StingLog.Info($"ProjectFolderEngine: Imported {fileName} → {targetFolderId}");
                LogActivity(doc, "IMPORT", fileName, $"→ {targetFolderId}");
                InvalidateFolderStatsCache();
                RaiseFileChanged("IMPORT", fileName, targetPath);
                return targetPath;
            }
            catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine.ImportFile: {ex.Message}"); }
            return null;
        }

        /// <summary>Check if a file extension is allowed for import (OP-002).</summary>
        public static bool IsAllowedExtension(string extension)
        {
            return AllowedExtensions.Contains(extension?.TrimStart('.').ToUpperInvariant() ?? "");
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
                lines.Add("Generated by StingTools — https://planscape.com");
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

                // CONFIG-02: Load custom discipline folders
                if (config["DISCIPLINE_FOLDERS"] is JArray discArr && discArr.Count > 0)
                {
                    var customDiscs = discArr.Select(d => d.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
                    if (customDiscs.Length > 0) _disciplineFolders = customDiscs;
                }
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

        /// <summary>PERF-03: Read only the last N lines of a file without loading entire file.</summary>
        private static List<string> TailReadLines(string filePath, int lineCount)
        {
            var lines = new List<string>();
            try
            {
                var fi = new FileInfo(filePath);
                if (fi.Length == 0) return lines;

                // For small files (< 64KB), just read all
                if (fi.Length < 65536)
                {
                    lines.AddRange(File.ReadAllLines(filePath));
                    return lines;
                }

                // For larger files, seek from end
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                long pos = stream.Length;
                int found = 0;
                int bufSize = Math.Min(8192, (int)stream.Length);
                byte[] buf = new byte[bufSize];

                while (pos > 0 && found < lineCount + 1)
                {
                    int toRead = (int)Math.Min(bufSize, pos);
                    pos -= toRead;
                    stream.Seek(pos, SeekOrigin.Begin);
                    stream.Read(buf, 0, toRead);
                    for (int i = toRead - 1; i >= 0; i--)
                    {
                        if (buf[i] == (byte)'\n') found++;
                        if (found > lineCount) { pos += i + 1; break; }
                    }
                }

                stream.Seek(Math.Max(0, pos), SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                string line;
                while ((line = reader.ReadLine()) != null)
                    lines.Add(line);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TailReadLines: {ex.Message}");
                // Fallback: read all
                try { lines = File.ReadAllLines(filePath).ToList(); } catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); }
            }
            return lines;
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
                try { projCode = doc?.ProjectInformation?.Number ?? "PRJ"; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
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

            // VALIDATION-01: Check file extension
            string extClean = ext.TrimStart('.').ToUpperInvariant();
            if (!string.IsNullOrEmpty(extClean) && !AllowedExtensions.Contains(extClean))
                errors.Add($"File extension '.{extClean}' not in approved list");

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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            string originator = "ZZ";
            string volume = "ZZ";
            string level = "XX";
            string role = discipline.Length == 1 ? discipline : "Z";
            string number = DateTime.Now.ToString("HHmmss");

            // VALIDATION-02: Auto-detect docType from extension if not specified
            if (docType == "DR" && !string.IsNullOrEmpty(ext))
            {
                string extUp = ext.TrimStart('.').ToUpperInvariant();
                docType = extUp switch
                {
                    "RVT" or "IFC" or "NWC" => "M3",
                    "PDF" => "DR",
                    "XLSX" or "CSV" => "SH",
                    "DOCX" or "DOC" => "RP",
                    "PPTX" or "PPT" => "PP",
                    "BCF" or "BCFZIP" => "RI",
                    "JPG" or "PNG" or "TIFF" => "VS",
                    _ => "DR"
                };
            }

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
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); user = Environment.UserName; }
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
        /// <summary>Get recent activity (PERF-03: tail-read, not full file load).</summary>
        public static List<ActivityEntry> GetRecentActivity(Document doc, int maxEntries = 50)
        {
            var entries = new List<ActivityEntry>();
            try
            {
                string root = GetRootPath(doc);
                string logPath = Path.Combine(root, "ACTIVITY_LOG.jsonl");
                if (!File.Exists(logPath)) return entries;

                // PERF-03: Read only the tail of the file
                var lines = TailReadLines(logPath, maxEntries * 2); // over-read to account for blanks
                foreach (string line in Enumerable.Reverse(lines).Take(maxEntries))
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
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
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
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
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
                string bimDir = GetMetaPath(doc, "STING_BIM_MANAGER");
                if (string.IsNullOrEmpty(bimDir))
                {
                    StingLog.Warn("AutoLogTransmittal: cannot resolve metadata path");
                    return;
                }

                string transPath = Path.Combine(bimDir, "transmittals.json");
                JArray arr;
                if (File.Exists(transPath))
                    arr = JArray.Parse(File.ReadAllText(transPath));
                else
                    arr = new JArray();

                string transId = $"TR-{DateTime.Now:yyyyMMdd-HHmmss}";
                string user = "";
                try { user = doc?.Application?.Username ?? Environment.UserName; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); user = Environment.UserName; }

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
                string bimDir = GetMetaPath(doc, "STING_BIM_MANAGER");
                if (string.IsNullOrEmpty(bimDir)) return groups;
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

        // ══════════════════════════════════════════════════════════════════
        //  INT-001: External hook / callback for file changes
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Event raised when a file is added, deleted, moved, or renamed in the project folder.
        /// External tools can subscribe to this for integration.
        /// </summary>
        public static event Action<string, string, string> FileChanged; // (action, fileName, path)

        internal static void RaiseFileChanged(string action, string fileName, string path)
        {
            try { FileChanged?.Invoke(action, fileName, path); }
            catch (Exception ex) { StingLog.Warn($"FileChanged handler error: {ex.Message}"); }
        }

        // ══════════════════════════════════════════════════════════════════
        //  FOLDER-04: FileSystemWatcher active-state guard
        /// <summary>Returns true when the FileSystemWatcher is currently enabled.</summary>
        public static bool IsWatcherActive => _watcher?.EnableRaisingEvents == true;

        //  INT-002: FileSystemWatcher for project folder monitoring
        // ══════════════════════════════════════════════════════════════════

        private static FileSystemWatcher _watcher;
        private static Action<string> _watcherCallback;

        /// <summary>
        /// Start monitoring the project folder for external changes.
        /// Callback receives the changed file path.
        /// </summary>
        public static void StartWatching(Document doc, Action<string> onFileChanged = null)
        {
            StopWatching();
            string root = GetRootPath(doc);
            if (!Directory.Exists(root)) return;

            _watcherCallback = onFileChanged;
            try
            {
                _watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };
                _watcher.Created += (s, e) =>
                {
                    if (e.Name?.StartsWith(".") == true || e.Name?.Contains("_RECYCLE") == true) return;
                    LogActivity(doc, "EXT_CREATE", Path.GetFileName(e.FullPath), e.FullPath);
                    InvalidateFolderStatsCache();
                    RaiseFileChanged("CREATE", Path.GetFileName(e.FullPath), e.FullPath);
                    _watcherCallback?.Invoke(e.FullPath);
                };
                _watcher.Deleted += (s, e) =>
                {
                    if (e.Name?.StartsWith(".") == true || e.Name?.Contains("_RECYCLE") == true) return;
                    LogActivity(doc, "EXT_DELETE", Path.GetFileName(e.FullPath), e.FullPath);
                    InvalidateFolderStatsCache();
                    RaiseFileChanged("DELETE", Path.GetFileName(e.FullPath), e.FullPath);
                    _watcherCallback?.Invoke(e.FullPath);
                };
                _watcher.Renamed += (s, e) =>
                {
                    LogActivity(doc, "EXT_RENAME", Path.GetFileName(e.FullPath), $"{e.OldName} → {e.Name}");
                    InvalidateFolderStatsCache();
                    RaiseFileChanged("RENAME", Path.GetFileName(e.FullPath), e.FullPath);
                    _watcherCallback?.Invoke(e.FullPath);
                };
                StingLog.Info($"ProjectFolderEngine: FileSystemWatcher started on {root}");
            }
            catch (Exception ex) { StingLog.Warn($"ProjectFolderEngine.StartWatching: {ex.Message}"); }
        }

        /// <summary>Stop monitoring the project folder.</summary>
        public static void StopWatching()
        {
            if (_watcher != null)
            {
                try { _watcher.EnableRaisingEvents = false; _watcher.Dispose(); }
                catch (Exception ex) { StingLog.Warn($"StopWatching: {ex.Message}"); }
                _watcher = null;
                _watcherCallback = null;
            }
        }

        // ── FOLDER-01: Cloud mirror helper ────────────────────────────────

        /// <summary>
        /// Best-effort cloud mirror of a local file on CDE state transition.
        /// Called from DocumentManagementDialog when a document moves to SHARED or PUBLISHED.
        /// Never throws — logs success/failure via StingLog.
        /// </summary>
        /// <param name="doc">Active Revit document (used for project context).</param>
        /// <param name="localFilePath">Absolute path of the file to mirror.</param>
        /// <param name="cdeState">CDE state that triggered the mirror ("SHARED" or "PUBLISHED").</param>
        public static void TryMirrorToCloud(Document doc, string localFilePath, string cdeState)
        {
            try
            {
                var setup = LoadOrDetectSetup(doc);
                if (setup == null) return;
                if (!setup.AutoMirrorOnPublish) return;

                bool shouldMirror = string.Equals(cdeState, "SHARED", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(cdeState, "PUBLISHED", StringComparison.OrdinalIgnoreCase);
                if (!shouldMirror) return;

                if (!File.Exists(localFilePath))
                {
                    StingLog.Warn($"TryMirrorToCloud: local file not found: {localFilePath}");
                    return;
                }

                string provider = setup.CloudProvider ?? "";
                string cloudRoot = setup.CloudRoot ?? "";
                StingLog.Info($"TryMirrorToCloud: provider={provider} state={cdeState} file={Path.GetFileName(localFilePath)}");

                switch (provider.ToUpperInvariant())
                {
                    case "ACC":
                        MirrorToACC(doc, localFilePath, cloudRoot, cdeState);
                        break;
                    case "SHAREPOINT":
                        MirrorToSharePoint(doc, localFilePath, cloudRoot, cdeState);
                        break;
                    case "DROPBOX":
                        MirrorToFolder(localFilePath, cloudRoot, cdeState);
                        break;
                    case "ONEDRIVE":
                        MirrorToFolder(localFilePath, cloudRoot, cdeState);
                        break;
                    default:
                        StingLog.Info($"TryMirrorToCloud: provider '{provider}' not configured — skipping");
                        break;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TryMirrorToCloud: {ex.Message}");
            }
        }

        private static void MirrorToACC(Document doc, string localFilePath, string cloudRoot, string cdeState)
        {
            // Route through the existing ACC publish pipeline.
            // PlatformLinkEngine is in BIMManager — call via the public static helper.
            // Wrap in a try so a missing ACC connection never blocks the caller.
            try
            {
                // Build a minimal package dir alongside the file
                string fileName = Path.GetFileName(localFilePath);
                string tmpDir = Path.Combine(Path.GetDirectoryName(localFilePath), "_acc_mirror_tmp");
                Directory.CreateDirectory(tmpDir);
                string dest = Path.Combine(tmpDir, fileName);
                File.Copy(localFilePath, dest, overwrite: true);

                // Build a lightweight manifest JSON next to the file
                string manifestPath = Path.Combine(tmpDir, "acc_manifest.json");
                File.WriteAllText(manifestPath, $"{{\"source\":\"{localFilePath.Replace("\\", "\\\\")}\",\"cdeState\":\"{cdeState}\",\"cloudRoot\":\"{cloudRoot.Replace("\\", "\\\\")}\"}}");

                StingLog.Info($"TryMirrorToCloud(ACC): staged '{fileName}' in {tmpDir} for ACC upload. Connect via BIM > ACC Publish to push.");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TryMirrorToCloud(ACC): {ex.Message}");
            }
        }

        private static void MirrorToSharePoint(Document doc, string localFilePath, string cloudRoot, string cdeState)
        {
            try
            {
                // SharePoint: stage the file in a well-known export folder.
                // The existing SharePointExportCommand handles the actual upload;
                // here we create a sidecar that tells it which file to upload.
                string bimDir = GetRootPath(doc);
                string spStageDir = Path.Combine(bimDir, "_DATA", "sharepoint_queue");
                Directory.CreateDirectory(spStageDir);
                string entry = $"{{\"file\":\"{localFilePath.Replace("\\", "\\\\")}\",\"cdeState\":\"{cdeState}\",\"cloudRoot\":\"{cloudRoot.Replace("\\", "\\\\")}\"}}";
                string queueFile = Path.Combine(spStageDir, $"{DateTime.Now:yyyyMMdd_HHmmss}_{Path.GetFileName(localFilePath)}.json");
                File.WriteAllText(queueFile, entry);
                StingLog.Info($"TryMirrorToCloud(SharePoint): queued '{Path.GetFileName(localFilePath)}' → {queueFile}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TryMirrorToCloud(SharePoint): {ex.Message}");
            }
        }

        private static void MirrorToFolder(string localFilePath, string cloudRoot, string cdeState)
        {
            // Dropbox / OneDrive: sync via local filesystem folder copy.
            try
            {
                if (string.IsNullOrWhiteSpace(cloudRoot) || !Directory.Exists(cloudRoot))
                {
                    StingLog.Warn($"TryMirrorToCloud(Folder): CloudRoot '{cloudRoot}' does not exist — skipping");
                    return;
                }
                string subFolder = cdeState.ToUpperInvariant() == "PUBLISHED" ? "Published" : "Shared";
                string destDir = Path.Combine(cloudRoot, subFolder);
                Directory.CreateDirectory(destDir);
                string destFile = Path.Combine(destDir, Path.GetFileName(localFilePath));
                File.Copy(localFilePath, destFile, overwrite: true);
                StingLog.Info($"TryMirrorToCloud(Folder): copied '{Path.GetFileName(localFilePath)}' → {destFile}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TryMirrorToCloud(Folder): {ex.Message}");
            }
        }
    }
}
