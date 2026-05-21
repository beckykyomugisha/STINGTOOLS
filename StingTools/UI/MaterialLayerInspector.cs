using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Engipedia-parity layer inspector.
    ///
    /// For Walls / Floors / Ceilings / Roofs / Foundations / Site Pads:
    ///   • read the CompoundStructure layers,
    ///   • return them in the order Revit stores them (exterior → interior),
    ///   • carry Function / Material name / Thickness (mm) / Wraps /
    ///     Variable / Membrane,
    ///   • build a multi-line tag string suitable for stamping into a
    ///     STING_LAYERS_TXT Type parameter.
    ///
    /// Engipedia's standout function is generating a clean readable layer
    /// tag the AEC team can drop straight onto a sheet. We mirror that
    /// here without any external dependency.
    /// </summary>
    public class MaterialLayer
    {
        public int Layer { get; set; }       // 1-based index
        public string Function { get; set; } // Finish1 / Substrate / Membrane / Insulation / Structure
        public string Material { get; set; }
        public string Thickness { get; set; } // mm string formatted to 1dp
        public bool Wraps { get; set; }
        public bool Variable { get; set; }
        public bool Membrane { get; set; }
        public double ThicknessMm { get; set; } // numeric for sorting / tagging
    }

    public static class MaterialLayerInspector
    {
        public static List<MaterialLayer> Read(Document doc, Element host)
        {
            var rows = new List<MaterialLayer>();
            if (doc == null || host == null) return rows;
            CompoundStructure cs = null;
            try
            {
                if (host is Wall w) cs = w.WallType?.GetCompoundStructure();
                else if (host is Floor f) cs = (doc.GetElement(f.GetTypeId()) as FloorType)?.GetCompoundStructure();
                else if (host is RoofBase r) cs = (doc.GetElement(r.GetTypeId()) as RoofType)?.GetCompoundStructure();
                else if (host is Ceiling c) cs = (doc.GetElement(c.GetTypeId()) as CeilingType)?.GetCompoundStructure();
                else if (host is Element el)
                {
                    // Foundations + Pads expose CompoundStructure via their type, too.
                    var et = doc.GetElement(host.GetTypeId()) as HostObjAttributes;
                    if (et != null) cs = et.GetCompoundStructure();
                }
            }
            catch (Exception ex) { StingLog.Warn($"LayerInspector.Read getCS: {ex.Message}"); }

            if (cs == null) return rows;

            try
            {
                var layers = cs.GetLayers();
                int i = 0;
                foreach (var layer in layers)
                {
                    i++;
                    string matName = "(by category)";
                    try
                    {
                        if (layer.MaterialId != null && layer.MaterialId.Value > 0)
                            matName = doc.GetElement(layer.MaterialId)?.Name ?? "(unknown)";
                    }
                    catch (Exception ex) { StingLog.Warn($"LayerInspector mat: {ex.Message}"); }

                    double tmm = 0;
                    try { tmm = UnitUtils.ConvertFromInternalUnits(layer.Width, UnitTypeId.Millimeters); }
                    catch (Exception ex) { StingLog.Warn($"LayerInspector thickness: {ex.Message}"); }

                    bool wraps = false, variable = false, membrane = false;
                    try { wraps = layer.LayerCapFlag; } catch { }
                    try { variable = cs.IsVariableLayer(i - 1); } catch { }
                    membrane = (layer.Function == MaterialFunctionAssignment.Membrane);

                    rows.Add(new MaterialLayer
                    {
                        Layer = i,
                        Function = layer.Function.ToString(),
                        Material = matName,
                        Thickness = tmm.ToString("F1"),
                        ThicknessMm = tmm,
                        Wraps = wraps,
                        Variable = variable,
                        Membrane = membrane,
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"LayerInspector.Read layers: {ex.Message}"); }
            return rows;
        }

        /// <summary>
        /// Build a multi-line tag string of the form
        ///   01 · Finish1     · BLE_Render_External · 15.0mm
        ///   02 · Substrate   · BLE_Block_Heavy     · 200.0mm
        ///   03 · Membrane    · BLE_DPM             · 0.5mm
        ///   04 · Insulation  · BLE_PIR             · 80.0mm
        ///   05 · Finish2     · BLE_Plasterboard    · 12.5mm
        /// suitable for a Wall/Floor/Roof type-parameter tag.
        /// </summary>
        public static string BuildLayerTag(List<MaterialLayer> layers)
        {
            if (layers == null || layers.Count == 0) return "";
            int maxFn = layers.Max(l => (l.Function ?? "").Length);
            int maxMat = layers.Max(l => (l.Material ?? "").Length);
            var sb = new StringBuilder();
            foreach (var l in layers)
            {
                sb.AppendLine(
                    $"{l.Layer:00} · " +
                    (l.Function ?? "").PadRight(maxFn) + " · " +
                    (l.Material ?? "").PadRight(maxMat) + " · " +
                    $"{l.ThicknessMm:F1}mm");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Stamp the layer tag into <c>STING_LAYERS_TXT</c> on the host
        /// element's Type. Caller owns the transaction.
        /// </summary>
        public static bool WriteLayerTag(Document doc, Element host, string tag)
        {
            if (doc == null || host == null) return false;
            try
            {
                var typeId = host.GetTypeId();
                if (typeId == null || typeId.Value <= 0) return false;
                var type = doc.GetElement(typeId);
                var p = type?.LookupParameter("STING_LAYERS_TXT");
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) return false;
                p.Set(tag ?? "");
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"WriteLayerTag: {ex.Message}"); return false; }
        }
    }
}
