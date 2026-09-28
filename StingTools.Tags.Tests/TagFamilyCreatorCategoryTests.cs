// The tag families are described TWICE: by the creator tables in
// TagFamilyCreatorCommand.cs, which decide the category a family is BORN in,
// and by STING_TAG_CONFIG_v5_0_*.csv, which declares the category a family
// SHOULD have. Nothing checked that the two agreed.
//
// They matter differently. The config declaration drives Fix Categories, which
// CORRECTS an existing family - a resolution step that failed four separate
// ways on 2026-09-21 (two unread CSV dialects, a name/file-name mismatch, a
// plural bug, prose where a category name belonged). The creator table drives
// NewFamilyDocument, where the family is born in the right category and there
// is no resolution to get wrong.
//
// So the creator is the sturdier of the two - and if they ever disagree, a
// freshly generated library and a freshly corrected one end up different, with
// nothing saying which is right. Measured 2026-09-21: 73 comparable entries,
// zero disagreements. This keeps it that way.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using StingTools.Tags;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class TagFamilyCreatorCategoryTests
    {
        private static DirectoryInfo RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            return dir;
        }

        /// <summary>(BuiltInCategory, template, display, familyNameSuffix) from the variant tables.</summary>
        private static List<(string Ost, string Suffix)> CreatorVariants(string srcRoot)
        {
            string file = Path.Combine(srcRoot, "Tags", "TagFamilyCreatorCommand.cs");
            Assert.True(File.Exists(file), "TagFamilyCreatorCommand.cs not found");

            var rx = new Regex(
                "\\(BuiltInCategory\\.(\\w+)\\s*,\\s*\"[^\"]+\"\\s*,\\s*\"[^\"]+\"\\s*,\\s*\"([^\"]+)\"\\s*\\)",
                RegexOptions.Compiled);

            return rx.Matches(File.ReadAllText(file))
                     .Select(m => (m.Groups[1].Value, m.Groups[2].Value))
                     .ToList();
        }

        private static Dictionary<string, string> DeclaredCategories(string dataDir)
        {
            var decl = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in Directory.GetFiles(dataDir, "STING_TAG_CONFIG_v5_0_*.csv"))
                foreach (var d in TagConfigDeclarations.Parse(File.ReadLines(f)))
                {
                    string k = TagCategoryNameForms.NormaliseKey(d.FamilyName);
                    if (!decl.ContainsKey(k)) decl[k] = d.HostCategory;
                }
            return decl;
        }

        [Fact]
        public void TheCreatorTablesAgreeWithTheDeclaredCategories()
        {
            var root = RepoRoot();
            Assert.True(root != null, "repo root not found");

            string data = Path.Combine(root.FullName, "StingTools", "Data");
            string src = Path.Combine(root.FullName, "StingTools");

            var variants = CreatorVariants(src);
            Assert.True(variants.Count > 50, $"expected the creator variant tables, found {variants.Count}");

            // Compare by CATEGORY ID, never by display name. Several names alias
            // one category - "MEP Ancillary" and "Mechanical Equipment" are both
            // OST_MechanicalEquipment - so a name comparison reports five clashes
            // that are the same category twice. (It did, on the first run of this
            // test, and the test was wrong, not the data.)
            var registry = JObject.Parse(File.ReadAllText(Path.Combine(data, "PARAMETER_REGISTRY.json")));
            var nameToOst = ((JObject)registry["category_enum_map"])
                .Properties()
                .ToDictionary(p => p.Name, p => (string)p.Value, StringComparer.Ordinal);

            var declared = DeclaredCategories(data);

            var clashes = new List<string>();
            int compared = 0;

            foreach (var (ost, suffix) in variants)
            {
                string key = TagCategoryNameForms.NormaliseKey("STING - " + suffix + " Tag");

                string cfg;
                if (!declared.TryGetValue(key, out cfg)) continue;          // no declaration to compare
                // Multi-Category is NOT skipped. The binding audit skips it because
                // "its own category" has no single answer there - but here the
                // question is exact: if the config says Multi-Category the creator
                // must build it from Multi-Category Tag.rft, or the family is born
                // in one host category and can never be corrected into the other
                // (Revit refuses that reassignment - measured 2026-09-21).
                //
                // Skipping it left a real hole: three creator entries were switched
                // to Multi-Category in the morning and six more were declared so in
                // the evening, and this gate excused all six.
                if (cfg.StartsWith("Multi", StringComparison.OrdinalIgnoreCase))
                {
                    compared++;
                    if (!string.Equals(ost, "OST_MultiCategoryTags", StringComparison.Ordinal))
                        clashes.Add($"{suffix}: config declares Multi-Category but the creator " +
                                    $"builds it as {ost} - Revit cannot convert it afterwards");
                    continue;
                }

                string cfgOst;
                if (!nameToOst.TryGetValue(cfg, out cfgOst)) continue;      // not a model category in the map

                compared++;
                if (!string.Equals(ost, cfgOst, StringComparison.Ordinal))
                    clashes.Add($"{suffix}: creator builds it as {ost}, the config declares " +
                                $"'{cfg}' ({cfgOst})");
            }

            Assert.True(compared > 50, $"only {compared} entries were comparable - the parse is probably broken");
            Assert.True(clashes.Count == 0,
                        $"{clashes.Count} family/families would be BORN in one category and CORRECTED to " +
                        "another:\n  " + string.Join("\n  ", clashes));
        }

        [Fact]
        public void EveryCreatorVariantTemplateIsAnRftFile()
        {
            // A template name that is not a .rft fails NewFamilyDocument, and the
            // family is simply never created - one FAIL line in a report.
            //
            // Matched on the FOUR-tuple form only. A looser pattern also catches
            // the category-to-display-name table, which holds names not templates,
            // and reports 150 false positives - it did on the first run here.
            var root = RepoRoot();
            string file = Path.Combine(root.FullName, "StingTools", "Tags", "TagFamilyCreatorCommand.cs");

            var rx = new Regex(
                "\\(BuiltInCategory\\.\\w+\\s*,\\s*\"([^\"]+)\"\\s*,\\s*\"[^\"]+\"\\s*,\\s*\"[^\"]+\"\\s*\\)",
                RegexOptions.Compiled);

            var templates = rx.Matches(File.ReadAllText(file))
                              .Select(m => m.Groups[1].Value)
                              .Distinct(StringComparer.Ordinal)
                              .ToList();

            Assert.True(templates.Count > 5, $"expected variant templates, found {templates.Count}");

            var bad = templates.Where(t => !t.EndsWith(".rft", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.True(bad.Count == 0, "variant templates that are not .rft: " + string.Join(", ", bad));
        }
    }
}
