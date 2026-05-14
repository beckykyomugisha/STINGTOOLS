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
using StingTools.UI;

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
        double FillPct,
        int    BendCount    // number of bends on this conduit run (BS 7671 §522.8.5)
    );

    internal static class WireAnnotationEngine
    {
        internal const double MmPerFt    = 304.8;
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

        private static List<Element> GetAnnotationsForConduit(Document doc, View view, Element conduit)
        {
            if (doc == null || view == null || conduit == null) return new List<Element>();
            string target = MarkerFor(conduit.UniqueId);
            var result = new List<Element>();
            try {
                result.AddRange(new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(TextNote))
                    .Cast<Element>()
                    .Where(n => MatchesMarker(n, target)));
                result.AddRange(new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag))
                    .Cast<Element>()
                    .Where(t => MatchesMarker(t, target)));
            } catch (Exception ex) { StingLog.Warn("GetAnnotationsForConduit: " + ex.Message); }
            return result;
        }

        public static int RemoveAnnotationForConduit(Document doc, View view, Element conduit)
        {
            int removed = 0;
            var items = GetAnnotationsForConduit(doc, view, conduit);
            foreach (var n in items)
            {
                try { doc.Delete(n.Id); removed++; }
                catch (Exception ex) { StingLog.Warn("RemoveAnnot: " + ex.Message); }
            }
            return removed;
        }

        public static bool HasAnnotation(Document doc, View view, Element conduit) =>
            GetAnnotationsForConduit(doc, view, conduit).Count > 0;

        public static WireAnnotationData ReadWireData(Element conduit)
        {
            string phase  = ParameterHelpers.GetString(conduit, "ELC_WIRE_PHASE_TXT");
            string mat    = ParameterHelpers.GetString(conduit, "ELC_WIRE_COND_MAT_TXT");
            string circ   = ParameterHelpers.GetString(conduit, "ELC_CIRCUIT_NR_TXT");
            string panel  = ParameterHelpers.GetString(conduit, "ELC_PNL_NAME_TXT");

            int    cores  = ParameterHelpers.GetInt(conduit, "ELC_WIRE_CORE_COUNT_INT", 0);
            double csa    = 0.0;
            double vd     = 0.0;
            double fill   = 0.0;

            try
            {
                var p = conduit.LookupParameter("ELC_WIRE_CSA_MM2_NUM");
                if (p == null) {
                    double.TryParse(ParameterHelpers.GetString(conduit, "ELC_WIRE_CSA_MM2_NUM"),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out csa);
                } else if (p.StorageType == StorageType.Double) {
                    csa = p.AsDouble();
                } else if (p.StorageType == StorageType.String) {
                    double.TryParse(p.AsString(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out csa);
                }
            } catch { }

            try
            {
                var p = conduit.LookupParameter("ELC_WIRE_VD_PCT_NUM");
                if (p == null) {
                    double.TryParse(ParameterHelpers.GetString(conduit, "ELC_WIRE_VD_PCT_NUM"),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out vd);
                } else if (p.StorageType == StorageType.Double) {
                    vd = p.AsDouble();
                } else if (p.StorageType == StorageType.String) {
                    double.TryParse(p.AsString(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out vd);
                }
            } catch { }

            // Recalculate VD from actual conduit length if stored value is missing/zero
            if (vd <= 0 && csa > 0 && cores > 0)
            {
                try
                {
                    var lc = conduit.Location as Autodesk.Revit.DB.LocationCurve;
                    if (lc?.Curve != null)
                    {
                        double lengthM = lc.Curve.Length * 0.3048; // ft → m
                        if (lengthM > 0.01)
                        {
                            // Read load current from connected ElectricalSystem if available
                            double currentA = 0;
                            try
                            {
                                foreach (Autodesk.Revit.DB.Connector c in ((Autodesk.Revit.DB.MEPCurve)conduit).ConnectorManager.Connectors)
                                {
                                    foreach (Autodesk.Revit.DB.Connector cr in c.AllRefs)
                                    {
                                        if (cr.Owner is Autodesk.Revit.DB.Electrical.ElectricalSystem sys)
                                        {
                                            currentA = sys.ApparentCurrent;
                                            break;
                                        }
                                    }
                                    if (currentA > 0) break;
                                }
                            }
                            catch { }

                            if (currentA <= 0) currentA = 16.0; // default 16A if no circuit found
                            int phases = cores >= 3 ? 3 : 1;
                            double voltV = phases == 3 ? 400.0 : 230.0;
                            string materialStr = string.IsNullOrEmpty(mat) ? "Cu" : mat;
                            vd = StingTools.Commands.Electrical.VoltageDrop.VoltageDropEngine.CalculateVoltDropPercent(
                                currentA, lengthM, csa, materialStr, voltV, phases);

                            // Write the recalculated value back to the element if inside a transaction
                            try
                            {
                                var vdP = conduit.LookupParameter("ELC_WIRE_VD_PCT_NUM");
                                if (vdP != null && !vdP.IsReadOnly && vdP.StorageType == Autodesk.Revit.DB.StorageType.Double)
                                    vdP.Set(vd);
                                else
                                {
                                    var vdPStr = conduit.LookupParameter("ELC_WIRE_VD_PCT_NUM");
                                    if (vdPStr != null && !vdPStr.IsReadOnly && vdPStr.StorageType == Autodesk.Revit.DB.StorageType.String)
                                        vdPStr.Set(vd.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                                }
                            }
                            catch { /* write-back is best-effort — may be outside a transaction */ }
                        }
                    }
                }
                catch (Exception ex) { StingTools.Core.StingLog.Warn($"VD recalc: {ex.Message}"); }
            }

            try
            {
                var p = conduit.LookupParameter("ELC_CDT_CBL_FILL_PCT");
                if (p == null) {
                    double.TryParse(ParameterHelpers.GetString(conduit, "ELC_CDT_CBL_FILL_PCT"),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out fill);
                } else if (p.StorageType == StorageType.Double) {
                    fill = p.AsDouble();
                } else if (p.StorageType == StorageType.String) {
                    double.TryParse(p.AsString(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out fill);
                }
            } catch { }

            double diaMm = 0.0;
            try
            {
                var p = conduit.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
                if (p != null && p.StorageType == StorageType.Double)
                    diaMm = p.AsDouble() * MmPerFt;
            }
            catch (Exception ex) { StingLog.Warn("ReadWireData diameter: " + ex.Message); }

            int bendCount = 0;
            try
            {
                int stored = ParameterHelpers.GetInt(conduit, "ELC_WIRE_BEND_COUNT_INT", 0);
                if (stored > 0) bendCount = stored;
            }
            catch { }

            return new WireAnnotationData(
                phase, cores, csa,
                string.IsNullOrEmpty(mat) ? "" : mat,
                circ, panel, vd, diaMm, fill, bendCount);
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
            var extras = new List<string>();
            if (d.DiameterMm > 0)
                extras.Add($"Ø{d.DiameterMm:0.#} mm");
            if (d.FillPct > 0)
            {
                string fillStr = $"Fill {d.FillPct:0.#}%";
                if (d.FillPct > 40.0) fillStr += " !";
                extras.Add(fillStr);
            }
            if (d.BendCount > 0)
            {
                string bendStr = $"{d.BendCount}B";
                if (d.BendCount >= 3) bendStr += "⚠"; // at BS 7671 §522.8.5 limit
                extras.Add(bendStr);
            }
            if (extras.Count > 0)
                result += "  |  " + string.Join("  |  ", extras);
            if (d.VoltDropPct > 3.0)
                result += $"  ** VD={d.VoltDropPct:0.0}%";
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

            // Move-with-host path: if the project has loaded a tag family
            // whose name signals it's the STING wire-annotation tag, use
            // IndependentTag so it tracks the conduit. Otherwise fall back
            // to a TextNote with our computed BS 7671 text.
            var tagId = TryPlaceIndependentTag(doc, view, conduit, annotPt, conduit?.UniqueId);
            if (tagId != ElementId.InvalidElementId)
            {
                if (addTickMarks)
                    PlaceTickMarks(doc, view, conduit, data.CoreCount);
                return tagId;
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
            if (coreCount <= 0) return;

            int tickCount = Math.Max(1, Math.Min(5, coreCount));

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

            const double minSizeFt = 2.5 / 304.8;
            var candidates = all
                .Select(t => {
                    var p = t.get_Parameter(BuiltInParameter.TEXT_SIZE);
                    return (type: t, size: p != null ? p.AsDouble() : 0.0);
                })
                .Where(x => x.size >= minSizeFt)
                .OrderBy(x => x.size)
                .ToList();

            return candidates.Count > 0
                ? candidates[0].type
                : all.OrderBy(t => {
                      var p = t.get_Parameter(BuiltInParameter.TEXT_SIZE);
                      return p != null ? p.AsDouble() : double.MaxValue;
                  }).First();
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
                    if (c.Origin != null && c.Origin.IsAlmostEqualTo(endPt, 1e-3))
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

        /// <summary>
        /// Places a home-run arrow on <paramref name="conduit"/> in
        /// <paramref name="view"/>.  Stamps the created elements with a
        /// marker of the form <c>STING_WIRE_HOMERUN|{conduit.UniqueId}</c>
        /// so <see cref="HomeRunArrowBatchCommand"/> can detect existing ones.
        /// Must be called inside an open Transaction.
        /// </summary>
        public static void PlaceHomeRunArrow(Document doc, View view, Element conduit)
        {
            if (doc == null || view == null || conduit == null) return;
            var lc = conduit.Location as LocationCurve;
            if (lc?.Curve == null) return;

            var curve  = lc.Curve;
            var p0     = curve.GetEndPoint(0);
            var p1     = curve.GetEndPoint(1);
            var rawAxis = p1 - p0;
            if (rawAxis.GetLength() < 1e-6) return;

            bool end0Panel = EndConnectsToPanel(conduit, p0);
            bool end1Panel = EndConnectsToPanel(conduit, p1);

            XYZ arrowBase;
            XYZ arrowDir;
            if (end0Panel && !end1Panel)
            { arrowBase = p1; arrowDir = (p0 - p1).Normalize(); }
            else if (end1Panel && !end0Panel)
            { arrowBase = p0; arrowDir = (p1 - p0).Normalize(); }
            else
            { arrowBase = p1; arrowDir = (p0 - p1).Normalize(); }

            var perpRaw = XYZ.BasisZ.CrossProduct(arrowDir);
            var perpDir = perpRaw.GetLength() < 1e-6 ? XYZ.BasisX : perpRaw.Normalize();

            string homeRunMarker = "STING_WIRE_HOMERUN|" + conduit.UniqueId;
            void Stamp(Element el)
            {
                try
                {
                    var p = el?.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (p != null && !p.IsReadOnly) p.Set(homeRunMarker);
                }
                catch { }
            }

            double arrowShaftFt = 150.0 / MmPerFt;
            double headLenFt    = 30.0  / MmPerFt;
            var arrowTip = arrowBase + arrowDir * arrowShaftFt;

            FamilySymbol sym = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_GenericAnnotation)
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
                    var fi = doc.Create.NewFamilyInstance(arrowBase, sym, view);
                    Stamp(fi);
                    placedFamily = true;
                }
                catch (Exception ex) { StingLog.Warn("PlaceHomeRunArrow family: " + ex.Message); }
            }

            if (!placedFamily)
            {
                try
                {
                    var shaft = doc.Create.NewDetailCurve(view, Line.CreateBound(arrowBase, arrowTip));
                    Stamp(shaft);
                    double ang = Math.PI / 12.0;
                    double cos = Math.Cos(ang), sin = Math.Sin(ang);
                    var legBase = -arrowDir * headLenFt;
                    XYZ RotZ(XYZ v, double a) {
                        double c = Math.Cos(a), s2 = Math.Sin(a);
                        return v * c + XYZ.BasisZ.CrossProduct(v) * s2
                               + XYZ.BasisZ * XYZ.BasisZ.DotProduct(v) * (1 - c);
                    }
                    var leftLeg  = RotZ(legBase,  ang);
                    var rightLeg = RotZ(legBase, -ang);
                    var legL = doc.Create.NewDetailCurve(view,
                        Line.CreateBound(arrowTip, arrowTip + leftLeg));
                    Stamp(legL);
                    var legR = doc.Create.NewDetailCurve(view,
                        Line.CreateBound(arrowTip, arrowTip + rightLeg));
                    Stamp(legR);
                }
                catch (Exception ex) { StingLog.Warn("PlaceHomeRunArrow primitives: " + ex.Message); }
            }

            // Label near arrowhead
            try
            {
                var data    = ReadWireData(conduit);
                string label = !string.IsNullOrEmpty(data.CircuitNumber) ? data.CircuitNumber
                             : !string.IsNullOrEmpty(data.PanelName)     ? data.PanelName
                             : "HR";
                var labelPt = arrowTip + perpDir * (50.0 / MmPerFt);
                var tnt = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault();
                var lbl = TextNote.Create(doc, view.Id, labelPt, label,
                    tnt?.Id ?? ElementId.InvalidElementId);
                Stamp(lbl);
            }
            catch (Exception ex) { StingLog.Warn("PlaceHomeRunArrow label: " + ex.Message); }
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

                int placed = 0, failed = 0, skipped = 0;
                bool cancelled = false;
                var prog = StingProgressDialog.Show("STING Wire Annotation", conduits.Count);
                using (var tg = new TransactionGroup(doc, "STING Batch Wire Annotations"))
                {
                    tg.Start();
                    foreach (var conduit in conduits)
                    {
                        prog.Increment($"Conduit {conduit.Id.Value}");
                        if (EscapeChecker.IsEscapePressed())
                        {
                            cancelled = true;
                            break;
                        }
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
                prog.Close();

                string suffix = "";
                if (skipped > 0) suffix += $" {skipped} already annotated (skipped).";
                if (failed > 0)  suffix += $" {failed} failed.";
                if (cancelled)   suffix += " Cancelled by user.";
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
        private static void StampHomeRun(Element el)
        {
            if (el == null) return;
            try {
                var p = el.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (p != null && !p.IsReadOnly) p.Set("STING_HOME_RUN");
            }
            catch { }
        }

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

                double arrowShaftFt = 150.0 / WireAnnotationEngine.MmPerFt;
                double headLenFt    = 30.0  / WireAnnotationEngine.MmPerFt;
                var arrowTip = arrowBase + arrowDir * arrowShaftFt;

                using (var t = new Transaction(doc, "STING Home Run Arrow"))
                {
                    t.Start();

                    FamilySymbol sym = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfCategory(BuiltInCategory.OST_GenericAnnotation)
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
                            var shaft = doc.Create.NewDetailCurve(view,
                                Line.CreateBound(arrowBase, arrowTip));
                            StampHomeRun(shaft);
                            double ang = Math.PI / 12.0;
                            var legBase = -arrowDir * headLenFt;
                            var leftLeg  = RotateAroundAxis(legBase, XYZ.BasisZ,  ang);
                            var rightLeg = RotateAroundAxis(legBase, XYZ.BasisZ, -ang);
                            var legL = doc.Create.NewDetailCurve(view,
                                Line.CreateBound(arrowTip, arrowTip + leftLeg));
                            StampHomeRun(legL);
                            var legR = doc.Create.NewDetailCurve(view,
                                Line.CreateBound(arrowTip, arrowTip + rightLeg));
                            StampHomeRun(legR);
                        }
                        catch (Exception ex)
                        { StingLog.Warn("HomeRun primitive draw: " + ex.Message); }
                    }

                    string label = !string.IsNullOrEmpty(data.CircuitNumber) ? data.CircuitNumber
                                 : !string.IsNullOrEmpty(data.PanelName)     ? data.PanelName
                                 : "HR";
                    var labelPt = arrowTip + perpDir * (50.0 / WireAnnotationEngine.MmPerFt);
                    var tnt = new FilteredElementCollector(doc)
                        .OfClass(typeof(TextNoteType))
                        .Cast<TextNoteType>()
                        .FirstOrDefault();
                    try
                    {
                        var lbl = TextNote.Create(doc, view.Id, labelPt, label,
                            tnt?.Id ?? ElementId.InvalidElementId);
                        StampHomeRun(lbl);
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

                var homeRunCurves = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(CurveElement))
                    .Cast<CurveElement>()
                    .Where(c => {
                        try {
                            var p = c.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                            return string.Equals(p?.AsString(), "STING_HOME_RUN", StringComparison.Ordinal);
                        } catch { return false; }
                    })
                    .ToList();
                var homeRunNotes = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(TextNote))
                    .Cast<TextNote>()
                    .Where(n => {
                        try {
                            var p = n.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                            return string.Equals(p?.AsString(), "STING_HOME_RUN", StringComparison.Ordinal);
                        } catch { return false; }
                    })
                    .ToList();

                if (notes.Count == 0 && indyTags.Count == 0 && ticks.Count == 0
                    && homeRunCurves.Count == 0 && homeRunNotes.Count == 0)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        "No STING wire annotations found in active view.");
                    return Result.Cancelled;
                }

                int annotCount = notes.Count + indyTags.Count;
                int homeRunCount = homeRunCurves.Count + homeRunNotes.Count;
                var td = new TaskDialog("STING Wire Annotation")
                {
                    MainInstruction = $"Delete {annotCount} annotations, {ticks.Count} tick marks, and {homeRunCount} home-run elements?",
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
                    foreach (var c in homeRunCurves)
                    {
                        try { doc.Delete(c.Id); removed++; }
                        catch (Exception ex) { StingLog.Warn("Clear home-run curve: " + ex.Message); }
                    }
                    foreach (var n in homeRunNotes)
                    {
                        try { doc.Delete(n.Id); removed++; }
                        catch (Exception ex) { StingLog.Warn("Clear home-run note: " + ex.Message); }
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
            e?.Category?.Id.Value == (long)BuiltInCategory.OST_Conduit;
        public bool AllowReference(Reference r, XYZ p) => true;
    }

    /// <summary>
    /// Places home-run arrows for ALL conduits in the active view that have
    /// wire annotations but no home-run arrow yet.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HomeRunArrowBatchCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc  = ctx.Doc;
            var view = doc.ActiveView;
            if (view == null) { message = "No active view."; return Result.Failed; }

            var conduits = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Conduit)
                .WhereElementIsNotElementType()
                .ToList();

            int placed = 0, skipped = 0;
            using (var tx = new Transaction(doc, "STING Batch Home-Run Arrows"))
            {
                tx.Start();
                foreach (var conduit in conduits)
                {
                    try
                    {
                        // Check if already has a home-run annotation for this conduit
                        string homeRunPrefix = "STING_WIRE_HOMERUN|" + conduit.UniqueId;
                        bool existing = new FilteredElementCollector(doc, view.Id)
                            .OfClass(typeof(IndependentTag))
                            .Cast<Element>()
                            .Concat(new FilteredElementCollector(doc, view.Id)
                                .OfClass(typeof(CurveElement))
                                .Cast<Element>())
                            .Concat(new FilteredElementCollector(doc, view.Id)
                                .OfClass(typeof(TextNote))
                                .Cast<Element>())
                            .Any(e => {
                                try {
                                    var p = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                    return (p?.AsString() ?? "").StartsWith(homeRunPrefix, StringComparison.Ordinal);
                                } catch { return false; }
                            });

                        if (existing) { skipped++; continue; }

                        WireAnnotationEngine.PlaceHomeRunArrow(doc, view, conduit);
                        placed++;
                    }
                    catch (Exception ex) { StingLog.Warn($"HomeRunBatch {conduit.Id.Value}: {ex.Message}"); }
                }
                tx.Commit();
            }

            TaskDialog.Show("STING Home-Run Arrows",
                $"Placed: {placed}  |  Already present: {skipped}  |  Total conduits: {conduits.Count}");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Detects and refreshes stale wire annotations in the active view.
    /// Uses WireAnnotationDriftDetector to compare annotation text against
    /// current conduit parameters and replaces any that have drifted.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RefreshWireAnnotationsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc  = ctx.Doc;
            var view = doc.ActiveView;
            if (view == null) { message = "No active view."; return Result.Failed; }

            // Detect drift first (read-only)
            WireAnnotationDriftReport report;
            try
            {
                report = WireAnnotationDriftDetector.Detect(doc, view);
            }
            catch (Exception ex)
            {
                message = $"Drift detection failed: {ex.Message}";
                return Result.Failed;
            }

            if (report.Drifted == 0)
            {
                TaskDialog.Show("STING Wire Annotations",
                    $"All {report.Checked} annotation(s) are current. Nothing to refresh.");
                return Result.Succeeded;
            }

            var dlg = new TaskDialog("STING Wire Annotations")
            {
                MainContent     = report.Summary + "\nRefresh stale annotations now?",
                CommonButtons   = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };
            if (dlg.Show() == TaskDialogResult.No) return Result.Cancelled;

            int refreshed = 0;
            using (var tx = new Transaction(doc, "STING Refresh Wire Annotations"))
            {
                tx.Start();
                try { refreshed = WireAnnotationDriftDetector.RefreshDrifted(doc, view, report); }
                catch (Exception ex) { StingLog.Warn($"RefreshDrifted: {ex.Message}"); }
                tx.Commit();
            }

            TaskDialog.Show("STING Wire Annotations", $"Refreshed {refreshed} annotation(s).");
            return Result.Succeeded;
        }
    }
}
