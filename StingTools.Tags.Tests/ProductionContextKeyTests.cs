using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>DTW-42: a production context is identified by level id / room id / box UniqueId, so renaming a level or box does not re-mint its views and sheets. The plugin calls the same file.</summary>
    public class ProductionContextKeyTests
    {

        [Fact]
        public void Renaming_the_level_keeps_the_context()
        {
            var before = ProductionContextKey.Compose("Level 1", 312, null, null, null, null);
            var after  = ProductionContextKey.Compose("Ground Floor", 312, null, null, null, null);
            Assert.NotEqual(before, after);
            Assert.True(ProductionContextKey.Matches(before, after, ProductionContextKey.Legacy("Ground Floor", null, null, null)));
        }

        [Fact]
        public void Renaming_the_scope_box_keeps_the_context()
        {
            var before = ProductionContextKey.Compose("Level 1", 312, null, "A01", "STING-AREA::A01::L01", "abc-0001");
            var after  = ProductionContextKey.Compose("Level 1", 312, null, "A07", "STING-AREA::A07::L01", "abc-0001");
            Assert.True(ProductionContextKey.Matches(before, after, null));
        }

        [Fact]
        public void A_different_level_or_box_is_a_different_context()
        {
            var a = ProductionContextKey.Compose("Level 1", 312, null, null, null, null);
            var b = ProductionContextKey.Compose("Level 1", 313, null, null, null, null);
            Assert.False(ProductionContextKey.Matches(a, b, ProductionContextKey.Legacy("Level 1", null, null, null) + "x"));
            var x = ProductionContextKey.Compose("Level 1", 312, null, "A01", "BOX", "uid-1");
            var y = ProductionContextKey.Compose("Level 1", 312, null, "A01", "BOX", "uid-2");
            Assert.False(ProductionContextKey.Matches(x, y, null));
        }

        [Fact]
        public void A_stamp_from_before_ids_is_found_through_the_legacy_form()
        {
            var legacyStamp = ViewContextTag.Compose("Level 1", "", null, "");
            var current = ProductionContextKey.Compose("Level 1", 312, null, null, null, null);
            Assert.True(ProductionContextKey.Matches(legacyStamp, current, ProductionContextKey.Legacy("Level 1", null, null, null)));
        }

        [Fact]
        public void The_stamp_keeps_the_parts_other_readers_parse()
        {
            var stamp = ProductionContextKey.Compose("Level 1", 312, null, "A01", "STING-AREA::A01::L01", "abc-0001");
            var parts = stamp.Split(new[] { "::" }, StringSplitOptions.None);
            Assert.Equal("Level 1", parts[0]);
            Assert.Equal("A01", parts[2]);
            Assert.Equal("STING-AREA::A01::L01", ViewContextTag.ScopeBoxName(stamp));
        }

        [Fact]
        public void A_context_with_no_ids_is_stamped_exactly_as_before()
            => Assert.Equal(ViewContextTag.Compose(null, null, "Grid-A", null),
                            ProductionContextKey.Compose(null, null, null, "Grid-A", null, null));

        [Fact]
        public void Two_tagged_contexts_on_one_level_without_a_box_stay_apart()
        {
            var a = ProductionContextKey.Compose("Level 1", 312, null, "STING-DEPENDENT-PARENT", null, null);
            var b = ProductionContextKey.Compose("Level 1", 312, null, null, null, null);
            Assert.False(ProductionContextKey.Matches(a, b, null));
        }
    }
}
