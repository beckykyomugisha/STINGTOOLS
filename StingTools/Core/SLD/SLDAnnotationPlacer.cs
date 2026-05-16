// StingTools — SLD annotation placer (Phase 175 + Phase 179 enhancements)
//
// Draws busbars, branch lines, and per-symbol labels on a generated SLD
// drafting view. PlaceCircuitAnnotation returns the new TextNote's ElementId
// so the generator can stamp it via STING_SYMBOL_LABEL_ID for fast-path
// lookup during incremental updates.
//
// Phase 179: accepts SLDAnnotationOptions so UI checkboxes (ShowVdPct,
// ShowFaultKa, ShowCsaMm2, ShowLoads) drive label content; BuildCircuitLabel
// now emits all populated BS 7671 / IEC 60364 fields.

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
        /// Place a label TextNote for every node in the tree.
        /// When <paramref name="nodeToInstance"/> maps a node to its placed
        /// FamilyInstance, the TextNote's ElementId is also stamped onto that
        /// instance via <c>STING_SYMBOL_LABEL_ID</c> for O(1) fast-path refresh.
        /// </summary>
        public static void PlaceAllAnnotations(Document doc, ViewDrafting view, SLDNode root,
            SLDLayout layout, string standardId,
            IDictionary<ElementId, ElementId> nodeToInstance = null,
            SLDAnnotationOptions annotOpts = null)
        {
            if (doc == null || view == null || root == null || layout == null) return;
            var rules = SymbolStandardRegistry.GetAnnotationRules(standardId);
            annotOpts = annotOpts ?? SLDAnnotationOptions.Default;

            var stack = new Stack<SLDNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (layout.SymbolPositions.TryGetValue(node.ElementId, out var pos))
                {
                    ElementId noteId = PlaceCircuitAnnotation(doc, view, node, pos, rules, annotOpts);
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
        /// Place a single circuit label TextNote and return its ElementId.
        /// Returns <see cref="ElementId.InvalidElementId"/> when no label is produced.
        /// </summary>
        public static ElementId PlaceCircuitAnnotation(Document doc, ViewDrafting view, SLDNode node,
            XYZ position, AnnotationRules rules, SLDAnnotationOptions annotOpts = null)
        {
            annotOpts = annotOpts ?? SLDAnnotationOptions.Default;
            try
            {
                string label = BuildCircuitLabel(node, rules, annotOpts);
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
                StingLog.Warn($"PlaceCircuitAnnotation {node?.ConceptId}: {ex.Message}");
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
                StingLog.Warn($"PlaceBusbarsAndBranches: {ex.Message}");
            }
        }

        // ── Label builder ────────────────────────────────────────────────────

        /// <summary>
        /// Builds the annotation label for a circuit node.
        /// Per-standard defaults from <paramref name="rules"/> are merged with UI
        /// overrides from <paramref name="opts"/>: if the standard mandates a field
        /// (e.g. IEC 60364 fault level) it shows even when the UI checkbox is off,
        /// unless the user explicitly has it as the only controlling flag.
        /// Merge rule: show = rules flag OR opts flag (either can enable; there is
        /// no explicit suppress — hide comes from both being false).
        /// </summary>
        private static string BuildCircuitLabel(SLDNode node, AnnotationRules rules,
            SLDAnnotationOptions opts)
        {
            if (node == null || rules == null) return "";

            var lines = new List<string>();

            // Merge per-standard defaults with live UI options (OR semantics).
            bool showLoad    = opts.ShowLoads    || rules.ShowLoad;
            bool showCsa     = opts.ShowCsaMm2   || rules.ShowCsaMm2;
            bool showVd      = opts.ShowVdPct    || rules.ShowVdPct;
            bool showFault   = opts.ShowFaultKa  || rules.ShowFaultKa;

            // Circuit reference.
            string circuit = string.IsNullOrEmpty(node.CircuitRef) ? ""
                : $"{rules.CircuitRefPrefix}{node.CircuitRef}{rules.CircuitRefSuffix}";
            if (!string.IsNullOrEmpty(circuit)) lines.Add(circuit);

            // Rating / poles — controlled by opts.ShowRatings (no per-standard override;
            // ratings are always meaningful so the UI flag is the sole gate).
            if (opts.ShowRatings && !(string.IsNullOrEmpty(node.Rating) && node.Poles == 0))
            {
                string fmt = (rules.RatingFormat ?? "{rating}{unit}")
                    .Replace("{poles}", node.Poles > 0 ? $"{node.Poles}P " : "")
                    .Replace("{rating}", node.Rating ?? "")
                    .Replace("{curve}", "")
                    .Replace("{unit}", "");
                if (!string.IsNullOrWhiteSpace(fmt)) lines.Add(fmt.Trim());
            }

            // Load (apparent power kVA) — standard default OR UI checkbox.
            // RBS_ELEC_APPARENT_LOAD stores VA; LoadKW holds VA/1000 = kVA.
            if (showLoad && node.LoadKW > 0)
            {
                string loadLine = (rules.LoadFormat ?? "{load}kVA")
                    .Replace("{load}", node.LoadKW.ToString("F1",
                        System.Globalization.CultureInfo.InvariantCulture));
                lines.Add(loadLine);
            }

            // Cable CSA — standard default OR UI checkbox.
            if (showCsa && !string.IsNullOrEmpty(node.CsaMm2))
            {
                string csaLine = (rules.CsaFormat ?? "{csa}mm²")
                    .Replace("{csa}", node.CsaMm2);
                lines.Add(csaLine);
            }

            // Voltage drop % — standard default OR UI checkbox.
            if (showVd && node.VdPct > 0)
            {
                string vdLine = (rules.VdFormat ?? "VD {vd}%")
                    .Replace("{vd}", node.VdPct.ToString("F1",
                        System.Globalization.CultureInfo.InvariantCulture));
                lines.Add(vdLine);
            }

            // Fault level — standard default OR UI checkbox.
            if (showFault && !string.IsNullOrEmpty(node.FaultKa))
            {
                string faultLine = (rules.FaultFormat ?? "Iₖ {fault}kA")
                    .Replace("{fault}", node.FaultKa);
                lines.Add(faultLine);
            }

            // Element name label.
            if (!string.IsNullOrEmpty(node.Label)) lines.Add(node.Label);

            return string.Join("\n", lines.Where(l => !string.IsNullOrWhiteSpace(l))).Trim();
        }

        // ── Geometry helpers ─────────────────────────────────────────────────

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
                StingLog.Warn($"SLDAnnotationPlacer.StampLabelId: {ex.Message}");
            }
        }
    }
}
