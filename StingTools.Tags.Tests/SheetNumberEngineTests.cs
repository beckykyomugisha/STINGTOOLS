using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// The one sheet-number engine that production, renumbering and the
    /// fabrication composer share. Each group below exists for a defect that
    /// reached — or would have reached — an issued sheet number:
    ///
    /// <list type="bullet">
    /// <item>P-3: renumbering erased the level ("A-RCP-L02-001" → "A-RCP-001").</item>
    /// <item>P-4: renumbering left sheets on "ZZ_STING_RENUM_…" and set the
    /// counter below a locked sheet's number.</item>
    /// <item>P-11: a silent, random-suffix uniquifier gave a re-run a different number.</item>
    /// <item>D-2 (ISO): 29 architectural types resolve to the same ISO fields, and a
    /// per-type counter handed each of them 0001.</item>
    /// <item>The "last digit run is the sequence" heuristic read the REVISION on an
    /// ISO number.</item>
    /// </list>
    /// </summary>
    public class SheetNumberEngineTests
    {
        // ── Substitution ────────────────────────────────────────────────

        [Fact]
        public void Producer_substitution_keeps_level_and_width()
            => Assert.Equal("A-RCP-L02-005",
                SheetNumberEngine.ApplyTokenPattern("A-RCP-{lvl}-{seq:D3}", "A", "L02", "", "", "", "RCP", 5, null));

        [Fact]
        public void Empty_segment_is_a_visible_XX_not_a_dropped_one()
            => Assert.Equal("A-RCP-XX-005",
                SheetNumberEngine.ApplyTokenPattern("A-RCP-{lvl}-{seq:D3}", "A", "", "", "", "", "RCP", 5, null));

        [Fact]
        public void Template_leaves_only_the_sequence_open()
        {
            var t = SheetNumberEngine.Template("A-RCP-{lvl}-{seq:D3}", "A", "L02", "", "", "", "RCP", null);
            Assert.Equal("A-RCP-L02-#", SheetNumberEngine.Mask(t));
        }

        [Theory]
        [InlineData("A-{lvl}")]                 // no sequence
        [InlineData("A-{seq}-{seq:D3}")]        // two
        public void Template_is_null_without_exactly_one_sequence(string pattern)
            => Assert.Null(SheetNumberEngine.Template(pattern, "A", "L01", "", "", "", "", null));

        // ── Reading the sequence back ───────────────────────────────────

        [Fact]
        public void Iso_number_yields_the_sequence_not_the_revision()
        {
            // The old DrawingTokenContext heuristic read the last digit run: 1.
            Assert.Equal(3, SheetNumberEngine.ExtractTrailingSequence("KUT-PLN-01-L01-DR-A-0003-S2-P01"));
        }

        [Theory]
        [InlineData("A-RCP-L02-007", 7)]
        [InlineData("A-RCP-L02-007-A", 7)]      // uniquifier suffix tolerated
        [InlineData("a-rcp-l02-007", 7)]        // case-insensitive
        public void Template_extraction_reads_by_shape(string number, int expected)
        {
            var t = SheetNumberEngine.Template("A-RCP-{lvl}-{seq:D3}", "A", "L02", "", "", "", "", null);
            Assert.Equal(expected, SheetNumberEngine.ExtractSequence(number, t));
        }

        [Fact]
        public void A_number_from_another_level_is_not_ours()
        {
            var t = SheetNumberEngine.Template("A-RCP-{lvl}-{seq:D3}", "A", "L02", "", "", "", "", null);
            Assert.Null(SheetNumberEngine.ExtractSequence("A-RCP-L03-007", t));
        }

        // ── Uniqueness ─────────────────────────────────────────────────

        [Fact]
        public void Free_number_is_kept_and_reserved_silently()
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Assert.Equal("A-001", SheetNumberEngine.MakeUnique("A-001", taken, out var note));
            Assert.Null(note);
            Assert.Contains("A-001", taken);
        }

        [Fact]
        public void Collision_is_deterministic_and_always_reported()
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A-001" };
            foreach (var c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ") taken.Add("A-001-" + c);
            var chosen = SheetNumberEngine.MakeUnique("a-001", taken, out var note);
            Assert.Equal("a-001-AA", chosen);   // never a random suffix
            Assert.NotNull(note);
        }

        // ── Counter buckets ────────────────────────────────────────────

        [Fact]
        public void Profile_bucket_is_byte_identical_to_the_historical_key()
            => Assert.Equal("arch-plan|PKG|A|01",
                SheetNumberEngine.CounterBucket(SheetNumberPolicyKind.Profile, "anything", "arch-plan", "PKG", "A", "01"));

        /// <summary>
        /// The catalogue gate. Under EITHER policy, two drawing types produced on
        /// the same level must never be able to mint the same number while drawing
        /// from different counters — that is a duplicate sheet number by
        /// construction, rescued only by a "-A" suffix.
        /// </summary>
        [Theory]
        [InlineData(SheetNumberPolicyKind.Profile)]
        [InlineData(SheetNumberPolicyKind.Iso)]
        public void No_two_drawing_types_can_mint_the_same_number_from_different_counters(SheetNumberPolicyKind policy)
        {
            var clashes = Clashes(LoadCatalogue(), policy);
            Assert.True(clashes.Count == 0,
                $"{policy}: {clashes.Count} template(s) shared by types on different counters:\n" +
                string.Join("\n", clashes.Take(15)));
        }

        [Fact]
        public void The_gate_catches_the_iso_collision_when_buckets_are_per_type()
        {
            // RED control: key ISO sheets the old way (per drawing type) and the
            // architectural profiles collide, proving the gate above can fail.
            var clashes = Clashes(LoadCatalogue(), SheetNumberPolicyKind.Iso, forcePerTypeBuckets: true);
            Assert.NotEmpty(clashes);
        }

        private static List<string> Clashes(List<DrawingType> types, SheetNumberPolicyKind policy,
            bool forcePerTypeBuckets = false)
        {
            var byTemplate = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var owners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var dt in types)
            {
                var pattern = SheetNumberPolicy.ResolvePattern(dt, policy);
                var t = SheetNumberEngine.Template(pattern, dt.Discipline, "L01", dt.System, "", "", dt.Purpose, Extras(dt));
                if (t == null) continue;
                var bucket = forcePerTypeBuckets
                    ? SheetNumberEngine.CounterBucket(SheetNumberPolicyKind.Profile, t, dt.Id, "", dt.Discipline, dt.IsoNaming?.Volume)
                    : SheetNumberEngine.CounterBucket(policy, t, dt.Id, "", dt.Discipline, dt.IsoNaming?.Volume);
                var key = SheetNumberEngine.Mask(t);
                if (!byTemplate.TryGetValue(key, out var set)) byTemplate[key] = set = new HashSet<string>();
                set.Add(bucket);
                if (!owners.TryGetValue(key, out var l)) owners[key] = l = new List<string>();
                l.Add(dt.Id);
            }
            return byTemplate.Where(kv => kv.Value.Count > 1)
                .Select(kv => $"  {kv.Key}  <=  {string.Join(", ", owners[kv.Key])}").ToList();
        }

        /// <summary>The ISO fields DrawingTokenContext.Build supplies, with a
        /// fixed project/originator — the part of the number every type shares.</summary>
        private static Dictionary<string, string> Extras(DrawingType dt) => new Dictionary<string, string>
        {
            ["project"] = "PRJ", ["originator"] = "ORG",
            ["vol"] = dt.IsoNaming?.Volume ?? "", ["type"] = dt.IsoNaming?.Type ?? "",
            ["role"] = dt.IsoNaming?.Role ?? dt.Discipline ?? "", ["suit"] = dt.IsoNaming?.Suitability ?? "",
            ["rev"] = dt.IsoNaming?.Revision ?? "", ["phase"] = dt.Phase ?? "",
        };

        private static List<DrawingType> LoadCatalogue()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_DRAWING_TYPES.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            var lib = JsonConvert.DeserializeObject<DrawingTypeLibrary>(
                File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "Data", "STING_DRAWING_TYPES.json")));
            Assert.True(lib.DrawingTypes.Count >= 90, "catalogue did not load");
            return lib.DrawingTypes;
        }

        // ── Renumbering ────────────────────────────────────────────────

        private static SheetNumberEngine.RenumberItem Sheet(string id, string level, int seq, bool locked = false,
            string bucket = "b")
            => new SheetNumberEngine.RenumberItem
            {
                Id = id,
                Bucket = bucket,
                CurrentSeq = seq,
                Locked = locked,
                CurrentNumber = $"A-RCP-{level}-{seq:D3}",
                NumberFor = s => $"A-RCP-{level}-{s:D3}",
            };

        [Fact]
        public void Renumber_keeps_each_sheets_level()
        {
            var items = new[] { Sheet("1", "L01", 1), Sheet("2", "L02", 4) };
            var plan = SheetNumberEngine.PlanRenumber(items, items.Select(i => i.CurrentNumber));
            var mv = Assert.Single(plan.Moves);
            Assert.Equal("A-RCP-L02-004", mv.From);
            Assert.Equal("A-RCP-L02-002", mv.To);   // level kept, gap closed
        }

        [Fact]
        public void Locked_sequence_is_skipped_and_counts_toward_the_high_water_mark()
        {
            var items = new[] { Sheet("1", "L01", 3), Sheet("2", "L01", 7, locked: true), Sheet("3", "L01", 9) };
            var plan = SheetNumberEngine.PlanRenumber(items, items.Select(i => i.CurrentNumber));
            Assert.DoesNotContain(plan.Moves, m => m.Id == "2");
            Assert.Equal(new[] { 1, 2 }, plan.Moves.OrderBy(m => m.Seq).Select(m => m.Seq));
            // Old behaviour set the counter to the group count (3) — below the locked 7.
            Assert.Equal(7, plan.HighWater["b"]);
        }

        [Fact]
        public void Locked_low_sequence_is_stepped_round()
        {
            var items = new[] { Sheet("1", "L01", 1, locked: true), Sheet("2", "L01", 5) };
            var plan = SheetNumberEngine.PlanRenumber(items, items.Select(i => i.CurrentNumber));
            Assert.Equal(2, Assert.Single(plan.Moves).Seq);
        }

        [Fact]
        public void Target_held_outside_the_plan_pins_the_mover_and_is_reported()
        {
            var items = new[] { Sheet("1", "L01", 4) };
            var plan = SheetNumberEngine.PlanRenumber(items, new[] { "A-RCP-L01-004", "A-RCP-L01-001" });
            Assert.Empty(plan.Moves);
            Assert.Single(plan.Conflicts);
        }

        [Fact]
        public void Gap_free_bucket_plans_nothing()
        {
            var items = new[] { Sheet("1", "L01", 1), Sheet("2", "L02", 2) };
            var plan = SheetNumberEngine.PlanRenumber(items, items.Select(i => i.CurrentNumber));
            Assert.Empty(plan.Moves);
            Assert.Equal(2, plan.HighWater["b"]);
        }

        [Fact]
        public void Buckets_compact_independently()
        {
            var items = new[] { Sheet("1", "L01", 5, bucket: "x"), Sheet("2", "L02", 9, bucket: "y") };
            var plan = SheetNumberEngine.PlanRenumber(items, items.Select(i => i.CurrentNumber));
            Assert.Equal(1, plan.HighWater["x"]);
            Assert.Equal(1, plan.HighWater["y"]);
        }
    }
}
