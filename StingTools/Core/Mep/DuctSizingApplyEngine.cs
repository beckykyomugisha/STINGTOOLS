// ════════════════════════════════════════════════════════════════════════════
// DuctSizingApplyEngine — dialog-free, Document-taking duct auto-size APPLY engine
//
// The dialog→engine extraction pattern (2nd instance, after CableSizerApplyEngine).
// The sizing MATH is UNCHANGED — it is relocated verbatim from
// MepAutoSizeDuctCommand.Execute: CIBSE Guide B3 low-velocity commercial target
// (≤ role velocity), aspect-ratio clamp (≤ role aspect), round-up to the region's
// standard duct-size table from STING_MEP_SIZING_RULES.json, round-duct equivalent
// fallback. Per-element segment role via HvacSegmentRoleDetector, per-role targets
// via MepSizingRegistry — all preserved.
//
// This engine removes the UI coupling that made the command un-drivable headlessly:
//   • scope is a PARAMETER (MepSizingScope: Project | ActiveView | ElementIds) —
//     never the static StingHvacCommandHandler.CurrentScope field.
//   • pressure-class id is a PARAMETER (default "low") — never CurrentPressureClassId.
//   • NO StingResultPanel / TaskDialog / HvacPanel push anywhere in the engine.
//   • dryRun = plan only (computes + returns the proposal, mutates nothing).
//
// WRITE TARGETS
//   PRIMARY (the sizing result): native Revit geometry INSTANCE params — "Width" +
//   "Height" (rectangular) or "Diameter" (round). These are BuiltInParameter geometry
//   intrinsic to each duct segment: always Instance-scoped, never a shared-param
//   binding (correctly absent from MR_PARAMETERS.txt), and always present on the
//   element. The anti-hollow guard keys off these — Computed>0 && Written==0 (e.g.
//   every duct read-only because its size is fitting-driven) → loud Warn +
//   NoWritesPersisted.
//   SECONDARY (audit trail, best-effort): HVC_SIZE_PREV_TXT / HVC_SIZE_MODIFIED_DT /
//   HVC_SIZE_RULE_ID_TXT / HVC_PRESSURE_CLASS_TXT / HVC_SEGMENT_ROLE_TXT — all shared
//   params in MR_PARAMETERS.txt group HVC_SYSTEMS, bound Instance-level to
//   OST_DuctCurves by LoadSharedParamsCommand (BuildGroupCategoryOverrides
//   ["HVC_SYSTEMS"] = mepCats, which contains OST_DuctCurves). Resolved via
//   ParamRegistry (never a literal). Stamped via ParameterHelpers.SetString (no-op if
//   unbound) — they do NOT count toward the clean-persist metric.
//
// Transaction: the engine opens its OWN Transaction for a real run (standalone-safe)
// and never a TransactionGroup, so it nests cleanly under a caller's
// McpSafety.RunInTransactionGroup — no double group. A forced fault inside the caller's
// group rolls the whole op back.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Commands.Mep;      // MepSizeTables (duct/pipe/conduit standard-size tables)
using StingTools.Core.Electrical;   // reuse CableSizerApplyEngine.IsInstanceBound

namespace StingTools.Core.Mep
{
    /// <summary>Which MEP elements to size. Scope is a parameter — never a UI field.
    /// Shared by the duct + pipe apply engines.</summary>
    public enum MepSizingScopeKind { Project, ActiveView, ElementIds }

    public sealed class MepSizingScope
    {
        public MepSizingScopeKind Kind { get; private set; }
        public IReadOnlyList<ElementId> ElementIds { get; private set; }

        public static MepSizingScope Project() => new MepSizingScope { Kind = MepSizingScopeKind.Project };
        public static MepSizingScope ActiveView() => new MepSizingScope { Kind = MepSizingScopeKind.ActiveView };
        public static MepSizingScope ForIds(IEnumerable<ElementId> ids) =>
            new MepSizingScope { Kind = MepSizingScopeKind.ElementIds, ElementIds = (ids ?? Enumerable.Empty<ElementId>()).ToList() };
    }

    /// <summary>One proposed / applied duct-size change (capped sample for read-back).</summary>
    public sealed class DuctSizingChange
    {
        public long ElementId { get; set; }
        public string RoleId { get; set; }
        public string RoleSource { get; set; }
        public double FlowLs { get; set; }
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        public double DiameterMm { get; set; }
        public string SizeLabel { get; set; }
        public string PrevSize { get; set; }
        public bool IsRound { get; set; }
    }

    public sealed class DuctSizingApplyResult
    {
        public int Inspected { get; set; }
        /// <summary>Ducts for which a valid new size was computed (independent of writing).</summary>
        public int Computed { get; set; }
        /// <summary>Ducts for which a geometry size param was actually Set on the element.</summary>
        public int Written { get; set; }
        /// <summary>Dry-run: how many ducts WOULD be resized.</summary>
        public int Planned { get; set; }
        /// <summary>Per-param successful geometry-write counts.</summary>
        public int WroteWidth { get; set; }
        public int WroteHeight { get; set; }
        public int WroteDiameter { get; set; }
        /// <summary>True on a real run when Computed &gt; 0 but Written == 0 (silent-no-op guard).</summary>
        public bool NoWritesPersisted { get; set; }
        /// <summary>Per-element values that would land on a TYPE-scoped param (contamination) —
        /// impossible for native geometry, kept for read-back parity with the cable engine.</summary>
        public List<string> TypeScopeWrites { get; } = new List<string>();
        /// <summary>Best-effort audit-stamp params not bound to Ducts — surfaced, never silent.</summary>
        public List<string> RequiredBindingGaps { get; } = new List<string>();
        public List<string> Skipped { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
        public List<DuctSizingChange> SampleChanges { get; } = new List<DuctSizingChange>();
    }

    public static class DuctSizingApplyEngine
    {
        private const double MmToFt = 1.0 / 304.8;
        private const double DuctMaxVelMsFallback = 6.0;
        private const double MaxAspectFallback = 3.0;
        private const double DefaultAspectFallback = 1.5;

        // Audit-stamp params resolved THROUGH ParamRegistry (never hand-typed). All are
        // TEXT, group HVC_SYSTEMS, bound Instance-level to OST_DuctCurves (LoadSharedParams).
        private static string P_PREV  => ParamRegistry.HVC_SIZE_PREV_TXT;
        private static string P_MOD   => ParamRegistry.HVC_SIZE_MODIFIED_DT;
        private static string P_RULE  => ParamRegistry.HVC_SIZE_RULE_ID_TXT;
        private static string P_PCLS  => ParamRegistry.HVC_PRESSURE_CLASS_TXT;

        /// <summary>
        /// Auto-size the in-scope ducts. dryRun computes and returns the plan (writes
        /// nothing); a real run writes the geometry result inside the engine's own
        /// Transaction and returns the applied read-back. Never opens modal UI.
        /// pressureClassId is a parameter (default "low") stamped for audit — never read
        /// from a static UI field.
        /// </summary>
        public static DuctSizingApplyResult Apply(Document doc, MepSizingScope scope, bool dryRun,
            string pressureClassId = "low")
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            scope = scope ?? MepSizingScope.Project();
            if (string.IsNullOrEmpty(pressureClassId)) pressureClassId = "low";

            var result = new DuctSizingApplyResult();
            List<Element> ducts = CollectDucts(doc, scope);
            result.Inspected = ducts.Count;

            // Registry (velocity/aspect targets + size table) — same source the command used.
            double[] sizeTable = MepSizeTables.DuctStandardMm;
            double defaultAspect = DefaultAspectFallback;
            MepSizingRules rules = null;
            try
            {
                rules = MepSizingRegistry.Get(doc);
                if (rules.DuctDefaultAspect > 0) defaultAspect = rules.DuctDefaultAspect;
                sizeTable = MepSizeTables.DuctSizesFor(doc);
            }
            catch (Exception ex) { StingLog.Warn($"DuctSizingApplyEngine registry fallback: {ex.Message}"); }

            if (dryRun)
            {
                // Plan only — mutate nothing. HvacSegmentRoleDetector opportunistically
                // caches HVC_SEGMENT_ROLE_TXT, so run role-detection + the pure proposal
                // compute inside a transaction we ROLL BACK: identical roles to the real
                // run, no warn-spam, net zero mutation.
                List<DuctSizingChange> planProposals;
                using (var tx = new Transaction(doc, "STING Duct Sizing (dry-run role scan)"))
                {
                    tx.Start();
                    var roleMap = DetectRoles(doc, ducts);
                    planProposals = ComputeProposals(doc, ducts, roleMap, rules, sizeTable, defaultAspect, result);
                    tx.RollBack();
                }
                foreach (var c in planProposals.Take(25)) result.SampleChanges.Add(c);
                result.Computed = planProposals.Count;
                result.Planned = planProposals.Count;
                return result;
            }

            // Best-effort binding pre-check for the audit stamp (informational — never
            // blocks the geometry write, which is a native instance param).
            bool? modScope = CableSizerApplyEngine.IsInstanceBound(doc, P_MOD, BuiltInCategory.OST_DuctCurves);
            if (modScope == null)
                result.RequiredBindingGaps.Add($"{P_MOD} not bound to Ducts — audit stamps skipped (run STING → Load Shared Parameters)");

            // Real run — engine owns its Transaction (nests under a caller's group).
            using (var tx = new Transaction(doc, "STING Auto-size ducts"))
            {
                tx.Start();
                var roleMap = DetectRoles(doc, ducts);
                List<DuctSizingChange> proposals =
                    ComputeProposals(doc, ducts, roleMap, rules, sizeTable, defaultAspect, result);

                foreach (var c in proposals.Take(25)) result.SampleChanges.Add(c);
                result.Computed = proposals.Count;

                foreach (var change in proposals)
                {
                    try
                    {
                        if (!(doc.GetElement(new ElementId(change.ElementId)) is Element d))
                        { result.Skipped.Add($"{change.ElementId}: duct vanished"); continue; }

                        bool wrote = false;
                        if (!change.IsRound)
                        {
                            if (WriteSize(d, "Width", change.WidthMm)) { result.WroteWidth++; wrote = true; }
                            if (WriteSize(d, "Height", change.HeightMm)) { result.WroteHeight++; wrote = true; }
                        }
                        if (!wrote && WriteSize(d, "Diameter", change.DiameterMm))
                        { result.WroteDiameter++; wrote = true; }

                        if (wrote)
                        {
                            result.Written++;
                            StampSizingAudit(d, change.PrevSize, change.SizeLabel, change.RoleId, change.RoleSource);
                            try { ParameterHelpers.SetString(d, P_PCLS, pressureClassId, overwrite: true); }
                            catch (Exception exPc) { StingLog.Warn($"PressureClass stamp {d.Id}: {exPc.Message}"); }
                        }
                        else result.Skipped.Add($"{change.ElementId}: geometry size params read-only/absent (fitting-driven?)");
                    }
                    catch (Exception ex) { result.Errors.Add($"{change.ElementId}: {ex.Message}"); }
                }
                tx.Commit();
            }

            // Anti-hollow guard: computed but persisted nothing → loud, not silent.
            if (result.Computed > 0 && result.Written == 0)
            {
                result.NoWritesPersisted = true;
                StingLog.Warn($"DuctSizingApplyEngine: computed {result.Computed} duct size(s) but persisted 0 — " +
                              "every in-scope duct's geometry size param was read-only/absent (fitting-driven runs?).");
            }

            StingLog.Info($"DuctSizingApplyEngine: inspected {result.Inspected}, computed {result.Computed}, " +
                          $"written {result.Written} (w {result.WroteWidth} / h {result.WroteHeight} / dia {result.WroteDiameter}), " +
                          $"skipped {result.Skipped.Count}, errors {result.Errors.Count}" +
                          (result.NoWritesPersisted ? " — NO WRITES PERSISTED" : "") +
                          $" (aspect≤{defaultAspect:F1}, {sizeTable.Length} sizes, pclass={pressureClassId}).");
            return result;
        }

        // ── role detection ───────────────────────────────────────────────────────

        /// <summary>Batch role detection (one upstream walk shared across the view).
        /// Caller must hold an open Transaction so the HVC_SEGMENT_ROLE_TXT cache commits
        /// (or is rolled back in the dry-run path).</summary>
        private static Dictionary<ElementId, string> DetectRoles(Document doc, List<Element> ducts)
        {
            try { return HvacSegmentRoleDetector.DetectRolesBatch(doc, ducts); }
            catch (Exception ex) { StingLog.Warn($"DuctSizingApplyEngine DetectRolesBatch: {ex.Message}"); return null; }
        }

        // ── pure proposal computation (MATH verbatim from MepAutoSizeDuctCommand) ──

        private static List<DuctSizingChange> ComputeProposals(Document doc, List<Element> ducts,
            Dictionary<ElementId, string> roleMap, MepSizingRules rules, double[] sizeTable,
            double defaultAspect, DuctSizingApplyResult result)
        {
            var proposals = new List<DuctSizingChange>();
            foreach (var d in ducts)
            {
                try
                {
                    // Per-element role lookup (batch, then per-element fallback).
                    string roleId = roleMap != null && roleMap.TryGetValue(d.Id, out var rid)
                        ? rid
                        : HvacSegmentRoleDetector.DetectRole(doc, d);
                    double maxVelMs = DuctMaxVelMsFallback;
                    double maxAspect = MaxAspectFallback;
                    string roleSrc = "fallback";
                    if (rules != null)
                    {
                        var role = rules.GetDuctRole(roleId);
                        if (role != null && role.MaxVelocityMs > 0)
                        {
                            maxVelMs = role.MaxVelocityMs;
                            maxAspect = role.AspectMax > 0 ? role.AspectMax : MaxAspectFallback;
                            roleSrc = string.IsNullOrEmpty(role.Source) ? "registry" : role.Source;
                        }
                    }

                    double flowLs = MepUnits.ReadAirFlowLs(d, "HVC_FLOW_LS");
                    if (flowLs <= 0)
                        flowLs = MepUnits.ReadBuiltInFlowLs(d, BuiltInParameter.RBS_DUCT_FLOW_PARAM);
                    if (flowLs <= 0) { result.Skipped.Add($"{d.Id.Value}: missing/zero flow"); continue; }
                    double flowM3s = flowLs * 1e-3;

                    // Area = q / v
                    double area = flowM3s / maxVelMs;
                    // Round-duct equivalent:
                    double diaMm = Math.Sqrt(4.0 * area / Math.PI) * 1000.0;
                    // Rectangular with configured aspect default
                    double widthMm = Math.Sqrt(area * defaultAspect) * 1000.0;
                    double heightMm = widthMm / defaultAspect;
                    // Clamp aspect
                    if (widthMm / heightMm > maxAspect)
                        heightMm = widthMm / maxAspect;
                    widthMm = MepSizeTables.RoundUpTo(widthMm, sizeTable);
                    heightMm = MepSizeTables.RoundUpTo(heightMm, sizeTable);
                    double stdDiaMm = MepSizeTables.RoundUpTo(diaMm, sizeTable);

                    // Decide rectangular vs round the same way the command's write order
                    // does: prefer Width/Height when the duct carries them, else Diameter.
                    bool isRound = !HasWritableGeometry(d, "Width") && !HasWritableGeometry(d, "Height")
                                   && HasWritableGeometry(d, "Diameter");

                    proposals.Add(new DuctSizingChange
                    {
                        ElementId = d.Id.Value,
                        RoleId = roleId,
                        RoleSource = roleSrc,
                        FlowLs = Math.Round(flowLs, 1),
                        WidthMm = widthMm,
                        HeightMm = heightMm,
                        DiameterMm = stdDiaMm,
                        IsRound = isRound,
                        SizeLabel = isRound ? $"Ø{stdDiaMm:F0}" : $"{widthMm:F0}x{heightMm:F0}",
                        PrevSize = SnapshotDuctSize(d),
                    });
                }
                catch (Exception ex) { result.Errors.Add($"{d?.Id?.Value}: {ex.Message}"); }
            }
            return proposals;
        }

        private static bool HasWritableGeometry(Element el, string param)
        {
            try
            {
                var p = el?.LookupParameter(param);
                return p != null && !p.IsReadOnly && p.StorageType == StorageType.Double;
            }
            catch { return false; }
        }

        // ── audit-trail helpers (verbatim from MepAutoSizeDuctCommand) ─────────────

        private static string SnapshotDuctSize(Element d)
        {
            try
            {
                double w = UnitUtils.ConvertFromInternalUnits(ReadDouble(d, "Width"), UnitTypeId.Millimeters);
                double h = UnitUtils.ConvertFromInternalUnits(ReadDouble(d, "Height"), UnitTypeId.Millimeters);
                double dia = UnitUtils.ConvertFromInternalUnits(ReadDouble(d, "Diameter"), UnitTypeId.Millimeters);
                if (w > 0 && h > 0) return $"{w:F0}x{h:F0}";
                if (dia > 0) return $"Ø{dia:F0}";
            }
            catch (Exception ex) { StingLog.Warn($"SnapshotDuctSize {d.Id}: {ex.Message}"); }
            return "";
        }

        private static void StampSizingAudit(Element el, string previous, string current, string roleId, string ruleSrc)
        {
            try
            {
                if (!string.IsNullOrEmpty(previous))
                    ParameterHelpers.SetString(el, P_PREV, previous, overwrite: true);
                ParameterHelpers.SetString(el, P_MOD, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), overwrite: true);
                ParameterHelpers.SetString(el, P_RULE,
                    string.IsNullOrEmpty(roleId) ? ruleSrc : $"{roleId}|{ruleSrc}", overwrite: true);
            }
            catch (Exception ex) { StingLog.Warn($"StampSizingAudit {el.Id}: {ex.Message}"); }
        }

        private static double ReadDouble(Element el, string param)
        {
            try
            {
                var p = el?.LookupParameter(param);
                if (p == null) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.String &&
                    double.TryParse(p.AsString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double v)) return v;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0;
        }

        private static bool WriteSize(Element el, string param, double mm)
        {
            try
            {
                var p = el.LookupParameter(param);
                if (p == null || p.IsReadOnly) return false;
                if (p.StorageType != StorageType.Double) return false;
                p.Set(mm * MmToFt);
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"WriteSize {param}={mm}: {ex.Message}"); return false; }
        }

        // ── scope collection ───────────────────────────────────────────────────────

        private static List<Element> CollectDucts(Document doc, MepSizingScope scope)
        {
            if (scope.Kind == MepSizingScopeKind.ElementIds)
            {
                var list = new List<Element>();
                foreach (ElementId id in scope.ElementIds ?? new List<ElementId>())
                {
                    Element el = doc.GetElement(id);
                    if (el != null && el.Category != null
                        && (BuiltInCategory)el.Category.Id.Value == BuiltInCategory.OST_DuctCurves)
                        list.Add(el);
                }
                return list;
            }

            if (scope.Kind == MepSizingScopeKind.ActiveView && doc.ActiveView != null)
                return new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfCategory(BuiltInCategory.OST_DuctCurves)
                    .WhereElementIsNotElementType().ToList();

            return new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_DuctCurves)
                .WhereElementIsNotElementType().ToList();
        }
    }
}
