// ════════════════════════════════════════════════════════════════════════════
// PipeSizingApplyEngine — dialog-free, Document-taking pipe auto-size APPLY engine
//
// The dialog→engine extraction pattern (3rd instance). The sizing MATH is UNCHANGED
// — relocated verbatim from MepAutoSizePipeCommand.Execute: per-service target
// velocity (CIBSE Guide C ≤ 2.5 m/s fallback, overridden per service from
// STING_MEP_SIZING_RULES.json), A = q / v, d = sqrt(4A/π), round up to the region's
// standard bore table. Per-pipe service via PipeServiceDetector (read-only), per-
// service velocity via MepSizingRegistry — all preserved.
//
// This engine removes the UI coupling that made the command un-drivable headlessly:
//   • scope is a PARAMETER (MepSizingScope: Project | ActiveView | ElementIds) —
//     never the static StingHvacCommandHandler.CurrentScope field.
//   • NO StingResultPanel / TaskDialog / HvacPanel push anywhere in the engine.
//   • dryRun = plan only. PipeServiceDetector.DetectServiceId is read-only and the
//     sizing math is pure, so the whole proposal compute runs WITHOUT a transaction
//     (exactly like CableSizerApplyEngine) — mutates nothing.
//
// WRITE TARGETS
//   PRIMARY (the sizing result): the pipe's native "Diameter" geometry INSTANCE param
//   — intrinsic to each pipe segment, always Instance-scoped, no shared-param binding
//   (correctly absent from MR_PARAMETERS.txt), always present. The anti-hollow guard
//   keys off it — Computed>0 && Written==0 → loud Warn + NoWritesPersisted.
//   SECONDARY (audit trail, best-effort): HVC_PIPE_SERVICE_TXT — a shared param in
//   MR_PARAMETERS.txt group HVC_SYSTEMS, bound Instance-level to OST_PipeCurves by
//   LoadSharedParamsCommand (BuildGroupCategoryOverrides["HVC_SYSTEMS"] = mepCats,
//   which contains OST_PipeCurves). Resolved via ParamRegistry (never a literal),
//   stamped via ParameterHelpers.SetString (no-op if unbound). Does NOT count toward
//   the clean-persist metric.
//
// Transaction: the engine opens its OWN Transaction for a real run (standalone-safe)
// and never a TransactionGroup, so it nests cleanly under a caller's
// McpSafety.RunInTransactionGroup — no double group.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Commands.Mep;      // MepSizeTables (pipe standard-bore tables)
using StingTools.Core.Electrical;   // reuse CableSizerApplyEngine.IsInstanceBound

namespace StingTools.Core.Mep
{
    /// <summary>One proposed / applied pipe-size change (capped sample for read-back).</summary>
    public sealed class PipeSizingChange
    {
        public long ElementId { get; set; }
        public string ServiceId { get; set; }
        public string ServiceLabel { get; set; }
        public double FlowLs { get; set; }
        public double MaxVelMs { get; set; }
        public double DiameterMm { get; set; }
        public string SizeLabel { get; set; }
    }

    public sealed class PipeSizingApplyResult
    {
        public int Inspected { get; set; }
        /// <summary>Pipes for which a valid new bore was computed (independent of writing).</summary>
        public int Computed { get; set; }
        /// <summary>Pipes for which the Diameter param was actually Set on the element.</summary>
        public int Written { get; set; }
        /// <summary>Dry-run: how many pipes WOULD be resized.</summary>
        public int Planned { get; set; }
        /// <summary>Per-param successful geometry-write count.</summary>
        public int WroteDiameter { get; set; }
        /// <summary>Best-effort service-stamp writes (audit; not a clean-persist).</summary>
        public int WroteService { get; set; }
        /// <summary>True on a real run when Computed &gt; 0 but Written == 0 (silent-no-op guard).</summary>
        public bool NoWritesPersisted { get; set; }
        /// <summary>Per-element values that would land on a TYPE-scoped param — impossible for
        /// native geometry, kept for read-back parity with the cable engine.</summary>
        public List<string> TypeScopeWrites { get; } = new List<string>();
        /// <summary>Best-effort audit-stamp params not bound to Pipes — surfaced, never silent.</summary>
        public List<string> RequiredBindingGaps { get; } = new List<string>();
        public List<string> Skipped { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
        public List<PipeSizingChange> SampleChanges { get; } = new List<PipeSizingChange>();
    }

    public static class PipeSizingApplyEngine
    {
        private const double MmToFt = 1.0 / 304.8;
        private const double PipeMaxVelMsFallback = 2.5;

        // Audit-stamp param resolved THROUGH ParamRegistry (never hand-typed). TEXT,
        // group HVC_SYSTEMS, bound Instance-level to OST_PipeCurves (LoadSharedParams).
        private static string P_SERVICE => ParamRegistry.HVC_PIPE_SERVICE_TXT;

        /// <summary>
        /// Auto-size the in-scope pipes. dryRun computes and returns the plan (writes
        /// nothing); a real run writes the Diameter result (+ best-effort service stamp)
        /// inside the engine's own Transaction and returns the applied read-back. Never
        /// opens modal UI.
        /// </summary>
        public static PipeSizingApplyResult Apply(Document doc, MepSizingScope scope, bool dryRun)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            scope = scope ?? MepSizingScope.Project();

            var result = new PipeSizingApplyResult();
            List<Element> pipes = CollectPipes(doc, scope);
            result.Inspected = pipes.Count;

            // Registry (per-service velocity + bore table) — same source the command used.
            double[] boreTable = MepSizeTables.PipeStandardBoreMm;
            MepSizingRules rules = null;
            try
            {
                rules = MepSizingRegistry.Get(doc);
                boreTable = MepSizeTables.PipeBoresFor(doc);
            }
            catch (Exception ex) { StingLog.Warn($"PipeSizingApplyEngine registry fallback: {ex.Message}"); }

            // Compute proposals (pure — DetectServiceId is read-only, math is pure).
            // Nothing is written here.
            var proposals = new List<PipeSizingChange>();
            foreach (var p in pipes)
            {
                try
                {
                    string serviceId = PipeServiceDetector.DetectServiceId(doc, p);
                    double maxVelMs = PipeMaxVelMsFallback;
                    string svcLabel = serviceId;
                    if (rules != null)
                    {
                        var svc = rules.GetPipeService(serviceId);
                        if (svc != null && svc.MaxVelocityMs > 0)
                        {
                            maxVelMs = svc.MaxVelocityMs;
                            svcLabel = string.IsNullOrEmpty(svc.Label) ? serviceId : svc.Label;
                        }
                    }

                    double flowLs = ReadDouble(p, "PLM_FLOW_LS");
                    if (flowLs <= 0) { result.Skipped.Add($"{p.Id.Value}: missing/zero flow (PLM_FLOW_LS)"); continue; }
                    double flowM3s = flowLs * 1e-3;
                    double area = flowM3s / maxVelMs;
                    double diaMm = Math.Sqrt(4.0 * area / Math.PI) * 1000.0;
                    double standard = MepSizeTables.RoundUpTo(diaMm, boreTable);

                    proposals.Add(new PipeSizingChange
                    {
                        ElementId = p.Id.Value,
                        ServiceId = serviceId,
                        ServiceLabel = svcLabel,
                        FlowLs = Math.Round(flowLs, 2),
                        MaxVelMs = maxVelMs,
                        DiameterMm = standard,
                        SizeLabel = $"Ø{standard:F0}",
                    });
                }
                catch (Exception ex) { result.Errors.Add($"{p?.Id?.Value}: {ex.Message}"); }
            }

            foreach (var c in proposals.Take(25)) result.SampleChanges.Add(c);
            result.Computed = proposals.Count;

            if (dryRun)
            {
                result.Planned = proposals.Count;
                return result;
            }

            // Best-effort binding pre-check for the service stamp (informational — never
            // blocks the Diameter write, which is a native instance param).
            bool? svcScope = CableSizerApplyEngine.IsInstanceBound(doc, P_SERVICE, BuiltInCategory.OST_PipeCurves);
            if (svcScope == null)
                result.RequiredBindingGaps.Add($"{P_SERVICE} not bound to Pipes — service stamp skipped (run STING → Load Shared Parameters)");

            // Real run — engine owns its Transaction (nests under a caller's group).
            using (var tx = new Transaction(doc, "STING Auto-size pipes"))
            {
                tx.Start();
                foreach (var change in proposals)
                {
                    try
                    {
                        if (!(doc.GetElement(new ElementId(change.ElementId)) is Element p))
                        { result.Skipped.Add($"{change.ElementId}: pipe vanished"); continue; }

                        // Stamp the detected service (audit) — same order as the command
                        // (before the size write), best-effort.
                        try
                        {
                            if (ParameterHelpers.SetString(p, P_SERVICE, change.ServiceId, overwrite: true))
                                result.WroteService++;
                        }
                        catch (Exception exS) { StingLog.Warn($"Pipe service stamp {p.Id}: {exS.Message}"); }

                        if (WriteSize(p, "Diameter", change.DiameterMm)) { result.WroteDiameter++; result.Written++; }
                        else result.Skipped.Add($"{change.ElementId}: Diameter read-only/absent");
                    }
                    catch (Exception ex) { result.Errors.Add($"{change.ElementId}: {ex.Message}"); }
                }
                tx.Commit();
            }

            // Anti-hollow guard: computed but persisted nothing → loud, not silent.
            if (result.Computed > 0 && result.Written == 0)
            {
                result.NoWritesPersisted = true;
                StingLog.Warn($"PipeSizingApplyEngine: computed {result.Computed} pipe bore(s) but persisted 0 — " +
                              "every in-scope pipe's Diameter param was read-only/absent.");
            }

            StingLog.Info($"PipeSizingApplyEngine: inspected {result.Inspected}, computed {result.Computed}, " +
                          $"written {result.Written} (dia {result.WroteDiameter}, service {result.WroteService}), " +
                          $"skipped {result.Skipped.Count}, errors {result.Errors.Count}" +
                          (result.NoWritesPersisted ? " — NO WRITES PERSISTED" : "") +
                          $" ({boreTable.Length} bores).");
            return result;
        }

        // ── helpers (verbatim from MepAutoSizePipeCommand) ─────────────────────────

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

        private static List<Element> CollectPipes(Document doc, MepSizingScope scope)
        {
            if (scope.Kind == MepSizingScopeKind.ElementIds)
            {
                var list = new List<Element>();
                foreach (ElementId id in scope.ElementIds ?? new List<ElementId>())
                {
                    Element el = doc.GetElement(id);
                    if (el != null && el.Category != null
                        && (BuiltInCategory)el.Category.Id.Value == BuiltInCategory.OST_PipeCurves)
                        list.Add(el);
                }
                return list;
            }

            if (scope.Kind == MepSizingScopeKind.ActiveView && doc.ActiveView != null)
                return new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType().ToList();

            return new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType().ToList();
        }
    }
}
