// StingTools — SLD annotation placer (Phase 175)
//
// Draws busbars, branch lines, and per-symbol rating/circuit labels on
// a generated SLD drafting view, using the active standard's
// AnnotationRules. PlaceCircuitAnnotation returns the new TextNote's
// ElementId so the generator can stamp it onto the corresponding symbol
// instance via STING_SYMBOL_LABEL_ID for direct fast-path lookup later.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.Symbols;

namespace StingTools.Core.SLD
{
    public static class SLDAnnotationPlacer
    {
        private const double MmPerFoot = 304.8;
        private static double Mm(double mm) => mm / MmPerFoot;

        /// <summary>
        /// Place a label TextNote for every node in <paramref name="root"/>'s
        /// tree. When <paramref name="nodeToInstance"/> maps a node to a
        /// placed FamilyInstance, the new TextNote's ElementId is also
        /// stamped onto that instance via <c>STING_SYMBOL_LABEL_ID</c>
        /// so <c>SLDGenerator.TryUpdateSingleNode</c> can find it
        /// directly without a spatial search.
        /// </summary>
        public static void PlaceAllAnnotations(Document doc, ViewDrafting view, SLDNode root,
            SLDLayout layout, string standardId,
            IDictionary<ElementId, ElementId> nodeToInstance = null)
        {
            if (doc == null || view == null || root == null || layout == null) return;
            var rules = SymbolStandardRegistry.GetAnnotationRules(standardId);

            var stack = new Stack<SLDNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (layout.SymbolPositions.TryGetValue(node.ElementId, out var pos))
                {
                    ElementId noteId = PlaceCircuitAnnotation(doc, view, node, pos, rules);
                    if (noteId != ElementId.InvalidElementId
                        && nodeToInstance != null
                        && nodeToInstance.TryGetValue(node.ElementId, out var instanceId))
                    {
                        StampLabelId(doc, instanceId, noteId);
                    }
                }
                foreach (var c in node.Children) stack.Push(c);
            }
        }

        /// <summary>
        /// Place a single rating/circuit label and return its
        /// <see cref="ElementId"/>. Returns
        /// <see cref="ElementId.InvalidElementId"/> when no label was
        /// produced (no host data, missing TextNoteType, etc.).
        /// </summary>
        public static ElementId PlaceCircuitAnnotation(Document doc, ViewDrafting view, SLDNode node,
            XYZ position, AnnotationRules rules)
        {
            try
            {
                string label = BuildCircuitLabel(node, rules);
                if (string.IsNullOrWhiteSpace(label)) return ElementId.InvalidElementId;

                XYZ textPos = OffsetForRule(position, rules.LabelPosition,
                    Mm(rules.TextHeightMm * 1.5));
                var tnt = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .FirstElementId();
                if (tnt == ElementId.InvalidElementId) return ElementId.InvalidElementId;
                var note = TextNote.Create(doc, view.Id, textPos, label, tnt);
                return note?.Id ?? ElementId.InvalidElementId;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"PlaceCircuitAnnotation {node?.ConceptId}: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        public static void PlaceBusbarsAndBranches(Document doc, ViewDrafting view, SLDLayout layout)
        {
            if (doc == null || view == null || layout == null) return;
            try
            {
                foreach (var seg in layout.BusbarSegments)
                {
                    if (seg.from.DistanceTo(seg.to) < 1e-6) continue;
                    doc.Create.NewDetailCurve(view, Line.CreateBound(seg.from, seg.to));
                }
                foreach (var seg in layout.BranchLines)
                {
                    if (seg.from.DistanceTo(seg.to) < 1e-6) continue;
                    doc.Create.NewDetailCurve(view, Line.CreateBound(seg.from, seg.to));
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"PlaceBusbarsAndBranches: {ex.Message}");
            }
        }

        // ── helpers ─────────────────────────────────────────────────────

        private static string BuildCircuitLabel(SLDNode node, AnnotationRules rules)
        {
            if (node == null || rules == null) return "";
            string fmt = (rules.RatingFormat ?? "{rating}{unit}")
                .Replace("{poles}", node.Poles > 0 ? node.Poles.ToString() : "")
                .Replace("{rating}", node.Rating ?? "")
                .Replace("{curve}", "")
                .Replace("{unit}", "");
            string circuit = string.IsNullOrEmpty(node.CircuitRef) ? ""
                : $"{rules.CircuitRefPrefix}{node.CircuitRef}{rules.CircuitRefSuffix}";
            return string.Join("\n",
                new[] { circuit, fmt, node.Label }
                .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        }

        private static XYZ OffsetForRule(XYZ pos, string rule, double off)
        {
            switch ((rule ?? "Above").Trim())
            {
                case "Above": return new XYZ(pos.X, pos.Y + off, pos.Z);
                case "Below": return new XYZ(pos.X, pos.Y - off, pos.Z);
                case "Right": return new XYZ(pos.X + off, pos.Y, pos.Z);
                case "Left":  return new XYZ(pos.X - off, pos.Y, pos.Z);
                default:      return pos.Add(new XYZ(0, off, 0));
            }
        }

        private static void StampLabelId(Document doc, ElementId instanceId, ElementId labelId)
        {
            try
            {
                var inst = doc.GetElement(instanceId) as FamilyInstance;
                if (inst == null) return;
                var p = inst.LookupParameter("STING_SYMBOL_LABEL_ID");
                if (p != null && !p.IsReadOnly) p.Set(labelId.IntegerValue.ToString());
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"SLDAnnotationPlacer.StampLabelId: {ex.Message}");
            }
        }
    }
}
