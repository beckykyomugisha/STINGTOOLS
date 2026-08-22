using System.Linq;
using StingTools.BOQ.Takeoff;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — painted area is a real take-off quantity, not a unit-table
    /// guess. It is the PLASTERED FACE area — face area × plastered faces —
    /// which the engine already derives to size plaster volume. Emitting it as
    /// its own constituent gives paint a genuine constituent kind, so it routes
    /// and converts exactly like cement, with none of the whole-element hazard
    /// of matching a Walls row by category.
    ///
    /// Exterior and interior are separated because they are different products
    /// at different prices — weather-guard vs silk — and the wall type already
    /// knows which it is.
    /// </summary>
    public class PaintTakeoffTests
    {
        private static MasonryWallInput Wall(int faces = 2, bool exterior = false) => new MasonryWallInput
        {
            FaceAreaM2 = 20.0,
            IsBrick = false,
            UnitsPerM2 = 12.5,
            UnitWastePct = 5,
            PlasterFaces = faces,
            PlasterThicknessM = 0.013,
            PlasterWastePct = 20,
            MortarRatioM3PerM2 = 0.011,
            MortarCementBagsPerM3 = 9,
            MortarSandRatio = 1.25,
            PlasterCementBagsPerM3 = 9,
            PlasterSandRatio = 1.25,
            IsRcWall = false,
            IsExteriorWall = exterior
        };

        private static CompoundLine? Line(MasonryWallInput w, string kind)
        {
            var hit = CompoundTakeoff.MasonryWall(w).Where(l => l.Kind == kind).ToList();
            return hit.Count == 0 ? (CompoundLine?)null : hit[0];
        }

        [Fact]
        public void Painted_Area_Equals_The_Plastered_Face_Area()
        {
            // 20 m² face × 2 plastered faces = 40 m² painted.
            var paint = Line(Wall(faces: 2), "paint_interior");

            Assert.NotNull(paint);
            Assert.Equal("m2", paint.Value.Unit);
            Assert.Equal(40.0, paint.Value.Quantity, 4);
        }

        [Fact]
        public void One_Plastered_Face_Paints_One_Face()
        {
            Assert.Equal(20.0, Line(Wall(faces: 1), "paint_interior").Value.Quantity, 4);
        }

        [Fact]
        public void An_Unplastered_Wall_Is_Not_Painted()
        {
            // Bare fair-faced blockwork is not painted as a matter of course, and
            // inventing a painted area for it would put buckets in the bill that
            // nobody ordered.
            Assert.Null(Line(Wall(faces: 0), "paint_interior"));
            Assert.Null(Line(Wall(faces: 0), "paint_exterior"));
        }

        [Fact]
        public void An_Exterior_Wall_Paints_As_Exterior_Not_Interior()
        {
            // Different product, different price — weather-guard vs silk.
            var w = Wall(faces: 2, exterior: true);

            Assert.NotNull(Line(w, "paint_exterior"));
            Assert.Null(Line(w, "paint_interior"));
        }

        [Fact]
        public void Paint_Does_Not_Disturb_The_Existing_Plaster_Constituents()
        {
            // Regression guard: paint is additive. Plaster area, cement and sand
            // must be byte-identical to before.
            var lines = CompoundTakeoff.MasonryWall(Wall(faces: 2));

            Assert.Equal(40.0, lines.Single(l => l.Kind == "plaster").Quantity, 4);
            // 40 m² × 0.013 m = 0.52 m³ NET of waste → × 9 bags = 4.68.
            // The 1.20 that used to sit here was engine-side waste, applied again
            // by the supplier-unit rule; the rule is now the only place it lives.
            Assert.Equal(4.68, lines.Single(l => l.Kind == "plaster_cement").Quantity, 3);
            Assert.Equal(0.65, lines.Single(l => l.Kind == "plaster_sand").Quantity, 3);
        }

        [Fact]
        public void Paint_Carries_The_Same_Section_As_Plaster()
        {
            var lines = CompoundTakeoff.MasonryWall(Wall(faces: 2));
            string plasterSection = lines.Single(l => l.Kind == "plaster").Nrm2Section;

            Assert.Equal(plasterSection, lines.Single(l => l.Kind == "paint_interior").Nrm2Section);
        }

        [Fact]
        public void End_To_End_A_Painted_Wall_Buys_Buckets_In_The_Finishes_Section()
        {
            // The whole chain against the SHIPPED data: engine emits the kind,
            // the stage library routes it to Finishes, the unit table converts
            // m2 to buckets and the rate list prices it. Each half was already
            // covered; this pins the seam between them.
            var units = Newtonsoft.Json.JsonConvert.DeserializeObject<StingTools.Core.MaterialSchedule.SupplierUnitTable>(
                System.IO.File.ReadAllText(System.IO.Path.Combine(
                    System.AppContext.BaseDirectory, "Data", "STING_SUPPLIER_UNITS.json")));
            var lib = Newtonsoft.Json.JsonConvert.DeserializeObject<StingTools.Core.MaterialSchedule.StageLibrary>(
                System.IO.File.ReadAllText(System.IO.Path.Combine(
                    System.AppContext.BaseDirectory, "Data", "STING_MATERIAL_STAGES.json")));
            var rates = StingTools.Core.MaterialSchedule.CommodityRateResolver.ParseCsv(
                System.IO.File.ReadAllLines(System.IO.Path.Combine(
                    System.AppContext.BaseDirectory, "Data", "STING_COMMODITY_RATES.csv")), out _);

            // 300 m² of painted interior face.
            var doc = StingTools.Core.MaterialSchedule.CommodityAggregator.Build(
                new StingTools.Core.MaterialSchedule.AggregatorInputs
                {
                    Constituents = { new StingTools.Core.MaterialSchedule.ConstituentInput {
                        ConstituentKind = "paint_interior", Category = "Walls",
                        Description = "Paint", Unit = "m2", Quantity = 300 } },
                    Units = units,
                    StageDefs = lib.Stages,
                    DefaultStageId = lib.DefaultStageId,
                    Rates = new StingTools.Core.MaterialSchedule.CommodityRateResolver(rates, null)
                });

            var finishes = doc.Stages.Single(st => st.StageId == "finishes");
            var paint = finishes.Commodities.Single(c => c.CommodityKey == "paint-interior");

            Assert.Equal("Bkts", paint.SupplierUnit);          // not m2
            Assert.Equal(6, paint.OrderQuantity);              // 300/60 = 5 net, +10% = 5.5 → 6
            Assert.False(paint.IsUnpriced);
            Assert.False(paint.ConversionBlocked);
        }

        [Fact]
        public void The_Painted_Area_Carries_No_Wastage_Of_Its_Own()
        {
            // Wastage on paint belongs to the supplier-unit rule (spreading rate
            // already absorbs over-application), not to the measured area — the
            // same separation plaster uses.
            var paint = Line(Wall(faces: 2), "paint_interior");
            Assert.Equal(40.0, paint.Value.Quantity, 4);   // not 40 × 1.2
        }
    }
}
