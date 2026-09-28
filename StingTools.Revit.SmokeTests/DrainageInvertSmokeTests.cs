// StingTools.Revit.SmokeTests — DrainageInvertDimensioner against a real model.
//
// The expected IL is computed here from the pipe's own parameters: centreline Z of
// each end + the survey-point datum − INTERNAL radius, formatted at the configured
// precision (InvertMath is the pure rule set; it is used for the arithmetic and the
// format, not asked what the answer is). The fixture sets the shared elevation to
// +45.250 m, so an engine that reported on the internal origin, used the nominal
// diameter, or annotated the midpoint would print a visibly different number.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using NUnit.Framework;
using StingTools.Core.Drawing;
using StingTools.Core.Drawing.Dimensioning;
using StingTools.Core.Plumbing;
using StingTools.Revit.SmokeTests.Fixtures;
using static StingTools.Revit.SmokeTests.Fixtures.RevitHarness;

namespace StingTools.Revit.SmokeTests
{
    [TestFixture]
    public class DrainageInvertSmokeTests
    {
        private SmokeModel _m;
        private Document Doc => _m.Doc;

        [OneTimeSetUp]
        public void Setup(Application application) => _m = SmokeModelCache.Get(application);

        private static AnnotationRulePack Pack() => new AnnotationRulePack();
        private static AutoAnnotationRule Rule() => new AutoAnnotationRule { RuleType = "AutoSpotInvert" };

        private sealed class Expected
        {
            public XYZ UpEnd, DownEnd;
            public string UpIl, DownIl, Gradient;
        }

        /// <summary>The drain's expected notes, from its parameters and the survey-point datum.</summary>
        private Expected ExpectFor(Pipe pipe)
        {
            var sp = BasePoint.GetSurveyPoint(Doc);
            double datumM = (sp.SharedPosition.Z - sp.Position.Z) * 0.3048;
            Assume.That(datumM, Is.EqualTo(SmokeModel.SharedElevationM).Within(0.001),
                $"fixture datum not applied: survey point offset is {datumM:F3} m, expected {SmokeModel.SharedElevationM:F3} m.");

            var idParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_INNER_DIAM_PARAM);
            Assume.That(idParam != null && idParam.HasValue && idParam.AsDouble() > 0, Is.True,
                $"pipe {pipe.Id} carries no internal diameter; the inner-radius rule cannot be checked on this template.");
            double idM = idParam.AsDouble() * 0.3048;

            var ln = (Line)((LocationCurve)pipe.Location).Curve;
            var a = ln.GetEndPoint(0); var b = ln.GetEndPoint(1);
            bool aUp = a.Z > b.Z;
            var up = aUp ? a : b; var dn = aUp ? b : a;
            double upZ = up.Z * 0.3048 + datumM, dnZ = dn.Z * 0.3048 + datumM;
            int dp = IlReportingOptions.Default.Decimals;

            double runM = new XYZ(b.X - a.X, b.Y - a.Y, 0).GetLength() * 0.3048;
            return new Expected
            {
                UpEnd = up,
                DownEnd = dn,
                UpIl = InvertMath.FormatIl(InvertMath.Invert(upZ, idM, null, out _).Value, dp),
                DownIl = InvertMath.FormatIl(InvertMath.Invert(dnZ, idM, null, out _).Value, dp),
                // The fixture is built at exactly 1:80; the gradient is asserted as the literal.
                Gradient = "1:" + Math.Round(runM / (upZ - dnZ)).ToString("0", CultureInfo.InvariantCulture),
            };
        }

        private static List<TextNote> OurNotes(Document doc, View view) =>
            new FilteredElementCollector(doc, view.Id).OfClass(typeof(TextNote)).Cast<TextNote>()
                .Where(t => InvertMath.IsOurNote(t.Text)).ToList();

        private static string T(TextNote n) => (n.Text ?? "").Trim();

        /// <summary>Assert the drain carries IL notes at both ends with the expected values, and one gradient.</summary>
        private void AssertNotesOnDrain(View view, List<TextNote> notes, Expected e, AnnotationResult r)
        {
            double paperFt = Math.Max(1, view.Scale) / 304.8;   // 1 paper mm in model feet
            double near = 10 * paperFt;                          // within 10 paper-mm of the end

            var ils = notes.Where(n => T(n).StartsWith("IL ")).ToList();
            var grads = notes.Where(n => T(n).StartsWith("1:")).ToList();

            TextNote NearestIl(XYZ p) => ils.OrderBy(n => InPlaneDistance(view, n.Coord, p)).FirstOrDefault();

            var atUp = NearestIl(e.UpEnd);
            var atDn = NearestIl(e.DownEnd);
            Assert.That(atUp, Is.Not.Null, $"no IL note at all in '{view.Name}'. {Dump(r)}");
            Assert.That(InPlaneDistance(view, atUp.Coord, e.UpEnd), Is.LessThan(near),
                $"no IL note near the upstream end (nearest is {InPlaneDistance(view, atUp.Coord, e.UpEnd) * 304.8:F0} mm away). {Dump(r)}");
            Assert.That(InPlaneDistance(view, atDn.Coord, e.DownEnd), Is.LessThan(near),
                $"no IL note near the downstream end (nearest is {InPlaneDistance(view, atDn.Coord, e.DownEnd) * 304.8:F0} mm away). {Dump(r)}");
            Assert.That(atUp.Id, Is.Not.EqualTo(atDn.Id), "one IL note is serving as both ends.");

            Assert.That(T(atUp), Is.EqualTo(e.UpIl),
                $"upstream IL reads '{T(atUp)}', expected '{e.UpIl}' (centreline − internal radius on the survey-point datum).");
            Assert.That(T(atDn), Is.EqualTo(e.DownIl),
                $"downstream IL reads '{T(atDn)}', expected '{e.DownIl}'.");

            Assert.That(grads.Select(T), Is.EquivalentTo(new[] { e.Gradient }),
                $"expected exactly one gradient note '{e.Gradient}', found [{string.Join(", ", grads.Select(T))}].");
            Assert.That(e.Gradient, Is.EqualTo("1:80"), "fixture sanity: the drain is built at 1:80.");
        }

        [Test]
        public void Invert_PlanView_IlAtBothEnds_Gradient_OnlyOnDrainage_ReRunUpdatesInPlace_MovedPipeNotesFollow_DeletedPipeNotesGo()
        {
            Assume.That(_m.Drain, Is.Not.Null, "No pipes in the fixture: " + _m.Describe());
            var e = ExpectFor(_m.Drain);

            Isolated(Doc, "invert plan", () =>
            {
                var view = NewPlan(Doc, _m.Level1, "invert");
                var before = Snapshot(Doc, view);

                var r1 = RunEngine(Doc, "DrainageInvert #1", r => DrainageInvertDimensioner.Run(Doc, view, Pack(), Rule(), r));
                var notes = OurNotes(Doc, view);

                Assert.That(r1.Warnings.Any(w => w.Contains("NOMINAL")), Is.False,
                    $"ILs fell back to the nominal size although the pipe has an internal diameter. {Dump(r1)}");
                Assert.That(notes.Count, Is.EqualTo(3),
                    $"expected 3 notes (2 ILs + 1 gradient) on the one drainage pipe, found {notes.Count}: " +
                    $"[{string.Join(", ", notes.Select(T))}]. The level pipe is cold water and must get none. {Dump(r1)}");
                AssertNotesOnDrain(view, notes, e, r1);

                // Nothing near the non-drainage pipe.
                var lvl = (Line)((LocationCurve)_m.LevelPipe.Location).Curve;
                var flat = Line.CreateBound(new XYZ(lvl.GetEndPoint(0).X, lvl.GetEndPoint(0).Y, 0),
                                            new XYZ(lvl.GetEndPoint(1).X, lvl.GetEndPoint(1).Y, 0));
                Assert.That(notes.Any(n => ElementDimensioner.PerpDistanceFt(new XYZ(n.Coord.X, n.Coord.Y, 0), flat) < 1000 / 304.8), Is.False,
                    $"a note landed on cold-water pipe {_m.LevelPipe.Id}; only drainage runs get inverts.");

                // Re-run: updates in place — no new element, same texts.
                var ids1 = notes.Select(n => n.Id).OrderBy(i => i.Value).ToList();
                var mid = Snapshot(Doc, view);
                var r2 = RunEngine(Doc, "DrainageInvert #2", r => DrainageInvertDimensioner.Run(Doc, view, Pack(), Rule(), r));
                var added = NewSince<Element>(Doc, view, mid);
                Assert.That(added, Is.Empty, $"re-run created {added.Count} new element(s) instead of updating in place. {Dump(r2)}");
                var notes2 = OurNotes(Doc, view);
                Assert.That(notes2.Select(n => n.Id).OrderBy(i => i.Value).ToList(), Is.EqualTo(ids1), "re-run replaced notes.");
                AssertNotesOnDrain(view, notes2, e, r2);

                // Move the drain 2 m sideways. The notes are provenance-stamped with
                // their pipe and end, so they must FOLLOW it: same ids, new positions,
                // no orphans and nothing created.
                Tx(Doc, "move drain", () => ElementTransformUtils.MoveElement(Doc, _m.Drain.Id, new XYZ(0, 2000 / 304.8, 0)));
                var beforeMove = Snapshot(Doc, view);
                var r3 = RunEngine(Doc, "DrainageInvert #3 (moved)", r => DrainageInvertDimensioner.Run(Doc, view, Pack(), Rule(), r));
                Assert.That(r3.Warnings.Any(w => w.Contains("no longer sit on a pipe end")), Is.False,
                    $"moved pipe: stamped notes were treated as orphans instead of following the pipe. {Dump(r3)}");
                Assert.That(NewSince<Element>(Doc, view, beforeMove), Is.Empty,
                    $"moved pipe: new notes were created instead of the stamped ones moving. {Dump(r3)}");
                var notes3 = OurNotes(Doc, view);
                Assert.That(notes3.Select(n => n.Id).OrderBy(i => i.Value).ToList(), Is.EqualTo(ids1),
                    "moved pipe: the stamped notes were replaced rather than moved.");
                AssertNotesOnDrain(view, notes3, ExpectFor(_m.Drain), r3);

                // Delete the drain: its stamped notes are provably ours and must go.
                Tx(Doc, "delete drain", () => Doc.Delete(_m.Drain.Id));
                var r4 = RunEngine(Doc, "DrainageInvert #4 (deleted)", r => DrainageInvertDimensioner.Run(Doc, view, Pack(), Rule(), r));
                Assert.That(OurNotes(Doc, view), Is.Empty,
                    $"deleted pipe: its IL/gradient notes were left behind. {Dump(r4)}");
            });
        }

        [Test]
        public void Invert_SectionAlongTheRun_IlAtBothEnds_Gradient()
        {
            Assume.That(_m.Drain, Is.Not.Null, "No pipes in the fixture: " + _m.Describe());
            var e = ExpectFor(_m.Drain);

            Isolated(Doc, "invert section", () =>
            {
                var view = NewSectionAlong(Doc, (Line)((LocationCurve)_m.Drain.Location).Curve, "invert-section");
                var r1 = RunEngine(Doc, "DrainageInvert (section)", r => DrainageInvertDimensioner.Run(Doc, view, Pack(), Rule(), r));
                var notes = OurNotes(Doc, view);
                Assert.That(notes.Count, Is.EqualTo(3),
                    $"expected 3 notes in the section, found {notes.Count}: [{string.Join(", ", notes.Select(T))}]. {Dump(r1)}");
                AssertNotesOnDrain(view, notes, e, r1);

                var mid = Snapshot(Doc, view);
                var r2 = RunEngine(Doc, "DrainageInvert (section) #2", r => DrainageInvertDimensioner.Run(Doc, view, Pack(), Rule(), r));
                Assert.That(NewSince<Element>(Doc, view, mid), Is.Empty, $"re-run in section created new elements. {Dump(r2)}");
            });
        }
    }
}
