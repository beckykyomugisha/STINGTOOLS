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
using StingTools.BOQ.Rates;
using StingTools.BOQ.Takeoff;
using StingTools.Core;
using StingTools.Core.Storage;
using StingTools.Core.Validation;
using StingTools.Core.Validation.Cost;
using StingTools.UI;

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
                UIDocument uidoc = commandData?.Application?.ActiveUIDocument;
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

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Cost validation — {rag}");
            sb.AppendLine($"Errors: {errors}   Warnings: {warnings}   Info: {info}");
            sb.AppendLine();
            sb.AppendLine("Top findings by code:");
            foreach (var g in byCode)
                sb.AppendLine($"  {g.Key,-22} {g.Count(),5}");
            sb.AppendLine();
            if (results.Count > 0)
                sb.AppendLine("First 10 issues:");
            foreach (var r in results.Take(10))
                sb.AppendLine($"  [{r.Severity}] {r.Code} — {r.Message}");

            var td = new TaskDialog("STING — Cost validation")
            {
                MainInstruction = $"Cost validation: {rag}",
                MainContent = sb.ToString(),
                CommonButtons = TaskDialogCommonButtons.Ok
            };
            if (errors > 0 || warnings > 0)
            {
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    $"Select affected elements ({errors + warnings})",
                    "Highlight every element flagged by a validator.");
            }
            var picked = td.Show();
            if (picked == TaskDialogResult.CommandLink1 && uidoc != null)
            {
                var ids = results
                    .Where(r => r.ElementId != null && r.ElementId.Value > 0)
                    .Select(r => r.ElementId)
                    .Distinct()
                    .ToList();
                if (ids.Count > 0) uidoc.Selection.SetElementIds(ids);
            }
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
                Document doc = commandData?.Application?.ActiveUIDocument?.Document;
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
                TaskDialog.Show("STING Cost", $"Cleared {cleared} stale flag(s).");
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
                Document doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var presets = DiscoverBoqPresets();
                if (presets.Count == 0)
                {
                    TaskDialog.Show("STING Cost — Workflows",
                        "No WORKFLOW_BOQ_*.json presets found in data folder.");
                    return Result.Cancelled;
                }

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

                var preset = LoadPreset(chosen.Path);
                if (preset == null)
                {
                    message = $"Failed to load preset: {chosen.Path}";
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

        private static List<PresetSummary> DiscoverBoqPresets()
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

        private class PresetSummary
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
            TaskDialog.Show("STING Cost", $"Cost stale-marker is now {(target ? "ENABLED" : "DISABLED")}.");
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
            TaskDialog.Show("STING Cost",
                "Rate provider + take-off rule + default-cost-rates caches cleared (and CostStamp config). " +
                "The next BOQ build will reload from disk.");
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
                Document doc = commandData?.Application?.ActiveUIDocument?.Document;
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

                TaskDialog.Show("STING Cost — migration",
                    $"Migrated:  {migrated}\nSkipped (already set):  {skipped}\nNo legacy rate:  {missing}\n\n" +
                    $"FX rate captured: {fxRate} UGX per USD at {fxDate}.");
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
                Document doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                int migrated = 0, skipped = 0, errors = 0;

                // Schema lookups. If v1 was never loaded into this Revit
                // session there's nothing to migrate — return immediately.
                var v1 = Autodesk.Revit.DB.ExtensibleStorage.Schema.Lookup(
                    StingCostRateOverrideSchema.SchemaGuid);
                if (v1 == null)
                {
                    TaskDialog.Show("STING Cost — migration",
                        "No v1 Extensible Storage schema present in this document. " +
                        "Either no overrides exist, or all are already v2.");
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
                                el, rate, unit, "GBP", note,
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

                TaskDialog.Show("STING Cost — migration",
                    $"v1 → v2 Extensible Storage migration complete.\n\n" +
                    $"Migrated:  {migrated}\nAlready v2 (orphan v1 deleted):  {skipped}\nErrors:  {errors}");
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
}
