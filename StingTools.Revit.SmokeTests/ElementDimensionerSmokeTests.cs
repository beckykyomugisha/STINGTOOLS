// StingTools.Revit.SmokeTests — ElementDimensioner (DRAW-1) against a real model.
//
// Every assertion is about WHERE a dimension landed and WHAT it measures, computed
// independently from the fixture's known geometry. A placed-count is logged, never
// asserted on its own: "3 dimensions placed" is equally true of three correct
// dimensions and of three that span the wrong faces.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using NUnit.Framework;
using StingTools.Core.Drawing;
using StingTools.Core.Drawing.Dimensioning;
using StingTools.Revit.SmokeTests.Fixtures;
using static StingTools.Revit.SmokeTests.Fixtures.RevitHarness;

namespace StingTools.Revit.SmokeTests
{
    [TestFixture]
    public class ElementDimensionerSmokeTests
    {
        private SmokeModel _m;
        private Document Doc => _m.Doc;

        [OneTimeSetUp]
        public void Setup(Application application) => _m = SmokeModelCache.Get(application);

        private static AnnotationRulePack Pack() => new AnnotationRulePack();
        private static AutoAnnotationRule Rule(string type) => new AutoAnnotationRule { RuleType = type };

        // ─────────────────────────────────────────────────────────────────
        //  AutoDimWallLength
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void WallLength_OneDimPerStraightWall_EqualToItsLength_CurvedWarns_ReRunAddsNothing()
        {
            Isolated(Doc, "wall length", () =>
            {
                var view = NewPlan(Doc, _m.Level1, "wall-length");
                var before = Snapshot(Doc, view);

                var r1 = RunEngine(Doc, "RunWallLength #1",
                    r => ElementDimensioner.RunWallLength(Doc, view, Pack(), Rule("AutoDimWallLength"), r));
                var dims = NewSince<Dimension>(Doc, view, before);

                foreach (var wall in _m.StraightWalls)
                {
                    var onWall = dims.Where(d => ReferencedIds(Doc, d).Contains(wall.Id)).ToList();
                    Assert.That(onWall.Count, Is.EqualTo(1),
                        $"straight wall {wall.Id}: expected exactly one length dimension, found {onWall.Count}. {Dump(r1)}");

                    var dim = onWall[0];
                    Assert.That(dim.NumberOfSegments, Is.EqualTo(0),
                        $"wall {wall.Id}: a length dimension should be a single segment, not a chain of {dim.NumberOfSegments}.");
                    double expected = wall.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH).AsDouble();
                    Assert.That(dim.Value, Is.Not.Null, $"wall {wall.Id}: dimension has no value.");
                    Assert.That(dim.Value.Value, Is.EqualTo(expected).Within(OneMmFt),
                        $"wall {wall.Id}: dimension reads {dim.Value.Value * MmPerFt:F1} mm, wall is {expected * MmPerFt:F1} mm " +
                        "— EndCapReferences picked the wrong pair of faces.");

                    // The dimension line runs along the wall, not across it.
                    var wallDir = ((Line)((LocationCurve)wall.Location).Curve).Direction;
                    var dimDir = ((Line)dim.Curve).Direction;
                    Assert.That(Math.Abs(dimDir.DotProduct(wallDir)), Is.GreaterThan(0.999),
                        $"wall {wall.Id}: dimension line is not parallel to the wall.");
                }

                // Curved wall: no dimension, and a warning that NAMES it.
                Assert.That(dims.Any(d => ReferencedIds(Doc, d).Contains(_m.ArcWall.Id)), Is.False,
                    $"curved wall {_m.ArcWall.Id} received a length dimension; it has no planar end-cap pair to measure between.");
                Assert.That(r1.Warnings.Any(w => w.Contains(_m.ArcWall.Id.ToString())), Is.True,
                    $"curved wall {_m.ArcWall.Id} was skipped silently — no warning names it. {Dump(r1)}");

                // Stacked wall (when the template has one): dimensioned correctly OR named in a warning. Never silent.
                if (_m.StackedWall != null)
                {
                    var ids = new HashSet<ElementId> { _m.StackedWall.Id };
                    foreach (var id in _m.StackedWall.GetStackedWallMemberIds()) ids.Add(id);
                    bool dimensioned = dims.Any(d => ReferencedIds(Doc, d).Overlaps(ids));
                    bool named = r1.Warnings.Any(w => ids.Any(id => w.Contains(id.ToString())));
                    Assert.That(dimensioned || named, Is.True,
                        $"stacked wall {_m.StackedWall.Id} (members {string.Join(",", ids)}) got neither a dimension nor a warning. {Dump(r1)}");
                }
                else Assert.Warn("Stacked-wall case not exercised: the template has no stacked wall type.");

                Assert.That(r1.DimsPlaced, Is.EqualTo(dims.Count),
                    "result.DimsPlaced disagrees with the dimensions actually present in the view.");

                // Re-run: nothing new.
                var mid = Snapshot(Doc, view);
                var r2 = RunEngine(Doc, "RunWallLength #2",
                    r => ElementDimensioner.RunWallLength(Doc, view, Pack(), Rule("AutoDimWallLength"), r));
                var added = NewSince<Element>(Doc, view, mid);
                Assert.That(added, Is.Empty,
                    $"re-run created {added.Count} new element(s) ({string.Join(", ", added.Select(e => e.Id))}). {Dump(r2)}");
                Assert.That(r2.DimsPlaced, Is.Zero, "re-run reported placing dimensions.");
            });
        }

        // ─────────────────────────────────────────────────────────────────
        //  AutoDimOpenings
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void Openings_OneChainPerHostWall_SegmentsOrderedAlongTheWall_ReRunAddsNothing()
        {
            Assume.That(_m.Openings.Count, Is.GreaterThan(0), "No doors/windows could be placed: " + _m.Describe());

            Isolated(Doc, "openings", () =>
            {
                var view = NewPlan(Doc, _m.Level1, "openings");
                var before = Snapshot(Doc, view);

                var r1 = RunEngine(Doc, "RunOpenings #1",
                    r => ElementDimensioner.RunOpenings(Doc, view, Pack(), Rule("AutoDimOpenings"), r));
                var dims = NewSince<Dimension>(Doc, view, before);

                var host = _m.OpeningHost;
                var chains = dims.Where(d => ReferencedIds(Doc, d).Overlaps(_m.Openings.Select(o => o.Id))).ToList();
                Assert.That(chains.Count, Is.EqualTo(1),
                    $"host wall {host.Id}: expected ONE chain carrying every opening, found {chains.Count}. {Dump(r1)}");
                var chain = chains[0];

                Assert.That(chain.NumberOfSegments, Is.EqualTo(_m.Openings.Count + 1),
                    $"chain on wall {host.Id} has {chain.NumberOfSegments} segments; {_m.Openings.Count} openings between two " +
                    $"end caps should give {_m.Openings.Count + 1}. {Dump(r1)}");

                // Expected boundaries along the wall: start cap, each opening centre, end cap.
                var axis = (Line)((LocationCurve)host.Location).Curve;
                var start = axis.GetEndPoint(0); var dir = axis.Direction;
                double length = axis.Length;
                var stations = _m.Openings
                    .Select(o => (((LocationPoint)o.Location).Point - start).DotProduct(dir))
                    .OrderBy(s => s).ToList();
                var bounds = new List<double> { 0 }; bounds.AddRange(stations); bounds.Add(length);
                var expected = bounds.Zip(bounds.Skip(1), (a, b) => b - a).ToList();

                var actual = new List<double>();
                foreach (DimensionSegment s in chain.Segments) actual.Add(s.Value ?? double.NaN);

                // Reading order: the chain may be listed start→end or end→start, never shuffled.
                var reversed = Enumerable.Reverse(expected).ToList();
                bool fwd = actual.Zip(expected, (a, e) => Math.Abs(a - e) <= OneMmFt).All(x => x);
                bool rev = actual.Zip(reversed, (a, e) => Math.Abs(a - e) <= OneMmFt).All(x => x);
                Assert.That(fwd || rev, Is.True,
                    $"segments [{string.Join(", ", actual.Select(v => (v * MmPerFt).ToString("F0")))}] mm do not match the wall's " +
                    $"geometry [{string.Join(", ", expected.Select(v => (v * MmPerFt).ToString("F0")))}] mm in either direction — " +
                    "openings are out of order along the wall, or the chain does not start/end at the wall's ends.");

                // Segment origins advance monotonically along the wall.
                var origins = new List<double>();
                foreach (DimensionSegment s in chain.Segments) origins.Add((s.Origin - start).DotProduct(dir));
                bool increasing = origins.Zip(origins.Skip(1), (a, b) => b > a).All(x => x);
                bool decreasing = origins.Zip(origins.Skip(1), (a, b) => b < a).All(x => x);
                Assert.That(increasing || decreasing, Is.True,
                    $"segment origins along the wall [{string.Join(", ", origins.Select(v => (v * MmPerFt).ToString("F0")))}] mm cross over.");

                // Walls without openings get no chain.
                foreach (var w in _m.StraightWalls.Where(w => w.Id != host.Id))
                    Assert.That(dims.Any(d => ReferencedIds(Doc, d).Contains(w.Id)), Is.False,
                        $"wall {w.Id} has no openings but received an opening chain.");

                var mid = Snapshot(Doc, view);
                var r2 = RunEngine(Doc, "RunOpenings #2",
                    r => ElementDimensioner.RunOpenings(Doc, view, Pack(), Rule("AutoDimOpenings"), r));
                var added = NewSince<Element>(Doc, view, mid);
                Assert.That(added, Is.Empty, $"re-run created {added.Count} new element(s). {Dump(r2)}");
            });
        }

        // ─────────────────────────────────────────────────────────────────
        //  AutoDimColumnGrid
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void ColumnToGrid_MeasuresAcrossTheNearestGrid_OnGridColumnSkipped_ReRunAddsNothing()
        {
            Assume.That(_m.ColumnsOffGrid.Count, Is.GreaterThan(0), "No structural columns could be placed: " + _m.Describe());

            Isolated(Doc, "column grid", () =>
            {
                var view = NewPlan(Doc, _m.Level1, "column-grid");
                var before = Snapshot(Doc, view);

                var r1 = RunEngine(Doc, "RunColumnToGrid #1",
                    r => ElementDimensioner.RunColumnToGrid(Doc, view, Pack(), Rule("AutoDimColumnGrid"), r));
                var dims = NewSince<Dimension>(Doc, view, before);

                foreach (var (col, offsetMm) in _m.ColumnsOffGrid)
                {
                    var p = ((LocationPoint)col.Location).Point;
                    var nearest = _m.Grids
                        .Select(g => (G: g, D: ElementDimensioner.PerpDistanceFt(p, (Line)g.Curve)))
                        .OrderBy(t => t.D).First();

                    var onCol = dims.Where(d => ReferencedIds(Doc, d).Contains(col.Id)).ToList();
                    Assert.That(onCol.Count, Is.EqualTo(1),
                        $"column {col.Id}: expected one setting-out dimension, found {onCol.Count}. {Dump(r1)}");
                    var dim = onCol[0];

                    Assert.That(ReferencedIds(Doc, dim).Contains(nearest.G.Id), Is.True,
                        $"column {col.Id}: dimensioned to grid(s) {string.Join(",", ReferencedIds(Doc, dim))}, " +
                        $"nearest is {nearest.G.Name} ({nearest.G.Id}).");

                    var gridDir = ((Line)nearest.G.Curve).Direction;
                    var dimDir = ((Line)dim.Curve).Direction;
                    Assert.That(Math.Abs(dimDir.DotProduct(gridDir)), Is.LessThan(1e-3),
                        $"column {col.Id}: the dimension line runs ALONG grid {nearest.G.Name} " +
                        $"(|cos| = {Math.Abs(dimDir.DotProduct(gridDir)):F3}); a setting-out dimension measures ACROSS it.");

                    Assert.That(dim.Value, Is.Not.Null.And.GreaterThan(0.0),
                        $"column {col.Id}: zero or missing value — BestAlignedReference chose the plane parallel to the grid.");
                    Assert.That(dim.Value.Value * MmPerFt, Is.EqualTo(offsetMm).Within(1.0),
                        $"column {col.Id}: reads {dim.Value.Value * MmPerFt:F1} mm, the column sits {offsetMm} mm off grid {nearest.G.Name}.");
                }

                if (_m.ColumnOnGrid != null)
                    Assert.That(dims.Any(d => ReferencedIds(Doc, d).Contains(_m.ColumnOnGrid.Id)), Is.False,
                        $"on-grid column {_m.ColumnOnGrid.Id} (on a grid intersection) received a dimension; it should be skipped. {Dump(r1)}");

                var mid = Snapshot(Doc, view);
                var r2 = RunEngine(Doc, "RunColumnToGrid #2",
                    r => ElementDimensioner.RunColumnToGrid(Doc, view, Pack(), Rule("AutoDimColumnGrid"), r));
                var added = NewSince<Element>(Doc, view, mid);
                Assert.That(added, Is.Empty, $"re-run created {added.Count} new element(s). {Dump(r2)}");
            });
        }
    }
}
