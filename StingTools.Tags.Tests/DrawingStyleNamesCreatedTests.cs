using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-64 — text and line style names the drawing data asks for must be
    /// ones the style creators make, and the drawing setup chain must run those
    /// creators. The match-line caption type "STING - 2.5mm" was created by
    /// nothing, so every caption was silently skipped.
    /// </summary>
    public class DrawingStyleNamesCreatedTests
    {
        private static string Data(string file) => Path.Combine(DrawingCatalogueFixture.RepoRoot(), "StingTools", "Data", file);

        /// <summary>Names in TemplateManager.TextStyleDefs (Revit-bound, so read as text).</summary>
        private static HashSet<string> CreatedTextStyles()
        {
            var src = DrawingCatalogueFixture.Source("Temp", "TemplateManagerCommands.cs");
            int start = src.IndexOf("TextStyleDefs =", StringComparison.Ordinal);
            int end = src.IndexOf("};", start, StringComparison.Ordinal);
            Assert.True(start > 0 && end > start, "TextStyleDefs not found");
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(src.Substring(start, end - start), "\\(\"(STING - [^\"]+)\""))
                set.Add(m.Groups[1].Value);
            Assert.True(set.Count >= 10, $"Only {set.Count} text styles parsed");
            return set;
        }

        /// <summary>Line styles Create Line Styles makes from MR_SCHEDULES.csv ("STING - " + name).</summary>
        private static HashSet<string> CreatedLineStyles()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(Data("MR_SCHEDULES.csv")))
            {
                if (!line.StartsWith("LINE_STYLE,", StringComparison.Ordinal)) continue;
                var cols = line.Split(',');
                if (cols.Length > 3) set.Add("STING - " + cols[3].Trim());
            }
            Assert.True(set.Count >= 20, $"Only {set.Count} line styles parsed");
            return set;
        }

        [Fact]
        public void Match_line_styles_are_created()
        {
            var cfg = JObject.Parse(File.ReadAllText(Data("STING_MATCH_LINES.json")));
            Assert.Contains(cfg["captions"].Value<string>("fallbackTextNoteTypeName"), CreatedTextStyles());
            Assert.Contains(cfg["geometry"].Value<string>("lineStyleName"), CreatedLineStyles());
        }

        [Fact]
        public void Every_pack_text_style_is_created()
        {
            var created = CreatedTextStyles();
            var packs = JObject.Parse(File.ReadAllText(Data("STING_VIEW_STYLE_PACKS.json")));
            var missing = new List<string>();
            int seen = 0;
            foreach (var p in packs["stylePacks"])
                foreach (var name in new[] { p.Value<string>("textStyle"), p["appearance"]?.Value<string>("textStyleName") })
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    seen++;
                    if (!created.Contains(name)) missing.Add($"{p.Value<string>("id")}: '{name}'");
                }
            Assert.True(seen >= 3, $"Only {seen} pack text styles read");
            Assert.True(missing.Count == 0, "Pack text styles no creator makes:\n  " + string.Join("\n  ", missing));
        }

        [Fact]
        public void Drawing_setup_creates_text_and_line_styles()
        {
            var wf = JObject.Parse(File.ReadAllText(Data("WORKFLOW_DrawingProductionSetup.json")));
            var tags = wf["steps"].Select(s => s.Value<string>("commandTag")).ToList();
            Assert.Contains("CreateTextStyles", tags);
            Assert.Contains("CreateLineStyles", tags);
        }
    }
}
