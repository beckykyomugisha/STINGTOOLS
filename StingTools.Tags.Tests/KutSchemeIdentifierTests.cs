using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT deep review ISO-1 / ISO-3 — the KUT element identifier rendered by the tag scheme
    /// (KUT-originator-volume-level-discipline-number).
    /// </summary>
    public class KutSchemeIdentifierTests
    {
        private static string Repo()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir.FullName;
        }

        private static JObject KutScheme() =>
            (JObject)JObject.Parse(File.ReadAllText(Path.Combine(Repo(), "project-templates", "KUT", "_BIM_COORD", "tag_schemes.json")))
                ["schemes"].First(s => (string)s["id"] == "kut-temple-example");

        [Fact]
        public void AnUnmappedLocationIsNotFiledUnderARealVolume()
        {
            // Old: "fallback": "00" — an element with no building filed under site-wide works.
            var loc = KutScheme()["segments"].First(s => (string)s["token"] == "LOC");
            var realVolumes = ((JObject)loc["map"]).Properties().Select(p => (string)p.Value).ToHashSet();
            string fallback = (string)loc["fallback"];
            Assert.False(realVolumes.Contains(fallback),
                $"LOC fallback '{fallback}' is a real volume code — an element with no building would be filed under it");
        }

        [Fact]
        public void TheKutIdentifierCarriesEveryFieldSeqIsNumberedBy()
        {
            // KUTDR-1. SEQ is unique only within its counter group (SeqAssigner.BuildSeqKey:
            // DISC_LOC_SYS_LVL on KUT, which sets SEQ_INCLUDE_LOC). The identifier had no SYS, so
            // an HVAC 0001 and a CHW 0001 on one level of one building rendered the same string.
            string seq = File.ReadAllText(Path.Combine(Repo(), "StingTools", "Core", "SeqAssigner.cs"));
            Assert.Contains("{disc}_{locPart}_{sys}_{lvl}", seq);

            var cfg = JObject.Parse(File.ReadAllText(Path.Combine(Repo(), "project-templates", "KUT", "_BIM_COORD", "project_config.json")));
            bool includeLoc = (bool?)cfg["SEQ_INCLUDE_LOC"] ?? false;
            bool includeZone = (bool?)cfg["SEQ_INCLUDE_ZONE"] ?? false;
            Assert.True(includeLoc, "KUT numbers SEQ per building; if that changes, revisit this test");

            var tokens = KutScheme()["segments"].Where(s => (string)s["kind"] == "token").Select(s => (string)s["token"]).ToList();
            Assert.Empty(TagSchemeUniqueness.MissingSeqGroupTokens(tokens, includeLoc, includeZone));

            // An empty SYS keys the counter as GEN, so the identifier must print GEN too —
            // a blank segment would be a different string for the same counter group.
            var sys = KutScheme()["segments"].First(s => (string)s["token"] == "SYS");
            Assert.Equal("GEN", (string)sys["fallback"]);
        }

        [Fact]
        public void MissingSeqGroupTokensNamesWhatAnIdentifierDrops()
        {
            var old = new[] { "LOC", "LVL", "DISC", "SEQ" };
            Assert.Equal(new[] { "SYS" }, TagSchemeUniqueness.MissingSeqGroupTokens(old, seqIncludesLoc: true, seqIncludesZone: false));
            Assert.Equal(new[] { "ZONE", "SYS" }, TagSchemeUniqueness.MissingSeqGroupTokens(old, true, true));
            Assert.Equal(new[] { "LOC", "SYS" }, TagSchemeUniqueness.MissingSeqGroupTokens(new[] { "DISC", "LVL", "SEQ" }, true, false));
            // No SEQ: not a per-element identifier, nothing to check.
            Assert.Empty(TagSchemeUniqueness.MissingSeqGroupTokens(new[] { "DISC", "LOC" }, true, true));
            Assert.Empty(TagSchemeUniqueness.MissingSeqGroupTokens(new[] { "disc", "loc", "sys", "lvl", "seq" }, true, false));
        }

        [Fact]
        public void TheAuditStillReportsSharedIdentifiers()
        {
            // An unmapped LOC still falls back to one value (XX), so two buildings outside the
            // map can share an identifier. The audit reports every shared value.
            var dupes = TagSchemeUniqueness.FindDuplicates(new[]
            {
                ("kut-temple-example", "KUT-SMB-01-L01-M-0001", 101L),
                ("kut-temple-example", "KUT-SMB-01-L01-M-0001", 202L),
                ("kut-temple-example", "KUT-SMB-01-L01-M-0002", 303L),
                ("kut-temple-example", "", 404L),
            });
            Assert.Single(dupes);
            Assert.Equal(new long[] { 101, 202 }, dupes.Values.Single());
        }

        [Fact]
        public void TheSchemeAuditChecksUniqueness()
        {
            // The audit compared each element with its own re-render, which cannot see a
            // collision between two elements.
            string src = File.ReadAllText(Path.Combine(Repo(), "StingTools", "Tags", "TagSchemeCommands.cs"));
            Assert.Contains("TagSchemeUniqueness.FindDuplicates", src);
        }

        [Fact]
        public void EveryShippedCorporateSchemeCarriesTheSeqGroup()
        {
            // The corporate examples are what a second project copies. Both left SYS out, so a
            // copied scheme collided the same way the KUT one did (KUTDR-1). Worst case LOC on.
            var lib = JObject.Parse(File.ReadAllText(Path.Combine(Repo(), "StingTools", "Data", "STING_TAG_SCHEMES.json")));
            var schemes = lib["schemes"].ToList();
            Assert.True(schemes.Count >= 2, "the corporate scheme library parsed to almost nothing");
            foreach (var s in schemes)
            {
                var tokens = s["segments"].Where(x => (string)x["kind"] == "token").Select(x => (string)x["token"]).ToList();
                var missing = TagSchemeUniqueness.MissingSeqGroupTokens(tokens, seqIncludesLoc: true, seqIncludesZone: false);
                Assert.True(missing.Count == 0, $"{(string)s["id"]} leaves out {string.Join(", ", missing)}");
            }
        }
    }
}
