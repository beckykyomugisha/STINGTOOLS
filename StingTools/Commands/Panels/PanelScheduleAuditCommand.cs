using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Panels
{
    /// <summary>
    /// Read-only audit of every panel and panel schedule in the project.
    /// Surfaces drift between the rule-based registry and reality:
    ///   - panels with no schedule
    ///   - schedules whose template name does not match the rule that
    ///     would apply today (e.g. registry rule changed since creation)
    ///   - schedules whose body has spares but unfilled real slots
    ///   - panels with empty STING_ELC_PNL_* parameter containers
    /// Mirrors the relationship that <c>TemplateAuditCommand</c> has with
    /// <c>AutoAssignTemplatesCommand</c>.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PanelScheduleAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            PanelScheduleTemplateRegistry.Reload(doc);

            var panels = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .ToList();

            var schedulesByPanel = new Dictionary<long, PanelScheduleView>();
            foreach (var psv in new FilteredElementCollector(doc)
                .OfClass(typeof(PanelScheduleView)).Cast<PanelScheduleView>())
            {
                try
                {
                    var pid = psv.GetPanel();
                    if (pid != null && pid != ElementId.InvalidElementId)
                        schedulesByPanel[pid.Value] = psv;
                }
                catch (Exception ex) { StingLog.Warn($"GetPanel: {ex.Message}"); }
            }

            int panelsTotal = panels.Count;
            int withSchedule = 0, withoutSchedule = 0, skippedByPattern = 0;
            int templateDrift = 0, missingPnlParams = 0, missingIp = 0;
            var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var driftRows = new List<string>();
            var noScheduleRows = new List<string>();
            var paramGapRows = new List<string>();

            foreach (var p in panels)
            {
                string panelName = BatchPanelSchedulesCommand.SafePanelName(p);
                bool skip = PanelScheduleTemplateRegistry.ShouldSkip(panelName);
                if (skip) { skippedByPattern++; continue; }

                if (!schedulesByPanel.TryGetValue(p.Id.Value, out var psv))
                {
                    withoutSchedule++;
                    noScheduleRows.Add(panelName);
                    continue;
                }
                withSchedule++;

                string currentTemplate = "(unknown)";
                try
                {
                    var tid = psv.GetTemplate();
                    if (tid != null && tid != ElementId.InvalidElementId)
                    {
                        var t = doc.GetElement(tid) as PanelScheduleTemplate;
                        if (t != null) currentTemplate = t.Name;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"GetTemplate audit '{psv.Name}': {ex.Message}"); }

                totals[currentTemplate] = totals.TryGetValue(currentTemplate, out int n) ? n + 1 : 1;

                _ = PanelScheduleTemplateRegistry.ResolveCandidates(doc, p, out _, out string suggestedTemplate);
                if (!string.IsNullOrEmpty(suggestedTemplate)
                    && !string.Equals(suggestedTemplate, currentTemplate, StringComparison.OrdinalIgnoreCase))
                {
                    templateDrift++;
                    driftRows.Add($"{panelName}: '{currentTemplate}' → suggest '{suggestedTemplate}'");
                }

                // ELC_PNL_VOLTAGE and ELC_WAYS resolve to NUMBER parameters, and
                // GetString returns "" for anything that is not text — so every panel
                // was reported as missing them, filled in or not.
                bool anyPnlParamEmpty =
                    !HasParamValue(p, ParamRegistry.ELC_PNL_NAME)
                    || !HasParamValue(p, ParamRegistry.ELC_PNL_VOLTAGE)
                    || !HasParamValue(p, ParamRegistry.ELC_WAYS);
                if (anyPnlParamEmpty)
                {
                    missingPnlParams++;
                    paramGapRows.Add(panelName);
                }
                // The STING schedule header shows the IP rating, and only the PNLS card's
                // Save writes it — so a blank column is reported here, not discovered on
                // the printed schedule.
                if (!HasParamValue(p, "ELC_PNL_IP_RATING_TXT")) missingIp++;
            }

            // PNL-20: a STING template built before its spec changed still renders — just
            // without the new columns — and nothing said so. Compare each built STING
            // template's bound parameters with STING_PANEL_SCHEDULE_SPECS.json.
            var staleRows = new List<string>();
            var specWarn = new List<string>();
            var unresolvedParams = new List<string>();
            string templateCheckError = null;
            int stingBuilt = 0, stingMissing = 0, specCount = 0;
            try
            {
                var specs = StingTools.Core.Panels.PanelTemplateBuilder.LoadSpecs(doc, specWarn).Templates;
                specCount = specs.Count;
                foreach (var spec in specs)
                {
                    var t = StingTools.Core.Panels.PanelTemplateBuilder.FindTemplate(doc, spec.Name);
                    if (t == null) { stingMissing++; continue; }
                    stingBuilt++;
                    var missing = StingTools.Core.Panels.PanelTemplateBuilder.MissingFromTemplate(doc, t, spec, unresolvedParams);
                    if (missing.Count > 0)
                        staleRows.Add($"{spec.Name}: {missing.Count} column(s) missing ({string.Join(", ", missing.Take(5))}{(missing.Count > 5 ? ", …" : "")})");
                }
            }
            catch (Exception ex)
            {
                // A crashed check is not "a template out of date" — report it as what it is.
                StingLog.Warn($"Audit STING template check: {ex.Message}");
                templateCheckError = ex.Message;
            }

            var result = StingResultPanel.Create("Panel Schedule Audit");
            result.SetSubtitle($"{panelsTotal} panels · {withSchedule} with schedule · {withoutSchedule} without · {templateDrift} drift");

            double pct = panelsTotal > 0 ? 100.0 * withSchedule / panelsTotal : 100.0;
            result.RAGBar(pct, $"{pct:F0}% panels have a schedule");

            result.AddSection("SUMMARY")
                  .Metric("Panels", panelsTotal.ToString())
                  .MetricHighlight("With schedule", withSchedule.ToString())
                  .MetricWarn("Without schedule", withoutSchedule.ToString())
                  .Metric("Skipped by pattern", skippedByPattern.ToString())
                  .MetricWarn("Template drift", templateDrift.ToString(), "current ≠ rule-suggested")
                  .MetricWarn("Missing PNL params", missingPnlParams.ToString(), "ELC_PNL_NAME / VOLTAGE / WAYS")
                  .MetricWarn("IP rating not set", missingIp.ToString(), "PNLS → PANEL PARAMETERS → IP Rating → Save to Model")
                  .Metric("STING templates built", specCount == 0 ? "no specs loaded" : $"{stingBuilt} of {specCount}",
                          stingMissing > 0 ? $"{stingMissing} not built — PNLS 📐" : null)
                  .MetricWarn("STING templates out of date", templateCheckError != null ? "not checked" : staleRows.Count.ToString(), "rebuild with PNLS 📐");
            if (templateCheckError != null)
                result.MetricError("STING template check failed", templateCheckError, "see the STING log");

            if (totals.Count > 0)
            {
                result.AddSection("BY TEMPLATE (in use)");
                foreach (var kv in totals.OrderByDescending(k => k.Value))
                    result.Metric(kv.Key, kv.Value.ToString());
            }

            if (noScheduleRows.Count > 0)
            {
                result.AddSection("PANELS WITHOUT A SCHEDULE");
                foreach (string s in noScheduleRows.Take(25)) result.Text(s);
                if (noScheduleRows.Count > 25) result.Text($"… {noScheduleRows.Count - 25} more.");
            }

            if (driftRows.Count > 0)
            {
                result.AddSection("TEMPLATE DRIFT");
                foreach (string s in driftRows.Take(25)) result.Text(s);
                if (driftRows.Count > 25) result.Text($"… {driftRows.Count - 25} more.");
            }

            if (staleRows.Count > 0)
            {
                result.AddSection("STING TEMPLATES OUT OF DATE (run PNLS 📐)");
                foreach (string s in staleRows) result.Text(s);
            }
            if (specWarn.Count > 0 || unresolvedParams.Count > 0)
            {
                result.AddSection("TEMPLATE SPEC NOTES");
                foreach (string w in specWarn) result.Text(w);
                var un = unresolvedParams.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (un.Count > 0)
                    result.Text($"Not in this project, so not checked ({un.Count}): {string.Join(", ", un.Take(8))}{(un.Count > 8 ? ", …" : "")} — run Load Params.");
            }

            if (paramGapRows.Count > 0)
            {
                result.AddSection("MISSING PNL PARAMETERS");
                foreach (string s in paramGapRows.Take(25)) result.Text(s);
                if (paramGapRows.Count > 25) result.Text($"… {paramGapRows.Count - 25} more.");
            }

            result.AddSection("FIX")
                  .Text("Run 'Batch Panel Schedules' to create missing schedules + fill ELC_PNL_* parameters in one pass.")
                  .Text("Edit STING_PANEL_SCHEDULE_TEMPLATES.json (corporate) or <project>/_BIM_COORD/panel_schedule_templates.json (project override) to refine rules.")
                  .Text("Template drift is informational only — Revit's API cannot swap templates on existing PanelScheduleViews. Delete + recreate if a swap is needed.");

            try { ActionAuditLog.Record("PanelSchedule_Audit",
                $"panels={panelsTotal} withSched={withSchedule} drift={templateDrift} paramGaps={missingPnlParams}"); }
            catch (Exception ex) { StingLog.Warn($"audit: {ex.Message}"); }

            result.Show();
            return Result.Succeeded;
        }

        /// <summary>True when the parameter exists and holds a value, whatever its
        /// storage type. Text counts only when non-empty.</summary>
        private static bool HasParamValue(Element el, string name)
        {
            var prm = el?.LookupParameter(name);
            if (prm == null || !prm.HasValue) return false;
            return prm.StorageType != StorageType.String || !string.IsNullOrEmpty(prm.AsString());
        }
    }
}
