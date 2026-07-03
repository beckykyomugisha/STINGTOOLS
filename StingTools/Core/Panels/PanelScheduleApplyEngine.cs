// ════════════════════════════════════════════════════════════════════════════
// PanelScheduleApplyEngine — dialog-free, Document-taking panel-schedule APPLY engine
//
// The dialog→engine extraction pattern (4th instance). The rule-based creation LOGIC
// is UNCHANGED — relocated verbatim from BatchPanelSchedulesCommand.Execute:
// per-panel PanelScheduleView.CreateInstanceView with multi-template fallback via
// PanelScheduleTemplateRegistry, elec-panel-schedule-A3 Drawing-Type stamp, ELC_PNL_*
// panel-param backfill, and ELC_PANEL_SCHEDULE_REF_TXT circuit back-references.
//
// Unlike the sizing engines, the PRIMARY output here is ELEMENT CREATION
// (PanelScheduleView instances) — directly observable, not a param write. The
// anti-hollow guard therefore keys off Created vs Computed (panels that WOULD get a
// new schedule): Computed>0 && Created==0 on a real run → loud Warn + NoWritesPersisted.
// The param stamps (Drawing-Type / ELC_PNL_* / circuit refs) are SECONDARY best-effort
// integration (SetIfEmpty / SetString no-op when unbound) and are counted separately.
//
// This engine removes the UI coupling that made the command un-drivable headlessly:
//   • scope is a PARAMETER (PanelScheduleScope: Project | ActiveView | ElementIds).
//   • NO TaskDialog / StingResultPanel anywhere in the engine.
//   • dryRun classifies panels read-only (Reload / ShouldSkip / ResolveCandidates are
//     all read-only; only CreateInstanceView + the stamps mutate) and creates nothing.
//
// WRITE / OUTPUT TARGETS
//   PRIMARY: PanelScheduleView instances created via PanelScheduleView.CreateInstanceView
//   (real Revit elements — the observable output). SECONDARY, best-effort, all resolved
//   via ParamRegistry (except the shipped ELC_PANEL_SCHEDULE_REF_TXT literal, preserved
//   verbatim): STING_DRAWING_TYPE_ID_TXT (via DrawingTypeStamper, existing verified path)
//   on the schedule view; ELC_PNL_NAME / ELC_PNL_VOLTAGE / ELC_PNL_LOAD / ELC_PNL_FED_FROM
//   / ELC_MAIN_BRK / ELC_WAYS on the panel (SetIfEmpty — never overwrites user values);
//   PARA_ELC_PANEL + ELC_PANEL_SCHEDULE_REF_TXT on each feeding ElectricalSystem circuit.
//
// Transaction: the engine opens its OWN Transaction for a real run (standalone-safe)
// and never a TransactionGroup, so it nests cleanly under a caller's
// McpSafety.RunInTransactionGroup — no double group.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using StingTools.Commands.Panels;   // PanelScheduleTemplateRegistry
using StingTools.Core.Drawing;      // DrawingTypeStamper

namespace StingTools.Core.Panels
{
    /// <summary>Which panels (electrical equipment) to schedule. Scope is a parameter.</summary>
    public enum PanelScheduleScopeKind { Project, ActiveView, ElementIds }

    public sealed class PanelScheduleScope
    {
        public PanelScheduleScopeKind Kind { get; private set; }
        public IReadOnlyList<ElementId> ElementIds { get; private set; }

        public static PanelScheduleScope Project() => new PanelScheduleScope { Kind = PanelScheduleScopeKind.Project };
        public static PanelScheduleScope ActiveView() => new PanelScheduleScope { Kind = PanelScheduleScopeKind.ActiveView };
        public static PanelScheduleScope ForIds(IEnumerable<ElementId> ids) =>
            new PanelScheduleScope { Kind = PanelScheduleScopeKind.ElementIds, ElementIds = (ids ?? Enumerable.Empty<ElementId>()).ToList() };
    }

    /// <summary>One planned / created panel schedule (capped sample for read-back).</summary>
    public sealed class PanelScheduleChange
    {
        public long PanelId { get; set; }
        public string PanelName { get; set; }
        public string ScheduleName { get; set; }
        public string Template { get; set; }
        public bool WasExisting { get; set; }
    }

    public sealed class PanelScheduleApplyResult
    {
        public int Inspected { get; set; }
        /// <summary>Panels that WOULD get a NEW schedule (no existing, not skipped, has candidate template).</summary>
        public int Computed { get; set; }
        /// <summary>Schedules actually created on a real run (the observable output).</summary>
        public int Created { get; set; }
        /// <summary>Dry-run: how many schedules WOULD be created (== Computed).</summary>
        public int Planned { get; set; }
        public int SkippedExisting { get; set; }
        public int SkippedPattern { get; set; }
        public int Failed { get; set; }
        public int NoTemplate { get; set; }
        public int DrawingTypeStamped { get; set; }
        public int ParamsStamped { get; set; }
        public int CircuitRefsStamped { get; set; }
        /// <summary>True on a real run when Computed &gt; 0 but Created == 0 (silent-no-op guard).</summary>
        public bool NoWritesPersisted { get; set; }
        public Dictionary<string, int> PerTemplate { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public List<string> Failures { get; } = new List<string>();
        public List<string> SkippedNames { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
        public List<PanelScheduleChange> SampleChanges { get; } = new List<PanelScheduleChange>();
    }

    public static class PanelScheduleApplyEngine
    {
        private const string DrawingTypeId = "elec-panel-schedule-A3";

        /// <summary>
        /// Batch-create one PanelScheduleView per in-scope panel using the rule-based
        /// registry. dryRun classifies read-only and creates nothing; a real run creates
        /// the schedules + runs the post-create wiring inside the engine's own Transaction
        /// and returns the read-back. Never opens modal UI.
        /// </summary>
        public static PanelScheduleApplyResult Apply(Document doc, PanelScheduleScope scope, bool dryRun)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            scope = scope ?? PanelScheduleScope.Project();

            var result = new PanelScheduleApplyResult();

            PanelScheduleTemplateRegistry.Reload(doc);   // read-only config load

            List<FamilyInstance> panels = CollectPanels(doc, scope);
            result.Inspected = panels.Count;

            // Index existing PanelScheduleView by panel id (read-only).
            var existingByPanel = new Dictionary<long, PanelScheduleView>();
            foreach (var psv in new FilteredElementCollector(doc).OfClass(typeof(PanelScheduleView)).Cast<PanelScheduleView>())
            {
                try
                {
                    var pid = psv.GetPanel();
                    if (pid != null && pid != ElementId.InvalidElementId) existingByPanel[pid.Value] = psv;
                }
                catch (Exception ex) { StingLog.Warn($"Existing PSV index '{psv?.Name}': {ex.Message}"); }
            }

            // Classify each panel read-only: skip-pattern / existing (wire on real run) /
            // create-plan (with candidate templates) / no-template.
            var createPlans = new List<(FamilyInstance panel, string name, List<ElementId> candidates, string templateUsed)>();
            var existingToWire = new List<(FamilyInstance panel, PanelScheduleView psv)>();
            foreach (var panel in panels)
            {
                try
                {
                    string panelName = BatchPanelSchedulesCommand.SafePanelName(panel);

                    if (PanelScheduleTemplateRegistry.ShouldSkip(panelName))
                    { result.SkippedPattern++; result.SkippedNames.Add(panelName); continue; }

                    if (existingByPanel.TryGetValue(panel.Id.Value, out var existing))
                    { result.SkippedExisting++; existingToWire.Add((panel, existing)); continue; }

                    var candidates = PanelScheduleTemplateRegistry.ResolveCandidates(doc, panel, out _, out string templateUsed);
                    if (candidates == null || candidates.Count == 0)
                    { result.NoTemplate++; result.Failures.Add($"{panelName}: no PanelScheduleTemplate available in project"); continue; }

                    createPlans.Add((panel, panelName, candidates, templateUsed));
                    result.Computed++;
                    if (result.SampleChanges.Count < 25)
                        result.SampleChanges.Add(new PanelScheduleChange
                        { PanelId = panel.Id.Value, PanelName = panelName, Template = templateUsed, WasExisting = false });
                }
                catch (Exception ex) { result.Errors.Add($"{panel?.Id?.Value}: {ex.Message}"); }
            }

            if (dryRun)
            {
                result.Planned = result.Computed;
                return result;
            }

            // Real run — engine owns its Transaction (nests under a caller's group).
            using (var tx = new Transaction(doc, "STING Batch Panel Schedules"))
            {
                tx.Start();

                // Create new schedules (multi-template fallback — verbatim).
                foreach (var (panel, panelName, candidates, templateUsed) in createPlans)
                {
                    try
                    {
                        PanelScheduleView psv = null;
                        string lastError = null;
                        string winningTemplate = null;
                        foreach (var tid in candidates)
                        {
                            try
                            {
                                psv = PanelScheduleView.CreateInstanceView(doc, tid, panel.Id);
                                if (psv != null)
                                {
                                    var tEl = doc.GetElement(tid) as PanelScheduleTemplate;
                                    winningTemplate = tEl?.Name ?? templateUsed;
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                lastError = ex.Message;
                                StingLog.Warn($"CreateInstanceView '{panelName}' template={tid}: {ex.Message}");
                            }
                        }

                        if (psv == null)
                        {
                            result.Failed++;
                            result.Failures.Add($"{panelName}: every candidate template rejected ({lastError ?? "null result"})");
                            continue;
                        }

                        result.Created++;
                        string key = winningTemplate ?? "(unknown)";
                        result.PerTemplate[key] = result.PerTemplate.TryGetValue(key, out int n) ? n + 1 : 1;

                        if (StampDrawingType(psv)) result.DrawingTypeStamped++;
                        if (StampPanelParams(panel, psv)) result.ParamsStamped++;
                        string psvName = null;
                        try { psvName = psv.Name; } catch (Exception ex) { StingLog.Warn($"psv.Name read on '{panelName}': {ex.Message}"); }
                        if (!string.IsNullOrEmpty(psvName))
                            result.CircuitRefsStamped += StampCircuitBackrefs(doc, panel, psvName);

                        // Enrich the earlier sample with the created schedule name.
                        var s = result.SampleChanges.FirstOrDefault(x => x.PanelId == panel.Id.Value);
                        if (s != null) s.ScheduleName = psvName;
                    }
                    catch (Exception ex) { result.Errors.Add($"{panelName}: {ex.Message}"); }
                }

                // Wire existing schedules so re-runs converge on the same state (verbatim).
                foreach (var (panel, existing) in existingToWire)
                {
                    try
                    {
                        if (StampDrawingType(existing)) result.DrawingTypeStamped++;
                        if (StampPanelParams(panel, existing)) result.ParamsStamped++;
                        result.CircuitRefsStamped += StampCircuitBackrefs(doc, panel, existing.Name);
                    }
                    catch (Exception ex) { result.Errors.Add($"existing {panel?.Id?.Value}: {ex.Message}"); }
                }

                tx.Commit();
            }

            try
            {
                ActionAuditLog.Record("PanelSchedule_BatchCreate",
                    $"created={result.Created} existing={result.SkippedExisting} noTemplate={result.NoTemplate} " +
                    $"failed={result.Failed} drawingType={result.DrawingTypeStamped} params={result.ParamsStamped} circuitRefs={result.CircuitRefsStamped}");
            }
            catch (Exception ex) { StingLog.Warn($"audit: {ex.Message}"); }

            // Anti-hollow guard: panels wanted schedules but none were created → loud.
            if (result.Computed > 0 && result.Created == 0)
            {
                result.NoWritesPersisted = true;
                StingLog.Warn($"PanelScheduleApplyEngine: {result.Computed} panel(s) needed a schedule but 0 were created — " +
                              "every candidate PanelScheduleTemplate was rejected by CreateInstanceView.");
            }

            StingLog.Info($"PanelScheduleApplyEngine: inspected {result.Inspected}, computed {result.Computed}, " +
                          $"created {result.Created}, existing {result.SkippedExisting}, skipped {result.SkippedPattern}, " +
                          $"noTemplate {result.NoTemplate}, failed {result.Failed}, drawingType {result.DrawingTypeStamped}, " +
                          $"params {result.ParamsStamped}, circuitRefs {result.CircuitRefsStamped}" +
                          (result.NoWritesPersisted ? " — NO SCHEDULES CREATED" : "") + ".");
            return result;
        }

        // ── post-create wiring (verbatim from BatchPanelSchedulesCommand) ──────────

        private static bool StampDrawingType(PanelScheduleView psv)
        {
            try { return DrawingTypeStamper.Stamp(psv, DrawingTypeId); }
            catch (Exception ex) { StingLog.Warn($"Stamp drawing-type on '{psv.Name}': {ex.Message}"); return false; }
        }

        private static bool StampPanelParams(FamilyInstance panel, PanelScheduleView psv)
        {
            if (panel == null || psv == null) return false;
            int wrote = 0;
            try
            {
                wrote += Try(panel, ParamRegistry.ELC_PNL_NAME, psv.Name);
                wrote += Try(panel, ParamRegistry.ELC_PNL_VOLTAGE, ReadString(panel, "Panel Voltage"));
                wrote += Try(panel, ParamRegistry.ELC_PNL_LOAD, ReadString(panel, "Total Connected"));
                wrote += Try(panel, ParamRegistry.ELC_PNL_FED_FROM, ReadString(panel, "Panel Source") ?? ReadString(panel, "Source"));
                wrote += Try(panel, ParamRegistry.ELC_MAIN_BRK, ReadString(panel, "Mains") ?? ReadString(panel, "Main Disconnect"));
                wrote += Try(panel, ParamRegistry.ELC_WAYS, ReadInt(panel, "Number Of Circuits") ?? ReadInt(panel, "Number of Slots"));
            }
            catch (Exception ex) { StingLog.Warn($"StampPanelParams '{panel.Name}': {ex.Message}"); }
            return wrote > 0;
        }

        private static int StampCircuitBackrefs(Document doc, FamilyInstance panel, string scheduleName)
        {
            if (panel == null || string.IsNullOrEmpty(scheduleName)) return 0;
            int n = 0;
            try
            {
                string refValue = $"PS: {scheduleName}";
                var circuits = new FilteredElementCollector(doc)
                    .OfClass(typeof(ElectricalSystem))
                    .Cast<ElectricalSystem>()
                    .Where(s =>
                    {
                        try { return s.BaseEquipment != null && s.BaseEquipment.Id == panel.Id; }
                        catch (Exception ex) { StingLog.Warn($"BaseEquipment probe: {ex.Message}"); return false; }
                    });

                foreach (var sys in circuits)
                {
                    if (ParameterHelpers.SetString(sys, ParamRegistry.PARA_ELC_PANEL, refValue, overwrite: true)) n++;
                    if (ParameterHelpers.SetString(sys, "ELC_PANEL_SCHEDULE_REF_TXT", refValue, overwrite: true)) n++;
                }
            }
            catch (Exception ex) { StingLog.Warn($"StampCircuitBackrefs '{panel.Name}': {ex.Message}"); }
            return n;
        }

        private static int Try(Element el, string paramName, string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            return ParameterHelpers.SetIfEmpty(el, paramName, value) ? 1 : 0;
        }

        private static string ReadString(Element el, string nativeParam)
        {
            try
            {
                var p = el?.LookupParameter(nativeParam);
                if (p == null) return null;
                if (p.StorageType == StorageType.String) return p.AsString();
                if (p.StorageType == StorageType.Double) return p.AsValueString();
                if (p.StorageType == StorageType.Integer) return p.AsInteger().ToString();
            }
            catch (Exception ex) { StingLog.Warn($"ReadString {nativeParam}: {ex.Message}"); }
            return null;
        }

        private static string ReadInt(Element el, string nativeParam)
        {
            try
            {
                var p = el?.LookupParameter(nativeParam);
                if (p == null) return null;
                if (p.StorageType == StorageType.Integer) return p.AsInteger().ToString();
                if (p.StorageType == StorageType.Double) return ((int)Math.Round(p.AsDouble())).ToString();
            }
            catch (Exception ex) { StingLog.Warn($"ReadInt {nativeParam}: {ex.Message}"); }
            return null;
        }

        // ── scope collection ───────────────────────────────────────────────────────

        private static List<FamilyInstance> CollectPanels(Document doc, PanelScheduleScope scope)
        {
            if (scope.Kind == PanelScheduleScopeKind.ElementIds)
            {
                var list = new List<FamilyInstance>();
                foreach (ElementId id in scope.ElementIds ?? new List<ElementId>())
                {
                    if (doc.GetElement(id) is FamilyInstance fi && fi.Category != null
                        && (BuiltInCategory)fi.Category.Id.Value == BuiltInCategory.OST_ElectricalEquipment)
                        list.Add(fi);
                }
                return list;
            }

            FilteredElementCollector col = (scope.Kind == PanelScheduleScopeKind.ActiveView && doc.ActiveView != null)
                ? new FilteredElementCollector(doc, doc.ActiveView.Id)
                : new FilteredElementCollector(doc);

            return col.OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType().OfType<FamilyInstance>().ToList();
        }
    }
}
