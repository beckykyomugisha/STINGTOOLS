// ══════════════════════════════════════════════════════════════════════════
//  UnitSuffixTests.cs — the unit a STING parameter's name promises.
//
//  NativeParamMapper wrote Revit internal units (feet, ft³/s, ft/s, and
//  1 V = 10.7639) into TEXT / unitless targets whose names promise SI —
//  HVC_AIRFLOW_LPS, HVC_DCT_WIDTH_MM, HVC_VEL_MPS, ELC_CKT_PWR_KW. The mapper
//  now converts to the unit this parser reads off the name, so a wrong parse
//  is a wrong number in the model.
// ══════════════════════════════════════════════════════════════════════════
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class UnitSuffixTests
    {
        [Theory]
        [InlineData("HVC_AIRFLOW_LPS", "lps")]
        [InlineData("HVC_DCT_FLW_CFM", "cfm")]
        [InlineData("HVC_DCT_WIDTH_MM", "mm")]
        [InlineData("HVC_VEL_MPS", "mps")]
        [InlineData("HVC_PRESSURE_DROP_PA", "pa")]
        [InlineData("PLM_PRES_STATIC_KPA", "kpa")]
        [InlineData("ELC_CKT_PWR_KW", "kw")]
        [InlineData("ELC_PNL_CONNECTED_LOAD_KW", "kw")]
        [InlineData("ELC_CKT_VLT_V", "v")]
        [InlineData("ASS_AREA_M2", "m2")]
        [InlineData("ASS_VOLUME_M3", "m3")]
        [InlineData("ASS_LENGTH_M", "m")]
        [InlineData("hvc_airflow_lps", "lps")]
        public void Reads_the_unit_off_the_name(string name, string expected)
        {
            Assert.Equal(expected, UnitSuffix.Of(name));
        }

        [Theory]
        [InlineData("ASS_DESCRIPTION_TXT")]
        [InlineData("ELC_CKT_NR")]
        [InlineData("ELC_CKT_PHASE_COUNT_NR")]
        [InlineData("")]
        [InlineData(null)]
        public void Returns_null_when_the_name_carries_no_unit(string name)
        {
            Assert.Null(UnitSuffix.Of(name));
        }

        [Fact]
        public void Longer_suffixes_win_over_their_tails()
        {
            // "_M2" must not be read as "_M", "_KPA" not as "_PA", "_KVA" not as "_A".
            Assert.Equal("m2", UnitSuffix.Of("X_M2"));
            Assert.Equal("kpa", UnitSuffix.Of("X_KPA"));
            Assert.Equal("kva", UnitSuffix.Of("X_KVA"));
            Assert.Equal("mm", UnitSuffix.Of("X_MM"));
        }

        [Theory]
        [InlineData("kw", true)]
        [InlineData("kva", true)]
        [InlineData("w", false)]
        [InlineData("v", false)]
        [InlineData(null, false)]
        public void Kilo_units_are_flagged(string suffix, bool kilo)
        {
            Assert.Equal(kilo, UnitSuffix.IsKilo(suffix));
        }
    }
}
