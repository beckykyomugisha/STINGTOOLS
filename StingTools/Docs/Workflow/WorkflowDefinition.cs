using System;
// WorkflowDefinition.cs — template engine v1.1 (S15).
//
// POCOs describing a workflow definition (states, transitions, SLAs,
// escalations). Loaded from JSON files under _BIM_COORD/workflows/ by
// WorkflowRegistry.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Planscape.Docs.Workflow
{
    public class WorkflowDefinition
    {
        [JsonProperty("id")]          public string Id { get; set; }
        [JsonProperty("name")]        public string Name { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("start_state")] public string StartState { get; set; }
        [JsonProperty("states")]      public List<WorkflowState> States { get; set; } = new List<WorkflowState>();
        [JsonProperty("transitions")] public List<WorkflowTransition> Transitions { get; set; } = new List<WorkflowTransition>();
    }

    public class WorkflowState
    {
        [JsonProperty("name")]          public string Name { get; set; }
        [JsonProperty("sla_hours")]     public int? SlaHours { get; set; }
        [JsonProperty("escalations")]   public List<WorkflowEscalation> Escalations { get; set; } = new List<WorkflowEscalation>();
        [JsonProperty("entry_actions")] public List<string> EntryActions { get; set; } = new List<string>();
        [JsonProperty("exit_actions")]  public List<string> ExitActions  { get; set; } = new List<string>();
        [JsonProperty("is_terminal")]   public bool IsTerminal { get; set; }
    }

    public class WorkflowTransition
    {
        [JsonProperty("from")]          public string From { get; set; }
        [JsonProperty("to")]            public string To { get; set; }
        [JsonProperty("action")]        public string Action { get; set; }   // "submit"|"approve"|"reject"
        [JsonProperty("allowed_roles")] public List<string> AllowedRoles { get; set; } = new List<string>();
        [JsonProperty("condition")]     public string Condition { get; set; }
    }

    public class WorkflowEscalation
    {
        [JsonProperty("after_hours")] public int AfterHours { get; set; }
        [JsonProperty("action")]      public string Action { get; set; }   // "remind"|"notify"|"reroute"|"auto_approve"
        [JsonProperty("to")]          public string To { get; set; }
    }

    /// <summary>Runtime instance state persisted to _BIM_COORD/workflow_state.json.</summary>
    public class WorkflowInstance
    {
        [JsonProperty("id")]            public string Id { get; set; }
        [JsonProperty("workflow_id")]   public string WorkflowId { get; set; }
        [JsonProperty("doc_id")]        public string DocId { get; set; }
        [JsonProperty("state")]         public string State { get; set; }
        [JsonProperty("assigned_to")]   public string AssignedTo { get; set; }
        [JsonProperty("started_at")]    public string StartedAt { get; set; }
        [JsonProperty("state_entered_at")] public string StateEnteredAt { get; set; }
        [JsonProperty("sla_deadline")]  public string SlaDeadline { get; set; }
        [JsonProperty("history")]       public List<WorkflowHistoryRow> History { get; set; } = new List<WorkflowHistoryRow>();
        [JsonProperty("closed")]        public bool Closed { get; set; }
    }

    public class WorkflowHistoryRow
    {
        [JsonProperty("ts")]         public string Ts { get; set; }
        [JsonProperty("from_state")] public string FromState { get; set; }
        [JsonProperty("to_state")]   public string ToState { get; set; }
        [JsonProperty("action")]     public string Action { get; set; }
        [JsonProperty("user")]       public string User { get; set; }
        [JsonProperty("comment")]    public string Comment { get; set; }
    }

    public class SlaBreach
    {
        public string InstanceId { get; set; }
        public string DocId { get; set; }
        public string State { get; set; }
        public int OverdueByHours { get; set; }
        public WorkflowEscalation NextEscalation { get; set; }
    }
}
