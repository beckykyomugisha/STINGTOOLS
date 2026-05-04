// StingTools — Wire Annotation Symbol commands.
//
// Places BS 7671-style wire-spec annotations on conduit runs:
//   text label  : N × CSA mm² Cu | phase | circuit | panel
//   tick marks  : short detail lines crossing the conduit at N positions
//   home-run    : arrow from last-outlet end pointing toward source panel
//
// Reads ELC_WIRE_* shared parameters already on the conduit; degrades
// gracefully when fields are empty.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using StingTools.Core;

namespace StingTools.Commands.Electrical
{
    public record WireAnnotationData(
        string Phase,
        int    CoreCount,
        double CsaMm2,
        string ConductorMat,
        string CircuitNumber,
        string PanelName,
        double VoltDropPct,
        double DiameterMm,
        double FillPct
    );

    internal static class WireAnnotationEngine
    {
        private const double MmPerFt   = 304.8;
        private const string MarkerTxt = "STING_WIRE_ANNOT";
        private const string TickStyle = "Wire Tick Marks";

        public static WireAnnotationData ReadWireData(Element conduit)
        {
            string phase  = ParameterHelpers.GetString(conduit, "ELC_WIRE_PHASE_TXT");
            string mat    = ParameterHelpers.GetString(conduit, "ELC_WIRE_COND_MAT_TXT");
            string circ   = ParameterHelpers.GetString(conduit, "ELC_CIRCUIT_NR_TXT");
            string panel  = ParameterHelpers.GetString(conduit, "ELC_PNL_NAME_TXT");

            int    cores  = 0;
            double csa    = 0.0;
            double vd     = 0.0;
            double fill   = 0.0;

            int.TryParse(ParameterHelpers.GetString(conduit, "ELC_WIRE_CORE_COUNT_INT"),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out cores);
            double.TryParse(ParameterHelpers.GetString(conduit, "ELC_WIRE_CSA_MM2_NUM"),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out csa);
            double.TryParse(ParameterHelpers.GetString(conduit, "ELC_WIRE_VD_PCT_NUM"),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out vd);
            double.TryParse(ParameterHelpers.GetString(conduit, "ELC_CDT_CBL_FILL_PCT"),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out fill);

            double diaMm = 0.0;
            try
            {
                var p = conduit.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
                if (p != null && p.StorageType == StorageType.Double)
                    diaMm = p.AsDouble() * MmPerFt;
            }
            catch (Exception ex) { StingLog.Warn("ReadWireData diameter: " + ex.Message); }

            return new WireAnnotationData(
                phase, cores, csa,
                string.IsNullOrEmpty(mat) ? "" : mat,
                circ, panel, vd, diaMm, fill);
        }

        public static string BuildAnnotationText(WireAnnotationData d)
        {
            string baseSpec;
            if (d.CoreCount > 0 && d.CsaMm2 > 0)
            {
                string mat = string.IsNullOrEmpty(d.ConductorMat) ? "Cu" : d.ConductorMat;
                baseSpec = $"{d.CoreCount} × {d.CsaMm2:0.##} mm² {mat}";
            }
            else if (d.CsaMm2 > 0)
            {
                string mat = string.IsNullOrEmpty(d.ConductorMat) ? "Cu" : d.ConductorMat;
                baseSpec = $"{d.CsaMm2:0.##} mm² {mat}";
            }
            else
            {
                baseSpec = "? Wire";
            }

            var parts = new List<string> { baseSpec };
            if (!string.IsNullOrEmpty(d.Phase))         parts.Add(d.Phase);
            if (!string.IsNullOrEmpty(d.CircuitNumber)) parts.Add(d.CircuitNumber);
            if (!string.IsNullOrEmpty(d.PanelName))     parts.Add(d.PanelName);

            string result = string.Join("  |  ", parts);
            if (d.VoltDropPct > 3.0)
                result += $"  ⚠ VD={d.VoltDropPct:0.0}%";
            return result;
        }

        public static ElementId PlaceAnnotation(Document doc, View view,
            Element conduit, WireAnnotationData data, bool addTickMarks)
        {
            if (doc == null || view == null || conduit == null)
                return ElementId.InvalidElementId;
            var lc = conduit.Location as LocationCurve;
            if (lc?.Curve == null) return ElementId.InvalidElementId;

            var curve   = lc.Curve;
            var p0      = curve.GetEndPoint(0);
            var p1      = curve.GetEndPoint(1);
            var mid     = (p0 + p1) * 0.5;
            var axisRaw = p1 - p0;
            if (axisRaw.GetLength() < 1e-6) return ElementId.InvalidElementId;
            var axisDir = axisRaw.Normalize();

            var perpRaw = XYZ.BasisZ.CrossProduct(axisDir);
            var perpDir = perpRaw.GetLength() < 1e-6
                ? XYZ.BasisX
                : perpRaw.Normalize();

            double offsetFt = 600.0 / MmPerFt;
            var annotPt = mid + perpDir * offsetFt;

            TextNoteType tnt = ResolveTextNoteType(doc);
            ElementId tntId = tnt?.Id ?? ElementId.InvalidElementId;

            string text = BuildAnnotationText(data);
            TextNote note;
            try
            {
                note = TextNote.Create(doc, view.Id, annotPt, text, tntId);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"WireAnnotation TextNote.Create: {ex.Message}");
                return ElementId.InvalidElementId;
            }

            MarkAsWireAnnotation(note);

            if (addTickMarks)
                PlaceTickMarks(doc, view, conduit, data.CoreCount);

            return note.Id;
        }

        public static void PlaceTickMarks(Document doc, View view, Element conduit, int coreCount)
        {
            if (doc == null || view == null || conduit == null) return;
            var lc = conduit.Location as LocationCurve;
            if (lc?.Curve == null) return;

            int requested = coreCount == 0 ? 3 : coreCount;
            int tickCount = Math.Max(1, Math.Min(5, requested));

            var curve   = lc.Curve;
            var p0      = curve.GetEndPoint(0);
            var p1      = curve.GetEndPoint(1);
            var axisRaw = p1 - p0;
            double len  = axisRaw.GetLength();
            if (len < 1e-6) return;
            var axisDir = axisRaw.Normalize();

            var perpRaw = XYZ.BasisZ.CrossProduct(axisDir);
            var perpDir = perpRaw.GetLength() < 1e-6
                ? XYZ.BasisX
                : perpRaw.Normalize();

            double spacing   = len / (tickCount + 1);
            double tickLenFt = 6.0 / MmPerFt;

            Category tickSub = ResolveTickSubcategory(doc);

            for (int i = 1; i <= tickCount; i++)
            {
                var startPt = p0 + axisDir * (spacing * i);
                var endPt   = startPt + perpDir * tickLenFt;
                try
                {
                    var dc = doc.Create.NewDetailCurve(view, Line.CreateBound(startPt, endPt));
                    if (tickSub != null && dc != null)
                    {
                        try
                        {
                            var newGs = tickSub.GetGraphicsStyle(GraphicsStyleType.Projection);
                            if (newGs != null) dc.LineStyle = newGs;
                        }
                        catch (Exception ex2) { StingLog.Warn("Tick line style: " + ex2.Message); }
                    }
                }
                catch (Exception ex)
                { StingLog.Warn($"Tick mark {i}: {ex.Message}"); }
            }
        }

        public static void MarkAsWireAnnotation(TextNote note)
        {
            if (note == null) return;
            try
            {
                var p = note.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (p != null && !p.IsReadOnly) p.Set(MarkerTxt);
            }
            catch (Exception ex) { StingLog.Warn("MarkAsWireAnnotation: " + ex.Message); }
        }

        public static bool IsWireAnnotation(TextNote note)
        {
            if (note == null) return false;
            try
            {
                var p = note.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                return string.Equals(p?.AsString(), MarkerTxt, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        public static bool IsTickMark(CurveElement ce)
        {
            if (ce == null) return false;
            try
            {
                var gs = ce.LineStyle as GraphicsStyle;
                return string.Equals(gs?.Name, TickStyle, StringComparison.Ordinal)
                    || string.Equals(gs?.GraphicsStyleCategory?.Name, TickStyle, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private static TextNoteType ResolveTextNoteType(Document doc)
        {
            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .ToList();
            if (all.Count == 0) return null;

            var named = all.FirstOrDefault(t =>
                string.Equals(t.Name, "STING Wire Annotation", StringComparison.OrdinalIgnoreCase));
            if (named != null) return named;

            return all
                .OrderBy(t =>
                {
                    var p = t.get_Parameter(BuiltInParameter.TEXT_SIZE);
                    return p != null ? p.AsDouble() : double.MaxValue;
                })
                .First();
        }

        private static Category ResolveTickSubcategory(Document doc)
        {
            try
            {
                var wireCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Wire);
                if (wireCat?.SubCategories == null) return null;
                foreach (Category sub in wireCat.SubCategories)
                {
                    if (string.Equals(sub.Name, TickStyle, StringComparison.Ordinal))
                        return sub;
                }
            }
            catch (Exception ex) { StingLog.Warn("ResolveTickSubcategory: " + ex.Message); }
            return null;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WireAnnotateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc   = ctx.Doc;
                var uidoc = ctx.UIDoc;
                var view  = doc.ActiveView;
                if (view is View3D || view is ViewSchedule)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        "Wire annotations require a plan, section, or elevation view.");
                    return Result.Cancelled;
                }

                Reference reference;
                try
                {
                    reference = uidoc.Selection.PickObject(ObjectType.Element,
                        new ConduitSelectionFilter(), "Pick a conduit to annotate");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                { return Result.Cancelled; }

                var conduit = doc.GetElement(reference);
                var data    = WireAnnotationEngine.ReadWireData(conduit);

                using (var t = new Transaction(doc, "STING Place Wire Annotation"))
                {
                    t.Start();
                    WireAnnotationEngine.PlaceAnnotation(doc, view, conduit, data,
                        addTickMarks: data.CoreCount > 0);
                    t.Commit();
                }

                StingLog.Info($"Wire annotation placed on conduit {conduit.Id.Value}");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("WireAnnotateCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WireAnnotateBatchCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc  = ctx.Doc;
                var view = doc.ActiveView;
                if (view is View3D || view is ViewSchedule)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        "Wire annotations require a plan, section, or elevation view.");
                    return Result.Cancelled;
                }

                var conduits = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_Conduit)
                    .WhereElementIsNotElementType()
                    .ToList();

                if (conduits.Count == 0)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        "No conduits found in active view.");
                    return Result.Cancelled;
                }

                var td = new TaskDialog("STING Wire Annotation")
                {
                    MainInstruction = $"Found {conduits.Count} conduits. Annotate all?",
                    CommonButtons   = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton   = TaskDialogResult.Yes,
                };
                if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;

                int placed = 0, failed = 0;
                using (var tg = new TransactionGroup(doc, "STING Batch Wire Annotations"))
                {
                    tg.Start();
                    foreach (var conduit in conduits)
                    {
                        var data = WireAnnotationEngine.ReadWireData(conduit);
                        using (var t = new Transaction(doc, "STING Wire Annot"))
                        {
                            t.Start();
                            try
                            {
                                var id = WireAnnotationEngine.PlaceAnnotation(
                                    doc, view, conduit, data,
                                    addTickMarks: data.CoreCount > 0);
                                if (id != ElementId.InvalidElementId) placed++;
                                else failed++;
                            }
                            catch (Exception ex)
                            {
                                failed++;
                                StingLog.Warn($"Batch wire annot {conduit.Id.Value}: {ex.Message}");
                            }
                            t.Commit();
                        }
                    }
                    tg.Assimilate();
                }

                string suffix = failed > 0 ? $" {failed} failed." : "";
                TaskDialog.Show("STING Wire Annotation",
                    $"Annotated {placed} conduits.{suffix}");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("WireAnnotateBatchCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HomeRunArrowCommand : IExternalCommand
    {
        private const double MmPerFt = 304.8;

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc   = ctx.Doc;
                var uidoc = ctx.UIDoc;
                var view  = doc.ActiveView;
                if (view is View3D || view is ViewSchedule)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        "Home-run arrows require a plan, section, or elevation view.");
                    return Result.Cancelled;
                }

                Reference reference;
                try
                {
                    reference = uidoc.Selection.PickObject(ObjectType.Element,
                        new ConduitSelectionFilter(), "Pick a conduit for home-run arrow");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                { return Result.Cancelled; }

                var conduit = doc.GetElement(reference);
                var data    = WireAnnotationEngine.ReadWireData(conduit);
                var lc      = conduit.Location as LocationCurve;
                if (lc?.Curve == null)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        "Picked element has no curve geometry.");
                    return Result.Cancelled;
                }

                var curve     = lc.Curve;
                var p0        = curve.GetEndPoint(0);
                var p1        = curve.GetEndPoint(1);
                var arrowBase = p1;
                var axisRaw   = p1 - p0;
                if (axisRaw.GetLength() < 1e-6)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        "Conduit too short to place home-run arrow.");
                    return Result.Cancelled;
                }
                var axisDir  = axisRaw.Normalize();
                var arrowDir = axisDir.Negate();
                var perpRaw  = XYZ.BasisZ.CrossProduct(axisDir);
                var perpDir  = perpRaw.GetLength() < 1e-6
                    ? XYZ.BasisX
                    : perpRaw.Normalize();

                double arrowShaftFt = 150.0 / MmPerFt;
                double headLenFt    = 30.0  / MmPerFt;
                var arrowTip = arrowBase + arrowDir * arrowShaftFt;

                using (var t = new Transaction(doc, "STING Home Run Arrow"))
                {
                    t.Start();

                    FamilySymbol sym = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .Cast<FamilySymbol>()
                        .FirstOrDefault(s =>
                            (s.FamilyName ?? "").IndexOf("Home Run Arrow", StringComparison.OrdinalIgnoreCase) >= 0
                         || (s.FamilyName ?? "").IndexOf("HomeRun",        StringComparison.OrdinalIgnoreCase) >= 0);

                    bool placedFamily = false;
                    if (sym != null)
                    {
                        try
                        {
                            if (!sym.IsActive) { sym.Activate(); doc.Regenerate(); }
                            doc.Create.NewFamilyInstance(arrowBase, sym, view);
                            placedFamily = true;
                        }
                        catch (Exception ex)
                        { StingLog.Warn("HomeRun family place: " + ex.Message); }
                    }

                    if (!placedFamily)
                    {
                        try
                        {
                            doc.Create.NewDetailCurve(view,
                                Line.CreateBound(arrowBase, arrowTip));
                            double ang = Math.PI / 12.0;
                            var legBase = -arrowDir * headLenFt;
                            var leftLeg  = RotateAroundAxis(legBase, XYZ.BasisZ,  ang);
                            var rightLeg = RotateAroundAxis(legBase, XYZ.BasisZ, -ang);
                            doc.Create.NewDetailCurve(view,
                                Line.CreateBound(arrowTip, arrowTip + leftLeg));
                            doc.Create.NewDetailCurve(view,
                                Line.CreateBound(arrowTip, arrowTip + rightLeg));
                        }
                        catch (Exception ex)
                        { StingLog.Warn("HomeRun primitive draw: " + ex.Message); }
                    }

                    string label = !string.IsNullOrEmpty(data.CircuitNumber) ? data.CircuitNumber
                                 : !string.IsNullOrEmpty(data.PanelName)     ? data.PanelName
                                 : "HR";
                    var labelPt = arrowTip + perpDir * 0.15;
                    var tnt = new FilteredElementCollector(doc)
                        .OfClass(typeof(TextNoteType))
                        .Cast<TextNoteType>()
                        .FirstOrDefault();
                    try
                    {
                        TextNote.Create(doc, view.Id, labelPt, label,
                            tnt?.Id ?? ElementId.InvalidElementId);
                    }
                    catch (Exception ex)
                    { StingLog.Warn("HomeRun label: " + ex.Message); }

                    t.Commit();
                }

                StingLog.Info($"Home run arrow placed for conduit {conduit.Id.Value}");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("HomeRunArrowCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static XYZ RotateAroundAxis(XYZ vec, XYZ axis, double angleRad)
        {
            double cos = Math.Cos(angleRad), sin = Math.Sin(angleRad);
            return vec * cos
                 + axis.CrossProduct(vec) * sin
                 + axis * axis.DotProduct(vec) * (1 - cos);
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ClearWireAnnotationsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc  = ctx.Doc;
                var view = doc.ActiveView;

                var notes = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(TextNote))
                    .Cast<TextNote>()
                    .Where(n => WireAnnotationEngine.IsWireAnnotation(n))
                    .ToList();

                var ticks = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(CurveElement))
                    .Cast<CurveElement>()
                    .Where(c => WireAnnotationEngine.IsTickMark(c))
                    .ToList();

                if (notes.Count == 0 && ticks.Count == 0)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        "No STING wire annotations found in active view.");
                    return Result.Cancelled;
                }

                var td = new TaskDialog("STING Wire Annotation")
                {
                    MainInstruction = $"Delete {notes.Count} annotations and {ticks.Count} tick marks?",
                    CommonButtons   = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton   = TaskDialogResult.Yes,
                };
                if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;

                int removed = 0;
                using (var t = new Transaction(doc, "STING Clear Wire Annotations"))
                {
                    t.Start();
                    foreach (var n in notes)
                    {
                        try { doc.Delete(n.Id); removed++; }
                        catch (Exception ex) { StingLog.Warn("Clear note: " + ex.Message); }
                    }
                    foreach (var c in ticks)
                    {
                        try { doc.Delete(c.Id); removed++; }
                        catch (Exception ex) { StingLog.Warn("Clear tick: " + ex.Message); }
                    }
                    t.Commit();
                }

                TaskDialog.Show("STING Wire Annotation",
                    $"Removed {removed} elements.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("ClearWireAnnotationsCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    internal class ConduitSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element e) =>
            e?.Category?.Id.Value == (long)BuiltInCategory.OST_Conduit ||
            e?.Category?.Id.Value == (long)BuiltInCategory.OST_ConduitRun;
        public bool AllowReference(Reference r, XYZ p) => true;
    }
}
