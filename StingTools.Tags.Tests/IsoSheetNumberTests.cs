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
    }
}
