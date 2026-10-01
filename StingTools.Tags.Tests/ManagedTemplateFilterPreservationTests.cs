using System;
using System.IO;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-226 — DTW-217 colours MEP systems by adding system filters to the managed
    /// STING:* template. A pack re-sync (checksum drift) re-applies the pack to that same
    /// template. These guards hold the re-sync additive for filters, so the system filters
    /// and their colours survive it: no filter is removed, no template is deleted and
    /// re-minted, and the template path never disables filters it did not add.
    /// </summary>
    public class ManagedTemplateFilterPreservationTests
    {
        private static string Src(string file)
            => File.ReadAllText(Path.Combine(RepoRoot(), "StingTools", "Core", "Drawing", file));

        private static string Method(string src, string signature)
        {
            int start = src.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, $"'{signature}' not found");
            // Up to the next member at class indentation.
            int end = src.IndexOf("\n        private static ", start + signature.Length, StringComparison.Ordinal);
            int end2 = src.IndexOf("\n        internal static ", start + signature.Length, StringComparison.Ordinal);
            int end3 = src.IndexOf("\n        public static ", start + signature.Length, StringComparison.Ordinal);
            int stop = src.Length;
            foreach (var e in new[] { end, end2, end3 }) if (e > 0 && e < stop) stop = e;
            return src.Substring(start, stop - start);
        }

        [Fact]
        public void TheSyncerNeverRemovesAFilter()
            => Assert.DoesNotContain("RemoveFilter(", Src("ManagedTemplateSyncer.cs"));

        [Fact]
        public void TheSyncerNeverDisablesFiltersOnATemplate()
        {
            var src = Src("ManagedTemplateSyncer.cs");
            Assert.DoesNotContain("ApplyFilterEnabled(", src);
            Assert.DoesNotContain("SetIsFilterEnabled(", src);
            Assert.DoesNotContain("SetFilterVisibility(", src);
        }

        [Fact]
        public void TheSyncersFilterPathOnlyAddsTheFiltersThePackNames()
        {
            var applier = Src("ViewStylePackApplier.cs");
            foreach (var sig in new[]
            {
                "internal static void ApplyFilterRules(",
                "private static void ApplyMaterialClassOverrides(",
                "public static void ApplyFilterRulesOnly(Document doc, View view, ViewStylePack pack, PackApplyResult r)",
            })
            {
                var body = Method(applier, sig);
                Assert.DoesNotContain("RemoveFilter(", body);
                Assert.DoesNotContain("SetIsFilterEnabled(", body);
                // Every filter it touches is one it resolved from the pack, never a sweep
                // over what the template already carries.
                Assert.DoesNotContain("foreach (var fid in view.GetFilters())", body);
            }
        }

        [Fact]
        public void ADriftedTemplateIsUpdatedInPlaceNotReminted()
        {
            var src = Src("ManagedTemplateSyncer.cs");
            // The only delete is TryDelete, and it is only called on newId — a template
            // minted moments ago in the same call, never an existing one.
            int calls = 0, idx = 0;
            while ((idx = src.IndexOf("TryDelete(doc, ", idx, StringComparison.Ordinal)) >= 0)
            {
                Assert.StartsWith("TryDelete(doc, newId)", src.Substring(idx));
                calls++; idx++;
            }
            Assert.True(calls > 0);
            Assert.Equal(1, Count(src, "doc.Delete("));   // inside TryDelete
        }

        private static int Count(string s, string needle)
        {
            int n = 0, i = 0;
            while ((i = s.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Core", "Drawing", "ManagedTemplateSyncer.cs")))
                dir = dir.Parent;
            Assert.True(dir != null, "repo root");
            return dir.FullName;
        }
    }
}
