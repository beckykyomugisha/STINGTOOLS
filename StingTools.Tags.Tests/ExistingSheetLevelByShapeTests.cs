using System;
using System.Collections.Generic;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-210. Title-block heal picked {lvl} by the CURRENT sheet-number policy. After a
    /// switch Profile → ISO, a profile-numbered sheet ("A-Level1-003") was healed with the
    /// ISO code ("00") and its title block no longer matched its own number; after ISO →
    /// Profile, an ISO sheet got the level name. The sheet's own number decides.
    /// </summary>
    public class ExistingSheetLevelByShapeTests
    {
        private static readonly Dictionary<string, string> Map =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "Level 1", "00" }, { "Level 2", "01" } };

        [Theory]
        [InlineData("PRJ-ORG-01-00-DR-A-0003", true)]
        [InlineData("PRJ-ORG-01-00-DR-A-0003-S2-P01", true)]
        [InlineData("A-Level1-003", false)]
        [InlineData("E-101", false)]
        [InlineData("", false)]
        public void Iso_shape_is_read_off_the_number(string number, bool iso)
            => Assert.Equal(iso, SheetNumberEngine.IsIsoShapedNumber(number));

        [Fact]
        public void A_profile_numbered_sheet_keeps_the_name_after_a_switch_to_iso()
            => Assert.Equal("Level 1", SheetNumberEngine.ExistingSheetLevel("A-Level1-003", "Level 1", true, Map));

        [Fact]
        public void An_iso_numbered_sheet_keeps_the_iso_code_after_a_switch_to_profile()
            => Assert.Equal("00", SheetNumberEngine.ExistingSheetLevel("PRJ-ORG-01-00-DR-A-0003", "Level 1", true, Map));

        [Fact]
        public void An_iso_numbered_sheet_with_a_status_suffix_is_still_iso()
            => Assert.Equal("01", SheetNumberEngine.ExistingSheetLevel("PRJ-ORG-01-01-DR-A-0007-S2-P01", "Level 2", true, Map));

        [Fact]
        public void A_profile_level_value_is_left_as_it_is()
            => Assert.Equal("ZZ", SheetNumberEngine.ExistingSheetLevel("PRJ-ORG-01-00-DR-A-0003", "ZZ", false, Map));

        [Fact]
        public void No_level_stays_none()
            => Assert.Null(SheetNumberEngine.ExistingSheetLevel("A-001", null, true, Map));
    }
}
