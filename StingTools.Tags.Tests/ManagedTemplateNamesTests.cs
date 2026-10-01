// DT-R11-F: managed view templates were named "STING:{packId}:{ViewType}". Revit
// refuses ':' in an element name, so the mint threw and no managed template was ever
// created in a real model. ManagedTemplateNames owns the legal name, its parse, and
// recognition of the legacy form; these tests hold it to the shipped data.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class ManagedTemplateNamesTests
    {
        // Every Revit ViewType a managed template can be minted for (names as the enum spells them).
        private static readonly string[] ViewTypes =
        {
            "FloorPlan", "CeilingPlan", "EngineeringPlan", "AreaPlan", "Section", "Elevation",
            "Detail", "DraftingView", "ThreeD", "Schedule", "Legend", "Walkthrough", "Rendering",
        };

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_VIEW_STYLE_PACKS.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            return dir.FullName;
        }

        private static JObject Load(string file)
            => JObject.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "StingTools", "Data", file)));

        private static List<string> ShippedPackIds()
            => Load("STING_VIEW_STYLE_PACKS.json")["stylePacks"].Select(p => (string)p["id"]).ToList();

        /// <summary>Every view template name the shipped data asks for, with where it came from.</summary>
        private static List<(string Where, string Name)> ShippedTemplateNames()
        {
            var list = new List<(string, string)>();
            foreach (var t in Load("STING_DRAWING_TYPES.json")["drawingTypes"])
            {
                string id = (string)t["id"];
                list.Add(($"{id}.viewTemplateName", (string)t["viewTemplateName"]));
                if (t["productionRules"] is JArray rules)
                    foreach (var r in rules) list.Add(($"{id} rule viewTemplateOverride", (string)r["viewTemplateOverride"]));
                if (t["slots"] is JArray slots)
                    foreach (var s in slots) list.Add(($"{id} slot viewTemplate", (string)s["viewTemplate"]));
            }
            foreach (var p in Load("STING_VIEW_STYLE_PACKS.json")["stylePacks"])
                list.Add(($"pack {(string)p["id"]}.viewTemplate", (string)p["viewTemplate"]));
            return list.Where(x => !string.IsNullOrWhiteSpace(x.Item2)).ToList();
        }

        [Fact]
        public void Every_shipped_pack_mints_a_legal_name_that_parses_back()
        {
            var packs = ShippedPackIds();
            Assert.True(packs.Count > 30, $"expected the shipped pack catalogue, found {packs.Count}");
            var bad = new List<string>();
            foreach (var id in packs)
            foreach (var vt in ViewTypes)
            {
                if (!ManagedTemplateNames.IsValidPackId(id)) { bad.Add($"pack id '{id}' not valid"); break; }
                var name = ManagedTemplateNames.Build(id, vt);
                if (!RevitNameRules.IsLegal(name)) bad.Add($"'{name}' illegal in Revit");
                if (!ManagedTemplateNames.TryParse(name, out var p, out var v) || p != id || v != vt)
                    bad.Add($"'{name}' does not round-trip");
            }
            Assert.True(bad.Count == 0, string.Join("\n", bad.Take(20)));
        }

        [Fact]
        public void Every_shipped_view_template_name_is_legal_and_managed_ones_resolve_to_a_shipped_pack()
        {
            var packs = new HashSet<string>(ShippedPackIds(), StringComparer.OrdinalIgnoreCase);
            var bad = new List<string>();
            foreach (var (where, name) in ShippedTemplateNames())
            {
                if (!RevitNameRules.IsLegal(name)) bad.Add($"{where}: '{name}' illegal in Revit");
                if (ManagedTemplateNames.HasManagedPrefix(name))
                {
                    if (!ManagedTemplateNames.TryParse(name, out var p, out var vt))
                        bad.Add($"{where}: '{name}' claims the managed namespace but does not parse");
                    else
                    {
                        if (!packs.Contains(p)) bad.Add($"{where}: '{name}' names pack '{p}', which is not shipped");
                        if (ManagedTemplateNames.Build(p, vt) != name) bad.Add($"{where}: '{name}' is not the canonical form");
                    }
                }
            }
            Assert.True(bad.Count == 0, $"{bad.Count} shipped view template name(s) wrong:\n  " + string.Join("\n  ", bad));
        }

        [Fact]
        public void The_shipped_data_does_name_managed_templates()
        {
            // Witness: the test above must actually see managed names to mean anything.
            Assert.Contains(ShippedTemplateNames(), x => ManagedTemplateNames.IsManagedTemplateName(x.Name));
        }

        [Fact]
        public void Canonical_form_is_the_legal_STING_MANAGED_name()
        {
            Assert.Equal("STING MANAGED - corp-coordination - FloorPlan",
                ManagedTemplateNames.Build("corp-coordination", "FloorPlan"));
            Assert.True(RevitNameRules.IsLegal(ManagedTemplateNames.Build("corp-coordination", "FloorPlan")));
            Assert.False(RevitNameRules.IsLegal(ManagedTemplateNames.BuildLegacy("corp-coordination", "FloorPlan")));
        }

        [Theory]
        [InlineData("STING:corp-coordination:FloorPlan", "corp-coordination", "FloorPlan")]
        [InlineData("STING MANAGED - corp-coordination - FloorPlan", "corp-coordination", "FloorPlan")]
        [InlineData("sting managed - corp-mep - ThreeD", "corp-mep", "ThreeD")]
        [InlineData("STING MANAGED - a.b_c-d - CeilingPlan", "a.b_c-d", "CeilingPlan")]
        public void Both_forms_parse(string name, string pack, string vt)
        {
            Assert.True(ManagedTemplateNames.TryParse(name, out var p, out var v));
            Assert.Equal(pack, p);
            Assert.Equal(vt, v);
            Assert.True(ManagedTemplateNames.IsManagedTemplateName(name));
            Assert.Equal(ManagedTemplateNames.Build(pack, vt), ManagedTemplateNames.Canonical(name));
        }

        [Theory]
        [InlineData("STING - Mechanical Plan")]               // a seed template
        [InlineData("STING - corp-mep - FloorPlan")]          // seed-shaped; must not be read as managed
        [InlineData("STING:my-favourite")]                    // GAP-O: two segments
        [InlineData("STING MANAGED - corp mep - FloorPlan")]  // space in pack id
        [InlineData("STING MANAGED - corp-mep")]
        [InlineData("Architectural Plan")]
        [InlineData("")]
        [InlineData(null)]
        public void Other_names_are_not_managed(string name)
        {
            Assert.False(ManagedTemplateNames.TryParse(name, out _, out _));
            Assert.False(ManagedTemplateNames.IsManagedTemplateName(name));
        }

        [Fact]
        public void The_managed_prefix_cannot_be_taken_for_a_seed_template()
        {
            // ManagedTemplateSyncer.ResolveSeed picks seeds by "STING - ".
            Assert.False(ManagedTemplateNames.Build("corp-mep", "FloorPlan").StartsWith("STING - ", StringComparison.Ordinal));
            Assert.False(ManagedTemplateNames.HasManagedPrefix("STING - Mechanical Plan"));
            Assert.True(ManagedTemplateNames.HasManagedPrefix("STING:my-favourite"));
        }

        [Fact]
        public void A_legacy_name_in_the_data_finds_a_canonical_template_and_vice_versa()
        {
            Assert.Equal(new[] { "STING MANAGED - corp-mep - FloorPlan", "STING:corp-mep:FloorPlan" },
                ManagedTemplateNames.Candidates("STING:corp-mep:FloorPlan").ToArray());
            Assert.True(ManagedTemplateNames.Matches("STING MANAGED - corp-mep - FloorPlan", "STING:corp-mep:FloorPlan"));
            Assert.True(ManagedTemplateNames.Matches("STING:corp-mep:FloorPlan", "STING MANAGED - corp-mep - FloorPlan"));
            Assert.False(ManagedTemplateNames.Matches("STING MANAGED - corp-mep - CeilingPlan", "STING:corp-mep:FloorPlan"));
            Assert.Equal(new[] { "Architectural Plan" }, ManagedTemplateNames.Candidates("Architectural Plan").ToArray());
        }

        [Fact]
        public void BelongsToPack_matches_by_pack_in_either_form()
        {
            Assert.True(ManagedTemplateNames.BelongsToPack("STING MANAGED - corp-mep - Section", "corp-mep", out var vt));
            Assert.Equal("Section", vt);
            Assert.True(ManagedTemplateNames.BelongsToPack("STING:corp-mep:Section", "corp-mep", out _));
            // "corp-mep" must not claim "corp-mep-healthcare"'s templates.
            Assert.False(ManagedTemplateNames.BelongsToPack("STING MANAGED - corp-mep-healthcare - Section", "corp-mep", out _));
        }

        [Theory]
        [InlineData("corp mep", "FloorPlan")]
        [InlineData("corp:mep", "FloorPlan")]
        [InlineData("", "FloorPlan")]
        [InlineData("corp-mep", "Floor Plan")]
        public void Build_refuses_what_would_not_parse_back(string pack, string vt)
            => Assert.Throws<ArgumentException>(() => ManagedTemplateNames.Build(pack, vt));
    }
}
