using System;
using System.IO;
using System.Linq;
using StingTools.Core.Rooms;
using Xunit;

namespace StingTools.Rooms.Tests
{
    /// <summary>
    /// The shipped baseline, asserted against the POCO that actually reads it.
    ///
    /// This is the assertion the LOD matrix and the COBie map each needed and lacked:
    /// Newtonsoft does not fail on a misspelled or mistyped field, it leaves the property
    /// at its default. A "rowbandMM" typo in the JSON parses cleanly, builds cleanly, and
    /// hands the planner a band height of 0 — which is why the planner treats 0 as a
    /// blocker and why this test exists to catch the typo before a user does.
    /// </summary>
    public class SchemeLibraryTests
    {
        private static string BaselinePath()
        {
            string p = Path.Combine(AppContext.BaseDirectory, "Data", RoomNumberingStore.BaselineFileName);
            if (!File.Exists(p))
                throw new FileNotFoundException(
                    "The shipped baseline was not copied to the test output. Without it every " +
                    "assertion below would pass on an empty library, which is the failure mode " +
                    "it exists to prevent. Check the <Content Include> entry in " +
                    "StingTools.Rooms.Tests.csproj.", p);
            return p;
        }

        private static RoomNumberingLibrary Baseline()
        {
            return RoomNumberingStore.Parse(File.ReadAllText(BaselinePath()));
        }

        [Fact]
        public void The_shipped_baseline_binds_every_field_it_declares()
        {
            var lib = Baseline();

            Assert.True(lib.Schemes.Count >= 5,
                "Only " + lib.Schemes.Count + " schemes parsed from the shipped baseline.");
            Assert.False(string.IsNullOrWhiteSpace(lib.DefaultSchemeId));

            foreach (var s in lib.Schemes)
            {
                Assert.False(string.IsNullOrWhiteSpace(s.Id), "A scheme has no id.");
                Assert.False(string.IsNullOrWhiteSpace(s.Name), s.Id + " has no name.");

                // Each of these is a field a silent Newtonsoft default would zero out.
                Assert.False(string.IsNullOrWhiteSpace(s.Pattern), s.Id + " has an empty Pattern.");
                Assert.True(s.RowBandMm > 0, s.Id + " has RowBandMm " + s.RowBandMm +
                                             " — the JSON key is probably misspelled.");
                Assert.True(s.Step != 0, s.Id + " has Step 0 — the JSON key is probably misspelled.");
                Assert.True(s.StartAt > 0, s.Id + " has StartAt " + s.StartAt + ".");
            }
        }

        [Fact]
        public void The_order_enum_survives_the_round_trip_from_its_string_form()
        {
            // "Serpentine" in JSON must arrive as the enum member, not as the zero default
            // that would also be Serpentine and hide the failure. RowMajor proves it binds.
            var lib = Baseline();

            var rowMajor = lib.Schemes.Where(s => s.Order == RoomNumberOrder.RowMajor).ToList();
            Assert.True(rowMajor.Count > 0,
                "No shipped scheme parsed as RowMajor. Because Serpentine is the enum's zero " +
                "value, a failure to bind 'order' at all would look identical to every scheme " +
                "being Serpentine. At least one non-default value must survive for this file " +
                "to prove anything.");
        }

        [Fact]
        public void Every_shipped_scheme_produces_a_usable_plan()
        {
            // Validated against the planner, not against itself. A scheme that cannot number
            // two rooms in two departments on two levels is not a scheme worth shipping.
            var seeds = new[]
            {
                new RoomSeed { Key = "a", XMm = 0,    YMm = 0,    LevelCode = "GF",  Department = "Ward",  Name = "a" },
                new RoomSeed { Key = "b", XMm = 6000, YMm = 0,    LevelCode = "GF",  Department = "Ward",  Name = "b" },
                new RoomSeed { Key = "c", XMm = 0,    YMm = 9000, LevelCode = "L01", Department = "Admin", Name = "c", LevelElevationMm = 3500 },
                new RoomSeed { Key = "d", XMm = 6000, YMm = 9000, LevelCode = "L01", Department = "Admin", Name = "d", LevelElevationMm = 3500 },
            };

            foreach (var scheme in Baseline().Schemes)
            {
                var plan = RoomNumberPlanner.Plan(seeds, scheme);

                Assert.True(plan.CanApply,
                    "Shipped scheme '" + scheme.Id + "' cannot produce a plan: " +
                    string.Join("; ", plan.Blockers));
                Assert.Empty(plan.Warnings);   // no unrecognised tokens in a shipped pattern
                Assert.Equal(4, plan.Assignments.Count);
                Assert.Equal(4, plan.Assignments.Select(a => a.NewNumber)
                                    .Distinct(StringComparer.OrdinalIgnoreCase).Count());
                Assert.All(plan.Assignments,
                    a => Assert.False(string.IsNullOrWhiteSpace(a.NewNumber),
                        "Scheme '" + scheme.Id + "' rendered a blank number."));
            }
        }

        [Fact]
        public void The_default_scheme_id_names_a_scheme_that_exists()
        {
            var lib = Baseline();
            Assert.Contains(lib.Schemes,
                s => string.Equals(s.Id, lib.DefaultSchemeId, StringComparison.OrdinalIgnoreCase));
        }

        // ── Layering ─────────────────────────────────────────────────────────

        [Fact]
        public void A_project_scheme_wins_by_id_and_leaves_the_rest_of_the_baseline_alone()
        {
            var baseline = Baseline();
            int baselineCount = baseline.Schemes.Count;

            var project = RoomNumberingStore.Parse(
                "{ \"schemes\": [ { \"id\": \"level-serpentine\", \"name\": \"House style\", " +
                "\"pattern\": \"{lvl}.{seq:D3}\", \"rowBandMm\": 4500.0, \"step\": 1, \"startAt\": 1 } ] }");

            var merged = RoomNumberingStore.Merge(baseline, project);

            Assert.Equal(baselineCount, merged.Schemes.Count);   // overridden, not appended
            var won = merged.Schemes.Single(s => s.Id == "level-serpentine");
            Assert.Equal("House style", won.Name);
            Assert.Equal(4500.0, won.RowBandMm);
        }

        [Fact]
        public void A_project_default_naming_a_scheme_nobody_defines_warns_rather_than_silently_switching()
        {
            var warnings = new System.Collections.Generic.List<string>();
            var project = RoomNumberingStore.Parse("{ \"defaultSchemeId\": \"does-not-exist\" }");

            var merged = RoomNumberingStore.Merge(Baseline(), project, warnings);

            Assert.Contains(warnings, w => w.Contains("does-not-exist"));
            // And it lands on something real rather than leaving a dangling id.
            Assert.Contains(merged.Schemes,
                s => string.Equals(s.Id, merged.DefaultSchemeId, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void A_missing_project_file_is_the_normal_state_not_an_error()
        {
            var warnings = new System.Collections.Generic.List<string>();

            var lib = RoomNumberingStore.LoadFile(
                Path.Combine(Path.GetTempPath(), "sting-no-such-room-numbering.json"), warnings);

            Assert.Empty(lib.Schemes);
            Assert.Empty(warnings);
        }

        [Fact]
        public void Malformed_project_json_warns_and_falls_back_instead_of_throwing()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "sting-broken-room-numbering-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{ this is not json");
            try
            {
                var warnings = new System.Collections.Generic.List<string>();
                var lib = RoomNumberingStore.LoadFile(path, warnings);

                Assert.Empty(lib.Schemes);
                Assert.Single(warnings);

                // The corporate baseline still stands, so the user can still renumber.
                var merged = RoomNumberingStore.Merge(Baseline(), lib);
                Assert.True(merged.Schemes.Count >= 5);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Resolve_falls_back_to_the_library_default_for_an_unknown_id()
        {
            var lib = Baseline();

            Assert.Equal(lib.DefaultSchemeId, RoomNumberingStore.Resolve(lib, null).Id);
            Assert.NotNull(RoomNumberingStore.Resolve(lib, "nonsense"));
            Assert.Equal("dept-sequence", RoomNumberingStore.Resolve(lib, "dept-sequence").Id);
        }
    }
}
