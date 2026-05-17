// StingTools — Wire Annotation Symbol commands.
//
// Places BS 7671-style wire-spec annotations on conduit runs:
//   text label  : N × CSA mm² Cu | phase | circuit | panel
//   slash marks : short detail lines crossing the conduit at N positions
//   home-run    : arrow from last-outlet end pointing toward source panel
//
// Slash mark appearance (length, spacing, angle, weight, colour, scale) is
// fully controllable via ELC_WIRE_ANNOT_* shared parameters on each conduit,
// or project-wide defaults stored in STING_WIRE_ANNOT_STYLE.json alongside
// the project file. The style command (WireAnnotationStyleCommand) provides
// a dialog to edit project defaults without touching individual conduits.
//
// Family opt-in: if a family named "STING Wire Annotation Tag" (or containing
// "Wire Annotation") is loaded into the project under OST_ConduitTags, STING
// uses IndependentTag instead of TextNote so the label tracks the conduit and
// updates live when parameters change. The .rfa spec is in
// Families/Annotation/STING_WIRE_ANNOTATION_TAG.params.txt.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Electrical;
using StingTools.UI;

namespace StingTools.Commands.Electrical
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Data model
    // ─────────────────────────────────────────────────────────────────────────

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
        double AmpacityA,
        double MaxDemandA,
        string CircuitType,
        string InstallMethod,
        bool   IsArmoured,
        bool   IsFireRated,
        bool   IsShielded,
        int    BendCount    // number of bends on this conduit run (BS 7671 §522.8.5)
    );

    // ─────────────────────────────────────────────────────────────────────────
    //  Style — controls ALL visual properties of the annotation geometry
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Visual appearance of wire annotation slash marks and label.
    /// Values are in real-world mm; the engine converts to Revit feet.
    /// Project defaults live in STING_WIRE_ANNOT_STYLE.json; per-conduit
    /// overrides come from ELC_WIRE_ANNOT_* shared parameters.
    /// </summary>
    public class WireAnnotationStyle
    {
        // ── Slash geometry ───────────────────────────────────────────────────

        /// <summary>Slash mark length in mm (perpendicular to conduit). Default 6 mm.</summary>
        public double SlashLengthMm { get; set; } = 6.0;

        /// <summary>Gap between adjacent slashes along the conduit axis in mm. Default 3 mm.</summary>
        public double SlashSpacingMm { get; set; } = 3.0;

        /// <summary>
        /// Angle of each slash relative to the conduit axis in degrees.
        /// 90° = perpendicular cross-tick; 60° = standard oblique slash (BS 7671 convention).
        /// Valid range: 30–90. Default 60°.
        /// </summary>
        public double SlashAngleDeg { get; set; } = 60.0;

        /// <summary>
        /// Global scale multiplier applied to SlashLengthMm, SlashSpacingMm, and
        /// LabelOffsetMm. Use &lt;1 for congested plans, &gt;1 for large-scale details.
        /// Default 1.0.
        /// </summary>
        public double ScaleFactor { get; set; } = 1.0;

        // ── Slash line style ─────────────────────────────────────────────────

        /// <summary>
        /// Line weight index (1–16, matching Revit line-weight scale).
        /// Default 3 ≈ 0.25 mm printed at 1:100.
        /// </summary>
        public int SlashLineWeight { get; set; } = 3;

        /// <summary>
        /// Colour override code applied to slash marks and label:
        /// 0 = auto/black, 1 = red (overloaded/alarm), 2 = blue (data/neutral),
        /// 3 = orange (review), 4 = green (safe/spare). Default 0.
        /// </summary>
        public int ColorCode { get; set; } = 0;

        // ── Label placement ──────────────────────────────────────────────────

        /// <summary>
        /// Perpendicular offset of the label from the conduit centreline in mm.
        /// Default 600 mm (6 mm at 1:100 print scale).
        /// </summary>
        public double LabelOffsetMm { get; set; } = 600.0;

        // ── Label content flags ──────────────────────────────────────────────

        /// <summary>Show voltage drop only when VD &gt; threshold. Default: show when VD &gt; 3%.</summary>
        public bool ShowVoltDrop    { get; set; } = true;

        /// <summary>Show conduit fill percentage. Default: show when fill &gt; 40%.</summary>
        public bool ShowFill        { get; set; } = true;

        /// <summary>Show conduit diameter.</summary>
        public bool ShowDiameter    { get; set; } = true;

        /// <summary>Show cable ampacity (Iz) and max demand (Ib).</summary>
        public bool ShowAmpacity    { get; set; } = false;

        /// <summary>Compact mode: omit panel name and install method.</summary>
        public bool CompactLabel    { get; set; } = false;

        // ── Threshold helpers ────────────────────────────────────────────────

        /// <summary>Voltage drop percentage above which VD is always shown (BS 7671: 3% lighting, 5% power).</summary>
        public double VdAlarmPct    { get; set; } = 3.0;

        /// <summary>Fill percentage above which fill warning is shown (IET guidance: 40%).</summary>
        public double FillAlarmPct  { get; set; } = 40.0;

        // ── Auto-colour rules ────────────────────────────────────────────────

        /// <summary>
        /// If ColorCode == 0 (auto), STING derives a colour from circuit data:
        /// VD > VdAlarmPct or fill > FillAlarmPct → red;
        /// fire-rated cable → orange;
        /// armoured cable → blue;
        /// else → black.
        /// </summary>
        public bool AutoColor { get; set; } = true;

        // ── Revit line-style name for slash marks ────────────────────────────

        /// <summary>
        /// Revit line style name applied to slash detail lines.
        /// "Wire Tick Marks" is created by TemplateCommands if not present.
        /// Falls back to the default thin line when not found.
        /// </summary>
        public string SlashLineStyleName { get; set; } = "Wire Tick Marks";

        // ── Factory: defaults ────────────────────────────────────────────────

        public static WireAnnotationStyle Default() => new WireAnnotationStyle();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Style store — project-level JSON persistence
    // ─────────────────────────────────────────────────────────────────────────

    internal static class WireAnnotationStyleStore
    {
        private const string FileName = "STING_WIRE_ANNOT_STYLE.json";

        public static WireAnnotationStyle Load(Document doc)
        {
            try
            {
                string path = ResolveStylePath(doc);
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    return JsonConvert.DeserializeObject<WireAnnotationStyle>(json)
                           ?? WireAnnotationStyle.Default();
                }
            }
            catch (Exception ex) { StingLog.Warn("WireAnnotStyleStore.Load: " + ex.Message); }
            return WireAnnotationStyle.Default();
        }

        public static void Save(Document doc, WireAnnotationStyle style)
        {
            try
            {
                string path = ResolveStylePath(doc);
                string dir  = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonConvert.SerializeObject(style, Formatting.Indented));
                StingLog.Info("WireAnnotationStyle saved to: " + path);
            }
            catch (Exception ex) { StingLog.Warn("WireAnnotStyleStore.Save: " + ex.Message); }
        }

        private static string ResolveStylePath(Document doc)
        {
            string projDir = null;
            try
            {
                if (!doc.IsWorkshared && !string.IsNullOrEmpty(doc.PathName))
                    projDir = Path.GetDirectoryName(doc.PathName);
                else if (doc.IsWorkshared)
                    projDir = Path.GetDirectoryName(
                        ModelPathUtils.ConvertModelPathToUserVisiblePath(doc.GetWorksharingCentralModelPath()));
            }
            catch { }

            string coordDir = string.IsNullOrEmpty(projDir)
                ? Path.Combine(Path.GetTempPath(), "STING")
                : Path.Combine(projDir, "_BIM_COORD");

            return Path.Combine(coordDir, FileName);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Per-conduit style override reader
    // ─────────────────────────────────────────────────────────────────────────

    internal static class WireAnnotationStyleOverride
    {
        /// <summary>
        /// Reads ELC_WIRE_ANNOT_* parameters from a conduit and overlays them on
        /// the provided project-level base style. Only non-zero / non-null values
        /// on the conduit override the base — unset params leave the base intact.
        /// </summary>
        public static WireAnnotationStyle Merge(WireAnnotationStyle baseStyle, Element conduit)
        {
            if (conduit == null) return baseStyle;

            // Clone via JSON round-trip so we don't mutate the project default
            var style = JsonConvert.DeserializeObject<WireAnnotationStyle>(
                JsonConvert.SerializeObject(baseStyle)) ?? WireAnnotationStyle.Default();

            TryOverrideDouble(conduit, "ELC_WIRE_ANNOT_SLASH_LEN_MM",    v => style.SlashLengthMm  = v);
            TryOverrideDouble(conduit, "ELC_WIRE_ANNOT_SPACING_MM",       v => style.SlashSpacingMm = v);
            TryOverrideDouble(conduit, "ELC_WIRE_ANNOT_ANGLE_DEG",        v => style.SlashAngleDeg  = Math.Max(30, Math.Min(90, v)));
            TryOverrideDouble(conduit, "ELC_WIRE_ANNOT_OFFSET_MM",        v => style.LabelOffsetMm  = v);
            TryOverrideDouble(conduit, "ELC_WIRE_ANNOT_SCALE_FACTOR",     v => { if (v > 0) style.ScaleFactor = v; });
            TryOverrideInt   (conduit, "ELC_WIRE_ANNOT_LINE_WEIGHT_INT",  v => style.SlashLineWeight = Math.Max(1, Math.Min(16, v)));
            TryOverrideInt   (conduit, "ELC_WIRE_ANNOT_COLOR_CODE_INT",   v => { style.ColorCode = v; style.AutoColor = (v == 0); });
            TryOverrideBool  (conduit, "ELC_WIRE_ANNOT_SHOW_VD_BOOL",     v => style.ShowVoltDrop   = v);
            TryOverrideBool  (conduit, "ELC_WIRE_ANNOT_SHOW_FILL_BOOL",   v => style.ShowFill       = v);
            TryOverrideBool  (conduit, "ELC_WIRE_ANNOT_SHOW_DIA_BOOL",    v => style.ShowDiameter   = v);
            TryOverrideBool  (conduit, "ELC_WIRE_ANNOT_SHOW_AMPACITY_BOOL", v => style.ShowAmpacity = v);
            TryOverrideBool  (conduit, "ELC_WIRE_ANNOT_COMPACT_BOOL",     v => style.CompactLabel   = v);

            return style;
        }

        private static void TryOverrideDouble(Element el, string paramName, Action<double> apply)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null) return;
                double v = p.StorageType == StorageType.Double ? p.AsDouble()
                         : p.StorageType == StorageType.String && double.TryParse(p.AsString(),
                               System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out double sv) ? sv : 0;
                if (v > 0) apply(v);
            }
            catch { }
        }

        private static void TryOverrideInt(Element el, string paramName, Action<int> apply)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null) return;
                int v = p.StorageType == StorageType.Integer ? p.AsInteger()
                      : p.StorageType == StorageType.String  && int.TryParse(p.AsString(), out int sv) ? sv : -1;
                if (v >= 0) apply(v);
            }
            catch { }
        }

        private static void TryOverrideBool(Element el, string paramName, Action<bool> apply)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null) return;
                if (p.StorageType == StorageType.Integer) apply(p.AsInteger() != 0);
            }
            catch { }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Core engine
    // ─────────────────────────────────────────────────────────────────────────

    internal static class WireAnnotationEngine
    {
        internal const double MmPerFt   = 304.8;
        private const string MarkerTxt  = "STING_WIRE_ANNOT";
        private const string TickMarker = "STING_WIRE_TICK";

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
                    .OfClass(typeof(TextNote)).Cast<Element>()
                    .Where(n => MatchesMarker(n, target)));
                result.AddRange(new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag)).Cast<Element>()
                    .Where(t => MatchesMarker(t, target)));
            } catch (Exception ex) { StingLog.Warn("GetAnnotationsForConduit: " + ex.Message); }
            return result;
        }

        public static int RemoveAnnotationForConduit(Document doc, View view, Element conduit)
        {
            int removed = 0;
            foreach (var n in GetAnnotationsForConduit(doc, view, conduit))
                try { doc.Delete(n.Id); removed++; }
                catch (Exception ex) { StingLog.Warn("RemoveAnnot: " + ex.Message); }
            return removed;
        }

        public static bool HasAnnotation(Document doc, View view, Element conduit) =>
            GetAnnotationsForConduit(doc, view, conduit).Count > 0;

        // ── Read conduit parameters ──────────────────────────────────────────

        public static WireAnnotationData ReadWireData(Element conduit)
        {
            string phase   = ParameterHelpers.GetString(conduit, "ELC_WIRE_PHASE_TXT");
            string mat     = ParameterHelpers.GetString(conduit, "ELC_WIRE_COND_MAT_TXT");
            string circ    = ParameterHelpers.GetString(conduit, "ELC_CIRCUIT_NR_TXT");
            string panel   = ParameterHelpers.GetString(conduit, "ELC_PNL_NAME_TXT");
            string circType= ParameterHelpers.GetString(conduit, "ELC_WIRE_CIRCUIT_TYPE_TXT");
            string instMeth= ParameterHelpers.GetString(conduit, "ELC_WIRE_INSTALL_METHOD_TXT");

            int  cores    = ParameterHelpers.GetInt(conduit, "ELC_WIRE_CORE_COUNT_INT", 0);
            bool armoured = ReadBoolParam(conduit, "ELC_WIRE_ARMOURED_BOOL");
            bool fireRated= ReadBoolParam(conduit, "ELC_WIRE_FIRE_RATED_BOOL");
            bool shielded = ReadBoolParam(conduit, "ELC_WIRE_SHIELDED_BOOL");

            double csa = 0, vd = 0, fill2 = 0;
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

            double ampacity  = ReadNumParam(conduit, "ELC_WIRE_AMPACITY_A");
            double maxDemand = ReadNumParam(conduit, "ELC_WIRE_MAX_DEMAND_A");

            double diaMm = 0.0;
            try
            {
                var p = conduit.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
                if (p?.StorageType == StorageType.Double) diaMm = p.AsDouble() * MmPerFt;
            }
            catch (Exception ex) { StingLog.Warn("ReadWireData diameter: " + ex.Message); }

            int bendCount = 0;
            try
            {
                int stored = ParameterHelpers.GetInt(conduit, "ELC_WIRE_BEND_COUNT_INT", 0);
                if (stored > 0) bendCount = stored;
            }
            catch { }

            double fill = fill2 > 0 ? fill2 : ReadNumParam(conduit, "ELC_CDT_CBL_FILL_PCT");

            return new WireAnnotationData(
                phase, cores, csa,
                string.IsNullOrEmpty(mat) ? "Cu" : mat,
                circ, panel, vd, diaMm, fill,
                ampacity, maxDemand, circType, instMeth,
                armoured, fireRated, shielded, bendCount);
        }

        private static double ReadNumParam(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null)
                {
                    double.TryParse(ParameterHelpers.GetString(el, name),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double sv);
                    return sv;
                }
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.String)
                {
                    double.TryParse(p.AsString(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double v);
                    return v;
                }
            }
            catch { }
            return 0.0;
        }

        private static bool ReadBoolParam(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                return p?.StorageType == StorageType.Integer && p.AsInteger() != 0;
            }
            catch { return false; }
        }

        // ── Label text builder ───────────────────────────────────────────────

        public static string BuildAnnotationText(WireAnnotationData d, WireAnnotationStyle style)
        {
            string mat = string.IsNullOrEmpty(d.ConductorMat) ? "Cu" : d.ConductorMat;
            string baseSpec;

            if (d.CoreCount > 0 && d.CsaMm2 > 0)
                baseSpec = $"{d.CoreCount} × {d.CsaMm2:0.##} mm² {mat}";
            else if (d.CsaMm2 > 0)
                baseSpec = $"{d.CsaMm2:0.##} mm² {mat}";
            else
                baseSpec = "? Wire";

            if (d.IsFireRated)  baseSpec += " (FR)";
            if (d.IsArmoured)   baseSpec += " SWA";
            if (d.IsShielded)   baseSpec += " SCR";

            var parts = new List<string> { baseSpec };
            if (!string.IsNullOrEmpty(d.Phase))         parts.Add(d.Phase);
            if (!string.IsNullOrEmpty(d.CircuitNumber)) parts.Add(d.CircuitNumber);

            if (!style.CompactLabel)
            {
                if (!string.IsNullOrEmpty(d.PanelName))     parts.Add(d.PanelName);
                if (!string.IsNullOrEmpty(d.CircuitType))   parts.Add(d.CircuitType);
                if (!string.IsNullOrEmpty(d.InstallMethod)) parts.Add($"Meth.{d.InstallMethod}");
            }

            string result = string.Join("  |  ", parts);
            var extras = new List<string>();

            if (style.ShowDiameter && d.DiameterMm > 0)
                extras.Add($"Ø{d.DiameterMm:0.#} mm");

            if (style.ShowFill && d.FillPct > 0)
            {
                string fillStr = $"Fill {d.FillPct:0.#}%";
                if (d.FillPct > style.FillAlarmPct) fillStr += " ⚠";
                extras.Add(fillStr);
            }

            if (style.ShowAmpacity && d.AmpacityA > 0)
            {
                string ampStr = $"Iz={d.AmpacityA:0.#}A";
                if (d.MaxDemandA > 0) ampStr += $" Ib={d.MaxDemandA:0.#}A";
                extras.Add(ampStr);
            }

            if (d.BendCount > 0)
            {
                string bendStr = $"{d.BendCount}B";
                if (d.BendCount >= 3) bendStr += "⚠"; // at BS 7671 §522.8.5 limit
                extras.Add(bendStr);
            }
            if (extras.Count > 0)
                result += "  |  " + string.Join("  |  ", extras);

            if (style.ShowVoltDrop && d.VoltDropPct > style.VdAlarmPct)
                result += $"  ** VD={d.VoltDropPct:0.0}%";

            return result;
        }

        // ── Colour resolution ────────────────────────────────────────────────

        /// <summary>Returns the Revit Color to use for slash marks and label given style + data.</summary>
        public static Color ResolveColor(WireAnnotationStyle style, WireAnnotationData data)
        {
            int code = style.ColorCode;

            if (style.AutoColor)
            {
                if (data != null)
                {
                    if (data.VoltDropPct > style.VdAlarmPct || data.FillPct > style.FillAlarmPct)
                        code = 1; // red
                    else if (data.IsFireRated)
                        code = 3; // orange
                    else if (data.IsArmoured || data.IsShielded)
                        code = 2; // blue
                }
            }

            return code switch
            {
                1 => new Color(220, 38,  38),   // red
                2 => new Color(37,  99,  235),  // blue
                3 => new Color(234, 88,  12),   // orange
                4 => new Color(22,  163, 74),   // green
                _ => new Color(0,   0,   0),    // black (default)
            };
        }

        // ── Gap 6: view-scale → auto ScaleFactor ────────────────────────────

        /// <summary>
        /// When the conduit has no per-element ELC_WIRE_ANNOT_SCALE_FACTOR set
        /// (i.e. style.ScaleFactor is still the project default of 1.0), auto-derive
        /// a scale multiplier from the active view's annotation scale so slashes and
        /// labels stay legible across plan/detail/site scales.
        ///   1:50  → 0.5×   1:100 → 1.0×   1:200 → 2.0×   1:500 → 5.0×
        /// The formula is simply: multiplier = viewScale / 100.
        /// </summary>
        public static double ViewScaleFactor(View view)
        {
            if (view == null) return 1.0;
            try
            {
                int scale = view.Scale; // e.g. 100 for 1:100
                if (scale > 0) return scale / 100.0;
            }
            catch { }
            return 1.0;
        }

        // ── Gap 10: simple occupied-region registry for collision avoidance ──

        // Keyed on (documentKey, viewId) so the cache never bleeds across documents.
        // Cleared each time a batch run starts via ClearAnnotationBoxes.
        private static readonly Dictionary<(string, long), List<(XYZ min, XYZ max)>> _viewAnnotBoxes
            = new Dictionary<(string, long), List<(XYZ, XYZ)>>();

        private static string DocKey(Document doc)
            => $"{doc?.Title}|{doc?.PathName}";

        public static void ClearAnnotationBoxes(Document doc, ElementId viewId)
        {
            var key = (DocKey(doc), viewId.Value);
            _viewAnnotBoxes.Remove(key);
        }

        private static XYZ FindNonCollidingPoint(Document doc, View view, XYZ preferredPt,
            XYZ perpDir, double offsetFt)
        {
            var key = (DocKey(doc), view.Id.Value);
            if (!_viewAnnotBoxes.TryGetValue(key, out var boxes) || boxes.Count == 0)
                return preferredPt;

            // Try 8 candidate offsets (±1×, ±2× offset, in both perp directions)
            double[] multipliers = { 1, -1, 2, -2, 3, -3, 4, -4 };
            foreach (var m in multipliers)
            {
                var candidate = preferredPt + perpDir * (offsetFt * m);
                double r = offsetFt * 0.4; // rough annotation radius
                bool collides = boxes.Any(b =>
                    candidate.X + r > b.min.X && candidate.X - r < b.max.X &&
                    candidate.Y + r > b.min.Y && candidate.Y - r < b.max.Y);
                if (!collides) return candidate;
            }
            return preferredPt; // give up, use default
        }

        private static void RegisterAnnotationBox(Document doc, ElementId viewId, XYZ pt, double halfSideFt)
        {
            var key = (DocKey(doc), viewId.Value);
            if (!_viewAnnotBoxes.TryGetValue(key, out var boxes))
                _viewAnnotBoxes[key] = boxes = new List<(XYZ, XYZ)>();
            boxes.Add((new XYZ(pt.X - halfSideFt, pt.Y - halfSideFt, pt.Z),
                       new XYZ(pt.X + halfSideFt, pt.Y + halfSideFt, pt.Z)));
        }

        // ── Placement ────────────────────────────────────────────────────────

        public static ElementId PlaceAnnotation(Document doc, View view,
            Element conduit, WireAnnotationData data, WireAnnotationStyle style)
        {
            if (doc == null || view == null || conduit == null)
                return ElementId.InvalidElementId;
            var lc = conduit.Location as LocationCurve;
            if (lc?.Curve == null) return ElementId.InvalidElementId;

            // Gap 6: if no per-conduit scale override, apply view-scale auto-factor
            // (only when style came directly from project defaults ScaleFactor == 1.0)
            var effectiveStyle = style;
            bool hasConduitOverride = conduit.LookupParameter("ELC_WIRE_ANNOT_SCALE_FACTOR")
                ?.AsDouble() > 0;
            if (!hasConduitOverride && Math.Abs(style.ScaleFactor - 1.0) < 1e-6)
            {
                double vsf = ViewScaleFactor(view);
                if (Math.Abs(vsf - 1.0) > 0.05)
                {
                    // Clone via JSON to avoid mutating caller's style object
                    effectiveStyle = JsonConvert.DeserializeObject<WireAnnotationStyle>(
                        JsonConvert.SerializeObject(style)) ?? WireAnnotationStyle.Default();
                    effectiveStyle.ScaleFactor = vsf;
                }
            }
            style = effectiveStyle;

            var curve   = lc.Curve;
            var p0      = curve.GetEndPoint(0);
            var p1      = curve.GetEndPoint(1);
            var mid     = (p0 + p1) * 0.5;
            var axisRaw = p1 - p0;
            if (axisRaw.GetLength() < 1e-6) return ElementId.InvalidElementId;
            var axisDir = axisRaw.Normalize();

            var perpRaw = XYZ.BasisZ.CrossProduct(axisDir);
            var perpDir = perpRaw.GetLength() < 1e-6 ? XYZ.BasisX : perpRaw.Normalize();

            double offsetFt = (style.LabelOffsetMm * style.ScaleFactor) / MmPerFt;
            // Gap 10: collision-aware label placement
            var preferredPt = mid + perpDir * offsetFt;
            var annotPt = FindNonCollidingPoint(doc, view, preferredPt, perpDir, offsetFt);
            RegisterAnnotationBox(doc, view.Id, annotPt, offsetFt * 0.5);

            // Prefer IndependentTag (tracks conduit, labels auto-update)
            var tagId = TryPlaceIndependentTag(doc, view, conduit, annotPt, conduit.UniqueId);
            if (tagId != ElementId.InvalidElementId)
            {
                PlaceSlashMarks(doc, view, conduit, data.CoreCount, style, data);
                return tagId;
            }

            // Fallback: TextNote + leader
            TextNoteType tnt   = ResolveTextNoteType(doc);
            ElementId    tntId = tnt?.Id ?? ElementId.InvalidElementId;
            string text        = BuildAnnotationText(data, style);

            TextNote note;
            try { note = TextNote.Create(doc, view.Id, annotPt, text, tntId); }
            catch (Exception ex)
            {
                StingLog.Warn($"WireAnnotation TextNote.Create: {ex.Message}");
                return ElementId.InvalidElementId;
            }

            MarkAsWireAnnotation(note, conduit.UniqueId);
            PlaceLeader(doc, view, mid, annotPt, perpDir);
            PlaceSlashMarks(doc, view, conduit, data.CoreCount, style, data);

            return note.Id;
        }

        // ── Slash marks ───────────────────────────────────────────────────────

        /// <summary>
        /// Places N slash detail lines crossing the conduit.
        /// Count is clamped to 1–4. All geometry (length, spacing, angle, weight,
        /// colour, line style) comes from <paramref name="style"/>.
        /// </summary>
        public static void PlaceSlashMarks(Document doc, View view, Element conduit,
            int coreCount, WireAnnotationStyle style, WireAnnotationData data = null)
        {
            if (doc == null || view == null || conduit == null) return;
            var lc = conduit.Location as LocationCurve;
            if (lc?.Curve == null) return;

            int slashCount = Math.Max(0, Math.Min(4, coreCount));
            if (slashCount == 0) return;

            double scale   = Math.Max(0.01, style.ScaleFactor);
            double lenFt   = (style.SlashLengthMm  * scale) / MmPerFt;
            double gapFt   = (style.SlashSpacingMm * scale) / MmPerFt;
            double angleRad= (style.SlashAngleDeg  * Math.PI) / 180.0;

            var curve   = lc.Curve;
            var p0      = curve.GetEndPoint(0);
            var p1      = curve.GetEndPoint(1);
            var axisRaw = p1 - p0;
            double runLen = axisRaw.GetLength();
            if (runLen < 1e-6) return;
            var axisDir = axisRaw.Normalize();

            // Perpendicular direction in the view plane
            var perpRaw = XYZ.BasisZ.CrossProduct(axisDir);
            var perpDir = perpRaw.GetLength() < 1e-6 ? XYZ.BasisX : perpRaw.Normalize();

            // Cluster all slashes at mid-point of conduit run
            double clusterLen = gapFt * (slashCount - 1);
            double startPos   = (runLen - clusterLen) / 2.0;

            // Resolve line style and colour
            GraphicsStyle lineStyle = ResolveLineStyle(doc, style.SlashLineStyleName);
            Color slashColor        = ResolveColor(style, data);

            for (int i = 0; i < slashCount; i++)
            {
                double pos = startPos + gapFt * i;
                var   mid  = p0 + axisDir * pos;

                // Rotate the perpendicular vector by SlashAngleDeg off the axis
                // so at 90° it's perpendicular, at 60° it's an oblique slash
                var rotPerp = RotateInPlane(perpDir, axisDir, 90.0 - style.SlashAngleDeg);

                var startPt = mid - rotPerp * (lenFt * 0.5);
                var endPt   = mid + rotPerp * (lenFt * 0.5);

                try
                {
                    var dc = doc.Create.NewDetailCurve(view, Line.CreateBound(startPt, endPt));
                    if (dc == null) continue;

                    StampTickMarker(dc);

                    // Line style
                    if (lineStyle != null)
                        try { dc.LineStyle = lineStyle; } catch { }

                    // Line weight via override graphics on this specific element
                    ApplySlashOverride(doc, view, dc.Id, slashColor, style.SlashLineWeight);
                }
                catch (Exception ex) { StingLog.Warn($"Slash mark {i}: {ex.Message}"); }
            }
        }

        private static void ApplySlashOverride(Document doc, View view, ElementId elemId,
            Color color, int lineWeight)
        {
            try
            {
                var ogs = new OverrideGraphicSettings();
                ogs.SetProjectionLineColor(color);
                ogs.SetProjectionLineWeight(Math.Max(1, Math.Min(16, lineWeight)));
                view.SetElementOverrides(elemId, ogs);
            }
            catch (Exception ex) { StingLog.Warn("ApplySlashOverride: " + ex.Message); }
        }

        private static XYZ RotateInPlane(XYZ vec, XYZ normal, double degrees)
        {
            double rad = degrees * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            return vec * cos + normal.CrossProduct(vec) * sin
                 + normal * normal.DotProduct(vec) * (1 - cos);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void PlaceLeader(Document doc, View view,
            XYZ conduitMid, XYZ annotPt, XYZ perpDir)
        {
            if (doc == null || view == null) return;
            try
            {
                double offsetFt = 50.0 / MmPerFt;
                var leaderEnd = annotPt - perpDir * offsetFt;
                var dc = doc.Create.NewDetailCurve(view, Line.CreateBound(conduitMid, leaderEnd));
                StampTickMarker(dc);
            }
            catch (Exception ex) { StingLog.Warn("Wire annot leader: " + ex.Message); }
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
            catch { }
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

        public static bool IsSlashMark(CurveElement ce)
        {
            if (ce == null) return false;
            try
            {
                var p = ce.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (string.Equals(p?.AsString(), TickMarker, StringComparison.Ordinal)) return true;
                var gs = ce.LineStyle as GraphicsStyle;
                return string.Equals(gs?.Name, "Wire Tick Marks", StringComparison.Ordinal)
                    || string.Equals(gs?.GraphicsStyleCategory?.Name, "Wire Tick Marks", StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private static TextNoteType ResolveTextNoteType(Document doc)
        {
            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType)).Cast<TextNoteType>().ToList();
            if (all.Count == 0) return null;

            var named = all.FirstOrDefault(t =>
                string.Equals(t.Name, "STING Wire Annotation", StringComparison.OrdinalIgnoreCase));
            if (named != null) return named;

            const double minSizeFt = 2.5 / 304.8;
            var candidates = all
                .Select(t => { var p = t.get_Parameter(BuiltInParameter.TEXT_SIZE);
                               return (type: t, size: p?.AsDouble() ?? 0.0); })
                .Where(x => x.size >= minSizeFt)
                .OrderBy(x => x.size).ToList();

            return candidates.Count > 0 ? candidates[0].type
                 : all.OrderBy(t => { var p = t.get_Parameter(BuiltInParameter.TEXT_SIZE);
                                      return p?.AsDouble() ?? double.MaxValue; }).First();
        }

        private static GraphicsStyle ResolveLineStyle(Document doc, string styleName)
        {
            try
            {
                var cat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                if (cat?.SubCategories == null) return null;
                foreach (Category sub in cat.SubCategories)
                {
                    if (string.Equals(sub.Name, styleName, StringComparison.OrdinalIgnoreCase))
                        return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
                }
            }
            catch (Exception ex) { StingLog.Warn("ResolveLineStyle: " + ex.Message); }
            return null;
        }

        public static FamilySymbol ResolveOptInWireTagSymbol(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_ConduitTags)
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s =>
                        (s.FamilyName ?? "").IndexOf("Wire Annotation", StringComparison.OrdinalIgnoreCase) >= 0
                     || (s.FamilyName ?? "").IndexOf("STING Wire",      StringComparison.OrdinalIgnoreCase) >= 0
                     || (s.Name       ?? "").IndexOf("Wire Annotation", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch (Exception ex) { StingLog.Warn("ResolveOptInWireTagSymbol: " + ex.Message); return null; }
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
            catch (Exception ex) { StingLog.Warn("IndependentTag.Create: " + ex.Message); return ElementId.InvalidElementId; }
        }

        // ── Panel-side end detection ─────────────────────────────────────────

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

            var visited  = new HashSet<long>();
            var frontier = new List<Connector> { startConn };
            for (int depth = 0; depth < 4 && frontier.Count > 0; depth++)
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
                        if (!visited.Add(oid)) continue;
                        var catId = owner.Category?.Id?.Value ?? 0;
                        if (catId == (long)BuiltInCategory.OST_ElectricalEquipment) return true;
                        if (catId == (long)BuiltInCategory.OST_ConduitFitting
                         || catId == (long)BuiltInCategory.OST_Conduit
                         || catId == (long)BuiltInCategory.OST_CableTray
                         || catId == (long)BuiltInCategory.OST_CableTrayFitting)
                        {
                            ConnectorManager ocm = null;
                            try { if (owner is MEPCurve omc) ocm = omc.ConnectorManager;
                                  else if (owner is FamilyInstance ofi) ocm = ofi.MEPModel?.ConnectorManager; }
                            catch { ocm = null; }
                            if (ocm == null) continue;
                            try { foreach (Connector pc in ocm.Connectors)
                                  { if (pc.ConnectorType != ConnectorType.End || pc.Id == other.Id) continue;
                                    next.Add(pc); } }
                            catch { }
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
>>>>>>> origin/claude/claude-md-mm3e3rr0h3nqaf6c-hAOJn
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Commands
    // ─────────────────────────────────────────────────────────────────────────

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
                var doc = ctx.Doc; var uidoc = ctx.UIDoc; var view = doc.ActiveView;
                if (view is View3D || view is ViewSchedule)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        "Wire annotations require a plan, section, or elevation view.");
                    return Result.Cancelled;
                }

                Reference reference;
                try { reference = uidoc.Selection.PickObject(ObjectType.Element,
                          new ConduitSelectionFilter(), "Pick a conduit to annotate"); }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

                var conduit = doc.GetElement(reference);
                var data    = WireAnnotationEngine.ReadWireData(conduit);
                var baseStyle = WireAnnotationStyleStore.Load(doc);
                var style   = WireAnnotationStyleOverride.Merge(baseStyle, conduit);

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
                    WireAnnotationEngine.PlaceAnnotation(doc, view, conduit, data, style);
                    t.Commit();
                }

                StingLog.Info($"Wire annotation placed on conduit {conduit.Id.Value}");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
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
                var doc = ctx.Doc; var view = doc.ActiveView;
                if (view is View3D || view is ViewSchedule)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        "Wire annotations require a plan, section, or elevation view.");
                    return Result.Cancelled;
                }

                var conduits = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_Conduit)
                    .WhereElementIsNotElementType().ToList();

                // Gap 10: reset collision registry for this batch
                WireAnnotationEngine.ClearAnnotationBoxes(doc, view.Id);

                if (conduits.Count == 0)
                {
                    TaskDialog.Show("STING Wire Annotation", "No conduits found in active view.");
                    return Result.Cancelled;
                }

                var td = new TaskDialog("STING Wire Annotation")
                {
                    MainInstruction = $"Found {conduits.Count} conduits. Annotate all?",
                    CommonButtons   = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton   = TaskDialogResult.Yes,
                };
                if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;

                var baseStyle = WireAnnotationStyleStore.Load(doc);
                int placed = 0, failed = 0, skipped = 0;
                bool cancelled = false;

                var prog = StingProgressDialog.Show("STING Wire Annotation", conduits.Count);
                using (var tg = new TransactionGroup(doc, "STING Batch Wire Annotations"))
                {
                    tg.Start();
                    foreach (var conduit in conduits)
                    {
                        prog.Increment($"Conduit {conduit.Id.Value}");
                        if (EscapeChecker.IsEscapePressed()) { cancelled = true; break; }

                        if (WireAnnotationEngine.HasAnnotation(doc, view, conduit))
                        { skipped++; continue; }

                        var data  = WireAnnotationEngine.ReadWireData(conduit);
                        var style = WireAnnotationStyleOverride.Merge(baseStyle, conduit);

                        using (var t = new Transaction(doc, "STING Wire Annot"))
                        {
                            t.Start();
                            try
                            {
                                var id = WireAnnotationEngine.PlaceAnnotation(
                                    doc, view, conduit, data, style);
                                if (id != ElementId.InvalidElementId) placed++; else failed++;
                            }
                            catch (Exception ex)
                            { failed++; StingLog.Warn($"Batch wire annot {conduit.Id.Value}: {ex.Message}"); }
                            t.Commit();
                        }
                    }
                    tg.Assimilate();
                }
                prog.Close();

                string suffix = "";
                if (skipped   > 0) suffix += $" {skipped} already annotated (skipped).";
                if (failed    > 0) suffix += $" {failed} failed.";
                if (cancelled)     suffix += " Cancelled by user.";
                TaskDialog.Show("STING Wire Annotation", $"Annotated {placed} conduits.{suffix}");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("WireAnnotateBatchCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Edit and save project-wide wire annotation style defaults.
    /// Per-conduit overrides (ELC_WIRE_ANNOT_* params) always win over these defaults.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WireAnnotationStyleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }

                var current = WireAnnotationStyleStore.Load(ctx.Doc);
                var dlg     = new WireAnnotationStyleDialog(current);
                if (dlg.ShowDialog() != true) return Result.Cancelled;

                WireAnnotationStyleStore.Save(ctx.Doc, dlg.Result);
                TaskDialog.Show("STING Wire Annotation Style",
                    "Project defaults saved.\n\n" +
                    "Per-conduit overrides (ELC_WIRE_ANNOT_* parameters) still take\n" +
                    "precedence over these defaults on individual conduits.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("WireAnnotationStyleCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Re-applies slash-mark appearance (colour, weight, length) to all existing
    /// wire annotations in the active view without removing and re-placing them.
    /// Useful after changing the project style or per-conduit overrides.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WireAnnotationRefreshStyleCommand : IExternalCommand
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

                var baseStyle = WireAnnotationStyleStore.Load(doc);

                // Find all slash-mark detail lines in this view
                var slashMarks = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(CurveElement))
                    .Cast<CurveElement>()
                    .Where(c => WireAnnotationEngine.IsSlashMark(c))
                    .ToList();

                if (slashMarks.Count == 0)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        "No STING slash marks found in the active view.");
                    return Result.Cancelled;
                }

                // For each slash mark, derive its owning conduit by marker text,
                // resolve the merged style, and re-apply graphic overrides.
                var conduitCache = new Dictionary<string, Element>();

                using (var t = new Transaction(doc, "STING Refresh Wire Annotation Style"))
                {
                    t.Start();
                    int updated = 0;
                    foreach (var mark in slashMarks)
                    {
                        try
                        {
                            // Resolve the conduit this mark belongs to
                            WireAnnotationStyle style = baseStyle;
                            Element conduit = null;
                            try
                            {
                                var cp = mark.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                string markerVal = cp?.AsString() ?? "";
                                if (markerVal.StartsWith("STING_WIRE_ANNOT|", StringComparison.Ordinal))
                                {
                                    string uid = markerVal.Substring("STING_WIRE_ANNOT|".Length);
                                    if (!conduitCache.TryGetValue(uid, out conduit))
                                    {
                                        conduit = new FilteredElementCollector(doc)
                                            .WhereElementIsNotElementType()
                                            .FirstOrDefault(e => e.UniqueId == uid);
                                        conduitCache[uid] = conduit;
                                    }
                                }
                            }
                            catch { }

                            if (conduit != null)
                                style = WireAnnotationStyleOverride.Merge(baseStyle, conduit);

                            WireAnnotationData data = conduit != null
                                ? WireAnnotationEngine.ReadWireData(conduit)
                                : null;

                            Color color  = WireAnnotationEngine.ResolveColor(style, data);
                            int   weight = Math.Max(1, Math.Min(16, style.SlashLineWeight));

                            var ogs = new OverrideGraphicSettings();
                            ogs.SetProjectionLineColor(color);
                            ogs.SetProjectionLineWeight(weight);
                            view.SetElementOverrides(mark.Id, ogs);
                            updated++;
                        }
                        catch (Exception ex) { StingLog.Warn($"RefreshStyle mark {mark.Id.Value}: {ex.Message}"); }
                    }
                    t.Commit();
                    TaskDialog.Show("STING Wire Annotation",
                        $"Style refreshed on {updated} slash marks in the active view.");
                }
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("WireAnnotationRefreshStyleCommand failed", ex);
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
            try { var p = el.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                  if (p != null && !p.IsReadOnly) p.Set("STING_HOME_RUN"); }
            catch { }
        }

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc; var uidoc = ctx.UIDoc; var view = doc.ActiveView;
                if (view is View3D || view is ViewSchedule)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        "Home-run arrows require a plan, section, or elevation view.");
                    return Result.Cancelled;
                }

                Reference reference;
                try { reference = uidoc.Selection.PickObject(ObjectType.Element,
                          new ConduitSelectionFilter(), "Pick a conduit for home-run arrow"); }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

                var conduit = doc.GetElement(reference);
                var data    = WireAnnotationEngine.ReadWireData(conduit);
                var lc      = conduit.Location as LocationCurve;
                if (lc?.Curve == null)
                {
                    TaskDialog.Show("STING Wire Annotation", "Picked element has no curve geometry.");
                    return Result.Cancelled;
                }

                var curve  = lc.Curve;
                var p0     = curve.GetEndPoint(0);
                var p1     = curve.GetEndPoint(1);
                var rawAxis= p1 - p0;
                if (rawAxis.GetLength() < 1e-6)
                {
                    TaskDialog.Show("STING Wire Annotation", "Conduit too short to place home-run arrow.");
                    return Result.Cancelled;
                }

                bool end0Panel = WireAnnotationEngine.EndConnectsToPanel(conduit, p0);
                bool end1Panel = WireAnnotationEngine.EndConnectsToPanel(conduit, p1);

                XYZ arrowBase, arrowDir;
                if (end0Panel && !end1Panel)      { arrowBase = p1; arrowDir = (p0 - p1).Normalize(); }
                else if (end1Panel && !end0Panel) { arrowBase = p0; arrowDir = (p1 - p0).Normalize(); }
                else                              { arrowBase = p1; arrowDir = (p0 - p1).Normalize(); }

                var style   = WireAnnotationStyleOverride.Merge(
                                WireAnnotationStyleStore.Load(doc), conduit);
                double arrowShaftFt = 150.0 / WireAnnotationEngine.MmPerFt;
                double headLenFt    = 30.0  / WireAnnotationEngine.MmPerFt;
                var   arrowTip      = arrowBase + arrowDir * arrowShaftFt;
                var   perpRaw       = XYZ.BasisZ.CrossProduct(arrowDir);
                var   perpDir       = perpRaw.GetLength() < 1e-6 ? XYZ.BasisX : perpRaw.Normalize();
                Color arrowColor    = WireAnnotationEngine.ResolveColor(style, data);

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
                        catch (Exception ex) { StingLog.Warn("HomeRun family place: " + ex.Message); }
                    }

                    if (!placedFamily)
                    {
                        void DrawLine(XYZ a, XYZ b)
                        {
                            try
                            {
                                var dc = doc.Create.NewDetailCurve(view, Line.CreateBound(a, b));
                                StampHomeRun(dc);
                                var ogs = new OverrideGraphicSettings();
                                ogs.SetProjectionLineColor(arrowColor);
                                ogs.SetProjectionLineWeight(style.SlashLineWeight);
                                view.SetElementOverrides(dc.Id, ogs);
                            }
                            catch (Exception ex) { StingLog.Warn("HomeRun line: " + ex.Message); }
                        }

                        DrawLine(arrowBase, arrowTip);
                        double ang = Math.PI / 12.0;
                        DrawLine(arrowTip, arrowTip + Rotate((-arrowDir) * headLenFt,  ang));
                        DrawLine(arrowTip, arrowTip + Rotate((-arrowDir) * headLenFt, -ang));
                    }

                    // Circuit-number label beside the arrow tip
                    string label = !string.IsNullOrEmpty(data.CircuitNumber) ? data.CircuitNumber
                                 : !string.IsNullOrEmpty(data.PanelName)     ? data.PanelName
                                 : "HR";
                    var labelPt = arrowTip + perpDir * (50.0 / WireAnnotationEngine.MmPerFt);
                    var tnt = new FilteredElementCollector(doc)
                        .OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault();
                    try
                    {
                        var lbl = TextNote.Create(doc, view.Id, labelPt, label,
                            tnt?.Id ?? ElementId.InvalidElementId);
                        StampHomeRun(lbl);
                    }
                    catch (Exception ex) { StingLog.Warn("HomeRun label: " + ex.Message); }

                    t.Commit();
                }

                StingLog.Info($"Home run arrow placed for conduit {conduit.Id.Value}");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("HomeRunArrowCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static XYZ Rotate(XYZ vec, double angleRad)
        {
            double cos = Math.Cos(angleRad), sin = Math.Sin(angleRad);
            return new XYZ(vec.X * cos - vec.Y * sin, vec.X * sin + vec.Y * cos, vec.Z);
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
                var doc = ctx.Doc; var view = doc.ActiveView;

                var notes = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(TextNote)).Cast<TextNote>()
                    .Where(n => WireAnnotationEngine.IsWireAnnotation(n)).ToList();

                var indyTags = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag)).Cast<IndependentTag>()
                    .Where(t => WireAnnotationEngine.IsWireAnnotationTag(t)).ToList();

                var slashes = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(CurveElement)).Cast<CurveElement>()
                    .Where(c => WireAnnotationEngine.IsSlashMark(c)).ToList();

                bool IsHomeRun(Element el) {
                    try { var p = el.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                          return string.Equals(p?.AsString(), "STING_HOME_RUN", StringComparison.Ordinal); }
                    catch { return false; }
                }

                var homeRunCurves = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(CurveElement)).Cast<CurveElement>()
                    .Where(c => IsHomeRun(c)).ToList();
                var homeRunNotes = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(TextNote)).Cast<TextNote>()
                    .Where(n => IsHomeRun(n)).ToList();

                if (notes.Count == 0 && indyTags.Count == 0 && slashes.Count == 0
                    && homeRunCurves.Count == 0 && homeRunNotes.Count == 0)
                {
                    TaskDialog.Show("STING Wire Annotation",
                        "No STING wire annotations found in active view.");
                    return Result.Cancelled;
                }

                var td = new TaskDialog("STING Wire Annotation")
                {
                    MainInstruction = $"Delete {notes.Count + indyTags.Count} annotations, " +
                                      $"{slashes.Count} slash marks, and " +
                                      $"{homeRunCurves.Count + homeRunNotes.Count} home-run elements?",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.Yes,
                };
                if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;

                int removed = 0;
                using (var t = new Transaction(doc, "STING Clear Wire Annotations"))
                {
                    t.Start();
                    foreach (var el in notes.Cast<Element>()
                        .Concat(indyTags).Concat(slashes)
                        .Concat(homeRunCurves).Concat(homeRunNotes))
                        try { doc.Delete(el.Id); removed++; }
                        catch (Exception ex) { StingLog.Warn("Clear element: " + ex.Message); }
                    t.Commit();
                }

                TaskDialog.Show("STING Wire Annotation", $"Removed {removed} elements.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("ClearWireAnnotationsCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Style dialog — WPF TaskDialog substitute (uses Revit API only)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lightweight style editor implemented as a multi-page TaskDialog chain.
    /// A full WPF dialog is the recommended replacement once the .rfa family
    /// is delivered; this gives operators immediate control without requiring
    /// a separate WPF file on this branch.
    /// </summary>
    internal class WireAnnotationStyleDialog
    {
        private readonly WireAnnotationStyle _input;
        public WireAnnotationStyle Result { get; private set; }
        public bool? DialogResult { get; private set; }

        public WireAnnotationStyleDialog(WireAnnotationStyle input)
        {
            _input = input;
            Result = input;
        }

        public bool? ShowDialog()
        {
            // Page 1 — Slash geometry
            var geomDlg = new TaskDialog("STING Wire Annotation Style — 1/3: Slash Geometry")
            {
                MainInstruction = "Current slash geometry defaults",
                MainContent     =
                    $"Length  : {_input.SlashLengthMm:0.#} mm\n" +
                    $"Spacing : {_input.SlashSpacingMm:0.#} mm\n" +
                    $"Angle   : {_input.SlashAngleDeg:0.#}° (30–90, 60=oblique/BS7671)\n" +
                    $"Scale   : ×{_input.ScaleFactor:0.##} multiplier\n" +
                    $"Offset  : {_input.LabelOffsetMm:0.#} mm label offset from conduit\n\n" +
                    "Edit these values in STING_WIRE_ANNOT_STYLE.json in _BIM_COORD/\n" +
                    "or set ELC_WIRE_ANNOT_* parameters on individual conduits.",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
            };
            if (geomDlg.Show() == TaskDialogResult.Cancel)
            { DialogResult = false; return false; }

            // Page 2 — Line style & colour
            var styleDlg = new TaskDialog("STING Wire Annotation Style — 2/3: Line Style & Colour")
            {
                MainInstruction = "Current line style defaults",
                MainContent     =
                    $"Line weight : {_input.SlashLineWeight} (1–16, 3≈0.25mm at 1:100)\n" +
                    $"Colour code : {_input.ColorCode} " +
                        $"(0=auto-black, 1=red, 2=blue, 3=orange, 4=green)\n" +
                    $"Auto-colour : {(_input.AutoColor ? "ON" : "OFF")}\n" +
                    $"  → red when VD>{_input.VdAlarmPct}% or fill>{_input.FillAlarmPct}%\n" +
                    $"  → orange for fire-rated cable\n" +
                    $"  → blue for armoured/shielded cable\n" +
                    $"Line style  : \"{_input.SlashLineStyleName}\"\n\n" +
                    "Override per-conduit via ELC_WIRE_ANNOT_COLOR_CODE_INT and\n" +
                    "ELC_WIRE_ANNOT_LINE_WEIGHT_INT parameters.",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
            };
            if (styleDlg.Show() == TaskDialogResult.Cancel)
            { DialogResult = false; return false; }

            // Page 3 — Label content
            var labelDlg = new TaskDialog("STING Wire Annotation Style — 3/3: Label Content")
            {
                MainInstruction = "Current label content defaults",
                MainContent     =
                    $"Show voltage drop  : {(_input.ShowVoltDrop  ? "YES" : "NO")}" +
                        $" (shown when VD>{_input.VdAlarmPct}%)\n" +
                    $"Show conduit fill  : {(_input.ShowFill      ? "YES" : "NO")}" +
                        $" (shown when fill>{_input.FillAlarmPct}%)\n" +
                    $"Show diameter      : {(_input.ShowDiameter  ? "YES" : "NO")}\n" +
                    $"Show ampacity      : {(_input.ShowAmpacity  ? "YES" : "NO")}\n" +
                    $"Compact label      : {(_input.CompactLabel  ? "YES" : "NO")}" +
                        " (omits panel name & install method)\n\n" +
                    "To change any value, edit STING_WIRE_ANNOT_STYLE.json directly\n" +
                    "or use the ELC_WIRE_ANNOT_* per-conduit parameters.",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
            };
            if (labelDlg.Show() == TaskDialogResult.Cancel)
            { DialogResult = false; return false; }

            Result = _input;     // read-only display for now; JSON edit is the edit path
            DialogResult = true;
            return true;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Selection filter
    // ─────────────────────────────────────────────────────────────────────────

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
