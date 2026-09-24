// The FUNC vocabulary must contain every code the resolvers can emit.
//
// Read from SOURCE rather than by calling the validator, because
// ISO19650Validator reaches TagConfig and therefore Revit. The property this
// guards is a relationship between two files, and text is enough to check it.
//
// WHAT WENT WRONG
//
// ValidFuncCodes was built from TagConfig.FuncMap.KEYS. FuncMap is SYS -> FUNC,
// so the keys are SYSTEM codes: the validator checked FUNC tokens against the
// wrong vocabulary in every project that has a FuncMap, which is every project,
// because the built-in defaults populate it. 14 legitimately emitted codes were
// rejected - SUP among them, the most common FUNC there is - while ARC, HVAC,
// HWS and LV were accepted as functions although they are systems.
//
// It survived because the FALLBACK list was correct, and the fallback applies
// exactly when FuncMap is empty, which never happens.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class FuncVocabularyTests
    {
        private static DirectoryInfo Repo()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Core")))
                dir = dir.Parent;
            Assert.True(dir != null, "could not locate StingTools/Core");
            return dir;
        }

        private static string Src(params string[] parts)
        {
            string p = Path.Combine(new[] { Repo().FullName }.Concat(parts).ToArray());
            Assert.True(File.Exists(p), p);
            return File.ReadAllText(p);
        }

        /// <summary>FuncMap VALUES — the actual FUNC codes, not the SYS keys.</summary>
        private static HashSet<string> FuncMapValues()
        {
            string t = Src("StingTools", "Core", "TagConfig.Defaults.cs");
            int a = t.IndexOf("DefaultFuncMap", StringComparison.Ordinal);
            int b = t.IndexOf("DefaultLocCodes", StringComparison.Ordinal);
            Assert.True(a > 0 && b > a, "DefaultFuncMap block not found");
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(t.Substring(a, b - a),
                                              @"\{\s*""([A-Z0-9_]+)""\s*,\s*""([A-Z0-9_]+)""\s*\}"))
                set.Add(m.Groups[2].Value);
            return set;
        }

        private static HashSet<string> DeclaredSubFunctions()
        {
            string t = Src("StingTools", "Core", "ISO19650Validator.cs");
            int a = t.IndexOf("SubFunctionCodes = new HashSet", StringComparison.Ordinal);
            Assert.True(a > 0, "SubFunctionCodes not found");
            string block = t.Substring(a, t.IndexOf("};", a, StringComparison.Ordinal) - a);
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(block, @"""([A-Z]{2,5})""")) set.Add(m.Groups[1].Value);
            return set;
        }

        /// <summary>Every literal the family-aware sub-function resolvers can return.</summary>
        private static HashSet<string> EmittedByResolvers()
        {
            string t = Src("StingTools", "Core", "TagConfig.cs");
            var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string fn in new[] { "GetHvacSubFunction", "GetHwsSubFunction",
                                          "GetSanSubFunction", "ResolveLpsFunc" })
            {
                int i = t.IndexOf(fn, StringComparison.Ordinal);
                while (i >= 0)
                {
                    int len = Math.Min(2600, t.Length - i);
                    string seg = t.Substring(i, len);
                    int end = seg.IndexOf("\n        private static", StringComparison.Ordinal);
                    if (end > 200) seg = seg.Substring(0, end);
                    foreach (Match m in Regex.Matches(seg, @"return\s+""([A-Z]{2,5})""\s*;"))
                        emitted.Add(m.Groups[1].Value);
                    i = t.IndexOf(fn, i + 1, StringComparison.Ordinal);
                }
            }
            return emitted;
        }

        [Fact]
        public void EveryCodeAResolverCanEmitIsInTheVocabulary()
        {
            var vocab = FuncMapValues();
            vocab.UnionWith(DeclaredSubFunctions());

            var missing = EmittedByResolvers().Where(c => !vocab.Contains(c)).OrderBy(c => c).ToList();

            // Enumerating what the code returns, rather than listing codes by
            // hand, is the point: a sub-function added later is covered without
            // anyone remembering to update this test.
            Assert.True(missing.Count == 0,
                "These FUNC codes are returned by a resolver but are not in the vocabulary, so "
                + "ValidateToken rejects every element carrying one: " + string.Join(", ", missing));
        }

        [Fact]
        public void TheVocabularyIsBuiltFromValuesNotKeys()
        {
            string t = Src("StingTools", "Core", "ISO19650Validator.cs");
            Assert.DoesNotContain("new HashSet<string>(TagConfig.FuncMap.Keys", t);
            Assert.Contains("new HashSet<string>(TagConfig.FuncMap.Values", t);
        }

        [Theory]
        [InlineData("SUP")]   // the most common FUNC; was rejected
        [InlineData("RTN")]
        [InlineData("EXH")]
        [InlineData("PWR")]
        [InlineData("FIT")]
        [InlineData("HTG")]
        public void CommonFunctionCodesAreValid(string code)
        {
            var vocab = FuncMapValues();
            vocab.UnionWith(DeclaredSubFunctions());
            Assert.Contains(code, vocab);
        }

        [Theory]
        [InlineData("HVAC")]  // these are SYSTEM codes and must not be FUNC codes
        [InlineData("HWS")]
        [InlineData("ARC")]
        [InlineData("LV")]
        public void SystemCodesAreNotFunctionCodes(string code)
        {
            var vocab = FuncMapValues();
            vocab.UnionWith(DeclaredSubFunctions());
            Assert.DoesNotContain(code, vocab);
        }

        [Fact]
        public void TheSourcesWereActuallyRead()
        {
            // A control. If a locator or a regex silently matched nothing, the
            // tests above would pass over empty sets.
            Assert.True(FuncMapValues().Count >= 15, "FuncMap values look empty");
            Assert.True(DeclaredSubFunctions().Count >= 10, "SubFunctionCodes looks empty");
            Assert.True(EmittedByResolvers().Count >= 8, "no resolver literals found");
        }
    }
}
