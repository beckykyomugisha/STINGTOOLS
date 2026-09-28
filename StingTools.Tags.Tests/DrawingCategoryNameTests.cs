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
    /// Every category name the shipped catalogue uses must resolve.
    ///
    /// <para><b>The bug this was written for.</b> The new dimension and MEP
    /// annotation engines first shipped with a resolver that only did
    /// <c>Enum.TryParse&lt;BuiltInCategory&gt;</c>. But the catalogue writes
    /// LOCALISED DISPLAY names — "Doors", "Windows", "Structural Columns",
    /// "Walls", "Pipes", "Ducts" — not <c>OST_</c> strings. So every shipped
    /// rule resolved to null, and the engines would have collected nothing
    /// while reporting "category is not a built-in category": a brand-new
    /// instance of the exact failure the phase existed to remove, caught only
    /// by reading the data rather than assuming its shape.</para>
    ///
    /// <para>Both spellings now route through RevitCategoryTree, which is
    /// Revit-free — so this gate can assert the resolution the engines depend
    /// on without a Revit host.</para>
    /// </summary>
    public class DrawingCategoryNameTests
    {
        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_DRAWING_TYPES.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        /// <summary>Mirrors ElementDimensioner.ResolveBic: OST_ name, else display name.</summary>
        private static bool Resolves(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            var k = key.Trim();
            if (k == "*") return true;
            if (k.StartsWith("OST_", StringComparison.OrdinalIgnoreCase))
                return RevitCategoryTree.FindByBic(k) != null;
            return RevitCategoryTree.FindByDisplayName(k) != null
                || RevitCategoryTree.FindByBic(k) != null;
        }

        [Fact]
        public void Every_annotation_rule_category_resolves_to_a_real_category()
        {
            var doc = JObject.Parse(File.ReadAllText(Path.Combine(DataDir(), "STING_DRAWING_TYPES.json")));
            var offenders = new List<string>();
            foreach (var t in doc["drawingTypes"])
            {
                var rules = t["annotation"]?["rules"];
                if (rules == null) continue;
                foreach (var r in rules)
                {
                    var rt  = r["ruleType"]?.ToString();
                    var cat = r["category"]?.ToString();

                    // A kind that forces its own category does not depend on the
                    // row's value, so a stale row there cannot break anything.
                    var forced = AnnotationRuleKinds.Resolve(rt)?.ForcedCategory;
                    if (!string.IsNullOrWhiteSpace(forced)) continue;

                    if (!Resolves(cat))
                        offenders.Add($"{t["id"]}: {rt} → category '{cat}'");
                }
            }
            Assert.True(offenders.Count == 0,
                "These rules name a category no resolver can map, so the engine collects nothing:\n  "
                + string.Join("\n  ", offenders.Distinct()));
        }

        [Fact]
        public void Every_pack_vgOverride_key_resolves_to_a_real_category()
        {
            // A vgOverrides key that resolves to nothing produces
            // "Category 'X' not found" at apply time and the override is lost.
            var packs = JObject.Parse(File.ReadAllText(Path.Combine(DataDir(), "STING_VIEW_STYLE_PACKS.json")));
            var offenders = new List<string>();
            foreach (var p in packs["stylePacks"])
            {
                var vg = p["vgOverrides"] as JObject;
                if (vg == null) continue;
                foreach (var kv in vg)
                {
                    // STING_* keys are STING's own annotation families, not
                    // Revit categories; they resolve at runtime by family name.
                    if (kv.Key.StartsWith("STING", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!Resolves(kv.Key)) offenders.Add($"{p["id"]}: '{kv.Key}'");
                }
            }
            Assert.True(offenders.Count == 0,
                "Unresolvable vgOverrides keys (the override is silently dropped at apply time):\n  "
                + string.Join("\n  ", offenders.Distinct()));
        }

        [Fact]
        public void Every_tagFamilies_key_resolves_to_a_real_category()
        {
            // ResolveTagTypeId does pack.TagFamilies.TryGetValue(ruleCategory),
            // so a key spelled any other way is DEAD: the declared family is
            // silently replaced by "first loaded tag of that category". Seven
            // PascalCase-without-spaces keys shipped that way — including
            // 'StructuralColumns' → STING_TAG_COL and 'LightingFixtures' →
            // STING_TAG_LIGHT — so those drawings used whatever tag happened to
            // load first and reported success.
            var doc = JObject.Parse(File.ReadAllText(Path.Combine(DataDir(), "STING_DRAWING_TYPES.json")));
            var offenders = new List<string>();
            foreach (var t in doc["drawingTypes"])
            {
                var fam = t["annotation"]?["tagFamilies"] as JObject;
                if (fam == null) continue;
                foreach (var kv in fam)
                    // The material-callout key is not a host category (a material tag tags a
                    // face of any host) — MaterialTag / MaterialTagLayers read it directly.
                    if (!Resolves(kv.Key)
                        && !string.Equals(kv.Key, StingTools.Core.Drawing.AnnotationRuleKinds.MaterialTagFamilyKey, StringComparison.Ordinal))
                        offenders.Add($"{t["id"]}: '{kv.Key}' → '{kv.Value}'");
            }
            Assert.True(offenders.Count == 0,
                "Unresolvable tagFamilies keys — the declared family is silently ignored:\n  "
                + string.Join("\n  ", offenders.Distinct()));
        }

        [Fact]
        public void No_tagFamilies_key_uses_the_PascalCase_no_space_spelling()
        {
            // The specific mistake, named: a key that is a display name with the
            // spaces removed. Caught separately from the resolve sweep because
            // "StructuralColumns" is the shape a contributor naturally types,
            // and it fails silently rather than loudly.
            var doc = JObject.Parse(File.ReadAllText(Path.Combine(DataDir(), "STING_DRAWING_TYPES.json")));
            var squashed = RevitCategoryTree.All
                .Where(c => c.DisplayName != null && c.DisplayName.Contains(' '))
                .ToDictionary(c => c.DisplayName.Replace(" ", ""), c => c.DisplayName,
                              StringComparer.OrdinalIgnoreCase);

            var offenders = new List<string>();
            foreach (var t in doc["drawingTypes"])
            {
                var fam = t["annotation"]?["tagFamilies"] as JObject;
                if (fam == null) continue;
                foreach (var kv in fam)
                    if (squashed.TryGetValue(kv.Key, out var proper) && kv.Key != proper)
                        offenders.Add($"{t["id"]}: '{kv.Key}' should be '{proper}'");
            }
            Assert.True(offenders.Count == 0,
                "tagFamilies keys missing their spaces:\n  " + string.Join("\n  ", offenders.Distinct()));
        }

        [Fact]
        public void Forced_categories_are_display_names_not_BIC_strings()
        {
            // AnnotationRuleKinds.ForcedCategory feeds BOTH ResolveCategoryId
            // (which accepts either spelling) and the tagFamilies lookup (which
            // accepts only the display name). A BIC there makes the family
            // lookup miss — which is exactly the regression this test was
            // written after causing: "OST_Rooms" broke the 12 profiles keying
            // their room tag under "Rooms".
            foreach (var kind in AnnotationRuleKinds.All)
            {
                if (string.IsNullOrWhiteSpace(kind.ForcedCategory)) continue;
                Assert.False(kind.ForcedCategory.StartsWith("OST_", StringComparison.OrdinalIgnoreCase),
                    $"{kind.Name}.ForcedCategory is '{kind.ForcedCategory}' — must be the display name, "
                    + "or the tagFamilies lookup silently misses.");
                Assert.NotNull(RevitCategoryTree.FindByDisplayName(kind.ForcedCategory));
            }
        }

        [Theory]
        [InlineData("Doors")]
        [InlineData("Windows")]
        [InlineData("Walls")]
        [InlineData("Structural Columns")]
        [InlineData("Pipes")]
        [InlineData("Ducts")]
        [InlineData("Roofs")]
        public void The_display_names_the_catalogue_actually_uses_all_resolve(string displayName)
        {
            // Named explicitly as well as swept above, because these are the
            // six the previously-dead rule types depend on: if any one of them
            // stops resolving, a dimension engine goes quiet again.
            var hit = RevitCategoryTree.FindByDisplayName(displayName);
            Assert.NotNull(hit);
            Assert.False(string.IsNullOrWhiteSpace(hit.Bic),
                $"'{displayName}' resolves but carries no BIC, so Enum.TryParse still fails.");
            Assert.StartsWith("OST_", hit.Bic);
        }
    }
}
