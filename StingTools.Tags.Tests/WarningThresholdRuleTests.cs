// DSCH-47. A warning threshold's comparison direction used to be read out of
// its description - "minimum" / "min " meant below, anything else above - and the
// same description was meant to be the parameter's tooltip. A re-worded tooltip
// could invert a check with nothing failing. The direction and the printed text
// are now fields of their own ("direction", "message"); these tests hold the
// migration to "behaviour unchanged" and the evaluator to the fields.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class WarningThresholdRuleTests
    {
        private static DirectoryInfo RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            return dir;
        }

        private static JObject Registry() => JObject.Parse(File.ReadAllText(
            Path.Combine(RepoRoot().FullName, "StingTools", "Data", "PARAMETER_REGISTRY.json")));

        private static List<JObject> Warnings() =>
            ((JArray)Registry()["warning_thresholds"]).OfType<JObject>().ToList();

        /// <summary>
        /// Entries whose direction was changed ON PURPOSE after the migration, so it
        /// may no longer match what the old wording implied. The migration itself
        /// changed none. Adding a name here is the deliberate act of changing a check
        /// - say why beside it.
        /// </summary>
        private static readonly Dictionary<string, string> DeliberateDepartures =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // A minimum bar diameter; the old case-sensitive match missed "Minimum".
                ["WARN_STR_REBAR_MIN_DIA"] = "min",
                // Rw of a door is a minimum; message and direction had been copied from
                // the duct sound-level limit and are now its own.
                ["WARN_BLE_DOOR_ACOUSTIC"] = "min",
                // An access clearance is a minimum space.
                ["WARN_PLM_FIXTURE_ACCESS"] = "min",
                // A provision count is a minimum.
                ["WARN_RGL_ACCESSIBLE_WC"] = "min",
                // The value is the board's rated short-circuit capacity, which must be
                // at least the fault current (BS 7671 Reg 434.5.1).
                ["WARN_ELC_PNL_SHORT_CIRCUIT"] = "min",
            };

        [Fact]
        public void EveryWarningCarriesADirectionAndAMessage()
        {
            var bad = Warnings()
                .Where(w => !WarningThresholdRule.TryParseDirection((string)w["direction"], out _)
                            || string.IsNullOrWhiteSpace((string)w["message"]))
                .Select(w => (string)w["param_name"]).ToList();
            Assert.True(bad.Count == 0,
                "warning_thresholds entries with no valid direction (min/max) or message - the plugin " +
                "refuses them: " + string.Join(", ", bad));
        }

        [Fact]
        public void EveryWarningEvaluatesInTheSameDirectionAsBeforeTheMigration()
        {
            // The message is the old description, verbatim, so the old reading of it
            // is still computable. 483 entries, 44 min / 439 max at migration.
            var warnings = Warnings();
            Assert.True(warnings.Count > 400, $"only {warnings.Count} warning_thresholds entries read");

            var changed = new List<string>();
            foreach (var w in warnings)
            {
                string name = (string)w["param_name"];
                if (DeliberateDepartures.TryGetValue(name, out var meant))
                {
                    Assert.True((string)w["direction"] == meant, $"{name}: direction is {(string)w["direction"]}, decided {meant}");
                    continue;
                }
                Assert.True(WarningThresholdRule.TryParseDirection((string)w["direction"], out var now), name);
                var before = WarningThresholdRule.LegacyDirectionFromText((string)w["message"]);
                if (now != before) changed.Add($"{name}: was {before}, now {now}");
            }
            Assert.True(changed.Count == 0,
                "direction differs from what the old wording implied (list it in DeliberateDepartures " +
                "if intended): " + string.Join("; ", changed));
        }

        [Fact]
        public void TheEvaluatorFollowsTheFieldNotTheWording()
        {
            // Message says "limit"; direction says min. The old code would have warned
            // ABOVE; the field says BELOW.
            Assert.Null(WarningThresholdRule.Evaluate(WarningDirection.Min, "HIGH",
                "Duct velocity limit", "9", "6", "m/s"));
            Assert.Equal("[!HIGH: Duct velocity limit — 3 m/s < 6 m/s]",
                WarningThresholdRule.Evaluate(WarningDirection.Min, "HIGH",
                    "Duct velocity limit", "3", "6", "m/s"));

            // Message says "minimum"; direction says max.
            Assert.Null(WarningThresholdRule.Evaluate(WarningDirection.Max, "LOW",
                "Ceiling height minimum", "2000", "2400", "mm"));
            Assert.Equal("[!LOW: Ceiling height minimum — 2600 mm exceeds 2400 mm]",
                WarningThresholdRule.Evaluate(WarningDirection.Max, "LOW",
                    "Ceiling height minimum", "2600", "2400", "mm"));
        }

        [Theory]
        [InlineData("3", "12", "25")]
        [InlineData("1", "8", "12.5")]
        [InlineData("0", "12", "0")]
        [InlineData("3", "0", null)]
        [InlineData("", "12", null)]
        [InlineData("3", "n/a", null)]
        public void SpareWaysAreAPercentageOfTheBoard(string part, string whole, string expected)
            => Assert.Equal(expected, WarningThresholdRule.PercentOf(part, whole));

        [Fact]
        public void EveryWarningDataBranchReturnsSomething()
        {
            // A branch of GetWarningDataValue that lost its return (ecf1e8f21) made the
            // next if its body, and two warnings - spare ways and pipe gradient - went
            // silent for six months with nothing failing. Every `if (wp.Contains(...))`
            // must be followed by a return or a block, never by another if.
            string src = File.ReadAllText(Path.Combine(RepoRoot().FullName,
                "StingTools", "Core", "TagConfig.Tag7.cs"));
            int start = src.IndexOf("private static string GetWarningDataValue(", StringComparison.Ordinal);
            Assert.True(start > 0, "GetWarningDataValue not found");
            int end = src.IndexOf("// Generic fallback", start, StringComparison.Ordinal);
            var lines = src.Substring(start, end - start).Split('\n')
                .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("//")).ToList();
            var bad = new List<string>();
            for (int i = 0; i < lines.Count - 1; i++)
                if (lines[i].StartsWith("if (wp.Contains(") && !lines[i].EndsWith(";")
                    && !(lines[i + 1].StartsWith("return") || lines[i + 1] == "{"))
                    bad.Add(lines[i]);
            Assert.True(bad.Count == 0, "branches without a return: " + string.Join(" | ", bad));
        }

        [Fact]
        public void AValueOnTheThresholdIsNotAWarning()
        {
            Assert.Null(WarningThresholdRule.Evaluate(WarningDirection.Min, "M", "m", "20", "20", "%"));
            Assert.Null(WarningThresholdRule.Evaluate(WarningDirection.Max, "M", "m", "20", "20", "%"));
        }

        [Fact]
        public void NonNumericTextIsNotCompared()
        {
            Assert.Null(WarningThresholdRule.Evaluate(WarningDirection.Max, "M", "m", "n/a", "20", "%"));
            Assert.Null(WarningThresholdRule.Evaluate(WarningDirection.Max, "M", "m", "30", "", "%"));
        }

        [Theory]
        [InlineData("min", true, WarningDirection.Min)]
        [InlineData(" MAX ", true, WarningDirection.Max)]
        [InlineData("minimum", false, WarningDirection.Max)]
        [InlineData("", false, WarningDirection.Max)]
        [InlineData(null, false, WarningDirection.Max)]
        public void OnlyMinAndMaxAreDirections(string text, bool ok, WarningDirection expected)
        {
            Assert.Equal(ok, WarningThresholdRule.TryParseDirection(text, out var d));
            if (ok) Assert.Equal(expected, d);
        }

        [Fact]
        public void DeprecatedEntriesAreFlaggedByFieldAndNameTheirReplacement()
        {
            var root = Registry();
            var all = root.Descendants().OfType<JObject>()
                .Where(o => o["param_name"] != null && o["deprecated"] != null).ToList();

            Assert.Contains(all, o => (string)o["param_name"] == "ASS_INSTALL_DATE_TXT");
            Assert.Contains(all, o => (string)o["param_name"] == "ASS_INST_DATE_TXT");
            // Same meaning as CST_INSTALL_HRS, which every reader and writer uses.
            Assert.Contains(all, o => (string)o["param_name"] == "CST_LABOUR_HOURS"
                                      && (string)o["replaced_by"] == "CST_INSTALL_HRS");

            var names = new HashSet<string>(root.Descendants().OfType<JObject>()
                .Select(o => (string)o["param_name"]).Where(n => n != null), StringComparer.Ordinal);
            foreach (var o in all)
            {
                string name = (string)o["param_name"];
                Assert.True(o["deprecated"].Type == JTokenType.Boolean, $"{name}: deprecated must be true/false");
                string by = (string)o["replaced_by"];
                Assert.False(string.IsNullOrWhiteSpace(by), $"{name}: deprecated with no replaced_by");
                Assert.Contains(by, names);
                Assert.DoesNotContain(all, x => (string)x["param_name"] == by && (bool)x["deprecated"]);
            }
        }
    }
}
