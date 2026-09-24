// A material tag can only show what a MATERIAL carries.
//
// WHAT WENT WRONG
//
// STING - Materials Tag was given the universal label: 68 parameters across ten tiers
// and a warnings block, gated on TAG_PARA_STATE_* / TAG_WARN_VISIBLE_BOOL. A material
// tag reads the tagged MATERIAL's parameters only — never the element's — and on a
// Material none of those resolve:
//   * `<ALL>` bindings never reach Materials. LoadSharedParamsCommand.CleanMaterialBindings
//     removes Materials from every parameter that is not material-relevant (MAT_, PROP_,
//     BLE_MAT_, … — IsMaterialRelevantParam), and RESOLVED_BINDINGS.csv records the result.
//   * The tier gates are read from the tagged element's TYPE (Core/TierGateScope.cs); a
//     Material has none.
// tools/check_tag_row_bindings.py could not see it: it counts `<ALL>` as bound for every
// category, which is true everywhere except Materials. So 66 of 68 rows looked fine.
//
// WHAT THIS CHECKS
//
// Every parameter a Materials tag spec names — as a row or inside a row's formula — must
// be a Material built-in or bound to Materials in RESOLVED_BINDINGS.csv. Zero exceptions:
// STING - Materials Tag was rebuilt in place on 2026-09-24 (MAT_CODE / MAT_NAME /
// MAT_MANUFACTURER / MAT_STANDARD, no tier gates), so the 68-row baseline this test
// started with is gone.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class MaterialTagLabelTests
    {
        /// <summary>Material identity built-ins a Material Tag label can show without any shared parameter.</summary>
        public static readonly HashSet<string> MaterialBuiltIns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Mark", "Description", "Keynote", "Manufacturer", "Model", "Cost",
            "Comments", "URL", "Name", "Class",
        };

        private static string Repo()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "StingTools", "Data"))) d = d.Parent;
            Assert.True(d != null, "StingTools/Data not found");
            return d.FullName;
        }

        private static string Data(string f) => Path.Combine(Repo(), "StingTools", "Data", f);

        /// <summary>Parameters RESOLVED_BINDINGS.csv binds to Materials explicitly.</summary>
        internal static HashSet<string> BoundToMaterials()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(Data("RESOLVED_BINDINGS.csv")))
            {
                if (line.StartsWith("#") || !line.Contains(',')) continue;
                int c = line.IndexOf(',');
                var cats = line.Substring(c + 1).Trim().Trim('"').Split('|').Select(x => x.Trim());
                if (cats.Contains("Materials")) set.Add(line.Substring(0, c).Trim());
            }
            return set;
        }

        private static HashSet<string> SharedNames() => new HashSet<string>(
            File.ReadLines(Data("MR_PARAMETERS.txt")).Where(l => l.StartsWith("PARAM\t"))
                .Select(l => l.Split('\t')[2]), StringComparer.Ordinal);

        /// <summary>(spec key, family name, parameter) for every row of every Materials tag spec.</summary>
        private static List<(string Spec, string Family, string Param)> MaterialSpecParams()
        {
            var root = JObject.Parse(File.ReadAllText(Data("LABEL_DEFINITIONS.json")));
            var labels = root["category_labels"] as JObject;
            Assert.NotNull(labels);
            var shared = SharedNames();
            var rx = new Regex("[A-Z][A-Z0-9_]{3,}");
            var result = new List<(string, string, string)>();
            foreach (var prop in labels.Properties())
            {
                if (!(prop.Value is JObject spec)) continue;
                string fam = (string)spec["family_name"] ?? "";
                bool isMaterial = prop.Name == "Materials"
                    || fam.IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0
                       && fam.IndexOf("Tag", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isMaterial) continue;
                foreach (var tier in spec.Properties().Where(p => p.Value is JArray))
                    foreach (var row in ((JArray)tier.Value).OfType<JObject>())
                    {
                        var p = (string)row["param"];
                        if (!string.IsNullOrWhiteSpace(p)) result.Add((prop.Name, fam, p.Trim()));
                        var f = (string)row["formula"];
                        if (!string.IsNullOrWhiteSpace(f))
                            foreach (Match m in rx.Matches(f))
                                if (shared.Contains(m.Value)) result.Add((prop.Name, fam, m.Value));
                    }
            }
            return result;
        }

        [Fact]
        public void The_measurement_is_real()
        {
            // Controls: the Materials spec was found, and the binding truth has the
            // material parameters in it — else every assertion below passes over nothing.
            Assert.Contains(MaterialSpecParams(), r => r.Spec == "Materials");
            var mat = BoundToMaterials();
            Assert.Contains("MAT_CODE", mat);
            Assert.Contains("MAT_NAME", mat);
            Assert.True(mat.Count >= 50, $"only {mat.Count} parameters bound to Materials");
            Assert.DoesNotContain("ASS_TAG_1_TXT", mat);   // <ALL> does not reach Materials
        }

        [Fact]
        public void No_material_tag_row_names_a_parameter_a_material_cannot_carry()
        {
            var readable = BoundToMaterials();
            var dead = MaterialSpecParams()
                .Where(r => !readable.Contains(r.Param) && !MaterialBuiltIns.Contains(r.Param))
                .Select(r => $"{r.Spec} ({r.Family}): {r.Param}").Distinct().OrderBy(x => x).ToList();
            Assert.True(dead.Count == 0,
                "A material tag row names a parameter no Material carries — it will print blank on every tag. " +
                "Use a Material built-in (Mark, Description, Keynote, Manufacturer, Model, …) or a parameter " +
                "bound to Materials (MAT_*, PROP_*, BLE_MAT_* …). Tier gates (TAG_PARA_STATE_*, " +
                "TAG_WARN_VISIBLE_BOOL) cannot work either: a Material has no type to read them from.\n" +
                string.Join("\n", dead));
        }

        [Fact]
        public void The_rebuilt_Materials_Tag_carries_the_callout_rows()
        {
            var rows = MaterialSpecParams().Where(r => r.Spec == "Materials").Select(r => r.Param).ToList();
            Assert.Equal(new[] { "MAT_CODE", "MAT_NAME", "MAT_MANUFACTURER", "MAT_STANDARD" }, rows);
        }
    }
}
