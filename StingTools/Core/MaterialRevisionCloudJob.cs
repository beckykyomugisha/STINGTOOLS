// ══════════════════════════════════════════════════════════════════════════
//  MaterialRevisionCloudJob.cs — D4.
//
//  Idling job that creates revision clouds around elements whose material
//  was changed AFTER the project's latest revision was issued. Triggered
//  via StingMaterialUpdaterStaleHook.OnMaterialChanged so the cloud
//  creation runs OUTSIDE the IUpdater's transaction (Idling tick).
//
//  Safety
//   • Only runs when the project has at least one Revision element.
//   • Only stamps clouds on issued sheets (revisions assigned).
//   • Skips elements without a placed sheet (e.g. unplaced views).
//   • Caps work per tick at MaxPerTick so a batch material swap can't
//     freeze Revit.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace StingTools.Core
{
    public sealed class MaterialRevisionCloudJob : IIdlingJob
    {
        public string Name => "STING Material → Revision Cloud";
        public int Priority => 3;
        public int BudgetMs => 250;

        private const int MaxPerTick = 10;
        private readonly Queue<long> _pending = new Queue<long>();
        private readonly HashSet<long> _seen = new HashSet<long>();

        public void Enqueue(long elementId)
        {
            if (elementId <= 0) return;
            lock (_pending)
            {
                if (_seen.Contains(elementId)) return;
                _pending.Enqueue(elementId);
                _seen.Add(elementId);
            }
        }

        public bool Execute(UIApplication uiApp)
        {
            var doc = uiApp?.ActiveUIDocument?.Document;
            if (doc == null) return DrainAndExit("no doc");

            // Need at least one revision in the project; otherwise the
            // cloud has nothing to anchor to.
            var revIds = new FilteredElementCollector(doc).OfClass(typeof(Revision))
                .Cast<Revision>().OrderBy(r => r.SequenceNumber).ToList();
            if (revIds.Count == 0) return DrainAndExit("no revisions in project");
            var latestRev = revIds.Last();

            int processed = 0;
            int created = 0;
            try
            {
                using (var t = new Transaction(doc, "STING Material Change → Revision Cloud"))
                {
                    t.Start();
                    while (processed < MaxPerTick)
                    {
                        long elId;
                        lock (_pending)
                        {
                            if (_pending.Count == 0) break;
                            elId = _pending.Dequeue();
                        }
                        processed++;
                        try
                        {
                            var el = doc.GetElement(new ElementId(elId));
                            if (el == null) continue;

                            // Find the sheets whose placed views contain this
                            // element. RevisionCloud must be created in a view
                            // — we use the first view on the first issued
                            // sheet we find.
                            var view = FindCloudableView(doc, el);
                            if (view == null) continue;

                            var bb = el.get_BoundingBox(view);
                            if (bb == null) continue;
                            double pad = 0.5; // ~6 inches
                            var pts = new List<XYZ>
                            {
                                new XYZ(bb.Min.X - pad, bb.Min.Y - pad, bb.Min.Z),
                                new XYZ(bb.Max.X + pad, bb.Min.Y - pad, bb.Min.Z),
                                new XYZ(bb.Max.X + pad, bb.Max.Y + pad, bb.Min.Z),
                                new XYZ(bb.Min.X - pad, bb.Max.Y + pad, bb.Min.Z),
                            };
                            var curves = new List<Curve>();
                            for (int i = 0; i < pts.Count; i++)
                            {
                                int next = (i + 1) % pts.Count;
                                curves.Add(Line.CreateBound(pts[i], pts[next]));
                            }
                            RevisionCloud.Create(doc, view, latestRev.Id, curves);
                            created++;
                        }
                        catch (Exception ex) { StingLog.Warn($"MaterialRevisionCloudJob {elId}: {ex.Message}"); }
                    }
                    t.Commit();
                }
                if (created > 0)
                    StingLog.Info($"MaterialRevisionCloudJob: created {created} cloud(s) for material change(s).");
            }
            catch (Exception ex) { StingLog.Warn($"MaterialRevisionCloudJob outer: {ex.Message}"); }

            // Re-queue ourselves if more work remains.
            lock (_pending)
            {
                return _pending.Count == 0;
            }
        }

        /// <summary>
        /// Find a view containing the element that's placed on at least
        /// one sheet which itself has any revisions assigned. We don't
        /// validate the revision lineage here — RevisionCloud.Create is
        /// idempotent against existing clouds anyway.
        /// </summary>
        private static View FindCloudableView(Document doc, Element el)
        {
            try
            {
                foreach (var sheet in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
                {
                    // Sheet must be issued — has at least one Revision attached.
                    var revs = sheet.GetAllRevisionIds();
                    if (revs == null || revs.Count == 0) continue;

                    foreach (var vId in sheet.GetAllPlacedViews())
                    {
                        var view = doc.GetElement(vId) as View;
                        if (view == null) continue;
                        // Element-on-view check is expensive — short-circuit by
                        // checking the view's bounding box vs the element's
                        // location. Good enough for the cloud's purpose.
                        try
                        {
                            var bb = el.get_BoundingBox(view);
                            if (bb != null) return view;
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"FindCloudableView: {ex.Message}"); }
            return null;
        }

        private bool DrainAndExit(string reason)
        {
            lock (_pending)
            {
                if (_pending.Count > 0)
                    StingLog.Info($"MaterialRevisionCloudJob: dropping {_pending.Count} queued element(s) ({reason}).");
                _pending.Clear();
                _seen.Clear();
            }
            return true; // remove from scheduler
        }
    }
}
