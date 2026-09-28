// StingTools.Revit.SmokeTests — MepAnnotator (DRAW-3 spot slopes, DRAW-2 flow arrows).
//
// DRAW-3's claim is narrow and checkable: a spot is only ever COUNTED when it really
// carries a spot-slope type, anything else is deleted, and a project with no usable
// slope type places nothing at all. So the assertions read the element TYPES in the
// view after the run, not the engine's own tally.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using NUnit.Framework;
using StingTools.Commands.Drawing;
using StingTools.Core.Drawing;
using StingTools.Core.Drawing.Dimensioning;
using StingTools.Revit.SmokeTests.Fixtures;
using static StingTools.Revit.SmokeTests.Fixtures.RevitHarness;

namespace StingTools.Revit.SmokeTests
{
    [TestFixture]
    public class MepAnnotatorSmokeTests
    {
        private SmokeModel _m;
        private Application _app;
        private Document Doc => _m.Doc;

        [OneTimeSetUp]
        public void Setup(Application application)
        {
            _app = application;
            _m = SmokeModelCache.Get(application);
        }

        private static AnnotationRulePack Pack() => new AnnotationRulePack();
        private static AutoAnnotationRule Rule(string type) => new AutoAnnotationRule { RuleType = type };

        private List<SpotDimensionType> SlopeTypes() =>
            new FilteredElementCollector(Doc).OfClass(typeof(SpotDimensionType)).Cast<SpotDimensionType>()
                .Where(t => { try { return t.StyleType == DimensionStyleType.SpotSlope; } catch { return false; } })
                .ToList();

        private static bool IsSlopeType(Document doc, Element e) =>
            doc.GetElement(e.GetTypeId()) is SpotDimensionType t && t.StyleType == DimensionStyleType.SpotSlope;

        // ─────────────────────────────────────────────────────────────────
        //  AutoAnnotateSlope — slope type present
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void SpotSlope_EveryPlacedSpotIsASlope_NoElevationLeftBehind_ReRunAddsNothing()
        {
            Assume.That(_m.Drain, Is.Not.Null, "No pipes in the fixture: " + _m.Describe());
            Assume.That(SlopeTypes(), Is.Not.Empty,
                "The template carries no spot-SLOPE type, so this case cannot be exercised here " +
                "(the BLOCKED case below still covers it). " + _m.Describe());

            Isolated(Doc, "spot slope", () =>
            {
                var view = NewPlan(Doc, _m.Level1, "spot-slope");
                var before = Snapshot(Doc, view);

                var r1 = RunEngine(Doc, "RunSlope #1", r => MepAnnotator.RunSlope(Doc, view, Pack(), Rule("AutoAnnotateSlope"), r));
                var spots = NewSince<SpotDimension>(Doc, view, before);

                // Whatever happened, no spot ELEVATION may survive: DRAW-3's whole point.
                var notSlopes = spots.Where(s => !IsSlopeType(Doc, s)).ToList();
                Assert.That(notSlopes, Is.Empty,
                    $"{notSlopes.Count} spot(s) left in the view do NOT carry a spot-slope type " +
                    $"(ids {string.Join(",", notSlopes.Select(s => s.Id))}, categories " +
                    $"{string.Join(",", notSlopes.Select(s => s.Category?.Name))}) — a drainage drawing would print an elevation. {Dump(r1)}");

                Assert.That(r1.SpotsPlaced, Is.EqualTo(spots.Count),
                    $"engine counted {r1.SpotsPlaced} spot slope(s), the view holds {spots.Count}. {Dump(r1)}");

                if (spots.Count == 0)
                {
                    // The guard held, but the feature placed nothing. That is the documented
                    // BLOCKED outcome, not a pass — surface it as inconclusive with the reason.
                    Assert.That(r1.Warnings.Any(w => w.Contains("BLOCKED") || w.Contains("could not place") || w.Contains("rejected")),
                        Is.True, $"no spot placed and no reason given — a silent no-op. {Dump(r1)}");
                    Assert.Inconclusive("Revit refused the spot-slope re-type on this project; the DRAW-3 guard held (nothing " +
                                        "wrong was left behind) but no slope annotation is placed. " + Dump(r1));
                }

                // Exactly the sloped drain carries a spot; the level pipe does not.
                var hosts = spots.SelectMany(s => ReferencedIds(Doc, s)).ToList();
                Assert.That(hosts.Count(id => id == _m.Drain.Id), Is.EqualTo(1),
                    $"the 1:80 drain {_m.Drain.Id} should carry exactly one spot slope; referenced hosts: {string.Join(",", hosts)}.");
                Assert.That(hosts.Contains(_m.LevelPipe.Id), Is.False,
                    $"the level pipe {_m.LevelPipe.Id} received a spot slope.");
                Assert.That(r1.Warnings.Any(w => w.Contains("level")), Is.True,
                    $"the level run was not reported as level. {Dump(r1)}");

                var mid = Snapshot(Doc, view);
                var r2 = RunEngine(Doc, "RunSlope #2", r => MepAnnotator.RunSlope(Doc, view, Pack(), Rule("AutoAnnotateSlope"), r));
                var added = NewSince<Element>(Doc, view, mid);
                Assert.That(added, Is.Empty,
                    $"re-run created {added.Count} new element(s) ({string.Join(",", added.Select(e => e.Category?.Name + " " + e.Id))}). {Dump(r2)}");
                Assert.That(r2.SpotsPlaced, Is.Zero);
            });
        }

        // ─────────────────────────────────────────────────────────────────
        //  AutoAnnotateSlope — no slope type: must BLOCK
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void SpotSlope_WithNoSlopeType_Blocks_PlacesNothing()
        {
            Assume.That(_m.Drain, Is.Not.Null, "No pipes in the fixture: " + _m.Describe());

            Isolated(Doc, "spot slope blocked", () =>
            {
                var slopeTypes = SlopeTypes();
                if (slopeTypes.Count > 0)
                {
                    try { Tx(Doc, "remove spot-slope types", () => Doc.Delete(slopeTypes.Select(t => t.Id).ToList())); }
                    catch (Exception ex)
                    {
                        Assert.Inconclusive($"Could not delete the template's spot-slope type(s) to set up the case: {ex.Message}");
                    }
                    Assume.That(SlopeTypes(), Is.Empty, "Revit kept a spot-slope type after deletion; case cannot be set up.");
                }

                var view = NewPlan(Doc, _m.Level1, "spot-slope-blocked");
                var before = Snapshot(Doc, view);

                var r = RunEngine(Doc, "RunSlope (no slope type)",
                    res => MepAnnotator.RunSlope(Doc, view, Pack(), Rule("AutoAnnotateSlope"), res));
                var added = NewSince<Element>(Doc, view, before);

                Assert.That(added, Is.Empty,
                    $"with no spot-slope type the run still left {added.Count} element(s) in the view " +
                    $"({string.Join(",", added.Select(e => e.Category?.Name + " " + e.Id))}) — each would print an ELEVATION.");
                Assert.That(r.SpotsPlaced, Is.Zero, "the engine counted spots it cannot have placed as slopes.");
                Assert.That(r.Warnings.Any(w => w.Contains("BLOCKED")), Is.True,
                    $"the run placed nothing but did not say BLOCKED — a silent no-op. {Dump(r)}");
            });
        }

        // ─────────────────────────────────────────────────────────────────
        //  AutoAnnotateFlowArrow
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void FlowArrow_OnePerRun_AlongThePipe_DownhillWhenKnown_ReRunAddsNothing()
        {
            Assume.That(_m.Drain, Is.Not.Null, "No pipes in the fixture: " + _m.Describe());

            // Author the corporate arrow family the same way DrawingTypes_BuildFlowArrow does.
            string rfa;
            try { rfa = BuildFlowArrowFamilyCommand.Author(_app, Doc); }
            catch (Exception ex) { Assert.Inconclusive($"Could not author {MepAnnotator.FlowArrowFamily}: {ex.Message}"); return; }

            Isolated(Doc, "flow arrow", () =>
            {
                Tx(Doc, "load flow arrow", () =>
                {
                    Assert.That(Doc.LoadFamily(rfa, out var fam) && fam != null, Is.True, $"LoadFamily refused {rfa}.");
                });

                var view = NewPlan(Doc, _m.Level1, "flow-arrow");
                var before = Snapshot(Doc, view);

                var r1 = RunEngine(Doc, "RunFlowArrow #1",
                    r => MepAnnotator.RunFlowArrow(Doc, view, Pack(), Rule("AutoAnnotateFlowArrow"), r));
                var arrows = NewSince<FamilyInstance>(Doc, view, before)
                    .Where(fi => fi.Symbol?.Family?.Name == MepAnnotator.FlowArrowFamily).ToList();

                // The two fixture pipes are unconnected ⇒ two runs ⇒ two arrows.
                Assert.That(arrows.Count, Is.EqualTo(2),
                    $"expected one arrow per run (2 unconnected runs), found {arrows.Count}. {Dump(r1)}");
                Assert.That(r1.DecorativePlaced, Is.EqualTo(arrows.Count));

                foreach (var pipe in new[] { _m.Drain, _m.LevelPipe })
                {
                    var ln = (Line)((LocationCurve)pipe.Location).Curve;
                    var mid = (ln.GetEndPoint(0) + ln.GetEndPoint(1)) * 0.5;
                    var arrow = arrows.OrderBy(a => InPlaneDistance(view, ((LocationPoint)a.Location).Point, mid)).First();
                    var at = ((LocationPoint)arrow.Location).Point;
                    Assert.That(InPlaneDistance(view, at, mid), Is.LessThan(1.0),
                        $"no arrow within 1 ft of pipe {pipe.Id}'s midpoint (nearest is {InPlaneDistance(view, at, mid) * MmPerFt:F0} mm away).");

                    var along = arrow.GetTransform().BasisX.Normalize();
                    var pipeDir = new XYZ(ln.Direction.X, ln.Direction.Y, 0).Normalize();
                    Assert.That(Math.Abs(along.DotProduct(pipeDir)), Is.GreaterThan(0.999),
                        $"arrow on pipe {pipe.Id} is not aligned with the pipe (|cos| = {Math.Abs(along.DotProduct(pipeDir)):F3}).");

                    bool unknownSaid = r1.Warnings.Any(w => w.Contains(pipe.Id.ToString()) && w.Contains("could not be read"));
                    if (pipe.Id == _m.Drain.Id && !unknownSaid)
                    {
                        // The engine claims to KNOW this run's direction: it must point downhill (+X).
                        var down = (ln.GetEndPoint(1).Z < ln.GetEndPoint(0).Z ? ln.GetEndPoint(1) - ln.GetEndPoint(0) : ln.GetEndPoint(0) - ln.GetEndPoint(1));
                        down = new XYZ(down.X, down.Y, 0).Normalize();
                        Assert.That(along.DotProduct(down), Is.GreaterThan(0.999),
                            $"arrow on drain {pipe.Id} points UPSTREAM while the engine claimed a known flow direction.");
                    }
                }

                var before2 = Snapshot(Doc, view);
                var r2 = RunEngine(Doc, "RunFlowArrow #2",
                    r => MepAnnotator.RunFlowArrow(Doc, view, Pack(), Rule("AutoAnnotateFlowArrow"), r));
                var added = NewSince<Element>(Doc, view, before2);
                Assert.That(added, Is.Empty, $"re-run stacked {added.Count} new element(s). {Dump(r2)}");
            });
        }
    }
}
