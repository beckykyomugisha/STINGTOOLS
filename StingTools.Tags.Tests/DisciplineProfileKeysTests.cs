using System.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// TAGACC-25: a retired DISCIPLINE_PROFILES key stays visible — it is classified, in either
    /// spelling, with the project-wide setting that replaces it — and the collision-mode
    /// precedence is explicit dialog &gt; profile &gt; DEFAULT_COLLISION_MODE &gt; AutoIncrement.
    /// </summary>
    public class DisciplineProfileKeysTests
    {
        [Theory]
        [InlineData("SeqScheme", "SEQ_SCHEME")]
        [InlineData("seq_scheme", "SEQ_SCHEME")]
        [InlineData("SeqIncludeZone", "SEQ_INCLUDE_ZONE")]
        [InlineData("seq_include_zone", "SEQ_INCLUDE_ZONE")]
        [InlineData("SeqPadWidth", "TAG_FORMAT.num_pad")]
        [InlineData("seq_pad_width", "TAG_FORMAT.num_pad")]
        [InlineData("DefaultZone", "STING_TAG_TOKEN_POLICY.json")]
        [InlineData("default_zone", "STING_TAG_TOKEN_POLICY.json")]
        [InlineData("DefaultLoc", "STING_TAG_TOKEN_POLICY.json")]
        [InlineData("default_loc", "STING_TAG_TOKEN_POLICY.json")]
        [InlineData("seqpadwidth", "TAG_FORMAT.num_pad")]   // Newtonsoft matched case-insensitively, so must we
        public void Retired_key_is_classified_with_its_replacement(string key, string replacementMentions)
        {
            Assert.Equal(DisciplineProfileKeyKind.Retired, DisciplineProfileKeys.Classify(key));

            var f = Assert.Single(DisciplineProfileKeys.Inspect("M", new[] { key }));
            Assert.Equal(DisciplineProfileKeyKind.Retired, f.Kind);
            Assert.Contains(replacementMentions, f.Replacement);
            Assert.Contains("DISCIPLINE_PROFILES.M." + key, f.Message);
            Assert.Contains(replacementMentions, f.Message);
        }

        [Fact]
        public void Retired_list_is_exactly_the_five_decided_settings_and_none_is_still_a_property()
        {
            Assert.Equal(
                new[] { "DefaultLoc", "DefaultZone", "SeqIncludeZone", "SeqPadWidth", "SeqScheme" },
                DisciplineProfileKeys.Retired.Select(r => r.Name).OrderBy(n => n).ToArray());
            foreach (var r in DisciplineProfileKeys.Retired)
            {
                Assert.DoesNotContain(r.Name, DisciplineProfileKeys.KnownSettings);
                Assert.False(string.IsNullOrWhiteSpace(r.Replacement));
            }
        }

        [Theory]
        [InlineData("CollisionMode")]
        [InlineData("collisionmode")]
        [InlineData("AllowedSysCodes")]
        [InlineData("DefaultParagraphDepth")]
        public void Bound_setting_is_known_and_produces_no_finding(string key)
        {
            Assert.Equal(DisciplineProfileKeyKind.Known, DisciplineProfileKeys.Classify(key));
            Assert.Empty(DisciplineProfileKeys.Inspect("E", new[] { key }));
        }

        [Fact]
        public void Unknown_key_is_named_and_a_snake_case_spelling_of_a_real_setting_gets_a_hint()
        {
            var findings = DisciplineProfileKeys.Inspect("P",
                new[] { "collision_mode", "Colour", "AllowedFuncCodes", "SeqPadWidth" });

            Assert.Equal(3, findings.Count);
            var snake = findings[0];
            Assert.Equal(DisciplineProfileKeyKind.Unknown, snake.Kind);
            Assert.Equal("CollisionMode", snake.DidYouMean);
            Assert.Contains("did you mean", snake.Message);

            Assert.Equal(DisciplineProfileKeyKind.Unknown, findings[1].Kind);
            Assert.Null(findings[1].DidYouMean);

            Assert.Equal(DisciplineProfileKeyKind.Retired, findings[2].Kind);
        }

        // ── Collision-mode precedence: all four levels ──

        [Fact]
        public void Explicit_dialog_choice_beats_everything()
            => Assert.Equal(TagCollisionMode.Skip, DisciplineProfileKeys.ResolvePrecedence<TagCollisionMode>(
                TagCollisionMode.Skip, TagCollisionMode.Overwrite, TagCollisionMode.Overwrite, TagCollisionMode.AutoIncrement));

        [Fact]
        public void Profile_beats_project_default_when_no_dialog_asked()
            => Assert.Equal(TagCollisionMode.Overwrite, DisciplineProfileKeys.ResolvePrecedence<TagCollisionMode>(
                null, TagCollisionMode.Overwrite, TagCollisionMode.Skip, TagCollisionMode.AutoIncrement));

        [Fact]
        public void Project_default_applies_when_the_profile_sets_nothing()
            => Assert.Equal(TagCollisionMode.Skip, DisciplineProfileKeys.ResolvePrecedence<TagCollisionMode>(
                null, null, TagCollisionMode.Skip, TagCollisionMode.AutoIncrement));

        [Fact]
        public void Fallback_applies_when_nothing_is_set()
            => Assert.Equal(TagCollisionMode.AutoIncrement, DisciplineProfileKeys.ResolvePrecedence<TagCollisionMode>(
                null, null, null, TagCollisionMode.AutoIncrement));

        [Fact]
        public void Profile_collision_mode_deserialises_from_project_config_spelling()
        {
            // Newtonsoft's enum converter: the string a project writes must bind, not default.
            var p = Newtonsoft.Json.JsonConvert.DeserializeObject<DisciplineProfile>("{\"CollisionMode\":\"Skip\"}");
            Assert.Equal(TagCollisionMode.Skip, p.CollisionMode);
            var q = Newtonsoft.Json.JsonConvert.DeserializeObject<DisciplineProfile>("{\"collisionMode\":\"overwrite\"}");
            Assert.Equal(TagCollisionMode.Overwrite, q.CollisionMode);
            var none = Newtonsoft.Json.JsonConvert.DeserializeObject<DisciplineProfile>("{\"DefaultProd\":\"EQP\"}");
            Assert.Null(none.CollisionMode);
        }
    }
}
