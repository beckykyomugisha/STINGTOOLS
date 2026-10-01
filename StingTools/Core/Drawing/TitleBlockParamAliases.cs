// StingTools — Drawing Template Manager · DTW-60
//
// Revit-free half of the title-block key lookup. A drawing type's
// titleBlockParams used to be keyed by display labels ("Client Name",
// "Revision", …) that no STING title-block family carries, so every sheet
// logged ~10 "no parameter" warnings and nothing was written. The shipped
// catalogue now uses the real family parameter names; this map keeps the
// old labels working for project overrides written against them.
//
// The map is data: "paramAliases" in STING_TITLE_BLOCKS.json, label →
// family parameter. A key is always tried as written first, so a family
// that really has a parameter called "Client Name" keeps receiving it.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Drawing
{
    public static class TitleBlockParamAliases
    {
        /// <summary>Reads "paramAliases" from the STING_TITLE_BLOCKS.json text.
        /// Empty (never null) when the block is absent or unreadable.</summary>
        public static Dictionary<string, string> FromLibraryJson(string json)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json)) return map;
            var root = JObject.Parse(json);
            if (root["paramAliases"] is JObject block)
                foreach (var prop in block.Properties())
                {
                    if (prop.Name.StartsWith("_", StringComparison.Ordinal)) continue; // comment keys
                    var target = prop.Value?.Type == JTokenType.String ? prop.Value.Value<string>() : null;
                    if (!string.IsNullOrWhiteSpace(prop.Name) && !string.IsNullOrWhiteSpace(target))
                        map[prop.Name.Trim()] = target.Trim();
                }
            return map;
        }

        /// <summary>Names to try on the title block for a declared key: the key
        /// itself, then its alias target when it has one.</summary>
        public static IEnumerable<string> Candidates(string key, IReadOnlyDictionary<string, string> aliases)
        {
            if (string.IsNullOrWhiteSpace(key)) yield break;
            yield return key;
            if (aliases != null && aliases.TryGetValue(key.Trim(), out var target)
                && !string.Equals(target, key, StringComparison.Ordinal))
                yield return target;
        }

        /// <summary>The alias target of <paramref name="key"/>, or null.</summary>
        public static string Target(string key, IReadOnlyDictionary<string, string> aliases)
            => !string.IsNullOrWhiteSpace(key) && aliases != null && aliases.TryGetValue(key.Trim(), out var t) ? t : null;

        /// <summary>
        /// True when <paramref name="key"/> is a legacy label whose target is also
        /// declared under its real name. The real name wins; writing both would
        /// make the result depend on dictionary order.
        /// </summary>
        public static bool IsShadowed(string key, IEnumerable<string> declaredKeys,
                                      IReadOnlyDictionary<string, string> aliases)
        {
            var target = Target(key, aliases);
            if (target == null || string.Equals(target, key, StringComparison.OrdinalIgnoreCase)) return false;
            return declaredKeys != null
                && declaredKeys.Any(k => string.Equals(k?.Trim(), target, StringComparison.OrdinalIgnoreCase));
        }
    }
}
