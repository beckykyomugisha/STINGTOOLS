using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED-3 — the second finish source.
    ///
    /// The layer source found ONE finish layer across ten wall/floor types on a
    /// real model, and it was Gypsum Wall Board. Most architects record finishes
    /// on the ROOM instead. These pin the two Revit-free halves: what a finish
    /// NAME means, and what the scan reports when nothing is found.
    /// </summary>
    public class FinishTextClassifierTests
    {
        [Theory]
        [InlineData("Ceramic Tile")]
        [InlineData("Porcelain tiles 600x600")]
        [InlineData("TERRAZZO")]
        [InlineData("Granite slab")]
        [InlineData("Mosaic")]
        [InlineData("Vitrified tile")]
        public void Tiled_Finishes_Are_Recognised(string name) => Assert.True(FinishTextClassifier.IsTile(name));

        [Theory]
        [InlineData("Gypsum Wall Board")]   // the one this model actually had
        [InlineData("Screed")]
        [InlineData("Paint")]
        [InlineData("Timber flooring")]
        [InlineData("")]
        [InlineData(null)]
        public void Non_Tiled_Finishes_Are_Not(string name) => Assert.False(FinishTextClassifier.IsTile(name));

        [Theory]
        [InlineData("Carpet tile")]      // laid, not bedded and grouted
        [InlineData("Vinyl tile")]
        [InlineData("LVT")]
        [InlineData("Ceiling tile")]     // not a floor or wall surface
        [InlineData("Roof tile")]        // a mis-typed schedule, not a floor
        [InlineData("Acoustic tile")]
        public void Things_Named_Tile_That_Are_Not_Ceramic_Are_Excluded(string name)
        {
            // Each of these contains "tile" and would otherwise be priced with
            // adhesive and grout at ceramic rates — wrong in both quantity and
            // rate. A narrow classifier beats a confident wrong number.
            Assert.False(FinishTextClassifier.IsTile(name));
        }

        [Theory]
        [InlineData("Porcelain", "PORCELAIN")]
        [InlineData("Vitrified tile", "PORCELAIN")]
        [InlineData("Terrazzo tile", "TERRAZZO")]
        [InlineData("Marble", "STONE")]
        [InlineData("Granite", "STONE")]
        [InlineData("Glass mosaic", "MOSAIC")]
        [InlineData("Ceramic Tile", "CERAMIC")]
        [InlineData("Something else", "CERAMIC")]
        public void Tile_Keys_Map_To_The_MATERIAL_LOOKUP_Rows(string name, string key)
            => Assert.Equal(key, FinishTextClassifier.TileKey(name));

        [Theory]
        [InlineData("None")]
        [InlineData("none")]
        [InlineData("N/A")]
        [InlineData("n/a")]
        [InlineData("NIL")]
        [InlineData("-")]
        [InlineData("---")]
        [InlineData("TBC")]
        [InlineData("x")]
        [InlineData("  ")]
        [InlineData(null)]
        public void Placeholder_Finish_Text_Is_Not_A_Real_Finish(string name)
        {
            // Rooms carry these literals far more often than they carry nothing,
            // and each would otherwise mint a skirting run around a room that
            // has none.
            Assert.False(FinishTextClassifier.IsRealFinish(name));
        }

        [Theory]
        [InlineData("Ceramic Tile")]
        [InlineData("Timber skirting")]
        [InlineData("Cement skirting 100mm")]
        public void A_Named_Finish_Is_Real(string name) => Assert.True(FinishTextClassifier.IsRealFinish(name));
    }

    public class RoomFinishTallyTests
    {
        [Fact]
        public void No_Placed_Rooms_Reports_Nothing()
        {
            // Silence, not an invented "0 of 0" that would read as a finding.
            Assert.Null(new RoomFinishTally().Summary());
        }

        [Fact]
        public void Rooms_Without_Finish_Text_Say_So_And_Name_Both_Fixes()
        {
            var t = new RoomFinishTally { RoomsRead = 24 };

            string s = t.Summary();

            Assert.Contains("24 placed room(s) read", s);
            Assert.Contains("carry no finish text", s);
            Assert.Contains("tiled finish layer", s);   // the other route out
        }

        [Fact]
        public void Unrecognised_Floor_Finishes_Are_Named()
        {
            var t = new RoomFinishTally { RoomsRead = 12 };
            t.UnrecognisedFloorFinishes.Add("Screed");
            t.UnrecognisedFloorFinishes.Add("Timber flooring");

            string s = t.Summary();

            Assert.Contains("Screed", s);
            Assert.Contains("Timber flooring", s);
        }

        [Fact]
        public void Suppression_By_The_Layer_Source_Is_Stated_Not_Silent()
        {
            // Otherwise a model whose types ARE tiled shows zero room tiling and
            // looks broken, when it is the double-count guard doing its job.
            var t = new RoomFinishTally
            {
                RoomsRead = 20, SkirtingRuns = 6, TilingSuppressedByLayerSource = true
            };

            string s = t.Summary();

            Assert.Contains("NOT measured", s);
            Assert.Contains("price the same surface twice", s);
            Assert.Contains("Skirting still comes from the rooms", s);
        }

        [Fact]
        public void A_Working_Scan_Reports_Counts_Without_Advice()
        {
            var t = new RoomFinishTally { RoomsRead = 20, FloorsTiled = 7, WallsTiled = 3, SkirtingRuns = 12 };

            string s = t.Summary();

            Assert.Contains("7 name a tiled floor", s);
            Assert.Contains("3 a tiled wall", s);
            Assert.Contains("12 a skirting", s);
            Assert.DoesNotContain("carry no finish text", s);
        }

        [Fact]
        public void Skirting_Alone_Is_Not_Reported_As_Nothing_Found()
        {
            // Skirting is measured even when no tiling is — a room with only a
            // Base Finish is a working scan, not an empty one.
            var t = new RoomFinishTally { RoomsRead = 20, SkirtingRuns = 12 };

            Assert.DoesNotContain("carry no finish text", t.Summary());
        }

        [Fact]
        public void Reset_Clears_Everything()
        {
            var t = new RoomFinishTally { RoomsRead = 5, FloorsTiled = 2, TilingSuppressedByLayerSource = true };
            t.UnrecognisedFloorFinishes.Add("Screed");

            t.Reset();

            Assert.Null(t.Summary());
            Assert.False(t.TilingSuppressedByLayerSource);
            Assert.Empty(t.UnrecognisedFloorFinishes);
        }
    }
}
