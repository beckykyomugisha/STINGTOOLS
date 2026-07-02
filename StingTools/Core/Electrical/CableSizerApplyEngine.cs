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
// Result → parameters written (only when bound on the element; else skipped):
//   ELC_WIRE_CSA_MM2 (Number) ← RecommendedCsaMm2
//   ELC_CBL_SZ_MM    (Text)   ← CsaLabel     (canonical "cable size" text param;
//                                the brief's "ELC_CABLE_SIZE_TXT" does not exist)
//   ELC_VOLT_DROP_PCT(Number) ← ActualVoltDropPct
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
        public int Sized { get; set; }
        /// <summary>Dry-run: how many circuits WOULD be sized.</summary>
        public int Planned { get; set; }
        public List<string> Skipped { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
        public List<CableSizingChange> SampleChanges { get; } = new List<CableSizingChange>();
    }

    public static class CableSizerApplyEngine
    {
        // Result parameter names (shared params — written only when bound on the element).
        private const string P_CSA_NUM = "ELC_WIRE_CSA_MM2";   // Number, mm²
        private const string P_CSA_TXT = "ELC_CBL_SZ_MM";      // Text, human label
        private const string P_VD_PCT  = "ELC_VOLT_DROP_PCT";  // Number, %

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

            if (dryRun)
            {
                result.Planned = proposals.Count;
                return result;
            }

            // Real run — engine owns its Transaction (nests under a caller's group).
            using (var tx = new Transaction(doc, "STING Cable Sizing"))
            {
                tx.Start();
                foreach (var (id, r, _) in proposals)
                {
                    try
                    {
                        Element el = doc.GetElement(id);
                        if (el == null) { result.Skipped.Add($"{id.Value}: element vanished"); continue; }
                        if (WriteResult(el, r)) result.Sized++;
                        else result.Skipped.Add($"{id.Value}: no result parameter bound (ELC_WIRE_CSA_MM2 / ELC_CBL_SZ_MM / ELC_VOLT_DROP_PCT)");
                    }
                    catch (Exception ex) { result.Errors.Add($"{id.Value}: {ex.Message}"); }
                }
                tx.Commit();
            }

            StingLog.Info($"CableSizerApplyEngine: inspected {result.Inspected}, sized {result.Sized}, " +
                          $"skipped {result.Skipped.Count}, errors {result.Errors.Count} " +
                          $"(std={assumptions.Standard}, method={assumptions.InstallMethod}, {assumptions.Material}/{assumptions.Insulation}).");
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

        // ── result → parameters (only when bound + writable) ─────────────────────

        private static bool WriteResult(Element el, CableSizeResult r)
        {
            bool wrote = false;

            Parameter csaNum = el.LookupParameter(P_CSA_NUM);
            if (csaNum != null && !csaNum.IsReadOnly && csaNum.StorageType == StorageType.Double)
                wrote |= csaNum.Set(r.RecommendedCsaMm2);

            Parameter csaTxt = el.LookupParameter(P_CSA_TXT);
            if (csaTxt != null && !csaTxt.IsReadOnly && csaTxt.StorageType == StorageType.String)
                wrote |= csaTxt.Set(r.CsaLabel ?? "");

            Parameter vd = el.LookupParameter(P_VD_PCT);
            if (vd != null && !vd.IsReadOnly && vd.StorageType == StorageType.Double)
                wrote |= vd.Set(r.ActualVoltDropPct);

            return wrote;
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
