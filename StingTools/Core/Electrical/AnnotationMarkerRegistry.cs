// StingTools — Unified annotation marker / ownership tracking.
//
// Replaces ad-hoc Comments-param usage in WireAnnotationCommands and
// provides a single, consistent API for all annotation commands to:
//   • Stamp ownership onto a newly placed annotation element.
//   • Find all annotations belonging to a specific owner element.
//   • Delete annotations by prefix or by specific owner.
//
// Marker format:  <prefix>|<ownerUniqueId>
// Written to:     BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS
//
// All read operations are safe to call outside a transaction.
// Write (Stamp) and Delete operations must be called inside an open Transaction.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Electrical
{
    /// <summary>
    /// Centralised registry for STING annotation ownership markers.
    /// Encodes ownership as a <c>prefix|uniqueId</c> string in the
    /// element's Comments parameter, matching the pattern originally used
    /// inline in <c>WireAnnotationEngine</c>.
    /// </summary>
    public static class AnnotationMarkerRegistry
    {
        // ── Prefix constants ─────────────────────────────────────────────────

        /// <summary>Prefix used by wire-spec text-note annotations.</summary>
        public const string WireAnnotationPrefix = "STING_WIRE_ANNOT";

        /// <summary>Prefix used by home-run arrow annotations.</summary>
        public const string HomeRunPrefix = "STING_WIRE_HOMERUN";

        /// <summary>Prefix used by tick-mark detail-line annotations.</summary>
        public const string TickMarkPrefix = "STING_WIRE_TICK";

        /// <summary>Prefix used by Drawing-Type-generated annotations.</summary>
        public const string DrawingTypePrefix = "STING_DT_ANNOT";

        private const char Separator = '|';

        // ── Marker construction ──────────────────────────────────────────────

        /// <summary>
        /// Builds the marker string for an annotation owned by
        /// <paramref name="ownerUniqueId"/>.  When the unique-id is null or
        /// empty, returns the bare <paramref name="prefix"/> (prefix-only
        /// marker, useful for non-element-owned annotations).
        /// </summary>
        public static string MarkerFor(string prefix, string ownerUniqueId)
        {
            if (string.IsNullOrEmpty(ownerUniqueId))
                return prefix ?? "";
            return (prefix ?? "") + Separator + ownerUniqueId;
        }

        // ── Write ────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes <paramref name="marker"/> to <paramref name="annotElement"/>'s
        /// Comments parameter.  Must be called inside an open Transaction.
        /// </summary>
        public static void Stamp(Document doc, Element annotElement, string marker)
        {
            if (doc == null || annotElement == null || marker == null) return;
            try
            {
                var p = annotElement.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (p != null && !p.IsReadOnly)
                    p.Set(marker);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"AnnotationMarkerRegistry.Stamp: {ex.Message}");
            }
        }

        // ── Read ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns <c>true</c> when <paramref name="el"/>'s Comments parameter
        /// starts with <paramref name="prefix"/>.
        /// </summary>
        public static bool HasMarker(Element el, string prefix)
        {
            string val = ReadComments(el);
            if (val == null || prefix == null) return false;
            return val.StartsWith(prefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="el"/>'s Comments parameter
        /// equals <paramref name="exactMarker"/> exactly (ordinal comparison).
        /// </summary>
        public static bool MatchesMarker(Element el, string exactMarker)
        {
            string val = ReadComments(el);
            return string.Equals(val, exactMarker, StringComparison.Ordinal);
        }

        // ── Query ────────────────────────────────────────────────────────────

        /// <summary>
        /// Finds all annotation elements in <paramref name="view"/> whose
        /// Comments parameter starts with <paramref name="prefix"/>.
        /// Searches TextNotes, IndependentTags, and detail curves (DetailLine /
        /// DetailArc / DetailNurbSpline).
        /// </summary>
        public static List<Element> FindByPrefix(Document doc, View view, string prefix)
        {
            var result = new List<Element>();
            if (doc == null || view == null || string.IsNullOrEmpty(prefix))
                return result;

            try
            {
                // TextNotes
                result.AddRange(
                    new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(TextNote))
                        .Cast<Element>()
                        .Where(e => HasMarker(e, prefix)));

                // IndependentTags
                result.AddRange(
                    new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(IndependentTag))
                        .Cast<Element>()
                        .Where(e => HasMarker(e, prefix)));

                // Detail curves (tick marks, home-run lines)
                result.AddRange(
                    new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(DetailLine))
                        .Cast<Element>()
                        .Where(e => HasMarker(e, prefix)));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"AnnotationMarkerRegistry.FindByPrefix: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Finds all annotation elements in <paramref name="view"/> that were
        /// stamped with <see cref="MarkerFor"/>(<paramref name="prefix"/>,
        /// <paramref name="ownerUniqueId"/>).
        /// </summary>
        public static List<Element> FindByOwner(Document doc, View view,
            string prefix, string ownerUniqueId)
        {
            string exact = MarkerFor(prefix, ownerUniqueId);
            return FindByPrefix(doc, view, prefix)
                .Where(e => MatchesMarker(e, exact))
                .ToList();
        }

        // ── Delete ───────────────────────────────────────────────────────────

        /// <summary>
        /// Deletes all annotation elements in <paramref name="view"/> whose
        /// Comments starts with <paramref name="prefix"/>.
        /// Caller must be inside an open Transaction.
        /// </summary>
        /// <returns>Number of elements deleted.</returns>
        public static int DeleteByPrefix(Document doc, View view, string prefix)
        {
            var targets = FindByPrefix(doc, view, prefix);
            int count   = 0;
            foreach (var el in targets)
            {
                try
                {
                    doc.Delete(el.Id);
                    count++;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"AnnotationMarkerRegistry.DeleteByPrefix: could not delete {el.Id}: {ex.Message}");
                }
            }
            return count;
        }

        /// <summary>
        /// Deletes annotation elements in <paramref name="view"/> owned by the
        /// element identified by <paramref name="ownerUniqueId"/>.
        /// Caller must be inside an open Transaction.
        /// </summary>
        /// <returns>Number of elements deleted.</returns>
        public static int DeleteByOwner(Document doc, View view,
            string prefix, string ownerUniqueId)
        {
            var targets = FindByOwner(doc, view, prefix, ownerUniqueId);
            int count   = 0;
            foreach (var el in targets)
            {
                try
                {
                    doc.Delete(el.Id);
                    count++;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"AnnotationMarkerRegistry.DeleteByOwner: could not delete {el.Id}: {ex.Message}");
                }
            }
            return count;
        }

        // ── Internal helpers ─────────────────────────────────────────────────

        private static string ReadComments(Element el)
        {
            try
            {
                return el?.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();
            }
            catch { return null; }
        }
    }
}
