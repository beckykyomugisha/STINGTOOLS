// A-2 — every JSON field on the annotation rule POCOs must have an ENGINE reader.
//
// At 9775cb213 AnnotationRulePack / AutoAnnotationRule / SpotAnnotationRule
// declared fields that the Drawing Type editor and the production-config
// dialog let a user set and that no engine ever read: tagDepths, depth,
// tag7Depth, addTickMarks, batchScope, slopeArrow, the tag rule's
// leaderStyle, minSizeMm on tag rules, and the legacy autoTag*/autoDim*
// flags (MigrateFromLegacy had no caller). A field like that is a setting
// that silently does nothing.
//
// The check: for each [JsonProperty] on the three POCOs, some file under
// StingTools/Core (the engines — UI editors are writers, not readers) other
// than the POCO file itself must mention ".PropertyName". It is a text scan,
// so a common name (Category, Enabled) can pass on an unrelated reader; it
// cannot fail on a live field, and it does fail on a uniquely-named dead one,
// which is every case found so far.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class AnnotationRulePackConsumerTests
    {
        /// <summary>Fields read only inside AnnotationRulePack.cs itself, with why that counts.</summary>
        private static readonly Dictionary<string, string> ReadInPocoFile = new Dictionary<string, string>
        {
            ["AnnotationRulePack.AutoDimGrids"]     = "legacy flag: HasLegacyFlags/MigrateFromLegacy, invoked by AnnotationRunner.Apply",
            ["AnnotationRulePack.AutoDimLevels"]    = "legacy flag: as above",
            ["AnnotationRulePack.AutoTagRooms"]     = "legacy flag: as above",
            ["AnnotationRulePack.AutoTagDoors"]     = "legacy flag: as above",
            ["AnnotationRulePack.AutoTagWindows"]   = "legacy flag: as above",
            ["AnnotationRulePack.AutoTagEquipment"] = "legacy flag: as above",
            ["AnnotationRulePack.AutoTagWelds"]     = "legacy flag: as above",
            ["AnnotationRulePack.AutoTagSupports"]  = "legacy flag: as above",
            ["AnnotationRulePack.AutoTagBends"]     = "legacy flag: as above",
        };

        private static string CoreDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Core", "Drawing")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Core");
            return Path.Combine(dir.FullName, "StingTools", "Core");
        }

        private static string EngineSource()
        {
            var files = Directory.GetFiles(CoreDir(), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .Where(f => !f.EndsWith("AnnotationRulePack.cs", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.True(files.Count > 200, $"only {files.Count} engine files found");
            return string.Join("\n", files.Select(File.ReadAllText));
        }

        private static IEnumerable<PropertyInfo> JsonProps(Type t)
            => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<JsonPropertyAttribute>() != null);

        [Fact]
        public void EveryAnnotationJsonFieldHasAnEngineReader()
        {
            var src = EngineSource();
            var types = new[] { typeof(AnnotationRulePack), typeof(AutoAnnotationRule), typeof(SpotAnnotationRule) };
            int checkedCount = 0;
            var dead = new List<string>();
            foreach (var t in types)
                foreach (var p in JsonProps(t))
                {
                    checkedCount++;
                    string key = t.Name + "." + p.Name;
                    if (ReadInPocoFile.ContainsKey(key)) continue;
                    if (!Regex.IsMatch(src, @"\." + Regex.Escape(p.Name) + @"\b"))
                        dead.Add($"{key} (json \"{p.GetCustomAttribute<JsonPropertyAttribute>().PropertyName}\")");
                }
            Assert.True(checkedCount > 30, $"only {checkedCount} JSON fields reflected");
            Assert.True(dead.Count == 0,
                "JSON fields no engine under StingTools/Core reads - wire a reader, or remove the field:\n  " +
                string.Join("\n  ", dead));
        }

        [Fact]
        public void TheLegacyFlagAllowanceHoldsOnlyWhileTheRunnerMigrates()
        {
            // The allowlist above is honest only while AnnotationRunner calls
            // MigrateFromLegacy. Remove that call and this fails, not silence.
            var runner = File.ReadAllText(Path.Combine(CoreDir(), "Drawing", "AnnotationRunner.cs"));
            Assert.Matches(@"pack\.MigrateFromLegacy\(\)", runner);
            foreach (var key in ReadInPocoFile.Keys)
            {
                var name = key.Split('.')[1];
                Assert.True(typeof(AnnotationRulePack).GetProperty(name) != null, "stale allowlist entry " + key);
            }
        }

        [Fact]
        public void ShippedCatalogueUsesNoRemovedAnnotationKey()
        {
            var dir = new DirectoryInfo(CoreDir()).Parent;
            var json = File.ReadAllText(Path.Combine(dir.FullName, "Data", "STING_DRAWING_TYPES.json"));
            foreach (var k in new[] { "tag7Depth", "addTickMarks", "batchScope", "slopeArrow" })
                Assert.DoesNotContain("\"" + k + "\"", json);
        }
    }
}
