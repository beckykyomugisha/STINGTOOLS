using System;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.TemplateManager;

namespace StingTools.Commands.TemplateManager
{
    // ═════════════════════════════════════════════════════════════════
    //  v2 commands — every command publishes an OperationResult to the
    //  bus when the dashboard is listening, and falls back to TaskDialog
    //  when no subscriber is present (ribbon entry points).
    // ═════════════════════════════════════════════════════════════════

    internal static class V2CommandHelper
    {
        public static Result PublishOrFallback(string opTag, string label, Document doc,
            Func<OperationResult> compute)
        {
            var sw = Stopwatch.StartNew();
            OperationResult r;
            try { r = compute(); }
            catch (Exception ex)
            {
                r = new OperationResult
                {
                    Operation = opTag,
                    OperationLabel = label,
                    Severity = ResultSeverity.Error,
                    Headline = "Failed: " + ex.Message
                };
                StingLog.Warn($"{opTag}: {ex.Message}");
            }
            r.Operation = opTag;
            if (string.IsNullOrEmpty(r.OperationLabel)) r.OperationLabel = label;
            r.DurationMs = sw.Elapsed.TotalMilliseconds;
            r.DocumentPath = doc?.PathName ?? "";
            r.UserName = Environment.UserName;

            bool delivered = OperationResultBus.Publish(r);
            if (!delivered)
            {
                // Fall back to TaskDialog so the ribbon entry point stays useful
                try
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine(r.Headline);
                    foreach (var sec in r.Sections)
                    {
                        sb.AppendLine();
                        sb.AppendLine(sec.Name + ":  " + sec.Headline);
                        if (sec.Metrics != null) foreach (var (k, v) in sec.Metrics) sb.AppendLine($"  {k}: {v}");
                        if (sec.Notes != null) sb.AppendLine(sec.Notes);
                    }
                    TaskDialog.Show(label, sb.ToString());
                }
                catch (Exception ex) { StingLog.Warn($"PublishOrFallback TaskDialog: {ex.Message}"); }
            }
            return r.Severity == ResultSeverity.Error ? Result.Failed : Result.Succeeded;
        }
    }

    // ── Drift ────────────────────────────────────────────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DriftScanCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            return V2CommandHelper.PublishOrFallback("DriftScan", "Template Drift Scan", ctx.Doc, () =>
            {
                var drift = DriftDetector.Scan(ctx.Doc);
                var r = new OperationResult
                {
                    Severity = drift.Count == 0 ? ResultSeverity.Success : ResultSeverity.Warning,
                    Headline = drift.Count == 0
                        ? "No STING template drift detected."
                        : $"{drift.Count} drift entries found across STING templates."
                };
                var sec = r.AddSection("Drift entries", $"{drift.Count} entries");
                foreach (var d in drift.Take(200))
                {
                    sec.Rows.Add(new ResultRow
                    {
                        Name = d.TemplateName,
                        Status = d.Kind,
                        Detail = d.Detail,
                        Discipline = "",
                        RevitElementId = d.TemplateId
                    });
                }
                r.Counters["drift_count"] = drift.Count.ToString();
                return r;
            });
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DriftStampCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            int stamped = 0;
            using (var tx = new Transaction(ctx.Doc, "STING Drift Stamp"))
            {
                tx.Start();
                stamped = DriftDetector.StampAll(ctx.Doc);
                tx.Commit();
            }
            return V2CommandHelper.PublishOrFallback("DriftStamp", "Stamp Template Checksums", ctx.Doc, () =>
            {
                var r = new OperationResult
                {
                    Severity = stamped > 0 ? ResultSeverity.Success : ResultSeverity.Info,
                    Headline = $"{stamped} STING templates stamped with checksum."
                };
                r.Counters["created"] = stamped.ToString();
                return r;
            });
        }
    }

    // ── Snapshot ─────────────────────────────────────────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SnapshotCaptureCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            string path = SnapshotEngine.Capture(ctx.Doc, "manual");
            return V2CommandHelper.PublishOrFallback("SnapshotCapture", "Snapshot capture", ctx.Doc, () =>
            {
                var r = new OperationResult
                {
                    Severity = string.IsNullOrEmpty(path) ? ResultSeverity.Warning : ResultSeverity.Success,
                    Headline = string.IsNullOrEmpty(path)
                        ? "Snapshot failed — see log."
                        : "Snapshot saved.",
                    SubHeadline = path
                };
                return r;
            });
        }
    }

    // ── Audit verify ─────────────────────────────────────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AuditVerifyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            string projDir = System.IO.Path.GetDirectoryName(ctx.Doc?.PathName ?? "");
            string dir = string.IsNullOrEmpty(projDir) ? "" : System.IO.Path.Combine(projDir, "_BIM_COORD");
            string file = "";
            if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
            {
                file = System.IO.Directory.GetFiles(dir, "template_audit_log_*.jsonl")
                    .OrderByDescending(f => f).FirstOrDefault() ?? "";
            }
            return V2CommandHelper.PublishOrFallback("AuditVerify", "Audit log verification", ctx.Doc, () =>
            {
                bool ok = !string.IsNullOrEmpty(file) && AuditLog.VerifyChain(file);
                var r = new OperationResult
                {
                    Severity = ok ? ResultSeverity.Success : ResultSeverity.Error,
                    Headline = string.IsNullOrEmpty(file)
                        ? "No audit log found."
                        : (ok ? "Audit log chain verified." : "Audit log chain BROKEN — possible tampering."),
                    SubHeadline = file
                };
                return r;
            });
        }
    }

    // ── Library ──────────────────────────────────────────────────────
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LibraryPullCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            var pulled = CorporateLibrary.PullJsonAssets(ctx.Doc);
            return V2CommandHelper.PublishOrFallback("LibraryPull", "Library pull", ctx.Doc, () =>
            {
                var r = new OperationResult
                {
                    Severity = pulled.Count > 0 ? ResultSeverity.Success : ResultSeverity.Warning,
                    Headline = $"{pulled.Count} corporate JSON file(s) pulled into _BIM_COORD."
                };
                var sec = r.AddSection("Pulled files");
                foreach (var p in pulled) sec.Rows.Add(new ResultRow { Name = p, Status = "Pulled" });
                r.Counters["created"] = pulled.Count.ToString();
                return r;
            });
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LibraryPushCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            var pushed = CorporateLibrary.PushJsonAssets(ctx.Doc);
            return V2CommandHelper.PublishOrFallback("LibraryPush", "Library push", ctx.Doc, () =>
            {
                var r = new OperationResult
                {
                    Severity = pushed.Count > 0 ? ResultSeverity.Success : ResultSeverity.Warning,
                    Headline = $"{pushed.Count} JSON file(s) pushed to corporate library."
                };
                var sec = r.AddSection("Pushed files");
                foreach (var p in pushed) sec.Rows.Add(new ResultRow { Name = p, Status = "Pushed" });
                return r;
            });
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LibraryConfigureCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            return V2CommandHelper.PublishOrFallback("LibraryConfigure", "Library config", ctx.Doc, () =>
            {
                var cfg = CorporateLibrary.LoadGlobal();
                string path = CorporateLibrary.ResolveLibraryPath(ctx.Doc);
                string ver = CorporateLibrary.ResolveVersionStamp(ctx.Doc);
                var r = new OperationResult
                {
                    Severity = ResultSeverity.Info,
                    Headline = string.IsNullOrEmpty(path) ? "No library configured." : $"Library: {path}"
                };
                var sec = r.AddSection("Config");
                sec.Metrics.Add(("Path", string.IsNullOrEmpty(path) ? "(none)" : path));
                sec.Metrics.Add(("Channel", cfg.Channel ?? "stable"));
                sec.Metrics.Add(("Version stamp", string.IsNullOrEmpty(ver) ? "(none)" : ver));
                sec.Metrics.Add(("Last synced", cfg.LastSynced.ToString("u")));
                return r;
            });
        }
    }

    // ── Cross-engine browsers ────────────────────────────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AecFiltersBrowseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            return V2CommandHelper.PublishOrFallback("AecFiltersBrowse", "AEC Filter Library", ctx.Doc, () =>
            {
                var preview = CrossEngineFacade.AecFiltersPreview(ctx.Doc);
                var r = new OperationResult
                {
                    Severity = ResultSeverity.Info,
                    Headline = preview.Summary
                };
                var sec = r.AddSection("Filters");
                foreach (var row in preview.Rows.Take(50))
                {
                    sec.Rows.Add(new ResultRow
                    {
                        Name = row.Name,
                        Status = row.Exists ? "Exists" : "Missing",
                        Discipline = row.Discipline,
                        Detail = row.Detail
                    });
                }
                return r;
            });
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ViewStylePacksBrowseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            return V2CommandHelper.PublishOrFallback("ViewStylePacksBrowse", "View Style Packs", ctx.Doc, () =>
            {
                var preview = CrossEngineFacade.ViewStylePacksPreview(ctx.Doc);
                var r = new OperationResult
                {
                    Severity = ResultSeverity.Info,
                    Headline = preview.Summary
                };
                var sec = r.AddSection("Packs");
                foreach (var row in preview.Rows)
                {
                    sec.Rows.Add(new ResultRow
                    {
                        Name = row.Name,
                        Status = row.Category,
                        Detail = row.Detail
                    });
                }
                return r;
            });
        }
    }
}
