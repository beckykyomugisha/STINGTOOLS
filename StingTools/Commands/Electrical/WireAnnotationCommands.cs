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
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using StingTools.Core;
using StingTools.Core.Electrical;

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
        private const double MmPerFt    = 304.8;
        private const string MarkerTxt  = "STING_WIRE_ANNOT";
        private const string TickMarker = "STING_WIRE_TICK";
        private const string TickStyle  = "Wire Tick Marks";

        private static string MarkerFor(string uniqueId) =>
            string.IsNullOrEmpty(uniqueId) ? MarkerTxt : MarkerTxt + "|" + uniqueId;

        private static bool MatchesMarker(Element el, string target)
        {
            try
            {
                var p = el?.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                return string.Equals(p?.AsString(), target, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        public static int RemoveAnnotationForConduit(Document doc, View view, Element conduit)
        {
            if (doc == null || view == null || conduit == null) return 0;
            string target = MarkerFor(conduit.UniqueId);
            int removed = 0;
            try
            {
                var notes = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(TextNote))
                    .Cast<Element>()
                    .Where(n => MatchesMarker(n, target))
                    .ToList();
                var tags = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag))
                    .Cast<Element>()
                    .Where(n => MatchesMarker(n, target))
                    .ToList();
                foreach (var n in notes)
                {
                    try { doc.Delete(n.Id); removed++; }
                    catch (Exception ex) { StingLog.Warn("RemoveAnnot note: " + ex.Message); }
                }
                foreach (var n in tags)
                {
                    try { doc.Delete(n.Id); removed++; }
                    catch (Exception ex) { StingLog.Warn("RemoveAnnot tag: " + ex.Message); }
                }
            }
            catch (Exception ex) { StingLog.Warn("RemoveAnnotationForConduit: " + ex.Message); }
            return removed;
        }

        public static bool HasAnnotation(Document doc, View view, Element conduit)
        {
            if (doc == null || view == null || conduit == null) return false;
            string target = MarkerFor(conduit.UniqueId);
            try
            {
                bool noteHit = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(TextNote))
                    .Cast<Element>()
                    .Any(n => MatchesMarker(n, target));
                if (noteHit) return true;
                return new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag))
                    .Cast<Element>()
                    .Any(t => MatchesMarker(t, target));
            }
            catch { return false; }
        }

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

        public static WireAnnotationData ApplyProfile(WireAnnotationData d, WireProfile p)
        {
            if (p == null) return d;
            return new WireAnnotationData(
                d.Phase,
                p.Cores > 0 ? p.Cores : d.CoreCount,
                p.CsaMm2 > 0 ? p.CsaMm2 : d.CsaMm2,
                string.IsNullOrEmpty(p.ConductorMat) ? d.ConductorMat : p.ConductorMat,
                d.CircuitNumber, d.PanelName, d.VoltDropPct, d.DiameterMm, d.FillPct);
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

            var perpDir = ResolvePerpInView(view, axisDir);

            double offsetFt = 600.0 / MmPerFt;
            var annotPt = mid + perpDir * offsetFt;

            bool is3D = view is View3D;

            // Move-with-host path: if the project has loaded a tag family
            // whose name signals it's the STING wire-annotation tag, use
            // IndependentTag so it tracks the conduit. Required path in 3D.
            var tagId = TryPlaceIndependentTag(doc, view, conduit, annotPt, conduit?.UniqueId);
            if (tagId != ElementId.InvalidElementId)
            {
                if (addTickMarks && !is3D)
                    PlaceTickMarks(doc, view, conduit, data.CoreCount);
                return tagId;
            }

            // TextNote / DetailCurve are not available in 3D views.
            if (is3D)
            {
                StingLog.Warn("3D wire annotation requires a loaded Conduit-Tag family with 'Wire Annotation' or 'STING Wire' in the name.");
                return ElementId.InvalidElementId;
            }

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

            MarkAsWireAnnotation(note, conduit?.UniqueId);
            PlaceLeader(doc, view, mid, annotPt, perpDir);

            if (addTickMarks)
                PlaceTickMarks(doc, view, conduit, data.CoreCount);

            return note.Id;
        }

        private static void PlaceLeader(Document doc, View view, XYZ conduitMid, XYZ annotPt, XYZ perpDir)
        {
            if (doc == null || view == null) return;
            try
            {
                double offsetFt = 50.0 / MmPerFt;
                var leaderEnd  = annotPt - perpDir * offsetFt;
                var dc = doc.Create.NewDetailCurve(view, Line.CreateBound(conduitMid, leaderEnd));
                StampTickMarker(dc);
            }
            catch (Exception ex) { StingLog.Warn("Wire annot leader: " + ex.Message); }
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

            var perpDir = ResolvePerpInView(view, axisDir);

            double maxSpacingFt = 1500.0 / MmPerFt;
            double spacing   = len / (tickCount + 1);
            double startOffset;
            if (spacing > maxSpacingFt)
            {
                spacing = maxSpacingFt;
                double clusterLen = spacing * (tickCount - 1);
                startOffset = (len - clusterLen) / 2.0;
            }
            else
            {
                startOffset = spacing;
            }
            double tickLenFt = 6.0 / MmPerFt;

            Category tickSub = ResolveTickSubcategory(doc);

            for (int i = 0; i < tickCount; i++)
            {
                var startPt = p0 + axisDir * (startOffset + spacing * i);
                var endPt   = startPt + perpDir * tickLenFt;
                try
                {
                    var dc = doc.Create.NewDetailCurve(view, Line.CreateBound(startPt, endPt));
                    StampTickMarker(dc);
                    if (tickSub != null && dc != null)
                    {
                        try
                        {
                            var newGs = tickSub.GetGraphicsStyle(GraphicsStyleType.Projection);
                            if (newGs != null) dc.LineStyle = newGs;
                        }
                        catch { /* OST_Wire subcat not always valid for DetailLine.LineStyle */ }
                    }
                }
                catch (Exception ex)
                { StingLog.Warn($"Tick mark {i}: {ex.Message}"); }
            }
        }

        public static void MarkAsWireAnnotation(TextNote note, string conduitUniqueId = null)
        {
            if (note == null) return;
            try
            {
                var p = note.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (p != null && !p.IsReadOnly) p.Set(MarkerFor(conduitUniqueId));
            }
            catch (Exception ex) { StingLog.Warn("MarkAsWireAnnotation: " + ex.Message); }
        }

        private static void StampTickMarker(CurveElement ce)
        {
            if (ce == null) return;
            try
            {
                var p = ce.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (p != null && !p.IsReadOnly) p.Set(TickMarker);
            }
            catch { /* DetailLine may not expose Comments — fall back to line-style match */ }
        }

        public static bool IsWireAnnotation(TextNote note) => IsWireAnnotationCore(note);
        public static bool IsWireAnnotationTag(IndependentTag tag) => IsWireAnnotationCore(tag);

        private static bool IsWireAnnotationCore(Element el)
        {
            if (el == null) return false;
            try
            {
                var p = el.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                var v = p?.AsString();
                return !string.IsNullOrEmpty(v)
                    && (string.Equals(v, MarkerTxt, StringComparison.Ordinal)
                        || v.StartsWith(MarkerTxt + "|", StringComparison.Ordinal));
            }
            catch { return false; }
        }

        public static bool IsTickMark(CurveElement ce)
        {
            if (ce == null) return false;
            try
            {
                var p = ce.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (string.Equals(p?.AsString(), TickMarker, StringComparison.Ordinal)) return true;

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

        public static bool EndConnectsToPanel(Element conduit, XYZ endPt)
        {
            if (conduit == null || endPt == null) return false;
            ConnectorManager cm = null;
            try
            {
                if (conduit is MEPCurve mc) cm = mc.ConnectorManager;
                else if (conduit is FamilyInstance fi) cm = fi.MEPModel?.ConnectorManager;
            }
            catch { return false; }
            if (cm == null) return false;

            Connector startConn = null;
            try
            {
                foreach (Connector c in cm.Connectors)
                {
                    if (c.ConnectorType != ConnectorType.End) continue;
                    if (c.Origin != null && c.Origin.IsAlmostEqualTo(endPt))
                    { startConn = c; break; }
                }
            }
            catch { return false; }
            if (startConn == null || !startConn.IsConnected) return false;

            var visited = new HashSet<long>();
            var frontier = new List<Connector> { startConn };
            const int maxDepth = 4;
            for (int depth = 0; depth < maxDepth && frontier.Count > 0; depth++)
            {
                var next = new List<Connector>();
                foreach (var fc in frontier)
                {
                    ConnectorSet refs;
                    try { refs = fc.AllRefs; } catch { continue; }
                    if (refs == null) continue;
                    foreach (Connector other in refs)
                    {
                        var owner = other?.Owner;
                        if (owner == null || owner.Id == conduit.Id) continue;
                        long oid = owner.Id.Value;
                        if (visited.Contains(oid)) continue;
                        visited.Add(oid);

                        var catId = owner.Category?.Id?.Value ?? 0;
                        if (catId == (long)BuiltInCategory.OST_ElectricalEquipment)
                            return true;

                        if (catId == (long)BuiltInCategory.OST_ConduitFitting
                         || catId == (long)BuiltInCategory.OST_Conduit
                         || catId == (long)BuiltInCategory.OST_CableTray
                         || catId == (long)BuiltInCategory.OST_CableTrayFitting)
                        {
                            ConnectorManager ocm = null;
                            try
                            {
                                if (owner is MEPCurve omc) ocm = omc.ConnectorManager;
                                else if (owner is FamilyInstance ofi) ocm = ofi.MEPModel?.ConnectorManager;
                            }
                            catch { ocm = null; }
                            if (ocm == null) continue;
                            try
                            {
                                foreach (Connector pc in ocm.Connectors)
                                {
                                    if (pc.ConnectorType != ConnectorType.End) continue;
                                    if (pc.Id == other.Id) continue;
                                    next.Add(pc);
                                }
                            }
                            catch { /* ignore */ }
                        }
                    }
                }
                frontier = next;
            }
            return false;
        }

        public static FamilySymbol ResolveOptInWireTagSymbol(Document doc)
        {
            try
            {
                var symbols = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_ConduitTags)
                    .Cast<FamilySymbol>()
                    .ToList();
                if (symbols.Count == 0) return null;
                return symbols.FirstOrDefault(s =>
                    (s.FamilyName ?? "").IndexOf("Wire Annotation", StringComparison.OrdinalIgnoreCase) >= 0
                 || (s.FamilyName ?? "").IndexOf("STING Wire",      StringComparison.OrdinalIgnoreCase) >= 0
                 || (s.Name       ?? "").IndexOf("Wire Annotation", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch (Exception ex)
            {
                StingLog.Warn("ResolveOptInWireTagSymbol: " + ex.Message);
                return null;
            }
        }

        public static ElementId TryPlaceIndependentTag(Document doc, View view,
            Element conduit, XYZ headPt, string conduitUniqueId)
        {
            var sym = ResolveOptInWireTagSymbol(doc);
            if (sym == null) return ElementId.InvalidElementId;
            try
            {
                if (!sym.IsActive) { sym.Activate(); doc.Regenerate(); }
                var tag = IndependentTag.Create(doc, sym.Id, view.Id,
                    new Reference(conduit), addLeader: true,
                    TagOrientation.Horizontal, headPt);
                if (tag == null) return ElementId.InvalidElementId;
                var p = tag.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (p != null && !p.IsReadOnly) p.Set(MarkerFor(conduitUniqueId));
                return tag.Id;
            }
            catch (Exception ex)
            {
                StingLog.Warn("IndependentTag.Create: " + ex.Message);
                return ElementId.InvalidElementId;
            }
        }

        public static XYZ ResolvePerpInView(View view, XYZ axisDir)
        {
            // For 3D views the natural "perpendicular in screen plane" is the
            // view's RightDirection (parallel to the screen X axis). For
            // plan / section / elevation views we use Z×axis so the offset
            // sits cleanly in the view plane.
            if (view is View3D)
            {
                try
                {
                    var right = view.RightDirection;
                    var n = right - axisDir * right.DotProduct(axisDir);
                    if (n.GetLength() > 1e-6) return n.Normalize();
                }
                catch { }
            }
            var perpRaw = XYZ.BasisZ.CrossProduct(axisDir);
            if (perpRaw.GetLength() > 1e-6) return perpRaw.Normalize();
            return XYZ.BasisX;
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
                if (view is ViewSchedule)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        "Wire annotations cannot be placed on a schedule view.");
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

                if (WireAnnotationEngine.HasAnnotation(doc, view, conduit))
                {
                    var dup = new TaskDialog("STING Wire Annotation")
                    {
                        MainInstruction = "This conduit is already annotated in the active view.",
                        MainContent     = "Replace the existing annotation?",
                        CommonButtons   = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                        DefaultButton   = TaskDialogResult.No,
                    };
                    if (dup.Show() != TaskDialogResult.Yes) return Result.Cancelled;
                }

                using (var t = new Transaction(doc, "STING Place Wire Annotation"))
                {
                    t.Start();
                    WireAnnotationEngine.RemoveAnnotationForConduit(doc, view, conduit);
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
                if (view is ViewSchedule)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        "Wire annotations cannot be placed on a schedule view.");
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

                int placed = 0, failed = 0, skipped = 0;
                using (var tg = new TransactionGroup(doc, "STING Batch Wire Annotations"))
                {
                    tg.Start();
                    foreach (var conduit in conduits)
                    {
                        if (WireAnnotationEngine.HasAnnotation(doc, view, conduit))
                        {
                            skipped++;
                            continue;
                        }
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

                string suffix = "";
                if (skipped > 0) suffix += $" {skipped} already annotated (skipped).";
                if (failed > 0)  suffix += $" {failed} failed.";
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

                var curve  = lc.Curve;
                var p0     = curve.GetEndPoint(0);
                var p1     = curve.GetEndPoint(1);
                var rawAxis= p1 - p0;
                if (rawAxis.GetLength() < 1e-6)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        "Conduit too short to place home-run arrow.");
                    return Result.Cancelled;
                }

                // Connector-graph inspection: prefer to point arrow toward
                // the panel-side end. Falls back to GetEndPoint(1) when the
                // network can't classify either end.
                bool end0Panel = WireAnnotationEngine.EndConnectsToPanel(conduit, p0);
                bool end1Panel = WireAnnotationEngine.EndConnectsToPanel(conduit, p1);

                XYZ arrowBase = p1;
                XYZ arrowDir;
                if (end0Panel && !end1Panel)
                {
                    arrowBase = p1;
                    arrowDir  = (p0 - p1).Normalize();
                }
                else if (end1Panel && !end0Panel)
                {
                    arrowBase = p0;
                    arrowDir  = (p1 - p0).Normalize();
                }
                else
                {
                    arrowBase = p1;
                    arrowDir  = (p0 - p1).Normalize();
                }

                var perpRaw  = XYZ.BasisZ.CrossProduct(arrowDir);
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

                var indyTags = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag))
                    .Cast<IndependentTag>()
                    .Where(t => WireAnnotationEngine.IsWireAnnotationTag(t))
                    .ToList();

                var ticks = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(CurveElement))
                    .Cast<CurveElement>()
                    .Where(c => WireAnnotationEngine.IsTickMark(c))
                    .ToList();

                if (notes.Count == 0 && indyTags.Count == 0 && ticks.Count == 0)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        "No STING wire annotations found in active view.");
                    return Result.Cancelled;
                }

                int annotCount = notes.Count + indyTags.Count;
                var td = new TaskDialog("STING Wire Annotation")
                {
                    MainInstruction = $"Delete {annotCount} annotations and {ticks.Count} tick marks?",
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
                    foreach (var tg in indyTags)
                    {
                        try { doc.Delete(tg.Id); removed++; }
                        catch (Exception ex) { StingLog.Warn("Clear tag: " + ex.Message); }
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

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AnnotateCircuitCommand : IExternalCommand
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
                if (view is ViewSchedule)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        "Run AnnotateCircuit on a plan, section, elevation, or 3D view.");
                    return Result.Cancelled;
                }

                var systems = new FilteredElementCollector(doc)
                    .OfClass(typeof(ElectricalSystem))
                    .WhereElementIsNotElementType()
                    .Cast<ElectricalSystem>()
                    .OrderBy(s => s.PanelName ?? "")
                    .ThenBy(s => s.Name ?? "")
                    .ToList();
                if (systems.Count == 0)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        "No electrical circuits found in this project.");
                    return Result.Cancelled;
                }

                var items = systems.Select(s => new StingListPicker.ListItem
                {
                    Label  = $"{s.PanelName} – {s.Name}",
                    Detail = $"{s.LoadName}  |  {SafeRating(s):0}A  |  {SafeLoad(s):0} VA",
                    Tag    = s,
                }).ToList();
                var picked = StingListPicker.Show(
                    "Annotate Circuit",
                    "Pick the electrical circuit to annotate. STING will walk the circuit's conduit / cable-tray run and place wire-spec annotations on every segment in the active view.",
                    items, allowMultiSelect: false);
                if (picked == null || picked.Count == 0) return Result.Cancelled;
                if (!(picked[0].Tag is ElectricalSystem sys)) return Result.Cancelled;

                var route = CircuitWalker.Walk(doc, sys);
                int candidates = route.Conduits.Count + route.CableTrays.Count;
                if (candidates == 0)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        $"Circuit '{sys.Name}' has no conduits or cable trays on its path.\n\n" +
                        $"Total length: {route.TotalLengthMm:F0} mm.");
                    return Result.Cancelled;
                }

                var map = CircuitWireMap.Load(doc);
                string profileId = map.GetProfileId(sys.Name ?? "");
                var profile = !string.IsNullOrEmpty(profileId)
                    ? WireProfileRegistry.Get(doc, profileId)
                    : null;

                int placed = 0, failed = 0, skipped = 0;
                using (var tg = new TransactionGroup(doc, "STING Annotate Circuit"))
                {
                    tg.Start();
                    foreach (var seg in route.AllSegments)
                    {
                        if (WireAnnotationEngine.HasAnnotation(doc, view, seg))
                        { skipped++; continue; }

                        var data = WireAnnotationEngine.ReadWireData(seg);
                        if (profile != null) data = WireAnnotationEngine.ApplyProfile(data, profile);

                        using (var t = new Transaction(doc, "STING Annot Seg"))
                        {
                            t.Start();
                            try
                            {
                                var id = WireAnnotationEngine.PlaceAnnotation(
                                    doc, view, seg, data, addTickMarks: data.CoreCount > 0);
                                if (id != ElementId.InvalidElementId) placed++;
                                else failed++;
                            }
                            catch (Exception ex)
                            { failed++; StingLog.Warn($"AnnotateCircuit seg {seg.Id.Value}: {ex.Message}"); }
                            t.Commit();
                        }
                    }
                    tg.Assimilate();
                }

                StingLog.Info($"AnnotateCircuit '{sys.Name}': {placed} placed, {skipped} skipped, {failed} failed across {candidates} segments");

                StingResultPanel.Create("Wire Annotation — Circuit")
                    .SetSubtitle($"Circuit: {sys.PanelName} – {sys.Name}")
                    .AddSection("RESULT")
                    .Metric("Conduits on circuit",   route.Conduits.Count.ToString())
                    .Metric("Cable trays on circuit",route.CableTrays.Count.ToString())
                    .Metric("Annotations placed",    placed.ToString())
                    .Metric("Already annotated",     skipped.ToString())
                    .Metric("Failed",                failed.ToString())
                    .Metric("Route length (mm)",     route.TotalLengthMm.ToString("F0"))
                    .Metric("Profile",               profile != null ? profile.Name : "Auto (from circuit)")
                    .Show();
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("AnnotateCircuitCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static double SafeRating(ElectricalSystem sys)
        {
            try
            {
                var p = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_RATING_PARAM);
                if (p != null && p.StorageType == StorageType.Double) return p.AsDouble();
            }
            catch { }
            return 0;
        }
        private static double SafeLoad(ElectricalSystem sys)
        {
            try
            {
                var p = sys.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD);
                if (p != null && p.StorageType == StorageType.Double) return p.AsDouble();
            }
            catch { }
            return 0;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RefreshAllWireAnnotationsCommand : IExternalCommand
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
                if (view is ViewSchedule)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        "Refresh requires a model/drawing view (not a schedule).");
                    return Result.Cancelled;
                }

                var cats = new ElementMulticategoryFilter(new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_Conduit,
                    BuiltInCategory.OST_CableTray,
                });
                var hosts = new FilteredElementCollector(doc, view.Id)
                    .WherePasses(cats)
                    .WhereElementIsNotElementType()
                    .Where(e => WireAnnotationEngine.HasAnnotation(doc, view, e))
                    .ToList();

                if (hosts.Count == 0)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        "No annotated conduits or cable trays found in active view. Use AnnotateCircuit or Wire Annotate first.");
                    return Result.Cancelled;
                }

                var map = CircuitWireMap.Load(doc);
                int rebuilt = 0;
                using (var tg = new TransactionGroup(doc, "STING Refresh Wire Annotations"))
                {
                    tg.Start();
                    foreach (var host in hosts)
                    {
                        var data = WireAnnotationEngine.ReadWireData(host);
                        var profileId = map.GetProfileId(data.CircuitNumber);
                        var profile = !string.IsNullOrEmpty(profileId)
                            ? WireProfileRegistry.Get(doc, profileId) : null;
                        if (profile != null)
                            data = WireAnnotationEngine.ApplyProfile(data, profile);

                        using (var t = new Transaction(doc, "STING Refresh Annot"))
                        {
                            t.Start();
                            try
                            {
                                WireAnnotationEngine.RemoveAnnotationForConduit(doc, view, host);
                                WireAnnotationEngine.PlaceAnnotation(doc, view, host, data,
                                    addTickMarks: data.CoreCount > 0);
                                rebuilt++;
                            }
                            catch (Exception ex)
                            { StingLog.Warn($"Refresh annot {host.Id.Value}: {ex.Message}"); }
                            t.Commit();
                        }
                    }
                    tg.Assimilate();
                }

                TaskDialog.Show("STING Wire Annotation",
                    $"Rebuilt {rebuilt} of {hosts.Count} annotations from current circuit data.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("RefreshAllWireAnnotationsCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WireQuantityScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                var report = WireQuantityCalculator.Compute(doc, scopeView: null);
                string baseDir = OutputLocationHelper.GetOutputDirectory(doc);
                string outDir = System.IO.Path.Combine(baseDir ?? System.IO.Path.GetTempPath(), "wire_quantities");
                string csvPath = WireQuantityCalculator.WriteCsv(report, outDir);

                var b = StingResultPanel.Create("Wire Quantity Schedule")
                    .SetSubtitle($"Project totals — {report.Rows.Count} cable types, {report.TotalMetres:F1} m, {report.TotalKg:F1} kg")
                    .AddSection("TOTALS BY CABLE TYPE");
                if (report.Rows.Count == 0)
                {
                    b.Text("No annotated circuits found. Annotate at least one circuit first.");
                }
                else
                {
                    foreach (var r in report.Rows)
                    {
                        b.Text($"{r.ProfileName,-50}  {r.TotalMetres,8:F1} m   {r.TotalKg,7:F1} kg   ({r.SegmentCount} segs)");
                    }
                }
                if (!string.IsNullOrEmpty(csvPath))
                    b.SetCsvPath(csvPath);
                b.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("WireQuantityScheduleCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WireConfigurationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                StingTools.UI.WireConfigurationDialog.ShowFor(doc);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("WireConfigurationCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
