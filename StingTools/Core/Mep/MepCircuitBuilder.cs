// StingTools — MEP Circuit Builder (Phase E, electrical).
//
// Electrical circuits use a different mechanism than duct/pipe systems
// (ElectricalSystem, not a user-creatable system type). This mirrors Phase B's
// reliable path for electrical:
//
//   BuildExisting — name every ElectricalSystem "<Panel>-<Circuit>", stamp
//     ASS_MEP_SYS_NAME_TXT + the DISC/SYS/FUNC tag tokens on the circuit and its
//     members, so circuits flow into schedules / tags like duct/pipe systems.
//   CreateFromSelection — best-effort, user-driven: create a power circuit from
//     the currently-selected electrical devices and (if a panel is selected) assign it.
//
// CALLER OWNS THE ACTIVE TRANSACTION.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using StingTools.Core;

namespace StingTools.Core.Mep
{
    public sealed class MepCircuitResult
    {
        public int Named { get; set; }
        public int Stamped { get; set; }
        public int Created { get; set; }
        public List<string> Rows { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class MepCircuitBuilder
    {
        /// <summary>Name + stamp every existing electrical circuit. Requires an open transaction.</summary>
        public static MepCircuitResult BuildExisting(Document doc)
        {
            var r = new MepCircuitResult();
            if (doc == null) { r.Warnings.Add("No document."); return r; }

            var circuits = new FilteredElementCollector(doc).OfClass(typeof(ElectricalSystem))
                .Cast<ElectricalSystem>().ToList();
            if (circuits.Count == 0) { r.Warnings.Add("No electrical circuits in the model."); return r; }

            foreach (var sys in circuits)
            {
                try
                {
                    string panel = ""; try { panel = sys.PanelName ?? ""; } catch { }
                    string num   = ""; try { num = sys.CircuitNumber ?? ""; } catch { }
                    string name = (!string.IsNullOrWhiteSpace(panel) && !string.IsNullOrWhiteSpace(num))
                        ? $"{panel}-{num}"
                        : SafeName(sys);
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    string func = FuncFor(sys);

                    // Stamp the circuit element itself.
                    StampTokens(sys, name, func);
                    r.Named++;

                    // Stamp its members so tags / schedules pick the circuit up.
                    try
                    {
                        foreach (Element el in sys.Elements.Cast<Element>())
                            StampTokens(el, name, func);
                    }
                    catch (Exception ex) { r.Warnings.Add($"{name}: members: {ex.Message}"); }

                    r.Stamped++;
                    if (r.Rows.Count < 80) r.Rows.Add($"◉ {name}  ({func})");
                }
                catch (Exception ex) { r.Warnings.Add($"Circuit {sys.Id}: {ex.Message}"); }
            }
            return r;
        }

        /// <summary>
        /// Best-effort: create a power circuit from selected electrical devices and
        /// assign the selected panel (if any). Requires an open transaction.
        /// </summary>
        public static ElectricalSystem CreateFromSelection(
            Document doc, ICollection<ElementId> selection, MepCircuitResult r)
        {
            if (doc == null || selection == null || selection.Count == 0) return null;

            FamilyInstance panel = null;
            var deviceIds = new List<ElementId>();
            foreach (var id in selection)
            {
                var fi = doc.GetElement(id) as FamilyInstance;
                if (fi?.MEPModel == null) continue;
                var bic = (BuiltInCategory)(fi.Category?.Id.Value ?? 0);
                if (bic == BuiltInCategory.OST_ElectricalEquipment && panel == null) { panel = fi; continue; }
                // any electrical device with electrical connectors becomes a member
                if (HasElectricalConnector(fi)) deviceIds.Add(id);
            }

            if (deviceIds.Count == 0)
            {
                r?.Warnings.Add("Selection create: no electrical devices selected.");
                return null;
            }

            try
            {
                var circuit = ElectricalSystem.Create(doc, deviceIds, ElectricalSystemType.PowerCircuit);
                if (circuit == null) { r?.Warnings.Add("Selection create: ElectricalSystem.Create returned null."); return null; }
                if (panel != null)
                {
                    try { circuit.SelectPanel(panel); }
                    catch (Exception ex) { r?.Warnings.Add($"Selection create: SelectPanel: {ex.Message}"); }
                }
                if (r != null) r.Created++;
                return circuit;
            }
            catch (Exception ex)
            {
                r?.Warnings.Add($"Selection create failed: {ex.Message}");
                return null;
            }
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private static void StampTokens(Element el, string name, string func)
        {
            try { ParameterHelpers.SetString(el, ParamRegistry.MEP_SYS_NAME, name, overwrite: true); } catch { }
            try { ParameterHelpers.SetIfEmpty(el, ParamRegistry.DISC, "E"); } catch { }
            try { ParameterHelpers.SetIfEmpty(el, ParamRegistry.SYS, "LV"); } catch { }
            if (!string.IsNullOrWhiteSpace(func))
                try { ParameterHelpers.SetIfEmpty(el, ParamRegistry.FUNC, func); } catch { }
        }

        private static string FuncFor(ElectricalSystem sys)
        {
            try
            {
                // Lighting circuits → LTG, everything else power → PWR.
                bool lighting = sys.Elements.Cast<Element>().Any(e =>
                    (BuiltInCategory)(e.Category?.Id.Value ?? 0) is BuiltInCategory.OST_LightingFixtures
                                                                  or BuiltInCategory.OST_LightingDevices);
                return lighting ? "LTG" : "PWR";
            }
            catch { return "PWR"; }
        }

        private static string SafeName(ElectricalSystem sys)
        {
            try { return sys.Name; } catch { return ""; }
        }

        private static bool HasElectricalConnector(FamilyInstance fi)
        {
            try
            {
                var cs = fi.MEPModel?.ConnectorManager?.Connectors;
                if (cs == null) return false;
                foreach (Connector c in cs)
                    try { if (c.Domain == Domain.DomainElectrical) return true; } catch { }
            }
            catch { }
            return false;
        }
    }
}
