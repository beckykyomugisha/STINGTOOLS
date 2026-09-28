// StingTools.Revit.SmokeTests — shared plumbing for the in-Revit tests.
//
// Isolation model: ONE model per run (building it costs seconds), and every test
// runs inside a TransactionGroup that is ROLLED BACK — including the view it
// annotates. So no test can see another's annotations, and order does not matter.
// Inside the group each engine pass is its own committed Transaction, which is what
// a real run looks like and what the re-run (idempotency) checks need: an engine's
// "already annotated?" index reads committed, regenerated references.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using NUnit.Framework;
using StingTools.Core.Drawing;

namespace StingTools.Revit.SmokeTests.Fixtures
{
    internal static class SmokeModelCache
    {
        private static SmokeModel _model;
        private static Exception _buildError;

        public static SmokeModel Get(Application app)
        {
            if (_model != null) return _model;
            if (_buildError != null)
                Assert.Fail($"Smoke model could not be built earlier in this run: {_buildError.Message}");
            try
            {
                _model = SmokeModelBuilder.Build(app);
                TestContext.Progress.WriteLine($"[smoke] model built: {_model.Describe()}");
                return _model;
            }
            catch (Exception ex)
            {
                _buildError = ex;
                Assert.Fail($"Smoke model could not be built: {ex}");
                return null;
            }
        }

        public static void Close()
        {
            try { _model?.Doc?.Close(false); }
            catch (Exception ex) { TestContext.Progress.WriteLine($"[smoke] closing the model failed: {ex.Message}"); }
            _model = null;
        }
    }

    internal static class RevitHarness
    {
        public const double MmPerFt = SmokeModelBuilder.MmPerFt;
        /// <summary>The ±1 mm the plan allows, in feet.</summary>
        public static readonly double OneMmFt = 1.0 / MmPerFt;

        /// <summary>
        /// Run <paramref name="body"/> inside a TransactionGroup that is always rolled
        /// back, so the shared model is pristine for the next test.
        /// </summary>
        public static void Isolated(Document doc, string name, Action body)
        {
            using (var group = new TransactionGroup(doc, "STING smoke — " + name))
            {
                group.Start();
                try { body(); }
                finally { if (group.HasStarted()) group.RollBack(); }
            }
        }

        /// <summary>One committed transaction, warnings swallowed (a modal would hang the run).</summary>
        public static void Tx(Document doc, string name, Action body)
        {
            using (var tx = new Transaction(doc, "STING smoke — " + name))
            {
                tx.Start();
                SmokeModelBuilder.SuppressWarnings(tx);
                try
                {
                    body();
                    var status = tx.Commit();
                    if (status != TransactionStatus.Committed)
                        Assert.Fail($"Transaction '{name}' did not commit (status {status}).");
                }
                catch
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    throw;
                }
            }
        }

        /// <summary>A fresh, template-free, uncropped Coordination floor plan on <paramref name="level"/>.</summary>
        public static ViewPlan NewPlan(Document doc, Level level, string name)
        {
            ViewPlan view = null;
            Tx(doc, "plan " + name, () =>
            {
                var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .First(t => t.ViewFamily == ViewFamily.FloorPlan);
                view = ViewPlan.Create(doc, vft.Id, level.Id);
                view.Name = $"STING smoke - {name} - {Guid.NewGuid().ToString("N").Substring(0, 6)}";
                view.ViewTemplateId = ElementId.InvalidElementId;
                try { view.Discipline = ViewDiscipline.Coordination; } catch (Exception ex) { TestContext.Progress.WriteLine($"[smoke] discipline: {ex.Message}"); }
                view.DetailLevel = ViewDetailLevel.Fine;
                view.CropBoxActive = false;
            });
            return view;
        }

        /// <summary>
        /// A section looking at <paramref name="along"/>'s run from the side: view plane
        /// vertical and containing the pipe axis, so both pipe ends project to distinct points.
        /// </summary>
        public static ViewSection NewSectionAlong(Document doc, Line along, string name)
        {
            ViewSection view = null;
            Tx(doc, "section " + name, () =>
            {
                var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .First(t => t.ViewFamily == ViewFamily.Section);
                var a = along.GetEndPoint(0); var b = along.GetEndPoint(1);
                var dir = new XYZ(b.X - a.X, b.Y - a.Y, 0).Normalize();
                var up = XYZ.BasisZ;
                var viewDir = dir.CrossProduct(up);                // horizontal, perpendicular to the run
                var mid = (a + b) * 0.5;
                var tf = Transform.Identity;
                tf.Origin = mid; tf.BasisX = dir; tf.BasisY = up; tf.BasisZ = viewDir;
                double half = along.Length / 2 + 3.0;
                var box = new BoundingBoxXYZ
                {
                    Transform = tf,
                    Min = new XYZ(-half, -6.0, -3.0),
                    Max = new XYZ(half, 6.0, 3.0),
                };
                view = ViewSection.CreateSection(doc, vft.Id, box);
                view.Name = $"STING smoke - {name} - {Guid.NewGuid().ToString("N").Substring(0, 6)}";
                view.ViewTemplateId = ElementId.InvalidElementId;
                view.DetailLevel = ViewDetailLevel.Fine;
            });
            return view;
        }

        /// <summary>Every non-type element owned by / visible in the view — the idempotency baseline.</summary>
        public static HashSet<ElementId> Snapshot(Document doc, View view) =>
            new HashSet<ElementId>(new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType().ToElementIds());

        public static List<T> NewSince<T>(Document doc, View view, HashSet<ElementId> before) where T : Element =>
            new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType().ToElements()
                .Where(e => !before.Contains(e.Id)).OfType<T>().ToList();

        /// <summary>Ids of the elements a dimension (or spot) references.</summary>
        public static HashSet<ElementId> ReferencedIds(Document doc, Dimension d)
        {
            var set = new HashSet<ElementId>();
            if (d?.References == null) return set;
            foreach (Reference r in d.References)
            {
                var el = doc.GetElement(r);
                if (el != null) set.Add(el.Id);
            }
            return set;
        }

        /// <summary>Run one engine pass in its own committed transaction and return its result.</summary>
        public static AnnotationResult RunEngine(Document doc, string name,
            Action<AnnotationResult> engine)
        {
            var result = new AnnotationResult();
            Tx(doc, name, () => engine(result));
            TestContext.Progress.WriteLine(
                $"[smoke] {name}: dims {result.DimsPlaced}, spots {result.SpotsPlaced}, decorative {result.DecorativePlaced}, " +
                $"skipped {result.Skipped}, warnings {result.Warnings.Count}" +
                (result.Warnings.Count == 0 ? "" : "\n    - " + string.Join("\n    - ", result.Warnings)));
            return result;
        }

        public static string Dump(AnnotationResult r) =>
            r == null ? "(no result)" : "warnings: [" + string.Join(" | ", r.Warnings) + "]";

        /// <summary>Distance between the projections of two points onto the view plane.</summary>
        public static double InPlaneDistance(View view, XYZ a, XYZ b)
        {
            var n = view.ViewDirection.Normalize();
            var d = b - a;
            return (d - n * d.DotProduct(n)).GetLength();
        }
    }
}
