// StingTools — Wire ELEMENT annotation commands (Phase 1: compute-on-place).
//
// Places the same BS 7671-style cable-spec annotation (conductor tick marks +
// spec label) that WireAnnotationCommands.cs places on modelled *conduit*, but
// directly on Revit *Wire* elements (Autodesk.Revit.DB.Electrical.Wire,
// category OST_Wire) — the 2-D lines drawn between devices on schematic plans
// where no conduit is modelled.
//
// This file is ADDITIVE and leaves the conduit annotation path untouched. It
// reuses the conduit engine's public surface (label builder, colour/style
// resolution, slash-rotation math) and the shared AnnotationMarkerRegistry so
// wire-owned annotations carry the SAME ownership markers as conduit ones and
// are removed by the existing "Wire clear" command for free.
//
// Compute-on-place (Phase 1): nothing is stamped onto the wire. The label
// reflects circuit values resolved at the moment of placement. The ELC_WIRE_*
// shared parameters bind to the Conduits category only, so — per the scope
// document — no ELC_WIRE_* parameters are written to wires. Persistence /
// refresh (Phase 2) is deferred pending the OST_Wire binding question.
//
// Data sources on a Wire (verified against the Revit 2025 API):
//   • Circuit        : wire.MEPSystem as ElectricalSystem  (Wire : MEPCurve)
//   • Poles / phase  : ElectricalSystem.PolesNumber  (fallback RBS_ELEC_NUMBER_OF_POLES)
//   • Apparent load  : ElectricalSystem.ApparentCurrent (A)
//   • Panel          : ElectricalSystem.PanelName  (fallback BaseEquipment.Name)
//   • Run length     : ElectricalSystem.Length (ft) — the CIRCUIT length, NOT
//                      the wire's schematic drawn length. Used for voltage drop.
//   • Geometry       : Wire.NumberOfVertices + Wire.GetVertex(i) — the drawn
//                      polyline; ticks + label go on the LONGEST segment.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using StingTools.Core;
using StingTools.Core.Electrical;
using StingTools.Commands.Electrical.CableSizer;
using StingTools.UI;

namespace StingTools.Commands.Electrical
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Engine — wire-specific circuit read, geometry, placement
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Compute-on-place engine for annotating Revit <see cref="Wire"/> elements.
    /// All visual geometry (slash marks + label) is placed by this class using
    /// the conduit engine's public helpers (<see cref="WireAnnotationEngine"/>)
    /// for colour, style, rotation, and label text; only the wire-specific
    /// circuit read, longest-segment geometry, and compute-on-place sizing live
    /// here. Ownership markers reuse <see cref="AnnotationMarkerRegistry"/> so the
    /// existing "Wire clear" command removes wire-owned annotations unchanged.
    /// </summary>
    internal static class WireElementAnnotationEngine
    {
        /// <summary>Result of reading a wire's circuit and sizing on-the-fly.</summary>
        internal sealed class WireCompute
        {
            public WireAnnotationData Data;
            public bool HasCircuit;
            public bool HasLength;   // circuit length available → VD computed
            public bool HasCurrent;  // apparent current available → CSA computed
        }

        // ── Circuit resolution ───────────────────────────────────────────────

        internal static ElectricalSystem ResolveCircuit(Wire wire)
        {
            if (wire == null) return null;
            try
            {
                if (wire.MEPSystem is ElectricalSystem es) return es;
            }
            catch (Exception ex) { StingLog.Warn($"ResolveCircuit MEPSystem: {ex.Message}"); }
            try
            {
                var systems = wire.GetMEPSystems();
                if (systems != null)
                    foreach (var s in systems)
                    {
                        var el = wire.Document?.GetElement(s) as ElectricalSystem;
                        if (el != null) return el;
                    }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveCircuit GetMEPSystems: {ex.Message}"); }
            return null;
        }

        // ── Circuit read + compute-on-place sizing ───────────────────────────

        /// <summary>
        /// Reads the wire's circuit and computes conductor count, CSA, and
        /// voltage drop. Sizing + VD are delegated to the existing engines
        /// (<see cref="CableSizerEngine"/> / <see cref="VoltageDrop.VoltageDropEngine"/>).
        /// The run length fed to VD is the CIRCUIT length, never the wire's
        /// schematic drawn length. When the circuit length is unavailable, VD is
        /// omitted (never guessed); when the apparent current is unavailable, the
        /// CSA is left unsized (never guessed).
        /// </summary>
        internal static WireCompute Compute(Wire wire)
        {
            var result = new WireCompute();
            var circuit = ResolveCircuit(wire);
            if (circuit == null)
            {
                result.HasCircuit = false;
                return result;
            }
            result.HasCircuit = true;

            // Poles → phase → conductor (tick) count. 1Ø → L+N (2), 3Ø → 3L+N (4).
            int poles = 0;
            try { poles = circuit.PolesNumber; } catch (Exception ex) { StingLog.Warn($"PolesNumber: {ex.Message}"); }
            if (poles <= 0)
            {
                try
                {
                    var pp = circuit.get_Parameter(BuiltInParameter.RBS_ELEC_NUMBER_OF_POLES);
                    if (pp != null && pp.StorageType == StorageType.Integer) poles = pp.AsInteger();
                }
                catch (Exception ex) { StingLog.Warn($"Poles param: {ex.Message}"); }
            }
            int phases = poles >= 3 ? 3 : 1;
            int cores  = phases == 3 ? 4 : 2;

            double currentA = 0;
            try { currentA = circuit.ApparentCurrent; } catch (Exception ex) { StingLog.Warn($"ApparentCurrent: {ex.Message}"); }
            result.HasCurrent = currentA > 0;

            double voltV = 0;
            try { voltV = circuit.Voltage; } catch (Exception ex) { StingLog.Warn($"Voltage: {ex.Message}"); }
            if (voltV < 50 || voltV > 1000) voltV = phases == 3 ? 400.0 : 230.0;

            double lengthM = 0;
            try { lengthM = circuit.Length * 0.3048; } catch (Exception ex) { StingLog.Warn($"CircuitLength: {ex.Message}"); }
            result.HasLength = lengthM > 0.01;

            string panel = "";
            try { panel = circuit.PanelName ?? ""; } catch { }
            if (string.IsNullOrEmpty(panel))
            {
                try { panel = circuit.BaseEquipment?.Name ?? ""; } catch { }
            }

            string circNum = "";
            try { circNum = circuit.CircuitNumber ?? ""; } catch { }

            // Compute-on-place sizing via the existing cable sizer. LoadKW is
            // back-derived from the circuit's apparent current at PF = 1 so the
            // sizer re-derives the SAME design current (we do not re-model load).
            double csa = 0, vd = 0;
            if (result.HasCurrent)
            {
                double loadKW = phases == 3
                    ? currentA * Math.Sqrt(3.0) * voltV / 1000.0
                    : currentA * voltV / 1000.0;

                var input = new CableSizeInput
                {
                    LoadKW        = loadKW,
                    VoltageV      = voltV,
                    PowerFactor   = 1.0,
                    LengthM       = result.HasLength ? lengthM : 0.0,
                    Material      = "Cu",
                    Insulation    = "PVC70",
                    Standard      = "BS7671",
                    Phases        = phases,
                    VDLimitPct    = 3.0,
                    AmbientTempC  = 30.0,
                    InstallMethod = "C",
                };
                try
                {
                    var res = CableSizerEngine.Calculate(input);
                    csa = res.RecommendedCsaMm2;
                    // Only claim a voltage drop when we actually had a circuit
                    // length to compute it from (otherwise CSA is ampacity-only).
                    vd  = result.HasLength ? res.ActualVoltDropPct : 0.0;
                }
                catch (Exception ex) { StingLog.Warn($"WireElement sizing: {ex.Message}"); }
            }

            result.Data = new WireAnnotationData(
                Phase:         phases == 3 ? "3Ø" : "1Ø",
                CoreCount:     cores,
                CsaMm2:        csa,
                ConductorMat:  "Cu",
                CircuitNumber: circNum,
                PanelName:     panel,
                VoltDropPct:   vd,
                DiameterMm:    0.0,   // N/A — a wire has no containment diameter
                FillPct:       0.0,   // N/A — a wire has no fill
                AmpacityA:     0.0,
                MaxDemandA:    currentA,
                CircuitType:   "",
                InstallMethod: "",
                IsArmoured:    false,
                IsFireRated:   false,
                IsShielded:    false,
                BendCount:     0);
            return result;
        }

        // ── Geometry: longest drawn segment of the wire polyline ─────────────

        /// <summary>
        /// Returns the two endpoints of the wire's longest straight segment
        /// (vertex-to-vertex). Handles multi-segment wires; for arc/chamfer wire
        /// types the chord between the two bounding vertices of the longest span
        /// is used (ticks are short marks, so the chord midpoint is an accurate
        /// placement anchor). Returns (null, null) when geometry is unavailable.
        /// </summary>
        internal static (XYZ a, XYZ b) LongestSegment(Wire wire)
        {
            if (wire == null) return (null, null);
            try
            {
                int n = wire.NumberOfVertices;
                if (n >= 2)
                {
                    XYZ bestA = null, bestB = null;
                    double bestLen = -1;
                    for (int i = 0; i < n - 1; i++)
                    {
                        var v0 = wire.GetVertex(i);
                        var v1 = wire.GetVertex(i + 1);
                        if (v0 == null || v1 == null) continue;
                        double len = v0.DistanceTo(v1);
                        if (len > bestLen) { bestLen = len; bestA = v0; bestB = v1; }
                    }
                    if (bestA != null && bestB != null) return (bestA, bestB);
                }
            }
            catch (Exception ex) { StingLog.Warn($"LongestSegment vertices: {ex.Message}"); }

            // Fallback: LocationCurve endpoints (Wire : MEPCurve).
            try
            {
                if (wire.Location is LocationCurve lc && lc.Curve != null)
                    return (lc.Curve.GetEndPoint(0), lc.Curve.GetEndPoint(1));
            }
            catch (Exception ex) { StingLog.Warn($"LongestSegment location: {ex.Message}"); }
            return (null, null);
        }

        // ── Collision avoidance (local mirror) ───────────────────────────────
        // The conduit engine's collision registry (FindNonCollidingPoint /
        // RegisterAnnotationBox) is private, so a minimal per-(doc,view) mirror
        // lives here to stagger labels during batch runs. Keyed on the same
        // (docKey, viewId) shape.

        private static readonly Dictionary<(string, long), List<(XYZ min, XYZ max)>> _boxes
            = new Dictionary<(string, long), List<(XYZ, XYZ)>>();

        private static string DocKey(Document doc) => $"{doc?.Title}|{doc?.PathName}";

        internal static void ClearBoxes(Document doc, ElementId viewId)
            => _boxes.Remove((DocKey(doc), viewId.Value));

        private static XYZ FindNonColliding(Document doc, View view, XYZ preferred, XYZ perpDir, double offsetFt)
        {
            var key = (DocKey(doc), view.Id.Value);
            if (!_boxes.TryGetValue(key, out var boxes) || boxes.Count == 0) return preferred;
            double[] multipliers = { 1, -1, 2, -2, 3, -3, 4, -4 };
            foreach (var m in multipliers)
            {
                var candidate = preferred + perpDir * (offsetFt * m);
                double r = offsetFt * 0.4;
                bool collides = boxes.Any(b =>
                    candidate.X + r > b.min.X && candidate.X - r < b.max.X &&
                    candidate.Y + r > b.min.Y && candidate.Y - r < b.max.Y);
                if (!collides) return candidate;
            }
            return preferred;
        }

        private static void RegisterBox(Document doc, ElementId viewId, XYZ pt, double halfSideFt)
        {
            var key = (DocKey(doc), viewId.Value);
            if (!_boxes.TryGetValue(key, out var boxes))
                _boxes[key] = boxes = new List<(XYZ, XYZ)>();
            boxes.Add((new XYZ(pt.X - halfSideFt, pt.Y - halfSideFt, pt.Z),
                       new XYZ(pt.X + halfSideFt, pt.Y + halfSideFt, pt.Z)));
        }

        // ── Ownership queries ────────────────────────────────────────────────

        internal static bool HasAnnotation(Document doc, View view, Wire wire)
            => AnnotationMarkerRegistry
                .FindByOwner(doc, view, AnnotationMarkerRegistry.WireAnnotationPrefix, wire.UniqueId)
                .Count > 0;

        internal static void RemoveAnnotation(Document doc, View view, Wire wire)
        {
            // Both the label (WireAnnotationPrefix) and the ticks/leader
            // (TickMarkPrefix) are keyed on the wire's UniqueId.
            AnnotationMarkerRegistry.DeleteByOwner(
                doc, view, AnnotationMarkerRegistry.WireAnnotationPrefix, wire.UniqueId);
            AnnotationMarkerRegistry.DeleteByOwner(
                doc, view, AnnotationMarkerRegistry.TickMarkPrefix, wire.UniqueId);
        }

        // ── Placement ────────────────────────────────────────────────────────

        /// <summary>
        /// Places the conductor tick marks + spec label for one wire in the
        /// active view. Must be called inside an open Transaction. Returns the
        /// label element id, or InvalidElementId on failure.
        /// </summary>
        internal static ElementId Place(Document doc, View view, Wire wire,
            WireAnnotationData data, WireAnnotationStyle style)
        {
            var (a, b) = LongestSegment(wire);
            if (a == null || b == null) return ElementId.InvalidElementId;

            // Auto-scale from view scale when no per-element override is set,
            // matching the conduit path's behaviour.
            var effectiveStyle = style;
            if (Math.Abs(style.ScaleFactor - 1.0) < 1e-6)
            {
                double vsf = WireAnnotationEngine.ViewScaleFactor(view);
                if (Math.Abs(vsf - 1.0) > 0.05)
                {
                    effectiveStyle = Clone(style);
                    effectiveStyle.ScaleFactor = vsf;
                }
            }
            style = effectiveStyle;

            var axisRaw = b - a;
            if (axisRaw.GetLength() < 1e-6) return ElementId.InvalidElementId;
            var axisDir    = axisRaw.Normalize();
            var mid        = (a + b) * 0.5;
            var viewNormal = view.ViewDirection ?? XYZ.BasisZ;
            var perpRaw    = viewNormal.CrossProduct(axisDir);
            var perpDir    = perpRaw.GetLength() < 1e-6 ? XYZ.BasisX : perpRaw.Normalize();

            PlaceTicks(doc, view, a, b, data.CoreCount, style, data, wire.UniqueId);

            double offsetFt = (style.LabelOffsetMm * style.ScaleFactor) / WireAnnotationEngine.MmPerFt;
            var preferred = mid + perpDir * offsetFt;
            var labelPt   = FindNonColliding(doc, view, preferred, perpDir, offsetFt);
            RegisterBox(doc, view.Id, labelPt, offsetFt * 0.5);

            // suppressContainmentFields:true drops the conduit-only Ø + fill%.
            string text = WireAnnotationEngine.BuildAnnotationText(data, style, suppressContainmentFields: true);

            TextNote note;
            try
            {
                var tnt = ResolveTextNoteType(doc);
                note = TextNote.Create(doc, view.Id, labelPt, text, tnt?.Id ?? ElementId.InvalidElementId);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"WireElement label TextNote.Create: {ex.Message}");
                return ElementId.InvalidElementId;
            }
            AnnotationMarkerRegistry.Stamp(doc, note,
                AnnotationMarkerRegistry.MarkerFor(AnnotationMarkerRegistry.WireAnnotationPrefix, wire.UniqueId));

            // Leader from segment midpoint toward the label. Stamped with the
            // tick prefix so "Wire clear" removes it with the tick marks.
            try
            {
                double leadFt = 50.0 / WireAnnotationEngine.MmPerFt;
                var leaderEnd = labelPt - perpDir * leadFt;
                var dc = doc.Create.NewDetailCurve(view, Line.CreateBound(mid, leaderEnd));
                AnnotationMarkerRegistry.Stamp(doc, dc,
                    AnnotationMarkerRegistry.MarkerFor(AnnotationMarkerRegistry.TickMarkPrefix, wire.UniqueId));
            }
            catch (Exception ex) { StingLog.Warn($"WireElement leader: {ex.Message}"); }

            return note.Id;
        }

        private static void PlaceTicks(Document doc, View view, XYZ segA, XYZ segB,
            int coreCount, WireAnnotationStyle style, WireAnnotationData data, string ownerUid)
        {
            int slashCount = Math.Max(0, Math.Min(4, coreCount));
            if (slashCount == 0) return;

            double scale = Math.Max(0.01, style.ScaleFactor);
            double lenFt = (style.SlashLengthMm  * scale) / WireAnnotationEngine.MmPerFt;
            double gapFt = (style.SlashSpacingMm * scale) / WireAnnotationEngine.MmPerFt;

            var axisRaw = segB - segA;
            double runLen = axisRaw.GetLength();
            if (runLen < 1e-6) return;
            var axisDir = axisRaw.Normalize();

            var viewNormal = view.ViewDirection ?? XYZ.BasisZ;
            var perpRaw = viewNormal.CrossProduct(axisDir);
            var perpDir = perpRaw.GetLength() < 1e-6 ? XYZ.BasisX : perpRaw.Normalize();

            double clusterLen = gapFt * (slashCount - 1);
            double startPos   = (runLen - clusterLen) / 2.0;
            Color color = WireAnnotationEngine.ResolveColor(style, data);

            for (int i = 0; i < slashCount; i++)
            {
                double pos = startPos + gapFt * i;
                var tickMid = segA + axisDir * pos;
                var rotPerp = WireAnnotationEngine.RotateInPlane(perpDir, viewNormal, 90.0 - style.SlashAngleDeg);
                var s = tickMid - rotPerp * (lenFt * 0.5);
                var e = tickMid + rotPerp * (lenFt * 0.5);
                try
                {
                    var dc = doc.Create.NewDetailCurve(view, Line.CreateBound(s, e));
                    if (dc == null) continue;
                    AnnotationMarkerRegistry.Stamp(doc, dc,
                        AnnotationMarkerRegistry.MarkerFor(AnnotationMarkerRegistry.TickMarkPrefix, ownerUid));
                    var ogs = new OverrideGraphicSettings();
                    ogs.SetProjectionLineColor(color);
                    ogs.SetProjectionLineWeight(Math.Max(1, Math.Min(16, style.SlashLineWeight)));
                    view.SetElementOverrides(dc.Id, ogs);
                }
                catch (Exception ex) { StingLog.Warn($"WireElement tick {i}: {ex.Message}"); }
            }
        }

        private static TextNoteType ResolveTextNoteType(Document doc)
        {
            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType)).Cast<TextNoteType>().ToList();
            if (all.Count == 0) return null;
            return all.FirstOrDefault(t =>
                       string.Equals(t.Name, "STING Wire Annotation", StringComparison.OrdinalIgnoreCase))
                   ?? all[0];
        }

        private static WireAnnotationStyle Clone(WireAnnotationStyle s)
        {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<WireAnnotationStyle>(
                Newtonsoft.Json.JsonConvert.SerializeObject(s)) ?? WireAnnotationStyle.Default();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Selection filter
    // ─────────────────────────────────────────────────────────────────────────

    internal class WireElementSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element e) =>
            e?.Category?.Id.Value == (long)BuiltInCategory.OST_Wire;
        public bool AllowReference(Reference r, XYZ p) => true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Command — single pick
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Places a cable-spec annotation (tick marks + label) on a single picked
    /// Revit Wire element, sourced from the wire's circuit. Compute-on-place:
    /// nothing is stamped on the wire. Mirrors <c>WireAnnotateCommand</c>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WireElementAnnotateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc; var uidoc = ctx.UIDoc; var view = doc.ActiveView;
                if (view == null || view is View3D || view is ViewSchedule)
                {
                    TaskDialog.Show("STING Wire Element Annotation",
                        "Wire annotations require a plan, section, or elevation view.");
                    return Result.Cancelled;
                }

                WireElementAnnotationEngine.ClearBoxes(doc, view.Id);

                Reference reference;
                try
                {
                    reference = uidoc.Selection.PickObject(ObjectType.Element,
                        new WireElementSelectionFilter(), "Pick a wire to annotate");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

                if (!(doc.GetElement(reference) is Wire wire))
                {
                    TaskDialog.Show("STING Wire Element Annotation", "Picked element is not a Wire.");
                    return Result.Cancelled;
                }

                var compute = WireElementAnnotationEngine.Compute(wire);
                if (!compute.HasCircuit)
                {
                    TaskDialog.Show("STING Wire Element Annotation",
                        "This wire is not assigned to a circuit — nothing to annotate.\n\n" +
                        "Assign the wire to an electrical system first.");
                    return Result.Cancelled;
                }

                if (WireElementAnnotationEngine.HasAnnotation(doc, view, wire))
                {
                    var dup = new TaskDialog("STING Wire Element Annotation")
                    {
                        MainInstruction = "This wire is already annotated in the active view.",
                        MainContent     = "Replace the existing annotation?",
                        CommonButtons   = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                        DefaultButton   = TaskDialogResult.No,
                    };
                    if (dup.Show() != TaskDialogResult.Yes) return Result.Cancelled;
                }

                var style = WireAnnotationStyleOverride.Merge(WireAnnotationStyleStore.Load(doc), wire);

                ElementId placedId;
                using (var t = new Transaction(doc, "STING Place Wire Element Annotation"))
                {
                    t.Start();
                    WireElementAnnotationEngine.RemoveAnnotation(doc, view, wire);
                    placedId = WireElementAnnotationEngine.Place(doc, view, wire, compute.Data, style);
                    t.Commit();
                }

                if (placedId == ElementId.InvalidElementId)
                {
                    TaskDialog.Show("STING Wire Element Annotation",
                        "Could not place the annotation (wire geometry unavailable).");
                    return Result.Cancelled;
                }

                if (!compute.HasLength)
                    TaskDialog.Show("STING Wire Element Annotation",
                        "Annotation placed. Voltage drop was omitted — the circuit length " +
                        "is not available for this circuit (never estimated from the drawn wire).");

                StingLog.Info($"Wire element annotation placed on wire {wire.Id.Value}" +
                              (compute.HasLength ? "" : " (VD omitted — no circuit length)"));
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("WireElementAnnotateCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Command — batch (all wires in active view)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Annotates every Revit Wire in the active view that is not already
    /// annotated. Mirrors <c>WireAnnotateBatchCommand</c>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WireElementAnnotateBatchCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc; var view = doc.ActiveView;
                if (view == null || view is View3D || view is ViewSchedule)
                {
                    TaskDialog.Show("STING Wire Element Annotation",
                        "Wire annotations require a plan, section, or elevation view.");
                    return Result.Cancelled;
                }

                var wires = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_Wire)
                    .WhereElementIsNotElementType()
                    .OfType<Wire>()
                    .ToList();

                if (wires.Count == 0)
                {
                    TaskDialog.Show("STING Wire Element Annotation", "No wires found in the active view.");
                    return Result.Cancelled;
                }

                var td = new TaskDialog("STING Wire Element Annotation")
                {
                    MainInstruction = $"Found {wires.Count} wire(s) in the active view. Annotate all?",
                    CommonButtons   = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton   = TaskDialogResult.Yes,
                };
                if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;

                WireElementAnnotationEngine.ClearBoxes(doc, view.Id);
                var baseStyle = WireAnnotationStyleStore.Load(doc);

                int placed = 0, failed = 0, skipped = 0, noCircuit = 0, noLength = 0;
                bool cancelled = false, loggedLengthWarn = false;

                var prog = StingProgressDialog.Show("STING Wire Element Annotation", wires.Count);
                using (var tg = new TransactionGroup(doc, "STING Batch Wire Element Annotations"))
                {
                    tg.Start();
                    foreach (var wire in wires)
                    {
                        prog.Increment($"Wire {wire.Id.Value}");
                        if (EscapeChecker.IsEscapePressed()) { cancelled = true; break; }

                        if (WireElementAnnotationEngine.HasAnnotation(doc, view, wire)) { skipped++; continue; }

                        var compute = WireElementAnnotationEngine.Compute(wire);
                        if (!compute.HasCircuit) { noCircuit++; continue; }
                        if (!compute.HasLength)
                        {
                            noLength++;
                            if (!loggedLengthWarn)
                            {
                                StingLog.Warn("WireElement batch: one or more circuits have no length — " +
                                              "voltage drop omitted (never estimated from drawn wire).");
                                loggedLengthWarn = true;
                            }
                        }

                        var style = WireAnnotationStyleOverride.Merge(baseStyle, wire);
                        using (var t = new Transaction(doc, "STING Wire Element Annot"))
                        {
                            t.Start();
                            try
                            {
                                var id = WireElementAnnotationEngine.Place(doc, view, wire, compute.Data, style);
                                if (id != ElementId.InvalidElementId) placed++; else failed++;
                            }
                            catch (Exception ex)
                            {
                                failed++;
                                StingLog.Warn($"Batch wire-element annot {wire.Id.Value}: {ex.Message}");
                            }
                            t.Commit();
                        }
                    }
                    tg.Assimilate();
                }
                prog.Close();

                string suffix = "";
                if (skipped   > 0) suffix += $" {skipped} already annotated (skipped).";
                if (noCircuit > 0) suffix += $" {noCircuit} not on a circuit (skipped).";
                if (noLength  > 0) suffix += $" {noLength} placed without VD (no circuit length).";
                if (failed    > 0) suffix += $" {failed} failed.";
                if (cancelled)     suffix += " Cancelled by user.";
                TaskDialog.Show("STING Wire Element Annotation", $"Annotated {placed} wire(s).{suffix}");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("WireElementAnnotateBatchCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
