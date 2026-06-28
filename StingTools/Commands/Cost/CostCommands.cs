// ══════════════════════════════════════════════════════════════════════════
//  CostCommands.cs — Three user-facing cost-management commands (P2).
//
//  Cost_ValidateAll       — Runs the 5-validator chain, opens result panel.
//  Cost_ClearStale        — Clears ASS_CST_STALE_BOOL after a build.
//  Cost_RunWorkflow       — Picks a WORKFLOW_BOQ_*.json preset and runs it.
//  Cost_ToggleStaleMarker — Enables/disables the StingCostStaleMarker IUpdater.
//  Cost_MigrateCurrencyParams — One-time migration UGX→neutral params.
//  Cost_ReloadRules       — Invalidates the rate + take-off caches after
//                           editing JSON / CSV on disk.
//
//  Pattern follows the existing v4 command file conventions —
//  [Transaction(TransactionMode.Manual)] for mutating commands,
//  [Transaction(TransactionMode.ReadOnly)] for diagnostics.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.BIMManager;
using StingTools.BOQ;
using StingTools.BOQ.Rates;
using StingTools.BOQ.Takeoff;
using StingTools.Core;
using StingTools.Core.Storage;
using StingTools.Core.Validation;
using StingTools.Core.Validation.Cost;
using StingTools.Select;
using StingTools.UI;       // StingResultPanel

namespace StingTools.Commands.Cost
{
    // ──────────────────────────────────────────────────────────────────
    //  Cost_ValidateAll — Run the 5-validator chain.
    //  ReadOnly: no model writes. Surfaces results in a TaskDialog with
    //  copy-to-clipboard + select-affected actions.
    // ──────────────────────────────────────────────────────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CostValidateAllCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
                Document doc = uidoc?.Document;
                if (doc == null)
                {
                    message = "No active document.";
                    return Result.Failed;
                }

                var results = CostValidatorChain.RunAll(doc);
                ShowResults(uidoc, results);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Cost_ValidateAll", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static void ShowResults(UIDocument uidoc, List<ValidationResult> results)
        {
            int errors = results.Count(r => r.Severity == ValidationSeverity.Error);
            int warnings = results.Count(r => r.Severity == ValidationSeverity.Warning);
            int info = results.Count(r => r.Severity == ValidationSeverity.Info);

            string rag = errors > 0 ? "Red — exports blocked"
                : warnings > 5 ? "Amber — review before export"
                : "Green — clear to build";

            var byCode = results.GroupBy(r => r.Code)
                .OrderByDescending(g => g.Count())
                .Take(10);

            var rp = StingResultPanel.Create("Cost validation")
                .SetSubtitle(rag);
            rp.AddSection("SUMMARY")
                .Metric("Errors", errors.ToString())
                .Metric("Warnings", warnings.ToString())
                .Metric("Info", info.ToString());

            if (byCode.Any())
            {
                var codeRows = byCode.Select(g => new[] { g.Key ?? "", g.Count().ToString() }).ToList();
                rp.AddSection("TOP FINDINGS BY CODE")
                    .Table(new[] { "Code", "Count" }, codeRows);
            }

            if (results.Count > 0)
            {
                var issueRows = results.Take(10)
                    .Select(r => new[] { r.Severity.ToString(), r.Code ?? "", r.Message ?? "" })
                    .ToList();
                rp.AddSection("FIRST 10 ISSUES")
                    .Table(new[] { "Severity", "Code", "Message" }, issueRows);
            }

            // Select-affected action — rendered only on the dialog (ribbon) path;
            // the inline Actions pane ignores actions, surfacing just the report.
            if ((errors > 0 || warnings > 0) && uidoc != null)
            {
                rp.Action($"Select affected elements ({errors + warnings})",
                    "Highlight every element flagged by a validator.", _ =>
                    {
                        var ids = results
                            .Where(r => r.ElementId != null && r.ElementId.Value > 0)
                            .Select(r => r.ElementId)
                            .Distinct()
                            .ToList();
                        if (ids.Count > 0) uidoc.Selection.SetElementIds(ids);
                    });
            }
            rp.Show();
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Cost_ClearStale — Reset ASS_CST_STALE_BOOL across the model.
    //  Typically invoked at the end of WORKFLOW_BOQ_FullRefresh after
    //  BuildBOQDocument has re-priced every element.
    // ──────────────────────────────────────────────────────────────────
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CostClearStaleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null)
                {
                    message = "No active document.";
                    return Result.Failed;
                }

                int cleared = 0;
                using (var t = new Transaction(doc, "STING Cost — clear stale flags"))
                {
                    t.Start();
                    var col = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                    foreach (Element el in col)
                    {
                        int v = ParameterHelpers.GetInt(el, ParamRegistry.CST_STALE_BOOL, 0);
                        if (v != 1) continue;
                        try
                        {
                            Parameter p = el.LookupParameter(ParamRegistry.CST_STALE_BOOL);
                            if (p != null && !p.IsReadOnly) p.Set(0);
                            Parameter pr = el.LookupParameter(ParamRegistry.CST_STALE_REASON_TXT);
                            if (pr != null && !pr.IsReadOnly) pr.Set("");
                            cleared++;
                        }
                        catch (Exception ex) { StingLog.Warn($"Cost_ClearStale {el.Id}: {ex.Message}"); }
                    }
                    t.Commit();
                }
                StingCostStaleMarker.ResetRecentlyProcessed();
                StingResultPanel.Create("Clear stale flags")
                    .AddSection("RESULT")
                    .Metric("Stale flags cleared", cleared.ToString())
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Cost_ClearStale", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Cost_RunWorkflow — Discover WORKFLOW_BOQ_*.json presets, let the
    //  user pick one, hand off to WorkflowEngine.
    //  Manual transaction — the WorkflowEngine takes its own
    //  TransactionGroup so this command starts none.
    // ──────────────────────────────────────────────────────────────────
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CostRunWorkflowCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var presets = DiscoverBoqPresets();
                if (presets.Count == 0)
                {
                    StingResultPanel.Create("Run cost workflow")
                        .AddSection("NO PRESETS")
                        .Text("No WORKFLOW_BOQ_*.json presets found in data folder.")
                        .Show();
                    return Result.Cancelled;
                }

                // P0.3 — inline-form gate: when the BOQ panel supplied the
                // CostWorkflowPath ExtraParam, skip the preset picker (no popup).
                // Falls back to the modal picker for ribbon / other callers.
                string chosenPath;
                string fPath = UI.StingCommandHandler.GetExtraParam("CostWorkflowPath");
                if (!string.IsNullOrEmpty(fPath) && File.Exists(fPath))
                {
                    chosenPath = fPath;
                }
                else
                {
                    // Replaced the 4-link TaskDialog cap with StingListPicker
                    // so the picker scales as more BOQ workflow presets are
                    // authored. Each item's Tag holds the preset summary so
                    // we can round-trip the file path without re-parsing.
                    var items = presets.Select(p => new StingListPicker.ListItem
                    {
                        Label = p.Name ?? Path.GetFileNameWithoutExtension(p.Path),
                        Detail = string.IsNullOrEmpty(p.Description)
                            ? Path.GetFileName(p.Path)
                            : p.Description,
                        Tag = p
                    }).ToList();

                    var picked = StingListPicker.Show(
                        "STING — Run cost workflow",
                        "Pick a BOQ workflow preset to execute.",
                        items,
                        allowMultiSelect: false);

                    if (picked == null || picked.Count == 0) return Result.Cancelled;
                    var chosen = picked[0].Tag as PresetSummary;
                    if (chosen == null) return Result.Cancelled;
                    chosenPath = chosen.Path;
                }

                var preset = LoadPreset(chosenPath);
                if (preset == null)
                {
                    message = $"Failed to load preset: {chosenPath}";
                    return Result.Failed;
                }
                return WorkflowEngine.ExecutePreset(preset, commandData, elements);
            }
            catch (Exception ex)
            {
                StingLog.Error("Cost_RunWorkflow", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // P0.3 — internal so the BOQ panel can build the inline preset combo from the
        // same discovery (single source of truth — no forked enumeration).
        internal static List<PresetSummary> DiscoverBoqPresets()
        {
            var list = new List<PresetSummary>();
            try
            {
                string dataDir = StingToolsApp.DataPath;
                if (string.IsNullOrEmpty(dataDir) || !Directory.Exists(dataDir)) return list;
                foreach (string f in Directory.EnumerateFiles(dataDir, "WORKFLOW_BOQ_*.json"))
                {
                    try
                    {
                        var preset = JsonConvert.DeserializeObject<WorkflowPreset>(File.ReadAllText(f));
                        list.Add(new PresetSummary
                        {
                            Path = f,
                            Name = preset?.Name,
                            Description = preset?.Description
                        });
                    }
                    catch (Exception ex) { StingLog.Warn($"Cost_RunWorkflow load {Path.GetFileName(f)}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"DiscoverBoqPresets: {ex.Message}"); }
            return list.OrderBy(p => p.Name ?? p.Path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static WorkflowPreset LoadPreset(string path)
        {
            try { return JsonConvert.DeserializeObject<WorkflowPreset>(File.ReadAllText(path)); }
            catch (Exception ex) { StingLog.Warn($"Cost_RunWorkflow.LoadPreset: {ex.Message}"); return null; }
        }

        internal class PresetSummary
        {
            public string Path;
            public string Name;
            public string Description;
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Cost_ToggleStaleMarker — Enable/disable the IUpdater.
    // ──────────────────────────────────────────────────────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CostToggleStaleMarkerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            bool target = !StingCostStaleMarker.IsEnabled;
            StingCostStaleMarker.SetEnabled(target);
            StingResultPanel.Create("Toggle stale marker")
                .AddSection("RESULT")
                .Metric("Cost stale-marker", target ? "ENABLED" : "DISABLED")
                .Show();
            return Result.Succeeded;
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Cost_ReloadRules — Force-clear the rate provider + take-off rule
    //  caches so the next BOQ build re-reads cost_rates_5d.csv +
    //  STING_TAKEOFF_RULES.json + project overrides from disk.
    // ──────────────────────────────────────────────────────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CostReloadRulesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            RateProviderRegistry.Invalidate();
            TakeoffRuleRegistry.Invalidate();
            StingTools.BOQ.CostStamp.Invalidate();
            StingTools.BIMManager.Scheduling4DEngine.InvalidateDefaultCostRates();
            StingTools.BOQ.MeasurementStandard.Icms3PhaseMap.Invalidate();
            // Phase 2A — also drop the measurement rule + void caches.
            StingTools.BOQ.MeasurementStandard.MeasurementRuleRegistry.Invalidate();
            StingTools.BOQ.MeasurementStandard.MeasurementDeductionEngine.ResetCaches();
            // Phase 2D — rate / measure config changed; the incremental host cache
            // holds rows priced under the old config, so force a full rebuild next.
            BOQCostManager.InvalidateHostCache();

            // Phase 2B — external live-rate feeds (BCIS / Planscape) are now part
            // of the default build chain (RateProviderRegistry.Build →
            // AddConfiguredFeeds reads _BIM_COORD/rate_feeds.json). Invalidate
            // above is enough; the next Get rebuilds with the configured feeds.

            StingResultPanel.Create("Reload rules")
                .AddSection("CACHES CLEARED")
                .Text("Rate provider + take-off rule + measurement rule + default-cost-rates caches cleared " +
                      "(and CostStamp config). Live-rate feeds (BCIS / Planscape, if enabled in Rate feeds) " +
                      "re-attach on the next BOQ build, which reloads from disk.")
                .Show();
            return Result.Succeeded;
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Cost_MigrateCurrencyParams — One-time migration that populates the
    //  P0.2 currency-neutral params (ASS_CST_UNIT_RATE_NR /
    //  ASS_CST_CURRENCY_TXT / ASS_CST_FX_TO_BASE_NR / ASS_CST_FX_DATE_DT /
    //  ASS_CST_AS_OF_DT) from the legacy CST_UNIT_RATE_UGX value.
    //  Idempotent — re-runs are no-op when neutral params are already
    //  populated.
    // ──────────────────────────────────────────────────────────────────
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CostMigrateCurrencyParamsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                double fxRate = TagConfig.GetConfigDouble("UGX_PER_USD", 3700.0);
                string fxDate = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string asOf = fxDate;

                int migrated = 0, skipped = 0, missing = 0;

                using (var t = new Transaction(doc, "STING Cost — migrate currency params"))
                {
                    t.Start();
                    var col = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                    foreach (Element el in col)
                    {
                        try
                        {
                            // Read legacy UGX rate.
                            string legacy = ParameterHelpers.GetString(el, "CST_UNIT_RATE_UGX");
                            if (string.IsNullOrEmpty(legacy)) { missing++; continue; }
                            if (!double.TryParse(legacy, NumberStyles.Any,
                                    CultureInfo.InvariantCulture, out double ugx) || ugx <= 0)
                            {
                                missing++; continue;
                            }

                            // Idempotency check — skip if neutral rate already set.
                            Parameter neutral = el.LookupParameter(ParamRegistry.CST_UNIT_RATE_NR);
                            if (neutral != null && neutral.HasValue && Math.Abs(neutral.AsDouble()) > 0.0001)
                            { skipped++; continue; }

                            // Write neutral set.
                            ParameterHelpers.SetString(el, ParamRegistry.CST_CURRENCY_TXT, "UGX", overwrite: true);
                            if (neutral != null && !neutral.IsReadOnly) neutral.Set(ugx);
                            Parameter fx = el.LookupParameter(ParamRegistry.CST_FX_TO_BASE_NR);
                            if (fx != null && !fx.IsReadOnly) fx.Set(fxRate);
                            ParameterHelpers.SetString(el, ParamRegistry.CST_FX_DATE_DT, fxDate, overwrite: true);
                            ParameterHelpers.SetString(el, ParamRegistry.CST_AS_OF_DT, asOf, overwrite: true);
                            migrated++;
                        }
                        catch (Exception ex) { StingLog.Warn($"Cost_MigrateCurrencyParams {el.Id}: {ex.Message}"); }
                    }
                    t.Commit();
                }

                StingResultPanel.Create("Migrate UGX → Neutral")
                    .AddSection("MIGRATION")
                    .Metric("Migrated", migrated.ToString())
                    .Metric("Skipped (already set)", skipped.ToString())
                    .Metric("No legacy rate", missing.ToString())
                    .Metric("FX rate captured", $"{fxRate} UGX per USD", $"at {fxDate}")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Cost_MigrateCurrencyParams", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Cost_MigrateESEntities — Bulk-migrate v1 Extensible Storage cost
    //  overrides to v2. Each v1 entity is read, re-written via the v2
    //  schema (which auto-deletes the v1 entity), and counted. Idempotent
    //  — elements that already have a v2 entity are skipped; elements
    //  with no v1 entity are skipped.
    //
    //  Currently the v1→v2 migration is lazy: Read() consults v2 first
    //  and falls back to v1; Write() always emits v2. This command does
    //  the same work eagerly so a project can be guaranteed v2-only,
    //  enabling future cleanup of the v1 read path.
    // ──────────────────────────────────────────────────────────────────
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CostMigrateESEntitiesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                int migrated = 0, skipped = 0, errors = 0;
                // B.1 — project currency, not a hardcoded "GBP", for the v2 stamp.
                string ccy = BOQCostManager.BuildBOQDocument(doc)?.Currency ?? "UGX";

                // Schema lookups. If v1 was never loaded into this Revit
                // session there's nothing to migrate — return immediately.
                var v1 = Autodesk.Revit.DB.ExtensibleStorage.Schema.Lookup(
                    StingCostRateOverrideSchema.SchemaGuid);
                if (v1 == null)
                {
                    StingResultPanel.Create("Migrate ES v1 → v2")
                        .AddSection("NOTHING TO MIGRATE")
                        .Text("No v1 Extensible Storage schema present in this document. " +
                              "Either no overrides exist, or all are already v2.")
                        .Show();
                    return Result.Succeeded;
                }

                using (var t = new Transaction(doc, "STING Cost — migrate v1 ES entities to v2"))
                {
                    t.Start();
                    var col = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                    foreach (Element el in col)
                    {
                        try
                        {
                            // Direct v1 lookup — don't go through Read()
                            // which prefers v2 and would mask the v1
                            // entity from the count.
                            var entityV1 = el.GetEntity(v1);
                            if (entityV1 == null || !entityV1.IsValid()) continue;

                            // Read v1 fields directly.
                            double rate = entityV1.Get<double>("RateGbp");
                            string unit = entityV1.Get<string>("Unit") ?? "";
                            string note = entityV1.Get<string>("Note") ?? "";
                            // StampedUtcTicks and StampedBy are re-stamped
                            // by Write() — we accept that as part of the
                            // migration audit trail.

                            // Idempotent skip if v2 entity already exists
                            // (shouldn't happen because Write() deletes v1,
                            // but defensive).
                            var v2 = Autodesk.Revit.DB.ExtensibleStorage.Schema.Lookup(
                                StingCostRateOverrideSchema.SchemaGuidV2);
                            if (v2 != null)
                            {
                                var existingV2 = el.GetEntity(v2);
                                if (existingV2 != null && existingV2.IsValid())
                                {
                                    // Stale v1 alongside an existing v2 —
                                    // delete the v1 orphan and skip the write.
                                    el.DeleteEntity(v1);
                                    skipped++;
                                    continue;
                                }
                            }

                            // Re-write as v2. This deletes the v1 entity
                            // as a side-effect of the Write() implementation.
                            bool ok = StingCostRateOverrideSchema.Write(
                                el, rate, unit, ccy, note,
                                wastePercent: 0, overheadPercent: 0, profitPercent: 0,
                                dayworksCode: "", lockedByUser: "", lockedUntilUtcTicks: 0);
                            if (ok) migrated++;
                            else errors++;
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            StingLog.Warn($"Cost_MigrateESEntities {el?.Id}: {ex.Message}");
                        }
                    }
                    t.Commit();
                }

                StingResultPanel.Create("Migrate ES v1 → v2")
                    .SetSubtitle("v1 → v2 Extensible Storage migration complete")
                    .AddSection("MIGRATION")
                    .Metric("Migrated", migrated.ToString())
                    .Metric("Already v2 (orphan v1 deleted)", skipped.ToString())
                    .Metric("Errors", errors.ToString())
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Cost_MigrateESEntities", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Cost_RepriceDrift — Phase 2C. Re-runs the rate-provider chain (incl.
    //  the 2B live feeds) for the drifted element ids the panel passes via
    //  the RepriceElementIds ExtraParam, pinning the fresh rate via the
    //  model-override sidecar. Manual Override rows are left untouched.
    //  Runs on the Revit API thread (the provider chain reads element params).
    // ──────────────────────────────────────────────────────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CostRepriceDriftCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                string csv = UI.StingCommandHandler.GetExtraParam("RepriceElementIds") ?? "";
                var ids = csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => long.TryParse(s.Trim(), out long v) ? v : -1)
                    .Where(v => v > 0)
                    .ToList();

                if (ids.Count == 0)
                {
                    StingResultPanel.Create("Re-price changed lines")
                        .SetSubtitle("No changed lines to re-price.")
                        .Show();
                    return Result.Succeeded;
                }

                var outcome = BOQCostManager.RepriceElements(doc, ids);

                var rp = StingResultPanel.Create("Re-price changed lines")
                    .SetSubtitle($"Re-ran the rate chain (incl. live feeds) on {outcome.Considered} changed line(s).");
                rp.AddSection("RESULT")
                  .Metric("Re-priced", outcome.Repriced.ToString(CultureInfo.InvariantCulture))
                  .Metric("Unchanged", outcome.Unchanged.ToString(CultureInfo.InvariantCulture))
                  .Metric("Override (protected)", outcome.SkippedOverride.ToString(CultureInfo.InvariantCulture))
                  .Metric("No rate found", outcome.NoRate.ToString(CultureInfo.InvariantCulture));
                if (outcome.Rows.Count > 0)
                    rp.AddSection("RATE MOVES")
                      .Table(new[] { "Category", "Old UGX", "New UGX", "Source" }, outcome.Rows.Take(200).ToList());
                else
                    rp.AddSection("RATE MOVES").Text("No rate moved — the changed lines re-priced to the same value.");
                rp.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Cost_RepriceDrift", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
