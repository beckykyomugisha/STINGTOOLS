using System;
using System.Linq;
using StingTools.Core.Mep.Networks;
using StingTools.Core.Plumbing;
using Xunit;

namespace StingTools.Mep.Tests
{
    /// <summary>
    /// The shipped design-data files are the behaviour of the sprinkler, gas and
    /// pressurisation commands. A typo in one is not a compile error, so these
    /// parse the real files and fail on anything Validate() rejects.
    /// </summary>
    public class ShippedDesignDataTests
    {
        [Fact]
        public void SprinklerDesignDataIsValid()
        {
            var d = SprinklerDesignData.Parse(RepoData.Read("STING_SPRINKLER_DESIGN.json"));
            Assert.Empty(d.Validate());
            Assert.NotNull(d.Hazard(d.DefaultHazardId));
            Assert.True(d.Hazards.All(h => h.Verify), "Hazard figures stay marked verify until checked against the standard.");
        }

        [Fact]
        public void GasDesignDataIsValid()
        {
            var d = GasDesignData.Parse(RepoData.Read("STING_GAS_DESIGN.json"));
            Assert.Empty(d.Validate());
            Assert.Equal(1.0, d.Gas("NG").MaxDropMbar, 9);
        }

        [Fact]
        public void SmokeControlDesignDataIsValid()
        {
            var d = SmokeControlDesignData.Parse(RepoData.Read("STING_SMOKE_CONTROL_DESIGN.json"));
            Assert.Empty(d.Validate());
        }

        [Fact]
        public void ProjectOverrideReplacesByIdAndKeepsTheRest()
        {
            string overlay = @"{ ""hazards"": [ { ""id"": ""OH1"", ""densityMmMin"": 6.0 },
                                                  { ""id"": ""PROJ"", ""label"": ""Project hazard"", ""densityMmMin"": 4, ""designAreaM2"": 90, ""minHeadPressureBar"": 0.5, ""maxAreaPerHeadM2"": 10 } ],
                                 ""velocityLimitsMs"": { ""pipe"": 8 } }";
            var d = SprinklerDesignData.Parse(RepoData.Read("STING_SPRINKLER_DESIGN.json"), overlay);
            var oh1 = d.Hazard("OH1");
            Assert.Equal(6.0, oh1.DensityMmMin, 9);          // overridden
            Assert.Equal(72, oh1.DesignAreaM2, 9);           // kept from corporate
            Assert.NotNull(d.Hazards.SingleOrDefault(h => h.Id == "PROJ"));
            Assert.Equal(8, d.PipeVelocityLimitMs, 9);
            Assert.Equal(6, d.ValveVelocityLimitMs, 9);      // untouched key survives
            Assert.Empty(d.Validate());
        }

        [Fact]
        public void BrokenDataIsCaughtByValidate()
        {
            var d = GasDesignData.Parse(@"{ ""gases"": [ { ""id"": ""X"", ""relativeDensity"": 0 } ],
                                             ""pipeSeries"": [ { ""id"": ""S"", ""sizes"": [ { ""label"": ""a"", ""boreMm"": 20, ""outerDiameterMm"": 15, ""nominalMm"": 15 } ] } ] }");
            var problems = d.Validate();
            Assert.Contains(problems, p => p.Contains("relativeDensity"));
            Assert.Contains(problems, p => p.Contains("outer diameter"));
            Assert.Contains(problems, p => p.Contains("Default"));
        }

        [Fact]
        public void UsKFactorsAreConvertedToSi()
        {
            var d = SprinklerDesignData.Parse(RepoData.Read("STING_SPRINKLER_DESIGN.json"));
            Assert.Equal(5.6 * 14.4, d.KFactorToSi(5.6), 9);   // K5.6 gpm/psi^½ ≈ K80
            Assert.Equal(80, d.KFactorToSi(80), 9);             // already SI
        }

        [Fact]
        public void PerSizeTableWinsOverBoresWhereItCoversTheBore()
        {
            string overlay = @"{ ""fittingEquivalentLengthsM"": { ""Elbow"": [[25, 0.76], [32, 1.0], [50, 1.5]] } }";
            var d = SprinklerDesignData.Parse(RepoData.Read("STING_SPRINKLER_DESIGN.json"), overlay);
            Assert.Equal(0.76, d.EquivalentLengthM("Elbow", 20), 9);        // first row covering 20 mm
            Assert.Equal(1.0, d.EquivalentLengthM("Elbow", 27.3), 9);
            Assert.Equal(30 * 80 / 1000.0, d.EquivalentLengthM("Elbow", 80), 9);   // beyond the table: bores rule
            Assert.Equal(60 * 27.3 / 1000.0, d.EquivalentLengthM("Tee", 27.3), 9); // no table for tees
            Assert.Empty(d.Validate());
        }

        [Fact]
        public void TreeNodesUseThePerSizeTableAfterInheritingTheirBore()
        {
            var main = new FlowNode { Id = "m", Kind = FlowNodeKind.Pipe, LengthM = 5, DiameterMm = 36 };
            var elbow = main.Add(new FlowNode { Id = "e", Kind = FlowNodeKind.Fitting, EquivLengthDiameters = 30, EquivLengthAtBore = b => b < 40 ? 1.2 : 2.0 });
            FlowTreeUtil.ResolveInheritedBores(main);
            Assert.Equal(1.2, elbow.EquivLengthM, 9);
        }

        [Theory]
        [InlineData("Elbow", 30)]
        [InlineData("Tee", 60)]
        [InlineData("Valve", 8)]
        [InlineData("SomethingElse", 10)]
        public void FittingEquivalentBoresFallBackToDefault(string part, double expected)
        {
            var d = SprinklerDesignData.Parse(RepoData.Read("STING_SPRINKLER_DESIGN.json"));
            Assert.Equal(expected, d.EquivalentBores(part), 9);
        }
    }

    public class IsometricProjectionTests
    {
        private static IsoSegmentInput Seg(string id, double x0, double y0, double z0, double x1, double y1, double z1, string label = "DN50")
            => new IsoSegmentInput { Id = id, Start = new IsoPoint3(x0, y0, z0), End = new IsoPoint3(x1, y1, z1), Label = label, Group = "DCW" };

        [Fact]
        public void AxesProjectToThirtyDegrees()
        {
            var ex = IsometricProjection.Project(new IsoPoint3(1, 0, 0));
            var ey = IsometricProjection.Project(new IsoPoint3(0, 1, 0));
            var ez = IsometricProjection.Project(new IsoPoint3(0, 0, 1));
            Assert.Equal(-30.0, Math.Atan2(ex.V, ex.U) * 180 / Math.PI, 9);   // east → down-right
            Assert.Equal(30.0, Math.Atan2(ey.V, ey.U) * 180 / Math.PI, 9);    // north → up-right
            Assert.Equal(0.0, ez.U, 12);                                     // vertical stays vertical
            Assert.Equal(1.0, ez.V, 12);
            // True isometric: every axis foreshortened equally.
            Assert.Equal(ex.DistanceTo(new IsoPoint2(0, 0)), ey.DistanceTo(new IsoPoint2(0, 0)), 12);
        }

        [Fact]
        public void DrawingIsTranslatedToTheOriginAndFitted()
        {
            var r = IsometricProjection.Project(new[]
            {
                Seg("a", 100, 100, 0, 110, 100, 0),
                Seg("b", 110, 100, 0, 110, 100, 3)
            }, new IsoProjectionOptions { FitTo = 1.0 });
            Assert.Equal(0, r.MinU, 12);
            Assert.Equal(0, r.MinV, 12);
            Assert.Equal(1.0, Math.Max(r.Width, r.Height), 9);
            Assert.All(r.Segments, s => Assert.True(s.Start.U >= -1e-12 && s.Start.V >= -1e-12));
        }

        [Fact]
        public void RisersAreFlaggedAndZeroLengthSegmentsDropped()
        {
            var r = IsometricProjection.Project(new[]
            {
                Seg("run", 0, 0, 0, 5, 0, 0),
                Seg("riser", 5, 0, 0, 5, 0, 4),
                Seg("dot", 5, 0, 4, 5, 0, 4)
            });
            Assert.Equal(1, r.Dropped);
            Assert.True(r.Segments.Single(s => s.Id == "riser").IsVertical);
            Assert.False(r.Segments.Single(s => s.Id == "run").IsVertical);
        }

        [Fact]
        public void ShortSegmentsAreDrawnButNotLabelled()
        {
            var r = IsometricProjection.Project(new[]
            {
                Seg("long", 0, 0, 0, 10, 0, 0),
                Seg("short", 10, 0, 0, 10.1, 0, 0)
            }, new IsoProjectionOptions { MinLabelledLength = 1.0 });
            Assert.True(r.Segments.Single(s => s.Id == "long").ShowLabel);
            Assert.False(r.Segments.Single(s => s.Id == "short").ShowLabel);
        }

        [Fact]
        public void PointsLandOnTheSameSheetAsTheSegments()
        {
            var r = IsometricProjection.Project(new[] { Seg("a", 100, 100, 0, 110, 100, 3) }, new IsoProjectionOptions { FitTo = 2.0 });
            var end = r.ToSheet(new IsoPoint3(110, 100, 3));
            var seg = r.Segments.Single();
            Assert.Equal(seg.End.U, end.U, 9);
            Assert.Equal(seg.End.V, end.V, 9);
        }

        [Fact]
        public void ValveIsABowTieCentredOnThePipe()
        {
            var edges = IsometricProjection.ValveSymbol(new IsoPoint2(5, 5), new IsoPoint2(1, 0), 4);
            Assert.Equal(6, edges.Count);
            // Every triangle has its apex at the centre and its base 2 either side along the pipe.
            var pts = edges.SelectMany(e => new[] { e.A, e.B }).ToList();
            Assert.Equal(3.0, pts.Min(p => p.U), 9);
            Assert.Equal(7.0, pts.Max(p => p.U), 9);
            Assert.Equal(4, edges.Count(e => (e.A.U == 5 && e.A.V == 5) || (e.B.U == 5 && e.B.V == 5)));
            Assert.Empty(IsometricProjection.ValveSymbol(new IsoPoint2(0, 0), new IsoPoint2(0, 0), 4));
        }

        [Fact]
        public void LabelsSitAboveRunsAndLeftOfRisers()
        {
            var above = IsometricProjection.LabelAnchorFor(new IsoPoint2(0, 0), new IsoPoint2(10, 0), 1);
            Assert.Equal(1.0, above.V, 12);
            var aboveReversed = IsometricProjection.LabelAnchorFor(new IsoPoint2(10, 0), new IsoPoint2(0, 0), 1);
            Assert.Equal(1.0, aboveReversed.V, 12);
            var left = IsometricProjection.LabelAnchorFor(new IsoPoint2(0, 0), new IsoPoint2(0, 10), 1);
            Assert.Equal(-1.0, left.U, 12);
            var leftDown = IsometricProjection.LabelAnchorFor(new IsoPoint2(0, 10), new IsoPoint2(0, 0), 1);
            Assert.Equal(-1.0, leftDown.U, 12);
        }
    }
}
