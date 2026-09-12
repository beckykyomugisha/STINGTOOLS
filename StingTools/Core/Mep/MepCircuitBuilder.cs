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
    /// <summary>
    /// H-4 — the write sink both result types implement, so StampTokens can
    /// report an unbound parameter regardless of which pass called it. Without
    /// it, AutoGroup would keep swallowing exactly the writes BuildExisting
    /// now reports — fixing one caller and leaving its twin silent.
    /// </summary>
    public interface IParamWriteSink
    {
        List<string> Warnings { get; }
        void NoteUnwritten(string param);
    }

    public sealed class MepCircuitResult : IParamWriteSink
    {
        public int Named { get; set; }
        public int Stamped { get; set; }
        public int Created { get; set; }
        public List<string> Rows { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>
        /// H-4 — parameter name → how many elements the write did not land on.
        /// Reported ONCE per parameter at the end rather than per element: a
        /// 4,000-element run would otherwise bury the finding in 4,000 identical
        /// warnings, which is its own kind of silence.
        /// </summary>
        public Dictionary<string, int> UnwrittenParams { get; } =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public void NoteUnwritten(string param)
        {
            if (string.IsNullOrEmpty(param)) return;
            UnwrittenParams.TryGetValue(param, out int c);
            UnwrittenParams[param] = c + 1;
        }

        /// <summary>Fold the unwritten tally into Warnings. Call once per run.</summary>
        public void FinaliseWarnings()
        {
            foreach (var kv in UnwrittenParams.OrderByDescending(k => k.Value))
                Warnings.Add($"{kv.Key}: write did not land on {kv.Value} element(s) — "
                           + "the parameter is not bound to those categories in this model. "
                           + "Run Load Shared Parameters, then re-run; until then downstream "
                           + "grouping on this token falls back to blank.");
        }
    }

    public sealed class CircuitGroupResult : IParamWriteSink
    {
        public int Groups { get; set; }       // panels that received circuits
        public int Created { get; set; }       // circuits created
        public int Unreachable { get; set; }   // devices with no panel in range
        public List<string> Rows { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();

        // H-4 — same unbound-parameter accounting as MepCircuitResult. AutoGroup
        // stamps through the same StampTokens, so it had the same silence.
        public Dictionary<string, int> UnwrittenParams { get; } =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public void NoteUnwritten(string param)
        {
            if (string.IsNullOrEmpty(param)) return;
            UnwrittenParams.TryGetValue(param, out int c);
            UnwrittenParams[param] = c + 1;
        }

        public void FinaliseWarnings()
        {
            foreach (var kv in UnwrittenParams.OrderByDescending(k => k.Value))
                Warnings.Add($"{kv.Key}: write did not land on {kv.Value} element(s) — "
                           + "the parameter is not bound to those categories in this model. "
                           + "Run Load Shared Parameters, then re-run; until then downstream "
                           + "grouping on this token falls back to blank.");
        }
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
                    StampTokens(sys, name, func, r);
                    r.Named++;

                    // Stamp its members so tags / schedules pick the circuit up.
                    try
                    {
                        foreach (Element el in sys.Elements.Cast<Element>())
                            StampTokens(el, name, func, r);
                    }
                    catch (Exception ex) { r.Warnings.Add($"{name}: members: {ex.Message}"); }

                    r.Stamped++;
                    if (r.Rows.Count < 80) r.Rows.Add($"◉ {name}  ({func})");
                }
                catch (Exception ex) { r.Warnings.Add($"Circuit {sys.Id}: {ex.Message}"); }
            }
            // H-4 — fold the unbound-parameter tally into the warnings the caller
            // shows. Without this the run still reports "Named N, Stamped N" while
            // having written nothing.
            r.FinaliseWarnings();
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

        /// <summary>
        /// Best-effort FIRST PASS: group every UN-circuited electrical device by its
        /// nearest panel (same level preferred) into power circuits of at most
        /// <paramref name="maxPerCircuit"/> devices, create them, and assign the panel.
        /// A starting point for an engineer to rebalance — it does NOT do load
        /// balancing or phase allocation. Requires an open transaction.
        /// </summary>
        public static CircuitGroupResult AutoGroup(Document doc, int maxPerCircuit, double maxDistM)
        {
            var r = new CircuitGroupResult();
            if (doc == null) { r.Warnings.Add("No document."); return r; }
            if (maxPerCircuit < 1) maxPerCircuit = 8;
            double maxFt = (maxDistM <= 0 ? 30.0 : maxDistM) / 0.3048; // metres → Revit internal feet (1 ft = 0.3048 m)

            var panels = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Where(p => HasElectricalConnector(p) && Pt(p) != null).ToList();
            if (panels.Count == 0) { r.Warnings.Add("No electrical panels (OST_ElectricalEquipment) to circuit to."); return r; }

            var byPanel = new Dictionary<ElementId, List<ElementId>>();
            var panelById = panels.ToDictionary(p => p.Id, p => p);
            var deviceCats = new ElementMulticategoryFilter(new[]
            {
                BuiltInCategory.OST_ElectricalFixtures, BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_LightingDevices
            });
            foreach (var fi in new FilteredElementCollector(doc).WhereElementIsNotElementType()
                         .WherePasses(deviceCats).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
            {
                try
                {
                    if (!HasElectricalConnector(fi)) continue;
                    var existing = fi.MEPModel?.GetElectricalSystems();
                    if (existing != null && existing.Count > 0) continue; // already on a circuit
                    var pt = Pt(fi); if (pt == null) continue;

                    var nearest = panels
                        .Select(p => new { p, d = Pt(p).DistanceTo(pt), sameLvl = SameLevel(fi, p) })
                        .Where(x => x.d <= maxFt)
                        .OrderByDescending(x => x.sameLvl).ThenBy(x => x.d)
                        .FirstOrDefault();
                    if (nearest == null) { r.Unreachable++; continue; }

                    if (!byPanel.TryGetValue(nearest.p.Id, out var list))
                        byPanel[nearest.p.Id] = list = new List<ElementId>();
                    list.Add(fi.Id);
                }
                catch (Exception ex) { r.Warnings.Add($"Device {fi.Id}: {ex.Message}"); }
            }

            foreach (var kv in byPanel)
            {
                var panel = panelById[kv.Key];
                string panelName = SafeInstName(panel);
                int circuitOnPanel = 0;
                foreach (var chunk in Chunk(kv.Value, maxPerCircuit))
                {
                    circuitOnPanel++;
                    try
                    {
                        var circuit = ElectricalSystem.Create(doc, chunk, ElectricalSystemType.PowerCircuit);
                        if (circuit == null) { r.Warnings.Add($"{panelName}: Create returned null"); continue; }
                        try { circuit.SelectPanel(panel); } catch (Exception ex) { r.Warnings.Add($"{panelName}: SelectPanel: {ex.Message}"); }
                        string name = $"{panelName}-AG{circuitOnPanel:D2}";
                        StampTokens(circuit, name, "PWR", r);
                        foreach (var id in chunk)
                            try { StampTokens(doc.GetElement(id), name, "PWR"); } catch { }
                        r.Created++;
                        if (r.Rows.Count < 80) r.Rows.Add($"✚ {name}  ({chunk.Count} device(s))");
                    }
                    catch (Exception ex) { r.Warnings.Add($"{panelName}: circuit create: {ex.Message}"); }
                }
            }
            r.Groups = byPanel.Count;
            r.FinaliseWarnings();   // H-4 — same reporting as BuildExisting
            return r;
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private static IEnumerable<List<ElementId>> Chunk(List<ElementId> ids, int size)
        {
            for (int i = 0; i < ids.Count; i += size)
                yield return ids.GetRange(i, Math.Min(size, ids.Count - i));
        }

        private static XYZ Pt(Element el)
        {
            try { return (el.Location as LocationPoint)?.Point; } catch { return null; }
        }

        private static bool SameLevel(Element a, Element b)
        {
            try { return a.LevelId != ElementId.InvalidElementId && a.LevelId == b.LevelId; }
            catch { return false; }
        }

        private static string SafeInstName(Element el)
        {
            try { return el.Name; } catch { return "PANEL"; }
        }

        // H-4 — this method swallowed four consecutive parameter writes, and it is
        // the SOLE writer of MEP_SYS_NAME. If that parameter is unbound — routine,
        // and exactly what G-8 is about — every circuited element silently got no
        // MEP system token and downstream grouping fell back to blank.
        //
        // The subtle part: a log line in the catch would NOT have caught it.
        // ParameterHelpers.SetString/SetIfEmpty return FALSE on an unbound
        // parameter and throw nothing at all, so the exception handler never
        // fires. The return value is the signal; the exception is the rare case.
        private static void StampTokens(Element el, string name, string func, IParamWriteSink r = null)
        {
            Write(el, ParamRegistry.MEP_SYS_NAME, name, overwrite: true, r: r);
            Write(el, ParamRegistry.DISC, "E", overwrite: false, r: r);
            Write(el, ParamRegistry.SYS, "LV", overwrite: false, r: r);
            if (!string.IsNullOrWhiteSpace(func))
                Write(el, ParamRegistry.FUNC, func, overwrite: false, r: r);
        }

        private static void Write(Element el, string param, string value, bool overwrite, IParamWriteSink r)
        {
            try
            {
                bool ok = overwrite
                    ? ParameterHelpers.SetString(el, param, value, overwrite: true)
                    : ParameterHelpers.SetIfEmpty(el, param, value);

                // SetIfEmpty returns false both when the parameter is missing AND
                // when it already holds a value, so only the overwrite path can
                // distinguish "unbound" from "left alone". Count the unbound case
                // off the lookup directly rather than inferring it from the return.
                if (!ok && el?.LookupParameter(param) == null)
                    r?.NoteUnwritten(param);
            }
            catch (Exception ex)
            {
                r?.Warnings.Add($"{param} on {el?.Id}: {ex.Message}");
                StingLog.WarnRateLimited("MepCircuit.Stamp", $"StampTokens {param}: {ex.Message}");
            }
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
