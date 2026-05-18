// Healthcare Pack H-7 — Medical Gas network walker.
//
// Walks every MEPCurve filtered to MGAS-* systems plus their connected
// terminal units, zone valve boxes, alarm panels and plant equipment.
// Builds a per-gas graph used by MgasFlowSolver and MgasSchematicComposer.

using System;
using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core;

namespace StingTools.Core.MedGas
{
    public class MgasNode
    {
        public ElementId Id;
        public string GasCode;
        public string Role;          // PIPE / TU / ZVB / AAP / MAP / MAN / VIE / PLT
        public string Tag;
        public XYZ Centre;
    }

    public class MgasEdge
    {
        public ElementId From;
        public ElementId To;
        public double LengthFt;
    }

    public class MgasNetwork
    {
        public Dictionary<string, List<MgasNode>> Nodes = new();
        public Dictionary<string, List<MgasEdge>> Edges = new();
        public string[] GasCodes;

        // Healthcare Pack — Phase H-7 accuracy fix: solver needs the document
        // to read MGS_DESIGN_FLOW_LPM_NR per terminal unit. The reference is
        // weak (we never touch it across transactions) but lets the flow
        // solver report real diversified zone loads instead of zeros.
        public Document SourceDoc { get; private set; }

        public static MgasNetwork Build(Document doc)
        {
            var net = new MgasNetwork();
            if (doc == null) return net;
            net.SourceDoc = doc;
            var gases = new[] {"O2","MA4","MA7","N2O","N2","CO2","HE","VAC","AGS","DENT"};
            net.GasCodes = gases;
            foreach (var g in gases) { net.Nodes[g] = new(); net.Edges[g] = new(); }

            var cats = new[]
            {
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_PipeAccessory,
                BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_MechanicalEquipment
            };
            var f = new ElementMulticategoryFilter(cats);
            var els = new FilteredElementCollector(doc).WherePasses(f).WhereElementIsNotElementType().ToElements();

            foreach (var el in els)
            {
                var gas = LookupString(el, "MGS_GAS_TYPE_TXT");
                if (string.IsNullOrEmpty(gas) || !net.Nodes.ContainsKey(gas)) continue;
                var role = ClassifyRole(el);
                var centre = TryGetCentre(el);
                net.Nodes[gas].Add(new MgasNode
                {
                    Id = el.Id, GasCode = gas, Role = role,
                    Tag = LookupString(el, "ASS_TAG_1") ?? el.Name, Centre = centre
                });
            }
            return net;
        }

        private static string ClassifyRole(Element el)
        {
            if (el.Category == null) return "OTHER";
            switch ((BuiltInCategory)(int)el.Category.Id.Value)
            {
                case BuiltInCategory.OST_PipeCurves:        return "PIPE";
                case BuiltInCategory.OST_PipeFitting:       return "FIT";
                case BuiltInCategory.OST_PipeAccessory:
                    return !string.IsNullOrEmpty(LookupString(el,"MGS_ZVB_REF_TXT")) ? "ZVB" : "ACC";
                case BuiltInCategory.OST_PlumbingFixtures:
                    var aap = LookupString(el,"MGS_AAP_REF_TXT");
                    if (!string.IsNullOrEmpty(aap)) return "AAP";
                    var prod = LookupString(el,"ASS_PRODCT_COD_TXT");
                    if (prod == "MAP") return "MAP";
                    if (prod != null && prod.StartsWith("TU")) return "TU";
                    return "FIX";
                case BuiltInCategory.OST_MechanicalEquipment:
                    var p = LookupString(el,"ASS_PRODCT_COD_TXT");
                    if (p == "VIE") return "VIE";
                    if (p == "MAN") return "MAN";
                    return "PLT";
                default: return "OTHER";
            }
        }

        private static string LookupString(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || !p.HasValue) return null;
                if (p.StorageType == StorageType.String) return p.AsString();
                return p.AsValueString();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        private static XYZ TryGetCentre(Element el)
        {
            try
            {
                if (el.Location is LocationPoint lp) return lp.Point;
                var bb = el.get_BoundingBox(null);
                if (bb != null) return (bb.Min + bb.Max) * 0.5;
            } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return XYZ.Zero;
        }

        public int TotalTerminalUnits =>
            Nodes.Values.SelectMany(n => n).Count(n => n.Role == "TU");
    }
}
