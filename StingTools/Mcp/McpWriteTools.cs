// ════════════════════════════════════════════════════════════════════════════
// McpWriteTools — Tier-2 curated WRITE verbs (Phase 3a: exactly two)
//
//   set_parameter — standalone storage-type-aware parameter write.
//   auto_tag      — routes through McpEngineRegistry (the AutoTag engine), proving
//                   the shared engine path (selection/view = sync; project = async).
//
// Every write: Guard() first (license + document); dryRun → plan, mutates nothing;
// confirm gate on bulk/project; all mutation inside McpSafety.RunInTransactionGroup
// (rolls back on any uncaught exception); structured read-back {changed, skipped,
// errors[], sampleIds[]}; a one-line StingLog before/after summary; no modal UI.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.BOQ;
using StingTools.Core;

namespace StingTools.Mcp
{
    internal static class McpWriteTools
    {
        // ── get_job_status (poll an async write submitted by auto_tag project scope) ─

        public static McpCallResult GetJobStatus(JObject args)
        {
            var lic = McpSafety.RequireLicense();
            if (lic != null) return lic.ToCallResult();

            string jobId = args["jobId"]?.Value<string>()?.Trim();
            if (string.IsNullOrEmpty(jobId))
                return McpJobResult.Error("bad_args", "Missing required argument: jobId.").ToCallResult();

            if (!McpJobBridge.TryGetResult(jobId, out McpJobResult result))
                return McpJobResult.Error("not_found",
                    $"Unknown jobId '{jobId}'. It may have completed and been evicted (only the most recent jobs are retained).")
                    .ToCallResult();

            if (result == null)
                return McpJobResult.Success($"Job {jobId} is still running.",
                    new Dictionary<string, object> { ["jobId"] = jobId, ["status"] = "running" }).ToCallResult();

            // Completed → return the stored read-back verbatim.
            return result.ToCallResult();
        }

        // ── auto_tag ─────────────────────────────────────────────────────────────

        public static McpCallResult AutoTag(JObject args)
        {
            args = args ?? new JObject();
            string scope = args["scope"]?.Value<string>()?.Trim().ToLowerInvariant() ?? "view";
            if (scope != "selection" && scope != "view" && scope != "project")
                return McpJobResult.Error("bad_args", "scope must be one of: selection, view, project.").ToCallResult();

            var callArgs = (JObject)args.DeepClone();
            callArgs["scope"] = scope;
            // Named Tier-2 verbs bypass the allowlist — dispatch straight through the
            // shared engine registry (guardrails: dry-run, confirm, tx-group, sync/async).
            return McpEngineRegistry.DispatchWrite("AutoTag", callArgs).ToCallResult();
        }

        // ── tag_scheme_render ────────────────────────────────────────────────────

        public static McpCallResult TagSchemeRender(JObject args)
        {
            args = args ?? new JObject();
            string scope = args["scope"]?.Value<string>()?.Trim().ToLowerInvariant() ?? "view";
            if (scope != "selection" && scope != "view" && scope != "project")
                return McpJobResult.Error("bad_args", "scope must be one of: selection, view, project.").ToCallResult();

            var callArgs = (JObject)args.DeepClone();
            callArgs["scope"] = scope;
            return McpEngineRegistry.DispatchWrite("TagScheme_Render", callArgs).ToCallResult();
        }

        // ── export_boq ───────────────────────────────────────────────────────────
        //
        // File output (not model mutation), so it does NOT go through the engine
        // registry / TransactionGroup. Still license+document gated. BOQ builds can be
        // heavy on large models → run async (Submit) and poll get_job_status. Uses the
        // verified dialog-free path:
        //   BOQCostManager.BuildBOQDocument(doc)  +  BoqErpExporter.ExportCsv(boq, path)

        public static McpCallResult ExportBoq(JObject args)
        {
            args = args ?? new JObject();
            string format = args["format"]?.Value<string>()?.Trim().ToLowerInvariant() ?? "csv";
            if (format != "csv" && format != "xlsx")
                return McpJobResult.Error("bad_args", "format must be 'csv' or 'xlsx'.").ToCallResult();
            if (format == "xlsx")
                return McpJobResult.Error("no_engine_path",
                    "xlsx BOQ export has no dialog-free path yet (the workbook builder is UI-bound). " +
                    "Use format:'csv'. xlsx is on the dialog→engine backlog.").ToCallResult();

            string jobId = McpJobBridge.Submit(uiApp =>
            {
                var lic = McpSafety.RequireLicense();
                if (lic != null) return lic;
                var de = McpSafety.RequireDocument(uiApp);
                if (de != null) return de;
                Document doc = uiApp.ActiveUIDocument.Document;

                try
                {
                    BOQDocument boq = BOQCostManager.BuildBOQDocument(doc);
                    string path = OutputLocationHelper.GetTimestampedPath(doc, "STING_BOQ", ".csv");
                    BoqErpExporter.ExportCsv(boq, path);

                    int lines = boq?.AllItems?.Count ?? 0;
                    StingLog.Info($"MCP export_boq: {lines} line(s) → {path}");
                    return McpJobResult.Success(
                        $"BOQ exported ({lines} line(s)) to {Path.GetFileName(path)}.",
                        new Dictionary<string, object> { ["path"] = path, ["lines"] = lines, ["format"] = "csv" });
                }
                catch (Exception ex)
                {
                    StingLog.Error("MCP export_boq failed", ex);
                    return McpJobResult.Error("exception", ex.Message);
                }
            });

            return McpJobResult.Success("BOQ export started. Poll get_job_status with this jobId.",
                new Dictionary<string, object> { ["jobId"] = jobId, ["status"] = "running" }).ToCallResult();
        }

        // ── set_parameter ────────────────────────────────────────────────────────

        public static McpCallResult SetParameter(JObject args)
        {
            // Scoped id-set write → synchronous with the longer write timeout.
            return McpJobBridge.Run(uiApp =>
            {
                var lic = McpSafety.RequireLicense();
                if (lic != null) return lic;
                var de = McpSafety.RequireDocument(uiApp);
                if (de != null) return de;
                Document doc = uiApp.ActiveUIDocument.Document;

                if (!(args["ids"] is JArray idsArr) || idsArr.Count == 0)
                    return McpJobResult.Error("bad_args", "Missing required argument: ids (non-empty array of element ids).");
                string name = args["name"]?.Value<string>()?.Trim();
                if (string.IsNullOrEmpty(name))
                    return McpJobResult.Error("bad_args", "Missing required argument: name (parameter name).");
                JToken valueTok = args["value"];
                if (valueTok == null)
                    return McpJobResult.Error("bad_args", "Missing required argument: value.");
                string value = valueTok.Type == JTokenType.String ? valueTok.Value<string>() : valueTok.ToString();

                bool dryRun  = McpSafety.IsDryRun(args);
                bool confirm = McpSafety.IsConfirmed(args);

                var ids = idsArr.Select(t => t?.Value<long?>() ?? -1).Where(v => v >= 0).ToList();

                // Classify every id: writable candidate vs skipped (with reason).
                var candidates = new List<(Element el, Parameter p)>();
                var skipped = new List<string>();
                foreach (long v in ids)
                {
                    Element el = doc.GetElement(new ElementId(v));
                    if (el == null) { skipped.Add($"{v}: not found"); continue; }
                    Parameter p = el.LookupParameter(name);
                    if (p == null) { skipped.Add($"{v}: no parameter '{name}'"); continue; }
                    if (p.IsReadOnly) { skipped.Add($"{v}: '{name}' is read-only"); continue; }
                    if (!TagPipelineHelper.IsEditableInWorksharing(doc, el))
                    { skipped.Add($"{v}: locked by another user"); continue; }
                    candidates.Add((el, p));
                }

                var sampleIds = candidates.Take(25).Select(c => c.el.Id.Value).ToList();

                if (dryRun)
                {
                    var plan = new Dictionary<string, object>
                    {
                        ["status"]         = "dry_run",
                        ["parameter"]      = name,
                        ["value"]          = value,
                        ["plannedChanges"] = candidates.Count,
                        ["skipped"]        = skipped.Count,
                        ["skippedDetail"]  = skipped.Take(25).ToList(),
                        ["sampleIds"]      = sampleIds,
                    };
                    return McpJobResult.Success(
                        $"Dry run: would set '{name}' on {candidates.Count} element(s); {skipped.Count} skipped; nothing mutated.",
                        plan);
                }

                // Confirm gate — bulk write (> 25 ids) needs confirm:true.
                var confirmErr = McpSafety.RequireConfirmation(ids.Count, isProjectScope: false, confirmed: confirm);
                if (confirmErr != null) return confirmErr;

                int changed = 0;
                var errors = new List<string>(skipped);   // seed with skip reasons
                StingLog.Info($"MCP set_parameter: '{name}'='{value}' on {candidates.Count} candidate(s) " +
                              $"({skipped.Count} pre-skipped).");

                McpSafety.RunInTransactionGroup(doc, $"STING MCP set_parameter {name}", () =>
                {
                    using (var tx = new Transaction(doc, $"STING MCP set '{name}'"))
                    {
                        tx.Start();
                        foreach (var (el, p) in candidates)
                        {
                            try
                            {
                                if (ApplyValue(p, value, out string err)) changed++;
                                else errors.Add($"{el.Id.Value}: {err ?? "set failed"}");
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"{el.Id.Value}: {ex.Message}");
                                StingLog.Warn($"MCP set_parameter {el.Id.Value}: {ex.Message}");
                            }
                        }
                        tx.Commit();
                    }
                });

                StingLog.Info($"MCP set_parameter: '{name}' → {changed} changed, {skipped.Count} skipped, " +
                              $"{errors.Count - skipped.Count} write error(s).");

                var rb = McpSafety.WriteResult(changed, skipped.Count, errors, sampleIds);
                return McpJobResult.Success(
                    $"Set '{name}' on {changed} element(s); {skipped.Count} skipped.", rb);
            }, 60000).ToCallResult();
        }

        /// <summary>Storage-type-aware parameter set. Numeric (Double) values are
        /// interpreted as project display-unit strings via SetValueString first.</summary>
        private static bool ApplyValue(Parameter p, string value, out string err)
        {
            err = null;
            switch (p.StorageType)
            {
                case StorageType.String:
                    return p.Set(value ?? "");

                case StorageType.Integer:
                {
                    string lv = (value ?? "").Trim().ToLowerInvariant();
                    if (lv == "true" || lv == "yes" || lv == "1")  return p.Set(1);
                    if (lv == "false" || lv == "no" || lv == "0")  return p.Set(0);
                    if (int.TryParse(value, out int iv)) return p.Set(iv);
                    err = $"'{value}' is not an integer";
                    return false;
                }

                case StorageType.Double:
                {
                    // Display-unit string first (respects project units, e.g. "100" mm).
                    try { if (p.SetValueString(value)) return true; } catch { /* fall through */ }
                    if (double.TryParse(value, out double dv)) return p.Set(dv);   // internal-unit fallback
                    err = $"'{value}' is not numeric";
                    return false;
                }

                case StorageType.ElementId:
                    if (long.TryParse(value, out long lid)) return p.Set(new ElementId(lid));
                    err = $"'{value}' is not an element id";
                    return false;

                default:
                    err = $"unsupported storage type {p.StorageType}";
                    return false;
            }
        }
    }
}
