// TemplatePackTests.cs — regression cover for the document template pack.
//
// WHY THESE EXIST
//
// Every one of the 15 content templates shipped with loop bodies written as bare
// {{number}} / {{title}}. MiniWord binds a foreach body only when the token carries
// the collection name ({{documents.number}}), so every generated table — transmittals,
// minutes, RFIs, variations, handover certificates — rendered EMPTY, with literal
// {{endforeach}} left in the document. It shipped that way because nothing rendered a
// template outside Revit and nothing asserted on the result.
//
// The template pack is pure data and MiniWord is Revit-free, so this is the one part of
// the document engine that can be pinned headlessly. These tests fail if anyone
// reintroduces a bare loop token, unbalances a foreach, or breaks expansion outright.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MiniSoftware;
using Xunit;

namespace StingTools.Templates.Tests
{
    public class TemplatePackTests
    {
        private static readonly Regex TokenRx = new(@"\{\{([^}]*)\}\}", RegexOptions.Compiled);

        private static string TemplateDir =>
            Path.Combine(AppContext.BaseDirectory, "_template_sources");

        public static IEnumerable<object[]> AllTemplates() =>
            Directory.Exists(TemplateDir)
                ? Directory.GetFiles(TemplateDir, "*.docx").Select(f => new object[] { Path.GetFileName(f) })
                : Enumerable.Empty<object[]>();

        private static string DocumentText(string path)
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.GetEntry("word/document.xml");
            Assert.NotNull(entry);
            using var sr = new StreamReader(entry!.Open(), Encoding.UTF8);
            return sr.ReadToEnd();
        }

        private static bool IsLoopStart(string t) =>
            t.StartsWith("foreach ", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("#foreach ", StringComparison.OrdinalIgnoreCase);

        private static bool IsLoopEnd(string t) =>
            t.Equals("endforeach", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("/foreach", StringComparison.OrdinalIgnoreCase);

        private static bool IsControl(string t) =>
            IsLoopStart(t) || IsLoopEnd(t) ||
            t.StartsWith("#if", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("if(", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("/if", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("endif", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("image:", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("link:", StringComparison.OrdinalIgnoreCase);

        [Fact]
        public void Template_pack_is_present()
        {
            Assert.True(Directory.Exists(TemplateDir), $"Template pack not copied to {TemplateDir}");
            Assert.NotEmpty(Directory.GetFiles(TemplateDir, "*.docx"));
        }

        /// <summary>
        /// Every token inside a foreach must be qualified with its collection name. A bare
        /// token silently renders nothing, which is exactly how the empty-table defect shipped.
        /// </summary>
        [Theory]
        [MemberData(nameof(AllTemplates))]
        public void Loop_body_tokens_are_qualified(string fileName)
        {
            string xml = DocumentText(Path.Combine(TemplateDir, fileName));
            var stack = new List<string>();
            var offenders = new List<string>();

            foreach (Match m in TokenRx.Matches(xml))
            {
                string tok = m.Groups[1].Value.Trim();
                if (tok.Length == 0) continue;

                if (IsLoopStart(tok)) { stack.Add(tok.Split(' ', 2)[1].Trim()); continue; }
                if (IsLoopEnd(tok)) { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); continue; }
                if (IsControl(tok) || stack.Count == 0) continue;

                if (!tok.Contains('.'))
                    offenders.Add($"{{{{{tok}}}}} inside foreach {stack[^1]}");
            }

            Assert.True(offenders.Count == 0,
                $"{fileName}: loop body tokens must be qualified (e.g. {{{{documents.number}}}}). " +
                $"Unqualified: {string.Join(", ", offenders)}");
        }

        /// <summary>Unbalanced markers leave scaffolding in the issued document.</summary>
        [Theory]
        [MemberData(nameof(AllTemplates))]
        public void Loop_markers_are_balanced(string fileName)
        {
            string xml = DocumentText(Path.Combine(TemplateDir, fileName));
            int depth = 0, minDepth = 0;
            foreach (Match m in TokenRx.Matches(xml))
            {
                string tok = m.Groups[1].Value.Trim();
                if (IsLoopStart(tok)) depth++;
                else if (IsLoopEnd(tok)) { depth--; minDepth = Math.Min(minDepth, depth); }
            }
            Assert.True(minDepth >= 0, $"{fileName}: an endforeach appears before its foreach.");
            Assert.True(depth == 0, $"{fileName}: {depth} unclosed foreach block(s).");
        }

        /// <summary>
        /// End-to-end through the real renderer: a two-row collection must produce two rows of
        /// real data. This is the assertion whose absence let the empty-table defect ship.
        /// </summary>
        [Fact]
        public void Transmittal_document_table_expands_every_row()
        {
            string tpl = Path.Combine(TemplateDir, "transmittal.docx");
            Assert.True(File.Exists(tpl), "transmittal.docx missing from the pack");

            string outPath = Path.Combine(Path.GetTempPath(), $"sting_tpl_{Guid.NewGuid():N}.docx");
            try
            {
                var value = new Dictionary<string, object>
                {
                    ["documents"] = new List<Dictionary<string, object>>
                    {
                        new() { ["number"] = "DOC-001", ["title"] = "Ground Floor Plan", ["revision"] = "P01", ["suitability"] = "S2", ["type"] = "DOCUMENT" },
                        new() { ["number"] = "DOC-002", ["title"] = "Roof Plan",         ["revision"] = "P02", ["suitability"] = "S2", ["type"] = "DOCUMENT" },
                    },
                    ["transmittal.id"] = "TX-0001",
                };
                MiniWord.SaveAsByTemplate(outPath, tpl, value);

                string rendered = DocumentText(outPath);
                string text = Regex.Replace(rendered, "<[^>]+>", "");

                Assert.Contains("DOC-001", text);
                Assert.Contains("Ground Floor Plan", text);
                Assert.Contains("DOC-002", text);   // the SECOND row — proves iteration, not a single bind
                Assert.Contains("Roof Plan", text);
            }
            finally
            {
                try { if (File.Exists(outPath)) File.Delete(outPath); } catch { /* temp cleanup */ }
            }
        }

        /// <summary>
        /// The loop-scaffolding stripper in MiniWordAdapter must remove every marker spelling
        /// and touch no real token. Kept in sync with MiniWordAdapter.LoopMarkerRx.
        /// </summary>
        [Fact]
        public void Loop_marker_pattern_strips_scaffolding_only()
        {
            var rx = new Regex(
                @"\{\{\s*(?:#?(?:foreach|each|loop)\s+[^}]*|/?(?:foreach|each|loop)|end(?:foreach|each|loop))\s*\}\}",
                RegexOptions.IgnoreCase);

            foreach (string marker in new[]
                     {
                         "{{foreach documents}}", "{{endforeach}}", "{{#foreach revision_history}}",
                         "{{/foreach}}", "{{each refs}}", "{{endloop}}", "{{ foreach documents }}",
                     })
                Assert.True(rx.IsMatch(marker), $"scaffolding not stripped: {marker}");

            foreach (string token in new[]
                     {
                         "{{documents.number}}", "{{transmittal.id}}", "{{doc.generated_at}}",
                         "{{people.issued_by}}", "{{link:doc.supersedes}}",
                     })
                Assert.False(rx.IsMatch(token), $"real token wrongly stripped: {token}");
        }
    }
}
