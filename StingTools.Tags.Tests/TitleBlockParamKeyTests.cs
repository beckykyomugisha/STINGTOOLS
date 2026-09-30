using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-60 — every titleBlockParams key of every shipped drawing type must
    /// name a parameter the resolved title-block family carries (directly, or
    /// through the paramAliases map). The keys were display labels ("Client
    /// Name", "Revision") that no STING family has, so every sheet logged
    /// ~10 "no parameter" warnings and TitleBlockParamApplier wrote nothing.
    /// </summary>
    public class TitleBlockParamKeyTests
    {
        private static string TitleBlocksJson() => File.ReadAllText(Path.Combine(
            DrawingCatalogueFixture.RepoRoot(), "StingTools", "Data", "STING_TITLE_BLOCKS.json"));

        private static readonly Lazy<JObject> Library = new Lazy<JObject>(() => JObject.Parse(TitleBlocksJson()));

        private static Dictionary<string, JObject> Families() =>
            ((JArray)Library.Value["families"]).Cast<JObject>()
                .ToDictionary(f => f.Value<string>("id"), f => f, StringComparer.OrdinalIgnoreCase);

        /// <summary>Parameter names of a family, walking its extends chain.</summary>
        private static HashSet<string> ParamsOf(string familyId)
        {
            var fams = Families();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var id = familyId;
            while (!string.IsNullOrEmpty(id) && seen.Add(id) && fams.TryGetValue(id, out var f))
            {
                foreach (var p in f["parameters"] ?? new JArray())
                    set.Add(p.Value<string>("name"));
                id = f.Value<string>("extends");
            }
            return set;
        }

        private static HashSet<string> Concrete() => new HashSet<string>(
            Families().Values.Where(f => !(f.Value<bool?>("abstract") ?? false)).Select(f => f.Value<string>("id")),
            StringComparer.OrdinalIgnoreCase);

        [Fact]
        public void Every_shipped_title_block_key_is_a_parameter_of_the_resolved_family()
        {
            var aliases = TitleBlockParamAliases.FromLibraryJson(TitleBlocksJson());
            var concrete = Concrete();
            var failures = new List<string>();
            int checkedKeys = 0;
            foreach (var dt in DrawingCatalogueFixture.Shipped().DrawingTypes)
            {
                if (dt.TitleBlockParams == null || dt.TitleBlockParams.Count == 0) continue;
                var res = TitleBlockFamilyNaming.Resolve(dt.TitleBlockFamily, dt.PaperSize, dt.Orientation, "BIM", concrete);
                if (!res.IsResolved || !concrete.Contains(res.Family))
                {
                    failures.Add($"{dt.Id}: title block '{dt.TitleBlockFamily}' does not resolve to a catalogue family");
                    continue;
                }
                var have = ParamsOf(res.Family);
                foreach (var key in dt.TitleBlockParams.Keys)
                {
                    checkedKeys++;
                    if (!TitleBlockParamAliases.Candidates(key, aliases).Any(have.Contains))
                        failures.Add($"{dt.Id}: '{key}' is not a parameter of {res.Family}");
                }
            }
            Assert.True(checkedKeys > 500, $"Only {checkedKeys} keys checked — catalogue binding broken?");
            Assert.True(failures.Count == 0, failures.Count + " key(s) no family carries:\n  " + string.Join("\n  ", failures));
        }

        [Fact]
        public void Shipped_types_use_real_parameter_names_not_legacy_labels()
        {
            // Aliases exist for project overrides; the corporate catalogue itself
            // names the parameter it writes, so what it says is what it does.
            var aliases = TitleBlockParamAliases.FromLibraryJson(TitleBlocksJson());
            var labels = DrawingCatalogueFixture.Shipped().DrawingTypes
                .Where(dt => dt.TitleBlockParams != null)
                .SelectMany(dt => dt.TitleBlockParams.Keys.Where(aliases.ContainsKey).Select(k => $"{dt.Id}: '{k}'"))
                .ToList();
            Assert.True(labels.Count == 0, "Legacy label keys in the shipped catalogue:\n  " + string.Join("\n  ", labels));
        }

        [Fact]
        public void Every_alias_targets_a_parameter_some_family_carries()
        {
            var aliases = TitleBlockParamAliases.FromLibraryJson(TitleBlocksJson());
            Assert.True(aliases.Count >= 10, $"Only {aliases.Count} aliases read — paramAliases missing?");
            var all = new HashSet<string>(Concrete().SelectMany(ParamsOf), StringComparer.OrdinalIgnoreCase);
            var dangling = aliases.Where(kv => !all.Contains(kv.Value)).Select(kv => $"'{kv.Key}' -> '{kv.Value}'").ToList();
            Assert.True(dangling.Count == 0, "Aliases to no family parameter:\n  " + string.Join("\n  ", dangling));
        }

        [Fact]
        public void Legacy_label_resolves_after_the_key_as_written()
        {
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["Client Name"] = "PRJ_TB_CLIENT_NAME_TXT" };
            Assert.Equal(new[] { "Client Name", "PRJ_TB_CLIENT_NAME_TXT" },
                         TitleBlockParamAliases.Candidates("Client Name", aliases).ToArray());
            Assert.Equal(new[] { "PRJ_TB_CLIENT_NAME_TXT" },
                         TitleBlockParamAliases.Candidates("PRJ_TB_CLIENT_NAME_TXT", aliases).ToArray());
        }

        [Fact]
        public void A_label_is_shadowed_only_when_its_target_is_declared_too()
        {
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["Revision"] = "PRJ_TB_REVISION_NR_TXT" };
            Assert.True(TitleBlockParamAliases.IsShadowed("Revision", new[] { "Revision", "PRJ_TB_REVISION_NR_TXT" }, aliases));
            Assert.False(TitleBlockParamAliases.IsShadowed("Revision", new[] { "Revision" }, aliases));
            Assert.False(TitleBlockParamAliases.IsShadowed("PRJ_TB_REVISION_NR_TXT", new[] { "Revision", "PRJ_TB_REVISION_NR_TXT" }, aliases));
        }

        [Fact]
        public void Comment_keys_and_blank_targets_are_not_aliases()
        {
            var map = TitleBlockParamAliases.FromLibraryJson(
                "{\"paramAliases\":{\"_comment\":\"x\",\"A\":\"\",\"B\":\"PRJ_B_TXT\"}}");
            Assert.Single(map);
            Assert.Equal("PRJ_B_TXT", map["b"]);
            Assert.Empty(TitleBlockParamAliases.FromLibraryJson("{}"));
        }
    }
}
