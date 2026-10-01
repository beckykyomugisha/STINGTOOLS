using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// TAGACC-25: every <c>DisciplineProfile</c> setting is either applied somewhere in the
    /// plugin or named by <c>IgnoredSettings()</c> — so a project that sets one is told, and
    /// a setting that gets implemented must come off the list.
    /// </summary>
    public class DisciplineProfileIgnoredSettingsTests
    {
        [Fact]
        public void Every_profile_setting_is_applied_or_reported_as_ignored()
        {
            string root = Path.Combine(DrawingCatalogueFixture.RepoRoot(), "StingTools");
            string models = File.ReadAllText(Path.Combine(root, "Core", "TaggingModels.cs"));

            int cls = models.IndexOf("public class DisciplineProfile");
            Assert.True(cls >= 0, "DisciplineProfile not found");
            int clsEnd = models.IndexOf("\n    }", cls);
            string body = models.Substring(cls, clsEnd - cls);

            var props = Regex.Matches(body, @"public\s+[\w<>\?,\s]+?\s+(\w+)\s*\{\s*get;")
                .Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Assert.True(props.Count >= 10, $"Only {props.Count} DisciplineProfile properties found — parser broken?");

            int ign = body.IndexOf("public List<string> IgnoredSettings()");
            Assert.True(ign > 0, "IgnoredSettings() not found");
            string ignBody = body.Substring(ign, body.IndexOf("\n        }", ign) - ign);
            var listed = Regex.Matches(ignBody, @"nameof\((\w+)\)").Cast<Match>().Select(m => m.Groups[1].Value).ToHashSet();

            // Readers: any plugin source except the model itself and the report that only displays
            // it; TagConfig.<same name> is a global setting, not the profile's.
            var sep = Path.DirectorySeparatorChar;
            string others = string.Join("\n", Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(sep + "obj" + sep) && !f.Contains(sep + "bin" + sep)
                            && !f.EndsWith("TaggingModels.cs") && !f.EndsWith("StingCommandHandler.cs"))
                .Select(File.ReadAllText));

            foreach (string p in props)
            {
                bool applied = Regex.IsMatch(others, @"(?<!TagConfig)\??\." + p + @"\b");
                if (applied)
                    Assert.False(listed.Contains(p), $"{p} is applied now — remove it from IgnoredSettings()");
                else
                    Assert.True(listed.Contains(p), $"{p} is read by nothing — list it in IgnoredSettings() so projects are told");
            }
        }
    }
}
