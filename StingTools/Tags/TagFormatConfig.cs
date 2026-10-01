using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingTools.Tags
{
    /// <summary>
    /// The <c>TAG_FORMAT</c> section of <c>project_config.json</c>: separator, SEQ pad and
    /// segment order.
    ///
    /// TAGACC-23: the loader (<c>TagConfig.LoadFromFile</c>) and <c>TagConfig.SaveToFile</c>
    /// use <c>separator</c> / <c>num_pad</c> / <c>segment_order</c>. The Tag Format command
    /// serialised this class without names, so it wrote <c>Separator</c> / <c>NumPad</c> /
    /// <c>SegmentOrder</c> — which the loader's case-sensitive lookup never reads — and, by
    /// replacing the whole section, erased the format the setup wizard had saved. The names
    /// are pinned here and the loader and saver use the same constants.
    /// </summary>
    public class TagFormatConfig
    {
        public const string SeparatorKey = "separator";
        public const string NumPadKey = "num_pad";
        public const string SegmentOrderKey = "segment_order";

        [JsonProperty(NumPadKey)]
        public int NumPad { get; set; } = 4;

        [JsonProperty(SeparatorKey)]
        public string Separator { get; set; } = "-";

        [JsonProperty(SegmentOrderKey)]
        public string[] SegmentOrder { get; set; } = { "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ" };

        /// <summary>
        /// True when a <c>TAG_FORMAT</c> section holds the names the old Tag Format command
        /// wrote (<c>NumPad</c> / <c>SegmentOrder</c>, exact case) and none of the names the
        /// loader reads: a format somebody saved that never took effect. It is reported, not
        /// applied — applying a long-ignored format would change how tags are built.
        /// </summary>
        public static bool IsLegacyUnreadSection(JObject section)
        {
            if (section == null) return false;
            bool hasCanonical = section.Property(SeparatorKey, System.StringComparison.Ordinal) != null
                             || section.Property(NumPadKey, System.StringComparison.Ordinal) != null
                             || section.Property(SegmentOrderKey, System.StringComparison.Ordinal) != null;
            bool hasLegacy = section.Property("NumPad", System.StringComparison.Ordinal) != null
                          || section.Property("SegmentOrder", System.StringComparison.Ordinal) != null
                          || section.Property("Separator", System.StringComparison.Ordinal) != null;
            return hasLegacy && !hasCanonical;
        }
    }
}
