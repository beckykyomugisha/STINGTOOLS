using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// TAGFAM-5: <c>TagTypeVariantWriter.CreateStandardVariants</c> sets arrowhead
    /// ElementIds on a family document's <c>LEADER_ARROWHEAD</c>. Ids are per-document,
    /// so the lookup must be built from that family document — never the project
    /// (<c>doc</c>), which is what Migrate and Propagate used to pass.
    /// </summary>
    public class ArrowheadLookupDocumentTests
    {
        [Fact]
        public void Every_variant_writer_call_resolves_arrowheads_in_the_family_document()
        {
            string root = Path.Combine(DrawingCatalogueFixture.RepoRoot(), "StingTools");
            var calls = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                         && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                // Line comments out first: a doc comment may name the call.
                .SelectMany(f => Regex.Matches(Regex.Replace(File.ReadAllText(f), @"//[^\n]*", ""), @"TagTypeVariantWriter\.CreateStandardVariants\s*\(([^;]*);")
                    .Cast<Match>()
                    .Select(m => (file: Path.GetFileName(f), args: m.Groups[1].Value)))
                .Where(c => c.file != "TagTypeVariantWriter.cs") // the definition
                .ToList();

            // Non-vacuous: creator, Migrate, Propagate (clone + master).
            Assert.True(calls.Count >= 4, $"Only {calls.Count} CreateStandardVariants call(s) found");

            foreach (var (file, args) in calls)
            {
                var m = Regex.Match(args, @"BuildArrowheadLookup\s*\(\s*(\w+)\s*\)");
                Assert.True(m.Success, $"{file}: CreateStandardVariants must resolve arrowheads inline in the family document: {args.Trim()}");
                Assert.NotEqual("doc", m.Groups[1].Value);
            }
        }
    }
}
