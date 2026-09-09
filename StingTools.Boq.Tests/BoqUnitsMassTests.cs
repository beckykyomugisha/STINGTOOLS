using StingTools.BOQ;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// BoqUnits guards a 1000x money error and had no test.
    ///
    /// <para>Its own header records why it exists: tonne and kg were once collapsed
    /// onto one token, so a per-TONNE rate could meet a KILOGRAM quantity and be
    /// billed as if the two were the same unit. Nothing errors when that happens --
    /// a bill of quantities simply carries a line a thousand times too large, in a
    /// column a reader has no reason to re-derive.</para>
    ///
    /// <para>Re-derived from claude/boq-accuracy-hardening, whose BoqUnitsTests.cs
    /// never landed. That branch is 1,478 commits behind and its helper names
    /// (<c>IsMassUnit</c>, <c>MassKgToRateUnit</c>) do not exist here: main solved the
    /// same problem with a different and fuller API, so the tests are rewritten
    /// against what shipped rather than cherry-picked.</para>
    /// </summary>
    public class BoqUnitsMassTests
    {
        [Theory]
        [InlineData("m²", "m2")]
        [InlineData("sqm", "m2")]
        [InlineData("M2", "m2")]
        [InlineData(" m2 ", "m2")]
        [InlineData("m³", "m3")]
        [InlineData("cum", "m3")]
        [InlineData("lm", "m")]
        [InlineData("linear-m", "m")]
        [InlineData("lin-m", "m")]
        [InlineData("kg", "kg")]
        [InlineData("kgs", "kg")]
        [InlineData("t", "tonne")]
        [InlineData("te", "tonne")]
        [InlineData("tonnes", "tonne")]
        [InlineData("no", "each")]
        [InlineData("nr", "each")]
        [InlineData("item", "each")]
        [InlineData("ea", "each")]
        public void Normalise_canonicalises_the_synonyms_the_rate_books_use(
            string input, string expected)
        {
            Assert.Equal(expected, BoqUnits.Normalise(input));
        }

        /// <summary>
        /// The whole point. If these two ever normalise to the same token again, a
        /// tonne rate and a kilogram quantity become "the same unit" and the 1000x
        /// error returns silently.
        /// </summary>
        [Fact]
        public void Tonne_and_kilogram_are_never_the_same_token()
        {
            foreach (string t in new[] { "t", "te", "tonne", "tonnes", "TONNE" })
                foreach (string k in new[] { "kg", "kgs", "KG" })
                    Assert.NotEqual(BoqUnits.Normalise(k), BoqUnits.Normalise(t));
        }

        [Theory]
        [InlineData("tonne", "kg", 1000.0)]
        [InlineData("t", "kgs", 1000.0)]
        [InlineData("kg", "tonne", 0.001)]
        [InlineData("kgs", "te", 0.001)]
        // Not a mass pair: the factor must be 1, never a silent scale.
        [InlineData("kg", "kg", 1.0)]
        [InlineData("tonne", "tonne", 1.0)]
        [InlineData("m2", "m3", 1.0)]
        [InlineData("m", "each", 1.0)]
        [InlineData("", "kg", 1.0)]
        public void MassFactor_scales_only_across_the_tonne_kilogram_boundary(
            string from, string to, double expected)
        {
            Assert.Equal(expected, BoqUnits.MassFactor(from, to), 9);
        }

        /// <summary>
        /// A quantity converted to a rate's unit and back must come home unchanged.
        /// A one-directional factor -- 1000 both ways, say -- would pass every
        /// single-step assertion above and still bill a million times over.
        /// </summary>
        [Theory]
        [InlineData("kg", "tonne", 2500.0)]
        [InlineData("tonne", "kg", 2.5)]
        public void Converting_to_the_rate_unit_and_back_is_identity(
            string a, string b, double qty)
        {
            double there = qty * BoqUnits.MassFactor(a, b);
            double back = there * BoqUnits.MassFactor(b, a);
            Assert.Equal(qty, back, 9);
        }

        /// <summary>
        /// The money property, stated as money: 2,500 kg of rebar against a rate of
        /// 4,000,000 per TONNE is 10,000,000 -- not 10,000,000,000.
        /// </summary>
        [Fact]
        public void A_kilogram_quantity_on_a_per_tonne_rate_bills_the_right_amount()
        {
            const double qtyKg = 2500.0;
            const double ratePerTonne = 4_000_000.0;

            Assert.True(BoqUnits.Align("kg", "tonne"),
                "kg and tonne must be recognised as the same dimension, or the "
                + "caller never applies MassFactor at all.");

            double qtyInRateUnits = qtyKg * BoqUnits.MassFactor("kg", "tonne");
            Assert.Equal(10_000_000.0, qtyInRateUnits * ratePerTonne, 6);
        }

        [Theory]
        [InlineData("kg", "tonne", true)]
        [InlineData("tonne", "kg", true)]
        [InlineData("t", "kgs", true)]
        [InlineData("m2", "m2", true)]
        [InlineData("sqm", "m²", true)]
        // Different dimensions must NOT align: aligning them would let an area
        // quantity be billed against a per-item rate with no factor and no warning.
        [InlineData("m2", "m3", false)]
        [InlineData("m", "each", false)]
        [InlineData("kg", "m2", false)]
        // An unknown unit is not compatible with anything but itself.
        [InlineData("bag", "kg", false)]
        [InlineData("bag", "bag", true)]
        public void Align_accepts_the_mass_pair_and_nothing_else_across_dimensions(
            string a, string b, bool expected)
        {
            Assert.Equal(expected, BoqUnits.Align(a, b));
        }

        /// <summary>
        /// An empty unit is UNKNOWN, not universal. Aligning "" with anything would
        /// let a line whose unit failed to resolve bill against any rate at all.
        /// </summary>
        [Theory]
        [InlineData("", "kg")]
        [InlineData("kg", "")]
        [InlineData("", "")]
        [InlineData(null, "kg")]
        [InlineData("kg", null)]
        public void An_unresolved_unit_aligns_with_nothing(string a, string b)
        {
            Assert.False(BoqUnits.Align(a, b));
        }

        [Fact]
        public void Normalise_survives_null_and_empty_without_inventing_a_unit()
        {
            Assert.Equal("", BoqUnits.Normalise(null));
            Assert.Equal("", BoqUnits.Normalise(""));
            Assert.Equal("", BoqUnits.Normalise("   "));
        }

        /// <summary>
        /// An unrecognised token is passed through lower-cased rather than mapped to
        /// a default. A default would make "bag" silently become "each" and bill a
        /// bag of cement as one item at the item rate.
        /// </summary>
        [Theory]
        [InlineData("BAG", "bag")]
        [InlineData("Roll", "roll")]
        [InlineData("m/s", "m/s")]
        public void An_unknown_unit_passes_through_rather_than_defaulting(
            string input, string expected)
        {
            Assert.Equal(expected, BoqUnits.Normalise(input));
        }
    }
}
