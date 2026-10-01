using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Tags;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>TAGACC-23: Tag Format writes TAG_FORMAT with the names the loader reads.</summary>
    public class TagFormatConfigTests
    {
        [Fact]
        public void Serialises_with_the_loader_key_names()
        {
            var fmt = new TagFormatConfig { Separator = ".", NumPad = 5, SegmentOrder = new[] { "DISC", "SEQ" } };
            var o = JObject.FromObject(fmt);
            Assert.Equal(".", (string)o["separator"]);
            Assert.Equal(5, (int)o["num_pad"]);
            Assert.Equal("SEQ", (string)o["segment_order"][1]);
            Assert.Null(o.Property("NumPad", System.StringComparison.Ordinal));
            Assert.Null(o.Property("Separator", System.StringComparison.Ordinal));
        }

        [Fact]
        public void Reads_the_section_SaveToFile_writes()
        {
            // The shape TagConfig.SaveToFile writes (and the wizard saves through it).
            var section = new Dictionary<string, object>
            {
                [TagFormatConfig.SeparatorKey] = "_",
                [TagFormatConfig.NumPadKey] = 3,
                [TagFormatConfig.SegmentOrderKey] = new[] { "LOC", "SEQ" },
            };
            var fmt = JsonConvert.DeserializeObject<TagFormatConfig>(JsonConvert.SerializeObject(section));
            Assert.Equal("_", fmt.Separator);
            Assert.Equal(3, fmt.NumPad);
            Assert.Equal(new[] { "LOC", "SEQ" }, fmt.SegmentOrder);
        }

        [Fact]
        public void Old_Tag_Format_sections_are_recognised_as_never_applied()
        {
            Assert.True(TagFormatConfig.IsLegacyUnreadSection(JObject.Parse(@"{ ""NumPad"": 5, ""Separator"": ""."", ""SegmentOrder"": [""DISC""] }")));
            Assert.False(TagFormatConfig.IsLegacyUnreadSection(JObject.Parse(@"{ ""num_pad"": 5, ""separator"": ""."" }")));
            Assert.False(TagFormatConfig.IsLegacyUnreadSection(JObject.Parse(@"{ ""num_pad"": 5, ""NumPad"": 4 }")));
            Assert.False(TagFormatConfig.IsLegacyUnreadSection(new JObject()));
            Assert.False(TagFormatConfig.IsLegacyUnreadSection(null));
        }

        /// <summary>The loader and SaveToFile use the same constants, not their own literals.</summary>
        [Fact]
        public void Loader_and_saver_use_the_shared_keys()
        {
            string src = File.ReadAllText(Path.Combine(DrawingCatalogueFixture.RepoRoot(), "StingTools", "Core", "TagConfig.cs"));
            Assert.Contains("TryGetValue(StingTools.Tags.TagFormatConfig.NumPadKey", src);
            Assert.Contains("[StingTools.Tags.TagFormatConfig.NumPadKey] = NumPad", src);
            Assert.DoesNotContain("TryGetValue(\"num_pad\"", src);
            Assert.DoesNotContain("[\"num_pad\"] = NumPad", src);
        }
    }
}
