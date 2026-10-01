using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// TAGACC-25: every public <c>DisciplineProfile</c> setting is read somewhere in the plugin
    /// outside the model itself. A setting that loads and is read by nothing is the failure this
    /// item was opened for — a project sets it and gets no effect, in silence. The retired ones
    /// (SeqScheme, SeqPadWidth, SeqIncludeZone, DefaultZone, DefaultLoc) were removed from the
    /// model rather than listed; a key a project still carries is classified and warned about by
    /// <c>DisciplineProfileKeys</c> instead.
    /// </summary>
    public class DisciplineProfileSettingsReadTests
    {
        [Fact]
        public void Every_profile_setting_is_read_outside_the_model()
        {
            string root = Path.Combine(DrawingCatalogueFixture.RepoRoot(), "StingTools");
            string models = File.ReadAllText(Path.Combine(root, "Core", "TaggingModels.cs"));

            int cls = models.IndexOf("public class DisciplineProfile");
            Assert.True(cls >= 0, "DisciplineProfile not found");
            int clsEnd = models.IndexOf("\n    }", cls);
            string body = models.Substring(cls, clsEnd - cls);

            var props = Regex.Matches(body, @"public\s+[\w<>\?,\s]+?\s+(\w+)\s*\{\s*get;")
                .Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Assert.True(props.Count >= 9, $"Only {props.Count} DisciplineProfile properties found — parser broken?");

            // Readers: any plugin source except the model itself and the report that only
            // displays it; TagConfig.<same name> is a global setting, not the profile's.
            var sep = Path.DirectorySeparatorChar;
            string others = string.Join("\n", Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(sep + "obj" + sep) && !f.Contains(sep + "bin" + sep)
                            && !f.EndsWith("TaggingModels.cs") && !f.EndsWith("StingCommandHandler.cs"))
                .Select(File.ReadAllText));

            var unread = props.Where(p => !Regex.IsMatch(others, @"(?<!TagConfig)\??\." + p + @"\b")).ToList();
            Assert.True(unread.Count == 0,
                $"{unread.Count} DisciplineProfile setting(s) are read by nothing: {string.Join(", ", unread)}. " +
                "Apply them, or retire them through DisciplineProfileKeys.Retired so a project that sets one is warned.");
        }
    }
}
