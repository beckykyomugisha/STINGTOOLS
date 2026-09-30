// ═════════════════════════════════════════════════════════════════════════════
//  WorkflowPresetModel.cs — what a preset and a step ARE, with nothing that
//  needs Revit to say it.
//
//  Both classes are pure Newtonsoft POCOs and always were; they lived in
//  WorkflowEngine.cs, which imports the Revit API for the engine around them,
//  and that import was the only reason the SHAPE of a preset could not be
//  asserted outside Revit. Moving them changes no behaviour and no call site —
//  same namespace, same JSON property names, same defaults — and it lets
//  WorkflowPresetOverride merge a project's presets over the corporate ones in a
//  test rather than only in a Revit session.
//
//  KEEP THIS FILE REVIT-FREE. A `using Autodesk.Revit.DB` here would silently
//  undo the only thing the split buys.
//
//  The [JsonProperty] names below are also the contract Tier 5 of
//  tools/check_workflow_wiring.ps1 derives from source: a key a preset writes
//  that is not bound here is read by nothing, and Newtonsoft drops it without a
//  word. Adding a property to make a preset step "work" is the defect that tier
//  exists to catch — make the engine act on it, or delete the key.
// ═════════════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace StingTools.Core
{
    public class WorkflowPreset
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("steps")]
        public List<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();

        /// <summary>LOG-06: When true, wraps all steps in a TransactionGroup and
        /// rolls back all changes if any non-optional step fails.</summary>
        [JsonProperty("rollback_on_failure")]
        public bool RollbackOnFailure { get; set; }

        /// <summary>GAP-06: When true, rolls back ALL changes if ANY step fails (including optional steps).
        /// Use for strict quality gates where partial results are unacceptable.</summary>
        [JsonProperty("rollback_on_optional_failure")]
        public bool RollbackOnOptionalFailure { get; set; }

        [JsonIgnore]
        public bool IsBuiltIn { get; set; }
    }

    /// <summary>The one rule for "an upstream step failed - does this step still run?" (R1).
    /// Revit-free so it is tested; WorkflowEngine calls it for every step.</summary>
    public static class WorkflowStepGate
    {
        /// <summary>True when a group before <paramref name="currentGroup"/> failed, no group
        /// from it up to this one succeeded, and the step does not run after failures.</summary>
        public static bool IsBlocked(WorkflowStep step, int currentGroup,
            ICollection<int> failedGroups, ICollection<int> succeededGroups, out int failedGroup)
        {
            failedGroup = 0;
            if (step == null || failedGroups == null || failedGroups.Count == 0) return false;
            var earlier = failedGroups.Where(g => g < currentGroup).ToList();
            if (earlier.Count == 0) return false;
            failedGroup = earlier.Max();
            int lastFailed = failedGroup;
            bool recovered = succeededGroups != null && succeededGroups.Any(g => g >= lastFailed && g < currentGroup);
            return !recovered && !step.RunsAfterUpstreamFailure;
        }
    }

    public class WorkflowStep
    {
        [JsonProperty("commandTag")]
        public string CommandTag { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("optional")]
        public bool Optional { get; set; }

        /// <summary>An optional step that RAN and returned Failed counts as a failure of the
        /// run, not as a skip. "Optional" then means only "may be skipped when it does not
        /// apply": a command that is not configured returns Cancelled, which still counts as
        /// skipped. Without this, a failed ACC clash pull (an expired token, a wrong
        /// container) reached the run summary as "skipped" and the coordination cycle read
        /// as clean although nothing had been checked.</summary>
        [JsonProperty("failOnError")]
        public bool FailOnError { get; set; }

        /// <summary>True when a Failed result from this step may be counted as a skip.</summary>
        [JsonIgnore]
        public bool ToleratesFailure => Optional && !FailOnError;

        /// <summary>Whether this step still runs after an earlier step FAILED (and nothing since
        /// succeeded). Unset: only a failure-tolerant optional step does (a report, a read-only
        /// check). A failOnError step matters to the run, so it is blocked like a required step:
        /// the KUT fortnightly issue must not package and upload to ACC after its revision gate
        /// failed (R1). Set true only for a step that is safe after any upstream failure.</summary>
        [JsonProperty("runAfterFailure")]
        public bool? RunAfterFailure { get; set; }

        [JsonIgnore]
        public bool RunsAfterUpstreamFailure => RunAfterFailure ?? ToleratesFailure;

        [JsonProperty("condition")]
        public string Condition { get; set; }

        /// <summary>F2: Skip step if current compliance % exceeds this threshold.</summary>
        [JsonProperty("maxCompliancePct")]
        public int? MaxCompliancePct { get; set; }

        /// <summary>F2: Skip step if current compliance % is below this threshold.</summary>
        [JsonProperty("minCompliancePct")]
        public int? MinCompliancePct { get; set; }

        /// <summary>F2: Skip step if no elements have the STALE flag set.</summary>
        [JsonProperty("requiresStaleElements")]
        public bool RequiresStaleElements { get; set; }

        /// <summary>AE-01: Number of retry attempts for transient failures (max 3).</summary>
        [JsonProperty("retryCount")]
        public int RetryCount { get; set; } = 0;

        /// <summary>AE-01: Delay in milliseconds between retries.</summary>
        [JsonProperty("retryDelayMs")]
        public int RetryDelayMs { get; set; } = 500;

        /// <summary>AE-05: Skip step if data files haven't changed since last run.</summary>
        [JsonProperty("skipIfDataUnchanged")]
        public bool SkipIfDataUnchanged { get; set; }

        /// <summary>Phase 39: Skip step if model is not workshared.</summary>
        [JsonProperty("requiresWorksharedModel")]
        public bool RequiresWorksharedModel { get; set; }

        /// <summary>Phase 39: Skip step if total element count is outside range [min, max].</summary>
        [JsonProperty("minElementCount")]
        public int? MinElementCount { get; set; }

        /// <summary>Phase 39: Maximum element count for step applicability.</summary>
        [JsonProperty("maxElementCount")]
        public int? MaxElementCount { get; set; }

        /// <summary>Phase 39: Timeout in seconds for this step (default 300 = 5 min).</summary>
        [JsonProperty("timeoutSeconds")]
        public int TimeoutSeconds { get; set; } = 300;

        /// <summary>Phase 48: Skip step if the previous step was skipped.</summary>
        [JsonProperty("skipIfPreviousSkipped")]
        public bool SkipIfPreviousSkipped { get; set; }

        /// <summary>Phase 48: Skip step if warning health score is above this threshold.</summary>
        [JsonProperty("minWarningHealthScore")]
        public int? MinWarningHealthScore { get; set; }

        /// <summary>Phase 69: Fallback command if this step fails.</summary>
        [JsonProperty("fallbackStep")]
        public string FallbackStep { get; set; }

        /// <summary>Phase 69: Condition logic for multiple conditions: "AND" (all must pass) or "OR" (any must pass).</summary>
        [JsonProperty("conditionLogic")]
        public string ConditionLogic { get; set; } = "AND";

        /// <summary>Phase 69: Array of condition keys for compound condition evaluation.</summary>
        [JsonProperty("conditions")]
        public List<string> Conditions { get; set; }

        /// <summary>Phase 69: Parallel execution group. Steps with same group number run concurrently.</summary>
        [JsonProperty("parallelGroup")]
        public int? ParallelGroup { get; set; }

        /// <summary>Phase 69: Minimum data drop level required (1-4). Skip if current DD is below.</summary>
        [JsonProperty("minDataDrop")]
        public int? MinDataDrop { get; set; }
    }
}
