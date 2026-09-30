// StingTools — Drawing Template Manager · putting a view a command already made on a
// drawing-type sheet
//
// Most drawings are made BY DrawingProducer. A few are made elsewhere and only need a
// sheet: the SLD and the riser diagram (drafting views drawn by their own engines) and
// the per-panel circuit schedules. They used to be left off every sheet, or put on a
// sheet the person had to pick, unstamped. DrawingProducer.PlaceExistingView gives them
// the same sheet path production uses — find-or-create by stamp and context, title
// block, number and name from the drawing type, slot placement.
//
// What happens on a re-run is decided here. The SLD engine makes a NEW drafting view on
// every run, so the sheet from last time already holds last time's SLD. Placing the new
// one beside it would stack two diagrams; the previous one comes off the sheet (the view
// itself is kept — it is the record of that run) and the new one goes on.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    /// <summary>A view already on the target sheet: its id and drawing-type stamp.</summary>
    public sealed class PlacedView
    {
        public long ViewId { get; set; }
        public string DrawingTypeId { get; set; }
    }

    public sealed class PlacementDecision
    {
        /// <summary>The view is already on the sheet: nothing to place.</summary>
        public bool AlreadyPlaced { get; set; }
        /// <summary>Views whose viewports come off the sheet first (earlier runs' output).</summary>
        public List<long> RemoveViewIds { get; } = new List<long>();
    }

    public static class ExistingViewPlacement
    {
        /// <summary>
        /// For <paramref name="viewId"/> of drawing type <paramref name="drawingTypeId"/>
        /// going onto a sheet that holds <paramref name="onSheet"/>: is it already there,
        /// and which other views of the SAME drawing type make way. Views of any other
        /// type — a key plan, a legend someone added — are never touched.
        /// </summary>
        public static PlacementDecision Decide(IEnumerable<PlacedView> onSheet, long viewId, string drawingTypeId)
            => Decide(onSheet, viewId, drawingTypeId, null);

        /// <summary>
        /// As above; a view stamped with one of <paramref name="formerDrawingTypeIds"/>
        /// (the id this request routed to before a project re-routed it) is an earlier
        /// run's view too and makes way the same.
        /// </summary>
        public static PlacementDecision Decide(IEnumerable<PlacedView> onSheet, long viewId, string drawingTypeId,
            IEnumerable<string> formerDrawingTypeIds)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(drawingTypeId)) ids.Add(drawingTypeId);
            foreach (var f in formerDrawingTypeIds ?? Enumerable.Empty<string>())
                if (!string.IsNullOrEmpty(f)) ids.Add(f);

            var d = new PlacementDecision();
            foreach (var p in onSheet ?? Enumerable.Empty<PlacedView>())
            {
                if (p == null) continue;
                if (p.ViewId == viewId) { d.AlreadyPlaced = true; continue; }
                if (!string.IsNullOrEmpty(p.DrawingTypeId) && ids.Contains(p.DrawingTypeId)
                    && !d.RemoveViewIds.Contains(p.ViewId))
                    d.RemoveViewIds.Add(p.ViewId);
            }
            return d;
        }
    }
}
