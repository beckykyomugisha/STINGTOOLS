// TemplateRegistry.cs — template engine v1.1 (S08).
//
// Loads a TemplateManifest + resolves the full file path for each registered
// template against a project-local templates directory. Caches the manifest
// and surfaces validation issues aggregated across every entry.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Core;

namespace Planscape.Docs.Templates
{
    public class TemplateRegistry
    {
        public string TemplatesDir { get; }
        public TemplateManifest Manifest { get; }

        private TemplateRegistry(string templatesDir, TemplateManifest manifest)
        {
            TemplatesDir = templatesDir;
            Manifest = manifest ?? new TemplateManifest();
        }

        /// <summary>Loads a registry from a templates directory.</summary>
        public static TemplateRegistry Load(string templatesDir, TemplateManifest m)
        {
            if (m == null)
            {
                string manifestPath = Path.Combine(templatesDir ?? ".", "manifest.json");
                m = TemplateManifestIO.Load(manifestPath);
            }
            return new TemplateRegistry(templatesDir, m);
        }

        public TemplateEntry ResolveById(string id) => Manifest.FindById(id);

        public TemplateEntry ResolveByPurpose(string family, string purpose)
            => Manifest.FindByPurpose(family, purpose);

        /// <summary>Absolute path to a template file on disk.</summary>
        public string ResolveFilePath(TemplateEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.FileRelative)) return null;
            string baseDir = string.IsNullOrEmpty(TemplatesDir) ? "." : TemplatesDir;
            string path = Path.Combine(baseDir, entry.FileRelative);
            return path;
        }

        /// <summary>Validates manifest structure + that every referenced file exists.</summary>
        public List<ValidationIssue> ValidateAll()
        {
            var issues = new List<ValidationIssue>(Manifest.Validate());
            foreach (var t in Manifest.Templates ?? new List<TemplateEntry>())
            {
                string path = ResolveFilePath(t);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    issues.Add(new ValidationIssue("ERROR", "TEMPLATE_FILE_MISSING",
                        $"Template '{t.Id}' file not found: {path}", t.Id));
            }
            return issues;
        }

        /// <summary>Returns every registered template as a snapshot list.</summary>
        public IReadOnlyList<TemplateEntry> All()
            => (Manifest.Templates ?? new List<TemplateEntry>()).AsReadOnly();
    }
}
