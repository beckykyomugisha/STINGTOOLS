// ════════════════════════════════════════════════════════════════════════════
// CableSizerApplyEngine — dialog-free, Document-taking cable-sizing APPLY engine
//
// The dialog→engine extraction pattern, first instance. The PURE sizing math
// already lives in CableSizerEngine.Calculate(CableSizeInput) → CableSizeResult
// (no Revit, unit-testable). This engine is the missing model-application layer:
// it maps electrical-circuit parameters → CableSizeInput, calls Calculate, and
// writes the result back to the circuit — with NO modal UI and scope passed as a
// PARAMETER (never a static UI field). It is the single source of truth for
// "apply cable sizing to the model", callable from MCP, headless, and tests.
//
// Element → input mapping (source = ElectricalSystem power circuits):
//   LoadKW      ← RBS_ELEC_APPARENT_LOAD (VA) / 1000, fed with PowerFactor=1 so the
//                 engine's design current equals the circuit's apparent line current.
//   VoltageV    ← RBS_ELEC_VOLTAGE (falls back to the assumptions voltage).
//   Phases      ← PolesNumber >= 3 ? 3 : 1.
//   LengthM     ← RBS_ELEC_CIRCUIT_LENGTH_PARAM (ft → m). Missing/0 → skipped.
//   InstallMethod / Material / Insulation / VDLimitPct / Standard / AmbientTempC /
//   ContinuousLoad ← design ASSUMPTIONS (not carried by the model) from the caller.
//
// WRITE TARGET (Option C — per-circuit, Instance-bound):
//   Results are written to the ElectricalSystem (circuit) INSTANCE itself — the correct
//   per-circuit home — as proper NUMBER params (schedulable / filterable), resolved
//   through ParamRegistry (never a hand-typed literal):
//     ParamRegistry.ELC_WIRE_CSA_MM2_NUM (Number, mm²) ← RecommendedCsaMm2
//     ParamRegistry.ELC_WIRE_VD_PCT_NUM  (Number, %)   ← ActualVoltDropPct
//   These are bound Instance-level to Electrical Circuits by LoadSharedParamsCommand:
//   both live in MR_PARAMETERS.txt group ELC_PWR (group 4), whose category override
//   (MepCategories) now includes OST_ElectricalCircuit. All STING bindings use
//   NewInstanceBinding; circuits have no type, so Instance is the only valid scope.
//   The prior Type-bound text write to connected equipment/fixtures (ELC_CBL_SZ_MM /
//   ELC_VLT_DROP_PCT) was REMOVED — it contaminated shared types (last-writer-wins).
//   NOTE: bindings take effect only after the user runs Load Shared Parameters in a
//   project; until then the anti-hollow guard reports noWritesPersisted.
//
// ANTI-HOLLOW + SCOPE GUARD: the result reports Computed (Calculate succeeded) AND
// Written (a Number param was actually Set on the circuit instance) + per-param counts;
// Computed>0 && Written==0 → loud warning + NoWritesPersisted. IsInstanceBound detects a
// TYPE-scoped binding; a per-circuit value hitting a Type-bound param is recorded in
// TypeScopeWrites and does NOT count as a clean persist.
//
// Transaction: the engine opens its OWN Transaction for a real run (standalone-safe).
// It never opens a TransactionGroup, so it nests cleanly when a caller (MCP) wraps it
// in McpSafety.RunInTransactionGroup — no double-TransactionGroup.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using StingTools.Commands.Electrical.CableSizer;

namespace StingTools.Core.Electrical
{
    /// <summary>Which circuits to size. Scope is a parameter — never a UI field.</summary>
    public enum CableSizingScopeKind { Project, ActiveView, ElementIds }

    public sealed class CableSizingScope
    {
        public CableSizingScopeKind Kind { get; private set; }
        public IReadOnlyList<ElementId> ElementIds { get; private set; }

        public static CableSizingScope Project() => new CableSizingScope { Kind = CableSizingScopeKind.Project };
        public static CableSizingScope ActiveView() => new CableSizingScope { Kind = CableSizingScopeKind.ActiveView };
        public static CableSizingScope ForIds(IEnumerable<ElementId> ids) =>
            new CableSizingScope { Kind = CableSizingScopeKind.ElementIds, ElementIds = (ids ?? Enumerable.Empty<ElementId>()).ToList() };
    }

    /// <summary>One proposed / applied cable-size change (capped sample for read-back).</summary>
    public sealed class CableSizingChange
    {
        public long ElementId { get; set; }
        public string Circuit { get; set; }
        public double DesignCurrentA { get; set; }
        public double CsaMm2 { get; set; }
        public string CsaLabel { get; set; }
        public double VoltDropPct { get; set; }
        public int BreakerA { get; set; }
    }

    public sealed class CableSizingApplyResult
    {
        public int Inspected { get; set; }
        /// <summary>Circuits for which Calculate produced a valid size (independent of writing).</summary>
        public int Computed { get; set; }
        /// <summary>Circuits for which at least one result param was actually Set on a real element.</summary>
        public int Written { get; set; }
        /// <summary>Dry-run: how many circuits WOULD be sized.</summary>
        public int Planned { get; set; }
        /// <summary>Per-param successful-write counts (numeric, on the circuit instance).</summary>
        public int WroteCsaNum { get; set; }
        public int WroteVdNum { get; set; }
        /// <summary>True on a real run when Computed &gt; 0 but Written == 0 (silent-no-op guard).</summary>
        public bool NoWritesPersisted { get; set; }
        /// <summary>Per-element/per-circuit values written to a TYPE-scoped param (shared across
        /// all instances of that type — contamination). Such writes do NOT count as a clean persist.</summary>
        public List<string> TypeScopeWrites { get; } = new List<string>();
        /// <summary>Params that exist but are bound to no writable target — surfaced, never dropped.</summary>
        public List<string> RequiredBindingGaps { get; } = new List<string>();
        public List<string> Skipped { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
        public List<CableSizingChange> SampleChanges { get; } = new List<CableSizingChange>();
    }

    public static class CableSizerApplyEngine
    {
        // Result params resolved THROUGH ParamRegistry (never hand-typed). Both are NUMBER,
        // bound Instance-level to Electrical Circuits (ELC_PWR group; LoadSharedParams).
        // The per-circuit object is the sole write target — the prior Type-bound text write
        // to connected equipment/fixtures contaminated shared types and has been removed.
        private static string P_CSA_NUM => ParamRegistry.ELC_WIRE_CSA_MM2_NUM;  // Number, mm²
        private static string P_VD_NUM  => ParamRegistry.ELC_WIRE_VD_PCT_NUM;   // Number, %

        /// <summary>
        /// Size cables for the in-scope power circuits. dryRun computes and returns the
        /// plan (writes nothing); a real run writes the result params inside the engine's
        /// own Transaction and returns the applied read-back. Never opens modal UI.
        /// </summary>
        public static CableSizingApplyResult Apply(Document doc, CableSizingScope scope,
            CableSizeInput assumptions, bool dryRun)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            scope = scope ?? CableSizingScope.Project();
            assumptions = assumptions ?? new CableSizeInput();

            var result = new CableSizingApplyResult();
            List<ElectricalSystem> circuits = CollectCircuits(doc, scope);
            result.Inspected = circuits.Count;

            // Compute proposals (pure). Nothing is written here.
            var proposals = new List<(ElementId id, CableSizeResult r, string label)>();
            foreach (ElectricalSystem sys in circuits)
            {
                try
                {
                    CableSizeInput input = MapInput(sys, assumptions, out string skipReason);
                    if (input == null) { result.Skipped.Add($"{sys.Id.Value}: {skipReason}"); continue; }

                    CableSizeResult r = CableSizerEngine.Calculate(input);
                    if (r == null || r.RecommendedCsaMm2 <= 0)
                    {
                        result.Skipped.Add($"{sys.Id.Value}: " +
                            (string.IsNullOrEmpty(r?.Warning) ? "no tabulated size" : r.Warning));
                        continue;
                    }
                    proposals.Add((sys.Id, r, CircuitLabel(sys)));
                }
                catch (Exception ex) { result.Errors.Add($"{sys.Id.Value}: {ex.Message}"); }
            }

            foreach (var (id, r, label) in proposals.Take(25))
                result.SampleChanges.Add(ToChange(id, r, label));

            result.Computed = proposals.Count;

            if (dryRun)
            {
                result.Planned = proposals.Count;
                return result;
            }

            // Binding-scope pre-check on the Electrical Circuits category: these numeric
            // params must be Instance-bound (circuits have no type). null = not yet bound
            // (LoadSharedParams not run) → surface as a required-binding gap, not silent.
            bool? csaScope = IsInstanceBound(doc, P_CSA_NUM, BuiltInCategory.OST_ElectricalCircuit);
            bool? vdScope  = IsInstanceBound(doc, P_VD_NUM, BuiltInCategory.OST_ElectricalCircuit);
            if (csaScope == null)
                result.RequiredBindingGaps.Add($"{P_CSA_NUM} not bound to Electrical Circuits — run STING → Load Shared Parameters");
            if (vdScope == null)
                result.RequiredBindingGaps.Add($"{P_VD_NUM} not bound to Electrical Circuits — run STING → Load Shared Parameters");

            // Real run — engine owns its Transaction (nests under a caller's group).
            // Write numeric results to the CIRCUIT instance itself, resolving names via ParamRegistry.
            using (var tx = new Transaction(doc, "STING Cable Sizing"))
            {
                tx.Start();
                foreach (var (id, r, _) in proposals)
                {
                    try
                    {
                        if (!(doc.GetElement(id) is ElectricalSystem circuit))
                        { result.Skipped.Add($"{id.Value}: circuit vanished"); continue; }

                        bool csa = SetNumberOnInstance(circuit, P_CSA_NUM, r.RecommendedCsaMm2,
                            csaScope, "conductor CSA", result);
                        bool vd = SetNumberOnInstance(circuit, P_VD_NUM, r.ActualVoltDropPct,
                            vdScope, "voltage drop %", result);

                        if (csa) result.WroteCsaNum++;
                        if (vd)  result.WroteVdNum++;
                        if (csa || vd) result.Written++;
                        else result.Skipped.Add($"{id.Value}: result params not bound on the circuit " +
                                                $"({P_CSA_NUM} / {P_VD_NUM}) — run Load Shared Parameters");
                    }
                    catch (Exception ex) { result.Errors.Add($"{id.Value}: {ex.Message}"); }
                }
                tx.Commit();
            }

            // Anti-hollow guard: computed but persisted nothing → loud, not silent.
            if (result.Computed > 0 && result.Written == 0)
            {
                result.NoWritesPersisted = true;
                StingLog.Warn($"CableSizerApplyEngine: cable sizing computed {result.Computed} but persisted 0 — " +
                              $"result params unresolved/unbound on Electrical Circuits: {P_CSA_NUM} / {P_VD_NUM}. " +
                              "Run STING → Load Shared Parameters (binds them Instance-level to Electrical Circuits).");
            }
            if (result.TypeScopeWrites.Count > 0)
                StingLog.Warn($"CableSizerApplyEngine: {result.TypeScopeWrites.Count} per-circuit value(s) written to a " +
                              "TYPE-scoped param (shared across instances) — see typeScopeWrites; these are NOT clean per-circuit persists.");

            StingLog.Info($"CableSizerApplyEngine: inspected {result.Inspected}, computed {result.Computed}, " +
                          $"written {result.Written} (csa {result.WroteCsaNum} / vd {result.WroteVdNum}), " +
                          $"typeScope {result.TypeScopeWrites.Count}, skipped {result.Skipped.Count}, errors {result.Errors.Count}" +
                          (result.NoWritesPersisted ? " — NO WRITES PERSISTED" : "") +
                          $" (std={assumptions.Standard}, method={assumptions.InstallMethod}, {assumptions.Material}/{assumptions.Insulation}).");
            return result;
        }

        // ── element → input mapping ──────────────────────────────────────────────

        private static CableSizeInput MapInput(ElectricalSystem sys, CableSizeInput a, out string skipReason)
        {
            skipReason = null;

            double va = SafeDouble(sys.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD));
            if (va <= 0) { skipReason = "missing/zero apparent load"; return null; }

            double lengthFt = SafeDouble(sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_LENGTH_PARAM));
            double lengthM = lengthFt > 0 ? UnitUtils.ConvertFromInternalUnits(lengthFt, UnitTypeId.Meters) : 0;
            if (lengthM <= 0) { skipReason = "missing circuit length (draw the circuit path first)"; return null; }

            double v = SafeDouble(sys.get_Parameter(BuiltInParameter.RBS_ELEC_VOLTAGE));
            if (v <= 0) v = a.VoltageV > 0 ? a.VoltageV : 230.0;

            int poles = SafePoles(sys);
            int phases = poles >= 3 ? 3 : 1;

            return new CableSizeInput
            {
                // Apparent kVA fed as kW with PF=1 → engine Ib == circuit apparent line current.
                LoadKW = va / 1000.0,
                PowerFactor = 1.0,
                VoltageV = v,
                Phases = phases,
                LengthM = lengthM,
                InstallMethod = string.IsNullOrEmpty(a.InstallMethod) ? "C" : a.InstallMethod,
                Material = string.IsNullOrEmpty(a.Material) ? "Cu" : a.Material,
                Insulation = string.IsNullOrEmpty(a.Insulation) ? "XLPE90" : a.Insulation,
                VDLimitPct = a.VDLimitPct > 0 ? a.VDLimitPct : 3.0,
                Standard = string.IsNullOrEmpty(a.Standard) ? "BS7671" : a.Standard,
                AmbientTempC = a.AmbientTempC > 0 ? a.AmbientTempC : 30.0,
                ContinuousLoad = a.ContinuousLoad,
            };
        }

        // ── result → parameter (numeric, on the CIRCUIT instance) ────────────────

        /// <summary>Set a NUMBER shared param on the circuit instance. Guards on
        /// LookupParameter != null &amp;&amp; !IsReadOnly &amp;&amp; StorageType==Double. When the resolved
        /// binding is TYPE-scoped (per-circuit value on a shared type), records it in
        /// TypeScopeWrites and does NOT count it as a clean persist (returns false).</summary>
        private static bool SetNumberOnInstance(Element circuit, string paramName, double value,
            bool? instanceBound, string label, CableSizingApplyResult result)
        {
            if (circuit == null || string.IsNullOrEmpty(paramName)) return false;

            Parameter p = circuit.LookupParameter(paramName);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.Double) return false;

            // Type-scope contamination guard: a per-circuit value must not land on a
            // Type-bound param (shared across every instance of the type).
            if (instanceBound == false)
            {
                result.TypeScopeWrites.Add($"{circuit.Id.Value}: {paramName} ({label}) is TYPE-bound — per-circuit value would be shared; not persisted");
                return false;
            }

            return p.Set(value);
        }

        /// <summary>
        /// Inspect doc.ParameterBindings for <paramref name="paramName"/> on
        /// <paramref name="category"/>: returns true when the binding is an
        /// InstanceBinding, false when it is a TypeBinding, and null when the param is
        /// not bound to that category at all (or not found).
        /// </summary>
        internal static bool? IsInstanceBound(Document doc, string paramName, BuiltInCategory category)
        {
            try
            {
                Category cat = doc.Settings.Categories.get_Item(category);
                if (cat == null) return null;
                long catId = cat.Id.Value;

                DefinitionBindingMapIterator it = doc.ParameterBindings.ForwardIterator();
                while (it.MoveNext())
                {
                    Definition def = it.Key;
                    if (def == null || !string.Equals(def.Name, paramName, StringComparison.Ordinal)) continue;

                    // Match the category, then report the binding kind.
                    if (it.Current is InstanceBinding ib)
                    {
                        foreach (Category c in ib.Categories) if (c.Id.Value == catId) return true;
                        return null;   // param bound Instance, but not to this category
                    }
                    if (it.Current is TypeBinding tb)
                    {
                        foreach (Category c in tb.Categories) if (c.Id.Value == catId) return false;
                        return null;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"IsInstanceBound {paramName}/{category}: {ex.Message}"); }
            return null;
        }

        // ── scope collection ─────────────────────────────────────────────────────

        private static List<ElectricalSystem> CollectCircuits(Document doc, CableSizingScope scope)
        {
            var seen = new HashSet<long>();
            var circuits = new List<ElectricalSystem>();

            void AddIfPower(ElectricalSystem s)
            {
                if (s == null || !seen.Add(s.Id.Value)) return;
                try { if (s.SystemType != ElectricalSystemType.PowerCircuit) return; } catch { return; }
                circuits.Add(s);
            }

            if (scope.Kind == CableSizingScopeKind.ElementIds)
            {
                foreach (ElementId id in scope.ElementIds ?? new List<ElementId>())
                {
                    Element el = doc.GetElement(id);
                    if (el is ElectricalSystem sys) { AddIfPower(sys); continue; }
                    // Selected equipment → expand to its circuits.
                    if (el is FamilyInstance fi && fi.MEPModel != null)
                    {
                        try { foreach (ElectricalSystem s in fi.MEPModel.GetElectricalSystems()) AddIfPower(s); }
                        catch (Exception ex) { StingLog.Warn($"GetElectricalSystems {id.Value}: {ex.Message}"); }
                    }
                }
                return circuits;
            }

            var all = new FilteredElementCollector(doc).OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>();

            if (scope.Kind == CableSizingScopeKind.ActiveView && doc.ActiveView != null)
            {
                // Circuits are non-graphical — a circuit is "in the active view" when at
                // least one of its connected elements is visible there.
                var visible = new HashSet<long>(
                    new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .WhereElementIsNotElementType().ToElementIds().Select(i => i.Value));
                foreach (ElectricalSystem s in all)
                {
                    try
                    {
                        bool inView = s.Elements != null && s.Elements.Cast<Element>().Any(e => visible.Contains(e.Id.Value));
                        if (inView) AddIfPower(s);
                    }
                    catch (Exception ex) { StingLog.Warn($"ActiveView circuit filter {s?.Id}: {ex.Message}"); }
                }
                return circuits;
            }

            foreach (ElectricalSystem s in all) AddIfPower(s);
            return circuits;
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        private static CableSizingChange ToChange(ElementId id, CableSizeResult r, string label) => new CableSizingChange
        {
            ElementId = id.Value,
            Circuit = label,
            DesignCurrentA = Math.Round(r.DesignCurrentA, 1),
            CsaMm2 = r.RecommendedCsaMm2,
            CsaLabel = r.CsaLabel,
            VoltDropPct = Math.Round(r.ActualVoltDropPct, 2),
            BreakerA = r.ProposedBreakerA,
        };

        private static string CircuitLabel(ElectricalSystem sys)
        {
            try
            {
                string panel = sys.PanelName ?? "";
                string num = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER)?.AsString() ?? "";
                return string.IsNullOrEmpty(panel) ? num : $"{panel}-{num}";
            }
            catch { return ""; }
        }

        private static double SafeDouble(Parameter p)
        {
            try { return p != null && p.HasValue ? p.AsDouble() : 0; }
            catch (Exception ex) { StingLog.Warn($"CableSizer read: {ex.Message}"); return 0; }
        }

        private static int SafePoles(ElectricalSystem s)
        {
            try { return s.PolesNumber; }
            catch (Exception ex) { StingLog.Warn($"CableSizer poles: {ex.Message}"); return 1; }
        }
    }
}
