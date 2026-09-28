// FUNC / PROD / SEQ alignment — the 2026-09-27 review.
//
// Each test pins one relationship that had drifted. Where the code under test
// reaches Revit (TagConfig, ISO19650Validator) the property is checked in the
// SOURCE and DATA, the same way FuncVocabularyTests does: what is being guarded
// is an agreement between files, and text is enough to check it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class FuncProdSeqAlignmentTests
    {
        // ── helpers ────────────────────────────────────────────────────────

        private static string Repo()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Core")))
                dir = dir.Parent;
            Assert.True(dir != null, "could not locate StingTools/Core");
            return dir.FullName;
        }

        private static string Src(params string[] parts)
        {
            string p = Path.Combine(new[] { Repo() }.Concat(parts).ToArray());
            Assert.True(File.Exists(p), p);
            return File.ReadAllText(p);
        }

        /// <summary>Body of a C# method: from its signature to the next member at the same indent.</summary>
        private static string MethodBody(string source, string signature)
        {
            int a = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(a >= 0, "not found: " + signature);
            int b = source.IndexOf("\n        public static ", a + signature.Length, StringComparison.Ordinal);
            int c = source.IndexOf("\n        private static ", a + signature.Length, StringComparison.Ordinal);
            int end = new[] { b, c }.Where(i => i > 0).DefaultIfEmpty(source.Length).Min();
            return source.Substring(a, end - a);
        }

        /// <summary>SYS → valid FUNC codes from STING_FUNC_SYS_MATRIX.csv.</summary>
        private static Dictionary<string, HashSet<string>> Matrix()
        {
            var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in Src("StingTools", "Data", "STING_FUNC_SYS_MATRIX.csv").Split('\n').Skip(1))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var f = line.Split(',');
                if (f.Length < 3) continue;
                if (!map.TryGetValue(f[0], out var set)) map[f[0]] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(f[2].Trim());
            }
            return map;
        }

        /// <summary>SYS → valid FUNC codes from the validator's hardcoded baseline.</summary>
        private static Dictionary<string, HashSet<string>> ValidatorBaseline()
        {
            string t = Src("StingTools", "Core", "ISO19650Validator.cs");
            int a = t.IndexOf("_validFuncsForSys =", StringComparison.Ordinal);
            Assert.True(a > 0, "_validFuncsForSys not found");
            string block = t.Substring(a, t.IndexOf("};", a, StringComparison.Ordinal) - a);
            var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(block, @"\{\s*""([A-Z0-9]+)"",\s*new HashSet<string>\([^)]*\)\s*\{([^}]*)\}"))
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match c in Regex.Matches(m.Groups[2].Value, @"""([A-Z0-9]+)""")) set.Add(c.Groups[1].Value);
                map[m.Groups[1].Value] = set;
            }
            return map;
        }

        /// <summary>DefaultFuncMap as SYS → FUNC.</summary>
        private static Dictionary<string, string> DefaultFuncMap()
        {
            string t = Src("StingTools", "Core", "TagConfig.Defaults.cs");
            int a = t.IndexOf("DefaultFuncMap", StringComparison.Ordinal);
            int b = t.IndexOf("DefaultLocCodes", StringComparison.Ordinal);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(t.Substring(a, b - a), @"\{\s*""([A-Z0-9_]+)""\s*,\s*""([A-Z0-9_]+)""\s*\}"))
                map[m.Groups[1].Value] = m.Groups[2].Value;
            return map;
        }

        private static bool ValidForSys(string sys, string func)
        {
            var matrix = Matrix();
            var baseline = ValidatorBaseline();
            return (matrix.TryGetValue(sys, out var a) && a.Contains(func))
                || (baseline.TryGetValue(sys, out var b) && b.Contains(func));
        }

        // ── FUNC ───────────────────────────────────────────────────────────

        [Fact]
        public void Every_default_FUNC_is_valid_for_its_own_SYS()
        {
            // FuncMap["LPS"] was "LPS", which the matrix does not list for SYS=LPS, so
            // every lightning-protection element failed the FUNC/SYS cross-check.
            var bad = DefaultFuncMap()
                .Where(kv => !ValidForSys(kv.Key, kv.Value))
                .Select(kv => kv.Key + "→" + kv.Value).ToList();
            Assert.True(bad.Count == 0, "Default FUNC not valid for its SYS: " + string.Join(", ", bad));
        }

        [Theory]
        [InlineData("LPS AIR TERMINAL 1M", "AT")]
        [InlineData("FINIAL", "AT")]
        [InlineData("DOWN CONDUCTOR TAPE", "DC")]
        [InlineData("EARTH ROD 2.4M", "EE")]
        [InlineData("EARTH PLATE", "EE")]
        [InlineData("EQUIPOTENTIAL BONDING BAR", "BOND")]
        [InlineData("SPARK GAP", "BOND")]
        [InlineData("LIGHTNING SPD TYPE 1", "SPD")]
        [InlineData("TEST CLAMP", "TC")]
        public void Lps_sub_function_is_read_off_the_name(string upper, string expected)
            => Assert.Equal(expected, LpsNameClassifier.Func(upper));

        [Fact]
        public void A_generic_Lps_name_has_no_sub_function()
            => Assert.Null(LpsNameClassifier.Func("LPS GENERIC COMPONENT"));

        [Fact]
        public void Every_Lps_sub_function_is_valid_for_SYS_LPS_in_matrix_and_validator()
        {
            // The resolver said AT/DC/EE/BOND/SPD/TC; the matrix and the validator said
            // AIR/DOW/ERT/BND/SPD/TST. One vocabulary now, checked in both places.
            string[] names =
            {
                "AIR TERMINAL", "DOWN CONDUCTOR", "EARTH ROD", "BONDING BAR",
                "LIGHTNING SPD TYPE 2", "TEST CLAMP",
            };
            var matrix = Matrix()["LPS"];
            var baseline = ValidatorBaseline()["LPS"];
            foreach (string n in names)
            {
                string code = LpsNameClassifier.Func(n);
                Assert.NotNull(code);
                Assert.Contains(code, matrix);
                Assert.Contains(code, baseline);
            }
            Assert.True(matrix.SetEquals(baseline),
                "LPS FUNC codes differ between STING_FUNC_SYS_MATRIX.csv and ISO19650Validator: "
                + string.Join(",", matrix.OrderBy(x => x)) + " vs " + string.Join(",", baseline.OrderBy(x => x)));
        }

        [Fact]
        public void Lps_data_files_use_the_same_FUNC_vocabulary()
        {
            var lps = Matrix()["LPS"];

            string inv = Src("StingTools", "Data", "LPS_FAMILY_INVENTORY.json");
            var invCodes = Regex.Matches(inv, @"""funcCode""\s*:\s*""([A-Z0-9]+)""").Select(m => m.Groups[1].Value).ToList();
            Assert.NotEmpty(invCodes);
            Assert.All(invCodes, c => Assert.Contains(c, lps));

            string[] lines = Src("StingTools", "Data", "COBIE_TYPE_MAP.csv").Replace("\r", "").Split('\n');
            var hdr = lines[0].TrimStart('﻿').Split(',').ToList();
            int sysIdx = hdr.IndexOf("StingSysCode"), funcIdx = hdr.IndexOf("StingFuncCode");
            Assert.True(sysIdx > 0 && funcIdx > 0, "COBIE_TYPE_MAP.csv header changed");
            var cobie = Regex.Matches(string.Join("\n", lines), @",(?:E|LV|G),LPS,([A-Z0-9]+),")
                             .Select(m => m.Groups[1].Value).ToList();
            Assert.NotEmpty(cobie);
            Assert.All(cobie, c => Assert.Contains(c, lps));
        }

        [Theory]
        [InlineData("LPS AIR TERMINAL")]
        [InlineData("SPARK GAP")]
        [InlineData("EARTH MESH")]
        [InlineData("INSPECTION POINT")]
        public void Lps_detection_and_Lps_PROD_use_one_keyword_set(string upper)
        {
            // "Spark Gap" used to be LPS to the validators and not to the PROD resolver.
            Assert.True(LpsNameClassifier.IsLps(upper));
            Assert.NotNull(ProdResolver.ResolveLps(upper));
        }

        [Fact]
        public void Every_Lps_keyword_list_goes_through_the_classifier()
        {
            string tc = Src("StingTools", "Core", "TagConfig.cs");
            Assert.Contains("LpsNameClassifier.Func(", MethodBody(tc, "public static string ResolveLpsFunc("));
            Assert.Contains("LpsNameClassifier.IsLps(", MethodBody(tc, "public static bool IsLightningProtection("));
            Assert.Contains("LpsNameClassifier.IsLps(", MethodBody(tc, "private static string GetSysFromFamilyName("));
            Assert.Contains("ResolveLpsFunc(el)", MethodBody(tc, "public static string GetSmartFuncCode("));
            Assert.Contains("LpsNameClassifier.IsLps(", Src("StingTools", "Core", "ProdResolver.cs"));
        }

        // ── PROD ───────────────────────────────────────────────────────────

        [Fact]
        public void The_PROD_token_never_carries_the_material_suffix()
        {
            // "COL" + "-" + "STL" contains the tag separator: the stored token was cut back
            // to COL and the overwrite path wrote a 9-segment tag.
            string tc = Src("StingTools", "Core", "TagConfig.cs");
            foreach (string sig in new[]
            {
                "public static string GetFamilyAwareProdCode(Element el, string categoryName)",
                "public static string GetFamilyAwareProdCode(Element el, string categoryName, out string source)",
            })
            {
                var m = Regex.Match(tc, Regex.Escape(sig) + @"\s*=>\s*(?<expr>[^;]*);");
                Assert.True(m.Success, "expected an expression-bodied " + sig);
                // The token IS the resolver's code — nothing appended.
                Assert.StartsWith("GetFamilyAwareProdCodeCore(el, categoryName, out ", m.Groups["expr"].Value.Trim());
            }
            Assert.DoesNotContain("MaterialProdOverrideRegistry",
                                  MethodBody(tc, "private static string GetFamilyAwareProdCodeCore("));
        }

        [Fact]
        public void Every_shipped_PROD_code_fits_the_token_shape()
        {
            var bad = new List<string>();
            foreach (string raw in Src("StingTools", "Data", "STING_PROD_CODES.csv").Split('\n').Skip(1))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string code = line.Split(',')[0].Trim();
                if (!Regex.IsMatch(code, "^[A-Z0-9]{2,4}$")) bad.Add(code);
            }
            Assert.True(bad.Count == 0, "PROD codes the validator would reject: " + string.Join(", ", bad));
        }

        [Fact]
        public void Every_ProdResolver_call_in_the_plugin_supplies_the_known_codes()
        {
            // Without knownCodes ProdNameCode.Extract returns null, so a type named
            // PLNS_WBL_Hollow200 never resolved to WBL in Revit although it did in tests.
            string tc = Src("StingTools", "Core", "TagConfig.cs");
            var calls = Regex.Matches(tc, @"ProdResolver\.Resolve\((?<args>[^;]*)\);");
            Assert.True(calls.Count >= 2, "expected the two TagConfig resolver calls");
            foreach (Match m in calls)
                Assert.Contains("GetKnownProdCodes(", m.Groups["args"].Value);
        }

        [Fact]
        public void A_declared_code_is_read_when_the_vocabulary_is_supplied()
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WBL", "WL" };
            var prodMap = new Dictionary<string, string> { { "Walls", "WL" } };
            string code = ProdResolver.Resolve("Basic Wall", "PLNS_WBL_Hollow200", "Walls",
                                               null, null, prodMap, out string source, known);
            Assert.Equal("WBL", code);
            Assert.Equal(ProdResolver.Sources.Declared, source);
        }

        // ── SEQ ────────────────────────────────────────────────────────────

        [Fact]
        public void Unresolved_LOC_numbers_in_its_own_group_not_building_one()
        {
            string unplaced = SeqAssigner.BuildSeqKey("M", "HVAC", "L01", null, "XX", false, includeLoc: true);
            string bld1 = SeqAssigner.BuildSeqKey("M", "HVAC", "L01", null, "BLD1", false, includeLoc: true);
            Assert.Equal("M_XX_HVAC_L01", unplaced);
            Assert.NotEqual(bld1, unplaced);
            Assert.Equal(unplaced, SeqAssigner.BuildSeqKey("M", "HVAC", "L01", null, "", false, includeLoc: true));
        }

        [Fact]
        public void GEN_makes_a_tag_assumed_not_incomplete()
        {
            // GEN is the policy fallback for SYS/FUNC/PROD. As an "incomplete" marker it
            // meant a GEN tag was re-processed on every run and burned a SEQ each time.
            string tc = Src("StingTools", "Core", "TagConfig.cs");
            var m = Regex.Match(tc, @"_unresolvedPlaceholders\s*=\s*new HashSet<string>\s*\{([^}]*)\}");
            Assert.True(m.Success, "_unresolvedPlaceholders not found");
            Assert.DoesNotContain("\"GEN\"", m.Groups[1].Value);
            Assert.Contains("\"XX\"", m.Groups[1].Value);
            Assert.Contains("_unresolvedPlaceholders", MethodBody(tc, "public static bool TagIsComplete("));

            // …while the strict check still treats it as unresolved.
            var strict = Regex.Match(tc, @"\s_placeholders\s*=\s*new HashSet<string>\s*\{([^}]*)\}");
            Assert.True(strict.Success);
            Assert.Contains("\"GEN\"", strict.Groups[1].Value);
        }

        [Fact]
        public void BuildAndWriteTag_has_no_hardcoded_GEN_ahead_of_the_token_policy()
        {
            // SYS/FUNC/PROD each had `: "GEN"` before ResolveToken, so the policy's
            // fallback was unreachable and no substitution was ever recorded.
            string tc = Src("StingTools", "Core", "TagConfig.cs");
            string body = MethodBody(tc, "public static bool BuildAndWriteTag(");
            int policy = body.IndexOf("ResolveToken(\"DISC\"", StringComparison.Ordinal);
            Assert.True(policy > 0, "ResolveToken calls not found");
            string before = body.Substring(0, policy);
            Assert.DoesNotMatch(@"\?\s*\w+\s*:\s*""GEN""", before);
        }

        [Fact]
        public void BuildAndWriteTag_keys_the_counter_on_the_values_it_writes()
        {
            // The key, the collision check and the written tag must share one set of
            // tokens; the old code keyed on derived DISC/LVL and wrote stored ones.
            string tc = Src("StingTools", "Core", "TagConfig.cs");
            string body = MethodBody(tc, "public static bool BuildAndWriteTag(");
            Assert.Contains("ParamRegistry.ReadTokenValues(el)", body);
            Assert.Equal(1, Regex.Matches(body, @"SeqAssigner\.BuildSeqKey\(").Count);
            // A held SEQ is reused, not re-allocated and thrown away.
            Assert.Contains("storedSeq", body);
        }
    }
}
