// TemplateDoctor.cs — pre-flight validation and post-render inspection for the
// document template engine.
//
// WHY THIS EXISTS
//
// The engine deliberately substitutes "<TOKEN_NOT_FOUND:name>" for tokens it cannot
// resolve — the MiniWordAdapter comment calls it a QA aid "so template authors can
// see misspellings". Nothing ever checked for it. A transmittal whose document table
// was empty and whose header read "<TOKEN_NOT_FOUND:doc.generated_at>" was returned to
// the caller as a finished deliverable, and the caller offered to open it. On an ISO
// 19650 project that document goes to the appointing party.
//
// A marker that nothing inspects is not a QA aid, it is a silent failure. This class
// makes it loud, in two places:
//
//   Inspect(path)  — reads a RENDERED file back and reports unresolved tokens and
//                    leftover scaffolding. TemplateEngine calls it on every render.
//   Validate(...)  — compares a TEMPLATE's tokens against the context it will actually
//                    receive, so a missing producer is caught before anyone issues.
//
// Both are read-only and never throw: a diagnostic that breaks issuing would be worse
// than the problem it reports.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using StingTools.Core;

namespace Planscape.Docs.Templates
{
    /// <summary>What a render or a template check found.</summary>
    public class TemplateHealth
    {
        /// <summary>Tokens that resolved to the not-found marker (e.g. "doc.generated_at").</summary>
        public List<string> UnresolvedTokens { get; } = new List<string>();

        /// <summary>Literal "{{…}}" left in the output — loop scaffolding or unprocessed syntax.</summary>
        public List<string> LeftoverMarkup { get; } = new List<string>();

        /// <summary>Loops the template iterates that the context supplied no rows for.</summary>
        public List<string> EmptyLoops { get; } = new List<string>();

        public bool IsClean => UnresolvedTokens.Count == 0 && LeftoverMarkup.Count == 0;

        /// <summary>One-line summary, or null when clean.</summary>
        public string Summary()
        {
            if (IsClean && EmptyLoops.Count == 0) return null;
            var parts = new List<string>();
            if (UnresolvedTokens.Count > 0)
                parts.Add($"{UnresolvedTokens.Count} unresolved token(s): {Join(UnresolvedTokens)}");
            if (LeftoverMarkup.Count > 0)
                parts.Add($"{LeftoverMarkup.Count} leftover marker(s): {Join(LeftoverMarkup)}");
            if (EmptyLoops.Count > 0)
                parts.Add($"{EmptyLoops.Count} empty table(s): {Join(EmptyLoops)}");
            return string.Join(" | ", parts);
        }

        private static string Join(List<string> items)
        {
            const int max = 6;
            var head = items.Take(max).ToList();
            string s = string.Join(", ", head);
            return items.Count > max ? $"{s} … (+{items.Count - max} more)" : s;
        }
    }

    public static class TemplateDoctor
    {
        private static readonly Regex NotFoundRx = new Regex(
            @"<TOKEN_NOT_FOUND:(?<name>[^>]*)>", RegexOptions.Compiled);

        private static readonly Regex MustacheRx = new Regex(
            @"\{\{[^}]*\}\}", RegexOptions.Compiled);

        /// <summary>
        /// Read a rendered .docx back and report what did not resolve. Returns an empty
        /// (clean) result for formats it cannot open — absence of evidence, not a pass.
        /// </summary>
        public static TemplateHealth Inspect(string renderedPath)
        {
            var health = new TemplateHealth();
            if (string.IsNullOrEmpty(renderedPath) || !File.Exists(renderedPath)) return health;
            if (!renderedPath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) return health;

            try
            {
                string text;
                using (var wp = WordprocessingDocument.Open(renderedPath, isEditable: false))
                    text = wp.MainDocumentPart?.Document?.InnerText ?? "";

                foreach (Match m in NotFoundRx.Matches(text))
                {
                    string name = m.Groups["name"].Value.Trim();
                    if (name.Length > 0 && !health.UnresolvedTokens.Contains(name))
                        health.UnresolvedTokens.Add(name);
                }
                foreach (Match m in MustacheRx.Matches(text))
                {
                    string tok = m.Value;
                    if (!health.LeftoverMarkup.Contains(tok)) health.LeftoverMarkup.Add(tok);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TemplateDoctor.Inspect({Path.GetFileName(renderedPath)}): {ex.Message}");
            }
            return health;
        }

        /// <summary>
        /// Check a TEMPLATE against the context it will be rendered with, before rendering.
        /// Reports tokens with no value and loops with no rows.
        /// </summary>
        public static TemplateHealth Validate(string templatePath, TokenContext ctx)
        {
            var health = new TemplateHealth();
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath)) return health;
            if (!templatePath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) return health;

            try
            {
                string text;
                using (var wp = WordprocessingDocument.Open(templatePath, isEditable: false))
                    text = wp.MainDocumentPart?.Document?.InnerText ?? "";

                var dict = ctx?.AsDictionary() ?? new Dictionary<string, object>();
                var loopStack = new List<string>();

                foreach (Match m in Regex.Matches(text, @"\{\{(?<t>[^}]*)\}\}"))
                {
                    string tok = m.Groups["t"].Value.Trim();
                    if (tok.Length == 0) continue;

                    if (TokenResolver.IsLoopStart(tok))
                    {
                        string name = TokenResolver.LoopName(tok);
                        if (!string.IsNullOrEmpty(name))
                        {
                            loopStack.Add(name);
                            bool hasRows = dict.TryGetValue(name, out object rows)
                                           && rows is List<Dictionary<string, object>> list && list.Count > 0;
                            if (!hasRows && !health.EmptyLoops.Contains(name)) health.EmptyLoops.Add(name);
                        }
                        continue;
                    }
                    if (TokenResolver.IsLoopEnd(tok))
                    {
                        if (loopStack.Count > 0) loopStack.RemoveAt(loopStack.Count - 1);
                        continue;
                    }
                    if (TokenResolver.IsIfStart(tok) || TokenResolver.IsIfEnd(tok)) continue;
                    if (tok.StartsWith("image:", StringComparison.OrdinalIgnoreCase) ||
                        tok.StartsWith("link:",  StringComparison.OrdinalIgnoreCase)) continue;

                    // Inside a loop the token is bound per row, not from the flat dictionary.
                    if (loopStack.Count > 0 &&
                        tok.StartsWith(loopStack[loopStack.Count - 1] + ".", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!dict.ContainsKey(tok) && !health.UnresolvedTokens.Contains(tok))
                        health.UnresolvedTokens.Add(tok);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TemplateDoctor.Validate({Path.GetFileName(templatePath)}): {ex.Message}");
            }
            return health;
        }
    }
}
