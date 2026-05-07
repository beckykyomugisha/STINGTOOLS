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
        double FillPct
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
            // Canonical resolution via ParamRegistry: ELC_CIRCUIT_NR aliases
            // to ELC_CKT_NR; ELC_PNL_NAME aliases to ELC_PNL_DESIGNATION_NAME_TXT.
            // (Phase 188 fix — earlier literals didn't match MR_PARAMETERS.txt.)
            string circ   = ParameterHelpers.GetString(conduit, ParamRegistry.ELC_CIRCUIT_NR);
            string panel  = ParameterHelpers.GetString(conduit, ParamRegistry.ELC_PNL_NAME);

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
                var p = conduit.LookupParameter(ParamRegistry.ELC_CKT_VD_PCT);
                if (p == null) {
                    double.TryParse(ParameterHelpers.GetString(conduit, ParamRegistry.ELC_CKT_VD_PCT),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out vd);
                } else if (p.StorageType == StorageType.Double) {
                    vd = p.AsDouble();
                } else if (p.StorageType == StorageType.String) {
                    double.TryParse(p.AsString(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out vd);
                }
            } catch { }

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

            // Prefer live circuit data when the conduit network reaches a
            // circuited device. ricaun-style: the wire spec should follow
            // the actual electrical model, not a separately-maintained
            // shared-parameter snapshot. Fall back to ELC_WIRE_* params
            // when no circuit is reachable.
            try
            {
                var circuit = GetConnectedCircuit(conduit);
                if (circuit != null)
                {
                    string circuitPanel = "";
                    string circuitNumber = "";
                    int circuitPoles = 0;
                    double voltage = 0.0;
                    try { circuitPanel = circuit.PanelName ?? ""; } catch { }
                    try { circuitNumber = circuit.CircuitNumber ?? ""; } catch { }
                    try { circuitPoles = circuit.PolesNumber; } catch { }
                    try { voltage = circuit.Voltage; } catch { }

                    if (!string.IsNullOrEmpty(circuitPanel))  panel = circuitPanel;
                    if (!string.IsNullOrEmpty(circuitNumber)) circ  = circuitNumber;
                    if (circuitPoles > 0 && cores <= 0)       cores = circuitPoles;
                    if (string.IsNullOrEmpty(phase) && circuitPoles > 0)
                    {
                        phase = circuitPoles == 1 ? "1P"
                              : circuitPoles == 2 ? "1P+N"
                              : circuitPoles == 3 ? "3P"
                              : circuitPoles == 4 ? "3P+N"
                              : phase;
                    }
                    // Prepend voltage when the phase string doesn't already
                    // carry a voltage marker. Revit stores Voltage in volts.
                    if (voltage > 0 && (string.IsNullOrEmpty(phase) || !phase.Contains("V")))
                    {
                        int vRound = (int)Math.Round(voltage);
                        phase = string.IsNullOrEmpty(phase)
                            ? vRound + "V"
                            : vRound + "V " + phase;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn("ReadWireData circuit: " + ex.Message); }

            // Cable-type override: STING_CABLE_TYPE_TXT (e.g. "T&E", "3C+E",
            // "4C+E", "SWA"). Wins over circuit poles when set, since the
            // installed cable type dictates conductor count, not the
            // electrical model's pole count (a 2-way switch strapper
            // segment between two SPDTs on a 1-pole circuit needs 3 cores).
            try
            {
                string cableType = ParameterHelpers.GetString(conduit, "STING_CABLE_TYPE_TXT");
                if (!string.IsNullOrEmpty(cableType))
                {
                    int parsedCores = ParseCableTypeCores(cableType);
                    if (parsedCores > 0) cores = parsedCores;
                }
            }
            catch (Exception ex) { StingLog.Warn("ReadWireData cable type: " + ex.Message); }

            return new WireAnnotationData(
                phase, cores, csa,
                string.IsNullOrEmpty(mat) ? "" : mat,
                circ, panel, vd, diaMm, fill);
        }

        public static string BuildAnnotationText(WireAnnotationData d) =>
            BuildAnnotationText(d, WireRole.Unknown);

        public static string BuildAnnotationText(WireAnnotationData d, WireRole role)
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
            if (extras.Count > 0)
                result += "  |  " + string.Join("  |  ", extras);
            if (d.VoltDropPct > 3.0)
                result += $"  ** VD={d.VoltDropPct:0.0}%";
            // Suffix the path role for non-default roles. LoadDrop is the
            // most common case so we don't tag it (annotation stays clean);
            // strapper/feeder/switched-live/ring get an explicit marker so
            // electricians can spot them at a glance.
            if (role != WireRole.Unknown && role != WireRole.LoadDrop)
                result += $"  [{role}]";
            return result;
        }

        public static ElementId PlaceAnnotation(Document doc, View view,
            Element conduit, WireAnnotationData data, bool addTickMarks) =>
            PlaceAnnotation(doc, view, conduit, data, addTickMarks, WireRole.Unknown);

        public static ElementId PlaceAnnotation(Document doc, View view,
            Element conduit, WireAnnotationData data, bool addTickMarks, WireRole role)
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

            string text = BuildAnnotationText(data, role);
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

        private static ConnectorManager GetMepConnectorManager(Element el)
        {
            try
            {
                if (el is MEPCurve mc) return mc.ConnectorManager;
                if (el is FamilyInstance fi) return fi.MEPModel?.ConnectorManager;
            }
            catch { }
            return null;
        }

        private static bool IsElectricalDevice(Element el)
        {
            if (!(el is FamilyInstance fi) || fi.MEPModel == null) return false;
            var catId = fi.Category?.Id?.Value ?? 0;
            return catId == (long)BuiltInCategory.OST_ElectricalEquipment
                || catId == (long)BuiltInCategory.OST_ElectricalFixtures
                || catId == (long)BuiltInCategory.OST_LightingFixtures
                || catId == (long)BuiltInCategory.OST_LightingDevices
                || catId == (long)BuiltInCategory.OST_DataDevices
                || catId == (long)BuiltInCategory.OST_CommunicationDevices
                || catId == (long)BuiltInCategory.OST_FireAlarmDevices
                || catId == (long)BuiltInCategory.OST_NurseCallDevices
                || catId == (long)BuiltInCategory.OST_SecurityDevices
                || catId == (long)BuiltInCategory.OST_TelephoneDevices;
        }

        // Switches/dimmers/sensors live in OST_LightingDevices. In LoadOnly
        // walk mode they're treated as pass-through fittings so the DFS
        // can reach the actual fixture beyond a 2-way / intermediate
        // switching chain.
        private static bool IsSwitch(Element el) =>
            el?.Category?.Id?.Value == (long)BuiltInCategory.OST_LightingDevices;

        private static bool IsSourceEquipment(Element el) =>
            el?.Category?.Id?.Value == (long)BuiltInCategory.OST_ElectricalEquipment;

        private static bool IsLoad(Element el)
        {
            var catId = el?.Category?.Id?.Value ?? 0;
            return catId == (long)BuiltInCategory.OST_LightingFixtures
                || catId == (long)BuiltInCategory.OST_ElectricalFixtures
                || catId == (long)BuiltInCategory.OST_DataDevices
                || catId == (long)BuiltInCategory.OST_CommunicationDevices
                || catId == (long)BuiltInCategory.OST_FireAlarmDevices
                || catId == (long)BuiltInCategory.OST_NurseCallDevices
                || catId == (long)BuiltInCategory.OST_SecurityDevices
                || catId == (long)BuiltInCategory.OST_TelephoneDevices;
        }

        public enum WalkMode
        {
            StopAtAnyDevice,
            StopAtLoadOnly,
        }

        public enum WireRole
        {
            Unknown,
            Feeder,        // source equipment → switch
            Strapper,      // switch ↔ switch (BS 7671 strappers / US travelers)
            SwitchedLive,  // last switch → fixture (the switch drop)
            LoadDrop,      // source equipment → fixture (no intermediate switch)
            Ring,          // ring final circuit (panel ↔ panel)
        }

        public static WireRole ClassifyRole(Element startEl, Element endEl)
        {
            if (startEl == null || endEl == null) return WireRole.Unknown;
            if (startEl.Id == endEl.Id) return WireRole.Ring;
            bool sSw = IsSwitch(startEl), eSw = IsSwitch(endEl);
            bool sSrc = IsSourceEquipment(startEl), eSrc = IsSourceEquipment(endEl);
            bool sLd = IsLoad(startEl), eLd = IsLoad(endEl);
            if (sSw && eSw) return WireRole.Strapper;
            if ((sSrc && eSw) || (sSw && eSrc)) return WireRole.Feeder;
            if ((sSw && eLd) || (sLd && eSw)) return WireRole.SwitchedLive;
            if ((sSrc && eLd) || (sLd && eSrc)) return WireRole.LoadDrop;
            return WireRole.Unknown;
        }

        public static WireRole ClassifyConduitRole(Element conduit)
        {
            if (conduit == null) return WireRole.Unknown;
            var lc = conduit.Location as LocationCurve;
            if (lc?.Curve == null) return WireRole.Unknown;
            var endA = FindFirstDeviceOnSide(conduit, lc.Curve.GetEndPoint(0));
            var endB = FindFirstDeviceOnSide(conduit, lc.Curve.GetEndPoint(1));
            return ClassifyRole(endA, endB);
        }

        private static Element FindFirstDeviceOnSide(Element start, XYZ startPt)
        {
            if (start == null || startPt == null) return null;
            var visited = new HashSet<long> { start.Id.Value };
            Element current = start;
            XYZ currentPt = startPt;
            int safety = 50;
            while (safety-- > 0)
            {
                var cm = GetMepConnectorManager(current);
                if (cm == null) return null;
                Connector outConn = null;
                try
                {
                    foreach (Connector c in cm.Connectors)
                    {
                        if (c.ConnectorType != ConnectorType.End) continue;
                        if (c.Origin != null && c.Origin.IsAlmostEqualTo(currentPt, 1e-3))
                        { outConn = c; break; }
                    }
                }
                catch { return null; }
                if (outConn == null || !outConn.IsConnected) return null;
                Connector partner = null;
                try
                {
                    foreach (Connector other in outConn.AllRefs)
                    {
                        if (other?.Owner == null) continue;
                        if (other.Owner.Id == current.Id) continue;
                        partner = other; break;
                    }
                }
                catch { return null; }
                if (partner == null) return null;
                var nextEl = partner.Owner;
                if (!visited.Add(nextEl.Id.Value)) return null;
                if (IsElectricalDevice(nextEl)) return nextEl;
                var catId = nextEl.Category?.Id?.Value ?? 0;
                if (catId != (long)BuiltInCategory.OST_Conduit
                 && catId != (long)BuiltInCategory.OST_ConduitFitting) return null;
                var nextCm = GetMepConnectorManager(nextEl);
                if (nextCm == null) return null;
                Connector exit = null;
                try
                {
                    foreach (Connector c in nextCm.Connectors)
                    {
                        if (c.ConnectorType != ConnectorType.End) continue;
                        if (c.Id == partner.Id) continue;
                        exit = c; break;
                    }
                }
                catch { return null; }
                if (exit == null || exit.Origin == null) return null;
                current = nextEl;
                currentPt = exit.Origin;
            }
            return null;
        }

        // Maps STING_CABLE_TYPE_TXT to a core count. Returns 0 when no
        // match — caller falls back to circuit poles or shared-param value.
        // Recognises UK BS-style cable codes: 2C+E / T&E / Twin&Earth /
        // 3C+E / 4C+E / 5C+E (with or without spaces / dashes).
        private static int ParseCableTypeCores(string cableType)
        {
            if (string.IsNullOrEmpty(cableType)) return 0;
            string s = cableType.ToUpperInvariant().Replace(" ", "").Replace("-", "");
            if (s.Contains("T&E") || s.Contains("TWIN&EARTH") || s.StartsWith("2C")) return 2;
            if (s.StartsWith("3C")) return 3;
            if (s.StartsWith("4C")) return 4;
            if (s.StartsWith("5C")) return 5;
            return 0;
        }

        public static ElectricalSystem GetConnectedCircuit(Element conduit)
        {
            if (conduit == null) return null;
            var cm = GetMepConnectorManager(conduit);
            if (cm == null) return null;

            var visited = new HashSet<long> { conduit.Id.Value };
            var frontier = new List<Connector>();
            try
            {
                foreach (Connector c in cm.Connectors)
                    if (c.ConnectorType == ConnectorType.End) frontier.Add(c);
            }
            catch { return null; }

            const int maxDepth = 12;
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
                        if (owner == null) continue;
                        long oid = owner.Id.Value;
                        if (!visited.Add(oid)) continue;

                        if (IsElectricalDevice(owner))
                        {
                            try
                            {
                                var systems = ((FamilyInstance)owner).MEPModel.GetElectricalSystems();
                                if (systems != null && systems.Count > 0)
                                    return systems.FirstOrDefault();
                            }
                            catch { }
                            continue;
                        }

                        var catId = owner.Category?.Id?.Value ?? 0;
                        if (catId != (long)BuiltInCategory.OST_Conduit
                         && catId != (long)BuiltInCategory.OST_ConduitFitting)
                            continue;

                        var ocm = GetMepConnectorManager(owner);
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
                        catch { }
                    }
                }
                frontier = next;
            }
            return null;
        }

        public class WirePathResult
        {
            public List<XYZ> Vertices { get; set; } = new List<XYZ>();
            public Connector StartConnector { get; set; }
            public Connector EndConnector { get; set; }
            public WireRole Role { get; set; } = WireRole.Unknown;
            // Conduit + conduit-fitting element IDs traversed by this path
            // (excluding the device endpoints). Used by ring-circuit dedup
            // to differentiate the two legs of a closed loop where both
            // ends terminate at the same panel.
            public List<long> TraversedIds { get; set; } = new List<long>();
        }

        // Back-compat: returns first path with default StopAtAnyDevice walk.
        public static WirePathResult BuildWirePath(Element conduit)
        {
            var all = BuildWirePaths(conduit);
            return all.Count > 0 ? all[0] : null;
        }

        public static List<WirePathResult> BuildWirePaths(Element conduit) =>
            BuildWirePaths(conduit, WalkMode.StopAtAnyDevice);

        // Walks both directions through the conduit + conduit-fitting graph
        // with a multi-branch DFS. WalkMode controls termination:
        //   StopAtAnyDevice: stop at any electrical device (per-segment wires).
        //   StopAtLoadOnly:  pass through switches (multi-connector LightingDevices)
        //                    so a 2-way / intermediate-switching chain emits a
        //                    single panel-to-fixture wire.
        // Each result carries a WireRole derived from start/end categories.
        public static List<WirePathResult> BuildWirePaths(Element conduit, WalkMode walkMode)
        {
            var results = new List<WirePathResult>();
            if (conduit == null) return results;
            var lc = conduit.Location as LocationCurve;
            if (lc?.Curve == null) return results;
            var p0 = lc.Curve.GetEndPoint(0);
            var p1 = lc.Curve.GetEndPoint(1);

            var seedVisited = new HashSet<long> { conduit.Id.Value };
            var sideA = WalkSideMulti(conduit, p0, seedVisited, walkMode);
            var sideB = WalkSideMulti(conduit, p1, seedVisited, walkMode);

            const int maxPairs = 64;

            void AddResult(List<XYZ> verts, Connector sc, Connector ec, HashSet<long> traversed)
            {
                var r = new WirePathResult
                {
                    Vertices       = DeDupeConsecutive(verts),
                    StartConnector = sc,
                    EndConnector   = ec,
                    TraversedIds   = traversed.ToList(),
                };
                r.Role = ClassifyRole(sc?.Owner, ec?.Owner);
                results.Add(r);
            }

            HashSet<long> CombinedTraversed(HashSet<long> a, HashSet<long> b)
            {
                var c = new HashSet<long>(a);
                c.UnionWith(b);
                c.Add(conduit.Id.Value);
                return c;
            }

            if (sideA.Count > 0 && sideB.Count > 0)
            {
                foreach (var a in sideA)
                {
                    foreach (var b in sideB)
                    {
                        if (results.Count >= maxPairs) break;
                        var verts = new List<XYZ>(a.points.Count + b.points.Count);
                        for (int i = a.points.Count - 1; i >= 0; i--) verts.Add(a.points[i]);
                        verts.AddRange(b.points);
                        AddResult(verts, a.deviceConn, b.deviceConn,
                            CombinedTraversed(a.traversed, b.traversed));
                    }
                }
            }
            else if (sideA.Count > 0)
            {
                foreach (var a in sideA)
                {
                    if (results.Count >= maxPairs) break;
                    var verts = new List<XYZ>(a.points.Count + 1);
                    for (int i = a.points.Count - 1; i >= 0; i--) verts.Add(a.points[i]);
                    verts.Add(p1);
                    AddResult(verts, a.deviceConn, null,
                        CombinedTraversed(a.traversed, new HashSet<long>()));
                }
            }
            else if (sideB.Count > 0)
            {
                foreach (var b in sideB)
                {
                    if (results.Count >= maxPairs) break;
                    var verts = new List<XYZ>(b.points.Count + 1) { p0 };
                    verts.AddRange(b.points);
                    AddResult(verts, null, b.deviceConn,
                        CombinedTraversed(new HashSet<long>(), b.traversed));
                }
            }
            else
            {
                AddResult(new List<XYZ> { p0, p1 }, null, null,
                    new HashSet<long> { conduit.Id.Value });
            }

            return results;
        }

        private static List<XYZ> DeDupeConsecutive(List<XYZ> verts)
        {
            var clean = new List<XYZ>();
            foreach (var v in verts)
            {
                if (clean.Count == 0 || !clean[clean.Count - 1].IsAlmostEqualTo(v, 1e-3))
                    clean.Add(v);
            }
            return clean;
        }

        private static List<(List<XYZ> points, Connector deviceConn, HashSet<long> traversed)> WalkSideMulti(
            Element startElement, XYZ startPt, HashSet<long> seedVisited, WalkMode walkMode)
        {
            var results = new List<(List<XYZ>, Connector, HashSet<long>)>();
            const int maxResults = 32;
            const int maxDepth   = 200;

            void Recurse(Element current, XYZ currentPt, List<XYZ> acc, HashSet<long> visited, int depth)
            {
                if (results.Count >= maxResults || depth > maxDepth) return;

                var cm = GetMepConnectorManager(current);
                if (cm == null) return;

                Connector outConn = null;
                try
                {
                    foreach (Connector c in cm.Connectors)
                    {
                        if (c.ConnectorType != ConnectorType.End) continue;
                        if (c.Origin != null && c.Origin.IsAlmostEqualTo(currentPt, 1e-3))
                        { outConn = c; break; }
                    }
                }
                catch { return; }
                if (outConn == null || !outConn.IsConnected) return;

                Connector partnerConn = null;
                try
                {
                    foreach (Connector other in outConn.AllRefs)
                    {
                        if (other?.Owner == null) continue;
                        if (other.Owner.Id == current.Id) continue;
                        partnerConn = other;
                        break;
                    }
                }
                catch { return; }
                if (partnerConn == null) return;

                var nextElement = partnerConn.Owner;
                if (visited.Contains(nextElement.Id.Value)) return;

                if (IsElectricalDevice(nextElement))
                {
                    // LoadOnly mode: try to pass through switches that have
                    // additional end connectors (multi-port smart switch /
                    // dimmer / sensor with input + output terminals).
                    if (walkMode == WalkMode.StopAtLoadOnly && IsSwitch(nextElement))
                    {
                        var nxtCm = GetMepConnectorManager(nextElement);
                        if (nxtCm != null)
                        {
                            var swExits = new List<Connector>();
                            try
                            {
                                foreach (Connector c in nxtCm.Connectors)
                                {
                                    if (c.ConnectorType != ConnectorType.End) continue;
                                    if (c.Id == partnerConn.Id) continue;
                                    swExits.Add(c);
                                }
                            }
                            catch { swExits.Clear(); }
                            if (swExits.Count > 0)
                            {
                                var swVisited = new HashSet<long>(visited) { nextElement.Id.Value };
                                foreach (var exit in swExits)
                                {
                                    if (results.Count >= maxResults) break;
                                    if (exit.Origin == null) continue;
                                    var branchAcc = new List<XYZ>(acc) { exit.Origin };
                                    Recurse(nextElement, exit.Origin, branchAcc,
                                        new HashSet<long>(swVisited), depth + 1);
                                }
                                return;
                            }
                        }
                        // No additional connectors — fall through to terminate.
                    }

                    var finalAcc = new List<XYZ>(acc);
                    if (partnerConn.Origin != null) finalAcc.Add(partnerConn.Origin);
                    var finalTraversed = new HashSet<long>(visited);
                    finalTraversed.Remove(startElement.Id.Value); // exclude the picked conduit (added by seed)
                    results.Add((finalAcc, partnerConn, finalTraversed));
                    return;
                }

                var catId = nextElement.Category?.Id?.Value ?? 0;
                if (catId != (long)BuiltInCategory.OST_Conduit
                 && catId != (long)BuiltInCategory.OST_ConduitFitting)
                    return;

                var nextCm = GetMepConnectorManager(nextElement);
                if (nextCm == null) return;

                var exits = new List<Connector>();
                try
                {
                    foreach (Connector c in nextCm.Connectors)
                    {
                        if (c.ConnectorType != ConnectorType.End) continue;
                        if (c.Id == partnerConn.Id) continue;
                        exits.Add(c);
                    }
                }
                catch { return; }
                if (exits.Count == 0) return;

                var visitedDown = new HashSet<long>(visited) { nextElement.Id.Value };

                foreach (var exit in exits)
                {
                    if (results.Count >= maxResults) break;
                    if (exit.Origin == null) continue;
                    var branchAcc = new List<XYZ>(acc) { exit.Origin };
                    Recurse(nextElement, exit.Origin, branchAcc, new HashSet<long>(visitedDown), depth + 1);
                }
            }

            var initialAcc = new List<XYZ> { startPt };
            Recurse(startElement, startPt, initialAcc, new HashSet<long>(seedVisited), 0);
            return results;
        }

        public static WireType ResolveWireType(Document doc)
        {
            if (doc == null) return null;
            try
            {
                var all = new FilteredElementCollector(doc)
                    .OfClass(typeof(WireType))
                    .Cast<WireType>()
                    .ToList();
                if (all.Count == 0) return null;
                var preferred = all.FirstOrDefault(w =>
                    (w.Name ?? "").IndexOf("STING", StringComparison.OrdinalIgnoreCase) >= 0);
                return preferred ?? all[0];
            }
            catch (Exception ex)
            {
                StingLog.Warn("ResolveWireType: " + ex.Message);
                return null;
            }
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

                var role = WireAnnotationEngine.ClassifyConduitRole(conduit);
                using (var t = new Transaction(doc, "STING Place Wire Annotation"))
                {
                    t.Start();
                    WireAnnotationEngine.RemoveAnnotationForConduit(doc, view, conduit);
                    WireAnnotationEngine.PlaceAnnotation(doc, view, conduit, data,
                        addTickMarks: data.CoreCount > 0, role: role);
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
                        var role = WireAnnotationEngine.ClassifyConduitRole(conduit);
                        using (var t = new Transaction(doc, "STING Wire Annot"))
                        {
                            t.Start();
                            try
                            {
                                var id = WireAnnotationEngine.PlaceAnnotation(
                                    doc, view, conduit, data,
                                    addTickMarks: data.CoreCount > 0, role: role);
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

    // Creates real Revit Wire elements following the conduit centerline
    // (ricaun-style). Walks the conduit + conduit-fitting graph from the
    // picked conduit outward in both directions until a circuited device
    // is reached, then mints a Wire.Create call connected to the circuit
    // at both ends. When a device end can't be reached, the wire is still
    // drawn along the partial path but left unconnected on that side.
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WireOnConduitCommand : IExternalCommand
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
                    TaskDialog.Show("STING Wire On Conduit",
                        "Wires must be created in a plan, section, or elevation view.");
                    return Result.Cancelled;
                }

                Reference reference;
                try
                {
                    reference = uidoc.Selection.PickObject(ObjectType.Element,
                        new ConduitSelectionFilter(),
                        "Pick a conduit on the wire run");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                { return Result.Cancelled; }

                var conduit = doc.GetElement(reference);
                var paths = WireAnnotationEngine.BuildWirePaths(conduit);
                if (paths == null || paths.Count == 0)
                {
                    TaskDialog.Show("STING Wire On Conduit",
                        "Could not trace a conduit path from the picked element.");
                    return Result.Cancelled;
                }

                var wireType = WireAnnotationEngine.ResolveWireType(doc);
                if (wireType == null)
                {
                    TaskDialog.Show("STING Wire On Conduit",
                        "No WireType defined in this project. Load or create a wire type first.");
                    return Result.Failed;
                }

                int placed = 0, failed = 0, partial = 0;
                using (var tg = new TransactionGroup(doc, "STING Wires On Conduit"))
                {
                    tg.Start();
                    foreach (var path in paths)
                    {
                        if (path.Vertices == null || path.Vertices.Count < 2) { failed++; continue; }
                        using (var t = new Transaction(doc, "STING Wire"))
                        {
                            t.Start();
                            try
                            {
                                var wire = Wire.Create(doc, wireType.Id, view.Id,
                                    WiringType.Chamfer, path.Vertices,
                                    path.StartConnector, path.EndConnector);
                                if (wire == null) { t.RollBack(); failed++; continue; }
                                t.Commit();
                                placed++;
                                if (path.StartConnector == null || path.EndConnector == null) partial++;
                            }
                            catch (Exception ex)
                            {
                                try { t.RollBack(); } catch { }
                                failed++;
                                StingLog.Warn($"Wire.Create: {ex.Message}");
                            }
                        }
                    }
                    tg.Assimilate();
                }

                string detail = paths.Count > 1
                    ? $"Placed {placed} wires across {paths.Count} branches"
                    : $"Placed {placed} wire";
                if (partial > 0) detail += $" ({partial} unconnected on one or both ends)";
                if (failed > 0)  detail += $", {failed} failed";
                StingLog.Info(detail);
                TaskDialog.Show("STING Wire On Conduit", detail + ".");
                return placed > 0 ? Result.Succeeded : Result.Failed;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("WireOnConduitCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // Logical-wire variant: walks through multi-port switches so a 2-way
    // or intermediate-switching chain emits a single panel-to-fixture
    // wire rather than per-segment wires. Useful when the documentation
    // intent is "show the circuit's logical conductors" rather than
    // "show every cable physically pulled".
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WireOnConduitLogicalCommand : IExternalCommand
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
                    TaskDialog.Show("STING Wire On Conduit (Logical)",
                        "Wires must be created in a plan, section, or elevation view.");
                    return Result.Cancelled;
                }

                Reference reference;
                try
                {
                    reference = uidoc.Selection.PickObject(ObjectType.Element,
                        new ConduitSelectionFilter(),
                        "Pick a conduit on the wire run (logical mode — passes through switches)");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                { return Result.Cancelled; }

                var conduit = doc.GetElement(reference);
                var paths = WireAnnotationEngine.BuildWirePaths(conduit,
                    WireAnnotationEngine.WalkMode.StopAtLoadOnly);
                if (paths == null || paths.Count == 0)
                {
                    TaskDialog.Show("STING Wire On Conduit (Logical)",
                        "Could not trace a logical wire path from the picked element.");
                    return Result.Cancelled;
                }

                var wireType = WireAnnotationEngine.ResolveWireType(doc);
                if (wireType == null)
                {
                    TaskDialog.Show("STING Wire On Conduit (Logical)",
                        "No WireType defined in this project. Load or create a wire type first.");
                    return Result.Failed;
                }

                int placed = 0, failed = 0, partial = 0;
                using (var tg = new TransactionGroup(doc, "STING Logical Wires On Conduit"))
                {
                    tg.Start();
                    foreach (var path in paths)
                    {
                        if (path.Vertices == null || path.Vertices.Count < 2) { failed++; continue; }
                        using (var t = new Transaction(doc, "STING Logical Wire"))
                        {
                            t.Start();
                            try
                            {
                                var wire = Wire.Create(doc, wireType.Id, view.Id,
                                    WiringType.Chamfer, path.Vertices,
                                    path.StartConnector, path.EndConnector);
                                if (wire == null) { t.RollBack(); failed++; continue; }
                                t.Commit();
                                placed++;
                                if (path.StartConnector == null || path.EndConnector == null) partial++;
                            }
                            catch (Exception ex)
                            {
                                try { t.RollBack(); } catch { }
                                failed++;
                                StingLog.Warn($"Logical Wire.Create: {ex.Message}");
                            }
                        }
                    }
                    tg.Assimilate();
                }

                string detail = paths.Count > 1
                    ? $"Placed {placed} logical wires across {paths.Count} branches"
                    : $"Placed {placed} logical wire";
                if (partial > 0) detail += $" ({partial} unconnected on one or both ends)";
                if (failed > 0)  detail += $", {failed} failed";
                StingLog.Info(detail);
                TaskDialog.Show("STING Wire On Conduit (Logical)", detail + ".");
                return placed > 0 ? Result.Succeeded : Result.Failed;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("WireOnConduitLogicalCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WireOnConduitBatchCommand : IExternalCommand
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
                    TaskDialog.Show("STING Wire On Conduit",
                        "Wires must be created in a plan, section, or elevation view.");
                    return Result.Cancelled;
                }

                var conduits = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_Conduit)
                    .WhereElementIsNotElementType()
                    .ToList();
                if (conduits.Count == 0)
                {
                    TaskDialog.Show("STING Wire On Conduit",
                        "No conduits found in active view.");
                    return Result.Cancelled;
                }

                var wireType = WireAnnotationEngine.ResolveWireType(doc);
                if (wireType == null)
                {
                    TaskDialog.Show("STING Wire On Conduit",
                        "No WireType defined in this project. Load or create a wire type first.");
                    return Result.Failed;
                }

                var td = new TaskDialog("STING Wire On Conduit")
                {
                    MainInstruction = $"Found {conduits.Count} conduits. Place wires along all unique runs?",
                    MainContent     = "One Wire per (device, device) pair connected through the network — multi-branch trunks produce one Wire per leaf device. Pairs are deduped so walking from either end yields the same wire once.",
                    CommonButtons   = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton   = TaskDialogResult.Yes,
                };
                if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;

                int placed = 0, skipped = 0, failed = 0;
                bool cancelled = false;
                // Dedupe by (deviceA, deviceB) sorted pair, plus the sorted
                // traversed-conduit set when both ends are the same device
                // (ring final circuits — both legs reach the same panel and
                // would collapse without a path differentiator).
                var placedKeys = new HashSet<string>();
                var prog = StingProgressDialog.Show("STING Wire On Conduit", conduits.Count);
                using (var tg = new TransactionGroup(doc, "STING Batch Wires On Conduit"))
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
                        var paths = WireAnnotationEngine.BuildWirePaths(conduit);
                        if (paths == null || paths.Count == 0) { skipped++; continue; }

                        foreach (var path in paths)
                        {
                            if (path.Vertices == null || path.Vertices.Count < 2
                                || path.StartConnector == null || path.EndConnector == null)
                            { skipped++; continue; }

                            long aId = path.StartConnector.Owner.Id.Value;
                            long bId = path.EndConnector.Owner.Id.Value;
                            string baseKey = aId < bId ? $"{aId}-{bId}" : $"{bId}-{aId}";
                            string fullKey = aId == bId
                                ? baseKey + "|" + string.Join(",", path.TraversedIds.OrderBy(x => x))
                                : baseKey;
                            if (!placedKeys.Add(fullKey)) { skipped++; continue; }

                            using (var t = new Transaction(doc, "STING Wire"))
                            {
                                t.Start();
                                try
                                {
                                    var wire = Wire.Create(doc, wireType.Id, view.Id,
                                        WiringType.Chamfer, path.Vertices,
                                        path.StartConnector, path.EndConnector);
                                    if (wire != null) { placed++; t.Commit(); }
                                    else { failed++; t.RollBack(); }
                                }
                                catch (Exception ex)
                                {
                                    failed++;
                                    StingLog.Warn($"Batch Wire.Create {conduit.Id.Value}: {ex.Message}");
                                    try { t.RollBack(); } catch { }
                                }
                            }
                        }
                    }
                    tg.Assimilate();
                }
                prog.Close();

                string suffix = "";
                if (skipped > 0)   suffix += $" {skipped} skipped (incomplete or duplicate runs).";
                if (failed > 0)    suffix += $" {failed} failed.";
                if (cancelled)     suffix += " Cancelled by user.";
                TaskDialog.Show("STING Wire On Conduit",
                    $"Placed {placed} wires.{suffix}");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("WireOnConduitBatchCommand failed", ex);
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
}
