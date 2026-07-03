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
// WRITE TARGET (corrected — evidence in CATEGORY_BINDINGS.csv):
//   The result params are NOT bound to Electrical Circuits; they are bound to the
//   circuit's connected physical elements (Electrical Equipment / Electrical Fixtures
//   / Lighting Fixtures …) at TYPE level. So the circuit stays the READ source and the
//   results are written to its connected elements (ElectricalSystem.Elements), each
//   param resolved through ParamRegistry (never a hand-typed literal):
//     ParamRegistry.ELC_CKT_CSA_MM2 → "ELC_CBL_SZ_MM"    (Text) ← CsaLabel
//     ParamRegistry.ELC_CKT_VD_PCT  → "ELC_VLT_DROP_PCT" (Text) ← ActualVoltDropPct
//   Bound to Electrical Equipment (CATEGORY_BINDINGS.csv:8222) + Electrical Fixtures
//   (8121) + Lighting/Comms/etc., all Type-level (True). A Type-bound param is written
//   on the element's type when the instance does not expose it.
//
// REQUIRED-BINDING GAPS (params exist but are bound to NO writable target — reported,
// never silently no-oped): the numeric ELC_WIRE_CSA_MM2_NUM (MR_PARAMETERS.txt:2994)
// and ELC_WIRE_VD_PCT_NUM (2996) have no CATEGORY_BINDINGS rows, so numeric CSA/VD
// cannot be persisted until they are bound to Electrical Equipment/Fixtures.
//
// ANTI-HOLLOW: the result reports Computed (Calculate succeeded) AND Written (a param
// was actually Set on a real element), plus PerParamWritten; a real run with
// Computed>0 && Written==0 logs a loud warning and sets NoWritesPersisted.
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
        /// <summary>Per-param successful-write counts.</summary>
        public int WroteCsaTxt { get; set; }
        public int WroteVdPct { get; set; }
        /// <summary>True on a real run when Computed &gt; 0 but Written == 0 (silent-no-op guard).</summary>
        public bool NoWritesPersisted { get; set; }
        /// <summary>Params that exist but are bound to no writable target — surfaced, never dropped.</summary>
        public List<string> RequiredBindingGaps { get; } = new List<string>();
        public List<string> Skipped { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
        public List<CableSizingChange> SampleChanges { get; } = new List<CableSizingChange>();
    }

    public static class CableSizerApplyEngine
    {
        // Result params resolved THROUGH ParamRegistry (never hand-typed). Both are TEXT,
        // bound to the circuit's connected Electrical Equipment / Fixtures (Type-level).
        private static string P_CSA_TXT => ParamRegistry.ELC_CKT_CSA_MM2;   // → ELC_CBL_SZ_MM  (Text)
        private static string P_VD_TXT  => ParamRegistry.ELC_CKT_VD_PCT;    // → ELC_VLT_DROP_PCT (Text)

        // Numeric CSA/VD params exist (MR_PARAMETERS) but are bound to no writable target.
        private const string GAP_CSA_NUM = "ELC_WIRE_CSA_MM2_NUM";
        private const string GAP_VD_NUM  = "ELC_WIRE_VD_PCT_NUM";

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

            // Numeric CSA/VD params exist but are bound to no writable category — always
            // surface as required-binding gaps (never silently dropped).
            result.RequiredBindingGaps.Add($"{GAP_CSA_NUM} (numeric conductor CSA — bind to Electrical Equipment/Fixtures to persist)");
            result.RequiredBindingGaps.Add($"{GAP_VD_NUM} (numeric voltage-drop % — bind to Electrical Equipment/Fixtures to persist)");

            if (dryRun)
            {
                result.Planned = proposals.Count;
                return result;
            }

            // Real run — engine owns its Transaction (nests under a caller's group).
            // Write the result to each circuit's connected elements (Equipment/Fixtures),
            // resolving param names via ParamRegistry.
            using (var tx = new Transaction(doc, "STING Cable Sizing"))
            {
                tx.Start();
                foreach (var (id, r, _) in proposals)
                {
                    try
                    {
                        Element circuit = doc.GetElement(id);
                        var targets = ConnectedTargets(doc, circuit as ElectricalSystem);
                        if (targets.Count == 0)
                        {
                            result.Skipped.Add($"{id.Value}: circuit has no connected equipment/fixtures to write to");
                            continue;
                        }

                        bool csaAny = false, vdAny = false;
                        foreach (Element t in targets)
                        {
                            if (SetOnElementOrType(doc, t, P_CSA_TXT, r.CsaLabel ?? "")) csaAny = true;
                            if (SetOnElementOrType(doc, t, P_VD_TXT,
                                    r.ActualVoltDropPct.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))) vdAny = true;
                        }

                        if (csaAny) result.WroteCsaTxt++;
                        if (vdAny)  result.WroteVdPct++;
                        if (csaAny || vdAny) result.Written++;
                        else result.Skipped.Add($"{id.Value}: result params not bound on connected elements " +
                                                $"({P_CSA_TXT} / {P_VD_TXT})");
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
                              $"result params unresolved/unbound on connected elements: {P_CSA_TXT} / {P_VD_TXT}. " +
                              "Bind these to Electrical Equipment/Fixtures (or the circuit's connected category).");
            }

            StingLog.Info($"CableSizerApplyEngine: inspected {result.Inspected}, computed {result.Computed}, " +
                          $"written {result.Written} (csa {result.WroteCsaTxt} / vd {result.WroteVdPct}), " +
                          $"skipped {result.Skipped.Count}, errors {result.Errors.Count}" +
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

        // ── result → parameters (connected elements; instance or type) ───────────

        /// <summary>The circuit's connected elements (Equipment/Fixtures/…) — the bound
        /// write targets. The circuit itself is the read source only.</summary>
        private static List<Element> ConnectedTargets(Document doc, ElectricalSystem sys)
        {
            var list = new List<Element>();
            if (sys == null) return list;
            try
            {
                if (sys.Elements != null)
                    foreach (Element e in sys.Elements) if (e != null) list.Add(e);
            }
            catch (Exception ex) { StingLog.Warn($"ConnectedTargets {sys?.Id}: {ex.Message}"); }
            return list;
        }

        /// <summary>Set a TEXT shared param on the element, or on its type when the param
        /// is Type-bound (not exposed on the instance). Returns true only if a Set landed.</summary>
        private static bool SetOnElementOrType(Document doc, Element el, string paramName, string value)
        {
            if (el == null || string.IsNullOrEmpty(paramName)) return false;

            Parameter p = el.LookupParameter(paramName);
            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                return p.Set(value ?? "");

            // Type-bound param → write on the element's type.
            ElementId typeId = el.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                Element te = doc.GetElement(typeId);
                Parameter tp = te?.LookupParameter(paramName);
                if (tp != null && !tp.IsReadOnly && tp.StorageType == StorageType.String)
                    return tp.Set(value ?? "");
            }
            return false;
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
