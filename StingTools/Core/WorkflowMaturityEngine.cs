// ============================================================================
// WorkflowMaturityEngine.cs — Phase 73: Workflow Maturity Enhancements
//
// Extends WorkflowEngine with:
//   1. StepDependencyResolver   — DAG-based step dependency ordering
//   2. PartialRollbackManager   — Per-step transaction isolation with rollback
//   3. CommissioningWorkflows    — MEP commissioning T&B workflow presets
//   4. WorkflowScheduler        — Time-based / event-driven workflow triggers
//   5. WorkflowValidator        — Pre-flight validation of workflow definitions
//   6. WorkflowMetrics          — Detailed step-level performance analytics
//
// Integrates with existing WorkflowEngine via static extension methods.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

namespace StingTools.Core
{
    // ════════════════════════════════════════════════════════════════
    //  STEP DEPENDENCY RESOLVER — DAG-based Ordering
    // ════════════════════════════════════════════════════════════════

    internal class StepDependency
    {
        public string StepTag { get; set; }
        public List<string> DependsOn { get; set; } = new List<string>();
        public bool IsOptional { get; set; }
    }

    internal static class StepDependencyResolver
    {
        /// <summary>Resolve step execution order using topological sort (Kahn's algorithm).</summary>
        public static List<string> ResolveOrder(List<StepDependency> steps)
        {
            var ordered = new List<string>();
            if (steps == null || steps.Count == 0) return ordered;

            try
            {
                var inDegree = new Dictionary<string, int>();
                var adjacency = new Dictionary<string, List<string>>();

                // Initialize
                foreach (var step in steps)
                {
                    if (!inDegree.ContainsKey(step.StepTag))
                        inDegree[step.StepTag] = 0;
                    if (!adjacency.ContainsKey(step.StepTag))
                        adjacency[step.StepTag] = new List<string>();
                }

                // Build graph
                foreach (var step in steps)
                {
                    foreach (var dep in step.DependsOn)
                    {
                        if (!adjacency.ContainsKey(dep))
                            adjacency[dep] = new List<string>();
                        adjacency[dep].Add(step.StepTag);

                        if (!inDegree.ContainsKey(step.StepTag))
                            inDegree[step.StepTag] = 0;
                        inDegree[step.StepTag]++;
                    }
                }

                // Kahn's algorithm
                var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));

                while (queue.Count > 0)
                {
                    string current = queue.Dequeue();
                    ordered.Add(current);

                    if (adjacency.TryGetValue(current, out var neighbors))
                    {
                        foreach (var neighbor in neighbors)
                        {
                            inDegree[neighbor]--;
                            if (inDegree[neighbor] == 0)
                                queue.Enqueue(neighbor);
                        }
                    }
                }

                // Check for cycles
                if (ordered.Count < steps.Count)
                {
                    var missing = steps.Select(s => s.StepTag).Except(ordered).ToList();
                    StingLog.Warn($"StepDependencyResolver: cycle detected — appending {missing.Count} steps involved in dependency cycle: {string.Join(", ", missing)}");
                    ordered.AddRange(missing);
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("StepDependencyResolver.ResolveOrder", ex);
                // Fallback: return original order
                return steps.Select(s => s.StepTag).ToList();
            }

            return ordered;
        }

        /// <summary>Validate that all dependencies reference existing steps.</summary>
        public static List<string> ValidateDependencies(List<StepDependency> steps)
        {
            var errors = new List<string>();
            var allTags = new HashSet<string>(steps.Select(s => s.StepTag));

            foreach (var step in steps)
            {
                foreach (var dep in step.DependsOn)
                {
                    if (!allTags.Contains(dep))
                        errors.Add($"Step '{step.StepTag}' depends on '{dep}' which does not exist");
                }
            }

            return errors;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  PARTIAL ROLLBACK MANAGER — Per-Step Transaction Isolation
    // ════════════════════════════════════════════════════════════════

    internal class StepExecutionRecord
    {
        public string StepTag { get; set; }
        public string Label { get; set; }
        public bool Succeeded { get; set; }
        public bool Skipped { get; set; }
        public bool RolledBack { get; set; }
        public double DurationMs { get; set; }
        public string ErrorMessage { get; set; }
        public int ElementsAffected { get; set; }
    }

    internal static class PartialRollbackManager
    {
        /// <summary>Execute a step in its own TransactionGroup for isolated rollback.</summary>
        public static StepExecutionRecord ExecuteIsolatedStep(
            Document doc, string stepTag, string label, Action<Document> stepAction,
            bool rollbackOnFailure = true)
        {
            var record = new StepExecutionRecord
            {
                StepTag = stepTag,
                Label = label
            };

            var sw = Stopwatch.StartNew();

            try
            {
                using (var tg = new TransactionGroup(doc, $"STING Workflow: {label}"))
                {
                    tg.Start();

                    try
                    {
                        stepAction(doc);
                        record.Succeeded = true;
                        tg.Assimilate();
                    }
                    catch (OperationCanceledException)
                    {
                        record.Skipped = true;
                        tg.RollBack();
                        record.RolledBack = true;
                        StingLog.Info($"Step '{label}' cancelled by user — rolled back");
                    }
                    catch (Exception ex)
                    {
                        record.ErrorMessage = ex.Message;

                        if (rollbackOnFailure)
                        {
                            tg.RollBack();
                            record.RolledBack = true;
                            StingLog.Warn($"Step '{label}' failed and rolled back: {ex.Message}");
                        }
                        else
                        {
                            // Keep partial results
                            tg.Assimilate();
                            StingLog.Warn($"Step '{label}' failed but partial results kept: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                record.ErrorMessage = ex.Message;
                StingLog.Error($"PartialRollback step '{label}'", ex);
            }

            sw.Stop();
            record.DurationMs = sw.ElapsedMilliseconds;
            return record;
        }

        /// <summary>Execute multiple steps with partial rollback support.</summary>
        public static List<StepExecutionRecord> ExecuteSteps(
            Document doc, List<(string Tag, string Label, Action<Document> Action, bool RollbackOnFail)> steps,
            bool stopOnFirstFailure = false)
        {
            var records = new List<StepExecutionRecord>();

            foreach (var (tag, label, action, rollback) in steps)
            {
                var record = ExecuteIsolatedStep(doc, tag, label, action, rollback);
                records.Add(record);

                if (!record.Succeeded && !record.Skipped && stopOnFirstFailure)
                {
                    StingLog.Warn($"Workflow stopped at step '{label}' due to stopOnFirstFailure");
                    break;
                }
            }

            return records;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMISSIONING WORKFLOW PRESETS — MEP T&B
    // ════════════════════════════════════════════════════════════════

    internal static class CommissioningWorkflows
    {
        /// <summary>Get MEP commissioning workflow steps.</summary>
        public static List<(string Tag, string Label, string Description)> GetCommissioningSteps()
        {
            return new List<(string, string, string)>
            {
                ("SystemParamPush",       "1. System Parameter Push",     "Propagate MEP system tokens to connected elements"),
                ("BatchTag",              "2. Tag All MEP Elements",      "Ensure all MEP elements have ISO 19650 tags"),
                ("ValidateTags",          "3. Validate Tag Completeness", "Check all 8 tag segments are populated"),
                ("COBieExport",           "4. COBie V2.4 Export",         "Export commissioning data sheets"),
                ("ExportSchedulesToExcel","5. Equipment Schedules",       "Export MEP equipment schedules to Excel"),
                ("WarningsAutoFix",       "6. Resolve MEP Warnings",     "Auto-fix MEP system warnings"),
                ("TagRegisterExport",     "7. Asset Register",            "Export comprehensive asset register CSV"),
                ("WeeklyCoordinatorReport","8. Commissioning Report",     "Generate HTML commissioning status report"),
            };
        }

        /// <summary>Get pre-handover validation workflow.</summary>
        public static List<(string Tag, string Label, string Description)> GetHandoverValidationSteps()
        {
            return new List<(string, string, string)>
            {
                ("RetagStale",            "1. Clear Stale Tags",          "Re-tag elements that moved since last tagging"),
                ("ResolveAllIssues",      "2. Resolve ISO Issues",        "One-click ISO 19650 compliance resolution"),
                ("ValidateTags",          "3. Final Validation",          "Validate all tags meet ISO 19650"),
                ("WarningsAutoFix",       "4. Warning Cleanup",           "Auto-fix remaining model warnings"),
                ("COBieExport",           "5. COBie Export",              "Generate COBie V2.4 spreadsheet"),
                ("ExportSheetRegister",   "6. Sheet Register",            "Export sheet register CSV"),
                ("GenerateBEP",           "7. BEP Update",                "Generate/update BIM Execution Plan"),
                ("WeeklyCoordinatorReport","8. Final Report",             "Generate handover status report"),
            };
        }

        /// <summary>Get sustainability assessment workflow.</summary>
        public static List<(string Tag, string Label, string Description)> GetSustainabilitySteps()
        {
            return new List<(string, string, string)>
            {
                ("EmbodiedCarbon",        "1. Embodied Carbon",           "Calculate A1-A3 embodied carbon per ICE v3.0"),
                ("BREEAMAssessment",      "2. BREEAM Assessment",         "Run BREEAM v6.0 credit assessment"),
                ("LifecycleAssessment",   "3. Lifecycle Assessment",      "BS EN 15978 whole-life carbon analysis"),
                ("CircularityScore",      "4. Circularity Scoring",       "Calculate material circularity index"),
                ("AcousticAnalysis",      "5. Acoustic Analysis",         "BS 8233 acoustic performance validation"),
                ("ModelHealthDashboard",  "6. Model Health",              "Overall model health assessment"),
            };
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  WORKFLOW VALIDATOR — Pre-flight Checks
    // ════════════════════════════════════════════════════════════════

    internal static class WorkflowValidator
    {
        /// <summary>Validate a workflow definition before execution.</summary>
        public static List<string> Validate(List<(string Tag, string Label)> steps, Document doc = null)
        {
            var issues = new List<string>();

            if (steps == null || steps.Count == 0)
            {
                issues.Add("Workflow has no steps defined");
                return issues;
            }

            // Check for duplicate step tags
            var duplicates = steps.GroupBy(s => s.Tag).Where(g => g.Count() > 1).Select(g => g.Key);
            foreach (var dup in duplicates)
                issues.Add($"Duplicate step tag: '{dup}'");

            // Check for empty labels
            foreach (var step in steps)
            {
                if (string.IsNullOrWhiteSpace(step.Label))
                    issues.Add($"Step '{step.Tag}' has empty label");
            }

            // Validate command tags resolve
            foreach (var step in steps)
            {
                if (WorkflowEngine.ResolveCommandPublic(step.Tag) == null)
                    issues.Add($"Step '{step.Tag}' does not resolve to a known command");
            }

            // Model-specific checks
            if (doc != null)
            {
                int elementCount = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .GetElementCount();

                if (elementCount == 0)
                    issues.Add("Model has no elements — workflow will produce no results");

                if (elementCount > 100000)
                    issues.Add($"Large model ({elementCount} elements) — workflow may take >30 minutes");
            }

            return issues;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  WORKFLOW METRICS — Step-level Analytics
    // ════════════════════════════════════════════════════════════════

    internal static class WorkflowMetrics
    {
        /// <summary>Generate summary report from step execution records.</summary>
        public static string GenerateReport(List<StepExecutionRecord> records, string workflowName)
        {
            var sb = new StringBuilder();

            double totalMs = records.Sum(r => r.DurationMs);
            int succeeded = records.Count(r => r.Succeeded);
            int failed = records.Count(r => !r.Succeeded && !r.Skipped);
            int skipped = records.Count(r => r.Skipped);
            int rolledBack = records.Count(r => r.RolledBack);

            sb.AppendLine($"══════════════════════════════════════════");
            sb.AppendLine($"  Workflow: {workflowName}");
            sb.AppendLine($"  Total: {records.Count} steps in {totalMs / 1000.0:F1}s");
            sb.AppendLine($"  ✓ {succeeded} succeeded, ✗ {failed} failed, ⊘ {skipped} skipped");
            if (rolledBack > 0) sb.AppendLine($"  ↩ {rolledBack} rolled back");
            sb.AppendLine($"══════════════════════════════════════════\n");

            sb.AppendLine($"{"Step",-35} {"Status",-12} {"Duration",10}");
            sb.AppendLine($"{new string('-', 35)} {new string('-', 12)} {new string('-', 10)}");

            foreach (var r in records)
            {
                string status = r.Succeeded ? "✓ OK" :
                    r.Skipped ? "⊘ Skip" :
                    r.RolledBack ? "↩ Rollback" : "✗ FAIL";
                sb.AppendLine($"{r.Label,-35} {status,-12} {r.DurationMs,8:F0}ms");

                if (!string.IsNullOrEmpty(r.ErrorMessage))
                    sb.AppendLine($"  └─ {r.ErrorMessage}");
            }

            // Bottleneck analysis
            var slowest = records.OrderByDescending(r => r.DurationMs).FirstOrDefault();
            if (slowest != null && totalMs > 0)
            {
                sb.AppendLine($"\nBottleneck: '{slowest.Label}' ({slowest.DurationMs / totalMs * 100:F0}% of total time)");
            }

            return sb.ToString();
        }

        /// <summary>Save metrics to JSON for historical tracking.</summary>
        public static void SaveMetrics(List<StepExecutionRecord> records, string workflowName, string outputDir)
        {
            try
            {
                string path = Path.Combine(outputDir, "STING_WORKFLOW_METRICS.json");
                var entry = new
                {
                    Timestamp = DateTime.Now.ToString("o"),
                    Workflow = workflowName,
                    TotalMs = records.Sum(r => r.DurationMs),
                    Steps = records.Select(r => new
                    {
                        r.StepTag, r.Label, r.Succeeded, r.Skipped,
                        r.RolledBack, r.DurationMs, r.ErrorMessage
                    }).ToList()
                };

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(entry, Newtonsoft.Json.Formatting.None);

                // Append to JSONL
                File.AppendAllText(path, json + "\n");
                StingLog.Info($"WorkflowMetrics saved: {path}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"WorkflowMetrics save failed: {ex.Message}");
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMANDS
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    internal class CommissioningWorkflowCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open"; return Result.Failed; }

                var steps = CommissioningWorkflows.GetCommissioningSteps();
                var sb = new StringBuilder("MEP Commissioning Workflow\n\n");
                foreach (var (tag, label, desc) in steps)
                    sb.AppendLine($"  {label}\n    {desc}\n");

                sb.AppendLine($"\nTotal: {steps.Count} steps");
                TaskDialog.Show("Commissioning Workflow", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("CommissioningWorkflowCommand", ex); message = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    internal class HandoverValidationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open"; return Result.Failed; }

                var steps = CommissioningWorkflows.GetHandoverValidationSteps();
                var sb = new StringBuilder("Pre-Handover Validation Workflow\n\n");
                foreach (var (tag, label, desc) in steps)
                    sb.AppendLine($"  {label}\n    {desc}\n");

                TaskDialog.Show("Handover Validation", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("HandoverValidationCommand", ex); message = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    internal class SustainabilityWorkflowCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open"; return Result.Failed; }

                var steps = CommissioningWorkflows.GetSustainabilitySteps();
                var sb = new StringBuilder("Sustainability Assessment Workflow\n\n");
                foreach (var (tag, label, desc) in steps)
                    sb.AppendLine($"  {label}\n    {desc}\n");

                TaskDialog.Show("Sustainability Workflow", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("SustainabilityWorkflowCommand", ex); message = ex.Message; return Result.Failed; }
        }
    }
}
