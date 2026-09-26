using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Gas;
using StingTools.Core.Mep.Networks;
using Xunit;

namespace StingTools.Mep.Tests
{
    public class GasPipeSizerTests
    {
        private static readonly GasProperties NaturalGas = new GasProperties
        {
            Id = "NG", RelativeDensity = 0.6, CalorificValueMJm3 = 38.7, MaxDropMbar = 1.0
        };

        private static readonly List<GasPipeSize> Copper = new List<GasPipeSize>
        {
            new GasPipeSize { Label = "15 mm", NominalMm = 15, OuterDiameterMm = 15, BoreMm = 13.6 },
            new GasPipeSize { Label = "22 mm", NominalMm = 22, OuterDiameterMm = 22, BoreMm = 20.2 },
            new GasPipeSize { Label = "28 mm", NominalMm = 28, OuterDiameterMm = 28, BoreMm = 26.2 },
            new GasPipeSize { Label = "35 mm", NominalMm = 35, OuterDiameterMm = 35, BoreMm = 32.6 },
        };

        private static FlowNode Pipe(string id, double l, double bore = 0)
            => new FlowNode { Id = id, Label = id, Kind = FlowNodeKind.Pipe, LengthM = l, DiameterMm = bore };

        private static FlowNode Appliance(string id, double kw)
            => new FlowNode { Id = id, Label = id, Kind = FlowNodeKind.Terminal, LoadKw = kw };

        [Fact]
        public void PolesFormulaIsSelfConsistent()
        {
            // 22 mm copper (20.2 bore), 10 m, 1 mbar, natural gas: ≈ 5.3 m³/h.
            double cap = GasPipeSizer.CapacityM3h(1.0, 10, 20.2, 0.6);
            Assert.Equal(5.316, cap, 2);
            Assert.Equal(1.0, GasPipeSizer.DropMbar(cap, 10, 20.2, 0.6), 9);
        }

        [Fact]
        public void HeatInputConvertsToVolumeFlowByCalorificValue()
            => Assert.Equal(30 * 3.6 / 38.7, GasPipeSizer.FlowM3h(30, NaturalGas), 9);

        [Fact]
        public void ThirtyKilowattBoilerAtFifteenMetresNeedsTwentyTwo()
        {
            // 15 mm drops ≈ 3.0 mbar over 15 m; 22 mm ≈ 0.41 mbar.
            var root = Pipe("run", 15);
            root.Add(Appliance("boiler", 30));
            var r = GasPipeSizer.Size(root, NaturalGas, Copper);
            Assert.True(r.Ok);
            Assert.Equal("22 mm", r.Nodes[root].SizeLabel);
            Assert.Equal(0.413, r.WorstPathDropMbar, 2);
        }

        [Fact]
        public void EveryApplianceEndsWithinTheAllowance()
        {
            var root = Pipe("main", 6);
            var tee = root.Add(new FlowNode { Id = "tee", Kind = FlowNodeKind.Fitting, EquivLengthDiameters = 60 });
            tee.Add(Pipe("b1", 10)).Add(Appliance("boiler", 35));
            tee.Add(Pipe("b2", 4)).Add(Appliance("hob", 10));
            var r = GasPipeSizer.Size(root, NaturalGas, Copper);
            Assert.True(r.Ok);
            Assert.True(r.WorstPathDropMbar <= 1.0 + 1e-9);
            // The main carries both loads, so it is never smaller than either branch.
            Assert.True(r.Nodes[root].BoreMm >= r.Nodes.Values.Where(n => n.Node.Id.StartsWith("b")).Max(n => n.BoreMm));
            Assert.Equal(45.0, r.TotalLoadKw, 9);
        }

        [Fact]
        public void CheckModeReportsAnUndersizedInstallation()
        {
            var root = Pipe("run", 15, bore: 13.6);
            root.Add(Appliance("boiler", 30));
            var r = GasPipeSizer.Check(root, NaturalGas);
            Assert.False(r.Ok);
            Assert.Equal(2.99, r.WorstPathDropMbar, 2);
            Assert.Contains(r.Warnings, w => w.Contains("exceeds"));
        }

        [Fact]
        public void ApplianceWithoutHeatInputIsListedNotGuessed()
        {
            var root = Pipe("run", 5);
            root.Add(Appliance("mystery", 0));
            root.Add(Appliance("boiler", 20));
            var r = GasPipeSizer.Size(root, NaturalGas, Copper);
            Assert.Contains(r.Warnings, w => w.Contains("no heat input") && w.Contains("mystery"));
            Assert.Equal(20.0, r.TotalLoadKw, 9);
        }

        [Fact]
        public void LoadTooBigForTheSeriesSaysSo()
        {
            var root = Pipe("run", 40);
            root.Add(Appliance("plant", 600));
            var r = GasPipeSizer.Size(root, NaturalGas, Copper);
            Assert.False(r.Ok);
            Assert.Contains(r.Warnings, w => w.Contains("largest bore"));
        }
    }
}
