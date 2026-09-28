// CODE_LEGEND.json is what the Code Legend dialog shows users as the meaning of every
// tag token. On 2026-09-28 it listed six SYS codes that do not exist (MED, LTG, DRN,
// SPR, ELC, PLB), RET where the tagger writes RTN, product codes the resolver never
// produces (WC, WHB, CHR, BLR …), LPS as a discipline, and described HWS as domestic
// hot water. These tests hold its token sections to the runtime vocabularies, read
// from source the same way FuncVocabularyTests does (the plugin half is Revit-bound).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class CodeLegendAlignmentTests
    {
        private static string Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Core"))) dir = dir.Parent;
            Assert.True(dir != null, "could not locate StingTools/Core");
            return dir.FullName;
        }

        private static string Src(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

        private static string Between(string text, string from, string to)
        {
            int a = text.IndexOf(from, StringComparison.Ordinal);
            int b = text.IndexOf(to, a + 1, StringComparison.Ordinal);
            Assert.True(a >= 0 && b > a, $"block {from} … {to} not found");
            return text.Substring(a, b - a);
        }

        private static List<string> Legend(string sectionTitle)
        {
            var root = JObject.Parse(Src("StingTools", "Data", "CODE_LEGEND.json"));
            var sec = root["sections"].FirstOrDefault(s => (string)s["section"] == sectionTitle);
            Assert.True(sec != null, "legend section missing: " + sectionTitle);
            return sec["entries"].Select(e => (string)e["code"]).ToList();
        }

        private static HashSet<string> RuntimeSys()
        {
            string block = Between(Src("StingTools", "Core", "TagConfig.Defaults.cs"), "DefaultSysMap()", "DefaultProdMap()");
            return new HashSet<string>(Regex.Matches(block, @"\{\s*""([A-Z]+)"",\s*new List<string>").Select(m => m.Groups[1].Value));
        }

        private static HashSet<string> RuntimeFunc()
        {
            string defaults = Between(Src("StingTools", "Core", "TagConfig.Defaults.cs"), "DefaultFuncMap()", "DefaultLocCodes()");
            var set = new HashSet<string>(Regex.Matches(defaults, @"\{\s*""[A-Z]+"",\s*""([A-Z0-9]+)""\s*\}").Select(m => m.Groups[1].Value));
            string subs = Between(Src("StingTools", "Core", "ISO19650Validator.cs"), "SubFunctionCodes = new", "};");
            foreach (Match m in Regex.Matches(subs, @"""([A-Z0-9]+)""")) set.Add(m.Groups[1].Value);
            return set;
        }

        private static HashSet<string> RuntimeDisc()
        {
            string block = Between(Src("StingTools", "Core", "ISO19650Validator.cs"), "_builtInDiscCodes = new", "};");
            return new HashSet<string>(Regex.Matches(block, @"""([A-Z]+)""").Select(m => m.Groups[1].Value));
        }

        private static HashSet<string> RuntimeProd()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            string map = Between(Src("StingTools", "Core", "TagConfig.Defaults.cs"), "DefaultProdMap()", "DefaultFuncMap()");
            foreach (Match m in Regex.Matches(map, @"\{\s*""[^""]+"",\s*""([A-Z0-9]+)""\s*\}")) set.Add(m.Groups[1].Value);
            foreach (string line in File.ReadAllLines(Path.Combine(Root(), "StingTools", "Data", "STING_PROD_CODES.csv")).Skip(1))
            {
                if (line.StartsWith("#") || line.Trim().Length == 0) continue;
                set.Add(line.Split(',')[0].Trim());
            }
            return set;
        }

        [Fact]
        public void The_runtime_vocabularies_were_read()
        {
            Assert.True(RuntimeSys().Count >= 18, "SysMap");
            Assert.True(RuntimeFunc().Count >= 25, "FUNC");
            Assert.Equal(12, RuntimeDisc().Count);
            Assert.True(RuntimeProd().Count > 300, "PROD");
        }

        [Fact]
        public void Legend_system_codes_are_the_runtime_systems()
            => Assert.Equal(RuntimeSys().OrderBy(x => x), Legend("System Codes (tag SYS token)").OrderBy(x => x));

        [Fact]
        public void Legend_function_codes_are_the_runtime_functions()
            => Assert.Equal(RuntimeFunc().OrderBy(x => x), Legend("Function Codes (tag FUNC token)").OrderBy(x => x));

        [Fact]
        public void Legend_discipline_codes_are_the_validator_disciplines()
            => Assert.Equal(RuntimeDisc().OrderBy(x => x), Legend("Discipline Codes (tag DISC token)").OrderBy(x => x));

        [Fact]
        public void Legend_product_codes_are_codes_the_resolver_produces()
        {
            var prod = RuntimeProd();
            var bad = Legend("Product Codes (PROD — MEP Equipment)").Where(c => !prod.Contains(c)).ToList();
            Assert.True(bad.Count == 0, "Legend PROD codes the resolver never writes: " + string.Join(", ", bad));
        }

        [Fact]
        public void Legend_tag_examples_are_valid_tags()
        {
            var sys = RuntimeSys(); var func = RuntimeFunc(); var disc = RuntimeDisc(); var prod = RuntimeProd();
            var examples = Legend("Tag Anatomy Example");
            Assert.NotEmpty(examples);
            foreach (string tag in examples)
            {
                var p = tag.Split('-');
                Assert.True(p.Length == 8, tag + " is not 8 segments");
                Assert.Contains(p[0], disc);
                Assert.Contains(p[4], sys);
                Assert.Contains(p[5], func);
                Assert.Contains(p[6], prod);
                Assert.Null(SeqAssigner.ValidateNumericSeq(p[7], 4));
            }
        }

        // The FUNC/PROD contradiction table named WC, WHB, CHR, BLR, DAM … — codes the
        // resolver never writes — so its checks could not fire.
        [Fact]
        public void Contradiction_table_names_codes_the_resolver_produces()
        {
            string block = Between(Src("StingTools", "Core", "ISO19650Validator.cs"), "_incompatibleFuncProdPairs =", "private static string ValidateFuncProdPair");
            var prod = RuntimeProd();
            var named = Regex.Matches(block, @"\{\s*""[A-Z]+"",\s*new HashSet<string>\([^)]*\)\s*\{([^}]*)\}")
                .SelectMany(m => Regex.Matches(m.Groups[1].Value, @"""([A-Z0-9]+)""").Select(x => x.Groups[1].Value)).ToList();
            Assert.True(named.Count > 20, "table not read");
            var bad = named.Where(c => !prod.Contains(c)).Distinct().ToList();
            Assert.True(bad.Count == 0, "Never produced: " + string.Join(", ", bad));
        }
    }
}
