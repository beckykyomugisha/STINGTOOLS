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

    public class WorkflowStep
    {
        [JsonProperty("commandTag")]
        public string CommandTag { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("optional")]
        public bool Optional { get; set; }

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

        /// <summary>Skip unless at least this many elements carry a tag (complete or not).
        /// Evaluated by WorkflowStepGates.</summary>
        [JsonProperty("requiresMinElements")]
        public int? RequiresMinElements { get; set; }

        /// <summary>Skip unless the project has at least this many open issues.</summary>
        [JsonProperty("requiresIssueCount")]
        public int? RequiresIssueCount { get; set; }

        /// <summary>Skip unless PRJ_ORG_PHASE_TXT is one of these codes ("DE" or "DE,CO").
        /// A project with no phase set skips the step.</summary>
        [JsonProperty("requiresPhase")]
        public string RequiresPhase { get; set; }

        /// <summary>Run only on these days ("Mon,Fri" or "Monday"). An unreadable day skips.</summary>
        [JsonProperty("onWeekday")]
        public string OnWeekday { get; set; }

        /// <summary>Run only at or after this local time, 24-hour "HH:mm".</summary>
        [JsonProperty("afterTime")]
        public string AfterTime { get; set; }

        /// <summary>Run only before this local time, 24-hour "HH:mm". With afterTime later
        /// than beforeTime the window runs overnight (afterTime 22:00, beforeTime 06:00).</summary>
        [JsonProperty("beforeTime")]
        public string BeforeTime { get; set; }

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

        /// <summary>
        /// Named inputs for a command that would otherwise ask for them in a dialog —
        /// e.g. {"drawingTypes": "elec-power-A1-1to100,mep-hvac-duct-A1-1to100",
        /// "output": "Views and sheets"}. The engine hands them to the step through
        /// StingCommandHandler's extra-param store for the step's duration only (see
        /// WorkflowEngine.StepParam); a command reads them only when
        /// WorkflowEngine.IsRunningPreset. Keys a command does not read are ignored
        /// by it, so a command that takes inputs lists them in its own header.
        /// </summary>
        [JsonProperty("params")]
        public Dictionary<string, string> Params { get; set; }
    }
}
