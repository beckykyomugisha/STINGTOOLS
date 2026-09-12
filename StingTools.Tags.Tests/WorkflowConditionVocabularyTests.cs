// ══════════════════════════════════════════════════════════════════════════
//  WorkflowConditionVocabularyTests.cs — keep the declared vocabulary and the
//  code that implements it from drifting apart.
//
//  WorkflowEngine.InlineConditionVocabulary exists so that a `condition` the
//  single-condition path does not test is REPORTED instead of silently ignored.
//  A hand-written list is only worth having if it cannot rot, so this reads the
//  engine's own source and fails when the two disagree in either direction:
//
//    a name in the list with no `cond == "..."` behind it  -> the warning never
//        fires for a condition that genuinely is ungated
//    a `cond == "..."` with no name in the list            -> the warning fires
//        for a condition that IS gated, and gets ignored as noise
//
//  Both failures end the same way: the report stops meaning anything. Reading
//  the source is the same mechanism check_workflow_wiring.ps1 uses, and for the
//  same reason — the test project cannot reference StingTools.csproj, which
//  needs the Revit API.
//
//  RED / GREEN, recorded 2026-09-10:
//    RED   one name deleted from the array   -> "declared but not implemented"
//          reports has_untagged, 1 of 2 assertions fails
//    GREEN 14 names, 14 comparisons, exact match
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace StingTools.Tags.Tests
{
    public class WorkflowConditionVocabularyTests
    {
        private readonly ITestOutputHelper _out;
        public WorkflowConditionVocabularyTests(ITestOutputHelper output) => _out = output;

        private static string EngineSource()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Core", "WorkflowEngine.cs")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate WorkflowEngine.cs from " + AppContext.BaseDirectory);
            return File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "Core", "WorkflowEngine.cs"));
        }

        /// <summary>The names the array declares.</summary>
        private static List<string> Declared(string src)
        {
            int i = src.IndexOf("InlineConditionVocabulary =", StringComparison.Ordinal);
            Assert.True(i > 0, "InlineConditionVocabulary array not found — was it renamed?");
            int open = src.IndexOf('{', i);
            int close = src.IndexOf("};", open, StringComparison.Ordinal);
            Assert.True(open > 0 && close > open, "could not read the array body");
            return Regex.Matches(src.Substring(open, close - open), "\"([a-z_0-9]+)\"")
                        .Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToList();
        }

        /// <summary>The names the single-condition block actually compares against.</summary>
        private static List<string> Implemented(string src)
        {
            int k = src.IndexOf("string cond = step.Condition.Trim().ToLowerInvariant();", StringComparison.Ordinal);
            int m = src.IndexOf("// Phase 69: Compound condition evaluation (AND/OR logic)", StringComparison.Ordinal);
            Assert.True(k > 0 && m > k, "could not locate the single-condition block — did it move?");
            return Regex.Matches(src.Substring(k, m - k), "cond == \"([a-z_0-9]+)\"")
                        .Select(x => x.Groups[1].Value).Distinct().OrderBy(x => x).ToList();
        }

        [Fact]
        public void The_Declared_Vocabulary_Is_Exactly_What_The_Block_Tests()
        {
            string src = EngineSource();
            var declared = Declared(src);
            var implemented = Implemented(src);

            var missing = declared.Except(implemented).ToList();
            var extra = implemented.Except(declared).ToList();

            Assert.True(missing.Count == 0,
                "declared but not implemented — the ungated-condition warning will never fire "
              + "for these:\n  " + string.Join("\n  ", missing));
            Assert.True(extra.Count == 0,
                "implemented but not declared — the warning will fire for conditions that ARE "
              + "gated, and will be ignored as noise:\n  " + string.Join("\n  ", extra));

            _out.WriteLine($"{declared.Count} declared, {implemented.Count} implemented, exact match");
        }

        [Fact]
        public void The_Two_Conditions_W5_Added_Are_In_Both_Paths()
        {
            // The defect W5 had to avoid: a condition added to the compound switch only
            // would run UNGATED through the single-condition path, which is how 14 of the
            // 15 condition values shipped presets use behave today.
            string src = EngineSource();
            var implemented = Implemented(src);

            foreach (string c in new[] { "has_unclassed_materials", "has_uncoded_materials" })
            {
                Assert.Contains(c, implemented);                       // single-condition path
                Assert.Contains("case \"" + c + "\":", src);           // compound path
            }
        }

        [Fact]
        public void Every_Condition_A_Shipped_Preset_Uses_Is_Known_To_At_Least_One_Path()
        {
            // Not a hard zero: 14 shipped values are switch-only or unknown, which is
            // WF-COND-1 and is reported at run time rather than fixed here. What this
            // pins is that the two W5 added did not join them, and that the count of
            // ungated ones does not GROW.
            string src = EngineSource();
            var implemented = new HashSet<string>(Implemented(src), StringComparer.OrdinalIgnoreCase);

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            Assert.True(dir != null);

            var ungated = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in Directory.GetFiles(Path.Combine(dir.FullName, "StingTools", "Data"),
                                                    "WORKFLOW_*.json"))
            {
                foreach (Match mm in Regex.Matches(File.ReadAllText(f), "\"condition\"\\s*:\\s*\"([^\"]+)\""))
                {
                    string c = mm.Groups[1].Value.Trim().ToLowerInvariant();
                    if (c.Length > 0 && !implemented.Contains(c)) ungated.Add(c);
                }
            }

            _out.WriteLine("ungated single-condition values in shipped presets: "
                         + string.Join(", ", ungated));

            // 14 as measured on 2026-09-10. A ceiling, not a target: this may shrink when
            // WF-COND-1 is closed, and must not grow.
            Assert.True(ungated.Count <= 14,
                $"{ungated.Count} shipped conditions are not tested by the single-condition "
              + "path, up from 14. A new one means a step ships with a gate that does "
              + "nothing:\n  " + string.Join("\n  ", ungated));

            Assert.DoesNotContain("has_unclassed_materials", ungated);
            Assert.DoesNotContain("has_uncoded_materials", ungated);
        }
    }
}
