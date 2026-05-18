// StingTools — SLD Inline Annotation Commands (Phase 179)
//
// Comprehensive inline annotation workflow for Single Line Diagram views.
// All commands operate on the active view (must be a plan, section, or
// drafting view used as an SLD/schematic view).
//
// Commands (all inline — no secondary windows):
//   SldAnnotate_All         — auto-annotate every element in view that has
//                             electrical parameters (voltage, current, cable)
//   SldAnnotate_Voltage     — voltage annotations only (kV or V from ELC_CIR_VOLTAGE)
//   SldAnnotate_Current     — current annotations only (A from ELC_CIR_DESIGN_CURRENT)
//   SldAnnotate_Fault       — fault level annotations (kA from ELC_CIR_FAULT_LEVEL)
//   SldAnnotate_Cable       — cable/wire size annotations (mm² from ELC_CABLE_CSA)
//   SldAnnotate_Phase       — phase labels L1/L2/L3/N/PE as text notes
//   SldAnnotate_Load        — load annotations (kW/kVA from ELC_CIR_DESIGN_LOAD)
//   SldAnnotate_Format      — switch annotation format: Compact / Full / Reference
//   SldAnnotate_UpdateCalcs — pull updated values from panel schedule parameters
//   SldAnnotate_Toggle      — show/hide SLD annotation categories in active view
//   SldAnnotate_Clear       — remove all STING SLD text notes from active view
//   SldAnnotate_Audit       — report: which elements have annotations vs missing
//   SldAnnotate_Impedance   — loop impedance annotations (Zs/Ze)
//   SldAnnotate_Diversity   — diversity factor annotations
//
// Annotation format presets:
//   Compact  — e.g. "4mm² / 32A / B32"
//   Full     — e.g. "Cable: 4mm² Cu PVC/PVC | Ib: 32A | Iz: 47A | Breaker: B32"
//   Reference — circuit ref + panel name only
//
// Shared parameter names read from ParamRegistry:
//   ELC_CIR_VOLTAGE_TXT, ELC_CIR_DESIGN_CURRENT_TXT, ELC_CIR_FAULT_LEVEL_TXT,
//   ELC_CABLE_CSA_TXT, ELC_CIR_DESIGN_LOAD_TXT, ELC_CIR_BREAKER_RATING_TXT,
//   ELC_PNL_NAME_TXT, ELC_CIR_REF_TXT.
//
// Text notes are stamped with:
//   STING_SLD_ANNOT_KIND_TXT — annotation kind (Voltage/Current/Fault/Cable/Phase/Load)
//   STING_SLD_ANNOT_ELEM_ID_TXT — source element id for update/clear matching

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Symbols
{
    // ── Shared engine ─────────────────────────────────────────────────────────

    internal static class SldAnnotationEngine
    {
        // Stamp parameters written on every SLD TextNote for tracking / update / clear
        public const string STAMP_KIND   = "STING_SLD_ANNOT_KIND_TXT";
        public const string STAMP_ELEM   = "STING_SLD_ANNOT_ELEM_ID_TXT";
        public const string STAMP_FORMAT = "STING_SLD_ANNOT_FORMAT_TXT";

        // Source electrical parameters (read from element instances)
        private const string P_VOLTAGE   = "ELC_CIR_VOLTAGE_TXT";
        private const string P_CURRENT   = "ELC_CIR_DESIGN_CURRENT_TXT";
        private const string P_FAULT     = "ELC_CIR_FAULT_LEVEL_TXT";
        private const string P_CABLE     = "ELC_CABLE_CSA_TXT";
        private const string P_LOAD      = "ELC_CIR_DESIGN_LOAD_TXT";
        private const string P_BREAKER   = "ELC_CIR_BREAKER_RATING_TXT";
        private const string P_PANEL     = "ELC_PNL_NAME_TXT";
        private const string P_CIRCUIT   = "ELC_CIR_REF_TXT";
        private const string P_IMPEDANCE = "ELC_CIR_ZS_TXT";
        private const string P_DIVERSITY = "ELC_CIR_DIVERSITY_FACTOR_TXT";

        public enum AnnotKind
        {
            All, Voltage, Current, Fault, Cable, Phase, Load, Reference, Impedance, Diversity
        }

        public enum AnnotFormat { Compact, Full, Reference }

        // Session-level format preference (persisted per Revit session)
        public static AnnotFormat CurrentFormat { get; set; } = AnnotFormat.Compact;

        // ── Build annotation text for a given element and kind ───────────────

        public static string BuildAnnotText(Element el, AnnotKind kind, AnnotFormat fmt)
        {
            switch (kind)
            {
                case AnnotKind.Voltage:   return BuildVoltage(el, fmt);
                case AnnotKind.Current:   return BuildCurrent(el, fmt);
                case AnnotKind.Fault:     return BuildFault(el, fmt);
                case AnnotKind.Cable:     return BuildCable(el, fmt);
                case AnnotKind.Phase:     return BuildPhase(el, fmt);
                case AnnotKind.Load:      return BuildLoad(el, fmt);
                case AnnotKind.Reference: return BuildReference(el, fmt);
                case AnnotKind.Impedance: return BuildImpedance(el, fmt);
                case AnnotKind.Diversity: return BuildDiversity(el, fmt);
                case AnnotKind.All:       return BuildAll(el, fmt);
                default:                  return "";
            }
        }

        private static string Get(Element el, string paramName)
            => ParameterHelpers.GetString(el, paramName);

        private static string BuildVoltage(Element el, AnnotFormat fmt)
        {
            string v = Get(el, P_VOLTAGE);
            if (string.IsNullOrWhiteSpace(v)) return "";
            return fmt == AnnotFormat.Full ? $"Voltage: {v}" : v;
        }

        private static string BuildCurrent(Element el, AnnotFormat fmt)
        {
            string i = Get(el, P_CURRENT);
            if (string.IsNullOrWhiteSpace(i)) return "";
            return fmt == AnnotFormat.Full ? $"Ib: {i} A" : $"{i}A";
        }

        private static string BuildFault(Element el, AnnotFormat fmt)
        {
            string f = Get(el, P_FAULT);
            if (string.IsNullOrWhiteSpace(f)) return "";
            return fmt == AnnotFormat.Full ? $"Icc: {f} kA" : $"{f}kA";
        }

        private static string BuildCable(Element el, AnnotFormat fmt)
        {
            string csa = Get(el, P_CABLE);
            string ib  = Get(el, P_CURRENT);
            string brk = Get(el, P_BREAKER);
            if (string.IsNullOrWhiteSpace(csa)) return "";
            switch (fmt)
            {
                case AnnotFormat.Compact:
                    return string.Join(" / ",
                        new[] { csa, ib, brk }.Where(x => !string.IsNullOrEmpty(x)));
                case AnnotFormat.Full:
                    var parts = new List<string>();
                    if (!string.IsNullOrEmpty(csa)) parts.Add($"Cable: {csa} mm²");
                    if (!string.IsNullOrEmpty(ib))  parts.Add($"Ib: {ib}A");
                    if (!string.IsNullOrEmpty(brk)) parts.Add($"Breaker: {brk}");
                    return string.Join(" | ", parts);
                default:
                    return csa;
            }
        }

        private static string BuildPhase(Element el, AnnotFormat fmt)
        {
            // Phase label inferred from voltage / circuit reference
            string v = Get(el, P_VOLTAGE);
            if (string.IsNullOrWhiteSpace(v)) return "";
            // Heuristic: 230V → 1Ph, 400V → 3Ph
            bool is3ph = v.Contains("400") || v.Contains("415") || v.Contains("3") ||
                         v.Contains("kV");
            return fmt == AnnotFormat.Full
                ? (is3ph ? "L1 / L2 / L3 / N / PE" : "L / N / PE")
                : (is3ph ? "3Ph+N+PE" : "1Ph+N");
        }

        private static string BuildLoad(Element el, AnnotFormat fmt)
        {
            string load = Get(el, P_LOAD);
            if (string.IsNullOrWhiteSpace(load)) return "";
            return fmt == AnnotFormat.Full ? $"Load: {load} kW" : $"{load}kW";
        }

        private static string BuildReference(Element el, AnnotFormat fmt)
        {
            string pnl = Get(el, P_PANEL);
            string cir = Get(el, P_CIRCUIT);
            var parts  = new[] { pnl, cir }.Where(x => !string.IsNullOrEmpty(x));
            string refs = string.Join(" — ", parts);
            return string.IsNullOrEmpty(refs) ? "" :
                (fmt == AnnotFormat.Full ? $"Ref: {refs}" : refs);
        }

        private static string BuildImpedance(Element el, AnnotFormat fmt)
        {
            string zs = Get(el, P_IMPEDANCE);
            if (string.IsNullOrWhiteSpace(zs)) return "";
            return fmt == AnnotFormat.Full ? $"Zs: {zs} Ω" : $"Zs={zs}Ω";
        }

        private static string BuildDiversity(Element el, AnnotFormat fmt)
        {
            string df = Get(el, P_DIVERSITY);
            if (string.IsNullOrWhiteSpace(df)) return "";
            return fmt == AnnotFormat.Full ? $"Diversity: {df}" : $"Df={df}";
        }

        private static string BuildAll(Element el, AnnotFormat fmt)
        {
            // Compact all = "4mm²/32A/B32 | 230V | 6kA" (only non-empty fields)
            var segments = new List<string>
            {
                BuildCable(el, AnnotFormat.Compact),
                BuildVoltage(el, AnnotFormat.Compact),
                BuildFault(el, AnnotFormat.Compact),
                BuildLoad(el, AnnotFormat.Compact),
            }.Where(s => !string.IsNullOrEmpty(s)).ToList();

            if (!segments.Any()) return "";
            return fmt == AnnotFormat.Full
                ? string.Join(" | ", segments)
                : string.Join("  ", segments);
        }

        // ── Element collection ────────────────────────────────────────────────

        /// <summary>Finds all elements in the view that have at least one SLD electrical
        /// parameter set (voltage, current, cable CSA, or circuit ref).</summary>
        public static List<Element> CollectAnnotatableElements(Document doc, View view)
        {
            var result = new List<Element>();
            try
            {
                var col = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType();
                foreach (var el in col)
                {
                    if (el == null) continue;
                    // Skip TextNotes (those are our own annotations)
                    if (el is TextNote) continue;
                    // Must have at least one SLD electrical parameter with a value
                    bool hasData = !string.IsNullOrWhiteSpace(Get(el, P_VOLTAGE))
                                || !string.IsNullOrWhiteSpace(Get(el, P_CURRENT))
                                || !string.IsNullOrWhiteSpace(Get(el, P_CABLE))
                                || !string.IsNullOrWhiteSpace(Get(el, P_CIRCUIT));
                    if (hasData) result.Add(el);
                }
            }
            catch (Exception ex) { StingLog.Warn($"SldAnnotationEngine.Collect: {ex.Message}"); }
            return result;
        }

        // ── Text note placement ───────────────────────────────────────────────

        /// <summary>Places a TextNote near the element's bounding box centre on the view,
        /// stamped with kind + source element id for later update / clear operations.</summary>
        public static TextNote PlaceAnnotation(
            Document doc, View view, Element el,
            string text, AnnotKind kind, AnnotFormat fmt,
            double offsetFt = 0.5)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                // Anchor: bounding box centre + offset in view's UpDirection
                BoundingBoxXYZ bb = el.get_BoundingBox(view);
                XYZ centre = bb != null
                    ? (bb.Min + bb.Max) * 0.5
                    : (el.Location is LocationPoint lp ? lp.Point
                       : el.Location is LocationCurve lc ? lc.Curve.Evaluate(0.5, true)
                       : XYZ.Zero);

                XYZ up = view.UpDirection ?? XYZ.BasisY;
                XYZ pos = centre + up.Multiply(offsetFt);

                // Use the smallest available TextNoteType
                var typeId = GetSmallestTextNoteType(doc);
                var tnOpts = new TextNoteOptions(typeId)
                {
                    HorizontalAlignment = HorizontalTextAlignment.Left,
                    KeepRotatedTextReadable = true,
                };

                var tn = TextNote.Create(doc, view.Id, pos, text, tnOpts);
                if (tn == null) return null;

                // Stamp for tracking
                TrySetParam(tn, STAMP_KIND,   kind.ToString());
                TrySetParam(tn, STAMP_ELEM,   el.Id.Value.ToString());
                TrySetParam(tn, STAMP_FORMAT, fmt.ToString());
                return tn;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SldAnnotationEngine.PlaceAnnotation: {ex.Message}");
                return null;
            }
        }

        private static ElementId _cachedTextTypeId;
        private static ElementId GetSmallestTextNoteType(Document doc)
        {
            if (_cachedTextTypeId != null && _cachedTextTypeId != ElementId.InvalidElementId)
                return _cachedTextTypeId;
            try
            {
                var types = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>()
                    .OrderBy(t =>
                    {
                        var p = t.LookupParameter("Text Size");
                        return p?.AsDouble() ?? double.MaxValue;
                    })
                    .ToList();
                if (types.Count > 0)
                {
                    _cachedTextTypeId = types[0].Id;
                    return _cachedTextTypeId;
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetSmallestTextNoteType: {ex.Message}"); }
            return ElementId.InvalidElementId;
        }

        private static void TrySetParam(Element el, string pName, string value)
        {
            try
            {
                var p = el.LookupParameter(pName);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    p.Set(value);
            }
            catch { }
        }

        // ── Existing annotation management ────────────────────────────────────

        /// <summary>Collects all STING SLD TextNote annotations on the view.</summary>
        public static List<TextNote> CollectExistingAnnotations(Document doc, View view,
            string kindFilter = null)
        {
            var result = new List<TextNote>();
            try
            {
                var col = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(TextNote))
                    .Cast<TextNote>();
                foreach (var tn in col)
                {
                    var p = tn.LookupParameter(STAMP_ELEM);
                    if (p == null || p.StorageType != StorageType.String) continue;
                    if (string.IsNullOrEmpty(p.AsString())) continue; // not our note
                    if (!string.IsNullOrEmpty(kindFilter))
                    {
                        var pk = tn.LookupParameter(STAMP_KIND);
                        if (pk?.AsString() != kindFilter) continue;
                    }
                    result.Add(tn);
                }
            }
            catch (Exception ex) { StingLog.Warn($"SldAnnotationEngine.CollectExisting: {ex.Message}"); }
            return result;
        }

        /// <summary>Updates text of existing annotations from current element parameters.</summary>
        public static int UpdateAnnotations(Document doc, View view, AnnotFormat fmt)
        {
            int updated = 0;
            var existing = CollectExistingAnnotations(doc, view);
            foreach (var tn in existing)
            {
                try
                {
                    var pElem = tn.LookupParameter(STAMP_ELEM);
                    var pKind = tn.LookupParameter(STAMP_KIND);
                    if (pElem == null || pKind == null) continue;

                    if (!long.TryParse(pElem.AsString(), out long eid)) continue;
                    string kindStr = pKind.AsString();
                    if (!Enum.TryParse(kindStr, out AnnotKind kind)) continue;

                    var el = doc.GetElement(new ElementId(eid));
                    if (el == null) continue;

                    string newText = BuildAnnotText(el, kind, fmt);
                    if (string.IsNullOrWhiteSpace(newText)) continue;
                    if (newText == tn.Text) continue;

                    tn.Text = newText;
                    updated++;
                }
                catch (Exception ex) { StingLog.Warn($"SldAnnotationEngine.Update: {ex.Message}"); }
            }
            return updated;
        }

        // ── Visibility toggle ─────────────────────────────────────────────────

        /// <summary>Shows or hides the TextNote category in the active view. Since all SLD
        /// annotation notes are TextNotes, we use subcategory overrides via
        /// OverrideGraphicSettings to grey them out rather than hiding them entirely
        /// (hiding all text notes would affect title block text etc.).</summary>
        public static void ToggleAnnotationVisibility(Document doc, View view, bool show)
        {
            try
            {
                // Apply halftone to all stamped SLD text notes (or remove halftone).
                var existing = CollectExistingAnnotations(doc, view);
                var ogs = new OverrideGraphicSettings();
                if (!show) ogs.SetHalftone(true);
                foreach (var tn in existing)
                    view.SetElementOverrides(tn.Id, ogs);
            }
            catch (Exception ex) { StingLog.Warn($"SldAnnotationEngine.Toggle: {ex.Message}"); }
        }
    }

    // ── Command implementations ───────────────────────────────────────────────

    /// <summary>Auto-annotate all annotatable elements in the active SLD view.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SldAnnotateAllCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;
            View view = ctx.UIDoc.ActiveView;
            if (!IsSchematicView(view))
            {
                TaskDialog.Show("STING SLD Annotate",
                    "Active view is not a schematic/SLD-suitable view.\n" +
                    "Switch to a Drafting, Section, Floor Plan, or Engineering Plan view.");
                return Result.Succeeded;
            }

            var elements = SldAnnotationEngine.CollectAnnotatableElements(ctx.Doc, view);
            if (elements.Count == 0)
            {
                TaskDialog.Show("STING SLD Annotate",
                    "No elements with electrical parameters (voltage, current, cable CSA, " +
                    "circuit reference) found in this view.\n\n" +
                    "Populate ELC_* parameters first via the PANELS or CIRCTS workflows.");
                return Result.Succeeded;
            }

            var fmt = SldAnnotationEngine.CurrentFormat;
            int placed = 0;
            using (var t = new Transaction(ctx.Doc, "STING SLD Annotate All"))
            {
                t.Start();
                foreach (var el in elements)
                {
                    string text = SldAnnotationEngine.BuildAnnotText(el,
                        SldAnnotationEngine.AnnotKind.All, fmt);
                    var tn = SldAnnotationEngine.PlaceAnnotation(
                        ctx.Doc, view, el, text,
                        SldAnnotationEngine.AnnotKind.All, fmt);
                    if (tn != null) placed++;
                }
                t.Commit();
            }
            TaskDialog.Show("STING SLD Annotate",
                $"Annotated {placed} of {elements.Count} elements.\n" +
                $"Format: {fmt}. Use 'SLD Format' to change.\n" +
                "Update annotations after recalculations with 'SLD Update from Calcs'.");
            return Result.Succeeded;
        }
        private static bool IsSchematicView(View v) =>
            v != null && !v.IsTemplate &&
            (v.ViewType == ViewType.DraftingView || v.ViewType == ViewType.FloorPlan ||
             v.ViewType == ViewType.EngineeringPlan || v.ViewType == ViewType.Section ||
             v.ViewType == ViewType.Detail);
    }

    /// <summary>Voltage annotations only.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SldAnnotateVoltageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
            => AnnotateSingle(cd, SldAnnotationEngine.AnnotKind.Voltage, "Voltage");
        private static Result AnnotateSingle(ExternalCommandData cd,
            SldAnnotationEngine.AnnotKind kind, string label)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;
            var elements = SldAnnotationEngine.CollectAnnotatableElements(
                ctx.Doc, ctx.UIDoc.ActiveView);
            if (elements.Count == 0)
            {
                TaskDialog.Show($"STING SLD — {label}",
                    "No elements with electrical parameters found in this view.");
                return Result.Succeeded;
            }
            int placed = 0;
            var fmt = SldAnnotationEngine.CurrentFormat;
            using (var t = new Transaction(ctx.Doc, $"STING SLD Annotate {label}"))
            {
                t.Start();
                foreach (var el in elements)
                {
                    string text = SldAnnotationEngine.BuildAnnotText(el, kind, fmt);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var tn = SldAnnotationEngine.PlaceAnnotation(
                        ctx.Doc, ctx.UIDoc.ActiveView, el, text, kind, fmt);
                    if (tn != null) placed++;
                }
                t.Commit();
            }
            TaskDialog.Show($"STING SLD — {label}", $"Placed {placed} {label} annotations.");
            return Result.Succeeded;
        }
    }

    /// <summary>Current (Ib) annotations only.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SldAnnotateCurrentCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;
            var elements = SldAnnotationEngine.CollectAnnotatableElements(
                ctx.Doc, ctx.UIDoc.ActiveView);
            int placed = 0;
            var fmt = SldAnnotationEngine.CurrentFormat;
            using (var t = new Transaction(ctx.Doc, "STING SLD Annotate Current"))
            {
                t.Start();
                foreach (var el in elements)
                {
                    string text = SldAnnotationEngine.BuildAnnotText(
                        el, SldAnnotationEngine.AnnotKind.Current, fmt);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var tn = SldAnnotationEngine.PlaceAnnotation(
                        ctx.Doc, ctx.UIDoc.ActiveView, el, text,
                        SldAnnotationEngine.AnnotKind.Current, fmt);
                    if (tn != null) placed++;
                }
                t.Commit();
            }
            TaskDialog.Show("STING SLD — Current", $"Placed {placed} current (Ib) annotations.");
            return Result.Succeeded;
        }
    }

    /// <summary>Fault level (Icc/kA) annotations only.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SldAnnotateFaultCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;
            var elements = SldAnnotationEngine.CollectAnnotatableElements(
                ctx.Doc, ctx.UIDoc.ActiveView);
            int placed = 0;
            var fmt = SldAnnotationEngine.CurrentFormat;
            using (var t = new Transaction(ctx.Doc, "STING SLD Annotate Fault"))
            {
                t.Start();
                foreach (var el in elements)
                {
                    string text = SldAnnotationEngine.BuildAnnotText(
                        el, SldAnnotationEngine.AnnotKind.Fault, fmt);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var tn = SldAnnotationEngine.PlaceAnnotation(
                        ctx.Doc, ctx.UIDoc.ActiveView, el, text,
                        SldAnnotationEngine.AnnotKind.Fault, fmt);
                    if (tn != null) placed++;
                }
                t.Commit();
            }
            TaskDialog.Show("STING SLD — Fault Level",
                $"Placed {placed} fault level (kA) annotations.");
            return Result.Succeeded;
        }
    }

    /// <summary>Cable size + breaker rating annotations.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SldAnnotateCableCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;
            var elements = SldAnnotationEngine.CollectAnnotatableElements(
                ctx.Doc, ctx.UIDoc.ActiveView);
            int placed = 0;
            var fmt = SldAnnotationEngine.CurrentFormat;
            using (var t = new Transaction(ctx.Doc, "STING SLD Annotate Cable"))
            {
                t.Start();
                foreach (var el in elements)
                {
                    string text = SldAnnotationEngine.BuildAnnotText(
                        el, SldAnnotationEngine.AnnotKind.Cable, fmt);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var tn = SldAnnotationEngine.PlaceAnnotation(
                        ctx.Doc, ctx.UIDoc.ActiveView, el, text,
                        SldAnnotationEngine.AnnotKind.Cable, fmt, offsetFt: 0.4);
                    if (tn != null) placed++;
                }
                t.Commit();
            }
            TaskDialog.Show("STING SLD — Cable", $"Placed {placed} cable size annotations.");
            return Result.Succeeded;
        }
    }

    /// <summary>Phase labels (L1/L2/L3/N/PE).</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SldAnnotatePhaseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;
            var elements = SldAnnotationEngine.CollectAnnotatableElements(
                ctx.Doc, ctx.UIDoc.ActiveView);
            int placed = 0;
            var fmt = SldAnnotationEngine.CurrentFormat;
            using (var t = new Transaction(ctx.Doc, "STING SLD Annotate Phase"))
            {
                t.Start();
                foreach (var el in elements)
                {
                    string text = SldAnnotationEngine.BuildAnnotText(
                        el, SldAnnotationEngine.AnnotKind.Phase, fmt);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var tn = SldAnnotationEngine.PlaceAnnotation(
                        ctx.Doc, ctx.UIDoc.ActiveView, el, text,
                        SldAnnotationEngine.AnnotKind.Phase, fmt, offsetFt: 0.6);
                    if (tn != null) placed++;
                }
                t.Commit();
            }
            TaskDialog.Show("STING SLD — Phase Labels", $"Placed {placed} phase labels.");
            return Result.Succeeded;
        }
    }

    /// <summary>Load (kW/kVA) annotations.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SldAnnotateLoadCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;
            var elements = SldAnnotationEngine.CollectAnnotatableElements(
                ctx.Doc, ctx.UIDoc.ActiveView);
            int placed = 0;
            var fmt = SldAnnotationEngine.CurrentFormat;
            using (var t = new Transaction(ctx.Doc, "STING SLD Annotate Load"))
            {
                t.Start();
                foreach (var el in elements)
                {
                    string text = SldAnnotationEngine.BuildAnnotText(
                        el, SldAnnotationEngine.AnnotKind.Load, fmt);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var tn = SldAnnotationEngine.PlaceAnnotation(
                        ctx.Doc, ctx.UIDoc.ActiveView, el, text,
                        SldAnnotationEngine.AnnotKind.Load, fmt, offsetFt: 0.7);
                    if (tn != null) placed++;
                }
                t.Commit();
            }
            TaskDialog.Show("STING SLD — Load", $"Placed {placed} load annotations.");
            return Result.Succeeded;
        }
    }

    /// <summary>Circuit reference annotations (panel name + circuit ref).</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SldAnnotateReferenceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;
            var elements = SldAnnotationEngine.CollectAnnotatableElements(
                ctx.Doc, ctx.UIDoc.ActiveView);
            int placed = 0;
            var fmt = SldAnnotationEngine.CurrentFormat;
            using (var t = new Transaction(ctx.Doc, "STING SLD Annotate Reference"))
            {
                t.Start();
                foreach (var el in elements)
                {
                    string text = SldAnnotationEngine.BuildAnnotText(
                        el, SldAnnotationEngine.AnnotKind.Reference, fmt);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var tn = SldAnnotationEngine.PlaceAnnotation(
                        ctx.Doc, ctx.UIDoc.ActiveView, el, text,
                        SldAnnotationEngine.AnnotKind.Reference, fmt, offsetFt: -0.4);
                    if (tn != null) placed++;
                }
                t.Commit();
            }
            TaskDialog.Show("STING SLD — References", $"Placed {placed} circuit reference annotations.");
            return Result.Succeeded;
        }
    }

    /// <summary>Loop impedance (Zs/Ze) annotations.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SldAnnotateImpedanceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;
            var elements = SldAnnotationEngine.CollectAnnotatableElements(
                ctx.Doc, ctx.UIDoc.ActiveView);
            int placed = 0;
            var fmt = SldAnnotationEngine.CurrentFormat;
            using (var t = new Transaction(ctx.Doc, "STING SLD Annotate Impedance"))
            {
                t.Start();
                foreach (var el in elements)
                {
                    string text = SldAnnotationEngine.BuildAnnotText(
                        el, SldAnnotationEngine.AnnotKind.Impedance, fmt);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var tn = SldAnnotationEngine.PlaceAnnotation(
                        ctx.Doc, ctx.UIDoc.ActiveView, el, text,
                        SldAnnotationEngine.AnnotKind.Impedance, fmt, offsetFt: 0.8);
                    if (tn != null) placed++;
                }
                t.Commit();
            }
            TaskDialog.Show("STING SLD — Impedance", $"Placed {placed} loop impedance annotations.");
            return Result.Succeeded;
        }
    }

    /// <summary>Switch annotation format: Compact / Full / Reference-only.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SldAnnotationFormatCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;

            var current = SldAnnotationEngine.CurrentFormat;
            var td = new TaskDialog("STING SLD — Annotation Format")
            {
                MainInstruction = $"Current format: {current}",
                MainContent = "Compact — cable/current/breaker on one line (e.g. 4mm² / 32A / B32)\n" +
                              "Full    — verbose labels for each field (e.g. Cable: 4mm² | Ib: 32A)\n" +
                              "Reference — circuit ref + panel name only",
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Compact");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Full");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Reference only");

            var result = td.Show();
            if (result == TaskDialogResult.CommandLink1)
                SldAnnotationEngine.CurrentFormat = SldAnnotationEngine.AnnotFormat.Compact;
            else if (result == TaskDialogResult.CommandLink2)
                SldAnnotationEngine.CurrentFormat = SldAnnotationEngine.AnnotFormat.Full;
            else if (result == TaskDialogResult.CommandLink3)
                SldAnnotationEngine.CurrentFormat = SldAnnotationEngine.AnnotFormat.Reference;
            else
                return Result.Succeeded;

            TaskDialog.Show("STING SLD — Format",
                $"Format set to: {SldAnnotationEngine.CurrentFormat}\n" +
                "Run 'SLD Update from Calcs' to refresh existing annotations.");
            return Result.Succeeded;
        }
    }

    /// <summary>Update existing SLD annotations from current parameter values
    /// (use after running panel schedule calculations).</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SldUpdateFromCalcsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;

            var view = ctx.UIDoc.ActiveView;
            var existing = SldAnnotationEngine.CollectExistingAnnotations(ctx.Doc, view);
            if (existing.Count == 0)
            {
                TaskDialog.Show("STING SLD — Update",
                    "No STING SLD annotations found in this view.\n" +
                    "Run 'SLD Annotate All' first.");
                return Result.Succeeded;
            }

            int updated = 0;
            using (var t = new Transaction(ctx.Doc, "STING SLD Update Annotations"))
            {
                t.Start();
                updated = SldAnnotationEngine.UpdateAnnotations(
                    ctx.Doc, view, SldAnnotationEngine.CurrentFormat);
                t.Commit();
            }
            TaskDialog.Show("STING SLD — Update",
                $"Updated {updated} of {existing.Count} annotations from current parameter values.");
            return Result.Succeeded;
        }
    }

    /// <summary>Show / hide STING SLD text note annotations in the active view.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SldAnnotationToggleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;

            var view = ctx.UIDoc.ActiveView;
            var existing = SldAnnotationEngine.CollectExistingAnnotations(ctx.Doc, view);
            if (existing.Count == 0)
            {
                TaskDialog.Show("STING SLD — Toggle",
                    "No STING SLD annotations found in this view.");
                return Result.Succeeded;
            }

            var td = new TaskDialog("STING SLD — Toggle Visibility")
            {
                MainInstruction = $"{existing.Count} SLD annotation(s) found.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Show (remove halftone)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Hide (halftone / grey out)");

            var result = td.Show();
            bool show = result == TaskDialogResult.CommandLink1;
            bool hide = result == TaskDialogResult.CommandLink2;
            if (!show && !hide) return Result.Succeeded;

            using (var t = new Transaction(ctx.Doc, "STING SLD Toggle Visibility"))
            {
                t.Start();
                SldAnnotationEngine.ToggleAnnotationVisibility(ctx.Doc, view, show);
                t.Commit();
            }
            TaskDialog.Show("STING SLD — Toggle",
                $"{existing.Count} annotation(s) {(show ? "shown" : "hidden (halftone)")}.");
            return Result.Succeeded;
        }
    }

    /// <summary>Remove all STING SLD text note annotations from the active view.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SldAnnotationClearCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;

            var view = ctx.UIDoc.ActiveView;
            var existing = SldAnnotationEngine.CollectExistingAnnotations(ctx.Doc, view);
            if (existing.Count == 0)
            {
                TaskDialog.Show("STING SLD — Clear", "No STING SLD annotations found in this view.");
                return Result.Succeeded;
            }

            var confirm = TaskDialog.Show("STING SLD — Clear Annotations",
                $"Delete {existing.Count} SLD annotation(s) from '{view.Name}'?",
                TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
            if (confirm != TaskDialogResult.Yes) return Result.Succeeded;

            int deleted = 0;
            using (var t = new Transaction(ctx.Doc, "STING SLD Clear Annotations"))
            {
                t.Start();
                foreach (var tn in existing)
                {
                    try { ctx.Doc.Delete(tn.Id); deleted++; }
                    catch (Exception ex) { StingLog.Warn($"SldAnnotationClear: {ex.Message}"); }
                }
                t.Commit();
            }
            TaskDialog.Show("STING SLD — Clear", $"Removed {deleted} annotations.");
            return Result.Succeeded;
        }
    }

    /// <summary>Audit report: which elements have annotations vs missing.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SldAnnotationAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;

            var view = ctx.UIDoc.ActiveView;
            var annotatable = SldAnnotationEngine.CollectAnnotatableElements(ctx.Doc, view);
            var existing    = SldAnnotationEngine.CollectExistingAnnotations(ctx.Doc, view);

            var annotatedIds = new System.Collections.Generic.HashSet<long>(
                existing
                    .Select(tn =>
                    {
                        var p = tn.LookupParameter(SldAnnotationEngine.STAMP_ELEM);
                        return long.TryParse(p?.AsString(), out long id) ? id : -1L;
                    })
                    .Where(id => id >= 0));

            int annotated  = annotatable.Count(el => annotatedIds.Contains(el.Id.Value));
            int unannotated = annotatable.Count - annotated;

            string report = $"SLD Annotation Audit — '{view.Name}'\n\n" +
                $"Elements with electrical parameters:  {annotatable.Count}\n" +
                $"  Annotated:                          {annotated}\n" +
                $"  Missing annotations:                {unannotated}\n" +
                $"Existing STING SLD text notes:        {existing.Count}\n" +
                $"Current annotation format:            {SldAnnotationEngine.CurrentFormat}\n\n" +
                (unannotated > 0
                    ? "Run 'SLD Annotate All' to fill missing annotations."
                    : "All annotatable elements have annotations.");
            TaskDialog.Show("STING SLD — Annotation Audit", report);
            return Result.Succeeded;
        }
    }

    /// <summary>Diversity factor annotations.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SldAnnotateDiversityCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;
            var elements = SldAnnotationEngine.CollectAnnotatableElements(
                ctx.Doc, ctx.UIDoc.ActiveView);
            int placed = 0;
            var fmt = SldAnnotationEngine.CurrentFormat;
            using (var t = new Transaction(ctx.Doc, "STING SLD Annotate Diversity"))
            {
                t.Start();
                foreach (var el in elements)
                {
                    string text = SldAnnotationEngine.BuildAnnotText(
                        el, SldAnnotationEngine.AnnotKind.Diversity, fmt);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var tn = SldAnnotationEngine.PlaceAnnotation(
                        ctx.Doc, ctx.UIDoc.ActiveView, el, text,
                        SldAnnotationEngine.AnnotKind.Diversity, fmt, offsetFt: 0.9);
                    if (tn != null) placed++;
                }
                t.Commit();
            }
            TaskDialog.Show("STING SLD — Diversity", $"Placed {placed} diversity factor annotations.");
            return Result.Succeeded;
        }
    }
}
