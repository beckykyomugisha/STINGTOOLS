using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Tag text size per drawing. DrawingType.EffectiveTagTextSizeMm decides the
    /// printed size (ISO 3098: 2.5 mm, 3.5 mm on A0; it used to shrink with the scale
    /// down to 1 mm) and the runner swaps in a size variant — inert until variants
    /// exist, so these pin both the choice and the "no variants → base" guarantee.
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
            // 1:200 wants 2.5 mm like every A1 drawing; only 3.5 and 5 built → 3.5, not nothing.
            Assert.Equal(3.5, TagSizeVariant.Choose(At(200), new[] { 3.5, 5.0 }, null).SizeMm);
        }

        [Fact]
        public void Family_variants_win_over_type_variants()
            => Assert.Equal(TagSizeVariant.Kind.Family,
                TagSizeVariant.Choose(At(50), new[] { 2.5 }, new[] { 2.5 }).Kind);

        [Fact]
        public void Type_variants_are_used_when_there_are_no_family_variants()
        {
            var c = TagSizeVariant.Choose(At(100), null, new[] { 2.5, 3.5 });
            Assert.Equal(TagSizeVariant.Kind.Type, c.Kind);
            Assert.Equal(2.5, c.SizeMm);
        }

        [Fact]
        public void Explicit_drawing_type_size_overrides_the_scale()
        {
            var dt = new DrawingType { Id = "t", Scale = 50, TagTextSizeMm = 3.5 };
            Assert.Equal(3.5, TagSizeVariant.Choose(dt, new[] { 2.5, 3.5 }, null).SizeMm);
        }

        // The tag style catalogue names types "{size}_{style}_{colour}_{arrow}_T{tier}", and the
        // specialist tag build sheet follows it. Only "2.5mm" was recognised, so those
        // families never had their size chosen.
        [Theory]
        [InlineData("2.5mm", 2.5)]
        [InlineData("2.5_NOM_BLACK_Open30_T2", 2.5)]
        [InlineData("2_BOLD_RED_Filled30_T2", 2.0)]
        [InlineData("3.5_NOM_BLACK_Open30_T2", 3.5)]
        [InlineData("2.5BOLD_RED", 2.5)]            // rule engine / scale tier naming
        [InlineData("2NOM_BLACK", 2.0)]
        [InlineData("3 Point Leader", null)]        // a number that is not a size
        [InlineData("Standard", null)]
        [InlineData("Code + Name", null)]
        [InlineData("_NOM", null)]
        public void Type_name_size_reads_both_conventions(string name, double? size)
            => Assert.Equal(size, TagSizeVariant.SizeOfTypeName(name));

        [Theory]
        [InlineData("2.5_BOLD_RED_Open30_T2", "BOLD_RED_Open30_T2")]
        [InlineData("2.5mm", "")]
        [InlineData("Standard", "")]
        public void Style_is_everything_but_the_size(string name, string style)
            => Assert.Equal(style, TagSizeVariant.StyleOfTypeName(name));

        [Fact]
        public void Only_a_type_differing_in_size_alone_is_a_size_variant()
        {
            // Switching a bold red 2.5 mm tag to 2 mm must not make it normal black.
            string baseStyle = TagSizeVariant.StyleOfTypeName("2.5_BOLD_RED_Open30_T2");
            Assert.Equal(baseStyle, TagSizeVariant.StyleOfTypeName("2_BOLD_RED_Open30_T2"));
            Assert.NotEqual(baseStyle, TagSizeVariant.StyleOfTypeName("2_NOM_BLACK_Open30_T2"));
        }

        [Fact]
        public void Rule_engine_type_names_are_size_variants_of_each_other()
        {
            Assert.Equal("BOLD_RED", TagSizeVariant.StyleOfTypeName("2.5BOLD_RED"));
            Assert.Equal(TagSizeVariant.StyleOfTypeName("3.5BOLD_RED"), TagSizeVariant.StyleOfTypeName("2BOLD_RED"));
            Assert.NotEqual(TagSizeVariant.StyleOfTypeName("2.5BOLD_RED"), TagSizeVariant.StyleOfTypeName("2.5NOM_RED"));
        }
    }
}
