// TemplateEngine.cs — template engine v1.1 (S08).
//
// Façade callers interact with. Given a Revit Document it discovers the
// project's _BIM_COORD/templates/ folder, loads the manifest, and dispatches
// every Render request to the correct renderer based on file extension.
//
// Output path: _BIM_COORD/generated/YYYYMMDD_{doc_number}_{template_id}.{ext}

using System;
using System.IO;
using System.Linq;
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

        /// <summary>Renders a template by id into the default generated/ folder. Returns absolute output path.</summary>
        public string RenderById(string id, TokenContext ctx) => RenderById(id, ctx, null);

        /// <summary>Renders a template by id into <paramref name="outDirOverride"/> (or generated/ when null).</summary>
        public string RenderById(string id, TokenContext ctx, string outDirOverride)
        {
            var entry = Registry.ResolveById(id);
            if (entry == null) throw new InvalidOperationException($"Template '{id}' not registered.");
            return RenderEntry(entry, ctx, outDirOverride);
        }

        /// <summary>
        /// Render a deliverable template into the ISO 19650 CDE tree —
        /// &lt;state&gt;/&lt;discipline&gt;/&lt;contentType&gt; (e.g. 00_WIP/A/Documents) — so the
        /// deliverable document is born inside its CDE container rather than a flat
        /// generated/ folder. Returns the absolute output path.
        /// </summary>
        public string RenderToCde(string id, TokenContext ctx, string state, string discipline, string contentType)
        {
            string dir = StingTools.Core.StingPaths.Cde(Document, state, discipline, contentType);
            if (string.IsNullOrEmpty(dir)) dir = GeneratedDir; // safety fallback
            return RenderById(id, ctx, dir);
        }

        /// <summary>
        /// Move-on-transition cleanup: after a deliverable is (re-)rendered into its current CDE
        /// state, remove any earlier render of the SAME deliverable+template from the other CDE
        /// state folders (and stale-dated copies in the same one), keeping only
        /// <paramref name="keepPath"/>. Ensures a deliverable's document exists in exactly one
        /// CDE state — no orphaned WIP copy after a Publish. Best-effort; never throws.
        /// </summary>
        public void PurgeStaleRenders(string docNumber, string templateId, string discipline, string keepPath)
        {
            try
            {
                if (string.IsNullOrEmpty(docNumber) || string.IsNullOrEmpty(templateId)) return;

                // EXACT match, not a glob. The render is "{yyyyMMdd}_{safeNumber}_{templateId}{ext}",
                // and a "*_{number}_{template}.*" wildcard would also match a DIFFERENT
                // deliverable whose number merely ENDS WITH "_" + this one (Sanitise turns
                // spaces and '/' into '_', so e.g. purging "0001" would delete "PRJ 0001"'s
                // render). Strip the date token and compare the remainder verbatim.
                string expected = Sanitise(docNumber) + "_" + templateId;
                string keepFull = string.IsNullOrEmpty(keepPath) ? null : Path.GetFullPath(keepPath);

                // ARCHIVE is deliberately EXCLUDED: under ISO 19650 the archived issue is the
                // retained record of what was issued, so a later re-issue must never delete it.
                foreach (string state in new[] { "WIP", "SHARED", "PUBLISHED" })
                {
                    string dir = StingTools.Core.StingPaths.Cde(Document, state, discipline, "Documents");
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                    foreach (string f in Directory.GetFiles(dir))
                    {
                        string bare = Path.GetFileNameWithoutExtension(f);
                        int firstUs = bare.IndexOf('_');
                        if (firstUs < 0) continue;                       // no date token
                        string datePart = bare.Substring(0, firstUs);
                        if (datePart.Length != 8 || !datePart.All(char.IsDigit)) continue;
                        if (!string.Equals(bare.Substring(firstUs + 1), expected, StringComparison.OrdinalIgnoreCase)) continue;
                        if (keepFull != null && string.Equals(Path.GetFullPath(f), keepFull, StringComparison.OrdinalIgnoreCase)) continue;
                        try { File.Delete(f); StingLog.Info($"TemplateEngine: purged stale render {f}"); }
                        catch (Exception ex) { StingLog.Warn($"PurgeStaleRenders delete {f}: {ex.Message}"); }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PurgeStaleRenders: {ex.Message}"); }
        }

        /// <summary>Renders a template by (family, purpose) lookup into generated/.</summary>
        public string RenderByPurpose(string family, string purpose, TokenContext ctx)
        {
            var entry = Registry.ResolveByPurpose(family, purpose);
            if (entry == null)
                throw new InvalidOperationException($"No template registered for family='{family}', purpose='{purpose}'.");
            return RenderEntry(entry, ctx, null);
        }

        private string RenderEntry(TemplateEntry entry, TokenContext ctx, string outDirOverride)
        {
            string templatePath = Registry.ResolveFilePath(entry);
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
                throw new FileNotFoundException($"Template file missing for '{entry.Id}'", templatePath);

            string docNumber = ctx != null && ctx.Doc.TryGetValue("number", out var v) && v is string s ? s : "UNKNOWN";
            string safeNumber = Sanitise(docNumber);
            string dateTag = DateTime.Now.ToString("yyyyMMdd");
            string ext = Path.GetExtension(templatePath).ToLowerInvariant();
            string outName = $"{dateTag}_{safeNumber}_{entry.Id}{ext}";
            string outDir = string.IsNullOrEmpty(outDirOverride) ? GeneratedDir : outDirOverride;
            try { Directory.CreateDirectory(outDir); } catch (Exception ex) { StingLog.Warn($"TemplateEngine outDir: {ex.Message}"); }
            string outPath = Path.Combine(outDir, outName);

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
            // Folder consolidation: nest "_BIM_COORD" inside the unified
            // project root's _data folder rather than as a sibling of the .rvt.
            try
            {
                string consolidated = StingTools.Core.ProjectFolderEngine.GetDataPath(doc);
                if (!string.IsNullOrEmpty(consolidated)) return consolidated;
            }
            catch { /* fall through to legacy lookup */ }
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
