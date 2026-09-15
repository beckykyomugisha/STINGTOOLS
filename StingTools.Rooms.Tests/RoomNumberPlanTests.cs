using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Rooms;
using Xunit;

namespace StingTools.Rooms.Tests
{
    /// <summary>
    /// The planner decides every room number before anything is written. These assert the
    /// two properties that matter: the walk order is the one a person would take, and a
    /// plan that would duplicate a number is refused rather than applied.
    /// </summary>
    public class RoomNumberPlanTests
    {
        private static RoomSeed Seed(string key, double x, double y,
            string lvl = "GF", string dept = "", string existing = "")
        {
            return new RoomSeed
            {
                Key = key, XMm = x, YMm = y, LevelCode = lvl,
                Department = dept, Name = key, ExistingNumber = existing,
            };
        }

        private static RoomNumberingScheme Scheme(
            RoomNumberOrder order = RoomNumberOrder.Serpentine, bool preserve = false)
        {
            return new RoomNumberingScheme
            {
                Pattern = "{lvl}{seq:D2}",
                Order = order,
                RowBandMm = 3000.0,
                StartAt = 1,
                Step = 1,
                RestartPerLevel = true,
                PreserveExistingNumbers = preserve,
            };
        }

        // ── Walk order ───────────────────────────────────────────────────────

        [Fact]
        public void Serpentine_reverses_every_other_band()
        {
            // Two bands of three. Band 0 is the TOP of the plan (higher Y).
            var seeds = new[]
            {
                Seed("topLeft",    0, 5000), Seed("topMid",   4000, 5000), Seed("topRight", 8000, 5000),
                Seed("botLeft",    0,    0), Seed("botMid",   4000,    0), Seed("botRight", 8000,    0),
            };

            var plan = RoomNumberPlanner.Plan(seeds, Scheme());

            Assert.True(plan.CanApply, string.Join("; ", plan.Blockers));
            var order = plan.Assignments.OrderBy(a => a.NewNumber, StringComparer.Ordinal)
                            .Select(a => a.Name).ToArray();

            // Top band left-to-right, then bottom band right-to-left: the corridor walk.
            Assert.Equal(
                new[] { "topLeft", "topMid", "topRight", "botRight", "botMid", "botLeft" },
                order);
        }

        [Fact]
        public void RowMajor_reads_every_band_left_to_right()
        {
            var seeds = new[]
            {
                Seed("topLeft", 0, 5000), Seed("topRight", 8000, 5000),
                Seed("botLeft", 0,    0), Seed("botRight", 8000,    0),
            };

            var plan = RoomNumberPlanner.Plan(seeds, Scheme(RoomNumberOrder.RowMajor));

            Assert.True(plan.CanApply);
            Assert.Equal(
                new[] { "topLeft", "topRight", "botLeft", "botRight" },
                plan.Assignments.OrderBy(a => a.NewNumber, StringComparer.Ordinal)
                    .Select(a => a.Name).ToArray());
        }

        [Fact]
        public void Rooms_within_one_band_height_are_treated_as_one_row()
        {
            // 1200 mm apart vertically, inside the 3000 mm band: one row, so X decides.
            var seeds = new[] { Seed("right", 8000, 1200), Seed("left", 0, 0) };

            var plan = RoomNumberPlanner.Plan(seeds, Scheme());

            Assert.Equal("GF01", plan.Assignments.Single(a => a.Name == "left").NewNumber);
            Assert.Equal("GF02", plan.Assignments.Single(a => a.Name == "right").NewNumber);
        }

        [Fact]
        public void Row_grouping_does_not_move_when_the_project_origin_does()
        {
            // The first implementation banded on floor(y / bandHeight), anchored to the
            // project origin. Two rooms 1.2 m apart then landed in different rows purely
            // because they straddled a multiple of the band height — and WHICH rooms did
            // changed if anyone moved the base point. Shifting every room by a constant
            // must not change the answer.
            var atOrigin = new[]
            {
                Seed("left", 0, 0), Seed("right", 8000, 1200),
                Seed("farLeft", 0, 20000), Seed("farRight", 8000, 20000),
            };

            foreach (double shift in new[] { 0.0, 1500.0, -2999.0, 47231.0 })
            {
                var shifted = atOrigin
                    .Select(s => Seed(s.Key, s.XMm, s.YMm + shift))
                    .ToArray();

                var plan = RoomNumberPlanner.Plan(shifted, Scheme());

                Assert.True(plan.CanApply, string.Join("; ", plan.Blockers));
                Assert.Equal(
                    new[] { "farLeft", "farRight", "right", "left" },
                    plan.Assignments.OrderBy(a => a.NewNumber, StringComparer.Ordinal)
                        .Select(a => a.Name).ToArray());
            }
        }

        // ── Grouping ─────────────────────────────────────────────────────────

        [Fact]
        public void Sequence_restarts_per_level()
        {
            var seeds = new[]
            {
                Seed("g1", 0, 0, "GF"), Seed("g2", 4000, 0, "GF"),
                Seed("f1", 0, 0, "L01"), Seed("f2", 4000, 0, "L01"),
            };

            var plan = RoomNumberPlanner.Plan(seeds, Scheme());

            Assert.Equal("GF01", plan.Assignments.Single(a => a.Name == "g1").NewNumber);
            Assert.Equal("GF02", plan.Assignments.Single(a => a.Name == "g2").NewNumber);
            Assert.Equal("L0101", plan.Assignments.Single(a => a.Name == "f1").NewNumber);
            Assert.Equal("L0102", plan.Assignments.Single(a => a.Name == "f2").NewNumber);
        }

        [Fact]
        public void Department_is_stripped_to_characters_Revit_accepts()
        {
            var scheme = Scheme();
            scheme.Pattern = "{dept}{seq:D2}";
            scheme.RestartPerDepartment = true;

            var plan = RoomNumberPlanner.Plan(
                new[] { Seed("a", 0, 0, "GF", "In-Patient / Ward") }, scheme);

            Assert.Equal("InPatientWard01", plan.Assignments.Single().NewNumber);
        }

        // ── Refusals: the whole point of planning before writing ─────────────

        [Fact]
        public void A_pattern_with_no_seq_token_is_a_blocker_not_a_collision()
        {
            var scheme = Scheme();
            scheme.Pattern = "{lvl}";   // every room renders "GF"

            var plan = RoomNumberPlanner.Plan(
                new[] { Seed("a", 0, 0), Seed("b", 4000, 0) }, scheme);

            Assert.False(plan.CanApply);
            Assert.Contains(plan.Blockers, b => b.Contains("{seq}"));
            // And it refuses BEFORE assigning anything, rather than producing duplicates.
            Assert.Empty(plan.Assignments);
        }

        [Fact]
        public void A_zero_band_height_is_refused_rather_than_silently_meaningless()
        {
            var scheme = Scheme();
            scheme.RowBandMm = 0;

            var plan = RoomNumberPlanner.Plan(new[] { Seed("a", 0, 0) }, scheme);

            Assert.False(plan.CanApply);
            Assert.Contains(plan.Blockers, b => b.Contains("RowBandMm"));
        }

        [Fact]
        public void A_zero_step_is_refused()
        {
            var scheme = Scheme();
            scheme.Step = 0;

            var plan = RoomNumberPlanner.Plan(
                new[] { Seed("a", 0, 0), Seed("b", 4000, 0) }, scheme);

            Assert.False(plan.CanApply);
            Assert.Contains(plan.Blockers, b => b.Contains("Step"));
        }

        [Fact]
        public void An_unrecognised_pattern_token_warns_and_is_emitted_verbatim()
        {
            var scheme = Scheme();
            scheme.Pattern = "{building}{seq:D2}";

            var plan = RoomNumberPlanner.Plan(new[] { Seed("a", 0, 0) }, scheme);

            // Verbatim, not dropped: dropping it would silently collapse two schemes
            // that differ only by the token onto the same numbers.
            Assert.True(plan.CanApply, string.Join("; ", plan.Blockers));
            Assert.Equal("{building}01", plan.Assignments.Single().NewNumber);
            Assert.Contains(plan.Warnings, w => w.Contains("building"));
        }

        // ── Preserving what is already issued ────────────────────────────────

        [Fact]
        public void Preserved_numbers_are_kept_and_never_reissued_to_another_room()
        {
            var scheme = Scheme(preserve: true);

            // "GF01" is already taken by a room the plan must not touch. The unnumbered
            // room sits FIRST in the walk, so a naive planner hands it GF01 as well.
            var seeds = new[]
            {
                Seed("unnumbered", 0,    0, "GF"),
                Seed("issued",     4000, 0, "GF", existing: "GF01"),
            };

            var plan = RoomNumberPlanner.Plan(seeds, scheme);

            Assert.True(plan.CanApply, string.Join("; ", plan.Blockers));
            Assert.Equal(1, plan.PreservedCount);

            var issued = plan.Assignments.Single(a => a.Name == "issued");
            Assert.Equal("GF01", issued.NewNumber);
            Assert.False(issued.Changed);

            var fresh = plan.Assignments.Single(a => a.Name == "unnumbered");
            Assert.Equal("GF02", fresh.NewNumber);
            Assert.NotEqual(issued.NewNumber, fresh.NewNumber);
        }

        [Fact]
        public void With_preserve_off_every_room_is_renumbered_from_the_walk()
        {
            var seeds = new[]
            {
                Seed("a", 0,    0, "GF", existing: "OLD-9"),
                Seed("b", 4000, 0, "GF", existing: "OLD-1"),
            };

            var plan = RoomNumberPlanner.Plan(seeds, Scheme(preserve: false));

            Assert.Equal(0, plan.PreservedCount);
            Assert.Equal("GF01", plan.Assignments.Single(a => a.Name == "a").NewNumber);
            Assert.Equal("GF02", plan.Assignments.Single(a => a.Name == "b").NewNumber);
            Assert.Equal(2, plan.ChangedCount);
        }

        [Fact]
        public void An_empty_scope_reports_itself_rather_than_passing_quietly()
        {
            var plan = RoomNumberPlanner.Plan(new RoomSeed[0], Scheme());

            // No blocker — there is nothing wrong — but it must not read as success.
            Assert.Empty(plan.Assignments);
            Assert.Equal(0, plan.ChangedCount);
            Assert.Contains(plan.Warnings, w => w.Contains("No rooms in scope"));
        }

        [Fact]
        public void The_plan_never_contains_a_duplicate_number()
        {
            // A hundred rooms scattered across three levels and two departments.
            var rnd = new Random(1234);
            var seeds = new List<RoomSeed>();
            for (int i = 0; i < 100; i++)
                seeds.Add(Seed("r" + i, rnd.Next(0, 40000), rnd.Next(0, 40000),
                               lvl: new[] { "GF", "L01", "L02" }[i % 3],
                               dept: (i % 2 == 0) ? "Ward" : "Admin"));

            var plan = RoomNumberPlanner.Plan(seeds, Scheme());

            Assert.True(plan.CanApply, string.Join("; ", plan.Blockers));
            Assert.Equal(100, plan.Assignments.Count);
            Assert.Equal(100, plan.Assignments.Select(a => a.NewNumber)
                                  .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void Planning_is_deterministic_for_rooms_at_identical_coordinates()
        {
            // Two rooms stacked at the same point (a real modelling mistake). The tie is
            // broken by key, so two runs cannot disagree and produce a churning diff.
            var seeds = new[] { Seed("bbb", 0, 0), Seed("aaa", 0, 0) };

            var first = RoomNumberPlanner.Plan(seeds, Scheme());
            var second = RoomNumberPlanner.Plan(seeds, Scheme());

            Assert.Equal(
                first.Assignments.Select(a => a.Name + "=" + a.NewNumber),
                second.Assignments.Select(a => a.Name + "=" + a.NewNumber));
            Assert.Equal("GF01", first.Assignments.Single(a => a.Name == "aaa").NewNumber);
        }
    }
}
