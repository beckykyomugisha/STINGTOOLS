using System.Collections.Generic;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Two tag rules on one category. The runner used to tag each category once per
    /// view and skip any element that carried any tag, so the second rule — a
    /// Pressure Regime Tag beside the Room Tag, a Pendant Tag beside a Bedhead
    /// Trunking Tag — was dropped without a word.
    /// </summary>
    public class TagRuleIdentityTests
    {
        private const long Rooms = -2000160;
        private const string RoomTag = "STING - Room Tag";
        private const string Pressure = "STING - Pressure Regime Tag";
        private static readonly ISet<string> Specialists = TagRuleIdentity.SpecialistFamilies(new[] { null, Pressure });

        [Fact]
        public void A_specialist_rule_is_separate_work_from_the_primary_rule_on_the_same_category()
            => Assert.NotEqual(TagRuleIdentity.DedupKey(Rooms, null, null),
                               TagRuleIdentity.DedupKey(Rooms, Pressure, null));

        [Fact]
        public void Rules_split_by_familyMatch_are_separate_work()
            => Assert.NotEqual(TagRuleIdentity.DedupKey(Rooms, null, "pendant"),
                               TagRuleIdentity.DedupKey(Rooms, null, "bedhead"));

        [Fact]
        public void Room_name_and_number_rules_still_collapse_onto_the_room_pass()
            // AutoTag / AutoTagRoomName / AutoTagRoomNumber all resolve to Rooms with no
            // family of their own; they were one pass before and must stay one.
            => Assert.Equal(TagRuleIdentity.DedupKey(Rooms, null, null),
                            TagRuleIdentity.DedupKey(Rooms, "", " "));

        [Fact]
        public void A_size_variant_is_the_same_rule_as_its_base()
            => Assert.Equal(TagRuleIdentity.DedupKey(Rooms, Pressure, null),
                            TagRuleIdentity.DedupKey(Rooms, Pressure + " 2.5mm", null));

        [Fact]
        public void Specialist_tag_is_added_beside_the_room_tag()
            => Assert.False(TagRuleIdentity.ShouldSkip(new[] { RoomTag }, true, Pressure, Specialists));

        [Fact]
        public void Room_tag_is_still_placed_when_the_specialist_tag_got_there_first()
            => Assert.False(TagRuleIdentity.ShouldSkip(new[] { Pressure }, false, RoomTag, Specialists));

        [Fact]
        public void Neither_rule_tags_twice()
        {
            Assert.True(TagRuleIdentity.ShouldSkip(new[] { RoomTag, Pressure }, true, Pressure, Specialists));
            Assert.True(TagRuleIdentity.ShouldSkip(new[] { RoomTag, Pressure }, false, RoomTag, Specialists));
            Assert.True(TagRuleIdentity.ShouldSkip(new[] { Pressure + " 2.5mm" }, true, Pressure, Specialists));
        }

        [Fact]
        public void A_users_own_tag_still_stops_the_primary_rule()
        {
            Assert.True(TagRuleIdentity.ShouldSkip(new[] { "Office Room Tag" }, false, RoomTag, Specialists));
            Assert.True(TagRuleIdentity.ShouldSkip(new[] { "" }, false, RoomTag, Specialists));
        }

        [Fact]
        public void A_users_own_tag_does_not_stop_a_specialist_rule()
            => Assert.False(TagRuleIdentity.ShouldSkip(new[] { "Office Room Tag" }, true, Pressure, Specialists));

        [Fact]
        public void An_untagged_element_is_never_skipped()
        {
            Assert.False(TagRuleIdentity.ShouldSkip(new string[0], false, RoomTag, Specialists));
            Assert.False(TagRuleIdentity.ShouldSkip(null, true, Pressure, Specialists));
        }

        [Theory]
        [InlineData("STING - Door Tag 2.5mm", "STING - Door Tag")]
        [InlineData("STING - Door Tag", "STING - Door Tag")]
        [InlineData("STING - 5-Gauss Marker Tag", "STING - 5-Gauss Marker Tag")]
        [InlineData(null, "")]
        public void Base_family_strips_only_a_size_token(string name, string expected)
            => Assert.Equal(expected, TagRuleIdentity.BaseFamily(name));
    }
}
