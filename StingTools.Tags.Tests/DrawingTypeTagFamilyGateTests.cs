// Every tag family a shipped drawing type asks for must be one Create Tag Families
// builds. A name that nothing builds is never loaded, so the runner falls back to
// some other tag — often one that does not show the ISO 19650 tag — and the only
// symptom is a warning in a long list. The Drawing Type editor used to offer
// "STING_TAG_ROOM"-style names that were never built.
//
// TagFamilyCreatorCommand.cs is Revit-bound, so its name tables are read as source
// text here (the same tables TagFamilyConfig.AllFamilyNames() walks at runtime).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class DrawingTypeTagFamilyGateTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Tags", "TagFamilyCreatorCommand.cs")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Tags/TagFamilyCreatorCommand.cs");
            return dir.FullName;
        }

        private static string Block(string src, string marker)
        {
            int i = src.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(i >= 0, "missing " + marker);
            int j = src.IndexOf("};", i, StringComparison.Ordinal);
            return src.Substring(i, j - i);
        }

        /// <summary>Everything Create Tag Families builds: its own tables plus the families
        /// declared only in the tag config, as TagFamilyConfig.AllFamilyNames() does.</summary>
        internal static HashSet<string> CreatorFamilyNames()
        {
            var names = BuiltInCreatorFamilyNames();
            var declared = new List<StingTools.Tags.TagDeclaration>();
            foreach (var f in Directory.GetFiles(Path.Combine(RepoRoot(), "StingTools", "Data"), "STING_TAG_CONFIG_v5_0_*.csv"))
                declared.AddRange(StingTools.Tags.TagConfigDeclarations.Parse(File.ReadLines(f)));
            foreach (var d in StingTools.Tags.DeclaredTagFamilies.NotBuiltBy(declared, names))
                names.Add(d.FamilyName);
            return names;
        }

        /// <summary>The names in the creator's C# tables only.</summary>
        internal static HashSet<string> BuiltInCreatorFamilyNames()
        {
            string src = File.ReadAllText(Path.Combine(RepoRoot(), "StingTools", "Tags", "TagFamilyCreatorCommand.cs"));
            var bics = Regex.Matches(Block(src, "CategoryTemplateMap ="), @"BuiltInCategory\.(OST_\w+)\s*,")
                            .Select(m => m.Groups[1].Value).Distinct().ToList();
            var pair = new Regex(@"BuiltInCategory\.(OST_\w+)\s*,\s*""([^""]*)""");
            var display = pair.Matches(Block(src, "CategoryDisplayName =")).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
            var csvKey  = pair.Matches(Block(src, "CategoryCsvFamilyKey =")).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
            var toCsv   = Regex.Matches(Block(src, "VariantSuffixToCsvName ="), @"\{\s*""([^""]+)""\s*,\s*""([^""]+)""\s*\}")
                               .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in bics)
                names.Add("STING - " + (csvKey.TryGetValue(b, out var k) ? k : display.TryGetValue(b, out var d) ? d : b.Substring(4)) + " Tag");
            var variant = new Regex(@"\(\s*BuiltInCategory\.OST_\w+\s*,\s*""[^""]*""\s*,\s*""[^""]*""\s*,\s*""([^""]*)""\s*\)");
            foreach (var arr in new[] { "TieInPointFamilies =", "DisciplineSheetFamilies =", "StructuralVariantFamilies =",
                                        "MepVariantFamilies =", "HealthcareVariantFamilies =" })
                foreach (Match m in variant.Matches(Block(src, arr)))
                {
                    string suffix = m.Groups[1].Value;
                    names.Add(toCsv.TryGetValue(suffix, out var csv) ? csv : "STING - " + suffix + " Tag");
                }
            return names;
        }

        [Fact]
        public void The_creator_table_parse_is_not_empty()
        {
            // Guards the parse itself: an empty set would make the next test pass vacuously.
            var names = CreatorFamilyNames();
            Assert.True(names.Count > 150, $"parsed only {names.Count} family names");
            Assert.Contains("STING - Door Tag", names);
            Assert.Contains("STING - Room Tag", names);
        }

        [Fact]
        public void Every_tag_family_a_drawing_type_names_is_one_the_creator_builds()
        {
            var built = CreatorFamilyNames();
            var json = JObject.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "StingTools", "Data", "STING_DRAWING_TYPES.json")));
            var unknown = new List<string>();
            int seen = 0;
            foreach (var dt in json["drawingTypes"] ?? new JArray())
            {
                var ann = dt["annotation"] as JObject;
                if (ann == null) continue;
                foreach (var p in (ann["tagFamilies"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                {
                    seen++;
                    string fam = (string)p.Value;
                    if (!string.IsNullOrWhiteSpace(fam) && !built.Contains(fam)) unknown.Add($"{dt["id"]}: tagFamilies[{p.Name}] = {fam}");
                }
                foreach (var r in (ann["rules"] as JArray) ?? new JArray())
                {
                    string fam = (string)r["tagFamily"];
                    if (string.IsNullOrWhiteSpace(fam)) continue;
                    seen++;
                    if (!built.Contains(fam)) unknown.Add($"{dt["id"]}: rule {r["category"]} tagFamily = {fam}");
                }
            }
            Assert.True(seen > 100, $"only {seen} tag family references found — the catalogue shape changed");
            Assert.True(unknown.Count == 0, "Tag families no tag creator builds:\n" + string.Join("\n", unknown));
        }
    }
}
