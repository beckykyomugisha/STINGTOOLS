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
        public void TwoSystemsOnOneLevelCanShareAnIdentifierAndTheAuditMustSayIt()
        {
            // The KUT identifier carries no SYS segment, while SEQ is numbered per SYS
            // (SeqAssigner.BuildSeqKey: DISC_LOC_SYS_LVL). Both facts are pinned here, so the day
            // either changes this test says the duplicate check may no longer be needed.
            var tokens = KutScheme()["segments"].Where(s => (string)s["kind"] == "token").Select(s => (string)s["token"]).ToList();
            Assert.DoesNotContain("SYS", tokens);
            string seq = File.ReadAllText(Path.Combine(Repo(), "StingTools", "Core", "SeqAssigner.cs"));
            Assert.Contains("{disc}_{locPart}_{sys}_{lvl}", seq);

            // HVAC 0001 and CHW 0001 on L01 of BLD1 render the same KUT identifier.
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
    }
}
