using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using StingTools.Core.Placement;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// MDP-1. Five STING_PLACEMENT_RULES.*.json packs shipped for months without
    /// ever loading, because registration was a hand-kept array nobody checked
    /// against the folder. Every pack on disk must now be either merged into the
    /// placement run or listed in PlacementPackRegistry.NotAutoMerged with a reason.
    /// </summary>
    public class PlacementPackRegistryTests
    {
        private static string PlacementDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "Placement")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data/Placement");
            return Path.Combine(dir.FullName, "StingTools", "Data", "Placement");
        }

        private static List<string> ShippedPacks() =>
            Directory.GetFiles(PlacementDir(), "STING_PLACEMENT_RULES*.json")
                .Select(Path.GetFileName)
                .Where(f => !string.Equals(f, PlacementPackRegistry.BaselineFileName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

        [Fact]
        public void EveryShippedPackIsRegisteredOrExplained()
        {
            var packs = ShippedPacks();
            Assert.True(packs.Count >= 16, $"expected >=16 packs in Data/Placement, found {packs.Count}");

            var forgotten = packs
                .Where(f => !PlacementPackRegistry.IsAutoMerged(f)
                            && !PlacementPackRegistry.NotAutoMerged.ContainsKey(f))
                .ToList();
            Assert.True(forgotten.Count == 0,
                "Placement pack(s) neither registered in PlacementPackRegistry.DisciplinePacks nor listed in "
              + "NotAutoMerged with a reason — they would never load: " + string.Join(", ", forgotten));
        }

        [Fact]
        public void EveryRegistryEntryNamesAFileThatShips()
        {
            var onDisk = new HashSet<string>(ShippedPacks(), StringComparer.OrdinalIgnoreCase);
            var named = PlacementPackRegistry.DisciplinePacks.Select(p => p.FileName)
                .Concat(PlacementPackRegistry.NotAutoMerged.Keys).ToList();
            var missing = named.Where(n => !onDisk.Contains(n)).ToList();
            Assert.True(missing.Count == 0, "Registry names file(s) not in Data/Placement: " + string.Join(", ", missing));
        }

        [Fact]
        public void APackIsEitherMergedOrNot_NeverBoth_AndNeverTwice()
        {
            var merged = PlacementPackRegistry.DisciplinePacks.Select(p => p.FileName).ToList();
            Assert.Equal(merged.Count, merged.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            var both = merged.Where(f => PlacementPackRegistry.NotAutoMerged.ContainsKey(f)).ToList();
            Assert.True(both.Count == 0, "Both merged and excluded: " + string.Join(", ", both));
            Assert.All(PlacementPackRegistry.NotAutoMerged, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value)));
            Assert.All(PlacementPackRegistry.DisciplinePacks, p => Assert.False(string.IsNullOrWhiteSpace(p.PackTag)));
        }

        // ── Medical gases: registered, so its scoping must hold ─────────────

        private static List<(string Id, Regex Include, Regex Exclude)> MedGasRules()
        {
            var doc = JObject.Parse(File.ReadAllText(Path.Combine(PlacementDir(), "STING_PLACEMENT_RULES.medical-gases.json")));
            var list = new List<(string, Regex, Regex)>();
            foreach (var r in (JArray)doc["Rules"])
            {
                string inc = (string)r["RoomFilter"];
                string exc = (string)r["ExcludeRoomFilter"];
                // The engine compiles both with IgnoreCase (FixturePlacementEngine).
                list.Add(((string)r["RuleId"],
                          string.IsNullOrEmpty(inc) ? null : new Regex(inc, RegexOptions.IgnoreCase),
                          string.IsNullOrEmpty(exc) ? null : new Regex(exc, RegexOptions.IgnoreCase)));
            }
            return list;
        }

        private static bool Fires((string Id, Regex Include, Regex Exclude) rule, string room)
            => (rule.Include == null || rule.Include.IsMatch(room))
               && (rule.Exclude == null || !rule.Exclude.IsMatch(room));

        [Fact]
        public void MedGasRules_AllCarryARoomFilter()
        {
            var rules = MedGasRules();
            Assert.NotEmpty(rules);
            Assert.All(rules, r => Assert.True(r.Include != null, $"{r.Id} has no RoomFilter — it would fire in every room"));
        }

        [Theory]
        [InlineData("Bedroom 1")]
        [InlineData("Master Bedroom")]
        [InlineData("Guest Bedroom")]
        [InlineData("Lecture Theatre")]
        [InlineData("Home Theatre")]
        [InlineData("Corridor")]
        [InlineData("Main Corridor")]
        [InlineData("Non-clinical Store")]
        [InlineData("Heat Recovery Plant")]
        [InlineData("Vehicular Access")]
        [InlineData("Office")]
        [InlineData("Kitchen")]
        [InlineData("Living Room")]
        [InlineData("Patient WC")]
        public void MedGasRules_NeverFireInNonClinicalRooms(string room)
        {
            var hits = MedGasRules().Where(r => Fires(r, room)).Select(r => r.Id).ToList();
            Assert.True(hits.Count == 0, $"'{room}' would receive med-gas rule(s): {string.Join(", ", hits)}");
        }

        [Theory]
        [InlineData("Ward 3 Bay 2", "htmgas-oxygen-bedside")]
        [InlineData("Patient Room 12", "htmgas-oxygen-bedside")]
        [InlineData("4-Bed Bay", "htmgas-vacuum-bedside")]
        [InlineData("Operating Theatre 1", "htmgas-nitrous-theatre")]
        [InlineData("Anaesthetic Room", "htmgas-anaesthetic-scavenging")]
        [InlineData("ICU Bed 4", "htmgas-oxygen-icu-4outlet")]
        [InlineData("Critical Care", "htmgas-ceiling-pendant-icu")]
        [InlineData("Ward Corridor", "htmgas-emergency-oxygen-corridor")]
        [InlineData("Recovery", "htmgas-recovery-pendant")]
        [InlineData("Endoscopy Suite", "htmgas-co2-endoscopy")]
        public void MedGasRules_StillFireInClinicalRooms(string room, string ruleId)
        {
            var rule = MedGasRules().Single(r => r.Id == ruleId);
            Assert.True(Fires(rule, room), $"{ruleId} no longer fires in '{room}'");
        }
    }
}
