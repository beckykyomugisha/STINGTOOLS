using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    // ─────────────────────────────────────────────────────────────────────────
    // TAGACC-19 — saving tag settings must not delete everything else.
    //
    // project_config.json is shared: TagConfig owns the tag maps and switches,
    // but the same file carries SEQ_SCHEME / SEQ_INCLUDE_LOC / SEQ_LEVEL_RESET,
    // the folder layout (CDE_FIRST_LAYOUT, FOLDER_CODE_SUFFIX), every COST_* and
    // BOQ_TENDER_* rate, SLA thresholds and more, read through GetConfigValue.
    // Both whole-file writers (TagConfig.SaveToFile and Save Config to Project)
    // built a fresh dictionary and wrote it over the file, so one save silently
    // reset every key they did not list — including the SEQ keys, which changes
    // how the next tags are numbered.
    //
    // Merge: start from the file as it is, overwrite only the keys the caller
    // owns, keep every other key and its position.
    // ─────────────────────────────────────────────────────────────────────────

    internal static class ConfigFileMerge
    {
        /// <summary>
        /// Overlay <paramref name="owned"/> onto <paramref name="existingJson"/>.
        /// Keys the caller does not own are kept unchanged. Blank existing text
        /// means a new file. Text that is not a JSON object throws
        /// <see cref="JsonException"/> — the caller decides what to do with a
        /// file it cannot read; this never discards it quietly.
        /// </summary>
        public static string Merge(string existingJson, IDictionary<string, object> owned, out int preserved)
        {
            preserved = 0;
            JObject root;
            if (string.IsNullOrWhiteSpace(existingJson))
            {
                root = new JObject();
            }
            else
            {
                JToken parsed = JToken.Parse(existingJson);
                root = parsed as JObject
                    ?? throw new JsonSerializationException(
                        $"project config is a JSON {parsed.Type}, not an object");
            }

            var serializer = JsonSerializer.CreateDefault();
            foreach (var kv in owned)
                root[kv.Key] = kv.Value == null ? JValue.CreateNull() : JToken.FromObject(kv.Value, serializer);

            foreach (var prop in root.Properties())
                if (!owned.ContainsKey(prop.Name)) preserved++;

            return root.ToString(Formatting.Indented);
        }
    }
}
