// StingTools — SLD annotation placer (Phase 175)
//
// Draws busbars, branch lines, and per-symbol rating/circuit labels on
// a generated SLD drafting view, using the active standard's
// AnnotationRules. PlaceCircuitAnnotation returns the new TextNote's
// ElementId so the generator can stamp it onto the corresponding symbol
// instance via STING_SYMBOL_LABEL_ID for direct fast-path lookup later.
//
// SLD-07: busbar segments use the standard's LineWeightSymbol line style;
//         branch lines use LineWeightConnection.
// SLD-08: {curve} token filled from ELC_CKT_PROTECTION_CURVE_TXT or
//         inferred from family name; {unit} derived from Rating content.
// SLD-09: pole tick marks drawn across branch drop lines.
// SLD-10: TextNoteType resolved by name ("STING SLD") or closest height.

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
        /// stamped onto that instance via <c>STING_SYMBOL_LABEL_ID</c>.
        /// </summary>
        public static void PlaceAllAnnotations(Document doc, ViewDrafting view, SLDNode root,
            SLDLayout layout, string standardId,
            IDictionary<ElementId, ElementId> nodeToInstance = null)
        {
            if (doc == null || view == null || root == null || layout == null) return;
            var rules = SymbolStandardRegistry.GetAnnotationRules(standardId);
            // SLD-10: resolve once
            ElementId tntId = ResolveTextNoteType(doc, rules.TextHeightMm);

            var stack = new Stack<SLDNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (layout.SymbolPositions.TryGetValue(node.ElementId, out var pos))
                {
                    ElementId noteId = PlaceCircuitAnnotation(doc, view, node, pos, rules, tntId);
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
        /// Place a single rating/circuit label and return its ElementId.
        /// Returns <see cref="ElementId.InvalidElementId"/> when no label
        /// was produced.
        /// </summary>
        public static ElementId PlaceCircuitAnnotation(Document doc, ViewDrafting view, SLDNode node,
            XYZ position, AnnotationRules rules, ElementId textNoteTypeId = default)
        {
            try
            {
                string label = BuildCircuitLabel(node, rules);
                if (string.IsNullOrWhiteSpace(label)) return ElementId.InvalidElementId;

                XYZ textPos = OffsetForRule(position, rules.LabelPosition,
                    Mm(rules.TextHeightMm * 1.5));

                // SLD-10: use provided / resolved type, or fall back to first available
                ElementId tntId = textNoteTypeId == default || textNoteTypeId == ElementId.InvalidElementId
                    ? ResolveTextNoteType(doc, rules.TextHeightMm)
                    : textNoteTypeId;
                if (tntId == ElementId.InvalidElementId) return ElementId.InvalidElementId;

                var note = TextNote.Create(doc, view.Id, textPos, label, tntId);
                return note?.Id ?? ElementId.InvalidElementId;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"PlaceCircuitAnnotation {node?.ConceptId}: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        /// <summary>
        /// Draw busbar and branch-drop lines. Uses the standard's line
        /// weights and draws pole tick marks on each branch. (SLD-07, SLD-09)
        /// </summary>
        public static void PlaceBusbarsAndBranches(Document doc, ViewDrafting view,
            SLDLayout layout, string standardId = null)
        {
            if (doc == null || view == null || layout == null) return;
            try
            {
                // SLD-07: look up line styles that match the standard's weights
                var std = SymbolStandardRegistry.GetStandard(standardId ?? "IEC");
                int busWeight    = std?.LineWeightSymbol     > 0 ? std.LineWeightSymbol     : 3;
                int branchWeight = std?.LineWeightConnection > 0 ? std.LineWeightConnection : 1;

                GraphicsStyle busStyle    = FindLineStyleByWeight(doc, busWeight);
                GraphicsStyle branchStyle = FindLineStyleByWeight(doc, branchWeight);

                foreach (var seg in layout.BusbarSegments)
                {
                    if (seg.from.DistanceTo(seg.to) < 1e-6) continue;
                    var curve = doc.Create.NewDetailCurve(view, Line.CreateBound(seg.from, seg.to));
                    if (curve != null && busStyle != null)
                        try { curve.LineStyle = busStyle; } catch { }
                }

                // SLD-09: draw branch lines with pole tick marks
                if (layout.BranchLinesWithPoles.Count > 0)
                {
                    foreach (var seg in layout.BranchLinesWithPoles)
                    {
                        if (seg.from.DistanceTo(seg.to) < 1e-6) continue;
                        var curve = doc.Create.NewDetailCurve(view,
                            Line.CreateBound(seg.from, seg.to));
                        if (curve != null && branchStyle != null)
                            try { curve.LineStyle = branchStyle; } catch { }
                        DrawPoleTickMarks(doc, view, seg.from, seg.to, seg.poles);
                    }
                }
                else
                {
                    // Fallback: use legacy BranchLines (no poles available)
                    foreach (var seg in layout.BranchLines)
                    {
                        if (seg.from.DistanceTo(seg.to) < 1e-6) continue;
                        var curve = doc.Create.NewDetailCurve(view,
                            Line.CreateBound(seg.from, seg.to));
                        if (curve != null && branchStyle != null)
                            try { curve.LineStyle = branchStyle; } catch { }
                    }
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

            // SLD-08: derive {curve} and {unit} from the element / circuit data
            string curve = DeriveProtectionCurve(node);
            string unit  = DeriveRatingUnit(node);

            string fmt = (rules.RatingFormat ?? "{rating}{unit}")
                .Replace("{poles}",  node.Poles > 0 ? node.Poles.ToString() : "")
                .Replace("{rating}", node.Rating ?? "")
                .Replace("{curve}",  curve)
                .Replace("{unit}",   unit);

            string circuit = string.IsNullOrEmpty(node.CircuitRef) ? ""
                : $"{rules.CircuitRefPrefix}{node.CircuitRef}{rules.CircuitRefSuffix}";

            return string.Join("\n",
                new[] { circuit, fmt, node.Label }
                .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        }

        // SLD-08: MCB/RCBO trip characteristic — read from STING param or infer from family name
        private static string DeriveProtectionCurve(SLDNode node)
        {
            if (node.RevitElement == null) return "";
            try
            {
                string v = ParameterHelpers.GetString(node.RevitElement, "ELC_CKT_PROTECTION_CURVE_TXT");
                if (!string.IsNullOrEmpty(v)) return v;
                string famUpper = (node.RevitElement.Symbol?.FamilyName ?? "").ToUpperInvariant();
                if (famUpper.Contains("TYPE B") || famUpper.EndsWith("-B") || famUpper.Contains(" B-")) return "B";
                if (famUpper.Contains("TYPE C") || famUpper.EndsWith("-C") || famUpper.Contains(" C-")) return "C";
                if (famUpper.Contains("TYPE D") || famUpper.EndsWith("-D") || famUpper.Contains(" D-")) return "D";
            }
            catch { }
            return "";
        }

        // SLD-08: unit string for the rating format
        private static string DeriveRatingUnit(SLDNode node)
        {
            if (string.IsNullOrEmpty(node.Rating)) return "A"; // default for protection devices
            string r = node.Rating.ToUpperInvariant().Trim();
            if (r.EndsWith("A")) return ""; // unit already included in Rating value
            if (r.Contains("KVA")) return "kVA";
            if (r.Contains("KW"))  return "kW";
            // Numeric-only rating — add Ampere unit
            if (double.TryParse(r, out _)) return "A";
            return "";
        }

        // SLD-09: draw short transverse tick marks along a branch drop line
        private static void DrawPoleTickMarks(Document doc, ViewDrafting view,
            XYZ from, XYZ to, int poles)
        {
            if (poles <= 1) return;
            try
            {
                double lineLen = from.DistanceTo(to);
                if (lineLen < 1e-6) return;
                double tickLen = Mm(2.5);
                XYZ dir  = (to - from).Normalize();
                XYZ perp = new XYZ(-dir.Y, dir.X, 0);
                for (int p = 0; p < poles; p++)
                {
                    double t = lineLen * (p + 1.0) / (poles + 1.0);
                    XYZ mid       = from + dir.Multiply(t);
                    XYZ tickFrom  = mid - perp.Multiply(tickLen * 0.5);
                    XYZ tickTo    = mid + perp.Multiply(tickLen * 0.5);
                    if (tickFrom.DistanceTo(tickTo) > 1e-6)
                        doc.Create.NewDetailCurve(view, Line.CreateBound(tickFrom, tickTo));
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"DrawPoleTickMarks: {ex.Message}");
            }
        }

        // SLD-10: prefer a named "STING SLD" TextNoteType, then closest to target height
        private static ElementId ResolveTextNoteType(Document doc, double targetHeightMm)
        {
            try
            {
                var all = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>()
                    .ToList();
                if (all.Count == 0) return ElementId.InvalidElementId;

                // 1. Named STING SLD type
                var byName = all.FirstOrDefault(t =>
                    t.Name.StartsWith("STING SLD", StringComparison.OrdinalIgnoreCase));
                if (byName != null) return byName.Id;

                // 2. Closest text size to the standard's TextHeightMm
                double targetFt = targetHeightMm / MmPerFoot;
                var closest = all
                    .OrderBy(t =>
                    {
                        try
                        {
                            var p = t.get_Parameter(BuiltInParameter.TEXT_SIZE);
                            if (p != null) return Math.Abs(p.AsDouble() - targetFt);
                        }
                        catch { }
                        return double.MaxValue;
                    })
                    .FirstOrDefault();
                return closest?.Id ?? ElementId.InvalidElementId;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"ResolveTextNoteType: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        // SLD-07: find a line style (DetailCurve GraphicsStyle) by projection line weight
        private static GraphicsStyle FindLineStyleByWeight(Document doc, int weight)
        {
            try
            {
                var linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                if (linesCat?.SubCategories == null) return null;
                foreach (Category sub in linesCat.SubCategories)
                {
                    try
                    {
                        if (sub.GetLineWeight(GraphicsStyleType.Projection) == weight)
                        {
                            var gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
                            if (gs != null) return gs;
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"FindLineStyleByWeight: {ex.Message}");
            }
            return null;
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
                if (p != null && !p.IsReadOnly) p.Set(labelId.Value.ToString());
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"SLDAnnotationPlacer.StampLabelId: {ex.Message}");
            }
        }
    }
}
