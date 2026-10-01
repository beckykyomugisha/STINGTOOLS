using System.Collections.Generic;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>DTW-117 — on a multi-building job {vol} carries the building, not the
    /// drawing type's fixed "01". The plugin's DrawingTokenContext calls these.</summary>
    public class SheetVolumeFromLocTests
    {
        [Fact]
        public void The_ISO_pattern_names_vol()
        {
            Assert.True(SheetNumberPolicy.PatternUsesVolume(SheetNumberPolicy.IsoPattern));
            Assert.False(SheetNumberPolicy.PatternUsesVolume("{disc}-{lvl}-{seq:D3}"));
            Assert.False(SheetNumberPolicy.PatternUsesVolume(null));
        }

        [Fact]
        public void A_mapped_building_takes_its_project_volume()
        {
            var map = new Dictionary<string, string> { { "BLDA", "01" }, { "BLDB", "02" } };
            Assert.Equal("02", SheetNumberPolicy.VolumeForLoc("bldb", map));
        }

        [Fact]
        public void An_unmapped_building_takes_its_LOC_code_cut_to_an_ISO_field()
        {
            Assert.Equal("BLDC", SheetNumberPolicy.VolumeForLoc("BLDC", new Dictionary<string, string> { { "BLDA", "01" } }));
            Assert.Equal("BLOCKA", SheetNumberPolicy.VolumeForLoc("Block-A", null));   // a '-' would split the field
            Assert.Equal("B2", SheetNumberPolicy.VolumeForLoc("b.2", null));
        }

        [Fact]
        public void No_building_means_no_volume()
        {
            Assert.Null(SheetNumberPolicy.VolumeForLoc(null, null));
            Assert.Null(SheetNumberPolicy.VolumeForLoc("  ", null));
            Assert.Null(SheetNumberPolicy.VolumeForLoc("--", null));
        }

        [Fact]
        public void The_map_file_reads_at_the_root_or_under_volumes()
        {
            var a = SheetNumberPolicy.ParseVolumeMap("{\"volumes\": {\"BLDA\": \"01\", \"BLDB\": \"02\"}}", out var e1);
            Assert.Null(e1);
            Assert.Equal("02", a["bldb"]);
            var b = SheetNumberPolicy.ParseVolumeMap("{\"BLDA\": \"V1\"}", out var e2);
            Assert.Null(e2);
            Assert.Equal("V1", b["BLDA"]);
        }

        [Fact]
        public void A_broken_map_file_is_an_error_not_an_empty_map()
        {
            SheetNumberPolicy.ParseVolumeMap("{ not json", out var err);
            Assert.NotNull(err);
            SheetNumberPolicy.ParseVolumeMap("{\"BLDA\": {\"x\": 1}}", out var err2);
            Assert.NotNull(err2);
        }
    }
}
