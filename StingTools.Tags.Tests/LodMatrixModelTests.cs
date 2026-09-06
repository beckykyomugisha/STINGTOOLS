using System.Collections.Generic;
using Newtonsoft.Json;
using StingTools.Core.Validation;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// WI-7 — an empty LOD scope must not read as a green gate.
    ///
    /// LodVerificationEngine.Verify drops an element whose category resolves to no
    /// check BEFORE counting it. If a matrix has category rules but no "*"
    /// fallback, every uncovered element is dropped, Total stays 0, and
    /// OverallPct returns 100.0 — a run that verified nothing reporting as a pass.
    /// Same failure class as the eleven workflow presets that executed zero steps
    /// and reported success (#630).
    ///
    /// These tests pin both halves of the fix: LodRuleResolver.Resolve returning
    /// null is the precondition, and LodTally's NoElementsInScope / SkippedNoRule
    /// are what stop the caller reading 100.0 as a pass.
    /// </summary>
    public class LodMatrixModelTests
    {
        private static LodMatrix MatrixWith(params LodCategoryRule[] rules)
            => new LodMatrix { CategoryRules = new List<LodCategoryRule>(rules) };

        private static LodCategoryRule Rule(string category, string lodKey)
            => new LodCategoryRule
            {
                Category = category,
                Checks = new Dictionary<string, LodCheck>
                {
                    [lodKey] = new LodCheck { RequireGeometry = true }
                }
            };

        // ── The precondition: what makes an element fall out of scope ──────────

        [Fact]
        public void Resolve_returns_null_when_category_has_no_rule_and_matrix_has_no_star()
        {
            var matrix = MatrixWith(Rule("Lighting Fixtures", "300"));
            Assert.Null(LodRuleResolver.Resolve(matrix, "Walls", "300"));
        }

        [Fact]
        public void Resolve_falls_back_to_the_star_rule_when_one_exists()
        {
            var matrix = MatrixWith(Rule("Lighting Fixtures", "300"), Rule("*", "300"));
            Assert.NotNull(LodRuleResolver.Resolve(matrix, "Walls", "300"));
        }

        [Fact]
        public void Resolve_returns_null_when_the_category_rule_has_no_rung_at_that_lod()
        {
            var matrix = MatrixWith(Rule("Walls", "300"));
            Assert.Null(LodRuleResolver.Resolve(matrix, "Walls", "500"));
        }

        [Fact]
        public void Resolve_is_case_insensitive_on_category()
        {
            var matrix = MatrixWith(Rule("Lighting Fixtures", "300"));
            Assert.NotNull(LodRuleResolver.Resolve(matrix, "lighting fixtures", "300"));
        }

        // ── The fix: a tally over zero elements is not a pass ──────────────────

        [Fact]
        public void OverallPct_still_returns_100_on_an_empty_tally_which_is_why_the_flag_exists()
        {
            var tally = new LodTally();
            Assert.Equal(0, tally.Total);
            Assert.Equal(100.0, tally.OverallPct);      // the trap, pinned deliberately
            Assert.True(tally.NoElementsInScope);       // the thing callers must branch on
        }

        [Fact]
        public void A_tally_with_verified_elements_is_not_reported_as_out_of_scope()
        {
            var tally = new LodTally { Total = 4, Passed = 3 };
            Assert.False(tally.NoElementsInScope);
            Assert.Equal(1, tally.Failed);
            Assert.Equal(75.0, tally.OverallPct);
        }

        [Fact]
        public void Every_element_skipped_leaves_an_empty_scope_and_a_non_zero_skip_count()
        {
            // The exact shape of the bug: an overlay supplies categoryRules, the
            // baseline has no "*", so nothing resolves and nothing is counted.
            var matrix = MatrixWith(Rule("Lighting Fixtures", "300"));
            var tally = new LodTally();

            foreach (var category in new[] { "Walls", "Walls", "Doors", "Ducts" })
            {
                if (LodRuleResolver.Resolve(matrix, category, "300") == null)
                {
                    tally.RecordSkip(category);
                    continue;
                }
                tally.Total++;
                tally.Passed++;
            }

            Assert.True(tally.NoElementsInScope);
            Assert.Equal(4, tally.SkippedNoRule);
            Assert.Equal(2, tally.SkippedByCategory["Walls"]);
            Assert.Equal(1, tally.SkippedByCategory["Doors"]);
            Assert.Equal(1, tally.SkippedByCategory["Ducts"]);
        }

        [Fact]
        public void Skipped_elements_stay_outside_the_denominator()
        {
            // Skips must not be silently folded into Total — that would turn a
            // coverage gap into a fail, which is a different lie.
            var tally = new LodTally { Total = 2, Passed = 2 };
            tally.RecordSkip("Topography");
            tally.RecordSkip("Topography");

            Assert.Equal(2, tally.Total);
            Assert.Equal(100.0, tally.OverallPct);
            Assert.Equal(2, tally.SkippedNoRule);
            Assert.False(tally.NoElementsInScope);
        }

        [Fact]
        public void RecordSkip_buckets_a_missing_category_name_rather_than_throwing()
        {
            var tally = new LodTally();
            tally.RecordSkip(null);
            tally.RecordSkip("");
            Assert.Equal(2, tally.SkippedNoRule);
            Assert.Equal(2, tally.SkippedByCategory["(no category)"]);
        }

        [Fact]
        public void SkippedByCategory_is_case_insensitive_so_one_category_is_one_bucket()
        {
            var tally = new LodTally();
            tally.RecordSkip("Walls");
            tally.RecordSkip("walls");
            Assert.Single(tally.SkippedByCategory);
            Assert.Equal(2, tally.SkippedByCategory["WALLS"]);
        }

        // ── Inheritance, since the fix delegates resolution to this type ───────

        [Fact]
        public void Inherit_folds_the_lower_rung_and_plus_adds_to_its_required_params()
        {
            var matrix = MatrixWith(new LodCategoryRule
            {
                Category = "Plumbing Fixtures",
                Checks = new Dictionary<string, LodCheck>
                {
                    ["400"] = new LodCheck
                    {
                        RequireManufacturerType = true,
                        RequiredParams = new List<string> { "ASS_MODEL_REF_TXT" }
                    },
                    ["500"] = new LodCheck
                    {
                        Inherit = "400",
                        RequiredParams = new List<string> { "+ASS_SERIAL_NR_TXT" }
                    }
                }
            });

            var r = LodRuleResolver.Resolve(matrix, "Plumbing Fixtures", "500");
            Assert.NotNull(r);
            Assert.True(r.RequireManufacturerType);
            Assert.Contains("ASS_MODEL_REF_TXT", r.RequiredParams);
            Assert.Contains("ASS_SERIAL_NR_TXT", r.RequiredParams);
        }

        [Fact]
        public void An_inheritance_loop_terminates_instead_of_hanging_the_gate()
        {
            var matrix = MatrixWith(new LodCategoryRule
            {
                Category = "Walls",
                Checks = new Dictionary<string, LodCheck>
                {
                    ["300"] = new LodCheck { Inherit = "400" },
                    ["400"] = new LodCheck { Inherit = "300" }
                }
            });

            Assert.NotNull(LodRuleResolver.Resolve(matrix, "Walls", "300"));
        }

        [Fact]
        public void The_matrix_model_round_trips_the_shipped_json_shape()
        {
            // Guards the Newtonsoft binding: a renamed field would leave these at
            // default and the gate would go quietly permissive.
            const string json = @"{
              ""version"": ""1.1"",
              ""milestones"": [ { ""id"": ""deliverable-d"", ""name"": ""D"", ""lod"": 500 } ],
              ""categoryRules"": [ { ""category"": ""*"", ""checks"": {
                  ""400"": { ""requireGeometry"": true, ""requiredParams"": [""ASS_MODEL_REF_TXT""] } } } ]
            }";
            var m = JsonConvert.DeserializeObject<LodMatrix>(json);

            Assert.Equal("1.1", m.Version);
            Assert.Equal(500, m.Milestones[0].Lod);
            Assert.Equal("deliverable-d", m.Milestones[0].Id);

            var r = LodRuleResolver.Resolve(m, "anything at all", "400");
            Assert.NotNull(r);
            Assert.True(r.RequireGeometry);
            Assert.Contains("ASS_MODEL_REF_TXT", r.RequiredParams);
        }
    }
}
