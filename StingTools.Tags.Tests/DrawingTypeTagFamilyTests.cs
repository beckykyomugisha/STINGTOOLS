// Drawing types must name tag families that exist, keyed the way the rules ask.
//
// WHAT WENT WRONG
//
// AnnotationRunner resolves a tag family as pack.TagFamilies[rule.Category]. Two
// independent mismatches made that lookup miss, and neither was visible on a
// drawing:
//
//   1. 19 family names did not exist — STING_TAG_ROOM, STING - Generic Tag and
//      friends, against a library that names them "STING - Room Tag".
//   2. 6 keys were camelCase (StructuralColumns) while the rules asked in spaced
//      form (Structural Columns).
//
// Either way the runner fell through to "first loaded tag of that category", so
// the sheet was annotated with whichever tag happened to load first. It drew
// something, so it looked like it worked.
//
// These two guards are exact and need no baseline: a name either exists in the
// library or it does not, and a key either matches a rule or is a near-miss of
// one. Orphan keys that match no rule at all are dead config rather than a
// defect, so they are not failed here.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class DrawingTypeTagFamilyTests
    {
        private static DirectoryInfo Repo()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "StingTools", "Data")))
                d = d.Parent;
            Assert.True(d != null, "could not locate StingTools/Data");
            return d;
        }

        private static JObject Catalogue()
        {
            string p = Path.Combine(Repo().FullName, "StingTools", "Data", "STING_DRAWING_TYPES.json");
            Assert.True(File.Exists(p), p);
            return JObject.Parse(File.ReadAllText(p));
        }

        private static HashSet<string> Library()
        {
            string dir = Path.Combine(Repo().FullName, "StingTools", "Data", "TagFamilies");
            Assert.True(Directory.Exists(dir), dir);
            return new HashSet<string>(
                Directory.GetFiles(dir, "*.rfa").Select(Path.GetFileNameWithoutExtension),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Families a drawing type asks for that the library does not yet hold.</summary>
        private static readonly HashSet<string> KnownAbsent = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "STING_TAG_RFL",   // roof lights — no such tag family exists
            "STING_TAG_RWO",   // rainwater outlets — likewise
        };

        private static IEnumerable<(string TypeId, string Key, string Family, List<string> Rules)> Entries()
        {
            foreach (var t in Catalogue()["drawingTypes"] ?? new JArray())
            {
                var ann = t["annotation"] as JObject;
                var fams = ann?["tagFamilies"] as JObject;
                if (fams == null) continue;
                var rules = ((ann["rules"] as JArray) ?? new JArray())
                    .Where(r => (string)r["ruleType"] == "AutoTag" && r["category"] != null)
                    .Select(r => (string)r["category"]).ToList();
                foreach (var p in fams.Properties())
                    yield return ((string)t["id"], p.Name, (string)p.Value, rules);
            }
        }

        [Fact]
        public void EveryNamedTagFamilyExistsInTheLibrary()
        {
            var lib = Library();
            var bad = Entries()
                .Where(e => !lib.Contains(e.Family) && !KnownAbsent.Contains(e.Family))
                .Select(e => $"{e.TypeId}: {e.Key} -> '{e.Family}'")
                .Distinct().OrderBy(x => x).ToList();

            Assert.True(bad.Count == 0,
                "A drawing type names a tag family that is not in StingTools/Data/TagFamilies. "
                + "AnnotationRunner falls back to the first loaded tag of that category, so the "
                + "sheet is annotated with the wrong tag and nothing looks broken.\n"
                + string.Join("\n", bad));
        }

        [Fact]
        public void NoTagFamilyKeyIsANearMissOfARuleCategory()
        {
            string Norm(string s) =>
                Regex.Replace((s ?? "").Replace("OST_", ""), "[^a-zA-Z]", "").ToLowerInvariant().TrimEnd('s');

            var bad = new List<string>();
            foreach (var e in Entries())
            {
                if (e.Rules.Contains(e.Key)) continue;                    // exact match, fine
                var near = e.Rules.FirstOrDefault(r => Norm(r) == Norm(e.Key));
                if (near != null)
                    bad.Add($"{e.TypeId}: key '{e.Key}' should be '{near}'");
            }

            // A key matching NO rule is dead config, not a defect — only a key that
            // differs from a real rule by spelling is, because that one looks
            // connected and is not.
            Assert.True(bad.Count == 0,
                "A tagFamilies key differs only in spelling from an AutoTag rule's category, so "
                + "the lookup misses and the family is never used:\n" + string.Join("\n", bad));
        }

        [Fact]
        public void TheCatalogueWasActuallyRead()
        {
            // A control: both tests above pass trivially over an empty sequence.
            Assert.True(Entries().Count() >= 30, "expected dozens of tagFamilies entries");
            Assert.True(Library().Count >= 100, "expected a populated tag family library");
        }
    }
}
