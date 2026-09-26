using StingTools.Core.Fire;
using Xunit;

namespace StingTools.Mep.Tests
{
    public class StairPressurisationTests
    {
        [Fact]
        public void LeakageEquation()
            => Assert.Equal(0.83 * 0.01 * System.Math.Sqrt(50), StairPressurisation.LeakageFlowM3s(0.01, 50), 12);

        [Fact]
        public void SeriesAndParallelAreas()
        {
            Assert.Equal(0.0141421, StairPressurisation.SeriesArea(new[] { 0.02, 0.02 }), 6);
            Assert.Equal(0.05, StairPressurisation.ParallelArea(new[] { 0.02, 0.03 }), 12);
        }

        [Fact]
        public void DoorOpeningForce()
            // 30 N closer + 1.0·(1.0·2.1)·50 / (2·(1.0 − 0.075)) = 86.76 N
            => Assert.Equal(86.757, StairPressurisation.DoorOpeningForceN(30, 1.0, 2.1, 50, 0.075), 3);

        private static StairPressurisationInput TenStoreys(double velocity)
        {
            var i = new StairPressurisationInput
            {
                DesignPressurePa = 50, OpenDoorVelocityMs = velocity, OpenDoors = 1,
                DoorWidthM = 1.0, DoorHeightM = 2.1, LeakageAllowance = 1.25,
                DoorCloserForceN = 30, HandleToEdgeM = 0.075, MaxDoorOpeningForceN = 100
            };
            i.ClosedLeakage.Add(new LeakagePath { Label = "Single door, into stair", Count = 10, AreaEachM2 = 0.01 });
            i.ClosedLeakage.Add(new LeakagePath { Label = "Other leakage", Count = 1, AreaEachM2 = 0.02 });
            return i;
        }

        [Fact]
        public void OpenDoorCaseGovernsAtTwoMetresPerSecond()
        {
            var r = StairPressurisation.Calculate(TenStoreys(2.0));
            Assert.Equal("open-door velocity", r.Governs);
            Assert.Equal(4.2, r.OpenDoorFlowM3s, 9);                       // 1.0 × 2.1 × 2.0
            Assert.Equal(r.SupplyBeforeAllowanceM3s * 1.25, r.SupplyM3s, 9);
            Assert.True(r.DoorForceOk);
        }

        [Fact]
        public void ClosedDoorLeakageUsesAllPaths()
        {
            var r = StairPressurisation.Calculate(TenStoreys(0.0));
            Assert.Equal(0.12, r.ClosedLeakageAreaM2, 9);                   // 10 × 0.01 + 0.02
            Assert.Equal(StairPressurisation.LeakageFlowM3s(0.12, 50), r.ClosedDoorsFlowM3s, 12);
            Assert.Equal("doors-closed pressure", r.Governs);
        }

        [Fact]
        public void OpenCaseRemovesTheOpenDoorsOwnLeakageAndUsesResidualPressure()
        {
            var i = TenStoreys(0.75);
            i.OpenCaseResidualPressurePa = 10;
            var r = StairPressurisation.Calculate(i);
            // 1 of 10 doors open: 0.12 − 0.01 m² leaking at 10 Pa.
            Assert.Equal(StairPressurisation.LeakageFlowM3s(0.11, 10), r.OpenCaseLeakageM3s, 12);
        }

        [Fact]
        public void ExcessiveDoorForceIsReported()
        {
            var i = TenStoreys(2.0);
            i.DesignPressurePa = 80;
            i.DoorCloserForceN = 60;
            var r = StairPressurisation.Calculate(i);
            Assert.False(r.DoorForceOk);
            Assert.Contains(r.Warnings, w => w.Contains("opening force"));
        }
    }
}
