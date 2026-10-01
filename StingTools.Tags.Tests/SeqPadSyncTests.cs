using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// TAGACC-24: the SEQ pad (<c>TagConfig.EffectiveSeqPad</c>) prefers <c>SeqPadWidth</c>
    /// over <c>ParamRegistry.NumPad</c>, and only the dock panel set it. Every format source
    /// (project load, Tag Format command, panel) goes through
    /// <c>ParamRegistry.ApplyTagFormatOverrides</c>, which must keep the two in step —
    /// before the segment-order validation can return early.
    /// </summary>
    public class SeqPadSyncTests
    {
        [Fact]
        public void ApplyTagFormatOverrides_keeps_SeqPadWidth_in_step_with_NumPad()
        {
            string src = File.ReadAllText(Path.Combine(DrawingCatalogueFixture.RepoRoot(),
                "StingTools", "Core", "ParamRegistry.cs"));
            int start = src.IndexOf("public static void ApplyTagFormatOverrides(");
            Assert.True(start > 0, "ApplyTagFormatOverrides not found");
            int end = src.IndexOf("\n        }", start);
            string body = src.Substring(start, end - start);

            int set = body.IndexOf("TagConfig.SeqPadWidth = NumPad;");
            Assert.True(set > 0, "ApplyTagFormatOverrides does not set TagConfig.SeqPadWidth");
            int pad = body.IndexOf("_overrideNumPad = numPad;");
            int firstReturn = Regex.Match(body, @"\breturn;").Index;
            Assert.True(set > pad, "SeqPadWidth must be set after the NumPad override");
            Assert.True(firstReturn == 0 || set < firstReturn, "SeqPadWidth must be set before any early return");
        }

        [Fact]
        public void Nothing_else_but_the_panel_writes_SeqPadWidth_directly()
        {
            // A second writer would let the SEQ pad and NumPad drift apart again.
            string root = Path.Combine(DrawingCatalogueFixture.RepoRoot(), "StingTools");
            var sep = Path.DirectorySeparatorChar;
            foreach (var f in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains(sep + "obj" + sep) || f.Contains(sep + "bin" + sep)) continue;
                string name = Path.GetFileName(f);
                if (name == "ParamRegistry.cs" || name == "StingDockPanel.xaml.cs") continue;
                Assert.DoesNotMatch(@"TagConfig\.SeqPadWidth\s*=[^=]", File.ReadAllText(f));
            }
        }
    }
}
