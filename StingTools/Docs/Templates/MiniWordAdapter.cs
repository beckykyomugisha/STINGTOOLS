// MiniWordAdapter.cs — template engine v1.1 (S06).
//
// Thin facade over MiniSoftware.MiniWord. Does three things MiniWord does not
// do itself:
//   1. Pre-processes {{#if ...}}...{{/if}} blocks so the conditional text
//      never reaches MiniWord when the expression is falsy.
//   2. Flattens tokens that must exist in the dictionary (MiniWord leaves
//      unknown tokens untouched — we substitute <TOKEN_NOT_FOUND:name>).
//   3. Post-processes {{link:doc.supersedes}} placeholders and core
//      document properties (author / last modified by).

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MiniSoftware;
using StingTools.Core;
using Autodesk.Revit.DB;

namespace Planscape.Docs.Templates
{
    public static class MiniWordAdapter
    {
        private static readonly Regex IfBlockRx = new Regex(
            @"\{\{#if\s+(?<expr>[^}]+)\}\}(?<body>.*?)\{\{\/if\}\}",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private static readonly Regex LinkTokenRx = new Regex(
            @"\{\{link:(?<path>[^}]+)\}\}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Renders <paramref name="templatePath"/> with the supplied context to <paramref name="outputPath"/>.</summary>
        public static void Render(string templatePath, TokenContext ctx, string outputPath)
        {
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
                throw new FileNotFoundException($"Template not found: {templatePath}", templatePath);
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

            string processed = PreProcess(templatePath, ctx);
            try
            {
                var dict = EnsureAllTokens(processed, ctx);
                MiniWord.SaveAsByTemplate(outputPath, processed, dict);
                PostProcess(outputPath, ctx);
            }
            finally
            {
                if (!string.Equals(processed, templatePath, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(processed))
                {
                    try { File.Delete(processed); } catch { /* best-effort */ }
                }
            }
        }

        // ── Pre-process: resolve {{#if}}…{{/if}} conditionals by rewriting the docx ──
        //
        // We copy the template to a tmp path, walk every paragraph text run, and
        // strip falsy blocks. MiniWord then handles token substitution and its own
        // loop expansion on the pre-processed artefact.
        private static string PreProcess(string templatePath, TokenContext ctx)
        {
            try
            {
                string tmp = Path.Combine(Path.GetTempPath(),
                    $"planscape_{Guid.NewGuid():N}_{Path.GetFileName(templatePath)}");
                File.Copy(templatePath, tmp, overwrite: true);

                using (var wp = WordprocessingDocument.Open(tmp, isEditable: true))
                {
                    var body = wp.MainDocumentPart?.Document?.Body;
                    if (body == null) return tmp;

                    foreach (var p in body.Descendants<Paragraph>().ToList())
                    {
                        string txt = p.InnerText ?? "";
                        if (!txt.Contains("{{#if", StringComparison.OrdinalIgnoreCase)) continue;

                        string rewritten = IfBlockRx.Replace(txt, match =>
                        {
                            string expr = match.Groups["expr"].Value.Trim();
                            bool keep = TokenResolver.EvaluateIf(expr, ctx);
                            return keep ? match.Groups["body"].Value : "";
                        });

                        if (rewritten != txt)
                        {
                            p.RemoveAllChildren<Run>();
                            p.AppendChild(new Run(new Text(rewritten) { Space = SpaceProcessingModeValues.Preserve }));
                        }
                    }
                    wp.MainDocumentPart.Document.Save();
                }
                return tmp;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MiniWordAdapter.PreProcess failed for '{templatePath}': {ex.Message}. Using original template.");
                return templatePath;
            }
        }

        // ── Ensure every token referenced by the template has a key in the dict ──
        //
        // MiniWord silently ignores tokens whose keys are missing. We want
        // <TOKEN_NOT_FOUND:name> so template authors can see misspellings in QA.
        private static Dictionary<string, object> EnsureAllTokens(string templatePath, TokenContext ctx)
        {
            var dict = ctx.AsDictionary();
            try
            {
                using (var wp = WordprocessingDocument.Open(templatePath, isEditable: false))
                {
                    string text = wp.MainDocumentPart?.Document?.InnerText ?? "";
                    foreach (var raw in TokenResolver.FindAllTokens(text))
                    {
                        string key = raw;
                        if (TokenResolver.IsLoopStart(key) || TokenResolver.IsLoopEnd(key)) continue;
                        if (TokenResolver.IsIfStart(key)   || TokenResolver.IsIfEnd(key))   continue;
                        if (key.StartsWith("image:", StringComparison.OrdinalIgnoreCase) ||
                            key.StartsWith("link:",  StringComparison.OrdinalIgnoreCase)) continue;
                        if (!dict.ContainsKey(key))
                            dict[key] = $"<TOKEN_NOT_FOUND:{key}>";
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MiniWordAdapter.EnsureAllTokens failed: {ex.Message}");
            }
            return dict;
        }

        // ── Post-process: wire {{link:...}} placeholders + set core props ──
        private static void PostProcess(string outputPath, TokenContext ctx)
        {
            try
            {
                using (var wp = WordprocessingDocument.Open(outputPath, isEditable: true))
                {
                    var body = wp.MainDocumentPart?.Document?.Body;
                    if (body != null)
                    {
                        foreach (var p in body.Descendants<Paragraph>().ToList())
                        {
                            string txt = p.InnerText ?? "";
                            if (!txt.Contains("{{link:", StringComparison.OrdinalIgnoreCase)) continue;

                            string rewritten = LinkTokenRx.Replace(txt, match =>
                            {
                                string path = match.Groups["path"].Value.Trim();
                                string resolved = TokenResolver.Resolve(path, ctx);
                                if (string.IsNullOrEmpty(resolved) || resolved.StartsWith("<TOKEN_NOT_FOUND"))
                                    return "";
                                return resolved;
                            });

                            if (rewritten != txt)
                            {
                                p.RemoveAllChildren<Run>();
                                p.AppendChild(new Run(new Text(rewritten) { Space = SpaceProcessingModeValues.Preserve }));
                            }
                        }
                        wp.MainDocumentPart.Document.Save();
                    }

                    // Set core properties — author/last-modified-by.
                    var corePart = wp.CoreFilePropertiesPart ?? wp.AddCoreFilePropertiesPart();
                    using (var stream = corePart.GetStream(FileMode.Create, FileAccess.Write))
                    using (var writer = new System.IO.StreamWriter(stream))
                    {
                        string author = SafeCtxString(ctx, "project.company_name", "Planscape Limited");
                        string lastBy = SafeCtxString(ctx, "people.issued_by", Environment.UserName);
                        writer.Write(
                            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
                            "<cp:coreProperties " +
                                "xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
                                "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
                                "xmlns:dcterms=\"http://purl.org/dc/terms/\" " +
                                "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                            $"<dc:creator>{System.Security.SecurityElement.Escape(author)}</dc:creator>" +
                            $"<cp:lastModifiedBy>{System.Security.SecurityElement.Escape(lastBy)}</cp:lastModifiedBy>" +
                            $"<dcterms:modified xsi:type=\"dcterms:W3CDTF\">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</dcterms:modified>" +
                            "</cp:coreProperties>");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MiniWordAdapter.PostProcess failed for '{outputPath}': {ex.Message}");
            }
        }

        private static string SafeCtxString(TokenContext ctx, string key, string fallback)
        {
            if (ctx == null) return fallback;
            var dict = ctx.AsDictionary();
            if (dict.TryGetValue(key, out var v) && v is string s && !string.IsNullOrEmpty(s)) return s;
            return fallback;
        }
    }
}
