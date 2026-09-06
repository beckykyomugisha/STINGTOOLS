using System.Linq;
using StingTools.BOQ.Takeoff;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — the first real export listed three RC columns as unpriced m³
    /// lumps and produced no SUB-STRUCTURE section at all, because
    /// CompoundTakeoffBuilder handled Walls, Floors and Structural Framing only:
    /// "Columns/foundations retain the composite line for now."
    ///
    /// So the columns' and foundations' concrete, rebar and formwork never
    /// reached the commodity totals — the bill was under-measured, which is the
    /// one class of error that costs real money on site.
    ///
    /// Formwork is the part worth pinning: a column shutters on all four faces
    /// with no soffit, and a foundation only shutters its sides — and only when
    /// it is not cast against the excavation.
    /// </summary>
    public class RcColumnFoundationTests
    {
        // ── columns ─────────────────────────────────────────────────────────

        [Fact]
        public void A_Rectangular_Column_Shutters_Its_Perimeter_By_Its_Height()
        {
            // 0.2 × 0.2 × 3.0 → concrete 0.12 m³, formwork 2(0.2+0.2)×3 = 2.4 m²
            var lines = CompoundTakeoff.RcColumn(new RcColumnInput
            {
                WidthM = 0.2, DepthM = 0.2, HeightM = 3.0, RebarBandKgPerM3 = 160
            });

            Assert.Equal(0.12, lines.Single(l => l.Kind == "concrete").Quantity, 4);
            Assert.Equal(2.4, lines.Single(l => l.Kind == "formwork").Quantity, 4);
            Assert.Equal(0.12 * 160, lines.Single(l => l.Kind == "rebar").Quantity, 4);
        }

        [Fact]
        public void A_Round_Column_Uses_Its_Circumference_Not_A_Box()
        {
            // Ø0.152 × 3.0 → concrete πr²h, formwork πdh. Treating it as a square
            // would over-order shuttering by ~27%.
            double d = 0.152, h = 3.0;
            var lines = CompoundTakeoff.RcColumn(new RcColumnInput
            {
                DiameterM = d, HeightM = h, RebarBandKgPerM3 = 160
            });

            // CompoundLine rounds every quantity to 4 dp, so the expectation is
            // rounded the same way rather than asserting a precision the engine
            // does not promise.
            Assert.Equal(System.Math.Round(System.Math.PI * d * d / 4.0 * h, 4),
                         lines.Single(l => l.Kind == "concrete").Quantity, 6);
            Assert.Equal(System.Math.Round(System.Math.PI * d * h, 4),
                         lines.Single(l => l.Kind == "formwork").Quantity, 6);
        }

        [Fact]
        public void A_Measured_Volume_Overrides_The_Derived_One()
        {
            // Revit's own solid volume is authoritative when we have it; the
            // dimensions are only there to derive formwork.
            var lines = CompoundTakeoff.RcColumn(new RcColumnInput
            {
                WidthM = 0.2, DepthM = 0.2, HeightM = 3.0,
                ConcreteM3Override = 0.1375, RebarBandKgPerM3 = 160
            });

            Assert.Equal(0.1375, lines.Single(l => l.Kind == "concrete").Quantity, 4);
            Assert.Equal(2.4, lines.Single(l => l.Kind == "formwork").Quantity, 4);  // unchanged
        }

        [Fact]
        public void A_Column_With_No_Usable_Dimensions_Emits_No_Formwork_Rather_Than_Guessing()
        {
            var lines = CompoundTakeoff.RcColumn(new RcColumnInput
            {
                ConcreteM3Override = 0.5, RebarBandKgPerM3 = 160
            });

            Assert.Equal(0.5, lines.Single(l => l.Kind == "concrete").Quantity, 4);
            Assert.DoesNotContain(lines, l => l.Kind == "formwork");
        }

        // ── foundations ─────────────────────────────────────────────────────

        [Fact]
        public void A_Pad_Foundation_Shutters_Only_Its_Sides()
        {
            // 1.5 × 1.5 × 0.4 → concrete 0.9 m³, sides 2(1.5+1.5)×0.4 = 2.4 m².
            // No soffit: it bears on the ground.
            var lines = CompoundTakeoff.RcFoundation(new RcFoundationInput
            {
                LengthM = 1.5, WidthM = 1.5, DepthM = 0.4,
                RebarBandKgPerM3 = 100, FormworkToSides = true
            });

            Assert.Equal(0.9, lines.Single(l => l.Kind == "concrete").Quantity, 4);
            Assert.Equal(2.4, lines.Single(l => l.Kind == "formwork").Quantity, 4);
        }

        [Fact]
        public void A_Foundation_Cast_Against_The_Excavation_Needs_No_Formwork()
        {
            var lines = CompoundTakeoff.RcFoundation(new RcFoundationInput
            {
                LengthM = 1.5, WidthM = 1.5, DepthM = 0.4,
                RebarBandKgPerM3 = 100, FormworkToSides = false
            });

            Assert.Equal(0.9, lines.Single(l => l.Kind == "concrete").Quantity, 4);
            Assert.DoesNotContain(lines, l => l.Kind == "formwork");
        }

        [Fact]
        public void Blinding_Carries_No_Reinforcement()
        {
            // Blinding is unreinforced by definition. Banding it would invent
            // steel that nobody orders and nobody fixes.
            var lines = CompoundTakeoff.RcFoundation(new RcFoundationInput
            {
                LengthM = 2.0, WidthM = 2.0, DepthM = 0.05,
                RebarBandKgPerM3 = 100, IsBlinding = true
            });

            Assert.Equal(0.2, lines.Single(l => l.Kind == "concrete").Quantity, 4);
            Assert.DoesNotContain(lines, l => l.Kind == "rebar");
        }

        [Fact]
        public void Zero_Or_Negative_Dimensions_Produce_Nothing_Rather_Than_Negative_Quantities()
        {
            Assert.Empty(CompoundTakeoff.RcColumn(new RcColumnInput { WidthM = -1, HeightM = 3 }));
            Assert.Empty(CompoundTakeoff.RcFoundation(new RcFoundationInput { LengthM = 0, WidthM = 0, DepthM = 0 }));
        }

        [Fact]
        public void Constituent_Kinds_Match_The_Ones_Walls_And_Slabs_Already_Emit()
        {
            // They must route and convert through the SAME supplier-unit rules as
            // wall and slab concrete — a new kind here would silently fail to
            // match any rule and land unpriced.
            var col = CompoundTakeoff.RcColumn(new RcColumnInput
            { WidthM = .3, DepthM = .3, HeightM = 3, RebarBandKgPerM3 = 160 });
            var fnd = CompoundTakeoff.RcFoundation(new RcFoundationInput
            { LengthM = 2, WidthM = 2, DepthM = .5, RebarBandKgPerM3 = 100, FormworkToSides = true });

            foreach (var kind in new[] { "concrete", "rebar", "formwork" })
            {
                Assert.Contains(col, l => l.Kind == kind);
                Assert.Contains(fnd, l => l.Kind == kind);
            }
        }
    }
}
