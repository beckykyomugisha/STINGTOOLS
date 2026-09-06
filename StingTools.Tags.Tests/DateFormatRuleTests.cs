using StingTools.Core.Validation;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// G2 — the BEP mandates YYYY-MM-DD for asset dates and nothing enforced it.
    ///
    /// Every date parameter in the registry is TEXT, so "12/03/2027",
    /// "12 Mar 2027" and "2027-03-12" all pass a completeness check and all look
    /// correct in a schedule. The divergence surfaces at close-out, when the
    /// handover dataset is assembled and the dates cannot be sorted or compared
    /// — after the installer has left site.
    ///
    /// The half of this worth pinning in a test is the SCOPE, not the regex. A
    /// rule matching every *_DATE_TXT would flag the four title-block parameters
    /// that are documented as DD-Mon-YYYY on purpose, reporting correct data as
    /// broken on every sheet with no remedy available — the same failure as
    /// requiring a parameter on a category it is not bound to.
    /// </summary>
    public class DateFormatRuleTests
    {
        [Theory]
        [InlineData("2027-03-12")]
        [InlineData("2020-02-29")]   // leap year
        [InlineData("1999-12-31")]
        [InlineData("2027-01-01")]
        public void AcceptsWellFormedIsoDates(string value)
        {
            Assert.True(DateFormatRule.IsConforming(value));
            Assert.Null(DateFormatRule.Explain("ASS_INSTALLATION_DATE_TXT", value));
        }

        [Theory]
        [InlineData("12/03/2027")]      // the convention this exists to catch
        [InlineData("03/12/2027")]      // and its ambiguous twin
        [InlineData("12 Mar 2027")]
        [InlineData("12-Mar-2027")]
        [InlineData("2027/03/12")]
        [InlineData("27-03-12")]        // two-digit year
        [InlineData("2027-3-12")]       // unpadded
        [InlineData("2027-03-12T00:00:00Z")]
        [InlineData("soon")]
        public void RejectsEveryOtherConvention(string value)
        {
            Assert.False(DateFormatRule.IsConforming(value));
            string why = DateFormatRule.Explain("ASS_INSTALLATION_DATE_TXT", value);
            Assert.NotNull(why);
            Assert.Contains(value.Trim(), why);
            Assert.Contains(DateFormatRule.Format, why);
        }

        [Theory]
        [InlineData("2027-02-30")]   // shape is right, the calendar is not
        [InlineData("2021-02-29")]   // not a leap year
        [InlineData("2027-13-01")]
        [InlineData("2027-00-10")]
        [InlineData("2027-04-31")]
        public void RejectsImpossibleDatesThatMatchTheShape(string value)
        {
            // A value that parses to nothing is exactly as unusable at handover
            // as one in the wrong format, so shape alone is not enough.
            Assert.False(DateFormatRule.IsConforming(value));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void EmptyIsConformingBecauseAbsenceIsADifferentDefect(string value)
        {
            // The caller already reports a missing required value. Counting one
            // empty cell as both missing and malformed would double the
            // non-conformance figure the monthly report is read for.
            Assert.True(DateFormatRule.IsConforming(value));
        }

        [Theory]
        [InlineData("ASS_INSTALLATION_DATE_TXT")]
        [InlineData("MNT_WARRANTY_EXPIRY_TXT")]
        [InlineData("COMM_DATE_TXT")]
        [InlineData("ASS_INST_DATE_TXT")]        // deprecated, still bound in live models
        [InlineData("COM_INSTALL_DATE_TXT")]     // legacy COBie alias
        public void CoversTheAssetDatesTheCloseOutGateRequires(string param)
        {
            Assert.True(DateFormatRule.IsDateParam(param));
        }

        [Theory]
        [InlineData("PRJ_TB_DATE_APVD_TXT")]
        [InlineData("PRJ_TB_DATE_CHECKED_TXT")]
        [InlineData("PRJ_TB_DATE_DRAWN_TXT")]
        [InlineData("PRJ_TB_REVISION_DATE_TXT")]
        public void DoesNotTouchTitleBlockDatesThatAreDeliberatelyNotIso(string param)
        {
            // These are documented as DD-Mon-YYYY. A name-pattern rule would
            // match them and fail every sheet in the project for holding exactly
            // what it was told to hold.
            Assert.False(DateFormatRule.IsDateParam(param));
            Assert.False(DateFormatRule.IsConforming("12-Mar-2027"));   // and the value really is non-ISO,
                                                                       // so only the scope is protecting them
        }

        [Theory]
        [InlineData("ASS_TAG_1_TXT")]
        [InlineData("ASS_SUPPLIER_TXT")]
        [InlineData("")]
        [InlineData(null)]
        public void IgnoresParametersThatAreNotDates(string param)
        {
            Assert.False(DateFormatRule.IsDateParam(param));
        }

        [Fact]
        public void ParameterNameMatchingIsCaseInsensitiveAndTrimmed()
        {
            Assert.True(DateFormatRule.IsDateParam("ass_installation_date_txt"));
            Assert.True(DateFormatRule.IsDateParam("  ASS_INSTALLATION_DATE_TXT  "));
        }
    }
}
