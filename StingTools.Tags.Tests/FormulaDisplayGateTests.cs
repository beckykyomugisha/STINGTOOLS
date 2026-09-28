// A shipped formula must never reference a tier display gate.
//
// WHY THIS IS A TEST AND NOT A CONVENTION
//
// TAG_PARA_STATE_n_BOOL decides whether a TAG DRAWS a tier. The narrative
// formulas write to a STORED parameter, and whether a tag shows that value is
// decided by the tag's label rows and the tag-type gates — never by the formula.
// So inside a formula the gate does not control display at all. It controls
// whether the value EXISTS, which is a presentation setting governing data.
//
// That cost 36 formulas their entire output. They were wrapped in
// if(TAG_PARA_STATE_3_BOOL, ..., ""), T3 was dropped from the universal tag
// master, the identifier became unresolvable, and every one of them failed on
// every element — silently, because a failed formula and a formula with nothing
// to say were the same null.
//
// The runtime now defaults an unbound gate to open, which stops the failure. It
// does NOT stop the conflation: bind T3 to a model category, set it false, and
// the narratives quietly stop being written again. Only removing the gate from
// the data closes that, and only this test stops it being reintroduced by the
// next person who reads "S3 = comprehensive" and thinks a formula is the place
// to express it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class FormulaDisplayGateTests
    {
        private static string FormulaCsv()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            Assert.True(dir != null, "could not locate StingTools/Data from the test output directory");
            string path = Path.Combine(dir.FullName, "StingTools", "Data",
                                       "FORMULAS_WITH_DEPENDENCIES.csv");
            Assert.True(File.Exists(path), path);
            return path;
        }

        [Fact]
        public void NoShippedFormulaReferencesATierDisplayGate()
        {
            // Matches the prose spelling too ("TAG_PARA_STATE_1/2/3_BOOL"), which
            // is how four stale descriptions survived the first sweep: the code
            // regex wanted \d+ and the prose used slashes.
            var any = new Regex(@"TAG_PARA_STATE[_0-9/]*_BOOL", RegexOptions.IgnoreCase);

            var offenders = new List<string>();
            var lines = File.ReadAllLines(FormulaCsv());
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("#")) continue;
                if (any.IsMatch(lines[i]))
                    offenders.Add($"line {i + 1}: {lines[i].Substring(0, Math.Min(90, lines[i].Length))}");
            }

            Assert.True(offenders.Count == 0,
                "A formula references a tier display gate. Display is the tag's business; a "
                + "formula that gates on it destroys data instead of hiding it.\n"
                + string.Join("\n", offenders));
        }

        [Fact]
        public void TheFileWasActuallyRead()
        {
            // A control. If the locator walked past the repo or the file were
            // empty, the test above would pass by reading nothing — an empty list
            // standing in for a result.
            var lines = File.ReadAllLines(FormulaCsv()).Where(l => !l.StartsWith("#")).ToList();
            Assert.True(lines.Count > 250,
                $"expected the shipped formula set to have hundreds of rows, read {lines.Count}");
            Assert.Contains(lines, l => l.Contains("ARCH_TAG_7_PARA_WALL_TXT"));
        }

        [Fact]
        public void TheRuntimeGuardStillCoversTheNamesTheDataNoLongerUses()
        {
            // The data gate above and the runtime default are not duplicates.
            // This one covers shipped data, which CI sees. The runtime default
            // covers anything CI does not — a hand-edited data file on a machine,
            // or a formula source added later. Keeping both is deliberate; the
            // runtime guard is read on every input lookup, not declared and
            // forgotten.
            Assert.True(DisplayGateRule.IsDisplayGate("TAG_PARA_STATE_3_BOOL"));
        }
    }
}
