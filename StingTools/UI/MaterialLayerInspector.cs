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
                    // Revit API exposes the variable-layer index, not a per-layer check.
                    try { variable = cs.VariableLayerIndex == (i - 1); } catch { }
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
    }
}
