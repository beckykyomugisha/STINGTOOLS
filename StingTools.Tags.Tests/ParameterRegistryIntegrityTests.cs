// Integrity of the shipped parameter data.
//
// PARAMETER_REGISTRY.json, MR_PARAMETERS.txt and MR_PARAMETERS.csv describe the
// same parameters three times. Nothing checked that they agreed, and nothing
// checked that any one of them agreed with itself.
//
// Measured 2026-09-21: support_params held 26 entries listed TWICE - identical
// GUID, identical name, identical every field. Harmless-looking, because a
// dictionary build just keeps the last one, which is exactly why it survived:
// it produced no error, no warning and no wrong answer, only a quietly wrong
// count for anyone measuring the registry.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class ParameterRegistryIntegrityTests
    {
        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            return dir == null ? null : Path.Combine(dir.FullName, "StingTools", "Data");
        }

        private static JObject Registry()
        {
            string data = DataDir();
            Assert.True(data != null, "StingTools/Data not found");
            string p = Path.Combine(data, "PARAMETER_REGISTRY.json");
            Assert.True(File.Exists(p), "PARAMETER_REGISTRY.json is missing");
            return JObject.Parse(File.ReadAllText(p));
        }

        /// <summary>Every registry list whose entries carry a param_name.</summary>
        public static IEnumerable<object[]> ParamLists()
        {
            yield return new object[] { "support_params" };
            yield return new object[] { "warning_thresholds" };
            yield return new object[] { "source_tokens" };
            yield return new object[] { "sheet_tokens" };
        }

        [Theory]
        [MemberData(nameof(ParamLists))]
        public void NoRegistryListHoldsTheSameParameterTwice(string listName)
        {
            var list = Registry()[listName] as JArray;
            Assert.True(list != null && list.Count > 0, listName + " is missing or empty");

            var dupNames = list.Select(e => (string)e["param_name"])
                               .Where(n => !string.IsNullOrEmpty(n))
                               .GroupBy(n => n, StringComparer.Ordinal)
                               .Where(g => g.Count() > 1)
                               .Select(g => $"{g.Key} x{g.Count()}")
                               .ToList();

            Assert.True(dupNames.Count == 0,
                        $"{listName} lists {dupNames.Count} parameter(s) more than once:\n  " +
                        string.Join("\n  ", dupNames.Take(15)));
        }

        [Fact]
        public void SupportParamGuidsAreUnique()
        {
            // A repeated GUID is worse than a repeated name: two DIFFERENT
            // parameters sharing one GUID are the same parameter to Revit, and
            // whichever binds second silently wins.
            var list = (JArray)Registry()["support_params"];

            var dupGuids = list.Select(e => new { G = (string)e["guid"], N = (string)e["param_name"] })
                               .Where(x => !string.IsNullOrEmpty(x.G))
                               .GroupBy(x => x.G, StringComparer.OrdinalIgnoreCase)
                               .Where(g => g.Count() > 1)
                               .Select(g => g.Key.Substring(0, 13) + " -> " +
                                            string.Join(", ", g.Select(x => x.N).Distinct()))
                               .ToList();

            Assert.True(dupGuids.Count == 0,
                        $"{dupGuids.Count} GUID(s) used by more than one entry:\n  " +
                        string.Join("\n  ", dupGuids.Take(15)));
        }

        [Fact]
        public void MrParametersTxtHasNoRepeatedNameOrGuid()
        {
            // The shared parameter FILE is what Revit ingests. A repeated line
            // here is a different and worse problem than a repeated registry
            // entry, so it is checked separately rather than assumed from it.
            string data = DataDir();
            var names = new List<string>();
            var guids = new List<string>();

            foreach (var line in File.ReadLines(Path.Combine(data, "MR_PARAMETERS.txt")))
            {
                if (!line.StartsWith("PARAM\t", StringComparison.Ordinal)) continue;
                var f = line.Split('\t');
                if (f.Length > 2) { guids.Add(f[1]); names.Add(f[2]); }
            }

            Assert.True(names.Count > 3000, $"MR_PARAMETERS.txt looks wrong: {names.Count} rows");

            var dn = names.GroupBy(n => n, StringComparer.Ordinal).Where(g => g.Count() > 1)
                          .Select(g => g.Key).ToList();
            var dg = guids.GroupBy(g => g, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
                          .Select(g => g.Key).ToList();

            Assert.True(dn.Count == 0, "repeated parameter names:\n  " + string.Join("\n  ", dn.Take(10)));
            Assert.True(dg.Count == 0, "repeated GUIDs:\n  " + string.Join("\n  ", dg.Take(10)));
        }

        [Fact]
        public void TheRegistryAgreesWithMrParametersOnGuids()
        {
            // Three files describe the same parameters. Where they overlap they
            // must not disagree - a parameter whose GUID differs between the
            // registry and the shared parameter file binds one way and reads the
            // other, which no error would ever announce.
            string data = DataDir();

            var fileGuid = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(Path.Combine(data, "MR_PARAMETERS.txt")))
            {
                if (!line.StartsWith("PARAM\t", StringComparison.Ordinal)) continue;
                var f = line.Split('\t');
                if (f.Length > 2) fileGuid[f[2]] = f[1];
            }

            var clashes = new List<string>();
            foreach (var e in (JArray)Registry()["support_params"])
            {
                string n = (string)e["param_name"], g = (string)e["guid"];
                if (string.IsNullOrEmpty(n) || string.IsNullOrEmpty(g)) continue;

                string fg;
                if (!fileGuid.TryGetValue(n, out fg)) continue;   // registry-only is a separate question
                if (!string.Equals(fg, g, StringComparison.OrdinalIgnoreCase))
                    clashes.Add($"{n}: registry {g.Substring(0, 13)} vs MR_PARAMETERS {fg.Substring(0, 13)}");
            }

            Assert.True(clashes.Count == 0,
                        $"{clashes.Count} parameter(s) carry different GUIDs in the two files:\n  " +
                        string.Join("\n  ", clashes.Take(15)));
        }
    }
}
