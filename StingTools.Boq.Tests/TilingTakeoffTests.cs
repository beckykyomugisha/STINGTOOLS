using System.Linq;
using StingTools.BOQ.Takeoff;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED-3 — tiling.
    ///
    /// The first attempt matched a whole ELEMENT from the unit table and priced
    /// entire floors as tiling; it was withdrawn. Tiling is measured from the
    /// host's own finish LAYER instead, so a floor that carries no tiled layer
    /// produces no tiling at all rather than a confident wrong number.
    ///
    /// These pin the engine half. The layer READ is Revit-side and cannot run
    /// here — see MATSCHED-3's remaining note.
    /// </summary>
    public class TilingTakeoffTests
    {
        private static TiledFinishInput Floor(double area) => new TiledFinishInput
        {
            AreaM2 = area, IsWall = false, TileLabel = "Ceramic Tile",
            AdhesiveKgPerM2 = 4.5, GroutKgPerM2 = 0.5
        };

        [Fact]
        public void Tiling_Emits_Tile_Adhesive_And_Grout()
        {
            var lines = CompoundTakeoff.TiledFinish(Floor(100));

            Assert.Equal(100, lines.Single(l => l.Kind == "floor_tile").Quantity);
            Assert.Equal(450, lines.Single(l => l.Kind == "tile_adhesive").Quantity);
            Assert.Equal(50, lines.Single(l => l.Kind == "tile_grout").Quantity);
        }

        [Fact]
        public void Wall_And_Floor_Tiling_Are_Different_Commodities()
        {
            // Different products at different rates, bought in different box
            // sizes — merging them would average two prices into one wrong one.
            var f = Floor(10);
            var w = Floor(10); w.IsWall = true;

            Assert.Contains(CompoundTakeoff.TiledFinish(f), l => l.Kind == "floor_tile");
            Assert.Contains(CompoundTakeoff.TiledFinish(w), l => l.Kind == "wall_tile");
        }

        [Fact]
        public void No_Area_Means_No_Tiling()
        {
            Assert.Empty(CompoundTakeoff.TiledFinish(Floor(0)));
            Assert.Empty(CompoundTakeoff.TiledFinish(Floor(-5)));
        }

        [Fact]
        public void Quantities_Are_Net_Of_Wastage()
        {
            // Tiles carry the largest allowance in the schedule (cuts at every
            // edge and penetration), which is exactly why it belongs to the
            // supplier-unit rule where it is visible and arguable — not baked in
            // here, where it would be applied twice as it was for blocks.
            var lines = CompoundTakeoff.TiledFinish(Floor(100));

            Assert.Equal(100, lines.Single(l => l.Kind == "floor_tile").Quantity);
        }

        [Fact]
        public void Missing_Coverage_Figures_Emit_Tiles_But_No_Consumables()
        {
            // A material with no row in MATERIAL_LOOKUP must not silently invent
            // adhesive from a zero rate.
            var t = Floor(50); t.AdhesiveKgPerM2 = 0; t.GroutKgPerM2 = 0;

            var lines = CompoundTakeoff.TiledFinish(t);

            Assert.Single(lines);
            Assert.Equal("floor_tile", lines[0].Kind);
        }

        // ── the paint interaction ────────────────────────────────────────

        private static MasonryWallInput Wall(int plasterFaces, int tiledFaces) => new MasonryWallInput
        {
            FaceAreaM2 = 100, PlasterFaces = plasterFaces, TiledFaces = tiledFaces,
            PlasterThicknessM = 0.012, IsExteriorWall = false
        };

        [Fact]
        public void A_Tiled_Face_Is_Not_Painted()
        {
            // Plastered both sides, tiled one: 100 m2 of paint, not 200.
            var lines = CompoundTakeoff.MasonryWall(Wall(2, 1));

            Assert.Equal(100, lines.Single(l => l.Kind == "paint_interior").Quantity);
            // The backing plaster still covers BOTH faces — a tiled wall is
            // plastered before it is tiled.
            Assert.Equal(200, lines.Single(l => l.Kind == "plaster").Quantity);
        }

        [Fact]
        public void A_Fully_Tiled_Wall_Emits_No_Paint_Row_At_All()
        {
            var lines = CompoundTakeoff.MasonryWall(Wall(2, 2));

            Assert.DoesNotContain(lines, l => l.Kind == "paint_interior" || l.Kind == "paint_exterior");
        }

        [Fact]
        public void More_Tiled_Faces_Than_Plastered_Never_Yields_Negative_Paint()
        {
            var lines = CompoundTakeoff.MasonryWall(Wall(1, 2));

            Assert.DoesNotContain(lines, l => l.Kind.StartsWith("paint"));
            Assert.All(lines, l => Assert.True(l.Quantity >= 0));
        }

        [Fact]
        public void An_Untiled_Wall_Paints_Exactly_As_Before()
        {
            var lines = CompoundTakeoff.MasonryWall(Wall(2, 0));

            Assert.Equal(200, lines.Single(l => l.Kind == "paint_interior").Quantity);
        }
    }

    /// <summary>
    /// MAT-SCHED-3 — the seam between the tiling code and the three data files
    /// it depends on. Each is valid on its own; only a comparison catches a key
    /// the builder composes one way and the CSV spells another, which fails
    /// silently at runtime as a zero coverage and no adhesive row.
    /// </summary>
    public class TilingShippedDataTests
    {
        private static string DataFile(string name) =>
            System.IO.Path.Combine(System.AppContext.BaseDirectory, "Data", name);

        private static System.Collections.Generic.Dictionary<string, StingTools.UI.MaterialLookupRow> Lookup() =>
            StingTools.UI.MaterialLookupParser.Parse(System.IO.File.ReadAllLines(DataFile("MATERIAL_LOOKUP.csv")));

        [Theory]
        [InlineData("TILE CERAMIC")]
        [InlineData("TILE PORCELAIN")]
        [InlineData("TILE STONE")]
        [InlineData("TILE TERRAZZO")]
        [InlineData("TILE MOSAIC")]
        [InlineData("TILE DEFAULT")]
        public void Every_Tile_Key_The_Builder_Composes_Resolves_To_Real_Coverages(string key)
        {
            // The builder asks for exactly these keys — "TILE " + TileKeyFor(material).
            // A typo here is not a compile error and not a load error; it is a
            // zero, and a zero silently drops the adhesive and grout rows.
            var row = Lookup()[key];

            Assert.True(row.Properties["ADHESIVE_KG_PER_M2"] > 0, key + " has no adhesive rate");
            Assert.True(row.Properties["GROUT_KG_PER_M2"] > 0, key + " has no grout rate");
        }

        [Fact]
        public void Adhesive_Coverage_Stays_In_A_Believable_Band()
        {
            // 2-10 kg/m2 spans a 4mm notch for mosaic to a 10mm notch for large
            // format. A figure outside it is a data-entry slip, not a product.
            foreach (var key in new[] { "TILE CERAMIC", "TILE PORCELAIN", "TILE STONE", "TILE TERRAZZO", "TILE MOSAIC", "TILE DEFAULT" })
            {
                double kg = Lookup()[key].Properties["ADHESIVE_KG_PER_M2"];
                Assert.InRange(kg, 2.0, 10.0);
            }
        }

        [Fact]
        public void Every_Tiling_Kind_Routes_To_A_Finishes_Stage()
        {
            // Tiling is a finish. The regression this guards is real and shipped
            // once: category-matched finish rules inherited the ELEMENT's stage
            // and filed wall paint under SUPERSTRUCTURE.
            var lib = Newtonsoft.Json.JsonConvert.DeserializeObject<StingTools.Core.MaterialSchedule.StageLibrary>(
                System.IO.File.ReadAllText(DataFile("STING_MATERIAL_STAGES.json")));
            var ix = StingTools.Core.MaterialSchedule.StageIndex.Build(lib.Stages, lib.DefaultStageId);

            foreach (string kind in new[] { "floor_tile", "wall_tile", "tile_adhesive", "tile_grout" })
                Assert.Equal("finishes", ix.Resolve(kind, "Floors", ""));
        }

        [Fact]
        public void Every_Tiling_Kind_Has_A_Supplier_Rule_And_A_Rate()
        {
            var units = Newtonsoft.Json.JsonConvert.DeserializeObject<StingTools.Core.MaterialSchedule.SupplierUnitTable>(
                System.IO.File.ReadAllText(DataFile("STING_SUPPLIER_UNITS.json")));
            var rates = StingTools.Core.MaterialSchedule.CommodityRateResolver.ParseCsv(
                System.IO.File.ReadAllLines(DataFile("STING_COMMODITY_RATES.csv")), out _);
            var resolver = new StingTools.Core.MaterialSchedule.CommodityRateResolver(rates, null);

            foreach (string kind in new[] { "floor_tile", "wall_tile", "tile_adhesive", "tile_grout" })
            {
                var rule = units.ResolveByKind(kind);
                Assert.True(rule != null, "no supplier-unit rule matches kind " + kind);
                Assert.True(resolver.Resolve(rule.CommodityKey).RateUGX > 0,
                    "commodity " + rule.CommodityKey + " has no baseline rate");
            }
        }

        [Fact]
        public void A_Tile_Rule_Buys_By_The_Unit_It_Is_Measured_In()
        {
            // The unit guard added in MATSCHED-10 REFUSES to convert when a
            // rule's sourceUnit disagrees with the measured unit — so a tile
            // rule declaring anything but m2 would silently stop converting and
            // print bare square metres instead of boxes.
            var units = Newtonsoft.Json.JsonConvert.DeserializeObject<StingTools.Core.MaterialSchedule.SupplierUnitTable>(
                System.IO.File.ReadAllText(DataFile("STING_SUPPLIER_UNITS.json")));

            Assert.Equal("m2", units.ResolveByKind("floor_tile").SourceUnit);
            Assert.Equal("m2", units.ResolveByKind("wall_tile").SourceUnit);
            Assert.Equal("kg", units.ResolveByKind("tile_adhesive").SourceUnit);
            Assert.Equal("kg", units.ResolveByKind("tile_grout").SourceUnit);
        }

        [Fact]
        public void Skirting_Routes_To_Finishes_And_Is_Bought_By_The_Metre()
        {
            // Skirting comes from the ROOM source and is measured in linear
            // metres. A rule declaring anything else would trip the unit guard
            // and print bare metres instead of a priced run.
            var lib = Newtonsoft.Json.JsonConvert.DeserializeObject<StingTools.Core.MaterialSchedule.StageLibrary>(
                System.IO.File.ReadAllText(DataFile("STING_MATERIAL_STAGES.json")));
            var ix = StingTools.Core.MaterialSchedule.StageIndex.Build(lib.Stages, lib.DefaultStageId);
            Assert.Equal("finishes", ix.Resolve("skirting", "Rooms", ""));

            var units = Newtonsoft.Json.JsonConvert.DeserializeObject<StingTools.Core.MaterialSchedule.SupplierUnitTable>(
                System.IO.File.ReadAllText(DataFile("STING_SUPPLIER_UNITS.json")));
            var rule = units.ResolveByKind("skirting");
            Assert.NotNull(rule);
            Assert.Equal("m", rule.SourceUnit);
            Assert.False(rule.RoundUpToWhole);   // you can buy 12.4 m of skirting
        }

    }

}
