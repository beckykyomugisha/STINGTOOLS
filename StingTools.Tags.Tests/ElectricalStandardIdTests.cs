using System;
using StingTools.Standards;
using StingTools.Standards.NEC2023;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT-7 — the electrical standard vocabulary and the sizing-support policy.
    ///
    /// <para>The row said "NEC2023 is unreachable". It was, and the reason was a
    /// vocabulary split nobody had written down: the panel emits <c>"NEC2023"</c> and
    /// the engine tested <c>standard == "NEC"</c>. That mattered more than it looks,
    /// because the obvious repair was the worst available change — the only two places
    /// that tested for <c>"NEC"</c> were a breaker lookup and a routine that renamed a
    /// BS 7671-sized mm² conductor to the nearest AWG. Matching the token would have
    /// switched on an AWG designation for a conductor chosen from BS 7671 Appendix 4.</para>
    /// </summary>
    public class ElectricalStandardIdTests
    {
        // ── One vocabulary ───────────────────────────────────────────────────────

        /// <summary>The panel's own combo and radio tags, taken verbatim from
        /// StingElectricalPanel.xaml. These are the strings that actually arrive.</summary>
        [Theory]
        [InlineData("BS7671", ElectricalStandardId.Bs7671)]
        [InlineData("NEC2023", ElectricalStandardId.Nec2023)]
        [InlineData("IEC60364", ElectricalStandardId.Iec60364)]
        [InlineData("ASNZS", ElectricalStandardId.AsNzs3000)]
        public void PanelTagsNormaliseToCanonicalIds(string panelTag, string expected)
            => Assert.Equal(expected, ElectricalStandardId.Normalise(panelTag));

        /// <summary>The engine's legacy token still resolves, so an older snapshot or a
        /// hand-set parameter is not silently re-read as BS 7671.</summary>
        [Fact]
        public void TheEnginesLegacyNecTokenStillResolves()
            => Assert.Equal(ElectricalStandardId.Nec2023, ElectricalStandardId.Normalise("NEC"));

        /// <summary>THE BUG, stated as a test: the panel tag and the engine token must
        /// land on the same id. Before KUT-7 they did not, and selecting NEC 2023
        /// produced a BS 7671 answer.</summary>
        [Fact]
        public void PanelTagAndEngineTokenAgree()
            => Assert.Equal(ElectricalStandardId.Normalise("NEC"), ElectricalStandardId.Normalise("NEC2023"));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("BS9999")]
        public void UnknownOrBlankFallsBackToTheDefault(string raw)
            => Assert.Equal(ElectricalStandardId.Default, ElectricalStandardId.Normalise(raw));

        /// <summary>Normalise alone cannot tell "unrecognised" from "BS 7671", because
        /// both answer BS7671. IsRecognised is how a caller tells them apart.</summary>
        [Fact]
        public void IsRecognisedSeparatesAFallbackFromARealChoice()
        {
            Assert.True(ElectricalStandardId.IsRecognised("BS7671"));
            Assert.True(ElectricalStandardId.IsRecognised("NEC2023"));
            Assert.False(ElectricalStandardId.IsRecognised("BS9999"));
            Assert.False(ElectricalStandardId.IsRecognised(null));
        }

        [Fact]
        public void MatchingIsCaseAndWhitespaceInsensitive()
        {
            Assert.Equal(ElectricalStandardId.Nec2023, ElectricalStandardId.Normalise("  nec2023 "));
            Assert.Equal(ElectricalStandardId.AsNzs3000, ElectricalStandardId.Normalise("as/nzs 3000"));
        }

        // ── The refusal, which is the safety-critical half ───────────────────────

        /// <summary>AS/NZS 3000 must REFUSE. Its AS/NZS 3008.1.1 tables are not in this
        /// tree; sizing on the BS 7671 tables and printing the result under an AS/NZS
        /// label is a wrong cable size wearing a standard's name, and nothing downstream
        /// can tell the difference.</summary>
        [Fact]
        public void AsNzsRefusesConductorSizingAndSaysWhy()
        {
            bool ok = ElectricalStandardId.SupportsConductorSizing(
                ElectricalStandardId.AsNzs3000, out string basis, out string refusal);

            Assert.False(ok);
            Assert.Null(basis);
            Assert.False(string.IsNullOrWhiteSpace(refusal));
            // The refusal must name the standard and say what to do instead — a bare
            // "not supported" leaves the engineer with no next step.
            Assert.Contains("AS/NZS", refusal);
            Assert.Contains("3008", refusal);
        }

        [Theory]
        [InlineData(ElectricalStandardId.Bs7671)]
        [InlineData(ElectricalStandardId.Nec2023)]
        [InlineData(ElectricalStandardId.Iec60364)]
        public void SupportedStandardsCarryAClauseLevelBasis(string id)
        {
            bool ok = ElectricalStandardId.SupportsConductorSizing(id, out string basis, out string refusal);
            Assert.True(ok);
            Assert.Null(refusal);
            Assert.False(string.IsNullOrWhiteSpace(basis));
        }

        /// <summary>Each supported standard's basis must cite ITS OWN code, not another's.
        /// A basis string that named BS 7671 under an NEC id would be the same
        /// substitution this gap exists to stop, just in prose.</summary>
        [Fact]
        public void EachBasisCitesItsOwnCode()
        {
            ElectricalStandardId.SupportsConductorSizing(ElectricalStandardId.Nec2023, out string nec, out _);
            Assert.Contains("310.16", nec);
            Assert.Contains("240.6", nec);
            Assert.DoesNotContain("BS 7671", nec);

            ElectricalStandardId.SupportsConductorSizing(ElectricalStandardId.Bs7671, out string bs, out _);
            Assert.Contains("BS 7671", bs);

            // IEC is the one place a cross-reference is legitimate, and it is stated
            // rather than hidden: BS 7671 IS the UK implementation of IEC 60364, and the
            // Appendix 4 factors are harmonised with IEC 60364-5-52 Annex B.
            ElectricalStandardId.SupportsConductorSizing(ElectricalStandardId.Iec60364, out string iec, out _);
            Assert.Contains("IEC 60364", iec);
        }

        /// <summary>Only NEC reports in AWG. This drives how a size is PRINTED and must
        /// never drive how one is chosen.</summary>
        [Fact]
        public void OnlyNecUsesTheAwgSeries()
        {
            Assert.True(ElectricalStandardId.UsesAwgSeries("NEC2023"));
            Assert.False(ElectricalStandardId.UsesAwgSeries("BS7671"));
            Assert.False(ElectricalStandardId.UsesAwgSeries("IEC60364"));
            Assert.False(ElectricalStandardId.UsesAwgSeries("ASNZS"));
        }

        // ── The NEC tables the sizer now routes to ───────────────────────────────

        /// <summary>Spot values from NEC 2023 Table 310.16, 75 °C copper column. These are
        /// the numbers the cable sizer now depends on; if the table is edited, this is
        /// where it shows.</summary>
        [Theory]
        [InlineData("14", 20)]
        [InlineData("12", 25)]
        [InlineData("10", 35)]
        [InlineData("8", 50)]
        [InlineData("6", 65)]
        [InlineData("4/0", 230)]
        public void Nec310_16Copper75CSpotValues(string size, int expected)
            => Assert.Equal(expected, NECStandards.GetConductorAmpacity(size, ConductorMaterial.Copper, 75));

        /// <summary>240.4(D) — the small-conductor rule caps 14/12/10 AWG below their
        /// tabulated ampacity. Without it a 12 AWG conductor would take a 25 A breaker.</summary>
        [Theory]
        [InlineData("14", 15)]
        [InlineData("12", 20)]
        [InlineData("10", 30)]
        public void Nec240_4DCapsTheSmallConductors(string size, int expectedMax)
            => Assert.Equal(expectedMax, NECStandards.GetMaximumBreakerSize(size));

        /// <summary>240.6(A) standard ratings — the sizer must land on one of these, not
        /// on an arbitrary amperage.</summary>
        [Theory]
        [InlineData(14.0, 15)]
        [InlineData(16.0, 20)]
        [InlineData(21.0, 25)]
        [InlineData(95.0, 100)]
        public void Nec240_6AStandardRatings(double required, int expected)
            => Assert.Equal(expected, NECStandards.GetStandardBreakerSize(required));

        /// <summary>310.15(B)(1) ambient correction and 310.15(C)(1) bundling adjustment
        /// both DERATE. A correction that increased ampacity at 40 °C would size a
        /// conductor too small.</summary>
        [Fact]
        public void NecCorrectionsDerate()
        {
            double baseA = NECStandards.GetConductorAmpacity("6", ConductorMaterial.Copper, 75);
            Assert.True(NECStandards.ApplyTemperatureCorrection(baseA, 40) < baseA);
            Assert.True(NECStandards.ApplyBundlingAdjustment(baseA, 6) < baseA);
            // At or below 3 current-carrying conductors there is no adjustment.
            Assert.Equal(baseA, NECStandards.ApplyBundlingAdjustment(baseA, 3));
        }
    }
}
