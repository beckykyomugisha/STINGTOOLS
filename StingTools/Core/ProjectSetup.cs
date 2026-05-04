using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace StingTools.Core
{
    /// <summary>Folder layout mode — full ISO 19650 BIM tree, or flat 5-folder mini.</summary>
    public enum ProjectFolderMode { BIM, Mini }

    /// <summary>Naming convention for files written into the export folders.</summary>
    public enum NamingConvention { ISO19650, Timestamp, Custom }

    /// <summary>Definition of a single folder in the project tree (numbered or named).</summary>
    public class FolderDef
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public bool HasDisciplineSubfolders { get; set; }
        public List<string> SubFolders { get; set; } = new();
        public bool IsCustom { get; set; }
    }

    /// <summary>
    /// Persisted project-folder configuration. Stored in {ProjectCode}\_data\project_setup.json.
    /// Single source of truth for folder layout, disciplines, export routing, and naming.
    /// </summary>
    public class ProjectSetup
    {
        public string ProjectCode { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string RootPath { get; set; } = "";
        public bool RootPathIsRelative { get; set; }
        public ProjectFolderMode Mode { get; set; } = ProjectFolderMode.BIM;
        public List<string> Disciplines { get; set; } = new();
        public List<FolderDef> CustomFolders { get; set; } = new();
        public List<string> HiddenFolders { get; set; } = new();
        public Dictionary<string, string> ExportRoutes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public NamingConvention NamingConvention { get; set; } = NamingConvention.ISO19650;
        public string CustomNamingPattern { get; set; } = "";
        public string TemplateName { get; set; } = "";
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime LastModified { get; set; } = DateTime.Now;

        // ── Default discipline folder names ────────────────────────────────
        public static readonly List<string> DefaultBimDisciplines = new()
        {
            "A_Architectural", "E_Electrical", "M_Mechanical", "P_Plumbing", "S_Structural"
        };

        // ── BIM folder defaults (16 numbered folders) ──────────────────────
        // Display names get suffixed with the project code at setup time
        // (e.g. "01_WIP" → "01_WIP_FIRESTONE_LIBERIA"). This makes every
        // folder uniquely identifiable when copied or zipped out of the root.
        public static readonly (string Id, string Name, bool DiscSubs, string[] SubFolders)[] BimFolderDefaults = new[]
        {
            ("WIP",          "01_WIP",          true,  new string[0]),
            ("SHARED",       "02_SHARED",       true,  new string[0]),
            ("PUBLISHED",    "03_PUBLISHED",    true,  new string[0]),
            ("ARCHIVE",      "04_ARCHIVE",      false, new string[0]),
            ("MODELS",       "05_MODELS",       false, new string[0]),
            ("DRAWINGS",     "06_DRAWINGS",     true,  new string[0]),
            ("SCHEDULES",    "07_SCHEDULES",    false, new string[0]),
            ("COBIE",        "08_COBie",        false, new string[0]),
            ("BEP",          "09_BEP",          false, new string[0]),
            ("TRANSMITTALS", "10_TRANSMITTALS", false, new string[0]),
            ("ISSUES",       "11_ISSUES",       false, new[] { "RFI", "TQ", "NCR", "EWN" }),
            ("CLASHES",      "12_CLASHES",      false, new[] { "BCF", "Reports", "Snapshots" }),
            ("HANDOVER",     "13_HANDOVER",     false, new string[0]),
            ("REVISIONS",    "14_REVISIONS",    false, new string[0]),
            ("REGISTERS",    "15_REGISTERS",    false, new string[0]),
            ("COMPLIANCE",   "16_COMPLIANCE",   false, new string[0]),
            ("BRIEFCASE",    "17_BRIEFCASE",    false, new string[0]),
            ("PHOTOS",       "18_PHOTOS",       false, new string[0]),
            ("CORRESPONDENCE","19_CORRESPONDENCE",false, new string[0]),
            ("MISC",         "20_MISC",         false, new string[0]),
        };

        // ── Mini folder defaults (5 flat folders) ──────────────────────────
        public static readonly (string Id, string Name)[] MiniFolderDefaults = new[]
        {
            ("DRAWINGS",  "Drawings"),
            ("MODELS",    "Models"),
            ("SCHEDULES", "Schedules"),
            ("DOCUMENTS", "Documents"),
            ("REPORTS",   "Reports"),
        };

        /// <summary>
        /// Append `_<projectCode>` to a folder display name if not already suffixed.
        /// Idempotent: WithCodeSuffix("01_WIP", "FIRESTONE") → "01_WIP_FIRESTONE"
        /// but a second call returns the same string.
        /// </summary>
        public static string WithCodeSuffix(string folderName, string projectCode)
        {
            if (string.IsNullOrWhiteSpace(folderName)) return folderName;
            if (string.IsNullOrWhiteSpace(projectCode)) return folderName;
            string suffix = "_" + projectCode;
            if (folderName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return folderName;
            return folderName + suffix;
        }

        // ── Default export route maps ──────────────────────────────────────
        public static Dictionary<string, string> DefaultBimRoutes() => new(StringComparer.OrdinalIgnoreCase)
        {
            ["PDF"] = "DRAWINGS",
            ["IFC"] = "MODELS", ["NWC"] = "MODELS", ["RVT"] = "MODELS", ["DWG"] = "MODELS",
            ["COBIE"] = "COBIE", ["COBie"] = "COBIE", ["COBieStream"] = "COBIE",
            ["SCHEDULE"] = "SCHEDULES", ["EXCEL"] = "SCHEDULES", ["CSV"] = "SCHEDULES", ["BOQ"] = "SCHEDULES",
            ["BEP"] = "BEP",
            ["TRANSMITTAL"] = "TRANSMITTALS", ["Transmittal"] = "TRANSMITTALS",
            ["ISSUE"] = "ISSUES", ["Issue"] = "ISSUES", ["RFI"] = "ISSUES",
            ["BCF"] = "CLASHES", ["CLASH"] = "CLASHES", ["Clash"] = "CLASHES",
            ["HANDOVER"] = "HANDOVER", ["Handover"] = "HANDOVER", ["OAM"] = "HANDOVER",
            ["OandM"] = "HANDOVER", ["Maintenance"] = "HANDOVER", ["AssetHealth"] = "HANDOVER",
            ["REVISION"] = "REVISIONS", ["Revision"] = "REVISIONS",
            ["REGISTER"] = "REGISTERS", ["TagRegister"] = "REGISTERS",
            ["DocRegister"] = "REGISTERS", ["AssetRegister"] = "REGISTERS",
            ["COMPLIANCE"] = "COMPLIANCE", ["Compliance"] = "COMPLIANCE",
            ["MODELHEALTH"] = "COMPLIANCE", ["ModelHealth"] = "COMPLIANCE",
            ["Validation"] = "COMPLIANCE",
            ["JSON"] = "_DATA",
        };

        public static Dictionary<string, string> DefaultMiniRoutes() => new(StringComparer.OrdinalIgnoreCase)
        {
            ["PDF"] = "DRAWINGS",
            ["IFC"] = "MODELS", ["NWC"] = "MODELS", ["RVT"] = "MODELS", ["DWG"] = "MODELS",
            ["SCHEDULE"] = "SCHEDULES", ["EXCEL"] = "SCHEDULES", ["CSV"] = "SCHEDULES", ["BOQ"] = "SCHEDULES",
            ["TRANSMITTAL"] = "DOCUMENTS", ["Transmittal"] = "DOCUMENTS",
            ["BEP"] = "DOCUMENTS",
            ["COMPLIANCE"] = "REPORTS", ["Compliance"] = "REPORTS",
            ["MODELHEALTH"] = "REPORTS", ["ModelHealth"] = "REPORTS",
            ["HANDOVER"] = "DOCUMENTS",
            ["JSON"] = "_DATA",
        };

        // ── Factory methods ────────────────────────────────────────────────

        /// <summary>Build a default BIM-mode setup with the 16 numbered folders.</summary>
        public static ProjectSetup CreateBIM(string projectCode, string rootPath, List<string> disciplines = null)
        {
            var s = new ProjectSetup
            {
                ProjectCode = projectCode ?? "PRJ",
                RootPath = rootPath ?? "",
                Mode = ProjectFolderMode.BIM,
                Disciplines = disciplines != null && disciplines.Count > 0
                    ? new List<string>(disciplines)
                    : new List<string>(DefaultBimDisciplines),
                ExportRoutes = DefaultBimRoutes(),
            };
            foreach (var (id, name, discSubs, subs) in BimFolderDefaults)
            {
                s.CustomFolders.Add(new FolderDef
                {
                    Id = id,
                    DisplayName = WithCodeSuffix(name, s.ProjectCode),
                    HasDisciplineSubfolders = discSubs,
                    SubFolders = subs.ToList(),
                    IsCustom = false,
                });
            }
            return s;
        }

        /// <summary>Build a default Mini-mode setup with 5 flat folders.</summary>
        public static ProjectSetup CreateMini(string projectCode, string rootPath)
        {
            var s = new ProjectSetup
            {
                ProjectCode = projectCode ?? "PRJ",
                RootPath = rootPath ?? "",
                Mode = ProjectFolderMode.Mini,
                Disciplines = new List<string>(),
                ExportRoutes = DefaultMiniRoutes(),
            };
            foreach (var (id, name) in MiniFolderDefaults)
            {
                s.CustomFolders.Add(new FolderDef
                {
                    Id = id,
                    DisplayName = WithCodeSuffix(name, s.ProjectCode),
                    HasDisciplineSubfolders = false,
                    SubFolders = new List<string>(),
                    IsCustom = false,
                });
            }
            return s;
        }

        // ── Persistence ────────────────────────────────────────────────────

        private const string SetupFileName = "project_setup.json";

        /// <summary>Read project_setup.json from the given _data folder. Returns null if not present.</summary>
        public static ProjectSetup Load(string dataPath)
        {
            try
            {
                if (string.IsNullOrEmpty(dataPath)) return null;
                string filePath = Path.Combine(dataPath, SetupFileName);
                if (!File.Exists(filePath)) return null;
                var json = File.ReadAllText(filePath);
                var setup = JsonConvert.DeserializeObject<ProjectSetup>(json);
                if (setup == null) return null;
                if (setup.ExportRoutes == null)
                    setup.ExportRoutes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (setup.Disciplines == null) setup.Disciplines = new List<string>();
                if (setup.CustomFolders == null) setup.CustomFolders = new List<FolderDef>();
                if (setup.HiddenFolders == null) setup.HiddenFolders = new List<string>();
                return setup;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ProjectSetup.Load: {ex.Message}");
                return null;
            }
        }

        /// <summary>Write project_setup.json to the given _data folder. Creates folder if missing.</summary>
        public void Save(string dataPath)
        {
            try
            {
                if (string.IsNullOrEmpty(dataPath)) return;
                if (!Directory.Exists(dataPath)) Directory.CreateDirectory(dataPath);
                string filePath = Path.Combine(dataPath, SetupFileName);
                LastModified = DateTime.Now;
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ProjectSetup.Save: {ex.Message}");
            }
        }

        /// <summary>Resolve the absolute root path from a Revit document's path. Honours RootPathIsRelative.</summary>
        public string ResolveRootPath(string rvtPath)
        {
            if (string.IsNullOrEmpty(RootPath)) return null;
            if (!RootPathIsRelative) return RootPath;
            if (string.IsNullOrEmpty(rvtPath)) return null;
            string rvtDir = Path.GetDirectoryName(rvtPath);
            if (string.IsNullOrEmpty(rvtDir)) return null;
            try { return Path.GetFullPath(Path.Combine(rvtDir, RootPath)); }
            catch { return Path.Combine(rvtDir, RootPath); }
        }

        /// <summary>Look up a folder definition by ID (case-insensitive).</summary>
        public FolderDef GetFolder(string folderId)
        {
            if (string.IsNullOrEmpty(folderId)) return null;
            return CustomFolders.FirstOrDefault(f =>
                string.Equals(f.Id, folderId, StringComparison.OrdinalIgnoreCase));
        }
    }
}
