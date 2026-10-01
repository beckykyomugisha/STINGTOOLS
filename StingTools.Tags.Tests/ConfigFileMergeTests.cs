using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>TAGACC-19: saving tag settings keeps every key it does not own.</summary>
    public class ConfigFileMergeTests
    {
        // A project_config.json as a real project has it: tag keys plus keys other
        // subsystems read through GetConfigValue.
        private const string Existing = @"{
  ""DISC_MAP"": { ""Doors"": ""A"" },
  ""SEQ_SCHEME"": ""PerLevel"",
  ""SEQ_INCLUDE_LOC"": true,
  ""SEQ_INCLUDE_ZONE"": false,
  ""SEQ_LEVEL_RESET"": true,
  ""CDE_FIRST_LAYOUT"": false,
  ""FOLDER_CODE_SUFFIX"": true,
  ""COST_CONTINGENCY_PCT"": 7.5,
  ""LEADER_CLEARANCE_MARGIN_FT"": 0.75,
  ""permissions"": { ""roles"": [ ""BIM Manager"" ] },
  ""RENUMBER_ON_OVERWRITE"": false
}";

        private static Dictionary<string, object> Owned() => new Dictionary<string, object>
        {
            ["DISC_MAP"] = new Dictionary<string, string> { ["Doors"] = "A", ["Walls"] = "A" },
            ["RENUMBER_ON_OVERWRITE"] = true,
            ["TAG_PREFIX"] = "",
        };

        [Fact]
        public void Keys_the_caller_does_not_own_survive_with_their_values()
        {
            var o = JObject.Parse(ConfigFileMerge.Merge(Existing, Owned(), out int preserved));

            Assert.Equal("PerLevel", (string)o["SEQ_SCHEME"]);
            Assert.True((bool)o["SEQ_INCLUDE_LOC"]);
            Assert.False((bool)o["SEQ_INCLUDE_ZONE"]);
            Assert.True((bool)o["SEQ_LEVEL_RESET"]);
            Assert.False((bool)o["CDE_FIRST_LAYOUT"]);
            Assert.True((bool)o["FOLDER_CODE_SUFFIX"]);
            Assert.Equal(7.5, (double)o["COST_CONTINGENCY_PCT"]);
            Assert.Equal(0.75, (double)o["LEADER_CLEARANCE_MARGIN_FT"]);
            Assert.Equal("BIM Manager", (string)o["permissions"]["roles"][0]);
            Assert.Equal(9, preserved);
        }

        [Fact]
        public void Owned_keys_are_overwritten_and_added()
        {
            var o = JObject.Parse(ConfigFileMerge.Merge(Existing, Owned(), out _));
            Assert.True((bool)o["RENUMBER_ON_OVERWRITE"]);
            Assert.Equal("A", (string)o["DISC_MAP"]["Walls"]);
            Assert.Equal("", (string)o["TAG_PREFIX"]);
        }

        [Fact]
        public void Existing_key_order_is_kept()
        {
            var o = JObject.Parse(ConfigFileMerge.Merge(Existing, Owned(), out _));
            var names = o.Properties().Select(p => p.Name).ToList();
            Assert.Equal("DISC_MAP", names[0]);
            Assert.Equal("SEQ_SCHEME", names[1]);
            Assert.Equal("TAG_PREFIX", names.Last()); // new keys go at the end
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void A_new_file_holds_only_the_owned_keys(string existing)
        {
            var o = JObject.Parse(ConfigFileMerge.Merge(existing, Owned(), out int preserved));
            Assert.Equal(3, o.Count);
            Assert.Equal(0, preserved);
        }

        [Theory]
        [InlineData("{ not json")]
        [InlineData("[1, 2]")]
        public void A_file_it_cannot_read_is_refused_not_replaced(string existing)
            => Assert.ThrowsAny<JsonException>(() => ConfigFileMerge.Merge(existing, Owned(), out _));

        /// <summary>Both whole-file writers go through the merge, and Save Config to
        /// Project no longer carries its own key list.</summary>
        [Fact]
        public void Writers_use_the_merge()
        {
            string root = DrawingCatalogueFixture.RepoRoot();
            string tagConfig = File.ReadAllText(Path.Combine(root, "StingTools", "Core", "TagConfig.cs"));
            string editor = File.ReadAllText(Path.Combine(root, "StingTools", "Tags", "ConfigEditorCommand.cs"));

            int save = tagConfig.IndexOf("public static bool SaveToFile(string path)");
            Assert.True(save > 0, "TagConfig.SaveToFile not found");
            int end = tagConfig.IndexOf("public static void SetConfigValue", save);
            string body = tagConfig.Substring(save, end - save);
            Assert.Contains("ConfigFileMerge.Merge(", body);
            Assert.DoesNotContain("File.WriteAllText(path,", body);

            Assert.Contains("TagConfig.SaveToFile(path)", editor);
            Assert.DoesNotContain("{ \"DISC_MAP\", TagConfig.DiscMap }", editor);
        }
    }
}
