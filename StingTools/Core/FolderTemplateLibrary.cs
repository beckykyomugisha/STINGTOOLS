using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace StingTools.Core
{
    /// <summary>
    /// Reusable folder-structure template — a saved profile of mode, disciplines,
    /// folder defs, hidden folders, export routes, and naming convention.
    /// Built-in templates ship with the plugin; user templates live in
    /// {ProjectCode}\_data\folder_templates\*.json.
    /// </summary>
    public class FolderTemplate
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public ProjectFolderMode Mode { get; set; } = ProjectFolderMode.BIM;
        public List<string> Disciplines { get; set; } = new();
        public List<FolderDef> CustomFolders { get; set; } = new();
        public List<string> HiddenFolders { get; set; } = new();
        public Dictionary<string, string> ExportRoutes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public NamingConvention NamingConvention { get; set; } = NamingConvention.ISO19650;
        public bool IsBuiltIn { get; set; }
    }

    /// <summary>Static library of folder templates — 4 built-ins + user-saved.</summary>
    public static class FolderTemplateLibrary
    {
        /// <summary>The 4 shipped templates.</summary>
        public static readonly List<FolderTemplate> BuiltInTemplates = new()
        {
            BuildFullBim(),
            BuildMepOnly(),
            BuildMiniProject(),
            BuildStructuralOnly(),
        };

        // ── Built-in template factories ────────────────────────────────────

        private static FolderTemplate BuildFullBim()
        {
            var setup = ProjectSetup.CreateBIM("PRJ", "");
            return new FolderTemplate
            {
                Name = "Full BIM — ISO 19650",
                Description = "All 16 numbered folders with discipline routing (A/E/M/P/S).",
                Mode = ProjectFolderMode.BIM,
                Disciplines = new List<string>(setup.Disciplines),
                CustomFolders = setup.CustomFolders,
                HiddenFolders = new List<string>(),
                ExportRoutes = setup.ExportRoutes,
                NamingConvention = NamingConvention.ISO19650,
                IsBuiltIn = true,
            };
        }

        private static FolderTemplate BuildMepOnly()
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "WIP", "SHARED", "PUBLISHED", "ARCHIVE", "MODELS", "DRAWINGS",
                "SCHEDULES", "COBIE", "TRANSMITTALS", "ISSUES", "CLASHES", "HANDOVER",
            };
            var setup = ProjectSetup.CreateBIM("PRJ", "",
                new List<string> { "E_Electrical", "M_Mechanical", "P_Plumbing" });
            return new FolderTemplate
            {
                Name = "MEP Only",
                Description = "Reduced BIM tree for MEP-only projects (12 folders, 3 disciplines).",
                Mode = ProjectFolderMode.BIM,
                Disciplines = new List<string>(setup.Disciplines),
                CustomFolders = setup.CustomFolders.Where(f => keep.Contains(f.Id)).ToList(),
                HiddenFolders = setup.CustomFolders.Where(f => !keep.Contains(f.Id))
                    .Select(f => f.Id).ToList(),
                ExportRoutes = setup.ExportRoutes,
                NamingConvention = NamingConvention.ISO19650,
                IsBuiltIn = true,
            };
        }

        private static FolderTemplate BuildMiniProject()
        {
            var setup = ProjectSetup.CreateMini("PRJ", "");
            return new FolderTemplate
            {
                Name = "Mini Project",
                Description = "5 flat folders: Drawings, Models, Schedules, Documents, Reports.",
                Mode = ProjectFolderMode.Mini,
                Disciplines = new List<string>(),
                CustomFolders = setup.CustomFolders,
                HiddenFolders = new List<string>(),
                ExportRoutes = setup.ExportRoutes,
                NamingConvention = NamingConvention.Timestamp,
                IsBuiltIn = true,
            };
        }

        private static FolderTemplate BuildStructuralOnly()
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "WIP", "SHARED", "PUBLISHED", "ARCHIVE", "MODELS", "DRAWINGS",
                "SCHEDULES", "REGISTERS", "COMPLIANCE",
            };
            var setup = ProjectSetup.CreateBIM("PRJ", "",
                new List<string> { "S_Structural", "A_Architectural" });
            return new FolderTemplate
            {
                Name = "Structural Only",
                Description = "Lean structural workflow (9 folders, S+A disciplines).",
                Mode = ProjectFolderMode.BIM,
                Disciplines = new List<string>(setup.Disciplines),
                CustomFolders = setup.CustomFolders.Where(f => keep.Contains(f.Id)).ToList(),
                HiddenFolders = setup.CustomFolders.Where(f => !keep.Contains(f.Id))
                    .Select(f => f.Id).ToList(),
                ExportRoutes = setup.ExportRoutes,
                NamingConvention = NamingConvention.ISO19650,
                IsBuiltIn = true,
            };
        }

        // ── User template load/save/delete ─────────────────────────────────

        /// <summary>Load every *.json template from the user's templates folder.</summary>
        public static List<FolderTemplate> LoadUserTemplates(string templateDir)
        {
            var list = new List<FolderTemplate>();
            try
            {
                if (string.IsNullOrEmpty(templateDir) || !Directory.Exists(templateDir)) return list;
                foreach (string file in Directory.GetFiles(templateDir, "*.json"))
                {
                    try
                    {
                        var t = JsonConvert.DeserializeObject<FolderTemplate>(File.ReadAllText(file));
                        if (t != null && !string.IsNullOrEmpty(t.Name))
                        {
                            t.IsBuiltIn = false;
                            list.Add(t);
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"FolderTemplateLibrary: skip {file}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"FolderTemplateLibrary.LoadUserTemplates: {ex.Message}"); }
            return list;
        }

        /// <summary>Built-ins + user templates from the given templates folder.</summary>
        public static List<FolderTemplate> GetAll(string templateDir)
        {
            var list = new List<FolderTemplate>(BuiltInTemplates);
            list.AddRange(LoadUserTemplates(templateDir));
            return list;
        }

        /// <summary>Save (or overwrite) a user template under {Name}.json.</summary>
        public static void SaveUserTemplate(FolderTemplate template, string templateDir)
        {
            try
            {
                if (template == null || string.IsNullOrEmpty(template.Name)) return;
                if (!Directory.Exists(templateDir)) Directory.CreateDirectory(templateDir);
                string safeName = Core.OutputLocationHelper.MakeSafeFileName(template.Name);
                string filePath = Path.Combine(templateDir, safeName + ".json");
                template.IsBuiltIn = false;
                File.WriteAllText(filePath, JsonConvert.SerializeObject(template, Formatting.Indented));
                StingLog.Info($"FolderTemplateLibrary: saved {template.Name} → {filePath}");
            }
            catch (Exception ex) { StingLog.Warn($"FolderTemplateLibrary.SaveUserTemplate: {ex.Message}"); }
        }

        /// <summary>Delete a user template (built-ins cannot be deleted).</summary>
        public static void DeleteUserTemplate(string name, string templateDir)
        {
            try
            {
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(templateDir)) return;
                string safeName = Core.OutputLocationHelper.MakeSafeFileName(name);
                string filePath = Path.Combine(templateDir, safeName + ".json");
                if (File.Exists(filePath)) File.Delete(filePath);
            }
            catch (Exception ex) { StingLog.Warn($"FolderTemplateLibrary.DeleteUserTemplate: {ex.Message}"); }
        }

        /// <summary>Materialise a template into a new ProjectSetup bound to a project code + root.</summary>
        public static ProjectSetup ApplyTemplate(FolderTemplate template, string projectCode, string rootPath)
        {
            if (template == null) return ProjectSetup.CreateBIM(projectCode, rootPath);
            var s = new ProjectSetup
            {
                ProjectCode = projectCode ?? "PRJ",
                RootPath = rootPath ?? "",
                Mode = template.Mode,
                Disciplines = new List<string>(template.Disciplines ?? new List<string>()),
                CustomFolders = (template.CustomFolders ?? new List<FolderDef>())
                    .Select(f => new FolderDef
                    {
                        Id = f.Id,
                        DisplayName = f.DisplayName,
                        HasDisciplineSubfolders = f.HasDisciplineSubfolders,
                        SubFolders = new List<string>(f.SubFolders ?? new List<string>()),
                        IsCustom = f.IsCustom,
                    }).ToList(),
                HiddenFolders = new List<string>(template.HiddenFolders ?? new List<string>()),
                ExportRoutes = new Dictionary<string, string>(
                    template.ExportRoutes ?? new Dictionary<string, string>(),
                    StringComparer.OrdinalIgnoreCase),
                NamingConvention = template.NamingConvention,
                TemplateName = template.Name,
                CreatedDate = DateTime.Now,
                LastModified = DateTime.Now,
            };
            return s;
        }
    }
}
