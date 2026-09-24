using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Tag text size per drawing. DrawingType.EffectiveTagTextSizeMm decided the
    /// size (2.5 mm at 1:50, 2 mm at 1:100 …) and nothing placed tags by it. The
    /// runner now swaps in a size variant — inert until variants exist, so these
    /// pin both the choice and the "no variants → base, unchanged" guarantee.
    /// </summary>
    public class TagSizeVariantTests
    {
        private static DrawingType At(int scale) => new DrawingType { Id = "t", Scale = scale };

        [Fact]
        public void Family_name_appends_the_size_token()
            => Assert.Equal("STING - Door Tag 2.5mm", TagSizeVariant.FamilyName("STING - Door Tag", 2.5));

        [Theory]
        [InlineData("STING - Door Tag 2.5mm", "STING - Door Tag", 2.5)]
        [InlineData("STING - Door Tag 2mm", "STING - Door Tag", 2.0)]
        public void Family_variant_is_recognised(string family, string baseFamily, double size)
            => Assert.Equal(size, TagSizeVariant.SizeOfFamilyVariant(family, baseFamily));

        [Theory]
        [InlineData("STING - Door Tag", "STING - Door Tag")]            // the base itself
        [InlineData("STING - Door Tag Large", "STING - Door Tag")]      // not a size
        [InlineData("STING - Window Tag 2.5mm", "STING - Door Tag")]    // another family
        public void Non_variants_are_not_mistaken_for_one(string family, string baseFamily)
            => Assert.Null(TagSizeVariant.SizeOfFamilyVariant(family, baseFamily));

        [Fact]
        public void No_variants_means_the_base_is_used_unchanged()
            => Assert.Equal(TagSizeVariant.Kind.None, TagSizeVariant.Choose(At(50), null, null).Kind);

        [Fact]
        public void Exact_size_for_the_scale_is_chosen()
        {
            var c = TagSizeVariant.Choose(At(50), new[] { 2.0, 2.5, 3.5 }, null);
            Assert.Equal(TagSizeVariant.Kind.Family, c.Kind);
            Assert.Equal(2.5, c.SizeMm);
        }

        [Fact]
        public void Nearest_built_size_when_the_ideal_was_never_authored()
        {
            // 1:200 wants 1.0 mm; only 2.5 and 3.5 built → 2.5, not nothing.
            Assert.Equal(2.5, TagSizeVariant.Choose(At(200), new[] { 2.5, 3.5 }, null).SizeMm);
        }

        [Fact]
        public void Family_variants_win_over_type_variants()
            => Assert.Equal(TagSizeVariant.Kind.Family,
                TagSizeVariant.Choose(At(50), new[] { 2.5 }, new[] { 2.5 }).Kind);

        [Fact]
        public void Type_variants_are_used_when_there_are_no_family_variants()
        {
            var c = TagSizeVariant.Choose(At(100), null, new[] { 2.0, 3.5 });
            Assert.Equal(TagSizeVariant.Kind.Type, c.Kind);
            Assert.Equal(2.0, c.SizeMm);
        }

        [Fact]
        public void Explicit_drawing_type_size_overrides_the_scale()
        {
            var dt = new DrawingType { Id = "t", Scale = 50, TagTextSizeMm = 3.5 };
            Assert.Equal(3.5, TagSizeVariant.Choose(dt, new[] { 2.5, 3.5 }, null).SizeMm);
        }
    }
}
