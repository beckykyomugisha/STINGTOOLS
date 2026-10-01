using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-68 — a pack's viewTemplate is only read by the editor's "push pack
    /// template to bound types"; it must name a template View Templates makes
    /// (its fixed TemplateDefs, or the drawing-type catalogue). Eleven packs
    /// named templates nothing created.
    /// </summary>
    public class PackViewTemplateNamesTests
    {
        private static HashSet<string> CreatedTemplates()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var src = DrawingCatalogueFixture.Source("Temp", "TemplateCommands.cs");
            int start = src.IndexOf("TemplateDefs = new[]", StringComparison.Ordinal);
            int end = src.IndexOf("};", start, StringComparison.Ordinal);
            Assert.True(start > 0 && end > start, "ViewTemplatesCommand.TemplateDefs not found");
            foreach (Match m in Regex.Matches(src.Substring(start, end - start), "\\(\"(STING - [^\"]+)\""))
                set.Add(m.Groups[1].Value);
            Assert.True(set.Count >= 30, $"Only {set.Count} fixed template defs parsed");
            foreach (var s in DrawingTemplateCatalogue.Plan(DrawingCatalogueFixture.Shipped().DrawingTypes).Creatable)
                set.Add(s.Name);
            return set;
        }

        [Fact]
        public void Every_pack_view_template_is_one_View_Templates_creates()
        {
            var created = CreatedTemplates();
            var packs = JObject.Parse(File.ReadAllText(Path.Combine(
                DrawingCatalogueFixture.RepoRoot(), "StingTools", "Data", "STING_VIEW_STYLE_PACKS.json")));
            var missing = new List<string>();
            int seen = 0;
            foreach (var p in packs["stylePacks"])
            {
                var vt = p.Value<string>("viewTemplate");
                if (string.IsNullOrWhiteSpace(vt) || DrawingTemplateCatalogue.IsManagedName(vt)) continue;
                seen++;
                if (!created.Contains(vt)) missing.Add($"{p.Value<string>("id")}: '{vt}'");
            }
            Assert.True(seen >= 20, $"Only {seen} pack templates read");
            Assert.True(missing.Count == 0, "Pack view templates nothing creates:\n  " + string.Join("\n  ", missing));
        }

        [Fact]
        public void The_dead_pack_template_fallback_is_gone_and_the_push_is_validated()
        {
            var pres = DrawingCatalogueFixture.Source("Core", "Drawing", "DrawingTypePresentation.cs");
            Assert.DoesNotContain("effectiveTemplateName", pres);
            var editor = DrawingCatalogueFixture.Source("UI", "DrawingTypeEditorDialog.cs");
            Assert.Contains("DrawingTemplateCatalogue.Plan(_types).Creatable", editor);
        }
    }
}
