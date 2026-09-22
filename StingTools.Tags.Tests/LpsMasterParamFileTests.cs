// MR_PARAMETERS_LPS_MASTER.txt is a 113-parameter subset of MR_PARAMETERS.txt,
// so Revit's "Add parameter" dialog offers only what the LPS master needs
// instead of 3,617 definitions.
//
// WHY IT NEEDS A GATE
//
// A shared parameter IS its GUID. A subset that carries a DIFFERENT GUID under
// the same name creates a different parameter: it binds, the label renders, the
// family looks finished - and it never receives a value from anything STING
// writes, because everything else is writing to the other GUID. Nothing would
// report it. The tag would simply be blank on every element, forever, and the
// obvious explanation would be that the data is missing.
//
// Copies drift. If a GUID is ever corrected in MR_PARAMETERS.txt, this subset
// keeps the old one and silently becomes the wrong file. That is the same shape
// as the content library dated 8 August, and as the build sheet's shared block.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class LpsMasterParamFileTests
    {
        private static DirectoryInfo RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            return dir;
        }

        private static string Data(string name)
        {
            var root = RepoRoot();
            Assert.True(root != null, "repo root not found");
            string p = Path.Combine(root.FullName, "StingTools", "Data", name);
            Assert.True(File.Exists(p), name + " not found");
            return p;
        }

        /// <summary>Shared by name to GUID. Tolerates the file being open elsewhere.</summary>
        private static Dictionary<string, string> Params(string path)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                           FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    var f = line.Split('\t');
                    // PARAM <guid> <name> ...
                    if (f.Length > 2 && f[0] == "PARAM" && !map.ContainsKey(f[2]))
                        map[f[2]] = f[1];
                }
            }
            return map;
        }

        [Fact]
        public void EveryGuidMatchesTheMasterParameterFile()
        {
            var master = Params(Data("MR_PARAMETERS.txt"));
            var subset = Params(Data("MR_PARAMETERS_LPS_MASTER.txt"));

            Assert.True(subset.Count > 100,
                        $"only {subset.Count} parameters parsed from the subset - " +
                        "the file format has probably changed and this gate is not reading it");

            var wrong = new List<string>();
            foreach (var kv in subset)
            {
                string canonical;
                if (!master.TryGetValue(kv.Key, out canonical))
                    wrong.Add($"{kv.Key}: not in MR_PARAMETERS.txt at all");
                else if (!string.Equals(canonical, kv.Value, StringComparison.OrdinalIgnoreCase))
                    wrong.Add($"{kv.Key}: subset has {kv.Value}, master has {canonical}");
            }

            Assert.True(wrong.Count == 0,
                $"{wrong.Count} parameter(s) in the LPS subset do not match MR_PARAMETERS.txt. " +
                "A differing GUID is a DIFFERENT parameter: it binds, the label renders, and it " +
                "never receives a value. Regenerate with tools/gen_lps_params.py.\n  " +
                string.Join("\n  ", wrong.Take(8)));
        }

        [Fact]
        public void NoTwoParametersShareAGuid()
        {
            // Two names on one GUID is one parameter with an alias. Revit would
            // accept the file and the second name would quietly shadow the first.
            var subset = Params(Data("MR_PARAMETERS_LPS_MASTER.txt"));

            var clashes = subset.GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                                .Where(g => g.Count() > 1)
                                .Select(g => $"{g.Key}: " + string.Join(", ", g.Select(x => x.Key)))
                                .ToList();

            Assert.True(clashes.Count == 0,
                $"{clashes.Count} GUID(s) are used by more than one parameter:\n  " +
                string.Join("\n  ", clashes));
        }

        [Fact]
        public void EveryParameterTheBuildSheetNamesIsPresent()
        {
            // The subset exists to make authoring fast. One missing parameter
            // sends the author back to the 3,617-definition file mid-build,
            // which is exactly the cost it was made to avoid - and worse, it
            // invites adding the parameter by hand, with a new GUID.
            var subset = Params(Data("MR_PARAMETERS_LPS_MASTER.txt"));
            var root = RepoRoot();
            string sheet = Path.Combine(root.FullName, "docs", "LPS_TAG_MASTER_BUILD_SHEET.md");
            Assert.True(File.Exists(sheet), "LPS_TAG_MASTER_BUILD_SHEET.md not found");

            var named = new HashSet<string>(StringComparer.Ordinal);
            using (var fs = new FileStream(sheet, FileMode.Open, FileAccess.Read,
                                           FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    foreach (System.Text.RegularExpressions.Match m in
                             System.Text.RegularExpressions.Regex.Matches(line, @"\b[A-Z][A-Z0-9_]{5,}\b"))
                    {
                        string p = m.Value;
                        // Only things that look like STING parameters, and not
                        // the tier gates' own template text.
                        if (p.StartsWith("ELC_") || p.StartsWith("ASS_") || p.StartsWith("CST_") ||
                            p.StartsWith("CBN_") || p.StartsWith("COMM_") || p.StartsWith("WARN_") ||
                            p.StartsWith("TAG_PARA_STATE_"))
                            named.Add(p);
                    }
                }
            }

            var absent = named.Where(p => !subset.ContainsKey(p))
                              .OrderBy(p => p, StringComparer.Ordinal).ToList();

            Assert.True(absent.Count == 0,
                $"{absent.Count} parameter(s) named in the build sheet are absent from the LPS " +
                "subset, so the author would have to leave the file mid-build:\n  " +
                string.Join("\n  ", absent.Take(10)));
        }
    }
}
