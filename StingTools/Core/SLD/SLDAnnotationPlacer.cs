// StingTools — SLD annotation placer (Phase 175)
//
// Draws busbars, branch lines, and per-symbol rating/circuit labels on
// a generated SLD drafting view, using the active standard's
// AnnotationRules.

using System;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.Symbols;

namespace StingTools.Core.SLD
{
    public static class SLDAnnotationPlacer
    {
        private const double MmPerFoot = 304.8;
        private static double Mm(double mm) => mm / MmPerFoot;

        public static void PlaceAllAnnotations(Document doc, ViewDrafting view, SLDNode root,
            SLDLayout layout, string standardId)
        {
            if (doc == null || view == null || root == null || layout == null) return;
            var rules = SymbolStandardRegistry.GetAnnotationRules(standardId);

            // BFS-style traversal over the tree.
            var stack = new System.Collections.Generic.Stack<SLDNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (layout.SymbolPositions.TryGetValue(node.ElementId, out var pos))
                    PlaceCircuitAnnotation(doc, view, node, pos, rules);
                foreach (var c in node.Children) stack.Push(c);
            }
        }

        public static void PlaceCircuitAnnotation(Document doc, ViewDrafting view, SLDNode node,
            XYZ position, AnnotationRules rules)
        {
            try
            {
                string label = BuildCircuitLabel(node, rules);
                if (string.IsNullOrWhiteSpace(label)) return;

                XYZ textPos = OffsetForRule(position, rules.LabelPosition,
                    Mm(rules.TextHeightMm * 1.5));
                var tnt = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .FirstElementId();
                if (tnt == ElementId.InvalidElementId) return;
                TextNote.Create(doc, view.Id, textPos, label, tnt);
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"PlaceCircuitAnnotation {node.ConceptId}: {ex.Message}");
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
    }
}
