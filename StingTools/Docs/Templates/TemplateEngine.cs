// TemplateEngine.cs — template engine v1.1 (S08).
//
// Façade callers interact with. Given a Revit Document it discovers the
// project's _BIM_COORD/templates/ folder, loads the manifest, and dispatches
// every Render request to the correct renderer based on file extension.
//
// Output path: _BIM_COORD/generated/YYYYMMDD_{doc_number}_{template_id}.{ext}

using System;
using System.IO;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace Planscape.Docs.Templates
{
    public class TemplateEngine
    {
        public Document Document { get; }
        public TemplateRegistry Registry { get; }
        public string ProjectRoot { get; }
        public string TemplatesDir { get; }
        public string GeneratedDir { get; }

        public TemplateEngine(Document doc)
        {
            Document = doc;
            ProjectRoot = ResolveProjectRoot(doc);
            TemplatesDir = Path.Combine(ProjectRoot, "_BIM_COORD", "templates");
            GeneratedDir = Path.Combine(ProjectRoot, "_BIM_COORD", "generated");
            Directory.CreateDirectory(TemplatesDir);
            Directory.CreateDirectory(GeneratedDir);

            var manifest = TemplateManifestIO.Load(Path.Combine(TemplatesDir, "manifest.json"));
            Registry = TemplateRegistry.Load(TemplatesDir, manifest);
        }

        /// <summary>Renders a template by id. Returns absolute output path.</summary>
        public string RenderById(string id, TokenContext ctx)
        {
            var entry = Registry.ResolveById(id);
            if (entry == null) throw new InvalidOperationException($"Template '{id}' not registered.");
            return RenderEntry(entry, ctx);
        }

        /// <summary>Renders a template by (family, purpose) lookup.</summary>
        public string RenderByPurpose(string family, string purpose, TokenContext ctx)
        {
            var entry = Registry.ResolveByPurpose(family, purpose);
            if (entry == null)
                throw new InvalidOperationException($"No template registered for family='{family}', purpose='{purpose}'.");
            return RenderEntry(entry, ctx);
        }

        private string RenderEntry(TemplateEntry entry, TokenContext ctx)
        {
            string templatePath = Registry.ResolveFilePath(entry);
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
                throw new FileNotFoundException($"Template file missing for '{entry.Id}'", templatePath);

            string docNumber = ctx != null && ctx.Doc.TryGetValue("number", out var v) && v is string s ? s : "UNKNOWN";
            string safeNumber = Sanitise(docNumber);
            string dateTag = DateTime.Now.ToString("yyyyMMdd");
            string ext = Path.GetExtension(templatePath).ToLowerInvariant();
            string outName = $"{dateTag}_{safeNumber}_{entry.Id}{ext}";
            string outPath = Path.Combine(GeneratedDir, outName);

            switch (ext)
            {
                case ".docx":
                    if (Registry.Manifest.UseLegacyRenderer)
                    {
                        StingLog.Info($"TemplateEngine: using legacy renderer for {entry.Id}");
                        LegacyDocxRenderer.Render(templatePath, ctx, outPath);
                    }
                    else
                    {
                        MiniWordAdapter.Render(templatePath, ctx, outPath);
                    }
                    break;

                case ".xlsx":
                    XlsxTemplateRenderer.Render(templatePath, ctx, outPath);
                    break;

                default:
                    throw new NotSupportedException($"Unsupported template extension '{ext}' for '{entry.Id}'.");
            }

            StingLog.Info($"TemplateEngine: rendered {entry.Id} → {outPath}");
            return outPath;
        }

        private static string Sanitise(string s)
        {
            if (string.IsNullOrEmpty(s)) return "doc";
            foreach (char invalid in Path.GetInvalidFileNameChars()) s = s.Replace(invalid, '_');
            return s.Replace(' ', '_');
        }

        private static string ResolveProjectRoot(Document doc)
        {
            try
            {
                string p = doc?.PathName;
                if (!string.IsNullOrEmpty(p))
                {
                    string dir = Path.GetDirectoryName(p);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
                }
            }
            catch { /* ignored */ }
            return Path.Combine(Path.GetTempPath(), "Planscape", "BIMCoord");
        }
    }
}
