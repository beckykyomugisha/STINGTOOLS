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
    /// <summary>
    /// The KUT owner-standards rule <c>discipline-code-valid</c> must accept every
    /// discipline code the tagging can write on a non-healthcare project. Before this,
    /// the rule was checked only against the naming source it was derived from — a
    /// circular check — and it rejected FP, LV and G on every sprinkler, fire alarm
    /// device, communications device and item of specialty equipment.
    ///
    /// The tagger's codes come from two places, read here from the shipped source
    /// rather than restated: the category map (<c>DefaultDiscMap</c>, parsed from
    /// TagConfig.Defaults.cs the way CategoryTokenAuditTests does) and the pipe
    /// correction by system (<c>CategoryTokenDefaults.SystemAwareDisc</c>, called).
    /// </summary>
    public class KutElementDisciplineTests
    {
        private static string Repo()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Core")))
                dir = dir.Parent;
            Assert.True(dir != null, "could not locate StingTools/Core");
            return dir.FullName;
        }

        private static Dictionary<string, string> DefaultDiscMap()
        {
            string src = File.ReadAllText(Path.Combine(Repo(), "StingTools", "Core", "TagConfig.Defaults.cs"));
            int a = src.IndexOf("DefaultDiscMap()", StringComparison.Ordinal);
            int b = src.IndexOf("DefaultSysMap()", a, StringComparison.Ordinal);
            Assert.True(a >= 0 && b > a, "DefaultDiscMap() block not found");
            string block = Regex.Replace(src.Substring(a, b - a), @"//[^\n]*", "");
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(block, @"\{\s*""([^""]+)""\s*,\s*""([^""]+)""\s*\}"))
                map[m.Groups[1].Value] = m.Groups[2].Value;
            // The instrument must find a real map before its silence means anything.
            Assert.True(map.Count > 50, "parsed only " + map.Count + " DefaultDiscMap entries");
            Assert.Equal("FP", map["Sprinklers"]);
            return map;
        }

        private static HashSet<string> KutRuleValues()
        {
            string path = Path.Combine(Repo(), "project-templates", "KUT", "_BIM_COORD", "owner_standards.json");
            var values = JObject.Parse(File.ReadAllText(path))["rules"]
                .First(r => (string)r["id"] == "discipline-code-valid")["values"];
            return new HashSet<string>(values.Select(v => (string)v), StringComparer.Ordinal);
        }

        /// <summary>Codes only a healthcare-profile project writes. KUT is not one; the
        /// corporate baseline rule carries them for projects that are.</summary>
        private static readonly HashSet<string> HealthcareOnly = new(StringComparer.Ordinal) { "H", "MG", "RP" };

        [Fact]
        public void TheKutRuleAcceptsEveryCodeTheCategoryMapWrites()
        {
            var allowed = KutRuleValues();
            var rejected = DefaultDiscMap()
                .Where(kv => !HealthcareOnly.Contains(kv.Value) && !allowed.Contains(kv.Value))
                .Select(kv => kv.Key + " -> " + kv.Value)
                .ToList();
            Assert.True(rejected.Count == 0,
                "the KUT discipline rule rejects what the tagger writes on these categories:\n" +
                string.Join("\n", rejected));
        }

        [Fact]
        public void TheKutRuleAcceptsEveryCodeThePipeSystemCorrectionWrites()
        {
            var allowed = KutRuleValues();
            // Every SYS the correction names, plus one it does not (falls through).
            string[] systems = { "DCW", "DHW", "SAN", "RWD", "GAS", "FP", "HVAC", "HWS", "CHW", "CDW", "REF", "ZZZ" };
            foreach (var cat in CategoryTokenDefaults.PipeCategories)
                foreach (var sys in systems)
                {
                    string disc = CategoryTokenDefaults.SystemAwareDisc("M", sys, cat);
                    Assert.True(allowed.Contains(disc),
                        $"{cat} on system {sys} is tagged {disc}, which the KUT rule rejects");
                }
        }

        /// <summary>The rule is not a dumping ground: every code it allows is one the
        /// tagger can produce, so a typo or a stale role code cannot hide in it.</summary>
        [Fact]
        public void TheKutRuleAllowsNothingTheTaggerCannotWrite()
        {
            var writable = new HashSet<string>(DefaultDiscMap().Values, StringComparer.Ordinal);
            foreach (var cat in CategoryTokenDefaults.PipeCategories)
                foreach (var sys in new[] { "DCW", "FP", "HVAC" })
                    writable.Add(CategoryTokenDefaults.SystemAwareDisc("M", sys, cat));
            var unreachable = KutRuleValues().Where(v => !writable.Contains(v)).ToList();
            Assert.True(unreachable.Count == 0,
                "the KUT rule allows codes the tagger never writes: " + string.Join(", ", unreachable));
        }
    }
}
