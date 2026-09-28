using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Acoustic;
using StingTools.Core.Calc;
using StingTools.Core.Plumbing;
using StingTools.Core.Refrigerant;
using Xunit;

namespace StingTools.Mep.Tests
{
    /// <summary>
    /// Pins the MEP kernels that shipped before this project with no test at all:
    /// duct friction, Hardy Cross, NC prediction, refrigerant sizing, expansion vessels.
    /// Reference values are computed independently in each test, not copied from the code.
    /// </summary>
    public class DuctFrictionTests
    {
        private static double Colebrook(double re, double relRough)
        {
            double f = 0.02;
            for (int i = 0; i < 60; i++)
                f = Math.Pow(-2 * Math.Log10(relRough / 3.7 + 2.51 / (re * Math.Sqrt(f))), -2);
            return f;
        }

        [Fact]
        public void SwameeJainTracksColebrookWithinTwoPercent()
        {
            var r = DuctFrictionSolver.Solve(DuctShape.Round, 400, 0, 10, 1.0, null);
            double f = Colebrook(r.ReynoldsNumber, DuctFrictionSolver.GalvRoughnessM / 0.4);
            Assert.InRange(r.FrictionFactor, f * 0.98, f * 1.02);
            Assert.Equal(1.0 / (Math.PI * 0.04), r.VelocityMs, 6);
            Assert.InRange(r.TotalDropPa, 16.3 * 0.98, 16.3 * 1.02);
        }

        [Fact]
        public void RectangularUsesHydraulicDiameter()
        {
            var r = DuctFrictionSolver.Solve(DuctShape.Rectangular, 600, 300, 5, 1.0, null);
            Assert.Equal(2 * 0.6 * 0.3 / 0.9, r.HydraulicDiameterM, 9);
            Assert.Equal(1.0 / 0.18, r.VelocityMs, 9);
        }

        [Fact]
        public void FittingLossIsCTimesVelocityPressure()
        {
            var fit = new DuctFittingLoss { C = 0.11, Count = 2 };
            var r = DuctFrictionSolver.Solve(DuctShape.Round, 400, 0, 1, 1.0, new[] { fit });
            double pv = 0.5 * DuctFrictionSolver.AirDensityKgM3 * r.VelocityMs * r.VelocityMs;
            Assert.Equal(2 * 0.11 * pv, r.FittingDropPa, 9);
        }
    }

    public class HardyCrossTests
    {
        private static (List<NetworkPipe> pipes, List<NetworkLoop> loops) Parallel(double q1, double q2)
        {
            var p1 = new NetworkPipe { Id = "p1", NodeA = "A", NodeB = "B", DiameterM = 0.05, LengthM = 20, FrictionFactor = 0.02, FlowM3S = q1 };
            var p2 = new NetworkPipe { Id = "p2", NodeA = "A", NodeB = "B", DiameterM = 0.04, LengthM = 20, FrictionFactor = 0.02, FlowM3S = q2 };
            var loop = new NetworkLoop { Id = "L1" };
            loop.Members.Add(("p1", +1));
            loop.Members.Add(("p2", -1));
            return (new List<NetworkPipe> { p1, p2 }, new List<NetworkLoop> { loop });
        }

        /// <summary>With fixed f, head ∝ Q²·L/D⁵, so the parallel split is Q1/Q2 = √(D1⁵/D2⁵).</summary>
        private static double ExpectedRatio => Math.Sqrt(Math.Pow(0.05, 5) / Math.Pow(0.04, 5));

        [Fact]
        public void ParallelPipesSplitByResistance()
        {
            var (pipes, loops) = Parallel(0.005, 0.005);
            var r = HardyCrossSolver.Solve(pipes, loops);
            Assert.True(r.Converged);
            Assert.Equal(0.010, pipes.Sum(p => p.FlowM3S), 9);
            Assert.Equal(ExpectedRatio, pipes[0].FlowM3S / pipes[1].FlowM3S, 3);
        }

        [Fact]
        public void ConvergesFromAGuessWithAReversedFlow()
        {
            // Same total (0.010 A→B) but the initial guess pushes p2 backwards.
            // Head loss must keep the sign of the flow for the correction to
            // pull it forward again.
            var (pipes, loops) = Parallel(0.013, -0.003);
            var r = HardyCrossSolver.Solve(pipes, loops);
            Assert.True(r.Converged);
            Assert.True(pipes[1].FlowM3S > 0);
            Assert.Equal(ExpectedRatio, pipes[0].FlowM3S / pipes[1].FlowM3S, 3);
        }

        [Fact]
        public void PipeDrawnAgainstTheFlowSettlesAtANegativeFlow()
        {
            // p2 is modelled B→A, so the real A→B flow through it is NEGATIVE
            // at the solution. The loop runs A→B on p1 and B→A on p2, i.e.
            // WITH p2's orientation (sign +1). Balance needs h1(Q1) = |h2(Q2)|,
            // which only holds if head loss keeps the sign of a negative flow.
            var p1 = new NetworkPipe { Id = "p1", NodeA = "A", NodeB = "B", DiameterM = 0.05, LengthM = 20, FrictionFactor = 0.02, FlowM3S = 0.006 };
            var p2 = new NetworkPipe { Id = "p2", NodeA = "B", NodeB = "A", DiameterM = 0.04, LengthM = 20, FrictionFactor = 0.02, FlowM3S = -0.004 };
            var loop = new NetworkLoop { Id = "L1" };
            loop.Members.Add(("p1", +1));
            loop.Members.Add(("p2", +1));
            var r = HardyCrossSolver.Solve(new List<NetworkPipe> { p1, p2 }, new List<NetworkLoop> { loop });

            Assert.True(r.Converged);
            Assert.True(p2.FlowM3S < 0);
            Assert.Equal(0.010, p1.FlowM3S - p2.FlowM3S, 9);
            Assert.Equal(ExpectedRatio, p1.FlowM3S / -p2.FlowM3S, 3);
        }
    }

    public class NcPredictionTests
    {
        private static RoomReceiver Office => new RoomReceiver
        {
            VolumeM3 = 150, SurfaceAreaM2 = 180, AvgAbsorption = 0.25, ListenerDistanceM = 1.5, Directivity = 2
        };

        [Fact]
        public void AttenuationOnlyLowersTheRoomLevel()
        {
            var fan = OctaveBand.FromArray(new[] { 80.0, 80, 80, 80, 80, 80, 80, 80 });
            var shortPath = new List<PathElement>
            {
                new PathElement { Kind = ElementKind.Fan, SourceLw = fan },
                new PathElement { Kind = ElementKind.StraightDuct, LengthM = 2 }
            };
            var longPath = new List<PathElement>
            {
                new PathElement { Kind = ElementKind.Fan, SourceLw = fan },
                new PathElement { Kind = ElementKind.StraightDuct, LengthM = 20 }
            };
            var a = NcPredictionEngine.Compute(shortPath, Office);
            var b = NcPredictionEngine.Compute(longPath, Office);
            for (int i = 0; i < 8; i++) Assert.True(b.RoomLw[i] <= a.RoomLw[i]);
            Assert.True(b.NcRating <= a.NcRating);
        }

        [Fact]
        public void RoomCorrectionIsDirectPlusReverberant()
        {
            var room = Office;
            double R = room.SurfaceAreaM2 * room.AvgAbsorption / (1 - room.AvgAbsorption);
            double expected = 10 * Math.Log10(room.Directivity / (4 * Math.PI * 1.5 * 1.5) + 4 / R);
            var lp = NcPredictionEngine.RoomLwToLp(OctaveBand.FromArray(new double[8]), room);
            Assert.Equal(expected, lp.Hz1000, 9);
        }

        [Fact]
        public void RatingIsTheLowestCurveNotExceeded()
        {
            Assert.Equal(35, NcCurves.Rate(OctaveBand.FromArray(NcCurves.Curves[35])));
            var justOver = NcCurves.Curves[35].ToArray();
            justOver[4] += 0.5;
            Assert.Equal(40, NcCurves.Rate(OctaveBand.FromArray(justOver)));
        }

        [Fact]
        public void RectangularBreakoutTransmissionLossMatchesTheAshraeForm()
        {
            // 600 × 300 mm, 6.28 kg/m² (0.8 mm steel): f_L = 24120/√(a·b) ≈ 1444 Hz.
            Assert.Equal(27.672, NcPredictionEngine.RectangularBreakoutTlDb(250, 600, 300, 6.28), 2);    // below f_L
            Assert.Equal(37.207, NcPredictionEngine.RectangularBreakoutTlDb(2000, 600, 300, 6.28), 2);   // above f_L
        }

        [Fact]
        public void BreakoutNeverExceedsTheInDuctPower()
        {
            var lwIn = OctaveBand.FromArray(new[] { 80.0, 80, 80, 80, 80, 80, 80, 80 });
            // Very light, very long duct: the floor TL = 10·log10(S/A) caps breakout at Lw_in.
            var bo = NcPredictionEngine.BreakoutLw(lwIn, 600, 300, 50, 0.5);
            for (int i = 0; i < 8; i++) Assert.True(bo[i] <= lwIn[i] + 1e-9);
        }

        [Fact]
        public void BreakoutOnlyAddsWhenTheDuctIsInTheRoom()
        {
            var fan = OctaveBand.FromArray(new[] { 85.0, 85, 85, 85, 85, 85, 85, 85 });
            PathElement Duct(bool inRoom) => new PathElement
            {
                Kind = ElementKind.StraightDuct, LengthM = 5, WidthMm = 600, HeightMm = 300,
                WallMassKgM2 = 6.28, BreakoutIntoRoom = inRoom
            };
            var room = Office;
            var outside = NcPredictionEngine.Compute(new List<PathElement> { new PathElement { Kind = ElementKind.Fan, SourceLw = fan }, Duct(false) }, room);
            var inside = NcPredictionEngine.Compute(new List<PathElement> { new PathElement { Kind = ElementKind.Fan, SourceLw = fan }, Duct(true) }, room);
            Assert.False(outside.HasBreakout);
            Assert.True(inside.HasBreakout);
            for (int i = 0; i < 8; i++) Assert.True(inside.RoomLw[i] >= outside.RoomLw[i]);
            Assert.True(inside.RoomLw.Hz125 > outside.RoomLw.Hz125 + 0.1);
        }

        [Fact]
        public void OffTheTopOfTheCurvesIsFlaggedNotReportedAs65()
        {
            var loud = OctaveBand.FromArray(new[] { 90.0, 90, 90, 90, 90, 90, 90, 90 });
            Assert.Equal(65, NcCurves.Rate(loud));
            Assert.True(NcCurves.ExceedsAll(loud));
            Assert.False(NcCurves.ExceedsAll(OctaveBand.FromArray(NcCurves.Curves[65])));
        }
    }

    public class RefrigerantSolverTests
    {
        [Fact]
        public void LiquidStaticHeadFollowsTheFlowDirection()
        {
            double perM = 1000 * RefrigerantPipeSolver.GravityMs2 / 1000.0;   // ρ = 1000 → 9.81 kPa/m
            // Outdoor unit 10 m above: cooling flows DOWN (credit), heating UP (debit).
            Assert.Equal(-10 * perM, RefrigerantPipeSolver.LiquidStaticHeadKpa(1000, 10, RefrigerantOperatingMode.CoolingOnly), 9);
            Assert.Equal(10 * perM, RefrigerantPipeSolver.LiquidStaticHeadKpa(1000, 10, RefrigerantOperatingMode.HeatingOnly), 9);
            // Reversible: always the uphill case, whichever unit is higher.
            Assert.Equal(10 * perM, RefrigerantPipeSolver.LiquidStaticHeadKpa(1000, 10, RefrigerantOperatingMode.Reversible), 9);
            Assert.Equal(10 * perM, RefrigerantPipeSolver.LiquidStaticHeadKpa(1000, -10, RefrigerantOperatingMode.Reversible), 9);
            // Outdoor unit below: cooling flows UP.
            Assert.Equal(10 * perM, RefrigerantPipeSolver.LiquidStaticHeadKpa(1000, -10, RefrigerantOperatingMode.CoolingOnly), 9);
        }

        private static RefrigerantSizingInput Suction(double mult) => new RefrigerantSizingInput
        {
            RefrigerantId = "R410A", Leg = RefrigerantLeg.Suction, CapacityKw = 28,
            EquivLengthM = 40, LiftM = 0, HasVerticalRiser = false, MaxPressureDropKpa = 1000,
            SuctionDpMultiplier = mult
        };

        [Fact]
        public void SuctionAllowanceScalesThePressureDrop()
        {
            var a = RefrigerantPipeSolver.Size(Suction(1.0));
            var b = RefrigerantPipeSolver.Size(Suction(1.25));
            Assert.True(a.Ok && b.Ok);
            Assert.Equal(a.SelectedBoreMm, b.SelectedBoreMm);
            Assert.Equal(a.PressureDropKpa * 1.25, b.PressureDropKpa, 6);
        }

        [Fact]
        public void AllowanceBelowOneIsTreatedAsOne()
        {
            var a = RefrigerantPipeSolver.Size(Suction(1.0));
            var b = RefrigerantPipeSolver.Size(Suction(0.5));
            Assert.Equal(a.PressureDropKpa, b.PressureDropKpa, 9);
        }

        [Fact]
        public void ZeroCapacityIsRefusedNotSized()
        {
            var i = Suction(1.1);
            i.CapacityKw = 0;
            var r = RefrigerantPipeSolver.Size(i);
            Assert.False(r.Ok);
            Assert.NotEmpty(r.Warnings);
        }
    }

    public class PumpCurveTests
    {
        private static readonly PumpCurvePoint[] Curve =
        {
            new PumpCurvePoint { FlowLps = 0,   HeadM = 30, EfficiencyPct = 0 },
            new PumpCurvePoint { FlowLps = 2.0, HeadM = 28, EfficiencyPct = 55 },
            new PumpCurvePoint { FlowLps = 4.0, HeadM = 22, EfficiencyPct = 70 },
            new PumpCurvePoint { FlowLps = 6.0, HeadM = 12, EfficiencyPct = 60 },
        };

        [Fact]
        public void HeadAtDutyIsInterpolatedOnTheCurve()
        {
            var c = PumpDutyCurve.Check(Curve, 3.0, 20);
            Assert.True(c.WithinCurve);
            Assert.Equal(25.0, c.HeadAtDutyM, 9);                    // midway 28 → 22
            Assert.Equal(62.5, c.EfficiencyAtDutyPct, 9);
            Assert.Equal(25.0, c.HeadMarginPct, 9);
            Assert.True(c.Meets);
        }

        [Fact]
        public void ARatedPointAboveTheDutyCanStillMissItOnTheCurve()
        {
            // Rated 6 L/s @ 12 m "covers" 5 L/s @ 20 m by the rated-point rule,
            // but the curve gives only 17 m at 5 L/s.
            var c = PumpDutyCurve.Check(Curve, 5.0, 20);
            Assert.True(c.WithinCurve);
            Assert.Equal(17.0, c.HeadAtDutyM, 9);
            Assert.False(c.Meets);
        }

        [Fact]
        public void DutyOffTheCurveIsNotWithinCurve()
        {
            var c = PumpDutyCurve.Check(Curve, 7.0, 5);
            Assert.False(c.WithinCurve);
            Assert.False(c.Meets);
        }

        [Fact]
        public void EfficiencyIsNaNWhereTheCurvePublishesNone()
            => Assert.True(double.IsNaN(PumpDutyCurve.Check(Curve, 1.0, 10).EfficiencyAtDutyPct));
    }

    public class ExpansionVesselTests
    {
        [Fact]
        public void AcceptanceFactorSizing()
        {
            // 200 L, 10→60 °C (e = 0.0037 at ΔT 50), 1 → 3 bar gauge:
            // V = 200·0.0037 / (1 − 2/4) = 1.48 L, ×1.1 margin, rounded up = 2 L.
            var r = ExpansionVesselSizer.Size(200, 10, 60, 1.0, 3.0);
            Assert.Equal(0.0037, r.ExpansionCoeff, 9);
            Assert.Equal(2, r.VTankL);
            Assert.Equal("EV-8L", r.RecommendedFamily);
        }

        [Fact]
        public void CoefficientInterpolatesBetweenTablePoints()
            => Assert.Equal((0.0025 + 0.0037) / 2, ExpansionVesselSizer.ExpansionCoefficientForDeltaT(45), 9);
    }
}
