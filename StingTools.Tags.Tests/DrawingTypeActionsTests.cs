// The Drawing Type Editor's "All Actions" tab must offer exactly what the dock
// panel's DRAWING TYPES section offers.
//
// The editor used to carry its own hand-picked toolbar of eight buttons while
// the dock section grew to forty-one, and nothing noticed. DrawingTypeActions is
// now the editor's list; this test reads the dock XAML and fails on any
// difference in group, label or tag, in either direction.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class DrawingTypeActionsTests
    {
        private static string DockXaml()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "StingTools", "UI", "StingDockPanel.xaml"))) d = d.Parent;
            Assert.True(d != null, "could not locate StingTools/UI/StingDockPanel.xaml");
            return File.ReadAllText(Path.Combine(d.FullName, "StingTools", "UI", "StingDockPanel.xaml"));
        }

        /// <summary>(group, label, tag) for every button in the dock's DRAWING TYPES section, in order.</summary>
        private static List<(string Group, string Label, string Tag)> DockSection()
        {
            var x = DockXaml();
            int start = x.IndexOf("Text=\"📐 DRAWING TYPES\"", StringComparison.Ordinal);
            int end = x.IndexOf("SectionLabel", start + 1, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start, "DRAWING TYPES section not found in the dock XAML");
            string group = DrawingTypeActions.MainGroup;
            var result = new List<(string, string, string)>();
            var token = new Regex(
                "Header=\"(?<exp>[^\"]+)\"|<TextBlock Text=\"(?<sub>[^\"]+)\" FontWeight=\"SemiBold\"|<Button[^>]*?Content=\"(?<label>[^\"]*)\" Tag=\"(?<tag>[^\"]+)\"");
            foreach (Match m in token.Matches(x.Substring(start, end - start)))
            {
                if (m.Groups["exp"].Success) group = m.Groups["exp"].Value;
                else if (m.Groups["sub"].Success) group = m.Groups["sub"].Value;
                else result.Add((group, WebUtility.HtmlDecode(m.Groups["label"].Value), m.Groups["tag"].Value));
            }
            return result;
        }

        [Fact]
        public void The_editor_offers_exactly_the_dock_sections_actions()
        {
            var dock = DockSection().Where(b => b.Tag != DrawingTypeActions.EditorTag).ToList();
            Assert.True(dock.Count >= 40, $"expected the full DRAWING TYPES section; read {dock.Count} buttons");
            var editor = DrawingTypeActions.All.Select(a => (a.Group, a.Label, a.Tag)).ToList();

            var missing = dock.Except(editor).Select(b => $"  add to DrawingTypeActions: [{b.Group}] '{b.Label}' → {b.Tag}");
            var extra = editor.Except(dock).Select(b => $"  not in the dock (remove, or add the button): [{b.Group}] '{b.Label}' → {b.Tag}");
            var diff = missing.Concat(extra).ToList();
            Assert.True(diff.Count == 0,
                "The Drawing Type Editor's All Actions tab and the dock panel's DRAWING TYPES section disagree:\n"
                + string.Join("\n", diff));
        }

        [Fact]
        public void Every_action_appears_once()
        {
            var dupes = DrawingTypeActions.All.GroupBy(a => a.Tag).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(dupes.Count == 0, "listed twice: " + string.Join(", ", dupes));
        }

        [Fact]
        public void The_editor_does_not_offer_to_open_itself()
            => Assert.Null(DrawingTypeActions.ByTag(DrawingTypeActions.EditorTag));
    }
}
