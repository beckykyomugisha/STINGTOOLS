// ============================================================================
// Phase75Enhancements.cs — 29 Workflow/Coordination Gap Implementations
//
// Implements all remaining gaps from Phase 74 deep review Agent 3:
//   WORKFLOW ENGINE:   WF-01..WF-05 (scheduled triggers, federated, adaptive,
//                      step output chaining, exception recovery)
//   WARNINGS MANAGER:  WM-01..WM-03 (fix categorization, root-cause graph,
//                      suppression audit trail)
//   BIM COORDINATION:  CC-01,03,04,05,06 (auto-refresh, team, trends,
//                      smart sequencing, role-based gating)
//   EVENT-DRIVEN:      ED-02..ED-04 (issue-triggered workflow, workset change,
//                      SLA monitoring)
//   CROSS-SYSTEM:      CSI-01..CSI-04 (warning→issue dedup, container↔warning,
//                      transmittal gating, approval↔CDE)
//   EFFICIENCY:        EF-01..EF-04 (async data, classification cache,
//                      command resolution cache, multi-threaded assembly)
//   ISO 19650:         ISO-01..ISO-03 (CDE enforcement, approval hierarchy,
//                      IM classification)
//   COORDINATOR:       CW-01,03,04 (mid-day workflow, cost/schedule impact,
//                      review prep workflow)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    // ════════════════════════════════════════════════════════════════
    //  WF-01: WORKFLOW SCHEDULER — Scheduled & event-driven triggers
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Manages scheduled and event-driven workflow triggers.
    /// Supports: document-open auto-execution, compliance-fall triggers,
    /// SLA-violation auto-workflows, periodic sync cycles.
    /// Persists schedules to project_config.json WORKFLOW_SCHEDULES section.
    /// </summary>
    internal static class WorkflowScheduler
    {
        private static readonly object _lock = new object();
        private static readonly List<ScheduledTrigger> _triggers = new();

        /// <summary>Trigger types for scheduled workflow execution.</summary>
        internal enum TriggerType
        {
            OnDocumentOpen,         // Run when document opens
            OnComplianceFall,       // Run when compliance drops below threshold
            OnSLAViolation,         // Run when issue SLA is violated
            OnWarningThreshold,     // Run when warning count exceeds limit
            Periodic                // Run at fixed interval (minutes)
        }

        /// <summary>A scheduled workflow trigger definition.</summary>
        internal class ScheduledTrigger
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
            public TriggerType Type { get; set; }
            public string PresetName { get; set; }
            public double Threshold { get; set; }    // Compliance %, warning count, or interval minutes
            public bool Enabled { get; set; } = true;
            public DateTime? LastTriggered { get; set; }
            public string CreatedBy { get; set; } = Environment.UserName;
            public DateTime CreatedDate { get; set; } = DateTime.Now;
        }

        /// <summary>Register a new workflow trigger.</summary>
        public static void AddTrigger(ScheduledTrigger trigger)
        {
            lock (_lock) { _triggers.Add(trigger); }
            StingLog.Info($"WorkflowScheduler: added {trigger.Type} trigger for '{trigger.PresetName}'");
        }

        /// <summary>Remove a trigger by ID.</summary>
        public static bool RemoveTrigger(string triggerId)
        {
            lock (_lock) { return _triggers.RemoveAll(t => t.Id == triggerId) > 0; }
        }

        /// <summary>Get all registered triggers.</summary>
        public static List<ScheduledTrigger> GetTriggers()
        {
            lock (_lock) { return new List<ScheduledTrigger>(_triggers); }
        }

        /// <summary>WF-01: Check and fire document-open triggers.</summary>
        public static void CheckDocumentOpenTriggers(Document doc)
        {
            if (doc == null) return;
            var openTriggers = GetTriggers()
                .Where(t => t.Enabled && t.Type == TriggerType.OnDocumentOpen).ToList();
            foreach (var trigger in openTriggers)
            {
                StingLog.Info($"WorkflowScheduler: document-open trigger firing '{trigger.PresetName}'");
                trigger.LastTriggered = DateTime.Now;
                // Queue for execution — actual execution happens via ExternalEvent
                _pendingPresets.Enqueue(trigger.PresetName);
            }
        }

        /// <summary>WF-01/ED-01: Check compliance-fall triggers after tagging operations.</summary>
        public static void CheckComplianceFallTriggers(Document doc, double currentCompliance)
        {
            if (doc == null) return;
            var fallTriggers = GetTriggers()
                .Where(t => t.Enabled && t.Type == TriggerType.OnComplianceFall).ToList();
            foreach (var trigger in fallTriggers)
            {
                if (currentCompliance < trigger.Threshold)
                {
                    // Only fire if not triggered in last 5 minutes (debounce)
                    if (trigger.LastTriggered.HasValue &&
                        (DateTime.Now - trigger.LastTriggered.Value).TotalMinutes < 5)
                        continue;
                    StingLog.Info($"WorkflowScheduler: compliance fall trigger firing '{trigger.PresetName}' (compliance={currentCompliance:F1}% < threshold={trigger.Threshold:F1}%)");
                    trigger.LastTriggered = DateTime.Now;
                    _pendingPresets.Enqueue(trigger.PresetName);
                }
            }
        }

        /// <summary>ED-04: Check SLA violation triggers.</summary>
        public static void CheckSLATriggers(Document doc, int slaViolationCount)
        {
            if (doc == null || slaViolationCount == 0) return;
            var slaTriggers = GetTriggers()
                .Where(t => t.Enabled && t.Type == TriggerType.OnSLAViolation).ToList();
            foreach (var trigger in slaTriggers)
            {
                if (slaViolationCount >= (int)trigger.Threshold)
                {
                    if (trigger.LastTriggered.HasValue &&
                        (DateTime.Now - trigger.LastTriggered.Value).TotalMinutes < 30)
                        continue;
                    StingLog.Info($"WorkflowScheduler: SLA violation trigger firing '{trigger.PresetName}' ({slaViolationCount} violations)");
                    trigger.LastTriggered = DateTime.Now;
                    _pendingPresets.Enqueue(trigger.PresetName);
                }
            }
        }

        /// <summary>Check warning threshold triggers.</summary>
        public static void CheckWarningThresholdTriggers(Document doc, int warningCount)
        {
            if (doc == null) return;
            var warnTriggers = GetTriggers()
                .Where(t => t.Enabled && t.Type == TriggerType.OnWarningThreshold).ToList();
            foreach (var trigger in warnTriggers)
            {
                if (warningCount >= (int)trigger.Threshold)
                {
                    if (trigger.LastTriggered.HasValue &&
                        (DateTime.Now - trigger.LastTriggered.Value).TotalMinutes < 15)
                        continue;
                    StingLog.Info($"WorkflowScheduler: warning threshold trigger firing '{trigger.PresetName}' ({warningCount} warnings >= {trigger.Threshold})");
                    trigger.LastTriggered = DateTime.Now;
                    _pendingPresets.Enqueue(trigger.PresetName);
                }
            }
        }

        // Queue of preset names pending execution
        private static readonly ConcurrentQueue<string> _pendingPresets = new();

        /// <summary>Dequeue next pending preset name, or null if empty.</summary>
        public static string DequeuePendingPreset() =>
            _pendingPresets.TryDequeue(out var name) ? name : null;

        /// <summary>Whether there are pending presets to execute.</summary>
        public static bool HasPendingPresets => !_pendingPresets.IsEmpty;

        /// <summary>Load triggers from project_config.json.</summary>
        public static void LoadFromConfig(string configPath)
        {
            try
            {
                if (!File.Exists(configPath)) return;
                var json = JObject.Parse(File.ReadAllText(configPath));
                var schedules = json["WORKFLOW_SCHEDULES"] as JArray;
                if (schedules == null) return;
                lock (_lock)
                {
                    _triggers.Clear();
                    foreach (var item in schedules)
                    {
                        var trigger = new ScheduledTrigger
                        {
                            Id = item["Id"]?.ToString() ?? Guid.NewGuid().ToString("N").Substring(0, 8),
                            Type = Enum.TryParse<TriggerType>(item["Type"]?.ToString(), out var tt) ? tt : TriggerType.OnDocumentOpen,
                            PresetName = item["PresetName"]?.ToString() ?? "",
                            Threshold = item["Threshold"]?.Value<double>() ?? 0,
                            Enabled = item["Enabled"]?.Value<bool>() ?? true,
                            CreatedBy = item["CreatedBy"]?.ToString() ?? "",
                        };
                        _triggers.Add(trigger);
                    }
                }
                StingLog.Info($"WorkflowScheduler: loaded {_triggers.Count} triggers from config");
            }
            catch (Exception ex) { StingLog.Warn($"WorkflowScheduler.LoadFromConfig: {ex.Message}"); }
        }

        /// <summary>Save triggers to project_config.json.</summary>
        public static void SaveToConfig(string configPath)
        {
            try
            {
                JObject json;
                if (File.Exists(configPath))
                    json = JObject.Parse(File.ReadAllText(configPath));
                else
                    json = new JObject();
                var arr = new JArray();
                lock (_lock)
                {
                    foreach (var t in _triggers)
                    {
                        arr.Add(new JObject
                        {
                            ["Id"] = t.Id,
                            ["Type"] = t.Type.ToString(),
                            ["PresetName"] = t.PresetName,
                            ["Threshold"] = t.Threshold,
                            ["Enabled"] = t.Enabled,
                            ["CreatedBy"] = t.CreatedBy,
                        });
                    }
                }
                json["WORKFLOW_SCHEDULES"] = arr;
                File.WriteAllText(configPath, json.ToString(Formatting.Indented));
                StingLog.Info($"WorkflowScheduler: saved {arr.Count} triggers to config");
            }
            catch (Exception ex) { StingLog.Warn($"WorkflowScheduler.SaveToConfig: {ex.Message}"); }
        }

        /// <summary>Reset all triggers (document close).</summary>
        public static void Reset()
        {
            lock (_lock) { _triggers.Clear(); }
            while (_pendingPresets.TryDequeue(out _)) { }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  WF-02: FEDERATED WORKFLOW SUPPORT — Multi-model validation
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pre-flight and compliance checking across host + linked models.
    /// Extends WorkflowEngine.PreFlightCheck with federated validation.
    /// </summary>
    internal static class FederatedWorkflowSupport
    {
        /// <summary>WF-02: Pre-flight check that includes linked models.</summary>
        public static (bool canProceed, List<string> issues) PreFlightCheckFederated(Document doc, WorkflowPreset preset)
        {
            // Start with standard pre-flight
            var (canProceed, issues) = WorkflowEngine.PreFlightCheck(doc, preset);

            if (doc == null) return (false, issues);

            try
            {
                // Check linked model element counts and compliance
                var links = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>().ToList();

                if (links.Count == 0) return (canProceed, issues);

                int totalFederatedElements = 0;
                double weightedCompliance = 0;
                int totalWeight = 0;

                foreach (var link in links)
                {
                    try
                    {
                        var linkDoc = link.GetLinkDocument();
                        if (linkDoc == null)
                        {
                            issues.Add($"Linked model '{link.Name}' is not loaded — federated validation skipped for this link");
                            continue;
                        }
                        int linkElementCount = new FilteredElementCollector(linkDoc)
                            .WhereElementIsNotElementType().GetElementCount();
                        totalFederatedElements += linkElementCount;

                        // Run compliance scan on linked document
                        var linkCompliance = ComplianceScan.Scan(linkDoc);
                        if (linkCompliance != null)
                        {
                            weightedCompliance += linkCompliance.CompliancePercent * linkElementCount;
                            totalWeight += linkElementCount;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"FederatedPreFlight link '{link.Name}': {ex.Message}"); }
                }

                // Check for tag ID collisions across models
                var hostTags = new HashSet<string>();
                try
                {
                    var hostElements = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType();
                    foreach (var el in hostElements)
                    {
                        try
                        {
                            string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                            if (!string.IsNullOrEmpty(tag)) hostTags.Add(tag);
                        }
                        catch (Exception ex) { StingLog.Warn($"FederatedPreFlight host tag scan: {ex.Message}"); }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"FederatedPreFlight host scan: {ex.Message}"); }

                int duplicateTagCount = 0;
                foreach (var link in links)
                {
                    try
                    {
                        var linkDoc = link.GetLinkDocument();
                        if (linkDoc == null) continue;
                        var linkElements = new FilteredElementCollector(linkDoc)
                            .WhereElementIsNotElementType();
                        foreach (var el in linkElements)
                        {
                            try
                            {
                                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                                if (!string.IsNullOrEmpty(tag) && hostTags.Contains(tag))
                                    duplicateTagCount++;
                            }
                            catch (Exception ex) { StingLog.Warn($"FederatedPreFlight link tag scan: {ex.Message}"); }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"FederatedPreFlight link '{link.Name}': {ex.Message}"); }
                }

                if (duplicateTagCount > 0)
                    issues.Add($"WARNING: {duplicateTagCount} duplicate tag IDs found across host and linked models — SEQ range allocation recommended");

                if (totalWeight > 0)
                {
                    double federatedCompliance = weightedCompliance / totalWeight;
                    if (federatedCompliance < 50)
                        issues.Add($"Federated compliance is low: {federatedCompliance:F1}% across {links.Count} linked models ({totalFederatedElements} elements)");
                }
            }
            catch (Exception ex) { StingLog.Warn($"FederatedPreFlight: {ex.Message}"); }

            return (issues.Count == 0, issues);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  WF-03: ADAPTIVE WORKFLOW CONDITIONS — Dynamic thresholds
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Evaluates adaptive workflow conditions with parseable threshold syntax.
    /// Supports: has_stale:5, tag_compliance:75, phase:Existing, time_before:1700
    /// </summary>
    internal static class AdaptiveConditionEvaluator
    {
        /// <summary>WF-03: Evaluate an adaptive condition string against document state.
        /// Returns true if step should EXECUTE, false if should SKIP.</summary>
        public static bool Evaluate(Document doc, string condition)
        {
            if (string.IsNullOrEmpty(condition) || doc == null) return true;

            // Parse "key:value" format
            string key = condition;
            string value = null;
            int colonIdx = condition.IndexOf(':');
            if (colonIdx > 0)
            {
                key = condition.Substring(0, colonIdx).Trim();
                value = condition.Substring(colonIdx + 1).Trim();
            }

            try
            {
                switch (key.ToLowerInvariant())
                {
                    case "has_stale":
                    {
                        int threshold = int.TryParse(value, out var t) ? t : 1;
                        int staleCount = CountStaleElements(doc);
                        return staleCount >= threshold;
                    }
                    case "tag_compliance":
                    {
                        double threshold = double.TryParse(value, out var t) ? t : 80;
                        var scan = ComplianceScan.Scan(doc);
                        return scan != null && scan.CompliancePercent < threshold; // Execute if BELOW threshold
                    }
                    case "tag_compliance_above":
                    {
                        double threshold = double.TryParse(value, out var t) ? t : 80;
                        var scan = ComplianceScan.Scan(doc);
                        return scan != null && scan.CompliancePercent >= threshold;
                    }
                    case "warning_count":
                    {
                        int threshold = int.TryParse(value, out var t) ? t : 10;
                        int warnCount = doc.GetWarnings()?.Count ?? 0;
                        return warnCount >= threshold;
                    }
                    case "element_count_above":
                    {
                        int threshold = int.TryParse(value, out var t) ? t : 1000;
                        int count = new FilteredElementCollector(doc).WhereElementIsNotElementType().GetElementCount();
                        return count >= threshold;
                    }
                    case "element_count_below":
                    {
                        int threshold = int.TryParse(value, out var t) ? t : 100000;
                        int count = new FilteredElementCollector(doc).WhereElementIsNotElementType().GetElementCount();
                        return count < threshold;
                    }
                    case "time_before":
                    {
                        if (int.TryParse(value, out var hhmm))
                        {
                            int nowHhmm = DateTime.Now.Hour * 100 + DateTime.Now.Minute;
                            return nowHhmm < hhmm;
                        }
                        return true;
                    }
                    case "time_after":
                    {
                        if (int.TryParse(value, out var hhmm))
                        {
                            int nowHhmm = DateTime.Now.Hour * 100 + DateTime.Now.Minute;
                            return nowHhmm >= hhmm;
                        }
                        return true;
                    }
                    case "day_of_week":
                    {
                        return DateTime.Now.DayOfWeek.ToString().Equals(value, StringComparison.OrdinalIgnoreCase);
                    }
                    default:
                        return true; // Unknown condition — allow execution
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"AdaptiveCondition '{condition}': {ex.Message}");
                return true;
            }
        }

        private static int CountStaleElements(Document doc)
        {
            try
            {
                int count = 0;
                var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                foreach (var el in collector)
                {
                    try
                    {
                        var p = el.LookupParameter(ParamRegistry.STALE);
                        if (p != null && p.AsInteger() == 1) count++;
                    }
                    catch (Exception ex) { StingLog.Warn($"CountStale element: {ex.Message}"); }
                }
                return count;
            }
            catch (Exception ex) { StingLog.Warn($"CountStaleElements: {ex.Message}"); return 0; }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  WF-04: STEP OUTPUT CHAINING — Data flow between steps
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Captures per-step output and makes it available to subsequent steps.
    /// Supports branching: "if step A affected > N elements, run step B; else skip".
    /// </summary>
    internal class WorkflowStepOutput
    {
        public string StepCommandTag { get; set; }
        public string StepLabel { get; set; }
        public int AffectedElementCount { get; set; }
        public bool Succeeded { get; set; }
        public double ComplianceDelta { get; set; }
        public int WarningDelta { get; set; }
        public Dictionary<string, string> ExtraData { get; set; } = new();

        /// <summary>Thread-safe storage for current workflow run outputs.</summary>
        private static readonly ConcurrentDictionary<string, WorkflowStepOutput> _currentRunOutputs = new();

        /// <summary>Record output for a step (keyed by command tag).</summary>
        public static void Record(WorkflowStepOutput output)
        {
            if (output?.StepCommandTag != null)
                _currentRunOutputs[output.StepCommandTag] = output;
        }

        /// <summary>Get output from a previous step by command tag.</summary>
        public static WorkflowStepOutput GetOutput(string commandTag)
        {
            _currentRunOutputs.TryGetValue(commandTag, out var output);
            return output;
        }

        /// <summary>Clear all outputs (start of new workflow run).</summary>
        public static void ClearAll() => _currentRunOutputs.Clear();

        /// <summary>Evaluate a branch condition against step outputs.
        /// Format: "stepTag:affected_gt:50" or "stepTag:succeeded" or "stepTag:compliance_delta_gt:5"</summary>
        public static bool EvaluateBranchCondition(string condition)
        {
            if (string.IsNullOrEmpty(condition)) return true;
            try
            {
                var parts = condition.Split(':');
                if (parts.Length < 2) return true;
                string stepTag = parts[0];
                string op = parts[1].ToLowerInvariant();
                var output = GetOutput(stepTag);
                if (output == null) return true; // No output = step didn't run = allow

                switch (op)
                {
                    case "succeeded": return output.Succeeded;
                    case "failed": return !output.Succeeded;
                    case "affected_gt":
                        return parts.Length >= 3 && int.TryParse(parts[2], out var gt) && output.AffectedElementCount > gt;
                    case "affected_lt":
                        return parts.Length >= 3 && int.TryParse(parts[2], out var lt) && output.AffectedElementCount < lt;
                    case "compliance_delta_gt":
                        return parts.Length >= 3 && double.TryParse(parts[2], out var cgt) && output.ComplianceDelta > cgt;
                    default: return true;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"EvaluateBranchCondition '{condition}': {ex.Message}");
                return true;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  WF-05: EXCEPTION RECOVERY STRATEGIES — Per-step error handling
    // ════════════════════════════════════════════════════════════════

    /// <summary>Strategy for handling step execution failures.</summary>
    internal enum ExceptionRecoveryStrategy
    {
        Rollback,      // Roll back entire transaction group (existing default)
        PartialRetry,  // Retry step with 50% element sample
        Fallback,      // Execute an alternate command tag
        Skip,          // Log and continue to next step
        Stop           // Abort entire workflow immediately
    }

    /// <summary>Extended step configuration for exception recovery.</summary>
    internal class StepRecoveryConfig
    {
        public ExceptionRecoveryStrategy Strategy { get; set; } = ExceptionRecoveryStrategy.Skip;
        public string FallbackCommandTag { get; set; }  // For Fallback strategy
        public int MaxRetries { get; set; } = 1;         // For PartialRetry strategy
        public int ErrorThreshold { get; set; } = 5;     // Stop after N consecutive failures

        /// <summary>Apply recovery strategy for a failed step.</summary>
        public static (bool shouldContinue, string action) ApplyRecovery(
            ExceptionRecoveryStrategy strategy, string stepLabel, string fallbackTag, Exception ex)
        {
            switch (strategy)
            {
                case ExceptionRecoveryStrategy.Skip:
                    StingLog.Warn($"Step '{stepLabel}' failed, SKIPPING: {ex.Message}");
                    return (true, "SKIPPED");

                case ExceptionRecoveryStrategy.Stop:
                    StingLog.Error($"Step '{stepLabel}' failed, STOPPING workflow", ex);
                    return (false, "STOPPED");

                case ExceptionRecoveryStrategy.Fallback:
                    if (!string.IsNullOrEmpty(fallbackTag))
                    {
                        StingLog.Warn($"Step '{stepLabel}' failed, falling back to '{fallbackTag}'");
                        return (true, $"FALLBACK:{fallbackTag}");
                    }
                    StingLog.Warn($"Step '{stepLabel}' failed, no fallback — skipping");
                    return (true, "SKIPPED");

                case ExceptionRecoveryStrategy.PartialRetry:
                    StingLog.Warn($"Step '{stepLabel}' failed, will retry with reduced scope");
                    return (true, "RETRY");

                default: // Rollback
                    StingLog.Error($"Step '{stepLabel}' failed, ROLLING BACK", ex);
                    return (false, "ROLLBACK");
            }
        }
    }


    // ════════════════════════════════════════════════════════════════
    //  WM-01: FIX CATEGORIZATION — Multi-dimensional fix assessment
    // ════════════════════════════════════════════════════════════════

    /// <summary>Fix complexity for warning auto-fix operations.</summary>
    internal enum FixComplexity
    {
        Simple,     // One-click, no user input needed
        Moderate,   // 2-3 steps, may need confirmation
        Complex     // Requires user input, analysis, or SME review
    }

    /// <summary>Risk level for auto-fix rollback assessment.</summary>
    internal enum FixRollbackRisk
    {
        Safe,       // Can be easily undone, no downstream impact
        Caution,    // May affect related elements
        HighRisk    // Could cause cascading changes, requires backup
    }

    /// <summary>WM-01: Extended warning fix assessment with complexity, risk, and batch safety.</summary>
    internal class WarningFixAssessment
    {
        public FixComplexity Complexity { get; set; } = FixComplexity.Simple;
        public FixRollbackRisk RollbackRisk { get; set; } = FixRollbackRisk.Safe;
        public string ImpactSummary { get; set; } = "";         // Side effects description
        public List<string> RequiredContext { get; set; } = new(); // Dependencies needed
        public bool BatchSafe { get; set; } = true;              // Can fix in bulk?
        public int EstimatedFixTimeSeconds { get; set; } = 1;

        /// <summary>Assess a classified warning for fix characteristics.</summary>
        public static WarningFixAssessment Assess(ClassifiedWarning warning)
        {
            var assessment = new WarningFixAssessment();
            if (warning == null) return assessment;

            string desc = warning.Description?.ToLowerInvariant() ?? "";

            // Duplicate instances — simple, safe, batch OK
            if (desc.Contains("duplicate instances"))
            {
                assessment.Complexity = FixComplexity.Simple;
                assessment.RollbackRisk = FixRollbackRisk.Safe;
                assessment.ImpactSummary = "Deletes duplicate element; original preserved";
                assessment.BatchSafe = true;
            }
            // Room separation — moderate, caution (affects room boundaries)
            else if (desc.Contains("room separation"))
            {
                assessment.Complexity = FixComplexity.Moderate;
                assessment.RollbackRisk = FixRollbackRisk.Caution;
                assessment.ImpactSummary = "Deletes shorter separation line; may affect room calculation";
                assessment.RequiredContext.Add("Room enclosure validation");
                assessment.BatchSafe = false;
            }
            // Duplicate marks — simple, safe
            else if (desc.Contains("duplicate") && desc.Contains("mark"))
            {
                assessment.Complexity = FixComplexity.Simple;
                assessment.RollbackRisk = FixRollbackRisk.Safe;
                assessment.ImpactSummary = "Appends numeric suffix to duplicate marks";
                assessment.BatchSafe = true;
            }
            // Geometry joins — moderate, caution
            else if (desc.Contains("joined but do not intersect"))
            {
                assessment.Complexity = FixComplexity.Simple;
                assessment.RollbackRisk = FixRollbackRisk.Caution;
                assessment.ImpactSummary = "Unjoins non-intersecting elements; may affect visual appearance";
                assessment.BatchSafe = true;
            }
            // Wall overlaps — complex, high risk
            else if (desc.Contains("wall") && desc.Contains("overlap"))
            {
                assessment.Complexity = FixComplexity.Complex;
                assessment.RollbackRisk = FixRollbackRisk.HighRisk;
                assessment.ImpactSummary = "Joins overlapping walls; may change room boundaries and area calculations";
                assessment.RequiredContext.Add("Structural wall analysis");
                assessment.RequiredContext.Add("Room boundary check");
                assessment.BatchSafe = false;
                assessment.EstimatedFixTimeSeconds = 5;
            }
            // Invalid sketch — complex, requires user
            else if (desc.Contains("invalid sketch"))
            {
                assessment.Complexity = FixComplexity.Complex;
                assessment.RollbackRisk = FixRollbackRisk.HighRisk;
                assessment.ImpactSummary = "Requires manual sketch editing — cannot auto-fix";
                assessment.BatchSafe = false;
                assessment.EstimatedFixTimeSeconds = 60;
            }
            // MEP connector issues — moderate
            else if (warning.Category == WarningCategory.MEP)
            {
                assessment.Complexity = FixComplexity.Moderate;
                assessment.RollbackRisk = FixRollbackRisk.Caution;
                assessment.ImpactSummary = "MEP system changes may affect flow calculations";
                assessment.RequiredContext.Add("MEP system balance check");
                assessment.BatchSafe = true;
                assessment.EstimatedFixTimeSeconds = 3;
            }
            // Default
            else
            {
                assessment.Complexity = warning.CanAutoFix ? FixComplexity.Simple : FixComplexity.Complex;
                assessment.RollbackRisk = warning.CanAutoFix ? FixRollbackRisk.Safe : FixRollbackRisk.Caution;
                assessment.BatchSafe = warning.CanAutoFix;
            }

            return assessment;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  WM-02: WARNING ROOT-CAUSE GRAPH — Causal dependency analysis
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a root-cause dependency graph from classified warnings.
    /// Groups by element and identifies which warnings are caused by others.
    /// E.g., 50 "duplicate instance" warnings all from 1 family type = 1 root cause.
    /// </summary>
    internal static class WarningRootCauseAnalyser
    {
        // MED-08: Pre-compiled regex avoids JIT regex compilation per call
        private static readonly System.Text.RegularExpressions.Regex _digitRegex
            = new System.Text.RegularExpressions.Regex(@"\d+", System.Text.RegularExpressions.RegexOptions.Compiled);
        /// <summary>A root cause with downstream impact count.</summary>
        internal class RootCause
        {
            public string Description { get; set; }
            public WarningCategory Category { get; set; }
            public WarningSeverity Severity { get; set; }
            public int DownstreamWarningCount { get; set; }
            public List<ElementId> SourceElements { get; set; } = new();
            public string FixRecommendation { get; set; }
            public int ImpactScore { get; set; } // 0-100
        }

        /// <summary>WM-02: Identify root causes from a warning report.</summary>
        public static List<RootCause> IdentifyRootCauses(WarningReport report)
        {
            if (report?.Warnings == null || report.Warnings.Count == 0)
                return new List<RootCause>();

            var rootCauses = new List<RootCause>();

            // Step 1: Group warnings by description pattern (root cause grouping)
            var groups = report.Warnings
                .GroupBy(w => NormalizeDescription(w.Description))
                .OrderByDescending(g => g.Count())
                .Take(20); // Top 20 root causes

            foreach (var group in groups)
            {
                var representative = group.First();
                var allElements = group.SelectMany(w => w.FailingElements ?? Enumerable.Empty<ElementId>())
                    .Distinct().ToList();

                // Step 2: Calculate impact score
                int impactScore = CalculateImpactScore(representative, group.Count(), allElements.Count);

                rootCauses.Add(new RootCause
                {
                    Description = representative.Description,
                    Category = representative.Category,
                    Severity = representative.Severity,
                    DownstreamWarningCount = group.Count(),
                    SourceElements = allElements,
                    FixRecommendation = GetRootCauseRecommendation(representative, group.Count()),
                    ImpactScore = impactScore,
                });
            }

            // Step 3: Identify element-level root causes
            // Elements that appear in many different warning types are likely root causes
            var elementWarningCounts = new Dictionary<long, int>();
            foreach (var w in report.Warnings)
            {
                if (w.FailingElements == null) continue;
                foreach (var eid in w.FailingElements)
                {
                    long key = eid.Value;
                    elementWarningCounts[key] = elementWarningCounts.GetValueOrDefault(key, 0) + 1;
                }
            }

            // Elements involved in 3+ different warnings are root cause candidates
            var rootCauseElements = elementWarningCounts
                .Where(kvp => kvp.Value >= 3)
                .OrderByDescending(kvp => kvp.Value)
                .Take(5);

            foreach (var kvp in rootCauseElements)
            {
                var elementId = new ElementId((long)kvp.Key);
                var relatedWarnings = report.Warnings
                    .Where(w => w.FailingElements != null && w.FailingElements.Contains(elementId))
                    .ToList();

                if (relatedWarnings.Count >= 3)
                {
                    rootCauses.Add(new RootCause
                    {
                        Description = $"Element #{kvp.Key} involved in {kvp.Value} warnings across {relatedWarnings.Select(w => w.Category).Distinct().Count()} categories",
                        Category = relatedWarnings.GroupBy(w => w.Category).OrderByDescending(g => g.Count()).First().Key,
                        Severity = relatedWarnings.Max(w => w.Severity) < WarningSeverity.Low ? relatedWarnings.Max(w => w.Severity) : WarningSeverity.High,
                        DownstreamWarningCount = kvp.Value,
                        SourceElements = new List<ElementId> { elementId },
                        FixRecommendation = "Review this element — it is the source of multiple warning types. Consider recreating or repositioning.",
                        ImpactScore = Math.Min(100, kvp.Value * 15),
                    });
                }
            }

            return rootCauses.OrderByDescending(r => r.ImpactScore).ToList();
        }

        private static string NormalizeDescription(string desc)
        {
            if (string.IsNullOrEmpty(desc)) return "";
            // Strip element-specific IDs and values to group similar warnings
            return _digitRegex.Replace(desc, "#").Trim();
        }

        private static int CalculateImpactScore(ClassifiedWarning w, int groupCount, int elementCount)
        {
            int score = 0;
            // Severity weight (max 50)
            score += w.Severity switch
            {
                WarningSeverity.Critical => 50,
                WarningSeverity.High => 35,
                WarningSeverity.Medium => 20,
                WarningSeverity.Low => 10,
                _ => 5
            };
            // Element count impact (max 20)
            score += Math.Min(20, elementCount / 5);
            // Group size impact (max 20)
            score += Math.Min(20, groupCount * 2);
            // Auto-fixability bonus (max 10)
            if (w.CanAutoFix) score += 10;
            return Math.Min(100, score);
        }

        private static string GetRootCauseRecommendation(ClassifiedWarning w, int count)
        {
            if (count > 20 && w.Description?.Contains("duplicate") == true)
                return $"Fix source: {count} duplicates likely from a single copy/paste or family reload. Delete originals and re-place.";
            if (w.Category == WarningCategory.Spatial)
                return "Fix room boundaries first — spatial errors cascade to tag, schedule, and COBie data.";
            if (w.Category == WarningCategory.MEP && count > 5)
                return "Review MEP system topology — disconnected or misrouted elements cause chain warnings.";
            if (w.CanAutoFix)
                return $"Auto-fixable: run Warnings Auto-Fix to resolve {count} instances in batch.";
            return $"Manual review required: {count} instances of this warning type.";
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  WM-03: SUPPRESSION AUDIT TRAIL — Context-aware suppressions
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// A warning suppression rule with time limits, context, and audit trail.
    /// Replaces simple string list with rich suppression records.
    /// </summary>
    internal class SuppressionRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Pattern { get; set; }                     // Warning text pattern to suppress
        public DateTime? SuppressUntil { get; set; }            // Expiry date (null = permanent)
        public string Context { get; set; } = "all";            // Phase context: all/SD/DD/CD/handover
        public string SuppressedBy { get; set; } = Environment.UserName;
        public DateTime SuppressedDate { get; set; } = DateTime.Now;
        public string Reason { get; set; } = "";                // Why suppressed
        public bool Active { get; set; } = true;
    }

    /// <summary>Manages warning suppressions with audit trail.</summary>
    internal static class SuppressionManager
    {
        private static readonly List<SuppressionRule> _rules = new();
        private static readonly object _lock = new object();

        /// <summary>Add a suppression rule.</summary>
        public static void AddRule(SuppressionRule rule)
        {
            lock (_lock) { _rules.Add(rule); }
            StingLog.Info($"Suppression added: '{rule.Pattern}' by {rule.SuppressedBy} (until {rule.SuppressUntil?.ToString("yyyy-MM-dd") ?? "permanent"}, reason: {rule.Reason})");
        }

        /// <summary>Remove a rule by ID.</summary>
        public static bool RemoveRule(string ruleId)
        {
            lock (_lock) { return _rules.RemoveAll(r => r.Id == ruleId) > 0; }
        }

        /// <summary>Check if a warning description is currently suppressed.</summary>
        public static bool IsSuppressed(string description, string currentPhase = "all")
        {
            if (string.IsNullOrEmpty(description)) return false;
            lock (_lock)
            {
                foreach (var rule in _rules)
                {
                    if (!rule.Active) continue;
                    // Check expiry
                    if (rule.SuppressUntil.HasValue && DateTime.Now > rule.SuppressUntil.Value)
                    {
                        rule.Active = false;
                        continue;
                    }
                    // Check context
                    if (!rule.Context.Equals("all", StringComparison.OrdinalIgnoreCase) &&
                        !rule.Context.Equals(currentPhase, StringComparison.OrdinalIgnoreCase))
                        continue;
                    // Check pattern match
                    if (description.IndexOf(rule.Pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            return false;
        }

        /// <summary>Get all rules (active + expired) for review.</summary>
        public static List<SuppressionRule> GetAllRules()
        {
            lock (_lock) { return new List<SuppressionRule>(_rules); }
        }

        /// <summary>Get suppression audit report.</summary>
        public static string GetAuditReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Warning Suppression Audit Trail");
            sb.AppendLine(new string('═', 60));
            lock (_lock)
            {
                if (_rules.Count == 0) { sb.AppendLine("No suppression rules defined."); return sb.ToString(); }
                foreach (var rule in _rules.OrderByDescending(r => r.SuppressedDate))
                {
                    string status = rule.Active ? "ACTIVE" : "EXPIRED";
                    if (rule.SuppressUntil.HasValue && DateTime.Now > rule.SuppressUntil.Value)
                        status = "EXPIRED";
                    sb.AppendLine($"  [{status}] Pattern: \"{rule.Pattern}\"");
                    sb.AppendLine($"    By: {rule.SuppressedBy} on {rule.SuppressedDate:yyyy-MM-dd HH:mm}");
                    sb.AppendLine($"    Context: {rule.Context} | Until: {rule.SuppressUntil?.ToString("yyyy-MM-dd") ?? "permanent"}");
                    sb.AppendLine($"    Reason: {rule.Reason}");
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        /// <summary>Load rules from project_config.json.</summary>
        public static void LoadFromConfig(string configPath)
        {
            try
            {
                if (!File.Exists(configPath)) return;
                var json = JObject.Parse(File.ReadAllText(configPath));
                var arr = json["WARNING_SUPPRESSIONS"] as JArray;
                if (arr == null) return;
                lock (_lock)
                {
                    _rules.Clear();
                    foreach (var item in arr)
                    {
                        _rules.Add(new SuppressionRule
                        {
                            Id = item["Id"]?.ToString() ?? Guid.NewGuid().ToString("N").Substring(0, 8),
                            Pattern = item["Pattern"]?.ToString() ?? "",
                            SuppressUntil = item["SuppressUntil"] != null ? DateTime.TryParse(item["SuppressUntil"].ToString(), out var dt) ? dt : (DateTime?)null : null,
                            Context = item["Context"]?.ToString() ?? "all",
                            SuppressedBy = item["SuppressedBy"]?.ToString() ?? "",
                            SuppressedDate = DateTime.TryParse(item["SuppressedDate"]?.ToString(), out var sd) ? sd : DateTime.Now,
                            Reason = item["Reason"]?.ToString() ?? "",
                            Active = item["Active"]?.Value<bool>() ?? true,
                        });
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"SuppressionManager.LoadFromConfig: {ex.Message}"); }
        }

        /// <summary>Save rules to project_config.json.</summary>
        public static void SaveToConfig(string configPath)
        {
            try
            {
                JObject json = File.Exists(configPath) ? JObject.Parse(File.ReadAllText(configPath)) : new JObject();
                var arr = new JArray();
                lock (_lock)
                {
                    foreach (var r in _rules)
                    {
                        arr.Add(new JObject
                        {
                            ["Id"] = r.Id, ["Pattern"] = r.Pattern,
                            ["SuppressUntil"] = r.SuppressUntil?.ToString("o"),
                            ["Context"] = r.Context, ["SuppressedBy"] = r.SuppressedBy,
                            ["SuppressedDate"] = r.SuppressedDate.ToString("o"),
                            ["Reason"] = r.Reason, ["Active"] = r.Active,
                        });
                    }
                }
                json["WARNING_SUPPRESSIONS"] = arr;
                File.WriteAllText(configPath, json.ToString(Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"SuppressionManager.SaveToConfig: {ex.Message}"); }
        }

        /// <summary>Reset all rules (document close).</summary>
        public static void Reset() { lock (_lock) { _rules.Clear(); } }
    }


    // ════════════════════════════════════════════════════════════════
    //  CC-01: AUTO-REFRESH TIMER — Periodic data refresh in dialogs
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Provides periodic refresh capability for WPF dialogs.
    /// Tracks last refresh time and change indicators.
    /// </summary>
    internal static class DialogRefreshManager
    {
        private static DateTime _lastRefreshTime = DateTime.MinValue;
        private static readonly Dictionary<string, int> _previousValues = new();

        /// <summary>Record current refresh time.</summary>
        public static void RecordRefresh() => _lastRefreshTime = DateTime.UtcNow;

        /// <summary>Get formatted last refresh time.</summary>
        public static string LastRefreshText =>
            _lastRefreshTime == DateTime.MinValue ? "Never" : _lastRefreshTime.ToString("HH:mm:ss");

        /// <summary>Seconds since last refresh.</summary>
        public static double SecondsSinceRefresh =>
            _lastRefreshTime == DateTime.MinValue ? double.MaxValue : (DateTime.UtcNow - _lastRefreshTime).TotalSeconds;

        /// <summary>Track a metric value and return change indicator (↑+N, ↓-N, or →0).</summary>
        public static string TrackChange(string metricName, int currentValue)
        {
            if (_previousValues.TryGetValue(metricName, out int prev))
            {
                int delta = currentValue - prev;
                _previousValues[metricName] = currentValue;
                if (delta > 0) return $"↑+{delta}";
                if (delta < 0) return $"↓{delta}";
                return "→0";
            }
            _previousValues[metricName] = currentValue;
            return "";
        }

        /// <summary>Reset tracking (document close).</summary>
        public static void Reset()
        {
            _lastRefreshTime = DateTime.MinValue;
            _previousValues.Clear();
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  CC-03: TEAM COLLABORATION SIGNALS — Activity tracking
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tracks team member activity signals from worksharing data,
    /// issues, and file changes. Provides near-real-time team awareness.
    /// </summary>
    internal static class TeamActivityTracker
    {
        /// <summary>A team activity entry.</summary>
        internal class ActivityEntry
        {
            public DateTime Timestamp { get; set; } = DateTime.Now;
            public string UserName { get; set; }
            public string Action { get; set; }          // "Checked out", "Tagged", "Issue created", etc.
            public string Detail { get; set; }           // Workset name, discipline, issue title, etc.
            public string Discipline { get; set; }
        }

        private static readonly List<ActivityEntry> _activities = new();
        private static readonly object _lock = new object();
        private const int MaxEntries = 200;

        /// <summary>Record a team activity.</summary>
        public static void Record(string user, string action, string detail, string discipline = "")
        {
            lock (_lock)
            {
                _activities.Add(new ActivityEntry
                {
                    UserName = user, Action = action,
                    Detail = detail, Discipline = discipline,
                });
                if (_activities.Count > MaxEntries)
                    _activities.RemoveRange(0, _activities.Count - MaxEntries);
            }
        }

        /// <summary>Get recent activities (last N minutes, default 60).</summary>
        public static List<ActivityEntry> GetRecent(int minutes = 60)
        {
            var cutoff = DateTime.Now.AddMinutes(-minutes);
            lock (_lock)
            {
                return _activities.Where(a => a.Timestamp >= cutoff)
                    .OrderByDescending(a => a.Timestamp).ToList();
            }
        }

        /// <summary>CC-03: Scan worksharing data for team activities.</summary>
        public static void ScanWorksharing(Document doc)
        {
            if (doc == null || !doc.IsWorkshared) return;
            try
            {
                var worksetTable = doc.GetWorksetTable();
                if (worksetTable == null) return;
                var iterator = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset);
                foreach (var ws in iterator)
                {
                    string owner = ws.Owner;
                    if (!string.IsNullOrEmpty(owner))
                    {
                        Record(owner, "Workset checked out", ws.Name);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"TeamActivityTracker.ScanWorksharing: {ex.Message}"); }
        }

        /// <summary>CC-03: Scan issues.json for recent team activity.</summary>
        public static void ScanIssues(Document doc)
        {
            try
            {
                string projectDir = doc != null ? Path.GetDirectoryName(doc.PathName) : null;
                if (string.IsNullOrEmpty(projectDir)) return;
                string issuesPath = Path.Combine(projectDir, "_bim_manager", "issues.json");
                if (!File.Exists(issuesPath)) return;

                var issues = JArray.Parse(File.ReadAllText(issuesPath));
                var cutoff = DateTime.Now.AddHours(-24);
                foreach (var issue in issues)
                {
                    var created = issue["created_date"]?.ToString();
                    if (DateTime.TryParse(created, out var dt) && dt >= cutoff)
                    {
                        string user = issue["created_by"]?.ToString() ?? "Unknown";
                        string title = issue["title"]?.ToString() ?? "";
                        string type = issue["type"]?.ToString() ?? "Issue";
                        Record(user, $"{type} created", title);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"TeamActivityTracker.ScanIssues: {ex.Message}"); }
        }

        /// <summary>Reset activities (document close).</summary>
        public static void Reset() { lock (_lock) { _activities.Clear(); } }
    }

    // ════════════════════════════════════════════════════════════════
    //  CC-04: COMPLIANCE IMPROVEMENT TRACKING — Multi-cycle trends
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tracks compliance improvement across multiple workflow cycles.
    /// Provides per-discipline trajectory and bottleneck identification.
    /// </summary>
    internal static class ComplianceImprovementTracker
    {
        /// <summary>A compliance data point for trend analysis.</summary>
        internal class ComplianceDataPoint
        {
            public DateTime Timestamp { get; set; }
            public double OverallCompliance { get; set; }
            public Dictionary<string, double> ByDiscipline { get; set; } = new();
            public int StaleCount { get; set; }
            public int WarningCount { get; set; }
            public string Source { get; set; } = "manual"; // manual, workflow, auto
        }

        private static readonly List<ComplianceDataPoint> _dataPoints = new();
        private static readonly object _lock = new object();

        /// <summary>Record a compliance data point.</summary>
        public static void RecordDataPoint(Document doc, string source = "manual")
        {
            if (doc == null) return;
            try
            {
                var scan = ComplianceScan.Scan(doc);
                if (scan == null) return;

                var dp = new ComplianceDataPoint
                {
                    Timestamp = DateTime.Now,
                    OverallCompliance = scan.CompliancePercent,
                    StaleCount = scan.StaleCount,
                    WarningCount = doc.GetWarnings()?.Count ?? 0,
                    Source = source,
                };

                // Per-discipline compliance
                if (scan.ByDisc != null)
                {
                    foreach (var kvp in scan.ByDisc)
                        dp.ByDiscipline[kvp.Key] = kvp.Value.CompliancePct;
                }

                lock (_lock)
                {
                    _dataPoints.Add(dp);
                    // Keep last 200 data points
                    if (_dataPoints.Count > 200)
                        _dataPoints.RemoveRange(0, _dataPoints.Count - 200);
                }
            }
            catch (Exception ex) { StingLog.Warn($"ComplianceImprovementTracker: {ex.Message}"); }
        }

        /// <summary>CC-04: Get 7-day trend with directional arrows per discipline.</summary>
        public static Dictionary<string, string> GetDisciplineTrends()
        {
            var trends = new Dictionary<string, string>();
            lock (_lock)
            {
                if (_dataPoints.Count < 2) return trends;
                var recent = _dataPoints.Where(d => d.Timestamp >= DateTime.Now.AddDays(-7)).ToList();
                if (recent.Count < 2) return trends;

                var first = recent.First();
                var last = recent.Last();

                // Overall trend
                double delta = last.OverallCompliance - first.OverallCompliance;
                trends["Overall"] = FormatTrend(delta);

                // Per-discipline trends
                var allDiscs = first.ByDiscipline.Keys.Union(last.ByDiscipline.Keys);
                foreach (var disc in allDiscs)
                {
                    double firstVal = first.ByDiscipline.GetValueOrDefault(disc, 0);
                    double lastVal = last.ByDiscipline.GetValueOrDefault(disc, 0);
                    trends[disc] = FormatTrend(lastVal - firstVal);
                }
            }
            return trends;
        }

        /// <summary>CC-04: Identify the discipline bottleneck (lowest compliance, slowest improvement).</summary>
        public static string IdentifyBottleneck()
        {
            lock (_lock)
            {
                if (_dataPoints.Count == 0) return "No data";
                var latest = _dataPoints.Last();
                if (latest.ByDiscipline.Count == 0) return "No discipline data";
                var worst = latest.ByDiscipline.OrderBy(kvp => kvp.Value).First();
                return $"{worst.Key} at {worst.Value:F0}%";
            }
        }

        /// <summary>CC-04: Estimate days to reach target compliance.</summary>
        public static string EstimateDaysToTarget(double targetPct = 95)
        {
            lock (_lock)
            {
                var recent = _dataPoints.Where(d => d.Timestamp >= DateTime.Now.AddDays(-7)).ToList();
                if (recent.Count < 2) return "Insufficient data";
                double first = recent.First().OverallCompliance;
                double last = recent.Last().OverallCompliance;
                double daysElapsed = (recent.Last().Timestamp - recent.First().Timestamp).TotalDays;
                if (daysElapsed < 0.01) return "Insufficient data";
                double ratePerDay = (last - first) / daysElapsed;
                if (ratePerDay <= 0) return "Not improving — compliance stalled or declining";
                double remaining = targetPct - last;
                if (remaining <= 0) return "Target already met!";
                double daysNeeded = remaining / ratePerDay;
                return $"~{daysNeeded:F0} days at current rate (+{ratePerDay:F1}%/day)";
            }
        }

        private static string FormatTrend(double delta)
        {
            if (delta > 1) return $"↑ +{delta:F1}%";
            if (delta < -1) return $"↓ {delta:F1}%";
            return "→ stable";
        }

        /// <summary>Reset tracking (document close).</summary>
        public static void Reset() { lock (_lock) { _dataPoints.Clear(); } }
    }

    // ════════════════════════════════════════════════════════════════
    //  CC-05: SMART ACTION SEQUENCING — Dependency-aware actions
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Manages action dependencies and prerequisite checking.
    /// When user triggers an action, checks if prerequisites are met.
    /// </summary>
    internal static class ActionDependencyManager
    {
        /// <summary>Action dependency definition.</summary>
        internal class ActionDependency
        {
            public string ActionTag { get; set; }
            public List<string> Prerequisites { get; set; } = new();
            public string Description { get; set; }
        }

        /// <summary>Built-in action dependency definitions.</summary>
        private static readonly Dictionary<string, ActionDependency> _dependencies = new(StringComparer.OrdinalIgnoreCase)
        {
            ["COBieExport"] = new ActionDependency
            {
                ActionTag = "COBieExport",
                Prerequisites = new() { "ValidateTags", "WarningsAutoFix" },
                Description = "COBie export requires clean tags and no critical warnings",
            },
            ["CreateTransmittal"] = new ActionDependency
            {
                ActionTag = "CreateTransmittal",
                Prerequisites = new() { "ValidateTags" },
                Description = "Transmittals should reference validated tag data",
            },
            ["BatchPrintSheets"] = new ActionDependency
            {
                ActionTag = "BatchPrintSheets",
                Prerequisites = new() { "SheetNamingCheck" },
                Description = "Print queue should use validated sheet names",
            },
            ["GenerateBEP"] = new ActionDependency
            {
                ActionTag = "GenerateBEP",
                Prerequisites = new() { "ValidateTags", "ModelHealthDashboard" },
                Description = "BEP generation benefits from current compliance and model health data",
            },
            ["ExportSheetRegister"] = new ActionDependency
            {
                ActionTag = "ExportSheetRegister",
                Prerequisites = new() { "SheetNamingCheck" },
                Description = "Sheet register should reflect validated sheet naming",
            },
            ["CreateRevision"] = new ActionDependency
            {
                ActionTag = "CreateRevision",
                Prerequisites = new() { "RetagStale", "ValidateTags" },
                Description = "Revisions should capture current state with no stale elements",
            },
        };

        /// <summary>CC-05: Check prerequisites for an action. Returns unmet prerequisites.</summary>
        public static List<string> GetUnmetPrerequisites(string actionTag, Document doc)
        {
            if (!_dependencies.TryGetValue(actionTag, out var dep))
                return new List<string>();

            var unmet = new List<string>();
            foreach (var prereq in dep.Prerequisites)
            {
                if (!IsPrerequisiteMet(prereq, doc))
                    unmet.Add(prereq);
            }
            return unmet;
        }

        /// <summary>Get the dependency description for an action.</summary>
        public static string GetDependencyDescription(string actionTag)
        {
            return _dependencies.TryGetValue(actionTag, out var dep) ? dep.Description : null;
        }

        private static bool IsPrerequisiteMet(string prereq, Document doc)
        {
            if (doc == null) return false;
            try
            {
                switch (prereq)
                {
                    case "ValidateTags":
                        var scan = ComplianceScan.Scan(doc);
                        return scan != null && scan.CompliancePercent >= 70; // Minimum 70% for downstream operations
                    case "WarningsAutoFix":
                        var warnings = doc.GetWarnings();
                        int criticalCount = 0;
                        foreach (var w in warnings ?? Enumerable.Empty<FailureMessage>())
                        {
                            string desc = w.GetDescriptionText()?.ToLowerInvariant() ?? "";
                            if (desc.Contains("invalid sketch") || desc.Contains("duplicate instances"))
                                criticalCount++;
                        }
                        return criticalCount == 0;
                    case "RetagStale":
                        // HIGH-12: Use cached ComplianceScan.StaleCount instead of full element scan
                        return (ComplianceScan.Scan(doc)?.StaleCount ?? 0) == 0;
                    case "SheetNamingCheck":
                        return true; // Always allow — naming check is advisory
                    case "ModelHealthDashboard":
                        return true; // Always allow — health check is advisory
                    default:
                        return true;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ActionDependencyManager.IsPrerequisiteMet '{prereq}': {ex.Message}");
                return true; // Don't block on errors
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  CC-06: ROLE-BASED ACTION GATING — ISO 19650 role visibility
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Controls action visibility and approval requirements based on ISO 19650 roles.
    /// Roles: A(Architect), M(Mechanical), E(Electrical), S(Structural), etc.
    /// </summary>
    internal static class RoleBasedAccessControl
    {
        /// <summary>ISO 19650 role definition.</summary>
        internal class RoleDefinition
        {
            public string Code { get; set; }
            public string Name { get; set; }
            public HashSet<string> AllowedActions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> ApprovalRequiredActions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        // HIGH-13: Per-document cache for GetCurrentUserRole to avoid repeated file reads
        private static string _cachedUserRole;
        private static string _cachedUserRoleDocKey;

        /// <summary>Get current user's role from project_config.json. Cached per config file path.</summary>
        public static string GetCurrentUserRole()
        {
            try
            {
                string configPath = TagConfig.ConfigSource;
                if (string.IsNullOrEmpty(configPath)) return "Z";
                // Return cached value if config path hasn't changed
                if (_cachedUserRoleDocKey == configPath && _cachedUserRole != null)
                    return _cachedUserRole;
                if (!File.Exists(configPath)) return "Z";
                var json = JObject.Parse(File.ReadAllText(configPath));
                _cachedUserRole = json["USER_ROLE"]?.ToString() ?? "Z";
                _cachedUserRoleDocKey = configPath;
                return _cachedUserRole;
            }
            catch (Exception ex) { StingLog.Warn($"GetCurrentUserRole: {ex.Message}"); return "Z"; }
        }

        /// <summary>Invalidate role cache (call when config changes).</summary>
        public static void InvalidateRoleCache() { _cachedUserRole = null; _cachedUserRoleDocKey = null; }

        /// <summary>CC-06: Check if action is allowed for current user role.</summary>
        public static bool IsActionAllowed(string actionTag, string userRole = null)
        {
            userRole ??= GetCurrentUserRole();

            // BIM Manager (K) and Coordinator (C) have all permissions
            if (userRole == "K" || userRole == "C") return true;

            // Check role-specific restrictions
            var restrictedActions = GetRestrictedActions();
            if (restrictedActions.TryGetValue(actionTag, out var allowedRoles))
                return allowedRoles.Contains(userRole);

            return true; // Default: allow
        }

        /// <summary>CC-06: Check if action requires approval.</summary>
        public static bool RequiresApproval(string actionTag, string userRole = null)
        {
            userRole ??= GetCurrentUserRole();

            // BIM Manager never needs approval
            if (userRole == "K") return false;

            // CDE state transitions always require approval from non-managers
            var approvalRequired = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "UpdateCDEStatus", "BulkUpdateCDE", "CreateTransmittal",
                "CreateRevision", "IssueSheetsForRevision",
            };

            return approvalRequired.Contains(actionTag);
        }

        /// <summary>Actions restricted to specific roles.</summary>
        private static Dictionary<string, HashSet<string>> GetRestrictedActions()
        {
            return new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                // CDE transitions restricted to managers and coordinators
                ["UpdateCDEStatus"] = new() { "K", "C", "I" },
                ["BulkUpdateCDE"] = new() { "K", "C" },
                // BEP generation restricted to BIM managers
                ["GenerateBEP"] = new() { "K", "C", "I" },
                ["UpdateBEP"] = new() { "K", "C" },
                // Bulk delete operations restricted
                ["PurgeSharedParams"] = new() { "K" },
                ["DeleteUnusedViews"] = new() { "K", "C", "A", "M", "E", "S" },
            };
        }

        /// <summary>Get role display name.</summary>
        public static string GetRoleName(string code)
        {
            return code switch
            {
                "A" => "Architect", "M" => "Mechanical Engineer", "E" => "Electrical Engineer",
                "S" => "Structural Engineer", "H" => "HVAC Engineer", "P" => "Plumber",
                "C" => "BIM Coordinator", "I" => "Information Manager", "K" => "BIM Manager",
                "Q" => "QA Manager", "F" => "Facilities Manager", "W" => "Contractor",
                "L" => "Client", "Z" => "General User",
                _ => $"Role {code}",
            };
        }
    }


    // ════════════════════════════════════════════════════════════════
    //  ED-02: ISSUE-TRIGGERED WORKFLOW — Auto-workflow on issue creation
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Monitors issue creation and triggers resolution workflows automatically.
    /// Fires IssueResolution workflow when CRITICAL/HIGH issue is created.
    /// </summary>
    internal static class IssueTriggeredWorkflow
    {
        /// <summary>ED-02: Called after issue creation to check for auto-workflow triggers.</summary>
        public static void OnIssueCreated(Document doc, string issueType, string priority, string title)
        {
            if (doc == null) return;
            StingLog.Info($"IssueTriggeredWorkflow: issue created — {issueType} [{priority}] \"{title}\"");

            // Auto-queue resolution workflow for CRITICAL priority
            if (priority.Equals("CRITICAL", StringComparison.OrdinalIgnoreCase))
            {
                WorkflowScheduler.CheckSLATriggers(doc, 1); // Trigger SLA-based workflow if configured
                TeamActivityTracker.Record(Environment.UserName, "CRITICAL issue created", title);
            }
            // Track for team awareness
            TeamActivityTracker.Record(Environment.UserName, $"{issueType} created", title);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  ED-03: WORKSET CHANGE NOTIFICATION — Team worksharing alerts
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tracks workset ownership changes and broadcasts team notifications.
    /// Wired into DocumentChanged event in StingToolsApp.
    /// </summary>
    internal static class WorksetChangeNotifier
    {
        private static readonly ConcurrentDictionary<string, string> _previousOwners = new();

        /// <summary>ED-03: Check for workset ownership changes and log to team activity.</summary>
        public static void CheckWorksetChanges(Document doc)
        {
            if (doc == null || !doc.IsWorkshared) return;
            try
            {
                var collector = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset);
                foreach (var ws in collector)
                {
                    string currentOwner = ws.Owner ?? "";
                    string key = $"{doc.Title}:{ws.Name}";
                    if (_previousOwners.TryGetValue(key, out string prevOwner))
                    {
                        if (prevOwner != currentOwner)
                        {
                            if (!string.IsNullOrEmpty(currentOwner))
                                TeamActivityTracker.Record(currentOwner, "Checked out workset", ws.Name);
                            else if (!string.IsNullOrEmpty(prevOwner))
                                TeamActivityTracker.Record(prevOwner, "Released workset", ws.Name);
                        }
                    }
                    _previousOwners[key] = currentOwner;
                }
            }
            catch (Exception ex) { StingLog.Warn($"WorksetChangeNotifier: {ex.Message}"); }
        }

        /// <summary>Reset tracking (document close).</summary>
        /// <summary>Reset tracking (document close).</summary>
        public static void Reset() => _previousOwners.Clear();
    }

    // ════════════════════════════════════════════════════════════════
    //  ED-04: SLA MONITORING — Background SLA violation detection
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Monitors issue SLA compliance and triggers escalation.
    /// SLA thresholds per ISO 19650: Critical=4h, High=24h, Medium=1wk, Low=2wk.
    /// </summary>
    internal static class SLAMonitor
    {
        private static DateTime _lastCheck = DateTime.MinValue;
        private const int CheckIntervalMinutes = 5;

        /// <summary>SLA thresholds in hours per priority level.</summary>
        public static readonly Dictionary<string, double> SLAThresholdsHours = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CRITICAL"] = 4, ["HIGH"] = 24, ["MEDIUM"] = 168, ["LOW"] = 336,
        };

        /// <summary>ED-04: Check for SLA violations. Call periodically (e.g., every 5 mins).</summary>
        public static (int violations, List<string> details) CheckViolations(Document doc)
        {
            if (doc == null) return (0, new List<string>());

            // Debounce: only check every N minutes
            if ((DateTime.UtcNow - _lastCheck).TotalMinutes < CheckIntervalMinutes)
                return (0, new List<string>());
            _lastCheck = DateTime.UtcNow;

            var violations = new List<string>();
            try
            {
                string projectDir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(projectDir)) return (0, violations);
                string issuesPath = Path.Combine(projectDir, "_bim_manager", "issues.json");
                if (!File.Exists(issuesPath)) return (0, violations);

                var issues = JArray.Parse(File.ReadAllText(issuesPath));
                foreach (var issue in issues)
                {
                    string status = issue["status"]?.ToString() ?? "";
                    if (status.Equals("CLOSED", StringComparison.OrdinalIgnoreCase) ||
                        status.Equals("RESOLVED", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string priority = issue["priority"]?.ToString() ?? "LOW";
                    string createdStr = issue["created_date"]?.ToString();
                    if (!DateTime.TryParse(createdStr, out var createdDate)) continue;

                    double ageHours = (DateTime.Now - createdDate).TotalHours;
                    double threshold = SLAThresholdsHours.GetValueOrDefault(priority, 336);

                    if (ageHours > threshold)
                    {
                        string id = issue["id"]?.ToString() ?? "";
                        string title = issue["title"]?.ToString() ?? "";
                        violations.Add($"{id}: [{priority}] \"{title}\" — {ageHours:F0}h old (SLA: {threshold}h)");
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"SLAMonitor.CheckViolations: {ex.Message}"); }

            // Trigger workflow scheduler if violations found
            if (violations.Count > 0)
                WorkflowScheduler.CheckSLATriggers(doc, violations.Count);

            return (violations.Count, violations);
        }

        /// <summary>Reset last check time (document close).</summary>
        public static void Reset() => _lastCheck = DateTime.MinValue;
    }

    // ════════════════════════════════════════════════════════════════
    //  CSI-01: WARNING→ISSUE AUTO-CREATION — With deduplication
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Auto-creates issues from critical/high warnings with deduplication.
    /// Groups identical warnings to avoid flooding the issue tracker.
    /// </summary>
    internal static class WarningToIssueCreator
    {
        /// <summary>CSI-01: Create issues from warnings with deduplication.</summary>
        public static (int created, List<string> details) CreateIssuesFromWarnings(
            Document doc, WarningReport report, WarningSeverity minSeverity = WarningSeverity.High)
        {
            var details = new List<string>();
            if (doc == null || report?.Warnings == null) return (0, details);

            string projectDir = Path.GetDirectoryName(doc.PathName);
            if (string.IsNullOrEmpty(projectDir)) return (0, details);

            // Load existing issues for deduplication
            string issuesPath = Path.Combine(projectDir, "_bim_manager", "issues.json");
            JArray existingIssues;
            try
            {
                if (File.Exists(issuesPath))
                    existingIssues = JArray.Parse(File.ReadAllText(issuesPath));
                else
                    existingIssues = new JArray();
            }
            catch (Exception ex) { StingLog.Warn($"WarningToIssueCreator load: {ex.Message}"); existingIssues = new JArray(); }

            // Build dedup key set from existing issues
            var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var issue in existingIssues)
            {
                string warnCat = issue["warning_category"]?.ToString();
                string warnDesc = issue["source_warning"]?.ToString();
                if (!string.IsNullOrEmpty(warnCat) && !string.IsNullOrEmpty(warnDesc))
                    existingKeys.Add($"{warnCat}:{warnDesc.Substring(0, Math.Min(50, warnDesc.Length))}");
            }

            // Group qualifying warnings by description
            var qualifyingGroups = report.Warnings
                .Where(w => w.Severity <= minSeverity) // Critical < High < Medium (enum order)
                .GroupBy(w => w.Description?.Substring(0, Math.Min(50, w.Description?.Length ?? 0)) ?? "")
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .Take(20); // Cap at 20 issue types per scan

            int created = 0;
            foreach (var group in qualifyingGroups)
            {
                var representative = group.First();
                string dedupKey = $"{representative.Category}:{group.Key}";
                if (existingKeys.Contains(dedupKey)) continue; // Already has issue

                string issueType = representative.Severity == WarningSeverity.Critical ? "NCR" : "SI";
                string priority = representative.Severity == WarningSeverity.Critical ? "CRITICAL" : "HIGH";
                int nextId = existingIssues.Count + 1;
                string issueId = $"{issueType}-{nextId:D4}";

                var newIssue = new JObject
                {
                    ["id"] = issueId,
                    ["title"] = $"[Auto] {representative.Description?.Substring(0, Math.Min(80, representative.Description?.Length ?? 0))}",
                    ["type"] = issueType,
                    ["priority"] = priority,
                    ["status"] = "OPEN",
                    ["created_by"] = Environment.UserName,
                    ["created_date"] = DateTime.Now.ToString("o"),
                    ["auto_created"] = true,
                    ["warning_category"] = representative.Category.ToString(),
                    ["source_warning"] = group.Key,
                    ["element_count"] = group.SelectMany(w => w.FailingElements ?? Enumerable.Empty<ElementId>()).Distinct().Count(),
                    ["description"] = $"Auto-created from {group.Count()} {representative.Category} warnings. Fix: {representative.FixStrategy}",
                };
                existingIssues.Add(newIssue);
                existingKeys.Add(dedupKey);
                created++;
                details.Add($"Created {issueId}: {representative.Description?.Substring(0, Math.Min(60, representative.Description?.Length ?? 0))}");
            }

            // Save updated issues
            if (created > 0)
            {
                try
                {
                    string dir = Path.GetDirectoryName(issuesPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(issuesPath, existingIssues.ToString(Formatting.Indented));
                    StingLog.Info($"WarningToIssueCreator: created {created} issues from warnings");
                }
                catch (Exception ex) { StingLog.Warn($"WarningToIssueCreator save: {ex.Message}"); }
            }

            return (created, details);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  CSI-02: CONTAINER↔WARNING CROSS-VALIDATION
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Cross-validates container completeness against warning patterns.
    /// Identifies warnings that could be resolved by writing containers.
    /// </summary>
    internal static class ContainerWarningCrossValidator
    {
        /// <summary>CSI-02: Analyse relationship between container gaps and warnings.</summary>
        public static (int containerRelatedWarnings, string recommendation) Analyse(Document doc)
        {
            if (doc == null) return (0, "");
            try
            {
                var scan = ComplianceScan.Scan(doc);
                if (scan == null) return (0, "");

                double containerPct = scan.ContainerCompletePct;
                int warningCount = doc.GetWarnings()?.Count ?? 0;

                if (containerPct >= 95) return (0, "Containers are complete — no container-related warnings expected.");

                // Estimate container-related warnings (data quality warnings often correlate with missing containers)
                int estimatedContainerWarnings = 0;
                var warnings = doc.GetWarnings();
                if (warnings != null)
                {
                    foreach (var w in warnings)
                    {
                        string desc = w.GetDescriptionText()?.ToLowerInvariant() ?? "";
                        if (desc.Contains("parameter") || desc.Contains("schedule") || desc.Contains("formula"))
                            estimatedContainerWarnings++;
                    }
                }

                string rec = containerPct < 80
                    ? $"Container completeness is {containerPct:F0}%. Run 'Combine Parameters' to populate {100 - containerPct:F0}% missing containers. This may resolve ~{estimatedContainerWarnings} data-quality warnings."
                    : $"Container completeness is {containerPct:F0}%. Minor gaps exist.";

                return (estimatedContainerWarnings, rec);
            }
            catch (Exception ex) { StingLog.Warn($"ContainerWarningCrossValidator: {ex.Message}"); return (0, ""); }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  CSI-03: TRANSMITTAL GATING — Compliance check before transmittal
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates model compliance before allowing transmittal creation.
    /// Blocks transmittals below configurable threshold.
    /// </summary>
    internal static class TransmittalGate
    {
        /// <summary>CSI-03: Check if model is ready for transmittal.
        /// Returns (canProceed, issues, compliancePct).</summary>
        public static (bool canProceed, List<string> issues, double compliancePct) ValidateForTransmittal(Document doc)
        {
            var issues = new List<string>();
            if (doc == null) { issues.Add("No document open."); return (false, issues, 0); }

            double tagThreshold = TagConfig.GetConfigDouble("TRANSMITTAL_TAG_THRESHOLD", 90);
            double containerThreshold = TagConfig.GetConfigDouble("TRANSMITTAL_CONTAINER_THRESHOLD", 95);

            try
            {
                ComplianceScan.InvalidateCache();
                var scan = ComplianceScan.Scan(doc);
                if (scan == null) { issues.Add("Compliance scan failed."); return (false, issues, 0); }

                if (scan.CompliancePercent < tagThreshold)
                    issues.Add($"Tag compliance {scan.CompliancePercent:F1}% < required {tagThreshold}%");

                if (scan.ContainerCompletePct < containerThreshold)
                    issues.Add($"Container completeness {scan.ContainerCompletePct:F1}% < required {containerThreshold}%");

                if (scan.StaleCount > 0)
                    issues.Add($"{scan.StaleCount} stale elements need re-tagging before transmittal");

                // Check for critical warnings
                int criticalWarnings = 0;
                var warnings = doc.GetWarnings();
                if (warnings != null)
                {
                    foreach (var w in warnings)
                    {
                        string desc = w.GetDescriptionText()?.ToLowerInvariant() ?? "";
                        if (desc.Contains("invalid sketch") || desc.Contains("completely inside"))
                            criticalWarnings++;
                    }
                }
                if (criticalWarnings > 0)
                    issues.Add($"{criticalWarnings} critical geometric warnings should be resolved before transmittal");

                return (issues.Count == 0, issues, scan.CompliancePercent);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TransmittalGate: {ex.Message}");
                issues.Add($"Validation error: {ex.Message}");
                return (false, issues, 0);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  CSI-04: APPROVAL↔CDE STATE MACHINE — Approval workflow for CDE
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Extends approval workflow with CDE state machine integration.
    /// Records approval decisions with timestamps and links to CDE transitions.
    /// </summary>
    internal static class CDEApprovalWorkflow
    {
        /// <summary>An approval request linked to a CDE state transition.</summary>
        internal class CDEApprovalRequest
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
            public string DocumentId { get; set; }
            public string FromState { get; set; }     // Current CDE state
            public string ToState { get; set; }       // Requested CDE state
            public string RequestedBy { get; set; } = Environment.UserName;
            public DateTime RequestedDate { get; set; } = DateTime.Now;
            public List<string> RequiredApprovers { get; set; } = new();
            public Dictionary<string, string> Decisions { get; set; } = new(); // user → APPROVED/REJECTED
            public string Status { get; set; } = "PENDING"; // PENDING, APPROVED, REJECTED
            public string Comment { get; set; }
        }

        /// <summary>CSI-04: Create approval request for CDE state transition.</summary>
        public static CDEApprovalRequest RequestApproval(string documentId, string fromState, string toState)
        {
            var request = new CDEApprovalRequest
            {
                DocumentId = documentId,
                FromState = fromState,
                ToState = toState,
                RequiredApprovers = GetRequiredApprovers(toState),
            };
            StingLog.Info($"CDEApproval: request {request.Id} for {documentId} ({fromState}→{toState}), approvers: {string.Join(",", request.RequiredApprovers)}");
            return request;
        }

        /// <summary>CSI-04: Record an approval decision.</summary>
        public static void RecordDecision(CDEApprovalRequest request, string approver, bool approved, string comment = "")
        {
            if (request == null) return;
            request.Decisions[approver] = approved ? "APPROVED" : "REJECTED";
            request.Comment = comment;

            // Check if all required approvers have decided
            int approvedCount = request.Decisions.Values.Count(v => v == "APPROVED");
            int rejectedCount = request.Decisions.Values.Count(v => v == "REJECTED");

            if (rejectedCount > 0)
                request.Status = "REJECTED";
            else if (approvedCount >= request.RequiredApprovers.Count)
                request.Status = "APPROVED";

            StingLog.Info($"CDEApproval: {request.Id} — {approver} {(approved ? "APPROVED" : "REJECTED")}. Status: {request.Status}");
        }

        /// <summary>Get required approver roles for a target CDE state.</summary>
        private static List<string> GetRequiredApprovers(string targetState)
        {
            return targetState?.ToUpperInvariant() switch
            {
                "SHARED" => new List<string> { "C", "K" },      // Coordinator or Manager
                "PUBLISHED" => new List<string> { "K" },         // Manager only
                "ARCHIVE" => new List<string> { "K", "I" },      // Manager and Information Manager
                _ => new List<string> { "C" },                   // Default: Coordinator
            };
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  EF-02: WARNING CLASSIFICATION CACHE
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Thread-safe classification cache for WarningsEngine.
    /// Avoids redundant regex evaluation for identical warning descriptions.
    /// </summary>
    internal static class WarningClassificationCache
    {
        private static readonly ConcurrentDictionary<string, (WarningCategory cat, WarningSeverity sev, string fix, bool autoFix)> _cache = new();

        /// <summary>EF-02: Get cached classification or compute and cache.</summary>
        public static (WarningCategory cat, WarningSeverity sev, string fix, bool autoFix) GetOrCompute(
            string description, Func<string, (WarningCategory, WarningSeverity, string, bool)> computeFunc)
        {
            if (string.IsNullOrEmpty(description))
                return (WarningCategory.Unknown, WarningSeverity.Info, "Review manually", false);

            return _cache.GetOrAdd(description, desc => computeFunc(desc));
        }

        /// <summary>Clear cache (document close or after warning resolution).</summary>
        public static void Clear() => _cache.Clear();

        /// <summary>Number of cached entries.</summary>
        public static int Count => _cache.Count;
    }

    // ════════════════════════════════════════════════════════════════
    //  EF-03: COMMAND RESOLUTION CACHE
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Lazy-initialized command instance cache for WorkflowEngine.ResolveCommand.
    /// Avoids 150+ case statement evaluation and new instance creation per step.
    /// </summary>
    internal static class CommandResolutionCache
    {
        private static readonly ConcurrentDictionary<string, Lazy<IExternalCommand>> _cache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>EF-03: Get or create a cached command instance.</summary>
        public static IExternalCommand GetOrCreate(string commandTag, Func<string, IExternalCommand> factory)
        {
            if (string.IsNullOrEmpty(commandTag)) return null;
            var lazy = _cache.GetOrAdd(commandTag, tag => new Lazy<IExternalCommand>(() => factory(tag)));
            try { return lazy.Value; }
            catch (Exception ex) { StingLog.Warn($"CommandResolutionCache: '{commandTag}' failed: {ex.Message}"); return null; }
        }

        /// <summary>Clear cache (session reset).</summary>
        public static void Clear() => _cache.Clear();
    }

    // ════════════════════════════════════════════════════════════════
    //  EF-04: MULTI-THREADED DATA ASSEMBLY helper
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Provides parallel data assembly for BIM Coordination Center.
    /// Runs compliance scan, warning scan, issue load, revision load concurrently.
    /// NOTE: Revit API is single-threaded, so only file I/O and analysis
    /// are parallelized; Revit API calls remain on main thread.
    /// </summary>
    internal static class ParallelDataAssembler
    {
        /// <summary>Assembled data from parallel operations.</summary>
        internal class AssembledData
        {
            public JArray Issues { get; set; }
            public JArray Meetings { get; set; }
            public List<string> SLAViolations { get; set; } = new();
            public int SLAViolationCount { get; set; }
        }

        /// <summary>EF-04: Load file-based data in parallel (issues, meetings, SLA checks).
        /// Revit API data (compliance, warnings) must be loaded on main thread.</summary>
        public static AssembledData LoadFileData(string projectDir)
        {
            var result = new AssembledData();
            if (string.IsNullOrEmpty(projectDir)) return result;

            // File I/O can be parallelized
            var issuesTask = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    string path = Path.Combine(projectDir, "_bim_manager", "issues.json");
                    if (File.Exists(path)) return JArray.Parse(File.ReadAllText(path));
                }
                catch (Exception ex) { StingLog.Warn($"ParallelLoad issues: {ex.Message}"); }
                return new JArray();
            });

            var meetingsTask = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    string path = Path.Combine(projectDir, "_bim_manager", "meetings.json");
                    if (File.Exists(path)) return JArray.Parse(File.ReadAllText(path));
                }
                catch (Exception ex) { StingLog.Warn($"ParallelLoad meetings: {ex.Message}"); }
                return new JArray();
            });

            try
            {
                System.Threading.Tasks.Task.WaitAll(issuesTask, meetingsTask);
                result.Issues = issuesTask.Result;
                result.Meetings = meetingsTask.Result;
            }
            catch (Exception ex) { StingLog.Warn($"ParallelDataAssembler: {ex.Message}"); }

            return result;
        }
    }


    // ════════════════════════════════════════════════════════════════
    //  ISO-01: CDE STATE MACHINE ENFORCEMENT IN UI
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Enforces ISO 19650 CDE state transitions with suitability code validation,
    /// timestamp recording, and user notification.
    /// </summary>
    internal static class CDEStateMachine
    {
        /// <summary>Valid one-way CDE state transitions per ISO 19650-2.</summary>
        private static readonly Dictionary<string, List<string>> ValidTransitions = new(StringComparer.OrdinalIgnoreCase)
        {
            ["WIP"] = new() { "SHARED" },
            ["SHARED"] = new() { "PUBLISHED", "WIP" },  // WIP = rework path
            ["PUBLISHED"] = new() { "ARCHIVE" },
            ["ARCHIVE"] = new(),                          // Terminal state
            ["SUPERSEDED"] = new(),
            ["WITHDRAWN"] = new(),
        };

        /// <summary>Required suitability codes per CDE state.</summary>
        private static readonly Dictionary<string, string> RequiredSuitability = new(StringComparer.OrdinalIgnoreCase)
        {
            ["SHARED"] = "S3",      // Suitable for review and comment
            ["PUBLISHED"] = "S4",   // Suitable for stage approval
            ["ARCHIVE"] = "S7",     // Suitable for record/archive
        };

        /// <summary>ISO-01: Validate a CDE state transition.
        /// Returns (valid, reason, requiredSuitability).</summary>
        public static (bool valid, string reason, string requiredSuitability) ValidateTransition(
            string fromState, string toState)
        {
            if (string.IsNullOrEmpty(fromState) || string.IsNullOrEmpty(toState))
                return (false, "Invalid state: empty value", "");

            if (!ValidTransitions.TryGetValue(fromState, out var allowed))
                return (false, $"Unknown source state: {fromState}", "");

            if (!allowed.Contains(toState))
                return (false, $"Invalid transition: {fromState} → {toState}. Allowed: {string.Join(", ", allowed)}", "");

            string suitability = RequiredSuitability.GetValueOrDefault(toState, "");
            return (true, "Valid", suitability);
        }

        /// <summary>ISO-01: Record a state transition with timestamp and user.</summary>
        public static JObject RecordTransition(string documentId, string fromState, string toState,
            string suitabilityCode, string user = null)
        {
            return new JObject
            {
                ["document_id"] = documentId,
                ["from_state"] = fromState,
                ["to_state"] = toState,
                ["suitability_code"] = suitabilityCode,
                ["timestamp"] = DateTime.Now.ToString("o"),
                ["user"] = user ?? Environment.UserName,
            };
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  ISO-02: APPROVAL HIERARCHY — Multi-party approval chains
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Defines approval hierarchies and delegation chains per ISO 19650.
    /// Supports multi-party approval (requires N of M approvers).
    /// </summary>
    internal static class ApprovalHierarchy
    {
        /// <summary>An approval chain definition.</summary>
        internal class ApprovalChain
        {
            public string ActionTag { get; set; }
            public List<string> PrimaryApprovers { get; set; } = new();  // Role codes
            public List<string> DelegateApprovers { get; set; } = new(); // Fallback roles
            public int MinApprovers { get; set; } = 1;                   // Minimum approvals needed
            public bool VetoEnabled { get; set; }                         // Any rejection = veto
        }

        /// <summary>Built-in approval chains per ISO 19650.</summary>
        private static readonly Dictionary<string, ApprovalChain> _chains = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CDEPublish"] = new ApprovalChain
            {
                ActionTag = "CDEPublish",
                PrimaryApprovers = new() { "K" },               // BIM Manager
                DelegateApprovers = new() { "I", "C" },         // Info Manager, Coordinator
                MinApprovers = 1,
                VetoEnabled = true,
            },
            ["CDEArchive"] = new ApprovalChain
            {
                ActionTag = "CDEArchive",
                PrimaryApprovers = new() { "K", "I" },          // Both required
                DelegateApprovers = new(),
                MinApprovers = 2,
                VetoEnabled = true,
            },
            ["TransmittalSend"] = new ApprovalChain
            {
                ActionTag = "TransmittalSend",
                PrimaryApprovers = new() { "K", "C" },
                DelegateApprovers = new() { "I" },
                MinApprovers = 1,
                VetoEnabled = false,
            },
            ["RevisionIssue"] = new ApprovalChain
            {
                ActionTag = "RevisionIssue",
                PrimaryApprovers = new() { "K" },
                DelegateApprovers = new() { "C" },
                MinApprovers = 1,
                VetoEnabled = true,
            },
        };

        /// <summary>ISO-02: Get approval chain for an action.</summary>
        public static ApprovalChain GetChain(string actionTag)
        {
            _chains.TryGetValue(actionTag, out var chain);
            return chain;
        }

        /// <summary>ISO-02: Check if current user can approve an action.</summary>
        public static bool CanUserApprove(string actionTag, string userRole)
        {
            var chain = GetChain(actionTag);
            if (chain == null) return true; // No chain = no approval needed
            return chain.PrimaryApprovers.Contains(userRole) ||
                   chain.DelegateApprovers.Contains(userRole);
        }

        /// <summary>ISO-02: Check if approval requirements are met.</summary>
        public static (bool met, string reason) CheckApprovalStatus(
            string actionTag, Dictionary<string, string> decisions)
        {
            var chain = GetChain(actionTag);
            if (chain == null) return (true, "No approval required");

            int approvedCount = decisions.Values.Count(v => v == "APPROVED");
            int rejectedCount = decisions.Values.Count(v => v == "REJECTED");

            if (chain.VetoEnabled && rejectedCount > 0)
                return (false, $"Vetoed: {rejectedCount} rejection(s)");

            if (approvedCount >= chain.MinApprovers)
                return (true, $"Approved: {approvedCount}/{chain.MinApprovers} required approvals");

            return (false, $"Pending: {approvedCount}/{chain.MinApprovers} approvals ({chain.MinApprovers - approvedCount} more needed)");
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  ISO-03: INFORMATION MATURITY CLASSIFICATION — PAS 1192-2
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tracks Information Maturity (IM) classification per PAS 1192-2 / ISO 19650-2.
    /// S0=task specific, S1=model/library, S2=information container,
    /// S3=issued for coordination, S4=issued for approval, S5=issued for construction.
    /// </summary>
    internal static class InformationMaturityTracker
    {
        /// <summary>IM classification codes with descriptions.</summary>
        public static readonly Dictionary<string, string> IMCodes = new()
        {
            ["S0"] = "Work in Progress — task specific",
            ["S1"] = "Shared — model/library reference",
            ["S2"] = "Shared — information container for coordination",
            ["S3"] = "Issued for Coordination — review and comment",
            ["S4"] = "Issued for Approval — stage gate",
            ["S5"] = "Issued for Construction / Manufacturing",
            ["S6"] = "Issued for As-Built / Record",
            ["S7"] = "Issued for Archive",
        };

        /// <summary>ISO-03: Map CDE state to IM classification.</summary>
        public static string CDEStateToIM(string cdeState)
        {
            return cdeState?.ToUpperInvariant() switch
            {
                "WIP" => "S2",
                "SHARED" => "S3",
                "PUBLISHED" => "S4",
                "ARCHIVE" => "S7",
                _ => "S0",
            };
        }

        /// <summary>ISO-03: Validate IM classification against CDE state.</summary>
        public static (bool valid, string reason) ValidateIMAgainstCDE(string imCode, string cdeState)
        {
            string expectedIM = CDEStateToIM(cdeState);
            if (string.IsNullOrEmpty(imCode))
                return (false, $"No IM classification set. Expected: {expectedIM} for CDE state {cdeState}");
            if (imCode == expectedIM)
                return (true, "IM classification matches CDE state");
            // Allow higher IM than required (e.g., S5 when S4 required)
            int imNum = int.TryParse(imCode.Substring(1), out var n) ? n : -1;
            int expNum = int.TryParse(expectedIM.Substring(1), out var e) ? e : -1;
            if (imNum >= expNum)
                return (true, $"IM {imCode} meets or exceeds required {expectedIM}");
            return (false, $"IM {imCode} is below required {expectedIM} for CDE state {cdeState}");
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  CW-01: MID-DAY COORDINATION WORKFLOW PRESET
    //  CW-03: COST/SCHEDULE IMPACT TOOLTIPS
    //  CW-04: REVIEW PREP WORKFLOW PRESET
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Provides coordinator-specific workflow presets and action impact tooltips.
    /// </summary>
    internal static class CoordinatorWorkflowPresets
    {
        /// <summary>CW-01: Get mid-day coordination checkpoint workflow.</summary>
        public static WorkflowPreset GetMidDayCoordination()
        {
            return new WorkflowPreset
            {
                Name = "MidDayCoordination",
                Description = "Quick coordination check before meetings (2-3 min)",
                Steps = new List<WorkflowStep>
                {
                    new() { CommandTag = "CompletenessDashboard", Label = "Dashboard refresh" },
                    new() { CommandTag = "WarningsDashboard", Label = "Warnings hotspots", Condition = "warning_count:10" },
                    new() { CommandTag = "DiscComplianceReport", Label = "Discipline compliance" },
                    new() { CommandTag = "ExportModelHealth", Label = "Team productivity snapshot", Optional = true },
                    new() { CommandTag = "WeeklyCoordinatorReport", Label = "Quick status report", Optional = true },
                },
            };
        }

        /// <summary>CW-04: Get design review preparation workflow.</summary>
        public static WorkflowPreset GetDesignReviewPrep()
        {
            return new WorkflowPreset
            {
                Name = "DesignReviewPrep",
                Description = "Prepare model for design review meeting (5-10 min)",
                Steps = new List<WorkflowStep>
                {
                    new() { CommandTag = "RetagStale", Label = "Clear stale elements", Condition = "has_stale:1" },
                    new() { CommandTag = "WarningsAutoFix", Label = "Auto-fix safe warnings", Condition = "has_warnings" },
                    new() { CommandTag = "ValidateTags", Label = "Validate ISO 19650 tags" },
                    new() { CommandTag = "SheetNamingCheck", Label = "Sheet naming compliance" },
                    new() { CommandTag = "GenerateBEP", Label = "Update BEP", Optional = true },
                    new() { CommandTag = "WeeklyCoordinatorReport", Label = "Generate HTML report" },
                    new() { CommandTag = "ExportSheetRegister", Label = "Export sheet register" },
                },
            };
        }

        /// <summary>CW-03: Get action impact tooltip describing time/cost/discipline impact.</summary>
        public static string GetActionImpact(string actionTag)
        {
            return actionTag switch
            {
                "RetagStale" => "⏱ ~2 min for 100 elements | Affects: all disciplines with moved elements",
                "BatchTag" => "⏱ ~5 min for 10K elements | 📊 Improves compliance by 10-40%",
                "WarningsAutoFix" => "⏱ ~1 min | Resolves 30-70% of auto-fixable warnings",
                "COBieExport" => "⏱ ~3 min for 10K elements | 📋 Generates 19-sheet COBie workbook",
                "ValidateTags" => "⏱ ~30 sec | 📊 Shows 4-bucket compliance report",
                "GenerateBEP" => "⏱ ~1 min | 📋 Full BEP document with compliance data",
                "AutoSchedule4D" => "⏱ ~2 min | 📊 4D timeline from 32-trade sequence",
                "AutoCost5D" => "⏱ ~2 min | 💰 5D cost estimate from rate database",
                "WeeklyCoordinatorReport" => "⏱ ~30 sec | 📋 Self-contained HTML report",
                "CreateRevision" => "⏱ ~1 min | 🔄 Tags snapshot for revision delta tracking",
                _ => "",
            };
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMANDS — IExternalCommand implementations for Phase 75
    // ════════════════════════════════════════════════════════════════

    /// <summary>Manage workflow schedule triggers.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WorkflowSchedulerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var triggers = WorkflowScheduler.GetTriggers();
            var sb = new StringBuilder();
            sb.AppendLine("Workflow Schedule Triggers");
            sb.AppendLine(new string('═', 50));
            if (triggers.Count == 0)
            {
                sb.AppendLine("No triggers configured.");
                sb.AppendLine("\nTrigger types: OnDocumentOpen, OnComplianceFall, OnSLAViolation, OnWarningThreshold");
            }
            else
            {
                foreach (var t in triggers)
                {
                    string status = t.Enabled ? "✓" : "✗";
                    sb.AppendLine($"  {status} [{t.Type}] → '{t.PresetName}' (threshold: {t.Threshold})");
                    if (t.LastTriggered.HasValue)
                        sb.AppendLine($"      Last triggered: {t.LastTriggered.Value:yyyy-MM-dd HH:mm}");
                }
            }
            sb.AppendLine($"\nTotal triggers: {triggers.Count}");
            TaskDialog.Show("Workflow Scheduler", sb.ToString());
            return Result.Succeeded;
        }
    }

    /// <summary>View warning root-cause analysis.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WarningRootCauseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var report = WarningsEngine.ScanWarnings(ctx.Doc);
            var rootCauses = WarningRootCauseAnalyser.IdentifyRootCauses(report);

            var sb = new StringBuilder();
            sb.AppendLine("Warning Root-Cause Analysis");
            sb.AppendLine(new string('═', 60));

            if (rootCauses.Count == 0)
            {
                sb.AppendLine("No root causes identified (0 warnings or insufficient data).");
            }
            else
            {
                sb.AppendLine($"Identified {rootCauses.Count} root causes from {report.Total} warnings:\n");
                int rank = 0;
                foreach (var rc in rootCauses.Take(10))
                {
                    rank++;
                    sb.AppendLine($"  #{rank} [Impact: {rc.ImpactScore}/100] [{rc.Severity}] {rc.Category}");
                    sb.AppendLine($"     {rc.Description}");
                    sb.AppendLine($"     → {rc.DownstreamWarningCount} warnings, {rc.SourceElements.Count} elements");
                    sb.AppendLine($"     Fix: {rc.FixRecommendation}");
                    sb.AppendLine();
                }
            }

            TaskDialog.Show("Root-Cause Analysis", sb.ToString());
            return Result.Succeeded;
        }
    }

    /// <summary>View warning suppression audit trail.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SuppressionAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            TaskDialog.Show("Suppression Audit", SuppressionManager.GetAuditReport());
            return Result.Succeeded;
        }
    }

    /// <summary>View team activity signals.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TeamActivityCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.Doc != null)
            {
                TeamActivityTracker.ScanWorksharing(ctx.Doc);
                TeamActivityTracker.ScanIssues(ctx.Doc);
            }

            var activities = TeamActivityTracker.GetRecent(60);
            var sb = new StringBuilder();
            sb.AppendLine("Team Activity (last 60 minutes)");
            sb.AppendLine(new string('═', 60));

            if (activities.Count == 0)
            {
                sb.AppendLine("No recent team activity detected.");
            }
            else
            {
                foreach (var a in activities.Take(30))
                {
                    sb.AppendLine($"  {a.Timestamp:HH:mm} | {a.UserName,-15} | {a.Action} — {a.Detail}");
                }
                if (activities.Count > 30)
                    sb.AppendLine($"\n  ... and {activities.Count - 30} more activities");
            }

            TaskDialog.Show("Team Activity", sb.ToString());
            return Result.Succeeded;
        }
    }

    /// <summary>View compliance improvement trends.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ComplianceTrendViewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var trends = ComplianceImprovementTracker.GetDisciplineTrends();
            var sb = new StringBuilder();
            sb.AppendLine("Compliance Improvement Trends (7-day)");
            sb.AppendLine(new string('═', 50));

            if (trends.Count == 0)
            {
                sb.AppendLine("Insufficient data — need at least 2 data points over 7 days.");
                sb.AppendLine("Run tagging or workflow operations to generate data points.");
            }
            else
            {
                foreach (var kvp in trends)
                    sb.AppendLine($"  {kvp.Key,-12} {kvp.Value}");
            }

            sb.AppendLine($"\nBottleneck: {ComplianceImprovementTracker.IdentifyBottleneck()}");
            sb.AppendLine($"ETA to 95%: {ComplianceImprovementTracker.EstimateDaysToTarget(95)}");

            TaskDialog.Show("Compliance Trends", sb.ToString());
            return Result.Succeeded;
        }
    }

    /// <summary>Run mid-day coordination workflow.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MidDayCoordinationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var preset = CoordinatorWorkflowPresets.GetMidDayCoordination();
            return WorkflowEngine.ExecutePreset(preset, commandData, elements);
        }
    }

    /// <summary>Run design review preparation workflow.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DesignReviewPrepCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var preset = CoordinatorWorkflowPresets.GetDesignReviewPrep();
            return WorkflowEngine.ExecutePreset(preset, commandData, elements);
        }
    }

    /// <summary>View SLA violation report.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SLAViolationReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var (count, details) = SLAMonitor.CheckViolations(ctx.Doc);
            var sb = new StringBuilder();
            sb.AppendLine("SLA Violation Report");
            sb.AppendLine(new string('═', 50));
            sb.AppendLine($"Violations: {count}");
            sb.AppendLine($"Thresholds: CRITICAL=4h, HIGH=24h, MEDIUM=1wk, LOW=2wk\n");

            if (count == 0)
                sb.AppendLine("✓ All issues within SLA thresholds.");
            else
            {
                foreach (var d in details)
                    sb.AppendLine($"  ⚠ {d}");
            }

            TaskDialog.Show("SLA Report", sb.ToString());
            return Result.Succeeded;
        }
    }

} // namespace StingTools.Core
