using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>DTW-43 / DTW-44: ISO sheet numbers carry the ISO level code and no suitability / revision. The plugin calls the same file.</summary>
    public class IsoSheetNumberTests
    {

        [Fact]
        public void The_iso_identifier_carries_no_suitability_or_revision()
        {
            Assert.DoesNotContain("{suit}", SheetNumberPolicy.IsoPattern);
            Assert.DoesNotContain("{rev}", SheetNumberPolicy.IsoPattern);
            Assert.EndsWith("{seq:D4}", SheetNumberPolicy.IsoPattern);
        }

        [Fact]
        public void An_iso_number_takes_the_iso_level_code_not_the_name()
        {
            var map = IsoLevelCode.BuildMap(new[]
            {
                new StoreyDatum { Name = "Level 1", ElevationMm = 0 },
                new StoreyDatum { Name = "Mezzanine", ElevationMm = 2000 },
                new StoreyDatum { Name = "Level 2", ElevationMm = 4000 },
            });
            Assert.Equal("00", SheetNumberPolicy.LevelToken(SheetNumberPolicy.IsoPattern, "Level 1", map));
            Assert.Equal("M1", SheetNumberPolicy.LevelToken(SheetNumberPolicy.IsoPattern, "Mezzanine", map));
            Assert.Equal("Level 1", SheetNumberPolicy.LevelToken("A-{lvl}-{seq:D3}", "Level 1", map));
            Assert.Null(SheetNumberPolicy.LevelToken(SheetNumberPolicy.IsoPattern, null, map));
        }

        [Fact]
        public void A_spool_sheet_takes_the_producers_iso_level_code()
        {
            // DTW-100: under an ISO-shaped pattern the spool sheet's {lvl} is the code the
            // producer gives the same level, so the two share numbers and counter buckets.
            // Any other pattern keeps the assembly's ASS_LVL_COD_TXT, as before.
            var map = IsoLevelCode.BuildMap(new[]
            {
                new StoreyDatum { Name = "Level 1", ElevationMm = 0 },
                new StoreyDatum { Name = "Mezzanine", ElevationMm = 2000 },
            });
            Assert.Equal("M1", SheetNumberPolicy.SpoolLevelToken(SheetNumberPolicy.IsoPattern, "L1M", "Mezzanine", map));
            Assert.Equal(SheetNumberPolicy.LevelToken(SheetNumberPolicy.IsoPattern, "Level 1", map),
                         SheetNumberPolicy.SpoolLevelToken(SheetNumberPolicy.IsoPattern, "GF", "Level 1", map));
            Assert.Equal("GF", SheetNumberPolicy.SpoolLevelToken("SP-{disc}-{sys}-{lvl}-{seq}", "GF", "Level 1", map));
            // No level element found: the assembly's own code stands.
            Assert.Equal("GF", SheetNumberPolicy.SpoolLevelToken(SheetNumberPolicy.IsoPattern, "GF", null, map));
            Assert.Equal("GF", SheetNumberPolicy.SpoolLevelToken(SheetNumberPolicy.IsoPattern, "GF", "  ", map));
        }

        [Fact]
        public void A_number_with_the_old_status_tail_is_read_as_its_container_number()
        {
            Assert.Equal("P-O-V-00-DR-A-0003", SheetNumberPolicy.StripStatusSuffix("P-O-V-00-DR-A-0003-S2-P01"));
            Assert.Equal("P-O-V-00-DR-A-0003", SheetNumberPolicy.StripStatusSuffix("P-O-V-00-DR-A-0003-A1-C02"));
            Assert.Equal("A-101", SheetNumberPolicy.StripStatusSuffix("A-101"));
        }
    }
}
