// LegacyDocxRenderer.cs — template engine v1.1 (S06 safety net).
//
// Emergency fallback renderer used only when manifest has use_legacy_renderer:
// true or when MiniWord throws for a specific template. Thin wrapper over the
// OpenXml SDK that walks every Text run and does string substitution with no
// support for loops or conditionals — callers should resolve those upstream.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using StingTools.Core;
using Autodesk.Revit.DB;

namespace Planscape.Docs.Templates
{
    public static class LegacyDocxRenderer
    {
        /// <summary>Renders by flat {{token}} substitution.
        /// No loops, no conditionals — pre-resolve those upstream.</summary>
        public static void Render(string templatePath, TokenContext ctx, string outputPath)
        {
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
                throw new FileNotFoundException("Template not found", templatePath);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            File.Copy(templatePath, outputPath, overwrite: true);

            var values = ctx?.AsDictionary() ?? new Dictionary<string, object>();

            try
            {
                using var wp = WordprocessingDocument.Open(outputPath, isEditable: true);
                var body = wp.MainDocumentPart?.Document?.Body;
                if (body == null) return;

                foreach (var t in body.Descendants<Text>().ToList())
                {
                    string s = t.Text ?? "";
                    if (!s.Contains("{{")) continue;
                    foreach (var kv in values)
                    {
                        string token = "{{" + kv.Key + "}}";
                        if (s.Contains(token))
                            s = s.Replace(token, kv.Value?.ToString() ?? "");
                    }
                    t.Text = s;
                }
                wp.MainDocumentPart.Document.Save();
            }
            catch (Exception ex)
            {
                StingLog.Error($"LegacyDocxRenderer failed for {outputPath}", ex);
                throw;
            }
        }
    }
}
