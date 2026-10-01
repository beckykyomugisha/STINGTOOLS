// StingTools — Drawing Template Manager · linked elements in the tag pass (DTW-85)
//
// Every tag collector used to be host-only, so a plan whose MEP lives in a
// linked model (which production now produces, DTW-49) came out untagged with
// no word said. The tag pass now also walks the loaded links the view shows.
// NOT VERIFIED IN REVIT.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace StingTools.Core.Drawing
{
    public static partial class AnnotationRunner
    {
        /// <summary>
        /// Elements of <paramref name="bic"/> in each loaded link the view shows,
        /// links with none left out. A link that cannot be read is a warning, once
        /// per run, never a silent omission.
        /// </summary>
        private static List<(ViewLink Link, IList<Element> Elements)> LinkedElementsOf(
            Document doc, View view, BuiltInCategory bic, string catKey, AnnotationRunStats stats)
        {
            var result = new List<(ViewLink, IList<Element>)>();
            var warnings = new List<string>();
            foreach (var link in ViewLinks.InView(doc, view, warnings))
            {
                try
                {
                    var les = ViewLinks.Visible(doc, view, link, bic);
                    if (les.Count > 0) result.Add((link, les));
                }
                catch (Exception ex)
                {
                    warnings.Add($"{catKey}: link '{link.Name}' could not be read ({ex.Message}) — its elements are not tagged.");
                }
            }
            foreach (var w in warnings)
                if (!stats.Warnings.Contains(w)) stats.Warnings.Add(w);
            return result;
        }

        /// <summary>
        /// Rooms in linked models get a room tag through the link (NewRoomTag takes a
        /// LinkElementId). Spaces and areas cannot be tagged through a link — Revit's
        /// NewSpaceTag / NewAreaTag take only host elements — so those are counted and
        /// reported rather than left out in silence.
        /// </summary>
        private static void TagLinkedSpatial(Document doc, View view, BuiltInCategory bic, SpatialTagKind kind,
            string catKey, AnnotationRunStats stats, Dictionary<string, List<string>> alreadyTagged,
            bool skipIfTagged, bool isSpecialistRule, string placedFamily, ISet<string> specialistFamilies,
            ElementId tagTypeId, bool withLeader, SpatialElementTagOrientation? orientation)
        {
            var linked = LinkedElementsOf(doc, view, bic, catKey, stats);
            if (linked.Count == 0) return;

            if (!SpatialTagRouting.LinkedTaggable(kind))
            {
                int n = linked.Sum(t => t.Elements.Count);
                stats.Warnings.Add($"{catKey}: {n} element(s) in linked models not tagged — Revit cannot place a " +
                                   $"{kind.ToString().ToLowerInvariant()} tag on a linked {kind.ToString().ToLowerInvariant()}. " +
                                   "Tag them in the linked model, or create host spaces / areas.");
                return;
            }

            int placed = 0, failed = 0, unplaced = 0;
            string firstFailure = null;
            foreach (var (link, les) in linked)
            {
                foreach (var se in les.OfType<SpatialElement>())
                {
                    try
                    {
                        var key = TaggedHostKey.Linked(link.Instance.Id.Value, se.Id.Value);
                        if (skipIfTagged && alreadyTagged != null
                            && alreadyTagged.TryGetValue(key, out var on)
                            && TagRuleIdentity.ShouldSkip(on, isSpecialistRule, placedFamily, specialistFamilies))
                        {
                            stats.Skipped++;
                            continue;
                        }
                        var local = SpatialTagPoint(se);
                        if (local == null) { unplaced++; stats.Skipped++; continue; }

                        var tag = PlaceSpatialTag(doc, view, se, kind, new LinkElementId(link.Instance.Id, se.Id),
                            link.Transform.OfPoint(local), tagTypeId, withLeader, orientation, out string problem);
                        if (tag == null) { failed++; firstFailure ??= $"{link.Name}/{se.Id}: {problem}"; continue; }
                        placed++;
                        stats.TagsPlaced++;
                        if (alreadyTagged != null)
                        {
                            if (!alreadyTagged.TryGetValue(key, out var fams)) alreadyTagged[key] = fams = new List<string>();
                            fams.Add(placedFamily);
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        firstFailure ??= $"{link.Name}/{se.Id}: {ex.Message}";
                    }
                }
            }
            if (unplaced > 0)
                stats.Warnings.Add($"{catKey}: {unplaced} unplaced or unbounded linked room(s) have no position to tag.");
            if (failed > 0)
                stats.Warnings.Add($"{catKey}: {failed} linked room(s) could not be tagged — first: {firstFailure}");
            if (placed > 0)
                StingLog.Info($"AnnotationRunner {catKey} in '{view.Name}': {placed} linked room(s) tagged.");
        }
    }
}
