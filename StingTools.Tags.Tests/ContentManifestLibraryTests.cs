using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// The content manifest describes the tag library that ships: every .rfa in
    /// Data/TagFamilies has an entry, every entry has its file, and every checksum
    /// matches. Four hand-built families (TAGFAM-3) were committed without entries,
    /// so ContentManifest's drift check never saw them.
    ///
    /// When a family is changed on purpose, re-stamp it:
    ///   python tools/restamp_content_manifest.py --apply StingTools/Data/TagFamilies
    /// </summary>
    public class ContentManifestLibraryTests
    {
        private static string Data => Path.Combine(DrawingCatalogueFixture.RepoRoot(), "StingTools", "Data");

        private static JArray TagFamilies()
        {
            var root = JObject.Parse(File.ReadAllText(Path.Combine(Data, "STING_CONTENT_MANIFEST.json")));
            var arr = root["tagFamilies"] as JArray;
            Assert.NotNull(arr);
            Assert.True(arr.Count >= 200, $"Only {arr.Count} tagFamilies entries — binding broken?");
            return arr;
        }

        [Fact]
        public void Keys_read_here_are_the_ones_the_model_binds()
        {
            string src = File.ReadAllText(Path.Combine(DrawingCatalogueFixture.RepoRoot(),
                "StingTools", "Core", "Content", "ContentManifest.cs"));
            Assert.Contains("[JsonProperty(\"tagFamilies\")]", src);
            Assert.Contains("[JsonProperty(\"familyFile\")]", src);
            Assert.Contains("[JsonProperty(\"checksum\")]", src);
        }

        [Fact]
        public void Every_shipped_tag_family_is_listed_and_every_entry_ships()
        {
            var listed = TagFamilies().Select(e => (string)e["familyFile"]).ToList();
            Assert.Equal(listed.Count, listed.Distinct(StringComparer.OrdinalIgnoreCase).Count());

            var onDisk = Directory.GetFiles(Path.Combine(Data, "TagFamilies"), "*.rfa", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName).ToList();

            var unlisted = onDisk.Except(listed, StringComparer.OrdinalIgnoreCase).ToList();
            var missing = listed.Except(onDisk, StringComparer.OrdinalIgnoreCase).ToList();
            Assert.True(unlisted.Count == 0, "On disk but not in STING_CONTENT_MANIFEST.json: " + string.Join("; ", unlisted));
            Assert.True(missing.Count == 0, "In the manifest but not in Data/TagFamilies: " + string.Join("; ", missing));
        }

        [Fact]
        public void Every_checksum_matches_its_file()
        {
            var lib = Path.Combine(Data, "TagFamilies");
            var drift = TagFamilies()
                .Select(e => (file: (string)e["familyFile"], sum: (string)e["checksum"]))
                .Where(e => File.Exists(Path.Combine(lib, e.file)))
                .Where(e => !string.Equals(e.sum, Sha256(Path.Combine(lib, e.file)), StringComparison.OrdinalIgnoreCase))
                .Select(e => e.file)
                .ToList();
            Assert.True(drift.Count == 0,
                "Checksum differs (re-stamp with tools/restamp_content_manifest.py --apply if the change is intended): "
                + string.Join("; ", drift));
        }

        private static string Sha256(string path)
        {
            using (var s = File.OpenRead(path))
            using (var h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }
    }
}
