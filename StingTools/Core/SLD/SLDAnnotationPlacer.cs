using StingTools.Core;
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
//
// Phase 179 S1–S6 additions:
//  S1 - BuildCircuitLabel appends non-standard voltage value line.
//  S4 - BuildCircuitLabel appends "via <RouteRef>" line.
//  S5 - StampDiscriminationBadges() places SEL / ! badges near device labels.
//  S6 - BuildCircuitLabel appends UPS runtime line.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.Symbols;
using StingTools.Commands.Electrical.Coordination;

namespace StingTools.Core.SLD
{
    public static class SLDAnnotationPlacer
    {
        private const double MmPerFoot = 304.8;
        private const double symSizeMm = 5.0; // default circuit symbol radius in mm
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

                // symSizeMm/2 clears the symbol body; + TextHeightMm adds one line of breathing room.
                // Use SLDLayoutOptions default symbol height (8 mm) as the sizing reference.
                const double symSizeMm = 8.0;
                XYZ textPos = OffsetForRule(position, rules.LabelPosition,
                    Mm(symSizeMm / 2.0 + rules.TextHeightMm));
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

            // S1 — Non-standard voltage: append only when voltage is populated and
            // does not match the common nominal values (230 V or 400 V).
            if (node.SystemVoltageV > 0
                && Math.Abs(node.SystemVoltageV - 230.0) > 1.0
                && Math.Abs(node.SystemVoltageV - 400.0) > 1.0)
            {
                lines.Add($"{node.SystemVoltageV:F0}V");
            }

            // S4 — Cable / conduit route reference.
            if (!string.IsNullOrEmpty(node.RouteRef))
                lines.Add($"via {node.RouteRef}");

            // S6 — UPS autonomy time.
            if (node.RuntimeMin > 0)
                lines.Add($"{node.RuntimeMin:F0}min");

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

        // ── S5 — Discrimination badges ───────────────────────────────────────

        /// <summary>
        /// S5 — Stamps SEL (selective) or ! (violation) badges near each
        /// protective-device annotation on the SLD view.
        ///
        /// <para>Logic:</para>
        /// <list type="bullet">
        ///   <item>A set of "violating" device labels is built from
        ///     <paramref name="violations"/> (both upstream and downstream names).</item>
        ///   <item>Walking the node tree from <paramref name="root"/>: nodes whose
        ///     label appears in the violation set receive a "!" badge.  Nodes whose
        ///     label is NOT in the violation set AND whose parent's label is ALSO not
        ///     in the violation set receive a "SEL" badge (the pair is selectively
        ///     coordinated).</item>
        ///   <item>Badges are placed as small TextNotes offset −boxHeight × 0.6 below
        ///     the symbol centre so they appear beneath the main circuit label.</item>
        ///   <item>When <paramref name="violations"/> is empty the method is a no-op
        ///     (no badges written, no transactions started).</item>
        /// </list>
        /// </summary>
        public static void StampDiscriminationBadges(
            Document doc,
            ViewDrafting view,
            SLDNode root,
            IEnumerable<CoordViolation> violations,
            IDictionary<ElementId, ElementId> nodeToInstance)
        {
            if (doc == null || view == null || root == null) return;
            var violationList = violations?.ToList() ?? new List<CoordViolation>();
            if (violationList.Count == 0) return;   // no-op on empty list

            try
            {
                // Build set of device labels involved in a violation (upstream or downstream).
                var violatingLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var v in violationList)
                {
                    if (!string.IsNullOrEmpty(v.UpstreamDevice))
                        violatingLabels.Add(v.UpstreamDevice);
                    if (!string.IsNullOrEmpty(v.DownstreamDevice))
                        violatingLabels.Add(v.DownstreamDevice);
                }

                // Retrieve first available TextNoteType once.
                ElementId tnt = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .FirstElementId();
                if (tnt == ElementId.InvalidElementId)
                {
                    StingLog.Warn("StampDiscriminationBadges: no TextNoteType in document.");
                    return;
                }

                // Badge offset: 0.6 × symbol height (in feet; use 8 mm default).
                double badgeOffsetFt = Mm(8.0) * 0.6;

                using (var tx = new Transaction(doc, "STING SLD Discrimination Badges"))
                {
                    tx.Start();
                    StampBadgesRecursive(doc, view, root, tnt, badgeOffsetFt,
                        violatingLabels, nodeToInstance);
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StampDiscriminationBadges: {ex.Message}");
            }
        }

        private static void StampBadgesRecursive(
            Document doc, ViewDrafting view, SLDNode node,
            ElementId tnt, double badgeOffsetFt,
            HashSet<string> violatingLabels,
            IDictionary<ElementId, ElementId> nodeToInstance)
        {
            if (node == null) return;
            try
            {
                // Only badge nodes that have a placed symbol instance.
                if (nodeToInstance != null
                    && nodeToInstance.ContainsKey(node.ElementId)
                    && !string.IsNullOrEmpty(node.Label))
                {
                    // Determine badge text.
                    bool isViolation = violatingLabels.Contains(node.Label);
                    bool parentViolation = node.Parent != null
                        && !string.IsNullOrEmpty(node.Parent.Label)
                        && violatingLabels.Contains(node.Parent.Label);

                    string badge = isViolation ? "!"
                                 : (!parentViolation ? "SEL" : null);

                    if (!string.IsNullOrEmpty(badge))
                    {
                        // Get position from the placed FamilyInstance.
                        var instanceId = nodeToInstance[node.ElementId];
                        var inst = doc.GetElement(instanceId) as FamilyInstance;
                        if (inst?.Location is LocationPoint lp)
                        {
                            var badgePos = new XYZ(lp.Point.X,
                                                   lp.Point.Y - badgeOffsetFt,
                                                   lp.Point.Z);
                            try
                            {
                                TextNote.Create(doc, view.Id, badgePos, badge, tnt);
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"StampBadgesRecursive TextNote: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StampBadgesRecursive node '{node?.Label}': {ex.Message}");
            }

            foreach (var child in node.Children)
                StampBadgesRecursive(doc, view, child, tnt, badgeOffsetFt,
                    violatingLabels, nodeToInstance);
        }
    }
}
